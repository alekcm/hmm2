using System.Collections.Generic;
using HoMM5.Map;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>Which side of the battlefield a unit fights on (advmap-startup.lua).</summary>
    public enum CombatSide
    {
        Attacker = 0,
        Defender = 1,
    }

    /// <summary>What kind of thing a unit is (advmap-startup.lua: CREATURE/WAR_MACHINE).</summary>
    public enum CombatUnitType
    {
        Creature = 1,
        WarMachine = 2,
    }

    /// <summary>
    /// A timed status on a stack: poison, blindness, stun, weakness and the
    /// ability marks. Durations tick at the unit's own turn.
    /// </summary>
    public class CombatEffect
    {
        public const string Poison = "poison";
        public const string Blind = "blind";
        public const string Stun = "stun";
        public const string Weakness = "weakness";     // attack penalty (cursing attack, Weakness spell)
        public const string Vulnerability = "vulnerability";  // defence penalty
        public const string Blessed = "blessed";       // max damage roll
        public const string Cursed = "cursed";         // min damage roll
        public const string Hasted = "hasted";
        public const string Slowed = "slowed";
        public const string StoneSkin = "stoneSkin";
        public const string DivineStrength = "divineStrength";
        public const string Frenzy = "frenzy";
        public const string Vampirism = "vampirism";
        public const string Regeneration = "regeneration";
        public const string FireMark = "fireMark";     // fire vulnerability (mark of fire)
        public const string NoHeal = "noHeal";         // wound: cannot be healed/resurrected
        public const string Hexed = "hexed";           // luck penalty (hexing attack)
        public const string Immunity = "immunity";     // magical immunity

        public string Kind;
        public int Turns;
        public float Magnitude;    // percent or flat value, effect-specific
        public CombatSide SourceSide;
    }

    /// <summary>
    /// One fighting stack. A stack is a creature type and a count; damage eats
    /// the top creature's health first, then the count.
    /// </summary>
    public class CombatUnit
    {
        public int Id;
        public CombatSide Side;
        public string CreatureId = "";
        public HoMM5.Data.CreatureDef Def;

        public int Count;
        /// <summary>Stack size when the battle started (casualty accounting).</summary>
        public int StartCount;
        /// <summary>Health remaining on the stack's current top creature.</summary>
        public int HealthLeft;

        public CombatUnitType UnitType = CombatUnitType.Creature;
        /// <summary>War machine kind (WAR_MACHINE_BALLISTA ...), null for creatures.</summary>
        public string WarMachineId;
        /// <summary>Damage factor tier for war machines (War Machines skill).</summary>
        public int WarMachineTier;
        /// <summary>Machine offence as a combat stat and its damage factors.</summary>
        public int WarMachineAttack;
        public int WarMachineMinFactor = 2;
        public int WarMachineMaxFactor = 3;

        /// <summary>Timed statuses on the stack.</summary>
        public readonly List<CombatEffect> Effects = new List<CombatEffect>();

        /// <summary>Friendly stacks died since battle start (enrage feed).</summary>
        public int FriendlyDeathsSeen;
        /// <summary>Cells the unit moved before its last attack (jousting/charge).</summary>
        public int CellsMovedLastTurn;
        /// <summary>Set when the unit was summoned this battle (not army casualties).</summary>
        public bool Summoned;

        /// <summary>Grid cell on the battlefield.</summary>
        public int X;
        public int Y;

        public int ShotsLeft;
        public bool Defended;
        public bool RetaliatedThisRound;
        public bool HasActedThisRound;
        public bool WaitedThisRound;

        /// <summary>Leader for stat bonuses; null for neutral monsters.</summary>
        public H5HeroState Leader;

        public bool IsAlive => Count > 0;
        public bool IsRanged => Def != null && Def.Shots > 0;
        public bool IsFlying => Def != null && Def.flying;
        public int MaxHealth => Def != null && Def.Health > 0 ? Def.Health : 1;

        /// <summary>Walk speed with hasted/slowed applied.</summary>
        public int Speed
        {
            get
            {
                var speed = Def != null ? Mathf.Max(1, Def.Speed) : 5;
                if (HasEffect(CombatEffect.Hasted)) speed = Mathf.RoundToInt(speed * 1.3f);
                if (HasEffect(CombatEffect.Slowed)) speed = Mathf.Max(1, Mathf.RoundToInt(speed * 0.7f));
                return speed;
            }
        }

        public int Initiative => Def != null ? Mathf.Max(1, Def.Initiative) : 8;

        /// <summary>Attack with hero bonus, enragement and weakening effects.</summary>
        public int AttackSkill
        {
            get
            {
                var a = (Def != null ? Def.AttackSkill : WarMachineAttack) + (Leader != null ? Leader.Attack : 0);
                if (FriendlyDeathsSeen > 0 && CombatAbilities.HasAbility(this, "ABILITY_ENRAGED"))
                    a += Mathf.RoundToInt(a * CombatAbilities.EnragePerDeath * FriendlyDeathsSeen);
                if (HasEffect(CombatEffect.Weakness))
                    a = Mathf.RoundToInt(a * (1f - FindEffect(CombatEffect.Weakness).Magnitude));
                if (HasEffect(CombatEffect.DivineStrength))
                    a = Mathf.RoundToInt(a * (1f + FindEffect(CombatEffect.DivineStrength).Magnitude));
                if (HasEffect(CombatEffect.Frenzy))
                    a = Mathf.RoundToInt(a * (1f + FindEffect(CombatEffect.Frenzy).Magnitude));
                return a;
            }
        }

        public int DefenceSkill
        {
            get
            {
                var d = (Def != null ? Def.DefenceSkill : 0) + (Leader != null ? Leader.Defence : 0);
                if (HasEffect(CombatEffect.Vulnerability))
                    d = Mathf.RoundToInt(d * (1f - FindEffect(CombatEffect.Vulnerability).Magnitude));
                if (HasEffect(CombatEffect.StoneSkin))
                    d = Mathf.RoundToInt(d * (1f + FindEffect(CombatEffect.StoneSkin).Magnitude));
                if (HasEffect(CombatEffect.Frenzy))
                    d = Mathf.RoundToInt(d * (1f - FindEffect(CombatEffect.Frenzy).Magnitude));
                if (Defended) d = Mathf.RoundToInt(d * (1f + CombatRules.DefenceBonusOnDefend));
                return d;
            }
        }

        public int MinDamage => Def != null ? Def.MinDamage : 1;
        public int MaxDamage => Def != null ? Mathf.Max(Def.MinDamage, Def.MaxDamage) : 1;

        public string DisplayName => Def != null ? Def.DisplayName
            : (UnitType == CombatUnitType.WarMachine
                ? HoMM5.Data.GameDatabase.PrettifyEnum(WarMachineId ?? "War Machine")
                : CreatureId);

        // -- effects -------------------------------------------------------

        public bool HasEffect(string kind)
        {
            for (var i = 0; i < Effects.Count; i++)
                if (Effects[i].Kind == kind) return true;
            return false;
        }

        public CombatEffect FindEffect(string kind)
        {
            for (var i = 0; i < Effects.Count; i++)
                if (Effects[i].Kind == kind) return Effects[i];
            return null;
        }

        public void AddEffect(string kind, int turns, float magnitude, CombatSide source)
        {
            var existing = FindEffect(kind);
            if (existing != null)
            {
                existing.Turns = Mathf.Max(existing.Turns, turns);
                existing.Magnitude = Mathf.Max(existing.Magnitude, magnitude);
                return;
            }
            Effects.Add(new CombatEffect
            {
                Kind = kind,
                Turns = turns,
                Magnitude = magnitude,
                SourceSide = source,
            });
        }

        public void RemoveEffect(string kind)
        {
            for (var i = Effects.Count - 1; i >= 0; i--)
                if (Effects[i].Kind == kind) Effects.RemoveAt(i);
        }

        /// <summary>Tick durations at the unit's turn; returns expired kinds for logging.</summary>
        public List<string> TickEffects()
        {
            var expired = new List<string>();
            for (var i = Effects.Count - 1; i >= 0; i--)
            {
                if (Effects[i].Kind == CombatEffect.NoHeal || Effects[i].Kind == CombatEffect.FireMark)
                    continue;   // battlefield-long marks
                Effects[i].Turns--;
                if (Effects[i].Turns <= 0)
                {
                    expired.Add(Effects[i].Kind);
                    Effects.RemoveAt(i);
                }
            }
            return expired;
        }

        /// <summary>Total health of the stack: full creatures plus the wounded one.</summary>
        public int TotalHealth => (Count - 1) * MaxHealth + HealthLeft;

        public CombatUnit Clone()
        {
            return (CombatUnit)MemberwiseClone();
        }
    }

    /// <summary>One army stack as it lives on a hero between battles.</summary>
    public class ArmyStackState
    {
        public string CreatureId = "";
        public int Count;

        public ArmyStackState() { }
        public ArmyStackState(string creatureId, int count)
        {
            CreatureId = creatureId;
            Count = count;
        }
    }

    /// <summary>How a battle ended.</summary>
    public enum CombatOutcome
    {
        AttackerWon,
        DefenderWon,
        Fled,
    }

    /// <summary>
    /// What the adventure layer needs after the dust settles: who won, the
    /// hero's surviving army, the monster's surviving stacks, experience.
    /// </summary>
    public class CombatResult
    {
        public CombatOutcome Outcome;
        public int Experience;
        public int CreaturesKilledByAttacker;
        public List<ArmyStackState> AttackerSurvivors = new List<ArmyStackState>();
        /// <summary>Monster-side survivors, so a rematch sees the same wounded stack.</summary>
        public List<ArmyStackState> DefenderSurvivors = new List<ArmyStackState>();
        public readonly List<string> Log = new List<string>();
    }

    /// <summary>A move/attack order the AI picked; the view layer animates it.</summary>
    public class CombatOrder
    {
        public int MoveX = -1;
        public int MoveY = -1;
        public CombatUnit Target;
        public bool Ranged;
        public bool Defend;
        public bool Wait;
        /// <summary>Target is a friendly to heal (first aid tent), not to attack.</summary>
        public bool Aid;
        /// <summary>The action already happened inside the AI; just end the turn.</summary>
        public bool Done;
    }

    /// <summary>One piece of siege works: wall segment, gate or tower.</summary>
    public class SiegeStructure
    {
        public const string Wall = "WALL";
        public const string Gate = "GATE";
        public const string LeftTower = "LEFT_TOWER";
        public const string BigTower = "BIG_TOWER";
        public const string RightTower = "RIGHT_TOWER";
        public const string Moat = "MOAT";

        public string Kind;
        public int MaxHealth;
        public int Health;
        /// <summary>Grid cells the structure blocks (walls/gate), towers sit beside.</summary>
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        public int X;   // anchor cell for towers/visuals
        public int Y;

        public bool IsAlive => Health > 0;
        public bool IsTower => Kind == LeftTower || Kind == BigTower || Kind == RightTower;
    }

    /// <summary>
    /// The siege works of one battle. Wall segments block the defender's
    /// courtyard until breached; towers shoot; the moat burns whoever crosses.
    /// </summary>
    public class SiegeState
    {
        public int FortLevel;   // 0 = none, 1 = Fort, 2 = Citadel, 3 = Castle
        public readonly List<SiegeStructure> Structures = new List<SiegeStructure>();
        /// <summary>Cells the moat covers (movement damage).</summary>
        public readonly HashSet<Vector2Int> MoatCells = new HashSet<Vector2Int>();

        public bool HasWalls => Structures.Exists(s => s.Kind == SiegeStructure.Wall && s.IsAlive);

        public SiegeStructure StructureAt(int x, int y)
        {
            foreach (var s in Structures)
                if (s.IsAlive && s.Cells.Contains(new Vector2Int(x, y))) return s;
            return null;
        }

        public List<SiegeStructure> Towers
        {
            get
            {
                var result = new List<SiegeStructure>();
                foreach (var s in Structures)
                    if (s.IsTower && s.IsAlive) result.Add(s);
                return result;
            }
        }
    }
}
