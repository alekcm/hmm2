using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Data
{
    /// <summary>Resource costs, as stored on every creature, building and spell.</summary>
    public class ResourceCost
    {
        public int Wood;
        public int Ore;
        public int Mercury;
        public int Crystal;
        public int Sulfur;
        public int Gem;
        public int Gold;

        public bool IsFree => Wood == 0 && Ore == 0 && Mercury == 0 && Crystal == 0
                              && Sulfur == 0 && Gem == 0 && Gold == 0;

        public override string ToString()
        {
            var parts = new List<string>(7);
            if (Wood != 0) parts.Add(Wood + " wood");
            if (Ore != 0) parts.Add(Ore + " ore");
            if (Mercury != 0) parts.Add(Mercury + " mercury");
            if (Crystal != 0) parts.Add(Crystal + " crystal");
            if (Sulfur != 0) parts.Add(Sulfur + " sulfur");
            if (Gem != 0) parts.Add(Gem + " gems");
            if (Gold != 0) parts.Add(Gold + " gold");
            return parts.Count == 0 ? "free" : string.Join(", ", parts);
        }
    }

    public class KnownSpell
    {
        public string spell;
        public string mastery;
    }

    public class CreatureVisualDef
    {
        public string name;
        public string description;
        public string abilities;
        public string mountBone;
        public float[] mountPoint;
        public string icon;
        public string geometryUid;
        public List<string> textures;
    }

    /// <summary>One creature or hero, transcribed from GameMechanics/Creature.</summary>
    public class CreatureDef
    {
        public string id;
        /// <summary>
        /// The <c>CREATURE_*</c> enum the rest of the game uses for this record.
        /// Maps, spells, dwellings and the necromancy table all name creatures
        /// that way, while <see cref="id"/> is the file name -- close but not
        /// equal (<c>Arch_Magi</c> is <c>CREATURE_ARCH_MAGI</c>). GameDatabase
        /// indexes both; lookups should try this one first.
        /// </summary>
        public string enumId;
        public string source;
        public bool isHero;

        public int AttackSkill;
        public int DefenceSkill;
        public int Shots;
        public int MinDamage;
        public int MaxDamage;
        public int Speed;
        public int Initiative;
        public int Health;
        public int SpellPoints;
        public int Exp;
        public int Power;
        public int TimeToCommand;
        public int CreatureTier;
        public int WeeklyGrowth;
        public int CombatSize;
        public int Range;

        public bool flying;
        public bool upgrade;
        public string town;
        public string pairCreature;
        public string baseCreature;
        public ResourceCost cost;
        public List<string> elements;
        public List<string> abilities;
        public List<KnownSpell> knownSpells;
        public List<string> upgrades;
        public CreatureVisualDef visual;
        /// <summary>
        /// Sound keys by animation event (attack, rangeattack, hit, death, ...), into
        /// sounds.json. Empty when the creature's animation set names none.
        /// </summary>
        public Dictionary<string, string> sounds = new Dictionary<string, string>();

        public string DisplayName => (visual != null && !string.IsNullOrEmpty(visual.name)) ? visual.name : id;
        public string Description => visual != null ? visual.description : null;
        public bool IsRanged => Shots > 0;
        public string DamageText => MinDamage == MaxDamage ? MinDamage.ToString() : MinDamage + "-" + MaxDamage;
    }

    public class SpellDef
    {
        public string id;
        public string kind;
        public string category;
        public string source;
        public string name;
        public string description;
        /// <summary>Original adventure/combat spell-book icon DDS href.</summary>
        public string icon;
        public int level;
        public string school;
        public string element;
        public string target;
        public int requiredHeroLevel;
        public bool isAimed;
        public bool isAreaAttack;
        public ResourceCost cost;
        public List<string> visuals;
        /// <summary>
        /// Sound and particle data per SpellVisual, resolved from the XDBs. Empty for
        /// spells whose visuals name no sound or texture.
        /// </summary>
        public List<SpellVisualDef> visualInfo = new List<SpellVisualDef>();

        /// <summary>Mana the hero spends to cast (the record's TrainedCost).</summary>
        public int manaCost;
        public bool canSelectDead;
        public bool damageIsElemental;
        /// <summary>Damage rows per mastery tier: base + perPower * spell power.</summary>
        public List<SpellLevelRow> damage;
        /// <summary>Buff/debuff duration rows per mastery tier.</summary>
        public List<SpellLevelRow> duration;

        public string DisplayName => string.IsNullOrEmpty(name) ? id : name;

        /// <summary>Value of one row at a given spell power; 0 when absent.</summary>
        public float LevelValue(List<SpellLevelRow> rows, int tier, int spellPower)
        {
            if (rows == null || rows.Count == 0) return 0f;
            var row = rows[Mathf.Clamp(tier, 0, rows.Count - 1)];
            return row.baseValue + row.perPower * spellPower;
        }
    }

    /// <summary>
    /// One SpellVisual: the sound its cast plays and the particle textures that
    /// are drawn for it (paths relative to the texture manifest, e.g. "/Textures/...").
    /// </summary>
    public class SpellVisualDef
    {
        public string sound;
        public List<string> textures = new List<string>();
        public float particleDuration;
        public float hitDelay;
    }

    /// <summary>One sound effect of sounds.json. File is relative to the HoMM5 import root.</summary>
    public class SoundDef
    {
        public string uid;
        public string file;
        public string source;
        public bool loop;
        public bool is3D;
        public float minDistance;
        public float maxDistance;
        public float volume = 1f;
        public int priority;
    }

    /// <summary>One music track of sounds.json. Times are milliseconds, as in the XDBs.</summary>
    public class MusicDef
    {
        public string file;
        public float fadeIn;
        public float fadeOut;
        public float minPause;
        public float maxPause;
    }

    /// <summary>The top level of sounds.json: effects and music, each keyed by a path-like name.</summary>
    public class SoundCatalogDef
    {
        public Dictionary<string, SoundDef> sounds = new Dictionary<string, SoundDef>();
        public Dictionary<string, MusicDef> music = new Dictionary<string, MusicDef>();
    }

    /// <summary>One mastery-tier scaling row of a spell (damage, duration).</summary>
    public class SpellLevelRow
    {
        public float baseValue;
        public float perPower;

        // The JSON spells these as "base"; JsonProperty alias keeps old and new
        // imports readable.
        [Newtonsoft.Json.JsonProperty("base")]
        public float BaseAlias { set { baseValue = value; } }
    }

    /// <summary>Ballista / catapult / first aid tent / ammo cart stats.</summary>
    public class WarMachineDef
    {
        public string id;
        public int shots;
        public int initiative;
        public int health;
        public ResourceCost cost;
        /// <summary>Offence per War Machines skill tier (0 = none ... 4 = ultimate).</summary>
        public List<int> offence;
        public List<int> defence;
        public List<int> minDamageFactor;
        public List<int> maxDamageFactor;
        public string visual;

        public string DisplayName => GameDatabase.PrettifyEnum(id);
    }

    /// <summary>Siege structure health (walls, gate, towers, moat).</summary>
    public class ArenaBuildingDef
    {
        public string id;
        public int baseHealth;
        /// <summary>Per fort tier: Fort, Citadel, Castle.</summary>
        public List<int> healthFortBonuses;

        public int HealthAtFortLevel(int fortLevel)
        {
            var bonus = 0;
            if (healthFortBonuses != null && fortLevel > 0)
                bonus = healthFortBonuses[Mathf.Min(fortLevel, healthFortBonuses.Count) - 1];
            return baseHealth + bonus;
        }
    }

    public class ArtifactStats
    {
        public int Attack;
        public int Defence;
        public int Knowledge;
        public int SpellPower;
        public int Morale;
        public int Luck;
    }

    public class ArtifactDef
    {
        public string id;
        public string name;
        public string description;
        public string artifactClass;
        public string slot;
        public int price;
        public int aiValue;
        public bool sellable;
        public string icon;
        public ArtifactStats stats;
        public string geometryUid;
        public List<string> textures;

        public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
    }

    /// <summary>Raw movement and start-resource values from DefaultStats.xdb.</summary>
    public class GameRulesDef
    {
        public string source;
        public int baseHeroMovement;
        /// <summary>DefaultStats.RPGAdventureParams.BaseHeroLookRange.</summary>
        public int baseHeroLookRange;
        public int minCreatureSpeed;
        public int maxCreatureSpeed;
        public int visitFountainOfYouthMovementBonus;
        public int visitRallyFlagMovementBonus;
        public int visitOasisMovementBonus;
        public int visitStablesMovementBonus;
        public int forWeekStablesMaxLandMovementBonus;
        public int lighthouseMaxSeaMovementBonus;
        public List<StartResourcesDef> startHumanResources;
        public List<StartResourcesDef> startComputerResources;
        public Dictionary<string, int> mineDailyIncome;
        /// <summary>Combat constants (combat/LuckAndMorale, combat/Creatures).</summary>
        public GameCombatRulesDef combat = new GameCombatRulesDef();
        /// <summary>Hero skill/perk coefficients (DefaultStats HeroSkills blocks).</summary>
        public GameSkillEffectsDef skillEffects = new GameSkillEffectsDef();
        /// <summary>Necromancy: slain creature -> risen creature.</summary>
        public Dictionary<string, string> necromancyTransform = new Dictionary<string, string>();
    }

    /// <summary>
    /// Combat constants read from DefaultStats.xdb. These are the shipped
    /// numbers; the attack/defence per-point damage coefficients are not in the
    /// data files and live in CombatRules with an "inferred" note.
    /// </summary>
    public class GameCombatRulesDef
    {
        public float defenceBonusOnDefend = 0.3f;
        public float goodLuckDamageMultiplier = 2f;
        public float badLuckDamageMultiplier = 0.5f;
        public float goodMoraleAtbValue = 0.5f;
        public float badMoraleAtbValue = 0.5f;
        public int heroInitiative = 10;
        public int spellInitiative = 10;
        public int atbBaseTime = 100000;
        public int atbEndTime = 20000;
    }

    /// <summary>
    /// Skill and perk effect coefficients from the HeroSkills blocks of
    /// DefaultStats.xdb. Rank lists are indexed by mastery - 1 (Basic..Expert,
    /// racial skills extend to ExtraExpert). All values are the shipped ones.
    /// </summary>
    public class GameSkillEffectsDef
    {
        // adventure map
        public int scoutingVisibilityIncrease = 4;
        public int disguiseVisibilityIncrease = 12;
        public float intelligenceManaMultiplier = 1.5f;
        public int warMachinesHealthPerSkill = 100;
        public float warMachinesPerkHealthMultiplier = 2f;

        // common combat skills
        public List<float> offenceDamageBonus = new List<float> { 0.05f, 0.1f, 0.15f };
        public float archeryDamageBonus = 0.2f;
        public List<float> defenceDamageReduce = new List<float> { 0.1f, 0.2f, 0.3f };
        public float evasionDamageReduce = 0.2f;

        // Knight
        public float retributionDamagePerMorale = 0.05f;
        public float holdGroundDefendMultiplier = 2f;
        public List<float> trainingRetaliationBonus = new List<float> { 0.05f, 0.1f, 0.2f, 0.25f };
        public float trainingCostDiscountPerLevel = 15f;
        public int roadHomeSpeedBonus = 2;

        // Demon Lord
        public List<float> gatingPortionByLevel = new List<float> { 0.3f, 0.35f, 0.4f, 0.45f };
        public float gatingMasteryPortionBonus = 0.2f;
        public float criticalGatingConstantProb = 0.1f;
        public float criticalGatingPerLuckPointProb = 0.05f;
        public int demonicFireBaseDamage = 10;
        public int demonicFireDamagePerPower = 10;

        // Necromancer
        public float necromancyRaiseBase = 10f;
        public float necromancyRaisePerLevel = 10f;
        public float lordOfUndeadRaiseBonus = 5f;
        public int spiritLinkHpForMana = 75;
        public int deathScreamMoralePenalty = 1;
        public float deathScreamInitiativePenalty = 0.1f;
        public int deathTreadSpeedBonus = 4;

        // Warlock / Wizard
        public float invocationChainBonusPerLevel = 0.1f;

        // Runemage + shared feats
        public float quicknessOfMindInitiativeBonus = 0.25f;
        public float tapRunesManaPerKnowledge = 0.5f;
        public float distractEnemyInitiativeDrop = 0.15f;
        public float setAfireDamageMultiplier = 0.33f;
        public int setAfireDurationRounds = 3;
        public float shrugDarknessSpellpowerMultiplier = 0.5f;
        public int runicArmorSpellpowerBonus = 4;
        public int runicAttunementMoraleBonus = 2;
        public int runicMachinesInitiativeBonus = 3;
        public float mentoringExpPercent = 0.25f;
        public float empathyAtbPerMorale = 0.1f;

        // Ranger
        public float elvenLuckDamageMultiplier = 2.25f;
        public float stormWindInitiativePenalty = 0.1f;
        public int stormWindSpeedPenalty = 1;
        public float fogVeilInitiativePenalty = 0.1f;
        public float fogVeilDamageDecrease = 0.1f;

        // Barbarian
        public int pathOfWarMovePoints = 350;
        public float stunningBlowAtbDrop = 0.1f;
        public float bodybuildingHpPercent = 10f;
        public float luckOfBarbarianMagicProofPerLuck = 0.05f;
        public float powerfullBlowCriticalChance = 10f;
        public float voiceTrainingCostDiscount = 0.2f;
        public int mightyVoiceHeroLevelBonus = 5;
        public float deathToNonexistentSummonDamageMult = 3f;
        public float corruptMagicCostIncrease = 0.5f;
        public float weakenMagicSpellpowerDecrease = 0.25f;
        public float detainMagicAtbDrop = 0.25f;
    }

    // -- hero skills -------------------------------------------------------

    /// <summary>
    /// One row of Skills.xdb: a ranked skill or one of its perks. ``kind`` is
    /// skill / standardPerk / classPerk / specialPerk / uniquePerk; perks point
    /// at their parent skill through ``skill``. Names/descriptions are per
    /// mastery rank (skills) or a single entry (perks), localized at import.
    /// </summary>
    public class SkillDef
    {
        public string id;
        public string kind;
        public string heroClass;
        public string skill;
        public List<string> names = new List<string>();
        public List<string> descriptions = new List<string>();
        public List<string> spellBuffs = new List<string>();
        /// <summary>Class -> dependency perks that must be owned first.</summary>
        public Dictionary<string, List<string>> prerequisites = new Dictionary<string, List<string>>();

        public bool IsSkill => kind == "skill";
        public int MaxMastery => names.Count > 0 ? names.Count : 1;

        public string DisplayName(int mastery = 1)
        {
            var i = Mathf.Clamp(mastery, 1, names.Count) - 1;
            return names.Count > 0 && !string.IsNullOrEmpty(names[i]) ? names[i] : GameDatabase.PrettifyEnum(id);
        }

        public string Description(int mastery = 1)
        {
            var i = Mathf.Clamp(mastery, 1, descriptions.Count) - 1;
            return descriptions.Count > 0 ? descriptions[i] : "";
        }

        /// <summary>
        /// True when this perk may be offered to a hero of ``heroClass`` who
        /// already owns ``owned``. Perks without prerequisite rows are gated by
        /// their parent skill alone.
        /// </summary>
        public bool AvailableFor(string heroClass, ICollection<string> owned)
        {
            if (prerequisites == null || prerequisites.Count == 0) return true;
            if (!prerequisites.TryGetValue(heroClass, out var deps)) return false;
            foreach (var d in deps)
                if (owned == null || !owned.Contains(d)) return false;
            return true;
        }
    }

    /// <summary>Level-up offer weights and primary-stat probabilities per class.</summary>
    public class HeroClassDef
    {
        public string id;
        public string name;
        public Dictionary<string, int> skillsProbs = new Dictionary<string, int>();
        public Dictionary<string, int> attributeProbs = new Dictionary<string, int>();
        public List<string> preferredSpells = new List<string>();

        public string DisplayName => string.IsNullOrEmpty(name) ? GameDatabase.PrettifyEnum(id) : name;
    }

    /// <summary>
    /// A named hero from the random-map pools (heroes.json). Keyed in the
    /// database by <c>Faction/File</c>; <see cref="townType"/> decides which
    /// faction's setup offers the hero.
    /// </summary>
    public class HeroDef
    {
        public string id;
        public string internalName;
        public string name;
        public string biography;
        public string townType;
        public List<string> pools = new List<string>();
        public string heroClass;
        public string specialization;
        public string specializationName;
        public MapSkillEntry primarySkill = new MapSkillEntry();
        public MapHeroPrimarySkills stats = new MapHeroPrimarySkills();
        public List<MapSkillEntry> skills = new List<MapSkillEntry>();
        public List<string> perks = new List<string>();
        public List<string> spells = new List<string>();
        public bool ballista;
        public bool firstAidTent;
        public bool ammoCart;
        /// <summary>Portrait source path (keys the texture manifest).</summary>
        public string face;
        public string faceSmall;

        public string DisplayName => string.IsNullOrEmpty(name) ? GameDatabase.PrettifyEnum(internalName ?? id) : name;
    }

    public class StartResourcesDef
    {
        public int wood;
        public int ore;
        public int mercury;
        public int crystal;
        public int sulfur;
        public int gem;
        public int gold;
    }

    [System.Serializable]
    public class PlayerResourceInventory
    {
        public int wood;
        public int ore;
        public int mercury;
        public int crystal;
        public int sulfur;
        public int gem;
        public int gold;

        public void Set(StartResourcesDef values)
        {
            if (values == null) return;
            wood = values.wood;
            ore = values.ore;
            mercury = values.mercury;
            crystal = values.crystal;
            sulfur = values.sulfur;
            gem = values.gem;
            gold = values.gold;
        }

        public void Add(string resource, int amount)
        {
            switch ((resource ?? "").ToUpperInvariant())
            {
                case "WOOD": wood += amount; break;
                case "ORE": ore += amount; break;
                case "MERCURY": mercury += amount; break;
                case "CRYSTAL": crystal += amount; break;
                case "SULFUR": sulfur += amount; break;
                case "GEM": case "GEMS": gem += amount; break;
                case "GOLD": gold += amount; break;
            }
        }

        public override string ToString()
        {
            return $"W {wood}   O {ore}   M {mercury}   C {crystal}   S {sulfur}   G {gem}   Gold {gold}";
        }
    }

    /// <summary>
    /// One <c>TownBuildingSharedStats</c> record: a single building of a single
    /// faction, at one upgrade tier.
    ///
    /// <see cref="key"/> is <c>"Haven/Peasant_Huts"</c> -- faction-qualified,
    /// because every faction ships a <c>Fort.xdb</c>. <see cref="dependencies"/>
    /// are more of those keys: what has to stand before this can be built.
    /// </summary>
    public class TownBuildingDef
    {
        public string id;
        public string key;
        public string faction;
        public string source;
        public string name;
        public string description;
        public string type;
        public string upgrade;
        public ResourceCost cost;
        /// <summary>Creatures bred per week; the dwelling creature's WeeklyGrowth.</summary>
        public int growth;
        /// <summary>Weekly-growth multiplier this building grants (Citadel 1.5, Castle 2).</summary>
        public float growthMultiplier = 1f;
        public int incomeGoldPerDay;
        public List<string> dependencies;
        /// <summary>The creature this dwelling recruits (CREATURE_UNKNOWN = none).</summary>
        public string creature;
        /// <summary>Second creature an upgraded dwelling opens (alternate upgrade).</summary>
        public string creature2;
        public int devLevelNeeded;
        public string uiObjectName;
        public int uiStackPosition;
        public string icon;

        public bool IsDwelling => !string.IsNullOrEmpty(type) && type.StartsWith("TB_DWELLING");
        public bool IsFort => type == "TB_FORT";
        public bool HasCreature => !string.IsNullOrEmpty(creature) && creature != "CREATURE_UNKNOWN";
        public string DisplayName => string.IsNullOrEmpty(name) ? key : name;

        /// <summary>BLD_UPG_2 -&gt; 2; BLD_UPG_NONE -&gt; 0. Tiers order the upgrade chain.</summary>
        public static int TierOf(string upgrade)
        {
            if (string.IsNullOrEmpty(upgrade) || upgrade == "BLD_UPG_NONE") return 0;
            var tail = upgrade.Substring(upgrade.LastIndexOf('_') + 1);
            return int.TryParse(tail, out var tier) ? tier : 0;
        }

        public int Tier => TierOf(upgrade);

        public string CostText() => cost != null ? cost.ToString() : "free";
    }

    /// <summary>
    /// One <c>TOWN_*</c> row of <c>RefTables/TownTypesInfo.xdb</c> plus the
    /// building list its <c>AdvMapTownShared</c> record carries.
    /// </summary>
    public class TownTypeDef
    {
        public string id;
        public string name;
        public string fortName;
        public string citadelName;
        public string castleName;
        /// <summary>The TownBuildingSharedStats folder this faction builds from.</summary>
        public string faction;
        /// <summary>
        /// Original TownTypesInfo plotting parts used by the adventure path
        /// preview. Values are model geometry UIDs plus source texture hrefs.
        /// </summary>
        public Dictionary<string, PathPlottingDef> pathPlotting;
        /// <summary>Every building key the town owns, in town-screen order.</summary>
        public List<string> buildings;
        public string neutralCreature;
        public string nativeWarMachine;
        /// <summary>What the resource silo (marketplace tier 2) pays per day.</summary>
        public ResourceCost siloIncome;
        public int moatMaxDamage;
        public List<TownMoatSpellDef> moatSpells;

        public string DisplayName => string.IsNullOrEmpty(name) ? GameDatabase.PrettifyEnum(id) : name;
    }

    public class TownMoatSpellDef
    {
        public string spell;
        public int chance;
        public string mastery;
        public int power;
    }

    /// <summary>One source-backed path marker model from TownTypesInfo.plotting.</summary>
    public class PathPlottingDef
    {
        public string source;
        public string geometryUid;
        public List<string> textures;
    }

    // -- maps --------------------------------------------------------------

    /// <summary>
    /// One material of a model. Mirrors the Material block of a Model XDB.
    /// AlphaMode is the one that matters visually: AM_ALPHA_TEST is the single
    /// most common value in the data (5244 of 11638 materials), and without it
    /// every bush, fence and railing renders as a solid rectangle.
    /// </summary>
    public class MaterialDef
    {
        public string texture;
        public string bump;
        public string alphaMode;
        public bool twoSided;
        public bool selfIllum;
        public float spec;
        public float gloss;
    }

    /// <summary>
    /// The clips a model can play, keyed by kind ("idle00", "move", "attack00").
    /// Produced from Character -&gt; AnimSet -&gt; BasicSkelAnim, and keyed by the
    /// geometry uid a map object already carries, so an object on the map reaches
    /// a clip in one lookup.
    /// </summary>
    public class ModelAnimDef
    {
        public string skeleton;
        public string model;
        public Dictionary<string, string> arena;
        public Dictionary<string, string> advmap;
    }

    /// <summary>One painted ground texture and how much of the map it covers.</summary>
    public class TerrainLayerInfo
    {
        public string tile;
        public string texture;
        public int priority;
        public string type;
        public float opaque;
    }

    public class TerrainLayersFile
    {
        public string map;
        public string plane;
        public int vertexStride;
        public List<TerrainLayerInfo> layers;
    }

    /// <summary>One authored building slot of a map's town.</summary>
    /// <summary>
    /// One authored building slot of a map's town: what it starts as, and how far
    /// this scenario lets it be upgraded.
    /// </summary>
    public class MapBuildingStateDef
    {
        public string type;
        /// <summary>The tier the town starts at (BLD_UPG_NONE = not built).</summary>
        public string upgrade;
        /// <summary>The tier this scenario allows it to reach.</summary>
        public string maxUpgrade;

        public int Tier => TownBuildingDef.TierOf(upgrade);
        public int MaxTier => TownBuildingDef.TierOf(maxUpgrade);
    }

    public class MapArmyStackDef
    {
        public string creature;
        public int amount;
        public int count;
        public bool customAmount;
    }

    /// <summary>One (skill, mastery) pair from a map hero's Editable block.</summary>
    public class MapSkillEntry
    {
        public string skill;
        public int mastery;
    }

    /// <summary>Primary stats as authored on a map hero.</summary>
    public class MapHeroPrimarySkills
    {
        public int offence;
        public int defence;
        public int spellpower;
        public int knowledge;
    }

    public class MapTilePointDef
    {
        public int x;
        public int y;
    }

    public class MapQuestDef
    {
        public string name;
        public string kind;
        public string caption;
        public string description;
        public int timeout;
        public int holdout;
    }

    public class MapObjectDef
    {
        public string kind;
        public string objectId;
        public float x;
        public float y;
        public float z;
        public float rot;
        public int floor;
        public string name;
        public string shared;
        public string geometryUid;
        public List<string> textures;
        public List<MaterialDef> materials;
        public List<List<int>> blocked;
        /// <summary>
        /// Tile offsets (relative to x/y) that trigger a visit when a hero
        /// walks onto them. The blocked footprint is impassable, so these are
        /// the actual interaction tiles the shared record declares.
        /// </summary>
        public List<List<int>> activeTiles;
        /// <summary>Treasure pickup roll range from the shared record (see H5ObjectVisits).</summary>
        public int minResource;
        public int maxResource;

        // AdvMapMonster
        public string creature;
        public int amount;
        public bool customAmount;
        public string attackType;
        public string mood;

        // AdvMapHero level-up history (Editable block)
        public List<MapSkillEntry> skills;
        public List<string> perks;
        public List<string> spells;
        public int experience;
        public MapHeroPrimarySkills primarySkills;

        // AdvMapTown / AdvMapHero / AdvMapMine / AdvMapArtifact
        public string townType;
        public string owner;
        public string hero;
        public string resource;
        public string artifact;
        public string guardType;
        public List<MapBuildingStateDef> buildings;
        public List<MapArmyStackDef> garrison;
        public List<MapArmyStackDef> additionalStacks;
        public List<MapArmyStackDef> army;
        public string garrisonHero;
        public List<string> bannedRaces;
        public bool isCustom;
        public string message;
        public string sharedType;
        public string buildingType;
        public string randomSource;
        public string linkToPlayer;
        public string linkToTown;
        public string spell;
        public string prisonedHero;
        public string riddle;
        public string keyColor;
        public int amount2;
        public int groupId;
        public int spellLevel;
        public int cost;
        public bool randomCreatures;
        public bool collectableArmy;
        public bool allowQuickCombat;
        public bool randomHero;
        public bool randomRiddle;
        public bool creatureSwapBlockedForAI;
        public List<string> creatures;
        public List<int> availableResources;
        public MapTilePointDef shipTile;
        public Dictionary<string, List<string>> upgradeFilter;
        public MapQuestDef quest;
    }

    [System.Serializable]
    public class MapFogCircleDef
    {
        public int x;
        public int y;
        public int floor;
        public int radius;
        public string player;
    }

    public class MapDef
    {
        public string id;
        public string source;
        public int tileX;
        public int tileY;
        public int initialFloor;
        public bool hasSurface;
        public bool hasUnderground;
        public Dictionary<string, int> objectCounts;
        public List<MapObjectDef> objects;
        // Literal top-level OpenCircleFog calls from the map's shipped Lua
        // bootstrap. Event/function calls are intentionally not flattened here.
        public List<MapFogCircleDef> fogBootstrap;
    }

    public class MapIndexEntry
    {
        public string id;
        public int tileX;
        public int tileY;
        public int objects;
        public Dictionary<string, int> counts;
        /// <summary>True for Maps/CombatArenas scenery, used as battlefields.</summary>
        public bool arena;
    }

    public class CampaignBonusDef
    {
        public bool targetHero;
        public string type;
        public string creature;
        public int creatureCount;
        public string artifact;
        public ResourceCost resources;
        public string spell;
        public string building;
    }

    public class CampaignHeroDef
    {
        public string hero;
        public string targetCampaign;
        public int targetMission;
    }

    public class CampaignMissionDef
    {
        public string id;
        public string mapId;
        public string mapSource;
        public string name;
        public string description;
        public int tileX;
        public int tileY;
        public List<CampaignBonusDef> bonuses;
        public List<CampaignHeroDef> heroes;
        public int heroCount;
    }

    public class CampaignDef
    {
        public string id;
        public string source;
        public string name;
        public string fullName;
        public string description;
        public List<string> dependentCampaigns;
        public List<CampaignMissionDef> missions;
        public bool userCampaign;
    }

    // -- terrain -----------------------------------------------------------

    public class TerrainInfo
    {
        public string map;
        public string plane;
        public int tileX;
        public int tileY;
        public int vertexCount;
        public float[] heightRange;
        public Dictionary<string, int> flags;
        public int waterVertices;
        public bool hasPassability;
        public int riverGrid;

        /// <summary>Planes are stored per vertex on a (tiles+1) square grid.</summary>
        public int Tiles => tileX;
    }
}
