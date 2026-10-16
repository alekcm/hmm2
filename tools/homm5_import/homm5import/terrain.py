"""``GroundTerrain.bin`` / ``UndergroundTerrain.bin`` reader.

Container: the Nival record grammar (see :mod:`homm5import.container`).  Inside
the single root block the planes appear in a fixed order, each introduced by a
framing group of two dimension scalars and the array itself::

    tag02 <u32>  TileX (tiles, not vertices)
    tag03 <u32>  TileY
    tag04        layer/format block
    tag05        height        f32  (V x V)   V = tiles + 1
    tag07        ground flags  u8   (V x V)
    tag08        reserved      u8   (V x V)
    tag0a        river plane   u8   ((2V-1) x (2V-1))
    tag0d        unknown block
    tag0e        single byte
    tag0f        passability   u8   (V x V)
    tag10        coarse RMG grid (8-byte records), optional

Height is discrete: 2.0 is the default ground level, 0.0 a dug-out bed, 1.6 the
shore ring.  Ground flags: 0 water, 16 ordinary ground, 32 plateau, bit 3 ramp.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional

from .container import Node, parse_records

TAG_TILE_X = 0x02
TAG_TILE_Y = 0x03
TAG_HEIGHT = 0x05
TAG_FLAGS = 0x07
TAG_RESERVED = 0x08
TAG_RIVER = 0x0A
TAG_PASSABILITY = 0x0F
TAG_RMG_GRID = 0x10

DEFAULT_GROUND = 2.0
SHORE_LEVEL = 1.6
WATER_FILL_LEVEL = 1.5

FLAG_WATER = 0
FLAG_GROUND = 16
FLAG_PLATEAU = 32
FLAG_RAMP_BIT = 8


@dataclass
class Plane:
    width: int
    height: int
    data: bytes

    @property
    def count(self) -> int:
        return self.width * self.height

    def at(self, x: int, y: int) -> float:
        return self.data[y * self.width + x]


@dataclass
class Terrain:
    tile_x: int = 0
    tile_y: int = 0
    height: Optional[Plane] = None
    flags: Optional[Plane] = None
    reserved: Optional[Plane] = None
    river: Optional[Plane] = None
    passability: Optional[Plane] = None

    @property
    def vertex_count(self) -> int:
        return self.tile_x + 1

    def height_at(self, x: int, y: int) -> float:
        if self.height is None:
            return DEFAULT_GROUND
        off = y * self.height.width + x
        return struct.unpack_from("<f", self.height.data, off * 4)[0]

    def flag_at(self, x: int, y: int) -> int:
        if self.flags is None:
            return FLAG_GROUND
        return self.flags.data[y * self.flags.width + x]

    def passable_at(self, x: int, y: int) -> bool:
        if self.passability is None or len(self.passability.data) < self.passability.count:
            return self.flag_at(x, y) != FLAG_WATER
        return self.passability.data[y * self.passability.width + x] != 0

    # -- statistics used by tests ------------------------------------------
    def height_stats(self) -> tuple:
        n = self.height.count
        flat = struct.unpack("<%df" % n, self.height.data[: n * 4])
        return min(flat), max(flat), sum(flat) / n

    def flag_histogram(self) -> dict:
        hist: dict = {}
        for b in self.flags.data[: self.flags.count]:
            hist[b] = hist.get(b, 0) + 1
        return hist

    def water_vertex_count(self) -> int:
        if self.flags is None:
            return 0
        return sum(1 for b in self.flags.data[: self.flags.count] if b == FLAG_WATER)


def _plane(node: Node, kind: str) -> Optional[Plane]:
    dims: List[int] = []
    payload: Optional[bytes] = None
    for child in node.children:
        # A plane of constant bytes (an all-water map is all zeros) still parses
        # as records, so take the raw payload rather than trusting is_leaf.
        if child.tag in (1, 2) and len(child.data) == 4:
            dims.append(child.u32())
        elif child.tag == 3:
            payload = child.data
    if payload is None or not dims:
        return None
    w = h = dims[0]
    if len(dims) > 1:
        h = dims[1]
    if kind == "f32":
        if len(payload) != w * h * 4:
            return None
    else:
        if len(payload) != w * h:
            return None
    return Plane(width=w, height=h, data=payload)


def decode(data: bytes) -> Terrain:
    nodes = parse_records(data)
    root = None
    for n in nodes:
        if n.tag == 1 and not n.is_leaf:
            root = n
            break
    if root is None:
        raise ValueError("no terrain root block")
    # The planes live one level down.
    block = root.find(1) or root

    terr = Terrain()
    tx = block.find(TAG_TILE_X)
    ty = block.find(TAG_TILE_Y)
    if tx is not None and tx.is_leaf:
        terr.tile_x = tx.u32()
    if ty is not None and ty.is_leaf:
        terr.tile_y = ty.u32()

    for tag, kind, attr in (
        (TAG_HEIGHT, "f32", "height"),
        (TAG_FLAGS, "u8", "flags"),
        (TAG_RESERVED, "u8", "reserved"),
        (TAG_RIVER, "u8", "river"),
        (TAG_PASSABILITY, "u8", "passability"),
    ):
        node = block.find(tag)
        if node is None:
            continue
        plane = _plane(node, kind)
        if plane is not None:
            setattr(terr, attr, plane)
    return terr


def decode_file(path: str) -> Terrain:
    with open(path, "rb") as fh:
        return decode(fh.read())
