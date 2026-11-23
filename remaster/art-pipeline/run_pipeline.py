"""
End-to-end ComfyUI API runner + VRAM telemetry + 1x sprite & normal-map export
for the Realmz HD-2D Songs-of-Conquest asset pipeline.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import threading
import time
import urllib.parse
from pathlib import Path

import requests

from postprocess_sprite import DEFAULT_LAIGTER_EXE, DEFAULT_PALETTE_PATH, process_sprite

PIPE_DIR = Path(__file__).parent
DEFAULT_WORKFLOW = PIPE_DIR / "comfy_workflow_sprite_api.json"


def query_vram_mib() -> int:
    try:
        out = subprocess.check_output(
            [
                "nvidia-smi",
                "--query-gpu=memory.used",
                "--format=csv,noheader,nounits",
            ],
            text=True,
            timeout=5,
            creationflags=0x08000000 if os.name == "nt" else 0,
        ).strip()
        return int(out.splitlines()[0].strip())
    except Exception:
        return -1


class VramMonitor:
    def __init__(self, interval: float = 0.25) -> None:
        self.interval = interval
        self.peak_mib = query_vram_mib()
        self.samples: list[int] = [self.peak_mib] if self.peak_mib >= 0 else []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def _run(self) -> None:
        while not self._stop.is_set():
            v = query_vram_mib()
            if v >= 0:
                self.samples.append(v)
                if v > self.peak_mib:
                    self.peak_mib = v
            time.sleep(self.interval)

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> int:
        self._stop.set()
        self._thread.join(timeout=2.0)
        v = query_vram_mib()
        if v > self.peak_mib:
            self.peak_mib = v
        return self.peak_mib


def wait_for_server(base_url: str, timeout: float = 60.0) -> dict:
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            r = requests.get(f"{base_url}/system_stats", timeout=3)
            if r.ok:
                return r.json()
        except Exception:
            pass
        time.sleep(0.5)
    raise RuntimeError(f"ComfyUI server not reachable at {base_url} within {timeout}s")


def run_comfy_prompt(
    base_url: str,
    workflow: dict,
    timeout: float = 240.0,
) -> tuple[bytes, str, float, int]:
    mon = VramMonitor(interval=0.25)
    mon.start()
    t0 = time.time()
    try:
        r = requests.post(f"{base_url}/prompt", json={"prompt": workflow}, timeout=30)
        if not r.ok:
            raise RuntimeError(f"ComfyUI /prompt failed ({r.status_code}): {r.text}")
        prompt_id = r.json()["prompt_id"]

        deadline = time.time() + timeout
        history_entry = None
        while time.time() < deadline:
            hr = requests.get(f"{base_url}/history/{prompt_id}", timeout=10)
            if hr.ok:
                data = hr.json()
                if prompt_id in data:
                    history_entry = data[prompt_id]
                    status = history_entry.get("status", {})
                    if status.get("status_str") == "error":
                        raise RuntimeError(f"ComfyUI execution error: {json.dumps(status)}")
                    if history_entry.get("outputs"):
                        break
            time.sleep(0.4)

        if not history_entry:
            raise TimeoutError(f"Timed out waiting for ComfyUI prompt {prompt_id}")

        # Locate first SaveImage output
        img_info = None
        for node_out in history_entry.get("outputs", {}).values():
            images = node_out.get("images", [])
            if images:
                img_info = images[0]
                break
        if not img_info:
            raise RuntimeError(f"No output images in ComfyUI history: {history_entry}")

        params = urllib.parse.urlencode(
            {
                "filename": img_info["filename"],
                "subfolder": img_info.get("subfolder", ""),
                "type": img_info.get("type", "output"),
            }
        )
        ir = requests.get(f"{base_url}/view?{params}", timeout=30)
        ir.raise_for_status()
        elapsed = time.time() - t0
    finally:
        peak_mib = mon.stop()

    return ir.content, img_info["filename"], elapsed, peak_mib


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate 1x Songs-of-Conquest pixel sprite + normal map via ComfyUI API"
    )
    parser.add_argument("--server", default="http://127.0.0.1:8188")
    parser.add_argument("--workflow", type=Path, default=DEFAULT_WORKFLOW)
    parser.add_argument("--name", default="knight_hero", help="Base asset stem")
    parser.add_argument("--out-dir", type=Path, default=PIPE_DIR / "samples")
    parser.add_argument("--prompt", default=None, help="Optional override positive prompt")
    parser.add_argument("--seed", type=int, default=1994064)
    parser.add_argument("--steps", type=int, default=28)
    parser.add_argument("--size", type=int, default=64, help="Final 1x sprite size")
    parser.add_argument("--lora", default=None, help="Optional LoRA filename override")
    parser.add_argument("--lora-strength", type=float, default=None)
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH)
    parser.add_argument("--dither", type=float, default=0.0)
    parser.add_argument("--grade", type=float, default=0.25)
    parser.add_argument(
        "--normal-backend",
        choices=("python", "laigter", "auto"),
        default="python",
    )
    parser.add_argument("--laigter-exe", type=Path, default=DEFAULT_LAIGTER_EXE)
    args = parser.parse_args()

    wait_for_server(args.server, timeout=30.0)

    workflow = json.loads(args.workflow.read_text(encoding="utf-8"))
    # Patch configurable node inputs cleanly
    for node in workflow.values():
        ctype = node.get("class_type")
        inputs = node.get("inputs", {})
        if ctype == "KSampler":
            inputs["seed"] = args.seed
            inputs["steps"] = args.steps
        elif ctype == "LoraLoader":
            if args.lora:
                inputs["lora_name"] = args.lora
            if args.lora_strength is not None:
                inputs["strength_model"] = args.lora_strength
                inputs["strength_clip"] = args.lora_strength
        elif ctype == "SaveImage":
            inputs["filename_prefix"] = f"realmz_{args.name}_raw"

    if args.prompt and "4" in workflow and workflow["4"].get("class_type") == "CLIPTextEncode":
        workflow["4"]["inputs"]["text"] = args.prompt

    vram_before = query_vram_mib()
    raw_bytes, comfy_filename, gen_sec, peak_vram = run_comfy_prompt(args.server, workflow)

    args.out_dir.mkdir(parents=True, exist_ok=True)
    raw_path = args.out_dir / f"{args.name}_raw_1024.png"
    albedo_path = args.out_dir / f"{args.name}_64x64.png" if args.size == 64 else args.out_dir / f"{args.name}_{args.size}x{args.size}.png"
    normal_path = albedo_path.with_name(f"{albedo_path.stem}_n.png")
    preview_path = args.out_dir / f"{args.name}_qa_8x.png"

    raw_path.write_bytes(raw_bytes)

    t_post0 = time.time()
    stats = process_sprite(
        input_path=raw_path,
        output_albedo=albedo_path,
        output_normal=normal_path,
        output_preview=preview_path,
        target_size=args.size,
        palette_path=args.palette,
        dither_strength=args.dither,
        grade_strength=args.grade,
        normal_backend=args.normal_backend,
        laigter_exe=args.laigter_exe,
    )
    post_sec = time.time() - t_post0

    report = {
        "asset": args.name,
        "seed": args.seed,
        "comfy_output_file": comfy_filename,
        "raw_path": str(raw_path),
        "vram_before_mib": vram_before,
        "vram_peak_mib": peak_vram,
        "vram_budget_mib": 8151,
        "vram_ok": 0 <= peak_vram < 8151,
        "comfy_gen_sec": round(gen_sec, 2),
        "postprocess_sec": round(post_sec, 2),
        "total_sec": round(gen_sec + post_sec, 2),
        "stats": stats,
    }
    report_path = args.out_dir / f"{args.name}_report.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
