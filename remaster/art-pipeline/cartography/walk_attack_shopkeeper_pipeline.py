"""
Realmz Walk, Attack & Bywater Shopkeeper Production Pipeline.

Generates 64 px/m Songs of Conquest Sunlit-128 animations and shopkeepers:
  - 13 Combat Units (6 Heroes + 7 Bywater Monsters): Walk (512x128) + Attack (512x128) + *_n.png
    using exact Idle f0 IP-Adapter identity lock from Unity sheets (read-only),
    Blender 4.5.9 Depth + Canny + interior-blurred #FF00FF chroma guides,
    shared base seeds across frames, constant world-space px_per_render_px scaling,
    and Blender ground-plane anchoring (grounded y=125 or airborne hop/pounce).
  - 4 Bywater Shopkeepers (blacksmith, tanner, apothecary, priestess):
    Idle 4-frame strip (512x128 + *_n.png) + matching 128x128 bronze/gold-framed
    portrait (portrait_npc_<id>.png + *_n.png).
  - Automated per-frame/strip QA gates (palette, 1-bit alpha, magenta despill,
    flat-blob check, laigter +Y normals, fg pixel ratio, inter-frame motion IoU/diff)
    with up to 3 seed rerolls and resumable state.json.
  - Outputs anim_shopkeeper_manifest.json and 3 proof contact sheets (plus
    pilot_comparison.png in --pilot mode).

Strictly writes only inside C:\\Users\\karti\\realmz\\.scratch\\realmz_walk_attack_shopkeepers\\.
Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW = 0x08000000 in all subprocesses.
"""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import json
import math
import os
import subprocess
import sys
import time
import urllib.parse
from collections import deque
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

CART_DIR = Path(__file__).resolve().parent
PIPE_DIR = CART_DIR.parent
if str(CART_DIR) not in sys.path:
    sys.path.insert(0, str(CART_DIR))
if str(PIPE_DIR) not in sys.path:
    sys.path.insert(0, str(PIPE_DIR))

import pixel_postprocess as pp  # noqa: E402

CREATE_NO_WINDOW = 0x08000000
SERVER_URL = "http://127.0.0.1:8188"
COMFY_PYTHON = Path(r"C:\ComfyUI\.venv\Scripts\python.exe")
COMFY_MAIN = Path(r"C:\ComfyUI\main.py")
COMFY_ROOT = Path(r"C:\ComfyUI")

PALETTE_PATH = PIPE_DIR / "palette_soc_sunlit_128.json"
MAGENTA_EXCLUDE = {"#E8569C", "#CE9BF0", "#E6C4FA", "#AE73D6"}

UNITY_UNITS_DIR = Path(r"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Art\Cartography\units")
MEADOW_TILE_PATH = Path(r"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Art\Cartography\tiles\meadow_grass.png")
ARLEON_TROOPS_REF = Path(
    r"C:\Users\karti\realmz\.scratch\realmz_soc_reverse_engineering\references\grass_frames\shadows_gif\arleon_troops_f01.png"
)

PACK_ROOT = Path(r"C:\Users\karti\realmz\.scratch\realmz_walk_attack_shopkeepers")
GUIDE_DIR = PACK_ROOT / "guides"
META_PATH = GUIDE_DIR / "guides_metadata.json"
RAW_DIR = PACK_ROOT / "raw"
IPREF_DIR = RAW_DIR / "iprefs"
SOFT_GUIDE_DIR = RAW_DIR / "soft_guides"
OUTPUT_DIR = PACK_ROOT / "output"
PILOT_OUT_DIR = OUTPUT_DIR / "pilot"
STATE_PATH = PACK_ROOT / "state.json"
MANIFEST_PATH = OUTPUT_DIR / "anim_shopkeeper_manifest.json"
LOG_PATH = PACK_ROOT / "comfyui_8188.log"

COMMON_SPRITE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "minecraft, voxel, lego, blocky cubes, low poly, geometric blocks, stiff box, "
    "multiple characters, sprite sheet, grid, floor, ground, grass ground, dirt mound, soil base, "
    "cast shadow, drop shadow, contact shadow, oval shadow, purple shadow, violet shadow, shadow puddle, shadow under feet, "
    "ground disc, pedestal, platform, isometric block, diorama, circle background, medallion, halo, vignette, text, watermark, "
    "signature, UI, frame, border, white background, grey background, lowres, worst quality, "
    "deformed anatomy, extra limbs, fused weapons, neon pink armor, magenta armor"
)

GRID_SPRITE_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "minecraft, voxel, lego, blocky cubes, low poly, geometric blocks, stiff box, "
    "floor, ground, grass ground, dirt mound, soil base, dividing lines, border lines, grid lines, box frame, "
    "cast shadow, drop shadow, contact shadow, oval shadow, purple shadow, violet shadow, shadow puddle, shadow under feet, "
    "ground disc, pedestal, platform, isometric block, diorama, circle background, medallion, halo, vignette, text, watermark, "
    "signature, UI, white background, grey background, lowres, worst quality, "
    "deformed anatomy, extra limbs, fused weapons, neon pink armor, magenta armor"
)

PORTRAIT_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "frame, border, oval frame, gold border, UI, text, watermark, signature, multiple people, "
    "full body, legs, boots, distant shot, faceless, blank face, empty hood, silhouette face, "
    "white background, grey background, dark background, castle background, "
    "deformed face, extra eyes, cut off head, lowres, worst quality, pink armor, neon pink skin"
)


# ---------------------------------------------------------------------------
# Dash Hygiene, Keep-Awake & Checkpoint State
# ---------------------------------------------------------------------------

def sanitize_dashes(text: str) -> str:
    return text.replace("\u2014", " - ").replace("\u2013", "-")


def write_clean_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    txt = sanitize_dashes(json.dumps(data, indent=2))
    tmp = path.with_suffix(".tmp")
    tmp.write_text(txt, encoding="utf-8")
    tmp.replace(path)


@contextlib.contextmanager
def keep_awake():
    """Keep Windows awake (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) during long runs."""
    es_continuous = 0x80000000
    es_system_required = 0x00000001
    if os.name == "nt":
        try:
            ctypes.windll.kernel32.SetThreadExecutionState(es_continuous | es_system_required)
        except Exception:
            pass
    try:
        yield
    finally:
        if os.name == "nt":
            try:
                ctypes.windll.kernel32.SetThreadExecutionState(es_continuous)
            except Exception:
                pass


class PackState:
    def __init__(self, state_path: Path) -> None:
        self.path = state_path
        self.data: dict = {"pipeline": "walk_attack_shopkeeper", "assets": {}}
        if state_path.is_file():
            try:
                loaded = json.loads(state_path.read_text(encoding="utf-8"))
                if isinstance(loaded, dict) and "assets" in loaded:
                    self.data = loaded
            except Exception:
                pass

    def is_done(self, asset_id: str, required_paths: Sequence[Path]) -> bool:
        rec = self.data.get("assets", {}).get(asset_id)
        if not rec or not rec.get("qa_passed"):
            return False
        for p in required_paths:
            if not p.is_file() or p.stat().st_size < 64:
                return False
        return True

    def record(self, asset_id: str, entry: dict) -> None:
        self.data.setdefault("assets", {})[asset_id] = entry
        write_clean_json(self.path, self.data)


# ---------------------------------------------------------------------------
# ComfyUI Lifecycle & VRAM Monitoring
# ---------------------------------------------------------------------------

def query_vram_mib() -> int:
    try:
        out = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            text=True,
            timeout=5,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        ).strip()
        return int(out.splitlines()[0].strip())
    except Exception:
        return -1


def is_comfy_up() -> bool:
    try:
        r = requests.get(f"{SERVER_URL}/system_stats", timeout=2.5)
        return r.ok
    except Exception:
        return False


def ensure_comfy_running() -> Optional[subprocess.Popen]:
    if is_comfy_up():
        print(f"[ComfyUI] Reusing running server on :8188 (VRAM={query_vram_mib()} MiB)", flush=True)
        return None
    if not COMFY_PYTHON.is_file() or not COMFY_MAIN.is_file():
        raise FileNotFoundError(f"ComfyUI not found at {COMFY_ROOT}")

    LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    log_fh = open(LOG_PATH, "ab")
    try:
        proc = subprocess.Popen(
            [
                str(COMFY_PYTHON),
                str(COMFY_MAIN),
                "--listen",
                "127.0.0.1",
                "--port",
                "8188",
                "--disable-auto-launch",
                "--reserve-vram",
                "0.6",
            ],
            cwd=str(COMFY_ROOT),
            stdin=subprocess.DEVNULL,
            stdout=log_fh,
            stderr=subprocess.STDOUT,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
    finally:
        log_fh.close()

    print(f"[ComfyUI] Spawned headless :8188 (pid={proc.pid}), waiting...", flush=True)
    deadline = time.time() + 100.0
    while time.time() < deadline:
        if proc.poll() is not None:
            raise RuntimeError(f"ComfyUI exited early rc={proc.returncode}. Log: {LOG_PATH}")
        if is_comfy_up():
            print(f"[ComfyUI] Ready (VRAM={query_vram_mib()} MiB).", flush=True)
            return proc
        time.sleep(1.0)
    proc.terminate()
    raise TimeoutError("Timed out waiting for ComfyUI startup on :8188")


def free_comfy_vram() -> int:
    try:
        requests.post(
            f"{SERVER_URL}/free",
            json={"unload_models": True, "free_memory": True},
            timeout=10.0,
        )
    except Exception:
        pass
    time.sleep(0.5)
    v = query_vram_mib()
    print(f"[ComfyUI] /free complete -> VRAM={v} MiB", flush=True)
    return v


def teardown_comfy(spawned_proc: Optional[subprocess.Popen], force_kill_any: bool = True) -> int:
    free_comfy_vram()
    if spawned_proc is not None and spawned_proc.poll() is None:
        print(f"[ComfyUI] Terminating spawned pid={spawned_proc.pid}...", flush=True)
        try:
            spawned_proc.terminate()
            spawned_proc.wait(timeout=10.0)
        except Exception:
            try:
                spawned_proc.kill()
                spawned_proc.wait(timeout=5.0)
            except Exception:
                pass
    if force_kill_any and os.name == "nt":
        ps_cmd = (
            "Get-NetTCPConnection -LocalPort 8188 -State Listen -ErrorAction SilentlyContinue | "
            "ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }"
        )
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", ps_cmd],
            capture_output=True,
            timeout=10,
            creationflags=CREATE_NO_WINDOW,
        )

    deadline = time.time() + 15.0
    v = query_vram_mib()
    while time.time() < deadline:
        v = query_vram_mib()
        if 0 <= v < 1000:
            break
        time.sleep(0.5)
    print(f"[ComfyUI] Teardown complete -> idle VRAM={v} MiB", flush=True)
    return v


def upload_image(image_path: Path, remote_name: str) -> str:
    with image_path.open("rb") as f:
        r = requests.post(
            f"{SERVER_URL}/upload/image",
            files={"image": (remote_name, f, "image/png")},
            data={"overwrite": "true"},
            timeout=30,
        )
        r.raise_for_status()
        return r.json()["name"]


def run_comfy_prompt(workflow: dict, timeout: float = 300.0) -> Tuple[bytes, float, int]:
    t0 = time.time()
    peak = query_vram_mib()
    r = requests.post(f"{SERVER_URL}/prompt", json={"prompt": workflow}, timeout=30)
    if not r.ok:
        raise RuntimeError(f"ComfyUI /prompt failed ({r.status_code}): {r.text}")
    pid = r.json()["prompt_id"]

    deadline = time.time() + timeout
    entry = None
    while time.time() < deadline:
        v = query_vram_mib()
        if v > peak:
            peak = v
        hr = requests.get(f"{SERVER_URL}/history/{pid}", timeout=10)
        if hr.ok:
            data = hr.json()
            if pid in data:
                entry = data[pid]
                st = entry.get("status", {})
                if st.get("status_str") == "error":
                    raise RuntimeError(f"ComfyUI execution error: {json.dumps(st)}")
                if entry.get("outputs"):
                    break
        time.sleep(0.35)

    if not entry:
        raise TimeoutError(f"ComfyUI timed out on {pid}")

    img_info = None
    for node_out in entry.get("outputs", {}).values():
        imgs = node_out.get("images", [])
        if imgs:
            img_info = imgs[0]
            break
    if not img_info:
        raise RuntimeError(f"No output images in {pid}")

    params = urllib.parse.urlencode(
        {
            "filename": img_info["filename"],
            "subfolder": img_info.get("subfolder", ""),
            "type": img_info.get("type", "output"),
        }
    )
    ir = requests.get(f"{SERVER_URL}/view?{params}", timeout=30)
    ir.raise_for_status()
    v = query_vram_mib()
    if v > peak:
        peak = v
    if peak >= 7800:
        print(f"[VRAM WARN] Peak VRAM hit {peak} MiB (>= 7800), flushing cache...", flush=True)
        free_comfy_vram()
    return ir.content, round(time.time() - t0, 2), peak


# ---------------------------------------------------------------------------
# Exact Unit & Shopkeeper Specifications
# ---------------------------------------------------------------------------

@dataclass
class UnitAnimSpec:
    unit_id: str
    display_name: str
    side: str  # "hero" or "monster"
    facing: str  # "East" or "West"
    archetype: str  # "biped", "magic_biped", "frog", "rat"
    attack_style: str
    visual_desc: str
    extra_neg: str
    walk_seeds: List[int]
    attack_seeds: List[int]
    ip_weight: float = 0.50
    ip_end: float = 0.78
    denoise: float = 0.88
    depth_strength: float = 0.44
    depth_end: float = 0.68
    canny_strength: float = 0.16
    canny_end: float = 0.36
    steps: int = 24


UNIT_SPECS: List[UnitAnimSpec] = [
    # 6 Heroes (Facing East / Right)
    UnitAnimSpec(
        unit_id="hero_knight",
        display_name="Aldric (Knight)",
        side="hero",
        facing="East",
        archetype="biped",
        attack_style="melee_shield",
        visual_desc=(
            "1boy, solo knight warrior facing right, red horsehair crest plume on helmet, "
            "silver steel plate armor, silver helmet and pauldrons, dark brown chest surcoat, "
            "cobalt-blue and gold kite shield, shining steel broadsword"
        ),
        extra_neg=", red sword, pink sword, skinny stick legs, dark background, castle background",
        walk_seeds=[710101, 710111, 710121],
        attack_seeds=[710151, 710161, 710171],
    ),
    UnitAnimSpec(
        unit_id="hero_cleric",
        display_name="Liora (Cleric)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        attack_style="cleric_smite",
        visual_desc=(
            "1girl, solo battle cleric priestess facing right, white and royal-blue pointed miter hood, "
            "sunlit gold pauldrons, white and royal-blue liturgical split robe, "
            "tall golden sun-crozier in back hand and spiked star-rod in front hand"
        ),
        extra_neg=", bare legs, modern clothing, dark robe, pink robe, floating halo, sunburst crown spikes, scythe, gold axe, back turned, facing away, purple cape",
        walk_seeds=[710211, 710231, 710241],
        attack_seeds=[710261, 710281, 710291],
        depth_strength=0.52,
        canny_strength=0.22,
    ),
    UnitAnimSpec(
        unit_id="hero_ranger",
        display_name="Kaelen (Ranger)",
        side="hero",
        facing="East",
        archetype="biped",
        attack_style="bow",
        visual_desc=(
            "1boy, solo woodland ranger archer facing right, vibrant emerald forest-green hooded cloak and green tunic #426643 #2E693B "
            "with olive-yellow shoulder patch, green-fletched quiver on back, wooden bow and arrow, brown leather boots"
        ),
        extra_neg=", blue cloak, navy hood, purple cloak, violet cloak, indigo hood, dark blue tunic, purple pants, gun, rifle, modern bow, neon green, pink cape",
        walk_seeds=[710301, 710311, 710321],
        attack_seeds=[710351, 710361, 710371],
        ip_weight=0.38,
    ),
    UnitAnimSpec(
        unit_id="hero_mage",
        display_name="Theron (Mage)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        attack_style="mage_cast",
        visual_desc=(
            "1boy, solo elder arcane battlemage wizard facing right, royal cobalt-blue pointed hood with gold trim, "
            "long flowing white beard, blue-and-gold wizard robe, tall oak staff topped with glowing cyan crystal orb"
        ),
        extra_neg=", pink robes, purple background, hat cut off, floating orbs detached",
        walk_seeds=[710401, 710411, 710421],
        attack_seeds=[710451, 710461, 710471],
    ),
    UnitAnimSpec(
        unit_id="hero_paladin",
        display_name="Seraphina (Paladin)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        attack_style="greatsword",
        visual_desc=(
            "1girl, solo holy paladin champion facing right, silver-gold winged knight helmet, "
            "white and bright gold sunburst tabard over gleaming silver steel plate armor, "
            "heavy long two-handed steel greatsword"
        ),
        extra_neg=", angel wings on back, dark black armor, skinny legs, red tabard, orange armor, crimson surcoat, red cloak, red plume, orange hair, red hair, crimson plume",
        walk_seeds=[710501, 710511, 710521],
        attack_seeds=[710561, 710581, 710591],
    ),
    UnitAnimSpec(
        unit_id="hero_rogue",
        display_name="Vex (Rogue)",
        side="hero",
        facing="East",
        archetype="biped",
        attack_style="twin_dagger",
        visual_desc=(
            "solo agile fantasy rogue assassin facing right, dark indigo stealth hood, white lower face mask, "
            "bright crimson scarf, dark leather vest, brown trousers, dark boots with white cuff, dual steel daggers"
        ),
        extra_neg=", blue dress, skirt, bare legs, giant cloud shadow, cyan shirt",
        walk_seeds=[710601, 710611, 710621],
        attack_seeds=[710651, 710661, 710671],
    ),
    # 7 Bywater Monsters (Facing West / Left)
    UnitAnimSpec(
        unit_id="monster_giant_frog",
        display_name="Giant Frog",
        side="monster",
        facing="West",
        archetype="frog",
        attack_style="frog",
        visual_desc=(
            "no humans, solo giant monster toad frog facing left, sunlit emerald-green spotted toad skin, "
            "bright golden-yellow throat sac and belly, bulging yellow-orange eyes, wide red mouth"
        ),
        extra_neg=", pure black skin, dark black frog, purple frog, grey frog, skinny upright frog, lizard, tail, horns, antlers, spear, lilypad, water puddle, pond",
        walk_seeds=[720101, 720111, 720121],
        attack_seeds=[720151, 720161, 720171],
    ),
    UnitAnimSpec(
        unit_id="monster_goblin_raider",
        display_name="Goblin Raider",
        side="monster",
        facing="West",
        archetype="biped",
        attack_style="melee_shield",
        visual_desc=(
            "no humans, solo goblin raider warrior facing left, yellow-green goblin head and long pointed ears, "
            "pale ice-cyan bone and leather armor harness #E0F9FF #BDF7EE, dark tattered loincloth, curved blue-steel scimitar"
        ),
        extra_neg=", giant troll, tall orc, lizard, tail, pure black outline blob, grey puddle shadow, purple skin, blue skin, red armor, crimson vest, red chestplate, huge scythe, giant crescent blade, status bar, UI bar, black bar",
        walk_seeds=[720211, 720221, 720231],
        attack_seeds=[720251, 720261, 720271],
    ),
    UnitAnimSpec(
        unit_id="monster_goblin_archer",
        display_name="Goblin Archer",
        side="monster",
        facing="West",
        archetype="biped",
        attack_style="bow",
        visual_desc=(
            "no humans, solo goblin archer scout facing left, brown leather hood #73311D, vibrant emerald-green goblin skin #3D8748 #52A653 with pointed ears, "
            "quiver of white-fletched arrows on back, wooden shortbow and arrow"
        ),
        extra_neg=", lizard, kobold, dragon, reptile, orange scales, tail, horns, spear, tall human, purple hood, blue tunic, blue skin, blue goblin, purple skin, cyan skin, indigo skin, blue armor, purple armor",
        walk_seeds=[720301, 720311, 720321],
        attack_seeds=[720351, 720361, 720371],
        ip_weight=0.34,
    ),
    UnitAnimSpec(
        unit_id="monster_orc_warrior",
        display_name="Orc Warrior",
        side="monster",
        facing="West",
        archetype="biped",
        attack_style="heavy_axe",
        visual_desc=(
            "no humans, solo massive hulking orc warrior facing left, dark charcoal-black iron armor and spiked black shoulder pauldrons #11131F #1C2033 #323845, "
            "dark charcoal-black iron greaves and boots, muted dark olive-green orc face and forearms with ivory tusks #426643, "
            "dark crimson-rust waist kilt #4A141E #701D2B, single-headed dark slate-iron battleaxe on wooden shaft #606B80 #633C24"
        ),
        extra_neg=(
            ", tiny goblin, human face, pistol, pink armor, purple, violet, purple armor, violet pauldrons, purple legs, "
            "blue armor, blue boots, blue axe, neon orange, bright orange, neon green, yellow-green, bright yellow, rainbow, "
            "striped, red blade, red axe, scythe, crescent scythe, red spear, double blades, twin axes, curved horns, "
            "sprouting horns, bare human chest, brown skin, tan skin, pink skin, orange puddle"
        ),
        walk_seeds=[720495, 720505, 720515],
        attack_seeds=[720525, 720535, 720545],
        ip_weight=0.62,
        denoise=0.78,
        depth_strength=0.56,
        canny_strength=0.24,
    ),
    UnitAnimSpec(
        unit_id="monster_giant_rat",
        display_name="Giant Rat",
        side="monster",
        facing="West",
        archetype="rat",
        attack_style="rat",
        visual_desc=(
            "no humans, solo giant monstrous cellar rat beast facing left, warm chestnut-brown shaggy fur #633C24 #825132 #A36943, "
            "pink snout and torn ears and clawed paws, sharp white fangs, glowing crimson eyes, thin low trailing tail behind rump"
        ),
        extra_neg=", pure black fur, flat brown blob, grey fur, black fur, dark charcoal rat, bright red fur, crimson rat, green moss, spikes on back, dorsal fin, shark fin, crescent blade, wing, bat wing, scorpion tail, umbrella tail, arch over back, arch over head, floating arc, purple shadow, horns, antlers, tusks, dragon, reptile, cat, standing bipedal rat",
        walk_seeds=[720531, 720541, 720551],
        attack_seeds=[720581, 720591, 720601],
        ip_weight=0.30,
        denoise=0.80,
        depth_strength=0.60,
        canny_strength=0.28,
    ),
    UnitAnimSpec(
        unit_id="monster_skeleton_warrior",
        display_name="Skeleton Warrior",
        side="monster",
        facing="West",
        archetype="biped",
        attack_style="claw_shield",
        visual_desc=(
            "no humans, solo undead skeleton warrior facing left, bare glowing cyan-tinted skull with NO helmet, "
            "dark eye sockets, glowing cyan ribcage, intact solid leg bones and feet, white and gold-trimmed loincloth, "
            "dark round shield on back arm, bony clawed forward arm"
        ),
        extra_neg=", helmet, iron helmet, pot helmet, red cloth, red loincloth, crimson waistcloth, fleshy human skin, modern gun, broken legs, missing legs, fragmented shins, floating torso, disconnected feet, thin stick legs",
        walk_seeds=[720601, 720611, 720621],
        attack_seeds=[720681, 720691, 720701],
    ),
    UnitAnimSpec(
        unit_id="monster_bandit_cutthroat",
        display_name="Bandit Cutthroat",
        side="monster",
        facing="West",
        archetype="biped",
        attack_style="melee_dual",
        visual_desc=(
            "1boy, solo medieval bandit highwayman cutthroat facing left, crimson bandana head and face mask, "
            "tan leather shoulder mantle, maroon-and-gold striped vest, dark blue trousers, brown boots, straight steel sword"
        ),
        extra_neg=", empty hands, unarmed, purple cloak, violet shadow, bucket helmet, transparent body holes, pink tunic",
        walk_seeds=[720701, 720711, 720721],
        attack_seeds=[720751, 720761, 720771],
    ),
]

UNIT_SPEC_BY_ID: Dict[str, UnitAnimSpec] = {u.unit_id: u for u in UNIT_SPECS}


@dataclass
class NpcSpec:
    npc_id: str
    display_name: str
    archetype: str
    sprite_prompt: str
    extra_neg: str
    portrait_prompt: str
    sprite_seeds: List[int]
    portrait_seeds: List[int]
    ip_weight: float = 0.34
    denoise: float = 0.86
    depth_strength: float = 0.48
    canny_strength: float = 0.22


NPC_SPECS: List[NpcSpec] = [
    NpcSpec(
        npc_id="npc_blacksmith",
        display_name="Bywater Blacksmith",
        archetype="biped",
        sprite_prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo stout master medieval blacksmith artisan, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG town shopkeeper sprite, "
            "3/4 oblique standing stance facing slightly right, full body head to boots centered, "
            "thick dark brown beard and hair, rolled-sleeve cream linen shirt #F0ECE4, heavy brown leather smithing apron #633C24 and dark belt, "
            "charcoal trousers, holding iron forging tongs in back hand and raised steel blacksmith hammer #9AA7BD in front hand, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon town villager style, NO ground shadow"
        ),
        extra_neg=", full plate armor, helmet, sword, anvil on floor, forge fire background, pink apron",
        portrait_prompt=(
            "1boy, stout bearded medieval master blacksmith artisan with warm weathered face, thick dark brown beard, "
            "cream shirt collar and dark brown leather smithing apron straps, sturdy determined craftsman gaze"
        ),
        sprite_seeds=[730101, 730111, 730121],
        portrait_seeds=[740131, 740141, 740151],
    ),
    NpcSpec(
        npc_id="npc_tanner",
        display_name="Bywater Tanner",
        archetype="biped",
        sprite_prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo medieval woodland tanner leatherworker merchant, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG town shopkeeper sprite, "
            "3/4 oblique standing stance facing slightly right, full body head to boots centered, "
            "mossy-green pointed hooded cloak #426643, tan hide leather vest #A36943, dark brown cross-body satchel and belt #633C24, "
            "sturdy brown leather boots with tan cuffs, holding coiled brown leather strap in back hand and steel cutting awl #D8E2F0 in front hand, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon villager style, NO ground shadow"
        ),
        extra_neg=", plate armor, sword, bow, shield, dark background, pink leather",
        portrait_prompt=(
            "1boy, keen-eyed medieval leatherworker tanner wearing mossy-green hooded cloak #426643 over tan hide leather vest collar "
            "and brown leather satchel strap, friendly rugged merchant expression"
        ),
        sprite_seeds=[730201, 730211, 730221],
        portrait_seeds=[740201, 740211, 740221],
    ),
    NpcSpec(
        npc_id="npc_apothecary",
        display_name="Bywater Apothecary",
        archetype="magic_biped",
        sprite_prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo scholarly medieval alchemist apothecary shopkeeper, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG town shopkeeper sprite, "
            "3/4 oblique standing stance facing slightly right, full body head to boots centered, "
            "plum-purple scholar cap with gold band #5A2D5E #ECA938, brass round spectacles, "
            "deep teal scholar robe #1D5E63 with plum shoulder mantle and gold collar and belt, "
            "holding bundled green herbs #52A653 in back hand and glowing emerald-cyan glass potion flask #54C9C0 raised in front hand, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon scholar style, NO ground shadow"
        ),
        extra_neg=", armor, sword, helmet, wizard hat cone, pink robe, floating flask",
        portrait_prompt=(
            "scholarly medieval alchemist apothecary wearing plum-purple cap with sunlit gold band, brass round spectacles over sharp eyes, "
            "deep teal robe with plum shoulders and sunlit gold collar, faint emerald-cyan potion glow"
        ),
        sprite_seeds=[730301, 730311, 730321],
        portrait_seeds=[740301, 740311, 740321],
    ),
    NpcSpec(
        npc_id="npc_priestess",
        display_name="Bywater Priestess",
        archetype="magic_biped",
        sprite_prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1girl, solo gentle temple healer priestess shopkeeper, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG town shopkeeper sprite, "
            "3/4 oblique standing stance facing slightly right, full body head to hem centered, "
            "auburn hair crowned with sunlit gold circlet and blue gem #ECA938, "
            "ivory-white temple dress robe #F0ECE4 with gold hem trim and royal cerulean-blue shoulder shawl and stole #3452A1, "
            "holding tied parchment prayer scroll in back hand and hanging glowing sunlit golden censer lantern #FFF3B0 in front hand, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon style, NO ground shadow"
        ),
        extra_neg=", plate armor, sword, shield, dark black robe, pink dress, modern clothing",
        portrait_prompt=(
            "1girl, serene gentle temple healer priestess with warm auburn hair, sunlit gold circlet with small blue gem on brow, "
            "ivory-white temple collar and royal cerulean-blue shoulder shawl #3452A1 #ECA938, kind compassionate eyes"
        ),
        sprite_seeds=[730401, 730411, 730421],
        portrait_seeds=[740401, 740411, 740421],
    ),
]

NPC_SPEC_BY_ID: Dict[str, NpcSpec] = {n.npc_id: n for n in NPC_SPECS}


# ---------------------------------------------------------------------------
# Per-Frame Action Prompts
# ---------------------------------------------------------------------------

def build_frame_action_prompt(spec: UnitAnimSpec, anim: str, frame_idx: int) -> str:
    if anim == "walk":
        if spec.archetype == "frog":
            cues = [
                "walk cycle frame 1, crouched ready hop coiled low on ground",
                "walk cycle frame 2, mid-air leap upward, hind legs stretched back, forelegs reaching forward",
                "walk cycle frame 3, landing brace forward, forelegs touching down",
                "walk cycle frame 4, settled crouch recovery stance",
            ]
        elif spec.archetype == "rat":
            cues = [
                "walk cycle frame 1, stretched forward scurry stride, paws extended",
                "walk cycle frame 2, arched spine bounding step, paws gathered",
                "walk cycle frame 3, forward stride reach, long tail swaying",
                "walk cycle frame 4, compact scurry recovery step",
            ]
        else:
            cues = [
                "walk cycle frame 1, left leg forward stride, opposite arm swung back, dynamic walking gait",
                "walk cycle frame 2, passing mid-stride high point, upright balance, legs crossing cleanly",
                "walk cycle frame 3, right leg forward stride, opposite arm swung forward, steady march",
                "walk cycle frame 4, passing recovery step, shifting weight smoothly onto front foot",
            ]
        return cues[frame_idx]

    # Attack frames
    st = spec.attack_style
    if st == "frog":
        cues = [
            "attack frame 1, windup crouch, wide red mouth opening wide, golden throat sac puffed",
            "attack frame 2, peak tongue lash attack, long bright red-pink sticky tongue shooting straight forward-left from wide open mouth",
            "attack frame 3, follow-through snap, red tongue retracting back into open jaws",
            "attack frame 4, guard recovery, jaws closing back to ready combat crouch",
        ]
    elif st == "rat":
        cues = [
            "attack frame 1, windup coil, rearing back on hind legs with open fanged jaws",
            "attack frame 2, peak feral pounce lunge forward-left, claws extended and bared white fangs snapping",
            "attack frame 3, follow-through bite landing forward low",
            "attack frame 4, guard recovery pulling back into fierce crouched stance",
        ]
    elif st == "bow":
        cues = [
            "attack frame 1, windup draw, raising bow and pulling bowstring and arrow back to cheek",
            "attack frame 2, peak bow shot release, bow arm extended straight toward target, arrow loosing forward",
            "attack frame 3, follow-through recoil after arrow release, bow held steady",
            "attack frame 4, guard recovery lowering bow smoothly to ready stance",
        ]
    elif st in ("mage_cast", "cleric_smite"):
        cues = [
            "attack frame 1, windup spell raise, pulling staff or mace back while channeling bright cyan-gold arcane glow",
            "attack frame 2, peak spell cast thrust forward, bright glowing arcane energy flare surging from staff or star-rod tip",
            "attack frame 3, follow-through sweep, arcane sparkles trailing off lowered weapon",
            "attack frame 4, guard recovery returning staff or mace to composed battle stance",
        ]
    elif st == "heavy_axe":
        cues = [
            "attack frame 1, heavy windup coil, hoisting huge battleaxe high behind shoulder",
            "attack frame 2, peak overhead crushing cleave lunge, huge notched battleaxe swinging powerfully forward-left",
            "attack frame 3, follow-through sweeping chop low across body",
            "attack frame 4, guard recovery hefting battleaxe back to ready war stance",
        ]
    elif st == "greatsword":
        cues = [
            "attack frame 1, windup two-handed swing, drawing steel greatsword back over shoulder",
            "attack frame 2, peak radiant greatsword slash lunging forward-right with extended arms",
            "attack frame 3, follow-through diagonal blade sweep across front",
            "attack frame 4, guard recovery raising greatsword back into noble combat guard",
        ]
    elif st == "twin_dagger":
        cues = [
            "attack frame 1, windup crouch coil, pulling both steel daggers close to torso",
            "attack frame 2, peak rapid twin-dagger lunge forward-right, front blade thrusting out",
            "attack frame 3, follow-through cross-slash sweep with second dagger",
            "attack frame 4, guard recovery springing back into agile dual-dagger crouch",
        ]
    elif st == "claw_shield":
        cues = [
            "attack frame 1, windup coil, pulling bony clawed arm back behind dark round shield",
            "attack frame 2, peak undead claw slash lunge forward-left, bony claw raking outward",
            "attack frame 3, follow-through swipe with shield bracing front",
            "attack frame 4, guard recovery settling back into undead battle stance",
        ]
    else:  # melee_shield / melee_dual
        cues = [
            "attack frame 1, windup coil, drawing sword arm back while bracing shield or offhand",
            "attack frame 2, peak melee weapon lunge strike forward, steel blade thrusting and slashing outward",
            "attack frame 3, follow-through broad blade sweep across front",
            "attack frame 4, guard recovery pulling weapon smoothly back into ready guard",
        ]
    return cues[frame_idx]


def build_unit_anim_prompt(spec: UnitAnimSpec, anim: str, frame_idx: int) -> str:
    action_cue = build_frame_action_prompt(spec, anim, frame_idx)
    return (
        f"pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, {spec.visual_desc}, "
        f"simple background, solid flat bright magenta #FF00FF chroma key background, "
        f"isolated RPG combat unit sprite, 3/4 oblique tactical stance, full body head to feet centered, "
        f"{action_cue}, crisp dark slate ink inner outlines #11131F, warm golden-hour sunlight from upper left, "
        f"Songs of Conquest pixel art style, NO ground shadow"
    )


def build_unit_grid_prompt(spec: UnitAnimSpec, anim: str) -> str:
    if anim == "walk":
        seq = (
            "2x2 grid of 4 sequential walk cycle animation frames of the exact same character "
            "(top-left left stride, top-right mid-stride passing, bottom-left right stride, bottom-right recovery)"
        )
    else:
        seq = (
            "2x2 grid of 4 sequential attack animation frames of the exact same character "
            "(top-left windup coil, top-right peak strike lunge, bottom-left follow-through sweep, bottom-right guard recovery)"
        )
    return (
        f"pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, {spec.visual_desc}, "
        f"simple background, solid flat bright magenta #FF00FF chroma key background, "
        f"{seq}, identical character proportions and colors across all 4 quadrants, "
        f"clean empty magenta space around each figure, NO grid lines, NO borders, NO text, "
        f"crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest pixel art style, NO ground shadow"
    )


# ---------------------------------------------------------------------------
# Reference & Soft Guide Builders (Read-Only against RealmzUnity)
# ---------------------------------------------------------------------------

def inspect_unity_idle_f0(unit_id: str, sheet_path: Path) -> Tuple[np.ndarray, float, int, float, float]:
    """Read Unity Idle f0 (strip[:, :128, :]) read-only and compute foot_cx, fg pixel count, and CIELAB L mean/std."""
    if not sheet_path.is_file():
        raise FileNotFoundError(f"Unity Idle sheet not found: {sheet_path}")
    strip = np.array(Image.open(sheet_path).convert("RGBA"), dtype=np.uint8)
    f0 = strip[:, :128, :].copy()
    op = f0[..., 3] >= 128
    ys, xs = np.where(op)
    if len(ys) == 0:
        raise RuntimeError(f"Empty Idle f0 in {sheet_path}")
    ybot = int(ys.max())
    bh = int(ybot - ys.min() + 1)
    foot_band_y0 = max(0, ybot - max(6, int(round(bh * 0.15))))
    foot_xs = np.where(op[foot_band_y0 : ybot + 1, :])[1]
    idle_foot_cx = float(np.median(foot_xs)) if len(foot_xs) else float(np.mean(xs))
    idle_fg_px = int(np.sum(op))
    f0_lab = pp.rgb_to_lab_float(f0[..., :3])
    fg_l = f0_lab[op, 0]
    idle_l_mean = float(np.mean(fg_l))
    idle_l_std = float(np.std(fg_l))
    return f0, idle_foot_cx, idle_fg_px, idle_l_mean, idle_l_std


def build_f0_ipref(unit_id: str, f0_rgba: np.ndarray) -> Path:
    """
    Build <id>_f0_ipref.png: crop tight foreground bbox of f0 + 2px pad onto a
    square #FF00FF canvas so the unit fills ~90% of the 1024x1024 IP-Adapter ref.
    """
    IPREF_DIR.mkdir(parents=True, exist_ok=True)
    out_p = IPREF_DIR / f"{unit_id}_f0_ipref.png"
    op = f0_rgba[..., 3] >= 128
    ys, xs = np.where(op)
    y0 = max(0, int(ys.min()) - 2)
    y1 = min(f0_rgba.shape[0], int(ys.max()) + 1 + 2)
    x0 = max(0, int(xs.min()) - 2)
    x1 = min(f0_rgba.shape[1], int(xs.max()) + 1 + 2)

    crop = f0_rgba[y0:y1, x0:x1]
    ch, cw, _ = crop.shape
    side = max(ch, cw)
    sq = np.full((side, side, 3), (82, 78, 75), dtype=np.uint8)
    oy = (side - ch) // 2
    ox = (side - cw) // 2
    sub_fg = crop[..., 3] >= 128
    sub_dst = sq[oy : oy + ch, ox : ox + cw]
    sub_dst[sub_fg] = crop[..., :3][sub_fg]

    ipref_1024 = cv2.resize(sq, (1024, 1024), interpolation=cv2.INTER_NEAREST)
    Image.fromarray(ipref_1024, "RGB").save(out_p, "PNG")
    return out_p


def prepare_soft_chroma_and_depth(
    chroma_path: Path,
    depth_path: Path,
    out_stem: str,
    is_grid: bool = False,
) -> Tuple[Path, Path]:
    """
    Soften rigid Blender box edges strictly inside foreground (preserving pure #FF00FF
    exterior) and lightly blur depth map for organic pixel-art anatomy.
    """
    if not chroma_path.is_file() or not depth_path.is_file():
        raise FileNotFoundError(f"Missing guide files: {chroma_path} / {depth_path}")

    SOFT_GUIDE_DIR.mkdir(parents=True, exist_ok=True)
    p_soft_init = SOFT_GUIDE_DIR / f"{out_stem}_soft_init.png"
    p_soft_depth = SOFT_GUIDE_DIR / f"{out_stem}_soft_depth.png"

    sigma_rgb = 3.0 if is_grid else 5.0
    sigma_d = 2.0 if is_grid else 3.5

    arr = np.array(Image.open(chroma_path).convert("RGB"), dtype=np.uint8)
    r, g, b = arr[..., 0].astype(np.int16), arr[..., 1].astype(np.int16), arr[..., 2].astype(np.int16)
    is_mag = (r > 210) & (b > 210) & (g < 45)
    fg = ~is_mag
    fg_f = fg.astype(np.float32)
    denom = cv2.GaussianBlur(fg_f, (0, 0), sigmaX=sigma_rgb, sigmaY=sigma_rgb)
    blurred = np.empty_like(arr, dtype=np.float32)
    for c in range(3):
        num = cv2.GaussianBlur(arr[..., c].astype(np.float32) * fg_f, (0, 0), sigmaX=sigma_rgb, sigmaY=sigma_rgb)
        blurred[..., c] = np.where(denom > 1e-4, num / np.maximum(denom, 1e-4), 0.0)
    boosted = np.clip(blurred[fg] * 1.14 + 6.0, 0.0, 255.0)
    max_ch = np.max(boosted, axis=-1)
    dark = max_ch < 32.0
    if np.any(dark):
        safe_max = np.maximum(max_ch[dark], 1.0)
        scale = 32.0 / safe_max
        lifted = np.where(
            max_ch[dark, None] > 1.0,
            boosted[dark] * scale[:, None],
            32.0,
        )
        boosted[dark] = np.clip(lifted, 0.0, 255.0)
    soft_init = np.full_like(arr, (255, 0, 255), dtype=np.uint8)
    soft_init[fg] = np.clip(np.round(boosted), 0, 255).astype(np.uint8)
    Image.fromarray(soft_init, "RGB").save(p_soft_init, "PNG")

    d_arr = np.array(Image.open(depth_path).convert("L"), dtype=np.uint8)
    d_soft = cv2.GaussianBlur(d_arr, (0, 0), sigmaX=sigma_d, sigmaY=sigma_d)
    Image.fromarray(d_soft, "L").save(p_soft_depth, "PNG")
    return p_soft_init, p_soft_depth


def build_soc_style_ref() -> Optional[Path]:
    """Build 512x512 SoC Arleon unit collage on solid #FF00FF for NPC generation."""
    if not ARLEON_TROOPS_REF.is_file():
        return None
    out_p = IPREF_DIR / "soc_arleon_style_magenta.png"
    if out_p.is_file() and out_p.stat().st_size > 1000:
        return out_p
    out_p.parent.mkdir(parents=True, exist_ok=True)
    src = np.array(Image.open(ARLEON_TROOPS_REF).convert("RGB"))
    crops = [
        (src[325:495, 585:735], (20, 20), 210),
        (src[320:505, 755:885], (275, 20), 215),
        (src[585:805, 755:885], (20, 268), 220),
        (src[595:800, 585:735], (268, 268), 210),
    ]
    canvas = np.full((512, 512, 3), (255, 0, 255), dtype=np.uint8)
    for crop, (ox, oy), target_h in crops:
        h, w, _ = crop.shape
        lab = pp.rgb_to_lab_float(crop)
        border_lab = np.median(
            np.concatenate([lab[:4, :, :].reshape(-1, 3), lab[:, :4, :].reshape(-1, 3)], axis=0),
            axis=0,
        )
        dist = np.linalg.norm(lab - border_lab[None, None, :], axis=-1)
        is_bg_cand = (dist <= 16.5) & (lab[..., 0] < 22.0)
        ext_bg = np.zeros((h, w), dtype=bool)
        vis = np.zeros((h, w), dtype=bool)
        q: deque[Tuple[int, int]] = deque()
        for x in range(w):
            for y in (0, h - 1):
                if is_bg_cand[y, x] and not vis[y, x]:
                    vis[y, x] = True
                    ext_bg[y, x] = True
                    q.append((y, x))
        for y in range(h):
            for x in (0, w - 1):
                if is_bg_cand[y, x] and not vis[y, x]:
                    vis[y, x] = True
                    ext_bg[y, x] = True
                    q.append((y, x))
        while q:
            cy, cx = q.popleft()
            for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                if 0 <= ny < h and 0 <= nx < w and not vis[ny, nx] and is_bg_cand[ny, nx]:
                    vis[ny, nx] = True
                    ext_bg[ny, nx] = True
                    q.append((ny, nx))
        fg = ~ext_bg
        lbl, nc = ndimage.label(fg)
        if nc > 1:
            sizes = ndimage.sum(fg, lbl, range(1, nc + 1))
            mx = float(np.max(sizes))
            keep = np.zeros_like(fg)
            for idx, sz in enumerate(sizes, start=1):
                if sz >= max(40.0, mx * 0.02):
                    keep |= (lbl == idx)
            fg = keep
        ys_f, xs_f = np.where(fg)
        if len(ys_f) == 0:
            continue
        crop_fg = crop[int(ys_f.min()) : int(ys_f.max()) + 1, int(xs_f.min()) : int(xs_f.max()) + 1]
        mask_fg = fg[int(ys_f.min()) : int(ys_f.max()) + 1, int(xs_f.min()) : int(xs_f.max()) + 1]
        ch, cw = mask_fg.shape
        sc = target_h / max(1, ch)
        nw, nh = max(8, int(round(cw * sc))), max(8, int(round(ch * sc)))
        scaled = cv2.resize(crop_fg, (nw, nh), interpolation=cv2.INTER_NEAREST)
        fg_s = cv2.resize(mask_fg.astype(np.uint8) * 255, (nw, nh), interpolation=cv2.INTER_NEAREST) > 128
        y1 = min(512, oy + nh)
        x1 = min(512, ox + nw)
        sub_fg = fg_s[: y1 - oy, : x1 - ox]
        canvas[oy:y1, ox:x1][sub_fg] = scaled[: y1 - oy, : x1 - ox][sub_fg]
    Image.fromarray(canvas, "RGB").save(out_p, "PNG")
    return out_p


# ---------------------------------------------------------------------------
# ComfyUI Workflow Builder
# ---------------------------------------------------------------------------

def build_sdxl_pixel_workflow(
    prompt: str,
    negative: str,
    seed: int,
    filename_prefix: str,
    lora_name: str = "pixel-art-style-illustrious-by-skormino.safetensors",
    lora_strength: float = 0.88,
    steps: int = 26,
    cfg: float = 6.5,
    width: int = 1024,
    height: int = 1024,
    ip_ref_remote: Optional[str] = None,
    ip_weight: float = 0.54,
    ip_weight_type: str = "style transfer",
    ip_start: float = 0.0,
    ip_end: float = 0.80,
    depth_remote: Optional[str] = None,
    depth_strength: float = 0.56,
    depth_end: float = 0.72,
    canny_remote: Optional[str] = None,
    canny_strength: float = 0.28,
    canny_end: float = 0.52,
    init_remote: Optional[str] = None,
    denoise: float = 0.80,
) -> dict:
    wf: Dict[str, dict] = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "Illustrious-XL-v2.0.safetensors"}},
        "2": {
            "class_type": "LoraLoader",
            "inputs": {
                "model": ["1", 0],
                "clip": ["1", 1],
                "lora_name": lora_name,
                "strength_model": lora_strength,
                "strength_clip": lora_strength,
            },
        },
        "3": {"class_type": "CLIPSetLastLayer", "inputs": {"clip": ["2", 1], "stop_at_clip_layer": -2}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": negative}},
    }

    model_ref = ["2", 0]
    pos_ref = ["4", 0]
    neg_ref = ["5", 0]
    nid = 10

    if ip_ref_remote and ip_weight > 0.0:
        ip_ldr = str(nid); nid += 1
        cv_ldr = str(nid); nid += 1
        ip_img = str(nid); nid += 1
        ip_app = str(nid); nid += 1
        wf[ip_ldr] = {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": "ip-adapter-plus_sdxl_vit-h.safetensors"}}
        wf[cv_ldr] = {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"}}
        wf[ip_img] = {"class_type": "LoadImage", "inputs": {"image": ip_ref_remote}}
        wf[ip_app] = {
            "class_type": "IPAdapterAdvanced",
            "inputs": {
                "model": model_ref,
                "ipadapter": [ip_ldr, 0],
                "image": [ip_img, 0],
                "weight": ip_weight,
                "weight_type": ip_weight_type,
                "combine_embeds": "concat",
                "start_at": ip_start,
                "end_at": ip_end,
                "embeds_scaling": "V only",
                "clip_vision": [cv_ldr, 0],
            },
        }
        model_ref = [ip_app, 0]

    cn_ldr = None
    if (depth_remote and depth_strength > 0.0) or (canny_remote and canny_strength > 0.0):
        cn_ldr = str(nid); nid += 1
        wf[cn_ldr] = {"class_type": "ControlNetLoader", "inputs": {"control_net_name": "xinsir-controlnet-union-sdxl-1.0-promax.safetensors"}}

    if depth_remote and depth_strength > 0.0:
        d_type = str(nid); nid += 1
        d_img = str(nid); nid += 1
        d_app = str(nid); nid += 1
        wf[d_type] = {"class_type": "SetUnionControlNetType", "inputs": {"control_net": [cn_ldr, 0], "type": "depth"}}
        wf[d_img] = {"class_type": "LoadImage", "inputs": {"image": depth_remote}}
        wf[d_app] = {
            "class_type": "ControlNetApplyAdvanced",
            "inputs": {
                "positive": pos_ref,
                "negative": neg_ref,
                "control_net": [d_type, 0],
                "image": [d_img, 0],
                "strength": depth_strength,
                "start_percent": 0.0,
                "end_percent": depth_end,
                "vae": ["1", 2],
            },
        }
        pos_ref = [d_app, 0]
        neg_ref = [d_app, 1]

    if canny_remote and canny_strength > 0.0:
        c_type = str(nid); nid += 1
        c_img = str(nid); nid += 1
        c_app = str(nid); nid += 1
        wf[c_type] = {"class_type": "SetUnionControlNetType", "inputs": {"control_net": [cn_ldr, 0], "type": "canny/lineart/anime_lineart/mlsd"}}
        wf[c_img] = {"class_type": "LoadImage", "inputs": {"image": canny_remote}}
        wf[c_app] = {
            "class_type": "ControlNetApplyAdvanced",
            "inputs": {
                "positive": pos_ref,
                "negative": neg_ref,
                "control_net": [c_type, 0],
                "image": [c_img, 0],
                "strength": canny_strength,
                "start_percent": 0.0,
                "end_percent": canny_end,
                "vae": ["1", 2],
            },
        }
        pos_ref = [c_app, 0]
        neg_ref = [c_app, 1]

    if init_remote:
        init_img = str(nid); nid += 1
        init_enc = str(nid); nid += 1
        wf[init_img] = {"class_type": "LoadImage", "inputs": {"image": init_remote}}
        wf[init_enc] = {"class_type": "VAEEncode", "inputs": {"pixels": [init_img, 0], "vae": ["1", 2]}}
        latent_ref = [init_enc, 0]
    else:
        lat = str(nid); nid += 1
        wf[lat] = {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}}
        latent_ref = [lat, 0]

    ksampler = str(nid); nid += 1
    vaedec = str(nid); nid += 1
    save = str(nid); nid += 1
    wf[ksampler] = {
        "class_type": "KSampler",
        "inputs": {
            "model": model_ref,
            "positive": pos_ref,
            "negative": neg_ref,
            "latent_image": latent_ref,
            "seed": seed,
            "steps": steps,
            "cfg": cfg,
            "sampler_name": "dpmpp_2m",
            "scheduler": "karras",
            "denoise": denoise,
        },
    }
    wf[vaedec] = {"class_type": "VAEDecode", "inputs": {"samples": [ksampler, 0], "vae": ["1", 2]}}
    wf[save] = {"class_type": "SaveImage", "inputs": {"images": [vaedec, 0], "filename_prefix": filename_prefix}}
    return wf


# ---------------------------------------------------------------------------
# Palette, Guide-Protected Magenta Extraction & World-Space Anchored Scaling
# ---------------------------------------------------------------------------

def load_unit_palette(palette_path: Path = PALETTE_PATH) -> np.ndarray:
    data = json.loads(palette_path.read_text(encoding="utf-8"))
    colors = [c for c in data["colors"] if c.upper() not in MAGENTA_EXCLUDE]
    return np.array([pp.hex_to_rgb(c) for c in colors], dtype=np.uint8)


def _load_guide_protect_mask(guide_chroma_path: Optional[Path], target_hw: Tuple[int, int], dilate_px: int) -> Optional[np.ndarray]:
    if not guide_chroma_path or not guide_chroma_path.is_file():
        return None
    g_arr = np.array(Image.open(guide_chroma_path).convert("RGB"), dtype=np.uint8)
    if (g_arr.shape[0], g_arr.shape[1]) != target_hw:
        g_arr = cv2.resize(g_arr, (target_hw[1], target_hw[0]), interpolation=cv2.INTER_NEAREST)
    r, g, b = g_arr[..., 0].astype(np.int16), g_arr[..., 1].astype(np.int16), g_arr[..., 2].astype(np.int16)
    guide_fg = ~((r > 210) & (b > 210) & (g < 45))
    if not np.any(guide_fg):
        return None
    return ndimage.binary_dilation(guide_fg, structure=np.ones((3, 3), dtype=bool), iterations=dilate_px)


def extract_clean_magenta_unit(
    raw_arr: np.ndarray,
    guide_chroma_path: Optional[Path] = None,
    is_quadrant: bool = False,
    fill_interior_holes: bool = False,
    max_auto_fill_hole_px: int = 36,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Extract unit foreground from solid #FF00FF chroma backdrop with guide-mask
    protection: dark boots/armor inside dilate(guide_fg, 18px) are NEVER eaten
    by bottom cast-shadow stripping, while bright neon-magenta exterior and
    enclosed gaps are cleanly punched and despilled.
    """
    h, w, _ = raw_arr.shape
    dilate_px = 9 if is_quadrant else 18
    guide_protect = _load_guide_protect_mask(guide_chroma_path, (h, w), dilate_px=dilate_px)

    # Disable unconditional ground-shadow stripping inside pp.remove_background_1bit when guide_protect exists
    rgb, ext_fg = pp.remove_background_1bit(raw_arr, strip_ground_shadow=(guide_protect is None))
    r = rgb[..., 0].astype(np.int16)
    g = rgb[..., 1].astype(np.int16)
    b = rgb[..., 2].astype(np.int16)

    border_px = 3 if is_quadrant else 6
    ext_fg[:border_px, :] = False
    ext_fg[-border_px:, :] = False
    ext_fg[:, :border_px] = False
    ext_fg[:, -border_px:] = False

    mag_dom = np.minimum(r, b) - g
    enclosed_magenta = (mag_dom > 36) & (r > 155) & (b > 155) & (g < 115)
    fg_mask = ext_fg & (~enclosed_magenta)

    halo_iter = 6 if is_quadrant else 10
    halo_zone = ndimage.binary_dilation(~fg_mask, iterations=halo_iter) & fg_mask
    halo_magenta = halo_zone & (mag_dom > 20) & (r > 130) & (b > 130) & (g < 115)
    fg_mask &= ~halo_magenta

    # Keep primary body + meaningful detached elements touching guide region first
    lbl_pre, n_pre = ndimage.label(fg_mask)
    if n_pre > 1:
        sizes_pre = ndimage.sum(fg_mask, lbl_pre, range(1, n_pre + 1))
        max_pre = float(np.max(sizes_pre)) if len(sizes_pre) else 0.0
        keep_pre = np.zeros_like(fg_mask)
        min_comp_pre = 18.0 if is_quadrant else 36.0
        guide_halo_check = (
            ndimage.binary_dilation(guide_protect, structure=np.ones((3, 3), dtype=bool), iterations=(10 if is_quadrant else 20))
            if guide_protect is not None
            else None
        )
        for idx, sz in enumerate(sizes_pre, start=1):
            comp = lbl_pre == idx
            if sz >= max(min_comp_pre, max_pre * 0.006):
                if guide_halo_check is None or np.any(comp & guide_halo_check):
                    keep_pre |= comp
        fg_mask = keep_pre

    # Strip bottom cast-shadow puddles and horizontal floor rays outside guide_protect
    ys_tmp = np.where(fg_mask)[0]
    if len(ys_tmp) > 0:
        y_bot = int(ys_tmp.max())
        band_h = 24 if is_quadrant else 48
        foot_band = np.zeros_like(fg_mask)
        foot_band[max(0, y_bot - band_h) : y_bot + 1, :] = True
        lum_tmp = 0.2126 * r + 0.7152 * g + 0.0722 * b
        if guide_protect is not None:
            g_ys = np.where(guide_protect)[0]
            g_bot = int(g_ys.max()) if len(g_ys) else y_bot
            floor_band = np.zeros_like(fg_mask)
            floor_band[max(0, g_bot - (24 if is_quadrant else 48)) :, :] = True
            guide_floor_keep = ndimage.binary_dilation(
                guide_protect, structure=np.ones((3, 3), dtype=bool), iterations=(6 if is_quadrant else 12)
            )
            fg_mask &= ~(floor_band & (~guide_floor_keep))
            chroma_span = np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])
            cool_shadow = (b > r + 6) & (b >= g - 6) & (lum_tmp < 205.0)
            neutral_shadow = (chroma_span < 36) & (lum_tmp < 195.0)
            purple_shadow = foot_band & fg_mask & (~guide_protect) & (cool_shadow | neutral_shadow)
        else:
            purple_shadow = foot_band & fg_mask & (b > g + 22) & (b > r + 10) & (lum_tmp < 85.0)
        if np.any(purple_shadow):
            fg_dil = ndimage.binary_dilation(~fg_mask, iterations=3)
            if np.any(purple_shadow & fg_dil):
                s_lab, _ = ndimage.label(purple_shadow)
                touch = set(int(v) for v in np.unique(s_lab[purple_shadow & fg_dil]) if v > 0)
                for lbl in touch:
                    fg_mask[s_lab == lbl] = False

    # Seal tiny unintended pinholes or all enclosed holes (for portraits)
    filled = ndimage.binary_fill_holes(fg_mask)
    holes = filled & (~fg_mask)
    if np.any(holes):
        if fill_interior_holes:
            fg_mask = filled
        else:
            h_lab, n_h = ndimage.label(holes)
            cap = 16 if is_quadrant else max_auto_fill_hole_px
            for idx in range(1, n_h + 1):
                comp = h_lab == idx
                if int(np.sum(comp)) <= cap:
                    fg_mask |= comp

    # Keep primary body + meaningful detached elements (>= 0.6% of largest component)
    lbl, n_c = ndimage.label(fg_mask)
    if n_c > 1:
        sizes = ndimage.sum(fg_mask, lbl, range(1, n_c + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        min_comp = 18.0 if is_quadrant else 36.0
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(min_comp, max_sz * 0.006):
                keep |= (lbl == idx)
        fg_mask = keep

    # Despill perimeter rim around outer silhouette and punched interior holes
    rim_iter = 4 if is_quadrant else 6
    inner_clean = ndimage.binary_erosion(fg_mask, iterations=rim_iter) & (mag_dom <= 10)
    rim = fg_mask & (~inner_clean)
    rim_bad = rim & (mag_dom > 12) & (r > 105) & (b > 105)
    if np.any(rim_bad) and np.any(inner_clean):
        _, idx = ndimage.distance_transform_edt(~inner_clean, return_indices=True)
        ry, rx = np.where(rim_bad)
        rgb[ry, rx] = rgb[idx[0, ry, rx], idx[1, ry, rx]]

    return rgb, fg_mask


def downscale_frame_anchored(
    rgb_clean: np.ndarray,
    fg_mask: np.ndarray,
    px_per_render_px: float,
    frame_meta: dict,
    idle_fg_y1: float,
    idle_foot_cx: float,
    palette_rgb: np.ndarray,
    is_quadrant: bool = False,
    idle_l_mean: Optional[float] = None,
    idle_l_std: Optional[float] = None,
) -> np.ndarray:
    """
    Downscale extracted animation frame at constant world-space scale (NEVER
    re-normalizing independently by frame cw/ch), then anchor to the Blender
    ground plane and Unity Idle f0 horizontal foot center:
      - Vertical: if grounded (guide_fg_y1 >= ground_y_px - 10 and not lifted
        well above idle_fg_y1), anchor lowest opaque sprite pixel at y=125; if
        airborne (e.g. frog hop or rat pounce), lift relative to ground plane so
        the sprite rises cleanly into the air. Clamp [1..126] if tall weapons
        approach top.
      - Horizontal: align ground_x_px -> idle_foot_cx, clamping only minimum
        pixels needed [1..127 - fit_w] when extended weapons/tongues reach edges.
    """
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Empty foreground mask after chroma extraction")

    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    crop_rgb = rgb_clean[y0:y1, x0:x1]
    crop_mask = fg_mask[y0:y1, x0:x1]
    ch, cw = crop_mask.shape

    coord_mult = 2.0 if is_quadrant else 1.0
    eff_scale = px_per_render_px * coord_mult

    fit_w = max(4, int(round(cw * eff_scale)))
    fit_h = max(4, int(round(ch * eff_scale)))
    # Guard only if an extreme Diffusion hallucination exceeds 126x126 canvas bounds
    if fit_w > 126 or fit_h > 126:
        clamp_s = min(126.0 / fit_w, 126.0 / fit_h)
        eff_scale *= clamp_s
        fit_w = max(4, int(round(cw * eff_scale)))
        fit_h = max(4, int(round(ch * eff_scale)))

    extended_rgb = pp._extend_fg_colors_into_bg(crop_rgb, crop_mask)
    down_rgb = pp._pixeloe_downscale_rgb(extended_rgb, fit_w=fit_w, fit_h=fit_h, thickness=1)

    mask_f = crop_mask.astype(np.float32)
    mask_sm = cv2.resize(mask_f, (fit_w, fit_h), interpolation=cv2.INTER_AREA)
    alpha_bin = mask_sm >= 0.25

    # Remove tiny 1-3px isolated dust on final 128x128 pixel grid
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(4.0, max_sz * 0.004):
                alpha_bin[labeled == idx] = False

    if not np.any(alpha_bin):
        raise RuntimeError("Downscaled alpha mask is empty")

    ys_a, xs_a = np.where(alpha_bin)
    a_y0, a_y1 = int(ys_a.min()), int(ys_a.max())

    # Horizontal anchor: align Blender ground_x_px -> Unity idle_foot_cx
    x0_1024 = float(x0) * coord_mult
    ground_x_px = float(frame_meta["ground_x_px"])
    ground_x_in_down = (ground_x_px - x0_1024) * (eff_scale / coord_mult)
    ox = int(round(idle_foot_cx - ground_x_in_down))
    max_ox = max(1, 127 - fit_w)
    ox = max(1, min(max_ox, ox))

    # Vertical anchor: grounded -> lowest opaque pixel at y=125; airborne -> lift relative to ground
    guide_fg_y1 = float(frame_meta["fg_bbox"][3])
    ground_y_px = float(frame_meta["ground_y_px"])
    is_airborne = (guide_fg_y1 < ground_y_px - 10.0) or (guide_fg_y1 < float(idle_fg_y1) - 35.0)
    if not is_airborne:
        target_bot = 125
    else:
        ref_ground = min(ground_y_px, float(idle_fg_y1)) if guide_fg_y1 < ground_y_px - 10.0 else float(idle_fg_y1)
        lift_px = max(0, int(round((ref_ground - guide_fg_y1) * px_per_render_px)))
        target_bot = max(1 + a_y1, min(125, 125 - lift_px))

    oy = target_bot - a_y1
    # Clamp [1..126] if tall overhead weapons ever reach top
    if oy + a_y0 < 1:
        oy = 1 - a_y0
    if oy + a_y1 > 126:
        oy = 126 - a_y1

    canvas = np.zeros((128, 128, 4), dtype=np.uint8)
    y_dst0 = max(0, oy)
    y_dst1 = min(128, oy + fit_h)
    x_dst0 = max(0, ox)
    x_dst1 = min(128, ox + fit_w)
    y_src0 = y_dst0 - oy
    y_src1 = y_src0 + (y_dst1 - y_dst0)
    x_src0 = x_dst0 - ox
    x_src1 = x_src0 + (x_dst1 - x_dst0)

    sub = canvas[y_dst0:y_dst1, x_dst0:x_dst1]
    sub_mask = alpha_bin[y_src0:y_src1, x_src0:x_src1]
    sub[..., :3] = down_rgb[y_src0:y_src1, x_src0:x_src1]
    sub[..., 3] = np.where(sub_mask, 255, 0).astype(np.uint8)
    sub[~sub_mask, :3] = 0

    # Smooth CIELAB L distribution match toward Idle f0 (preserving dark ink L < 14)
    if idle_l_mean is not None and idle_l_std is not None:
        fg_c = canvas[..., 3] == 255
        if np.any(fg_c):
            lab_c = pp.rgb_to_lab_float(canvas[..., :3])
            l_fg = lab_c[fg_c, 0]
            m = float(np.mean(l_fg))
            s = float(np.std(l_fg))
            gain = float(np.clip(idle_l_std / max(s, 4.0), 0.75, 1.35))
            l_matched = (l_fg - m) * gain + idle_l_mean
            l_target = np.clip(0.75 * l_matched + 0.25 * l_fg, 0.0, 100.0)
            lab_c[fg_c, 0] = np.where(l_fg < 14.0, l_fg, l_target)
            canvas[..., :3] = pp.lab_float_to_rgb_u8(lab_c)

    quant: Optional[np.ndarray] = None
    for d_str in (0.05, 0.09, 0.14, 0.20, 0.26):
        cand = pp.apply_sunlit_grade_and_quantize(
            canvas,
            palette_rgb,
            dither_strength=d_str,
            grade_strength=0.14,
            shadow_lift=4.5,
            vibrance_boost=1.08,
            preserve_alpha=False,
        )
        cand = _despill_final_128(cand)
        quant = cand
        if pp.compute_flat_blob_max_square(cand) < 10:
            break
    assert quant is not None
    return quant


def downscale_to_sprite(
    rgb_clean: np.ndarray,
    fg_mask: np.ndarray,
    canvas_wh: Tuple[int, int],
    target_height_px: int,
    max_width_px: int,
    palette_rgb: np.ndarray,
    bottom_margin_px: int = 2,
    alpha_cov_thresh: float = 0.25,
    fill_small_holes_post: bool = True,
    dither_strength: float = 0.05,
) -> np.ndarray:
    """Standard PixelOE BottomCenter sprite downscaler for NPCs and portrait busts."""
    cw_out, ch_out = canvas_wh
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Empty foreground mask after extraction")

    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    crop_rgb = rgb_clean[y0:y1, x0:x1]
    crop_mask = fg_mask[y0:y1, x0:x1]
    ch, cw = crop_mask.shape

    scale = min(max_width_px / cw, target_height_px / ch)
    fit_w = max(8, int(round(cw * scale)))
    fit_h = max(8, int(round(ch * scale)))

    extended_rgb = pp._extend_fg_colors_into_bg(crop_rgb, crop_mask)
    down_rgb = pp._pixeloe_downscale_rgb(extended_rgb, fit_w=fit_w, fit_h=fit_h, thickness=1)

    mask_f = crop_mask.astype(np.float32)
    mask_sm = cv2.resize(mask_f, (fit_w, fit_h), interpolation=cv2.INTER_AREA)
    alpha_bin = mask_sm >= alpha_cov_thresh

    if fill_small_holes_post:
        filled_a = ndimage.binary_fill_holes(alpha_bin)
        holes_a = filled_a & (~alpha_bin)
        if np.any(holes_a):
            hl, nh = ndimage.label(holes_a)
            for idx in range(1, nh + 1):
                if int(np.sum(hl == idx)) <= 4:
                    alpha_bin |= (hl == idx)

    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(4.0, max_sz * 0.004):
                alpha_bin[labeled == idx] = False

    canvas = np.zeros((ch_out, cw_out, 4), dtype=np.uint8)
    oy = max(1, (ch_out - bottom_margin_px) - fit_h)
    ox = (cw_out - fit_w) // 2
    y_end = min(ch_out, oy + fit_h)
    x_end = min(cw_out, ox + fit_w)
    sub_h = y_end - oy
    sub_w = x_end - ox

    sub = canvas[oy:y_end, ox:x_end]
    sub[..., :3] = down_rgb[:sub_h, :sub_w]
    sub[..., 3] = np.where(alpha_bin[:sub_h, :sub_w], 255, 0).astype(np.uint8)
    sub[~alpha_bin[:sub_h, :sub_w], :3] = 0

    ys_f = np.where(canvas[..., 3] == 255)[0]
    if len(ys_f) > 0:
        target_bot = ch_out - 1 - bottom_margin_px
        dy = target_bot - int(ys_f.max())
        if dy != 0:
            shifted = np.zeros_like(canvas)
            if dy > 0:
                shifted[dy:] = canvas[: ch_out - dy]
            else:
                s = -dy
                shifted[: ch_out - s] = canvas[s:]
            canvas = shifted

    quant: Optional[np.ndarray] = None
    d_tries = []
    for dt in (dither_strength, 0.10, 0.18, 0.28, 0.40):
        if dt not in d_tries:
            d_tries.append(dt)
    for d_str in d_tries:
        cand = pp.apply_sunlit_grade_and_quantize(
            canvas,
            palette_rgb,
            dither_strength=d_str,
            grade_strength=0.14,
            shadow_lift=4.5,
            vibrance_boost=1.08,
            preserve_alpha=False,
        )
        cand = _despill_final_128(cand)
        quant = cand
        if pp.compute_flat_blob_max_square(cand) < 10:
            break
    assert quant is not None
    return quant


def _despill_final_128(quant: np.ndarray) -> np.ndarray:
    op_q = quant[..., 3] == 255
    rq = quant[..., 0].astype(np.int16)
    gq = quant[..., 1].astype(np.int16)
    bq = quant[..., 2].astype(np.int16)
    bad_mag = op_q & ((np.minimum(rq, bq) - gq) > 40) & (rq > 120) & (bq > 120)
    if np.any(bad_mag) and np.any(op_q & (~bad_mag)):
        clean_fg = op_q & (~bad_mag)
        _, idx_q = ndimage.distance_transform_edt(~clean_fg, return_indices=True)
        my, mx = np.where(bad_mag)
        quant[my, mx, :3] = quant[idx_q[0, my, mx], idx_q[1, my, mx], :3]
    return quant


# ---------------------------------------------------------------------------
# Idle Synthesis & Portrait Frame Helpers (from combat_roster / run_pack1)
# ---------------------------------------------------------------------------

def synthesize_unit_idle_frames(
    f0_rgba: np.ndarray,
    archetype: str,
    unit_palette: np.ndarray,
    facing: str = "East",
) -> List[np.ndarray]:
    """Synthesize 4 zero-flicker 128x128 idle frames from NPC f0 with anchored feet (y=125)."""
    h, w, _ = f0_rgba.shape
    fg = f0_rgba[..., 3] == 255
    ys = np.where(fg)[0]
    if len(ys) == 0:
        return [f0_rgba.copy() for _ in range(4)]
    y_top, y_bot = int(ys.min()), int(ys.max())
    span_h = max(8, y_bot - y_top + 1)

    torso_split = y_top + int(span_h * 0.62)
    frames = []
    for fidx in range(4):
        if fidx == 0:
            frames.append(f0_rgba.copy())
            continue
        cur = f0_rgba.copy()
        if fidx in (1, 2) and torso_split > 2:
            cur[: torso_split - 1] = f0_rgba[1:torso_split]
            cur[torso_split - 1] = f0_rgba[torso_split - 1]
        if fidx in (2, 3):
            b0 = y_top + int(span_h * 0.45)
            b1 = y_top + int(span_h * 0.84)
            if facing == "East":
                row_slice = cur[b0:b1, :-1].copy()
                cur[b0:b1, 1:] = np.where(
                    (np.arange(w)[None, 1:, None] < w * 0.44) & (row_slice[..., 3:4] == 255),
                    row_slice,
                    cur[b0:b1, 1:],
                )
            else:
                row_slice = cur[b0:b1, 1:].copy()
                cur[b0:b1, :-1] = np.where(
                    (np.arange(w)[None, :-1, None] > w * 0.56) & (row_slice[..., 3:4] == 255),
                    row_slice,
                    cur[b0:b1, :-1],
                )
        if archetype == "magic_biped" and fidx == 2:
            op = cur[..., 3] == 255
            cyan = op & (cur[..., 2] > cur[..., 0] + 40) & (cur[..., 1] > 140)
            cur[cyan, 0] = np.minimum(255, cur[cyan, 0].astype(np.int32) + 45).astype(np.uint8)
            cur[cyan, 1] = np.minimum(255, cur[cyan, 1].astype(np.int32) + 25).astype(np.uint8)
            cur[cyan, 2] = np.minimum(255, cur[cyan, 2].astype(np.int32) + 15).astype(np.uint8)
        frames.append(cur)

    if archetype == "magic_biped":
        frames[2] = pp.apply_sunlit_grade_and_quantize(
            frames[2],
            unit_palette,
            dither_strength=0.0,
            grade_strength=0.0,
            shadow_lift=0.0,
            vibrance_boost=1.0,
            preserve_alpha=False,
        )
    return frames


def build_portrait_bust_ref(sheet_path: Path, out_ref_path: Path) -> Path:
    """
    Extract tight shoulders-up headshot crop (top 38% of Frame 0) onto solid #FF00FF
    chroma key and create both crisp ref and interior-blurred init for ComfyUI.
    """
    strip = np.array(Image.open(sheet_path).convert("RGBA"))
    f0 = strip[:, :128, :]
    fg = f0[..., 3] == 255
    ys, xs = np.where(fg)
    y0, y1 = int(ys.min()), int(ys.max())
    x0, x1 = int(xs.min()), int(xs.max())
    h_span = max(20, y1 - y0 + 1)

    bust_y1 = min(128, y0 + max(24, int(h_span * 0.38)))
    top_xs = np.where(fg[y0 : min(128, y0 + max(18, int(h_span * 0.30))), :])[1]
    cx = int(np.median(top_xs)) if len(top_xs) else (x0 + x1) // 2
    half_box = max(18, int(h_span * 0.24))
    bx0 = max(0, cx - half_box)
    bx1 = min(128, cx + half_box)
    by0 = max(0, y0 - 2)
    crop = f0[by0:bust_y1, bx0:bx1]

    ch, cw, _ = crop.shape
    side = max(ch, cw) + 6
    sq = np.full((side, side, 3), (255, 0, 255), dtype=np.uint8)
    sq_fg = np.zeros((side, side), dtype=bool)
    oy = side - ch
    ox = (side - cw) // 2
    y_e = min(side, oy + ch)
    x_e = min(side, ox + cw)
    sub_c = crop[: y_e - oy, : x_e - ox]
    m = sub_c[..., 3] == 255
    sq[oy:y_e, ox:x_e][m] = sub_c[..., :3][m]
    sq_fg[oy:y_e, ox:x_e] = m

    ref_1024 = cv2.resize(sq, (1024, 1024), interpolation=cv2.INTER_NEAREST)
    fg_1024 = cv2.resize(sq_fg.astype(np.uint8) * 255, (1024, 1024), interpolation=cv2.INTER_NEAREST) > 128

    fg_f = fg_1024.astype(np.float32)
    denom = cv2.GaussianBlur(fg_f, (0, 0), sigmaX=5.5, sigmaY=5.5)
    blurred = np.empty_like(ref_1024, dtype=np.float32)
    for c in range(3):
        num = cv2.GaussianBlur(ref_1024[..., c].astype(np.float32) * fg_f, (0, 0), sigmaX=5.5, sigmaY=5.5)
        blurred[..., c] = np.where(denom > 1e-4, num / np.maximum(denom, 1e-4), 0.0)
    init_1024 = np.full_like(ref_1024, (255, 0, 255), dtype=np.uint8)
    init_1024[fg_1024] = np.clip(np.round(blurred[fg_1024]), 0, 255).astype(np.uint8)

    out_ref_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(ref_1024, "RGB").save(out_ref_path, "PNG")
    init_path = out_ref_path.with_name(out_ref_path.stem + "_init.png")
    Image.fromarray(init_1024, "RGB").save(init_path, "PNG")
    return init_path


def make_heraldic_portrait_backdrop() -> np.ndarray:
    """Deterministic 112x112 dithered slate-cobalt vignette in palette_soc_sunlit_128.json."""
    c_hi = pp.hex_to_rgb("#3E4C73")
    c_md = pp.hex_to_rgb("#2F3A59")
    c_dk = pp.hex_to_rgb("#232B42")
    c_dp = pp.hex_to_rgb("#1C2033")
    bayer = np.array([[0, 2], [3, 1]], dtype=np.float32) / 4.0 - 0.375
    bg = np.zeros((112, 112, 3), dtype=np.uint8)
    for y in range(112):
        for x in range(112):
            d = math.hypot((x - 56) / 56.0, (y - 48) / 56.0) + bayer[y % 2, x % 2] * 0.25
            stip = (x * 3 + y * 5) % 7 == 0
            if d < 0.48:
                bg[y, x] = c_md if stip else c_hi
            elif d < 0.84:
                bg[y, x] = c_dk if stip else c_md
            elif d < 1.12:
                bg[y, x] = c_dp if stip else c_dk
            else:
                bg[y, x] = c_dp if ((x + y * 2) % 5 == 0) else c_dk
    return bg


def apply_bronze_gold_portrait_frame(inner_112: np.ndarray) -> np.ndarray:
    """Composite 112x112 bust inside ornate 128x128 pixel-authored bronze/gold frame."""
    c = np.zeros((128, 128, 4), dtype=np.uint8)

    def hx(s: str) -> Tuple[int, int, int, int]:
        r, g, b = pp.hex_to_rgb(s)
        return (r, g, b, 255)

    K = hx("#11131F")
    G_HI = hx("#FFF3B0")
    G_LT = hx("#FFC952")
    G_MD = hx("#ECA938")
    B_MD = hx("#CE8629")
    B_DK = hx("#A6641F")
    B_SH = hx("#7D4818")
    B_DP = hx("#593212")

    for y in range(128):
        for x in range(128):
            if (x + y < 2) or ((127 - x) + y < 2) or (x + (127 - y) < 2) or ((127 - x) + (127 - y) < 2):
                continue
            d_edge = min(x, y, 127 - x, 127 - y)
            if d_edge == 0 or (x + y == 2) or ((127 - x) + y == 2) or (x + (127 - y) == 2) or ((127 - x) + (127 - y) == 2):
                c[y, x] = K
            elif d_edge == 7:
                c[y, x] = K
            elif 8 <= x <= 119 and 8 <= y <= 119:
                continue
            else:
                is_top_left = (x < 120 and y < 8) or (y < 120 and x < 8)
                if d_edge == 1:
                    c[y, x] = G_LT if is_top_left else B_SH
                elif d_edge == 2:
                    c[y, x] = G_HI if (x < 64 or y < 64) else G_MD
                elif d_edge in (3, 4):
                    c[y, x] = G_MD if is_top_left else B_MD
                elif d_edge == 5:
                    c[y, x] = B_MD if is_top_left else B_DK
                else:
                    c[y, x] = B_DK if is_top_left else B_DP

    c[8:120, 8:120, :3] = inner_112[..., :3]
    c[8:120, 8:120, 3] = 255

    for cx0, cy0 in ((1, 1), (117, 1), (1, 117), (117, 117)):
        for dy in range(10):
            for dx in range(10):
                px, py = cx0 + dx, cy0 + dy
                if c[py, px, 3] == 0:
                    continue
                if dx in (0, 9) or dy in (0, 9):
                    c[py, px] = K
                elif 3 <= dx <= 6 and 3 <= dy <= 6:
                    c[py, px] = G_HI if (dx <= 4 and dy <= 4) else G_LT
                elif dx in (2, 7) or dy in (2, 7):
                    c[py, px] = B_SH
                else:
                    c[py, px] = G_MD
    return c


# ---------------------------------------------------------------------------
# Automated QA Gates
# ---------------------------------------------------------------------------

def _count_magenta_bleed(rgba: np.ndarray) -> int:
    op = rgba[..., 3] == 255
    if not np.any(op):
        return 0
    r = rgba[op, 0].astype(np.int16)
    g = rgba[op, 1].astype(np.int16)
    b = rgba[op, 2].astype(np.int16)
    return int(np.sum(((np.minimum(r, b) - g) > 42) & (r > 125) & (b > 125)))


def _compute_motion_metrics(frame_a: np.ndarray, frame_b: np.ndarray) -> Tuple[float, float]:
    fg_a = frame_a[..., 3] == 255
    fg_b = frame_b[..., 3] == 255
    union = fg_a | fg_b
    n_union = int(np.sum(union))
    if n_union == 0:
        return 1.0, 0.0
    inter = int(np.sum(fg_a & fg_b))
    iou = float(inter) / float(n_union)
    diff_px = int(np.sum(np.any(frame_a != frame_b, axis=-1) & union))
    diff_pct = float(diff_px) / float(n_union)
    return round(iou, 4), round(diff_pct, 4)


def _color_hist_cosine_sim(frame_rgba: np.ndarray, ref_rgba: np.ndarray, bins: int = 8) -> float:
    """Compute 3D RGB histogram cosine similarity between opaque foreground pixels of two sprites."""
    def _hist(im: np.ndarray) -> np.ndarray:
        op = im[..., 3] >= 128
        if not np.any(op):
            return np.zeros(bins ** 3, dtype=np.float64)
        rgb = im[op, :3].astype(np.int32)
        b_idx = (rgb * bins) // 256
        idx = b_idx[:, 0] * (bins * bins) + b_idx[:, 1] * bins + b_idx[:, 2]
        vec = np.bincount(idx, minlength=bins ** 3).astype(np.float64)
        n = float(np.linalg.norm(vec))
        return vec / n if n > 0.0 else vec

    return float(np.dot(_hist(frame_rgba), _hist(ref_rgba)))


def check_single_frame_qa(
    fr_rgba: np.ndarray,
    nf_rgba: np.ndarray,
    palette_rgb: np.ndarray,
    idle_f0_rgba: np.ndarray,
    idle_fg_px: int,
    frame_idx: int,
    min_hist_sim: float = 0.56,
    min_fg_ratio: float = 0.60,
) -> Tuple[bool, dict, str]:
    fst = pp.validate_outputs(fr_rgba, nf_rgba, palette_rgb, is_tile=False, asset_kind="unit")
    f_op = int(fst["opaque_pixels"])
    ratio = round(float(f_op) / float(max(1, idle_fg_px)), 3)
    f_mag = _count_magenta_bleed(fr_rgba)
    hist_sim = round(_color_hist_cosine_sim(fr_rgba, idle_f0_rgba, bins=8), 4)

    fst["fg_ratio"] = ratio
    fst["magenta_bleed_pixels"] = f_mag
    fst["hist_cosine_sim"] = hist_sim

    if fst["off_palette_colors"] != 0:
        return False, fst, f"f{frame_idx} off_palette_colors={fst['off_palette_colors']}"
    if not fst["alpha_is_1bit"]:
        return False, fst, f"f{frame_idx} alpha not 1-bit"
    if f_mag > 0:
        return False, fst, f"f{frame_idx} magenta_bleed_pixels={f_mag}"
    if fst["flat_blob_max_square"] >= 10:
        return False, fst, f"f{frame_idx} flat_blob_max_square={fst['flat_blob_max_square']} >= 10"
    if fst["unique_palette_colors"] < 14:
        return False, fst, f"f{frame_idx} unique_palette_colors={fst['unique_palette_colors']} < 14"
    if not (0.94 <= fst["normal_unit_mag_mean"] <= 1.06):
        return False, fst, f"f{frame_idx} normal_unit_mag_mean={fst['normal_unit_mag_mean']}"
    if not fst["normal_bg_neutral_ok"]:
        return False, fst, f"f{frame_idx} normal_bg_neutral_ok=False"
    if not (min_fg_ratio <= ratio <= 2.10):
        return False, fst, f"f{frame_idx} fg_px_ratio={ratio:.2f} outside [{min_fg_ratio:.2f}..2.10]"
    if hist_sim < min_hist_sim:
        return False, fst, f"f{frame_idx} hist_cosine_sim={hist_sim:.3f} < {min_hist_sim:.2f}"

    return True, fst, f"f{frame_idx} ok (c={fst['unique_palette_colors']} r={ratio:.2f} sim={hist_sim:.2f})"


def check_anim_qa(
    frames: List[np.ndarray],
    norm_frames: List[np.ndarray],
    strip_rgba: np.ndarray,
    strip_norm: np.ndarray,
    palette_rgb: np.ndarray,
    anim: str,
    idle_f0_rgba: np.ndarray,
    idle_fg_px: int,
    min_fg_ratio: float = 0.60,
) -> Tuple[bool, dict, str]:
    strip_stats = pp.validate_outputs(strip_rgba, strip_norm, palette_rgb, is_tile=False, asset_kind="unit")
    mag_strip = _count_magenta_bleed(strip_rgba)
    strip_stats["magenta_bleed_pixels"] = mag_strip

    frame_ratios: List[float] = []
    frame_colors: List[int] = []
    frame_hist_sims: List[float] = []
    min_sim = 0.48 if anim == "attack" else 0.54
    for idx, (fr, nf) in enumerate(zip(frames, norm_frames)):
        f_ok, fst, f_msg = check_single_frame_qa(
            fr,
            nf,
            palette_rgb,
            idle_f0_rgba,
            idle_fg_px,
            idx,
            min_hist_sim=min_sim,
            min_fg_ratio=min_fg_ratio,
        )
        frame_ratios.append(float(fst["fg_ratio"]))
        frame_colors.append(int(fst["unique_palette_colors"]))
        frame_hist_sims.append(float(fst["hist_cosine_sim"]))
        if not f_ok:
            strip_stats["frame_fg_ratios"] = frame_ratios
            strip_stats["frame_colors"] = frame_colors
            strip_stats["frame_hist_sims"] = frame_hist_sims
            return False, strip_stats, f_msg

    if strip_stats["off_palette_colors"] != 0:
        return False, strip_stats, f"strip off_palette_colors={strip_stats['off_palette_colors']}"
    if not strip_stats["alpha_is_1bit"]:
        return False, strip_stats, "strip alpha not 1-bit"
    if mag_strip > 0:
        return False, strip_stats, f"strip magenta_bleed_pixels={mag_strip}"
    if strip_stats["flat_blob_max_square"] >= 10:
        return False, strip_stats, f"strip flat_blob_max_square={strip_stats['flat_blob_max_square']} >= 10"
    if strip_stats["unique_palette_colors"] < 14:
        return False, strip_stats, f"strip unique_palette_colors={strip_stats['unique_palette_colors']} < 14"
    if not (0.94 <= strip_stats["normal_unit_mag_mean"] <= 1.06):
        return False, strip_stats, f"strip normal_unit_mag_mean={strip_stats['normal_unit_mag_mean']}"
    if not strip_stats["normal_bg_neutral_ok"]:
        return False, strip_stats, "strip normal_bg_neutral_ok=False"

    # Inter-frame motion check: walk f0 vs f2; attack f0 vs f1
    pair_a, pair_b = (0, 2) if anim == "walk" else (0, 1)
    motion_iou, motion_diff = _compute_motion_metrics(frames[pair_a], frames[pair_b])
    strip_stats["frame_fg_ratios"] = frame_ratios
    strip_stats["frame_colors"] = frame_colors
    strip_stats["frame_hist_sims"] = frame_hist_sims
    strip_stats["motion_pair"] = f"f{pair_a}_vs_f{pair_b}"
    strip_stats["motion_iou"] = motion_iou
    strip_stats["motion_diff_pct"] = motion_diff

    if not (motion_iou < 0.92 or motion_diff > 0.10):
        return False, strip_stats, (
            f"motion check failed ({strip_stats['motion_pair']}): iou={motion_iou:.3f} >= 0.92 and diff={motion_diff:.3f} <= 0.10"
        )

    msg = (
        f"colors={strip_stats['unique_palette_colors']} ratios={frame_ratios} sims={[round(s,2) for s in frame_hist_sims]} "
        f"max_sq={strip_stats['flat_blob_max_square']} {strip_stats['motion_pair']}:iou={motion_iou:.2f}/diff={motion_diff:.2f}"
    )
    return True, strip_stats, msg


def check_npc_qa(
    f0_rgba: np.ndarray,
    strip_rgba: np.ndarray,
    strip_norm: np.ndarray,
    palette_rgb: np.ndarray,
    target_height_px: int,
) -> Tuple[bool, dict, str]:
    stats = pp.validate_outputs(strip_rgba, strip_norm, palette_rgb, is_tile=False, asset_kind="unit")
    mag_bleed = _count_magenta_bleed(strip_rgba)
    stats["magenta_bleed_pixels"] = mag_bleed

    op0 = f0_rgba[..., 3] == 255
    n_op0 = int(np.sum(op0))
    ys, xs = np.where(op0)
    bh = int(ys.max() - ys.min() + 1) if len(ys) else 0
    bw = int(xs.max() - xs.min() + 1) if len(xs) else 0
    stats["pixel_width"] = bw
    stats["pixel_height"] = bh
    stats["f0_opaque_pixels"] = n_op0

    if n_op0 < 950:
        return False, stats, f"f0 opaque_px={n_op0} < 950"
    if stats["off_palette_colors"] != 0:
        return False, stats, f"off_palette_colors={stats['off_palette_colors']}"
    if not stats["alpha_is_1bit"]:
        return False, stats, "alpha not 1-bit"
    if mag_bleed > 0:
        return False, stats, f"magenta_bleed_pixels={mag_bleed}"
    if stats["flat_blob_max_square"] >= 10:
        return False, stats, f"flat_blob_max_square={stats['flat_blob_max_square']} >= 10"
    if stats["unique_palette_colors"] < 14:
        return False, stats, f"unique_palette_colors={stats['unique_palette_colors']} < 14"
    if not (0.94 <= stats["normal_unit_mag_mean"] <= 1.06):
        return False, stats, f"normal_unit_mag_mean={stats['normal_unit_mag_mean']}"
    if not stats["normal_bg_neutral_ok"]:
        return False, stats, "normal_bg_neutral_ok=False"
    if abs(bh - target_height_px) > 10:
        return False, stats, f"height={bh}px outside {target_height_px}+/-10"
    if int(ys.max()) < 123 or int(ys.max()) > 126:
        return False, stats, f"bottom anchor y={int(ys.max())} not near 125"

    msg = (
        f"size={bw}x{bh}px colors={stats['unique_palette_colors']} op0={n_op0} "
        f"max_sq={stats['flat_blob_max_square']} mag=0"
    )
    return True, stats, msg


def check_portrait_qa(
    portrait_rgba: np.ndarray,
    portrait_norm: np.ndarray,
    palette_rgb: np.ndarray,
) -> Tuple[bool, dict, str]:
    stats = pp.validate_outputs(portrait_rgba, portrait_norm, palette_rgb, is_tile=False, asset_kind="portrait")
    mag_bleed = _count_magenta_bleed(portrait_rgba)
    stats["magenta_bleed_pixels"] = mag_bleed
    n_op = int(stats["opaque_pixels"])
    mean_b = float(np.mean(portrait_norm[portrait_rgba[..., 3] == 255, 2])) if n_op > 0 else 255.0
    stats["normal_mean_b"] = round(mean_b, 2)

    if n_op < 15000:
        return False, stats, f"opaque_pixels={n_op} < 15000"
    if stats["off_palette_colors"] != 0:
        return False, stats, f"off_palette_colors={stats['off_palette_colors']}"
    if not stats["alpha_is_1bit"]:
        return False, stats, "alpha not 1-bit"
    if mag_bleed > 0:
        return False, stats, f"magenta_bleed_pixels={mag_bleed}"
    if stats["flat_blob_max_square"] >= 10:
        return False, stats, f"flat_blob_max_square={stats['flat_blob_max_square']} >= 10"
    if stats["unique_palette_colors"] < 18:
        return False, stats, f"unique_palette_colors={stats['unique_palette_colors']} < 18"
    if not (0.94 <= stats["normal_unit_mag_mean"] <= 1.06):
        return False, stats, f"normal_unit_mag_mean={stats['normal_unit_mag_mean']}"
    if not stats["normal_bg_neutral_ok"]:
        return False, stats, "normal_bg_neutral_ok=False"

    msg = (
        f"op={n_op} colors={stats['unique_palette_colors']} "
        f"max_sq={stats['flat_blob_max_square']} mag=0 nB={mean_b:.0f}"
    )
    return True, stats, msg


# ---------------------------------------------------------------------------
# Unit Walk/Attack & NPC/Portrait Generation Runners
# ---------------------------------------------------------------------------

def generate_unit_animation(
    spec: UnitAnimSpec,
    anim: str,
    unit_meta: dict,
    unit_palette: np.ndarray,
    state: PackState,
    mode: str = "per_frame",
    weight_type: str = "style transfer",
    force: bool = False,
    out_dir: Optional[Path] = None,
    file_stem_suffix: str = "",
    state_key_override: Optional[str] = None,
) -> dict:
    assert anim in ("walk", "attack")
    target_dir = out_dir or OUTPUT_DIR
    target_dir.mkdir(parents=True, exist_ok=True)
    stem = f"{spec.unit_id}_{anim}{file_stem_suffix}_sheet"
    out_albedo = target_dir / f"{stem}.png"
    out_normal = target_dir / f"{stem}_n.png"
    asset_key = state_key_override or f"{spec.unit_id}_{anim}"

    if not force and state.is_done(asset_key, [out_albedo, out_normal]):
        print(f"[RESUME] Skipping {asset_key} (already QA-passed)", flush=True)
        return state.data["assets"][asset_key]

    sheet_path = Path(unit_meta["sheet_path"])
    f0_unity, idle_foot_cx, idle_fg_px, idle_l_mean, idle_l_std = inspect_unity_idle_f0(spec.unit_id, sheet_path)
    ipref_path = build_f0_ipref(spec.unit_id, f0_unity)
    ipref_remote = upload_image(ipref_path, f"was_ipref_{spec.unit_id}.png")

    px_per_render_px = float(unit_meta["px_per_render_px"])
    idle_fg_y1 = float(unit_meta["idle_ref"]["fg_bbox"][3])
    anim_meta = unit_meta[anim]
    frame_metas = anim_meta["frames"]

    # Pre-upload guides once before seed attempts
    if mode == "grid_2x2":
        grid_meta = anim_meta["grid"]
        g_chroma_p = Path(grid_meta["grid_chroma_init"])
        g_depth_p = Path(grid_meta["grid_depth"])
        g_canny_p = Path(grid_meta["grid_canny"])
        soft_init_p, soft_depth_p = prepare_soft_chroma_and_depth(
            g_chroma_p, g_depth_p, f"{spec.unit_id}_{anim}_grid", is_grid=True
        )
        grid_init_rem = upload_image(soft_init_p, f"was_ginit_{spec.unit_id}_{anim}.png")
        grid_depth_rem = upload_image(soft_depth_p, f"was_gdepth_{spec.unit_id}_{anim}.png")
        grid_canny_rem = upload_image(g_canny_p, f"was_gcanny_{spec.unit_id}_{anim}.png")
    else:
        frame_remotes: List[Tuple[str, str, str, Path]] = []
        for idx, fm in enumerate(frame_metas):
            c_p = Path(fm["chroma_init"])
            d_p = Path(fm["depth"])
            n_p = Path(fm["canny"])
            soft_init_p, soft_depth_p = prepare_soft_chroma_and_depth(
                c_p, d_p, f"{spec.unit_id}_{anim}_f{idx}", is_grid=False
            )
            init_r = upload_image(soft_init_p, f"was_init_{spec.unit_id}_{anim}_f{idx}.png")
            depth_r = upload_image(soft_depth_p, f"was_depth_{spec.unit_id}_{anim}_f{idx}.png")
            canny_r = upload_image(n_p, f"was_canny_{spec.unit_id}_{anim}_f{idx}.png")
            frame_remotes.append((init_r, depth_r, canny_r, c_p))

    base_seeds = spec.walk_seeds if anim == "walk" else spec.attack_seeds
    retry_seeds = (list(base_seeds[:3]) + [base_seeds[0] + 101, base_seeds[0] + 202])[:5]
    n_attempts = len(retry_seeds)
    best_cand: Optional[Tuple[np.ndarray, np.ndarray, dict, str]] = None
    rerolls = 0

    # Per-frame retry tracking (per_frame mode stores passing frames and retries only failing indices)
    frames_best: List[Optional[np.ndarray]] = [None, None, None, None]
    norms_best: List[Optional[np.ndarray]] = [None, None, None, None]
    frame_passed: List[bool] = [False, False, False, False]
    frame_hist_best: List[float] = [-1.0, -1.0, -1.0, -1.0]
    frame_raw_best: List[str] = ["", "", "", ""]
    frames_to_run: List[int] = [0, 1, 2, 3]
    pair_b = 2 if anim == "walk" else 1

    for attempt, seed_base in enumerate(retry_seeds, start=1):
        if attempt == 1:
            den_try, cfg_try = spec.denoise, 6.5
        elif attempt == 2:
            den_try, cfg_try = spec.denoise, 6.8
        elif attempt == 3:
            den_try, cfg_try = max(0.82, spec.denoise - 0.04), 6.2
        elif attempt == 4:
            den_try, cfg_try = spec.denoise, 6.5
        else:
            den_try, cfg_try = max(0.80, spec.denoise - 0.06), 6.3
        t_total = 0.0
        peak_vram = 0
        raw_paths: List[str] = []
        b_used = "sobel"

        try:
            if mode == "grid_2x2":
                wf = build_sdxl_pixel_workflow(
                    prompt=build_unit_grid_prompt(spec, anim),
                    negative=GRID_SPRITE_NEG + spec.extra_neg,
                    seed=seed_base,
                    filename_prefix=f"was_grid_{spec.unit_id}_{anim}",
                    ip_ref_remote=ipref_remote,
                    ip_weight=spec.ip_weight,
                    ip_weight_type=weight_type,
                    ip_start=0.0,
                    ip_end=spec.ip_end,
                    depth_remote=grid_depth_rem,
                    depth_strength=spec.depth_strength,
                    depth_end=spec.depth_end,
                    canny_remote=grid_canny_rem,
                    canny_strength=spec.canny_strength,
                    canny_end=spec.canny_end,
                    init_remote=grid_init_rem,
                    denoise=den_try,
                    cfg=cfg_try,
                    steps=spec.steps,
                )
                img_bytes, dt, pk = run_comfy_prompt(wf)
                t_total += dt
                peak_vram = max(peak_vram, pk)
                raw_p = RAW_DIR / f"{spec.unit_id}_{anim}_grid_{weight_type.replace(' ', '_')}_a{attempt}.png"
                raw_p.parent.mkdir(parents=True, exist_ok=True)
                raw_p.write_bytes(img_bytes)
                raw_paths.append(str(raw_p))

                grid_arr = np.array(Image.open(raw_p).convert("RGBA"), dtype=np.uint8)
                quads = [
                    grid_arr[0:512, 0:512],
                    grid_arr[0:512, 512:1024],
                    grid_arr[512:1024, 0:512],
                    grid_arr[512:1024, 512:1024],
                ]
                frames_out: List[np.ndarray] = []
                norms_out: List[np.ndarray] = []
                for idx, q_arr in enumerate(quads):
                    fm = frame_metas[idx]
                    rgb_clean, fg_mask = extract_clean_magenta_unit(
                        q_arr,
                        guide_chroma_path=Path(fm["chroma_init"]),
                        is_quadrant=True,
                    )
                    fr_128 = downscale_frame_anchored(
                        rgb_clean,
                        fg_mask,
                        px_per_render_px=px_per_render_px,
                        frame_meta=fm,
                        idle_fg_y1=idle_fg_y1,
                        idle_foot_cx=idle_foot_cx,
                        palette_rgb=unit_palette,
                        is_quadrant=True,
                        idle_l_mean=idle_l_mean,
                        idle_l_std=idle_l_std,
                    )
                    nf_128, b_used = pp.generate_normal_map(fr_128, periodic=False, normal_backend="auto")
                    frames_out.append(fr_128)
                    norms_out.append(nf_128)
            else:
                failed_this_attempt: List[int] = []
                for idx in frames_to_run:
                    init_r, depth_r, canny_r, guide_c_p = frame_remotes[idx]
                    wf = build_sdxl_pixel_workflow(
                        prompt=build_unit_anim_prompt(spec, anim, idx),
                        negative=COMMON_SPRITE_NEG + spec.extra_neg,
                        seed=seed_base,  # shared base seed across frames
                        filename_prefix=f"was_{spec.unit_id}_{anim}_f{idx}",
                        ip_ref_remote=ipref_remote,
                        ip_weight=spec.ip_weight,
                        ip_weight_type=weight_type,
                        ip_start=0.0,
                        ip_end=spec.ip_end,
                        depth_remote=depth_r,
                        depth_strength=spec.depth_strength,
                        depth_end=spec.depth_end,
                        canny_remote=canny_r,
                        canny_strength=spec.canny_strength,
                        canny_end=spec.canny_end,
                        init_remote=init_r,
                        denoise=den_try,
                        cfg=cfg_try,
                        steps=spec.steps,
                    )
                    img_bytes, dt, pk = run_comfy_prompt(wf)
                    t_total += dt
                    peak_vram = max(peak_vram, pk)
                    wt_slug = weight_type.replace(" ", "_")
                    raw_p = RAW_DIR / f"{spec.unit_id}_{anim}_f{idx}_{wt_slug}_a{attempt}.png"
                    raw_p.parent.mkdir(parents=True, exist_ok=True)
                    raw_p.write_bytes(img_bytes)

                    raw_arr = np.array(Image.open(raw_p).convert("RGBA"), dtype=np.uint8)
                    rgb_clean, fg_mask = extract_clean_magenta_unit(
                        raw_arr,
                        guide_chroma_path=guide_c_p,
                        is_quadrant=False,
                    )
                    fr_128 = downscale_frame_anchored(
                        rgb_clean,
                        fg_mask,
                        px_per_render_px=px_per_render_px,
                        frame_meta=frame_metas[idx],
                        idle_fg_y1=idle_fg_y1,
                        idle_foot_cx=idle_foot_cx,
                        palette_rgb=unit_palette,
                        is_quadrant=False,
                        idle_l_mean=idle_l_mean,
                        idle_l_std=idle_l_std,
                    )
                    nf_128, b_used = pp.generate_normal_map(fr_128, periodic=False, normal_backend="auto")
                    f_ok, fst, f_msg = check_single_frame_qa(
                        fr_128,
                        nf_128,
                        unit_palette,
                        f0_unity,
                        idle_fg_px,
                        idx,
                        min_hist_sim=(0.48 if anim == "attack" else 0.54),
                        min_fg_ratio=(0.82 if spec.unit_id == "monster_skeleton_warrior" else 0.60),
                    )
                    f_sim = float(fst.get("hist_cosine_sim", 0.0))
                    print(
                        f"  [{asset_key} f{idx}] attempt {attempt}/{n_attempts} seed={seed_base} ({dt:.1f}s) -> {f_msg}",
                        flush=True,
                    )
                    if f_ok:
                        frames_best[idx] = fr_128
                        norms_best[idx] = nf_128
                        frame_passed[idx] = True
                        frame_hist_best[idx] = f_sim
                        frame_raw_best[idx] = str(raw_p)
                    else:
                        failed_this_attempt.append(idx)
                        if frames_best[idx] is None or (not frame_passed[idx] and f_sim > frame_hist_best[idx]):
                            frames_best[idx] = fr_128
                            norms_best[idx] = nf_128
                            frame_hist_best[idx] = f_sim
                            frame_raw_best[idx] = str(raw_p)

                frames_out = [fr for fr in frames_best if fr is not None]
                norms_out = [nf for nf in norms_best if nf is not None]
                raw_paths = [rp for rp in frame_raw_best if rp]
                if len(frames_out) < 4:
                    raise RuntimeError(f"Incomplete frame assembly ({len(frames_out)}/4)")

            strip_rgba = np.concatenate(frames_out, axis=1)
            strip_norm = np.concatenate(norms_out, axis=1)
            ok, stats, msg = check_anim_qa(
                frames_out,
                norms_out,
                strip_rgba,
                strip_norm,
                unit_palette,
                anim=anim,
                idle_f0_rgba=f0_unity,
                idle_fg_px=idle_fg_px,
                min_fg_ratio=(0.82 if spec.unit_id == "monster_skeleton_warrior" else 0.60),
            )
            stats["elapsed_s"] = round(t_total, 2)
            stats["peak_vram_mib"] = peak_vram
            stats["normal_backend"] = b_used
            stats["seed_base"] = seed_base
            stats["attempts"] = attempt
            stats["mode"] = mode
            stats["weight_type"] = weight_type
            stats["raw_paths"] = raw_paths

            print(
                f"[{asset_key}] ({mode}/{weight_type}) attempt {attempt}/{n_attempts} seed={seed_base} "
                f"({t_total:.1f}s, {peak_vram} MiB) ok={ok}: {msg}",
                flush=True,
            )

            if ok:
                Image.fromarray(strip_rgba, "RGBA").save(out_albedo, "PNG")
                Image.fromarray(strip_norm, "RGBA").save(out_normal, "PNG")
                entry = {
                    "asset_id": asset_key,
                    "unit_id": spec.unit_id,
                    "anim": anim,
                    "kind": f"{spec.side}_{anim}",
                    "display_name": spec.display_name,
                    "albedo": str(out_albedo),
                    "normal": str(out_normal),
                    "ppu": 64,
                    "frame_size": [128, 128],
                    "strip_size": [512, 128],
                    "frame_count": 4,
                    "facing": spec.facing,
                    "qa_passed": True,
                    "rerolls": rerolls,
                    "qa_notes": msg,
                    "stats": stats,
                }
                state.record(asset_key, entry)
                return entry

            rerolls += 1
            best_cand = (strip_rgba, strip_norm, stats, msg)
            if mode == "per_frame":
                if failed_this_attempt:
                    frames_to_run = failed_this_attempt
                elif "motion check failed" in msg:
                    frame_passed[pair_b] = False
                    frames_to_run = [pair_b]
                else:
                    frames_to_run = [i for i in range(4) if not frame_passed[i]] or [0, 1, 2, 3]
        except Exception as exc:
            rerolls += 1
            print(f"[{asset_key}] attempt {attempt}/{n_attempts} error: {exc}", flush=True)
            if best_cand is None:
                best_cand = (
                    np.zeros((128, 512, 4), dtype=np.uint8),
                    np.zeros((128, 512, 4), dtype=np.uint8),
                    {"error": str(exc), "seed_base": seed_base, "attempts": attempt},
                    f"EXCEPTION: {exc}",
                )

    assert best_cand is not None
    strip_rgba, strip_norm, stats, msg = best_cand
    Image.fromarray(strip_rgba, "RGBA").save(out_albedo, "PNG")
    Image.fromarray(strip_norm, "RGBA").save(out_normal, "PNG")
    entry = {
        "asset_id": asset_key,
        "unit_id": spec.unit_id,
        "anim": anim,
        "kind": f"{spec.side}_{anim}",
        "display_name": spec.display_name,
        "albedo": str(out_albedo),
        "normal": str(out_normal),
        "ppu": 64,
        "frame_size": [128, 128],
        "strip_size": [512, 128],
        "frame_count": 4,
        "facing": spec.facing,
        "qa_passed": False,
        "rerolls": rerolls,
        "qa_notes": f"FLAGGED_AFTER_{n_attempts}_ATTEMPTS: {msg}",
        "stats": stats,
    }
    state.record(asset_key, entry)
    return entry


def generate_npc_and_portrait(
    npc_spec: NpcSpec,
    npc_meta: dict,
    style_ref_remote: Optional[str],
    unit_palette: np.ndarray,
    state: PackState,
    force: bool = False,
) -> Tuple[dict, dict]:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    nid = npc_spec.npc_id
    out_sheet = OUTPUT_DIR / f"{nid}_sheet.png"
    out_sheet_n = OUTPUT_DIR / f"{nid}_sheet_n.png"
    target_h = int(npc_meta["target_height_px"])

    # 1. Generate 4-frame idle strip for NPC
    if not force and state.is_done(nid, [out_sheet, out_sheet_n]):
        print(f"[RESUME] Skipping {nid} (already QA-passed)", flush=True)
        npc_entry = state.data["assets"][nid]
    else:
        chroma_p = Path(npc_meta["chroma_init"])
        depth_p = Path(npc_meta["depth"])
        canny_p = Path(npc_meta["canny"])
        soft_init_p, soft_depth_p = prepare_soft_chroma_and_depth(chroma_p, depth_p, nid, is_grid=False)
        init_rem = upload_image(soft_init_p, f"was_npc_init_{nid}.png")
        depth_rem = upload_image(soft_depth_p, f"was_npc_depth_{nid}.png")
        canny_rem = upload_image(canny_p, f"was_npc_canny_{nid}.png")

        best_npc: Optional[Tuple[np.ndarray, np.ndarray, dict, str]] = None
        rerolls = 0
        npc_entry: Optional[dict] = None

        for attempt, seed in enumerate(npc_spec.sprite_seeds[:3], start=1):
            cfg_try = 6.5 if attempt == 1 else (6.9 if attempt == 2 else 6.1)
            den_try = npc_spec.denoise if attempt < 3 else max(0.80, npc_spec.denoise - 0.05)
            wf = build_sdxl_pixel_workflow(
                prompt=npc_spec.sprite_prompt,
                negative=COMMON_SPRITE_NEG + npc_spec.extra_neg,
                seed=seed,
                filename_prefix=f"was_{nid}",
                ip_ref_remote=style_ref_remote,
                ip_weight=npc_spec.ip_weight if style_ref_remote else 0.0,
                ip_weight_type="style transfer",
                ip_start=0.0,
                ip_end=0.72,
                depth_remote=depth_rem,
                depth_strength=npc_spec.depth_strength,
                depth_end=0.70,
                canny_remote=canny_rem,
                canny_strength=npc_spec.canny_strength,
                canny_end=0.52,
                init_remote=init_rem,
                denoise=den_try,
                cfg=cfg_try,
                steps=26,
            )
            img_bytes, dt, peak = run_comfy_prompt(wf)
            raw_p = RAW_DIR / f"{nid}_a{attempt}.png"
            raw_p.parent.mkdir(parents=True, exist_ok=True)
            raw_p.write_bytes(img_bytes)

            raw_arr = np.array(Image.open(raw_p).convert("RGBA"), dtype=np.uint8)
            rgb_clean, fg_mask = extract_clean_magenta_unit(raw_arr, guide_chroma_path=chroma_p, is_quadrant=False)
            f0_rgba = downscale_to_sprite(
                rgb_clean,
                fg_mask,
                canvas_wh=(128, 128),
                target_height_px=target_h,
                max_width_px=112,
                palette_rgb=unit_palette,
                bottom_margin_px=2,
            )
            frames = synthesize_unit_idle_frames(f0_rgba, npc_spec.archetype, unit_palette, facing="East")
            strip_rgba = np.concatenate(frames, axis=1)

            norm_frames = []
            b_used = "sobel"
            for fr in frames:
                nf, b_used = pp.generate_normal_map(fr, periodic=False, normal_backend="auto")
                norm_frames.append(nf)
            strip_norm = np.concatenate(norm_frames, axis=1)

            ok, stats, msg = check_npc_qa(f0_rgba, strip_rgba, strip_norm, unit_palette, target_height_px=target_h)
            stats["elapsed_s"] = dt
            stats["peak_vram_mib"] = peak
            stats["normal_backend"] = b_used
            stats["seed"] = seed
            stats["attempts"] = attempt
            stats["raw_path"] = str(raw_p)
            print(f"[{nid}] attempt {attempt}/3 seed={seed} ({dt:.1f}s, {peak} MiB) ok={ok}: {msg}", flush=True)

            if ok:
                Image.fromarray(strip_rgba, "RGBA").save(out_sheet, "PNG")
                Image.fromarray(strip_norm, "RGBA").save(out_sheet_n, "PNG")
                npc_entry = {
                    "asset_id": nid,
                    "kind": "npc_idle",
                    "display_name": npc_spec.display_name,
                    "albedo": str(out_sheet),
                    "normal": str(out_sheet_n),
                    "ppu": 64,
                    "frame_size": [128, 128],
                    "strip_size": [512, 128],
                    "frame_count": 4,
                    "facing": "East",
                    "qa_passed": True,
                    "rerolls": rerolls,
                    "qa_notes": msg,
                    "stats": stats,
                }
                state.record(nid, npc_entry)
                break

            rerolls += 1
            best_npc = (strip_rgba, strip_norm, stats, msg)

        if npc_entry is None:
            assert best_npc is not None
            strip_rgba, strip_norm, stats, msg = best_npc
            Image.fromarray(strip_rgba, "RGBA").save(out_sheet, "PNG")
            Image.fromarray(strip_norm, "RGBA").save(out_sheet_n, "PNG")
            npc_entry = {
                "asset_id": nid,
                "kind": "npc_idle",
                "display_name": npc_spec.display_name,
                "albedo": str(out_sheet),
                "normal": str(out_sheet_n),
                "ppu": 64,
                "frame_size": [128, 128],
                "strip_size": [512, 128],
                "frame_count": 4,
                "facing": "East",
                "qa_passed": False,
                "rerolls": rerolls,
                "qa_notes": f"FLAGGED_AFTER_3_ATTEMPTS: {msg}",
                "stats": stats,
            }
            state.record(nid, npc_entry)

    # 2. Generate matching 128x128 bronze-framed portrait
    pid = f"portrait_{nid}"
    out_p = OUTPUT_DIR / f"{pid}.png"
    out_pn = OUTPUT_DIR / f"{pid}_n.png"

    if not force and state.is_done(pid, [out_p, out_pn]):
        print(f"[RESUME] Skipping {pid} (already QA-passed)", flush=True)
        return npc_entry, state.data["assets"][pid]

    ref_p = RAW_DIR / f"{pid}_ref.png"
    init_p = build_portrait_bust_ref(out_sheet, ref_p)
    ref_rem = upload_image(ref_p, f"was_pref_{nid}.png")
    init_rem = upload_image(init_p, f"was_pinit_{nid}.png")
    heraldic_bg = make_heraldic_portrait_backdrop()

    prompt = (
        f"pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo RPG shopkeeper portrait headshot bust, "
        f"simple background, solid flat bright magenta #FF00FF chroma key background, isolated chest-up portrait bust looking slightly right, "
        f"large close-up expressive head, face, and shoulders filling frame, {npc_spec.portrait_prompt}, "
        f"crisp dark slate ink outer outlines #11131F, warm golden-hour sunlight from upper left, "
        f"Songs of Conquest hero portrait card style, NO border, NO frame"
    )

    best_port: Optional[Tuple[np.ndarray, np.ndarray, dict, str]] = None
    rerolls_p = 0
    port_entry: Optional[dict] = None

    for attempt, seed in enumerate(npc_spec.portrait_seeds[:3], start=1):
        den = 0.78 if attempt == 1 else (0.74 if attempt == 2 else 0.82)
        ip_w = 0.34 if attempt == 1 else 0.28
        wf = build_sdxl_pixel_workflow(
            prompt=prompt,
            negative=PORTRAIT_NEG + ", blue crystal, ice slab, glowing crystal, sword, shield",
            seed=seed,
            filename_prefix=f"was_{pid}",
            ip_ref_remote=ref_rem,
            ip_weight=ip_w,
            ip_weight_type="style transfer",
            ip_start=0.0,
            ip_end=0.72,
            init_remote=init_rem,
            denoise=den,
            cfg=6.5,
            steps=26,
        )
        img_bytes, dt, peak = run_comfy_prompt(wf)
        raw_p = RAW_DIR / f"{pid}_a{attempt}.png"
        raw_p.write_bytes(img_bytes)

        raw_arr = np.array(Image.open(raw_p).convert("RGBA"), dtype=np.uint8)
        rgb_clean, fg_mask = extract_clean_magenta_unit(raw_arr, fill_interior_holes=True)
        lbl_p, nc_p = ndimage.label(fg_mask)
        if nc_p > 1:
            sz_p = ndimage.sum(fg_mask, lbl_p, range(1, nc_p + 1))
            fg_mask = lbl_p == (int(np.argmax(sz_p)) + 1)

        bust_112 = downscale_to_sprite(
            rgb_clean,
            fg_mask,
            canvas_wh=(112, 112),
            target_height_px=106,
            max_width_px=108,
            palette_rgb=unit_palette,
            bottom_margin_px=0,
            fill_small_holes_post=True,
        )
        comp_112 = np.zeros((112, 112, 4), dtype=np.uint8)
        comp_112[..., :3] = heraldic_bg
        comp_112[..., 3] = 255
        b_fg = bust_112[..., 3] == 255
        comp_112[b_fg, :3] = bust_112[b_fg, :3]

        framed = apply_bronze_gold_portrait_frame(comp_112)
        norm, b_used = pp.generate_normal_map(framed, periodic=False, normal_backend="auto")
        ok, stats, msg = check_portrait_qa(framed, norm, unit_palette)
        stats["elapsed_s"] = dt
        stats["peak_vram_mib"] = peak
        stats["normal_backend"] = b_used
        stats["seed"] = seed
        stats["attempts"] = attempt
        stats["raw_path"] = str(raw_p)
        print(f"[{pid}] attempt {attempt}/3 seed={seed} ({dt:.1f}s, {peak} MiB) ok={ok}: {msg}", flush=True)

        if ok:
            Image.fromarray(framed, "RGBA").save(out_p, "PNG")
            Image.fromarray(norm, "RGBA").save(out_pn, "PNG")
            port_entry = {
                "asset_id": pid,
                "npc_id": nid,
                "kind": "npc_portrait",
                "display_name": npc_spec.display_name,
                "albedo": str(out_p),
                "normal": str(out_pn),
                "size": [128, 128],
                "ppu": 128,
                "qa_passed": True,
                "rerolls": rerolls_p,
                "qa_notes": msg,
                "stats": stats,
            }
            state.record(pid, port_entry)
            break

        rerolls_p += 1
        best_port = (framed, norm, stats, msg)

    if port_entry is None:
        assert best_port is not None
        framed, norm, stats, msg = best_port
        Image.fromarray(framed, "RGBA").save(out_p, "PNG")
        Image.fromarray(norm, "RGBA").save(out_pn, "PNG")
        port_entry = {
            "asset_id": pid,
            "npc_id": nid,
            "kind": "npc_portrait",
            "display_name": npc_spec.display_name,
            "albedo": str(out_p),
            "normal": str(out_pn),
            "size": [128, 128],
            "ppu": 128,
            "qa_passed": False,
            "rerolls": rerolls_p,
            "qa_notes": f"FLAGGED_AFTER_3_ATTEMPTS: {msg}",
            "stats": stats,
        }
        state.record(pid, port_entry)

    return npc_entry, port_entry


# ---------------------------------------------------------------------------
# Contact Sheets, Pilot Comparison & Manifest
# ---------------------------------------------------------------------------

def load_font(size: int = 14, bold: bool = False) -> ImageFont.ImageFont | ImageFont.FreeTypeFont:
    cands = (
        [r"C:\Windows\Fonts\consolab.ttf", r"C:\Windows\Fonts\segoeuib.ttf"]
        if bold
        else [r"C:\Windows\Fonts\consola.ttf", r"C:\Windows\Fonts\segoeui.ttf"]
    )
    for c in cands:
        p = Path(c)
        if p.is_file():
            return ImageFont.truetype(str(p), size=size)
    return ImageFont.load_default()


def _make_meadow_bg(w: int, h: int, zoom: int = 2) -> Image.Image:
    if MEADOW_TILE_PATH.is_file():
        tile = Image.open(MEADOW_TILE_PATH).convert("RGB").resize((128 * zoom, 128 * zoom), Image.Resampling.NEAREST)
        bg = Image.new("RGB", (w, h), (42, 48, 64))
        for ty in range(0, h, tile.height):
            for tx in range(0, w, tile.width):
                bg.paste(tile, (tx, ty))
        return bg
    return Image.new("RGB", (w, h), (42, 54, 46))


def build_pilot_comparison_sheet(entries: List[Tuple[str, UnitAnimSpec, dict]]) -> Path:
    """
    Build pilot_comparison.png showing Idle f0 | Walk modes | Attack side-by-side
    at 2x nearest-neighbor zoom over meadow_grass.png.
    """
    zoom = 2
    cell_sz = 128 * zoom  # 256
    pad = 18
    hdr_h = 68
    lbl_w = 270
    row_h = cell_sz + 36
    # Columns: Idle f0 (1 frame) + 4 frames (5 * 256)
    canvas_w = pad + lbl_w + 5 * cell_sz + 2 * pad
    canvas_h = hdr_h + pad + len(entries) * row_h + pad

    sheet = Image.new("RGB", (canvas_w, canvas_h), (18, 22, 32))
    draw = ImageDraw.Draw(sheet)
    f_title = load_font(20, bold=True)
    f_sub = load_font(13, bold=False)
    f_lbl = load_font(14, bold=True)
    f_meta = load_font(11, bold=False)

    draw.text((pad, 12), "PILOT COMPARISON: IDLE F0 | WALK MODES | ATTACK (2x NEAREST OVER MEADOW)", fill=(255, 201, 82), font=f_title)
    draw.text((pad, 40), "Col 1: Unity Idle f0 | Cols 2-5: Generated f0..f3 (per_frame_style vs per_frame_linear vs grid_2x2 + Attack)", fill=(186, 198, 218), font=f_sub)

    for r_idx, (row_label, spec, rec) in enumerate(entries):
        y0 = hdr_h + pad + r_idx * row_h
        draw.rectangle([pad - 2, y0 - 2, canvas_w - pad, y0 + cell_sz + 24], fill=(26, 32, 44), outline=(74, 86, 114))
        draw.text((pad + 8, y0 + 18), sanitize_dashes(row_label), fill=(240, 236, 228), font=f_lbl)
        qa_str = "PASS" if rec.get("qa_passed") else "FLAG"
        draw.text((pad + 8, y0 + 44), sanitize_dashes(f"QA: {qa_str}"), fill=(134, 228, 152) if qa_str == "PASS" else (245, 130, 130), font=f_meta)
        draw.text((pad + 8, y0 + 64), sanitize_dashes(str(rec.get("qa_notes", ""))[:36]), fill=(154, 167, 189), font=f_meta)
        draw.text((pad + 8, y0 + 82), sanitize_dashes(str(rec.get("qa_notes", ""))[36:72]), fill=(154, 167, 189), font=f_meta)

        x_strip = pad + lbl_w
        bg = _make_meadow_bg(5 * cell_sz, cell_sz, zoom=zoom)
        idle_p = UNITY_UNITS_DIR / f"{spec.unit_id}_sheet.png"
        if idle_p.is_file():
            f0 = Image.open(idle_p).convert("RGBA").crop((0, 0, 128, 128)).resize((cell_sz, cell_sz), Image.Resampling.NEAREST)
            bg.paste(f0, (0, 0), f0)
        albedo_p = Path(rec.get("albedo", ""))
        if albedo_p.is_file():
            st_im = Image.open(albedo_p).convert("RGBA").resize((4 * cell_sz, cell_sz), Image.Resampling.NEAREST)
            bg.paste(st_im, (cell_sz, 0), st_im)

        sheet.paste(bg, (x_strip, y0))
        # Subtle divider between Idle f0 and generated 4 frames
        draw.line([(x_strip + cell_sz, y0), (x_strip + cell_sz, y0 + cell_sz)], fill=(255, 201, 82), width=2)
        for col_i, col_lbl in enumerate(("Idle f0", "f0", "f1", "f2", "f3")):
            draw.text((x_strip + col_i * cell_sz + 8, y0 + cell_sz + 4), col_lbl, fill=(186, 198, 218), font=f_meta)

    out_p = PACK_ROOT / "pilot_comparison.png"
    sheet.save(out_p, "PNG")
    return out_p


def _build_unit_anim_contact_sheet(
    title: str,
    subtitle: str,
    specs: Sequence[UnitAnimSpec],
    out_path: Path,
    state: PackState,
) -> Path:
    """
    Build contact sheet: Idle f0 | Walk f0..f3 | Attack f0..f3 at 1x (128px per cell)
    with crisp labels over meadow_grass.png.
    """
    cell_sz = 128
    pad = 16
    hdr_h = 64
    lbl_w = 240
    row_h = cell_sz + 26
    # 9 frames wide: Idle f0 (1) + Walk f0..f3 (4) + Attack f0..f3 (4)
    total_strip_w = 9 * cell_sz
    canvas_w = pad + lbl_w + total_strip_w + pad
    canvas_h = hdr_h + pad + len(specs) * row_h + pad

    sheet = Image.new("RGB", (canvas_w, canvas_h), (18, 22, 32))
    draw = ImageDraw.Draw(sheet)
    f_title = load_font(20, bold=True)
    f_sub = load_font(13, bold=False)
    f_lbl = load_font(14, bold=True)
    f_meta = load_font(11, bold=False)

    draw.text((pad, 12), sanitize_dashes(title), fill=(255, 201, 82), font=f_title)
    draw.text((pad, 38), sanitize_dashes(subtitle), fill=(186, 198, 218), font=f_sub)

    for r_idx, spec in enumerate(specs):
        y0 = hdr_h + pad + r_idx * row_h
        draw.rectangle([pad - 2, y0 - 2, canvas_w - pad + 2, y0 + cell_sz + 18], fill=(26, 32, 44), outline=(74, 86, 114))
        w_rec = state.data.get("assets", {}).get(f"{spec.unit_id}_walk", {})
        a_rec = state.data.get("assets", {}).get(f"{spec.unit_id}_attack", {})
        w_tag = "PASS" if w_rec.get("qa_passed") else ("MISS" if not w_rec else "FLAG")
        a_tag = "PASS" if a_rec.get("qa_passed") else ("MISS" if not a_rec else "FLAG")

        draw.text((pad + 8, y0 + 14), sanitize_dashes(spec.display_name), fill=(240, 236, 228), font=f_lbl)
        draw.text((pad + 8, y0 + 36), sanitize_dashes(spec.unit_id), fill=(154, 167, 189), font=f_meta)
        draw.text((pad + 8, y0 + 56), sanitize_dashes(f"Walk: {w_tag} | Atk: {a_tag}"), fill=(138, 224, 245), font=f_meta)

        x_strip = pad + lbl_w
        bg = _make_meadow_bg(total_strip_w, cell_sz, zoom=1)

        idle_p = UNITY_UNITS_DIR / f"{spec.unit_id}_sheet.png"
        if idle_p.is_file():
            f0 = Image.open(idle_p).convert("RGBA").crop((0, 0, 128, 128))
            bg.paste(f0, (0, 0), f0)

        walk_p = OUTPUT_DIR / f"{spec.unit_id}_walk_sheet.png"
        if walk_p.is_file():
            wk = Image.open(walk_p).convert("RGBA")
            bg.paste(wk, (128, 0), wk)

        atk_p = OUTPUT_DIR / f"{spec.unit_id}_attack_sheet.png"
        if atk_p.is_file():
            ak = Image.open(atk_p).convert("RGBA")
            bg.paste(ak, (5 * 128, 0), ak)

        sheet.paste(bg, (x_strip, y0))
        draw.line([(x_strip + 128, y0), (x_strip + 128, y0 + cell_sz)], fill=(255, 201, 82), width=2)
        draw.line([(x_strip + 5 * 128, y0), (x_strip + 5 * 128, y0 + cell_sz)], fill=(138, 224, 245), width=2)

    # Column header strip above first row
    hx = pad + lbl_w
    draw.text((hx + 36, hdr_h - 4), "IDLE f0", fill=(255, 201, 82), font=f_meta)
    draw.text((hx + 128 + 200, hdr_h - 4), "WALK (f0 .. f3)", fill=(138, 224, 245), font=f_meta)
    draw.text((hx + 5 * 128 + 190, hdr_h - 4), "ATTACK (f0 .. f3)", fill=(245, 160, 130), font=f_meta)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, "PNG")
    return out_path


def _build_shopkeepers_contact_sheet(out_path: Path, state: PackState) -> Path:
    """Build shopkeepers_sheet.png showing Portrait 128x128 | Idle f0..f3 | Normal f0..f3."""
    cell_sz = 128
    pad = 16
    hdr_h = 64
    lbl_w = 220
    row_h = cell_sz + 26
    total_w = 9 * cell_sz  # Portrait(1) + Idle(4) + Normal(4)
    canvas_w = pad + lbl_w + total_w + pad
    canvas_h = hdr_h + pad + len(NPC_SPECS) * row_h + pad

    sheet = Image.new("RGB", (canvas_w, canvas_h), (18, 22, 32))
    draw = ImageDraw.Draw(sheet)
    f_title = load_font(20, bold=True)
    f_sub = load_font(13, bold=False)
    f_lbl = load_font(14, bold=True)
    f_meta = load_font(11, bold=False)

    draw.text((pad, 12), "BYWATER SHOPKEEPERS: PORTRAIT 128x128 | IDLE F0..F3 | NORMAL F0..F3", fill=(255, 201, 82), font=f_title)
    draw.text((pad, 38), "4 Town Merchants (64 px/m Sunlit-128 Sprites + Bronze/Gold Framed 128x128 Portraits + Laigter +Y Normals)", fill=(186, 198, 218), font=f_sub)

    for r_idx, npc in enumerate(NPC_SPECS):
        y0 = hdr_h + pad + r_idx * row_h
        draw.rectangle([pad - 2, y0 - 2, canvas_w - pad + 2, y0 + cell_sz + 18], fill=(26, 32, 44), outline=(74, 86, 114))
        s_rec = state.data.get("assets", {}).get(npc.npc_id, {})
        p_rec = state.data.get("assets", {}).get(f"portrait_{npc.npc_id}", {})
        s_tag = "PASS" if s_rec.get("qa_passed") else ("MISS" if not s_rec else "FLAG")
        p_tag = "PASS" if p_rec.get("qa_passed") else ("MISS" if not p_rec else "FLAG")

        draw.text((pad + 8, y0 + 16), sanitize_dashes(npc.display_name), fill=(240, 236, 228), font=f_lbl)
        draw.text((pad + 8, y0 + 38), sanitize_dashes(npc.npc_id), fill=(154, 167, 189), font=f_meta)
        draw.text((pad + 8, y0 + 58), sanitize_dashes(f"Sprite: {s_tag} | Port: {p_tag}"), fill=(138, 224, 245), font=f_meta)

        x_strip = pad + lbl_w
        bg = _make_meadow_bg(total_w, cell_sz, zoom=1)

        port_p = OUTPUT_DIR / f"portrait_{npc.npc_id}.png"
        if port_p.is_file():
            pm = Image.open(port_p).convert("RGBA")
            bg.paste(pm, (0, 0), pm)

        idle_p = OUTPUT_DIR / f"{npc.npc_id}_sheet.png"
        if idle_p.is_file():
            im = Image.open(idle_p).convert("RGBA")
            bg.paste(im, (128, 0), im)

        norm_p = OUTPUT_DIR / f"{npc.npc_id}_sheet_n.png"
        if norm_p.is_file():
            nm = Image.open(norm_p).convert("RGB")
            bg.paste(nm, (5 * 128, 0))

        sheet.paste(bg, (x_strip, y0))
        draw.line([(x_strip + 128, y0), (x_strip + 128, y0 + cell_sz)], fill=(255, 201, 82), width=2)
        draw.line([(x_strip + 5 * 128, y0), (x_strip + 5 * 128, y0 + cell_sz)], fill=(138, 224, 245), width=2)

    hx = pad + lbl_w
    draw.text((hx + 16, hdr_h - 4), "PORTRAIT 128", fill=(255, 201, 82), font=f_meta)
    draw.text((hx + 128 + 200, hdr_h - 4), "IDLE (f0 .. f3)", fill=(138, 224, 245), font=f_meta)
    draw.text((hx + 5 * 128 + 180, hdr_h - 4), "NORMAL MAP (f0 .. f3)", fill=(186, 198, 218), font=f_meta)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, "PNG")
    return out_path


def build_contact_sheets_and_manifest(state: PackState) -> Tuple[Path, List[Path]]:
    heroes = [u for u in UNIT_SPECS if u.side == "hero"]
    monsters = [u for u in UNIT_SPECS if u.side == "monster"]

    hs = _build_unit_anim_contact_sheet(
        "REALMZ HEROES: IDLE F0 | WALK F0..F3 | ATTACK F0..F3 (64 px/m)",
        "All 6 Hero Classes facing East/Right over meadow_grass.png (1x 128x128 per frame)",
        heroes,
        PACK_ROOT / "walk_attack_heroes_sheet.png",
        state,
    )
    ms = _build_unit_anim_contact_sheet(
        "BYWATER MONSTERS: IDLE F0 | WALK F0..F3 | ATTACK F0..F3 (64 px/m)",
        "All 7 Enemy Units facing West/Left over meadow_grass.png (1x 128x128 per frame)",
        monsters,
        PACK_ROOT / "walk_attack_monsters_sheet.png",
        state,
    )
    ss = _build_shopkeepers_contact_sheet(PACK_ROOT / "shopkeepers_sheet.png", state)

    raw_assets = state.data.get("assets", {})
    assets = {k: v for k, v in raw_assets.items() if not k.startswith("pilot_")}
    passed = sum(1 for v in assets.values() if v.get("qa_passed"))
    manifest = {
        "pipeline": "walk_attack_shopkeeper",
        "ppu": 64,
        "palette": str(PALETTE_PATH),
        "total_assets": len(assets),
        "qa_passed": passed,
        "qa_flagged": len(assets) - passed,
        "contact_sheets": {
            "heroes": str(hs),
            "monsters": str(ms),
            "shopkeepers": str(ss),
        },
        "assets": assets,
    }
    write_clean_json(MANIFEST_PATH, manifest)
    return MANIFEST_PATH, [hs, ms, ss]


# ---------------------------------------------------------------------------
# Pilot Runner
# ---------------------------------------------------------------------------

def run_pilot(meta: dict, unit_palette: np.ndarray, state: PackState, force: bool = False) -> Path:
    knight = UNIT_SPEC_BY_ID["hero_knight"]
    frog = UNIT_SPEC_BY_ID["monster_giant_frog"]
    units_meta = meta["units"]

    pilot_jobs = [
        ("Knight Walk (per_frame + style)", knight, "walk", "per_frame", "style transfer", "_pilot_pf_style", "pilot_hero_knight_walk_pf_style"),
        ("Knight Walk (per_frame + linear)", knight, "walk", "per_frame", "linear", "_pilot_pf_linear", "pilot_hero_knight_walk_pf_linear"),
        ("Knight Walk (grid_2x2 + style)", knight, "walk", "grid_2x2", "style transfer", "_pilot_grid2x2", "pilot_hero_knight_walk_grid2x2"),
        ("Knight Attack (per_frame + style)", knight, "attack", "per_frame", "style transfer", "_pilot_pf_style", "pilot_hero_knight_attack_pf_style"),
        ("Giant Frog Attack (per_frame + style)", frog, "attack", "per_frame", "style transfer", "_pilot_pf_style", "pilot_monster_giant_frog_attack_pf_style"),
    ]

    results: List[Tuple[str, UnitAnimSpec, dict]] = []
    for label, sp, anim, mode, wt, suffix, skey in pilot_jobs:
        rec = generate_unit_animation(
            sp,
            anim,
            units_meta[sp.unit_id],
            unit_palette,
            state,
            mode=mode,
            weight_type=wt,
            force=force,
            out_dir=PILOT_OUT_DIR,
            file_stem_suffix=suffix,
            state_key_override=skey,
        )
        results.append((label, sp, rec))

    comp_path = build_pilot_comparison_sheet(results)
    print(f"[PILOT] Comparison sheet written: {comp_path}", flush=True)
    return comp_path


# ---------------------------------------------------------------------------
# Main CLI Entry Point
# ---------------------------------------------------------------------------

def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Realmz Walk, Attack & Bywater Shopkeeper Art Pipeline (64 px/m Sunlit-128)"
    )
    parser.add_argument(
        "--pilot",
        action="store_true",
        help="Run hero_knight Walk (3 modes) + hero_knight Attack + monster_giant_frog Attack and build pilot_comparison.png",
    )
    parser.add_argument(
        "--mode",
        choices=("per_frame", "grid_2x2"),
        default="per_frame",
        help="Generation mode for unit animations (default: per_frame)",
    )
    parser.add_argument(
        "--weight-type",
        choices=("style transfer", "linear"),
        default="style transfer",
        help="IPAdapter weight_type (default: 'style transfer')",
    )
    parser.add_argument(
        "--phase",
        choices=("all", "heroes", "monsters", "npcs", "sheets"),
        default="all",
        help="Execution phase (default: all)",
    )
    parser.add_argument(
        "--only",
        type=str,
        default="",
        help="Comma-separated unit/NPC IDs to process (e.g. hero_knight,npc_blacksmith)",
    )
    parser.add_argument(
        "--anim",
        choices=("walk", "attack", "both"),
        default="both",
        help="Animation type for combat units (default: both)",
    )
    parser.add_argument(
        "--resume",
        action="store_true",
        help="Skip assets already QA-passed in state.json whose output PNGs exist",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing assets regardless of state.json",
    )
    parser.add_argument(
        "--keep-comfy",
        action="store_true",
        help="Keep ComfyUI :8188 running after exit (default: always teardown and verify VRAM < 1000 MiB)",
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    if not META_PATH.is_file():
        raise FileNotFoundError(f"Missing guide metadata: {META_PATH}")
    meta = json.loads(META_PATH.read_text(encoding="utf-8"))
    state = PackState(STATE_PATH)
    unit_palette = load_unit_palette()

    only_set = {tok.strip() for tok in args.only.split(",") if tok.strip()}
    # By default resume skips done assets unless --force is passed (and --resume is explicitly supported)
    force = bool(args.force) and not bool(args.resume)

    with keep_awake():
        if args.phase == "sheets" and not args.pilot:
            m_path, sheets = build_contact_sheets_and_manifest(state)
            print(f"[SHEETS] Manifest: {m_path}", flush=True)
            for s in sheets:
                print(f"[SHEETS] Contact sheet: {s}", flush=True)
            return 0

        spawned: Optional[subprocess.Popen] = None
        try:
            spawned = ensure_comfy_running()

            if args.pilot:
                run_pilot(meta, unit_palette, state, force=force)
                free_comfy_vram()
                return 0

            anims = ["walk", "attack"] if args.anim == "both" else [args.anim]
            units_meta = meta["units"]
            npcs_meta = meta["npcs"]

            if args.phase in ("all", "heroes"):
                for spec in UNIT_SPECS:
                    if spec.side != "hero":
                        continue
                    if only_set and spec.unit_id not in only_set:
                        continue
                    for anim in anims:
                        generate_unit_animation(
                            spec,
                            anim,
                            units_meta[spec.unit_id],
                            unit_palette,
                            state,
                            mode=args.mode,
                            weight_type=args.weight_type,
                            force=force,
                        )

            if args.phase in ("all", "monsters"):
                for spec in UNIT_SPECS:
                    if spec.side != "monster":
                        continue
                    if only_set and spec.unit_id not in only_set:
                        continue
                    for anim in anims:
                        generate_unit_animation(
                            spec,
                            anim,
                            units_meta[spec.unit_id],
                            unit_palette,
                            state,
                            mode=args.mode,
                            weight_type=args.weight_type,
                            force=force,
                        )

            if args.phase in ("all", "npcs"):
                style_p = build_soc_style_ref()
                style_rem = upload_image(style_p, "was_soc_style_ref.png") if style_p else None
                for npc_spec in NPC_SPECS:
                    if only_set and npc_spec.npc_id not in only_set:
                        continue
                    generate_npc_and_portrait(
                        npc_spec,
                        npcs_meta[npc_spec.npc_id],
                        style_rem,
                        unit_palette,
                        state,
                        force=force,
                    )

            free_comfy_vram()
        finally:
            if not args.keep_comfy:
                v_idle = teardown_comfy(spawned, force_kill_any=True)
                if v_idle >= 1000:
                    print(f"[WARN] Post-teardown VRAM is {v_idle} MiB (>= 1000 MiB)", flush=True)

        m_path, sheets = build_contact_sheets_and_manifest(state)
        print(f"[DONE] Manifest: {m_path}", flush=True)
        for s in sheets:
            print(f"[DONE] Contact sheet: {s}", flush=True)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
