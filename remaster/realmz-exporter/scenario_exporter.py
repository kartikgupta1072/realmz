"""Scenario-agnostic multi-scenario binary extractor for Classic Realmz (P1.5)."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from typing import Any, Dict, List, Optional, Tuple

try:
    import yaml
except ImportError:
    yaml = None

SCRIPT_DIR = str(Path(__file__).resolve().parent)
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from formats import (
    DOOR_BYTES,
    DOOR_COUNT_PER_LEVEL,
    LAND_BYTES,
    RANDLEVEL_BYTES,
    parse_battles_tactical,
    parse_doors,
    parse_dungeon_grid,
    parse_edcd,
    parse_items,
    parse_land_grid,
    parse_maps_metadata,
    parse_mapstats,
    parse_monsters,
    parse_rand_level,
    parse_scenario_header,
    parse_sd2_strings,
    parse_shops,
    parse_solids,
    parse_treasures,
    sanitize_text,
)
from rsrc import ResourceFork


AP_OPCODE_NAMES: Dict[int, str] = {
    1: "string",
    2: "battle",
    3: "option",
    4: "simple_encounter",
    5: "complex_encounter",
    6: "shop",
    7: "modify_ap",
    8: "treasure",
    9: "sound",
    10: "picture",
    11: "give_item",
    12: "take_item",
    13: "change_tile",
    14: "camp",
    15: "temple",
    19: "spell",
    23: "check_flag",
    24: "end_ap",
    25: "set_flag",
    27: "music",
    37: "teleport",
    38: "set_return_or_state",
    39: "macro_jump",
    45: "condition_check",
    46: "party_effect",
    49: "text_prompt",
    56: "quest_event",
}

FACING_MAP: Dict[int, str] = {
    0: "North",
    1: "East",
    2: "South",
    3: "West",
}

# Mapping from Data RD landlook byte -> canonical tileset * BD filename in Data Files (or scenario custom BD).
LANDLOOK_TO_BD: Dict[int, str] = {
    0: "Data P BD",
    3: "Data Castle BD",
    4: "Data SUB BD",
    5: "Data Desert BD",
    6: "Data Snow BD",
    9: "Data Swamp BD",
}


def slugify_scenario(name: str) -> str:
    """Convert scenario display name to a filesystem-safe lowercase slug."""
    cleaned = re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")
    return cleaned or "scenario"


def _sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _write_json(path: Path, payload: Any) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = sanitize_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n")
    path.write_text(text, encoding="utf-8")
    return path


def load_overrides(overrides_path: Optional[Path]) -> Dict[str, Any]:
    """Load optional remaster art/name enrichment YAML or JSON overrides."""
    if overrides_path is None:
        return {}
    p = Path(overrides_path)
    if not p.is_file():
        raise FileNotFoundError(f"Overrides file not found: {p}")
    raw_text = p.read_text(encoding="utf-8")
    if p.suffix.lower() in (".yaml", ".yml"):
        if yaml is None:
            raise RuntimeError("PyYAML is required to load YAML overrides")
        loaded = yaml.safe_load(raw_text)
    else:
        loaded = json.loads(raw_text)
    return loaded if isinstance(loaded, dict) else {}


def resolve_tileset_bd(data_files: Path, scen_dir: Path, landlook: int) -> Tuple[Path, str]:
    """Resolve the * BD tileset metadata file for a given Data RD landlook index."""
    # Check scenario-local custom tilesets first for landlook >= 10 or custom indices.
    if landlook >= 10:
        custom_idx = landlook - 9
        custom_path = scen_dir / f"Data Custom {custom_idx} BD"
        if custom_path.is_file():
            return (custom_path, custom_path.name)

    bd_name = LANDLOOK_TO_BD.get(landlook, "Data P BD")
    cand = data_files / bd_name
    if cand.is_file():
        return (cand, bd_name)

    fallback = data_files / "Data P BD"
    return (fallback, fallback.name)


def _safe_string(strings: List[str], sid: int) -> str:
    idx = abs(sid)
    if 0 <= idx < len(strings):
        return strings[idx].strip()
    return ""


def _resolve_encounter_zones(
    rd: Dict[str, Any],
    battles_by_id: Dict[int, Dict[str, Any]],
) -> List[Dict[str, Any]]:
    zones: List[Dict[str, Any]] = []
    for z in rd.get("encounter_zones", []):
        b_min, b_max = z["battle_range"]
        if b_max < b_min:
            b_min, b_max = b_max, b_min
        valid_bids: List[int] = []
        mon_ids: List[int] = []
        seen = set()
        for bid in range(b_min, b_max + 1):
            b = battles_by_id.get(bid)
            if b is None or not b["is_valid"]:
                continue
            valid_bids.append(bid)
            for mid in b["monster_indices"]:
                if mid not in seen:
                    seen.add(mid)
                    mon_ids.append(mid)
        rect = z["rect"]
        zones.append(
            {
                "slot": z["slot"],
                "top": rect["top"],
                "left": rect["left"],
                "bottom": rect["bottom"],
                "right": rect["right"],
                "percent": z["percent"],
                "battle_ids": valid_bids,
                "monster_indices": mon_ids,
            }
        )
    return zones


def _decode_level_doors(
    doors: List[Dict[str, Any]],
    source_map_id: str,
    source_is_dungeon: bool,
    source_level: int,
    land_level_count: int,
    dungeon_level_count: int,
    edcd: List[Tuple[int, ...]],
    strings: List[str],
    battles_by_id: Dict[int, Dict[str, Any]],
    shop_count: int,
    treasure_count: int,
) -> Dict[str, Any]:
    """Decode AP door macros on a single land or dungeon level into portals, shops, battles, treasures, sounds."""
    portals: List[Dict[str, Any]] = []
    shop_triggers: List[Dict[str, Any]] = []
    battle_triggers: List[Dict[str, Any]] = []
    treasure_triggers: List[Dict[str, Any]] = []
    sound_triggers: List[Dict[str, Any]] = []
    ap_doors: List[Dict[str, Any]] = []

    for dr in doors:
        # Filter to doors belonging to this level when land_id is populated for land maps.
        if not source_is_dungeon and dr["land_id"] != source_level:
            continue
        x, y = dr["x"], dr["y"]
        if not (0 <= x < 90 and 0 <= y < 90):
            continue

        ops: List[Dict[str, Any]] = []
        lead_message = ""
        for code, cid in zip(dr["codes"], dr["ids"]):
            if code == 0 and cid == 0:
                continue
            op_name = AP_OPCODE_NAMES.get(code, f"opcode_{code}")
            op_entry: Dict[str, Any] = {"code": code, "op_name": op_name, "id": cid}

            if code == 1:
                text = _safe_string(strings, cid)
                if text:
                    op_entry["text"] = text
                    if not lead_message:
                        lead_message = text
            elif code == 2:
                b = battles_by_id.get(cid)
                if b and b["is_valid"]:
                    battle_triggers.append(
                        {
                            "x": x,
                            "y": y,
                            "battle_id": cid,
                            "percent": dr["percent"],
                            "monster_indices": b["monster_indices"],
                            "message": lead_message,
                        }
                    )
            elif code == 6:
                if 0 <= cid < shop_count:
                    shop_triggers.append(
                        {
                            "x": x,
                            "y": y,
                            "shop_id": cid,
                            "message": lead_message,
                        }
                    )
            elif code == 8:
                if 0 <= cid < treasure_count:
                    treasure_triggers.append(
                        {
                            "x": x,
                            "y": y,
                            "treasure_id": cid,
                            "percent": dr["percent"],
                            "message": lead_message,
                        }
                    )
            elif code == 9:
                if cid > 0:
                    sound_triggers.append({"x": x, "y": y, "sound_id": cid})
            elif code == 37:
                if 0 <= cid < len(edcd):
                    dest_is_land, dest_lvl, dest_x, dest_y, facing_code = edcd[cid]
                    dest_is_dungeon = dest_is_land == 0
                    valid_dest_lvl = (
                        (0 <= dest_lvl < dungeon_level_count)
                        if dest_is_dungeon
                        else (0 <= dest_lvl < land_level_count)
                    )
                    if dest_is_land in (0, 1) and valid_dest_lvl and 0 <= dest_x < 90 and 0 <= dest_y < 90:
                        dest_map_id = f"dungeon_{dest_lvl}" if dest_is_dungeon else f"land_{dest_lvl}"
                        portal = {
                            "id": f"{source_map_id}_portal_{x}_{y}_edcd_{cid}",
                            "source_map_id": source_map_id,
                            "x": x,
                            "y": y,
                            "edcd_id": cid,
                            "dest_is_dungeon": dest_is_dungeon,
                            "dest_level": int(dest_lvl),
                            "dest_map_id": dest_map_id,
                            "dest_x": int(dest_x),
                            "dest_y": int(dest_y),
                            "dest_facing": FACING_MAP.get(facing_code, "South"),
                            "label": lead_message[:80].strip() if lead_message else f"Portal to {dest_map_id} ({dest_x},{dest_y})",
                        }
                        portals.append(portal)
                        op_entry["portal"] = {
                            "dest_map_id": dest_map_id,
                            "dest_x": int(dest_x),
                            "dest_y": int(dest_y),
                        }

            ops.append(op_entry)

        if ops:
            ap_doors.append(
                {
                    "slot": dr["slot"],
                    "door_id": dr["door_id"],
                    "x": x,
                    "y": y,
                    "percent": dr["percent"],
                    "ops": ops,
                }
            )

    return {
        "portals": portals,
        "shop_triggers": shop_triggers,
        "battle_triggers": battle_triggers,
        "treasure_triggers": treasure_triggers,
        "sound_triggers": sound_triggers,
        "ap_doors": ap_doors,
    }


def export_scenario_package(
    realmz_base: Path,
    scenario_name: str,
    out_dir: Path,
    overrides_path: Optional[Path] = None,
) -> Dict[str, Any]:
    """Export any Realmz scenario into a scenario-agnostic ScenarioPackageJson directory."""
    base = Path(realmz_base)
    data_files = base / "Data Files"
    scen_dir = base / "Scenarios" / scenario_name
    if not scen_dir.is_dir():
        raise FileNotFoundError(f"Scenario directory not found: {scen_dir}")

    overrides = load_overrides(overrides_path)
    slug = slugify_scenario(scenario_name)
    pkg_dir = Path(out_dir) / slug
    land_dir = pkg_dir / "land_levels"
    dungeon_dir = pkg_dir / "dungeon_levels"
    pkg_dir.mkdir(parents=True, exist_ok=True)
    land_dir.mkdir(parents=True, exist_ok=True)
    dungeon_dir.mkdir(parents=True, exist_ok=True)

    # Resource forks (optional scenario .rsrc for map names & custom items).
    id_rsrc = ResourceFork(data_files / "Data ID.rsrc")
    scen_rsrc_path = scen_dir / "Scenario.rsrc"
    scen_rsrc = ResourceFork(scen_rsrc_path) if scen_rsrc_path.is_file() else None

    # 1. Core scenario binary tables.
    scen_header_path = scen_dir / scenario_name
    scen_header = parse_scenario_header(scen_header_path) if scen_header_path.is_file() else {}
    maps_md2 = parse_maps_metadata(scen_dir / "Data MD2") if (scen_dir / "Data MD2").is_file() else []
    map_names: List[str] = []
    if scen_rsrc and scen_rsrc.has("STR#", -102):
        map_names = [sanitize_text(s).strip() for s in scen_rsrc.get_str_list(-102)]

    solids = parse_solids(scen_dir / "Data Solids") if (scen_dir / "Data Solids").is_file() else []
    edcd = parse_edcd(scen_dir / "Data EDCD") if (scen_dir / "Data EDCD").is_file() else []
    strings = parse_sd2_strings(scen_dir / "Data SD2") if (scen_dir / "Data SD2").is_file() else []
    raw_monsters = parse_monsters(scen_dir / "Data MD") if (scen_dir / "Data MD").is_file() else []
    valid_monsters = [m for m in raw_monsters if m["is_valid"]]
    max_monster_idx = max(len(raw_monsters), 200)

    battles = (
        parse_battles_tactical(scen_dir / "Data BD", max_monster_index=max_monster_idx)
        if (scen_dir / "Data BD").is_file()
        else []
    )
    for b in battles:
        b["message_before_text"] = _safe_string(strings, b["message_before"]) if b["message_before"] > 0 else ""
        b["message_after_text"] = _safe_string(strings, b["message_after"]) if b["message_after"] > 0 else ""
    battles_by_id = {b["battle_id"]: b for b in battles}
    valid_battles = [b for b in battles if b["is_valid"]]

    raw_items = parse_items(
        data_files / "Data ID",
        scen_dir / "Data NI" if (scen_dir / "Data NI").is_file() else None,
        id_rsrc,
        scen_rsrc,
    )
    custom_items = [raw_items[iid] for iid in sorted(raw_items.keys()) if iid >= 800]

    raw_shops = parse_shops(scen_dir / "Data SD") if (scen_dir / "Data SD").is_file() else []
    shop_name_overrides = overrides.get("shop_names", {}) if isinstance(overrides.get("shop_names"), dict) else {}
    shops_out: List[Dict[str, Any]] = []
    for sh in raw_shops:
        sid = sh["shop_id"]
        infl = sh["inflation_pct"]
        stock_out = []
        for entry in sh["stock"]:
            iid = entry["item_id"]
            item_meta = raw_items.get(iid)
            base_price = max(1, item_meta["base_price"]) if item_meta else 10
            price = max(1, (base_price * (100 + infl)) // 100)
            stock_out.append(
                {
                    "item_id": iid,
                    "name": item_meta["name"] if item_meta else f"Item {iid}",
                    "quantity": entry["quantity"],
                    "price": price,
                }
            )
        shops_out.append(
            {
                "shop_id": sid,
                "name": sanitize_text(str(shop_name_overrides.get(str(sid), shop_name_overrides.get(sid, f"Shop {sid}")))),
                "inflation_pct": infl,
                "stock_count": len(stock_out),
                "stock": stock_out,
            }
        )

    treasures = parse_treasures(scen_dir / "Data TD") if (scen_dir / "Data TD").is_file() else []
    for tr in treasures:
        tr["item_names"] = [raw_items[iid]["name"] if iid in raw_items else f"Item {iid}" for iid in tr["item_ids"]]

    # 2. Level counts for Data LD / Data DL.
    ld_path = scen_dir / "Data LD"
    rd_path = scen_dir / "Data RD"
    dd_path = scen_dir / "Data DD"
    dl_path = scen_dir / "Data DL"
    rdd_path = scen_dir / "Data RDD"
    ddd_path = scen_dir / "Data DDD"

    land_level_count = ld_path.stat().st_size // LAND_BYTES if ld_path.is_file() else 0
    rd_level_count = rd_path.stat().st_size // RANDLEVEL_BYTES if rd_path.is_file() else 0
    dd_level_count = dd_path.stat().st_size // (DOOR_COUNT_PER_LEVEL * DOOR_BYTES) if dd_path.is_file() else 0

    dungeon_level_count = dl_path.stat().st_size // LAND_BYTES if dl_path.is_file() else 0
    rdd_level_count = rdd_path.stat().st_size // RANDLEVEL_BYTES if rdd_path.is_file() else 0
    ddd_level_count = ddd_path.stat().st_size // (DOOR_COUNT_PER_LEVEL * DOOR_BYTES) if ddd_path.is_file() else 0

    # Cache parsed * BD tileset rule tables.
    mapstats_cache: Dict[str, List[Dict[str, Any]]] = {}

    def get_tile_rules_for_landlook(landlook: int) -> Tuple[str, List[Dict[str, Any]]]:
        bd_path, bd_name = resolve_tileset_bd(data_files, scen_dir, landlook)
        if bd_name not in mapstats_cache:
            parsed = parse_mapstats(bd_path)
            mapstats_cache[bd_name] = [parsed[tid] for tid in sorted(parsed.keys())]
        return (bd_name, mapstats_cache[bd_name])

    all_portals: List[Dict[str, Any]] = []
    land_index: List[Dict[str, Any]] = []
    dungeon_index: List[Dict[str, Any]] = []
    total_ap_triggers = 0

    # 3. Export all land levels.
    for lv in range(land_level_count):
        land = parse_land_grid(ld_path, level=lv)
        rd = parse_rand_level(rd_path, level=lv) if lv < rd_level_count else {
            "landlook": 0,
            "tileset_pict_id": 300,
            "isdark": False,
            "uselos": False,
            "encounter_zones": [],
        }
        doors = parse_doors(dd_path, level=lv) if lv < dd_level_count else []
        bd_name, tile_rules = get_tile_rules_for_landlook(rd["landlook"])
        map_id = f"land_{lv}"

        decoded_ap = _decode_level_doors(
            doors=doors,
            source_map_id=map_id,
            source_is_dungeon=False,
            source_level=lv,
            land_level_count=land_level_count,
            dungeon_level_count=dungeon_level_count,
            edcd=edcd,
            strings=strings,
            battles_by_id=battles_by_id,
            shop_count=len(shops_out),
            treasure_count=len(treasures),
        )
        all_portals.extend(decoded_ap["portals"])
        total_ap_triggers += len(decoded_ap["ap_doors"])

        is_start_level = lv == scen_header.get("start_land_level", 0)
        start_spawn = (
            {
                "x": int(scen_header.get("start_look_x", 0)),
                "y": int(scen_header.get("start_look_y", 0)),
                "facing": "South",
            }
            if is_start_level
            else None
        )
        zones = _resolve_encounter_zones(rd, battles_by_id)
        level_name = map_names[lv] if lv < len(map_names) and map_names[lv] else f"Land Level {lv}"

        land_payload = {
            "map_id": map_id,
            "scenario": sanitize_text(scenario_name),
            "name": level_name,
            "level": lv,
            "is_dungeon": False,
            "width": land["width"],
            "height": land["height"],
            "layout": "row_major_yx",
            "landlook": rd["landlook"],
            "tileset_pict_id": rd["tileset_pict_id"],
            "tileset_bd_file": bd_name,
            "is_dark": rd["isdark"],
            "use_los": rd["uselos"],
            "start_spawn": start_spawn,
            "stats": land["stats"],
            "tiles_yx": land["tiles_yx"],
            "tile_rules": tile_rules,
            "solid_negative_ids": solids,
            "portals": decoded_ap["portals"],
            "shop_triggers": decoded_ap["shop_triggers"],
            "battle_triggers": decoded_ap["battle_triggers"],
            "treasure_triggers": decoded_ap["treasure_triggers"],
            "sound_triggers": decoded_ap["sound_triggers"],
            "ap_doors": decoded_ap["ap_doors"],
            "encounter_zones": zones,
        }
        rel_file = f"land_levels/{map_id}.json"
        _write_json(pkg_dir / rel_file, land_payload)
        land_index.append(
            {
                "level": lv,
                "map_id": map_id,
                "name": level_name,
                "file": rel_file,
                "landlook": rd["landlook"],
                "tileset_pict_id": rd["tileset_pict_id"],
                "tileset_bd_file": bd_name,
                "is_dark": rd["isdark"],
                "use_los": rd["uselos"],
                "portal_count": len(decoded_ap["portals"]),
                "shop_trigger_count": len(decoded_ap["shop_triggers"]),
                "battle_trigger_count": len(decoded_ap["battle_triggers"]),
                "treasure_trigger_count": len(decoded_ap["treasure_triggers"]),
                "encounter_zone_count": len(zones),
                "ap_door_count": len(decoded_ap["ap_doors"]),
            }
        )

    # 4. Export all dungeon levels.
    for lv in range(dungeon_level_count):
        dung = parse_dungeon_grid(dl_path, level=lv)
        rdd = parse_rand_level(rdd_path, level=lv) if lv < rdd_level_count else {
            "landlook": 0,
            "tileset_pict_id": 300,
            "isdark": True,
            "uselos": True,
            "encounter_zones": [],
        }
        ddd = parse_doors(ddd_path, level=lv) if lv < ddd_level_count else []
        map_id = f"dungeon_{lv}"

        decoded_ap = _decode_level_doors(
            doors=ddd,
            source_map_id=map_id,
            source_is_dungeon=True,
            source_level=lv,
            land_level_count=land_level_count,
            dungeon_level_count=dungeon_level_count,
            edcd=edcd,
            strings=strings,
            battles_by_id=battles_by_id,
            shop_count=len(shops_out),
            treasure_count=len(treasures),
        )
        all_portals.extend(decoded_ap["portals"])
        total_ap_triggers += len(decoded_ap["ap_doors"])
        zones = _resolve_encounter_zones(rdd, battles_by_id)

        dung_payload = {
            "map_id": map_id,
            "scenario": sanitize_text(scenario_name),
            "name": f"Dungeon Level {lv}",
            "level": lv,
            "is_dungeon": True,
            "width": dung["width"],
            "height": dung["height"],
            "layout": "row_major_yx",
            "tileset_pict_id": rdd["tileset_pict_id"],
            "is_dark": True,
            "use_los": True,
            "tiles_yx": dung["tiles_yx"],
            "tile_rules": dung["tile_rules"],
            "solid_negative_ids": [],
            "portals": decoded_ap["portals"],
            "shop_triggers": decoded_ap["shop_triggers"],
            "battle_triggers": decoded_ap["battle_triggers"],
            "treasure_triggers": decoded_ap["treasure_triggers"],
            "sound_triggers": decoded_ap["sound_triggers"],
            "ap_doors": decoded_ap["ap_doors"],
            "encounter_zones": zones,
        }
        rel_file = f"dungeon_levels/{map_id}.json"
        _write_json(pkg_dir / rel_file, dung_payload)
        dungeon_index.append(
            {
                "level": lv,
                "map_id": map_id,
                "file": rel_file,
                "tileset_pict_id": rdd["tileset_pict_id"],
                "is_dark": True,
                "use_los": True,
                "portal_count": len(decoded_ap["portals"]),
                "battle_trigger_count": len(decoded_ap["battle_triggers"]),
                "treasure_trigger_count": len(decoded_ap["treasure_triggers"]),
                "encounter_zone_count": len(zones),
                "ap_door_count": len(decoded_ap["ap_doors"]),
            }
        )

    # 5. Write scenario entity tables.
    _write_json(
        pkg_dir / "battles.json",
        {
            "scenario": sanitize_text(scenario_name),
            "total_slots": len(battles),
            "valid_count": len(valid_battles),
            "battles": battles,
        },
    )
    _write_json(
        pkg_dir / "monsters.json",
        {
            "scenario": sanitize_text(scenario_name),
            "total_slots": len(raw_monsters),
            "valid_count": len(valid_monsters),
            "monsters": valid_monsters,
        },
    )
    _write_json(
        pkg_dir / "shops.json",
        {
            "scenario": sanitize_text(scenario_name),
            "count": len(shops_out),
            "shops": shops_out,
        },
    )
    _write_json(
        pkg_dir / "treasures.json",
        {
            "scenario": sanitize_text(scenario_name),
            "count": len(treasures),
            "treasures": treasures,
        },
    )
    non_empty_strings = [{"id": i, "text": s} for i, s in enumerate(strings) if s.strip()]
    _write_json(
        pkg_dir / "strings.json",
        {
            "scenario": sanitize_text(scenario_name),
            "total_slots": len(strings),
            "non_empty_count": len(non_empty_strings),
            "strings": non_empty_strings,
        },
    )
    _write_json(
        pkg_dir / "portals.json",
        {
            "scenario": sanitize_text(scenario_name),
            "count": len(all_portals),
            "portals": all_portals,
        },
    )
    _write_json(
        pkg_dir / "custom_items.json",
        {
            "scenario": sanitize_text(scenario_name),
            "count": len(custom_items),
            "items": custom_items,
        },
    )

    # 6. Write root ScenarioPackageJson manifest.
    package_json = {
        "schema": "ScenarioPackageJson",
        "schema_version": "1.0.0",
        "exporter": "realmz-exporter.scenario_exporter",
        "scenario": sanitize_text(overrides.get("display_name", scenario_name)),
        "source_scenario_name": sanitize_text(scenario_name),
        "slug": slug,
        "header": scen_header,
        "map_names": map_names,
        "maps_md2": maps_md2,
        "counts": {
            "land_levels": land_level_count,
            "dungeon_levels": dungeon_level_count,
            "monsters_valid": len(valid_monsters),
            "battles_total": len(battles),
            "battles_valid": len(valid_battles),
            "shops": len(shops_out),
            "treasures": len(treasures),
            "strings_total": len(strings),
            "strings_non_empty": len(non_empty_strings),
            "portals": len(all_portals),
            "ap_doors": total_ap_triggers,
            "custom_items": len(custom_items),
            "edcd_records": len(edcd),
        },
        "land_levels": land_index,
        "dungeon_levels": dungeon_index,
        "files": {
            "battles": "battles.json",
            "monsters": "monsters.json",
            "shops": "shops.json",
            "treasures": "treasures.json",
            "strings": "strings.json",
            "portals": "portals.json",
            "custom_items": "custom_items.json",
        },
        "overrides": {
            "source": str(overrides_path) if overrides_path else "",
            "art_bindings": overrides.get("art_bindings", {}),
            "music_bindings": overrides.get("music_bindings", {}),
        },
    }
    pkg_path = _write_json(pkg_dir / "scenario_package.json", package_json)
    package_json["package_sha256"] = _sha256_file(pkg_path)
    return package_json


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Scenario-agnostic binary extractor exporting Realmz scenarios to ScenarioPackageJson."
    )
    parser.add_argument(
        "--realmz-base",
        type=Path,
        default=Path(r"C:\realmz\base\Realmz"),
        help="Path to base Realmz directory.",
    )
    parser.add_argument(
        "--scenarios",
        nargs="+",
        default=["City of Bywater", "Prelude to Pestilence", "Assault on Giant Mountain"],
        help="One or more scenario directory names under Scenarios/.",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path(r"C:\realmz\remaster\extracted\scenarios"),
        help="Root output directory for extracted scenario packages.",
    )
    parser.add_argument(
        "--overrides",
        type=Path,
        default=None,
        help="Optional YAML/JSON overrides file (art/name enrichment only, applied when exporting 1 scenario).",
    )
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)
    try:
        for sname in args.scenarios:
            ov = args.overrides if (args.overrides and len(args.scenarios) == 1) else None
            pkg = export_scenario_package(args.realmz_base, sname, args.out, overrides_path=ov)
            c = pkg["counts"]
            print(
                f"Exported '{pkg['scenario']}' -> {args.out / pkg['slug']}: "
                f"land={c['land_levels']} dung={c['dungeon_levels']} portals={c['portals']} "
                f"battles={c['battles_valid']}/{c['battles_total']} shops={c['shops']} "
                f"treasures={c['treasures']} strings={c['strings_non_empty']}"
            )
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
