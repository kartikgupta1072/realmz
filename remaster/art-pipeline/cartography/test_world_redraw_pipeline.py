"""Unit tests for cartography/world_redraw_pipeline.py."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from pixel_postprocess import DEFAULT_PALETTE_PATH, load_palette, rgb_to_lab_float  # noqa: E402
from world_redraw_pipeline import (  # noqa: E402
    ASSET_SPECS,
    UNITY_CART_ROOT,
    _enforce_ink_outline_size,
    build_surface_oil_sheet,
    main as cli_main,
    process_npc_sheet,
    process_portrait_128,
    process_static_asset,
    resolve_bywater_inn_size,
    run_wave,
)


def _make_synthetic_prop_white_bg(w: int = 512, h: int = 384) -> np.ndarray:
    """Synthetic archway/wagon prop on white studio backdrop with enclosed white pocket."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    # Faint bottom cast-shadow smudge that remove_background_1bit should strip
    img[338:354, 110:400, :3] = [232, 232, 236]

    # Stone arch pillars + lintel surrounding an enclosed pure-white studio opening
    img[72:338, 120:188, :3] = [135, 129, 122]
    img[72:338, 324:392, :3] = [135, 129, 122]
    img[58:138, 120:392, :3] = [194, 188, 178]
    # Moss patches & pale limestone cap
    img[66:96, 150:250, :3] = [90, 130, 84]
    img[110:210, 124:152, :3] = [240, 236, 228]
    # Timber brace near base
    img[260:338, 136:174, :3] = [130, 81, 50]
    img[260:338, 338:376, :3] = [130, 81, 50]
    return img


def _make_synthetic_building_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Synthetic medieval cottage with bright enclosed pure-white plaster & warm windows."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    # Stone foundation & timber frame
    img[230:460, 100:412, :3] = [99, 60, 36]
    img[405:460, 96:416, :3] = [135, 129, 122]
    # Enclosed bright pure-white sunlit plaster wall inside timber frame (must stay opaque!)
    img[248:398, 124:388, :3] = [255, 255, 255]
    # Timber crossbeams dividing plaster into enclosed pockets
    img[248:398, 248:264, :3] = [71, 42, 26]
    img[316:332, 124:388, :3] = [71, 42, 26]
    # Warm sunlit window panes
    img[268:304, 160:210, :3] = [255, 243, 176]
    img[268:304, 302:352, :3] = [255, 224, 125]
    # Terracotta roof overhang
    for row in range(110, 244):
        half = int((row - 100) * 1.28)
        x0 = max(72, 256 - half)
        x1 = min(440, 256 + half)
        img[row, x0:x1, :3] = [196, 93, 51]
    return img


def _make_synthetic_portrait_bust(w: int = 512, h: int = 512) -> np.ndarray:
    """Synthetic character bust on pure-white studio backdrop."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    # Shoulders / cloak
    img[320:490, 96:416, :3] = [52, 82, 161]
    img[335:490, 130:382, :3] = [154, 167, 189]
    # Neck & head
    img[260:340, 215:297, :3] = [217, 155, 124]
    img[115:270, 178:334, :3] = [237, 184, 154]
    # Hair & circlet
    img[88:165, 170:342, :3] = [163, 105, 67]
    img[125:142, 182:330, :3] = [255, 201, 82]
    # Eyes
    img[182:198, 212:232, :3] = [38, 127, 130]
    img[182:198, 280:300, :3] = [38, 127, 130]
    return img


def _make_synthetic_npc_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Synthetic standing town NPC on white studio backdrop."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    img[375:452, 204:242, :3] = [71, 42, 26]
    img[375:452, 270:308, :3] = [71, 42, 26]
    img[205:378, 176:336, :3] = [130, 81, 50]
    img[225:365, 198:314, :3] = [194, 188, 178]
    img[120:208, 212:300, :3] = [237, 184, 154]
    img[108:142, 210:302, :3] = [99, 60, 36]
    return img


class TestWorldRedrawPipeline(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.palette_rgb = load_palette(DEFAULT_PALETTE_PATH)
        cls.pal_set = {tuple(int(c) for c in row) for row in cls.palette_rgb}

    def _assert_palette_and_1bit_alpha(self, rgba: np.ndarray, expected_shape: Tuple[int, int, int]) -> None:
        self.assertEqual(rgba.shape, expected_shape)
        self.assertEqual(rgba.dtype, np.uint8)
        alpha_vals = {int(v) for v in np.unique(rgba[..., 3])}
        self.assertTrue(alpha_vals.issubset({0, 255}))
        op = rgba[..., 3] == 255
        self.assertTrue(np.any(op))
        used = {tuple(int(c) for c in row) for row in rgba[op, :3]}
        self.assertEqual(len(used - self.pal_set), 0)

    def _assert_valid_normals(self, albedo: np.ndarray, normal: np.ndarray, min_z: float = 0.35) -> None:
        self.assertEqual(normal.shape, albedo.shape)
        op = albedo[..., 3] == 255
        nz = (normal[op, 2].astype(np.float32) - 127.5) / 127.5
        self.assertGreaterEqual(float(nz.min()), min_z)
        bg = normal[~op]
        if len(bg) > 0:
            self.assertTrue(np.all(bg == np.array([128, 128, 255, 0], dtype=np.uint8)))

    def test_no_em_or_en_dashes_in_pipeline_files(self) -> None:
        for fname in ("world_redraw_pipeline.py", "test_world_redraw_pipeline.py"):
            text = (_THIS_DIR / fname).read_text(encoding="utf-8")
            self.assertNotIn("\u2014", text, msg=f"Em-dash found in {fname}")
            self.assertNotIn("\u2013", text, msg=f"En-dash found in {fname}")

    def test_prop_punches_white_pocket_and_enforces_outline(self) -> None:
        raw_prop = _make_synthetic_prop_white_bg()
        albedo, normal = process_static_asset(
            raw_prop,
            target_size=(192, 160),
            mode="prop",
            palette_rgb=self.palette_rgb,
        )
        self._assert_palette_and_1bit_alpha(albedo, (160, 192, 4))
        self._assert_valid_normals(albedo, normal, min_z=0.35)

        # Enclosed white arch opening in the middle should be punched transparent
        center_alpha = albedo[70:115, 86:106, 3]
        self.assertEqual(int(np.sum(center_alpha == 0)), center_alpha.size)

        # Bottom baseline must not drop past H - 3
        ys = np.where(albedo[..., 3] == 255)[0]
        self.assertLessEqual(int(ys.max()), 160 - 3)

        # Direct check on _enforce_ink_outline_size baseline guard
        pale = np.zeros((112, 128, 4), dtype=np.uint8)
        pale[30 : 112 - 2, 30:98] = [238, 244, 252, 255]
        guarded = _enforce_ink_outline_size(pale, safe_pad=2)
        g_ys = np.where(guarded[..., 3] == 255)[0]
        self.assertLessEqual(int(g_ys.max()), 112 - 3)
        self.assertTrue(np.all(guarded[29, 30:98, :3] == np.array([17, 19, 31], dtype=np.uint8)))
        self.assertTrue(np.all(guarded[109, 30:98, :3] == np.array([17, 19, 31], dtype=np.uint8)))

    def test_building_preserves_enclosed_white_plaster_and_windows(self) -> None:
        raw_bld = _make_synthetic_building_white_bg()
        albedo, normal = process_static_asset(
            raw_bld,
            target_size=(256, 256),
            mode="building",
            palette_rgb=self.palette_rgb,
        )
        self._assert_palette_and_1bit_alpha(albedo, (256, 256, 4))
        self._assert_valid_normals(albedo, normal, min_z=0.35)

        # Enclosed sunlit plaster pockets inside the cottage wall must stay 100% opaque
        plaster_region = albedo[145:195, 90:165, 3]
        self.assertTrue(np.all(plaster_region == 255))

    def test_surface_oil_sheet_coverage_holes_and_frames(self) -> None:
        albedo, normal = build_surface_oil_sheet(palette_rgb=self.palette_rgb)
        self._assert_palette_and_1bit_alpha(albedo, (128, 512, 4))
        self._assert_valid_normals(albedo, normal, min_z=0.35)

        from scipy import ndimage

        frames = [albedo[:, f * 128 : (f + 1) * 128] for f in range(4)]
        for idx, fr in enumerate(frames):
            op = fr[..., 3] == 255
            cov_pct = 100.0 * float(np.sum(op)) / float(128 * 128)
            self.assertGreaterEqual(cov_pct, 68.0, msg=f"Frame {idx} coverage too low: {cov_pct:.2f}%")
            self.assertLessEqual(cov_pct, 78.0, msg=f"Frame {idx} coverage too high: {cov_pct:.2f}%")
            # Zero interior holes inside pool silhouette
            filled = ndimage.binary_fill_holes(op)
            self.assertTrue(np.array_equal(op, filled), msg=f"Interior hole in oil frame {idx}")

        # Frames must animate subtly across all 4 steps
        for idx in range(4):
            a = frames[idx]
            b = frames[(idx + 1) % 4]
            self.assertGreater(int(np.sum(np.any(a != b, axis=-1))), 80)

    def test_portrait_128_with_reference_and_fallback_frame(self) -> None:
        bust = _make_synthetic_portrait_bust()
        ref_path = UNITY_CART_ROOT / "Portraits" / "portrait_knight.png"
        albedo_ref, normal_ref = process_portrait_128(
            bust,
            frame_ref_path=ref_path,
            palette_rgb=self.palette_rgb,
        )
        self.assertEqual(albedo_ref.shape, (128, 128, 4))
        self.assertTrue(np.all(albedo_ref[..., 3] == 255))
        self._assert_palette_and_1bit_alpha(albedo_ref, (128, 128, 4))
        self.assertEqual(normal_ref.shape, (128, 128, 4))
        self.assertTrue(np.all(normal_ref[..., 3] == 255))

        # Check gold frame border copied on top row center
        self.assertGreater(int(albedo_ref[2, 64, 0]), 180)

        # Also test fallback gold frame when reference path does not exist
        albedo_fb, normal_fb = process_portrait_128(
            bust,
            frame_ref_path=Path("nonexistent_portrait_frame_ref.png"),
            palette_rgb=self.palette_rgb,
        )
        self._assert_palette_and_1bit_alpha(albedo_fb, (128, 128, 4))
        self.assertTrue(np.all(albedo_fb[..., 3] == 255))
        self.assertEqual(normal_fb.shape, (128, 128, 4))

    def test_npc_sheet_512x128(self) -> None:
        raw_npc = _make_synthetic_npc_white_bg()
        sheet, normal = process_npc_sheet(raw_npc, role="npc_blacksmith", palette_rgb=self.palette_rgb)
        self._assert_palette_and_1bit_alpha(sheet, (128, 512, 4))
        self._assert_valid_normals(sheet, normal, min_z=0.35)
        frames = [sheet[:, f * 128 : (f + 1) * 128] for f in range(4)]
        for idx, fr in enumerate(frames):
            ys = np.where(fr[..., 3] == 255)[0]
            op_h = int(ys.max() - ys.min() + 1)
            self.assertGreaterEqual(op_h, 85, msg=f"NPC frame {idx} height {op_h}")
            self.assertLessEqual(op_h, 94, msg=f"NPC frame {idx} height {op_h}")

    def test_asset_specs_and_wave_cli_promotion(self) -> None:
        self.assertEqual(ASSET_SPECS["bywater_inn"]["size"], resolve_bywater_inn_size())
        self.assertEqual(ASSET_SPECS["broken_archway"]["size"], (192, 160))
        self.assertEqual(ASSET_SPECS["building_guildhall"]["size"], (384, 320))

        with tempfile.TemporaryDirectory(prefix="realmz_world_redraw_test_") as td:
            td_path = Path(td)
            prop_raw = td_path / "arch.png"
            bld_raw = td_path / "inn.png"
            port_raw = td_path / "mage_bust.png"
            npc_raw = td_path / "blacksmith.png"
            Image.fromarray(_make_synthetic_prop_white_bg(), mode="RGBA").save(prop_raw)
            Image.fromarray(_make_synthetic_building_white_bg(), mode="RGBA").save(bld_raw)
            Image.fromarray(_make_synthetic_portrait_bust(), mode="RGBA").save(port_raw)
            Image.fromarray(_make_synthetic_npc_white_bg(), mode="RGBA").save(npc_raw)

            manifest_p = td_path / "manifest.json"
            manifest_p.write_text(
                json.dumps(
                    {
                        "broken_archway": str(prop_raw),
                        "bywater_inn": str(bld_raw),
                        "portrait_mage": str(port_raw),
                        "npc_blacksmith": str(npc_raw),
                    }
                ),
                encoding="utf-8",
            )

            out_dir = td_path / "out"
            qa_dir = td_path / "qa"
            prom_root = td_path / "promote_cartography"

            cli_main(
                [
                    "--wave",
                    "all",
                    "--manifest",
                    str(manifest_p),
                    "--out-dir",
                    str(out_dir),
                    "--qa-dir",
                    str(qa_dir),
                    "--promote",
                    "--promote-root",
                    str(prom_root),
                ]
            )

            # Verify staged + promoted subfolders & exact dimensions
            expected = [
                ("Props/broken_archway.png", (192, 160)),
                ("Surfaces/surface_oil.png", (512, 128)),
                ("Portraits/portrait_mage.png", (128, 128)),
                ("units/npc_blacksmith_sheet.png", (512, 128)),
                ("structures/bywater_inn.png", resolve_bywater_inn_size()),
            ]
            for rel, sz in expected:
                p_out = out_dir / rel
                p_prom = prom_root / rel
                p_n = prom_root / (str(Path(rel).with_suffix("")) + "_n.png")
                self.assertTrue(p_out.exists(), msg=f"Missing staged {p_out}")
                self.assertTrue(p_prom.exists(), msg=f"Missing promoted {p_prom}")
                self.assertTrue(p_n.exists(), msg=f"Missing promoted normal {p_n}")
                with Image.open(p_prom) as im:
                    self.assertEqual(im.size, sz, msg=rel)

            # Verify QA sheets
            self.assertTrue((qa_dir / "broken_archway_qa.png").exists())
            self.assertTrue((qa_dir / "surface_oil_qa.png").exists())
            self.assertTrue((qa_dir / "portraits_qa.png").exists())
            self.assertTrue((qa_dir / "npc_blacksmith_qa.png").exists())
            self.assertTrue((qa_dir / "bywater_inn_qa.png").exists())

            # Test single-wave filtering via run_wave
            props_only = run_wave(
                wave="props",
                manifest_path=manifest_p,
                out_dir=td_path / "out_props",
                qa_dir=td_path / "qa_props",
                promote=False,
            )
            self.assertEqual(set(props_only.keys()), {"broken_archway", "surface_oil"})


if __name__ == "__main__":
    unittest.main(verbosity=2)
