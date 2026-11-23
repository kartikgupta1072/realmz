"""Local 8 GB VRAM Image-to-3D + Multi-View Texture Synthesis via Hunyuan3D-2mini-Turbo and Hunyuan3D-Paint.

Runs shape generation and texture painting in strictly isolated sequential GPU stages
with explicit gc.collect(), torch.cuda.empty_cache(), and subprocess-level CUDA teardown
so peak VRAM stays safely under 7,800 MiB on RTX 5060 Ti (8,151 MiB) and idle VRAM
returns below 1,000 MiB.
"""

from __future__ import annotations

import argparse
import gc
import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any

from .config import (
    CREATE_NO_WINDOW,
    FOUNDRY_CODE_DIR,
    FOUNDRY_VENV_PY,
    HF_CACHE_DIR,
    HUNYUAN_REPO_DIR,
    query_gpu_vram,
)


def _setup_hunyuan_env() -> None:
    HF_CACHE_DIR.mkdir(parents=True, exist_ok=True)
    os.environ["HF_HOME"] = str(HF_CACHE_DIR)
    os.environ["HY3DGEN_MODELS"] = str(HF_CACHE_DIR / "hy3dgen")
    repo_str = str(HUNYUAN_REPO_DIR)
    if repo_str not in sys.path:
        sys.path.insert(0, repo_str)


def _cleanup_torch_cuda() -> None:
    gc.collect()
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.synchronize()
            torch.cuda.empty_cache()
            torch.cuda.ipc_collect()
    except Exception:
        pass


class VramMonitor:
    """Background nvidia-smi poller recording exact peak VRAM across a GPU stage."""

    def __init__(self, interval_sec: float = 0.4) -> None:
        self.interval_sec = interval_sec
        self.peak_used_mib = 0
        self.samples: list[int] = []
        self._stop_event = threading.Event()
        self._thread: threading.Thread | None = None

    def __enter__(self) -> "VramMonitor":
        snap = query_gpu_vram()
        if snap["used_mib"] > self.peak_used_mib:
            self.peak_used_mib = snap["used_mib"]
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()
        return self

    def _loop(self) -> None:
        while not self._stop_event.wait(self.interval_sec):
            snap = query_gpu_vram()
            used = snap.get("used_mib", 0)
            if used > 0:
                self.samples.append(used)
                if used > self.peak_used_mib:
                    self.peak_used_mib = used

    def __exit__(self, exc_type: Any, exc: Any, tb: Any) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)
        snap = query_gpu_vram()
        if snap.get("used_mib", 0) > self.peak_used_mib:
            self.peak_used_mib = snap["used_mib"]


def _ensure_shapegen_safetensors_cached() -> None:
    """Pre-fetch only .safetensors and .yaml files for Hunyuan3D-2mini-Turbo so redundant .ckpt blobs are skipped."""
    import huggingface_hub

    hy_dir = Path(os.environ["HY3DGEN_MODELS"]) / "tencent" / "Hunyuan3D-2mini"
    dit_dir = hy_dir / "hunyuan3d-dit-v2-mini-turbo"
    if (dit_dir / "model.fp16.safetensors").exists():
        return

    snap_path = Path(
        huggingface_hub.snapshot_download(
            repo_id="tencent/Hunyuan3D-2mini",
            allow_patterns=[
                "hunyuan3d-dit-v2-mini-turbo/*.safetensors",
                "hunyuan3d-dit-v2-mini-turbo/*.yaml",
            ],
        )
    )
    for sub in ("hunyuan3d-dit-v2-mini-turbo",):
        dst_sub = hy_dir / sub
        dst_sub.mkdir(parents=True, exist_ok=True)
        for src_file in (snap_path / sub).glob("*"):
            if src_file.is_file():
                dst_file = dst_sub / src_file.name
                if not dst_file.exists():
                    try:
                        os.link(src_file, dst_file)
                    except OSError:
                        import shutil

                        shutil.copy2(src_file, dst_file)


def run_shape_stage(
    image_rgba_path: Path,
    out_raw_glb: Path,
    target_tris: int = 8000,
    steps: int = 5,
    octree_res: int = 256,
    seed: int = 42,
    low_vram: bool = True,
) -> dict[str, Any]:
    """Generate clean decimated shape mesh using Hunyuan3D-2mini-Turbo (fp16)."""
    _setup_hunyuan_env()
    _ensure_shapegen_safetensors_cached()
    import torch
    import trimesh
    from PIL import Image

    from hy3dgen.shapegen import (
        DegenerateFaceRemover,
        FaceReducer,
        FloaterRemover,
        Hunyuan3DDiTFlowMatchingPipeline,
    )
    from hy3dgen.shapegen.pipelines import export_to_trimesh  # noqa: F401

    t0 = time.perf_counter()
    image = Image.open(image_rgba_path).convert("RGBA")

    with VramMonitor() as mon:
        pipe = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
            "tencent/Hunyuan3D-2mini",
            subfolder="hunyuan3d-dit-v2-mini-turbo",
            use_safetensors=True,
            variant="fp16",
            device="cuda",
            dtype=torch.float16,
        )

        gen = torch.Generator(device="cuda").manual_seed(int(seed))
        mesh = pipe(
            image=image,
            num_inference_steps=int(steps),
            guidance_scale=5.0,
            generator=gen,
            octree_resolution=int(octree_res),
            num_chunks=8000,
            output_type="trimesh",
        )[0]

        del pipe
        _cleanup_torch_cuda()

    mesh = FloaterRemover()(mesh)
    mesh = DegenerateFaceRemover()(mesh)
    # Keep slightly above final budget during texture projection so paint UV detail is crisp,
    # or decimate cleanly if very dense.
    paint_face_cap = max(int(target_tris), 6000)
    if len(mesh.faces) > paint_face_cap:
        mesh = FaceReducer()(mesh, max_facenum=paint_face_cap)

    if isinstance(mesh, trimesh.Scene):
        mesh = mesh.dump(concatenate=True)

    out_raw_glb.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(str(out_raw_glb))

    elapsed = round(time.perf_counter() - t0, 2)
    idle = query_gpu_vram()
    return {
        "stage": "shape",
        "elapsed_sec": elapsed,
        "peak_vram_mib": mon.peak_used_mib,
        "post_vram_mib": idle["used_mib"],
        "faces": int(len(mesh.faces)),
        "vertices": int(len(mesh.vertices)),
        "out_glb": str(out_raw_glb),
    }


def run_paint_stage(
    raw_glb_path: Path,
    image_rgba_path: Path,
    out_textured_glb: Path,
    delighted_out_path: Path | None = None,
    render_size: int = 1024,
    texture_size: int = 1024,
    skip_delight_for_pixel: bool = False,
) -> dict[str, Any]:
    """Synthesize multi-view UV textures onto raw mesh using sequential Delight -> Multiview -> Bake."""
    _setup_hunyuan_env()
    import huggingface_hub
    import numpy as np
    import torch
    import trimesh
    from PIL import Image

    from hy3dgen.texgen.differentiable_renderer.mesh_render import MeshRender
    from hy3dgen.texgen.pipelines import Hunyuan3DTexGenConfig
    from hy3dgen.texgen.utils.dehighlight_utils import Light_Shadow_Remover
    from hy3dgen.texgen.utils.multiview_utils import Multiview_Diffusion_Net
    from hy3dgen.texgen.utils.uv_warp_utils import mesh_uv_wrap

    t0 = time.perf_counter()
    subfolder = "hunyuan3d-paint-v2-0-turbo"

    delight_root = huggingface_hub.snapshot_download(
        repo_id="tencent/Hunyuan3D-2",
        allow_patterns=["hunyuan3d-delight-v2-0/*"],
    )
    paint_root = huggingface_hub.snapshot_download(
        repo_id="tencent/Hunyuan3D-2",
        allow_patterns=[f"{subfolder}/*"],
        ignore_patterns=[f"{subfolder}/unet/diffusion_pytorch_model.bin"],
    )
    delight_ckpt = os.path.join(delight_root, "hunyuan3d-delight-v2-0")
    multiview_ckpt = os.path.join(paint_root, subfolder)

    cfg = Hunyuan3DTexGenConfig(delight_ckpt, multiview_ckpt, subfolder)
    cfg.render_size = int(render_size)
    cfg.texture_size = int(texture_size)

    src_img = Image.open(image_rgba_path).convert("RGBA")

    # Recenter RGBA cleanly with 20% border matching Hunyuan3DPaintPipeline
    alpha_arr = np.array(src_img)[:, :, 3]
    nz = np.argwhere(alpha_arr > 0)
    if nz.size > 0:
        min_r, min_c = nz.min(axis=0)
        max_r, max_c = nz.max(axis=0)
        cropped = src_img.crop((int(min_c), int(min_r), int(max_c) + 1, int(max_r) + 1))
        cw, ch = cropped.size
        bw, bh = int(cw * 0.2), int(ch * 0.2)
        sq = max(cw + 2 * bw, ch + 2 * bh)
        centered = Image.new("RGBA", (sq, sq), (255, 255, 255, 0))
        centered.paste(cropped, ((sq - cw) // 2, (sq - ch) // 2))
    else:
        centered = src_img

    with VramMonitor() as mon:
        # Sub-stage 2a: Delight (load on CPU first, stream modules on CUDA via offload)
        if skip_delight_for_pixel:
            white = Image.new("RGBA", centered.size, (255, 255, 255, 255))
            white.alpha_composite(centered)
            prompt_img = white.convert("RGB").resize((512, 512), Image.Resampling.LANCZOS)
        else:
            orig_dev = cfg.device
            cfg.device = "cpu"
            delight_model = Light_Shadow_Remover(cfg)
            cfg.device = orig_dev
            delight_model.device = "cuda"
            delight_model.pipeline.enable_attention_slicing()
            delight_model.pipeline.enable_model_cpu_offload()
            prompt_img = delight_model(centered)
            del delight_model
            _cleanup_torch_cuda()

        if delighted_out_path is not None:
            delighted_out_path.parent.mkdir(parents=True, exist_ok=True)
            prompt_img.save(delighted_out_path)

        # Sub-stage 2b: UV wrap + geometric normal/position render
        mesh = trimesh.load(str(raw_glb_path))
        if isinstance(mesh, trimesh.Scene):
            mesh = mesh.dump(concatenate=True)
        mesh = mesh_uv_wrap(mesh)

        render = MeshRender(
            default_resolution=cfg.render_size,
            texture_size=cfg.texture_size,
        )
        render.load_mesh(mesh)

        elevs = cfg.candidate_camera_elevs
        azims = cfg.candidate_camera_azims
        weights = cfg.candidate_view_weights

        normal_maps = [render.render_normal(e, a, use_abs_coor=True, return_type="pl") for e, a in zip(elevs, azims)]
        position_maps = [render.render_position(e, a, return_type="pl") for e, a in zip(elevs, azims)]

        camera_info = [
            (((azim // 30) + 9) % 12) // {-20: 1, 0: 1, 20: 1, -90: 3, 90: 3}[elev]
            + {-20: 0, 0: 12, 20: 24, -90: 36, 90: 40}[elev]
            for azim, elev in zip(azims, elevs)
        ]

        # Sub-stage 2c: Multiview Diffusion (load on CPU first so peak VRAM never holds full pipeline)
        orig_dev = cfg.device
        cfg.device = "cpu"
        mv_model = Multiview_Diffusion_Net(cfg)
        cfg.device = orig_dev
        mv_model.device = "cuda"
        mv_model.pipeline.enable_attention_slicing()
        if hasattr(mv_model.pipeline, "enable_vae_slicing"):
            mv_model.pipeline.enable_vae_slicing()
        mv_model.pipeline.enable_model_cpu_offload()

        multiviews = mv_model([prompt_img], normal_maps + position_maps, camera_info)
        del mv_model
        _cleanup_torch_cuda()

        # Sub-stage 2d: Back-project 6 views and inpaint UV seams
        resized_views = [v.resize((cfg.render_size, cfg.render_size), Image.Resampling.LANCZOS) for v in multiviews]
        project_textures = []
        project_weighted_cos_maps = []
        for view, elev, azim, w in zip(resized_views, elevs, azims, weights):
            proj_tex, cos_map, _ = render.back_project(view, elev, azim)
            project_textures.append(proj_tex)
            project_weighted_cos_maps.append(w * (cos_map ** cfg.bake_exp))

        texture, ori_trust_map = render.fast_bake_texture(project_textures, project_weighted_cos_maps)
        mask = ori_trust_map > 1e-8
        mask_np = (mask.squeeze(-1).cpu().numpy() * 255).astype(np.uint8)

        tex_np = render.uv_inpaint(texture, mask_np)
        render.set_texture(torch.tensor(tex_np / 255.0).float().to(texture.device))
        textured_mesh = render.save_mesh()

        del render, project_textures, project_weighted_cos_maps, texture, ori_trust_map
        _cleanup_torch_cuda()

    out_textured_glb.parent.mkdir(parents=True, exist_ok=True)
    textured_mesh.export(str(out_textured_glb), include_normals=True)

    elapsed = round(time.perf_counter() - t0, 2)
    idle = query_gpu_vram()
    return {
        "stage": "paint",
        "elapsed_sec": elapsed,
        "peak_vram_mib": mon.peak_used_mib,
        "post_vram_mib": idle["used_mib"],
        "faces": int(len(textured_mesh.faces)),
        "vertices": int(len(textured_mesh.vertices)),
        "out_glb": str(out_textured_glb),
    }


def run_isolated_stage(args_list: list[str]) -> dict[str, Any]:
    """Execute one img2mesh GPU stage in an isolated subprocess so OS CUDA frees 100% VRAM on exit."""
    cmd = [str(FOUNDRY_VENV_PY), "-m", "foundry.img2mesh", *args_list]
    res = subprocess.run(
        cmd,
        cwd=str(FOUNDRY_CODE_DIR),
        capture_output=True,
        text=True,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode != 0:
        raise RuntimeError(
            f"img2mesh stage failed (rc={res.returncode}):\nSTDOUT:\n{res.stdout[-1500:]}\nSTDERR:\n{res.stderr[-2000:]}"
        )
    for line in reversed(res.stdout.strip().splitlines()):
        line = line.strip()
        if line.startswith("{") and line.endswith("}"):
            return json.loads(line)
    raise RuntimeError(f"img2mesh stage produced no JSON result:\n{res.stdout[-1000:]}")


def generate_textured_glb(
    image_rgba_path: Path,
    work_dir: Path,
    target_tris: int = 6500,
    steps: int = 8,
    octree_res: int = 256,
    seed: int = 42,
    render_size: int = 1024,
    texture_size: int = 1024,
    skip_delight_for_pixel: bool = False,
) -> dict[str, Any]:
    """Run ShapeGen then Paint in separate isolated GPU subprocesses and report aggregate telemetry."""
    raw_glb = work_dir / "stage1_shape.glb"
    textured_glb = work_dir / "stage2_textured.glb"
    delighted_png = work_dir / "stage2_delighted.png"

    shape_stats = run_isolated_stage(
        [
            "--stage",
            "shape",
            "--image",
            str(image_rgba_path),
            "--out-glb",
            str(raw_glb),
            "--target-tris",
            str(target_tris),
            "--steps",
            str(steps),
            "--octree-res",
            str(octree_res),
            "--seed",
            str(seed),
        ]
    )

    paint_args = [
        "--stage",
        "paint",
        "--image",
        str(image_rgba_path),
        "--in-glb",
        str(raw_glb),
        "--out-glb",
        str(textured_glb),
        "--delighted-out",
        str(delighted_png),
        "--render-size",
        str(render_size),
        "--texture-size",
        str(texture_size),
    ]
    if skip_delight_for_pixel:
        paint_args.append("--skip-delight")

    paint_stats = run_isolated_stage(paint_args)
    idle_after = query_gpu_vram()
    peak_vram = max(shape_stats["peak_vram_mib"], paint_stats["peak_vram_mib"])

    return {
        "raw_glb": str(raw_glb),
        "textured_glb": str(textured_glb),
        "delighted_png": str(delighted_png),
        "shape": shape_stats,
        "paint": paint_stats,
        "peak_vram_mib": peak_vram,
        "idle_vram_after_mib": idle_after["used_mib"],
    }


def _main() -> int:
    parser = argparse.ArgumentParser(description="Realmz Asset Foundry 8 GB Image-to-3D Stage Runner")
    parser.add_argument("--stage", choices=("shape", "paint"), required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--in-glb", default="")
    parser.add_argument("--out-glb", required=True)
    parser.add_argument("--delighted-out", default="")
    parser.add_argument("--target-tris", type=int, default=6500)
    parser.add_argument("--steps", type=int, default=8)
    parser.add_argument("--octree-res", type=int, default=256)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--render-size", type=int, default=1024)
    parser.add_argument("--texture-size", type=int, default=1024)
    parser.add_argument("--skip-delight", action="store_true")
    args = parser.parse_args()

    if args.stage == "shape":
        res = run_shape_stage(
            image_rgba_path=Path(args.image),
            out_raw_glb=Path(args.out_glb),
            target_tris=args.target_tris,
            steps=args.steps,
            octree_res=args.octree_res,
            seed=args.seed,
        )
    else:
        res = run_paint_stage(
            raw_glb_path=Path(args.in_glb),
            image_rgba_path=Path(args.image),
            out_textured_glb=Path(args.out_glb),
            delighted_out_path=Path(args.delighted_out) if args.delighted_out else None,
            render_size=args.render_size,
            texture_size=args.texture_size,
            skip_delight_for_pixel=args.skip_delight,
        )

    print(json.dumps(res))
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
