"""Nival record container.

`bin/Geometries/<uid>`, `bin/AIGeometries/<uid>`, `bin/Lights/<uid>` and every
`GroundTerrain.bin` share one self-describing record grammar:

    record  := <tag:u8> <sizefield> <payload>
    sizefield (even byte B) := B            ; payload length = B / 2
    sizefield (odd  byte B) := <u32 LE>     ; the odd byte IS the low byte of the
                                              u32, length = (u32 - 1) / 2

A top level stream ends with the 6 byte marker ``00 00 02 00 05 00``.

A payload is either a run of further records or a raw ("leaf") byte array; the
container itself carries no discriminator, the schema decides.  `Node.is_leaf`
therefore reports a *structural* verdict: a payload that parses cleanly as
records and consumes itself exactly is treated as a branch, otherwise as a leaf.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Iterator, List, Optional, Sequence

END_MARKER = b"\x00\x00\x02\x00\x05\x00"


class ContainerError(ValueError):
    """Raised when a buffer does not follow the record grammar."""


@dataclass
class Node:
    """One record of the container."""

    tag: int
    data: bytes
    start: int
    size_byte: int = 0
    children: List["Node"] = field(default_factory=list)

    # -- leaf accessors -----------------------------------------------------
    @property
    def is_leaf(self) -> bool:
        return not self.children

    def u32(self) -> int:
        """Scalar form ``tag 08 <u32>``."""
        return struct.unpack_from("<I", self.data, 0)[0]

    def i32(self) -> int:
        return struct.unpack_from("<i", self.data, 0)[0]

    def f32(self) -> float:
        return struct.unpack_from("<f", self.data, 0)[0]

    def u8(self) -> int:
        return self.data[0]

    def ascii(self) -> str:
        return self.data.rstrip(b"\x00").decode("ascii", "replace")

    def utf16(self) -> str:
        return self.data.rstrip(b"\x00").decode("utf-16-le", "replace")

    def u16_array(self) -> Sequence[int]:
        return struct.unpack_from("<%dH" % (len(self.data) // 2), self.data, 0)

    def u32_array(self) -> Sequence[int]:
        return struct.unpack_from("<%dI" % (len(self.data) // 4), self.data, 0)

    def f32_array(self) -> Sequence[float]:
        return struct.unpack_from("<%df" % (len(self.data) // 4), self.data, 0)

    def vec3_array(self) -> List[tuple]:
        n = len(self.data) // 12
        flat = struct.unpack_from("<%df" % (n * 3), self.data, 0)
        return [(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]) for i in range(n)]

    # -- navigation ---------------------------------------------------------
    def find(self, tag: int) -> Optional["Node"]:
        for c in self.children:
            if c.tag == tag:
                return c
        return None

    def find_all(self, tag: int) -> List["Node"]:
        return [c for c in self.children if c.tag == tag]

    def walk(self, depth: int = 0) -> Iterator[tuple]:
        yield depth, self
        for c in self.children:
            yield from c.walk(depth + 1)

    def describe(self, max_depth: int = 6) -> str:
        lines: List[str] = []
        for depth, n in self.walk():
            if depth > max_depth:
                continue
            extra = ""
            if n.is_leaf:
                extra = f" leaf[{len(n.data)}]"
                if len(n.data) == 4:
                    extra += f" u32={n.u32()} i32={n.i32()} f32={n.f32():.5g}"
            lines.append(f"{'  ' * depth}tag{n.tag:02x}{extra}")
        return "\n".join(lines)


def _read_size(buf: bytes, off: int) -> tuple:
    """Return (payload_length, header_length) for the size field at ``off``."""
    b = buf[off]
    if b % 2 == 0:
        return b // 2, 1
    v = struct.unpack_from("<I", buf, off)[0]
    if v == 0:
        raise ContainerError("zero size field at offset %d" % off)
    return (v - 1) // 2, 4


def parse_records(buf: bytes, start: int = 0, end: Optional[int] = None) -> List[Node]:
    """Parse a run of records.  Raises if the run does not end exactly at ``end``."""
    end = len(buf) if end is None else end
    nodes: List[Node] = []
    off = start
    while off < end:
        if buf[off:off + len(END_MARKER)] == END_MARKER:
            off += len(END_MARKER)
            continue
        tag = buf[off]
        try:
            length, hdr = _read_size(buf, off + 1)
        except struct.error as exc:
            raise ContainerError(f"truncated size field at {off}") from exc
        payload_start = off + 1 + hdr
        payload_end = payload_start + length
        if payload_end > end:
            raise ContainerError(
                f"record tag {tag} at {off} claims {length} bytes, only {end - payload_start} left"
            )
        payload = buf[payload_start:payload_end]
        node = Node(tag=tag, data=payload, start=off, size_byte=buf[off + 1])
        node.children = _maybe_branch(buf, payload_start, payload_end)
        nodes.append(node)
        off = payload_end
    if off != end:
        raise ContainerError(f"records overran: stopped at {off}, expected {end}")
    return nodes


def _maybe_branch(buf: bytes, start: int, end: int) -> List[Node]:
    """Parse as records when that succeeds; empty children means 'leaf'."""
    if end - start < 2:
        return []
    if end - start == 4:
        return []  # `tag 08 <u32>` scalar form
    try:
        return parse_records(buf, start, end)
    except ContainerError:
        return []


def parse_file(path: str) -> List[Node]:
    with open(path, "rb") as fh:
        return parse_records(fh.read())
