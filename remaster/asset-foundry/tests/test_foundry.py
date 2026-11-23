"""Unit tests for Realmz Asset Foundry V1 configuration, preprocessing, shipping, and hygiene."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

FOUNDRY_CODE_DIR = Path(r"C:\realmz\remaster\asset-foundry")
if str(FOUNDRY_CODE_DIR) not in sys.path:
    sys.path.insert(0, str(FOUNDRY_CODE_DIR))

from foundry.config import infer_rig_profile, normalize_category, normalize_mode, sanitize_ascii, sanitize_asset_id
from foundry.preprocess import prepare_reference_image


class RealmzFoundryUnitTests(unittest.TestCase):
    def test_sanitize_and_infer_rig(self) -> None:
        self.assertEqual(sanitize_asset_id("River Trout! "), "river_trout")
        self.assertEqual(normalize_category("hero"), "Hero")
        self.assertEqual(normalize_mode("hd-2d"), "HD2D")
        self.assertEqual(infer_rig_profile("Monster", "river_fish", "Auto"), "Aquatic")
        self.assertEqual(infer_rig_profile("Monster", "giant_frog", "Auto"), "Quadruped")
        self.assertEqual(infer_rig_profile("Hero", "knight", "Auto"), "Humanoid")
        self.assertEqual(infer_rig_profile("Building", "cottage_medieval", "Auto"), "Static")
        cleaned = sanitize_ascii("Hello\u2014World\u2013Test")
        self.assertNotIn("\u2014", cleaned)
        self.assertNotIn("\u2013", cleaned)

    def test_preprocess_transparent_sprite_and_opaque_image(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            spr = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
            for y in range(30, 100):
                for x in range(40, 90):
                    spr.putpixel((x, y), (200, 140, 60, 255))
            spr_path = root / "sprite.png"
            spr.save(spr_path)

            out_rgba = root / "prep_rgba.png"
            out_rgb = root / "prep_rgb.png"
            info = prepare_reference_image(spr_path, out_rgba, out_rgb)
            self.assertEqual(info["bg_method"], "existing_alpha")
            self.assertTrue(info["was_pixel_sprite"])
            self.assertGreater(info["fg_coverage"], 0.05)
            with Image.open(out_rgba) as im_rgba:
                self.assertEqual(im_rgba.size, (512, 512))
            with Image.open(out_rgb) as im_rgb:
                self.assertEqual(im_rgb.mode, "RGB")

    def test_zero_em_or_en_dashes_in_foundry_sources(self) -> None:
        for py_file in (FOUNDRY_CODE_DIR / "foundry").glob("*.py"):
            txt = py_file.read_text(encoding="utf-8")
            self.assertNotIn("\u2014", txt, f"Em-dash found in {py_file}")
            self.assertNotIn("\u2013", txt, f"En-dash found in {py_file}")


if __name__ == "__main__":
    unittest.main()
