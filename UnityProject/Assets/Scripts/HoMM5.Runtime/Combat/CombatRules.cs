using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// The combat math. Two sources of truth, in decreasing authority:
    ///
    ///   1. shipped constants from GameMechanics/RPGStats/DefaultStats.xdb
    ///      (defending bonus, luck damage multipliers, morale ATB values) --
    ///      delivered through <see cref="Configure"/>;
    ///   2. formulas the data files do not spell out -- attack/defence per-point
    ///      coefficients and morale/luck probabilities. Those are the classic
    ///      5%/2.5% skill coefficients and a flat 4% per morale/luck point, and
    ///      are marked "inferred" below. The original engine's exact curves
    ///      live in H5_Game_PEST.exe and are not reconstructed yet.
    /// </summary>
    public static class CombatRules
    {
        // -- shipped (overwritten by Configure) ---------------------------
        public static float DefenceBonusOnDefend = 0.3f;
        public static float GoodLuckDamageMultiplier = 2f;
        public static float BadLuckDamageMultiplier = 0.5f;
        public static float GoodMoraleAtbValue = 0.5f;
        public static float BadMoraleAtbValue = 0.5f;
        public static int HeroInitiative = 10;

        // -- inferred ----------------------------------------------------
        /// <summary>Inferred: damage bonus per point of attack over defence.</summary>
        public const float AttackBonusPerPoint = 0.05f;
        /// <summary>Inferred: damage reduction per point of defence over attack.</summary>
        public const float DefenceReductionPerPoint = 0.025f;
        public const float MaxAttackMultiplier = 3.0f;
        public const float MinDefenceMultiplier = 0.3f;
        /// <summary>Inferred: morale/luck proc chance per stat point.</summary>
        public const float ProcChancePerPoint = 0.04f;
        /// <summary>Inferred: shooters with an enemy in melee deal half damage.</summary>
        public const float ShooterInMeleeMultiplier = 0.5f;

        public static void Configure(HoMM5.Data.GameCombatRulesDef rules)
        {
            if (rules == null) return;
            DefenceBonusOnDefend = rules.defenceBonusOnDefend;
            GoodLuckDamageMultiplier = rules.goodLuckDamageMultiplier;
            BadLuckDamageMultiplier = rules.badLuckDamageMultiplier;
            GoodMoraleAtbValue = rules.goodMoraleAtbValue;
            BadMoraleAtbValue = rules.badMoraleAtbValue;
            HeroInitiative = rules.heroInitiative;
        }

        /// <summary>Chance of a good/bad morale or luck proc for a stat value.</summary>
        public static float ProcChance(int value)
        {
            return Mathf.Clamp(value, -10, 10) * ProcChancePerPoint;
        }

        /// <summary>
        /// Roll one attack. Damage = stack size × per-creature roll, scaled by
        /// the attack/defence difference, ability multipliers and luck.
        /// Returns 0 on a miss (incorporeal targets).
        /// </summary>
        public static DamageOutcome RollAttack(System.Random rng, CombatUnit attacker,
                                               CombatUnit defender, bool ranged,
                                               bool shooterInMelee, int cellsMoved = 0,
                                               bool retaliation = false)
        {
            var outcome = new DamageOutcome();
            if (attacker == null || defender == null || !attacker.IsAlive || !defender.IsAlive)
                return outcome;

            // Incorporeal: half the time physical blows pass through.
            if (!ranged && CombatAbilities.HasAbility(defender, "ABILITY_INCORPOREAL") &&
                rng.NextDouble() < CombatAbilities.IncorporealMissChance)
            {
                outcome.Missed = true;
                return outcome;
            }

            // Blessed rolls maximum, cursed minimum damage.
            int perCreature;
            float baseDamage;
            if (attacker.UnitType == CombatUnitType.WarMachine)
            {
                // Ballista: the machine's offence plus the hero's attack
                // (both roll into AttackSkill), scaled by the shipped damage
                // factor range for the War Machines tier.
                var factor = rng.Next(attacker.WarMachineMinFactor, attacker.WarMachineMaxFactor + 1);
                baseDamage = attacker.AttackSkill * factor;
            }
            else
            {
                if (attacker.HasEffect(CombatEffect.Blessed)) perCreature = attacker.MaxDamage;
                else if (attacker.HasEffect(CombatEffect.Cursed)) perCreature = attacker.MinDamage;
                else perCreature = rng.Next(attacker.MinDamage, attacker.MaxDamage + 1);
                baseDamage = (float)perCreature * attacker.Count;
            }

            var diff = attacker.AttackSkill - defender.DefenceSkill;
            var charge = CombatAbilities.ChargeDefenceReduction(attacker, cellsMoved);
            diff += Mathf.RoundToInt(charge * defender.DefenceSkill);
            var multiplier = diff >= 0
                ? Mathf.Min(MaxAttackMultiplier, 1f + AttackBonusPerPoint * diff)
                : Mathf.Max(MinDefenceMultiplier, 1f + DefenceReductionPerPoint * diff);
            baseDamage *= multiplier;

            // Jousting/brutality and friends.
            baseDamage *= CombatAbilities.AttackDamageMultiplier(attacker, cellsMoved);

            // Secondary skills: Offence/Archery on the way out, Defence/
            // Evasion on the way in, Retribution riding on morale, Training
            // sharpening the counter-strike.
            var atkFx = attacker.LeaderFx;
            var defFx = defender.LeaderFx;
            if (ranged) baseDamage *= atkFx.RangedDamageMultiplier * defFx.RangedDamageTakenMultiplier;
            else baseDamage *= atkFx.MeleeDamageMultiplier * defFx.MeleeDamageTakenMultiplier;
            if (atkFx.RetributionPerMorale > 0f && attacker.Leader != null && attacker.Leader.Morale > 0)
                baseDamage *= 1f + atkFx.RetributionPerMorale * attacker.Leader.Morale;
            if (retaliation && atkFx.RetaliationDamageMultiplier > 0f)
                baseDamage *= 1f + atkFx.RetaliationDamageMultiplier;

            // Shooter penalty channel + Large Shield and other takers' filters.
            if (ranged && shooterInMelee && !CombatAbilities.HasAbility(attacker, "ABILITY_NO_MELEE_PENALTY"))
                baseDamage *= ShooterInMeleeMultiplier;
            baseDamage *= CombatAbilities.DamageTakenMultiplier(defender, ranged, false, null);

            // Luck: double (Elven Luck widens the bonus), halve, or nothing.
            // Rolled against the attacker's own luck (hero bonus included).
            var luck = attacker.Leader != null ? attacker.Leader.Luck : 0;
            var roll = (float)rng.NextDouble();
            if (luck > 0 && roll < ProcChance(luck))
            {
                outcome.GoodLuck = true;
                baseDamage *= Mathf.Max(GoodLuckDamageMultiplier, atkFx.LuckDamageMultiplier);
            }
            else if (luck < 0 && roll < ProcChance(-luck))
            {
                outcome.BadLuck = true;
                baseDamage *= BadLuckDamageMultiplier;
            }

            outcome.Damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage));
            outcome.CreaturesKilled = ApplyDamage(defender, outcome.Damage);
            return outcome;
        }

        /// <summary>
        /// Apply raw damage to a stack: wounds the top creature first, then
        /// kills from the top. Returns how many creatures died.
        /// </summary>
        public static int ApplyDamage(CombatUnit defender, int damage)
        {
            if (defender == null || !defender.IsAlive || damage <= 0) return 0;
            var killed = 0;
            var remaining = damage;

            if (remaining >= defender.HealthLeft)
            {
                remaining -= defender.HealthLeft;
                defender.HealthLeft = 0;
                killed++;
                while (remaining > 0 && killed < defender.Count)
                {
                    // Next creature: full health must be eaten through.
                    if (remaining >= defender.MaxHealth)
                    {
                        remaining -= defender.MaxHealth;
                        killed++;
                    }
                    else
                    {
                        defender.HealthLeft = defender.MaxHealth - remaining;
                        remaining = 0;
                    }
                }
            }
            else
            {
                defender.HealthLeft -= remaining;
                remaining = 0;
            }

            defender.Count -= killed;
            if (defender.Count > 0 && defender.HealthLeft == 0)
                defender.HealthLeft = defender.MaxHealth;
            return killed;
        }

        /// <summary>Roll morale for a unit's upcoming turn.</summary>
        public static MoraleOutcome RollMorale(System.Random rng, CombatUnit unit)
        {
            var morale = unit != null && unit.Leader != null ? unit.Leader.Morale : 0;
            if (morale == 0) return MoraleOutcome.Normal;
            var roll = (float)rng.NextDouble();
            if (morale > 0 && roll < ProcChance(morale)) return MoraleOutcome.Good;
            if (morale < 0 && roll < ProcChance(-morale)) return MoraleOutcome.Bad;
            return MoraleOutcome.Normal;
        }
    }

    /// <summary>What a morale roll did to a unit's upcoming turn.</summary>
    public enum MoraleOutcome
    {
        Normal,
        Good,
        Bad,
    }

    public struct DamageOutcome
    {
        public int Damage;
        public int CreaturesKilled;
        public bool GoodLuck;
        public bool BadLuck;
        public bool Missed;
    }
}
