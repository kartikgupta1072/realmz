"""Unit tests for cartography/ui_chrome_pipeline.py."""

from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from ui_chrome_pipeline import (  # noqa: E402
    DEFAULT_CONTACT_SHEET,
    DEFAULT_FONT_SRC,
    DEFAULT_UI_OUT_DIR,
    UI_ASSET_SPECS,
    _make_stone_panel_clasp_10x10,
    build_all_assets,
    generate_ui_chrome_kit,
    slice_9,
)


class UiChromePipelineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.out_dir = DEFAULT_UI_OUT_DIR
        cls.contact_sheet = DEFAULT_CONTACT_SHEET
        generate_ui_chrome_kit(
            out_dir=cls.out_dir,
            font_src=DEFAULT_FONT_SRC,
            contact_sheet_path=cls.contact_sheet,
            copy_font=True,
        )

    def test_all_14_pngs_exist_with_exact_dimensions_and_rgba(self) -> None:
        self.assertEqual(len(UI_ASSET_SPECS), 14)
        for spec in UI_ASSET_SPECS:
            path = self.out_dir / spec.filename
            self.assertTrue(path.exists(), f"Missing UI chrome asset: {path}")
            with Image.open(path) as img:
                self.assertEqual(img.mode, "RGBA", f"Expected RGBA for {spec.filename}, got {img.mode}")
                self.assertEqual(
                    img.size,
                    (spec.width, spec.height),
                    f"Dimension mismatch for {spec.filename}: {img.size} != {(spec.width, spec.height)}",
                )

    def test_black_chancery_font_copied_with_parity(self) -> None:
        font_dst = self.out_dir / "BlackChancery.ttf"
        self.assertTrue(DEFAULT_FONT_SRC.exists(), f"Missing source font: {DEFAULT_FONT_SRC}")
        self.assertTrue(font_dst.exists(), f"Missing copied font: {font_dst}")
        src_sha = hashlib.sha256(DEFAULT_FONT_SRC.read_bytes()).hexdigest()
        dst_sha = hashlib.sha256(font_dst.read_bytes()).hexdigest()
        self.assertEqual(src_sha, dst_sha)

    def test_non_empty_opaque_border_bands_on_9slice_assets(self) -> None:
        for spec in UI_ASSET_SPECS:
            if not spec.is_9slice:
                continue
            arr = np.array(Image.open(self.out_dir / spec.filename), dtype=np.uint8)
            l, b, r, t = spec.border
            if l > 0:
                self.assertGreater(int(np.count_nonzero(arr[:, :l, 3] > 0)), 0, f"{spec.filename} empty L band")
            if r > 0:
                self.assertGreater(int(np.count_nonzero(arr[:, -r:, 3] > 0)), 0, f"{spec.filename} empty R band")
            if t > 0:
                self.assertGreater(int(np.count_nonzero(arr[:t, :, 3] > 0)), 0, f"{spec.filename} empty T band")
            if b > 0:
                self.assertGreater(int(np.count_nonzero(arr[-b:, :, 3] > 0)), 0, f"{spec.filename} empty B band")

            # Verify slice_9 preserves exact corner dimensions when stretched
            stretched = slice_9(arr, spec.width * 2, max(spec.height, spec.height * 2 if t + b > 0 else spec.height), spec.border)
            self.assertEqual(stretched.shape[1], spec.width * 2)

    def test_stone_panel_symmetric_corner_clasps(self) -> None:
        arr = np.array(Image.open(self.out_dir / "ui_stone_panel_9s.png"), dtype=np.uint8)
        tl = arr[:10, :10]
        tr = np.fliplr(arr[:10, -10:])
        bl = np.flipud(arr[-10:, :10])
        br = np.flipud(np.fliplr(arr[-10:, -10:]))

        clasp_alpha = _make_stone_panel_clasp_10x10()[..., 3] > 0
        gold_bright = (tl[..., 0] >= 180) & (tl[..., 1] >= 140) & (tl[..., 2] <= 130)
        self.assertGreaterEqual(int(np.count_nonzero(gold_bright)), 12, "Expected bright gold clasp pixels in corner")

        def warm_mask(patch: np.ndarray) -> np.ndarray:
            return patch[..., 0].astype(int) > patch[..., 2].astype(int) + 25

        tl_warm = warm_mask(tl)
        for name, other in [("TR", tr), ("BL", bl), ("BR", br)]:
            np.testing.assert_array_equal(tl_warm, warm_mask(other), err_msg=f"Clasp silhouette mismatch for {name}")
            np.testing.assert_array_equal(tl[clasp_alpha], other[clasp_alpha], err_msg=f"Clasp RGBA mismatch for {name}")

    def test_tint_panel_high_luminance_neutral_center(self) -> None:
        arr = np.array(Image.open(self.out_dir / "ui_tint_panel_9s.png"), dtype=np.uint8)
        center = arr[6:-6, 6:-6]
        self.assertTrue(np.all(center[..., 3] == 255))
        # Neutral grayscale check
        self.assertEqual(int(np.max(np.abs(center[..., 0].astype(int) - center[..., 1].astype(int)))), 0)
        self.assertEqual(int(np.max(np.abs(center[..., 1].astype(int) - center[..., 2].astype(int)))), 0)

        luma = (0.2126 * center[..., 0] + 0.7152 * center[..., 1] + 0.0722 * center[..., 2]) / 255.0
        mean_luma = float(np.mean(luma))
        self.assertGreaterEqual(mean_luma, 0.72, f"Tint panel center mean luma {mean_luma:.3f} < 0.72")
        self.assertLessEqual(mean_luma, 0.97)

    def test_modal_scrim_radial_alpha_falloff(self) -> None:
        arr = np.array(Image.open(self.out_dir / "ui_modal_scrim.png"), dtype=np.uint8)
        alpha = arr[..., 3].astype(np.float32) / 255.0
        ch, cw = alpha.shape[0] // 2, alpha.shape[1] // 2
        center_alpha = float(np.mean(alpha[ch - 8 : ch + 8, cw - 8 : cw + 8]))
        edge_ring = np.concatenate([alpha[0, :], alpha[-1, :], alpha[1:-1, 0], alpha[1:-1, -1]])
        edge_alpha = float(np.mean(edge_ring))
        corner_alpha = float(np.mean([alpha[0, 0], alpha[0, -1], alpha[-1, 0], alpha[-1, -1]]))

        self.assertGreater(edge_alpha, center_alpha)
        self.assertGreaterEqual(center_alpha, 0.55)
        self.assertLessEqual(center_alpha, 0.62)
        self.assertGreaterEqual(edge_alpha, 0.80)
        self.assertLessEqual(edge_alpha, 0.88)
        self.assertGreaterEqual(corner_alpha, edge_alpha)

    def test_determinism_and_contact_sheet(self) -> None:
        first = build_all_assets()
        second = build_all_assets()
        for name in first:
            np.testing.assert_array_equal(first[name], second[name])

        self.assertTrue(self.contact_sheet.exists(), f"Missing contact sheet: {self.contact_sheet}")
        with Image.open(self.contact_sheet) as sheet_img:
            self.assertEqual(sheet_img.format, "PNG")
            self.assertGreater(sheet_img.width, 800)
            self.assertGreater(sheet_img.height, 800)

    def test_isolated_temp_dir_generation(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_dir = Path(tmp) / "ui"
            tmp_sheet = Path(tmp) / "sheet.png"
            written = generate_ui_chrome_kit(
                out_dir=tmp_dir,
                font_src=DEFAULT_FONT_SRC,
                contact_sheet_path=tmp_sheet,
                copy_font=True,
            )
            self.assertEqual(len(written), 16)


if __name__ == "__main__":
    unittest.main()
