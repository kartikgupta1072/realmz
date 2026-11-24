# realmz-exporter

Self-contained Python 3 CLI for extracting Classic Mac OS `.rsrc` graphics (`cicn`, `PICT`) and big-endian Realmz binary scenario/character files into structured JSON and RGBA PNGs.

## Why Self-Contained Python

`C:\Users\karti\.local\tools\resource_dasm\bin\release-windows\realmz_dasm.exe` (`v2026-09-03`) currently hits an upstream Windows UCRT `EINVAL 22` crash when `phosg::fopen_binary_raw` passes `"wtb"` while writing `script.txt`. `realmz-exporter` reads the Classic Mac `.rsrc` data fork and Realmz big-endian structs directly in pure Python + Pillow with zero external binary dependencies. On POSIX/Linux/macOS, `realmz_dasm` can still be used independently for script disassembly cross-checks.

## Requirements

- Python 3.12+
- Pillow 13.0.0+

## Usage

```powershell
python C:\realmz\remaster\realmz-exporter\export_realmz.py `
  --realmz-base C:\realmz\base\Realmz `
  --scenario "City of Bywater" `
  --out C:\realmz\remaster\extracted
```

## Extracted Artifacts (`City of Bywater`)

- `scenario.json` - Scenario startup header, map names (`Scenario.rsrc`), `Data MD2` map metadata, and Land 0 summary.
- `bywater_land_0.json` - Row-major `90x90` Land 0 tile grid (`tiles_yx[y][x]` converted from Realmz `field[x][y]`), `Data RD[0]` flags (`landlook`, `isdark`, `uselos`), start coordinates, and `Data DD[0]` AP door summary.
- `bywater_land_0_preview.png` - `90x90` color minimap preview generated from `PICT 300` tile averages and Land 0 AP door coordinates.
- `heroes.json` - All 6 starter heroes from `Character Files/*` resolved against `STR# 129` (races) and `STR# 131` (castes).
- `hero_kevlar_tactical_9018.png` - `32x32` RGBA tactical icon (`Tacticals.rsrc` `cicn 9018`).
- `hero_kevlar_portrait_275.png` - `44x44` RGBA portrait (`Portraits.rsrc` `cicn 275`).
- `monsters.json` - All 155 monster slots from `Data MD` (`133` active populated entries + slot validity flags).
- `monster_giant_frog_392.png` - `64x32` RGBA monster sprite (`The Family Jewels.rsrc` `cicn 392`).
- `terrain_bywater_tilesheet_300.png` - `640x320` RGBA terrain tilesheet (`The Family Jewels.rsrc` `PICT 300`).
- `terrain_bywater_tile_grass.png` - `32x32` most common Land 0 terrain tile (`tile_id=156`).
- `portrait_bywater_npc_257.png` - `44x44` RGBA NPC portrait (`Portraits.rsrc` `cicn 257`).
- `bywater_m1/land_0.json` - `90x90` M1 Land 0 grid, `Data P BD` tile rules, town POIs, `Dungeon 0` portal `(89,48)->(33,72)`, and encounter zones.
- `bywater_m1/dungeon_0.json` - `90x90` row-major M1 Dungeon 0 grid, bitflag passability/LOS rules, exit portal `(32,72)->(88,48)`, and encounter zones.
- `bywater_m1/party_m1.json` - Starter 6-hero Level-1 party (`Knight`, `Cleric`, `Ranger`, `Mage`, `Paladin`, `Rogue`) with unequipped base `ac` and `to_hit` alongside `equipped_ids` (P0.4 contract: `RealmzCore` applies equipment bonuses once at runtime), spells, and shared stash.
- `bywater_m1/monsters_bywater_m1.json` - 12 authentic early Bywater monsters extracted from `Data MD` & `Data BD`.
- `bywater_m1/spells_items_m1.json` - All 18 `Data AD` `spellarea` footprints, starter spells, starter equipment/potions (`Data ID`/`Data NI`), and Bywater town shops (`Data SD`).
- `audio/audio_manifest.json` - Audio extraction manifest (`audio.py`): 142 base SFX (`The Family Jewels.rsrc` `snd ` -> PCM `.wav`), 3 scenario SFX, and all 11 `Realmz Music` suites (`10` ProTracker `M.K.` MOD + `1` PlayerPRO `MADG` `Outdoor Music` converted to MOD -> `libvorbis` `.ogg`).
- `scenarios/<slug>/scenario_package.json` - Scenario-agnostic `ScenarioPackageJson` packages (`scenario_exporter.py`) covering `City of Bywater`, `Prelude to Pestilence`, and `Assault on Giant Mountain` (all land/dungeon grids, binary-decoded portals, shops, treasure tables, `13x13` tactical battle grids, monsters, and `Data SD2` strings, with optional `overrides/bywater_overrides.yaml` art/name bindings).
- `manifest.json` - SHA-256 checksums, dimensions, byte sizes, and source resource IDs for all extracted files.

## Audio & Multi-Scenario CLI

```powershell
# Extract all 142 base SFX (.wav) + 11 Realmz Music suites (.ogg)
python C:\realmz\remaster\realmz-exporter\audio.py `
  --realmz-base C:\realmz\base\Realmz `
  --out C:\realmz\remaster\extracted\audio

# Export generic ScenarioPackageJson bundles
python C:\realmz\remaster\realmz-exporter\scenario_exporter.py `
  --scenarios "City of Bywater" "Prelude to Pestilence" "Assault on Giant Mountain" `
  --out C:\realmz\remaster\extracted\scenarios
```

## Tests

```powershell
python -m unittest discover -s C:\realmz\remaster\realmz-exporter
```
