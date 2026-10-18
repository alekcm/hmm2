using System;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.World
{
    /// <summary>
    /// Decompiled implementation of <c>NWorld::CCombatCreature</c> (vftable <c>0x00e4fba4</c>).
    /// </summary>
    public class NWorldCombatStack
    {
        public CreatureDef Definition { get; }
        public NWorldHero OwnerHero { get; }
        public int Side { get; }
        public int Count { get; private set; }
        public int TopUnitHealth { get; private set; }
        public int ShotsRemaining { get; set; }
        public bool HasRetaliatedThisRound { get; set; }
        public bool IsDefending { get; private set; }
        public bool IsSummoned { get; set; }
        public Vector2Int GridPos { get; set; }

        /// <summary>
        /// ATB progress in <c>[0, 1]</c>, where <c>1.0f</c> means ready to act.
        /// </summary>
        public float AtbValue { get; set; }

        public bool IsAlive => Count > 0 && TopUnitHealth > 0;

        public NWorldCombatStack(CreatureDef def, int count, int side, NWorldHero ownerHero = null)
        {
            Definition = def ?? throw new ArgumentNullException(nameof(def));
            Count = Mathf.Max(1, count);
            Side = side;
            OwnerHero = ownerHero;
            TopUnitHealth = Mathf.Max(1, def.Health);
            ShotsRemaining = def.Shots;
            AtbValue = 0f;
        }

        public int EffectiveAttack => Mathf.Max(0, Definition.AttackSkill + (OwnerHero != null ? OwnerHero.Attack : 0));

        public int EffectiveDefence
        {
            get
            {
                var def = Mathf.Max(0, Definition.DefenceSkill + (OwnerHero != null ? OwnerHero.Defence : 0));
                if (IsDefending)
                    def = Mathf.RoundToInt(def * 1.3f); // DefenceBonusOnDefend = 0.3 (0x00a5e660)
                return def;
            }
        }

        public float EffectiveInitiative => Mathf.Max(1f, Definition.Initiative);

        public void BeginTurn()
        {
            IsDefending = false;
        }

        public void Wait()
        {
            IsDefending = false;
            AtbValue = 0.5f; // 0x00a5e610: Wait sets ATB to 0.5f (half turn delay)
        }

        public void Defend()
        {
            IsDefending = true;
            AtbValue = 0.0f; // 0x00a5e660: Defend consumes full turn (1.0f period)
        }

        public void TriggerGoodMorale(float goodMoraleAtb = 0.5f)
        {
            AtbValue = Mathf.Clamp01(goodMoraleAtb); // 0x00a5e515: GoodMorale_ATBValue = 0.5
        }

        /// <summary>
        /// Applies total damage to the stack and returns the number of creatures killed.
        /// </summary>
        public int ApplyDamage(int damage, out int totalXpAwarded)
        {
            totalXpAwarded = 0;
            if (damage <= 0 || !IsAlive) return 0;

            var maxHp = Mathf.Max(1, Definition.Health);
            var totalPool = (Count - 1) * maxHp + TopUnitHealth;
            var remainingPool = Mathf.Max(0, totalPool - damage);

            var prevCount = Count;
            if (remainingPool == 0)
            {
                Count = 0;
                TopUnitHealth = 0;
            }
            else
            {
                Count = (remainingPool + maxHp - 1) / maxHp;
                TopUnitHealth = remainingPool - (Count - 1) * maxHp;
            }

            var killed = prevCount - Count;
            totalXpAwarded = NWorldCombat.CalcUnitExperienceValue(Definition, killed, IsSummoned);
            return killed;
        }
    }

    /// <summary>
    /// Decompiled implementation of <c>NWorld::CCombat</c> (vftable <c>0x00e79d3c</c>)
    /// and <c>NWorld::CArenaATB</c> (vftable <c>0x00e6e250</c>) from <c>H5_Game_PEST.exe</c>.
    /// </summary>
    public class NWorldCombat
    {
        public const float DefaultAtbBaseTime = 100000.0f;
        public const float DefaultHeroInitiative = 10.0f;
        public const float AttackDamageCoef = 0.05f;   // .rdata 0x00e0cd50
        public const float DefenseDamageCoef = 0.05f;  // DefaultStats.combat.damageDecrease
        public const float OneThird = 0.33333334f;     // .rdata 0x00e0ae38

        private readonly DefaultStatsDef _stats;
        public List<NWorldCombatStack> Stacks { get; } = new List<NWorldCombatStack>();

        public NWorldCombat(DefaultStatsDef defaultStats = null)
        {
            _stats = defaultStats ?? new DefaultStatsDef();
        }

        /// <summary>
        /// Exact decompiled <c>NWorld::SFLB0_GetUnitExperienceValue</c> (<c>0x004d8ba0</c>):
        /// <c>isSummoned ? 0 : (creature.Exp &gt; 0 ? creature.Exp : creature.Power) * count</c>.
        /// </summary>
        public static int CalcUnitExperienceValue(CreatureDef creature, int count, bool isSummoned = false)
        {
            if (isSummoned || creature == null || count <= 0) return 0;
            var unitExp = creature.Exp > 0 ? creature.Exp : creature.Power;
            return unitExp * count;
        }

        /// <summary>
        /// Exact decompiled Attack-Defense multiplier from <c>0x00a58d30</c> and <c>0x00a58de0</c>:
        /// - If <c>attack &gt; defense</c>: <c>1.0f + 0.05f * (attack - defense)</c>
        /// - If <c>defense &gt;= attack</c>: <c>1.0f / (1.0f + 0.05f * (defense - attack))</c>
        /// </summary>
        public static float CalcAttackDefenseMultiplier(int attack, int defense)
        {
            if (attack > defense)
                return 1.0f + AttackDamageCoef * (attack - defense);
            return 1.0f / (1.0f + DefenseDamageCoef * (defense - attack));
        }

        /// <summary>
        /// Exact decompiled base damage roll from <c>0x00a597a0</c>:
        /// - If <c>minDmg == maxDmg</c>: <c>minDmg * count</c>
        /// - If <c>count &lt;= 3</c>: sum of <c>count</c> independent rolls in <c>[minDmg, maxDmg]</c>
        /// - If <c>count &gt; 3</c>: sum of 3 rolls multiplied by <c>count * 0.33333334f</c>
        /// </summary>
        public static float RollBaseStackDamage(int minDmg, int maxDmg, int count, System.Random rng = null)
        {
            if (count <= 0) return 0f;
            if (minDmg >= maxDmg) return minDmg * (float)count;

            rng = rng ?? new System.Random();
            if (count <= 3)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                    sum += rng.Next(minDmg, maxDmg + 1);
                return sum;
            }
            else
            {
                var sum3 = 0;
                for (var i = 0; i < 3; i++)
                    sum3 += rng.Next(minDmg, maxDmg + 1);
                return sum3 * (float)count * OneThird;
            }
        }

        /// <summary>
        /// Computes full physical attack damage including Attack/Defense ratio (<c>0x00a58d30</c>),
        /// ranged/melee penalties (<c>0x00a5a900</c>), Jousting bonus (<c>0x00a5c8ca</c>), and Luck.
        /// </summary>
        public static int CalcPhysicalDamage(
            NWorldCombatStack attacker,
            NWorldCombatStack defender,
            bool isMelee,
            bool isHalfRangeOrObstacle = false,
            int tilesMovedForJousting = 0,
            int luckState = 0,
            System.Random rng = null)
        {
            if (attacker == null || defender == null || !attacker.IsAlive) return 0;

            var baseDmg = RollBaseStackDamage(
                attacker.Definition.MinDamage,
                attacker.Definition.MaxDamage,
                attacker.Count,
                rng);

            var adMult = CalcAttackDefenseMultiplier(attacker.EffectiveAttack, defender.EffectiveDefence);
            var dmg = baseDmg * adMult;

            // Shooter in melee penalty (* 0.5f at 0x00a5a9d0) unless ABILITY_NO_MELEE_PENALTY
            if (isMelee && attacker.Definition.IsRanged && !HasAbility(attacker.Definition, "ABILITY_NO_MELEE_PENALTY"))
                dmg *= 0.5f;

            // Ranged half-range / obstacle penalty (* 0.5f at 0x00a5a990) unless ABILITY_NO_RANGE_PENALTY
            if (!isMelee && isHalfRangeOrObstacle && !HasAbility(attacker.Definition, "ABILITY_NO_RANGE_PENALTY"))
                dmg *= 0.5f;

            // Jousting charge bonus: +5% per tile moved (0x00a5c8ca)
            if (isMelee && tilesMovedForJousting > 0 && HasAbility(attacker.Definition, "ABILITY_JOUSTING"))
                dmg *= 1.0f + 0.05f * tilesMovedForJousting;

            // Good / Bad Luck (DefaultStats.combat.goodLuckDamageMultiplier = 2.0f, badLuck = 0.5f)
            if (luckState > 0) dmg *= 2.0f;
            else if (luckState < 0) dmg *= 0.5f;

            return Mathf.Max(1, (int)dmg);
        }

        /// <summary>
        /// Exact decompiled <c>NWorld::CArenaATB</c> unit period (<c>0x00a2d6c0</c>):
        /// <c>100000.0f / max(initiative, 1.0f)</c>.
        /// </summary>
        public float GetCreatureAtbPeriod(float initiative)
        {
            var atbBase = _stats != null && _stats.atbBaseTime > 0 ? _stats.atbBaseTime : DefaultAtbBaseTime;
            return atbBase / Mathf.Max(1.0f, initiative);
        }

        /// <summary>
        /// Exact decompiled <c>NWorld::CArenaATB</c> hero period (<c>0x00a2d700</c>):
        /// <c>100000.0f / 10.0f</c>.
        /// </summary>
        public float GetHeroAtbPeriod()
        {
            var atbBase = _stats != null && _stats.atbBaseTime > 0 ? _stats.atbBaseTime : DefaultAtbBaseTime;
            var heroInit = _stats != null && _stats.heroInitiative > 0 ? _stats.heroInitiative : DefaultHeroInitiative;
            return atbBase / Mathf.Max(1.0f, heroInit);
        }

        /// <summary>
        /// Advances the ATB timeline until the next living stack reaches <c>AtbValue &gt;= 1.0f</c>
        /// and returns that stack.
        /// </summary>
        public NWorldCombatStack AdvanceToNextTurn()
        {
            NWorldCombatStack nextStack = null;
            var minTimeRemaining = float.MaxValue;

            foreach (var s in Stacks)
            {
                if (!s.IsAlive) continue;
                var period = GetCreatureAtbPeriod(s.EffectiveInitiative);
                var remaining = Mathf.Max(0f, 1.0f - s.AtbValue) * period;
                if (remaining < minTimeRemaining)
                {
                    minTimeRemaining = remaining;
                    nextStack = s;
                }
            }

            if (nextStack == null) return null;

            foreach (var s in Stacks)
            {
                if (!s.IsAlive) continue;
                var period = GetCreatureAtbPeriod(s.EffectiveInitiative);
                s.AtbValue += minTimeRemaining / period;
            }

            nextStack.AtbValue = Mathf.Max(0f, nextStack.AtbValue - 1.0f);
            nextStack.BeginTurn();
            return nextStack;
        }

        private static bool HasAbility(CreatureDef def, string abilityId)
        {
            if (def?.abilities == null) return false;
            for (var i = 0; i < def.abilities.Count; i++)
                if (string.Equals(def.abilities[i], abilityId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
