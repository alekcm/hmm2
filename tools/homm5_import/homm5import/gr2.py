<<<<<<< HEAD
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
=======
"""Granny2 (GR2 v6) container, Oodle1 (comp=2) decompressor, and Skeleton/Animation parser.

Decompiled directly from `bin/granny2.dll` (ImageBase `0x10000000`):
  - `GrannyDecompressData` (`0x1000f430`)
  - `Oodle1_InitState` (`0x1000cc10`)
  - `Oodle1_DecompressLoop` (`0x1000f2e0`)
  - `Oodle1_DecodeStep` (`0x1000ee20`)
  - `Oodle1_BitstreamRead` (`0x1000cf30`)
  - `Oodle1_AdaptiveModelRead` (`0x1000db10`)

Supports both:
  - `comp == 0` (uncompressed GR2 v6 sections: 2,816 files in `data/bin/animations/`)
  - `comp == 2` (Oodle1 LZ+adaptive arithmetic coded GR2 v6 sections: 2,242 files in
    `data/bin/Skeletons/` and 577 ToTE files in `data/bin/animations/`)
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
"""

from __future__ import annotations

<<<<<<< HEAD
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

# struct module codes, for reading a run of scalars in one call.
_SCALAR_FORMAT = {
    REAL32: "f", INT8: "b", UINT8: "B", BINORMAL_INT8: "b", NORMAL_UINT8: "B",
    INT16: "h", UINT16: "H", BINORMAL_INT16: "h", NORMAL_UINT16: "H",
    INT32: "i", UINT32: "I", REAL16: "e",
}

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
        # Section payloads are sliced out of the file once and kept.  Doing the
        # slice per read instead is quadratic: a curve-heavy file issues one
        # section copy per knot, which is minutes per file rather than seconds.
        self._section_cache: Dict[int, bytes] = {}
        self._size_cache: Dict[Tuple[int, int], int] = {}
        self._scalar_cache: Dict[Tuple[int, int], int] = {}

    # -- sections ----------------------------------------------------------

    def is_stored(self) -> bool:
        return all(s.compression == COMP_STORED for s in self.sections)

    def compressed_sections(self) -> List[int]:
        return [i for i, s in enumerate(self.sections)
                if s.compression != COMP_STORED]

    def section_bytes(self, i: int) -> bytes:
        cached = self._section_cache.get(i)
        if cached is not None:
            return cached
        s = self.sections[i]
        if s.compression != COMP_STORED:
            raise GR2Error("section %d uses compression %d (Oodle1 unsupported)"
                           % (i, s.compression))
        payload = self.data[s.offset:s.offset + s.size]
        self._section_cache[i] = payload
        return payload

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

    def _single_scalar(self, sec: int, off: int) -> Optional[int]:
        """The kind of a struct whose only field is one scalar, else None."""
        key = (sec, off)
        if key in self._scalar_cache:
            kind = self._scalar_cache[key]
            return kind if kind >= 0 else None
        ms = self.members(sec, off)
        kind = -1
        if len(ms) == 1 and ms[0].array_width == 1 and ms[0].kind in _SCALAR_FORMAT:
            kind = ms[0].kind
        self._scalar_cache[key] = kind
        return kind if kind >= 0 else None

    def _read_array(self, target: Tuple[int, int], m: Member, count: int) -> List[Any]:
        """ReferenceToArray: ``count`` elements of the member's element type."""
        sec, off = target
        buf = self.section_bytes(sec)

        # Knots and controls are arrays of a one-field "Real32" struct.  Read as
        # structs they cost a dict and three calls per knot, which is where
        # almost all of a curve-heavy file's time went.  They are consumed by
        # gr2._curve, which already accepts either shape.
        if m.name in ("Knots", "Controls"):
            kind = self._single_scalar(m.type_sec, m.type_off)
            if kind is not None:
                if count <= 0:
                    return []
                return list(struct.unpack_from(
                    "<%d%s" % (count, _SCALAR_FORMAT[kind]), buf, off))

        unit = self._type_size(m.type_sec, m.type_off)
        out = []
        for i in range(count):
            at = off + i * unit
            if unit == 4 and m.type_sec < 0:
                out.append(_read_scalar(buf, at, REAL32))
            else:
                out.append(self.read_struct(sec, at, m.type_sec, m.type_off))
        return out

    def _struct_size(self, sec: int, off: int) -> int:
        key = (sec, off)
        cached = self._size_cache.get(key)
        if cached is not None:
            return cached
        total = 0
        for m in self.members(sec, off):
            total += self._member_size(m)
        self._size_cache[key] = total
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
=======
import ctypes
from dataclasses import dataclass, field, asdict
from pathlib import Path
import struct
import subprocess
import tempfile
from typing import Dict, List, Optional, Tuple


GR2_LITTLE_ENDIAN_MAGICS = {
    bytes.fromhex("b867b0caf86db10f84728c7e5e19001e"),
    bytes.fromhex("c06bd9e56f03784f8e21a24075639660"),
}


_OODLE1_C_SOURCE = r"""
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    uint32_t low;       // +0x00
    uint32_t range;     // +0x04
    const uint8_t *ptr; // +0x08
    uint32_t step;      // +0x0c
    uint32_t bit_carry; // +0x10
} ArithBits;

static void arith_init(ArithBits *ab, const uint8_t *src) {
    ab->ptr = src + 1;
    uint32_t b0 = src[0];
    ab->bit_carry = b0 & 1;
    ab->low = b0 >> 1;
    ab->range = 0x80;
    ab->step = 0;
}

static void arith_renorm(ArithBits *ab) {
    uint32_t range = ab->range;
    if (range <= 0x800000) {
        uint32_t low = ab->low;
        uint32_t carry = ab->bit_carry & 1;
        const uint8_t *p = ab->ptr;
        do {
            low = ((low * 2 + carry) << 7) | ((*p) >> 1);
            carry = (*p) & 1;
            p++;
            range <<= 8;
        } while (range <= 0x800000);
        ab->ptr = p;
        ab->bit_carry = carry;
        ab->low = low;
        ab->range = range;
    }
}

static uint32_t arith_peek_shift(ArithBits *ab, uint32_t shift, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t step = ab->range >> shift;
    ab->step = step;
    uint32_t v = ab->low / step;
    return (v < max_val) ? v : (max_val - 1);
}

static void arith_consume(ArithBits *ab, uint32_t cum_low, uint32_t freq, uint32_t total) {
    uint32_t step = ab->step;
    uint32_t sub = step * cum_low;
    ab->low -= sub;
    if (cum_low + freq < total) {
        ab->range = step * freq;
    } else {
        ab->range -= sub;
    }
}

static uint32_t arith_get_uniform(ArithBits *ab, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t range = ab->range;
    uint32_t step = range / max_val;
    uint32_t low = ab->low;
    uint32_t sym = low / step;
    if (sym >= max_val) sym = max_val - 1;
    uint32_t sub = sym * step;
    ab->low = low - sub;
    if (sym + 1 < max_val) {
        ab->range = step;
    } else {
        ab->range = range - sub;
    }
    return sym;
}

static uint32_t arith_get_bits(ArithBits *ab, uint32_t shift, uint32_t max_val) {
    arith_renorm(ab);
    uint32_t range = ab->range;
    uint32_t step = range >> shift;
    uint32_t low = ab->low;
    uint32_t sym = low / step;
    if (sym >= max_val) sym = max_val - 1;
    uint32_t sub = sym * step;
    ab->low = low - sub;
    if (sym + 1 < max_val) {
        ab->range = step;
    } else {
        ab->range = range - sub;
    }
    return sym;
}

typedef struct {
    uint16_t total_freq;       // +0x00
    uint16_t next_rebuild;     // +0x02
    uint16_t rebuild_step;     // +0x04
    uint16_t cur_step;         // +0x06
    uint16_t max_freq;         // +0x08
    uint16_t num_active;       // +0x0a
    uint16_t num_table;        // +0x0c
    uint16_t max_symbols;      // +0x0e
    uint16_t *symbols;         // +0x10
    uint16_t *freqs;           // +0x14
    uint32_t search_depth;     // +0x18
    uint32_t search_steps[13]; // +0x1c .. +0x4c
    uint16_t cum[1];           // +0x50 (variable length)
} Model;

static uint32_t model_size(uint32_t max_sym) {
    uint32_t a = (max_sym + 5) & ~3u;
    return a * 6 + sizeof(Model) + 32;
}

static Model* model_init(void *mem, uint32_t max_val, uint32_t max_sym) {
    uint32_t sz = model_size(max_sym);
    memset(mem, 0, sz);
    Model *m = (Model*)mem;
    uint32_t a = (max_sym + 5) & ~3u;
    uint16_t *freqs = (uint16_t*)((uint8_t*)mem + sizeof(Model) + 16 + a * 2);
    uint16_t *syms  = (uint16_t*)((uint8_t*)freqs + a * 2);
    m->symbols = syms;
    m->freqs = freqs;
    m->cum[1] = 0xc000;
    m->cum[2] = 0xc000;
    m->cum[3] = 0xc000;
    m->cum[4] = 0xc000;
    m->cum[5] = 0xc000;
    m->max_symbols = (uint16_t)max_sym;
    m->total_freq = 4;
    m->cum[0] = 0x8000;
    freqs[0] = 4;
    m->next_rebuild = 8;
    m->cur_step = 4;
    uint32_t mf = max_val << 5;
    if (mf < 0x100) mf = 0x100;
    else if (mf > 0x3b38) mf = 0x3b38;
    m->max_freq = (uint16_t)mf;
    uint32_t rs = max_val * 2;
    if (rs < 0x80) {
        m->rebuild_step = 0x80;
    } else {
        uint32_t lim = (m->max_freq >> 1) - 0x20;
        if (rs >= lim) rs = lim;
        m->rebuild_step = (uint16_t)rs;
    }
    return m;
}

static void model_rescale(Model *m) {
    m->freqs[0] >>= 1;
    m->total_freq = m->freqs[0];
    uint16_t best_freq = 0;
    uint32_t best_idx = 0;
    uint32_t i = 1;
    while (i <= m->num_active) {
        while (m->freqs[i] <= 1) {
            if (i >= m->num_active) {
                m->freqs[i] = 0;
                m->num_active--;
                goto done_loop;
            }
            m->freqs[i] = m->freqs[m->num_active];
            m->freqs[m->num_active] = 0;
            m->symbols[i] = m->symbols[m->num_active];
            m->num_active--;
        }
        m->freqs[i] >>= 1;
        m->total_freq += m->freqs[i];
        if (m->freqs[i] > best_freq) {
            best_freq = m->freqs[i];
            best_idx = i;
        }
        i++;
    }
done_loop:
    if (best_freq != 0 && m->num_active != 0 && best_idx != m->num_active) {
        uint16_t tf = m->freqs[m->num_active];
        m->freqs[m->num_active] = m->freqs[best_idx];
        m->freqs[best_idx] = tf;
        uint16_t ts = m->symbols[m->num_active];
        m->symbols[m->num_active] = m->symbols[best_idx];
        m->symbols[best_idx] = ts;
    }
    if (m->num_active != m->max_symbols && m->freqs[0] == 0) {
        m->freqs[0] = 1;
        m->total_freq++;
    }
}

static void model_rebuild_cum(Model *m) {
    int32_t scale = 0x20000 / (int32_t)m->total_freq;
    uint32_t acc = (((uint32_t)m->freqs[0] * (uint32_t)scale) >> 3) + 0x8000;
    m->cum[0] = 0x8000;
    for (uint32_t i = 1; ; i++) {
        m->cum[i] = (uint16_t)acc;
        if (i > m->num_active) break;
        acc += ((uint32_t)m->freqs[i] * (uint32_t)scale) >> 3;
    }
    uint32_t step = m->cur_step * 2;
    if (step > m->rebuild_step) {
        m->next_rebuild = m->total_freq + m->rebuild_step;
        m->num_table = m->num_active;
        m->cum[m->num_active + 1] = 0xc000;
    } else {
        m->cur_step = (uint16_t)step;
        m->next_rebuild = m->total_freq + (uint16_t)step;
        m->num_table = m->num_active;
        m->cum[m->num_active + 1] = 0xc000;
    }
}

static void model_build_steps(uint32_t *out, uint32_t n) {
    uint32_t steps[16];
    uint32_t cnt = 0;
    uint32_t v = n + 1;
    do {
        v = (v + 1) >> 1;
        steps[cnt++] = v;
    } while (v > 4);
    cnt--;
    memset(out, 0, 14 * sizeof(uint32_t));
    out[0] = cnt;
    out[1] = steps[0];
    if (cnt > 0) {
        for (uint32_t i = 0; i < cnt; i++) {
            out[14 - cnt + i] = steps[1 + i];
        }
    }
}

static uintptr_t model_decode(Model *m, ArithBits *ab) {
    if (m->total_freq >= m->next_rebuild) {
        if (m->next_rebuild >= m->max_freq) {
            model_rescale(m);
        }
        model_rebuild_cum(m);
        uint16_t nt = m->num_table;
        m->cum[nt + 2] = 0xc000;
        m->cum[nt + 3] = 0xc000;
        m->cum[nt + 4] = 0xc000;
        m->cum[nt + 5] = 0xc000;
        m->cum[nt + 6] = 0xc000;
        model_build_steps(&m->search_depth, nt);
    }
    uint32_t target = arith_peek_shift(ab, 14, 0x4000) + 0x8000;
    int32_t idx = (int32_t)m->search_steps[0];
    uint32_t depth = m->search_depth;
    for (uint32_t k = 11 - depth; k < 11; k++) {
        int32_t s = (int32_t)m->search_steps[2 + k];
        if (m->cum[idx] > target) idx -= s;
        else idx += s;
    }
    if (m->cum[idx] > target) idx -= 2;
    else idx += 2;
    if (m->cum[idx] > target) {
        if (m->cum[idx - 1] > target) idx -= 2;
        else idx -= 1;
    } else {
        if (m->cum[idx + 1] <= target) idx += 1;
    }
    uint32_t c_low = m->cum[idx];
    uint32_t c_high = m->cum[idx + 1];
    arith_consume(ab, c_low - 0x8000, c_high - c_low, 0x4000);
    m->freqs[idx]++;
    m->total_freq++;
    if (idx > 0) {
        return (uintptr_t)m->symbols[idx];
    }
    if (m->num_active != m->num_table) {
        if (arith_get_bits(ab, 1, 2) != 0) {
            uint32_t r = arith_get_uniform(ab, m->num_active - m->num_table);
            uint32_t sym_idx = m->num_table + r + 1;
            m->freqs[sym_idx] += 2;
            m->total_freq += 2;
            return (uintptr_t)m->symbols[sym_idx];
        }
    }
    m->num_active++;
    uint32_t na = m->num_active;
    m->freqs[na] += 2;
    m->total_freq += 2;
    if (m->num_active == m->max_symbols && m->freqs[0] != 0) {
        m->total_freq -= m->freqs[0];
        m->freqs[0] = 0;
    }
    return (uintptr_t)&m->symbols[na];
}

int granny_decompress_oodle1(
    const uint8_t *comp, uint32_t comp_len,
    uint8_t *uncomp, uint32_t stop0, uint32_t stop1, uint32_t decomp_len
) {
    ArithBits ab;
    arith_init(&ab, comp + 0x24);
    uint32_t stops[3] = { stop0, stop1, decomp_len };
    uint32_t out_pos = 0;
    static const uint32_t len_table[4] = { 128, 192, 256, 512 };
    for (int phase = 0; phase < 3; phase++) {
        const uint32_t *hdr = (const uint32_t*)(comp + phase * 12);
        uint32_t h0 = hdr[0];
        uint32_t h1 = hdr[1];
        uint32_t h2 = hdr[2];
        uint32_t lit_max = h0 & 0x1ff;
        uint32_t win_max = h0 >> 9;
        uint32_t off_low_max = (win_max < 4) ? (win_max + 1) : 4;
        uint32_t off_mid_max = ((win_max >> 2) < 0x100) ? ((win_max >> 2) + 1) : 0x100;
        uint32_t off_hi_max  = (win_max >> 10) + 1;
        uint32_t sym_cap     = h1 & 0x1ff;

        void *arena = calloc(1, 4 * 1024 * 1024);
        if (!arena) return -1;
        uint8_t *p = (uint8_t*)arena;
        Model *lit_m[4];
        for (int i = 0; i < 4; i++) {
            lit_m[i] = model_init(p, lit_max - 1, sym_cap);
            p += model_size(sym_cap);
        }
        Model *len_m[64];
        for (int g = 0; g < 4; g++) {
            uint32_t g_cap = (h2 >> (24 - g * 8)) & 0xff;
            for (int k = 0; k < 16; k++) {
                len_m[g * 16 + k] = model_init(p, 0x40, g_cap);
                p += model_size(g_cap);
            }
        }
        uint32_t last_cap = h2 & 0xff;
        Model *len_m64 = model_init(p, 0x40, last_cap);
        p += model_size(last_cap);

        Model **off_mid_m = (Model**)calloc(off_hi_max + 1, sizeof(Model*));
        for (uint32_t i = 0; i < off_hi_max; i++) {
            off_mid_m[i] = model_init(p, off_mid_max - 1, off_mid_max);
            p += model_size(off_mid_max);
        }
        Model *off_low_m = model_init(p, off_low_max - 1, off_low_max);
        p += model_size(off_low_max);
        uint32_t hi_cap = (h1 >> 19) + 1;
        Model *off_hi_m = model_init(p, off_hi_max - 1, hi_cap);

        uint32_t cur_win = 0;
        uint32_t prev_len_sym = 0;

        while (out_pos < stops[phase]) {
            Model *lm = (prev_len_sym < 64) ? len_m[prev_len_sym] : len_m64;
            uintptr_t r = model_decode(lm, &ab);
            uint32_t len_sym;
            if (r > 0x10000) {
                len_sym = arith_get_uniform(&ab, 0x41);
                *(uint16_t*)r = (uint16_t)len_sym;
            } else {
                len_sym = (uint32_t)r;
            }
            prev_len_sym = len_sym;
            if (len_sym == 0) {
                Model *m = lit_m[out_pos & 3];
                uintptr_t lr = model_decode(m, &ab);
                uint32_t b;
                if (lr > 0x10000) {
                    b = arith_get_uniform(&ab, lit_max);
                    *(uint16_t*)lr = (uint16_t)b;
                } else {
                    b = (uint32_t)lr;
                }
                uncomp[out_pos++] = (uint8_t)b;
                cur_win++;
            } else {
                uint32_t eff_win = (win_max < cur_win) ? win_max : cur_win;
                uint32_t match_len = (len_sym < 0x3d) ? (len_sym + 1) : len_table[len_sym - 0x3d];

                uintptr_t r_low = model_decode(off_low_m, &ab);
                uint32_t o_low;
                if (r_low > 0x10000) {
                    o_low = arith_get_uniform(&ab, off_low_max);
                    *(uint16_t*)r_low = (uint16_t)o_low;
                } else {
                    o_low = (uint32_t)r_low;
                }

                uintptr_t r_hi = model_decode(off_hi_m, &ab);
                uint32_t o_hi;
                if (r_hi > 0x10000) {
                    o_hi = arith_get_uniform(&ab, (eff_win >> 10) + 1);
                    *(uint16_t*)r_hi = (uint16_t)o_hi;
                } else {
                    o_hi = (uint32_t)r_hi;
                }

                uintptr_t r_mid = model_decode(off_mid_m[o_hi], &ab);
                uint32_t o_mid;
                if (r_mid > 0x10000) {
                    uint32_t lim = (eff_win < 0x400) ? ((eff_win >> 2) + 1) : 0x100;
                    o_mid = arith_get_uniform(&ab, lim);
                    *(uint16_t*)r_mid = (uint16_t)o_mid;
                } else {
                    o_mid = (uint32_t)r_mid;
                }

                uint32_t offset = ((o_hi << 8) + o_mid) * 4 + o_low + 1;
                cur_win += match_len;
                for (uint32_t k = 0; k < match_len; k++) {
                    uncomp[out_pos + k] = uncomp[out_pos - offset + k];
                }
                out_pos += match_len;
            }
        }
        free(off_mid_m);
        free(arena);
    }
    return (int)out_pos;
}
"""

_ACCEL_LIB: Optional[ctypes.CDLL] = None


def _get_oodle1_accel() -> Optional[ctypes.CDLL]:
    global _ACCEL_LIB
    if _ACCEL_LIB is not None:
        return _ACCEL_LIB
    so_path = Path(tempfile.gettempdir()) / "libhomm5_oodle1.so"
    if not so_path.is_file():
        c_path = Path(tempfile.gettempdir()) / "homm5_oodle1.c"
        c_path.write_text(_OODLE1_C_SOURCE, encoding="utf-8")
        try:
            subprocess.run(
                ["gcc", "-O3", "-shared", "-fPIC", str(c_path), "-o", str(so_path)],
                check=True,
                capture_output=True,
            )
        except Exception:
            return None
    try:
        lib = ctypes.CDLL(str(so_path))
        lib.granny_decompress_oodle1.argtypes = [
            ctypes.c_char_p,
            ctypes.c_uint32,
            ctypes.c_char_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_uint32,
        ]
        lib.granny_decompress_oodle1.restype = ctypes.c_int
        _ACCEL_LIB = lib
        return lib
    except Exception:
        return None


def decompress_oodle1(
    comp_bytes: bytes,
    stop0: int,
    stop1: int,
    decomp_size: int,
) -> bytes:
    """Decompress a GR2 Oodle1 (comp == 2) section."""
    if decomp_size == 0:
        return b""
    padded_comp = comp_bytes + b"\x00" * 64
    out_buf = ctypes.create_string_buffer(decomp_size + 64)
    lib = _get_oodle1_accel()
    if lib is None:
        raise RuntimeError("Failed to initialize Oodle1 decompressor shared library")
    res = lib.granny_decompress_oodle1(
        padded_comp,
        len(comp_bytes),
        out_buf,
        stop0,
        stop1,
        decomp_size,
    )
    if res != decomp_size:
        raise ValueError(f"Oodle1 decompression returned {res}, expected {decomp_size}")
    return out_buf.raw[:decomp_size]


@dataclass
class GR2SectionHeader:
    compression: int
    data_offset: int
    data_size: int
    decompressed_size: int
    alignment: int
    first_16bit: int
    first_8bit: int
    fixup_offset: int
    fixup_count: int
    marshall_offset: int
    marshall_count: int


@dataclass
class GrannyBone:
    name: str
    parent_index: int
    flags: int
    position: Tuple[float, float, float]
    orientation: Tuple[float, float, float, float]
    scale_shear: Tuple[float, ...]
    inverse_world_4x4: Tuple[float, ...]


@dataclass
class GrannySkeleton:
    name: str
    bones: List[GrannyBone] = field(default_factory=list)

    def to_dict(self) -> Dict[str, object]:
        return asdict(self)


@dataclass
class GrannyCurve2:
    format: int
    degree: int
    knots: List[float] = field(default_factory=list)
    controls: List[float] = field(default_factory=list)


@dataclass
class GrannyTransformTrack:
    name: str
    flags: int
    position_curve: GrannyCurve2
    orientation_curve: GrannyCurve2
    scale_shear_curve: GrannyCurve2


@dataclass
class GrannyTrackGroup:
    name: str
    transform_tracks: List[GrannyTransformTrack] = field(default_factory=list)


@dataclass
class GrannyAnimation:
    name: str
    duration: float
    time_step: float
    oversampling: float
    track_groups: List[GrannyTrackGroup] = field(default_factory=list)

    def to_dict(self) -> Dict[str, object]:
        return asdict(self)


@dataclass
class GR2File:
    version: int
    file_size: int
    crc32: int
    num_sections: int
    root_type_section: int
    root_type_offset: int
    root_node_section: int
    root_node_offset: int
    tag: int
    sections: List[GR2SectionHeader]
    section_buffers: List[bytes]
    fixup_map: Dict[Tuple[int, int], Tuple[int, int]]
    skeletons: List[GrannySkeleton] = field(default_factory=list)
    animations: List[GrannyAnimation] = field(default_factory=list)

    def read_u32(self, sec: int, off: int) -> int:
        return struct.unpack_from("<I", self.section_buffers[sec], off)[0]

    def read_i32(self, sec: int, off: int) -> int:
        return struct.unpack_from("<i", self.section_buffers[sec], off)[0]

    def read_f32(self, sec: int, off: int) -> float:
        return struct.unpack_from("<f", self.section_buffers[sec], off)[0]

    def deref(self, sec: int, off: int) -> Optional[Tuple[int, int]]:
        return self.fixup_map.get((sec, off))

    def read_cstring_at_ptr(self, sec: int, off: int) -> str:
        target = self.deref(sec, off)
        if target is None:
            return ""
        t_sec, t_off = target
        buf = self.section_buffers[t_sec]
        end = buf.find(b"\x00", t_off, t_off + 256)
        if end == -1:
            end = min(len(buf), t_off + 256)
        return buf[t_off:end].decode("utf-8", errors="replace")


def parse_gr2_bytes(raw: bytes) -> GR2File:
    """Parse a GR2 v6 binary file (supporting both comp=0 uncompressed and comp=2 Oodle1)."""
    if len(raw) < 64:
        raise ValueError(f"GR2 file too small ({len(raw)} bytes)")
    magic = raw[:16]
    if magic not in GR2_LITTLE_ENDIAN_MAGICS:
        raise ValueError(f"Unrecognized GR2 magic: {magic.hex()}")

    hdr_size, hdr_fmt = struct.unpack_from("<2I", raw, 16)
    if hdr_fmt != 0:
        raise ValueError(f"Compressed GR2 header (format={hdr_fmt}) is not supported")

    version, file_size, crc, sec_off, num_secs = struct.unpack_from("<5I", raw, 32)
    rt_sec, rt_off, rn_sec, rn_off, tag = struct.unpack_from("<5I", raw, 52)
    sec_table_pos = 32 + sec_off

    sections: List[GR2SectionHeader] = []
    section_buffers: List[bytes] = []
    fixup_map: Dict[Tuple[int, int], Tuple[int, int]] = {}

    for s_idx in range(num_secs):
        vals = struct.unpack_from("<11I", raw, sec_table_pos + s_idx * 44)
        sh = GR2SectionHeader(*vals)
        sections.append(sh)

        if sh.decompressed_size == 0:
            section_buffers.append(b"")
        elif sh.compression == 0:
            section_buffers.append(raw[sh.data_offset:sh.data_offset + sh.decompressed_size])
        elif sh.compression == 2:
            comp_slice = raw[sh.data_offset:sh.data_offset + sh.data_size]
            decomp = decompress_oodle1(
                comp_slice,
                sh.first_16bit,
                sh.first_8bit,
                sh.decompressed_size,
            )
            section_buffers.append(decomp)
        else:
            raise ValueError(f"Unsupported GR2 compression type {sh.compression} in section {s_idx}")

        for f_idx in range(sh.fixup_count):
            src_off, dst_sec, dst_off = struct.unpack_from(
                "<3I", raw, sh.fixup_offset + f_idx * 12
            )
            fixup_map[(s_idx, src_off)] = (dst_sec, dst_off)

    gr2 = GR2File(
        version=version,
        file_size=file_size,
        crc32=crc,
        num_sections=num_secs,
        root_type_section=rt_sec,
        root_type_offset=rt_off,
        root_node_section=rn_sec,
        root_node_offset=rn_off,
        tag=tag,
        sections=sections,
        section_buffers=section_buffers,
        fixup_map=fixup_map,
    )
    _parse_granny_file_info(gr2)
    return gr2


def _parse_granny_file_info(gr2: GR2File) -> None:
    """Parse `granny_file_info` root structure at `(root_node_section, root_node_offset)`."""
    r_sec = gr2.root_node_section
    r_off = gr2.root_node_offset
    if r_sec >= len(gr2.section_buffers) or len(gr2.section_buffers[r_sec]) < r_off + 0x54:
        return

    # Skeletons: SkeletonCount at +0x1c, Skeletons** at +0x20
    skel_count = gr2.read_u32(r_sec, r_off + 0x1C)
    skel_arr = gr2.deref(r_sec, r_off + 0x20)
    if skel_count > 0 and skel_arr is not None:
        sa_sec, sa_off = skel_arr
        for i in range(skel_count):
            skel_ptr = gr2.deref(sa_sec, sa_off + i * 4)
            if skel_ptr is not None:
                gr2.skeletons.append(_parse_skeleton(gr2, skel_ptr[0], skel_ptr[1]))

    # Animations: AnimationCount at +0x4c, Animations** at +0x50
    anim_count = gr2.read_u32(r_sec, r_off + 0x4C)
    anim_arr = gr2.deref(r_sec, r_off + 0x50)
    if anim_count > 0 and anim_arr is not None:
        aa_sec, aa_off = anim_arr
        for i in range(anim_count):
            anim_ptr = gr2.deref(aa_sec, aa_off + i * 4)
            if anim_ptr is not None:
                gr2.animations.append(_parse_animation(gr2, anim_ptr[0], anim_ptr[1]))


def _parse_skeleton(gr2: GR2File, sec: int, off: int) -> GrannySkeleton:
    name = gr2.read_cstring_at_ptr(sec, off + 0x00)
    bone_count = gr2.read_u32(sec, off + 0x04)
    bones_ptr = gr2.deref(sec, off + 0x08)
    bones: List[GrannyBone] = []
    if bone_count > 0 and bones_ptr is not None:
        b_sec, b_off = bones_ptr
        buf = gr2.section_buffers[b_sec]
        for i in range(bone_count):
            bo = b_off + i * 156
            b_name = gr2.read_cstring_at_ptr(b_sec, bo + 0x00)
            parent_idx = struct.unpack_from("<i", buf, bo + 0x04)[0]
            flags = struct.unpack_from("<I", buf, bo + 0x08)[0]
            pos = struct.unpack_from("<3f", buf, bo + 0x0C)
            rot = struct.unpack_from("<4f", buf, bo + 0x18)
            scale = struct.unpack_from("<9f", buf, bo + 0x28)
            inv_world = struct.unpack_from("<16f", buf, bo + 0x4C)
            bones.append(
                GrannyBone(
                    name=b_name,
                    parent_index=parent_idx,
                    flags=flags,
                    position=pos,
                    orientation=rot,
                    scale_shear=scale,
                    inverse_world_4x4=inv_world,
                )
            )
    return GrannySkeleton(name=name, bones=bones)


def _parse_curve2(gr2: GR2File, sec: int, off: int, stride: int) -> GrannyCurve2:
    """Parse a GR2 v6 Curve2 (20 bytes) or inline curve header."""
    # Check whether CurveData is a pointer at off+0x04 or inline 20-byte Curve2
    curve_data_ptr = gr2.deref(sec, off + 0x04)
    if curve_data_ptr is not None and gr2.deref(sec, off + 0x08) is None:
        # GrannyCurve2 with CurveData* at +0x04
        cd_sec, cd_off = curve_data_ptr
        buf = gr2.section_buffers[cd_sec]
        fmt, degree = struct.unpack_from("<BB", buf, cd_off)
        return GrannyCurve2(format=fmt, degree=degree)

    # Direct 20-byte curve: Degree (+0x00), KnotCount (+0x04), Knots* (+0x08), ControlCount (+0x0c), Controls* (+0x10)
    buf = gr2.section_buffers[sec]
    degree, knot_count = struct.unpack_from("<2i", buf, off)
    knots_ptr = gr2.deref(sec, off + 0x08)
    ctrl_count = struct.unpack_from("<i", buf, off + 0x0C)[0]
    ctrls_ptr = gr2.deref(sec, off + 0x10)

    knots: List[float] = []
    if 0 < knot_count <= 65536 and knots_ptr is not None:
        k_sec, k_off = knots_ptr
        k_buf = gr2.section_buffers[k_sec]
        if k_off + knot_count * 4 <= len(k_buf):
            knots = list(struct.unpack_from(f"<{knot_count}f", k_buf, k_off))

    controls: List[float] = []
    if 0 < ctrl_count <= 262144 and ctrls_ptr is not None:
        c_sec, c_off = ctrls_ptr
        c_buf = gr2.section_buffers[c_sec]
        if c_off + ctrl_count * 4 <= len(c_buf):
            controls = list(struct.unpack_from(f"<{ctrl_count}f", c_buf, c_off))

    return GrannyCurve2(format=0, degree=degree, knots=knots, controls=controls)


def _parse_animation(gr2: GR2File, sec: int, off: int) -> GrannyAnimation:
    buf = gr2.section_buffers[sec]
    name = gr2.read_cstring_at_ptr(sec, off + 0x00)
    if gr2.deref(sec, off + 0x10) is not None:
        # GR2 v6.7 (tag 0x80000015): Name (+0x00), Duration (+0x04), TimeStep (+0x08),
        # TrackGroupCount (+0x0c), TrackGroups** (+0x10)
        duration, time_step = struct.unpack_from("<2f", buf, off + 0x04)
        oversampling = 1.0
        tg_count = struct.unpack_from("<i", buf, off + 0x0C)[0]
        tg_arr = gr2.deref(sec, off + 0x10)
    else:
        # GR2 v6.8 (tag 0x80000037): Name (+0x00), Duration (+0x04), TimeStep (+0x08),
        # Oversampling (+0x0c), TrackGroupCount (+0x10), TrackGroups** (+0x14)
        duration, time_step, oversampling = struct.unpack_from("<3f", buf, off + 0x04)
        tg_count = struct.unpack_from("<i", buf, off + 0x10)[0]
        tg_arr = gr2.deref(sec, off + 0x14)

    track_groups: List[GrannyTrackGroup] = []
    if 0 < tg_count <= 256 and tg_arr is not None:
        tga_sec, tga_off = tg_arr
        for i in range(tg_count):
            tg_ptr = gr2.deref(tga_sec, tga_off + i * 4)
            if tg_ptr is None:
                continue
            tg_sec, tg_off = tg_ptr
            tg_name = gr2.read_cstring_at_ptr(tg_sec, tg_off + 0x00)
            tt_count = gr2.read_u32(tg_sec, tg_off + 0x0C)
            tt_ptr = gr2.deref(tg_sec, tg_off + 0x10)
            tracks: List[GrannyTransformTrack] = []
            if 0 < tt_count <= 4096 and tt_ptr is not None:
                tt_sec, tt_off = tt_ptr
                # Detect track stride: 64 bytes (direct 20B curves) vs 44 bytes (8B Curve2 refs)
                # In GR2 v6, TransformTrack is 64B (or 68B with Flags in v6.8)
                tt_buf = gr2.section_buffers[tt_sec]
                # Check if second track name pointer is at +64 or +68 or +44
                stride = 64
                if tt_count > 1:
                    for cand in (64, 68, 44):
                        if gr2.deref(tt_sec, tt_off + cand) is not None:
                            stride = cand
                            break
                for j in range(tt_count):
                    to = tt_off + j * stride
                    if to + stride > len(tt_buf):
                        break
                    t_name = gr2.read_cstring_at_ptr(tt_sec, to + 0x00)
                    if stride == 68:
                        t_flags = gr2.read_u32(tt_sec, to + 0x04)
                        pos_c = _parse_curve2(gr2, tt_sec, to + 0x08, 3)
                        ori_c = _parse_curve2(gr2, tt_sec, to + 0x1C, 4)
                        scl_c = _parse_curve2(gr2, tt_sec, to + 0x30, 9)
                    elif stride == 64:
                        t_flags = 0
                        pos_c = _parse_curve2(gr2, tt_sec, to + 0x04, 3)
                        ori_c = _parse_curve2(gr2, tt_sec, to + 0x18, 4)
                        scl_c = _parse_curve2(gr2, tt_sec, to + 0x2C, 9)
                    else:
                        t_flags = gr2.read_u32(tt_sec, to + 0x04)
                        pos_c = GrannyCurve2(0, 0)
                        ori_c = GrannyCurve2(0, 0)
                        scl_c = GrannyCurve2(0, 0)
                    tracks.append(
                        GrannyTransformTrack(
                            name=t_name,
                            flags=t_flags,
                            position_curve=pos_c,
                            orientation_curve=ori_c,
                            scale_shear_curve=scl_c,
                        )
                    )
            track_groups.append(GrannyTrackGroup(name=tg_name, transform_tracks=tracks))

    return GrannyAnimation(
        name=name,
        duration=duration,
        time_step=time_step,
        oversampling=oversampling,
        track_groups=track_groups,
    )
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
