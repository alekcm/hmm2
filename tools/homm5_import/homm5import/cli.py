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
import time
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


def _progress(label: str, total: int, every: float = 2.0):
    """A reporter that prints at most once every ``every`` seconds.

    The slow stages run for minutes without a natural line of output, and on a
    Windows console silence is indistinguishable from a hang -- which is exactly
    how the animation stage used to be reported.  Writing progress keeps the run
    visibly alive.  The line is plain ASCII and flushed so it shows up even when
    the console output is redirected.
    """
    state = {"last": time.monotonic()}

    def report(done: int) -> None:
        now = time.monotonic()
        if now - state["last"] < every:
            return
        state["last"] = now
        pct = (100.0 * done / total) if total else 100.0
        print("  %s %d/%d (%.0f%%)" % (label, done, total, pct), flush=True)

    return report


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

    # What an earlier run left behind.  This is the quick way to answer "did
    # the import actually write the textures?" without opening a file manager.
    out = os.path.abspath(args.out)
    print()
    print("output    :", out)
    if not os.path.isdir(out):
        print("  (not created yet -- run the import)")
        return 0

    def count(*parts, suffix="*"):
        return len(glob.glob(os.path.join(out, *parts, suffix), recursive=True))

    print("  textures %d .png, meshes %d .glb, maps %d, "
          "terrain %d .heights, animations %d"
          % (count("Textures", suffix="**/*.png"),
             count("Meshes", suffix="*.glb"),
             len([p for p in glob.glob(os.path.join(out, "Maps", "*.json"))
                  if os.path.basename(p) != "_index.json"]),
             count("Terrain", suffix="**/*.heights"),
             count("Animations", suffix="*.json.gz")))
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
    progress = _progress("maps", len(files))
    for i, path in enumerate(files, 1):
        progress(i)
        try:
            md = load_map(xdb, path, resolve_models=not getattr(args, "fast", False))
        except Exception as exc:  # a broken map must not stop the batch
            print(f"  SKIP {path}: {exc}")
            continue
        name = ids.get(path) or md.map_id
        _write_json(_out(args.out, "Maps", name + ".json"), md.to_json())
        total_objects += len(md.objects)
        index.append({"id": name, "tileX": md.tile_x, "tileY": md.tile_y,
                      "objects": len(md.objects), "counts": md.by_kind(),
                      # Combat arenas double as battle scenery; the runtime
                      # picks one when a fight starts.
                      "arena": "CombatArenas" in path.replace("\\", "/")})
    _write_json(_out(args.out, "Maps", "_index.json"), index)
    print(f"maps: {len(index)} written, {total_objects} objects")
    return 0


def cmd_meshes(args) -> int:
    import glob
    import re

    from .container import ContainerError

    files = _limited(sorted(glob.glob(os.path.join(args.data, "bin/Geometries/*"))), args.limit)
    ok = skipped = 0
    progress = _progress("meshes", len(files))
    for i, path in enumerate(files, 1):
        progress(i)
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


def cmd_ui(args) -> int:
    from .ui import export_ui
    skin = export_ui(args.data, args.out)
    print("ui: %d native images, %d window layouts -> UI/skin.json" %
          (len(skin["assets"]), len(skin["windows"])))
    return 0


def cmd_textures(args) -> int:
    import glob

    files = _limited(sorted(glob.glob(os.path.join(args.data, "**", "*.dds"), recursive=True)), args.limit)
    ok = bad = 0
    manifest = {}
    renamed = 0
    progress = _progress("textures", len(files))
    for i, path in enumerate(files, 1):
        progress(i)
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


def _round5(o):
    """Round floats to 5 dp. Animation data does not need more, and it is the
    difference between 153 KB and 45 KB a file once gzipped."""
    if isinstance(o, float):
        return round(o, 5)
    if isinstance(o, list):
        return [_round5(x) for x in o]
    if isinstance(o, dict):
        return {k: _round5(v) for k, v in o.items()}
    return o


def cmd_animations(args) -> int:
    """Export the stored (uncompressed) GR2 animation files.

    An animation file carries its own copy of the skeleton it animates, so this
    yields bones, rest pose and curves in one pass. The 577 files that pack
    their sections with Oodle1 are skipped and counted: that codec is not
    implemented.
    """
    import glob
    import gzip

    from .gr2 import GR2, GR2Error, extract

    files = _limited(sorted(glob.glob(os.path.join(args.data, "bin", "animations", "*"))),
                     args.limit)
    ok = packed = bad = 0
    index = {}
    progress = _progress("animations", len(files))
    for i, path in enumerate(files, 1):
        progress(i)
        uid = os.path.basename(path)
        try:
            with open(path, "rb") as fh:
                g = GR2(fh.read())
            if not g.is_stored():
                packed += 1
                continue
            data = extract(g.root())
        except (GR2Error, OSError) as exc:
            bad += 1
            if args.verbose:
                print(f"  SKIP {uid}: {exc}")
            continue

        clip = data["animations"][0] if data["animations"] else {}
        skel = data["skeletons"][0] if data["skeletons"] else {}
        index[uid] = {
            "source": data["source"],
            "name": (clip.get("name") or "").split("/")[-1],
            "duration": round(float(clip.get("duration") or 0.0), 5),
            "timeStep": round(float(clip.get("timeStep") or 0.0), 6),
            "bones": len(skel.get("bones") or []),
            "tracks": len(clip.get("tracks") or []),
        }
        payload = json.dumps(_round5(data), separators=(",", ":")).encode("utf8")
        with gzip.open(_out(args.out, "Animations", uid + ".json.gz"), "wb", 6) as fh:
            fh.write(payload)
        ok += 1

    _write_json(_out(args.out, "Animations", "_index.json"), index)
    print(f"animations: {ok} written, {packed} skipped (Oodle1-packed), "
          f"{bad} failed (of {len(files)})")
    return 0


def cmd_sounds(args) -> int:
    """Sound and music catalog, plus the audio files it names.

    Writes ``Game/sounds.json`` and copies the referenced files into ``Audio/``.
    Only referenced files are copied: the shipped ``soundsfx`` and ``music``
    trees hold more than the catalog uses (localized dialogue voices in
    ``sound/``, for one), and those are not part of this stage.
    """
    from .sounds import copy_audio, load_sound_catalog

    xdb = XDB(args.data)
    repo_root = os.path.dirname(os.path.abspath(args.data))
    sounds, music, report = load_sound_catalog(xdb, repo_root)
    _write_json(_out(args.out, "Game", "sounds.json"), {"sounds": sounds, "music": music})
    copied, skipped = copy_audio(repo_root, args.out, sounds, music)
    print(f"sounds: {len(sounds)} effects, {len(music)} music tracks; "
          f"{copied} files copied, {skipped} already present")
    dropped = {k: v for k, v in report.items() if v and ("Missing" in k or "Duplicate" in k)}
    if dropped:
        print("  dropped:", ", ".join(f"{k}={v}" for k, v in sorted(dropped.items())))
    return 0


def _terrain_ids(terrain_files, map_files, data_root):
    """Output folder name for every folder that carries a terrain plane.

    Folders that hold an ``AdvMapDesc`` get that map's collision-free id, which
    is what ``Maps/_index.json`` uses.  The rest -- hero-screen and mission
    arenas with no map descriptor -- fall back to their path under ``Maps/``
    rather than their basename: several ship as ``.../S1``, ``.../S2`` and
    ``.../S3`` and used to overwrite the real multiplayer maps ``S1``-``S3``
    (and each other) in the output tree.
    """
    ids = {}
    for path, mid in unique_map_ids(map_files).items():
        ids[os.path.normpath(os.path.abspath(os.path.dirname(path)))] = mid
    taken = set(ids.values())
    maps_root = os.path.join(os.path.abspath(data_root), "Maps")
    for path in terrain_files:
        folder = os.path.normpath(os.path.abspath(os.path.dirname(path)))
        if folder in ids:
            continue
        rel = os.path.relpath(folder, maps_root).replace(os.sep, "_")
        rel = "".join(c if c.isalnum() or c in "._-" else "_" for c in rel)
        name = rel
        n = 2
        while name in taken:
            name = "%s_%d" % (rel, n)
            n += 1
        taken.add(name)
        ids[folder] = name
    return ids


def cmd_terrain(args) -> int:
    import glob

    from .xdb import XDB
    tile_db = XDB(args.data)

    all_files = sorted(
        glob.glob(os.path.join(args.data, "Maps/**/GroundTerrain.bin"), recursive=True) +
        glob.glob(os.path.join(args.data, "Maps/**/UndergroundTerrain.bin"), recursive=True))
    files = _limited(all_files, args.limit)
    # Several shipped maps share a folder name (notably S1), and arena folders
    # carry such basenames without being maps at all.  Map JSON uses
    # collision-free ids from unique_map_ids; terrain must use the same identity
    # (plus a path-derived one for folders that are not maps) or one map
    # silently reads another map's height planes.
    map_files = iter_map_files(tile_db, only_map_descs=True)
    terrain_ids = _terrain_ids(all_files, map_files, args.data)
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
        parent = os.path.normpath(os.path.abspath(os.path.dirname(path)))
        map_dir = terrain_ids.get(parent, os.path.basename(parent))
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
                     ("meshes", cmd_meshes), ("textures", cmd_textures), ("terrain", cmd_terrain),
                     ("animations", cmd_animations), ("sounds", cmd_sounds), ("ui", cmd_ui)):
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
    stages = (("gamedata", cmd_gamedata), ("terrain", cmd_terrain),
              ("maps", cmd_maps), ("meshes", cmd_meshes),
              ("textures", cmd_textures), ("animations", cmd_animations),
              ("sounds", cmd_sounds), ("ui", cmd_ui))
    started = time.monotonic()
    for i, (name, fn) in enumerate(stages, 1):
        print("[%d/%d] %s" % (i, len(stages), name), flush=True)
        rc = fn(args)
        if rc:
            return rc
    out = os.path.abspath(args.out)
    print("done in %.0fs; output is %s" % (time.monotonic() - started, out),
          flush=True)
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
