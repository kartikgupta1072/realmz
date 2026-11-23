"""Local GPU (ComfyUI Z-Image-Turbo / Illustrious-XL) and reference-image seamless PBR terrain generator."""

from __future__ import annotations

import gc
import io
import json
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

from .config import (
    COMFY_MAIN,
    COMFY_PYTHON,
    COMFY_ROOT,
    COMFY_URL_DEFAULT,
    COMFY_XL_CKPT,
    COMFY_ZIT_CLIP,
    COMFY_ZIT_UNET,
    COMFY_ZIT_VAE,
    CREATE_NO_WINDOW,
    JOBS_DIR,
    TERRAIN_DEFAULT_RES,
    TERRAIN_DEFAULT_STYLE,
    TERRAIN_DEFAULT_TILE_SCALE_M,
    VALID_TERRAIN_ENGINES,
    query_gpu_vram,
    sanitize_ascii,
    sanitize_asset_id,
)
from .img2mesh import VramMonitor
from .verify_terrain_material import verify_terrain_bundle

_MATERIAL_PROFILES: dict[str, dict[str, Any]] = {
    "dungeon": {
        "keywords": ("dungeon", "flagstone", "crypt", "vault", "catacomb", "temple", "slate", "wall"),
        "rough_min": 0.42,
        "rough_max": 0.60,
        "crevice_rough": 0.84,
        "normal_strength": 3.2,
        "crevice_weight": 0.55,
    },
    "cobble": {
        "keywords": ("cobble", "cobblestone", "stone", "paving", "brick", "masonry", "rock", "ruin", "plaza"),
        "rough_min": 0.50,
        "rough_max": 0.66,
        "crevice_rough": 0.88,
        "normal_strength": 3.0,
        "crevice_weight": 0.50,
    },
    "mud": {
        "keywords": ("mud", "riverbank", "swamp", "marsh", "silt", "bog", "wet", "puddle", "mire"),
        "rough_min": 0.35,
        "rough_max": 0.65,
        "crevice_rough": 0.36,
        "normal_strength": 2.4,
        "crevice_weight": 0.35,
    },
    "grass": {
        "keywords": ("grass", "meadow", "lawn", "turf", "moss", "clover", "pasture", "forest", "field"),
        "rough_min": 0.72,
        "rough_max": 0.86,
        "crevice_rough": 0.88,
        "normal_strength": 2.5,
        "crevice_weight": 0.30,
    },
    "dirt": {
        "keywords": ("dirt", "trail", "path", "sand", "gravel", "earth", "soil", "dust", "road"),
        "rough_min": 0.68,
        "rough_max": 0.88,
        "crevice_rough": 0.90,
        "normal_strength": 2.6,
        "crevice_weight": 0.38,
    },
    "generic": {
        "keywords": (),
        "rough_min": 0.58,
        "rough_max": 0.82,
        "crevice_rough": 0.86,
        "normal_strength": 2.6,
        "crevice_weight": 0.40,
    },
}


def _iso_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def infer_material_profile(name_or_id: str, prompt_text: str = "") -> tuple[str, dict[str, Any]]:
    haystack = f"{name_or_id} {prompt_text}".lower()
    for profile_name in ("dungeon", "cobble", "mud", "grass", "dirt"):
        spec = _MATERIAL_PROFILES[profile_name]
        if any(kw in haystack for kw in spec["keywords"]):
            return profile_name, spec
    return "generic", _MATERIAL_PROFILES["generic"]


def _http_json(url: str, method: str = "GET", payload: dict[str, Any] | None = None, timeout: float = 5.0) -> Any:
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        raw = resp.read().decode("utf-8", errors="replace")
        return json.loads(raw) if raw.strip() else {}


def detect_comfy_url() -> str | None:
    candidates = [COMFY_URL_DEFAULT, "http://127.0.0.1:8188", "http://127.0.0.1:8189"]
    seen: set[str] = set()
    for url in candidates:
        clean = url.rstrip("/")
        if clean in seen:
            continue
        seen.add(clean)
        try:
            _http_json(f"{clean}/system_stats", timeout=1.5)
            return clean
        except Exception:
            continue
    return None


_SPAWNED_COMFY_PROC: subprocess.Popen[bytes] | None = None


def ensure_comfy_running() -> tuple[str, subprocess.Popen[bytes] | None]:
    """Ensure headless ComfyUI is listening on 127.0.0.1:8188. Returns (url, spawned_proc_or_none)."""
    global _SPAWNED_COMFY_PROC
    existing = detect_comfy_url()
    if existing:
        return existing, None

    if not COMFY_PYTHON.is_file() or not COMFY_MAIN.is_file():
        raise RuntimeError(f"ComfyUI installation not found at {COMFY_ROOT}")

    parsed = urllib.parse.urlparse(COMFY_URL_DEFAULT)
    port = str(parsed.port or 8188)
    host = parsed.hostname or "127.0.0.1"
    log_path = COMFY_ROOT / "realmz_foundry_comfy.log"

    log_fh = open(log_path, "ab")
    try:
        proc = subprocess.Popen(
            [
                str(COMFY_PYTHON),
                str(COMFY_MAIN),
                "--listen",
                host,
                "--port",
                port,
                "--disable-auto-launch",
                "--reserve-vram",
                "0.6",
            ],
            cwd=str(COMFY_ROOT),
            stdin=subprocess.DEVNULL,
            stdout=log_fh,
            stderr=subprocess.STDOUT,
            creationflags=CREATE_NO_WINDOW,
        )
    finally:
        log_fh.close()

    deadline = time.time() + 90.0
    target_url = f"http://{host}:{port}"
    while time.time() < deadline:
        if proc.poll() is not None:
            raise RuntimeError(f"Headless ComfyUI exited unexpectedly (code={proc.returncode}).")
        try:
            _http_json(f"{target_url}/system_stats", timeout=1.5)
            _SPAWNED_COMFY_PROC = proc
            return target_url, proc
        except Exception:
            time.sleep(1.0)

    proc.terminate()
    raise RuntimeError("Timed out waiting for headless ComfyUI (:8188) startup.")


def comfy_free_vram(comfy_url: str) -> bool:
    try:
        _http_json(
            f"{comfy_url}/free",
            method="POST",
            payload={"unload_models": True, "free_memory": True},
            timeout=8.0,
        )
        return True
    except Exception:
        return False


def teardown_comfy(
    comfy_url: str | None,
    spawned_proc: subprocess.Popen[bytes] | None,
    keep_comfy: bool = False,
) -> None:
    global _SPAWNED_COMFY_PROC
    if keep_comfy:
        return
    if comfy_url:
        comfy_free_vram(comfy_url)
    proc_to_stop = spawned_proc or _SPAWNED_COMFY_PROC
    if proc_to_stop is not None and proc_to_stop.poll() is None:
        try:
            proc_to_stop.terminate()
            proc_to_stop.wait(timeout=10)
        except Exception:
            try:
                proc_to_stop.kill()
                proc_to_stop.wait(timeout=5)
            except Exception:
                pass
    if proc_to_stop is _SPAWNED_COMFY_PROC:
        _SPAWNED_COMFY_PROC = None
    gc.collect()
    deadline = time.time() + 3.0
    while time.time() < deadline:
        if query_gpu_vram().get("used_mib", 0) < 1000:
            break
        time.sleep(0.4)


def comfy_upload_image(comfy_url: str, image_path: Path, remote_name: str | None = None) -> str:
    """Upload a PNG image to ComfyUI /upload/image via stdlib multipart/form-data."""
    boundary = f"----RealmzBoundary{uuid.uuid4().hex}"
    fname = remote_name or image_path.name
    img_bytes = image_path.read_bytes()

    parts: list[bytes] = []
    for field_name, field_val in (("overwrite", "true"), ("type", "input")):
        parts.append(
            (
                f"--{boundary}\r\n"
                f'Content-Disposition: form-data; name="{field_name}"\r\n\r\n'
                f"{field_val}\r\n"
            ).encode("utf-8")
        )
    parts.append(
        (
            f"--{boundary}\r\n"
            f'Content-Disposition: form-data; name="image"; filename="{fname}"\r\n'
            f"Content-Type: image/png\r\n\r\n"
        ).encode("utf-8")
    )
    parts.append(img_bytes)
    parts.append(f"\r\n--{boundary}--\r\n".encode("utf-8"))
    body = b"".join(parts)

    req = urllib.request.Request(
        f"{comfy_url}/upload/image",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=20.0) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return str(data.get("name") or fname)


def comfy_download_image(
    comfy_url: str, filename: str, subfolder: str = "", folder_type: str = "output"
) -> Image.Image:
    query = urllib.parse.urlencode({"filename": filename, "subfolder": subfolder, "type": folder_type})
    with urllib.request.urlopen(f"{comfy_url}/view?{query}", timeout=30.0) as resp:
        raw = resp.read()
    return Image.open(io.BytesIO(raw)).convert("RGB")


def comfy_run_workflow(
    comfy_url: str,
    workflow: dict[str, Any],
    save_node_id: str,
    timeout_sec: float = 240.0,
) -> Image.Image:
    resp = _http_json(
        f"{comfy_url}/prompt",
        method="POST",
        payload={"prompt": workflow},
        timeout=15.0,
    )
    prompt_id = resp.get("prompt_id")
    if not prompt_id:
        raise RuntimeError(f"ComfyUI rejected workflow: {resp}")

    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        hist = _http_json(f"{comfy_url}/history/{prompt_id}", timeout=5.0)
        if prompt_id in hist:
            entry = hist[prompt_id]
            status_info = entry.get("status", {})
            if status_info.get("status_str") == "error":
                raise RuntimeError(f"ComfyUI execution error: {status_info}")
            outputs = entry.get("outputs", {})
            images = outputs.get(save_node_id, {}).get("images", [])
            if images:
                info = images[0]
                return comfy_download_image(
                    comfy_url,
                    filename=str(info["filename"]),
                    subfolder=str(info.get("subfolder", "")),
                    folder_type=str(info.get("type", "output")),
                )
            if status_info.get("completed"):
                raise RuntimeError("ComfyUI finished without producing an output image.")
        time.sleep(0.5)

    raise RuntimeError(f"Timed out waiting for ComfyUI prompt {prompt_id}")


def build_terrain_prompt(base_prompt: str, style: str, prompt_hint: str = "") -> tuple[str, str]:
    """Build DOS2/BG3-inspired painterly PBR daylight tabletop positive and negative cues."""
    parts = [base_prompt.strip()]
    if prompt_hint.strip():
        parts.append(prompt_hint.strip())

    style_clean = style.strip().lower()
    if style_clean == "painterly-pbr":
        style_desc = (
            "Divinity Original Sin 2 and Baldur's Gate 3 hand-painted stylized PBR game terrain material, "
            "crisp sculpted surface micro-detail, tactile organic variation, natural daylight albedo"
        )
    else:
        style_desc = f"{style.strip()} stylized fantasy tabletop PBR terrain texture, crisp surface detail"

    positive = sanitize_ascii(
        ", ".join(
            [
                p
                for p in parts
                if p
            ]
            + [
                style_desc,
                "strictly 90-degree top-down orthographic overhead view",
                "edge-to-edge uniform flat surface",
                "even diffuse ambient lighting with zero directional cast shadows",
                "no perspective, no horizon, no sky, no vignette, no borders, no character, seamless tileable ground",
            ]
        )
    )
    negative = sanitize_ascii(
        "directional shadows, harsh sunlight, oblique angle, perspective, horizon, sky, "
        "vignette, border, frame, blurry, muddy dark blotches, crushed black, blown white, text, watermark"
    )
    return positive, negative


def build_zit_terrain_pass1(prompt_text: str, seed: int, steps: int = 8) -> dict[str, Any]:
    return {
        "1": {
            "class_type": "UNETLoader",
            "inputs": {"unet_name": COMFY_ZIT_UNET.name, "weight_dtype": "default"},
        },
        "2": {
            "class_type": "CLIPLoader",
            "inputs": {"clip_name": COMFY_ZIT_CLIP, "type": "lumina2", "device": "default"},
        },
        "3": {
            "class_type": "VAELoader",
            "inputs": {"vae_name": COMFY_ZIT_VAE},
        },
        "5": {
            "class_type": "ModelSamplingAuraFlow",
            "inputs": {"model": ["1", 0], "shift": 3.0},
        },
        "6": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["2", 0], "text": prompt_text},
        },
        "7": {
            "class_type": "ConditioningZeroOut",
            "inputs": {"conditioning": ["6", 0]},
        },
        "8": {
            "class_type": "EmptySD3LatentImage",
            "inputs": {"width": 1024, "height": 1024, "batch_size": 1},
        },
        "9": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["5", 0],
                "positive": ["6", 0],
                "negative": ["7", 0],
                "latent_image": ["8", 0],
                "seed": int(seed),
                "steps": int(steps),
                "cfg": 1.0,
                "sampler_name": "euler",
                "scheduler": "simple",
                "denoise": 1.0,
            },
        },
        "10": {
            "class_type": "VAEDecode",
            "inputs": {"samples": ["9", 0], "vae": ["3", 0]},
        },
        "11": {
            "class_type": "SaveImage",
            "inputs": {"images": ["10", 0], "filename_prefix": "RealmzTerrain_ZIT_P1"},
        },
    }


def build_zit_terrain_pass2_inpaint(
    prompt_text: str,
    rolled_image_name: str,
    mask_image_name: str,
    seed: int,
    steps: int = 8,
    denoise: float = 0.72,
) -> dict[str, Any]:
    return {
        "1": {
            "class_type": "UNETLoader",
            "inputs": {"unet_name": COMFY_ZIT_UNET.name, "weight_dtype": "default"},
        },
        "2": {
            "class_type": "CLIPLoader",
            "inputs": {"clip_name": COMFY_ZIT_CLIP, "type": "lumina2", "device": "default"},
        },
        "3": {
            "class_type": "VAELoader",
            "inputs": {"vae_name": COMFY_ZIT_VAE},
        },
        "4": {
            "class_type": "LoadImage",
            "inputs": {"image": rolled_image_name},
        },
        "5": {
            "class_type": "LoadImageMask",
            "inputs": {"image": mask_image_name, "channel": "red"},
        },
        "6": {
            "class_type": "VAEEncode",
            "inputs": {"pixels": ["4", 0], "vae": ["3", 0]},
        },
        "7": {
            "class_type": "SetLatentNoiseMask",
            "inputs": {"samples": ["6", 0], "mask": ["5", 0]},
        },
        "8": {
            "class_type": "ModelSamplingAuraFlow",
            "inputs": {"model": ["1", 0], "shift": 3.0},
        },
        "9": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["2", 0], "text": prompt_text},
        },
        "10": {
            "class_type": "ConditioningZeroOut",
            "inputs": {"conditioning": ["9", 0]},
        },
        "11": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["8", 0],
                "positive": ["9", 0],
                "negative": ["10", 0],
                "latent_image": ["7", 0],
                "seed": int(seed),
                "steps": int(steps),
                "cfg": 1.0,
                "sampler_name": "euler",
                "scheduler": "simple",
                "denoise": float(denoise),
            },
        },
        "12": {
            "class_type": "VAEDecode",
            "inputs": {"samples": ["11", 0], "vae": ["3", 0]},
        },
        "13": {
            "class_type": "SaveImage",
            "inputs": {"images": ["12", 0], "filename_prefix": "RealmzTerrain_ZIT_P2"},
        },
    }


def build_xl_terrain_pass1(
    positive_prompt: str, negative_prompt: str, seed: int, steps: int = 16
) -> dict[str, Any]:
    xl_steps = max(int(steps), 16)
    return {
        "1": {
            "class_type": "CheckpointLoaderSimple",
            "inputs": {"ckpt_name": COMFY_XL_CKPT.name},
        },
        "2": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["1", 1], "text": positive_prompt},
        },
        "3": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["1", 1], "text": negative_prompt},
        },
        "4": {
            "class_type": "EmptyLatentImage",
            "inputs": {"width": 1024, "height": 1024, "batch_size": 1},
        },
        "5": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["1", 0],
                "positive": ["2", 0],
                "negative": ["3", 0],
                "latent_image": ["4", 0],
                "seed": int(seed),
                "steps": xl_steps,
                "cfg": 6.5,
                "sampler_name": "dpmpp_2m",
                "scheduler": "karras",
                "denoise": 1.0,
            },
        },
        "6": {
            "class_type": "VAEDecode",
            "inputs": {"samples": ["5", 0], "vae": ["1", 2]},
        },
        "7": {
            "class_type": "SaveImage",
            "inputs": {"images": ["6", 0], "filename_prefix": "RealmzTerrain_XL_P1"},
        },
    }


def build_xl_terrain_pass2_inpaint(
    positive_prompt: str,
    negative_prompt: str,
    rolled_image_name: str,
    mask_image_name: str,
    seed: int,
    steps: int = 16,
    denoise: float = 0.72,
) -> dict[str, Any]:
    xl_steps = max(int(steps), 16)
    return {
        "1": {
            "class_type": "CheckpointLoaderSimple",
            "inputs": {"ckpt_name": COMFY_XL_CKPT.name},
        },
        "2": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["1", 1], "text": positive_prompt},
        },
        "3": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["1", 1], "text": negative_prompt},
        },
        "4": {
            "class_type": "LoadImage",
            "inputs": {"image": rolled_image_name},
        },
        "5": {
            "class_type": "LoadImageMask",
            "inputs": {"image": mask_image_name, "channel": "red"},
        },
        "6": {
            "class_type": "VAEEncode",
            "inputs": {"pixels": ["4", 0], "vae": ["1", 2]},
        },
        "7": {
            "class_type": "SetLatentNoiseMask",
            "inputs": {"samples": ["6", 0], "mask": ["5", 0]},
        },
        "8": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["1", 0],
                "positive": ["2", 0],
                "negative": ["3", 0],
                "latent_image": ["7", 0],
                "seed": int(seed),
                "steps": xl_steps,
                "cfg": 6.5,
                "sampler_name": "dpmpp_2m",
                "scheduler": "karras",
                "denoise": float(denoise),
            },
        },
        "9": {
            "class_type": "VAEDecode",
            "inputs": {"samples": ["8", 0], "vae": ["1", 2]},
        },
        "10": {
            "class_type": "SaveImage",
            "inputs": {"images": ["9", 0], "filename_prefix": "RealmzTerrain_XL_P2"},
        },
    }


def _rgb_to_lab_float(rgb_u8: np.ndarray) -> np.ndarray:
    lab = cv2.cvtColor(rgb_u8, cv2.COLOR_RGB2LAB).astype(np.float32)
    lab[..., 0] = lab[..., 0] * (100.0 / 255.0)
    lab[..., 1] = lab[..., 1] - 128.0
    lab[..., 2] = lab[..., 2] - 128.0
    return lab


def _lab_float_to_rgb_u8(lab: np.ndarray) -> np.ndarray:
    cv_lab = np.empty(lab.shape, dtype=np.uint8)
    cv_lab[..., 0] = np.clip(np.round(lab[..., 0] * (255.0 / 100.0)), 0, 255).astype(np.uint8)
    cv_lab[..., 1] = np.clip(np.round(lab[..., 1] + 128.0), 0, 255).astype(np.uint8)
    cv_lab[..., 2] = np.clip(np.round(lab[..., 2] + 128.0), 0, 255).astype(np.uint8)
    return cv2.cvtColor(cv_lab, cv2.COLOR_LAB2RGB)


def _gaussian_blur_wrap(arr: np.ndarray, sigma: float) -> np.ndarray:
    """Fast periodic Gaussian blur via wrap padding + OpenCV separable blur."""
    h, w = arr.shape[:2]
    pad = max(8, min(h // 2, w // 2, int(round(sigma * 3.0))))
    padded = np.pad(arr, ((pad, pad), (pad, pad)), mode="wrap")
    blurred = cv2.GaussianBlur(padded, (0, 0), sigmaX=float(sigma), sigmaY=float(sigma))
    return blurred[pad : pad + h, pad : pad + w]


def delight_albedo_periodic(rgb_u8: np.ndarray, strength: float = 0.85) -> np.ndarray:
    """Flatten low-frequency lighting gradients in CIELAB L* with periodic wrap and lift crushed blacks."""
    lab = _rgb_to_lab_float(rgb_u8)
    l_chan = lab[..., 0]

    # Periodic Gaussian low-frequency gradient subtraction
    sigma_l = max(32.0, min(rgb_u8.shape[0], rgb_u8.shape[1]) * 0.12)
    l_low = _gaussian_blur_wrap(l_chan, sigma_l)
    l_mean = float(np.mean(l_chan))
    l_flat = l_chan - float(strength) * (l_low - l_mean)

    # Gentle chroma gradient balance so opposite tile edges share color temperature
    for ch in (1, 2):
        c_low = _gaussian_blur_wrap(lab[..., ch], max(28.0, sigma_l))
        lab[..., ch] = lab[..., ch] - 0.35 * (c_low - float(np.mean(lab[..., ch])))

    # Lift dead crushed blacks smoothly while preserving rich shadow structure
    crushed = np.clip((18.0 - l_flat) / 18.0, 0.0, 1.0)
    l_flat = l_flat + crushed * 8.5

    # Soft-compress blown highlights above 88
    over = np.maximum(0.0, l_flat - 88.0)
    l_flat = l_flat - over * 0.45

    # Ensure healthy contrast and non-collapsed daylight range
    mean_now = float(np.mean(l_flat))
    if mean_now < 26.0:
        l_flat = l_flat + (32.0 - mean_now)
    elif mean_now > 76.0:
        l_flat = l_flat - (mean_now - 70.0)

    std_now = float(np.std(l_flat))
    if 0.5 < std_now < 6.0:
        m = float(np.mean(l_flat))
        l_flat = m + (l_flat - m) * (7.0 / std_now)

    lab[..., 0] = np.clip(l_flat, 4.0, 95.0)
    return _lab_float_to_rgb_u8(lab)


def _build_cross_seam_mask(
    size: int, plateau_half: int = 48, band_half: int = 140, blur_sigma: float = 16.0
) -> np.ndarray:
    """Create feathered [0..1] float mask with flat 1.0 core cross on a 50% rolled canvas."""
    half = size // 2
    yy, xx = np.ogrid[:size, :size]
    dist_x = np.abs(xx - half).astype(np.float32)
    dist_y = np.abs(yy - half).astype(np.float32)

    span = max(1.0, float(band_half - plateau_half))
    wx = np.clip(1.0 - np.maximum(0.0, dist_x - float(plateau_half)) / span, 0.0, 1.0)
    wy = np.clip(1.0 - np.maximum(0.0, dist_y - float(plateau_half)) / span, 0.0, 1.0)
    wx = 0.5 - 0.5 * np.cos(np.pi * wx)
    wy = 0.5 - 0.5 * np.cos(np.pi * wy)
    cross = np.maximum(wx, wy)
    cross = ndimage.gaussian_filter(cross, sigma=float(blur_sigma), mode="constant", cval=0.0)

    # Re-pin core cross to strictly 1.0 so center seam stays pure foreground across pyramid levels
    core_pin = np.maximum(
        np.clip(1.0 - np.maximum(0.0, dist_x - float(plateau_half * 0.7)) / 12.0, 0.0, 1.0),
        np.clip(1.0 - np.maximum(0.0, dist_y - float(plateau_half * 0.7)) / 12.0, 0.0, 1.0),
    )
    cross = np.maximum(cross, core_pin)

    # Taper strictly to 0 near outer edges so unmasked border remains exact across wrap
    margin = max(32, size // 16)
    edge_x = np.minimum(xx, size - 1 - xx).astype(np.float32)
    edge_y = np.minimum(yy, size - 1 - yy).astype(np.float32)
    taper = np.clip(np.minimum(edge_x, edge_y) / float(margin), 0.0, 1.0)
    mask = np.clip(cross * taper, 0.0, 1.0).astype(np.float32)
    return mask


def _laplacian_pyramid_blend(
    fg_rgb: np.ndarray, bg_rgb: np.ndarray, mask_f: np.ndarray, levels: int = 4, wrap_pad: int = 64
) -> np.ndarray:
    """Periodic multi-band Laplacian pyramid blend between fg (where mask=1) and bg (where mask=0)."""
    h0, w0 = fg_rgb.shape[:2]
    if wrap_pad > 0:
        fg = np.pad(fg_rgb, ((wrap_pad, wrap_pad), (wrap_pad, wrap_pad), (0, 0)), mode="wrap").astype(np.float32)
        bg = np.pad(bg_rgb, ((wrap_pad, wrap_pad), (wrap_pad, wrap_pad), (0, 0)), mode="wrap").astype(np.float32)
        m_pad = np.pad(mask_f, ((wrap_pad, wrap_pad), (wrap_pad, wrap_pad)), mode="wrap")[..., None].astype(np.float32)
    else:
        fg = fg_rgb.astype(np.float32)
        bg = bg_rgb.astype(np.float32)
        m_pad = mask_f[..., None].astype(np.float32)

    gp_fg = [fg]
    gp_bg = [bg]
    gp_m = [m_pad]
    for _ in range(levels):
        gp_fg.append(cv2.pyrDown(gp_fg[-1]))
        gp_bg.append(cv2.pyrDown(gp_bg[-1]))
        m_down = cv2.pyrDown(gp_m[-1])
        if m_down.ndim == 2:
            m_down = m_down[..., None]
        gp_m.append(m_down)

    lp_fg = [gp_fg[-1]]
    lp_bg = [gp_bg[-1]]
    for i in range(levels - 1, -1, -1):
        h, w = gp_fg[i].shape[:2]
        up_fg = cv2.pyrUp(gp_fg[i + 1], dstsize=(w, h))
        up_bg = cv2.pyrUp(gp_bg[i + 1], dstsize=(w, h))
        lp_fg.append(gp_fg[i] - up_fg)
        lp_bg.append(gp_bg[i] - up_bg)

    blended_pyr = []
    for idx in range(len(lp_fg)):
        m_lvl = np.clip(gp_m[levels - idx], 0.0, 1.0)
        if idx == 0:
            # Base low-frequency residual: standard linear blend
            blended_pyr.append(lp_fg[idx] * m_lvl + lp_bg[idx] * (1.0 - m_lvl))
        else:
            # Detail levels: variance-preserving blend so texture grain never softens in transition band
            denom = np.sqrt(m_lvl * m_lvl + (1.0 - m_lvl) * (1.0 - m_lvl) + 1e-6)
            blended_pyr.append((lp_fg[idx] * m_lvl + lp_bg[idx] * (1.0 - m_lvl)) / denom)

    out = blended_pyr[0]
    for i in range(1, len(blended_pyr)):
        h, w = blended_pyr[i].shape[:2]
        out = cv2.pyrUp(out, dstsize=(w, h)) + blended_pyr[i]

    if wrap_pad > 0:
        out = out[wrap_pad : wrap_pad + h0, wrap_pad : wrap_pad + w0]

    return np.clip(np.round(out), 0, 255).astype(np.uint8)


def _build_1d_strip_mask(
    size: int,
    axis: int,
    center: int | None = None,
    plateau_half: int = 96,
    band_half: int = 200,
    blur_sigma: float = 14.0,
) -> np.ndarray:
    """Create a 1D-extruded [0..1] mask centered at `center` (1.0 at strip, 0.0 at outer borders)."""
    c = size // 2 if center is None else int(center)
    coords = np.arange(size, dtype=np.float32)
    dist = np.abs(coords - c)
    span = max(1.0, float(band_half - plateau_half))
    w1d = np.clip(1.0 - np.maximum(0.0, dist - float(plateau_half)) / span, 0.0, 1.0)
    w1d = 0.5 - 0.5 * np.cos(np.pi * w1d)
    w1d = ndimage.gaussian_filter1d(w1d, sigma=float(blur_sigma), mode="constant", cval=0.0)
    core_pin = np.clip(1.0 - np.maximum(0.0, dist - float(plateau_half * 0.75)) / 10.0, 0.0, 1.0)
    w1d = np.maximum(w1d, core_pin)
    margin = max(32, size // 16)
    edge_dist = np.minimum(coords, size - 1 - coords)
    taper = np.clip(edge_dist / float(margin), 0.0, 1.0)
    w1d = np.clip(w1d * taper, 0.0, 1.0).astype(np.float32)
    if axis == 1:
        return np.broadcast_to(w1d[None, :], (size, size)).copy()
    return np.broadcast_to(w1d[:, None], (size, size)).copy()


def _find_min_gradient_cut(rgb: np.ndarray, axis: int) -> int:
    """Find column/row index in central third with lowest cross-line gradient for clean toroidal wrap."""
    size = rgb.shape[axis]
    lo = size // 3
    hi = (2 * size) // 3
    f = rgb.astype(np.float32)
    if axis == 1:
        diffs = np.mean(np.abs(f[:, lo:hi] - f[:, lo - 1 : hi - 1]), axis=(0, 2))
    else:
        diffs = np.mean(np.abs(f[lo:hi, :] - f[lo - 1 : hi - 1, :]), axis=(1, 2))
    return lo + int(np.argmin(diffs))


def make_seamless_toroidal_reference(rgb_1024: np.ndarray) -> np.ndarray:
    """Make a 1024x1024 reference texture seamlessly tileable via sequential X and Y toroidal heals."""
    h, w, _ = rgb_1024.shape
    p_half = max(64, h // 13)
    b_half = max(160, h // 6)
    b_sig = max(10.0, h / 72.0)

    # Pass 1: Heal X wrap using minimum-gradient vertical cut
    cut_x = _find_min_gradient_cut(rgb_1024, axis=1)
    shift_x = w - cut_x
    roll_x = np.roll(rgb_1024, shift=(0, shift_x), axis=(0, 1))
    mask_x = _build_1d_strip_mask(
        w, axis=1, center=shift_x, plateau_half=p_half, band_half=b_half, blur_sigma=b_sig
    )
    healed_x = _laplacian_pyramid_blend(
        fg_rgb=rgb_1024, bg_rgb=roll_x, mask_f=mask_x, levels=4, wrap_pad=64
    )

    # Pass 2: Heal Y wrap using minimum-gradient horizontal cut (X is already periodic everywhere)
    cut_y = _find_min_gradient_cut(healed_x, axis=0)
    shift_y = h - cut_y
    roll_y = np.roll(healed_x, shift=(shift_y, 0), axis=(0, 1))
    mask_y = _build_1d_strip_mask(
        h, axis=0, center=shift_y, plateau_half=p_half, band_half=b_half, blur_sigma=b_sig
    )
    return _laplacian_pyramid_blend(
        fg_rgb=healed_x, bg_rgb=roll_y, mask_f=mask_y, levels=4, wrap_pad=64
    )


def toroidal_downsample(rgb_1024: np.ndarray, target_res: int) -> np.ndarray:
    """Downsample 1024x1024 seamless texture cleanly with periodic wrap padding so edges match exactly."""
    h, w, _ = rgb_1024.shape
    if h == target_res and w == target_res:
        return rgb_1024.copy()

    pad_src = 64
    pad_dst = max(4, int(round(pad_src * (float(target_res) / float(h)))))
    padded = np.pad(rgb_1024, ((pad_src, pad_src), (pad_src, pad_src), (0, 0)), mode="wrap")
    resized = np.array(
        Image.fromarray(padded, mode="RGB").resize(
            (target_res + 2 * pad_dst, target_res + 2 * pad_dst),
            Image.Resampling.LANCZOS,
        )
    )
    return resized[pad_dst : pad_dst + target_res, pad_dst : pad_dst + target_res].copy()


def _prepare_square_1024(image_path: Path) -> np.ndarray:
    with Image.open(image_path) as im:
        rgb = im.convert("RGB")
        w, h = rgb.size
        side = min(w, h)
        left = (w - side) // 2
        top = (h - side) // 2
        cropped = rgb.crop((left, top, left + side, top + side))
        if cropped.size != (1024, 1024):
            cropped = cropped.resize((1024, 1024), Image.Resampling.LANCZOS)
        return np.array(cropped)


def derive_pbr_maps(
    albedo_u8: np.ndarray,
    asset_id: str,
    prompt_text: str,
    out_dir: Path,
) -> tuple[dict[str, str], str]:
    """Derive 6 physically consistent periodic PBR maps from seamless albedo and save PNGs."""
    h, w, _ = albedo_u8.shape
    profile_name, profile = infer_material_profile(asset_id, prompt_text)

    # 1. Calibrate final albedo gently in CIELAB so midtones and contrast stay crisp
    albedo_clean = delight_albedo_periodic(albedo_u8, strength=0.35)

    # 2. Periodic multi-scale height map (L, 8-bit)
    rgb_f = albedo_clean.astype(np.float32) / 255.0
    lum = 0.2126 * rgb_f[..., 0] + 0.7152 * rgb_f[..., 1] + 0.0722 * rgb_f[..., 2]

    scale_f = max(0.5, float(h) / 512.0)
    blur_low = ndimage.gaussian_filter(lum, sigma=10.0 * scale_f, mode="wrap")
    blur_mid = ndimage.gaussian_filter(lum, sigma=3.0 * scale_f, mode="wrap")
    blur_fine = ndimage.gaussian_filter(lum, sigma=1.1 * scale_f, mode="wrap")

    # Crevice emphasis: dark joints/mortar sink below surrounding plateau
    local_relief = blur_mid - blur_low
    micro_detail = lum - blur_fine
    crevice_w = float(profile["crevice_weight"])
    raw_height = (
        0.45 * lum
        + (0.35 + 0.25 * crevice_w) * local_relief
        + 0.20 * micro_detail
    )
    # Periodic smoothing of 1px staircase noise before normalization
    raw_height = ndimage.gaussian_filter(raw_height, sigma=0.55, mode="wrap")

    p2 = float(np.percentile(raw_height, 2))
    p98 = float(np.percentile(raw_height, 98))
    span = max(1e-4, p98 - p2)
    height_f = np.clip((raw_height - p2) / span, 0.0, 1.0)
    height_u8 = np.round(height_f * 255.0).astype(np.uint8)
    height_norm = height_u8.astype(np.float32) / 255.0

    # 3. OpenGL (+Y up) periodic tangent-space normal map from quantized height
    h_for_n = ndimage.gaussian_filter(height_norm, sigma=0.65, mode="wrap")
    padded_h = np.pad(h_for_n, 1, mode="wrap")
    strength = float(profile["normal_strength"])
    gx = (cv2.Scharr(padded_h, cv2.CV_32F, 1, 0)[1:-1, 1:-1] / 32.0) * strength
    gy = (cv2.Scharr(padded_h, cv2.CV_32F, 0, 1)[1:-1, 1:-1] / 32.0) * strength

    nx = -gx
    ny = gy
    nz = np.ones_like(nx, dtype=np.float32)
    mag = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
    nx /= mag
    ny /= mag
    nz /= mag

    normal_u8 = np.zeros((h, w, 3), dtype=np.uint8)
    normal_u8[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_u8[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_u8[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)

    # 4. Periodic multi-radius cavity AO (0.45..1.0 -> 115..255)
    b_s = ndimage.gaussian_filter(height_norm, sigma=2.5 * scale_f, mode="wrap")
    b_m = ndimage.gaussian_filter(height_norm, sigma=7.5 * scale_f, mode="wrap")
    b_l = ndimage.gaussian_filter(height_norm, sigma=18.0 * scale_f, mode="wrap")
    cavity = (
        0.50 * np.maximum(0.0, b_s - height_norm)
        + 0.35 * np.maximum(0.0, b_m - height_norm)
        + 0.15 * np.maximum(0.0, b_l - height_norm)
    )
    ao_f = np.clip(1.0 - cavity * 2.4, 0.45, 1.0)
    ao_u8 = np.clip(np.round(ao_f * 255.0), 115, 255).astype(np.uint8)

    # 5. Profile-calibrated periodic roughness map
    r_min = float(profile["rough_min"])
    r_max = float(profile["rough_max"])
    crevice_r = float(profile["crevice_rough"])

    # Raised surfaces take r_min..r_max inversely with height; crevices blend toward crevice_r
    base_rough = r_max - (r_max - r_min) * height_norm
    crevice_mask = np.clip(cavity * 3.5, 0.0, 1.0)
    rough_f = base_rough * (1.0 - crevice_mask) + crevice_r * crevice_mask
    rough_f = np.clip(rough_f, 0.25, 0.96)
    rough_u8 = np.round(rough_f * 255.0).astype(np.uint8)

    # 6. URP MaskMap (RGBA): R=0 (Metallic), G=AO, B=Height, A=Smoothness (255 - Roughness)
    mask_u8 = np.zeros((h, w, 4), dtype=np.uint8)
    mask_u8[..., 0] = 0
    mask_u8[..., 1] = ao_u8
    mask_u8[..., 2] = height_u8
    mask_u8[..., 3] = (255 - rough_u8).astype(np.uint8)

    out_dir.mkdir(parents=True, exist_ok=True)
    filenames = {
        "albedo": f"{asset_id}_albedo.png",
        "normal": f"{asset_id}_normal.png",
        "height": f"{asset_id}_height.png",
        "roughness": f"{asset_id}_roughness.png",
        "ao": f"{asset_id}_ao.png",
        "mask": f"{asset_id}_mask.png",
    }
    Image.fromarray(albedo_clean, mode="RGB").save(out_dir / filenames["albedo"], format="PNG")
    Image.fromarray(normal_u8, mode="RGB").save(out_dir / filenames["normal"], format="PNG")
    Image.fromarray(height_u8, mode="L").save(out_dir / filenames["height"], format="PNG")
    Image.fromarray(rough_u8, mode="L").save(out_dir / filenames["roughness"], format="PNG")
    Image.fromarray(ao_u8, mode="L").save(out_dir / filenames["ao"], format="PNG")
    Image.fromarray(mask_u8, mode="RGBA").save(out_dir / filenames["mask"], format="PNG")

    return filenames, profile_name


def _build_two_disc_mask(size: int, radius: float = 160.0, blur_sigma: float = 20.0) -> np.ndarray:
    """Create smooth [0..1] mask covering the two border segments left by Pass 2a after a 25% roll."""
    yy, xx = np.ogrid[:size, :size]
    q1 = size // 4
    q3 = (3 * size) // 4
    centers = ((q1, q3), (q3, q1))
    mask = np.zeros((size, size), dtype=np.float32)
    core_r = radius * 0.55
    span = max(1.0, radius - core_r)
    for cy, cx in centers:
        dist = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2).astype(np.float32)
        w = np.clip(1.0 - np.maximum(0.0, dist - core_r) / span, 0.0, 1.0)
        w = 0.5 - 0.5 * np.cos(np.pi * w)
        mask = np.maximum(mask, w)
    mask = ndimage.gaussian_filter(mask, sigma=float(blur_sigma), mode="constant", cval=0.0)
    margin = max(32, size // 16)
    edge_x = np.minimum(xx, size - 1 - xx).astype(np.float32)
    edge_y = np.minimum(yy, size - 1 - yy).astype(np.float32)
    taper = np.clip(np.minimum(edge_x, edge_y) / float(margin), 0.0, 1.0)
    return np.clip(mask * taper, 0.0, 1.0).astype(np.float32)


def _polish_periodic_edges(rgb_u8: np.ndarray, decay_px: float = 14.0) -> np.ndarray:
    """Distribute residual low-frequency edge jump smoothly across wrap margins without blurring detail."""
    f = rgb_u8.astype(np.float32)
    h, w, _ = f.shape
    span = min(w // 4, int(round(decay_px * 3.0)))
    xs = np.arange(span, dtype=np.float32)
    ramp_x = 0.5 * np.exp(-xs / float(decay_px)) * (1.0 - xs / float(span))

    # Match column 0 and column -1 gently along low-frequency wrap difference
    diff_x = f[:, 0, :] - f[:, -1, :]
    diff_x_smooth = cv2.GaussianBlur(diff_x[:, None, :], (1, 9), sigmaX=0.0, sigmaY=2.5)[:, 0, :]
    f[:, :span, :] -= diff_x_smooth[:, None, :] * ramp_x[None, :, None]
    f[:, -span:, :] += diff_x_smooth[:, None, :] * ramp_x[::-1][None, :, None]

    span_y = min(h // 4, int(round(decay_px * 3.0)))
    ys = np.arange(span_y, dtype=np.float32)
    ramp_y = 0.5 * np.exp(-ys / float(decay_px)) * (1.0 - ys / float(span_y))
    diff_y = f[0, :, :] - f[-1, :, :]
    diff_y_smooth = cv2.GaussianBlur(diff_y[None, :, :], (9, 1), sigmaX=2.5, sigmaY=0.0)[0, :, :]
    f[:span_y, :, :] -= diff_y_smooth[None, :, :] * ramp_y[:, None, None]
    f[-span_y:, :, :] += diff_y_smooth[None, :, :] * ramp_y[::-1][:, None, None]

    return np.clip(np.round(f), 0, 255).astype(np.uint8)


def _run_comfy_terrain_synthesis(
    base_rgb_1024: np.ndarray | None,
    prompt_text: str,
    style: str,
    prompt_hint: str,
    engine_resolved: str,
    seed: int,
    steps: int,
    work_dir: Path,
    keep_comfy: bool,
) -> tuple[np.ndarray, int]:
    """Run Pass 1 (if needed) + 50% toroidal roll + Pass 2 cross-seam inpaint via ComfyUI."""
    pos_prompt, neg_prompt = build_terrain_prompt(prompt_text, style, prompt_hint)
    comfy_url: str | None = None
    spawned_proc: subprocess.Popen[bytes] | None = None

    with VramMonitor() as mon:
        try:
            comfy_url, spawned_proc = ensure_comfy_running()

            if base_rgb_1024 is None:
                if engine_resolved == "comfy-zit":
                    wf1 = build_zit_terrain_pass1(pos_prompt, seed=seed, steps=steps)
                    p1_img = comfy_run_workflow(comfy_url, wf1, save_node_id="11")
                else:
                    wf1 = build_xl_terrain_pass1(pos_prompt, neg_prompt, seed=seed, steps=steps)
                    p1_img = comfy_run_workflow(comfy_url, wf1, save_node_id="7")
                p1_rgb = np.array(p1_img.resize((1024, 1024), Image.Resampling.LANCZOS))
            else:
                p1_rgb = base_rgb_1024

            p1_rgb = delight_albedo_periodic(p1_rgb, strength=0.85)
            Image.fromarray(p1_rgb, mode="RGB").save(work_dir / "pass1_delit.png", format="PNG")

            # 50% toroidal roll + feathered Gaussian cross-seam mask
            rolled_rgb = np.roll(p1_rgb, shift=(512, 512), axis=(0, 1))
            mask_f = _build_cross_seam_mask(1024, plateau_half=56, band_half=128, blur_sigma=22.0)
            mask_u8 = np.clip(np.round(mask_f * 255.0), 0, 255).astype(np.uint8)

            rolled_local = work_dir / "pass1_rolled.png"
            mask_local = work_dir / "cross_seam_mask.png"
            Image.fromarray(rolled_rgb, mode="RGB").save(rolled_local, format="PNG")
            Image.fromarray(mask_u8, mode="L").save(mask_local, format="PNG")

            tag = uuid.uuid4().hex[:8]
            up_rolled = comfy_upload_image(comfy_url, rolled_local, remote_name=f"realmz_terrain_rolled_{tag}.png")
            up_mask = comfy_upload_image(comfy_url, mask_local, remote_name=f"realmz_terrain_mask_{tag}.png")

            if engine_resolved == "comfy-zit":
                wf2 = build_zit_terrain_pass2_inpaint(
                    pos_prompt,
                    rolled_image_name=up_rolled,
                    mask_image_name=up_mask,
                    seed=int(seed) + 1,
                    steps=steps,
                    denoise=0.94,
                )
                p2_img = comfy_run_workflow(comfy_url, wf2, save_node_id="13")
            else:
                wf2 = build_xl_terrain_pass2_inpaint(
                    pos_prompt,
                    neg_prompt,
                    rolled_image_name=up_rolled,
                    mask_image_name=up_mask,
                    seed=int(seed) + 1,
                    steps=steps,
                    denoise=0.90,
                )
                p2_img = comfy_run_workflow(comfy_url, wf2, save_node_id="10")

            p2_rgb = np.array(p2_img.resize((1024, 1024), Image.Resampling.LANCZOS))
            blended_rolled = _laplacian_pyramid_blend(
                fg_rgb=p2_rgb, bg_rgb=rolled_rgb, mask_f=mask_f, levels=5
            )
            unrolled_2a = np.roll(blended_rolled, shift=(-512, -512), axis=(0, 1))

            # Pass 2b: 25% toroidal roll moves the two Pass 2a taper-margin gaps into the interior
            q_shift = 256
            rolled_2b = np.roll(unrolled_2a, shift=(q_shift, q_shift), axis=(0, 1))
            mask_2b_f = _build_two_disc_mask(1024, radius=155.0, blur_sigma=18.0)
            mask_2b_u8 = np.clip(np.round(mask_2b_f * 255.0), 0, 255).astype(np.uint8)

            rolled_2b_local = work_dir / "pass2b_rolled.png"
            mask_2b_local = work_dir / "pass2b_mask.png"
            Image.fromarray(rolled_2b, mode="RGB").save(rolled_2b_local, format="PNG")
            Image.fromarray(mask_2b_u8, mode="L").save(mask_2b_local, format="PNG")

            up_rolled_2b = comfy_upload_image(
                comfy_url, rolled_2b_local, remote_name=f"realmz_terrain_r2b_{tag}.png"
            )
            up_mask_2b = comfy_upload_image(
                comfy_url, mask_2b_local, remote_name=f"realmz_terrain_m2b_{tag}.png"
            )

            if engine_resolved == "comfy-zit":
                wf2b = build_zit_terrain_pass2_inpaint(
                    pos_prompt,
                    rolled_image_name=up_rolled_2b,
                    mask_image_name=up_mask_2b,
                    seed=int(seed) + 2,
                    steps=steps,
                    denoise=0.92,
                )
                p2b_img = comfy_run_workflow(comfy_url, wf2b, save_node_id="13")
            else:
                wf2b = build_xl_terrain_pass2_inpaint(
                    pos_prompt,
                    neg_prompt,
                    rolled_image_name=up_rolled_2b,
                    mask_image_name=up_mask_2b,
                    seed=int(seed) + 2,
                    steps=steps,
                    denoise=0.88,
                )
                p2b_img = comfy_run_workflow(comfy_url, wf2b, save_node_id="10")

            p2b_rgb = np.array(p2b_img.resize((1024, 1024), Image.Resampling.LANCZOS))
            blended_2b = _laplacian_pyramid_blend(
                fg_rgb=p2b_rgb, bg_rgb=rolled_2b, mask_f=mask_2b_f, levels=5
            )
            unrolled = np.roll(blended_2b, shift=(-q_shift, -q_shift), axis=(0, 1))
            unrolled = delight_albedo_periodic(unrolled, strength=0.45)
            unrolled = _polish_periodic_edges(unrolled, decay_px=14.0)
        finally:
            teardown_comfy(comfy_url, spawned_proc, keep_comfy=keep_comfy)

    return unrolled, int(mon.peak_used_mib)


def _resolve_engine(engine: str, has_ref_image: bool) -> str:
    eng = engine.strip().lower()
    if eng not in VALID_TERRAIN_ENGINES:
        raise ValueError(f"Unsupported terrain engine '{engine}'. Expected one of: {', '.join(VALID_TERRAIN_ENGINES)}")
    if eng == "image":
        return "image"
    if eng == "auto":
        if has_ref_image:
            return "image"
        if COMFY_ZIT_UNET.exists():
            return "comfy-zit"
        if COMFY_XL_CKPT.exists():
            return "comfy-xl"
        raise FileNotFoundError(
            "No ComfyUI terrain model found (checked z-image-turbo_fp8_scaled_e4m3fn_KJ.safetensors and Illustrious-XL-v2.0.safetensors)."
        )
    if eng == "comfy-zit" and not COMFY_ZIT_UNET.exists():
        raise FileNotFoundError(f"ComfyUI Z-Image-Turbo UNET missing: {COMFY_ZIT_UNET}")
    if eng == "comfy-xl" and not COMFY_XL_CKPT.exists():
        raise FileNotFoundError(f"ComfyUI Illustrious-XL checkpoint missing: {COMFY_XL_CKPT}")
    return eng


def generate_terrain_asset(
    image_or_prompt: str | Path,
    name: str | None = None,
    res: int = TERRAIN_DEFAULT_RES,
    style: str = TERRAIN_DEFAULT_STYLE,
    tile_scale: float = TERRAIN_DEFAULT_TILE_SCALE_M,
    prompt_hint: str = "",
    seed: int = 42,
    steps: int = 8,
    engine: str = "auto",
    keep_comfy: bool = False,
    ship: bool = False,
    notify: bool = False,
    run_unity_import: bool = False,
    jobs_dir: Path | None = None,
) -> dict[str, Any]:
    """Generate a seamless 6-map PBR terrain bundle, verify QA, and optionally ship to RealmzUnity."""
    raw_input = str(image_or_prompt).strip()
    candidate_path = Path(raw_input)
    has_ref_image = candidate_path.exists() and candidate_path.is_file()

    if name:
        asset_id = sanitize_asset_id(name)
    elif has_ref_image:
        asset_id = sanitize_asset_id(candidate_path.stem)
    else:
        asset_id = sanitize_asset_id("_".join(raw_input.split()[:5]))

    folder_name = asset_id if asset_id.startswith("terrain_") else f"terrain_{asset_id}"
    root_jobs = Path(jobs_dir) if jobs_dir is not None else JOBS_DIR
    job_dir = root_jobs / folder_name
    job_dir.mkdir(parents=True, exist_ok=True)
    work_dir = job_dir / "_work"
    work_dir.mkdir(parents=True, exist_ok=True)

    target_res = int(res)
    if target_res < 64:
        raise ValueError(f"Terrain resolution must be >= 64, got {target_res}")

    engine_resolved = _resolve_engine(engine, has_ref_image=has_ref_image)
    t_start = time.perf_counter()

    if has_ref_image:
        ref_1024 = _prepare_square_1024(candidate_path)
        prompt_text = f"{asset_id.replace('_', ' ')} {prompt_hint}".strip()
        if engine_resolved == "image":
            delit_1024 = delight_albedo_periodic(ref_1024, strength=0.85)
            seamless_1024 = make_seamless_toroidal_reference(delit_1024)
            peak_vram = query_gpu_vram().get("used_mib", 0)
        else:
            seamless_1024, peak_vram = _run_comfy_terrain_synthesis(
                base_rgb_1024=ref_1024,
                prompt_text=prompt_text,
                style=style,
                prompt_hint=prompt_hint,
                engine_resolved=engine_resolved,
                seed=seed,
                steps=steps,
                work_dir=work_dir,
                keep_comfy=keep_comfy,
            )
    else:
        if engine_resolved == "image":
            raise ValueError("Engine 'image' requires an existing reference image path.")
        prompt_text = raw_input
        seamless_1024, peak_vram = _run_comfy_terrain_synthesis(
            base_rgb_1024=None,
            prompt_text=prompt_text,
            style=style,
            prompt_hint=prompt_hint,
            engine_resolved=engine_resolved,
            seed=seed,
            steps=steps,
            work_dir=work_dir,
            keep_comfy=keep_comfy,
        )

    Image.fromarray(seamless_1024, mode="RGB").save(work_dir / "seamless_1024.png", format="PNG")
    albedo_res = toroidal_downsample(seamless_1024, target_res=target_res)
    maps, material_profile = derive_pbr_maps(
        albedo_u8=albedo_res,
        asset_id=asset_id,
        prompt_text=f"{asset_id} {prompt_text} {prompt_hint}",
        out_dir=job_dir,
    )

    idle_vram = query_gpu_vram().get("used_mib", 0)
    elapsed_sec = round(time.perf_counter() - t_start, 2)
    display_name = sanitize_ascii(asset_id.replace("_", " ").strip().title())

    manifest: dict[str, Any] = {
        "schema_version": "1.0",
        "asset_kind": "terrain",
        "id": asset_id,
        "name": display_name,
        "category": "Tile",
        "material_profile": material_profile,
        "resolution": target_res,
        "style": sanitize_ascii(style),
        "tile_scale_m": float(tile_scale),
        "maps": maps,
        "seamless_metrics": {},
        "normal_qa": {},
        "luma_contrast_qa": {},
        "engine": engine_resolved,
        "seed": int(seed),
        "peak_vram_mib": int(peak_vram),
        "idle_vram_mib": int(idle_vram),
        "elapsed_sec": elapsed_sec,
        "timestamp": _iso_now(),
    }
    manifest_path = job_dir / "terrain.asset.json"
    manifest_path.write_text(sanitize_ascii(json.dumps(manifest, indent=2)), encoding="utf-8")

    qa = verify_terrain_bundle(job_dir, render_contact_sheet=True)
    manifest["seamless_metrics"] = qa["seamless_metrics"]
    manifest["normal_qa"] = qa["normal_qa"]
    manifest["luma_contrast_qa"] = qa["luma_contrast_qa"]
    manifest["qa_passed"] = qa["passed"]
    manifest["qa_failures"] = qa["failures"]
    manifest_path.write_text(sanitize_ascii(json.dumps(manifest, indent=2)), encoding="utf-8")

    ship_result = None
    if ship:
        from .pipeline import ship_to_realmz

        ship_result = ship_to_realmz(
            job_dir,
            notify=notify,
            run_unity_import=run_unity_import,
        )

    return {
        "status": "ok" if qa["passed"] else "qa_warning",
        "asset_kind": "terrain",
        "id": asset_id,
        "folder_name": folder_name,
        "category": "Tile",
        "material_profile": material_profile,
        "resolution": target_res,
        "style": sanitize_ascii(style),
        "tile_scale_m": float(tile_scale),
        "engine": engine_resolved,
        "seed": int(seed),
        "qa_passed": qa["passed"],
        "qa_failures": qa["failures"],
        "seamless_metrics": qa["seamless_metrics"],
        "normal_qa": qa["normal_qa"],
        "luma_contrast_qa": qa["luma_contrast_qa"],
        "elapsed_sec": elapsed_sec,
        "peak_vram_mib": int(peak_vram),
        "idle_vram_mib": int(idle_vram),
        "job_dir": str(job_dir),
        "manifest_path": str(manifest_path),
        "qa_contact_sheet": qa.get("qa_contact_sheet"),
        "maps": maps,
        "shipped": ship_result,
    }
