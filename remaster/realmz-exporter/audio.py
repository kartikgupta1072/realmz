"""Classic Realmz audio extractor: Mac 'snd ' SFX to PCM WAV and MOD/MADG music to OGG."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import tempfile
from typing import Any, Dict, List, Optional, Tuple
import wave

SCRIPT_DIR = str(Path(__file__).resolve().parent)
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from formats import sanitize_text
from rsrc import ResourceFork

CREATE_NO_WINDOW = 0x08000000 if sys.platform == "win32" else 0

# Amiga ProTracker 5-octave period table (C-0 = index 0 .. B-4 = index 59).
# Standard ProTracker 3-octave range is C-1 (index 12, period 856) .. B-3 (index 47, period 113).
PROTRACKER_PERIODS_5OCT: Tuple[int, ...] = (
    # Octave 0 (C-0..B-0)
    1712, 1616, 1525, 1440, 1357, 1281, 1209, 1141, 1077, 1017, 961, 907,
    # Octave 1 (C-1..B-1)
    856, 808, 762, 720, 678, 640, 604, 570, 538, 508, 480, 453,
    # Octave 2 (C-2..B-2)
    428, 404, 381, 360, 339, 320, 302, 285, 269, 254, 240, 226,
    # Octave 3 (C-3..B-3)
    214, 202, 190, 180, 170, 160, 151, 143, 135, 127, 120, 113,
    # Octave 4 (C-4..B-4)
    107, 101, 95, 90, 85, 80, 76, 71, 67, 64, 60, 57,
)

MOD_MAGIC_TAGS = {
    b"M.K.",
    b"M!K!",
    b"FLT4",
    b"FLT8",
    b"4CHN",
    b"6CHN",
    b"8CHN",
    b"CD81",
    b"OKTA",
}

MUSIC_KEY_MAP = {
    "Battle Music": "battle",
    "Camp Music": "camp",
    "Cave Music": "cave",
    "Create Music": "create",
    "Dungeon Music": "dungeon",
    "Indoor Music": "indoor",
    "Items Music": "items",
    "Outdoor Music": "outdoor",
    "Shop Music": "shop",
    "Temple Music": "temple",
    "Treasure Music": "treasure",
}


class AudioDecodeError(ValueError):
    """Raised when a sound or music resource cannot be decoded cleanly."""


class UnsupportedCompressionError(AudioDecodeError):
    """Raised when a Mac snd resource uses an unsupported codec such as MACE."""


def _slugify(text: str, fallback: str) -> str:
    cleaned = re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")
    return cleaned or fallback


def _sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _write_json(path: Path, payload: Any) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = sanitize_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n")
    path.write_text(text, encoding="utf-8")
    return path


def _validate_sound_header_at(data: bytes, offset: int) -> bool:
    """Return True if offset points to a plausible Mac SoundHeader/ExtSoundHeader/CmpSoundHeader."""
    if offset < 0 or offset + 22 > len(data):
        return False
    sample_ptr, length_field, sr_fixed = struct.unpack(">III", data[offset : offset + 12])
    encode = data[offset + 20]
    if sample_ptr != 0:
        return False
    if encode not in (0x00, 0xFF, 0xFE):
        return False
    sr = sr_fixed / 65536.0
    if sr < 500.0 or sr > 96000.0:
        return False
    if encode == 0x00:
        return length_field > 0 and (offset + 22 + length_field) <= (len(data) + 16)
    if offset + 64 > len(data):
        return False
    channels = length_field
    frames = struct.unpack(">I", data[offset + 22 : offset + 26])[0]
    return 1 <= channels <= 2 and frames > 0


def _compute_pcm_stats(pcm_bytes: bytes, sample_width: int) -> Tuple[float, float]:
    """Compute (rms, peak) normalized to [0.0, 1.0] for 8-bit unsigned or 16-bit signed LE PCM."""
    if not pcm_bytes:
        return (0.0, 0.0)
    if sample_width == 1:
        n = len(pcm_bytes)
        sq_sum = 0.0
        peak = 0.0
        for b in pcm_bytes:
            v = (b - 128) / 128.0
            av = abs(v)
            if av > peak:
                peak = av
            sq_sum += v * v
        return (round(math.sqrt(sq_sum / n), 6), round(peak, 6))
    n = len(pcm_bytes) // 2
    if n <= 0:
        return (0.0, 0.0)
    samples = struct.unpack(f"<{n}h", pcm_bytes[: n * 2])
    sq_sum = 0.0
    peak = 0.0
    for s in samples:
        v = s / 32768.0
        av = abs(v)
        if av > peak:
            peak = av
        sq_sum += v * v
    return (round(math.sqrt(sq_sum / n), 6), round(peak, 6))


def decode_mac_snd(raw: bytes) -> Dict[str, Any]:
    """Decode a Classic Mac OS Format 1 or Format 2 'snd ' resource to WAV-ready PCM."""
    if len(raw) < 14:
        raise AudioDecodeError(f"snd resource too short ({len(raw)} bytes)")

    fmt = struct.unpack(">H", raw[:2])[0]
    pos = 2
    if fmt == 1:
        num_mod = struct.unpack(">H", raw[pos : pos + 2])[0]
        pos += 2 + num_mod * 6
    elif fmt == 2:
        pos += 2  # refCount
    else:
        raise AudioDecodeError(f"Unsupported snd format version: {fmt}")

    if pos + 2 > len(raw):
        raise AudioDecodeError("Truncated snd command count")
    num_cmds = struct.unpack(">H", raw[pos : pos + 2])[0]
    pos += 2

    cmd_data_offset: Optional[int] = None
    for _ in range(num_cmds):
        if pos + 8 > len(raw):
            raise AudioDecodeError("Truncated snd command table")
        cmd, _p1, p2 = struct.unpack(">HHI", raw[pos : pos + 8])
        pos += 8
        cmd_id = cmd & 0x7FFF
        has_data_offset = bool(cmd & 0x8000)
        if cmd_id in (80, 81) and has_data_offset:
            cmd_data_offset = p2

    after_cmds = pos
    header_offset: Optional[int] = None
    if cmd_data_offset is not None and _validate_sound_header_at(raw, cmd_data_offset):
        header_offset = cmd_data_offset
    elif _validate_sound_header_at(raw, after_cmds):
        # Classic Mac Format-2 resources converted from Format-1 often retain p2=20
        # even though the SoundHeader immediately follows the 14-byte Format-2 header.
        header_offset = after_cmds
    elif cmd_data_offset is not None and cmd_data_offset + 22 <= len(raw):
        header_offset = cmd_data_offset
    else:
        raise AudioDecodeError(
            f"Could not locate valid SoundHeader (fmt={fmt}, cmd_offset={cmd_data_offset}, after_cmds={after_cmds})"
        )

    hdr = raw[header_offset:]
    _sample_ptr, length_field, sr_fixed, _loop_start, _loop_end = struct.unpack(">IIIII", hdr[:20])
    encode = hdr[20]
    sample_rate = max(1000, int(round(sr_fixed / 65536.0)))

    if encode == 0x00:
        # Standard SoundHeader (stdSH): 22-byte header + unsigned 8-bit mono PCM.
        pcm_raw = hdr[22 : 22 + length_field]
        if not pcm_raw:
            raise AudioDecodeError("Empty stdSH PCM payload")
        channels = 1
        sample_width = 1
        num_frames = len(pcm_raw)
        pcm_out = pcm_raw
        enc_name = "stdSH_pcm_u8"
    elif encode == 0xFF:
        # Extended SoundHeader (extSH): 64-byte header + 8-bit unsigned or 16-bit signed BE PCM.
        if len(hdr) < 64:
            raise AudioDecodeError("Truncated extSH header")
        channels = max(1, min(2, length_field))
        num_frames = struct.unpack(">I", hdr[22:26])[0]
        bits = struct.unpack(">H", hdr[48:50])[0]
        if bits not in (8, 16):
            raise AudioDecodeError(f"Unsupported extSH bit depth: {bits}")
        sample_width = bits // 8
        byte_len = num_frames * channels * sample_width
        pcm_raw = hdr[64 : 64 + byte_len]
        if not pcm_raw:
            raise AudioDecodeError("Empty extSH PCM payload")
        num_frames = len(pcm_raw) // (channels * sample_width)
        pcm_raw = pcm_raw[: num_frames * channels * sample_width]
        if sample_width == 2:
            # Classic Mac 16-bit PCM is signed big-endian; WAV requires signed little-endian.
            count = len(pcm_raw) // 2
            pcm_out = struct.pack(f"<{count}h", *struct.unpack(f">{count}h", pcm_raw))
            enc_name = "extSH_pcm_s16le"
        else:
            pcm_out = pcm_raw
            enc_name = "extSH_pcm_u8"
    elif encode == 0xFE:
        # Compressed SoundHeader (cmpSH): 64-byte header.
        if len(hdr) < 64:
            raise AudioDecodeError("Truncated cmpSH header")
        channels = max(1, min(2, length_field))
        num_frames = struct.unpack(">I", hdr[22:26])[0]
        fmt_tag = hdr[36:40]
        comp_id = struct.unpack(">h", hdr[56:58])[0]
        bits = struct.unpack(">H", hdr[62:64])[0]
        if comp_id == 0 or fmt_tag in (b"NONE", b"raw ", b"twos", b"sowt"):
            sample_width = 2 if bits == 16 else 1
            byte_len = num_frames * channels * sample_width
            pcm_raw = hdr[64 : 64 + byte_len]
            if not pcm_raw:
                raise AudioDecodeError("Empty uncompressed cmpSH payload")
            num_frames = len(pcm_raw) // (channels * sample_width)
            pcm_raw = pcm_raw[: num_frames * channels * sample_width]
            if sample_width == 2 and fmt_tag != b"sowt":
                count = len(pcm_raw) // 2
                pcm_out = struct.pack(f"<{count}h", *struct.unpack(f">{count}h", pcm_raw))
                enc_name = "cmpSH_uncompressed_s16le"
            else:
                pcm_out = pcm_raw
                enc_name = "cmpSH_uncompressed_u8" if sample_width == 1 else "cmpSH_sowt_s16le"
        else:
            tag_str = fmt_tag.decode("latin1", errors="replace")
            raise UnsupportedCompressionError(
                f"Compressed Mac snd codec not decoded (comp_id={comp_id}, format={tag_str!r}, bits={bits})"
            )
    else:
        raise AudioDecodeError(f"Unknown Mac snd encoding byte: 0x{encode:02x}")

    if num_frames <= 0:
        raise AudioDecodeError("Decoded zero PCM frames")

    rms, peak = _compute_pcm_stats(pcm_out, sample_width)
    duration_sec = round(num_frames / float(sample_rate), 4)
    return {
        "format_version": fmt,
        "encoding": enc_name,
        "sample_rate": sample_rate,
        "channels": channels,
        "sample_width": sample_width,
        "bits_per_sample": sample_width * 8,
        "num_frames": num_frames,
        "duration_sec": duration_sec,
        "rms": rms,
        "peak": peak,
        "pcm_bytes": pcm_out,
    }


def write_wav(path: Path, decoded: Dict[str, Any]) -> Path:
    """Write a decoded PCM dict to a standard RIFF WAVE file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(decoded["channels"])
        wf.setsampwidth(decoded["sample_width"])
        wf.setframerate(decoded["sample_rate"])
        wf.writeframes(decoded["pcm_bytes"])
    return path


def convert_madg_to_mod(raw: bytes) -> Tuple[bytes, str]:
    """Convert a PlayerPRO MADG 4-channel module into a standard ProTracker M.K. MOD."""
    if len(raw) < 0x0DA8 or raw[:4] != b"MADG":
        raise AudioDecodeError("Input is not a valid PlayerPRO MADG module")

    song_title = sanitize_text(raw[4:36].split(b"\x00", 1)[0].decode("mac_roman", errors="replace")).strip()
    num_pat = raw[0x0D24]
    num_orders = raw[0x0D25]
    orders = list(raw[0x0D26 : 0x0D26 + 128])
    num_chn = raw[0x0DA6]

    if num_chn != 4:
        raise AudioDecodeError(f"MADG converter supports 4-channel modules, got {num_chn}")
    if num_pat == 0 or num_orders == 0 or num_orders > 128:
        raise AudioDecodeError(f"Invalid MADG pattern/order counts: pat={num_pat}, orders={num_orders}")

    # Parse 64 x 52-byte InstrData slots starting at 0x0024.
    instruments: List[Dict[str, Any]] = []
    for idx in range(64):
        off = 0x0024 + idx * 52
        name_raw = raw[off : off + 32].split(b"\x00", 1)[0]
        smp_len = struct.unpack(">I", raw[off + 32 : off + 36])[0]
        vol = struct.unpack(">H", raw[off + 36 : off + 38])[0]
        loop_start = struct.unpack(">H", raw[off + 38 : off + 40])[0]
        loop_size = struct.unpack(">H", raw[off + 40 : off + 42])[0]
        instruments.append(
            {
                "orig_index": idx + 1,  # 1-based instrument index in MADG patterns
                "name": name_raw[:22],
                "length": smp_len,
                "volume": max(0, min(64, vol)),
                "loop_start": loop_start,
                "loop_size": loop_size,
            }
        )

    pat_base = 0x0DA8
    pat_stride = 36 + 64 * num_chn * 4  # 1060 bytes for 64 rows x 4 channels
    smp_base = pat_base + num_pat * pat_stride
    if smp_base > len(raw):
        raise AudioDecodeError(f"Truncated MADG pattern block (need {smp_base}, have {len(raw)})")

    # Map populated instruments (up to 31) into MOD slots 1..31 while preserving 1..31 indexing when possible.
    active_orig = [ins["orig_index"] for ins in instruments if ins["length"] > 0]
    if len(active_orig) > 31:
        raise AudioDecodeError(f"MADG has {len(active_orig)} active instruments (>31 MOD limit)")

    if all(i <= 31 for i in active_orig):
        orig_to_mod = {i: i for i in active_orig}
        mod_slots: List[Optional[Dict[str, Any]]] = [instruments[i] for i in range(31)]
    else:
        orig_to_mod = {orig: slot + 1 for slot, orig in enumerate(active_orig)}
        mod_slots = [instruments[orig - 1] for orig in active_orig]
        while len(mod_slots) < 31:
            mod_slots.append(None)

    # Slice sample blobs sequentially from smp_base in original instrument order.
    smp_pos = smp_base
    sample_blobs: Dict[int, bytes] = {}
    for ins in instruments:
        slen = ins["length"]
        if slen <= 0:
            continue
        blob = raw[smp_pos : smp_pos + slen]
        if len(blob) < slen:
            blob = blob + b"\x00" * (slen - len(blob))
        if len(blob) % 2 == 1:
            blob = blob + b"\x00"
        sample_blobs[ins["orig_index"]] = blob
        smp_pos += slen

    # Assemble ProTracker 4-channel M.K. MOD header (1084 bytes).
    out = bytearray()
    title_bytes = song_title.encode("ascii", errors="replace")[:20].ljust(20, b"\x00")
    out.extend(title_bytes)

    for slot_idx in range(31):
        ins = mod_slots[slot_idx]
        if ins is None or ins["length"] == 0:
            out.extend(b"\x00" * 22)
            out.extend(struct.pack(">HBBHH", 0, 0, 0, 0, 1))
            continue
        blob = sample_blobs[ins["orig_index"]]
        len_words = min(0xFFFF, len(blob) // 2)
        lp_start_words = min(len_words, ins["loop_start"] // 2)
        lp_len_words = max(1, min(len_words - lp_start_words, ins["loop_size"] // 2 if ins["loop_size"] > 2 else 1))
        name22 = ins["name"][:22].ljust(22, b"\x00")
        out.extend(name22)
        out.extend(struct.pack(">HBBHH", len_words, 0, ins["volume"], lp_start_words, lp_len_words))

    out.append(num_orders)
    out.append(127)  # classic ProTracker restart byte
    out.extend(bytes(orders[:128]))
    out.extend(b"M.K.")

    # Convert each 64-row x 4-channel pattern into ProTracker 1024-byte pattern.
    for p in range(num_pat):
        poff = pat_base + p * pat_stride
        rows = struct.unpack(">I", raw[poff : poff + 4])[0]
        comp = raw[poff + 4 : poff + 8]
        if rows != 64 or comp != b"NONE":
            raise AudioDecodeError(f"Unsupported MADG pattern {p}: rows={rows}, comp={comp!r}")
        body = raw[poff + 36 : poff + pat_stride]
        for cell_i in range(0, 1024, 4):
            ins_orig, note, cmd, arg = body[cell_i : cell_i + 4]
            mod_ins = orig_to_mod.get(ins_orig, 0) if ins_orig else 0
            if note in (0, 0xFF):
                period = 0
            else:
                clamped_note = max(0, min(len(PROTRACKER_PERIODS_5OCT) - 1, note))
                period = PROTRACKER_PERIODS_5OCT[clamped_note]
            eff = cmd & 0x0F
            b0 = (mod_ins & 0xF0) | ((period >> 8) & 0x0F)
            b1 = period & 0xFF
            b2 = ((mod_ins & 0x0F) << 4) | eff
            b3 = arg & 0xFF
            out.extend((b0, b1, b2, b3))

    # Append sample data in MOD slot order (1..31).
    for slot_idx in range(31):
        ins = mod_slots[slot_idx]
        if ins is not None and ins["length"] > 0:
            out.extend(sample_blobs[ins["orig_index"]])

    return (bytes(out), song_title)


def _probe_audio_file(audio_path: Path, ffprobe_exe: str, ffmpeg_exe: str) -> Dict[str, Any]:
    """Run ffprobe + ffmpeg volumedetect to verify duration, stream params, and non-silence."""
    probe = subprocess.run(
        [
            ffprobe_exe,
            "-v",
            "error",
            "-show_entries",
            "format=duration,bit_rate:stream=sample_rate,channels,codec_name",
            "-of",
            "json",
            str(audio_path),
        ],
        capture_output=True,
        text=True,
        check=True,
        creationflags=CREATE_NO_WINDOW,
    )
    meta = json.loads(probe.stdout)
    fmt_info = meta.get("format", {})
    streams = meta.get("streams", [])
    s0 = streams[0] if streams else {}
    duration = float(fmt_info.get("duration", 0.0) or 0.0)
    sample_rate = int(s0.get("sample_rate", 44100) or 44100)
    channels = int(s0.get("channels", 2) or 2)
    codec = str(s0.get("codec_name", "unknown"))

    vol = subprocess.run(
        [
            ffmpeg_exe,
            "-hide_banner",
            "-nostats",
            "-i",
            str(audio_path),
            "-af",
            "volumedetect",
            "-f",
            "null",
            "-",
        ],
        capture_output=True,
        text=True,
        check=True,
        creationflags=CREATE_NO_WINDOW,
    )
    mean_match = re.search(r"mean_volume:\s*(-?\d+(?:\.\d+)?)\s*dB", vol.stderr)
    max_match = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?)\s*dB", vol.stderr)
    mean_db = float(mean_match.group(1)) if mean_match else -91.0
    max_db = float(max_match.group(1)) if max_match else -91.0

    return {
        "duration_sec": round(duration, 3),
        "sample_rate": sample_rate,
        "channels": channels,
        "codec": codec,
        "mean_volume_db": round(mean_db, 2),
        "max_volume_db": round(max_db, 2),
    }


def extract_sfx_from_rsrc(
    rsrc_path: Path,
    out_dir: Path,
    manifest_root: Path,
    source_label: str,
) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    """Extract all decodable Mac 'snd ' resources from one .rsrc file into PCM WAV files."""
    rf = ResourceFork(rsrc_path)
    out_dir.mkdir(parents=True, exist_ok=True)
    extracted: List[Dict[str, Any]] = []
    skipped: List[Dict[str, Any]] = []

    for rid in rf.ids("snd "):
        entry = rf.get("snd ", rid)
        clean_name = sanitize_text(entry.name).strip()
        try:
            decoded = decode_mac_snd(entry.data)
        except AudioDecodeError as exc:
            skipped.append(
                {
                    "sound_id": rid,
                    "name": clean_name,
                    "source_rsrc": source_label,
                    "reason": sanitize_text(str(exc)),
                }
            )
            continue

        slug = _slugify(clean_name, f"sound_{rid}")
        fname = f"snd_{rid}_{slug}.wav"
        wav_path = write_wav(out_dir / fname, decoded)
        rel_path = wav_path.relative_to(manifest_root).as_posix()
        extracted.append(
            {
                "sound_id": rid,
                "name": clean_name or f"Sound {rid}",
                "relative_path": rel_path,
                "sample_rate": decoded["sample_rate"],
                "channels": decoded["channels"],
                "bits_per_sample": decoded["bits_per_sample"],
                "num_frames": decoded["num_frames"],
                "duration_sec": decoded["duration_sec"],
                "rms": decoded["rms"],
                "peak": decoded["peak"],
                "format_version": decoded["format_version"],
                "encoding": decoded["encoding"],
                "source_rsrc": source_label,
                "bytes": wav_path.stat().st_size,
                "sha256": _sha256_file(wav_path),
            }
        )

    return (extracted, skipped)


def extract_music_suite(
    music_dir: Path,
    out_dir: Path,
    manifest_root: Path,
    ffmpeg_exe: Optional[str] = None,
    ffprobe_exe: Optional[str] = None,
) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    """Decode all Realmz Music MOD and MADG files into OGG Vorbis tracks."""
    ffmpeg_bin = ffmpeg_exe or shutil.which("ffmpeg")
    ffprobe_bin = ffprobe_exe or shutil.which("ffprobe")
    if not ffmpeg_bin or not ffprobe_bin:
        raise FileNotFoundError("ffmpeg and ffprobe must be installed on PATH to decode Realmz Music tracks")

    out_dir.mkdir(parents=True, exist_ok=True)
    tracks: List[Dict[str, Any]] = []
    skipped: List[Dict[str, Any]] = []

    with tempfile.TemporaryDirectory() as tmp_dir:
        tmp_path = Path(tmp_dir)
        for src in sorted(p for p in Path(music_dir).iterdir() if p.is_file()):
            raw = src.read_bytes()
            is_madg = raw[:4] == b"MADG"
            tag1080 = raw[1080:1084] if len(raw) > 1084 else b""
            is_mod = tag1080 in MOD_MAGIC_TAGS

            if not is_madg and not is_mod:
                skipped.append(
                    {
                        "source_file": src.name,
                        "reason": "Not a MOD or MADG music file",
                    }
                )
                continue

            key = MUSIC_KEY_MAP.get(src.name, _slugify(src.name, src.stem.lower()))
            ogg_path = out_dir / f"{key}.ogg"

            if is_madg:
                mod_bytes, song_title = convert_madg_to_mod(raw)
                decode_input = tmp_path / f"{key}_converted.mod"
                decode_input.write_bytes(mod_bytes)
                source_format = "MADG->ProTracker_MOD"
            else:
                song_title = sanitize_text(raw[:20].split(b"\x00", 1)[0].decode("mac_roman", errors="replace")).strip()
                decode_input = src
                source_format = f"ProTracker_MOD({tag1080.decode('latin1', errors='replace')})"

            proc = subprocess.run(
                [
                    ffmpeg_bin,
                    "-y",
                    "-v",
                    "error",
                    "-i",
                    str(decode_input),
                    "-vn",
                    "-c:a",
                    "libvorbis",
                    "-q:a",
                    "5",
                    "-ar",
                    "44100",
                    "-ac",
                    "2",
                    str(ogg_path),
                ],
                capture_output=True,
                text=True,
                creationflags=CREATE_NO_WINDOW,
            )
            if proc.returncode != 0 or not ogg_path.is_file() or ogg_path.stat().st_size == 0:
                raise AudioDecodeError(f"ffmpeg failed decoding {src.name}: {proc.stderr.strip()}")

            stats = _probe_audio_file(ogg_path, ffprobe_bin, ffmpeg_bin)
            if stats["duration_sec"] <= 0.5 or stats["max_volume_db"] <= -70.0:
                raise AudioDecodeError(
                    f"Decoded track {src.name} appears silent or invalid: {stats}"
                )

            tracks.append(
                {
                    "key": key,
                    "title": song_title or src.name,
                    "source_file": src.name,
                    "source_format": source_format,
                    "relative_path": ogg_path.relative_to(manifest_root).as_posix(),
                    "output_format": "ogg_vorbis",
                    "sample_rate": stats["sample_rate"],
                    "channels": stats["channels"],
                    "duration_sec": stats["duration_sec"],
                    "mean_volume_db": stats["mean_volume_db"],
                    "max_volume_db": stats["max_volume_db"],
                    "bytes": ogg_path.stat().st_size,
                    "sha256": _sha256_file(ogg_path),
                }
            )

    return (tracks, skipped)


def export_audio(
    realmz_base: Path,
    out_dir: Path,
    include_scenario_sfx: Optional[List[str]] = None,
) -> Dict[str, Any]:
    """Export base SFX (.wav), Realmz Music suite (.ogg), and audio_manifest.json."""
    base = Path(realmz_base)
    audio_root = Path(out_dir)
    sfx_dir = audio_root / "sfx"
    music_dir = audio_root / "music"
    audio_root.mkdir(parents=True, exist_ok=True)

    fj_path = base / "Data Files" / "The Family Jewels.rsrc"
    base_sfx, skipped_sfx = extract_sfx_from_rsrc(
        fj_path,
        sfx_dir,
        manifest_root=audio_root,
        source_label="Data Files/The Family Jewels.rsrc",
    )

    scenario_sfx_entries: List[Dict[str, Any]] = []
    for scen_name in include_scenario_sfx or []:
        scen_rsrc = base / "Scenarios" / scen_name / "Scenario.rsrc"
        if not scen_rsrc.is_file():
            continue
        scen_slug = _slugify(scen_name, "scenario")
        ext, skp = extract_sfx_from_rsrc(
            scen_rsrc,
            sfx_dir / scen_slug,
            manifest_root=audio_root,
            source_label=f"Scenarios/{scen_name}/Scenario.rsrc",
        )
        for item in ext:
            item["scenario"] = sanitize_text(scen_name)
        scenario_sfx_entries.extend(ext)
        skipped_sfx.extend(skp)

    music_tracks, skipped_music = extract_music_suite(
        base / "Realmz Music",
        music_dir,
        manifest_root=audio_root,
    )

    manifest = {
        "exporter": "realmz-exporter.audio",
        "version": "1.0.0",
        "realmz_base": str(base),
        "audio_root": str(audio_root),
        "sfx_count": len(base_sfx),
        "scenario_sfx_count": len(scenario_sfx_entries),
        "skipped_sfx_count": len(skipped_sfx),
        "music_count": len(music_tracks),
        "sfx": base_sfx,
        "sfx_by_id": {str(e["sound_id"]): e for e in base_sfx},
        "scenario_sfx": scenario_sfx_entries,
        "skipped_sfx": skipped_sfx,
        "music": music_tracks,
        "music_by_key": {t["key"]: t for t in music_tracks},
        "skipped_music": skipped_music,
    }
    _write_json(audio_root / "audio_manifest.json", manifest)
    return manifest


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Extract Classic Realmz 'snd ' SFX to PCM WAV and Realmz Music MOD/MADG suites to OGG."
    )
    parser.add_argument(
        "--realmz-base",
        type=Path,
        default=Path(r"C:\realmz\base\Realmz"),
        help="Path to the base Realmz directory.",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path(r"C:\realmz\remaster\extracted\audio"),
        help="Destination directory for extracted audio/sfx, audio/music, and audio_manifest.json.",
    )
    parser.add_argument(
        "--scenario-sfx",
        nargs="*",
        default=["City of Bywater", "Prelude to Pestilence", "Assault on Giant Mountain"],
        help="Optional scenario names whose Scenario.rsrc snd resources should also be extracted.",
    )
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)
    try:
        manifest = export_audio(args.realmz_base, args.out, include_scenario_sfx=args.scenario_sfx)
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(
        f"Exported {manifest['sfx_count']} base SFX + {manifest['scenario_sfx_count']} scenario SFX "
        f"+ {manifest['music_count']} music tracks -> {manifest['audio_root']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
