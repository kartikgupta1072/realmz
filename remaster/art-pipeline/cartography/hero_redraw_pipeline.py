"""
Realmz HD-2D Hero Redraw Pipeline:
  1. Master Cutout & Framing (128x128 RGBA, BottomCenter pivot at y=125,
     target opaque height 88-96 px, pinhole fill, <4px dust removal,
     cobblestone ink outline guard, sunlit palette quantization).
  2. Faithful 4-Frame Animation Synthesis (Idle, Walk, Attack as 512x128 RGBA
     strips with strict per-frame 128x128 isolation and palette lock).
  3. Enhanced Volumetric + Laigter OpenGL (+Y) Normal Maps (55% Laigter / Sobel
     + 45% cylindrical/dome hero volume + EDT bevel, nz >= 0.35, [128,128,255,0]
     transparent background).
  4. 4x Nearest-Neighbor QA Contact Sheets over dark checkerboard and tiled
     cobble_plaza.png, plus CLI (--manifest / --single / --out-dir / --qa-dir /
     --promote-dir).
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple, Union

import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from pixel_postprocess import (  # noqa: E402
    CREATE_NO_WINDOW,
    DEFAULT_LAIGTER_EXE,
    DEFAULT_PALETTE_PATH,
    apply_sunlit_grade_and_quantize,
    generate_normal_map_laigter,
    generate_normal_map_sobel_fallback,
    load_palette,
    pixeloe_downscale_sprite,
    remove_background_1bit,
    rgb_to_lab_float,
)

CANVAS_W = 128
CANVAS_H = 128
FRAME_COUNT = 4
SHEET_W = CANVAS_W * FRAME_COUNT
GROUND_Y = 125
SAFE_MIN = 2
SAFE_MAX = 125
INK_RGB = np.array([17, 19, 31], dtype=np.uint8)  # #11131F
DEFAULT_COBBLE_PATH = (
    Path(__file__).resolve().parents[2]
    / "RealmzUnity"
    / "Assets"
    / "Art"
    / "Cartography"
    / "tiles"
    / "cobble_plaza.png"
)


def normalize_hero_role(role: str) -> str:
    """Normalize role name to canonical 'hero_<short>' identifier."""
    cleaned = role.strip().lower().replace("-", "_").replace(" ", "_")
    if cleaned.startswith("hero_"):
        return cleaned
    return f"hero_{cleaned}"


def _short_role(role: str) -> str:
    norm = normalize_hero_role(role)
    return norm[len("hero_") :]


def _load_rgba_input(source: Union[str, Path, np.ndarray]) -> np.ndarray:
    """Load an image path or normalize an RGB/RGBA numpy array to uint8 RGBA."""
    if isinstance(source, (str, Path)):
        return np.array(Image.open(Path(source)).convert("RGBA"), dtype=np.uint8)
    arr = np.asarray(source)
    if arr.ndim != 3 or arr.shape[2] not in (3, 4):
        raise ValueError(f"Expected HxWx3 or HxWx4 array, got shape {arr.shape}")
    if arr.dtype != np.uint8:
        arr = np.clip(np.round(arr), 0, 255).astype(np.uint8)
    else:
        arr = arr.copy()
    if arr.shape[2] == 3:
        rgba = np.empty((arr.shape[0], arr.shape[1], 4), dtype=np.uint8)
        rgba[..., :3] = arr
        rgba[..., 3] = 255
        return rgba
    return arr


def _quantize_preserve_colors(rgba: np.ndarray, palette_rgb: np.ndarray) -> np.ndarray:
    """Snap all opaque pixels directly to palette_rgb in CIELAB with zero grade shift."""
    out = rgba.copy()
    active = out[..., 3] >= 128
    out[~active] = 0
    if not np.any(active):
        return out
    pal_lab = rgb_to_lab_float(palette_rgb)
    pix_lab = rgb_to_lab_float(out[active, :3])
    n_pix = pix_lab.shape[0]
    nearest = np.empty(n_pix, dtype=np.int32)
    chunk = 8192
    for start in range(0, n_pix, chunk):
        end = min(n_pix, start + chunk)
        diff = pix_lab[start:end, None, :] - pal_lab[None, :, :]
        dist2 = (1.10 * diff[..., 0] ** 2) + (1.15 * diff[..., 1] ** 2) + (1.15 * diff[..., 2] ** 2)
        nearest[start:end] = np.argmin(dist2, axis=1)
    out[active, :3] = palette_rgb[nearest]
    out[active, 3] = 255
    return out


def _remove_small_dust(canvas: np.ndarray, min_component_px: int = 4) -> np.ndarray:
    """Remove isolated foreground components smaller than min_component_px."""
    out = canvas.copy()
    op = out[..., 3] >= 128
    if not np.any(op):
        return out
    labeled, n_comp = ndimage.label(op, structure=np.ones((3, 3), dtype=bool))
    if n_comp <= 1:
        return out
    sizes = ndimage.sum(op, labeled, range(1, n_comp + 1))
    max_sz = float(np.max(sizes)) if len(sizes) else 0.0
    if max_sz < min_component_px:
        return out
    for idx, sz in enumerate(sizes, start=1):
        if sz < min_component_px:
            comp = labeled == idx
            out[comp] = 0
    return out


def _fill_interior_pinholes(canvas: np.ndarray) -> np.ndarray:
    """Fill 1 px interior alpha pinholes completely 4-surrounded by opaque foreground."""
    out = canvas.copy()
    op = out[..., 3] >= 128
    if not np.any(op):
        return out
    up = np.zeros_like(op)
    dn = np.zeros_like(op)
    lf = np.zeros_like(op)
    rt = np.zeros_like(op)
    up[1:, :] = op[:-1, :]
    dn[:-1, :] = op[1:, :]
    lf[:, 1:] = op[:, :-1]
    rt[:, :-1] = op[:, 1:]
    pinholes = (~op) & up & dn & lf & rt
    py, px = np.where(pinholes)
    for y, x in zip(py, px):
        nbrs = np.array(
            [
                out[y - 1, x, :3],
                out[y + 1, x, :3],
                out[y, x - 1, :3],
                out[y, x + 1, :3],
            ],
            dtype=np.float32,
        )
        out[y, x, :3] = np.clip(np.round(np.mean(nbrs, axis=0)), 0, 255).astype(np.uint8)
        out[y, x, 3] = 255
    return out


def _anchor_bottom_and_center(canvas: np.ndarray, ground_y: int = GROUND_Y) -> np.ndarray:
    """Shift opaque silhouette vertically so lowest opaque row lands strictly at ground_y."""
    h, w, _ = canvas.shape
    op = canvas[..., 3] >= 128
    ys, xs = np.where(op)
    if len(ys) == 0:
        return canvas.copy()
    y_bot = int(ys.max())
    dy = ground_y - y_bot
    if dy < 0:
        # Never clip top above SAFE_MIN if we can avoid it
        dy = max(dy, SAFE_MIN - int(ys.min()))
    x_min, x_max = int(xs.min()), int(xs.max())
    dx = 0
    if x_min < SAFE_MIN:
        dx = SAFE_MIN - x_min
    elif x_max > SAFE_MAX:
        dx = SAFE_MAX - x_max
    if dy == 0 and dx == 0:
        return canvas.copy()
    out = np.zeros_like(canvas)
    for y in ys:
        pass
    src_y0 = max(0, -dy)
    src_y1 = min(h, h - dy)
    dst_y0 = max(0, dy)
    dst_y1 = min(h, h + dy)
    src_x0 = max(0, -dx)
    src_x1 = min(w, w - dx)
    dst_x0 = max(0, dx)
    dst_x1 = min(w, w + dx)
    if src_y1 > src_y0 and src_x1 > src_x0:
        out[dst_y0:dst_y1, dst_x0:dst_x1] = canvas[src_y0:src_y1, src_x0:src_x1]
    return out


def enforce_cobblestone_ink_outline(canvas: np.ndarray) -> np.ndarray:
    """
    Cobblestone Pop Outline Guard:
      - Adds a 1px outer dark ink (#11131F) contour where transparent pixels inside
        x in [2..125], y in [2..125] are 4-adjacent to pale opaque pixels (L > 42),
        keeping feet grounded at y = 125 (never expanding onto y >= 126).
      - Snaps any remaining washed-out/pale 1px outer perimeter pixels (L > 28 in
        CIELAB) to dark ink (#11131F) so the hero separates cleanly on cobble_plaza.
    """
    out = canvas.copy()
    op = out[..., 3] >= 128
    if not np.any(op):
        return out

    h, w, _ = out.shape
    lab = rgb_to_lab_float(out[..., :3])
    L = lab[..., 0]
    pale_src = op & (L > 42.0)

    # 4-dilate pale_src into transparent interior safe box [2..125, 2..125]
    cross = np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], dtype=bool)
    pale_dil = ndimage.binary_dilation(pale_src, structure=cross)
    safe_box = np.zeros((h, w), dtype=bool)
    safe_box[SAFE_MIN : SAFE_MAX + 1, SAFE_MIN : SAFE_MAX + 1] = True

    outer_ink = (~op) & pale_dil & safe_box
    if np.any(outer_ink):
        out[outer_ink, :3] = INK_RGB
        out[outer_ink, 3] = 255

    # Inspect 1px outer perimeter of updated silhouette and snap washed-out L > 28 to dark ink
    op2 = out[..., 3] >= 128
    eroded2 = ndimage.binary_erosion(op2, structure=cross, border_value=0)
    perim2 = op2 & (~eroded2)
    lab2 = rgb_to_lab_float(out[..., :3])
    L2 = lab2[..., 0]
    snap_mask = perim2 & (L2 > 28.0)
    if np.any(snap_mask):
        out[snap_mask, :3] = INK_RGB
        out[snap_mask, 3] = 255

    return out


def _punch_enclosed_studio_white_pockets(
    rgb: np.ndarray,
    fg_mask: np.ndarray,
) -> np.ndarray:
    """
    Punch trapped enclosed pure-white studio backdrop pockets inside weapon/arm loops
    (e.g. inside a bowstring loop or between a wizard staff and robes) without touching
    shaded white/cream clothing highlights.
    """
    bg_rgb = rgb[~fg_mask]
    if len(bg_rgb) == 0:
        return fg_mask
    border_med = np.median(bg_rgb, axis=0).astype(np.uint8)
    chroma_bg = int(np.max(border_med)) - int(np.min(border_med))
    bg_lab = rgb_to_lab_float(border_med.reshape(1, 3))[0]
    if bg_lab[0] < 92.0 or chroma_bg >= 18:
        return fg_mask

    img_lab = rgb_to_lab_float(rgb)
    dist = np.linalg.norm(img_lab - bg_lab[None, None, :], axis=-1)
    cand = fg_mask & (dist <= 4.5)
    if not np.any(cand):
        return fg_mask

    lbl, n_comp = ndimage.label(cand)
    out_mask = fg_mask.copy()
    for idx in range(1, n_comp + 1):
        comp = lbl == idx
        sz = int(np.sum(comp))
        if sz < 32:
            continue
        mean_d = float(np.mean(dist[comp]))
        mean_l = float(np.mean(img_lab[..., 0][comp]))
        if mean_d <= 1.35 and mean_l >= 99.1:
            out_mask[comp] = False
    return out_mask


def build_clean_hero_master(
    source: Union[str, Path, np.ndarray],
    palette_rgb: Optional[np.ndarray] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    target_height_range: Tuple[int, int] = (88, 96),
    pad_px: int = 2,
) -> np.ndarray:
    """
    Build a clean 128x128 RGBA BottomCenter hero master sprite:
      - Removes white/chroma studio backdrop and ground shadow with 1-bit keying.
      - Punches trapped pure-white studio backdrop loops inside bows/staves.
      - Runs pixeloe_downscale_sprite with uniform scaling targeting 88-96 px height
        (clamped inside x in [2..125] without top/side clipping) and feet at y = 125.
      - Removes < 4 px stray dust components and fills 1 px interior alpha pinholes.
      - Applies enforce_cobblestone_ink_outline for dark #11131F contrast edges.
      - Grades and strictly quantizes to palette_soc_sunlit_128.json with {0, 255} alpha.
    """
    if palette_rgb is None:
        palette_rgb = load_palette(palette_path)

    raw_rgba = _load_rgba_input(source)
    rgb_clean, fg_mask = remove_background_1bit(raw_rgba, strip_ground_shadow=True)
    fg_mask = _punch_enclosed_studio_white_pockets(rgb_clean, fg_mask)

    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Hero background removal produced an empty foreground mask.")
    ch = int(ys.max() - ys.min() + 1)
    cw = int(xs.max() - xs.min() + 1)

    lo_h, hi_h = int(target_height_range[0]), int(target_height_range[1])
    # Target ~91 px before 1px top outline so final height lands cleanly inside [88..96]
    desired_h = max(lo_h, min(hi_h, (lo_h + hi_h) // 2 - 1))
    max_fit_w = CANVAS_W - 2 * (pad_px + 1)  # Leave room for 1px outline inside [2..125]
    max_fit_h = hi_h - 1

    scale_h = desired_h / float(max(1, ch))
    scale_w = max_fit_w / float(max(1, cw))
    scale = min(scale_h, scale_w)
    fit_h = max(1, min(max_fit_h, int(round(ch * scale))))
    fit_w = max(1, min(max_fit_w, int(round(cw * scale))))

    # Use pixeloe_downscale_sprite on an exact sub-canvas sized so PixelOE produces
    # the uniform (fit_w, fit_h) sprite with pad_px=2, bottom_pivot=True, then place
    # onto the 128x128 master canvas anchored at y = 125.
    sub_w = fit_w + 2 * pad_px
    sub_h = fit_h + 2 * pad_px
    sub_sprite = pixeloe_downscale_sprite(
        rgb_clean,
        fg_mask,
        target_size=(sub_w, sub_h),
        pad_px=pad_px,
        bottom_pivot=True,
    )

    canvas = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    sub_op = sub_sprite[..., 3] >= 128
    s_ys, s_xs = np.where(sub_op)
    if len(s_ys) == 0:
        raise RuntimeError("PixelOE downscale produced an empty hero sprite.")

    sy0, sy1 = int(s_ys.min()), int(s_ys.max()) + 1
    sx0, sx1 = int(s_xs.min()), int(s_xs.max()) + 1
    crop_sub = sub_sprite[sy0:sy1, sx0:sx1]
    crop_h, crop_w, _ = crop_sub.shape

    ox = max(SAFE_MIN + 1, (CANVAS_W - crop_w) // 2)
    if ox + crop_w > SAFE_MAX:
        ox = max(SAFE_MIN, SAFE_MAX - crop_w)
    oy = max(SAFE_MIN + 1, GROUND_Y + 1 - crop_h)
    paste_h = min(crop_h, CANVAS_H - oy)
    paste_w = min(crop_w, CANVAS_W - ox)
    canvas[oy : oy + paste_h, ox : ox + paste_w] = crop_sub[:paste_h, :paste_w]

    canvas = _remove_small_dust(canvas, min_component_px=4)
    canvas = _fill_interior_pinholes(canvas)
    canvas = _anchor_bottom_and_center(canvas, ground_y=GROUND_Y)
    canvas = enforce_cobblestone_ink_outline(canvas)

    canvas = apply_sunlit_grade_and_quantize(
        canvas,
        palette_rgb,
        dither_strength=0.0,
        grade_strength=0.10,
        shadow_lift=3.0,
        vibrance_boost=1.06,
        preserve_alpha=False,
    )
    canvas = _anchor_bottom_and_center(canvas, ground_y=GROUND_Y)
    return canvas


def _shift_mask_pixels(
    src: np.ndarray,
    mask: np.ndarray,
    dy: int,
    dx: int,
    dst: np.ndarray,
    clamp_box: Tuple[int, int, int, int] = (SAFE_MIN, SAFE_MAX, SAFE_MIN, GROUND_Y),
) -> None:
    """Copy pixels from src where mask is True, shifted by (dy, dx), into dst within clamp_box."""
    min_x, max_x, min_y, max_y = clamp_box
    ys, xs = np.where(mask & (src[..., 3] >= 128))
    if len(ys) == 0:
        return
    ny = ys + dy
    nx = xs + dx
    valid = (ny >= min_y) & (ny <= max_y) & (nx >= min_x) & (nx <= max_x)
    dst[ny[valid], nx[valid]] = src[ys[valid], xs[valid]]


def _apply_accent_glow_pulse(
    frame: np.ndarray,
    delta_l: float,
    palette_rgb: np.ndarray,
) -> np.ndarray:
    """Apply a palette-quantized highlight pulse on bright or saturated accent pixels."""
    if abs(delta_l) < 1e-3:
        return frame
    out = frame.copy()
    op = out[..., 3] >= 128
    if not np.any(op):
        return out
    lab = rgb_to_lab_float(out[..., :3])
    L = lab[..., 0]
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    accent = op & ((L >= 58.0) | ((chroma >= 28.0) & (L >= 42.0)))
    if not np.any(accent):
        accent = op & (L >= float(np.percentile(L[op], 75)))
    if np.any(accent):
        lab_mod = lab.copy()
        lab_mod[accent, 0] = np.clip(lab_mod[accent, 0] + delta_l, 0.0, 100.0)
        from pixel_postprocess import lab_float_to_rgb_u8

        out[..., :3] = lab_float_to_rgb_u8(lab_mod)
        out = _quantize_preserve_colors(out, palette_rgb)
    return out


def _apply_upper_breeze_sway(
    frame: np.ndarray,
    y_top: int,
    h_op: int,
    sway_dx: int,
) -> np.ndarray:
    """Apply 1 px horizontal breeze sway on outer upper cape/plume columns."""
    if sway_dx == 0:
        return frame
    out = frame.copy()
    band_end = min(GROUND_Y - 1, y_top + max(6, int(round(h_op * 0.45))))
    for y in range(max(SAFE_MIN, y_top), band_end + 1):
        xs_row = np.where(out[y, :, 3] >= 128)[0]
        if len(xs_row) < 6:
            continue
        x_l, x_r = int(xs_row.min()), int(xs_row.max())
        # Sway outer 3 columns on left and right edges of upper silhouette
        edge_cols = list(range(x_l, min(x_l + 3, x_r))) + list(range(max(x_l + 3, x_r - 2), x_r + 1))
        row_copy = out[y].copy()
        for x in edge_cols:
            nx = max(SAFE_MIN, min(SAFE_MAX, x + sway_dx))
            if out[y, x, 3] >= 128:
                out[y, nx] = row_copy[x]
        # Avoid 1px holes inside the row after swaying outward
        if sway_dx > 0:
            for x in range(x_l, x_l + sway_dx):
                if x + 1 <= x_r and out[y, x, 3] == 0 and out[y, x + 1, 3] >= 128:
                    out[y, x] = out[y, x + 1]
        elif sway_dx < 0:
            for x in range(x_r + sway_dx + 1, x_r + 1):
                if x - 1 >= x_l and out[y, x, 3] == 0 and out[y, x - 1, 3] >= 128:
                    out[y, x] = out[y, x - 1]
    return out


def _synth_idle_frames(master: np.ndarray, palette_rgb: np.ndarray) -> List[np.ndarray]:
    """Synthesize 4-frame Idle animation with anchored feet, 1px breath, breeze sway, and glow pulse."""
    ys = np.where(master[..., 3] >= 128)[0]
    y_top, y_bot = int(ys.min()), int(ys.max())
    h_op = y_bot - y_top + 1
    leg_h = max(6, int(round(h_op * 0.22)))
    y_leg_start = y_bot - leg_h + 1

    breath_dys = [0, -1, -1, 0]
    sway_dxs = [0, 1, 0, -1]
    glow_deltas = [0.0, 8.0, 15.0, 4.0]

    frames: List[np.ndarray] = []
    for f in range(FRAME_COUNT):
        dy = breath_dys[f]
        if dy == 0:
            frame = master.copy()
        else:
            frame = np.zeros_like(master)
            # Anchor lower legs/boots strictly to ground
            leg_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            leg_mask[y_leg_start:, :] = True
            _shift_mask_pixels(master, leg_mask, 0, 0, frame)

            # Seamless waist fill: preserve waist row before shifting upper torso up
            waist_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            waist_mask[max(SAFE_MIN, y_leg_start - 1) : y_leg_start, :] = True
            _shift_mask_pixels(master, waist_mask, 0, 0, frame)

            # Upper body/head/shoulders breath shift
            upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            upper_mask[:y_leg_start, :] = True
            _shift_mask_pixels(master, upper_mask, dy, 0, frame)

        frame = _apply_upper_breeze_sway(frame, y_top + dy, h_op, sway_dxs[f])
        frame = _apply_accent_glow_pulse(frame, glow_deltas[f], palette_rgb)
        frame = _fill_interior_pinholes(frame)
        frame = _quantize_preserve_colors(frame, palette_rgb)
        frames.append(frame)
    return frames


def _synth_walk_frames(master: np.ndarray, palette_rgb: np.ndarray) -> List[np.ndarray]:
    """Synthesize 4-phase Walk stride loop: [contact-L, passing-up, contact-R, passing-up]."""
    ys, xs = np.where(master[..., 3] >= 128)
    y_top, y_bot = int(ys.min()), int(ys.max())
    h_op = y_bot - y_top + 1
    leg_h = max(6, int(round(h_op * 0.22)))
    y_leg_start = y_bot - leg_h + 1

    leg_xs = xs[ys >= y_leg_start]
    x_mid = int(round(float(np.mean(leg_xs)))) if len(leg_xs) else int(round(float(np.mean(xs))))

    # [contact-L, passing-up-1, contact-R, passing-up-2]
    bounce_dys = [0, -2, 0, -1]
    leg_shears = [(-2, 2), (0, 0), (2, -2), (0, 0)]
    torso_leans = [1, 1, 1, 0]
    arm_sways = [-1, 0, 1, 0]

    y_arm_top = y_top + max(4, int(round(h_op * 0.25)))
    y_arm_bot = y_leg_start

    frames: List[np.ndarray] = []
    for f in range(FRAME_COUNT):
        dy = bounce_dys[f]
        l_dx, r_dx = leg_shears[f]
        lean_dx = torso_leans[f]
        arm_dx = arm_sways[f]

        frame = np.zeros_like(master)

        # 1. Lower legs with tapered shear near waist to prevent crotch gaps
        left_leg = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        right_leg = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        left_leg[y_leg_start:, :x_mid] = True
        right_leg[y_leg_start:, x_mid:] = True

        # Taper upper 3 rows of leg split so hip connection remains solid
        hip_rows = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        hip_rows[y_leg_start : min(y_bot + 1, y_leg_start + 3), :] = True

        l_hip_dx = int(round(l_dx * 0.5))
        r_hip_dx = int(round(r_dx * 0.5))

        # Draw back leg first, then front leg on top
        if l_dx <= r_dx:
            _shift_mask_pixels(master, left_leg & (~hip_rows), dy, l_dx, frame)
            _shift_mask_pixels(master, left_leg & hip_rows, dy, l_hip_dx, frame)
            _shift_mask_pixels(master, right_leg & (~hip_rows), dy, r_dx, frame)
            _shift_mask_pixels(master, right_leg & hip_rows, dy, r_hip_dx, frame)
        else:
            _shift_mask_pixels(master, right_leg & (~hip_rows), dy, r_dx, frame)
            _shift_mask_pixels(master, right_leg & hip_rows, dy, r_hip_dx, frame)
            _shift_mask_pixels(master, left_leg & (~hip_rows), dy, l_dx, frame)
            _shift_mask_pixels(master, left_leg & hip_rows, dy, l_hip_dx, frame)

        # 2. Upper body / torso forward lean + stride bounce
        upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        upper_mask[:y_leg_start, :] = True
        _shift_mask_pixels(master, upper_mask, dy, lean_dx, frame)

        # Bridge waist row in case bounce/lean creates a 1px notch
        waist_bridge = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        waist_bridge[max(SAFE_MIN, y_leg_start - 1) : min(CANVAS_H, y_leg_start + 1), :] = True
        _shift_mask_pixels(master, waist_bridge, dy, int(round(lean_dx * 0.5)), frame)

        # 3. Opposing shoulder/weapon sway on outer arm columns
        if arm_dx != 0:
            for y in range(max(SAFE_MIN, y_arm_top), min(CANVAS_H, y_arm_bot)):
                xs_row = np.where(master[y, :, 3] >= 128)[0]
                if len(xs_row) < 6:
                    continue
                x_l, x_r = int(xs_row.min()), int(xs_row.max())
                outer = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                outer[y, x_l : min(x_l + 4, x_r)] = True
                outer[y, max(x_l + 4, x_r - 3) : x_r + 1] = True
                _shift_mask_pixels(master, outer, dy, lean_dx + arm_dx, frame)

        # Ensure lowest foot row sits at 125 on contact (dy=0) and 123/124 on passing
        frame = _fill_interior_pinholes(frame)
        frame = _quantize_preserve_colors(frame, palette_rgb)
        frames.append(frame)
    return frames


def _draw_pixel_vfx_arc(
    frame: np.ndarray,
    role: str,
    x_front: int,
    y_mid: int,
    phase: str,
    palette_rgb: np.ndarray,
) -> np.ndarray:
    """
    Draw crisp class-themed pixel-art strike VFX strictly inside [2..125, 2..125]
    and quantize to palette_soc_sunlit_128.json.
    """
    out = frame.copy()
    norm_role = normalize_hero_role(role)

    # Select class VFX palette colors
    if norm_role in ("hero_ranger", "hero_archer"):
        c_core = np.array([232, 252, 202, 255], dtype=np.uint8)  # #E8FCCA
        c_mid = np.array([180, 236, 134, 255], dtype=np.uint8)   # #B4EC86
        c_rim = np.array([82, 166, 83, 255], dtype=np.uint8)     # #52A653
        style = "arrow"
    elif norm_role == "hero_cleric":
        c_core = np.array([255, 243, 176, 255], dtype=np.uint8)  # #FFF3B0
        c_mid = np.array([255, 201, 82, 255], dtype=np.uint8)    # #FFC952
        c_rim = np.array([236, 169, 56, 255], dtype=np.uint8)    # #ECA938
        style = "flare"
    elif norm_role in ("hero_mage", "hero_enchanter", "hero_druid"):
        c_core = np.array([189, 247, 238, 255], dtype=np.uint8)  # #BDF7EE
        c_mid = np.array([133, 230, 218, 255], dtype=np.uint8)   # #85E6DA
        c_rim = np.array([38, 127, 130, 255], dtype=np.uint8)    # #267F82
        style = "arcane"
    else:
        # hero_knight, hero_paladin, hero_rogue, etc.: steel/gold crescent slash
        c_core = np.array([238, 244, 252, 255], dtype=np.uint8)  # #EEF4FC
        c_mid = np.array([255, 201, 82, 255], dtype=np.uint8)    # #FFC952
        c_rim = np.array([154, 167, 189, 255], dtype=np.uint8)   # #9AA7BD
        style = "slash"

    def put(px: int, py: int, col: np.ndarray) -> None:
        if SAFE_MIN <= px <= SAFE_MAX and SAFE_MIN <= py <= SAFE_MAX:
            out[py, px] = col

    if norm_role == "hero_knight":
        cx = min(SAFE_MAX - 10, max(SAFE_MIN + 10, x_front - 2))
    elif norm_role == "hero_cleric":
        cx = min(SAFE_MAX - 12, max(SAFE_MIN + 12, x_front))
    else:
        cx = min(SAFE_MAX - 8, max(SAFE_MIN + 10, x_front + 4))
    cy = max(SAFE_MIN + 14, min(GROUND_Y - 16, y_mid))

    if phase == "lunge":
        if style == "slash":
            # Bold crescent slash arc spanning ~36 px vertically; mirror sweep on knight sword side
            dir_sign = -1.0 if norm_role == "hero_knight" else 1.0
            for deg in range(-65, 70, 3):
                rad = math.radians(deg)
                rx = int(round(cx + dir_sign * (12.0 * math.cos(rad) - 6.0)))
                ry = int(round(cy + 20.0 * math.sin(rad)))
                for dx_t, col in ((-1, c_rim), (0, c_mid), (1, c_core), (2, c_core), (3, c_mid)):
                    put(rx + int(dir_sign) * dx_t, ry, col)
                put(rx + int(dir_sign), ry - 1, c_mid)
                put(rx + int(dir_sign), ry + 1, c_mid)
        elif style == "arrow":
            # Crisp emerald arrow streak + wind chevron
            tip_x = min(SAFE_MAX - 1, cx + 16)
            for x in range(max(SAFE_MIN, cx - 10), tip_x + 1):
                put(x, cy - 1, c_rim)
                put(x, cy, c_core)
                put(x, cy + 1, c_mid)
            for k in range(1, 5):
                put(tip_x - k, cy - k, c_mid)
                put(tip_x - k, cy + k, c_mid)
                put(tip_x - k - 1, cy - k, c_rim)
                put(tip_x - k - 1, cy + k, c_rim)
        elif style == "flare":
            # Sun-gold radiant starburst
            for r in range(0, 14):
                col = c_core if r < 5 else (c_mid if r < 10 else c_rim)
                put(cx + r, cy, col)
                put(cx - r, cy, col)
                put(cx, cy + r, col)
                put(cx, cy - r, col)
                if r < 10:
                    put(cx + r, cy + r, col)
                    put(cx + r, cy - r, col)
                    put(cx - r, cy + r, col)
                    put(cx - r, cy - r, col)
            for dy_c in range(-2, 3):
                for dx_c in range(-2, 3):
                    put(cx + dx_c, cy + dy_c, c_core)
        else:
            # Cyan arcane burst ring + rays
            for deg in range(0, 360, 10):
                rad = math.radians(deg)
                for r, col in ((6, c_rim), (8, c_mid), (9, c_core), (11, c_mid)):
                    put(int(round(cx + r * math.cos(rad))), int(round(cy + r * math.sin(rad))), col)
            for dx_c in range(-3, 4):
                for dy_c in range(-3, 4):
                    if abs(dx_c) + abs(dy_c) <= 4:
                        put(cx + dx_c, cy + dy_c, c_core)
    elif phase == "follow":
        # Fading secondary glint / spark trail
        for idx, (ox, oy) in enumerate(((4, -8), (7, -2), (5, 5), (9, 2), (2, 10))):
            col = c_core if idx % 2 == 0 else c_mid
            put(cx + ox, cy + oy, col)
            put(cx + ox + 1, cy + oy, c_rim)
    elif phase == "recover":
        # Subtle trailing ember / gleam
        for ox, oy in ((2, -4), (4, 1)):
            put(cx + ox, cy + oy, c_mid)

    return _quantize_preserve_colors(out, palette_rgb)


def _synth_attack_frames(
    master: np.ndarray,
    role: str,
    palette_rgb: np.ndarray,
) -> List[np.ndarray]:
    """
    Synthesize 4-phase combat attack strip:
      0: Anticipation (-3 px coil back + 1 px crouch)
      1: Strike Lunge (+5 px forward lunge clamped <= 125 + class-themed VFX)
      2: Follow-Through (+3 px forward extension + fading secondary glint)
      3: Recovery (+1 px settle back toward neutral stance)
    """
    ys, xs = np.where(master[..., 3] >= 128)
    y_top, y_bot = int(ys.min()), int(ys.max())
    x_min, x_max = int(xs.min()), int(xs.max())
    h_op = y_bot - y_top + 1
    leg_h = max(6, int(round(h_op * 0.22)))
    y_leg_start = y_bot - leg_h + 1
    norm_role = normalize_hero_role(role)
    if norm_role == "hero_mage":
        y_mid = y_top + int(round(h_op * 0.25))
    elif norm_role == "hero_cleric":
        y_mid = y_top + int(round(h_op * 0.30))
    elif norm_role == "hero_knight":
        y_mid = y_top + int(round(h_op * 0.62))
    else:
        y_mid = y_top + int(round(h_op * 0.44))

    def _vfx_anchor_x(shift_dx: int) -> int:
        if norm_role in ("hero_knight", "hero_cleric"):
            return x_min + shift_dx
        if norm_role == "hero_mage":
            return x_max + shift_dx - 6
        return x_max + shift_dx

    max_right_safe = max(0, SAFE_MAX - x_max)
    max_left_safe = max(0, x_min - SAFE_MIN)

    # Frame 0: Anticipation (-3 px coil back + 1 px upper crouch, legs anchored at y=125)
    f0 = np.zeros_like(master)
    coil_dx = -min(3, max_left_safe)
    leg_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
    leg_mask[y_leg_start:, :] = True
    upper_mask = ~leg_mask
    _shift_mask_pixels(master, leg_mask, 0, max(-1, coil_dx), f0)
    _shift_mask_pixels(master, upper_mask, 1, coil_dx, f0)
    f0 = _fill_interior_pinholes(f0)
    f0 = _quantize_preserve_colors(f0, palette_rgb)

    # Frame 1: Strike Lunge (+5 px forward lunge clamped <= 125 + class VFX)
    f1 = np.zeros_like(master)
    lunge_dx = min(5, max_right_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, lunge_dx, f1)
    f1 = _draw_pixel_vfx_arc(f1, role, _vfx_anchor_x(lunge_dx), y_mid, "lunge", palette_rgb)

    # Frame 2: Follow-Through (+3 px forward extension + fading secondary glint)
    f2 = np.zeros_like(master)
    follow_dx = min(3, max_right_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, follow_dx, f2)
    f2 = _draw_pixel_vfx_arc(f2, role, _vfx_anchor_x(follow_dx), y_mid, "follow", palette_rgb)

    # Frame 3: Recovery (+1 px settle toward neutral + faint trailing gleam)
    f3 = np.zeros_like(master)
    rec_dx = min(1, max_right_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, rec_dx, f3)
    f3 = _draw_pixel_vfx_arc(f3, role, _vfx_anchor_x(rec_dx), y_mid, "recover", palette_rgb)

    return [f0, f1, f2, f3]


def synthesize_hero_animations(
    master_128: np.ndarray,
    role: str = "hero_knight",
    palette_rgb: Optional[np.ndarray] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
) -> Dict[str, np.ndarray]:
    """
    Synthesize 3 faithful 4-frame 512x128 RGBA animation strips from a 128x128 master:
      - 'sheet': Idle loop (feet locked at y=125, 1px breath, breeze sway, glow pulse)
      - 'walk_sheet': 4-phase Walk stride loop
      - 'attack_sheet': 4-phase Attack strike loop with class-themed pixel VFX
    """
    if master_128.shape != (CANVAS_H, CANVAS_W, 4):
        raise ValueError(f"Expected (128, 128, 4) master sprite, got {master_128.shape}")
    if palette_rgb is None:
        palette_rgb = load_palette(palette_path)

    idle_frames = _synth_idle_frames(master_128, palette_rgb)
    walk_frames = _synth_walk_frames(master_128, palette_rgb)
    attack_frames = _synth_attack_frames(master_128, role, palette_rgb)

    return {
        "sheet": np.concatenate(idle_frames, axis=1),
        "walk_sheet": np.concatenate(walk_frames, axis=1),
        "attack_sheet": np.concatenate(attack_frames, axis=1),
    }


def _compute_hero_volumetric_normal(frame_128: np.ndarray) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """
    Compute smooth cylindrical/dome hero volume normals + EDT bevel:
      - Per-row horizontal span u in [-1, 1] for cylindrical curvature nx_vol.
      - Upper head/torso dome vertical curve ny_vol (+Y up in OpenGL).
      - Smooth EDT edge bevel around silhouette perimeter.
    """
    h, w, _ = frame_128.shape
    alpha = frame_128[..., 3] > 0
    if not np.any(alpha):
        return (
            np.zeros((h, w), dtype=np.float32),
            np.zeros((h, w), dtype=np.float32),
            np.ones((h, w), dtype=np.float32),
        )

    ys = np.where(alpha)[0]
    y_top, y_bot = int(ys.min()), int(ys.max())
    h_op = max(2.0, float(y_bot - y_top))

    nx_cyl = np.zeros((h, w), dtype=np.float32)
    ny_dome = np.zeros((h, w), dtype=np.float32)

    for y in range(y_top, y_bot + 1):
        xs_row = np.where(alpha[y])[0]
        if len(xs_row) == 0:
            continue
        x0, x1 = int(xs_row.min()), int(xs_row.max())
        if x1 > x0:
            u = 2.0 * (xs_row.astype(np.float32) - float(x0)) / float(x1 - x0) - 1.0
            nx_cyl[y, xs_row] = 0.65 * u
        v_rel = float(y - y_top) / h_op
        # Upper head/torso dome (+Y up = top faces +Y, lower body flattens gently)
        if v_rel < 0.55:
            ny_val = 0.50 * math.cos((v_rel / 0.55) * (math.pi * 0.5))
        else:
            ny_val = -0.10 * ((v_rel - 0.55) / 0.45)
        ny_dome[y, xs_row] = float(ny_val)

    # Smooth EDT silhouette bevel
    dist_in = ndimage.distance_transform_edt(alpha).astype(np.float32)
    t = np.clip(dist_in / 4.5, 0.0, 1.0)
    bevel_h = np.sqrt(np.maximum(0.0, 2.0 * t - t * t))
    gx_b = cv2.Sobel(bevel_h, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gy_b = cv2.Sobel(bevel_h, cv2.CV_32F, 0, 1, ksize=3) / 8.0
    nx_bev = -gx_b
    ny_bev = gy_b

    vx = 0.68 * nx_cyl + 0.55 * nx_bev
    vy = 0.68 * ny_dome + 0.55 * ny_bev
    vz = np.ones((h, w), dtype=np.float32)
    mag = np.sqrt(vx * vx + vy * vy + vz * vz) + 1e-8
    return vx / mag, vy / mag, vz / mag


def _generate_single_frame_hero_normal(
    frame_128: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> np.ndarray:
    """Blend 55% Laigter (or Sobel fallback) with 45% hero cylindrical/dome volume normal."""
    h, w, _ = frame_128.shape
    alpha = frame_128[..., 3] > 0
    if not np.any(alpha):
        out = np.zeros((h, w, 4), dtype=np.uint8)
        out[..., :] = [128, 128, 255, 0]
        return out

    laigter_n = generate_normal_map_laigter(
        frame_128,
        laigter_exe=laigter_exe,
        periodic=False,
        xy_scale=0.68,
        min_z=0.32,
    )
    if laigter_n is None:
        laigter_n = generate_normal_map_sobel_fallback(frame_128, periodic=False)

    lx = (laigter_n[..., 0].astype(np.float32) - 127.5) / 127.5
    ly = (laigter_n[..., 1].astype(np.float32) - 127.5) / 127.5
    lz = (laigter_n[..., 2].astype(np.float32) - 127.5) / 127.5

    vx, vy, vz = _compute_hero_volumetric_normal(frame_128)

    bx = 0.55 * lx + 0.45 * vx
    by = 0.55 * ly + 0.45 * vy
    bz = 0.55 * lz + 0.45 * vz

    mag = np.sqrt(bx * bx + by * by + bz * bz) + 1e-8
    bx /= mag
    by /= mag
    bz /= mag

    # Enforce strict nz >= 0.35 after unit normalization
    min_z = 0.35
    low_z = alpha & (bz < min_z)
    if np.any(low_z):
        max_xy = math.sqrt(1.0 - min_z * min_z)
        xy_mag = np.hypot(bx[low_z], by[low_z])
        scale_xy = np.where(xy_mag > 1e-8, max_xy / np.maximum(xy_mag, 1e-8), 0.0)
        bx[low_z] *= scale_xy
        by[low_z] *= scale_xy
        bz[low_z] = min_z

    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[..., 0] = np.clip(np.round(bx * 127.5 + 127.5), 0, 255).astype(np.uint8)
    out[..., 1] = np.clip(np.round(by * 127.5 + 127.5), 0, 255).astype(np.uint8)
    out[..., 2] = np.clip(np.round(bz * 127.5 + 127.5), 128, 255).astype(np.uint8)
    out[..., 3] = 255
    out[~alpha] = [128, 128, 255, 0]
    return out


def generate_hero_sheet_normals(
    sheet_rgba: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> np.ndarray:
    """Generate 512x128 (or k*128 x 128) OpenGL +Y normal map with strict per-frame 128x128 isolation."""
    h, w, _ = sheet_rgba.shape
    if h != CANVAS_H or w % CANVAS_W != 0:
        raise ValueError(f"Expected (128, k*128, 4) sprite sheet, got {sheet_rgba.shape}")
    n_frames = w // CANVAS_W
    norm_frames: List[np.ndarray] = []
    for f in range(n_frames):
        sub = sheet_rgba[:, f * CANVAS_W : (f + 1) * CANVAS_W]
        norm_frames.append(_generate_single_frame_hero_normal(sub, laigter_exe=laigter_exe))
    return np.concatenate(norm_frames, axis=1)


def compute_sheet_qa_stats(
    sheet_rgba: np.ndarray,
    normal_rgba: np.ndarray,
    palette_rgb: np.ndarray,
) -> Dict[str, Any]:
    """Compute QA metrics across a 4-frame 512x128 hero sheet and its normal map."""
    h, w, _ = sheet_rgba.shape
    n_frames = w // CANVAS_W
    active = sheet_rgba[..., 3] == 255
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used_colors = {tuple(int(c) for c in row) for row in sheet_rgba[active, :3]}
    off_palette = len(used_colors - pal_set)

    heights: List[int] = []
    bottom_ys: List[int] = []
    frames: List[np.ndarray] = []
    for f in range(n_frames):
        fr = sheet_rgba[:, f * CANVAS_W : (f + 1) * CANVAS_W]
        frames.append(fr)
        ys = np.where(fr[..., 3] == 255)[0]
        if len(ys):
            heights.append(int(ys.max() - ys.min() + 1))
            bottom_ys.append(int(ys.max()))
        else:
            heights.append(0)
            bottom_ys.append(-1)

    inter_diffs: List[float] = []
    for f in range(n_frames):
        a = frames[f]
        b = frames[(f + 1) % n_frames]
        union_op = (a[..., 3] == 255) | (b[..., 3] == 255)
        denom = max(1, int(np.sum(union_op)))
        diff_px = int(np.sum(np.any(a != b, axis=-1)))
        inter_diffs.append(round(100.0 * float(diff_px) / float(denom), 2))

    nx = (normal_rgba[active, 0].astype(np.float32) - 127.5) / 127.5
    ny = (normal_rgba[active, 1].astype(np.float32) - 127.5) / 127.5
    nz = (normal_rgba[active, 2].astype(np.float32) - 127.5) / 127.5
    bg_normals = normal_rgba[~active]
    bg_neutral_ok = bool(
        len(bg_normals) == 0
        or np.all(bg_normals == np.array([128, 128, 255, 0], dtype=np.uint8))
    )

    return {
        "width": int(w),
        "height": int(h),
        "frame_heights": heights,
        "bottom_row_y": bottom_ys,
        "unique_colors": len(used_colors),
        "off_palette_count": off_palette,
        "inter_frame_diff_pct": inter_diffs,
        "min_inter_frame_diff_pct": min(inter_diffs) if inter_diffs else 0.0,
        "normal_xy_std": round(float(np.std(nx) + np.std(ny)) if len(nx) else 0.0, 4),
        "normal_min_z": round(float(nz.min()) if len(nz) else 1.0, 3),
        "normal_bg_neutral_ok": bg_neutral_ok,
    }


def _make_checkerboard_bg(w: int, h: int, cell: int = 16) -> np.ndarray:
    yy, xx = np.mgrid[0:h, 0:w]
    parity = ((yy // cell) + (xx // cell)) % 2
    bg = np.empty((h, w, 3), dtype=np.uint8)
    bg[parity == 0] = [34, 38, 48]
    bg[parity == 1] = [48, 52, 64]
    return bg


def _make_cobble_bg(w: int, h: int, cobble_path: Path = DEFAULT_COBBLE_PATH) -> np.ndarray:
    if cobble_path.exists():
        tile = np.array(Image.open(cobble_path).convert("RGB"), dtype=np.uint8)
        th, tw, _ = tile.shape
        reps_y = (h + th - 1) // th
        reps_x = (w + tw - 1) // tw
        return np.tile(tile, (reps_y, reps_x, 1))[:h, :w]
    # Neutral mid-grey stone fallback
    yy, xx = np.mgrid[0:h, 0:w]
    tile = np.full((h, w, 3), [132, 127, 120], dtype=np.uint8)
    mortar = ((yy % 32) == 0) | ((xx % 32) == 0)
    tile[mortar] = [92, 88, 84]
    return tile


def _composite_over_bg(fg_rgba: np.ndarray, bg_rgb: np.ndarray) -> np.ndarray:
    a = fg_rgba[..., 3:4].astype(np.float32) / 255.0
    comp = fg_rgba[..., :3].astype(np.float32) * a + bg_rgb.astype(np.float32) * (1.0 - a)
    return np.clip(np.round(comp), 0, 255).astype(np.uint8)


def build_hero_qa_contact_sheet(
    role: str,
    sheets: Dict[str, np.ndarray],
    normals: Dict[str, np.ndarray],
    out_qa_path: Path,
    scale: int = 4,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Path:
    """
    Build a 4x nearest-neighbor magnified QA contact sheet showing Idle, Walk, Attack
    over both dark checkerboard and tiled cobble_plaza.png, plus their normal maps.
    """
    scale = max(1, int(scale))
    strip_w = SHEET_W * scale
    strip_h = CANVAS_H * scale
    label_h = 28
    pad = 8

    checker = _make_checkerboard_bg(SHEET_W, CANVAS_H, cell=8)
    cobble_1x = _make_cobble_bg(SHEET_W, CANVAS_H, cobble_path=cobble_path)

    rows: List[Tuple[str, np.ndarray]] = []
    for key, title in (
        ("sheet", "IDLE"),
        ("walk_sheet", "WALK"),
        ("attack_sheet", "ATTACK"),
    ):
        comp_chk = _composite_over_bg(sheets[key], checker)
        comp_cob = _composite_over_bg(sheets[key], cobble_1x)
        rows.append((f"{role} | {title} (Dark Checkerboard 4x)", comp_chk))
        rows.append((f"{role} | {title} (Cobble Plaza 4x)", comp_cob))
    for key, title in (
        ("sheet", "IDLE NORMALS"),
        ("walk_sheet", "WALK NORMALS"),
        ("attack_sheet", "ATTACK NORMALS"),
    ):
        rows.append((f"{role} | {title} (OpenGL +Y 4x)", normals[key][..., :3]))

    total_h = len(rows) * (strip_h + label_h + pad) + pad
    total_w = strip_w + 2 * pad
    canvas = Image.new("RGB", (total_w, total_h), (20, 22, 30))
    draw = ImageDraw.Draw(canvas)

    y_cur = pad
    for label, img_1x in rows:
        draw.text((pad, y_cur + 6), label, fill=(235, 232, 220))
        y_cur += label_h
        up = Image.fromarray(img_1x, mode="RGB").resize((strip_w, strip_h), Image.Resampling.NEAREST)
        canvas.paste(up, (pad, y_cur))
        # Subtle frame divider lines at 128px boundaries
        for f in range(1, FRAME_COUNT):
            gx = pad + f * CANVAS_W * scale
            draw.line([(gx, y_cur), (gx, y_cur + strip_h - 1)], fill=(90, 96, 115), width=1)
        y_cur += strip_h + pad

    out_qa_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out_qa_path, format="PNG")
    return out_qa_path


def process_hero(
    role: str,
    input_path: Union[str, Path],
    out_dir: Union[str, Path],
    qa_dir: Union[str, Path],
    promote_dir: Optional[Union[str, Path]] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Dict[str, Any]:
    """Run full cutout, animation synthesis, normal generation, and QA for one hero."""
    norm_role = normalize_hero_role(role)
    out_root = Path(out_dir)
    qa_root = Path(qa_dir)
    out_root.mkdir(parents=True, exist_ok=True)
    qa_root.mkdir(parents=True, exist_ok=True)

    palette_rgb = load_palette(palette_path)
    master = build_clean_hero_master(input_path, palette_rgb=palette_rgb, palette_path=palette_path)
    sheets = synthesize_hero_animations(master, role=norm_role, palette_rgb=palette_rgb, palette_path=palette_path)
    normals = {
        key: generate_hero_sheet_normals(arr, laigter_exe=laigter_exe)
        for key, arr in sheets.items()
    }

    written_files: List[Path] = []
    sheet_stats: Dict[str, Dict[str, Any]] = {}
    for key in ("sheet", "walk_sheet", "attack_sheet"):
        albedo_path = out_root / f"{norm_role}_{key}.png"
        normal_path = out_root / f"{norm_role}_{key}_n.png"
        Image.fromarray(sheets[key], mode="RGBA").save(albedo_path, format="PNG")
        Image.fromarray(normals[key], mode="RGBA").save(normal_path, format="PNG")
        written_files.extend([albedo_path, normal_path])

        st = compute_sheet_qa_stats(sheets[key], normals[key], palette_rgb)
        sheet_stats[key] = st
        print(
            f"[QA] {norm_role}_{key}: heights={st['frame_heights']} "
            f"bottom_y={st['bottom_row_y']} colors={st['unique_colors']} "
            f"off_palette={st['off_palette_count']} diff_pct={st['inter_frame_diff_pct']} "
            f"normal_xy_std={st['normal_xy_std']} min_z={st['normal_min_z']}"
        )

    qa_path = qa_root / f"{norm_role}_qa.png"
    build_hero_qa_contact_sheet(norm_role, sheets, normals, qa_path, scale=4, cobble_path=cobble_path)

    promoted_files: List[str] = []
    if promote_dir is not None:
        prom_root = Path(promote_dir)
        prom_root.mkdir(parents=True, exist_ok=True)
        for src_p in written_files:
            dst_p = prom_root / src_p.name
            shutil.copy2(src_p, dst_p)
            promoted_files.append(str(dst_p))

    return {
        "role": norm_role,
        "input_path": str(input_path),
        "qa_contact_sheet": str(qa_path),
        "outputs": [str(p) for p in written_files],
        "promoted": promoted_files,
        "sheets": sheet_stats,
    }


def _parse_manifest_entries(manifest_data: Any, manifest_dir: Path) -> List[Tuple[str, Path]]:
    entries: List[Tuple[str, Path]] = []
    if isinstance(manifest_data, dict):
        if "heroes" in manifest_data and isinstance(manifest_data["heroes"], list):
            items: Iterable[Any] = manifest_data["heroes"]
        else:
            for k, v in manifest_data.items():
                if isinstance(v, str):
                    p = Path(v)
                    if not p.is_absolute():
                        p = (manifest_dir / p).resolve()
                    entries.append((str(k), p))
                elif isinstance(v, dict):
                    raw_in = v.get("input") or v.get("input_path") or v.get("path")
                    if raw_in:
                        p = Path(raw_in)
                        if not p.is_absolute():
                            p = (manifest_dir / p).resolve()
                        entries.append((str(v.get("role", k)), p))
            return entries
    elif isinstance(manifest_data, list):
        items = manifest_data
    else:
        raise ValueError("Unsupported manifest JSON structure.")

    for item in items:
        if not isinstance(item, dict):
            continue
        role = item.get("role") or item.get("unit_id") or item.get("id")
        raw_in = item.get("input") or item.get("input_path") or item.get("path")
        if role and raw_in:
            p = Path(raw_in)
            if not p.is_absolute():
                p = (manifest_dir / p).resolve()
            entries.append((str(role), p))
    return entries


def process_all_heroes(
    manifest_path: Union[str, Path],
    out_dir: Union[str, Path],
    qa_dir: Union[str, Path],
    promote_dir: Optional[Union[str, Path]] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Dict[str, Any]:
    """Process all heroes defined in a JSON manifest."""
    m_path = Path(manifest_path)
    data = json.loads(m_path.read_text(encoding="utf-8"))
    entries = _parse_manifest_entries(data, m_path.parent)
    if not entries:
        raise ValueError(f"No valid hero entries found in manifest: {m_path}")

    results: Dict[str, Any] = {}
    for role, in_p in entries:
        res = process_hero(
            role=role,
            input_path=in_p,
            out_dir=out_dir,
            qa_dir=qa_dir,
            promote_dir=promote_dir,
            palette_path=palette_path,
            laigter_exe=laigter_exe,
            cobble_path=cobble_path,
        )
        results[res["role"]] = res
    return results


def main(argv: Optional[List[str]] = None) -> None:
    parser = argparse.ArgumentParser(description="Realmz HD-2D Hero Redraw Pipeline")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--manifest", type=Path, help="JSON manifest mapping hero roles to raw PNG paths")
    mode.add_argument(
        "--single",
        nargs=2,
        metavar=("ROLE", "INPUT_PATH"),
        help="Process a single hero role and raw input image path",
    )
    parser.add_argument("--out-dir", type=Path, required=True, help="Output directory for 512x128 strips")
    parser.add_argument("--qa-dir", type=Path, default=None, help="Output directory for 4x QA contact sheets")
    parser.add_argument(
        "--promote-dir",
        type=Path,
        default=None,
        help="Optional Unity Cartography units directory to copy finished sheets into",
    )
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH, help="Master palette JSON path")
    parser.add_argument("--laigter-exe", type=Path, default=DEFAULT_LAIGTER_EXE, help="Path to laigter.exe")
    parser.add_argument("--cobble-path", type=Path, default=DEFAULT_COBBLE_PATH, help="Path to cobble_plaza.png")

    args = parser.parse_args(argv)
    qa_dir = args.qa_dir if args.qa_dir is not None else (args.out_dir / "qa")

    if args.manifest is not None:
        summary = process_all_heroes(
            args.manifest,
            out_dir=args.out_dir,
            qa_dir=qa_dir,
            promote_dir=args.promote_dir,
            palette_path=args.palette,
            laigter_exe=args.laigter_exe,
            cobble_path=args.cobble_path,
        )
    else:
        role, raw_in = args.single
        summary = process_hero(
            role=role,
            input_path=Path(raw_in),
            out_dir=args.out_dir,
            qa_dir=qa_dir,
            promote_dir=args.promote_dir,
            palette_path=args.palette,
            laigter_exe=args.laigter_exe,
            cobble_path=args.cobble_path,
        )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
