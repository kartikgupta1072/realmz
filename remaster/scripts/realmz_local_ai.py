#!/usr/bin/env python3
"""Realmz HD-2D Local GPU & VRAM Coordinator CLI.

Stdlib-only wrapper supporting standalone llama.cpp (primary zero-daemon GPU
backend reusing local GGUF models or Ollama blob stores with instant VRAM
release) and optional Ollama fallback, with strict 8 GB RTX 5060 Ti VRAM
discipline and zero em/en-dash output enforcement.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

REALMZ_ROOT = Path(os.environ.get("REALMZ_ROOT", r"C:\Users\karti\realmz"))
OLLAMA_URL = os.environ.get("REALMZ_OLLAMA_URL", "http://127.0.0.1:11434").rstrip("/")
COMFY_URL = os.environ.get("REALMZ_COMFY_URL", "http://127.0.0.1:8188").rstrip("/")

LLAMA_CPP_DIR = Path(os.environ.get("REALMZ_LLAMA_CPP_DIR", r"C:\Users\karti\.local\tools\llama.cpp"))
GGUF_DIR = Path(os.environ.get("REALMZ_GGUF_DIR", r"C:\Users\karti\.local\models\gguf"))
OLLAMA_MODELS_DIR = Path(os.environ.get("REALMZ_OLLAMA_MODELS_DIR", r"C:\Users\karti\.ollama\models"))
DEFAULT_BACKEND = os.environ.get("REALMZ_LOCAL_AI_BACKEND", "auto").strip().lower() or "auto"

FAST_MODEL = os.environ.get("REALMZ_LOCAL_FAST_MODEL", "qwen2.5:7b-instruct")
HEAVY_MODEL = os.environ.get("REALMZ_LOCAL_HEAVY_MODEL", "qwen2.5:14b-instruct")
VISION_MODEL = os.environ.get("REALMZ_LOCAL_VISION_MODEL", "qwen2.5vl:3b")

STATS_PATH = REALMZ_ROOT / ".scratch" / "runs" / "local_ai_stats.json"

LOG_SIGNAL_RE = re.compile(
    r"\b(error|exception|failed|fail|assert|shader error|warning|stacktrace|summary|test-run|nunit|nullreferenceexception)\b",
    re.IGNORECASE,
)
LOG_HARD_PROBLEM_RE = re.compile(
    r"\b(error|exception|failed|assert|shader error|warning|nullreferenceexception)\b",
    re.IGNORECASE,
)

VISION_PROMPTS = {
    "proof": (
        "Inspect this Unity HD-2D tactical RPG screenshot for visual defects: "
        "horizontal skybox or elevation cracks, clipped or overlapping HUD text, "
        "unreadable pitch-black water with white speckles, broken sprite scaling, or missing textures. "
        "Respond ONLY with JSON matching: "
        '{"pass": bool, "severity": "NONE"|"P2"|"P1"|"P0", "defects": [string], "summary": string}.'
    ),
    "sprite": (
        "Inspect this 64 px/m pixel-art RPG sprite for quality defects: "
        "interior alpha holes on dark roofs or foliage, cropped limbs or roofs, "
        "magenta or bright fringe halos, or perspective distortion. "
        "Respond ONLY with JSON matching: "
        '{"pass": bool, "severity": "NONE"|"P2"|"P1"|"P0", "defects": [string], "summary": string}.'
    ),
    "sheet": (
        "Inspect this RPG sprite sheet or contact sheet for defects: "
        "frame-to-frame drift, inconsistent scale across cells, corrupted tiles, or cutoff edges. "
        "Respond ONLY with JSON matching: "
        '{"pass": bool, "severity": "NONE"|"P2"|"P1"|"P0", "defects": [string], "summary": string}.'
    ),
}


def sanitize_dashes(value: Any) -> Any:
    """Recursively replace em-dashes and en-dashes with ASCII equivalents."""
    if isinstance(value, str):
        return value.replace("\u2014", " - ").replace("\u2013", "-")
    if isinstance(value, list):
        return [sanitize_dashes(item) for item in value]
    if isinstance(value, dict):
        return {k: sanitize_dashes(v) for k, v in value.items()}
    return value


def http_json(url: str, payload: dict[str, Any] | None = None, timeout: float = 10.0) -> dict[str, Any]:
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    req = urllib.request.Request(url, data=data, headers=headers, method="POST" if data is not None else "GET")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        raw = resp.read().decode("utf-8", errors="replace")
    return json.loads(raw) if raw.strip() else {}


def query_nvidia_smi() -> dict[str, Any] | None:
    try:
        res = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=name,memory.total,memory.used,memory.free",
                "--format=csv,noheader,nounits",
            ],
            capture_output=True,
            text=True,
            timeout=5,
            creationflags=CREATE_NO_WINDOW,
        )
        if res.returncode != 0 or not res.stdout.strip():
            return None
        line = res.stdout.strip().splitlines()[0]
        parts = [p.strip() for p in line.split(",")]
        if len(parts) < 4:
            return None
        return {
            "name": parts[0],
            "memory_total_mib": int(float(parts[1])),
            "memory_used_mib": int(float(parts[2])),
            "memory_free_mib": int(float(parts[3])),
        }
    except Exception:
        return None


def find_llama_binary(names: tuple[str, ...] = ("llama-completion.exe", "llama-cli.exe", "llama-completion", "llama-cli")) -> Path | None:
    """Find a llama.cpp CLI binary inside LLAMA_CPP_DIR or PATH."""
    for name in names:
        candidate = LLAMA_CPP_DIR / name
        if candidate.is_file():
            return candidate
    for name in names:
        found = shutil.which(name)
        if found:
            return Path(found)
    return None


def _resolve_ollama_manifest_blobs(model_spec: str, ollama_dir: Path) -> tuple[Path | None, Path | None]:
    manifests_root = ollama_dir / "manifests"
    blobs_root = ollama_dir / "blobs"
    if not manifests_root.is_dir() or not blobs_root.is_dir():
        return None, None

    cleaned = model_spec.strip()
    candidates: list[Path] = []
    if "/" in cleaned:
        repo_part, tag_part = (cleaned.split(":", 1) + ["latest"])[:2]
        candidates.append(manifests_root / Path(repo_part) / tag_part)
    else:
        repo_part, tag_part = (cleaned.split(":", 1) + ["latest"])[:2]
        candidates.append(manifests_root / "registry.ollama.ai" / "library" / repo_part / tag_part)

    manifest_path = next((p for p in candidates if p.is_file()), None)
    if manifest_path is None:
        return None, None

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8", errors="replace"))
    except Exception:
        return None, None

    model_blob: Path | None = None
    proj_blob: Path | None = None
    for layer in manifest.get("layers") or []:
        if not isinstance(layer, dict):
            continue
        media = str(layer.get("mediaType") or "")
        digest = str(layer.get("digest") or "")
        if not digest.startswith("sha256:"):
            continue
        blob_file = blobs_root / f"sha256-{digest.split(':', 1)[1]}"
        if not blob_file.is_file():
            continue
        if media.endswith(".model"):
            model_blob = blob_file
        elif media.endswith(".projector") or "mmproj" in media:
            proj_blob = blob_file
    return model_blob, proj_blob


def resolve_gguf_model(
    model_spec: str,
    gguf_dir: Path | None = None,
    ollama_models_dir: Path | None = None,
) -> tuple[Path | None, Path | None]:
    """Resolve a model alias or file path to (model_gguf, mmproj_gguf)."""
    if not model_spec:
        return None, None

    direct = Path(model_spec)
    if direct.is_file():
        return direct, None

    gdir = gguf_dir if gguf_dir is not None else GGUF_DIR
    odir = ollama_models_dir if ollama_models_dir is not None else OLLAMA_MODELS_DIR

    tokens = [t for t in re.split(r"[^a-z0-9.]+", model_spec.lower()) if t and t != "instruct"]
    if gdir.is_dir():
        ggufs = sorted([p for p in gdir.rglob("*.gguf") if p.is_file()])
        model_match: Path | None = None
        mmproj_match: Path | None = None
        for p in ggufs:
            low = p.name.lower()
            is_mmproj = "mmproj" in low
            if tokens and all(tok in low for tok in tokens):
                if is_mmproj and mmproj_match is None:
                    mmproj_match = p
                elif not is_mmproj and model_match is None:
                    model_match = p
        if model_match is not None:
            if mmproj_match is None and tokens:
                base_tok = tokens[0]
                for p in ggufs:
                    low = p.name.lower()
                    if "mmproj" in low and base_tok in low:
                        mmproj_match = p
                        break
            return model_match, mmproj_match

    return _resolve_ollama_manifest_blobs(model_spec, odir)


def _format_qwen_chatml(messages: list[dict[str, Any]], json_mode: bool = False) -> str:
    parts: list[str] = []
    has_system = any(m.get("role") == "system" for m in messages)
    if not has_system:
        sys_text = (
            "Respond strictly with valid JSON and zero markdown fences. Use strictly ASCII hyphens (no em-dashes or en-dashes)."
            if json_mode
            else "Follow instructions concisely using strictly ASCII punctuation (no em-dashes or en-dashes)."
        )
        parts.append(f"<|im_start|>system\n{sys_text}<|im_end|>\n")
    for msg in messages:
        role = str(msg.get("role") or "user")
        content = str(msg.get("content") or "")
        parts.append(f"<|im_start|>{role}\n{content}<|im_end|>\n")
    parts.append("<|im_start|>assistant\n")
    return "".join(parts)


def _clean_llamacpp_output(raw_text: str) -> str:
    cleaned = raw_text.replace("\r\n", "\n").strip()
    for stop_tok in ("[end of text]", "<|im_end|>", "<|endoftext|>"):
        if cleaned.endswith(stop_tok):
            cleaned = cleaned[: -len(stop_tok)].strip()
        cleaned = cleaned.replace(stop_tok, "")
    return sanitize_dashes(cleaned.strip())


def llamacpp_chat(
    model_path: Path,
    messages: list[dict[str, Any]],
    num_ctx: int = 8192,
    max_tokens: int = 1024,
    temperature: float = 0.1,
    timeout: float = 180.0,
    json_mode: bool = False,
) -> str:
    binary = find_llama_binary(("llama-completion.exe", "llama-cli.exe", "llama-completion", "llama-cli"))
    if binary is None:
        raise RuntimeError(f"llama.cpp binary not found in {LLAMA_CPP_DIR}")

    prompt_text = _format_qwen_chatml(messages, json_mode=json_mode)
    tmp_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".txt", delete=False) as tf:
            tf.write(prompt_text)
            tmp_path = Path(tf.name)

        cmd = [
            str(binary),
            "-m",
            str(model_path),
            "--fit",
            "on",
            "-fitt",
            "512",
            "-no-cnv",
            "--no-display-prompt",
            "-c",
            str(num_ctx),
            "-n",
            str(max_tokens),
            "--temp",
            str(temperature),
            "-r",
            "<|im_end|>",
            "-f",
            str(tmp_path),
        ]
        res = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
            creationflags=CREATE_NO_WINDOW,
        )
        if res.returncode != 0:
            err_tail = (res.stderr or res.stdout or "").strip()[-400:]
            raise RuntimeError(f"llama.cpp failed (exit={res.returncode}): {err_tail}")
        return _clean_llamacpp_output(res.stdout or "")
    finally:
        if tmp_path is not None:
            try:
                tmp_path.unlink(missing_ok=True)
            except Exception:
                pass


def llamacpp_vision_chat(
    model_path: Path,
    mmproj_path: Path,
    image_path: Path,
    prompt: str,
    num_ctx: int = 4096,
    max_tokens: int = 512,
    temperature: float = 0.1,
    timeout: float = 120.0,
) -> str:
    binary = find_llama_binary(("llama-mtmd-cli.exe", "llama-mtmd-cli"))
    if binary is None:
        raise RuntimeError(f"llama-mtmd-cli not found in {LLAMA_CPP_DIR}")

    cmd = [
        str(binary),
        "-m",
        str(model_path),
        "--mmproj",
        str(mmproj_path),
        "--image",
        str(image_path),
        "-c",
        str(num_ctx),
        "-n",
        str(max_tokens),
        "--temp",
        str(temperature),
        "-p",
        prompt,
    ]
    res = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode != 0:
        err_tail = (res.stderr or res.stdout or "").strip()[-400:]
        raise RuntimeError(f"llama-mtmd-cli failed (exit={res.returncode}): {err_tail}")
    return _clean_llamacpp_output(res.stdout or "")


def get_loaded_ollama_models(base_url: str = OLLAMA_URL) -> list[str]:
    data = http_json(f"{base_url}/api/ps", timeout=4.0)
    models = data.get("models") or []
    names: list[str] = []
    for item in models:
        name = item.get("name") or item.get("model")
        if isinstance(name, str) and name:
            names.append(name)
    return names


def is_ollama_online(base_url: str = OLLAMA_URL) -> bool:
    try:
        http_json(f"{base_url}/api/tags", timeout=1.5)
        return True
    except Exception:
        return False


def evict_ollama_models(base_url: str = OLLAMA_URL) -> list[str]:
    """Unload any VRAM-resident Ollama models if Ollama is reachable."""
    try:
        loaded = get_loaded_ollama_models(base_url)
    except Exception:
        return []
    unloaded: list[str] = []
    for name in loaded:
        try:
            http_json(
                f"{base_url}/api/generate",
                payload={"model": name, "prompt": "", "keep_alive": 0, "stream": False},
                timeout=15.0,
            )
            unloaded.append(name)
        except Exception:
            pass
    return unloaded


def record_stats(command: str, model: str, input_chars: int, output_chars: int, backend: str = "llamacpp") -> None:
    try:
        STATS_PATH.parent.mkdir(parents=True, exist_ok=True)
        stats: dict[str, Any] = {"calls": 0, "input_chars": 0, "output_chars": 0, "by_command": {}}
        if STATS_PATH.is_file():
            loaded = json.loads(STATS_PATH.read_text(encoding="utf-8"))
            if isinstance(loaded, dict):
                stats.update(loaded)
        stats["calls"] = int(stats.get("calls", 0)) + 1
        stats["input_chars"] = int(stats.get("input_chars", 0)) + int(input_chars)
        stats["output_chars"] = int(stats.get("output_chars", 0)) + int(output_chars)
        by_cmd = stats.setdefault("by_command", {})
        entry = by_cmd.setdefault(command, {"calls": 0, "last_model": model, "last_backend": backend})
        entry["calls"] = int(entry.get("calls", 0)) + 1
        entry["last_model"] = model
        entry["last_backend"] = backend
        stats["last_updated"] = time.strftime("%Y-%m-%d %H:%M:%S")
        STATS_PATH.write_text(json.dumps(sanitize_dashes(stats), indent=2), encoding="utf-8")
    except Exception:
        pass


def ollama_chat(
    model: str,
    messages: list[dict[str, Any]],
    keep_alive: int | str = 0,
    num_ctx: int = 8192,
    timeout: float = 60.0,
    json_mode: bool = False,
    temperature: float = 0.1,
    base_url: str = OLLAMA_URL,
) -> str:
    payload: dict[str, Any] = {
        "model": model,
        "messages": messages,
        "stream": False,
        "keep_alive": keep_alive,
        "options": {"temperature": temperature, "num_ctx": num_ctx},
    }
    if json_mode:
        payload["format"] = "json"
    resp = http_json(f"{base_url}/api/chat", payload=payload, timeout=timeout)
    msg = resp.get("message") or {}
    content = msg.get("content") or ""
    return sanitize_dashes(str(content).strip())


def run_local_chat(
    model: str,
    messages: list[dict[str, Any]],
    backend: str = DEFAULT_BACKEND,
    keep_alive: int | str = 0,
    num_ctx: int = 8192,
    max_tokens: int = 1024,
    timeout: float = 180.0,
    json_mode: bool = False,
    temperature: float = 0.1,
) -> tuple[str, str]:
    """Execute text inference via llama.cpp (primary) or Ollama fallback."""
    chosen = (backend or DEFAULT_BACKEND).lower()
    if chosen in ("auto", "llamacpp"):
        llama_bin = find_llama_binary()
        model_gguf, _ = resolve_gguf_model(model)
        if llama_bin is not None and model_gguf is not None:
            raw = llamacpp_chat(
                model_path=model_gguf,
                messages=messages,
                num_ctx=num_ctx,
                max_tokens=max_tokens,
                temperature=temperature,
                timeout=timeout,
                json_mode=json_mode,
            )
            return sanitize_dashes(raw), "llamacpp"
        if chosen == "llamacpp":
            raise RuntimeError(
                f"llama.cpp requested but missing binary={llama_bin} or GGUF for '{model}' "
                f"(checked {GGUF_DIR} and {OLLAMA_MODELS_DIR})."
            )

    raw = ollama_chat(
        model=model,
        messages=messages,
        keep_alive=keep_alive,
        num_ctx=num_ctx,
        timeout=timeout,
        json_mode=json_mode,
        temperature=temperature,
    )
    return raw, "ollama"


def parse_json_content(raw_text: str, fallback: dict[str, Any]) -> dict[str, Any]:
    cleaned = raw_text.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:json)?\s*|\s*```$", "", cleaned, flags=re.DOTALL).strip()
    try:
        parsed = json.loads(cleaned)
        if isinstance(parsed, dict):
            return sanitize_dashes(parsed)
    except Exception:
        m = re.search(r"\{.*\}", cleaned, flags=re.DOTALL)
        if m:
            try:
                parsed = json.loads(m.group(0))
                if isinstance(parsed, dict):
                    return sanitize_dashes(parsed)
            except Exception:
                pass
    return sanitize_dashes(fallback)


def filter_log_lines(text: str, max_lines: int = 180) -> tuple[list[str], int, int]:
    """Deterministically condense huge Unity/build logs into high-signal lines."""
    all_lines = text.splitlines()
    total_lines = len(all_lines)
    matched: list[str] = []
    seen_idx: set[int] = set()
    hard_count = 0

    for idx, line in enumerate(all_lines):
        if LOG_HARD_PROBLEM_RE.search(line):
            hard_count += 1
        if LOG_SIGNAL_RE.search(line):
            seen_idx.add(idx)
            matched.append(f"L{idx + 1}: {line.rstrip()}")

    tail_start = max(0, total_lines - 25)
    tail_lines: list[str] = []
    for idx in range(tail_start, total_lines):
        if idx not in seen_idx:
            tail_lines.append(f"L{idx + 1}: {all_lines[idx].rstrip()}")

    if len(matched) + len(tail_lines) > max_lines:
        matched_budget = max(20, max_lines - min(25, len(tail_lines)))
        if len(matched) > matched_budget:
            head_n = matched_budget // 2
            tail_n = matched_budget - head_n
            matched = matched[:head_n] + ["... [truncated middle signal lines] ..."] + matched[-tail_n:]
    combined = (matched + ["--- LOG TAIL ---"] + tail_lines)[:max_lines]
    return combined, total_lines, hard_count


def cmd_status(args: argparse.Namespace) -> int:
    llama_bin = find_llama_binary()
    mtmd_bin = find_llama_binary(("llama-mtmd-cli.exe", "llama-mtmd-cli"))
    fast_gguf, _ = resolve_gguf_model(FAST_MODEL)
    heavy_gguf, _ = resolve_gguf_model(HEAVY_MODEL)
    vision_gguf, vision_mmproj = resolve_gguf_model(VISION_MODEL)

    ollama_ok = False
    installed: list[str] = []
    resident: list[str] = []
    try:
        tags = http_json(f"{OLLAMA_URL}/api/tags", timeout=1.5)
        installed = [m.get("name", "") for m in (tags.get("models") or []) if m.get("name")]
        resident = get_loaded_ollama_models(OLLAMA_URL)
        ollama_ok = True
    except Exception:
        ollama_ok = False

    comfy_ok = False
    try:
        http_json(f"{COMFY_URL}/system_stats", timeout=1.5)
        comfy_ok = True
    except Exception:
        comfy_ok = False

    gpu = query_nvidia_smi()
    out = sanitize_dashes(
        {
            "preferred_backend": getattr(args, "backend", DEFAULT_BACKEND),
            "llamacpp_ready": llama_bin is not None and (fast_gguf is not None or heavy_gguf is not None),
            "llamacpp_binary": str(llama_bin) if llama_bin else "",
            "llamacpp_vision_binary": str(mtmd_bin) if mtmd_bin else "",
            "resolved_ggufs": {
                "fast": str(fast_gguf) if fast_gguf else "",
                "heavy": str(heavy_gguf) if heavy_gguf else "",
                "vision": str(vision_gguf) if vision_gguf else "",
                "vision_mmproj": str(vision_mmproj) if vision_mmproj else "",
            },
            "gguf_dir": str(GGUF_DIR),
            "ollama_online": ollama_ok,
            "ollama_url": OLLAMA_URL,
            "installed_models": installed,
            "resident_vram_models": resident,
            "comfyui_online": comfy_ok,
            "comfyui_url": COMFY_URL,
            "gpu": gpu,
            "tier_models": {"fast": FAST_MODEL, "heavy": HEAVY_MODEL, "vision": VISION_MODEL},
        }
    )
    if args.json:
        print(json.dumps(out, indent=2))
    else:
        print(f"llama.cpp: {'READY' if out['llamacpp_ready'] else 'MISSING'} ({llama_bin or LLAMA_CPP_DIR})")
        print(f"GGUF fast: {fast_gguf or 'not found'} ({FAST_MODEL})")
        print(f"GGUF hvy : {heavy_gguf or 'not found'} ({HEAVY_MODEL})")
        print(f"Vision   : mtmd={mtmd_bin is not None}, mmproj={vision_mmproj or 'not found (Ollama fallback if online)'}")
        print(f"Ollama   : {'ONLINE (fallback)' if ollama_ok else 'OFFLINE (not required)'} | resident={', '.join(resident) if resident else 'none'}")
        print(f"ComfyUI  : {'ONLINE' if comfy_ok else 'OFFLINE'} ({COMFY_URL})")
        if gpu:
            print(
                f"GPU      : {gpu['name']} ({gpu['memory_used_mib']} / {gpu['memory_total_mib']} MiB used, "
                f"{gpu['memory_free_mib']} MiB free)"
            )
    return 0


def cmd_evict(args: argparse.Namespace) -> int:
    unloaded = evict_ollama_models(OLLAMA_URL)
    if unloaded:
        time.sleep(0.5)
    gpu = query_nvidia_smi()
    out = sanitize_dashes({"mode": "llamacpp-oneshot (no persistent daemon)", "unloaded_ollama": unloaded, "gpu": gpu})
    if args.json:
        print(json.dumps(out, indent=2))
    else:
        print(f"llama.cpp: one-shot (0 resident VRAM after exit)")
        print(f"Ollama   : unloaded {', '.join(unloaded) if unloaded else 'none'}")
        if gpu:
            print(f"GPU Free : {gpu['memory_free_mib']} / {gpu['memory_total_mib']} MiB")
    return 0


def collect_vision_targets(target: Path, max_images: int) -> list[Path]:
    if target.is_file():
        return [target]
    if target.is_dir():
        imgs = [p for p in target.iterdir() if p.is_file() and p.suffix.lower() in (".png", ".jpg", ".jpeg")]
        imgs.sort(key=lambda p: p.stat().st_mtime, reverse=True)
        return imgs[: max(1, max_images)]
    raise FileNotFoundError(f"Vision QA target not found: {target}")


def cmd_vision_qa(args: argparse.Namespace) -> int:
    target = Path(args.path)
    targets = collect_vision_targets(target, args.max_images)
    prompt = args.prompt.strip() if args.prompt else VISION_PROMPTS[args.mode]
    backend_pref = (getattr(args, "backend", DEFAULT_BACKEND) or DEFAULT_BACKEND).lower()

    mtmd_bin = find_llama_binary(("llama-mtmd-cli.exe", "llama-mtmd-cli"))
    vision_gguf, vision_mmproj = resolve_gguf_model(VISION_MODEL)
    use_llamacpp_vision = (
        backend_pref in ("auto", "llamacpp")
        and mtmd_bin is not None
        and vision_gguf is not None
        and vision_mmproj is not None
    )

    if backend_pref == "llamacpp" and not use_llamacpp_vision:
        raise RuntimeError(
            f"llama-mtmd-cli vision requires both model GGUF and mmproj GGUF (drop an mmproj-*.gguf in {GGUF_DIR})."
        )

    is_batch = len(targets) > 1
    keep_alive: int | str = "30s" if is_batch else 0
    results: list[dict[str, Any]] = []
    has_blocking = False
    used_backend = "llamacpp" if use_llamacpp_vision else "ollama"
    try:
        for img_path in targets:
            if use_llamacpp_vision and vision_gguf is not None and vision_mmproj is not None:
                raw = llamacpp_vision_chat(
                    model_path=vision_gguf,
                    mmproj_path=vision_mmproj,
                    image_path=img_path,
                    prompt=prompt,
                    num_ctx=4096,
                    max_tokens=384,
                    temperature=0.1,
                    timeout=90.0,
                )
            else:
                b64 = base64.b64encode(img_path.read_bytes()).decode("ascii")
                messages = [{"role": "user", "content": prompt, "images": [b64]}]
                raw = ollama_chat(
                    model=VISION_MODEL,
                    messages=messages,
                    keep_alive=keep_alive,
                    num_ctx=4096,
                    timeout=60.0,
                    json_mode=True,
                    temperature=0.1,
                )
            parsed = parse_json_content(
                raw,
                fallback={
                    "pass": False,
                    "severity": "P2",
                    "defects": ["Unparsed model output"],
                    "summary": raw[:240],
                },
            )
            severity = str(parsed.get("severity", "NONE")).upper()
            if severity in ("P0", "P1"):
                has_blocking = True
            entry = {
                "file": str(img_path),
                "name": img_path.name,
                "pass": bool(parsed.get("pass", True)),
                "severity": severity,
                "defects": parsed.get("defects") if isinstance(parsed.get("defects"), list) else [],
                "summary": str(parsed.get("summary", "")),
            }
            results.append(sanitize_dashes(entry))
            record_stats("vision-qa", VISION_MODEL, len(prompt), len(raw), backend=used_backend)
    finally:
        if is_batch and not use_llamacpp_vision:
            evict_ollama_models(OLLAMA_URL)

    payload = sanitize_dashes({"mode": args.mode, "backend": used_backend, "model": VISION_MODEL, "count": len(results), "results": results})
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        for r in results:
            defects = "; ".join(r["defects"]) if r["defects"] else "none"
            print(f"[{r['severity']}] {r['name']} | pass={r['pass']} | {r['summary']} | defects={defects}")

    if args.fail_on_defect and has_blocking:
        return 2
    return 0


def cmd_log_triage(args: argparse.Namespace) -> int:
    log_path = Path(args.log_path)
    if not log_path.is_file():
        raise FileNotFoundError(f"Log file not found: {log_path}")

    raw_text = log_path.read_text(encoding="utf-8", errors="replace")
    excerpt_lines, total_lines, hard_count = filter_log_lines(raw_text, max_lines=args.max_lines)
    excerpt = "\n".join(excerpt_lines)

    if hard_count == 0:
        out = sanitize_dashes(
            {
                "log_path": str(log_path),
                "total_lines": total_lines,
                "backend": "deterministic",
                "model": "deterministic-fastpath",
                "status": "CLEAN",
                "error_count_estimate": 0,
                "root_causes": [],
                "warnings": [],
                "next_action": "No errors, exceptions, or warnings matched in log.",
            }
        )
        print(json.dumps(out, indent=2) if args.json else f"[CLEAN] {log_path.name} ({total_lines} lines): no errors/warnings.")
        return 0

    model = HEAVY_MODEL if args.tier == "heavy" else FAST_MODEL
    timeout = 180.0 if args.tier == "heavy" else 60.0
    prompt = (
        "You are triaging a Unity 6 / C# build or test log for Realmz. "
        "Analyze ONLY the extracted log lines below and respond with JSON matching: "
        '{"status": "CLEAN"|"WARNINGS"|"ERRORS", "error_count_estimate": int, '
        '"root_causes": [string], "warnings": [string], "next_action": string}. '
        "Use strictly ASCII punctuation (no em-dashes or en-dashes).\n\n"
        f"{excerpt}"
    )
    raw_resp, used_backend = run_local_chat(
        model=model,
        messages=[{"role": "user", "content": prompt}],
        backend=getattr(args, "backend", DEFAULT_BACKEND),
        keep_alive=0,
        num_ctx=8192,
        max_tokens=768,
        timeout=timeout,
        json_mode=True,
        temperature=0.1,
    )
    record_stats("log-triage", model, len(prompt), len(raw_resp), backend=used_backend)
    parsed = parse_json_content(
        raw_resp,
        fallback={
            "status": "WARNINGS",
            "error_count_estimate": hard_count,
            "root_causes": [],
            "warnings": [raw_resp[:240]],
            "next_action": "Inspect raw log.",
        },
    )
    out = sanitize_dashes(
        {
            "log_path": str(log_path),
            "total_lines": total_lines,
            "excerpt_lines": len(excerpt_lines),
            "backend": used_backend,
            "model": model,
            "status": parsed.get("status", "WARNINGS"),
            "error_count_estimate": parsed.get("error_count_estimate", hard_count),
            "root_causes": parsed.get("root_causes", []),
            "warnings": parsed.get("warnings", []),
            "next_action": parsed.get("next_action", ""),
        }
    )
    if args.json:
        print(json.dumps(out, indent=2))
    else:
        print(f"[{out['status']}] {log_path.name} ({total_lines} lines -> {len(excerpt_lines)} excerpt lines via {used_backend}/{model})")
        for rc in out["root_causes"]:
            print(f"  root_cause: {rc}")
        for w in out["warnings"]:
            print(f"  warning   : {w}")
        print(f"  next      : {out['next_action']}")
    return 0


def cmd_summarize(args: argparse.Namespace) -> int:
    file_path = Path(args.file_path)
    if not file_path.is_file():
        raise FileNotFoundError(f"File not found: {file_path}")

    raw_text = file_path.read_text(encoding="utf-8", errors="replace")
    truncated = len(raw_text) > args.max_chars
    snippet = raw_text[: args.max_chars]
    focus = f" Focus specifically on: {args.focus}." if args.focus else ""
    trunc_note = f"\n[Note: file truncated from {len(raw_text)} to {args.max_chars} chars.]" if truncated else ""

    model = HEAVY_MODEL if args.tier == "heavy" else FAST_MODEL
    timeout = 180.0 if args.tier == "heavy" else 60.0
    prompt = (
        "Summarize the following file accurately and concisely in bullet points with exact symbol/function names. "
        "Do not invent symbols not shown. Use strictly ASCII hyphens (zero em-dashes or en-dashes)."
        f"{focus}{trunc_note}\n\n{snippet}"
    )
    summary, used_backend = run_local_chat(
        model=model,
        messages=[{"role": "user", "content": prompt}],
        backend=getattr(args, "backend", DEFAULT_BACKEND),
        keep_alive=0,
        num_ctx=8192,
        max_tokens=768,
        timeout=timeout,
        json_mode=False,
        temperature=0.1,
    )
    record_stats("summarize", model, len(prompt), len(summary), backend=used_backend)
    out = sanitize_dashes(
        {
            "file_path": str(file_path),
            "backend": used_backend,
            "model": model,
            "truncated": truncated,
            "summary": summary,
        }
    )
    print(json.dumps(out, indent=2) if args.json else out["summary"])
    return 0


def cmd_draft_content(args: argparse.Namespace) -> int:
    model = HEAVY_MODEL if args.tier == "heavy" else FAST_MODEL
    timeout = 180.0 if args.tier == "heavy" else 60.0
    prompt = (
        f"Draft {args.count} concise {args.kind} entries for Realmz HD-2D, a tactical survival RPG. "
        "Tone: grounded, crisp classic fantasy RPG (no fluff, strictly ASCII hyphens, zero em-dashes or en-dashes). "
        f"Spec: {args.spec}\n"
        'Respond ONLY with JSON: {"kind": string, "entries": [{"title": string, "text": string}]}.'
    )
    raw, used_backend = run_local_chat(
        model=model,
        messages=[{"role": "user", "content": prompt}],
        backend=getattr(args, "backend", DEFAULT_BACKEND),
        keep_alive=0,
        num_ctx=4096,
        max_tokens=768,
        timeout=timeout,
        json_mode=True,
        temperature=0.4,
    )
    record_stats("draft-content", model, len(prompt), len(raw), backend=used_backend)
    parsed = parse_json_content(raw, fallback={"kind": args.kind, "entries": [{"title": args.kind, "text": raw}]})
    parsed["backend"] = used_backend
    parsed["model"] = model
    if args.json:
        print(json.dumps(parsed, indent=2))
    else:
        for entry in parsed.get("entries") or []:
            print(f"- {entry.get('title', '')}: {entry.get('text', '')}")
    return 0


def _add_backend_arg(p: argparse.ArgumentParser) -> None:
    p.add_argument(
        "--backend",
        choices=("auto", "llamacpp", "ollama"),
        default=DEFAULT_BACKEND,
        help="Inference backend: auto (prefers standalone llama.cpp), llamacpp, or ollama",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Realmz Local GPU & VRAM Coordinator CLI")
    sub = parser.add_subparsers(dest="command", required=True)

    p_status = sub.add_parser("status", help="Show llama.cpp, GGUF, optional Ollama, ComfyUI, and GPU VRAM status")
    _add_backend_arg(p_status)
    p_status.add_argument("--json", action="store_true")
    p_status.set_defaults(func=cmd_status)

    p_evict = sub.add_parser("evict", help="Report VRAM readiness and unload any resident Ollama models")
    _add_backend_arg(p_evict)
    p_evict.add_argument("--json", action="store_true")
    p_evict.set_defaults(func=cmd_evict)

    p_vis = sub.add_parser("vision-qa", help="Inspect screenshot/sprite PNG(s) locally")
    _add_backend_arg(p_vis)
    p_vis.add_argument("path", help="PNG/JPG file or directory")
    p_vis.add_argument("--mode", choices=("proof", "sprite", "sheet"), default="proof")
    p_vis.add_argument("--prompt", default="")
    p_vis.add_argument("--max-images", type=int, default=12)
    p_vis.add_argument("--fail-on-defect", action="store_true")
    p_vis.add_argument("--json", action="store_true")
    p_vis.set_defaults(func=cmd_vision_qa)

    p_log = sub.add_parser("log-triage", help="Filter and triage a large Unity/build log locally")
    _add_backend_arg(p_log)
    p_log.add_argument("log_path")
    p_log.add_argument("--tier", choices=("fast", "heavy"), default="fast")
    p_log.add_argument("--max-lines", type=int, default=180)
    p_log.add_argument("--json", action="store_true")
    p_log.set_defaults(func=cmd_log_triage)

    p_sum = sub.add_parser("summarize", help="Summarize a code/doc file locally (defaults to heavy 14B tier)")
    _add_backend_arg(p_sum)
    p_sum.add_argument("file_path")
    p_sum.add_argument("--focus", default="")
    p_sum.add_argument("--tier", choices=("fast", "heavy"), default="heavy")
    p_sum.add_argument("--max-chars", type=int, default=18000)
    p_sum.add_argument("--json", action="store_true")
    p_sum.set_defaults(func=cmd_summarize)

    p_draft = sub.add_parser("draft-content", help="Draft concise RPG flavor/content locally (defaults to heavy 14B tier)")
    _add_backend_arg(p_draft)
    p_draft.add_argument("--kind", choices=("item", "monster", "npc", "encounter", "changelog"), required=True)
    p_draft.add_argument("--spec", required=True)
    p_draft.add_argument("--count", type=int, default=1)
    p_draft.add_argument("--tier", choices=("fast", "heavy"), default="heavy")
    p_draft.add_argument("--json", action="store_true")
    p_draft.set_defaults(func=cmd_draft_content)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
