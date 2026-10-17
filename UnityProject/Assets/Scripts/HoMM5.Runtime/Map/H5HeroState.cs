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

        public int Attack = 1;
        public int Defence = 1;
        public int SpellPower = 1;
        public int Knowledge = 1;

        public int Level = 1;
        public int Experience;

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

        /// <summary>Spell points cap: the shipped engine uses Knowledge * 10.</summary>
        public int MaxMana => Knowledge * 10;

        public int Morale => MoraleBonus + TemporaryMorale;
        public int Luck => LuckBonus + TemporaryLuck;

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

        /// <summary>Add experience, leveling up as many times as it pays for.</summary>
        public void AddExperience(int amount)
        {
            if (amount <= 0) return;
            Experience += amount;
            while (Level < 100 && Experience >= ExperienceForLevel(Level + 1))
                Level++;
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
                   $"Mana {Mana:0}/{MaxMana}   Morale {Morale}  Luck {Luck}";
        }
    }
}
