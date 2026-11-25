"""
Realmz Asset Foundry V1 - HD-2D Palette-Quantized Sprite & Normal Map Baker.

Takes raw orthographic albedo + view-space OpenGL (+Y up) normal renders
from `blender_rig_bake.py --mode render_views` and produces crisp 1x pixel-art
spritesheets (`<id>_sheet.png`) and matching unit-normalized OpenGL normal
maps (`<id>_sheet_n.png`) with 1-bit alpha [0, 255], BottomCenter pivot framing,
zero mixels, and Songs-of-Conquest 128-color sunlit palette quantization.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

DEFAULT_ART_PIPELINE_DIR = Path(r"C:\Users\karti\realmz\remaster\art-pipeline")
DEFAULT_POSTPROCESS_M15_PATH = DEFAULT_ART_PIPELINE_DIR / "postprocess_m15.py"
DEFAULT_PALETTE_PATH = DEFAULT_ART_PIPELINE_DIR / "palette_soc_sunlit_128.json"


def _load_postprocess_m15(module_path: Path = DEFAULT_POSTPROCESS_M15_PATH) -> Any:
    """Import postprocess_m15 cleanly from art-pipeline without requiring package install."""
    if not module_path.exists():
        raise FileNotFoundError(f"Expected art-pipeline module at {module_path}")
    spec = importlib.util.spec_from_file_location("realmz_postprocess_m15", str(module_path))
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not load spec from {module_path}")
    mod = importlib.util.module_from_spec(spec)
    sys.modules["realmz_postprocess_m15"] = mod
    spec.loader.exec_module(mod)
    return mod


def _default_size_for_category(category: str) -> int:
    cat = category.strip().lower()
    if cat in ("building", "tree", "large_tree"):
        return 256
    return 128


def _clean_foreground_mask(alpha_u8: np.ndarray) -> np.ndarray:
    """Convert raw alpha channel to clean 1-bit foreground mask removing tiny 1-3px render dust."""
    fg = alpha_u8 >= 128
    labeled, n_comp = ndimage.label(fg)
    if n_comp > 1:
        sizes = ndimage.sum(fg, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(16.0, max_sz * 0.005):
                keep |= labeled == idx
        fg = keep
    return fg


def _snap_pair_to_1x_grid(
    crop_rgb: np.ndarray,
    crop_norm_rgb: np.ndarray,
    crop_mask: np.ndarray,
    target_size: int,
    pad_px: int,
    scale: float,
    bottom_pivot: bool = True,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Snap a single cropped frame (albedo + normal) onto a target_size x target_size
    1x pixel grid using shared `scale` so multi-frame strips never pop in size.
    """
    ch, cw = crop_mask.shape
    out_w = max(1, min(target_size - 2 * pad_px, int(round(cw * scale))))
    out_h = max(1, min(target_size - 2 * pad_px, int(round(ch * scale))))

    ox = (target_size - out_w) // 2
    oy = (target_size - pad_px - out_h) if bottom_pivot else (target_size - out_h) // 2

    albedo_1x = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    normal_1x = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    normal_1x[..., 0] = 128
    normal_1x[..., 1] = 128
    normal_1x[..., 2] = 255
    normal_1x[..., 3] = 0

    lum = (
        0.2126 * crop_rgb[..., 0].astype(np.float32)
        + 0.7152 * crop_rgb[..., 1].astype(np.float32)
        + 0.0722 * crop_rgb[..., 2].astype(np.float32)
    )

    # Decode raw normal RGB into float32 vectors in [-1, 1]
    norm_vec = (crop_norm_rgb.astype(np.float32) - 127.5) / 127.5

    y_edges = np.linspace(0, ch, out_h + 1)
    x_edges = np.linspace(0, cw, out_w + 1)

    for ty in range(out_h):
        sy0 = int(np.floor(y_edges[ty]))
        sy1 = min(ch, max(sy0 + 1, int(np.ceil(y_edges[ty + 1]))))
        for tx in range(out_w):
            sx0 = int(np.floor(x_edges[tx]))
            sx1 = min(cw, max(sx0 + 1, int(np.ceil(x_edges[tx + 1]))))

            cell_m = crop_mask[sy0:sy1, sx0:sx1]
            if float(np.mean(cell_m)) < 0.44:
                continue

            cell_rgb = crop_rgb[sy0:sy1, sx0:sx1][cell_m]
            cell_lum = lum[sy0:sy1, sx0:sx1][cell_m]

            p15 = float(np.percentile(cell_lum, 15))
            p50 = float(np.percentile(cell_lum, 50))
            p85 = float(np.percentile(cell_lum, 85))

            if p15 < 28.0 and (p50 - p15) > 40.0:
                sel = cell_rgb[cell_lum <= p15 + 4.0]
                chosen = np.median(sel, axis=0)
            elif p85 > 205.0 and (p85 - p50) > 36.0:
                sel = cell_rgb[cell_lum >= p85 - 4.0]
                chosen = np.median(sel, axis=0)
            else:
                chosen = np.median(cell_rgb, axis=0)

            albedo_1x[oy + ty, ox + tx, :3] = np.clip(np.round(chosen), 0, 255).astype(np.uint8)
            albedo_1x[oy + ty, ox + tx, 3] = 255

            # Snap and re-normalize OpenGL (+Y up) view-space normal vector
            cell_n = norm_vec[sy0:sy1, sx0:sx1][cell_m]
            mean_n = np.mean(cell_n, axis=0)
            mean_n[2] = max(0.05, float(mean_n[2]))
            mag = float(np.linalg.norm(mean_n))
            if mag < 1e-6:
                mean_n = np.array([0.0, 0.0, 1.0], dtype=np.float32)
            else:
                mean_n /= mag

            normal_1x[oy + ty, ox + tx, 0] = np.uint8(np.clip(round(float(mean_n[0]) * 127.5 + 127.5), 0, 255))
            normal_1x[oy + ty, ox + tx, 1] = np.uint8(np.clip(round(float(mean_n[1]) * 127.5 + 127.5), 0, 255))
            normal_1x[oy + ty, ox + tx, 2] = np.uint8(np.clip(round(float(mean_n[2]) * 127.5 + 127.5), 128, 255))
            normal_1x[oy + ty, ox + tx, 3] = 255

    # Remove isolated 1-3px stray dust on the 1x alpha grid and sync normal mask
    alpha_bin = albedo_1x[..., 3] > 0
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(4.0, max_sz * 0.004):
                drop = labeled == idx
                albedo_1x[drop] = 0
                normal_1x[drop] = [128, 128, 255, 0]

    return albedo_1x, normal_1x


def _apply_crisp_1px_outline_accent(sprite_1x: np.ndarray) -> np.ndarray:
    """
    Subtly darken the 4-connected 1px silhouette perimeter toward deep indigo ink
    (#11131F) for Songs-of-Conquest HD-2D sprite readability without bloating bounds.
    """
    out = sprite_1x.copy()
    alpha = out[..., 3] == 255
    if not np.any(alpha):
        return out
    struct4 = np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], dtype=bool)
    inner = ndimage.binary_erosion(alpha, structure=struct4, border_value=0)
    rim = alpha & (~inner)
    if np.any(rim):
        ink = np.array([17.0, 19.0, 31.0], dtype=np.float32)  # #11131F
        rgb_f = out[rim, :3].astype(np.float32)
        blended = np.clip(np.round(rgb_f * 0.60 + ink[None, :] * 0.40), 0, 255).astype(np.uint8)
        out[rim, :3] = blended
    return out


def bake_hd2d_from_renders(
    albedo_raw_path: str | Path,
    normal_raw_path: str | Path,
    out_dir: str | Path | None = None,
    asset_id: str | None = None,
    category: str = "Monster",
    target_size: int | None = None,
    frames: int | None = None,
    pad_px: int = 3,
    quantize: bool = True,
    palette_path: str | Path = DEFAULT_PALETTE_PATH,
    dither_strength: float = 0.0,
    grade_strength: float = 0.14,
    shadow_lift: float = 4.5,
    vibrance_boost: float = 1.08,
    outline_dark: bool = True,
    bottom_pivot: bool = True,
    out_albedo: str | Path | None = None,
    out_normal: str | Path | None = None,
) -> Dict[str, Any]:
    """
    Bake raw Blender orthographic albedo + camera-space normal renders into
    HD-2D 1x palette-quantized spritesheet (`<id>_sheet.png`) and OpenGL (+Y up)
    normal map (`<id>_sheet_n.png`), returning QA metrics.
    """
    albedo_raw_p = Path(albedo_raw_path)
    normal_raw_p = Path(normal_raw_path)
    if not albedo_raw_p.exists():
        raise FileNotFoundError(f"Missing raw albedo render: {albedo_raw_p}")
    if not normal_raw_p.exists():
        raise FileNotFoundError(f"Missing raw normal render: {normal_raw_p}")

    if out_albedo is None:
        if out_dir is None or asset_id is None:
            raise ValueError("Provide either out_albedo or both out_dir and asset_id.")
        out_albedo_p = Path(out_dir) / f"{asset_id}_sheet.png"
    else:
        out_albedo_p = Path(out_albedo)

    out_normal_p = (
        Path(out_normal)
        if out_normal is not None
        else out_albedo_p.with_name(f"{out_albedo_p.stem}_n.png")
    )

    m15 = _load_postprocess_m15()
    palette_rgb = m15.load_palette(Path(palette_path))

    raw_alb = np.array(Image.open(albedo_raw_p).convert("RGBA"))
    raw_nrm = np.array(Image.open(normal_raw_p).convert("RGBA"))
    if raw_alb.shape[:2] != raw_nrm.shape[:2]:
        raise ValueError(
            f"Raw albedo shape {raw_alb.shape[:2]} != raw normal shape {raw_nrm.shape[:2]}"
        )

    raw_h, raw_w, _ = raw_alb.shape
    if frames is None:
        if raw_w > raw_h and (raw_w % raw_h == 0):
            frames = raw_w // raw_h
        else:
            frames = 1

    if raw_w % frames != 0:
        raise ValueError(f"Raw image width {raw_w} is not divisible by frames={frames}")

    cell_w = raw_w // frames
    size = int(target_size) if target_size is not None else _default_size_for_category(category)

    # Split into per-frame slices and compute unified bounding box across all frames
    frame_rgbs: List[np.ndarray] = []
    frame_nrms: List[np.ndarray] = []
    frame_masks: List[np.ndarray] = []

    y_min, y_max = raw_h, 0
    x_min, x_max = cell_w, 0
    any_fg = False

    for i in range(frames):
        xs = i * cell_w
        xe = (i + 1) * cell_w
        alb_slice = raw_alb[:, xs:xe]
        nrm_slice = raw_nrm[:, xs:xe]
        fg_mask = _clean_foreground_mask(alb_slice[..., 3])
        ys_idx, xs_idx = np.where(fg_mask)
        if len(ys_idx) > 0:
            any_fg = True
            y_min = min(y_min, int(ys_idx.min()))
            y_max = max(y_max, int(ys_idx.max()) + 1)
            x_min = min(x_min, int(xs_idx.min()))
            x_max = max(x_max, int(xs_idx.max()) + 1)
        frame_rgbs.append(alb_slice[..., :3])
        frame_nrms.append(nrm_slice[..., :3])
        frame_masks.append(fg_mask)

    if not any_fg:
        raise RuntimeError(f"Foreground mask is empty in {albedo_raw_p}")

    crop_h = max(1, y_max - y_min)
    crop_w = max(1, x_max - x_min)
    avail = max(1, size - 2 * pad_px)
    shared_scale = min(avail / float(crop_w), avail / float(crop_h))

    snapped_alb_frames: List[np.ndarray] = []
    snapped_nrm_frames: List[np.ndarray] = []

    for i in range(frames):
        c_rgb = frame_rgbs[i][y_min:y_max, x_min:x_max]
        c_nrm = frame_nrms[i][y_min:y_max, x_min:x_max]
        c_msk = frame_masks[i][y_min:y_max, x_min:x_max]

        alb_1x, nrm_1x = _snap_pair_to_1x_grid(
            c_rgb,
            c_nrm,
            c_msk,
            target_size=size,
            pad_px=pad_px,
            scale=shared_scale,
            bottom_pivot=bottom_pivot,
        )

        if outline_dark:
            alb_1x = _apply_crisp_1px_outline_accent(alb_1x)

        if quantize:
            alb_1x = m15.apply_sunlit_grade_and_quantize(
                alb_1x,
                palette_rgb,
                dither_strength=dither_strength,
                grade_strength=grade_strength,
                shadow_lift=shadow_lift,
                vibrance_boost=vibrance_boost,
            )
        else:
            # Enforce crisp 1-bit alpha and zero RGB in transparent pixels
            op = alb_1x[..., 3] >= 128
            alb_1x[..., 3] = np.where(op, 255, 0).astype(np.uint8)
            alb_1x[~op] = 0

        # Ensure normal mask strictly matches final 1-bit alpha
        final_op = alb_1x[..., 3] == 255
        nrm_1x[~final_op] = [128, 128, 255, 0]
        nrm_1x[final_op, 3] = 255

        snapped_alb_frames.append(alb_1x)
        snapped_nrm_frames.append(nrm_1x)

    sheet_alb = np.concatenate(snapped_alb_frames, axis=1) if frames > 1 else snapped_alb_frames[0]
    sheet_nrm = np.concatenate(snapped_nrm_frames, axis=1) if frames > 1 else snapped_nrm_frames[0]

    out_albedo_p.parent.mkdir(parents=True, exist_ok=True)
    out_normal_p.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(sheet_alb, mode="RGBA").save(out_albedo_p, format="PNG")
    Image.fromarray(sheet_nrm, mode="RGBA").save(out_normal_p, format="PNG")

    # Compute exact QA metrics
    opaque = sheet_alb[..., 3] == 255
    alpha_vals = sorted(int(v) for v in np.unique(sheet_alb[..., 3]))
    alpha_1bit_ok = bool(alpha_vals in ([0, 255], [255]))

    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used_colors = {tuple(int(c) for c in row) for row in sheet_alb[opaque, :3]} if np.any(opaque) else set()
    off_palette = len(used_colors - pal_set)

    n_op = (sheet_nrm[opaque, :3].astype(np.float32) - 127.5) / 127.5 if np.any(opaque) else np.zeros((1, 3), dtype=np.float32)
    mags = np.linalg.norm(n_op, axis=-1)

    bg_nrm = sheet_nrm[~opaque]
    if len(bg_nrm) == 0:
        normal_neutral_pct = 100.0
    else:
        neutral_match = np.all(bg_nrm[:, :3] == np.array([128, 128, 255], dtype=np.uint8), axis=1)
        normal_neutral_pct = round(100.0 * float(np.mean(neutral_match)), 2)

    normal_bg_neutral_ok = bool(
        len(bg_nrm) == 0
        or np.all(bg_nrm == np.array([128, 128, 255, 0], dtype=np.uint8))
    )

    return {
        "width": int(sheet_alb.shape[1]),
        "height": int(sheet_alb.shape[0]),
        "frames": int(frames),
        "palette_color_count": int(len(used_colors)),
        "off_palette_colors": int(off_palette),
        "quantized": bool(quantize),
        "alpha_1bit_ok": alpha_1bit_ok,
        "alpha_values": alpha_vals,
        "opaque_pixels": int(np.sum(opaque)),
        "opaque_coverage_pct": round(100.0 * float(np.mean(opaque)), 2),
        "normal_neutral_pct": normal_neutral_pct,
        "normal_bg_neutral_ok": normal_bg_neutral_ok,
        "normal_unit_mag_mean": round(float(mags.mean()), 4),
        "normal_min_z": round(float(n_op[:, 2].min()), 3),
        "normal_mean_rgb": [round(float(sheet_nrm[opaque, c].mean()), 1) for c in range(3)] if np.any(opaque) else [128.0, 128.0, 255.0],
        "albedo_path": str(out_albedo_p),
        "normal_path": str(out_normal_p),
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Realmz Asset Foundry - HD-2D Sprite & Normal Baker"
    )
    parser.add_argument("--albedo-raw", required=True, type=Path, help="Raw Blender albedo PNG")
    parser.add_argument("--normal-raw", required=True, type=Path, help="Raw Blender view-space normal PNG")
    parser.add_argument("--out-dir", required=True, type=Path, help="Output directory")
    parser.add_argument("--id", required=True, dest="asset_id", help="Asset ID prefix")
    parser.add_argument("--category", default="Monster", help="Asset category (Hero, Monster, Prop, Building, Tile)")
    parser.add_argument("--size", type=int, default=None, help="Target cell size (128 or 256)")
    parser.add_argument("--frames", type=int, default=None, help="Frame count (1 or 4)")
    parser.add_argument("--pad", type=int, default=3, help="Padding pixels")
    parser.add_argument("--no-quantize", action="store_true", help="Disable 128-color palette quantization")
    parser.add_argument("--no-outline", action="store_true", help="Disable 1px dark ink outline accent")
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH, help="Palette JSON path")
    args = parser.parse_args()

    stats = bake_hd2d_from_renders(
        albedo_raw_path=args.albedo_raw,
        normal_raw_path=args.normal_raw,
        out_dir=args.out_dir,
        asset_id=args.asset_id,
        category=args.category,
        target_size=args.size,
        frames=args.frames,
        pad_px=args.pad,
        quantize=not args.no_quantize,
        outline_dark=not args.no_outline,
        palette_path=args.palette,
    )
    print(json.dumps(stats, indent=2))


if __name__ == "__main__":
    main()
