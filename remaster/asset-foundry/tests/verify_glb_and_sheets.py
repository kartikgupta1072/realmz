"""Automated QA verifier for canonical .glb geometry/rig/animations and HD-2D sprite/normal sheets."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

import numpy as np
import trimesh
from PIL import Image
from pygltflib import GLTF2

FOUNDRY_CODE_DIR = Path(r"C:\realmz\remaster\asset-foundry")
if str(FOUNDRY_CODE_DIR) not in sys.path:
    sys.path.insert(0, str(FOUNDRY_CODE_DIR))

from foundry.config import PALETTE_SOC_128  # noqa: E402


def inspect_glb_and_sheets(job_dir: Path) -> dict[str, Any]:
    asset_id = job_dir.name
    glb_path = job_dir / f"{asset_id}.glb"
    sheet_path = job_dir / f"{asset_id}_sheet.png"
    normal_path = job_dir / f"{asset_id}_sheet_n.png"
    manifest_path = job_dir / f"{asset_id}.asset.json"

    if not glb_path.exists():
        raise FileNotFoundError(f"Missing GLB: {glb_path}")
    if not manifest_path.exists():
        raise FileNotFoundError(f"Missing manifest: {manifest_path}")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    # Inspect GLB mesh geometry + bounds via trimesh
    scene_or_mesh = trimesh.load(str(glb_path))
    if isinstance(scene_or_mesh, trimesh.Scene):
        mesh = scene_or_mesh.dump(concatenate=True)
    else:
        mesh = scene_or_mesh

    bounds = mesh.bounds
    bmin, bmax = bounds[0], bounds[1]
    extents = bmax - bmin
    center = 0.5 * (bmin + bmax)

    uv_ok = hasattr(mesh.visual, "uv") and mesh.visual.uv is not None and len(mesh.visual.uv) > 0

    # Inspect glTF structure (skins, joints, animations, images) via pygltflib
    gltf = GLTF2().load(str(glb_path))
    skin_bones = 0
    if gltf.skins:
        skin_bones = len(gltf.skins[0].joints)
    anim_names = [a.name or f"anim_{i}" for i, a in enumerate(gltf.animations or [])]
    image_count = len(gltf.images or [])

    # Inspect HD-2D sheets if present
    sheet_info: dict[str, Any] = {}
    if sheet_path.exists() and normal_path.exists():
        with Image.open(sheet_path) as im_a, Image.open(normal_path) as im_n:
            alb = np.array(im_a.convert("RGBA"))
            nrm = np.array(im_n.convert("RGBA"))

        alpha_u8 = alb[..., 3]
        alpha_vals = sorted(int(v) for v in np.unique(alpha_u8))
        alpha_1bit_ok = alpha_vals in ([0, 255], [255])

        opaque = alpha_u8 == 255
        n_op = (nrm[opaque, :3].astype(np.float32) - 127.5) / 127.5 if np.any(opaque) else np.zeros((1, 3))
        mags = np.linalg.norm(n_op, axis=-1)

        pal_data = json.loads(PALETTE_SOC_128.read_text(encoding="utf-8"))
        pal_set = {
            (int(c[1:3], 16), int(c[3:5], 16), int(c[5:7], 16))
            for c in pal_data["colors"]
        }
        used_colors = {tuple(int(x) for x in px) for px in alb[opaque, :3]} if np.any(opaque) else set()
        off_palette = len(used_colors - pal_set)

        sheet_info = {
            "size": [int(alb.shape[1]), int(alb.shape[0])],
            "alpha_1bit_ok": alpha_1bit_ok,
            "palette_colors_used": len(used_colors),
            "off_palette_colors": off_palette,
            "normal_unit_mag_mean": round(float(mags.mean()), 4),
            "normal_min_z": round(float(n_op[:, 2].min()), 3),
        }

    return {
        "id": asset_id,
        "faces": int(len(mesh.faces)),
        "vertices": int(len(mesh.vertices)),
        "uv_present": bool(uv_ok),
        "glb_images": int(image_count),
        "min_y": round(float(bmin[1]), 4),
        "center_x": round(float(center[0]), 4),
        "center_z": round(float(center[2]), 4),
        "extents": [round(float(extents[0]), 4), round(float(extents[1]), 4), round(float(extents[2]), 4)],
        "skin_joints": int(skin_bones),
        "animations": anim_names,
        "manifest_rig_source": manifest.get("rig_source"),
        "manifest_rigged": manifest.get("rigged"),
        "hd2d": sheet_info,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify Foundry job outputs")
    parser.add_argument("job_dirs", nargs="+", type=Path)
    args = parser.parse_args()
    out = [inspect_glb_and_sheets(d) for d in args.job_dirs]
    print(json.dumps(out, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
