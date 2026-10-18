"""Adventure map descriptors (``AdvMapDesc``) -> flat JSON the runtime can load.

A map file lists its objects inline. Each one carries a position in tiles, a
floor index (0 = ground/surface, 1 = underground, per the shipped Lua constants)
and a ``Shared`` reference to the
object's shared record, which in turn points at the ``Model`` -- and the model
holds the geometry UID and the material list.  Resolving that chain here means
the Unity side never has to touch the XML.
"""

from __future__ import annotations

import os
import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from xml.etree import ElementTree as ET

from .xdb import XDB, texture_dds_path

OBJECT_TYPES = (
    "AdvMapStatic", "AdvMapTreasure", "AdvMapMonster", "AdvMapBuilding",
    "AdvMapArtifact", "AdvMapMine", "AdvMapShrine", "AdvMapTown",
    "AdvMapDwelling", "AdvMapShipyard", "AdvMapAbanMine", "AdvMapPrison",
    "AdvMapTent", "AdvMapSign", "AdvMapGarrison", "AdvMapHero", "AdvMapSphinx",
    "AdvMapCartographer", "AdvMapHillFort", "AdvMapSeerHut", "AdvMapDwarvenWarren",
)


class MapObject:
    __slots__ = ("kind", "object_id", "pos", "rot", "floor", "name", "shared_href",
                 "geometry_uid", "textures", "materials", "blocked", "extra")

    def __init__(self, kind: str, pos: Tuple[float, float, float], rot: float,
                 floor: int, name: str, object_id: str = ""):
        self.kind = kind
        self.object_id = object_id
        self.pos = pos
        self.rot = rot
        self.floor = floor
        self.name = name
        self.shared_href: Optional[str] = None
        self.geometry_uid: Optional[str] = None
        self.textures: List[str] = []
        self.materials: List[dict] = []
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
        if self.object_id:
            d["objectId"] = self.object_id
        if self.name:
            d["name"] = self.name
        if self.shared_href:
            d["shared"] = self.shared_href
        if self.geometry_uid:
            d["geometryUid"] = self.geometry_uid
        if self.textures:
            d["textures"] = self.textures
        if self.materials:
            d["materials"] = self.materials
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


def _literal_fog_bootstrap(xdb: XDB, scenario_id: str,
                           map_source: Optional[str] = None) -> List[dict]:
    """Read only literal, top-level OpenCircleFog calls from the shipped Lua.

    Calls inside a function are event-driven and must not be executed at map
    load; the runtime handles those through its source-backed event bridge.
    Keeping this importer deliberately conservative is preferable to opening
    every scripted scene at scenario start.
    """
    # Follow the MapScript reference in the map descriptor instead of scanning
    # every ``*.lua`` beside it.  Scenario folders contain old debugging
    # captures (A2C1M5 has three such files); the native map executes only the
    # script named by this source reference.  XDB resolves an overlay record
    # before the base record, so the same call also handles a2p1-data.
    candidates = []
    if not map_source:
        # Preserve the public helper's old two-argument form without relaxing
        # the source rule: infer the descriptor path, then resolve its actual
        # MapScript reference below. There is deliberately no Lua filename
        # fallback here.
        for root_base in (getattr(xdb, "overlay_root", None), xdb.data_root):
            if not root_base:
                continue
            inferred = Path(root_base) / "Maps" / "Scenario" / scenario_id / "map.xdb"
            if inferred.is_file():
                map_source = str(inferred)
                break
    if map_source:
        map_href = xdb.href_from_path(map_source)
        map_root = xdb.load(map_href)
        if map_root is not None:
            for ref in map_root.iter("MapScript"):
                href = ref.get("href")
                if not href:
                    continue
                script_href = xdb.absolutize(href, map_href)
                script_root = xdb.load(script_href)
                if script_root is None:
                    continue
                filename = script_root.find("FileName")
                if filename is None or not filename.get("href"):
                    continue
                file_href = xdb.absolutize(filename.get("href"), script_href)
                path = Path(xdb._path_for(file_href.split("#", 1)[0]))
                if path.is_file() and path.suffix.lower() == ".lua":
                    if path not in candidates:
                        candidates.append(path)
    if not candidates:
        return []

    result = []
    depth = 0
    call = re.compile(
        r"OpenCircleFog\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*"
        r"(GROUND|UNDERGROUND|0|1)\s*,\s*(\d+)\s*,\s*(PLAYER_\d+|\d+)\s*\)",
        re.IGNORECASE,
    )

    def lua_block_delta(line):
        """Approximate Lua block nesting without treating ``while ... do`` as two blocks."""
        opens = (len(re.findall(r"\bfunction\b", line, re.I)) +
                 len(re.findall(r"\bif\b", line, re.I)) +
                 len(re.findall(r"\bfor\b", line, re.I)) +
                 len(re.findall(r"\bwhile\b", line, re.I)) +
                 len(re.findall(r"\brepeat\b", line, re.I)))
        control_do = len(re.findall(r"\b(if|for|while)\b[^;\n]*\bdo\b", line, re.I))
        standalone_do = max(0, len(re.findall(r"\bdo\b", line, re.I)) - control_do)
        closes = len(re.findall(r"\bend\b", line, re.I)) + len(re.findall(r"\buntil\b", line, re.I))
        return opens + standalone_do - closes

    def add_match(m):
        floor = 1 if m.group(3).upper() == "UNDERGROUND" or m.group(3) == "1" else 0
        player = m.group(5)
        if player.isdigit(): player = "PLAYER_" + player
        entry = {"x": int(m.group(1)), "y": int(m.group(2)), "floor": floor,
                 "radius": int(m.group(4)), "player": player}
        if entry not in result: result.append(entry)

    for path in candidates:
        try:
            lines = path.read_text(encoding="utf-8", errors="ignore").splitlines()
        except OSError:
            continue
        clean = [line.split("--", 1)[0] for line in lines]
        startup_calls = {"mission_setup", "f_open_fog_start"}
        depth = 0
        for line in clean:
            if not line.strip():
                continue
            # A map script's top-level statements run during load. Function,
            # if, loop and do blocks are intentionally not treated as load-time.
            if depth == 0:
                m = call.search(line)
                if m:
                    add_match(m)
                # A top-level startThread is also a load-time entry point. We
                # inspect only the called function's straight-line prefix below;
                # code after sleep/branch/loop remains event-driven.
                thread = re.search(r"\bstartThread\s*\(\s*([A-Za-z_]\w*)\b", line, re.I)
                if thread:
                    startup_calls.add(thread.group(1))
                if (not re.search(r"\bfunction\s+mission_setup\b", line, re.I) and
                        re.search(r"\bmission_setup\s*\(", line, re.I)):
                    startup_calls.add("mission_setup")
            depth = max(0, depth + lua_block_delta(line))

        # Follow only verified load-time entry points.  A startup thread's
        # straight-line prefix executes immediately, but a fog call after a
        # sleep, branch or loop belongs to a later event/cutscene and must not
        # be flattened into static scenario bootstrap data.
        for name in startup_calls:
            inside = False
            fn_depth = 0
            blocked = False
            for line in clean:
                if not inside:
                    if re.search(r"\bfunction\s+" + re.escape(name) + r"\s*\(", line, re.I):
                        inside = True
                        fn_depth = 1
                    continue
                if fn_depth == 1 and not blocked:
                    if re.search(r"\b(sleep|if|for|while|repeat|do)\b", line, re.I):
                        blocked = True
                    else:
                        m = call.search(line)
                        if m:
                            add_match(m)
                fn_depth += lua_block_delta(line)
                if fn_depth <= 0:
                    inside = False
                    blocked = False
    return result


class MapDesc:
    def __init__(self, source: str, tile_x: int, tile_y: int):
        self.map_id = map_id(source)
        self.source = source
        self.tile_x = tile_x
        self.tile_y = tile_y
        self.initial_floor = 0
        self.has_surface = True
        self.has_underground = False
        self.fog_bootstrap: List[dict] = []
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
            "initialFloor": self.initial_floor,
            "hasSurface": self.has_surface,
            "hasUnderground": self.has_underground,
            "fogBootstrap": self.fog_bootstrap,
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
        obj.geometry_uid, obj.textures, obj.materials, obj.blocked, town_type = cache[href]
        if town_type:
            obj.extra["townType"] = town_type
        return

    geom_uid: Optional[str] = None
    textures: List[str] = []
    materials: List[dict] = []
    town_type: Optional[str] = None
    node, shared_href = xdb.resolve(shared)
    if node is not None:
        # Town map instances point at a shared town record, but the town model
        # is selected by its faction record's inline Exterior.upgrades list.
        # AdvMapTownShared.Model is empty for the real faction towns, while the
        # first exterior upgrade is the actual level-one town model. RandomTown
        # deliberately has only a placeholder: its faction is chosen at map
        # load time and is not encoded in the map descriptor.
        town_type = xdb.text(node.find("Type")) if node.tag == "AdvMapTownShared" else None
        # The random-town record's enum is a marker, not a faction. Preserve the
        # existing contract: do not claim a race the map never specifies.
        if town_type == "TOWN_RANDOM_TYPE":
            town_type = None
        if town_type:
            obj.extra["townType"] = town_type
        for tag in ("blockedTiles", "BlockedTiles"):
            blocked = node.find(tag)
            if blocked is not None:
                # <Item><x>0</x><y>0</y></Item> -- the object's footprint offset.
                obj.blocked = [[int(xdb.number(i.find("x"))), int(xdb.number(i.find("y")))]
                               for i in xdb.items(blocked)]
                break
        model_elem = node.find("Model")
        model, model_href = xdb.resolve(model_elem, shared_href)

        # Most shared records point straight to a model. A faction town stores
        # its exterior models under Exterior/AdvMapTownExterior/upgrades instead.
        # Use the first (level-one town) model when the direct field is empty.
        if model is None and node.tag == "AdvMapTownShared":
            exterior = node.find("Exterior")
            exterior = (list(exterior)[0] if exterior is not None and len(exterior)
                        else None)
            upgrades = exterior.find("upgrades") if exterior is not None else None
            upgrade_items = xdb.items(upgrades)
            if upgrade_items:
                model, model_href = xdb.resolve(upgrade_items[0].find("Model"), shared_href)

        if model is not None:
            geom = model.find("Geometry")
            if geom is not None:
                # Geometry hrefs inside a model are often relative to that
                # model file (e.g. Rail-geom.xdb), not to the data root.
                inner = xdb.resolve_field(geom, model_href) or geom
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
                    rel = texture_dds_path(xdb, tex, tex_href)
                    bump, bump_href = xdb.resolve(mat.find("Bump"), model_href)
                    materials.append({
                        "texture": rel,
                        "bump": texture_dds_path(xdb, bump, bump_href),
                        # AM_ALPHA_TEST is the common case (5244 of 11638
                        # materials): foliage, grates and railings that must punch
                        # holes rather than render as solid quads.
                        "alphaMode": XDB.text(mat.find("AlphaMode"), "AM_OPAQUE"),
                        "twoSided": XDB.boolean(mat.find("Is2Sided")),
                        "selfIllum": XDB.text(mat.find("LightingMode")) == "L_SELFILLUM",
                        "spec": round(XDB.number(mat.find("SpecFactor")), 4),
                        "gloss": round(XDB.number(mat.find("Gloss")), 4),
                    })
                    if rel:
                        textures.append(rel)
    cache[href] = (geom_uid, textures, materials, obj.blocked, town_type)
    obj.geometry_uid, obj.textures, obj.materials = geom_uid, textures, materials


def load_map(xdb: XDB, path: str, resolve_models: bool = True) -> MapDesc:
    logical_source = xdb.href_from_path(path)
    # ``path`` is normally the base-tree representative returned by
    # iter_map_files; resolve it once more so a same-named a2p1 map is the
    # record that is actually imported.
    root = ET.parse(xdb._path_for(logical_source)).getroot()
    md = MapDesc(source=logical_source,
                 tile_x=int(xdb.number(root.find("TileX"))),
                 tile_y=int(xdb.number(root.find("TileY"))))
    md.map_id = map_id(path)
    md.initial_floor = int(xdb.number(root.find("InitialFloor"), 0))
    md.has_surface = xdb.boolean(root.find("HasSurface"), True)
    md.has_underground = xdb.boolean(root.find("HasUnderground"), False)
    md.fog_bootstrap = _literal_fog_bootstrap(xdb, md.map_id, path)

    cache: dict = {}
    objects_node = root.find("objects")
    if objects_node is None:
        return md
    base_href = logical_source
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
            floor=int(xdb.number(inner.find("Floor"), 0)),
            name=xdb.text(inner.find("Name")),
            object_id=item.get("id", ""),
        )
        shared = inner.find("Shared")
        shared_node = None
        if shared is not None and shared.get("href"):
            shared_node, _shared_path = xdb.resolve(shared, base_href)
            if resolve_models:
                _resolve_static(xdb, shared, obj, cache)
            else:
                obj.shared_href = shared.get("href")
            if shared_node is not None:
                shared_type = xdb.text(shared_node.find("Type"))
                if shared_type:
                    obj.extra["sharedType"] = shared_type
                # Which tiles trigger a visit. Every shared record carries the
                # list; the runtime walks onto one of these offsets to interact,
                # since the blocked footprint itself is impassable.
                active = [
                    [int(xdb.number(i.find("x"))), int(xdb.number(i.find("y")))]
                    for i in xdb.items(shared_node.find("activeTiles"))
                ]
                if active:
                    obj.extra["activeTiles"] = active
                # Treasure amounts: the roll the engine makes when the pile is
                # picked up. Units differ per treasure kind (see the runtime's
                # H5ObjectVisits), so carry the raw roll range across unchanged.
                mn = xdb.number(shared_node.find("MinResource"), 0)
                mx = xdb.number(shared_node.find("MaxResource"), 0)
                if mn or mx:
                    obj.extra["minResource"] = int(mn)
                    obj.extra["maxResource"] = int(mx)

        # Type specific payloads worth carrying across.
        if inner.tag == "AdvMapMonster":
            obj.extra["creature"] = (xdb.text(inner.find("Type")) or
                                      xdb.text(inner.find("Creature")) or
                                      (xdb.text(shared_node.find("Creature")) if shared_node is not None else ""))
            obj.extra["amount"] = int(xdb.number(inner.find("Amount")))
            obj.extra["amount2"] = int(xdb.number(inner.find("Amount2"), obj.extra["amount"]))
            obj.extra["customAmount"] = xdb.boolean(inner.find("CustomAmount"),
                                                     xdb.boolean(inner.find("Custom")))
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
            # There is no TownType/Owner/TownName on an AdvMapTown. The owner is
            # PlayerID, the name is plain Name, and the faction is NOT in the map
            # at all: a town's shared definition is RandomTown, whose model is a
            # flat 18x18 marker under _(Model)/TESTS/. The game picks the race at
            # load time, constrained by BannedRaces. So the castle cannot be
            # rendered from the map alone -- what the map does carry is who owns
            # it, what is built and what garrisons it.
            obj.extra["owner"] = xdb.text(inner.find("PlayerID"))
            obj.extra["buildings"] = [
                {"type": xdb.text(b.find("Type")),
                 "upgrade": int(xdb.number(b.find("InitialUpgrade")))
                 if xdb.text(b.find("InitialUpgrade")).isdigit()
                 else xdb.text(b.find("InitialUpgrade")),
                 # MaxUpgrade caps what the town screen may build here: a
                 # scenario can forbid the Castle tier of a town's Fort by
                 # shipping MaxUpgrade below the record's own tier.
                 "maxUpgrade": xdb.text(b.find("MaxUpgrade"))}
                for b in xdb.items(inner.find("buildings"))
            ]
            obj.extra["garrison"] = [
                {"creature": xdb.text(a.find("Creature")),
                 "amount": int(xdb.number(a.find("Amount")))}
                for a in xdb.items(inner.find("armySlots"))
                if xdb.text(a.find("Creature"))
            ]
            garrison_hero = xdb.text(inner.find("GarrisonHero"))
            if garrison_hero:
                obj.extra["garrisonHero"] = garrison_hero
            banned = [xdb.text(r) for r in xdb.items(inner.find("BannedRaces"))]
            if banned:
                obj.extra["bannedRaces"] = banned
            if xdb.text(inner.find("Name")):
                obj.extra["name"] = xdb.text(inner.find("Name"))
        elif inner.tag == "AdvMapHero":
            obj.extra["hero"] = (xdb.text(inner.find("Hero")) or xdb.text(inner.find("Type")) or
                                 (xdb.text(shared_node.find("InternalName")) if shared_node is not None else ""))
            obj.extra["owner"] = xdb.text(inner.find("PlayerID")) or xdb.text(inner.find("Owner"))
            army_slots = xdb.items(inner.find("armySlots")) or xdb.items(inner.find("Army"))
            obj.extra["army"] = [
                {"creature": xdb.text(s.find("Creature")),
                 "count": int(xdb.number(s.find("Count"), xdb.number(s.find("Amount"))))}
                for s in army_slots if xdb.text(s.find("Creature"))
            ]
            # The Editable block carries the hero's level-up history: ranked
            # skills (SkillWithMastery), the perks picked along the way and the
            # spellbook. Mastery strings map to the numeric ranks of Mastery.xdb.
            mastery_rank = {"MASTERY_NONE": 0, "MASTERY_BASIC": 1, "MASTERY_ADVANCED": 2,
                            "MASTERY_EXPERT": 3, "MASTERY_EXTRA_EXPERT": 4}
            editable = inner.find("Editable")
            if editable is not None:
                skills = [
                    {"skill": xdb.text(s.find("SkillID")),
                     "mastery": mastery_rank.get(xdb.text(s.find("Mastery")), 0)}
                    for s in xdb.items(editable.find("skills"))
                    if xdb.text(s.find("SkillID")) not in ("", "HERO_SKILL_NONE")
                ]
                if skills:
                    obj.extra["skills"] = skills
                perks = [xdb.text(i) for i in xdb.items(editable.find("perkIDs"))
                         if xdb.text(i) and xdb.text(i) != "HERO_SKILL_NONE"]
                if perks:
                    obj.extra["perks"] = perks
                spells = [xdb.text(i) for i in xdb.items(editable.find("spellIDs"))
                          if xdb.text(i) and xdb.text(i) != "SPELL_NONE"]
                if spells:
                    obj.extra["spells"] = spells
                primary = {
                    "offence": int(xdb.number(editable.find("Offence"))),
                    "defence": int(xdb.number(editable.find("Defence"))),
                    "spellpower": int(xdb.number(editable.find("Spellpower"))),
                    "knowledge": int(xdb.number(editable.find("Knowledge"))),
                }
                if any(primary.values()):
                    obj.extra["primarySkills"] = primary
            experience = int(xdb.number(inner.find("Experience")))
            if experience:
                obj.extra["experience"] = experience
        elif inner.tag == "AdvMapMine":
            mine_type = (xdb.text(inner.find("Resource")) or xdb.text(inner.find("Type")) or
                         (xdb.text(shared_node.find("Type")) if shared_node is not None else ""))
            mine_resources = {
                "BUILDING_GOLD_MINE": "GOLD", "BUILDING_ORE_PIT": "ORE",
                "BUILDING_SAWMILL": "WOOD", "BUILDING_ALCHEMIST_LAB": "MERCURY",
                "BUILDING_CRYSTAL_CAVERN": "CRYSTAL", "BUILDING_SULFUR_DUNE": "SULFUR",
                "BUILDING_GEM_POND": "GEM",
            }
            obj.extra["resource"] = mine_resources.get(mine_type, mine_type)
            obj.extra["buildingType"] = mine_type
            obj.extra["owner"] = xdb.text(inner.find("PlayerID")) or xdb.text(inner.find("Owner"))
        elif inner.tag == "AdvMapArtifact":
            obj.extra["artifact"] = (xdb.text(inner.find("Artifact")) or
                                      xdb.text(inner.find("ArtifactID")) or
                                      xdb.text(inner.find("Type")) or
                                      (xdb.text(shared_node.find("ArtifactID")) if shared_node is not None else ""))
        elif inner.tag == "AdvMapTreasure":
            # A chest. What is inside comes from the shared definition; the
            # instance carries how much and whether the author overrode it.
            obj.extra["isCustom"] = xdb.boolean(inner.find("IsCustom"))
            obj.extra["amount"] = int(xdb.number(inner.find("Amount")))
            msg = xdb.text(inner.find("MessageFileRef"))
            if msg:
                obj.extra["message"] = msg
        elif inner.tag == "AdvMapDwelling":
            obj.extra["owner"] = xdb.text(inner.find("PlayerID"))
            obj.extra["randomCreatures"] = xdb.boolean(inner.find("RandomCreatures"))
            obj.extra["randomSource"] = xdb.text(inner.find("RndSource"))
            obj.extra["linkToPlayer"] = xdb.text(inner.find("LinkToPlayer"))
            obj.extra["linkToTown"] = xdb.text(inner.find("LinkToTown"))
            shared_creatures = xdb.items(shared_node.find("creatures")) if shared_node is not None else []
            obj.extra["creatures"] = [xdb.text(i) for i in shared_creatures if xdb.text(i)]
        elif inner.tag == "AdvMapBuilding":
            obj.extra["owner"] = xdb.text(inner.find("PlayerID"))
            obj.extra["groupId"] = int(xdb.number(inner.find("GroupID")))
        elif inner.tag == "AdvMapGarrison":
            obj.extra["owner"] = xdb.text(inner.find("PlayerID"))
            obj.extra["collectableArmy"] = xdb.boolean(inner.find("CollectableArmy"))
            obj.extra["allowQuickCombat"] = xdb.boolean(inner.find("AllowQuickCombat"), True)
            obj.extra["townType"] = xdb.text(inner.find("TownType"))
            obj.extra["garrison"] = [
                {"creature": xdb.text(a.find("Creature")),
                 "amount": int(xdb.number(a.find("Amount")))}
                for a in xdb.items(inner.find("armySlots"))
                if xdb.text(a.find("Creature"))
            ]
        elif inner.tag == "AdvMapAbanMine":
            obj.extra["availableResources"] = [int(xdb.number(i))
                                                for i in xdb.items(inner.find("AvailableResources"))]
        elif inner.tag == "AdvMapShipyard":
            ship_tile = inner.find("ShipTile")
            if ship_tile is not None:
                obj.extra["shipTile"] = {"x": int(xdb.number(ship_tile.find("x"))),
                                          "y": int(xdb.number(ship_tile.find("y")))}
        elif inner.tag == "AdvMapShrine":
            obj.extra["spell"] = xdb.text(inner.find("SpellID"))
            obj.extra["spellLevel"] = int(xdb.number(shared_node.find("Level"))) if shared_node is not None else 0
        elif inner.tag == "AdvMapPrison":
            obj.extra["randomHero"] = xdb.boolean(inner.find("RandomHero"))
            obj.extra["prisonedHero"] = xdb.text(inner.find("PrisonedHero"))
        elif inner.tag == "AdvMapSphinx":
            obj.extra["randomRiddle"] = xdb.boolean(inner.find("RandomRiddle"))
            obj.extra["riddle"] = xdb.text(inner.find("Riddle"))
        elif inner.tag == "AdvMapTent":
            color = xdb.text(shared_node.find("Color")) if shared_node is not None else ""
            obj.extra["keyColor"] = color
        elif inner.tag == "AdvMapSign":
            message = inner.find("MessageFileRef")
            if message is not None:
                obj.extra["message"] = message.get("href") or xdb.text(message)
        elif inner.tag == "AdvMapCartographer":
            obj.extra["cost"] = int(xdb.number(inner.find("Cost")))
        elif inner.tag == "AdvMapHillFort":
            filt = inner.find("CreaturesUpgradesFilter")
            if filt is not None:
                obj.extra["upgradeFilter"] = {
                    field: [xdb.text(v) for v in xdb.items(filt.find(field)) if xdb.text(v)]
                    for field in ("ForbiddenBasicUpgradeTiers", "ForbiddenAlterUpgradeTiers",
                                  "NotUpgradeable", "ForbiddenUpgrades")
                }
        elif inner.tag == "AdvMapDwarvenWarren":
            obj.extra["owner"] = xdb.text(inner.find("PlayerID"))
            obj.extra["creatureSwapBlockedForAI"] = xdb.boolean(inner.find("CreatureSwapBlockedForAI"))
            obj.extra["garrison"] = [
                {"creature": xdb.text(a.find("Creature")),
                 "amount": int(xdb.number(a.find("Amount")))}
                for a in xdb.items(inner.find("armySlots"))
                if xdb.text(a.find("Creature"))
            ]
        elif inner.tag == "AdvMapSeerHut":
            quest = inner.find("Quest")
            if quest is not None:
                obj.extra["quest"] = {
                    "name": xdb.text(quest.find("Name")),
                    "kind": xdb.text(quest.find("Kind")),
                    "caption": quest.find("CaptionFileRef").get("href")
                               if quest.find("CaptionFileRef") is not None else "",
                    "description": quest.find("DescriptionFileRef").get("href")
                                   if quest.find("DescriptionFileRef") is not None else "",
                    "timeout": int(xdb.number(quest.find("Timeout"), -1)),
                    "holdout": int(xdb.number(quest.find("Holdout"), -1)),
                }
        md.objects.append(obj)
    return md


def iter_map_files(xdb: XDB, only_map_descs: bool = False) -> List[str]:
    """Every ``AdvMapDesc`` file: scenario, multiplayer, single missions, arenas.

    The overlay replaces the logical file selected by :meth:`XDB._path_for`,
    but callers often sort the returned physical paths. Return the base path
    when it exists so that replacing one campaign map does not reorder the
    entire map corpus; ``load_map`` still resolves its contents through XDB and
    therefore reads the overlay record.
    """
    out = []
    seen = set()
    for sub in ("Maps/Scenario", "Maps/Multiplayer", "Maps/SingleMissions",
                "Maps/CombatArenas", "Maps/DuelMode"):
        for p in xdb.iter_files(sub):
            if only_map_descs and not is_map_desc(p):
                continue
            logical = xdb.href_from_path(p)
            if logical.lower() in seen:
                continue
            seen.add(logical.lower())
            base = os.path.join(xdb.data_root, logical.lstrip("/"))
            out.append(base if os.path.isfile(base) else p)
    return out
