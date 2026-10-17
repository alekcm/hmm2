using System.Collections.Generic;
using HoMM5.Combat;
using HoMM5.Data;

namespace HoMM5.Map
{
    /// <summary>
    /// The adventure-map state of one hero: primary skills, level, mana, the
    /// artifact satchel and the spellbook.
    ///
    /// A map's <c>AdvMapHero</c> only says who starts where; this is the part
    /// that changes as the hero visits objects, picks things up and gains
    /// experience -- the part a save file has to carry.
    /// </summary>
    public class H5HeroState
    {
        /// <summary>Map object id of the hero; the save file's key.</summary>
        public string Id = "";
        public string HeroName = "";

        /// <summary>HERO_CLASS_* of the hero's town (drives level-up offers).</summary>
        public string HeroClass = "HERO_CLASS_KNIGHT";

        public int Attack = 1;
        public int Defence = 1;
        public int SpellPower = 1;
        public int Knowledge = 1;

        public int Level = 1;
        public int Experience;

        /// <summary>Secondary skills: HERO_SKILL_* -> mastery rank 1..4.</summary>
        public Dictionary<string, int> Skills = new Dictionary<string, int>();

        /// <summary>Perk ids picked while leveling (HERO_SKILL_* of perk rows).</summary>
        public List<string> Perks = new List<string>();

        /// <summary>
        /// Level-ups earned but not yet chosen. The level-up dialog pops once
        /// per entry; each choice grants +1 primary stat and one skill/perk.
        /// </summary>
        public int PendingLevelUps;

        /// <summary>
        /// Cached aggregate of skill/perk modifiers, refreshed through
        /// <see cref="RecalculateEffects"/> whenever the skill set changes.
        /// </summary>
        public HeroSkillEffects Effects = HeroSkillEffects.Create();

        /// <summary>
        /// Army stacks a hero may carry at once. The shipped maps never author
        /// more than seven <c>armySlots</c> on a hero or a town garrison, which
        /// is the original's limit; recruitment stops there rather than stacking
        /// an eighth.
        /// </summary>
        public const int MaxArmySlots = 7;

        /// <summary>Current spell points; the cap is <see cref="MaxMana"/>.</summary>
        public float Mana;

        /// <summary>Bonuses that last until the end of the day (buoys, fountains).</summary>
        public int TemporaryMorale;
        public int TemporaryLuck;

        /// <summary>Permanent morale/luck bonuses from visits and artifacts.</summary>
        public int MoraleBonus;
        public int LuckBonus;

        public List<string> Artifacts = new List<string>();
        public List<string> Spells = new List<string>();

        /// <summary>Keymaster tent colors, for border guards.</summary>
        public List<string> KeyColors = new List<string>();

        /// <summary>
        /// The army the hero carries between battles. Seeded from the map's
        /// armySlots on first use; casualties are written back here.
        /// </summary>
        public List<ArmyStackState> Army = new List<ArmyStackState>();

        /// <summary>
        /// War machine ids (WAR_MACHINE_BALLISTA, ...) the hero brings to
        /// battles. Seeded with the workshop trio; town recruitment screens
        /// will replace this when they exist.
        /// </summary>
        public List<string> WarMachines = new List<string>
        {
            "WAR_MACHINE_BALLISTA", "WAR_MACHINE_AMMO_CART", "WAR_MACHINE_FIRST_AID_TENT",
        };

        /// <summary>
        /// Spell points cap: Knowledge * 10 (shipped), scaled by Intelligence.
        /// </summary>
        public int MaxMana => (int)(Knowledge * 10 * Effects.ManaMultiplier);

        public int Morale => MoraleBonus + TemporaryMorale + Effects.MoraleFromSkills;
        public int Luck => LuckBonus + TemporaryLuck + Effects.LuckFromSkills;

        /// <summary>
        /// Cumulative experience needed for a level. HoMM5's hero curve is the
        /// triangular one -- level n costs 1000 * n * (n - 1) / 2 total points
        /// (level 2 at 1000, level 3 at 3000, level 4 at 6000, ...).
        /// </summary>
        public static int ExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            return 1000 * level * (level - 1) / 2;
        }

        /// <summary>
        /// Add experience (Learning's bonus included), queueing one pending
        /// level-up dialog per level earned. Callers drain
        /// <see cref="PendingLevelUps"/> with <see cref="RollLevelUpOffers"/>.
        /// </summary>
        public void AddExperience(int amount, GameDatabase db = null)
        {
            if (amount <= 0) return;
            if (db != null && Effects.XpMultiplier != 1f)
                amount = (int)System.Math.Round(amount * Effects.XpMultiplier);
            Experience += amount;
            while (Level < 100 && Experience >= ExperienceForLevel(Level + 1))
            {
                Level++;
                PendingLevelUps++;
            }
        }

        // -- secondary skills ----------------------------------------------

        /// <summary>Current mastery rank of a skill (0 when unknown).</summary>
        public int SkillRank(string skillId)
        {
            return Skills != null && Skills.TryGetValue(skillId, out var m) ? m : 0;
        }

        public bool HasPerk(string perkId) => Perks != null && Perks.Contains(perkId);

        /// <summary>
        /// Non-racial secondary skills a hero may hold at once. The map data
        /// tops out at five distinct common skills per hero, with the class
        /// skill carried separately -- inferred from the shipped maps.
        /// </summary>
        public const int MaxSecondarySkills = 5;

        public bool LearnSkill(string skillId, int mastery)
        {
            if (string.IsNullOrEmpty(skillId) || mastery <= 0) return false;
            Skills[skillId] = System.Math.Max(SkillRank(skillId), mastery);
            return true;
        }

        public bool LearnPerk(string perkId)
        {
            if (string.IsNullOrEmpty(perkId) || HasPerk(perkId)) return false;
            Perks.Add(perkId);
            return true;
        }

        /// <summary>Refresh the cached skill/perk aggregate.</summary>
        public void RecalculateEffects(GameDatabase db)
        {
            Effects = HeroSkillEffects.Compute(this, db);
            if (Mana > MaxMana) Mana = MaxMana;
        }

        /// <summary>
        /// The hero's class skill (Training, Gating, ...), if any is known.
        /// </summary>
        public string RacialSkill => HeroClass != null && HeroClassSkills.TryGetValue(HeroClass, out var s) ? s : null;

        static readonly Dictionary<string, string> HeroClassSkills = new Dictionary<string, string>
        {
            { "HERO_CLASS_KNIGHT", "HERO_SKILL_TRAINING" },
            { "HERO_CLASS_DEMON_LORD", "HERO_SKILL_GATING" },
            { "HERO_CLASS_NECROMANCER", "HERO_SKILL_NECROMANCY" },
            { "HERO_CLASS_RANGER", "HERO_SKILL_AVENGER" },
            { "HERO_CLASS_WIZARD", "HERO_SKILL_ARTIFICIER" },
            { "HERO_CLASS_WARLOCK", "HERO_SKILL_INVOCATION" },
            { "HERO_CLASS_RUNEMAGE", "HERO_SKILL_RUNELORE" },
            { "HERO_CLASS_BARBARIAN", "HERO_SKILL_DEMONIC_RAGE" },
        };

        // -- level up ------------------------------------------------------

        /// <summary>One (skill, perk) pair of the level-up dialog.</summary>
        public class LevelUpOffer
        {
            public string SkillId;
            /// <summary>Mastery the hero gains (or 0 for a +2 primary stat).</summary>
            public int Mastery;
            public bool IsNewSkill;
            /// <summary>Perk granted with the skill, when the skill has one left.</summary>
            public string PerkId;

            public string Summary(GameDatabase db)
            {
                if (Mastery <= 0) return "Primary skill +2";
                var skill = db != null ? db.GetSkill(SkillId) : null;
                var name = skill != null ? skill.DisplayName(Mastery) : GameDatabase.PrettifyEnum(SkillId);
                var perk = db != null ? db.GetSkill(PerkId) : null;
                var perkName = perk != null ? perk.DisplayName() : null;
                var masteryName = SkillMastery.Name(Mastery);
                return string.IsNullOrEmpty(perkName)
                    ? $"{name}  ({masteryName})"
                    : $"{name}  ({masteryName})  +  {perkName}";
            }
        }

        /// <summary>
        /// Roll the two offers of a level-up. Following the shipped LevelUpBox
        /// (a Skills row and a Perks row over two panels), each offer is a
        /// skill -- new at Basic or an existing one at the next rank -- paired
        /// with one perk of that skill that this class may still learn. Skills
        /// are weighted by the class's SkillsProbs table; when nothing can be
        /// offered the panel degrades to +2 primary skill.
        /// </summary>
        public List<LevelUpOffer> RollLevelUpOffers(GameDatabase db, System.Random rng)
        {
            var offers = new List<LevelUpOffer>();
            if (db == null) return offers;
            var cls = db.GetHeroClass(HeroClass);
            var pool = new List<KeyValuePair<string, int>>();

            foreach (var kv in (cls != null ? cls.skillsProbs : new Dictionary<string, int>()))
            {
                var skill = db.GetSkill(kv.Key);
                if (skill == null || !skill.IsSkill || kv.Value <= 0) continue;
                var rank = SkillRank(kv.Key);
                if (rank >= skill.MaxMastery) continue;
                if (rank == 0 && kv.Key != RacialSkill && SecondarySkillCount >= MaxSecondarySkills) continue;
                pool.Add(kv);
            }

            for (var slot = 0; slot < 2 && pool.Count > 0; slot++)
            {
                var picked = PickWeighted(pool, rng);
                pool.Remove(picked);
                var rank = SkillRank(picked.Key);
                offers.Add(new LevelUpOffer
                {
                    SkillId = picked.Key,
                    Mastery = rank + 1,
                    IsNewSkill = rank == 0,
                    PerkId = PickPerkFor(picked.Key, db, rng),
                });
            }
            while (offers.Count < 2)
                offers.Add(new LevelUpOffer { Mastery = 0 });
            return offers;
        }

        /// <summary>Non-racial skills currently known.</summary>
        public int SecondarySkillCount
        {
            get
            {
                var n = 0;
                foreach (var kv in Skills)
                    if (kv.Key != RacialSkill && kv.Value > 0) n++;
                return n;
            }
        }

        /// <summary>
        /// A perk of ``skillId`` this class may still learn: standard perks
        /// always, class feats per the record's prerequisite rows (see
        /// SkillDef.AvailableFor).
        /// </summary>
        public string PickPerkFor(string skillId, GameDatabase db, System.Random rng)
        {
            var candidates = new List<string>();
            foreach (var perk in db.PerksOfSkill(skillId))
            {
                if (HasPerk(perk.id)) continue;
                if (!perk.AvailableFor(HeroClass, Perks)) continue;
                candidates.Add(perk.id);
            }
            if (candidates.Count == 0) return null;
            return candidates[rng.Next(candidates.Count)];
        }

        /// <summary>
        /// Commit one level-up choice: the +1 primary stat roll (class
        /// AttributeProbs), the offer's skill and perk, and Learning's extra
        /// stat every N levels.
        /// </summary>
        public void ApplyLevelUp(LevelUpOffer offer, GameDatabase db, System.Random rng)
        {
            if (PendingLevelUps > 0) PendingLevelUps--;
            if (offer == null) return;

            var cls = db != null ? db.GetHeroClass(HeroClass) : null;
            RollPrimaryStat(cls, rng);
            if (Effects.FreeStatEveryNLevels > 0 && Level % Effects.FreeStatEveryNLevels == 0)
                RollPrimaryStat(cls, rng);

            if (offer.Mastery > 0)
            {
                LearnSkill(offer.SkillId, offer.Mastery);
                if (!string.IsNullOrEmpty(offer.PerkId)) LearnPerk(offer.PerkId);
            }
            else
            {
                // The +2 fallback panel: two points in one random stat.
                RollPrimaryStat(cls, rng);
                RollPrimaryStat(cls, rng);
            }
            RecalculateEffects(db);
        }

        /// <summary>+1 to a random primary stat, weighted by class probabilities.</summary>
        public void RollPrimaryStat(HeroClassDef cls, System.Random rng)
        {
            var probs = cls != null ? cls.attributeProbs : null;
            var total = 0;
            if (probs != null)
                foreach (var kv in probs) total += System.Math.Max(0, kv.Value);
            if (total <= 0)
            {
                AddPrimarySkill(1 + (rng != null ? rng.Next(4) : 0), 1);
                return;
            }
            var roll = rng != null ? rng.Next(total) : 0;
            if (probs != null)
            {
                foreach (var kv in probs)
                {
                    roll -= System.Math.Max(0, kv.Value);
                    if (roll < 0)
                    {
                        switch (kv.Key)
                        {
                            case "offence": Attack++; break;
                            case "defence": Defence++; break;
                            case "spellpower": SpellPower++; break;
                            default: Knowledge++; break;
                        }
                        return;
                    }
                }
            }
            Knowledge++;
        }

        static KeyValuePair<string, int> PickWeighted(List<KeyValuePair<string, int>> pool, System.Random rng)
        {
            var total = 0;
            foreach (var kv in pool) total += kv.Value;
            var roll = rng != null ? rng.Next(System.Math.Max(1, total)) : 0;
            foreach (var kv in pool)
            {
                roll -= kv.Value;
                if (roll < 0) return kv;
            }
            return pool[pool.Count - 1];
        }

        /// <summary>
        /// One primary skill point. The four stats use the source engine's
        /// STAT_ATTACK..STAT_KNOWLEDGE order (advmap-startup.lua).
        /// </summary>
        public void AddPrimarySkill(int stat, int amount)
        {
            switch (stat)
            {
                case 1: Attack += amount; break;
                case 2: Defence += amount; break;
                case 3: SpellPower += amount; break;
                case 4: Knowledge += amount; break;
            }
        }

        public bool HasKey(string keyColor)
        {
            return !string.IsNullOrEmpty(keyColor) && KeyColors.Contains(keyColor);
        }

        public void GiveKey(string keyColor)
        {
            if (!HasKey(keyColor)) KeyColors.Add(keyColor);
        }

        /// <summary>Learn a spell the hero does not already know.</summary>
        public bool LearnSpell(string spellId)
        {
            if (string.IsNullOrEmpty(spellId) || Spells.Contains(spellId)) return false;
            Spells.Add(spellId);
            return true;
        }

        /// <summary>Wear an artifact; returns false when already worn.</summary>
        public bool WearArtifact(string artifactId)
        {
            if (string.IsNullOrEmpty(artifactId) || Artifacts.Contains(artifactId)) return false;
            Artifacts.Add(artifactId);
            return true;
        }

        /// <summary>
        /// Fold an artifact's stat block into the hero's skills and bonuses.
        /// Called when the artifact is put on; the values are the shipped
        /// artifact table's own numbers.
        /// </summary>
        public void ApplyArtifactStats(ArtifactDef artifact)
        {
            if (artifact == null || artifact.stats == null) return;
            Attack += artifact.stats.Attack;
            Defence += artifact.stats.Defence;
            SpellPower += artifact.stats.SpellPower;
            Knowledge += artifact.stats.Knowledge;
            MoraleBonus += artifact.stats.Morale;
            LuckBonus += artifact.stats.Luck;
        }

        /// <summary>Daily reset of the temporary morale/luck flags.</summary>
        public void OnNewDay()
        {
            TemporaryMorale = 0;
            TemporaryLuck = 0;
        }

        /// <summary>Fill the army from map-authored stacks, once.</summary>
        public void EnsureArmy(List<MapArmyStackDef> mapStacks)
        {
            if (Army.Count > 0 || mapStacks == null) return;
            foreach (var stack in mapStacks)
            {
                if (stack == null || string.IsNullOrEmpty(stack.creature)) continue;
                var count = stack.count > 0 ? stack.count : stack.amount;
                if (count > 0) Army.Add(new ArmyStackState(stack.creature, count));
            }
        }

        /// <summary>Replace the army with battle survivors.</summary>
        public void SetArmy(List<ArmyStackState> survivors)
        {
            Army.Clear();
            if (survivors == null) return;
            foreach (var s in survivors)
                if (s != null && s.Count > 0 && !string.IsNullOrEmpty(s.CreatureId))
                    Army.Add(new ArmyStackState(s.CreatureId, s.Count));
        }

        public int ArmyCount
        {
            get
            {
                var total = 0;
                foreach (var s in Army) if (s != null) total += s.Count;
                return total;
            }
        }

        /// <summary>One-line summary for the HUD.</summary>
        public string Summary()
        {
            return $"{HeroName}  Lv {Level}   A {Attack}  D {Defence}  S {SpellPower}  K {Knowledge}   " +
                   $"Mana {Mana:0}/{MaxMana}   Morale {Morale}  Luck {Luck}   Skills {Skills.Count}";
        }

        /// <summary>Multi-line skill/perk roster for the hero panel.</summary>
        public string SkillSummary(HoMM5.Data.GameDatabase db)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(HeroName + "  —  " + (db != null && db.GetHeroClass(HeroClass) != null
                ? db.GetHeroClass(HeroClass).DisplayName : GameDatabase.PrettifyEnum(HeroClass)));
            foreach (var kv in Skills)
            {
                if (kv.Value <= 0) continue;
                var skill = db != null ? db.GetSkill(kv.Key) : null;
                var name = skill != null ? skill.DisplayName(kv.Value) : GameDatabase.PrettifyEnum(kv.Key);
                sb.AppendLine($"  {name}  ({SkillMastery.Name(kv.Value)})");
            }
            if (Perks.Count > 0)
            {
                sb.AppendLine("Perks:");
                foreach (var perkId in Perks)
                {
                    var perk = db != null ? db.GetSkill(perkId) : null;
                    sb.AppendLine("  " + (perk != null ? perk.DisplayName() : GameDatabase.PrettifyEnum(perkId)));
                }
            }
            return sb.ToString();
        }
    }
}
