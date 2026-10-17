using System;
using System.Collections.Generic;
using HoMM5.Data;
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
        /// <summary>Named roster heroes placed on a map hero, keyed by its state key.</summary>
        readonly Dictionary<string, HeroDef> _rosterHeroes = new Dictionary<string, HeroDef>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<MapObjectDef, H5MapObject> _views = new Dictionary<MapObjectDef, H5MapObject>();

        /// <summary>The scene object for a map definition, or null when it has no mesh.</summary>
        public H5MapObject ViewOf(MapObjectDef def)
        {
            return def != null && _views.TryGetValue(def, out var view) ? view : null;
        }

        readonly Dictionary<string, H5TownState> _townStates = new Dictionary<string, H5TownState>(StringComparer.OrdinalIgnoreCase);
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

        /// <summary>
        /// A hero stepped onto a monster's activation tile. The bootstrap turns
        /// this into a battle scene; without a listener the fight is resolved
        /// headless by the battle AI.
        /// </summary>
        public event Action<BattleContext> CombatRequested;

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
                _rosterHeroes.Clear();
                _heroPositions.Clear();
                _townStates.Clear();
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
            BindTowns();
            if (builder != null && builder.Root != null)
            {
                var objects = builder.Root.GetComponentsInChildren<H5MapObject>(true);
                _views.Clear();
                for (var i = 0; i < objects.Length; i++)
                {
                    var obj = objects[i];
                    if (obj == null || obj.Def == null) continue;
                    _views[obj.Def] = obj;
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

                    // A town reports its live state (faction, fort tier, what the
                    // dwellings have bred) instead of only what the map authored.
                    if (obj.IsTown) obj.Town = GetTownState(obj.Def);

                    if (!string.IsNullOrEmpty(objectId) && _collectedObjects.Contains(objectId))
                    {
                        obj.gameObject.SetActive(false);
                        continue;
                    }
                    if (!obj.IsHero) continue;
                    var key = KeyFor(obj.Def, i);
                    // Every hero is on the roster, the computer's included: they
                    // move, fight, earn and count towards the match. Only the
                    // local player's are interactive (IsLocalHero).
                    var isLocalHero = string.Equals(obj.Def.owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase);
                    _heroes.Add(obj);
                    obj.BindMovement(this, key, GetMovement(key), isLocalHero);
                    if (_heroStates.TryGetValue(key, out var heroState))
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
            NewDayForTowns();
            _movement.Clear();
            _visitedDay.Clear();
            if ((Day - 1) % 7 == 0)
            {
                _visitedWeek.Clear();
                GrowTowns();
            }
            for (var i = 0; i < _heroes.Count; i++)
            {
                if (_heroes[i] == null) continue;
                var hs = _heroStates.TryGetValue(_heroes[i].StateKey, out var s) ? s : null;
                if (hs != null)
                {
                    // Logistics multiplies the day's movement budget.
                    _heroes[i].SetMovementPoints(DailyHeroMovement * hs.Effects.MovementMultiplier);
                    // Estates pays the treasury; mana returns 10% of the cap
                    // per day (inferred -- the regen rate is engine-side),
                    // doubled by Mysticism.
                    if (hs.Effects.EstatesGoldPerDay > 0)
                    {
                        TreasuryOf(_heroes[i]).gold += hs.Effects.EstatesGoldPerDay;
                        AddLog($"{hs.HeroName}: Estates add {hs.Effects.EstatesGoldPerDay} gold.");
                    }
                    var regen = hs.MaxMana * 0.1f * hs.Effects.ManaRegenMultiplier;
                    if (regen > 0f) hs.Mana = Math.Min(hs.MaxMana, hs.Mana + regen);
                    hs.OnNewDay();
                }
                else
                {
                    _heroes[i].SetMovementPoints(DailyHeroMovement);
                }
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
                Resources.Set(DefaultStartResources());
            }
        }

        // -- visits --------------------------------------------------------

        /// <summary>Called as a hero steps onto a tile; runs any visit triggers there.</summary>
        internal void HeroEnteredTile(H5MapObject hero, UnityEngine.Vector2Int tile)
        {
            // Every hero's position is kept, the computer's included, so a save
            // restores where they were.
            if (hero != null && hero.Def != null)
                _heroPositions[hero.StateKey] = new Vector2(hero.Def.x, hero.Def.y);
            H5ObjectVisits.TryVisitAtTile(this, hero, tile);
            Changed?.Invoke();
        }

        /// <summary>First hero of the local player, for HUD fallbacks.</summary>
        public H5HeroState FirstLocalHeroState()
        {
            for (var i = 0; i < _heroes.Count; i++)
            {
                if (_heroes[i] == null || !_heroes[i].IsLocalHero) continue;
                var hs = GetHeroState(_heroes[i]);
                if (hs != null) return hs;
            }
            return null;
        }

        /// <summary>Per-hero persistent state (stats, spells, artifacts).</summary>
        public H5HeroState GetHeroState(H5MapObject hero)
        {
            if (hero == null || hero.Def == null) return null;
            var key = hero.StateKey;
            if (string.IsNullOrEmpty(key)) return null;
            if (_heroStates.TryGetValue(key, out var state) && state != null) return state;

            _rosterHeroes.TryGetValue(key, out var roster);
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
                hs.HeroClass = ClassForTown(def.town);
            }
            if (roster != null) ApplyRosterHero(hs, roster);
            else ApplyAuthoredHistory(hs, hero);
            hs.RecalculateEffects(Database);
            hs.Mana = hs.MaxMana;
            hs.EnsureArmy(hero.Def != null ? hero.Def.army : null);
            if (hs.Spells.Count == 0)
            {
                // Town guilds and hero classes do not feed spellbooks yet;
                // until they do every hero carries a modest starter grimoire
                // so spellcasting is playable. Replaced when the town screen
                // lands.
                foreach (var starter in new[]
                {
                    "Magic_Arrow", "Ice_Bolt", "Lightning_Bolt", "Fireball",
                    "Haste", "Slow", "Bless", "Curse", "StoneSkin", "Weakness",
                    "Blind", "Resurrect",
                })
                    hs.LearnSpell(starter);
            }
            _heroStates[key] = hs;
            hero.BindHeroState(hs);
            return hs;
        }

        /// <summary>
        /// The level-up history a map authored for its hero (Editable block of
        /// AdvMapHero): primary stats, experience, skills, perks, spells.
        /// </summary>
        void ApplyAuthoredHistory(H5HeroState hs, H5MapObject hero)
        {
            // Map-authored level-up history (Editable block of AdvMapHero).
            var mapDef = hero.Def;
            if (mapDef.primarySkills != null)
            {
                if (mapDef.primarySkills.offence > 0) hs.Attack = mapDef.primarySkills.offence;
                if (mapDef.primarySkills.defence > 0) hs.Defence = mapDef.primarySkills.defence;
                if (mapDef.primarySkills.spellpower > 0) hs.SpellPower = mapDef.primarySkills.spellpower;
                if (mapDef.primarySkills.knowledge > 0) hs.Knowledge = mapDef.primarySkills.knowledge;
            }
            if (mapDef.experience > 0)
            {
                hs.Experience = mapDef.experience;
                while (hs.Level < 100 && hs.Experience >= H5HeroState.ExperienceForLevel(hs.Level + 1))
                    hs.Level++;
            }
            if (mapDef.skills != null)
                foreach (var s in mapDef.skills)
                    if (s != null && !string.IsNullOrEmpty(s.skill))
                        hs.LearnSkill(s.skill, s.mastery > 0 ? s.mastery : 1);
            if (mapDef.perks != null)
                foreach (var p in mapDef.perks)
                    hs.LearnPerk(p);
            if (mapDef.spells != null)
                foreach (var s in mapDef.spells)
                    if (!string.IsNullOrEmpty(s)) hs.LearnSpell(s);
            if (hs.Skills.Count == 0)
            {
                // Hero records that predate skill authoring start with their
                // class skill at Basic and its first class perk -- inferred
                // from the shipped hero presets (DuelMode/Heroes).
                var racial = hs.RacialSkill;
                if (racial != null)
                {
                    hs.LearnSkill(racial, 1);
                    hs.LearnPerk(hs.PickPerkFor(racial, Database, Rand));
                }
            }
        }

        /// <summary>
        /// Sets a named roster hero's identity, primary stats, skills, perks and
        /// spells on a hero state. Replaces the authored history: the chosen
        /// hero starts at level 1 with exactly the roster's learned abilities.
        /// </summary>
        void ApplyRosterHero(H5HeroState hs, HeroDef roster)
        {
            hs.HeroName = roster.DisplayName;
            if (!string.IsNullOrEmpty(roster.heroClass)) hs.HeroClass = roster.heroClass;
            hs.Specialty = roster.specializationName ?? "";
            if (roster.stats != null)
            {
                hs.Attack = roster.stats.offence;
                hs.Defence = roster.stats.defence;
                hs.SpellPower = roster.stats.spellpower;
                hs.Knowledge = roster.stats.knowledge;
            }
            hs.Level = 1;
            hs.Experience = 0;
            hs.PendingLevelUps = 0;
            hs.Skills.Clear();
            hs.Perks.Clear();
            hs.Spells.Clear();
            if (roster.skills != null)
                foreach (var s in roster.skills)
                    if (s != null && !string.IsNullOrEmpty(s.skill))
                        hs.LearnSkill(s.skill, s.mastery > 0 ? s.mastery : 1);
            if (roster.perks != null)
                foreach (var p in roster.perks) hs.LearnPerk(p);
            if (roster.spells != null)
                foreach (var s in roster.spells) hs.LearnSpell(s);
            if (hs.Skills.Count == 0)
            {
                var racial = hs.RacialSkill;
                if (racial != null)
                {
                    hs.LearnSkill(racial, 1);
                    hs.LearnPerk(hs.PickPerkFor(racial, Database, Rand));
                }
            }
        }

        /// <summary>HERO_CLASS_* for a town id (hero classes are per faction).</summary>
        public static string ClassForTown(string town)
        {
            switch ((town ?? "").ToUpperInvariant())
            {
                case "TOWN_HEAVEN": return "HERO_CLASS_KNIGHT";
                case "TOWN_INFERNO": return "HERO_CLASS_DEMON_LORD";
                case "TOWN_NECROMANCY": case "TOWN_NECROPOLIS": return "HERO_CLASS_NECROMANCER";
                case "TOWN_PRESERVE": case "TOWN_SYLVAN": return "HERO_CLASS_RANGER";
                case "TOWN_ACADEMY": return "HERO_CLASS_WIZARD";
                case "TOWN_DUNGEON": return "HERO_CLASS_WARLOCK";
                case "TOWN_FORTRESS": return "HERO_CLASS_RUNEMAGE";
                case "TOWN_STRONGHOLD": return "HERO_CLASS_BARBARIAN";
                default: return "HERO_CLASS_KNIGHT";
            }
        }

        /// <summary>
        /// Show one level-up dialog per earned but unspent level. Each offer's
        /// action applies the choice and re-enters, so stacked level-ups walk
        /// one dialog at a time (and drain inline when no HUD is listening).
        /// </summary>
        public void CheckLevelUps(H5HeroState hero)
        {
            if (hero == null || hero.PendingLevelUps <= 0) return;
            var offers = hero.RollLevelUpOffers(Database, Rand);
            if (offers.Count == 0)
            {
                hero.PendingLevelUps = 0;
                return;
            }
            var request = new H5DialogRequest
            {
                Message = $"{hero.HeroName} reaches level {hero.Level}. Choose a skill:",
            };
            foreach (var offer in offers)
            {
                var chosen = offer;
                request.Options.Add(new H5DialogOption
                {
                    Label = offer.Summary(Database),
                    Action = () =>
                    {
                        hero.ApplyLevelUp(chosen, Database, Rand);
                        AddLog($"{hero.HeroName}: {chosen.Summary(Database)}");
                        CheckLevelUps(hero);
                    },
                });
            }
            RequestDialog(request);
        }

        // -- combat --------------------------------------------------------

        /// <summary>Everything one battle needs beyond the two stacks.</summary>
        public class BattleContext
        {
            public H5MapObject Hero;
            public H5MapObject Monster;
            /// <summary>Guard stacks when they come from a table, not the map (banks).</summary>
            public List<HoMM5.Combat.ArmyStackState> GuardOverride;
            /// <summary>0-3 fort level for sieges (Fort/Citadel/Castle).</summary>
            public int FortLevel;
            /// <summary>True when walls, towers and a catapult come into play.</summary>
            public bool IsSiege;
        }

        /// <summary>
        /// Start a battle between a local hero and a map monster. Halts the
        /// hero first: the fight pauses the adventure map. Garrison stacks on
        /// the object become the defenders; towns and garrison posts fight
        /// behind their walls.
        /// </summary>
        internal void RequestCombat(H5MapObject hero, H5MapObject monster)
        {
            RequestCombat(hero, monster, null);
        }

        internal void RequestCombat(H5MapObject hero, H5MapObject monster,
                                    List<HoMM5.Combat.ArmyStackState> guardOverride)
        {
            if (hero == null) return;
            if (monster == null)
            {
                // The object has no scene view (no mesh), so there is nothing to
                // fight through. Say so instead of ending the turn silently.
                AddLog("That creature cannot be engaged on this map.");
                return;
            }
            hero.HaltMovement();

            var context = new BattleContext
            {
                Hero = hero,
                Monster = monster,
                GuardOverride = guardOverride,
                IsSiege = IsSiegeFight(monster.Def),
                FortLevel = FortLevelOf(monster.Def),
            };

            // The player's own fights go to the combat scene. The computer's
            // are resolved here, as its turn happens, with the battle AI.
            if (CombatRequested != null && hero.IsLocalHero)
            {
                CombatRequested.Invoke(context);
                return;
            }
            RunHeadlessBattle(context);
        }

        void RunHeadlessBattle(BattleContext context)
        {
            GetHeroState(context.Hero);
            var battle = HoMM5.Combat.CombatGameState.Create(
                context.Hero, context.Monster, Database, Rand, context.GuardOverride);
            if (battle.Units.Find(u => u.Side == HoMM5.Combat.CombatSide.Attacker) == null)
            {
                AddLog("There is no army to attack with.");
                return;
            }
            if (context.IsSiege) battle.SetupSiege(context.FortLevel);
            AddLog("Battle begins!");
            var result = battle.RunQuickCombat();
            ApplyCombatResult(context.Hero, context.Monster, result);
        }

        /// <summary>Defenders fight behind walls.</summary>
        public static bool IsSiegeFight(HoMM5.Data.MapObjectDef def)
        {
            if (def == null) return false;
            return def.kind == "AdvMapTown" || def.kind == "AdvMapGarrison" ||
                   (def.garrison != null && def.garrison.stacks != null && def.garrison.stacks.Count > 0 &&
                    def.kind != "AdvMapDwelling");
        }

        /// <summary>Town fortification tier from its Fort building (0-3).</summary>
        public int FortLevelOf(HoMM5.Data.MapObjectDef def)
        {
            // The town screen raises Fort/Citadel/Castle during play, so the live
            // state wins over what the map authored.
            var town = GetTownState(def);
            if (town != null && town.FortTier > 0) return town.SiegeFortLevel;
            if (def == null || def.buildings == null || _townBuildingDefs == null) return 0;
            var level = 0;
            foreach (var buildingState in def.buildings)
            {
                if (buildingState == null) continue;
                foreach (var building in _townBuildingDefs)
                {
                    if (building == null || building.type != "TB_FORT") continue;
                    if (!string.Equals(building.upgrade, buildingState.upgrade,
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    var tier = building.upgrade == "BLD_UPG_2" ? 2
                             : building.upgrade == "BLD_UPG_3" ? 3 : 1;
                    if (tier > level) level = tier;
                }
            }
            return level;
        }

        /// <summary>
        /// Fold a finished battle back into the adventure map: army
        /// casualties, experience, the defeated monster (or the defeated hero).
        /// </summary>
        public void ApplyCombatResult(H5MapObject heroObj, H5MapObject monsterObj,
                                      HoMM5.Combat.CombatResult result)
        {
            if (result == null) return;
            var heroState = heroObj != null ? GetHeroState(heroObj) : null;

            switch (result.Outcome)
            {
                case HoMM5.Combat.CombatOutcome.AttackerWon:
                    if (heroState != null)
                    {
                        heroState.SetArmy(result.AttackerSurvivors);
                        WriteArmyToDef(heroObj.Def, result.AttackerSurvivors);
                    }
                    if (monsterObj != null)
                    {
                        // Dwellings and mines change hands; towns and banks
                        // are cleared out; everything else is picked up.
                        if (monsterObj.Def != null &&
                            (monsterObj.Def.kind == "AdvMapDwelling" || 
                             monsterObj.Def.kind == "AdvMapTown" || 
                             monsterObj.Def.kind == "AdvMapGarrison"))
                            CaptureObject(monsterObj);
                        if (monsterObj.Def != null && IsBank(monsterObj.Def))
                            PayBankReward(monsterObj.Def, heroState, TreasuryOf(heroObj));
                        CollectObject(monsterObj);
                    }
                    AddLog(result.Experience > 0
                        ? $"Victory! The army earns {result.Experience} experience."
                        : "Victory! The enemy stack is destroyed.");
                    if (heroState != null)
                    {
                        RaiseTheDead(heroState, monsterObj, result);
                        CheckLevelUps(heroState);
                    }
                    break;

                case HoMM5.Combat.CombatOutcome.Fled:
                    if (heroState != null)
                    {
                        heroState.SetArmy(result.AttackerSurvivors);
                        WriteArmyToDef(heroObj.Def, result.AttackerSurvivors);
                    }
                    WriteMonsterSurvivors(monsterObj, result.DefenderSurvivors);
                    AddLog("The army retreats; the enemy keeps its wounds.");
                    break;

                default:
                    // The army is gone and so is the hero.
                    AddLog("The hero's army is destroyed in battle.");
                    if (heroObj != null)
                    {
                        heroObj.gameObject.SetActive(false);
                        _heroes.Remove(heroObj);
                    }
                    break;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Necromancy after a victory: a share of the fallen rises as undead
        /// (RaisePercentBase + per-rank, Lord of Undead bonus), transformed
        /// through the shipped TransformTable (peasants -> skeletons, titans ->
        /// wraiths, ...). Creatures with no table row rise as Skeletons.
        /// </summary>
        void RaiseTheDead(H5HeroState hero, H5MapObject monsterObj, HoMM5.Combat.CombatResult result)
        {
            if (hero == null || result == null) return;
            var percent = hero.Effects.NecromancyRaisePercent;
            if (percent <= 0f || result.CreaturesKilledByAttacker <= 0) return;

            var risen = (int)(result.CreaturesKilledByAttacker * percent);
            if (risen <= 0) return;

            var sourceId = monsterObj != null && monsterObj.Def != null ? monsterObj.Def.creature : null;
            var risenId = "CREATURE_SKELETON";
            if (_rules != null && _rules.necromancyTransform != null && !string.IsNullOrEmpty(sourceId) &&
                _rules.necromancyTransform.TryGetValue(sourceId, out var mapped))
                risenId = mapped;

            HoMM5.Combat.ArmyStackState stack = null;
            foreach (var s in hero.Army)
                if (s != null && s.CreatureId == risenId) { stack = s; break; }
            if (stack != null) stack.Count += risen;
            else hero.Army.Add(new HoMM5.Combat.ArmyStackState(risenId, risen));
            if (monsterObj != null) WriteArmyToDef(heroArmedDef(hero), hero.Army);
            AddLog($"Necromancy raises {risen} {HoMM5.Data.GameDatabase.PrettifyEnum(risenId)} from the fallen.");
        }

        /// <summary>The map def the hero's army mirrors, if any is bound.</summary>
        HoMM5.Data.MapObjectDef heroArmedDef(H5HeroState hero)
        {
            if (hero == null) return null;
            for (var i = 0; i < _heroes.Count; i++)
                if (_heroes[i] != null && _heroes[i].StateKey == hero.Id) return _heroes[i].Def;
            return null;
        }

        static void WriteArmyToDef(HoMM5.Data.MapObjectDef def, List<HoMM5.Combat.ArmyStackState> survivors)
        {
            if (def == null) return;
            def.army = new List<HoMM5.Data.MapArmyStackDef>();
            if (survivors == null) return;
            foreach (var s in survivors)
            {
                if (s == null || s.Count <= 0 || string.IsNullOrEmpty(s.CreatureId)) continue;
                def.army.Add(new HoMM5.Data.MapArmyStackDef
                {
                    creature = s.CreatureId,
                    count = s.Count,
                    amount = s.Count,
                    customAmount = true,
                });
            }
        }

        /// <summary>
        /// Write a wounded monster's surviving stacks back to the map object,
        /// so a rematch fights the remainder.
        /// </summary>
        static void WriteMonsterSurvivors(H5MapObject monsterObj, List<HoMM5.Combat.ArmyStackState> survivors)
        {
            if (monsterObj == null || monsterObj.Def == null || survivors == null) return;
            var def = monsterObj.Def;
            var main = survivors.Count > 0 ? survivors[0] : null;
            if (main == null || main.Count <= 0) return;
            def.creature = main.CreatureId;
            def.amount = main.Count;
            def.amount2 = main.Count;
            def.customAmount = true;
            def.additionalStacks = new List<HoMM5.Data.MapArmyStackDef>();
            for (var i = 1; i < survivors.Count; i++)
            {
                var s = survivors[i];
                if (s == null || s.Count <= 0) continue;
                def.additionalStacks.Add(new HoMM5.Data.MapArmyStackDef
                {
                    creature = s.CreatureId,
                    amount = s.Count,
                    count = s.Count,
                    customAmount = true,
                });
            }
        }

        // -- banks ---------------------------------------------------------

        /// <summary>Guarded treasure sites whose prize is claimed after battle.</summary>
        public static bool IsBank(HoMM5.Data.MapObjectDef def)
        {
            var t = (def.sharedType ?? "").ToUpperInvariant();
            return t == "BUILDING_CRYPT" || t == "BUILDING_FORGOTTEN_CRYPT" ||
                   t == "BUILDING_GRAVEYARD" || t == "BUILDING_DRAGON_UTOPIA" ||
                   t == "BUILDING_NAGA_BANK" || t == "BUILDING_PYRAMID" ||
                   t == "BUILDING_SUNKEN_TEMPLE" || t == "BUILDING_TOMB_OF_THE_WARRIOR" ||
                   t == "TREASURE_SOULSTONE" || t == "TREASURE_SHIPWRECK";
        }

        /// <summary>
        /// Payout of a cleared bank. The shipped records carry the guards'
        /// existence but not the prize tables (those are scenario-scripted in
        /// the original), so these are the classic HoMM5 payouts, inferred and
        /// marked here.
        /// </summary>
        void PayBankReward(HoMM5.Data.MapObjectDef def, H5HeroState heroState, HoMM5.Data.PlayerResourceInventory treasury)
        {
            var t = (def.sharedType ?? "").ToUpperInvariant();
            switch (t)
            {
                case "BUILDING_CRYPT":
                    PayLoot(2000 + Rand.Next(0, 1001), "the Crypt", artifactChance: 0.3f, heroState, treasury);
                    break;
                case "BUILDING_FORGOTTEN_CRYPT":
                    PayLoot(3000 + Rand.Next(0, 1001), "the Forgotten Crypt", artifactChance: 0.3f, heroState, treasury);
                    break;
                case "BUILDING_GRAVEYARD":
                    PayLoot(1500 + Rand.Next(0, 1001), "the Graveyard", artifactChance: 0f, heroState, treasury);
                    break;
                case "BUILDING_DRAGON_UTOPIA":
                    PayLoot(20000 + Rand.Next(0, 10001), "Dragon Utopia", artifactChance: 1f, heroState, treasury);
                    break;
                case "BUILDING_NAGA_BANK":
                    PayLoot(5000, "the Naga Bank", artifactChance: 0f, heroState, treasury);
                    treasury.Add("GEM", 10);
                    AddLog("The Naga Bank also held 10 gems.");
                    break;
                case "BUILDING_PYRAMID":
                    PayLoot(4000, "the Pyramid", artifactChance: 0.5f, heroState, treasury);
                    heroState?.AddExperience(2000);
                    break;
                case "BUILDING_SUNKEN_TEMPLE":
                case "BUILDING_TOMB_OF_THE_WARRIOR":
                    PayLoot(1500 + Rand.Next(0, 1001), "the ruins", artifactChance: 0.3f, heroState, treasury);
                    heroState?.AddExperience(1500);
                    break;
                case "TREASURE_SOULSTONE":
                case "TREASURE_SHIPWRECK":
                    PayLoot(1500 + Rand.Next(0, 1001), "the wreck", artifactChance: 0.2f, heroState, treasury);
                    break;
            }
        }

        void PayLoot(int gold, string place, float artifactChance, H5HeroState heroState, HoMM5.Data.PlayerResourceInventory treasury)
        {
            treasury.gold += gold;
            AddLog($"{place} yields {gold} gold.");
            if (artifactChance <= 0f || Database == null || heroState == null) return;
            if (Rand.NextDouble() > artifactChance) return;
            // A random minor-class artifact from the shipped table.
            HoMM5.Data.ArtifactDef prize = null;
            foreach (var kv in Database.Artifacts)
            {
                var a = kv.Value;
                if (a == null || a.artifactClass != "ARTF_CLASS_MINOR") continue;
                if (Rand.Next(4) == 0) { prize = a; break; }
                prize = prize ?? a;
            }
            if (prize == null) return;
            if (heroState.WearArtifact(prize.id))
            {
                heroState.ApplyArtifactStats(prize);
                AddLog($"Among the spoils: the {prize.DisplayName}!");
            }
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
            if (mapObj == null) return;
            CollectObject(mapObj.Def);
            mapObj.gameObject.SetActive(false);
        }

        /// <summary>Remove a map object from play (picked up, cleared, or a visit used up).</summary>
        public void CollectObject(MapObjectDef def)
        {
            if (def == null) return;
            if (!string.IsNullOrEmpty(def.objectId))
                _collectedObjects.Add(def.objectId);
            var view = ViewOf(def);
            if (view != null) view.gameObject.SetActive(false);
        }

        /// <summary>Flag an object (mine, town) for the local player.</summary>
        public void CaptureObject(H5MapObject mapObj)
        {
            if (mapObj == null) return;
            CaptureObject(mapObj.Def);
        }

        /// <summary>Hand a map object to the acting player.</summary>
        public void CaptureObject(MapObjectDef def)
        {
            if (def == null) return;
            var owner = ActingPlayerId;
            def.owner = owner;
            if (!string.IsNullOrEmpty(def.objectId))
                _ownerOverrides[def.objectId] = owner;

            // A captured town changes hands: its owner is the capturing player
            // now, and whatever the garrison had bred is gone with the defenders.
            var town = GetTownState(def);
            if (town != null)
            {
                town.Owner = owner;
                town.Pools.Clear();
                town.BuiltToday = true;      // it did not build for you today
            }
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
                if (obj == null || string.IsNullOrEmpty(obj.owner) ||
                    string.Equals(obj.owner, "PLAYER_NONE", StringComparison.OrdinalIgnoreCase)) continue;
                var treasury = TreasuryOfOwner(obj.owner);
                if (obj.kind == "AdvMapMine" && _rules != null && _rules.mineDailyIncome != null &&
                    !string.IsNullOrEmpty(obj.resource) &&
                    _rules.mineDailyIncome.TryGetValue(obj.resource.ToUpperInvariant(), out var mineIncome))
                    treasury.Add(obj.resource, mineIncome);
                else if (obj.kind == "AdvMapTown")
                    PayTownIncome(GetTownState(obj), treasury);
            }
        }

        /// <summary>
        /// One town's daily payment: its town hall tier's gold, the Grail's, and
        /// the faction's resource silo once the marketplace is upgraded. All three
        /// are shipped numbers (the hall and Grail amounts come out of their
        /// descriptions, the silo resource from TownTypesInfo).
        /// </summary>
        void PayTownIncome(H5TownState town, HoMM5.Data.PlayerResourceInventory treasury)
        {
            if (town == null || treasury == null) return;
            int gold;
            string siloResource;
            int siloAmount;
            town.PayDailyIncome(Database, treasury, out gold, out siloResource, out siloAmount);
            if (gold > 0 || siloAmount > 0)
                AddLog($"{town.Name} pays {gold} gold" +
                       (siloAmount > 0 ? $" and {siloAmount} {siloResource}" : "") + ".");
        }

        /// <summary>A new day lets every town build again.</summary>
        void NewDayForTowns()
        {
            foreach (var town in AllTowns()) town.BuiltToday = false;
        }

        /// <summary>A new week breeds creatures in every dwelling.</summary>
        void GrowTowns()
        {
            foreach (var town in AllTowns())
            {
                var before = PoolTotal(town);
                town.AddWeeklyGrowth(Database);
                var bred = PoolTotal(town) - before;
                if (bred > 0) AddLog($"{town.Name} breeds {bred} creatures.");
            }
        }

        static int PoolTotal(H5TownState town)
        {
            var total = 0;
            foreach (var kv in town.Pools) total += kv.Value;
            return total;
        }

        // -- towns ---------------------------------------------------------

        /// <summary>
        /// Build the town registry for the loaded map. Towns live on one floor
        /// but their state must survive a floor switch, so they are keyed by map
        /// object id and rebuilt from the map's object list rather than from the
        /// scene.
        /// </summary>
        void BindTowns()
        {
            if (_map == null || _map.objects == null) return;
            foreach (var def in _map.objects)
            {
                if (def == null || def.kind != "AdvMapTown") continue;
                var id = TownKey(def);
                if (string.IsNullOrEmpty(id)) continue;
                if (_townStates.TryGetValue(id, out var town) && town != null)
                    town.SyncFromMap(Database, def);
                else
                    _townStates[id] = H5TownState.FromMapObject(def, Database);
            }
        }

        static string TownKey(HoMM5.Data.MapObjectDef def)
        {
            if (def == null) return null;
            return !string.IsNullOrEmpty(def.objectId)
                ? def.objectId
                : "town|" + def.floor + "|" + def.x + "|" + def.y;
        }

        /// <summary>The live state of one placed town.</summary>
        public H5TownState GetTownState(HoMM5.Data.MapObjectDef def)
        {
            var id = TownKey(def);
            if (string.IsNullOrEmpty(id)) return null;
            return _townStates.TryGetValue(id, out var town) ? town : null;
        }

        public H5TownState GetTownState(H5MapObject obj)
        {
            return obj != null ? GetTownState(obj.Def) : null;
        }

        // -- players -------------------------------------------------------

        /// <summary>
        /// The scenario's player roster. Populated by <see cref="BindMap"/> from
        /// the map's authored owners, or by the new-game setup.
        /// </summary>
        public H5PlayerRoster Players { get; private set; } = new H5PlayerRoster();

        /// <summary>Whoever is acting right now.</summary>
        public H5PlayerState CurrentPlayer => Players != null ? Players.Current : null;

        /// <summary>Whether the player at the keyboard is the one acting.</summary>
        public bool IsLocalTurn => CurrentPlayer != null &&
            string.Equals(CurrentPlayer.Id, LocalPlayerId, StringComparison.OrdinalIgnoreCase);

        /// <summary>The local player's record.</summary>
        public H5PlayerState LocalPlayer => Players != null ? Players.Find(LocalPlayerId) : null;

        /// <summary>Raised when a player is defeated or the match ends.</summary>
        public event Action<string> MatchEnded;

        /// <summary>
        /// Start a match: a human player with the chosen faction and an
        /// opponent to play against. The map's authored owners still decide who
        /// holds which town; this decides who those owners <em>are</em>.
        /// </summary>
        public void SetupMatch(UI.NewGameSetup setup)
        {
            Players = new H5PlayerRoster();
            var human = Players.GetOrAdd("PLAYER_1", true);
            human.IsHuman = true;
            human.IsAlive = true;
            human.Faction = setup != null ? setup.Faction : "";
            human.TownType = setup != null ? setup.TownType : "";
            human.HeroClass = setup != null ? setup.HeroClass : "";
            if (setup != null && !string.IsNullOrEmpty(setup.HeroId))
            {
                var chosen = Database != null ? Database.GetHero(setup.HeroId) : null;
                if (chosen != null && !AssignRosterHero(human.Id, chosen))
                    AddLog("This map gives you no hero; " + chosen.DisplayName + " was not placed.");
            }
            human.Difficulty = setup != null ? setup.Difficulty : 2;
            if (setup != null && !string.IsNullOrEmpty(setup.PlayerName)) human.Name = setup.PlayerName;

            var cpu = Players.GetOrAdd("PLAYER_2", false);
            cpu.IsHuman = false;
            cpu.IsAlive = true;
            // The opponent gets a different faction where the map allows it, so
            // a 1v1 does not read as a mirror match by accident.
            cpu.Faction = PickOpponentFaction(human.Faction);
            cpu.TownType = TownTypeForFaction(cpu.Faction);
            cpu.HeroClass = ClassForTown(cpu.TownType);
            if (setup != null && !string.IsNullOrEmpty(setup.OpponentName)) cpu.Name = setup.OpponentName;

            Players.TurnIndex = 0;
            ApplyPlayerFactions();
            Changed?.Invoke();
        }

        /// <summary>A faction that is not the player's, from the imported town types.</summary>
        string PickOpponentFaction(string takenFaction)
        {
            if (Database == null || Database.TownTypes == null) return "";
            var names = new List<string>();
            foreach (var kv in Database.TownTypes)
            {
                var townType = kv.Value;
                if (townType == null || string.IsNullOrEmpty(townType.faction)) continue;
                if (townType.faction == "RandomTown") continue;
                if (townType.buildings == null || townType.buildings.Count == 0) continue;
                names.Add(townType.faction);
            }
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
                if (!string.Equals(name, takenFaction, StringComparison.OrdinalIgnoreCase))
                    return name;
            return names.Count > 0 ? names[0] : "";
        }

        /// <summary>The <c>TOWN_*</c> id whose faction folder is <paramref name="faction"/>.</summary>
        public string TownTypeForFaction(string faction)
        {
            if (Database == null || Database.TownTypes == null || string.IsNullOrEmpty(faction)) return "";
            foreach (var kv in Database.TownTypes)
            {
                var townType = kv.Value;
                if (townType != null &&
                    string.Equals(townType.faction, faction, StringComparison.OrdinalIgnoreCase))
                    return townType.id;
            }
            return "";
        }

        /// <summary>
        /// Give every neutral town and hero on the map an owner, so a scenario
        /// that authored only <c>PLAYER_1</c> objects still has an opponent.
        /// The player keeps what the map gave them; everything else goes to the
        /// first computer that does not already have a town.
        /// </summary>
        void ApplyPlayerFactions()
        {
            var human = Players != null ? Players.Find(LocalPlayerId) : null;
            var cpu = Players != null ? Players.Computers() : null;
            if (cpu == null || cpu.Count == 0) return;

            // A shipped Multiplayer scenario authors a PlayerID per town, and
            // 38 of the 40 do. Those players are real opponents and need a
            // record each, or their towns belong to nobody the roster knows
            // about and never act. Only PLAYER_1 is the human; everyone else
            // the map named is the computer.
            foreach (var town in AllTowns())
            {
                if (town == null || string.IsNullOrEmpty(town.Owner)) continue;
                if (town.Owner == "PLAYER_NONE") continue;
                if (Players.Find(town.Owner) != null) continue;
                var extra = Players.GetOrAdd(town.Owner, false);
                extra.IsHuman = string.Equals(extra.Id, LocalPlayerId,
                                              StringComparison.OrdinalIgnoreCase);
                extra.IsAlive = true;
            }
            cpu = Players.Computers();

            var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var town in AllTowns())
            {
                if (town == null) continue;
                var existingOwner = string.IsNullOrEmpty(town.Owner)
                    ? null : Players.Find(town.Owner);
                if (existingOwner != null)
                {
                    if (!existingOwner.TownIds.Contains(town.ObjectId))
                        existingOwner.TownIds.Add(town.ObjectId);
                    continue;
                }
                // Hand unowned towns out round-robin, least-loaded first.
                var target = LeastLoaded(cpu, assigned);
                town.Owner = target.Id;
                assigned[target.Id] = assigned.TryGetValue(target.Id, out var n) ? n + 1 : 1;
            }

            // A player's faction is whatever their first town says, so the town
            // screen, the dwellings and the hero class all agree.
            foreach (var player in Players.Alive())
            {
                var first = FirstTownOf(player);
                if (first != null && !string.IsNullOrEmpty(first.TownType))
                {
                    player.TownType = first.TownType;
                    player.Faction = first.Faction;
                }
            }
            // The player's chosen faction wins over whatever the map authored
            // on their first town, so the menu's choice is what they get.
            if (human != null && !string.IsNullOrEmpty(human.Faction) &&
                string.IsNullOrEmpty(human.TownType))
                human.TownType = TownTypeForFaction(human.Faction);
        }

        static H5PlayerState LeastLoaded(List<H5PlayerState> candidates, Dictionary<string, int> assigned)
        {
            H5PlayerState best = null;
            var bestCount = int.MaxValue;
            foreach (var c in candidates)
            {
                if (c == null) continue;
                var count = assigned.TryGetValue(c.Id, out var n) ? n : 0;
                if (count < bestCount)
                {
                    bestCount = count;
                    best = c;
                }
            }
            return best ?? candidates[0];
        }

        H5TownState FirstTownOf(H5PlayerState player)
        {
            if (player == null) return null;
            foreach (var town in AllTowns())
                if (town != null && string.Equals(town.Owner, player.Id, StringComparison.OrdinalIgnoreCase))
                    return town;
            return null;
        }

        /// <summary>The towns a player holds.</summary>
        public List<H5TownState> TownsOf(H5PlayerState player)
        {
            var result = new List<H5TownState>();
            if (player == null) return result;
            foreach (var town in AllTowns())
                if (town != null && string.Equals(town.Owner, player.Id, StringComparison.OrdinalIgnoreCase))
                    result.Add(town);
            return result;
        }

        /// <summary>The hero map objects a player commands.</summary>
        public List<H5MapObject> HeroesOf(H5PlayerState player)
        {
            var result = new List<H5MapObject>();
            if (player == null || _heroes == null) return result;
            foreach (var hero in _heroes)
            {
                if (hero == null || hero.Def == null) continue;
                if (string.Equals(hero.Def.owner, player.Id, StringComparison.OrdinalIgnoreCase))
                    result.Add(hero);
            }
            return result;
        }

        /// <summary>
        /// Places the named hero the player chose on that player's first
        /// authored hero. Returns false when the map gives the player no hero,
        /// in which case the choice has nothing to attach to.
        /// </summary>
        public bool AssignRosterHero(string playerId, HeroDef roster)
        {
            if (roster == null) return false;
            var heroes = HeroesOf(Players != null ? Players.Find(playerId) : null);
            if (heroes.Count == 0) return false;
            var key = heroes[0].StateKey;
            if (string.IsNullOrEmpty(key)) return false;
            _rosterHeroes[key] = roster;
            if (_heroStates.TryGetValue(key, out var existing) && existing != null)
                ApplyRosterHero(existing, roster);
            return true;
        }

        /// <summary>
        /// The army a player recruits into: their first hero's, or the garrison
        /// of their first town when no hero is available.
        /// </summary>
        public List<HoMM5.Combat.ArmyStackState> ArmyOf(H5PlayerState player)
        {
            var heroes = HeroesOf(player);
            foreach (var hero in heroes)
            {
                var hs = hero != null ? hero.HeroState : null;
                if (hs != null && hs.Army != null) return hs.Army;
            }
            foreach (var town in TownsOf(player))
            {
                var def = TownDef(town);
                if (def == null) continue;
                EnsureGarrison(def);
                return _garrisonArmies[TownKey(def)];
            }
            return null;
        }

        /// <summary>
        /// Recruit for a player who is not necessarily local: the same rules as
        /// <see cref="RecruitFromTown"/>, but paid from that player's treasury.
        /// </summary>
        public int RecruitFor(H5PlayerState player, H5TownState town, string slotType,
                              string creatureId, int count)
        {
            if (player == null || town == null) return 0;
            if (!string.Equals(town.Owner, player.Id, StringComparison.OrdinalIgnoreCase)) return 0;
            var def = TownDef(town);
            var army = ArmyOf(player);
            if (army == null)
            {
                if (def == null) return 0;
                EnsureGarrison(def);
                army = _garrisonArmies[TownKey(def)];
            }
            string reason;
            var taken = town.Recruit(Database, slotType, creatureId, count, TreasuryOf(player), army,
                                     H5HeroState.MaxArmySlots, out reason);
            if (taken > 0) SyncTownDef(town);
            return taken;
        }

        // -- turn order ----------------------------------------------------

        /// <summary>
        /// End the current player's turn. In Heroes 5 the day advances only when
        /// the last surviving player has acted, so this hands the map to the
        /// next player and calls <see cref="EndDay"/> on the wrap.
        /// </summary>
        public void EndTurn()
        {
            if (Players == null || Players.Players.Count == 0)
            {
                EndDay();
                return;
            }
            if (HasMovingHero)
            {
                AddLog("A hero is still moving.");
                return;
            }
            var wrapped = Players.EndTurn();
            if (wrapped) EndDay();
            Changed?.Invoke();

            // The computer plays its turn immediately; there is no hot-seat
            // hand-off to wait for.
            RunComputerTurns();
            CheckMatchEnd();
        }

        /// <summary>
        /// Let every computer player whose turn came up act, until the turn is
        /// back with a human.
        /// </summary>
        void RunComputerTurns()
        {
            var guard = 0;
            while (CurrentPlayer != null && !CurrentPlayer.IsHuman && guard++ < 16)
            {
                List<string> lines;
                _actingPlayerId = CurrentPlayer.Id;
                try { lines = H5AiController.PlayTurn(this, CurrentPlayer); }
                finally { _actingPlayerId = null; }
                foreach (var line in lines) AddLog(line);
                var wrapped = Players.EndTurn();
                if (wrapped) EndDay();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// A player who has lost every town and hero is out. When one player is
        /// left, the match is over.
        /// </summary>
        public void CheckMatchEnd()
        {
            if (Players == null) return;
            foreach (var player in Players.Players)
            {
                if (player == null || !player.IsAlive) continue;
                var hasTown = TownsOf(player).Count > 0;
                var hasHero = HeroesOf(player).Count > 0;
                if (hasTown || hasHero) continue;
                player.IsAlive = false;
                AddLog($"{player.Name} is defeated.");
            }
            var winner = Players.Winner();
            if (winner != null && MatchEnded != null) MatchEnded(winner.Id);
        }

        /// <summary>Every town on the map, owned or not.</summary>
        public List<H5TownState> AllTowns()
        {
            var outList = new List<H5TownState>();
            foreach (var kv in _townStates)
                if (kv.Value != null) outList.Add(kv.Value);
            outList.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return outList;
        }

        /// <summary>The local player's towns.</summary>
        public List<H5TownState> OwnedTowns()
        {
            var outList = new List<H5TownState>();
            foreach (var town in AllTowns())
                if (string.Equals(town.Owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase))
                    outList.Add(town);
            return outList;
        }

        /// <summary>
        /// Raise a building in a town: cost, prerequisites, the scenario's cap and
        /// the one-building-per-day rule all come from the shipped tables.
        /// </summary>
        public bool BuildInTown(H5TownState town, HoMM5.Data.TownBuildingDef building)
        {
            if (town == null || building == null) return false;
            if (!string.Equals(town.Owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("That town is not yours.");
                return false;
            }
            string reason;
            if (!town.Build(Database, building, Resources, out reason))
            {
                AddLog(reason);
                return false;
            }
            AddLog($"{town.Name}: {building.DisplayName} built ({building.CostText()}).");
            SyncTownDef(town);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Recruit from a dwelling. Creatures go to a local hero standing in the
        /// town, and to the town garrison when no hero is there.
        /// </summary>
        public int RecruitFromTown(H5TownState town, string slotType, string creatureId, int count)
        {
            if (town == null) return 0;
            if (!string.Equals(town.Owner, LocalPlayerId, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("That town is not yours.");
                return 0;
            }
            var def = TownDef(town);
            var hero = HeroInTown(town);
            List<HoMM5.Combat.ArmyStackState> army;
            string where;
            if (hero != null)
            {
                army = hero.Army;
                where = hero.HeroName + "'s army";
            }
            else if (def != null)
            {
                EnsureGarrison(def);
                army = _garrisonArmies[TownKey(def)];
                where = "the garrison of " + town.Name;
            }
            else
            {
                AddLog("That town is not on the loaded floor.");
                return 0;
            }
            string reason;
            var taken = town.Recruit(Database, slotType, creatureId, count, Resources, army,
                                     H5HeroState.MaxArmySlots, out reason);
            if (taken <= 0)
            {
                AddLog(reason);
                return 0;
            }
            var creature = Database != null ? Database.GetCreature(creatureId) : null;
            var name = creature != null ? creature.DisplayName : creatureId;
            AddLog($"{taken} {name} join {where}.");
            if (hero != null)
            {
                var heroDef = heroArmedDef(hero);
                WriteArmyToDef(heroDef, hero.Army);
            }
            Changed?.Invoke();
            return taken;
        }

        readonly Dictionary<string, List<HoMM5.Combat.ArmyStackState>> _garrisonArmies =
            new Dictionary<string, List<HoMM5.Combat.ArmyStackState>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A town's garrison as army stacks. The map's authored garrison is the
        /// seed; recruiting without a hero in town adds to it.
        /// </summary>
        void EnsureGarrison(HoMM5.Data.MapObjectDef def)
        {
            var key = TownKey(def);
            if (string.IsNullOrEmpty(key) || _garrisonArmies.ContainsKey(key)) return;
            var army = new List<HoMM5.Combat.ArmyStackState>();
            if (def.garrison != null)
                foreach (var stack in def.garrison)
                {
                    if (stack == null || string.IsNullOrEmpty(stack.creature)) continue;
                    var n = stack.count > 0 ? stack.count : stack.amount;
                    if (n > 0) army.Add(new HoMM5.Combat.ArmyStackState(stack.creature, n));
                }
            _garrisonArmies[key] = army;
        }

        /// <summary>The map def a town state was built from.</summary>
        HoMM5.Data.MapObjectDef TownDef(H5TownState town)
        {
            if (town == null || _map == null || _map.objects == null) return null;
            foreach (var def in _map.objects)
                if (def != null && def.kind == "AdvMapTown" && TownKey(def) == town.ObjectId)
                    return def;
            return null;
        }

        /// <summary>A local hero close enough to the town to recruit into.</summary>
        H5HeroState HeroInTown(H5TownState town)
        {
            if (town == null) return null;
            for (var i = 0; i < _heroes.Count; i++)
            {
                var hero = _heroes[i];
                if (hero == null || hero.Def == null || !hero.IsLocalHero) continue;
                var dx = hero.Def.x - town.X;
                var dy = hero.Def.y - town.Y;
                // A town covers several tiles; its blocked footprint reaches two
                // tiles out from the position the map stores.
                if (dx * dx + dy * dy > 9f) continue;
                var hs = GetHeroState(hero);
                if (hs != null) return hs;
            }
            return null;
        }

        /// <summary>Push a town's built tiers back onto its map def (sieges read it).</summary>
        void SyncTownDef(H5TownState town)
        {
            var def = TownDef(town);
            if (def == null || town == null) return;
            def.buildings = town.Slots;
        }

        // -- treasuries ----------------------------------------------------

        /// <summary>The player whose turn is being played: the computer during its turn, otherwise the human.</summary>
        public string ActingPlayerId => _actingPlayerId ?? LocalPlayerId;
        string _actingPlayerId;

        /// <summary>The treasury a player spends from and earns into.</summary>
        public HoMM5.Data.PlayerResourceInventory TreasuryOf(H5PlayerState player)
        {
            if (player == null || player.IsHuman) return Resources;
            if (!player.TreasuryFunded) FundComputer(player);
            return player.Resources;
        }

        /// <summary>The treasury of whoever owns a hero or other map object.</summary>
        public HoMM5.Data.PlayerResourceInventory TreasuryOf(H5MapObject hero)
        {
            return TreasuryOfOwner(hero != null && hero.Def != null ? hero.Def.owner : null);
        }

        public HoMM5.Data.PlayerResourceInventory TreasuryOfOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId) ||
                string.Equals(ownerId, LocalPlayerId, StringComparison.OrdinalIgnoreCase))
                return Resources;
            var player = Players != null ? Players.Find(ownerId) : null;
            // An owner the roster does not know is paid into nothing, rather than
            // into the human's purse.
            return player != null ? TreasuryOf(player) : new HoMM5.Data.PlayerResourceInventory();
        }

        /// <summary>A computer opponent starts with the shipped computer stake for its difficulty.</summary>
        void FundComputer(H5PlayerState player)
        {
            player.TreasuryFunded = true;
            var rows = _rules != null ? _rules.startComputerResources : null;
            if (rows != null && rows.Count > 0)
            {
                var index = Math.Max(0, Math.Min(rows.Count - 1, player.Difficulty));
                player.Resources.Set(rows[index]);
            }
            else
            {
                player.Resources.Set(DefaultStartResources());
            }
        }

        static HoMM5.Data.StartResourcesDef DefaultStartResources()
        {
            // advmap-common.lua's default resources at normal difficulty.
            return new HoMM5.Data.StartResourcesDef
            {
                wood = 20, ore = 20, mercury = 10, crystal = 10,
                sulfur = 10, gem = 10, gold = 20000
            };
        }

        // -- movement ------------------------------------------------------

        internal float GetMovement(string key)
        {
            if (!string.IsNullOrEmpty(key) && _movement.TryGetValue(key, out var points))
                return points;
            // Fresh day / first bind: the budget is the daily base scaled by
            // the hero's Logistics rank.
            if (!string.IsNullOrEmpty(key) && _heroStates.TryGetValue(key, out var hs) && hs != null)
                return DailyHeroMovement * hs.Effects.MovementMultiplier;
            return DailyHeroMovement;
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
                // The roster carries each computer's treasury and whether it is
                // still in the match; the human's treasury stays in `resources`.
                players = Players != null ? new List<H5PlayerState>(Players.Players) : new List<H5PlayerState>(),
                turnIndex = Players != null ? Players.TurnIndex : 0,
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
            foreach (var town in AllTowns()) save.towns.Add(town);
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
            if (save.players != null && save.players.Count > 0)
                Players = new H5PlayerRoster { Players = save.players, TurnIndex = save.turnIndex };
            _movement.Clear();
            if (save.movement != null)
                foreach (var kv in save.movement) _movement[kv.Key] = kv.Value;
            _heroStates.Clear();
            if (save.heroes != null)
                foreach (var h in save.heroes)
                {
                    if (h == null || string.IsNullOrEmpty(h.Id)) continue;
                    // The Effects aggregate is a cache; recompute it from the
                    // persisted skill/perk lists on every load.
                    h.RecalculateEffects(Database);
                    _heroStates[h.Id] = h;
                }
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
            // Town state is restored before the rebuild; BindTowns then replays
            // the built tiers over the freshly parsed map slots.
            _townStates.Clear();
            _garrisonArmies.Clear();
            if (save.towns != null)
                foreach (var town in save.towns)
                    if (town != null && !string.IsNullOrEmpty(town.ObjectId))
                        _townStates[town.ObjectId] = town;
            Log.Clear();
            if (save.log != null) Log.AddRange(save.log);
            Changed?.Invoke();
        }
    }
}
