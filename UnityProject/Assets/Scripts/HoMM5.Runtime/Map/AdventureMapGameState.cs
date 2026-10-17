using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>How often a visit effect can fire.</summary>
    public enum VisitFrequency
    {
        OnceEver,
        OncePerDay,
        OncePerWeek,
    }

    /// <summary>
    /// The adventure-map turn loop and the world state that changes as heroes
    /// walk around: day counter, resources, per-hero state, which treasures are
    /// gone, who owns which mine, which visit effects have fired.
    ///
    /// Movement cost is still the prototype grid conversion; original terrain
    /// multipliers and army-based movement remain engine features.
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
        readonly HashSet<string> _visitedOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _visitedDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _visitedWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _ownerOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, H5HeroState> _heroStates = new Dictionary<string, H5HeroState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector2> _heroPositions = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        HoMM5.Data.MapDef _map;
        HoMM5.Data.GameRulesDef _rules;
        List<HoMM5.Data.TownBuildingDef> _townBuildingDefs;

        /// <summary>The imported data tables; set before BindMap.</summary>
        public HoMM5.Data.GameDatabase Database { get; set; }
        public HoMM5.Data.GameRulesDef Rules => _rules;

        /// <summary>Deterministic die for treasure rolls; reseeded per scenario.</summary>
        public System.Random Rand { get; private set; } = new System.Random(12345);

        /// <summary>The live map's object definitions (per floor, as imported).</summary>
        public List<HoMM5.Data.MapObjectDef> MapObjects => _map != null ? _map.objects : null;

        public HoMM5.Data.PlayerResourceInventory Resources { get; private set; } =
            new HoMM5.Data.PlayerResourceInventory();
        public int Day { get; private set; } = 1;
        public int Week => ((Day - 1) / 7) % 4 + 1;
        public int Month => (Day - 1) / 28 + 1;
        public string MapId { get; private set; } = "";
        public string LocalPlayerId { get; private set; } = "PLAYER_1";

        /// <summary>Message log, newest last. HUD shows the tail.</summary>
        public readonly List<string> Log = new List<string>();
        public event Action<string> MessageLogged;

        /// <summary>Modal choices (chest, arena, tree of knowledge).</summary>
        public event Action<H5DialogRequest> DialogRequested;

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

        // -- binding -------------------------------------------------------

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
                _visitedOnce.Clear();
                _visitedDay.Clear();
                _visitedWeek.Clear();
                _ownerOverrides.Clear();
                _heroStates.Clear();
                _heroPositions.Clear();
                Log.Clear();
                Rand = new System.Random(12345);
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
                    var obj = objects[i];
                    if (obj == null || obj.Def == null) continue;
                    var objectId = obj.Def.objectId;

                    // Ownership and hero positions change over the game and
                    // have to survive a map rebuild (floor switch, load).
                    if (!string.IsNullOrEmpty(objectId))
                    {
                        if (_ownerOverrides.TryGetValue(objectId, out var owner))
                            obj.Def.owner = owner;
                        if (_heroPositions.TryGetValue(objectId, out var pos))
                        {
                            obj.Def.x = pos.x;
                            obj.Def.y = pos.y;
                            obj.transform.position = builder.TileToWorld(pos.x, pos.y);
                        }
                    }

                    if (!string.IsNullOrEmpty(objectId) && _collectedObjects.Contains(objectId))
                    {
                        obj.gameObject.SetActive(false);
                        continue;
                    }
                    if (!obj.IsHero) continue;
                    var key = KeyFor(obj.Def, i);
                    var isLocalHero = string.Equals(obj.Def.owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase);
                    if (isLocalHero) _heroes.Add(obj);
                    obj.BindMovement(isLocalHero ? this : null, key,
                                     isLocalHero ? GetMovement(key) : 0f);
                    if (isLocalHero && _heroStates.TryGetValue(key, out var heroState))
                        obj.BindHeroState(heroState);
                }
            }
            Changed?.Invoke();
        }

        // -- turn loop -----------------------------------------------------

        public void EndDay()
        {
            Day++;
            PayDailyIncome();
            _movement.Clear();
            _visitedDay.Clear();
            if ((Day - 1) % 7 == 0) _visitedWeek.Clear();
            for (var i = 0; i < _heroes.Count; i++)
            {
                if (_heroes[i] == null) continue;
                _heroes[i].SetMovementPoints(DailyHeroMovement);
                var hs = _heroStates.TryGetValue(_heroes[i].StateKey, out var s) ? s : null;
                hs?.OnNewDay();
            }
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

        // -- visits --------------------------------------------------------

        /// <summary>Called as a hero steps onto a tile; runs any visit triggers there.</summary>
        internal void HeroEnteredTile(H5MapObject hero, UnityEngine.Vector2Int tile)
        {
            if (hero != null && hero.Def != null && hero.IsLocalHero)
                _heroPositions[hero.StateKey] = new Vector2(hero.Def.x, hero.Def.y);
            H5ObjectVisits.TryVisitAtTile(this, hero, tile);
            Changed?.Invoke();
        }

        /// <summary>Per-hero persistent state (stats, spells, artifacts).</summary>
        public H5HeroState GetHeroState(H5MapObject hero)
        {
            if (hero == null || hero.Def == null) return null;
            var key = hero.StateKey;
            if (string.IsNullOrEmpty(key)) return null;
            if (_heroStates.TryGetValue(key, out var state) && state != null) return state;

            var hs = new H5HeroState { Id = key, HeroName = hero.Def.hero ?? "Hero" };
            // Per-hero starting stats are not in the imported tables yet (the
            // shipped per-hero records live outside GameMechanics/Creature);
            // hero names and starting spells still resolve where possible.
            var def = Database != null ? Database.GetCreature(hero.Def.hero) : null;
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.DisplayName)) hs.HeroName = def.DisplayName;
                if (def.AttackSkill > 0) hs.Attack = def.AttackSkill;
                if (def.DefenceSkill > 0) hs.Defence = def.DefenceSkill;
                if (def.knownSpells != null)
                    foreach (var s in def.knownSpells)
                        if (s != null && !string.IsNullOrEmpty(s.spell)) hs.Spells.Add(s.spell);
            }
            hs.Mana = hs.MaxMana;
            _heroStates[key] = hs;
            hero.BindHeroState(hs);
            return hs;
        }

        /// <summary>
        /// Gate a visit effect by frequency. Returns false when it already
        /// fired (today, this week, or ever).
        /// </summary>
        public bool TryBeginVisit(HoMM5.Data.MapObjectDef obj, VisitFrequency frequency)
        {
            if (obj == null) return true;
            var id = VisitKey(obj);
            switch (frequency)
            {
                case VisitFrequency.OnceEver:
                    if (_visitedOnce.Contains(id)) return false;
                    _visitedOnce.Add(id);
                    return true;
                case VisitFrequency.OncePerDay:
                    if (_visitedDay.TryGetValue(id, out var day) && day == Day) return false;
                    _visitedDay[id] = Day;
                    return true;
                default:
                    if (_visitedWeek.TryGetValue(id, out var week) && week == Week) return false;
                    _visitedWeek[id] = Week;
                    return true;
            }
        }

        /// <summary>Log a "not implemented" notice at most once per day per object.</summary>
        public void LogOncePerDay(HoMM5.Data.MapObjectDef obj, string message)
        {
            if (!TryBeginVisit(obj, VisitFrequency.OncePerDay)) return;
            AddLog(message);
        }

        static string VisitKey(HoMM5.Data.MapObjectDef obj)
        {
            return string.IsNullOrEmpty(obj.objectId)
                ? obj.kind + "|" + obj.sharedType + "|" + obj.floor + "|" + obj.x + "|" + obj.y
                : obj.objectId;
        }

        // -- world mutations ----------------------------------------------

        /// <summary>Whether this object has already been picked up/consumed.</summary>
        public bool IsCollected(HoMM5.Data.MapObjectDef obj)
        {
            return obj != null && !string.IsNullOrEmpty(obj.objectId) &&
                   _collectedObjects.Contains(obj.objectId);
        }

        /// <summary>Pick an object up: mark it gone and hide its scene object.</summary>
        public void CollectObject(H5MapObject mapObj)
        {
            if (mapObj == null || mapObj.Def == null) return;
            if (!string.IsNullOrEmpty(mapObj.Def.objectId))
                _collectedObjects.Add(mapObj.Def.objectId);
            mapObj.gameObject.SetActive(false);
        }

        /// <summary>Flag an object (mine) for the local player.</summary>
        public void CaptureObject(H5MapObject mapObj)
        {
            if (mapObj == null || mapObj.Def == null) return;
            mapObj.Def.owner = LocalPlayerId;
            if (!string.IsNullOrEmpty(mapObj.Def.objectId))
                _ownerOverrides[mapObj.Def.objectId] = LocalPlayerId;
        }

        public void AddLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Log.Add(message);
            if (Log.Count > 200) Log.RemoveRange(0, Log.Count - 200);
            MessageLogged?.Invoke(message);
            Changed?.Invoke();
        }

        /// <summary>Ask the HUD for a modal choice; default to option 1 headless.</summary>
        public void RequestDialog(H5DialogRequest request)
        {
            if (request == null || request.Options.Count == 0) return;
            if (DialogRequested != null) DialogRequested.Invoke(request);
            else request.Options[0].Action?.Invoke();
            Changed?.Invoke();
        }

        // -- income --------------------------------------------------------

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

        // -- movement ------------------------------------------------------

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

        // -- save / load ---------------------------------------------------

        /// <summary>Snapshot the mutable world state into a DTO.</summary>
        public H5SaveGame CaptureState()
        {
            var save = new H5SaveGame
            {
                mapId = MapId,
                day = Day,
                localPlayerId = LocalPlayerId,
                resources = Resources,
            };
            foreach (var kv in _movement) save.movement[kv.Key] = kv.Value;
            foreach (var kv in _heroStates) save.heroes.Add(kv.Value);
            foreach (var kv in _heroPositions)
                save.heroPositions.Add(new H5SaveGame.PositionEntry
                {
                    id = kv.Key, x = kv.Value.x, y = kv.Value.y
                });
            save.collected.AddRange(_collectedObjects);
            save.visitedOnce.AddRange(_visitedOnce);
            foreach (var kv in _visitedDay) save.visitedDay[kv.Key] = kv.Value;
            foreach (var kv in _visitedWeek) save.visitedWeek[kv.Key] = kv.Value;
            foreach (var kv in _ownerOverrides) save.owners[kv.Key] = kv.Value;
            save.log.AddRange(Log);
            return save;
        }

        /// <summary>
        /// Restore a snapshot. Call before BindMap(..., newScenario: false) so
        /// the rebuild applies ownership and hero positions.
        /// </summary>
        public void RestoreState(H5SaveGame save)
        {
            if (save == null) return;
            Day = Math.Max(1, save.day);
            LocalPlayerId = string.IsNullOrEmpty(save.localPlayerId) ? "PLAYER_1" : save.localPlayerId;
            if (save.resources != null) Resources = save.resources;
            _movement.Clear();
            if (save.movement != null)
                foreach (var kv in save.movement) _movement[kv.Key] = kv.Value;
            _heroStates.Clear();
            if (save.heroes != null)
                foreach (var h in save.heroes)
                    if (h != null && !string.IsNullOrEmpty(h.Id)) _heroStates[h.Id] = h;
            _heroPositions.Clear();
            if (save.heroPositions != null)
                foreach (var p in save.heroPositions)
                    if (p != null && !string.IsNullOrEmpty(p.id))
                        _heroPositions[p.id] = new Vector2(p.x, p.y);
            _collectedObjects.Clear();
            if (save.collected != null)
                foreach (var id in save.collected) _collectedObjects.Add(id);
            _visitedOnce.Clear();
            if (save.visitedOnce != null)
                foreach (var id in save.visitedOnce) _visitedOnce.Add(id);
            _visitedDay.Clear();
            if (save.visitedDay != null)
                foreach (var kv in save.visitedDay) _visitedDay[kv.Key] = kv.Value;
            _visitedWeek.Clear();
            if (save.visitedWeek != null)
                foreach (var kv in save.visitedWeek) _visitedWeek[kv.Key] = kv.Value;
            _ownerOverrides.Clear();
            if (save.owners != null)
                foreach (var kv in save.owners) _ownerOverrides[kv.Key] = kv.Value;
            Log.Clear();
            if (save.log != null) Log.AddRange(save.log);
            Changed?.Invoke();
        }
    }
}
