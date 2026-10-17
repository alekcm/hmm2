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
        public int Speed => Def != null ? Mathf.Max(1, Def.Speed) : 5;
        public int Initiative => Def != null ? Mathf.Max(1, Def.Initiative) : 8;
        public int AttackSkill => (Def != null ? Def.AttackSkill : 0) + (Leader != null ? Leader.Attack : 0);
        public int DefenceSkill
        {
            get
            {
                var d = (Def != null ? Def.DefenceSkill : 0) + (Leader != null ? Leader.Defence : 0);
                return Defended ? Mathf.RoundToInt(d * (1f + CombatRules.DefenceBonusOnDefend)) : d;
            }
        }

        public int MinDamage => Def != null ? Def.MinDamage : 1;
        public int MaxDamage => Def != null ? Mathf.Max(Def.MinDamage, Def.MaxDamage) : 1;

        public string DisplayName => Def != null ? Def.DisplayName : CreatureId;

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
    }
}
