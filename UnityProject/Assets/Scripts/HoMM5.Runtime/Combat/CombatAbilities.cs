using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// Creature combat abilities, implemented against the shipped ability
    /// descriptions (texts/Text/Game/Creatures/Creature_abilities) and the
    /// engine constants in DefaultStats.xdb. Anything without a reconstructable
    /// formula is marked "inferred" and stays tweakable here.
    ///
    /// The ability ids are the ABILITY_* enums the creature records carry.
    /// </summary>
    public static class CombatAbilities
    {
        // Numbers from DefaultStats.xdb / the shipped descriptions.
        public const float JoustingPerCell = 0.05f;          // shipped: Jousting_DamageBonusPerPathCell
        public const float FireShieldReflect = 0.2f;         // shipped: FireShield_Damage_Multiplier
        public const float MagmaShieldReflect = 0.4f;        // shipped: MagmaShield_Damage_Multiplier

        // Inferred from the ability texts ("резко увеличивается", "шанс"):
        public const float EnragePerDeath = 0.3f;            // +30% attack per friendly stack death
        public const float BashChance = 0.2f;
        public const float BlindChance = 0.3f;
        public const float IncorporealMissChance = 0.5f;
        public const float BrutalityBonus = 0.5f;            // +50% dealt and taken
        public const float PoisonTurnFraction = 0.3f;        // of the hit, spread over 3 turns
        public const float LifeDrainHealFraction = 0.5f;     // of damage dealt
        public const float RegenerationMin = 30f;            // desc: 30-50 hp per turn
        public const float RegenerationMax = 50f;
        public const float RiderChargeDefencePerCell = 0.05f;

        public static bool HasAbility(CombatUnit unit, string ability)
        {
            var abilities = unit != null && unit.Def != null ? unit.Def.abilities : null;
            return abilities != null && abilities.Contains(ability);
        }

        // -- creature types ------------------------------------------------

        public static bool IsUndead(CombatUnit u) => HasAbility(u, "ABILITY_UNDEAD");
        public static bool IsDemon(CombatUnit u) => HasAbility(u, "ABILITY_DEMONIC") || HasAbility(u, "ABILITY_DEMON_RAGED");
        public static bool IsElemental(CombatUnit u) => HasAbility(u, "ABILITY_ELEMENTAL");
        public static bool IsMechanical(CombatUnit u) => HasAbility(u, "ABILITY_MECHANICAL");

        /// <summary>Blinding attack's own exclusion list (undead, elementals, mechanicals).</summary>
        public static bool IsBlindImmune(CombatUnit u) =>
            IsUndead(u) || IsElemental(u) || IsMechanical(u) ||
            HasAbility(u, "ABILITY_IMMUNITY_TO_BLIND");

        public static bool IsMindImmune(CombatUnit u) =>
            IsUndead(u) || IsElemental(u) || IsMechanical(u) ||
            HasAbility(u, "ABILITY_IMMUNITY_TO_MIND_CONTROL");

        /// <summary>Armored: immune to defence-lowering spells and effects.</summary>
        public static bool IgnoresDefenceReduction(CombatUnit u) => HasAbility(u, "ABILITY_ARMORED");

        // -- damage taken ---------------------------------------------------

        /// <summary>Multiplier on incoming damage from a given channel.</summary>
        public static float DamageTakenMultiplier(CombatUnit defender, bool ranged, bool magical, string element)
        {
            if (defender == null) return 1f;
            var mult = 1f;

            // Large Shield: "50% damage from all non-magical shots".
            if (ranged && !magical && HasAbility(defender, "ABILITY_LARGE_SHIELD"))
                mult *= 0.5f;

            if (magical)
            {
                if (HasAbility(defender, "ABILITY_MAGIC_PROOF_25")) mult *= 0.75f;
                if (HasAbility(defender, "ABILITY_MAGIC_PROOF_50")) mult *= 0.5f;
                if (HasAbility(defender, "ABILITY_MAGIC_PROOF_75")) mult *= 0.25f;
                if (IsElementalImmune(defender, element)) mult = 0f;
            }

            // Brutality: deals more and takes more.
            if (HasAbility(defender, "ABILITY_BRUTALITY"))
                mult *= 1f + BrutalityBonus;

            // Fire mark (Mark of Fire) amplifies fire damage.
            if (defender.HasEffect(CombatEffect.FireMark) &&
                (element == "ELEMENT_FIRE" || (element == null && magical)))
                mult *= 1.5f;

            return mult;
        }

        public static bool IsElementalImmune(CombatUnit u, string element)
        {
            if (u == null || string.IsNullOrEmpty(element)) return false;
            switch (element)
            {
                case "ELEMENT_FIRE": return HasAbility(u, "ABILITY_IMMUNITY_TO_FIRE");
                case "ELEMENT_AIR": return HasAbility(u, "ABILITY_IMMUNITY_TO_AIR");
                case "ELEMENT_WATER": return HasAbility(u, "ABILITY_IMMUNITY_TO_WATER");
                case "ELEMENT_EARTH": return HasAbility(u, "ABILITY_IMMUNITY_TO_EARTH");
                default: return false;
            }
        }

        // -- damage dealt ---------------------------------------------------

        /// <summary>Multiplier on outgoing melee damage (jousting, brutality, charge).</summary>
        public static float AttackDamageMultiplier(CombatUnit attacker, int cellsMoved)
        {
            if (attacker == null) return 1f;
            var mult = 1f;

            // Jousting: "+5% per arena cell walked before the attack".
            if (cellsMoved > 0 && HasAbility(attacker, "ABILITY_JOUSTING"))
                mult *= 1f + JoustingPerCell * cellsMoved;

            if (HasAbility(attacker, "ABILITY_BRUTALITY"))
                mult *= 1f + BrutalityBonus;

            return mult;
        }

        /// <summary>Defence reduction from Rider Charge per cell moved.</summary>
        public static float ChargeDefenceReduction(CombatUnit attacker, int cellsMoved)
        {
            return cellsMoved > 0 && HasAbility(attacker, "ABILITY_RIDER_CHARGE")
                ? RiderChargeDefencePerCell * cellsMoved
                : 0f;
        }

        // -- retaliation ----------------------------------------------------

        /// <summary>No enemy retaliation: the target never swings back.</summary>
        public static bool BlocksRetaliation(CombatUnit attacker) =>
            HasAbility(attacker, "ABILITY_NO_ENEMY_RETALIATION");

        /// <summary>Unlimited retaliation: always swings back in melee.</summary>
        public static bool AlwaysRetaliates(CombatUnit defender) =>
            HasAbility(defender, "ABILITY_UNLIMITED_RETALIATION");

        /// <summary>Shooters can fire back when shot at (Ranged retaliate).</summary>
        public static bool RangedRetaliate(CombatUnit defender) =>
            HasAbility(defender, "ABILITY_RANGED_RETALIATE");

        /// <summary>Shooting penalty rules.</summary>
        public static float ShooterPenaltyMultiplier(CombatUnit shooter, bool inMelee, bool farRange)
        {
            if (HasAbility(shooter, "ABILITY_NO_MELEE_PENALTY") && !farRange) return 1f;
            if (HasAbility(shooter, "ABILITY_NO_RANGE_PENALTY")) return 1f;
            var mult = 1f;
            if (inMelee) mult *= CombatRules.ShooterInMeleeMultiplier;
            return mult;
        }

        // -- on-hit effects -------------------------------------------------

        /// <summary>
        /// Roll and apply the attacker's on-hit abilities. Mutates the defender
        /// and returns the effect name for the battle log (null = none).
        /// </summary>
        public static string ApplyOnHitEffects(System.Random rng, CombatUnit attacker,
                                               CombatUnit defender, int damage)
        {
            if (attacker == null || defender == null || !defender.IsAlive) return null;

            if (HasAbility(attacker, "ABILITY_POISONOUS_ATTACK"))
            {
                var total = Mathf.RoundToInt(damage * PoisonTurnFraction);
                defender.AddEffect(CombatEffect.Poison, 3, total / 3f, attacker.Side);
                return "poisoned";
            }
            if (HasAbility(attacker, "ABILITY_BLINDING_ATTACK") && !IsBlindImmune(defender))
            {
                if ((float)rng.NextDouble() < BlindChance)
                {
                    defender.AddEffect(CombatEffect.Blind, 2, 0f, attacker.Side);
                    return "blinded";
                }
            }
            if (HasAbility(attacker, "ABILITY_BASH") && !IsMechanical(defender))
            {
                if ((float)rng.NextDouble() < BashChance)
                {
                    defender.AddEffect(CombatEffect.Stun, 1, 0f, attacker.Side);
                    return "stunned";
                }
            }
            if (HasAbility(attacker, "ABILITY_CURSING_ATTACK") && !IgnoresDefenceReduction(defender))
            {
                defender.AddEffect(CombatEffect.Weakness, 3, 0.25f, attacker.Side);
                return "cursed";
            }
            if (HasAbility(attacker, "ABILITY_HEXING_ATTACK"))
            {
                defender.AddEffect(CombatEffect.Hexed, 3, 3f, attacker.Side);
                return "hexed";
            }
            if (HasAbility(attacker, "ABILITY_WOUND"))
            {
                defender.AddEffect(CombatEffect.NoHeal, 999, 0f, attacker.Side);
            }
            if (HasAbility(attacker, "ABILITY_MARK_OF_FIRE"))
            {
                defender.AddEffect(CombatEffect.FireMark, 999, 0.5f, attacker.Side);
            }
            return null;
        }

        /// <summary>Life drain: the attacker's stack heals from damage dealt.</summary>
        public static int LifeDrainHeal(CombatUnit attacker, int damage)
        {
            if (!HasAbility(attacker, "ABILITY_LIFE_DRAIN")) return 0;
            return HealStack(attacker, Mathf.RoundToInt(damage * LifeDrainHealFraction));
        }

        /// <summary>Heal a stack, reviving dead creatures if it overflows.</summary>
        public static int HealStack(CombatUnit unit, int amount)
        {
            if (unit == null || !unit.IsAlive || amount <= 0 ||
                unit.HasEffect(CombatEffect.NoHeal)) return 0;
            var revived = 0;
            if (unit.HealthLeft < unit.MaxHealth)
            {
                var heal = Mathf.Min(amount, unit.MaxHealth - unit.HealthLeft);
                unit.HealthLeft += heal;
                amount -= heal;
            }
            // Overflow revives fallen creatures (life drain, lay hands, resurrections).
            while (amount >= unit.MaxHealth && unit.Count < unit.StartCount)
            {
                amount -= unit.MaxHealth;
                unit.Count++;
                revived++;
            }
            return revived;
        }

        // -- per-turn behaviors ---------------------------------------------

        /// <summary>
        /// Start-of-turn bookkeeping for one unit: poison, regeneration,
        /// lay hands. Returns a log line or null. Call after blind/stun checks.
        /// </summary>
        public static string OnTurnStart(System.Random rng, CombatUnit unit)
        {
            if (unit == null || !unit.IsAlive) return null;

            var poison = unit.FindEffect(CombatEffect.Poison);
            if (poison != null)
            {
                var damage = Mathf.Max(1, Mathf.RoundToInt(poison.Magnitude));
                CombatRules.ApplyDamage(unit, damage);
                if (!unit.IsAlive) return $"{unit.DisplayName} succumbs to poison.";
                return $"{unit.DisplayName} suffers {damage} poison damage.";
            }

            if (HasAbility(unit, "ABILITY_REGENERATION") ||
                unit.HasEffect(CombatEffect.Regeneration))
            {
                var heal = unit.HasEffect(CombatEffect.Regeneration)
                    ? Mathf.RoundToInt(unit.FindEffect(CombatEffect.Regeneration).Magnitude)
                    : Mathf.RoundToInt(RegenerationMin + rng.NextDouble() * (RegenerationMax - RegenerationMin));
                HealStack(unit, heal);
                return $"{unit.DisplayName} regenerates {heal} health.";
            }

            if (HasAbility(unit, "ABILITY_LAY_HANDS") && !unit.HasEffect("layHandsUsed"))
            {
                if (unit.TotalHealth < unit.StartCount * unit.MaxHealth * 0.5f)
                {
                    HealStack(unit, unit.MaxHealth * unit.Count);
                    unit.AddEffect("layHandsUsed", 999, 0f, unit.Side);
                    return $"{unit.DisplayName} lays hands and is restored.";
                }
            }
            return null;
        }

        /// <summary>
        /// Enrage bookkeeping: call for every unit of a side when one of its
        /// stacks dies. Also fright auras and cowardice morale hits.
        /// </summary>
        public static string OnFriendlyDeath(List<CombatUnit> allies, CombatUnit dead)
        {
            string log = null;
            if (allies == null) return null;
            foreach (var u in allies)
            {
                if (u == null || !u.IsAlive || u == dead) continue;
                if (HasAbility(u, "ABILITY_ENRAGED") || HasAbility(u, "ABILITY_DEMON_RAGED"))
                    u.FriendlyDeathsSeen++;
            }
            return log;
        }

        /// <summary>Fright aura: adjacent enemy morale sink (tracked as hexed luck twin).</summary>
        public static void ApplyAuras(CombatUnit unit, List<CombatUnit> units)
        {
            if (unit == null || !unit.IsAlive || units == null) return;
            if (!HasAbility(unit, "ABILITY_FRIGHT_AURA") && !HasAbility(unit, "ABILITY_FEAR")) return;
            foreach (var other in units)
            {
                if (other == null || !other.IsAlive || other.Side == unit.Side) continue;
                var dx = Mathf.Abs(other.X - unit.X);
                var dy = Mathf.Abs(other.Y - unit.Y);
                if (dx <= 1 && dy <= 1)
                    other.AddEffect(CombatEffect.Hexed, 1, 2f, unit.Side);
            }
        }

        /// <summary>Fire shield / magma shield: melee attackers get burned.</summary>
        public static int MeleeReflect(CombatUnit defender, int damage)
        {
            if (defender == null) return 0;
            var fraction = 0f;
            if (HasAbility(defender, "ABILITY_FIRE_SHIELD")) fraction = FireShieldReflect;
            if (HasAbility(defender, "ABILITY_MAGMA_SHIELD")) fraction = MagmaShieldReflect;
            return fraction > 0f ? Mathf.RoundToInt(damage * fraction) : 0;
        }

        // -- multi-target strikes -------------------------------------------

        /// <summary>
        /// Which extra stacks a sweeping strike hits, beside the target:
        /// fire/acid breath (one behind), three/six-headed (adjacent), spray
        /// and whirlwind (all around).
        /// </summary>
        public static List<CombatUnit> ExtraTargets(CombatUnit attacker, CombatUnit target,
                                                    List<CombatUnit> units)
        {
            var extras = new List<CombatUnit>();
            if (attacker == null || target == null || units == null) return extras;

            if (HasAbility(attacker, "ABILITY_FIRE_BREATH") ||
                HasAbility(attacker, "ABILITY_ACID_BREATH") ||
                HasAbility(attacker, "ABILITY_CLEAVE"))
            {
                // The cell directly behind the target, away from the attacker.
                var bx = target.X + Mathf.Sign(target.X - attacker.X);
                var by = target.Y + Mathf.Sign(target.Y - attacker.Y);
                foreach (var u in units)
                    if (u != null && u.IsAlive && u.Side != attacker.Side &&
                        Mathf.Abs(u.X - bx) <= 0 && Mathf.Abs(u.Y - by) <= 0)
                        extras.Add(u);
                return extras;
            }

            int heads = 0;
            if (HasAbility(attacker, "ABILITY_THREE_HEADED_ATTACK")) heads = 3;
            else if (HasAbility(attacker, "ABILITY_SIX_HEADED_ATTACK")) heads = 6;
            else if (HasAbility(attacker, "ABILITY_SPRAY_ATTACK")) heads = 4;

            if (heads > 0)
            {
                foreach (var u in units)
                {
                    if (u == null || !u.IsAlive || u == target || u.Side == attacker.Side) continue;
                    if (Mathf.Abs(u.X - target.X) <= 1 && Mathf.Abs(u.Y - target.Y) <= 1)
                        extras.Add(u);
                }
                while (extras.Count > heads - 1) extras.RemoveAt(extras.Count - 1);
                return extras;
            }

            if (HasAbility(attacker, "ABILITY_WHIRLWIND") ||
                HasAbility(attacker, "ABILITY_WHIP_STRIKE"))
            {
                foreach (var u in units)
                {
                    if (u == null || !u.IsAlive || u.Side == attacker.Side) continue;
                    if (Mathf.Abs(u.X - attacker.X) <= 1 && Mathf.Abs(u.Y - attacker.Y) <= 1)
                        extras.Add(u);
                }
            }
            return extras;
        }

        /// <summary>Strike and return: whether the attacker springs back after the blow.</summary>
        public static bool StrikeAndReturn(CombatUnit attacker) =>
            HasAbility(attacker, "ABILITY_STRIKE_AND_RETURN");

        /// <summary>Double shot: elves fire twice.</summary>
        public static bool DoubleShot(CombatUnit attacker) =>
            HasAbility(attacker, "ABILITY_ELVES_DOUBLE_SHOT") ||
            HasAbility(attacker, "ABILITY_GOBLIN_THROWER");

        /// <summary>Explosion on death: damage to all adjacent stacks.</summary>
        public static int DeathExplosionDamage(CombatUnit dead)
        {
            return HasAbility(dead, "ABILITY_EXPLOSION") ? dead.MaxHealth * dead.StartCount / 2 : 0;
        }
    }
}
