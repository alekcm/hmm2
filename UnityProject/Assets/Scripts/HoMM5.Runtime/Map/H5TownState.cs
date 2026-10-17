using System.Collections.Generic;
using HoMM5.Data;

namespace HoMM5.Map
{
    /// <summary>
    /// One town on the adventure map, as it changes during a game.
    ///
    /// The imported map only says what a town starts as (its authored
    /// <c>buildings</c> list, with a starting tier and a cap per slot). This is
    /// the part that moves: which buildings have gone up, what the dwellings have
    /// bred since the last recruitment, and who owns it now.
    ///
    /// Every number comes from the shipped tables -- a building's cost and
    /// prerequisites from <c>TownBuildingSharedStats</c>, a dwelling's weekly
    /// number from the creature it breeds, the silo's daily resource and the
    /// moat's damage from <c>TownTypesInfo</c>. What is *not* in the data is
    /// marked where it is used.
    /// </summary>
    public class H5TownState
    {
        /// <summary>Map object id; the key this town is saved under.</summary>
        public string ObjectId = "";
        public string Name = "";
        /// <summary>TOWN_HEAVEN and friends; empty for a random town.</summary>
        public string TownType = "";
        /// <summary>TownBuildingSharedStats folder this town builds from.</summary>
        public string Faction = "";
        public string Owner = "";
        public int Floor;
        /// <summary>Tile position, as the map authors it.</summary>
        public float X;
        public float Y;

        /// <summary>
        /// The authored slots, in the order the map lists them: what the town
        /// starts with and how far this scenario lets each one go.
        /// </summary>
        public List<MapBuildingStateDef> Slots = new List<MapBuildingStateDef>();

        /// <summary>Building keys raised during play, on top of the authored ones.</summary>
        public List<string> Built = new List<string>();

        /// <summary>One building per day, as in the original.</summary>
        public bool BuiltToday;

        /// <summary>Slot type ("TB_DWELLING_1") -&gt; creatures waiting to be recruited.</summary>
        public Dictionary<string, int> Pools = new Dictionary<string, int>();

        /// <summary>
        /// Set when a random town has had its faction rolled. A scenario's
        /// <c>BannedRaces</c> list is honoured; the choice itself is the
        /// prototype's, since the original picks it at map load.
        /// </summary>
        public bool FactionResolved;

        // -- construction ---------------------------------------------------

        /// <summary>Build the state for one placed town object.</summary>
        public static H5TownState FromMapObject(MapObjectDef def, GameDatabase db)
        {
            if (def == null) return null;
            var town = new H5TownState
            {
                ObjectId = def.objectId ?? "",
                Name = string.IsNullOrEmpty(def.name) ? "Town" : def.name,
                TownType = def.townType ?? "",
                Owner = def.owner ?? "",
                Floor = def.floor,
                X = def.x,
                Y = def.y,
            };
            if (def.buildings != null)
                foreach (var slot in def.buildings)
                    if (slot != null && !string.IsNullOrEmpty(slot.type))
                        town.Slots.Add(CopySlot(slot));
            town.ResolveFaction(db, def.bannedRaces);
            return town;
        }

        /// <summary>
        /// Re-attach to a freshly parsed map def after a rebuild (floor switch or
        /// load). The authored slots come back from the JSON, so the tiers raised
        /// during play have to be replayed over them.
        /// </summary>
        public void SyncFromMap(GameDatabase db, MapObjectDef def)
        {
            if (def == null) return;
            Name = string.IsNullOrEmpty(def.name) ? Name : def.name;
            // A chosen/random faction is live state, not an authored default.
            // Re-reading the map on F1/F9 must not undo that choice.
            if (!FactionResolved && !string.IsNullOrEmpty(def.townType)) TownType = def.townType;
            Floor = def.floor;
            X = def.x;
            Y = def.y;
            // def.buildings may *be* Slots: building hands the slot list back to
            // the map def, so clearing in place would empty the list being
            // iterated. Capture the authored list first, then replace.
            var authored = def.buildings;
            Slots = new List<MapBuildingStateDef>();
            if (authored != null)
                foreach (var slot in authored)
                    if (slot != null && !string.IsNullOrEmpty(slot.type))
                        Slots.Add(CopySlot(slot));
            // Owner may have changed hands during play; the def is the imported
            // one, so keep the live value unless this is the first look.
            if (string.IsNullOrEmpty(Owner)) Owner = def.owner ?? "";
            ResolveFaction(db, def.bannedRaces);
            ReplayBuilt(db);
        }

        static MapBuildingStateDef CopySlot(MapBuildingStateDef slot)
        {
            return new MapBuildingStateDef { type = slot.type, upgrade = slot.upgrade, maxUpgrade = slot.maxUpgrade };
        }

        /// <summary>Apply a new-game faction choice before construction/recruitment.</summary>
        public bool SetStartingFaction(GameDatabase db, string townType)
        {
            var type = db != null ? db.GetTownType(townType) : null;
            if (type == null || string.IsNullOrEmpty(type.faction) || type.faction == "RandomTown" ||
                type.buildings == null || type.buildings.Count == 0) return false;
            TownType = type.id;
            Faction = type.faction;
            FactionResolved = true;
            Built.Clear();
            Pools.Clear();
            BuiltToday = false;
            return true;
        }

        /// <summary>Re-apply the tiers built during play onto the authored slots.</summary>
        public void ReplayBuilt(GameDatabase db)
        {
            foreach (var key in Built)
            {
                var building = db != null ? db.GetTownBuilding(key) : null;
                if (building == null) continue;
                var slot = SlotOf(building.type);
                if (slot == null) continue;
                if (building.Tier > slot.Tier) slot.upgrade = building.upgrade;
            }
        }

        /// <summary>
        /// Work out which faction's buildings this town uses.
        ///
        /// A fixed town carries its <c>TOWN_*</c> type from its shared record. A
        /// random town does not -- the original picks the race at load time -- so
        /// one is rolled here from the factions the scenario has not banned.
        /// </summary>
        public void ResolveFaction(GameDatabase db, List<string> bannedRaces)
        {
            if (db == null) return;
            var type = db.GetTownType(TownType);
            if (type != null && !string.IsNullOrEmpty(type.faction) && type.faction != "RandomTown")
            {
                Faction = type.faction;
                FactionResolved = true;
                return;
            }
            if (FactionResolved && !string.IsNullOrEmpty(Faction)) return;

            var banned = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (bannedRaces != null)
                foreach (var race in bannedRaces)
                    if (!string.IsNullOrEmpty(race)) banned.Add(race);

            var choices = new List<string>();
            foreach (var kv in db.TownTypes)
            {
                var t = kv.Value;
                if (t == null || string.IsNullOrEmpty(t.faction) || t.faction == "RandomTown") continue;
                if (banned.Contains(t.id)) continue;
                choices.Add(t.id);
            }
            if (choices.Count == 0) return;
            choices.Sort(System.StringComparer.Ordinal);
            // Deterministic per town, so a reload of the same map rolls the same
            // race; a save carries the resolved faction anyway.
            var seed = 0;
            foreach (var ch in ObjectId) seed = seed * 31 + ch;
            TownType = choices[(int)((uint)seed % (uint)choices.Count)];
            var picked = db.GetTownType(TownType);
            Faction = picked != null ? picked.faction : "";
            FactionResolved = !string.IsNullOrEmpty(Faction);
        }

        // -- what is built --------------------------------------------------

        /// <summary>
        /// The building record standing in one slot right now, or null when the
        /// slot is empty (<c>BLD_UPG_NONE</c>).
        /// </summary>
        public TownBuildingDef BuiltIn(GameDatabase db, string slotType)
        {
            var slot = SlotOf(slotType);
            if (db == null || slot == null || slot.Tier <= 0) return null;
            return BuildingAt(db, slotType, slot.Tier);
        }

        public MapBuildingStateDef SlotOf(string slotType)
        {
            foreach (var slot in Slots)
                if (slot != null && slot.type == slotType) return slot;
            return null;
        }

        /// <summary>The faction's record for one slot at one tier.</summary>
        public TownBuildingDef BuildingAt(GameDatabase db, string slotType, int tier)
        {
            if (db == null || tier <= 0) return null;
            foreach (var b in db.TownBuildingsForFaction(Faction))
                if (b != null && b.type == slotType && b.Tier == tier) return b;
            return null;
        }

        public int FortTier
        {
            get
            {
                var slot = SlotOf("TB_FORT");
                return slot != null ? slot.Tier : 0;
            }
        }

        public bool HasBuilding(GameDatabase db, string slotType, int minTier = 1)
        {
            var slot = SlotOf(slotType);
            return slot != null && slot.Tier >= minTier;
        }

        public bool HasGrail => HasBuilding(null, "TB_GRAIL");

        /// <summary>Fort tier 0-3 for the siege rules (Fort / Citadel / Castle).</summary>
        public int SiegeFortLevel => System.Math.Min(3, FortTier);

        // -- building -------------------------------------------------------

        /// <summary>
        /// The next tier of one slot, or null when the slot is finished, capped by
        /// the scenario, or has no such record.
        /// </summary>
        public TownBuildingDef NextTier(GameDatabase db, string slotType)
        {
            var slot = SlotOf(slotType);
            if (db == null || slot == null) return null;
            var next = slot.Tier + 1;
            if (slot.MaxTier > 0 && next > slot.MaxTier) return null;
            return BuildingAt(db, slotType, next);
        }

        /// <summary>Every slot that has a next tier available.</summary>
        public List<TownBuildingDef> Buildable(GameDatabase db)
        {
            var outList = new List<TownBuildingDef>();
            if (db == null) return outList;
            foreach (var slot in Slots)
            {
                if (slot == null) continue;
                var next = NextTier(db, slot.type);
                if (next != null) outList.Add(next);
            }
            return outList;
        }

        /// <summary>Why a building cannot go up yet, or null when it can.</summary>
        public string BuildBlocker(GameDatabase db, TownBuildingDef building,
                                   PlayerResourceInventory resources)
        {
            if (db == null || building == null) return "Unknown building.";
            if (BuiltToday) return "Already built here this day.";
            var slot = SlotOf(building.type);
            if (slot == null) return "This town has no such building.";
            if (building.Tier != slot.Tier + 1) return "Build the earlier tiers first.";
            if (slot.MaxTier > 0 && building.Tier > slot.MaxTier)
                return "This scenario does not allow that upgrade.";
            if (!DependenciesMet(db, building)) return "A prerequisite is missing.";
            if (!CanAfford(resources, building.cost)) return "Not enough resources.";
            return null;
        }

        /// <summary>
        /// Whether everything this building needs is standing.
        ///
        /// One shipped row is broken: Necromancy's Ruined Tower depends on
        /// <c>/Preserve/Fort/Fort.xdb</c>, an absolute href into another
        /// faction's folder, where every other dependency in the table is
        /// relative. A Necropolis can never hold a Sylvan Fort, so
        /// cross-faction prerequisites are ignored rather than making the
        /// building permanently unbuildable.
        /// </summary>
        public bool DependenciesMet(GameDatabase db, TownBuildingDef building)
        {
            if (db == null || building == null || building.dependencies == null) return true;
            foreach (var dep in building.dependencies)
            {
                var depBuilding = db.GetTownBuilding(dep);
                if (depBuilding == null) continue;
                if (!string.Equals(depBuilding.faction, Faction, System.StringComparison.OrdinalIgnoreCase))
                    continue;                       // the shipped cross-faction row
                if (!IsBuilt(db, dep)) return false;
            }
            return true;
        }

        /// <summary>Whether a building key is standing in this town.</summary>
        public bool IsBuilt(GameDatabase db, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var building = db != null ? db.GetTownBuilding(key) : null;
            if (building == null) return Built.Contains(key);
            if (Built.Contains(key)) return true;
            var slot = SlotOf(building.type);
            return slot != null && slot.Tier >= building.Tier;
        }

        /// <summary>
        /// Raise a building: take the cost, move the slot to that tier. Returns
        /// false (with a reason in the log) when it is not allowed.
        /// </summary>
        public bool Build(GameDatabase db, TownBuildingDef building,
                          PlayerResourceInventory resources, out string reason)
        {
            reason = BuildBlocker(db, building, resources);
            if (reason != null) return false;
            Pay(resources, building.cost);
            var slot = SlotOf(building.type);
            slot.upgrade = building.upgrade;
            Built.Add(building.key);
            BuiltToday = true;
            return true;
        }

        // -- dwellings and recruitment --------------------------------------

        /// <summary>The dwellings this town has, in tier order.</summary>
        public List<TownBuildingDef> Dwellings(GameDatabase db)
        {
            var outList = new List<TownBuildingDef>();
            if (db == null) return outList;
            for (var tier = 1; tier <= 7; tier++)
            {
                var built = BuiltIn(db, "TB_DWELLING_" + tier);
                if (built != null && built.HasCreature) outList.Add(built);
            }
            return outList;
        }

        /// <summary>Creatures waiting in one dwelling.</summary>
        public int PoolOf(string slotType)
        {
            return Pools.TryGetValue(slotType ?? "", out var n) ? n : 0;
        }

        /// <summary>
        /// The weekly number a dwelling breeds, with the Citadel/Castle and Grail
        /// multipliers applied.
        ///
        /// The multipliers are the shipped descriptions' own numbers ("прирост
        /// существ увеличивается на 50%", "удваивается", and the Grail's
        /// "+50%"); they exist nowhere as a figure, so the importer parses them
        /// out of the text and they arrive as <c>growthMultiplier</c>. The
        /// Citadel's and the Castle's are alternatives, not cumulative -- each
        /// describes the state of the town at that tier.
        /// </summary>
        public int WeeklyGrowth(GameDatabase db, TownBuildingDef dwelling)
        {
            if (db == null || dwelling == null) return 0;
            var growth = dwelling.growth;
            if (growth <= 0) return 0;
            var multiplier = FortGrowthMultiplier(db);
            var grail = BuiltIn(db, "TB_GRAIL");
            if (grail != null && grail.growthMultiplier > 1f) multiplier *= grail.growthMultiplier;
            return System.Math.Max(1, (int)System.Math.Round(growth * multiplier));
        }

        /// <summary>1.0 / 1.5 / 2.0 by fort tier, from the buildings' own text.</summary>
        public float FortGrowthMultiplier(GameDatabase db)
        {
            var fort = BuiltIn(db, "TB_FORT");
            return fort != null && fort.growthMultiplier > 0f ? fort.growthMultiplier : 1f;
        }

        /// <summary>Add a week's breeding to every dwelling.</summary>
        public void AddWeeklyGrowth(GameDatabase db)
        {
            foreach (var dwelling in Dwellings(db))
            {
                var add = WeeklyGrowth(db, dwelling);
                if (add <= 0) continue;
                var slotType = dwelling.type;
                Pools[slotType] = PoolOf(slotType) + add;
            }
        }

        /// <summary>
        /// Recruit creatures out of a dwelling's pool into an army. The price is
        /// the creature's own <c>Cost</c> row.
        /// </summary>
        public int Recruit(GameDatabase db, string slotType, string creatureId, int wanted,
                           PlayerResourceInventory resources, List<Combat.ArmyStackState> army,
                           int armySlotLimit, out string reason)
        {
            reason = null;
            var available = PoolOf(slotType);
            if (available <= 0)
            {
                reason = "There is nobody to recruit there.";
                return 0;
            }
            var creature = db != null ? db.GetCreature(creatureId) : null;
            if (creature == null)
            {
                reason = "Unknown creature " + creatureId;
                return 0;
            }
            var count = wanted <= 0 ? available : System.Math.Min(wanted, available);
            if (creature.cost != null && !creature.cost.IsFree)
            {
                // Recruit only as many as the treasury covers.
                var affordable = count;
                while (affordable > 0 && !CanAfford(resources, creature.cost, affordable))
                    affordable--;
                if (affordable <= 0)
                {
                    reason = "Not enough gold for even one " + creature.DisplayName + ".";
                    return 0;
                }
                count = affordable;
            }
            if (army == null)
            {
                reason = "There is no army to take them.";
                return 0;
            }
            if (!HasStackFor(army, creatureId) && FreeArmySlots(army, armySlotLimit) <= 0)
            {
                reason = "The army has no free slot.";
                return 0;
            }
            Pay(resources, creature.cost, count);
            AddToArmy(army, creatureId, count);
            Pools[slotType] = available - count;
            return count;
        }

        /// <summary>
        /// An upgraded dwelling opens a second creature (Academy's Manufactory
        /// recruits Master Gremlins *or* Gremlin Saboteurs). Both come out of the
        /// one pool, as they do in the original.
        /// </summary>
        public List<string> Recruitables(GameDatabase db, string slotType)
        {
            var outList = new List<string>();
            var dwelling = BuiltIn(db, slotType);
            if (dwelling == null) return outList;
            if (dwelling.HasCreature) outList.Add(dwelling.creature);
            if (!string.IsNullOrEmpty(dwelling.creature2) && dwelling.creature2 != "CREATURE_UNKNOWN")
                outList.Add(dwelling.creature2);
            return outList;
        }

        // -- income ---------------------------------------------------------

        /// <summary>
        /// What this town pays the treasury each day: the town hall tier's gold
        /// and, with a resource silo (marketplace tier 2) standing, the faction's
        /// silo resource.
        /// </summary>
        public void PayDailyIncome(GameDatabase db, PlayerResourceInventory resources,
                                   out int gold, out string siloResource, out int siloAmount)
        {
            gold = 0;
            siloResource = null;
            siloAmount = 0;
            if (db == null || resources == null) return;

            var hall = BuiltIn(db, "TB_TOWN_HALL");
            if (hall != null && hall.incomeGoldPerDay > 0) gold = hall.incomeGoldPerDay;
            var grail = BuiltIn(db, "TB_GRAIL");
            if (grail != null && grail.incomeGoldPerDay > 0) gold += grail.incomeGoldPerDay;

            var silo = BuiltIn(db, "TB_MARKETPLACE");
            var townType = db.GetTownType(TownType);
            if (silo != null && silo.Tier >= 2 && townType != null && townType.siloIncome != null)
            {
                AddResource(resources, "Wood", townType.siloIncome.Wood);
                AddResource(resources, "Ore", townType.siloIncome.Ore);
                AddResource(resources, "Mercury", townType.siloIncome.Mercury);
                AddResource(resources, "Crystal", townType.siloIncome.Crystal);
                AddResource(resources, "Sulfur", townType.siloIncome.Sulfur);
                AddResource(resources, "Gem", townType.siloIncome.Gem);
                AddResource(resources, "Gold", townType.siloIncome.Gold);
                siloResource = DominantResource(townType.siloIncome);
                siloAmount = ResourceAmount(townType.siloIncome, siloResource);
            }
            if (gold > 0) resources.gold += gold;
        }

        static string DominantResource(ResourceCost cost)
        {
            if (cost.Wood != 0) return "Wood";
            if (cost.Ore != 0) return "Ore";
            if (cost.Mercury != 0) return "Mercury";
            if (cost.Crystal != 0) return "Crystal";
            if (cost.Sulfur != 0) return "Sulfur";
            if (cost.Gem != 0) return "Gems";
            return cost.Gold != 0 ? "Gold" : null;
        }

        static int ResourceAmount(ResourceCost cost, string resource)
        {
            switch (resource)
            {
                case "Wood": return cost.Wood;
                case "Ore": return cost.Ore;
                case "Mercury": return cost.Mercury;
                case "Crystal": return cost.Crystal;
                case "Sulfur": return cost.Sulfur;
                case "Gem": case "Gems": return cost.Gem;
                case "Gold": return cost.Gold;
                default: return 0;
            }
        }

        static void AddResource(PlayerResourceInventory resources, string name, int amount)
        {
            if (amount != 0) resources.Add(name, amount);
        }

        // -- helpers --------------------------------------------------------

        public static bool CanAfford(PlayerResourceInventory resources, ResourceCost cost, int times = 1)
        {
            if (cost == null || cost.IsFree) return true;
            if (resources == null) return false;
            return resources.wood >= cost.Wood * times && resources.ore >= cost.Ore * times &&
                   resources.mercury >= cost.Mercury * times && resources.crystal >= cost.Crystal * times &&
                   resources.sulfur >= cost.Sulfur * times && resources.gem >= cost.Gem * times &&
                   resources.gold >= cost.Gold * times;
        }

        static void Pay(PlayerResourceInventory resources, ResourceCost cost, int times = 1)
        {
            if (cost == null || cost.IsFree || resources == null || times <= 0) return;
            resources.wood -= cost.Wood * times;
            resources.ore -= cost.Ore * times;
            resources.mercury -= cost.Mercury * times;
            resources.crystal -= cost.Crystal * times;
            resources.sulfur -= cost.Sulfur * times;
            resources.gem -= cost.Gem * times;
            resources.gold -= cost.Gold * times;
        }

        static bool HasStackFor(List<Combat.ArmyStackState> army, string creatureId)
        {
            foreach (var s in army)
                if (s != null && s.CreatureId == creatureId) return true;
            return false;
        }

        static int FreeArmySlots(List<Combat.ArmyStackState> army, int limit)
        {
            if (limit <= 0) return 1;
            var used = 0;
            foreach (var s in army)
                if (s != null && s.Count > 0) used++;
            return limit - used;
        }

        static void AddToArmy(List<Combat.ArmyStackState> army, string creatureId, int count)
        {
            foreach (var s in army)
            {
                if (s == null || s.CreatureId != creatureId) continue;
                s.Count += count;
                return;
            }
            army.Add(new Combat.ArmyStackState(creatureId, count));
        }

        /// <summary>Everything the town screen prints in its header.</summary>
        public string Summary(GameDatabase db)
        {
            var type = db != null ? db.GetTownType(TownType) : null;
            var factionName = type != null ? type.DisplayName : GameDatabase.PrettifyEnum(TownType);
            return Name + "   —   " + (string.IsNullOrEmpty(factionName) ? "Town" : factionName);
        }
    }
}
