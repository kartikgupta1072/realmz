"""Milestone 1 City of Bywater structured vertical-slice builder."""

from __future__ import annotations

from pathlib import Path
import re
from typing import Any, Dict, List, Optional, Tuple

from formats import (
    parse_battles,
    parse_doors,
    parse_dungeon_grid,
    parse_edcd,
    parse_items,
    parse_land_grid,
    parse_mapstats,
    parse_monsters,
    parse_races_and_castes,
    parse_rand_level,
    parse_solids,
    parse_spellarea,
    parse_spells,
    parse_shops,
    sanitize_text,
)
from rsrc import ResourceFork


STARTER_MONSTER_INDICES = [0, 133, 125, 92, 130, 129, 116, 76, 70, 78, 91, 126]

STARTER_PARTY_SPECS = [
    {
        "id": "hero_knight",
        "name": "Aldric",
        "archetype": "Knight",
        "race_id": 1,
        "caste_id": 1,
        "icon_id": 9018,
        "portrait_id": 275,
        "base_attrs": [16, 10, 10, 13, 15, 11],
        "equipped_ids": [20, 209, 418, 400],
        "known_spell_ids": [],
    },
    {
        "id": "hero_cleric",
        "name": "Liora",
        "archetype": "Cleric",
        "race_id": 7,
        "caste_id": 7,
        "icon_id": 9072,
        "portrait_id": 329,
        "base_attrs": [14, 12, 17, 11, 14, 12],
        "equipped_ids": [37, 215, 418, 400],
        "known_spell_ids": [2105, 2207, 2106, 2103, 2310],
    },
    {
        "id": "hero_ranger",
        "name": "Kaelen",
        "archetype": "Ranger",
        "race_id": 8,
        "caste_id": 4,
        "icon_id": 9047,
        "portrait_id": 304,
        "base_attrs": [14, 12, 12, 16, 13, 12],
        "equipped_ids": [75, 104, 209],
        "known_spell_ids": [],
    },
    {
        "id": "hero_mage",
        "name": "Theron",
        "archetype": "Mage",
        "race_id": 3,
        "caste_id": 6,
        "icon_id": 9084,
        "portrait_id": 341,
        "base_attrs": [9, 17, 13, 14, 11, 13],
        "equipped_ids": [1, 200],
        "known_spell_ids": [1108, 1104, 1306, 1102, 1111, 3202],
    },
    {
        "id": "hero_paladin",
        "name": "Seraphina",
        "archetype": "Paladin",
        "race_id": 1,
        "caste_id": 3,
        "icon_id": 9054,
        "portrait_id": 311,
        "base_attrs": [16, 12, 15, 12, 15, 11],
        "equipped_ids": [10, 215, 418, 400],
        "known_spell_ids": [2105, 2106],
    },
    {
        "id": "hero_rogue",
        "name": "Vex",
        "archetype": "Rogue",
        "race_id": 5,
        "caste_id": 5,
        "icon_id": 9106,
        "portrait_id": 363,
        "base_attrs": [12, 13, 10, 17, 12, 15],
        "equipped_ids": [75, 209],
        "known_spell_ids": [],
    },
]

CURATED_SPELL_CONFIG = {
    1108: {
        "key": "magic_missile",
        "alias": "Magic Missile",
        "target_mode": "SingleEnemy",
        "spell_area_index": 0,
        "effect_kind": "Damage",
        "effect_dice": "1d5+2",
        "damage_type": "Magic",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "None",
        "range_override": 12,
    },
    1104: {
        "key": "burning_hands",
        "alias": "Burning Hands",
        "target_mode": "AreaOfEffect",
        "spell_area_index": 2,
        "effect_kind": "Damage",
        "effect_dice": "2d4+1",
        "damage_type": "Heat",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "Heat",
        "range_override": 3,
    },
    1306: {
        "key": "fireball",
        "alias": "Fireball",
        "target_mode": "AreaOfEffect",
        "spell_area_index": 4,
        "effect_kind": "Damage",
        "effect_dice": "3d6+2",
        "damage_type": "Heat",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "Heat",
        "range_override": 12,
    },
    2101: {
        "key": "brimstones",
        "alias": "Brimstones",
        "target_mode": "AreaOfEffect",
        "spell_area_index": 3,
        "effect_kind": "Damage",
        "effect_dice": "1d6+1",
        "damage_type": "Heat",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "Heat",
        "range_override": 10,
    },
    2105: {
        "key": "heal_light_wounds",
        "alias": "Heal Light Wounds",
        "target_mode": "SingleAlly",
        "spell_area_index": 0,
        "effect_kind": "Heal",
        "effect_dice": "1d8+2",
        "damage_type": "Heal",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "None",
        "range_override": 5,
    },
    2207: {
        "key": "heal_medium_wounds",
        "alias": "Heal Medium Wounds",
        "target_mode": "SingleAlly",
        "spell_area_index": 0,
        "effect_kind": "Heal",
        "effect_dice": "2d8+4",
        "damage_type": "Heal",
        "applied_condition": "None",
        "condition_duration": 0,
        "base_recovery_pct": 20,
        "save_kind": "None",
        "range_override": 5,
    },
    2106: {
        "key": "bless",
        "alias": "Bless",
        "target_mode": "AllAllies",
        "spell_area_index": 0,
        "effect_kind": "Buff",
        "effect_dice": "",
        "damage_type": "Magic",
        "applied_condition": "MagicAura",
        "condition_duration": 5,
        "base_recovery_pct": 0,
        "save_kind": "None",
        "range_override": 10,
    },
    3202: {
        "key": "hold",
        "alias": "Hold",
        "target_mode": "SingleEnemy",
        "spell_area_index": 0,
        "effect_kind": "CrowdControl",
        "effect_dice": "",
        "damage_type": "Mental",
        "applied_condition": "Helpless",
        "condition_duration": 3,
        "base_recovery_pct": 25,
        "save_kind": "Mental",
        "range_override": 8,
    },
    2310: {
        "key": "sleep",
        "alias": "Sleep",
        "target_mode": "AreaOfEffect",
        "spell_area_index": 3,
        "effect_kind": "CrowdControl",
        "effect_dice": "",
        "damage_type": "Mental",
        "applied_condition": "Helpless",
        "condition_duration": 3,
        "base_recovery_pct": 25,
        "save_kind": "Mental",
        "range_override": 8,
    },
    1102: {
        "key": "enchanted_blade",
        "alias": "Enchanted Blade",
        "target_mode": "SingleAlly",
        "spell_area_index": 0,
        "effect_kind": "Buff",
        "effect_dice": "",
        "damage_type": "Magic",
        "applied_condition": "AttackBonus",
        "condition_duration": 5,
        "base_recovery_pct": 0,
        "save_kind": "None",
        "range_override": 6,
    },
    1111: {
        "key": "sparkling_armor",
        "alias": "Sparkling Armor",
        "target_mode": "Self",
        "spell_area_index": 0,
        "effect_kind": "Buff",
        "effect_dice": "",
        "damage_type": "Magic",
        "applied_condition": "DefenseBonus",
        "condition_duration": 5,
        "base_recovery_pct": 0,
        "save_kind": "None",
        "range_override": 0,
    },
    2103: {
        "key": "fearful_thoughts",
        "alias": "Fearful Thoughts",
        "target_mode": "SingleEnemy",
        "spell_area_index": 0,
        "effect_kind": "CrowdControl",
        "effect_dice": "",
        "damage_type": "Mental",
        "applied_condition": "RunsAway",
        "condition_duration": 3,
        "base_recovery_pct": 25,
        "save_kind": "Mental",
        "range_override": 8,
    },
}

CORE_ITEM_IDS = [
    1, 2, 10, 11, 20, 31, 37, 75, 98, 104, 108,
    200, 209, 215, 219,
    400, 418,
    602, 611, 612, 622, 723,
    803, 805, 806,
]


def _slugify(text: str, fallback: str) -> str:
    cleaned = re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")
    return cleaned or fallback


def _resolve_zones(
    rd: Dict[str, Any],
    battles: List[Dict[str, Any]],
) -> List[Dict[str, Any]]:
    zones: List[Dict[str, Any]] = []
    for z in rd.get("encounter_zones", []):
        b_min, b_max = z["battle_range"]
        if b_max < b_min:
            b_min, b_max = b_max, b_min
        b_ids = [b for b in range(b_min, b_max + 1) if 0 <= b < len(battles)]
        mon_ids: List[int] = []
        seen = set()
        for bid in b_ids:
            for mid in battles[bid]["monster_indices"]:
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
                "battle_ids": b_ids,
                "monster_indices": mon_ids,
            }
        )
    return zones


def _classify_item(raw_item: Dict[str, Any]) -> Tuple[str, str, str, str]:
    """Return (category, slot, damage_dice, heal_dice) for an item record."""
    iid = raw_item["id"]
    rtype = raw_item["raw_type"]
    vssmall = max(1, raw_item["vssmall"])
    name_low = raw_item["name"].lower()

    if rtype == 15:
        return ("Weapon", "MissileWeapon", f"1d{vssmall}", "")
    if rtype == 2 or iid < 200:
        return ("Weapon", "Weapon", f"1d{vssmall}", "")
    if rtype == 3 or "shield" in name_low:
        return ("Shield", "Shield", "", "")
    if rtype == 4 or (200 <= iid < 400):
        return ("Armor", "Armor", "", "")
    if rtype == 7 or (400 <= iid < 600):
        return ("Helm", "Helm", "", "")
    if "heal small" in name_low:
        return ("Potion", "Consumable", "", "1d8+2")
    if "heal medium" in name_low:
        return ("Potion", "Consumable", "", "2d8+4")
    if "heal" in name_low or "refresh" in name_low or "potion" in name_low:
        return ("Potion", "Consumable", "", "1d6+2")
    if 800 <= iid < 1000:
        return ("Supply", "Utility", "", "")
    return ("Magic", "Consumable", "", "")


def _format_item(raw_item: Dict[str, Any]) -> Dict[str, Any]:
    category, slot, dmg_dice, heal_dice = _classify_item(raw_item)
    iid = raw_item["id"]
    is_junk = (raw_item["base_price"] <= 15) and (category in ("Supply", "Weapon", "Armor")) and (raw_item["weapon_bonus"] == 0) and (raw_item["ac_bonus"] <= 3) and iid not in (1, 803, 805)
    return {
        "id": iid,
        "name": sanitize_text(raw_item["name"]),
        "description": sanitize_text(raw_item["description"]),
        "category": category,
        "slot": slot,
        "damage_dice": dmg_dice,
        "weapon_bonus": raw_item["weapon_bonus"],
        "armor_class_bonus": raw_item["ac_bonus"],
        "movement_mod": raw_item["movement_mod"],
        "range": 8 if slot == "MissileWeapon" else 1,
        "weight": raw_item["weight"],
        "base_price": max(1, raw_item["base_price"]),
        "charges": raw_item["charges"],
        "linked_spell_id": 2105 if "heal small" in raw_item["name"].lower() else (2207 if "heal medium" in raw_item["name"].lower() else None),
        "heal_dice": heal_dice,
        "is_two_handed": raw_item["hands"] >= 2,
        "is_junk": is_junk,
    }


def _monster_dice_from_pair(lo: int, hi: int, hd: int) -> str:
    lo = max(1, lo)
    hi = max(lo, hi)
    if lo == 1:
        return f"1d{hi}"
    if lo == hi:
        return str(lo)
    spread = hi - lo + 1
    if spread >= 2:
        return f"1d{spread}+{lo - 1}"
    return f"1d{max(4, hd * 2)}"


def build_bywater_m1_payloads(realmz_base: Path) -> Dict[str, Any]:
    """Build all 5 structured JSON payloads for City of Bywater Milestone 1."""
    base = Path(realmz_base)
    df = base / "Data Files"
    by = base / "Scenarios" / "City of Bywater"

    fj_rsrc = ResourceFork(df / "The Family Jewels.rsrc")
    id_rsrc = ResourceFork(df / "Data ID.rsrc")
    scen_rsrc = ResourceFork(by / "Scenario.rsrc")

    # 1. Land 0 + Dungeon 0 maps
    land0 = parse_land_grid(by / "Data LD", level=0)
    rd0 = parse_rand_level(by / "Data RD", level=0)
    mapstats = parse_mapstats(df / "Data P BD")
    solids = parse_solids(by / "Data Solids")
    battles = parse_battles(by / "Data BD")
    edcd = parse_edcd(by / "Data EDCD")
    doors0 = parse_doors(by / "Data DD", level=0)

    dungeon0 = parse_dungeon_grid(by / "Data DL", level=0)
    rdd0 = parse_rand_level(by / "Data RDD", level=0)
    ddd0 = parse_doors(by / "Data DDD", level=0)

    # Verify dungeon entrance EDCD[660] = (0, 0, 33, 72, 2) and exit EDCD[661] = (1, 0, 88, 48, 0)
    portal_in = edcd[660] if len(edcd) > 660 else (0, 0, 33, 72, 2)
    portal_out = edcd[661] if len(edcd) > 661 else (1, 0, 88, 48, 0)

    land0_portals = [
        {
            "id": "bywater_dungeon_0_entrance",
            "x": 89,
            "y": 48,
            "dest_map_id": "dungeon_0",
            "dest_x": int(portal_in[2]),
            "dest_y": int(portal_in[3]),
            "dest_facing": "South",
            "label": "Bywater Cavern Entrance (Dungeon 0)",
        }
    ]
    land0_pois = [
        {"id": "bywater_gate", "kind": "gate", "x": 2, "y": 2, "label": "City of Bywater Gate", "shop_id": ""},
        {"id": "bywater_shop_armory", "kind": "shop", "x": 7, "y": 11, "label": "Bywater Armory & General Store", "shop_id": "1"},
        {"id": "bywater_shop_tannery", "kind": "shop", "x": 38, "y": 13, "label": "Bywater Tannery", "shop_id": "4"},
        {"id": "bywater_shop_apothecary", "kind": "shop", "x": 88, "y": 12, "label": "Bywater Apothecary", "shop_id": "10"},
        {"id": "bywater_tavern", "kind": "tavern", "x": 9, "y": 13, "label": "Bywater Fine Tavern", "shop_id": ""},
        {"id": "bywater_temple", "kind": "temple", "x": 9, "y": 17, "label": "Bywater Magistrate & Shrine", "shop_id": ""},
    ]

    land0_json = {
        "map_id": "land_0",
        "scenario": "City of Bywater",
        "level": 0,
        "is_dungeon": False,
        "width": land0["width"],
        "height": land0["height"],
        "layout": "row_major_yx",
        "tileset_pict_id": rd0["tileset_pict_id"],
        "is_dark": rd0["isdark"],
        "use_los": rd0["uselos"],
        "start_spawn": {"x": 2, "y": 1, "facing": "South"},
        "tiles_yx": land0["tiles_yx"],
        "tile_rules": [mapstats[tid] for tid in sorted(mapstats.keys())],
        "solid_negative_ids": solids,
        "portals": land0_portals,
        "points_of_interest": land0_pois,
        "encounter_zones": _resolve_zones(rd0, battles),
        "door_count": len([d for d in doors0 if d["land_id"] == 0]),
    }

    dungeon0_portals = [
        {
            "id": "dungeon_0_exit",
            "x": 32,
            "y": 72,
            "dest_map_id": "land_0",
            "dest_x": int(portal_out[2]),
            "dest_y": int(portal_out[3]),
            "dest_facing": "North",
            "label": "Stairs to Bywater Overworld",
        }
    ]
    dungeon0_json = {
        "map_id": "dungeon_0",
        "scenario": "City of Bywater",
        "level": 0,
        "is_dungeon": True,
        "width": dungeon0["width"],
        "height": dungeon0["height"],
        "layout": "row_major_yx",
        "tileset_pict_id": rdd0["tileset_pict_id"],
        "is_dark": True,
        "use_los": True,
        "start_spawn": {"x": int(portal_in[2]), "y": int(portal_in[3]), "facing": "South"},
        "tiles_yx": dungeon0["tiles_yx"],
        "tile_rules": dungeon0["tile_rules"],
        "solid_negative_ids": [],
        "portals": dungeon0_portals,
        "points_of_interest": [],
        "encounter_zones": _resolve_zones(rdd0, battles),
        "door_count": len(ddd0),
    }

    # 2. Items, Spells, SpellAreas, Shops
    spell_areas = parse_spellarea(df / "Data AD")
    area_by_idx = {a["index"]: a for a in spell_areas}
    raw_spells = {s["id"]: s for s in parse_spells(df / "Data S", fj_rsrc)}

    curated_spells: List[Dict[str, Any]] = []
    for sid, cfg in CURATED_SPELL_CONFIG.items():
        rs = raw_spells.get(sid)
        if rs is None:
            continue
        area_idx = cfg["spell_area_index"]
        offsets = area_by_idx[area_idx]["offsets"] if area_idx in area_by_idx else [{"dx": 0, "dy": 0}]
        curated_spells.append(
            {
                "id": sid,
                "key": cfg["key"],
                "name": rs["name"],
                "alias": cfg["alias"],
                "description": rs["description"],
                "school": rs["school"],
                "level": rs["level"],
                "sp_cost": max(1, rs["sp_cost"]),
                "range": cfg["range_override"],
                "target_mode": cfg["target_mode"],
                "spell_area_index": area_idx,
                "aoe_offsets": offsets,
                "effect_kind": cfg["effect_kind"],
                "effect_dice": cfg["effect_dice"],
                "damage_type": cfg["damage_type"],
                "applied_condition": cfg["applied_condition"],
                "condition_duration": cfg["condition_duration"],
                "base_recovery_pct": cfg["base_recovery_pct"],
                "save_kind": cfg["save_kind"],
            }
        )

    raw_items = parse_items(df / "Data ID", by / "Data NI", id_rsrc, scen_rsrc)
    raw_shops = parse_shops(by / "Data SD")
    selected_shop_ids = [1, 4, 10]
    shop_names = {
        1: "Bywater Armory & General Store",
        4: "Bywater Tannery",
        10: "Bywater Apothecary",
    }

    referenced_item_ids = set(CORE_ITEM_IDS)
    for sp_spec in STARTER_PARTY_SPECS:
        referenced_item_ids.update(sp_spec["equipped_ids"])
    for sid in selected_shop_ids:
        if sid < len(raw_shops):
            for entry in raw_shops[sid]["stock"][:24]:
                referenced_item_ids.add(entry["item_id"])

    formatted_items = [
        _format_item(raw_items[iid])
        for iid in sorted(referenced_item_ids)
        if iid in raw_items
    ]
    item_price_map = {it["id"]: it["base_price"] for it in formatted_items}

    formatted_shops: List[Dict[str, Any]] = []
    for sid in selected_shop_ids:
        if sid >= len(raw_shops):
            continue
        sh = raw_shops[sid]
        infl = sh["inflation_pct"]
        stock = []
        seen_stock = set()
        # Ensure healing potions & torches are stocked in shop 1/10 for playable vertical slice
        candidate_entries = list(sh["stock"][:24])
        if sid == 1:
            for extra_id, extra_qty in [(611, 8), (612, 4), (805, 10), (803, 10)]:
                if not any(e["item_id"] == extra_id for e in candidate_entries):
                    candidate_entries.append({"item_id": extra_id, "quantity": extra_qty})
        for entry in candidate_entries:
            iid = entry["item_id"]
            if iid in item_price_map and iid not in seen_stock:
                seen_stock.add(iid)
                base_p = item_price_map[iid]
                price = max(1, (base_p * (100 + infl)) // 100)
                stock.append({"item_id": iid, "price": price, "quantity": entry["quantity"]})
        formatted_shops.append(
            {
                "shop_id": str(sid),
                "name": shop_names.get(sid, f"Bywater Shop {sid}"),
                "inflation_pct": infl,
                "stock": stock,
            }
        )

    spells_items_json = {
        "scenario": "City of Bywater",
        "spell_areas": spell_areas,
        "spells": curated_spells,
        "items": formatted_items,
        "shops": formatted_shops,
    }

    # 3. Starter 6-Hero Level-1 Party
    races, castes = parse_races_and_castes(df / "Data Race", df / "Data Caste", fj_rsrc)
    item_lookup = {it["id"]: it for it in formatted_items}
    heroes_out: List[Dict[str, Any]] = []
    for spec in STARTER_PARTY_SPECS:
        race = races[spec["race_id"]]
        caste = castes[spec["caste_id"]]
        att_bonus_r = race["att_bonus"]
        att_bonus_c = caste["att_bonus"]
        attrs = [
            max(6, min(20, spec["base_attrs"][i] + att_bonus_r[i] + att_bonus_c[i]))
            for i in range(6)
        ]
        br, kn, ju, ag, vi, lu = attrs
        vit_hp_bonus = max(0, (vi - 12) // 2)
        hp = max(8, caste["stamina_base"] + vit_hp_bonus)

        # Level-1 SP based on Sorcerer/Priest/Crusader casting affinity
        if spec["archetype"] == "Mage":
            sp = 16 + max(0, kn - 12)
        elif spec["archetype"] == "Cleric":
            sp = 14 + max(0, ju - 12)
        elif spec["archetype"] == "Paladin":
            sp = 8 + max(0, ju - 12)
        else:
            sp = 0

        # Export unequipped base AC and To-Hit only; RealmzCore PartyMemberState
        # applies equipped armor AC and weapon bonus (+5% per +1) once at runtime.
        base_ac = max(5, 10 + max(0, (ag - 12) // 2))
        to_hit = 10 + caste["tohit_base"] + max(0, (br - 12) // 2)
        dodge = max(2, caste["dodge_base"] + max(0, (ag - 12)))
        missile = caste["missile_base"] + max(0, (ag - 12))
        mv = max(8, race["base_move"] + caste["move_bonus"])
        atk_round = max(1.0, float(race["num_attacks"] + caste["bonus_attacks"]))

        heroes_out.append(
            {
                "id": spec["id"],
                "name": spec["name"],
                "archetype": spec["archetype"],
                "race_id": spec["race_id"],
                "race_name": race["name"],
                "caste_id": spec["caste_id"],
                "caste_name": caste["name"],
                "level": 1,
                "hp": hp,
                "max_hp": hp,
                "sp": sp,
                "max_sp": sp,
                "ac": base_ac,
                "to_hit": to_hit,
                "dodge": dodge,
                "missile": missile,
                "movement_max": mv,
                "attacks_per_round": atk_round,
                "icon_id": spec["icon_id"],
                "portrait_id": spec["portrait_id"],
                "attributes": {
                    "brawn": br,
                    "knowledge": kn,
                    "judgment": ju,
                    "agility": ag,
                    "vitality": vi,
                    "luck": lu,
                },
                "equipped_ids": list(spec["equipped_ids"]),
                "known_spell_ids": list(spec["known_spell_ids"]),
            }
        )

    party_json = {
        "party_name": "Bywater Vanguard",
        "initial_gold": 400,
        "stash_item_ids": [611, 611, 611, 611, 612, 602, 805, 805, 803, 1],
        "heroes": heroes_out,
    }

    # 4. Early Bywater Monsters
    all_monsters = parse_monsters(by / "Data MD")
    m_by_idx = {m["index"]: m for m in all_monsters if m["is_valid"]}
    monsters_out: List[Dict[str, Any]] = []
    for midx in STARTER_MONSTER_INDICES:
        m = m_by_idx.get(midx)
        if not m:
            continue
        hd = max(1, m["hd"])
        bonus = m["bonus"]
        hp_min = max(1, hd + bonus)
        hp_max = max(hp_min, hd * 8 + bonus)
        hp_def = max(4, round(hd * 4.5) + bonus)
        atk0 = m["attacks"][0] if m["attacks"] else [1, 4, 0, 0]
        atk_min, atk_max = atk0[0], atk0[1]
        if atk_min <= 0 and m["weapon_id"] in raw_items:
            vss = max(2, raw_items[m["weapon_id"]]["vssmall"])
            atk_dice = f"1d{vss}"
        else:
            atk_dice = _monster_dice_from_pair(atk_min, atk_max, hd)
        ranged_range = 8 if m["missile_chance_pct"] > 0 else 0
        ranged_dice = "1d6" if ranged_range > 0 else ""
        xp_val = m["exp"] if m["exp"] > 0 else hd * 25
        mtypes = m["monster_types"]
        monsters_out.append(
            {
                "index": m["index"],
                "id": _slugify(m["name"], f"monster_{m['index']}"),
                "name": m["name"],
                "icon_id": m["icon_id"],
                "hit_dice": hd,
                "hp_bonus": bonus,
                "hp_min": hp_min,
                "hp_max": hp_max,
                "hp_default": hp_def,
                "ac": m["ac"],
                "to_hit_bonus": hd * 5 + max(0, m["damage_bonus"]) * 5,
                "dexterity": m["dx"],
                "movement_max": max(6, m["movement_max"]),
                "size": m["size"],
                "num_attacks": m["num_attacks"],
                "attack_dice": atk_dice,
                "damage_bonus": m["damage_bonus"],
                "missile_chance_pct": m["missile_chance_pct"],
                "cast_chance_pct": m["cast_chance_pct"],
                "ranged_range": ranged_range,
                "ranged_dice": ranged_dice,
                "spell_ids": [sid for sid in m["spell_ids"] if sid in CURATED_SPELL_CONFIG],
                "magic_resistance": m["magic_resistance"],
                "saves": m["saves"],
                "spell_immunities": m["spell_immunities"],
                "is_evil": bool(mtypes[4]) if len(mtypes) > 4 else False,
                "is_undead": bool(mtypes[1]) if len(mtypes) > 1 else False,
                "xp_reward": xp_val,
                "gold_min": 0,
                "gold_max": max(0, m["money"][0]),
                "loot_item_ids": [iid for iid in m["loot_item_ids"] if iid in item_price_map],
            }
        )

    monsters_json = {
        "scenario": "City of Bywater",
        "count": len(monsters_out),
        "monsters": monsters_out,
    }

    return {
        "land_0.json": land0_json,
        "dungeon_0.json": dungeon0_json,
        "party_m1.json": party_json,
        "monsters_bywater_m1.json": monsters_json,
        "spells_items_m1.json": spells_items_json,
    }
