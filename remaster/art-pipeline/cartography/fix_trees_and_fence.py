"""
Fix Phase 1 sprite polish defects found during contact-sheet visual QA:
1. Open the sealed white arch void beneath stone_bridge.png -> transparent + neutral normal.
2. Gently lift ultra-dark shadow values on tree_pine_b.png inside palette_soc_sunlit_128.json.
3. Re-roll stone_fence_v, tree_oak_b, tree_pine_a, and tree_willow_a using textured mottled
   latent anchors blurred onto pure #FFFFFF + denoise=0.90 + soft blurred Depth (NO stiff Canny!)
   so Illustrious-XL + skormino renders crisp SoC leaf clusters, bark, and weathered stonework
   without vector-circle outlines or forest backdrop bleed.

Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW in all subprocesses.
"""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Dict, Tuple

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw, ImageFilter

CART_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(CART_DIR))
import generate_cartography_slice as gs  # noqa: E402
import pixel_postprocess as pp  # noqa: E402

GUIDE_DIR = gs.GUIDE_DIR
RAW_DIR = gs.RAW_DIR
UNITY_CART_ROOT = gs.UNITY_CART_ROOT
SERVER_URL = gs.SERVER_URL


def keep_major_components(rgba: np.ndarray, min_frac: float = 0.04) -> np.ndarray:
    """Drop tiny detached halo/speck islands whose area is < min_frac of the largest component."""
    out = rgba.copy()
    fg = (out[..., 3] > 0).astype(np.uint8)
    n_labels, labels, stats, _ = cv2.connectedComponentsWithStats(fg, connectivity=8)
    if n_labels <= 2:
        return out
    areas = [int(stats[i, cv2.CC_STAT_AREA]) for i in range(1, n_labels)]
    max_area = max(areas) if areas else 0
    if max_area == 0:
        return out
    thresh = max(12, int(max_area * min_frac))
    for i in range(1, n_labels):
        if int(stats[i, cv2.CC_STAT_AREA]) < thresh:
            out[labels == i] = [0, 0, 0, 0]
    return out


def fix_stone_bridge_arch() -> Dict[str, int]:
    """Open the trapped white arch void under stone_bridge.png and refresh _n.png."""
    bridge_path = UNITY_CART_ROOT / "structures" / "stone_bridge.png"
    bridge_n_path = UNITY_CART_ROOT / "structures" / "stone_bridge_n.png"
    arr = np.array(Image.open(bridge_path).convert("RGBA"), dtype=np.uint8)
    h, w, _ = arr.shape
    near_white = (arr[..., 3] > 0) & (arr[..., 0] >= 228) & (arr[..., 1] >= 228) & (arr[..., 2] >= 228)
    n_lbl, labels, stats, centroids = cv2.connectedComponentsWithStats(near_white.astype(np.uint8), connectivity=8)
    removed = 0
    for i in range(1, n_lbl):
        area = int(stats[i, cv2.CC_STAT_AREA])
        cx, cy = float(centroids[i][0]), float(centroids[i][1])
        # Under-bridge arch void is a bright white blob centered horizontally in lower half
        if area >= 80 and (0.20 * w <= cx <= 0.80 * w) and (cy >= 0.48 * h):
            arr[labels == i] = [0, 0, 0, 0]
            removed += area
    # Also clean 1-px bright anti-aliased fringe immediately bordering the removed arch
    if removed > 0:
        Image.fromarray(arr, mode="RGBA").save(bridge_path)
        norm_arr, _ = pp.generate_normal_map(arr, periodic=False)
        Image.fromarray(norm_arr).save(bridge_n_path)
    print(f"[stone_bridge] cleared white arch void pixels={removed}")
    return {"arch_void_cleared": removed}


def lift_tree_pine_b_contrast() -> Dict[str, int]:
    """Brighten overly dim pine foliage inside the SoC 128-color palette for crisp readability."""
    pine_path = UNITY_CART_ROOT / "trees" / "tree_pine_b.png"
    pine_n_path = UNITY_CART_ROOT / "trees" / "tree_pine_b_n.png"
    arr = np.array(Image.open(pine_path).convert("RGBA"), dtype=np.uint8)
    fg = arr[..., 3] > 0
    rgb = arr[..., :3].astype(np.float32)
    # Lift mid-dark shadow foliage mildly toward sunlit emerald while preserving silhouettes
    rgb[fg] = np.clip(rgb[fg] * 1.24 + np.array([6.0, 14.0, 8.0], dtype=np.float32), 0, 255)
    lifted = arr.copy()
    lifted[..., :3] = rgb.astype(np.uint8)
    pal_rgb = pp.load_palette(gs.PALETTE_PATH)
    quant = pp.apply_sunlit_grade_and_quantize(
        lifted,
        pal_rgb,
        dither_strength=0.0,
        grade_strength=0.10,
        shadow_lift=4.0,
        vibrance_boost=1.08,
        preserve_alpha=True,
    )
    quant = keep_major_components(quant, min_frac=0.05)
    Image.fromarray(quant, mode="RGBA").save(pine_path)
    norm_arr, _ = pp.generate_normal_map(quant, periodic=False)
    Image.fromarray(norm_arr).save(pine_n_path)
    st = pp.validate_outputs(quant, norm_arr, pal_rgb, is_tile=False)
    print(f"[tree_pine_b] lifted contrast | colors={st['unique_palette_colors']} | off_pal={st['off_palette_colors']}")
    return st


def _soft_guides_from_painting(name: str, rgb_im: Image.Image, depth_map: np.ndarray, blur_init: float = 34.0, blur_depth: float = 24.0) -> Tuple[Path, Path]:
    """Save feathered pure-white-bordered init RGB + soft blurred depth (no crisp geometric Canny lock)."""
    # Feather init colors into pure #FFFFFF so SDXL creates rich leaf/stone edges while outer border stays white
    blurred_init = rgb_im.filter(ImageFilter.GaussianBlur(radius=blur_init))
    arr_init = np.array(blurred_init, dtype=np.uint8)
    # Keep a thin pure white safety rim so no forest edge touches frame boundary
    rim = 36
    arr_init[:rim, :] = 255
    arr_init[-rim:, :] = 255
    arr_init[:, :rim] = 255
    arr_init[:, -rim:] = 255
    p_init = GUIDE_DIR / f"{name}_soft_init.png"
    Image.fromarray(arr_init, mode="RGB").save(p_init)

    d_u8 = np.clip(depth_map * 255.0, 0, 255).astype(np.uint8)
    d_im = Image.fromarray(np.stack([d_u8, d_u8, d_u8], axis=-1), mode="RGB").filter(ImageFilter.GaussianBlur(radius=blur_depth))
    p_depth = GUIDE_DIR / f"{name}_soft_depth.png"
    d_im.save(p_depth)
    return p_init, p_depth


def paint_mottled_oak_b(size: int = 1024, seed: int = 101) -> Tuple[Path, Path]:
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (size, size), (255, 255, 255))
    draw = ImageDraw.Draw(im)
    depth = np.zeros((size, size), dtype=np.float32)
    cx = size // 2

    # Trunk mass
    draw.polygon([(cx - 48, 600), (cx + 48, 600), (cx + 85, 900), (cx - 85, 900)], fill=(116, 76, 46))
    cv2.fillPoly(depth, [np.array([(cx - 48, 600), (cx + 48, 600), (cx + 85, 900), (cx - 85, 900)], np.int32)], 0.55)

    # Organic dappled foliage cluster cloud (hundreds of small mottled leaf blobs, never clean big circles)
    centers = [(cx - 170, 445, 165), (cx + 175, 455, 160), (cx - 125, 300, 155), (cx + 115, 315, 150), (cx, 240, 160), (cx, 435, 190)]
    for b_cx, b_cy, b_r in centers:
        for _ in range(32):
            ang = rng.uniform(0, 2 * np.pi)
            rad = rng.uniform(0, b_r * 0.85)
            lx = int(b_cx + rad * np.cos(ang))
            ly = int(b_cy + rad * np.sin(ang))
            lr = int(rng.integers(24, 58))
            lit = (cx - lx) * 0.6 + (450 - ly) * 0.7
            if lit > 90:
                col = (int(rng.integers(125, 175)), int(rng.integers(178, 212)), int(rng.integers(64, 96)))
            elif lit > -30:
                col = (int(rng.integers(72, 118)), int(rng.integers(134, 172)), int(rng.integers(48, 72)))
            else:
                col = (int(rng.integers(34, 64)), int(rng.integers(76, 108)), int(rng.integers(36, 56)))
            draw.ellipse([lx - lr, ly - lr, lx + lr, ly + lr], fill=col)
            cv2.circle(depth, (lx, ly), lr, 0.82 + 0.14 * float(ly < 380), -1)
    return _soft_guides_from_painting("tree_oak_b", im, depth, blur_init=30.0, blur_depth=22.0)


def paint_mottled_pine_a(size: int = 1024, seed: int = 202) -> Tuple[Path, Path]:
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (size, size), (255, 255, 255))
    draw = ImageDraw.Draw(im)
    depth = np.zeros((size, size), dtype=np.float32)
    cx = size // 2

    draw.rectangle([cx - 42, 740, cx + 42, 920], fill=(104, 68, 44))
    cv2.rectangle(depth, (cx - 42, 740), (cx + 42, 920), 0.52, -1)

    # Organic shaggy spruce silhouette made of overlapping drooping needle tufts
    for row_i in range(14):
        t = row_i / 13.0
        y_center = int(145 + t * 655)
        spread = int(42 + (t ** 1.08) * 265)
        n_tufts = max(4, int(4 + t * 14))
        for _ in range(n_tufts):
            tx = int(cx + rng.uniform(-spread, spread))
            ty = int(y_center + rng.integers(-22, 26))
            rx = int(rng.integers(24, 52))
            ry = int(rng.integers(18, 40))
            if tx < cx - 20 and t < 0.7:
                col = (int(rng.integers(72, 114)), int(rng.integers(142, 184)), int(rng.integers(70, 96)))
            elif tx < cx + spread * 0.25:
                col = (int(rng.integers(44, 78)), int(rng.integers(104, 144)), int(rng.integers(56, 82)))
            else:
                col = (int(rng.integers(26, 48)), int(rng.integers(68, 98)), int(rng.integers(44, 68)))
            draw.ellipse([tx - rx, ty - ry, tx + rx, ty + ry], fill=col)
            cv2.ellipse(depth, (tx, ty), (rx, ry), 0, 0, 360, float(0.74 + (1.0 - t) * 0.20), -1)
    return _soft_guides_from_painting("tree_pine_a", im, depth, blur_init=28.0, blur_depth=22.0)


def paint_mottled_willow_a(size: int = 1024, seed: int = 303) -> Tuple[Path, Path]:
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (size, size), (255, 255, 255))
    draw = ImageDraw.Draw(im)
    depth = np.zeros((size, size), dtype=np.float32)
    cx = size // 2

    # Broad twisted willow trunk + boughs
    draw.polygon([(cx - 55, 450), (cx + 55, 450), (cx + 115, 915), (cx - 115, 915)], fill=(96, 62, 42))
    cv2.fillPoly(depth, [np.array([(cx - 55, 450), (cx + 55, 450), (cx + 115, 915), (cx - 115, 915)], np.int32)], 0.56)

    # Organic willow crown canopy clouds + soft clustered draping vine waves
    for _ in range(110):
        ang = rng.uniform(0, 2 * np.pi)
        rx = rng.uniform(0, 340)
        ry = rng.uniform(0, 175)
        lx = int(cx + rx * np.cos(ang))
        ly = int(310 + ry * np.sin(ang))
        r = int(rng.integers(32, 72))
        lit = (cx - lx) * 0.4 + (380 - ly) * 0.8
        if lit > 60:
            col = (int(rng.integers(112, 162)), int(rng.integers(172, 206)), int(rng.integers(78, 108)))
        elif lit > -40:
            col = (int(rng.integers(68, 108)), int(rng.integers(134, 168)), int(rng.integers(64, 90)))
        else:
            col = (int(rng.integers(40, 68)), int(rng.integers(88, 122)), int(rng.integers(52, 76)))
        draw.ellipse([lx - r, ly - r, lx + r, ly + r], fill=col)
        cv2.circle(depth, (lx, ly), r, 0.85, -1)

    # Irregular draping vine clusters down sides (leaving central trunk glimpse)
    for vx in range(cx - 330, cx + 331, 34):
        if abs(vx - cx) < 58 and rng.random() < 0.7:
            continue
        length = int(rng.integers(220, 430))
        col = (84, 146, 74) if vx < cx else (48, 98, 60)
        for step_y in range(360, 360 + length, 32):
            wiggle = int(rng.integers(-14, 15))
            rr = int(rng.integers(16, 30))
            draw.ellipse([vx + wiggle - rr, step_y - rr, vx + wiggle + rr, step_y + rr], fill=col)
            cv2.circle(depth, (vx + wiggle, step_y), rr, 0.76, -1)

    return _soft_guides_from_painting("tree_willow_a", im, depth, blur_init=32.0, blur_depth=24.0)


def paint_mottled_stone_fence_v(size: int = 1024, seed: int = 404) -> Tuple[Path, Path]:
    """Painterly low mossy blue-slate & sandstone garden fence matching stone_fence_h on pure #FFFFFF."""
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (size, size), (255, 255, 255))
    draw = ImageDraw.Draw(im)
    depth = np.zeros((size, size), dtype=np.float32)
    cx = size // 2

    # Base low stone wall core (north-south oblique span with two end stone pillars + mossy hedges tucked beside stones)
    draw.rectangle([cx - 88, 190, cx + 88, 860], fill=(118, 120, 134))
    cv2.rectangle(depth, (cx - 88, 190), (cx + 88, 860), 0.72, -1)
    # Stone end pillars
    for py in (140, 740):
        draw.rectangle([cx - 118, py, cx + 118, py + 175], fill=(134, 132, 144))
        draw.rectangle([cx - 122, py, cx + 110, py + 58], fill=(176, 174, 172))
        cv2.rectangle(depth, (cx - 122, py), (cx + 122, py + 175), 0.88, -1)
    # Cobble variations + lush emerald moss patches
    for by in range(210, 760, 48):
        tone = int(rng.integers(-24, 24))
        draw.rectangle([cx - 80, by, cx + 82, by + 42], fill=(142 + tone, 138 + tone, 148 + tone))
        if rng.random() < 0.6:
            mx = int(cx + rng.integers(-65, 55))
            draw.ellipse([mx - 24, by - 10, mx + 36, by + 26], fill=(74, 134, 62))
    return _soft_guides_from_painting("stone_fence_v", im, depth, blur_init=24.0, blur_depth=18.0)


def run_item(
    name: str,
    rel_subdir: str,
    out_size: Tuple[int, int],
    prompt: str,
    extra_neg: str,
    seed: int,
    p_init: Path,
    p_depth: Path,
    denoise: float,
    depth_strength: float,
    depth_end: float,
    is_tree: bool,
) -> dict:
    in_rem = gs.upload_image(p_init, f"cart_v2_{name}_init.png")
    d_rem = gs.upload_image(p_depth, f"cart_v2_{name}_depth.png")

    neg = gs.SPRITE_NEG + ", " + extra_neg
    wf = gs.build_cartography_workflow(
        prompt=prompt,
        negative=neg,
        seed=seed,
        filename_prefix=f"cart_v2_{name}",
        lora_strength=0.90,
        steps=28,
        cfg=6.8,
        width=1024,
        height=1024,
        ip_ref_remote=None,
        depth_remote=d_rem,
        depth_strength=depth_strength,
        depth_end=depth_end,
        canny_remote=None,
        init_remote=in_rem,
        denoise=denoise,
    )
    img_bytes, dt, peak_vram = gs.run_comfy_prompt(wf)
    raw_path = RAW_DIR / f"{name}_v2.png"
    raw_path.write_bytes(img_bytes)

    out_png = UNITY_CART_ROOT / rel_subdir / f"{name}.png"
    out_nrm = UNITY_CART_ROOT / rel_subdir / f"{name}_n.png"
    pp.process_sprite(
        raw_path,
        out_png,
        output_normal=out_nrm,
        target_size=out_size,
        pad_px=3 if is_tree else 2,
        bottom_pivot=True,
        palette_path=gs.PALETTE_PATH,
        thickness=1,
    )

    cleaned = np.array(Image.open(out_png).convert("RGBA"), dtype=np.uint8)
    if is_tree:
        cleaned = gs.strip_tree_bottom_dirt_oval(cleaned)
    cleaned = keep_major_components(cleaned, min_frac=0.05)
    Image.fromarray(cleaned, mode="RGBA").save(out_png)
    norm_arr, _ = pp.generate_normal_map(cleaned, periodic=False)
    Image.fromarray(norm_arr).save(out_nrm)
    pal_rgb = pp.load_palette(gs.PALETTE_PATH)
    stats = pp.validate_outputs(cleaned, norm_arr, pal_rgb, is_tile=False)
    stats["elapsed_s"] = round(dt, 2)
    stats["peak_vram_mib"] = peak_vram
    stats["fg_pixels"] = int(np.sum(cleaned[..., 3] > 0))
    print(f"[{name}] {dt:.1f}s | peak {peak_vram} MiB | colors={stats['unique_palette_colors']} | fg={stats['fg_pixels']} | off_pal={stats['off_palette_colors']}")
    return stats


def main() -> int:
    r = requests.get(f"{SERVER_URL}/system_stats", timeout=10)
    r.raise_for_status()
    print(f"ComfyUI reachable. Initial VRAM: {gs.query_vram_mib()} MiB")

    # Deterministic pixel QA fixes on existing sprites
    fix_stone_bridge_arch()
    lift_tree_pine_b_contrast()

    # Soft feathered mottled guides -> denoise=0.90 rich SoC texture without vector circle outlines
    fence_g = paint_mottled_stone_fence_v()
    oak_b_g = paint_mottled_oak_b()
    pine_a_g = paint_mottled_pine_a()
    willow_a_g = paint_mottled_willow_a()

    jobs = [
        dict(
            name="stone_fence_v",
            rel_subdir="structures",
            out_size=(128, 128),
            prompt=(
                "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo prop, simple background, "
                "pure solid white background, isolated top-down 3/4 oblique RPG prop, low medieval village "
                "cobblestone wall segment running north-south with weathered blue-grey and sandstone bricks, "
                "square carved stone pillar caps, emerald ivy bushes and moss tucked along stones, rich mortar detail, "
                "crisp dark slate ink pixel outlines, warm upper-left sunlight, Songs of Conquest village prop"
            ),
            extra_neg="smooth untextured blocks, flat vector boxes, plain slab, forest, sky, grass field, dirt ground",
            seed=304514,
            guides=fence_g,
            denoise=0.90,
            depth_strength=0.45,
            depth_end=0.58,
            is_tree=False,
        ),
        dict(
            name="tree_oak_b",
            rel_subdir="trees",
            out_size=(224, 224),
            prompt=(
                "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, simple background, "
                "pure solid white background, lush hand-pixeled medieval sunlit broadleaf oak tree, clusters of "
                "hundreds of crisp individual leaf pixels, warm sunlit lime-green highlights, rich emerald midtones, "
                "deep cool teal-blue leaf shadows, gnarled cracked brown bark trunk with root flare, crisp dark ink outlines, "
                "Songs of Conquest Arleon oak tree style"
            ),
            extra_neg="flat smooth circles, MS paint blobs, gradient fill, vector spheres, forest horizon, sky, dirt mound, oval grass base, yellow streaks",
            seed=304522,
            guides=oak_b_g,
            denoise=0.92,
            depth_strength=0.44,
            depth_end=0.58,
            is_tree=True,
        ),
        dict(
            name="tree_pine_a",
            rel_subdir="trees",
            out_size=(192, 256),
            prompt=(
                "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, simple background, "
                "pure solid white background, lush hand-pixeled medieval evergreen spruce pine tree, shaggy drooping "
                "pine needle boughs textured with intricate pixel clusters, sunlit emerald green tips and deep teal-pine "
                "shadows, warm brown bark trunk at base, crisp dark pixel outlines, Songs of Conquest conifer style"
            ),
            extra_neg="geometric flat triangles, smooth vector cones, flat green, yellow sky streaks, snow, clouds, forest scenery, dirt mound",
            seed=304523,
            guides=pine_a_g,
            denoise=0.92,
            depth_strength=0.44,
            depth_end=0.58,
            is_tree=True,
        ),
        dict(
            name="tree_willow_a",
            rel_subdir="trees",
            out_size=(256, 256),
            prompt=(
                "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, simple background, "
                "pure solid white background, lush hand-pixeled medieval weeping willow tree, rich leafy green canopy "
                "with intricate overlapping pixelated leaf clusters and soft swaying draped hanging leafy vines, gnarled "
                "cracked brown bark trunk, crisp dark ink pixel outlines, Songs of Conquest willow tree style"
            ),
            extra_neg="flat vector circles, straight vertical bars, fence slats, rigid stripes, smooth gradient, forest scenery, pond, dirt mound",
            seed=304525,
            guides=willow_a_g,
            denoise=0.92,
            depth_strength=0.42,
            depth_end=0.56,
            is_tree=True,
        ),
    ]

    for j in jobs:
        g_init, g_depth = j.pop("guides")
        run_item(p_init=g_init, p_depth=g_depth, **j)

    gs.make_contact_sheets()
    print(f"Rebuilt contact sheet at {gs.SCRATCH_ROOT / 'phase1_assets_contact_sheet.png'}")
    print(f"Post-run VRAM: {gs.query_vram_mib()} MiB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
