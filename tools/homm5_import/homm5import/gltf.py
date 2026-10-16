"""glTF 2.0 writer.

Unity 6 imports glTF through the official ``com.unity.cloud.gltfast`` package,
so exporting meshes as ``.glb`` keeps the importer free of any Unity-side
dependency: the Python pipeline writes standard binary glTF and Unity reads it
back as a real ``Mesh`` with normals, UVs and a skinned bone list.
"""

from __future__ import annotations

import json
import struct
from typing import Dict, List, Optional, Sequence

from .geometry import Geometry, MeshGroup

GLB_MAGIC = 0x46546C67
GLB_VERSION = 2
COMPONENT_FLOAT = 5126
COMPONENT_USHORT = 5123


def _pack_array(fmt: str, values: Sequence) -> bytes:
    flat: List[float] = []
    for v in values:
        flat.extend(v)
    return struct.pack("<%d%s" % (len(flat), fmt), *flat)


class _Buffer:
    def __init__(self) -> None:
        self.data = bytearray()

    def add(self, blob: bytes) -> int:
        while len(self.data) % 4:
            self.data.append(0)
        off = len(self.data)
        self.data += blob
        return off


def group_to_glb(group: MeshGroup, name: str = "mesh") -> bytes:
    """One material slice as a standalone binary glTF."""
    if not group.triangles:
        raise ValueError("group has no triangles")
    buf = _Buffer()
    accessors: List[dict] = []
    views: List[dict] = []

    def accessor(blob: bytes, comp: int, count: int, atype: str,
                 target: Optional[int] = None, minmax: bool = False) -> int:
        off = buf.add(blob)
        views.append({"buffer": 0, "byteOffset": off, "byteLength": len(blob)})
        if target:
            views[-1]["target"] = target
        acc: dict = {"bufferView": len(views) - 1, "componentType": comp,
                     "count": count, "type": atype}
        if minmax:
            flat = struct.unpack("<%df" % (len(blob) // 4), blob)
            n = len(flat) // 3
            acc["min"] = [min(flat[i * 3 + k] for i in range(n)) for k in range(3)]
            acc["max"] = [max(flat[i * 3 + k] for i in range(n)) for k in range(3)]
        accessors.append(acc)
        return len(accessors) - 1

    pos = accessor(_pack_array("f", group.positions), COMPONENT_FLOAT,
                   len(group.positions), "VEC3", target=34962, minmax=True)
    nor = accessor(_pack_array("f", group.normals), COMPONENT_FLOAT,
                   len(group.normals), "VEC3", target=34962)
    uv = accessor(_pack_array("f", group.uvs), COMPONENT_FLOAT,
                  len(group.uvs), "VEC2", target=34962)
    idx_blob = struct.pack("<%dH" % (len(group.triangles) * 3),
                           *[i for t in group.triangles for i in t])
    idx = accessor(idx_blob, COMPONENT_USHORT, len(group.triangles) * 3, "SCALAR", target=34963)

    primitive: dict = {"attributes": {"POSITION": pos, "NORMAL": nor, "TEXCOORD_0": uv},
                       "indices": idx, "mode": 4}
    doc = {
        "asset": {"version": "2.0", "generator": "homm5-import"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": name}],
        "meshes": [{"name": name, "primitives": [primitive]}],
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(buf.data)}],
    }
    return _write_glb(doc, bytes(buf.data))


def geometry_to_glb(geom: Geometry, name: Optional[str] = None) -> bytes:
    """Every mesh of a geometry as one glTF node each."""
    buf = _Buffer()
    accessors: List[dict] = []
    views: List[dict] = []
    meshes: List[dict] = []
    nodes: List[dict] = []

    def accessor(blob: bytes, comp: int, count: int, atype: str,
                 target: Optional[int] = None, minmax: bool = False) -> int:
        off = buf.add(blob)
        views.append({"buffer": 0, "byteOffset": off, "byteLength": len(blob)})
        if target:
            views[-1]["target"] = target
        acc: dict = {"bufferView": len(views) - 1, "componentType": comp,
                     "count": count, "type": atype}
        if minmax:
            flat = struct.unpack("<%df" % (len(blob) // 4), blob)
            n = len(flat) // 3
            acc["min"] = [min(flat[i * 3 + k] for i in range(n)) for k in range(3)]
            acc["max"] = [max(flat[i * 3 + k] for i in range(n)) for k in range(3)]
        accessors.append(acc)
        return len(accessors) - 1

    for mi, mesh in enumerate(geom.meshes):
        for gi, group in enumerate(mesh.groups):
            if not group.triangles:
                continue
            pos = accessor(_pack_array("f", group.positions), COMPONENT_FLOAT,
                           len(group.positions), "VEC3", target=34962, minmax=True)
            nor = accessor(_pack_array("f", group.normals), COMPONENT_FLOAT,
                           len(group.normals), "VEC3", target=34962)
            uv = accessor(_pack_array("f", group.uvs), COMPONENT_FLOAT,
                          len(group.uvs), "VEC2", target=34962)
            idx_blob = struct.pack("<%dH" % (len(group.triangles) * 3),
                                   *[i for t in group.triangles for i in t])
            idx = accessor(idx_blob, COMPONENT_USHORT, len(group.triangles) * 3,
                           "SCALAR", target=34963)
            mname = f"{name or geom.uid}_{mi}_{gi}"
            meshes.append({"name": mname, "primitives": [{
                "attributes": {"POSITION": pos, "NORMAL": nor, "TEXCOORD_0": uv},
                "indices": idx, "mode": 4}]})
            nodes.append({"mesh": len(meshes) - 1, "name": mname})

    if not meshes:
        raise ValueError("geometry %s has no drawable mesh" % geom.uid)

    doc = {
        "asset": {"version": "2.0", "generator": "homm5-import"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(buf.data)}],
    }
    return _write_glb(doc, bytes(buf.data))


def _write_glb(doc: dict, binblob: bytes) -> bytes:
    js = json.dumps(doc, separators=(",", ":")).encode("utf-8")
    while len(js) % 4:
        js += b" "
    while len(binblob) % 4:
        binblob += b"\x00"
    total = 12 + 8 + len(js) + 8 + len(binblob)
    out = bytearray()
    out += struct.pack("<III", GLB_MAGIC, GLB_VERSION, total)
    out += struct.pack("<II", len(js), 0x4E4F534A)
    out += js
    out += struct.pack("<II", len(binblob), 0x004E4942)
    out += binblob
    return bytes(out)
