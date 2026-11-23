"""
Final 3-item Phase 1 micro-polish:
1. Remove bottom grey shadow bar under tree_pine_a.png trunk.
2. Re-roll tree_oak_b without weird yellow sun-orb circles on branch tips.
3. Re-roll stone_fence_v as a low compact mossy village cobblestone post + hedge block
   matching stone_fence_h (not a tall classical column).
"""

from __future__ import annotations

import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

CART_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(CART_DIR))
import fix_trees_and_fence as ftf  # noqa: E402
import generate_cartography_slice as gs  # noqa: E402
import pixel_postprocess as pp  # noqa: E402


def clean_pine_a_bottom_bar() -> None:
    p = gs.UNITY_CART_ROOT / "trees" / "tree_pine_a.png"
    pn = gs.UNITY_CART_ROOT / "trees" / "tree_pine_a_n.png"
    arr = np.array(Image.open(p).convert("RGBA"), dtype=np.uint8)
    h, w, _ = arr.shape
    fg = arr[..., 3] > 0
    ys = np.where(fg)[0]
    y_bot = int(ys.max())
    # Inspect waist of trunk (y_bot-24 .. y_bot-10)
    waist = [int(np.sum(fg[y])) for y in range(max(0, y_bot - 26), max(1, y_bot - 10)) if np.any(fg[y])]
    trunk_w = max(14.0, float(np.median(waist)) if waist else 26.0)
    removed = 0
    for y in range(max(0, y_bot - 12), y_bot + 1):
        xs = np.where(fg[y])[0]
        if len(xs) > trunk_w * 1.45:
            # Clear wide pale gray/blue ground bar pixels outside trunk
            cx = w / 2.0
            out_trunk = np.abs(np.arange(w) - cx) > (trunk_w * 0.72)
            # Also check pale blue-grey colors (R,G,B near gray and B >= R)
            rgb = arr[y, :, :3].astype(np.int32)
            pale_bar = (rgb[:, 2] >= rgb[:, 0] - 5) & (rgb.mean(axis=1) > 120)
            kill = out_trunk | (pale_bar & (np.abs(np.arange(w) - cx) > trunk_w * 0.48))
            removed += int(np.sum(kill & fg[y]))
            arr[y, kill] = [0, 0, 0, 0]
    arr = ftf.keep_major_components(arr, min_frac=0.05)
    Image.fromarray(arr, mode="RGBA").save(p)
    norm_arr, _ = pp.generate_normal_map(arr, periodic=False)
    Image.fromarray(norm_arr).save(pn)
    print(f"[tree_pine_a] cleared bottom shadow bar pixels={removed}")


def paint_low_stone_fence_post(size: int = 1024) -> tuple[Path, Path]:
    """Low chunky mossy cobblestone village fence pier + receding hedge wall on #FFFFFF."""
    rng = np.random.default_rng(304615)
    im = Image.new("RGB", (size, size), (255, 255, 255))
    draw = ImageDraw.Draw(im)
    depth = np.zeros((size, size), dtype=np.float32)

    cx = size // 2
    # Compact chunky lower-half courtyard stone post + stub mossy wall block (height ~55% of canvas)
    # Matching slate-blue/sandstone tones of stone_fence_h!
    draw.rectangle([cx - 210, 460, cx + 210, 895], fill=(104, 108, 134))
    cv2.rectangle(depth, (cx - 210, 460), (cx + 210, 895), 0.72, -1)

    # Top angled receding coping stones suggesting north-south extension into depth
    draw.polygon([(cx - 175, 340), (cx + 175, 340), (cx + 210, 520), (cx - 210, 520)], fill=(134, 140, 166))
    cv2.fillPoly(depth, [np.array([(cx - 175, 340), (cx + 175, 340), (cx + 210, 520), (cx - 210, 520)], np.int32)], 0.84)

    # Individual brick courses & sunlit top slabs
    for y in range(365, 860, 68):
        for x in range(cx - 190, cx + 170, 96):
            shade = int(rng.integers(-18, 22))
            draw.rectangle([x, y, x + 84, y + 54], fill=(138 + shade, 142 + shade, 168 + shade), outline=(66, 68, 92), width=4)

    # Lush emerald ivy bush clusters & golden-green moss hugging left/right sides
    for mx, my, mr in [(cx - 150, 520, 48), (cx + 145, 550, 46), (cx - 95, 810, 55), (cx + 110, 795, 50), (cx + 15, 420, 44)]:
        draw.ellipse([mx - mr, my - mr, mx + mr, my + mr], fill=(74, 142, 62))
        cv2.circle(depth, (mx, my), mr, 0.88, -1)

    return ftf._soft_guides_from_painting("stone_fence_v_low", im, depth, blur_init=22.0, blur_depth=16.0)


def main() -> int:
    clean_pine_a_bottom_bar()

    oak_init, oak_depth = ftf.paint_mottled_oak_b(seed=909)
    ftf.run_item(
        name="tree_oak_b",
        rel_subdir="trees",
        out_size=(224, 224),
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, simple background, "
            "pure solid white background, lush hand-pixeled medieval sunlit broadleaf oak tree, dense crisp green "
            "leafy foliage clusters only, sunlit lime green crown, rich emerald midtones, deep cool teal shadows, "
            "gnarled brown bark trunk and roots, crisp dark outlines, Songs of Conquest oak tree style"
        ),
        extra_neg=(
            "yellow circles, glowing orbs, orange balls, fruit, apples, lemons, golden spheres, flat dots, "
            "flat smooth circles, forest horizon, sky, dirt mound, yellow blotches"
        ),
        seed=304622,
        p_init=oak_init,
        p_depth=oak_depth,
        denoise=0.92,
        depth_strength=0.44,
        depth_end=0.58,
        is_tree=True,
    )

    fv_init, fv_depth = paint_low_stone_fence_post()
    ftf.run_item(
        name="stone_fence_v",
        rel_subdir="structures",
        out_size=(128, 128),
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo prop, simple background, "
            "pure solid white background, isolated top-down 3/4 oblique medieval village low garden wall segment "
            "seen foreshortened end-on, chunky weathered blue-grey slate and sandstone cobblestones, flat square stone "
            "cap, green hedge shrub and emerald ivy tucked against wall base, crisp dark purple-slate pixel ink "
            "outlines, matches stone fence height, Songs of Conquest village courtyard prop"
        ),
        extra_neg=(
            "tall Greek pillar, marble column, temple pillar, obelisk, chimney, tower, pedestal base, smooth vector, "
            "forest background, sky, grass field, dirt ground"
        ),
        seed=304615,
        p_init=fv_init,
        p_depth=fv_depth,
        denoise=0.91,
        depth_strength=0.45,
        depth_end=0.58,
        is_tree=False,
    )

    gs.make_contact_sheets()
    print(f"Final contact sheet saved. VRAM: {gs.query_vram_mib()} MiB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
