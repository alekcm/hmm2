"""RAD Game Tools **Granny GR2** reader — ``data/bin/animations``, ``data/bin/Skeletons``.

Unlike everything else under ``bin/``, these are not Nival's record container.
They are Granny files, written by "Granny Standard Exporter, SDK 2.5.0.5".

The property that makes them readable is that **a GR2 describes itself**: the
file carries a tree of type definitions naming every field of every structure,
so a reader walks it by field name -- ``Bones``, ``ParentIndex``,
``InverseWorldTransform`` -- instead of by guessed offsets.

Three things cost the most to get right, all verified below against the data:

* ``sectionArrayOffset`` is relative to **byte 32**, not to the file. Read as
  file-absolute, every section field comes out shifted by eight bytes and still
  parses plausibly. The tell is ``headerSize != 88 + 44 * sectionCount``.
* A pointer field holds nothing usable. Each section carries a relocation table
  mapping the field's offset to the ``(section, offset)`` it targets; a field
  with no entry is a null pointer.
* Type definitions are recursive -- a bone's ``ExtendedData`` can point at a
  type already being read -- so the member list must be published before it is
  filled, or the walk never terminates.

Only **stored** sections are read here. Most ``bin/Skeletons`` files and 577 of
3393 animations pack their sections with Oodle1, an adaptive arithmetic codec
that is out of scope. That costs nothing in practice: an animation file carries
its own copy of the skeleton it animates, so 2816 fully stored animations yield
bones, rest pose and curves together.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

MAGIC = bytes.fromhex("b867b0caf86db10f84728c7e5e19001e")

COMP_STORED = 0
COMP_OODLE0 = 1
COMP_OODLE1 = 2

# Member kinds, in the SDK's enum order -- the stored index IS the tag.
END = 0
INLINE = 1
REFERENCE = 2
REFERENCE_TO_ARRAY = 3
ARRAY_OF_REFERENCES = 4
VARIANT_REFERENCE = 5
REFERENCE_TO_VARIANT_ARRAY = 7
STRING = 8
TRANSFORM = 9
REAL32 = 10
INT8 = 11
UINT8 = 12
BINORMAL_INT8 = 13
NORMAL_UINT8 = 14
INT16 = 15
UINT16 = 16
BINORMAL_INT16 = 17
NORMAL_UINT16 = 18
INT32 = 19
UINT32 = 20
REAL16 = 21
EMPTY_REFERENCE = 22

# Bytes each kind occupies in the 32-bit layout, before the array width.
_SCALAR_SIZE = {
    REAL32: 4, INT8: 1, UINT8: 1, BINORMAL_INT8: 1, NORMAL_UINT8: 1,
    INT16: 2, UINT16: 2, BINORMAL_INT16: 2, NORMAL_UINT16: 2,
    INT32: 4, UINT32: 4, REAL16: 2, TRANSFORM: 68,
}


class GR2Error(ValueError):
    pass


@dataclass
class Section:
    compression: int
    offset: int
    size: int
    uncompressed_size: int
    alignment: int
    stop0: int
    stop1: int
    reloc_offset: int
    reloc_count: int
    mixed_offset: int
    mixed_count: int


@dataclass
class Member:
    kind: int
    name: str
    # Where the element's own type definition lives.
    type_sec: int = -1
    type_off: int = -1
    array_width: int = 1


class GR2:
    """One parsed Granny file."""

    def __init__(self, data: bytes):
        if len(data) < 88 or data[:16] != MAGIC:
            raise GR2Error("not a Granny GR2 file")

        self.data = data
        # headerSize sits at 16, but version and fileSize are at 32 and 36 --
        # bytes 20..31 are padding. Reading them as a run of three from 16 picks
        # up zeros and every file then fails the fileSize check.
        self.header_size, = struct.unpack_from("<I", data, 16)
        self.version, self.file_size, self.crc, self.section_array_offset, \
            self.section_count = struct.unpack_from("<5I", data, 32)
        self.root_type = struct.unpack_from("<2I", data, 52)
        self.root_object = struct.unpack_from("<2I", data, 60)
        self.type_tag = struct.unpack_from("<I", data, 68)[0]

        # The cheap self-check. Getting sectionArrayOffset wrong breaks it.
        if self.header_size != 88 + 44 * self.section_count:
            raise GR2Error(
                "headerSize %d != 88 + 44*%d; sectionArrayOffset misread"
                % (self.header_size, self.section_count))
        if self.file_size != len(data):
            raise GR2Error("declared fileSize %d != %d bytes" % (self.file_size, len(data)))

        self.sections: List[Section] = []
        base = 32 + self.section_array_offset
        for i in range(self.section_count):
            v = struct.unpack_from("<11I", data, base + i * 44)
            self.sections.append(Section(*v))

        self._relocs: Dict[int, Dict[int, Tuple[int, int]]] = {}
        self._type_cache: Dict[Tuple[int, int], List[Member]] = {}
        self._strings: Dict[Tuple[int, int], str] = {}

    # -- sections ----------------------------------------------------------

    def is_stored(self) -> bool:
        return all(s.compression == COMP_STORED for s in self.sections)

    def compressed_sections(self) -> List[int]:
        return [i for i, s in enumerate(self.sections)
                if s.compression != COMP_STORED]

    def section_bytes(self, i: int) -> bytes:
        s = self.sections[i]
        if s.compression != COMP_STORED:
            raise GR2Error("section %d uses compression %d (Oodle1 unsupported)"
                           % (i, s.compression))
        return self.data[s.offset:s.offset + s.size]

    def _read(self, sec: int, off: int, fmt: str) -> Any:
        buf = self.section_bytes(sec)
        if off < 0 or off > len(buf):
            raise GR2Error("read at offset %d outside section %d (%d bytes)"
                           % (off, sec, len(buf)))
        return struct.unpack_from(fmt, buf, off)

    # -- relocations -------------------------------------------------------

    def relocs(self, sec: int) -> Dict[int, Tuple[int, int]]:
        """Field offset within the section -> (target section, target offset)."""
        if sec in self._relocs:
            return self._relocs[sec]
        table: Dict[int, Tuple[int, int]] = {}
        s = self.sections[sec]
        # The table lives outside the compressed payload, so it is readable even
        # when a section is not.
        for i in range(s.reloc_count):
            field_off, tgt_sec, tgt_off = struct.unpack_from(
                "<3I", self.data, s.reloc_offset + i * 12)
            table[field_off] = (tgt_sec, tgt_off)
        self._relocs[sec] = table
        return table

    def _follow(self, sec: int, field_off: int) -> Optional[Tuple[int, int]]:
        return self.relocs(sec).get(field_off)

    # -- type tree ---------------------------------------------------------

    def members(self, sec: int, off: int) -> List[Member]:
        """The member list of a type definition, cached and recursion-safe."""
        key = (sec, off)
        if key in self._type_cache:
            return self._type_cache[key]

        # Published before it is filled: a type can reference itself through a
        # bone's ExtendedData, and filling first would recurse forever.
        out: List[Member] = []
        self._type_cache[key] = out

        buf = self.section_bytes(sec)
        pos = off
        while pos + 32 <= len(buf):
            kind, name_ptr, elem_ptr, width = struct.unpack_from("<4I", buf, pos)
            if kind == END:
                break
            name = ""
            nm = self._follow(sec, pos + 4)
            if nm is not None:
                name = self.c_string(*nm)
            et = self._follow(sec, pos + 8)
            out.append(Member(
                kind=kind, name=name,
                type_sec=et[0] if et else -1,
                type_off=et[1] if et else -1,
                array_width=max(1, width),
            ))
            pos += 32
        return out

    def c_string(self, sec: int, off: int) -> str:
        key = (sec, off)
        if key in self._strings:
            return self._strings[key]
        buf = self.section_bytes(sec)
        end = buf.find(b"\x00", off)
        if end < 0:
            end = len(buf)
        val = buf[off:end].decode("ascii", "replace")
        self._strings[key] = val
        return val

    # -- values ------------------------------------------------------------

    def read_struct(self, sec: int, off: int, type_sec: int, type_off: int) -> dict:
        out: Dict[str, Any] = {}
        buf = self.section_bytes(sec)
        pos = off
        for m in self.members(type_sec, type_off):
            value, size = self._read_member(buf, sec, pos, m)
            out[m.name or ("field_%d" % pos)] = value
            pos += size
        return out

    def _read_member(self, buf: bytes, sec: int, pos: int,
                     m: Member) -> Tuple[Any, int]:
        n = m.array_width

        if m.kind == INLINE:
            size = self._struct_size(m.type_sec, m.type_off) * n
            if n == 1:
                return self.read_struct(sec, pos, m.type_sec, m.type_off), size
            return [self.read_struct(sec, pos + i * (size // n), m.type_sec, m.type_off)
                    for i in range(n)], size

        if m.kind in _SCALAR_SIZE:
            unit = _SCALAR_SIZE[m.kind]
            total = unit * n
            if m.kind == TRANSFORM:
                return (_read_transform(buf, pos) if n == 1
                        else [_read_transform(buf, pos + i * 68) for i in range(n)]), total
            vals = [_read_scalar(buf, pos + i * unit, m.kind) for i in range(n)]
            return (vals[0] if n == 1 else vals), total

        if m.kind == STRING:
            tgt = self._follow(sec, pos)
            return (self.c_string(*tgt) if tgt else ""), 4

        if m.kind == REFERENCE:
            tgt = self._follow(sec, pos)
            if tgt is None:
                return None, 4
            return self._read_typed(tgt, m), 4

        if m.kind == REFERENCE_TO_ARRAY:
            count, = struct.unpack_from("<I", buf, pos)
            tgt = self._follow(sec, pos + 4)
            if tgt is None:
                return [], 8
            return self._read_array(tgt, m, count), 8

        if m.kind == ARRAY_OF_REFERENCES:
            count, = struct.unpack_from("<I", buf, pos)
            tgt = self._follow(sec, pos + 4)
            if tgt is None:
                return [], 8
            # The target is an array of pointers; each slot relocates on its own.
            items = []
            for i in range(count):
                slot = self._follow(tgt[0], tgt[1] + i * 4)
                items.append(self._read_typed(slot, m) if slot else None)
            return items, 8

        if m.kind == VARIANT_REFERENCE:
            tgt = self._follow(sec, pos + 4)
            if tgt is None:
                return None, 8
            tsec, toff = struct.unpack_from("<2I", self.section_bytes(sec), pos)
            return self._read_typed(tgt, Member(kind=INLINE, name="",
                                                type_sec=tsec, type_off=toff)), 8

        if m.kind == EMPTY_REFERENCE:
            return None, 0

        raise GR2Error("unsupported member kind %d (%s)" % (m.kind, m.name))

    def _read_typed(self, target: Tuple[int, int], m: Member) -> Any:
        if m.kind == ARRAY_OF_REFERENCES:
            return self.read_struct(target[0], target[1], m.type_sec, m.type_off)
        return self.read_struct(target[0], target[1], m.type_sec, m.type_off)

    def _read_array(self, target: Tuple[int, int], m: Member, count: int) -> List[Any]:
        """ReferenceToArray: ``count`` elements of the member's element type."""
        sec, off = target
        unit = self._type_size(m.type_sec, m.type_off)
        buf = self.section_bytes(sec)
        out = []
        for i in range(count):
            at = off + i * unit
            if unit == 4 and m.type_sec < 0:
                out.append(_read_scalar(buf, at, REAL32))
            else:
                out.append(self.read_struct(sec, at, m.type_sec, m.type_off))
        return out

    def _struct_size(self, sec: int, off: int) -> int:
        total = 0
        for m in self.members(sec, off):
            total += self._member_size(m)
        return total

    def _type_size(self, sec: int, off: int) -> int:
        return self._struct_size(sec, off)

    def _member_size(self, m: Member) -> int:
        n = m.array_width
        if m.kind == INLINE:
            return self._struct_size(m.type_sec, m.type_off) * n
        if m.kind in _SCALAR_SIZE:
            return _SCALAR_SIZE[m.kind] * n
        if m.kind == STRING or m.kind == REFERENCE:
            return 4
        if m.kind in (REFERENCE_TO_ARRAY, ARRAY_OF_REFERENCES, VARIANT_REFERENCE):
            return 8
        if m.kind == REFERENCE_TO_VARIANT_ARRAY:
            return 12
        if m.kind == EMPTY_REFERENCE:
            return 0
        raise GR2Error("unsupported member kind %d (%s)" % (m.kind, m.name))

    # -- entry point -------------------------------------------------------

    def root(self) -> dict:
        return self.read_struct(self.root_object[0], self.root_object[1],
                                self.root_type[0], self.root_type[1])


def _read_scalar(buf: bytes, off: int, kind: int) -> float:
    if kind == REAL32:
        return struct.unpack_from("<f", buf, off)[0]
    if kind in (INT8, BINORMAL_INT8):
        return struct.unpack_from("<b", buf, off)[0]
    if kind in (UINT8, NORMAL_UINT8):
        return struct.unpack_from("<B", buf, off)[0]
    if kind in (INT16, BINORMAL_INT16):
        return struct.unpack_from("<h", buf, off)[0]
    if kind in (UINT16, NORMAL_UINT16):
        return struct.unpack_from("<H", buf, off)[0]
    if kind == INT32:
        return struct.unpack_from("<i", buf, off)[0]
    if kind == UINT32:
        return struct.unpack_from("<I", buf, off)[0]
    if kind == REAL16:
        # Half precision; no curve in the shipped library uses it.
        return struct.unpack_from("<e", buf, off)[0]
    raise GR2Error("not a scalar kind: %d" % kind)


def _read_transform(buf: bytes, off: int) -> dict:
    """68 bytes: flags, Real32[3] position, Real32[4] quaternion xyzw, Real32[9]
    scale-shear. A component whose flag bit is clear is the identity, whatever
    bytes sit in its slot."""
    flags, = struct.unpack_from("<I", buf, off)
    pos = list(struct.unpack_from("<3f", buf, off + 4))
    quat = list(struct.unpack_from("<4f", buf, off + 16))
    ss = list(struct.unpack_from("<9f", buf, off + 32))
    if not flags & 1:
        pos = [0.0, 0.0, 0.0]
    if not flags & 2:
        quat = [0.0, 0.0, 0.0, 1.0]
    if not flags & 4:
        ss = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]
    return {"flags": flags, "position": pos, "quaternion": quat, "scaleShear": ss}


def read(path: str) -> GR2:
    with open(path, "rb") as fh:
        return GR2(fh.read())


# ---------------------------------------------------------------------------
# Structured extraction
# ---------------------------------------------------------------------------

def _curve(c) -> Optional[dict]:
    """One curve as {degree, knots, controls}. Controls hold 3 values per knot
    for a position, 4 for a quaternion, 9 for scale-shear."""
    if not c:
        return None
    knots = [k["Real32"] if isinstance(k, dict) else float(k) for k in (c.get("Knots") or [])]
    controls = [v["Real32"] if isinstance(v, dict) else float(v)
                for v in (c.get("Controls") or [])]
    if not knots and not controls:
        return None
    return {"degree": int(c.get("Degree", 0)), "knots": knots, "controls": controls}


def extract(root: dict) -> dict:
    """Pull the skeleton and the clips out of a parsed GR2 root.

    An animation file carries its own copy of the skeleton it animates, so one
    read yields bones, rest pose and curves together -- which is why the
    standalone files under ``bin/Skeletons`` are not needed.
    """
    skeletons = []
    for sk in (root.get("Skeletons") or []):
        bones = []
        for b in (sk.get("Bones") or []):
            t = b.get("Transform") or {}
            bones.append({
                "name": b.get("Name", ""),
                "parent": int(b.get("ParentIndex", -1)),
                "position": list(t.get("position", [0.0, 0.0, 0.0])),
                "quaternion": list(t.get("quaternion", [0.0, 0.0, 0.0, 1.0])),
                "scaleShear": list(t.get("scaleShear",
                                         [1, 0, 0, 0, 1, 0, 0, 0, 1])),
                "inverseBind": [float(x) for x in
                                (b.get("InverseWorldTransform") or [])],
            })
        skeletons.append({"name": sk.get("Name", ""), "bones": bones})

    animations = []
    for a in (root.get("Animations") or []):
        tracks = []
        for tg in (a.get("TrackGroups") or []):
            for t in (tg.get("TransformTracks") or []):
                tracks.append({
                    "name": t.get("Name", ""),
                    "position": _curve(t.get("PositionCurve")),
                    "orientation": _curve(t.get("OrientationCurve")),
                    "scaleShear": _curve(t.get("ScaleShearCurve")),
                })
        animations.append({
            "name": a.get("Name", ""),
            "duration": float(a.get("Duration", 0.0)),
            "timeStep": float(a.get("TimeStep", 0.0)),
            "tracks": tracks,
        })

    models = [{"name": m.get("Name", "")} for m in (root.get("Models") or [])]
    return {
        "source": root.get("FromFileName", ""),
        "models": models,
        "skeletons": skeletons,
        "animations": animations,
    }


def extract_file(path: str) -> dict:
    return extract(read(path).root())
