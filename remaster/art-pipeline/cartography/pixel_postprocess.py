"""
Realmz HD-2D Cartography Post-Processor (PixelOE + Exterior-Only Safe Keying + Laigter Normals).

Key fixes and upgrades over M1.5:
  1. Exterior-only border-seeded BFS background removal:
     - Never punches interior white/cream pockets on white/neutral studio backdrops
       (fixes the cottage_medieval.png white-hole bug on sunlit stucco/marble/windows).
     - Punches enclosed interior pockets ONLY when backdrop is saturated chroma
       (chroma_sat >= 45.0, e.g. #FF00FF magenta or #00FF00 green screen).
     - Strips faint bottom cast-shadow smudges only when connected to exterior background.
     - Despills perimeter chroma/white anti-aliased fringes.
  2. PixelOE contrast-aware downscaling + BottomCenter pivot framing:
     - Supports arbitrary (out_w, out_h) targets (e.g. (320, 256), (224, 224), (128, 128)).
     - Extends foreground border RGB outward before PixelOE so outlines stay razor-sharp
       with zero background halo bleed and strict 1-bit [0, 255] alpha.
     - Snaps every opaque pixel in CIELAB space to palette_soc_sunlit_128.json (0 off-palette).
  3. Seamless 128x128 tile processor + deterministic pixel-art alpha transition mask [64..255]:
     - Runs 3x3 wrap-padded PixelOE downscale for seamless boundaries.
     - Packs structured clustered pixel noise into alpha [64..255] for CartographyTerrain.shader.
  4. Headless laigter.exe (+Y OpenGL #8080FF) normal map generator with CREATE_NO_WINDOW
     and periodic Sobel fallback.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
from collections import deque
from pathlib import Path
from typing import Tuple, Union

import cv2
import numpy as np
from PIL import Image
from pixeloe.legacy.pixelize import pixelize as legacy_pixelize
from scipy import ndimage

CREATE_NO_WINDOW = 0x08000000

DEFAULT_PALETTE_PATH = Path(__file__).resolve().parent.parent / "palette_soc_sunlit_128.json"
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


def normalize_target_size(target_size: Union[int, Tuple[int, int]]) -> Tuple[int, int]:
    if isinstance(target_size, int):
        return (target_size, target_size)
    if len(target_size) != 2:
        raise ValueError(f"Expected int or (w, h), got {target_size}")
    return (int(target_size[0]), int(target_size[1]))


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
    m = max(2, min(16, min(h, w) // 32))
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
    Exterior-only border-seeded BFS 1-bit cutout for white or chroma studio backdrops:
      - Seeds BFS strictly from image borders (top/bottom/left/right edges).
      - Punches enclosed interior pockets ONLY when backdrop is saturated chroma
        (chroma_sat >= 45.0, e.g. #FF00FF magenta or #00FF00 green).
      - NEVER punches interior white/cream/neutral pockets on white-background sprites
        or buildings, keeping sunlit stucco walls, marble, and bright windows opaque.
      - Strips faint bottom cast-shadow smudges only when connected to exterior bg_mask.
      - Despills chroma or white fringes on foreground perimeter pixels.
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

    m = max(2, min(16, min(h, w) // 32))
    border_d = np.concatenate(
        [
            dist[:m, :].ravel(),
            dist[-m:, :].ravel(),
            dist[:, :m].ravel(),
            dist[:, -m:].ravel(),
        ]
    )
    border_p99 = float(np.percentile(border_d, 99))
    chroma_sat = float(int(np.max(bg_rgb)) - int(np.min(bg_rgb)))
    is_saturated_chroma = chroma_sat >= 45.0
    is_white_bg = (bg_lab[0] > 88.0) and (chroma_sat < 28.0)

    if bg_threshold is None:
        if is_saturated_chroma:
            bg_threshold = min(30.0, max(12.0, border_p99 * 2.5 + 7.5))
        elif is_white_bg:
            bg_threshold = min(14.0, max(8.5, border_p99 * 2.4 + 4.0))
        else:
            bg_threshold = min(14.0, max(9.0, border_p99 * 2.5 + 4.0))
    if edge_threshold is None:
        edge_threshold = min(bg_threshold + 6.0, 34.0 if is_saturated_chroma else 18.0)

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

    # Punch enclosed pockets ONLY for saturated chroma screens (magenta/green).
    # Never punch interior pockets on white or neutral backgrounds.
    if is_saturated_chroma:
        pocket_thresh = max(5.5, bg_threshold * 0.80)
        pocket_cand = (~bg_mask) & (dist <= pocket_thresh)
        p_lab, n_p = ndimage.label(pocket_cand)
        if n_p > 0:
            min_pocket = max(24, (h * w) // 20000)
            mean_cap = max(4.5, bg_threshold * 0.65)
            for idx in range(1, n_p + 1):
                comp = p_lab == idx
                if int(np.sum(comp)) >= min_pocket:
                    comp_d = dist[comp]
                    if float(np.mean(comp_d)) <= mean_cap and float(np.std(comp_d)) < 3.5:
                        bg_mask |= comp

    # Strip faint flat cast-shadow smudges beneath feet/foundations, restricted to
    # regions directly connected to the exterior bg_mask so interior shadows stay safe.
    if strip_ground_shadow:
        fg_temp = ~bg_mask
        ys_fg = np.where(fg_temp)[0]
        if len(ys_fg) > 0:
            y_top, y_bot = int(ys_fg.min()), int(ys_fg.max())
            foot_band = np.zeros((h, w), dtype=bool)
            band_start = max(y_top, y_bot - max(10, int((y_bot - y_top) * 0.065)))
            foot_band[band_start : y_bot + 1, :] = True
            chroma_diff = np.hypot(img_lab[..., 1] - bg_lab[1], img_lab[..., 2] - bg_lab[2])
            l_diff = bg_lab[0] - img_lab[..., 0]
            shadow_like = (
                fg_temp
                & foot_band
                & (chroma_diff <= 5.5)
                & (l_diff >= 3.0)
                & (l_diff <= 22.0)
                & (img_lab[..., 0] > 16.0)
            )
            if np.any(shadow_like):
                # Flood from bg_mask into shadow_like only
                bg_dilated = ndimage.binary_dilation(bg_mask, structure=np.ones((3, 3), dtype=bool))
                seed_shadow = shadow_like & bg_dilated
                if np.any(seed_shadow):
                    s_lab, n_s = ndimage.label(shadow_like)
                    touch_labels = set(int(v) for v in np.unique(s_lab[seed_shadow]) if v > 0)
                    for lbl in touch_labels:
                        bg_mask |= s_lab == lbl

    fg_mask = ~bg_mask

    if erode_px > 0:
        fg_mask = ndimage.binary_erosion(fg_mask, structure=np.ones((3, 3), dtype=bool), iterations=erode_px)

    # Keep primary component and meaningful detached elements (>= 0.8% of largest)
    labeled, n_comp = ndimage.label(fg_mask)
    if n_comp > 1:
        sizes = ndimage.sum(fg_mask, labeled, range(1, n_comp + 1))
        max_size = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(32.0, max_size * 0.008):
                keep |= labeled == idx
        fg_mask = keep

    # Perimeter despill for saturated chroma backdrops AND anti-aliased white fringes
    rim_iter = max(2, min(6, min(h, w) // 160))
    eroded_inner = ndimage.binary_erosion(fg_mask, iterations=rim_iter)
    rim = fg_mask & (~eroded_inner)
    if np.any(rim) and np.any(eroded_inner):
        rim_bad = np.zeros((h, w), dtype=bool)
        if is_saturated_chroma:
            rim_bad = rim & (dist < edge_threshold * 1.45)
            # Catch green spill (G significantly higher than R and B)
            if bg_rgb[1] > 170 and bg_rgb[0] < 120 and bg_rgb[2] < 120:
                green_spill = (
                    rgb[..., 1].astype(np.int16) - np.maximum(rgb[..., 0], rgb[..., 2]).astype(np.int16)
                ) > 35
                rim_bad |= rim & green_spill
            # Catch magenta spill (R and B significantly higher than G)
            if bg_rgb[0] > 170 and bg_rgb[2] > 170 and bg_rgb[1] < 120:
                mag_spill = (
                    np.minimum(rgb[..., 0], rgb[..., 2]).astype(np.int16) - rgb[..., 1].astype(np.int16)
                ) > 35
                rim_bad |= rim & mag_spill
        elif is_white_bg:
            rim_bad = rim & (dist < edge_threshold * 1.25) & (img_lab[..., 0] > 84.0)

        if np.any(rim_bad):
            _, nearest_idx = ndimage.distance_transform_edt(~eroded_inner, return_indices=True)
            ry, rx = np.where(rim_bad)
            rgb[ry, rx] = rgb[nearest_idx[0, ry, rx], nearest_idx[1, ry, rx]]

    return rgb, fg_mask


def _extend_fg_colors_into_bg(rgb: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """Nearest-neighbor bleed foreground colors into transparent region before PixelOE."""
    if np.all(mask) or not np.any(mask):
        return rgb.copy()
    out = rgb.copy()
    _, nearest_idx = ndimage.distance_transform_edt(~mask, return_indices=True)
    by, bx = np.where(~mask)
    out[by, bx] = out[nearest_idx[0, by, bx], nearest_idx[1, by, bx]]
    return out


def _pixeloe_downscale_rgb(
    rgb: np.ndarray,
    fit_w: int,
    fit_h: int,
    thickness: int = 1,
    patch_size: int | None = None,
) -> np.ndarray:
    """Run PixelOE contrast-aware downscale on RGB image (handles BGR conversion & exact shape)."""
    sh, sw, _ = rgb.shape
    if sw <= fit_w and sh <= fit_h:
        return cv2.resize(rgb, (fit_w, fit_h), interpolation=cv2.INTER_NEAREST)

    if patch_size is None:
        ratio_est = max(sw / max(1, fit_w), sh / max(1, fit_h))
        patch_size = max(4, min(16, int(round(ratio_est))))
    else:
        patch_size = max(4, int(patch_size))

    bgr = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
    out_bgr = legacy_pixelize(
        bgr,
        mode="contrast",
        target_size=(fit_w, fit_h),
        patch_size=patch_size,
        thickness=thickness,
        color_matching=True,
        no_upscale=True,
    )
    out_rgb = cv2.cvtColor(out_bgr, cv2.COLOR_BGR2RGB)
    if out_rgb.shape[0] != fit_h or out_rgb.shape[1] != fit_w:
        out_rgb = cv2.resize(out_rgb, (fit_w, fit_h), interpolation=cv2.INTER_NEAREST)
    return out_rgb


def pixeloe_downscale_sprite(
    rgb: np.ndarray,
    fg_mask: np.ndarray,
    target_size: Union[int, Tuple[int, int]] = (128, 128),
    pad_px: int = 2,
    bottom_pivot: bool = True,
    thickness: int = 1,
    patch_size: int | None = None,
) -> np.ndarray:
    """
    Crop foreground, extend edge colors, apply PixelOE contrast-aware downscale,
    and frame onto (out_w, out_h) RGBA canvas with BottomCenter pivot and 1-bit [0, 255] alpha.
    """
    out_w, out_h = normalize_target_size(target_size)
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Background removal produced an empty foreground mask.")

    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    crop_rgb = rgb[y0:y1, x0:x1]
    crop_mask = fg_mask[y0:y1, x0:x1]
    ch, cw = crop_mask.shape

    avail_w = max(1, out_w - 2 * pad_px)
    avail_h = max(1, out_h - 2 * pad_px)
    scale = min(avail_w / cw, avail_h / ch)
    fit_w = max(1, int(round(cw * scale)))
    fit_h = max(1, int(round(ch * scale)))

    # Nearest-neighbor extend foreground colors so PixelOE never pulls bg halos into outlines
    extended_rgb = _extend_fg_colors_into_bg(crop_rgb, crop_mask)
    down_rgb = _pixeloe_downscale_rgb(
        extended_rgb,
        fit_w=fit_w,
        fit_h=fit_h,
        thickness=thickness,
        patch_size=patch_size,
    )

    # Crisp 1-bit alpha downscale via area coverage threshold
    mask_f = crop_mask.astype(np.float32)
    mask_sm = cv2.resize(mask_f, (fit_w, fit_h), interpolation=cv2.INTER_AREA)
    alpha_bin = mask_sm >= 0.45

    # Remove isolated 1-2px stray dust on final pixel grid
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(3.0, max_sz * 0.004):
                alpha_bin[labeled == idx] = False

    ox = (out_w - fit_w) // 2
    oy = (out_h - pad_px - fit_h) if bottom_pivot else (out_h - fit_h) // 2

    canvas = np.zeros((out_h, out_w, 4), dtype=np.uint8)
    sub = canvas[oy : oy + fit_h, ox : ox + fit_w]
    sub[..., :3] = down_rgb
    sub[..., 3] = np.where(alpha_bin, 255, 0).astype(np.uint8)
    sub[~alpha_bin, :3] = 0
    return canvas


def pixeloe_downscale_seamless_tile(
    rgb: np.ndarray,
    target_size: Union[int, Tuple[int, int]] = (128, 128),
    thickness: int = 1,
    patch_size: int | None = None,
) -> np.ndarray:
    """
    Downscale a seamless tile (e.g. 1024x1024 -> 128x128) using 3x3 periodic wrap padding
    around PixelOE so edges stay 100% tileable.
    """
    out_w, out_h = normalize_target_size(target_size)
    # 3x3 periodic tile before PixelOE so outline_expansion and contrast blocks wrap cleanly
    tiled_in = np.tile(rgb, (3, 3, 1))
    tiled_out = _pixeloe_downscale_rgb(
        tiled_in,
        fit_w=out_w * 3,
        fit_h=out_h * 3,
        thickness=thickness,
        patch_size=patch_size,
    )
    center = tiled_out[out_h : 2 * out_h, out_w : 2 * out_w]
    out = np.zeros((out_h, out_w, 4), dtype=np.uint8)
    out[..., :3] = center
    out[..., 3] = 255
    return out


def apply_sunlit_grade_and_quantize(
    sprite_1x: np.ndarray,
    palette_rgb: np.ndarray,
    dither_strength: float = 0.0,
    grade_strength: float = 0.14,
    shadow_lift: float = 4.5,
    vibrance_boost: float = 1.08,
    preserve_alpha: bool = False,
) -> np.ndarray:
    """
    Apply golden-hour sunlit grade (lifts crushed dark midtones while preserving crisp ink
    outlines, adds subtle warm sunlight on highlights and cool slate-indigo tint in shadows)
    and snap every opaque/visible pixel to palette_soc_sunlit_128.json in CIELAB space.
    """
    out = sprite_1x.copy()
    alpha_orig = out[..., 3].copy()
    active = alpha_orig > 0 if preserve_alpha else (alpha_orig == 255)
    if not np.any(active):
        return out

    rgb_u8 = out[..., :3].copy()

    if shadow_lift > 0.0 or vibrance_boost != 1.0:
        lab = rgb_to_lab_float(rgb_u8)
        L = lab[..., 0]
        lift_mask = np.clip((L - 6.0) / 14.0, 0.0, 1.0) * np.clip((52.0 - L) / 40.0, 0.0, 1.0)
        lab[..., 0] = np.clip(L + lift_mask * shadow_lift, 0.0, 100.0)
        chroma = np.hypot(lab[..., 1], lab[..., 2])
        vib_factor = 1.0 + (vibrance_boost - 1.0) * np.clip(1.0 - chroma / 85.0, 0.25, 1.0)
        lab[..., 1] *= vib_factor
        lab[..., 2] *= vib_factor
        rgb_u8 = lab_float_to_rgb_u8(lab)

    rgb = rgb_u8.astype(np.float32)

    if grade_strength > 0.0:
        lum = (0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]) / 255.0
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
        rgb = np.clip(rgb + tiled[..., None] * (dither_strength * 18.0), 0.0, 255.0)

    rgb_u8 = np.round(rgb).astype(np.uint8)
    pal_lab = rgb_to_lab_float(palette_rgb)
    pix_lab = rgb_to_lab_float(rgb_u8[active])

    n_pix = pix_lab.shape[0]
    nearest = np.empty(n_pix, dtype=np.int32)
    chunk = 8192
    for start in range(0, n_pix, chunk):
        end = min(n_pix, start + chunk)
        diff = pix_lab[start:end, None, :] - pal_lab[None, :, :]
        dist2 = (1.10 * diff[..., 0] ** 2) + (1.15 * diff[..., 1] ** 2) + (1.15 * diff[..., 2] ** 2)
        nearest[start:end] = np.argmin(dist2, axis=1)

    quant_rgb = np.zeros_like(rgb_u8)
    quant_rgb[active] = palette_rgb[nearest]

    out[..., :3] = quant_rgb
    if not preserve_alpha:
        out[~active] = 0
        out[active, 3] = 255
    else:
        out[~active, :3] = 0
        out[active, 3] = alpha_orig[active]
    return out


def generate_alpha_transition_mask(
    tile_rgb: np.ndarray,
    seed: int = 42,
    min_alpha: int = 64,
    max_alpha: int = 255,
) -> np.ndarray:
    """
    Generate a deterministic, seamless pixel-art clustered noise alpha transition channel
    in [min_alpha, max_alpha] (default [64..255]) for CartographyTerrain.shader splat blends.
    Combines multi-scale periodic block noise (4x4 and 2x2 pixel clusters) with normalized
    tile luminance texture so transitions follow grass blades and cobble grain cleanly.
    """
    h, w, _ = tile_rgb.shape
    rng = np.random.default_rng(seed)

    def periodic_block_noise(block_sz: int) -> np.ndarray:
        gh = max(1, (h + block_sz - 1) // block_sz)
        gw = max(1, (w + block_sz - 1) // block_sz)
        grid = rng.uniform(0.0, 1.0, size=(gh, gw)).astype(np.float32)
        up = np.repeat(np.repeat(grid, block_sz, axis=0), block_sz, axis=1)[:h, :w]
        return up

    n8 = periodic_block_noise(8)
    n4 = periodic_block_noise(4)
    n2 = periodic_block_noise(2)
    n1 = rng.uniform(0.0, 1.0, size=(h, w)).astype(np.float32)

    # Periodic box smoothing on coarse layer for organic patches while keeping 2x2 pixel grain crisp
    coarse = ndimage.uniform_filter(n8, size=5, mode="wrap")
    lum = (
        0.2126 * tile_rgb[..., 0].astype(np.float32)
        + 0.7152 * tile_rgb[..., 1].astype(np.float32)
        + 0.0722 * tile_rgb[..., 2].astype(np.float32)
    ) / 255.0
    lum_mean = ndimage.gaussian_filter(lum, sigma=3.0, mode="wrap")
    lum_detail = np.clip(0.5 + (lum - lum_mean) * 2.2, 0.0, 1.0)

    combined = 0.34 * coarse + 0.28 * n4 + 0.22 * n2 + 0.08 * n1 + 0.08 * lum_detail
    # Normalize robustly to [0, 1] and step into crisp pixel-art clusters (16 discrete levels)
    lo = float(np.percentile(combined, 2))
    hi = float(np.percentile(combined, 98))
    normed = np.clip((combined - lo) / max(1e-5, hi - lo), 0.0, 1.0)
    stepped = np.round(normed * 15.0) / 15.0

    alpha = np.round(min_alpha + stepped * (max_alpha - min_alpha)).astype(np.uint8)
    return np.clip(alpha, min_alpha, max_alpha).astype(np.uint8)


def generate_normal_map_laigter(
    sprite_1x: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    periodic: bool = False,
    xy_scale: float = 0.68,
    min_z: float = 0.32,
) -> np.ndarray | None:
    """
    Generate crisp 1x OpenGL (+Y up, #8080FF neutral) tangent-space normal map using
    headless laigter.exe (with CREATE_NO_WINDOW = 0x08000000).
    For seamless tiles (periodic=True), runs laigter on an opaque 3x3 wrap-tiled canvas
    and crops the center 1x1 region so normals wrap seamlessly across all 4 edges.
    """
    if not laigter_exe.exists():
        return None

    h, w, _ = sprite_1x.shape
    alpha = np.ones((h, w), dtype=bool) if periodic else (sprite_1x[..., 3] > 0)

    feed = sprite_1x.copy()
    if periodic:
        feed[..., 3] = 255
        feed = np.tile(feed, (3, 3, 1))

    with tempfile.TemporaryDirectory(prefix="realmz_carto_laigter_") as td:
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
                creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
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

        if np.any(alpha) and float(np.std(nx[alpha]) + np.std(ny[alpha])) < 0.020:
            return None

        out = np.zeros((h, w, 4), dtype=np.uint8)
        out[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
        out[..., 3] = 255
        if not periodic:
            out[~alpha] = [128, 128, 255, 0]
        return out


def generate_normal_map_sobel_fallback(
    sprite_1x: np.ndarray,
    periodic: bool = False,
    bevel_strength: float = 1.45,
    detail_strength: float = 1.25,
    bevel_radius: float = 6.0,
) -> np.ndarray:
    """Sobel OpenGL (+Y up, #8080FF) tangent-space normal generator fallback."""
    h, w, _ = sprite_1x.shape
    alpha = np.ones((h, w), dtype=bool) if periodic else (sprite_1x[..., 3] > 0)
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


def generate_normal_map(
    sprite_1x: np.ndarray,
    periodic: bool = False,
    normal_backend: str = "auto",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> Tuple[np.ndarray, str]:
    """Generate OpenGL +Y normal map via laigter.exe or Sobel fallback."""
    if normal_backend in ("auto", "laigter"):
        xy_scale = 0.62 if periodic else 0.68
        min_z = 0.38 if periodic else 0.32
        norm = generate_normal_map_laigter(
            sprite_1x,
            laigter_exe=laigter_exe,
            periodic=periodic,
            xy_scale=xy_scale,
            min_z=min_z,
        )
        if norm is not None:
            return norm, ("laigter_periodic" if periodic else "laigter")
        if normal_backend == "laigter":
            raise RuntimeError("Laigter normal map generation failed and backend='laigter' was required.")
    return (
        generate_normal_map_sobel_fallback(sprite_1x, periodic=periodic),
        ("sobel_periodic" if periodic else "sobel"),
    )


def compute_tile_seam_ratio(tile_1x: np.ndarray) -> float:
    lab = rgb_to_lab_float(tile_1x[..., :3])
    interior_h = np.linalg.norm(lab[:, 1:] - lab[:, :-1], axis=-1).mean()
    interior_v = np.linalg.norm(lab[1:, :] - lab[:-1, :], axis=-1).mean()
    seam_h = np.linalg.norm(lab[:, 0] - lab[:, -1], axis=-1).mean()
    seam_v = np.linalg.norm(lab[0, :] - lab[-1, :], axis=-1).mean()
    interior = max(1e-4, 0.5 * (interior_h + interior_v))
    seam = 0.5 * (seam_h + seam_v)
    return round(float(seam / interior), 3)


def validate_outputs(
    sprite_1x: np.ndarray,
    normal_1x: np.ndarray,
    palette_rgb: np.ndarray,
    is_tile: bool = False,
) -> dict:
    active = (sprite_1x[..., 3] > 0) if is_tile else (sprite_1x[..., 3] == 255)
    alpha_vals = sorted(int(v) for v in np.unique(sprite_1x[..., 3]))
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used_colors = {tuple(int(c) for c in row) for row in sprite_1x[active, :3]}
    off_palette = len(used_colors - pal_set)

    n = (normal_1x[active, :3].astype(np.float32) - 127.5) / 127.5
    mags = np.linalg.norm(n, axis=-1) if len(n) else np.array([1.0])
    bg_normals = normal_1x[~active]
    bg_neutral_ok = bool(
        is_tile
        or len(bg_normals) == 0
        or np.all(bg_normals == np.array([128, 128, 255, 0], dtype=np.uint8))
    )

    stats = {
        "width": int(sprite_1x.shape[1]),
        "height": int(sprite_1x.shape[0]),
        "opaque_pixels": int(np.sum(active)),
        "alpha_min": int(sprite_1x[..., 3].min()),
        "alpha_max": int(sprite_1x[..., 3].max()),
        "alpha_is_1bit": alpha_vals in ([0, 255], [255]),
        "unique_palette_colors": len(used_colors),
        "off_palette_colors": off_palette,
        "normal_unit_mag_mean": round(float(mags.mean()), 4),
        "normal_min_z": round(float(n[:, 2].min()), 3) if len(n) else 1.0,
        "normal_bg_neutral_ok": bg_neutral_ok,
    }
    if is_tile:
        stats["seam_ratio"] = compute_tile_seam_ratio(sprite_1x)
    return stats


def process_sprite(
    input_path: Path,
    output_albedo: Path,
    output_normal: Path | None = None,
    target_size: Union[int, Tuple[int, int]] = (128, 128),
    pad_px: int = 2,
    bottom_pivot: bool = True,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    thickness: int = 1,
    dither_strength: float = 0.0,
    grade_strength: float = 0.14,
    shadow_lift: float = 4.5,
    vibrance_boost: float = 1.08,
    normal_backend: str = "auto",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    strip_ground_shadow: bool = True,
) -> dict:
    raw = np.array(Image.open(input_path).convert("RGBA"))
    palette_rgb = load_palette(palette_path)

    rgb_clean, fg_mask = remove_background_1bit(raw, strip_ground_shadow=strip_ground_shadow)
    sprite_1x = pixeloe_downscale_sprite(
        rgb_clean,
        fg_mask,
        target_size=target_size,
        pad_px=pad_px,
        bottom_pivot=bottom_pivot,
        thickness=thickness,
    )
    sprite_1x = apply_sunlit_grade_and_quantize(
        sprite_1x,
        palette_rgb,
        dither_strength=dither_strength,
        grade_strength=grade_strength,
        shadow_lift=shadow_lift,
        vibrance_boost=vibrance_boost,
        preserve_alpha=False,
    )

    normal_1x, backend_used = generate_normal_map(
        sprite_1x,
        periodic=False,
        normal_backend=normal_backend,
        laigter_exe=laigter_exe,
    )

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
    output_normal: Path | None = None,
    target_size: Union[int, Tuple[int, int]] = (128, 128),
    palette_path: Path = DEFAULT_PALETTE_PATH,
    thickness: int = 1,
    dither_strength: float = 0.02,
    grade_strength: float = 0.12,
    shadow_lift: float = 4.0,
    vibrance_boost: float = 1.10,
    noise_seed: int = 42,
    alpha_min: int = 64,
    alpha_max: int = 255,
    normal_backend: str = "auto",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> dict:
    raw = np.array(Image.open(raw_seamless_path).convert("RGB"))
    palette_rgb = load_palette(palette_path)

    tile_1x = pixeloe_downscale_seamless_tile(raw, target_size=target_size, thickness=thickness)
    tile_1x = apply_sunlit_grade_and_quantize(
        tile_1x,
        palette_rgb,
        dither_strength=dither_strength,
        grade_strength=grade_strength,
        shadow_lift=shadow_lift,
        vibrance_boost=vibrance_boost,
        preserve_alpha=False,
    )

    normal_1x, backend_used = generate_normal_map(
        tile_1x,
        periodic=True,
        normal_backend=normal_backend,
        laigter_exe=laigter_exe,
    )

    # Pack deterministic pixel-art transition noise into alpha [64..255] AFTER normal generation
    tile_1x[..., 3] = generate_alpha_transition_mask(
        tile_1x[..., :3],
        seed=noise_seed,
        min_alpha=alpha_min,
        max_alpha=alpha_max,
    )

    output_albedo.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(tile_1x, mode="RGBA").save(output_albedo, format="PNG")
    if output_normal is None:
        output_normal = output_albedo.with_name(f"{output_albedo.stem}_n.png")
    output_normal.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(normal_1x, mode="RGBA").save(output_normal, format="PNG")

    stats = validate_outputs(tile_1x, normal_1x, palette_rgb, is_tile=True)
    stats["normal_backend"] = backend_used
    stats["albedo_path"] = str(output_albedo)
    stats["normal_path"] = str(output_normal)
    return stats


def _parse_size_arg(val: str) -> Tuple[int, int]:
    s = val.lower().strip()
    if "x" in s:
        parts = s.split("x", 1)
        return (int(parts[0]), int(parts[1]))
    n = int(s)
    return (n, n)


def main() -> None:
    parser = argparse.ArgumentParser(description="Realmz Cartography PixelOE Post-Processor")
    parser.add_argument("input", type=Path, help="Raw input PNG")
    parser.add_argument("-o", "--output", type=Path, required=True, help="Output albedo PNG")
    parser.add_argument("--normal", type=Path, default=None, help="Output normal PNG (*_n.png)")
    parser.add_argument("--mode", choices=("sprite", "tile"), default="sprite")
    parser.add_argument("--size", type=str, default="128", help="Target WxH or N (e.g. 320x256 or 128)")
    parser.add_argument("--pad", type=int, default=2)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH)
    parser.add_argument("--normal-backend", choices=("laigter", "auto", "python"), default="auto")
    args = parser.parse_args()

    target_wh = _parse_size_arg(args.size)
    if args.mode == "tile":
        stats = process_seamless_tile(
            args.input,
            args.output,
            output_normal=args.normal,
            target_size=target_wh,
            palette_path=args.palette,
            noise_seed=args.seed,
            normal_backend=args.normal_backend,
        )
    else:
        stats = process_sprite(
            args.input,
            args.output,
            output_normal=args.normal,
            target_size=target_wh,
            pad_px=args.pad,
            palette_path=args.palette,
            normal_backend=args.normal_backend,
        )
    print(json.dumps(stats, indent=2))


if __name__ == "__main__":
    main()
