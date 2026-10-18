using System.Collections.Generic;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using HoMM5.UI;
using HoMM5.World;
using UnityEngine;

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

        GameObject _mapRoot;
        HoMM5Hud _hud;
        HoMM5MainMenu _mainMenu;
        AdventureMapController _controller;
        H5AdventureController _adventure;

        void Start()
        {
            Database = new GameDatabase();
            Database.Load();

            ActivePlayer = new NWorldPlayer("PLAYER_1");
            ActiveHero = new NWorldHero("Hero1", Database.DefaultStats);

            Meshes = new MeshCache();
            Materials = new H5MaterialCache();

            BuildLights();
            BuildCamera();
            _hud = gameObject.AddComponent<HoMM5Hud>();
            _hud.Controller = _controller;
            _hud.Database = Database;
            _hud.PopulateMapList(LoadMap);

            _adventure = gameObject.AddComponent<H5AdventureController>();
            _adventure.StatusChanged += RefreshStats;

            _mainMenu = gameObject.AddComponent<HoMM5MainMenu>();
            _mainMenu.Initialize(Database, LoadMap, ReturnToMainMenu);

            if (!Database.IsLoaded)
            {
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
        }

        public void LoadMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return;
            _mainMenu?.Hide();
            _adventure?.Unbind();
            if (CurrentMap != null && CurrentMap.Root != null) Destroy(CurrentMap.Root);
            Meshes.Clear();

            var builder = new MapSceneBuilder(Meshes, MakeTerrainMaterial(), MakeObjectMaterial(),
                                               MakeWaterMaterial(), Materials);
            var root = builder.Build(mapId, floor);
            if (root == null)
            {
                _hud.SetStats("Failed to load map " + mapId);
                return;
            }
            CurrentMap = builder;
            floor = builder.ActiveFloor;
            ActiveHero = CreateHeroForMap(builder);

            if (builder.Terrain != null)
                _controller.SetMapBounds(builder.Terrain.Tiles, builder.Terrain.Tiles);
            _controller.FocusOn(new Vector3((builder.Map.tileX) * 0.5f, 0f, (builder.Map.tileY) * 0.5f), 45f);
            _adventure?.Bind(builder, _controller, ActiveHero, Database);

            RefreshStats();
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
                $"{CurrentMap.Map.id}   {CurrentMap.Map.tileX}x{CurrentMap.Map.tileY}   Floor: {floorLabel}\n" +
                $"Hero Lv.{ActiveHero.Level} (XP {ActiveHero.Experience}, MP {ActiveHero.MovePoints}/{ActiveHero.GetMaxMovePoints()}, Mana {ActiveHero.ManaPoints}/{ActiveHero.GetMaxManaPoints()})\n" +
                $"objects placed {CurrentMap.ObjectsPlaced}  (no mesh {CurrentMap.ObjectsMissingMesh})  " +
                $"terrain chunks {CurrentMap.TerrainChunks}  meshes cached {Meshes.CachedCount}\n" +
                (_adventure == null || string.IsNullOrEmpty(_adventure.Status) ? "" : _adventure.Status + "\n") +
                string.Join("  ", counts));
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                floor = floor == 0 ? 1 : 0;
                if (CurrentMap?.Map != null)
                    LoadMap(CurrentMap.Map.id);
            }
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
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
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
            if (Meshes != null) Meshes.DestroyMeshes();
            if (Materials != null) Materials.Dispose();
        }
    }
}
