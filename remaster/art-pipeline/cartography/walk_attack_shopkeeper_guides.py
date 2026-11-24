"""
Headless Blender 4.5.9 Orthographic (26 deg pitch) Walk, Attack & Shopkeeper Guide Generator.

Produces 1024x1024 ControlNet Depth (*_depth.png), Canny (*_canny.png), and
solid #FF00FF magenta-background material-colored init guides (*_chroma_init.png)
into:
  c:/Users/karti/LifeTwin/scratch/realmz_walk_attack_shopkeepers/guides/

Assets generated:
  - 13 existing combat units:
      * <id>_idle_ref_{depth,canny,chroma_init}.png (baseline height calibration at same ortho_scale)
      * <id>_walk_f0..f3_{depth,canny,chroma_init}.png + <id>_walk_grid_{depth,canny,chroma_init}.png
      * <id>_attack_f0..f3_{depth,canny,chroma_init}.png + <id>_attack_grid_{depth,canny,chroma_init}.png
  - 4 Bywater shopkeeper NPCs (East-facing yaw=-28.0, ortho_scale=2.8):
      * <npc_id>_{depth,canny,chroma_init}.png
  - Contact sheets:
      * guides_walk_preview.png
      * guides_attack_preview.png
      * guides_npc_preview.png
  - Calibration metadata:
      * guides_metadata.json (includes exact world-(0,0,0) projection ground_x_px/ground_y_px,
        idle_f0_h_px from RealmzUnity unit sheets, idle_fg_height_px, and px_per_render_px)

Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW = 0x08000000 in all subprocesses.

Usage (via asset-foundry venv Python):
  C:/realmz/tools/asset-foundry/.venv/Scripts/python.exe walk_attack_shopkeeper_guides.py
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

CREATE_NO_WINDOW = 0x08000000
DEFAULT_BLENDER_EXE = Path(r"C:\realmz\tools\blender\blender-4.5.9-windows-x64\blender.exe")
DEFAULT_OUT_DIR = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_walk_attack_shopkeepers\guides")
UNITY_UNITS_DIR = Path(r"C:\realmz\remaster\RealmzUnity\Assets\Art\Cartography\units")
RENDER_RES = 1024
CAMERA_PITCH_DEG = 26.0
MAGENTA_BG = (255, 0, 255)


UNIT_SPECS: List[Dict[str, object]] = [
    # 6 Heroes (East/Right, yaw_deg = -32.0)
    {
        "id": "hero_knight",
        "label": "Knight (Aldric)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "melee_shield",
        "yaw_deg": -32.0,
        "ortho_scale": 3.0,
        "target": (0.28, -0.14, 1.12),
    },
    {
        "id": "hero_cleric",
        "label": "Cleric (Liora)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "cleric_smite",
        "yaw_deg": -32.0,
        "ortho_scale": 3.0,
        "target": (0.20, -0.12, 1.10),
    },
    {
        "id": "hero_ranger",
        "label": "Ranger (Kaelen)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "bow",
        "yaw_deg": -32.0,
        "ortho_scale": 2.8,
        "target": (0.22, -0.14, 1.00),
    },
    {
        "id": "hero_mage",
        "label": "Mage (Theron)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "mage_cast",
        "yaw_deg": -32.0,
        "ortho_scale": 2.8,
        "target": (0.18, -0.12, 1.06),
    },
    {
        "id": "hero_paladin",
        "label": "Paladin (Seraphina)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "greatsword",
        "yaw_deg": -32.0,
        "ortho_scale": 3.1,
        "target": (0.42, -0.22, 1.14),
    },
    {
        "id": "hero_rogue",
        "label": "Rogue (Vex)",
        "faction": "hero",
        "kind": "combat",
        "attack_style": "twin_dagger",
        "yaw_deg": -32.0,
        "ortho_scale": 2.8,
        "target": (0.14, -0.16, 0.90),
    },
    # 7 Monsters (West/Left, yaw_deg = +32.0)
    {
        "id": "monster_giant_frog",
        "label": "Giant Frog",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "frog",
        "yaw_deg": 32.0,
        "ortho_scale": 2.5,
        "target": (-0.06, -0.32, 0.44),
    },
    {
        "id": "monster_goblin_raider",
        "label": "Goblin Raider",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "melee_shield",
        "yaw_deg": 32.0,
        "ortho_scale": 2.7,
        "target": (-0.22, -0.18, 0.80),
    },
    {
        "id": "monster_goblin_archer",
        "label": "Goblin Archer",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "bow",
        "yaw_deg": 32.0,
        "ortho_scale": 2.5,
        "target": (-0.18, -0.18, 0.74),
    },
    {
        "id": "monster_orc_warrior",
        "label": "Orc Warrior",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "heavy_axe",
        "yaw_deg": 32.0,
        "ortho_scale": 3.3,
        "target": (-0.42, -0.20, 1.22),
    },
    {
        "id": "monster_giant_rat",
        "label": "Giant Rat",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "rat",
        "yaw_deg": 32.0,
        "ortho_scale": 2.5,
        "target": (-0.04, -0.06, 0.44),
    },
    {
        "id": "monster_skeleton_warrior",
        "label": "Skeleton Warrior",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "claw_shield",
        "yaw_deg": 32.0,
        "ortho_scale": 2.8,
        "target": (-0.16, -0.14, 1.00),
    },
    {
        "id": "monster_bandit_cutthroat",
        "label": "Bandit Cutthroat",
        "faction": "monster",
        "kind": "combat",
        "attack_style": "melee_dual",
        "yaw_deg": 32.0,
        "ortho_scale": 3.0,
        "target": (-0.26, -0.16, 1.08),
    },
]

NPC_SPECS: List[Dict[str, object]] = [
    {
        "id": "npc_blacksmith",
        "label": "Bywater Blacksmith",
        "faction": "npc",
        "kind": "npc",
        "yaw_deg": -28.0,
        "ortho_scale": 2.8,
        "target": (0.04, -0.04, 1.02),
        "target_height_px": 92,
    },
    {
        "id": "npc_tanner",
        "label": "Bywater Tanner",
        "faction": "npc",
        "kind": "npc",
        "yaw_deg": -28.0,
        "ortho_scale": 2.8,
        "target": (0.04, -0.04, 1.00),
        "target_height_px": 90,
    },
    {
        "id": "npc_apothecary",
        "label": "Bywater Apothecary",
        "faction": "npc",
        "kind": "npc",
        "yaw_deg": -28.0,
        "ortho_scale": 2.8,
        "target": (0.04, -0.04, 1.00),
        "target_height_px": 90,
    },
    {
        "id": "npc_priestess",
        "label": "Bywater Priestess",
        "faction": "npc",
        "kind": "npc",
        "yaw_deg": -28.0,
        "ortho_scale": 2.8,
        "target": (0.04, -0.04, 1.02),
        "target_height_px": 92,
    },
]


@dataclass
class BipedPose:
    hip_dx: float = 0.0
    hip_dy: float = 0.0
    hip_dz: float = 0.0
    torso_pitch: float = 0.0   # + tilts forward (-Y), - leans back
    torso_roll: float = 0.0
    torso_yaw: float = 0.0     # + turns toward +X (East), - turns toward -X (West)
    head_pitch: float = 0.0
    head_yaw: float = 0.0
    # Legs: thigh_fwd > 0 swings leg forward (-Y); knee_bend >= 0 bends shin back (+Y)
    l_thigh_fwd: float = 0.0
    l_knee_bend: float = 0.0
    l_thigh_splay: float = -4.0
    r_thigh_fwd: float = 0.0
    r_knee_bend: float = 0.0
    r_thigh_splay: float = 4.0
    # Arms: sh_fwd > 0 swings forward/up (-Y); sh_out > 0 splays out laterally; el_bend >= 0 bends forearm forward
    l_sh_fwd: float = 20.0
    l_sh_out: float = 16.0
    l_sh_yaw: float = 0.0
    l_el_bend: float = 28.0
    r_sh_fwd: float = 20.0
    r_sh_out: float = 16.0
    r_sh_yaw: float = 0.0
    r_el_bend: float = 28.0
    # Optional weapon extra pitch/roll/yaw at hand attachment
    weapon_rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)
    offhand_rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)
    weapon_world_mode: bool = False
    cape_trail_deg: float = 0.0
    prop_fx: str = ""
    arrow_fly_dist: float = 0.0


def _biped_walk_pose(frame: int, crouch: float = 0.0, stride_scale: float = 1.0) -> BipedPose:
    """4-frame alternating biped walk cycle with clear leg separation and hip bob."""
    c_fwd = 12.0 * crouch
    c_knee = 24.0 * crouch
    if frame == 0:
        # Contact A: Left leg forward, Right leg back, Right arm forward, Left arm back
        return BipedPose(
            hip_dy=-0.04,
            hip_dz=-0.04,
            torso_pitch=6.0 + 10.0 * crouch,
            torso_yaw=8.0,
            head_pitch=-4.0 - 6.0 * crouch,
            head_yaw=-6.0,
            l_thigh_fwd=26.0 * stride_scale + c_fwd,
            l_knee_bend=10.0 + c_knee * 0.5,
            r_thigh_fwd=-24.0 * stride_scale + c_fwd * 0.4,
            r_knee_bend=18.0 + c_knee * 0.6,
            l_sh_fwd=-22.0,
            l_sh_out=14.0,
            l_el_bend=22.0,
            r_sh_fwd=38.0,
            r_sh_out=16.0,
            r_el_bend=38.0,
            cape_trail_deg=14.0,
        )
    if frame == 1:
        # Passing A: Left leg planted straight, Right knee lifted passing forward, hip bobs up
        return BipedPose(
            hip_dz=0.05,
            torso_pitch=4.0 + 9.0 * crouch,
            torso_yaw=0.0,
            head_pitch=-3.0 - 5.0 * crouch,
            l_thigh_fwd=2.0 + c_fwd * 0.5,
            l_knee_bend=4.0 + c_knee * 0.4,
            r_thigh_fwd=32.0 * stride_scale + c_fwd,
            r_knee_bend=48.0 + c_knee * 0.5,
            l_sh_fwd=8.0,
            l_sh_out=16.0,
            l_el_bend=30.0,
            r_sh_fwd=14.0,
            r_sh_out=18.0,
            r_el_bend=32.0,
            cape_trail_deg=8.0,
        )
    if frame == 2:
        # Contact B: Right leg forward, Left leg back, Left arm forward, Right arm back
        return BipedPose(
            hip_dy=-0.04,
            hip_dz=-0.04,
            torso_pitch=6.0 + 10.0 * crouch,
            torso_yaw=-8.0,
            head_pitch=-4.0 - 6.0 * crouch,
            head_yaw=6.0,
            l_thigh_fwd=-24.0 * stride_scale + c_fwd * 0.4,
            l_knee_bend=18.0 + c_knee * 0.6,
            r_thigh_fwd=26.0 * stride_scale + c_fwd,
            r_knee_bend=10.0 + c_knee * 0.5,
            l_sh_fwd=38.0,
            l_sh_out=16.0,
            l_el_bend=38.0,
            r_sh_fwd=-22.0,
            r_sh_out=14.0,
            r_el_bend=22.0,
            cape_trail_deg=14.0,
        )
    # frame == 3: Passing B: Right leg planted, Left knee lifted passing forward, hip bobs up
    return BipedPose(
        hip_dz=0.05,
        torso_pitch=4.0 + 9.0 * crouch,
        torso_yaw=0.0,
        head_pitch=-3.0 - 5.0 * crouch,
        l_thigh_fwd=32.0 * stride_scale + c_fwd,
        l_knee_bend=48.0 + c_knee * 0.5,
        r_thigh_fwd=2.0 + c_fwd * 0.5,
        r_knee_bend=4.0 + c_knee * 0.4,
        l_sh_fwd=14.0,
        l_sh_out=18.0,
        l_el_bend=32.0,
        r_sh_fwd=8.0,
        r_sh_out=16.0,
        r_el_bend=30.0,
        cape_trail_deg=8.0,
    )


def _mirror_pose_west(pose: BipedPose) -> BipedPose:
    """Mirror East-facing (+X primary) attack pose for West-facing (-X primary) units."""
    return BipedPose(
        hip_dx=-pose.hip_dx,
        hip_dy=pose.hip_dy,
        hip_dz=pose.hip_dz,
        torso_pitch=pose.torso_pitch,
        torso_roll=-pose.torso_roll,
        torso_yaw=-pose.torso_yaw,
        head_pitch=pose.head_pitch,
        head_yaw=-pose.head_yaw,
        l_thigh_fwd=pose.r_thigh_fwd,
        l_knee_bend=pose.r_knee_bend,
        l_thigh_splay=-pose.r_thigh_splay,
        r_thigh_fwd=pose.l_thigh_fwd,
        r_knee_bend=pose.l_knee_bend,
        r_thigh_splay=-pose.l_thigh_splay,
        l_sh_fwd=pose.r_sh_fwd,
        l_sh_out=pose.r_sh_out,
        l_sh_yaw=-pose.r_sh_yaw,
        l_el_bend=pose.r_el_bend,
        r_sh_fwd=pose.l_sh_fwd,
        r_sh_out=pose.l_sh_out,
        r_sh_yaw=-pose.l_sh_yaw,
        r_el_bend=pose.l_el_bend,
        weapon_rot=(pose.weapon_rot[0], -pose.weapon_rot[1], -pose.weapon_rot[2]),
        offhand_rot=(pose.offhand_rot[0], -pose.offhand_rot[1], -pose.offhand_rot[2]),
        weapon_world_mode=pose.weapon_world_mode,
        cape_trail_deg=pose.cape_trail_deg,
        prop_fx=pose.prop_fx,
        arrow_fly_dist=pose.arrow_fly_dist,
    )


def _attack_pose(style: str, facing_east: bool, frame: int, crouch: float = 0.0) -> BipedPose:
    """Build 4-frame attack poses assuming East (+X primary arm = R) then mirror if West."""
    c_fwd = 10.0 * crouch
    c_knee = 18.0 * crouch
    if style in ("melee_shield", "melee_dual", "heavy_axe", "greatsword", "claw_shield", "cleric_smite"):
        if frame == 0:
            # Windup: weapon raised high / pulled back, torso twisted away
            p = BipedPose(
                hip_dy=0.06,
                hip_dz=-0.03,
                torso_pitch=-8.0 + 12.0 * crouch,
                torso_yaw=-24.0,
                head_pitch=4.0,
                head_yaw=16.0,
                l_thigh_fwd=18.0 + c_fwd,
                l_knee_bend=22.0 + c_knee,
                r_thigh_fwd=-18.0,
                r_knee_bend=14.0 + c_knee,
                l_sh_fwd=34.0,
                l_sh_out=18.0,
                l_el_bend=42.0,
                r_sh_fwd=126.0 if style != "heavy_axe" else 132.0,
                r_sh_out=26.0,
                r_sh_yaw=-24.0,
                r_el_bend=46.0,
                weapon_rot=(-62.0, 14.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=6.0,
            )
        elif frame == 1:
            # Peak strike: torso lunged forward, weapon slammed forward/horizontal/diagonal-down
            fx = "sunburst" if style == "cleric_smite" else ("claws_extended" if style == "claw_shield" else "")
            p = BipedPose(
                hip_dx=0.08,
                hip_dy=-0.20,
                hip_dz=-0.07,
                torso_pitch=18.0 + 10.0 * crouch,
                torso_yaw=30.0,
                head_pitch=-12.0,
                head_yaw=-20.0,
                l_thigh_fwd=-28.0,
                l_knee_bend=12.0 + c_knee * 0.5,
                r_thigh_fwd=34.0 + c_fwd,
                r_knee_bend=34.0 + c_knee,
                l_sh_fwd=-16.0,
                l_sh_out=24.0,
                l_el_bend=26.0,
                r_sh_fwd=84.0,
                r_sh_out=8.0,
                r_sh_yaw=22.0,
                r_el_bend=8.0,
                weapon_rot=(82.0, 26.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=24.0,
                prop_fx=fx,
            )
        elif frame == 2:
            # Follow-through: weapon swung lower forward/across, cape/scarf flared
            p = BipedPose(
                hip_dx=0.04,
                hip_dy=-0.12,
                hip_dz=-0.05,
                torso_pitch=14.0 + 10.0 * crouch,
                torso_yaw=22.0,
                head_pitch=-8.0,
                head_yaw=-14.0,
                l_thigh_fwd=-20.0,
                l_knee_bend=16.0 + c_knee * 0.5,
                r_thigh_fwd=26.0 + c_fwd,
                r_knee_bend=28.0 + c_knee,
                l_sh_fwd=12.0,
                l_sh_out=22.0,
                l_el_bend=32.0,
                r_sh_fwd=38.0,
                r_sh_out=24.0,
                r_sh_yaw=28.0,
                r_el_bend=16.0,
                weapon_rot=(112.0, 28.0, 12.0),
                weapon_world_mode=True,
                cape_trail_deg=20.0,
            )
        else:
            # Guard Recover: pulling back toward ready stance
            p = BipedPose(
                hip_dy=-0.02,
                hip_dz=-0.02,
                torso_pitch=4.0 + 10.0 * crouch,
                torso_yaw=6.0,
                head_pitch=-2.0,
                head_yaw=-6.0,
                l_thigh_fwd=-10.0,
                l_knee_bend=12.0 + c_knee,
                r_thigh_fwd=14.0 + c_fwd,
                r_knee_bend=16.0 + c_knee,
                l_sh_fwd=24.0,
                l_sh_out=18.0,
                l_el_bend=34.0,
                r_sh_fwd=46.0,
                r_sh_out=18.0,
                r_sh_yaw=6.0,
                r_el_bend=34.0,
                weapon_rot=(22.0, 14.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=10.0,
            )
        if style == "greatsword" and frame in (0, 1):
            p.l_sh_fwd = p.r_sh_fwd - 10.0
            p.l_sh_out = -6.0
            p.l_el_bend = p.r_el_bend + 8.0
    elif style == "twin_dagger":
        if frame == 0:
            p = BipedPose(
                hip_dy=0.05,
                hip_dz=-0.05,
                torso_pitch=10.0,
                torso_yaw=-24.0,
                head_pitch=-6.0,
                head_yaw=14.0,
                l_thigh_fwd=22.0,
                l_knee_bend=28.0,
                r_thigh_fwd=-18.0,
                r_knee_bend=24.0,
                l_sh_fwd=-20.0,
                l_sh_out=22.0,
                l_el_bend=54.0,
                r_sh_fwd=118.0,
                r_sh_out=22.0,
                r_el_bend=52.0,
                weapon_rot=(-28.0, 14.0, 0.0),
                offhand_rot=(18.0, -14.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=14.0,
            )
        elif frame == 1:
            p = BipedPose(
                hip_dx=0.06,
                hip_dy=-0.20,
                hip_dz=-0.08,
                torso_pitch=24.0,
                torso_yaw=24.0,
                head_pitch=-16.0,
                head_yaw=-16.0,
                l_thigh_fwd=-28.0,
                l_knee_bend=16.0,
                r_thigh_fwd=34.0,
                r_knee_bend=38.0,
                l_sh_fwd=78.0,
                l_sh_out=14.0,
                l_el_bend=8.0,
                r_sh_fwd=86.0,
                r_sh_out=12.0,
                r_el_bend=6.0,
                weapon_rot=(82.0, 20.0, 0.0),
                offhand_rot=(78.0, -18.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=26.0,
            )
        elif frame == 2:
            p = BipedPose(
                hip_dx=0.04,
                hip_dy=-0.12,
                hip_dz=-0.05,
                torso_pitch=18.0,
                torso_yaw=14.0,
                head_pitch=-12.0,
                l_thigh_fwd=-18.0,
                l_knee_bend=20.0,
                r_thigh_fwd=24.0,
                r_knee_bend=30.0,
                l_sh_fwd=34.0,
                l_sh_out=32.0,
                l_el_bend=16.0,
                r_sh_fwd=38.0,
                r_sh_out=32.0,
                r_el_bend=16.0,
                weapon_rot=(110.0, 26.0, 0.0),
                offhand_rot=(110.0, -26.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=20.0,
            )
        else:
            p = BipedPose(
                hip_dz=-0.04,
                torso_pitch=14.0,
                torso_yaw=4.0,
                head_pitch=-10.0,
                l_thigh_fwd=12.0,
                l_knee_bend=24.0,
                r_thigh_fwd=-10.0,
                r_knee_bend=22.0,
                l_sh_fwd=38.0,
                l_sh_out=20.0,
                l_el_bend=38.0,
                r_sh_fwd=48.0,
                r_sh_out=20.0,
                r_el_bend=40.0,
                weapon_rot=(28.0, 14.0, 0.0),
                offhand_rot=(28.0, -14.0, 0.0),
                weapon_world_mode=True,
                cape_trail_deg=12.0,
            )
    elif style == "bow":
        if frame == 0:
            # Nock & Draw: bow raised forward, draw hand pulled back to cheek
            p = BipedPose(
                hip_dz=-0.03,
                torso_pitch=4.0 + 8.0 * crouch,
                torso_yaw=-14.0,
                head_yaw=10.0,
                l_thigh_fwd=14.0 + c_fwd,
                l_knee_bend=12.0 + c_knee,
                r_thigh_fwd=-14.0,
                r_knee_bend=14.0 + c_knee,
                r_sh_fwd=78.0,
                r_sh_out=14.0,
                r_el_bend=12.0,
                l_sh_fwd=66.0,
                l_sh_out=18.0,
                l_el_bend=82.0,
                prop_fx="bow_draw",
                arrow_fly_dist=0.0,
            )
        elif frame == 1:
            # Release: bow arm braced forward, draw hand snapped back, arrow flying 0.45m ahead
            p = BipedPose(
                hip_dy=-0.05,
                hip_dz=-0.02,
                torso_pitch=8.0 + 8.0 * crouch,
                torso_yaw=8.0,
                head_yaw=-6.0,
                l_thigh_fwd=18.0 + c_fwd,
                l_knee_bend=14.0 + c_knee,
                r_thigh_fwd=-16.0,
                r_knee_bend=12.0 + c_knee,
                r_sh_fwd=82.0,
                r_sh_out=12.0,
                r_el_bend=8.0,
                l_sh_fwd=-18.0,
                l_sh_out=26.0,
                l_el_bend=44.0,
                prop_fx="bow_release",
                arrow_fly_dist=0.45,
            )
        elif frame == 2:
            # Follow-Through: bow angled slightly down after recoil
            p = BipedPose(
                hip_dz=-0.02,
                torso_pitch=6.0 + 8.0 * crouch,
                torso_yaw=4.0,
                l_thigh_fwd=14.0 + c_fwd,
                l_knee_bend=12.0 + c_knee,
                r_thigh_fwd=-14.0,
                r_knee_bend=12.0 + c_knee,
                r_sh_fwd=56.0,
                r_sh_out=14.0,
                r_el_bend=18.0,
                l_sh_fwd=-8.0,
                l_sh_out=20.0,
                l_el_bend=36.0,
                weapon_rot=(22.0, 0.0, 0.0),
                prop_fx="bow_empty",
            )
        else:
            # Recover: lowering bow / reaching back to quiver
            p = BipedPose(
                hip_dz=-0.01,
                torso_pitch=2.0 + 8.0 * crouch,
                torso_yaw=-6.0,
                l_thigh_fwd=10.0 + c_fwd,
                l_knee_bend=10.0 + c_knee,
                r_thigh_fwd=-10.0,
                r_knee_bend=10.0 + c_knee,
                r_sh_fwd=44.0,
                r_sh_out=14.0,
                r_el_bend=22.0,
                l_sh_fwd=118.0,
                l_sh_out=18.0,
                l_el_bend=64.0,
                weapon_rot=(12.0, 0.0, 0.0),
                prop_fx="bow_empty",
            )
    else:
        # mage_cast
        if frame == 0:
            p = BipedPose(
                hip_dy=0.04,
                torso_pitch=-10.0,
                torso_yaw=-18.0,
                head_pitch=6.0,
                head_yaw=12.0,
                l_thigh_fwd=10.0,
                l_knee_bend=10.0,
                r_thigh_fwd=-10.0,
                r_knee_bend=10.0,
                l_sh_fwd=42.0,
                l_sh_out=22.0,
                l_el_bend=44.0,
                r_sh_fwd=118.0,
                r_sh_out=22.0,
                r_el_bend=28.0,
                weapon_rot=(-28.0, 10.0, 0.0),
            )
        elif frame == 1:
            p = BipedPose(
                hip_dx=0.05,
                hip_dy=-0.12,
                torso_pitch=14.0,
                torso_yaw=22.0,
                head_pitch=-8.0,
                head_yaw=-16.0,
                l_thigh_fwd=-14.0,
                l_knee_bend=10.0,
                r_thigh_fwd=22.0,
                r_knee_bend=18.0,
                l_sh_fwd=56.0,
                l_sh_out=18.0,
                l_el_bend=24.0,
                r_sh_fwd=78.0,
                r_sh_out=10.0,
                r_el_bend=8.0,
                weapon_rot=(72.0, 14.0, 0.0),
                prop_fx="mage_burst_big",
            )
        elif frame == 2:
            p = BipedPose(
                hip_dx=0.03,
                hip_dy=-0.06,
                torso_pitch=8.0,
                torso_yaw=12.0,
                l_thigh_fwd=-8.0,
                l_knee_bend=10.0,
                r_thigh_fwd=14.0,
                r_knee_bend=14.0,
                l_sh_fwd=34.0,
                l_sh_out=18.0,
                l_el_bend=30.0,
                r_sh_fwd=52.0,
                r_sh_out=14.0,
                r_el_bend=18.0,
                weapon_rot=(40.0, 8.0, 0.0),
                prop_fx="mage_burst_small",
            )
        else:
            p = BipedPose(
                torso_pitch=2.0,
                torso_yaw=2.0,
                l_thigh_fwd=6.0,
                l_knee_bend=8.0,
                r_thigh_fwd=-6.0,
                r_knee_bend=8.0,
                l_sh_fwd=26.0,
                l_sh_out=16.0,
                l_el_bend=32.0,
                r_sh_fwd=42.0,
                r_sh_out=16.0,
                r_el_bend=26.0,
                weapon_rot=(8.0, 4.0, 0.0),
            )

    return p if facing_east else _mirror_pose_west(p)


# ---------------------------------------------------------------------------
# Blender-side scene builders (executed inside blender.exe -b)
# ---------------------------------------------------------------------------

def _srgb_channel_to_linear(c: float) -> float:
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def _hex_to_linear(hex_str: str) -> Tuple[float, float, float]:
    h = hex_str.strip().lstrip("#")
    r = int(h[0:2], 16) / 255.0
    g = int(h[2:4], 16) / 255.0
    b = int(h[4:6], 16) / 255.0
    return (
        _srgb_channel_to_linear(r),
        _srgb_channel_to_linear(g),
        _srgb_channel_to_linear(b),
    )


def _blender_main(
    raw_dir: Path,
    selected_units: Sequence[str] | None = None,
    only_anims: Sequence[str] | None = None,
) -> None:
    import bmesh
    import bpy
    import numpy as np
    from mathutils import Euler, Vector

    projections: Dict[str, Dict[str, object]] = {}

    def clear_scene() -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def make_mat(name: str, hex_color: str) -> "bpy.types.Material":
        mat = bpy.data.materials.new(name=name)
        r, g, b = _hex_to_linear(hex_color)
        mat.diffuse_color = (r, g, b, 1.0)
        return mat

    def make_empty(
        name: str,
        loc: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        parent=None,
    ) -> "bpy.types.Object":
        emp = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(emp)
        emp.location = loc
        emp.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if parent is not None:
            emp.parent = parent
        return emp

    def add_bevel(obj: "bpy.types.Object", width: float = 0.02, segments: int = 1) -> None:
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = width
        mod.segments = segments
        mod.limit_method = "ANGLE"

    def set_smooth(obj: "bpy.types.Object", smooth: bool = True) -> None:
        for poly in obj.data.polygons:
            poly.use_smooth = smooth

    def add_box(
        name: str,
        size: Tuple[float, float, float],
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        mat=None,
        bevel: float = 0.015,
        parent=None,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
        obj = bpy.context.active_object
        obj.name = name
        obj.scale = (size[0], size[1], size[2])
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if mat:
            obj.data.materials.append(mat)
        if bevel > 0.0:
            add_bevel(obj, bevel, 1)
        if parent is not None:
            obj.parent = parent
        return obj

    def add_cyl(
        name: str,
        radius: float,
        depth: float,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        verts: int = 16,
        mat=None,
        smooth: bool = True,
        parent=None,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
        obj = bpy.context.active_object
        obj.name = name
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if smooth:
            set_smooth(obj, True)
        if mat:
            obj.data.materials.append(mat)
        if parent is not None:
            obj.parent = parent
        return obj

    def add_cone(
        name: str,
        radius1: float,
        radius2: float,
        depth: float,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        verts: int = 16,
        mat=None,
        smooth: bool = True,
        parent=None,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_cone_add(
            vertices=verts,
            radius1=radius1,
            radius2=radius2,
            depth=depth,
            location=loc,
        )
        obj = bpy.context.active_object
        obj.name = name
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if smooth:
            set_smooth(obj, True)
        if mat:
            obj.data.materials.append(mat)
        if parent is not None:
            obj.parent = parent
        return obj

    def add_sphere(
        name: str,
        radius: float,
        loc: Tuple[float, float, float],
        scale: Tuple[float, float, float] = (1.0, 1.0, 1.0),
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        segs: int = 18,
        rings: int = 12,
        mat=None,
        smooth: bool = True,
        parent=None,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=radius, location=loc)
        obj = bpy.context.active_object
        obj.name = name
        obj.scale = scale
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if smooth:
            set_smooth(obj, True)
        if mat:
            obj.data.materials.append(mat)
        if parent is not None:
            obj.parent = parent
        return obj

    def add_tube_curve(
        name: str,
        points_radii: Sequence[Tuple[float, float, float, float]],
        segs: int = 12,
        mat=None,
        smooth: bool = True,
        cap_ends: bool = True,
        parent=None,
    ) -> "bpy.types.Object":
        mesh = bpy.data.meshes.new(name + "_mesh")
        bm = bmesh.new()
        rings: List[List[bmesh.types.BMVert]] = []
        n_pts = len(points_radii)
        prev_up = Vector((0.0, 0.0, 1.0))

        for i, (px, py, pz, rad) in enumerate(points_radii):
            center = Vector((px, py, pz))
            if i == 0:
                tangent = (Vector(points_radii[1][:3]) - center).normalized()
            elif i == n_pts - 1:
                tangent = (center - Vector(points_radii[-2][:3])).normalized()
            else:
                tangent = (Vector(points_radii[i + 1][:3]) - Vector(points_radii[i - 1][:3])).normalized()
            if tangent.length < 1e-6:
                tangent = Vector((0.0, 0.0, 1.0))

            ref = prev_up
            if abs(tangent.dot(ref)) > 0.92:
                ref = Vector((1.0, 0.0, 0.0)) if abs(tangent.x) < 0.8 else Vector((0.0, 1.0, 0.0))
            side = tangent.cross(ref).normalized()
            up = side.cross(tangent).normalized()
            prev_up = up

            ring: List[bmesh.types.BMVert] = []
            for s in range(segs):
                ang = 2.0 * math.pi * s / segs
                offset = (side * math.cos(ang) + up * math.sin(ang)) * rad
                ring.append(bm.verts.new(center + offset))
            rings.append(ring)

        for i in range(n_pts - 1):
            r0 = rings[i]
            r1 = rings[i + 1]
            for s in range(segs):
                ns = (s + 1) % segs
                bm.faces.new((r0[s], r0[ns], r1[ns], r1[s]))

        if cap_ends and len(rings) >= 2:
            bm.faces.new(list(reversed(rings[0])))
            bm.faces.new(rings[-1])

        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        if smooth:
            set_smooth(obj, True)
        if mat:
            obj.data.materials.append(mat)
        if parent is not None:
            obj.parent = parent
        return obj

    def add_prism(
        name: str,
        profile_xz: Sequence[Tuple[float, float]],
        half_thickness_y: float,
        loc: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        mat=None,
        bevel: float = 0.012,
        parent=None,
    ) -> "bpy.types.Object":
        mesh = bpy.data.meshes.new(name + "_mesh")
        bm = bmesh.new()
        f_v = [bm.verts.new((x, -half_thickness_y, z)) for x, z in profile_xz]
        b_v = [bm.verts.new((x, half_thickness_y, z)) for x, z in profile_xz]
        bm.faces.new(f_v)
        bm.faces.new(list(reversed(b_v)))
        n = len(profile_xz)
        for i in range(n):
            ni = (i + 1) % n
            bm.faces.new((f_v[i], f_v[ni], b_v[ni], b_v[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if mat:
            obj.data.materials.append(mat)
        if bevel > 0.0:
            add_bevel(obj, bevel, 1)
        if parent is not None:
            obj.parent = parent
        return obj

    # -----------------------------------------------------------------------
    # Articulated Biped Rig Helpers
    # -----------------------------------------------------------------------
    def create_biped_rig(
        pose: BipedPose,
        base_hip_z: float = 0.88,
        torso_h: float = 0.48,
        shoulder_x: float = 0.34,
        shoulder_z_off: float = 0.36,
    ) -> Dict[str, "bpy.types.Object"]:
        char_root = make_empty("CharRoot", (0.0, 0.0, 0.0))
        pelvis = make_empty(
            "PelvisPivot",
            (pose.hip_dx, pose.hip_dy, base_hip_z + pose.hip_dz),
            (0.0, 0.0, pose.torso_yaw * 0.35),
            parent=char_root,
        )
        torso = make_empty(
            "TorsoPivot",
            (0.0, 0.0, 0.14),
            (pose.torso_pitch, pose.torso_roll, pose.torso_yaw * 0.65),
            parent=pelvis,
        )
        head = make_empty(
            "HeadPivot",
            (0.0, -0.02, torso_h + 0.14),
            (pose.head_pitch, 0.0, pose.head_yaw),
            parent=torso,
        )
        sh_l = make_empty("Shoulder_L", (-shoulder_x, 0.0, shoulder_z_off), parent=torso)
        sh_r = make_empty("Shoulder_R", (shoulder_x, 0.0, shoulder_z_off), parent=torso)
        return {
            "root": char_root,
            "pelvis": pelvis,
            "torso": torso,
            "head": head,
            "sh_l": sh_l,
            "sh_r": sh_r,
        }

    def add_articulated_legs(
        rig: Dict[str, "bpy.types.Object"],
        pose: BipedPose,
        m_boot,
        m_shin,
        m_thigh,
        stance_x: float = 0.21,
        thigh_len: float = 0.36,
        shin_len: float = 0.38,
        thigh_r: float = 0.135,
        shin_r: float = 0.115,
        boot_size: Tuple[float, float, float] = (0.20, 0.32, 0.15),
        m_boot_cuff=None,
        m_greave=None,
        ground_clamp: bool = True,
    ) -> List["bpy.types.Object"]:
        """Attach articulated thigh -> knee -> shin -> boot chains to pelvis and clamp lowest boot to z=0."""
        pelvis = rig["pelvis"]
        boots: List["bpy.types.Object"] = []
        cuff_mat = m_boot_cuff if m_boot_cuff is not None else m_boot

        leg_specs = (
            ("L", -stance_x, pose.l_thigh_fwd, pose.l_knee_bend, pose.l_thigh_splay),
            ("R", stance_x, pose.r_thigh_fwd, pose.r_knee_bend, pose.r_thigh_splay),
        )
        for side, sx, th_fwd, kn_bend, splay in leg_specs:
            # Blender X rot: negative tilts -Z forward (-Y); positive bends knee back (+Y)
            hip_rot_x = -th_fwd
            hip = make_empty(
                f"Hip_{side}",
                (sx, 0.0, -0.02),
                (hip_rot_x, splay, 0.0),
                parent=pelvis,
            )
            add_sphere(f"HipJoint_{side}", thigh_r * 0.96, (0.0, 0.0, 0.0), mat=m_thigh, parent=hip)
            add_cyl(f"Thigh_{side}", thigh_r, thigh_len, (0.0, 0.0, -thigh_len * 0.5), verts=14, mat=m_thigh, parent=hip)

            knee = make_empty(
                f"Knee_{side}",
                (0.0, 0.0, -thigh_len),
                (kn_bend, 0.0, 0.0),
                parent=hip,
            )
            add_sphere(f"KneeCap_{side}", shin_r * 1.12, (0.0, -0.02, 0.0), mat=m_shin, parent=knee)
            add_cyl(f"Shin_{side}", shin_r, shin_len, (0.0, 0.0, -shin_len * 0.5), verts=14, mat=m_shin, parent=knee)
            if m_greave is not None:
                add_box(
                    f"Greave_{side}",
                    (shin_r * 2.0, shin_r * 1.3, shin_len * 0.84),
                    (0.0, -0.04, -shin_len * 0.5),
                    mat=m_greave,
                    bevel=0.015,
                    parent=knee,
                )

            # Level foot to world ground plane (undo accumulated X pitch)
            foot_pitch_undo = -(hip_rot_x + kn_bend) * 0.72
            ankle = make_empty(
                f"Ankle_{side}",
                (0.0, 0.0, -shin_len),
                (foot_pitch_undo, -splay * 0.5, -8.0 if side == "L" else 8.0),
                parent=knee,
            )
            add_cyl(f"BootCuff_{side}", shin_r * 1.16, 0.08, (0.0, 0.0, 0.02), verts=14, mat=cuff_mat, parent=ankle)
            boot = add_box(
                f"Boot_{side}",
                boot_size,
                (0.0, -0.06, -boot_size[2] * 0.48),
                mat=m_boot,
                bevel=0.02,
                parent=ankle,
            )
            boots.append(boot)

        if ground_clamp:
            bpy.context.view_layer.update()
            min_z = 999.0
            for boot in boots:
                for corner in boot.bound_box:
                    wz = (boot.matrix_world @ Vector(corner)).z
                    if wz < min_z:
                        min_z = wz
            rig["root"].location.z -= min_z
            bpy.context.view_layer.update()

        return boots

    def add_articulated_arm(
        rig: Dict[str, "bpy.types.Object"],
        side: str,
        sh_fwd: float,
        sh_out: float,
        sh_yaw: float,
        el_bend: float,
        m_upper,
        m_forearm,
        m_hand=None,
        upper_len: float = 0.28,
        fore_len: float = 0.26,
        radius: float = 0.095,
    ) -> "bpy.types.Object":
        """Attach articulated shoulder -> elbow -> hand_pivot chain and return HandPivot."""
        sh_pivot = rig["sh_l"] if side == "L" else rig["sh_r"]
        sign = -1.0 if side == "L" else 1.0
        # sh_fwd > 0 swings forward/up (-Y): rot_x = -sh_fwd; sh_out > 0 splays laterally: rot_y = -sign * sh_out
        sh_pivot.rotation_euler = tuple(math.radians(a) for a in (-sh_fwd, -sign * sh_out, sh_yaw))
        add_cyl(f"UpperArm_{side}", radius, upper_len, (0.0, 0.0, -upper_len * 0.5), verts=12, mat=m_upper, parent=sh_pivot)
        elbow = make_empty(
            f"Elbow_{side}",
            (0.0, 0.0, -upper_len),
            (-el_bend, 0.0, 0.0),
            parent=sh_pivot,
        )
        add_sphere(f"ElbowBall_{side}", radius * 0.96, (0.0, 0.0, 0.0), mat=m_forearm, parent=elbow)
        add_cyl(f"Forearm_{side}", radius * 0.92, fore_len, (0.0, 0.0, -fore_len * 0.5), verts=12, mat=m_forearm, parent=elbow)
        hand = make_empty(f"Hand_{side}", (0.0, 0.0, -fore_len), parent=elbow)
        if m_hand is not None:
            add_sphere(f"Fist_{side}", radius * 0.92, (0.0, 0.0, 0.0), mat=m_hand, parent=hand)
        return hand

    # -----------------------------------------------------------------------
    # Reusable Weapon & Shield Builders (Parentable to HandPivot)
    # -----------------------------------------------------------------------
    def add_broadsword(
        prefix: str,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float],
        m_blade,
        m_hilt,
        m_guard,
        blade_len: float = 0.88,
        blade_w: float = 0.095,
        grip_len: float = 0.24,
        parent=None,
    ) -> "bpy.types.Object":
        root = make_empty(f"{prefix}_Root", loc, rot_deg, parent=parent)
        add_cyl(f"{prefix}_Grip", 0.034, grip_len, (0.0, 0.0, 0.0), verts=12, mat=m_hilt, parent=root)
        add_sphere(f"{prefix}_Pommel", 0.052, (0.0, 0.0, -grip_len * 0.54), mat=m_guard, parent=root)
        add_box(f"{prefix}_Guard", (blade_w * 3.0, 0.07, 0.065), (0.0, 0.0, grip_len * 0.54), mat=m_guard, bevel=0.01, parent=root)
        add_box(
            f"{prefix}_Blade",
            (blade_w, 0.036, blade_len * 0.82),
            (0.0, 0.0, grip_len * 0.54 + blade_len * 0.42),
            mat=m_blade,
            bevel=0.008,
            parent=root,
        )
        add_cone(
            f"{prefix}_Tip",
            blade_w * 0.65,
            0.005,
            blade_len * 0.22,
            (0.0, 0.0, grip_len * 0.54 + blade_len * 0.92),
            verts=4,
            rot_deg=(0.0, 0.0, 45.0),
            mat=m_blade,
            smooth=False,
            parent=root,
        )
        return root

    def add_curved_blade(
        prefix: str,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float],
        m_blade,
        m_hilt,
        m_guard,
        length: float = 0.78,
        width: float = 0.095,
        curve_x: float = -0.14,
        parent=None,
    ) -> "bpy.types.Object":
        root = make_empty(f"{prefix}_Root", loc, rot_deg, parent=parent)
        add_cyl(f"{prefix}_Grip", 0.032, 0.20, (0.0, 0.0, 0.0), verts=12, mat=m_hilt, parent=root)
        add_sphere(f"{prefix}_Pommel", 0.044, (0.0, 0.0, -0.11), mat=m_guard, parent=root)
        add_box(f"{prefix}_Guard", (width * 2.3, 0.06, 0.055), (0.0, 0.0, 0.11), mat=m_guard, bevel=0.01, parent=root)

        profile: List[Tuple[float, float]] = []
        steps = 6
        for i in range(steps + 1):
            t = i / steps
            z = 0.13 + t * length
            cx = curve_x * (t ** 1.65)
            w_t = width * (1.0 - 0.55 * (t ** 2.2))
            profile.append((cx - w_t * 0.5, z))
        profile.append((curve_x, 0.13 + length + 0.08))
        for i in range(steps, -1, -1):
            t = i / steps
            z = 0.13 + t * length
            cx = curve_x * (t ** 1.65)
            w_t = width * (1.0 - 0.55 * (t ** 2.2))
            profile.append((cx + w_t * 0.5, z))

        add_prism(f"{prefix}_Blade", profile, 0.018, mat=m_blade, bevel=0.006, parent=root)
        return root

    def add_round_shield(
        prefix: str,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float],
        radius: float,
        m_face,
        m_rim,
        parent=None,
    ) -> "bpy.types.Object":
        root = make_empty(f"{prefix}_Root", loc, rot_deg, parent=parent)
        add_cyl(f"{prefix}_Rim", radius, 0.06, (0.0, 0.0, 0.0), rot_deg=(90.0, 0.0, 0.0), verts=20, mat=m_rim, parent=root)
        add_cyl(f"{prefix}_Face", radius * 0.86, 0.072, (0.0, -0.004, 0.0), rot_deg=(90.0, 0.0, 0.0), verts=20, mat=m_face, parent=root)
        add_sphere(f"{prefix}_Boss", radius * 0.28, (0.0, -0.04, 0.0), scale=(1.0, 0.65, 1.0), mat=m_rim, parent=root)
        return root

    # -----------------------------------------------------------------------
    # Camera & Render Helpers (with exact ground projection)
    # -----------------------------------------------------------------------
    def setup_render_and_camera(
        ortho_scale: float,
        target: Tuple[float, float, float],
        yaw_deg: float,
    ) -> Tuple[float, float]:
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.render.resolution_x = RENDER_RES
        scene.render.resolution_y = RENDER_RES
        scene.render.resolution_percentage = 100
        scene.render.film_transparent = True
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"

        try:
            scene.view_settings.view_transform = "Standard"
            scene.view_settings.look = "None"
        except Exception:
            pass

        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "MATERIAL"
        scene.display.shading.show_cavity = True
        scene.display.shading.cavity_type = "BOTH"
        scene.display.shading.cavity_ridge_factor = 1.6
        scene.display.shading.cavity_valley_factor = 1.8
        scene.display.shading.show_object_outline = False

        vl = scene.view_layers[0]
        vl.use_pass_z = True

        cam_data = bpy.data.cameras.new("OrthoCam")
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho_scale
        cam_data.clip_start = 0.1
        cam_data.clip_end = 100.0
        cam_obj = bpy.data.objects.new("OrthoCam", cam_data)
        bpy.context.collection.objects.link(cam_obj)
        scene.camera = cam_obj

        pitch_r = math.radians(CAMERA_PITCH_DEG)
        yaw_r = math.radians(yaw_deg)
        dist = 20.0
        dx = dist * math.cos(pitch_r) * math.sin(yaw_r)
        dy = -dist * math.cos(pitch_r) * math.cos(yaw_r)
        dz = dist * math.sin(pitch_r)
        cam_obj.location = (target[0] + dx, target[1] + dy, target[2] + dz)

        direction = Vector(target) - cam_obj.location
        rot_quat = direction.to_track_quat("-Z", "Y")
        cam_obj.rotation_euler = rot_quat.to_euler()
        bpy.context.view_layer.update()

        # Project Blender world origin (0, 0, 0) into 1024x1024 pixel coords
        p_cam = cam_obj.matrix_world.inverted() @ Vector((0.0, 0.0, 0.0))
        ndc_x = 0.5 + (p_cam.x / ortho_scale)
        ndc_y = 0.5 + (p_cam.y / ortho_scale)
        ground_x_px = float(ndc_x * RENDER_RES)
        ground_y_px = float((1.0 - ndc_y) * RENDER_RES)
        return ground_x_px, ground_y_px

    def render_asset_passes(stem: str, ground_xy: Tuple[float, float], ortho_scale: float, yaw_deg: float) -> None:
        scene = bpy.context.scene
        scene.use_nodes = True
        nt = scene.node_tree
        nt.nodes.clear()

        rl = nt.nodes.new("CompositorNodeRLayers")
        comp = nt.nodes.new("CompositorNodeComposite")
        nt.links.new(rl.outputs["Image"], comp.inputs["Image"])

        fo = nt.nodes.new("CompositorNodeOutputFile")
        fo.base_path = str(raw_dir)
        fo.format.file_format = "OPEN_EXR"
        fo.format.color_depth = "32"
        fo.file_slots.clear()
        fo.file_slots.new(f"{stem}_depth_")
        nt.links.new(rl.outputs["Depth"], fo.inputs[f"{stem}_depth_"])

        rgba_path = raw_dir / f"{stem}_rgba.png"
        scene.render.filepath = str(rgba_path)
        bpy.ops.render.render(write_still=True)

        exr_path = raw_dir / f"{stem}_depth_0001.exr"
        img = bpy.data.images.load(str(exr_path), check_existing=False)
        w, h = img.size[0], img.size[1]
        arr = np.array(img.pixels[:], dtype=np.float32).reshape((h, w, 4))
        arr = np.flipud(arr)
        np.save(str(raw_dir / f"{stem}_depth.npy"), arr[..., 0])
        bpy.data.images.remove(img)
        exr_path.unlink(missing_ok=True)

        projections[stem] = {
            "ground_x_px": round(ground_xy[0], 2),
            "ground_y_px": round(ground_xy[1], 2),
            "ortho_scale": ortho_scale,
            "yaw_deg": yaw_deg,
        }

    # -----------------------------------------------------------------------
    # 13 Combat Unit Builders (Exact f0 colors/designs + Articulated Poses)
    # -----------------------------------------------------------------------

    def build_hero_knight(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_steel_lt = make_mat("SteelLight", "#D8E2F0")
        m_steel_mid = make_mat("SteelMid", "#9AA7BD")
        m_steel_dk = make_mat("SteelDark", "#475061")
        m_chest_brn = make_mat("ChestBrown", "#633C24")
        m_blade = make_mat("BladeSilver", "#D8E2F0")
        m_blade_edge = make_mat("BladeBlue", "#869BC9")
        m_gold = make_mat("SunlitGold", "#ECA938")
        m_crimson = make_mat("CrimsonPlume", "#C23846")
        m_crimson_dk = make_mat("CrimsonCape", "#992838")
        m_cobalt = make_mat("CobaltShield", "#1C2952")

        rig = create_biped_rig(pose, base_hip_z=0.88, torso_h=0.48, shoulder_x=0.37, shoulder_z_off=0.38)
        add_articulated_legs(
            rig, pose, m_steel_dk, m_steel_lt, m_steel_mid,
            stance_x=0.21, thigh_len=0.36, shin_len=0.38, thigh_r=0.14, shin_r=0.12,
            m_boot_cuff=m_steel_lt,
        )

        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("Pelvis", (0.48, 0.30, 0.20), (0.0, 0.0, 0.06), mat=m_steel_mid, bevel=0.025, parent=pelvis)
        for sx in (-0.23, 0.23):
            add_box(f"Tasset_{sx}", (0.16, 0.26, 0.22), (sx, -0.04, -0.04), rot_deg=(0.0, 12.0 if sx < 0 else -12.0, 0.0), mat=m_steel_lt, parent=pelvis)
        add_box("Belt", (0.52, 0.34, 0.09), (0.0, 0.0, 0.14), mat=m_gold, bevel=0.015, parent=pelvis)

        # Dark brown chest brigandine matching f0, steel gorget & pauldrons
        add_box("ChestArmor", (0.56, 0.36, 0.48), (0.0, 0.0, 0.24), mat=m_chest_brn, bevel=0.045, parent=torso)
        add_box("ChestStrap", (0.22, 0.38, 0.12), (0.0, -0.01, 0.26), mat=m_gold, bevel=0.015, parent=torso)
        add_box("Gorget", (0.36, 0.28, 0.10), (0.0, 0.0, 0.49), mat=m_steel_lt, bevel=0.015, parent=torso)

        for side, sx in (("L", -0.37), ("R", 0.37)):
            add_sphere(f"Pauldron_{side}", 0.19, (sx, 0.0, 0.42), scale=(1.05, 0.95, 0.85), mat=m_steel_lt, parent=torso)
            add_sphere(f"PauldronRim_{side}", 0.18, (sx * 1.03, 0.0, 0.39), scale=(1.08, 0.98, 0.35), mat=m_gold, parent=torso)

        add_cyl("HelmBody", 0.175, 0.32, (0.0, 0.0, 0.08), verts=16, mat=m_steel_lt, parent=head)
        add_sphere("HelmDome", 0.175, (0.0, 0.0, 0.22), scale=(1.0, 1.0, 0.78), mat=m_steel_lt, parent=head)
        add_box("HelmVisor", (0.24, 0.12, 0.10), (0.0, -0.15, 0.10), mat=m_steel_dk, bevel=0.01, parent=head)
        add_cyl("HelmBand", 0.188, 0.055, (0.0, 0.0, 0.19), verts=16, mat=m_gold, parent=head)
        add_tube_curve(
            "HelmPlume",
            [
                (0.0, -0.02, 0.36, 0.065),
                (0.0, 0.12, 0.42, 0.085),
                (0.0, 0.30, 0.34, 0.075),
                (0.0, 0.44, 0.18, 0.045),
            ],
            mat=m_crimson,
            parent=head,
        )

        # Flowing crimson cape tucked narrower behind back
        add_box("CapeMain", (0.40, 0.10, 0.96), (0.0, 0.34 + 0.006 * pose.cape_trail_deg, -0.08), rot_deg=(-16.0 - pose.cape_trail_deg * 0.7, 0.0, 0.0), mat=m_crimson_dk, bevel=0.03, parent=torso)

        # Left arm (-X): Cobalt & Gold Kite Shield
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_steel_mid, m_steel_lt, m_steel_dk)
        shield_pts = [(-0.26, 0.32), (0.26, 0.32), (0.28, 0.06), (0.0, -0.44), (-0.28, 0.06)]
        shield_root = make_empty("KiteShieldRoot", (-0.06, -0.06, 0.02), (pose.offhand_rot[0] - 8.0, pose.offhand_rot[1], pose.offhand_rot[2] + 24.0), parent=hand_l)
        add_prism("KiteShieldRim", shield_pts, 0.042, mat=m_gold, bevel=0.015, parent=shield_root)
        inner_pts = [(x * 0.80, z * 0.80) for x, z in shield_pts]
        add_prism("KiteShieldFace", inner_pts, 0.050, loc=(0.0, -0.008, 0.0), mat=m_cobalt, bevel=0.01, parent=shield_root)
        add_sphere("KiteShieldBoss", 0.075, (0.0, -0.06, 0.08), scale=(1.0, 0.6, 1.0), mat=m_gold, parent=shield_root)

        # Right arm (+X): Broadsword
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_steel_mid, m_steel_lt, m_steel_dk)
        sw_rx = (pose.r_sh_fwd + pose.r_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (24.0 + pose.weapon_rot[0])
        sw_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (14.0 + pose.weapon_rot[1])
        add_broadsword(
            "KnightSword",
            (0.02, -0.04, 0.04),
            (sw_rx, sw_ry, pose.weapon_rot[2]),
            m_blade,
            m_steel_dk,
            m_gold,
            blade_len=0.90,
            blade_w=0.105,
            parent=hand_r,
        )

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_hero_cleric(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_ivory = make_mat("IvoryRobe", "#F0ECE4")
        m_azure = make_mat("RoyalAzure", "#3452A1")
        m_gold = make_mat("SunlitGold", "#ECA938")
        m_gold_glow = make_mat("SunGlow", "#FFF3B0")
        m_cyan = make_mat("ArcaneCyan", "#54C9C0")
        m_wood = make_mat("StaffWood", "#825132")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.46, shoulder_x=0.34, shoulder_z_off=0.36)
        add_articulated_legs(
            rig, pose, m_wood, m_azure, m_ivory,
            stance_x=0.18, thigh_len=0.35, shin_len=0.36, thigh_r=0.135, shin_r=0.115,
            boot_size=(0.18, 0.28, 0.14),
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        # Split robes matching f0 (tapered/raised hem +0.10m so striding boots read cleanly in f0/f2)
        add_cone("RobeSkirt", 0.30, 0.22, 0.34, (0.0, 0.0, -0.09), verts=18, mat=m_ivory, parent=pelvis)
        add_box("FrontStoleLower", (0.17, 0.31, 0.32), (0.0, -0.04, -0.09), mat=m_azure, bevel=0.02, parent=pelvis)
        add_box("Belt", (0.48, 0.34, 0.09), (0.0, 0.0, 0.10), mat=m_gold, bevel=0.015, parent=pelvis)

        add_box("Torso", (0.48, 0.32, 0.44), (0.0, 0.0, 0.22), mat=m_ivory, bevel=0.04, parent=torso)
        add_box("FrontStoleUpper", (0.18, 0.35, 0.40), (0.0, -0.01, 0.24), mat=m_azure, bevel=0.015, parent=torso)
        add_box("GoldMantle", (0.64, 0.38, 0.15), (0.0, 0.02, 0.40), mat=m_gold, bevel=0.03, parent=torso)

        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"Shoulder_{side}", 0.16, (sx, 0.0, 0.39), scale=(1.0, 0.9, 0.85), mat=m_gold, parent=torso)

        add_sphere("Head", 0.155, (0.0, -0.04, 0.06), mat=m_skin, parent=head)
        add_cone("MitreBack", 0.21, 0.07, 0.34, (0.0, 0.05, 0.20), rot_deg=(14.0, 0.0, 0.0), verts=14, mat=m_ivory, parent=head)
        add_cyl("MitreBand", 0.19, 0.065, (0.0, 0.01, 0.17), verts=16, mat=m_azure, parent=head)

        # Back hand (-X): Tall golden sun-crozier matching f0 (counter-pitch arm so staff stays upright)
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_ivory, m_ivory, m_skin)
        crozier_undo_x = pose.l_sh_fwd + pose.l_el_bend
        staff_root = make_empty("SunCrozierRoot", (-0.02, -0.04, -0.02), (crozier_undo_x + 8.0 + pose.offhand_rot[0], -6.0, 0.0), parent=hand_l)
        add_cyl("CrozierShaft", 0.038, 1.72, (0.0, 0.0, 0.12), verts=12, mat=m_wood, parent=staff_root)
        add_cyl("CrozierCollar", 0.065, 0.10, (0.0, 0.0, 0.88), verts=12, mat=m_gold, parent=staff_root)
        crozier_pts = [
            (0.14 * math.cos(math.radians(a)), 0.0, 1.06 + 0.14 * math.sin(math.radians(a)), 0.028)
            for a in range(-60, 241, 25)
        ]
        add_tube_curve("CrozierSunArc", crozier_pts, mat=m_gold, parent=staff_root)
        add_sphere("CrozierGem", 0.075, (0.0, 0.0, 1.06), mat=m_cyan, parent=staff_root)

        # Front hand (+X): Spiked golden star-rod / mace
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_ivory, m_ivory, m_skin)
        mace_rx = (pose.r_sh_fwd + pose.r_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (24.0 + pose.weapon_rot[0])
        mace_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (14.0 + pose.weapon_rot[1])
        mace_root = make_empty("StarRodRoot", (0.02, -0.04, 0.04), (mace_rx, mace_ry, pose.weapon_rot[2]), parent=hand_r)
        add_cyl("MaceShaft", 0.034, 0.60, (0.0, 0.0, 0.16), verts=12, mat=m_wood, parent=mace_root)
        add_sphere("MaceHead", 0.115, (0.0, 0.0, 0.48), mat=m_gold, parent=mace_root)
        for ang in (0.0, 45.0, 90.0, 135.0):
            add_box(f"MaceFlange_{ang}", (0.28, 0.045, 0.22), (0.0, 0.0, 0.48), rot_deg=(0.0, 0.0, ang), mat=m_gold, bevel=0.008, parent=mace_root)
        if pose.prop_fx == "sunburst":
            add_sphere("SunBurstOrb", 0.20, (0.0, -0.06, 0.64), mat=m_gold_glow, parent=mace_root)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_hero_ranger(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_em_dk = make_mat("ForestCloak", "#426643")
        m_em_lt = make_mat("OlivePatch", "#8FA848")
        m_trousers = make_mat("DarkTrousers", "#2E4A33")
        m_boots = make_mat("BrownBoots", "#633C24")
        m_bow = make_mat("BowWood", "#825132")
        m_skin = make_mat("WarmSkin", "#EDB89A")
        m_fletch = make_mat("GreenFletch", "#52A653")
        m_steel = make_mat("ArrowTip", "#D8E2F0")

        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.45, shoulder_x=0.33, shoulder_z_off=0.36)
        add_articulated_legs(
            rig, pose, m_boots, m_trousers, m_trousers,
            stance_x=0.20, thigh_len=0.35, shin_len=0.36, thigh_r=0.13, shin_r=0.115,
            m_boot_cuff=m_boots,
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("PelvisTunic", (0.46, 0.30, 0.24), (0.0, 0.0, 0.04), mat=m_em_dk, bevel=0.03, parent=pelvis)
        add_box("Belt", (0.50, 0.33, 0.085), (0.0, 0.0, 0.14), mat=m_boots, bevel=0.015, parent=pelvis)

        add_box("TunicTorso", (0.48, 0.32, 0.44), (0.0, 0.0, 0.22), mat=m_em_dk, bevel=0.04, parent=torso)
        add_box("LeatherJerkin", (0.40, 0.34, 0.36), (0.0, -0.01, 0.24), mat=m_boots, bevel=0.03, parent=torso)
        add_box("ShoulderMantle", (0.62, 0.38, 0.16), (0.0, 0.04, 0.38), mat=m_em_dk, bevel=0.03, parent=torso)
        add_sphere("OlivePatch_R", 0.16, (0.31, -0.01, 0.38), scale=(1.0, 0.9, 0.8), mat=m_em_lt, parent=torso)
        add_box("CloakBack", (0.56, 0.10, 0.98), (0.0, 0.26 + 0.008 * pose.cape_trail_deg, -0.06), rot_deg=(-18.0 - pose.cape_trail_deg * 0.6, 0.0, 0.0), mat=m_em_dk, bevel=0.03, parent=torso)

        quiv = make_empty("QuiverRoot", (-0.18, 0.24, 0.34), (16.0, -26.0, 0.0), parent=torso)
        add_cyl("QuiverBody", 0.085, 0.54, (0.0, 0.0, 0.0), verts=12, mat=m_boots, parent=quiv)
        for ax, ay in ((-0.03, 0.02), (0.02, -0.02), (0.0, 0.03)):
            add_cyl(f"QArrow_{ax}", 0.016, 0.22, (ax, ay, 0.35), verts=8, mat=m_bow, parent=quiv)
            add_box(f"QFletch_{ax}", (0.05, 0.016, 0.095), (ax, ay, 0.45), mat=m_fletch, bevel=0.004, parent=quiv)

        add_sphere("Head", 0.15, (0.0, -0.06, 0.06), mat=m_skin, parent=head)
        add_sphere("HoodCowl", 0.18, (0.0, 0.03, 0.09), scale=(1.05, 1.12, 1.06), mat=m_em_dk, parent=head)
        add_cone("HoodTip", 0.15, 0.02, 0.30, (0.0, 0.20, 0.18), rot_deg=(-58.0, 0.0, 0.0), verts=12, mat=m_em_dk, parent=head)

        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_em_dk, m_em_lt, m_skin)
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_em_dk, m_boots, m_skin)

        # Undo arm pitch so bow stands vertical and arrow aims East/forward
        bow_undo_x = pose.r_sh_fwd + pose.r_el_bend
        bow_root = make_empty("BowRoot", (0.02, -0.04, 0.0), (bow_undo_x + pose.weapon_rot[0], pose.weapon_rot[1] + 8.0, pose.weapon_rot[2] + 24.0), parent=hand_r)
        add_tube_curve(
            "RecurveBow",
            [
                (0.0, 0.22, -0.60, 0.026),
                (0.0, 0.06, -0.42, 0.034),
                (0.0, -0.06, -0.20, 0.042),
                (0.0, -0.08, 0.0, 0.048),
                (0.0, -0.06, 0.20, 0.042),
                (0.0, 0.06, 0.42, 0.034),
                (0.0, 0.22, 0.60, 0.026),
            ],
            mat=m_bow,
            parent=bow_root,
        )
        if pose.prop_fx != "bow_empty":
            fly_y = -pose.arrow_fly_dist
            draw_y = 0.14 if pose.prop_fx == "bow_draw" else 0.0
            add_cyl("ArrowShaft", 0.018, 0.80, (0.0, -0.14 + draw_y + fly_y, 0.02), rot_deg=(90.0, 0.0, 0.0), verts=8, mat=m_bow, parent=bow_root)
            add_cone("ArrowHead", 0.045, 0.004, 0.13, (0.0, -0.58 + draw_y + fly_y, 0.02), rot_deg=(-90.0, 0.0, 0.0), verts=4, mat=m_steel, smooth=False, parent=bow_root)
            add_box("ArrowFletch", (0.065, 0.12, 0.02), (0.0, 0.24 + draw_y + fly_y, 0.02), mat=m_fletch, bevel=0.004, parent=bow_root)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_hero_mage(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_sapphire = make_mat("SapphireRobe", "#273C78")
        m_cobalt_lt = make_mat("CobaltTrim", "#476EC7")
        m_gold = make_mat("ArcaneGold", "#ECA938")
        m_beard = make_mat("WhiteBeard", "#F7F4EA")
        m_staff = make_mat("GnarledWood", "#825132")
        m_crystal = make_mat("ArcaneCrystal", "#54C9C0")
        m_burst = make_mat("ArcaneBurst", "#E0F9FF")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.45, shoulder_x=0.33, shoulder_z_off=0.36)
        add_articulated_legs(
            rig, pose, m_staff, m_sapphire, m_sapphire,
            stance_x=0.18, thigh_len=0.35, shin_len=0.36, thigh_r=0.13, shin_r=0.115,
            boot_size=(0.18, 0.28, 0.14),
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_cone("WizardSkirt", 0.31, 0.23, 0.35, (0.0, 0.0, -0.095), verts=18, mat=m_sapphire, parent=pelvis)
        add_cone("RobeHemGold", 0.325, 0.30, 0.075, (0.0, 0.0, -0.25), verts=18, mat=m_gold, parent=pelvis)
        add_box("RobePanel", (0.17, 0.32, 0.34), (0.0, -0.04, -0.095), mat=m_cobalt_lt, bevel=0.02, parent=pelvis)
        add_box("ArcaneSash", (0.50, 0.35, 0.11), (0.0, 0.0, 0.10), mat=m_gold, bevel=0.015, parent=pelvis)

        add_box("Torso", (0.48, 0.34, 0.44), (0.0, 0.0, 0.22), mat=m_sapphire, bevel=0.04, parent=torso)
        add_box("ShoulderTrim", (0.62, 0.38, 0.16), (0.0, 0.02, 0.38), mat=m_cobalt_lt, bevel=0.03, parent=torso)
        add_box("GoldCollar", (0.40, 0.34, 0.08), (0.0, -0.02, 0.44), mat=m_gold, bevel=0.015, parent=torso)

        add_sphere("Head", 0.15, (0.0, -0.06, 0.06), mat=m_skin, parent=head)
        add_sphere("Hood", 0.185, (0.0, 0.05, 0.10), scale=(1.08, 1.15, 1.08), mat=m_sapphire, parent=head)
        add_cone("HoodPeak", 0.16, 0.02, 0.36, (0.0, 0.20, 0.22), rot_deg=(-54.0, 0.0, 0.0), verts=12, mat=m_sapphire, parent=head)
        add_cyl("HoodGoldRim", 0.17, 0.045, (0.0, 0.01, 0.14), rot_deg=(12.0, 0.0, 0.0), verts=14, mat=m_gold, parent=head)
        add_tube_curve(
            "WhiteBeard",
            [
                (0.0, -0.16, 0.01, 0.115),
                (0.0, -0.20, -0.16, 0.105),
                (0.0, -0.22, -0.32, 0.075),
                (0.0, -0.20, -0.44, 0.035),
            ],
            mat=m_beard,
            parent=head,
        )

        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_cobalt_lt, m_sapphire, m_skin)
        add_sphere("SpellOrb_L", 0.09, (0.0, -0.06, 0.0), mat=m_crystal, parent=hand_l)

        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_cobalt_lt, m_sapphire, m_skin)
        staff_undo_x = (pose.r_sh_fwd + pose.r_el_bend) if pose.prop_fx == "" and pose.weapon_rot == (0.0, 0.0, 0.0) else 0.0
        staff_root = make_empty("MageStaffRoot", (0.02, -0.04, 0.0), (staff_undo_x + 10.0 + pose.weapon_rot[0], 8.0 + pose.weapon_rot[1], pose.weapon_rot[2]), parent=hand_r)
        add_tube_curve(
            "GnarledStaff",
            [
                (0.0, 0.0, -0.96, 0.036),
                (0.02, -0.02, -0.28, 0.042),
                (-0.02, 0.01, 0.30, 0.044),
                (0.03, -0.02, 0.72, 0.052),
                (0.07, -0.04, 0.92, 0.038),
            ],
            mat=m_staff,
            parent=staff_root,
        )
        add_tube_curve(
            "StaffFork",
            [(0.02, -0.01, 0.72, 0.036), (-0.07, -0.03, 0.90, 0.032)],
            mat=m_staff,
            parent=staff_root,
        )
        add_cone("CrystalTop", 0.115, 0.01, 0.25, (0.0, -0.03, 1.04), verts=6, mat=m_crystal, smooth=False, parent=staff_root)
        add_cone("CrystalBot", 0.115, 0.01, 0.16, (0.0, -0.03, 0.85), rot_deg=(180.0, 0.0, 0.0), verts=6, mat=m_crystal, smooth=False, parent=staff_root)
        if pose.prop_fx == "mage_burst_big":
            add_sphere("SpellBurstOuter", 0.26, (0.0, -0.08, 1.28), mat=m_crystal, parent=staff_root)
            add_sphere("SpellBurstCore", 0.17, (0.0, -0.14, 1.32), mat=m_burst, parent=staff_root)
            for ang in (0.0, 45.0, 90.0, 135.0):
                add_box(f"ArcaneRay_{ang}", (0.58, 0.05, 0.05), (0.0, -0.08, 1.28), rot_deg=(0.0, ang, 0.0), mat=m_burst, bevel=0.008, parent=staff_root)
        elif pose.prop_fx == "mage_burst_small":
            add_sphere("SpellBurstFade", 0.16, (0.0, -0.06, 1.18), mat=m_crystal, parent=staff_root)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_hero_paladin(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_silver = make_mat("RadiantSilver", "#D8E2F0")
        m_silver_mid = make_mat("SteelMid", "#BAC6DA")
        m_gold = make_mat("RadiantGold", "#ECA938")
        m_tabard = make_mat("WhiteTabard", "#F0ECE4")
        m_dark = make_mat("SlateJoint", "#475061")

        rig = create_biped_rig(pose, base_hip_z=0.88, torso_h=0.48, shoulder_x=0.37, shoulder_z_off=0.38)
        add_articulated_legs(
            rig, pose, m_silver_mid, m_silver, m_silver,
            stance_x=0.22, thigh_len=0.36, shin_len=0.38, thigh_r=0.14, shin_r=0.125,
            m_boot_cuff=m_gold,
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("TabardLower", (0.44, 0.34, 0.40), (0.0, -0.02, -0.06), mat=m_tabard, bevel=0.025, parent=pelvis)
        add_box("TabardSunStripe", (0.16, 0.36, 0.38), (0.0, -0.02, -0.06), mat=m_gold, bevel=0.01, parent=pelvis)
        add_box("Belt", (0.50, 0.36, 0.09), (0.0, 0.0, 0.14), mat=m_gold, bevel=0.015, parent=pelvis)

        add_box("PlateTorso", (0.54, 0.36, 0.48), (0.0, 0.0, 0.24), mat=m_silver, bevel=0.045, parent=torso)
        add_box("ChestTabard", (0.30, 0.38, 0.42), (0.0, -0.01, 0.24), mat=m_tabard, bevel=0.02, parent=torso)
        add_sphere("SunburstCrest", 0.11, (0.0, -0.20, 0.29), scale=(1.0, 0.4, 1.0), mat=m_gold, parent=torso)

        for side, sx in (("L", -0.37), ("R", 0.37)):
            add_sphere(f"Pauldron_{side}", 0.19, (sx, 0.0, 0.42), scale=(1.05, 0.95, 0.88), mat=m_silver, parent=torso)
            add_sphere(f"PauldronGold_{side}", 0.18, (sx * 1.04, 0.0, 0.39), scale=(1.08, 0.98, 0.36), mat=m_gold, parent=torso)

        add_cyl("HelmBody", 0.17, 0.32, (0.0, 0.0, 0.08), verts=16, mat=m_silver, parent=head)
        add_sphere("HelmDome", 0.17, (0.0, 0.0, 0.22), scale=(1.0, 1.0, 0.78), mat=m_silver, parent=head)
        add_box("HelmVisor", (0.22, 0.12, 0.09), (0.0, -0.14, 0.10), mat=m_dark, bevel=0.01, parent=head)
        add_box("HelmCrest", (0.06, 0.24, 0.10), (0.0, 0.0, 0.34), mat=m_gold, bevel=0.015, parent=head)

        wing_profile = [(0.0, 0.0), (0.18, 0.12), (0.32, 0.32), (0.24, 0.38), (0.10, 0.24), (-0.02, 0.10)]
        wing_gold_profile = [(0.0, 0.0), (0.13, 0.09), (0.24, 0.24), (0.16, 0.28), (0.06, 0.18), (-0.01, 0.08)]
        for side, sign in (("L", -1.0), ("R", 1.0)):
            w_pts = [(sign * x, z) for x, z in wing_profile]
            wg_pts = [(sign * x, z) for x, z in wing_gold_profile]
            if sign < 0:
                w_pts = list(reversed(w_pts))
                wg_pts = list(reversed(wg_pts))
            add_prism(f"HelmWing_{side}", w_pts, 0.025, loc=(sign * 0.12, 0.02, 0.13), rot_deg=(-12.0, 0.0, sign * -22.0), mat=m_silver, bevel=0.008, parent=head)
            add_prism(f"HelmWingGold_{side}", wg_pts, 0.030, loc=(sign * 0.12, 0.01, 0.12), rot_deg=(-12.0, 0.0, sign * -22.0), mat=m_gold, bevel=0.006, parent=head)

        add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_silver, m_silver_mid, m_silver)
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_silver, m_silver_mid, m_silver)
        pal_rx = (pose.r_sh_fwd + pose.r_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (22.0 + pose.weapon_rot[0])
        pal_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (14.0 + pose.weapon_rot[1])
        add_broadsword(
            "Greatsword",
            (0.02, -0.04, 0.04),
            (pal_rx, pal_ry, pose.weapon_rot[2]),
            m_silver,
            m_dark,
            m_gold,
            blade_len=1.06,
            blade_w=0.12,
            grip_len=0.32,
            parent=hand_r,
        )

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_hero_rogue(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_vest = make_mat("RogueVest", "#4D3C2E")
        m_trousers = make_mat("RogueBrown", "#633C24")
        m_boot = make_mat("DarkBoot", "#2F3A59")
        m_cuff = make_mat("WhiteCuff", "#F0ECE4")
        m_hood = make_mat("IndigoHood", "#3E4C73")
        m_scarf = make_mat("BrightScarf", "#E35259")
        m_dagger = make_mat("SteelDagger", "#D8E2F0")
        m_guard = make_mat("DarkGuard", "#475061")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        rig = create_biped_rig(pose, base_hip_z=0.78, torso_h=0.42, shoulder_x=0.32, shoulder_z_off=0.33)
        add_articulated_legs(
            rig, pose, m_boot, m_trousers, m_trousers,
            stance_x=0.20, thigh_len=0.33, shin_len=0.34, thigh_r=0.125, shin_r=0.11,
            boot_size=(0.18, 0.28, 0.14), m_boot_cuff=m_cuff,
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("Pelvis", (0.44, 0.28, 0.18), (0.0, 0.0, 0.04), mat=m_trousers, bevel=0.02, parent=pelvis)
        add_box("Belt", (0.48, 0.32, 0.08), (0.0, 0.0, 0.11), mat=m_guard, bevel=0.012, parent=pelvis)

        add_box("Torso", (0.46, 0.30, 0.40), (0.0, 0.0, 0.20), mat=m_vest, bevel=0.035, parent=torso)
        add_box("ChestArmor", (0.42, 0.32, 0.30), (0.0, -0.02, 0.22), mat=m_boot, bevel=0.025, parent=torso)
        for side, sx in (("L", -0.32), ("R", 0.32)):
            add_sphere(f"Shoulder_{side}", 0.145, (sx, 0.0, 0.35), scale=(1.0, 0.9, 0.8), mat=m_guard, parent=torso)

        add_sphere("Head", 0.145, (0.0, -0.04, 0.06), mat=m_skin, parent=head)
        # White face mask + dark hood + bright red neck scarf matching f0
        add_cyl("WhiteFaceMask", 0.156, 0.12, (0.0, -0.05, 0.02), verts=14, mat=m_cuff, parent=head)
        add_sphere("Hood", 0.185, (0.0, 0.02, 0.09), scale=(1.06, 1.22, 1.05), mat=m_hood, parent=head)
        add_cone("HoodPoint", 0.15, 0.02, 0.32, (0.0, 0.18, 0.16), rot_deg=(-64.0, 0.0, 0.0), verts=12, mat=m_hood, parent=head)
        add_cyl("ScarfWrap", 0.175, 0.14, (0.0, -0.04, -0.05), verts=14, mat=m_scarf, parent=head)
        add_box("ScarfTail1", (0.14, 0.48, 0.06), (-0.06, 0.24, -0.02), rot_deg=(-12.0 - pose.cape_trail_deg * 0.5, 0.0, 14.0), mat=m_scarf, bevel=0.015, parent=head)
        add_box("ScarfTail2", (0.12, 0.42, 0.05), (0.08, 0.22, -0.06), rot_deg=(-18.0 - pose.cape_trail_deg * 0.5, 0.0, -16.0), mat=m_scarf, bevel=0.015, parent=head)

        # Twin steel daggers (one in each hand!)
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_guard, m_vest, m_skin, upper_len=0.25, fore_len=0.24, radius=0.088)
        dag_l_rx = (pose.l_sh_fwd + pose.l_el_bend + pose.offhand_rot[0]) if pose.weapon_world_mode else (50.0 + pose.offhand_rot[0])
        dag_l_ry = pose.offhand_rot[1] if pose.weapon_world_mode else (10.0 + pose.offhand_rot[1])
        add_curved_blade("Dagger_L", (0.0, -0.04, 0.0), (dag_l_rx, dag_l_ry, pose.offhand_rot[2]), m_dagger, m_vest, m_guard, length=0.44, width=0.065, curve_x=0.07, parent=hand_l)

        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_guard, m_vest, m_skin, upper_len=0.25, fore_len=0.24, radius=0.088)
        dag_r_rx = (pose.r_sh_fwd + pose.r_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (56.0 + pose.weapon_rot[0])
        dag_r_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (18.0 + pose.weapon_rot[1])
        add_curved_blade("Dagger_R", (0.0, -0.04, 0.0), (dag_r_rx, dag_r_ry, pose.weapon_rot[2]), m_dagger, m_vest, m_guard, length=0.46, width=0.065, curve_x=0.08, parent=hand_r)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_giant_frog(anim: str, frame: int, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_skin = make_mat("FrogGreen", "#3D8748")
        m_dark = make_mat("FrogRidge", "#183622")
        m_belly = make_mat("FrogYellow", "#FFD92E")
        m_eye = make_mat("FrogEyeOrange", "#FFC952")
        m_pupil = make_mat("FrogPupil", "#1C2033")
        m_mouth = make_mat("FrogRedMouth", "#C45D33")
        m_tongue = make_mat("FrogTongue", "#E35259")
        m_tongue_tip = make_mat("FrogTongueTip", "#BF7E62")

        # Determine frog motion parameters
        z_lift = 0.0
        body_pitch = 0.0
        body_y = 0.0
        jaw_open = 10.0  # slight open matching f0
        tongue_len = 0.0
        hind_stretch = 0.0
        fore_reach = 0.0
        squash_z = 1.0

        if anim == "walk":
            if frame == 0:
                z_lift, body_pitch, squash_z = 0.0, 6.0, 0.88
            elif frame == 1:
                z_lift, body_pitch, hind_stretch, fore_reach = 0.28, -16.0, 0.42, 0.16
            elif frame == 2:
                z_lift, body_pitch, body_y, fore_reach = 0.10, 10.0, -0.08, 0.24
            else:
                z_lift, body_pitch, squash_z = 0.0, 4.0, 0.84
        elif anim == "attack":
            if frame == 0:
                z_lift, body_pitch, jaw_open, squash_z = 0.0, -6.0, 34.0, 0.90
            elif frame == 1:
                z_lift, body_pitch, body_y, jaw_open, tongue_len = 0.04, 6.0, -0.14, 42.0, 1.05
            elif frame == 2:
                z_lift, body_pitch, body_y, jaw_open, tongue_len = 0.02, 2.0, -0.06, 28.0, 0.45
            else:
                z_lift, body_pitch, jaw_open = 0.0, 0.0, 14.0

        frog_root = make_empty("FrogRoot", (0.0, body_y, z_lift), (body_pitch, 0.0, 0.0))
        add_sphere("FrogTorso", 0.50, (0.0, 0.10, 0.42 * squash_z), scale=(1.15, 1.28, 0.76 * squash_z), rot_deg=(-14.0, 0.0, 0.0), mat=m_skin, parent=frog_root)
        add_sphere("FrogHead", 0.40, (0.0, -0.32, 0.60 * squash_z), scale=(1.18, 1.05, 0.60 * squash_z), rot_deg=(-6.0, 0.0, 0.0), mat=m_skin, parent=frog_root)

        # Open red mouth + bright yellow throat/belly
        jaw_pivot = make_empty("JawPivot", (0.0, -0.26, 0.48 * squash_z), (jaw_open, 0.0, 0.0), parent=frog_root)
        add_box("MouthInterior", (0.64, 0.44, 0.14), (0.0, -0.20, 0.03), mat=m_mouth, bevel=0.03, parent=jaw_pivot)
        add_box("JawRidge", (0.72, 0.36, 0.10), (0.0, -0.28, -0.02), mat=m_belly, bevel=0.03, parent=jaw_pivot)
        add_sphere("ThroatSac", 0.28, (0.0, -0.20, -0.12), scale=(1.12, 1.06, 0.88), mat=m_belly, parent=jaw_pivot)

        # Parent tongue to frog_root so it lashes horizontally forward-West without dipping into bottom edge
        if tongue_len > 0.05:
            pts = [
                (0.0, -0.44, 0.48 * squash_z, 0.068),
                (-0.10 * tongue_len, -0.44 - tongue_len * 0.45, 0.44, 0.065),
                (-0.20 * tongue_len, -0.44 - tongue_len * 0.88, 0.38, 0.062),
                (-0.24 * tongue_len, -0.44 - tongue_len, 0.36, 0.088),
            ]
            add_tube_curve("FrogTongue", pts, mat=m_tongue, parent=frog_root)
            add_sphere("FrogTongueTip", 0.10, (-0.24 * tongue_len, -0.44 - tongue_len, 0.36), mat=m_tongue_tip, parent=frog_root)

        for side, sx in (("L", -0.24), ("R", 0.24)):
            add_sphere(f"EyeRidge_{side}", 0.15, (sx, -0.36, 0.82 * squash_z), scale=(1.0, 1.0, 0.92), mat=m_skin, parent=frog_root)
            add_sphere(f"EyeOrb_{side}", 0.105, (sx * 1.02, -0.44, 0.84 * squash_z), mat=m_eye, parent=frog_root)
            add_box(f"EyePupil_{side}", (0.09, 0.04, 0.035), (sx * 1.03, -0.53, 0.84 * squash_z), mat=m_pupil, bevel=0.005, parent=frog_root)

        for idx, (wx, wy, wz, wr) in enumerate((
            (-0.18, 0.18, 0.72, 0.105),
            (0.20, 0.16, 0.71, 0.105),
            (-0.14, 0.40, 0.58, 0.095),
            (0.16, 0.42, 0.56, 0.095),
            (0.0, 0.28, 0.68, 0.11),
        )):
            add_sphere(f"Wart_{idx}", wr, (wx, wy, wz * squash_z), scale=(1.0, 1.1, 0.68), mat=m_dark, parent=frog_root)

        for side, sx in (("L", -0.54), ("R", 0.54)):
            add_sphere(
                f"HindThigh_{side}", 0.30,
                (sx, 0.28 + hind_stretch * 0.45, 0.34 * squash_z - hind_stretch * 0.15),
                scale=(0.76, 1.24 + hind_stretch * 0.6, 0.82),
                rot_deg=(12.0 + hind_stretch * 55.0, 0.0, -18.0 if side == "L" else 18.0),
                mat=m_skin, parent=frog_root,
            )
            add_box(
                f"HindFoot_{side}", (0.32, 0.46, 0.10),
                (sx * 1.04, 0.14 + hind_stretch * 0.85, 0.05 - hind_stretch * 0.28),
                rot_deg=(hind_stretch * 60.0, 0.0, -24.0 if side == "L" else 24.0),
                mat=m_belly, bevel=0.03, parent=frog_root,
            )
            add_cyl(
                f"ForeArm_{side}", 0.11, 0.42,
                (sx * 0.72, -0.36 - fore_reach * 0.5, 0.22),
                rot_deg=(18.0 + fore_reach * 110.0, 12.0 if side == "L" else -12.0, 0.0),
                verts=12, mat=m_skin, parent=frog_root,
            )
            add_box(
                f"ForeFoot_{side}", (0.24, 0.26, 0.085),
                (sx * 0.78, -0.50 - fore_reach * 0.9, 0.045 - fore_reach * 0.05),
                rot_deg=(0.0, 0.0, -15.0 if side == "L" else 15.0),
                mat=m_belly, bevel=0.025, parent=frog_root,
            )

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_goblin_raider(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_gob_head = make_mat("GoblinBright", "#B4EC86")
        m_gob_skin = make_mat("GoblinSkin", "#85B860")
        m_harness = make_mat("PaleCyanHarness", "#BDF7EE")
        m_kilt = make_mat("TatteredKilt", "#633C24")
        m_boot = make_mat("DarkLeather", "#3B281E")
        m_steel = make_mat("ScimitarSteel", "#D8E2F0")
        m_iron = make_mat("BucklerIron", "#475061")
        m_eye = make_mat("GoblinEye", "#FFE07D")

        rig = create_biped_rig(pose, base_hip_z=0.66, torso_h=0.34, shoulder_x=0.30, shoulder_z_off=0.28)
        add_articulated_legs(
            rig, pose, m_boot, m_gob_skin, m_gob_skin,
            stance_x=0.19, thigh_len=0.28, shin_len=0.29, thigh_r=0.12, shin_r=0.105,
            boot_size=(0.17, 0.26, 0.13),
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("Loincloth", (0.42, 0.28, 0.22), (0.0, -0.02, -0.02), mat=m_kilt, bevel=0.025, parent=pelvis)
        add_box("Belt", (0.46, 0.30, 0.08), (0.0, -0.02, 0.09), mat=m_boot, bevel=0.015, parent=pelvis)

        add_sphere("GobTorso", 0.25, (0.0, -0.04, 0.18), scale=(1.05, 0.85, 1.05), mat=m_gob_skin, parent=torso)
        # Pale cyan chest harness matching f0
        add_box("CyanHarness", (0.44, 0.30, 0.30), (0.0, -0.05, 0.18), mat=m_harness, bevel=0.025, parent=torso)
        for side, sx in (("L", -0.30), ("R", 0.30)):
            add_sphere(f"Shoulder_{side}", 0.135, (sx, -0.04, 0.30), scale=(1.0, 0.9, 0.85), mat=m_harness if side == "R" else m_gob_skin, parent=torso)

        add_sphere("GobHead", 0.19, (0.0, -0.06, 0.06), scale=(1.08, 1.05, 0.92), mat=m_gob_head, parent=head)
        add_cone("GobNose", 0.06, 0.015, 0.14, (0.0, -0.24, 0.04), rot_deg=(-105.0, 0.0, 0.0), verts=10, mat=m_gob_skin, parent=head)
        for side, sx in (("L", -0.08), ("R", 0.08)):
            add_sphere(f"GobEye_{side}", 0.042, (sx, -0.23, 0.09), mat=m_eye, parent=head)
            add_cone(
                f"GobEar_{side}", 0.08, 0.01, 0.30,
                (sx * 3.1, -0.02, 0.10),
                rot_deg=(12.0, -78.0 if sx < 0 else 78.0, 0.0),
                verts=10, mat=m_gob_head, parent=head,
            )

        # Left hand (-X, West front): curved steel scimitar
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_gob_skin, m_gob_skin, m_gob_head, upper_len=0.22, fore_len=0.21, radius=0.085)
        scim_rx = (pose.l_sh_fwd + pose.l_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (24.0 + pose.weapon_rot[0])
        scim_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (-16.0 + pose.weapon_rot[1])
        add_curved_blade("GobScimitar", (-0.02, -0.04, 0.02), (scim_rx, scim_ry, pose.weapon_rot[2]), m_steel, m_kilt, m_iron, length=0.58, width=0.09, curve_x=-0.12, parent=hand_l)

        # Right hand (+X, back): round dark buckler
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_harness, m_gob_skin, m_gob_head, upper_len=0.22, fore_len=0.21, radius=0.085)
        add_round_shield("GobBuckler", (0.04, -0.04, 0.0), (-8.0, 0.0, -24.0), 0.22, m_harness, m_iron, parent=hand_r)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_goblin_archer(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_gob = make_mat("GoblinSkin", "#2E693B")
        m_gob_dk = make_mat("GoblinShadow", "#1E4728")
        m_hood = make_mat("BrownHood", "#73311D")
        m_jerkin = make_mat("LeatherJerkin", "#633C24")
        m_bow = make_mat("ShortbowWood", "#825132")
        m_arrow_wht = make_mat("WhiteArrow", "#F0ECE4")
        m_steel = make_mat("ArrowSteel", "#D8E2F0")
        m_eye = make_mat("GoblinEye", "#FFC952")

        rig = create_biped_rig(pose, base_hip_z=0.66, torso_h=0.34, shoulder_x=0.29, shoulder_z_off=0.28)
        add_articulated_legs(
            rig, pose, m_hood, m_gob, m_gob,
            stance_x=0.18, thigh_len=0.28, shin_len=0.29, thigh_r=0.115, shin_r=0.10,
            boot_size=(0.17, 0.26, 0.13),
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("Pelvis", (0.42, 0.26, 0.20), (0.0, -0.02, 0.0), mat=m_hood, bevel=0.02, parent=pelvis)
        add_box("Belt", (0.45, 0.29, 0.075), (0.0, -0.02, 0.10), mat=m_jerkin, bevel=0.012, parent=pelvis)

        add_box("Jerkin", (0.44, 0.30, 0.34), (0.0, -0.04, 0.18), mat=m_jerkin, bevel=0.03, parent=torso)
        add_box("HoodMantle", (0.50, 0.34, 0.12), (0.0, -0.01, 0.32), mat=m_hood, bevel=0.025, parent=torso)

        quiv = make_empty("GobQuiverRoot", (0.16, 0.18, 0.26), (16.0, 24.0, 0.0), parent=torso)
        add_cyl("GobQuiver", 0.078, 0.46, (0.0, 0.0, 0.0), verts=12, mat=m_jerkin, parent=quiv)
        for ax, ay in ((-0.025, 0.02), (0.025, -0.02), (0.0, 0.03)):
            add_cyl(f"GQArrow_{ax}", 0.015, 0.18, (ax, ay, 0.29), verts=8, mat=m_bow, parent=quiv)
            add_box(f"GQFletch_{ax}", (0.045, 0.015, 0.085), (ax, ay, 0.38), mat=m_arrow_wht, bevel=0.004, parent=quiv)

        add_sphere("GobHead", 0.18, (0.0, -0.06, 0.06), scale=(1.04, 1.0, 0.92), mat=m_gob, parent=head)
        add_cone("GobNose", 0.052, 0.012, 0.12, (0.0, -0.23, 0.04), rot_deg=(-102.0, 0.0, 0.0), verts=10, mat=m_gob_dk, parent=head)
        add_sphere("RaggedHood", 0.20, (0.0, 0.0, 0.10), scale=(1.06, 1.15, 0.98), mat=m_hood, parent=head)
        add_cone("HoodTail", 0.15, 0.02, 0.24, (0.0, 0.18, 0.16), rot_deg=(-60.0, 0.0, 0.0), verts=10, mat=m_hood, parent=head)
        for side, sx in (("L", -0.075), ("R", 0.075)):
            add_sphere(f"GobEye_{side}", 0.038, (sx, -0.22, 0.08), mat=m_eye, parent=head)
            add_cone(
                f"GobEar_{side}", 0.075, 0.01, 0.32,
                (sx * 3.3, -0.03, 0.08),
                rot_deg=(10.0, -82.0 if sx < 0 else 82.0, 0.0),
                verts=10, mat=m_gob, parent=head,
            )

        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_gob, m_gob, m_gob, upper_len=0.22, fore_len=0.21, radius=0.082)
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_gob, m_gob, m_gob, upper_len=0.22, fore_len=0.21, radius=0.082)

        # Undo arm pitch so bow stands vertical and arrow aims West/forward
        bow_undo_x = pose.l_sh_fwd + pose.l_el_bend
        bow_root = make_empty("GobBowRoot", (-0.02, -0.04, 0.0), (bow_undo_x + pose.weapon_rot[0], pose.weapon_rot[1] - 8.0, pose.weapon_rot[2] - 24.0), parent=hand_l)
        add_tube_curve(
            "GobShortbow",
            [
                (0.0, 0.18, -0.46, 0.024),
                (0.0, 0.04, -0.30, 0.032),
                (0.0, -0.06, -0.14, 0.038),
                (0.0, -0.08, 0.0, 0.042),
                (0.0, -0.06, 0.14, 0.038),
                (0.0, 0.04, 0.30, 0.032),
                (0.0, 0.18, 0.46, 0.024),
            ],
            mat=m_bow,
            parent=bow_root,
        )
        if pose.prop_fx != "bow_empty":
            fly_y = -pose.arrow_fly_dist
            draw_y = 0.12 if pose.prop_fx == "bow_draw" else 0.0
            add_cyl("GobArrowShaft", 0.016, 0.66, (0.0, -0.12 + draw_y + fly_y, 0.02), rot_deg=(90.0, 0.0, 0.0), verts=8, mat=m_bow, parent=bow_root)
            add_cone("GobArrowTip", 0.040, 0.004, 0.11, (0.0, -0.48 + draw_y + fly_y, 0.02), rot_deg=(-90.0, 0.0, 0.0), verts=4, mat=m_steel, smooth=False, parent=bow_root)
            add_box("GobArrowFletch", (0.055, 0.10, 0.02), (0.0, 0.20 + draw_y + fly_y, 0.02), mat=m_arrow_wht, bevel=0.004, parent=bow_root)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_orc_warrior(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_orc = make_mat("OrcMoss", "#426643")
        m_orc_hi = make_mat("OrcOchre", "#5A8254")
        m_iron = make_mat("SlateIron", "#1C2033")
        m_iron_dk = make_mat("DarkIron", "#11131F")
        m_blade = make_mat("CleaverSteel", "#606B80")
        m_kilt = make_mat("WarKilt", "#701D2B")
        m_tusk = make_mat("IvoryTusk", "#F0DEC2")
        m_eye = make_mat("OrcEye", "#E35259")
        m_haft = make_mat("AxeHaft", "#633C24")

        rig = create_biped_rig(pose, base_hip_z=0.94, torso_h=0.54, shoulder_x=0.45, shoulder_z_off=0.44)
        add_articulated_legs(
            rig, pose, m_iron_dk, m_iron, m_iron_dk,
            stance_x=0.25, thigh_len=0.38, shin_len=0.40, thigh_r=0.165, shin_r=0.145,
            boot_size=(0.24, 0.36, 0.17), m_greave=m_iron,
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_cone("WarSkirt", 0.44, 0.32, 0.40, (0.0, 0.0, -0.06), verts=14, mat=m_kilt, parent=pelvis)
        add_box("OrcBelt", (0.62, 0.42, 0.12), (0.0, 0.0, 0.14), mat=m_iron_dk, bevel=0.02, parent=pelvis)
        add_box("BeltBuckle", (0.18, 0.08, 0.14), (0.0, -0.21, 0.14), mat=m_blade, bevel=0.015, parent=pelvis)

        add_box("OrcChest", (0.72, 0.46, 0.54), (0.0, 0.0, 0.27), mat=m_iron, bevel=0.06, parent=torso)
        for sx in (-0.18, 0.18):
            add_sphere(f"Pec_{sx}", 0.19, (sx, -0.14, 0.35), scale=(1.05, 0.65, 0.85), mat=m_orc, parent=torso)
        add_box("ChestStrap", (0.74, 0.48, 0.11), (0.0, 0.0, 0.29), rot_deg=(0.0, 28.0, 0.0), mat=m_iron_dk, bevel=0.015, parent=torso)

        for side, sx in (("L", -0.46), ("R", 0.46)):
            add_sphere(f"OrcPauldron_{side}", 0.24, (sx, 0.0, 0.48), scale=(1.1, 1.0, 0.85), mat=m_iron, parent=torso)
            add_cone(f"PauldronSpike_{side}", 0.055, 0.008, 0.18, (sx * 1.12, 0.0, 0.64), rot_deg=(0.0, -24.0 if sx < 0 else 24.0, 0.0), verts=8, mat=m_blade, parent=torso)

        add_sphere("OrcHead", 0.21, (0.0, -0.04, 0.08), scale=(1.05, 1.05, 0.96), mat=m_orc, parent=head)
        add_box("OrcBrow", (0.34, 0.18, 0.10), (0.0, -0.18, 0.16), mat=m_orc_hi, bevel=0.02, parent=head)
        add_box("OrcJaw", (0.34, 0.24, 0.14), (0.0, -0.14, -0.02), mat=m_orc_hi, bevel=0.025, parent=head)
        for side, sx in (("L", -0.09), ("R", 0.09)):
            add_sphere(f"OrcEye_{side}", 0.036, (sx, -0.23, 0.10), mat=m_eye, parent=head)
            add_cone(f"OrcTusk_{side}", 0.036, 0.008, 0.15, (sx * 1.15, -0.24, 0.06), rot_deg=(-18.0, 0.0, 0.0), verts=8, mat=m_tusk, parent=head)
            add_cone(f"OrcEar_{side}", 0.05, 0.008, 0.16, (sx * 2.4, 0.0, 0.12), rot_deg=(0.0, -76.0 if sx < 0 else 76.0, 0.0), verts=8, mat=m_orc, parent=head)

        add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_iron, m_orc, m_iron_dk, upper_len=0.30, fore_len=0.28, radius=0.125)
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_iron, m_orc, m_iron_dk, upper_len=0.30, fore_len=0.28, radius=0.125)

        axe_rx = (pose.l_sh_fwd + pose.l_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (18.0 + pose.weapon_rot[0])
        axe_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (-12.0 + pose.weapon_rot[1])
        axe_root = make_empty("BattleaxeRoot", (-0.08, -0.06, 0.06), (axe_rx, axe_ry, pose.weapon_rot[2]), parent=hand_l)
        add_cyl("AxeHaft", 0.048, 1.24, (0.0, 0.0, 0.20), verts=12, mat=m_haft, parent=axe_root)
        add_box("AxeSocket", (0.14, 0.14, 0.22), (0.0, 0.0, 0.68), mat=m_iron_dk, bevel=0.015, parent=axe_root)
        axe_profile = [(-0.04, 0.56), (-0.42, 0.42), (-0.50, 0.68), (-0.42, 0.96), (-0.04, 0.82)]
        add_prism("AxeHead", axe_profile, 0.036, mat=m_blade, bevel=0.012, parent=axe_root)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_giant_rat(anim: str, frame: int, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_fur = make_mat("RatFur", "#633C24")
        m_fur_dk = make_mat("RatFurDark", "#4D2014")
        m_pink = make_mat("RatPink", "#BF7E62")
        m_fang = make_mat("RatFang", "#FFFFFF")
        m_mouth = make_mat("RatMouth", "#E35259")
        m_eye = make_mat("RatCrimson", "#E35259")

        z_lift = 0.0
        body_y = 0.0
        spine_pitch = 0.0
        stretch_y = 1.0
        arch_z = 1.0
        jaw_open = 6.0
        fore_fwd = 0.0
        hind_back = 0.0
        tail_lift = 0.0

        if anim == "walk":
            if frame == 0:
                stretch_y, fore_fwd, hind_back, tail_lift = 1.12, 0.16, 0.14, 0.04
            elif frame == 1:
                z_lift, arch_z, stretch_y, fore_fwd, hind_back, tail_lift = 0.06, 1.18, 0.86, -0.10, -0.12, 0.18
            elif frame == 2:
                stretch_y, fore_fwd, hind_back, tail_lift = 1.08, -0.06, 0.18, -0.04
            else:
                z_lift, arch_z, fore_fwd, hind_back, tail_lift = 0.02, 0.94, 0.10, -0.08, 0.10
        elif anim == "attack":
            if frame == 0:
                spine_pitch, arch_z, stretch_y, jaw_open, fore_fwd = -14.0, 1.12, 0.88, 32.0, -0.08
            elif frame == 1:
                z_lift, body_y, spine_pitch, stretch_y, jaw_open, fore_fwd, hind_back, tail_lift = 0.16, -0.22, 10.0, 1.16, 48.0, 0.24, 0.22, 0.22
            elif frame == 2:
                body_y, spine_pitch, jaw_open, fore_fwd = -0.14, 18.0, 16.0, 0.16
            else:
                body_y, spine_pitch, jaw_open = 0.04, -4.0, 8.0

        rat_root = make_empty("RatRoot", (0.0, body_y, z_lift), (spine_pitch, 0.0, 0.0))
        add_sphere("RatBody", 0.43, (0.0, 0.10, 0.39 * arch_z), scale=(0.82, 1.44 * stretch_y, 0.78 * arch_z), rot_deg=(-10.0, 0.0, 0.0), mat=m_fur, parent=rat_root)
        add_sphere("RatRump", 0.37, (0.0, 0.42 * stretch_y, 0.35 * arch_z), scale=(0.88, 1.05, 0.85), mat=m_fur_dk, parent=rat_root)
        for idx, (wy, wz, wr) in enumerate(((-0.06, 0.66, 0.14), (0.14, 0.70, 0.15), (0.34, 0.64, 0.13))):
            add_cone(f"Bristle_{idx}", wr * 0.75, 0.015, 0.22, (0.0, wy, wz * arch_z), rot_deg=(28.0, 0.0, 0.0), verts=8, mat=m_fur_dk, parent=rat_root)

        head_y = -0.34 * stretch_y
        head_z = 0.45 * arch_z
        add_sphere("RatHead", 0.26, (0.0, head_y, head_z), scale=(0.86, 1.18, 0.76), rot_deg=(10.0, 0.0, 0.0), mat=m_fur, parent=rat_root)
        add_cone("RatSnout", 0.18, 0.055, 0.38, (0.0, head_y - 0.26, head_z - 0.04), rot_deg=(-98.0, 0.0, 0.0), verts=14, mat=m_fur, parent=rat_root)
        add_sphere("RatNose", 0.055, (0.0, head_y - 0.46, head_z - 0.06), mat=m_pink, parent=rat_root)

        # Lower jaw + bright mouth interior + upper/lower white fangs
        jaw = make_empty("RatJaw", (0.0, head_y - 0.10, head_z - 0.08), (jaw_open, 0.0, 0.0), parent=rat_root)
        add_box("RatLowerJaw", (0.20, 0.34, 0.085), (0.0, -0.16, -0.025), mat=m_fur_dk, bevel=0.02, parent=jaw)
        if jaw_open >= 20.0:
            add_box("RatMouthGape", (0.17, 0.26, 0.09), (0.0, -0.18, 0.025), mat=m_mouth, bevel=0.02, parent=jaw)
        fang_scale = 1.38 if (anim == "attack" and frame == 1) else 1.0
        for side, sx in (("L", -0.10), ("R", 0.10)):
            add_sphere(f"RatEye_{side}", 0.045, (sx * 1.1, head_y - 0.14, head_z + 0.06), mat=m_eye, parent=rat_root)
            add_cone(f"RatFang_{side}", 0.032 * fang_scale, 0.005, 0.12 * fang_scale, (sx * 0.72, head_y - 0.36, head_z - 0.11), rot_deg=(168.0, 0.0, 0.0), verts=6, mat=m_fang, parent=rat_root)
            if jaw_open >= 20.0:
                add_cone(f"RatLowerFang_{side}", 0.028 * fang_scale, 0.004, 0.10 * fang_scale, (sx * 0.68, -0.28, 0.05), rot_deg=(-12.0, 0.0, 0.0), verts=6, mat=m_fang, parent=jaw)
            add_cyl(f"RatEar_{side}", 0.115, 0.04, (sx * 1.7, head_y + 0.10, head_z + 0.16), rot_deg=(65.0, 0.0, -28.0 if sx < 0 else 28.0), verts=14, mat=m_pink, parent=rat_root)

        for side, sx in (("L", -0.34), ("R", 0.34)):
            alt = 0.08 if (anim == "walk" and frame in (0, 2) and side == "L") else (-0.08 if (anim == "walk" and frame in (0, 2) and side == "R") else 0.0)
            add_sphere(f"HindLeg_{side}", 0.23, (sx, 0.36 + hind_back + alt, 0.23), scale=(0.70, 1.15, 0.85), mat=m_fur_dk, parent=rat_root)
            add_box(f"HindPaw_{side}", (0.16, 0.28, 0.09), (sx * 1.02, 0.26 + hind_back * 1.2 + alt, 0.045), mat=m_pink, bevel=0.02, parent=rat_root)
            add_cyl(f"ForeLeg_{side}", 0.095, 0.30, (sx * 0.82, -0.28 - fore_fwd - alt, 0.17), rot_deg=(16.0 + fore_fwd * 120.0, 0.0, 0.0), verts=10, mat=m_fur_dk, parent=rat_root)
            add_box(f"ForePaw_{side}", (0.14, 0.24, 0.08), (sx * 0.84, -0.38 - fore_fwd * 1.3 - alt, 0.04), mat=m_pink, bevel=0.02, parent=rat_root)

        add_tube_curve(
            "RatTail",
            [
                (0.0, 0.66 * stretch_y, 0.26, 0.075),
                (0.28, 0.88 * stretch_y, 0.20 + tail_lift * 0.4, 0.064),
                (0.56, 0.92 * stretch_y, 0.28 + tail_lift * 0.8, 0.054),
                (0.74, 0.76 * stretch_y, 0.44 + tail_lift, 0.044),
                (0.62, 0.60 * stretch_y, 0.58 + tail_lift * 1.1, 0.034),
            ],
            mat=m_fur,
            parent=rat_root,
        )

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_skeleton_warrior(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_bone_glow = make_mat("CyanBone", "#BDF7EE")
        m_bone_cool = make_mat("SlateBone", "#9AA7BD")
        m_socket = make_mat("DarkSocket", "#1C2033")
        m_cloth = make_mat("IvoryCloth", "#F0ECE4")
        m_gold = make_mat("GoldTrim", "#ECA938")
        m_shield_face = make_mat("ShieldBlue", "#3E4C73")
        m_shield_rim = make_mat("ShieldIron", "#475061")

        rig = create_biped_rig(pose, base_hip_z=0.86, torso_h=0.46, shoulder_x=0.34, shoulder_z_off=0.36)
        add_articulated_legs(
            rig, pose, m_bone_cool, m_bone_glow, m_bone_glow,
            stance_x=0.20, thigh_len=0.35, shin_len=0.37, thigh_r=0.118, shin_r=0.108,
            boot_size=(0.19, 0.29, 0.13),
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("PelvisBone", (0.44, 0.26, 0.18), (0.0, 0.0, 0.02), mat=m_bone_cool, bevel=0.03, parent=pelvis)
        # White & gold-trimmed waist loincloth matching f0 (NO red cloth)
        add_box("LoinclothWhite", (0.38, 0.29, 0.30), (0.0, -0.02, -0.10), mat=m_cloth, bevel=0.02, parent=pelvis)
        add_box("LoinclothGoldHem", (0.40, 0.31, 0.065), (0.0, -0.02, -0.22), mat=m_gold, bevel=0.01, parent=pelvis)
        add_box("WaistBelt", (0.46, 0.31, 0.075), (0.0, -0.01, 0.07), mat=m_gold, bevel=0.012, parent=pelvis)

        add_cyl("Spine", 0.088, 0.52, (0.0, 0.06, 0.22), verts=12, mat=m_bone_cool, parent=torso)
        add_box("RibCore", (0.34, 0.22, 0.34), (0.0, 0.0, 0.24), mat=m_socket, bevel=0.03, parent=torso)
        add_box("Sternum", (0.10, 0.28, 0.32), (0.0, -0.02, 0.25), mat=m_bone_glow, bevel=0.015, parent=torso)
        for rz in (0.12, 0.24, 0.36):
            add_box(f"Rib_{rz}", (0.48, 0.30, 0.065), (0.0, -0.01, rz), mat=m_bone_glow, bevel=0.02, parent=torso)
        add_box("Clavicle", (0.58, 0.18, 0.085), (0.0, -0.04, 0.44), mat=m_bone_glow, bevel=0.02, parent=torso)

        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"ShoulderBone_{side}", 0.115, (sx, 0.0, 0.42), mat=m_bone_cool, parent=torso)

        # Bare glowing cyan-tinted skull matching f0 (NO pot helm!)
        add_sphere("SkullCranium", 0.175, (0.0, -0.02, 0.10), scale=(0.98, 1.06, 1.0), mat=m_bone_glow, parent=head)
        add_box("SkullJaw", (0.20, 0.20, 0.12), (0.0, -0.09, -0.03), mat=m_bone_cool, bevel=0.02, parent=head)
        for side, sx in (("L", -0.065), ("R", 0.065)):
            add_sphere(f"EyeSocket_{side}", 0.050, (sx, -0.17, 0.07), mat=m_socket, parent=head)

        # Forward arm (-X): Bony clawed arm matching f0 (NO sword!)
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_bone_glow, m_bone_cool, m_bone_glow, radius=0.082)
        claw_scale = 1.45 if pose.prop_fx == "claws_extended" else 1.0
        for cx in (-0.06, 0.0, 0.06):
            add_cone(f"BonyClaw_{cx}", 0.028 * claw_scale, 0.004, 0.18 * claw_scale, (cx, -0.09 * claw_scale, -0.02), rot_deg=(-85.0, 0.0, 0.0), verts=6, mat=m_bone_glow, parent=hand_l)

        # Back arm (+X): Dark round shield matching f0
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_bone_glow, m_bone_cool, m_bone_glow, radius=0.082)
        add_round_shield("SkelShield", (0.04, -0.04, 0.0), (-8.0, 0.0, -26.0), 0.30, m_shield_face, m_shield_rim, parent=hand_r)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_monster_bandit_cutthroat(pose: BipedPose, stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_bandana_dk = make_mat("BandanaDeep", "#701D2B")
        m_bandana_lt = make_mat("BandanaRed", "#C23846")
        m_mantle = make_mat("TanMantle", "#6B5542")
        m_vest = make_mat("MaroonVest", "#4A141E")
        m_gold = make_mat("GoldStripe", "#ECA938")
        m_trousers = make_mat("DarkBluePants", "#1C2952")
        m_boots = make_mat("BrownBoots", "#633C24")
        m_blade = make_mat("StraightSteel", "#D8E2F0")
        m_iron = make_mat("GuardSteel", "#9AA7BD")

        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.45, shoulder_x=0.34, shoulder_z_off=0.36)
        add_articulated_legs(
            rig, pose, m_boots, m_trousers, m_trousers,
            stance_x=0.20, thigh_len=0.35, shin_len=0.36, thigh_r=0.135, shin_r=0.118,
            m_boot_cuff=m_boots,
        )
        pelvis = rig["pelvis"]
        torso = rig["torso"]
        head = rig["head"]

        add_box("Pelvis", (0.46, 0.30, 0.20), (0.0, 0.0, 0.04), mat=m_trousers, bevel=0.025, parent=pelvis)
        add_box("Belt", (0.50, 0.34, 0.09), (0.0, 0.0, 0.13), mat=m_boots, bevel=0.015, parent=pelvis)
        add_box("BeltBuckle", (0.12, 0.06, 0.10), (0.0, -0.17, 0.13), mat=m_iron, bevel=0.01, parent=pelvis)

        # Maroon & gold striped vest + tan leather shoulder mantle matching f0
        add_box("VestBody", (0.48, 0.34, 0.44), (0.0, -0.01, 0.22), mat=m_vest, bevel=0.04, parent=torso)
        for sz in (0.14, 0.24, 0.34):
            add_box(f"GoldRibStripe_{sz}", (0.34, 0.36, 0.045), (0.0, -0.02, sz), mat=m_gold, bevel=0.008, parent=torso)
        add_box("TanMantle", (0.62, 0.38, 0.20), (0.0, 0.02, 0.36), mat=m_mantle, bevel=0.035, parent=torso)
        add_box("AsymCape", (0.44, 0.10, 0.88), (0.10, 0.22 + 0.008 * pose.cape_trail_deg, -0.02), rot_deg=(-16.0 - pose.cape_trail_deg * 0.6, 10.0, -10.0), mat=m_mantle, bevel=0.03, parent=torso)
        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"Shoulder_{side}", 0.16, (sx, 0.0, 0.39), scale=(1.05, 0.95, 0.85), mat=m_mantle, parent=torso)

        # Crimson bandana/hood mask covering head matching f0
        add_sphere("BandanaHead", 0.175, (0.0, -0.02, 0.07), scale=(1.04, 1.08, 1.0), mat=m_bandana_lt, parent=head)
        add_cyl("BandanaMask", 0.172, 0.14, (0.0, -0.04, 0.01), verts=14, mat=m_bandana_dk, parent=head)
        add_cone("BandanaTail", 0.10, 0.02, 0.24, (0.06, 0.18, 0.06), rot_deg=(-70.0, 0.0, -14.0), verts=10, mat=m_bandana_lt, parent=head)

        # Left hand (-X): Straight steel sword matching f0
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, pose.l_sh_yaw, pose.l_el_bend, m_mantle, m_vest, m_boots)
        band_rx = (pose.l_sh_fwd + pose.l_el_bend + pose.weapon_rot[0]) if pose.weapon_world_mode else (24.0 + pose.weapon_rot[0])
        band_ry = pose.weapon_rot[1] if pose.weapon_world_mode else (-14.0 + pose.weapon_rot[1])
        add_broadsword(
            "BanditSword",
            (-0.02, -0.04, 0.04),
            (band_rx, band_ry, pose.weapon_rot[2]),
            m_blade,
            m_boots,
            m_iron,
            blade_len=0.78,
            blade_w=0.085,
            grip_len=0.22,
            parent=hand_l,
        )

        # Right hand (+X)
        add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_mantle, m_vest, m_boots)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    # -----------------------------------------------------------------------
    # 4 Bywater Shopkeeper NPCs (East-facing yaw = -28.0, ortho_scale = 2.8)
    # -----------------------------------------------------------------------

    def build_npc_blacksmith(stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_apron = make_mat("SmithApron", "#633C24")
        m_apron_dk = make_mat("SmithApronDark", "#4D2014")
        m_shirt = make_mat("CreamShirt", "#F0ECE4")
        m_pants = make_mat("CharcoalPants", "#3B3837")
        m_skin = make_mat("WarmSkin", "#EDB89A")
        m_beard = make_mat("DarkBeard", "#3B281E")
        m_iron = make_mat("HammerIron", "#475061")
        m_steel = make_mat("HammerSteel", "#9AA7BD")
        m_wood = make_mat("HaftWood", "#825132")

        pose = BipedPose(
            l_thigh_fwd=6.0, l_knee_bend=8.0, r_thigh_fwd=-6.0, r_knee_bend=8.0,
            l_sh_fwd=38.0, l_sh_out=16.0, l_el_bend=42.0,
            r_sh_fwd=58.0, r_sh_out=26.0, r_sh_yaw=14.0, r_el_bend=38.0,
        )
        rig = create_biped_rig(pose, base_hip_z=0.86, torso_h=0.48, shoulder_x=0.39, shoulder_z_off=0.38)
        add_articulated_legs(rig, pose, m_apron_dk, m_pants, m_pants, stance_x=0.22, thigh_len=0.35, shin_len=0.37, thigh_r=0.145, shin_r=0.125)

        pelvis, torso, head = rig["pelvis"], rig["torso"], rig["head"]
        add_box("Pelvis", (0.54, 0.34, 0.22), (0.0, 0.0, 0.04), mat=m_pants, bevel=0.03, parent=pelvis)
        add_box("ApronLower", (0.44, 0.36, 0.42), (0.0, -0.02, -0.08), mat=m_apron, bevel=0.025, parent=pelvis)
        add_box("Belt", (0.56, 0.37, 0.10), (0.0, 0.0, 0.14), mat=m_apron_dk, bevel=0.015, parent=pelvis)

        # Stout broad torso + leather apron over cream shirt + rolled sleeves
        add_box("ShirtTorso", (0.62, 0.40, 0.48), (0.0, 0.0, 0.24), mat=m_shirt, bevel=0.05, parent=torso)
        add_box("ApronBib", (0.42, 0.42, 0.44), (0.0, -0.02, 0.22), mat=m_apron, bevel=0.03, parent=torso)
        for side, sx in (("L", -0.39), ("R", 0.39)):
            add_sphere(f"RolledSleeve_{side}", 0.16, (sx, 0.0, 0.38), scale=(1.0, 0.95, 0.88), mat=m_shirt, parent=torso)

        add_sphere("Head", 0.165, (0.0, -0.04, 0.08), mat=m_skin, parent=head)
        add_sphere("HairCap", 0.17, (0.0, 0.02, 0.14), scale=(1.05, 1.08, 0.82), mat=m_beard, parent=head)
        add_tube_curve("DarkBeard", [(0.0, -0.16, 0.02, 0.12), (0.0, -0.18, -0.14, 0.10), (0.0, -0.16, -0.26, 0.06)], mat=m_beard, parent=head)

        # Left hand (-X): Iron forging tongs
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, 0.0, pose.l_el_bend, m_shirt, m_skin, m_skin, radius=0.105)
        tongs = make_empty("TongsRoot", (-0.02, -0.04, 0.0), (42.0, -10.0, 0.0), parent=hand_l)
        add_cyl("TongArm1", 0.024, 0.54, (-0.03, 0.0, 0.14), rot_deg=(0.0, -6.0, 0.0), verts=8, mat=m_iron, parent=tongs)
        add_cyl("TongArm2", 0.024, 0.54, (0.03, 0.0, 0.14), rot_deg=(0.0, 6.0, 0.0), verts=8, mat=m_iron, parent=tongs)

        # Right hand (+X): Raised iron forging hammer
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_shirt, m_skin, m_skin, radius=0.105)
        hammer = make_empty("HammerRoot", (0.04, -0.04, 0.02), (pose.r_sh_fwd + pose.r_el_bend - 14.0, 22.0, 0.0), parent=hand_r)
        add_cyl("HammerHaft", 0.036, 0.56, (0.0, 0.0, 0.18), verts=10, mat=m_wood, parent=hammer)
        add_box("HammerHead", (0.16, 0.30, 0.15), (0.0, 0.0, 0.44), mat=m_steel, bevel=0.02, parent=hammer)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_npc_tanner(stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_hood = make_mat("MossyCloak", "#426643")
        m_vest = make_mat("TanHide", "#A36943")
        m_satchel = make_mat("BrownSatchel", "#633C24")
        m_boots = make_mat("SturdyBoots", "#4D2014")
        m_pants = make_mat("TannerPants", "#3B3837")
        m_steel = make_mat("AwlSteel", "#D8E2F0")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        pose = BipedPose(
            l_thigh_fwd=8.0, l_knee_bend=10.0, r_thigh_fwd=-8.0, r_knee_bend=10.0,
            l_sh_fwd=32.0, l_sh_out=18.0, l_el_bend=44.0,
            r_sh_fwd=48.0, r_sh_out=16.0, r_el_bend=38.0,
        )
        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.45, shoulder_x=0.34, shoulder_z_off=0.36)
        add_articulated_legs(rig, pose, m_boots, m_pants, m_pants, stance_x=0.20, thigh_len=0.35, shin_len=0.36, m_boot_cuff=m_vest)

        pelvis, torso, head = rig["pelvis"], rig["torso"], rig["head"]
        add_box("Pelvis", (0.46, 0.30, 0.22), (0.0, 0.0, 0.04), mat=m_pants, bevel=0.025, parent=pelvis)
        add_box("Belt", (0.50, 0.34, 0.09), (0.0, 0.0, 0.14), mat=m_satchel, bevel=0.015, parent=pelvis)
        add_box("SatchelBag", (0.22, 0.18, 0.26), (-0.28, -0.06, 0.02), rot_deg=(0.0, 12.0, 0.0), mat=m_satchel, bevel=0.025, parent=pelvis)

        add_box("HideVest", (0.50, 0.34, 0.44), (0.0, 0.0, 0.22), mat=m_vest, bevel=0.04, parent=torso)
        add_box("SatchelStrap", (0.54, 0.38, 0.08), (0.0, -0.01, 0.24), rot_deg=(0.0, 34.0, 0.0), mat=m_satchel, bevel=0.015, parent=torso)
        add_box("CloakMantle", (0.62, 0.38, 0.16), (0.0, 0.04, 0.38), mat=m_hood, bevel=0.03, parent=torso)
        add_box("CloakBack", (0.56, 0.10, 0.96), (0.0, 0.26, -0.06), rot_deg=(-16.0, 0.0, 0.0), mat=m_hood, bevel=0.03, parent=torso)

        add_sphere("Head", 0.15, (0.0, -0.06, 0.06), mat=m_skin, parent=head)
        add_sphere("MossHood", 0.185, (0.0, 0.03, 0.09), scale=(1.06, 1.14, 1.06), mat=m_hood, parent=head)
        add_cone("HoodPeak", 0.15, 0.02, 0.30, (0.0, 0.20, 0.17), rot_deg=(-58.0, 0.0, 0.0), verts=12, mat=m_hood, parent=head)

        # Left hand (-X): Coiled leather strap
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, 0.0, pose.l_el_bend, m_hood, m_vest, m_skin)
        coil_pts = [(0.13 * math.cos(math.radians(a)), -0.04, 0.13 * math.sin(math.radians(a)), 0.032) for a in range(0, 361, 30)]
        add_tube_curve("LeatherCoil", coil_pts, mat=m_satchel, parent=hand_l)

        # Right hand (+X): Cutting awl
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, 0.0, pose.r_el_bend, m_hood, m_vest, m_skin)
        awl = make_empty("AwlRoot", (0.02, -0.04, 0.0), (54.0, 12.0, 0.0), parent=hand_r)
        add_cyl("AwlHandle", 0.036, 0.18, (0.0, 0.0, 0.0), verts=10, mat=m_satchel, parent=awl)
        add_cone("AwlBlade", 0.026, 0.004, 0.24, (0.0, 0.0, 0.20), verts=8, mat=m_steel, parent=awl)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_npc_apothecary(stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_teal = make_mat("DeepTeal", "#1D5E63")
        m_plum = make_mat("PlumRobe", "#5A2D5E")
        m_gold = make_mat("GoldTrim", "#ECA938")
        m_potion = make_mat("EmeraldPotion", "#54C9C0")
        m_glass = make_mat("FlaskGlass", "#E0F9FF")
        m_herb = make_mat("HerbGreen", "#52A653")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        pose = BipedPose(
            l_thigh_fwd=4.0, l_knee_bend=6.0, r_thigh_fwd=-4.0, r_knee_bend=6.0,
            l_sh_fwd=36.0, l_sh_out=16.0, l_el_bend=44.0,
            r_sh_fwd=70.0, r_sh_out=18.0, r_el_bend=38.0,
        )
        rig = create_biped_rig(pose, base_hip_z=0.84, torso_h=0.45, shoulder_x=0.33, shoulder_z_off=0.36)
        add_articulated_legs(rig, pose, m_plum, m_teal, m_teal, stance_x=0.18, thigh_len=0.35, shin_len=0.36, boot_size=(0.18, 0.28, 0.14))

        pelvis, torso, head = rig["pelvis"], rig["torso"], rig["head"]
        add_cone("ScholarSkirt", 0.42, 0.25, 0.56, (0.0, 0.0, -0.20), verts=18, mat=m_teal, parent=pelvis)
        add_box("PlumPanel", (0.20, 0.42, 0.54), (0.0, -0.04, -0.20), mat=m_plum, bevel=0.02, parent=pelvis)
        add_box("GoldBelt", (0.48, 0.34, 0.09), (0.0, 0.0, 0.10), mat=m_gold, bevel=0.015, parent=pelvis)

        add_box("ScholarTorso", (0.48, 0.34, 0.44), (0.0, 0.0, 0.22), mat=m_teal, bevel=0.04, parent=torso)
        add_box("PlumShoulders", (0.60, 0.38, 0.16), (0.0, 0.02, 0.38), mat=m_plum, bevel=0.03, parent=torso)
        add_box("GoldCollar", (0.38, 0.35, 0.08), (0.0, -0.02, 0.44), mat=m_gold, bevel=0.015, parent=torso)

        add_sphere("Head", 0.15, (0.0, -0.05, 0.06), mat=m_skin, parent=head)
        add_cyl("ScholarCap", 0.17, 0.16, (0.0, 0.0, 0.16), verts=16, mat=m_plum, parent=head)
        add_cyl("CapGoldBand", 0.178, 0.045, (0.0, 0.0, 0.11), verts=16, mat=m_gold, parent=head)
        # Brass-toned spectacles
        for sx in (-0.06, 0.06):
            add_cyl(f"Spectacle_{sx}", 0.048, 0.025, (sx, -0.19, 0.08), rot_deg=(90.0, 0.0, 0.0), verts=12, mat=m_gold, parent=head)
        add_box("SpectacleBridge", (0.05, 0.02, 0.018), (0.0, -0.19, 0.08), mat=m_gold, bevel=0.004, parent=head)

        # Back hand (-X): Bundled herbs
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, 0.0, pose.l_el_bend, m_plum, m_teal, m_skin)
        herbs = make_empty("HerbBundle", (-0.02, -0.04, 0.0), (38.0, -14.0, 0.0), parent=hand_l)
        for hx, hy in ((-0.03, 0.0), (0.03, 0.0), (0.0, -0.03)):
            add_cone(f"HerbSprig_{hx}_{hy}", 0.065, 0.015, 0.32, (hx, hy, 0.08), verts=8, mat=m_herb, parent=herbs)

        # Front hand (+X): Glowing emerald-cyan potion flask held up
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, 0.0, pose.r_el_bend, m_plum, m_teal, m_skin)
        flask = make_empty("PotionFlask", (0.02, -0.06, 0.04), (pose.r_sh_fwd + pose.r_el_bend - 10.0, 8.0, 0.0), parent=hand_r)
        add_sphere("FlaskBulb", 0.145, (0.0, 0.0, 0.10), mat=m_potion, parent=flask)
        add_cyl("FlaskNeck", 0.048, 0.12, (0.0, 0.0, 0.25), verts=12, mat=m_glass, parent=flask)
        add_cyl("FlaskCork", 0.055, 0.05, (0.0, 0.0, 0.32), verts=10, mat=m_gold, parent=flask)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    def build_npc_priestess(stem: str, spec: Dict[str, object]) -> None:
        clear_scene()
        m_ivory = make_mat("TempleIvory", "#F0ECE4")
        m_gold = make_mat("SunlitGold", "#ECA938")
        m_lantern = make_mat("LanternGlow", "#FFF3B0")
        m_cerulean = make_mat("CeruleanStole", "#3452A1")
        m_scroll = make_mat("ScrollParchment", "#E5D8BD")
        m_skin = make_mat("WarmSkin", "#EDB89A")
        m_hair = make_mat("AuburnHair", "#825132")

        pose = BipedPose(
            l_thigh_fwd=4.0, l_knee_bend=6.0, r_thigh_fwd=-4.0, r_knee_bend=6.0,
            l_sh_fwd=32.0, l_sh_out=16.0, l_el_bend=44.0,
            r_sh_fwd=84.0, r_sh_out=24.0, r_sh_yaw=10.0, r_el_bend=18.0,
        )
        rig = create_biped_rig(pose, base_hip_z=0.86, torso_h=0.46, shoulder_x=0.33, shoulder_z_off=0.36)
        add_articulated_legs(rig, pose, m_gold, m_ivory, m_ivory, stance_x=0.18, thigh_len=0.35, shin_len=0.37, boot_size=(0.18, 0.28, 0.14))

        pelvis, torso, head = rig["pelvis"], rig["torso"], rig["head"]
        add_cone("TempleSkirt", 0.43, 0.25, 0.58, (0.0, 0.0, -0.20), verts=18, mat=m_ivory, parent=pelvis)
        add_cone("SkirtGoldHem", 0.445, 0.41, 0.09, (0.0, 0.0, -0.46), verts=18, mat=m_gold, parent=pelvis)
        add_box("CeruleanStoleLower", (0.20, 0.43, 0.56), (0.0, -0.04, -0.20), mat=m_cerulean, bevel=0.02, parent=pelvis)
        add_box("GoldSash", (0.48, 0.34, 0.09), (0.0, 0.0, 0.10), mat=m_gold, bevel=0.015, parent=pelvis)

        add_box("TempleTorso", (0.46, 0.32, 0.44), (0.0, 0.0, 0.22), mat=m_ivory, bevel=0.04, parent=torso)
        add_box("CeruleanShawl", (0.62, 0.38, 0.18), (0.0, 0.02, 0.38), mat=m_cerulean, bevel=0.03, parent=torso)
        add_box("GoldTrimPanel", (0.18, 0.35, 0.38), (0.0, -0.01, 0.22), mat=m_gold, bevel=0.015, parent=torso)

        add_sphere("Head", 0.15, (0.0, -0.05, 0.06), mat=m_skin, parent=head)
        add_sphere("HairBack", 0.165, (0.0, 0.03, 0.07), scale=(1.05, 1.12, 1.05), mat=m_hair, parent=head)
        # Flowing ivory-and-gold temple headdress veil framing head and shoulders
        add_sphere("VeilCrown", 0.185, (0.0, 0.02, 0.11), scale=(1.12, 1.16, 0.96), mat=m_ivory, parent=head)
        add_cyl("VeilGoldTrim", 0.188, 0.05, (0.0, -0.01, 0.14), rot_deg=(8.0, 0.0, 0.0), verts=16, mat=m_gold, parent=head)
        add_box("VeilDrapeBack", (0.44, 0.12, 0.58), (0.0, 0.16, -0.10), rot_deg=(-14.0, 0.0, 0.0), mat=m_ivory, bevel=0.03, parent=head)
        add_box("VeilDrapeGold", (0.46, 0.13, 0.07), (0.0, 0.22, -0.36), rot_deg=(-14.0, 0.0, 0.0), mat=m_gold, bevel=0.015, parent=head)
        for side, sx in (("L", -0.17), ("R", 0.17)):
            add_box(f"VeilCheek_{side}", (0.08, 0.14, 0.34), (sx, -0.01, -0.04), rot_deg=(0.0, -8.0 if sx < 0 else 8.0, 0.0), mat=m_ivory, bevel=0.02, parent=head)
        add_sphere("CircletGem", 0.045, (0.0, -0.19, 0.15), mat=m_cerulean, parent=head)

        # Back hand (-X): Prayer scroll
        hand_l = add_articulated_arm(rig, "L", pose.l_sh_fwd, pose.l_sh_out, 0.0, pose.l_el_bend, m_cerulean, m_ivory, m_skin)
        scroll = make_empty("PrayerScroll", (-0.02, -0.04, 0.0), (24.0, 12.0, 40.0), parent=hand_l)
        add_cyl("ScrollBody", 0.048, 0.34, (0.0, 0.0, 0.0), verts=12, mat=m_scroll, parent=scroll)
        add_cyl("ScrollRibbon", 0.054, 0.05, (0.0, 0.0, 0.0), verts=12, mat=m_cerulean, parent=scroll)

        # Front hand (+X): Raised ornate golden sun-lantern / chalice held high and clear
        hand_r = add_articulated_arm(rig, "R", pose.r_sh_fwd, pose.r_sh_out, pose.r_sh_yaw, pose.r_el_bend, m_cerulean, m_ivory, m_skin)
        chalice = make_empty("SunChalice", (0.03, -0.05, 0.02), (pose.r_sh_fwd + pose.r_el_bend - 6.0, 10.0, 0.0), parent=hand_r)
        add_cyl("ChaliceStem", 0.034, 0.24, (0.0, 0.0, 0.08), verts=12, mat=m_gold, parent=chalice)
        add_cone("ChaliceCup", 0.045, 0.135, 0.16, (0.0, 0.0, 0.24), verts=14, mat=m_gold, parent=chalice)
        add_sphere("SunLanternOrb", 0.125, (0.0, 0.0, 0.38), mat=m_lantern, parent=chalice)
        sun_halo_pts = [(0.19 * math.cos(math.radians(a)), 0.0, 0.38 + 0.19 * math.sin(math.radians(a)), 0.024) for a in range(-45, 226, 25)]
        add_tube_curve("ChaliceSunHalo", sun_halo_pts, mat=m_gold, parent=chalice)

        gxy = setup_render_and_camera(float(spec["ortho_scale"]), tuple(spec["target"]), float(spec["yaw_deg"]))
        render_asset_passes(stem, gxy, float(spec["ortho_scale"]), float(spec["yaw_deg"]))

    biped_builders = {
        "hero_knight": (build_hero_knight, 0.0, 1.0),
        "hero_cleric": (build_hero_cleric, 0.0, 1.24),
        "hero_ranger": (build_hero_ranger, 0.12, 1.0),
        "hero_mage": (build_hero_mage, 0.0, 1.24),
        "hero_paladin": (build_hero_paladin, 0.0, 1.0),
        "hero_rogue": (build_hero_rogue, 0.65, 1.05),
        "monster_goblin_raider": (build_monster_goblin_raider, 0.50, 0.95),
        "monster_goblin_archer": (build_monster_goblin_archer, 0.45, 0.95),
        "monster_orc_warrior": (build_monster_orc_warrior, 0.15, 0.95),
        "monster_skeleton_warrior": (build_monster_skeleton_warrior, 0.08, 1.0),
        "monster_bandit_cutthroat": (build_monster_bandit_cutthroat, 0.20, 1.0),
    }

    npc_builders = {
        "npc_blacksmith": build_npc_blacksmith,
        "npc_tanner": build_npc_tanner,
        "npc_apothecary": build_npc_apothecary,
        "npc_priestess": build_npc_priestess,
    }

    want_units = set(selected_units) if selected_units else None
    want_anims = set(only_anims) if only_anims else {"idle_ref", "walk", "attack"}

    for spec in UNIT_SPECS:
        uid = str(spec["id"])
        if want_units is not None and uid not in want_units:
            continue

        if uid == "monster_giant_frog":
            if "idle_ref" in want_anims:
                build_monster_giant_frog("idle", 0, f"{uid}_idle_ref", spec)
            if "walk" in want_anims:
                for f in range(4):
                    build_monster_giant_frog("walk", f, f"{uid}_walk_f{f}", spec)
            if "attack" in want_anims:
                for f in range(4):
                    build_monster_giant_frog("attack", f, f"{uid}_attack_f{f}", spec)
        elif uid == "monster_giant_rat":
            if "idle_ref" in want_anims:
                build_monster_giant_rat("idle", 0, f"{uid}_idle_ref", spec)
            if "walk" in want_anims:
                for f in range(4):
                    build_monster_giant_rat("walk", f, f"{uid}_walk_f{f}", spec)
            if "attack" in want_anims:
                for f in range(4):
                    build_monster_giant_rat("attack", f, f"{uid}_attack_f{f}", spec)
        else:
            fn, crouch, stride_scale = biped_builders[uid]
            facing_east = spec["faction"] == "hero"
            attack_style = str(spec["attack_style"])
            if "idle_ref" in want_anims:
                idle_pose = BipedPose(
                    l_thigh_fwd=8.0 * crouch,
                    l_knee_bend=16.0 * crouch,
                    r_thigh_fwd=-6.0 * crouch,
                    r_knee_bend=16.0 * crouch,
                    torso_pitch=14.0 * crouch,
                    l_sh_fwd=30.0 if not facing_east else 24.0,
                    r_sh_fwd=30.0 if facing_east else 24.0,
                    l_el_bend=34.0,
                    r_el_bend=34.0,
                    prop_fx="bow_draw" if attack_style == "bow" else "",
                )
                fn(idle_pose, f"{uid}_idle_ref", spec)
            if "walk" in want_anims:
                for f in range(4):
                    wp = _biped_walk_pose(f, crouch=crouch, stride_scale=stride_scale)
                    if attack_style == "bow":
                        wp.prop_fx = "bow_draw"
                    fn(wp, f"{uid}_walk_f{f}", spec)
            if "attack" in want_anims:
                for f in range(4):
                    ap = _attack_pose(attack_style, facing_east=facing_east, frame=f, crouch=crouch)
                    fn(ap, f"{uid}_attack_f{f}", spec)

    if "npc" in want_anims or only_anims is None:
        for spec in NPC_SPECS:
            nid = str(spec["id"])
            if want_units is not None and nid not in want_units:
                continue
            npc_builders[nid](nid, spec)

    (raw_dir / "render_projections.json").write_text(json.dumps(projections, indent=2), encoding="utf-8")


# ---------------------------------------------------------------------------
# Outer Python post-processing: _depth.png, _canny.png, _chroma_init.png,
# 2x2 grid composites, idle f0 measurement, and preview sheets
# ---------------------------------------------------------------------------

def _convert_raw_to_guides(stem: str, raw_dir: Path, out_dir: Path) -> Dict[str, object]:
    import cv2
    import numpy as np
    from PIL import Image

    rgba = np.array(Image.open(raw_dir / f"{stem}_rgba.png").convert("RGBA"))
    z_raw = np.load(raw_dir / f"{stem}_depth.npy")

    alpha = (rgba[..., 3] >= 128) & (z_raw < 1e5)
    if not np.any(alpha):
        raise RuntimeError(f"Blender rendered empty foreground for {stem}")

    # 1. Normalized ControlNet Depth Map
    z_fg = z_raw[alpha]
    z_min = float(np.percentile(z_fg, 0.5))
    z_max = float(np.percentile(z_fg, 99.5))
    span = max(1e-4, z_max - z_min)
    depth_norm = np.clip((z_max - z_raw) / span, 0.0, 1.0)
    depth_u8 = np.zeros_like(z_raw, dtype=np.uint8)
    depth_u8[alpha] = np.round(64.0 + depth_norm[alpha] * 191.0).astype(np.uint8)

    depth_path = out_dir / f"{stem}_depth.png"
    Image.fromarray(depth_u8, mode="L").save(depth_path, format="PNG")

    # 2. Crisp ControlNet Canny / Edge Map
    sil_u8 = np.where(alpha, 255, 0).astype(np.uint8)
    contours, _ = cv2.findContours(sil_u8, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    edge_sil = np.zeros_like(sil_u8)
    cv2.drawContours(edge_sil, contours, -1, 255, thickness=2)

    edge_depth = cv2.Canny(depth_u8, 28, 85)
    rgb_wb = rgba[..., :3].copy()
    rgb_wb[~alpha] = 0
    edge_rgb = cv2.Canny(rgb_wb, 35, 100)
    gray_wb = cv2.cvtColor(rgb_wb, cv2.COLOR_RGB2GRAY)
    edge_gray = cv2.Canny(gray_wb, 30, 90)

    combined = np.maximum.reduce([edge_sil, edge_depth, edge_rgb, edge_gray])
    fg_dil = cv2.dilate(sil_u8, np.ones((3, 3), dtype=np.uint8), iterations=1) > 0
    combined[~fg_dil] = 0

    canny_path = out_dir / f"{stem}_canny.png"
    Image.fromarray(combined, mode="L").save(canny_path, format="PNG")

    # 3. Solid #FF00FF Chroma Init Guide
    mask_f = alpha.astype(np.float32)
    rgb_f = rgba[..., :3].astype(np.float32)
    num = cv2.GaussianBlur(rgb_f * mask_f[..., None], (0, 0), 1.1)
    den = cv2.GaussianBlur(mask_f, (0, 0), 1.1)
    smooth_fg = np.clip(num / np.maximum(den[..., None], 1e-5), 0.0, 255.0).astype(np.uint8)

    chroma_rgb = np.full((rgba.shape[0], rgba.shape[1], 3), MAGENTA_BG, dtype=np.uint8)
    chroma_rgb[alpha] = smooth_fg[alpha]

    chroma_path = out_dir / f"{stem}_chroma_init.png"
    Image.fromarray(chroma_rgb, mode="RGB").save(chroma_path, format="PNG")

    ys, xs = np.where(alpha)
    bbox = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
    clipped_edges = []
    if bbox[0] <= 2:
        clipped_edges.append("left")
    if bbox[1] <= 2:
        clipped_edges.append("top")
    if bbox[2] >= RENDER_RES - 3:
        clipped_edges.append("right")
    if bbox[3] >= RENDER_RES - 3:
        clipped_edges.append("bottom")

    return {
        "depth": str(depth_path),
        "canny": str(canny_path),
        "chroma_init": str(chroma_path),
        "fg_bbox": bbox,
        "fg_width_px": int(bbox[2] - bbox[0] + 1),
        "fg_height_px": int(bbox[3] - bbox[1] + 1),
        "clipped_edges": clipped_edges,
    }


def _build_2x2_grid(uid: str, anim: str, out_dir: Path) -> Dict[str, str]:
    """Assemble 2x2 1024x1024 composite [TL=f0, TR=f1, BL=f2, BR=f3] for depth, canny, chroma_init."""
    import cv2
    import numpy as np
    from PIL import Image

    half = RENDER_RES // 2
    offsets = ((0, 0), (half, 0), (0, half), (half, half))
    grid_paths: Dict[str, str] = {}

    for suffix, mode, interp in (
        ("depth", "L", cv2.INTER_AREA),
        ("canny", "L", cv2.INTER_AREA),
        ("chroma_init", "RGB", cv2.INTER_NEAREST),
    ):
        if mode == "L":
            canvas = np.zeros((RENDER_RES, RENDER_RES), dtype=np.uint8)
        else:
            canvas = np.full((RENDER_RES, RENDER_RES, 3), MAGENTA_BG, dtype=np.uint8)

        for idx, (ox, oy) in enumerate(offsets):
            frame_p = out_dir / f"{uid}_{anim}_f{idx}_{suffix}.png"
            arr = np.array(Image.open(frame_p).convert(mode))
            resized = cv2.resize(arr, (half, half), interpolation=interp)
            if suffix == "canny":
                # Keep crisp high-contrast lines after downscale
                resized = np.where(resized >= 48, 255, 0).astype(np.uint8)
            canvas[oy : oy + half, ox : ox + half] = resized

        out_p = out_dir / f"{uid}_{anim}_grid_{suffix}.png"
        Image.fromarray(canvas, mode=mode).save(out_p, format="PNG")
        grid_paths[f"grid_{suffix}"] = str(out_p)

    return grid_paths


def _measure_idle_f0_from_sheet(uid: str, units_dir: Path = UNITY_UNITS_DIR) -> Optional[Dict[str, object]]:
    """Read f0 bbox directly from existing 512x128 RealmzUnity unit sheet (read-only)."""
    import numpy as np
    from PIL import Image

    sheet_path = units_dir / f"{uid}_sheet.png"
    if not sheet_path.exists():
        return None
    arr = np.array(Image.open(sheet_path).convert("RGBA"))
    frame_w = min(128, arr.shape[0])
    f0 = arr[:, :frame_w]
    ys, xs = np.where(f0[..., 3] >= 128)
    if len(ys) == 0:
        return None
    bbox = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
    return {
        "sheet_path": str(sheet_path),
        "idle_f0_bbox": bbox,
        "idle_f0_w_px": int(bbox[2] - bbox[0] + 1),
        "idle_f0_h_px": int(bbox[3] - bbox[1] + 1),
    }


def _build_anim_preview(
    out_dir: Path,
    specs: Sequence[Dict[str, object]],
    anim: str,
    preview_name: str,
    title: str,
) -> Path:
    from PIL import Image, ImageDraw, ImageFont

    rows = len(specs)
    cols = 4
    thumb = 240
    pad = 12
    lbl_w = 230
    header_h = 50
    row_h = thumb + pad

    sheet_w = pad + lbl_w + cols * (thumb + pad)
    sheet_h = header_h + rows * row_h + pad

    sheet = Image.new("RGB", (sheet_w, sheet_h), (20, 24, 36))
    draw = ImageDraw.Draw(sheet)

    try:
        font_title = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 20)
        font_lbl = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 14)
        font_sm = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 11)
    except Exception:
        font_title = font_lbl = font_sm = ImageFont.load_default()

    draw.text((pad, 14), title, fill=(244, 198, 92), font=font_title)

    for r, spec in enumerate(specs):
        uid = str(spec["id"])
        y0 = header_h + r * row_h
        draw.rectangle([pad, y0, pad + lbl_w - 8, y0 + thumb - 1], fill=(30, 36, 54))
        draw.text((pad + 10, y0 + 18), uid, fill=(242, 244, 248), font=font_lbl)
        draw.text((pad + 10, y0 + 42), str(spec["label"]), fill=(140, 210, 250), font=font_sm)
        draw.text(
            (pad + 10, y0 + 62),
            f"yaw={spec['yaw_deg']:+.0f} | scale={spec['ortho_scale']}",
            fill=(186, 198, 218),
            font=font_sm,
        )
        draw.text((pad + 10, y0 + 82), f"style={spec['attack_style']}", fill=(186, 198, 218), font=font_sm)

        for c in range(cols):
            x0 = pad + lbl_w + c * (thumb + pad)
            fp = out_dir / f"{uid}_{anim}_f{c}_chroma_init.png"
            im = Image.open(fp).convert("RGB").resize((thumb, thumb), Image.Resampling.LANCZOS)
            sheet.paste(im, (x0, y0))
            draw.rectangle([x0, y0, x0 + thumb - 1, y0 + thumb - 1], outline=(78, 92, 128), width=2)
            draw.text((x0 + 6, y0 + 6), f"f{c}", fill=(255, 255, 255), font=font_lbl)

    preview_path = out_dir / preview_name
    sheet.save(preview_path, format="PNG")
    return preview_path


def _build_npc_preview(out_dir: Path, specs: Sequence[Dict[str, object]]) -> Path:
    from PIL import Image, ImageDraw, ImageFont

    cols = 4
    thumb = 320
    pad = 18
    label_h = 52
    header_h = 54
    sheet_w = pad + cols * (thumb + pad)
    sheet_h = header_h + thumb + label_h + pad

    sheet = Image.new("RGB", (sheet_w, sheet_h), (20, 24, 36))
    draw = ImageDraw.Draw(sheet)

    try:
        font_title = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 22)
        font_lbl = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 14)
        font_sm = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 11)
    except Exception:
        font_title = font_lbl = font_sm = ImageFont.load_default()

    draw.text(
        (pad, 14),
        "BYWATER SHOPKEEPER NPCS (4 Units | 1024x1024 | 26 deg Ortho | yaw=-28 | #FF00FF Chroma)",
        fill=(244, 198, 92),
        font=font_title,
    )

    for idx, spec in enumerate(specs):
        x0 = pad + idx * (thumb + pad)
        y0 = header_h
        im = Image.open(out_dir / f"{spec['id']}_chroma_init.png").convert("RGB").resize((thumb, thumb), Image.Resampling.LANCZOS)
        sheet.paste(im, (x0, y0))
        draw.rectangle([x0, y0, x0 + thumb - 1, y0 + thumb - 1], outline=(78, 92, 128), width=2)
        draw.rectangle([x0, y0 + thumb, x0 + thumb - 1, y0 + thumb + label_h - 1], fill=(30, 36, 54))
        draw.text((x0 + 8, y0 + thumb + 6), str(spec["id"]), fill=(242, 244, 248), font=font_lbl)
        draw.text(
            (x0 + 8, y0 + thumb + 26),
            f"{spec['label']} | s={spec['ortho_scale']} | h={spec['target_height_px']}px",
            fill=(186, 198, 218),
            font=font_sm,
        )

    preview_path = out_dir / "guides_npc_preview.png"
    sheet.save(preview_path, format="PNG")
    return preview_path


def generate_walk_attack_shopkeeper_guides(
    blender_exe: Path = DEFAULT_BLENDER_EXE,
    out_dir: Path = DEFAULT_OUT_DIR,
    only_units: Sequence[str] | None = None,
    only_anims: Sequence[str] | None = None,
) -> dict:
    if not blender_exe.exists():
        raise FileNotFoundError(f"Blender executable not found: {blender_exe}")

    t0 = time.perf_counter()
    out_dir.mkdir(parents=True, exist_ok=True)
    want_units = set(only_units) if only_units else None
    want_anims = set(only_anims) if only_anims else {"idle_ref", "walk", "attack", "npc"}

    unit_results: Dict[str, Dict[str, object]] = {}
    npc_results: Dict[str, Dict[str, object]] = {}
    warnings: List[str] = []

    with tempfile.TemporaryDirectory(prefix="realmz_walk_attack_guides_") as td:
        raw_dir = Path(td)
        cmd = [
            str(blender_exe),
            "-b",
            "--python-exit-code",
            "1",
            "--python",
            str(Path(__file__).resolve()),
            "--",
            "--blender-inner",
            "--raw-dir",
            str(raw_dir),
        ]
        if only_units:
            cmd.extend(["--only", ",".join(only_units)])
        if only_anims:
            cmd.extend(["--anims", ",".join(only_anims)])

        t_blend0 = time.perf_counter()
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=600,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        t_blend = time.perf_counter() - t_blend0
        if proc.returncode != 0:
            raise RuntimeError(f"Blender failed (rc={proc.returncode}):\n{proc.stdout}\n{proc.stderr}")

        projections = json.loads((raw_dir / "render_projections.json").read_text(encoding="utf-8"))

        t_post0 = time.perf_counter()
        for spec in UNIT_SPECS:
            uid = str(spec["id"])
            if want_units is not None and uid not in want_units:
                continue

            entry: Dict[str, object] = {
                **spec,
                "camera_pitch_deg": CAMERA_PITCH_DEG,
                "render_res": RENDER_RES,
                "px_per_meter": 64,
            }

            f0_info = _measure_idle_f0_from_sheet(uid)
            if f0_info:
                entry.update(f0_info)
            else:
                warnings.append(f"Missing or empty idle f0 sheet for {uid}")

            if "idle_ref" in want_anims:
                stem = f"{uid}_idle_ref"
                g = _convert_raw_to_guides(stem, raw_dir, out_dir)
                proj = projections[stem]
                if g["clipped_edges"]:
                    warnings.append(f"{stem} clipped edges: {g['clipped_edges']}")
                idle_h = int(g["fg_height_px"])
                entry["idle_ref"] = {**g, **proj}
                entry["idle_fg_height_px"] = idle_h
                entry["idle_ground_x_px"] = proj["ground_x_px"]
                entry["idle_ground_y_px"] = proj["ground_y_px"]
                if f0_info and idle_h > 0:
                    entry["px_per_render_px"] = round(float(f0_info["idle_f0_h_px"]) / float(idle_h), 6)

            for anim in ("walk", "attack"):
                if anim not in want_anims:
                    continue
                frames: List[Dict[str, object]] = []
                for f in range(4):
                    stem = f"{uid}_{anim}_f{f}"
                    g = _convert_raw_to_guides(stem, raw_dir, out_dir)
                    proj = projections[stem]
                    if g["clipped_edges"]:
                        warnings.append(f"{stem} clipped edges: {g['clipped_edges']}")
                    frames.append({"frame": f, **g, **proj})
                grid = _build_2x2_grid(uid, anim, out_dir)
                entry[anim] = {
                    "frames": frames,
                    "grid": grid,
                    "ground_x_px": frames[0]["ground_x_px"],
                    "ground_y_px": frames[0]["ground_y_px"],
                }

            unit_results[uid] = entry

        if "npc" in want_anims:
            for spec in NPC_SPECS:
                nid = str(spec["id"])
                if want_units is not None and nid not in want_units:
                    continue
                g = _convert_raw_to_guides(nid, raw_dir, out_dir)
                proj = projections[nid]
                if g["clipped_edges"]:
                    warnings.append(f"{nid} clipped edges: {g['clipped_edges']}")
                px_per_render = round(float(spec["target_height_px"]) / float(g["fg_height_px"]), 6)
                npc_results[nid] = {
                    **spec,
                    "camera_pitch_deg": CAMERA_PITCH_DEG,
                    "render_res": RENDER_RES,
                    "px_per_meter": 64,
                    "px_per_render_px": px_per_render,
                    **g,
                    **proj,
                }
        t_post = time.perf_counter() - t_post0

    previews: Dict[str, str] = {}
    meta_path = out_dir / "guides_metadata.json"
    if (only_units is not None or only_anims is not None) and meta_path.exists():
        existing = json.loads(meta_path.read_text(encoding="utf-8"))
        merged_units = dict(existing.get("units", {}))
        for uid, udata in unit_results.items():
            prev_u = dict(merged_units.get(uid, {}))
            prev_u.update(udata)
            merged_units[uid] = prev_u
        merged_npcs = dict(existing.get("npcs", {}))
        merged_npcs.update(npc_results)
        unit_results = merged_units
        npc_results = merged_npcs
        previews = dict(existing.get("previews", {}))

    if "walk" in want_anims and all((out_dir / f"{s['id']}_walk_f{c}_chroma_init.png").exists() for s in UNIT_SPECS for c in range(4)):
        previews["walk"] = str(
            _build_anim_preview(
                out_dir, UNIT_SPECS, "walk", "guides_walk_preview.png",
                "REALMZ 4-FRAME WALK GUIDES (13 Units | 1024x1024 | 26 deg Ortho | #FF00FF Chroma)",
            )
        )
    if "attack" in want_anims and all((out_dir / f"{s['id']}_attack_f{c}_chroma_init.png").exists() for s in UNIT_SPECS for c in range(4)):
        previews["attack"] = str(
            _build_anim_preview(
                out_dir, UNIT_SPECS, "attack", "guides_attack_preview.png",
                "REALMZ 4-FRAME ATTACK GUIDES (13 Units | 1024x1024 | 26 deg Ortho | #FF00FF Chroma)",
            )
        )
    if "npc" in want_anims and all((out_dir / f"{s['id']}_chroma_init.png").exists() for s in NPC_SPECS):
        previews["npc"] = str(_build_npc_preview(out_dir, NPC_SPECS))

    metadata = {
        "out_dir": str(out_dir),
        "previews": previews,
        "unit_count": len(unit_results),
        "npc_count": len(npc_results),
        "camera_pitch_deg": CAMERA_PITCH_DEG,
        "render_res": RENDER_RES,
        "px_per_meter": 64,
        "warnings": warnings,
        "timings_s": {
            "blender_render": round(t_blend, 2),
            "postprocess": round(t_post, 2),
            "total": round(time.perf_counter() - t0, 2),
        },
        "units": unit_results,
        "npcs": npc_results,
    }
    meta_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    metadata["metadata_path"] = str(meta_path)
    return metadata


def main() -> None:
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = argv[1:]

    parser = argparse.ArgumentParser(description="Realmz Walk, Attack & Shopkeeper Blender Guide Builder")
    parser.add_argument("--blender-inner", action="store_true", help="Internal flag when running inside Blender")
    parser.add_argument("--raw-dir", type=Path, default=None)
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR)
    parser.add_argument("--blender-exe", type=Path, default=DEFAULT_BLENDER_EXE)
    parser.add_argument("--only", default=None, help="Comma-separated unit/NPC IDs to render")
    parser.add_argument("--anims", default=None, help="Comma-separated anim sets: idle_ref,walk,attack,npc")
    args = parser.parse_args(argv)

    only_list = [u.strip() for u in args.only.split(",") if u.strip()] if args.only else None
    anims_list = [a.strip() for a in args.anims.split(",") if a.strip()] if args.anims else None

    if args.blender_inner:
        if args.raw_dir is None:
            raise ValueError("--raw-dir is required with --blender-inner")
        args.raw_dir.mkdir(parents=True, exist_ok=True)
        _blender_main(args.raw_dir, selected_units=only_list, only_anims=anims_list)
    else:
        summary = generate_walk_attack_shopkeeper_guides(
            blender_exe=args.blender_exe,
            out_dir=args.out_dir,
            only_units=only_list,
            only_anims=anims_list,
        )
        print(json.dumps({k: v for k, v in summary.items() if k not in ("units", "npcs")}, indent=2))
        print(f"Wrote metadata: {summary['metadata_path']}")


if __name__ == "__main__":
    main()
