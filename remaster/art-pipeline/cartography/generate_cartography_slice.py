"""
Realmz Phase 1 Cartography Asset Generator (Songs of Conquest 64 px/m Standard).

Generates serialized on RTX 5060 Ti 8 GB via ComfyUI (:8188) + IP-Adapter Plus +
ControlNet Union SDXL (Blender Depth/Canny & procedural flat top-down pattern guides) +
PixelOE + exterior-only safe alpha + palette_soc_sunlit_128.json + laigter normals.

Outputs directly into C:\\realmz\\remaster\\RealmzUnity\\Assets\\Art\\Cartography\\:
  - tiles/: 7 seamless 128x128 RGBA ground/water tiles + _n.png (pixel noise alpha)
  - structures/: bywater_inn (320x256), stone_bridge (256x192), stone_fence_h/v (128x128) + _n.png
  - trees/: tree_oak_a/b (224x224), tree_pine_a/b (192x256), tree_willow_a (256x256) + _n.png
  - units/: knight (512x128 4-frame strip), giant_frog (512x128 4-frame strip) + _n.png

And saves visual QA contact sheets into scratch/realmz_soc_vertical_slice/.
Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW in all subprocesses.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
import urllib.parse
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw, ImageFont

CART_DIR = Path(__file__).resolve().parent
PIPE_DIR = CART_DIR.parent
UNITY_CART_ROOT = Path(r"C:\realmz\remaster\RealmzUnity\Assets\Art\Cartography")
SCRATCH_ROOT = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_soc_vertical_slice")
GUIDE_DIR = SCRATCH_ROOT / "whitebox_guides"
STYLE_DIR = SCRATCH_ROOT / "style_refs"
RAW_DIR = SCRATCH_ROOT / "raw_comfy"
PALETTE_PATH = PIPE_DIR / "palette_soc_sunlit_128.json"

sys.path.insert(0, str(CART_DIR))
sys.path.insert(0, str(PIPE_DIR))
import pixel_postprocess as pp  # noqa: E402

SERVER_URL = "http://127.0.0.1:8188"
CREATE_NO_WINDOW = 0x08000000

SPRITE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "multiple characters, sprite sheet, grid, ground, floor, cast shadow, drop shadow, "
    "circle background, medallion, badge, halo, vignette, pedestal, platform, ground disc, "
    "isometric block, diorama, floating island, cube base, dirt block, grass block, terrain slab, "
    "dirt mound, grass patch under feet, soil base, UI, text, watermark, signature, lowres, worst quality"
)

TILE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, perspective, horizon, sky, vanish point, "
    "isometric diamond, slanted view, buildings, houses, trees, characters, border, frame, "
    "vignette, diagonal bands, striped horizon, text, watermark, lowres, worst quality"
)


def query_vram_mib() -> int:
    try:
        import subprocess

        out = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            text=True,
            timeout=5,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        ).strip()
        return int(out.splitlines()[0].strip())
    except Exception:
        return -1


def upload_image(image_path: Path, remote_name: str) -> str:
    with image_path.open("rb") as f:
        r = requests.post(
            f"{SERVER_URL}/upload/image",
            files={"image": (remote_name, f, "image/png")},
            data={"overwrite": "true"},
            timeout=30,
        )
        r.raise_for_status()
        return r.json()["name"]


def run_comfy_prompt(workflow: dict, timeout: float = 300.0) -> Tuple[bytes, float, int]:
    t0 = time.time()
    peak = query_vram_mib()
    r = requests.post(f"{SERVER_URL}/prompt", json={"prompt": workflow}, timeout=30)
    if not r.ok:
        raise RuntimeError(f"ComfyUI /prompt failed ({r.status_code}): {r.text}")
    pid = r.json()["prompt_id"]

    deadline = time.time() + timeout
    entry = None
    while time.time() < deadline:
        v = query_vram_mib()
        if v > peak:
            peak = v
        hr = requests.get(f"{SERVER_URL}/history/{pid}", timeout=10)
        if hr.ok:
            data = hr.json()
            if pid in data:
                entry = data[pid]
                st = entry.get("status", {})
                if st.get("status_str") == "error":
                    raise RuntimeError(f"ComfyUI execution error: {json.dumps(st)}")
                if entry.get("outputs"):
                    break
        time.sleep(0.35)

    if not entry:
        raise TimeoutError(f"ComfyUI timed out on {pid}")

    img_info = None
    for node_out in entry.get("outputs", {}).values():
        imgs = node_out.get("images", [])
        if imgs:
            img_info = imgs[0]
            break
    if not img_info:
        raise RuntimeError(f"No output images in {pid}")

    params = urllib.parse.urlencode(
        {
            "filename": img_info["filename"],
            "subfolder": img_info.get("subfolder", ""),
            "type": img_info.get("type", "output"),
        }
    )
    ir = requests.get(f"{SERVER_URL}/view?{params}", timeout=30)
    ir.raise_for_status()
    v = query_vram_mib()
    if v > peak:
        peak = v
    return ir.content, round(time.time() - t0, 2), peak


def build_cartography_workflow(
    prompt: str,
    negative: str,
    seed: int,
    filename_prefix: str,
    lora_name: str = "pixel-art-style-illustrious-by-skormino.safetensors",
    lora_strength: float = 0.88,
    steps: int = 26,
    cfg: float = 6.5,
    width: int = 1024,
    height: int = 1024,
    ip_ref_remote: Optional[str] = None,
    ip_weight: float = 0.42,
    depth_remote: Optional[str] = None,
    depth_strength: float = 0.56,
    depth_end: float = 0.82,
    canny_remote: Optional[str] = None,
    canny_strength: float = 0.46,
    canny_end: float = 0.68,
    init_remote: Optional[str] = None,
    denoise: float = 1.0,
) -> dict:
    wf: Dict[str, dict] = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "Illustrious-XL-v2.0.safetensors"}},
        "2": {
            "class_type": "LoraLoader",
            "inputs": {
                "model": ["1", 0],
                "clip": ["1", 1],
                "lora_name": lora_name,
                "strength_model": lora_strength,
                "strength_clip": lora_strength,
            },
        },
        "3": {"class_type": "CLIPSetLastLayer", "inputs": {"clip": ["2", 1], "stop_at_clip_layer": -2}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": negative}},
    }

    model_ref = ["2", 0]
    pos_ref = ["4", 0]
    neg_ref = ["5", 0]
    nid = 10

    if ip_ref_remote:
        ip_ldr = str(nid); nid += 1
        cv_ldr = str(nid); nid += 1
        ip_img = str(nid); nid += 1
        ip_app = str(nid); nid += 1
        wf[ip_ldr] = {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": "ip-adapter-plus_sdxl_vit-h.safetensors"}}
        wf[cv_ldr] = {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"}}
        wf[ip_img] = {"class_type": "LoadImage", "inputs": {"image": ip_ref_remote}}
        wf[ip_app] = {
            "class_type": "IPAdapterAdvanced",
            "inputs": {
                "model": model_ref,
                "ipadapter": [ip_ldr, 0],
                "image": [ip_img, 0],
                "weight": ip_weight,
                "weight_type": "style transfer",
                "combine_embeds": "concat",
                "start_at": 0.0,
                "end_at": 0.85,
                "embeds_scaling": "V only",
                "clip_vision": [cv_ldr, 0],
            },
        }
        model_ref = [ip_app, 0]

    cn_ldr = None
    if depth_remote or canny_remote:
        cn_ldr = str(nid); nid += 1
        wf[cn_ldr] = {"class_type": "ControlNetLoader", "inputs": {"control_net_name": "xinsir-controlnet-union-sdxl-1.0-promax.safetensors"}}

    if depth_remote:
        d_type = str(nid); nid += 1
        d_img = str(nid); nid += 1
        d_app = str(nid); nid += 1
        wf[d_type] = {"class_type": "SetUnionControlNetType", "inputs": {"control_net": [cn_ldr, 0], "type": "depth"}}
        wf[d_img] = {"class_type": "LoadImage", "inputs": {"image": depth_remote}}
        wf[d_app] = {
            "class_type": "ControlNetApplyAdvanced",
            "inputs": {
                "positive": pos_ref,
                "negative": neg_ref,
                "control_net": [d_type, 0],
                "image": [d_img, 0],
                "strength": depth_strength,
                "start_percent": 0.0,
                "end_percent": depth_end,
                "vae": ["1", 2],
            },
        }
        pos_ref = [d_app, 0]
        neg_ref = [d_app, 1]

    if canny_remote:
        c_type = str(nid); nid += 1
        c_img = str(nid); nid += 1
        c_app = str(nid); nid += 1
        wf[c_type] = {"class_type": "SetUnionControlNetType", "inputs": {"control_net": [cn_ldr, 0], "type": "canny/lineart/anime_lineart/mlsd"}}
        wf[c_img] = {"class_type": "LoadImage", "inputs": {"image": canny_remote}}
        wf[c_app] = {
            "class_type": "ControlNetApplyAdvanced",
            "inputs": {
                "positive": pos_ref,
                "negative": neg_ref,
                "control_net": [c_type, 0],
                "image": [c_img, 0],
                "strength": canny_strength,
                "start_percent": 0.0,
                "end_percent": canny_end,
                "vae": ["1", 2],
            },
        }
        pos_ref = [c_app, 0]
        neg_ref = [c_app, 1]

    if init_remote:
        init_img = str(nid); nid += 1
        init_enc = str(nid); nid += 1
        wf[init_img] = {"class_type": "LoadImage", "inputs": {"image": init_remote}}
        wf[init_enc] = {"class_type": "VAEEncode", "inputs": {"pixels": [init_img, 0], "vae": ["1", 2]}}
        latent_ref = [init_enc, 0]
    else:
        lat = str(nid); nid += 1
        wf[lat] = {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}}
        latent_ref = [lat, 0]

    ksampler = str(nid); nid += 1
    vaedec = str(nid); nid += 1
    save = str(nid); nid += 1
    wf[ksampler] = {
        "class_type": "KSampler",
        "inputs": {
            "model": model_ref,
            "positive": pos_ref,
            "negative": neg_ref,
            "latent_image": latent_ref,
            "seed": seed,
            "steps": steps,
            "cfg": cfg,
            "sampler_name": "dpmpp_2m",
            "scheduler": "karras",
            "denoise": denoise,
        },
    }
    wf[vaedec] = {"class_type": "VAEDecode", "inputs": {"samples": [ksampler, 0], "vae": ["1", 2]}}
    wf[save] = {"class_type": "SaveImage", "inputs": {"images": [vaedec, 0], "filename_prefix": filename_prefix}}
    return wf


def build_inpaint_workflow(
    prompt: str,
    negative: str,
    seed: int,
    remote_img: str,
    remote_mask: str,
    prefix: str,
    lora_name: str = "pixel-art-xl-nerijs.safetensors",
    lora_strength: float = 0.85,
    steps: int = 18,
    denoise: float = 0.76,
) -> dict:
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "Illustrious-XL-v2.0.safetensors"}},
        "2": {
            "class_type": "LoraLoader",
            "inputs": {
                "model": ["1", 0],
                "clip": ["1", 1],
                "lora_name": lora_name,
                "strength_model": lora_strength,
                "strength_clip": lora_strength,
            },
        },
        "3": {"class_type": "CLIPSetLastLayer", "inputs": {"clip": ["2", 1], "stop_at_clip_layer": -2}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": negative}},
        "6": {"class_type": "LoadImage", "inputs": {"image": remote_img}},
        "7": {"class_type": "LoadImageMask", "inputs": {"image": remote_mask, "channel": "red"}},
        "8": {"class_type": "VAEEncode", "inputs": {"pixels": ["6", 0], "vae": ["1", 2]}},
        "9": {"class_type": "SetLatentNoiseMask", "inputs": {"samples": ["8", 0], "mask": ["7", 0]}},
        "10": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["2", 0],
                "positive": ["4", 0],
                "negative": ["5", 0],
                "latent_image": ["9", 0],
                "seed": seed,
                "steps": steps,
                "cfg": 6.5,
                "sampler_name": "dpmpp_2m",
                "scheduler": "karras",
                "denoise": denoise,
            },
        },
        "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["1", 2]}},
        "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": prefix}},
    }


def make_tile_pattern_guides(out_dir: Path) -> Dict[str, Tuple[Path, Optional[Path]]]:
    """Build flat 1024x1024 top-down pattern guides (init RGB + optional Canny edge guide)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    guides: Dict[str, Tuple[Path, Optional[Path]]] = {}
    rng = np.random.default_rng(30401)

    # 1) cobble_plaza: periodic Voronoi cobblestone pavers + mortar seams
    n_pts = 85
    pts = rng.uniform(0.0, 1.0, size=(n_pts, 2))
    yy, xx = np.mgrid[0:512, 0:512] / 512.0
    coords = np.stack([xx, yy], axis=-1)  # H,W,2
    best_d1 = np.full((512, 512), 1e9, dtype=np.float32)
    best_d2 = np.full((512, 512), 1e9, dtype=np.float32)
    best_id = np.zeros((512, 512), dtype=np.int32)
    for i, (px, py) in enumerate(pts):
        dx = np.abs(coords[..., 0] - px)
        dx = np.minimum(dx, 1.0 - dx)
        dy = np.abs(coords[..., 1] - py)
        dy = np.minimum(dy, 1.0 - dy)
        d = np.sqrt(dx * dx + dy * dy)
        m1 = d < best_d1
        m2 = (~m1) & (d < best_d2)
        best_d2 = np.where(m1, best_d1, np.where(m2, d, best_d2))
        best_d1 = np.where(m1, d, best_d1)
        best_id = np.where(m1, i, best_id)
    seam = (best_d2 - best_d1) < 0.011
    stone_shades = rng.integers(118, 178, size=(n_pts, 3), dtype=np.uint8)
    stone_shades[:, 0] = np.clip(stone_shades[:, 0] + 4, 0, 255)
    stone_shades[:, 2] = np.clip(stone_shades[:, 2] - 4, 0, 255)
    cob_rgb = stone_shades[best_id]
    cob_rgb[seam] = [56, 53, 50]
    cob_init = cv2.resize(cob_rgb, (1024, 1024), interpolation=cv2.INTER_NEAREST)
    cob_canny = np.zeros((1024, 1024), dtype=np.uint8)
    cob_canny[cv2.resize(seam.astype(np.uint8) * 255, (1024, 1024), interpolation=cv2.INTER_NEAREST) > 128] = 255
    p_ci = out_dir / "cobble_plaza_init.png"
    p_cc = out_dir / "cobble_plaza_canny.png"
    Image.fromarray(cob_init, "RGB").save(p_ci)
    Image.fromarray(cob_canny, "L").convert("RGB").save(p_cc)
    guides["cobble_plaza"] = (p_ci, p_cc)

    # 2) cliff_rock: vertical orthographic stepped stone wall ledges + cracks
    cliff = np.full((512, 512, 3), [74, 70, 72], dtype=np.uint8)
    edge = np.zeros((512, 512), dtype=np.uint8)
    for row_y in range(0, 512, 64):
        cv2.line(cliff, (0, row_y), (512, row_y), (42, 40, 48), 5)
        cv2.line(cliff, (0, min(511, row_y + 4)), (512, min(511, row_y + 4)), (132, 126, 120), 4)
        cv2.line(edge, (0, row_y), (512, row_y), 255, 3)
        x_cur = (row_y * 37) % 96
        while x_cur < 512:
            step_w = int(rng.integers(58, 122))
            cv2.line(cliff, (x_cur, row_y), (x_cur, min(511, row_y + 64)), (36, 35, 44), 4)
            cv2.line(edge, (x_cur, row_y), (x_cur, min(511, row_y + 64)), 255, 3)
            x_cur += step_w
    p_cli = out_dir / "cliff_rock_init.png"
    p_clc = out_dir / "cliff_rock_canny.png"
    Image.fromarray(cv2.resize(cliff, (1024, 1024), interpolation=cv2.INTER_NEAREST), "RGB").save(p_cli)
    Image.fromarray(cv2.resize(edge, (1024, 1024), interpolation=cv2.INTER_NEAREST), "L").convert("RGB").save(p_clc)
    guides["cliff_rock"] = (p_cli, p_clc)

    # 3) flat organic color field inits for meadow_grass, moss_patch, dirt_road, river_mud, shallow_water
    pal_patches = {
        "meadow_grass": ([46, 105, 59], [61, 135, 72], [34, 77, 46]),
        "moss_patch": ([31, 51, 36], [46, 74, 51], [66, 102, 67]),
        "dirt_road": ([138, 112, 88], [170, 141, 112], [107, 85, 66]),
        "river_mud": ([71, 42, 26], [99, 60, 36], [46, 74, 51]),
        "shallow_water": ([26, 78, 117], [34, 106, 156], [63, 168, 224]),
    }
    for name, (c0, c1, c2) in pal_patches.items():
        base = np.zeros((256, 256, 3), dtype=np.float32)
        base[:, :] = c0
        noise_a = cv2.GaussianBlur(rng.normal(0, 1, (256, 256)).astype(np.float32), (0, 0), 9.0)
        noise_b = rng.normal(0, 1, (256, 256)).astype(np.float32)
        w1 = np.clip(0.5 + noise_a * 0.45, 0.0, 1.0)[..., None]
        w2 = np.clip(0.35 + noise_b * 0.25, 0.0, 1.0)[..., None]
        mix = base * (1.0 - w1) + np.array(c1, dtype=np.float32) * w1
        mix = mix * (1.0 - w2 * 0.45) + np.array(c2, dtype=np.float32) * (w2 * 0.45)
        u8 = np.clip(np.round(mix), 0, 255).astype(np.uint8)
        p_init = out_dir / f"{name}_init.png"
        Image.fromarray(cv2.resize(u8, (1024, 1024), interpolation=cv2.INTER_NEAREST), "RGB").save(p_init)
        guides[name] = (p_init, None)

    return guides


def make_seamless_tile_with_inpaint(
    name: str,
    prompt: str,
    extra_neg: str,
    seed: int,
    init_path: Path,
    canny_path: Optional[Path],
    ip_ref_remote: Optional[str],
    out_albedo: Path,
    noise_seed: int,
    alpha_min: int = 64,
    alpha_max: int = 255,
) -> dict:
    """Generate 1024x1024 flat top-down tile, heal seams via 50% + 25% toroidal inpaint, then PixelOE -> 128x128."""
    init_remote = upload_image(init_path, f"carto_tile_init_{name}.png")
    canny_remote = upload_image(canny_path, f"carto_tile_canny_{name}.png") if canny_path else None
    neg = TILE_NEG + extra_neg

    wf = build_cartography_workflow(
        prompt=prompt,
        negative=neg,
        seed=seed,
        filename_prefix=f"carto_raw_{name}",
        lora_name="pixel-art-xl-nerijs.safetensors",
        lora_strength=0.86,
        steps=24,
        cfg=6.2,
        ip_ref_remote=ip_ref_remote,
        ip_weight=0.35 if ip_ref_remote else 0.0,
        canny_remote=canny_remote,
        canny_strength=0.58 if canny_remote else 0.0,
        canny_end=0.72,
        init_remote=init_remote,
        denoise=0.72 if canny_remote else 0.78,
    )
    raw_bytes, sec1, peak1 = run_comfy_prompt(wf)
    raw_p = RAW_DIR / f"{name}_p1.png"
    raw_p.write_bytes(raw_bytes)

    # Pass 2: 50% toroidal roll + cross-seam mask inpaint
    arr = np.array(Image.open(raw_p).convert("RGB"))
    h, w, _ = arr.shape
    rolled = np.roll(np.roll(arr, h // 2, axis=0), w // 2, axis=1)
    mask = np.zeros((h, w), dtype=np.uint8)
    bw = 72
    mask[h // 2 - bw : h // 2 + bw, :] = 255
    mask[:, w // 2 - bw : w // 2 + bw] = 255
    mask = cv2.GaussianBlur(mask, (31, 31), 0)

    p_r = RAW_DIR / f"{name}_roll.png"
    p_m = RAW_DIR / f"{name}_mask.png"
    Image.fromarray(rolled, "RGB").save(p_r)
    Image.fromarray(mask, "L").save(p_m)
    r_img = upload_image(p_r, f"carto_roll_{name}.png")
    r_msk = upload_image(p_m, f"carto_mask_{name}.png")

    wf_in = build_inpaint_workflow(
        prompt=prompt,
        negative=neg,
        seed=seed + 17,
        remote_img=r_img,
        remote_mask=r_msk,
        prefix=f"carto_inpaint_{name}",
        denoise=0.74,
    )
    in_bytes, sec2, peak2 = run_comfy_prompt(wf_in)
    p_in = RAW_DIR / f"{name}_inpainted.png"
    p_in.write_bytes(in_bytes)
    in_arr = np.array(Image.open(p_in).convert("RGB"))
    healed = np.roll(np.roll(in_arr, -(h // 2), axis=0), -(w // 2), axis=1)
    p_final_raw = RAW_DIR / f"{name}_1024_seamless.png"
    Image.fromarray(healed, "RGB").save(p_final_raw)

    stats = pp.process_seamless_tile(
        p_final_raw,
        out_albedo,
        target_size=(128, 128),
        thickness=1,
        dither_strength=0.02,
        grade_strength=0.12,
        shadow_lift=4.0,
        vibrance_boost=1.08,
        noise_seed=noise_seed,
        alpha_min=alpha_min,
        alpha_max=alpha_max,
    )
    stats["elapsed_s"] = round(sec1 + sec2, 2)
    stats["peak_vram_mib"] = max(peak1, peak2)
    return stats


def strip_tree_bottom_dirt_oval(rgba: np.ndarray) -> np.ndarray:
    """Remove accidental floating brown dirt mound ovals beneath tree trunks while keeping trunk roots."""
    out = rgba.copy()
    h, w, _ = out.shape
    fg = out[..., 3] == 255
    ys = np.where(fg)[0]
    if len(ys) == 0:
        return out
    y_bot = int(ys.max())
    # Look at bottom 14% rows: if horizontal span abruptly balloons wider than 2.2x trunk waist above it, trim
    waist_y0 = max(0, y_bot - int(h * 0.28))
    waist_y1 = max(waist_y0 + 1, y_bot - int(h * 0.14))
    waist_widths = [int(np.sum(fg[y])) for y in range(waist_y0, waist_y1) if np.any(fg[y])]
    if not waist_widths:
        return out
    trunk_w = max(8.0, float(np.median(waist_widths)))
    for y in range(max(0, y_bot - int(h * 0.12)), y_bot + 1):
        row_xs = np.where(fg[y])[0]
        if len(row_xs) > trunk_w * 2.2:
            cx = (row_xs.min() + row_xs.max()) / 2.0
            keep_half = trunk_w * 0.85
            bad = np.abs(np.arange(w) - cx) > keep_half
            out[y, bad] = [0, 0, 0, 0]
    return out


def synthesize_knight_idle_frames(f0_rgba: np.ndarray) -> List[np.ndarray]:
    """Synthesize 4 consistent idle breathing/cloak/blade frames (128x128 each) with zero identity flicker."""
    h, w, _ = f0_rgba.shape
    fg = f0_rgba[..., 3] == 255
    ys = np.where(fg)[0]
    if len(ys) == 0:
        return [f0_rgba.copy() for _ in range(4)]
    y_top, y_bot = int(ys.min()), int(ys.max())
    torso_split = y_top + int((y_bot - y_top) * 0.62)

    frames = []
    # f0: neutral, f1: +1px upper breathe, f2: +1px breathe + cloak wave, f3: cloak settle
    for fidx in range(4):
        if fidx == 0:
            frames.append(f0_rgba.copy())
            continue
        cur = f0_rgba.copy()
        if fidx in (1, 2):
            # Shift upper body up 1px cleanly while anchoring boots
            upper = f0_rgba[:torso_split, :, :].copy()
            cur[max(0, 0) : torso_split - 1, :, :] = upper[1:, :, :]
            # Fill junction row seamlessly from f0
            cur[torso_split - 1, :, :] = f0_rgba[torso_split - 1, :, :]
        if fidx in (2, 3):
            # Subtle 1px cloak/edge flutter on left silhouette middle band
            b0 = y_top + int((y_bot - y_top) * 0.45)
            b1 = y_top + int((y_bot - y_top) * 0.82)
            row_slice = cur[b0:b1, :-1, :].copy()
            # Shift left 25% of foreground cloak region 1px left
            cur[b0:b1, 1:, :] = np.where(
                (np.arange(w)[None, 1:, None] < w * 0.45) & (row_slice[..., 3:4] == 255),
                row_slice,
                cur[b0:b1, 1:, :],
            )
        frames.append(cur)
    return frames


def synthesize_frog_idle_frames(f0_rgba: np.ndarray) -> List[np.ndarray]:
    """Synthesize 4 consistent idle breathing & throat-sac frames (128x128 each)."""
    h, w, _ = f0_rgba.shape
    fg = f0_rgba[..., 3] == 255
    ys = np.where(fg)[0]
    if len(ys) == 0:
        return [f0_rgba.copy() for _ in range(4)]
    y_top, y_bot = int(ys.min()), int(ys.max())
    mid_y = y_top + int((y_bot - y_top) * 0.48)

    frames = []
    for fidx in range(4):
        if fidx == 0:
            frames.append(f0_rgba.copy())
            continue
        cur = f0_rgba.copy()
        if fidx in (1, 2):
            # Upper head/back rises 1px on inhale
            cur[: mid_y - 1] = f0_rgba[1:mid_y]
        if fidx == 2:
            # Throat sac expands 1px downward in middle lower belly
            t0 = y_top + int((y_bot - y_top) * 0.55)
            t1 = min(h - 1, y_bot - 4)
            if t1 > t0:
                cur[t0 + 1 : t1 + 1] = np.where(
                    cur[t0 + 1 : t1 + 1, :, 3:4] == 0,
                    f0_rgba[t0:t1],
                    cur[t0 + 1 : t1 + 1],
                )
        frames.append(cur)
    return frames


def build_unit_strip(
    name: str,
    prompt: str,
    depth_guide: Path,
    ip_ref_remote: Optional[str],
    seed: int,
    sprite_height_px: int,
    out_albedo: Path,
    synth_fn,
) -> dict:
    d_rem = upload_image(depth_guide, f"carto_depth_{name}.png")
    wf = build_cartography_workflow(
        prompt=prompt,
        negative=SPRITE_NEG,
        seed=seed,
        filename_prefix=f"carto_raw_{name}",
        lora_name="pixel-art-style-illustrious-by-skormino.safetensors",
        lora_strength=0.90,
        steps=26,
        cfg=6.5,
        ip_ref_remote=ip_ref_remote,
        ip_weight=0.40 if ip_ref_remote else 0.0,
        depth_remote=d_rem,
        depth_strength=0.52,
        depth_end=0.76,
    )
    raw_bytes, sec, peak = run_comfy_prompt(wf)
    raw_p = RAW_DIR / f"{name}_f0.png"
    raw_p.write_bytes(raw_bytes)

    # Downscale frame 0 to exact (sprite_height_px) inside 128x128 BottomCenter
    raw_arr = np.array(Image.open(raw_p).convert("RGBA"))
    rgb_clean, fg_mask = pp.remove_background_1bit(raw_arr, strip_ground_shadow=True)
    # Snap onto 128x128 with pad calculated so character stands ~sprite_height_px tall
    pad_top_bottom = max(2, (128 - sprite_height_px) // 2)
    f0_tmp = pp.pixeloe_downscale_sprite(
        rgb_clean,
        fg_mask,
        target_size=(128, 128),
        pad_px=pad_top_bottom,
        bottom_pivot=True,
        thickness=1,
    )
    # Shift down so foot rests at y=125 (3px bottom pad) instead of pad_top_bottom
    ys = np.where(f0_tmp[..., 3] == 255)[0]
    if len(ys) > 0:
        shift_d = 125 - int(ys.max())
        if shift_d > 0:
            shifted = np.zeros_like(f0_tmp)
            shifted[shift_d:] = f0_tmp[: 128 - shift_d]
            f0_tmp = shifted

    pal = pp.load_palette(PALETTE_PATH)
    f0_quant = pp.apply_sunlit_grade_and_quantize(f0_tmp, pal, preserve_alpha=False)

    frames = synth_fn(f0_quant)
    strip_rgba = np.concatenate(frames, axis=1)  # 512x128

    norm_frames = []
    backend_used = "sobel"
    for fr in frames:
        nf, b_used = pp.generate_normal_map(fr, periodic=False, normal_backend="auto")
        norm_frames.append(nf)
        backend_used = b_used
    strip_norm = np.concatenate(norm_frames, axis=1)

    out_albedo.parent.mkdir(parents=True, exist_ok=True)
    out_norm = out_albedo.with_name(f"{out_albedo.stem}_n.png")
    Image.fromarray(strip_rgba, "RGBA").save(out_albedo, "PNG")
    Image.fromarray(strip_norm, "RGBA").save(out_norm, "PNG")

    stats = pp.validate_outputs(strip_rgba, strip_norm, pal, is_tile=False)
    stats["normal_backend"] = backend_used
    stats["elapsed_s"] = sec
    stats["peak_vram_mib"] = peak
    stats["albedo_path"] = str(out_albedo)
    return stats


def make_contact_sheets() -> None:
    """Create 2x2 ground tile check sheet and overall Phase 1 cartography contact sheet."""
    # 1) 2x2 Ground Tiles check sheet
    tile_names = ["meadow_grass", "moss_patch", "dirt_road", "cobble_plaza", "river_mud", "cliff_rock", "shallow_water"]
    tw, th = 256, 256
    cols = 4
    rows = 2
    pad = 16
    sheet = Image.new("RGB", (cols * (tw + pad) + pad, rows * (th + 42 + pad) + pad + 44), (20, 22, 30))
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 12), "PHASE 1 SEAMLESS GROUND TILES (128x128 @ 2x2 Tiled, 64 px/m Standard)", fill=(244, 198, 92))
    for idx, tname in enumerate(tile_names):
        p = UNITY_CART_ROOT / "tiles" / f"{tname}.png"
        if not p.exists():
            continue
        im = Image.open(p).convert("RGB")
        tiled = Image.new("RGB", (256, 256))
        for ry in range(2):
            for rx in range(2):
                tiled.paste(im, (rx * 128, ry * 128))
        c = idx % cols
        r = idx // cols
        x = pad + c * (tw + pad)
        y = 48 + pad + r * (th + 42 + pad)
        draw.rectangle([x - 2, y - 2, x + tw + 2, y + th + 32], fill=(30, 35, 48), outline=(74, 86, 114))
        draw.text((x + 6, y + 4), tname, fill=(238, 241, 248))
        sheet.paste(tiled, (x, y + 26))
    sheet.save(SCRATCH_ROOT / "ground_tiles_2x2.png")

    # 2) Structures, Trees & Units sheet
    items = [
        ("bywater_inn (320x256, 5x4m)", UNITY_CART_ROOT / "structures" / "bywater_inn.png"),
        ("stone_bridge (256x192, 4x3m)", UNITY_CART_ROOT / "structures" / "stone_bridge.png"),
        ("stone_fence_h (128x128)", UNITY_CART_ROOT / "structures" / "stone_fence_h.png"),
        ("stone_fence_v (128x128)", UNITY_CART_ROOT / "structures" / "stone_fence_v.png"),
        ("tree_oak_a (224x224)", UNITY_CART_ROOT / "trees" / "tree_oak_a.png"),
        ("tree_oak_b (224x224)", UNITY_CART_ROOT / "trees" / "tree_oak_b.png"),
        ("tree_pine_a (192x256)", UNITY_CART_ROOT / "trees" / "tree_pine_a.png"),
        ("tree_pine_b (192x256)", UNITY_CART_ROOT / "trees" / "tree_pine_b.png"),
        ("tree_willow_a (256x256)", UNITY_CART_ROOT / "trees" / "tree_willow_a.png"),
        ("knight (512x128 4f)", UNITY_CART_ROOT / "units" / "knight.png"),
        ("giant_frog (512x128 4f)", UNITY_CART_ROOT / "units" / "giant_frog.png"),
    ]
    cw, ch = 340, 280
    c_cols = 4
    c_rows = math.ceil(len(items) / c_cols)
    cs = Image.new("RGB", (c_cols * (cw + pad) + pad, c_rows * (ch + 36 + pad) + pad + 44), (20, 22, 30))
    cdraw = ImageDraw.Draw(cs)
    cdraw.text((16, 12), "PHASE 1 STRUCTURES, TREES & 4-FRAME UNITS (64 px/m PixelOE + SoC Sunlit 128 Palette)", fill=(244, 198, 92))
    for idx, (lbl, ip) in enumerate(items):
        if not ip.exists():
            continue
        c = idx % c_cols
        r = idx // c_cols
        x = pad + c * (cw + pad)
        y = 48 + pad + r * (ch + 36 + pad)
        cdraw.rectangle([x - 2, y - 2, x + cw + 2, y + ch + 28], fill=(38, 44, 58), outline=(74, 86, 114))
        cdraw.text((x + 8, y + 4), lbl, fill=(128, 224, 134))
        im = Image.open(ip).convert("RGBA")
        # Checkerboard background to expose any alpha leaks clearly
        bg = Image.new("RGB", (cw, ch - 8), (44, 50, 66))
        scale = min(cw / im.width, (ch - 8) / im.height)
        nw = max(1, int(round(im.width * scale)))
        nh = max(1, int(round(im.height * scale)))
        rs = im.resize((nw, nh), Image.NEAREST)
        bg.paste(rs, ((cw - nw) // 2, (ch - 8 - nh) // 2), rs)
        cs.paste(bg, (x, y + 26))
    cs.save(SCRATCH_ROOT / "phase1_assets_contact_sheet.png")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate Realmz Phase 1 Cartography assets via ComfyUI")
    parser.add_argument("--group", choices=("all", "tiles", "structures", "trees", "units"), default="all")
    parser.add_argument("--only", type=str, default=None, help="Run a single named asset")
    parser.add_argument("--no-ipadapter", action="store_true", help="Skip IP-Adapter if desired")
    args = parser.parse_args()

    RAW_DIR.mkdir(parents=True, exist_ok=True)
    UNITY_CART_ROOT.mkdir(parents=True, exist_ok=True)
    report: Dict[str, dict] = {}

    # Upload SoC style crops once
    ip_town = None if args.no_ipadapter else upload_image(STYLE_DIR / "soc_arleon_town.png", "soc_style_town.png")
    ip_meadow = None if args.no_ipadapter else upload_image(STYLE_DIR / "soc_arleon_meadow.png", "soc_style_meadow.png")
    ip_knight = None if args.no_ipadapter else upload_image(STYLE_DIR / "soc_arleon_knight.png", "soc_style_knight.png")
    ip_frog = None if args.no_ipadapter else upload_image(STYLE_DIR / "soc_rana_frog.png", "soc_style_frog.png")

    def want(group: str, name: str) -> bool:
        if args.only:
            return args.only == name
        return args.group in ("all", group)

    # 1. TILES
    tile_guides = make_tile_pattern_guides(RAW_DIR / "tile_guides")
    tiles_cfg = [
        (
            "meadow_grass",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic sunlit medieval meadow grass turf, soft clustered emerald and mossy olive grass strokes, calm unified turf patches, Songs of Conquest ground texture, edge to edge seamless",
            ", yellow confetti, vertical static, noise, flowers, rocks, grid",
            304101,
            101,
            64,
            255,
        ),
        (
            "moss_patch",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic woodland moss and dark sage clover forest floor, soft clustered mossy green patches, Songs of Conquest ground texture, edge to edge seamless",
            ", yellow speckles, branches, logs, flowers",
            304102,
            102,
            64,
            255,
        ),
        (
            "dirt_road",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic warm sunlit packed sandstone earth dirt trail, subtle warm earth tones with tiny crisp pebble clusters, Songs of Conquest dirt road texture, edge to edge seamless",
            ", grass, mud puddle, planks, bricks",
            304103,
            103,
            64,
            255,
        ),
        (
            "cobble_plaza",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic medieval town cobblestone plaza pavers, irregular warm gray and sandstone rounded paving stones separated by dark slate-moss mortar seams, Songs of Conquest town plaza texture, edge to edge seamless",
            ", brick wall, perspective, horizon, road curbs",
            304104,
            104,
            64,
            255,
        ),
        (
            "river_mud",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic wet dark riverbank soil and mossy mud bank, rich dark earth with damp sage moss flecks and tiny river pebbles, Songs of Conquest ground texture, edge to edge seamless",
            ", deep water, waves, grass blades",
            304105,
            105,
            64,
            255,
        ),
        (
            "cliff_rock",
            "pixel art, 16-bit, hi-bit RPG terrain cliff texture swatch, flat straight front orthographic stepped medieval mountain rock cliff face, layered cool slate-gray and warm umber stratified stone ledges with vertical crevices and tiny moss tufts, Songs of Conquest cliff texture, edge to edge",
            ", sky, horizon, grass top, ground plane, trees",
            304106,
            106,
            180,
            255,
        ),
        (
            "shallow_water",
            "pixel art, 16-bit, hi-bit RPG terrain texture swatch, flat straight top-down orthographic bright cerulean shallow river water, calm deep blue-teal water with delicate crisp sky-cyan stylized pixel wave ripples, Songs of Conquest water texture, edge to edge seamless",
            ", land, shoreline, rocks, foam whirlpool",
            304107,
            107,
            255,
            255,
        ),
    ]
    for tname, tprompt, tneg, tseed, nseed, amin, amax in tiles_cfg:
        if not want("tiles", tname):
            continue
        p_init, p_canny = tile_guides[tname]
        st = make_seamless_tile_with_inpaint(
            tname,
            tprompt,
            tneg,
            tseed,
            p_init,
            p_canny,
            ip_meadow if tname in ("meadow_grass", "moss_patch") else ip_town,
            UNITY_CART_ROOT / "tiles" / f"{tname}.png",
            noise_seed=nseed,
            alpha_min=amin,
            alpha_max=amax,
        )
        report[f"tile:{tname}"] = st
        print(f"[tile:{tname}] done in {st['elapsed_s']}s peak={st['peak_vram_mib']} MiB seam={st['seam_ratio']}", flush=True)

    # 2. STRUCTURES (Blender Depth + Canny + IP-Adapter -> PixelOE)
    structs_cfg = [
        (
            "bywater_inn",
            (320, 256),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo building, simple background, solid flat bright magenta #FF00FF chroma key background, isolated game building sprite, 2.5D medieval fantasy two-wing half-timbered village tavern inn, warm ivory-cream plaster walls with rich chestnut-oak timber cross-beams, warm cobblestone foundation, steep sunlit terracotta-red and warm russet-oak shingled roofs, weathered warm gray stone chimney, arched oak doorway under porch awning, wooden barrels by porch, glowing warm golden-amber windows, crisp dark slate ink outlines, bright golden-hour sunlight from upper left, Songs of Conquest Arleon village tavern style, no ground, no grass, no shadow",
            304205,
            GUIDE_DIR / "bywater_inn_depth.png",
            GUIDE_DIR / "bywater_inn_canny.png",
            0.58,
            0.44,
        ),
        (
            "stone_bridge",
            (256, 192),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo structure, simple background, solid pure white background, isolated game prop, medieval arched stone footbridge with chiseled warm gray and sandstone masonry arch, low stone parapets, end pillars, subtle green moss tufts, crisp dark slate ink outlines, warm sunlight from upper left, Songs of Conquest stone bridge style, empty white opening under arch, no water, no ground, no shadow",
            304212,
            GUIDE_DIR / "stone_bridge_depth.png",
            GUIDE_DIR / "stone_bridge_canny.png",
            0.60,
            0.48,
        ),
        (
            "stone_fence_h",
            (128, 128),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo prop, simple background, solid pure white background, isolated game prop, low horizontal medieval fieldstone garden wall with end stone pillars and wooden latch post, weathered warm gray and sandstone stones with tiny green moss flecks, crisp dark slate ink outlines, warm sunlight from upper left, Songs of Conquest style, no ground, no shadow",
            304203,
            GUIDE_DIR / "stone_fence_h_depth.png",
            GUIDE_DIR / "stone_fence_h_canny.png",
            0.55,
            0.46,
        ),
        (
            "stone_fence_v",
            (128, 128),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo prop, simple background, solid pure white background, isolated game prop, 3/4 oblique low medieval fieldstone garden wall segment with end square stone pillars, weathered warm gray and sandstone cobbles with subtle moss, crisp dark slate ink outlines, warm sunlight from upper left, Songs of Conquest village wall style, no ground, no shadow",
            304214,
            GUIDE_DIR / "stone_fence_v_depth.png",
            GUIDE_DIR / "stone_fence_v_canny.png",
            0.58,
            0.48,
        ),
    ]
    for sname, swh, sprompt, sseed, d_p, c_p, dst, cst in structs_cfg:
        if not want("structures", sname):
            continue
        d_rem = upload_image(d_p, f"carto_d_{sname}.png")
        c_rem = upload_image(c_p, f"carto_c_{sname}.png")
        s_neg = SPRITE_NEG + ", purple, violet, magenta, gray background"
        wf = build_cartography_workflow(
            prompt=sprompt,
            negative=s_neg,
            seed=sseed,
            filename_prefix=f"carto_raw_{sname}",
            lora_name="pixel-art-style-illustrious-by-skormino.safetensors",
            lora_strength=0.88,
            steps=26,
            cfg=6.5,
            ip_ref_remote=ip_town,
            ip_weight=0.22 if ip_town else 0.0,
            depth_remote=d_rem,
            depth_strength=dst,
            depth_end=0.82,
            canny_remote=c_rem,
            canny_strength=cst,
            canny_end=0.68,
        )
        raw_bytes, sec, peak = run_comfy_prompt(wf)
        raw_p = RAW_DIR / f"{sname}.png"
        raw_p.write_bytes(raw_bytes)
        st = pp.process_sprite(
            raw_p,
            UNITY_CART_ROOT / "structures" / f"{sname}.png",
            target_size=swh,
            pad_px=2,
            bottom_pivot=True,
            thickness=1,
        )
        st["elapsed_s"] = sec
        st["peak_vram_mib"] = peak
        report[f"struct:{sname}"] = st
        print(f"[struct:{sname}] done in {sec}s peak={peak} MiB colors={st['unique_palette_colors']}", flush=True)

    # 3. TREES (White background + clean Depth silhouette guides; zero forest scenery or dirt ovals)
    trees_cfg = [
        (
            "tree_oak_a",
            (224, 224),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo tree, simple background, solid pure white background, isolated RPG tree prop sprite on pure white, single lush rounded medieval oak tree centered, thick warm chestnut trunk clean cut at bottom, rich layered emerald and olive-gold sunlit leaf clusters with cool dark green shadows, crisp dark slate ink outlines, Songs of Conquest Arleon oak tree style, NO grass, NO dirt mound, NO soil patch, NO cast shadow",
            304311,
        ),
        (
            "tree_oak_b",
            (224, 224),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo tree, simple background, solid pure white background, isolated RPG tree prop sprite on pure white, twin-lobed mature woodland oak tree centered, gnarled chestnut bark trunk clean cut at bottom, deep forest-emerald and warm sunlit sage-gold leaf clusters, crisp dark ink outlines, Songs of Conquest tree style, NO grass, NO dirt mound, NO soil patch, NO cast shadow",
            304312,
        ),
        (
            "tree_pine_a",
            (192, 256),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo tree, simple background, solid pure white background, isolated RPG tree prop sprite on pure white, tall pointed alpine conifer spruce pine tree centered, narrow brown trunk clean cut at bottom, tiered deep emerald and sage-teal pine needle boughs catching warm sunlight on upper left, crisp dark slate outlines, Songs of Conquest pine tree style, NO grass, NO dirt mound, NO snow, NO shadow",
            304313,
        ),
        (
            "tree_pine_b",
            (192, 256),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo tree, simple background, solid pure white background, isolated RPG tree prop sprite on pure white, slender ancient evergreen spruce pine tree centered with layered dark forest green and emerald boughs, narrow bark trunk clean cut at bottom, crisp dark ink outlines, Songs of Conquest conifer style, NO grass, NO dirt mound, NO shadow",
            304314,
        ),
        (
            "tree_willow_a",
            (256, 256),
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo tree, simple background, solid pure white background, isolated RPG tree prop sprite on pure white, graceful riverside weeping willow tree centered, sturdy twisted oak-brown trunk clean cut at bottom, cascading hanging emerald and olive-lime willow vine leaves catching golden-hour sunlight, crisp dark ink outlines, Songs of Conquest willow tree style, NO grass, NO dirt mound, NO water, NO shadow",
            304315,
        ),
    ]
    for tr_name, tr_wh, tr_prompt, tr_seed in trees_cfg:
        if not want("trees", tr_name):
            continue
        td_rem = upload_image(GUIDE_DIR / f"{tr_name}_depth.png", f"carto_d_{tr_name}.png")
        wf = build_cartography_workflow(
            prompt=tr_prompt,
            negative=SPRITE_NEG + ", forest, woods, sky, path, landscape, scenery, dirt oval, soil mound, roots in dirt, ground circle, multiple trees, magenta, purple",
            seed=tr_seed,
            filename_prefix=f"carto_raw_{tr_name}",
            lora_name="pixel-art-style-illustrious-by-skormino.safetensors",
            lora_strength=0.88,
            steps=26,
            cfg=6.5,
            ip_ref_remote=None,
            ip_weight=0.0,
            depth_remote=td_rem,
            depth_strength=0.60,
            depth_end=0.80,
        )
        raw_bytes, sec, peak = run_comfy_prompt(wf)
        raw_p = RAW_DIR / f"{tr_name}.png"
        raw_p.write_bytes(raw_bytes)
        out_albedo = UNITY_CART_ROOT / "trees" / f"{tr_name}.png"
        st = pp.process_sprite(
            raw_p,
            out_albedo,
            target_size=tr_wh,
            pad_px=3,
            bottom_pivot=True,
            thickness=1,
        )
        # Extra clean guard: trim any floating dirt mound wider than trunk base
        im_arr = np.array(Image.open(out_albedo).convert("RGBA"))
        trimmed = strip_tree_bottom_dirt_oval(im_arr)
        if not np.array_equal(trimmed, im_arr):
            Image.fromarray(trimmed, "RGBA").save(out_albedo, "PNG")
            norm_arr, _ = pp.generate_normal_map(trimmed, periodic=False, normal_backend="auto")
            Image.fromarray(norm_arr, "RGBA").save(out_albedo.with_name(f"{out_albedo.stem}_n.png"), "PNG")
        st["elapsed_s"] = sec
        st["peak_vram_mib"] = peak
        report[f"tree:{tr_name}"] = st
        print(f"[tree:{tr_name}] done in {sec}s peak={peak} MiB colors={st['unique_palette_colors']}", flush=True)

    # 4. ANIMATED 4-FRAME UNITS (64 px/m: ~96px tall in 128x128 frame -> 512x128 strip)
    if want("units", "knight"):
        st = build_unit_strip(
            "knight",
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo, simple background, solid pure white background, isolated RPG hero sprite, full body head to boots centered, 3/4 tactical RPG sprite, gleaming polished bright silver steel plate armor with sunlit gold trim, vibrant scarlet crimson flowing cloak, royal cobalt and gold kite shield, shining steel broadsword held upright in hand, crested silver knight helmet, crisp dark slate ink outlines, warm sunlit rim light, Songs of Conquest hero style, no floor, no cast shadow",
            GUIDE_DIR / "knight_f0_depth.png",
            None,
            304411,
            sprite_height_px=96,
            out_albedo=UNITY_CART_ROOT / "units" / "knight.png",
            synth_fn=synthesize_knight_idle_frames,
        )
        report["unit:knight"] = st
        print(f"[unit:knight] done in {st['elapsed_s']}s peak={st['peak_vram_mib']} MiB", flush=True)

    if want("units", "giant_frog"):
        st = build_unit_strip(
            "giant_frog",
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo monster, simple background, solid pure white background, isolated RPG monster sprite, chunky crouching giant mossy-emerald fantasy battle toad frog with warm ochre-tan belly and bright golden amber eyes, crisp dark slate ink outlines, warm sunlit rim light, Songs of Conquest monster style, no floor, no cast shadow",
            GUIDE_DIR / "giant_frog_f0_depth.png",
            None,
            304412,
            sprite_height_px=84,
            out_albedo=UNITY_CART_ROOT / "units" / "giant_frog.png",
            synth_fn=synthesize_frog_idle_frames,
        )
        report["unit:giant_frog"] = st
        print(f"[unit:giant_frog] done in {st['elapsed_s']}s peak={st['peak_vram_mib']} MiB", flush=True)

    make_contact_sheets()
    rep_path = SCRATCH_ROOT / "phase1_generation_report.json"
    rep_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Report written:", rep_path)


if __name__ == "__main__":
    main()
