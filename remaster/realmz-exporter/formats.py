"""Binary parsers for Realmz scenario, map, monster, and character files."""

from __future__ import annotations

from collections import Counter
from pathlib import Path
import struct
from typing import Any, Dict, List, Optional, Tuple


LAND_WIDTH = 90
LAND_HEIGHT = 90
LAND_BYTES = LAND_WIDTH * LAND_HEIGHT * 2  # 16,200 bytes
RANDLEVEL_BYTES = 644
MAPS_BYTES = 340
DOOR_COUNT_PER_LEVEL = 100
DOOR_BYTES = 40
MONSTER_BYTES = 210
CHARACTER_BYTES = 872


def sanitize_text(text: str) -> str:
    """Normalize text to avoid em-dashes, en-dashes, and stray control bytes."""
    cleaned = text.replace("\u2014", "-").replace("\u2013", "-")
    return "".join(ch for ch in cleaned if ch >= " " or ch in "\n\t")


def decode_cstr(raw: bytes) -> str:
    """Decode a null-terminated MacRoman C string safely."""
    prefix = raw.split(b"\x00", 1)[0]
    return sanitize_text(prefix.decode("mac_roman", errors="replace")).strip()


def decode_pstr(raw: bytes) -> str:
    """Decode a Pascal Str255 buffer safely."""
    if not raw:
        return ""
    slen = raw[0]
    return sanitize_text(raw[1 : 1 + slen].decode("mac_roman", errors="replace"))


def parse_scenario_header(path: Path) -> Dict[str, int]:
    """Parse the 5 big-endian int32 startup fields from the scenario header file."""
    raw = Path(path).read_bytes()
    if len(raw) < 20:
        raise ValueError(f"Scenario header file too short: {path}")
    reclevel, maxlevel, landlevel, lookx, looky = struct.unpack(">5i", raw[:20])
    return {
        "recommended_level": reclevel,
        "max_level": maxlevel,
        "start_land_level": landlevel,
        "start_look_x": lookx,
        "start_look_y": looky,
        "file_size_bytes": len(raw),
    }


def normalize_tile_id(raw_val: int) -> Dict[str, Any]:
    """Decode Realmz tile flags and repeated 1000-offset bumps into a base tile ID."""
    if raw_val < 0:
        val = raw_val
        bumps = 0
        while val < -999:
            val += 1000
            bumps += 1
        return {
            "raw": raw_val,
            "base_id": val,
            "is_negative": True,
            "is_note": False,
            "is_path": False,
            "is_secret": False,
            "bumps": bumps,
        }

    is_note = bool(raw_val & 0x4000)
    is_path = bool(raw_val & 0x2000)
    val = raw_val & ~0x6000
    bumps = 0
    is_secret = False

    if val > 999:
        val -= 1000
        bumps += 1
        if val > 999:
            val -= 1000
            bumps += 1
            is_secret = True
            if val > 999:
                val -= 1000
                bumps += 1
                is_secret = False

    return {
        "raw": raw_val,
        "base_id": val,
        "is_negative": False,
        "is_note": is_note,
        "is_path": is_path,
        "is_secret": is_secret,
        "bumps": bumps,
    }


def parse_land_grid(data_ld_path: Path, level: int = 0) -> Dict[str, Any]:
    """Extract a 90x90 land level from Data LD into row-major [y][x] order."""
    raw = Path(data_ld_path).read_bytes()
    total_levels = len(raw) // LAND_BYTES
    if level < 0 or level >= total_levels:
        raise ValueError(f"Land level {level} out of range (0..{total_levels - 1})")

    offset = level * LAND_BYTES
    vals = struct.unpack(f">{LAND_WIDTH * LAND_HEIGHT}h", raw[offset : offset + LAND_BYTES])

    # Realmz stores field[x][y] where outer index is x (0..89) and inner is y (0..89).
    tiles_yx: List[List[int]] = [[0] * LAND_WIDTH for _ in range(LAND_HEIGHT)]
    tile_counts: Counter[int] = Counter()
    special_count = 0
    bumped_count = 0

    for x in range(LAND_WIDTH):
        col_off = x * LAND_HEIGHT
        for y in range(LAND_HEIGHT):
            raw_val = vals[col_off + y]
            tiles_yx[y][x] = raw_val
            info = normalize_tile_id(raw_val)
            if info["is_negative"]:
                special_count += 1
            else:
                if info["bumps"] > 0:
                    bumped_count += 1
                base_id = info["base_id"]
                if 1 <= base_id <= 200:
                    tile_counts[base_id] += 1

    most_common_tile_id, most_common_tile_count = (
        tile_counts.most_common(1)[0] if tile_counts else (1, 0)
    )
    return {
        "level": level,
        "total_levels": total_levels,
        "width": LAND_WIDTH,
        "height": LAND_HEIGHT,
        "layout": "row_major_yx",
        "tiles_yx": tiles_yx,
        "stats": {
            "total_cells": LAND_WIDTH * LAND_HEIGHT,
            "negative_special_cells": special_count,
            "bumped_1000_cells": bumped_count,
            "most_common_terrain_tile_id": most_common_tile_id,
            "most_common_terrain_tile_count": most_common_tile_count,
            "top_10_terrain_tiles": [
                {"tile_id": tid, "count": cnt} for tid, cnt in tile_counts.most_common(10)
            ],
        },
    }


def parse_rand_level(data_rd_path: Path, level: int = 0) -> Dict[str, Any]:
    """Parse a 644-byte struct randlevel record from Data RD."""
    raw = Path(data_rd_path).read_bytes()
    total_levels = len(raw) // RANDLEVEL_BYTES
    if level < 0 or level >= total_levels:
        raise ValueError(f"RandLevel {level} out of range (0..{total_levels - 1})")

    rec = raw[level * RANDLEVEL_BYTES : (level + 1) * RANDLEVEL_BYTES]
    landlook = rec[520]
    isdark = bool(rec[521])
    uselos = bool(rec[522])

    rects = []
    for i in range(20):
        top, left, bottom, right = struct.unpack(">4h", rec[i * 8 : (i + 1) * 8])
        pct = struct.unpack(">h", rec[160 + i * 2 : 162 + i * 2])[0]
        b_min, b_max = struct.unpack(">2h", rec[200 + i * 4 : 204 + i * 4])
        if any((top, left, bottom, right, pct, b_min, b_max)):
            rects.append(
                {
                    "slot": i,
                    "rect": {"top": top, "left": left, "bottom": bottom, "right": right},
                    "percent": pct,
                    "battle_range": [b_min, b_max],
                }
            )

    return {
        "level": level,
        "landlook": landlook,
        "tileset_pict_id": 300 + landlook,
        "isdark": isdark,
        "uselos": uselos,
        "active_encounter_zones": len(rects),
        "encounter_zones": rects,
    }


def parse_maps_metadata(data_md2_path: Path) -> List[Dict[str, Any]]:
    """Parse all 20 struct maps entries (340 bytes each) from Data MD2."""
    raw = Path(data_md2_path).read_bytes()
    count = len(raw) // MAPS_BYTES
    maps: List[Dict[str, Any]] = []
    for idx in range(count):
        rec = raw[idx * MAPS_BYTES : (idx + 1) * MAPS_BYTES]
        startx, starty, level, pictid, iconsize, show, isdungeon, spare = struct.unpack(
            ">8h", rec[60:76]
        )
        rect_vals = struct.unpack(">4h", rec[76:84])
        note = decode_pstr(rec[84:340])
        maps.append(
            {
                "index": idx,
                "startx": startx,
                "starty": starty,
                "level": level,
                "pictid": pictid,
                "iconsize": iconsize,
                "show": show,
                "isdungeon": bool(isdungeon),
                "spare": spare,
                "rect": {
                    "top": rect_vals[0],
                    "left": rect_vals[1],
                    "bottom": rect_vals[2],
                    "right": rect_vals[3],
                },
                "note": note,
            }
        )
    return maps


def parse_doors(data_dd_path: Path, level: int = 0) -> List[Dict[str, Any]]:
    """Parse the 100 struct door entries (40 bytes each) for a land level in Data DD."""
    raw = Path(data_dd_path).read_bytes()
    block_size = DOOR_COUNT_PER_LEVEL * DOOR_BYTES
    total_levels = len(raw) // block_size
    if level < 0 or level >= total_levels:
        raise ValueError(f"Door level {level} out of range (0..{total_levels - 1})")

    base = level * block_size
    doors: List[Dict[str, Any]] = []
    for idx in range(DOOR_COUNT_PER_LEVEL):
        rec = raw[base + idx * DOOR_BYTES : base + (idx + 1) * DOOR_BYTES]
        doorid, landid, landx, landy, percent = struct.unpack(">ibbbb", rec[:8])
        codes = list(struct.unpack(">8h", rec[8:24]))
        ids = list(struct.unpack(">8h", rec[24:40]))
        if doorid == 0 and not any(codes) and not any(ids):
            continue
        doors.append(
            {
                "slot": idx,
                "door_id": doorid,
                "land_id": landid,
                "x": landx,
                "y": landy,
                "percent": percent,
                "codes": codes,
                "ids": ids,
            }
        )
    return doors


def parse_monsters(data_md_path: Path) -> List[Dict[str, Any]]:
    """Parse all 155 struct monster records (210 bytes each) from Data MD."""
    raw = Path(data_md_path).read_bytes()
    count = len(raw) // MONSTER_BYTES
    monsters: List[Dict[str, Any]] = []
    for idx in range(count):
        rec = raw[idx * MONSTER_BYTES : (idx + 1) * MONSTER_BYTES]
        hd, bonus, dx, name_flag, movementmax = struct.unpack(">5B", rec[0:5])
        ac, magres, dist, traiter, size = struct.unpack(">5b", rec[5:10])
        money = list(struct.unpack(">3h", rec[58:64]))
        weapon, iconid, spellpoints, exp, stamina, staminamax = struct.unpack(">6h", rec[96:108])
        maxspellpoints = struct.unpack(">h", rec[168:170])[0]

        raw_name_bytes = rec[170:210].split(b"\x00", 1)[0]
        decoded_name = sanitize_text(raw_name_bytes.decode("mac_roman", errors="replace")).strip()
        is_ascii_clean = bool(decoded_name) and all(32 <= ord(c) <= 126 for c in decoded_name)
        is_valid = iconid > 0 and is_ascii_clean and staminamax >= 0

        monsters.append(
            {
                "index": idx,
                "name": decoded_name if is_valid else "",
                "is_valid": is_valid,
                "icon_id": iconid,
                "hd": hd,
                "bonus": bonus,
                "dx": dx,
                "movement_max": movementmax,
                "ac": ac,
                "magic_resistance": magres,
                "dist": dist,
                "traiter": traiter,
                "size": size,
                "exp": exp,
                "stamina": stamina,
                "stamina_max": staminamax,
                "spell_points": spellpoints,
                "max_spell_points": maxspellpoints,
                "weapon_id": weapon,
                "money": money,
            }
        )
    return monsters


def parse_character(
    char_path: Path,
    race_names: Optional[List[str]] = None,
    caste_names: Optional[List[str]] = None,
) -> Dict[str, Any]:
    """Parse an 872-byte Realmz Character Files record."""
    raw = Path(char_path).read_bytes()
    if len(raw) != CHARACTER_BYTES:
        raise ValueError(f"Expected {CHARACTER_BYTES} bytes for character {char_path}, got {len(raw)}")

    shorts = struct.unpack(">146h", raw[:292])
    version = shorts[0]
    verify1 = shorts[1]
    tohit = shorts[2]
    dodge = shorts[3]
    missile = shorts[4]
    twohand = shorts[5]
    ac = shorts[22]
    damage = shorts[23]
    race_id = shorts[24]
    caste_id = shorts[25]
    spellcaster_type = shorts[26]
    gender = shorts[27]
    level = shorts[28]
    movement = shorts[29]
    movementmax = shorts[30]
    attacks = shorts[31]
    stamina = shorts[39]
    staminamax = shorts[40]
    pictid = shorts[41]
    iconid = shorts[42]
    spellpoints = shorts[43]
    spellpointsmax = shorts[44]
    verify2 = shorts[145]

    age, exp = struct.unpack(">2i", raw[492:500])
    load, loadmax, money0, money1, money2 = struct.unpack(">5H", raw[500:510])
    inbattle = bool(raw[516])
    st, iq, wi, de, co, lu = struct.unpack(">6B", raw[517:523])
    name = decode_cstr(raw[607:637])
    # 1-byte alignment pad at 637 before short verify3 at 638..640 on 68k/PPC.
    verify3 = struct.unpack(">h", raw[638:640])[0]

    race_name = (
        race_names[race_id - 1]
        if race_names and 1 <= race_id <= len(race_names)
        else f"Race {race_id}"
    )
    caste_name = (
        caste_names[caste_id - 1]
        if caste_names and 1 <= caste_id <= len(caste_names)
        else f"Caste {caste_id}"
    )

    return {
        "file_name": char_path.name,
        "name": name or char_path.name,
        "version": version,
        "race_id": race_id,
        "race_name": sanitize_text(race_name),
        "caste_id": caste_id,
        "caste_name": sanitize_text(caste_name),
        "gender": gender,
        "level": level,
        "stamina": stamina,
        "stamina_max": staminamax,
        "spell_points": spellpoints,
        "spell_points_max": spellpointsmax,
        "spellcaster_type": spellcaster_type,
        "pict_id": pictid,
        "icon_id": iconid,
        "ac": ac,
        "tohit": tohit,
        "dodge": dodge,
        "missile": missile,
        "twohand": twohand,
        "damage": damage,
        "attacks": attacks,
        "movement": movement,
        "movement_max": movementmax,
        "age_days": age,
        "exp": exp,
        "load": load,
        "load_max": loadmax,
        "money": [money0, money1, money2],
        "in_battle": inbattle,
        "attributes": {
            "brawn": st,
            "knowledge": iq,
            "judgment": wi,
            "agility": de,
            "vitality": co,
            "luck": lu,
        },
        "verify": [verify1, verify2, verify3],
    }
