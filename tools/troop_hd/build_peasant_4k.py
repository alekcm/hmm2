#!/usr/bin/env python3
"""Build a separate, UV-audited high-poly Haven Peasant variant.

The output is deliberately kept outside HoMM5/Meshes so it cannot replace the
currently selected Peasant model when the overlay is copied into StreamingAssets.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from collections import Counter
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "troop_hd"))
sys.path.insert(0, str(REPO / "tools" / "homm5_import"))
import build_peasant_poc as poc  # noqa: E402
from homm5import.dds import decode_dds, write_png  # noqa: E402
from homm5import.geometry import Geometry, MeshGroup, decode_file  # noqa: E402

LABEL = "Haven Peasant 4K"
VARIANT_DIR = REPO / "Troops_HD_Patch" / "Variants" / "HavenPeasant_4K"
ATLAS_SOURCE = (REPO / "data" / "Textures" / "auto-imported_______320" / "dev" / "Test"
                / "TMPcharacters" / "Peasant" / "Peasant.tga.dds")
REFINEMENT_PASSES = 2
REGION_THRESHOLD = 0.20
REGION_NAMES = ("other", "hands", "hood", "legs_and_shins", "pouch")
REGION_ID = {name: index for index, name in enumerate(REGION_NAMES)}
UNKNOWN, AMBIGUOUS = 255, 254

# Colors used for the UV diagnostic. The map is an overlay only; no material
# texture pixels are written back to the game asset directory.
REGION_COLORS: Dict[str, Tuple[int, int, int]] = {
    "hands": (238, 66, 73),
    "hood": (255, 184, 48),
    "legs_and_shins": (44, 215, 115),
    "pouch": (202, 89, 224),
    "other": (210, 220, 230),
}
BACKGROUND = (34, 38, 43)


def _triangle_points(uvs: Sequence[Sequence[float]], width: int, height: int
                     ) -> Tuple[Tuple[float, float], ...]:
    """Convert a triangle's normalized glTF UVs to top-left-origin atlas pixels."""
    return tuple((
        max(0.0, min(width - 1.0, float(uv[0]) * (width - 1))),
        max(0.0, min(height - 1.0, (1.0 - float(uv[1])) * (height - 1))),
    ) for uv in uvs)


def _triangle_pixels(points: Sequence[Sequence[float]], width: int, height: int
                     ) -> Iterable[Tuple[int, int]]:
    """Yield atlas texels covered by one UV-space triangle."""
    (x0, y0), (x1, y1), (x2, y2) = points
    denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
    if abs(denom) < 1e-10:
        return
    left = max(0, int(math.floor(min(x0, x1, x2))))
    right = min(width - 1, int(math.ceil(max(x0, x1, x2))))
    top = max(0, int(math.floor(min(y0, y1, y2))))
    bottom = min(height - 1, int(math.ceil(max(y0, y1, y2))))
    for y in range(top, bottom + 1):
        py = y + 0.5
        for x in range(left, right + 1):
            px = x + 0.5
            w0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / denom
            w1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / denom
            w2 = 1.0 - w0 - w1
            if w0 >= -1e-6 and w1 >= -1e-6 and w2 >= -1e-6:
                yield x, y


def _face_part_scores(group: MeshGroup) -> Tuple[List[str], List[float], List[dict]]:
    """Assign a provisional anatomical region to each triangle from skin weights."""
    _, vertex_parts = poc._part_masks(group)
    labels: List[str] = []
    confidence: List[float] = []
    scores_by_face: List[dict] = []
    part_names = tuple(vertex_parts)
    for triangle in group.triangles:
        scores = {
            name: sum(vertex_parts[name][int(index)] for index in triangle) / 3.0
            for name in part_names
        }
        ranked = sorted(scores.items(), key=lambda item: (-item[1], item[0]))
        best_name, best_score = ranked[0]
        label = best_name if best_score >= REGION_THRESHOLD else "other"
        labels.append(label)
        confidence.append(best_score if label != "other" else 1.0 - best_score)
        scores_by_face.append(scores)
    return labels, confidence, scores_by_face


def map_triangles_through_uv(group: MeshGroup, width: int, height: int
                             ) -> Tuple[List[str], List[dict], bytearray]:
    """Rasterize semantic face labels into UV space and resolve each face there.

    Semantic seeds come from the rig's skin weights, then are projected through
    the mesh's actual UV triangles onto the Peasant atlas. Sampling each face's
    UV centroid makes the correspondence auditable and detects overlapping UV
    islands instead of silently treating an atlas pixel as a unique face.
    """
    if len(group.uvs) < len(group.positions):
        raise ValueError("BodyShape needs UV coordinates to create a UV region map")
    raw_labels, confidence, scores_by_face = _face_part_scores(group)
    uv_labels = bytearray([UNKNOWN]) * (width * height)
    face_points: List[Tuple[Tuple[float, float], ...]] = []

    for triangle, raw_label in zip(group.triangles, raw_labels):
        points = _triangle_points([group.uvs[int(i)] for i in triangle], width, height)
        face_points.append(points)
        label_id = REGION_ID[raw_label]
        pixels = list(_triangle_pixels(points, width, height))
        if not pixels:
            center_x = int(round(sum(point[0] for point in points) / 3.0))
            center_y = int(round(sum(point[1] for point in points) / 3.0))
            pixels = [(max(0, min(width - 1, center_x)),
                       max(0, min(height - 1, center_y)))]
        for x, y in pixels:
            offset = y * width + x
            previous = uv_labels[offset]
            if previous == UNKNOWN:
                uv_labels[offset] = label_id
            elif previous != label_id and previous != AMBIGUOUS:
                uv_labels[offset] = AMBIGUOUS

    resolved_labels: List[str] = []
    records: List[dict] = []
    for face_index, (triangle, raw_label, face_confidence, scores, points) in enumerate(
            zip(group.triangles, raw_labels, confidence, scores_by_face, face_points)):
        center_u = sum(float(group.uvs[int(i)][0]) for i in triangle) / 3.0
        center_v = sum(float(group.uvs[int(i)][1]) for i in triangle) / 3.0
        atlas_x = max(0, min(width - 1, int(round(center_u * (width - 1)))))
        atlas_y = max(0, min(height - 1, int(round((1.0 - center_v) * (height - 1)))))
        sampled_id = uv_labels[atlas_y * width + atlas_x]
        if sampled_id == AMBIGUOUS:
            status = "uv_overlap_ambiguous"
            resolved = raw_label
        elif sampled_id == UNKNOWN:
            status = "uv_no_coverage_fallback_to_skin_weights"
            resolved = raw_label
        else:
            sampled_label = REGION_NAMES[sampled_id]
            if sampled_label == raw_label:
                status = "uv_confirmed"
                resolved = sampled_label
            else:
                # An atlas overlap can put another island under the centroid.
                # Keep the rig-derived label rather than misclassifying a face.
                status = "uv_overlap_conflict_fallback_to_skin_weights"
                resolved = raw_label
        resolved_labels.append(resolved)
        record = {
            "triangle_index": face_index,
            "region": resolved,
            "skin_region": raw_label,
            "region_confidence": face_confidence,
            "uv_mapping_status": status,
            "uv_center_u": center_u,
            "uv_center_v": center_v,
            "atlas_x": atlas_x,
            "atlas_y": atlas_y,
            "u0": float(group.uvs[int(triangle[0])][0]),
            "v0": float(group.uvs[int(triangle[0])][1]),
            "u1": float(group.uvs[int(triangle[1])][0]),
            "v1": float(group.uvs[int(triangle[1])][1]),
            "u2": float(group.uvs[int(triangle[2])][0]),
            "v2": float(group.uvs[int(triangle[2])][1]),
        }
        record.update({f"{name}_score": scores[name] for name in scores})
        records.append(record)
    return resolved_labels, records, uv_labels


def _blend_pixel(image: bytearray, width: int, x: int, y: int,
                 color: Sequence[int], alpha: int) -> None:
    offset = (y * width + x) * 4
    inverse = 255 - alpha
    for channel in range(3):
        image[offset + channel] = (image[offset + channel] * inverse
                                   + int(color[channel]) * alpha) // 255
    image[offset + 3] = 255


def _draw_line(image: bytearray, width: int, height: int,
               start: Sequence[float], end: Sequence[float], color: Sequence[int],
               alpha: int) -> None:
    x0, y0 = int(round(start[0])), int(round(start[1]))
    x1, y1 = int(round(end[0])), int(round(end[1]))
    dx, dy = abs(x1 - x0), abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    error = dx - dy
    while True:
        if 0 <= x0 < width and 0 <= y0 < height:
            _blend_pixel(image, width, x0, y0, color, alpha)
        if x0 == x1 and y0 == y1:
            break
        doubled = 2 * error
        if doubled > -dy:
            error -= dy
            x0 += sx
        if doubled < dx:
            error += dx
            y0 += sy


def render_uv_map(base_rgba: Sequence[int], group: MeshGroup, labels: Sequence[str],
                  width: int, height: int, wireframe: bool) -> bytes:
    """Composite semantic UV regions over the original diffuse atlas."""
    if len(base_rgba) != width * height * 4:
        raise ValueError("atlas pixel buffer size does not match its dimensions")
    if len(labels) != len(group.triangles):
        raise ValueError("UV labels must match the final body triangle count")
    image = bytearray(base_rgba)
    for pixel in range(width * height):
        offset = pixel * 4
        alpha = image[offset + 3]
        if alpha < 255:
            inverse = 255 - alpha
            for channel in range(3):
                image[offset + channel] = (image[offset + channel] * alpha
                                           + BACKGROUND[channel] * inverse) // 255
            image[offset + 3] = 255

    # First fill regions; thin regions remain visible because every face maps
    # its UV polygon rather than only the centroid.
    for triangle, label in zip(group.triangles, labels):
        if label == "other":
            continue
        points = _triangle_points([group.uvs[int(i)] for i in triangle], width, height)
        for x, y in _triangle_pixels(points, width, height):
            _blend_pixel(image, width, x, y, REGION_COLORS[label], 96)
    if wireframe:
        for triangle, label in zip(group.triangles, labels):
            points = _triangle_points([group.uvs[int(i)] for i in triangle], width, height)
            color = REGION_COLORS.get(label, REGION_COLORS["other"])
            alpha = 150 if label != "other" else 45
            for q in range(3):
                _draw_line(image, width, height, points[q], points[(q + 1) % 3], color, alpha)
    return bytes(image)


def _group_signature(group: MeshGroup) -> tuple:
    return (tuple(group.positions), tuple(group.uvs), tuple(group.normals),
            tuple(group.triangles), tuple(group.bone_indices), tuple(group.bone_weights))


def _write_triangle_csv(path: Path, body_records: Sequence[dict], fork: MeshGroup) -> None:
    fieldnames = [
        "mesh_slice", "triangle_index", "region", "skin_region", "region_confidence",
        "uv_mapping_status", "uv_center_u", "uv_center_v", "atlas_x", "atlas_y",
        "u0", "v0", "u1", "v1", "u2", "v2",
        "hands_score", "hood_score", "legs_and_shins_score", "pouch_score",
    ]
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        for source in body_records:
            row = {name: source.get(name, "") for name in fieldnames}
            row["mesh_slice"] = "BodyShape"
            for key in ("region_confidence", "uv_center_u", "uv_center_v", "u0", "v0",
                        "u1", "v1", "u2", "v2", "hands_score", "hood_score",
                        "legs_and_shins_score", "pouch_score"):
                if row[key] != "":
                    row[key] = f"{float(row[key]):.7f}"
            writer.writerow(row)
        for face_index, triangle in enumerate(fork.triangles):
            uv_points = [fork.uvs[int(i)] for i in triangle] if len(fork.uvs) >= len(fork.positions) else []
            row = {name: "" for name in fieldnames}
            row.update({"mesh_slice": "PitchForkShape", "triangle_index": face_index,
                        "region": "pitchfork_preserved",
                        "uv_mapping_status": "separate_mesh_not_part_of_peasant_diffuse_atlas"})
            if uv_points:
                center_u = sum(float(uv[0]) for uv in uv_points) / 3.0
                center_v = sum(float(uv[1]) for uv in uv_points) / 3.0
                row.update({"uv_center_u": f"{center_u:.7f}", "uv_center_v": f"{center_v:.7f}"})
                for corner, uv in enumerate(uv_points):
                    row[f"u{corner}"] = f"{float(uv[0]):.7f}"
                    row[f"v{corner}"] = f"{float(uv[1]):.7f}"
            writer.writerow(row)


def build(out_dir: Path = VARIANT_DIR) -> dict:
    if not poc.SOURCE_GEOMETRY.is_file():
        raise FileNotFoundError(f"Peasant source geometry is missing: {poc.SOURCE_GEOMETRY}")
    if not ATLAS_SOURCE.is_file():
        raise FileNotFoundError(f"Peasant atlas DDS is missing: {ATLAS_SOURCE}")

    geometry: Geometry = decode_file(str(poc.SOURCE_GEOMETRY))
    if (len(geometry.meshes) != 2 or len(geometry.meshes[0].groups) != 1
            or len(geometry.meshes[1].groups) != 1):
        raise ValueError("expected BodyShape and separate PitchForkShape mesh slices")
    body = geometry.meshes[0].groups[0]
    fork = geometry.meshes[1].groups[0]
    fork_before = _group_signature(fork)
    atlas_width, atlas_height, atlas_rgba = decode_dds(ATLAS_SOURCE.read_bytes())

    passes = []
    for pass_number in range(1, REFINEMENT_PASSES + 1):
        uv_regions, _, _ = map_triangles_through_uv(body, atlas_width, atlas_height)
        body, stats = poc.refine_body(body, triangle_regions=uv_regions)
        stats["pass"] = pass_number
        stats["uv_triangle_regions"] = dict(Counter(uv_regions))
        passes.append(stats)
    geometry.meshes[0].groups[0] = body
    if _group_signature(geometry.meshes[1].groups[0]) != fork_before:
        raise AssertionError("4K generation modified the separate PitchForkShape")

    final_regions, body_records, _ = map_triangles_through_uv(body, atlas_width, atlas_height)
    status_counts = Counter(record["uv_mapping_status"] for record in body_records)
    region_counts = Counter(final_regions)

    out_dir.mkdir(parents=True, exist_ok=True)
    model_path = out_dir / "HavenPeasant_4K.glb"
    blob = poc.write_glb(geometry, "HavenPeasant_4K", variant_label=LABEL)
    glb_stats = poc.validate_glb(blob, geometry.total_triangles)
    model_path.write_bytes(blob)

    fork = geometry.meshes[1].groups[0]
    _write_triangle_csv(out_dir / "triangle_uv_regions.csv", body_records, fork)
    write_png(str(out_dir / "uv_semantic_map.png"), atlas_width, atlas_height,
              render_uv_map(atlas_rgba, body, final_regions, atlas_width, atlas_height, wireframe=False))
    write_png(str(out_dir / "uv_triangle_wireframe.png"), atlas_width, atlas_height,
              render_uv_map(atlas_rgba, body, final_regions, atlas_width, atlas_height, wireframe=True))

    manifest = {
        "label": LABEL,
        "edition_note": "4K is a variant label, not a 4096-pixel texture claim or an exact triangle target",
        "is_separate_variant": True,
        "replaces_default_peasant": False,
        "default_model_unchanged": f"../../HoMM5/Meshes/{poc.UID}.glb",
        "model": model_path.name,
        "source_geometry_uid": poc.UID,
        "source_skeleton_uid": poc.SKELETON_UID,
        "source_texture_dds": str(ATLAS_SOURCE.relative_to(REPO)),
        "atlas_dimensions": [atlas_width, atlas_height],
        "uv_semantic_tracking": {
            "triangle_csv": "triangle_uv_regions.csv",
            "semantic_map": "uv_semantic_map.png",
            "triangle_wireframe": "uv_triangle_wireframe.png",
            "labels": {
                "hands": "red",
                "hood": "amber",
                "legs_and_shins": "green",
                "pouch": "violet",
                "other": "gray wireframe / unfilled",
                "pitchfork_preserved": "separate mesh; not projected onto the Peasant diffuse atlas",
            },
            "region_counts": dict(region_counts),
            "uv_mapping_status_counts": dict(status_counts),
            "method": "seed triangle semantics from skin weights; rasterize each UV triangle into atlas space; sample its UV centroid and record overlaps/fallbacks; use these UV-region labels to gate per-vertex smoothing masks",
        },
        "refinement": {
            "passes": passes,
            "body_shape_triangles": len(body.triangles),
            "pitchfork_shape_triangles": len(fork.triangles),
            "pitchfork_changed": False,
        },
        "glb": glb_stats | {"bytes": len(blob)},
        "textures_modified": False,
        "texture_note": "the existing restored texture files are not copied or modified by this builder",
    }
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=VARIANT_DIR,
                        help="separate output folder for the Haven Peasant 4K variant")
    args = parser.parse_args()
    result = build(args.out.resolve())
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
