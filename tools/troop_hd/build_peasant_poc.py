#!/usr/bin/env python3
"""Build a localized Haven Peasant refinement prototype.

Only the BodyShape is altered: skin-weight masks round the hands, hood/cap,
and waist pouch; the separate PitchForkShape is passed through unchanged.
The diffuse atlas keeps its hand-painted palette and receives restrained,
visible cloth/leather/felt microtexture rather than a resize-only treatment.

Run from the repository root (ImageMagick's `convert` is required):
    python3 tools/troop_hd/build_peasant_poc.py --out Troops_HD_Patch
"""
from __future__ import annotations

import argparse
import json
import math
import shutil
import struct
import subprocess
import sys
import tempfile
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "homm5_import"))
from homm5import.dds import decode_dds, write_png  # noqa: E402
from homm5import.geometry import Geometry, MeshGroup, decode_file  # noqa: E402

UID = "B609BA27-66B2-4F26-B681-EF5F7F3A4601"
SOURCE_GEOMETRY = REPO / "data" / "bin" / "Geometries" / UID
SOURCE_TEXTURE_REL = (
    "Textures/auto-imported_______320/dev/Test/TMPcharacters/Peasant/Peasant.tga.dds"
)
SOURCE_TEXTURE = REPO / "data" / SOURCE_TEXTURE_REL
# The importer derives the runtime texture path from the source DDS path.
OUTPUT_TEXTURE_REL = "Textures/" + SOURCE_TEXTURE_REL + ".png"
SKELETON_UID = "419E6DD3-4C1B-460A-9BEF-4B918E33281E"
HAND_BONES = set(range(27, 34)) | set(range(41, 48))
WRIST_BONES = {26, 34, 39, 40}
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
    parts: Dict[str, List[float]] = {"hands": [], "hood": [], "pouch": []}
    combined: List[float] = []
    for position, joints, weights in zip(group.positions, group.bone_indices, group.bone_weights):
        hands = min(1.0, _bone_weight(joints, weights, HAND_BONES)
                    + 0.28 * _bone_weight(joints, weights, WRIST_BONES))
        hood = (_bone_weight(joints, weights, {HEAD_BONE})
                * _smoothstep(1.405, 1.505, float(position[2])))
        pouch = min(1.0, _bone_weight(joints, weights, POUCH_BONES))
        parts["hands"].append(hands)
        parts["hood"].append(hood)
        parts["pouch"].append(pouch)
        combined.append(max(0.92 * hands, 0.82 * hood, 0.95 * pouch))
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
        blend = min(0.78, strength * 0.86)
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
            blend = min(0.70, min(canonical_mask[ci], canonical_mask[cj]) * 0.82)
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


def _uv_mask(group: MeshGroup, vertex_weights: Sequence[float], width: int, height: int,
             z_cut: float | None = None) -> bytearray:
    """Rasterize skin-weight masks in UV space for localized texture detailing."""
    mask = bytearray(width * height)
    for tri in group.triangles:
        indices = tuple(int(i) for i in tri)
        weights = [float(vertex_weights[i]) for i in indices]
        if max(weights) < 0.2:
            continue
        if (z_cut is not None
                and sum(float(group.positions[i][2]) for i in indices) / 3.0 < z_cut):
            continue
        uv = [group.uvs[i] for i in indices]
        xy = [(float(u) * (width - 1), (1.0 - float(v)) * (height - 1)) for u, v in uv]
        min_x = max(0, int(min(point[0] for point in xy)))
        max_x = min(width - 1, int(max(point[0] for point in xy)) + 1)
        min_y = max(0, int(min(point[1] for point in xy)))
        max_y = min(height - 1, int(max(point[1] for point in xy)) + 1)
        (x0, y0), (x1, y1), (x2, y2) = xy
        denominator = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(denominator) < 1e-9:
            continue
        for py in range(min_y, max_y + 1):
            for px in range(min_x, max_x + 1):
                l0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denominator
                l1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denominator
                l2 = 1.0 - l0 - l1
                if l0 < -0.01 or l1 < -0.01 or l2 < -0.01:
                    continue
                value = max(0.0, min(1.0, l0 * weights[0] + l1 * weights[1] + l2 * weights[2]))
                offset = py * width + px
                quantized = int(value * 255.0)
                if quantized > mask[offset]:
                    mask[offset] = quantized
    return mask


def _smooth_mask(mask: bytearray, width: int, height: int, radius: int) -> bytearray:
    """Apply a tiny box blur so texture details do not hard-cut at UV island edges."""
    if radius <= 0:
        return mask
    temporary = bytearray(len(mask))
    output = bytearray(len(mask))
    for y in range(height):
        row = y * width
        for x in range(width):
            samples = [mask[row + xx] for xx in range(max(0, x - radius), min(width, x + radius + 1))]
            temporary[row + x] = sum(samples) // len(samples)
    for y in range(height):
        for x in range(width):
            samples = [temporary[yy * width + x]
                       for yy in range(max(0, y - radius), min(height, y + radius + 1))]
            output[y * width + x] = sum(samples) // len(samples)
    return output


def _hash_noise(x: int, y: int, seed: int = 0) -> float:
    value = (x * 0x9E3779B1 + y * 0x85EBCA77 + seed * 0xC2B2AE3D) & 0xFFFFFFFF
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    value ^= value >> 16
    return (value & 0xFFFF) / 32767.5 - 1.0


def enhance_texture(output_path: Path, source_width: int, source_height: int,
                    source_rgba: bytes, pouch_mask: bytearray, hood_mask: bytearray,
                    convert: str) -> dict:
    """Upscale the atlas and bake restrained woven/leather/felt material detail."""
    output_width, output_height = source_width * 2, source_height * 2
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="peasant-poc-") as temporary_dir:
        temp_dir = Path(temporary_dir)
        source_png = temp_dir / "source.png"
        resized_raw = temp_dir / "resized.rgba"
        write_png(str(source_png), source_width, source_height, source_rgba)
        subprocess.run(
            [convert, str(source_png), "-filter", "Lanczos", "-resize",
             f"{output_width}x{output_height}!", "-depth", "8", "RGBA:" + str(resized_raw)],
            check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
        )
        pixels = bytearray(resized_raw.read_bytes())
    if len(pixels) != output_width * output_height * 4:
        raise ValueError("ImageMagick returned an incomplete RGBA atlas")

    def mask_value(mask: bytearray, x: int, y: int) -> float:
        sx = min(source_width - 1, (x * source_width) // output_width)
        sy = min(source_height - 1, (y * source_height) // output_height)
        return mask[sy * source_width + sx] / 255.0

    for y in range(output_height):
        for x in range(output_width):
            offset = (y * output_width + x) * 4
            red, green, blue, alpha = pixels[offset:offset + 4]
            if alpha == 0:
                continue
            # Crossed warp/weft threads are visible on painted blue cloth but remain low contrast.
            blue_cloth = (max(0.0, min(1.0, (blue - red - 18) / 38.0))
                          * max(0.0, min(1.0, (blue - green - 5) / 45.0))
                          * max(0.0, min(1.0, (blue - 65) / 80.0)))
            if blue_cloth > 0.02:
                warp = 1.0 if x % 8 < 3 else -0.32
                weft = 1.0 if y % 8 < 3 else -0.32
                parity = 1.0 if ((x // 8 + y // 8) & 1) == 0 else -1.0
                weave = (warp + weft) * 0.55 + parity * 0.22
                grain = _hash_noise(x // 2, y // 2, 11) * 0.65
                delta = int(round(5.8 * (weave + grain) * blue_cloth))
                for channel in range(3):
                    pixels[offset + channel] = max(0, min(255, pixels[offset + channel] + delta))

            pouch = mask_value(pouch_mask, x, y)
            if pouch > 0.02:
                grain = _hash_noise(x // 3, y // 2, 23) * 3.5
                seam = ((1.0 if x % 41 < 2 else 0.0)
                        * _hash_noise(y // 3, 0, 7) * 2.2)
                delta = int(round((grain + seam) * pouch))
                for channel in range(3):
                    pixels[offset + channel] = max(0, min(255, pixels[offset + channel] + delta))

            hood = mask_value(hood_mask, x, y)
            if hood > 0.02:
                delta = int(round(_hash_noise(x // 3, y // 4, 47) * 2.8 * hood))
                for channel in range(3):
                    pixels[offset + channel] = max(0, min(255, pixels[offset + channel] + delta))

    write_png(str(output_path), output_width, output_height, bytes(pixels))
    return {
        "source_size": [source_width, source_height],
        "output_size": [output_width, output_height],
        "detail": {
            "blue_cloth": "low-contrast crossed warp/weft weave and fine grain",
            "hood": "weight-masked subtle felt grain",
            "waist_pouch": "weight-masked leather grain and restrained seam striation",
        },
        "png_bytes": output_path.stat().st_size,
    }


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
    triangles = 0
    vertices = 0
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            triangles += int(document["accessors"][primitive["indices"]]["count"]) // 3
            vertices += int(document["accessors"][primitive["attributes"]["POSITION"]]["count"])
    if triangles != expected_triangles:
        raise ValueError(f"GLB triangle count mismatch: {triangles} != {expected_triangles}")
    return {"triangles": triangles, "vertices": vertices, "mesh_slices": len(document.get("meshes", []))}


def _png_size(path: Path) -> Tuple[int, int]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or len(data) < 24:
        raise ValueError(f"invalid PNG: {path}")
    return struct.unpack_from(">II", data, 16)


def build(out_root: Path) -> dict:
    convert = shutil.which("convert")
    if not convert:
        raise RuntimeError("ImageMagick 'convert' is required to build the diffuse atlas")
    if not SOURCE_GEOMETRY.is_file() or not SOURCE_TEXTURE.is_file():
        raise FileNotFoundError("Peasant source geometry or texture is missing from data/")

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
    if fork_signature != (tuple(fork.positions), tuple(fork.uvs), tuple(fork.normals),
                          tuple(fork.triangles), tuple(fork.bone_indices), tuple(fork.bone_weights)):
        raise AssertionError("the separate PitchForkShape mesh was modified")

    width, height, rgba = decode_dds(SOURCE_TEXTURE.read_bytes())
    pouch_weights = [_bone_weight(joints, weights, POUCH_BONES)
                     for joints, weights in zip(body.bone_indices, body.bone_weights)]
    hood_weights = [(_bone_weight(joints, weights, {HEAD_BONE})
                     * _smoothstep(1.40, 1.50, float(position[2])))
                    for position, joints, weights
                    in zip(body.positions, body.bone_indices, body.bone_weights)]
    pouch_mask = _smooth_mask(_uv_mask(body, pouch_weights, width, height), width, height, 2)
    hood_mask = _smooth_mask(_uv_mask(body, hood_weights, width, height, z_cut=1.43),
                             width, height, 1)

    model_path = out_root / "HoMM5" / "Meshes" / f"{UID}.glb"
    texture_path = out_root / "HoMM5" / "Textures" / OUTPUT_TEXTURE_REL
    model_path.parent.mkdir(parents=True, exist_ok=True)
    blob = write_glb(geometry, UID)
    glb_stats = validate_glb(blob, geometry.total_triangles)
    model_path.write_bytes(blob)
    texture_stats = enhance_texture(texture_path, width, height, bytes(rgba),
                                    pouch_mask, hood_mask, convert)
    if _png_size(texture_path) != tuple(texture_stats["output_size"]):
        raise ValueError("texture dimensions do not match the generated atlas")

    manifest = {
        "format": "HoMM5 Unity StreamingAssets overlay",
        "prototype": "Haven Peasant",
        "source_geometry_uid": UID,
        "source_skeleton_uid": SKELETON_UID,
        "source_geometry": f"data/bin/Geometries/{UID}",
        "source_texture_dds": SOURCE_TEXTURE_REL,
        "body_shape": geometry_stats,
        "pitchfork_shape": {
            "vertices": len(fork.positions),
            "triangles": len(fork.triangles),
            "changed": False,
        },
        "glb": {"path": f"Meshes/{UID}.glb", **glb_stats, "bytes": len(blob)},
        "texture": {"path": OUTPUT_TEXTURE_REL, **texture_stats},
        "method": {
            "geometry": "skin-weight-local Loop-style subdivision/relaxation of hands, hood top and waist pouch; other body regions are not globally subdivided",
            "material": "painted atlas retained with restrained, visible cloth weave and weight-masked pouch/hood grain baked into diffuse RGB",
            "sharp_parts": "PitchForkShape is a separate mesh block and is unchanged",
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
