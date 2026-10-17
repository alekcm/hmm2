using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>Mastery ranks of a secondary skill (Mastery.xdb order).</summary>
    public static class SkillMastery
    {
        public const int None = 0;
        public const int Basic = 1;
        public const int Advanced = 2;
        public const int Expert = 3;
        /// <summary>Racial skills rank once more than common ones.</summary>
        public const int ExtraExpert = 4;

        public static string Name(int mastery)
        {
            switch (mastery)
            {
                case Basic: return "Basic";
                case Advanced: return "Advanced";
                case Expert: return "Expert";
                case ExtraExpert: return "Extra Expert";
                default: return "";
            }
        }
    }

    /// <summary>
    /// Every numeric modifier a hero's skills and perks currently apply,
    /// computed once from the shipped coefficients in rules.json
    /// (DefaultStats.xdb HeroSkills) and the hero's owned skills/perks.
    ///
    /// Combat and the adventure map read this struct instead of walking the
    /// perk list themselves. Values are multipliers/addends exactly as the
    /// skill descriptions state them (Offence 5/10/15% melee, ...).
    /// </summary>
    public struct HeroSkillEffects
    {
        // -- adventure map -------------------------------------------------

        /// <summary>Logistics: +10% per rank of land movement.</summary>
        public float MovementMultiplier;
        /// <summary>Pathfinding: terrain penalties cut in half.</summary>
        public float TerrainPenaltyMultiplier;
        /// <summary>Path of War: flat movement points per battle won.</summary>
        public int MovePointsPerVictory;
        /// <summary>Scouting / Disguise and Reckon: fog and minimap radius.</summary>
        public int ScoutingRadiusBonus;
        /// <summary>Estates: gold per day.</summary>
        public int EstatesGoldPerDay;
        /// <summary>Intelligence: maximum mana multiplier.</summary>
        public float ManaMultiplier;
        /// <summary>Mysticism: daily mana regeneration multiplier.</summary>
        public float ManaRegenMultiplier;
        /// <summary>Arcane Training and friends: spell mana cost multiplier.</summary>
        public float SpellCostMultiplier;
        /// <summary>Empowered Spells: +50% spell damage (for double mana).</summary>
        public float SpellDamageMultiplier;
        /// <summary>Learning: battle experience multiplier.</summary>
        public float XpMultiplier;
        /// <summary>Learning: +1 primary stat every N levels (0 = never).</summary>
        public int FreeStatEveryNLevels;
        /// <summary>Necromancy: percent of fallen enemies raised as undead.</summary>
        public float NecromancyRaisePercent;

        // -- combat: damage ------------------------------------------------

        /// <summary>Offence: melee damage dealt multiplier (1.05/1.10/1.15).</summary>
        public float MeleeDamageMultiplier;
        /// <summary>Archery: ranged damage dealt multiplier.</summary>
        public float RangedDamageMultiplier;
        /// <summary>Defence: melee damage taken multiplier (0.9/0.8/0.7).</summary>
        public float MeleeDamageTakenMultiplier;
        /// <summary>Evasion: ranged damage taken multiplier.</summary>
        public float RangedDamageTakenMultiplier;
        /// <summary>Protection / Magic Cushion: hostile spell damage taken.</summary>
        public float MagicDamageTakenMultiplier;
        /// <summary>Retribution: extra damage per point of morale.</summary>
        public float RetributionPerMorale;
        /// <summary>Frenzy: flat damage per creature.</summary>
        public int DamagePerCreature;
        /// <summary>Toughness: flat hit points per creature.</summary>
        public int HealthPerCreature;
        /// <summary>Bodybuilding: percent hit-point bonus.</summary>
        public float HealthPercentBonus;
        /// <summary>Hold Ground: multiplies the Defend command's defence bonus.</summary>
        public float DefendBonusMultiplier;
        /// <summary>Training: retaliation damage bonus per rank.</summary>
        public float RetaliationDamageMultiplier;

        // -- combat: tempo -------------------------------------------------

        /// <summary>Quickness of Mind: initiative multiplier for the battle.</summary>
        public float InitiativeMultiplier;
        /// <summary>Fast and Furious: creature speed bonus.</summary>
        public int SpeedBonus;
        /// <summary>Death Tread: speed bonus during sieges.</summary>
        public int SiegeSpeedBonus;
        /// <summary>Road Home: speed bonus on grass battlefields.</summary>
        public int GrassSpeedBonus;
        /// <summary>Sorcery: hero's between-casts interval multiplier.</summary>
        public float CastIntervalMultiplier;

        // -- combat: morale, luck -----------------------------------------

        public int MoraleFromSkills;
        public int LuckFromSkills;
        /// <summary>Elven Luck: lucky strike damage multiplier (2.25).</summary>
        public float LuckDamageMultiplier;
        /// <summary>Luck of the Barbarian: magic proof per luck point.</summary>
        public float MagicProofPerLuck;

        // -- war machines --------------------------------------------------

        /// <summary>War Machines mastery (drives the machine stat tiers).</summary>
        public int WarMachineTier;
        /// <summary>Ammo cart: ranged attack bonus for shooters.</summary>
        public int AmmoCartAttackBonus;
        /// <summary>Catapult: percent chance to destroy a wall section per hit.</summary>
        public int CatapultDestroyChance;
        /// <summary>Ballista perk: extra shots (+1 more with Triple Ballista).</summary>
        public int BallistaExtraShots;
        public int CatapultExtraShots;
        /// <summary>Runic Machines: machine initiative bonus.</summary>
        public int MachineInitiativeBonus;

        // -- tactics, magic schools ----------------------------------------

        /// <summary>Tactics perk: extra deployment columns.</summary>
        public int TacticsExtraColumns;
        /// <summary>School masteries: 0..4, gate castable spell circles.</summary>
        public int DestructiveMastery;
        public int DarkMastery;
        public int LightMastery;
        public int SummoningMastery;
        /// <summary>Wisdom perk: spells up to circle 3 castable regardless.</summary>
        public bool Wisdom;

        /// <summary>Clean slate: every multiplier neutral.</summary>
        public static HeroSkillEffects Create()
        {
            return new HeroSkillEffects
            {
                MovementMultiplier = 1f,
                TerrainPenaltyMultiplier = 1f,
                ManaMultiplier = 1f,
                ManaRegenMultiplier = 1f,
                SpellCostMultiplier = 1f,
                SpellDamageMultiplier = 1f,
                XpMultiplier = 1f,
                MeleeDamageMultiplier = 1f,
                RangedDamageMultiplier = 1f,
                MeleeDamageTakenMultiplier = 1f,
                RangedDamageTakenMultiplier = 1f,
                MagicDamageTakenMultiplier = 1f,
                DefendBonusMultiplier = 1f,
                RetaliationDamageMultiplier = 1f,
                InitiativeMultiplier = 1f,
                CastIntervalMultiplier = 1f,
                LuckDamageMultiplier = 2f,
                CatapultDestroyChance = 25,
            };
        }

        /// <summary>Highest spell circle the hero may learn/cast.</summary>
        public int MaxSpellCircle
        {
            get
            {
                if (Wisdom) return 3;
                var best = 0;
                if (DestructiveMastery > best) best = DestructiveMastery;
                if (DarkMastery > best) best = DarkMastery;
                if (LightMastery > best) best = LightMastery;
                if (SummoningMastery > best) best = SummoningMastery;
                // Shipped rule: school mastery 1 opens circle 3, mastery n
                // opens circle n+2, no school caps at circle 2.
                return best == 0 ? 2 : best + 2;
            }
        }

        /// <summary>
        /// Fold one skill rank in. Rank effects are the shipped per-mastery
        /// numbers from the skill descriptions / HeroSkills constants.
        /// </summary>
        void ApplySkillRank(string skillId, int mastery, GameSkillEffectsDef c)
        {
            switch (skillId)
            {
                case "HERO_SKILL_LOGISTICS":
                    MovementMultiplier += 0.1f * mastery;
                    break;
                case "HERO_SKILL_OFFENCE":
                    MeleeDamageMultiplier += c.offenceDamageBonus[Mathf.Min(mastery, c.offenceDamageBonus.Count) - 1];
                    break;
                case "HERO_SKILL_DEFENCE":
                    MeleeDamageTakenMultiplier -= c.defenceDamageReduce[Mathf.Min(mastery, c.defenceDamageReduce.Count) - 1];
                    break;
                case "HERO_SKILL_LEADERSHIP":
                    MoraleFromSkills += mastery;
                    break;
                case "HERO_SKILL_LUCK":
                    LuckFromSkills += mastery;
                    break;
                case "HERO_SKILL_LEARNING":
                    XpMultiplier += 0.05f * mastery;
                    FreeStatEveryNLevels = 5 - mastery;   // 4 / 3 / 2 levels
                    break;
                case "HERO_SKILL_SORCERY":
                    CastIntervalMultiplier -= 0.1f * mastery;
                    break;
                case "HERO_SKILL_WAR_MACHINES":
                    WarMachineTier = mastery;
                    AmmoCartAttackBonus = mastery;        // +1/2/3 ranged attack
                    CatapultDestroyChance = 20 + 10 * mastery;  // 30/40/50%
                    break;
                case "HERO_SKILL_DESTRUCTIVE_MAGIC":
                    DestructiveMastery = mastery;
                    break;
                case "HERO_SKILL_DARK_MAGIC":
                    DarkMastery = mastery;
                    break;
                case "HERO_SKILL_LIGHT_MAGIC":
                    LightMastery = mastery;
                    break;
                case "HERO_SKILL_SUMMONING_MAGIC":
                    SummoningMastery = mastery;
                    break;
                case "HERO_SKILL_NECROMANCY":
                    // Base 10% + 10% per rank (RaisePercentBase + PerSkillLevel).
                    NecromancyRaisePercent = (c.necromancyRaiseBase + c.necromancyRaisePerLevel * mastery) / 100f;
                    break;
                case "HERO_SKILL_TRAINING":
                    RetaliationDamageMultiplier += RankValue(c.trainingRetaliationBonus, mastery);
                    break;
            }
        }

        /// <summary>Fold one perk in; numbers come from skill texts + HeroSkills.</summary>
        void ApplyPerk(string perkId, GameSkillEffectsDef c)
        {
            switch (perkId)
            {
                // -- standard perks --------------------------------------
                case "HERO_SKILL_PATHFINDING":
                    TerrainPenaltyMultiplier = 0.5f;
                    break;
                case "HERO_SKILL_SCOUTING":
                    ScoutingRadiusBonus += c.scoutingVisibilityIncrease;
                    break;
                case "HERO_SKILL_NAVIGATION":
                    MovementMultiplier += 0.5f;   // sea movement; reused on land maps
                    break;
                case "HERO_SKILL_ARCHERY":
                    RangedDamageMultiplier += c.archeryDamageBonus;
                    break;
                case "HERO_SKILL_FRENZY":
                    DamagePerCreature += 1;
                    break;
                case "HERO_SKILL_EVASION":
                    RangedDamageTakenMultiplier -= c.evasionDamageReduce;
                    break;
                case "HERO_SKILL_PROTECTION":
                    MagicDamageTakenMultiplier -= 0.15f;   // skill text: 15%
                    break;
                case "HERO_SKILL_TOUGHNESS":
                    HealthPerCreature += 2;                // skill text: +2 hp
                    break;
                case "HERO_SKILL_INTELLIGENCE":
                    ManaMultiplier = c.intelligenceManaMultiplier;
                    break;
                case "HERO_SKILL_MYSTICISM":
                    ManaRegenMultiplier = 2f;
                    break;
                case "HERO_SKILL_ARCANE_TRAINING":
                    SpellCostMultiplier -= 0.2f;
                    break;
                case "HERO_SKILL_WISDOM":
                    Wisdom = true;
                    break;
                case "HERO_SKILL_ESTATES":
                    EstatesGoldPerDay += 250;
                    break;
                case "HERO_SKILL_TACTICS":
                    TacticsExtraColumns += 1;
                    break;
                case "HERO_SKILL_BALLISTA":
                    BallistaExtraShots += 1;
                    break;
                case "HERO_SKILL_CATAPULT":
                    CatapultExtraShots += 1;
                    break;
                case "HERO_SKILL_FIRST_AID":
                    break;    // manual tent control; no numeric effect
                case "HERO_SKILL_RESISTANCE":
                    // +15% magic resistance per the skill text; modeled as
                    // spell damage taken reduction alongside Protection.
                    MagicDamageTakenMultiplier -= 0.15f;
                    break;

                // -- class perks ------------------------------------------
                case "HERO_SKILL_TRIPLE_BALLISTA":
                    BallistaExtraShots += 1;
                    break;
                case "HERO_SKILL_TRIPLE_CATAPULT":
                    CatapultExtraShots += 1;
                    break;
                case "HERO_SKILL_HOLD_GROUND":
                    DefendBonusMultiplier = c.holdGroundDefendMultiplier;
                    break;
                case "HERO_SKILL_RETRIBUTION":
                    RetributionPerMorale += c.retributionDamagePerMorale;
                    break;
                case "HERO_SKILL_LAST_STAND":
                case "HERO_SKILL_GUARDIAN_ANGEL":
                case "HERO_SKILL_HOLY_CHARGE":
                case "HERO_SKILL_PRAYER":
                case "HERO_SKILL_EXPERT_TRAINER":
                    break;    // active/proc abilities handled in combat code
                case "HERO_SKILL_QUICKNESS_OF_MIND":
                    InitiativeMultiplier += c.quicknessOfMindInitiativeBonus;
                    break;
                case "HERO_SKILL_FAST_AND_FURIOUS":
                    SpeedBonus += 1;
                    break;
                case "HERO_SKILL_DEATH_TREAD":
                    SiegeSpeedBonus += c.deathTreadSpeedBonus;
                    break;
                case "HERO_SKILL_ROAD_HOME":
                    GrassSpeedBonus += c.roadHomeSpeedBonus;
                    break;
                case "HERO_SKILL_ELVEN_LUCK":
                    LuckDamageMultiplier = c.elvenLuckDamageMultiplier;
                    break;
                case "HERO_SKILL_LUCK_OF_THE_BARBARIAN":
                    MagicProofPerLuck += c.luckOfBarbarianMagicProofPerLuck;
                    break;
                case "HERO_SKILL_EMPOWERED_SPELLS":
                    SpellDamageMultiplier += 0.5f;
                    SpellCostMultiplier += 1f;   // double mana (skill text)
                    break;
                case "HERO_SKILL_LORD_OF_UNDEAD":
                    NecromancyRaisePercent += c.lordOfUndeadRaiseBonus / 100f;
                    break;
                case "HERO_SKILL_BODYBUILDING":
                    HealthPercentBonus += c.bodybuildingHpPercent / 100f;
                    break;
                case "HERO_SKILL_MAGIC_CUSHION":
                    MagicDamageTakenMultiplier -= 0.2f;   // enemy spells -20%
                    break;
                case "HERO_SKILL_RUNIC_MACHINES":
                    MachineInitiativeBonus += c.runicMachinesInitiativeBonus;
                    break;
                case "HERO_SKILL_DISTRACT":
                    break;    // enemy hero after-cast penalty; combat hooks
                case "HERO_SKILL_STORM_WIND":
                case "HERO_SKILL_FOG_VEIL":
                case "HERO_SKILL_DEATH_SCREAM":
                case "HERO_SKILL_POWER_OF_HASTE":
                case "HERO_SKILL_POWER_OF_STONE":
                case "HERO_SKILL_RUNIC_ARMOR":
                case "HERO_SKILL_TELEPORT_ASSAULT":
                case "HERO_SKILL_SHAKE_GROUND":
                    break;    // spell-granting/active perks: granted as spells or combat actions
            }
        }

        static float RankValue(List<float> rows, int mastery)
        {
            if (rows == null || rows.Count == 0) return 0f;
            var i = Mathf.Clamp(mastery, 1, rows.Count) - 1;
            return rows[i];
        }

        /// <summary>
        /// Build the effect set for one hero. Call again whenever skills or
        /// perks change (level-up, load, scripting).
        /// </summary>
        public static HeroSkillEffects Compute(H5HeroState hero, GameDatabase db)
        {
            var fx = Create();
            if (hero == null) return fx;
            var rules = db != null ? db.Rules : null;
            var c = rules != null && rules.skillEffects != null
                ? rules.skillEffects
                : new GameSkillEffectsDef();

            if (hero.Skills != null)
                foreach (var kv in hero.Skills)
                    if (kv.Value > 0)
                        fx.ApplySkillRank(kv.Key, kv.Value, c);

            if (hero.Perks != null)
                foreach (var perk in hero.Perks)
                    fx.ApplyPerk(perk, c);

            fx.MeleeDamageMultiplier = Mathf.Max(0.1f, fx.MeleeDamageMultiplier);
            fx.RangedDamageMultiplier = Mathf.Max(0.1f, fx.RangedDamageMultiplier);
            fx.MeleeDamageTakenMultiplier = Mathf.Clamp(fx.MeleeDamageTakenMultiplier, 0.1f, 2f);
            fx.RangedDamageTakenMultiplier = Mathf.Clamp(fx.RangedDamageTakenMultiplier, 0.1f, 2f);
            fx.MagicDamageTakenMultiplier = Mathf.Clamp(fx.MagicDamageTakenMultiplier, 0.1f, 2f);
            fx.CastIntervalMultiplier = Mathf.Clamp(fx.CastIntervalMultiplier, 0.1f, 1f);
            fx.SpellCostMultiplier = Mathf.Max(0.1f, fx.SpellCostMultiplier);
            return fx;
        }
    }
}
