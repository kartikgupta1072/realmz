"""
Headless Blender 4.5.9 Orthographic (26 deg pitch) Whitebox Guide Generator.

Produces crisp 1024x1024 ControlNet Depth (_depth.png) and Canny/Edge (_canny.png)
maps inside C:/Users/karti/realmz/.scratch/realmz_soc_vertical_slice/whitebox_guides/ for:
  - bywater_inn (5:4 L-shaped medieval tavern/inn w/ timber beams, dormer, chimney, awning, barrels)
  - stone_bridge (arched medieval stone footbridge with parapets and voussoir arch)
  - stone_fence_h & stone_fence_v (low mossy fieldstone wall with wooden gatepost)
  - knight (f0..f3 idle breathing/stance poses)
  - giant_frog (f0..f3 idle breathing/throat-sac poses)

Usage (via asset-foundry venv Python):
  C:/Users/karti/realmz/tools/asset-foundry/.venv/Scripts/python.exe blender_whitebox_guides.py
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Dict, List, Tuple

CREATE_NO_WINDOW = 0x08000000
DEFAULT_BLENDER_EXE = Path(r"C:\Users\karti\realmz\tools\blender\blender-4.5.9-windows-x64\blender.exe")
DEFAULT_OUT_DIR = Path(r"C:\Users\karti\realmz\.scratch\realmz_soc_vertical_slice\whitebox_guides")
RENDER_RES = 1024
CAMERA_PITCH_DEG = 26.0


# ---------------------------------------------------------------------------
# Blender-side scene builders (executed inside blender.exe -b)
# ---------------------------------------------------------------------------

def _blender_main(raw_dir: Path) -> None:
    import bmesh
    import bpy
    from mathutils import Euler, Vector

    def clear_scene() -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def make_mat(name: str, color: Tuple[float, float, float]) -> "bpy.types.Material":
        mat = bpy.data.materials.new(name=name)
        mat.diffuse_color = (color[0], color[1], color[2], 1.0)
        return mat

    def add_bevel(obj: "bpy.types.Object", width: float = 0.025, segments: int = 2) -> None:
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = width
        mod.segments = segments
        mod.limit_method = "ANGLE"

    def add_box(
        name: str,
        size: Tuple[float, float, float],
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        mat=None,
        bevel: float = 0.02,
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
        return obj

    def add_cyl(
        name: str,
        radius: float,
        depth: float,
        loc: Tuple[float, float, float],
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        verts: int = 24,
        mat=None,
        smooth: bool = True,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
        obj = bpy.context.active_object
        obj.name = name
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if smooth:
            for poly in obj.data.polygons:
                poly.use_smooth = True
        if mat:
            obj.data.materials.append(mat)
        return obj

    def add_sphere(
        name: str,
        radius: float,
        loc: Tuple[float, float, float],
        scale: Tuple[float, float, float] = (1.0, 1.0, 1.0),
        rot_deg: Tuple[float, float, float] = (0.0, 0.0, 0.0),
        segs: int = 24,
        mat=None,
        smooth: bool = True,
    ) -> "bpy.types.Object":
        bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=16, radius=radius, location=loc)
        obj = bpy.context.active_object
        obj.name = name
        obj.scale = scale
        obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        if smooth:
            for poly in obj.data.polygons:
                poly.use_smooth = True
        if mat:
            obj.data.materials.append(mat)
        return obj

    def add_pitched_roof(
        name: str,
        span_x: float,
        span_y: float,
        ridge_h: float,
        loc: Tuple[float, float, float],
        axis: str = "X",
        overhang: float = 0.22,
        thickness: float = 0.14,
        mat=None,
    ) -> "bpy.types.Object":
        """Create a crisp pitched gable prism with thick overhanging eaves."""
        mesh = bpy.data.meshes.new(name + "_mesh")
        bm = bmesh.new()
        if axis == "X":
            hx = span_x * 0.5 + overhang
            hy = span_y * 0.5 + overhang
            # Gable runs along X; slope along Y
            verts = [
                bm.verts.new((-hx, -hy, 0.0)),
                bm.verts.new((hx, -hy, 0.0)),
                bm.verts.new((hx, 0.0, ridge_h)),
                bm.verts.new((-hx, 0.0, ridge_h)),
                bm.verts.new((-hx, hy, 0.0)),
                bm.verts.new((hx, hy, 0.0)),
            ]
        else:
            hx = span_x * 0.5 + overhang
            hy = span_y * 0.5 + overhang
            # Gable runs along Y; slope along X
            verts = [
                bm.verts.new((-hx, -hy, 0.0)),
                bm.verts.new((-hx, hy, 0.0)),
                bm.verts.new((0.0, hy, ridge_h)),
                bm.verts.new((0.0, -hy, ridge_h)),
                bm.verts.new((hx, -hy, 0.0)),
                bm.verts.new((hx, hy, 0.0)),
            ]
        bm.faces.new((verts[0], verts[1], verts[2], verts[3]))
        bm.faces.new((verts[3], verts[2], verts[5], verts[4]))
        bm.faces.new((verts[0], verts[3], verts[4]))
        bm.faces.new((verts[1], verts[5], verts[2]))
        bm.faces.new((verts[0], verts[4], verts[5], verts[1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = loc
        if mat:
            obj.data.materials.append(mat)
        # Add solidify-like ridge beam lip via bevel
        add_bevel(obj, thickness * 0.15, 1)
        return obj

    def setup_render_and_camera(
        ortho_scale: float,
        target: Tuple[float, float, float],
        yaw_deg: float = 0.0,
    ) -> None:
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.render.resolution_x = RENDER_RES
        scene.render.resolution_y = RENDER_RES
        scene.render.resolution_percentage = 100
        scene.render.film_transparent = True
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"

        # Workbench Studio lighting with cavity highlights for sharp architectural/unit edges
        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "MATERIAL"
        scene.display.shading.show_cavity = True
        scene.display.shading.cavity_type = "BOTH"
        scene.display.shading.cavity_ridge_factor = 1.6
        scene.display.shading.cavity_valley_factor = 1.8
        scene.display.shading.show_object_outline = False

        vl = scene.view_layers[0]
        vl.use_pass_z = True

        # Camera at 26 deg elevation pitch looking at target
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
        # Base vector from target to camera before yaw: (0, -dist*cos(pitch), dist*sin(pitch))
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

        # Convert EXR depth to compact float32 .npy inside Blender so outer Python needs zero EXR libs
        exr_path = raw_dir / f"{stem}_depth_0001.exr"
        img = bpy.data.images.load(str(exr_path), check_existing=False)
        w, h = img.size[0], img.size[1]
        import numpy as np
        arr = np.array(img.pixels[:], dtype=np.float32).reshape((h, w, 4))
        arr = np.flipud(arr)
        np.save(str(raw_dir / f"{stem}_depth.npy"), arr[..., 0])
        bpy.data.images.remove(img)
        exr_path.unlink(missing_ok=True)

    # -----------------------------------------------------------------------
    # Asset 1: bywater_inn (5:4 L-shaped medieval tavern/inn)
    # -----------------------------------------------------------------------
    def build_bywater_inn() -> None:
        clear_scene()
        m_stone = make_mat("Stone", (0.56, 0.54, 0.51))
        m_stucco = make_mat("Stucco", (0.88, 0.84, 0.75))
        m_timber = make_mat("Timber", (0.38, 0.24, 0.14))
        m_roof = make_mat("Roof", (0.58, 0.30, 0.19))
        m_iron = make_mat("Iron", (0.24, 0.26, 0.32))

        # Stepped stone foundation plinth
        add_box("Plinth_Main", (3.30, 2.30, 0.42), (-0.75, 0.20, 0.21), mat=m_stone)
        add_box("Plinth_Wing", (2.05, 3.40, 0.42), (1.35, -0.35, 0.21), mat=m_stone)

        # Main hall body + L-wing body (upper story slightly overhanging timber jetty)
        add_box("Hall_Lower", (3.10, 2.10, 1.35), (-0.75, 0.20, 1.09), mat=m_stone)
        add_box("Hall_Upper", (3.25, 2.25, 1.15), (-0.75, 0.20, 2.34), mat=m_stucco)
        add_box("Wing_Lower", (1.90, 3.20, 1.35), (1.35, -0.35, 1.09), mat=m_stone)
        add_box("Wing_Upper", (2.04, 3.35, 1.15), (1.35, -0.35, 2.34), mat=m_stucco)

        # Pitched roofs (Main wing runs X, Tavern wing runs Y)
        add_pitched_roof("Roof_Hall", 3.35, 2.35, 1.45, (-0.75, 0.20, 2.91), axis="X", overhang=0.25, mat=m_roof)
        add_pitched_roof("Roof_Wing", 2.12, 3.45, 1.40, (1.35, -0.35, 2.91), axis="Y", overhang=0.24, mat=m_roof)

        # Front dormer on main hall roof
        add_box("Dormer_Box", (0.85, 0.90, 0.72), (-1.05, -0.68, 3.42), mat=m_stucco)
        add_pitched_roof("Dormer_Roof", 0.95, 1.00, 0.55, (-1.05, -0.68, 3.78), axis="Y", overhang=0.14, mat=m_roof)
        add_box("Dormer_Win", (0.44, 0.08, 0.48), (-1.05, -1.14, 3.45), mat=m_timber)

        # Tall stepped stone chimney near wing junction
        add_box("Chimney_Base", (0.72, 0.72, 2.20), (0.35, 0.85, 3.55), mat=m_stone)
        add_box("Chimney_Top", (0.58, 0.58, 0.80), (0.35, 0.85, 4.85), mat=m_stone)
        add_box("Chimney_Cap", (0.76, 0.76, 0.14), (0.35, 0.85, 5.28), mat=m_stone)

        # Timber framing beams on front facade
        for bx in (-2.30, -1.30, -0.30, 0.40, 2.32):
            add_box(f"Post_{bx:.2f}", (0.14, 0.14, 1.20), (bx, -0.92, 2.34), mat=m_timber)
        add_box("Jetty_Beam_H", (3.30, 0.16, 0.16), (-0.75, -0.90, 1.76), mat=m_timber)
        add_box("Jetty_Beam_W", (2.10, 0.16, 0.16), (1.35, -2.02, 1.76), mat=m_timber)
        for bx in (0.38, 1.35, 2.32):
            add_box(f"WingPost_{bx:.2f}", (0.14, 0.14, 1.20), (bx, -2.02, 2.34), mat=m_timber)

        # Front arched doorway + 2 stone steps + sloped porch awning
        add_box("Door_Step1", (1.25, 0.55, 0.16), (-0.85, -1.12, 0.08), mat=m_stone)
        add_box("Door_Step2", (1.05, 0.36, 0.16), (-0.85, -1.04, 0.24), mat=m_stone)
        add_box("Door_Frame", (0.96, 0.24, 1.15), (-0.85, -0.88, 0.92), mat=m_timber)
        add_cyl("Door_Arch", 0.48, 0.24, (-0.85, -0.88, 1.48), rot_deg=(90.0, 0.0, 0.0), verts=16, mat=m_stone)
        add_box("Door_Panel", (0.68, 0.12, 1.05), (-0.85, -0.82, 0.90), mat=m_timber)

        # Porch awning + 2 timber porch posts
        add_box("Porch_Awning", (1.50, 0.95, 0.12), (-0.85, -1.32, 1.88), rot_deg=(24.0, 0.0, 0.0), mat=m_roof)
        add_box("Porch_Post_L", (0.12, 0.12, 1.62), (-1.48, -1.68, 0.96), mat=m_timber)
        add_box("Porch_Post_R", (0.12, 0.12, 1.62), (-0.22, -1.68, 0.96), mat=m_timber)

        # Wing front bay window + hanging inn sign
        add_box("Wing_Win", (0.76, 0.14, 0.72), (1.35, -2.02, 1.08), mat=m_timber)
        add_box("Wing_Upper_Win", (0.72, 0.14, 0.64), (1.35, -2.02, 2.38), mat=m_timber)
        add_box("Sign_Arm", (0.10, 0.68, 0.10), (0.35, -2.28, 1.72), mat=m_iron)
        add_box("Sign_Board", (0.08, 0.48, 0.40), (0.35, -2.42, 1.42), mat=m_timber)

        # Corner wooden barrels at front-left
        for idx, (bx, by, bz) in enumerate(((-2.05, -1.25, 0.34), (-1.62, -1.35, 0.34), (-1.85, -1.32, 0.96))):
            add_cyl(f"Barrel_{idx}", 0.24, 0.64, (bx, by, bz), verts=14, mat=m_timber)
            add_cyl(f"BarrelHoop_{idx}", 0.255, 0.08, (bx, by, bz + 0.14), verts=14, mat=m_iron)

        # Frame cleanly for 5:4 aspect inside 1024x1024 with generous chimney headroom
        setup_render_and_camera(ortho_scale=6.9, target=(0.0, -0.35, 2.40), yaw_deg=0.0)
        render_asset_passes("bywater_inn")

    # -----------------------------------------------------------------------
    # Asset 2: stone_bridge (arched medieval stone footbridge)
    # -----------------------------------------------------------------------
    def build_stone_bridge() -> None:
        clear_scene()
        m_stone = make_mat("BridgeStone", (0.60, 0.58, 0.54))
        m_dark = make_mat("BridgeDark", (0.42, 0.41, 0.40))
        m_moss = make_mat("BridgeMoss", (0.35, 0.48, 0.32))

        # Construct genuine arched bridge deck via bmesh profile extruded along Y
        mesh = bpy.data.meshes.new("BridgeArch_mesh")
        bm = bmesh.new()
        n_seg = 16
        half_span = 2.4
        arch_r = 1.05
        half_w = 0.88

        # Build front/back profile vertices around arch
        top_pts = []
        bot_pts = []
        for i in range(n_seg + 1):
            t = i / n_seg
            x = -half_span + t * (2.0 * half_span)
            # Graceful cambered bridge deck
            z_top = 0.45 + 0.72 * math.sin(math.pi * t)
            # Semicircular arch cutout in center |x| <= arch_r
            if abs(x) <= arch_r:
                z_bot = math.sqrt(max(0.0, arch_r * arch_r - x * x)) * 0.92
            else:
                z_bot = 0.0
            top_pts.append((x, z_top))
            bot_pts.append((x, z_bot))

        f_top = [bm.verts.new((x, -half_w, z)) for x, z in top_pts]
        f_bot = [bm.verts.new((x, -half_w, z)) for x, z in bot_pts]
        b_top = [bm.verts.new((x, half_w, z)) for x, z in top_pts]
        b_bot = [bm.verts.new((x, half_w, z)) for x, z in bot_pts]

        for i in range(n_seg):
            bm.faces.new((f_bot[i], f_bot[i + 1], f_top[i + 1], f_top[i]))
            bm.faces.new((b_top[i], b_top[i + 1], b_bot[i + 1], b_bot[i]))
            bm.faces.new((f_top[i], f_top[i + 1], b_top[i + 1], b_top[i]))
            bm.faces.new((b_bot[i], b_bot[i + 1], f_bot[i + 1], f_bot[i]))
        bm.faces.new((b_top[0], b_bot[0], f_bot[0], f_top[0]))
        bm.faces.new((f_top[-1], f_bot[-1], b_bot[-1], b_top[-1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()

        arch_obj = bpy.data.objects.new("BridgeDeck", mesh)
        bpy.context.collection.objects.link(arch_obj)
        arch_obj.data.materials.append(m_stone)
        add_bevel(arch_obj, 0.02, 1)

        # Voussoir arch ring stones along front & back arch edge
        for i in range(11):
            ang = math.pi * (i + 0.5) / 11.0
            vx = -arch_r * math.cos(ang)
            vz = arch_r * 0.92 * math.sin(ang) + 0.06
            tilt = math.degrees(ang - math.pi * 0.5)
            add_box(f"Voussoir_F_{i}", (0.24, 0.18, 0.24), (vx, -half_w - 0.02, vz), rot_deg=(0.0, tilt, 0.0), mat=m_dark, bevel=0.015)

        # Low stone parapets + capstones following cambered arch
        for i in range(12):
            t = (i + 0.5) / 12.0
            px = -2.2 + t * 4.4
            pz = 0.45 + 0.72 * math.sin(math.pi * t) + 0.28
            slope = math.degrees(math.atan2(0.72 * math.pi * math.cos(math.pi * t) / 4.8, 1.0))
            add_box(f"ParapetF_{i}", (0.36, 0.22, 0.52), (px, -half_w + 0.11, pz), rot_deg=(0.0, -slope, 0.0), mat=m_stone, bevel=0.018)
            add_box(f"ParapetB_{i}", (0.36, 0.22, 0.52), (px, half_w - 0.11, pz), rot_deg=(0.0, -slope, 0.0), mat=m_stone, bevel=0.018)

        # 4 Corner stone bollards + mossy base abutments
        for sx in (-2.35, 2.35):
            for sy in (-half_w + 0.08, half_w - 0.08):
                add_box(f"Bollard_{sx}_{sy}", (0.36, 0.36, 0.95), (sx, sy, 0.48), mat=m_dark, bevel=0.025)
                add_box(f"BollardCap_{sx}_{sy}", (0.42, 0.42, 0.14), (sx, sy, 0.98), mat=m_moss, bevel=0.02)

        setup_render_and_camera(ortho_scale=5.8, target=(0.0, 0.0, 0.65), yaw_deg=0.0)
        render_asset_passes("stone_bridge")

    # -----------------------------------------------------------------------
    # Assets 3 & 4: stone_fence_h and stone_fence_v
    # -----------------------------------------------------------------------
    def build_stone_fence(orientation: str) -> None:
        clear_scene()
        m_stone = make_mat("WallStone", (0.58, 0.56, 0.52))
        m_dark = make_mat("WallDark", (0.44, 0.43, 0.40))
        m_wood = make_mat("GatePost", (0.42, 0.28, 0.16))
        m_moss = make_mat("MossCap", (0.34, 0.52, 0.30))

        # Build wall along X first, then rotate 90 deg around Z for vertical
        parent = bpy.data.objects.new("FenceRoot", None)
        bpy.context.collection.objects.link(parent)

        def attach(obj: "bpy.types.Object") -> None:
            obj.parent = parent

        # Base stone foundation
        attach(add_box("WallBase", (2.60, 0.56, 0.24), (-0.15, 0.0, 0.12), mat=m_dark, bevel=0.02))

        # Irregular fieldstone blocks (3 staggered courses)
        courses = [
            (0.32, [(-1.15, 0.48, 0.24), (-0.60, 0.54, 0.25), (-0.02, 0.50, 0.24), (0.54, 0.52, 0.25), (1.02, 0.36, 0.23)]),
            (0.56, [(-1.05, 0.58, 0.22), (-0.42, 0.56, 0.23), (0.18, 0.54, 0.22), (0.76, 0.56, 0.22)]),
            (0.76, [(-1.12, 0.50, 0.14), (-0.54, 0.58, 0.15), (0.06, 0.56, 0.14), (0.64, 0.56, 0.15), (1.06, 0.30, 0.14)]),
        ]
        for ci, (cz, blocks) in enumerate(courses):
            for bi, (bx, bw, bh) in enumerate(blocks):
                mat_b = m_moss if (ci == 2 and (bi % 2 == 0)) else m_stone
                attach(add_box(f"Block_{ci}_{bi}", (bw * 0.94, 0.50, bh), (bx, 0.0, cz), mat=mat_b, bevel=0.025))

        # Left stone end-pillar
        attach(add_box("EndPillar", (0.48, 0.62, 1.02), (-1.48, 0.0, 0.51), mat=m_dark, bevel=0.025))
        attach(add_box("EndPillarCap", (0.56, 0.68, 0.14), (-1.48, 0.0, 1.06), mat=m_moss, bevel=0.02))

        # Right chamfered wooden gatepost + iron cap & hinge bar
        attach(add_box("GatePost", (0.34, 0.34, 1.38), (1.36, 0.0, 0.69), mat=m_wood, bevel=0.03))
        attach(add_box("GatePostCap", (0.40, 0.40, 0.12), (1.36, 0.0, 1.40), mat=m_dark, bevel=0.02))
        attach(add_box("GateBrace", (0.42, 0.12, 0.14), (1.18, -0.14, 0.85), mat=m_dark, bevel=0.01))

        if orientation == "V":
            parent.rotation_euler = (0.0, 0.0, math.radians(90.0))
            setup_render_and_camera(ortho_scale=4.2, target=(0.0, 0.0, 0.62), yaw_deg=0.0)
            render_asset_passes("stone_fence_v")
        else:
            setup_render_and_camera(ortho_scale=4.2, target=(0.0, 0.0, 0.62), yaw_deg=0.0)
            render_asset_passes("stone_fence_h")

    # -----------------------------------------------------------------------
    # Asset 5: knight (4 idle breathing/stance poses f0..f3)
    # -----------------------------------------------------------------------
    def build_knight_pose(frame_idx: int) -> None:
        clear_scene()
        m_steel = make_mat("Steel", (0.68, 0.72, 0.78))
        m_dark = make_mat("DarkSteel", (0.34, 0.38, 0.46))
        m_gold = make_mat("GoldTrim", (0.86, 0.66, 0.24))
        m_cape = make_mat("CrimsonCape", (0.72, 0.18, 0.22))
        m_plume = make_mat("Plume", (0.82, 0.24, 0.26))

        phase = 2.0 * math.pi * frame_idx / 4.0
        breath = math.sin(phase)        # 0, +1, 0, -1
        bob_z = 0.035 * max(0.0, breath)
        chest_scale = 1.0 + 0.04 * breath
        arm_lift = 0.045 * breath
        cape_sway = 5.0 * math.sin(phase + 0.5)

        # Grounded boots + armored greaves (planted firmly)
        for side, sx in (("L", -0.20), ("R", 0.20)):
            add_box(f"Boot_{side}", (0.18, 0.30, 0.14), (sx, -0.04, 0.07), rot_deg=(0.0, 0.0, -8.0 if side == "L" else 8.0), mat=m_dark)
            add_cyl(f"Shin_{side}", 0.10, 0.42, (sx, 0.0, 0.33), verts=12, mat=m_steel)
            add_cyl(f"Thigh_{side}", 0.115, 0.36, (sx * 0.92, 0.0, 0.68), verts=12, mat=m_dark)

        # Tassets + belt
        add_box("Pelvis", (0.44, 0.28, 0.20), (0.0, 0.0, 0.88 + bob_z * 0.4), mat=m_dark)
        add_box("Belt", (0.48, 0.32, 0.08), (0.0, 0.0, 0.98 + bob_z * 0.5), mat=m_gold)

        # Sculpted chest breastplate
        add_box(
            "Torso",
            (0.50 * chest_scale, 0.34 * chest_scale, 0.50),
            (0.0, 0.0, 1.24 + bob_z),
            mat=m_steel,
            bevel=0.04,
        )
        add_box("ChestCrest", (0.16, 0.06, 0.22), (0.0, -0.18, 1.28 + bob_z), mat=m_gold, bevel=0.01)

        # Layered shoulder pauldrons
        for side, sx in (("L", -0.34), ("R", 0.34)):
            add_sphere(f"Pauldron_{side}", 0.17, (sx, 0.0, 1.44 + bob_z + arm_lift * 0.5), scale=(1.0, 0.9, 0.85), mat=m_steel)

        # Crested great-helm + visor + sweeping crimson plume
        helm_z = 1.68 + bob_z * 1.15
        add_cyl("HelmBody", 0.165, 0.32, (0.0, 0.0, helm_z), verts=14, mat=m_steel)
        add_sphere("HelmDome", 0.165, (0.0, 0.0, helm_z + 0.14), scale=(1.0, 1.0, 0.72), mat=m_steel)
        add_box("HelmVisor", (0.22, 0.12, 0.10), (0.0, -0.14, helm_z + 0.02), mat=m_dark, bevel=0.01)
        add_box("HelmCrest", (0.07, 0.26, 0.16), (0.0, 0.05, helm_z + 0.30), rot_deg=(-12.0 + breath * 3.0, 0.0, 0.0), mat=m_plume, bevel=0.02)
        add_box("HelmPlumeTail", (0.08, 0.32, 0.14), (0.0, 0.24, helm_z + 0.22), rot_deg=(28.0 + cape_sway * 0.6, 0.0, 0.0), mat=m_plume, bevel=0.02)

        # Left arm + Kite Shield angled forward
        add_cyl("Arm_L", 0.085, 0.38, (-0.38, -0.08, 1.18 + bob_z + arm_lift), rot_deg=(28.0, 18.0, 0.0), verts=10, mat=m_dark)
        # Kite shield built via bmesh
        sh_mesh = bpy.data.meshes.new("KiteShield_mesh")
        bm = bmesh.new()
        pts = [(-0.24, 0.32), (0.24, 0.32), (0.26, 0.05), (0.0, -0.44), (-0.26, 0.05)]
        f_v = [bm.verts.new((x, -0.05, z)) for x, z in pts]
        b_v = [bm.verts.new((x, 0.05, z)) for x, z in pts]
        bm.faces.new(f_v)
        bm.faces.new(list(reversed(b_v)))
        for i in range(len(pts)):
            ni = (i + 1) % len(pts)
            bm.faces.new((f_v[i], f_v[ni], b_v[ni], b_v[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(sh_mesh)
        bm.free()
        shield = bpy.data.objects.new("KiteShield", sh_mesh)
        bpy.context.collection.objects.link(shield)
        shield.location = (-0.42, -0.24, 1.06 + bob_z + arm_lift * 1.1)
        shield.rotation_euler = (math.radians(-6.0), math.radians(4.0), math.radians(22.0))
        shield.data.materials.append(m_gold)
        add_bevel(shield, 0.015, 1)

        # Right arm + Raised Heroic Broadsword mounted on unified SwordRoot
        add_cyl("Arm_R", 0.085, 0.40, (0.40, -0.10, 1.22 + bob_z + arm_lift), rot_deg=(-36.0, -18.0, 0.0), verts=16, mat=m_dark)
        sw_z = 1.26 + bob_z + arm_lift * 1.3
        sword_root = bpy.data.objects.new("SwordRoot", None)
        bpy.context.collection.objects.link(sword_root)
        sword_root.location = (0.48, -0.28, sw_z)
        sword_root.rotation_euler = (math.radians(-18.0 + breath * 4.0), math.radians(-10.0), 0.0)

        grip = add_cyl("SwordGrip", 0.032, 0.24, (0.0, 0.0, 0.0), verts=12, mat=m_dark)
        pommel = add_sphere("SwordPommel", 0.048, (0.0, 0.0, -0.13), mat=m_gold)
        guard = add_box("SwordGuard", (0.28, 0.065, 0.06), (0.0, 0.0, 0.13), mat=m_gold, bevel=0.01)
        blade = add_box("SwordBlade", (0.095, 0.035, 0.90), (0.0, 0.0, 0.60), mat=m_steel, bevel=0.008)
        for part in (grip, pommel, guard, blade):
            part.parent = sword_root

        # Flowing crimson cape trailing behind shoulders
        add_box(
            "Cape",
            (0.58, 0.10, 1.18),
            (0.0, 0.28, 0.88 + bob_z * 0.5),
            rot_deg=(-16.0 + cape_sway, 0.0, 0.0),
            mat=m_cape,
            bevel=0.025,
        )

        setup_render_and_camera(ortho_scale=2.55, target=(0.0, 0.0, 1.02), yaw_deg=-24.0)
        render_asset_passes(f"knight_f{frame_idx}")

    # -----------------------------------------------------------------------
    # Asset 6: giant_frog (4 idle breathing/throat-sac poses f0..f3)
    # -----------------------------------------------------------------------
    def build_giant_frog_pose(frame_idx: int) -> None:
        clear_scene()
        m_skin = make_mat("FrogSkin", (0.36, 0.64, 0.30))
        m_belly = make_mat("FrogBelly", (0.78, 0.72, 0.44))
        m_sac = make_mat("ThroatSac", (0.88, 0.78, 0.48))
        m_eye = make_mat("FrogEye", (0.92, 0.64, 0.16))

        # Throat sac puffs strongly on f1, peaks on f2, deflates f3 -> f0
        sac_factors = [0.65, 0.98, 1.28, 0.88]
        bob_vals = [0.0, -0.025, 0.035, 0.01]
        sf = sac_factors[frame_idx]
        bz = bob_vals[frame_idx]

        # Chunky squat body torso
        add_sphere("FrogTorso", 0.52, (0.0, 0.10, 0.44 + bz), scale=(1.15, 1.28, 0.76), rot_deg=(-16.0, 0.0, 0.0), mat=m_skin)
        # Broad frog head + snout
        add_sphere("FrogHead", 0.42, (0.0, -0.34, 0.62 + bz * 1.1), scale=(1.18, 1.05, 0.64), rot_deg=(-8.0, 0.0, 0.0), mat=m_skin)
        # Wide lower jaw / mouth crease ridge
        add_box("JawRidge", (0.76, 0.34, 0.10), (0.0, -0.58, 0.50 + bz), rot_deg=(8.0, 0.0, 0.0), mat=m_belly, bevel=0.03)

        # Inflatable vocal throat sac beneath chin!
        add_sphere(
            "ThroatSac",
            0.28,
            (0.0, -0.48, 0.34 + bz * 0.5),
            scale=(1.10 * sf, 1.05 * sf, 0.88 * sf),
            mat=m_sac,
        )

        # Bulging dorsal eye ridges
        for side, sx in (("L", -0.24), ("R", 0.24)):
            add_sphere(f"EyeRidge_{side}", 0.15, (sx, -0.38, 0.86 + bz * 1.2), scale=(1.0, 1.0, 0.92), mat=m_skin)
            add_sphere(f"EyeOrb_{side}", 0.10, (sx * 1.02, -0.45, 0.88 + bz * 1.2), mat=m_eye)

        # Dorsal wart/spine bumps along back
        for idx, (wx, wy, wz, wr) in enumerate((
            (-0.18, 0.18, 0.76, 0.10), (0.20, 0.16, 0.75, 0.10),
            (-0.14, 0.42, 0.62, 0.09), (0.16, 0.44, 0.60, 0.09),
        )):
            add_sphere(f"Wart_{idx}", wr, (wx, wy, wz + bz), scale=(1.0, 1.1, 0.65), mat=m_skin)

        # Muscular folded hind haunches + webbed feet
        for side, sx in (("L", -0.56), ("R", 0.56)):
            add_sphere(f"HindThigh_{side}", 0.32, (sx, 0.28, 0.36 + bz * 0.3), scale=(0.76, 1.24, 0.82), rot_deg=(12.0, 0.0, -18.0 if side == "L" else 18.0), mat=m_skin)
            add_box(f"HindFoot_{side}", (0.32, 0.46, 0.10), (sx * 1.06, 0.14, 0.05), rot_deg=(0.0, 0.0, -24.0 if side == "L" else 24.0), mat=m_belly, bevel=0.03)

        # Stout front forelimbs planted forward
        for side, sx in (("L", -0.38), ("R", 0.38)):
            add_cyl(f"ForeArm_{side}", 0.11, 0.44, (sx, -0.38, 0.24), rot_deg=(18.0, 12.0 if side == "L" else -12.0, 0.0), verts=12, mat=m_skin)
            add_box(f"ForeFoot_{side}", (0.24, 0.28, 0.09), (sx * 1.08, -0.52, 0.045), rot_deg=(0.0, 0.0, -15.0 if side == "L" else 15.0), mat=m_belly, bevel=0.025)

        setup_render_and_camera(ortho_scale=2.35, target=(0.0, -0.05, 0.48), yaw_deg=-26.0)
        render_asset_passes(f"giant_frog_f{frame_idx}")

    build_bywater_inn()
    build_stone_bridge()
    build_stone_fence("H")
    build_stone_fence("V")
    for fi in range(4):
        build_knight_pose(fi)
    for fi in range(4):
        build_giant_frog_pose(fi)


# ---------------------------------------------------------------------------
# Outer Python post-processing: convert raw Blender passes -> _depth & _canny
# ---------------------------------------------------------------------------

def _convert_raw_to_guides(stem: str, raw_dir: Path, out_dir: Path) -> Dict[str, str]:
    import cv2
    import numpy as np
    from PIL import Image

    rgba = np.array(Image.open(raw_dir / f"{stem}_rgba.png").convert("RGBA"))
    z_raw = np.load(raw_dir / f"{stem}_depth.npy")

    alpha = (rgba[..., 3] > 64) & (z_raw < 1e5)
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
    # Combine: (a) outer silhouette contour, (b) depth discontinuities, (c) Workbench shaded material & cavity edges
    sil_u8 = np.where(alpha, 255, 0).astype(np.uint8)
    contours, _ = cv2.findContours(sil_u8, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    edge_sil = np.zeros_like(sil_u8)
    cv2.drawContours(edge_sil, contours, -1, 255, thickness=2)

    edge_depth = cv2.Canny(depth_u8, 28, 85)

    # Color + luminance Canny on Workbench Studio + Cavity material pass
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

    return {"depth": str(depth_path), "canny": str(canny_path)}


def generate_all_whitebox_guides(
    blender_exe: Path = DEFAULT_BLENDER_EXE,
    out_dir: Path = DEFAULT_OUT_DIR,
) -> dict:
    if not blender_exe.exists():
        raise FileNotFoundError(f"Blender executable not found: {blender_exe}")

    out_dir.mkdir(parents=True, exist_ok=True)
    stems: List[str] = [
        "bywater_inn",
        "stone_bridge",
        "stone_fence_h",
        "stone_fence_v",
        "knight_f0", "knight_f1", "knight_f2", "knight_f3",
        "giant_frog_f0", "giant_frog_f1", "giant_frog_f2", "giant_frog_f3",
    ]

    results = {}
    with tempfile.TemporaryDirectory(prefix="realmz_blender_guides_") as td:
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
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=120,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        if proc.returncode != 0:
            raise RuntimeError(f"Blender failed (rc={proc.returncode}):\n{proc.stdout}\n{proc.stderr}")

        for stem in stems:
            results[stem] = _convert_raw_to_guides(stem, raw_dir, out_dir)

    return {"out_dir": str(out_dir), "count": len(results), "assets": results}


def main() -> None:
    # Detect Blender "--" separator when invoked inside Blender
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = argv[1:]

    parser = argparse.ArgumentParser(description="Blender Headless Whitebox ControlNet Guide Builder")
    parser.add_argument("--blender-inner", action="store_true", help="Internal flag when running inside Blender")
    parser.add_argument("--raw-dir", type=Path, default=None)
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR)
    parser.add_argument("--blender-exe", type=Path, default=DEFAULT_BLENDER_EXE)
    args = parser.parse_args(argv)

    if args.blender_inner:
        if args.raw_dir is None:
            raise ValueError("--raw-dir is required with --blender-inner")
        args.raw_dir.mkdir(parents=True, exist_ok=True)
        _blender_main(args.raw_dir)
    else:
        summary = generate_all_whitebox_guides(blender_exe=args.blender_exe, out_dir=args.out_dir)
        print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
