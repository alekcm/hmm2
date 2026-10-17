using System.Collections.Generic;

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
        public int level;
        public string school;
        public string element;
        public string target;
        public int requiredHeroLevel;
        public bool isAimed;
        public bool isAreaAttack;
        public ResourceCost cost;
        public List<string> visuals;

        public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
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

    public class TownBuildingDef
    {
        public string id;
        public string source;
        public string name;
        public string type;
        public string upgrade;
        public ResourceCost cost;
        public int growth;
        public int incomeGoldPerDay;
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

    public class MapBuildingStateDef
    {
        public string type;
        public string upgrade;
    }

    public class MapArmyStackDef
    {
        public string creature;
        public int amount;
        public int count;
        public bool customAmount;
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
    }

    public class MapIndexEntry
    {
        public string id;
        public int tileX;
        public int tileY;
        public int objects;
        public Dictionary<string, int> counts;
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
