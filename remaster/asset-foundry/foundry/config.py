"""Configuration, paths, category defaults, and GPU VRAM telemetry for Realmz Asset Foundry."""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path
from typing import Any

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0

REALMZ_ROOT = Path(os.environ.get("REALMZ_ROOT", r"C:\realmz"))
FOUNDRY_CODE_DIR = REALMZ_ROOT / "remaster" / "asset-foundry"
FOUNDRY_TOOLS_DIR = REALMZ_ROOT / "tools" / "asset-foundry"
FOUNDRY_VENV_PY = FOUNDRY_TOOLS_DIR / ".venv" / "Scripts" / "python.exe"
JOBS_DIR = FOUNDRY_TOOLS_DIR / "jobs"
HF_CACHE_DIR = Path(os.environ.get("HF_HOME", str(FOUNDRY_TOOLS_DIR / "hf_cache")))

BLENDER_EXE = Path(
    os.environ.get(
        "REALMZ_BLENDER_EXE",
        str(REALMZ_ROOT / "tools" / "blender" / "blender-4.5.9-windows-x64" / "blender.exe"),
    )
)
HUNYUAN_REPO_DIR = REALMZ_ROOT / "tools" / "repos" / "Hunyuan3D-2"
UNIRIG_REPO_DIR = REALMZ_ROOT / "tools" / "repos" / "UniRig"

UNITY_PROJECT_DIR = REALMZ_ROOT / "remaster" / "RealmzUnity"
UNITY_INBOX_DIR = UNITY_PROJECT_DIR / "Assets" / "Inbox"
UNITY_PREFABS_DIR = UNITY_PROJECT_DIR / "Assets" / "Prefabs" / "Foundry"
UNITY_ART_M1_DIR = UNITY_PROJECT_DIR / "Assets" / "Art" / "M1"
UNITY_CLI_EXE = Path(r"C:\Users\karti\AppData\Local\Unity\bin\unity.exe")

PALETTE_SOC_128 = REALMZ_ROOT / "remaster" / "art-pipeline" / "palette_soc_sunlit_128.json"
LAIGTER_EXE = Path(r"C:\ComfyUI\tools\laigter\laigter-windows\laigter.exe")

VALID_CATEGORIES = ("Hero", "Monster", "Prop", "Building", "Tile")
VALID_RIG_PROFILES = ("Auto", "Humanoid", "Quadruped", "Aquatic", "Static")
VALID_MODES = ("Both", "3D", "HD2D")

CATEGORY_DEFAULTS: dict[str, dict[str, Any]] = {
    "Hero": {
        "scale_m": 1.80,
        "target_tris": 6500,
        "rig": "Humanoid",
        "hd2d_size": 128,
        "hd2d_ppu": 128,
    },
    "Monster": {
        "scale_m": 1.35,
        "target_tris": 6500,
        "rig": "Auto",
        "hd2d_size": 128,
        "hd2d_ppu": 128,
    },
    "Prop": {
        "scale_m": 1.00,
        "target_tris": 3500,
        "rig": "Static",
        "hd2d_size": 128,
        "hd2d_ppu": 128,
    },
    "Building": {
        "scale_m": 4.50,
        "target_tris": 4500,
        "rig": "Static",
        "hd2d_size": 256,
        "hd2d_ppu": 256,
    },
    "Tile": {
        "scale_m": 1.00,
        "target_tris": 2000,
        "rig": "Static",
        "hd2d_size": 128,
        "hd2d_ppu": 128,
    },
}

AQUATIC_KEYWORDS = (
    "fish",
    "salmon",
    "trout",
    "carp",
    "shark",
    "eel",
    "whale",
    "dolphin",
    "octopus",
    "squid",
    "mermaid",
    "siren",
    "aquatic",
)

QUADRUPED_KEYWORDS = (
    "frog",
    "toad",
    "rat",
    "wolf",
    "dog",
    "hound",
    "bear",
    "boar",
    "spider",
    "horse",
    "deer",
    "stag",
    "cat",
    "lion",
    "tiger",
    "quadruped",
    "beetle",
)

HUMANOID_KEYWORDS = (
    "hero",
    "knight",
    "cleric",
    "mage",
    "paladin",
    "ranger",
    "rogue",
    "goblin",
    "kobold",
    "skeleton",
    "zombie",
    "bandit",
    "orc",
    "warrior",
    "humanoid",
)


def sanitize_ascii(text: str) -> str:
    """Enforce strictly zero em-dashes or en-dashes."""
    return text.replace("\u2014", " - ").replace("\u2013", "-")


def sanitize_asset_id(raw_name: str) -> str:
    """Normalize an asset name into a safe lowercase identifier."""
    cleaned = re.sub(r"[^a-zA-Z0-9_-]+", "_", raw_name.strip().lower()).strip("_-")
    if not cleaned:
        raise ValueError("Asset name cannot be empty after sanitization.")
    return cleaned


def normalize_category(category: str) -> str:
    for item in VALID_CATEGORIES:
        if item.lower() == category.strip().lower():
            return item
    raise ValueError(f"Unsupported category '{category}'. Expected one of: {', '.join(VALID_CATEGORIES)}")


def normalize_mode(mode: str) -> str:
    m = mode.strip().lower()
    if m == "both":
        return "Both"
    if m in ("3d", "stylized3d", "stylized_3d"):
        return "3D"
    if m in ("hd2d", "hd-2d", "2d", "sprite"):
        return "HD2D"
    raise ValueError(f"Unsupported output mode '{mode}'. Expected one of: {', '.join(VALID_MODES)}")


def infer_rig_profile(category: str, name: str, requested_rig: str) -> str:
    req = requested_rig.strip().lower()
    if req != "auto":
        for item in VALID_RIG_PROFILES:
            if item.lower() == req:
                return item
        raise ValueError(f"Unsupported rig profile '{requested_rig}'.")

    lower_name = name.lower()
    if any(k in lower_name for k in AQUATIC_KEYWORDS):
        return "Aquatic"
    if any(k in lower_name for k in QUADRUPED_KEYWORDS):
        return "Quadruped"
    if category == "Hero" or any(k in lower_name for k in HUMANOID_KEYWORDS):
        return "Humanoid"
    if category == "Monster":
        return "Humanoid"
    return "Static"


def query_gpu_vram() -> dict[str, Any]:
    """Query RTX GPU VRAM usage safely via nvidia-smi without spawning a console window."""
    try:
        res = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=name,memory.used,memory.total,memory.free",
                "--format=csv,noheader,nounits",
            ],
            capture_output=True,
            text=True,
            timeout=5,
            creationflags=CREATE_NO_WINDOW,
        )
        if res.returncode == 0 and res.stdout.strip():
            parts = [p.strip() for p in res.stdout.strip().splitlines()[0].split(",")]
            if len(parts) >= 4:
                return {
                    "gpu": parts[0],
                    "used_mib": int(parts[1]),
                    "total_mib": int(parts[2]),
                    "free_mib": int(parts[3]),
                }
    except Exception:
        pass
    return {"gpu": "unknown", "used_mib": 0, "total_mib": 0, "free_mib": 0}
