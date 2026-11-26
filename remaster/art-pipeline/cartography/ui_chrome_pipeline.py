"""
Deterministic 9-Slice Stone & Forged-Gold UI Chrome Kit Pipeline for Realmz M1.15
("Chiseled Slate & Forged Gold").

Generates 14 RGBA PNGs and ensures BlackChancery.ttf in:
  remaster/RealmzUnity/Assets/Art/Cartography/UI/
Also outputs a labeled 9-slice inspection contact sheet to:
  .scratch/runs/ui_chrome_contact_sheet.png

All textures use deterministic seeded multi-octave value noise (zero unseeded
randomness), crisp #0E1016 dark ink seams, hammered bronze/gold bevels, and
strictly aligned 9-slice border regions (L, B, R, T).
"""

from __future__ import annotations

import argparse
import hashlib
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_FONT_SRC = REPO_ROOT / "resources" / "Black Chancery.ttf"
DEFAULT_UI_OUT_DIR = REPO_ROOT / "remaster" / "RealmzUnity" / "Assets" / "Art" / "Cartography" / "UI"
DEFAULT_CONTACT_SHEET = REPO_ROOT / ".scratch" / "runs" / "ui_chrome_contact_sheet.png"

INK_HEX = "#100F0E"


@dataclass(frozen=True)
class UiAssetSpec:
    filename: str
    width: int
    height: int
    border: Tuple[int, int, int, int]  # Unity Sprite border order: (L, B, R, T)
    is_9slice: bool
    label: str


UI_ASSET_SPECS: Tuple[UiAssetSpec, ...] = (
    UiAssetSpec("ui_stone_panel_9s.png", 64, 64, (10, 10, 10, 10), True, "Stone Panel (64x64, LBRT 10)"),
    UiAssetSpec("ui_stone_bar_9s.png", 48, 48, (6, 6, 6, 6), True, "Stone Bar (48x48, LBRT 6)"),
    UiAssetSpec("ui_stone_well_9s.png", 48, 48, (6, 6, 6, 6), True, "Stone Well (48x48, LBRT 6)"),
    UiAssetSpec("ui_tint_panel_9s.png", 48, 48, (6, 6, 6, 6), True, "Tint Panel (48x48, LBRT 6)"),
    UiAssetSpec("ui_btn_stone_9s.png", 48, 32, (6, 6, 6, 6), True, "Stone Button (48x32, LBRT 6)"),
    UiAssetSpec("ui_btn_gold_9s.png", 48, 32, (6, 6, 6, 6), True, "Gold Button (48x32, LBRT 6)"),
    UiAssetSpec("ui_bar_track_9s.png", 32, 16, (4, 4, 4, 4), True, "Gauge Track (32x16, LBRT 4)"),
    UiAssetSpec("ui_bar_fill_9s.png", 16, 16, (2, 2, 2, 2), True, "Gauge Fill (16x16, LBRT 2)"),
    UiAssetSpec("ui_header_rule.png", 128, 8, (16, 0, 16, 0), True, "Header Rule (128x8, L16 R16)"),
    UiAssetSpec("ui_gem_pip_off.png", 16, 16, (0, 0, 0, 0), False, "Gem Pip Off (16x16)"),
    UiAssetSpec("ui_gem_pip_gold.png", 16, 16, (0, 0, 0, 0), False, "Gem Pip Gold (16x16)"),
    UiAssetSpec("ui_gem_pip_cyan.png", 16, 16, (0, 0, 0, 0), False, "Gem Pip Cyan (16x16)"),
    UiAssetSpec("ui_gem_pip_emerald.png", 16, 16, (0, 0, 0, 0), False, "Gem Pip Emerald (16x16)"),
    UiAssetSpec("ui_modal_scrim.png", 320, 180, (0, 0, 0, 0), False, "Modal Scrim (320x180)"),
)


def hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    h = hex_color.strip().lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def _rgb_f(hex_color: str) -> np.ndarray:
    return np.array(hex_to_rgb(hex_color), dtype=np.float32)


def _lerp_rgb(c0: np.ndarray, c1: np.ndarray, t: np.ndarray | float) -> np.ndarray:
    if isinstance(t, np.ndarray) and t.ndim == 2:
        t = t[..., None]
    return c0 * (1.0 - t) + c1 * t


def _to_rgba_u8(rgb_f: np.ndarray, alpha_u8: Optional[np.ndarray] = None) -> np.ndarray:
    h, w, _ = rgb_f.shape
    out = np.empty((h, w, 4), dtype=np.uint8)
    out[..., :3] = np.clip(np.round(rgb_f), 0, 255).astype(np.uint8)
    if alpha_u8 is None:
        out[..., 3] = 255
    else:
        out[..., 3] = alpha_u8
    return out


def _value_noise_2d(h: int, w: int, grid_h: int, grid_w: int, seed: int) -> np.ndarray:
    """Deterministic seeded smoothstep 2D value noise mapped onto (h, w)."""
    rng = np.random.default_rng(seed)
    grid = rng.random((grid_h + 1, grid_w + 1), dtype=np.float64).astype(np.float32)
    # Wrap edge for seamless interior tiling when sampled modulo grid
    grid[-1, :] = grid[0, :]
    grid[:, -1] = grid[:, 0]

    ys = np.linspace(0.0, float(grid_h), h, endpoint=False, dtype=np.float32)
    xs = np.linspace(0.0, float(grid_w), w, endpoint=False, dtype=np.float32)
    yi = np.floor(ys).astype(np.int32)
    xi = np.floor(xs).astype(np.int32)
    fy = ys - yi
    fx = xs - xi

    # Smoothstep
    sy = (fy * fy * (3.0 - 2.0 * fy))[:, None]
    sx = (fx * fx * (3.0 - 2.0 * fx))[None, :]

    v00 = grid[yi[:, None], xi[None, :]]
    v10 = grid[yi[:, None], (xi + 1)[None, :]]
    v01 = grid[(yi + 1)[:, None], xi[None, :]]
    v11 = grid[(yi + 1)[:, None], (xi + 1)[None, :]]

    top = v00 * (1.0 - sx) + v10 * sx
    bot = v01 * (1.0 - sx) + v11 * sx
    return top * (1.0 - sy) + bot * sy


def fbm_granite(
    h: int,
    w: int,
    seed: int,
    octaves: Tuple[Tuple[int, int, float], ...] = (
        (4, 4, 0.48),
        (8, 8, 0.30),
        (16, 16, 0.14),
        (32, 32, 0.08),
    ),
) -> np.ndarray:
    """Deterministic multi-octave carved granite noise normalized to [0, 1]."""
    acc = np.zeros((h, w), dtype=np.float32)
    weight_sum = 0.0
    for idx, (gh, gw, wt) in enumerate(octaves):
        acc += _value_noise_2d(h, w, max(2, gh), max(2, gw), seed + idx * 9973) * wt
        weight_sum += wt
    acc /= max(weight_sum, 1e-6)

    # Fine deterministic pixel-level mineral speckle (subtle so 9-slice stretching stays smooth)
    rng = np.random.default_rng(seed + 7919)
    speckle = rng.random((h, w), dtype=np.float64).astype(np.float32)
    combined = 0.84 * acc + 0.16 * speckle
    lo = float(combined.min())
    hi = float(combined.max())
    if hi - lo > 1e-6:
        combined = (combined - lo) / (hi - lo)
    return np.clip(combined, 0.0, 1.0)


def _paint_pixel(canvas: np.ndarray, y: int, x: int, hex_color: str, alpha: int = 255) -> None:
    if 0 <= y < canvas.shape[0] and 0 <= x < canvas.shape[1]:
        r, g, b = hex_to_rgb(hex_color)
        canvas[y, x] = [r, g, b, alpha]


def _draw_rect_ring(
    canvas: np.ndarray,
    inset: int,
    top_left_hex: str,
    bottom_right_hex: str,
    top_hex: Optional[str] = None,
    left_hex: Optional[str] = None,
    bottom_hex: Optional[str] = None,
    right_hex: Optional[str] = None,
) -> None:
    """Paint a 1px rectangular bevel ring at `inset` pixels from the outer edge."""
    h, w, _ = canvas.shape
    y0, y1 = inset, h - 1 - inset
    x0, x1 = inset, w - 1 - inset
    if y0 > y1 or x0 > x1:
        return

    c_top = hex_to_rgb(top_hex or top_left_hex)
    c_left = hex_to_rgb(left_hex or top_left_hex)
    c_bot = hex_to_rgb(bottom_hex or bottom_right_hex)
    c_right = hex_to_rgb(right_hex or bottom_right_hex)

    # Bottom and right first, then top and left take priority at top-left / mitered corners
    canvas[y1, x0 : x1 + 1, :3] = c_bot
    canvas[y0 : y1 + 1, x1, :3] = c_right
    canvas[y0, x0 : x1 + 1, :3] = c_top
    canvas[y0 : y1 + 1, x0, :3] = c_left
    canvas[y0, x0 : x1 + 1, 3] = 255
    canvas[y1, x0 : x1 + 1, 3] = 255
    canvas[y0 : y1 + 1, x0, 3] = 255
    canvas[y0 : y1 + 1, x1, 3] = 255


def _stamp_symmetric_corners(canvas: np.ndarray, tl_patch: np.ndarray) -> None:
    """Stamp a top-left RGBA patch onto all 4 corners with exact mirror symmetry."""
    ph, pw, _ = tl_patch.shape
    patches = [
        (0, 0, tl_patch),
        (0, canvas.shape[1] - pw, np.fliplr(tl_patch)),
        (canvas.shape[0] - ph, 0, np.flipud(tl_patch)),
        (canvas.shape[0] - ph, canvas.shape[1] - pw, np.flipud(np.fliplr(tl_patch))),
    ]
    for y0, x0, patch in patches:
        mask = patch[..., 3] > 0
        sub = canvas[y0 : y0 + ph, x0 : x0 + pw]
        sub[mask] = patch[mask]


def _make_stone_panel_clasp_10x10() -> np.ndarray:
    """
    Build a 10x10 hammered bronze/gold L-bracket corner clasp with a 2x2 gold
    corner rivet stud and #0E1016 dark ink seams. Designed so all 4 corners are
    strictly mirror-symmetric and stay strictly inside the 10px 9-slice border.
    """
    p = np.zeros((10, 10, 4), dtype=np.uint8)
    # 0 = transparent (preserve stone behind), codes map to bronze/gold/ink
    # 10x10 grid strictly inside border L=10, T=10
    layout = [
        "KKKKKKKKKK",
        "KHHHHHHHMK",
        "KHGGGGGGMK",
        "KHGSSGMMMK",
        "KHGSSGMKKK",
        "KHGGMMK...",
        "KHHMMK....",
        "KMMMK.....",
        "KMMK......",
        "KKK.......",
    ]
    palette = {
        "K": "#100F0E",  # Dark ink seam
        "D": "#5E431B",  # Deep bronze shadow
        "M": "#8A632B",  # Hammered mid-bronze
        "G": "#C99A48",  # Forged gold body
        "H": "#F2CF78",  # Gleaming gold bevel
        "S": "#F2CF78",  # 2x2 gold corner rivet stud
    }
    for y, row in enumerate(layout):
        for x, ch in enumerate(row):
            if ch != ".":
                r, g, b = hex_to_rgb(palette[ch])
                p[y, x] = [r, g, b, 255]
    # Add dark bronze inner shade on lower-right half of the 2x2 stud for relief
    _paint_pixel(p, 4, 4, "#C99A48")
    # Make outer bevel transition crisp on stud seat
    _paint_pixel(p, 2, 2, "#F2CF78")
    _paint_pixel(p, 3, 2, "#C99A48")
    _paint_pixel(p, 2, 3, "#C99A48")
    # Give inner rivet highlight a bright specular pinpoint
    _paint_pixel(p, 3, 3, "#FFF1B8")
    return p


def build_stone_panel_9s() -> np.ndarray:
    """64x64 raised chiseled charcoal-granite modal slab (border L=10, B=10, R=10, T=10)."""
    h, w = 64, 64
    noise = fbm_granite(
        h,
        w,
        seed=101,
        octaves=((4, 4, 0.45), (8, 8, 0.30), (16, 16, 0.15), (32, 32, 0.10)),
    )
    # #222120 .. #2E2C2A warm-neutral charcoal granite grain
    rgb = _lerp_rgb(_rgb_f("#222120"), _rgb_f("#2E2C2A"), noise)
    canvas = _to_rgba_u8(rgb)

    # Outer 1px ink seam & raised granite bevels (within border=10)
    _draw_rect_ring(canvas, 0, "#100F0E", "#100F0E")
    _draw_rect_ring(canvas, 1, "#5C5852", "#100F0E")
    _draw_rect_ring(canvas, 2, "#3D3A36", "#181715")
    # Stone bezel face d=3..5 inherits subtle darker/lighter granite grain
    _draw_rect_ring(canvas, 6, "#141312", "#141312")  # recessed groove
    _draw_rect_ring(canvas, 7, "#8A632B", "#5E431B", top_hex="#A87B36", left_hex="#996F30")  # dark-bronze inner trim seam
    _draw_rect_ring(canvas, 8, "#100F0E", "#100F0E")  # crisp ink trim border
    _draw_rect_ring(canvas, 9, "#1A1917", "#32302D")  # subtle inner drop shadow / catchlight

    # Symmetric hammered bronze/gold L-bracket corner clasps on all 4 corners
    clasp = _make_stone_panel_clasp_10x10()
    _stamp_symmetric_corners(canvas, clasp)
    return canvas


def _make_bronze_rivet_patch_6x6(pin_size: int = 3) -> np.ndarray:
    """Compact corner bronze rivet patch strictly inside 6x6 corner border."""
    p = np.zeros((6, 6, 4), dtype=np.uint8)
    if pin_size == 3:
        coords = [
            (1, 1, "#100F0E"), (1, 2, "#100F0E"), (1, 3, "#100F0E"), (1, 4, "#100F0E"),
            (2, 1, "#100F0E"), (2, 2, "#F2CF78"), (2, 3, "#C99A48"), (2, 4, "#100F0E"),
            (3, 1, "#100F0E"), (3, 2, "#C99A48"), (3, 3, "#8A632B"), (3, 4, "#100F0E"),
            (4, 1, "#100F0E"), (4, 2, "#100F0E"), (4, 3, "#100F0E"), (4, 4, "#100F0E"),
        ]
    else:
        coords = [
            (2, 2, "#B89150"), (2, 3, "#8A6836"),
            (3, 2, "#8A6836"), (3, 3, "#5E4624"),
            (4, 2, "#100F0E"), (4, 3, "#100F0E"),
            (2, 4, "#100F0E"), (3, 4, "#100F0E"), (4, 4, "#100F0E"),
        ]
    for y, x, c in coords:
        _paint_pixel(p, y, x, c)
    return p


def build_stone_bar_9s() -> np.ndarray:
    """48x48 compact chiseled warm-charcoal HUD bar/card frame (border L=6, B=6, R=6, T=6)."""
    h, w = 48, 48
    noise = fbm_granite(h, w, seed=202, octaves=((3, 3, 0.45), (6, 6, 0.30), (12, 12, 0.15), (24, 24, 0.10)))
    rgb = _lerp_rgb(_rgb_f("#1D1C1B"), _rgb_f("#282624"), noise)
    canvas = _to_rgba_u8(rgb)

    _draw_rect_ring(canvas, 0, "#100F0E", "#100F0E")
    _draw_rect_ring(canvas, 1, "#55514B", "#100F0E", top_hex="#5E5A53", left_hex="#4C4843")
    _draw_rect_ring(canvas, 2, "#34312E", "#171615")
    _draw_rect_ring(canvas, 3, "#272523", "#1C1B19")
    _draw_rect_ring(canvas, 4, "#11100F", "#302E2B")  # crisp recessed inner seam
    _draw_rect_ring(canvas, 5, "#191816", "#23211F")

    _stamp_symmetric_corners(canvas, _make_bronze_rivet_patch_6x6(pin_size=3))
    return canvas


def build_stone_well_9s() -> np.ndarray:
    """48x48 deeply recessed carved-stone socket/well (border L=6, B=6, R=6, T=6)."""
    h, w = 48, 48
    noise = fbm_granite(h, w, seed=303, octaves=((3, 3, 0.45), (6, 6, 0.30), (12, 12, 0.15), (24, 24, 0.10)))
    rgb = _lerp_rgb(_rgb_f("#151413"), _rgb_f("#1F1E1C"), noise)
    canvas = _to_rgba_u8(rgb)

    # Outer rim & deep carved top-left shadow + crisp bottom-right stone catch-light
    _draw_rect_ring(canvas, 0, "#100F0E", "#100F0E")
    _draw_rect_ring(canvas, 1, "#2A2826", "#4E4A44")  # outer stone lip with strong BR catch-light
    _draw_rect_ring(canvas, 2, "#0B0A09", "#33302D")  # deep TL inner shadow / BR inner slope
    _draw_rect_ring(canvas, 3, "#0B0A09", "#22201E")
    _draw_rect_ring(canvas, 4, "#100F0E", "#1C1B19")
    _draw_rect_ring(canvas, 5, "#131211", "#1A1917")

    # Subtle carved masonry corner miter seams strictly inside 6x6 corners
    for k in range(1, 5):
        _paint_pixel(canvas, k, k, "#0B0A09")
        _paint_pixel(canvas, k, w - 1 - k, "#1A1917")
        _paint_pixel(canvas, h - 1 - k, k, "#1A1917")
        _paint_pixel(canvas, h - 1 - k, w - 1 - k, "#33302D")
    return canvas


def build_tint_panel_9s() -> np.ndarray:
    """
    48x48 high-key neutral grayscale chiseled stone/enamel slab (border L=6, B=6, R=6, T=6).
    Center luminance sits cleanly in ~0.78..0.96 so Image.color tinting keeps stone grain
    and readable contrast.
    """
    h, w = 48, 48
    noise = fbm_granite(h, w, seed=404, octaves=((3, 3, 0.48), (6, 6, 0.28), (12, 12, 0.14), (24, 24, 0.10)))
    # Neutral grayscale 0.80..0.95 (204..242)
    luma = 204.0 + noise * 38.0
    rgb = np.repeat(luma[..., None], 3, axis=2)
    canvas = _to_rgba_u8(rgb)

    _draw_rect_ring(canvas, 0, "#181818", "#181818")  # crisp dark 1px outer seam
    _draw_rect_ring(canvas, 1, "#FAFAFA", "#707070")  # high-contrast bevel highlights
    _draw_rect_ring(canvas, 2, "#E4E4E4", "#989898")
    _draw_rect_ring(canvas, 3, "#D2D2D2", "#B8B8B8")
    _draw_rect_ring(canvas, 4, "#AEAEAE", "#E8E8E8")  # subtle inner enamel trim seam
    _draw_rect_ring(canvas, 5, "#DCDCDC", "#CACACA")

    # Small corner bevel studs in neutral bright metal inside 6x6 corners
    stud = np.zeros((6, 6, 4), dtype=np.uint8)
    for y, x, c in [
        (2, 2, "#FFFFFF"), (2, 3, "#D6D6D6"),
        (3, 2, "#D6D6D6"), (3, 3, "#8A8A8A"),
    ]:
        _paint_pixel(stud, y, x, c)
    _stamp_symmetric_corners(canvas, stud)
    return canvas


def build_btn_stone_9s() -> np.ndarray:
    """48x32 raised beveled warm-granite brick button (border L=6, B=6, R=6, T=6)."""
    h, w = 32, 48
    noise = fbm_granite(h, w, seed=505, octaves=((3, 4, 0.45), (6, 8, 0.30), (12, 16, 0.15), (24, 32, 0.10)))
    # Subtle top-to-bottom lighting gradient on raised brick face (#2F2D2A .. #3E3B37)
    grad_y = np.linspace(0.25, 0.0, h, dtype=np.float32)[:, None]
    t = np.clip(noise * 0.78 + grad_y, 0.0, 1.0)
    rgb = _lerp_rgb(_rgb_f("#2F2D2A"), _rgb_f("#3E3B37"), t)
    canvas = _to_rgba_u8(rgb)

    _draw_rect_ring(canvas, 0, "#100F0E", "#100F0E")
    _draw_rect_ring(canvas, 1, "#6B665F", "#0F0E0D")  # crisp bright TL chisel, deep BR shadow
    _draw_rect_ring(canvas, 2, "#4F4B45", "#1C1B19")
    _draw_rect_ring(canvas, 3, "#3D3A36", "#262422")
    _draw_rect_ring(canvas, 4, "#35322F", "#2B2926")
    _draw_rect_ring(canvas, 5, "#33302D", "#2E2C29")

    # Subtle tarnished-bronze corner pins strictly inside 6x6
    _stamp_symmetric_corners(canvas, _make_bronze_rivet_patch_6x6(pin_size=2))
    return canvas


def build_btn_gold_9s() -> np.ndarray:
    """48x32 hammered forged-gold/bronze primary CTA & active-tab button (border L=6, B=6, R=6, T=6)."""
    h, w = 32, 48
    noise = fbm_granite(h, w, seed=606, octaves=((3, 6, 0.50), (6, 12, 0.28), (12, 24, 0.14), (24, 32, 0.08)))
    # Horizontal brushed/hammered gold sheen + vertical burnish
    grad_y = np.linspace(0.32, 0.0, h, dtype=np.float32)[:, None]
    t = np.clip(noise * 0.72 + grad_y, 0.0, 1.0)
    rgb = _lerp_rgb(_rgb_f("#593E18"), _rgb_f("#7A5622"), t)
    canvas = _to_rgba_u8(rgb)

    _draw_rect_ring(canvas, 0, "#100F0E", "#100F0E")
    _draw_rect_ring(canvas, 1, "#F3CF74", "#261909")  # gleaming gold TL bevel, deep umber BR shadow
    _draw_rect_ring(canvas, 2, "#CFA046", "#3E2A10")
    _draw_rect_ring(canvas, 3, "#9E722E", "#4A3313")
    _draw_rect_ring(canvas, 4, "#2E1F0B", "#2E1F0B")  # fine dark bronze inner trim seam
    _draw_rect_ring(canvas, 5, "#876027", "#533915")

    # Gleaming corner rivets inside 6x6
    rivet = np.zeros((6, 6, 4), dtype=np.uint8)
    for y, x, c in [
        (1, 1, "#261909"), (1, 2, "#261909"), (1, 3, "#261909"), (1, 4, "#261909"),
        (2, 1, "#261909"), (2, 2, "#FFF2B2"), (2, 3, "#F3CF74"), (2, 4, "#261909"),
        (3, 1, "#261909"), (3, 2, "#F3CF74"), (3, 3, "#C99A48"), (3, 4, "#261909"),
        (4, 1, "#261909"), (4, 2, "#261909"), (4, 3, "#261909"), (4, 4, "#261909"),
    ]:
        _paint_pixel(rivet, y, x, c)
    _stamp_symmetric_corners(canvas, rivet)
    return canvas


def build_bar_track_9s() -> np.ndarray:
    """32x16 recessed carved stone gauge trough with hammered brass end-caps (border L=4, B=4, R=4, T=4)."""
    h, w = 16, 32
    noise = fbm_granite(h, w, seed=707, octaves=((2, 4, 0.50), (4, 8, 0.30), (8, 16, 0.20)))
    rgb = _lerp_rgb(_rgb_f("#11100F"), _rgb_f("#1A1917"), noise)
    canvas = _to_rgba_u8(rgb)

    # Top and bottom 4px stone gauge frame (stretches cleanly horizontally across x=4..27)
    for x in range(4, w - 4):
        _paint_pixel(canvas, 0, x, "#100F0E")
        _paint_pixel(canvas, 1, x, "#423F3A")
        _paint_pixel(canvas, 2, x, "#242220")
        _paint_pixel(canvas, 3, x, "#0B0A09")  # deep top inner shadow

        _paint_pixel(canvas, h - 4, x, "#4D4943")  # crisp bottom catch-light
        _paint_pixel(canvas, h - 3, x, "#22201E")
        _paint_pixel(canvas, h - 2, x, "#141312")
        _paint_pixel(canvas, h - 1, x, "#100F0E")

    # Left and right 4px hammered brass end-caps (border L=4, R=4)
    brass_cap = np.zeros((h, 4, 4), dtype=np.uint8)
    for y in range(h):
        _paint_pixel(brass_cap, y, 0, "#100F0E")
        _paint_pixel(brass_cap, y, 3, "#100F0E")
        if y == 0 or y == h - 1:
            _paint_pixel(brass_cap, y, 1, "#100F0E")
            _paint_pixel(brass_cap, y, 2, "#100F0E")
        elif y == 1:
            _paint_pixel(brass_cap, y, 1, "#F2CF78")
            _paint_pixel(brass_cap, y, 2, "#F2CF78")
        elif y == 2:
            _paint_pixel(brass_cap, y, 1, "#C99A48")
            _paint_pixel(brass_cap, y, 2, "#E3B85D")
        elif y == h - 2:
            _paint_pixel(brass_cap, y, 1, "#4F3715")
            _paint_pixel(brass_cap, y, 2, "#3B2810")
        elif y == h - 3:
            _paint_pixel(brass_cap, y, 1, "#8A632B")
            _paint_pixel(brass_cap, y, 2, "#6E4E20")
        else:
            # Hammered brass face + vertical rivet notch
            c1 = "#C99A48" if (y % 3 != 0) else "#DFB459"
            c2 = "#A67C35" if (y % 2 == 0) else "#8A632B"
            _paint_pixel(brass_cap, y, 1, c1)
            _paint_pixel(brass_cap, y, 2, c2)
    # Center stud on end-caps
    _paint_pixel(brass_cap, 7, 1, "#F2CF78")
    _paint_pixel(brass_cap, 8, 1, "#C99A48")

    canvas[:, 0:4] = brass_cap
    canvas[:, w - 4 : w] = np.fliplr(brass_cap)
    return canvas


def build_bar_fill_9s() -> np.ndarray:
    """
    16x16 neutral white-to-light-gray faceted gemstone bar fill (border L=2, B=2, R=2, T=2).
    Top specular glint + subtle vertical bevel shading so tinting looks like backlit stained glass.
    """
    h, w = 16, 16
    # Pure neutral grayscale row profile so horizontal 9-slice stretching is seamless
    row_luma = [
        142,  # y=0: dark facet rim
        255,  # y=1: top specular glint
        244,  # y=2: bright upper facet
        236,  # y=3
        228,  # y=4
        222,  # y=5: core upper
        214,  # y=6: core facet step
        210,  # y=7
        204,  # y=8
        198,  # y=9
        190,  # y=10
        182,  # y=11: lower facet
        174,  # y=12
        196,  # y=13: crisp bottom internal bounce glint
        160,  # y=14: lower bevel shadow
        128,  # y=15: bottom dark facet rim
    ]
    canvas = np.zeros((h, w, 4), dtype=np.uint8)
    for y, lum in enumerate(row_luma):
        canvas[y, :, :3] = lum
        canvas[y, :, 3] = 255

    # Left & right 2px faceted end bevels (strictly within L=2, R=2)
    for y in range(1, h - 1):
        # Left edge rim & bright chamfer
        _paint_pixel(canvas, y, 0, "#909090")
        canvas[y, 1, :3] = min(255, int(row_luma[y]) + 18)
        # Right edge shadow chamfer & dark rim
        canvas[y, w - 2, :3] = max(120, int(row_luma[y]) - 22)
        _paint_pixel(canvas, y, w - 1, "#7E7E7E")

    # Crisp corners
    for cy, cx in [(0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)]:
        _paint_pixel(canvas, cy, cx, "#707070")
    return canvas


def build_header_rule() -> np.ndarray:
    """
    128x8 tapered forged-gold ornamental horizontal rule with a tiny center diamond stud
    (intended 9-slice border L=16, B=0, R=16, T=0). Transparent background.
    """
    h, w = 8, 128
    canvas = np.zeros((h, w, 4), dtype=np.uint8)

    # Stretchable horizontal gold shaft across x=16..111 (rows y=2..5)
    for x in range(16, w - 16):
        _paint_pixel(canvas, 2, x, "#0E1016")
        _paint_pixel(canvas, 3, x, "#F2CF78")
        _paint_pixel(canvas, 4, x, "#C99A48")
        _paint_pixel(canvas, 5, x, "#0E1016")

    # Left 16px tapered ornamental finial (mirrored onto right 16px)
    finial_l = np.zeros((h, 16, 4), dtype=np.uint8)
    # Tapered spear tip (x=1..11) + forged collar (x=12..15)
    for x in range(1, 16):
        if x == 1:
            _paint_pixel(finial_l, 3, x, "#0E1016")
            _paint_pixel(finial_l, 4, x, "#0E1016")
        elif x <= 4:
            _paint_pixel(finial_l, 2, x, "#0E1016")
            _paint_pixel(finial_l, 3, x, "#C99A48")
            _paint_pixel(finial_l, 4, x, "#8A632B")
            _paint_pixel(finial_l, 5, x, "#0E1016")
        elif x <= 10:
            _paint_pixel(finial_l, 2, x, "#0E1016")
            _paint_pixel(finial_l, 3, x, "#F2CF78")
            _paint_pixel(finial_l, 4, x, "#C99A48")
            _paint_pixel(finial_l, 5, x, "#0E1016")
        elif x in (11, 12):
            # Flared ornamental collar
            _paint_pixel(finial_l, 1, x, "#0E1016")
            _paint_pixel(finial_l, 2, x, "#F2CF78")
            _paint_pixel(finial_l, 3, x, "#FFF1B8")
            _paint_pixel(finial_l, 4, x, "#C99A48")
            _paint_pixel(finial_l, 5, x, "#8A632B")
            _paint_pixel(finial_l, 6, x, "#0E1016")
        elif x == 13:
            _paint_pixel(finial_l, 2, x, "#0E1016")
            _paint_pixel(finial_l, 3, x, "#8A632B")
            _paint_pixel(finial_l, 4, x, "#5E431B")
            _paint_pixel(finial_l, 5, x, "#0E1016")
        else:
            _paint_pixel(finial_l, 2, x, "#0E1016")
            _paint_pixel(finial_l, 3, x, "#F2CF78")
            _paint_pixel(finial_l, 4, x, "#C99A48")
            _paint_pixel(finial_l, 5, x, "#0E1016")

    canvas[:, 0:16] = finial_l
    canvas[:, w - 16 : w] = np.fliplr(finial_l)

    # Tiny center diamond stud at x=60..67, y=0..7
    cx = 63.5
    cy = 3.5
    for y in range(h):
        for x in range(59, 69):
            dist = abs(x - cx) + abs(y - cy)
            if dist <= 3.1:
                if dist > 2.2:
                    _paint_pixel(canvas, y, x, "#0E1016")
                elif x <= cx and y <= cy:
                    _paint_pixel(canvas, y, x, "#FFF4BC")
                elif y <= cy:
                    _paint_pixel(canvas, y, x, "#F2CF78")
                elif x <= cx:
                    _paint_pixel(canvas, y, x, "#C99A48")
                else:
                    _paint_pixel(canvas, y, x, "#8A632B")
    return canvas


def build_gem_pip(variant: str) -> np.ndarray:
    """
    16x16 non-sliced carved stone square socket holding an unlit or glowing orb.
    variant in {'off', 'gold', 'cyan', 'emerald'}.
    """
    palettes = {
        "off": {
            "rim": "#22211F",
            "dark": "#343230",
            "mid": "#524F4A",
            "bright": "#78746D",
            "spec": "#9E9990",
            "glow_rim": "#3F3C38",
        },
        "gold": {
            "rim": "#543710",
            "dark": "#8F601E",
            "mid": "#CFA03E",
            "bright": "#F7D36E",
            "spec": "#FFF6C4",
            "glow_rim": "#7D5B26",
        },
        "cyan": {
            "rim": "#0F3842",
            "dark": "#1B6678",
            "mid": "#2CAEC2",
            "bright": "#62ECFA",
            "spec": "#D4FDFF",
            "glow_rim": "#255B66",
        },
        "emerald": {
            "rim": "#11381E",
            "dark": "#1E6E38",
            "mid": "#32AB56",
            "bright": "#70EB8D",
            "spec": "#D8FFE0",
            "glow_rim": "#235734",
        },
    }
    if variant not in palettes:
        raise ValueError(f"Unknown gem pip variant: {variant!r}")
    pal = palettes[variant]

    h, w = 16, 16
    canvas = np.zeros((h, w, 4), dtype=np.uint8)

    # Carved stone square socket with 1px chamfered corners
    for y in range(h):
        for x in range(w):
            # 1px chamfer at corners
            if (x in (0, w - 1)) and (y in (0, h - 1)):
                continue
            # Outer dark ink seam
            if x == 0 or x == w - 1 or y == 0 or y == h - 1 or (
                (x in (1, w - 2)) and (y in (1, h - 2))
            ):
                _paint_pixel(canvas, y, x, "#100F0E")
            # Bevel rim d=1
            elif x == 1 or y == 1:
                _paint_pixel(canvas, y, x, "#54504A")
            elif x == w - 2 or y == h - 2:
                _paint_pixel(canvas, y, x, "#1B1A18")
            # Recessed socket shadow d=2
            elif x == 2 or y == 2:
                _paint_pixel(canvas, y, x, "#0B0A09")
            elif x == w - 3 or y == h - 3:
                _paint_pixel(canvas, y, x, pal["glow_rim"])
            else:
                _paint_pixel(canvas, y, x, "#161514")

    # Circular gemstone orb centered at (7.5, 7.5)
    cx, cy = 7.5, 7.5
    for y in range(3, 13):
        for x in range(3, 13):
            dx = x - cx
            dy = y - cy
            r = float(np.hypot(dx, dy))
            if r > 4.65:
                continue
            if r > 3.9:
                _paint_pixel(canvas, y, x, "#100F0E")
                continue

            # Upper-left lighting projection
            light = (-0.65 * dx - 0.65 * dy) / 4.0 + (1.0 - r / 4.2) * 0.45
            if r < 1.7 and dx <= 0.1 and dy <= 0.1:
                _paint_pixel(canvas, y, x, pal["spec"])
            elif light > 0.36:
                _paint_pixel(canvas, y, x, pal["bright"])
            elif light > -0.04:
                _paint_pixel(canvas, y, x, pal["mid"])
            elif light > -0.45:
                _paint_pixel(canvas, y, x, pal["dark"])
            else:
                _paint_pixel(canvas, y, x, pal["rim"])

    # Crisp 1px top-left specular gleam inside orb
    _paint_pixel(canvas, 6, 6, pal["spec"])
    return canvas


def build_modal_scrim() -> np.ndarray:
    """
    320x180 non-sliced pixel-art radial vignette in deep charcoal #07090D
    using subtle 4x4 Bayer dithering (alpha ~0.58 center to ~0.84 edges)
    so Point-filtered 64 PPU stretching across 1920x1080 renders smoothly.
    """
    h, w = 180, 320
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx = (xx - (w - 1) * 0.5) / ((w - 1) * 0.5)
    dy = (yy - (h - 1) * 0.5) / ((h - 1) * 0.5)

    r_radial = np.clip(np.hypot(dx, dy), 0.0, 1.0)
    r_corner = np.clip(np.hypot(dx, dy) / np.sqrt(2.0), 0.0, 1.0)
    # Smoothstep radial curve: 0.58 at center, ~0.82 at edge midpoints, ~0.85 at corners
    s_rad = r_radial * r_radial * (3.0 - 2.0 * r_radial)
    s_cor = r_corner * r_corner * (3.0 - 2.0 * r_corner)
    bayer4 = (
        np.array(
            [
                [0, 8, 2, 10],
                [12, 4, 14, 6],
                [3, 11, 1, 9],
                [15, 7, 13, 5],
            ],
            dtype=np.float32,
        )
        / 16.0
        - 0.46875
    )
    dither = bayer4[np.arange(h)[:, None] % 4, np.arange(w)[None, :] % 4] * (4.0 / 255.0)
    alpha_f = 0.58 + 0.24 * s_rad + 0.03 * s_cor + dither
    alpha_u8 = np.clip(np.round(alpha_f * 255.0), 0, 255).astype(np.uint8)

    out = np.empty((h, w, 4), dtype=np.uint8)
    out[..., :3] = hex_to_rgb("#0A0908")
    out[..., 3] = alpha_u8
    return out


def slice_9(
    rgba: np.ndarray,
    target_w: int,
    target_h: int,
    border: Tuple[int, int, int, int],
) -> np.ndarray:
    """
    Stretch an RGBA image to (target_w, target_h) respecting Unity 9-slice border (L, B, R, T).
    Uses nearest-neighbor resampling for crisp pixel-art bevels and stone grain.
    """
    src_h, src_w, _ = rgba.shape
    l, b, r, t = border
    if l + r >= src_w or t + b >= src_h:
        raise ValueError(f"Invalid 9-slice border {border} for size {(src_w, src_h)}")

    out = np.zeros((target_h, target_w, 4), dtype=np.uint8)

    src_x_slices = [(0, l), (l, src_w - r), (src_w - r, src_w)]
    src_y_slices = [(0, t), (t, src_h - b), (src_h - b, src_h)]
    dst_x_slices = [(0, l), (l, target_w - r), (target_w - r, target_w)]
    dst_y_slices = [(0, t), (t, target_h - b), (target_h - b, target_h)]

    for sy, dy in zip(src_y_slices, dst_y_slices):
        sh = sy[1] - sy[0]
        dh = dy[1] - dy[0]
        if sh <= 0 or dh <= 0:
            continue
        for sx, dx in zip(src_x_slices, dst_x_slices):
            sw = sx[1] - sx[0]
            dw = dx[1] - dx[0]
            if sw <= 0 or dw <= 0:
                continue
            patch = rgba[sy[0] : sy[1], sx[0] : sx[1]]
            if (sw, sh) != (dw, dh):
                patch_img = Image.fromarray(patch, mode="RGBA").resize(
                    (dw, dh), resample=Image.Resampling.NEAREST
                )
                patch = np.array(patch_img, dtype=np.uint8)
            out[dy[0] : dy[1], dx[0] : dx[1]] = patch
    return out


def ensure_black_chancery_font(
    src_path: Path = DEFAULT_FONT_SRC,
    dst_path: Path = DEFAULT_UI_OUT_DIR / "BlackChancery.ttf",
) -> Path:
    """Ensure BlackChancery.ttf is copied to the Unity UI folder if missing or mismatched."""
    if not src_path.exists():
        raise FileNotFoundError(f"Source font missing: {src_path}")
    dst_path.parent.mkdir(parents=True, exist_ok=True)
    src_hash = hashlib.sha256(src_path.read_bytes()).hexdigest()
    needs_copy = True
    if dst_path.exists():
        dst_hash = hashlib.sha256(dst_path.read_bytes()).hexdigest()
        needs_copy = dst_hash != src_hash
    if needs_copy:
        shutil.copy2(src_path, dst_path)
    return dst_path


def build_all_assets() -> Dict[str, np.ndarray]:
    """Generate all 14 deterministic RGBA numpy arrays keyed by filename."""
    return {
        "ui_stone_panel_9s.png": build_stone_panel_9s(),
        "ui_stone_bar_9s.png": build_stone_bar_9s(),
        "ui_stone_well_9s.png": build_stone_well_9s(),
        "ui_tint_panel_9s.png": build_tint_panel_9s(),
        "ui_btn_stone_9s.png": build_btn_stone_9s(),
        "ui_btn_gold_9s.png": build_btn_gold_9s(),
        "ui_bar_track_9s.png": build_bar_track_9s(),
        "ui_bar_fill_9s.png": build_bar_fill_9s(),
        "ui_header_rule.png": build_header_rule(),
        "ui_gem_pip_off.png": build_gem_pip("off"),
        "ui_gem_pip_gold.png": build_gem_pip("gold"),
        "ui_gem_pip_cyan.png": build_gem_pip("cyan"),
        "ui_gem_pip_emerald.png": build_gem_pip("emerald"),
        "ui_modal_scrim.png": build_modal_scrim(),
    }


def _make_checkerboard(h: int, w: int, cell: int = 8) -> Image.Image:
    yy, xx = np.mgrid[0:h, 0:w]
    pat = ((xx // cell) + (yy // cell)) % 2
    bg = np.empty((h, w, 3), dtype=np.uint8)
    bg[pat == 0] = (32, 36, 46)
    bg[pat == 1] = (44, 49, 62)
    return Image.fromarray(bg, mode="RGB")


def _paste_over_bg(sheet: Image.Image, rgba: np.ndarray, x: int, y: int) -> None:
    fg = Image.fromarray(rgba, mode="RGBA")
    sheet.paste(fg, (x, y), fg)


def _tint_rgba(rgba: np.ndarray, hex_color: str) -> np.ndarray:
    r, g, b = hex_to_rgb(hex_color)
    out = rgba.copy().astype(np.float32)
    out[..., 0] *= r / 255.0
    out[..., 1] *= g / 255.0
    out[..., 2] *= b / 255.0
    return np.clip(np.round(out), 0, 255).astype(np.uint8)


def _load_ui_font(font_path: Optional[Path], size: int) -> ImageFont.ImageFont | ImageFont.FreeTypeFont:
    if font_path and font_path.exists():
        try:
            return ImageFont.truetype(str(font_path), size=size)
        except OSError:
            pass
    return ImageFont.load_default()


def build_contact_sheet(
    assets: Dict[str, np.ndarray],
    out_path: Path = DEFAULT_CONTACT_SHEET,
    font_path: Optional[Path] = None,
) -> Path:
    """
    Build a labeled inspection sheet showing each 9-slice asset at 1x, 2x, and 4x
    (stretched strictly via 9-slice borders), non-sliced pips/scrim, and a live
    composed UI mockup.
    """
    sheet_w, sheet_h = 1360, 1080
    sheet = Image.new("RGB", (sheet_w, sheet_h), (18, 21, 28))
    draw = ImageDraw.Draw(sheet)

    title_font = _load_ui_font(font_path, 26)
    label_font = ImageFont.load_default()

    draw.text((24, 16), "Realmz M1.15 - Chiseled Slate & Forged Gold UI Chrome Kit", fill=(242, 207, 118), font=title_font)
    draw.text(
        (24, 48),
        "9-Slice Verification Sheet: 1x Native | 2x Sliced | 4x Sliced (strict border preservation)",
        fill=(180, 188, 204),
        font=label_font,
    )

    y_cursor = 78
    for spec in UI_ASSET_SPECS:
        rgba = assets[spec.filename]
        if not spec.is_9slice:
            continue

        draw.text((24, y_cursor), f"{spec.filename}  -  {spec.label}", fill=(224, 214, 192), font=label_font)
        row_y = y_cursor + 18

        # 1x native
        _paste_over_bg(sheet, rgba, 24, row_y)
        x_next = 24 + spec.width + 20

        # 2x 9-sliced stretch
        w2 = min(256, spec.width * 2)
        h2 = min(84, spec.height * 2 if spec.border[1] + spec.border[3] > 0 else spec.height)
        s2 = slice_9(rgba, w2, h2, spec.border)
        _paste_over_bg(sheet, s2, x_next, row_y)
        x_next += w2 + 20

        # 4x 9-sliced stretch (wide horizontal & tall where applicable)
        w4 = min(400, spec.width * 4)
        h4 = min(96, spec.height * 2 if spec.border[1] + spec.border[3] > 0 else spec.height)
        s4 = slice_9(rgba, w4, h4, spec.border)
        _paste_over_bg(sheet, s4, x_next, row_y)

        y_cursor = row_y + max(spec.height, h2, h4) + 18

    # Right-hand column: Non-sliced Pips, Modal Scrim & Composed Mockup
    col_x = 960
    draw.text((col_x, 78), "Non-Sliced Gem Pips (1x & 4x)", fill=(242, 207, 118), font=label_font)
    pip_names = ["ui_gem_pip_off.png", "ui_gem_pip_gold.png", "ui_gem_pip_cyan.png", "ui_gem_pip_emerald.png"]
    py = 100
    for pname in pip_names:
        pip = assets[pname]
        _paste_over_bg(sheet, pip, col_x, py)
        pip_4x = np.array(Image.fromarray(pip, "RGBA").resize((64, 64), Image.Resampling.NEAREST), dtype=np.uint8)
        _paste_over_bg(sheet, pip_4x, col_x + 32, py)
        draw.text((col_x + 110, py + 24), pname.replace(".png", ""), fill=(195, 202, 216), font=label_font)
        py += 74

    # Modal scrim over checkerboard
    draw.text((col_x, py + 8), "ui_modal_scrim.png (over checkerboard)", fill=(242, 207, 118), font=label_font)
    scrim = assets["ui_modal_scrim.png"]
    scrim_2x = np.array(Image.fromarray(scrim, "RGBA").resize((128, 128), Image.Resampling.BILINEAR), dtype=np.uint8)
    cb = _make_checkerboard(128, 128, cell=8)
    cb.paste(Image.fromarray(scrim_2x, "RGBA"), (0, 0), Image.fromarray(scrim_2x, "RGBA"))
    sheet.paste(cb, (col_x, py + 28))

    # Tinted panel samples
    tint_y = py + 174
    draw.text((col_x, tint_y), "ui_tint_panel_9s.png Tinted Variants", fill=(242, 207, 118), font=label_font)
    tint_base = slice_9(assets["ui_tint_panel_9s.png"], 96, 36, (6, 6, 6, 6))
    for idx, (tname, thex) in enumerate([("Amber", "#D9A44E"), ("Crimson", "#B8484E"), ("Emerald", "#46A868")]):
        tinted = _tint_rgba(tint_base, thex)
        tx = col_x + idx * 108
        _paste_over_bg(sheet, tinted, tx, tint_y + 20)
        draw.text((tx + 12, tint_y + 32), tname, fill=(20, 20, 24), font=label_font)

    # Live Composed Modal Mockup
    mock_y = tint_y + 78
    draw.text((col_x, mock_y), "Composed M1.15 Modal Mockup", fill=(242, 207, 118), font=label_font)
    panel = slice_9(assets["ui_stone_panel_9s.png"], 320, 240, (10, 10, 10, 10))
    _paste_over_bg(sheet, panel, col_x, mock_y + 20)
    draw.text((col_x + 22, mock_y + 34), "Bywater Armory", fill=(242, 207, 118), font=_load_ui_font(font_path, 22))
    rule = slice_9(assets["ui_header_rule.png"], 284, 8, (16, 0, 16, 0))
    _paste_over_bg(sheet, rule, col_x + 18, mock_y + 62)

    well = slice_9(assets["ui_stone_well_9s.png"], 284, 72, (6, 6, 6, 6))
    _paste_over_bg(sheet, well, col_x + 18, mock_y + 78)

    # HP / SP / XP gauges inside well
    track = slice_9(assets["ui_bar_track_9s.png"], 200, 16, (4, 4, 4, 4))
    fill_base = assets["ui_bar_fill_9s.png"]
    for i, (g_col, g_w) in enumerate([("#3BC45D", 160), ("#35C2D6", 124), ("#F2CF78", 92)]):
        gy = mock_y + 88 + i * 20
        _paste_over_bg(sheet, track, col_x + 30, gy)
        fill = slice_9(_tint_rgba(fill_base, g_col), g_w, 10, (2, 2, 2, 2))
        _paste_over_bg(sheet, fill, col_x + 34, gy + 3)

    # Pips next to gauges
    for i, pname in enumerate(["ui_gem_pip_gold.png", "ui_gem_pip_cyan.png", "ui_gem_pip_emerald.png", "ui_gem_pip_off.png"]):
        _paste_over_bg(sheet, assets[pname], col_x + 236 + (i % 2) * 20, mock_y + 92 + (i // 2) * 22)

    # Stone & Gold buttons
    btn_s = slice_9(assets["ui_btn_stone_9s.png"], 130, 36, (6, 6, 6, 6))
    btn_g = slice_9(assets["ui_btn_gold_9s.png"], 130, 36, (6, 6, 6, 6))
    _paste_over_bg(sheet, btn_s, col_x + 22, mock_y + 196)
    _paste_over_bg(sheet, btn_g, col_x + 168, mock_y + 196)
    draw.text((col_x + 62, mock_y + 208), "Inspect", fill=(224, 228, 236), font=label_font)
    draw.text((col_x + 208, mock_y + 208), "Forge", fill=(255, 244, 188), font=label_font)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, format="PNG")
    return out_path


def generate_ui_chrome_kit(
    out_dir: Path = DEFAULT_UI_OUT_DIR,
    font_src: Path = DEFAULT_FONT_SRC,
    contact_sheet_path: Optional[Path] = DEFAULT_CONTACT_SHEET,
    copy_font: bool = True,
) -> List[Path]:
    """Generate all 14 UI chrome RGBA PNGs, copy font, and render inspection sheet."""
    out_dir.mkdir(parents=True, exist_ok=True)
    written: List[Path] = []

    font_dst: Optional[Path] = None
    if copy_font:
        font_dst = ensure_black_chancery_font(src_path=font_src, dst_path=out_dir / "BlackChancery.ttf")
        written.append(font_dst)

    assets = build_all_assets()
    for spec in UI_ASSET_SPECS:
        arr = assets[spec.filename]
        if arr.shape != (spec.height, spec.width, 4):
            raise RuntimeError(f"Asset {spec.filename} shape {arr.shape} != {(spec.height, spec.width, 4)}")
        dst = out_dir / spec.filename
        Image.fromarray(arr, mode="RGBA").save(dst, format="PNG")
        written.append(dst)

    if contact_sheet_path is not None:
        sheet = build_contact_sheet(assets, out_path=contact_sheet_path, font_path=font_dst or font_src)
        written.append(sheet)

    return written


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Generate Realmz M1.15 9-slice Stone & Forged-Gold UI Chrome Kit.")
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_UI_OUT_DIR)
    parser.add_argument("--font-src", type=Path, default=DEFAULT_FONT_SRC)
    parser.add_argument("--contact-sheet", type=Path, default=DEFAULT_CONTACT_SHEET)
    parser.add_argument("--skip-font", action="store_true")
    parser.add_argument("--skip-contact-sheet", action="store_true")
    args = parser.parse_args(argv)

    written = generate_ui_chrome_kit(
        out_dir=args.out_dir,
        font_src=args.font_src,
        contact_sheet_path=None if args.skip_contact_sheet else args.contact_sheet,
        copy_font=not args.skip_font,
    )
    print(f"Generated {len(written)} UI Chrome Kit artifacts in {args.out_dir}")
    for path in written:
        print(f"  - {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
