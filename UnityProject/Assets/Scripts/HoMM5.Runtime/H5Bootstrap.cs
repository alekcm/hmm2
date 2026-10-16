using System.Collections.Generic;
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

        [Tooltip("0 = underground, 1 = surface.")]
        public int floor = 1;

        public GameDatabase Database { get; private set; }
        public MeshCache Meshes { get; private set; }
        public HoMM5.Core.H5TextureCache Textures { get; private set; }
        public MapSceneBuilder CurrentMap { get; private set; }

        GameObject _mapRoot;
        HoMM5Hud _hud;
        AdventureMapController _controller;

        void Start()
        {
            Database = new GameDatabase();
            Database.Load();

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
        }

        public void LoadMap(string mapId)
        {
            if (CurrentMap != null && CurrentMap.Root != null) Destroy(CurrentMap.Root);
            Meshes.Clear();
            if (Textures != null) Textures.Dispose();

            var builder = new MapSceneBuilder(Meshes, MakeTerrainMaterial(), MakeObjectMaterial(),
                                              MakeWaterMaterial(), Textures);
            var root = builder.Build(mapId, floor);
            if (root == null)
            {
                _hud.SetStats("Failed to load map " + mapId);
                return;
            }
            CurrentMap = builder;

            if (builder.Terrain != null)
                _controller.SetMapBounds(builder.Terrain.Tiles, builder.Terrain.Tiles);
            _controller.FocusOn(new Vector3((builder.Map.tileX) * 0.5f, 0f, (builder.Map.tileY) * 0.5f), 45f);

            RefreshStats();
        }

        void RefreshStats()
        {
            if (_hud == null || CurrentMap == null) return;
            var counts = new List<string>();
            foreach (var kv in CurrentMap.Map.objectCounts) counts.Add($"{kv.Key} {kv.Value}");
            _hud.SetStats(
                $"{CurrentMap.Map.id}   {CurrentMap.Map.tileX}x{CurrentMap.Map.tileY}\n" +
                $"objects placed {CurrentMap.ObjectsPlaced}  (no mesh {CurrentMap.ObjectsMissingMesh})\n" +
                $"terrain chunks {CurrentMap.TerrainChunks}   meshes cached {Meshes.CachedCount}\n" +
                string.Join("  ", counts));
        }

        void Update()
        {
            if (HoMM5.Core.H5Input.FloorTogglePressed())
                floor = floor == 1 ? 0 : 1;
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
        }
    }
}
