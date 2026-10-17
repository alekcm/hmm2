"""Game data: creatures, heroes, spells, skills, abilities.

Every stat in the game lives in ``data/GameMechanics`` as XML, so this is a
faithful transcription rather than an interpretation: the numbers Unity sees are
the numbers the shipped engine reads.
"""

from __future__ import annotations

import os
import re
from typing import Dict, List, Optional
from xml.etree import ElementTree as ET

from .localization import Localization
from .xdb import XDB, texture_dds_path

RESOURCE_FIELDS = ("Wood", "Ore", "Mercury", "Crystal", "Sulfur", "Gem", "Gold")

CREATURE_INT_FIELDS = (
    "AttackSkill", "DefenceSkill", "Shots", "MinDamage", "MaxDamage", "Speed",
    "Initiative", "Health", "SpellPoints", "Exp", "Power", "TimeToCommand",
    "CreatureTier", "WeeklyGrowth", "CombatSize", "Range",
)


def _res(xdb: XDB, node: Optional[ET.Element]) -> Dict[str, int]:
    if node is None:
        return {f: 0 for f in RESOURCE_FIELDS}
    return {f: int(xdb.number(node.find(f))) for f in RESOURCE_FIELDS}


def _text_ref(xdb: XDB, loc: Optional[Localization], node: Optional[ET.Element]) -> Optional[str]:
    if node is None or loc is None:
        return None
    href = node.get("href")
    return loc.read(href) if href else None


def _base_key(path: str) -> str:
    return os.path.splitext(os.path.basename(path))[0]


def load_creatures(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, dict]:
    """All creature and hero definitions, keyed by their enum-style name."""
    out: Dict[str, dict] = {}
    for path in list(xdb.iter_files("GameMechanics/Creature/Creatures")) + \
                list(xdb.iter_files("GameMechanics/Creature/Heroes")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        key = _base_key(path)
        rec = {"id": key, "source": xdb.href_from_path(path), "isHero": "Heroes" in path.replace("\\", "/")}
        for f in CREATURE_INT_FIELDS:
            rec[f] = int(xdb.number(root.find(f)))
        rec["flying"] = xdb.boolean(root.find("Flying"))
        rec["upgrade"] = xdb.boolean(root.find("Upgrade"))
        rec["town"] = xdb.text(root.find("CreatureTown"))
        rec["pairCreature"] = xdb.text(root.find("PairCreature"))
        rec["baseCreature"] = xdb.text(root.find("BaseCreature"))
        rec["cost"] = _res(xdb, root.find("Cost"))
        elem = root.find("MagicElement")
        rec["elements"] = [xdb.text(elem.find("First")), xdb.text(elem.find("Second"))] if elem is not None else []
        rec["abilities"] = [xdb.text(i) for i in xdb.items(root.find("Abilities")) if xdb.text(i)]
        rec["knownSpells"] = [
            {"spell": xdb.text(i.find("Spell")), "mastery": xdb.text(i.find("Mastery"))}
            for i in xdb.items(root.find("KnownSpells"))
        ]
        rec["upgrades"] = [xdb.text(i) for i in xdb.items(root.find("Upgrades")) if xdb.text(i)]

        visual, visual_href = xdb.resolve(root.find("Visual"), xdb.href_from_path(path))
        rec["visual"] = {}
        if visual is not None:
            rec["visual"]["name"] = _text_ref(xdb, loc, visual.find("CreatureNameFileRef"))
            rec["visual"]["description"] = _text_ref(xdb, loc, visual.find("DescriptionFileRef"))
            rec["visual"]["abilities"] = _text_ref(xdb, loc, visual.find("CreatureAbilitiesFileRef"))
            rec["visual"]["mountBone"] = xdb.text(visual.find("MountBone"))
            rec["visual"]["mountPoint"] = xdb.vec3(visual.find("MountPoint"))
            rec["visual"]["icon"] = xdb.href_of(visual.find("Icon128")) or xdb.href_of(visual.find("Icon"))
            anim, anim_href = xdb.resolve(visual.find("AnimCharacter"), visual_href)
            if anim is not None:
                model, model_href = xdb.resolve(anim.find("Model"), anim_href)
                geom_uid, textures = model_geometry(xdb, model, model_href)
                rec["visual"]["geometryUid"] = geom_uid
                rec["visual"]["textures"] = textures
        out[key] = rec
    return out


def load_spells(xdb: XDB, loc: Optional[Localization] = None, subdirs=(
        "GameMechanics/Spell/Combat_Spells",
        "GameMechanics/Spell/Adventure_Spells",
        "GameMechanics/Spell/Hero_Skills",
        "GameMechanics/Spell/Creature_Abilities",
        "GameMechanics/Spell/Hero_Abilities",
        "GameMechanics/Spell/SpellLikeEffects",
        "GameMechanics/Spell/Town_Specializations",
)) -> Dict[str, dict]:
    out: Dict[str, dict] = {}
    for sub in subdirs:
        for path in xdb.iter_files(sub):
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue
            if root.tag not in ("Spell", "HeroSkill", "SpellLikeEffect", "TownSpecialization"):
                continue
            key = _base_key(path)
            rec = {
                "id": key,
                "kind": root.tag,
                "category": sub.split("/")[-1],
                "source": xdb.href_from_path(path),
                "name": _text_ref(xdb, loc, root.find("NameFileRef")),
                "description": _text_ref(xdb, loc, root.find("LongDescriptionFileRef"))
                             or _text_ref(xdb, loc, root.find("ShortDescriptionFileRef")),
                "level": int(xdb.number(root.find("Level"))),
                "school": xdb.text(root.find("MagicSchool")),
                "element": xdb.text(root.find("Element")),
                "target": xdb.text(root.find("Target")),
                "requiredHeroLevel": int(xdb.number(root.find("RequiredHeroLevel"))),
                "isAimed": xdb.boolean(root.find("IsAimed")),
                "isAreaAttack": xdb.boolean(root.find("IsAreaAttack")),
                "cost": _res(xdb, root.find("sSpellCost")),
            }
            rec["visuals"] = [
                xdb.href_of(i) or xdb.text(i) for i in xdb.items(root.find("visuals"))
            ]
            out[key] = rec
    return out


MINE_INCOME_DESCRIPTIONS = {
    "GOLD": "GoldMine",
    "ORE": "OrePit",
    "WOOD": "Sawmill",
    "MERCURY": "AlchemistLab",
    "CRYSTAL": "CrystalCavern",
    "SULFUR": "SulfurDune",
    "GEM": "GemPond",
}
MINE_DAILY_INCOME_FALLBACK = {
    "GOLD": 1000, "ORE": 2, "WOOD": 2, "MERCURY": 1,
    "CRYSTAL": 1, "SULFUR": 1, "GEM": 1,
}


def load_mine_daily_income(loc: Optional[Localization]) -> Dict[str, int]:
    """Read production amounts from the shipped mine description texts.

    The descriptions explicitly state daily yields. Fall back to those same
    shipped values if the caller omitted the optional text bundle.
    """
    if loc is None:
        return dict(MINE_DAILY_INCOME_FALLBACK)
    income = {}
    for resource, mine in MINE_INCOME_DESCRIPTIONS.items():
        description = loc.read(f"/Text/Game/Mines/{mine}/Description.txt")
        match = re.search(r"\d+", description or "")
        if match:
            income[resource] = int(match.group(0))
    return income or dict(MINE_DAILY_INCOME_FALLBACK)


def load_game_rules(xdb: XDB, loc: Optional[Localization] = None) -> dict:
    """Import data-driven adventure/RPG constants from the authoritative stats record.

    These are raw shipped values. This does not emulate the native engine's
    formulas that combine the constants with army, terrain, artifact and skill data.
    """
    root = xdb.load("/GameMechanics/RPGStats/DefaultStats.xdb")
    if root is None:
        return {}
    adventure = root.find("adventure")
    adventure = adventure if adventure is not None else ET.Element("adventure")

    def number(parent, field):
        return int(xdb.number(parent.find(field)))

    def resource_rows(parent, field):
        group = parent.find(field)
        if group is None:
            return []
        rows = []
        for item in xdb.items(group):
            rows.append({key[0].lower() + key[1:]: int(xdb.number(item.find(key)))
                         for key in RESOURCE_FIELDS})
        return rows

    return {
        "source": "/GameMechanics/RPGStats/DefaultStats.xdb",
        "baseHeroMovement": number(adventure, "BaseHeroMovement"),
        "minCreatureSpeed": number(adventure, "MinCreatureSpeed"),
        "maxCreatureSpeed": number(adventure, "MaxCreatureSpeed"),
        "visitFountainOfYouthMovementBonus": number(adventure, "VisitFountainOfYouthMovementBonus"),
        "visitRallyFlagMovementBonus": number(adventure, "VisitRallyFlagMovementBonus"),
        "visitOasisMovementBonus": number(adventure, "VisitOasisMovementBonus"),
        "visitStablesMovementBonus": number(adventure, "VisitStablesMovementBonus"),
        "forWeekStablesMaxLandMovementBonus": number(adventure, "ForWeekStablesMaxLandMovementBonus"),
        "lighthouseMaxSeaMovementBonus": number(adventure, "LighthouseMaxSeaMovementBonus"),
        "startHumanResources": resource_rows(adventure, "StartHumanResources"),
        "startComputerResources": resource_rows(adventure, "StartComputerResources"),
        "mineDailyIncome": load_mine_daily_income(loc),
    }


def model_geometry(xdb: XDB, model: Optional[ET.Element],
                   model_href: Optional[str]) -> tuple:
    """From a ``<Model>`` record: its geometry UID and its material textures."""
    geom_uid = ""
    textures: List[str] = []
    if model is None:
        return geom_uid, textures
    geom = model.find("Geometry")
    if geom is not None:
        inner = xdb.resolve_field(geom, model_href) or geom
        geom_uid = xdb.text(inner.find("uid"))
    mats = model.find("Materials")
    if mats is not None:
        for item in xdb.items(mats):
            mat = xdb.resolve_field(item, model_href) or item
            if mat is None:
                continue
            tex, tex_href = xdb.resolve(mat.find("Texture"), model_href)
            # A Texture record names its file as <DestName>, not <FileName>.
            name = texture_dds_path(xdb, tex, tex_href)
            if name:
                textures.append(name)
    return geom_uid, textures


def load_artifacts(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, dict]:
    """Artifacts come from the authoritative ``RefTables/Artifacts.xdb`` table.

    ``MapObjects/Artifacts/*`` only holds the map-object view (model, blocked
    tiles, pickup message); class, slot, price and stat modifiers live in the
    reference table keyed by ``<ID>``.
    """
    out: Dict[str, dict] = {}
    root = xdb.load("/GameMechanics/RefTables/Artifacts.xdb")
    if root is None:
        return out
    # Reference tables wrap their rows in <objects>.
    table = root.find("objects") or root.find("Artifacts") or root
    for item in xdb.items(table):
        aid = xdb.text(item.find("ID"))
        obj = item.find("obj")
        if not aid or obj is None:
            continue
        rec = {
            "id": aid,
            "name": _text_ref(xdb, loc, obj.find("NameFileRef")) or aid,
            "description": _text_ref(xdb, loc, obj.find("DescriptionFileRef")),
            "artifactClass": xdb.text(obj.find("Type")),
            "slot": xdb.text(obj.find("Slot")),
            "price": int(xdb.number(obj.find("CostOfGold"))),
            "aiValue": int(xdb.number(obj.find("AIValue"))),
            "sellable": xdb.boolean(obj.find("CanBeGeneratedToSell")),
            "icon": xdb.href_of(obj.find("Icon")),
            "stats": {
                f: int(xdb.number((obj.find("HeroStatsModif") or ET.Element("x")).find(f)))
                for f in ("Attack", "Defence", "Knowledge", "SpellPower", "Morale", "Luck")
            },
        }
        model, model_href = xdb.resolve(obj.find("Model"), "/GameMechanics/RefTables/Artifacts.xdb")
        rec["geometryUid"], rec["textures"] = model_geometry(xdb, model, model_href)
        out[aid] = rec
    return out


def load_town_buildings(xdb: XDB, loc: Optional[Localization] = None) -> List[dict]:
    out: List[dict] = []
    for path in xdb.iter_files("GameMechanics/TownBuildingSharedStats"):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        description = _text_ref(xdb, loc, root.find("DescriptionFileRef"))
        income_match = re.search(r"ежедневн\w*.*?(\d+)\s+золот", description or "", re.I | re.S)
        rec = {
            "id": _base_key(path),
            "source": xdb.href_from_path(path),
            "name": _text_ref(xdb, loc, root.find("NameFileRef")),
            "type": xdb.text(root.find("Type")),
            "upgrade": xdb.text(root.find("Upgrade")),
            "cost": _res(xdb, root.find("Cost")),
            "growth": int(xdb.number(root.find("Growth"))),
            "incomeGoldPerDay": int(income_match.group(1)) if income_match else 0,
        }
        out.append(rec)
    return out


def _anim_set(xdb: XDB, elem: Optional[ET.Element],
              base_href: Optional[str]) -> Dict[str, str]:
    """One AnimSet as ``{kind: clip uid}``.

    ``<Item><Kind>idle00</Kind><Anim href="...(BasicSkelAnim).xdb"/></Item>`` --
    the BasicSkelAnim record carries the uid that names the file under
    ``bin/animations/``.
    """
    out: Dict[str, str] = {}
    aset, href = xdb.resolve(elem, base_href)
    if aset is None:
        return out
    items = aset.find("animations")
    if items is None:
        return out
    for item in xdb.items(items):
        kind = xdb.text(item.find("Kind"))
        anim, anim_href = xdb.resolve(item.find("Anim"), href)
        if not kind or anim is None:
            continue
        uid = xdb.text(anim.find("uid"))
        if uid:
            out[kind] = uid
    return out


def load_model_animations(xdb: XDB) -> Dict[str, dict]:
    """``geometryUid -> {skeleton, arena, advmap}``, the clips a model can play.

    Keyed by geometry uid because that is what a map object already carries, so
    the runtime can go from an object on the map to a clip in one lookup.
    """
    out: Dict[str, dict] = {}
    for path in xdb.iter_files("Characters"):
        root = xdb.load(xdb.href_from_path(path))
        if root is None or root.tag != "Character":
            continue
        base = xdb.href_from_path(path)
        model, model_href = xdb.resolve(root.find("Model"), base)
        if model is None:
            continue
        geom_uid, _textures = model_geometry(xdb, model, model_href)
        if not geom_uid:
            continue

        skel, skel_href = xdb.resolve(model.find("Skeleton"), model_href)
        skel_uid = xdb.text(skel.find("uid")) if skel is not None else ""

        entry = {
            "skeleton": skel_uid,
            "model": model_href or "",
            "arena": _anim_set(xdb, root.find("ArenaAnimSet"), base),
            "advmap": _anim_set(xdb, root.find("AdvMapAnimSet"), base),
        }
        if entry["skeleton"] or entry["arena"] or entry["advmap"]:
            out[geom_uid] = entry
    return out


def export_all(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, object]:
    return {
        "creatures": load_creatures(xdb, loc),
        "spells": load_spells(xdb, loc),
        "artifacts": load_artifacts(xdb, loc),
        "townBuildings": load_town_buildings(xdb, loc),
        "modelAnimations": load_model_animations(xdb),
        "rules": load_game_rules(xdb),
    }
