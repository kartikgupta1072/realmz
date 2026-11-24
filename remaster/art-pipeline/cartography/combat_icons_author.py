"""
Deterministic 32x32 RGBA pixel-art combat hotbar & utility icon author for Realmz Remaster.

Authors 21 crisp 32x32 icons directly on the integer 32x32 pixel grid (zero high-res
downscaling, zero mixels, strict 1-bit alpha {0, 255}, strict 1px dark ink outline,
upper-left sunlit lighting, and 100% quantization to palette_soc_sunlit_128.json).
Also builds hotbar_icons_sheet.png (4x zoomed grid on dark slate UI button frames).
"""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw, ImageFont

CREATE_NO_WINDOW = 0x08000000

PALETTE_PATH = Path(r"C:\realmz\remaster\art-pipeline\palette_soc_sunlit_128.json")
OUT_ICONS_DIR = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_combat_art_pack\output\icons")
OUT_SHEET_PATH = Path(r"c:\Users\karti\LifeTwin\scratch\realmz_combat_art_pack\hotbar_icons_sheet.png")

# Named palette tokens verified directly against palette_soc_sunlit_128.json
PAL: Dict[str, str] = {
    # Ink & anchors
    "K": "#11131F",
    "k": "#1C2033",
    "w": "#F7F4EA",
    "W": "#FFFFFF",
    # Slate indigo shadow
    "I0": "#232B42",
    "I1": "#2F3A59",
    "I2": "#3E4C73",
    "I3": "#50628F",
    "I4": "#687CAE",
    "I5": "#869BC9",
    "I6": "#A8BBE0",
    "I7": "#CBD8F2",
    # Gleaming steel
    "S0": "#323845",
    "S1": "#475061",
    "S2": "#606B80",
    "S3": "#7C889E",
    "S4": "#9AA7BD",
    "S5": "#BAC6DA",
    "S6": "#D8E2F0",
    "S7": "#EEF4FC",
    # Sunlit gold amber
    "G0": "#593212",
    "G1": "#7D4818",
    "G2": "#A6641F",
    "G3": "#CE8629",
    "G4": "#ECA938",
    "G5": "#FFC952",
    "G6": "#FFE07D",
    "G7": "#FFF3B0",
    # Crimson scarlet
    "R0": "#4A141E",
    "R1": "#701D2B",
    "R2": "#992838",
    "R3": "#C23846",
    "R4": "#E35259",
    "R5": "#F57673",
    "R6": "#FFA399",
    "R7": "#FFCCC2",
    # Emerald turf
    "E0": "#183622",
    "E1": "#224D2E",
    "E2": "#2E693B",
    "E3": "#3D8748",
    "E4": "#52A653",
    "E5": "#6EC260",
    "E6": "#8FD96E",
    "E7": "#B4EC86",
    "E8": "#D4F7A8",
    "E9": "#E8FCCA",
    # Sunlit sandstone
    "N0": "#4D3C2E",
    "N1": "#6B5542",
    "N2": "#8A7058",
    "N3": "#AA8D70",
    "N4": "#C8AC8C",
    "N5": "#E0C8A8",
    "N6": "#F0DEC2",
    "N7": "#FAF0DC",
    # Timber leather earth
    "L0": "#2E1B11",
    "L1": "#472A1A",
    "L2": "#633C24",
    "L3": "#825132",
    "L4": "#A36943",
    "L5": "#C2855A",
    "L6": "#DC9F73",
    "L7": "#EDBC95",
    "L8": "#F7D6B8",
    # Terracotta russet (flame orange)
    "O0": "#4D2014",
    "O1": "#73311D",
    "O2": "#9C4527",
    "O3": "#C45D33",
    "O4": "#E07A48",
    "O5": "#F29D6B",
    "O6": "#FCC29A",
    # Cerulean water
    "C0": "#123652",
    "C1": "#1A4E75",
    "C2": "#226A9C",
    "C3": "#2D88C2",
    "C4": "#3FA8E0",
    "C5": "#60C6F2",
    "C6": "#8CE0FA",
    "C7": "#BCF0FF",
    "C8": "#E0F9FF",
    # Arcane teal cyan
    "T0": "#123D42",
    "T1": "#1B5C61",
    "T2": "#267F82",
    "T3": "#36A6A3",
    "T4": "#54C9C0",
    "T5": "#85E6DA",
    "T6": "#BDF7EE",
    # Royal cobalt sapphire
    "B0": "#1C2952",
    "B1": "#273C78",
    "B2": "#3452A1",
    "B3": "#476EC7",
    "B4": "#638CE3",
    "B5": "#8AB0F5",
    "B6": "#B8D2FC",
    # Royal plum violet
    "V0": "#2E1940",
    "V1": "#482763",
    "V2": "#673A8A",
    "V3": "#8A52B3",
    "V4": "#AE73D6",
    "V5": "#CE9BF0",
    "V6": "#E6C4FA",
    # Warm skin parchment
    "P0": "#5C3426",
    "P1": "#7D4B38",
    "P2": "#9E634B",
    "P3": "#BF7E62",
    "P4": "#D99B7C",
    "P5": "#EDB89A",
    "P6": "#F7D1B7",
    "P7": "#FCE4D0",
}


def hex_to_rgba(hex_str: str) -> Tuple[int, int, int, int]:
    h = hex_str.strip().lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


def load_palette_rgb_set(path: Path = PALETTE_PATH) -> set[Tuple[int, int, int]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    out = set()
    for c in data["colors"]:
        r, g, b, _ = hex_to_rgba(c)
        out.add((r, g, b))
    return out


class PixelCanvas:
    """Direct 32x32 integer pixel grid with 1-bit alpha and ink outline helpers."""

    def __init__(self, width: int = 32, height: int = 32) -> None:
        self.w = width
        self.h = height
        self.grid = np.zeros((height, width, 4), dtype=np.uint8)

    def _resolve(self, tok: Optional[str]) -> Tuple[int, int, int, int]:
        if tok is None or tok == ".":
            return (0, 0, 0, 0)
        if tok.startswith("#"):
            return hex_to_rgba(tok)
        return hex_to_rgba(PAL[tok])

    def pset(self, x: int, y: int, tok: Optional[str]) -> None:
        xi, yi = int(round(x)), int(round(y))
        if 0 <= xi < self.w and 0 <= yi < self.h:
            self.grid[yi, xi] = self._resolve(tok)

    def pget(self, x: int, y: int) -> Tuple[int, int, int, int]:
        if 0 <= x < self.w and 0 <= y < self.h:
            t = self.grid[y, x]
            return (int(t[0]), int(t[1]), int(t[2]), int(t[3]))
        return (0, 0, 0, 0)

    def is_opaque(self, x: int, y: int) -> bool:
        return 0 <= x < self.w and 0 <= y < self.h and self.grid[y, x, 3] == 255

    def line(self, x0: int, y0: int, x1: int, y1: int, tok: str) -> None:
        x0, y0, x1, y1 = int(x0), int(y0), int(x1), int(y1)
        dx = abs(x1 - x0)
        dy = -abs(y1 - y0)
        sx = 1 if x0 < x1 else -1
        sy = 1 if y0 < y1 else -1
        err = dx + dy
        while True:
            self.pset(x0, y0, tok)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def rect(self, x0: int, y0: int, x1: int, y1: int, tok: Optional[str]) -> None:
        for y in range(min(y0, y1), max(y0, y1) + 1):
            for x in range(min(x0, x1), max(x0, x1) + 1):
                self.pset(x, y, tok)

    def fill_ellipse(self, cx: float, cy: float, rx: float, ry: float, tok: Optional[str]) -> None:
        for y in range(self.h):
            for x in range(self.w):
                dx = (x - cx) / rx
                dy = (y - cy) / ry
                if dx * dx + dy * dy <= 1.0:
                    self.pset(x, y, tok)

    def fill_poly(self, pts: Sequence[Tuple[float, float]], tok: Optional[str]) -> None:
        n = len(pts)
        if n < 3:
            return
        for y in range(self.h):
            for x in range(self.w):
                px, py = x + 0.0, y + 0.0
                inside = False
                j = n - 1
                for i in range(n):
                    xi, yi = pts[i]
                    xj, yj = pts[j]
                    if ((yi > py) != (yj > py)) and (
                        px < (xj - xi) * (py - yi) / ((yj - yi) if (yj - yi) != 0 else 1e-9) + xi
                    ):
                        inside = not inside
                    j = i
                if inside:
                    self.pset(x, y, tok)

    def stamp(self, rows: Sequence[str], cmap: Dict[str, Optional[str]], ox: int = 0, oy: int = 0) -> None:
        for dy, row in enumerate(rows):
            for dx, ch in enumerate(row):
                if ch in (" ", "."):
                    continue
                if ch in cmap:
                    tok = cmap[ch]
                    if tok is not None:
                        self.pset(ox + dx, oy + dy, tok)

    def apply_4conn_outline(self, ink_tok: str = "K") -> None:
        """Add a crisp 1px 4-connected dark ink outline around all existing opaque pixels."""
        ink = self._resolve(ink_tok)
        alpha = self.grid[:, :, 3] == 255
        outline = np.zeros((self.h, self.w), dtype=bool)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            shifted = np.zeros_like(alpha)
            y_src = slice(max(0, -dy), min(self.h, self.h - dy))
            y_dst = slice(max(0, dy), min(self.h, self.h + dy))
            x_src = slice(max(0, -dx), min(self.w, self.w - dx))
            x_dst = slice(max(0, dx), min(self.w, self.w + dx))
            shifted[y_dst, x_dst] = alpha[y_src, x_src]
            outline |= shifted
        outline &= ~alpha
        self.grid[outline] = ink

    def apply_8conn_outline(self, ink_tok: str = "K") -> None:
        """Add a 1px 8-connected dark ink outline around all existing opaque pixels."""
        ink = self._resolve(ink_tok)
        alpha = self.grid[:, :, 3] == 255
        outline = np.zeros((self.h, self.w), dtype=bool)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                shifted = np.zeros_like(alpha)
                y_src = slice(max(0, -dy), min(self.h, self.h - dy))
                y_dst = slice(max(0, dy), min(self.h, self.h + dy))
                x_src = slice(max(0, -dx), min(self.w, self.w - dx))
                x_dst = slice(max(0, dx), min(self.w, self.w + dx))
                shifted[y_dst, x_dst] = alpha[y_src, x_src]
                outline |= shifted
        outline &= ~alpha
        self.grid[outline] = ink

    def bbox(self) -> Optional[Tuple[int, int, int, int]]:
        ys, xs = np.where(self.grid[:, :, 3] > 0)
        if len(xs) == 0:
            return None
        return (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))

    def center_in_frame(self, min_margin: int = 2) -> None:
        """Shift opaque pixels by integer pixels so silhouette is centered with >= min_margin."""
        bb = self.bbox()
        if bb is None:
            return
        x0, y0, x1, y1 = bb
        bw = x1 - x0 + 1
        bh = y1 - y0 + 1
        tx0 = max(min_margin, min(self.w - min_margin - bw, (self.w - bw) // 2))
        ty0 = max(min_margin, min(self.h - min_margin - bh, (self.h - bh) // 2))
        dx = tx0 - x0
        dy = ty0 - y0
        if dx == 0 and dy == 0:
            return
        new_grid = np.zeros_like(self.grid)
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                nx, ny = x + dx, y + dy
                if 0 <= nx < self.w and 0 <= ny < self.h:
                    new_grid[ny, nx] = self.grid[y, x]
        self.grid = new_grid

    def to_image(self) -> Image.Image:
        return Image.fromarray(self.grid, mode="RGBA")


def build_icon_sword() -> PixelCanvas:
    c = PixelCanvas()
    # Diagonal steel broadsword from pommel (6,25) to tip (26,5)
    # Blade spine along x + y = 31, d = x - y from -11 to 21
    for y in range(4, 27):
        for x in range(4, 27):
            s = x + y
            d = x - y
            if 28 <= s <= 34 and -9 <= d <= 21:
                # Tip taper near d >= 16
                half_w = 2 if d < 17 else (1 if d < 20 else 0)
                if abs(s - 31) <= half_w:
                    if s < 31:
                        col = "S7" if d >= 12 else ("S6" if s == 29 else "S5")
                    elif s == 31:
                        col = "S7" if d >= 18 else ("S3" if d >= -4 else "S4")
                    else:
                        col = "S1" if s == 33 else "S2"
                    if d == 21:
                        col = "S7"
                    c.pset(x, y, col)
    # Golden crossguard along d = -10..-8, s from 23..39
    for off in range(-6, 7):
        gx = 11 + off
        gy = 21 - off
        #Wait: perpendicular to x-y = const is x+y = const! So dx=+1, dy=+1!
        pass
    for t in range(-6, 7):
        gx = 11 + t
        gy = 20 + t
        if abs(t) >= 5:
            c.pset(gx, gy, "G6" if t < 0 else "G3")
            c.pset(gx + 1, gy, "G4" if t < 0 else "G2")
        else:
            c.pset(gx, gy, "G6" if t <= 0 else "G4")
            c.pset(gx + 1, gy, "G5" if t <= 0 else "G2")
            c.pset(gx, gy + 1, "G3" if t <= 0 else "G1")
    # Center jewel on crossguard
    c.pset(11, 20, "G7")
    c.pset(12, 21, "G4")
    # Grip from (10,21) down-left to (7,24)
    for i in range(1, 5):
        gx = 11 - i
        gy = 21 + i
        c.pset(gx, gy, "R3" if i % 2 == 1 else "L3")
        c.pset(gx - 1, gy, "R5" if i % 2 == 1 else "L4")
        c.pset(gx, gy + 1, "R1" if i % 2 == 1 else "L1")
    # Golden pommel at (6, 25)
    c.fill_ellipse(5.5, 25.5, 1.6, 1.6, "G4")
    c.pset(5, 25, "G7")
    c.pset(6, 26, "G2")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_bow() -> PixelCanvas:
    c = PixelCanvas()
    # Diagonal recurve bow facing up-right; grip around (11, 20)
    # Upper limb curving to (7, 7) with recurve hook (9, 5)
    bow_pts = [
        (9, 5), (8, 5), (7, 6), (7, 8), (8, 10), (8, 13), (9, 16), (10, 18),
        (11, 20),
        (13, 21), (15, 22), (18, 23), (21, 23), (23, 24), (25, 24), (26, 23), (26, 22)
    ]
    # Thick limbs with highlight on top-left and shadow bottom-right
    for i, (bx, by) in enumerate(bow_pts):
        if i in (0, 1, 15, 16):
            c.pset(bx, by, "G5")
            c.pset(bx + 1, by + 1, "G2")
        elif 7 <= i <= 9:
            c.pset(bx, by, "G5")
            c.pset(bx + 1, by + 1, "G3")
            c.pset(bx + 1, by, "G6")
        else:
            c.pset(bx, by, "L5")
            c.pset(bx + 1, by, "L4")
            c.pset(bx + 1, by + 1, "L2")
    # Taut bowstring from (9, 6) to (25, 22)
    c.line(9, 6, 25, 22, "N5")
    # Make sure string doesn't overwrite grip
    c.pset(11, 20, "G5")
    c.pset(12, 20, "G6")
    c.pset(12, 21, "G3")
    # Arrow from (8, 23) to broadhead tip (26, 5)
    c.line(8, 23, 24, 7, "L6")
    # Fletching at (7..10, 21..24)
    c.pset(7, 24, "R5")
    c.pset(8, 24, "R4")
    c.pset(7, 23, "w")
    c.pset(8, 22, "R5")
    c.pset(9, 24, "R3")
    c.pset(9, 21, "w")
    # Steel broadhead at (23..26, 5..8)
    c.pset(26, 5, "S7")
    c.pset(25, 5, "S6")
    c.pset(25, 6, "S7")
    c.pset(24, 6, "S5")
    c.pset(26, 6, "S4")
    c.pset(25, 7, "S3")
    c.pset(23, 6, "S5")
    c.pset(25, 8, "S2")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_dagger() -> PixelCanvas:
    c = PixelCanvas()
    # Sleek roguish curved silver dagger pointing top-right (26, 5)
    # Crimson-wrapped grip from (5, 26) to (10, 21)
    for i in range(5):
        hx = 5 + i
        hy = 26 - i
        c.pset(hx, hy, "R4" if i % 2 == 0 else "R2")
        c.pset(hx + 1, hy, "R2" if i % 2 == 0 else "R1")
        c.pset(hx, hy - 1, "R5" if i % 2 == 0 else "R3")
    # Gold pommel
    c.pset(4, 27, "G5")
    c.pset(5, 27, "G3")
    c.pset(4, 26, "G6")
    # Curved gold assassin S-guard at (9..13, 19..23)
    for px, py, col in [
        (8, 19, "G6"), (9, 19, "G7"), (10, 20, "G5"),
        (11, 21, "G4"), (12, 22, "G3"), (13, 23, "G2"),
        (10, 21, "G5"),
    ]:
        c.pset(px, py, col)
    # Sleek curved silver blade tapering cleanly to a needle point at (26, 5)
    # Upper back edge (brightest steel) + lower curved belly + sharp taper
    blade_rows = [
        # (x, y_top, thickness)
        (11, 19, 3),
        (12, 18, 3),
        (13, 17, 3),
        (14, 15, 3),
        (15, 14, 3),
        (16, 13, 3),
        (17, 12, 3),
        (18, 11, 2),
        (19, 10, 2),
        (20, 9, 2),
        (21, 8, 2),
        (22, 7, 2),
        (23, 6, 2),
        (24, 6, 1),
        (25, 5, 1),
        (26, 5, 1),
    ]
    for idx, (bx, by, th) in enumerate(blade_rows):
        if th == 3:
            c.pset(bx, by, "S7")
            c.pset(bx, by + 1, "S4" if idx < 6 else "S5")
            c.pset(bx + 1, by + 1, "S3")
            c.pset(bx + 1, by + 2, "S1")
        elif th == 2:
            c.pset(bx, by, "S7")
            c.pset(bx, by + 1, "S5")
            c.pset(bx + 1, by + 1, "S2")
        else:
            c.pset(bx, by, "S7" if idx >= 14 else "S6")
    # Subtle crimson/dark fuller blood-groove near hilt
    c.pset(13, 18, "S2")
    c.pset(14, 17, "S2")
    c.pset(15, 16, "S2")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_staff() -> PixelCanvas:
    c = PixelCanvas()
    # Gnarled oak staff from bottom-left (5, 26) to cradle (19, 12)
    shaft = [
        (5, 26), (6, 25), (7, 24), (8, 23), (9, 22), (10, 21), (11, 20),
        (12, 19), (13, 18), (14, 17), (15, 16), (16, 15), (17, 14), (18, 13), (19, 12)
    ]
    for i, (sx, sy) in enumerate(shaft):
        c.pset(sx, sy, "L4")
        c.pset(sx, sy - 1, "L5" if i % 4 != 2 else "L6")
        c.pset(sx + 1, sy, "L2")
        if i in (4, 10):
            # Gnarled wood knot
            c.pset(sx - 1, sy - 1, "L5")
            c.pset(sx + 1, sy + 1, "L1")
    # Brass ferrule & cradle prongs holding arcane crystal
    c.pset(18, 12, "G6")
    c.pset(19, 12, "G5")
    c.pset(19, 13, "G3")
    c.pset(20, 13, "G2")
    # Left & right cradle horns
    for px, py, col in [
        (18, 10, "G6"), (17, 8, "G6"), (18, 7, "G5"),
        (21, 13, "G4"), (23, 13, "G4"), (24, 12, "G3"),
        (19, 11, "G5"), (20, 12, "G4"),
    ]:
        c.pset(px, py, col)
    # Single-char crystal stamp
    c.stamp(
        [
            "..65..",
            ".6W43.",
            "6W532.",
            "54321.",
            ".321..",
            "..2...",
        ],
        {"6": "T6", "W": "W", "5": "T5", "4": "T4", "3": "T3", "2": "T2", "1": "T1"},
        ox=19,
        oy=5,
    )
    # Glowing arcane motes around crystal
    c.pset(15, 6, "T5")
    c.pset(26, 6, "T6")
    c.pset(26, 10, "T4")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_fireball() -> PixelCanvas:
    c = PixelCanvas()
    # Blazing sphere at top-right (20, 11) with organic sweeping flame tail to bottom-left (5, 25)
    c.fill_poly([(6, 24), (10, 15), (17, 7), (24, 9), (24, 16), (16, 20)], "R3")
    c.fill_ellipse(20.0, 11.0, 6.2, 6.2, "R4")
    # Organic curved flame licks along outer edges
    for px, py, col in [
        (7, 20, "R4"), (8, 17, "R4"), (10, 13, "R4"), (12, 10, "R4"),
        (14, 7, "R4"), (17, 5, "R4"), (22, 5, "R4"),
        (10, 24, "R3"), (13, 22, "R3"), (17, 20, "R3"), (22, 18, "R2"),
        (25, 13, "R3"), (26, 9, "R4"),
    ]:
        c.pset(px, py, col)
    # Mid orange body + sweeping streak
    c.fill_poly([(9, 21), (13, 14), (19, 8), (23, 12), (17, 17)], "O4")
    c.fill_ellipse(20.0, 11.0, 4.5, 4.5, "O5")
    c.pset(15, 7, "O5")
    c.pset(24, 9, "O4")
    # Bright gold core + curved flame streak
    c.fill_ellipse(20.0, 11.0, 2.9, 2.9, "G5")
    c.line(11, 20, 19, 12, "G5")
    c.line(12, 19, 19, 12, "G6")
    c.line(14, 16, 18, 12, "G6")
    # White-hot specular core
    c.fill_ellipse(19.5, 10.5, 1.5, 1.5, "G7")
    c.pset(19, 10, "W")
    c.pset(20, 10, "W")
    # Detached bright ember sparks in tail
    c.pset(5, 26, "O5")
    c.pset(7, 26, "G6")
    c.pset(5, 22, "G5")
    c.pset(14, 24, "O4")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_flame_hands() -> PixelCanvas:
    c = PixelCanvas()
    # Surging golden-orange flame plume in center (y=4..22)
    c.fill_poly([(9, 21), (10, 13), (14, 5), (16, 4), (19, 10), (22, 16), (22, 21), (16, 23)], "R4")
    c.fill_poly([(7, 16), (8, 9), (11, 13), (10, 18)], "O4")
    c.fill_poly([(24, 16), (23, 9), (20, 13), (21, 18)], "O4")
    # Inner orange flame
    c.fill_poly([(11, 21), (12, 14), (16, 7), (20, 14), (20, 21), (16, 22)], "O5")
    # Bright golden flame core
    c.fill_poly([(13, 21), (14, 15), (16, 10), (18, 15), (19, 21)], "G6")
    c.line(16, 13, 16, 20, "G7")
    c.pset(15, 16, "W")
    c.pset(16, 16, "W")
    c.pset(16, 17, "W")
    # Clear cupped open casting hands (fingers curled inward, wrists at bottom-left/right)
    left_hand = [
        "...6.....",
        "..65.6...",
        ".654.56..",
        "6543.45..",
        "5432234..",
        "4322223..",
        ".32222...",
        "..2111...",
        "..1100...",
    ]
    right_hand = [
        ".....5...",
        "...5.43..",
        "..54.321.",
        "..43.210.",
        "..322100.",
        "..221100.",
        "...2110..",
        "...1100..",
        "...000...",
    ]
    skin_map = {"6": "P7", "5": "P6", "4": "P5", "3": "P4", "2": "P3", "1": "P2", "0": "P1"}
    c.stamp(left_hand, skin_map, ox=4, oy=18)
    c.stamp(right_hand, skin_map, ox=19, oy=18)
    # Floating fire sparks
    c.pset(11, 6, "G6")
    c.pset(21, 6, "G6")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_magic_darts() -> PixelCanvas:
    c = PixelCanvas()
    # Three parallel arcane cerulean/cyan bolts streaking up-right
    bolts = [
        (17, 6, 8),   # upper-left bolt (tip x, tip y, length)
        (26, 7, 13),  # main center bolt
        (25, 17, 10), # lower-right bolt
    ]
    for tx, ty, length in bolts:
        # Tail
        for i in range(2, length):
            bx = tx - i
            by = ty + i
            col = "C6" if i < 4 else ("C5" if i < 6 else ("C4" if i < 8 else "C2"))
            c.pset(bx, by, col)
            if i < length - 2:
                c.pset(bx, by - 1, "C6" if i < 5 else "C3")
                c.pset(bx + 1, by, "C3" if i < 5 else "B2")
        # Bright spearhead
        c.pset(tx, ty, "W")
        c.pset(tx - 1, ty, "C8")
        c.pset(tx, ty + 1, "C7")
        c.pset(tx - 1, ty + 1, "W")
        c.pset(tx - 2, ty, "C6")
        c.pset(tx, ty + 2, "C5")
        c.pset(tx - 2, ty + 1, "C7")
        c.pset(tx - 1, ty + 2, "C6")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_heal() -> PixelCanvas:
    c = PixelCanvas()
    # Ornate golden chalice in lower half + glowing emerald-cyan healing cross/sparkle above
    # Chalice bowl (y=15..21)
    chalice = [
        "111111111111",
        "655544443321",
        ".6554443321.",
        "..55443321..",
        "...443321...",
        ".....43.....",
        ".....43.....",
        ".....43.....",
        "...554322...",
        "..65443321..",
    ]
    gold_map = {"6": "G7", "5": "G6", "4": "G5", "3": "G4", "2": "G3", "1": "G2"}
    c.stamp(chalice, gold_map, ox=10, oy=16)
    # Glowing emerald-cyan pool inside rim of chalice
    c.line(11, 16, 20, 16, "T5")
    c.line(12, 15, 19, 15, "E7")
    c.pset(14, 15, "E9")
    c.pset(15, 15, "W")
    # Crisp luminous emerald-cyan healing cross/sparkle hovering above (center 16, 9)
    # Cross arms
    c.rect(14, 5, 17, 13, "E6")
    c.rect(11, 7, 20, 10, "E6")
    # Sunlit top-left shading on cross
    c.rect(14, 5, 16, 12, "E7")
    c.rect(11, 7, 19, 9, "E7")
    c.line(12, 8, 19, 8, "E8")
    c.line(15, 6, 15, 11, "E9")
    c.pset(15, 8, "W")
    c.pset(16, 8, "E9")
    # Shadow edge bottom-right on cross
    c.line(12, 10, 20, 10, "E4")
    c.line(17, 6, 17, 12, "E4")
    # Golden healing motes
    c.pset(8, 11, "G6")
    c.pset(23, 12, "G6")
    c.pset(9, 17, "G7")
    c.pset(24, 18, "G5")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_buff() -> PixelCanvas:
    c = PixelCanvas()
    # Royal-cobalt shield/crest in lower center (x=9..22, y=13..27)
    for y in range(14, 28):
        half = 6 if y < 21 else max(0, 6 - (y - 20))
        for x in range(16 - half, 16 + half):
            if y == 14 or x == 16 - half or x == 15 + half:
                c.pset(x, y, "S6" if x < 16 else "S3")
            elif x < 16:
                c.pset(x, y, "B4" if y < 20 else "B3")
            else:
                c.pset(x, y, "B2" if y < 20 else "B1")
    # Bold radiant sunlit-gold ascending double chevron / wing aura surging above shield
    # Lower chevron
    for i in range(8):
        for t in range(3):
            c.pset(15 - i, 12 + i + t, "G6" if t == 0 else ("G5" if t == 1 else "G3"))
            c.pset(16 + i, 12 + i + t, "G5" if t == 0 else ("G4" if t == 1 else "G2"))
    # Upper crown chevron
    for i in range(9):
        for t in range(3):
            c.pset(15 - i, 5 + i + t, "G7" if t == 0 else ("G5" if t == 1 else "G3"))
            c.pset(16 + i, 5 + i + t, "G6" if t == 0 else ("G4" if t == 1 else "G2"))
    c.pset(15, 5, "W")
    c.pset(16, 5, "G7")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_daze() -> PixelCanvas:
    c = PixelCanvas()
    # Continuous smooth tilted violet dizziness orbit ring around center (16, 16)
    for deg in range(0, 360, 2):
        rad = math.radians(deg)
        ex = 10.2 * math.cos(rad)
        ey = 4.5 * math.sin(rad)
        ang = -0.32
        rx = 16.0 + ex * math.cos(ang) - ey * math.sin(ang)
        ry = 16.0 + ex * math.sin(ang) + ey * math.cos(ang)
        col = "V6" if 130 <= deg <= 250 else ("V5" if deg < 130 else "V3")
        c.pset(int(round(rx)), int(round(ry)), col)
        c.pset(int(round(rx)), int(round(ry)) + 1, "V2")
    # Inner spiral swirl ribbon
    for deg in range(0, 360, 3):
        rad = math.radians(deg)
        r_scale = 0.25 + 0.75 * (deg / 360.0)
        rx = 16.0 + 5.6 * r_scale * math.cos(rad)
        ry = 16.0 + 2.8 * r_scale * math.sin(rad)
        c.pset(int(round(rx)), int(round(ry)), "V5" if deg < 180 else "V4")

    # Crisp 4-point sunlit-gold stun stars
    def draw_star(cx: int, cy: int, size: int) -> None:
        c.pset(cx, cy, "W")
        for d in range(1, size + 1):
            col = "G7" if d == 1 else ("G6" if d == 2 else "G4")
            c.pset(cx - d, cy, col)
            c.pset(cx + d, cy, col if d < size else "G3")
            c.pset(cx, cy - d, col)
            c.pset(cx, cy + d, col if d < size else "G3")
        if size >= 3:
            c.pset(cx - 1, cy - 1, "G6")
            c.pset(cx + 1, cy - 1, "G5")
            c.pset(cx - 1, cy + 1, "G5")
            c.pset(cx + 1, cy + 1, "G3")

    draw_star(9, 10, 4)
    draw_star(23, 20, 4)
    draw_star(22, 7, 3)
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_shove() -> PixelCanvas:
    c = PixelCanvas()
    # Armored steel gauntlet fist punching right: clear cuff, back-of-hand, 4 horizontal curled fingers, tucked thumb
    fist = [
        "....55544444..",
        "gg66554466543.",
        "gG554443kkkk2.",
        "gG54433355432.",
        "gG443332kkkk1.",
        "gD43322244321.",
        "gD332211kkkk0.",
        "..22111133210.",
        "...111554320..",
        "......22110...",
    ]
    fist_map = {
        "6": "S7", "5": "S6", "4": "S4", "3": "S3", "2": "S2", "1": "S1", "0": "S0",
        "k": "k",
        "g": "G6", "G": "G5", "D": "G3",
    }
    c.stamp(fist, fist_map, ox=4, oy=11)
    # Crisp kinetic shockwave arcs on right (x=20..27)
    for y in range(8, 24):
        dy = y - 16
        x1 = 22 - int(round((dy * dy) / 28.0))
        c.pset(x1, y, "W" if abs(dy) <= 3 else "C7")
        c.pset(x1 + 1, y, "C6" if abs(dy) <= 4 else "C4")
    for y in range(6, 26):
        dy = y - 16
        x2 = 26 - int(round((dy * dy) / 26.0))
        if abs(dy) % 2 == 0 or abs(dy) <= 6:
            c.pset(x2, y, "C7" if abs(dy) <= 5 else "C5")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_potion() -> PixelCanvas:
    c = PixelCanvas()
    # Classic faceted glass flask filled with glowing ruby-crimson elixir
    c.rect(14, 4, 17, 6, "L4")
    c.line(14, 4, 16, 4, "L6")
    c.pset(14, 5, "L5")
    c.pset(17, 6, "L2")
    # Flared glass lip & neck
    c.line(12, 7, 19, 7, "C7")
    c.pset(19, 7, "S4")
    c.rect(13, 8, 18, 11, "C6")
    c.line(17, 8, 18, 11, "S4")
    c.line(14, 8, 14, 11, "C8")
    # Faceted octagonal glass flask body (y=11..26)
    for y in range(11, 27):
        if y < 14:
            half = 4 + (y - 11) * 2
        elif y <= 21:
            half = 9
        else:
            half = 9 - (y - 21) * 2
        for x in range(16 - half, 16 + half):
            if y < 15:
                if x < 13:
                    c.pset(x, y, "C8" if x == 16 - half + 1 else "C7")
                elif x > 18:
                    c.pset(x, y, "S3" if x == 15 + half else "C5")
                else:
                    c.pset(x, y, "C6")
            else:
                # Faceted ruby-crimson elixir
                if y == 15:
                    c.pset(x, y, "R6" if x < 17 else "R5")
                elif x <= 11 and y <= 21:
                    c.pset(x, y, "R5" if y <= 18 else "R4")
                elif x >= 20 or y >= 24:
                    c.pset(x, y, "R1" if y >= 25 else "R2")
                elif x in (12, 19) and 16 <= y <= 21:
                    c.pset(x, y, "R5" if x == 12 else "R2")
                else:
                    c.pset(x, y, "R4" if x < 17 else "R3")
    # Facet bevel highlights, liquid bubble & glass specular glint
    c.pset(9, 16, "W")
    c.pset(9, 17, "W")
    c.pset(10, 15, "W")
    c.pset(11, 14, "C8")
    c.pset(10, 18, "R7")
    c.pset(14, 20, "R6")
    c.pset(15, 19, "R7")
    c.pset(18, 18, "R5")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_guard() -> PixelCanvas:
    c = PixelCanvas()
    # Sturdy royal-cobalt & sunlit-gold kite shield with crisp inner bevel & faceted steel boss
    for y in range(4, 28):
        if y <= 14:
            half = 10
        else:
            t = (y - 14) / 13.0
            half = max(1, int(round(10 * (1.0 - t * 0.92))))
        for x in range(16 - half, 16 + half):
            is_outer_rim = (y <= 5) or (x <= 16 - half + 1) or (x >= 15 + half - 1) or (y >= 26)
            is_inner_seam = (not is_outer_rim) and (
                (y == 6) or (x == 16 - half + 2) or (x == 15 + half - 2) or (y == 25)
            )
            if is_outer_rim:
                if x < 16 and y < 18:
                    c.pset(x, y, "G7" if (y == 4 and x < 12) else ("G6" if (y == 4 or x == 16 - half) else "G5"))
                elif x >= 16 and y >= 18:
                    c.pset(x, y, "G2")
                else:
                    c.pset(x, y, "G4")
            elif is_inner_seam:
                c.pset(x, y, "B0" if x >= 16 or y >= 18 else "B1")
            else:
                if x < 16 and y < 15:
                    c.pset(x, y, "B5")
                elif x < 16:
                    c.pset(x, y, "B4")
                elif y < 15:
                    c.pset(x, y, "B3")
                else:
                    c.pset(x, y, "B2")
    # Steel center cross-ribs + crisp faceted diamond boss
    c.line(15, 7, 15, 22, "S5")
    c.line(16, 7, 16, 22, "S3")
    for dy in range(-3, 4):
        hw = 3 - abs(dy)
        for dx in range(-hw, hw + 1):
            px, py = 15 + dx, 13 + dy
            if dx < 0 and dy < 0:
                col = "S7"
            elif dx <= 0 and dy <= 0:
                col = "W" if (dx == 0 and dy == 0) else "S6"
            elif dx > 0 and dy > 0:
                col = "S1"
            else:
                col = "S4"
            c.pset(px, py, col)
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_undo() -> PixelCanvas:
    c = PixelCanvas()
    # Crisp curved back-arrow in sunlit gold/cream
    # Circular loop sweeping from bottom-right (22, 23) up-right -> top -> left arrowhead at (5, 12)
    cx, cy = 16.5, 16.5
    for y in range(4, 28):
        for x in range(4, 28):
            dx = x - cx
            dy = y - cy
            dist = math.hypot(dx, dy)
            ang = math.degrees(math.atan2(dy, dx))
            # Arc from -160 deg through -90 (top), 0 (right) to +115 deg (bottom)
            if 5.5 <= dist <= 9.8 and (-165 <= ang <= 115):
                if dy < -4 or (dx < 0 and dy < 0):
                    col = "G7" if dist > 8.2 else "G6"
                elif dist > 8.5 and dy > 2:
                    col = "G3"
                elif dist < 6.5 and dy > 0:
                    col = "G2"
                else:
                    col = "G5" if dy < 3 else "G4"
                c.pset(x, y, col)
    # Bold triangular arrowhead on top-left pointing left-down to (5, 13)
    head = [
        (4, 12), (11, 5), (12, 17)
    ]
    c.fill_poly(head, "G5")
    c.line(4, 12, 11, 5, "G7")
    c.line(5, 12, 11, 6, "w")
    c.line(5, 13, 12, 17, "G3")
    c.pset(12, 16, "G2")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_end_turn() -> PixelCanvas:
    c = PixelCanvas()
    # Ornate brass/gold sand hourglass
    # Top and bottom brass caps
    c.rect(7, 4, 24, 6, "G4")
    c.line(7, 4, 23, 4, "G6")
    c.line(8, 5, 22, 5, "G5")
    c.line(8, 6, 24, 6, "G2")
    c.rect(7, 25, 24, 27, "G4")
    c.line(7, 25, 23, 25, "G6")
    c.line(8, 27, 24, 27, "G1")
    # Vertical side pillars in dark bronze/gold
    c.line(8, 7, 8, 24, "G4")
    c.line(9, 7, 9, 24, "G2")
    c.line(22, 7, 22, 24, "G3")
    c.line(23, 7, 23, 24, "G1")
    # Glass bulbs (y=7..24)
    for y in range(7, 25):
        dy = abs(y - 15.5)
        half = max(1, int(round(1.0 + (dy / 8.5) * 4.5)))
        for x in range(16 - half, 16 + half):
            if y <= 15:
                # Upper bulb: sand in lower half (y=11..15)
                if y >= 11 and (y >= 12 or abs(x - 15.5) <= 3):
                    c.pset(x, y, "G7" if x < 16 else "G5")
                else:
                    c.pset(x, y, "C8" if x < 14 else ("C6" if x < 18 else "S4"))
            else:
                # Lower bulb: trickle stream + pyramid sand pile at bottom (y=20..24)
                pile_w = max(0, y - 19)
                if abs(x - 15.5) <= pile_w:
                    c.pset(x, y, "G7" if x <= 15 else "G4")
                elif x in (15, 16) and y <= 20:
                    c.pset(x, y, "G7")
                else:
                    c.pset(x, y, "C8" if x < 14 else ("C6" if x < 18 else "S4"))
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_auto_combat() -> PixelCanvas:
    c = PixelCanvas()
    # Two crossed gleaming steel swords (X) + golden magic spark emblem at intersection
    # Sword 1: bottom-left (6,25) -> top-right (25,6)
    # Sword 2: bottom-right (25,25) -> top-left (6,6)
    for d in range(20):
        # Blade 1 (from 10,21 to 25,6)
        if d >= 5:
            x, y = 6 + d, 25 - d
            c.pset(x, y, "S6")
            c.pset(x - 1, y, "S7")
            c.pset(x, y + 1, "S3")
        # Blade 2 (from 21,21 to 6,6)
        if d >= 5:
            x, y = 25 - d, 25 - d
            c.pset(x, y, "S5")
            c.pset(x + 1, y, "S2")
            c.pset(x, y - 1, "S7")
    # Hilts & Crossguards
    # Left hilt (bottom-left)
    c.line(6, 25, 9, 22, "L4")
    c.pset(5, 26, "G5")
    c.line(7, 19, 12, 24, "G5")
    c.line(8, 19, 12, 23, "G6")
    # Right hilt (bottom-right)
    c.line(25, 25, 22, 22, "L3")
    c.pset(26, 26, "G4")
    c.line(24, 19, 19, 24, "G4")
    c.line(23, 19, 19, 23, "G5")
    # Golden magic spark/cog emblem at center (16, 15)
    cx, cy = 15, 15
    c.fill_ellipse(cx + 0.5, cy + 0.5, 2.5, 2.5, "G5")
    c.pset(cx, cy, "W")
    c.pset(cx + 1, cy, "G7")
    c.pset(cx, cy + 1, "G7")
    for k in range(1, 5):
        col = "G7" if k <= 2 else "G5"
        c.pset(cx - k, cy, col)
        c.pset(cx + k + 1, cy, col)
        c.pset(cx, cy - k, col)
        c.pset(cx, cy + k + 1, col)
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_speed() -> PixelCanvas:
    c = PixelCanvas()
    # Twin crisp sunlit-gold fast-forward arrowheads >>
    for ox in (4, 15):
        # Triangle from (ox, 7) -> (ox+10, 15.5) -> (ox, 24)
        for y in range(7, 25):
            dy = abs(y - 15.5)
            width = max(1, int(round(10.5 * (1.0 - dy / 9.0))))
            for dx in range(width):
                x = ox + dx
                if y <= 11 or dx == 0:
                    col = "G7" if y <= 11 else "G6"
                elif y >= 21:
                    col = "G2"
                elif y >= 18:
                    col = "G3"
                else:
                    col = "G5" if y <= 15 else "G4"
                c.pset(x, y, col)
    # Ink vertical seam between the two arrowheads so they stay super crisp
    for y in range(10, 22):
        c.pset(14, y, "K")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_audio() -> PixelCanvas:
    c = PixelCanvas()
    # Crisp steel & gold speaker cone on left + 2 acoustic sound wave arcs on right
    # Speaker box & cone (x=5..16, y=6..25)
    c.rect(5, 12, 9, 19, "S5")
    c.line(5, 12, 9, 12, "S7")
    c.line(5, 12, 5, 19, "S6")
    c.line(5, 19, 9, 19, "S2")
    # Flared horn cone
    for x in range(10, 17):
        t = x - 9  # 1..7
        half = 3 + t
        for y in range(15 - half, 17 + half):
            if x >= 15:
                # Golden speaker rim
                c.pset(x, y, "G6" if y < 14 else ("G4" if y < 20 else "G2"))
            else:
                c.pset(x, y, "S6" if y < 13 else ("S4" if y < 19 else "S2"))
    # Sound wave arcs on right
    for y in range(10, 22):
        dy = y - 15.5
        if abs(dy) <= 5.5:
            ax = 20 - int(round((dy * dy) / 14.0))
            c.pset(ax, y, "G7" if dy < 0 else "G5")
            c.pset(ax + 1, y, "G5" if dy < 0 else "G3")
    for y in range(6, 26):
        dy = y - 15.5
        if abs(dy) <= 9.5:
            ax = 25 - int(round((dy * dy) / 22.0))
            c.pset(ax, y, "G7" if dy < -2 else ("G5" if dy < 4 else "G3"))
            c.pset(ax + 1, y, "G4" if dy < 4 else "G2")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_grid() -> PixelCanvas:
    c = PixelCanvas()
    # 3x3 isometric tactical tile grid (sandstone/steel with bright active emerald center)
    # Each tile is a crisp isometric diamond: half-width=4, half-height=2.5
    # Grid stride: u=(+4, +2.5) or integer (+4, +2) / (+4, +3)
    # Using exact pixel tile centers on a 3x3 isometric lattice:
    # Step u = (4, 2), step v = (-4, 2) -> Wait: 3x3 spans x in [4..28], y in [9..23] -> let's use stride (4, 3) and (-4, 3)!
    # With stride (4, 3): tile height=6, width=8 -> 3x3 total width = 24px, total height = 18px!
    cx, cy = 15, 7
    for r in range(3):
        for col_i in range(3):
            tcx = cx + (col_i - r) * 4
            tcy = cy + (col_i + r) * 3 + 2
            is_active = (r == 1 and col_i == 1)
            is_alt = (r + col_i) % 2 == 0
            # Draw crisp 7x5 isometric diamond
            for dy in range(-2, 3):
                hw = 3 - abs(dy)
                for dx in range(-hw, hw + 1):
                    px, py = tcx + dx, tcy + dy
                    if is_active:
                        if dy < 0 or dx < 0:
                            c.pset(px, py, "E8" if dy == -1 and dx == 0 else "E7")
                        else:
                            c.pset(px, py, "E5")
                    elif is_alt:
                        c.pset(px, py, "N6" if dy < 0 else ("N5" if dx <= 0 else "N4"))
                    else:
                        c.pset(px, py, "S5" if dy < 0 else ("S4" if dx <= 0 else "S3"))
            # Subtle 1px dark bevel edge along bottom-right of each tile so tiles separate cleanly
            c.pset(tcx, tcy + 2, "E2" if is_active else "k")
            c.pset(tcx + 1, tcy + 2, "k")
            c.pset(tcx - 1, tcy + 2, "k")
            c.pset(tcx + 2, tcy + 1, "k")
            c.pset(tcx - 2, tcy + 1, "k")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_settings() -> PixelCanvas:
    c = PixelCanvas()
    # Exact 8-way symmetrical 24x24 pixel-art mechanical gear cog (steel teeth + gold ring)
    gear_rows = [
        "..........SSSS..........",
        "..........SSSS..........",
        "...SS.....SSSS.....SS...",
        "..SSSS..SSSSSSSS..SSSS..",
        "..SSSSSSSSSSSSSSSSSSSS..",
        "...SSSSSSSSSSSSSSSSSS...",
        "....SSSSSSGGGGSSSSSS....",
        "....SSSSGGGGGGGGSSSS....",
        "...SSSSGGG....GGGSSSS...",
        "...SSSSGG......GGSSSS...",
        "SSSSSSGG........GGSSSSSS",
        "SSSSSSGG........GGSSSSSS",
        "SSSSSSGG........GGSSSSSS",
        "SSSSSSGG........GGSSSSSS",
        "...SSSSGG......GGSSSS...",
        "...SSSSGGG....GGGSSSS...",
        "....SSSSGGGGGGGGSSSS....",
        "....SSSSSSGGGGSSSSSS....",
        "...SSSSSSSSSSSSSSSSSS...",
        "..SSSSSSSSSSSSSSSSSSSS..",
        "..SSSS..SSSSSSSS..SSSS..",
        "...SS.....SSSS.....SS...",
        "..........SSSS..........",
        "..........SSSS..........",
    ]
    for dy, row in enumerate(gear_rows):
        for dx, ch in enumerate(row):
            if ch == ".":
                continue
            x, y = 4 + dx, 4 + dy
            diag = (dx - 11.5) + (dy - 11.5)
            if ch == "G":
                if diag < -2.0:
                    c.pset(x, y, "G6")
                elif diag > 3.0:
                    c.pset(x, y, "G2")
                else:
                    c.pset(x, y, "G4")
            else:
                if diag < -6.0:
                    c.pset(x, y, "S7")
                elif diag < 0.0:
                    c.pset(x, y, "S5")
                elif diag > 7.0:
                    c.pset(x, y, "S1")
                else:
                    c.pset(x, y, "S3")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


def build_icon_flee() -> PixelCanvas:
    c = PixelCanvas()
    # Agile running winged-boot sprinting right: tall leather boot, crimson cuff, 3-feather swept wing, speed lines
    c.line(4, 15, 7, 15, "w")
    c.line(3, 19, 7, 19, "G6")
    c.line(5, 23, 8, 23, "G5")
    boot_wing = [
        "WW.................",
        "WWGG...............",
        ".WWGGGg............",
        "WWGGGgg....RRRRRR..",
        ".WWGGGg....RrrrRR..",
        "..WWGGgg...LLLLLL..",
        "...WGGGgg..LLllLL..",
        "....GGGGgg.LLllLL..",
        ".....GGGggLLLllLL..",
        ".......GGLLLLllLLLL",
        "........LLLLLLLLLLL",
        "........LLLllLLLLLL",
        ".......DDD...DDDDDD",
    ]
    bw_map = {
        "W": "w",
        "G": "G6",
        "g": "G4",
        "R": "R5",
        "r": "R3",
        "L": "L5",
        "l": "L3",
        "D": "L2",
    }
    c.stamp(boot_wing, bw_map, ox=6, oy=9)
    # Sunlit highlights on cuff, shin & toe
    c.line(17, 12, 21, 12, "R6")
    c.pset(17, 14, "L6")
    c.pset(17, 15, "L6")
    c.pset(22, 18, "L6")
    c.pset(23, 19, "L6")
    c.apply_4conn_outline("K")
    c.center_in_frame(2)
    return c


ICON_BUILDERS = [
    ("icon_sword.png", "Sword", build_icon_sword),
    ("icon_bow.png", "Bow", build_icon_bow),
    ("icon_dagger.png", "Dagger", build_icon_dagger),
    ("icon_staff.png", "Staff", build_icon_staff),
    ("icon_fireball.png", "Fireball", build_icon_fireball),
    ("icon_flame_hands.png", "Flame Hands", build_icon_flame_hands),
    ("icon_magic_darts.png", "Magic Darts", build_icon_magic_darts),
    ("icon_heal.png", "Heal", build_icon_heal),
    ("icon_buff.png", "Buff", build_icon_buff),
    ("icon_daze.png", "Daze", build_icon_daze),
    ("icon_shove.png", "Shove", build_icon_shove),
    ("icon_potion.png", "Potion", build_icon_potion),
    ("icon_guard.png", "Guard", build_icon_guard),
    ("icon_undo.png", "Undo", build_icon_undo),
    ("icon_end_turn.png", "End Turn", build_icon_end_turn),
    ("icon_auto_combat.png", "Auto Combat", build_icon_auto_combat),
    ("icon_speed.png", "Speed", build_icon_speed),
    ("icon_audio.png", "Audio", build_icon_audio),
    ("icon_grid.png", "Grid", build_icon_grid),
    ("icon_settings.png", "Settings", build_icon_settings),
    ("icon_flee.png", "Flee", build_icon_flee),
]


def qa_check_icon(img: Image.Image, palette_rgb: set[Tuple[int, int, int]]) -> Dict[str, object]:
    arr = np.array(img, dtype=np.uint8)
    h, w, ch = arr.shape
    alphas = set(int(a) for a in np.unique(arr[:, :, 3]))
    alpha_is_1bit = alphas.issubset({0, 255})
    opaque_mask = arr[:, :, 3] == 255
    opaque_rgb = arr[opaque_mask][:, :3]
    off_palette = 0
    for rgb in np.unique(opaque_rgb, axis=0):
        t = (int(rgb[0]), int(rgb[1]), int(rgb[2]))
        if t not in palette_rgb:
            off_palette += 1
    ys, xs = np.where(opaque_mask)
    if len(xs) > 0:
        margin_l = int(xs.min())
        margin_r = int(w - 1 - xs.max())
        margin_t = int(ys.min())
        margin_b = int(h - 1 - ys.max())
        min_margin = min(margin_l, margin_r, margin_t, margin_b)
    else:
        min_margin = 0
    return {
        "size": (w, h),
        "mode": img.mode,
        "alpha_is_1bit": alpha_is_1bit,
        "off_palette_colors": off_palette,
        "opaque_pixels": int(opaque_mask.sum()),
        "min_margin": min_margin,
    }


def load_label_font(size: int = 13) -> ImageFont.ImageFont | ImageFont.FreeTypeFont:
    for candidate in (
        r"C:\Windows\Fonts\consola.ttf",
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    ):
        p = Path(candidate)
        if p.exists():
            return ImageFont.truetype(str(p), size=size)
    return ImageFont.load_default()


def build_hotbar_sheet(icons: List[Tuple[str, str, Image.Image]], out_path: Path) -> None:
    cols = 7
    rows = 3
    zoom = 4
    icon_px = 32 * zoom  # 128
    frame_pad = 12
    frame_size = icon_px + frame_pad * 2  # 152
    label_h = 28
    cell_w = frame_size + 20
    cell_h = frame_size + label_h + 18
    margin_x = 24
    margin_y = 56
    sheet_w = margin_x * 2 + cols * cell_w
    sheet_h = margin_y + rows * cell_h + 20

    sheet = Image.new("RGBA", (sheet_w, sheet_h), (18, 21, 33, 255))
    draw = ImageDraw.Draw(sheet)
    title_font = load_label_font(18)
    label_font = load_label_font(13)

    draw.text(
        (margin_x, 18),
        "Realmz Remaster - Combat Hotbar & Utility Icons (21x 32x32 @ 4x)",
        fill=(247, 244, 234, 255),
        font=title_font,
    )

    for idx, (fname, label, icon_img) in enumerate(icons):
        r = idx // cols
        c = idx % cols
        cx = margin_x + c * cell_w + cell_w // 2
        fx0 = cx - frame_size // 2
        fy0 = margin_y + r * cell_h
        fx1 = fx0 + frame_size
        fy1 = fy0 + frame_size

        # Dark slate UI button frame with subtle bevel
        draw.rounded_rectangle([fx0, fy0, fx1, fy1], radius=8, fill=(35, 43, 66, 255), outline=(80, 98, 143, 255), width=2)
        draw.rounded_rectangle(
            [fx0 + 3, fy0 + 3, fx1 - 3, fy1 - 3],
            radius=6,
            fill=(28, 32, 51, 255),
            outline=(17, 19, 31, 255),
            width=1,
        )

        # Nearest-neighbor 4x paste
        zoomed = icon_img.resize((icon_px, icon_px), resample=Image.Resampling.NEAREST)
        sheet.alpha_composite(zoomed, dest=(fx0 + frame_pad, fy0 + frame_pad))

        # Crisp centered label
        text = f"{idx + 1}. {label}"
        bbox = draw.textbbox((0, 0), text, font=label_font)
        tw = bbox[2] - bbox[0]
        draw.text((cx - tw // 2, fy1 + 6), text, fill=(216, 226, 240, 255), font=label_font)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, format="PNG")


def main() -> None:
    palette_rgb = load_palette_rgb_set()
    # Verify every token in PAL is strictly inside palette_soc_sunlit_128.json
    for tok, hx in PAL.items():
        r, g, b, _ = hex_to_rgba(hx)
        if (r, g, b) not in palette_rgb:
            raise ValueError(f"Token {tok}={hx} is not in palette_soc_sunlit_128.json")

    OUT_ICONS_DIR.mkdir(parents=True, exist_ok=True)
    rendered: List[Tuple[str, str, Image.Image]] = []
    all_pass = True

    for fname, label, builder in ICON_BUILDERS:
        canvas = builder()
        img = canvas.to_image()
        qa = qa_check_icon(img, palette_rgb)
        if qa["off_palette_colors"] != 0 or not qa["alpha_is_1bit"] or qa["size"] != (32, 32):
            all_pass = False
        out_file = OUT_ICONS_DIR / fname
        img.save(out_file, format="PNG")
        rendered.append((fname, label, img))
        print(
            f"{fname:22s} | size={qa['size']} | 1bit={qa['alpha_is_1bit']} | "
            f"off_pal={qa['off_palette_colors']} | px={qa['opaque_pixels']:3d} | margin={qa['min_margin']}"
        )

    build_hotbar_sheet(rendered, OUT_SHEET_PATH)
    print(f"SHEET_WRITTEN={OUT_SHEET_PATH}")
    if not all_pass:
        raise SystemExit("QA failed on one or more icons!")


if __name__ == "__main__":
    main()
