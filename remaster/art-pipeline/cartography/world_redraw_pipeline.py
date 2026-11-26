"""
Realmz HD-2D World Redraw Pipeline (Props, Surface Oil, Portraits, NPCs, Buildings):
  1. Static Props & Buildings (`process_static_asset`):
     - Exterior-only 1-bit background removal (`strip_ground_shadow=True`),
       optional enclosed studio white pocket punching (`mode="prop"` only;
       never on `mode="building"` so sunlit plaster and warm windows stay opaque).
     - PixelOE contrast-aware downscale to exact target Unity `(W, H)` dimensions
       with `pad_px=2, bottom_pivot=True`.
     - <4px dust removal, 1px interior pinhole fill, generalized dark `#11131F`
       ink contour guard (`_enforce_ink_outline_size(canvas, safe_pad=2)`),
       and sunlit palette quantization (`0` off-palette, `{0, 255}` alpha).
     - OpenGL `+Y` normal map blending 55% Laigter (Sobel fallback) with 45%
       cylindrical/dome + EDT bevel volume normal (`nz >= 0.35`, transparent
       pixels strictly `[128, 128, 255, 0]`).
  2. Procedural Animated Surface Oil (`build_surface_oil_sheet`):
     - 4-frame `512x128` RGBA dark tar/oil slick pool (`68-78%` frame coverage,
       zero interior holes, iridescent oil-sheen ribbons, drifting 1-2px
       liquid specular glints, palette-locked) + smooth liquid ripple `+Y` normal.
  3. 128x128 Framed Portraits (`process_portrait_128`):
     - Extracts outer 8px ornate gold riveted frame from reference portrait
       (or builds a gold-bordered fallback if missing), cuts out and PixelOE
       downscales bust into inner `112x112` opening with `bottom_pivot=True`,
       adds `#11131F` contour, composites over Bayer radial slate vignette,
       overlays gold frame, quantizes strictly (`alpha=255` across `128x128`),
       and generates OpenGL `+Y` normal map.
  4. Animated NPC Sheets (`process_npc_sheet`):
     - Reuses `build_clean_hero_master(..., target_height_range=(86, 92))`,
       `_synth_idle_frames`, and `generate_hero_sheet_normals` for `512x128`
       4-frame idle strips and `_n.png` normal maps.
  5. Wave / Manifest Runner & QA Contact Sheets (`--wave`, `--manifest`,
     `--out-dir`, `--qa-dir`, `--promote`).
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

from hero_redraw_pipeline import (  # noqa: E402
    CANVAS_H,
    CANVAS_W,
    DEFAULT_COBBLE_PATH,
    INK_RGB,
    _composite_over_bg,
    _fill_interior_pinholes,
    _load_rgba_input,
    _make_checkerboard_bg,
    _make_cobble_bg,
    _punch_enclosed_studio_white_pockets,
    _quantize_preserve_colors,
    _remove_small_dust,
    _synth_idle_frames,
    build_clean_hero_master,
    generate_hero_sheet_normals,
)
from pixel_postprocess import (  # noqa: E402
    CREATE_NO_WINDOW,
    DEFAULT_LAIGTER_EXE,
    DEFAULT_PALETTE_PATH,
    apply_sunlit_grade_and_quantize,
    generate_normal_map_laigter,
    generate_normal_map_sobel_fallback,
    load_palette,
    normalize_target_size,
    pixeloe_downscale_sprite,
    remove_background_1bit,
    rgb_to_lab_float,
)

UNITY_CART_ROOT = (
    Path(__file__).resolve().parents[2]
    / "RealmzUnity"
    / "Assets"
    / "Art"
    / "Cartography"
)
DEFAULT_PORTRAIT_FRAME_PATH = UNITY_CART_ROOT / "Portraits" / "portrait_knight.png"

_BAYER_4X4 = (
    np.array(
        [
            [0, 8, 2, 10],
            [12, 4, 14, 6],
            [3, 11, 1, 9],
            [15, 7, 13, 5],
        ],
        dtype=np.float32,
    )
    + 0.5
) / 16.0 - 0.5


def resolve_bywater_inn_size(
    cart_root: Path = UNITY_CART_ROOT,
    default_size: Tuple[int, int] = (320, 256),
) -> Tuple[int, int]:
    """Read target (W, H) dynamically from existing structures/bywater_inn.png if present."""
    inn_path = cart_root / "structures" / "bywater_inn.png"
    if inn_path.exists():
        with Image.open(inn_path) as im:
            w, h = im.size
            return (int(w), int(h))
    return default_size


ASSET_SPECS: Dict[str, Dict[str, Any]] = {
    # Wave: props (13 static props + procedural surface_oil)
    "prop_barrels_crates": {
        "wave": "props",
        "kind": "prop",
        "size": (128, 112),
        "subdir": "Props",
        "filename": "prop_barrels_crates.png",
    },
    "prop_wooden_wagon": {
        "wave": "props",
        "kind": "prop",
        "size": (192, 144),
        "subdir": "Props",
        "filename": "prop_wooden_wagon.png",
    },
    "cart_wreck": {
        "wave": "props",
        "kind": "prop",
        "size": (192, 128),
        "subdir": "Props",
        "filename": "cart_wreck.png",
    },
    "boulder_moss_a": {
        "wave": "props",
        "kind": "prop",
        "size": (96, 96),
        "subdir": "Props",
        "filename": "boulder_moss_a.png",
    },
    "boulder_moss_b": {
        "wave": "props",
        "kind": "prop",
        "size": (112, 96),
        "subdir": "Props",
        "filename": "boulder_moss_b.png",
    },
    "boulder_moss_c": {
        "wave": "props",
        "kind": "prop",
        "size": (80, 80),
        "subdir": "Props",
        "filename": "boulder_moss_c.png",
    },
    "rubble_pile_a": {
        "wave": "props",
        "kind": "prop",
        "size": (96, 80),
        "subdir": "Props",
        "filename": "rubble_pile_a.png",
    },
    "rubble_pile_b": {
        "wave": "props",
        "kind": "prop",
        "size": (112, 80),
        "subdir": "Props",
        "filename": "rubble_pile_b.png",
    },
    "palisade_stakes": {
        "wave": "props",
        "kind": "prop",
        "size": (128, 96),
        "subdir": "Props",
        "filename": "palisade_stakes.png",
    },
    "broken_archway": {
        "wave": "props",
        "kind": "prop",
        "size": (192, 160),
        "subdir": "Props",
        "filename": "broken_archway.png",
    },
    "ruined_wall_corner": {
        "wave": "props",
        "kind": "prop",
        "size": (128, 128),
        "subdir": "Props",
        "filename": "ruined_wall_corner.png",
    },
    "fallen_hollow_log": {
        "wave": "props",
        "kind": "prop",
        "size": (160, 80),
        "subdir": "Props",
        "filename": "fallen_hollow_log.png",
    },
    "town_notice_board": {
        "wave": "props",
        "kind": "prop",
        "size": (128, 144),
        "subdir": "Props",
        "filename": "town_notice_board.png",
    },
    "surface_oil": {
        "wave": "props",
        "kind": "surface_oil",
        "size": (512, 128),
        "subdir": "Surfaces",
        "filename": "surface_oil.png",
        "procedural": True,
    },
    # Wave: portraits_npcs (6 portraits + 4 NPC sheets)
    "portrait_knight": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_knight.png",
    },
    "portrait_paladin": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_paladin.png",
    },
    "portrait_cleric": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_cleric.png",
    },
    "portrait_rogue": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_rogue.png",
    },
    "portrait_ranger": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_ranger.png",
    },
    "portrait_mage": {
        "wave": "portraits_npcs",
        "kind": "portrait",
        "size": (128, 128),
        "subdir": "Portraits",
        "filename": "portrait_mage.png",
    },
    "npc_blacksmith": {
        "wave": "portraits_npcs",
        "kind": "npc",
        "size": (512, 128),
        "subdir": "units",
        "filename": "npc_blacksmith_sheet.png",
    },
    "npc_priestess": {
        "wave": "portraits_npcs",
        "kind": "npc",
        "size": (512, 128),
        "subdir": "units",
        "filename": "npc_priestess_sheet.png",
    },
    "npc_tanner": {
        "wave": "portraits_npcs",
        "kind": "npc",
        "size": (512, 128),
        "subdir": "units",
        "filename": "npc_tanner_sheet.png",
    },
    "npc_apothecary": {
        "wave": "portraits_npcs",
        "kind": "npc",
        "size": (512, 128),
        "subdir": "units",
        "filename": "npc_apothecary_sheet.png",
    },
    # Wave: buildings (7 Buildings/ + structures/bywater_inn)
    "building_apothecary": {
        "wave": "buildings",
        "kind": "building",
        "size": (288, 256),
        "subdir": "Buildings",
        "filename": "building_apothecary.png",
    },
    "building_blacksmith": {
        "wave": "buildings",
        "kind": "building",
        "size": (320, 256),
        "subdir": "Buildings",
        "filename": "building_blacksmith.png",
    },
    "building_cottage_b": {
        "wave": "buildings",
        "kind": "building",
        "size": (256, 256),
        "subdir": "Buildings",
        "filename": "building_cottage_b.png",
    },
    "building_cottage_c": {
        "wave": "buildings",
        "kind": "building",
        "size": (256, 256),
        "subdir": "Buildings",
        "filename": "building_cottage_c.png",
    },
    "building_guildhall": {
        "wave": "buildings",
        "kind": "building",
        "size": (384, 320),
        "subdir": "Buildings",
        "filename": "building_guildhall.png",
    },
    "building_temple": {
        "wave": "buildings",
        "kind": "building",
        "size": (320, 320),
        "subdir": "Buildings",
        "filename": "building_temple.png",
    },
    "building_watchtower": {
        "wave": "buildings",
        "kind": "building",
        "size": (256, 320),
        "subdir": "Buildings",
        "filename": "building_watchtower.png",
    },
    "bywater_inn": {
        "wave": "buildings",
        "kind": "building",
        "size": resolve_bywater_inn_size(),
        "subdir": "structures",
        "filename": "bywater_inn.png",
    },
}


def resolve_asset_key(raw_key: str) -> str:
    """Resolve filename or alias key to canonical ASSET_SPECS key."""
    key = Path(raw_key.strip()).stem.lower().replace("-", "_").replace(" ", "_")
    if key in ASSET_SPECS:
        return key
    if key.endswith("_sheet") and key[: -len("_sheet")] in ASSET_SPECS:
        return key[: -len("_sheet")]
    raise KeyError(f"Unknown world redraw asset key: {raw_key}")


def _verify_palette_and_1bit_alpha(rgba: np.ndarray, palette_rgb: np.ndarray, context: str) -> None:
    """Verify strict 1-bit {0, 255} alpha and 0 off-palette opaque pixels."""
    alpha_vals = {int(v) for v in np.unique(rgba[..., 3])}
    if not alpha_vals.issubset({0, 255}):
        raise ValueError(f"{context}: expected 1-bit {{0, 255}} alpha, found {sorted(alpha_vals)}")
    op = rgba[..., 3] == 255
    if not np.any(op):
        raise ValueError(f"{context}: produced zero opaque pixels")
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    used = {tuple(int(c) for c in row) for row in rgba[op, :3]}
    off = len(used - pal_set)
    if off != 0:
        raise ValueError(f"{context}: found {off} off-palette colors")


def _enforce_ink_outline_size(
    canvas: np.ndarray,
    safe_pad: int = 2,
    allow_bottom_expand: bool = False,
) -> np.ndarray:
    """
    Generalized dark #11131F ink outline guard for arbitrary (H, W, 4) canvases:
      - Adds a 1 px #11131F contour where transparent pixels inside
        x in [safe_pad .. W - safe_pad - 1], y in [safe_pad .. H - safe_pad - 1]
        are 4-adjacent to pale opaque pixels (L > 42.0), never dropping the
        bottom baseline past H - safe_pad - 1 (unless allow_bottom_expand=True).
      - Snaps remaining 1 px outer perimeter pixels with L > 28.0 to #11131F.
    """
    out = canvas.copy()
    h, w, _ = out.shape
    op = out[..., 3] >= 128
    if not np.any(op):
        return out

    ys_orig = np.where(op)[0]
    orig_bot = int(ys_orig.max())
    max_y_allowed = (h - safe_pad - 1) if allow_bottom_expand else min(orig_bot, h - safe_pad - 1)

    lab = rgb_to_lab_float(out[..., :3])
    L = lab[..., 0]
    pale_src = op & (L > 42.0)

    cross = np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], dtype=bool)
    pale_dil = ndimage.binary_dilation(pale_src, structure=cross)

    safe_box = np.zeros((h, w), dtype=bool)
    y0 = max(0, safe_pad)
    y1 = min(h, max_y_allowed + 1)
    x0 = max(0, safe_pad)
    x1 = min(w, w - safe_pad)
    if y1 > y0 and x1 > x0:
        safe_box[y0:y1, x0:x1] = True

    outer_ink = (~op) & pale_dil & safe_box
    if np.any(outer_ink):
        out[outer_ink, :3] = INK_RGB
        out[outer_ink, 3] = 255

    op2 = out[..., 3] >= 128
    eroded2 = ndimage.binary_erosion(op2, structure=cross, border_value=0)
    perim2 = op2 & (~eroded2)
    lab2 = rgb_to_lab_float(out[..., :3])
    L2 = lab2[..., 0]
    snap_mask = perim2 & (L2 > 28.0)
    if np.any(snap_mask):
        out[snap_mask, :3] = INK_RGB
        out[snap_mask, 3] = 255

    out[out[..., 3] < 128] = 0
    return out


def _compute_static_volumetric_normal(rgba: np.ndarray) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Compute smooth cylindrical/dome + EDT bevel volume normal for arbitrary (H, W, 4)."""
    h, w, _ = rgba.shape
    alpha = rgba[..., 3] > 0
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
            nx_cyl[y, xs_row] = 0.60 * u
        v_rel = float(y - y_top) / h_op
        if v_rel < 0.55:
            ny_val = 0.48 * math.cos((v_rel / 0.55) * (math.pi * 0.5))
        else:
            ny_val = -0.10 * ((v_rel - 0.55) / 0.45)
        ny_dome[y, xs_row] = float(ny_val)

    dist_in = ndimage.distance_transform_edt(alpha).astype(np.float32)
    t = np.clip(dist_in / 5.0, 0.0, 1.0)
    bevel_h = np.sqrt(np.maximum(0.0, 2.0 * t - t * t))
    gx_b = cv2.Sobel(bevel_h, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gy_b = cv2.Sobel(bevel_h, cv2.CV_32F, 0, 1, ksize=3) / 8.0

    vx = 0.66 * nx_cyl + 0.54 * (-gx_b)
    vy = 0.66 * ny_dome + 0.54 * gy_b
    vz = np.ones((h, w), dtype=np.float32)
    mag = np.sqrt(vx * vx + vy * vy + vz * vz) + 1e-8
    return vx / mag, vy / mag, vz / mag


def _generate_blended_static_normal(
    albedo_rgba: np.ndarray,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> np.ndarray:
    """
    Generate OpenGL +Y normal map blending 55% Laigter (Sobel fallback if None)
    with 45% cylindrical/dome + EDT bevel volume normal (nz >= 0.35, transparent
    pixels strictly [128, 128, 255, 0]).
    """
    h, w, _ = albedo_rgba.shape
    alpha = albedo_rgba[..., 3] > 0
    if not np.any(alpha):
        out = np.zeros((h, w, 4), dtype=np.uint8)
        out[..., :] = [128, 128, 255, 0]
        return out

    laigter_n = generate_normal_map_laigter(
        albedo_rgba,
        laigter_exe=laigter_exe,
        periodic=False,
        xy_scale=0.68,
        min_z=0.32,
    )
    if laigter_n is None:
        laigter_n = generate_normal_map_sobel_fallback(albedo_rgba, periodic=False)

    lx = (laigter_n[..., 0].astype(np.float32) - 127.5) / 127.5
    ly = (laigter_n[..., 1].astype(np.float32) - 127.5) / 127.5
    lz = (laigter_n[..., 2].astype(np.float32) - 127.5) / 127.5

    vx, vy, vz = _compute_static_volumetric_normal(albedo_rgba)

    bx = 0.55 * lx + 0.45 * vx
    by = 0.55 * ly + 0.45 * vy
    bz = 0.55 * lz + 0.45 * vz

    mag = np.sqrt(bx * bx + by * by + bz * bz) + 1e-8
    bx /= mag
    by /= mag
    bz /= mag

    # Clamp slightly above 0.35 so uint8 decoding stays strictly >= 0.35
    min_z = 0.355
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


def process_static_asset(
    src_path: Union[str, Path, np.ndarray],
    target_size: Tuple[int, int],
    mode: str,
    palette_rgb: Optional[np.ndarray] = None,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Process a static prop or building sprite to exact Unity (W, H) dimensions:
      - mode="prop": punches trapped enclosed pure-white studio pockets.
      - mode="building": never punches enclosed white pockets so sunlit plaster
        and bright warm windows remain 100% opaque.
    Returns (albedo_RGBA, normal_RGBA).
    """
    if mode not in ("prop", "building"):
        raise ValueError(f"Expected mode in ('prop', 'building'), got {mode!r}")
    if palette_rgb is None:
        palette_rgb = load_palette(DEFAULT_PALETTE_PATH)
    out_w, out_h = normalize_target_size(target_size)

    raw_rgba = _load_rgba_input(src_path)
    rgb_clean, fg_mask = remove_background_1bit(raw_rgba, strip_ground_shadow=True)
    if mode == "prop":
        fg_mask = _punch_enclosed_studio_white_pockets(rgb_clean, fg_mask)

    canvas = pixeloe_downscale_sprite(
        rgb_clean,
        fg_mask,
        target_size=(out_w, out_h),
        pad_px=2,
        bottom_pivot=True,
    )
    canvas = _remove_small_dust(canvas, min_component_px=4)
    canvas = _fill_interior_pinholes(canvas)
    canvas = _enforce_ink_outline_size(canvas, safe_pad=2)
    canvas = apply_sunlit_grade_and_quantize(
        canvas,
        palette_rgb,
        dither_strength=0.0,
        grade_strength=0.08,
        shadow_lift=2.5,
        vibrance_boost=1.05,
        preserve_alpha=False,
    )
    _verify_palette_and_1bit_alpha(canvas, palette_rgb, f"process_static_asset({mode})")
    normal_rgba = _generate_blended_static_normal(canvas, laigter_exe=laigter_exe)
    return canvas, normal_rgba


def build_surface_oil_sheet(
    palette_rgb: Optional[np.ndarray] = None,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Procedurally generate 4-frame 512x128 RGBA dark tar/oil slick pool + OpenGL +Y normal map:
      - Each 128x128 frame occupies ~68-78% area centered around (64, 64) with
        a continuous organic silhouette and zero interior Swiss-cheese holes.
      - Strictly uses deep obsidian/indigo (#11131F, #1C2033, #232B42),
        swirling iridescent oil-sheen ribbons (#2F3A59, #3E4C73, #267F82),
        and drifting 1-2 px liquid specular glints (#869BC9, #85E6DA).
    """
    if palette_rgb is None:
        palette_rgb = load_palette(DEFAULT_PALETTE_PATH)

    c_ink = np.array([17, 19, 31], dtype=np.uint8)      # #11131F
    c_deep = np.array([28, 32, 51], dtype=np.uint8)     # #1C2033
    c_body = np.array([35, 43, 66], dtype=np.uint8)     # #232B42
    c_sheen1 = np.array([47, 58, 89], dtype=np.uint8)   # #2F3A59
    c_sheen2 = np.array([62, 76, 115], dtype=np.uint8)  # #3E4C73
    c_teal = np.array([38, 127, 130], dtype=np.uint8)   # #267F82
    c_glint1 = np.array([134, 155, 201], dtype=np.uint8)  # #869BC9
    c_glint2 = np.array([133, 230, 218], dtype=np.uint8)  # #85E6DA

    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    cx, cy = 63.5, 63.5
    dx = xx - cx
    dy = yy - cy
    theta = np.arctan2(dy, dx)
    # Slightly wider ellipse aspect for natural ground pool (~72% coverage)
    rad_ell = np.sqrt((dx / 1.03) ** 2 + (dy / 0.99) ** 2)
    bayer_tile = np.tile(_BAYER_4X4, (CANVAS_H // 4, CANVAS_W // 4))

    albedo_frames: List[np.ndarray] = []
    normal_frames: List[np.ndarray] = []

    for f in range(4):
        phase = (2.0 * math.pi * f) / 4.0
        boundary_r = (
            60.5
            + 2.2 * np.cos(2.0 * theta + 0.4 + 0.35 * math.sin(phase))
            + 1.6 * np.sin(3.0 * theta - 0.7 + phase)
            + 1.1 * np.cos(5.0 * theta + 1.1 - phase)
            + 0.7 * np.sin(7.0 * theta + 0.3 + phase)
        )
        pool_mask = (rad_ell <= boundary_r) & (xx >= 1) & (xx <= 126) & (yy >= 1) & (yy <= 126)
        pool_mask = ndimage.binary_fill_holes(pool_mask)

        dist_in = ndimage.distance_transform_edt(pool_mask).astype(np.float32)
        norm_r = np.clip(rad_ell / np.maximum(1.0, boundary_r), 0.0, 1.0)

        # Swirling organic oil-sheen flow field
        warp_x = 0.075 * xx + 1.35 * np.sin(0.055 * yy + phase)
        warp_y = 0.085 * yy + 1.25 * np.cos(0.050 * xx - phase)
        swirl = np.sin(warp_x + warp_y) + 0.55 * np.cos(1.7 * warp_x - 1.3 * warp_y + 0.6 * phase)
        ripple = 0.5 + 0.5 * np.sin(0.16 * rad_ell - phase + 1.4 * swirl)

        frame = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
        frame[pool_mask, :3] = c_deep
        frame[pool_mask, 3] = 255

        # Depth shading + Bayer dithered dark indigo body
        body_val = (1.0 - norm_r * 0.75) + 0.25 * bayer_tile
        frame[pool_mask & (body_val > 0.55), :3] = c_body
        frame[pool_mask & (body_val < 0.28), :3] = c_ink

        # Iridescent swirling oil-sheen bands in interior (keep outer 3px dark)
        interior = pool_mask & (dist_in >= 3.0)
        sheen_val = ripple + 0.14 * bayer_tile
        frame[interior & (sheen_val > 0.54), :3] = c_sheen1
        frame[interior & (sheen_val > 0.68), :3] = c_sheen2
        teal_band = interior & (swirl > 0.55) & (sheen_val > 0.72)
        frame[teal_band, :3] = c_teal

        # Crisp 1 px obsidian rim around pool silhouette
        rim = pool_mask & (dist_in <= 1.5)
        frame[rim, :3] = c_ink

        # Drifting 1-2 px liquid specular glints on sheen crests
        glint_anchors = [
            (46.0 + 3.0 * math.cos(phase), 44.0 + 2.0 * math.sin(phase), False),
            (74.0 - 2.5 * math.sin(phase), 52.0 + 2.5 * math.cos(phase), True),
            (54.0 + 2.0 * math.sin(phase), 78.0 - 2.5 * math.cos(phase), False),
            (82.0 + 2.0 * math.cos(phase), 74.0 + 2.0 * math.sin(phase), True),
            (63.0 + 2.5 * math.cos(phase + 1.0), 61.0 + 2.0 * math.sin(phase + 1.0), True),
        ]
        for gx_f, gy_f, is_cyan in glint_anchors:
            gx_i = int(round(gx_f))
            gy_i = int(round(gy_f))
            col = c_glint2 if is_cyan else c_glint1
            if 2 <= gy_i < CANVAS_H - 2 and 2 <= gx_i < CANVAS_W - 2 and pool_mask[gy_i, gx_i]:
                frame[gy_i, gx_i, :3] = col
                if gx_i + 1 < CANVAS_W - 2 and pool_mask[gy_i, gx_i + 1]:
                    frame[gy_i, gx_i + 1, :3] = c_glint1 if is_cyan else c_sheen2

        frame = _quantize_preserve_colors(frame, palette_rgb)
        albedo_frames.append(frame)

        # Smooth liquid ripple OpenGL +Y normal map
        t_bev = np.clip(dist_in / 5.5, 0.0, 1.0)
        meniscus_h = np.sqrt(np.maximum(0.0, 2.0 * t_bev - t_bev * t_bev))
        wave_h = 0.28 * np.sin(warp_x + warp_y) + 0.16 * np.sin(0.18 * rad_ell - phase)
        height = np.where(pool_mask, 0.65 * meniscus_h + wave_h, 0.0).astype(np.float32)

        gx_n = cv2.Sobel(height, cv2.CV_32F, 1, 0, ksize=3) / 8.0
        gy_n = cv2.Sobel(height, cv2.CV_32F, 0, 1, ksize=3) / 8.0
        nx = -gx_n * 1.4
        ny = gy_n * 1.4
        nz = np.ones_like(nx, dtype=np.float32)
        mag = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
        nx /= mag
        ny /= mag
        nz /= mag

        n_frame = np.zeros((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
        n_frame[..., 0] = np.clip(np.round(nx * 127.5 + 127.5), 0, 255).astype(np.uint8)
        n_frame[..., 1] = np.clip(np.round(ny * 127.5 + 127.5), 0, 255).astype(np.uint8)
        n_frame[..., 2] = np.clip(np.round(nz * 127.5 + 127.5), 128, 255).astype(np.uint8)
        n_frame[..., 3] = 255
        n_frame[~pool_mask] = [128, 128, 255, 0]
        normal_frames.append(n_frame)

    albedo_sheet = np.concatenate(albedo_frames, axis=1)
    normal_sheet = np.concatenate(normal_frames, axis=1)
    _verify_palette_and_1bit_alpha(albedo_sheet, palette_rgb, "build_surface_oil_sheet")
    return albedo_sheet, normal_sheet


def _build_gold_frame_fallback(size: int = 128, border: int = 8) -> Tuple[np.ndarray, np.ndarray]:
    """Build a crisp 8px ornate gold riveted border frame + mask if reference is unavailable."""
    frame = np.zeros((size, size, 4), dtype=np.uint8)
    mask = np.zeros((size, size), dtype=bool)
    c_ink = np.array([17, 19, 31, 255], dtype=np.uint8)     # #11131F
    c_dark = np.array([89, 50, 18, 255], dtype=np.uint8)    # #593212
    c_mid = np.array([166, 100, 31, 255], dtype=np.uint8)   # #A6641F
    c_gold = np.array([236, 169, 56, 255], dtype=np.uint8)  # #ECA938
    c_hi = np.array([255, 243, 176, 255], dtype=np.uint8)   # #FFF3B0

    # Outer 8px rails
    mask[:border, :] = True
    mask[-border:, :] = True
    mask[:, :border] = True
    mask[:, -border:] = True

    frame[mask] = c_mid
    # Top & bottom beveled gold bars
    frame[0, :] = c_ink
    frame[1, 1:-1] = c_hi
    frame[2:5, 1:-1] = c_gold
    frame[5:7, 1:-1] = c_dark
    frame[7, :] = c_ink

    frame[size - 1, :] = c_ink
    frame[size - 2, 1:-1] = c_dark
    frame[size - 5 : size - 2, 1:-1] = c_gold
    frame[size - 7 : size - 5, 1:-1] = c_hi
    frame[size - 8, :] = c_ink

    # Left & right beveled gold bars
    frame[:, 0] = c_ink
    frame[1:-1, 1] = c_hi
    frame[1:-1, 2:5] = c_gold
    frame[1:-1, 5:7] = c_dark
    frame[:, 7] = c_ink

    frame[:, size - 1] = c_ink
    frame[1:-1, size - 2] = c_dark
    frame[1:-1, size - 5 : size - 2] = c_gold
    frame[1:-1, size - 7 : size - 5] = c_hi
    frame[:, size - 8] = c_ink

    # 11x11 corner rivet studs
    rivet_sz = 11
    for cy0, cx0 in (
        (0, 0),
        (0, size - rivet_sz),
        (size - rivet_sz, 0),
        (size - rivet_sz, size - rivet_sz),
    ):
        mask[cy0 : cy0 + rivet_sz, cx0 : cx0 + rivet_sz] = True
        frame[cy0 : cy0 + rivet_sz, cx0 : cx0 + rivet_sz] = c_ink
        frame[cy0 + 1 : cy0 + rivet_sz - 1, cx0 + 1 : cx0 + rivet_sz - 1] = c_gold
        frame[cy0 + 2 : cy0 + rivet_sz - 2, cx0 + 2 : cx0 + rivet_sz - 2] = c_mid
        frame[cy0 + 3 : cy0 + rivet_sz - 3, cx0 + 3 : cx0 + rivet_sz - 3] = c_hi

    return frame, mask


def _extract_portrait_frame(
    frame_ref_path: Optional[Union[str, Path]],
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Extract the outer 8 px ornate gold riveted frame (plus 11x11 corner rivets)
    from frame_ref_path, or build a gold-bordered fallback if missing.
    """
    ref_p: Optional[Path] = Path(frame_ref_path) if frame_ref_path is not None else DEFAULT_PORTRAIT_FRAME_PATH
    if ref_p is not None and not ref_p.exists():
        alt = UNITY_CART_ROOT / "Portraits" / ref_p.name
        if alt.exists():
            ref_p = alt
        else:
            ref_p = None

    if ref_p is None or not ref_p.exists():
        return _build_gold_frame_fallback(CANVAS_W, border=8)

    ref_rgba = np.array(Image.open(ref_p).convert("RGBA"), dtype=np.uint8)
    if ref_rgba.shape[:2] != (CANVAS_H, CANVAS_W):
        ref_rgba = np.array(
            Image.fromarray(ref_rgba, mode="RGBA").resize(
                (CANVAS_W, CANVAS_H), Image.Resampling.NEAREST
            ),
            dtype=np.uint8,
        )

    ring_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
    ring_mask[:8, :] = True
    ring_mask[-8:, :] = True
    ring_mask[:, :8] = True
    ring_mask[:, -8:] = True
    # Preserve 11x11 corner rivet blocks if present
    ring_mask[:11, :11] = True
    ring_mask[:11, -11:] = True
    ring_mask[-11:, :11] = True
    ring_mask[-11:, -11:] = True

    frame_mask = ring_mask & (ref_rgba[..., 3] >= 128)
    return ref_rgba, frame_mask


def _build_slate_bayer_vignette(palette_rgb: np.ndarray) -> np.ndarray:
    """Create a 128x128 clean dark midnight-slate Bayer radial vignette background."""
    yy, xx = np.mgrid[0:CANVAS_H, 0:CANVAS_W].astype(np.float32)
    dist = np.hypot(xx - 63.5, yy - 61.5) / 62.0
    bayer = np.tile(_BAYER_4X4, (CANVAS_H // 4, CANVAS_W // 4))
    val = np.clip(1.0 - dist + 0.16 * bayer, 0.0, 1.0)

    c_dark = np.array([28, 32, 51, 255], dtype=np.uint8)    # #1C2033
    c_mid = np.array([35, 43, 66, 255], dtype=np.uint8)     # #232B42
    c_light = np.array([47, 58, 89, 255], dtype=np.uint8)   # #2F3A59

    bg = np.empty((CANVAS_H, CANVAS_W, 4), dtype=np.uint8)
    bg[:] = c_dark
    bg[val >= 0.34] = c_mid
    bg[val >= 0.68] = c_light
    return _quantize_preserve_colors(bg, palette_rgb)


def process_portrait_128(
    src_path: Union[str, Path, np.ndarray],
    frame_ref_path: Optional[Union[str, Path]] = DEFAULT_PORTRAIT_FRAME_PATH,
    palette_rgb: Optional[np.ndarray] = None,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Process a 128x128 framed hero/NPC portrait:
      - Extracts outer 8px ornate gold riveted frame from frame_ref_path (or fallback).
      - Cuts out bust from white studio backdrop, PixelOE downscales into the
        inner 112x112 opening (x in [8..119], y in [8..119]) with bottom_pivot=True
        resting cleanly on the bottom border bar and ~4-8 px headroom under top bar.
      - Adds crisp #11131F contour around bust, composites over dark midnight-slate
        Bayer radial vignette, draws gold frame on top, quantizes strictly to
        palette_soc_sunlit_128.json (alpha=255 everywhere), and emits +Y normal map.
    """
    if palette_rgb is None:
        palette_rgb = load_palette(DEFAULT_PALETTE_PATH)

    raw_rgba = _load_rgba_input(src_path)
    rgb_clean, fg_mask = remove_background_1bit(raw_rgba, strip_ground_shadow=True)
    fg_mask = _punch_enclosed_studio_white_pockets(rgb_clean, fg_mask)

    # Fit inside 112x112 inner opening with 5px top headroom (max bust height ~106 px)
    # and horizontal padding so 1px dark contour stays strictly inside [8..119].
    ys_fg, xs_fg = np.where(fg_mask)
    if len(ys_fg) == 0:
        raise RuntimeError("Portrait background removal produced an empty foreground mask.")
    ch = int(ys_fg.max() - ys_fg.min() + 1)
    cw = int(xs_fg.max() - xs_fg.min() + 1)

    inner_size = 112
    max_bust_w = 106
    max_bust_h = 105  # Leaves ~6-7 px headroom below top frame bar (y=8) before 1px contour
    scale = min(max_bust_w / max(1, cw), max_bust_h / max(1, ch))
    fit_w = max(1, int(round(cw * scale)))
    fit_h = max(1, int(round(ch * scale)))

    sub = pixeloe_downscale_sprite(
        rgb_clean,
        fg_mask,
        target_size=(fit_w + 4, fit_h + 4),
        pad_px=2,
        bottom_pivot=True,
    )
    sub = _remove_small_dust(sub, min_component_px=4)
    sub = _fill_interior_pinholes(sub)

    sub_op = sub[..., 3] >= 128
    s_ys, s_xs = np.where(sub_op)
    if len(s_ys) == 0:
        raise RuntimeError("Portrait PixelOE downscale produced an empty bust.")
    crop_bust = sub[int(s_ys.min()) : int(s_ys.max()) + 1, int(s_xs.min()) : int(s_xs.max()) + 1]
    bh, bw, _ = crop_bust.shape

    inner_canvas = np.zeros((inner_size, inner_size, 4), dtype=np.uint8)
    ox = max(2, (inner_size - bw) // 2)
    oy = max(5, inner_size - bh)  # Rest cleanly at inner y=111 -> canvas y=119
    paste_h = min(bh, inner_size - oy)
    paste_w = min(bw, inner_size - ox)
    inner_canvas[oy : oy + paste_h, ox : ox + paste_w] = crop_bust[:paste_h, :paste_w]

    # Add crisp dark #11131F contour around bust inside inner 112x112 opening
    inner_canvas = _enforce_ink_outline_size(inner_canvas, safe_pad=1, allow_bottom_expand=True)
    inner_canvas = apply_sunlit_grade_and_quantize(
        inner_canvas,
        palette_rgb,
        dither_strength=0.0,
        grade_strength=0.08,
        shadow_lift=2.5,
        vibrance_boost=1.05,
        preserve_alpha=False,
    )

    canvas = _build_slate_bayer_vignette(palette_rgb)
    bust_mask = inner_canvas[..., 3] == 255
    inner_view = canvas[8:120, 8:120]
    inner_view[bust_mask] = inner_canvas[bust_mask]

    frame_rgba, frame_mask = _extract_portrait_frame(frame_ref_path)
    canvas[frame_mask] = frame_rgba[frame_mask]
    # Keep outer 128x128 fully opaque (alpha=255) even on rounded frame corners
    canvas[..., 3] = 255
    canvas = _quantize_preserve_colors(canvas, palette_rgb)
    _verify_palette_and_1bit_alpha(canvas, palette_rgb, "process_portrait_128")

    # Emit clean Laigter / Sobel OpenGL +Y normal map across full 128x128 portrait
    normal_rgba = generate_normal_map_laigter(
        canvas,
        laigter_exe=laigter_exe,
        periodic=False,
        xy_scale=0.68,
        min_z=0.355,
    )
    if normal_rgba is None:
        normal_rgba = generate_normal_map_sobel_fallback(canvas, periodic=False)
    normal_rgba[..., 3] = 255
    return canvas, normal_rgba


def process_npc_sheet(
    src_path: Union[str, Path, np.ndarray],
    role: str = "npc_blacksmith",
    palette_rgb: Optional[np.ndarray] = None,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Build a 512x128 4-frame animated NPC idle strip + OpenGL +Y normal map
    using build_clean_hero_master(..., target_height_range=(86, 92)),
    _synth_idle_frames, and generate_hero_sheet_normals.
    """
    _ = role
    if palette_rgb is None:
        palette_rgb = load_palette(DEFAULT_PALETTE_PATH)

    master = build_clean_hero_master(
        src_path,
        palette_rgb=palette_rgb,
        target_height_range=(86, 92),
    )
    idle_frames = _synth_idle_frames(master, palette_rgb)
    sheet_rgba = np.concatenate(idle_frames, axis=1)
    sheet_rgba = _quantize_preserve_colors(sheet_rgba, palette_rgb)
    _verify_palette_and_1bit_alpha(sheet_rgba, palette_rgb, f"process_npc_sheet({role})")
    normal_rgba = generate_hero_sheet_normals(sheet_rgba, laigter_exe=laigter_exe)
    return sheet_rgba, normal_rgba


def _build_sprite_qa_sheet(
    name: str,
    albedo_rgba: np.ndarray,
    normal_rgba: np.ndarray,
    out_qa_path: Path,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Path:
    """Build a zoomed QA sheet over dark checkerboard + cobble_plaza.png + OpenGL +Y normals."""
    h, w, _ = albedo_rgba.shape
    scale = 4 if w <= 192 else 2
    disp_w, disp_h = w * scale, h * scale
    label_h = 26
    pad = 8

    checker = _make_checkerboard_bg(w, h, cell=8)
    cobble = _make_cobble_bg(w, h, cobble_path=cobble_path)

    rows = [
        (f"{name} | Dark Checkerboard ({w}x{h} @ {scale}x)", _composite_over_bg(albedo_rgba, checker)),
        (f"{name} | Cobble Plaza ({w}x{h} @ {scale}x)", _composite_over_bg(albedo_rgba, cobble)),
        (f"{name} | OpenGL +Y Normal ({w}x{h} @ {scale}x)", normal_rgba[..., :3]),
    ]

    total_w = disp_w + 2 * pad
    total_h = len(rows) * (disp_h + label_h + pad) + pad
    canvas = Image.new("RGB", (total_w, total_h), (20, 22, 30))
    draw = ImageDraw.Draw(canvas)

    y_cur = pad
    for title, img_1x in rows:
        draw.text((pad, y_cur + 5), title, fill=(235, 232, 220))
        y_cur += label_h
        up = Image.fromarray(img_1x, mode="RGB").resize((disp_w, disp_h), Image.Resampling.NEAREST)
        canvas.paste(up, (pad, y_cur))
        y_cur += disp_h + pad

    out_qa_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out_qa_path, format="PNG")
    return out_qa_path


def _build_portrait_grid_qa_sheet(
    portraits: List[Tuple[str, np.ndarray, np.ndarray]],
    out_qa_path: Path,
    scale: int = 3,
) -> Path:
    """Build a zoomed grid QA contact sheet for portraits + their normal maps."""
    if not portraits:
        raise ValueError("Cannot build portrait QA sheet with zero portraits.")
    cell_sz = CANVAS_W * scale
    label_h = 24
    pad = 10
    cols = min(3, len(portraits))
    rows_count = (len(portraits) + cols - 1) // cols

    # Each cell displays Albedo on top and Normal below
    slot_w = cell_sz
    slot_h = label_h + cell_sz * 2 + pad
    total_w = cols * (slot_w + pad) + pad
    total_h = rows_count * (slot_h + pad) + pad
    canvas = Image.new("RGB", (total_w, total_h), (20, 22, 30))
    draw = ImageDraw.Draw(canvas)

    for idx, (name, alb, nrm) in enumerate(portraits):
        r = idx // cols
        c = idx % cols
        x0 = pad + c * (slot_w + pad)
        y0 = pad + r * (slot_h + pad)
        draw.text((x0, y0 + 4), f"{name} (128x128 @ {scale}x)", fill=(235, 232, 220))
        alb_up = Image.fromarray(alb[..., :3], mode="RGB").resize((cell_sz, cell_sz), Image.Resampling.NEAREST)
        nrm_up = Image.fromarray(nrm[..., :3], mode="RGB").resize((cell_sz, cell_sz), Image.Resampling.NEAREST)
        canvas.paste(alb_up, (x0, y0 + label_h))
        canvas.paste(nrm_up, (x0, y0 + label_h + cell_sz + 4))

    out_qa_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out_qa_path, format="PNG")
    return out_qa_path


def _parse_manifest_entries(manifest_data: Any, manifest_dir: Path) -> Dict[str, Path]:
    """Parse JSON manifest into {canonical_key: Path}."""
    entries: Dict[str, Path] = {}
    if isinstance(manifest_data, dict):
        for container_key in ("assets", "items", "props", "buildings", "portraits", "npcs"):
            if container_key in manifest_data and isinstance(manifest_data[container_key], list):
                for item in manifest_data[container_key]:
                    if isinstance(item, dict):
                        raw_k = item.get("id") or item.get("key") or item.get("name") or item.get("role")
                        raw_in = item.get("input") or item.get("input_path") or item.get("path")
                        if raw_k and raw_in:
                            p = Path(raw_in)
                            if not p.is_absolute():
                                p = (manifest_dir / p).resolve()
                            entries[resolve_asset_key(str(raw_k))] = p
                return entries

        for k, v in manifest_data.items():
            if isinstance(v, str):
                p = Path(v)
                if not p.is_absolute():
                    p = (manifest_dir / p).resolve()
                entries[resolve_asset_key(str(k))] = p
            elif isinstance(v, dict):
                raw_in = v.get("input") or v.get("input_path") or v.get("path")
                if raw_in:
                    p = Path(raw_in)
                    if not p.is_absolute():
                        p = (manifest_dir / p).resolve()
                    raw_k = str(v.get("id") or v.get("key") or v.get("name") or v.get("role") or k)
                    entries[resolve_asset_key(raw_k)] = p
        return entries

    if isinstance(manifest_data, list):
        for item in manifest_data:
            if not isinstance(item, dict):
                continue
            raw_k = item.get("id") or item.get("key") or item.get("name") or item.get("role")
            raw_in = item.get("input") or item.get("input_path") or item.get("path")
            if raw_k and raw_in:
                p = Path(raw_in)
                if not p.is_absolute():
                    p = (manifest_dir / p).resolve()
                entries[resolve_asset_key(str(raw_k))] = p
        return entries

    raise ValueError("Unsupported manifest JSON structure.")


def _normal_filename_for(albedo_filename: str) -> str:
    stem = Path(albedo_filename).stem
    return f"{stem}_n.png"


def run_wave(
    wave: str,
    manifest_path: Optional[Union[str, Path]],
    out_dir: Union[str, Path],
    qa_dir: Union[str, Path],
    promote: bool = False,
    promote_root: Path = UNITY_CART_ROOT,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
    frame_ref_path: Optional[Union[str, Path]] = DEFAULT_PORTRAIT_FRAME_PATH,
) -> Dict[str, Any]:
    """
    Process a wave ('props', 'portraits_npcs', 'buildings', or 'all') from a manifest,
    generating staged PNGs, zoomed QA contact sheets, and optionally promoting to
    exact Cartography destination subfolders.
    """
    if wave not in ("props", "portraits_npcs", "buildings", "all"):
        raise ValueError(f"Unknown wave {wave!r}; expected props, portraits_npcs, buildings, or all")

    out_root = Path(out_dir)
    qa_root = Path(qa_dir)
    out_root.mkdir(parents=True, exist_ok=True)
    qa_root.mkdir(parents=True, exist_ok=True)
    palette_rgb = load_palette(palette_path)

    manifest_entries: Dict[str, Path] = {}
    if manifest_path is not None:
        m_path = Path(manifest_path)
        data = json.loads(m_path.read_text(encoding="utf-8"))
        manifest_entries = _parse_manifest_entries(data, m_path.parent)

    results: Dict[str, Any] = {}
    portrait_qa_items: List[Tuple[str, np.ndarray, np.ndarray]] = []

    for key, spec in ASSET_SPECS.items():
        if wave != "all" and spec["wave"] != wave:
            continue

        kind = spec["kind"]
        is_proc = bool(spec.get("procedural", False))
        if not is_proc and key not in manifest_entries:
            continue

        if kind == "surface_oil":
            albedo, normal = build_surface_oil_sheet(palette_rgb=palette_rgb)
        elif kind in ("prop", "building"):
            target_sz = (
                resolve_bywater_inn_size(promote_root)
                if key == "bywater_inn"
                else tuple(spec["size"])
            )
            albedo, normal = process_static_asset(
                manifest_entries[key],
                target_size=target_sz,
                mode=kind,
                palette_rgb=palette_rgb,
                laigter_exe=laigter_exe,
            )
        elif kind == "portrait":
            albedo, normal = process_portrait_128(
                manifest_entries[key],
                frame_ref_path=frame_ref_path,
                palette_rgb=palette_rgb,
                laigter_exe=laigter_exe,
            )
            portrait_qa_items.append((key, albedo, normal))
        elif kind == "npc":
            albedo, normal = process_npc_sheet(
                manifest_entries[key],
                role=key,
                palette_rgb=palette_rgb,
                laigter_exe=laigter_exe,
            )
        else:
            raise ValueError(f"Unsupported spec kind {kind!r} for {key}")

        albedo_name = spec["filename"]
        normal_name = _normal_filename_for(albedo_name)
        staged_sub = out_root / spec["subdir"]
        staged_sub.mkdir(parents=True, exist_ok=True)
        albedo_out = staged_sub / albedo_name
        normal_out = staged_sub / normal_name
        Image.fromarray(albedo, mode="RGBA").save(albedo_out, format="PNG")
        Image.fromarray(normal, mode="RGBA").save(normal_out, format="PNG")

        qa_files: List[str] = []
        if kind != "portrait":
            qa_path = _build_sprite_qa_sheet(
                key,
                albedo,
                normal,
                qa_root / f"{key}_qa.png",
                cobble_path=cobble_path,
            )
            qa_files.append(str(qa_path))

        promoted_files: List[str] = []
        if promote:
            dst_sub = promote_root / spec["subdir"]
            dst_sub.mkdir(parents=True, exist_ok=True)
            dst_albedo = dst_sub / albedo_name
            dst_normal = dst_sub / normal_name
            shutil.copy2(albedo_out, dst_albedo)
            shutil.copy2(normal_out, dst_normal)
            promoted_files.extend([str(dst_albedo), str(dst_normal)])

        results[key] = {
            "wave": spec["wave"],
            "kind": kind,
            "size": [int(albedo.shape[1]), int(albedo.shape[0])],
            "outputs": [str(albedo_out), str(normal_out)],
            "qa_sheets": qa_files,
            "promoted": promoted_files,
        }

    if portrait_qa_items:
        grid_path = _build_portrait_grid_qa_sheet(portrait_qa_items, qa_root / "portraits_qa.png")
        for key, _, _ in portrait_qa_items:
            results[key]["qa_sheets"].append(str(grid_path))

    if not results:
        raise ValueError(f"No matching assets processed for wave={wave!r}")
    return results


def main(argv: Optional[List[str]] = None) -> None:
    parser = argparse.ArgumentParser(description="Realmz HD-2D World Redraw Pipeline")
    parser.add_argument(
        "--wave",
        choices=("props", "portraits_npcs", "buildings", "all"),
        default="all",
        help="Asset wave to process",
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=None,
        help="JSON manifest mapping asset keys to raw PNG paths",
    )
    parser.add_argument("--out-dir", type=Path, required=True, help="Staging output directory")
    parser.add_argument("--qa-dir", type=Path, default=None, help="Output directory for QA contact sheets")
    parser.add_argument(
        "--promote",
        action="store_true",
        help="Copy finished PNGs directly into destination subfolders under Cartography root",
    )
    parser.add_argument(
        "--promote-root",
        type=Path,
        default=UNITY_CART_ROOT,
        help="Target Unity Cartography root directory for --promote",
    )
    parser.add_argument("--palette", type=Path, default=DEFAULT_PALETTE_PATH, help="Master palette JSON path")
    parser.add_argument("--laigter-exe", type=Path, default=DEFAULT_LAIGTER_EXE, help="Path to laigter.exe")
    parser.add_argument("--cobble-path", type=Path, default=DEFAULT_COBBLE_PATH, help="Path to cobble_plaza.png")
    parser.add_argument(
        "--frame-ref",
        type=Path,
        default=DEFAULT_PORTRAIT_FRAME_PATH,
        help="Reference 128x128 portrait for 8px gold frame extraction",
    )

    args = parser.parse_args(argv)
    qa_dir = args.qa_dir if args.qa_dir is not None else (args.out_dir / "qa")

    summary = run_wave(
        wave=args.wave,
        manifest_path=args.manifest,
        out_dir=args.out_dir,
        qa_dir=qa_dir,
        promote=bool(args.promote),
        promote_root=args.promote_root,
        palette_path=args.palette,
        laigter_exe=args.laigter_exe,
        cobble_path=args.cobble_path,
        frame_ref_path=args.frame_ref,
    )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
