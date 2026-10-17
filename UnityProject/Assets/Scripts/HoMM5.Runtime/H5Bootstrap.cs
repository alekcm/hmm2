using System.Collections.Generic;
using HoMM5.Combat;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using HoMM5.UI;
using UnityEngine;

namespace HoMM5
{
    /// <summary>
    /// Entry point.  Loads the imported game data, builds a camera, lights and a
    /// HUD, then loads the first map on the list.
    ///
    /// Nothing here needs a hand-authored scene asset beyond an empty scene with
    /// this component on one GameObject -- which is what
    /// <c>Assets/Scenes/AdventureMap.unity</c> contains.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class H5Bootstrap : MonoBehaviour
    {
        [Tooltip("Load this map on start.  Empty means the first one in the index.")]
        public string initialMapId = "";

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

        GameObject _mapRoot;
        HoMM5Hud _hud;
        AdventureMapController _controller;

        // One battle at a time: its scene lives in its own root while the
        // adventure map is hidden under it.
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
            GameState = new AdventureMapGameState();
            GameState.Database = Database;
            GameState.SetBaseHeroMovement(Database.Rules != null
                ? Database.Rules.baseHeroMovement : AdventureMapGameState.DefaultDailyHeroMovement);
            GameState.Changed += RefreshTurnHud;
            GameState.CombatRequested += StartCombat;
            CombatRules.Configure(Database.Rules != null ? Database.Rules.combat : null);

            Meshes = new MeshCache();

            Textures = new HoMM5.Core.H5TextureCache(Shader.Find("Standard"));
            if (!Textures.LoadManifest())
                Debug.LogWarning("[HoMM5] no texture manifest found -- objects will be untextured.\n" +
                                 HoMM5.Data.GameDatabase.MissingImportMessage());

            BuildLights();
            BuildCamera();
            _hud = gameObject.AddComponent<HoMM5Hud>();
            _hud.Controller = _controller;
            _hud.Database = Database;
            _hud.GameState = GameState;
            _hud.EndDayRequested = EndDay;
            _hud.SaveRequested = SaveGame;
            _hud.LoadRequested = LoadGame;
            RefreshTurnHud();
            // PopulateMapList needs the HUD's Start to have built its canvas.
            _hud.PopulateMapList(LoadMap);

            if (!Database.IsLoaded)
            {
                _hud.SetStats("Import not found.\nRun: python -m homm5import.cli all\n(see tools/homm5_import/README.md)");
                return;
            }

            var id = initialMapId;
            if (string.IsNullOrEmpty(id) && Database.MapIndex.Count > 0) id = Database.MapIndex[0].id;
            if (!string.IsNullOrEmpty(id)) LoadMap(id);
            GameState.AddLog("Left-click selects; right-click moves the selected hero. F5 saves, F9 loads.");
        }

        public void LoadMap(string mapId)
        {
            var map = MapSceneBuilder.LoadMapDef(mapId);
            LoadMapAtFloor(mapId, map != null ? map.initialFloor : floor);
        }

        void LoadMapAtFloor(string mapId, int targetFloor, bool restoredState = false)
        {
            if (GameState != null && GameState.HasMovingHero) return;
            var previousMapId = CurrentMap != null && CurrentMap.Map != null ? CurrentMap.Map.id : "";
            var previousFloor = floor;
            var preserveScenarioState = string.Equals(previousMapId, mapId,
                System.StringComparison.OrdinalIgnoreCase) && previousFloor != targetFloor;
            floor = targetFloor == 1 ? 1 : 0;
            if (_controller != null) _controller.Select(null);
            if (CurrentMap != null && CurrentMap.Root != null) Destroy(CurrentMap.Root);
            Meshes.Clear();
            if (Textures != null) Textures.Dispose();

            var builder = new MapSceneBuilder(Meshes, MakeTerrainMaterial(), MakeObjectMaterial(),
                                              MakeWaterMaterial(), Textures, Database);
            var root = builder.Build(mapId, floor);
            if (root == null)
            {
                _hud.SetStats("Failed to load map " + mapId);
                return;
            }
            CurrentMap = builder;
            // A restored save carries its own day, resources and world changes;
            // a floor switch preserves the running scenario; anything else is a
            // brand-new scenario.
            var newScenario = !restoredState && !preserveScenarioState;
            GameState.BindMap(builder, newScenario, localPlayerId, Database.Rules,
                              Database.TownBuildings, gameDifficulty);

            _controller.SetMapBounds(builder.Map.tileX, builder.Map.tileY);
            // TileToWorld accounts for both the horizontal tile scale and the
            // terrain height at the map center.
            _controller.FocusOn(builder.TileToWorld(builder.Map.tileX * 0.5f,
                                                    builder.Map.tileY * 0.5f), 45f);

            _hud.AttachMap(builder);
            RefreshStats();
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
                GameState.AddLog("No quick save found (F5 to save).");
                return;
            }
            // Restore the world state first: BindMap applies ownership and
            // hero positions from it when the scene is rebuilt.
            GameState.RestoreState(save);
            var mapId = string.IsNullOrEmpty(save.mapId) && CurrentMap != null && CurrentMap.Map != null
                ? CurrentMap.Map.id : save.mapId;
            if (string.IsNullOrEmpty(mapId)) return;
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
            _combat.StartBattle(state, scene, _controller != null ? _controller.GetComponent<Camera>() : null);
        }

        void OnCombatFinished(CombatResult result)
        {
            if (_combat == null) return;
            _combat.BattleFinished -= OnCombatFinished;

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

        void EndDay()
        {
            if (GameState == null || GameState.HasMovingHero || CombatActive) return;
            GameState.EndDay();
            RefreshStats();
        }

        void RefreshTurnHud()
        {
            if (_hud != null && GameState != null)
            {
                _hud.SetTurnDate(GameState.Day, GameState.Week, GameState.Month);
                _hud.SetResourceSummary(GameState.Resources != null ? GameState.Resources.ToString() : "");
            }
        }

        void RefreshStats()
        {
            if (_hud == null || CurrentMap == null) return;
            var counts = new List<string>();
            foreach (var kv in CurrentMap.Map.objectCounts) counts.Add($"{kv.Key} {kv.Value}");
            _hud.SetStats(
                $"{CurrentMap.Map.id}   {CurrentMap.Map.tileX}x{CurrentMap.Map.tileY}\n" +
                $"objects placed {CurrentMap.ObjectsPlaced}  (no mesh {CurrentMap.ObjectsMissingMesh})\n" +
                $"terrain chunks {CurrentMap.TerrainChunks} (ground textures {CurrentMap.TerrainTexturesBound})\n" +
                $"meshes cached {Meshes.CachedCount}  animated {CurrentMap.AnimatedObjects}\n" +
                string.Join("  ", counts));
        }

        void Update()
        {
            if (CombatActive) return;
            if (HoMM5.Core.H5Input.SavePressed()) SaveGame();
            if (HoMM5.Core.H5Input.LoadPressed()) LoadGame();

            if (!HoMM5.Core.H5Input.FloorTogglePressed() || CurrentMap == null || CurrentMap.Map == null)
                return;

            // F1 must rebuild the scene on the other plane; changing only the
            // integer leaves the current level rendered. Respect each map's
            // available floors and use the source game's GROUND=0 convention.
            var targetFloor = floor == 0 ? 1 : 0;
            if (targetFloor == 1 && !CurrentMap.Map.hasUnderground) return;
            if (targetFloor == 0 && !CurrentMap.Map.hasSurface) return;
            LoadMapAtFloor(CurrentMap.Map.id, targetFloor);
        }

        // -- scene furniture -----------------------------------------------

        void BuildLights()
        {
            var go = new GameObject("Sun");
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
            }
            if (Meshes != null) Meshes.DestroyMeshes();
        }
    }
}
