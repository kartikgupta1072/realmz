"""
Deterministic 512x256 (16x8 cells of 32x32) Songs-of-Conquest-style Bywater Foliage Atlas Generator.

Outputs:
  - C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/foliage/bywater_foliage_atlas.png
  - C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/foliage/bywater_foliage_atlas_n.png
  - C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/foliage/bywater_foliage_atlas.json
  - c:/Users/karti/LifeTwin/scratch/realmz_soc_vertical_slice/foliage_atlas_preview_4x.png
  - c:/Users/karti/LifeTwin/scratch/realmz_soc_vertical_slice/foliage_atlas_anim_4x.gif

Atlas layout (512x256, 16 cols x 8 rows of 32x32 cells):
  - Left bank  (x = 0..255,   col = frame 0..7):     Variants 0..7   at row = variant
  - Right bank (x = 256..511, col = 8 + frame 0..7): Variants 8..15  at row = variant - 8
  - BottomCenter anchor at local y = 30 (leaving y = 31 and >= 1-2px left/right/top clear).
  - Strictly locked to palette_soc_sunlit_128.json with 1-bit [0, 255] alpha.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
from PIL import Image, ImageDraw

from pixel_postprocess import (
    DEFAULT_LAIGTER_EXE,
    DEFAULT_PALETTE_PATH,
    generate_normal_map_laigter,
    generate_normal_map_sobel_fallback,
    hex_to_rgb,
    load_palette,
)

ATLAS_W = 512
ATLAS_H = 256
CELL_SIZE = 32
FRAMES_PER_VARIANT = 8
FRAME_DURATION_MS = 155
ANCHOR_X = 16
ANCHOR_Y = 30  # BottomCenter inside 0..31 (leaves row 31 transparent)

UNITY_FOLIAGE_DIR = Path(r"C:\realmz\remaster\RealmzUnity\Assets\Art\Cartography\foliage")
SCRATCH_DIR = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_soc_vertical_slice")

# Exact hex colors from palette_soc_sunlit_128.json
EMERALD = [
    "#183622",  # 0: deep root AO
    "#224D2E",  # 1: dark emerald shadow
    "#2E693B",  # 2: mid-dark emerald
    "#3D8748",  # 3: meadow green
    "#52A653",  # 4: lit meadow green
    "#6EC260",  # 5: sunlit lime
    "#8FD96E",  # 6: bright lime tip
    "#B4EC86",  # 7: sunbeam highlight
    "#D4F7A8",  # 8: crisp shimmer
]
SAGE = [
    "#1F3324",  # 0: deep moss AO
    "#2E4A33",  # 1: dark sage
    "#426643",  # 2: mid-dark sage
    "#5A8254",  # 3: sage green
    "#77A168",  # 4: lit sage
    "#99BF7E",  # 5: sunlit sage
    "#BED99A",  # 6: highlight sage
]
GOLD = ["#593212", "#7D4818", "#A6641F", "#CE8629", "#ECA938", "#FFC952", "#FFE07D", "#FFD92E"]
CRIMSON = ["#4A141E", "#701D2B", "#992838", "#C23846", "#E35259", "#F57673", "#FFA399"]
SKY = ["#123652", "#1A4E75", "#226A9C", "#2D88C2", "#3FA8E0", "#60C6F2", "#8CE0FA"]
TIMBER = ["#2E1B11", "#472A1A", "#633C24", "#825132", "#A36943", "#C2855A", "#DC9F73"]
INDIGO = ["#1C2033", "#232B42", "#2F3A59"]
CREAM = "#F7F4EA"
PINK_FLOWER = "#E8569C"


def rgb(hex_c: str) -> Tuple[int, int, int]:
    return hex_to_rgb(hex_c)


@dataclass
class BladeSpec:
    x0: float        # base x offset from anchor
    y0: float        # base y offset (0 = ANCHOR_Y, negative = higher)
    dx_tip: float    # horizontal tip reach relative to x0
    height: float    # vertical blade height
    bend: float      # curve midpoint horizontal bias
    base_w: float    # half-width at root (px)
    tip_w: float     # half-width at tip (px)
    layer: int       # 0 = back shadow, 1 = mid, 2 = front sunlit
    sway_amp: float  # max horizontal tip displacement in px
    phase_off: float = 0.0
    gold_tip: bool = False
    dark_forest: bool = False


def _set_px(cell: np.ndarray, x: int, y: int, color: Tuple[int, int, int], zbuf: np.ndarray, z: float) -> None:
    # Keep 2px left/right/top safe border inside 32x32 cell, bottom <= ANCHOR_Y (30)
    if 2 <= x <= 29 and 2 <= y <= ANCHOR_Y:
        if z >= zbuf[y, x]:
            zbuf[y, x] = z
            cell[y, x, :3] = color
            cell[y, x, 3] = 255


def _draw_blade(
    cell: np.ndarray,
    zbuf: np.ndarray,
    b: BladeSpec,
    wind_sin: float,
    shimmer: float,
    ramp_override: List[str] | None = None,
) -> None:
    ramp = ramp_override or EMERALD
    local_wind = math.sin(math.asin(max(-1.0, min(1.0, wind_sin))) + b.phase_off) if abs(b.phase_off) > 1e-4 else wind_sin
    # Discrete pixel wind offset at tip
    wind_dx = b.sway_amp * local_wind
    # Subtle height dip at strong lean
    lean_dip = 0.75 if abs(wind_dx) >= 1.5 else 0.0

    steps = max(24, int(b.height * 3))
    p0x = ANCHOR_X + b.x0
    p0y = ANCHOR_Y + b.y0
    p2x = p0x + b.dx_tip + wind_dx
    p2y = p0y - max(2.0, b.height - lean_dip)
    # Lock bottom 2-3px root by keeping control point bias above root
    p1x = p0x + b.bend + wind_dx * 0.32
    p1y = p0y - b.height * 0.54

    for i in range(steps + 1):
        t = i / steps
        # Quadratic Bezier
        inv = 1.0 - t
        cx = inv * inv * p0x + 2.0 * inv * t * p1x + t * t * p2x
        cy = inv * inv * p0y + 2.0 * inv * t * p1y + t * t * p2y

        # Lock bottom 2.5px completely against wind
        if (p0y - cy) <= 2.3:
            cx = inv * inv * p0x + 2.0 * inv * t * (p0x + b.bend) + t * t * (p0x + b.dx_tip)

        # Tapered half-width from thick root (2-3px) to razor 1px tip
        hw = b.base_w * ((1.0 - t) ** 0.85) + b.tip_w * (t ** 0.85)
        r_ceil = int(math.ceil(hw))

        ix_c = int(round(cx))
        iy_c = int(round(cy))

        for ox in range(-r_ceil, r_ceil + 1):
            for oy in range(-1, 2):
                px = ix_c + ox
                py = iy_c + oy
                dist = math.hypot((px - cx), (py - cy) * 1.15)
                if dist <= hw:
                    # Directional sunlight from upper-left: left/top of thick blade gets +1 step
                    sun_bias = -0.65 * ox - 0.35 * oy
                    base_idx = 0.7 + t * 4.9 + b.layer * 0.85 + (0.55 if sun_bias > 0.35 else (-0.45 if sun_bias < -0.35 else 0.0))
                    if t > 0.72 and shimmer > 0.4:
                        base_idx += 0.6
                    if b.dark_forest:
                        base_idx -= 1.35

                    if b.gold_tip and t > 0.62:
                        g_idx = min(6, max(2, int(round(2 + (t - 0.62) / 0.38 * 3.8 + (0.4 if shimmer > 0.4 else 0.0)))))
                        col = rgb(GOLD[g_idx])
                    else:
                        idx = max(0, min(len(ramp) - 1, int(round(base_idx))))
                        col = rgb(ramp[idx])

                    # Depth z prioritizes front layers and slightly higher pixels inside same layer
                    z = b.layer * 100.0 + t * 10.0 + (0.5 if ox <= 0 else 0.0)
                    _set_px(cell, px, py, col, zbuf, z)


def _draw_root_ao_mound(cell: np.ndarray, zbuf: np.ndarray, half_w: int, height: int = 3, dark: bool = False) -> None:
    """Grounded dark-emerald/indigo ambient occlusion cluster along bottom 2-3 rows."""
    for x in range(ANCHOR_X - half_w, ANCHOR_X + half_w + 1):
        edge = abs(x - ANCHOR_X) / max(1, half_w)
        max_h = max(1, int(round(height * (1.0 - 0.55 * (edge ** 1.6)))))
        for h_i in range(max_h):
            y = ANCHOR_Y - h_i
            if h_i == 0:
                col = rgb(INDIGO[1] if dark else EMERALD[0])
            elif h_i == 1:
                col = rgb(EMERALD[0] if dark else EMERALD[1])
            else:
                col = rgb(EMERALD[1] if dark else SAGE[1])
            _set_px(cell, x, y, col, zbuf, z=45.0 + h_i)


def _draw_flower_head(
    cell: np.ndarray,
    zbuf: np.ndarray,
    fx: int,
    fy: int,
    kind: str,
    z: float = 350.0,
) -> None:
    """Tiny 2-3px clustered SoC-style wildflower blossom tucked into grass blades."""
    if kind == "crimson":
        c_dark, c_mid, c_hi, c_core = rgb(CRIMSON[1]), rgb(CRIMSON[3]), rgb(CRIMSON[4]), rgb(GOLD[5])
    elif kind == "gold":
        c_dark, c_mid, c_hi, c_core = rgb(GOLD[2]), rgb(GOLD[4]), rgb(GOLD[6]), rgb(CREAM)
    elif kind == "sky":
        c_dark, c_mid, c_hi, c_core = rgb(SKY[2]), rgb(SKY[4]), rgb(SKY[5]), rgb(CREAM)
    else:
        c_dark, c_mid, c_hi, c_core = rgb(SAGE[2]), rgb(CREAM), rgb("#FFFFFF"), rgb(GOLD[5])

    # Subtle 1px dark green/shadow sepal directly beneath blossom
    _set_px(cell, fx, fy + 1, rgb(EMERALD[0]), zbuf, z - 1.0)
    # 2x2 or 3x2 crisp pixel flower cluster
    _set_px(cell, fx - 1, fy, c_mid, zbuf, z)
    _set_px(cell, fx + 1, fy, c_dark, zbuf, z)
    _set_px(cell, fx, fy - 1, c_hi, zbuf, z + 0.5)
    _set_px(cell, fx, fy, c_core, zbuf, z + 1.0)


def _draw_leaf_cluster(
    cell: np.ndarray,
    zbuf: np.ndarray,
    cx: float,
    cy: float,
    rx: float,
    ry: float,
    ramp: List[str],
    shade_bias: int,
    z: float,
    crisp_dither: bool = True,
) -> None:
    """Clustered pixel-art foliage lobe with scalloped edges, upper-left sun, and lower-right AO."""
    x0 = int(math.floor(cx - rx - 1))
    x1 = int(math.ceil(cx + rx + 1))
    y0 = int(math.floor(cy - ry - 1))
    y1 = int(math.ceil(cy + ry + 1))
    for py in range(y0, y1 + 1):
        for px in range(x0, x1 + 1):
            d = ((px - cx) / max(0.5, rx)) ** 2 + ((py - cy) / max(0.5, ry)) ** 2
            # Subtle pixel-cluster scallop along outer rim so clusters do not look like smooth balls
            scallop = 0.14 if (crisp_dither and ((px * 3 + py * 5) % 4 == 0)) else 0.0
            if d <= 1.0 - scallop:
                light = (-(px - cx) * 0.65 - (py - cy) * 0.90) / max(1.0, (rx + ry) * 0.5)
                idx = int(round(shade_bias + light * 1.9))
                if d > 0.72 and light < -0.15:
                    idx = max(0, idx - 1)
                idx = max(0, min(len(ramp) - 1, idx))
                _set_px(cell, px, py, rgb(ramp[idx]), zbuf, z + (1.0 - d))


def _build_meadow_tuft_blades(variant: int) -> List[BladeSpec]:
    """Variants 0..5 + 14 (golden) + 15 (deep forest): 6-9 overlapping tapered blades."""
    configs: Dict[int, List[Tuple[float, float, float, float, int, float, float]]] = {
        # (x0, dx_tip, height, bend, layer, base_w, phase_off)
        0: [  # Balanced lush 8-blade tuft (~20px wide x 14px tall)
            (-6.0, -5.2, 10.0, -2.0, 0, 1.40, -0.35),
            (-2.2, -1.6, 13.0, -0.8, 0, 1.50, -0.10),
            (2.4, 2.0, 12.5, 1.0, 0, 1.45, 0.20),
            (5.8, 5.2, 9.2, 2.0, 0, 1.35, 0.35),
            (-4.2, -3.0, 13.5, -1.4, 1, 1.55, -0.20),
            (0.0, 0.4, 14.5, 0.2, 2, 1.65, 0.0),
            (3.8, 3.2, 11.8, 1.4, 1, 1.50, 0.25),
            (-1.4, -0.8, 9.8, -0.4, 2, 1.40, 0.10),
        ],
        1: [  # Right-leaning breezy meadow clump (~19px wide x 15px tall)
            (-5.5, -2.2, 9.5, -0.8, 0, 1.35, -0.30),
            (-2.2, 0.8, 13.5, 0.3, 0, 1.50, -0.10),
            (1.6, 3.8, 14.0, 1.6, 0, 1.50, 0.15),
            (5.0, 5.4, 10.0, 2.2, 0, 1.35, 0.35),
            (-3.8, -0.8, 12.2, -0.3, 1, 1.50, -0.15),
            (-0.4, 2.0, 15.2, 1.0, 2, 1.65, 0.0),
            (3.2, 4.2, 12.0, 1.7, 2, 1.50, 0.20),
        ],
        2: [  # Wide low-crowned meadow fan (~23px wide x 12px tall)
            (-7.0, -5.6, 8.8, -2.2, 0, 1.35, -0.35),
            (-4.2, -3.2, 11.2, -1.2, 0, 1.50, -0.20),
            (0.0, 0.0, 12.2, 0.0, 0, 1.55, 0.0),
            (4.2, 3.2, 11.2, 1.2, 0, 1.50, 0.20),
            (7.0, 5.4, 8.5, 2.2, 0, 1.35, 0.35),
            (-5.6, -4.0, 10.5, -1.5, 1, 1.45, -0.25),
            (-1.8, -1.0, 11.8, -0.5, 2, 1.55, -0.05),
            (2.0, 1.4, 11.6, 0.7, 2, 1.55, 0.15),
            (5.6, 4.0, 9.5, 1.6, 1, 1.40, 0.30),
        ],
        3: [  # Twin-peaked wild tuft (~18px wide x 15px tall)
            (-5.0, -4.0, 13.5, -1.7, 0, 1.50, -0.25),
            (-1.8, -1.4, 10.5, -0.7, 0, 1.40, -0.10),
            (1.5, 1.4, 14.2, 0.7, 0, 1.55, 0.15),
            (5.0, 4.2, 11.0, 1.7, 0, 1.40, 0.30),
            (-3.4, -2.6, 14.8, -1.1, 2, 1.60, -0.15),
            (0.0, -0.2, 9.8, -0.1, 1, 1.45, 0.0),
            (3.2, 2.8, 13.6, 1.2, 2, 1.55, 0.25),
        ],
        4: [  # Compact stout turf clump (~16px wide x 11px tall)
            (-4.6, -3.4, 8.8, -1.3, 0, 1.45, -0.25),
            (-1.4, -0.8, 11.0, -0.3, 0, 1.55, -0.05),
            (1.8, 1.4, 10.6, 0.7, 0, 1.55, 0.15),
            (4.6, 3.5, 8.5, 1.4, 0, 1.40, 0.30),
            (-3.0, -2.0, 10.5, -0.9, 1, 1.50, -0.15),
            (0.4, 0.5, 11.8, 0.2, 2, 1.65, 0.05),
            (3.2, 2.5, 9.8, 1.0, 2, 1.45, 0.20),
        ],
        5: [  # Tall asymmetrical arch tuft (~20px wide x 16px tall)
            (-5.8, -4.2, 11.2, -1.6, 0, 1.40, -0.30),
            (-2.5, -1.8, 15.2, -0.9, 0, 1.55, -0.10),
            (1.2, 1.0, 14.8, 0.5, 0, 1.55, 0.10),
            (5.2, 4.4, 10.6, 1.8, 0, 1.40, 0.30),
            (-4.0, -2.8, 13.6, -1.2, 1, 1.50, -0.20),
            (-0.6, -0.3, 15.8, -0.2, 2, 1.70, 0.0),
            (2.8, 2.8, 13.2, 1.2, 2, 1.55, 0.20),
        ],
        14: [  # Warm sunlit golden-tipped meadow grass (~20px wide x 17px tall)
            (-6.0, -4.2, 12.0, -1.6, 0, 1.40, -0.30),
            (-2.8, -1.8, 16.0, -0.8, 0, 1.55, -0.12),
            (0.8, 0.9, 17.0, 0.5, 0, 1.60, 0.10),
            (4.2, 3.4, 14.6, 1.4, 0, 1.50, 0.25),
            (6.5, 5.0, 10.5, 2.0, 0, 1.35, 0.35),
            (-4.2, -2.6, 15.0, -1.1, 1, 1.55, -0.20),
            (-0.8, -0.4, 17.5, -0.2, 2, 1.70, 0.0),
            (2.6, 2.4, 15.4, 1.0, 2, 1.55, 0.18),
        ],
        15: [  # Cool shaded deep-emerald forest grass (~20px wide x 16px tall)
            (-6.0, -4.4, 11.0, -1.6, 0, 1.45, -0.30),
            (-2.4, -1.5, 14.6, -0.7, 0, 1.60, -0.10),
            (1.4, 1.2, 14.2, 0.6, 0, 1.55, 0.12),
            (5.6, 4.4, 10.6, 1.8, 0, 1.40, 0.30),
            (-4.0, -2.6, 13.6, -1.2, 1, 1.55, -0.20),
            (-0.2, 0.2, 15.6, 0.1, 2, 1.70, 0.0),
            (3.4, 2.8, 12.6, 1.2, 2, 1.50, 0.22),
        ],
    }

    is_gold = variant == 14
    is_forest = variant == 15
    blades: List[BladeSpec] = []
    for x0, dx, h, bend, layer, bw, ph in configs[variant]:
        blades.append(
            BladeSpec(
                x0=x0,
                y0=0.0,
                dx_tip=dx,
                height=h,
                bend=bend,
                base_w=bw,
                tip_w=0.48,
                layer=layer,
                sway_amp=1.9 if h >= 14.0 else 1.5,
                phase_off=ph,
                gold_tip=is_gold,
                dark_forest=is_forest,
            )
        )
    return blades


def render_variant_frame(variant: int, frame: int) -> np.ndarray:
    """Render one 32x32 RGBA cell for (variant 0..15, frame 0..7)."""
    cell = np.zeros((CELL_SIZE, CELL_SIZE, 4), dtype=np.uint8)
    zbuf = np.full((CELL_SIZE, CELL_SIZE), -1e6, dtype=np.float32)

    phase = 2.0 * math.pi * frame / FRAMES_PER_VARIANT
    wind_sin = math.sin(phase)
    # Highlight shimmer peaks when blades lean sunward (+x / frame 2)
    shimmer = max(0.0, math.sin(phase))

    if variant in (0, 1, 2, 3, 4, 5, 14, 15):
        half_w = 8 if variant != 2 else 10
        _draw_root_ao_mound(cell, zbuf, half_w=half_w, height=3, dark=(variant == 15))
        for b in _build_meadow_tuft_blades(variant):
            _draw_blade(cell, zbuf, b, wind_sin=wind_sin, shimmer=shimmer)

    elif variant in (6, 7, 8):
        # 3 lush grass clumps with tiny 2-3px crimson (6), gold (7), sky-blue (8) wildflowers
        kind = {6: "crimson", 7: "gold", 8: "sky"}[variant]
        _draw_root_ao_mound(cell, zbuf, half_w=7, height=3)
        tuft_blades = [
            BladeSpec(-5.5, 0.0, -2.5, 10.5, -1.0, 1.35, 0.45, 0, 1.4, -0.2),
            BladeSpec(-1.5, 0.0, -0.6, 13.0, -0.3, 1.45, 0.45, 0, 1.6, 0.0),
            BladeSpec(2.2, 0.0, 1.2, 12.0, 0.5, 1.40, 0.45, 0, 1.5, 0.15),
            BladeSpec(5.5, 0.0, 2.8, 9.5, 1.2, 1.30, 0.45, 0, 1.3, 0.25),
            BladeSpec(-3.5, 0.0, -1.5, 11.5, -0.7, 1.45, 0.48, 1, 1.5, -0.1),
            BladeSpec(0.5, 0.0, 0.4, 14.0, 0.2, 1.50, 0.48, 2, 1.7, 0.05),
            BladeSpec(3.6, 0.0, 2.0, 11.0, 0.9, 1.40, 0.45, 2, 1.4, 0.2),
        ]
        for b in tuft_blades:
            _draw_blade(cell, zbuf, b, wind_sin=wind_sin, shimmer=shimmer)

        # Flower stalks + tiny 2-3px flowers swaying cohesively with upper blades
        dx_f = int(round(1.6 * wind_sin))
        flowers = {
            6: [(-4, 19), (1, 16), (5, 20)],
            7: [(-5, 20), (0, 15), (4, 19)],
            8: [(-3, 18), (2, 16), (-6, 21)],
        }[variant]
        for fx_off, fy in flowers:
            fx = ANCHOR_X + fx_off + dx_f
            # 1px stem connecting down into tuft
            _set_px(cell, fx, fy + 2, rgb(EMERALD[2]), zbuf, z=320.0)
            _draw_flower_head(cell, zbuf, fx, fy, kind=kind, z=360.0)

    elif variant == 9:
        # Broad clustered arched fern rosette (24x20)
        _draw_root_ao_mound(cell, zbuf, half_w=9, height=3, dark=True)
        fronds = [
            # (x0, dx_tip, height, bend, layer, base_w, phase_off)
            (-4.0, -7.5, 9.0, -4.5, 0, 1.35, -0.30),
            (-2.5, -5.0, 15.0, -3.0, 0, 1.45, -0.15),
            (0.0, 0.3, 18.5, 0.2, 2, 1.55, 0.0),
            (2.5, 5.2, 14.8, 3.0, 1, 1.45, 0.15),
            (4.0, 7.5, 8.8, 4.5, 0, 1.35, 0.30),
        ]
        for x0, dx, h, bend, layer, bw, ph in fronds:
            b = BladeSpec(x0, 0.0, dx, h, bend, bw, 0.45, layer, 1.6, ph)
            _draw_blade(cell, zbuf, b, wind_sin=wind_sin, shimmer=shimmer, ramp_override=EMERALD)
            # Tapered triangular fern pinnae clusters along upper 65% of frond
            w_dx = 1.6 * math.sin(phase + ph)
            for frac, pin_rx in ((0.35, 2.2), (0.55, 2.0), (0.74, 1.6), (0.90, 1.1)):
                inv = 1.0 - frac
                p0x = ANCHOR_X + x0
                p1x = p0x + bend + w_dx * 0.32
                p2x = p0x + dx + w_dx
                lx = inv * inv * p0x + 2.0 * inv * frac * p1x + frac * frac * p2x
                p0y = ANCHOR_Y
                p1y = p0y - h * 0.54
                p2y = p0y - h
                ly = inv * inv * p0y + 2.0 * inv * frac * p1y + frac * frac * p2y
                shade = 2 + layer + (1 if frac > 0.7 else 0)
                z_base = 150.0 + layer * 40.0 + frac * 15.0
                _draw_leaf_cluster(cell, zbuf, lx - pin_rx * 0.75, ly, pin_rx, 1.15, EMERALD, shade, z_base, crisp_dither=False)
                _draw_leaf_cluster(cell, zbuf, lx + pin_rx * 0.75, ly, pin_rx, 1.15, EMERALD, max(1, shade - 1), z_base, crisp_dither=False)

    elif variant == 10:
        # Broad clustered bush tuft (24x20) with scalloped leaf lobes and crisp AO pockets
        _draw_root_ao_mound(cell, zbuf, half_w=10, height=3, dark=True)
        dx_top = 1.4 * wind_sin
        dx_mid = 0.7 * wind_sin
        # Deep AO core pocket behind clusters
        _draw_leaf_cluster(cell, zbuf, ANCHOR_X + dx_mid * 0.5, ANCHOR_Y - 7.5, 9.5, 6.2, EMERALD, 0, 95.0, crisp_dither=False)
        # Sculpted irregular foliage lobes (back -> mid -> sunlit upper-left crown)
        lobes = [
            (-8.2 + dx_mid, -5.2, 3.2, 2.7, 1, 130.0),
            (8.0 + dx_mid, -4.8, 3.1, 2.6, 1, 130.0),
            (-5.5 + dx_top, -10.8, 3.4, 3.0, 2, 160.0),
            (5.4 + dx_top, -10.2, 3.3, 2.9, 2, 160.0),
            (-0.5 + dx_top, -14.2, 3.8, 3.2, 3, 190.0),
            (-6.2 + dx_mid, -7.8, 3.3, 2.8, 3, 210.0),
            (6.0 + dx_mid, -7.2, 3.2, 2.7, 2, 210.0),
            (-2.5 + dx_top, -10.2, 3.6, 3.1, 4, 240.0),
            (2.8 + dx_mid, -9.5, 3.4, 2.9, 3, 235.0),
            (-3.2 + dx_mid, -4.5, 3.5, 2.5, 2, 220.0),
            (3.0 + dx_mid, -4.2, 3.4, 2.4, 2, 220.0),
            (0.0 + dx_top, -13.2, 3.0, 2.4, 5 if shimmer < 0.4 else 6, 280.0),
            (-4.2 + dx_top, -10.8, 2.4, 2.0, 5, 275.0),
        ]
        for lx, ly, rx, ry, sb, z in lobes:
            _draw_leaf_cluster(cell, zbuf, ANCHOR_X + lx, ANCHOR_Y + ly, rx, ry, EMERALD, sb, z, crisp_dither=True)

    elif variant == 11:
        # Riverbank tall reed / cattail clump (16x24)
        _draw_root_ao_mound(cell, zbuf, half_w=8, height=3, dark=True)
        reeds = [
            BladeSpec(-5.0, 0.0, -3.4, 17.5, -1.4, 1.35, 0.45, 0, 2.0, -0.25),
            BladeSpec(-1.5, 0.0, -0.6, 22.0, -0.3, 1.40, 0.45, 0, 2.1, -0.05),
            BladeSpec(2.2, 0.0, 1.5, 20.5, 0.7, 1.40, 0.45, 1, 2.0, 0.15),
            BladeSpec(4.8, 0.0, 3.4, 15.0, 1.5, 1.35, 0.45, 1, 1.6, 0.30),
            BladeSpec(-3.0, 0.0, -1.5, 14.5, -0.6, 1.45, 0.45, 2, 1.5, -0.10),
        ]
        for b in reeds:
            _draw_blade(cell, zbuf, b, wind_sin=wind_sin, shimmer=shimmer, ramp_override=SAGE + [EMERALD[6]])

        # Two crisp cattail seed heads on upright stalks
        for sx_off, top_y, ph in ((-2, 8, -0.08), (3, 10, 0.12)):
            w_s = int(round(1.9 * math.sin(phase + ph)))
            stalk_x = ANCHOR_X + sx_off
            for py in range(top_y - 2, ANCHOR_Y):
                frac = max(0.0, (ANCHOR_Y - py) / max(1, ANCHOR_Y - top_y))
                px = stalk_x + int(round(w_s * (frac ** 1.3))) if frac > 0.12 else stalk_x
                _set_px(cell, px, py, rgb(SAGE[2] if py < top_y + 4 else EMERALD[1]), zbuf, z=300.0)
            cx_head = stalk_x + w_s
            for dy_h in range(5):
                py_h = top_y + dy_h
                _set_px(cell, cx_head, py_h, rgb(TIMBER[4] if dy_h < 2 else TIMBER[3]), zbuf, z=340.0)
                _set_px(cell, cx_head + 1, py_h, rgb(TIMBER[2] if dy_h > 0 else TIMBER[3]), zbuf, z=339.0)
            _set_px(cell, cx_head, top_y - 1, rgb(TIMBER[1]), zbuf, z=341.0)
            _set_px(cell, cx_head, top_y - 2, rgb(TIMBER[2]), zbuf, z=341.0)

    elif variant in (12, 13):
        # Low clover & mossy ground tufts (16-18px wide x 10px tall)
        _draw_root_ao_mound(cell, zbuf, half_w=8, height=3, dark=(variant == 13))
        dx_w = 0.9 * wind_sin
        if variant == 12:
            # Distinct clover cushion with 3-leaf clusters + 2 blossoms
            clusters = [
                (-5.8, -3.2, 2.7, 2.0, 2, 130.0),
                (5.8, -3.0, 2.7, 2.0, 2, 130.0),
                (0.0, -3.4, 3.2, 2.2, 2, 140.0),
                (-3.0 + dx_w * 0.6, -5.6, 2.8, 2.2, 4, 170.0),
                (3.0 + dx_w * 0.6, -5.2, 2.8, 2.2, 3, 170.0),
                (0.0 + dx_w, -7.2, 3.0, 2.3, 5, 210.0),
            ]
            for lx, ly, rx, ry, sb, z in clusters:
                _draw_leaf_cluster(cell, zbuf, ANCHOR_X + lx, ANCHOR_Y + ly, rx, ry, SAGE, sb, z, crisp_dither=True)
            _draw_flower_head(cell, zbuf, ANCHOR_X + int(round(dx_w)) + 2, ANCHOR_Y - 7, kind="cream", z=260.0)
            _draw_flower_head(cell, zbuf, ANCHOR_X + int(round(dx_w)) - 4, ANCHOR_Y - 5, kind="cream", z=255.0)
        else:
            # Cohesive lush mossy tussock with embedded cobble pebble & short sage tufts
            moss_lobes = [
                (-4.5, -2.5, 3.5, 2.4, 2, 140.0),
                (0.5, -3.2, 4.2, 2.8, 3, 160.0),
                (-2.0 + dx_w * 0.5, -5.2, 3.2, 2.4, 4, 190.0),
                (4.5, -2.2, 3.0, 2.0, 2, 140.0),
            ]
            for lx, ly, rx, ry, sb, z in moss_lobes:
                _draw_leaf_cluster(cell, zbuf, ANCHOR_X + lx, ANCHOR_Y + ly, rx, ry, SAGE, sb, z, crisp_dither=True)
            for b in [
                BladeSpec(-4.2, 0.0, -1.8, 7.2, -0.7, 1.35, 0.45, 1, 1.0, -0.2),
                BladeSpec(-0.8, 0.0, -0.2, 8.6, -0.2, 1.45, 0.45, 2, 1.1, 0.0),
                BladeSpec(2.2, 0.0, 1.2, 7.5, 0.6, 1.35, 0.45, 2, 1.0, 0.2),
            ]:
                _draw_blade(cell, zbuf, b, wind_sin=wind_sin, shimmer=shimmer, ramp_override=SAGE + [EMERALD[5]])
            # Cute 4x3 sunlit stone pebble nested snugly into mossy right side
            for px_o, py_o, hex_c in (
                (4, -1, "#524E4B"), (5, -1, "#524E4B"), (6, -1, "#3B3837"), (7, -1, "#3B3837"),
                (4, -2, "#87817A"), (5, -2, "#A49E95"), (6, -2, "#87817A"), (7, -2, "#524E4B"),
                (5, -3, "#C2BCB2"), (6, -3, "#A49E95"),
            ):
                _set_px(cell, ANCHOR_X + px_o, ANCHOR_Y + py_o, rgb(hex_c), zbuf, 260.0)

    return cell


def build_foliage_atlas(
    palette_path: Path = DEFAULT_PALETTE_PATH,
    laigter_exe: Path = DEFAULT_LAIGTER_EXE,
) -> dict:
    palette_rgb = load_palette(palette_path)
    pal_set = {tuple(int(c) for c in row) for row in palette_rgb}

    atlas = np.zeros((ATLAS_H, ATLAS_W, 4), dtype=np.uint8)
    variant_names = [
        "meadow_grass_balanced",
        "meadow_grass_breeze",
        "meadow_grass_wide",
        "meadow_grass_twin",
        "meadow_grass_stout",
        "meadow_grass_tall",
        "wildflower_crimson",
        "wildflower_gold",
        "wildflower_sky",
        "fern_radiating",
        "shrub_clustered",
        "reed_cattail",
        "clover_cushion",
        "moss_pebble_tuft",
        "meadow_grass_golden",
        "forest_grass_shaded",
    ]
    categories = [
        "grass", "grass", "grass", "grass", "grass", "grass",
        "wildflower", "wildflower", "wildflower",
        "fern", "bush", "reed", "clover", "moss", "grass_sunlit", "grass_shaded",
    ]

    variants_meta: List[dict] = []

    for v in range(16):
        bank = "left" if v < 8 else "right"
        row = v if v < 8 else (v - 8)
        col_start = 0 if v < 8 else 8
        y0 = row * CELL_SIZE

        frame_rects = []
        for f in range(FRAMES_PER_VARIANT):
            col = col_start + f
            x0 = col * CELL_SIZE
            cell = render_variant_frame(v, f)
            atlas[y0 : y0 + CELL_SIZE, x0 : x0 + CELL_SIZE] = cell
            frame_rects.append(
                {
                    "frame": f,
                    "col": col,
                    "row": row,
                    "px_rect": [x0, y0, CELL_SIZE, CELL_SIZE],
                    "uv_rect": [
                        round(x0 / ATLAS_W, 6),
                        round(1.0 - (y0 + CELL_SIZE) / ATLAS_H, 6),
                        round(CELL_SIZE / ATLAS_W, 6),
                        round(CELL_SIZE / ATLAS_H, 6),
                    ],
                }
            )

        variants_meta.append(
            {
                "id": v,
                "name": variant_names[v],
                "category": categories[v],
                "bank": bank,
                "row": row,
                "col_start": col_start,
                "frames": frame_rects,
            }
        )

    # Strict palette & 1-bit alpha verification
    opaque = atlas[..., 3] == 255
    alpha_vals = sorted(int(a) for a in np.unique(atlas[..., 3]))
    used_colors = {tuple(int(c) for c in row) for row in atlas[opaque, :3]}
    off_palette = used_colors - pal_set
    if off_palette:
        raise RuntimeError(f"Found {len(off_palette)} off-palette colors in foliage atlas: {list(off_palette)[:5]}")
    if alpha_vals != [0, 255]:
        raise RuntimeError(f"Expected strictly 1-bit [0, 255] alpha, got {alpha_vals}")

    # Generate OpenGL +Y normal map (laigter.exe with upward foliage hemisphere bias, or Sobel fallback)
    normal_map = generate_normal_map_laigter(
        atlas,
        laigter_exe=laigter_exe,
        periodic=False,
        xy_scale=0.65,
        min_z=0.36,
    )
    normal_backend = "laigter"
    if normal_map is None:
        normal_map = generate_normal_map_sobel_fallback(atlas, periodic=False, bevel_strength=1.1, detail_strength=1.35, bevel_radius=3.0)
        normal_backend = "sobel"

    # Blend gentle upward (+Y) hemisphere bias on opaque foliage blades for cohesive meadow sun response
    nx = (normal_map[opaque, 0].astype(np.float32) - 127.5) / 127.5
    ny = (normal_map[opaque, 1].astype(np.float32) - 127.5) / 127.5 + 0.24
    nz = (normal_map[opaque, 2].astype(np.float32) - 127.5) / 127.5 + 0.12
    mag = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-8
    normal_map[opaque, 0] = np.clip(np.round((nx / mag) * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_map[opaque, 1] = np.clip(np.round((ny / mag) * 127.5 + 127.5), 0, 255).astype(np.uint8)
    normal_map[opaque, 2] = np.clip(np.round((nz / mag) * 127.5 + 127.5), 128, 255).astype(np.uint8)
    normal_map[opaque, 3] = 255
    normal_map[~opaque] = [128, 128, 255, 0]

    # Save Unity outputs
    UNITY_FOLIAGE_DIR.mkdir(parents=True, exist_ok=True)
    atlas_path = UNITY_FOLIAGE_DIR / "bywater_foliage_atlas.png"
    normal_path = UNITY_FOLIAGE_DIR / "bywater_foliage_atlas_n.png"
    json_path = UNITY_FOLIAGE_DIR / "bywater_foliage_atlas.json"

    Image.fromarray(atlas, mode="RGBA").save(atlas_path, format="PNG")
    Image.fromarray(normal_map, mode="RGBA").save(normal_path, format="PNG")

    metadata = {
        "name": "bywater_foliage_atlas",
        "atlas_width": ATLAS_W,
        "atlas_height": ATLAS_H,
        "cell_width": CELL_SIZE,
        "cell_height": CELL_SIZE,
        "columns": 16,
        "rows": 8,
        "variant_count": 16,
        "frames_per_variant": FRAMES_PER_VARIANT,
        "frame_duration_ms": FRAME_DURATION_MS,
        "pivot": "BottomCenter",
        "anchor_local_px": [ANCHOR_X, ANCHOR_Y],
        "pivot_normalized": [0.5, round((CELL_SIZE - 1 - ANCHOR_Y + 0.5) / CELL_SIZE, 6)],
        "palette": str(palette_path.name),
        "unique_palette_colors": len(used_colors),
        "off_palette_colors": len(off_palette),
        "alpha_is_1bit": True,
        "normal_backend": normal_backend,
        "variants": variants_meta,
    }
    json_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")

    # Render 4x preview sheet + 4x animated GIF in scratch
    SCRATCH_DIR.mkdir(parents=True, exist_ok=True)
    preview_path = SCRATCH_DIR / "foliage_atlas_preview_4x.png"
    anim_gif_path = SCRATCH_DIR / "foliage_atlas_anim_4x.gif"

    # Preview 4x: composite atlas over dark slate-meadow backdrop with crisp cell grid & divider
    bg = Image.new("RGBA", (ATLAS_W, ATLAS_H), (35, 43, 66, 255))
    atlas_img = Image.fromarray(atlas, mode="RGBA")
    comp = Image.alpha_composite(bg, atlas_img)
    preview_4x = comp.resize((ATLAS_W * 4, ATLAS_H * 4), Image.Resampling.NEAREST)
    draw = ImageDraw.Draw(preview_4x)
    # Draw subtle cell grid lines
    for c in range(1, 16):
        x = c * CELL_SIZE * 4
        col = (255, 201, 82, 255) if c == 8 else (62, 76, 115, 255)
        w_line = 3 if c == 8 else 1
        draw.line([(x, 0), (x, ATLAS_H * 4)], fill=col, width=w_line)
    for r in range(1, 8):
        y = r * CELL_SIZE * 4
        draw.line([(0, y), (ATLAS_W * 4, y)], fill=(62, 76, 115, 255), width=1)
    for v in range(16):
        bx = 4 if v < 8 else (8 * CELL_SIZE * 4 + 4)
        by = (v if v < 8 else v - 8) * CELL_SIZE * 4 + 3
        draw.text((bx, by), f"V{v:02d} {variant_names[v]}", fill=(247, 244, 234, 255))
    preview_4x.save(preview_path, format="PNG")

    # Animated 4x GIF: 4x4 grid of all 16 variants swaying over sunlit meadow turf
    tile_px = 40  # 32x32 cell with 4px breathing space around each variant
    grid_w, grid_h = 4 * tile_px, 4 * tile_px
    gif_frames: List[Image.Image] = []
    for f in range(FRAMES_PER_VARIANT):
        canvas = Image.new("RGBA", (grid_w, grid_h), (46, 105, 59, 255))
        cdraw = ImageDraw.Draw(canvas)
        for v in range(16):
            gx = v % 4
            gy = v // 4
            row = v if v < 8 else v - 8
            col = f if v < 8 else (8 + f)
            cell_crop = atlas_img.crop((col * CELL_SIZE, row * CELL_SIZE, (col + 1) * CELL_SIZE, (row + 1) * CELL_SIZE))
            ox = gx * tile_px + 4
            oy = gy * tile_px + 4
            # Ground swatch pad
            cdraw.rectangle([gx * tile_px + 1, gy * tile_px + 1, (gx + 1) * tile_px - 2, (gy + 1) * tile_px - 2], fill=(61, 135, 72, 255))
            canvas.paste(cell_crop, (ox, oy), cell_crop)
        up = canvas.resize((grid_w * 4, grid_h * 4), Image.Resampling.NEAREST)
        udraw = ImageDraw.Draw(up)
        for v in range(16):
            gx, gy = v % 4, v // 4
            udraw.text((gx * tile_px * 4 + 6, gy * tile_px * 4 + 4), f"V{v:02d}", fill=(240, 222, 194, 255))
        udraw.text((8, grid_h * 4 - 16), f"Frame {f}/7 ({FRAME_DURATION_MS}ms)", fill=(255, 224, 125, 255))
        gif_frames.append(up.convert("P", palette=Image.Palette.ADAPTIVE, colors=128))

    gif_frames[0].save(
        anim_gif_path,
        save_all=True,
        append_images=gif_frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,
        disposal=2,
    )

    return {
        "atlas_path": str(atlas_path),
        "normal_path": str(normal_path),
        "json_path": str(json_path),
        "preview_path": str(preview_path),
        "anim_gif_path": str(anim_gif_path),
        "unique_palette_colors": len(used_colors),
        "off_palette_colors": len(off_palette),
        "normal_backend": normal_backend,
    }


if __name__ == "__main__":
    summary = build_foliage_atlas()
    print(json.dumps(summary, indent=2))
