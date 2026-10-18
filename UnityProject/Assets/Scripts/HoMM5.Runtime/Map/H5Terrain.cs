using System.IO;
using HoMM5.Core;
using HoMM5.Data;
using Newtonsoft.Json;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>Imported heights, gameplay flags and painted coverage planes.</summary>
    public class H5Terrain
    {
        public const float DefaultGroundLevel = 2.0f;
        public const float ShoreLevel = 1.6f;
        public const float WaterFillLevel = 1.5f;

        public const byte FlagWater = 0;
        public const byte FlagGround = 16;
        public const byte FlagPlateau = 32;
        public const byte FlagRampBit = 8;

        public TerrainInfo Info { get; private set; }
        public float[] Heights { get; private set; }
        public byte[] Flags { get; private set; }
        public byte[] Passability { get; private set; }

        /// <summary>Painted ground textures, in paint order. May be empty.</summary>
        public System.Collections.Generic.List<TerrainLayerInfo> Layers
        {
            get; private set;
        }

        // Coverage planes, one byte per vertex per layer, concatenated.
        byte[] _layerWeights;
        int _layerStride;

        public int LayerCount => Layers != null ? Layers.Count : 0;

        public int VertexCount => Info != null ? Info.vertexCount : 0;
        public int Tiles => Info != null ? Info.tileX : 0;
        public int TilesX => Info != null ? Info.tileX : 0;
        public int TilesY => Info != null ? Info.tileY : 0;

        /// <summary>Whether a map tile can be entered according to its imported plane.</summary>
        public bool IsPassable(int x, int y)
        {
            if (x < 0 || y < 0 || x > TilesX || y > TilesY) return false;
            var stride = TilesX + 1;
            var index = y * stride + x;
            if (Passability != null && index >= 0 && index < Passability.Length)
                return Passability[index] != 0;
            return FlagAt(x, y) != FlagWater;
        }

        public static H5Terrain Load(string mapId, string plane)
        {
            var infoJson = H5Paths.ReadAllText(H5Paths.Terrain(mapId, plane + ".json"));
            if (infoJson == null) return null;

            var t = new H5Terrain { Info = JsonConvert.DeserializeObject<TerrainInfo>(infoJson) };
            var n = t.Info.vertexCount * t.Info.vertexCount;

            var heights = H5Paths.ReadAllBytes(H5Paths.Terrain(mapId, plane + ".heights"));
            if (heights == null || heights.Length < n * 4)
            {
                Debug.LogWarning($"[HoMM5] no height plane for {mapId}/{plane}");
                return null;
            }
            t.Heights = new float[n];
            System.Buffer.BlockCopy(heights, 0, t.Heights, 0, n * 4);

            var flags = H5Paths.ReadAllBytes(H5Paths.Terrain(mapId, plane + ".flags"));
            if (flags != null && flags.Length >= n)
            {
                t.Flags = new byte[n];
                System.Buffer.BlockCopy(flags, 0, t.Flags, 0, n);
            }

            var pass = H5Paths.ReadAllBytes(H5Paths.Terrain(mapId, plane + ".passability"));
            if (pass != null && pass.Length >= n)
            {
                t.Passability = new byte[n];
                System.Buffer.BlockCopy(pass, 0, t.Passability, 0, n);
            }

            LoadLayers(t, mapId, plane);
            return t;
        }

        /// <summary>
        /// Read the painted-texture layers. Optional: an import from before they
        /// were decoded simply has none, and the terrain falls back to one
        /// material as it did before.
        /// </summary>
        static void LoadLayers(H5Terrain t, string mapId, string plane)
        {
            var json = H5Paths.ReadAllText(H5Paths.Terrain(mapId, plane + ".layers.json"));
            if (json == null) return;

            TerrainLayersFile file;
            try { file = JsonConvert.DeserializeObject<TerrainLayersFile>(json); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HoMM5] bad terrain layers for {mapId}/{plane}: {e.Message}");
                return;
            }
            if (file == null || file.layers == null || file.layers.Count == 0) return;

            var perPlane = file.vertexStride * file.vertexStride;
            var raw = H5Paths.ReadAllBytes(H5Paths.Terrain(mapId, plane + ".layers"));
            if (raw == null || raw.Length < perPlane * file.layers.Count)
            {
                Debug.LogWarning($"[HoMM5] terrain layer weights missing or short for " +
                                 $"{mapId}/{plane}");
                return;
            }
            t.Layers = file.layers;
            t._layerStride = file.vertexStride;
            t._layerWeights = raw;
        }

        /// <summary>
        /// Which painted layer shows at a tile: the highest-priority one that is
        /// more than half opaque there, else the base layer.
        ///
        /// The game blends these smoothly; picking one per tile is the cheap
        /// approximation that still reads correctly from above.
        /// </summary>
        public int DominantLayerAt(int x, int y)
        {
            if (LayerCount == 0) return 0;
            if (x < 0 || y < 0 || x >= _layerStride || y >= _layerStride) return 0;

            var at = y * _layerStride + x;
            var best = 0;
            var bestPriority = int.MinValue;
            for (var i = 0; i < LayerCount; i++)
            {
                if (_layerWeights[i * _layerStride * _layerStride + at] <= 127) continue;
                var pri = Layers[i] != null ? Layers[i].priority : 0;
                if (pri > bestPriority)
                {
                    bestPriority = pri;
                    best = i;
                }
            }
            return best;
        }

        public float HeightAt(int x, int y)
        {
            var v = VertexCount;
            x = Mathf.Clamp(x, 0, v - 1);
            y = Mathf.Clamp(y, 0, v - 1);
            return Heights[y * v + x];
        }

        public byte FlagAt(int x, int y)
        {
            if (Flags == null) return FlagGround;
            var v = VertexCount;
            x = Mathf.Clamp(x, 0, v - 1);
            y = Mathf.Clamp(y, 0, v - 1);
            return Flags[y * v + x];
        }

        public bool HasPassability => Passability != null && Passability.Length >= VertexCount * VertexCount;

        /// <summary>
        /// Raw ``GroundTerrain`` tag 0x0f value.  NWorld checks this value as
        /// the tile's passability flag; it is not the visual ground flag.
        /// </summary>
        public byte PassabilityAt(int x, int y)
        {
            if (!HasPassability) return 0;
            var v = VertexCount;
            x = Mathf.Clamp(x, 0, v - 1);
            y = Mathf.Clamp(y, 0, v - 1);
            return Passability[y * v + x];
        }

        /// <summary>
        /// The confirmed passability-flag part of NWorld::IsTilePassable.
        /// Walkable-state changes from map objects are applied by Pathfinder.
        /// </summary>
        public bool IsTilePassable(int x, int y)
        {
            var flag = PassabilityAt(x, y);
            return flag == 1 || flag == 4;
        }

        public bool IsWater(int x, int y) => FlagAt(x, y) == FlagWater;
        public bool IsPlateau(int x, int y) => (FlagAt(x, y) & FlagPlateau) != 0;
        public bool IsRamp(int x, int y) => (FlagAt(x, y) & FlagRampBit) != 0;

        /// <summary>Sample the same two triangles emitted by TerrainMeshBuilder.</summary>
        public float SampleWorldHeight(float tileX, float tileY)
        {
            if (VertexCount < 2) return VertexCount == 1 ? HeightAt(0, 0) : 0f;
            var fx = Mathf.Clamp(tileX, 0f, VertexCount - 1f);
            var fy = Mathf.Clamp(tileY, 0f, VertexCount - 1f);
            var ix = Mathf.Min(Mathf.FloorToInt(fx), VertexCount - 2);
            var iy = Mathf.Min(Mathf.FloorToInt(fy), VertexCount - 2);
            var tx = fx - ix;
            var ty = fy - iy;
            var h00 = HeightAt(ix, iy);
            var h10 = HeightAt(ix + 1, iy);
            var h01 = HeightAt(ix, iy + 1);
            var h11 = HeightAt(ix + 1, iy + 1);
            return ty <= tx
                ? h00 + (h10 - h00) * tx + (h11 - h10) * ty
                : h00 + (h11 - h01) * tx + (h01 - h00) * ty;
        }

        /// <summary>Global derivatives keep lighting continuous at chunk edges.</summary>
        public Vector3 NormalAt(int x, int y)
        {
            var left = Mathf.Max(0, x - 1);
            var right = Mathf.Min(VertexCount - 1, x + 1);
            var down = Mathf.Max(0, y - 1);
            var up = Mathf.Min(VertexCount - 1, y + 1);
            var dx = (HeightAt(right, y) - HeightAt(left, y)) /
                     (Mathf.Max(1, right - left) * TerrainMeshBuilder.TileSize);
            var dz = (HeightAt(x, up) - HeightAt(x, down)) /
                     (Mathf.Max(1, up - down) * TerrainMeshBuilder.TileSize);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        public byte LayerWeightAt(int layer, int x, int y)
        {
            if (_layerWeights == null || layer < 0 || layer >= LayerCount || _layerStride < 1) return 0;
            x = Mathf.Clamp(x, 0, _layerStride - 1);
            y = Mathf.Clamp(y, 0, _layerStride - 1);
            return _layerWeights[layer * _layerStride * _layerStride + y * _layerStride + x];
        }

        /// <summary>Water / ground / plateau -- the KIND, ignoring ramps.</summary>
        public int GroundKind(int x, int y)
        {
            var f = FlagAt(x, y);
            if (f == FlagWater) return 0;
            if ((f & FlagPlateau) != 0) return 2;
            return 1;
        }

        /// <summary>Where a water plane should sit, just under the shore ring.</summary>
        public float WaterLevel => WaterFillLevel;

        public bool AnyWater
        {
            get
            {
                if (Info == null) return false;
                return Info.waterVertices > 0;
            }
        }
    }
}
