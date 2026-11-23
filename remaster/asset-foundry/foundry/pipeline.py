"""End-to-end orchestration pipeline for Realmz Asset Foundry V1 (convert, batch, ship, status)."""

from __future__ import annotations

import json
import shutil
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .config import (
    BLENDER_EXE,
    CATEGORY_DEFAULTS,
    CREATE_NO_WINDOW,
    FOUNDRY_VENV_PY,
    HF_CACHE_DIR,
    JOBS_DIR,
    PALETTE_SOC_128,
    UNITY_CLI_EXE,
    UNITY_INBOX_DIR,
    UNITY_PREFABS_DIR,
    UNITY_PROJECT_DIR,
    infer_rig_profile,
    normalize_category,
    normalize_mode,
    query_gpu_vram,
    sanitize_ascii,
    sanitize_asset_id,
)
from .hd2d_baker import bake_hd2d_from_renders
from .img2mesh import generate_textured_glb
from .preprocess import prepare_reference_image
from .rig_normalize import (
    normalize_and_rig_with_blender,
    probe_unirig_native_blockers,
    render_views_with_blender,
    try_unirig_skeleton,
)

NOTIFY_SCRIPT = Path(r"C:\Users\karti\LifeTwin\.agent\scripts\notify-main-twin.py")


def _iso_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def convert_asset(
    image_path: str | Path,
    name: str | None = None,
    category: str = "Prop",
    rig: str = "Auto",
    scale_m: float | None = None,
    mode: str = "Both",
    target_tris: int | None = None,
    hd2d_size: int | None = None,
    frames: int = 1,
    quantize: bool = True,
    steps: int = 5,
    octree_res: int = 256,
    seed: int = 42,
    use_unirig_ar: bool = True,
    ship: bool = False,
    notify: bool = False,
    run_unity_import: bool = False,
) -> dict[str, Any]:
    """Convert one reference image into canonical rigged 3D .glb + HD-2D sprite/normal sheet."""
    src_path = Path(image_path).resolve()
    if not src_path.exists():
        raise FileNotFoundError(f"Input image not found: {src_path}")

    asset_id = sanitize_asset_id(name or src_path.stem)
    cat = normalize_category(category)
    out_mode = normalize_mode(mode)
    defaults = CATEGORY_DEFAULTS[cat]

    resolved_scale = float(scale_m) if scale_m is not None else float(defaults["scale_m"])
    resolved_tris = int(target_tris) if target_tris is not None else int(defaults["target_tris"])
    resolved_rig = infer_rig_profile(cat, asset_id, rig)
    resolved_hd2d_size = int(hd2d_size) if hd2d_size is not None else int(defaults["hd2d_size"])
    resolved_ppu = resolved_hd2d_size
    resolved_frames = max(1, int(frames))
    if resolved_rig == "Static" and resolved_frames > 1:
        resolved_frames = 1

    job_dir = JOBS_DIR / asset_id
    job_dir.mkdir(parents=True, exist_ok=True)
    work_dir = job_dir / "_work"
    work_dir.mkdir(parents=True, exist_ok=True)

    t_start = time.perf_counter()

    # 1. CPU reference isolation
    prep_rgba = work_dir / "prep_rgba.png"
    prep_rgb = work_dir / "prep_rgb.png"
    prep_info = prepare_reference_image(src_path, prep_rgba, prep_rgb, canvas_size=512)

    # 2. Local 8 GB Image-to-3D + Multi-view UV paint (subprocess-isolated stages)
    mesh_stats = generate_textured_glb(
        image_rgba_path=prep_rgba,
        work_dir=work_dir,
        target_tris=max(resolved_tris, 6000),
        steps=steps,
        octree_res=octree_res,
        seed=seed,
        render_size=1024,
        texture_size=1024,
        skip_delight_for_pixel=bool(prep_info.get("was_pixel_sprite", False)),
    )
    textured_raw_glb = Path(mesh_stats["textured_glb"])

    # 3. Optional UniRig Skeleton AR (with honest blocker check & Blender template fallback)
    joints_json = work_dir / "unirig_joints.json"
    unirig_info: dict[str, Any] = {"used_unirig_ar": False}
    if resolved_rig != "Static" and use_unirig_ar:
        unirig_info = try_unirig_skeleton(textured_raw_glb, joints_json, resolved_rig, seed=seed)

    # 4. Headless Blender normalization, decimation, rigging/skinning, and clip baking
    canonical_glb = job_dir / f"{asset_id}.glb"
    blender_meta_path = work_dir / "blender_meta.json"
    blender_meta = normalize_and_rig_with_blender(
        raw_glb=textured_raw_glb,
        canonical_glb=canonical_glb,
        meta_out=blender_meta_path,
        category=cat,
        rig_profile=resolved_rig,
        scale_m=resolved_scale,
        target_tris=resolved_tris,
        joints_json=joints_json if unirig_info.get("used_unirig_ar") else None,
    )

    rig_source = "none"
    if blender_meta.get("rigged"):
        if unirig_info.get("used_unirig_ar"):
            rig_source = "unirig_ar+blender_auto_skin"
        else:
            skin_info = blender_meta.get("skinning", {})
            if skin_info.get("bone_heat_succeeded"):
                rig_source = "blender_template+bone_heat"
            else:
                rig_source = "blender_template+gaussian_skin"

    # 5. Render orthographic views + 2x2 3D turntable QA contact sheet
    raw_albedo = work_dir / "raw_ortho_albedo.png"
    raw_normal = work_dir / "raw_ortho_normal.png"
    qa_preview = job_dir / f"{asset_id}_qa_preview.png"
    render_views_with_blender(
        canonical_glb=canonical_glb,
        out_albedo=raw_albedo,
        out_normal=raw_normal,
        out_qa=qa_preview,
        size=resolved_hd2d_size,
        frames=resolved_frames,
    )

    # 6. Bake quantized HD-2D sprite sheet + OpenGL +Y normal map
    sheet_png = job_dir / f"{asset_id}_sheet.png"
    sheet_n_png = job_dir / f"{asset_id}_sheet_n.png"
    hd2d_stats = bake_hd2d_from_renders(
        albedo_raw_path=raw_albedo,
        normal_raw_path=raw_normal,
        out_dir=job_dir,
        asset_id=asset_id,
        category=cat,
        target_size=resolved_hd2d_size,
        frames=resolved_frames,
        quantize=quantize,
        palette_path=PALETTE_SOC_128,
        out_albedo=sheet_png,
        out_normal=sheet_n_png,
    )

    bounds = blender_meta.get("bounds_m", {"x": 1.0, "y": resolved_scale, "z": 1.0})
    peak_vram = max(
        int(mesh_stats.get("peak_vram_mib", 0)),
        int(unirig_info.get("ar_result", {}).get("post_vram_mib", 0)),
    )
    idle_vram = query_gpu_vram()["used_mib"]
    elapsed_total = round(time.perf_counter() - t_start, 2)

    display_name = (name or src_path.stem).replace("_", " ").strip().title()
    manifest = {
        "schema_version": "1.0",
        "id": asset_id,
        "name": sanitize_ascii(display_name),
        "category": cat,
        "rig_profile": blender_meta.get("rig_profile_resolved", resolved_rig),
        "rigged": bool(blender_meta.get("rigged", False)),
        "rig_source": rig_source,
        "bones": blender_meta.get("bones", []),
        "animations": blender_meta.get("animations", []),
        "bounds_m": {
            "x": float(bounds.get("x", 1.0)),
            "y": float(bounds.get("y", resolved_scale)),
            "z": float(bounds.get("z", 1.0)),
            "width": float(bounds.get("x", 1.0)),
            "height": float(bounds.get("y", resolved_scale)),
            "depth": float(bounds.get("z", 1.0)),
        },
        "pivot": "bottom_center",
        "forward_axis": "+Z",
        "triangle_count": int(blender_meta.get("triangle_count", 0)),
        "target_scale_m": resolved_scale,
        "preferred_mode": out_mode,
        "hd2d_frame_size": resolved_hd2d_size,
        "hd2d_frame_count": resolved_frames,
        "hd2d_ppu": resolved_ppu,
        "source_image": src_path.name,
        "peak_vram_mib": peak_vram,
        "timestamp": _iso_now(),
    }
    manifest_path = job_dir / f"{asset_id}.asset.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    report = {
        "id": asset_id,
        "job_dir": str(job_dir),
        "glb_path": str(canonical_glb),
        "sheet_path": str(sheet_png),
        "normal_path": str(sheet_n_png),
        "qa_preview_path": str(qa_preview),
        "manifest_path": str(manifest_path),
        "elapsed_total_sec": elapsed_total,
        "peak_vram_mib": peak_vram,
        "idle_vram_after_mib": idle_vram,
        "preprocess": prep_info,
        "img2mesh": mesh_stats,
        "unirig": unirig_info,
        "blender": blender_meta,
        "hd2d_qa": hd2d_stats,
        "manifest": manifest,
    }
    report_path = job_dir / f"{asset_id}.report.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    ship_result = None
    if ship:
        ship_result = ship_to_realmz(
            job_dir,
            notify=notify,
            run_unity_import=run_unity_import,
        )

    return {
        "status": "ok",
        "id": asset_id,
        "category": cat,
        "rig_profile": manifest["rig_profile"],
        "rigged": manifest["rigged"],
        "rig_source": rig_source,
        "bones": len(manifest["bones"]),
        "animations": manifest["animations"],
        "triangle_count": manifest["triangle_count"],
        "bounds_m": {"x": manifest["bounds_m"]["x"], "y": manifest["bounds_m"]["y"], "z": manifest["bounds_m"]["z"]},
        "preferred_mode": out_mode,
        "elapsed_sec": elapsed_total,
        "peak_vram_mib": peak_vram,
        "idle_vram_mib": idle_vram,
        "job_dir": str(job_dir),
        "glb_path": str(canonical_glb),
        "sheet_path": str(sheet_png),
        "qa_preview_path": str(qa_preview),
        "shipped": ship_result,
    }


def _resolve_job_dir(asset_dir_or_id: str | Path) -> Path:
    p = Path(asset_dir_or_id)
    if p.exists() and p.is_dir():
        return p.resolve()
    candidate = JOBS_DIR / sanitize_asset_id(str(asset_dir_or_id))
    if candidate.exists() and candidate.is_dir():
        return candidate.resolve()
    raise FileNotFoundError(f"Asset job directory or ID not found: {asset_dir_or_id}")


def ship_to_realmz(
    asset_dir_or_id: str | Path,
    notify: bool = False,
    dry_run_notify: bool = False,
    run_unity_import: bool = False,
    notify_target: str | None = None,
) -> dict[str, Any]:
    """Copy a Foundry asset bundle into RealmzUnity Assets/Inbox/<id>/ and optionally trigger import & notify."""
    src_dir = _resolve_job_dir(asset_dir_or_id)
    asset_id = src_dir.name
    manifest_src = src_dir / f"{asset_id}.asset.json"
    if not manifest_src.exists():
        # Search for any *.asset.json inside src_dir
        matches = list(src_dir.glob("*.asset.json"))
        if not matches:
            raise FileNotFoundError(f"No .asset.json found in {src_dir}")
        manifest_src = matches[0]
        asset_id = manifest_src.name[: -len(".asset.json")]

    manifest = json.loads(manifest_src.read_text(encoding="utf-8"))
    mode = normalize_mode(manifest.get("preferred_mode", "Both"))

    inbox_dir = UNITY_INBOX_DIR / asset_id
    inbox_dir.mkdir(parents=True, exist_ok=True)

    copied_files: list[str] = []
    shutil.copy2(manifest_src, inbox_dir / f"{asset_id}.asset.json")
    copied_files.append(str(inbox_dir / f"{asset_id}.asset.json"))

    glb_src = src_dir / f"{asset_id}.glb"
    if mode in ("Both", "3D") and glb_src.exists():
        dst_glb = inbox_dir / f"{asset_id}.glb"
        shutil.copy2(glb_src, dst_glb)
        copied_files.append(str(dst_glb))

    sheet_src = src_dir / f"{asset_id}_sheet.png"
    sheet_n_src = src_dir / f"{asset_id}_sheet_n.png"
    if mode in ("Both", "HD2D") and sheet_src.exists():
        dst_sheet = inbox_dir / f"{asset_id}_sheet.png"
        shutil.copy2(sheet_src, dst_sheet)
        copied_files.append(str(dst_sheet))
        if sheet_n_src.exists():
            dst_n = inbox_dir / f"{asset_id}_sheet_n.png"
            shutil.copy2(sheet_n_src, dst_n)
            copied_files.append(str(dst_n))

    unity_status = "skipped"
    unity_receipt = None
    if run_unity_import and UNITY_CLI_EXE.exists():
        unity_status = _trigger_unity_inbox_import(asset_id)
        receipt_path = inbox_dir / f"{asset_id}.import_receipt.json"
        if receipt_path.exists():
            unity_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))

    notify_msg = sanitize_ascii(
        f"[Realmz Asset Foundry] Shipped '{asset_id}' ({manifest.get('category', 'Prop')}, "
        f"{manifest.get('rig_profile', 'Static')}, {manifest.get('triangle_count', 0)} tris, "
        f"mode={mode}) to Assets/Inbox/{asset_id}/ and Prefabs/Foundry/."
    )

    notify_result: dict[str, Any] = {"sent": False, "message": notify_msg}
    if dry_run_notify:
        notify_result["dry_run"] = True
    elif notify and NOTIFY_SCRIPT.exists():
        cmd = ["python", str(NOTIFY_SCRIPT), "--stdin"]
        if notify_target:
            cmd.extend(["--target", notify_target])
        proc = subprocess.run(
            cmd,
            input=notify_msg,
            capture_output=True,
            text=True,
            timeout=20,
            creationflags=CREATE_NO_WINDOW,
        )
        notify_result["sent"] = proc.returncode == 0
        notify_result["returncode"] = proc.returncode
        if proc.returncode != 0:
            notify_result["error"] = (proc.stderr or proc.stdout).strip()[:300]

    return {
        "status": "shipped",
        "id": asset_id,
        "mode": mode,
        "inbox_dir": str(inbox_dir),
        "copied_files": [Path(p).name for p in copied_files],
        "unity_import": unity_status,
        "unity_receipt": unity_receipt,
        "notify": notify_result,
    }


def _trigger_unity_inbox_import(asset_id: str) -> str:
    """Invoke RealmzAssetInboxPostprocessor.ProcessAsset via unity CLI if an Editor is running."""
    try:
        st = subprocess.run(
            [str(UNITY_CLI_EXE), "status", "--project-path", str(UNITY_PROJECT_DIR), "--json"],
            capture_output=True,
            text=True,
            timeout=15,
            creationflags=CREATE_NO_WINDOW,
        )
        if st.returncode != 0 or '"count": 0' in st.stdout:
            return "editor_not_running"

        subprocess.run(
            [
                str(UNITY_CLI_EXE),
                "assets",
                "refresh",
                "--project-path",
                str(UNITY_PROJECT_DIR),
            ],
            capture_output=True,
            text=True,
            timeout=120,
            creationflags=CREATE_NO_WINDOW,
        )
        ev = subprocess.run(
            [
                str(UNITY_CLI_EXE),
                "command",
                "script",
                "eval",
                "--project-path",
                str(UNITY_PROJECT_DIR),
                "--caller",
                "plugin",
                "--skill",
                "unity-cli",
                f'Realmz.EditorTools.RealmzAssetInboxPostprocessor.ProcessAsset("{asset_id}");',
            ],
            capture_output=True,
            text=True,
            timeout=120,
            creationflags=CREATE_NO_WINDOW,
        )
        return "ok" if ev.returncode == 0 else f"eval_error_{ev.returncode}"
    except Exception as exc:
        return f"error: {exc}"


def batch_convert(
    manifest_path: str | Path,
    ship: bool = False,
    notify: bool = False,
    run_unity_import: bool = False,
) -> dict[str, Any]:
    """Run sequential GPU conversions from a JSON list or {'assets': [...]} manifest."""
    raw = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    items = raw.get("assets", raw) if isinstance(raw, dict) else raw
    if not isinstance(items, list):
        raise ValueError("Batch manifest must be a JSON array or {'assets': [...]}.")

    results: list[dict[str, Any]] = []
    for item in items:
        res = convert_asset(
            image_path=item["image"],
            name=item.get("name"),
            category=item.get("category", "Prop"),
            rig=item.get("rig", "Auto"),
            scale_m=item.get("scale"),
            mode=item.get("mode", "Both"),
            target_tris=item.get("target_tris"),
            hd2d_size=item.get("hd2d_size"),
            frames=item.get("frames", 1),
            quantize=item.get("quantize", True),
            steps=item.get("steps", 5),
            octree_res=item.get("octree_res", 256),
            seed=item.get("seed", 42),
            use_unirig_ar=item.get("use_unirig_ar", True),
            ship=ship or bool(item.get("ship", False)),
            notify=notify,
            run_unity_import=run_unity_import,
        )
        results.append(res)

    return {
        "status": "ok",
        "count": len(results),
        "idle_vram_mib": query_gpu_vram()["used_mib"],
        "assets": [
            {
                "id": r["id"],
                "category": r["category"],
                "rig": r["rig_profile"],
                "tris": r["triangle_count"],
                "peak_vram_mib": r["peak_vram_mib"],
                "elapsed_sec": r["elapsed_sec"],
            }
            for r in results
        ],
    }


def get_status() -> dict[str, Any]:
    """Inspect local GPU VRAM, tool readiness, converted jobs, and shipped Unity Inbox assets."""
    vram = query_gpu_vram()
    jobs = sorted(p.name for p in JOBS_DIR.iterdir() if p.is_dir()) if JOBS_DIR.exists() else []
    inbox = sorted(p.name for p in UNITY_INBOX_DIR.iterdir() if p.is_dir() and not p.name.startswith("__")) if UNITY_INBOX_DIR.exists() else []
    prefabs = sorted(p.name for p in UNITY_PREFABS_DIR.glob("*.prefab")) if UNITY_PREFABS_DIR.exists() else []

    hf_hub = HF_CACHE_DIR / "hub"
    models_cached = {
        "hunyuan3d_2mini": (hf_hub / "models--tencent--Hunyuan3D-2mini").exists(),
        "hunyuan3d_2_paint": (hf_hub / "models--tencent--Hunyuan3D-2").exists(),
        "unirig": (hf_hub / "models--VAST-AI--UniRig").exists(),
    }

    return {
        "status": "ready" if FOUNDRY_VENV_PY.exists() and BLENDER_EXE.exists() else "incomplete",
        "gpu": vram,
        "vram_idle_ok": vram.get("used_mib", 9999) < 1200,
        "venv_ready": FOUNDRY_VENV_PY.exists(),
        "blender_ready": BLENDER_EXE.exists(),
        "blender_exe": str(BLENDER_EXE),
        "models_cached": models_cached,
        "unirig_probe": probe_unirig_native_blockers(),
        "jobs": jobs,
        "unity_inbox": inbox,
        "unity_prefabs_count": len(prefabs),
    }
