"""
Generate calm, Songs-of-Conquest-faithful 128x128 seamless ground tiles (64 px/m)
and orthographic Depth/Canny silhouette guides for Bywater showcase trees.

Outputs:
  - Assets/Art/Cartography/tiles/*.png + *_n.png
  - scratch/realmz_soc_vertical_slice/whitebox_guides/tree_*_{depth,canny}.png
  - scratch/realmz_soc_vertical_slice/ground_tiles_2x2.png

Zero Unicode em-dashes or en-dashes. Uses CREATE_NO_WINDOW = 0x08000000.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path
from typing import Tuple

import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

CART_DIR = Path(__file__).resolve().parent
PIPE_DIR = CART_DIR.parent
UNITY_TILES = Path(r"C:\realmz\remaster\RealmzUnity\Assets\Art\Cartography\tiles")
SCRATCH_ROOT = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_soc_vertical_slice")
GUIDE_DIR = SCRATCH_ROOT / "whitebox_guides"
PALETTE_PATH = PIPE_DIR / "palette_soc_sunlit_128.json"

sys.path.insert(0, str(CART_DIR))
import pixel_postprocess as pp  # noqa: E402


def hex_rgb(hx: str) -> np.ndarray:
    h = hx.strip().lstrip("#")
    return np.array([int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)], dtype=np.uint8)


def periodic_fbm(size: int, seed: int, octaves: Tuple[Tuple[int, float], ...]) -> np.ndarray:
    rng = np.random.default_rng(seed)
    acc = np.zeros((size, size), dtype=np.float32)
    w_sum = 0.0
    for cell, weight in octaves:
        gh = max(2, size // cell)
        grid = rng.uniform(0.0, 1.0, size=(gh, gh)).astype(np.float32)
        tiled = np.tile(grid, (3, 3))
        up3 = cv2.resize(tiled, (size * 3, size * 3), interpolation=cv2.INTER_CUBIC)
        up = up3[size : 2 * size, size : 2 * size]
        acc += up * weight
        w_sum += weight
    acc /= max(1e-5, w_sum)
    lo, hi = float(acc.min()), float(acc.max())
    return np.clip((acc - lo) / max(1e-5, hi - lo), 0.0, 1.0)


def save_tile_and_normal(name: str, rgb: np.ndarray, pal: np.ndarray, noise_seed: int, alpha_min: int = 64) -> dict:
    rgba = np.zeros((128, 128, 4), dtype=np.uint8)
    rgba[..., :3] = rgb
    rgba[..., 3] = 255
    quant = pp.apply_sunlit_grade_and_quantize(
        rgba,
        pal,
        dither_strength=0.0,
        grade_strength=0.0,
        shadow_lift=0.0,
        vibrance_boost=1.0,
        preserve_alpha=False,
    )
    norm, backend = pp.generate_normal_map(quant, periodic=True, normal_backend="auto")
    if alpha_min < 255:
        quant[..., 3] = pp.generate_alpha_transition_mask(
            quant[..., :3],
            seed=noise_seed,
            min_alpha=alpha_min,
            max_alpha=255,
        )
    else:
        quant[..., 3] = 255

    out_a = UNITY_TILES / f"{name}.png"
    out_n = UNITY_TILES / f"{name}_n.png"
    out_a.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(quant, "RGBA").save(out_a, "PNG")
    Image.fromarray(norm, "RGBA").save(out_n, "PNG")
    st = pp.validate_outputs(quant, norm, pal, is_tile=True)
    st["normal_backend"] = backend
    return st


def build_meadow_grass(pal: np.ndarray) -> dict:
    """Calm 4-tone SoC emerald/sage pixel turf with subtle 2-3px organic grass clusters (no static)."""
    sz = 128
    rng = np.random.default_rng(40101)
    c_deep = hex_rgb("#1F3324")
    c_dark = hex_rgb("#224D2E")
    c_mid = hex_rgb("#2E693B")
    c_moss = hex_rgb("#426643")
    c_hi = hex_rgb("#5A8254")

    f = periodic_fbm(sz, 40101, ((32, 0.55), (16, 0.30), (8, 0.15)))
    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    rgb[f < 0.28] = c_dark
    rgb[(f >= 0.28) & (f < 0.62)] = c_mid
    rgb[(f >= 0.62) & (f < 0.84)] = c_moss
    rgb[f >= 0.84] = c_hi

    # Subtle 2-3px clustered blade flecks (wrapped toroidally)
    for _ in range(190):
        x = int(rng.integers(0, sz))
        y = int(rng.integers(0, sz))
        tone = c_deep if rng.random() < 0.45 else c_hi
        bh = int(rng.integers(2, 4))
        for dy in range(bh):
            rgb[(y - dy) % sz, x % sz] = tone
        if rng.random() < 0.4:
            rgb[(y - 1) % sz, (x + 1) % sz] = tone
    return save_tile_and_normal("meadow_grass", rgb, pal, noise_seed=101)


def build_moss_patch(pal: np.ndarray) -> dict:
    sz = 128
    rng = np.random.default_rng(40102)
    c_shadow = hex_rgb("#183622")
    c_deep = hex_rgb("#1F3324")
    c_mid = hex_rgb("#2E4A33")
    c_light = hex_rgb("#426643")

    f = periodic_fbm(sz, 40102, ((32, 0.50), (16, 0.32), (8, 0.18)))
    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    rgb[f < 0.25] = c_shadow
    rgb[(f >= 0.25) & (f < 0.58)] = c_deep
    rgb[(f >= 0.58) & (f < 0.85)] = c_mid
    rgb[f >= 0.85] = c_light

    for _ in range(120):
        x = int(rng.integers(0, sz))
        y = int(rng.integers(0, sz))
        col = c_light if rng.random() < 0.5 else c_shadow
        rgb[y % sz, x % sz] = col
        rgb[y % sz, (x + 1) % sz] = col
    return save_tile_and_normal("moss_patch", rgb, pal, noise_seed=102)


def build_dirt_road(pal: np.ndarray) -> dict:
    sz = 128
    rng = np.random.default_rng(40103)
    c_dark = hex_rgb("#4D3C2E")
    c_mid = hex_rgb("#6B5542")
    c_warm = hex_rgb("#8A7058")
    c_hi = hex_rgb("#AA8D70")

    f = periodic_fbm(sz, 40103, ((32, 0.55), (16, 0.30), (6, 0.15)))
    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    rgb[f < 0.24] = c_dark
    rgb[(f >= 0.24) & (f < 0.62)] = c_mid
    rgb[(f >= 0.62) & (f < 0.88)] = c_warm
    rgb[f >= 0.88] = c_hi

    # Small rounded 2x1 / 2x2 pixel pebbles
    for _ in range(95):
        x = int(rng.integers(0, sz))
        y = int(rng.integers(0, sz))
        p_col = c_hi if rng.random() < 0.55 else c_dark
        rgb[y % sz, x % sz] = p_col
        rgb[y % sz, (x + 1) % sz] = p_col
        if rng.random() < 0.35:
            rgb[(y + 1) % sz, x % sz] = c_dark
    return save_tile_and_normal("dirt_road", rgb, pal, noise_seed=103)


def build_cobble_plaza(pal: np.ndarray) -> dict:
    """Crisp flat top-down Voronoi cobblestones in warm_cobble_gray + subtle sunlit sandstone/moss seams."""
    sz = 128
    rng = np.random.default_rng(40104)
    # Jittered periodic grid of stone centers (~8x8 irregular cobbles across 128px -> 16px per stone!)
    gx, gy = 8, 8
    pts = []
    for j in range(gy):
        for i in range(gx):
            px = (i + 0.18 + rng.random() * 0.64) / gx
            py = (j + 0.18 + rng.random() * 0.64) / gy
            pts.append((px, py))
    pts_arr = np.array(pts, dtype=np.float32)

    yy, xx = np.mgrid[0:sz, 0:sz] / float(sz)
    d1 = np.full((sz, sz), 1e9, dtype=np.float32)
    d2 = np.full((sz, sz), 1e9, dtype=np.float32)
    sid = np.zeros((sz, sz), dtype=np.int32)
    for idx, (px, py) in enumerate(pts_arr):
        dx = np.abs(xx - px)
        dx = np.minimum(dx, 1.0 - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, 1.0 - dy)
        d = np.sqrt(dx * dx + dy * dy)
        m1 = d < d1
        m2 = (~m1) & (d < d2)
        d2 = np.where(m1, d1, np.where(m2, d, d2))
        d1 = np.where(m1, d, d1)
        sid = np.where(m1, idx, sid)

    edge_dist = d2 - d1
    mortar = edge_dist < 0.017
    bevel_top = np.zeros((sz, sz), dtype=bool)
    bevel_bot = np.zeros((sz, sz), dtype=bool)
    # Compute pseudo sun highlight on upper-left edges of each stone
    sid_up = np.roll(sid, 1, axis=0)
    sid_lf = np.roll(sid, 1, axis=1)
    sid_dn = np.roll(sid, -1, axis=0)
    sid_rt = np.roll(sid, -1, axis=1)
    bevel_top = (~mortar) & ((np.roll(mortar, 1, axis=0)) | (np.roll(mortar, 1, axis=1)))
    bevel_bot = (~mortar) & ((np.roll(mortar, -1, axis=0)) | (np.roll(mortar, -1, axis=1)))

    c_mortar = hex_rgb("#3B3837")
    c_mortar_moss = hex_rgb("#2E4A33")
    stone_tones = [
        hex_rgb("#6B6661"),
        hex_rgb("#87817A"),
        hex_rgb("#8A7058"),
        hex_rgb("#A49E95"),
    ]
    stone_hi = hex_rgb("#C2BCB2")
    stone_sh = hex_rgb("#524E4B")

    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    tone_idx = rng.integers(0, len(stone_tones), size=len(pts_arr))
    for i in range(len(pts_arr)):
        rgb[sid == i] = stone_tones[tone_idx[i]]

    # Add pixel bevels & subtle interior stone texture
    rgb[bevel_bot] = stone_sh
    rgb[bevel_top] = stone_hi
    noise = periodic_fbm(sz, 40199, ((8, 0.6), (4, 0.4)))
    speck = (~mortar) & (~bevel_top) & (~bevel_bot) & (noise > 0.78)
    rgb[speck] = stone_hi

    moss_noise = periodic_fbm(sz, 40188, ((16, 0.7), (8, 0.3)))
    rgb[mortar] = c_mortar
    rgb[mortar & (moss_noise > 0.68)] = c_mortar_moss

    return save_tile_and_normal("cobble_plaza", rgb, pal, noise_seed=104)


def build_river_mud(pal: np.ndarray) -> dict:
    """Calm wet dark earth and mossy bank stones (zero bright terracotta red)."""
    sz = 128
    rng = np.random.default_rng(40105)
    c_deep = hex_rgb("#2E1B11")
    c_mid = hex_rgb("#472A1A")
    c_warm = hex_rgb("#4D3C2E")
    c_moss = hex_rgb("#2E4A33")
    c_pebble = hex_rgb("#6B6661")

    f = periodic_fbm(sz, 40105, ((32, 0.55), (16, 0.30), (8, 0.15)))
    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    rgb[f < 0.32] = c_deep
    rgb[(f >= 0.32) & (f < 0.68)] = c_mid
    rgb[(f >= 0.68) & (f < 0.86)] = c_warm
    rgb[f >= 0.86] = c_moss

    for _ in range(55):
        x = int(rng.integers(0, sz))
        y = int(rng.integers(0, sz))
        rgb[y % sz, x % sz] = c_pebble
        rgb[y % sz, (x + 1) % sz] = c_pebble
    return save_tile_and_normal("river_mud", rgb, pal, noise_seed=105)


def build_cliff_rock(pal: np.ndarray) -> dict:
    """Crisp SoC vertical stepped rock strata ledges & crevices."""
    sz = 128
    rng = np.random.default_rng(40106)
    c_crevice = hex_rgb("#232B42")
    c_dark = hex_rgb("#3B3837")
    c_mid = hex_rgb("#524E4B")
    c_stone = hex_rgb("#6B6661")
    c_hi = hex_rgb("#87817A")
    c_moss = hex_rgb("#2E4A33")
    c_moss_hi = hex_rgb("#426643")

    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    f = periodic_fbm(sz, 40106, ((16, 0.6), (8, 0.4)))
    rgb[f < 0.35] = c_dark
    rgb[(f >= 0.35) & (f < 0.72)] = c_mid
    rgb[f >= 0.72] = c_stone

    # Horizontal strata shelves + jagged vertical fissures
    for y_base in (14, 42, 74, 104):
        for x in range(sz):
            wy = (y_base + int(round(math.sin(x * 0.14 + y_base) * 2.0))) % sz
            rgb[wy, x] = c_crevice
            rgb[(wy + 1) % sz, x] = c_crevice
            rgb[(wy + 2) % sz, x] = c_hi
            if rng.random() < 0.16:
                rgb[(wy + 2) % sz, x] = c_moss_hi
                rgb[(wy + 3) % sz, x] = c_moss

    # Vertical stone fractures (organic offset fissures, not a rectangular brick grid)
    for x_start in (20, 56, 94):
        cx = x_start
        for y in range(sz):
            if y % 4 == 0:
                cx = (cx + int(rng.choice([-1, 0, 1]))) % sz
            if (y % 42) < 28:
                rgb[y, cx] = c_crevice
                rgb[y, (cx + 1) % sz] = c_dark
    return save_tile_and_normal("cliff_rock", rgb, pal, noise_seed=106, alpha_min=180)


def build_shallow_water(pal: np.ndarray) -> dict:
    sz = 128
    rng = np.random.default_rng(40107)
    c_deep = hex_rgb("#1A4E75")
    c_mid = hex_rgb("#226A9C")
    c_light = hex_rgb("#2D88C2")
    c_shimmer = hex_rgb("#3FA8E0")

    f = periodic_fbm(sz, 40107, ((32, 0.5), (16, 0.35), (8, 0.15)))
    rgb = np.zeros((sz, sz, 3), dtype=np.uint8)
    rgb[f < 0.30] = c_deep
    rgb[(f >= 0.30) & (f < 0.70)] = c_mid
    rgb[f >= 0.70] = c_light

    for _ in range(75):
        x = int(rng.integers(0, sz))
        y = int(rng.integers(0, sz))
        wlen = int(rng.integers(3, 8))
        for dx in range(wlen):
            rgb[y % sz, (x + dx) % sz] = c_shimmer if dx % 3 != 0 else c_light
    return save_tile_and_normal("shallow_water", rgb, pal, noise_seed=107, alpha_min=255)


def make_tree_silhouette_guides() -> None:
    """Generate clean 1024x1024 organic orthographic Depth + Canny guides for 5 trees + 3/4 stone_fence_v."""
    GUIDE_DIR.mkdir(parents=True, exist_ok=True)
    specs = {
        "tree_oak_a": ("oak_round", 4001),
        "tree_oak_b": ("oak_twin", 4002),
        "tree_pine_a": ("pine_broad", 4003),
        "tree_pine_b": ("pine_slender", 4004),
        "tree_willow_a": ("willow", 4005),
    }
    for name, (kind, seed) in specs.items():
        depth = np.zeros((1024, 1024), dtype=np.uint8)
        rng = np.random.default_rng(seed)

        # Clean trunk cut at y=910 with zero dirt mound
        if kind in ("oak_round", "oak_twin", "willow"):
            tw = 38 if kind != "willow" else 44
            cv2.rectangle(depth, (512 - tw, 500), (512 + tw, 910), 145, -1)
            cv2.rectangle(depth, (512 - tw // 2, 500), (512 + tw // 2, 910), 175, -1)
        else:
            cv2.rectangle(depth, (512 - 24, 620), (512 + 24, 910), 145, -1)

        if kind == "oak_round":
            blobs = [
                (512, 355, 195, 225),
                (350, 425, 135, 200),
                (674, 425, 135, 200),
                (440, 235, 130, 215),
                (590, 245, 125, 215),
                (512, 460, 155, 245),
            ]
            for cx, cy, r, val in blobs:
                for _ in range(9):
                    ox = int(rng.integers(-35, 36))
                    oy = int(rng.integers(-28, 29))
                    rr = int(r * rng.uniform(0.55, 0.85))
                    cv2.circle(depth, (cx + ox, cy + oy), rr, val, -1)
        elif kind == "oak_twin":
            blobs = [
                (410, 380, 175, 220),
                (615, 325, 168, 235),
                (520, 470, 150, 245),
                (310, 470, 110, 195),
                (715, 445, 118, 205),
            ]
            for cx, cy, r, val in blobs:
                for _ in range(8):
                    ox = int(rng.integers(-30, 31))
                    oy = int(rng.integers(-25, 26))
                    rr = int(r * rng.uniform(0.55, 0.85))
                    cv2.circle(depth, (cx + ox, cy + oy), rr, val, -1)
        elif kind == "pine_broad":
            tiers = [
                (140, 330, 80, 205),
                (260, 480, 165, 220),
                (400, 650, 235, 235),
                (550, 800, 270, 250),
            ]
            for y0, y1, half_w, val in tiers:
                pts = np.array([[512, y0], [512 - half_w, y1], [512 + half_w, y1]], dtype=np.int32)
                cv2.fillPoly(depth, [pts], val)
                for k in range(7):
                    bx = int(512 - half_w * 0.8 + k * (half_w * 1.6 / 6.0))
                    cv2.circle(depth, (bx, y1 - 8), int(rng.integers(26, 44)), val, -1)
        elif kind == "pine_slender":
            tiers = [
                (110, 280, 62, 210),
                (210, 410, 115, 220),
                (340, 560, 155, 235),
                (470, 710, 190, 245),
                (610, 820, 205, 250),
            ]
            for y0, y1, half_w, val in tiers:
                pts = np.array([[512, y0], [512 - half_w, y1], [512 + half_w, y1]], dtype=np.int32)
                cv2.fillPoly(depth, [pts], val)
                for k in range(6):
                    bx = int(512 - half_w * 0.78 + k * (half_w * 1.56 / 5.0))
                    cv2.circle(depth, (bx, y1 - 6), int(rng.integers(22, 36)), val, -1)
        elif kind == "willow":
            for cx, cy, r, val in [(512, 320, 215, 220), (370, 395, 145, 215), (654, 395, 145, 215)]:
                cv2.circle(depth, (cx, cy), r, val, -1)
            # Soft arched organic weeping drape clusters
            for x in range(255, 775, 28):
                drop = int(rng.integers(200, 410))
                pts = np.array(
                    [
                        [x - 16, 360],
                        [x + 16, 360],
                        [x + 6 + int(rng.integers(-8, 9)), min(850, 360 + drop)],
                        [x - 6 + int(rng.integers(-8, 9)), min(850, 360 + drop)],
                    ],
                    dtype=np.int32,
                )
                cv2.fillPoly(depth, [pts], 235)

        depth_blur = cv2.GaussianBlur(depth, (17, 17), 0)
        mask = depth > 0
        depth_out = np.where(mask, np.clip(depth_blur, 90, 255), 0).astype(np.uint8)
        canny_out = cv2.Canny((depth > 0).astype(np.uint8) * 255, 50, 150)
        canny_out = cv2.dilate(canny_out, np.ones((3, 3), np.uint8), iterations=1)

        Image.fromarray(depth_out, "L").convert("RGB").save(GUIDE_DIR / f"{name}_depth.png")
        Image.fromarray(canny_out, "L").convert("RGB").save(GUIDE_DIR / f"{name}_canny.png")

    # Also build a 3/4 oblique low stone wall guide for stone_fence_v (avoiding straight-on pole)
    f_depth = np.zeros((1024, 1024), dtype=np.uint8)
    f_canny = np.zeros((1024, 1024), dtype=np.uint8)
    # Oblique receding wall from bottom-left (310, 790) to mid-right (710, 560)
    wall_poly = np.array([[320, 620], [700, 420], [700, 630], [320, 830]], dtype=np.int32)
    top_poly = np.array([[295, 600], [675, 400], [700, 420], [320, 620]], dtype=np.int32)
    cv2.fillPoly(f_depth, [wall_poly], 185)
    cv2.fillPoly(f_depth, [top_poly], 215)
    # Front end stone pillar
    cv2.rectangle(f_depth, (250, 560), (350, 845), 235, -1)
    cv2.rectangle(f_depth, (240, 530), (360, 565), 245, -1)
    # Far end stone pillar
    cv2.rectangle(f_depth, (655, 385), (735, 645), 160, -1)
    cv2.rectangle(f_depth, (648, 360), (742, 390), 172, -1)
    cv2.polylines(f_canny, [wall_poly, top_poly], True, 255, 4)
    cv2.rectangle(f_canny, (250, 560), (350, 845), 255, 4)
    cv2.rectangle(f_canny, (240, 530), (360, 565), 255, 4)
    cv2.rectangle(f_canny, (655, 385), (735, 645), 255, 4)
    cv2.rectangle(f_canny, (648, 360), (742, 390), 255, 4)
    Image.fromarray(f_depth, "L").convert("RGB").save(GUIDE_DIR / "stone_fence_v_depth.png")
    Image.fromarray(f_canny, "L").convert("RGB").save(GUIDE_DIR / "stone_fence_v_canny.png")
    print("Saved 5 organic tree + stone_fence_v Depth+Canny guides to", GUIDE_DIR)


def main() -> None:
    pal = pp.load_palette(PALETTE_PATH)
    builders = [
        build_meadow_grass,
        build_moss_patch,
        build_dirt_road,
        build_cobble_plaza,
        build_river_mud,
        build_cliff_rock,
        build_shallow_water,
    ]
    for fn in builders:
        st = fn(pal)
        print(f"Tile {fn.__name__}: off_pal={st['off_palette_colors']} seam={st['seam_ratio']} normal={st['normal_backend']}")
    make_tree_silhouette_guides()

    # Rebuild 2x2 contact sheet
    tile_names = ["meadow_grass", "moss_patch", "dirt_road", "cobble_plaza", "river_mud", "cliff_rock", "shallow_water"]
    tw, th = 256, 256
    cols = 4
    rows = 2
    pad = 16
    sheet = Image.new("RGB", (cols * (tw + pad) + pad, rows * (th + 42 + pad) + pad + 44), (20, 22, 30))
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 12), "PHASE 1 CALM SOC-STYLE GROUND TILES (128x128 @ 2x2 Tiled, 64 px/m Standard)", fill=(244, 198, 92))
    for idx, tname in enumerate(tile_names):
        p = UNITY_TILES / f"{tname}.png"
        im = Image.open(p).convert("RGB")
        tiled = Image.new("RGB", (256, 256))
        for ry in range(2):
            for rx in range(2):
                tiled.paste(im, (rx * 128, ry * 128))
        c = idx % cols
        r = idx // cols
        x = pad + c * (tw + pad)
        y = 48 + pad + r * (th + 42 + pad)
        draw.rectangle([x - 2, y - 2, x + tw + 2, y + th + 32], fill=(30, 35, 48), outline=(74, 86, 114))
        draw.text((x + 6, y + 4), tname, fill=(238, 241, 248))
        sheet.paste(tiled, (x, y + 26))
    sheet.save(SCRATCH_ROOT / "ground_tiles_2x2.png")
    print("Updated ground_tiles_2x2.png")


if __name__ == "__main__":
    import math

    main()
