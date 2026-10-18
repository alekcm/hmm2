using System.Collections.Generic;
using HoMM5.Combat;
using HoMM5.Audio;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using HoMM5.UI;
using HoMM5.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HoMM5
{
    /// <summary>
    /// Entry point.  Loads the imported game data, builds the main menu, camera,
    /// lights and HUD, then starts the mission selected from the campaign data.
    ///
    /// Nothing here needs a hand-authored scene asset beyond an empty scene with
    /// this component on one GameObject -- which is what
    /// <c>Assets/Scenes/AdventureMap.unity</c> contains.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class H5Bootstrap : MonoBehaviour
    {
<<<<<<< HEAD
        [Tooltip("Debug only: explicitly bypass the main menu using initialMapId.")]
        public bool startDirectlyOnMap;
        public string initialMapId = "";

        static NewGameSetup _launchSetup;
        static bool _launchSave;

        [Tooltip("0 = surface (GROUND), 1 = underground (UNDERGROUND).")]
        public int floor = 0;

        [Tooltip("Map player whose heroes/resources this local prototype controls.")]
        public string localPlayerId = "PLAYER_1";

        [Tooltip("Starting-resource tier from DefaultStats: 0 easiest, 1, 2 normal, 3 hardest.")]
        [Range(0, 3)] public int gameDifficulty = 2;

        public GameDatabase Database { get; private set; }
        public MeshCache Meshes { get; private set; }
        public HoMM5.Core.H5TextureCache Textures { get; private set; }
        public MapSceneBuilder CurrentMap { get; private set; }
        public AdventureMapGameState GameState { get; private set; }
=======
        [Tooltip("Load this map on start.  Empty opens the campaign mission menu.")]
        public string initialMapId = "";

        [Tooltip("0 = surface (GROUND), 1 = underground (UNDERGROUND).")]
        public int floor = 0;

        public GameDatabase Database { get; private set; }
        public MeshCache Meshes { get; private set; }
        public H5MaterialCache Materials { get; private set; }
        public MapSceneBuilder CurrentMap { get; private set; }
        public NWorldPlayer ActivePlayer { get; private set; }
        public NWorldHero ActiveHero { get; private set; }
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

        GameObject _mapRoot;
        HoMM5Hud _hud;
        HoMM5MainMenu _mainMenu;
        AdventureMapController _controller;
        H5AdventureController _adventure;

        // One battle at a time: its scene lives in its own root while the
        // adventure map is hidden under it.
        HoMM5.UI.MainMenu _menu;

        CombatSceneBuilder _combatScene;
        CombatController _combat;
        CombatHud _combatHud;
        H5MapObject _combatHero;
        H5MapObject _combatMonster;

        /// <summary>Whether a battle is in progress (blocks turns, F1, saving).</summary>
        public bool CombatActive => _combat != null;

        void Start()
        {
            Database = new GameDatabase();
            Database.Load();
            H5Audio.Create(gameObject, Database);
            GameState = new AdventureMapGameState();
            GameState.Database = Database;
            GameState.SetBaseHeroMovement(Database.Rules != null
                ? Database.Rules.baseHeroMovement : AdventureMapGameState.DefaultDailyHeroMovement);
            GameState.Changed += RefreshTurnHud;
            GameState.CombatRequested += StartCombat;
            CombatRules.Configure(Database.Rules != null ? Database.Rules.combat : null);

            ActivePlayer = new NWorldPlayer("PLAYER_1");
            ActiveHero = new NWorldHero("Hero1", Database.DefaultStats);

            Meshes = new MeshCache();
            Materials = new H5MaterialCache();

            Textures = new HoMM5.Core.H5TextureCache(Shader.Find("Standard"));
            if (!Textures.LoadManifest())
                Debug.LogWarning("[HoMM5] no texture manifest found -- objects will be untextured.\n" +
                                 HoMM5.Data.GameDatabase.MissingImportMessage());

            BuildLights();
            BuildCamera();
            _hud = gameObject.AddComponent<HoMM5Hud>();
            _hud.Controller = _controller;
            _hud.Database = Database;
<<<<<<< HEAD
            _hud.GameState = GameState;
            _hud.EndDayRequested = EndDay;
            _hud.SaveRequested = SaveGame;
            _hud.LoadRequested = LoadGame;
            _hud.TownScreenRequested = ShowTownScreen;
            _hud.TownSelectedRequested = OpenTown;
            _hud.MenuRequested = ReturnToMenu;
            _hud.NextHeroRequested = SelectNextHero;
            _hud.HeroScreenRequested = ShowHeroSkills;
            _hud.FloorRequested = ToggleFloor;
            _hud.ScenarioRequested = ShowScenario;
            RefreshTurnHud();
            // Awake has built the HUD before these dependencies are assigned.
=======
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
            _hud.PopulateMapList(LoadMap);
            GameState.MatchEnded += OnMatchEnded;

            // The main menu is the front door: the map, the faction and the
            // hero are the player's choices, not a default. It is created on
            // the HUD's canvas and sits above every adventure panel.
            _menu = HoMM5.UI.MainMenu.Create(_hud.CanvasRoot, Database);
            _menu.StartRequested = StartNewGame;
            _menu.LoadRequested = LoadGame;
            _menu.QuitRequested = QuitGame;

            _adventure = gameObject.AddComponent<H5AdventureController>();
            _adventure.StatusChanged += RefreshStats;

            _mainMenu = gameObject.AddComponent<HoMM5MainMenu>();
            _mainMenu.Initialize(Database, LoadMap, ReturnToMainMenu);

            if (!Database.IsLoaded)
            {
<<<<<<< HEAD
                _hud.Hide();
                _controller.enabled = false;
                _menu.SetStatus("Import not found: run python import_to_unity.py from the repository.");
                return;
            }

            if (_launchSetup != null)
            {
                var setup = _launchSetup;
                _launchSetup = null;
                StartNewGame(setup);
                return;
            }
            if (_launchSave)
            {
                _launchSave = false;
                LoadGame();
                return;
            }

            // A stale inspector map name must never silently bypass the menu.
            if (startDirectlyOnMap && !string.IsNullOrEmpty(initialMapId))
            {
                _menu.Hide();
                LoadMap(initialMapId);
                return;
            }

            _hud.Hide();
            _controller.enabled = false;
            GameState.AddLog("Choose a scenario to begin.");
            if (H5Paths.ReadAllText(H5Paths.Import("UI/skin.json")) == null)
                _menu.SetStatus("Original UI not imported. Run: python import_to_unity.py ui");
            H5Audio.Theme(H5Cues.MainMenuTheme);
        }

        // -- new game ------------------------------------------------------

        /// <summary>
        /// The menu's "Start": load the chosen scenario, hand the game state
        /// the chosen faction and hero, and give the player an opponent.
        /// </summary>
        public void StartNewGame(HoMM5.UI.NewGameSetup setup)
        {
            if (setup == null || string.IsNullOrEmpty(setup.MapId)) return;
            if (Database == null || !Database.IsLoaded || CombatActive || GameState.HasMovingHero) return;
            if (MapSceneBuilder.LoadMapDef(setup.MapId) == null)
            {
                _menu.SetStatus("Cannot open scenario: " + setup.MapId + ". Check the maps import.");
                return;
            }

            if (SceneManager.GetActiveScene().name == "MainMenu" &&
                Application.CanStreamedLevelBeLoaded("AdventureMap"))
            {
                _launchSetup = setup;
                SceneManager.LoadScene("AdventureMap");
                return;
            }
            gameDifficulty = Mathf.Clamp(setup.Difficulty, 0, 3);
            localPlayerId = "PLAYER_1";
            _pendingSetup = setup;

            LoadMap(setup.MapId);
            GameState.AddLog($"{setup.PlayerName} ({setup.FactionName}) vs {setup.OpponentName}. " +
                             "End Turn hands the map over; F5 saves, F9 loads.");
        }

        HoMM5.UI.NewGameSetup _pendingSetup;

        void OnMatchEnded(string winnerId)
        {
            if (GameState == null) return;
            var winner = GameState.Players != null ? GameState.Players.Find(winnerId) : null;
            var name = winner != null ? winner.Name : winnerId;
            var localWon = string.Equals(winnerId, localPlayerId,
                                         System.StringComparison.OrdinalIgnoreCase);
            var folder = LocalFactionFolder();
            if (localWon)
                H5Audio.Stinger(new[] { "win-campaign/" + H5Cues.Dashed(folder), "win-campaign/haven" }, null);
            else
                H5Audio.Stinger(new[] { "lose-campaign/" + H5Cues.Dashed(folder), "lose-campaign/haven" }, null);
            GameState.AddLog(localWon ? $"Victory -- {name} wins the scenario."
                                      : $"Defeat -- {name} wins the scenario.");
            var request = new H5DialogRequest
            {
                Message = localWon ? "Victory!\n\nThe scenario is yours."
                                   : "Defeat.\n\nYour last town and hero are gone.",
            };
            request.Options.Add(new H5DialogOption
            {
                Label = "Main menu",
                Action = () => ReturnToMenu(),
            });
            GameState.RequestDialog(request);
        }

        /// <summary>Pause the adventure behind the main menu.</summary>
        public void ReturnToMenu()
        {
            if (CombatActive || (GameState != null && GameState.HasMovingHero)) return;
            if (_hud != null)
            {
                if (_hud.Town != null) _hud.Town.Hide();
                _hud.Hide();
            }
            _townMusic = false;
            if (_controller != null) _controller.enabled = false;
            if (CurrentMap != null && CurrentMap.Root != null) CurrentMap.Root.SetActive(false);
            if (_menu != null)
            {
                var local = GameState != null ? GameState.LocalPlayer : null;
                _menu.ResumeRequested = CurrentMap != null && (local == null || local.IsAlive)
                    ? (System.Action)ResumeAdventure : null;
                _menu.Show();
            }
            H5Audio.Theme(H5Cues.MainMenuTheme);
        }

        void ResumeAdventure()
        {
            if (_menu != null) _menu.Hide();
            if (CurrentMap != null && CurrentMap.Root != null) CurrentMap.Root.SetActive(true);
            if (_controller != null) _controller.enabled = true;
            if (_hud != null) _hud.Show();
            ResumeMapMusic();
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
=======
                _hud.SetStats("Import not found.\nRun: python -m homm5import.cli all\n(see tools/homm5_import/README.md)");
                _mainMenu.Show();
                return;
            }

            if (!string.IsNullOrEmpty(initialMapId))
            {
                LoadMap(initialMapId);
            }
            else
            {
                _mainMenu.Show();
                _hud.SetStats("Select a campaign mission to begin.");
            }
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
        }

        public void LoadMap(string mapId)
        {
<<<<<<< HEAD
            var map = MapSceneBuilder.LoadMapDef(mapId);
            LoadMapAtFloor(mapId, map != null ? map.initialFloor : floor);
        }

        void LoadMapAtFloor(string mapId, int targetFloor, bool restoredState = false)
        {
            if (GameState != null && GameState.HasMovingHero) return;
            var previousMapId = CurrentMap != null && CurrentMap.Map != null ? CurrentMap.Map.id : "";
            var previousFloor = floor;
            var preserveScenarioState = string.Equals(previousMapId, mapId,
                System.StringComparison.OrdinalIgnoreCase) && previousFloor != targetFloor && _pendingSetup == null;
            // Do not tear down a running scenario for a missing map.
            if (MapSceneBuilder.LoadMapDef(mapId) == null)
            {
                if (_menu != null && _menu.IsOpen) _menu.SetStatus("Cannot open scenario: " + mapId);
                return;
            }
            floor = targetFloor == 1 ? 1 : 0;
            if (_controller != null) _controller.Select(null);
            if (CurrentMap != null && CurrentMap.Root != null)
            {
                CurrentMap.Root.SetActive(false);
                Destroy(CurrentMap.Root);
            }
            Meshes.Clear();
            if (Textures != null) Textures.Dispose();

            var builder = new MapSceneBuilder(Meshes, MakeTerrainMaterial(), MakeObjectMaterial(),
                                              MakeWaterMaterial(), Textures, Database);
=======
            if (string.IsNullOrEmpty(mapId)) return;
            _mainMenu?.Hide();
            _adventure?.Unbind();
            if (CurrentMap != null && CurrentMap.Root != null) Destroy(CurrentMap.Root);
            Meshes.Clear();

            var builder = new MapSceneBuilder(Meshes, MakeTerrainMaterial(), MakeObjectMaterial(),
                                               MakeWaterMaterial(), Materials);
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
            var root = builder.Build(mapId, floor);
            if (root == null)
            {
                _hud.SetStats("Failed to load map " + mapId);
                return;
            }
            CurrentMap = builder;
<<<<<<< HEAD
            // A restored save carries its own day, resources and world changes;
            // a floor switch preserves the running scenario; anything else is a
            // brand-new scenario.
            var newScenario = !restoredState && !preserveScenarioState;
            GameState.BindMap(builder, newScenario, localPlayerId, Database.Rules,
                              Database.TownBuildings, gameDifficulty);

            // A new game sets up its match once, after the map's objects exist:
            // ownership, the opponent's faction and the starting treasury all
            // need the towns and heroes BindMap just registered. A floor switch
            // or a load must not re-deal the scenario.
            if (newScenario && _pendingSetup != null)
            {
                GameState.SetupMatch(_pendingSetup);
                _pendingSetup = null;
            }
=======
            floor = builder.ActiveFloor;
            ActiveHero = CreateHeroForMap(builder);

            if (builder.Terrain != null)
                _controller.SetMapBounds(builder.Terrain.Tiles, builder.Terrain.Tiles);
            _controller.FocusOn(new Vector3((builder.Map.tileX) * 0.5f, 0f, (builder.Map.tileY) * 0.5f), 45f);
            _adventure?.Bind(builder, _controller, ActiveHero, Database);
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

            _controller.SetMapBounds(builder.Map.tileX, builder.Map.tileY);
            // TileToWorld accounts for both the horizontal tile scale and the
            // terrain height at the map center.
            _controller.FocusOn(builder.TileToWorld(builder.Map.tileX * 0.5f,
                                                    builder.Map.tileY * 0.5f), 45f);

            _hud.AttachMap(builder);
            RefreshStats();
            ResumeAdventure();
        }

        // -- save / load ---------------------------------------------------

        public void SaveGame()
        {
            if (GameState == null || CurrentMap == null || GameState.HasMovingHero || CombatActive) return;
            var save = GameState.CaptureState();
            save.floor = floor;
            save.SaveToFile();
            GameState.AddLog("Game saved.");
        }

        public void LoadGame()
        {
            if (GameState == null || GameState.HasMovingHero || CombatActive) return;
            var save = H5SaveGame.LoadFromFile();
            if (save == null)
            {
                GameState.AddLog("No readable quick save found (F5 to save).");
                if (_menu != null && _menu.IsOpen) _menu.SetStatus("No readable quick save found. Choose New Game.");
                return;
            }
            var mapId = string.IsNullOrEmpty(save.mapId) && CurrentMap != null && CurrentMap.Map != null
                ? CurrentMap.Map.id : save.mapId;
            if (string.IsNullOrEmpty(mapId) || MapSceneBuilder.LoadMapDef(mapId) == null)
            {
                if (_menu != null && _menu.IsOpen) _menu.SetStatus("The saved scenario is not imported.");
                GameState.AddLog("Cannot load: the saved scenario is not imported.");
                return;
            }
            if (SceneManager.GetActiveScene().name == "MainMenu" &&
                Application.CanStreamedLevelBeLoaded("AdventureMap"))
            {
                _launchSave = true;
                SceneManager.LoadScene("AdventureMap");
                return;
            }
            // Validate first; a failed Continue must leave the menu and the
            // running state intact. BindMap must use the saved local player.
            _pendingSetup = null;
            GameState.RestoreState(save);
            localPlayerId = GameState.LocalPlayerId;
            LoadMapAtFloor(mapId, save.floor == 1 ? 1 : 0, true);
            GameState.AddLog("Game loaded.");
        }

        // -- combat --------------------------------------------------------

        void StartCombat(AdventureMapGameState.BattleContext context)
        {
            if (_combat != null || CurrentMap == null || GameState == null || context == null) return;

            var arenaId = PickArenaId(context.IsSiege);
            var state = CombatGameState.Create(context.Hero, context.Monster, Database,
                                               GameState.Rand, context.GuardOverride);
            if (state.Units.Find(u => u.Side == CombatSide.Attacker) == null)
            {
                GameState.AddLog("There is no army to attack with.");
                return;
            }
            if (context.IsSiege)
                state.SetupSiege(context.FortLevel);

            // Skill-driven battlefield context: the ammo cart sharpens the
            // shooters (+1/2/3 attack by War Machines rank), Road Home hastens
            // the army on grass arenas.
            var heroFx = context.Hero != null && context.Hero.HeroState != null
                ? context.Hero.HeroState.Effects
                : HoMM5.Map.HeroSkillEffects.Create();
            var hasAmmoCart = state.Units.Exists(u =>
                u.Side == CombatSide.Attacker && u.IsAlive &&
                u.UnitType == CombatUnitType.WarMachine && u.WarMachineId == "WAR_MACHINE_AMMO_CART");
            var grassBonus = arenaId != null && arenaId.ToLowerInvariant().Contains("grass")
                ? heroFx.GrassSpeedBonus : 0;
            foreach (var u in state.Units)
            {
                if (u.Side != CombatSide.Attacker) continue;
                if (hasAmmoCart && u.IsRanged) u.ContextAttackBonus += heroFx.AmmoCartAttackBonus;
                if (grassBonus > 0) u.ContextSpeedBonus += grassBonus;
            }

            var scene = new CombatSceneBuilder(Meshes, Textures, Database,
                MakeTerrainMaterial(), MakeObjectMaterial(), MakeWaterMaterial());
            if (!scene.BuildArena(arenaId))
            {
                Debug.LogWarning("[HoMM5] failed to build combat arena " + arenaId);
                GameState.AddLog("The battlefield could not be built.");
                return;
            }
            scene.BuildUnits(state);
            scene.BuildSiege(state.Siege);

            _combatScene = scene;
            _combatHero = context.Hero;
            _combatMonster = context.Monster;

            // The adventure map hides under the arena until the battle ends.
            if (CurrentMap.Root != null) CurrentMap.Root.SetActive(false);
            _hud.Hide();
            if (_controller != null)
            {
                _controller.Select(null);
                _controller.SetMapBounds(scene.Arena.Map.tileX, scene.Arena.Map.tileY);
                _controller.FocusOn(scene.Arena.TileToWorld(scene.Arena.Map.tileX * 0.5f,
                                                            scene.Arena.Map.tileY * 0.5f), 38f);
            }

            var host = new GameObject("Combat");
            _combat = host.AddComponent<CombatController>();
            _combatHud = host.AddComponent<CombatHud>();
            _combat.Hud = _combatHud;
            _combatHud.Controller = _combat;
            _combatHud.Database = Database;
            _combat.BattleFinished += OnCombatFinished;

            var cam = _controller != null ? _controller.GetComponent<Camera>() : null;
            var effects = host.AddComponent<CombatEffects>();
            effects.Setup(scene, Textures, cam);
            effects.Bind(state);

            var folder = BattleFolder(context);
            H5Audio.Theme(H5Cues.BattleTheme(folder), H5Cues.BattleTheme(""));
            _combat.StartBattle(state, scene, cam);
        }

        void OnCombatFinished(CombatResult result)
        {
            if (_combat == null) return;
            _combat.BattleFinished -= OnCombatFinished;

            var folder = LocalFactionFolder();
            var resume = MapThemeKey();
            var outcome = result != null ? result.Outcome : CombatOutcome.Fled;
            if (outcome == CombatOutcome.AttackerWon)
                H5Audio.Stinger(H5Cues.VictoryCandidates(folder), resume);
            else if (outcome == CombatOutcome.DefenderWon)
                H5Audio.Stinger(H5Cues.DefeatCandidates(folder), resume);
            else
                H5Audio.Stinger(H5Cues.RetreatCandidates(folder), resume);

            if (GameState != null)
                GameState.ApplyCombatResult(_combatHero, _combatMonster, result);

            if (_combatScene != null && _combatScene.Root != null) Destroy(_combatScene.Root);
            _combatScene = null;
            Destroy(_combat.gameObject);
            _combat = null;
            _combatHud = null;

            // Back to the adventure map.
            if (CurrentMap != null && CurrentMap.Root != null) CurrentMap.Root.SetActive(true);
            if (_controller != null && CurrentMap != null && CurrentMap.Map != null)
            {
                _controller.SetMapBounds(CurrentMap.Map.tileX, CurrentMap.Map.tileY);
                var focus = _combatHero != null && _combatHero.gameObject.activeInHierarchy
                    ? _combatHero.transform.position
                    : CurrentMap.TileToWorld(CurrentMap.Map.tileX * 0.5f, CurrentMap.Map.tileY * 0.5f);
                _controller.FocusOn(focus, 45f);
            }
            _combatHero = null;
            _combatMonster = null;
            _hud.Show();
            RefreshTurnHud();
            RefreshStats();
        }

        /// <summary>
        /// Pick a combat arena whose scenery matches the adventure terrain
        /// (grass fights on grass arenas); sieges prefer siege arenas. Falls
        /// back to any open arena.
        /// </summary>
        string PickArenaId(bool siege)
        {
            var keyword = TerrainKeyword();
            string fallback = null;
            string best = null;
            string siegeBest = null;
            foreach (var entry in Database.MapIndex)
            {
                if (entry == null || !entry.arena) continue;
                var id = entry.id ?? "";
                var lower = id.ToLowerInvariant();
                if (lower.Contains("boss") || lower.Contains("boat") ||
                    lower.Contains("citadel")) continue;
                var isSiegeArena = lower.Contains("siege");
                if (siege && isSiegeArena && (keyword.Length == 0 || lower.Contains(keyword)))
                {
                    siegeBest = siegeBest ?? id;
                }
                if (fallback == null && !isSiegeArena) fallback = id;
                if (!isSiegeArena && keyword.Length > 0 && lower.Contains(keyword))
                {
                    best = id;
                    if (lower.Contains("small")) break;
                }
            }
            return (siege ? siegeBest : null) ?? best ?? fallback ?? "AbandonedMine";
        }

        string TerrainKeyword()
        {
            var terrain = CurrentMap != null ? CurrentMap.Terrain : null;
            if (terrain == null || terrain.Layers == null) return "";
            foreach (var layer in terrain.Layers)
            {
                var name = layer != null ? (layer.texture ?? "").ToLowerInvariant() : "";
                foreach (var keyword in new[] { "grass", "dirt", "snow", "lava", "sand", "orc", "moss", "beach" })
                    if (name.Contains(keyword)) return keyword;
            }
            return "";
        }

        /// <summary>
        /// End Turn. With a roster this passes play to the next player and the
        /// computer answers immediately; the day advances on the wrap. Without a
        /// roster -- a map opened straight from the editor -- it is the old
        /// single-player day step.
        /// </summary>
        void EndDay()
        {
            if (GameState == null || GameState.HasMovingHero || CombatActive) return;
            if (GameState.Players != null && GameState.Players.Players.Count > 0)
                GameState.EndTurn();
            else
                GameState.EndDay();
            RefreshStats();
        }

        void RefreshTurnHud()
        {
            if (_hud == null || GameState == null) return;
            _hud.SetTurnDate(GameState.Day, GameState.Week, GameState.Month);
            var current = GameState.CurrentPlayer;
            if (current != null)
                _hud.SetCurrentPlayer(current.Name, GameState.IsLocalTurn);
            // The treasury shown is the local player's own, not a shared pot.
            var local = GameState.LocalPlayer;
            var resources = GameState.TreasuryOf(local);
            _hud.SetResources(resources);
        }

        public void ReturnToMainMenu()
        {
            _adventure?.Unbind();
            if (CurrentMap != null && CurrentMap.Root != null) Destroy(CurrentMap.Root);
            CurrentMap = null;
            Meshes?.Clear();
            _controller?.Select(null);
            _mainMenu?.Show();
            _hud?.SetStats("Select a campaign mission to begin.");
        }

        NWorldHero CreateHeroForMap(MapSceneBuilder builder)
        {
            MapObjectDef heroDef = null;
            if (builder.Map?.objects != null)
            {
                for (var i = 0; i < builder.Map.objects.Count; i++)
                {
                    var candidate = builder.Map.objects[i];
                    if (candidate.kind == "AdvMapHero" && candidate.floor == builder.ActiveFloor &&
                        string.Equals(candidate.owner, "PLAYER_1", System.StringComparison.OrdinalIgnoreCase))
                    {
                        heroDef = candidate;
                        break;
                    }
                }
            }

            var hero = new NWorldHero(heroDef != null && !string.IsNullOrEmpty(heroDef.hero)
                ? heroDef.hero : "Hero1", Database.DefaultStats);
            if (heroDef == null) return hero;

            if (heroDef.experience > 0) hero.ChangeHeroStat(EHeroStat.Experience, heroDef.experience);
            hero.BaseAttack = heroDef.heroAttack;
            hero.BaseDefence = heroDef.heroDefence;
            hero.BaseSpellPower = heroDef.heroSpellPower;
            hero.BaseKnowledge = heroDef.heroKnowledge;
            hero.MovePoints = hero.GetMaxMovePoints();
            hero.ManaPoints = hero.GetMaxManaPoints();
            hero.TileX = Mathf.RoundToInt(heroDef.x);
            hero.TileY = Mathf.RoundToInt(heroDef.y);
            hero.Floor = builder.ActiveFloor;
            hero.HasPathfinding = ContainsSkill(heroDef, "PATHFINDING");
            hero.HasSnatch = ContainsSkill(heroDef, "SNATCH");

            if (heroDef.army != null)
            {
                var count = Mathf.Min(heroDef.army.Count, NWorldHero.ArmySlotCount);
                for (var i = 0; i < count; i++)
                {
                    var slot = heroDef.army[i];
                    hero.Army[i] = new HeroCreatureStack { CreatureId = slot.creature, Count = slot.count };
                }
            }
            return hero;
        }

        static bool ContainsSkill(MapObjectDef hero, string suffix)
        {
            if (hero.heroSkills != null)
                for (var i = 0; i < hero.heroSkills.Count; i++)
                    if (hero.heroSkills[i] != null && hero.heroSkills[i].EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                        return true;
            if (hero.heroPerks != null)
                for (var i = 0; i < hero.heroPerks.Count; i++)
                    if (hero.heroPerks[i] != null && hero.heroPerks[i].EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }

        void RefreshStats()
        {
            if (_hud == null || CurrentMap == null) return;
            var counts = new List<string>();
            if (CurrentMap.Map.objectCounts != null)
                foreach (var kv in CurrentMap.Map.objectCounts) counts.Add($"{kv.Key} {kv.Value}");
            var floorLabel = floor == 0 ? "GROUND (0)" : "UNDERGROUND (1)";
            _hud.SetStats(
<<<<<<< HEAD
                $"{CurrentMap.Map.id}   {CurrentMap.Map.tileX}x{CurrentMap.Map.tileY}\n" +
                $"objects placed {CurrentMap.ObjectsPlaced}  (no mesh {CurrentMap.ObjectsMissingMesh})\n" +
                $"terrain chunks {CurrentMap.TerrainChunks} (ground textures {CurrentMap.TerrainTexturesBound})\n" +
                $"meshes cached {Meshes.CachedCount}  animated {CurrentMap.AnimatedObjects}\n" +
=======
                $"{CurrentMap.Map.id}   {CurrentMap.Map.tileX}x{CurrentMap.Map.tileY}   Floor: {floorLabel}\n" +
                $"Hero Lv.{ActiveHero.Level} (XP {ActiveHero.Experience}, MP {ActiveHero.MovePoints}/{ActiveHero.GetMaxMovePoints()}, Mana {ActiveHero.ManaPoints}/{ActiveHero.GetMaxManaPoints()})\n" +
                $"objects placed {CurrentMap.ObjectsPlaced}  (no mesh {CurrentMap.ObjectsMissingMesh})  " +
                $"terrain chunks {CurrentMap.TerrainChunks}  meshes cached {Meshes.CachedCount}\n" +
                (_adventure == null || string.IsNullOrEmpty(_adventure.Status) ? "" : _adventure.Status + "\n") +
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
                string.Join("  ", counts));
        }

        /// <summary>
        /// T: the town screen of the selected town, or of the player's first town
        /// when something else (or nothing) is selected.
        /// </summary>
        public void ShowTownScreen()
        {
            if (GameState == null || CombatActive || _hud == null || _hud.Town == null) return;
            if (_hud.Town.IsOpen)
            {
                _hud.Town.Hide();
                return;
            }
            var selected = _controller != null ? _controller.Selected : null;
            var town = selected != null && selected.IsTown ? GameState.GetTownState(selected) : null;
            if (town == null)
            {
                var owned = GameState.OwnedTowns();
                if (owned.Count > 0) town = owned[0];
            }
            if (town == null)
            {
                GameState.AddLog("There is no town to open.");
                return;
            }
            if (!string.Equals(town.Owner, GameState.LocalPlayerId, System.StringComparison.OrdinalIgnoreCase))
                GameState.AddLog($"{town.Name} belongs to {town.Owner}; its screen is read-only.");
            OpenTown(town);
        }

        void OpenTown(H5TownState town)
        {
            if (town == null || CombatActive || _hud == null || _hud.Town == null) return;
            _hud.Town.Show(town);
            _townMusic = true;
            H5Audio.Theme(H5Cues.TownTheme(H5Cues.FactionFolder(town.TownType, town.Faction)));
        }

        void SelectNextHero()
        {
            if (GameState == null || _controller == null) return;
            var heroes = GameState.HeroesOf(GameState.LocalPlayer);
            if (heroes.Count == 0) return;
            var index = heroes.IndexOf(_controller.Selected);
            var hero = heroes[(index + 1) % heroes.Count];
            _controller.Select(hero);
            _controller.FocusOn(hero.transform.position);
        }

        void ShowScenario()
        {
            if (GameState == null || CurrentMap == null) return;
            var request = new H5DialogRequest
            {
                Message = CurrentMap.Map.id + "\n\nCapture your opponents' towns and defeat their heroes.\n" +
                          "Scripted scenario objectives are not implemented."
            };
            request.Options.Add(new H5DialogOption { Label = "Close" });
            GameState.RequestDialog(request);
        }

        /// <summary>
        /// H: hero screen -- the hero's class, secondary skills with ranks and
        /// the perk roster, in one modal panel.
        /// </summary>
        void ShowHeroSkills()
        {
            if (GameState == null) return;
            var heroObj = _controller != null ? _controller.Selected as H5MapObject : null;
            var hs = heroObj != null && heroObj.IsHero ? GameState.GetHeroState(heroObj) : null;
            if (hs == null) hs = GameState.FirstLocalHeroState();
            if (hs == null) return;

            var request = new H5DialogRequest { Message = hs.SkillSummary(Database) };
            request.Options.Add(new H5DialogOption { Label = "Close" });
            GameState.RequestDialog(request);
        }

        void Update()
        {
<<<<<<< HEAD
            if (HoMM5.Core.H5Input.MutePressed() && H5Audio.Instance != null)
            {
                H5Audio.Instance.ToggleMute();
                if (GameState != null)
                    GameState.AddLog(H5Audio.Instance.Muted ? "Sound off (M toggles)." : "Sound on.");
            }
            if (CombatActive || (_menu != null && _menu.IsOpen)) return;

            // Closing the town screen by any route (its button, Esc, T) brings the map theme back.
            if (_townMusic && (_hud == null || _hud.Town == null || !_hud.Town.IsOpen))
            {
                _townMusic = false;
                ResumeMapMusic();
            }

            if (HoMM5.Core.H5Input.EndTurnPressed()) EndDay();
            if (HoMM5.Core.H5Input.SavePressed()) SaveGame();
            if (HoMM5.Core.H5Input.LoadPressed()) LoadGame();
            if (HoMM5.Core.H5Input.HeroScreenPressed()) ShowHeroSkills();
            if (HoMM5.Core.H5Input.TownScreenPressed()) ShowTownScreen();
            if (HoMM5.Core.H5Input.CancelPressed())
            {
                if (_hud != null && _hud.Town != null && _hud.Town.IsOpen) _hud.Town.Hide();
                else ReturnToMenu();
                return;
            }
            // The town screen sits over the map, so clicks on it must not also
            // reach the adventure map underneath.
            if (_hud != null && _hud.Town != null && _hud.Town.IsOpen) return;

            if (HoMM5.Core.H5Input.FloorTogglePressed()) ToggleFloor();
        }

        void ToggleFloor()
        {
            if (CombatActive || CurrentMap == null || CurrentMap.Map == null ||
                (GameState != null && GameState.HasMovingHero)) return;
            // F1 must rebuild the scene on the other plane; changing only the
            // integer leaves the current level rendered. Respect each map's
            // available floors and use the source game's GROUND=0 convention.
            var targetFloor = floor == 0 ? 1 : 0;
            if (targetFloor == 1 && !CurrentMap.Map.hasUnderground) return;
            if (targetFloor == 0 && !CurrentMap.Map.hasSurface) return;
            LoadMapAtFloor(CurrentMap.Map.id, targetFloor);
        }

        // -- music -----------------------------------------------------------

        bool _townMusic;

        /// <summary>The adventure theme for the terrain under the current map and floor.</summary>
        string MapThemeKey()
        {
            if (H5Audio.Instance == null || CurrentMap == null) return null;
            return H5Audio.Instance.FirstTrack(H5Cues.TerrainCandidates(TerrainKeyword(), floor == 1));
        }

        void ResumeMapMusic()
        {
            var key = MapThemeKey();
            if (key != null) H5Audio.Instance.PlayTheme(key);
        }

        /// <summary>The faction folder of the local player's first town, or "" when there is none.</summary>
        string LocalFactionFolder()
        {
            if (GameState == null) return "";
            var owned = GameState.OwnedTowns();
            if (owned == null || owned.Count == 0) return "";
            return H5Cues.FactionFolder(owned[0].TownType, owned[0].Faction);
        }

        /// <summary>A siege fights on its defender's faction; a field battle on the neutral theme.</summary>
        string BattleFolder(AdventureMapGameState.BattleContext context)
        {
            if (context == null || !context.IsSiege || context.Monster == null || GameState == null) return "";
            var town = GameState.GetTownState(context.Monster);
            return town != null ? H5Cues.FactionFolder(town.TownType, town.Faction) : "";
=======
            if (Input.GetKeyDown(KeyCode.F1))
            {
                floor = floor == 0 ? 1 : 0;
                if (CurrentMap?.Map != null)
                    LoadMap(CurrentMap.Map.id);
            }
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
        }

        // -- scene furniture -----------------------------------------------

        void BuildLights()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.42f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.60f, 0.66f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 420f;
        }

        void BuildCamera()
        {
            var existing = Camera.main;
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("Main Camera");
                go.transform.SetParent(transform, false);
                go.tag = "MainCamera";
                go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            _controller = go.GetComponent<AdventureMapController>();
            if (_controller == null) _controller = go.AddComponent<AdventureMapController>();
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.18f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 45f;

            // A uGUI canvas needs an EventSystem for the map list to be clickable.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if H5_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                // StandaloneInputModule reads the old Input manager. With the
                // Input System active it logs an error and swallows every click,
                // so the map list would render but never respond.
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }
        }

        static Material MakeTerrainMaterial()
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "H5_Terrain" };
            m.SetFloat("_Glossiness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            m.color = Color.white;
            return m;
        }

        static Material MakeObjectMaterial()
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "H5_Object" };
            m.SetFloat("_Glossiness", 0.25f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        static Material MakeWaterMaterial()
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "H5_Water" };
            m.SetFloat("_Glossiness", 0.85f);
            m.SetFloat("_Metallic", 0.1f);
            m.color = new Color(0.16f, 0.32f, 0.48f, 0.72f);
            m.SetFloat("_Mode", 3f);           // Transparent
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            return m;
        }

        void OnDestroy()
        {
            if (GameState != null)
            {
                GameState.Changed -= RefreshTurnHud;
                GameState.CombatRequested -= StartCombat;
                GameState.MatchEnded -= OnMatchEnded;
            }
            if (Meshes != null) Meshes.DestroyMeshes();
<<<<<<< HEAD
            if (Textures != null) Textures.Dispose();
=======
            if (Materials != null) Materials.Dispose();
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
        }
    }
}
