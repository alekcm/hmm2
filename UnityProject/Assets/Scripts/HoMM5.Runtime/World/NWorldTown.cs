using System;
using System.Collections.Generic;
using HoMM5.Data;

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
        /// <summary>
        /// Scenario-provided resource stock.  CPlayer construction does not
        /// establish a universal starting wallet; map/scenario data must fill it.
        /// </summary>
        public ResourceCost Resources { get; } = new ResourceCost();

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

        public bool TryBuild(TownBuildingDef building)
        {
            if (building == null || HasBuiltToday) return false;
            if (BuiltBuildings.Contains(building.id)) return false;
            if (Owner != null && !Owner.Spend(building.cost)) return false;

            BuiltBuildings.Add(building.id);
            HasBuiltToday = true;
            return true;
        }

        /// <summary>
        /// Starts a new town day.  Income and creature growth depend on the
        /// resolved TownType/TownBuilding records and are deliberately not
        /// replaced with hard-coded Capitol/Castle tables here.
        /// </summary>
        public void OnNewDay(int dayOfWeek)
        {
            HasBuiltToday = false;
        }
    }
}
