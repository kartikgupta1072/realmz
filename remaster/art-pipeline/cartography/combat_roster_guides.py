"""
Headless Blender 4.5.9 Orthographic (26 deg pitch) Combat Roster Guide Generator.

Produces 1024x1024 ControlNet Depth (*_depth.png), Canny (*_canny.png), and
solid #FF00FF magenta-background material-colored init guides (*_chroma_init.png)
for all 13 Realmz combat units into:
  c:/Users/karti/LifeTwin/scratch/realmz_combat_art_pack/guides/
plus a 4-column contact sheet (guides_preview.png) and metadata JSON
(guides_metadata.json) recording target_height_px (64 px/m) and camera specs.

Facing convention:
  - Characters are sculpted facing -Y in Blender space.
  - All 6 Heroes use yaw_deg = -32.0 (facing East/Right, 3/4 South-East).
  - All 7 Monsters use yaw_deg = +32.0 (facing West/Left, 3/4 South-West).

Usage (via asset-foundry venv Python):
  C:/realmz/tools/asset-foundry/.venv/Scripts/python.exe combat_roster_guides.py
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
from pathlib import Path
from typing import Dict, List, Sequence, Tuple

CREATE_NO_WINDOW = 0x08000000
DEFAULT_BLENDER_EXE = Path(r"C:\realmz\tools\blender\blender-4.5.9-windows-x64\blender.exe")
DEFAULT_OUT_DIR = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_combat_art_pack\guides")
RENDER_RES = 1024
CAMERA_PITCH_DEG = 26.0
MAGENTA_BG = (255, 0, 255)

UNIT_SPECS: List[Dict[str, object]] = [
    # 6 Heroes (East/Right, yaw_deg = -32.0)
    {
        "id": "hero_knight",
        "label": "Knight (Aldric)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 96,
    },
    {
        "id": "hero_cleric",
        "label": "Cleric (Liora)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 94,
    },
    {
        "id": "hero_ranger",
        "label": "Ranger (Kaelen)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 94,
    },
    {
        "id": "hero_mage",
        "label": "Mage (Theron)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 96,
    },
    {
        "id": "hero_paladin",
        "label": "Paladin (Seraphina)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 98,
    },
    {
        "id": "hero_rogue",
        "label": "Rogue (Vex)",
        "faction": "hero",
        "yaw_deg": -32.0,
        "ortho_scale": 2.6,
        "target_height_px": 88,
    },
    # 7 Monsters (West/Left, yaw_deg = +32.0)
    {
        "id": "monster_giant_frog",
        "label": "Giant Frog",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.2,
        "target_height_px": 84,
    },
    {
        "id": "monster_goblin_raider",
        "label": "Goblin Raider",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.2,
        "target_height_px": 76,
    },
    {
        "id": "monster_goblin_archer",
        "label": "Goblin Archer",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.2,
        "target_height_px": 76,
    },
    {
        "id": "monster_orc_warrior",
        "label": "Orc Warrior",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.7,
        "target_height_px": 112,
    },
    {
        "id": "monster_giant_rat",
        "label": "Giant Rat",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.2,
        "target_height_px": 64,
    },
    {
        "id": "monster_skeleton_warrior",
        "label": "Skeleton Warrior",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.6,
        "target_height_px": 92,
    },
    {
        "id": "monster_bandit_cutthroat",
        "label": "Bandit Cutthroat",
        "faction": "monster",
        "yaw_deg": 32.0,
        "ortho_scale": 2.6,
        "target_height_px": 94,
    },
]


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


def _blender_main(raw_dir: Path, selected_units: Sequence[str] | None = None) -> None:
    import bmesh
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector

    def clear_scene() -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def make_mat(name: str, hex_color: str) -> "bpy.types.Material":
        mat = bpy.data.materials.new(name=name)
        r, g, b = _hex_to_linear(hex_color)
        mat.diffuse_color = (r, g, b, 1.0)
        return mat

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
        verts: int = 18,
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
        verts: int = 18,
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
        segs: int = 20,
        rings: int = 14,
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
        """Build a swept tapered tube along a 3D polyline (x, y, z, radius)."""
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
    ) -> "bpy.types.Object":
        root = bpy.data.objects.new(f"{prefix}_Root", None)
        bpy.context.collection.objects.link(root)
        root.location = loc
        root.rotation_euler = tuple(math.radians(a) for a in rot_deg)

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
    ) -> "bpy.types.Object":
        root = bpy.data.objects.new(f"{prefix}_Root", None)
        bpy.context.collection.objects.link(root)
        root.location = loc
        root.rotation_euler = tuple(math.radians(a) for a in rot_deg)

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
    ) -> "bpy.types.Object":
        root = bpy.data.objects.new(f"{prefix}_Root", None)
        bpy.context.collection.objects.link(root)
        root.location = loc
        root.rotation_euler = tuple(math.radians(a) for a in rot_deg)

        add_cyl(f"{prefix}_Rim", radius, 0.06, (0.0, 0.0, 0.0), rot_deg=(90.0, 0.0, 0.0), verts=20, mat=m_rim, parent=root)
        add_cyl(f"{prefix}_Face", radius * 0.86, 0.072, (0.0, -0.004, 0.0), rot_deg=(90.0, 0.0, 0.0), verts=20, mat=m_face, parent=root)
        add_sphere(f"{prefix}_Boss", radius * 0.28, (0.0, -0.04, 0.0), scale=(1.0, 0.65, 1.0), mat=m_rim, parent=root)
        return root

    def add_humanoid_legs(
        m_boot,
        m_shin,
        m_thigh,
        stance_x: float = 0.21,
        crouch: float = 0.0,
        shin_r: float = 0.12,
        thigh_r: float = 0.14,
        boot_size: Tuple[float, float, float] = (0.20, 0.32, 0.16),
    ) -> float:
        """Build sturdy SoC-weight humanoid legs and return hip Z height."""
        knee_y = -0.10 * crouch
        hip_y = 0.05 * crouch
        shin_h = 0.42 - 0.08 * crouch
        thigh_h = 0.38 - 0.08 * crouch
        shin_tilt = 14.0 * crouch
        thigh_tilt = -20.0 * crouch

        for side, sx in (("L", -stance_x), ("R", stance_x)):
            yaw_foot = -10.0 if side == "L" else 10.0
            add_box(
                f"Boot_{side}",
                boot_size,
                (sx, -0.05, boot_size[2] * 0.5),
                rot_deg=(0.0, 0.0, yaw_foot),
                mat=m_boot,
                bevel=0.02,
            )
            add_cyl(
                f"BootCuff_{side}",
                shin_r * 1.08,
                0.08,
                (sx, -0.01, boot_size[2] + 0.02),
                verts=14,
                mat=m_boot,
            )
            shin_z = boot_size[2] + shin_h * 0.46
            add_cyl(
                f"Shin_{side}",
                shin_r,
                shin_h,
                (sx, knee_y * 0.5, shin_z),
                rot_deg=(shin_tilt, 0.0, 0.0),
                verts=14,
                mat=m_shin,
            )
            add_sphere(
                f"Knee_{side}",
                shin_r * 1.08,
                (sx, knee_y, shin_z + shin_h * 0.44),
                mat=m_shin,
            )
            thigh_z = shin_z + shin_h * 0.44 + thigh_h * 0.44
            add_cyl(
                f"Thigh_{side}",
                thigh_r,
                thigh_h,
                (sx * 0.92, (knee_y + hip_y) * 0.5, thigh_z),
                rot_deg=(thigh_tilt, 0.0, 0.0),
                verts=14,
                mat=m_thigh,
            )

        hip_z = boot_size[2] + shin_h * 0.90 + thigh_h * 0.84
        return hip_z

    def setup_render_and_camera(
        ortho_scale: float,
        target: Tuple[float, float, float],
        yaw_deg: float,
    ) -> None:
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

    def render_asset_passes(stem: str) -> None:
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

    # -----------------------------------------------------------------------
    # 1. hero_knight (Aldric) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_knight() -> None:
        clear_scene()
        m_steel_lt = make_mat("SteelLight", "#BAC6DA")
        m_steel_mid = make_mat("SteelMid", "#7C889E")
        m_steel_dk = make_mat("SteelDark", "#475061")
        m_blade = make_mat("BladeSilver", "#D8E2F0")
        m_gold = make_mat("SunlitGold", "#ECA938")
        m_crimson = make_mat("CrimsonPlume", "#C23846")
        m_cobalt = make_mat("CobaltShield", "#3452A1")

        hip_z = add_humanoid_legs(m_steel_dk, m_steel_lt, m_steel_mid, stance_x=0.22, shin_r=0.125, thigh_r=0.145)

        add_box("Pelvis", (0.48, 0.30, 0.20), (0.0, 0.0, hip_z + 0.04), mat=m_steel_mid, bevel=0.025)
        for sx in (-0.23, 0.23):
            add_box(f"Tasset_{sx}", (0.16, 0.26, 0.22), (sx, -0.04, hip_z - 0.06), rot_deg=(0.0, 12.0 if sx < 0 else -12.0, 0.0), mat=m_steel_lt)
        add_box("Belt", (0.52, 0.34, 0.09), (0.0, 0.0, hip_z + 0.14), mat=m_gold, bevel=0.015)

        torso_z = hip_z + 0.40
        add_box("Breastplate", (0.56, 0.36, 0.50), (0.0, 0.0, torso_z), mat=m_steel_lt, bevel=0.045)
        add_box("ChestCrest", (0.20, 0.08, 0.24), (0.0, -0.18, torso_z + 0.04), mat=m_gold, bevel=0.012)
        add_box("Gorget", (0.36, 0.28, 0.10), (0.0, 0.0, torso_z + 0.26), mat=m_gold, bevel=0.015)

        for side, sx in (("L", -0.37), ("R", 0.37)):
            add_sphere(f"Pauldron_{side}", 0.19, (sx, 0.0, torso_z + 0.20), scale=(1.05, 0.95, 0.85), mat=m_steel_lt)
            add_sphere(f"PauldronRim_{side}", 0.18, (sx * 1.03, 0.0, torso_z + 0.17), scale=(1.08, 0.98, 0.35), mat=m_gold)

        helm_z = torso_z + 0.46
        add_cyl("HelmBody", 0.175, 0.32, (0.0, 0.0, helm_z), verts=16, mat=m_steel_lt)
        add_sphere("HelmDome", 0.175, (0.0, 0.0, helm_z + 0.14), scale=(1.0, 1.0, 0.78), mat=m_steel_lt)
        add_box("HelmVisor", (0.24, 0.12, 0.10), (0.0, -0.15, helm_z + 0.02), mat=m_steel_dk, bevel=0.01)
        add_cyl("HelmBand", 0.188, 0.055, (0.0, 0.0, helm_z + 0.11), verts=16, mat=m_gold)
        add_tube_curve(
            "HelmPlume",
            [
                (0.0, -0.02, helm_z + 0.28, 0.065),
                (0.0, 0.12, helm_z + 0.34, 0.085),
                (0.0, 0.30, helm_z + 0.26, 0.075),
                (0.0, 0.44, helm_z + 0.10, 0.045),
            ],
            mat=m_crimson,
        )

        # Flowing crimson cape behind shoulders
        add_box("CapeMain", (0.64, 0.11, 1.15), (0.0, 0.32, 0.88), rot_deg=(-18.0, 0.0, 0.0), mat=m_crimson, bevel=0.03)

        # Left arm + Cobalt & Gold Kite Shield forward-left (-X, -Y)
        add_cyl("Arm_L", 0.10, 0.42, (-0.42, -0.10, torso_z - 0.04), rot_deg=(28.0, 22.0, 0.0), verts=12, mat=m_steel_mid)
        shield_pts = [(-0.27, 0.34), (0.27, 0.34), (0.29, 0.06), (0.0, -0.46), (-0.29, 0.06)]
        shield_root = bpy.data.objects.new("KiteShieldRoot", None)
        bpy.context.collection.objects.link(shield_root)
        shield_root.location = (-0.46, -0.28, 1.06)
        shield_root.rotation_euler = (math.radians(-6.0), math.radians(6.0), math.radians(24.0))
        add_prism("KiteShieldRim", shield_pts, 0.045, mat=m_gold, bevel=0.015, parent=shield_root)
        inner_pts = [(x * 0.80, z * 0.80) for x, z in shield_pts]
        add_prism("KiteShieldFace", inner_pts, 0.052, loc=(0.0, -0.008, 0.0), mat=m_cobalt, bevel=0.01, parent=shield_root)
        add_sphere("KiteShieldBoss", 0.08, (0.0, -0.06, 0.08), scale=(1.0, 0.6, 1.0), mat=m_gold, parent=shield_root)

        # Right arm + Broadsword held clearly forward-right (+X, -Y), separated from cape
        add_cyl("Arm_R", 0.10, 0.44, (0.44, -0.14, torso_z - 0.02), rot_deg=(36.0, 22.0, 0.0), verts=12, mat=m_steel_mid)
        add_broadsword("KnightSword", (0.56, -0.34, 1.22), (26.0, 22.0, 0.0), m_blade, m_steel_dk, m_gold, blade_len=0.92, blade_w=0.105)

        setup_render_and_camera(ortho_scale=2.6, target=(0.06, -0.04, 1.05), yaw_deg=-32.0)
        render_asset_passes("hero_knight")

    # -----------------------------------------------------------------------
    # 2. hero_cleric (Liora) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_cleric() -> None:
        clear_scene()
        m_ivory = make_mat("IvoryRobe", "#F0ECE4")
        m_azure = make_mat("RoyalAzure", "#3452A1")
        m_gold = make_mat("SunlitGold", "#ECA938")
        m_cyan = make_mat("ArcaneCyan", "#54C9C0")
        m_wood = make_mat("StaffWood", "#825132")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        hip_z = add_humanoid_legs(m_wood, m_azure, m_ivory, stance_x=0.18, shin_r=0.12, thigh_r=0.14, boot_size=(0.18, 0.28, 0.15))

        # Flared ivory liturgical robe skirt + azure panels
        add_cone("RobeSkirt", 0.42, 0.25, 0.84, (0.0, 0.0, 0.54), verts=18, mat=m_ivory)
        add_box("FrontStoleLower", (0.20, 0.44, 0.72), (0.0, -0.05, 0.54), mat=m_azure, bevel=0.02)
        add_box("Belt", (0.48, 0.34, 0.09), (0.0, 0.0, hip_z + 0.10), mat=m_gold, bevel=0.015)

        torso_z = hip_z + 0.36
        add_box("Torso", (0.48, 0.32, 0.46), (0.0, 0.0, torso_z), mat=m_ivory, bevel=0.04)
        add_box("FrontStoleUpper", (0.18, 0.35, 0.42), (0.0, -0.01, torso_z + 0.02), mat=m_azure, bevel=0.015)
        add_box("GoldMantle", (0.64, 0.38, 0.16), (0.0, 0.02, torso_z + 0.18), mat=m_gold, bevel=0.03)

        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"Shoulder_{side}", 0.16, (sx, 0.0, torso_z + 0.18), scale=(1.0, 0.9, 0.85), mat=m_gold)

        head_z = torso_z + 0.42
        add_sphere("Head", 0.155, (0.0, -0.04, head_z), mat=m_skin)
        # Mitre/Hood + open gold halo-crest ring behind head
        add_cone("MitreBack", 0.21, 0.07, 0.34, (0.0, 0.05, head_z + 0.14), rot_deg=(14.0, 0.0, 0.0), verts=14, mat=m_ivory)
        add_cyl("MitreBand", 0.19, 0.065, (0.0, 0.01, head_z + 0.11), verts=16, mat=m_azure)
        halo_pts = [
            (
                0.28 * math.cos(math.radians(a)),
                0.15,
                head_z + 0.18 + 0.28 * math.sin(math.radians(a)),
                0.032,
            )
            for a in range(-30, 211, 20)
        ]
        add_tube_curve("HaloRing", halo_pts, mat=m_gold)

        # Right hand (+X, forward-right): Golden flanged war-mace
        add_cyl("Arm_R", 0.095, 0.42, (0.38, -0.14, torso_z - 0.02), rot_deg=(38.0, 20.0, 0.0), verts=12, mat=m_ivory)
        mace_root = bpy.data.objects.new("MaceRoot", None)
        bpy.context.collection.objects.link(mace_root)
        mace_root.location = (0.48, -0.34, 1.20)
        mace_root.rotation_euler = (math.radians(28.0), math.radians(18.0), 0.0)
        add_cyl("MaceShaft", 0.036, 0.64, (0.0, 0.0, 0.14), verts=12, mat=m_wood, parent=mace_root)
        add_sphere("MaceHead", 0.125, (0.0, 0.0, 0.48), mat=m_gold, parent=mace_root)
        for ang in (0.0, 45.0, 90.0, 135.0):
            add_box(f"MaceFlange_{ang}", (0.28, 0.05, 0.22), (0.0, 0.0, 0.48), rot_deg=(0.0, 0.0, ang), mat=m_gold, bevel=0.008, parent=mace_root)

        # Left hand (-X, forward-left): Tall ward staff with glowing arcane cyan crystal orb
        add_cyl("Arm_L", 0.095, 0.40, (-0.38, -0.12, torso_z - 0.02), rot_deg=(28.0, -14.0, 0.0), verts=12, mat=m_ivory)
        staff_root = bpy.data.objects.new("WardStaffRoot", None)
        bpy.context.collection.objects.link(staff_root)
        staff_root.location = (-0.44, -0.26, 0.96)
        staff_root.rotation_euler = (math.radians(8.0), math.radians(-6.0), 0.0)
        add_cyl("WardShaft", 0.04, 1.84, (0.0, 0.0, -0.04), verts=12, mat=m_wood, parent=staff_root)
        add_cyl("WardCollar", 0.068, 0.10, (0.0, 0.0, 0.82), verts=12, mat=m_gold, parent=staff_root)
        add_sphere("WardOrb", 0.145, (0.0, 0.0, 0.98), mat=m_cyan, parent=staff_root)

        setup_render_and_camera(ortho_scale=2.6, target=(0.02, -0.04, 1.02), yaw_deg=-32.0)
        render_asset_passes("hero_cleric")

    # -----------------------------------------------------------------------
    # 3. hero_ranger (Kaelen) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_ranger() -> None:
        clear_scene()
        m_em_dk = make_mat("EmeraldDark", "#2E693B")
        m_em_lt = make_mat("EmeraldBright", "#52A653")
        m_leather = make_mat("ChestnutLeather", "#825132")
        m_bow = make_mat("RecurveWood", "#A36943")
        m_skin = make_mat("WarmSkin", "#EDB89A")
        m_fletch = make_mat("ArrowFletch", "#C23846")
        m_steel = make_mat("ArrowTip", "#D8E2F0")

        hip_z = add_humanoid_legs(m_leather, m_leather, m_em_dk, stance_x=0.22, crouch=0.15, shin_r=0.12, thigh_r=0.14)

        add_box("PelvisTunic", (0.46, 0.30, 0.24), (0.0, 0.0, hip_z + 0.02), mat=m_em_dk, bevel=0.03)
        add_box("Belt", (0.50, 0.33, 0.085), (0.0, 0.0, hip_z + 0.14), mat=m_leather, bevel=0.015)

        torso_z = hip_z + 0.38
        add_box("TunicTorso", (0.48, 0.32, 0.46), (0.0, 0.0, torso_z), mat=m_em_lt, bevel=0.04)
        add_box("LeatherJerkin", (0.42, 0.34, 0.38), (0.0, -0.01, torso_z + 0.02), mat=m_leather, bevel=0.03)
        add_box("ShoulderMantle", (0.62, 0.38, 0.16), (0.0, 0.04, torso_z + 0.18), mat=m_em_dk, bevel=0.03)
        add_box("CloakBack", (0.58, 0.10, 1.05), (0.0, 0.28, 0.86), rot_deg=(-20.0, 0.0, 0.0), mat=m_em_dk, bevel=0.03)

        # Quiver + arrows on back
        quiv = bpy.data.objects.new("QuiverRoot", None)
        bpy.context.collection.objects.link(quiv)
        quiv.location = (-0.18, 0.24, torso_z + 0.12)
        quiv.rotation_euler = (math.radians(16.0), math.radians(-26.0), 0.0)
        add_cyl("QuiverBody", 0.085, 0.56, (0.0, 0.0, 0.0), verts=12, mat=m_leather, parent=quiv)
        for ax, ay in ((-0.03, 0.02), (0.02, -0.02), (0.0, 0.03)):
            add_cyl(f"QArrow_{ax}", 0.016, 0.22, (ax, ay, 0.36), verts=8, mat=m_bow, parent=quiv)
            add_box(f"QFletch_{ax}", (0.045, 0.015, 0.09), (ax, ay, 0.46), mat=m_fletch, bevel=0.004, parent=quiv)

        # Head + pointed emerald hood (open front so face reads cleanly)
        head_z = torso_z + 0.40
        add_sphere("Head", 0.15, (0.0, -0.06, head_z), mat=m_skin)
        add_sphere("HoodCowl", 0.18, (0.0, 0.03, head_z + 0.03), scale=(1.05, 1.12, 1.06), mat=m_em_dk)
        add_cone("HoodTip", 0.15, 0.02, 0.30, (0.0, 0.20, head_z + 0.12), rot_deg=(-58.0, 0.0, 0.0), verts=12, mat=m_em_dk)

        # Arms + Recurve Bow held forward-right (+X, -Y) with notched arrow aiming East/Right
        add_cyl("Arm_R_Bracer", 0.095, 0.44, (0.40, -0.18, torso_z + 0.02), rot_deg=(68.0, 24.0, 0.0), verts=12, mat=m_leather)
        add_cyl("Arm_L_Draw", 0.09, 0.40, (-0.28, -0.18, torso_z + 0.02), rot_deg=(72.0, -26.0, 0.0), verts=12, mat=m_em_lt)

        bow_root = bpy.data.objects.new("BowRoot", None)
        bpy.context.collection.objects.link(bow_root)
        bow_root.location = (0.46, -0.34, torso_z + 0.10)
        bow_root.rotation_euler = (math.radians(0.0), math.radians(10.0), math.radians(28.0))
        add_tube_curve(
            "RecurveBow",
            [
                (0.0, 0.22, -0.62, 0.026),
                (0.0, 0.06, -0.44, 0.034),
                (0.0, -0.06, -0.22, 0.042),
                (0.0, -0.08, 0.0, 0.048),
                (0.0, -0.06, 0.22, 0.042),
                (0.0, 0.06, 0.44, 0.034),
                (0.0, 0.22, 0.62, 0.026),
            ],
            mat=m_bow,
            parent=bow_root,
        )
        # Notched arrow pointing forward-right (-Y rotated +28 deg around Z)
        add_cyl("ArrowShaft", 0.018, 0.82, (0.0, -0.14, 0.02), rot_deg=(90.0, 0.0, 0.0), verts=8, mat=m_bow, parent=bow_root)
        add_cone("ArrowHead", 0.042, 0.004, 0.12, (0.0, -0.58, 0.02), rot_deg=(-90.0, 0.0, 0.0), verts=4, mat=m_steel, smooth=False, parent=bow_root)
        add_box("ArrowFletch", (0.06, 0.12, 0.02), (0.0, 0.24, 0.02), mat=m_fletch, bevel=0.004, parent=bow_root)

        setup_render_and_camera(ortho_scale=2.6, target=(0.06, -0.04, 1.00), yaw_deg=-32.0)
        render_asset_passes("hero_ranger")

    # -----------------------------------------------------------------------
    # 4. hero_mage (Theron) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_mage() -> None:
        clear_scene()
        m_sapphire = make_mat("SapphireRobe", "#273C78")
        m_cobalt_lt = make_mat("CobaltTrim", "#476EC7")
        m_gold = make_mat("ArcaneGold", "#ECA938")
        m_beard = make_mat("WhiteBeard", "#F0ECE4")
        m_staff = make_mat("GnarledWood", "#825132")
        m_crystal = make_mat("ArcaneCrystal", "#85E6DA")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        hip_z = add_humanoid_legs(m_staff, m_sapphire, m_sapphire, stance_x=0.18, shin_r=0.12, thigh_r=0.14, boot_size=(0.18, 0.28, 0.15))

        # Sweeping sapphire wizard robe + gold trim + sash
        add_cone("WizardSkirt", 0.45, 0.26, 0.86, (0.0, 0.0, 0.54), verts=18, mat=m_sapphire)
        add_cone("RobeHemGold", 0.465, 0.43, 0.10, (0.0, 0.0, 0.16), verts=18, mat=m_gold)
        add_box("RobePanel", (0.20, 0.46, 0.72), (0.0, -0.05, 0.55), mat=m_cobalt_lt, bevel=0.02)
        add_box("ArcaneSash", (0.50, 0.35, 0.11), (0.0, 0.0, hip_z + 0.10), mat=m_gold, bevel=0.015)

        torso_z = hip_z + 0.36
        add_box("Torso", (0.48, 0.34, 0.46), (0.0, 0.0, torso_z), mat=m_sapphire, bevel=0.04)
        add_box("ShoulderTrim", (0.62, 0.38, 0.16), (0.0, 0.02, torso_z + 0.18), mat=m_cobalt_lt, bevel=0.03)
        add_box("GoldCollar", (0.40, 0.34, 0.08), (0.0, -0.02, torso_z + 0.24), mat=m_gold, bevel=0.015)

        # Head + deep sapphire hood + flowing white beard
        head_z = torso_z + 0.42
        add_sphere("Head", 0.15, (0.0, -0.06, head_z), mat=m_skin)
        add_sphere("Hood", 0.185, (0.0, 0.05, head_z + 0.04), scale=(1.08, 1.15, 1.08), mat=m_sapphire)
        add_cone("HoodPeak", 0.16, 0.02, 0.36, (0.0, 0.20, head_z + 0.16), rot_deg=(-54.0, 0.0, 0.0), verts=12, mat=m_sapphire)
        add_tube_curve(
            "WhiteBeard",
            [
                (0.0, -0.16, head_z - 0.05, 0.115),
                (0.0, -0.20, head_z - 0.22, 0.105),
                (0.0, -0.22, head_z - 0.38, 0.075),
                (0.0, -0.20, head_z - 0.50, 0.035),
            ],
            mat=m_beard,
        )

        # Left arm (-X, forward-left): glowing arcane spell focus
        add_cyl("Sleeve_L", 0.12, 0.42, (-0.38, -0.14, torso_z - 0.02), rot_deg=(36.0, -18.0, 0.0), verts=12, mat=m_cobalt_lt)
        add_sphere("SpellOrb_L", 0.10, (-0.44, -0.30, torso_z + 0.04), mat=m_crystal)

        # Right arm (+X, forward-right) holding tall gnarled wooden staff topped with bright cyan crystal
        add_cyl("Sleeve_R", 0.12, 0.44, (0.40, -0.16, torso_z + 0.04), rot_deg=(46.0, 22.0, 0.0), verts=12, mat=m_cobalt_lt)
        staff_root = bpy.data.objects.new("MageStaffRoot", None)
        bpy.context.collection.objects.link(staff_root)
        staff_root.location = (0.50, -0.34, 1.02)
        staff_root.rotation_euler = (math.radians(10.0), math.radians(8.0), 0.0)
        add_tube_curve(
            "GnarledStaff",
            [
                (0.0, 0.0, -1.00, 0.036),
                (0.02, -0.02, -0.30, 0.042),
                (-0.02, 0.01, 0.30, 0.044),
                (0.03, -0.02, 0.72, 0.052),
                (0.07, -0.04, 0.92, 0.038),
            ],
            mat=m_staff,
            parent=staff_root,
        )
        add_tube_curve(
            "StaffFork",
            [
                (0.02, -0.01, 0.72, 0.036),
                (-0.07, -0.03, 0.90, 0.032),
            ],
            mat=m_staff,
            parent=staff_root,
        )
        add_cone("CrystalTop", 0.11, 0.01, 0.24, (0.0, -0.03, 1.04), verts=6, mat=m_crystal, smooth=False, parent=staff_root)
        add_cone("CrystalBot", 0.11, 0.01, 0.16, (0.0, -0.03, 0.85), rot_deg=(180.0, 0.0, 0.0), verts=6, mat=m_crystal, smooth=False, parent=staff_root)

        setup_render_and_camera(ortho_scale=2.6, target=(0.04, -0.04, 1.04), yaw_deg=-32.0)
        render_asset_passes("hero_mage")

    # -----------------------------------------------------------------------
    # 5. hero_paladin (Seraphina) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_paladin() -> None:
        clear_scene()
        m_silver = make_mat("RadiantSilver", "#D8E2F0")
        m_gold = make_mat("RadiantGold", "#FFC952")
        m_gold_trim = make_mat("GoldTrim", "#ECA938")
        m_tabard = make_mat("WhiteTabard", "#F0ECE4")
        m_dark = make_mat("SlateJoint", "#475061")

        hip_z = add_humanoid_legs(m_silver, m_silver, m_silver, stance_x=0.22, shin_r=0.125, thigh_r=0.145)
        for side, sx in (("L", -0.22), ("R", 0.22)):
            add_sphere(f"KneeGold_{side}", 0.14, (sx, -0.04, 0.56), scale=(1.0, 0.9, 0.9), mat=m_gold)

        # White & gold sunburst tabard over hips + chest
        add_box("TabardLower", (0.44, 0.34, 0.42), (0.0, -0.02, hip_z - 0.06), mat=m_tabard, bevel=0.025)
        add_box("TabardSunStripe", (0.16, 0.36, 0.40), (0.0, -0.02, hip_z - 0.06), mat=m_gold_trim, bevel=0.01)
        add_box("Belt", (0.50, 0.36, 0.09), (0.0, 0.0, hip_z + 0.14), mat=m_gold, bevel=0.015)

        torso_z = hip_z + 0.40
        add_box("PlateTorso", (0.54, 0.36, 0.50), (0.0, 0.0, torso_z), mat=m_silver, bevel=0.045)
        add_box("ChestTabard", (0.30, 0.38, 0.42), (0.0, -0.01, torso_z), mat=m_tabard, bevel=0.02)
        add_sphere("SunburstCrest", 0.11, (0.0, -0.20, torso_z + 0.05), scale=(1.0, 0.4, 1.0), mat=m_gold)

        for side, sx in (("L", -0.37), ("R", 0.37)):
            add_sphere(f"Pauldron_{side}", 0.19, (sx, 0.0, torso_z + 0.21), scale=(1.05, 0.95, 0.88), mat=m_silver)
            add_sphere(f"PauldronGold_{side}", 0.18, (sx * 1.04, 0.0, torso_z + 0.18), scale=(1.08, 0.98, 0.36), mat=m_gold)

        # Iconic winged silver-gold helm
        helm_z = torso_z + 0.46
        add_cyl("HelmBody", 0.17, 0.32, (0.0, 0.0, helm_z), verts=16, mat=m_silver)
        add_sphere("HelmDome", 0.17, (0.0, 0.0, helm_z + 0.14), scale=(1.0, 1.0, 0.78), mat=m_silver)
        add_box("HelmVisor", (0.22, 0.12, 0.09), (0.0, -0.14, helm_z + 0.02), mat=m_dark, bevel=0.01)
        add_box("HelmCrest", (0.06, 0.24, 0.10), (0.0, 0.0, helm_z + 0.26), mat=m_gold, bevel=0.015)

        # Two crisp curved wing crests on helmet (silver outer wing + gold inner wing ridge)
        wing_profile = [
            (0.0, 0.0),
            (0.18, 0.12),
            (0.32, 0.32),
            (0.24, 0.38),
            (0.10, 0.24),
            (-0.02, 0.10),
        ]
        wing_gold_profile = [
            (0.0, 0.0),
            (0.13, 0.09),
            (0.24, 0.24),
            (0.16, 0.28),
            (0.06, 0.18),
            (-0.01, 0.08),
        ]
        for side, sign in (("L", -1.0), ("R", 1.0)):
            w_pts = [(sign * x, z) for x, z in wing_profile]
            wg_pts = [(sign * x, z) for x, z in wing_gold_profile]
            if sign < 0:
                w_pts = list(reversed(w_pts))
                wg_pts = list(reversed(wg_pts))
            add_prism(
                f"HelmWing_{side}",
                w_pts,
                0.025,
                loc=(sign * 0.12, 0.02, helm_z + 0.05),
                rot_deg=(-12.0, 0.0, sign * -22.0),
                mat=m_silver,
                bevel=0.008,
            )
            add_prism(
                f"HelmWingGold_{side}",
                wg_pts,
                0.030,
                loc=(sign * 0.12, 0.01, helm_z + 0.04),
                rot_deg=(-12.0, 0.0, sign * -22.0),
                mat=m_gold,
                bevel=0.006,
            )

        # Both arms angled forward-right gripping two-handed greatsword
        add_cyl("Arm_R", 0.10, 0.44, (0.40, -0.16, torso_z - 0.02), rot_deg=(48.0, 22.0, 0.0), verts=12, mat=m_silver)
        add_cyl("Arm_L", 0.10, 0.46, (-0.10, -0.24, torso_z - 0.06), rot_deg=(58.0, 44.0, 0.0), verts=12, mat=m_silver)
        add_broadsword(
            "Greatsword",
            (0.44, -0.34, 1.24),
            (24.0, 18.0, 0.0),
            m_silver,
            m_dark,
            m_gold_trim,
            blade_len=1.20,
            blade_w=0.125,
            grip_len=0.34,
        )

        setup_render_and_camera(ortho_scale=2.6, target=(0.06, -0.06, 1.06), yaw_deg=-32.0)
        render_asset_passes("hero_paladin")

    # -----------------------------------------------------------------------
    # 6. hero_rogue (Vex) - yaw = -32.0 (facing East/Right)
    # -----------------------------------------------------------------------
    def build_hero_rogue() -> None:
        clear_scene()
        m_slate = make_mat("SlateLeather", "#2F3A59")
        m_armor = make_mat("DarkArmor", "#475061")
        m_hood = make_mat("IndigoHood", "#3E4C73")
        m_scarf = make_mat("CrimsonScarf", "#C23846")
        m_dagger = make_mat("SteelDagger", "#BAC6DA")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        hip_z = add_humanoid_legs(m_slate, m_armor, m_slate, stance_x=0.25, crouch=0.72, shin_r=0.118, thigh_r=0.138)

        add_box("Pelvis", (0.44, 0.28, 0.18), (0.0, 0.02, hip_z + 0.02), mat=m_slate, bevel=0.02)
        add_box("Belt", (0.48, 0.32, 0.08), (0.0, 0.0, hip_z + 0.10), mat=m_armor, bevel=0.012)

        # Hunched agile torso leaning forward (-Y)
        torso_z = hip_z + 0.30
        add_box("Torso", (0.46, 0.30, 0.42), (0.0, -0.06, torso_z), rot_deg=(-18.0, 0.0, 0.0), mat=m_slate, bevel=0.035)
        add_box("ChestArmor", (0.42, 0.32, 0.32), (0.0, -0.08, torso_z + 0.02), rot_deg=(-18.0, 0.0, 0.0), mat=m_armor, bevel=0.025)

        for side, sx in (("L", -0.32), ("R", 0.32)):
            add_sphere(f"Shoulder_{side}", 0.15, (sx, -0.08, torso_z + 0.16), scale=(1.0, 0.9, 0.8), mat=m_armor)

        # Deep pointed hood + crimson scarf & trailing scarf tail
        head_z = torso_z + 0.34
        add_sphere("Head", 0.145, (0.0, -0.16, head_z), mat=m_skin)
        add_sphere("Hood", 0.185, (0.0, -0.10, head_z + 0.03), scale=(1.06, 1.22, 1.05), mat=m_hood)
        add_cone("HoodPoint", 0.15, 0.02, 0.32, (0.0, 0.08, head_z + 0.10), rot_deg=(-64.0, 0.0, 0.0), verts=12, mat=m_hood)
        add_cyl("ScarfWrap", 0.175, 0.14, (0.0, -0.13, head_z - 0.08), verts=14, mat=m_scarf)
        add_box("ScarfTail1", (0.14, 0.48, 0.06), (-0.06, 0.18, head_z - 0.04), rot_deg=(-12.0, 0.0, 14.0), mat=m_scarf, bevel=0.015)
        add_box("ScarfTail2", (0.12, 0.42, 0.05), (0.08, 0.16, head_z - 0.08), rot_deg=(-18.0, 0.0, -16.0), mat=m_scarf, bevel=0.015)

        # Twin gleaming steel curved daggers held low & forward-right
        add_cyl("Arm_R", 0.09, 0.40, (0.36, -0.22, torso_z - 0.04), rot_deg=(58.0, 22.0, 0.0), verts=12, mat=m_armor)
        add_curved_blade("Dagger_R", (0.48, -0.42, torso_z - 0.10), (68.0, 24.0, 0.0), m_dagger, m_slate, m_armor, length=0.46, width=0.065, curve_x=0.08)

        add_cyl("Arm_L", 0.09, 0.38, (-0.34, -0.20, torso_z - 0.06), rot_deg=(46.0, -16.0, 0.0), verts=12, mat=m_armor)
        add_curved_blade("Dagger_L", (-0.40, -0.38, torso_z - 0.14), (58.0, 12.0, 0.0), m_dagger, m_slate, m_armor, length=0.44, width=0.065, curve_x=0.07)

        setup_render_and_camera(ortho_scale=2.6, target=(0.06, -0.10, 0.88), yaw_deg=-32.0)
        render_asset_passes("hero_rogue")

    # -----------------------------------------------------------------------
    # 7. monster_giant_frog - yaw = +32.0 (facing West/Left)
    # -----------------------------------------------------------------------
    def build_monster_giant_frog() -> None:
        clear_scene()
        m_skin = make_mat("FrogMoss", "#3D8748")
        m_dark = make_mat("FrogDark", "#2E693B")
        m_belly = make_mat("FrogBelly", "#E0C8A8")
        m_sac = make_mat("FrogSac", "#ECA938")
        m_eye = make_mat("FrogAmber", "#FFC952")
        m_pupil = make_mat("FrogPupil", "#1C2033")

        add_sphere("FrogTorso", 0.52, (0.0, 0.10, 0.44), scale=(1.15, 1.28, 0.76), rot_deg=(-16.0, 0.0, 0.0), mat=m_skin)
        add_sphere("FrogHead", 0.42, (0.0, -0.34, 0.62), scale=(1.18, 1.05, 0.64), rot_deg=(-8.0, 0.0, 0.0), mat=m_skin)
        add_box("JawRidge", (0.76, 0.34, 0.10), (0.0, -0.58, 0.50), rot_deg=(8.0, 0.0, 0.0), mat=m_belly, bevel=0.03)
        add_sphere("ThroatSac", 0.29, (0.0, -0.48, 0.35), scale=(1.12, 1.08, 0.92), mat=m_sac)

        for side, sx in (("L", -0.24), ("R", 0.24)):
            add_sphere(f"EyeRidge_{side}", 0.155, (sx, -0.38, 0.86), scale=(1.0, 1.0, 0.92), mat=m_skin)
            add_sphere(f"EyeOrb_{side}", 0.105, (sx * 1.02, -0.46, 0.88), mat=m_eye)
            add_box(f"EyePupil_{side}", (0.09, 0.04, 0.035), (sx * 1.03, -0.55, 0.88), mat=m_pupil, bevel=0.005)

        for idx, (wx, wy, wz, wr) in enumerate((
            (-0.18, 0.18, 0.76, 0.105),
            (0.20, 0.16, 0.75, 0.105),
            (-0.14, 0.42, 0.62, 0.095),
            (0.16, 0.44, 0.60, 0.095),
            (0.0, 0.28, 0.72, 0.11),
        )):
            add_sphere(f"Wart_{idx}", wr, (wx, wy, wz), scale=(1.0, 1.1, 0.68), mat=m_dark)

        for side, sx in (("L", -0.56), ("R", 0.56)):
            add_sphere(f"HindThigh_{side}", 0.32, (sx, 0.28, 0.36), scale=(0.76, 1.24, 0.82), rot_deg=(12.0, 0.0, -18.0 if side == "L" else 18.0), mat=m_skin)
            add_box(f"HindFoot_{side}", (0.34, 0.48, 0.10), (sx * 1.06, 0.14, 0.05), rot_deg=(0.0, 0.0, -24.0 if side == "L" else 24.0), mat=m_belly, bevel=0.03)

        for side, sx in (("L", -0.38), ("R", 0.38)):
            add_cyl(f"ForeArm_{side}", 0.115, 0.44, (sx, -0.38, 0.24), rot_deg=(18.0, 12.0 if side == "L" else -12.0, 0.0), verts=12, mat=m_skin)
            add_box(f"ForeFoot_{side}", (0.25, 0.28, 0.09), (sx * 1.08, -0.52, 0.045), rot_deg=(0.0, 0.0, -15.0 if side == "L" else 15.0), mat=m_belly, bevel=0.025)

        setup_render_and_camera(ortho_scale=2.2, target=(0.0, -0.05, 0.48), yaw_deg=32.0)
        render_asset_passes("monster_giant_frog")

    # -----------------------------------------------------------------------
    # 8. monster_goblin_raider - yaw = +32.0 (facing West/Left)
    # -----------------------------------------------------------------------
    def build_monster_goblin_raider() -> None:
        clear_scene()
        m_gob = make_mat("GoblinSkin", "#52A653")
        m_gob_dk = make_mat("GoblinShadow", "#3D8748")
        m_lth = make_mat("GoblinLeather", "#825132")
        m_lth_dk = make_mat("GoblinLoin", "#633C24")
        m_steel = make_mat("ScimitarSteel", "#9AA7BD")
        m_wood = make_mat("BucklerWood", "#A36943")
        m_iron = make_mat("BucklerIron", "#606B80")
        m_eye = make_mat("GoblinEye", "#FFC952")

        hip_z = add_humanoid_legs(m_lth_dk, m_gob_dk, m_gob, stance_x=0.21, crouch=0.55, shin_r=0.115, thigh_r=0.135, boot_size=(0.18, 0.28, 0.14))

        add_box("Loincloth", (0.42, 0.28, 0.22), (0.0, -0.02, hip_z - 0.02), mat=m_lth_dk, bevel=0.025)
        add_box("Belt", (0.46, 0.30, 0.08), (0.0, -0.02, hip_z + 0.09), mat=m_lth, bevel=0.015)

        # Hunched forward goblin torso
        torso_z = hip_z + 0.28
        add_sphere("GobTorso", 0.26, (0.0, -0.06, torso_z), scale=(1.05, 0.85, 1.05), rot_deg=(-20.0, 0.0, 0.0), mat=m_gob)
        add_box("LeatherHarness", (0.44, 0.30, 0.32), (0.0, -0.08, torso_z), rot_deg=(-20.0, 0.0, 0.0), mat=m_lth, bevel=0.025)

        for side, sx in (("L", -0.31), ("R", 0.31)):
            add_sphere(f"Shoulder_{side}", 0.145, (sx, -0.08, torso_z + 0.13), scale=(1.0, 0.9, 0.85), mat=m_gob)

        # Goblin head + brow + nose + splayed large pointed ears
        head_z = torso_z + 0.32
        add_sphere("GobHead", 0.19, (0.0, -0.18, head_z), scale=(1.08, 1.05, 0.92), mat=m_gob)
        add_cone("GobNose", 0.06, 0.015, 0.14, (0.0, -0.36, head_z - 0.02), rot_deg=(-105.0, 0.0, 0.0), verts=10, mat=m_gob_dk)
        for side, sx in (("L", -0.08), ("R", 0.08)):
            add_sphere(f"GobEye_{side}", 0.042, (sx, -0.35, head_z + 0.03), mat=m_eye)
            # Large pointed goblin ears splayed out
            add_cone(
                f"GobEar_{side}",
                0.08,
                0.01,
                0.28,
                (sx * 3.1, -0.14, head_z + 0.04),
                rot_deg=(12.0, -78.0 if sx < 0 else 78.0, 0.0),
                verts=10,
                mat=m_gob,
            )

        # Left hand (-X, forward-left): notched steel scimitar
        add_cyl("Arm_L", 0.09, 0.38, (-0.36, -0.20, torso_z - 0.02), rot_deg=(46.0, -24.0, 0.0), verts=12, mat=m_gob)
        add_curved_blade("GobScimitar", (-0.46, -0.38, torso_z + 0.02), (28.0, -24.0, 0.0), m_steel, m_lth_dk, m_iron, length=0.62, width=0.095, curve_x=-0.12)

        # Right hand (+X, closer front-right): round wooden/iron buckler
        add_cyl("Arm_R", 0.09, 0.38, (0.35, -0.18, torso_z - 0.04), rot_deg=(28.0, 18.0, 0.0), verts=12, mat=m_gob)
        add_round_shield("GobBuckler", (0.42, -0.30, torso_z - 0.06), (-8.0, 0.0, -28.0), 0.25, m_wood, m_iron)

        setup_render_and_camera(ortho_scale=2.2, target=(-0.06, -0.10, 0.72), yaw_deg=32.0)
        render_asset_passes("monster_goblin_raider")

    # -----------------------------------------------------------------------
    # 9. monster_goblin_archer - yaw = +32.0 (facing West/Left)
    #    ACTUAL emerald-green goblin, long ears out from hood, NO horns/tail/lizard snout
    # -----------------------------------------------------------------------
    def build_monster_goblin_archer() -> None:
        clear_scene()
        m_gob = make_mat("GoblinSkin", "#52A653")
        m_gob_dk = make_mat("GoblinShadow", "#3D8748")
        m_hood = make_mat("RaggedHood", "#633C24")
        m_jerkin = make_mat("LeatherJerkin", "#825132")
        m_bow = make_mat("ShortbowWood", "#A36943")
        m_fletch = make_mat("ArrowFletch", "#C23846")
        m_steel = make_mat("ArrowSteel", "#BAC6DA")
        m_eye = make_mat("GoblinEye", "#FFC952")

        hip_z = add_humanoid_legs(m_hood, m_gob_dk, m_gob, stance_x=0.20, crouch=0.50, shin_r=0.115, thigh_r=0.135, boot_size=(0.18, 0.28, 0.14))

        add_box("Pelvis", (0.42, 0.26, 0.20), (0.0, -0.02, hip_z), mat=m_hood, bevel=0.02)
        add_box("Belt", (0.45, 0.29, 0.075), (0.0, -0.02, hip_z + 0.10), mat=m_jerkin, bevel=0.012)

        torso_z = hip_z + 0.28
        add_box("Jerkin", (0.44, 0.30, 0.36), (0.0, -0.06, torso_z), rot_deg=(-16.0, 0.0, 0.0), mat=m_jerkin, bevel=0.03)
        add_box("HoodMantle", (0.50, 0.34, 0.12), (0.0, -0.02, torso_z + 0.15), rot_deg=(-12.0, 0.0, 0.0), mat=m_hood, bevel=0.025)

        # Quiver of feathered arrows on back
        quiv = bpy.data.objects.new("GobQuiverRoot", None)
        bpy.context.collection.objects.link(quiv)
        quiv.location = (0.16, 0.18, torso_z + 0.08)
        quiv.rotation_euler = (math.radians(16.0), math.radians(24.0), 0.0)
        add_cyl("GobQuiver", 0.078, 0.48, (0.0, 0.0, 0.0), verts=12, mat=m_jerkin, parent=quiv)
        for ax, ay in ((-0.025, 0.02), (0.025, -0.02), (0.0, 0.03)):
            add_cyl(f"GQArrow_{ax}", 0.015, 0.18, (ax, ay, 0.30), verts=8, mat=m_bow, parent=quiv)
            add_box(f"GQFletch_{ax}", (0.042, 0.015, 0.08), (ax, ay, 0.39), mat=m_fletch, bevel=0.004, parent=quiv)

        # Goblin head + ragged hood + unmistakable long pointed goblin ears poking out from hood
        head_z = torso_z + 0.32
        add_sphere("GobHead", 0.18, (0.0, -0.16, head_z), scale=(1.04, 1.0, 0.92), mat=m_gob)
        add_cone("GobNose", 0.052, 0.012, 0.12, (0.0, -0.33, head_z - 0.02), rot_deg=(-102.0, 0.0, 0.0), verts=10, mat=m_gob_dk)
        add_sphere("RaggedHood", 0.20, (0.0, -0.10, head_z + 0.04), scale=(1.06, 1.15, 0.98), mat=m_hood)
        add_cone("HoodTail", 0.15, 0.02, 0.24, (0.0, 0.08, head_z + 0.10), rot_deg=(-60.0, 0.0, 0.0), verts=10, mat=m_hood)
        for side, sx in (("L", -0.075), ("R", 0.075)):
            add_sphere(f"GobEye_{side}", 0.038, (sx, -0.32, head_z + 0.02), mat=m_eye)
            add_cone(
                f"GobEar_{side}",
                0.075,
                0.01,
                0.32,
                (sx * 3.3, -0.13, head_z + 0.02),
                rot_deg=(10.0, -82.0 if sx < 0 else 82.0, 0.0),
                verts=10,
                mat=m_gob,
            )

        # Bow aimed forward-left (-X, -Y) toward heroes
        add_cyl("Arm_L_Bow", 0.088, 0.38, (-0.34, -0.22, torso_z + 0.02), rot_deg=(68.0, -26.0, 0.0), verts=12, mat=m_gob)
        add_cyl("Arm_R_Draw", 0.088, 0.36, (0.24, -0.20, torso_z + 0.02), rot_deg=(70.0, 24.0, 0.0), verts=12, mat=m_gob)

        bow_root = bpy.data.objects.new("GobBowRoot", None)
        bpy.context.collection.objects.link(bow_root)
        bow_root.location = (-0.42, -0.36, torso_z + 0.06)
        bow_root.rotation_euler = (math.radians(0.0), math.radians(-10.0), math.radians(-26.0))
        add_tube_curve(
            "GobShortbow",
            [
                (0.0, 0.18, -0.48, 0.024),
                (0.0, 0.04, -0.32, 0.032),
                (0.0, -0.06, -0.14, 0.038),
                (0.0, -0.08, 0.0, 0.042),
                (0.0, -0.06, 0.14, 0.038),
                (0.0, 0.04, 0.32, 0.032),
                (0.0, 0.18, 0.48, 0.024),
            ],
            mat=m_bow,
            parent=bow_root,
        )
        add_cyl("GobArrowShaft", 0.016, 0.68, (0.0, -0.12, 0.02), rot_deg=(90.0, 0.0, 0.0), verts=8, mat=m_bow, parent=bow_root)
        add_cone("GobArrowTip", 0.038, 0.004, 0.11, (0.0, -0.48, 0.02), rot_deg=(-90.0, 0.0, 0.0), verts=4, mat=m_steel, smooth=False, parent=bow_root)
        add_box("GobArrowFletch", (0.05, 0.10, 0.02), (0.0, 0.20, 0.02), mat=m_fletch, bevel=0.004, parent=bow_root)

        setup_render_and_camera(ortho_scale=2.2, target=(-0.06, -0.10, 0.72), yaw_deg=32.0)
        render_asset_passes("monster_goblin_archer")

    # -----------------------------------------------------------------------
    # 10. monster_orc_warrior - yaw = +32.0 (facing West/Left)
    # -----------------------------------------------------------------------
    def build_monster_orc_warrior() -> None:
        clear_scene()
        m_orc = make_mat("OrcMoss", "#5A8254")
        m_orc_hi = make_mat("OrcOchre", "#77A168")
        m_iron = make_mat("SlateIron", "#475061")
        m_iron_dk = make_mat("DarkIron", "#323845")
        m_blade = make_mat("CleaverSteel", "#9AA7BD")
        m_skirt = make_mat("CrimsonSkirt", "#992838")
        m_tusk = make_mat("IvoryTusk", "#F0DEC2")
        m_eye = make_mat("OrcEye", "#E35259")
        m_haft = make_mat("AxeHaft", "#633C24")

        hip_z = add_humanoid_legs(m_iron_dk, m_orc, m_orc, stance_x=0.26, crouch=0.15, shin_r=0.145, thigh_r=0.175, boot_size=(0.24, 0.36, 0.18))
        for side, sx in (("L", -0.26), ("R", 0.26)):
            add_box(f"Greave_{side}", (0.24, 0.16, 0.34), (sx, -0.09, 0.38), mat=m_iron, bevel=0.02)

        # Crimson war-skirt + studded dark slate-iron belt
        add_cone("WarSkirt", 0.44, 0.32, 0.42, (0.0, 0.0, hip_z - 0.06), verts=14, mat=m_skirt)
        add_box("OrcBelt", (0.62, 0.42, 0.12), (0.0, 0.0, hip_z + 0.14), mat=m_iron_dk, bevel=0.02)
        add_box("BeltBuckle", (0.18, 0.08, 0.14), (0.0, -0.21, hip_z + 0.14), mat=m_iron, bevel=0.015)

        # Massive broad muscular chest
        torso_z = hip_z + 0.44
        add_box("OrcChest", (0.72, 0.46, 0.56), (0.0, 0.0, torso_z), mat=m_orc, bevel=0.06)
        for sx in (-0.18, 0.18):
            add_sphere(f"Pec_{sx}", 0.20, (sx, -0.14, torso_z + 0.08), scale=(1.1, 0.7, 0.9), mat=m_orc_hi)
        add_box("ChestStrap", (0.74, 0.48, 0.11), (0.0, 0.0, torso_z + 0.02), rot_deg=(0.0, 28.0, 0.0), mat=m_iron_dk, bevel=0.015)

        # Massive studded slate-iron pauldrons
        for side, sx in (("L", -0.46), ("R", 0.46)):
            add_sphere(f"OrcPauldron_{side}", 0.24, (sx, 0.0, torso_z + 0.24), scale=(1.1, 1.0, 0.85), mat=m_iron)
            add_cone(f"PauldronSpike_{side}", 0.055, 0.008, 0.18, (sx * 1.12, 0.0, torso_z + 0.40), rot_deg=(0.0, -24.0 if sx < 0 else 24.0, 0.0), verts=8, mat=m_blade)

        # Heavy-browed orc head + prominent ivory lower tusks
        head_z = torso_z + 0.46
        add_sphere("OrcHead", 0.21, (0.0, -0.06, head_z), scale=(1.05, 1.05, 0.96), mat=m_orc)
        add_box("OrcBrow", (0.34, 0.18, 0.10), (0.0, -0.20, head_z + 0.08), mat=m_orc_hi, bevel=0.02)
        add_box("OrcJaw", (0.34, 0.24, 0.14), (0.0, -0.16, head_z - 0.10), mat=m_orc_hi, bevel=0.025)
        for side, sx in (("L", -0.09), ("R", 0.09)):
            add_sphere(f"OrcEye_{side}", 0.036, (sx, -0.25, head_z + 0.02), mat=m_eye)
            add_cone(f"OrcTusk_{side}", 0.036, 0.008, 0.15, (sx * 1.15, -0.26, head_z - 0.02), rot_deg=(-18.0, 0.0, 0.0), verts=8, mat=m_tusk)
            add_cone(f"OrcEar_{side}", 0.05, 0.008, 0.16, (sx * 2.4, -0.02, head_z + 0.04), rot_deg=(0.0, -76.0 if sx < 0 else 76.0, 0.0), verts=8, mat=m_orc)

        # Right arm (+X): thick armored fist
        add_cyl("Arm_R", 0.135, 0.50, (0.48, -0.12, torso_z - 0.04), rot_deg=(-24.0, -18.0, 0.0), verts=12, mat=m_orc)
        add_sphere("Fist_R", 0.12, (0.54, -0.24, torso_z - 0.26), mat=m_iron_dk)

        # Left arm (-X, forward-left): huge heavy notched iron battleaxe/cleaver
        add_cyl("Arm_L", 0.135, 0.50, (-0.48, -0.16, torso_z + 0.02), rot_deg=(44.0, -22.0, 0.0), verts=12, mat=m_orc)
        axe_root = bpy.data.objects.new("BattleaxeRoot", None)
        bpy.context.collection.objects.link(axe_root)
        axe_root.location = (-0.56, -0.36, torso_z + 0.10)
        axe_root.rotation_euler = (math.radians(20.0), math.radians(-14.0), 0.0)
        add_cyl("AxeHaft", 0.048, 1.32, (0.0, 0.0, 0.10), verts=12, mat=m_haft, parent=axe_root)
        add_box("AxeSocket", (0.14, 0.14, 0.22), (0.0, 0.0, 0.62), mat=m_iron_dk, bevel=0.015, parent=axe_root)
        axe_profile = [
            (-0.04, 0.50),
            (-0.42, 0.36),
            (-0.50, 0.62),
            (-0.42, 0.90),
            (-0.04, 0.76),
        ]
        add_prism("AxeHead", axe_profile, 0.036, mat=m_blade, bevel=0.012, parent=axe_root)

        setup_render_and_camera(ortho_scale=2.7, target=(-0.18, -0.06, 1.12), yaw_deg=32.0)
        render_asset_passes("monster_orc_warrior")

    # -----------------------------------------------------------------------
    # 11. monster_giant_rat - yaw = +32.0 (facing West/Left)
    # -----------------------------------------------------------------------
    def build_monster_giant_rat() -> None:
        clear_scene()
        m_fur = make_mat("RatFur", "#6B5542")
        m_fur_dk = make_mat("RatBristle", "#4D3C2E")
        m_grey = make_mat("RatGrey", "#87817A")
        m_tail = make_mat("RatTail", "#BF7E62")
        m_fang = make_mat("RatFang", "#F0ECE4")
        m_eye = make_mat("RatCrimson", "#E35259")

        # Hunched body low to ground
        add_sphere("RatBody", 0.44, (0.0, 0.12, 0.40), scale=(0.82, 1.48, 0.78), rot_deg=(-10.0, 0.0, 0.0), mat=m_fur)
        add_sphere("RatRump", 0.38, (0.0, 0.44, 0.36), scale=(0.88, 1.05, 0.85), mat=m_fur_dk)
        # Bristled spine tufts
        for idx, (wy, wz, wr) in enumerate(((-0.06, 0.68, 0.14), (0.14, 0.72, 0.15), (0.34, 0.66, 0.13))):
            add_cone(f"Bristle_{idx}", wr * 0.75, 0.015, 0.22, (0.0, wy, wz), rot_deg=(28.0, 0.0, 0.0), verts=8, mat=m_grey)

        # Elongated pointed snout + fangs + crimson eyes + torn rounded rat ears
        add_sphere("RatHead", 0.26, (0.0, -0.34, 0.46), scale=(0.86, 1.18, 0.76), rot_deg=(12.0, 0.0, 0.0), mat=m_fur)
        add_cone("RatSnout", 0.18, 0.055, 0.40, (0.0, -0.62, 0.40), rot_deg=(-98.0, 0.0, 0.0), verts=14, mat=m_grey)
        add_sphere("RatNose", 0.055, (0.0, -0.82, 0.38), mat=m_tail)
        for side, sx in (("L", -0.10), ("R", 0.10)):
            add_sphere(f"RatEye_{side}", 0.045, (sx * 1.1, -0.48, 0.52), mat=m_eye)
            add_cone(f"RatFang_{side}", 0.025, 0.005, 0.10, (sx * 0.65, -0.70, 0.31), rot_deg=(168.0, 0.0, 0.0), verts=6, mat=m_fang)
            add_cyl(f"RatEar_{side}", 0.115, 0.04, (sx * 1.7, -0.24, 0.62), rot_deg=(65.0, 0.0, -28.0 if sx < 0 else 28.0), verts=14, mat=m_tail)

        # Sturdy hind haunches & clawed paws
        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"HindLeg_{side}", 0.24, (sx, 0.38, 0.24), scale=(0.70, 1.15, 0.85), mat=m_fur_dk)
            add_box(f"HindPaw_{side}", (0.16, 0.28, 0.09), (sx * 1.02, 0.28, 0.045), mat=m_tail, bevel=0.02)
            add_cyl(f"ForeLeg_{side}", 0.095, 0.32, (sx * 0.82, -0.30, 0.18), rot_deg=(16.0, 0.0, 0.0), verts=10, mat=m_grey)
            add_box(f"ForePaw_{side}", (0.14, 0.24, 0.08), (sx * 0.84, -0.40, 0.04), mat=m_tail, bevel=0.02)

        # Long curled scarred pink-grey tail sweeping cleanly behind-right (+X, +Y)
        add_tube_curve(
            "RatTail",
            [
                (0.0, 0.70, 0.26, 0.075),
                (0.28, 0.92, 0.20, 0.064),
                (0.58, 0.96, 0.28, 0.054),
                (0.76, 0.80, 0.44, 0.044),
                (0.64, 0.64, 0.58, 0.034),
            ],
            mat=m_tail,
        )

        setup_render_and_camera(ortho_scale=2.2, target=(0.06, 0.08, 0.42), yaw_deg=32.0)
        render_asset_passes("monster_giant_rat")

    # -----------------------------------------------------------------------
    # 12. monster_skeleton_warrior - yaw = +32.0 (facing West/Left)
    #     Thick bones (>= 0.075 radius) so bones survive PixelOE 128x128 downscale cleanly
    # -----------------------------------------------------------------------
    def build_monster_skeleton_warrior() -> None:
        clear_scene()
        m_bone = make_mat("AgedBone", "#E5D8BD")
        m_bone_hi = make_mat("BrightBone", "#F7F4EA")
        m_socket = make_mat("RecessedDark", "#232B42")
        m_helm = make_mat("RustedIron", "#606B80")
        m_cloth = make_mat("TatteredCrimson", "#992838")
        m_wood = make_mat("CrackedWood", "#825132")
        m_sword = make_mat("NotchedSteel", "#BAC6DA")

        hip_z = add_humanoid_legs(m_bone, m_bone, m_bone_hi, stance_x=0.21, crouch=0.08, shin_r=0.088, thigh_r=0.098, boot_size=(0.18, 0.28, 0.12))

        add_box("PelvisBone", (0.44, 0.26, 0.18), (0.0, 0.0, hip_z + 0.02), mat=m_bone, bevel=0.03)
        add_box("TatteredWaist", (0.38, 0.29, 0.34), (0.0, -0.02, hip_z - 0.12), mat=m_cloth, bevel=0.02)

        torso_z = hip_z + 0.38
        # Solid spine + recessed core + thick high-contrast bone ribs
        add_cyl("Spine", 0.09, 0.56, (0.0, 0.06, torso_z - 0.02), verts=12, mat=m_bone)
        add_box("RibCore", (0.36, 0.24, 0.36), (0.0, 0.0, torso_z + 0.02), mat=m_socket, bevel=0.03)
        add_box("Sternum", (0.10, 0.28, 0.34), (0.0, -0.02, torso_z + 0.04), mat=m_bone_hi, bevel=0.015)
        for rz in (-0.10, 0.02, 0.14):
            add_box(f"Rib_{rz}", (0.48, 0.30, 0.065), (0.0, -0.01, torso_z + rz), mat=m_bone_hi, bevel=0.02)
        add_box("Clavicle", (0.58, 0.18, 0.085), (0.0, -0.04, torso_z + 0.24), mat=m_bone_hi, bevel=0.02)

        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"ShoulderBone_{side}", 0.115, (sx, 0.0, torso_z + 0.22), mat=m_bone)

        # Skull + rusted dark iron pot helm
        head_z = torso_z + 0.44
        add_sphere("SkullCranium", 0.17, (0.0, -0.02, head_z + 0.02), scale=(0.98, 1.06, 1.0), mat=m_bone_hi)
        add_box("SkullJaw", (0.20, 0.20, 0.12), (0.0, -0.09, head_z - 0.11), mat=m_bone, bevel=0.02)
        for side, sx in (("L", -0.065), ("R", 0.065)):
            add_sphere(f"EyeSocket_{side}", 0.048, (sx, -0.17, head_z - 0.01), mat=m_socket)
        add_cyl("PotHelm", 0.19, 0.20, (0.0, 0.0, head_z + 0.11), rot_deg=(0.0, -8.0, 0.0), verts=16, mat=m_helm)
        add_cyl("PotHelmBrim", 0.22, 0.05, (0.0, 0.0, head_z + 0.04), rot_deg=(0.0, -8.0, 0.0), verts=16, mat=m_helm)

        # Thick bone arms (radius >= 0.08) + shortsword (-X) + round shield (+X)
        add_cyl("Arm_L", 0.085, 0.44, (-0.38, -0.14, torso_z + 0.02), rot_deg=(40.0, -22.0, 0.0), verts=10, mat=m_bone_hi)
        add_broadsword("SkelSword", (-0.48, -0.32, torso_z + 0.04), (26.0, -20.0, 0.0), m_sword, m_helm, m_helm, blade_len=0.72, blade_w=0.095, grip_len=0.20)

        add_cyl("Arm_R", 0.085, 0.42, (0.38, -0.14, torso_z - 0.02), rot_deg=(26.0, 18.0, 0.0), verts=10, mat=m_bone_hi)
        add_round_shield("SkelShield", (0.46, -0.28, torso_z - 0.04), (-8.0, 0.0, -26.0), 0.32, m_wood, m_helm)

        setup_render_and_camera(ortho_scale=2.6, target=(-0.04, -0.04, 1.00), yaw_deg=32.0)
        render_asset_passes("monster_skeleton_warrior")

    # -----------------------------------------------------------------------
    # 13. monster_bandit_cutthroat - yaw = +32.0 (facing West/Left)
    # -----------------------------------------------------------------------
    def build_monster_bandit_cutthroat() -> None:
        clear_scene()
        m_charcoal = make_mat("CharcoalCloak", "#3B3837")
        m_leather = make_mat("BanditLeather", "#633C24")
        m_bandana = make_mat("CrimsonBandana", "#C23846")
        m_cutlass = make_mat("CutlassSteel", "#D8E2F0")
        m_steel = make_mat("BuckleSteel", "#9AA7BD")
        m_skin = make_mat("WarmSkin", "#EDB89A")

        hip_z = add_humanoid_legs(m_leather, m_leather, m_charcoal, stance_x=0.22, crouch=0.22, shin_r=0.12, thigh_r=0.14)

        add_box("Pelvis", (0.46, 0.30, 0.22), (0.0, 0.0, hip_z + 0.02), mat=m_charcoal, bevel=0.025)
        add_box("Belt", (0.50, 0.34, 0.09), (0.0, 0.0, hip_z + 0.13), mat=m_leather, bevel=0.015)
        add_box("BeltBuckle", (0.12, 0.06, 0.10), (0.0, -0.17, hip_z + 0.13), mat=m_steel, bevel=0.01)

        torso_z = hip_z + 0.38
        add_box("Torso", (0.50, 0.34, 0.46), (0.0, -0.02, torso_z), mat=m_leather, bevel=0.04)
        add_box("CloakChest", (0.54, 0.36, 0.22), (0.0, 0.0, torso_z + 0.12), mat=m_charcoal, bevel=0.03)
        # Asymmetric shoulder cape draped over right/back side
        add_box("AsymCape", (0.46, 0.12, 0.98), (0.14, 0.24, 0.92), rot_deg=(-18.0, 12.0, -14.0), mat=m_charcoal, bevel=0.03)
        add_sphere("Shoulder_R", 0.17, (0.34, 0.0, torso_z + 0.18), scale=(1.05, 0.95, 0.85), mat=m_leather)
        add_sphere("Shoulder_L", 0.16, (-0.34, -0.02, torso_z + 0.18), scale=(1.0, 0.95, 0.85), mat=m_charcoal)

        # Wide hood/brim + crimson face bandana
        head_z = torso_z + 0.40
        add_sphere("Head", 0.155, (0.0, -0.04, head_z), mat=m_skin)
        add_cyl("BandanaMask", 0.168, 0.15, (0.0, -0.05, head_z - 0.05), verts=14, mat=m_bandana)
        add_sphere("BanditHood", 0.19, (0.0, 0.0, head_z + 0.06), scale=(1.08, 1.15, 0.96), mat=m_charcoal)
        add_cyl("WideBrim", 0.30, 0.045, (0.0, -0.02, head_z + 0.11), rot_deg=(-6.0, 0.0, 0.0), verts=16, mat=m_leather)

        # Left hand (-X, forward-left): curved steel cutlass/sabre
        add_cyl("Arm_L", 0.095, 0.42, (-0.38, -0.16, torso_z), rot_deg=(44.0, -22.0, 0.0), verts=12, mat=m_charcoal)
        add_curved_blade("Cutlass", (-0.48, -0.34, torso_z + 0.04), (30.0, -22.0, 0.0), m_cutlass, m_leather, m_steel, length=0.78, width=0.095, curve_x=-0.14)

        # Right hand (+X): belt dagger ready
        add_cyl("Arm_R", 0.095, 0.40, (0.36, -0.14, torso_z - 0.04), rot_deg=(28.0, 18.0, 0.0), verts=12, mat=m_leather)
        add_curved_blade("BanditDagger", (0.42, -0.28, torso_z - 0.18), (55.0, 14.0, 0.0), m_cutlass, m_leather, m_steel, length=0.38, width=0.06, curve_x=0.06)

        setup_render_and_camera(ortho_scale=2.6, target=(-0.05, -0.04, 1.00), yaw_deg=32.0)
        render_asset_passes("monster_bandit_cutthroat")

    builders = {
        "hero_knight": build_hero_knight,
        "hero_cleric": build_hero_cleric,
        "hero_ranger": build_hero_ranger,
        "hero_mage": build_hero_mage,
        "hero_paladin": build_hero_paladin,
        "hero_rogue": build_hero_rogue,
        "monster_giant_frog": build_monster_giant_frog,
        "monster_goblin_raider": build_monster_goblin_raider,
        "monster_goblin_archer": build_monster_goblin_archer,
        "monster_orc_warrior": build_monster_orc_warrior,
        "monster_giant_rat": build_monster_giant_rat,
        "monster_skeleton_warrior": build_monster_skeleton_warrior,
        "monster_bandit_cutthroat": build_monster_bandit_cutthroat,
    }

    want = set(selected_units) if selected_units else set(builders.keys())
    for spec in UNIT_SPECS:
        uid = str(spec["id"])
        if uid in want:
            builders[uid]()


# ---------------------------------------------------------------------------
# Outer Python post-processing: _depth.png, _canny.png, _chroma_init.png, preview
# ---------------------------------------------------------------------------

def _convert_raw_to_guides(stem: str, raw_dir: Path, out_dir: Path) -> Dict[str, object]:
    import cv2
    import numpy as np
    from PIL import Image

    rgba = np.array(Image.open(raw_dir / f"{stem}_rgba.png").convert("RGBA"))
    z_raw = np.load(raw_dir / f"{stem}_depth.npy")

    # Strict alpha >= 128 mask so zero semi-transparent purple halo exists
    alpha = (rgba[..., 3] >= 128) & (z_raw < 1e5)
    if not np.any(alpha):
        raise RuntimeError(f"Blender rendered empty foreground for {stem}")

    # 1. Normalized ControlNet Depth Map (nearest = 255 white, farthest fg = 64, bg = 0 black)
    z_fg = z_raw[alpha]
    z_min = float(np.percentile(z_fg, 0.5))
    z_max = float(np.percentile(z_fg, 99.5))
    span = max(1e-4, z_max - z_min)
    depth_norm = np.clip((z_max - z_raw) / span, 0.0, 1.0)
    depth_u8 = np.zeros_like(z_raw, dtype=np.uint8)
    depth_u8[alpha] = np.round(64.0 + depth_norm[alpha] * 191.0).astype(np.uint8)

    depth_path = out_dir / f"{stem}_depth.png"
    Image.fromarray(depth_u8, mode="L").save(depth_path, format="PNG")

    # 2. Crisp ControlNet Canny / Edge Map (white lines on pure black background)
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

    # 3. Solid #FF00FF Chroma Init Guide (light interior-only smoothing sigma=1.1, pure #FF00FF outside)
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

    return {
        "depth": str(depth_path),
        "canny": str(canny_path),
        "chroma_init": str(chroma_path),
        "fg_bbox": bbox,
        "fg_width_px": int(bbox[2] - bbox[0] + 1),
        "fg_height_px": int(bbox[3] - bbox[1] + 1),
    }


def _build_guides_preview(out_dir: Path, specs: Sequence[Dict[str, object]]) -> Path:
    from PIL import Image, ImageDraw, ImageFont

    heroes = [s for s in specs if s["faction"] == "hero"]
    monsters = [s for s in specs if s["faction"] == "monster"]

    cols = 4
    thumb = 320
    pad = 18
    label_h = 46
    sec_h = 36
    header_h = 54

    hero_rows = math.ceil(len(heroes) / cols)
    mon_rows = math.ceil(len(monsters) / cols)
    cell_h = thumb + label_h

    sheet_w = pad + cols * (thumb + pad)
    sheet_h = (
        header_h
        + sec_h + hero_rows * (cell_h + pad)
        + 10
        + sec_h + mon_rows * (cell_h + pad)
        + pad
    )

    sheet = Image.new("RGB", (sheet_w, sheet_h), (20, 24, 36))
    draw = ImageDraw.Draw(sheet)

    try:
        font_title = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 22)
        font_sec = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 17)
        font_lbl = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 14)
        font_sm = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 11)
    except Exception:
        font_title = font_sec = font_lbl = font_sm = ImageFont.load_default()

    draw.text(
        (pad, 14),
        "REALMZ COMBAT ROSTER BLENDER GUIDES (13 Units | 1024x1024 | 26 deg Ortho | #FF00FF Chroma)",
        fill=(244, 198, 92),
        font=font_title,
    )

    def draw_group(title: str, items: Sequence[Dict[str, object]], y_start: int) -> int:
        draw.text((pad, y_start + 6), title, fill=(140, 210, 250), font=font_sec)
        y_grid = y_start + sec_h
        for idx, spec in enumerate(items):
            r = idx // cols
            c = idx % cols
            x0 = pad + c * (thumb + pad)
            y0 = y_grid + r * (cell_h + pad)

            chroma_p = out_dir / f"{spec['id']}_chroma_init.png"
            im = Image.open(chroma_p).convert("RGB").resize((thumb, thumb), Image.Resampling.LANCZOS)
            sheet.paste(im, (x0, y0))
            draw.rectangle([x0, y0, x0 + thumb - 1, y0 + thumb - 1], outline=(78, 92, 128), width=2)

            draw.rectangle([x0, y0 + thumb, x0 + thumb - 1, y0 + cell_h - 1], fill=(30, 36, 54))
            draw.text((x0 + 8, y0 + thumb + 5), f"{spec['id']}", fill=(242, 244, 248), font=font_lbl)
            meta_line = (
                f"{spec['label']} | yaw={spec['yaw_deg']:+.0f} "
                f"| s={spec['ortho_scale']} | h={spec['target_height_px']}px"
            )
            draw.text((x0 + 8, y0 + thumb + 25), meta_line, fill=(186, 198, 218), font=font_sm)

        rows = math.ceil(len(items) / cols)
        return y_grid + rows * (cell_h + pad)

    y_next = draw_group("HEROES (East/Right Facing, yaw_deg = -32)", heroes, header_h)
    draw_group("MONSTERS (West/Left Facing, yaw_deg = +32)", monsters, y_next + 10)

    preview_path = out_dir / "guides_preview.png"
    sheet.save(preview_path, format="PNG")
    return preview_path


def generate_combat_roster_guides(
    blender_exe: Path = DEFAULT_BLENDER_EXE,
    out_dir: Path = DEFAULT_OUT_DIR,
    only_units: Sequence[str] | None = None,
) -> dict:
    if not blender_exe.exists():
        raise FileNotFoundError(f"Blender executable not found: {blender_exe}")

    t0 = time.perf_counter()
    out_dir.mkdir(parents=True, exist_ok=True)
    want = set(only_units) if only_units else {str(s["id"]) for s in UNIT_SPECS}

    results: Dict[str, Dict[str, object]] = {}
    with tempfile.TemporaryDirectory(prefix="realmz_combat_guides_") as td:
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

        t_blend0 = time.perf_counter()
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=240,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        t_blend = time.perf_counter() - t_blend0
        if proc.returncode != 0:
            raise RuntimeError(f"Blender failed (rc={proc.returncode}):\n{proc.stdout}\n{proc.stderr}")

        t_post0 = time.perf_counter()
        for spec in UNIT_SPECS:
            uid = str(spec["id"])
            if uid not in want:
                continue
            paths = _convert_raw_to_guides(uid, raw_dir, out_dir)
            results[uid] = {
                **spec,
                "camera_pitch_deg": CAMERA_PITCH_DEG,
                "render_res": RENDER_RES,
                "px_per_meter": 64,
                **paths,
            }
        t_post = time.perf_counter() - t_post0

    preview_path = _build_guides_preview(out_dir, UNIT_SPECS)

    metadata = {
        "out_dir": str(out_dir),
        "preview": str(preview_path),
        "count": len(results),
        "camera_pitch_deg": CAMERA_PITCH_DEG,
        "render_res": RENDER_RES,
        "px_per_meter": 64,
        "timings_s": {
            "blender_render": round(t_blend, 2),
            "postprocess": round(t_post, 2),
            "total": round(time.perf_counter() - t0, 2),
        },
        "units": results,
    }
    meta_path = out_dir / "guides_metadata.json"
    meta_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    metadata["metadata_path"] = str(meta_path)
    return metadata


def main() -> None:
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = argv[1:]

    parser = argparse.ArgumentParser(description="Realmz 13-Unit Combat Roster Blender Guide Builder")
    parser.add_argument("--blender-inner", action="store_true", help="Internal flag when running inside Blender")
    parser.add_argument("--raw-dir", type=Path, default=None)
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR)
    parser.add_argument("--blender-exe", type=Path, default=DEFAULT_BLENDER_EXE)
    parser.add_argument("--only", default=None, help="Comma-separated unit IDs to render")
    args = parser.parse_args(argv)

    only_list = [u.strip() for u in args.only.split(",") if u.strip()] if args.only else None

    if args.blender_inner:
        if args.raw_dir is None:
            raise ValueError("--raw-dir is required with --blender-inner")
        args.raw_dir.mkdir(parents=True, exist_ok=True)
        _blender_main(args.raw_dir, selected_units=only_list)
    else:
        summary = generate_combat_roster_guides(
            blender_exe=args.blender_exe,
            out_dir=args.out_dir,
            only_units=only_list,
        )
        print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
