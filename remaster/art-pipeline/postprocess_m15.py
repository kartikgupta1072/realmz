"""
Realmz M1.5 Pass A - Vibrant Hi-Bit (128x128 / 256x256) Pixel-Art & Laigter Normal-Map Post-Processor.

Upgrades over M1:
  - Locked to sunlit 128-color Songs-of-Conquest golden-hour palette (palette_soc_sunlit_128.json)
  - Crisp 128x128 (or 256x256 for large props/portraits) hi-bit snapping with zero mixels and 1-bit alpha [0, 255]
  - Aggressive halo-free cutout + circular backdrop plate / ground shadow removal
  - Sunlit golden-hour shadow lift + gentle chroma vibrance (fixes M1 crushed-black/muddy midtones)
  - Headless azagaya/laigter 1x OpenGL (+Y up, #8080FF) tangent-space normal maps for sprites, props, and
    3x3 wrap-tiled periodic seamless tiles (with unit-vector normalization and slope smoothing)
  - Automated QA metrics including 1-bit alpha, <=128 palette compliance, normal magnitude/orientation,
    brightness/mud check, circular-plate check, and tile seam ratio check.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
from collections import deque
from pathlib import Path
from typing import Tuple

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

DEFAULT_PALETTE_PATH = Path(__file__).parent / "palette_soc_sunlit_128.json"
DEFAULT_LAIGTER_EXE = Path(r"C:\ComfyUI\tools\laigter\laigter-windows\laigter.exe")


def hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    h = hex_color.strip().lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def load_palette(path: Path = DEFAULT_PALETTE_PATH) -> np.ndarray:
    data = json.loads(path.read_text(encoding="utf-8"))
    colors = data["colors"]
    unique_check = len(set(c.upper() for c in colors))
    if unique_check != len(colors) or len(colors) > 128:
        raise ValueError(f"Invalid palette {path}: {len(colors)} entries, {unique_check} unique")
    return np.array([hex_to_rgb(c) for c in colors], dtype=np.uint8)


def rgb_to_lab_float(rgb_u8: np.ndarray) -> np.ndarray:
    """Convert HxWx3 or Nx3 uint8 sRGB to float32 CIELAB (L in [0,100], a/b centered at 0)."""
    orig_shape = rgb_u8.shape
    img = rgb_u8.reshape(1, -1, 3) if rgb_u8.ndim == 2 else rgb_u8
    lab = cv2.cvtColor(img, cv2.COLOR_RGB2LAB).astype(np.float32)
    lab[..., 0] = lab[..., 0] * (100.0 / 255.0)
    lab[..., 1] = lab[..., 1] - 128.0
    lab[..., 2] = lab[..., 2] - 128.0
    return lab.reshape(orig_shape)


def lab_float_to_rgb_u8(lab: np.ndarray) -> np.ndarray:
    orig_shape = lab.shape
    arr = lab.reshape(1, -1, 3) if lab.ndim == 2 else lab.copy()
    cv_lab = np.empty_like(arr, dtype=np.uint8)
    cv_lab[..., 0] = np.clip(np.round(arr[..., 0] * (255.0 / 100.0)), 0, 255).astype(np.uint8)
    cv_lab[..., 1] = np.clip(np.round(arr[..., 1] + 128.0), 0, 255).astype(np.uint8)
    cv_lab[..., 2] = np.clip(np.round(arr[..., 2] + 128.0), 0, 255).astype(np.uint8)
    rgb = cv2.cvtColor(cv_lab, cv2.COLOR_LAB2RGB)
    return rgb.reshape(orig_shape)


def estimate_border_bg_color(rgb: np.ndarray) -> np.ndarray:
    """Estimate dominant background color from outer border strip."""
    h, w, _ = rgb.shape
    m = max(4, min(16, min(h, w) // 32))
    border = np.concatenate(
        [
            rgb[:m, :, :].reshape(-1, 3),
            rgb[-m:, :, :].reshape(-1, 3),
            rgb[:, :m, :].reshape(-1, 3),
            rgb[:, -m:, :].reshape(-1, 3),
        ],
        axis=0,
    )
    return np.median(border, axis=0).astype(np.uint8)


def remove_background_1bit(
    rgba: np.ndarray,
    bg_threshold: float | None = None,
    edge_threshold: float | None = None,
    erode_px: int = 0,
    strip_ground_shadow: bool = True,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Halo-free 1-bit cutout for solid white, lime-green, or magenta studio backdrops:
      - Adaptive CIELAB border tolerance + gradient-gated flood fill (stops at ink outlines)
      - Punches out enclosed uniform background pockets (between legs, weapons, branches, rafters)
      - Strips faint flat cast-shadow bands beneath feet/bases without touching real sprite outlines
      - Despills chroma fringe on foreground perimeter pixels
    """
    rgb = rgba[..., :3].copy()
    h, w, _ = rgb.shape

    alpha_in = rgba[..., 3]
    if np.mean(alpha_in < 128) > 0.15:
        fg_mask = alpha_in >= 128
        return rgb, fg_mask

    bg_rgb = estimate_border_bg_color(rgb)
    bg_lab = rgb_to_lab_float(bg_rgb.reshape(1, 3))[0]
    img_lab = rgb_to_lab_float(rgb)
    dist = np.linalg.norm(img_lab - bg_lab[None, None, :], axis=-1)

    m = max(4, min(16, min(h, w) // 32))
    border_d = np.concatenate(
        [
            dist[:m, :].ravel(),
            dist[-m:, :].ravel(),
            dist[:, :m].ravel(),
            dist[:, -m:].ravel(),
        ]
    )
    border_p99 = float(np.percentile(border_d, 99))
    chroma_sat = float(np.max(bg_rgb) - np.min(bg_rgb))
    is_white_bg = (bg_lab[0] > 88.0) and (chroma_sat < 28.0)

    if bg_threshold is None:
        if chroma_sat >= 45.0:
            bg_threshold = min(30.0, max(12.0, border_p99 * 2.5 + 7.5))
        elif is_white_bg:
            bg_threshold = min(15.0, max(9.5, border_p99 * 2.4 + 4.5))
        else:
            bg_threshold = min(14.0, max(9.5, border_p99 * 2.5 + 4.5))
    if edge_threshold is None:
        edge_threshold = min(bg_threshold + 6.0, 34.0 if chroma_sat >= 45.0 else 18.5)

    gx = cv2.Sobel(img_lab[..., 0], cv2.CV_32F, 1, 0, ksize=3) / 4.0
    gy = cv2.Sobel(img_lab[..., 0], cv2.CV_32F, 0, 1, ksize=3) / 4.0
    grad_l = np.hypot(gx, gy)

    bg_mask = np.zeros((h, w), dtype=bool)
    visited = np.zeros((h, w), dtype=bool)
    q: deque[Tuple[int, int]] = deque()

    def try_seed(y: int, x: int) -> None:
        if not visited[y, x] and dist[y, x] <= edge_threshold:
            visited[y, x] = True
            bg_mask[y, x] = True
            q.append((y, x))

    for x in range(w):
        try_seed(0, x)
        try_seed(h - 1, x)
    for y in range(h):
        try_seed(y, 0)
        try_seed(y, w - 1)

    while q:
        cy, cx = q.popleft()
        c_lab = img_lab[cy, cx]
        for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
            if 0 <= ny < h and 0 <= nx < w and not visited[ny, nx]:
                d_global = float(dist[ny, nx])
                d_local = float(np.linalg.norm(img_lab[ny, nx] - c_lab))
                if d_global <= bg_threshold:
                    visited[ny, nx] = True
                    bg_mask[ny, nx] = True
                    q.append((ny, nx))
                elif d_global <= edge_threshold and d_local <= 5.0 and grad_l[ny, nx] < 13.0:
                    visited[ny, nx] = True
                    bg_mask[ny, nx] = True
                    q.append((ny, nx))

    # Punch out uniform enclosed background pockets (gaps between legs, arms, weapons, foliage)
    pocket_thresh = max(5.5, bg_threshold * 0.80) if chroma_sat >= 45.0 else max(4.2, bg_threshold * 0.65)
    pocket_cand = (~bg_mask) & (dist <= pocket_thresh)
    p_lab, n_p = ndimage.label(pocket_cand)
    if n_p > 0:
        min_pocket = max(36, (h * w) // 20000)
        for idx in range(1, n_p + 1):
            comp = p_lab == idx
            sz = int(np.sum(comp))
            if sz >= min_pocket:
                comp_d = dist[comp]
                mean_cap = max(4.5, bg_threshold * 0.65) if chroma_sat >= 45.0 else max(3.2, bg_threshold * 0.50)
                if float(np.mean(comp_d)) <= mean_cap and float(np.std(comp_d)) < 3.5:
                    bg_mask |= comp

    fg_mask = ~bg_mask

    # Strip faint flat ground cast shadow directly beneath feet/base
    if strip_ground_shadow:
        ys_fg = np.where(fg_mask)[0]
        if len(ys_fg) > 0:
            y_top, y_bot = int(ys_fg.min()), int(ys_fg.max())
            foot_band = np.zeros_like(fg_mask)
            band_start = max(y_top, y_bot - max(14, int((y_bot - y_top) * 0.065)))
            foot_band[band_start : y_bot + 1, :] = True
            chroma_diff = np.hypot(img_lab[..., 1] - bg_lab[1], img_lab[..., 2] - bg_lab[2])
            l_diff = bg_lab[0] - img_lab[..., 0]
            shadow_like = (
                fg_mask
                & foot_band
                & (chroma_diff <= 5.5)
                & (l_diff >= 3.0)
                & (l_diff <= 22.0)
                & (img_lab[..., 0] > 16.0)
            )
            fg_mask = fg_mask & (~shadow_like)

    if erode_px > 0:
        fg_mask = ndimage.binary_erosion(fg_mask, structure=np.ones((3, 3), dtype=bool), iterations=erode_px)

    # Keep primary component and meaningful detached elements (>= 1.0% of max)
    labeled, n_comp = ndimage.label(fg_mask)
    if n_comp > 1:
        sizes = ndimage.sum(fg_mask, labeled, range(1, n_comp + 1))
        max_size = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(48.0, max_size * 0.010):
                keep |= labeled == idx
        fg_mask = keep

    # Perimeter despill for saturated chroma backdrops (green/magenta) AND anti-aliased white fringes
    rim_iter = max(2, min(6, h // 180))
    eroded_inner = ndimage.binary_erosion(fg_mask, iterations=rim_iter)
    rim = fg_mask & (~eroded_inner)
    if np.any(rim) and np.any(eroded_inner):
        if chroma_sat >= 45.0:
            # Replace chroma-polluted rim pixels from nearest clean interior pixel
            rim_bad = rim & (dist < edge_threshold * 1.45)
            # Also catch bright neon-green spill (G much higher than R and B on a green screen)
            if bg_rgb[1] > 170 and bg_rgb[0] < 120 and bg_rgb[2] < 120:
                green_spill = (rgb[..., 1].astype(np.int16) - np.maximum(rgb[..., 0], rgb[..., 2]).astype(np.int16)) > 35
                rim_bad |= rim & green_spill
            if np.any(rim_bad):
                _, nearest_idx = ndimage.distance_transform_edt(~eroded_inner, return_indices=True)
                ry, rx = np.where(rim_bad)
                rgb[ry, rx] = rgb[nearest_idx[0, ry, rx], nearest_idx[1, ry, rx]]
        elif is_white_bg:
            # Remove bright white anti-aliased fringe along outer 2px rim
            rim_bad = rim & (dist < edge_threshold * 1.25) & (img_lab[..., 0] > 82.0)
            if np.any(rim_bad):
                _, nearest_idx = ndimage.distance_transform_edt(~eroded_inner, return_indices=True)
                ry, rx = np.where(rim_bad)
                rgb[ry, rx] = rgb[nearest_idx[0, ry, rx], nearest_idx[1, ry, rx]]

    return rgb, fg_mask


def snap_to_1x_pixel_grid(
    rgb: np.ndarray,
    fg_mask: np.ndarray,
    target_size: int = 128,
    pad_px: int = 3,
    bottom_pivot: bool = True,
) -> np.ndarray:
    """
    Frame sprite/prop cleanly onto target_size x target_size canvas with crisp 1x pixel-grid snap.
    Tuned for hi-bit 128x128 / 256x256: preserves fine dark ink outlines and bright glints
    without darkening midtone fills.
    """
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Background removal produced an empty foreground mask.")

    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    crop_rgb = rgb[y0:y1, x0:x1]
    crop_mask = fg_mask[y0:y1, x0:x1]
    ch, cw = crop_mask.shape

    avail = target_size - 2 * pad_px
    scale = min(avail / cw, avail / ch)
    out_w = max(1, int(round(cw * scale)))
    out_h = max(1, int(round(ch * scale)))

    ox = (target_size - out_w) // 2
    oy = (target_size - pad_px - out_h) if bottom_pivot else (target_size - out_h) // 2

    canvas = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    lum = (
        0.2126 * crop_rgb[..., 0].astype(np.float32)
        + 0.7152 * crop_rgb[..., 1].astype(np.float32)
        + 0.0722 * crop_rgb[..., 2].astype(np.float32)
    )

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
                # Preserve only true crisp dark ink outline pixels (not midtones)
                sel = cell_rgb[cell_lum <= p15 + 4.0]
                chosen = np.median(sel, axis=0)
            elif p85 > 205.0 and (p85 - p50) > 36.0:
                # Preserve gleaming steel/gold/sunlight specular highlight
                sel = cell_rgb[cell_lum >= p85 - 4.0]
                chosen = np.median(sel, axis=0)
            else:
                chosen = np.median(cell_rgb, axis=0)

            canvas[oy + ty, ox + tx, :3] = np.clip(np.round(chosen), 0, 255).astype(np.uint8)
            canvas[oy + ty, ox + tx, 3] = 255

    # Remove isolated 1-3px stray dust on the 1x alpha grid
    alpha_bin = canvas[..., 3] > 0
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(4.0, max_sz * 0.004):
                canvas[labeled == idx] = 0

    return canvas


def snap_full_image_to_1x(rgb: np.ndarray, target_size: int = 256) -> np.ndarray:
    """Crisp hi-bit 1x downsampling for opaque 256x256 portraits (preserves sharp pixel clusters)."""
    h, w, _ = rgb.shape
    if target_size >= 200:
        # At 256x256 (4x downscale from 1024), gentle bilateral + area + crisp unsharp keeps eyes/armor razor sharp
        smooth = cv2.bilateralFilter(rgb, d=5, sigmaColor=24, sigmaSpace=5)
        small = cv2.resize(smooth, (target_size, target_size), interpolation=cv2.INTER_AREA)
        blur = cv2.GaussianBlur(small.astype(np.float32), (0, 0), sigmaX=0.7)
        sharp = np.clip(np.round(small.astype(np.float32) * 1.24 - blur * 0.24), 0, 255).astype(np.uint8)
        out = np.zeros((target_size, target_size, 4), dtype=np.uint8)
        out[..., :3] = sharp
        out[..., 3] = 255
        return out

    out = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    lum = (
        0.2126 * rgb[..., 0].astype(np.float32)
        + 0.7152 * rgb[..., 1].astype(np.float32)
        + 0.0722 * rgb[..., 2].astype(np.float32)
    )
    y_edges = np.linspace(0, h, target_size + 1)
    x_edges = np.linspace(0, w, target_size + 1)
    for ty in range(target_size):
        sy0 = int(np.floor(y_edges[ty]))
        sy1 = min(h, max(sy0 + 1, int(np.ceil(y_edges[ty + 1]))))
        for tx in range(target_size):
            sx0 = int(np.floor(x_edges[tx]))
            sx1 = min(w, max(sx0 + 1, int(np.ceil(x_edges[tx + 1]))))
            cell_rgb = rgb[sy0:sy1, sx0:sx1].reshape(-1, 3)
            cell_lum = lum[sy0:sy1, sx0:sx1].reshape(-1)
            p15 = float(np.percentile(cell_lum, 15))
            p50 = float(np.percentile(cell_lum, 50))
            p85 = float(np.percentile(cell_lum, 85))
            if p15 < 28.0 and (p50 - p15) > 38.0:
                chosen = np.median(cell_rgb[cell_lum <= p15 + 4.0], axis=0)
            elif p85 > 208.0 and (p85 - p50) > 38.0:
                chosen = np.median(cell_rgb[cell_lum >= p85 - 4.0], axis=0)
            else:
                chosen = np.median(cell_rgb, axis=0)
            out[ty, tx, :3] = np.clip(np.round(chosen), 0, 255).astype(np.uint8)
            out[ty, tx, 3] = 255
    return out


def snap_tile_clean_1x(rgb: np.ndarray, target_size: int = 128) -> np.ndarray:
    """Noise-free crisp 1x downsampling for seamless 128x128 terrain and dungeon tiles."""
    smooth = cv2.bilateralFilter(rgb, d=5, sigmaColor=28, sigmaSpace=5)
    small = cv2.resize(smooth, (target_size, target_size), interpolation=cv2.INTER_AREA)
    # Periodic Gaussian blur for seam-safe unsharp mask
    small_f = small.astype(np.float32)
    blur = np.stack(
        [ndimage.gaussian_filter(small_f[..., c], sigma=0.75, mode="wrap") for c in range(3)],
        axis=-1,
    )
    sharp = np.clip(np.round(small_f * 1.24 - blur * 0.24), 0, 255).astype(np.uint8)
    out = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    out[..., :3] = sharp
    out[..., 3] = 255
    return out


def apply_sunlit_grade_and_quantize(
    sprite_1x: np.ndarray,
    palette_rgb: np.ndarray,
    dither_strength: float = 0.0,
    grade_strength: float = 0.14,
    shadow_lift: float = 4.5,
    vibrance_boost: float = 1.08,
) -> np.ndarray:
    """
    Apply golden-hour Songs-of-Conquest sunlit grade (lifts crushed shadows, gently boosts
    vibrance, adds subtle warm sunlight on highlights and cool slate-indigo tint in deep
    shadows) and snap every opaque pixel to the locked 128-color palette in CIELAB space.
    """
    out = sprite_1x.copy()
    opaque = out[..., 3] == 255
    if not np.any(opaque):
        return out

    rgb_u8 = out[..., :3].copy()

    # Work in CIELAB for shadow lift + gentle vibrance without hue muddying
    if shadow_lift > 0.0 or vibrance_boost != 1.0:
        lab = rgb_to_lab_float(rgb_u8)
        L = lab[..., 0]
        # Lift dark midtones smoothly while preserving crisp dark ink outlines (L < 8 stays dark ink)
        lift_mask = np.clip((L - 6.0) / 14.0, 0.0, 1.0) * np.clip((52.0 - L) / 40.0, 0.0, 1.0)
        lab[..., 0] = np.clip(L + lift_mask * shadow_lift, 0.0, 100.0)
        # Gentle chroma vibrance boost (stronger on mid-saturation colors, avoids clipping already pure colors)
        chroma = np.hypot(lab[..., 1], lab[..., 2])
        vib_factor = 1.0 + (vibrance_boost - 1.0) * np.clip(1.0 - chroma / 85.0, 0.25, 1.0)
        lab[..., 1] *= vib_factor
        lab[..., 2] *= vib_factor
        rgb_u8 = lab_float_to_rgb_u8(lab)

    rgb = rgb_u8.astype(np.float32)

    if grade_strength > 0.0:
        lum = (0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]) / 255.0
        # Subtle cool slate-indigo in deep shadows, warm golden-hour sunlight on highlights
        shadow_w = np.clip((0.36 - lum) / 0.36, 0.0, 1.0) * grade_strength
        hi_w = np.clip((lum - 0.52) / 0.48, 0.0, 1.0) * grade_strength
        rgb[..., 0] = rgb[..., 0] - shadow_w * 5.0 + hi_w * 12.0
        rgb[..., 1] = rgb[..., 1] - shadow_w * 2.0 + hi_w * 8.0
        rgb[..., 2] = rgb[..., 2] + shadow_w * 12.0 - hi_w * 8.0
        rgb = np.clip(rgb, 0.0, 255.0)

    if dither_strength > 0.0:
        bayer2 = np.array([[-0.5, 0.5], [0.5, -0.5]], dtype=np.float32)
        h, w, _ = rgb.shape
        tiled = np.tile(bayer2, ((h + 1) // 2, (w + 1) // 2))[:h, :w]
        rgb = np.clip(rgb + tiled[..., None] * (dither_strength * 18.0), 0, 255.0)

    rgb_u8 = np.round(rgb).astype(np.uint8)
    pal_lab = rgb_to_lab_float(palette_rgb)
    pix_lab = rgb_to_lab_float(rgb_u8[opaque])

    # Chunked nearest-neighbor in CIELAB (keeps RAM low for 256x256 portraits)
    n_pix = pix_lab.shape[0]
    nearest = np.empty(n_pix, dtype=np.int32)
    chunk = 8192
    for start in range(0, n_pix, chunk):
        end = min(n_pix, start + chunk)
        diff = pix_lab[start:end, None, :] - pal_lab[None, :, :]
        dist2 = (1.10 * diff[..., 0] ** 2) + (1.15 * diff[..., 1] ** 2) + (1.15 * diff[..., 2] ** 2)
        nearest[start:end] = np.argmin(dist2, axis=1)

    quant_rgb = np.zeros_like(rgb_u8)
    quant_rgb[opaque] = palette_rgb[nearest]

    out[..., :3] = quant_rgb
    out[~opaque] = 0
    return out


def generate_normal_map_laigter(
    sprite_1x: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    periodic: bool = False,
    xy_scale: float = 0.68,
    min_z: float = 0.32,
) -> np.ndarray | None:
    """
    Generate crisp 1x OpenGL (+Y up, #8080FF neutral) tangent-space normal map using
    headless azagaya/laigter CLI.
    For seamless tiles (periodic=True), runs laigter on a 3x3 wrap-tiled canvas and crops
    the center 1x1 region so tile normals wrap seamlessly across all 4 edges.
    """
    if not laigter_exe.exists():
        return None

    h, w, _ = sprite_1x.shape
    alpha = sprite_1x[..., 3] == 255

    if periodic:
        feed = np.tile(sprite_1x, (3, 3, 1))
    else:
        feed = sprite_1x

    with tempfile.TemporaryDirectory(prefix="realmz_m15_laigter_") as td:
        tmp_in = Path(td) / "input.png"
        expected_out = Path(td) / "input_n.png"
        Image.fromarray(feed, mode="RGBA").save(tmp_in, format="PNG")

        env = os.environ.copy()
        env.setdefault("QT_QPA_PLATFORM", "windows")
        cmd = [str(laigter_exe), "-g", "-d", str(tmp_in), "-n"]
        try:
            proc = subprocess.run(
                cmd,
                cwd=str(laigter_exe.parent),
                env=env,
                capture_output=True,
                text=True,
                timeout=30,
                creationflags=0x08000000 if os.name == "nt" else 0,
            )
        except Exception:
            return None

        if proc.returncode != 0 or not expected_out.exists():
            return None

        raw_n = np.array(Image.open(expected_out).convert("RGBA"))
        if periodic:
            if raw_n.shape[:2] != (h * 3, w * 3):
                return None
            raw_n = raw_n[h : 2 * h, w : 2 * w]
        elif raw_n.shape[:2] != (h, w):
            return None

        nx = ((raw_n[..., 0].astype(np.float32) - 127.5) / 127.5) * xy_scale
        ny = ((raw_n[..., 1].astype(np.float32) - 127.5) / 127.5) * xy_scale
        nz = np.maximum(min_z, (raw_n[..., 2].astype(np.float32) - 127.5) / 127.5)
        mag = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
        nx /= mag
        ny /= mag
        nz /= mag

        if float(np.std(nx[alpha]) + np.std(ny[alpha])) < 0.025:
            return None

        out = np.zeros((h, w, 4), dtype=np.uint8)
        out[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
        out[..., 3] = 255
        if not periodic:
            out[~alpha] = [128, 128, 255, 0]
        return out


def generate_normal_map_python_fallback(
    sprite_1x: np.ndarray,
    periodic: bool = False,
    bevel_strength: float = 1.45,
    detail_strength: float = 1.25,
    bevel_radius: float = 6.0,
) -> np.ndarray:
    """Fallback OpenGL (+Y up) normal generator if laigter ever fails."""
    h, w, _ = sprite_1x.shape
    alpha = sprite_1x[..., 3] == 255
    rgb = sprite_1x[..., :3].astype(np.float32) / 255.0
    lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]

    if periodic:
        lum_smooth = ndimage.gaussian_filter(lum, sigma=0.75, mode="wrap")
        height = (2.1 * lum_smooth).astype(np.float32)
        padded = np.pad(height, 1, mode="wrap")
        gx = cv2.Sobel(padded, cv2.CV_32F, 1, 0, ksize=3)[1:-1, 1:-1] / 8.0
        gy = cv2.Sobel(padded, cv2.CV_32F, 0, 1, ksize=3)[1:-1, 1:-1] / 8.0
    else:
        dist_in = ndimage.distance_transform_edt(alpha).astype(np.float32)
        t = np.clip(dist_in / max(1.0, bevel_radius), 0.0, 1.0)
        bevel_h = np.sqrt(np.maximum(0.0, 2.0 * t - t * t))
        lum_clean = lum.copy()
        if np.any(alpha) and np.any(~alpha):
            _, idx_bg = ndimage.distance_transform_edt(~alpha, return_indices=True)
            by, bx = np.where(~alpha)
            lum_clean[by, bx] = lum_clean[idx_bg[0, by, bx], idx_bg[1, by, bx]]
        lum_smooth = ndimage.gaussian_filter(lum_clean, sigma=0.7)
        height = (bevel_strength * bevel_h + detail_strength * lum_smooth).astype(np.float32)
        gx = cv2.Sobel(height, cv2.CV_32F, 1, 0, ksize=3) / 8.0
        gy = cv2.Sobel(height, cv2.CV_32F, 0, 1, ksize=3) / 8.0

    nx = -gx
    ny = gy
    nz = np.ones_like(nx, dtype=np.float32)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
    nx /= norm
    ny /= norm
    nz /= norm

    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
    out[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
    out[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
    out[..., 3] = 255
    if not periodic:
        out[~alpha] = [128, 128, 255, 0]
    return out


def compute_tile_seam_ratio(tile_1x: np.ndarray) -> float:
    """Ratio of cross-border CIELAB difference to interior adjacent-pixel CIELAB difference."""
    lab = rgb_to_lab_float(tile_1x[..., :3])
    interior_h = np.linalg.norm(lab[:, 1:] - lab[:, :-1], axis=-1).mean()
    interior_v = np.linalg.norm(lab[1:, :] - lab[:-1, :], axis=-1).mean()
    seam_h = np.linalg.norm(lab[:, 0] - lab[:, -1], axis=-1).mean()
    seam_v = np.linalg.norm(lab[0, :] - lab[-1, :], axis=-1).mean()
    interior = max(1e-4, 0.5 * (interior_h + interior_v))
    seam = 0.5 * (seam_h + seam_v)
    return round(float(seam / interior), 3)


def compute_normal_seam_ratio(normal_1x: np.ndarray) -> float:
    """Periodic wrap seam ratio on tile normal map (~1.0 = seamless)."""
    arr = normal_1x[..., :3].astype(np.float32)
    interior_h = np.linalg.norm(arr[:, 1:] - arr[:, :-1], axis=-1).mean()
    interior_v = np.linalg.norm(arr[1:, :] - arr[:-1, :], axis=-1).mean()
    seam_h = np.linalg.norm(arr[:, 0] - arr[:, -1], axis=-1).mean()
    seam_v = np.linalg.norm(arr[0, :] - arr[-1, :], axis=-1).mean()
    interior = max(1e-4, 0.5 * (interior_h + interior_v))
    seam = 0.5 * (seam_h + seam_v)
    return round(float(seam / interior), 3)


def detect_circular_plate_artifact(sprite_1x: np.ndarray) -> dict:
    """
    Detect unwanted circular background plates/halos behind a cutout sprite by comparing
    the foreground mask's solidity and circularity (4*pi*Area / Perimeter^2) when bbox fill is high.
    """
    mask = (sprite_1x[..., 3] == 255).astype(np.uint8)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    if not contours:
        return {"circularity": 0.0, "bbox_fill": 0.0, "suspect_circle_plate": False}
    largest = max(contours, key=cv2.contourArea)
    area = float(cv2.contourArea(largest))
    perim = float(cv2.arcLength(largest, True))
    x, y, bw, bh = cv2.boundingRect(largest)
    bbox_fill = area / max(1.0, float(bw * bh))
    circularity = (4.0 * np.pi * area) / max(1.0, perim * perim)
    # A solid circle behind a humanoid has circularity > 0.72 and bbox_fill > 0.68
    suspect = bool(circularity > 0.72 and bbox_fill > 0.68)
    return {
        "circularity": round(circularity, 3),
        "bbox_fill": round(bbox_fill, 3),
        "suspect_circle_plate": suspect,
    }


def validate_outputs(
    sprite_1x: np.ndarray,
    normal_1x: np.ndarray,
    palette_rgb: np.ndarray,
    is_tile: bool = False,
) -> dict:
    opaque = sprite_1x[..., 3] == 255
    alpha_vals = sorted(int(v) for v in np.unique(sprite_1x[..., 3]))
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used_colors = {tuple(int(c) for c in row) for row in sprite_1x[opaque, :3]}
    off_palette = len(used_colors - pal_set)

    n = (normal_1x[opaque, :3].astype(np.float32) - 127.5) / 127.5
    mags = np.linalg.norm(n, axis=-1) if len(n) else np.array([1.0])
    bg_normals = normal_1x[~opaque]
    bg_neutral_ok = bool(
        len(bg_normals) == 0
        or np.all(bg_normals == np.array([128, 128, 255, 0], dtype=np.uint8))
    )

    # Brightness / vibrance telemetry in CIELAB
    lab_op = rgb_to_lab_float(sprite_1x[opaque, :3]) if np.any(opaque) else np.zeros((1, 3), dtype=np.float32)
    mean_L = float(lab_op[:, 0].mean())
    p90_L = float(np.percentile(lab_op[:, 0], 90))
    mean_chroma = float(np.hypot(lab_op[:, 1], lab_op[:, 2]).mean())
    dark_pixel_pct = float(100.0 * np.mean(lab_op[:, 0] < 16.0))

    stats = {
        "width": int(sprite_1x.shape[1]),
        "height": int(sprite_1x.shape[0]),
        "opaque_pixels": int(np.sum(opaque)),
        "opaque_coverage_pct": round(100.0 * float(np.mean(opaque)), 2),
        "alpha_values": alpha_vals,
        "alpha_is_1bit": alpha_vals in ([0, 255], [255]),
        "unique_palette_colors": len(used_colors),
        "off_palette_colors": off_palette,
        "mean_L": round(mean_L, 1),
        "p90_L": round(p90_L, 1),
        "mean_chroma": round(mean_chroma, 1),
        "dark_pixel_pct": round(dark_pixel_pct, 1),
        "normal_mean_rgb": [round(float(normal_1x[opaque, c].mean()), 1) for c in range(3)],
        "normal_xy_std": round(float(np.std(n[:, 0]) + np.std(n[:, 1])), 3) if len(n) else 0.0,
        "normal_unit_mag_mean": round(float(mags.mean()), 4),
        "normal_min_z": round(float(n[:, 2].min()), 3) if len(n) else 1.0,
        "normal_bg_neutral_ok": bg_neutral_ok,
    }
    if is_tile:
        stats["seam_ratio"] = compute_tile_seam_ratio(sprite_1x)
        stats["normal_seam_ratio"] = compute_normal_seam_ratio(normal_1x)
    else:
        stats.update(detect_circular_plate_artifact(sprite_1x))
    return stats


def process_sprite(
    input_path: Path,
    output_albedo: Path,
    output_normal: Path | None = None,
    target_size: int = 128,
    pad_px: int = 3,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    dither_strength: float = 0.0,
    grade_strength: float = 0.14,
    shadow_lift: float = 4.5,
    vibrance_boost: float = 1.08,
    normal_backend: str = "laigter",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    strip_ground_shadow: bool = True,
) -> dict:
    raw = np.array(Image.open(input_path).convert("RGBA"))
    palette_rgb = load_palette(palette_path)

    rgb_clean, fg_mask = remove_background_1bit(raw, strip_ground_shadow=strip_ground_shadow)
    sprite_1x = snap_to_1x_pixel_grid(rgb_clean, fg_mask, target_size=target_size, pad_px=pad_px)
    sprite_1x = apply_sunlit_grade_and_quantize(
        sprite_1x,
        palette_rgb,
        dither_strength=dither_strength,
        grade_strength=grade_strength,
        shadow_lift=shadow_lift,
        vibrance_boost=vibrance_boost,
    )

    backend_used = "python"
    normal_1x = None
    if normal_backend in ("auto", "laigter"):
        normal_1x = generate_normal_map_laigter(
            sprite_1x, laigter_exe=laigter_exe, periodic=False, xy_scale=0.68, min_z=0.32
        )
        if normal_1x is not None:
            backend_used = "laigter"
        elif normal_backend == "laigter":
            raise RuntimeError(f"Laigter normal generation failed for {input_path}")
    if normal_1x is None:
        normal_1x = generate_normal_map_python_fallback(sprite_1x, periodic=False)
        backend_used = "python"

    output_albedo.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(sprite_1x, mode="RGBA").save(output_albedo, format="PNG")
    if output_normal is None:
        output_normal = output_albedo.with_name(f"{output_albedo.stem}_n.png")
    output_normal.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(normal_1x, mode="RGBA").save(output_normal, format="PNG")

    stats = validate_outputs(sprite_1x, normal_1x, palette_rgb, is_tile=False)
    stats["normal_backend"] = backend_used
    stats["albedo_path"] = str(output_albedo)
    stats["normal_path"] = str(output_normal)
    return stats


def process_seamless_tile(
    raw_seamless_path: Path,
    output_albedo: Path,
    output_normal: Path,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    target_size: int = 128,
    dither_strength: float = 0.02,
    grade_strength: float = 0.12,
    shadow_lift: float = 4.0,
    vibrance_boost: float = 1.10,
    normal_backend: str = "laigter",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> dict:
    raw = np.array(Image.open(raw_seamless_path).convert("RGB"))
    palette_rgb = load_palette(palette_path)
    tile_1x = snap_tile_clean_1x(raw, target_size=target_size)
    tile_1x = apply_sunlit_grade_and_quantize(
        tile_1x,
        palette_rgb,
        dither_strength=dither_strength,
        grade_strength=grade_strength,
        shadow_lift=shadow_lift,
        vibrance_boost=vibrance_boost,
    )

    backend_used = "python_periodic"
    normal_1x = None
    if normal_backend in ("auto", "laigter"):
        normal_1x = generate_normal_map_laigter(
            tile_1x, laigter_exe=laigter_exe, periodic=True, xy_scale=0.62, min_z=0.38
        )
        if normal_1x is not None:
            backend_used = "laigter_periodic"
        elif normal_backend == "laigter":
            raise RuntimeError(f"Laigter periodic normal generation failed for {raw_seamless_path}")
    if normal_1x is None:
        normal_1x = generate_normal_map_python_fallback(tile_1x, periodic=True)
        backend_used = "python_periodic"

    output_albedo.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(tile_1x, mode="RGBA").save(output_albedo, format="PNG")
    output_normal.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(normal_1x, mode="RGBA").save(output_normal, format="PNG")

    stats = validate_outputs(tile_1x, normal_1x, palette_rgb, is_tile=True)
    stats["normal_backend"] = backend_used
    stats["albedo_path"] = str(output_albedo)
    stats["normal_path"] = str(output_normal)
    return stats


def process_portrait(
    raw_path: Path,
    output_path: Path,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    target_size: int = 256,
    frame_px: int = 4,
) -> dict:
    """Downsample hero portrait cleanly to 256x256 with crisp beveled sunlit gold/ink RPG frame."""
    raw = np.array(Image.open(raw_path).convert("RGB"))
    palette_rgb = load_palette(palette_path)
    inner_size = target_size - 2 * frame_px
    inner = snap_full_image_to_1x(raw, target_size=inner_size)
    inner = apply_sunlit_grade_and_quantize(
        inner,
        palette_rgb,
        dither_strength=0.02,
        grade_strength=0.14,
        shadow_lift=4.0,
        vibrance_boost=1.08,
    )

    canvas = np.zeros((target_size, target_size, 4), dtype=np.uint8)
    canvas[..., 3] = 255
    canvas[frame_px : target_size - frame_px, frame_px : target_size - frame_px] = inner

    ink = np.array(hex_to_rgb("#11131F") + (255,), dtype=np.uint8)
    gold_dark = np.array(hex_to_rgb("#7D4818") + (255,), dtype=np.uint8)
    gold_mid = np.array(hex_to_rgb("#ECA938") + (255,), dtype=np.uint8)
    gold_hi = np.array(hex_to_rgb("#FFE07D") + (255,), dtype=np.uint8)

    # Outer 1px ink border
    canvas[0, :] = ink
    canvas[-1, :] = ink
    canvas[:, 0] = ink
    canvas[:, -1] = ink
    # Inner 2px beveled gold frame
    canvas[1:3, 1:-1] = gold_mid
    canvas[1:-1, 1:3] = gold_mid
    canvas[-3:-1, 1:-1] = gold_dark
    canvas[1:-1, -3:-1] = gold_dark
    # Inner 1px crisp ink inset
    canvas[3, 3:-3] = ink
    canvas[-4, 3:-3] = ink
    canvas[3:-3, 3] = ink
    canvas[3:-3, -4] = ink
    # Corner gilded rivets
    for cy, cx in ((1, 1), (1, target_size - 3), (target_size - 3, 1), (target_size - 3, target_size - 3)):
        canvas[cy : cy + 2, cx : cx + 2] = gold_hi

    output_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(canvas, mode="RGBA").save(output_path, format="PNG")

    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used = {tuple(int(c) for c in row) for row in canvas.reshape(-1, 4)[:, :3]}
    alpha_vals = sorted(int(v) for v in np.unique(canvas[..., 3]))
    lab_all = rgb_to_lab_float(canvas[..., :3].reshape(-1, 3))
    return {
        "width": target_size,
        "height": target_size,
        "alpha_values": alpha_vals,
        "alpha_is_1bit": alpha_vals in ([0, 255], [255]),
        "unique_palette_colors": len(used),
        "off_palette_colors": len(used - pal_set),
        "mean_L": round(float(lab_all[:, 0].mean()), 1),
        "albedo_path": str(output_path),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Realmz M1.5 Hi-Bit Sprite/Tile/Portrait Post-Processor")
    parser.add_argument("input", type=Path, help="Raw ComfyUI PNG")
    parser.add_argument("-o", "--output", type=Path, required=True, help="Output albedo PNG")
    parser.add_argument("--normal", type=Path, default=None, help="Output normal PNG (*_n.png)")
    parser.add_argument("--mode", choices=("sprite", "tile", "portrait"), default="sprite")
    parser.add_argument("--size", type=int, default=128, help="Target size (128 or 256)")
    parser.add_argument("--pad", type=int, default=3)
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH)
    parser.add_argument("--normal-backend", choices=("laigter", "auto", "python"), default="laigter")
    args = parser.parse_args()

    if args.mode == "portrait":
        stats = process_portrait(args.input, args.output, palette_path=args.palette, target_size=args.size)
    elif args.mode == "tile":
        norm = args.normal or args.output.with_name(f"{args.output.stem}_n.png")
        stats = process_seamless_tile(
            args.input, args.output, norm, palette_path=args.palette, target_size=args.size, normal_backend=args.normal_backend
        )
    else:
        stats = process_sprite(
            args.input,
            args.output,
            output_normal=args.normal,
            target_size=args.size,
            pad_px=args.pad,
            palette_path=args.palette,
            normal_backend=args.normal_backend,
        )
    print(json.dumps(stats, indent=2))


if __name__ == "__main__":
    main()
