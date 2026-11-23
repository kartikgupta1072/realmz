"""Classic Mac OS data-fork resource (.rsrc) reader."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import struct
from typing import Dict, List, Tuple


@dataclass(frozen=True)
class ResourceEntry:
    """Single resource entry parsed from a Mac .rsrc file."""

    res_type: str
    res_id: int
    name: str
    data: bytes


class ResourceFork:
    """Parser for Classic Mac OS data-fork resource files (.rsrc)."""

    def __init__(self, path: Path) -> None:
        self.path = Path(path)
        self._resources: Dict[str, Dict[int, ResourceEntry]] = {}
        self._load()

    def _load(self) -> None:
        raw = self.path.read_bytes()
        if len(raw) < 16:
            raise ValueError(f"Resource file too small: {self.path}")

        data_off, map_off, data_len, map_len = struct.unpack(">IIII", raw[:16])
        if map_off + 28 > len(raw) or data_off + data_len > len(raw):
            raise ValueError(f"Corrupt resource header in {self.path}")

        type_list_rel, name_list_rel = struct.unpack(">HH", raw[map_off + 24 : map_off + 28])
        type_list_off = map_off + type_list_rel
        name_list_off = map_off + name_list_rel

        if type_list_off + 2 > len(raw):
            raise ValueError(f"Missing resource type list in {self.path}")

        num_types_minus_1 = struct.unpack(">h", raw[type_list_off : type_list_off + 2])[0]
        if num_types_minus_1 < 0:
            return
        num_types = num_types_minus_1 + 1

        pos = type_list_off + 2
        for _ in range(num_types):
            rtype = raw[pos : pos + 4].decode("mac_roman", errors="replace")
            count = struct.unpack(">H", raw[pos + 4 : pos + 6])[0] + 1
            ref_rel = struct.unpack(">H", raw[pos + 6 : pos + 8])[0]
            pos += 8

            ref_base = type_list_off + ref_rel
            entries: Dict[int, ResourceEntry] = {}
            for idx in range(count):
                rp = ref_base + idx * 12
                rid, name_rel, attrs_and_off = struct.unpack(">hhI", raw[rp : rp + 8])
                entry_off = data_off + (attrs_and_off & 0x00FFFFFF)
                if entry_off + 4 > len(raw):
                    raise ValueError(f"Out-of-bounds resource data for {rtype} {rid} in {self.path}")
                rlen = struct.unpack(">I", raw[entry_off : entry_off + 4])[0]
                rdata = raw[entry_off + 4 : entry_off + 4 + rlen]

                rname = ""
                if name_rel != -1:
                    no = name_list_off + name_rel
                    if no < len(raw):
                        nlen = raw[no]
                        rname = raw[no + 1 : no + 1 + nlen].decode("mac_roman", errors="replace")

                entries[rid] = ResourceEntry(res_type=rtype, res_id=rid, name=rname, data=rdata)
            self._resources[rtype] = entries

    def types(self) -> List[str]:
        """Return sorted list of resource type codes."""
        return sorted(self._resources.keys())

    def ids(self, res_type: str) -> List[int]:
        """Return sorted list of resource IDs for a given type."""
        return sorted(self._resources.get(res_type, {}).keys())

    def get(self, res_type: str, res_id: int) -> ResourceEntry:
        """Return a ResourceEntry or raise KeyError if missing."""
        by_id = self._resources.get(res_type)
        if not by_id or res_id not in by_id:
            raise KeyError(f"Resource {res_type!r} ID {res_id} not found in {self.path}")
        return by_id[res_id]

    def has(self, res_type: str, res_id: int) -> bool:
        """Return True if the resource exists."""
        return res_id in self._resources.get(res_type, {})

    def get_str_list(self, res_id: int) -> List[str]:
        """Decode a Mac STR# Pascal string list resource."""
        blob = self.get("STR#", res_id).data
        if len(blob) < 2:
            return []
        count = struct.unpack(">H", blob[:2])[0]
        pos = 2
        items: List[str] = []
        for _ in range(count):
            if pos >= len(blob):
                break
            slen = blob[pos]
            pos += 1
            items.append(blob[pos : pos + slen].decode("mac_roman", errors="replace"))
            pos += slen
        return items

    def get_text(self, res_id: int) -> str:
        """Decode a Mac TEXT resource."""
        blob = self.get("TEXT", res_id).data
        return blob.decode("mac_roman", errors="replace")
