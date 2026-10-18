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
        readonly Material _terrainMaterial;
        readonly Material _objectMaterial;
        readonly Material _waterMaterial;

        public MapDef Map { get; private set; }
        public H5Terrain Terrain { get; private set; }
        public NWorldPathfinder Pathfinder { get; private set; }
        public GameObject Root { get; private set; }
        public int ActiveFloor { get; private set; }

        public int ObjectsPlaced { get; private set; }
        public int ObjectsMissingMesh { get; private set; }
        public int TerrainChunks { get; private set; }

        public MapSceneBuilder(MeshCache meshes, Material terrainMaterial,
                               Material objectMaterial, Material waterMaterial)
        {
            _meshes = meshes;
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

        /// <summary>
        /// Build the map into a new root GameObject.
        /// Matches <c>common.lua</c> and <c>NWorld::IsTilePassable</c> (<c>0x005e11a0</c>):
        /// <c>floor = 0</c> is surface (<c>GROUND</c>), <c>floor = 1</c> is underground (<c>UNDERGROUND</c>).
        /// </summary>
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

            Root = new GameObject("Map_" + mapId);
            var planeName = floor == 1 ? "UndergroundTerrain" : "GroundTerrain";
            Terrain = H5Terrain.Load(mapId, planeName);
            if (Terrain == null && floor == 1)
                Terrain = H5Terrain.Load(mapId, "GroundTerrain");

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
            var builder = new TerrainMeshBuilder(Terrain);
            TerrainChunks = builder.BuildAll(terrainRoot.transform, _terrainMaterial);

            if (Terrain.AnyWater && _waterMaterial != null)
                BuildWaterPlane(terrainRoot.transform);
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
            // In .xdb AdvMapDesc files, <Rot> is stored in radians (e.g. 1.5708, 3.14159, 4.71239).
            var yawDegrees = (Mathf.Abs(def.rot) > 0.0001f && Mathf.Abs(def.rot) <= 6.2832f)
                ? def.rot * Mathf.Rad2Deg
                : def.rot;
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            go.transform.localScale = Vector3.one * ObjectScale;

            var combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            var hasBounds = false;
            for (var m = 0; m < meshes.Length; m++)
            {
                var child = new GameObject(meshes[m].name);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[m];
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _objectMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                if (!hasBounds)
                {
                    combinedBounds = meshes[m].bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(meshes[m].bounds);
                }
            }

            if (hasBounds)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = combinedBounds.center;
                col.size = Vector3.Max(combinedBounds.size, new Vector3(0.5f, 0.5f, 0.5f));
            }

            go.AddComponent<H5MapObject>().Attach(def, this);
            return true;
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
