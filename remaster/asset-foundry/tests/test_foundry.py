"""Unit tests for Realmz Asset Foundry V1 configuration, preprocessing, terrain PBR, shipping, and hygiene."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import numpy as np
from PIL import Image

FOUNDRY_CODE_DIR = Path(r"C:\Users\karti\realmz\remaster\asset-foundry")
if str(FOUNDRY_CODE_DIR) not in sys.path:
    sys.path.insert(0, str(FOUNDRY_CODE_DIR))

from foundry.config import (
    CREATE_NO_WINDOW,
    infer_rig_profile,
    normalize_category,
    normalize_mode,
    sanitize_ascii,
    sanitize_asset_id,
)
from foundry.preprocess import prepare_reference_image
from foundry.terrain_gen import generate_terrain_asset
from foundry.verify_terrain_material import verify_terrain_bundle

CLI_AGENT_PATH = Path(r"C:\Users\karti\realmz\remaster\asset-foundry\bin\realmz-foundry.py")
CLI_LOCAL_BIN_PATH = Path(r"C:\Users\karti\.local\bin\realmz-foundry.py")


def _make_synthetic_cobble_image(path: Path, size: int = 640) -> None:
    """Create a realistic non-periodic stone/cobble RGB texture with directional lighting gradient."""
    rng = np.random.default_rng(12345)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)

    # Multi-frequency organic stone grain + cobblestone block pattern
    grid_x = np.sin(xx * (2.0 * np.pi / 78.0) + 0.4 * np.sin(yy * 0.03))
    grid_y = np.sin(yy * (2.0 * np.pi / 64.0) + 0.4 * np.cos(xx * 0.03))
    mortar = np.minimum(np.abs(grid_x), np.abs(grid_y))
    stone_mask = np.clip((mortar - 0.12) / 0.18, 0.0, 1.0)

    noise = rng.normal(0.0, 14.0, size=(size, size)).astype(np.float32)
    # Correlate stone grain spatially so texture resembles painterly brushwork rather than raw white noise
    import cv2

    noise = cv2.GaussianBlur(noise, (0, 0), sigmaX=1.3, sigmaY=1.3)
    # Add low-frequency lighting gradient across image (to verify de-lighting flattens it)
    grad = 24.0 * (xx / size - 0.5) - 18.0 * (yy / size - 0.5)

    r = 92.0 + 58.0 * stone_mask + noise + grad
    g = 88.0 + 52.0 * stone_mask + noise * 0.9 + grad
    b = 82.0 + 45.0 * stone_mask + noise * 0.8 + grad

    rgb = np.stack([r, g, b], axis=-1)
    rgb_u8 = np.clip(np.round(rgb), 10, 245).astype(np.uint8)
    Image.fromarray(rgb_u8, mode="RGB").save(path, format="PNG")


class RealmzFoundryUnitTests(unittest.TestCase):
    def test_sanitize_and_infer_rig(self) -> None:
        self.assertEqual(sanitize_asset_id("River Trout! "), "river_trout")
        self.assertEqual(normalize_category("hero"), "Hero")
        self.assertEqual(normalize_mode("hd-2d"), "HD2D")
        self.assertEqual(infer_rig_profile("Monster", "river_fish", "Auto"), "Aquatic")
        self.assertEqual(infer_rig_profile("Monster", "giant_frog", "Auto"), "Quadruped")
        self.assertEqual(infer_rig_profile("Hero", "knight", "Auto"), "Humanoid")
        self.assertEqual(infer_rig_profile("Building", "cottage_medieval", "Auto"), "Static")
        cleaned = sanitize_ascii("Hello\u2014World\u2013Test")
        self.assertNotIn("\u2014", cleaned)
        self.assertNotIn("\u2013", cleaned)

    def test_preprocess_transparent_sprite_and_opaque_image(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            spr = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
            for y in range(30, 100):
                for x in range(40, 90):
                    spr.putpixel((x, y), (200, 140, 60, 255))
            spr_path = root / "sprite.png"
            spr.save(spr_path)

            out_rgba = root / "prep_rgba.png"
            out_rgb = root / "prep_rgb.png"
            info = prepare_reference_image(spr_path, out_rgba, out_rgb)
            self.assertEqual(info["bg_method"], "existing_alpha")
            self.assertTrue(info["was_pixel_sprite"])
            self.assertGreater(info["fg_coverage"], 0.05)
            with Image.open(out_rgba) as im_rgba:
                self.assertEqual(im_rgba.size, (512, 512))
            with Image.open(out_rgb) as im_rgb:
                self.assertEqual(im_rgb.mode, "RGB")

    def test_terrain_reference_image_pbr_qa_and_ship(self) -> None:
        from foundry import pipeline

        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            src_img = root / "cobble_src.png"
            _make_synthetic_cobble_image(src_img, size=640)

            jobs_dir = root / "jobs"
            inbox_dir = root / "UnityInbox"

            res = generate_terrain_asset(
                image_or_prompt=src_img,
                name="river_cobble",
                res=512,
                style="painterly-pbr",
                tile_scale=2.0,
                seed=42,
                engine="image",
                jobs_dir=jobs_dir,
            )
            self.assertEqual(res["status"], "ok")
            self.assertTrue(res["qa_passed"], f"QA failures: {res.get('qa_failures')}")
            self.assertEqual(res["asset_kind"], "terrain")
            self.assertEqual(res["resolution"], 512)

            bundle_dir = Path(res["job_dir"])
            self.assertEqual(bundle_dir.name, "terrain_river_cobble")
            manifest_path = bundle_dir / "terrain.asset.json"
            self.assertTrue(manifest_path.exists())
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            for key in (
                "schema_version",
                "asset_kind",
                "id",
                "name",
                "category",
                "resolution",
                "style",
                "tile_scale_m",
                "maps",
                "seamless_metrics",
                "normal_qa",
                "luma_contrast_qa",
                "engine",
                "seed",
                "peak_vram_mib",
                "idle_vram_mib",
                "timestamp",
            ):
                self.assertIn(key, manifest, f"Missing manifest key: {key}")
            self.assertEqual(manifest["asset_kind"], "terrain")
            self.assertEqual(manifest["category"], "Tile")

            # Verify 512x512 dimensions and RGBA URP mask channel packing
            albedo = np.array(Image.open(bundle_dir / "river_cobble_albedo.png"))
            normal = np.array(Image.open(bundle_dir / "river_cobble_normal.png"))
            height = np.array(Image.open(bundle_dir / "river_cobble_height.png"))
            rough = np.array(Image.open(bundle_dir / "river_cobble_roughness.png"))
            ao = np.array(Image.open(bundle_dir / "river_cobble_ao.png"))
            mask = np.array(Image.open(bundle_dir / "river_cobble_mask.png"))

            self.assertEqual(albedo.shape, (512, 512, 3))
            self.assertEqual(normal.shape, (512, 512, 3))
            self.assertEqual(height.shape, (512, 512))
            self.assertEqual(rough.shape, (512, 512))
            self.assertEqual(ao.shape, (512, 512))
            self.assertEqual(mask.shape, (512, 512, 4))

            self.assertTrue(np.all(mask[..., 0] == 0))
            self.assertTrue(np.array_equal(mask[..., 1], ao))
            self.assertTrue(np.array_equal(mask[..., 2], height))
            self.assertTrue(np.array_equal(mask[..., 3], (255 - rough).astype(np.uint8)))

            qa_sheet = bundle_dir / "river_cobble_qa_2x2.png"
            self.assertTrue(qa_sheet.exists())

            # Verify standalone verifier passes
            qa_rep = verify_terrain_bundle(bundle_dir, render_contact_sheet=False)
            self.assertTrue(qa_rep["passed"], f"Verifier failed: {qa_rep['failures']}")
            self.assertLessEqual(qa_rep["seamless_metrics"]["albedo"]["seam_ratio"], 1.35)
            self.assertLessEqual(qa_rep["seamless_metrics"]["albedo"]["mean_border_delta"], 14.0)
            self.assertLessEqual(qa_rep["seamless_metrics"]["normal"]["seam_ratio"], 1.35)
            self.assertGreater(qa_rep["normal_qa"]["mean_B"], 180.0)
            self.assertGreaterEqual(qa_rep["normal_qa"]["min_B"], 128)

            # Verify ship_to_realmz routes terrain bundle into terrain_<id>/
            with mock.patch.object(pipeline, "UNITY_INBOX_DIR", inbox_dir):
                ship_res = pipeline.ship_to_realmz(bundle_dir, dry_run_notify=True)
            self.assertEqual(ship_res["status"], "shipped")
            self.assertEqual(ship_res["asset_kind"], "terrain")
            shipped_dir = inbox_dir / "terrain_river_cobble"
            self.assertTrue(shipped_dir.is_dir())
            for expected_name in (
                "terrain.asset.json",
                "river_cobble_albedo.png",
                "river_cobble_normal.png",
                "river_cobble_height.png",
                "river_cobble_roughness.png",
                "river_cobble_ao.png",
                "river_cobble_mask.png",
            ):
                self.assertTrue((shipped_dir / expected_name).exists(), f"Missing shipped {expected_name}")

    def test_cli_terrain_and_batch_isolated_root(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            src_img = root / "meadow_src.png"
            _make_synthetic_cobble_image(src_img, size=512)

            fake_realmz = root / "realmz_root"
            env = os.environ.copy()
            env["REALMZ_ROOT"] = str(fake_realmz)
            env["REALMZ_FOUNDRY_DIR"] = str(FOUNDRY_CODE_DIR)

            proc = subprocess.run(
                [
                    sys.executable,
                    str(CLI_AGENT_PATH),
                    "terrain",
                    str(src_img),
                    "--name",
                    "bywater_meadow",
                    "--res",
                    "512",
                    "--engine",
                    "image",
                    "--ship",
                    "--no-notify",
                    "--json",
                ],
                env=env,
                capture_output=True,
                text=True,
                timeout=60,
                creationflags=CREATE_NO_WINDOW,
            )
            self.assertEqual(proc.returncode, 0, f"CLI stderr: {proc.stderr}")
            data = json.loads(proc.stdout.strip().splitlines()[-1])
            self.assertEqual(data["status"], "ok")
            self.assertTrue(data["qa_passed"])
            self.assertTrue(
                (fake_realmz / "remaster" / "RealmzUnity" / "Assets" / "Inbox" / "terrain_bywater_meadow" / "terrain.asset.json").exists()
            )

            # Test batch routing for kind="terrain"
            batch_json = root / "batch.json"
            batch_json.write_text(
                json.dumps(
                    {
                        "assets": [
                            {
                                "kind": "terrain",
                                "image": str(src_img),
                                "name": "batch_cobble",
                                "engine": "image",
                                "res": 512,
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            proc_b = subprocess.run(
                [
                    sys.executable,
                    str(CLI_AGENT_PATH),
                    "batch",
                    str(batch_json),
                    "--ship",
                    "--json",
                ],
                env=env,
                capture_output=True,
                text=True,
                timeout=60,
                creationflags=CREATE_NO_WINDOW,
            )
            self.assertEqual(proc_b.returncode, 0, f"Batch stderr: {proc_b.stderr}")
            b_data = json.loads(proc_b.stdout.strip().splitlines()[-1])
            self.assertEqual(b_data["count"], 1)
            self.assertEqual(b_data["assets"][0]["asset_kind"], "terrain")
            self.assertTrue(
                (fake_realmz / "remaster" / "RealmzUnity" / "Assets" / "Inbox" / "terrain_batch_cobble" / "batch_cobble_mask.png").exists()
            )

    def test_zero_em_or_en_dashes_and_cli_parity(self) -> None:
        files_to_check = list((FOUNDRY_CODE_DIR / "foundry").glob("*.py")) + list(
            (FOUNDRY_CODE_DIR / "tests").glob("*.py")
        )
        if CLI_AGENT_PATH.exists():
            files_to_check.append(CLI_AGENT_PATH)
        if CLI_LOCAL_BIN_PATH.exists():
            files_to_check.append(CLI_LOCAL_BIN_PATH)

        for py_file in files_to_check:
            txt = py_file.read_text(encoding="utf-8")
            self.assertNotIn("\u2014", txt, f"Em-dash found in {py_file}")
            self.assertNotIn("\u2013", txt, f"En-dash found in {py_file}")

        if CLI_AGENT_PATH.exists() and CLI_LOCAL_BIN_PATH.exists():
            h1 = hashlib.sha256(CLI_AGENT_PATH.read_bytes()).hexdigest()
            h2 = hashlib.sha256(CLI_LOCAL_BIN_PATH.read_bytes()).hexdigest()
            self.assertEqual(h1, h2, "realmz-foundry.py SHA-256 mismatch between asset-foundry/bin and .local/bin")


if __name__ == "__main__":
    unittest.main()
