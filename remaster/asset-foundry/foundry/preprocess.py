"""Reference image preprocessing and clean background isolation (CPU-only)."""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image


def _estimate_border_color(rgb: np.ndarray) -> np.ndarray:
    h, w, _ = rgb.shape
    m = max(3, min(12, min(h, w) // 24))
    border = np.concatenate(
        [
            rgb[:m, :, :].reshape(-1, 3),
            rgb[-m:, :, :].reshape(-1, 3),
            rgb[:, :m, :].reshape(-1, 3),
            rgb[:, -m:, :].reshape(-1, 3),
        ],
        axis=0,
    )
    return np.median(border, axis=0)


def _strip_bottom_cast_shadow_rgba(rgba: np.ndarray) -> tuple[np.ndarray, int]:
    """
    Strip flat ground contact shadow ovals at the bottom edge of Pass A pixel sprites
    (e.g., wide slate shadow puddles below knight boots) without trimming real toes/bases.
    """
    alpha = rgba[..., 3] >= 128
    ys, _ = np.where(alpha)
    if len(ys) < 16:
        return rgba, 0
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    bh = y1 - y0
    if bh < 24:
        return rgba, 0

    widths = [int(np.sum(alpha[y])) for y in range(y0, y1)]
    band_top = max(0, bh - max(12, int(round(bh * 0.20))))
    band_bot = max(band_top + 1, bh - max(5, int(round(bh * 0.08))))
    ref_widths = [widths[i] for i in range(band_top, band_bot) if widths[i] > 0]
    if not ref_widths:
        return rgba, 0
    med_leg_w = float(np.median(ref_widths))

    # Locate the first row in the bottom 8% where width suddenly flares > 2.0x above ankle width
    flare_row = None
    bot_start = max(y0 + band_bot, y1 - max(6, int(round(bh * 0.08))))
    for y in range(bot_start, y1):
        if widths[y - y0] >= max(int(med_leg_w * 2.0), int(med_leg_w + 18)):
            flare_row = y
            break
    if flare_row is None:
        return rgba, 0

    # Sample reference X span just above flare row to isolate lateral shadow puddle pixels
    above_cols = np.where(alpha[max(y0, flare_row - 4) : flare_row])[1]
    if len(above_cols) == 0:
        return rgba, 0
    ax0, ax1 = int(above_cols.min()), int(above_cols.max())

    flare_cols = np.where(alpha[flare_row])[0]
    outer = [c for c in flare_cols if c < ax0 - 2 or c > ax1 + 2]
    if len(outer) < 6:
        return rgba, 0
    shadow_rgb = np.median(rgba[flare_row, outer, :3].astype(np.float32), axis=0)
    # Require cool/neutral shadow tone (avoid stripping bright warm base stone/grass props)
    if float(shadow_rgb[0]) > float(shadow_rgb[2]) + 15.0 or float(np.max(shadow_rgb)) > 210.0:
        return rgba, 0

    out = rgba.copy()
    scan_start = max(y0, flare_row - 2)
    rgb_f = out[scan_start:y1, :, :3].astype(np.float32)
    dist = np.linalg.norm(rgb_f - shadow_rgb[None, None, :], axis=-1)
    drop = (dist <= 30.0) & (out[scan_start:y1, :, 3] >= 128)
    removed = int(np.sum(drop))
    if removed > 0:
        out[scan_start:y1, :][drop] = [0, 0, 0, 0]
    return out, removed


def _simple_border_cutout(rgba: np.ndarray) -> Image.Image:
    """Fallback border distance cutout if rembg is unavailable or image has solid studio backdrop."""
    rgb = rgba[..., :3].astype(np.float32)
    bg = _estimate_border_color(rgb)
    dist = np.linalg.norm(rgb - bg[None, None, :], axis=-1)
    mask = (dist > 22.0).astype(np.uint8) * 255
    out = rgba.copy()
    out[..., 3] = mask
    return Image.fromarray(out, mode="RGBA")


def prepare_reference_image(
    input_path: Path,
    out_rgba_path: Path,
    out_rgb_white_path: Path,
    canvas_size: int = 512,
    margin_ratio: float = 0.12,
) -> dict[str, Any]:
    """
    Prepare any user photo, sketch, or Pass A transparent pixel sprite into a centered,
    cleanly isolated 512x512 RGBA and white-backdrop RGB image ready for Image-to-3D.
    Runs strictly on CPU so zero GPU VRAM is consumed.
    """
    src = Image.open(input_path).convert("RGBA")
    arr = np.array(src)
    alpha = arr[..., 3]
    transparent_ratio = float(np.mean(alpha < 128))
    already_transparent = transparent_ratio >= 0.12
    was_pixel_sprite = src.width <= 256 and src.height <= 256

    shadow_pixels_stripped = 0
    if already_transparent:
        if was_pixel_sprite:
            stripped_arr, shadow_pixels_stripped = _strip_bottom_cast_shadow_rgba(arr)
            cutout = Image.fromarray(stripped_arr, mode="RGBA")
        else:
            cutout = src
        bg_method = "existing_alpha"
    else:
        bg_method = "rembg_cpu"
        try:
            from rembg import remove, new_session

            session = new_session("u2net", providers=["CPUExecutionProvider"])
            cutout = remove(src, session=session).convert("RGBA")
            c_arr = np.array(cutout)
            if float(np.mean(c_arr[..., 3] >= 128)) < 0.01:
                cutout = _simple_border_cutout(arr)
                bg_method = "border_fallback_empty_rembg"
        except Exception:
            cutout = _simple_border_cutout(arr)
            bg_method = "border_fallback"

    c_arr = np.array(cutout)
    fg_mask = c_arr[..., 3] >= 128
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        # Fallback: keep full image if completely opaque / non-removable
        fg_mask = np.ones((c_arr.shape[0], c_arr.shape[1]), dtype=bool)
        ys, xs = np.where(fg_mask)
        bg_method = "full_frame_fallback"

    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    cropped = cutout.crop((x0, y0, x1, y1))

    # Clean semi-transparent rim pixels for crisp 3D silhouettes
    crop_arr = np.array(cropped)
    if already_transparent:
        crop_arr[..., 3] = np.where(crop_arr[..., 3] >= 128, 255, 0).astype(np.uint8)
        cropped = Image.fromarray(crop_arr, mode="RGBA")

    cw, ch = cropped.size
    avail = int(round(canvas_size * (1.0 - 2.0 * margin_ratio)))
    scale = min(avail / max(1, cw), avail / max(1, ch))
    new_w = max(1, int(round(cw * scale)))
    new_h = max(1, int(round(ch * scale)))

    resample_mode = Image.Resampling.NEAREST if was_pixel_sprite else Image.Resampling.LANCZOS
    resized = cropped.resize((new_w, new_h), resample_mode)

    canvas_rgba = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    ox = (canvas_size - new_w) // 2
    oy = (canvas_size - new_h) // 2
    canvas_rgba.paste(resized, (ox, oy), resized)

    white_bg = Image.new("RGBA", (canvas_size, canvas_size), (255, 255, 255, 255))
    white_bg.alpha_composite(canvas_rgba)
    canvas_rgb = white_bg.convert("RGB")

    out_rgba_path.parent.mkdir(parents=True, exist_ok=True)
    canvas_rgba.save(out_rgba_path, format="PNG")
    canvas_rgb.save(out_rgb_white_path, format="PNG")

    return {
        "input_size": [src.width, src.height],
        "canvas_size": canvas_size,
        "was_pixel_sprite": was_pixel_sprite,
        "bg_method": bg_method,
        "shadow_pixels_stripped": shadow_pixels_stripped,
        "fg_coverage": round(float(np.mean(np.array(canvas_rgba)[..., 3] >= 128)), 4),
    }
