"""
Realmz Asset Foundry V1 - Headless Blender 4.5.9 Normalization, Auto-Rigging,
Animation, and HD-2D Orthographic + View-Space Normal Renderer.

Uses ONLY Blender built-in Python modules (stdlib + bpy + bmesh + mathutils + numpy).
Zero external venv packages required inside Blender.

Modes:
  1. --mode normalize_rig
     Normalizes a raw .glb (floater removal, consistent normals, Y-up glTF grounding
     with min_y == 0.0, center_x == 0.0, center_z == 0.0, max extent == --scale,
     front facing +Z in glTF), optional triangle decimation, template or --joints-json
     armature rigging with bone-heat + Gaussian fallback skinning, 3 looping NLA clips
     (Idle, Move, Attack), and canonical .glb + meta.json export.

  2. --mode render_views
     Loads canonical .glb and renders:
       - --out-albedo: orthographic 26-deg HD-2D front view (+Z glTF / -Y Blender),
         transparent background (1 or 4 animation frames as horizontal strip).
       - --out-normal: exact camera/view-space OpenGL (+Y up) normal map with
         neutral front = ~(128, 128, 255) and #8080FF background.
       - --out-qa: 2x2 turntable contact sheet (1024x1024: Front, 45deg Hero, Side, Back)
         on dark-slate studio background with floor grid and axis cues.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector


# ---------------------------------------------------------------------------
# Argument Parsing (handles args after `--`)
# ---------------------------------------------------------------------------

def parse_args() -> argparse.Namespace:
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []

    parser = argparse.ArgumentParser(
        description="Realmz Asset Foundry - Blender 4.5.9 Rig & Render Tool"
    )
    parser.add_argument(
        "--mode",
        required=True,
        choices=("normalize_rig", "render_views"),
        help="Execution mode",
    )

    # Shared
    parser.add_argument("--input", required=True, help="Input .glb path")

    # normalize_rig options
    parser.add_argument("--output", help="Output canonical .glb path")
    parser.add_argument("--meta-out", help="Output metadata JSON path")
    parser.add_argument(
        "--category",
        default="Monster",
        choices=("Hero", "Monster", "Prop", "Building", "Tile"),
        help="Asset category",
    )
    parser.add_argument(
        "--rig",
        default="Auto",
        choices=("Auto", "Humanoid", "Quadruped", "Aquatic", "Static"),
        help="Rig profile",
    )
    parser.add_argument("--scale", type=float, default=1.5, help="Target max extent (meters)")
    parser.add_argument("--target-tris", type=int, default=12000, help="Triangle budget")
    parser.add_argument("--joints-json", default=None, help="Optional UniRig joints JSON path")

    # render_views options
    parser.add_argument("--out-albedo", help="Output raw albedo PNG path")
    parser.add_argument("--out-normal", help="Output raw view-space normal PNG path")
    parser.add_argument("--out-qa", help="Output 2x2 QA contact sheet PNG path")
    parser.add_argument("--size", type=int, default=128, choices=(128, 256, 512), help="Target sprite size")
    parser.add_argument("--render-frames", type=int, default=1, choices=(1, 4), help="1 or 4 strip frames")

    return parser.parse_args(argv)


# ---------------------------------------------------------------------------
# Scene & Mesh Utilities
# ---------------------------------------------------------------------------

def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def import_glb(filepath: str) -> List[bpy.types.Object]:
    bpy.ops.import_scene.gltf(filepath=str(filepath))
    return list(bpy.context.scene.objects)


def collect_mesh_objects() -> List[bpy.types.Object]:
    return [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]


def bake_and_join_meshes(mesh_objs: List[bpy.types.Object]) -> bpy.types.Object:
    """
    Bake world transforms into mesh data, standardize UV layer names so UV coordinates
    survive joining, remove empty non-mesh helper nodes, and join into one clean mesh.
    """
    if not mesh_objs:
        raise RuntimeError("No MESH objects found in imported .glb.")

    bpy.ops.object.select_all(action="DESELECT")

    cleaned_objs: List[bpy.types.Object] = []
    for obj in mesh_objs:
        if obj.data.users > 1:
            obj.data = obj.data.copy()

        # Unparent while preserving world transform, then bake world matrix directly into vertices
        world_mat = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = Matrix.Identity(4)

        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.transform(bm, matrix=world_mat, verts=bm.verts)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()

        # Ensure primary UV layer is consistently named so join() merges UV layers cleanly
        if obj.data.uv_layers:
            active_uv = obj.data.uv_layers.active or obj.data.uv_layers[0]
            active_uv.name = "UVMap"

        if len(obj.data.polygons) > 0:
            cleaned_objs.append(obj)
        else:
            bpy.data.objects.remove(obj, do_unlink=True)

    if not cleaned_objs:
        raise RuntimeError("All imported meshes have zero polygons.")

    # Remove non-mesh objects (empties, cameras, lights, old armatures)
    for obj in list(bpy.context.scene.objects):
        if obj not in cleaned_objs:
            bpy.data.objects.remove(obj, do_unlink=True)

    if len(cleaned_objs) == 1:
        main_obj = cleaned_objs[0]
    else:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in cleaned_objs:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = cleaned_objs[0]
        bpy.ops.object.join()
        main_obj = bpy.context.view_layer.objects.active

    main_obj.name = "AssetMesh"
    main_obj.data.name = "AssetMeshData"
    return main_obj


def remove_stray_floaters_and_fix_normals(mesh_obj: bpy.types.Object) -> int:
    """
    Use bmesh connected-components analysis (using spatial vertex positions so UV
    seams and split-normal edges stay welded topologically) to remove tiny
    disconnected stray floaters (< 1% of largest component or < 12 faces when main
    mesh has > 100 faces), and make face normals consistently outward-facing.
    Returns number of removed floater faces.
    """
    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    n_faces = len(bm.faces)
    removed_faces = 0

    if n_faces > 0:
        coords = np.array([[v.co.x, v.co.y, v.co.z] for v in bm.verts], dtype=np.float64)
        ext = float(np.max(coords.max(axis=0) - coords.min(axis=0))) if len(coords) else 1.0
        inv_tol = 1.0 / max(ext * 1e-5, 1e-6)

        # Map spatial position key -> list of faces touching that 3D point
        pos_to_faces: Dict[Tuple[int, int, int], List[int]] = {}
        face_pos_keys: List[List[Tuple[int, int, int]]] = []
        for f in bm.faces:
            keys: List[Tuple[int, int, int]] = []
            for v in f.verts:
                k = (
                    int(round(v.co.x * inv_tol)),
                    int(round(v.co.y * inv_tol)),
                    int(round(v.co.z * inv_tol)),
                )
                keys.append(k)
                pos_to_faces.setdefault(k, []).append(f.index)
            face_pos_keys.append(keys)

        visited = set()
        components: List[List[int]] = []

        for f_idx in range(n_faces):
            if f_idx in visited:
                continue
            comp: List[int] = []
            stack = [f_idx]
            visited.add(f_idx)
            while stack:
                cur = stack.pop()
                comp.append(cur)
                for k in face_pos_keys[cur]:
                    for nf_idx in pos_to_faces[k]:
                        if nf_idx not in visited:
                            visited.add(nf_idx)
                            stack.append(nf_idx)
            components.append(comp)

        if len(components) > 1:
            components.sort(key=len, reverse=True)
            largest_count = len(components[0])
            faces_to_delete: List[bmesh.types.BMFace] = []
            for comp in components[1:]:
                sz = len(comp)
                is_tiny_pct = sz < (0.01 * largest_count)
                is_tiny_abs = (largest_count > 100) and (sz < 12)
                if is_tiny_pct or is_tiny_abs:
                    faces_to_delete.extend(bm.faces[idx] for idx in comp)

            if faces_to_delete:
                removed_faces = len(faces_to_delete)
                bmesh.ops.delete(bm, geom=faces_to_delete, context="FACES")

                orphan_verts = [v for v in bm.verts if not v.link_faces]
                if orphan_verts:
                    bmesh.ops.delete(bm, geom=orphan_verts, context="VERTS")

        bm.faces.ensure_lookup_table()
        if len(bm.faces) > 0:
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))

    bm.to_mesh(mesh_obj.data)
    bm.free()
    mesh_obj.data.validate(verbose=False)
    mesh_obj.data.update()
    return removed_faces


def compute_mesh_bounds_blender(mesh_obj: bpy.types.Object) -> Tuple[np.ndarray, np.ndarray]:
    """Return (min_xyz, max_xyz) in Blender world coordinates."""
    coords = np.empty(len(mesh_obj.data.vertices) * 3, dtype=np.float64)
    mesh_obj.data.vertices.foreach_get("co", coords)
    pts = coords.reshape(-1, 3)
    if len(pts) == 0:
        return np.zeros(3), np.zeros(3)
    mat = np.array(mesh_obj.matrix_world, dtype=np.float64)
    pts_h = np.hstack([pts, np.ones((len(pts), 1), dtype=np.float64)])
    world_pts = (pts_h @ mat.T)[:, :3]
    return world_pts.min(axis=0), world_pts.max(axis=0)


def normalize_mesh_transform(mesh_obj: bpy.types.Object, target_scale: float) -> Dict[str, Any]:
    """
    Center X=0, Y=0, ground min Z=0 in Blender space, and uniformly scale so
    max extent == target_scale. When exported via glTF (export_yup=True):
      - Blender +X -> glTF +X (center_x == 0.0)
      - Blender +Z -> glTF +Y (min_y == 0.0)
      - Blender -Y -> glTF +Z (center_z == 0.0, front faces +Z)
    """
    bmin, bmax = compute_mesh_bounds_blender(mesh_obj)
    ext = bmax - bmin
    max_ext = float(max(ext[0], ext[1], ext[2], 1e-6))
    scale_factor = float(target_scale) / max_ext

    cx = 0.5 * float(bmin[0] + bmax[0])
    cy = 0.5 * float(bmin[1] + bmax[1])
    min_z = float(bmin[2])

    # Apply translation then uniform scale directly into vertex data
    t_mat = Matrix.Translation(Vector((-cx, -cy, -min_z)))
    s_mat = Matrix.Scale(scale_factor, 4)
    xform = s_mat @ t_mat

    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)
    bmesh.ops.transform(bm, matrix=xform, verts=bm.verts)
    bm.to_mesh(mesh_obj.data)
    bm.free()
    mesh_obj.data.update()

    bmin2, bmax2 = compute_mesh_bounds_blender(mesh_obj)
    ext2 = bmax2 - bmin2
    return {
        "blender_min": [round(float(v), 5) for v in bmin2],
        "blender_max": [round(float(v), 5) for v in bmax2],
        # glTF Y-up space equivalents: x=x, y=z, z=-y
        "bounds_m": {
            "x": round(float(ext2[0]), 5),
            "y": round(float(ext2[2]), 5),
            "z": round(float(ext2[1]), 5),
        },
        "min_y": round(float(bmin2[2]), 5),
        "center_x": round(0.5 * float(bmin2[0] + bmax2[0]), 5),
        "center_z": round(-0.5 * float(bmin2[1] + bmax2[1]), 5),
    }


def count_triangles(mesh_data: bpy.types.Mesh) -> int:
    return sum(max(1, len(p.vertices) - 2) for p in mesh_data.polygons)


def decimate_if_needed(mesh_obj: bpy.types.Object, target_tris: int) -> int:
    cur_tris = count_triangles(mesh_obj.data)
    if target_tris > 0 and cur_tris > target_tris:
        ratio = max(0.01, min(1.0, float(target_tris) / float(cur_tris)))
        mod = mesh_obj.modifiers.new(name="FoundryDecimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True

        bpy.ops.object.select_all(action="DESELECT")
        mesh_obj.select_set(True)
        bpy.context.view_layer.objects.active = mesh_obj
        bpy.ops.object.modifier_apply(modifier=mod.name)

        # Re-fix normals cleanly after collapse decimation
        bm = bmesh.new()
        bm.from_mesh(mesh_obj.data)
        if len(bm.faces) > 0:
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh_obj.data)
        bm.free()
        mesh_obj.data.validate(verbose=False)
        mesh_obj.data.update()
        cur_tris = count_triangles(mesh_obj.data)
    return cur_tris


# ---------------------------------------------------------------------------
# Rigging Profiles & Skinning
# ---------------------------------------------------------------------------

def resolve_rig_profile(rig_arg: str, category: str, bounds_m: Dict[str, float]) -> str:
    if category in ("Building", "Tile"):
        return "Static"
    if rig_arg != "Auto":
        return rig_arg
    if category == "Prop":
        return "Static"
    if category == "Hero":
        return "Humanoid"
    # Monster auto-detection from proportions (Y is height, Z is front-back, X is width in glTF)
    h = bounds_m["y"]
    depth = bounds_m["z"]
    width = bounds_m["x"]
    if h >= max(depth, width) * 0.85:
        return "Humanoid"
    if depth > h * 1.35 and width < depth * 0.65:
        return "Quadruped"
    return "Humanoid"


def _add_edit_bone(
    edit_bones: bpy.types.ArmatureEditBones,
    name: str,
    head: Tuple[float, float, float],
    tail: Tuple[float, float, float],
    parent_name: Optional[str] = None,
    use_deform: bool = True,
    connected: bool = False,
) -> bpy.types.EditBone:
    eb = edit_bones.new(name)
    h_vec = Vector(head)
    t_vec = Vector(tail)
    if (t_vec - h_vec).length < 1e-4:
        t_vec = h_vec + Vector((0.0, 0.0, 0.05))
    eb.head = h_vec
    eb.tail = t_vec
    eb.use_deform = use_deform
    if parent_name and parent_name in edit_bones:
        eb.parent = edit_bones[parent_name]
        eb.use_connect = connected
    return eb


def _compute_mesh_coords_array(mesh_obj: bpy.types.Object) -> np.ndarray:
    coords = np.empty(len(mesh_obj.data.vertices) * 3, dtype=np.float64)
    mesh_obj.data.vertices.foreach_get("co", coords)
    return coords.reshape(-1, 3)


def create_template_armature(
    profile: str,
    mesh_obj: bpy.types.Object,
) -> bpy.types.Object:
    """
    Build a deterministic template armature fitted cleanly inside the normalized
    Blender mesh bounding box (Z-up, front facing -Y).
    """
    pts = _compute_mesh_coords_array(mesh_obj)
    bmin = pts.min(axis=0)
    bmax = pts.max(axis=0)
    dx = float(max(bmax[0] - bmin[0], 0.1))
    dy = float(max(bmax[1] - bmin[1], 0.1))
    dz = float(max(bmax[2] - bmin[2], 0.1))
    cx = 0.5 * float(bmin[0] + bmax[0])
    cy = 0.5 * float(bmin[1] + bmax[1])
    z0 = float(bmin[2])

    arm_data = bpy.data.armatures.new("AssetRigData")
    arm_obj = bpy.data.objects.new("AssetRig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)

    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    if profile == "Humanoid":
        # 20 template bones (all required names: Root, Hips, Spine, Chest, Neck, Head,
        # Shoulder.L/R, UpperArm.L/R, LowerArm.L/R, Hand.L/R, Thigh.L/R, Shin.L/R, Foot.L/R)
        hip_z = z0 + 0.50 * dz
        spine_z = z0 + 0.60 * dz
        chest_z = z0 + 0.72 * dz
        neck_z = z0 + 0.82 * dz
        head_z = z0 + 0.88 * dz
        head_top = z0 + 0.98 * dz

        sh_x = min(0.16 * dx, 0.13 * dz)
        ua_x = min(0.26 * dx, 0.20 * dz)
        la_x = min(0.36 * dx, 0.30 * dz)
        ha_x = min(0.44 * dx, 0.38 * dz)
        sh_z = z0 + 0.79 * dz
        el_z = z0 + 0.64 * dz
        wr_z = z0 + 0.50 * dz
        ha_z = z0 + 0.44 * dz

        hip_x = min(0.16 * dx, 0.09 * dz)
        knee_z = z0 + 0.27 * dz
        ank_z = z0 + 0.05 * dz
        toe_y = cy - min(0.28 * dy, 0.08 * dz)

        _add_edit_bone(eb, "Root", (cx, cy, z0), (cx, cy, z0 + 0.15 * dz), None, use_deform=False)
        _add_edit_bone(eb, "Hips", (cx, cy, hip_z), (cx, cy, spine_z), "Root", use_deform=True)
        _add_edit_bone(eb, "Spine", (cx, cy, spine_z), (cx, cy, chest_z), "Hips", connected=True)
        _add_edit_bone(eb, "Chest", (cx, cy, chest_z), (cx, cy, neck_z), "Spine", connected=True)
        _add_edit_bone(eb, "Neck", (cx, cy, neck_z), (cx, cy, head_z), "Chest", connected=True)
        _add_edit_bone(eb, "Head", (cx, cy, head_z), (cx, cy, head_top), "Neck", connected=True)

        for side, sgn in (("L", 1.0), ("R", -1.0)):
            _add_edit_bone(eb, f"Shoulder.{side}", (cx + sgn * 0.03 * dz, cy, sh_z), (cx + sgn * sh_x, cy, sh_z), "Chest")
            _add_edit_bone(eb, f"UpperArm.{side}", (cx + sgn * sh_x, cy, sh_z), (cx + sgn * ua_x, cy, el_z), f"Shoulder.{side}", connected=True)
            _add_edit_bone(eb, f"LowerArm.{side}", (cx + sgn * ua_x, cy, el_z), (cx + sgn * la_x, cy, wr_z), f"UpperArm.{side}", connected=True)
            _add_edit_bone(eb, f"Hand.{side}", (cx + sgn * la_x, cy, wr_z), (cx + sgn * ha_x, cy, ha_z), f"LowerArm.{side}", connected=True)

            _add_edit_bone(eb, f"Thigh.{side}", (cx + sgn * hip_x, cy, hip_z), (cx + sgn * hip_x, cy, knee_z), "Hips")
            _add_edit_bone(eb, f"Shin.{side}", (cx + sgn * hip_x, cy, knee_z), (cx + sgn * hip_x, cy, ank_z), f"Thigh.{side}", connected=True)
            _add_edit_bone(eb, f"Foot.{side}", (cx + sgn * hip_x, cy, ank_z), (cx + sgn * hip_x, toe_y, z0 + 0.02 * dz), f"Shin.{side}", connected=True)

    elif profile == "Quadruped":
        # Front faces -Y, tail toward +Y
        body_z = z0 + 0.56 * dz
        chest_y = cy - 0.16 * dy
        hip_y = cy + 0.16 * dy
        neck_y = cy - 0.28 * dy
        head_y = cy - 0.40 * dy
        leg_x = min(0.24 * dx, 0.16 * dy)
        knee_z = z0 + 0.28 * dz
        paw_z = z0 + 0.06 * dz

        _add_edit_bone(eb, "Root", (cx, cy, z0), (cx, cy - 0.15 * dy, z0), None, use_deform=False)
        _add_edit_bone(eb, "Hips", (cx, hip_y, body_z), (cx, cy, body_z), "Root")
        _add_edit_bone(eb, "Spine", (cx, cy, body_z), (cx, chest_y, body_z + 0.03 * dz), "Hips", connected=True)
        _add_edit_bone(eb, "Chest", (cx, chest_y, body_z + 0.03 * dz), (cx, neck_y, body_z + 0.10 * dz), "Spine", connected=True)
        _add_edit_bone(eb, "Neck", (cx, neck_y, body_z + 0.10 * dz), (cx, head_y, z0 + 0.78 * dz), "Chest", connected=True)
        _add_edit_bone(eb, "Head", (cx, head_y, z0 + 0.78 * dz), (cx, cy - 0.48 * dy, z0 + 0.76 * dz), "Neck", connected=True)

        _add_edit_bone(eb, "Tail1", (cx, hip_y + 0.04 * dy, body_z + 0.02 * dz), (cx, cy + 0.34 * dy, body_z + 0.04 * dz), "Hips")
        _add_edit_bone(eb, "Tail2", (cx, cy + 0.34 * dy, body_z + 0.04 * dz), (cx, cy + 0.46 * dy, body_z), "Tail1", connected=True)

        for side, sgn in (("L", 1.0), ("R", -1.0)):
            _add_edit_bone(eb, f"FrontLeg_{side}", (cx + sgn * leg_x, chest_y, body_z), (cx + sgn * leg_x, chest_y, knee_z), "Chest")
            _add_edit_bone(eb, f"FrontPaw_{side}", (cx + sgn * leg_x, chest_y, paw_z), (cx + sgn * leg_x, chest_y - 0.08 * dy, z0 + 0.02 * dz), f"FrontLeg_{side}")
            _add_edit_bone(eb, f"BackLeg_{side}", (cx + sgn * leg_x, hip_y, body_z), (cx + sgn * leg_x, hip_y, knee_z), "Hips")
            _add_edit_bone(eb, f"BackPaw_{side}", (cx + sgn * leg_x, hip_y, paw_z), (cx + sgn * leg_x, hip_y - 0.08 * dy, z0 + 0.02 * dz), f"BackLeg_{side}")

    elif profile == "Aquatic":
        # 8 bones along longitudinal swim axis (-Y head to +Y tail)
        mid_z = z0 + 0.50 * dz
        _add_edit_bone(eb, "Root", (cx, cy, z0), (cx, cy - 0.12 * dy, z0), None, use_deform=False)
        _add_edit_bone(eb, "Head", (cx, cy - 0.18 * dy, mid_z), (cx, cy - 0.44 * dy, mid_z), "Root")
        _add_edit_bone(eb, "Body_Mid", (cx, cy - 0.18 * dy, mid_z), (cx, cy + 0.06 * dy, mid_z), "Root")
        _add_edit_bone(eb, "Tail_Base", (cx, cy + 0.06 * dy, mid_z), (cx, cy + 0.24 * dy, mid_z), "Body_Mid", connected=True)
        _add_edit_bone(eb, "Tail_Mid", (cx, cy + 0.24 * dy, mid_z), (cx, cy + 0.38 * dy, mid_z), "Tail_Base", connected=True)
        _add_edit_bone(eb, "Tail_Fin", (cx, cy + 0.38 * dy, mid_z), (cx, cy + 0.48 * dy, mid_z), "Tail_Mid", connected=True)
        _add_edit_bone(eb, "Fin_L", (cx + 0.12 * dx, cy - 0.08 * dy, mid_z - 0.05 * dz), (cx + 0.42 * dx, cy + 0.02 * dy, mid_z - 0.12 * dz), "Body_Mid")
        _add_edit_bone(eb, "Fin_R", (cx - 0.12 * dx, cy - 0.08 * dy, mid_z - 0.05 * dz), (cx - 0.42 * dx, cy + 0.02 * dy, mid_z - 0.12 * dz), "Body_Mid")

    else:
        raise ValueError(f"Unsupported rig profile: {profile}")

    # Align bone roll cleanly along +Y/+Z for deterministic Euler rotations
    bpy.ops.armature.select_all(action="SELECT")
    bpy.ops.armature.calculate_roll(type="GLOBAL_POS_Z")
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def create_armature_from_joints_json(joints_path: str) -> bpy.types.Object:
    """
    Build a Blender Armature from a UniRig-style JSON file containing joint coordinates
    in glTF Y-up space (transformed back to Blender Z-up: bx=gx, by=-gz, bz=gy)
    and parent indices.
    Supported schemas:
      1. {"joints": [{"name": "...", "pos": [x,y,z], "parent": -1|idx|name}, ...]}
      2. {"joints": [[x,y,z], ...], "parents": [-1, ...], "names": ["...", ...]}
    """
    data = json.loads(Path(joints_path).read_text(encoding="utf-8"))
    raw_joints = data.get("joints")
    if not raw_joints or not isinstance(raw_joints, list):
        raise ValueError(f"Invalid joints JSON schema in {joints_path}")

    names: List[str] = []
    positions_bl: List[Vector] = []
    parents: List[int] = []

    def gltf_to_blender(pt: Sequence[float]) -> Vector:
        gx, gy, gz = float(pt[0]), float(pt[1]), float(pt[2])
        return Vector((gx, -gz, gy))

    if isinstance(raw_joints[0], dict):
        name_to_idx: Dict[str, int] = {}
        for i, item in enumerate(raw_joints):
            nm = str(item.get("name") or f"Joint_{i:02d}")
            names.append(nm)
            name_to_idx[nm] = i
            pos_raw = item.get("pos") or item.get("position") or item.get("head")
            if pos_raw is None:
                raise ValueError(f"Joint {nm} missing 'pos' field")
            positions_bl.append(gltf_to_blender(pos_raw))
        for i, item in enumerate(raw_joints):
            p = item.get("parent", -1)
            if isinstance(p, str):
                parents.append(name_to_idx.get(p, -1))
            elif p is None:
                parents.append(-1)
            else:
                parents.append(int(p))
    else:
        raw_parents = data.get("parents", [-1] * len(raw_joints))
        raw_names = data.get("names", [f"Joint_{i:02d}" for i in range(len(raw_joints))])
        for i, pt in enumerate(raw_joints):
            names.append(str(raw_names[i]))
            positions_bl.append(gltf_to_blender(pt))
            parents.append(int(raw_parents[i]))

    # Precompute children to set clean bone tails pointing toward child centroid
    children_map: Dict[int, List[int]] = {i: [] for i in range(len(names))}
    for i, p_idx in enumerate(parents):
        if 0 <= p_idx < len(names) and p_idx != i:
            children_map[p_idx].append(i)

    arm_data = bpy.data.armatures.new("AssetRigData")
    arm_obj = bpy.data.objects.new("AssetRig", arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)

    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    created_bones: List[bpy.types.EditBone] = []
    for i, nm in enumerate(names):
        head = positions_bl[i]
        ch = children_map[i]
        if ch:
            avg_child = sum((positions_bl[c] for c in ch), Vector((0.0, 0.0, 0.0))) / float(len(ch))
            tail = avg_child
        elif 0 <= parents[i] < len(names):
            delta = head - positions_bl[parents[i]]
            if delta.length > 1e-4:
                tail = head + delta.normalized() * max(0.05, delta.length * 0.6)
            else:
                tail = head + Vector((0.0, 0.0, 0.08))
        else:
            tail = head + Vector((0.0, 0.0, 0.12))

        if (tail - head).length < 1e-4:
            tail = head + Vector((0.0, 0.0, 0.05))

        bone = eb.new(nm)
        bone.head = head
        bone.tail = tail
        bone.use_deform = (i != 0 or len(names) == 1 or nm.lower() != "root")
        created_bones.append(bone)

    for i, p_idx in enumerate(parents):
        if 0 <= p_idx < len(created_bones) and p_idx != i:
            created_bones[i].parent = created_bones[p_idx]
            created_bones[i].use_connect = False

    bpy.ops.armature.select_all(action="SELECT")
    bpy.ops.armature.calculate_roll(type="GLOBAL_POS_Z")
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def _point_to_segment_distance_sq(p: np.ndarray, a: np.ndarray, b: np.ndarray) -> np.ndarray:
    """Vectorized squared distance from N points p (N, 3) to line segment [a, b]."""
    ab = b - a
    len_sq = float(np.dot(ab, ab))
    if len_sq < 1e-10:
        diff = p - a[None, :]
        return np.sum(diff * diff, axis=1)
    t = np.clip(np.dot(p - a[None, :], ab) / len_sq, 0.0, 1.0)
    proj = a[None, :] + t[:, None] * ab[None, :]
    diff = p - proj
    return np.sum(diff * diff, axis=1)


def skin_mesh_to_armature(mesh_obj: bpy.types.Object, arm_obj: bpy.types.Object, target_scale: float) -> Dict[str, Any]:
    """
    Skin mesh_obj to arm_obj using ARMATURE_AUTO (bone heat), then inspect all
    vertices and apply smooth inverse-distance Gaussian weights to the 3 nearest
    deform bone segments for any vertex whose total weight < 1e-4.
    """
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj

    heat_ok = True
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except Exception:
        heat_ok = False
        bpy.ops.object.parent_set(type="ARMATURE_NAME")

    # Ensure every deform bone has a vertex group on mesh_obj
    deform_bones = [b for b in arm_obj.data.bones if b.use_deform]
    if not deform_bones:
        deform_bones = list(arm_obj.data.bones)

    vg_map: Dict[str, bpy.types.VertexGroup] = {}
    for b in deform_bones:
        vg = mesh_obj.vertex_groups.get(b.name)
        if vg is None:
            vg = mesh_obj.vertex_groups.new(name=b.name)
        vg_map[b.name] = vg

    deform_vg_indices = {vg.index for vg in vg_map.values()}

    # Check each vertex total deform weight
    unskinned_indices: List[int] = []
    for v in mesh_obj.data.vertices:
        w_sum = sum(g.weight for g in v.groups if g.group in deform_vg_indices)
        if w_sum < 1e-4:
            unskinned_indices.append(v.index)

    fallback_count = len(unskinned_indices)
    if fallback_count > 0 and deform_bones:
        pts = _compute_mesh_coords_array(mesh_obj)[unskinned_indices]
        # Compute distance matrix (M_unskinned x N_bones)
        dist_sq = np.empty((len(unskinned_indices), len(deform_bones)), dtype=np.float64)
        for j, b in enumerate(deform_bones):
            a = np.array(b.head_local, dtype=np.float64)
            b_pt = np.array(b.tail_local, dtype=np.float64)
            dist_sq[:, j] = _point_to_segment_distance_sq(pts, a, b_pt)

        k = min(3, len(deform_bones))
        sigma_floor = max(0.05 * target_scale, 1e-3)
        for row_idx, vert_idx in enumerate(unskinned_indices):
            d2_row = dist_sq[row_idx]
            nearest_cols = np.argpartition(d2_row, k - 1)[:k] if k < len(deform_bones) else np.arange(len(deform_bones))
            nearest_d2 = d2_row[nearest_cols]
            sigma = max(sigma_floor, float(np.sqrt(np.min(nearest_d2))) * 1.5)
            weights = np.exp(-nearest_d2 / (2.0 * sigma * sigma))
            w_total = float(np.sum(weights))
            if w_total < 1e-12:
                weights = np.ones(k, dtype=np.float64) / float(k)
            else:
                weights = weights / w_total
            for col_i, w_val in zip(nearest_cols, weights):
                if float(w_val) > 1e-5:
                    b_name = deform_bones[int(col_i)].name
                    vg_map[b_name].add([vert_idx], float(w_val), "REPLACE")

    return {
        "bone_heat_succeeded": heat_ok and fallback_count == 0,
        "fallback_weighted_vertices": fallback_count,
        "total_vertices": len(mesh_obj.data.vertices),
    }


# ---------------------------------------------------------------------------
# Animation Synthesis (24 fps, Idle / Move / Attack pushed to NLA Tracks)
# ---------------------------------------------------------------------------

def _reset_pose(arm_obj: bpy.types.Object) -> None:
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def _key_bone(
    arm_obj: bpy.types.Object,
    bone_name: str,
    frame: int,
    rot_deg: Optional[Tuple[float, float, float]] = None,
    loc: Optional[Tuple[float, float, float]] = None,
) -> None:
    pb = arm_obj.pose.bones.get(bone_name)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    if rot_deg is not None:
        pb.rotation_euler = Euler(
            (math.radians(rot_deg[0]), math.radians(rot_deg[1]), math.radians(rot_deg[2])),
            "XYZ",
        )
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)
    if loc is not None:
        pb.location = Vector(loc)
        pb.keyframe_insert(data_path="location", frame=frame)


def _push_action_to_nla(arm_obj: bpy.types.Object, action: bpy.types.Action) -> None:
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    track = arm_obj.animation_data.nla_tracks.new()
    track.name = action.name
    start_f = int(round(action.frame_range[0]))
    strip = track.strips.new(action.name, start_f, action)
    strip.name = action.name
    arm_obj.animation_data.action = None


def generate_animations(arm_obj: bpy.types.Object, profile: str, target_scale: float) -> List[str]:
    """
    Generate 3 clean 24 fps Actions (Idle, Move, Attack) tailored to the rig
    profile (or generic bone-name matching for custom UniRig skeletons) and push
    them onto NLA tracks for glTF export.
    """
    scene = bpy.context.scene
    scene.render.fps = 24
    scene.frame_start = 1
    scene.frame_end = 49

    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()

    # Helper to find first matching bone from candidate names
    def pick(*candidates: str) -> Optional[str]:
        for c in candidates:
            if c in arm_obj.pose.bones:
                return c
        lower_map = {b.name.lower(): b.name for b in arm_obj.pose.bones}
        for c in candidates:
            if c.lower() in lower_map:
                return lower_map[c.lower()]
        return None

    hips = pick("Hips", "Pelvis", "Root", "Body_Mid")
    spine = pick("Spine", "Body_Mid")
    chest = pick("Chest", "Torso", "UpperChest")
    head = pick("Head")
    l_arm = pick("UpperArm.L", "FrontLeg_L", "Fin_L")
    r_arm = pick("UpperArm.R", "FrontLeg_R", "Fin_R")
    l_fore = pick("LowerArm.L", "FrontPaw_L")
    r_fore = pick("LowerArm.R", "FrontPaw_R")
    l_thigh = pick("Thigh.L", "BackLeg_L")
    r_thigh = pick("Thigh.R", "BackLeg_R")
    l_shin = pick("Shin.L", "BackPaw_L")
    r_shin = pick("Shin.R", "BackPaw_R")
    tail1 = pick("Tail1", "Tail_Base")
    tail2 = pick("Tail2", "Tail_Mid")
    tail3 = pick("Tail_Fin")

    bob = 0.02 * target_scale
    lunge = 0.10 * target_scale
    created_actions: List[str] = []

    # 1. Idle (frames 1..49, 2.0s loop)
    act_idle = bpy.data.actions.new("Idle")
    arm_obj.animation_data.action = act_idle
    _reset_pose(arm_obj)
    for f, phase in ((1, 0.0), (13, 1.0), (25, 0.0), (37, -1.0), (49, 0.0)):
        if hips:
            _key_bone(arm_obj, hips, f, loc=(0.0, 0.0, bob * 0.5 * phase))
        if profile == "Aquatic":
            if head:
                _key_bone(arm_obj, head, f, rot_deg=(0.0, 0.0, 3.0 * phase))
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(0.0, 0.0, -5.0 * phase))
            if tail1:
                _key_bone(arm_obj, tail1, f, rot_deg=(0.0, 0.0, 10.0 * phase))
            if tail2:
                _key_bone(arm_obj, tail2, f, rot_deg=(0.0, 0.0, 14.0 * phase))
            if tail3:
                _key_bone(arm_obj, tail3, f, rot_deg=(0.0, 0.0, 18.0 * phase))
            if l_arm:
                _key_bone(arm_obj, l_arm, f, rot_deg=(6.0 * phase, 0.0, 0.0))
            if r_arm:
                _key_bone(arm_obj, r_arm, f, rot_deg=(6.0 * phase, 0.0, 0.0))
        else:
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(2.5 * phase, 0.0, 0.0))
            if chest:
                _key_bone(arm_obj, chest, f, rot_deg=(3.0 * phase, 0.0, 0.0))
            if head:
                _key_bone(arm_obj, head, f, rot_deg=(-2.0 * phase, 1.5 * phase, 0.0))
            if l_arm:
                _key_bone(arm_obj, l_arm, f, rot_deg=(0.0, 3.0 * phase, 0.0))
            if r_arm:
                _key_bone(arm_obj, r_arm, f, rot_deg=(0.0, -3.0 * phase, 0.0))
            if tail1:
                _key_bone(arm_obj, tail1, f, rot_deg=(0.0, 0.0, 8.0 * phase))
    _push_action_to_nla(arm_obj, act_idle)
    created_actions.append("Idle")

    # 2. Move (frames 1..25, 1.0s loop)
    act_move = bpy.data.actions.new("Move")
    arm_obj.animation_data.action = act_move
    _reset_pose(arm_obj)
    for f, sgn, bounce in ((1, 0.0, 0.0), (7, 1.0, 1.0), (13, 0.0, 0.0), (19, -1.0, 1.0), (25, 0.0, 0.0)):
        if hips:
            _key_bone(arm_obj, hips, f, loc=(0.0, 0.0, bob * bounce), rot_deg=(0.0, 0.0, 4.0 * sgn))
        if profile == "Aquatic":
            if head:
                _key_bone(arm_obj, head, f, rot_deg=(0.0, 0.0, -6.0 * sgn))
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(0.0, 0.0, 10.0 * sgn))
            if tail1:
                _key_bone(arm_obj, tail1, f, rot_deg=(0.0, 0.0, -16.0 * sgn))
            if tail2:
                _key_bone(arm_obj, tail2, f, rot_deg=(0.0, 0.0, -22.0 * sgn))
            if tail3:
                _key_bone(arm_obj, tail3, f, rot_deg=(0.0, 0.0, -26.0 * sgn))
            if l_arm:
                _key_bone(arm_obj, l_arm, f, rot_deg=(12.0 * sgn, 0.0, 8.0 * sgn))
            if r_arm:
                _key_bone(arm_obj, r_arm, f, rot_deg=(-12.0 * sgn, 0.0, 8.0 * sgn))
        else:
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(4.0 * bounce, 0.0, -5.0 * sgn))
            if l_thigh:
                _key_bone(arm_obj, l_thigh, f, rot_deg=(24.0 * sgn, 0.0, 0.0))
            if r_thigh:
                _key_bone(arm_obj, r_thigh, f, rot_deg=(-24.0 * sgn, 0.0, 0.0))
            if l_shin:
                _key_bone(arm_obj, l_shin, f, rot_deg=(max(0.0, -20.0 * sgn), 0.0, 0.0))
            if r_shin:
                _key_bone(arm_obj, r_shin, f, rot_deg=(max(0.0, 20.0 * sgn), 0.0, 0.0))
            if l_arm:
                _key_bone(arm_obj, l_arm, f, rot_deg=(-18.0 * sgn, 0.0, 0.0))
            if r_arm:
                _key_bone(arm_obj, r_arm, f, rot_deg=(18.0 * sgn, 0.0, 0.0))
            if l_fore:
                _key_bone(arm_obj, l_fore, f, rot_deg=(10.0, 0.0, 0.0))
            if r_fore:
                _key_bone(arm_obj, r_fore, f, rot_deg=(10.0, 0.0, 0.0))
            if tail1:
                _key_bone(arm_obj, tail1, f, rot_deg=(0.0, 0.0, 14.0 * sgn))
            if tail2:
                _key_bone(arm_obj, tail2, f, rot_deg=(0.0, 0.0, 16.0 * sgn))
    _push_action_to_nla(arm_obj, act_move)
    created_actions.append("Move")

    # 3. Attack (frames 1..25, windup -> lunge strike -> recover)
    act_atk = bpy.data.actions.new("Attack")
    arm_obj.animation_data.action = act_atk
    _reset_pose(arm_obj)
    # Frame 1: rest, Frame 6: windup, Frame 12: lunge strike, Frame 18: follow-through, Frame 25: rest
    keyframes = [
        (1, 0.0, 0.0, 0.0),
        (6, 0.4 * lunge, -14.0, -35.0),
        (12, -1.0 * lunge, 22.0, 55.0),
        (18, -0.5 * lunge, 10.0, 25.0),
        (25, 0.0, 0.0, 0.0),
    ]
    for f, y_shift, torso_pitch, arm_swing in keyframes:
        if hips:
            _key_bone(arm_obj, hips, f, loc=(0.0, y_shift, 0.0))
        if profile == "Aquatic":
            if head:
                _key_bone(arm_obj, head, f, rot_deg=(torso_pitch * 0.5, 0.0, arm_swing * 0.3))
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(0.0, 0.0, -arm_swing * 0.4))
            if tail1:
                _key_bone(arm_obj, tail1, f, rot_deg=(0.0, 0.0, arm_swing * 0.5))
            if tail3:
                _key_bone(arm_obj, tail3, f, rot_deg=(0.0, 0.0, arm_swing * 0.7))
        else:
            if spine:
                _key_bone(arm_obj, spine, f, rot_deg=(torso_pitch * 0.5, 0.0, -arm_swing * 0.2))
            if chest:
                _key_bone(arm_obj, chest, f, rot_deg=(torso_pitch * 0.6, 0.0, -arm_swing * 0.25))
            if r_arm:
                _key_bone(arm_obj, r_arm, f, rot_deg=(arm_swing, 0.0, -15.0))
            if r_fore:
                _key_bone(arm_obj, r_fore, f, rot_deg=(max(0.0, arm_swing * 0.4), 0.0, 0.0))
            if l_arm:
                _key_bone(arm_obj, l_arm, f, rot_deg=(-arm_swing * 0.3, 0.0, 10.0))
            if head:
                _key_bone(arm_obj, head, f, rot_deg=(-torso_pitch * 0.3, 0.0, 0.0))
    _push_action_to_nla(arm_obj, act_atk)
    created_actions.append("Attack")

    _reset_pose(arm_obj)
    scene.frame_set(1)
    return created_actions


# ---------------------------------------------------------------------------
# Mode 1: normalize_rig
# ---------------------------------------------------------------------------

def run_normalize_rig(args: argparse.Namespace) -> None:
    if not args.output or not args.meta_out:
        raise ValueError("--output and --meta-out are required in normalize_rig mode.")

    reset_scene()
    import_glb(args.input)
    mesh_objs = collect_mesh_objects()
    main_mesh = bake_and_join_meshes(mesh_objs)
    removed_floaters = remove_stray_floaters_and_fix_normals(main_mesh)

    norm_info = normalize_mesh_transform(main_mesh, args.scale)
    final_tris = decimate_if_needed(main_mesh, args.target_tris)
    uv_present = len(main_mesh.data.uv_layers) > 0
    vert_count = len(main_mesh.data.vertices)

    rig_resolved = resolve_rig_profile(args.rig, args.category, norm_info["bounds_m"])

    bones_list: List[str] = []
    anim_list: List[str] = []
    skin_info: Dict[str, Any] = {}
    rigged = False

    if rig_resolved == "Static":
        # Clean root Empty parent for static assets (glTF preserves node hierarchy cleanly)
        root_empty = bpy.data.objects.new("AssetRoot", None)
        bpy.context.scene.collection.objects.link(root_empty)
        main_mesh.parent = root_empty
    else:
        if args.joints_json and Path(args.joints_json).exists():
            arm_obj = create_armature_from_joints_json(args.joints_json)
        else:
            arm_obj = create_template_armature(rig_resolved, main_mesh)

        skin_info = skin_mesh_to_armature(main_mesh, arm_obj, args.scale)
        anim_list = generate_animations(arm_obj, rig_resolved, args.scale)
        bones_list = [b.name for b in arm_obj.data.bones]
        rigged = True

    out_glb = Path(args.output)
    out_glb.parent.mkdir(parents=True, exist_ok=True)
    _sanitize_foundry_materials([main_mesh])
    bpy.ops.export_scene.gltf(
        filepath=str(out_glb),
        export_format="GLB",
        export_image_format="AUTO",
        export_materials="EXPORT",
        export_tangents=True,
        export_skins=True,
        export_animations=True,
        export_animation_mode="NLA_TRACKS",
        export_nla_strips=True,
        export_yup=True,
        export_def_bones=False,
        export_rest_position_armature=True,
    )

    meta = {
        "input": str(args.input),
        "output_glb": str(out_glb),
        "category": args.category,
        "rig_requested": args.rig,
        "rig_profile_resolved": rig_resolved,
        "rigged": rigged,
        "bones": bones_list,
        "bone_count": len(bones_list),
        "animations": anim_list,
        "bounds_m": norm_info["bounds_m"],
        "min_y": norm_info["min_y"],
        "center_x": norm_info["center_x"],
        "center_z": norm_info["center_z"],
        "triangle_count": final_tris,
        "vertex_count": vert_count,
        "uv_present": uv_present,
        "removed_floater_faces": removed_floaters,
        "skinning": skin_info,
    }
    meta_path = Path(args.meta_out)
    meta_path.parent.mkdir(parents=True, exist_ok=True)
    meta_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")
    print(json.dumps({"status": "ok", "mode": "normalize_rig", "meta_out": str(meta_path)}, indent=2))


# ---------------------------------------------------------------------------
# Mode 2: render_views
# ---------------------------------------------------------------------------

def _scene_mesh_bounds() -> Tuple[Vector, Vector]:
    """Compute world-space AABB across all MESH objects in current scene."""
    pts_list = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        eval_obj = obj.evaluated_get(depsgraph)
        mesh = eval_obj.to_mesh()
        if len(mesh.vertices) > 0:
            coords = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
            mesh.vertices.foreach_get("co", coords)
            pts = coords.reshape(-1, 3)
            mat = np.array(eval_obj.matrix_world, dtype=np.float64)
            pts_h = np.hstack([pts, np.ones((len(pts), 1), dtype=np.float64)])
            w_pts = (pts_h @ mat.T)[:, :3]
            pts_list.append(w_pts)
        eval_obj.to_mesh_clear()

    if not pts_list:
        return Vector((-0.5, -0.5, 0.0)), Vector((0.5, 0.5, 1.0))
    all_pts = np.vstack(pts_list)
    bmin = all_pts.min(axis=0)
    bmax = all_pts.max(axis=0)
    return Vector((float(bmin[0]), float(bmin[1]), float(bmin[2]))), Vector(
        (float(bmax[0]), float(bmax[1]), float(bmax[2]))
    )


def _configure_ortho_camera(
    cam_obj: bpy.types.Object,
    bmin: Vector,
    bmax: Vector,
    azimuth_deg: float,
    pitch_down_deg: float,
    margin: float = 1.12,
) -> None:
    """
    Place an orthographic camera at `azimuth_deg` (0 = looking at -Y Blender / +Z glTF front)
    pitched downward by `pitch_down_deg`, fitting all 8 AABB corners cleanly with `margin`.
    """
    cam_obj.data.type = "ORTHO"
    rx = math.radians(90.0 - pitch_down_deg)
    rz = math.radians(azimuth_deg)
    rot = Euler((rx, 0.0, rz), "XYZ").to_matrix()

    cam_right = rot @ Vector((1.0, 0.0, 0.0))
    cam_up = rot @ Vector((0.0, 1.0, 0.0))
    cam_look = rot @ Vector((0.0, 0.0, -1.0))

    corners = [
        Vector((x, y, z))
        for x in (bmin.x, bmax.x)
        for y in (bmin.y, bmax.y)
        for z in (bmin.z, bmax.z)
    ]
    xs = [c.dot(cam_right) for c in corners]
    ys = [c.dot(cam_up) for c in corners]
    zs = [c.dot(cam_look) for c in corners]

    mid_x = 0.5 * (min(xs) + max(xs))
    mid_y = 0.5 * (min(ys) + max(ys))
    mid_z = 0.5 * (min(zs) + max(zs))

    span_x = max(xs) - min(xs)
    span_y = max(ys) - min(ys)
    extent = max(span_x, span_y, 0.1)

    target = cam_right * mid_x + cam_up * mid_y + cam_look * mid_z
    dist = max(extent * 4.0, (max(zs) - min(zs)) * 2.0 + 2.0)

    cam_obj.location = target - cam_look * dist
    cam_obj.rotation_euler = Euler((rx, 0.0, rz), "XYZ")
    cam_obj.data.ortho_scale = extent * margin
    cam_obj.data.clip_start = 0.05
    cam_obj.data.clip_end = dist * 4.0 + 20.0


def _setup_lighting_and_world(bg_rgba: Tuple[float, float, float, float], ambient_strength: float) -> List[bpy.types.Object]:
    scene = bpy.context.scene
    if scene.world is None:
        scene.world = bpy.data.worlds.new("FoundryWorld")
    world = scene.world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = bg_rgba
    bg.inputs["Strength"].default_value = ambient_strength
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])

    # Warm sunlit key from top-front-left + cool sky rim/fill
    lights: List[bpy.types.Object] = []

    key_d = bpy.data.lights.new("KeySun", type="SUN")
    key_d.energy = 2.6
    key_d.color = (1.0, 0.96, 0.88)
    key_d.angle = math.radians(18.0)
    key = bpy.data.objects.new("KeySun", key_d)
    key.rotation_euler = Euler((math.radians(48.0), math.radians(-18.0), math.radians(-35.0)), "XYZ")
    scene.collection.objects.link(key)
    lights.append(key)

    fill_d = bpy.data.lights.new("FillSun", type="SUN")
    fill_d.energy = 1.45
    fill_d.color = (0.82, 0.90, 1.0)
    fill_d.angle = math.radians(45.0)
    fill = bpy.data.objects.new("FillSun", fill_d)
    fill.rotation_euler = Euler((math.radians(65.0), math.radians(20.0), math.radians(135.0)), "XYZ")
    scene.collection.objects.link(fill)
    lights.append(fill)

    return lights


def _sanitize_foundry_materials(mesh_objs: List[bpy.types.Object]) -> None:
    """Force imported PBR materials to double-sided diffuse-friendly settings (no metallic blackness or backface holes)."""
    for obj in mesh_objs:
        for mat in obj.data.materials:
            if mat is None:
                continue
            mat.use_backface_culling = False
            if hasattr(mat, "blend_method"):
                mat.blend_method = "OPAQUE"
            if not mat.use_nodes or mat.node_tree is None:
                continue
            for node in mat.node_tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    met = node.inputs.get("Metallic")
                    if met is not None and not met.links and float(met.default_value) > 0.35:
                        met.default_value = 0.05
                    rough = node.inputs.get("Roughness")
                    if rough is not None and not rough.links:
                        rough.default_value = max(0.58, min(0.85, float(rough.default_value)))
                    spec = node.inputs.get("Specular IOR Level")
                    if spec is not None and not spec.links:
                        spec.default_value = 0.25


def _ensure_default_material_if_missing(mesh_objs: List[bpy.types.Object]) -> None:
    """Guarantee untextured meshes still render cleanly with a neutral clay Principled BSDF."""
    fallback_mat = None
    for obj in mesh_objs:
        if len(obj.data.materials) == 0 or all(m is None for m in obj.data.materials):
            if fallback_mat is None:
                fallback_mat = bpy.data.materials.new("FoundryDefaultClay")
                fallback_mat.use_nodes = True
                bsdf = fallback_mat.node_tree.nodes.get("Principled BSDF")
                if bsdf:
                    bsdf.inputs["Base Color"].default_value = (0.68, 0.65, 0.60, 1.0)
                    bsdf.inputs["Roughness"].default_value = 0.65
            if len(obj.data.materials) == 0:
                obj.data.materials.append(fallback_mat)
            else:
                obj.data.materials[0] = fallback_mat


def _create_camera_normal_material() -> bpy.types.Material:
    """
    Create a camera/view-space OpenGL (+Y up, #8080FF front-facing) normal shader:
      World Normal -> VectorTransform (WORLD -> CAMERA) -> *(0.5, 0.5, -0.5) + (0.5, 0.5, 0.5)
      -> Emission output.
    Must be rendered under view_transform='Raw' so pixel values are exact linear 8-bit.
    """
    mat = bpy.data.materials.new("FoundryCameraNormal")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    geom = nt.nodes.new("ShaderNodeNewGeometry")
    vtrans = nt.nodes.new("ShaderNodeVectorTransform")
    vtrans.vector_type = "NORMAL"
    vtrans.convert_from = "WORLD"
    vtrans.convert_to = "CAMERA"

    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = (0.5, 0.5, -0.5)

    add = nt.nodes.new("ShaderNodeVectorMath")
    add.operation = "ADD"
    add.inputs[1].default_value = (0.5, 0.5, 0.5)

    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")

    nt.links.new(geom.outputs["Normal"], vtrans.inputs[0])
    nt.links.new(vtrans.outputs[0], mul.inputs[0])
    nt.links.new(mul.outputs[0], add.inputs[0])
    nt.links.new(add.outputs[0], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def _load_image_rgba_bottom_up(path: str) -> Tuple[np.ndarray, int, int]:
    img = bpy.data.images.load(str(path), check_existing=False)
    w, h = int(img.size[0]), int(img.size[1])
    arr = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return arr, w, h


def _save_rgba_bottom_up(arr: np.ndarray, path: str) -> None:
    h, w, _ = arr.shape
    out_path = Path(path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    img = bpy.data.images.new("FoundrySaveTmp", width=w, height=h, alpha=True)
    img.colorspace_settings.name = "Non-Color" if out_path.name.endswith("_normal.png") else "sRGB"
    img.pixels.foreach_set(arr.astype(np.float32).ravel())
    img.filepath_raw = str(out_path)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def _create_qa_studio_stage(bmin: Vector, bmax: Vector) -> List[bpy.types.Object]:
    """Create dark-slate studio floor disc + crisp X/Z axis cues for the QA contact sheet."""
    created: List[bpy.types.Object] = []
    extent = max(bmax.x - bmin.x, bmax.y - bmin.y, bmax.z - bmin.z, 1.0)
    grid_half = extent * 0.85
    step = extent * 0.25

    def add_bar(name: str, p0: Tuple[float, float, float], p1: Tuple[float, float, float], radius: float, color: Tuple[float, float, float, float]) -> None:
        v0 = Vector(p0)
        v1 = Vector(p1)
        vec = v1 - v0
        length = vec.length
        if length < 1e-5:
            return
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(v0 + v1) * 0.5)
        bar = bpy.context.active_object
        bar.name = name
        bar.scale = (length, radius, radius)
        bar.rotation_euler = vec.to_track_quat("X", "Z").to_euler()
        mat = bpy.data.materials.new(f"{name}_Mat")
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Color"].default_value = color
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(em.outputs[0], out.inputs["Surface"])
        bar.data.materials.append(mat)
        created.append(bar)

    # Subtle slate grid lines at Z = 0
    thin = max(0.004 * extent, 0.003)
    grid_col = (0.16, 0.20, 0.30, 1.0)
    n_lines = 4
    for i in range(-n_lines, n_lines + 1):
        offset = i * (grid_half / n_lines)
        if i == 0:
            continue
        add_bar(f"GridX_{i}", (-grid_half, offset, 0.001), (grid_half, offset, 0.001), thin, grid_col)
        add_bar(f"GridY_{i}", (offset, -grid_half, 0.001), (offset, grid_half, 0.001), thin, grid_col)

    # X-axis (red), glTF +Z / Blender -Y front indicator (blue), +Z up tick (green)
    axis_r = thin * 2.0
    add_bar("AxisX", (-grid_half, 0.0, 0.002), (grid_half, 0.0, 0.002), axis_r, (0.75, 0.22, 0.25, 1.0))
    add_bar("AxisFrontZ", (0.0, 0.0, 0.002), (0.0, -grid_half, 0.002), axis_r, (0.25, 0.55, 0.95, 1.0))
    add_bar("AxisBack", (0.0, 0.0, 0.002), (0.0, grid_half, 0.002), thin * 1.4, (0.22, 0.28, 0.42, 1.0))
    return created


def _pick_animation_frames(render_frames: int) -> List[int]:
    """Select 1 or 4 frame indices from the scene's Move or Idle NLA strip (or frame 1)."""
    if render_frames <= 1:
        return [1]

    # Prefer Move, then Idle, then any strip
    chosen_range: Optional[Tuple[int, int]] = None
    for obj in bpy.context.scene.objects:
        if obj.animation_data and obj.animation_data.nla_tracks:
            tracks = list(obj.animation_data.nla_tracks)
            for pref in ("Move", "Idle"):
                for tr in tracks:
                    if tr.name.lower() == pref.lower() and tr.strips:
                        st = tr.strips[0]
                        chosen_range = (int(round(st.frame_start)), int(round(st.frame_end)))
                        break
                if chosen_range:
                    break
            if not chosen_range and tracks and tracks[0].strips:
                st = tracks[0].strips[0]
                chosen_range = (int(round(st.frame_start)), int(round(st.frame_end)))
        if chosen_range:
            break

    if chosen_range and chosen_range[1] > chosen_range[0]:
        f0, f1 = chosen_range
        span = f1 - f0
        return [f0 + int(round((i * span) / float(render_frames))) for i in range(render_frames)]

    return [1] * render_frames


def run_render_views(args: argparse.Namespace) -> None:
    if not args.out_albedo or not args.out_normal or not args.out_qa:
        raise ValueError("--out-albedo, --out-normal, and --out-qa are required in render_views mode.")

    reset_scene()
    import_glb(args.input)
    mesh_objs = collect_mesh_objects()
    if not mesh_objs:
        raise RuntimeError("No MESH objects found in input .glb for render_views.")
    _ensure_default_material_if_missing(mesh_objs)
    _sanitize_foundry_materials(mesh_objs)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.display_settings.display_device = "sRGB"

    cam_data = bpy.data.cameras.new("FoundryCamData")
    cam_obj = bpy.data.objects.new("FoundryCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    _setup_lighting_and_world((0.55, 0.58, 0.65, 1.0), ambient_strength=0.95)

    frames = _pick_animation_frames(args.render_frames)
    # Supersample cell resolution cleanly for downstream 1x pixel-grid snap
    cell_res = max(512, min(1024, args.size * 4))

    # Compute unified AABB across all rendered frames so framing never pops across frames
    bmin_all = Vector((1e9, 1e9, 1e9))
    bmax_all = Vector((-1e9, -1e9, -1e9))
    for f in set(frames):
        scene.frame_set(f)
        bmin_f, bmax_f = _scene_mesh_bounds()
        bmin_all.x = min(bmin_all.x, bmin_f.x)
        bmin_all.y = min(bmin_all.y, bmin_f.y)
        bmin_all.z = min(bmin_all.z, bmin_f.z)
        bmax_all.x = max(bmax_all.x, bmax_f.x)
        bmax_all.y = max(bmax_all.y, bmax_f.y)
        bmax_all.z = max(bmax_all.z, bmax_f.z)

    # ------------------------------------------------------------------
    # 1. Albedo Render (26-deg isometric/HD-2D front view, transparent bg)
    # ------------------------------------------------------------------
    _configure_ortho_camera(cam_obj, bmin_all, bmax_all, azimuth_deg=0.0, pitch_down_deg=26.0, margin=1.12)
    scene.render.resolution_x = cell_res
    scene.render.resolution_y = cell_res
    scene.render.film_transparent = True
    scene.eevee.taa_render_samples = 8
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0

    albedo_frames: List[np.ndarray] = []
    out_albedo = Path(args.out_albedo)
    out_albedo.parent.mkdir(parents=True, exist_ok=True)
    tmp_frame_path = str(out_albedo.with_name(f".{out_albedo.stem}_tmp_frame.png"))

    for f in frames:
        scene.frame_set(f)
        scene.render.filepath = tmp_frame_path
        bpy.ops.render.render(write_still=True)
        arr, _, _ = _load_image_rgba_bottom_up(tmp_frame_path)
        albedo_frames.append(arr)

    albedo_strip = np.concatenate(albedo_frames, axis=1) if len(albedo_frames) > 1 else albedo_frames[0]
    _save_rgba_bottom_up(albedo_strip, str(out_albedo))

    # ------------------------------------------------------------------
    # 2. View-Space OpenGL (+Y up) Normal Map Render
    # ------------------------------------------------------------------
    orig_mats: Dict[str, List[Optional[bpy.types.Material]]] = {}
    for obj in mesh_objs:
        orig_mats[obj.name] = list(obj.data.materials)

    normal_mat = _create_camera_normal_material()
    for obj in mesh_objs:
        if len(obj.data.materials) == 0:
            obj.data.materials.append(normal_mat)
        else:
            for i in range(len(obj.data.materials)):
                obj.data.materials[i] = normal_mat

    scene.eevee.taa_render_samples = 1
    scene.view_settings.view_transform = "Raw"
    scene.view_settings.look = "None"

    normal_frames: List[np.ndarray] = []
    out_normal = Path(args.out_normal)
    out_normal.parent.mkdir(parents=True, exist_ok=True)

    for f in frames:
        scene.frame_set(f)
        scene.render.filepath = tmp_frame_path
        bpy.ops.render.render(write_still=True)
        arr, _, _ = _load_image_rgba_bottom_up(tmp_frame_path)
        # Set transparent background pixels to exact neutral normal #8080FF (128/255, 128/255, 1.0)
        bg_mask = arr[..., 3] < 0.5
        arr[bg_mask, 0] = 128.0 / 255.0
        arr[bg_mask, 1] = 128.0 / 255.0
        arr[bg_mask, 2] = 1.0
        arr[bg_mask, 3] = 0.0
        normal_frames.append(arr)

    normal_strip = np.concatenate(normal_frames, axis=1) if len(normal_frames) > 1 else normal_frames[0]
    # Save directly as linear PNG via PIL-free raw float -> sRGB raw pass
    out_img = bpy.data.images.new("FoundryNormalOut", width=normal_strip.shape[1], height=normal_strip.shape[0], alpha=True)
    out_img.colorspace_settings.name = "sRGB"
    out_img.pixels.foreach_set(normal_strip.astype(np.float32).ravel())
    out_img.filepath_raw = str(out_normal)
    out_img.file_format = "PNG"
    out_img.save()
    bpy.data.images.remove(out_img)

    # Restore original materials for QA turntable render
    for obj in mesh_objs:
        mats = orig_mats.get(obj.name, [])
        obj.data.materials.clear()
        for m in mats:
            obj.data.materials.append(m)

    # ------------------------------------------------------------------
    # 3. 2x2 QA Turntable Contact Sheet (1024x1024 total, 512x512 each)
    # ------------------------------------------------------------------
    scene.frame_set(frames[0])
    stage_objs = _create_qa_studio_stage(bmin_all, bmax_all)

    # Dark-slate studio backdrop (#1C2033 in linear ~0.011, 0.014, 0.033)
    world_bg = scene.world.node_tree.nodes.get("Background")
    if world_bg:
        world_bg.inputs["Color"].default_value = (0.012, 0.015, 0.034, 1.0)
        world_bg.inputs["Strength"].default_value = 0.9

    scene.render.film_transparent = False
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.eevee.taa_render_samples = 8
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = 0.0

    qa_views = [
        ("Front", 0.0, 26.0, 512, 0),       # Top-Left in bottom-up coords
        ("Hero45", -45.0, 26.0, 512, 512), # Top-Right
        ("Side", 90.0, 20.0, 0, 0),        # Bottom-Left
        ("Back", 180.0, 26.0, 0, 512),     # Bottom-Right
    ]

    qa_canvas = np.zeros((1024, 1024, 4), dtype=np.float32)
    for _, azim, pitch, row_off, col_off in qa_views:
        _configure_ortho_camera(cam_obj, bmin_all, bmax_all, azimuth_deg=azim, pitch_down_deg=pitch, margin=1.18)
        scene.render.filepath = tmp_frame_path
        bpy.ops.render.render(write_still=True)
        q_arr, _, _ = _load_image_rgba_bottom_up(tmp_frame_path)
        qa_canvas[row_off : row_off + 512, col_off : col_off + 512] = q_arr

    # Draw subtle 2px divider lines between quadrants
    div_col = np.array([0.25, 0.30, 0.45, 1.0], dtype=np.float32)
    qa_canvas[511:513, :] = div_col
    qa_canvas[:, 511:513] = div_col

    out_qa = Path(args.out_qa)
    out_qa.parent.mkdir(parents=True, exist_ok=True)
    qa_img = bpy.data.images.new("FoundryQASheet", width=1024, height=1024, alpha=True)
    qa_img.colorspace_settings.name = "sRGB"
    qa_img.pixels.foreach_set(qa_canvas.ravel())
    qa_img.filepath_raw = str(out_qa)
    qa_img.file_format = "PNG"
    qa_img.save()
    bpy.data.images.remove(qa_img)

    # Remove temporary frame file and stage objects
    for s_obj in stage_objs:
        bpy.data.objects.remove(s_obj, do_unlink=True)
    if os.path.exists(tmp_frame_path):
        os.remove(tmp_frame_path)

    print(
        json.dumps(
            {
                "status": "ok",
                "mode": "render_views",
                "out_albedo": str(out_albedo),
                "out_normal": str(out_normal),
                "out_qa": str(out_qa),
                "frames": len(frames),
                "cell_res": cell_res,
            },
            indent=2,
        )
    )


def main() -> None:
    args = parse_args()
    if args.mode == "normalize_rig":
        run_normalize_rig(args)
    elif args.mode == "render_views":
        run_render_views(args)


if __name__ == "__main__":
    main()
