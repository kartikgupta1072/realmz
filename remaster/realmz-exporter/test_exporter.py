"""Unit tests for realmz-exporter parsers, graphics decoders, and CLI output."""

from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
import unittest

from PIL import Image

EXPORTER_DIR = Path(__file__).resolve().parent
if str(EXPORTER_DIR) not in sys.path:
    sys.path.insert(0, str(EXPORTER_DIR))

from export_realmz import export_scenario
from formats import parse_character, parse_doors, parse_land_grid, parse_maps_metadata, parse_monsters, parse_rand_level
from graphics import decode_cicn, decode_pict, slice_tilesheet
from rsrc import ResourceFork


REALMZ_BASE = Path(r"C:\realmz\base\Realmz")
BYWATER_DIR = REALMZ_BASE / "Scenarios" / "City of Bywater"
DATA_FILES = REALMZ_BASE / "Data Files"


class TestResourceFork(unittest.TestCase):
    """Verify Classic Mac .rsrc data-fork parsing."""

    def test_load_rsrc_files(self) -> None:
        portraits = ResourceFork(DATA_FILES / "Portraits.rsrc")
        tacticals = ResourceFork(DATA_FILES / "Tacticals.rsrc")
        jewels = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        scen = ResourceFork(BYWATER_DIR / "Scenario.rsrc")

        self.assertTrue(portraits.has("cicn", 275))
        self.assertTrue(portraits.has("cicn", 257))
        self.assertTrue(tacticals.has("cicn", 9018))
        self.assertTrue(jewels.has("cicn", 392))
        self.assertTrue(jewels.has("PICT", 300))
        self.assertIn("City of Bywater", scen.get_str_list(-102))


class TestGraphicsDecoders(unittest.TestCase):
    """Verify cicn and PICT QuickDraw decoding and alpha masks."""

    def _assert_valid_rgba(self, img: Image.Image, expected_size: tuple[int, int], expect_transparency: bool) -> None:
        self.assertEqual(img.mode, "RGBA")
        self.assertEqual(img.size, expected_size)
        raw = img.tobytes()
        nonzero_rgb = sum(1 for i in range(0, len(raw), 4) if raw[i] or raw[i + 1] or raw[i + 2])
        self.assertGreater(nonzero_rgb, 0)
        alphas = raw[3::4]
        opaque = sum(1 for a in alphas if a == 255)
        trans = sum(1 for a in alphas if a == 0)
        self.assertGreater(opaque, 0)
        if expect_transparency:
            self.assertGreater(trans, 0)

    def test_decode_cicn_sprites_and_portraits(self) -> None:
        tacticals = ResourceFork(DATA_FILES / "Tacticals.rsrc")
        portraits = ResourceFork(DATA_FILES / "Portraits.rsrc")
        jewels = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")

        img_9018 = decode_cicn(tacticals.get("cicn", 9018).data)
        self._assert_valid_rgba(img_9018, (32, 32), expect_transparency=True)

        img_392 = decode_cicn(jewels.get("cicn", 392).data)
        self._assert_valid_rgba(img_392, (64, 32), expect_transparency=True)

        img_275 = decode_cicn(portraits.get("cicn", 275).data)
        self._assert_valid_rgba(img_275, (44, 44), expect_transparency=False)

        img_257 = decode_cicn(portraits.get("cicn", 257).data)
        self._assert_valid_rgba(img_257, (44, 44), expect_transparency=False)

    def test_decode_pict_300_and_slice_tile(self) -> None:
        jewels = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        sheet = decode_pict(jewels.get("PICT", 300).data)
        self.assertEqual(sheet.mode, "RGBA")
        self.assertEqual(sheet.size, (640, 320))

        tile = slice_tilesheet(sheet, tile_id=156)
        self.assertEqual(tile.size, (32, 32))
        self.assertGreater(len(set(tile.get_flattened_data())), 1)


class TestBinaryFormats(unittest.TestCase):
    """Verify Realmz map, monster, and character binary parsing."""

    def test_land_0_and_metadata(self) -> None:
        land0 = parse_land_grid(BYWATER_DIR / "Data LD", level=0)
        self.assertEqual(land0["total_levels"], 9)
        self.assertEqual(land0["width"], 90)
        self.assertEqual(land0["height"], 90)
        self.assertEqual(len(land0["tiles_yx"]), 90)
        self.assertEqual(len(land0["tiles_yx"][0]), 90)
        self.assertEqual(land0["stats"]["most_common_terrain_tile_id"], 156)

        rd0 = parse_rand_level(BYWATER_DIR / "Data RD", level=0)
        self.assertEqual(rd0["landlook"], 0)
        self.assertFalse(rd0["isdark"])
        self.assertFalse(rd0["uselos"])

        maps = parse_maps_metadata(BYWATER_DIR / "Data MD2")
        self.assertEqual(len(maps), 20)
        self.assertEqual((maps[0]["level"], maps[0]["startx"], maps[0]["starty"]), (0, 5, 5))

        doors0 = parse_doors(BYWATER_DIR / "Data DD", level=0)
        self.assertEqual(len(doors0), 100)

    def test_heroes_and_monsters(self) -> None:
        jewels = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        races = jewels.get_str_list(129)
        castes = jewels.get_str_list(131)

        char_files = sorted(p for p in (REALMZ_BASE / "Character Files").iterdir() if p.is_file())
        self.assertEqual(len(char_files), 6)
        heroes = [parse_character(p, race_names=races, caste_names=castes) for p in char_files]
        by_name = {h["name"]: h for h in heroes}
        self.assertIn("Kevlar", by_name)
        kevlar = by_name["Kevlar"]
        self.assertEqual(kevlar["level"], 3)
        self.assertEqual(kevlar["race_name"], "Orc")
        self.assertEqual(kevlar["caste_name"], "Berzerker")
        self.assertEqual(kevlar["pict_id"], 275)
        self.assertEqual(kevlar["icon_id"], 9018)

        monsters = parse_monsters(BYWATER_DIR / "Data MD")
        self.assertEqual(len(monsters), 155)
        self.assertEqual(monsters[0]["name"], "Giant Frog")
        self.assertEqual(monsters[0]["icon_id"], 392)


class TestEndToEndExport(unittest.TestCase):
    """Verify full export pipeline into a temporary directory."""

    def test_export_creates_all_artifacts_without_dashes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out_dir = Path(tmp)
            manifest = export_scenario(REALMZ_BASE, "City of Bywater", out_dir)
            self.assertGreaterEqual(manifest["artifact_count"], 11)
            expected_files = [
                "scenario.json",
                "manifest.json",
                "bywater_land_0.json",
                "bywater_land_0_preview.png",
                "heroes.json",
                "hero_kevlar_tactical_9018.png",
                "hero_kevlar_portrait_275.png",
                "monsters.json",
                "monster_giant_frog_392.png",
                "terrain_bywater_tilesheet_300.png",
                "terrain_bywater_tile_grass.png",
                "portrait_bywater_npc_257.png",
            ]
            for fname in expected_files:
                fpath = out_dir / fname
                self.assertTrue(fpath.is_file(), f"Missing output: {fname}")
                if fname.endswith(".json"):
                    raw_text = fpath.read_text(encoding="utf-8")
                    self.assertNotIn("\u2014", raw_text)
                    self.assertNotIn("\u2013", raw_text)
                    json.loads(raw_text)


if __name__ == "__main__":
    unittest.main()
