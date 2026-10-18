#!/usr/bin/env python3
"""Build a copy-ready HD troop asset overlay for Unity StreamingAssets.

The source .xdb/.bin/.dds files are kept untouched.  The script writes only the
replacement GLBs and texture PNGs that belong to playable creature models.

Usage (from the repository root, ImageMagick's `convert` must be installed):
    python tools/troop_hd/build_patch.py
    python tools/troop_hd/build_patch.py --out /path/to/Troops_HD_Patch

The resulting <out>/HoMM5 folder is an overlay for
Assets/StreamingAssets/HoMM5 -- merge it into the existing imported data folder.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

REPO = Path(__file__).resolve().parents[2]
IMPORTER = REPO / "tools" / "homm5_import"
sys.path.insert(0, str(IMPORTER))

from homm5import.dds import DDSError, decode_dds, write_png  # noqa: E402
from homm5import.geometry import Geometry, MeshGroup, decode_file  # noqa: E402

GLB_MAGIC = 0x46546C67
GLB_VERSION = 2
COMPONENT_UNSIGNED_BYTE = 5121
COMPONENT_UNSIGNED_SHORT = 5123
COMPONENT_UNSIGNED_INT = 5125
COMPONENT_FLOAT = 5126


def _normal_path(value: str) -> str:
    return value.replace("\\", "/").lstrip("/")


def safe_relative_path(rel: str) -> str:
    """Match the importer's path normalization used by Textures/_manifest.json."""
    unsafe = set('?<>:*|"')
    parts = []
    for part in _normal_path(rel).split("/"):
        part = "".join("_" if c in unsafe else c for c in part).strip(" ")
        if part.startswith("."):
            part = "_" + part[1:]
        parts.append(part or "_")
    return "/".join(parts)


class GameData:
    """Case-insensitive file resolver with the addon folder as an overlay."""

    def __init__(self, repo: Path):
        self.roots = [repo / "a2p1-data", repo / "data"]
        self.roots = [p for p in self.roots if p.is_dir()]
        if not self.roots:
            raise FileNotFoundError("neither data/ nor a2p1-data/ exists")
        self.file_indexes: Dict[Path, Dict[str, Path]] = {}
        for root in self.roots:
            index: Dict[str, Path] = {}
            for base, dirs, files in os.walk(root):
                dirs.sort()
                for name in files:
                    path = Path(base) / name
                    rel = path.relative_to(root).as_posix()
                    index.setdefault(rel.casefold(), path)
            self.file_indexes[root] = index

    def resolve(self, href: Optional[str], base_file: Optional[Path] = None) -> Optional[Path]:
        """Resolve an XDB/file href, including relative links and addon overrides."""
        if not href:
            return None
        raw = href.split("#", 1)[0].strip()
        if not raw or raw.startswith("#"):
            return None
        raw = raw.replace("\\", "/")

        # Relative hrefs in old XDBs are relative to the file that contains them.
        if not raw.startswith("/") and base_file is not None:
            candidate = (base_file.parent / raw).resolve()
            if candidate.is_file():
                # If a higher-priority addon file has the same relative path,
                # it must shadow the base-game copy.
                for root in self.roots:
                    try:
                        rel = candidate.relative_to(root.resolve()).as_posix()
                    except ValueError:
                        continue
                    preferred = self.file_indexes[root].get(rel.casefold())
                    if preferred is not None:
                        return preferred
                return candidate
            # A case-insensitive relative lookup, still relative to the base.
            for root in self.roots:
                try:
                    rel = candidate.relative_to(root.resolve()).as_posix()
                except ValueError:
                    continue
                found = self.file_indexes[root].get(rel.casefold())
                if found is not None:
                    return found

        rel = _normal_path(raw)
        for root in self.roots:
            found = self.file_indexes[root].get(rel.casefold())
            if found is not None:
                return found
            direct = root / rel
            if direct.is_file():
                return direct
        return None

    def prefer_overlay(self, path: Path) -> Path:
        """Return the highest-priority copy for the same relative asset path."""
        for root in self.roots:
            try:
                rel = path.resolve().relative_to(root.resolve()).as_posix()
            except ValueError:
                continue
            for candidate_root in self.roots:
                candidate = self.file_indexes[candidate_root].get(rel.casefold())
                if candidate is not None:
                    return candidate
            return path
        return path

    def rel_path(self, path: Path) -> str:
        path = self.prefer_overlay(path).resolve()
        for root in self.roots:
            try:
                return path.relative_to(root.resolve()).as_posix()
            except ValueError:
                continue
        raise ValueError("asset is outside both game data roots: " + str(path))


def _parse(path: Path) -> ET.Element:
    return ET.parse(path).getroot()


def _resolved_record(data: GameData, node: Optional[ET.Element], base: Path
                     ) -> Tuple[Optional[ET.Element], Optional[Path]]:
    """Follow an XDB href, or unwrap an inline record if one is present."""
    if node is None:
        return None, None
    href = node.get("href")
    if href:
        if href.startswith("#n:inline") or href.startswith("#inline"):
            # Most inline records are a child with the same type name.
            for child in list(node):
                if child.tag not in ("uid",):
                    return child, base
            return node, base
        target = data.resolve(href, base)
        if target is None:
            return None, None
        return _parse(target), target
    # Material/Geometry wrappers often contain the actual inline object.
    for child in list(node):
        if child.tag in ("Material", "Geometry", "Texture"):
            return child, base
    return node, base


def _extract_model(data: GameData, creature_file: Path, creature: ET.Element
                   ) -> Tuple[Optional[Path], Optional[str]]:
    visual, visual_file = _resolved_record(data, creature.find("Visual"), creature_file)
    if visual is None or visual_file is None:
        return None, None
    character, character_file = _resolved_record(data, visual.find("AnimCharacter"), visual_file)
    if character is None or character_file is None:
        return None, None
    model, model_file = _resolved_record(data, character.find("Model"), character_file)
    if model is None or model_file is None:
        return None, None

    geometry_ref = model.find("Geometry")
    if geometry_ref is None:
        return model_file, None
    geometry, _geometry_file = _resolved_record(data, geometry_ref, model_file)
    if geometry is None:
        return model_file, None
    uid = geometry.findtext("uid")
    if not uid:
        # Inline model Geometry contains a nested <Geometry> record.
        nested = geometry.find("Geometry")
        if nested is not None:
            uid = nested.findtext("uid")
    return model_file, uid.strip() if uid else None


def _texture_dds(data: GameData, texture_slot: Optional[ET.Element], base: Path) -> Optional[Path]:
    if texture_slot is None:
        return None
    texture, texture_file = _resolved_record(data, texture_slot, base)
    if texture is None:
        return None

    dest = texture.find("DestName")
    if dest is not None:
        # DestName is the actual .dds path and is normally relative to the
        # Texture XDB, not to the model or material which references it.
        dest_ref = dest.get("href") or (dest.text or "").strip()
        if dest_ref:
            source = data.resolve(dest_ref, texture_file or base)
            if source is not None:
                return data.prefer_overlay(source)

    # Fallback for slightly non-standard Texture records without DestName.
    if texture_file is not None:
        candidate = texture_file.with_suffix(".dds")
        if candidate.is_file():
            return data.prefer_overlay(candidate)
    return None


def _collect_troops(data: GameData) -> Tuple[Dict[str, dict], Dict[str, dict]]:
    """Collect active troop geometry and the textures referenced by its materials."""
    geometry_records: Dict[str, dict] = {}
    texture_records: Dict[str, dict] = {}
    seen_creatures: Set[str] = set()

    # Addon first so duplicate Creature XDBs use their patched records.
    for root in data.roots:
        creature_dir = root / "GameMechanics" / "Creature" / "Creatures"
        if not creature_dir.is_dir():
            continue
        for creature_file in sorted(creature_dir.rglob("*.xdb")):
            rel_key = creature_file.relative_to(root).as_posix().casefold()
            if rel_key in seen_creatures:
                continue
            try:
                creature = _parse(creature_file)
            except (ET.ParseError, OSError):
                continue
            if creature.tag != "Creature":
                continue
            # The None record has a visual dummy but no MonsterShared record.
            shared = creature.find("MonsterShared")
            if shared is None or not shared.get("href"):
                continue
            model_file, uid = _extract_model(data, creature_file, creature)
            if not uid or model_file is None:
                continue
            geometry_key = uid.casefold()
            rec = geometry_records.setdefault(geometry_key, {
                "uid": uid,
                "units": set(),
                "model_files": set(),
                "source_file": None,
            })
            rec["units"].add(creature_file.stem)
            rec["model_files"].add(data.rel_path(model_file))
            seen_creatures.add(rel_key)

            try:
                model = _parse(model_file)
            except (ET.ParseError, OSError):
                continue
            materials = model.find("Materials")
            if materials is None:
                continue
            for item in list(materials):
                material, material_file = _resolved_record(data, item, model_file)
                if material is None or material_file is None:
                    continue
                for slot_name in ("Texture", "Bump", "Gloss", "Mirror", "DetailTexture"):
                    source = _texture_dds(data, material.find(slot_name), material_file)
                    if source is None:
                        continue
                    rel = data.rel_path(source)
                    tex = texture_records.setdefault(rel.casefold(), {
                        "relative": rel,
                        "source": source,
                        "slots": set(),
                        "units": set(),
                    })
                    tex["slots"].add(slot_name)
                    tex["units"].add(creature_file.stem)
    return geometry_records, texture_records


def _normalize(v: Sequence[float], fallback: Sequence[float] = (0.0, 0.0, 1.0)) -> Tuple[float, float, float]:
    x, y, z = float(v[0]), float(v[1]), float(v[2])
    n2 = x * x + y * y + z * z
    if n2 <= 1e-16:
        return float(fallback[0]), float(fallback[1]), float(fallback[2])
    inv = 1.0 / math.sqrt(n2)
    return x * inv, y * inv, z * inv


def _midpoint_skin(group: MeshGroup, i: int, j: int) -> Tuple[Tuple[int, int, int, int], Tuple[float, float, float, float]]:
    weights_by_bone: Dict[int, float] = defaultdict(float)
    for vertex in (i, j):
        if vertex >= len(group.bone_indices) or vertex >= len(group.bone_weights):
            continue
        for bone, weight in zip(group.bone_indices[vertex], group.bone_weights[vertex]):
            if weight > 0.0:
                weights_by_bone[int(bone)] += float(weight) * 0.5
    strongest = sorted(weights_by_bone.items(), key=lambda pair: (-pair[1], pair[0]))[:4]
    total = sum(weight for _, weight in strongest)
    if total <= 1e-12:
        return (0, 0, 0, 0), (0.0, 0.0, 0.0, 0.0)
    joints = [bone for bone, _ in strongest]
    weights = [weight / total for _, weight in strongest]
    joints.extend([0] * (4 - len(joints)))
    weights.extend([0.0] * (4 - len(weights)))
    return tuple(joints[:4]), tuple(weights[:4])  # type: ignore[return-value]


def subdivide_group(group: MeshGroup) -> MeshGroup:
    """One feature-preserving longest-edge bisection pass (2x triangles).

    Each face gets one interpolated midpoint on its longest edge. New vertices
    stay exactly on the source surface, so silhouettes, armor corners, UV color
    islands and the original art direction do not shrink or round off. Normals
    and skin weights are interpolated. A shared edge reuses its midpoint when
    adjacent faces choose it too.
    """
    vertex_count = len(group.positions)
    if not group.triangles or vertex_count == 0:
        return group

    positions = list(group.positions)
    uvs = list(group.uvs)
    normals = list(group.normals)
    while len(uvs) < vertex_count:
        uvs.append((0.0, 0.0))
    while len(normals) < vertex_count:
        normals.append((0.0, 0.0, 0.0))

    # Fill genuinely missing source normals from the original face geometry;
    # valid shipped normals remain untouched.
    accumulated = [[0.0, 0.0, 0.0] for _ in range(vertex_count)]
    for tri in group.triangles:
        if min(tri) < 0 or max(tri) >= vertex_count:
            continue
        a, b, c = (positions[tri[0]], positions[tri[1]], positions[tri[2]])
        ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
        ac = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
        face = (ab[1] * ac[2] - ab[2] * ac[1],
                ab[2] * ac[0] - ab[0] * ac[2],
                ab[0] * ac[1] - ab[1] * ac[0])
        for index in tri:
            for axis in range(3):
                accumulated[index][axis] += face[axis]
    for i, normal in enumerate(normals):
        if sum(float(v) * float(v) for v in normal) <= 1e-12:
            normals[i] = _normalize(accumulated[i])
        else:
            normals[i] = _normalize(normal)

    has_skin = (len(group.bone_indices) == vertex_count
                and len(group.bone_weights) == vertex_count)
    bone_indices = list(group.bone_indices) if has_skin else []
    bone_weights = list(group.bone_weights) if has_skin else []
    edge_cache: Dict[Tuple[int, int], int] = {}

    def edge_vertex(i: int, j: int) -> int:
        key = (i, j) if i < j else (j, i)
        cached = edge_cache.get(key)
        if cached is not None:
            return cached
        a, b = key
        index = len(positions)
        positions.append(tuple((float(positions[a][k]) + float(positions[b][k])) * 0.5 for k in range(3)))
        uvs.append(tuple((float(uvs[a][k]) + float(uvs[b][k])) * 0.5 for k in range(2)))
        normals.append(_normalize(tuple(float(normals[a][k]) + float(normals[b][k]) for k in range(3)), normals[a]))
        if has_skin:
            joints, weights = _midpoint_skin(group, a, b)
            bone_indices.append(joints)
            bone_weights.append(weights)
        edge_cache[key] = index
        return index

    triangles: List[Tuple[int, int, int]] = []
    for tri in group.triangles:
        if min(tri) < 0 or max(tri) >= vertex_count:
            continue
        a, b, c = (int(tri[0]), int(tri[1]), int(tri[2]))
        candidates = ((a, b, c), (b, c, a), (c, a, b))
        e0, e1, opposite = max(
            candidates,
            key=lambda edge: sum((float(positions[edge[0]][axis]) -
                                  float(positions[edge[1]][axis])) ** 2 for axis in range(3)))
        midpoint = edge_vertex(e0, e1)
        triangles.extend(((e0, midpoint, opposite), (midpoint, e1, opposite)))

    return MeshGroup(
        positions=positions,
        uvs=uvs,
        normals=normals,
        tangents=[],
        triangles=triangles,
        bone_indices=bone_indices,
        bone_weights=bone_weights,
    )


def _pack_floats(values: Iterable[float]) -> bytes:
    vals = list(values)
    return struct.pack("<%df" % len(vals), *vals) if vals else b""


def _write_glb(geom: Geometry, uid: str) -> bytes:
    """Write the importer's GLB subset while promoting large indices to uint32."""
    binary = bytearray()
    views: List[dict] = []
    accessors: List[dict] = []
    meshes: List[dict] = []
    nodes: List[dict] = []

    def add_accessor(blob: bytes, component_type: int, count: int, kind: str,
                     target: int, minmax: bool = False) -> int:
        while len(binary) % 4:
            binary.append(0)
        offset = len(binary)
        binary.extend(blob)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(blob), "target": target}
        views.append(view)
        accessor = {"bufferView": len(views) - 1, "componentType": component_type,
                    "count": count, "type": kind}
        if minmax and count:
            vals = struct.unpack("<%df" % (len(blob) // 4), blob)
            accessor["min"] = [min(vals[i * 3 + axis] for i in range(count)) for axis in range(3)]
            accessor["max"] = [max(vals[i * 3 + axis] for i in range(count)) for axis in range(3)]
        accessors.append(accessor)
        return len(accessors) - 1

    for mesh_index, source_mesh in enumerate(geom.meshes):
        for group_index, group in enumerate(source_mesh.groups):
            if not group.triangles:
                continue
            vertex_count = len(group.positions)
            if not vertex_count:
                continue
            positions = [tuple(float(v) for v in p[:3]) for p in group.positions]
            normals = [tuple(float(v) for v in n[:3]) for n in group.normals[:vertex_count]]
            while len(normals) < vertex_count:
                normals.append((0.0, 0.0, 1.0))
            uvs = [tuple(float(v) for v in uv[:2]) for uv in group.uvs[:vertex_count]]
            while len(uvs) < vertex_count:
                uvs.append((0.0, 0.0))

            position_accessor = add_accessor(
                _pack_floats(v for p in positions for v in p), COMPONENT_FLOAT,
                vertex_count, "VEC3", 34962, minmax=True)
            normal_accessor = add_accessor(
                _pack_floats(v for n in normals for v in n), COMPONENT_FLOAT,
                vertex_count, "VEC3", 34962)
            uv_accessor = add_accessor(
                _pack_floats(v for uv in uvs for v in uv), COMPONENT_FLOAT,
                vertex_count, "VEC2", 34962)

            flat_indices = [int(index) for tri in group.triangles for index in tri]
            if flat_indices and max(flat_indices) <= 0xFFFF:
                index_blob = struct.pack("<%dH" % len(flat_indices), *flat_indices)
                index_component = COMPONENT_UNSIGNED_SHORT
            else:
                index_blob = struct.pack("<%dI" % len(flat_indices), *flat_indices)
                index_component = COMPONENT_UNSIGNED_INT
            index_accessor = add_accessor(index_blob, index_component, len(flat_indices),
                                          "SCALAR", 34963)

            attributes = {"POSITION": position_accessor, "NORMAL": normal_accessor,
                          "TEXCOORD_0": uv_accessor}
            if (len(group.bone_indices) == vertex_count
                    and len(group.bone_weights) == vertex_count):
                joints = [tuple(max(0, min(255, int(v))) for v in row[:4])
                          for row in group.bone_indices]
                weights = [tuple(float(v) for v in row[:4]) for row in group.bone_weights]
                if all(len(row) == 4 for row in joints) and all(len(row) == 4 for row in weights):
                    joint_blob = struct.pack("<%dB" % (vertex_count * 4),
                                             *(v for row in joints for v in row))
                    weight_blob = _pack_floats(v for row in weights for v in row)
                    attributes["JOINTS_0"] = add_accessor(
                        joint_blob, COMPONENT_UNSIGNED_BYTE, vertex_count, "VEC4", 34962)
                    attributes["WEIGHTS_0"] = add_accessor(
                        weight_blob, COMPONENT_FLOAT, vertex_count, "VEC4", 34962)

            mesh_name = "%s_%d_%d" % (uid, mesh_index, group_index)
            meshes.append({"name": mesh_name, "primitives": [{
                "attributes": attributes, "indices": index_accessor, "mode": 4}]})
            nodes.append({"mesh": len(meshes) - 1, "name": mesh_name})

    if not meshes:
        raise ValueError("geometry %s has no drawable mesh" % uid)

    document = {
        "asset": {"version": "2.0", "generator": "homm5-troop-hd"},
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
    total_length = 12 + 8 + len(json_chunk) + 8 + len(binary)
    out = bytearray(struct.pack("<III", GLB_MAGIC, GLB_VERSION, total_length))
    out.extend(struct.pack("<II", len(json_chunk), 0x4E4F534A))
    out.extend(json_chunk)
    out.extend(struct.pack("<II", len(binary), 0x004E4942))
    out.extend(binary)
    return bytes(out)


def _validate_glb(blob: bytes, expected_triangles: int) -> dict:
    if len(blob) < 20:
        raise ValueError("GLB is too short")
    magic, version, total = struct.unpack_from("<III", blob, 0)
    if magic != GLB_MAGIC or version != GLB_VERSION or total != len(blob):
        raise ValueError("invalid GLB header")
    json_size, chunk_type = struct.unpack_from("<II", blob, 12)
    if chunk_type != 0x4E4F534A:
        raise ValueError("GLB is missing its JSON chunk")
    document = json.loads(blob[20:20 + json_size].decode("utf-8"))
    triangle_count = 0
    vertex_count = 0
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            index_accessor = document["accessors"][primitive["indices"]]
            triangle_count += int(index_accessor["count"]) // 3
            pos_accessor = document["accessors"][primitive["attributes"]["POSITION"]]
            vertex_count += int(pos_accessor["count"])
    if triangle_count != expected_triangles:
        raise ValueError("GLB triangle count mismatch: %d != %d" % (triangle_count, expected_triangles))
    return {"triangles": triangle_count, "vertices": vertex_count,
            "meshes": len(document.get("meshes", []))}


def _png_dimensions(path: Path) -> Tuple[int, int]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or len(data) < 24:
        raise ValueError("invalid PNG written by ImageMagick: " + str(path))
    return struct.unpack_from(">II", data, 16)


_SECONDARY_TEXTURE_MARKERS = (
    "_add", "add_", "add.", "_glow", "glow.", "glow_", "_glass", "glass",
    "_fx", "fx_", "spiral", "lightning", "miror", "mirror", "gloss",
    "fire1", "_burn", "_lash",
)


def _is_primary_albedo(record: dict) -> bool:
    if "Texture" not in record["slots"]:
        return False
    name = Path(record["relative"]).stem.casefold()
    path = "/" + record["relative"].casefold().replace("\\", "/") + "/"
    if "/effects/" in path or "/cutscenes/" in path:
        return False
    return not any(marker in name for marker in _SECONDARY_TEXTURE_MARKERS)


def _upscale_texture(record: dict, output_root: Path, convert: str) -> dict:
    source: Path = record["source"]
    relative = record["relative"]
    target_rel = "Textures/" + safe_relative_path(relative) + ".png"
    target = output_root / "Textures" / target_rel
    target.parent.mkdir(parents=True, exist_ok=True)

    width, height, rgba = decode_dds(source.read_bytes())
    primary = _is_primary_albedo(record)
    # Scale the authored color atlases moderately; masks, glow/additive layers,
    # mirrors and gloss maps keep their source size so their alpha/material
    # boundaries remain exact. Never create a huge atlas beyond 2048 px.
    scale = min(1.5, 2048.0 / max(width, height)) if primary else 1.0
    out_w = max(1, int(round(width * scale)))
    out_h = max(1, int(round(height * scale)))
    palette_colors = 192 if primary else 128
    alpha_values = rgba[3::4]
    has_soft_alpha = any(alpha < 255 for alpha in alpha_values)
    has_fully_transparent = any(alpha == 0 for alpha in alpha_values)
    # ImageMagick 6's PNG8 writer drops a texture's alpha when it contains only
    # partial opacity and no fully transparent texel. Keep those uncommon maps
    # as RGBA PNG32 instead of silently making them opaque.
    preserve_soft_alpha = has_soft_alpha and not has_fully_transparent

    with tempfile.TemporaryDirectory(prefix="troop-hd-") as temp_dir:
        temp_in = Path(temp_dir) / "source.png"
        temp_out = Path(temp_dir) / "upscaled.png"
        write_png(str(temp_in), width, height, bytes(rgba))
        command = [convert, str(temp_in), "-filter", "Lanczos", "-resize",
                   "%dx%d!" % (out_w, out_h)]
        if primary:
            # A restrained high-frequency lift makes the original painted edges
            # read more clearly without recolouring or repainting the UV islands.
            command += ["-channel", "RGB", "-unsharp", "0x0.45+0.18+0.01", "+channel"]
        if preserve_soft_alpha:
            command += ["-strip", "-define", "png:compression-level=9", "PNG32:" + str(temp_out)]
        else:
            # Palette PNG keeps the copy-ready patch compact. Floyd-Steinberg
            # dithering avoids obvious banding in the game's painted gradients.
            command += ["-dither", "FloydSteinberg", "-colors", str(palette_colors),
                        "-strip", "-define", "png:compression-level=9", "PNG8:" + str(temp_out)]
        subprocess.run(command, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        target.write_bytes(temp_out.read_bytes())

    out_width, out_height = _png_dimensions(target)
    if (out_width, out_height) != (out_w, out_h):
        raise ValueError("texture dimensions mismatch after upscale: " + relative)
    return {
        "source_dds": relative,
        "output_png": target_rel,
        "source_size": [width, height],
        "output_size": [out_width, out_height],
        "slots": sorted(record["slots"]),
        "units": sorted(record["units"]),
        "primary_albedo": primary,
        "palette_colors": None if preserve_soft_alpha else palette_colors,
        "alpha_encoding": "rgba" if preserve_soft_alpha else "palette",
        "bytes": target.stat().st_size,
    }


def build_patch(repo: Path, out_dir: Path, subdivision_levels: int = 1) -> dict:
    data = GameData(repo)
    convert = shutil.which("magick") or shutil.which("convert")
    if not convert:
        raise RuntimeError("ImageMagick (convert or magick) is required to build textures")

    geometry_records, texture_records = _collect_troops(data)
    if not geometry_records:
        raise RuntimeError("no playable troop models were found in the game data")

    output_root = out_dir / "HoMM5"
    mesh_out = output_root / "Meshes"
    mesh_out.mkdir(parents=True, exist_ok=True)
    source_geometry_index: Dict[str, Path] = {}
    for root in data.roots:
        for folder_name in ("Geometries", "geometries"):
            folder = root / "bin" / folder_name
            if not folder.is_dir():
                continue
            for path in folder.iterdir():
                if path.is_file():
                    source_geometry_index.setdefault(path.name.casefold(), path)

    mesh_manifest = []
    total_original_triangles = 0
    total_hd_triangles = 0
    missing_geometry = []
    for key, record in sorted(geometry_records.items()):
        uid = record["uid"]
        source = source_geometry_index.get(key)
        if source is None:
            missing_geometry.append(uid)
            continue
        geom = decode_file(str(source))
        original_triangles = geom.total_triangles
        original_vertices = geom.total_vertices
        if subdivision_levels:
            for mesh in geom.meshes:
                mesh.groups = [subdivide_group(group) for group in mesh.groups]
        hd_triangles = geom.total_triangles
        blob = _write_glb(geom, uid)
        stats = _validate_glb(blob, hd_triangles)
        target = mesh_out / (uid + ".glb")
        target.write_bytes(blob)
        total_original_triangles += original_triangles
        total_hd_triangles += hd_triangles
        mesh_manifest.append({
            "uid": uid,
            "source_geometry": data.rel_path(source),
            "models": sorted(record["model_files"]),
            "units": sorted(record["units"]),
            "source_vertices": original_vertices,
            "source_triangles": original_triangles,
            "hd_vertices": stats["vertices"],
            "hd_triangles": stats["triangles"],
            "mesh_slices": stats["meshes"],
            "output_glb": "Meshes/" + uid + ".glb",
            "bytes": target.stat().st_size,
        })

    if missing_geometry:
        raise RuntimeError("missing source geometry for: " + ", ".join(missing_geometry[:10]))

    texture_manifest = []
    for _, record in sorted(texture_records.items()):
        texture_manifest.append(_upscale_texture(record, output_root, convert))

    patch_manifest = {
        "format": "HoMM5 Unity StreamingAssets overlay",
        "method": {
            "mesh": "one feature-preserving longest-edge bisection pass (2x triangles); new vertices stay on the source surface; UVs, normals and skin weights are interpolated",
            "texture": "diffuse color atlases (including weapon maps) are scaled up to 1.5x (maximum side 2048) with a restrained unsharp mask and 192-color palette; effect, mirror and gloss maps keep source dimensions in a dithered 128-color palette; alpha is retained, with RGBA PNG fallback for partially transparent images that have no fully transparent texels",
            "art_direction": "source geometry, palette and UVs are not repainted or reshaped",
        },
        "source_roots": [str(path.relative_to(repo)) for path in data.roots],
        "subdivision_levels": subdivision_levels,
        "mesh_count": len(mesh_manifest),
        "texture_count": len(texture_manifest),
        "source_triangles": total_original_triangles,
        "hd_triangles": total_hd_triangles,
        "geometry": mesh_manifest,
        "textures": texture_manifest,
    }
    manifest_path = out_dir / "manifest.json"
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(patch_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return patch_manifest


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=REPO, help="repository root (default: detected automatically)")
    parser.add_argument("--out", type=Path, default=REPO / "Troops_HD_Patch",
                        help="patch output directory (default: Troops_HD_Patch/)")
    parser.add_argument("--subdivision-levels", type=int, choices=(0, 1), default=1,
                        help="feature-preserving mesh refinement passes (default: 1)")
    args = parser.parse_args(argv)
    repo = args.repo.resolve()
    out = args.out if args.out.is_absolute() else (repo / args.out)
    try:
        result = build_patch(repo, out.resolve(), args.subdivision_levels)
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError, DDSError) as exc:
        print("error: %s" % exc, file=sys.stderr)
        return 1
    print("Troop overlay created: %s" % (out.resolve() / "HoMM5"))
    print("  models: %d | triangles: %d -> %d" % (
        result["mesh_count"], result["source_triangles"], result["hd_triangles"]))
    print("  textures: %d | manifest: %s" % (result["texture_count"], out.resolve() / "manifest.json"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
