"""Automated seamless PBR terrain bundle QA verifier and 2x2 contact sheet renderer."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw

from .config import sanitize_ascii


def _resolve_id_and_res(bundle_dir: Path) -> tuple[str, int | None, dict[str, Any]]:
    manifest_path = bundle_dir / "terrain.asset.json"
    if manifest_path.exists():
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
        asset_id = str(data.get("id") or "").strip()
        res = int(data["resolution"]) if "resolution" in data else None
        if asset_id:
            return asset_id, res, data

    albedos = sorted(bundle_dir.glob("*_albedo.png"))
    if albedos:
        stem = albedos[0].name[: -len("_albedo.png")]
        return stem, None, {}

    dir_name = bundle_dir.name
    if dir_name.startswith("terrain_"):
        dir_name = dir_name[len("terrain_") :]
    return dir_name, None, {}


def _wrap_edge_metrics(arr: np.ndarray) -> dict[str, float]:
    """Compute horizontal/vertical wrap deltas and seam/interior ratios on a periodic array."""
    f = arr.astype(np.float32)
    if f.ndim == 2:
        int_h = float(np.mean(np.abs(f[:, 1:] - f[:, :-1])))
        int_v = float(np.mean(np.abs(f[1:, :] - f[:-1, :])))
        seam_h = float(np.mean(np.abs(f[:, 0] - f[:, -1])))
        seam_v = float(np.mean(np.abs(f[0, :] - f[-1, :])))
    else:
        int_h = float(np.mean(np.linalg.norm(f[:, 1:] - f[:, :-1], axis=-1)))
        int_v = float(np.mean(np.linalg.norm(f[1:, :] - f[:-1, :], axis=-1)))
        seam_h = float(np.mean(np.linalg.norm(f[:, 0] - f[:, -1], axis=-1)))
        seam_v = float(np.mean(np.linalg.norm(f[0, :] - f[-1, :], axis=-1)))

    interior = 0.5 * (int_h + int_v)
    seam = 0.5 * (seam_h + seam_v)
    if interior < 1e-4:
        ratio = 1.0 if seam < 1e-3 else round(seam / 1e-4, 4)
    else:
        ratio = round(seam / interior, 4)

    # Low-frequency macro wrap ratio catches tonal seams without penalizing sharp micro-texture.
    # For sigma=2.2 BORDER_REFLECT, half-Gaussian centroids sit ~1.76px inside each edge (total span ~4.5-5px).
    blurred = cv2.GaussianBlur(f, (7, 7), sigmaX=2.2, sigmaY=2.2)
    step = min(5, max(1, f.shape[1] // 4))
    if blurred.ndim == 2:
        lf_int_h = float(np.mean(np.abs(blurred[:, step:] - blurred[:, :-step])))
        lf_int_v = float(np.mean(np.abs(blurred[step:, :] - blurred[:-step, :])))
        lf_h = float(np.mean(np.abs(blurred[:, 0] - blurred[:, -1])))
        lf_v = float(np.mean(np.abs(blurred[0, :] - blurred[-1, :])))
    else:
        lf_int_h = float(np.mean(np.linalg.norm(blurred[:, step:] - blurred[:, :-step], axis=-1)))
        lf_int_v = float(np.mean(np.linalg.norm(blurred[step:, :] - blurred[:-step, :], axis=-1)))
        lf_h = float(np.mean(np.linalg.norm(blurred[:, 0] - blurred[:, -1], axis=-1)))
        lf_v = float(np.mean(np.linalg.norm(blurred[0, :] - blurred[-1, :], axis=-1)))
    lf_interior = 0.5 * (lf_int_h + lf_int_v)
    lf_seam = 0.5 * (lf_h + lf_v)
    lf_ratio = 1.0 if lf_interior < 1e-4 and lf_seam < 1e-3 else round(lf_seam / max(1e-4, lf_interior), 4)

    return {
        "seam_h_delta": round(seam_h, 3),
        "seam_v_delta": round(seam_v, 3),
        "mean_border_delta": round(seam, 3),
        "interior_delta": round(interior, 3),
        "lowfreq_border_delta": round(lf_seam, 3),
        "lowfreq_ratio": lf_ratio,
        "seam_ratio": ratio,
    }


def _compute_cielab_luma_stats(rgb_u8: np.ndarray) -> dict[str, float]:
    lab = cv2.cvtColor(rgb_u8, cv2.COLOR_RGB2LAB).astype(np.float32)
    l_star = lab[..., 0] * (100.0 / 255.0)
    return {
        "mean_L": round(float(np.mean(l_star)), 2),
        "std_L": round(float(np.std(l_star)), 2),
        "p05_L": round(float(np.percentile(l_star, 5)), 2),
        "p95_L": round(float(np.percentile(l_star, 95)), 2),
    }


def _compute_normal_qa(normal_rgb: np.ndarray) -> dict[str, Any]:
    nx = (normal_rgb[..., 0].astype(np.float32) - 127.5) / 127.5
    ny = (normal_rgb[..., 1].astype(np.float32) - 127.5) / 127.5
    nz = (normal_rgb[..., 2].astype(np.float32) - 127.5) / 127.5
    mags = np.sqrt(nx * nx + ny * ny + nz * nz)
    b_chan = normal_rgb[..., 2]
    xy_std = float(np.std(nx) + np.std(ny))
    return {
        "mean_B": round(float(np.mean(b_chan)), 2),
        "min_B": int(np.min(b_chan)),
        "unit_mag_mean": round(float(np.mean(mags)), 4),
        "xy_std": round(xy_std, 4),
    }


def _render_qa_contact_sheet(
    out_path: Path,
    asset_id: str,
    res: int,
    albedo: np.ndarray,
    normal: np.ndarray,
    height: np.ndarray,
    ao: np.ndarray,
    roughness: np.ndarray,
    mask: np.ndarray,
    report: dict[str, Any],
) -> None:
    """Render 1024x840 2x2 seamless contact sheet with Height/AO/Roughness/Mask strip and telemetry."""
    panel_size = 512
    sub_size = 256
    footer_h = 72
    canvas_w = panel_size * 2
    canvas_h = panel_size + sub_size + footer_h

    def tile_2x2(rgb_arr: np.ndarray) -> Image.Image:
        tiled = np.tile(rgb_arr, (2, 2, 1))
        return Image.fromarray(tiled, mode="RGB").resize(
            (panel_size, panel_size), Image.Resampling.LANCZOS
        )

    def gray_panel(gray_arr: np.ndarray) -> Image.Image:
        rgb = np.stack([gray_arr, gray_arr, gray_arr], axis=-1)
        return Image.fromarray(rgb, mode="RGB").resize(
            (sub_size, sub_size), Image.Resampling.LANCZOS
        )

    smooth_rgb = np.stack([mask[..., 3], mask[..., 3], mask[..., 3]], axis=-1)
    smooth_panel = Image.fromarray(smooth_rgb, mode="RGB").resize(
        (sub_size, sub_size), Image.Resampling.LANCZOS
    )

    sheet = Image.new("RGB", (canvas_w, canvas_h), (20, 22, 28))
    sheet.paste(tile_2x2(albedo), (0, 0))
    sheet.paste(tile_2x2(normal), (panel_size, 0))
    sheet.paste(gray_panel(height), (0, panel_size))
    sheet.paste(gray_panel(ao), (sub_size, panel_size))
    sheet.paste(gray_panel(roughness), (sub_size * 2, panel_size))
    sheet.paste(smooth_panel, (sub_size * 3, panel_size))

    draw = ImageDraw.Draw(sheet)
    labels = [
        (8, 8, "2x2 ALBEDO WRAP"),
        (panel_size + 8, 8, "2x2 NORMAL (+Y)"),
        (8, panel_size + 8, "HEIGHT"),
        (sub_size + 8, panel_size + 8, "AO"),
        (sub_size * 2 + 8, panel_size + 8, "ROUGHNESS"),
        (sub_size * 3 + 8, panel_size + 8, "SMOOTHNESS (MASK.A)"),
    ]
    for lx, ly, text in labels:
        draw.rectangle([lx - 3, ly - 2, lx + len(text) * 6 + 6, ly + 14], fill=(15, 17, 22))
        draw.text((lx, ly), sanitize_ascii(text), fill=(235, 240, 245))

    sm = report["seamless_metrics"]
    nq = report["normal_qa"]
    lq = report["luma_contrast_qa"]
    status_str = "PASS" if report["passed"] else "FAIL"
    line1 = sanitize_ascii(
        f"[{status_str}] Terrain: {asset_id} ({res}x{res}) | "
        f"AlbSeam={sm['albedo']['seam_ratio']} (dRGB={sm['albedo']['mean_border_delta']}) | "
        f"NrmSeam={sm['normal']['seam_ratio']} | HgtSeam={sm['height']['seam_ratio']}"
    )
    line2 = sanitize_ascii(
        f"Normal: mean_B={nq['mean_B']} min_B={nq['min_B']} mag={nq['unit_mag_mean']} xy_std={nq['xy_std']} | "
        f"Luma: mean_L={lq['mean_L']} std_L={lq['std_L']}"
    )
    y_foot = panel_size + sub_size
    draw.rectangle([0, y_foot, canvas_w, canvas_h], fill=(16, 18, 24))
    draw.text((12, y_foot + 14), line1, fill=(130, 240, 160) if report["passed"] else (255, 110, 110))
    draw.text((12, y_foot + 38), line2, fill=(210, 220, 230))

    sheet.save(out_path, format="PNG")


def verify_terrain_bundle(bundle_dir: Path, render_contact_sheet: bool = True) -> dict[str, Any]:
    """Verify 6-map seamless PBR terrain bundle integrity, wrap continuity, normal math, and luma."""
    bdir = Path(bundle_dir).resolve()
    if not bdir.is_dir():
        raise FileNotFoundError(f"Terrain bundle directory not found: {bdir}")

    asset_id, expected_res, _manifest = _resolve_id_and_res(bdir)
    map_paths = {
        "albedo": bdir / f"{asset_id}_albedo.png",
        "normal": bdir / f"{asset_id}_normal.png",
        "height": bdir / f"{asset_id}_height.png",
        "roughness": bdir / f"{asset_id}_roughness.png",
        "ao": bdir / f"{asset_id}_ao.png",
        "mask": bdir / f"{asset_id}_mask.png",
    }

    missing = [k for k, p in map_paths.items() if not p.exists()]
    failures: list[str] = []
    if missing:
        failures.append(f"Missing maps: {', '.join(missing)}")
        return {
            "passed": False,
            "id": asset_id,
            "bundle_dir": str(bdir),
            "resolution": expected_res or 0,
            "files_ok": False,
            "missing_files": missing,
            "dimension_errors": [],
            "seamless_metrics": {},
            "normal_qa": {},
            "luma_contrast_qa": {},
            "mask_qa": {},
            "qa_contact_sheet": None,
            "failures": failures,
        }

    with Image.open(map_paths["albedo"]) as im:
        albedo = np.array(im.convert("RGB"))
    with Image.open(map_paths["normal"]) as im:
        normal = np.array(im.convert("RGB"))
    with Image.open(map_paths["height"]) as im:
        height = np.array(im.convert("L"))
    with Image.open(map_paths["roughness"]) as im:
        roughness = np.array(im.convert("L"))
    with Image.open(map_paths["ao"]) as im:
        ao = np.array(im.convert("L"))
    with Image.open(map_paths["mask"]) as im:
        mask = np.array(im.convert("RGBA"))

    res = expected_res if expected_res is not None else int(albedo.shape[0])
    dim_errors: list[str] = []
    arrays = {
        "albedo": albedo,
        "normal": normal,
        "height": height,
        "roughness": roughness,
        "ao": ao,
        "mask": mask,
    }
    for key, arr in arrays.items():
        if arr.shape[0] != res or arr.shape[1] != res:
            dim_errors.append(f"{key} shape {arr.shape[1]}x{arr.shape[0]} != {res}x{res}")
    if dim_errors:
        failures.extend(dim_errors)

    seamless_metrics = {
        "albedo": _wrap_edge_metrics(albedo),
        "normal": _wrap_edge_metrics(normal),
        "height": _wrap_edge_metrics(height),
        "mask": _wrap_edge_metrics(mask),
    }
    normal_qa = _compute_normal_qa(normal)
    luma_qa = _compute_cielab_luma_stats(albedo)

    mask_r_max = int(np.max(mask[..., 0]))
    mask_g_diff = int(np.max(np.abs(mask[..., 1].astype(np.int16) - ao.astype(np.int16))))
    mask_b_diff = int(np.max(np.abs(mask[..., 2].astype(np.int16) - height.astype(np.int16))))
    expected_smooth = 255 - roughness.astype(np.int16)
    mask_a_diff = int(np.max(np.abs(mask[..., 3].astype(np.int16) - expected_smooth)))
    mask_qa = {
        "metallic_r_max": mask_r_max,
        "ao_g_max_diff": mask_g_diff,
        "height_b_max_diff": mask_b_diff,
        "smoothness_a_max_diff": mask_a_diff,
        "packing_ok": bool(mask_r_max == 0 and mask_g_diff <= 1 and mask_b_diff <= 1 and mask_a_diff <= 1),
    }

    if seamless_metrics["albedo"]["seam_ratio"] > 1.35:
        failures.append(f"albedo seam_ratio {seamless_metrics['albedo']['seam_ratio']} > 1.35")
    if seamless_metrics["albedo"]["lowfreq_ratio"] > 1.85:
        failures.append(
            f"albedo lowfreq_ratio {seamless_metrics['albedo']['lowfreq_ratio']} > 1.85"
        )
    if seamless_metrics["normal"]["seam_ratio"] > 1.50:
        failures.append(f"normal seam_ratio {seamless_metrics['normal']['seam_ratio']} > 1.50")

    if normal_qa["mean_B"] <= 180.0:
        failures.append(f"normal mean_B {normal_qa['mean_B']} <= 180")
    if normal_qa["min_B"] < 128:
        failures.append(f"normal min_B {normal_qa['min_B']} < 128")
    if not (0.97 <= normal_qa["unit_mag_mean"] <= 1.03):
        failures.append(f"normal unit_mag_mean {normal_qa['unit_mag_mean']} outside [0.97, 1.03]")
    if normal_qa["xy_std"] < 0.03:
        failures.append(f"normal xy_std {normal_qa['xy_std']} < 0.03")

    if luma_qa["mean_L"] < 18.0 or luma_qa["mean_L"] > 86.0:
        failures.append(f"albedo mean_L {luma_qa['mean_L']} outside [18.0, 86.0]")
    if luma_qa["std_L"] < 4.0:
        failures.append(f"albedo std_L {luma_qa['std_L']} < 4.0")
    if not mask_qa["packing_ok"]:
        failures.append("mask RGBA channel packing mismatch")

    passed = len(failures) == 0
    report: dict[str, Any] = {
        "passed": passed,
        "id": asset_id,
        "bundle_dir": str(bdir),
        "resolution": res,
        "files_ok": True,
        "missing_files": [],
        "dimension_errors": dim_errors,
        "seamless_metrics": seamless_metrics,
        "normal_qa": normal_qa,
        "luma_contrast_qa": luma_qa,
        "mask_qa": mask_qa,
        "qa_contact_sheet": None,
        "failures": failures,
    }

    if render_contact_sheet and not dim_errors:
        qa_path = bdir / f"{asset_id}_qa_2x2.png"
        _render_qa_contact_sheet(
            out_path=qa_path,
            asset_id=asset_id,
            res=res,
            albedo=albedo,
            normal=normal,
            height=height,
            ao=ao,
            roughness=roughness,
            mask=mask,
            report=report,
        )
        report["qa_contact_sheet"] = str(qa_path)

    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Verify Realmz seamless PBR terrain bundle")
    parser.add_argument("bundle_dir", help="Path to terrain job or inbox directory")
    parser.add_argument("--no-sheet", action="store_true", help="Skip rendering <id>_qa_2x2.png")
    parser.add_argument("--json", action="store_true", dest="as_json", help="Compact JSON output")
    args = parser.parse_args(argv)

    result = verify_terrain_bundle(Path(args.bundle_dir), render_contact_sheet=not args.no_sheet)
    if args.as_json:
        print(sanitize_ascii(json.dumps(result, ensure_ascii=True)))
    else:
        print(sanitize_ascii(json.dumps(result, indent=2)))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
