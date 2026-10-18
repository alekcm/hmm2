#!/usr/bin/env python3
"""Build a localized Haven Peasant refinement prototype.

Only the BodyShape is altered: skin-weight masks round the hands, hood/cap,
legs/shins, and waist pouch; the separate PitchForkShape is passed through
unchanged. Existing material files are left untouched for manual editing.

Run from the repository root:
    python3 tools/troop_hd/build_peasant_poc.py --out Troops_HD_Patch
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "homm5_import"))
from homm5import.geometry import Geometry, MeshGroup, decode_file  # noqa: E402

UID = "B609BA27-66B2-4F26-B681-EF5F7F3A4601"
SOURCE_GEOMETRY = REPO / "data" / "bin" / "Geometries" / UID
SKELETON_UID = "419E6DD3-4C1B-460A-9BEF-4B918E33281E"
HAND_BONES = set(range(27, 34)) | set(range(41, 48))
WRIST_BONES = {26, 34, 39, 40}
LEG_BONES = {3, 4, 7, 8}       # left/right thigh and lower-leg (shin)
ANKLE_BONES = {5, 9}           # low influence only near the boot cuff
POUCH_BONES = {14, 15}
HEAD_BONE = 17

GLB_MAGIC = 0x46546C67
GLB_VERSION = 2
COMPONENT_UNSIGNED_BYTE = 5121
COMPONENT_UNSIGNED_SHORT = 5123
COMPONENT_UNSIGNED_INT = 5125
COMPONENT_FLOAT = 5126


def _normalize(v: Sequence[float], fallback: Sequence[float] = (0.0, 0.0, 1.0)
               ) -> Tuple[float, float, float]:
    x, y, z = float(v[0]), float(v[1]), float(v[2])
    norm2 = x * x + y * y + z * z
    if norm2 <= 1e-16:
        return float(fallback[0]), float(fallback[1]), float(fallback[2])
    inv = 1.0 / math.sqrt(norm2)
    return x * inv, y * inv, z * inv


def _midpoint_skin(group: MeshGroup, i: int, j: int
                   ) -> Tuple[Tuple[int, int, int, int], Tuple[float, float, float, float]]:
    by_bone: Dict[int, float] = defaultdict(float)
    for vertex in (i, j):
        for bone, weight in zip(group.bone_indices[vertex], group.bone_weights[vertex]):
            if weight > 0.0:
                by_bone[int(bone)] += float(weight) * 0.5
    strongest = sorted(by_bone.items(), key=lambda item: (-item[1], item[0]))[:4]
    total = sum(weight for _, weight in strongest)
    if total <= 1e-12:
        return (0, 0, 0, 0), (0.0, 0.0, 0.0, 0.0)
    joints = [bone for bone, _ in strongest]
    weights = [weight / total for _, weight in strongest]
    joints.extend([0] * (4 - len(joints)))
    weights.extend([0.0] * (4 - len(weights)))
    return tuple(joints), tuple(weights)  # type: ignore[return-value]


def _bone_weight(indices: Sequence[int], weights: Sequence[float], bones: set[int]) -> float:
    return sum(float(weight) for bone, weight in zip(indices, weights)
               if int(bone) in bones and float(weight) > 0.0)


def _smoothstep(a: float, b: float, x: float) -> float:
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _part_masks(group: MeshGroup) -> Tuple[List[float], Dict[str, List[float]]]:
    parts: Dict[str, List[float]] = {
        "hands": [], "hood": [], "legs_and_shins": [], "pouch": [],
    }
    combined: List[float] = []
    for position, joints, weights in zip(group.positions, group.bone_indices, group.bone_weights):
        height = float(position[2])
        hands = min(1.0, _bone_weight(joints, weights, HAND_BONES)
                    + 0.28 * _bone_weight(joints, weights, WRIST_BONES))
        hood = (_bone_weight(joints, weights, {HEAD_BONE})
                * _smoothstep(1.405, 1.505, height))
        # Round thigh/shin sections; use foot weights only to soften the ankle
        # transition, fading them out before the angular boot toe/sole.
        legs = min(1.0, _bone_weight(joints, weights, LEG_BONES)
                   + 0.30 * _bone_weight(joints, weights, ANKLE_BONES)
                   * _smoothstep(0.035, 0.19, height))
        pouch = min(1.0, _bone_weight(joints, weights, POUCH_BONES))
        parts["hands"].append(hands)
        parts["hood"].append(hood)
        parts["legs_and_shins"].append(legs)
        parts["pouch"].append(pouch)
        combined.append(max(0.92 * hands, 0.82 * hood, 0.96 * legs, 0.95 * pouch))
    return combined, parts


def _canonical_vertices(positions: Sequence[Sequence[float]], masks: Sequence[float]
                        ) -> Tuple[List[tuple], List[float], List[int], List[List[int]]]:
    """Weld coincident render vertices for geometry decisions, preserving UV seams."""
    canonical: Dict[tuple, int] = {}
    coordinates: List[tuple] = []
    strengths: List[float] = []
    vertex_to_canonical: List[int] = []
    members: List[List[int]] = []
    for index, (position, strength) in enumerate(zip(positions, masks)):
        key = tuple(round(float(value), 6) for value in position)
        ci = canonical.get(key)
        if ci is None:
            ci = len(coordinates)
            canonical[key] = ci
            coordinates.append(tuple(float(value) for value in position))
            strengths.append(float(strength))
            members.append([])
        else:
            strengths[ci] = max(strengths[ci], float(strength))
        vertex_to_canonical.append(ci)
        members[ci].append(index)
    return coordinates, strengths, vertex_to_canonical, members


def refine_body(group: MeshGroup) -> Tuple[MeshGroup, dict]:
    """Subdivide and relax only skin-weighted soft parts of Peasant BodyShape."""
    vertex_count = len(group.positions)
    if not group.triangles or vertex_count == 0:
        raise ValueError("Peasant BodyShape is empty")
    if len(group.bone_indices) != vertex_count or len(group.bone_weights) != vertex_count:
        raise ValueError("Peasant BodyShape is missing its skin weights")
    if len(group.uvs) < vertex_count:
        raise ValueError("Peasant BodyShape is missing UV coordinates")
    for tri in group.triangles:
        if len(tri) != 3 or min(tri) < 0 or max(tri) >= vertex_count:
            raise ValueError("Peasant BodyShape contains an invalid triangle")

    masks, part_masks = _part_masks(group)
    positions = [tuple(float(v) for v in p[:3]) for p in group.positions]
    uvs = [tuple(float(v) for v in uv[:2]) for uv in group.uvs[:vertex_count]]
    normals = [tuple(float(v) for v in n[:3]) for n in group.normals[:vertex_count]]
    while len(normals) < vertex_count:
        normals.append((0.0, 0.0, 1.0))

    canonical_pos, canonical_mask, vertex_to_canonical, members = _canonical_vertices(
        positions, masks)
    edge_faces: Dict[Tuple[int, int], List[int]] = {}
    neighbors: List[set[int]] = [set() for _ in canonical_pos]
    for tri in group.triangles:
        ctri = [vertex_to_canonical[int(i)] for i in tri]
        if len(set(ctri)) < 3:
            continue
        for q in range(3):
            a, b, opposite = ctri[q], ctri[(q + 1) % 3], ctri[(q + 2) % 3]
            key = (a, b) if a < b else (b, a)
            edge_faces.setdefault(key, []).append(opposite)
            neighbors[a].add(b)
            neighbors[b].add(a)

    # Loop-style relaxation shifts existing vertices toward a curved surface.
    # It is applied only to masked vertices and blended at the mask boundary.
    relaxed_positions = list(positions)
    for ci, center in enumerate(canonical_pos):
        strength = canonical_mask[ci]
        soft_neighbors = [j for j in neighbors[ci] if canonical_mask[j] > 0.25]
        if strength < 0.18 or len(soft_neighbors) < 3:
            continue
        count = len(soft_neighbors)
        beta = 3.0 / 16.0 if count == 3 else 3.0 / (8.0 * count)
        loop_position = tuple(
            (1.0 - count * beta) * center[axis]
            + beta * sum(canonical_pos[j][axis] for j in soft_neighbors)
            for axis in range(3)
        )
        # Approximately double the previous relaxation amount; clamp at a full
        # Loop step to avoid overshooting at fully weighted vertices.
        blend = min(1.0, strength * 1.72)
        target = tuple(center[axis] + (loop_position[axis] - center[axis]) * blend
                       for axis in range(3))
        for original_index in members[ci]:
            relaxed_positions[original_index] = target

    new_positions = list(relaxed_positions)
    new_uvs = list(uvs)
    new_normals = list(normals)
    new_joints = list(group.bone_indices)
    new_weights = list(group.bone_weights)
    new_masks = list(masks)
    edge_cache: Dict[Tuple[int, int], int] = {}

    def edge_vertex(i: int, j: int) -> int:
        key = (i, j) if i < j else (j, i)
        if key in edge_cache:
            return edge_cache[key]
        ci, cj = vertex_to_canonical[i], vertex_to_canonical[j]
        canonical_edge = (ci, cj) if ci < cj else (cj, ci)
        p0, p1 = canonical_pos[ci], canonical_pos[cj]
        midpoint = tuple((p0[axis] + p1[axis]) * 0.5 for axis in range(3))
        opposite = edge_faces.get(canonical_edge, [])
        if len(opposite) == 2 and min(canonical_mask[ci], canonical_mask[cj]) > 0.32:
            loop = tuple(
                0.375 * (p0[axis] + p1[axis])
                + 0.125 * (canonical_pos[opposite[0]][axis]
                            + canonical_pos[opposite[1]][axis])
                for axis in range(3)
            )
            blend = min(1.0, min(canonical_mask[ci], canonical_mask[cj]) * 1.64)
            position = tuple(midpoint[axis] + (loop[axis] - midpoint[axis]) * blend
                             for axis in range(3))
        else:
            position = midpoint

        new_index = len(new_positions)
        new_positions.append(position)
        new_uvs.append(tuple((uvs[i][axis] + uvs[j][axis]) * 0.5 for axis in range(2)))
        new_normals.append(_normalize(tuple(normals[i][axis] + normals[j][axis]
                                             for axis in range(3)), normals[i]))
        joints, weights = _midpoint_skin(group, i, j)
        new_joints.append(joints)
        new_weights.append(weights)
        new_masks.append((masks[i] + masks[j]) * 0.5)
        edge_cache[key] = new_index
        return new_index

    triangles: List[Tuple[int, int, int]] = []
    face_edge_splits = 0
    for source_tri in group.triangles:
        a, b, c = map(int, source_tri)
        oriented_edges = ((a, b), (b, c), (c, a))
        split_vertices: List[int | None] = []
        for u, v in oriented_edges:
            edge_strength = max(canonical_mask[vertex_to_canonical[u]],
                                canonical_mask[vertex_to_canonical[v]])
            split_vertices.append(edge_vertex(u, v) if edge_strength > 0.17 else None)
        face_edge_splits += sum(index is not None for index in split_vertices)
        pieces = [(a, b, c)]
        for (u, v), midpoint in zip(oriented_edges, split_vertices):
            if midpoint is None:
                continue
            updated: List[Tuple[int, int, int]] = []
            for face in pieces:
                split_done = False
                for q in range(3):
                    x, y = face[q], face[(q + 1) % 3]
                    if x == u and y == v:
                        opposite = face[(q + 2) % 3]
                        updated.extend(((x, midpoint, opposite), (midpoint, y, opposite)))
                        split_done = True
                        break
                if not split_done:
                    updated.append(face)
            pieces = updated
        triangles.extend(pieces)

    # Recompute normals only in the refined soft zones; retain authored shading elsewhere.
    normal_accum = [[0.0, 0.0, 0.0] for _ in new_positions]
    for tri in triangles:
        p0, p1, p2 = (new_positions[i] for i in tri)
        ab = tuple(p1[k] - p0[k] for k in range(3))
        ac = tuple(p2[k] - p0[k] for k in range(3))
        face_normal = (ab[1] * ac[2] - ab[2] * ac[1],
                       ab[2] * ac[0] - ab[0] * ac[2],
                       ab[0] * ac[1] - ab[1] * ac[0])
        for i in tri:
            for axis in range(3):
                normal_accum[i][axis] += face_normal[axis]
    for i, strength in enumerate(new_masks):
        if strength > 0.24:
            new_normals[i] = _normalize(normal_accum[i], new_normals[i])

    refined = MeshGroup(
        positions=new_positions,
        uvs=new_uvs,
        normals=new_normals,
        tangents=[],
        triangles=triangles,
        bone_indices=new_joints,
        bone_weights=new_weights,
    )
    stats = {
        "source_vertices": vertex_count,
        "refined_vertices": len(refined.positions),
        "source_triangles": len(group.triangles),
        "refined_triangles": len(refined.triangles),
        "face_edge_splits": face_edge_splits,
        "moved_vertices": sum(math.dist(positions[i], relaxed_positions[i]) > 1e-7
                               for i in range(vertex_count)),
        "max_vertex_displacement": max(
            (math.dist(positions[i], relaxed_positions[i]) for i in range(vertex_count)),
            default=0.0,
        ),
        "parts": {
            name: {
                "vertices_weighted_over_0_2": sum(value > 0.2 for value in values),
                "faces_mean_weight_over_0_2": sum(
                    sum(values[int(i)] for i in tri) / 3.0 > 0.2 for tri in group.triangles
                ),
            }
            for name, values in part_masks.items()
        },
    }
    return refined, stats


def _pack_floats(values: Iterable[float]) -> bytes:
    flat = list(values)
    return struct.pack("<%df" % len(flat), *flat) if flat else b""


def write_glb(geometry: Geometry, uid: str) -> bytes:
    """Serialize the model as glTF 2.0 GLB for the Unity asset importer."""
    binary = bytearray()
    views: List[dict] = []
    accessors: List[dict] = []
    meshes: List[dict] = []
    nodes: List[dict] = []

    def add_accessor(blob: bytes, component: int, count: int, kind: str, target: int,
                     bounds: bool = False) -> int:
        while len(binary) % 4:
            binary.append(0)
        offset = len(binary)
        binary.extend(blob)
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(blob), "target": target})
        accessor = {"bufferView": len(views) - 1, "componentType": component,
                    "count": count, "type": kind}
        if bounds and count:
            flat = struct.unpack("<%df" % (len(blob) // 4), blob)
            accessor["min"] = [min(flat[i * 3 + axis] for i in range(count)) for axis in range(3)]
            accessor["max"] = [max(flat[i * 3 + axis] for i in range(count)) for axis in range(3)]
        accessors.append(accessor)
        return len(accessors) - 1

    for mesh_index, source_mesh in enumerate(geometry.meshes):
        for group_index, group in enumerate(source_mesh.groups):
            if not group.triangles or not group.positions:
                continue
            count = len(group.positions)
            positions = [tuple(float(v) for v in p[:3]) for p in group.positions]
            normals = [tuple(float(v) for v in n[:3]) for n in group.normals[:count]]
            uvs = [tuple(float(v) for v in uv[:2]) for uv in group.uvs[:count]]
            normals.extend([(0.0, 0.0, 1.0)] * (count - len(normals)))
            uvs.extend([(0.0, 0.0)] * (count - len(uvs)))
            position_accessor = add_accessor(_pack_floats(v for p in positions for v in p),
                                             COMPONENT_FLOAT, count, "VEC3", 34962, bounds=True)
            normal_accessor = add_accessor(_pack_floats(v for n in normals for v in n),
                                           COMPONENT_FLOAT, count, "VEC3", 34962)
            uv_accessor = add_accessor(_pack_floats(v for uv in uvs for v in uv),
                                       COMPONENT_FLOAT, count, "VEC2", 34962)
            flat_indices = [int(index) for tri in group.triangles for index in tri]
            if max(flat_indices) <= 0xFFFF:
                index_data = struct.pack("<%dH" % len(flat_indices), *flat_indices)
                index_component = COMPONENT_UNSIGNED_SHORT
            else:
                index_data = struct.pack("<%dI" % len(flat_indices), *flat_indices)
                index_component = COMPONENT_UNSIGNED_INT
            index_accessor = add_accessor(index_data, index_component, len(flat_indices),
                                           "SCALAR", 34963)
            attributes = {"POSITION": position_accessor, "NORMAL": normal_accessor,
                          "TEXCOORD_0": uv_accessor}
            if len(group.bone_indices) == count and len(group.bone_weights) == count:
                joints = [tuple(max(0, min(255, int(value))) for value in row[:4])
                          for row in group.bone_indices]
                weights = [tuple(float(value) for value in row[:4])
                           for row in group.bone_weights]
                joint_data = struct.pack("<%dB" % (count * 4),
                                         *(value for row in joints for value in row))
                weight_data = _pack_floats(value for row in weights for value in row)
                attributes["JOINTS_0"] = add_accessor(
                    joint_data, COMPONENT_UNSIGNED_BYTE, count, "VEC4", 34962)
                attributes["WEIGHTS_0"] = add_accessor(
                    weight_data, COMPONENT_FLOAT, count, "VEC4", 34962)
            name = f"{uid}_{mesh_index}_{group_index}"
            meshes.append({"name": name, "primitives": [{
                "attributes": attributes, "indices": index_accessor, "mode": 4,
            }]})
            nodes.append({"mesh": len(meshes) - 1, "name": name})

    document = {
        "asset": {"version": "2.0", "generator": "homm5-peasant-poc"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(binary)}],
    }
    json_chunk = json.dumps(document, separators=(",", ":")).encode("utf-8")
    while len(json_chunk) % 4:
        json_chunk += b" "
    while len(binary) % 4:
        binary.append(0)
    total_size = 12 + 8 + len(json_chunk) + 8 + len(binary)
    output = bytearray(struct.pack("<III", GLB_MAGIC, GLB_VERSION, total_size))
    output.extend(struct.pack("<II", len(json_chunk), 0x4E4F534A))
    output.extend(json_chunk)
    output.extend(struct.pack("<II", len(binary), 0x004E4942))
    output.extend(binary)
    return bytes(output)


def validate_glb(blob: bytes, expected_triangles: int) -> dict:
    if len(blob) < 20:
        raise ValueError("GLB is too short")
    magic, version, total_length = struct.unpack_from("<III", blob, 0)
    if magic != GLB_MAGIC or version != GLB_VERSION or total_length != len(blob):
        raise ValueError("invalid GLB header")
    json_length, chunk_type = struct.unpack_from("<II", blob, 12)
    if chunk_type != 0x4E4F534A:
        raise ValueError("GLB is missing its JSON chunk")
    document = json.loads(blob[20:20 + json_length].decode("utf-8"))
    bin_header = 20 + json_length
    if bin_header + 8 > len(blob):
        raise ValueError("GLB is missing its binary chunk")
    bin_length, bin_type = struct.unpack_from("<II", blob, bin_header)
    if bin_type != 0x004E4942 or bin_header + 8 + bin_length > len(blob):
        raise ValueError("invalid GLB binary chunk")
    binary = blob[bin_header + 8:bin_header + 8 + bin_length]
    triangles = 0
    vertices = 0
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            index_accessor = document["accessors"][primitive["indices"]]
            position_accessor = document["accessors"][primitive["attributes"]["POSITION"]]
            index_count = int(index_accessor["count"])
            vertex_count = int(position_accessor["count"])
            if index_count % 3:
                raise ValueError("GLB triangle index count is not divisible by three")
            component = int(index_accessor["componentType"])
            fmt = {COMPONENT_UNSIGNED_SHORT: "H", COMPONENT_UNSIGNED_INT: "I"}.get(component)
            if fmt is None:
                raise ValueError("GLB indices must use an unsigned integer type")
            view = document["bufferViews"][index_accessor["bufferView"]]
            offset = int(view.get("byteOffset", 0)) + int(index_accessor.get("byteOffset", 0))
            indices = struct.unpack_from(f"<{index_count}{fmt}", binary, offset)
            if indices and max(indices) >= vertex_count:
                raise ValueError("GLB index references a vertex outside its primitive")
            triangles += index_count // 3
            vertices += vertex_count
    if triangles != expected_triangles:
        raise ValueError(f"GLB triangle count mismatch: {triangles} != {expected_triangles}")
    return {"triangles": triangles, "vertices": vertices, "mesh_slices": len(document.get("meshes", []))}


def build(out_root: Path) -> dict:
    if not SOURCE_GEOMETRY.is_file():
        raise FileNotFoundError(f"Peasant source geometry is missing: {SOURCE_GEOMETRY}")

    geometry = decode_file(str(SOURCE_GEOMETRY))
    if (len(geometry.meshes) != 2
            or len(geometry.meshes[0].groups) != 1
            or len(geometry.meshes[1].groups) != 1):
        raise ValueError("expected one material slice each for BodyShape and PitchForkShape")
    body = geometry.meshes[0].groups[0]
    fork = geometry.meshes[1].groups[0]
    fork_signature = (tuple(fork.positions), tuple(fork.uvs), tuple(fork.normals),
                      tuple(fork.triangles), tuple(fork.bone_indices), tuple(fork.bone_weights))

    refined_body, geometry_stats = refine_body(body)
    geometry.meshes[0].groups[0] = refined_body
    current_fork = geometry.meshes[1].groups[0]
    current_fork_signature = (
        tuple(current_fork.positions), tuple(current_fork.uvs), tuple(current_fork.normals),
        tuple(current_fork.triangles), tuple(current_fork.bone_indices), tuple(current_fork.bone_weights),
    )
    if fork_signature != current_fork_signature:
        raise AssertionError("the separate PitchForkShape mesh was modified")

    model_path = out_root / "HoMM5" / "Meshes" / f"{UID}.glb"
    model_path.parent.mkdir(parents=True, exist_ok=True)
    blob = write_glb(geometry, UID)
    glb_stats = validate_glb(blob, geometry.total_triangles)
    model_path.write_bytes(blob)

    # Texture assets are deliberately not processed by this prototype. Count
    # any existing PNGs but leave their bytes and directories untouched.
    texture_root = out_root / "HoMM5" / "Textures"
    texture_count = sum(1 for path in texture_root.rglob("*.png")) if texture_root.is_dir() else 0
    manifest = {
        "format": "HoMM5 Unity StreamingAssets overlay",
        "prototype": "Haven Peasant",
        "source_geometry_uid": UID,
        "source_skeleton_uid": SKELETON_UID,
        "source_geometry": f"data/bin/Geometries/{UID}",
        "body_shape": geometry_stats,
        "pitchfork_shape": {
            "vertices": len(fork.positions),
            "triangles": len(fork.triangles),
            "changed": False,
        },
        "glb": {"path": f"Meshes/{UID}.glb", **glb_stats, "bytes": len(blob)},
        "texture_assets": {
            "count": texture_count,
            "policy": "preserved unchanged for manual editing; this builder does not modify textures",
            "restored_from_commit": "ec289b38d",
        },
        "method": {
            "geometry": "skin-weight-guided local Loop refinement of hands, hood, thighs/shins, and waist pouch",
            "strength": "relaxation multipliers doubled from the prior prototype; full-weight vertices cap at one Loop step to avoid overshoot",
            "sharp_parts": "PitchForkShape and boot toes/soles are preserved",
        },
    }
    out_root.mkdir(parents=True, exist_ok=True)
    (out_root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n",
                                              encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=REPO / "Troops_HD_Patch",
                        help="output folder containing the copy-ready HoMM5 overlay")
    args = parser.parse_args()
    result = build(args.out.resolve())
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
