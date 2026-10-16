"""Adventure map descriptors (``AdvMapDesc``) -> flat JSON the runtime can load.

A map file lists its objects inline.  Each one carries a position in tiles, a
floor index (0 = underground, 1 = surface) and a ``Shared`` reference to the
object's shared record, which in turn points at the ``Model`` -- and the model
holds the geometry UID and the material list.  Resolving that chain here means
the Unity side never has to touch the XML.
"""

from __future__ import annotations

import os
import re
from typing import Dict, List, Optional, Tuple
from xml.etree import ElementTree as ET

from .xdb import XDB

OBJECT_TYPES = (
    "AdvMapStatic", "AdvMapTreasure", "AdvMapMonster", "AdvMapBuilding",
    "AdvMapArtifact", "AdvMapMine", "AdvMapShrine", "AdvMapTown",
    "AdvMapDwelling", "AdvMapShipyard", "AdvMapAbanMine", "AdvMapPrison",
    "AdvMapTent", "AdvMapSign", "AdvMapGarrison", "AdvMapHero", "AdvMapSphinx",
)


class MapObject:
    __slots__ = ("kind", "pos", "rot", "floor", "name", "shared_href",
                 "geometry_uid", "textures", "blocked", "extra")

    def __init__(self, kind: str, pos: Tuple[float, float, float], rot: float,
                 floor: int, name: str):
        self.kind = kind
        self.pos = pos
        self.rot = rot
        self.floor = floor
        self.name = name
        self.shared_href: Optional[str] = None
        self.geometry_uid: Optional[str] = None
        self.textures: List[str] = []
        self.blocked: List[List[int]] = []
        self.extra: Dict[str, object] = {}

    def to_json(self) -> dict:
        d = {
            "kind": self.kind,
            "x": round(self.pos[0], 4),
            "y": round(self.pos[1], 4),
            "z": round(self.pos[2], 4),
            "rot": round(self.rot, 4),
            "floor": self.floor,
        }
        if self.name:
            d["name"] = self.name
        if self.shared_href:
            d["shared"] = self.shared_href
        if self.geometry_uid:
            d["geometryUid"] = self.geometry_uid
        if self.textures:
            d["textures"] = self.textures
        if self.blocked:
            d["blocked"] = self.blocked
        if self.extra:
            d.update(self.extra)
        return d


def map_id(path: str) -> str:
    """Maps ship as ``<Name>/map.xdb``, so the folder is the identity."""
    base = os.path.splitext(os.path.basename(path))[0]
    if base.lower() in ("map", "advmapdesc"):
        base = os.path.basename(os.path.dirname(path))
    return base


def is_map_desc(path: str) -> bool:
    """Only ``AdvMapDesc`` files are maps.

    ``data/Maps`` also holds MapScript records, ``map-tag`` files and thumbnail
    descriptors, which share the folder and would otherwise be imported as
    zero-sized maps.
    """
    try:
        with open(path, "rb") as fh:
            head = fh.read(4096)
    except OSError:
        return False
    m = re.search(rb"<(\w+)[\s>]", head.split(b"?>", 1)[-1])
    return bool(m) and m.group(1) == b"AdvMapDesc"


def unique_map_ids(paths) -> Dict[str, str]:
    """Assign each map a distinct id.

    ``Maps/Multiplayer/S1`` and ``Maps/SingleMissions/S1`` both reduce to ``S1``,
    so collisions get their parent folder as a prefix.  Without this the second
    of a pair silently overwrites the first on disk.
    """
    from collections import Counter

    base = {p: map_id(p) for p in paths}
    counts = Counter(base.values())
    out: Dict[str, str] = {}
    for p in paths:
        name = base[p]
        if counts[name] > 1:
            parent = os.path.basename(os.path.dirname(p))
            candidate = f"{parent}_{name}" if parent and parent != name else name
            # Still ambiguous: fall back to the path, which is unique by construction.
            if candidate in out.values() and out.get(p) != candidate:
                rel = os.path.normpath(os.path.dirname(p)).replace(os.sep, "_")
                candidate = f"{rel}_{name}".lstrip("_")
            name = candidate
        out[p] = name
    return out


class MapDesc:
    def __init__(self, source: str, tile_x: int, tile_y: int):
        self.map_id = map_id(source)
        self.source = source
        self.tile_x = tile_x
        self.tile_y = tile_y
        self.has_surface = True
        self.has_underground = False
        self.objects: List[MapObject] = []

    def by_kind(self) -> Dict[str, int]:
        out: Dict[str, int] = {}
        for o in self.objects:
            out[o.kind] = out.get(o.kind, 0) + 1
        return out

    def to_json(self) -> dict:
        return {
            "id": self.map_id,
            "source": self.source,
            "tileX": self.tile_x,
            "tileY": self.tile_y,
            "hasSurface": self.has_surface,
            "hasUnderground": self.has_underground,
            "objectCounts": self.by_kind(),
            "objects": [o.to_json() for o in self.objects],
        }


def _resolve_static(xdb: XDB, shared: ET.Element, obj: MapObject, cache: dict) -> None:
    """Follow Shared -> Model -> {geometry uid, materials}."""
    href = shared.get("href")
    if not href:
        return
    obj.shared_href = href
    if href in cache:
        obj.geometry_uid, obj.textures, obj.blocked = cache[href]
        return

    geom_uid: Optional[str] = None
    textures: List[str] = []
    node, shared_href = xdb.resolve(shared)
    if node is not None:
        for tag in ("blockedTiles", "BlockedTiles"):
            blocked = node.find(tag)
            if blocked is not None:
                # <Item><x>0</x><y>0</y></Item> -- the object's footprint offset.
                obj.blocked = [[int(xdb.number(i.find("x"))), int(xdb.number(i.find("y")))]
                               for i in xdb.items(blocked)]
                break
        model_elem = node.find("Model")
        model, model_href = xdb.resolve(model_elem, shared_href)
        if model is not None:
            geom = model.find("Geometry")
            if geom is not None:
                inner = xdb.resolve_field(geom) or geom
                geom_uid = xdb.text(inner.find("uid")) or None
            mats = model.find("Materials")
            if mats is not None:
                for item in xdb.items(mats):
                    mat = xdb.resolve_field(item, model_href) or item
                    if mat is None:
                        continue
                    # <Texture> points at a (Texture).xdb, whose <DestName> is
                    # the shipped .dds -- relative to that file, not to the data
                    # root.  Reading <FileName> here found nothing: the field is
                    # called DestName, so every object came out untextured.
                    tex, tex_href = xdb.resolve(mat.find("Texture"), model_href)
                    if tex is None:
                        continue
                    dest = tex.find("DestName")
                    ref = dest.get("href") if dest is not None else None
                    rel = xdb.absolutize(ref, tex_href) if ref else xdb.text(dest)
                    if not rel:
                        rel = xdb.text(tex.find("FileName"))
                    if rel:
                        # Kept exactly as the texture manifest keys it.
                        textures.append("/" + rel.lstrip("/"))
    cache[href] = (geom_uid, textures, obj.blocked)
    obj.geometry_uid, obj.textures = geom_uid, textures


def load_map(xdb: XDB, path: str, resolve_models: bool = True) -> MapDesc:
    root = ET.parse(path).getroot()
    md = MapDesc(source=xdb.href_from_path(path),
                 tile_x=int(xdb.number(root.find("TileX"))),
                 tile_y=int(xdb.number(root.find("TileY"))))
    md.map_id = map_id(path)
    md.has_surface = xdb.boolean(root.find("HasSurface"), True)
    md.has_underground = xdb.boolean(root.find("HasUnderground"), False)

    cache: dict = {}
    objects_node = root.find("objects")
    if objects_node is None:
        return md
    base_href = xdb.href_from_path(path)
    for item in xdb.items(objects_node):
        # Objects are either inline children or external references -- the duel
        # preset maps use the second form for every object they hold.
        inner = list(item)[0] if len(item) else None
        if inner is None:
            inner = xdb.resolve_field(item, base_href)
        if inner is None or inner.tag not in OBJECT_TYPES:
            continue
        pos = xdb.vec3(inner.find("Pos"))
        obj = MapObject(
            kind=inner.tag,
            pos=pos,
            rot=xdb.number(inner.find("Rot")),
            floor=int(xdb.number(inner.find("Floor"), 1)),
            name=xdb.text(inner.find("Name")),
        )
        shared = inner.find("Shared")
        if shared is not None and shared.get("href"):
            if resolve_models:
                _resolve_static(xdb, shared, obj, cache)
            else:
                obj.shared_href = shared.get("href")

        # Type specific payloads worth carrying across.
        if inner.tag == "AdvMapMonster":
            obj.extra["creature"] = xdb.text(inner.find("Type")) or xdb.text(inner.find("Creature"))
            obj.extra["amount"] = int(xdb.number(inner.find("Amount")))
            obj.extra["customAmount"] = xdb.boolean(inner.find("CustomAmount"))
            obj.extra["attackType"] = xdb.text(inner.find("AttackType"))
            obj.extra["mood"] = xdb.text(inner.find("Mood"))
            stacks = [
                {"creature": xdb.text(s.find("Creature")),
                 "amount": int(xdb.number(s.find("Amount"))),
                 "customAmount": xdb.boolean(s.find("CustomAmount"))}
                for s in xdb.items(inner.find("AdditionalStacks"))
            ]
            if stacks:
                obj.extra["additionalStacks"] = stacks
        elif inner.tag == "AdvMapTown":
            obj.extra["townType"] = xdb.text(inner.find("TownType")) or xdb.text(inner.find("Type"))
            obj.extra["owner"] = xdb.text(inner.find("Owner"))
            obj.extra["name"] = xdb.text(inner.find("TownName")) or obj.name
        elif inner.tag == "AdvMapHero":
            obj.extra["hero"] = xdb.text(inner.find("Hero")) or xdb.text(inner.find("Type"))
            obj.extra["owner"] = xdb.text(inner.find("Owner"))
            obj.extra["army"] = [
                {"creature": xdb.text(s.find("Creature")), "count": int(xdb.number(s.find("Count")))}
                for s in xdb.items(inner.find("Army"))
            ]
        elif inner.tag == "AdvMapMine":
            obj.extra["resource"] = xdb.text(inner.find("Resource")) or xdb.text(inner.find("Type"))
            obj.extra["owner"] = xdb.text(inner.find("Owner"))
        elif inner.tag == "AdvMapArtifact":
            obj.extra["artifact"] = xdb.text(inner.find("Artifact")) or xdb.text(inner.find("Type"))
        elif inner.tag == "AdvMapTreasure":
            obj.extra["guardType"] = xdb.text(inner.find("GuardType"))
        md.objects.append(obj)
    return md


def iter_map_files(xdb: XDB, only_map_descs: bool = False) -> List[str]:
    """Every ``AdvMapDesc`` file: scenario, multiplayer, single missions, arenas."""
    out = []
    for sub in ("Maps/Scenario", "Maps/Multiplayer", "Maps/SingleMissions",
                "Maps/CombatArenas", "Maps/DuelMode"):
        for p in xdb.iter_files(sub):
            if only_map_descs and not is_map_desc(p):
                continue
            out.append(p)
    return out
