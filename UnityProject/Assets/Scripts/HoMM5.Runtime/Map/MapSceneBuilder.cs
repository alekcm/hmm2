using System.Collections.Generic;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.World;
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
        readonly H5MaterialCache _materialCache;

        public MapDef Map { get; private set; }
        public H5Terrain Terrain { get; private set; }
        public NWorldPathfinder Pathfinder { get; private set; }
        public GameObject Root { get; private set; }
        public int ActiveFloor { get; private set; }

        public int ObjectsPlaced { get; private set; }
        public int ObjectsMissingMesh { get; private set; }
        public int TexturedObjects { get; private set; }
        public int TerrainTexturesBound { get; private set; }
        public int AnimatedObjects { get; private set; }
        public int TerrainChunks { get; private set; }
        public List<H5MapObject> PlacedObjects { get; } = new List<H5MapObject>();

        public MapSceneBuilder(MeshCache meshes, Material terrainMaterial,
                               Material objectMaterial, Material waterMaterial,
<<<<<<< HEAD
                               HoMM5.Core.H5TextureCache textures = null,
                               HoMM5.Data.GameDatabase db = null)
=======
                               H5MaterialCache materialCache = null)
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
        {
            _meshes = meshes;
            _textures = textures;
            _db = db;
            _terrainMaterial = terrainMaterial;
            _objectMaterial = objectMaterial;
            _waterMaterial = waterMaterial;
            _materialCache = materialCache ?? new H5MaterialCache();
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

<<<<<<< HEAD
        /// <summary>Build the map into a new root GameObject.</summary>
=======
        /// <summary>
        /// Build the map into a new root GameObject.
        /// Matches <c>common.lua</c> and <c>NWorld::IsTilePassable</c> (<c>0x005e11a0</c>):
        /// <c>floor = 0</c> is surface (<c>GROUND</c>), <c>floor = 1</c> is underground (<c>UNDERGROUND</c>).
        /// </summary>
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
        public GameObject Build(string mapId, int floor = 0)
        {
            Map = LoadMapDef(mapId);
            if (Map == null)
            {
                Debug.LogError("[HoMM5] map not found: " + mapId);
                return null;
            }

            if (Map.objects != null && Map.objects.Count > 0)
            {
                var hasRequestedFloor = false;
                for (var i = 0; i < Map.objects.Count; i++)
                {
                    if (Map.objects[i].floor == floor)
                    {
                        hasRequestedFloor = true;
                        break;
                    }
                }
                if (!hasRequestedFloor)
                    floor = Map.objects[0].floor;
            }
            ActiveFloor = floor;
            PlacedObjects.Clear();
            ObjectsPlaced = 0;
            ObjectsMissingMesh = 0;
            TerrainChunks = 0;

            Root = new GameObject("Map_" + mapId);
<<<<<<< HEAD
            // Shipped script constants: GROUND=0, UNDERGROUND=1.
            Terrain = H5Terrain.Load(mapId, floor == 1 ? "UndergroundTerrain" : "GroundTerrain");
=======
            var planeName = floor == 1 ? "UndergroundTerrain" : "GroundTerrain";
            Terrain = H5Terrain.Load(mapId, planeName);
            if (Terrain == null && floor == 1)
                Terrain = H5Terrain.Load(mapId, "GroundTerrain");
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

            BuildTerrain(floor);
            BuildObjects(floor);
            Pathfinder = new NWorldPathfinder(Map.tileX, Map.tileY, Terrain, Map, floor);

            Debug.Log($"[HoMM5] map {mapId} (floor {floor}): {Map.tileX}x{Map.tileY} tiles, " +
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
            var rendering = terrainRoot.AddComponent<H5TerrainRendering>();
            var blended = rendering.Configure(Terrain, _textures);
            var materials = blended != null ? new[] { blended } : TerrainMaterials();
            if (blended != null) TerrainTexturesBound = rendering.TexturesBound;
            var builder = new TerrainMeshBuilder(Terrain, TerrainMeshBuilder.DefaultChunkSize, blended != null);
            TerrainChunks = builder.BuildAll(terrainRoot.transform, materials);

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
                // The plane spans the same world extent as the terrain mesh.
                new Vector3(0f, 0f, 0f),
                new Vector3(tiles * TerrainMeshBuilder.TileSize, 0f, 0f),
                new Vector3(tiles * TerrainMeshBuilder.TileSize, 0f, tiles * TerrainMeshBuilder.TileSize),
                new Vector3(0f, 0f, tiles * TerrainMeshBuilder.TileSize),
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
<<<<<<< HEAD
            // A map rotation is a turn about the vertical axis. The map files
            // store it in radians (pi for a half-turn), Unity's Euler wants
            // degrees -- without the conversion every rotated object faces
            // within a few degrees of its authored direction.
            go.transform.localRotation = Quaternion.Euler(0f, def.rot * Mathf.Rad2Deg, 0f);
            go.transform.localScale = Vector3.one * ObjectScale;

            var mats = new List<Material>(meshes.Length);
            for (var m = 0; m < meshes.Length; m++)
                mats.Add(MaterialForMesh(def, m) ?? _objectMaterial);

            // An object whose model names an adventure-map clip is rigged and
            // animated; anything else stays a static mesh. Models with a move
            // clip get both, so a walking hero can switch between them.
            var idle = ClipFor(def, "idle");
            if (idle != null && HoMM5.Anim.H5Rig.Build(go, new List<Mesh>(meshes), mats, idle) is Animation anim)
            {
                AnimatedObjects++;
                var mapObject = go.AddComponent<H5MapObject>();
                mapObject.Attach(def, this);
                var move = ClipFor(def, "move");
                if (move != null)
                {
                    var baked = HoMM5.Anim.H5Anim.Bake(move, "Rig");
                    if (baked != null)
                    {
                        baked.name = baked.name + "#move";
                        anim.AddClip(baked, baked.name);
                        mapObject.AttachAnimation(anim, anim.clip != null ? anim.clip.name : null, baked.name);
                    }
                }
                return true;
            }

=======
            // In .xdb AdvMapDesc files, <Rot> is stored in radians (e.g. 1.5708, 3.14159, 4.71239).
            var yawDegrees = (Mathf.Abs(def.rot) > 0.0001f && Mathf.Abs(def.rot) <= 6.2832f)
                ? def.rot * Mathf.Rad2Deg
                : def.rot;
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            go.transform.localScale = Vector3.one * ObjectScale;

            var combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            var hasBounds = false;
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
            for (var m = 0; m < meshes.Length; m++)
            {
                var child = new GameObject(meshes[m].name);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[m];
                var mr = child.AddComponent<MeshRenderer>();
<<<<<<< HEAD
                mr.sharedMaterial = mats[m];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
=======
                var materialDef = MaterialForSlice(def.materials, m, meshes.Count);
                mr.sharedMaterial = _materialCache.Get(materialDef, _objectMaterial);
                H5MaterialCache.ConfigureRenderer(mr, materialDef);

                if (!hasBounds)
                {
                    combinedBounds = meshes[m].bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(meshes[m].bounds);
                }
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
            }

            if (hasBounds)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = combinedBounds.center;
                col.size = Vector3.Max(combinedBounds.size, new Vector3(0.5f, 0.5f, 0.5f));
            }

            var mapObject = go.AddComponent<H5MapObject>();
            mapObject.Attach(def, this);
            PlacedObjects.Add(mapObject);
            return true;
        }

        /// <summary>
<<<<<<< HEAD
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
        /// One adventure-map clip (idle or move) for an object's model, or null
        /// when the model is not animated. Cached per clip uid: a map holds
        /// hundreds of objects sharing a handful of models.
        /// </summary>
        HoMM5.Anim.H5Clip ClipFor(MapObjectDef def, string kind)
        {
            if (_db == null || string.IsNullOrEmpty(def.geometryUid)) return null;
            if (!_db.ModelAnimations.TryGetValue(def.geometryUid, out var ma)) return null;
            if (ma == null || ma.advmap == null || ma.advmap.Count == 0) return null;

            string uid = null;
            if (kind == "move")
            {
                // Clip sets name the walk cycle "move" or "moveNN".
                foreach (var kv in ma.advmap)
                    if (kv.Key == "move" || kv.Key.StartsWith("move"))
                    { uid = kv.Value; break; }
            }
            else
            {
                if (!ma.advmap.TryGetValue("idle00", out uid))
                {
                    // Not every set names idle00; take whatever it has that is
                    // not a move cycle (that one is attached separately).
                    foreach (var kv in ma.advmap)
                        if (!kv.Key.StartsWith("move")) { uid = kv.Value; break; }
                }
            }
            if (string.IsNullOrEmpty(uid)) return null;

            HoMM5.Anim.H5Clip clip;
            if (_clips.TryGetValue(uid, out clip)) return clip;
            clip = HoMM5.Anim.H5Anim.Load(uid);
            _clips[uid] = clip;   // cached even when null, so a miss is looked up once
            return clip;
=======
        /// Bind only source relationships that are unambiguous.  A Model with
        /// one material can use it for every emitted geometry slice.  Otherwise
        /// the authoritative Model.Materials array must have exactly the same
        /// number of non-empty GLB slices for ordinal binding; an ambiguous
        /// count falls back instead of guessing by mesh name or texture name.
        /// </summary>
        static MaterialDef MaterialForSlice(List<MaterialDef> definitions, int index, int sliceCount)
        {
            if (definitions == null || definitions.Count == 0) return null;
            if (definitions.Count == 1) return definitions[0];
            if (definitions.Count != sliceCount) return null;
            return index >= 0 && index < definitions.Count ? definitions[index] : null;
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
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
