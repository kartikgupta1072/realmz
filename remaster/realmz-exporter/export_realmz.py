"""Self-contained CLI exporter for Realmz Classic Mac binary and .rsrc assets."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
from typing import Any, Dict, List, Optional

from PIL import Image

from bywater_m1 import build_bywater_m1_payloads
from formats import (
    parse_character,
    parse_doors,
    parse_land_grid,
    parse_maps_metadata,
    parse_monsters,
    parse_rand_level,
    parse_scenario_header,
    sanitize_text,
)
from graphics import (
    build_land_minimap,
    compute_tile_palette,
    decode_cicn,
    decode_pict,
    slice_tilesheet,
)
from rsrc import ResourceFork


def sha256_file(path: Path) -> str:
    """Compute hex SHA-256 digest of a file."""
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def write_json(path: Path, payload: Any) -> Path:
    """Write formatted UTF-8 JSON with strictly zero em/en-dashes."""
    text = json.dumps(payload, indent=2, ensure_ascii=False) + "\n"
    sanitized = sanitize_text(text)
    path.write_text(sanitized, encoding="utf-8")
    return path


def save_rgba_png(path: Path, image: Image.Image) -> Dict[str, Any]:
    """Save an RGBA PNG and return pixel channel stats for manifest.json."""
    rgba = image.convert("RGBA")
    rgba.save(path, format="PNG")
    alphas = rgba.tobytes()[3::4]
    opaque = sum(1 for a in alphas if a > 0)
    transparent = len(alphas) - opaque
    return {
        "width": rgba.width,
        "height": rgba.height,
        "mode": rgba.mode,
        "opaque_pixels": opaque,
        "transparent_pixels": transparent,
    }


def export_scenario(realmz_base: Path, scenario_name: str, out_dir: Path) -> Dict[str, Any]:
    """Extract reference JSON data and PNG sprites/tiles for a Realmz scenario."""
    base = Path(realmz_base)
    data_files = base / "Data Files"
    char_dir = base / "Character Files"
    scen_dir = base / "Scenarios" / scenario_name

    for required in (data_files, char_dir, scen_dir):
        if not required.is_dir():
            raise FileNotFoundError(f"Missing required Realmz directory: {required}")

    out_dir.mkdir(parents=True, exist_ok=True)
    artifacts: List[Dict[str, Any]] = []

    def record_json(name: str, payload: Any, source_file: str) -> Path:
        p = write_json(out_dir / name, payload)
        artifacts.append(
            {
                "file": name,
                "kind": "json",
                "bytes": p.stat().st_size,
                "sha256": sha256_file(p),
                "source": source_file,
            }
        )
        return p

    def record_png(
        name: str,
        image: Image.Image,
        source_rsrc: str,
        source_type: str,
        source_id: Any,
        note: str = "",
    ) -> Path:
        p = out_dir / name
        stats = save_rgba_png(p, image)
        entry: Dict[str, Any] = {
            "file": name,
            "kind": "png",
            "bytes": p.stat().st_size,
            "sha256": sha256_file(p),
            "source_rsrc": source_rsrc,
            "source_type": source_type,
            "source_id": source_id,
            **stats,
        }
        if note:
            entry["note"] = sanitize_text(note)
        artifacts.append(entry)
        return p

    # Load Mac resource forks.
    fj_rsrc = ResourceFork(data_files / "The Family Jewels.rsrc")
    portraits_rsrc = ResourceFork(data_files / "Portraits.rsrc")
    tacticals_rsrc = ResourceFork(data_files / "Tacticals.rsrc")
    scen_rsrc_path = scen_dir / "Scenario.rsrc"
    scen_rsrc = ResourceFork(scen_rsrc_path) if scen_rsrc_path.is_file() else None

    race_names = [sanitize_text(s) for s in fj_rsrc.get_str_list(129)]
    caste_names = [sanitize_text(s) for s in fj_rsrc.get_str_list(131)]

    # 1. Scenario metadata & Data MD2.
    scen_header_path = scen_dir / scenario_name
    scen_header = parse_scenario_header(scen_header_path) if scen_header_path.is_file() else {}
    maps_md2 = parse_maps_metadata(scen_dir / "Data MD2")
    map_names: List[str] = []
    if scen_rsrc and scen_rsrc.has("STR#", -102):
        map_names = [sanitize_text(s) for s in scen_rsrc.get_str_list(-102)]

    # 2. Land 0 + Data RD[0] + Data DD[0].
    land0 = parse_land_grid(scen_dir / "Data LD", level=0)
    rd0 = parse_rand_level(scen_dir / "Data RD", level=0)
    doors0 = parse_doors(scen_dir / "Data DD", level=0)
    land0_doors = [d for d in doors0 if d["land_id"] == 0]
    md2_0 = maps_md2[0] if maps_md2 else {}

    land0_payload = {
        "scenario": sanitize_text(scenario_name),
        "level": 0,
        "width": land0["width"],
        "height": land0["height"],
        "layout": land0["layout"],
        "landlook": rd0["landlook"],
        "tileset_pict_id": rd0["tileset_pict_id"],
        "isdark": rd0["isdark"],
        "uselos": rd0["uselos"],
        "start_position": {
            "look_x": scen_header.get("start_look_x", 0),
            "look_y": scen_header.get("start_look_y", 0),
            "map0_start_x": md2_0.get("startx", 0),
            "map0_start_y": md2_0.get("starty", 0),
            "map0_level": md2_0.get("level", 0),
        },
        "stats": land0["stats"],
        "ap_doors_summary": {
            "total_records_in_level0_block": len(doors0),
            "land_0_matching_doors": len(land0_doors),
            "doors": doors0,
        },
        "encounter_zones": rd0["encounter_zones"],
        "tiles_yx": land0["tiles_yx"],
    }
    record_json("bywater_land_0.json", land0_payload, "Data LD + Data RD + Data DD + Data MD2")

    # 3. Terrain PICT 300 tilesheet, most common Land 0 tile, and Land 0 90x90 preview.
    tileset_id = rd0["tileset_pict_id"]
    tilesheet_img = decode_pict(fj_rsrc.get("PICT", tileset_id).data)
    record_png(
        "terrain_bywater_tilesheet_300.png",
        tilesheet_img,
        "The Family Jewels.rsrc",
        "PICT",
        tileset_id,
    )

    grass_tile_id = land0["stats"]["most_common_terrain_tile_id"]
    grass_tile_img = slice_tilesheet(tilesheet_img, tile_id=grass_tile_id)
    record_png(
        "terrain_bywater_tile_grass.png",
        grass_tile_img,
        "The Family Jewels.rsrc",
        "PICT_TILE",
        f"{tileset_id}#tile_{grass_tile_id}",
        note=f"Most common Land 0 terrain tile (tile_id={grass_tile_id}, count={land0['stats']['most_common_terrain_tile_count']})",
    )

    tile_palette = compute_tile_palette(tilesheet_img)
    ap_coords = [(d["x"], d["y"]) for d in land0_doors]
    minimap_img = build_land_minimap(land0["tiles_yx"], tile_palette, ap_coords=ap_coords)
    record_png(
        "bywater_land_0_preview.png",
        minimap_img,
        "Data LD + Data DD + PICT 300",
        "MINIMAP",
        0,
        note="90x90 pixel color minimap of Land 0",
    )

    # 4. Heroes (all 6 character files) + Kevlar tactical 9018 + Kevlar portrait 275.
    heroes: List[Dict[str, Any]] = []
    for char_file in sorted(p for p in char_dir.iterdir() if p.is_file()):
        heroes.append(parse_character(char_file, race_names=race_names, caste_names=caste_names))

    record_json(
        "heroes.json",
        {"count": len(heroes), "heroes": heroes},
        "Character Files/* + The Family Jewels.rsrc STR# 129,131",
    )

    kevlar = next((h for h in heroes if h["name"] == "Kevlar"), heroes[0])
    kevlar_tactical_img = decode_cicn(tacticals_rsrc.get("cicn", kevlar["icon_id"]).data)
    record_png(
        "hero_kevlar_tactical_9018.png",
        kevlar_tactical_img,
        "Tacticals.rsrc",
        "cicn",
        kevlar["icon_id"],
    )

    kevlar_portrait_img = decode_cicn(portraits_rsrc.get("cicn", kevlar["pict_id"]).data)
    record_png(
        "hero_kevlar_portrait_275.png",
        kevlar_portrait_img,
        "Portraits.rsrc",
        "cicn",
        kevlar["pict_id"],
    )

    # 5. Monsters (155 slots in Data MD) + Giant Frog cicn 392.
    monsters = parse_monsters(scen_dir / "Data MD")
    active_monsters = [m for m in monsters if m["is_valid"]]
    record_json(
        "monsters.json",
        {
            "total_slots": len(monsters),
            "active_count": len(active_monsters),
            "monsters": monsters,
        },
        "Data MD",
    )

    giant_frog = next((m for m in active_monsters if m["name"] == "Giant Frog"), active_monsters[0])
    giant_frog_img = decode_cicn(fj_rsrc.get("cicn", giant_frog["icon_id"]).data)
    record_png(
        "monster_giant_frog_392.png",
        giant_frog_img,
        "The Family Jewels.rsrc",
        "cicn",
        giant_frog["icon_id"],
    )

    # 6. Reference NPC portrait cicn 257 from Portraits.rsrc.
    npc_portrait_img = decode_cicn(portraits_rsrc.get("cicn", 257).data)
    record_png(
        "portrait_bywater_npc_257.png",
        npc_portrait_img,
        "Portraits.rsrc",
        "cicn",
        257,
        note="Native 44x44 portrait cicn 257 from Portraits.rsrc",
    )

    # 7. Scenario summary JSON.
    scenario_payload = {
        "scenario": sanitize_text(scenario_name),
        "header": scen_header,
        "land_levels": land0["total_levels"],
        "monster_slots": len(monsters),
        "active_monster_count": len(active_monsters),
        "starter_hero_count": len(heroes),
        "map_names": map_names,
        "maps_md2": maps_md2,
        "land_0_summary": {
            "landlook": rd0["landlook"],
            "isdark": rd0["isdark"],
            "uselos": rd0["uselos"],
            "most_common_terrain_tile_id": grass_tile_id,
            "ap_door_count": len(doors0),
        },
    }
    record_json("scenario.json", scenario_payload, "City of Bywater + Data MD2 + Scenario.rsrc")

    # 7b. Milestone 1 Bywater vertical-slice bundle (extracted/bywater_m1/).
    if scenario_name == "City of Bywater":
        m1_dir = out_dir / "bywater_m1"
        m1_dir.mkdir(parents=True, exist_ok=True)
        m1_payloads = build_bywater_m1_payloads(base)
        for fname, payload in m1_payloads.items():
            rel = f"bywater_m1/{fname}"
            p = write_json(m1_dir / fname, payload)
            artifacts.append(
                {
                    "file": rel,
                    "kind": "json",
                    "bytes": p.stat().st_size,
                    "sha256": sha256_file(p),
                    "source": "City of Bywater M1 vertical slice",
                }
            )

    # 8. Manifest JSON (includes SHA-256 of every prior artifact).
    manifest_payload = {
        "exporter": "realmz-exporter",
        "version": "1.1.0",
        "scenario": sanitize_text(scenario_name),
        "realmz_base": str(base),
        "output_dir": str(out_dir),
        "artifact_count": len(artifacts),
        "artifacts": artifacts,
    }
    write_json(out_dir / "manifest.json", manifest_payload)
    return manifest_payload


def build_arg_parser() -> argparse.ArgumentParser:
    """Create CLI argument parser."""
    parser = argparse.ArgumentParser(
        description="Extract Realmz scenario maps, entities, and Mac .rsrc graphics to JSON/PNG."
    )
    parser.add_argument(
        "--realmz-base",
        type=Path,
        default=Path(r"C:\Users\karti\realmz\base\Realmz"),
        help="Path to the base Realmz directory containing Data Files, Character Files, and Scenarios.",
    )
    parser.add_argument(
        "--scenario",
        type=str,
        default="City of Bywater",
        help="Scenario directory name under Scenarios/.",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path(r"C:\Users\karti\realmz\remaster\extracted"),
        help="Destination directory for extracted JSON and PNG assets.",
    )
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    """CLI entry point."""
    # Ensure sibling modules resolve regardless of current working directory.
    script_dir = str(Path(__file__).resolve().parent)
    if script_dir not in sys.path:
        sys.path.insert(0, script_dir)

    parser = build_arg_parser()
    args = parser.parse_args(argv)
    try:
        manifest = export_scenario(args.realmz_base, args.scenario, args.out)
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(
        f"Exported {manifest['artifact_count']} artifacts for '{manifest['scenario']}' -> {manifest['output_dir']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
