"""Mesh normalization, UniRig skeleton prediction (with Windows/Blackwell compatibility check),
and headless Blender 4.5.9 auto-rigging, skinning, animation, and view rendering.
"""

from __future__ import annotations

import argparse
import gc
import importlib.util
import json
import os
import subprocess
import sys
import types
from pathlib import Path
from typing import Any

from .config import (
    BLENDER_EXE,
    CREATE_NO_WINDOW,
    FOUNDRY_CODE_DIR,
    FOUNDRY_VENV_PY,
    HF_CACHE_DIR,
    UNIRIG_REPO_DIR,
    query_gpu_vram,
)


def probe_unirig_native_blockers() -> dict[str, Any]:
    """Check which UniRig compiled CUDA/C++ packages are available or blocked on this host."""
    missing: list[str] = []
    for mod in ("spconv", "torch_scatter", "torch_cluster", "flash_attn", "bpy"):
        if importlib.util.find_spec(mod) is None:
            missing.append(mod)
    return {
        "native_full_unirig_available": len(missing) == 0,
        "missing_native_packages": missing,
        "reason": (
            "All native UniRig packages present."
            if not missing
            else (
                "Windows Python 3.12 + RTX 5060 Ti (sm_120, torch 2.10+cu130) without MSVC/CUDA Toolkit "
                f"lacks prebuilt wheels for: {', '.join(missing)}. "
                "UniRig Skinning (PTv3Object/spconv/flash_attn) falls back to Blender 4.5.9 ARMATURE_AUTO "
                "+ smooth Gaussian nearest-bone skinning."
            )
        ),
    }


def _install_unirig_ar_shims() -> None:
    """Install lightweight pure-PyTorch shims so UniRigAR (michelangelo + OPT-350M SDPA) can run
    without compiled spconv / flash_attn / torch_cluster extensions when using michelangelo_encoder.
    """
    import torch

    if "torch_cluster" not in sys.modules:
        tc = types.ModuleType("torch_cluster")

        def fps(
            src: torch.Tensor,
            batch: torch.Tensor | None = None,
            ratio: float = 0.5,
            random_start: bool = False,
        ) -> torch.Tensor:
            if batch is None:
                batch = torch.zeros(src.shape[0], dtype=torch.long, device=src.device)
            out_indices = []
            unique_batches = torch.unique(batch)
            for b in unique_batches:
                mask = batch == b
                global_idx = torch.nonzero(mask, as_tuple=False).squeeze(-1)
                pts = src[global_idx]
                n = pts.shape[0]
                k = max(1, int(round(n * float(ratio))))
                if k >= n:
                    out_indices.append(global_idx)
                    continue
                chosen = torch.empty(k, dtype=torch.long, device=src.device)
                cur = int(torch.randint(0, n, (1,), device=src.device).item()) if random_start else 0
                dists = torch.full((n,), float("inf"), device=src.device, dtype=pts.dtype)
                for i in range(k):
                    chosen[i] = cur
                    diff = pts - pts[cur].unsqueeze(0)
                    d2 = torch.sum(diff * diff, dim=-1)
                    dists = torch.minimum(dists, d2)
                    cur = int(torch.argmax(dists).item())
                out_indices.append(global_idx[chosen])
            return torch.cat(out_indices, dim=0)

        tc.fps = fps
        sys.modules["torch_cluster"] = tc

    if "spconv" not in sys.modules:
        spconv_mod = types.ModuleType("spconv")
        spconv_pt = types.ModuleType("spconv.pytorch")

        class _DummyModule:
            def __init__(self, *args: Any, **kwargs: Any) -> None:
                pass

        for attr in ("SparseConvTensor", "SparseModule", "SparseSequential", "SubMConv3d", "SparseConv3d"):
            setattr(spconv_pt, attr, _DummyModule)
        spconv_mod.pytorch = spconv_pt
        sys.modules["spconv"] = spconv_mod
        sys.modules["spconv.pytorch"] = spconv_pt

    if "torch_scatter" not in sys.modules:
        ts = types.ModuleType("torch_scatter")
        sys.modules["torch_scatter"] = ts

    # Import OPT + flash-attn checker cleanly before stubbing flash_attn for UniRigSkin
    import transformers.models.opt.modeling_opt  # noqa: F401
    import transformers.utils as _tf_utils
    import transformers.utils.import_utils as _tf_imp

    _tf_imp.is_flash_attn_2_available = lambda *a, **kw: False
    _tf_utils.is_flash_attn_2_available = lambda *a, **kw: False

    if "flash_attn" not in sys.modules:
        fa = types.ModuleType("flash_attn")
        fa_mha = types.ModuleType("flash_attn.modules")
        fa_mha_mod = types.ModuleType("flash_attn.modules.mha")
        fa_mha_mod.MHA = object
        fa_mha.mha = fa_mha_mod
        fa.modules = fa_mha
        sys.modules["flash_attn"] = fa
        sys.modules["flash_attn.modules"] = fa_mha
        sys.modules["flash_attn.modules.mha"] = fa_mha_mod


def _run_unirig_ar_in_process(
    glb_path: Path,
    out_joints_json: Path,
    rig_profile: str,
    seed: int = 42,
) -> dict[str, Any]:
    """Run UniRig Skeleton AR (350M OPT + Michelangelo encoder) directly on GPU."""
    glb_path = Path(glb_path).resolve()
    out_joints_json = Path(out_joints_json).resolve()
    HF_CACHE_DIR.mkdir(parents=True, exist_ok=True)
    os.environ["HF_HOME"] = str(HF_CACHE_DIR)
    unirig_str = str(UNIRIG_REPO_DIR)
    if unirig_str not in sys.path:
        sys.path.insert(0, unirig_str)

    _install_unirig_ar_shims()

    import huggingface_hub
    import numpy as np
    import torch
    import trimesh
    import yaml
    from box import Box

    prev_cwd = Path.cwd()
    os.chdir(UNIRIG_REPO_DIR)
    try:
        from src.model.unirig_ar import UniRigAR
        from src.tokenizer.parse import get_tokenizer
        from src.tokenizer.spec import TokenizerConfig

        model_cfg_path = UNIRIG_REPO_DIR / "configs" / "model" / "unirig_ar_350m_1024_81920_float32.yaml"
        tok_cfg_path = UNIRIG_REPO_DIR / "configs" / "tokenizer" / "tokenizer_parts_articulationxl_256.yaml"

        model_cfg = Box(yaml.safe_load(model_cfg_path.read_text(encoding="utf-8")))
        tok_cfg = Box(yaml.safe_load(tok_cfg_path.read_text(encoding="utf-8")))

        # Override flash_attention_2 to PyTorch native SDPA on Windows/Blackwell
        if hasattr(model_cfg, "llm") and "_attn_implementation" in model_cfg.llm:
            model_cfg.llm["_attn_implementation"] = "sdpa"

        tokenizer = get_tokenizer(TokenizerConfig.parse(config=tok_cfg))
        kwargs = dict(model_cfg)
        kwargs.pop("__target__", None)
        kwargs["tokenizer"] = tokenizer
        model = UniRigAR(**kwargs)

        ckpt_path = huggingface_hub.hf_hub_download(
            repo_id="VAST-AI/UniRig",
            filename="skeleton/articulation-xl_quantization_256/model.ckpt",
        )
        ckpt = torch.load(ckpt_path, map_location="cpu", weights_only=False)
        state_dict = ckpt.get("state_dict", ckpt)
        cleaned = {}
        for k, v in state_dict.items():
            nk = k[len("model.") :] if k.startswith("model.") else k
            cleaned[nk] = v
        missing_k, unexpected_k = model.load_state_dict(cleaned, strict=False)

        mesh = trimesh.load(str(glb_path))
        if isinstance(mesh, trimesh.Scene):
            mesh = mesh.dump(concatenate=True)

        # Sample surface points + face normals normalized to [-0.95, 0.95] cube centered at origin
        bounds = mesh.bounds
        center = 0.5 * (bounds[0] + bounds[1])
        extent = float(np.max(bounds[1] - bounds[0]))
        scale = 1.9 / max(extent, 1e-6)

        pts, face_idx = trimesh.sample.sample_surface(mesh, 4096)
        norms = mesh.face_normals[face_idx]
        pts_norm = (pts - center) * scale

        cls_map = {
            "Humanoid": "mixamo",
            "Quadruped": "rigxl",
            "Aquatic": "rigxl",
        }
        cls_name = cls_map.get(rig_profile, None)

        device = "cuda" if torch.cuda.is_available() else "cpu"
        model = model.to(device).eval()
        v_tensor = torch.from_numpy(pts_norm.astype(np.float32)).to(device)
        n_tensor = torch.from_numpy(norms.astype(np.float32)).to(device)

        torch.manual_seed(int(seed))
        with torch.no_grad():
            res = model.generate(
                vertices=v_tensor,
                normals=n_tensor,
                cls=cls_name,
                max_new_tokens=1024,
                do_sample=False,
            )

        joints_norm = np.asarray(res.joints, dtype=np.float32)
        parents = [int(p) if p is not None else -1 for p in res.parents]
        names = [str(nm) if nm else f"Bone_{i:02d}" for i, nm in enumerate(res.names)]

        # Undo normalization back into glTF mesh space
        joints_world = (joints_norm / scale) + center

        del model, v_tensor, n_tensor, ckpt
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

        if len(joints_world) < 4 or not np.all(np.isfinite(joints_world)):
            raise RuntimeError(f"UniRigAR returned invalid skeleton (count={len(joints_world)})")

        payload = {
            "source": "unirig_ar_350m",
            "bones_count": len(names),
            "joints": joints_world.tolist(),
            "parents": parents,
            "names": names,
            "missing_keys_count": len(missing_k),
            "unexpected_keys_count": len(unexpected_k),
        }
        out_joints_json.parent.mkdir(parents=True, exist_ok=True)
        out_joints_json.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        return {
            "status": "ok",
            "bone_count": len(names),
            "joints_json": str(out_joints_json),
            "post_vram_mib": query_gpu_vram()["used_mib"],
        }
    finally:
        os.chdir(prev_cwd)


def try_unirig_skeleton(
    glb_path: Path,
    out_joints_json: Path,
    rig_profile: str,
    seed: int = 42,
) -> dict[str, Any]:
    """Run UniRigAR in an isolated subprocess with clean CUDA teardown and transparent fallback status."""
    blocker_info = probe_unirig_native_blockers()
    if rig_profile == "Static":
        return {"used_unirig_ar": False, "reason": "Static asset", **blocker_info}

    cmd = [
        str(FOUNDRY_VENV_PY),
        "-m",
        "foundry.rig_normalize",
        "--unirig-ar",
        "--in-glb",
        str(glb_path),
        "--joints-out",
        str(out_joints_json),
        "--rig",
        rig_profile,
        "--seed",
        str(seed),
    ]
    res = subprocess.run(
        cmd,
        cwd=str(FOUNDRY_CODE_DIR),
        capture_output=True,
        text=True,
        timeout=300,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode == 0 and out_joints_json.exists():
        for line in reversed(res.stdout.strip().splitlines()):
            line = line.strip()
            if line.startswith("{") and line.endswith("}"):
                data = json.loads(line)
                return {"used_unirig_ar": True, "ar_result": data, **blocker_info}
    return {
        "used_unirig_ar": False,
        "ar_error": (res.stderr or res.stdout)[-800:].strip(),
        **blocker_info,
    }


def normalize_and_rig_with_blender(
    raw_glb: Path,
    canonical_glb: Path,
    meta_out: Path,
    category: str,
    rig_profile: str,
    scale_m: float,
    target_tris: int,
    joints_json: Path | None = None,
) -> dict[str, Any]:
    """Execute headless Blender 4.5.9 normalize_rig step."""
    if not BLENDER_EXE.exists():
        raise FileNotFoundError(f"Blender executable not found at {BLENDER_EXE}")

    blender_script = FOUNDRY_CODE_DIR / "foundry" / "blender_rig_bake.py"
    cmd = [
        str(BLENDER_EXE),
        "--background",
        "--python",
        str(blender_script),
        "--",
        "--mode",
        "normalize_rig",
        "--input",
        str(raw_glb),
        "--output",
        str(canonical_glb),
        "--meta-out",
        str(meta_out),
        "--category",
        category,
        "--rig",
        rig_profile,
        "--scale",
        f"{scale_m:.4f}",
        "--target-tris",
        str(int(target_tris)),
    ]
    if joints_json is not None and joints_json.exists():
        cmd.extend(["--joints-json", str(joints_json)])

    res = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        timeout=240,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode != 0 or not meta_out.exists() or not canonical_glb.exists():
        raise RuntimeError(
            f"Blender normalize_rig failed (rc={res.returncode}):\nSTDOUT:\n{res.stdout[-1200:]}\nSTDERR:\n{res.stderr[-1200:]}"
        )
    return json.loads(meta_out.read_text(encoding="utf-8"))


def render_views_with_blender(
    canonical_glb: Path,
    out_albedo: Path,
    out_normal: Path,
    out_qa: Path,
    size: int = 128,
    frames: int = 1,
) -> None:
    """Render orthographic HD-2D albedo, view-space OpenGL normal, and 2x2 turntable QA contact sheet."""
    if not BLENDER_EXE.exists():
        raise FileNotFoundError(f"Blender executable not found at {BLENDER_EXE}")

    blender_script = FOUNDRY_CODE_DIR / "foundry" / "blender_rig_bake.py"
    cmd = [
        str(BLENDER_EXE),
        "--background",
        "--python",
        str(blender_script),
        "--",
        "--mode",
        "render_views",
        "--input",
        str(canonical_glb),
        "--out-albedo",
        str(out_albedo),
        "--out-normal",
        str(out_normal),
        "--out-qa",
        str(out_qa),
        "--size",
        str(int(size)),
        "--render-frames",
        str(int(frames)),
    ]
    res = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        timeout=240,
        creationflags=CREATE_NO_WINDOW,
    )
    if res.returncode != 0 or not out_albedo.exists() or not out_normal.exists():
        raise RuntimeError(
            f"Blender render_views failed (rc={res.returncode}):\nSTDOUT:\n{res.stdout[-1200:]}\nSTDERR:\n{res.stderr[-1200:]}"
        )


def _main() -> int:
    parser = argparse.ArgumentParser(description="Realmz UniRig / Blender Helper")
    parser.add_argument("--unirig-ar", action="store_true")
    parser.add_argument("--in-glb", default="")
    parser.add_argument("--joints-out", default="")
    parser.add_argument("--rig", default="Humanoid")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    if args.unirig_ar:
        out = _run_unirig_ar_in_process(
            Path(args.in_glb),
            Path(args.joints_out),
            args.rig,
            seed=args.seed,
        )
        print(json.dumps(out))
        return 0

    print(json.dumps(probe_unirig_native_blockers(), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
