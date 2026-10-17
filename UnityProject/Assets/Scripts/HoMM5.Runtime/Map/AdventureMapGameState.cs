using System;
using System.Collections.Generic;

namespace HoMM5.Map
{
    /// <summary>
    /// Small, local adventure-map turn loop. This deliberately models only the
    /// player-facing day and hero movement budget; original turn order, terrain
    /// multipliers and army-based movement calculations remain engine features.
    /// </summary>
    public sealed class AdventureMapGameState
    {
        // DefaultHeroMovement is read from RPGStats/DefaultStats.xdb. The native
        // engine combines it with army, terrain, skills and artifacts; only the
        // shipped base value is reproduced here so far.
        public const float DefaultDailyHeroMovement = 2500f;
        // Temporary grid conversion until CHeroMovementCost/CTetraMovementCost
        // have been fully reconstructed from the native executable.
        public const float PrototypeMovementPointsPerTile = 100f;
        public float DailyHeroMovement { get; private set; } = DefaultDailyHeroMovement;

        public void SetBaseHeroMovement(float points)
        {
            if (points > 0f) DailyHeroMovement = points;
        }

        readonly Dictionary<string, float> _movement = new Dictionary<string, float>();
        readonly List<H5MapObject> _heroes = new List<H5MapObject>();
        readonly HashSet<string> _collectedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HoMM5.Data.MapDef _map;
        HoMM5.Data.GameRulesDef _rules;
        List<HoMM5.Data.TownBuildingDef> _townBuildingDefs;

        public HoMM5.Data.PlayerResourceInventory Resources { get; private set; } =
            new HoMM5.Data.PlayerResourceInventory();
        public int Day { get; private set; } = 1;
        public int Week => ((Day - 1) / 7) % 4 + 1;
        public int Month => (Day - 1) / 28 + 1;
        public string MapId { get; private set; } = "";
        public string LocalPlayerId { get; private set; } = "PLAYER_1";
        public event Action Changed;
        public bool HasMovingHero
        {
            get
            {
                for (var i = 0; i < _heroes.Count; i++)
                    if (_heroes[i] != null && _heroes[i].IsMoving) return true;
                return false;
            }
        }

        /// <summary>Attach the currently rendered floor, preserving day/moves on floor switches.</summary>
        public void BindMap(MapSceneBuilder builder, bool newScenario, string localPlayerId,
                            HoMM5.Data.GameRulesDef rules,
                            List<HoMM5.Data.TownBuildingDef> townBuildingDefs,
                            int difficultyIndex)
        {
            if (newScenario)
            {
                Day = 1;
                _movement.Clear();
                _collectedObjects.Clear();
            }
            _heroes.Clear();
            LocalPlayerId = string.IsNullOrEmpty(localPlayerId) ? "PLAYER_1" : localPlayerId;
            _map = builder != null ? builder.Map : null;
            _rules = rules;
            _townBuildingDefs = townBuildingDefs;
            MapId = _map != null ? _map.id : "";
            if (newScenario) InitializeResources(rules, difficultyIndex);
            if (builder != null && builder.Root != null)
            {
                var objects = builder.Root.GetComponentsInChildren<H5MapObject>(true);
                for (var i = 0; i < objects.Length; i++)
                {
                    var hero = objects[i];
                    if (hero == null) continue;
                    if (hero.Def != null && !string.IsNullOrEmpty(hero.Def.objectId) &&
                        _collectedObjects.Contains(hero.Def.objectId))
                    {
                        hero.gameObject.SetActive(false);
                        continue;
                    }
                    if (!hero.IsHero) continue;
                    var key = KeyFor(hero.Def, i);
                    var isLocalHero = hero.Def != null &&
                        string.Equals(hero.Def.owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase);
                    if (isLocalHero) _heroes.Add(hero);
                    hero.BindMovement(isLocalHero ? this : null, key,
                                      isLocalHero ? GetMovement(key) : 0f);
                }
            }
            Changed?.Invoke();
        }

        public void EndDay()
        {
            Day++;
            PayDailyIncome();
            _movement.Clear();
            for (var i = 0; i < _heroes.Count; i++)
                if (_heroes[i] != null) _heroes[i].SetMovementPoints(DailyHeroMovement);
            Changed?.Invoke();
        }

        void InitializeResources(HoMM5.Data.GameRulesDef rules, int difficultyIndex)
        {
            Resources = new HoMM5.Data.PlayerResourceInventory();
            var rows = rules != null ? rules.startHumanResources : null;
            if (rows != null && rows.Count > 0)
            {
                var index = Math.Max(0, Math.Min(rows.Count - 1, difficultyIndex));
                Resources.Set(rows[index]);
            }
            else
            {
                // advmap-common.lua's default resources at normal difficulty.
                Resources.Set(new HoMM5.Data.StartResourcesDef
                {
                    wood = 20, ore = 20, mercury = 10, crystal = 10,
                    sulfur = 10, gem = 10, gold = 20000
                });
            }
        }

        /// <summary>
        /// Collect map-authored custom resource piles when a local hero enters
        /// their active tile. Randomly generated piles and chests are left alone;
        /// gold is also deferred until its native amount scaling is verified.
        /// </summary>
        internal void HeroEnteredTile(H5MapObject hero, UnityEngine.Vector2Int tile)
        {
            if (hero == null || hero.Def == null || _map == null || _map.objects == null) return;
            for (var i = 0; i < _map.objects.Count; i++)
            {
                var obj = _map.objects[i];
                if (obj == null || obj.kind != "AdvMapTreasure" || !obj.isCustom || obj.amount <= 0 ||
                    obj.floor != hero.Def.floor ||
                    Math.Abs(obj.x - tile.x) > 0.49f || Math.Abs(obj.y - tile.y) > 0.49f ||
                    string.IsNullOrEmpty(obj.objectId) || _collectedObjects.Contains(obj.objectId)) continue;
                var resource = FixedTreasureResource(obj.sharedType);
                if (string.IsNullOrEmpty(resource)) continue;

                Resources.Add(resource, obj.amount);
                _collectedObjects.Add(obj.objectId);
                HideCollectedMapObject(hero, obj.objectId);
                Changed?.Invoke();
                return;
            }
        }

        static string FixedTreasureResource(string sharedType)
        {
            switch ((sharedType ?? "").ToUpperInvariant())
            {
                case "TREASURE_WOOD": return "WOOD";
                case "TREASURE_ORE": return "ORE";
                case "TREASURE_MERCURY": return "MERCURY";
                case "TREASURE_CRYSTAL": return "CRYSTAL";
                case "TREASURE_SULFUR": return "SULFUR";
                case "TREASURE_GEMS": return "GEM";
                // Gold's serialized amount uses engine-side scaling not yet
                // confirmed from the native pickup command.
                default: return null;
            }
        }

        static void HideCollectedMapObject(H5MapObject hero, string objectId)
        {
            if (hero == null || hero.Builder == null || hero.Builder.Root == null) return;
            var objects = hero.Builder.Root.GetComponentsInChildren<H5MapObject>(true);
            for (var i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && objects[i].Def != null &&
                    string.Equals(objects[i].Def.objectId, objectId, StringComparison.OrdinalIgnoreCase))
                {
                    objects[i].gameObject.SetActive(false);
                    return;
                }
            }
        }

        void PayDailyIncome()
        {
            if (_map == null || _map.objects == null) return;
            for (var i = 0; i < _map.objects.Count; i++)
            {
                var obj = _map.objects[i];
                if (obj == null || !string.Equals(obj.owner, LocalPlayerId,
                                                  StringComparison.OrdinalIgnoreCase)) continue;
                if (obj.kind == "AdvMapMine" && _rules != null && _rules.mineDailyIncome != null &&
                    !string.IsNullOrEmpty(obj.resource) &&
                    _rules.mineDailyIncome.TryGetValue(obj.resource.ToUpperInvariant(), out var mineIncome))
                    Resources.Add(obj.resource, mineIncome);
                else if (obj.kind == "AdvMapTown")
                    PayTownIncome(obj);
            }
        }

        void PayTownIncome(HoMM5.Data.MapObjectDef town)
        {
            if (town.buildings == null || _townBuildingDefs == null) return;
            var dailyGold = 0;
            for (var i = 0; i < town.buildings.Count; i++)
            {
                var state = town.buildings[i];
                if (state == null) continue;
                for (var j = 0; j < _townBuildingDefs.Count; j++)
                {
                    var building = _townBuildingDefs[j];
                    if (building == null || building.type != "TB_TOWN_HALL" ||
                        !string.Equals(building.upgrade, state.upgrade, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (building.incomeGoldPerDay > dailyGold) dailyGold = building.incomeGoldPerDay;
                }
            }
            if (dailyGold > 0) Resources.gold += dailyGold;
        }

        internal float GetMovement(string key)
        {
            return !string.IsNullOrEmpty(key) && _movement.TryGetValue(key, out var points)
                ? points : DailyHeroMovement;
        }

        internal void StoreMovement(string key, float points)
        {
            if (!string.IsNullOrEmpty(key)) _movement[key] = Math.Max(0f, points);
            Changed?.Invoke();
        }

        static string KeyFor(HoMM5.Data.MapObjectDef def, int fallbackIndex)
        {
            if (def != null && !string.IsNullOrEmpty(def.objectId)) return def.objectId;
            // Older imports did not include editor IDs. Position and hero name
            // provide a stable-enough fallback while that map remains loaded.
            return (def != null ? def.owner + "|" + def.hero + "|" + def.floor + "|" +
                    def.x + "|" + def.y : "hero") + "|" + fallbackIndex;
        }
    }
}
