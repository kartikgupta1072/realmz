"""Unit tests for cartography/pixel_postprocess.py."""

from __future__ import annotations

import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

from pixel_postprocess import (
    DEFAULT_LAIGTER_EXE,
    DEFAULT_PALETTE_PATH,
    load_palette,
    process_seamless_tile,
    process_sprite,
    remove_background_1bit,
)


def test_white_bg_preserves_interior_stucco_and_windows() -> None:
    """White-background cottage with bright #FFFFFF stucco and windows must have ZERO interior holes."""
    h, w = 512, 512
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    # Dark ink outer wall box
    img[120:420, 100:412, :3] = [17, 19, 31]
    # Bright sunlit white/cream stucco wall interior (including pure white highlights)
    img[132:408, 112:400, :3] = [250, 246, 236]
    img[150:260, 140:260, :3] = [255, 255, 255]
    # Dark window frame with pure white sunlit pane inside
    img[170:240, 280:360, :3] = [46, 27, 17]
    img[180:230, 290:350, :3] = [255, 255, 255]
    # Timber crossbeam (thin 6px line)
    img[270:278, 112:400, :3] = [99, 60, 36]
    # Faint gray cast shadow at bottom exterior
    img[420:435, 110:402, :3] = [232, 232, 234]

    _, fg_mask = remove_background_1bit(img, strip_ground_shadow=True)

    # Exterior corners must be background (False)
    assert not fg_mask[10, 10]
    assert not fg_mask[490, 490]
    # Entire interior stucco + pure-white patch + pure-white window pane must be foreground (True)
    assert np.all(fg_mask[135:405, 115:395]), "Interior white stucco or window pane was punched out!"

    # Full sprite pipeline test at (320, 256) BottomCenter
    with tempfile.TemporaryDirectory(prefix="realmz_test_white_") as td:
        td_path = Path(td)
        in_p = td_path / "cottage.png"
        out_p = td_path / "cottage_out.png"
        out_n = td_path / "cottage_out_n.png"
        Image.fromarray(img, mode="RGBA").save(in_p)

        stats = process_sprite(
            in_p,
            out_p,
            output_normal=out_n,
            target_size=(320, 256),
            pad_px=2,
            bottom_pivot=True,
            normal_backend="auto",
        )
        out_arr = np.array(Image.open(out_p).convert("RGBA"))
        norm_arr = np.array(Image.open(out_n).convert("RGBA"))

        assert out_arr.shape == (256, 320, 4)
        assert norm_arr.shape == (256, 320, 4)
        assert stats["alpha_is_1bit"] is True
        assert stats["off_palette_colors"] == 0
        # Check BottomCenter: bottom-most opaque row sits at 256 - 1 - 2 = 253
        ys_op = np.where(out_arr[..., 3] == 255)[0]
        assert int(ys_op.max()) == 253
        # Check interior center of cottage has zero alpha=0 holes
        interior_alpha = out_arr[90:230, 100:220, 3]
        assert np.all(interior_alpha == 255), "Downscaled cottage has interior alpha holes!"


def test_chroma_bg_punches_enclosed_arch_and_despills() -> None:
    """Magenta #FF00FF chroma background must punch enclosed interior arch pocket."""
    h, w = 512, 512
    img = np.full((h, w, 4), [255, 0, 255, 255], dtype=np.uint8)
    # Stone bridge silhouette
    img[160:380, 60:452, :3] = [135, 129, 122]
    # Dark outline around bridge and arch
    img[160:170, 60:452, :3] = [28, 32, 51]
    # Enclosed magenta arch opening underneath bridge
    img[260:380, 170:342, :3] = [255, 0, 255]
    # Bridge base closed across bottom so arch is strictly enclosed
    img[372:384, 60:452, :3] = [46, 27, 17]
    # Add 1px magenta spill along top edge
    img[159, 60:452, :3] = [245, 40, 245]

    rgb_clean, fg_mask = remove_background_1bit(img, strip_ground_shadow=False)
    assert not fg_mask[10, 10]
    assert np.all(~fg_mask[270:360, 180:330]), "Enclosed chroma arch pocket was not punched!"
    assert np.all(fg_mask[175:245, 80:430]), "Bridge body was lost!"
    assert rgb_clean.shape == (h, w, 3)


def test_seamless_tile_and_alpha_transition_mask() -> None:
    """Seamless 1024x1024 -> 128x128 tile locks to palette and produces [64..255] alpha noise."""
    h, w = 512, 512
    yy, xx = np.mgrid[0:h, 0:w]
    tile = np.zeros((h, w, 3), dtype=np.uint8)
    tile[..., 0] = (50 + 25 * np.sin(2 * np.pi * xx / 64.0) * np.cos(2 * np.pi * yy / 64.0)).astype(np.uint8)
    tile[..., 1] = (120 + 35 * np.sin(2 * np.pi * xx / 32.0)).astype(np.uint8)
    tile[..., 2] = (55 + 15 * np.cos(2 * np.pi * yy / 32.0)).astype(np.uint8)

    with tempfile.TemporaryDirectory(prefix="realmz_test_tile_") as td:
        td_path = Path(td)
        in_p = td_path / "raw_tile.png"
        out_p = td_path / "tile_128.png"
        out_n = td_path / "tile_128_n.png"
        Image.fromarray(tile, mode="RGB").save(in_p)

        stats = process_seamless_tile(
            in_p,
            out_p,
            output_normal=out_n,
            target_size=128,
            noise_seed=123,
            normal_backend="auto",
        )
        out_arr = np.array(Image.open(out_p).convert("RGBA"))
        norm_arr = np.array(Image.open(out_n).convert("RGBA"))
        pal = load_palette(DEFAULT_PALETTE_PATH)
        pal_set = {tuple(int(c) for c in row) for row in pal}
        used = {tuple(int(c) for c in row) for row in out_arr[..., :3].reshape(-1, 3)}

        assert out_arr.shape == (128, 128, 4)
        assert norm_arr.shape == (128, 128, 4)
        assert len(used - pal_set) == 0
        assert stats["off_palette_colors"] == 0
        assert int(out_arr[..., 3].min()) >= 64
        assert int(out_arr[..., 3].max()) <= 255
        assert int(out_arr[..., 3].max()) > int(out_arr[..., 3].min()) + 64
        assert stats["normal_min_z"] >= 0.0
        # Verify deterministic seed reproducibility
        out_p2 = td_path / "tile_128_b.png"
        out_n2 = td_path / "tile_128_b_n.png"
        process_seamless_tile(in_p, out_p2, output_normal=out_n2, target_size=128, noise_seed=123, normal_backend="auto")
        assert np.array_equal(out_arr, np.array(Image.open(out_p2).convert("RGBA")))
        print("Tile normal backend used:", stats["normal_backend"], "| Laigter exists:", DEFAULT_LAIGTER_EXE.exists())


if __name__ == "__main__":
    test_white_bg_preserves_interior_stucco_and_windows()
    test_chroma_bg_punches_enclosed_arch_and_despills()
    test_seamless_tile_and_alpha_transition_mask()
    print("ALL pixel_postprocess.py TESTS PASSED!")
