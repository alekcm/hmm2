using System;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.World
{
    /// <summary>
    /// Decompiled implementation of <c>NWorld::CPlayer</c> (vftable <c>0x00e1a09c</c>)
    /// 7-resource economy (<c>Wood</c>, <c>Ore</c>, <c>Mercury</c>, <c>Crystal</c>,
    /// <c>Sulfur</c>, <c>Gem</c>, <c>Gold</c>).
    /// </summary>
    public class NWorldPlayer
    {
        public string PlayerId { get; }
        public ResourceCost Resources { get; } = new ResourceCost
        {
            Wood = 20,
            Ore = 20,
            Mercury = 10,
            Crystal = 10,
            Sulfur = 10,
            Gem = 10,
            Gold = 20000
        };

        public NWorldPlayer(string playerId)
        {
            PlayerId = playerId;
        }

        public bool CanAfford(ResourceCost cost)
        {
            if (cost == null) return true;
            return Resources.Wood >= cost.Wood &&
                   Resources.Ore >= cost.Ore &&
                   Resources.Mercury >= cost.Mercury &&
                   Resources.Crystal >= cost.Crystal &&
                   Resources.Sulfur >= cost.Sulfur &&
                   Resources.Gem >= cost.Gem &&
                   Resources.Gold >= cost.Gold;
        }

        public bool Spend(ResourceCost cost)
        {
            if (!CanAfford(cost)) return false;
            if (cost == null) return true;
            Resources.Wood -= cost.Wood;
            Resources.Ore -= cost.Ore;
            Resources.Mercury -= cost.Mercury;
            Resources.Crystal -= cost.Crystal;
            Resources.Sulfur -= cost.Sulfur;
            Resources.Gem -= cost.Gem;
            Resources.Gold -= cost.Gold;
            return true;
        }
    }

    /// <summary>
    /// Decompiled implementation of <c>NWorld::CTown</c> (vftable <c>0x00e19674</c>)
    /// in <c>H5_Game_PEST.exe</c>.
    /// </summary>
    public class NWorldTown
    {
        public string TownName { get; set; }
        public string TownType { get; set; }
        public NWorldPlayer Owner { get; set; }
        public bool HasBuiltToday { get; private set; }
        public HashSet<string> BuiltBuildings { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> AvailableRecruits { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public NWorldTown(string townName, string townType, NWorldPlayer owner = null)
        {
            TownName = townName;
            TownType = townType;
            Owner = owner;
        }

        public int TownLevel => BuiltBuildings.Count;

        public int GetDailyGoldIncome()
        {
            if (BuiltBuildings.Contains("TB_CAPITOL")) return 4000;
            if (BuiltBuildings.Contains("TB_CITY_HALL")) return 2000;
            if (BuiltBuildings.Contains("TB_TOWN_HALL")) return 1000;
            return 500;
        }

        public float GetFortificationGrowthMultiplier()
        {
            if (BuiltBuildings.Contains("TB_CASTLE")) return 2.0f;
            if (BuiltBuildings.Contains("TB_CITADEL")) return 1.5f;
            return 1.0f;
        }

        public bool TryBuild(TownBuildingDef building)
        {
            if (building == null || HasBuiltToday) return false;
            if (BuiltBuildings.Contains(building.id)) return false;
            if (Owner != null && !Owner.Spend(building.cost)) return false;

            BuiltBuildings.Add(building.id);
            HasBuiltToday = true;
            return true;
        }

        public void OnNewDay(int dayOfWeek, IEnumerable<CreatureDef> factionCreatures = null)
        {
            HasBuiltToday = false;
            if (Owner != null)
                Owner.Resources.Gold += GetDailyGoldIncome();

            // Weekly growth on Day 1 of the week
            if (dayOfWeek == 1 && factionCreatures != null)
            {
                var mult = GetFortificationGrowthMultiplier();
                foreach (var c in factionCreatures)
                {
                    if (c == null || c.WeeklyGrowth <= 0) continue;
                    var add = Mathf.RoundToInt(c.WeeklyGrowth * mult);
                    AvailableRecruits.TryGetValue(c.id, out var cur);
                    AvailableRecruits[c.id] = cur + add;
                }
            }
        }
    }
}
