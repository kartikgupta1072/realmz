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
        monster_types = [bool(b) for b in rec[10:18]]
        no_of_attacks, no_of_mag_attacks = struct.unpack(">2b", rec[18:20])
        attacks = [list(struct.unpack(">4b", rec[20 + k * 4 : 24 + k * 4])) for k in range(5)]
        damplus, castpercent, runpercent, surrenderpercent, missilepercent, cansum = struct.unpack(">6b", rec[40:46])
        saves = list(struct.unpack(">6b", rec[46:52]))
        spell_immunities = [bool(b) for b in rec[52:58]]
        money = list(struct.unpack(">3h", rec[58:64]))
        spells = [s for s in struct.unpack(">10h", rec[64:84]) if s > 0]
        items = [it for it in struct.unpack(">6h", rec[84:96]) if it > 0]
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
                "monster_types": monster_types,
                "num_attacks": max(1, no_of_attacks),
                "num_magic_attacks": no_of_mag_attacks,
                "attacks": attacks,
                "damage_bonus": damplus,
                "cast_chance_pct": max(0, castpercent),
                "missile_chance_pct": max(0, missilepercent),
                "saves": saves,
                "spell_immunities": spell_immunities,
                "spell_ids": spells,
                "loot_item_ids": items,
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


MAPSTATS_BYTES = 40
MAPSTATS_COUNT = 201
SPELLAREA_COUNT = 18
SPELLAREA_DIM = 7
SPELL_BYTES = 30
ITEMATTR_BYTES = 100
RACE_BYTES = 960
CASTE_BYTES = 576
SHOP_BYTES = 3002
BATTLE_BYTES = 346
EDCD_BYTES = 10
SD2_BYTES = 256


def parse_mapstats(bd_path: Path) -> Dict[int, Dict[str, Any]]:
    """Parse 201 struct mapstats records (40 bytes each) from a * BD tileset metadata file."""
    raw = Path(bd_path).read_bytes()
    max_entries = min(MAPSTATS_COUNT, len(raw) // MAPSTATS_BYTES)
    rules: Dict[int, Dict[str, Any]] = {}
    for tid in range(max_entries):
        rec = raw[tid * MAPSTATS_BYTES : (tid + 1) * MAPSTATS_BYTES]
        vals = struct.unpack(">20h", rec)
        sound_id, time_cost, solid, shore, need_boat, is_path, los, fly_float, forest = vals[:9]
        move_cost = max(1, time_cost // 2) if time_cost > 0 else 1
        passable = (solid < 2) and (need_boat != 2)
        rules[tid] = {
            "tile_id": tid,
            "sound_id": sound_id,
            "time": time_cost,
            "move_cost": move_cost,
            "solid": solid,
            "passable": passable,
            "shore": bool(shore),
            "need_boat": need_boat,
            "needs_boat": need_boat == 2,
            "is_path": bool(is_path),
            "blocks_los": bool(los),
            "fly_float": fly_float,
            "is_forest": forest != 0,
            "forest": forest,
        }
    return rules


def parse_solids(solids_path: Path) -> List[int]:
    """Parse Data Solids (1024 uint8 flags) returning positive magnitudes of solid negative tiles."""
    raw = Path(solids_path).read_bytes()
    return [idx for idx, flag in enumerate(raw) if flag != 0]


def decode_dungeon_cell(raw_val: int) -> Dict[str, Any]:
    """Decode Realmz Data DL 16-bit dungeon cell bitflags."""
    is_wall = bool(raw_val & 0x0001)
    is_hdoor = bool(raw_val & 0x0002)
    is_vdoor = bool(raw_val & 0x0004)
    is_stairs = bool(raw_val & 0x0008)
    is_pillar = bool(raw_val & 0x0010)
    is_note = bool(raw_val & 0x0020)
    is_secret = bool(raw_val & 0x0040)
    is_dark = bool(raw_val & 0x0080)
    has_trigger = bool(raw_val & 0x1000)
    is_arch = bool(raw_val & 0x2000)
    is_open = bool(raw_val & 0x4000)

    has_opening = is_hdoor or is_vdoor or is_stairs or is_arch or is_open
    passable = not (is_wall and not has_opening)
    blocks_los = is_wall and not (is_stairs or is_arch or is_open)

    return {
        "tile_id": raw_val,
        "passable": passable,
        "move_cost": 1,
        "blocks_los": blocks_los,
        "is_forest": False,
        "is_path": False,
        "needs_boat": False,
        "sound_id": 0,
        "is_wall": is_wall,
        "is_door": is_hdoor or is_vdoor,
        "is_stairs": is_stairs,
        "is_pillar": is_pillar,
        "is_arch": is_arch,
        "is_dark": is_dark,
        "has_trigger": has_trigger,
        "is_note": is_note,
        "is_secret": is_secret,
    }


def parse_dungeon_grid(data_dl_path: Path, level: int = 0) -> Dict[str, Any]:
    """Extract a 90x90 dungeon level from Data DL (stored row-major [y][x])."""
    raw = Path(data_dl_path).read_bytes()
    total_levels = len(raw) // LAND_BYTES
    if level < 0 or level >= total_levels:
        raise ValueError(f"Dungeon level {level} out of range (0..{total_levels - 1})")

    offset = level * LAND_BYTES
    vals = struct.unpack(f">{LAND_WIDTH * LAND_HEIGHT}h", raw[offset : offset + LAND_BYTES])

    tiles_yx: List[List[int]] = [[0] * LAND_WIDTH for _ in range(LAND_HEIGHT)]
    unique_counts: Counter[int] = Counter()
    for y in range(LAND_HEIGHT):
        row_off = y * LAND_WIDTH
        for x in range(LAND_WIDTH):
            v = vals[row_off + x]
            tiles_yx[y][x] = v
            unique_counts[v] += 1

    tile_rules = [decode_dungeon_cell(v) for v in sorted(unique_counts.keys())]
    return {
        "level": level,
        "total_levels": total_levels,
        "width": LAND_WIDTH,
        "height": LAND_HEIGHT,
        "layout": "row_major_yx",
        "tiles_yx": tiles_yx,
        "tile_rules": tile_rules,
        "unique_value_count": len(unique_counts),
    }


def parse_spellarea(data_ad_path: Path) -> List[Dict[str, Any]]:
    """Parse Data AD spellarea[18][7][7] boolean footprints."""
    raw = Path(data_ad_path).read_bytes()
    expected = SPELLAREA_COUNT * SPELLAREA_DIM * SPELLAREA_DIM
    if len(raw) < expected:
        raise ValueError(f"Data AD too short ({len(raw)} bytes, expected {expected})")

    templates: List[Dict[str, Any]] = []
    for idx in range(SPELLAREA_COUNT):
        block = raw[idx * 49 : (idx + 1) * 49]
        grid_7x7: List[List[bool]] = []
        offsets: List[Dict[str, int]] = []
        for dy in range(SPELLAREA_DIM):
            row: List[bool] = []
            for dx in range(SPELLAREA_DIM):
                active = block[dy * SPELLAREA_DIM + dx] != 0
                row.append(active)
                if active:
                    offsets.append({"dx": dx - 3, "dy": dy - 3})
            grid_7x7.append(row)
        templates.append(
            {
                "index": idx,
                "grid_7x7": grid_7x7,
                "offsets": offsets,
            }
        )
    return templates


def parse_spells(data_s_path: Path, fj_rsrc: Any) -> List[Dict[str, Any]]:
    """Parse standard Sorcerer, Priest, and Enchanter spells from Data S + STR# resources."""
    raw = Path(data_s_path).read_bytes()
    schools = [(0, "Sorcerer", 1000), (1, "Priest", 2000), (2, "Enchanter", 3000)]
    spells: List[Dict[str, Any]] = []

    for caste_idx, school_name, str_base in schools:
        for lvl in range(7):
            names = fj_rsrc.get_str_list(str_base + lvl) if fj_rsrc.has("STR#", str_base + lvl) else []
            descs = fj_rsrc.get_str_list(-(str_base + lvl)) if fj_rsrc.has("STR#", -(str_base + lvl)) else []
            for slot_idx, raw_name in enumerate(names):
                clean_name = sanitize_text(raw_name).strip()
                if not clean_name:
                    continue
                rec_idx = caste_idx * 105 + lvl * 15 + slot_idx
                off = rec_idx * SPELL_BYTES
                if off + SPELL_BYTES > len(raw):
                    continue
                rec = raw[off : off + SPELL_BYTES]
                vals = struct.unpack(">25bBb3B", rec)
                desc = sanitize_text(descs[slot_idx]).strip() if slot_idx < len(descs) else ""
                desc = desc.replace("\r", " ").strip()
                spell_id = 1101 + caste_idx * 1000 + lvl * 100 + slot_idx
                spells.append(
                    {
                        "id": spell_id,
                        "name": clean_name,
                        "description": desc,
                        "school": school_name,
                        "level": lvl + 1,
                        "range1": vals[0],
                        "range2": vals[1],
                        "sp_cost": abs(vals[10]),
                        "damage1": vals[11],
                        "damage2": vals[12],
                        "power_damage1": vals[13],
                        "power_damage2": vals[14],
                        "duration1": vals[15],
                        "duration2": vals[16],
                        "target_type": vals[23],
                        "size": vals[24],
                        "special": vals[25],
                        "damage_type_id": vals[26],
                        "spell_class": vals[27],
                        "cannot": vals[8],
                    }
                )
    return spells


def _lookup_item_text(rsrc_fork: Any, group_base: int, offset_in_group: int) -> Tuple[str, str, str]:
    """Fetch (unidentified_name, name, description) for an item from STR# resources."""
    if rsrc_fork is None:
        return ("", "", "")
    unid_lst = rsrc_fork.get_str_list(group_base) if rsrc_fork.has("STR#", group_base) else []
    name_lst = rsrc_fork.get_str_list(group_base + 1) if rsrc_fork.has("STR#", group_base + 1) else []
    desc_lst = rsrc_fork.get_str_list(group_base + 2) if rsrc_fork.has("STR#", group_base + 2) else []
    unid_name = sanitize_text(unid_lst[offset_in_group]).strip() if offset_in_group < len(unid_lst) else ""
    name = sanitize_text(name_lst[offset_in_group]).strip() if offset_in_group < len(name_lst) else ""
    desc = sanitize_text(desc_lst[offset_in_group]).strip() if offset_in_group < len(desc_lst) else ""
    return (unid_name, name, desc)


def parse_items(
    data_id_path: Path,
    data_ni_path: Optional[Path],
    id_rsrc: Any,
    scen_rsrc: Optional[Any] = None,
) -> Dict[int, Dict[str, Any]]:
    """Parse 100-byte struct itemattr tables from Data ID (0..799) and Data NI (800..999)."""
    raw_id = Path(data_id_path).read_bytes()
    raw_ni = Path(data_ni_path).read_bytes() if data_ni_path and Path(data_ni_path).is_file() else b""
    items: Dict[int, Dict[str, Any]] = {}

    def decode_record(item_id: int, rec: bytes, rsrc_fork: Any, group_base: int, local_idx: int) -> None:
        if len(rec) < ITEMATTR_BYTES:
            return
        shorts_a = struct.unpack(">18h", rec[0:36])
        shorts_b = struct.unpack(">15h", rec[70:100])
        unid_name, name, desc = _lookup_item_text(rsrc_fork, group_base, local_idx)
        if not name:
            return
        itype = abs(shorts_a[3])
        items[item_id] = {
            "id": item_id,
            "name": name,
            "unidentified_name": unid_name or name,
            "description": desc,
            "raw_type": itype,
            "blunt": shorts_a[4],
            "hands": shorts_a[5],
            "movement_mod": shorts_a[7],
            "ac_bonus": shorts_a[8],
            "magic_res": shorts_a[9],
            "weapon_bonus": shorts_a[10],
            "sp_bonus": shorts_a[11],
            "weight": shorts_a[13],
            "base_price": abs(shorts_a[14]),
            "charges": shorts_a[15],
            "is_magical": bool(shorts_a[17]),
            "vssmall": shorts_b[0],
            "vslarge": shorts_b[1],
            "heat": shorts_b[2],
            "cold": shorts_b[3],
            "electric": shorts_b[4],
            "vsundead": shorts_b[5],
            "vsevil": shorts_b[7],
            "sp1": shorts_b[8],
            "sp2": shorts_b[9],
        }

    for iid in range(min(800, len(raw_id) // ITEMATTR_BYTES)):
        group_base = (iid // 200) * 200
        decode_record(iid, raw_id[iid * ITEMATTR_BYTES : (iid + 1) * ITEMATTR_BYTES], id_rsrc, group_base, iid % 200)

    for local_idx in range(min(200, len(raw_ni) // ITEMATTR_BYTES)):
        iid = 800 + local_idx
        decode_record(iid, raw_ni[local_idx * ITEMATTR_BYTES : (local_idx + 1) * ITEMATTR_BYTES], scen_rsrc, 800, local_idx)

    return items


def parse_races_and_castes(
    data_race_path: Path,
    data_caste_path: Path,
    fj_rsrc: Any,
) -> Tuple[Dict[int, Dict[str, Any]], Dict[int, Dict[str, Any]]]:
    """Parse Data Race (960B) and Data Caste (576B) records indexed by 1-based ID."""
    raw_race = Path(data_race_path).read_bytes()
    raw_caste = Path(data_caste_path).read_bytes()
    race_names = [sanitize_text(s).strip() for s in fj_rsrc.get_str_list(129)]
    caste_names = [sanitize_text(s).strip() for s in fj_rsrc.get_str_list(131)]

    races: Dict[int, Dict[str, Any]] = {}
    for idx in range(min(len(race_names), len(raw_race) // RACE_BYTES)):
        name = race_names[idx]
        if not name:
            continue
        rec = raw_race[idx * RACE_BYTES : (idx + 1) * RACE_BYTES]
        att_bonus = struct.unpack(">6h", rec[60:72])
        basemove = struct.unpack(">h", rec[196:198])[0]
        num_attacks = struct.unpack(">2h", rec[204:208])
        races[idx + 1] = {
            "id": idx + 1,
            "name": name,
            "att_bonus": list(att_bonus),
            "base_move": basemove,
            "num_attacks": num_attacks[0],
        }

    castes: Dict[int, Dict[str, Any]] = {}
    for idx in range(min(len(caste_names), len(raw_caste) // CASTE_BYTES)):
        name = caste_names[idx]
        if not name:
            continue
        rec = raw_caste[idx * CASTE_BYTES : (idx + 1) * CASTE_BYTES]
        att_bonus = struct.unpack(">6h", rec[72:84])
        stamina = struct.unpack(">2h", rec[216:220])
        dodge = struct.unpack(">2h", rec[224:228])
        tohit = struct.unpack(">2h", rec[228:232])
        missile = struct.unpack(">2h", rec[232:236])
        move_bonus = struct.unpack(">h", rec[252:254])[0]
        bonus_attacks = struct.unpack(">h", rec[260:262])[0]
        start_money = struct.unpack(">h", rec[384:386])[0]
        start_items = [x for x in struct.unpack(">20h", rec[386:426]) if x != 0]
        castes[idx + 1] = {
            "id": idx + 1,
            "name": name,
            "att_bonus": list(att_bonus),
            "stamina_base": stamina[0],
            "dodge_base": dodge[0],
            "tohit_base": tohit[0],
            "missile_base": missile[0],
            "move_bonus": move_bonus,
            "bonus_attacks": bonus_attacks,
            "start_money": start_money,
            "start_items": start_items,
        }

    return races, castes


def parse_shops(data_sd_path: Path) -> List[Dict[str, Any]]:
    """Parse all 3002-byte struct shop records from Data SD."""
    raw = Path(data_sd_path).read_bytes()
    count = len(raw) // SHOP_BYTES
    shops: List[Dict[str, Any]] = []
    for idx in range(count):
        rec = raw[idx * SHOP_BYTES : (idx + 1) * SHOP_BYTES]
        ids = struct.unpack(">1000h", rec[:2000])
        nums = rec[2000:3000]
        inflation = struct.unpack(">h", rec[3000:3002])[0]
        stock = [{"item_id": iid, "quantity": cnt} for iid, cnt in zip(ids, nums) if iid != 0]
        shops.append({"shop_id": idx, "inflation_pct": inflation, "stock": stock})
    return shops


def parse_battles(data_bd_path: Path) -> List[Dict[str, Any]]:
    """Parse 346-byte struct battle records from Data BD."""
    raw = Path(data_bd_path).read_bytes()
    count = len(raw) // BATTLE_BYTES
    battles: List[Dict[str, Any]] = []
    for idx in range(count):
        rec = raw[idx * BATTLE_BYTES : (idx + 1) * BATTLE_BYTES]
        grid = struct.unpack(">169h", rec[:338])
        dist = rec[338]
        msg_before, msg_after, macro = struct.unpack(">3h", rec[340:346])
        monster_indices = [abs(x) for x in grid if x != 0]
        battles.append(
            {
                "battle_id": idx,
                "dist": dist,
                "message_before": msg_before,
                "message_after": msg_after,
                "macro": macro,
                "monster_indices": monster_indices,
            }
        )
    return battles


def parse_edcd(data_edcd_path: Path) -> List[Tuple[int, ...]]:
    """Parse 10-byte (5 short) extracode records from Data EDCD."""
    raw = Path(data_edcd_path).read_bytes()
    count = len(raw) // EDCD_BYTES
    return [struct.unpack(">5h", raw[i * EDCD_BYTES : (i + 1) * EDCD_BYTES]) for i in range(count)]


def parse_sd2_strings(data_sd2_path: Path) -> List[str]:
    """Parse 256-byte Str255 encounter strings from Data SD2."""
    raw = Path(data_sd2_path).read_bytes()
    count = len(raw) // SD2_BYTES
    return [decode_pstr(raw[i * SD2_BYTES : (i + 1) * SD2_BYTES]) for i in range(count)]
