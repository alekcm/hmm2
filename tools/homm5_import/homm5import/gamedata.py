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


def _text_list(xdb: XDB, loc: Optional[Localization],
               node: Optional[ET.Element]) -> List[str]:
    """One localized string per ``<Item href="...">`` of a name/description list."""
    if node is None or loc is None:
        return []
    out: List[str] = []
    for item in xdb.items(node):
        href = item.get("href")
        text = loc.read(href) if href else None
        out.append(text or "")
    return out


def _tree(node: Optional[ET.Element]):
    """Generic XML subtree -> nested dict/list/number/bool/str.

    Used for the ``HeroSkills`` constants blocks of DefaultStats.xdb, whose
    per-class layout varies too much to enumerate by hand. Repeated ``Item``
    tags collapse to a list; leaf text is parsed as number/bool when possible.
    """
    if node is None:
        return None
    kids = list(node)
    if not kids:
        text = (node.text or "").strip()
        if text == "":
            return ""
        if text.lower() in ("true", "false"):
            return text.lower() == "true"
        try:
            return int(text) if re.match(r"^-?\d+$", text) else float(text)
        except ValueError:
            return text
    out: dict = {}
    order: List[str] = []
    for kid in kids:
        value = _tree(kid)
        if kid.tag == "Item":
            out.setdefault("Item", [])
            if not isinstance(out["Item"], list):
                out["Item"] = [out["Item"]]
            out["Item"].append(value)
        elif kid.tag in out:
            if not isinstance(out[kid.tag], list):
                out[kid.tag] = [out[kid.tag]]
            out[kid.tag].append(value)
        else:
            out[kid.tag] = value
            order.append(kid.tag)
    return out


def _find(node: Optional[ET.Element], path: str) -> Optional[ET.Element]:
    """ElementTree path lookup that tolerates missing intermediate nodes."""
    return node.find(path) if node is not None else None


def _number_items(node: Optional[ET.Element]) -> List[float]:
    """Numeric ``<Item>`` children of a node (empty when absent)."""
    if node is None:
        return []
    return [float((i.text or "0").strip() or 0) for i in node if i.tag == "Item"]


def _mastery_rows(xdb: XDB, node: Optional[ET.Element]) -> List[dict]:
    """<damage>/<duration> rows: one per mastery tier, Base + PerPower."""
    if node is None:
        return []
    rows = []
    for item in xdb.items(node):
        rows.append({
            "base": float(xdb.number(item.find("Base"))),
            "perPower": float(xdb.number(item.find("PerPower"))),
        })
    return rows


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
                # Mana is spelled <TrainedCost> in the spell record.
                "manaCost": int(xdb.number(root.find("TrainedCost"))),
                "canSelectDead": xdb.boolean(root.find("CanSelectDead")),
                "damageIsElemental": xdb.boolean(root.find("DamageIsElemental")),
            }
            # damage/duration carry four mastery tiers (none/basic/advanced/
            # expert), each scaling with spell power: Base + PerPower * Power.
            rec["damage"] = _mastery_rows(xdb, root.find("damage"))
            rec["duration"] = _mastery_rows(xdb, root.find("duration"))
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

    def real(parent, field, default):
        node = parent.find(field) if parent is not None else None
        value = xdb.number(node, default)
        return float(value)

    combat = root.find("combat")
    combat_creatures = combat.find("Creatures") if combat is not None else None
    luck_morale = combat.find("LuckAndMorale") if combat is not None else None

    # Hero skill constants: the engine keeps every skill/perk coefficient in
    # two HeroSkills blocks (adventure map and combat). Exported verbatim so
    # the runtime can read the shipped numbers instead of hardcoding them.
    hero_skills_adv = adventure.find("HeroSkills")
    hero_skills_combat = combat.find("HeroSkills") if combat is not None else None

    necro_transform: Dict[str, str] = {}
    if hero_skills_combat is not None:
        table = hero_skills_combat.find("Necromancer/Necromancy/TransformTable")
        for row in xdb.items(table):
            dead, rise = xdb.text(row.find("Dead")), xdb.text(row.find("Rise"))
            if dead and rise:
                necro_transform[dead] = rise

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
        # Combat constants the engine reads from the same file. The luck and
        # morale multipliers and the defending bonus are the shipped numbers;
        # the ATB scale is exposed because the turn model is reconstructed
        # around it.
        "combat": {
            "defenceBonusOnDefend": real(combat_creatures, "DefenceBonusOnDefend", 0.3),
            "goodLuckDamageMultiplier": real(luck_morale, "GoodLuck_DamageMultiplier", 2.0),
            "badLuckDamageMultiplier": real(luck_morale, "BadLuck_DamageMultiplier", 0.5),
            "goodMoraleAtbValue": real(luck_morale, "GoodMorale_ATBValue", 0.5),
            "badMoraleAtbValue": real(luck_morale, "BadMorale_ATBValue", 0.5),
            "heroInitiative": number(root, "HeroInitiative"),
            "spellInitiative": number(root, "SpellInitiative"),
            "atbBaseTime": number(root, "ATBBaseTime"),
            "atbEndTime": number(root, "ATBEndTime"),
        },
        # Verbatim HeroSkills coefficient trees (nested numbers/strings).
        "heroSkills": {
            "adventure": _tree(hero_skills_adv),
            "combat": _tree(hero_skills_combat),
        },
        # Flat, runtime-facing subset of the same constants. Keys are the
        # effects the combat and adventure layers apply per skill rank/perk;
        # values are the shipped coefficients (rank lists index mastery 1..n).
        "skillEffects": {
            "scoutingVisibilityIncrease": int(xdb.number(_find(hero_skills_adv, "Scouting_VisibilityIncrease"))),
            "disguiseVisibilityIncrease": int(xdb.number(_find(hero_skills_adv, "DisguiseAndReckon_VisibilityIncrease"))),
            "intelligenceManaMultiplier": float(xdb.number(_find(hero_skills_adv, "Intelligence_ManaPointsBonus"), 1)),
            "warMachinesHealthPerSkill": int(xdb.number(_find(hero_skills_adv, "WarMachines_HealthBonusPerSkillTrained"))),
            "warMachinesPerkHealthMultiplier": float(xdb.number(_find(hero_skills_adv, "WarMachines_PerkSpecificHealthMultiplier"), 1)),
            "offenceDamageBonus": [float(xdb.number(_find(hero_skills_combat, "SkillOffence/BasicDamageBonus"))),
                                   float(xdb.number(_find(hero_skills_combat, "SkillOffence/AdvancedDamageBonus"))),
                                   float(xdb.number(_find(hero_skills_combat, "SkillOffence/ExpertDamageBonus")))],
            "archeryDamageBonus": float(xdb.number(_find(hero_skills_combat, "SkillOffence/Archery_DamageBonus"))),
            "defenceDamageReduce": [float(xdb.number(_find(hero_skills_combat, "SkillDefence/BasicDamageReduce"))),
                                    float(xdb.number(_find(hero_skills_combat, "SkillDefence/AdvancedDamageReduce"))),
                                    float(xdb.number(_find(hero_skills_combat, "SkillDefence/ExpertDamageReduce")))],
            "evasionDamageReduce": float(xdb.number(_find(hero_skills_combat, "SkillDefence/Evasion_DamageReduce"))),
            "retributionDamagePerMorale": float(xdb.number(_find(hero_skills_combat, "Knight/Retribution_DamageBonusPerMorale"))),
            "holdGroundDefendMultiplier": float(xdb.number(_find(hero_skills_combat, "Knight/HoldGround_DefendBonusMultiplier"), 1)),
            "trainingRetaliationBonus": [float(xdb.number(_find(hero_skills_combat, "Knight/Training/BasicDamageBonus"))),
                                         float(xdb.number(_find(hero_skills_combat, "Knight/Training/AdvancedDamageBonus"))),
                                         float(xdb.number(_find(hero_skills_combat, "Knight/Training/ExpertDamageBonus"))),
                                         float(xdb.number(_find(hero_skills_combat, "Knight/Training/ExtraExpertDamageBonus")))],
            "trainingCostDiscountPerLevel": float(xdb.number(_find(hero_skills_combat, "Knight/Training/SkillTraining_CostDiscountPerLevel"))),
            "roadHomeSpeedBonus": int(xdb.number(_find(hero_skills_combat, "Knight/RoadHome/SpeedBonus"))),
            "gatingPortionByLevel": list(_number_items(_find(hero_skills_combat, "DemonLord/Gating/PortionOfSummonedByGatingLevel"))),
            "gatingMasteryPortionBonus": float(xdb.number(_find(hero_skills_combat, "DemonLord/Gating/GatingMasteryPortionBonus"))),
            "criticalGatingConstantProb": float(xdb.number(_find(hero_skills_combat, "DemonLord/Gating/CriticalGatingConstantProb"))),
            "criticalGatingPerLuckPointProb": float(xdb.number(_find(hero_skills_combat, "DemonLord/Gating/CriticalGatingPerLuckPointProb"))),
            "demonicFireBaseDamage": int(xdb.number(_find(hero_skills_combat, "DemonLord/DemonicFire/BaseDamage"))),
            "demonicFireDamagePerPower": int(xdb.number(_find(hero_skills_combat, "DemonLord/DemonicFire/DamagePerSpellPower"))),
            "necromancyRaiseBase": float(xdb.number(_find(hero_skills_combat, "Necromancer/Necromancy/RaisePercentBase"))),
            "necromancyRaisePerLevel": float(xdb.number(_find(hero_skills_combat, "Necromancer/Necromancy/RaisePercentPerSkillLevel"))),
            "lordOfUndeadRaiseBonus": float(xdb.number(_find(hero_skills_combat, "Necromancer/Necromancy/LordOfUndeadBonus"))),
            "spiritLinkHpForMana": int(xdb.number(_find(hero_skills_combat, "Necromancer/SpiritLink_HPForOneMana"))),
            "deathScreamMoralePenalty": int(xdb.number(_find(hero_skills_combat, "Necromancer/DeathScream_MoralePenalty"))),
            "deathScreamInitiativePenalty": float(xdb.number(_find(hero_skills_combat, "Necromancer/DeathScream_InitiativePenalty"))),
            "deathTreadSpeedBonus": int(xdb.number(_find(hero_skills_combat, "Necromancer/DeathTread/SpeedBonus"))),
            "invocationChainBonusPerLevel": float(xdb.number(_find(hero_skills_combat, "Warlock/ElementalChainBonusPerInvocationTrained"))),
            "quicknessOfMindInitiativeBonus": float(xdb.number(_find(hero_skills_combat, "Runemage/QuicknessOfMind_InitiativeBonus"))),
            "tapRunesManaPerKnowledge": float(xdb.number(_find(hero_skills_combat, "Runemage/TapRunes_ManaRestorePerKnowledge"))),
            "distractEnemyInitiativeDrop": float(xdb.number(_find(hero_skills_combat, "Runemage/Distract_EnemyHeroInitiativeDrop"))),
            "setAfireDamageMultiplier": float(xdb.number(_find(hero_skills_combat, "Runemage/SetAfire_DamageMultiplier"))),
            "setAfireDurationRounds": int(xdb.number(_find(hero_skills_combat, "Runemage/SetAfire_DamageDurationInRounds"))),
            "shrugDarknessSpellpowerMultiplier": float(xdb.number(_find(hero_skills_combat, "Runemage/ShrugDarkness_EnemySpellpowerMultiplier"), 1)),
            "runicArmorSpellpowerBonus": int(xdb.number(_find(hero_skills_combat, "Runemage/RunicArmor_SpellpowerBonus"))),
            "runicAttunementMoraleBonus": int(xdb.number(_find(hero_skills_combat, "Runemage/RunicAttunement_MoraleBonus"))),
            "runicMachinesInitiativeBonus": int(xdb.number(_find(hero_skills_combat, "Runemage/RunicMachines_InitiativeBonus"))),
            "mentoringExpPercent": float(xdb.number(_find(hero_skills_combat, "Runemage/Mentoring_ExpPecentToGiveToVisitor"))),
            "empathyAtbPerMorale": float(xdb.number(_find(hero_skills_combat, "Runemage/Empathy_ATBAffectionOnMorale"))),
            "elvenLuckDamageMultiplier": float(xdb.number(_find(hero_skills_combat, "Ranger/ElvenLuck_DamageMultiplier"), 2)),
            "stormWindInitiativePenalty": float(xdb.number(_find(hero_skills_combat, "Ranger/StormWind_InitiativePenalty"))),
            "stormWindSpeedPenalty": int(xdb.number(_find(hero_skills_combat, "Ranger/StormWind_SpeedPenalty"))),
            "fogVeilInitiativePenalty": float(xdb.number(_find(hero_skills_combat, "Ranger/FogVeil_InitiativePenalty"))),
            "fogVeilDamageDecrease": float(xdb.number(_find(hero_skills_combat, "Ranger/FogVeil_DamageDecrease"))),
            "pathOfWarMovePoints": int(xdb.number(_find(hero_skills_combat, "Barbarian/PathOfWar_MovePointsBonus"))),
            "stunningBlowAtbDrop": float(xdb.number(_find(hero_skills_combat, "Barbarian/StunningBlow_ATBDrop"))),
            "bodybuildingHpPercent": float(xdb.number(_find(hero_skills_combat, "Barbarian/Bodybuilding_HPBonusPercent"))),
            "luckOfBarbarianMagicProofPerLuck": float(xdb.number(_find(hero_skills_combat, "Barbarian/LuckOfTheBarbarian_MagicProofPerLuck"))),
            "powerfullBlowCriticalChance": float(xdb.number(_find(hero_skills_combat, "Barbarian/PowerfullBlow_ChanceToCritical"))),
            "voiceTrainingCostDiscount": float(xdb.number(_find(hero_skills_combat, "Barbarian/VoiceTraining_WarcryCostDiscount"))),
            "mightyVoiceHeroLevelBonus": int(xdb.number(_find(hero_skills_combat, "Barbarian/MightyVoice_HeroLevelBonus"))),
            "deathToNonexistentSummonDamageMult": float(xdb.number(_find(hero_skills_combat, "Barbarian/DeathToNonexistent_DamageToSummonsMult"), 1)),
            "corruptMagicCostIncrease": float(xdb.number(_find(hero_skills_combat, "Barbarian/CorruptMagic_SpellCostIncrease"))),
            "weakenMagicSpellpowerDecrease": float(xdb.number(_find(hero_skills_combat, "Barbarian/WeakenMagic_SpellPowerDecrease"))),
            "detainMagicAtbDrop": float(xdb.number(_find(hero_skills_combat, "Barbarian/DetainMagic_CasterExtraATBDrop"))),
        },
        # Necromancy: which risen creature replaces each slain one.
        "necromancyTransform": necro_transform,
    }


def load_war_machines(xdb: XDB) -> Dict[str, dict]:
    """Ballista / catapult / first aid tent / ammo cart combat stats.

    The table carries per-skill-tier rows for offence, defence and the damage
    factors (tier 0 = no War Machines skill ... tier 4 = ultimate).
    """
    root = xdb.load("/GameMechanics/RefTables/WarMachines.xdb")
    out: Dict[str, dict] = {}
    if root is None:
        return out

    def tier_rows(node):
        return [int(xdb.number(i)) for i in xdb.items(node)]

    for item in xdb.items(root.find("objects")):
        key = xdb.text(item.find("ID"))
        obj = item.find("Obj")
        if obj is not None:
            # An #n:inline(WarMachine) href resolves to the WarMachine record
            # itself; a plain record would sit under <Obj>.
            inner = xdb.resolve_field(obj, "/GameMechanics/RefTables/WarMachines.xdb") or obj
        else:
            inner = None
        wm = inner if inner is not None and inner.tag == "WarMachine" else (
            inner.find("WarMachine") if inner is not None else None)
        if wm is None or key == "WAR_MACHINE_NONE":
            continue
        out[key] = {
            "id": key,
            "shots": int(xdb.number(wm.find("Shots"))),
            "initiative": int(xdb.number(wm.find("Initiative"))),
            "health": int(xdb.number(wm.find("Health"))),
            "cost": _res(xdb, wm.find("Cost")),
            "offence": tier_rows(wm.find("Offense")),
            "defence": tier_rows(wm.find("Defense")),
            "minDamageFactor": tier_rows(wm.find("MinDamageFactor")),
            "maxDamageFactor": tier_rows(wm.find("MaxDamageFactor")),
            "visual": xdb.href_of(wm.find("Visual")),
        }
    return out


def load_arena_buildings(xdb: XDB) -> Dict[str, dict]:
    """Siege structure health: walls, gate, towers, moat, magic wall.

    ``HealthFortBonuses`` are the per-fort-tier additions (Fort, Citadel,
    Castle) on top of BaseHealth.
    """
    root = xdb.load("/GameMechanics/RefTables/ArenaBuildingsStats.xdb")
    out: Dict[str, dict] = {}
    if root is None:
        return out
    for item in xdb.items(root.find("objects")):
        key = xdb.text(item.find("ID"))
        obj = item.find("obj")
        if obj is None:
            continue
        out[key] = {
            "id": key,
            "baseHealth": int(xdb.number(obj.find("BaseHealth"))),
            "healthFortBonuses": [int(xdb.number(i)) for i in xdb.items(obj.find("HealthFortBonuses"))],
        }
    return out


def load_hero_classes(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, dict]:
    """Per-class level-up probabilities: which skills the game offers and how
    likely each primary stat is to gain a point on level up.

    ``SkillsProbs`` drives the weighted skill offers of the level-up dialog;
    ``AttributeProbs`` rolls the +1 primary stat every level.
    """
    root = xdb.load("/GameMechanics/RefTables/HeroClass.xdb")
    out: Dict[str, dict] = {}
    if root is None:
        return out
    for item in xdb.items(root.find("objects")):
        key = xdb.text(item.find("ID"))
        obj = item.find("obj")
        if obj is None or key == "HERO_CLASS_NONE":
            continue
        probs = {}
        for row in xdb.items(obj.find("SkillsProbs")):
            skill = xdb.text(row.find("SkillID"))
            if skill and skill != "HERO_SKILL_NONE":
                probs[skill] = int(xdb.number(row.find("Prob")))
        attrs = obj.find("AttributeProbs")
        name_node = obj.find("NameFileRef")
        name = None
        if name_node is not None and loc is not None and name_node.get("href"):
            # Class name texts are relative to the ref table itself
            # (HeroClass/HeroClassKnight.txt), mirrored under texts/.
            name = loc.read(xdb.absolutize(name_node.get("href"),
                                           "/GameMechanics/RefTables/HeroClass.xdb"))
        out[key] = {
            "id": key,
            "name": name,
            "skillsProbs": probs,
            "attributeProbs": {
                "offence": int(xdb.number(attrs.find("OffenceProb"))) if attrs is not None else 0,
                "defence": int(xdb.number(attrs.find("DefenceProb"))) if attrs is not None else 0,
                "spellpower": int(xdb.number(attrs.find("SpellpowerProb"))) if attrs is not None else 0,
                "knowledge": int(xdb.number(attrs.find("KnowledgeProb"))) if attrs is not None else 0,
            },
            "preferredSpells": [xdb.text(i) for i in xdb.items(obj.find("PreferredSpellsFromSpellShop"))
                                if xdb.text(i) and xdb.text(i) != "SPELL_NONE"],
        }
    return out


def load_skills(xdb: XDB, loc: Optional[Localization] = None) -> Dict[str, dict]:
    """Every skill, perk and feat from the shipped skill table.

    ``Skills.xdb`` mixes five kinds of records in one list: the ranked skills
    (``SKILLTYPE_SKILL``; common ones cap at mastery 3, racial ones at 4) and
    four perk flavours that hang off them through ``BasicSkillID``. Perk
    availability is per class: a record with ``SkillPrerequisites`` rows is
    learnable only by the classes that have a row, and only once every listed
    dependency perk is owned. Records without rows are gated by their skill
    alone (class perks belong to racial skills only their class can learn).
    """
    root = xdb.load("/GameMechanics/RefTables/Skills.xdb")
    out: Dict[str, dict] = {}
    if root is None:
        return out
    kinds = {
        "SKILLTYPE_SKILL": "skill",
        "SKILLTYPE_STANDART_PERK": "standardPerk",
        "SKILLTYPE_CLASS_PERK": "classPerk",
        "SKILLTYPE_SPECIAL_PERK": "specialPerk",
        "SKILLTYPE_UINQUE_PERK": "uniquePerk",
    }
    for item in xdb.items(root.find("objects")):
        key = xdb.text(item.find("ID"))
        obj = item.find("obj")
        if obj is None or key == "HERO_SKILL_NONE":
            continue
        kind = kinds.get(xdb.text(obj.find("SkillType")), "skill")
        names = _text_list(xdb, loc, obj.find("NameFileRef"))
        descs = _text_list(xdb, loc, obj.find("DescriptionFileRef"))
        prereqs = {}
        for row in xdb.items(obj.find("SkillPrerequisites")):
            cls = xdb.text(row.find("Class"))
            deps = [xdb.text(i) for i in xdb.items(row.find("dependenciesIDs")) if xdb.text(i)]
            if cls:
                prereqs[cls] = deps
        basic = xdb.text(obj.find("BasicSkillID"))
        out[key] = {
            "id": key,
            "kind": kind,
            "heroClass": xdb.text(obj.find("HeroClass")),
            # Skills are their own group head; perks hang off BasicSkillID.
            "skill": key if kind == "skill" else basic,
            # Racial skills carry four rank texts (Basic..ExtraExpert); common
            # skills three; perks a single name/description.
            "names": names,
            "descriptions": descs,
            "spellBuffs": [xdb.text(i) for i in xdb.items(obj.find("spellBuffs")) if xdb.text(i)],
            "prerequisites": prereqs,
        }
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
        "warMachines": load_war_machines(xdb),
        "arenaBuildings": load_arena_buildings(xdb),
        "skills": load_skills(xdb, loc),
        "heroClasses": load_hero_classes(xdb, loc),
    }
