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

    public class TownBuildingDef
    {
        public string id;
        public string source;
        public string name;
        public ResourceCost cost;
        public int growth;
    }

    public class DefaultAdventureStatsDef
    {
        public int baseHeroMovement = 2500;
        public int baseHeroLookRange = 12;
        public int heroManaPerKnowledge = 10;
        public int visitStablesMovementBonus = 600;
        public int visitOasisMovementBonus = 800;
        public int visitRallyFlagMovementBonus = 400;
    }

    public class DefaultCombatStatsDef
    {
        public float damageIncrease = 0.05f;
        public float damageIncreaseCap = 3.0f;
        public float damageDecrease = 0.05f;
        public float damageDecreaseCap = 0.1f;
        public float defenceBonusOnDefend = 0.3f;
        public float goodLuckDamageMultiplier = 2.0f;
        public float badLuckDamageMultiplier = 0.5f;
        public float goodMoraleAtbValue = 0.5f;
        public float badMoraleAtbValue = 0.0f;
    }

    /// <summary>
    /// Global RPGStats from <c>/GameMechanics/RPGStats/DefaultStats.xdb</c>
    /// (ToTE 3.1 <c>a2p1-data</c>), accessed in <c>H5_Game_PEST.exe</c> via
    /// <c>NWorld::GetDefaultStats()</c> (<c>0x00bcb3f0</c>).
    /// </summary>
    public class DefaultStatsDef
    {
        public string source;
        public int heroInitiative = 10;
        public int spellInitiative = 10;
        public int atbEndTime = 20000;
        public int atbBaseTime = 100000;
        public int rumorCost = 100;
        public DefaultAdventureStatsDef adventure = new DefaultAdventureStatsDef();
        public DefaultCombatStatsDef combat = new DefaultCombatStatsDef();
    }

    // -- maps --------------------------------------------------------------

    public class MapObjectDef
    {
        public string kind;
        public float x;
        public float y;
        public float z;
        public float rot;
        public int floor;
        public string name;
        public string shared;
        public string geometryUid;
        public List<string> textures;
        public List<List<int>> blocked;

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
    }

    public class MapDef
    {
        public string id;
        public string source;
        public int tileX;
        public int tileY;
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
