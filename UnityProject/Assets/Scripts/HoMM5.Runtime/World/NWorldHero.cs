using System;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.World
{
    /// <summary>
    /// Hero stat enum matching <c>NWorld::GetHeroStat</c> (<c>0x005c33a0</c>)
    /// and <c>NWorld::ChangeHeroStat</c> (<c>0x005c3620</c>) in <c>H5_Game_PEST.exe</c>.
    /// </summary>
    public enum EHeroStat
    {
        Experience = 0,
        Attack = 1,
        Defence = 2,
        SpellPower = 3,
        Knowledge = 4,
        Luck = 5,
        Morale = 6,
        MovePoints = 7,
        ManaPoints = 8
    }

    [Serializable]
    public struct HeroCreatureStack
    {
        public string CreatureId;
        public int Count;
    }

    /// <summary>
    /// Decompiled implementation of <c>NWorld::CHero</c> (vftable <c>0x00e7e484</c> in <c>H5_Game_PEST.exe</c>).
    ///
    /// Implements:
    /// - 40-level XP progression table (<c>0x00b4c6b0</c>, seed at <c>0x00f755e4</c>, table at <c>0x01026d90</c>)
    /// - Primary &amp; secondary stat clamping (<c>0x00b5ad70</c>, <c>0x00b4ca10..0x00b4cde0</c>)
    /// - Movement points (<c>+0x13c</c>, base <c>2500</c> from <c>DefaultStats.xdb</c>)
    /// - Mana points (<c>+0x140</c>, <c>Knowledge * 10</c>)
    /// - 7 army slots and equipped artifact stat modifiers
    /// </summary>
    public class NWorldHero
    {
        public const int MaxHeroLevel = 40;
        public const int ArmySlotCount = 7;

        /// <summary>
        /// Seed XP thresholds for Levels 1..12 from <c>.data</c> <c>0x00f755e4</c>.
        /// </summary>
        private static readonly int[] SeedXpThresholds =
        {
            0, 1000, 2000, 3200, 4600, 6200,
            8000, 10000, 12200, 14700, 17500, 20600
        };

        private static int[] _xpTable;

        /// <summary>
        /// Computes the 40-level hero XP table exactly as <c>0x00b4c6b0</c> in <c>H5_Game_PEST.exe</c>.
        /// </summary>
        public static int[] GetExperienceTable()
        {
            if (_xpTable != null) return _xpTable;

            var table = new int[MaxHeroLevel];
            for (var i = 0; i < SeedXpThresholds.Length; i++)
                table[i] = SeedXpThresholds[i];

            // Levels 13..24 (0-based indices 12..23): delta = (int)(prevDelta * 1.2f)
            for (var i = 12; i < 24; i++)
            {
                var prevDelta = table[i - 1] - table[i - 2];
                var delta = (int)(prevDelta * 1.2f);
                table[i] = table[i - 1] + delta;
            }

            // Levels 25..40 (0-based indices 24..39): delta = (int)(prevDelta * (i - 11) * 0.1f)
            // Note: in 1-based indexing i=25..40 from 0x00b4c6e1, multiplier is (i - 13) * 0.1f = 1.2f..2.7f.
            for (var level1Based = 25; level1Based <= MaxHeroLevel; level1Based++)
            {
                var idx = level1Based - 1;
                var prevDelta = table[idx - 1] - table[idx - 2];
                var mult = (level1Based - 13) * 0.1f;
                var delta = (int)(prevDelta * mult);
                table[idx] = table[idx - 1] + delta;
            }

            _xpTable = table;
            return _xpTable;
        }

        public static int GetLevelForExperience(int exp)
        {
            var table = GetExperienceTable();
            var level = 1;
            for (var i = 0; i < table.Length; i++)
            {
                if (exp >= table[i]) level = i + 1;
                else break;
            }
            return level;
        }

        public string HeroId { get; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        public int Floor { get; set; }
        public int NativeTerrainType { get; set; } = 1;

        public int Experience { get; private set; }
        public int Level => GetLevelForExperience(Experience);

        public int BaseAttack { get; set; } = 1;
        public int BaseDefence { get; set; } = 1;
        public int BaseSpellPower { get; set; } = 1;
        public int BaseKnowledge { get; set; } = 1;
        public int BaseMorale { get; set; }
        public int BaseLuck { get; set; }

        public int MovePoints { get; set; } = 2500;
        public int ManaPoints { get; set; } = 10;

        public int LogisticsMastery { get; set; }
        public bool HasPathfinding { get; set; }
        public bool HasSnatch { get; set; }
        public int IntelligenceMastery { get; set; }

        public HeroCreatureStack[] Army { get; } = new HeroCreatureStack[ArmySlotCount];
        public List<ArtifactDef> EquippedArtifacts { get; } = new List<ArtifactDef>();

        private readonly DefaultStatsDef _defaultStats;

        public NWorldHero(string heroId, DefaultStatsDef defaultStats = null)
        {
            HeroId = heroId;
            _defaultStats = defaultStats ?? new DefaultStatsDef();
            MovePoints = GetMaxMovePoints();
            ManaPoints = GetMaxManaPoints();
        }

        /// <summary>
        /// Effective Attack clamped to <c>&gt;= 0</c> (<c>0x00b4ca60</c>).
        /// </summary>
        public int Attack
        {
            get
            {
                var total = BaseAttack;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.Attack;
                return Mathf.Max(0, total);
            }
        }

        /// <summary>
        /// Effective Defence clamped to <c>&gt;= 0</c> (<c>0x00b4cab0</c>).
        /// </summary>
        public int Defence
        {
            get
            {
                var total = BaseDefence;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.Defence;
                return Mathf.Max(0, total);
            }
        }

        /// <summary>
        /// Effective SpellPower clamped to <c>&gt;= 1</c> (<c>0x00b4cb00</c>).
        /// </summary>
        public int SpellPower
        {
            get
            {
                var total = BaseSpellPower;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.SpellPower;
                return Mathf.Max(1, total);
            }
        }

        /// <summary>
        /// Effective Knowledge clamped to <c>&gt;= 1</c> (<c>0x00b4cb50</c>).
        /// </summary>
        public int Knowledge
        {
            get
            {
                var total = BaseKnowledge;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.Knowledge;
                return Mathf.Max(1, total);
            }
        }

        /// <summary>
        /// Effective Morale clamped to <c>[-5, +5]</c> (<c>0x00b4cb60</c>).
        /// </summary>
        public int Morale
        {
            get
            {
                var total = BaseMorale;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.Morale;
                return Mathf.Clamp(total, -5, 5);
            }
        }

        /// <summary>
        /// Effective Luck clamped to <c>[-5, +5]</c> (<c>0x00b4cd40</c>).
        /// </summary>
        public int Luck
        {
            get
            {
                var total = BaseLuck;
                foreach (var a in EquippedArtifacts)
                    if (a?.stats != null) total += a.stats.Luck;
                return Mathf.Clamp(total, -5, 5);
            }
        }

        public int GetMaxMovePoints()
        {
            var baseMove = _defaultStats?.adventure != null ? _defaultStats.adventure.baseHeroMovement : 2500;
            var mult = 1.0f + 0.10f * Mathf.Clamp(LogisticsMastery, 0, 3);
            return Mathf.RoundToInt(baseMove * mult);
        }

        public int GetMaxManaPoints()
        {
            var perKnow = _defaultStats?.adventure != null ? _defaultStats.adventure.heroManaPerKnowledge : 10;
            var baseMana = Knowledge * perKnow;
            var mult = IntelligenceMastery > 0 ? 1.5f : 1.0f;
            return Mathf.RoundToInt(baseMana * mult);
        }

        public int GetHeroStat(EHeroStat stat)
        {
            switch (stat)
            {
                case EHeroStat.Experience: return Experience;
                case EHeroStat.Attack: return Attack;
                case EHeroStat.Defence: return Defence;
                case EHeroStat.SpellPower: return SpellPower;
                case EHeroStat.Knowledge: return Knowledge;
                case EHeroStat.Luck: return Luck;
                case EHeroStat.Morale: return Morale;
                case EHeroStat.MovePoints: return MovePoints;
                case EHeroStat.ManaPoints: return ManaPoints;
                default: return 0;
            }
        }

        public void ChangeHeroStat(EHeroStat stat, int delta)
        {
            switch (stat)
            {
                case EHeroStat.Experience:
                    Experience = Mathf.Max(0, Experience + delta);
                    break;
                case EHeroStat.Attack:
                    BaseAttack = Mathf.Max(0, BaseAttack + delta);
                    break;
                case EHeroStat.Defence:
                    BaseDefence = Mathf.Max(0, BaseDefence + delta);
                    break;
                case EHeroStat.SpellPower:
                    BaseSpellPower = Mathf.Max(1, BaseSpellPower + delta);
                    break;
                case EHeroStat.Knowledge:
                    BaseKnowledge = Mathf.Max(1, BaseKnowledge + delta);
                    break;
                case EHeroStat.Luck:
                    BaseLuck = Mathf.Clamp(BaseLuck + delta, -5, 5);
                    break;
                case EHeroStat.Morale:
                    BaseMorale = Mathf.Clamp(BaseMorale + delta, -5, 5);
                    break;
                case EHeroStat.MovePoints:
                    MovePoints = Mathf.Max(0, MovePoints + delta);
                    break;
                case EHeroStat.ManaPoints:
                    ManaPoints = Mathf.Clamp(ManaPoints + delta, 0, GetMaxManaPoints());
                    break;
            }
        }
    }
}
