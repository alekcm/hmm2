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
from .gr2 import parse_gr2_bytes
from .localization import Localization
from .maps import iter_map_files, load_map, unique_map_ids
from .terrain import decode_file as decode_terrain
from .xdb import XDB


def _find_root(explicit: Optional[str], names: List[str]) -> Optional[str]:
    if explicit:
        explicit_abs = os.path.abspath(explicit)
        # The public CLI accepts either the unpacked game root or its `data`
        # directory.  Normalize the latter before main() appends `data`/`texts`.
        if os.path.basename(explicit_abs).lower() == "data":
            return os.path.dirname(explicit_abs)
        return explicit_abs
    here = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
    for cand in [here, os.getcwd(), os.path.dirname(here)]:
        if all(os.path.isdir(os.path.join(cand, n)) for n in names):
            return cand
    return None


def _out(out_dir: str, *parts: str) -> str:
    p = os.path.join(out_dir, *parts)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


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

    # XDB resolution gives a sibling a2p1-data overlay precedence over data.
    # The texture stage must enumerate the same roots, otherwise a patched
    # material can resolve successfully but its image is absent from the Unity
    # output.
    roots = [args.data]
    patch = os.path.join(os.path.dirname(os.path.abspath(args.data)), "a2p1-data")
    if os.path.isdir(patch):
        roots.insert(0, patch)
    files = []
    root_for_path = {}
    seen = set()
    for root in roots:
        for path in sorted(glob.glob(os.path.join(root, "**", "*.dds"), recursive=True)):
            rel = os.path.relpath(path, root).replace("\\", "/")
            if rel in seen:
                continue
            seen.add(rel)
            files.append(path)
            root_for_path[path] = root
    files = _limited(files, args.limit)
    ok = bad = 0
    manifest = {}
    for path in files:
        root = root_for_path[path]
        rel = os.path.relpath(path, root).replace("\\", "/")
        try:
            w, h = dds_to_png(path, _out(args.out, "Textures", rel + ".png"))
        except (DDSError, OSError) as exc:
            bad += 1
            if args.verbose:
                print(f"  SKIP {rel}: {exc}")
            continue
        manifest["/" + rel] = {"width": w, "height": h, "png": rel + ".png"}
        ok += 1
    _write_json(_out(args.out, "Textures", "_manifest.json"), manifest)
    print(f"textures: {ok} written, {bad} failed (of {len(files)})")
    return 0


def cmd_terrain(args) -> int:
    import glob

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
        ok += 1
    print(f"terrain: {ok} written, {bad} failed (of {len(files)})")
    return 0


def cmd_skeletons(args) -> int:
    import glob

    files = _limited(sorted(glob.glob(os.path.join(args.data, "bin/Skeletons/*"))), args.limit)
    ok = bad = 0
    for path in files:
        uid = os.path.basename(path)
        try:
            with open(path, "rb") as fh:
                gr2 = parse_gr2_bytes(fh.read())
            if not gr2.skeletons:
                bad += 1
                continue
        except Exception as exc:
            bad += 1
            if args.verbose:
                print(f"  SKIP {uid}: {exc}")
            continue
        _write_json(
            _out(args.out, "Skeletons", uid + ".json"),
            {"uid": uid, "skeletons": [s.to_dict() for s in gr2.skeletons]},
        )
        ok += 1
    print(f"skeletons: {ok} written, {bad} failed (of {len(files)})")
    return 0


def cmd_animations(args) -> int:
    import glob

    files = _limited(sorted(glob.glob(os.path.join(args.data, "bin/animations/*"))), args.limit)
    ok = bad = 0
    for path in files:
        uid = os.path.basename(path)
        try:
            with open(path, "rb") as fh:
                gr2 = parse_gr2_bytes(fh.read())
            if not gr2.animations:
                bad += 1
                continue
        except Exception as exc:
            bad += 1
            if args.verbose:
                print(f"  SKIP {uid}: {exc}")
            continue
        _write_json(
            _out(args.out, "Animations", uid + ".json"),
            {"uid": uid, "animations": [a.to_dict() for a in gr2.animations]},
        )
        ok += 1
    print(f"animations: {ok} written, {bad} failed (of {len(files)})")
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

    for name, fn in (
        ("info", cmd_info),
        ("gamedata", cmd_gamedata),
        ("maps", cmd_maps),
        ("meshes", cmd_meshes),
        ("textures", cmd_textures),
        ("terrain", cmd_terrain),
        ("skeletons", cmd_skeletons),
        ("animations", cmd_animations),
    ):
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
    root = _find_root(getattr(args, "data", None), ["data", "texts"])
    if root:
        args.data = os.path.join(root, "data")
        args.texts = os.path.join(root, "texts")
    if not getattr(args, "data", None) or not os.path.isdir(args.data):
        print("error: no game data root found; pass --data <path to unpacked data.pak>", file=sys.stderr)
        return 2
    if not getattr(args, "texts", None) or not os.path.isdir(args.texts):
        args.texts = None
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
