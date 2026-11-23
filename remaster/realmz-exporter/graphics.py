"""QuickDraw cicn and PICT decoders for Realmz assets."""

from __future__ import annotations

import struct
from typing import Dict, Iterable, List, Tuple

from PIL import Image


RGB = Tuple[int, int, int]


def unpack_bits(src: bytes, expected_len: int) -> bytes:
    """Decompress Apple PackBits scanline data up to expected_len bytes."""
    out = bytearray()
    i = 0
    n = len(src)
    while i < n and len(out) < expected_len:
        ctrl = struct.unpack(">b", src[i : i + 1])[0]
        i += 1
        if ctrl >= 0:
            count = ctrl + 1
            out.extend(src[i : i + count])
            i += count
        elif ctrl != -128:
            count = 1 - ctrl
            if i < n:
                out.extend(src[i : i + 1] * count)
            i += 1
    if len(out) < expected_len:
        out.extend(b"\x00" * (expected_len - len(out)))
    return bytes(out[:expected_len])


def _read_color_table(blob: bytes, pos: int) -> Tuple[Dict[int, RGB], int]:
    """Parse a QuickDraw ColorTable starting at pos."""
    _ct_seed, ct_flags, ct_size = struct.unpack(">IHH", blob[pos : pos + 8])
    pos += 8
    palette: Dict[int, RGB] = {}
    is_device = bool(ct_flags & 0x8000)
    for idx in range(ct_size + 1):
        val, r16, g16, b16 = struct.unpack(">HHHH", blob[pos : pos + 8])
        pos += 8
        key = idx if is_device else val
        palette[key] = (r16 >> 8, g16 >> 8, b16 >> 8)
    return palette, pos


def _indexed_pixel(row: bytes, x: int, pixel_size: int) -> int:
    """Read a 1, 2, 4, or 8 bpp big-endian pixel index from a scanline."""
    if pixel_size == 8:
        return row[x]
    if pixel_size == 4:
        bval = row[x >> 1]
        return (bval >> 4) if (x & 1) == 0 else (bval & 0x0F)
    if pixel_size == 2:
        bval = row[x >> 2]
        return (bval >> (6 - 2 * (x & 3))) & 0x03
    if pixel_size == 1:
        bval = row[x >> 3]
        return (bval >> (7 - (x & 7))) & 0x01
    raise ValueError(f"Unsupported indexed pixelSize: {pixel_size}")


def decode_cicn(blob: bytes) -> Image.Image:
    """Decode a Classic Mac OS 'cicn' color icon resource into an RGBA Pillow Image."""
    if len(blob) < 82:
        raise ValueError(f"cicn payload too short: {len(blob)} bytes")

    pm_rb_raw = struct.unpack(">H", blob[4:6])[0]
    pm_rb = pm_rb_raw & 0x3FFF
    top, left, bottom, right = struct.unpack(">4h", blob[6:14])
    width = right - left
    height = bottom - top
    if width <= 0 or height <= 0:
        raise ValueError(f"Invalid cicn bounds: {(top, left, bottom, right)}")

    pixel_size = struct.unpack(">H", blob[32:34])[0]
    mask_rb = struct.unpack(">H", blob[54:56])[0] & 0x3FFF
    bmap_rb = struct.unpack(">H", blob[68:70])[0] & 0x3FFF

    pos = 82
    mask_len = mask_rb * height
    mask_data = blob[pos : pos + mask_len]
    pos += mask_len

    bmap_data = b""
    if bmap_rb > 0:
        bmap_len = bmap_rb * height
        bmap_data = blob[pos : pos + bmap_len]
        pos += bmap_len

    if pm_rb_raw & 0x8000:
        palette, pos = _read_color_table(blob, pos)
        pix_data = blob[pos : pos + pm_rb * height]
    else:
        palette = {0: (255, 255, 255), 1: (0, 0, 0)}
        pix_data = bmap_data
        pm_rb = bmap_rb
        pixel_size = 1

    rgba = bytearray(width * height * 4)
    for y in range(height):
        row = pix_data[y * pm_rb : (y + 1) * pm_rb]
        mask_row = mask_data[y * mask_rb : (y + 1) * mask_rb]
        for x in range(width):
            cidx = _indexed_pixel(row, x, pixel_size)
            r, g, b = palette.get(cidx, (0, 0, 0))
            if mask_rb > 0:
                m_byte = mask_row[x >> 3]
                alpha = 255 if ((m_byte >> (7 - (x & 7))) & 1) else 0
            else:
                alpha = 255
            p = (y * width + x) * 4
            rgba[p : p + 4] = bytes((r, g, b, alpha))

    return Image.frombytes("RGBA", (width, height), bytes(rgba))


def _decode_direct_scanline(raw_row: bytes, width: int, pixel_size: int, cmp_count: int) -> List[RGB]:
    """Decode a 16-bit (RGB555) or 32-bit direct PixMap scanline."""
    pixels: List[RGB] = []
    if pixel_size == 16:
        for x in range(width):
            val = struct.unpack(">H", raw_row[x * 2 : x * 2 + 2])[0]
            r = ((val >> 10) & 0x1F) * 255 // 31
            g = ((val >> 5) & 0x1F) * 255 // 31
            b = (val & 0x1F) * 255 // 31
            pixels.append((r, g, b))
        return pixels

    if pixel_size == 32:
        # Planar ARGB/RGB layout in PackBits QuickDraw DirectBits.
        plane_len = len(raw_row) // cmp_count
        if cmp_count == 4:
            r_off, g_off, b_off = plane_len, 2 * plane_len, 3 * plane_len
        elif cmp_count == 3:
            r_off, g_off, b_off = 0, plane_len, 2 * plane_len
        else:
            raise ValueError(f"Unsupported DirectBits cmpCount={cmp_count}")
        for x in range(width):
            pixels.append((raw_row[r_off + x], raw_row[g_off + x], raw_row[b_off + x]))
        return pixels

    raise ValueError(f"Unsupported DirectBits pixelSize={pixel_size}")


def decode_pict(blob: bytes) -> Image.Image:
    """Decode a QuickDraw v1/v2 PICT resource into an RGBA Pillow Image."""
    if len(blob) < 12:
        raise ValueError(f"PICT payload too short: {len(blob)} bytes")

    top, left, bottom, right = struct.unpack(">4h", blob[2:10])
    frame_w = right - left
    frame_h = bottom - top
    if frame_w <= 0 or frame_h <= 0:
        raise ValueError(f"Invalid PICT frame: {(top, left, bottom, right)}")

    canvas = Image.new("RGBA", (frame_w, frame_h), (0, 0, 0, 255))
    pos = 10
    n = len(blob)
    is_v2 = False

    while pos < n:
        if is_v2 and (pos & 1):
            pos += 1
        if pos >= n:
            break

        if not is_v2:
            if pos + 2 <= n and blob[pos : pos + 2] == b"\x00\x11":
                op = struct.unpack(">H", blob[pos : pos + 2])[0]
                pos += 2
            else:
                op = blob[pos]
                pos += 1
        else:
            if pos + 2 > n:
                break
            op = struct.unpack(">H", blob[pos : pos + 2])[0]
            pos += 2

        if op == 0x0000:  # NOP
            continue
        if op == 0x0011:  # VersionOp
            if is_v2 or (pos + 2 <= n and blob[pos : pos + 2] == b"\x02\xFF"):
                pos += 2
                is_v2 = True
            else:
                pos += 1
            continue
        if op == 0x0C00:  # HeaderOp (24 bytes)
            is_v2 = True
            pos += 24
            continue
        if op == 0x001E:  # DefHilite
            continue
        if op == 0x0001:  # ClipRgn
            rgn_size = struct.unpack(">H", blob[pos : pos + 2])[0]
            pos += rgn_size
            continue
        if op == 0x00A0:  # ShortComment
            pos += 2
            continue
        if op == 0x00A1:  # LongComment
            _kind, dlen = struct.unpack(">HH", blob[pos : pos + 4])
            pos += 4 + dlen
            continue
        if op in (0x0098, 0x0099, 0x009A, 0x009B):
            is_direct = op in (0x009A, 0x009B)
            has_rgn = op in (0x0099, 0x009B)
            if is_direct:
                # DirectBits has a 4-byte dummy baseAddr before rowBytes.
                pos += 4

            rb_raw = struct.unpack(">H", blob[pos : pos + 2])[0]
            rb = rb_raw & 0x3FFF
            btop, bleft, bbottom, bright = struct.unpack(">4h", blob[pos + 2 : pos + 10])
            bw = bright - bleft
            bh = bbottom - btop

            if (rb_raw & 0x8000) or is_direct:
                pack_type = struct.unpack(">H", blob[pos + 12 : pos + 14])[0]
                pixel_size = struct.unpack(">H", blob[pos + 28 : pos + 30])[0]
                cmp_count = struct.unpack(">H", blob[pos + 30 : pos + 32])[0]
                pos += 46
                if is_direct:
                    palette = {}
                else:
                    palette, pos = _read_color_table(blob, pos)
            else:
                pack_type = 0
                pixel_size = 1
                cmp_count = 1
                pos += 10
                palette = {0: (255, 255, 255), 1: (0, 0, 0)}

            src_rect = struct.unpack(">4h", blob[pos : pos + 8])
            dst_rect = struct.unpack(">4h", blob[pos + 8 : pos + 16])
            _mode = struct.unpack(">H", blob[pos + 16 : pos + 18])[0]
            pos += 18

            if has_rgn:
                rgn_size = struct.unpack(">H", blob[pos : pos + 2])[0]
                pos += rgn_size

            # Unpack scanlines.
            if is_direct and pack_type == 4 and pixel_size == 32:
                expected_row_bytes = bw * cmp_count
            else:
                expected_row_bytes = rb

            bitmap = Image.new("RGBA", (bw, bh), (0, 0, 0, 255))
            pix = bytearray(bw * bh * 4)
            for y in range(bh):
                if rb < 8 or (is_direct and pack_type in (1, 2)):
                    row_bytes = blob[pos : pos + expected_row_bytes]
                    pos += expected_row_bytes
                else:
                    if rb > 250:
                        bc = struct.unpack(">H", blob[pos : pos + 2])[0]
                        pos += 2
                    else:
                        bc = blob[pos]
                        pos += 1
                    row_bytes = unpack_bits(blob[pos : pos + bc], expected_row_bytes)
                    pos += bc

                if is_direct:
                    rgb_list = _decode_direct_scanline(row_bytes, bw, pixel_size, cmp_count)
                    for x, (r, g, b) in enumerate(rgb_list):
                        p = (y * bw + x) * 4
                        pix[p : p + 4] = bytes((r, g, b, 255))
                else:
                    for x in range(bw):
                        cidx = _indexed_pixel(row_bytes, x, pixel_size)
                        r, g, b = palette.get(cidx, (0, 0, 0))
                        p = (y * bw + x) * 4
                        pix[p : p + 4] = bytes((r, g, b, 255))

            bitmap = Image.frombytes("RGBA", (bw, bh), bytes(pix))

            s_top, s_left, s_bottom, s_right = src_rect
            d_top, d_left, d_bottom, d_right = dst_rect
            crop_box = (s_left - bleft, s_top - btop, s_right - bleft, s_bottom - btop)
            cropped = bitmap.crop(crop_box)
            dw = d_right - d_left
            dh = d_bottom - d_top
            if (dw, dh) != cropped.size and dw > 0 and dh > 0:
                cropped = cropped.resize((dw, dh), Image.NEAREST)
            canvas.paste(cropped, (d_left - left, d_top - top))
            continue
        if op == 0x00FF:  # EndOfPicture
            break
        raise ValueError(f"Unsupported PICT opcode 0x{op:04X} at offset {pos - 2}")

    return canvas


def slice_tilesheet(
    tilesheet: Image.Image,
    tile_id: int,
    tile_width: int = 32,
    tile_height: int = 32,
    columns: int = 20,
) -> Image.Image:
    """Slice a 1-based tile from a Realmz terrain tilesheet (640x320, 20x10)."""
    if tile_id < 1:
        raise ValueError(f"tile_id must be >= 1, got {tile_id}")
    zero_idx = tile_id - 1
    row = zero_idx // columns
    col = zero_idx % columns
    left = col * tile_width
    top = row * tile_height
    return tilesheet.crop((left, top, left + tile_width, top + tile_height))


def compute_tile_palette(
    tilesheet: Image.Image,
    tile_count: int = 200,
    tile_width: int = 32,
    tile_height: int = 32,
    columns: int = 20,
) -> Dict[int, RGB]:
    """Compute representative average RGB color for each 1-based tile in PICT 300."""
    rgb_sheet = tilesheet.convert("RGB")
    colors: Dict[int, RGB] = {}
    for tid in range(1, tile_count + 1):
        tile = slice_tilesheet(rgb_sheet, tid, tile_width, tile_height, columns)
        data = tile.tobytes()
        n = tile_width * tile_height
        r_sum = sum(data[0::3])
        g_sum = sum(data[1::3])
        b_sum = sum(data[2::3])
        colors[tid] = (r_sum // n, g_sum // n, b_sum // n)
    return colors


def build_land_minimap(
    tiles_yx: List[List[int]],
    tile_colors: Dict[int, RGB],
    ap_coords: Iterable[Tuple[int, int]] = (),
) -> Image.Image:
    """Render a 90x90 RGBA color minimap preview from Land 0 tiles and doors."""
    height = len(tiles_yx)
    width = len(tiles_yx[0]) if height > 0 else 0
    door_set = set(ap_coords)
    rgba = bytearray(width * height * 4)

    for y in range(height):
        row = tiles_yx[y]
        for x in range(width):
            raw = row[x]
            if raw < 0:
                # Special icon / monster / encounter tile.
                color = (220, 60, 60)
            else:
                val = raw & ~0x6000
                while val > 999:
                    val -= 1000
                if 1 <= val <= 200:
                    color = tile_colors.get(val, (40, 40, 40))
                else:
                    color = (180, 140, 60)

            if (x, y) in door_set:
                # Tint action-point door tiles bright gold for visual verification.
                color = (245, 205, 55)

            p = (y * width + x) * 4
            rgba[p : p + 4] = bytes((color[0], color[1], color[2], 255))

    return Image.frombytes("RGBA", (width, height), bytes(rgba))
