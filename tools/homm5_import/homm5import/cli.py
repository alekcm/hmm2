"""Command line entry point for the HoMM5 -> Unity import pipeline.

    python -m homm5import.cli info     --data <data> --texts <texts>
    python -m homm5import.cli gamedata --data <data> --texts <texts> -o out/
    python -m homm5import.cli maps     --data <data> -o out/ [--limit N]
    python -m homm5import.cli meshes   --data <data> -o out/ [--limit N]
    python -m homm5import.cli textures --data <data> -o out/ [--limit N]
    python -m homm5import.cli terrain  --data <data> -o out/ [--limit N]
    python -m homm5import.cli all      --data <data> --texts <texts> -o out/

Everything writes plain files (JSON / PNG / GLB) into the Unity project's
``Assets/StreamingAssets/HoMM5`` tree, which the runtime loads with
``UnityWebRequest``/``File.ReadAllBytes`` -- no editor-time asset database needed,
so a partial import is still a runnable game.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from typing import Iterable, List, Optional

from .dds import DDSError, dds_to_png
from .gamedata import export_all
from .geometry import decode_file as decode_geometry
from .gltf import geometry_to_glb
from .localization import Localization
from .maps import iter_map_files, load_map, unique_map_ids
from .terrain import decode_file as decode_terrain
from .xdb import XDB


def _looks_like_data_root(path: str) -> bool:
    """Identify the unpacked ``data`` folder.

    ``GameMechanics`` is the discriminator.  The repository root also has a
    ``bin`` (the game's own executables), so testing for ``bin`` alone mistakes
    the repo root for the data root and every later stage finds nothing.
    """
    if not os.path.isdir(path):
        return False
    if os.path.isdir(os.path.join(path, "GameMechanics")):
        return True
    return (os.path.isdir(os.path.join(path, "Maps"))
            and os.path.isdir(os.path.join(path, "Textures")))


def _resolve_roots(explicit_data, explicit_texts):
    """Work out where the game data and texts live.

    ``--data`` may point either at the data root itself or at the repository
    folder that contains it.  Guessing wrong appends a second ``data`` and the
    run dies claiming there is no data at all, so both readings are tried
    explicitly instead.
    """
    data = os.path.abspath(explicit_data) if explicit_data else None

    if data:
        if _looks_like_data_root(data):
            pass                                   # points at the data root
        elif _looks_like_data_root(os.path.join(data, "data")):
            data = os.path.join(data, "data")      # points at the repo root
    else:
        here = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
        for cand in (here, os.getcwd(), os.path.dirname(os.getcwd())):
            if _looks_like_data_root(os.path.join(cand, "data")):
                data = os.path.join(cand, "data")
                break
            if _looks_like_data_root(cand):
                data = cand
                break

    texts = os.path.abspath(explicit_texts) if explicit_texts else None
    if texts:
        if not os.path.isdir(texts) and os.path.isdir(os.path.join(texts, "texts")):
            texts = os.path.join(texts, "texts")
    elif data:
        guess = os.path.join(os.path.dirname(data), "texts")
        texts = guess if os.path.isdir(guess) else None

    if texts and not os.path.isdir(texts):
        texts = None
    return data, texts


def _out(out_dir: str, *parts: str) -> str:
    """Join a destination path, splitting forward slashes into real components.

    Callers pass source-relative paths like ``Textures/A/B.dds``, which on
    Windows would otherwise land inside ``os.path.join`` as a single component
    with embedded separators.
    """
    components = [out_dir]
    for part in parts:
        components.extend(part.replace("\\", "/").split("/"))
    p = os.path.join(*[c for c in components if c])
    parent = os.path.dirname(p)
    if parent:
        os.makedirs(parent, exist_ok=True)
    return p


# Unity refuses asset names that start with a space or contain ? < > : * | ",
# and the game ships at least one such file:
#   data/Textures/Effects/_Various/ Balor_swordClot_red_NoADD.(Texture).dds
# The source path is preserved in the manifest, so renaming on the way out costs
# nothing and keeps the import loadable.
_UNSAFE_NAME_CHARS = set('?<>:*|"')


def safe_relative_path(rel: str) -> str:
    """Normalise a source-relative path into a Unity-acceptable asset path."""
    parts = []
    for part in rel.replace("\\", "/").split("/"):
        cleaned = "".join("_" if c in _UNSAFE_NAME_CHARS else c for c in part)
        cleaned = cleaned.strip(" ")
        if cleaned.startswith("."):
            cleaned = "_" + cleaned[1:]
        parts.append(cleaned or "_")
    return "/".join(parts)


def _write_json(path: str, obj) -> None:
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(obj, fh, ensure_ascii=False, separators=(",", ":"))


def _limited(items: Iterable[str], limit: Optional[int]) -> Iterable[str]:
    if limit is None:
        return items
    out = []
    for i, v in enumerate(items):
        if i >= limit:
            break
        out.append(v)
    return out


def cmd_info(args) -> int:
    xdb = XDB(args.data)
    loc = Localization(args.texts) if args.texts else None
    import glob

    print("data root :", args.data)
    print("texts root:", args.texts or "(none)")
    print("xdb files :", sum(1 for _ in xdb.iter_files("", ".xdb")))
    print("dds files :", len(glob.glob(os.path.join(args.data, "**", "*.dds"), recursive=True)))
    print("geometries:", len(glob.glob(os.path.join(args.data, "bin/Geometries/*"))))
    print("skeletons :", len(glob.glob(os.path.join(args.data, "bin/Skeletons/*"))))
    print("animations:", len(glob.glob(os.path.join(args.data, "bin/animations/*"))))
    print("maps      :", len(iter_map_files(xdb, only_map_descs=True)), "AdvMapDesc")
    print("  (other .xdb under Maps/ are sounds, statics, textures and tags)")
    if loc:
        print("text files:", sum(1 for _ in loc.iter_files()))
    return 0


def cmd_gamedata(args) -> int:
    xdb = XDB(args.data)
    loc = Localization(args.texts) if args.texts else None
    data = export_all(xdb, loc)
    for key, payload in data.items():
        path = _out(args.out, "Game", key + ".json")
        _write_json(path, payload)
        n = len(payload) if hasattr(payload, "__len__") else 0
        print(f"{key:15} {n:6} -> {os.path.relpath(path, args.out)}")
    if loc:
        table = loc.dump_all()
        path = _out(args.out, "Game", "Localization.json")
        _write_json(path, table)
        print(f"{'localization':15} {len(table):6} -> Game/Localization.json")
    return 0


def cmd_maps(args) -> int:
    xdb = XDB(args.data)
    files = _limited(sorted(iter_map_files(xdb, only_map_descs=True)), args.limit)
    ids = unique_map_ids(files)
    total_objects = 0
    index = []
    for path in files:
        try:
            md = load_map(xdb, path, resolve_models=not getattr(args, "fast", False))
        except Exception as exc:  # a broken map must not stop the batch
            print(f"  SKIP {path}: {exc}")
            continue
        name = ids.get(path) or md.map_id
        _write_json(_out(args.out, "Maps", name + ".json"), md.to_json())
        total_objects += len(md.objects)
        index.append({"id": name, "tileX": md.tile_x, "tileY": md.tile_y,
                      "objects": len(md.objects), "counts": md.by_kind()})
    _write_json(_out(args.out, "Maps", "_index.json"), index)
    print(f"maps: {len(index)} written, {total_objects} objects")
    return 0


def cmd_meshes(args) -> int:
    import glob
    import re

    from .container import ContainerError

    files = _limited(sorted(glob.glob(os.path.join(args.data, "bin/Geometries/*"))), args.limit)
    ok = skipped = 0
    for path in files:
        uid = os.path.basename(path)
        try:
            geom = decode_geometry(path)
            if not any(g.triangles for m in geom.meshes for g in m.groups):
                skipped += 1
                continue
            blob = geometry_to_glb(geom, name=uid)
        except (ContainerError, ValueError, IndexError, struct.error) as exc:
            skipped += 1
            if args.verbose:
                print(f"  SKIP {uid}: {exc}")
            continue
        with open(_out(args.out, "Meshes", uid + ".glb"), "wb") as fh:
            fh.write(blob)
        ok += 1
    print(f"meshes: {ok} written, {skipped} skipped (of {len(files)})")
    return 0


def cmd_textures(args) -> int:
    import glob

    files = _limited(sorted(glob.glob(os.path.join(args.data, "**", "*.dds"), recursive=True)), args.limit)
    ok = bad = 0
    manifest = {}
    renamed = 0
    for path in files:
        rel = os.path.relpath(path, args.data).replace("\\", "/")
        asset = safe_relative_path(rel)
        if asset != rel:
            renamed += 1
        try:
            w, h = dds_to_png(path, _out(args.out, "Textures", asset + ".png"))
        except (DDSError, OSError) as exc:
            bad += 1
            if args.verbose:
                print(f"  SKIP {rel}: {exc}")
            continue
        # Keyed by the source path, so a material can still find its texture.
        manifest["/" + rel] = {"width": w, "height": h, "png": asset + ".png"}
        ok += 1
    _write_json(_out(args.out, "Textures", "_manifest.json"), manifest)
    if renamed:
        print(f"  ({renamed} renamed to satisfy Unity's asset-name rules)")
    print(f"textures: {ok} written, {bad} failed (of {len(files)})")
    return 0


def _export_layers(args, terr, map_dir: str, plane: str, tile_db) -> None:
    """Write the painted ground textures and their per-vertex coverage.

    Two files: a JSON descriptor naming each layer's texture, and the raw V x V
    coverage planes concatenated, one per layer, in the same order.
    """
    from .xdb import advmap_tile

    entries = []
    blob = bytearray()
    for layer in terr.layers:
        info = advmap_tile(tile_db, layer.href) or {}
        entries.append({
            "tile": layer.href,
            "texture": info.get("texture"),
            "priority": info.get("priority", 0),
            "type": info.get("type", ""),
            "opaque": round(sum(1 for b in layer.weights if b > 127) / max(layer.count, 1), 4),
        })
        blob += layer.weights[: layer.count]
    _write_json(_out(args.out, "Terrain", map_dir, plane + ".layers.json"), {
        "map": map_dir, "plane": plane,
        "vertexStride": terr.layers[0].width if terr.layers else 0,
        "layers": entries,
    })
    with open(_out(args.out, "Terrain", map_dir, plane + ".layers"), "wb") as fh:
        fh.write(bytes(blob))


def cmd_terrain(args) -> int:
    import glob

    from .xdb import XDB
    tile_db = XDB(args.data)

    files = _limited(sorted(
        glob.glob(os.path.join(args.data, "Maps/**/GroundTerrain.bin"), recursive=True) +
        glob.glob(os.path.join(args.data, "Maps/**/UndergroundTerrain.bin"), recursive=True)
    ), args.limit)
    ok = bad = 0
    for path in files:
        try:
            terr = decode_terrain(path)
            if terr.height is None:
                bad += 1
                continue
        except Exception as exc:
            bad += 1
            print(f"  SKIP {path}: {exc}")
            continue
        map_dir = os.path.basename(os.path.dirname(path))
        plane = os.path.basename(path).replace(".bin", "")
        lo, hi, mean = terr.height_stats()
        _write_json(_out(args.out, "Terrain", map_dir, plane + ".json"), {
            "map": map_dir, "plane": plane, "tileX": terr.tile_x, "tileY": terr.tile_y,
            "vertexCount": terr.vertex_count,
            "heightRange": [round(lo, 4), round(hi, 4), round(mean, 4)],
            "flags": {str(k): v for k, v in sorted(terr.flag_histogram().items())},
            "waterVertices": terr.water_vertex_count(),
            "hasPassability": terr.passability is not None,
            "riverGrid": terr.river.width if terr.river else 0,
        })
        # Raw planes as compact binary the runtime can mmap straight into a mesh.
        with open(_out(args.out, "Terrain", map_dir, plane + ".heights"), "wb") as fh:
            fh.write(terr.height.data[: terr.height.count * 4])
        if terr.flags is not None:
            with open(_out(args.out, "Terrain", map_dir, plane + ".flags"), "wb") as fh:
                fh.write(terr.flags.data[: terr.flags.count])
        if terr.passability is not None:
            with open(_out(args.out, "Terrain", map_dir, plane + ".passability"), "wb") as fh:
                fh.write(terr.passability.data[: terr.passability.count])
        if terr.layers:
            try:
                _export_layers(args, terr, map_dir, plane, tile_db)
            except Exception as exc:
                print(f"  WARN layers {map_dir}/{plane}: {exc}")
        ok += 1
    print(f"terrain: {ok} written, {bad} failed (of {len(files)})")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="homm5import")
    sub = p.add_subparsers(dest="cmd", required=True)

    def common(sp):
        sp.add_argument("--data", help="game data root (unpacked data.pak)")
        sp.add_argument("--texts", help="texts root (unpacked texts.pak)")
        sp.add_argument("-o", "--out", default="out", help="output directory")
        sp.add_argument("--limit", type=int, default=None)
        sp.add_argument("-v", "--verbose", action="store_true")

    for name, fn in (("info", cmd_info), ("gamedata", cmd_gamedata), ("maps", cmd_maps),
                     ("meshes", cmd_meshes), ("textures", cmd_textures), ("terrain", cmd_terrain)):
        sp = sub.add_parser(name)
        common(sp)
        sp.set_defaults(func=fn)
    sp = sub.add_parser("all")
    common(sp)
    sp.set_defaults(func=lambda a: _all(a))

    # --fast belongs to maps, and `all` reaches the same code path.
    for sp_name in ("maps", "all"):
        sub.choices[sp_name].add_argument("--fast", action="store_true",
                                          help="do not resolve Shared -> Model -> geometry")
    return p


def _all(args) -> int:
    for fn in (cmd_gamedata, cmd_terrain, cmd_maps, cmd_meshes, cmd_textures):
        rc = fn(args)
        if rc:
            return rc
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    data, texts = _resolve_roots(getattr(args, "data", None), getattr(args, "texts", None))
    args.data, args.texts = data, texts
    if not data:
        print("error: no game data found. Pass --data <path to the unpacked data folder>", file=sys.stderr)
        return 2
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
