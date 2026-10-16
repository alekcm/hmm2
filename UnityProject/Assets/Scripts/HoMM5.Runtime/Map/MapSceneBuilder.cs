using System.Collections.Generic;
using HoMM5.Core;
using HoMM5.Data;
using Newtonsoft.Json;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// Turns an imported map into a scene: terrain chunks, a water plane, and one
    /// GameObject per map object placed on the terrain.
    /// </summary>
    public class MapSceneBuilder
    {
        public const float ObjectScale = 1.0f;

        readonly MeshCache _meshes;
        readonly HoMM5.Core.H5TextureCache _textures;
        readonly HoMM5.Data.GameDatabase _db;
        // Many objects share one model, so a clip is loaded once per uid.
        readonly Dictionary<string, HoMM5.Anim.H5Clip> _clips
            = new Dictionary<string, HoMM5.Anim.H5Clip>();
        readonly Material _terrainMaterial;
        readonly Material _objectMaterial;
        readonly Material _waterMaterial;

        public MapDef Map { get; private set; }
        public H5Terrain Terrain { get; private set; }
        public GameObject Root { get; private set; }

        public int ObjectsPlaced { get; private set; }
        public int ObjectsMissingMesh { get; private set; }
        public int TexturedObjects { get; private set; }
        public int TerrainTexturesBound { get; private set; }
        public int AnimatedObjects { get; private set; }
        public int TerrainChunks { get; private set; }

        public MapSceneBuilder(MeshCache meshes, Material terrainMaterial,
                               Material objectMaterial, Material waterMaterial,
                               HoMM5.Core.H5TextureCache textures = null,
                               HoMM5.Data.GameDatabase db = null)
        {
            _meshes = meshes;
            _textures = textures;
            _db = db;
            _terrainMaterial = terrainMaterial;
            _objectMaterial = objectMaterial;
            _waterMaterial = waterMaterial;
        }

        public static MapDef LoadMapDef(string mapId)
        {
            var json = H5Paths.ReadAllText(H5Paths.Maps(mapId + ".json"));
            if (json == null) return null;
            try { return JsonConvert.DeserializeObject<MapDef>(json); }
            catch (System.Exception e)
            {
                Debug.LogError("[HoMM5] bad map json " + mapId + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Build the map into a new root GameObject.</summary>
        public GameObject Build(string mapId, int floor = 1)
        {
            Map = LoadMapDef(mapId);
            if (Map == null)
            {
                Debug.LogError("[HoMM5] map not found: " + mapId);
                return null;
            }

            Root = new GameObject("Map_" + mapId);
            Terrain = H5Terrain.Load(mapId, floor == 0 ? "UndergroundTerrain" : "GroundTerrain");

            BuildTerrain(floor);
            BuildObjects(floor);

            Debug.Log($"[HoMM5] map {mapId}: {Map.tileX}x{Map.tileY} tiles, " +
                      $"{ObjectsPlaced} objects placed, {ObjectsMissingMesh} without imported mesh, " +
                      $"{TerrainChunks} terrain chunks");
            return Root;
        }

        // -- terrain -------------------------------------------------------

        void BuildTerrain(int floor)
        {
            if (Terrain == null)
            {
                Debug.LogWarning("[HoMM5] no terrain plane; building a flat placeholder");
                Terrain = null;
                return;
            }

            var terrainRoot = new GameObject("Terrain");
            terrainRoot.transform.SetParent(Root.transform, false);
            var builder = new TerrainMeshBuilder(Terrain);
            TerrainChunks = builder.BuildAll(terrainRoot.transform, TerrainMaterials());

            if (Terrain.AnyWater && _waterMaterial != null)
                BuildWaterPlane(terrainRoot.transform);
        }

        /// <summary>
        /// One material per painted ground layer, in the order the mesh's
        /// submeshes use. Falls back to the single tinted material when the
        /// import carries no layers.
        /// </summary>
        Material[] TerrainMaterials()
        {
            var n = Terrain.LayerCount;
            if (n == 0 || _textures == null) return new[] { _terrainMaterial };

            var mats = new Material[n];
            TerrainTexturesBound = 0;
            for (var i = 0; i < n; i++)
            {
                var src = Terrain.Layers[i] != null ? Terrain.Layers[i].texture : null;
                mats[i] = !string.IsNullOrEmpty(src)
                    ? _textures.MaterialFor(src) ?? _terrainMaterial
                    : _terrainMaterial;
                if (!string.IsNullOrEmpty(src) && mats[i] != _terrainMaterial)
                    TerrainTexturesBound++;
            }
            return mats;
        }

        void BuildWaterPlane(Transform parent)
        {
            var tiles = Terrain.Tiles;
            var mesh = new Mesh { name = "water" };
            var v = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(tiles, 0f, 0f),
                new Vector3(tiles, 0f, tiles),
                new Vector3(0f, 0f, tiles),
            };
            mesh.vertices = v;
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Water");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, Terrain.WaterLevel, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _waterMaterial;
        }

        // -- objects -------------------------------------------------------

        void BuildObjects(int floor)
        {
            if (Map.objects == null) return;
            var objectsRoot = new GameObject("Objects");
            objectsRoot.transform.SetParent(Root.transform, false);

            for (var i = 0; i < Map.objects.Count; i++)
            {
                var def = Map.objects[i];
                if (def.floor != floor) continue;
                if (PlaceObject(objectsRoot.transform, def, i)) ObjectsPlaced++;
            }
        }

        bool PlaceObject(Transform parent, MapObjectDef def, int index)
        {
            var meshes = def.geometryUid != null ? _meshes.Get(def.geometryUid) : null;
            if (meshes == null || meshes.Length == 0)
            {
                ObjectsMissingMesh++;
                return false;
            }

            var go = new GameObject(ObjectName(def, index));
            go.transform.SetParent(parent, false);

            // Map positions are tile coordinates on the ground plane; the height
            // comes from the terrain, not from the file's z (which is almost
            // always 0 for statics).
            var height = Terrain != null ? Terrain.SampleWorldHeight(def.x, def.y) : 0f;
            go.transform.localPosition = TerrainMeshBuilder.ToUnity(def.x, def.y, height);
            // A map rotation is a turn about the vertical axis.
            go.transform.localRotation = Quaternion.Euler(0f, def.rot, 0f);
            go.transform.localScale = Vector3.one * ObjectScale;

            var mats = new List<Material>(meshes.Length);
            for (var m = 0; m < meshes.Length; m++)
                mats.Add(MaterialForMesh(def, m) ?? _objectMaterial);

            // An object whose model names an adventure-map clip is rigged and
            // animated; anything else stays a static mesh.
            var clip = ClipFor(def);
            if (clip != null && HoMM5.Anim.H5Rig.Build(go, new List<Mesh>(meshes), mats, clip) != null)
            {
                AnimatedObjects++;
                go.AddComponent<H5MapObject>().Attach(def, this);
                return true;
            }

            for (var m = 0; m < meshes.Length; m++)
            {
                var child = new GameObject(meshes[m].name);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[m];
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[m];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            go.AddComponent<H5MapObject>().Attach(def, this);
            return true;
        }

        /// <summary>
        /// The material for the m-th mesh of an object. HoMM5 lists one texture
        /// per model material in the same order as the meshes, so they line up
        /// directly; when a model has fewer materials than meshes the last one is
        /// reused rather than falling back to blank.
        /// </summary>
        Material MaterialForMesh(MapObjectDef def, int m)
        {
            if (_textures == null) return null;

            // Prefer the full material record, which carries the alpha mode; fall
            // back to the bare texture list for an import made before materials
            // were exported.
            if (def.materials != null && def.materials.Count > 0)
            {
                var mi = m < def.materials.Count ? m : def.materials.Count - 1;
                var md = def.materials[mi];
                if (md != null && !string.IsNullOrEmpty(md.texture))
                {
                    var mat = _textures.MaterialFor(md.texture, md);
                    if (mat != null) TexturedObjects++;
                    return mat;
                }
            }

            if (def.textures == null || def.textures.Count == 0) return null;
            var idx = m < def.textures.Count ? m : def.textures.Count - 1;
            var src = def.textures[idx];
            if (string.IsNullOrEmpty(src)) return null;
            var fallback = _textures.MaterialFor(src);
            if (fallback != null) TexturedObjects++;
            return fallback;
        }

        /// <summary>
        /// The adventure-map idle for an object's model, or null when the model
        /// is not animated. Cached per geometry uid: a map holds hundreds of
        /// objects sharing a handful of models.
        /// </summary>
        HoMM5.Anim.H5Clip ClipFor(MapObjectDef def)
        {
            if (_db == null || string.IsNullOrEmpty(def.geometryUid)) return null;
            if (!_db.ModelAnimations.TryGetValue(def.geometryUid, out var ma)) return null;
            if (ma == null || ma.advmap == null || ma.advmap.Count == 0) return null;

            string uid;
            if (!ma.advmap.TryGetValue("idle00", out uid))
            {
                // Not every set names idle00; take whatever it has.
                foreach (var kv in ma.advmap) { uid = kv.Value; break; }
            }
            if (string.IsNullOrEmpty(uid)) return null;

            HoMM5.Anim.H5Clip clip;
            if (_clips.TryGetValue(uid, out clip)) return clip;
            clip = HoMM5.Anim.H5Anim.Load(uid);
            _clips[uid] = clip;   // cached even when null, so a miss is looked up once
            return clip;
        }

        static string ObjectName(MapObjectDef def, int index)
        {
            var label = def.kind;
            if (!string.IsNullOrEmpty(def.creature)) label += "_" + def.creature;
            else if (!string.IsNullOrEmpty(def.artifact)) label += "_" + def.artifact;
            else if (!string.IsNullOrEmpty(def.townType)) label += "_" + def.townType;
            if (!string.IsNullOrEmpty(def.name)) label += " (" + def.name + ")";
            return index + "_" + label;
        }

        /// <summary>World position of a tile, on the terrain surface.</summary>
        public Vector3 TileToWorld(float tileX, float tileY)
        {
            var h = Terrain != null ? Terrain.SampleWorldHeight(tileX, tileY) : 0f;
            return Root != null
                ? Root.transform.TransformPoint(TerrainMeshBuilder.ToUnity(tileX, tileY, h))
                : TerrainMeshBuilder.ToUnity(tileX, tileY, h);
        }
    }
}
