"""Geometry meshes: ``data/bin/Geometries/<UID>``.

Layout (verified against the shipped assets, see tests/test_geometry.py):

    root tag01
      tag02  mesh list
        tag02 <u32>          declared mesh count  (== <NumMeshes> in the .xdb)
        tag01 x N            one block per mesh; a mesh using several materials
                             carries one inner group per material slice
          tag02 <u32>        material slices in this block
          tag01 x M          mesh node
            tag02 { count, f32[3]*n }   positions, unique XYZ
            tag03 { count, 20B*n }      per render vertex attributes
            tag04 { count, 24B*n }      skin binding (weights + bone indices)
            tag05 { count, u16*n }      render vertex -> position index (remap)
            tag06 { count, u16*n }      render vertex -> first vertex at position
            tag07 { count, u16*3n }     triangle list
            tag09 <u32>                 triangle count
            tag0a <f32>                 lod / screen coverage factor

The 20 byte attribute record is UV (2 x s16 / 2048), an unused 4 byte slot, then
normal / tangent / binormal as packed byte triples in **z, y, x** order (a
D3DCOLOR), decoded ``(b - 128) / 127``.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Sequence

from .container import Node, parse_records

ATTR_STRIDE = 20
SKIN_STRIDE = 24
UV_SCALE = 2048.0


@dataclass
class MeshGroup:
    """One material slice of one mesh."""

    positions: List[tuple] = field(default_factory=list)
    uvs: List[tuple] = field(default_factory=list)
    normals: List[tuple] = field(default_factory=list)
    tangents: List[tuple] = field(default_factory=list)
    triangles: List[tuple] = field(default_factory=list)
    bone_indices: List[tuple] = field(default_factory=list)
    bone_weights: List[tuple] = field(default_factory=list)

    @property
    def vertex_count(self) -> int:
        return len(self.uvs) or len(self.positions)

    @property
    def triangle_count(self) -> int:
        return len(self.triangles)


@dataclass
class GeometryMesh:
    name: str
    groups: List[MeshGroup] = field(default_factory=list)


@dataclass
class Geometry:
    uid: str
    meshes: List[GeometryMesh] = field(default_factory=list)
    declared_mesh_count: int = 0
    triangle_count_field: List[int] = field(default_factory=list)

    @property
    def total_triangles(self) -> int:
        return sum(g.triangle_count for m in self.meshes for g in m.groups)

    @property
    def total_vertices(self) -> int:
        return sum(g.vertex_count for m in self.meshes for g in m.groups)

    def bounds(self) -> Optional[tuple]:
        pts = [p for m in self.meshes for g in m.groups for p in g.positions]
        if not pts:
            return None
        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        return tuple(lo), tuple(hi)


def _count_and_leaf(node: Node) -> tuple:
    """Return (declared_count, payload_node) for a ``{tag01 count, tag02 data}`` pair."""
    count = None
    leaf = None
    for child in node.children:
        if child.tag == 1 and len(child.data) == 4 and not child.children:
            count = child.u32()
        elif child.tag == 2:
            leaf = child
    return count, leaf


def _unpack_normal(data: bytes, off: int) -> tuple:
    """Byte triple stored z, y, x (D3DCOLOR B, G, R order)."""
    z, y, x = data[off], data[off + 1], data[off + 2]
    return ((x - 128) / 127.0, (y - 128) / 127.0, (z - 128) / 127.0)


def decode_group(mesh_node: Node) -> Optional[MeshGroup]:
    """Decode one mesh node into a :class:`MeshGroup`."""
    pos_count, pos_leaf = _count_and_leaf(mesh_node.find(2)) if mesh_node.find(2) else (None, None)
    # Uniform float data can still parse as records, so validate by size rather
    # than by the leaf heuristic.
    if pos_leaf is None or len(pos_leaf.data) % 12 != 0 or not pos_leaf.data:
        return None
    positions = pos_leaf.vec3_array()
    if pos_count is not None and pos_count != len(positions):
        return None

    attr_count, attr_leaf = _count_and_leaf(mesh_node.find(3)) if mesh_node.find(3) else (None, None)
    uvs: List[tuple] = []
    normals: List[tuple] = []
    tangents: List[tuple] = []
    render_count = attr_count or 0
    if attr_leaf is not None and len(attr_leaf.data) % ATTR_STRIDE == 0 and attr_leaf.data:
        render_count = len(attr_leaf.data) // ATTR_STRIDE
        blob = attr_leaf.data
        for i in range(render_count):
            o = i * ATTR_STRIDE
            u = struct.unpack_from("<h", blob, o)[0] / UV_SCALE
            v = struct.unpack_from("<h", blob, o + 2)[0] / UV_SCALE
            uvs.append((u, v))
            normals.append(_unpack_normal(blob, o + 8))
            tangents.append(_unpack_normal(blob, o + 12))
    elif attr_leaf is not None:
        render_count = attr_count or 0

    _, remap_leaf = _count_and_leaf(mesh_node.find(5)) if mesh_node.find(5) else (None, None)
    remap: Sequence[int] = ()
    if remap_leaf is not None:
        arr = remap_leaf.u16_array()
        if arr and max(arr) < max(len(positions), 1):
            remap = arr

    _, skin_leaf = _count_and_leaf(mesh_node.find(4)) if mesh_node.find(4) else (None, None)
    bone_indices: List[tuple] = []
    bone_weights: List[tuple] = []
    if skin_leaf is not None and len(skin_leaf.data) % SKIN_STRIDE == 0 and skin_leaf.data:
        blob = skin_leaf.data
        for i in range(len(blob) // SKIN_STRIDE):
            o = i * SKIN_STRIDE
            bone_weights.append(struct.unpack_from("<4f", blob, o))
            # Bytes 16..19 are the SAME weights quantized to 0..255; the bone
            # indices are 20..23. Reading them at 16 returns the quantized
            # weights -- so a vertex weighted 1.0 came back bound to bone 255.
            bone_indices.append(struct.unpack_from("<4B", blob, o + 20))

    _, idx_leaf = _count_and_leaf(mesh_node.find(7)) if mesh_node.find(7) else (None, None)
    triangles: List[tuple] = []
    if idx_leaf is not None:
        flat = idx_leaf.u16_array()
        for i in range(len(flat) // 3):
            triangles.append((flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]))

    # Expand positions through the remap so triangles address a flat vertex list.
    out_pos: List[tuple] = []
    if remap:
        out_pos = [positions[r] if r < len(positions) else (0.0, 0.0, 0.0) for r in remap]
    elif render_count == len(positions):
        out_pos = list(positions)
    else:
        out_pos = list(positions)
        render_count = len(positions)

    # Skin arrays are indexed by POSITION, remap them onto render vertices.
    out_bi: List[tuple] = []
    out_bw: List[tuple] = []
    if bone_indices and remap:
        out_bi = [bone_indices[r] if r < len(bone_indices) else (0, 0, 0, 0) for r in remap]
        out_bw = [bone_weights[r] if r < len(bone_weights) else (0.0, 0.0, 0.0, 0.0) for r in remap]
    elif bone_indices:
        out_bi, out_bw = list(bone_indices), list(bone_weights)

    # Pad/trim attribute lists to the vertex count the triangles address.
    need = max([max(t) for t in triangles] + [len(out_pos) - 1]) + 1 if triangles else len(out_pos)
    while len(out_pos) < need:
        out_pos.append((0.0, 0.0, 0.0))
    while len(uvs) < need:
        uvs.append((0.0, 0.0))
    while len(normals) < need:
        normals.append((0.0, 0.0, 1.0))
    while len(tangents) < need:
        tangents.append((1.0, 0.0, 0.0))
    while len(out_bi) < need:
        out_bi.append((0, 0, 0, 0))
        out_bw.append((0.0, 0.0, 0.0, 0.0))

    return MeshGroup(
        positions=out_pos[:need],
        uvs=uvs[:need],
        normals=normals[:need],
        tangents=tangents[:need],
        triangles=triangles,
        bone_indices=out_bi[:need],
        bone_weights=out_bw[:need],
    )


def _decode_mesh_block(block: Node) -> List[MeshGroup]:
    """A block is ``{count, tag01 x M}`` -- M material groups."""
    groups: List[MeshGroup] = []
    for inner in block.find_all(1):
        g = decode_group(inner)
        if g is not None:
            groups.append(g)
    if groups:
        return groups
    # No inner wrapper: the block is itself a mesh node.
    g = decode_group(block)
    return [g] if g is not None else []


def decode(data: bytes, uid: str = "") -> Geometry:
    """Decode a whole geometry file."""
    nodes = parse_records(data)
    root = None
    for n in nodes:
        if n.tag == 1 and not n.is_leaf:
            root = n
            break
    if root is None:
        raise ValueError("no mesh list found in geometry %s" % uid)

    mesh_list = root.find(2)
    if mesh_list is None:
        raise ValueError("no tag02 mesh list in geometry %s" % uid)

    geom = Geometry(uid=uid)
    header = mesh_list.find(2)
    if header is not None and header.is_leaf and len(header.data) == 4:
        geom.declared_mesh_count = header.u32()

    for block in mesh_list.find_all(1):
        geom.meshes.append(GeometryMesh(name="", groups=_decode_mesh_block(block)))
    return geom


def decode_file(path: str) -> Geometry:
    import os

    with open(path, "rb") as fh:
        return decode(fh.read(), uid=os.path.basename(path))
