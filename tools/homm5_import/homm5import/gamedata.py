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
from .xdb import XDB

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


def _texture_data(xdb: XDB, node: Optional[ET.Element],
                  base_href: Optional[str]) -> dict:
    """Export the resolved ``NDb::Texture`` reference without assigning assets heuristically.

    The material points at a ``Texture`` XDB record, while ``cmd_textures`` writes
    the corresponding DDS as ``Textures/<xdb path>.png``.  Keeping both the
    source XDB path and the generated path in the JSON makes the association
    auditable and lets Unity load the exact material slot rather than guessing
    from a mesh name or PNG order.
    """
    result = {}
    if node is None:
        return result
    texture, texture_href = xdb.resolve(node, base_href)
    if texture is None:
        return result

    # Texture records are normally addressed by an absolute XDB href.  The
    # shipped converter preserves that path and only changes the extension to
    # .dds; the offline PNG exporter appends .png once more.
    source = (texture_href or "").split("#", 1)[0]
    if source:
        result["xdb"] = source
        dds = source[:-4] + ".dds" if source.lower().endswith(".xdb") else source
        result["dds"] = dds
        result["png"] = dds.lstrip("/") + ".png"
    file_name = xdb.text(texture.find("FileName")) or xdb.text(texture.find("DestName"))
    if file_name:
        result["fileName"] = file_name
    result["address"] = xdb.text(texture.find("AddrType"))
    result["flipY"] = xdb.boolean(texture.find("FlipY"), False)
    return result


def model_materials(xdb: XDB, model: Optional[ET.Element],
                    model_href: Optional[str]) -> List[dict]:
    """Read ``NDb::Model.Materials`` in the authoritative XDB array order.

    ``geometry.py`` emits one glTF node for each non-empty geometry slice.  The
    runtime binds the arrays ordinally only when their counts make that relation
    unambiguous; this exporter never invents a mapping for an ambiguous model.
    No texture is invented when a material has no texture reference.
    """
    materials: List[dict] = []
    if model is None:
        return materials
    mats = model.find("Materials")
    for item in xdb.items(mats):
        material, material_href = xdb.resolve(item, model_href)
        if material is None:
            # Inline records have the Material child under the Item.
            material = item.find("Material")
            material_href = model_href
        if material is None:
            materials.append({})
            continue

        spec_color = xdb.vec3(material.find("SpecColor"))
        translucent_color = xdb.vec3(material.find("TranslucentColor"))
        record = {
            "texture": _texture_data(xdb, material.find("Texture"), material_href),
            "bump": _texture_data(xdb, material.find("Bump"), material_href),
            "gloss": _texture_data(xdb, material.find("Gloss"), material_href),
            "mirror": _texture_data(xdb, material.find("Mirror"), material_href),
            "detailTexture": _texture_data(xdb, material.find("DetailTexture"), material_href),
            "specFactor": xdb.number(material.find("SpecFactor")),
            "specColor": list(spec_color),
            "metalMirror": xdb.number(material.find("MetalMirror")),
            "dielMirror": xdb.number(material.find("DielMirror")),
            "castShadow": xdb.boolean(material.find("CastShadow"), True),
            "receiveShadow": xdb.boolean(material.find("ReceiveShadow"), True),
            "priority": int(xdb.number(material.find("Priority"))),
            "translucentColor": list(translucent_color),
            "floatParam": xdb.number(material.find("FloatParam")),
            "detailScale": xdb.number(material.find("DetailScale"), 5.0),
            "projectOnTerrain": xdb.boolean(material.find("ProjectOnTerrain"), False),
            "lightingMode": xdb.text(material.find("LightingMode")),
            "dynamicMode": xdb.text(material.find("DynamicMode")),
            "is2Sided": xdb.boolean(material.find("Is2Sided"), False),
            "effect": xdb.text(material.find("Effect")),
            "alphaMode": xdb.text(material.find("AlphaMode")),
            "affectedByFog": xdb.boolean(material.find("AffectedByFog"), True),
            "addPlaced": xdb.boolean(material.find("AddPlaced"), False),
            "ignoreZBuffer": xdb.boolean(material.find("IgnoreZBuffer"), False),
            "backFaceCastShadow": xdb.boolean(material.find("BackFaceCastShadow"), False),
        }
        materials.append(record)
    return materials


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
                rec["visual"]["materials"] = model_materials(xdb, model, model_href)
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
            if tex is None:
                continue
            name = xdb.text(tex.find("FileName"))
            if not name:
                name = (tex_href or xdb.href_of(mat.find("Texture")) or "").split("#")[0]
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
        rec["materials"] = model_materials(xdb, model, model_href)
        out[aid] = rec
    return out


def load_town_buildings(xdb: XDB, loc: Optional[Localization] = None) -> List[dict]:
    out: List[dict] = []
    for path in xdb.iter_files("GameMechanics/TownBuildingSharedStats"):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        rec = {
            "id": _base_key(path),
            "source": xdb.href_from_path(path),
            "name": _text_ref(xdb, loc, root.find("NameFileRef")),
            "cost": _res(xdb, root.find("Cost")),
            "growth": int(xdb.number(root.find("Growth"))),
        }
        out.append(rec)
    return out


def load_default_stats(xdb: XDB) -> Dict[str, object]:
    """Load global RPGStats from ``/GameMechanics/RPGStats/DefaultStats.xdb``
    (resolved through the ToTE 3.1 ``a2p1-data`` overlay when present), matching
    ``NWorld::GetDefaultStats()`` (`0x00bcb3f0` in ``H5_Game_PEST.exe``).
    """
    root = xdb.load("/GameMechanics/RPGStats/DefaultStats.xdb")
    if root is None:
        return {}
    adv = root.find("adventure")
    cmb = root.find("combat")
    lm = cmb.find("LuckAndMorale") if cmb is not None else None
    cre = cmb.find("Creatures") if cmb is not None else None
    return {
        "source": "/GameMechanics/RPGStats/DefaultStats.xdb",
        "heroInitiative": int(xdb.number(root.find("HeroInitiative"), 10)),
        "spellInitiative": int(xdb.number(root.find("SpellInitiative"), 10)),
        "atbEndTime": int(xdb.number(root.find("ATBEndTime"), 20000)),
        "atbBaseTime": int(xdb.number(root.find("ATBBaseTime"), 100000)),
        "rumorCost": int(xdb.number(root.find("RumorCost"), 100)),
        "adventure": {
            "baseHeroMovement": int(xdb.number(adv.find("BaseHeroMovement"), 2500)) if adv is not None else 2500,
            "baseHeroLookRange": int(xdb.number(adv.find("BaseHeroLookRange"), 12)) if adv is not None else 12,
            "heroManaPerKnowledge": int(xdb.number(adv.find("HeroManaPerKnowledge"), 10)) if adv is not None else 10,
            "visitStablesMovementBonus": int(xdb.number(adv.find("VisitStablesMovementBonus"), 600)) if adv is not None else 600,
            "visitOasisMovementBonus": int(xdb.number(adv.find("VisitOasisMovementBonus"), 800)) if adv is not None else 800,
            "visitRallyFlagMovementBonus": int(xdb.number(adv.find("VisitRallyFlagMovementBonus"), 400)) if adv is not None else 400,
        },
        "combat": {
            "damageIncrease": xdb.number(cmb.find("DamageIncrease"), 0.05) if cmb is not None else 0.05,
            "damageIncreaseCap": xdb.number(cmb.find("DamageIncreaseCap"), 3.0) if cmb is not None else 3.0,
            "damageDecrease": xdb.number(cmb.find("DamageDecrease"), 0.05) if cmb is not None else 0.05,
            "damageDecreaseCap": xdb.number(cmb.find("DamageDecreaseCap"), 0.1) if cmb is not None else 0.1,
            "defenceBonusOnDefend": xdb.number(cre.find("DefenceBonusOnDefend"), 0.3) if cre is not None else 0.3,
            "goodLuckDamageMultiplier": xdb.number(lm.find("GoodLuck_DamageMultiplier"), 2.0) if lm is not None else 2.0,
            "badLuckDamageMultiplier": xdb.number(lm.find("BadLuck_DamageMultiplier"), 0.5) if lm is not None else 0.5,
            "goodMoraleAtbValue": xdb.number(lm.find("GoodMorale_ATBValue"), 0.5) if lm is not None else 0.5,
            "badMoraleAtbValue": xdb.number(lm.find("BadMorale_ATBValue"), 0.0) if lm is not None else 0.0,
        },
    }


def export_all(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, object]:
    return {
        "defaultStats": load_default_stats(xdb),
        "creatures": load_creatures(xdb, loc),
        "spells": load_spells(xdb, loc),
        "artifacts": load_artifacts(xdb, loc),
        "townBuildings": load_town_buildings(xdb, loc),
    }
