"""Unit tests for cartography/monster_redraw_pipeline.py."""

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
    compute_sheet_qa_stats,
    generate_hero_sheet_normals,
)
from monster_redraw_pipeline import (  # noqa: E402
    MONSTER_SPECS,
    build_clean_monster_master,
    build_monster_qa_contact_sheet,
    main as cli_main,
    normalize_monster_role,
    process_monster,
    synthesize_monster_animations,
)
from pixel_postprocess import DEFAULT_PALETTE_PATH, load_palette  # noqa: E402


def _make_synthetic_orc_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Create a synthetic high-res white-background left-facing orc warrior sprite."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)

    # Subtle ground shadow smudge under feet that remove_background_1bit should strip
    img[454:468, 165:345, :3] = [232, 232, 236]

    # Legs / boots (y 368..454)
    img[368:454, 192:240, :3] = [71, 42, 26]
    img[368:454, 272:320, :3] = [71, 42, 26]
    img[434:454, 184:242, :3] = [46, 27, 17]
    img[434:454, 268:326, :3] = [46, 27, 17]

    # Muscular torso & iron armor (y 195..372, x 165..345)
    img[195:372, 165:345, :3] = [66, 102, 67]     # Olive-green orc hide
    img[210:345, 185:325, :3] = [124, 136, 158]   # Iron breastplate
    img[290:312, 172:338, :3] = [153, 40, 56]     # Crimson war belt

    # Cleaver raised on left side (facing left)
    img[145:290, 128:156, :3] = [216, 226, 240]
    img[278:298, 120:166, :3] = [130, 81, 50]

    # Head & tusks (y 115..198)
    img[118:198, 205:302, :3] = [90, 130, 84]
    img[145:162, 215:245, :3] = [227, 82, 89]     # Red eyes
    img[175:192, 205:225, :3] = [247, 244, 234]   # Left tusk

    return img


def _make_synthetic_frog_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Create a synthetic wide left-facing giant frog sprite on white studio backdrop."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)

    # Subtle ground shadow smudge under webbed feet
    img[372:386, 100:410, :3] = [232, 233, 236]

    # Webbed hind & front feet at bottom (y 318..372)
    img[325:372, 118:188, :3] = [46, 105, 59]
    img[318:372, 290:392, :3] = [46, 105, 59]

    # Wide squat body & pale throat sac (y 180..336, x 110..395)
    img[190:336, 118:390, :3] = [61, 135, 72]     # Emerald frog hide
    img[235:322, 126:235, :3] = [220, 215, 206]   # Pale throat sac on left
    img[178:230, 150:220, :3] = [82, 166, 83]     # Brow / eyes
    img[192:212, 160:185, :3] = [255, 201, 82]    # Amber eye highlight
    img[205:265, 250:360, :3] = [46, 74, 51]      # Mossy dorsal mottling

    return img


def _make_synthetic_zombie_white_bg(w: int = 512, h: int = 512) -> np.ndarray:
    """Create a synthetic high-res white-background left-facing zombie sprite."""
    img = np.full((h, w, 4), [255, 255, 255, 255], dtype=np.uint8)
    img[375:452, 200:240, :3] = [60, 76, 115]
    img[375:452, 272:312, :3] = [60, 76, 115]
    img[205:378, 178:334, :3] = [119, 161, 104]   # Pale undead skin/tunic
    img[225:345, 196:316, :3] = [82, 78, 75]      # Tattered cloth
    img[124:208, 212:298, :3] = [153, 191, 126]   # Head
    img[152:168, 222:245, :3] = [227, 82, 89]     # Glowing eye
    return img


class TestMonsterRedrawPipeline(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.palette_rgb = load_palette(DEFAULT_PALETTE_PATH)
        cls.pal_set = {tuple(int(c) for c in row) for row in cls.palette_rgb}
        cls.raw_orc = _make_synthetic_orc_white_bg()
        cls.raw_frog = _make_synthetic_frog_white_bg()
        cls.raw_zombie = _make_synthetic_zombie_white_bg()

        cls.master_orc = build_clean_monster_master(
            cls.raw_orc,
            role="monster_orc_warrior",
            palette_rgb=cls.palette_rgb,
        )
        cls.master_frog = build_clean_monster_master(
            cls.raw_frog,
            role="monster_giant_frog",
            palette_rgb=cls.palette_rgb,
        )
        cls.master_zombie = build_clean_monster_master(
            cls.raw_zombie,
            role="monster_zombie",
            palette_rgb=cls.palette_rgb,
        )

    def _assert_master_valid(self, master: np.ndarray, role: str) -> None:
        spec = MONSTER_SPECS[role]
        lo_h, hi_h = spec["height_range"]
        max_w = spec["max_fit_w"]

        self.assertEqual(master.shape, (CANVAS_H, CANVAS_W, 4))
        self.assertEqual(master.dtype, np.uint8)
        self.assertEqual(set(int(v) for v in np.unique(master[..., 3])), {0, 255})

        op = master[..., 3] == 255
        ys, xs = np.where(op)
        self.assertGreater(len(ys), 300)
        self.assertEqual(int(ys.max()), GROUND_Y)
        op_h = int(ys.max() - ys.min() + 1)
        op_w = int(xs.max() - xs.min() + 1)

        self.assertGreaterEqual(int(xs.min()), 2)
        self.assertLessEqual(int(xs.max()), 125)
        self.assertGreaterEqual(int(ys.min()), 2)
        self.assertLessEqual(op_w, max_w)
        # Either inside height_range or width-capped cleanly at <= hi_h
        if op_w < max_w - 2:
            self.assertGreaterEqual(op_h, lo_h)
        self.assertLessEqual(op_h, hi_h)

        used = {tuple(int(c) for c in row) for row in master[op, :3]}
        self.assertEqual(len(used - self.pal_set), 0)

    def test_normalize_and_master_framing_all_types(self) -> None:
        self.assertEqual(normalize_monster_role("giant_frog"), "monster_giant_frog")
        self.assertEqual(normalize_monster_role("Monster Orc-Warrior"), "monster_orc_warrior")
        self.assertEqual(normalize_monster_role("zombie"), "monster_zombie")

        self._assert_master_valid(self.master_orc, "monster_orc_warrior")
        self._assert_master_valid(self.master_frog, "monster_giant_frog")
        self._assert_master_valid(self.master_zombie, "monster_zombie")

        # Verify flip_horizontal produces mirrored cutout
        flipped = build_clean_monster_master(
            self.raw_orc,
            role="orc_warrior",
            palette_rgb=self.palette_rgb,
            flip_horizontal=True,
        )
        self._assert_master_valid(flipped, "monster_orc_warrior")
        self.assertFalse(np.array_equal(flipped, self.master_orc))

    def test_orc_warrior_animations_left_attack_and_palette(self) -> None:
        sheets = synthesize_monster_animations(
            self.master_orc,
            role="monster_orc_warrior",
            palette_rgb=self.palette_rgb,
        )
        self.assertEqual(set(sheets.keys()), {"sheet", "walk_sheet", "attack_sheet"})

        for name, strip in sheets.items():
            self.assertEqual(strip.shape, (CANVAS_H, SHEET_W, 4), msg=name)
            self.assertTrue(set(int(v) for v in np.unique(strip[..., 3])).issubset({0, 255}))

            op = strip[..., 3] == 255
            used = {tuple(int(c) for c in row) for row in strip[op, :3]}
            self.assertEqual(len(used - self.pal_set), 0, msg=f"Off-palette pixels in {name}")

            frames = [strip[:, f * CANVAS_W : (f + 1) * CANVAS_W] for f in range(4)]
            for f_idx, fr in enumerate(frames):
                self.assertFalse(np.any(fr[:, :2, 3] > 0), msg=f"{name} frame {f_idx} clipped left")
                self.assertFalse(np.any(fr[:, 126:, 3] > 0), msg=f"{name} frame {f_idx} clipped right")
                self.assertFalse(np.any(fr[126:, :, 3] > 0), msg=f"{name} frame {f_idx} clipped bottom")
                ys = np.where(fr[..., 3] == 255)[0]
                if name in ("sheet", "attack_sheet"):
                    self.assertEqual(int(ys.max()), GROUND_Y)
                elif name == "walk_sheet":
                    if f_idx in (0, 2):
                        self.assertEqual(int(ys.max()), GROUND_Y)
                    else:
                        self.assertIn(int(ys.max()), (123, 124))

            for f_idx in range(4):
                a = frames[f_idx]
                b = frames[(f_idx + 1) % 4]
                union_op = (a[..., 3] == 255) | (b[..., 3] == 255)
                diff_pct = 100.0 * float(np.sum(np.any(a != b, axis=-1))) / float(max(1, np.sum(union_op)))
                self.assertGreater(diff_pct, 0.5, msg=f"{name} diff({f_idx}->{(f_idx+1)%4}) too small")

        # Verify leftward (-X) attack strike: lunge frame 1 reaches further left than coil frame 0
        atk = sheets["attack_sheet"]
        f0_xs = np.where(atk[:, :CANVAS_W, 3] == 255)[1]
        f1_xs = np.where(atk[:, CANVAS_W : 2 * CANVAS_W, 3] == 255)[1]
        self.assertLess(int(f1_xs.min()), int(f0_xs.min()))

    def test_giant_frog_hop_cycle_and_tongue_lash(self) -> None:
        sheets = synthesize_monster_animations(
            self.master_frog,
            role="monster_giant_frog",
            palette_rgb=self.palette_rgb,
        )
        self.assertEqual(set(sheets.keys()), {"sheet", "walk_sheet", "attack_sheet"})

        walk = sheets["walk_sheet"]
        walk_frames = [walk[:, f * CANVAS_W : (f + 1) * CANVAS_W] for f in range(4)]
        bottoms = [int(np.where(fr[..., 3] == 255)[0].max()) for fr in walk_frames]
        self.assertEqual(bottoms, [125, 122, 125, 124])

        # Verify distinct frames on frog idle, walk, and attack
        for name, strip in sheets.items():
            op = strip[..., 3] == 255
            used = {tuple(int(c) for c in row) for row in strip[op, :3]}
            self.assertEqual(len(used - self.pal_set), 0, msg=f"Off-palette in frog {name}")
            frames = [strip[:, f * CANVAS_W : (f + 1) * CANVAS_W] for f in range(4)]
            for f_idx in range(4):
                a = frames[f_idx]
                b = frames[(f_idx + 1) % 4]
                union_op = (a[..., 3] == 255) | (b[..., 3] == 255)
                diff_pct = 100.0 * float(np.sum(np.any(a != b, axis=-1))) / float(max(1, np.sum(union_op)))
                self.assertGreater(diff_pct, 0.5, msg=f"Frog {name} diff({f_idx}->{(f_idx+1)%4}) too small")

        # Verify frog tongue lash extends left on attack frame 1
        atk = sheets["attack_sheet"]
        f0_min_x = int(np.where(atk[:, :CANVAS_W, 3] == 255)[1].min())
        f1_min_x = int(np.where(atk[:, CANVAS_W : 2 * CANVAS_W, 3] == 255)[1].min())
        self.assertLess(f1_min_x, f0_min_x - 8)

    def test_zombie_idle_only_normals_and_qa_contact_sheets(self) -> None:
        zombie_sheets = synthesize_monster_animations(
            self.master_zombie,
            role="monster_zombie",
            palette_rgb=self.palette_rgb,
        )
        self.assertEqual(set(zombie_sheets.keys()), {"sheet"})
        z_idle = zombie_sheets["sheet"]
        z_norm = generate_hero_sheet_normals(z_idle)
        self.assertEqual(z_norm.shape, (CANVAS_H, SHEET_W, 4))

        op = z_idle[..., 3] == 255
        self.assertTrue(np.all(z_norm[~op] == np.array([128, 128, 255, 0], dtype=np.uint8)))
        nz = (z_norm[op, 2].astype(np.float32) - 127.5) / 127.5
        self.assertGreaterEqual(float(nz.min()), 0.345)

        stats = compute_sheet_qa_stats(z_idle, z_norm, self.palette_rgb)
        self.assertEqual(stats["off_palette_count"], 0)
        self.assertTrue(stats["normal_bg_neutral_ok"])

        with tempfile.TemporaryDirectory(prefix="realmz_monster_redraw_test_") as td:
            td_path = Path(td)
            z_qa = td_path / "zombie_qa.png"
            build_monster_qa_contact_sheet(
                "monster_zombie",
                zombie_sheets,
                {"sheet": z_norm},
                z_qa,
            )
            self.assertTrue(z_qa.exists())
            with Image.open(z_qa) as img:
                self.assertEqual(img.size[0], 512 * 4 + 16)
                # 1-state QA has 3 rows (2 composites + 1 normal)
                self.assertGreater(img.size[1], 128 * 4 * 3)

            # Full 3-state QA + CLI manifest execution (dict with flip_horizontal option + simple path)
            orc_p = td_path / "raw_orc.png"
            frog_p = td_path / "raw_frog.png"
            zombie_p = td_path / "raw_zombie.png"
            Image.fromarray(self.raw_orc, mode="RGBA").save(orc_p)
            Image.fromarray(self.raw_frog, mode="RGBA").save(frog_p)
            Image.fromarray(self.raw_zombie, mode="RGBA").save(zombie_p)

            res_frog = process_monster(
                role="giant_frog",
                input_path=frog_p,
                out_dir=td_path / "out_single",
                qa_dir=td_path / "qa_single",
                promote_dir=td_path / "promote_single",
            )
            self.assertEqual(res_frog["role"], "monster_giant_frog")
            self.assertEqual(len(res_frog["outputs"]), 6)
            self.assertEqual(len(res_frog["promoted"]), 6)
            self.assertTrue(Path(res_frog["qa_contact_sheet"]).exists())

            manifest_p = td_path / "manifest.json"
            manifest_p.write_text(
                json.dumps(
                    {
                        "monster_orc_warrior": {"path": str(orc_p), "flip_horizontal": True},
                        "monster_zombie": str(zombie_p),
                    }
                ),
                encoding="utf-8",
            )
            cli_out = td_path / "cli_out"
            cli_qa = td_path / "cli_qa"
            cli_main(["--manifest", str(manifest_p), "--out-dir", str(cli_out), "--qa-dir", str(cli_qa)])
            self.assertTrue((cli_out / "monster_orc_warrior_attack_sheet.png").exists())
            self.assertTrue((cli_out / "monster_orc_warrior_attack_sheet_n.png").exists())
            self.assertTrue((cli_qa / "monster_orc_warrior_qa.png").exists())
            self.assertTrue((cli_out / "monster_zombie_sheet.png").exists())
            self.assertTrue((cli_out / "monster_zombie_sheet_n.png").exists())
            self.assertFalse((cli_out / "monster_zombie_walk_sheet.png").exists())
            self.assertTrue((cli_qa / "monster_zombie_qa.png").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
