using System.IO;
using HoMM5.Core;
using HoMM5.Data;
using Newtonsoft.Json;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The decoded terrain planes of one map floor.
    ///
    /// Heights are not a smooth field: the ground is built from flat steps.
    /// 2.0 is the default ground level, 0.0 a bed dug out for water and 1.6 the
    /// shore ring the editor lays between them.  Ground flags say what KIND of
    /// ground a vertex is -- 0 water, 16 ordinary ground, 32 plateau, bit 3 a
    /// ramp -- and that kind, not steepness, is what makes an edge a cliff.
    /// </summary>
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

        public int VertexCount => Info != null ? Info.vertexCount : 0;
        public int Tiles => Info != null ? Info.tileX : 0;

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

            return t;
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

        public bool IsWater(int x, int y) => FlagAt(x, y) == FlagWater;
        public bool IsPlateau(int x, int y) => (FlagAt(x, y) & FlagPlateau) != 0;
        public bool IsRamp(int x, int y) => (FlagAt(x, y) & FlagRampBit) != 0;

        /// <summary>
        /// Ground height in world units at a tile position.
        ///
        /// A cell is a step whenever its four corners are not all at the same
        /// height and the change crosses a ground KIND boundary; sampling such a
        /// cell bilinearly would put units inside a cliff face, so those cells
        /// snap to the corner the position is nearest.
        /// </summary>
        public float SampleWorldHeight(float tileX, float tileY)
        {
            var v = VertexCount;
            var fx = Mathf.Clamp(tileX, 0f, v - 1.001f);
            var fy = Mathf.Clamp(tileY, 0f, v - 1.001f);
            var ix = Mathf.FloorToInt(fx);
            var iy = Mathf.FloorToInt(fy);
            if (ix >= Tiles || iy >= Tiles) return HeightAt(ix, iy);

            var h00 = HeightAt(ix, iy);
            var h10 = HeightAt(ix + 1, iy);
            var h01 = HeightAt(ix, iy + 1);
            var h11 = HeightAt(ix + 1, iy + 1);

            if (Mathf.Approximately(h00, h10) && Mathf.Approximately(h00, h01) && Mathf.Approximately(h00, h11))
                return h00;

            if (!IsCutCell(ix, iy))
            {
                var tx = fx - ix;
                var ty = fy - iy;
                var a = Mathf.Lerp(h00, h10, tx);
                var b = Mathf.Lerp(h01, h11, tx);
                return Mathf.Lerp(a, b, ty);
            }

            // Step: take the nearest corner rather than interpolating the drop.
            var tx2 = fx - ix;
            var ty2 = fy - iy;
            return tx2 < 0.5f ? (ty2 < 0.5f ? h00 : h01) : (ty2 < 0.5f ? h10 : h11);
        }

        /// <summary>
        /// A cut is a change of ground KIND, not steepness.  Smoothed hillsides
        /// can reach 12 units of relief and must not be cut; a 0.8 step where
        /// water meets land must be.
        /// </summary>
        public bool IsCutCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Tiles || y >= Tiles) return false;
            var k00 = GroundKind(x, y);
            var k10 = GroundKind(x + 1, y);
            var k01 = GroundKind(x, y + 1);
            var k11 = GroundKind(x + 1, y + 1);
            if (k00 == k10 && k00 == k01 && k00 == k11) return false;

            // Ramps stay walkable across a boundary.
            if (IsRamp(x, y) || IsRamp(x + 1, y) || IsRamp(x, y + 1) || IsRamp(x + 1, y + 1)) return false;

            var lo = Mathf.Min(Mathf.Min(HeightAt(x, y), HeightAt(x + 1, y)),
                               Mathf.Min(HeightAt(x, y + 1), HeightAt(x + 1, y + 1)));
            var hi = Mathf.Max(Mathf.Max(HeightAt(x, y), HeightAt(x + 1, y)),
                               Mathf.Max(HeightAt(x, y + 1), HeightAt(x + 1, y + 1)));
            return hi - lo >= 0.8f;
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
