using System.Collections.Generic;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// Casting in battle. Damage and duration scales are the spell record's
    /// own per-mastery rows (Base + PerPower x spell power); effect kinds are
    /// reconstructed from the spell names and schools (the shipped records
    /// carry no effect enum -- the native engine wires those in code).
    ///
    /// Secondary skills (Air/Fire/... Magic masteries) are not implemented, so
    /// every cast uses mastery tier 0 ("none"). HeroManaPerKnowledge from
    /// DefaultStats confirms the Knowledge x 10 mana cap in H5HeroState.
    /// </summary>
    public static class CombatSpells
    {
        /// <summary>Inferred: summon health per point of spell power.</summary>
        public const float SummonHealthPerPower = 20f;
        /// <summary>Inferred: resurrection health per point of spell power.</summary>
        public const float ResurrectHealthPerPower = 25f;

        static readonly HashSet<string> DamageSpells = new HashSet<string>
        {
            "Lightning_Bolt", "Ice_Bolt", "Fireball", "Frost_Ring", "Implosion",
            "MagicFist", "Magic_Arrow", "Meteor_Shower", "StoneSpikes", "Armageddon",
            "DeepFreeze", "Uber_Chain_Lightning", "Uber_Meteor_Shower",
            "Empowered_Armageddon", "Empowered_Chain_Lightning", "Empowered_DeepFreeze",
            "Empowered_Fireball", "Empowered_Frost_Ring", "Empowered_Ice_Bolt",
            "Empowered_Implosion", "Empowered_Lightning_Bolt", "Empowered_MagicFist",
            "Empowered_Magic_Arrow", "Empowered_Meteor_Shower", "Empowered_StoneSpikes",
        };

        /// <summary>Can this hero cast this spell right now?</summary>
        public static bool CanCast(H5HeroState hero, SpellDef spell, int manaLeft, out string reason)
        {
            reason = null;
            if (hero == null || spell == null)
            {
                reason = "No spell.";
                return false;
            }
            if (manaLeft < spell.manaCost)
            {
                reason = "Not enough mana.";
                return false;
            }
            if (!string.IsNullOrEmpty(spell.category) && spell.category != "Combat_Spells")
            {
                reason = "This spell is not cast in battle.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Resolve one cast. Returns a battle-log line; null when the spell has
        /// no implemented effect. Mana is spent by the caller (combat state).
        /// </summary>
        public static string Cast(CombatGameState state, H5HeroState caster, SpellDef spell,
                                  CombatUnit target, int cellX, int cellY)
        {
            if (state == null || spell == null) return null;
            var power = caster != null ? caster.SpellPower : 1;
            var id = spell.id ?? "";

            // -- destructive damage --------------------------------------
            if (DamageSpells.Contains(id) || (spell.damage != null && spell.damage.Count > 0 &&
                (spell.target == "TARGET_HOSTILE" || spell.target == "TARGET_ALL")))
            {
                return CastDamage(state, caster, spell, target, cellX, cellY, power);
            }

            // -- named effects ------------------------------------------
            switch (id)
            {
                case "Haste":
                case "Mass_Haste":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Hasted, 0.3f, power);
                case "Slow":
                case "Mass_Slow":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Slowed, 0.3f, power);
                case "StoneSkin":
                case "Mass_Stoneskin":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.StoneSkin, 0.3f, power);
                case "Bloodlust":
                case "Mass_Bloodlast":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.DivineStrength, 0.2f, power);
                case "Bless":
                case "Mass_Bless":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Blessed, 1f, power);
                case "Curse":
                case "Mass_Curse":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Cursed, 1f, power);
                case "Weakness":
                case "Mass_Weakness":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Weakness, 0.25f, power);
                case "Disrupting_Ray":
                case "Mass_Disrupting_Ray":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.Vulnerability, 0.3f, power);
                case "Regeneration":
                    return Buff(state, caster, spell, false, target, CombatEffect.Regeneration, power * 3f, power);
                case "Vampirism":
                    return Buff(state, caster, spell, false, target, CombatEffect.Vampirism, 0.25f, power);
                case "Blind":
                    return Debuff(state, caster, spell, target, CombatEffect.Blind, DurationOf(spell, power));
                case "Plague":
                case "Mass_Plague":
                    return Debuff(state, caster, spell, target, CombatEffect.Weakness, DurationOf(spell, power));
                case "Forgetfulness":
                case "Mass_Forgetfulness":
                    return Debuff(state, caster, spell, target, CombatEffect.Cursed, DurationOf(spell, power));
                case "AntiMagic":
                case "Celestial_Shield":
                    return Buff(state, caster, spell, false, target, CombatEffect.Immunity, 1f, power);
                case "Deflect_Arrows":
                case "Mass_Deflect_Arrows":
                    return Buff(state, caster, spell, id.StartsWith("Mass"), target, CombatEffect.StoneSkin, 0.5f, power);
                case "Berserk":
                    return Debuff(state, caster, spell, target, CombatEffect.Frenzy, DurationOf(spell, power));

                // -- healing / resurrection ------------------------------
                case "Resurrect":
                case "Animate_Dead":
                    return CastHeal(state, caster, spell, target, power, true);
                case "DivineVengeance":
                    return CastDamage(state, caster, spell, target, cellX, cellY, power);

                // -- summoning ------------------------------------------
                case "Summon_Elementals":
                    return CastSummon(state, caster, spell, "CREATURE_ELEMENTAL", power);
                case "ConjurePhoenix":
                    return CastSummon(state, caster, spell, "CREATURE_PHOENIX", power);
                case "Phantom":
                    return CastPhantom(state, caster, spell, target);
                case "Wasp_Swarm":
                case "SummonHive":
                    return CastDamage(state, caster, spell, target, cellX, cellY, power);

                // -- battlefield obstacles ------------------------------
                case "Firewall":
                case "Land_Mine":
                case "BladeBarrier":
                case "ArcaneCrystal":
                case "Earthquake":
                    return CastDamage(state, caster, spell, target, cellX, cellY, power);

                case "Teleport":
                    return "Teleport moves a stack -- not implemented yet.";

                case "Dispel":
                case "Mass_Dispel":
                    return CastDispel(state, caster, spell, target, id.StartsWith("Mass"));

                case "Holy_Word":
                case "Unholy_Word":
                    return CastDamage(state, caster, spell, target, cellX, cellY, power);
            }

            // Unreconstructed spell effects stay honest instead of guessing.
            return null;
        }

        // -- implementations -----------------------------------------------

        static string CastDamage(CombatGameState state, H5HeroState caster, SpellDef spell,
                                 CombatUnit target, int cellX, int cellY, int power)
        {
            var tier = 0;   // no mastery skills yet
            var raw = spell.LevelValue(spell.damage, tier, power);
            if (raw <= 0f) raw = 10f + 10f * power;   // damage-less records: inferred floor

            var hits = new List<CombatUnit>();
            if (spell.isAreaAttack)
            {
                // Area spells center on the clicked cell or unit.
                var cx = target != null ? target.X : cellX;
                var cy = target != null ? target.Y : cellY;
                foreach (var u in state.Units)
                    if (u.IsAlive && Mathf.Abs(u.X - cx) <= 1 && Mathf.Abs(u.Y - cy) <= 1)
                        hits.Add(u);
            }
            else if (target != null && target.IsAlive)
            {
                hits.Add(target);
            }
            if (hits.Count == 0) return "The spell finds no target.";

            var element = spell.element;
            var killed = 0;
            var total = 0;
            foreach (var hit in hits)
            {
                var reduced = raw * CombatAbilities.DamageTakenMultiplier(hit, false, true, element);
                if (reduced <= 0f)
                {
                    state.AddLog($"{hit.DisplayName} is immune to {spell.DisplayName}.");
                    continue;
                }
                var damage = Mathf.Max(1, Mathf.RoundToInt(reduced));
                var dead = CombatRules.ApplyDamage(hit, damage);
                total += damage;
                killed += dead;
                if (!hit.IsAlive) state.NotifyDeath(hit);
            }
            var who = caster != null ? caster.HeroName : "The spell";
            return $"{who} casts {spell.DisplayName}: {total} damage, {killed} killed.";
        }

        static string Buff(CombatGameState state, H5HeroState caster, SpellDef spell,
                           bool mass, CombatUnit target, string effect, float magnitude, int power)
        {
            var side = IsAttackerHero(state, caster) ? CombatSide.Attacker : CombatSide.Defender;
            var tier = 0;
            var turns = Mathf.Max(1, Mathf.RoundToInt(spell.LevelValue(spell.duration, tier, power)));
            var applied = 0;

            if (mass)
            {
                foreach (var u in state.Units)
                    if (u.IsAlive && u.Side == side)
                    {
                        u.AddEffect(effect, turns, magnitude, side);
                        applied++;
                    }
            }
            else
            {
                // One stack: the chosen one, else the most wounded friendly.
                var pick = target != null && target.IsAlive && target.Side == side
                    ? target : MostWounded(state, side);
                if (pick == null) return "The spell finds no target.";
                pick.AddEffect(effect, turns, magnitude, side);
                applied = 1;
            }

            if (applied == 0) return null;
            var label = mass ? "the whole army" : $"{applied} stack";
            return $"{caster?.HeroName ?? "The hero"} empowers {label} with {spell.DisplayName}.";
        }

        static CombatUnit MostWounded(CombatGameState state, CombatSide side)
        {
            CombatUnit best = null;
            var bestRatio = float.MaxValue;
            foreach (var u in state.Units)
            {
                if (!u.IsAlive || u.Side != side) continue;
                var ratio = (float)u.TotalHealth / Mathf.Max(1, u.StartCount * u.MaxHealth);
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = u;
                }
            }
            return best;
        }

        static string Debuff(CombatGameState state, H5HeroState caster, SpellDef spell,
                             CombatUnit target, string effect, int turns)
        {
            if (target == null || !target.IsAlive) return "The spell finds no target.";
            if (effect == CombatEffect.Blind && CombatAbilities.IsBlindImmune(target))
            {
                state.AddLog($"{target.DisplayName} cannot be blinded.");
                return null;
            }
            if (target.HasEffect(CombatEffect.Immunity))
            {
                state.AddLog($"{target.DisplayName} is magically protected.");
                return null;
            }
            target.AddEffect(effect, Mathf.Max(1, turns), effect == CombatEffect.Frenzy ? 0.5f : 0f,
                             caster != null ? CombatSide.Attacker : CombatSide.Defender);
            return $"{caster?.HeroName ?? "The hero"} enchants {target.DisplayName} with {spell.DisplayName}.";
        }

        static string CastHeal(CombatGameState state, H5HeroState caster, SpellDef spell,
                               CombatUnit target, int power, bool resurrect)
        {
            if (target == null || !target.IsAlive) return "The spell finds no living target.";
            if (target.HasEffect(CombatEffect.NoHeal))
            {
                state.AddLog($"{target.DisplayName} is wounded beyond healing.");
                return null;
            }
            var amount = resurrect
                ? Mathf.RoundToInt(ResurrectHealthPerPower * power)
                : Mathf.RoundToInt(spell.LevelValue(spell.damage, 0, power));
            if (amount <= 0) amount = 25 * power;
            var revived = CombatAbilities.HealStack(target, amount);
            return $"{caster?.HeroName ?? "The hero"} restores {target.DisplayName}" +
                   (revived > 0 ? $", reviving {revived}." : ".");
        }

        static string CastSummon(CombatGameState state, H5HeroState caster, SpellDef spell,
                                 string creatureId, int power)
        {
            var hp = Mathf.RoundToInt(SummonHealthPerPower * power);
            var side = IsAttackerHero(state, caster) ? CombatSide.Attacker : CombatSide.Defender;
            var unit = state.SummonStack(side, creatureId, hp);
            if (unit == null) return null;
            return $"{caster?.HeroName ?? "The hero"} summons {unit.DisplayName} ({unit.Count}).";
        }

        static string CastPhantom(CombatGameState state, H5HeroState caster, SpellDef spell,
                                  CombatUnit target)
        {
            if (target == null || !target.IsAlive || target.Side != CombatSide.Attacker)
                return "Phantom needs a friendly stack.";
            var copy = state.SummonStack(target.Side, target.CreatureId,
                                         target.Count * target.MaxHealth);
            if (copy == null) return null;
            copy.Count = target.Count;
            copy.StartCount = target.Count;
            return $"A phantom of {target.DisplayName} appears on the field.";
        }

        static string CastDispel(CombatGameState state, H5HeroState caster, SpellDef spell,
                                 CombatUnit target, bool mass)
        {
            var cleared = 0;
            foreach (var u in state.Units)
            {
                if (!u.IsAlive) continue;
                if (!mass && u != target) continue;
                for (var i = u.Effects.Count - 1; i >= 0; i--)
                {
                    if (u.Effects[i].Kind == CombatEffect.NoHeal) continue;
                    u.Effects.RemoveAt(i);
                    cleared++;
                }
            }
            return cleared > 0 ? $"{spell.DisplayName} clears {cleared} enchantments." : null;
        }

        static int DurationOf(SpellDef spell, int power)
        {
            return Mathf.Max(1, Mathf.RoundToInt(spell.LevelValue(spell.duration, 0, power)));
        }

        static bool IsAttackerHero(CombatGameState state, H5HeroState hero)
        {
            if (state == null || hero == null) return true;
            foreach (var u in state.Units)
                if (u.Leader == hero) return u.Side == CombatSide.Attacker;
            return true;
        }
    }
}
