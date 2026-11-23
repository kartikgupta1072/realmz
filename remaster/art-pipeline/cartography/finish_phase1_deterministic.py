"""
Deterministic final polish for tree_pine_a bottom grey bar & architectural-matched
stone_fence_v derived directly from stone_fence_h at a foreshortened 3/4 oblique angle.
Zero ComfyUI calls needed. Zero Unicode em/en-dashes.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

CART_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(CART_DIR))
import fix_trees_and_fence as ftf  # noqa: E402
import generate_cartography_slice as gs  # noqa: E402
import pixel_postprocess as pp  # noqa: E402


def clean_pine_a_bottom_rows() -> None:
    p = gs.UNITY_CART_ROOT / "trees" / "tree_pine_a.png"
    pn = gs.UNITY_CART_ROOT / "trees" / "tree_pine_a_n.png"
    arr = np.array(Image.open(p).convert("RGBA"), dtype=np.uint8)
    h, w, _ = arr.shape
    removed = 0
    # Clear any cool pale grey/silver pedestal pixels in the bottom 12 rows (where R <= G <= B or low saturation gray)
    for y in range(max(0, h - 14), h):
        for x in range(w):
            r, g, b, a = int(arr[y, x, 0]), int(arr[y, x, 1]), int(arr[y, x, 2]), int(arr[y, x, 3])
            if a == 0:
                continue
            # Warm bark pixels always have R significantly > B (usually R - B >= 25); pale blue-grey bar has B >= R - 10
            if (r - b) < 18 and (r + g + b) > 250:
                arr[y, x] = [0, 0, 0, 0]
                removed += 1
    arr = ftf.keep_major_components(arr, min_frac=0.05)
    # Re-anchor lowest opaque pixel to pad_px=3 above bottom edge just like process_sprite
    ys = np.where(arr[..., 3] > 0)[0]
    if len(ys) > 0:
        cur_bot = int(ys.max())
        target_bot = h - 4
        dy = target_bot - cur_bot
        if dy > 0:
            shifted = np.zeros_like(arr)
            shifted[dy:, :] = arr[: h - dy, :]
            arr = shifted
    Image.fromarray(arr, mode="RGBA").save(p)
    norm_arr, _ = pp.generate_normal_map(arr, periodic=False)
    Image.fromarray(norm_arr).save(pn)
    print(f"[tree_pine_a] cleared bottom bar pixels={removed}")


def build_matching_stone_fence_v() -> None:
    """
    Build a foreshortened 3/4 oblique north-south village cobblestone fence matching
    stone_fence_h's exact brick color, purple-slate mortar, and mossy hedge leaves.
    """
    fh_path = gs.UNITY_CART_ROOT / "structures" / "stone_fence_h.png"
    fv_path = gs.UNITY_CART_ROOT / "structures" / "stone_fence_v.png"
    fv_n_path = gs.UNITY_CART_ROOT / "structures" / "stone_fence_v_n.png"

    fh = Image.open(fh_path).convert("RGBA")
    fh_arr = np.array(fh, dtype=np.uint8)
    ys, xs = np.where(fh_arr[..., 3] > 0)
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())
    crop = fh.crop((x0, y0, x1 + 1, y1 + 1))

    # Separate into stone wall strips & foreshorten horizontally (0.58x) with vertical oblique depth sweep
    # Creating a compact oblique N-S wall span with front lower stone pier overlapping rear upper pier
    cw, ch = crop.size
    # Left stone pillar (clean architectural pier) + 1 mossy cobble span sliver
    pillar_front = crop.crop((0, 0, min(28, cw), ch))
    rail_mid = crop.crop((24, int(ch * 0.18), min(52, cw - 8), ch))

    canvas = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    # Rear stone pier (slightly higher & shifted left so oblique wall body connects both piers smoothly)
    rear_pier = pillar_front.resize((max(18, int(pillar_front.width * 0.86)), max(40, int(pillar_front.height * 0.86))), Image.NEAREST)
    canvas.alpha_composite(rear_pier, dest=(44, 42))

    # Smooth diagonal oblique wall body connecting (48, 48) down-right to (60, 72)
    r_arr = np.array(rail_mid.resize((20, max(24, int(rail_mid.height * 0.82))), Image.NEAREST), dtype=np.uint8)
    s_arr = np.zeros((128, 128, 4), dtype=np.uint8)
    rh, rw, _ = r_arr.shape
    for rx in range(rw):
        t = rx / max(1, rw - 1)
        nx = 54 + rx
        ny_off = int(round(52 + t * 18))
        for ry in range(rh):
            if r_arr[ry, rx, 3] > 0 and 0 <= nx < 128 and 0 <= ny_off + ry < 128:
                s_arr[ny_off + ry, nx] = r_arr[ry, rx]
    canvas.alpha_composite(Image.fromarray(s_arr, mode="RGBA"), dest=(0, 0))

    # Front stone pillar anchoring bottom foreground cleanly
    canvas.alpha_composite(pillar_front, dest=(58, 128 - 4 - pillar_front.height))

    out_arr = np.array(canvas, dtype=np.uint8)
    pal_rgb = pp.load_palette(gs.PALETTE_PATH)
    out_arr = pp.apply_sunlit_grade_and_quantize(
        out_arr,
        pal_rgb,
        dither_strength=0.0,
        grade_strength=0.0,
        shadow_lift=0.0,
        vibrance_boost=1.0,
        preserve_alpha=True,
    )
    out_arr = ftf.keep_major_components(out_arr, min_frac=0.04)
    Image.fromarray(out_arr, mode="RGBA").save(fv_path)
    norm_arr, _ = pp.generate_normal_map(out_arr, periodic=False)
    Image.fromarray(norm_arr).save(fv_n_path)
    st = pp.validate_outputs(out_arr, norm_arr, pal_rgb, is_tile=False)
    print(f"[stone_fence_v] matched from stone_fence_h | colors={st['unique_palette_colors']} | fg={st['opaque_pixels']} | off_pal={st['off_palette_colors']}")


def main() -> int:
    clean_pine_a_bottom_rows()
    build_matching_stone_fence_v()
    gs.make_contact_sheets()
    print(f"Updated contact sheet: {gs.SCRATCH_ROOT / 'phase1_assets_contact_sheet.png'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
