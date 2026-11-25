"""
Realmz Unified Combat Roster Pipeline (W-234 / T-311):
64 px/m Songs of Conquest Sunlit 128 Standard across ALL 6 Heroes & ALL 7 Bywater Enemies.

Features:
  - Uses Blender 4.5.9 depth + canny + material-colored #FF00FF chroma init guides
    from combat_roster_guides.py (Heroes yaw=-32 facing East/Right; Enemies yaw=+32 facing West/Left).
  - Serializes ComfyUI (:8188) Illustrious-XL + skormino pixel-art LoRA + ControlNet Union
    (Depth + optional Canny) + optional IP-Adapter Plus style reference, keeping peak VRAM < 7800 MiB.
  - Strictly enforces #FF00FF chroma key removal + enclosed gap punching + 5px perimeter despill
    + bottom shadow guard + PixelOE contrast-aware scaling to exact 64 px/m proportions in 128x128.
  - Locks all pixels to palette_soc_sunlit_128.json, synthesizes zero-flicker 4-frame idle strips
    (512x128 RGBA) with archetype-aware breathing/cloak/throat-sac/tail motion + laigter +Y normals.
  - Enforces automated QA gates with multi-seed retries and builds unified_roster_lineup.png
    over real Cartography meadow_grass.png turf + roster_pack_manifest.json.

Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW = 0x08000000 in all subprocesses.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
import time
import urllib.parse
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw
from scipy import ndimage

CART_DIR = Path(__file__).resolve().parent
PIPE_DIR = CART_DIR.parent
sys.path.insert(0, str(CART_DIR))
sys.path.insert(0, str(PIPE_DIR))

import pixel_postprocess as pp  # noqa: E402

CREATE_NO_WINDOW = 0x08000000
SERVER_URL = "http://127.0.0.1:8188"
COMFY_PYTHON = Path(r"C:\ComfyUI\.venv\Scripts\python.exe")
COMFY_MAIN = Path(r"C:\ComfyUI\main.py")
COMFY_ROOT = Path(r"C:\ComfyUI")
PALETTE_PATH = PIPE_DIR / "palette_soc_sunlit_128.json"
MEADOW_TILE_PATH = Path(r"C:\Users\karti\realmz\remaster\RealmzUnity\Assets\Art\Cartography\tiles\meadow_grass.png")
ARLEON_TROOPS_REF = Path(
    r"C:\Users\karti\realmz\.scratch\realmz_soc_reverse_engineering\references\grass_frames\shadows_gif\arleon_troops_f01.png"
)

PACK_ROOT = Path(r"C:\Users\karti\realmz\.scratch\realmz_combat_art_pack")
GUIDE_DIR = PACK_ROOT / "guides"
RAW_DIR = PACK_ROOT / "raw"
OUTPUT_DIR = PACK_ROOT / "output"
HEROES_OUT = OUTPUT_DIR / "heroes"
MONSTERS_OUT = OUTPUT_DIR / "monsters"
ICONS_OUT = OUTPUT_DIR / "icons"

COMMON_NEG = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "minecraft, voxel, lego, blocky cubes, low poly, geometric blocks, stiff box, "
    "multiple characters, sprite sheet, grid, floor, ground, grass ground, dirt mound, soil base, "
    "cast shadow, drop shadow, contact shadow, oval shadow, purple shadow, violet shadow, shadow puddle, shadow under feet, "
    "ground disc, pedestal, platform, isometric block, diorama, circle background, medallion, halo, vignette, text, watermark, "
    "signature, UI, frame, border, white background, grey background, lowres, worst quality, "
    "deformed anatomy, extra limbs, fused weapons, neon pink armor, magenta armor"
)


@dataclass
class UnitSpec:
    unit_id: str
    display_name: str
    side: str  # "hero" or "monster"
    facing: str  # "East" or "West"
    archetype: str  # "biped", "frog", "rat", "magic_biped"
    target_height_px: int
    max_width_px: int
    prompt: str
    extra_neg: str
    seeds: List[int]
    legacy_aliases: List[str] = field(default_factory=list)
    denoise: float = 0.88
    depth_strength: float = 0.44
    depth_end: float = 0.66
    canny_strength: float = 0.0
    canny_end: float = 0.0
    ip_weight: float = 0.34
    lora_strength: float = 0.88


UNIT_SPECS: List[UnitSpec] = [
    # ------------------------------------------------------------------
    # 6 HEROES (Facing East / Right toward enemies)
    # ------------------------------------------------------------------
    UnitSpec(
        unit_id="hero_knight",
        display_name="Aldric (Knight)",
        side="hero",
        facing="East",
        archetype="biped",
        target_height_px=90,
        max_width_px=112,
        legacy_aliases=["knight", "aldric"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo knight warrior, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique tactical stance facing right, full body head to boots centered, sturdy armored greaves, "
            "gleaming silver steel plate armor #BAC6DA with sunlit gold trim #ECA938, crested silver helmet with crimson plume, "
            "flowing crimson scarlet cloak #C23846 trailing behind on left, royal cobalt blue and gold kite shield #3452A1, "
            "shining bright silver steel broadsword held upright in front right hand, crisp dark slate ink inner outlines #11131F, "
            "warm golden-hour sunlight from upper left, Songs of Conquest Arleon Footman hero style, NO ground shadow"
        ),
        extra_neg=", red sword, pink sword, skinny stick legs, floating, dark background, castle background",
        seeds=[510101, 510102, 510103, 510104],
    ),
    UnitSpec(
        unit_id="hero_cleric",
        display_name="Liora (Cleric)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        target_height_px=88,
        max_width_px=110,
        legacy_aliases=["cleric", "liora"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1girl, solo battle cleric priestess, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique tactical stance facing right, full body head to boots centered, organic cloth folds, "
            "ivory white and royal azure liturgical battle vestments #F0ECE4 #3452A1 with sunlit gold shoulder mantle #ECA938, "
            "white and gold hooded coif, ornate golden flanged war mace in right hand and tall ward staff topped with glowing cyan arcane crystal #54C9C0, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon hero style, NO ground shadow"
        ),
        extra_neg=", bare legs, modern clothing, dark robe, pink robe, floating",
        seeds=[510201, 510202, 510203, 510204],
    ),
    UnitSpec(
        unit_id="hero_ranger",
        display_name="Kaelen (Ranger)",
        side="hero",
        facing="East",
        archetype="biped",
        target_height_px=88,
        max_width_px=114,
        legacy_aliases=["ranger", "kaelen"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo woodland ranger archer, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique tactical stance facing right, full body head to boots centered, sturdy chestnut leather boots #825132, "
            "vibrant forest emerald green hooded cloak and moss tunic #2E693B #52A653 with leather belt and arrow quiver on back, "
            "holding long wooden recurve bow #A36943 aimed right with steel-tipped arrow ready, organic tunic drapery, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon Ranger style, NO ground shadow"
        ),
        extra_neg=", blue cloak, navy hood, slate blue cloak, gun, rifle, modern bow, disconnected bowstring, neon green, pink cape",
        seeds=[510311, 510312, 510313, 510314],
        ip_weight=0.14,
        denoise=0.84,
    ),
    UnitSpec(
        unit_id="hero_mage",
        display_name="Theron (Mage)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        target_height_px=92,
        max_width_px=112,
        legacy_aliases=["mage", "theron"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo elder arcane battlemage wizard, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique tactical stance facing right, full body head to boots centered, flowing draped cloth, "
            "royal sapphire cobalt wizard robes #273C78 #476EC7 with sunlit gold arcane trim #ECA938, pointed hood and flowing white beard, "
            "holding tall gnarled oak wizard staff #825132 topped with bright glowing arcane cyan crystal orb #85E6DA raised forward-right, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest hero style, NO ground shadow"
        ),
        extra_neg=", pink robes, purple background, hat cut off, floating orbs detached",
        seeds=[510401, 510402, 510403, 510404],
    ),
    UnitSpec(
        unit_id="hero_paladin",
        display_name="Seraphina (Paladin)",
        side="hero",
        facing="East",
        archetype="magic_biped",
        target_height_px=92,
        max_width_px=114,
        legacy_aliases=["paladin", "seraphina"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1girl, solo holy paladin champion, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique tactical stance facing right, full body head to boots centered, articulated curved plate armor, "
            "gleaming silver-steel and sunlit gold heavy plate armor #D8E2F0 #FFC952, iconic silver-gold winged knight helmet, "
            "white and gold sunburst surcoat tabard #F0ECE4 #ECA938, holding radiant steel two-handed greatsword raised forward-right, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Shield of Order style, NO ground shadow"
        ),
        extra_neg=", angel wings on back, dark black armor, skinny legs, floating",
        seeds=[510501, 510502, 510503, 510504],
    ),
    UnitSpec(
        unit_id="hero_rogue",
        display_name="Vex (Rogue)",
        side="hero",
        facing="East",
        archetype="biped",
        target_height_px=78,
        max_width_px=112,
        legacy_aliases=["rogue", "vex"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, solo agile fantasy rogue assassin, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG combat unit sprite, "
            "3/4 oblique crouched ready stance facing right, full body head to boots centered, dynamic agile anatomy, "
            "dark slate-indigo leather armor and pointed stealth hood #2F3A59 #475061 with flowing crimson scarf #C23846, "
            "fitted dark boots and leather utility belt, wielding dual gleaming curved silver steel daggers #BAC6DA, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon style, NO ground shadow"
        ),
        extra_neg=", blue dress, skirt, bare legs, giant cloud shadow, cyan shirt",
        seeds=[510601, 510602, 510603, 510604],
    ),
    # ------------------------------------------------------------------
    # 7 BYWATER ENEMIES (Facing West / Left toward heroes)
    # ------------------------------------------------------------------
    UnitSpec(
        unit_id="monster_giant_frog",
        display_name="Giant Frog",
        side="monster",
        facing="West",
        archetype="frog",
        target_height_px=62,
        max_width_px=108,
        legacy_aliases=["giant_frog"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo giant monster toad, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique chunky crouching battle toad frog facing left, full body centered, "
            "vibrant sunlit mossy emerald green and olive-lime speckled toad skin #3D8748 #52A653 #6EC260 with dark forest spots #183622, "
            "warm cream-ochre belly and inflated throat sac #E0C8A8 #ECA938, bulging golden-amber eyes #FFC952, sturdy webbed claws, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Rana frog monster style, NO ground shadow"
        ),
        extra_neg=", pure black skin, near-black green, dark silhouette, skinny upright frog, lizard, tail, horns, antlers, spear, lilypad, water puddle, pond",
        seeds=[520111, 520112, 520113, 520114],
        denoise=0.84,
        depth_strength=0.52,
        ip_weight=0.0,
    ),
    UnitSpec(
        unit_id="monster_goblin_raider",
        display_name="Goblin Raider",
        side="monster",
        facing="West",
        archetype="biped",
        target_height_px=66,
        max_width_px=106,
        legacy_aliases=["goblin", "goblin_raider", "goblin_champion"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo goblin warrior monster, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique hunched crouched goblin raider facing left, compact short stature, full body head to feet centered, "
            "vibrant emerald-moss green goblin skin #52A653 #3D8748, long pointed goblin ears, fierce golden eyes, "
            "patched chestnut leather armor and ragged crimson waistcloth #825132 #992838, wielding notched steel scimitar and round wooden iron-rimmed buckler shield, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest monster style, NO ground shadow"
        ),
        extra_neg=", giant troll, tall orc, lizard, tail, pure black outline blob, grey puddle shadow",
        seeds=[520201, 520202, 520203, 520204],
    ),
    UnitSpec(
        unit_id="monster_goblin_archer",
        display_name="Goblin Archer",
        side="monster",
        facing="West",
        archetype="biped",
        target_height_px=66,
        max_width_px=108,
        legacy_aliases=["goblin_archer", "goblin_shaman", "orc_archer"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo emerald goblin archer monster, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique hunched goblin scout facing left, compact short goblin stature, full body head to feet centered, "
            "emerald-green goblin skin #52A653 #3D8748 with unmistakable long pointed green goblin ears poking out from ragged brown-olive hood #633C24, "
            "leather jerkin and feathered arrow quiver on back, drawing wooden shortbow #A36943 aimed left toward enemy with arrow notched, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest monster style, NO ground shadow"
        ),
        extra_neg=", lizard, kobold, dragon, reptile, orange scales, tail, horns, spear, tall human",
        seeds=[520301, 520302, 520303, 520304],
    ),
    UnitSpec(
        unit_id="monster_orc_warrior",
        display_name="Orc Warrior",
        side="monster",
        facing="West",
        archetype="biped",
        target_height_px=98,
        max_width_px=118,
        legacy_aliases=["orc_warrior"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo massive muscular orc berserker brute, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique imposing broad-shouldered battle stance facing left, full body head to boots centered, defined organic musculature, "
            "broad olive-sage and warm ochre muscular orc skin #5A8254 #77A168, heavy brow and prominent ivory tusks #F0DEC2, "
            "studded dark iron shoulder pauldrons and bracers #475061, crimson war kilt #992838, wielding huge heavy notched iron battleaxe cleaver #9AA7BD, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Horned One brute scale, NO ground shadow"
        ),
        extra_neg=", tiny goblin, skinny knight, human face, pistol, pink armor",
        seeds=[520401, 520402, 520403, 520404],
    ),
    UnitSpec(
        unit_id="monster_giant_rat",
        display_name="Giant Rat",
        side="monster",
        facing="West",
        archetype="rat",
        target_height_px=56,
        max_width_px=112,
        legacy_aliases=["giant_rat", "giant_sewer_rat", "monstrous_rat"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo giant monstrous cellar rat beast, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique hunched feral rat facing left, full body snout to curled tail centered, detailed shaggy fur clusters, "
            "sunlit warm taupe-brown and stone-grey mottled fur #8A7058 #AA8D70 #87817A with dark umber shadows #4D3C2E, "
            "elongated snarling snout with sharp white fangs, glowing crimson-red eyes #E35259, small torn ears, clawed paws, "
            "long curled scarred pink-tan tail #DC9F73 trailing right, crisp dark slate ink inner outlines #11131F, "
            "warm upper-left sunlight, Songs of Conquest Rana giant rat style, NO ground shadow"
        ),
        extra_neg=", pure black fur, flat brown blob, horns, curved horns, antlers, tusks, crest, spikes, dragon, reptile, cute squirrel, fluffy puppy, standing bipedal rat, blue ground shadow, cat",
        seeds=[520521, 520522, 520523, 520524],
        denoise=0.83,
        depth_strength=0.54,
        ip_weight=0.0,
    ),
    UnitSpec(
        unit_id="monster_skeleton_warrior",
        display_name="Skeleton Warrior",
        side="monster",
        facing="West",
        archetype="biped",
        target_height_px=84,
        max_width_px=110,
        legacy_aliases=["skeleton_warrior", "skeletal_warrior"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, no humans, solo undead skeleton warrior, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG monster sprite, "
            "3/4 oblique battle stance facing left, full body skull to bony feet centered, detailed skeletal anatomy, "
            "bleached sunlit parchment bone skull, ribcage, and sturdy bone limbs #E5D8BD #F7F4EA #8A7058, "
            "rusted dark iron pot helmet and shoulder guard #606B80, tattered crimson waistcloth #992838, cracked round wooden iron shield and notched steel shortsword, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest undead warrior style, NO ground shadow"
        ),
        extra_neg=", red sword blade, fleshy human skin, modern gun, invisible thin bones",
        seeds=[520601, 520602, 520603, 520604],
    ),
    UnitSpec(
        unit_id="monster_bandit_cutthroat",
        display_name="Bandit Cutthroat",
        side="monster",
        facing="West",
        archetype="biped",
        target_height_px=82,
        max_width_px=112,
        legacy_aliases=["bandit_cutthroat"],
        prompt=(
            "pixpix, pixel_art, hi-bit pixel art, masterpiece, best quality, 1boy, solo medieval bandit highwayman cutthroat, "
            "simple background, solid flat bright magenta #FF00FF chroma key background, isolated RPG enemy sprite, "
            "3/4 oblique menacing duelist stance facing left, full body head to boots centered, solid torso and cloak drapery, "
            "weathered warm chestnut leather brigandine vest and dark olive-slate cloak #825132 #633C24 #475061, crimson bandana mask #C23846, "
            "sturdy tall leather boots, gripping long gleaming silver-steel curved cutlass sabre sword held diagonally forward-left #D8E2F0, "
            "crisp dark slate ink inner outlines #11131F, warm upper-left sunlight, Songs of Conquest Arleon Militia outlaw style, NO ground shadow"
        ),
        extra_neg=", empty hands, unarmed, purple cloak, violet shadow, red block helmet, bucket helmet, transparent body holes, pink tunic",
        seeds=[520711, 520712, 520713, 520714],
        denoise=0.82,
        depth_strength=0.56,
        depth_end=0.76,
        ip_weight=0.20,
    ),
]

SPEC_BY_ID: Dict[str, UnitSpec] = {u.unit_id: u for u in UNIT_SPECS}


# ---------------------------------------------------------------------------
# ComfyUI Lifecycle & Requests
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
        r = requests.get(f"{SERVER_URL}/system_stats", timeout=2.0)
        return r.ok
    except Exception:
        return False


def ensure_comfy_running() -> Optional[subprocess.Popen]:
    if is_comfy_up():
        print("[ComfyUI] Reusing running server at :8188", flush=True)
        return None
    if not COMFY_PYTHON.is_file() or not COMFY_MAIN.is_file():
        raise FileNotFoundError(f"ComfyUI not found at {COMFY_ROOT}")

    log_path = PACK_ROOT / "comfyui_8188.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_fh = open(log_path, "ab")
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

    print(f"[ComfyUI] Spawned headless :8188 (pid={proc.pid}), waiting for readiness...", flush=True)
    deadline = time.time() + 90.0
    while time.time() < deadline:
        if proc.poll() is not None:
            raise RuntimeError(f"ComfyUI exited early with code {proc.returncode}. See {log_path}")
        if is_comfy_up():
            print("[ComfyUI] Ready.", flush=True)
            return proc
        time.sleep(1.0)
    proc.terminate()
    raise TimeoutError("Timed out waiting for ComfyUI startup on :8188")


def teardown_comfy(spawned_proc: Optional[subprocess.Popen], force_kill_any: bool = False) -> None:
    try:
        requests.post(
            f"{SERVER_URL}/free",
            json={"unload_models": True, "free_memory": True},
            timeout=8.0,
        )
    except Exception:
        pass

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
    elif force_kill_any and os.name == "nt":
        # Stop any listening ComfyUI python process on :8188 cleanly
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

    deadline = time.time() + 10.0
    while time.time() < deadline:
        v = query_vram_mib()
        if 0 <= v < 1000:
            break
        time.sleep(0.5)
    print(f"[ComfyUI] Post-teardown GPU VRAM: {query_vram_mib()} MiB", flush=True)


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
    return ir.content, round(time.time() - t0, 2), peak


def build_soc_style_ref() -> Optional[Path]:
    """
    Create a crisp 512x512 Songs of Conquest Arleon unit style collage on solid #FF00FF
    so IPAdapter transfers SoC pixel brushwork & shading without dark-purple bg bleed.
    """
    if not ARLEON_TROOPS_REF.is_file():
        return None
    out_p = GUIDE_DIR / "soc_arleon_style_magenta.png"
    out_p.parent.mkdir(parents=True, exist_ok=True)
    src = np.array(Image.open(ARLEON_TROOPS_REF).convert("RGB"))
    # Crop Ranger, Footman, Shield of Order, Archer cleanly (1920x1080 sheet; zero horned monsters)
    crops = [
        (src[325:495, 585:735], (20, 20), 210),     # Ranger
        (src[320:505, 755:885], (275, 20), 215),    # Footman
        (src[585:805, 755:885], (20, 268), 220),    # Shield of Order
        (src[595:800, 585:735], (268, 268), 210),   # Archer
    ]
    canvas = np.full((512, 512, 3), (255, 0, 255), dtype=np.uint8)
    from collections import deque

    for crop, (ox, oy), target_h in crops:
        h, w, _ = crop.shape
        lab = pp.rgb_to_lab_float(crop)
        # Exterior border-seeded BFS removing dark plum/burgundy studio vignette & ground shadow
        border_lab = np.median(
            np.concatenate([lab[:4, :, :].reshape(-1, 3), lab[:, :4, :].reshape(-1, 3)], axis=0),
            axis=0,
        )
        dist = np.linalg.norm(lab - border_lab[None, None, :], axis=-1)
        is_bg_cand = (dist <= 16.5) & (lab[..., 0] < 22.0)
        ext_bg = np.zeros((h, w), dtype=bool)
        vis = np.zeros((h, w), dtype=bool)
        q = deque()
        for x in range(w):
            for y in (0, h - 1):
                if is_bg_cand[y, x] and not vis[y, x]:
                    vis[y, x] = True; ext_bg[y, x] = True; q.append((y, x))
        for y in range(h):
            for x in (0, w - 1):
                if is_bg_cand[y, x] and not vis[y, x]:
                    vis[y, x] = True; ext_bg[y, x] = True; q.append((y, x))
        while q:
            cy, cx = q.popleft()
            for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                if 0 <= ny < h and 0 <= nx < w and not vis[ny, nx] and is_bg_cand[ny, nx]:
                    vis[ny, nx] = True; ext_bg[ny, nx] = True; q.append((ny, nx))
        fg = ~ext_bg
        # Keep major connected component(s) >= 2% of largest
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


def build_unit_workflow(
    spec: UnitSpec,
    seed: int,
    init_remote: str,
    depth_remote: str,
    canny_remote: Optional[str],
    ip_ref_remote: Optional[str],
    ip_weight: float,
    denoise: float,
    depth_strength: float,
    canny_strength: float,
) -> dict:
    neg = COMMON_NEG + spec.extra_neg
    wf: Dict[str, dict] = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "Illustrious-XL-v2.0.safetensors"}},
        "2": {
            "class_type": "LoraLoader",
            "inputs": {
                "model": ["1", 0],
                "clip": ["1", 1],
                "lora_name": "pixel-art-style-illustrious-by-skormino.safetensors",
                "strength_model": spec.lora_strength,
                "strength_clip": spec.lora_strength,
            },
        },
        "3": {"class_type": "CLIPSetLastLayer", "inputs": {"clip": ["2", 1], "stop_at_clip_layer": -2}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": spec.prompt}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["3", 0], "text": neg}},
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
                "weight_type": "style transfer",
                "combine_embeds": "concat",
                "start_at": 0.0,
                "end_at": 0.72,
                "embeds_scaling": "V only",
                "clip_vision": [cv_ldr, 0],
            },
        }
        model_ref = [ip_app, 0]

    cn_ldr = str(nid); nid += 1
    wf[cn_ldr] = {"class_type": "ControlNetLoader", "inputs": {"control_net_name": "xinsir-controlnet-union-sdxl-1.0-promax.safetensors"}}

    # Depth ControlNet
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
            "end_percent": spec.depth_end,
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
                "end_percent": spec.canny_end,
                "vae": ["1", 2],
            },
        }
        pos_ref = [c_app, 0]
        neg_ref = [c_app, 1]

    init_img = str(nid); nid += 1
    init_enc = str(nid); nid += 1
    wf[init_img] = {"class_type": "LoadImage", "inputs": {"image": init_remote}}
    wf[init_enc] = {"class_type": "VAEEncode", "inputs": {"pixels": [init_img, 0], "vae": ["1", 2]}}

    ksampler = str(nid); nid += 1
    vaedec = str(nid); nid += 1
    save = str(nid); nid += 1
    wf[ksampler] = {
        "class_type": "KSampler",
        "inputs": {
            "model": model_ref,
            "positive": pos_ref,
            "negative": neg_ref,
            "latent_image": [init_enc, 0],
            "seed": seed,
            "steps": 26,
            "cfg": 6.5,
            "sampler_name": "dpmpp_2m",
            "scheduler": "karras",
            "denoise": denoise,
        },
    }
    wf[vaedec] = {"class_type": "VAEDecode", "inputs": {"samples": [ksampler, 0], "vae": ["1", 2]}}
    wf[save] = {"class_type": "SaveImage", "inputs": {"images": [vaedec, 0], "filename_prefix": f"combat_{spec.unit_id}"}}
    return wf


# ---------------------------------------------------------------------------
# Strict #FF00FF Chroma Extraction + Exact 64 px/m PixelOE Scaling
# ---------------------------------------------------------------------------

def load_unit_palette(palette_path: Path = PALETTE_PATH) -> np.ndarray:
    """
    Load palette_soc_sunlit_128.json excluding neon wildflower pink (#E8569C) and
    bright lavender (#CE9BF0, #E6C4FA) so edge despill never snaps onto pink/magenta tones.
    """
    data = json.loads(palette_path.read_text(encoding="utf-8"))
    exclude = {"#E8569C", "#CE9BF0", "#E6C4FA", "#AE73D6"}
    colors = [c for c in data["colors"] if c.upper() not in exclude]
    return np.array([pp.hex_to_rgb(c) for c in colors], dtype=np.uint8)


def extract_clean_magenta_unit(raw_arr: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """
    Extract unit foreground from solid #FF00FF magenta chroma backdrop:
      1. Border-seeded BFS removes outer #FF00FF / pinkish / shadow fringes.
      2. Punches enclosed magenta/purple gaps between legs, bowstring, staff, cape.
      3. Erodes outer 12px magenta/orchid glow halo back to crisp dark outline.
      4. Despills outer 6px perimeter rim around both outer and inner punched holes.
    """
    rgb, ext_fg = pp.remove_background_1bit(raw_arr, strip_ground_shadow=True)
    r = rgb[..., 0].astype(np.int16)
    g = rgb[..., 1].astype(np.int16)
    b = rgb[..., 2].astype(np.int16)

    # Force outer 6px frame border to background so edge-padding lines never enter fg_mask
    ext_fg[:6, :] = False
    ext_fg[-6:, :] = False
    ext_fg[:, :6] = False
    ext_fg[:, -6:] = False

    mag_dom = np.minimum(r, b) - g
    chroma_bleed = ((r - g) > 16) & ((b - g) > 16)
    # Only punch genuinely bright neon-magenta chroma pockets (never dark brown/charcoal/indigo shadows)
    enclosed_magenta = ((mag_dom > 36) & (r > 155) & (b > 155) & (g < 115))
    fg_mask = ext_fg & (~enclosed_magenta)

    # Strip soft magenta/orchid transition glow in a 10px outer border band around ~fg_mask
    halo_zone = ndimage.binary_dilation(~fg_mask, iterations=10) & fg_mask
    halo_magenta = halo_zone & ((mag_dom > 20) & (r > 130) & (b > 130) & (g < 115))
    fg_mask &= ~halo_magenta

    # Strip any faint bottom dark-purple/indigo cast-shadow puddle beneath feet
    ys_tmp = np.where(fg_mask)[0]
    if len(ys_tmp) > 0:
        y_bot = int(ys_tmp.max())
        foot_band = np.zeros_like(fg_mask)
        foot_band[max(0, y_bot - 64) : y_bot + 1, :] = True
        lum_tmp = (0.2126 * r + 0.7152 * g + 0.0722 * b)
        purple_shadow = foot_band & fg_mask & (b > g + 22) & (b > r + 10) & (lum_tmp < 85.0)
        if np.any(purple_shadow):
            fg_dil = ndimage.binary_dilation(~fg_mask, iterations=3)
            if np.any(purple_shadow & fg_dil):
                s_lab, _ = ndimage.label(purple_shadow)
                touch = set(int(v) for v in np.unique(s_lab[purple_shadow & fg_dil]) if v > 0)
                for lbl in touch:
                    fg_mask[s_lab == lbl] = False

    # Keep primary body + meaningful detached elements (>= 0.6% of largest component)
    lbl, n_c = ndimage.label(fg_mask)
    if n_c > 1:
        sizes = ndimage.sum(fg_mask, lbl, range(1, n_c + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(36.0, max_sz * 0.006):
                keep |= (lbl == idx)
        fg_mask = keep

    # Despill 6px rim around outer silhouette AND punched interior pockets
    inner_clean = ndimage.binary_erosion(fg_mask, iterations=6) & (mag_dom <= 10)
    rim = fg_mask & (~inner_clean)
    rim_bad = rim & (mag_dom > 12) & (r > 105) & (b > 105)
    if np.any(rim_bad) and np.any(inner_clean):
        _, idx = ndimage.distance_transform_edt(~inner_clean, return_indices=True)
        ry, rx = np.where(rim_bad)
        rgb[ry, rx] = rgb[idx[0, ry, rx], idx[1, ry, rx]]

    return rgb, fg_mask


def downscale_unit_to_128(
    rgb_clean: np.ndarray,
    fg_mask: np.ndarray,
    target_height_px: int,
    max_width_px: int,
    palette_rgb: np.ndarray,
) -> np.ndarray:
    """
    Downscale extracted unit via PixelOE to exact 64 px/m height inside 128x128 frame,
    anchored BottomCenter at y=125 (3px bottom pad), quantized strictly to palette_soc_sunlit_128.
    """
    ys, xs = np.where(fg_mask)
    if len(ys) == 0:
        raise RuntimeError("Empty foreground mask after chroma extraction")

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
    # Use 0.25 coverage threshold so thin bows, arrows, mace hafts, and sword tips survive 1024->88px downscale cleanly
    alpha_bin = mask_sm >= 0.25

    # Remove tiny isolated 1-3px dust specs while keeping valid detached elements >= 4px
    labeled, n_comp = ndimage.label(alpha_bin)
    if n_comp > 1:
        sizes = ndimage.sum(alpha_bin, labeled, range(1, n_comp + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        for idx, sz in enumerate(sizes, start=1):
            if sz < max(4.0, max_sz * 0.004):
                alpha_bin[labeled == idx] = False

    canvas = np.zeros((128, 128, 4), dtype=np.uint8)
    # BottomCenter anchor: lowest opaque pixel sits at y = 125 (2px bottom margin: rows 126, 127 empty)
    oy = 126 - fit_h
    if oy < 2:
        oy = 2
    ox = (128 - fit_w) // 2
    sub = canvas[oy : oy + fit_h, ox : ox + fit_w]
    sub[..., :3] = down_rgb
    sub[..., 3] = np.where(alpha_bin, 255, 0).astype(np.uint8)
    sub[~alpha_bin, :3] = 0

    # Shift down/up so exact lowest opaque pixel sits at y=125
    ys_f = np.where(canvas[..., 3] == 255)[0]
    if len(ys_f) > 0:
        dy = 125 - int(ys_f.max())
        if dy != 0:
            shifted = np.zeros_like(canvas)
            if dy > 0:
                shifted[dy:] = canvas[: 128 - dy]
            else:
                s = -dy
                shifted[: 128 - s] = canvas[s:]
            canvas = shifted

    quant = pp.apply_sunlit_grade_and_quantize(
        canvas,
        palette_rgb,
        dither_strength=0.05,
        grade_strength=0.14,
        shadow_lift=4.5,
        vibrance_boost=1.08,
        preserve_alpha=False,
    )
    # Final 128x128 guard: despilling any tiny remaining magenta pixel against clean interior neighbor
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
# Archetype-Aware 4-Frame Idle Strip Synthesis (Zero Flicker)
# ---------------------------------------------------------------------------

def synthesize_unit_idle_frames(f0_rgba: np.ndarray, spec: UnitSpec) -> List[np.ndarray]:
    """
    Synthesize 4 crisp 128x128 idle animation frames from f0 with anchored feet (y=125)
    and zero identity flicker.
    """
    h, w, _ = f0_rgba.shape
    fg = f0_rgba[..., 3] == 255
    ys = np.where(fg)[0]
    if len(ys) == 0:
        return [f0_rgba.copy() for _ in range(4)]
    y_top, y_bot = int(ys.min()), int(ys.max())
    span_h = max(8, y_bot - y_top + 1)

    if spec.archetype == "frog":
        mid_y = y_top + int(span_h * 0.48)
        frames = []
        for fidx in range(4):
            if fidx == 0:
                frames.append(f0_rgba.copy())
                continue
            cur = f0_rgba.copy()
            if fidx in (1, 2):
                cur[: mid_y - 1] = f0_rgba[1:mid_y]
                cur[mid_y - 1] = f0_rgba[mid_y - 1]
            if fidx == 2:
                # Throat sac puffs 1px downward on front-lower jaw area
                t0 = y_top + int(span_h * 0.52)
                t1 = min(h - 2, y_bot - 4)
                if t1 > t0:
                    x_limit = int(w * 0.65)
                    patch = cur[t0 + 1 : t1 + 1, :x_limit]
                    src = f0_rgba[t0:t1, :x_limit]
                    cur[t0 + 1 : t1 + 1, :x_limit] = np.where(patch[..., 3:4] == 0, src, patch)
            frames.append(cur)
        return frames

    if spec.archetype == "rat":
        # Low hunched rat: subtle upper back breathe + curled tail-tip flick on trailing right side
        mid_y = y_top + int(span_h * 0.52)
        xs = np.where(fg)[1]
        x_min, x_max = int(xs.min()), int(xs.max())
        tail_x0 = x_min + int((x_max - x_min) * 0.68)
        frames = []
        for fidx in range(4):
            if fidx == 0:
                frames.append(f0_rgba.copy())
                continue
            cur = f0_rgba.copy()
            if fidx in (1, 2):
                # Subtle 1px spine/head lift on front 68%
                cur[: mid_y - 1, :tail_x0] = f0_rgba[1:mid_y, :tail_x0]
                cur[mid_y - 1, :tail_x0] = f0_rgba[mid_y - 1, :tail_x0]
            if fidx in (2, 3):
                # Tail tip lifts 1px on trailing right region
                t_y0 = max(1, y_top)
                t_y1 = min(h, y_bot - 4)
                if t_y1 > t_y0:
                    cur[t_y0 - 1 : t_y1 - 1, tail_x0:] = f0_rgba[t_y0:t_y1, tail_x0:]
            frames.append(cur)
        return frames

    # Biped / Magic Biped: chest/head inhale (+1px) + subtle cloak/back flutter + magic crystal pulse
    torso_split = y_top + int(span_h * 0.62)
    frames = []
    for fidx in range(4):
        if fidx == 0:
            frames.append(f0_rgba.copy())
            continue
        cur = f0_rgba.copy()
        if fidx in (1, 2) and torso_split > 2:
            cur[: torso_split - 1] = f0_rgba[1:torso_split]
            # Bridge junction row seamlessly so no horizontal tear appears
            cur[torso_split - 1] = f0_rgba[torso_split - 1]
        if fidx in (2, 3):
            b0 = y_top + int(span_h * 0.45)
            b1 = y_top + int(span_h * 0.84)
            if spec.facing == "East":
                # Trailing back edge is on the left side
                row_slice = cur[b0:b1, :-1].copy()
                cur[b0:b1, 1:] = np.where(
                    (np.arange(w)[None, 1:, None] < w * 0.44) & (row_slice[..., 3:4] == 255),
                    row_slice,
                    cur[b0:b1, 1:],
                )
            else:
                # Trailing back edge is on the right side
                row_slice = cur[b0:b1, 1:].copy()
                cur[b0:b1, :-1] = np.where(
                    (np.arange(w)[None, :-1, None] > w * 0.56) & (row_slice[..., 3:4] == 255),
                    row_slice,
                    cur[b0:b1, :-1],
                )
        if spec.archetype == "magic_biped" and fidx == 2:
            # Subtle magic arcane glow pulse: boost cyan/gold bright pixels by 1 palette step
            op = cur[..., 3] == 255
            cyan = op & (cur[..., 2] > cur[..., 0] + 40) & (cur[..., 1] > 140)
            cur[cyan, 0] = np.minimum(255, cur[cyan, 0].astype(np.int32) + 45).astype(np.uint8)
            cur[cyan, 1] = np.minimum(255, cur[cyan, 1].astype(np.int32) + 25).astype(np.uint8)
            cur[cyan, 2] = np.minimum(255, cur[cyan, 2].astype(np.int32) + 15).astype(np.uint8)
        frames.append(cur)

    # Re-quantize magic_biped frame 2 cleanly to guarantee 0 off-palette colors
    if spec.archetype == "magic_biped":
        pal = pp.load_palette(PALETTE_PATH)
        frames[2] = pp.apply_sunlit_grade_and_quantize(
            frames[2], pal, dither_strength=0.0, grade_strength=0.0, shadow_lift=0.0, vibrance_boost=1.0, preserve_alpha=False
        )
    return frames


# ---------------------------------------------------------------------------
# Strict Automated Unit QA Gates
# ---------------------------------------------------------------------------

def check_unit_qa(f0_rgba: np.ndarray, strip_rgba: np.ndarray, stats: dict, spec: UnitSpec) -> Tuple[bool, str]:
    op0 = f0_rgba[..., 3] == 255
    n_op0 = int(np.sum(op0))
    if n_op0 < 950:
        return False, f"f0 foreground too small ({n_op0} < 950 px)"
    if stats["off_palette_colors"] != 0:
        return False, f"off_palette_colors={stats['off_palette_colors']}"
    if not stats["alpha_is_1bit"]:
        return False, "alpha is not strictly 1-bit"

    # Reject any remaining true magenta/pink bleed in foreground (allow deep slate-plum shadow #2E1940/#482763)
    op_s = strip_rgba[..., 3] == 255
    r_c = strip_rgba[op_s, 0].astype(np.int16)
    g_c = strip_rgba[op_s, 1].astype(np.int16)
    b_c = strip_rgba[op_s, 2].astype(np.int16)
    mag_leaks = int(np.sum(((np.minimum(r_c, b_c) - g_c) > 42) & (r_c > 125) & (b_c > 125)))
    if mag_leaks > 0:
        return False, f"magenta_bleed_pixels={mag_leaks}"

    max_sq = stats["flat_blob_max_square"]
    if max_sq >= 10:
        return False, f"flat_blob_max_square={max_sq} (>= 10)"
    if stats["unique_palette_colors"] < 14:
        return False, f"too few palette colors ({stats['unique_palette_colors']})"

    ys, xs = np.where(op0)
    bh = int(ys.max() - ys.min() + 1)
    bw = int(xs.max() - xs.min() + 1)
    if abs(bh - spec.target_height_px) > 10:
        return False, f"height {bh}px drifted from target {spec.target_height_px}px"
    if bw > spec.max_width_px + 4:
        return False, f"width {bw}px exceeds max {spec.max_width_px}px"
    if int(ys.max()) < 123 or int(ys.max()) > 126:
        return False, f"bottom anchor y={int(ys.max())} not near 125"

    return True, (
        f"size={bw}x{bh}px colors={stats['unique_palette_colors']} op0={n_op0} "
        f"max_sq={max_sq} mag_bleed=0"
    )


def prepare_soft_guides(spec: UnitSpec) -> Tuple[Path, Path]:
    """
    Soften rigid Blender box edges inside the chroma foreground (keeping #FF00FF pure outside)
    and lightly smooth the depth map so SDXL paints organic Songs of Conquest pixel anatomy
    while preserving exact pose, facing, scale, and color layout.
    """
    init_path = GUIDE_DIR / f"{spec.unit_id}_chroma_init.png"
    depth_path = GUIDE_DIR / f"{spec.unit_id}_depth.png"
    if not init_path.is_file() or not depth_path.is_file():
        raise FileNotFoundError(f"Missing guide for {spec.unit_id}: {init_path} / {depth_path}")

    arr = np.array(Image.open(init_path).convert("RGB"), dtype=np.uint8)
    r, g, b = arr[..., 0].astype(np.int16), arr[..., 1].astype(np.int16), arr[..., 2].astype(np.int16)
    is_mag = (r > 210) & (b > 210) & (g < 45)
    fg = ~is_mag
    fg_f = fg.astype(np.float32)
    denom = cv2.GaussianBlur(fg_f, (0, 0), sigmaX=5.5, sigmaY=5.5)
    blurred = np.empty_like(arr, dtype=np.float32)
    for c in range(3):
        num = cv2.GaussianBlur(arr[..., c].astype(np.float32) * fg_f, (0, 0), sigmaX=5.5, sigmaY=5.5)
        blurred[..., c] = np.where(denom > 1e-4, num / np.maximum(denom, 1e-4), 0.0)
    soft_init = np.full_like(arr, (255, 0, 255), dtype=np.uint8)
    soft_init[fg] = np.clip(np.round(blurred[fg]), 0, 255).astype(np.uint8)
    p_soft_init = GUIDE_DIR / f"{spec.unit_id}_soft_init.png"
    Image.fromarray(soft_init, "RGB").save(p_soft_init, "PNG")

    d_arr = np.array(Image.open(depth_path).convert("L"), dtype=np.uint8)
    d_soft = cv2.GaussianBlur(d_arr, (0, 0), sigmaX=4.0, sigmaY=4.0)
    p_soft_depth = GUIDE_DIR / f"{spec.unit_id}_soft_depth.png"
    Image.fromarray(d_soft, "L").save(p_soft_depth, "PNG")
    return p_soft_init, p_soft_depth


# ---------------------------------------------------------------------------
# Process & Reroll Single Unit
# ---------------------------------------------------------------------------

def generate_unit(
    spec: UnitSpec,
    ip_ref_remote: Optional[str],
    palette_rgb: np.ndarray,
    ip_weight_override: Optional[float] = None,
    denoise_override: Optional[float] = None,
    dry_run_postprocess: bool = False,
) -> dict:
    init_path, depth_path = prepare_soft_guides(spec)
    canny_path = GUIDE_DIR / f"{spec.unit_id}_canny.png"

    out_dir = HEROES_OUT if spec.side == "hero" else MONSTERS_OUT
    out_dir.mkdir(parents=True, exist_ok=True)
    out_albedo = out_dir / f"{spec.unit_id}_sheet.png"
    out_normal = out_dir / f"{spec.unit_id}_sheet_n.png"

    ip_w = spec.ip_weight if ip_weight_override is None else ip_weight_override
    den = spec.denoise if denoise_override is None else denoise_override

    if not dry_run_postprocess:
        init_rem = upload_image(init_path, f"combat_init_{spec.unit_id}.png")
        depth_rem = upload_image(depth_path, f"combat_depth_{spec.unit_id}.png")
        canny_rem = upload_image(canny_path, f"combat_canny_{spec.unit_id}.png") if canny_path.is_file() else None

    last_reason = "no attempts"
    last_stats: dict = {}

    for attempt, seed in enumerate(spec.seeds, start=1):
        raw_path = RAW_DIR / f"{spec.unit_id}_a{attempt}.png"
        if dry_run_postprocess and raw_path.is_file():
            dt, peak_vram = 0.0, query_vram_mib()
        else:
            wf = build_unit_workflow(
                spec=spec,
                seed=seed,
                init_remote=init_rem,
                depth_remote=depth_rem,
                canny_remote=canny_rem,
                ip_ref_remote=ip_ref_remote,
                ip_weight=ip_w,
                denoise=den,
                depth_strength=spec.depth_strength,
                canny_strength=spec.canny_strength,
            )
            img_bytes, dt, peak_vram = run_comfy_prompt(wf)
            raw_path.parent.mkdir(parents=True, exist_ok=True)
            raw_path.write_bytes(img_bytes)

        raw_arr = np.array(Image.open(raw_path).convert("RGBA"), dtype=np.uint8)
        rgb_clean, fg_mask = extract_clean_magenta_unit(raw_arr)
        f0_rgba = downscale_unit_to_128(
            rgb_clean,
            fg_mask,
            target_height_px=spec.target_height_px,
            max_width_px=spec.max_width_px,
            palette_rgb=palette_rgb,
        )
        frames = synthesize_unit_idle_frames(f0_rgba, spec)
        strip_rgba = np.concatenate(frames, axis=1)  # 512x128

        norm_frames = []
        backend_used = "sobel"
        for fr in frames:
            nf, b_used = pp.generate_normal_map(fr, periodic=False, normal_backend="auto")
            norm_frames.append(nf)
            backend_used = b_used
        strip_norm = np.concatenate(norm_frames, axis=1)

        stats = pp.validate_outputs(strip_rgba, strip_norm, palette_rgb, is_tile=False, asset_kind="unit")
        stats["elapsed_s"] = dt
        stats["peak_vram_mib"] = peak_vram
        stats["normal_backend"] = backend_used
        stats["seed"] = seed

        ok, msg = check_unit_qa(f0_rgba, strip_rgba, stats, spec)
        last_stats = stats
        last_reason = msg
        print(f"[{spec.unit_id}] attempt {attempt}/{len(spec.seeds)} seed={seed} ({dt:.1f}s, {peak_vram} MiB) -> ok={ok}: {msg}", flush=True)
        if ok:
            ys0, xs0 = np.where(f0_rgba[..., 3] == 255)
            stats["pixel_width"] = int(xs0.max() - xs0.min() + 1)
            stats["pixel_height"] = int(ys0.max() - ys0.min() + 1)
            stats["albedo_path"] = str(out_albedo)
            stats["normal_path"] = str(out_normal)
            Image.fromarray(strip_rgba, "RGBA").save(out_albedo, "PNG")
            Image.fromarray(strip_norm, "RGBA").save(out_normal, "PNG")
            return stats

    raise RuntimeError(f"Unit {spec.unit_id} failed QA after {len(spec.seeds)} seeds: {last_reason} | last={last_stats}")


# ---------------------------------------------------------------------------
# Unified Battle Lineup Proof Sheet & Manifest Builder
# ---------------------------------------------------------------------------

def build_unified_lineup_proof() -> Path:
    """
    Build C:/Users/karti/realmz/.scratch/realmz_combat_art_pack/unified_roster_lineup.png
    placing ALL 6 heroes on the left facing East/Right vs ALL 7 enemies on the right facing West/Left
    across tiled Cartography meadow_grass.png turf at exact 1:1 relative scale (3x nearest-neighbor zoom
    for clear inspection, keeping 100% identical relative scaling across all 13 units!).
    """
    zoom = 3
    frame_z = 128 * zoom  # 384px per unit cell height; overlap rows slightly for natural tactical formation
    canvas_w = 1860
    canvas_h = 1040

    bg = Image.new("RGB", (canvas_w, canvas_h), (24, 30, 38))
    # Tile real Cartography meadow_grass.png at 3x nearest zoom across the arena floor
    if MEADOW_TILE_PATH.is_file():
        tile_im = Image.open(MEADOW_TILE_PATH).convert("RGB")
        tile_z = tile_im.resize((128 * zoom, 128 * zoom), Image.NEAREST)
        for ty in range(72, canvas_h - 40, tile_z.height):
            for tx in range(24, canvas_w - 24, tile_z.width):
                bg.paste(tile_z, (tx, ty))
    draw = ImageDraw.Draw(bg)
    # Dark slate header & footer
    draw.rectangle([0, 0, canvas_w, 72], fill=(20, 24, 34))
    draw.rectangle([0, canvas_h - 40, canvas_w, canvas_h], fill=(20, 24, 34))
    draw.line([(canvas_w // 2, 76), (canvas_w // 2, canvas_h - 44)], fill=(236, 169, 56), width=3)
    draw.text(
        (28, 16),
        "REALMZ UNIFIED 64 PX/M COMBAT ROSTER LINEUP (1:1 Relative Scale @ 3x Zoom on Cartography Meadow Turf)",
        fill=(255, 201, 82),
    )
    draw.text(
        (28, 42),
        "HEROES (Facing East / Right ->)",
        fill=(138, 224, 245),
    )
    draw.text(
        (canvas_w // 2 + 28, 42),
        "<- BYWATER ENEMIES (Facing West / Left)",
        fill=(255, 140, 130),
    )

    heroes = [u for u in UNIT_SPECS if u.side == "hero"]
    monsters = [u for u in UNIT_SPECS if u.side == "monster"]

    # Place 6 heroes in 2 tactical ranks on the left (3 per row, ample margin from edges)
    hero_slots = [
        (215, 430), (465, 430), (715, 430),
        (215, 845), (465, 845), (715, 845),
    ]
    # Place 7 monsters in 2 tactical ranks on the right (4 top, 3 bottom)
    monster_slots = [
        (1090, 430), (1295, 430), (1500, 430), (1705, 430),
        (1170, 845), (1405, 845), (1640, 845),
    ]

    def paste_unit(spec: UnitSpec, cx: int, base_y: int) -> None:
        sub = HEROES_OUT if spec.side == "hero" else MONSTERS_OUT
        sp = sub / f"{spec.unit_id}_sheet.png"
        if not sp.is_file():
            return
        strip = Image.open(sp).convert("RGBA")
        f0 = strip.crop((0, 0, 128, 128))
        ys = np.where(np.array(f0)[..., 3] == 255)[0]
        ph = int(ys.max() - ys.min() + 1) if len(ys) else 0
        f0_z = f0.resize((128 * zoom, 128 * zoom), Image.NEAREST)
        ox = cx - (128 * zoom) // 2
        oy = base_y - (128 * zoom)
        # Subtle ground anchor tick so feet position is obvious
        draw.ellipse([cx - 42, base_y - 16, cx + 42, base_y + 4], outline=(255, 224, 125) if spec.side == "hero" else (245, 118, 115), width=2)
        bg.paste(f0_z, (ox, oy), f0_z)
        lbl = f"{spec.display_name} [{ph}px]"
        draw.rectangle([cx - 92, base_y + 10, cx + 92, base_y + 34], fill=(20, 24, 34), outline=(74, 86, 114))
        draw.text((cx - 84, base_y + 16), lbl, fill=(240, 236, 228))

    for spec, (cx, by) in zip(heroes, hero_slots):
        paste_unit(spec, cx, by)
    for spec, (cx, by) in zip(monsters, monster_slots):
        paste_unit(spec, cx, by)

    draw.text(
        (28, canvas_h - 28),
        "All 13 units rendered on palette_soc_sunlit_128.json | 4-frame 512x128 strips + Laigter +Y normals | Zero ground shadow bleed",
        fill=(186, 198, 218),
    )

    out_path = PACK_ROOT / "unified_roster_lineup.png"
    bg.save(out_path, "PNG")
    return out_path


def write_manifest(unit_stats: Dict[str, dict]) -> Path:
    manifest = {
        "pack_id": "realmz_unified_combat_art_pack_v1",
        "standard": "Songs of Conquest Sunlit 128 / Realmz Cartography 64 px/m",
        "palette": str(PALETTE_PATH),
        "ppu": 64,
        "frame_size": [128, 128],
        "strip_size": [512, 128],
        "frame_count": 4,
        "anim_fps": 5.5,
        "anchor": "BottomCenter",
        "pivot": [0.5, 0.0],
        "normal_convention": "OpenGL +Y up (#8080FF neutral)",
        "heroes": [],
        "monsters": [],
        "icons": [],
    }
    for spec in UNIT_SPECS:
        sub = "heroes" if spec.side == "hero" else "monsters"
        albedo_rel = f"{sub}/{spec.unit_id}_sheet.png"
        normal_rel = f"{sub}/{spec.unit_id}_sheet_n.png"
        st = unit_stats.get(spec.unit_id, {})
        entry = {
            "id": spec.unit_id,
            "display_name": spec.display_name,
            "legacy_aliases": spec.legacy_aliases,
            "albedo": albedo_rel,
            "normal": normal_rel,
            "frame_size": [128, 128],
            "strip_size": [512, 128],
            "frame_count": 4,
            "ppu": 64,
            "anchor": "BottomCenter",
            "pivot": [0.5, 0.0],
            "facing": spec.facing,
            "pixel_width": st.get("pixel_width"),
            "pixel_height": st.get("pixel_height"),
            "unique_palette_colors": st.get("unique_palette_colors"),
            "off_palette_colors": st.get("off_palette_colors", 0),
            "normal_backend": st.get("normal_backend", "laigter"),
        }
        if spec.side == "hero":
            manifest["heroes"].append(entry)
        else:
            manifest["monsters"].append(entry)

    icon_names = [
        "icon_sword", "icon_bow", "icon_dagger", "icon_staff", "icon_fireball",
        "icon_flame_hands", "icon_magic_darts", "icon_heal", "icon_buff", "icon_daze",
        "icon_shove", "icon_potion", "icon_guard", "icon_undo", "icon_end_turn",
        "icon_auto_combat", "icon_speed", "icon_audio", "icon_grid", "icon_settings",
        "icon_flee",
    ]
    for iname in icon_names:
        ip = ICONS_OUT / f"{iname}.png"
        if ip.is_file():
            manifest["icons"].append(
                {
                    "id": iname,
                    "path": f"icons/{iname}.png",
                    "size": [32, 32],
                    "ppu": 32,
                    "anchor": "Center",
                    "pivot": [0.5, 0.5],
                }
            )

    m_path = OUTPUT_DIR / "roster_pack_manifest.json"
    m_path.parent.mkdir(parents=True, exist_ok=True)
    m_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return m_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Realmz Unified 64 px/m Combat Roster Pipeline")
    parser.add_argument("--only", nargs="+", default=None, help="Specific unit IDs to generate")
    parser.add_argument("--no-ipadapter", action="store_true", help="Disable IP-Adapter style reference")
    parser.add_argument("--ip-weight", type=float, default=None, help="Override IP-Adapter weight")
    parser.add_argument("--denoise", type=float, default=None, help="Override denoise")
    parser.add_argument("--postprocess-only", action="store_true", help="Reuse existing raw PNGs without ComfyUI")
    parser.add_argument("--keep-comfy", action="store_true", help="Keep ComfyUI running after script exits")
    parser.add_argument("--proof-only", action="store_true", help="Only rebuild lineup proof & manifest")
    args = parser.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    report_path = PACK_ROOT / "generation_report.json"
    report: Dict[str, dict] = {}
    if report_path.is_file():
        try:
            report = json.loads(report_path.read_text(encoding="utf-8"))
        except Exception:
            report = {}

    if not args.proof_only:
        specs = [SPEC_BY_ID[u] for u in args.only] if args.only else list(UNIT_SPECS)
        palette_rgb = load_unit_palette(PALETTE_PATH)
        spawned_proc = None
        if not args.postprocess_only:
            spawned_proc = ensure_comfy_running()

        try:
            ip_remote = None
            if not args.postprocess_only and not args.no_ipadapter:
                style_p = build_soc_style_ref()
                if style_p is not None:
                    ip_remote = upload_image(style_p, "soc_arleon_style_magenta.png")

            for spec in specs:
                st = generate_unit(
                    spec,
                    ip_ref_remote=ip_remote,
                    palette_rgb=palette_rgb,
                    ip_weight_override=args.ip_weight,
                    denoise_override=args.denoise,
                    dry_run_postprocess=args.postprocess_only,
                )
                report[spec.unit_id] = st
                report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        finally:
            if not args.postprocess_only and not args.keep_comfy:
                teardown_comfy(spawned_proc, force_kill_any=True)

    lineup_p = build_unified_lineup_proof()
    manifest_p = write_manifest(report)
    print(f"[Lineup] {lineup_p}", flush=True)
    print(f"[Manifest] {manifest_p}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
