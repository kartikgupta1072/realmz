"""Unit tests for cartography/hero_redraw_pipeline.py."""

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

from hero_redraw_pipeline import (  # noqa: E402
    CANVAS_H,
    CANVAS_W,
    GROUND_Y,
    SHEET_W,
    build_clean_hero_master,
    build_hero_qa_contact_sheet,
    compute_sheet_qa_stats,
    enforce_cobblestone_ink_outline,
    generate_hero_sheet_normals,
    main as cli_main,
    process_all_heroes,
    process_hero,
    synthesize_hero_animations,
)
from pixel_postprocess import DEFAULT_PALETTE_PATH, load_palette, rgb_to_lab_float  # noqa: E402


def _make_synthetic_hero_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Create a synthetic high-res white-background hero sprite (knight/paladin silhouette)."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)

    # Subtle ground shadow smudge under feet that remove_background_1bit should strip
    img[452:466, 175:335, :3] = [232, 232, 235]

    # Boots / lower legs (y 370..452)
    img[370:452, 195:238, :3] = [71, 42, 26]
    img[370:452, 274:317, :3] = [71, 42, 26]
    img[432:452, 188:240, :3] = [46, 27, 17]
    img[432:452, 272:324, :3] = [46, 27, 17]

    # Tunic / armor torso (y 210..374, x 172..340)
    img[210:374, 172:340, :3] = [154, 167, 189]  # Pale steel breastplate
    img[220:360, 195:317, :3] = [52, 82, 161]    # Royal cobalt surcoat
    img[290:312, 172:340, :3] = [206, 134, 41]   # Gold belt

    # Pale cape / shoulder pauldrons
    img[205:355, 148:180, :3] = [216, 226, 240]  # Very pale silver edge to test outline guard
    img[215:305, 336:364, :3] = [220, 215, 206]

    # Helmet & pale plume (y 110..215)
    img[140:215, 210:302, :3] = [216, 226, 240]
    img[155:190, 226:286, :3] = [217, 155, 124]  # Face/visor opening
    img[110:145, 234:282, :3] = [245, 118, 115]  # Plume

    # Sword raised on right side (x 352..386, y 145..310)
    img[145:290, 360:378, :3] = [238, 244, 252]
    img[282:298, 344:392, :3] = [236, 169, 56]

    # Bright arcane/gem orb highlight on chest
    img[244:268, 244:268, :3] = [133, 230, 218]

    return img


class TestHeroRedrawPipeline(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.palette_rgb = load_palette(DEFAULT_PALETTE_PATH)
        cls.pal_set = {tuple(int(c) for c in row) for row in cls.palette_rgb}
        cls.raw_hero = _make_synthetic_hero_white_bg()
        cls.master = build_clean_hero_master(cls.raw_hero, palette_rgb=cls.palette_rgb)

    def test_master_framing_and_palette(self) -> None:
        master = self.master
        self.assertEqual(master.shape, (CANVAS_H, CANVAS_W, 4))
        self.assertEqual(master.dtype, np.uint8)

        alpha_vals = set(int(v) for v in np.unique(master[..., 3]))
        self.assertEqual(alpha_vals, {0, 255})

        op = master[..., 3] == 255
        ys, xs = np.where(op)
        self.assertGreater(len(ys), 500)
        self.assertEqual(int(ys.max()), GROUND_Y)
        op_h = int(ys.max() - ys.min() + 1)
        self.assertGreaterEqual(op_h, 88)
        self.assertLessEqual(op_h, 96)
        self.assertGreaterEqual(int(xs.min()), 2)
        self.assertLessEqual(int(xs.max()), 125)

        used = {tuple(int(c) for c in row) for row in master[op, :3]}
        self.assertEqual(len(used - self.pal_set), 0)

        # Verify 1px outer perimeter has dark ink contrast (no pale L > 28 border pixels)
        eroded = np.zeros_like(op)
        eroded[1:-1, 1:-1] = (
            op[:-2, 1:-1] & op[2:, 1:-1] & op[1:-1, :-2] & op[1:-1, 2:]
        )
        perim = op & (~eroded)
        perim_lab = rgb_to_lab_float(master[perim, :3])
        self.assertLessEqual(float(perim_lab[:, 0].max()), 28.5)

    def test_cobblestone_outline_guard_on_pale_box(self) -> None:
        # Directly verify enforce_cobblestone_ink_outline on a pale sprite grounded at y=125
        pale = np.zeros((128, 128, 4), dtype=np.uint8)
        pale[40:126, 40:88] = [238, 244, 252, 255]  # Bright steel block, bottom y=125
        guarded = enforce_cobblestone_ink_outline(pale)
        ys = np.where(guarded[..., 3] == 255)[0]
        # Must remain grounded at y = 125 (never leak into y = 126)
        self.assertEqual(int(ys.max()), 125)
        # Outer border at y=39 (expanded 1px contour) must be #11131F
        self.assertTrue(np.all(guarded[39, 40:88, :3] == np.array([17, 19, 31], dtype=np.uint8)))
        # Bottom border at y=125 must be snapped to #11131F
        self.assertTrue(np.all(guarded[125, 40:88, :3] == np.array([17, 19, 31], dtype=np.uint8)))

    def test_animation_sheets_distinct_grounded_and_palette_locked(self) -> None:
        sheets = synthesize_hero_animations(self.master, role="hero_knight", palette_rgb=self.palette_rgb)
        self.assertEqual(set(sheets.keys()), {"sheet", "walk_sheet", "attack_sheet"})

        for name, strip in sheets.items():
            self.assertEqual(strip.shape, (CANVAS_H, SHEET_W, 4), msg=name)
            alpha_vals = set(int(v) for v in np.unique(strip[..., 3]))
            self.assertTrue(alpha_vals.issubset({0, 255}), msg=name)

            op = strip[..., 3] == 255
            used = {tuple(int(c) for c in row) for row in strip[op, :3]}
            self.assertEqual(len(used - self.pal_set), 0, msg=f"Off-palette pixels in {name}")

            frames = [strip[:, f * CANVAS_W : (f + 1) * CANVAS_W] for f in range(4)]
            for f_idx, fr in enumerate(frames):
                # Strict boundary isolation: columns 0,1 and 126,127 empty, bottom <= 125
                self.assertFalse(np.any(fr[:, :2, 3] > 0), msg=f"{name} frame {f_idx} clipped left")
                self.assertFalse(np.any(fr[:, 126:, 3] > 0), msg=f"{name} frame {f_idx} clipped right")
                self.assertFalse(np.any(fr[126:, :, 3] > 0), msg=f"{name} frame {f_idx} clipped bottom")
                ys = np.where(fr[..., 3] == 255)[0]
                if name == "sheet":
                    self.assertEqual(int(ys.max()), GROUND_Y, msg=f"Idle frame {f_idx} not grounded")
                elif name == "walk_sheet":
                    if f_idx in (0, 2):
                        self.assertEqual(int(ys.max()), GROUND_Y, msg=f"Walk contact frame {f_idx}")
                    else:
                        self.assertIn(int(ys.max()), (123, 124), msg=f"Walk passing frame {f_idx}")
                elif name == "attack_sheet":
                    self.assertEqual(int(ys.max()), GROUND_Y, msg=f"Attack frame {f_idx} not grounded")

            # Measurable inter-frame pixel diff > 0.5% between all adjacent frames
            for f_idx in range(4):
                a = frames[f_idx]
                b = frames[(f_idx + 1) % 4]
                union_op = (a[..., 3] == 255) | (b[..., 3] == 255)
                diff_pct = 100.0 * float(np.sum(np.any(a != b, axis=-1))) / float(max(1, np.sum(union_op)))
                self.assertGreater(diff_pct, 0.5, msg=f"{name} diff({f_idx}->{(f_idx+1)%4}) too small: {diff_pct}%")

        # Also check class-specific attack VFX roles stay strictly quantized
        for role in ("hero_ranger", "hero_cleric", "hero_mage"):
            r_sheets = synthesize_hero_animations(self.master, role=role, palette_rgb=self.palette_rgb)
            atk = r_sheets["attack_sheet"]
            op_r = atk[..., 3] == 255
            used_r = {tuple(int(c) for c in row) for row in atk[op_r, :3]}
            self.assertEqual(len(used_r - self.pal_set), 0, msg=f"Off-palette VFX for {role}")

    def test_per_frame_normal_maps_isolated_and_valid(self) -> None:
        sheets = synthesize_hero_animations(self.master, role="hero_knight", palette_rgb=self.palette_rgb)
        idle = sheets["sheet"]
        norm_sheet = generate_hero_sheet_normals(idle)
        self.assertEqual(norm_sheet.shape, (CANVAS_H, SHEET_W, 4))

        op = idle[..., 3] == 255
        nz = (norm_sheet[op, 2].astype(np.float32) - 127.5) / 127.5
        self.assertGreaterEqual(float(nz.min()), 0.345)  # uint8 rounding around 0.35

        bg = norm_sheet[~op]
        self.assertTrue(np.all(bg == np.array([128, 128, 255, 0], dtype=np.uint8)))

        # Zero boundary bleed: frame 0 normal computed alone must match slice 0 of sheet normal
        f0_alone = generate_hero_sheet_normals(idle[:, :CANVAS_W])
        self.assertTrue(np.array_equal(f0_alone, norm_sheet[:, :CANVAS_W]))

        stats = compute_sheet_qa_stats(idle, norm_sheet, self.palette_rgb)
        self.assertEqual(stats["off_palette_count"], 0)
        self.assertTrue(stats["normal_bg_neutral_ok"])
        self.assertGreater(stats["normal_xy_std"], 0.05)

    def test_qa_contact_sheet_and_cli_pipeline(self) -> None:
        with tempfile.TemporaryDirectory(prefix="realmz_hero_redraw_test_") as td:
            td_path = Path(td)
            raw_p = td_path / "raw_knight.png"
            out_dir = td_path / "out"
            qa_dir = td_path / "qa"
            prom_dir = td_path / "promote"
            Image.fromarray(self.raw_hero, mode="RGBA").save(raw_p)

            res = process_hero(
                role="knight",
                input_path=raw_p,
                out_dir=out_dir,
                qa_dir=qa_dir,
                promote_dir=prom_dir,
            )
            self.assertEqual(res["role"], "hero_knight")
            self.assertEqual(len(res["outputs"]), 6)
            self.assertEqual(len(res["promoted"]), 6)
            qa_png = Path(res["qa_contact_sheet"])
            self.assertTrue(qa_png.exists())

            with Image.open(qa_png) as qa_img:
                # 4x magnification of 512px strip width + 16px margin = 2064
                self.assertEqual(qa_img.size[0], 512 * 4 + 16)
                self.assertGreater(qa_img.size[1], 128 * 4 * 9)

            # Test manifest + CLI execution
            manifest_p = td_path / "manifest.json"
            manifest_p.write_text(
                json.dumps({"heroes": [{"role": "hero_mage", "input": str(raw_p)}]}),
                encoding="utf-8",
            )
            cli_out = td_path / "cli_out"
            cli_qa = td_path / "cli_qa"
            cli_main(["--manifest", str(manifest_p), "--out-dir", str(cli_out), "--qa-dir", str(cli_qa)])
            self.assertTrue((cli_out / "hero_mage_attack_sheet.png").exists())
            self.assertTrue((cli_out / "hero_mage_attack_sheet_n.png").exists())
            self.assertTrue((cli_qa / "hero_mage_qa.png").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
