"""Unit tests for realmz-exporter parsers, graphics decoders, and CLI output."""

from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
import unittest
import wave

from PIL import Image

EXPORTER_DIR = Path(__file__).resolve().parent
if str(EXPORTER_DIR) not in sys.path:
    sys.path.insert(0, str(EXPORTER_DIR))

from audio import convert_madg_to_mod, decode_mac_snd, write_wav
from bywater_m1 import MONSTER_BALANCE_OVERRIDES, STARTER_PARTY_SPECS, build_bywater_m1_payloads
from export_realmz import export_scenario
from formats import (
    parse_battles_tactical,
    parse_character,
    parse_doors,
    parse_dungeon_grid,
    parse_land_grid,
    parse_mapstats,
    parse_maps_metadata,
    parse_monsters,
    parse_races_and_castes,
    parse_rand_level,
    parse_spellarea,
    parse_spells,
    parse_treasures,
)
from graphics import decode_cicn, decode_pict, slice_tilesheet
from rsrc import ResourceFork
from scenario_exporter import export_scenario_package, slugify_scenario


REALMZ_BASE = Path(r"C:\Users\karti\realmz\base\Realmz")
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

            m1_files = [
                "bywater_m1/land_0.json",
                "bywater_m1/dungeon_0.json",
                "bywater_m1/party_m1.json",
                "bywater_m1/monsters_bywater_m1.json",
                "bywater_m1/spells_items_m1.json",
            ]
            for rel in m1_files:
                fpath = out_dir / rel
                self.assertTrue(fpath.is_file(), f"Missing M1 output: {rel}")
                raw_text = fpath.read_text(encoding="utf-8")
                self.assertNotIn("\u2014", raw_text)
                self.assertNotIn("\u2013", raw_text)
                json.loads(raw_text)

            land0 = json.loads((out_dir / "bywater_m1/land_0.json").read_text(encoding="utf-8"))
            self.assertEqual((land0["width"], land0["height"]), (90, 90))
            self.assertEqual((land0["start_spawn"]["x"], land0["start_spawn"]["y"]), (2, 1))
            self.assertTrue(any(p["dest_map_id"] == "dungeon_0" and p["dest_x"] == 33 and p["dest_y"] == 72 for p in land0["portals"]))

            dung0 = json.loads((out_dir / "bywater_m1/dungeon_0.json").read_text(encoding="utf-8"))
            self.assertEqual((dung0["width"], dung0["height"]), (90, 90))
            self.assertTrue(dung0["is_dungeon"])
            self.assertEqual(dung0["tiles_yx"][72][33], 0)
            self.assertEqual(dung0["tiles_yx"][72][32], 4105)

            party = json.loads((out_dir / "bywater_m1/party_m1.json").read_text(encoding="utf-8"))
            self.assertEqual(len(party["heroes"]), 6)
            self.assertEqual(
                [h["archetype"] for h in party["heroes"]],
                ["Knight", "Cleric", "Ranger", "Mage", "Paladin", "Rogue"],
            )
            self.assertTrue(all(h["level"] == 1 for h in party["heroes"]))

            monsters_m1 = json.loads((out_dir / "bywater_m1/monsters_bywater_m1.json").read_text(encoding="utf-8"))
            self.assertGreaterEqual(monsters_m1["count"], 10)
            mon_names = {m["name"] for m in monsters_m1["monsters"]}
            self.assertTrue({"Giant Frog", "Goblin", "Giant Rat", "Skeletal Warrior"}.issubset(mon_names))

            si = json.loads((out_dir / "bywater_m1/spells_items_m1.json").read_text(encoding="utf-8"))
            self.assertEqual(len(si["spell_areas"]), 18)
            spell_aliases = {s["alias"] for s in si["spells"]}
            self.assertTrue({"Magic Missile", "Burning Hands", "Fireball", "Heal Light Wounds", "Bless", "Hold", "Sleep"}.issubset(spell_aliases))
            self.assertGreaterEqual(len(si["shops"]), 3)


class TestM1BinaryParsers(unittest.TestCase):
    """Verify M1 mapstats, dungeon grid, spellarea, and spell parsers."""

    def test_dungeon_grid_and_mapstats_and_spells(self) -> None:
        mapstats = parse_mapstats(DATA_FILES / "Data P BD")
        self.assertTrue(mapstats[156]["passable"])
        self.assertFalse(mapstats[61]["passable"])
        self.assertTrue(mapstats[61]["blocks_los"])

        dung0 = parse_dungeon_grid(BYWATER_DIR / "Data DL", level=0)
        self.assertEqual(dung0["total_levels"], 2)
        self.assertEqual(dung0["tiles_yx"][72][33], 0)
        self.assertEqual(dung0["tiles_yx"][72][32], 4105)
        rules_by_id = {r["tile_id"]: r for r in dung0["tile_rules"]}
        self.assertFalse(rules_by_id[129]["passable"])
        self.assertTrue(rules_by_id[129]["blocks_los"])
        self.assertTrue(rules_by_id[0]["passable"])
        self.assertTrue(rules_by_id[4105]["passable"])

        areas = parse_spellarea(DATA_FILES / "Data AD")
        self.assertEqual(len(areas), 18)
        self.assertEqual(len(areas[0]["offsets"]), 1)
        self.assertEqual(len(areas[4]["offsets"]), 13)

        fj = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        spells = parse_spells(DATA_FILES / "Data S", fj)
        by_id = {s["id"]: s for s in spells}
        self.assertEqual(by_id[1108]["name"], "Magic Darts")
        self.assertEqual(by_id[1306]["name"], "Fireball")
        self.assertEqual(by_id[2105]["name"], "Heal Small Wounds")


class TestP04UnequippedStatContract(unittest.TestCase):
    """Verify P0.4 fix: party_m1.json exports unequipped base ac and to_hit without gear baking."""

    def test_starter_party_exports_unequipped_base_stats(self) -> None:
        payloads = build_bywater_m1_payloads(REALMZ_BASE)
        party = payloads["party_m1.json"]
        items_by_id = {it["id"]: it for it in payloads["spells_items_m1.json"]["items"]}

        fj = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        races, castes = parse_races_and_castes(DATA_FILES / "Data Race", DATA_FILES / "Data Caste", fj)

        self.assertEqual(len(party["heroes"]), len(STARTER_PARTY_SPECS))
        for hero, spec in zip(party["heroes"], STARTER_PARTY_SPECS):
            self.assertEqual(hero["equipped_ids"], spec["equipped_ids"])
            attrs = hero["attributes"]
            caste = castes[spec["caste_id"]]
            # Agility is applied once at runtime (RealmzCore), never baked into exported base AC/missile.
            expected_base_ac = 10
            expected_base_to_hit = 10 + caste["tohit_base"] + max(0, (attrs["brawn"] - 12) // 2)

            self.assertEqual(hero["ac"], expected_base_ac, f"{hero['name']} base AC has baked equipment or Agility!")
            self.assertEqual(hero["to_hit"], expected_base_to_hit, f"{hero['name']} base To-Hit has baked equipment!")
            self.assertEqual(hero["missile"], caste["missile_base"], f"{hero['name']} missile has baked Agility!")

            # Verify runtime equipment application adds armor AC cleanly once.
            eq_ac = sum(items_by_id[iid]["armor_class_bonus"] for iid in hero["equipped_ids"] if iid in items_by_id)
            if eq_ac != 0:
                self.assertNotEqual(hero["ac"], expected_base_ac + eq_ac)

        # Verify on-disk extracted/bywater_m1/party_m1.json matches contract and is BOM-free.
        disk_party_path = Path(r"C:\Users\karti\realmz\remaster\extracted\bywater_m1\party_m1.json")
        if disk_party_path.is_file():
            raw_bytes = disk_party_path.read_bytes()
            self.assertFalse(raw_bytes.startswith(b"\xef\xbb\xbf"))
            disk_party = json.loads(raw_bytes.decode("utf-8"))
            self.assertEqual(disk_party["heroes"], party["heroes"])

    def test_ux_overhaul_item_hero_and_monster_fixes(self) -> None:
        payloads = build_bywater_m1_payloads(REALMZ_BASE)
        items = {it["id"]: it for it in payloads["spells_items_m1.json"]["items"]}
        self.assertEqual((items[452]["category"], items[452]["slot"]), ("Gloves", "Hands"))
        self.assertFalse(items[452]["is_junk"])
        self.assertEqual((items[454]["category"], items[454]["slot"]), ("Boots", "Feet"))
        self.assertEqual((items[455]["category"], items[455]["slot"]), ("Boots", "Feet"))
        self.assertEqual((items[250]["category"], items[250]["slot"]), ("Cloak", "Accessory2"))
        self.assertEqual((items[622]["category"], items[622]["slot"]), ("Accessory", "Accessory1"))
        self.assertEqual((items[675]["category"], items[675]["slot"]), ("Accessory", "Accessory2"))
        self.assertEqual((items[603]["category"], items[603]["slot"]), ("Potion", "Consumable"))
        self.assertEqual(items[603]["heal_dice"], "")
        self.assertEqual((items[723]["category"], items[723]["slot"]), ("Potion", "Consumable"))
        self.assertEqual((items[747]["category"], items[747]["slot"]), ("Supply", "Utility"))
        self.assertEqual((items[748]["category"], items[748]["slot"]), ("Supply", "Utility"))
        self.assertEqual(items[102]["name"], "Sparring Quarter Staff")
        self.assertEqual(items[2]["armor_class_bonus"], 2)

        heroes = {h["id"]: h for h in payloads["party_m1.json"]["heroes"]}
        self.assertEqual(heroes["hero_cleric"]["attacks_per_round"], 1.0)
        self.assertEqual(heroes["hero_mage"]["attacks_per_round"], 1.0)
        self.assertEqual(heroes["hero_ranger"]["attributes"]["agility"], 17)
        self.assertEqual(heroes["hero_rogue"]["attributes"]["luck"], 17)
        self.assertEqual(heroes["hero_rogue"]["attacks_per_round"], 1.5)

        monsters = {m["id"]: m for m in payloads["monsters_bywater_m1.json"]["monsters"]}
        self.assertEqual((monsters["skeletal_warrior"]["gold_min"], monsters["skeletal_warrior"]["gold_max"]), (3, 10))


class TestAudioExtraction(unittest.TestCase):
    """Verify Mac snd decoding, MADG->MOD conversion, and extracted audio_manifest.json."""

    def test_decode_mac_snd_format1_and_format2_variants(self) -> None:
        fj = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        # Test Format 1 stdSH (82), Format 1 extSH (702), Format 1 cmpSH uncompressed (10122),
        # Format 2 normal (20), and Format 2 off-by-6 (90, 643).
        sample_ids = [20, 82, 90, 643, 702, 10122]
        with tempfile.TemporaryDirectory() as tmp:
            for rid in sample_ids:
                self.assertTrue(fj.has("snd ", rid), f"Missing snd {rid}")
                decoded = decode_mac_snd(fj.get("snd ", rid).data)
                self.assertGreater(decoded["num_frames"], 0)
                self.assertGreater(decoded["duration_sec"], 0.0)
                self.assertGreater(decoded["sample_rate"], 1000)
                self.assertGreater(decoded["rms"], 0.0)

                wav_path = write_wav(Path(tmp) / f"snd_{rid}.wav", decoded)
                head = wav_path.read_bytes()[:12]
                self.assertEqual(head[:4], b"RIFF")
                self.assertEqual(head[8:12], b"WAVE")
                with wave.open(str(wav_path), "rb") as wf:
                    self.assertEqual(wf.getnframes(), decoded["num_frames"])
                    self.assertEqual(wf.getframerate(), decoded["sample_rate"])
                    self.assertEqual(wf.getnchannels(), decoded["channels"])

    def test_decode_all_142_base_snd_resources(self) -> None:
        fj = ResourceFork(DATA_FILES / "The Family Jewels.rsrc")
        snd_ids = fj.ids("snd ")
        self.assertEqual(len(snd_ids), 142)
        for rid in snd_ids:
            decoded = decode_mac_snd(fj.get("snd ", rid).data)
            self.assertGreater(decoded["num_frames"], 0, f"snd {rid} decoded 0 frames")
            self.assertGreater(decoded["rms"], 0.0, f"snd {rid} decoded silent")

    def test_convert_madg_outdoor_music_to_protracker_mod(self) -> None:
        outdoor_raw = (REALMZ_BASE / "Realmz Music" / "Outdoor Music").read_bytes()
        mod_bytes, title = convert_madg_to_mod(outdoor_raw)
        self.assertEqual(title, "after-the-rain")
        self.assertEqual(mod_bytes[1080:1084], b"M.K.")
        self.assertEqual(mod_bytes[950], 30)  # 30 song positions
        self.assertGreater(len(mod_bytes), 50000)

    def test_extracted_audio_manifest_and_assets_on_disk(self) -> None:
        audio_root = Path(r"C:\Users\karti\realmz\remaster\extracted\audio")
        manifest_path = audio_root / "audio_manifest.json"
        self.assertTrue(manifest_path.is_file(), "audio_manifest.json must be extracted")
        raw_bytes = manifest_path.read_bytes()
        self.assertFalse(raw_bytes.startswith(b"\xef\xbb\xbf"))
        raw_text = raw_bytes.decode("utf-8")
        self.assertNotIn("\u2014", raw_text)
        self.assertNotIn("\u2013", raw_text)

        manifest = json.loads(raw_text)
        self.assertEqual(manifest["sfx_count"], 142)
        self.assertEqual(manifest["skipped_sfx_count"], 0)
        self.assertEqual(manifest["music_count"], 11)
        self.assertIn("90", manifest["sfx_by_id"])

        for sfx_entry in manifest["sfx"][:15]:
            wav_p = audio_root / sfx_entry["relative_path"]
            self.assertTrue(wav_p.is_file(), f"Missing WAV: {wav_p}")
            hdr = wav_p.read_bytes()[:12]
            self.assertEqual(hdr[:4], b"RIFF")
            self.assertEqual(hdr[8:12], b"WAVE")
            self.assertGreater(sfx_entry["duration_sec"], 0.0)

        expected_tracks = {
            "battle", "camp", "cave", "create", "dungeon",
            "indoor", "items", "outdoor", "shop", "temple", "treasure",
        }
        self.assertEqual(set(manifest["music_by_key"].keys()), expected_tracks)
        for key, track in manifest["music_by_key"].items():
            ogg_p = audio_root / track["relative_path"]
            self.assertTrue(ogg_p.is_file(), f"Missing OGG: {ogg_p}")
            self.assertGreater(ogg_p.stat().st_size, 100_000, f"Track {key} unexpectedly small")
            self.assertEqual(ogg_p.read_bytes()[:4], b"OggS")
            self.assertGreater(track["duration_sec"], 30.0)
            self.assertGreater(track["max_volume_db"], -25.0)


class TestMultiScenarioExporter(unittest.TestCase):
    """Verify scenario-agnostic P1.5 extraction across Bywater + 2 additional classic scenarios."""

    def test_treasures_and_13x13_tactical_battles_parsers(self) -> None:
        treasures = parse_treasures(BYWATER_DIR / "Data TD")
        self.assertEqual(len(treasures), 76)
        self.assertGreater(treasures[5]["gold"], 0)
        self.assertGreater(len(treasures[5]["item_ids"]), 0)

        battles = parse_battles_tactical(BYWATER_DIR / "Data BD")
        self.assertEqual(len(battles), 257)
        b1 = battles[1]
        self.assertTrue(b1["is_valid"])
        self.assertEqual(len(b1["tactical_grid_13x13"]), 13)
        self.assertEqual(len(b1["tactical_grid_13x13"][0]), 13)
        self.assertGreater(len(b1["placements"]), 0)

    def test_export_three_scenarios_without_hardcoded_coords(self) -> None:
        scenarios = [
            "City of Bywater",
            "Prelude to Pestilence",
            "Assault on Giant Mountain",
        ]
        bywater_ov = EXPORTER_DIR / "overrides" / "bywater_overrides.yaml"
        with tempfile.TemporaryDirectory() as tmp:
            out_root = Path(tmp)
            for sname in scenarios:
                ov = bywater_ov if sname == "City of Bywater" else None
                pkg = export_scenario_package(REALMZ_BASE, sname, out_root, overrides_path=ov)
                slug = slugify_scenario(sname)
                pkg_dir = out_root / slug
                pkg_json_path = pkg_dir / "scenario_package.json"
                self.assertTrue(pkg_json_path.is_file())

                raw_bytes = pkg_json_path.read_bytes()
                self.assertFalse(raw_bytes.startswith(b"\xef\xbb\xbf"))
                raw_text = raw_bytes.decode("utf-8")
                self.assertNotIn("\u2014", raw_text)
                self.assertNotIn("\u2013", raw_text)

                c = pkg["counts"]
                self.assertGreaterEqual(c["land_levels"], 5, sname)
                self.assertGreaterEqual(c["dungeon_levels"], 1, sname)
                self.assertGreaterEqual(c["portals"], 5, sname)
                self.assertGreaterEqual(c["battles_valid"], 100, sname)
                self.assertGreaterEqual(c["monsters_valid"], 100, sname)
                self.assertGreaterEqual(c["shops"], 10, sname)
                self.assertGreaterEqual(c["treasures"], 50, sname)
                self.assertGreaterEqual(c["strings_non_empty"], 500, sname)

                # Verify land_0 and dungeon_0 grids, battles 13x13, and strings payloads
                land0 = json.loads((pkg_dir / "land_levels" / "land_0.json").read_text(encoding="utf-8"))
                self.assertEqual((land0["width"], land0["height"]), (90, 90))
                self.assertEqual(len(land0["tiles_yx"]), 90)
                self.assertGreater(len(land0["tile_rules"]), 100)

                dung0 = json.loads((pkg_dir / "dungeon_levels" / "dungeon_0.json").read_text(encoding="utf-8"))
                self.assertEqual((dung0["width"], dung0["height"]), (90, 90))
                self.assertTrue(dung0["is_dungeon"])

                battles_doc = json.loads((pkg_dir / "battles.json").read_text(encoding="utf-8"))
                valid_b = [b for b in battles_doc["battles"] if b["is_valid"]]
                self.assertEqual(len(valid_b), c["battles_valid"])
                self.assertEqual(len(valid_b[0]["tactical_grid_13x13"]), 13)

                strings_doc = json.loads((pkg_dir / "strings.json").read_text(encoding="utf-8"))
                self.assertEqual(strings_doc["non_empty_count"], c["strings_non_empty"])

                if sname == "City of Bywater":
                    portals_doc = json.loads((pkg_dir / "portals.json").read_text(encoding="utf-8"))
                    self.assertTrue(
                        any(
                            p["source_map_id"] == "land_0"
                            and p["x"] == 89
                            and p["y"] == 48
                            and p["dest_map_id"] == "dungeon_0"
                            and p["dest_x"] == 33
                            and p["dest_y"] == 72
                            for p in portals_doc["portals"]
                        )
                    )


class TestStage2MonsterAndBalanceOverrides(unittest.TestCase):
    """Verify Stage 2 monster HP/role overrides and StreamingAssets balance_overrides.json."""

    def test_monster_balance_overrides_and_roles(self) -> None:
        payloads = build_bywater_m1_payloads(REALMZ_BASE)
        monsters_doc = payloads["monsters_bywater_m1.json"]
        monsters = {m["id"]: m for m in monsters_doc["monsters"]}

        for m in monsters_doc["monsters"]:
            self.assertIn("role", m)
            self.assertIsInstance(m["role"], str)
            self.assertTrue(m["role"], f"Monster {m['id']} missing non-empty role")
            self.assertIn("special_abilities", m)
            self.assertIsInstance(m["special_abilities"], list)
            self.assertTrue(all(isinstance(a, str) for a in m["special_abilities"]))

        orc = monsters["orc_warrior"]
        self.assertEqual((orc["hit_dice"], orc["hp_bonus"]), (3, 3))
        self.assertEqual((orc["hp_min"], orc["hp_max"], orc["hp_default"]), (18, 24, 21))
        self.assertEqual(orc["xp_reward"], 65)
        self.assertEqual(orc["role"], "Bruiser")
        self.assertEqual(orc["special_abilities"], ["cleave_2tile"])

        frog = monsters["giant_frog"]
        self.assertEqual((frog["hp_min"], frog["hp_max"], frog["hp_default"]), (15, 19, 17))
        self.assertEqual(frog["xp_reward"], 70)
        self.assertEqual(frog["role"], "Controller")
        self.assertEqual(frog["special_abilities"], ["tongue_pull"])

        gob = monsters["goblin"]
        self.assertEqual(gob["name"], "Goblin")
        self.assertEqual((gob["hp_min"], gob["hp_max"], gob["hp_default"]), (8, 11, 9))
        self.assertEqual(gob["role"], "Skirmisher")
        self.assertEqual(gob["special_abilities"], [])

        arch = monsters["goblin_archer"]
        self.assertEqual((arch["hp_min"], arch["hp_max"], arch["hp_default"]), (6, 9, 8))
        self.assertEqual(arch["role"], "Ranged")
        self.assertEqual(arch["special_abilities"], ["high_ground_seeker"])

        self.assertEqual(monsters["goblin_shaman"]["role"], "Caster")
        self.assertEqual(monsters["orc_archer"]["role"], "Ranged")

        extracted_dir = Path(r"C:\Users\karti\realmz\remaster\extracted\bywater_m1")
        extracted_jsons = sorted(p.name for p in extracted_dir.glob("*.json"))
        self.assertEqual(len(extracted_jsons), 5)

        for disk_path in (
            extracted_dir / "monsters_bywater_m1.json",
            Path(r"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\StreamingAssets\bywater_m1\monsters_bywater_m1.json"),
        ):
            self.assertTrue(disk_path.is_file(), f"Missing {disk_path}")
            raw_bytes = disk_path.read_bytes()
            self.assertFalse(raw_bytes.startswith(b"\xef\xbb\xbf"), f"BOM found in {disk_path}")
            raw_text = raw_bytes.decode("utf-8")
            self.assertNotIn("\u2014", raw_text)
            self.assertNotIn("\u2013", raw_text)
            self.assertEqual(json.loads(raw_text), monsters_doc)

    def test_streaming_balance_overrides_json(self) -> None:
        path = Path(r"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\StreamingAssets\bywater_m1\balance_overrides.json")
        self.assertTrue(path.is_file(), f"Missing {path}")
        raw_bytes = path.read_bytes()
        self.assertFalse(raw_bytes.startswith(b"\xef\xbb\xbf"), "balance_overrides.json must be BOM-free")
        raw_text = raw_bytes.decode("utf-8")
        self.assertNotIn("\u2014", raw_text)
        self.assertNotIn("\u2013", raw_text)

        data = json.loads(raw_text)
        self.assertEqual(data["version"], 1)

        mon_by_id = {m["id"]: m for m in data["monsters"]}
        self.assertEqual(set(mon_by_id.keys()), set(MONSTER_BALANCE_OVERRIDES.keys()))
        self.assertEqual(mon_by_id["goblin"]["alias"], "Goblin Raider")
        for mid, expected in MONSTER_BALANCE_OVERRIDES.items():
            for k, v in expected.items():
                self.assertEqual(mon_by_id[mid][k], v, f"Mismatch on {mid}.{k}")

        surv = data["survival"]
        self.assertEqual(surv["torch_price"], 12)
        self.assertEqual(surv["ration_price"], 5)
        self.assertEqual(surv["torch_turn_duration"], 30)
        self.assertEqual(surv["bandage_max_charges"], 2)
        self.assertEqual(
            surv["vision_radii"],
            {
                "day_base": 11,
                "day_scout_bonus": 2,
                "dusk_unlit": 8,
                "dusk_lit_torch": 10,
                "night_unlit": 4,
                "night_lit_torch": 9,
                "dungeon_unlit": 3,
                "dungeon_lit_torch": 8,
            },
        )

        sets_by_id = {s["set_id"]: s for s in data["equipment_sets"]}
        self.assertEqual(set(sets_by_id.keys()), {"scouts_leather", "iron_vanguard", "thornwood_stalker", "cryptbreaker"})
        self.assertEqual(sets_by_id["scouts_leather"]["item_ids"], [209, 210, 211, 450, 452, 454])
        self.assertEqual(sets_by_id["iron_vanguard"]["item_ids"], [400, 418, 455, 20, 10, 11])
        self.assertTrue(sets_by_id["thornwood_stalker"]["is_preview"])
        self.assertEqual(sets_by_id["thornwood_stalker"]["item_ids"], [9101, 9102, 9103])
        self.assertTrue(sets_by_id["cryptbreaker"]["is_preview"])
        self.assertEqual(sets_by_id["cryptbreaker"]["item_ids"], [9201, 9202, 9203])

        quests = data["quests"]
        self.assertEqual(len(quests), 4)
        self.assertEqual(
            [q["quest_id"] for q in quests],
            ["bounty_river_frogs", "bounty_road_goblins", "bounty_thornwood_orcs", "bounty_cavern_undead"],
        )
        for q in quests:
            self.assertEqual(q["source"], "BywaterNoticeBoard")
            self.assertTrue(q["is_notice_board_bounty"])
            for field in ("title", "description", "target_monster_id", "target_count", "gold_reward", "xp_reward", "location_hint"):
                self.assertIn(field, q)


if __name__ == "__main__":
    unittest.main()
