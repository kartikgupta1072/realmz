"""
Realmz HD-2D Monster Redraw Pipeline:
  1. Master Cutout & Framing (128x128 RGBA, BottomCenter pivot at y=125,
     per-species height_range and max_fit_w bounds, optional horizontal flip,
     pinhole fill, <4px dust removal, cobblestone ink outline guard, sunlit
     palette quantization).
  2. Left-Facing 4-Frame Monster Animation Synthesis (512x128 RGBA strips for
     each monster's supported states with strict per-frame 128x128 isolation
     and palette lock):
       - Idle ('sheet'): humanoid 22% leg anchor or amphibian/quadruped 28% paw
         anchor, 1px breath with seamless waist fill, accent highlight pulse.
       - Walk ('walk_sheet'): leftward (-X) lean; amphibian 4-phase hop cycle
         [0, -3, 0, -1] or humanoid/quadruped 4-phase stride [0, -2, 0, -1].
       - Attack ('attack_sheet'): leftward (-X) strike with +3px right coil
         anticipation, -5px leftward lunge + species pixel VFX, follow-through,
         and recovery.
  3. Per-frame Volumetric + Laigter OpenGL (+Y) Normal Maps via
     generate_hero_sheet_normals.
  4. 4x Nearest-Neighbor QA Contact Sheets (supporting both 3-state and
     1-state monsters cleanly) and CLI (--manifest / --single / --out-dir /
     --qa-dir / --promote-dir).
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple, Union

import numpy as np
from PIL import Image, ImageDraw

_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from hero_redraw_pipeline import (  # noqa: E402
    CANVAS_H,
    CANVAS_W,
    DEFAULT_COBBLE_PATH,
    FRAME_COUNT,
    GROUND_Y,
    SAFE_MAX,
    SAFE_MIN,
    SHEET_W,
    _anchor_bottom_and_center,
    _apply_accent_glow_pulse,
    _apply_upper_breeze_sway,
    _composite_over_bg,
    _fill_interior_pinholes,
    _load_rgba_input,
    _make_checkerboard_bg,
    _make_cobble_bg,
    _punch_enclosed_studio_white_pockets,
    _quantize_preserve_colors,
    _remove_small_dust,
    _shift_mask_pixels,
    build_hero_qa_contact_sheet,
    compute_sheet_qa_stats,
    enforce_cobblestone_ink_outline,
    generate_hero_sheet_normals,
)
from pixel_postprocess import (  # noqa: E402
    CREATE_NO_WINDOW,
    DEFAULT_LAIGTER_EXE,
    DEFAULT_PALETTE_PATH,
    apply_sunlit_grade_and_quantize,
    load_palette,
    pixeloe_downscale_sprite,
    remove_background_1bit,
)

MONSTER_SPECS: Dict[str, Dict[str, Any]] = {
    "monster_giant_frog": {
        "height_range": (64, 72),
        "max_fit_w": 98,
        "body_type": "amphibian",
        "vfx": "tongue_lash",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_giant_rat": {
        "height_range": (58, 66),
        "max_fit_w": 96,
        "body_type": "quadruped",
        "vfx": "claw_bite",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_goblin_raider": {
        "height_range": (70, 76),
        "max_fit_w": 92,
        "body_type": "humanoid",
        "vfx": "jagged_slash",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_goblin_archer": {
        "height_range": (70, 76),
        "max_fit_w": 92,
        "body_type": "humanoid",
        "vfx": "arrow_left",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_orc_warrior": {
        "height_range": (96, 102),
        "max_fit_w": 104,
        "body_type": "humanoid",
        "vfx": "heavy_cleave",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_skeleton_warrior": {
        "height_range": (88, 94),
        "max_fit_w": 96,
        "body_type": "humanoid",
        "vfx": "spectral_slash",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_bandit_cutthroat": {
        "height_range": (88, 94),
        "max_fit_w": 96,
        "body_type": "humanoid",
        "vfx": "jagged_slash",
        "states": ("sheet", "walk_sheet", "attack_sheet"),
    },
    "monster_zombie": {
        "height_range": (88, 94),
        "max_fit_w": 96,
        "body_type": "humanoid",
        "vfx": "claw_bite",
        "states": ("sheet",),
    },
}


def normalize_monster_role(role: str) -> str:
    """Normalize role name to canonical 'monster_<short>' identifier."""
    cleaned = role.strip().lower().replace("-", "_").replace(" ", "_")
    if cleaned.startswith("monster_"):
        return cleaned
    return f"monster_{cleaned}"


def get_monster_spec(role: str) -> Dict[str, Any]:
    """Return MONSTER_SPECS entry for normalized monster role."""
    norm = normalize_monster_role(role)
    if norm not in MONSTER_SPECS:
        valid = ", ".join(sorted(MONSTER_SPECS.keys()))
        raise ValueError(f"Unknown monster role '{role}' (normalized '{norm}'). Valid: {valid}")
    return MONSTER_SPECS[norm]


def build_clean_monster_master(
    source: Union[str, Path, np.ndarray],
    role: str = "monster_orc_warrior",
    palette_rgb: Optional[np.ndarray] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    flip_horizontal: bool = False,
    pad_px: int = 2,
) -> np.ndarray:
    """
    Build a clean 128x128 RGBA BottomCenter monster master sprite at y = 125:
      - Optionally flips raw RGBA horizontally so right-facing inputs face left.
      - Removes studio backdrop and ground shadow with 1-bit keying + enclosed
        white pocket punching.
      - Uniformly downscales via pixeloe_downscale_sprite respecting both
        spec['height_range'] and spec['max_fit_w'] without clipping [2..125].
      - Cleans <4px dust, fills 1px pinholes, anchors BottomCenter at y=125,
        enforces dark cobblestone ink outline, grades and quantizes to palette.
    """
    spec = get_monster_spec(role)
    if palette_rgb is None:
        palette_rgb = load_palette(palette_path)

    raw_rgba = _load_rgba_input(source)
    if flip_horizontal:
        raw_rgba = np.ascontiguousarray(raw_rgba[:, ::-1, :])

    rgb_clean, fg_mask = remove_background_1bit(raw_rgba, strip_ground_shadow=True)
    fg_mask = _punch_enclosed_studio_white_pockets(rgb_clean, fg_mask)

    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError(f"Monster background removal produced an empty foreground mask for {role}.")
    ch = int(ys.max() - ys.min() + 1)
    cw = int(xs.max() - xs.min() + 1)

    lo_h, hi_h = int(spec["height_range"][0]), int(spec["height_range"][1])
    spec_max_w = int(spec["max_fit_w"])

    desired_h = max(lo_h, min(hi_h, (lo_h + hi_h) // 2 - 1))
    # Reserve 2 px for optional 1 px outline expansion on left/right and 1 px on top
    max_fit_w = min(max(4, spec_max_w - 2), CANVAS_W - 2 * (pad_px + 1))
    max_fit_h = min(hi_h - 1, CANVAS_H - 2 * (pad_px + 1))

    scale_h = desired_h / float(max(1, ch))
    scale_w = max_fit_w / float(max(1, cw))
    scale_cap_h = float(hi_h) / float(max(1, ch))
    scale = min(scale_h, scale_w, scale_cap_h)

    fit_h = max(1, min(max_fit_h, int(round(ch * scale))))
    fit_w = max(1, min(max_fit_w, int(round(cw * scale))))

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
        raise RuntimeError(f"PixelOE downscale produced an empty monster sprite for {role}.")

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

    alpha_vals = set(int(v) for v in np.unique(canvas[..., 3]))
    if alpha_vals != {0, 255}:
        raise RuntimeError(f"Expected 1-bit {{0, 255}} alpha in monster master, got {alpha_vals}")
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}
    op = canvas[..., 3] == 255
    used_colors = {tuple(int(c) for c in row) for row in canvas[op, :3]}
    off_pal = used_colors - pal_set
    if off_pal:
        raise RuntimeError(f"Monster master for {role} has {len(off_pal)} off-palette colors")

    return canvas


def _synth_monster_idle_frames(
    master: np.ndarray,
    body_type: str,
    palette_rgb: np.ndarray,
) -> List[np.ndarray]:
    """
    Synthesize 4-frame Idle animation:
      - humanoid: bottom 22% legs anchored at y=125, 1px upper breath, breeze sway, accent pulse.
      - amphibian / quadruped: bottom 28% paws/feet anchored at y=125, 1px upper body/throat
        breath with seamless middle fill and throat/eye highlight pulse.
    """
    ys = np.where(master[..., 3] >= 128)[0]
    y_top, y_bot = int(ys.min()), int(ys.max())
    h_op = y_bot - y_top + 1
    anchor_ratio = 0.28 if body_type in ("amphibian", "quadruped") else 0.22
    leg_h = max(6, int(round(h_op * anchor_ratio)))
    y_leg_start = y_bot - leg_h + 1

    breath_dys = [0, -1, -1, 0]
    sway_dxs = [0, 1, 0, -1] if body_type == "humanoid" else [0, 0, -1, 0]
    glow_deltas = [0.0, 8.0, 15.0, 4.0]

    frames: List[np.ndarray] = []
    for f in range(FRAME_COUNT):
        dy = breath_dys[f]
        if dy == 0:
            frame = master.copy()
        else:
            frame = np.zeros_like(master)
            # Anchor lower legs/paws strictly to ground
            leg_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            leg_mask[y_leg_start:, :] = True
            _shift_mask_pixels(master, leg_mask, 0, 0, frame)

            # Seamless middle/waist row fill before lifting upper torso/head/throat
            waist_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            waist_mask[max(SAFE_MIN, y_leg_start - 1) : y_leg_start, :] = True
            _shift_mask_pixels(master, waist_mask, 0, 0, frame)

            # Upper body/head/throat breath shift
            upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
            upper_mask[:y_leg_start, :] = True
            _shift_mask_pixels(master, upper_mask, dy, 0, frame)

        frame = _apply_upper_breeze_sway(frame, y_top + dy, h_op, sway_dxs[f])
        frame = _apply_accent_glow_pulse(frame, glow_deltas[f], palette_rgb)
        frame = _fill_interior_pinholes(frame)
        frame = _quantize_preserve_colors(frame, palette_rgb)
        frames.append(frame)
    return frames


def _synth_monster_walk_frames(
    master: np.ndarray,
    body_type: str,
    palette_rgb: np.ndarray,
) -> List[np.ndarray]:
    """
    Synthesize left-facing (-X lean) 4-phase Walk / Hop loop:
      - amphibian: 4-phase hop cycle [0, -3, 0, -1] bounce dy, leftward -2px reach
        on hop frame 1, grounded landing at y=125 on contact frames 0 & 2.
      - quadruped / humanoid: 4-phase stride [0, -2, 0, -1] bounce dy, y=125 on
        contact frames 0 & 2 and y=123-124 on passing frames, alternating leg shear
        [-2, 0, +2, 0], leftward lean [-1, -1, -1, 0], subtle arm/shoulder/tail sway.
    """
    ys, xs = np.where(master[..., 3] >= 128)
    y_top, y_bot = int(ys.min()), int(ys.max())
    h_op = y_bot - y_top + 1

    if body_type == "amphibian":
        leg_h = max(6, int(round(h_op * 0.28)))
        y_leg_start = y_bot - leg_h + 1
        leg_xs = xs[ys >= y_leg_start]
        x_mid = int(round(float(np.mean(leg_xs)))) if len(leg_xs) else int(round(float(np.mean(xs))))

        # [0: crouch push-off (y=125), 1: airborne hop (y=122, -2px left),
        #  2: landing squash (y=125), 3: settle rebound (y=124)]
        frames: List[np.ndarray] = []
        for f in range(FRAME_COUNT):
            frame = np.zeros_like(master)
            if f == 0:
                left_paw = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                right_paw = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                left_paw[y_leg_start:, :x_mid] = True
                right_paw[y_leg_start:, x_mid:] = True
                hip_rows = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                hip_rows[y_leg_start : min(y_bot + 1, y_leg_start + 3), :] = True
                _shift_mask_pixels(master, left_paw & (~hip_rows), 0, -1, frame)
                _shift_mask_pixels(master, right_paw & (~hip_rows), 0, 1, frame)
                _shift_mask_pixels(master, hip_rows, 0, 0, frame)

                upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                upper_mask[:y_leg_start, :] = True
                _shift_mask_pixels(master, upper_mask, 0, -1, frame)
            elif f == 1:
                full_mask = np.ones((CANVAS_H, CANVAS_W), dtype=bool)
                _shift_mask_pixels(master, full_mask, -3, -2, frame)
                frame = _apply_accent_glow_pulse(frame, 6.0, palette_rgb)
            elif f == 2:
                left_paw = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                right_paw = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                left_paw[y_leg_start:, :x_mid] = True
                right_paw[y_leg_start:, x_mid:] = True
                hip_rows = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                hip_rows[y_leg_start : min(y_bot + 1, y_leg_start + 3), :] = True
                _shift_mask_pixels(master, left_paw & (~hip_rows), 0, 1, frame)
                _shift_mask_pixels(master, right_paw & (~hip_rows), 0, -1, frame)
                _shift_mask_pixels(master, hip_rows, 0, 0, frame)

                upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
                upper_mask[:y_leg_start, :] = True
                _shift_mask_pixels(master, upper_mask, 1, -1, frame)
            else:
                full_mask = np.ones((CANVAS_H, CANVAS_W), dtype=bool)
                _shift_mask_pixels(master, full_mask, -1, -1, frame)
                frame = _apply_upper_breeze_sway(frame, max(SAFE_MIN, y_top - 1), h_op, 1)

            frame = _fill_interior_pinholes(frame)
            frame = _quantize_preserve_colors(frame, palette_rgb)
            frames.append(frame)
        return frames

    # Humanoid / quadruped 4-phase walk stride with leftward (-X) lean
    leg_ratio = 0.28 if body_type == "quadruped" else 0.22
    leg_h = max(6, int(round(h_op * leg_ratio)))
    y_leg_start = y_bot - leg_h + 1
    leg_xs = xs[ys >= y_leg_start]
    x_mid = int(round(float(np.mean(leg_xs)))) if len(leg_xs) else int(round(float(np.mean(xs))))

    bounce_dys = [0, -2, 0, -1]
    leg_shears = [(-2, 2), (0, 0), (2, -2), (0, 0)]
    torso_leans = [-1, -1, -1, 0]
    arm_sways = [1, 0, -1, 0]

    y_arm_top = y_top + max(4, int(round(h_op * 0.25)))
    y_arm_bot = y_leg_start

    frames = []
    for f in range(FRAME_COUNT):
        dy = bounce_dys[f]
        l_dx, r_dx = leg_shears[f]
        lean_dx = torso_leans[f]
        arm_dx = arm_sways[f]

        frame = np.zeros_like(master)

        left_leg = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        right_leg = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        left_leg[y_leg_start:, :x_mid] = True
        right_leg[y_leg_start:, x_mid:] = True

        hip_rows = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        hip_rows[y_leg_start : min(y_bot + 1, y_leg_start + 3), :] = True

        l_hip_dx = int(round(l_dx * 0.5))
        r_hip_dx = int(round(r_dx * 0.5))

        if l_dx <= r_dx:
            _shift_mask_pixels(master, right_leg & (~hip_rows), dy, r_dx, frame)
            _shift_mask_pixels(master, right_leg & hip_rows, dy, r_hip_dx, frame)
            _shift_mask_pixels(master, left_leg & (~hip_rows), dy, l_dx, frame)
            _shift_mask_pixels(master, left_leg & hip_rows, dy, l_hip_dx, frame)
        else:
            _shift_mask_pixels(master, left_leg & (~hip_rows), dy, l_dx, frame)
            _shift_mask_pixels(master, left_leg & hip_rows, dy, l_hip_dx, frame)
            _shift_mask_pixels(master, right_leg & (~hip_rows), dy, r_dx, frame)
            _shift_mask_pixels(master, right_leg & hip_rows, dy, r_hip_dx, frame)

        upper_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        upper_mask[:y_leg_start, :] = True
        _shift_mask_pixels(master, upper_mask, dy, lean_dx, frame)

        waist_bridge = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
        waist_bridge[max(SAFE_MIN, y_leg_start - 1) : min(CANVAS_H, y_leg_start + 1), :] = True
        _shift_mask_pixels(master, waist_bridge, dy, int(round(lean_dx * 0.5)), frame)

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

        frame = _fill_interior_pinholes(frame)
        frame = _quantize_preserve_colors(frame, palette_rgb)
        frames.append(frame)
    return frames


def _draw_monster_pixel_vfx(
    frame: np.ndarray,
    vfx_style: str,
    x_front: int,
    y_mid: int,
    phase: str,
    palette_rgb: np.ndarray,
) -> np.ndarray:
    """
    Draw crisp leftward-extending (-X) pixel-art monster combat VFX anchored on
    the left edge x_front strictly inside [2..125, 2..125] and quantized to
    palette_soc_sunlit_128.json.
    """
    out = frame.copy()

    if vfx_style == "tongue_lash":
        c_core = np.array([255, 204, 194, 255], dtype=np.uint8)  # #FFCCC2 wet highlight
        c_mid = np.array([232, 86, 156, 255], dtype=np.uint8)    # #E8569C bright pink
        c_rim = np.array([153, 40, 56, 255], dtype=np.uint8)     # #992838 crimson underside
        c_gleam = np.array([255, 255, 255, 255], dtype=np.uint8) # #FFFFFF wet tip gleam
    elif vfx_style == "heavy_cleave":
        c_core = np.array([238, 244, 252, 255], dtype=np.uint8)  # #EEF4FC steel-white
        c_mid = np.array([227, 82, 89, 255], dtype=np.uint8)     # #E35259 crimson
        c_rim = np.array([153, 40, 56, 255], dtype=np.uint8)     # #992838 dark crimson
        c_gleam = np.array([255, 255, 255, 255], dtype=np.uint8)
    elif vfx_style == "jagged_slash":
        c_core = np.array([238, 244, 252, 255], dtype=np.uint8)  # #EEF4FC steel
        c_mid = np.array([255, 201, 82, 255], dtype=np.uint8)    # #FFC952 gold
        c_rim = np.array([154, 167, 189, 255], dtype=np.uint8)   # #9AA7BD steel rim
        c_gleam = np.array([255, 243, 176, 255], dtype=np.uint8)
    elif vfx_style == "spectral_slash":
        c_core = np.array([189, 247, 238, 255], dtype=np.uint8)  # #BDF7EE glowing cyan
        c_mid = np.array([133, 230, 218, 255], dtype=np.uint8)   # #85E6DA cyan
        c_rim = np.array([38, 127, 130, 255], dtype=np.uint8)    # #267F82 dark teal
        c_gleam = np.array([238, 244, 252, 255], dtype=np.uint8) # #EEF4FC steel gleam
    elif vfx_style == "arrow_left":
        c_core = np.array([255, 243, 176, 255], dtype=np.uint8)  # #FFF3B0 warm core
        c_mid = np.array([236, 169, 56, 255], dtype=np.uint8)    # #ECA938 amber
        c_rim = np.array([194, 56, 70, 255], dtype=np.uint8)     # #C23846 crimson-amber
        c_gleam = np.array([255, 201, 82, 255], dtype=np.uint8)
    else:
        # claw_bite
        c_core = np.array([247, 244, 234, 255], dtype=np.uint8)  # #F7F4EA ivory-white
        c_mid = np.array([227, 82, 89, 255], dtype=np.uint8)     # #E35259 crimson
        c_rim = np.array([153, 40, 56, 255], dtype=np.uint8)     # #992838 dark crimson
        c_gleam = np.array([255, 255, 255, 255], dtype=np.uint8)

    def put(px: int, py: int, col: np.ndarray) -> None:
        if SAFE_MIN <= px <= SAFE_MAX and SAFE_MIN <= py <= SAFE_MAX:
            out[py, px] = col

    cx = max(SAFE_MIN + 4, min(SAFE_MAX - 10, x_front))
    cy = max(SAFE_MIN + 12, min(GROUND_Y - 14, y_mid))

    if phase == "lunge":
        if vfx_style == "tongue_lash":
            # Pink/crimson whip extending up to 18 px left from maw height with wet tip gleam
            start_x = min(SAFE_MAX - 2, x_front + 6)
            tip_x = max(SAFE_MIN, x_front - 18)
            span = max(4, start_x - tip_x)
            for x in range(tip_x, start_x + 1):
                t = float(start_x - x) / float(span)
                wy = int(round(cy - 2.0 * math.sin(t * math.pi)))
                put(x, wy - 1, c_core)
                put(x, wy, c_mid)
                put(x, wy + 1, c_rim)
            # Wet bulbous tip + gleam droplets
            for dy_t in (-2, -1, 0, 1, 2):
                for dx_t in (0, 1, 2):
                    if abs(dy_t) + abs(dx_t) <= 2:
                        put(tip_x + dx_t, cy + dy_t, c_mid if dx_t > 0 else c_core)
            put(tip_x, cy - 1, c_gleam)
            put(tip_x + 1, cy - 1, c_gleam)
            put(max(SAFE_MIN, tip_x - 1), cy - 3, c_core)
            put(tip_x + 2, cy + 3, c_core)
        elif vfx_style == "heavy_cleave":
            arc_cx = max(SAFE_MIN + 12, cx + 2)
            for deg in range(-68, 72, 3):
                rad = math.radians(deg)
                rx = int(round(arc_cx - (12.0 * math.cos(rad) - 4.0)))
                ry = int(round(cy + 20.0 * math.sin(rad)))
                for dx_t, col in ((2, c_rim), (1, c_mid), (0, c_core), (-1, c_core), (-2, c_mid), (-3, c_rim)):
                    put(rx + dx_t, ry, col)
                put(rx - 1, ry - 1, c_mid)
                put(rx - 1, ry + 1, c_mid)
        elif vfx_style == "jagged_slash":
            arc_cx = max(SAFE_MIN + 10, cx + 2)
            for deg in range(-62, 65, 3):
                rad = math.radians(deg)
                jag = -2 if (abs(deg) % 18 < 6) else 0
                rx = int(round(arc_cx - (10.0 * math.cos(rad) - 3.0))) + jag
                ry = int(round(cy + 17.0 * math.sin(rad)))
                for dx_t, col in ((1, c_rim), (0, c_mid), (-1, c_core), (-2, c_mid)):
                    put(rx + dx_t, ry, col)
        elif vfx_style == "spectral_slash":
            arc_cx = max(SAFE_MIN + 11, cx + 2)
            for deg in range(-64, 68, 3):
                rad = math.radians(deg)
                rx = int(round(arc_cx - (11.0 * math.cos(rad) - 4.0)))
                ry = int(round(cy + 18.0 * math.sin(rad)))
                for dx_t, col in ((1, c_rim), (0, c_mid), (-1, c_core), (-2, c_core), (-3, c_mid)):
                    put(rx + dx_t, ry, col)
                put(rx - 2, ry - 1, c_gleam)
        elif vfx_style == "arrow_left":
            tip_x = max(SAFE_MIN, x_front - 16)
            tail_x = min(SAFE_MAX - 1, x_front + 8)
            for x in range(tip_x, tail_x + 1):
                put(x, cy - 1, c_rim)
                put(x, cy, c_core)
                put(x, cy + 1, c_mid)
            # Left-pointing arrowhead chevron
            for k in range(1, 5):
                put(tip_x + k, cy - k, c_mid)
                put(tip_x + k, cy + k, c_mid)
                put(tip_x + k + 1, cy - k, c_rim)
                put(tip_x + k + 1, cy + k, c_rim)
            put(tip_x, cy, c_gleam)
        else:
            # claw_bite: triple diagonal slash + fang accents on left
            start_x = min(SAFE_MAX - 6, cx + 3)
            for s_idx, y_off in enumerate((-8, 0, 8)):
                for step in range(0, 14):
                    px = start_x - step
                    py = cy + y_off + (step // 2) - 3
                    put(px, py - 1, c_rim)
                    put(px, py, c_core if step < 10 else c_mid)
                    put(px, py + 1, c_mid)
            # Upper/lower fang points
            for fk in range(0, 4):
                put(max(SAFE_MIN, start_x - 8 - fk), cy - 11 + fk, c_gleam)
                put(max(SAFE_MIN, start_x - 8 - fk), cy + 11 - fk, c_gleam)
    elif phase == "follow":
        # Fading leftward sparks
        for idx, (ox, oy) in enumerate(((4, -7), (8, -2), (6, 5), (10, 2), (3, 9))):
            col = c_core if idx % 2 == 0 else c_mid
            put(cx - ox, cy + oy, col)
            put(cx - ox - 1, cy + oy, c_rim)
    elif phase == "recover":
        # Faint trailing leftward spark
        for ox, oy in ((3, -4), (6, 1)):
            put(cx - ox, cy + oy, c_mid)

    return _quantize_preserve_colors(out, palette_rgb)


def _synth_monster_attack_frames(
    master: np.ndarray,
    body_type: str,
    vfx_style: str,
    palette_rgb: np.ndarray,
) -> List[np.ndarray]:
    """
    Synthesize 4-phase leftward (-X) monster attack strip:
      0: Anticipation (+3 px coil back right + 1 px upper crouch, legs at y=125)
      1: Strike Lunge (-5 px leftward lunge clamped >= SAFE_MIN + leftward VFX)
      2: Follow-Through (-3 px forward extension + fading left sparks)
      3: Recovery (-1 px settle back + faint trailing spark)
    """
    ys, xs = np.where(master[..., 3] >= 128)
    y_top, y_bot = int(ys.min()), int(ys.max())
    x_min, x_max = int(xs.min()), int(xs.max())
    h_op = y_bot - y_top + 1
    leg_ratio = 0.28 if body_type in ("amphibian", "quadruped") else 0.22
    leg_h = max(6, int(round(h_op * leg_ratio)))
    y_leg_start = y_bot - leg_h + 1
    y_mid = y_top + int(round(h_op * 0.42))

    max_right_safe = max(0, SAFE_MAX - x_max)
    max_left_safe = max(0, x_min - SAFE_MIN)

    # Frame 0: Anticipation (+3 px coil back right + 1 px upper crouch)
    f0 = np.zeros_like(master)
    coil_dx = min(3, max_right_safe)
    leg_mask = np.zeros((CANVAS_H, CANVAS_W), dtype=bool)
    leg_mask[y_leg_start:, :] = True
    upper_mask = ~leg_mask
    _shift_mask_pixels(master, leg_mask, 0, min(1, coil_dx), f0)
    _shift_mask_pixels(master, upper_mask, 1, coil_dx, f0)
    f0 = _fill_interior_pinholes(f0)
    f0 = _quantize_preserve_colors(f0, palette_rgb)

    # Frame 1: Strike Lunge (-5 px leftward lunge + leftward pixel VFX)
    f1 = np.zeros_like(master)
    lunge_dx = -min(5, max_left_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, lunge_dx, f1)
    f1 = _draw_monster_pixel_vfx(f1, vfx_style, x_min + lunge_dx, y_mid, "lunge", palette_rgb)

    # Frame 2: Follow-Through (-3 px forward extension + fading left sparks)
    f2 = np.zeros_like(master)
    follow_dx = -min(3, max_left_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, follow_dx, f2)
    f2 = _draw_monster_pixel_vfx(f2, vfx_style, x_min + follow_dx, y_mid, "follow", palette_rgb)

    # Frame 3: Recovery (-1 px settle back + faint trailing spark)
    f3 = np.zeros_like(master)
    rec_dx = -min(1, max_left_safe)
    _shift_mask_pixels(master, np.ones((CANVAS_H, CANVAS_W), dtype=bool), 0, rec_dx, f3)
    f3 = _draw_monster_pixel_vfx(f3, vfx_style, x_min + rec_dx, y_mid, "recover", palette_rgb)

    return [f0, f1, f2, f3]


def synthesize_monster_animations(
    master_128: np.ndarray,
    role: str = "monster_orc_warrior",
    palette_rgb: Optional[np.ndarray] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
) -> Dict[str, np.ndarray]:
    """
    Synthesize 4-frame 512x128 RGBA animation strips for each state in spec['states']:
      - 'sheet': Idle loop
      - 'walk_sheet': Walk/Hop loop (if present in spec['states'])
      - 'attack_sheet': Leftward combat strike loop (if present in spec['states'])
    """
    if master_128.shape != (CANVAS_H, CANVAS_W, 4):
        raise ValueError(f"Expected (128, 128, 4) master sprite, got {master_128.shape}")
    spec = get_monster_spec(role)
    if palette_rgb is None:
        palette_rgb = load_palette(palette_path)

    body_type = str(spec["body_type"])
    vfx_style = str(spec["vfx"])
    states = tuple(spec["states"])

    out: Dict[str, np.ndarray] = {}
    if "sheet" in states:
        idle_frames = _synth_monster_idle_frames(master_128, body_type=body_type, palette_rgb=palette_rgb)
        out["sheet"] = np.concatenate(idle_frames, axis=1)
    if "walk_sheet" in states:
        walk_frames = _synth_monster_walk_frames(master_128, body_type=body_type, palette_rgb=palette_rgb)
        out["walk_sheet"] = np.concatenate(walk_frames, axis=1)
    if "attack_sheet" in states:
        attack_frames = _synth_monster_attack_frames(
            master_128,
            body_type=body_type,
            vfx_style=vfx_style,
            palette_rgb=palette_rgb,
        )
        out["attack_sheet"] = np.concatenate(attack_frames, axis=1)
    return out


def build_monster_qa_contact_sheet(
    role: str,
    sheets: Dict[str, np.ndarray],
    normals: Dict[str, np.ndarray],
    out_qa_path: Path,
    scale: int = 4,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Path:
    """
    Build a 4x nearest-neighbor QA contact sheet, reusing build_hero_qa_contact_sheet
    for 3-state monsters and cleanly supporting 1-state monsters (e.g. monster_zombie).
    """
    required_three = ("sheet", "walk_sheet", "attack_sheet")
    if all(k in sheets and k in normals for k in required_three):
        return build_hero_qa_contact_sheet(
            role=role,
            sheets=sheets,
            normals=normals,
            out_qa_path=out_qa_path,
            scale=scale,
            cobble_path=cobble_path,
        )

    scale = max(1, int(scale))
    strip_w = SHEET_W * scale
    strip_h = CANVAS_H * scale
    label_h = 28
    pad = 8

    checker = _make_checkerboard_bg(SHEET_W, CANVAS_H, cell=8)
    cobble_1x = _make_cobble_bg(SHEET_W, CANVAS_H, cobble_path=cobble_path)

    state_titles = (
        ("sheet", "IDLE"),
        ("walk_sheet", "WALK"),
        ("attack_sheet", "ATTACK"),
    )
    active_states = [(k, t) for k, t in state_titles if k in sheets and k in normals]
    if not active_states:
        raise ValueError(f"No matching sheet/normal pairs provided for QA contact sheet of {role}")

    rows: List[Tuple[str, np.ndarray]] = []
    for key, title in active_states:
        comp_chk = _composite_over_bg(sheets[key], checker)
        comp_cob = _composite_over_bg(sheets[key], cobble_1x)
        rows.append((f"{role} | {title} (Dark Checkerboard 4x)", comp_chk))
        rows.append((f"{role} | {title} (Cobble Plaza 4x)", comp_cob))
    for key, title in active_states:
        rows.append((f"{role} | {title} NORMALS (OpenGL +Y 4x)", normals[key][..., :3]))

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
        for f in range(1, FRAME_COUNT):
            gx = pad + f * CANVAS_W * scale
            draw.line([(gx, y_cur), (gx, y_cur + strip_h - 1)], fill=(90, 96, 115), width=1)
        y_cur += strip_h + pad

    out_qa_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(out_qa_path, format="PNG")
    return out_qa_path


def process_monster(
    role: str,
    input_path: Union[str, Path],
    out_dir: Union[str, Path],
    qa_dir: Union[str, Path],
    promote_dir: Optional[Union[str, Path]] = None,
    flip_horizontal: bool = False,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Dict[str, Any]:
    """Run full cutout, animation synthesis, normal generation, and QA for one monster."""
    norm_role = normalize_monster_role(role)
    spec = get_monster_spec(norm_role)
    out_root = Path(out_dir)
    qa_root = Path(qa_dir)
    out_root.mkdir(parents=True, exist_ok=True)
    qa_root.mkdir(parents=True, exist_ok=True)

    palette_rgb = load_palette(palette_path)
    master = build_clean_monster_master(
        input_path,
        role=norm_role,
        palette_rgb=palette_rgb,
        palette_path=palette_path,
        flip_horizontal=flip_horizontal,
    )
    sheets = synthesize_monster_animations(
        master,
        role=norm_role,
        palette_rgb=palette_rgb,
        palette_path=palette_path,
    )
    normals = {
        key: generate_hero_sheet_normals(arr, laigter_exe=laigter_exe)
        for key, arr in sheets.items()
    }

    written_files: List[Path] = []
    sheet_stats: Dict[str, Dict[str, Any]] = {}
    for key in spec["states"]:
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
    build_monster_qa_contact_sheet(norm_role, sheets, normals, qa_path, scale=4, cobble_path=cobble_path)

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
        "flip_horizontal": bool(flip_horizontal),
        "states": list(spec["states"]),
        "qa_contact_sheet": str(qa_path),
        "outputs": [str(p) for p in written_files],
        "promoted": promoted_files,
        "sheets": sheet_stats,
    }


def _parse_manifest_entries(manifest_data: Any, manifest_dir: Path) -> List[Tuple[str, Path, bool]]:
    entries: List[Tuple[str, Path, bool]] = []
    if isinstance(manifest_data, dict):
        if "monsters" in manifest_data and isinstance(manifest_data["monsters"], list):
            items: Iterable[Any] = manifest_data["monsters"]
        elif "units" in manifest_data and isinstance(manifest_data["units"], list):
            items = manifest_data["units"]
        elif "heroes" in manifest_data and isinstance(manifest_data["heroes"], list):
            items = manifest_data["heroes"]
        else:
            for k, v in manifest_data.items():
                if isinstance(v, str):
                    p = Path(v)
                    if not p.is_absolute():
                        p = (manifest_dir / p).resolve()
                    entries.append((str(k), p, False))
                elif isinstance(v, dict):
                    raw_in = v.get("input") or v.get("input_path") or v.get("path")
                    if raw_in:
                        p = Path(raw_in)
                        if not p.is_absolute():
                            p = (manifest_dir / p).resolve()
                        role = str(v.get("id") or v.get("role") or v.get("unit_id") or k)
                        flip_h = bool(v.get("flip_horizontal", False))
                        entries.append((role, p, flip_h))
            return entries
    elif isinstance(manifest_data, list):
        items = manifest_data
    else:
        raise ValueError("Unsupported manifest JSON structure.")

    for item in items:
        if not isinstance(item, dict):
            continue
        role = item.get("id") or item.get("role") or item.get("unit_id")
        raw_in = item.get("input") or item.get("input_path") or item.get("path")
        if role and raw_in:
            p = Path(raw_in)
            if not p.is_absolute():
                p = (manifest_dir / p).resolve()
            flip_h = bool(item.get("flip_horizontal", False))
            entries.append((str(role), p, flip_h))
    return entries


def process_all_monsters(
    manifest_path: Union[str, Path],
    out_dir: Union[str, Path],
    qa_dir: Union[str, Path],
    promote_dir: Optional[Union[str, Path]] = None,
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
    cobble_path: Path = DEFAULT_COBBLE_PATH,
) -> Dict[str, Any]:
    """Process all monsters defined in a JSON manifest."""
    m_path = Path(manifest_path)
    data = json.loads(m_path.read_text(encoding="utf-8"))
    entries = _parse_manifest_entries(data, m_path.parent)
    if not entries:
        raise ValueError(f"No valid monster entries found in manifest: {m_path}")

    results: Dict[str, Any] = {}
    for role, in_p, flip_h in entries:
        res = process_monster(
            role=role,
            input_path=in_p,
            out_dir=out_dir,
            qa_dir=qa_dir,
            promote_dir=promote_dir,
            flip_horizontal=flip_h,
            palette_path=palette_path,
            laigter_exe=laigter_exe,
            cobble_path=cobble_path,
        )
        results[res["role"]] = res
    return results


def main(argv: Optional[List[str]] = None) -> None:
    parser = argparse.ArgumentParser(description="Realmz HD-2D Monster Redraw Pipeline")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--manifest", type=Path, help="JSON manifest mapping monster IDs to raw PNG paths")
    mode.add_argument(
        "--single",
        nargs=2,
        metavar=("ROLE", "INPUT_PATH"),
        help="Process a single monster role and raw input image path",
    )
    parser.add_argument("--flip-horizontal", action="store_true", help="Flip single input sprite horizontally")
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
        summary = process_all_monsters(
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
        summary = process_monster(
            role=role,
            input_path=Path(raw_in),
            out_dir=args.out_dir,
            qa_dir=qa_dir,
            promote_dir=args.promote_dir,
            flip_horizontal=bool(args.flip_horizontal),
            palette_path=args.palette,
            laigter_exe=args.laigter_exe,
            cobble_path=args.cobble_path,
        )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
