"""
Sprint 1 Phase-1 Cartography Sprite Re-Roll (4 Defective Assets).

Regenerates onto C:/realmz/remaster/RealmzUnity/Assets/Art/Cartography/ on RTX 5060 Ti 8 GB:
  1. trees/tree_willow_a.png + _n.png (256x256, #FF00FF chroma, textured emerald willow drapes)
  2. trees/tree_oak_b.png + _n.png (224x224, #FF00FF chroma, lush twin-lobed oak clusters)
  3. structures/stone_fence_v.png + _n.png (128x128, #FF00FF chroma, continuous receding N-S wall)
  4. structures/stone_bridge.png + _n.png (256x192, #FF00FF chroma, 3/4 oblique N-S stone bridge)

Enforces Sprint 1 automated QA gates (flat_blob_max_square < 10, sky_bleed_pixels == 0,
off_palette_colors == 0, alpha_is_1bit, max_color_frac < 0.42, deep_shadow_pct >= 8%),
auto-retries up to 3 seeds per asset, regenerates phase1_assets_contact_sheet.png, and shuts
down ComfyUI cleanly in finally: until idle VRAM < 1000 MiB.

Zero Unicode em-dashes or en-dashes. Passes CREATE_NO_WINDOW = 0x08000000 in all subprocesses.
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
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
import requests
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

CART_DIR = Path(__file__).resolve().parent
PIPE_DIR = CART_DIR.parent
sys.path.insert(0, str(CART_DIR))
sys.path.insert(0, str(PIPE_DIR))

import generate_cartography_slice as gs  # noqa: E402
import pixel_postprocess as pp  # noqa: E402

CREATE_NO_WINDOW = 0x08000000
BLENDER_EXE = Path(r"C:\realmz\tools\blender\blender-4.5.9-windows-x64\blender.exe")
COMFY_PYTHON = Path(r"C:\ComfyUI\.venv\Scripts\python.exe")
COMFY_MAIN = Path(r"C:\ComfyUI\main.py")
COMFY_ROOT = Path(r"C:\ComfyUI")
SERVER_URL = "http://127.0.0.1:8188"

GUIDE_DIR = gs.GUIDE_DIR
RAW_DIR = gs.RAW_DIR
STYLE_DIR = gs.STYLE_DIR
UNITY_CART_ROOT = gs.UNITY_CART_ROOT

# Magenta chroma key (255, 0, 255) for clean branch-gap & arch-void cutout
MAGENTA = (255, 0, 255)

TREE_NEG_CHROMA = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, chibi, "
    "flat color, uniform fill, MS paint, vector art, simple blobs, cartoon shapes, "
    "yellow blotches, yellow leaves, orange blotches, sky, blue sky, clouds, water, "
    "forest, woods, multiple trees, scenery, landscape, horizon, ground, grass patch, "
    "dirt mound, soil base, pedestal, oval base, cast shadow, drop shadow, text, watermark, "
    "white background, white border, lowres, worst quality"
)

STRUCT_NEG_CHROMA = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, "
    "flat vector boxes, white background, grey background, forest background, sky, grass ground, "
    "water, river, tombstone, gravestone, headstone, obelisk, Greek pillar, marble column, "
    "pedestal, isometric cube, dirt block, cast shadow, drop shadow, humans, text, watermark, lowres"
)

STRUCT_NEG_WHITE = (
    "blurry, anti-aliased, 3d render, photorealistic, smooth gradient, painterly, "
    "flat vector boxes, magenta, pink, purple, turquoise, cyan, aqua, teal, neon blue, "
    "wireframe, hollow, holes, tombstone, gravestone, headstone, obelisk, Greek pillar, "
    "marble column, pedestal, isometric cube, forest, sky, water, grass ground, "
    "cast shadow, drop shadow, humans, text, watermark, lowres"
)


# ---------------------------------------------------------------------------
# Headless ComfyUI Lifecycle (VRAM-safe)
# ---------------------------------------------------------------------------

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

    log_path = gs.SCRATCH_ROOT / "comfyui_sprint1_8188.log"
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


def teardown_comfy(spawned_proc: Optional[subprocess.Popen]) -> None:
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

    deadline = time.time() + 10.0
    while time.time() < deadline:
        v = gs.query_vram_mib()
        if 0 <= v < 1000:
            break
        time.sleep(0.5)
    print(f"[ComfyUI] Post-teardown GPU VRAM: {gs.query_vram_mib()} MiB", flush=True)


# ---------------------------------------------------------------------------
# Blender 4.5.9 Depth + Canny Guides for N-S Stone Bridge & Continuous Wall
# ---------------------------------------------------------------------------

def render_ns_structure_guides() -> Dict[str, Tuple[Path, Path]]:
    """
    Runs a temporary inner Blender script cleanly to render 3/4 oblique (26 deg pitch,
    -24 deg yaw) Depth + Canny guides for North-South stone_bridge and continuous stone_fence_v.
    """
    if not BLENDER_EXE.exists():
        raise FileNotFoundError(f"Blender executable not found: {BLENDER_EXE}")

    GUIDE_DIR.mkdir(parents=True, exist_ok=True)
    inner_script = """
import math, sys
from pathlib import Path
import bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
raw_dir = Path(argv[0])
raw_dir.mkdir(parents=True, exist_ok=True)

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def make_mat(name, col):
    m = bpy.data.materials.new(name=name)
    m.diffuse_color = (col[0], col[1], col[2], 1.0)
    return m

def add_bevel(obj, w=0.02, segs=1):
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = w
    mod.segments = segs
    mod.limit_method = "ANGLE"

def add_box(name, size, loc, rot_deg=(0,0,0), mat=None, bevel=0.02, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    obj.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    if mat: obj.data.materials.append(mat)
    if bevel > 0: add_bevel(obj, bevel, 1)
    if parent: obj.parent = parent
    return obj

def setup_cam(ortho_scale, target, pitch_deg=32.0, yaw_deg=-22.0):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.cavity_ridge_factor = 1.6
    scene.display.shading.cavity_valley_factor = 1.8
    scene.view_layers[0].use_pass_z = True

    cd = bpy.data.cameras.new("OrthoCam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho_scale
    co = bpy.data.objects.new("OrthoCam", cd)
    bpy.context.collection.objects.link(co)
    scene.camera = co

    pr, yr, dist = math.radians(pitch_deg), math.radians(yaw_deg), 20.0
    dx = dist * math.cos(pr) * math.sin(yr)
    dy = -dist * math.cos(pr) * math.cos(yr)
    dz = dist * math.sin(pr)
    co.location = (target[0] + dx, target[1] + dy, target[2] + dz)
    d = Vector(target) - co.location
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

def render_stem(stem):
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
    scene.render.filepath = str(raw_dir / f"{stem}_rgba.png")
    bpy.ops.render.render(write_still=True)

    import numpy as np
    exr = raw_dir / f"{stem}_depth_0001.exr"
    img = bpy.data.images.load(str(exr), check_existing=False)
    w, h = img.size[0], img.size[1]
    arr = np.flipud(np.array(img.pixels[:], dtype=np.float32).reshape((h, w, 4)))
    np.save(str(raw_dir / f"{stem}_depth.npy"), arr[..., 0])
    bpy.data.images.remove(img)
    exr.unlink(missing_ok=True)

# 1. Continuous N-S Stone Fence segment seen straight 3/4 oblique (yaw=0 matches camera so tiled segments align vertically)
clear_scene()
m_stone = make_mat("WallStone", (0.58, 0.56, 0.53))
m_dark = make_mat("WallDark", (0.34, 0.35, 0.42))
add_box("Base", (0.44, 1.12, 0.14), (0, 0, 0.07), mat=m_dark, bevel=0.015)
add_box("SolidCore", (0.40, 1.08, 0.38), (0, 0, 0.28), mat=m_dark, bevel=0.015)
for cz, y_specs in [
    (0.16, [(-0.36, 0.36), (0.01, 0.34), (0.37, 0.35)]),
    (0.30, [(-0.42, 0.25), (-0.12, 0.32), (0.20, 0.30), (0.44, 0.21)]),
    (0.44, [(-0.35, 0.38), (0.03, 0.35), (0.38, 0.33)]),
]:
    for i, (by, blen) in enumerate(y_specs):
        add_box(f"B_{cz}_{i}", (0.44, blen, 0.12), (0, by, cz), mat=m_stone, bevel=0.018)
for i, (cy, clen) in enumerate([(-0.37, 0.35), (0.0, 0.36), (0.37, 0.35)]):
    add_box(f"Cap_{i}", (0.48, clen, 0.09), (0, cy, 0.55), mat=m_dark, bevel=0.016)
setup_cam(ortho_scale=2.2, target=(0.0, 0.0, 0.30), pitch_deg=46.0, yaw_deg=0.0)
render_stem("stone_fence_v_ns")

# 2. North-South Arched Stone Bridge seen 3/4 oblique
clear_scene()
m_deck = make_mat("Deck", (0.62, 0.60, 0.56))
m_arch = make_mat("Arch", (0.45, 0.47, 0.54))
m_bmoss = make_mat("BridgeMoss", (0.30, 0.52, 0.30))
mesh = bpy.data.meshes.new("BridgeNS_mesh")
bm = bmesh.new()
n_seg = 16
half_l = 1.65
arch_r = 0.85
half_w = 0.88
top_pts, bot_pts = [], []
for i in range(n_seg + 1):
    t = i / n_seg
    y = -half_l + t * (2.0 * half_l)
    zt = 0.36 + 0.52 * math.sin(math.pi * t)
    zb = math.sqrt(max(0.0, arch_r * arch_r - y * y)) * 0.78 if abs(y) <= arch_r else 0.0
    top_pts.append((y, zt))
    bot_pts.append((y, zb))
l_top = [bm.verts.new((-half_w, y, z)) for y, z in top_pts]
l_bot = [bm.verts.new((-half_w, y, z)) for y, z in bot_pts]
r_top = [bm.verts.new((half_w, y, z)) for y, z in top_pts]
r_bot = [bm.verts.new((half_w, y, z)) for y, z in bot_pts]
for i in range(n_seg):
    bm.faces.new((l_bot[i], l_bot[i + 1], l_top[i + 1], l_top[i]))
    bm.faces.new((r_top[i], r_top[i + 1], r_bot[i + 1], r_bot[i]))
    bm.faces.new((l_top[i], l_top[i + 1], r_top[i + 1], r_top[i]))
    bm.faces.new((r_bot[i], r_bot[i + 1], l_bot[i + 1], l_bot[i]))
bm.faces.new((r_top[0], r_bot[0], l_bot[0], l_top[0]))
bm.faces.new((l_top[-1], l_bot[-1], r_bot[-1], r_top[-1]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()
bo = bpy.data.objects.new("BridgeBody", mesh)
bpy.context.collection.objects.link(bo)
bo.data.materials.append(m_deck)
add_bevel(bo, 0.02, 1)
for i in range(10):
    t = (i + 0.5) / 10.0
    py = -1.48 + t * 2.96
    pz = 0.36 + 0.52 * math.sin(math.pi * t) + 0.24
    slope = math.degrees(math.atan2(0.52 * math.pi * math.cos(math.pi * t) / 3.3, 1.0))
    add_box(f"PL_{i}", (0.20, 0.28, 0.44), (-half_w + 0.10, py, pz), rot_deg=(slope, 0, 0), mat=m_arch)
    add_box(f"PR_{i}", (0.20, 0.28, 0.44), ( half_w - 0.10, py, pz), rot_deg=(slope, 0, 0), mat=m_arch)
for sx in (-half_w + 0.08, half_w - 0.08):
    for sy in (-1.58, 1.58):
        add_box(f"Bol_{sx}_{sy}", (0.30, 0.30, 0.80), (sx, sy, 0.40), mat=m_arch)
        add_box(f"Cap_{sx}_{sy}", (0.34, 0.34, 0.12), (sx, sy, 0.82), mat=m_bmoss)
setup_cam(ortho_scale=4.2, target=(0.0, 0.0, 0.45), pitch_deg=40.0, yaw_deg=-6.0)
render_stem("stone_bridge_ns")
"""

    results: Dict[str, Tuple[Path, Path]] = {}
    with tempfile.TemporaryDirectory(prefix="realmz_sprint1_blender_") as td:
        td_p = Path(td)
        bpy_file = td_p / "render_ns_guides.py"
        bpy_file.write_text(inner_script, encoding="utf-8")
        raw_out = td_p / "raw"
        proc = subprocess.run(
            [
                str(BLENDER_EXE),
                "-b",
                "--python-exit-code",
                "1",
                "--python",
                str(bpy_file),
                "--",
                str(raw_out),
            ],
            capture_output=True,
            text=True,
            timeout=120,
            creationflags=CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        if proc.returncode != 0:
            raise RuntimeError(f"Blender failed: {proc.stderr[-800:]}")

        import blender_whitebox_guides as bwg  # noqa: E402
        for stem in ("stone_fence_v_ns", "stone_bridge_ns"):
            info = bwg._convert_raw_to_guides(stem, raw_out, GUIDE_DIR)
            results[stem] = (Path(info["depth"]), Path(info["canny"]))
    return results


def make_magenta_chroma_guide_from_depth(
    name: str,
    depth_path: Path,
    fill_kind: str,
    size: int = 1024,
) -> Path:
    """Create a solid #FF00FF chroma init RGB image aligned 1:1 with a Blender depth silhouette."""
    d_arr = np.array(Image.open(depth_path).convert("L"))
    if d_arr.shape[:2] != (size, size):
        d_arr = cv2.resize(d_arr, (size, size), interpolation=cv2.INTER_NEAREST)
    fg = d_arr > 20
    rgb = np.full((size, size, 3), MAGENTA, dtype=np.uint8)
    rng = np.random.default_rng(701 if fill_kind == "bridge" else 702)

    # Cool blue-slate cobblestones (#586380) blending into warm cobble (#87817A) matching stone_fence_h
    yy, xx = np.mgrid[0:size, 0:size]
    noise = cv2.GaussianBlur(rng.normal(0.0, 1.0, (size, size)).astype(np.float32), (0, 0), 8.0)
    shades = d_arr.astype(np.float32) / 255.0

    r = np.clip(64.0 + shades * 78.0 + noise * 18.0, 42, 184)
    g = np.clip(72.0 + shades * 76.0 + noise * 16.0, 46, 180)
    b = np.clip(92.0 + shades * 68.0 + noise * 15.0, 62, 192)

    if fill_kind == "wall":
        # Blend warm cobble highlights (#87817A) on upper facets and dark ashlar mortar courses
        warm = (shades > 0.56) & (noise > -0.15)
        r[warm] = np.clip(r[warm] + 22.0, 48, 186)
        g[warm] = np.clip(g[warm] + 12.0, 48, 178)
        b[warm] = np.clip(b[warm] - 10.0, 56, 174)
        row_h = 54
        col_w = 96
        row_idx = yy // row_h
        x_shifted = xx + (row_idx % 2) * (col_w // 2)
        mortar = ((yy % row_h) < 5) | ((x_shifted % col_w) < 5)
        r[mortar] *= 0.52
        g[mortar] *= 0.52
        b[mortar] *= 0.58

    # Tiny subtle moss specks (<2% area) only when fill_kind != "wall" or extreme peaks
    moss = (noise > (1.45 if fill_kind == "wall" else 0.55)) & (shades > 0.48)
    r[moss] = 66.0
    g[moss] = 102.0
    b[moss] = 67.0

    rgb[fg, 0] = r[fg].astype(np.uint8)
    rgb[fg, 1] = g[fg].astype(np.uint8)
    rgb[fg, 2] = b[fg].astype(np.uint8)

    out_p = GUIDE_DIR / f"{name}_chroma_init.png"
    Image.fromarray(rgb, mode="RGB").save(out_p)
    return out_p


def make_white_stone_guide_from_depth(
    name: str,
    depth_path: Path,
    fill_kind: str,
    size: int = 1024,
) -> Path:
    """Create a solid pure-white #FFFFFF backdrop studio init guide with warm cobblestone & mortar variation."""
    d_arr = np.array(Image.open(depth_path).convert("L"))
    if d_arr.shape[:2] != (size, size):
        d_arr = cv2.resize(d_arr, (size, size), interpolation=cv2.INTER_NEAREST)
    fg = d_arr > 20
    rgb = np.full((size, size, 3), (255, 255, 255), dtype=np.uint8)
    rng = np.random.default_rng(711 if fill_kind == "bridge" else 712)

    yy, xx = np.mgrid[0:size, 0:size]
    noise = cv2.GaussianBlur(rng.normal(0.0, 1.0, (size, size)).astype(np.float32), (0, 0), 7.0)
    shades = d_arr.astype(np.float32) / 255.0

    # Warm medieval masonry base (#6B6661 -> #87817A -> #A49E95 + subtle #AA8D70 sandstone warmth)
    r = np.clip(92.0 + shades * 80.0 + noise * 16.0, 62, 198)
    g = np.clip(86.0 + shades * 74.0 + noise * 15.0, 58, 190)
    b = np.clip(81.0 + shades * 68.0 + noise * 14.0, 55, 182)

    # Highlight sunlit upper stone facets (#A49E95 / #AA8D70)
    sunlit = (shades > 0.66) & (noise > 0.10)
    r[sunlit] = np.clip(r[sunlit] + 18.0, 62, 212)
    g[sunlit] = np.clip(g[sunlit] + 12.0, 58, 198)
    b[sunlit] = np.clip(b[sunlit] + 2.0, 55, 186)

    # Olive moss patches (#426643) along upper crevices
    moss = (noise > 0.34) & (shades > 0.48)
    r[moss] = 66.0
    g[moss] = 102.0
    b[moss] = 67.0

    # Ashlar cobblestone mortar courses (#3B3837) + per-stone shading so bollards stay crisp and textured
    row_h = 46 if fill_kind == "wall" else 42
    col_w = 88 if fill_kind == "wall" else 78
    row_idx = yy // row_h
    x_shifted = xx + (row_idx % 2) * (col_w // 2)
    col_idx = x_shifted // col_w
    brick_jitter = (((row_idx * 37 + col_idx * 73) % 9) - 4).astype(np.float32) * 5.0
    fine_grain = rng.normal(0.0, 4.5, (size, size)).astype(np.float32)
    r = np.clip(r + brick_jitter + fine_grain, 50, 215)
    g = np.clip(g + brick_jitter + fine_grain, 48, 205)
    b = np.clip(b + brick_jitter * 0.9 + fine_grain, 46, 198)
    mortar = ((yy % row_h) < 4) | ((x_shifted % col_w) < 4)
    r[mortar] = 59.0
    g[mortar] = 56.0
    b[mortar] = 55.0

    rgb[fg, 0] = r[fg].astype(np.uint8)
    rgb[fg, 1] = g[fg].astype(np.uint8)
    rgb[fg, 2] = b[fg].astype(np.uint8)

    out_p = GUIDE_DIR / f"{name}_white_init.png"
    Image.fromarray(rgb, mode="RGB").save(out_p)
    return out_p


def _interior_blur_on_magenta(canvas_rgb: np.ndarray, fg_mask: np.ndarray, sigma: float = 7.0) -> np.ndarray:
    """Gaussian blur interior RGB only via normalized convolution so #FF00FF backdrop stays razor-clean."""
    fg_f = fg_mask.astype(np.float32)
    denom = cv2.GaussianBlur(fg_f, (0, 0), sigmaX=sigma, sigmaY=sigma)
    blurred = np.empty_like(canvas_rgb, dtype=np.float32)
    for c in range(3):
        num = cv2.GaussianBlur(canvas_rgb[..., c].astype(np.float32) * fg_f, (0, 0), sigmaX=sigma, sigmaY=sigma)
        blurred[..., c] = np.where(denom > 1e-4, num / np.maximum(denom, 1e-4), 0.0)
    out = np.full_like(canvas_rgb, MAGENTA, dtype=np.uint8)
    out[fg_mask] = np.clip(np.round(blurred[fg_mask]), 0, 255).astype(np.uint8)
    return out


def paint_chroma_oak_b(size: int = 1024, seed: int = 801) -> Tuple[Path, Path]:
    """Rich multi-tone emerald/moss leaf-clustered oak guide on solid #FF00FF magenta chroma."""
    rng = np.random.default_rng(seed)
    rgb = np.full((size, size, 3), MAGENTA, dtype=np.uint8)
    fg = np.zeros((size, size), dtype=bool)
    depth = np.zeros((size, size), dtype=np.float32)
    draw = ImageDraw.Draw(Image.fromarray(rgb))
    cx = size // 2

    # Gnarled flared oak trunk + boughs
    trunk_pts = [(cx - 96, 895), (cx - 56, 760), (cx - 46, 520), (cx + 46, 520), (cx + 58, 760), (cx + 98, 895)]
    trunk_mask = np.zeros((size, size), dtype=np.uint8)
    cv2.fillPoly(trunk_mask, [np.array(trunk_pts, np.int32)], 255)
    # Two diagonal main branches
    cv2.line(trunk_mask, (cx - 10, 560), (cx - 185, 420), 255, 48)
    cv2.line(trunk_mask, (cx + 12, 550), (cx + 185, 430), 255, 46)
    t_m = trunk_mask > 0
    fg |= t_m
    depth[t_m] = 0.56
    yy, xx = np.mgrid[0:size, 0:size]
    bark_wave = np.sin(xx * 0.28 + np.sin(yy * 0.04) * 1.4) > 0.25
    left_lit = xx < cx + 10
    rgb[t_m & left_lit] = [136, 86, 52]
    rgb[t_m & left_lit & bark_wave] = [98, 60, 36]
    rgb[t_m & (~left_lit)] = [78, 48, 30]
    rgb[t_m & (~left_lit) & bark_wave] = [44, 26, 17]

    # Twin-lobed dappled leaf clusters (380 crisp leaf clusters, dark emerald core -> sunlit upper-left tips)
    lobes = [
        (cx - 190, 420, 175), (cx + 190, 440, 170),
        (cx - 135, 290, 165), (cx + 135, 305, 160),
        (cx, 225, 175), (cx, 395, 185),
    ]
    canopy_mask = np.zeros((size, size), dtype=np.uint8)
    canopy_col = np.zeros((size, size, 3), dtype=np.uint8)

    # First paint deep shadow inner pockets
    for l_cx, l_cy, l_r in lobes:
        cv2.circle(canopy_mask, (l_cx, l_cy), int(l_r * 0.88), 255, -1)
        cv2.circle(canopy_col, (l_cx, l_cy), int(l_r * 0.88), (24, 54, 34), -1)
        cv2.circle(depth, (l_cx, l_cy), int(l_r * 0.88), 0.72, -1)

    # Add 360 high-frequency mottled individual leaf clusters (dark/mid/sunlit emerald, zero flat neon!)
    for _ in range(360):
        l_cx, l_cy, l_r = lobes[int(rng.integers(0, len(lobes)))]
        ang = rng.uniform(0.0, 2.0 * math.pi)
        rad = math.sqrt(rng.uniform(0.0, 1.0)) * l_r * 0.94
        lx = int(l_cx + rad * math.cos(ang))
        ly = int(l_cy + rad * math.sin(ang))
        lr = int(rng.integers(14, 34))
        lit = (cx - lx) * 0.55 + (420 - ly) * 0.85 + rng.normal(0.0, 45.0)
        if lit > 135:
            col = (int(rng.integers(86, 118)), int(rng.integers(172, 206)), int(rng.integers(84, 108)))
            dz = 0.94
        elif lit > 10:
            col = (int(rng.integers(48, 72)), int(rng.integers(126, 158)), int(rng.integers(64, 82)))
            dz = 0.84
        else:
            col = (int(rng.integers(24, 42)), int(rng.integers(68, 96)), int(rng.integers(40, 56)))
            dz = 0.74
        cv2.circle(canopy_mask, (lx, ly), lr, 255, -1)
        cv2.circle(canopy_col, (lx, ly), lr, col, -1)
        cv2.circle(depth, (lx, ly), lr, dz, -1)

    c_m = canopy_mask > 0
    # Keep a few intentional magenta gap pockets near outer boughs so tree feels airy
    fg = (fg | c_m)
    rgb[c_m] = canopy_col[c_m]
    rgb[~fg] = MAGENTA

    init_rgb = _interior_blur_on_magenta(rgb, fg, sigma=7.0)
    d_u8 = np.clip(depth * 255.0, 0, 255).astype(np.uint8)
    d_im = Image.fromarray(np.stack([d_u8, d_u8, d_u8], axis=-1), "RGB").filter(ImageFilter.GaussianBlur(radius=12.0))

    p_init = GUIDE_DIR / "tree_oak_b_chroma_init.png"
    p_depth = GUIDE_DIR / "tree_oak_b_chroma_depth.png"
    Image.fromarray(init_rgb, "RGB").save(p_init)
    d_im.save(p_depth)
    return p_init, p_depth


def paint_chroma_willow_a(size: int = 1024, seed: int = 802) -> Tuple[Path, Path]:
    """Textured weeping willow guide with deep emerald shadows and shaggy leaf curtains on #FF00FF."""
    rng = np.random.default_rng(seed)
    rgb = np.full((size, size, 3), MAGENTA, dtype=np.uint8)
    fg_trunk = np.zeros((size, size), dtype=np.uint8)
    fg_canopy = np.zeros((size, size), dtype=np.uint8)
    col_canopy = np.zeros((size, size, 3), dtype=np.uint8)
    depth = np.zeros((size, size), dtype=np.float32)
    cx = size // 2

    # Broad gnarled trunk + upper boughs lifted into y=400 so crown center never gets a donut hole
    pts = [(cx - 118, 910), (cx - 64, 730), (cx - 56, 425), (cx + 56, 425), (cx + 68, 730), (cx + 120, 910)]
    cv2.fillPoly(fg_trunk, [np.array(pts, np.int32)], 255)
    cv2.line(fg_trunk, (cx - 12, 470), (cx - 135, 395), 255, 44)
    cv2.line(fg_trunk, (cx + 12, 470), (cx + 135, 400), 255, 44)
    t_m = fg_trunk > 0
    depth[t_m] = 0.56
    yy, xx = np.mgrid[0:size, 0:size]
    bark_wave = np.sin(xx * 0.28 + np.sin(yy * 0.04) * 1.4) > 0.25
    rgb[t_m & (xx < cx + 8)] = [126, 82, 48]
    rgb[t_m & (xx < cx + 8) & bark_wave] = [92, 56, 34]
    rgb[t_m & (xx >= cx + 8)] = [74, 46, 28]
    rgb[t_m & (xx >= cx + 8) & bark_wave] = [44, 26, 17]

    # Fill hollow center gap above trunk (ly ~ 340..480, cx - 75..cx + 75) with deep emerald shadow leaf clusters
    for _ in range(95):
        lx = int(cx + rng.integers(-75, 76))
        ly = int(rng.integers(340, 481))
        lr = int(rng.integers(18, 36))
        if rng.random() < 0.55:
            col = (24, 54, 34)
            dz = 0.72
        else:
            col = (38, 84, 50)
            dz = 0.78
        cv2.circle(fg_canopy, (lx, ly), lr, 255, -1)
        cv2.circle(col_canopy, (lx, ly), lr, col, -1)
        cv2.circle(depth, (lx, ly), lr, dz, -1)

    # Deep emerald upper dome underpainting + speckled sunlit leaf clusters
    for _ in range(380):
        ang = rng.uniform(0.0, 2.0 * math.pi)
        rad = math.sqrt(rng.uniform(0.0, 1.0))
        lx = int(cx + rad * 355.0 * math.cos(ang))
        ly = int(310 + rad * 180.0 * math.sin(ang))
        lr = int(rng.integers(14, 34))
        lit = (cx - lx) * 0.45 + (350 - ly) * 0.90 + rng.normal(0.0, 48.0)
        if lit > 125:
            col = (int(rng.integers(88, 124)), int(rng.integers(170, 202)), int(rng.integers(82, 106)))
            dz = 0.94
        elif lit > -15:
            col = (int(rng.integers(46, 72)), int(rng.integers(122, 152)), int(rng.integers(64, 84)))
            dz = 0.85
        else:
            col = (int(rng.integers(24, 40)), int(rng.integers(64, 92)), int(rng.integers(42, 58)))
            dz = 0.74
        cv2.circle(fg_canopy, (lx, ly), lr, 255, -1)
        cv2.circle(col_canopy, (lx, ly), lr, col, -1)
        cv2.circle(depth, (lx, ly), lr, dz, -1)

    # Organic hanging weeping vine curtains with leaf speckles & alternating depth
    for vx_base in range(cx - 335, cx + 336, 26):
        if abs(vx_base - cx) < 52 and rng.random() < 0.72:
            continue
        length = int(rng.integers(230, 445))
        is_front = rng.random() > 0.35
        for step_y in range(340, 340 + length, 18):
            wiggle = int(rng.integers(-11, 12))
            rx = int(rng.integers(10, 18))
            ry = int(rng.integers(12, 22))
            if is_front and vx_base < cx + 80 and step_y < 590:
                col = (int(rng.integers(62, 98)), int(rng.integers(148, 184)), int(rng.integers(70, 92)))
                dz = 0.88
            elif vx_base < cx + 140:
                col = (int(rng.integers(38, 62)), int(rng.integers(106, 138)), int(rng.integers(58, 76)))
                dz = 0.80
            else:
                col = (int(rng.integers(22, 38)), int(rng.integers(64, 92)), int(rng.integers(44, 58)))
                dz = 0.72
            cv2.ellipse(fg_canopy, (vx_base + wiggle, step_y), (rx, ry), 0, 0, 360, 255, -1)
            cv2.ellipse(col_canopy, (vx_base + wiggle, step_y), (rx, ry), 0, 0, 360, col, -1)
            cv2.ellipse(depth, (vx_base + wiggle, step_y), (rx, ry), 0, 0, 360, dz, -1)

    c_m = fg_canopy > 0
    fg = t_m | c_m
    rgb[c_m] = col_canopy[c_m]
    rgb[~fg] = MAGENTA

    init_rgb = _interior_blur_on_magenta(rgb, fg, sigma=7.0)
    d_u8 = np.clip(depth * 255.0, 0, 255).astype(np.uint8)
    d_im = Image.fromarray(np.stack([d_u8, d_u8, d_u8], axis=-1), "RGB").filter(ImageFilter.GaussianBlur(radius=12.0))

    p_init = GUIDE_DIR / "tree_willow_a_chroma_init.png"
    p_depth = GUIDE_DIR / "tree_willow_a_chroma_depth.png"
    Image.fromarray(init_rgb, "RGB").save(p_init)
    d_im.save(p_depth)
    return p_init, p_depth


def check_sprint1_qa(out_png: Path, stats: dict, is_tree: bool) -> Tuple[bool, str]:
    arr = np.array(Image.open(out_png).convert("RGBA"))
    op = arr[..., 3] == 255
    n_op = int(np.sum(op))
    min_op = 2000 if (is_tree or out_png.stem == "stone_fence_v") else 3000
    if n_op < min_op:
        return False, f"foreground too small ({n_op} < {min_op} px)"
    if stats["off_palette_colors"] != 0:
        return False, f"off_palette_colors={stats['off_palette_colors']}"
    if not stats["alpha_is_1bit"]:
        return False, "alpha is not strictly 1-bit"
    if is_tree and stats["sky_bleed_pixels"] > 0:
        return False, f"sky_bleed_pixels={stats['sky_bleed_pixels']}"

    # Reject any remaining magenta or cyan bleed across foreground pixels
    r_c = arr[op, 0].astype(np.int16)
    g_c = arr[op, 1].astype(np.int16)
    b_c = arr[op, 2].astype(np.int16)
    mag_leaks = int(np.sum((np.minimum(r_c, b_c) - g_c) > 28))
    if mag_leaks > 0:
        return False, f"magenta_bleed_pixels={mag_leaks}"

    hole_pixels = 0
    if not is_tree:
        cyan_leaks = int(np.sum((g_c > r_c + 25) & (b_c > r_c + 25) & (g_c > 110)))
        if cyan_leaks > 0:
            return False, f"cyan_bleed_pixels={cyan_leaks}"
        hole_pixels = int(np.sum(ndimage.binary_fill_holes(op) & (~op)))
        if out_png.stem == "stone_fence_v" and hole_pixels >= 120:
            return False, f"stone_fence_v hollow interior holes={hole_pixels} (>= 120)"

    max_sq = stats["flat_blob_max_square"]
    if max_sq >= 10:
        kind_lbl = "tree" if is_tree else "structure"
        return False, f"{kind_lbl} flat_blob_max_square={max_sq} (>= 10, {stats.get('flat_blob_max_run_pct', 0.0)}%)"

    # Ensure rich tonal shading (no single dominant flat color > 42%, deep shadow >= 12% on trees)
    op_rgb = arr[op, :3]
    _, counts = np.unique(op_rgb, axis=0, return_counts=True)
    max_color_frac = float(np.max(counts)) / float(n_op)
    if is_tree and max_color_frac > 0.42:
        return False, f"flat dominant color fraction={max_color_frac:.3f} (>0.42)"

    lum = 0.2126 * op_rgb[:, 0] + 0.7152 * op_rgb[:, 1] + 0.0722 * op_rgb[:, 2]
    deep_shadow_pct = 100.0 * float(np.mean(lum < 75.0))
    if is_tree and deep_shadow_pct < 12.0:
        return False, f"lacks deep canopy shadow contrast ({deep_shadow_pct:.1f}%)"

    if stats["unique_palette_colors"] < 14:
        return False, f"too few palette colors ({stats['unique_palette_colors']})"

    return True, (
        f"colors={stats['unique_palette_colors']} op_px={n_op} holes={hole_pixels} max_sq={max_sq} "
        f"sky_bleed={stats['sky_bleed_pixels']} max_frac={max_color_frac:.2f} shadow={deep_shadow_pct:.1f}%"
    )


def load_tree_botanical_palette(palette_path: Path = gs.PALETTE_PATH) -> np.ndarray:
    """Subset of palette_soc_sunlit_128.json containing natural foliage, bark, moss, sandstone, and ink ramps."""
    data = json.loads(palette_path.read_text(encoding="utf-8"))
    ramps = data["ramps"]
    keep_ramps = (
        "ink_and_anchors",
        "emerald_turf",
        "sage_olive_moss",
        "timber_leather_earth",
        "sunlit_sandstone",
        "warm_cobble_gray",
        "terracotta_russet",
    )
    hexes: List[str] = []
    for k in keep_ramps:
        hexes.extend(ramps.get(k, []))
    # Also include darkest slate shadow ink (#232B42) without sky/cobalt blues
    hexes.append("#232B42")
    unique_hex = list(dict.fromkeys(hexes))
    return np.array([pp.hex_to_rgb(c) for c in unique_hex], dtype=np.uint8)


def load_stone_masonry_palette(palette_path: Path = gs.PALETTE_PATH) -> np.ndarray:
    """
    Stone masonry subset of palette_soc_sunlit_128.json: warm cobbles, sunlit sandstone,
    mortar earth, dark ink anchors, deep moss, and dark slate shadows (#232B42, #2F3A59, #3E4C73).
    Excludes bright cerulean, cyan, cobalt, and plum ramps so turquoise roof bleed is impossible.
    """
    data = json.loads(palette_path.read_text(encoding="utf-8"))
    ramps = data["ramps"]
    keep_ramps = (
        "ink_and_anchors",
        "warm_cobble_gray",
        "sunlit_sandstone",
        "timber_leather_earth",
        "emerald_turf",
        "sage_olive_moss",
    )
    hexes: List[str] = []
    for k in keep_ramps:
        hexes.extend(ramps.get(k, []))
    hexes.extend(["#232B42", "#2F3A59", "#3E4C73"])
    unique_hex = list(dict.fromkeys(hexes))
    return np.array([pp.hex_to_rgb(c) for c in unique_hex], dtype=np.uint8)


def _prepare_clean_magenta_rgba(raw_arr: np.ndarray) -> np.ndarray:
    """
    Remove both exterior #FF00FF backdrop and enclosed antialiased purple/magenta gaps
    (e.g. narrow willow vine slits and bridge arch voids) while despilling perimeter halos.
    """
    from scipy import ndimage

    rgb, ext_fg = pp.remove_background_1bit(raw_arr, strip_ground_shadow=False)
    r = rgb[..., 0].astype(np.int16)
    g = rgb[..., 1].astype(np.int16)
    b = rgb[..., 2].astype(np.int16)

    # Punch both exterior/enclosed neon magenta AND muted purple/orchid chroma transitions inside gaps
    mag_dom = np.minimum(r, b) - g
    chroma_bleed = ((r - g) > 18) & ((b - g) > 18)
    enclosed_magenta = (mag_dom > 16) | ((r > 130) & (b > 130) & (g < 115) & chroma_bleed)
    fg_mask = ext_fg & (~enclosed_magenta)

    # Remove tiny detached <32px islands in 1024x1024 before PixelOE downscale
    lbl, n_c = ndimage.label(fg_mask)
    if n_c > 1:
        sizes = ndimage.sum(fg_mask, lbl, range(1, n_c + 1))
        max_sz = float(np.max(sizes)) if len(sizes) else 0.0
        keep = np.zeros_like(fg_mask)
        for idx, sz in enumerate(sizes, start=1):
            if sz >= max(32.0, max_sz * 0.004):
                keep |= (lbl == idx)
        fg_mask = keep

    # Despill outer 5px rim around BOTH exterior edge AND punched interior vine/branch gaps
    inner_clean = ndimage.binary_erosion(fg_mask, iterations=5) & (mag_dom <= 4)
    rim = fg_mask & (~inner_clean)
    rim_bad = rim & ((mag_dom > 6) | chroma_bleed)
    if np.any(rim_bad) and np.any(inner_clean):
        _, idx = ndimage.distance_transform_edt(~inner_clean, return_indices=True)
        ry, rx = np.where(rim_bad)
        rgb[ry, rx] = rgb[idx[0, ry, rx], idx[1, ry, rx]]

    out = np.zeros((raw_arr.shape[0], raw_arr.shape[1], 4), dtype=np.uint8)
    out[..., :3] = rgb
    out[fg_mask, 3] = 255
    out[~fg_mask] = 0
    return out


def _frame_bottom_centered(sprite_inner: np.ndarray, canvas_size: Tuple[int, int]) -> np.ndarray:
    """Place a bottom-pivoted inner sprite centered horizontally and bottom-aligned on canvas_size."""
    out_w, out_h = canvas_size
    ih, iw, _ = sprite_inner.shape
    canvas = np.zeros((out_h, out_w, 4), dtype=np.uint8)
    ox = (out_w - iw) // 2
    oy = out_h - ih
    canvas[oy : oy + ih, ox : ox + iw] = sprite_inner
    return canvas


def reroll_job(
    name: str,
    rel_subdir: str,
    out_size: Tuple[int, int],
    prompt: str,
    negative: str,
    seeds: List[int],
    p_init: Path,
    p_depth: Path,
    p_canny: Optional[Path],
    ip_ref_remote: Optional[str],
    ip_weight: float,
    denoise: float,
    depth_strength: float,
    depth_end: float,
    canny_strength: float,
    canny_end: float,
    is_tree: bool,
    use_chroma_magenta: bool = True,
    inner_fit_size: Optional[Tuple[int, int]] = None,
) -> dict:
    in_rem = gs.upload_image(p_init, f"s1_{name}_init.png")
    d_rem = gs.upload_image(p_depth, f"s1_{name}_depth.png")
    c_rem = gs.upload_image(p_canny, f"s1_{name}_canny.png") if p_canny else None

    out_png = UNITY_CART_ROOT / rel_subdir / f"{name}.png"
    out_nrm = UNITY_CART_ROOT / rel_subdir / f"{name}_n.png"

    best_stats = None
    best_reason = "no attempt"

    for attempt, seed in enumerate(seeds, start=1):
        wf = gs.build_cartography_workflow(
            prompt=prompt,
            negative=negative,
            seed=seed,
            filename_prefix=f"s1_{name}_a{attempt}",
            lora_name="pixel-art-style-illustrious-by-skormino.safetensors",
            lora_strength=0.88,
            steps=26,
            cfg=6.5,
            width=1024,
            height=1024,
            ip_ref_remote=ip_ref_remote,
            ip_weight=ip_weight,
            depth_remote=d_rem,
            depth_strength=depth_strength,
            depth_end=depth_end,
            canny_remote=c_rem,
            canny_strength=canny_strength,
            canny_end=canny_end,
            init_remote=in_rem,
            denoise=denoise,
        )
        img_bytes, dt, peak_vram = gs.run_comfy_prompt(wf)
        raw_path = RAW_DIR / f"{name}_s1_a{attempt}.png"
        raw_path.write_bytes(img_bytes)

        raw_arr = np.array(Image.open(raw_path).convert("RGBA"), dtype=np.uint8)
        if use_chroma_magenta:
            clean_rgba = _prepare_clean_magenta_rgba(raw_arr)
            rgb_clean, fg_mask = pp.remove_background_1bit(clean_rgba, strip_ground_shadow=False)
        else:
            rgb_clean, fg_mask = pp.remove_background_1bit(raw_arr, strip_ground_shadow=True)
        down_target = inner_fit_size if inner_fit_size is not None else out_size
        sprite_1x = pp.pixeloe_downscale_sprite(
            rgb_clean,
            fg_mask,
            target_size=down_target,
            pad_px=3 if is_tree else 2,
            bottom_pivot=True,
            thickness=1,
        )
        if inner_fit_size is not None:
            sprite_1x = _frame_bottom_centered(sprite_1x, out_size)
        quant_pal = (
            load_tree_botanical_palette(gs.PALETTE_PATH)
            if is_tree
            else load_stone_masonry_palette(gs.PALETTE_PATH)
        )
        sprite_1x = pp.apply_sunlit_grade_and_quantize(
            sprite_1x,
            quant_pal,
            dither_strength=0.14 if is_tree else 0.12,
            grade_strength=0.05 if is_tree else 0.08,
            shadow_lift=2.5 if is_tree else 3.0,
            vibrance_boost=1.06 if is_tree else 1.02,
            preserve_alpha=False,
        )

        cleaned = sprite_1x
        if is_tree:
            cleaned = gs.strip_tree_bottom_dirt_oval(cleaned)

        # Keep major connected components (>= 2% of main body)
        fg = (cleaned[..., 3] > 0).astype(np.uint8)
        n_lbl, labels, cc_stats, _ = cv2.connectedComponentsWithStats(fg, connectivity=8)
        if n_lbl > 2:
            areas = [int(cc_stats[i, cv2.CC_STAT_AREA]) for i in range(1, n_lbl)]
            max_a = max(areas)
            for i in range(1, n_lbl):
                if int(cc_stats[i, cv2.CC_STAT_AREA]) < max(16, int(max_a * 0.025)):
                    cleaned[labels == i] = [0, 0, 0, 0]

        out_png.parent.mkdir(parents=True, exist_ok=True)
        out_nrm.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(cleaned, "RGBA").save(out_png)
        norm_arr, backend_used = pp.generate_normal_map(cleaned, periodic=False, normal_backend="auto")
        Image.fromarray(norm_arr, "RGBA").save(out_nrm)

        pal_rgb = pp.load_palette(gs.PALETTE_PATH)
        stats = pp.validate_outputs(
            cleaned,
            norm_arr,
            pal_rgb,
            is_tile=False,
            asset_kind="tree" if is_tree else "structure",
        )
        stats["elapsed_s"] = dt
        stats["peak_vram_mib"] = peak_vram
        stats["normal_backend"] = backend_used
        stats["seed"] = seed

        ok, msg = check_sprint1_qa(out_png, stats, is_tree=is_tree)
        best_stats = stats
        best_reason = msg
        print(f"[{name}] attempt {attempt}/{len(seeds)} seed={seed} ({dt:.1f}s, {peak_vram} MiB) -> ok={ok}: {msg}", flush=True)
        if ok:
            return stats

    raise RuntimeError(f"Asset '{name}' failed Sprint 1 QA after {len(seeds)} seeds: {best_reason} | last={best_stats}")


ALL_TARGETS = ("tree_willow_a", "tree_oak_b", "stone_fence_v", "stone_bridge")


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    ap = argparse.ArgumentParser(description="Sprint 1 Phase-1 Cartography Sprite Re-Roll")
    ap.add_argument(
        "--only",
        nargs="+",
        choices=ALL_TARGETS,
        default=list(ALL_TARGETS),
        help="Subset of Sprint 1 assets to regenerate (default: all four).",
    )
    return ap.parse_args(argv)


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    targets = set(args.only)

    GUIDE_DIR.mkdir(parents=True, exist_ok=True)
    RAW_DIR.mkdir(parents=True, exist_ok=True)

    ns_guides: Dict[str, Tuple[Path, Path]] = {}
    if "stone_fence_v" in targets or "stone_bridge" in targets:
        print("[Step 3B] Generating 3/4 oblique Blender N-S bridge & continuous wall guides...", flush=True)
        ns_guides = render_ns_structure_guides()

    fence_v_init = fence_v_depth = fence_v_canny = None
    if "stone_fence_v" in targets:
        fence_v_depth, fence_v_canny = ns_guides["stone_fence_v_ns"]
        fence_v_init = make_magenta_chroma_guide_from_depth("stone_fence_v_ns", fence_v_depth, fill_kind="wall")

    bridge_ns_init = bridge_ns_depth = bridge_ns_canny = None
    if "stone_bridge" in targets:
        bridge_ns_depth, bridge_ns_canny = ns_guides["stone_bridge_ns"]
        bridge_ns_init = make_white_stone_guide_from_depth("stone_bridge_ns", bridge_ns_depth, fill_kind="bridge")

    oak_b_init = oak_b_depth = None
    if "tree_oak_b" in targets:
        oak_b_init, oak_b_depth = paint_chroma_oak_b()

    willow_a_init = willow_a_depth = None
    if "tree_willow_a" in targets:
        willow_a_init, willow_a_depth = paint_chroma_willow_a()

    report_p = gs.SCRATCH_ROOT / "sprint1_art_reroll_report.json"
    results: dict = {}
    if report_p.is_file():
        try:
            results = json.loads(report_p.read_text(encoding="utf-8"))
        except Exception:
            results = {}

    spawned_proc = ensure_comfy_running()
    try:
        if "tree_willow_a" in targets and willow_a_init is not None and willow_a_depth is not None:
            results["tree_willow_a"] = reroll_job(
                name="tree_willow_a",
                rel_subdir="trees",
                out_size=(256, 256),
                prompt=(
                    "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, "
                    "solid flat bright magenta #FF00FF chroma key background, isolated RPG tree sprite, "
                    "lush hand-pixeled medieval riverside weeping willow tree, rich textured individual leaf clusters "
                    "in deep forest emerald green #183622 shadows and vibrant spring emerald #52A653 sunlit highlights, "
                    "intricate hanging leafy willow vine drapes over gnarled oak trunk, crisp dark slate ink outlines, "
                    "Songs of Conquest Arleon meadow tree style, no flat fill"
                ),
                negative=TREE_NEG_CHROMA,
                seeds=[410101, 410102, 410103],
                p_init=willow_a_init,
                p_depth=willow_a_depth,
                p_canny=None,
                ip_ref_remote=None,
                ip_weight=0.0,
                denoise=0.78,
                depth_strength=0.48,
                depth_end=0.65,
                canny_strength=0.0,
                canny_end=0.0,
                is_tree=True,
                use_chroma_magenta=True,
            )

        if "tree_oak_b" in targets and oak_b_init is not None and oak_b_depth is not None:
            results["tree_oak_b"] = reroll_job(
                name="tree_oak_b",
                rel_subdir="trees",
                out_size=(224, 224),
                prompt=(
                    "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated tree, "
                    "solid flat bright magenta #FF00FF chroma key background, isolated RPG tree sprite, "
                    "lush hand-pixeled medieval mature broadleaf oak tree, dense textured pixel leaf clusters "
                    "with deep emerald-teal canopy shadows #183622, mid emerald #2E693B foliage, and sunlit green #52A653 tips, "
                    "knobby oak brown bark trunk and exposed roots cleanly trimmed at bottom, crisp dark slate ink outlines, "
                    "Songs of Conquest Arleon oak tree style, empty magenta gaps between branches"
                ),
                negative=TREE_NEG_CHROMA,
                seeds=[410201, 410202, 410203],
                p_init=oak_b_init,
                p_depth=oak_b_depth,
                p_canny=None,
                ip_ref_remote=None,
                ip_weight=0.0,
                denoise=0.78,
                depth_strength=0.48,
                depth_end=0.65,
                canny_strength=0.0,
                canny_end=0.0,
                is_tree=True,
                use_chroma_magenta=True,
            )

        if "stone_fence_v" in targets and fence_v_init is not None and fence_v_depth is not None:
            results["stone_fence_v"] = reroll_job(
                name="stone_fence_v",
                rel_subdir="structures",
                out_size=(128, 128),
                prompt=(
                    "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated structure prop, "
                    "solid flat bright magenta #FF00FF chroma key background, isolated RPG structure prop, straight vertical 3/4 top-down medieval village "
                    "low waist-height continuous stone masonry garden wall segment running straight North-South (matching low stone fence height, no tall corner towers), "
                    "cool slate-blue and weathered grey cut ashlar stone blocks with warm sandstone edge highlights, dark slate ink mortar lines, and dark flat stone capstones, "
                    "clean grey stonework, zero holes, zero wireframe, Songs of Conquest village wall prop"
                ),
                negative=STRUCT_NEG_CHROMA + ", moss-covered, green hedge, green block, shrubbery, bush, foliage wall, diagonal slant",
                seeds=[420302, 420303, 420304, 420305],
                p_init=fence_v_init,
                p_depth=fence_v_depth,
                p_canny=fence_v_canny,
                ip_ref_remote=None,
                ip_weight=0.0,
                denoise=0.70,
                depth_strength=0.65,
                depth_end=0.85,
                canny_strength=0.45,
                canny_end=0.65,
                is_tree=False,
                use_chroma_magenta=True,
                inner_fit_size=(54, 82),
            )

        if "stone_bridge" in targets and bridge_ns_init is not None and bridge_ns_depth is not None:
            results["stone_bridge"] = reroll_job(
                name="stone_bridge",
                rel_subdir="structures",
                out_size=(256, 192),
                prompt=(
                    "pixpix, pixel_art, hi-bit pixel art, masterpiece, no humans, solo isolated structure prop, "
                    "solid pure flat #FFFFFF white background, isolated studio cutout, 3/4 oblique top-down medieval fantasy "
                    "arched stone cobblestone footbridge oriented North-South receding in depth, solid cambered warm gray and sandstone cobble roadway "
                    "over dark shaded arch opening, sturdy stone parapets and four low corner stone bollards with olive moss tufts, "
                    "crisp dark slate ink outlines, open white space beneath bridge arch, Songs of Conquest style"
                ),
                negative=STRUCT_NEG_WHITE,
                seeds=[420401, 420402, 420403, 420404],
                p_init=bridge_ns_init,
                p_depth=bridge_ns_depth,
                p_canny=bridge_ns_canny,
                ip_ref_remote=None,
                ip_weight=0.0,
                denoise=0.74,
                depth_strength=0.60,
                depth_end=0.82,
                canny_strength=0.45,
                canny_end=0.65,
                is_tree=False,
                use_chroma_magenta=False,
            )
    finally:
        teardown_comfy(spawned_proc)

    gs.make_contact_sheets()
    report_p.write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(f"[Sprint 1 Art] Updated contact sheet & report: {report_p}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
