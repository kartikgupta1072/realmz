"""
Realmz HD-2D Songs-of-Conquest Pixel-Art + Normal-Map Post-Processor.

Zero-PyTorch-conflict deterministic pipeline using PIL, NumPy, SciPy, and OpenCV:
  1. Halo-free 1-bit background removal (border flood-fill in CIELAB + chroma despill)
  2. Tight bottom-center foot-pivot framing + outline-preserving 1x pixel-grid snap
  3. Subtle warm-torch / cool-indigo grade + locked 64-color CIELAB quantization
  4. Crisp 1x OpenGL (+Y up) tangent-space normal map (*_n.png) via azagaya/laigter or
     deterministic edge-aware alpha-bevel + outline-protected luminance height-to-normal
  5. Optional 8x nearest-neighbor visual QA sheet with simulated torchlight relighting
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import tempfile
from collections import deque
from pathlib import Path
from typing import Tuple

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage

DEFAULT_PALETTE_PATH = Path(__file__).parent / "palette_songs_of_conquest_64.json"
DEFAULT_LAIGTER_EXE = Path(r"C:\ComfyUI\tools\laigter\laigter-windows\laigter.exe")


def hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    h = hex_color.strip().lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def load_palette(path: Path) -> np.ndarray:
    data = json.loads(path.read_text(encoding="utf-8"))
    colors = data["colors"]
    rgb = np.array([hex_to_rgb(c) for c in colors], dtype=np.uint8)
    return rgb


def rgb_to_lab_float(rgb_u8: np.ndarray) -> np.ndarray:
    """Convert HxWx3 or Nx3 uint8 sRGB to float32 CIELAB."""
    orig_shape = rgb_u8.shape
    if rgb_u8.ndim == 2:
        img = rgb_u8.reshape(1, -1, 3)
    else:
        img = rgb_u8
    lab = cv2.cvtColor(img, cv2.COLOR_RGB2LAB).astype(np.float32)
    # OpenCV uint8 Lab scales L to [0,255], a,b to [0,255] centered at 128
    lab[..., 0] = lab[..., 0] * (100.0 / 255.0)
    lab[..., 1] = lab[..., 1] - 128.0
    lab[..., 2] = lab[..., 2] - 128.0
    return lab.reshape(orig_shape)


def estimate_border_bg_color(rgb: np.ndarray) -> np.ndarray:
    """Estimate dominant background color from outer 6px image border."""
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
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Remove solid backdrop cleanly with zero soft halo (works for both high-chroma
    and neutral studio backdrops):
      - Measures border color distribution in CIELAB to set a tight adaptive threshold
      - Border-seeded flood fill that stops dead at crisp pixel-art ink outlines
      - Punches out uniform interior background pockets (e.g. between legs/weapons)
      - Removes faint low-contrast flat ground shadow strips beneath boots
      - Despills chroma fringe on foreground perimeter pixels
    """
    rgb = rgba[..., :3].copy()
    h, w, _ = rgb.shape

    # If input already has meaningful transparency, honor it first
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

    if bg_threshold is None:
        if chroma_sat >= 45.0:
            bg_threshold = min(26.0, max(10.0, border_p99 * 2.5 + 6.0))
        else:
            bg_threshold = min(13.5, max(9.5, border_p99 * 2.5 + 4.5))
    if edge_threshold is None:
        edge_threshold = min(bg_threshold + 5.0, 28.0 if chroma_sat >= 45.0 else 16.5)

    # Compute local gradient magnitude in Lab so flood fill never leaks across crisp outlines
    gx = cv2.Sobel(img_lab[..., 0], cv2.CV_32F, 1, 0, ksize=3) / 4.0
    gy = cv2.Sobel(img_lab[..., 0], cv2.CV_32F, 0, 1, ksize=3) / 4.0
    grad_l = np.hypot(gx, gy)

    # Seed flood-fill from all border pixels matching background
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
                elif (
                    d_global <= edge_threshold
                    and d_local <= 4.5
                    and grad_l[ny, nx] < 12.0
                ):
                    visited[ny, nx] = True
                    bg_mask[ny, nx] = True
                    q.append((ny, nx))

    # Punch out uniform enclosed background pockets (e.g., gaps between legs/sword/cloak)
    pocket_cand = (~bg_mask) & (dist <= max(4.5, bg_threshold * 0.75))
    p_lab, n_p = ndimage.label(pocket_cand)
    if n_p > 0:
        for idx in range(1, n_p + 1):
            comp = p_lab == idx
            sz = int(np.sum(comp))
            if sz >= max(48, (h * w) // 16000):
                comp_d = dist[comp]
                if float(np.mean(comp_d)) <= max(3.5, bg_threshold * 0.55) and float(np.std(comp_d)) < 3.0:
                    bg_mask |= comp

    fg_mask = ~bg_mask

    # Strip faint flat ground cast shadow directly beneath feet (only bottom 6% of bbox,
    # low chroma diff from bg, medium-dark L slightly below bg, never touching dark outlines)
    ys_fg = np.where(fg_mask)[0]
    if len(ys_fg) > 0:
        y_top, y_bot = int(ys_fg.min()), int(ys_fg.max())
        foot_band = np.zeros_like(fg_mask)
        band_start = max(y_top, y_bot - max(12, int((y_bot - y_top) * 0.055)))
        foot_band[band_start : y_bot + 1, :] = True
        chroma_diff = np.hypot(
            img_lab[..., 1] - bg_lab[1],
            img_lab[..., 2] - bg_lab[2],
        )
        l_diff = bg_lab[0] - img_lab[..., 0]
        # Ground shadow is slightly darker than bg (l_diff in 4..18) with near-zero chroma shift
        # and still clearly lighter than dark ink outlines (L > 14)
        shadow_like = (
            fg_mask
            & foot_band
            & (chroma_diff <= 4.5)
            & (l_diff >= 3.5)
            & (l_diff <= 18.0)
            & (img_lab[..., 0] > 14.0)
        )
        fg_mask = fg_mask & (~shadow_like)

    # Optional light morphological cleanup
    if erode_px > 0:
        struct = np.ones((3, 3), dtype=bool)
        fg_mask = ndimage.binary_erosion(fg_mask, structure=struct, iterations=erode_px)

    # Keep primary sprite component and any substantial detached pieces (>= 1.5% of max)
    labeled, n_comp = ndimage.label(fg_mask)
    if n_comp > 1:
        sizes = ndimage.sum(fg_mask, labeled, range(1, n_comp + 1))
        max_size = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(48.0, max_size * 0.015):
                keep |= labeled == idx
        fg_mask = keep

    # Despill border chroma bleed (only for saturated chroma backdrops)
    if chroma_sat >= 45.0:
        eroded_inner = ndimage.binary_erosion(fg_mask, iterations=max(2, min(6, h // 180)))
        rim = fg_mask & (~eroded_inner)
        if np.any(rim) and np.any(eroded_inner):
            rim_bad = rim & (dist < edge_threshold * 1.15)
            if np.any(rim_bad):
                _, nearest_idx = ndimage.distance_transform_edt(~eroded_inner, return_indices=True)
                ry, rx = np.where(rim_bad)
                rgb[ry, rx] = rgb[nearest_idx[0, ry, rx], nearest_idx[1, ry, rx]]

    return rgb, fg_mask


def snap_to_1x_pixel_grid(
    rgb: np.ndarray,
    fg_mask: np.ndarray,
    target_size: int = 64,
    pad_px: int = 2,
) -> np.ndarray:
    """
    Frame sprite cleanly onto target_size x target_size canvas with bottom-center foot pivot
    and perform outline-preserving crisp 1x pixel-art downsampling (zero mixels, zero blur).
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

    # Bottom-center foot pivot alignment (Songs of Conquest / Unity HD-2D standard)
    ox = (target_size - out_w) // 2
    oy = target_size - pad_px - out_h

    canvas = np.zeros((target_size, target_size, 4), dtype=np.uint8)

    # Precompute luminance for outline/highlight preservation inside each 1x cell
    lum = (
        0.2126 * crop_rgb[..., 0].astype(np.float32)
        + 0.7152 * crop_rgb[..., 1].astype(np.float32)
        + 0.0722 * crop_rgb[..., 2].astype(np.float32)
    )

    y_edges = np.linspace(0, ch, out_h + 1)
    x_edges = np.linspace(0, cw, out_w + 1)

    for ty in range(out_h):
        sy0 = int(np.floor(y_edges[ty]))
        sy1 = max(sy0 + 1, int(np.ceil(y_edges[ty + 1])))
        sy1 = min(sy1, ch)
        for tx in range(out_w):
            sx0 = int(np.floor(x_edges[tx]))
            sx1 = max(sx0 + 1, int(np.ceil(x_edges[tx + 1])))
            sx1 = min(sx1, cw)

            cell_m = crop_mask[sy0:sy1, sx0:sx1]
            cov = float(np.mean(cell_m))
            if cov < 0.42:
                continue

            cell_rgb = crop_rgb[sy0:sy1, sx0:sx1][cell_m]
            cell_lum = lum[sy0:sy1, sx0:sx1][cell_m]

            # Outline-preserving contrast selection (PixelOE-inspired):
            # If the cell contains a strong dark outline edge or bright specular glint,
            # preserve it instead of averaging into muddy gray.
            p15 = float(np.percentile(cell_lum, 15))
            p50 = float(np.percentile(cell_lum, 50))
            p85 = float(np.percentile(cell_lum, 85))

            if p15 < 42.0 and (p50 - p15) > 28.0:
                # Preserve dark crisp ink outline
                sel = cell_rgb[cell_lum <= p15 + 4.0]
                chosen = np.median(sel, axis=0)
            elif p85 > 205.0 and (p85 - p50) > 45.0:
                # Preserve bright metal/torch specular highlight
                sel = cell_rgb[cell_lum >= p85 - 4.0]
                chosen = np.median(sel, axis=0)
            else:
                # Crisp center-biased median (avoids edge blending mixels)
                chosen = np.median(cell_rgb, axis=0)

            canvas[oy + ty, ox + tx, :3] = np.clip(np.round(chosen), 0, 255).astype(np.uint8)
            canvas[oy + ty, ox + tx, 3] = 255

    # Remove isolated 1-pixel stray dust on the 1x alpha grid
    alpha_bin = canvas[..., 3] > 0
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(3.0, max_sz * 0.01):
                canvas[labeled == idx] = 0

    return canvas


def apply_moody_grade_and_quantize(
    sprite_1x: np.ndarray,
    palette_rgb: np.ndarray,
    dither_strength: float = 0.0,
    grade_strength: float = 0.25,
) -> np.ndarray:
    """
    Apply subtle Songs-of-Conquest warm-torch / cool-indigo grade and snap every
    opaque pixel to the locked 64-color master palette in CIELAB space.
    """
    out = sprite_1x.copy()
    opaque = out[..., 3] == 255
    if not np.any(opaque):
        return out

    rgb = out[..., :3].astype(np.float32)

    if grade_strength > 0.0:
        lum = (0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]) / 255.0
        # Cool indigo push in deep shadows, warm amber push in upper highlights
        shadow_w = np.clip((0.45 - lum) / 0.45, 0.0, 1.0) * grade_strength
        hi_w = np.clip((lum - 0.55) / 0.45, 0.0, 1.0) * grade_strength
        rgb[..., 0] = rgb[..., 0] - shadow_w * 10.0 + hi_w * 14.0
        rgb[..., 1] = rgb[..., 1] - shadow_w * 6.0 + hi_w * 6.0
        rgb[..., 2] = rgb[..., 2] + shadow_w * 18.0 - hi_w * 12.0
        rgb = np.clip(rgb, 0.0, 255.0)

    if dither_strength > 0.0:
        bayer2 = np.array([[-0.5, 0.5], [0.5, -0.5]], dtype=np.float32)
        h, w, _ = rgb.shape
        tiled = np.tile(bayer2, ((h + 1) // 2, (w + 1) // 2))[:h, :w]
        rgb = np.clip(rgb + tiled[..., None] * (dither_strength * 24.0), 0.0, 255.0)

    rgb_u8 = np.round(rgb).astype(np.uint8)
    pal_lab = rgb_to_lab_float(palette_rgb)  # (64, 3)

    pix_lab = rgb_to_lab_float(rgb_u8[opaque])  # (N, 3)
    # Perceptual Lab distance with slightly higher weight on Lightness for readable silhouettes
    diff = pix_lab[:, None, :] - pal_lab[None, :, :]
    dist2 = (1.25 * diff[..., 0] ** 2) + (diff[..., 1] ** 2) + (diff[..., 2] ** 2)
    nearest = np.argmin(dist2, axis=1)

    quant_rgb = np.zeros_like(rgb_u8)
    quant_rgb[opaque] = palette_rgb[nearest]

    out[..., :3] = quant_rgb
    out[~opaque] = 0
    return out


def generate_normal_map_python(
    sprite_1x: np.ndarray,
    bevel_strength: float = 1.65,
    detail_strength: float = 1.15,
    bevel_radius: float = 5.0,
) -> np.ndarray:
    """
    Deterministic crisp 1x OpenGL (+Y up) tangent-space normal map generator for pixel art.
    Combines:
      - Smooth Euclidean alpha-distance silhouette bevel (gives 3D rounded form under torches)
      - Outline-protected luminance height (dark ink lines do not carve sunken trenches)
      - Normalized unit tangent-space vectors with #8080FF flat neutral base
    """
    h, w, _ = sprite_1x.shape
    alpha = sprite_1x[..., 3] == 255
    rgb = sprite_1x[..., :3].astype(np.float32) / 255.0

    # 1. Silhouette distance bevel (smoothly curved pillow-free edge roll-off)
    dist_in = ndimage.distance_transform_edt(alpha).astype(np.float32)
    t = np.clip(dist_in / max(1.0, bevel_radius), 0.0, 1.0)
    # Quarter-circle / smoothstep dome profile near edges, flat interior plateau
    bevel_h = np.sqrt(np.maximum(0.0, 2.0 * t - t * t))

    # 2. Luminance relief with dark-outline protection
    lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    is_outline = alpha & (lum < 0.14)
    lum_clean = lum.copy()
    if np.any(is_outline) and np.any(alpha & (~is_outline)):
        # Fill dark outline pixels from nearest non-outline foreground pixel
        _, idx = ndimage.distance_transform_edt(~(alpha & (~is_outline)), return_indices=True)
        oy, ox = np.where(is_outline)
        lum_clean[oy, ox] = lum[idx[0, oy, ox], idx[1, oy, ox]]

    # Fill background height smoothly from nearest foreground to prevent harsh alpha-edge spikes
    if np.any(alpha) and np.any(~alpha):
        _, idx_bg = ndimage.distance_transform_edt(~alpha, return_indices=True)
        by, bx = np.where(~alpha)
        lum_clean[by, bx] = lum_clean[idx_bg[0, by, bx], idx_bg[1, by, bx]]

    # Light directional smoothing on luminance so 1px noise does not flicker under lights
    lum_smooth = ndimage.gaussian_filter(lum_clean, sigma=0.65)
    height = bevel_strength * bevel_h + detail_strength * lum_smooth

    # 3. Scharr/Sobel partial derivatives
    gx = cv2.Sobel(height.astype(np.float32), cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gy = cv2.Sobel(height.astype(np.float32), cv2.CV_32F, 0, 1, ksize=3) / 8.0

    # Tangent-space normal in OpenGL convention (+X right, +Y up, +Z out of screen):
    # Image y grows downward, so dH/dy_opengl = -gy -> ny = +gy (facing upward on top bevels)
    nx = -gx
    ny = gy
    nz = np.ones_like(nx, dtype=np.float32)

    norm = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
    nx /= norm
    ny /= norm
    nz /= norm

    # Encode [-1, 1] -> [0, 255] with neutral (0, 0, 1) -> (128, 128, 255)
    normal_rgba = np.zeros((h, w, 4), dtype=np.uint8)
    normal_rgba[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_rgba[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_rgba[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
    normal_rgba[..., 3] = 255

    # Transparent pixels: neutral normal #8080FF with alpha 0
    normal_rgba[~alpha, 0] = 128
    normal_rgba[~alpha, 1] = 128
    normal_rgba[~alpha, 2] = 255
    normal_rgba[~alpha, 3] = 0

    return normal_rgba


def generate_normal_map_laigter(
    sprite_1x: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> np.ndarray | None:
    """
    Attempt headless azagaya/laigter CLI normal map generation and enforce exact 1x alpha
    mask + unit-vector normalization. Returns None if laigter cannot run headlessly.
    """
    if not laigter_exe.exists():
        return None

    h, w, _ = sprite_1x.shape
    alpha = sprite_1x[..., 3] == 255

    with tempfile.TemporaryDirectory(prefix="realmz_laigter_") as td:
        tmp_in = Path(td) / "sprite.png"
        expected_out = Path(td) / "sprite_n.png"
        Image.fromarray(sprite_1x, mode="RGBA").save(tmp_in)

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
                timeout=20,
                creationflags=0x08000000 if os.name == "nt" else 0,
            )
        except Exception:
            return None

        if proc.returncode != 0 or not expected_out.exists():
            return None

        raw_n = np.array(Image.open(expected_out).convert("RGBA"))
        if raw_n.shape[:2] != (h, w):
            return None

        # Normalize unit tangent-space vectors and ensure valid Z > 0
        nx = (raw_n[..., 0].astype(np.float32) - 127.5) / 127.5
        ny = (raw_n[..., 1].astype(np.float32) - 127.5) / 127.5
        nz = np.maximum(0.05, (raw_n[..., 2].astype(np.float32) - 127.5) / 127.5)
        mag = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
        nx /= mag
        ny /= mag
        nz /= mag

        # Validate non-degenerate normal map (bevels exist)
        if float(np.std(nx[alpha]) + np.std(ny[alpha])) < 0.03:
            return None

        out = np.zeros((h, w, 4), dtype=np.uint8)
        out[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
        out[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
        out[..., 3] = 255
        out[~alpha] = [128, 128, 255, 0]
        return out


def build_qa_preview_sheet(
    sprite_1x: np.ndarray,
    normal_1x: np.ndarray,
    scale: int = 8,
) -> Image.Image:
    """
    Create a crisp 8x nearest-neighbor visual QA strip:
      [Panel 1: 1x Albedo on dark checkerboard]
      [Panel 2: 1x Tangent-Space Normal Map]
      [Panel 3: Simulated Songs-of-Conquest Warm Torch + Cool Indigo Rim Lighting]
    """
    h, w, _ = sprite_1x.shape
    alpha = (sprite_1x[..., 3] == 255)[..., None]

    # Dark checkerboard background
    yy, xx = np.indices((h, w))
    chk = (((yy // 4) + (xx // 4)) % 2 == 0)[..., None]
    bg = np.where(chk, np.array([24, 28, 42], dtype=np.uint8), np.array([16, 19, 30], dtype=np.uint8))

    p1 = np.where(alpha, sprite_1x[..., :3], bg)
    p2 = np.where(alpha, normal_1x[..., :3], np.array([128, 128, 255], dtype=np.uint8))

    # Simulated 3D lighting: warm upper-left torch + cool lower-right indigo bounce
    albedo = sprite_1x[..., :3].astype(np.float32) / 255.0
    n = (normal_1x[..., :3].astype(np.float32) - 127.5) / 127.5
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-8

    l_torch = np.array([-0.55, 0.58, 0.60], dtype=np.float32)
    l_torch /= np.linalg.norm(l_torch)
    l_cool = np.array([0.60, -0.45, 0.66], dtype=np.float32)
    l_cool /= np.linalg.norm(l_cool)

    ndotl_warm = np.clip(np.sum(n * l_torch[None, None, :], axis=-1), 0.0, 1.0)[..., None]
    ndotl_cool = np.clip(np.sum(n * l_cool[None, None, :], axis=-1), 0.0, 1.0)[..., None]

    warm_col = np.array([1.28, 0.92, 0.56], dtype=np.float32)
    cool_col = np.array([0.32, 0.44, 0.85], dtype=np.float32)
    ambient = np.array([0.22, 0.25, 0.36], dtype=np.float32)

    lit = albedo * (ambient + ndotl_warm * warm_col + ndotl_cool * cool_col * 0.65)
    lit_u8 = np.clip(np.round(lit * 255.0), 0, 255).astype(np.uint8)
    p3 = np.where(alpha, lit_u8, np.array([14, 17, 29], dtype=np.uint8))

    sep = np.full((h, 2, 3), [48, 56, 82], dtype=np.uint8)
    strip = np.concatenate([p1, sep, p2, sep, p3], axis=1)
    img = Image.fromarray(strip, mode="RGB")
    return img.resize((strip.shape[1] * scale, strip.shape[0] * scale), Image.Resampling.NEAREST)


def validate_outputs(
    sprite_1x: np.ndarray,
    normal_1x: np.ndarray,
    palette_rgb: np.ndarray,
) -> dict:
    opaque = sprite_1x[..., 3] == 255
    alpha_vals = sorted(int(v) for v in np.unique(sprite_1x[..., 3]))
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used_colors = {tuple(int(c) for c in row) for row in sprite_1x[opaque, :3]}
    off_palette = len(used_colors - pal_set)

    # Normal vector stats on opaque pixels
    n = (normal_1x[opaque, :3].astype(np.float32) - 127.5) / 127.5
    mags = np.linalg.norm(n, axis=-1) if len(n) else np.array([1.0])
    bg_normals = normal_1x[~opaque]
    bg_neutral_ok = bool(
        len(bg_normals) == 0
        or np.all(bg_normals == np.array([128, 128, 255, 0], dtype=np.uint8))
    )

    return {
        "width": int(sprite_1x.shape[1]),
        "height": int(sprite_1x.shape[0]),
        "opaque_pixels": int(np.sum(opaque)),
        "opaque_coverage_pct": round(100.0 * float(np.mean(opaque)), 2),
        "alpha_values": alpha_vals,
        "alpha_is_1bit": alpha_vals in ([0, 255], [255]),
        "unique_palette_colors": len(used_colors),
        "off_palette_colors": off_palette,
        "normal_mean_rgb": [round(float(normal_1x[opaque, c].mean()), 1) for c in range(3)],
        "normal_xy_std": round(float(np.std(n[:, 0]) + np.std(n[:, 1])), 3) if len(n) else 0.0,
        "normal_unit_mag_mean": round(float(mags.mean()), 4),
        "normal_min_z": round(float(n[:, 2].min()), 3) if len(n) else 1.0,
        "normal_bg_neutral_ok": bg_neutral_ok,
    }


def process_sprite(
    input_path: Path,
    output_albedo: Path,
    output_normal: Path | None = None,
    output_preview: Path | None = None,
    target_size: int = 64,
    pad_px: int = 2,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    dither_strength: float = 0.0,
    grade_strength: float = 0.25,
    normal_backend: str = "python",
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> dict:
    raw = np.array(Image.open(input_path).convert("RGBA"))
    palette_rgb = load_palette(palette_path)

    rgb_clean, fg_mask = remove_background_1bit(raw)
    sprite_1x = snap_to_1x_pixel_grid(rgb_clean, fg_mask, target_size=target_size, pad_px=pad_px)
    sprite_1x = apply_moody_grade_and_quantize(
        sprite_1x,
        palette_rgb,
        dither_strength=dither_strength,
        grade_strength=grade_strength,
    )

    backend_used = "python"
    normal_1x = None
    if normal_backend in ("auto", "laigter"):
        normal_1x = generate_normal_map_laigter(sprite_1x, laigter_exe=laigter_exe)
        if normal_1x is not None:
            backend_used = "laigter"
        elif normal_backend == "laigter":
            raise RuntimeError(f"Laigter headless normal generation failed using {laigter_exe}")

    if normal_1x is None:
        normal_1x = generate_normal_map_python(sprite_1x)
        backend_used = "python"

    output_albedo.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(sprite_1x, mode="RGBA").save(output_albedo, format="PNG")

    if output_normal is None:
        output_normal = output_albedo.with_name(f"{output_albedo.stem}_n.png")
    output_normal.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(normal_1x, mode="RGBA").save(output_normal, format="PNG")

    if output_preview is not None:
        output_preview.parent.mkdir(parents=True, exist_ok=True)
        build_qa_preview_sheet(sprite_1x, normal_1x, scale=8).save(output_preview, format="PNG")

    stats = validate_outputs(sprite_1x, normal_1x, palette_rgb)
    stats["normal_backend"] = backend_used
    stats["albedo_path"] = str(output_albedo)
    stats["normal_path"] = str(output_normal)
    if output_preview is not None:
        stats["preview_path"] = str(output_preview)
    return stats


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Realmz HD-2D 1x Pixel-Art + Normal-Map Post-Processor"
    )
    parser.add_argument("input", type=Path, help="Raw ComfyUI PNG output")
    parser.add_argument("-o", "--output", type=Path, required=True, help="Output 1x albedo PNG")
    parser.add_argument("--normal", type=Path, default=None, help="Output 1x normal PNG (*_n.png)")
    parser.add_argument("--preview", type=Path, default=None, help="Optional 8x QA strip PNG")
    parser.add_argument("--size", type=int, default=64, help="Target 1x sprite size (32/64/96/128)")
    parser.add_argument("--pad", type=int, default=2, help="Padding pixels inside 1x canvas")
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH)
    parser.add_argument("--dither", type=float, default=0.0, help="Bayer 2x2 dither strength (0.0-0.15)")
    parser.add_argument("--grade", type=float, default=0.25, help="Warm/cool grade strength (0.0-0.5)")
    parser.add_argument(
        "--normal-backend",
        choices=("python", "laigter", "auto"),
        default="python",
        help="Normal map generator backend (python default for crisp #8080FF bevels; laigter optional)",
    )
    parser.add_argument("--laigter-exe", type=Path, default=DEFAULT_LAIGTER_EXE)
    args = parser.parse_args()

    stats = process_sprite(
        input_path=args.input,
        output_albedo=args.output,
        output_normal=args.normal,
        output_preview=args.preview,
        target_size=args.size,
        pad_px=args.pad,
        palette_path=args.palette,
        dither_strength=args.dither,
        grade_strength=args.grade,
        normal_backend=args.normal_backend,
        laigter_exe=args.laigter_exe,
    )
    print(json.dumps(stats, indent=2))


if __name__ == "__main__":
    main()
