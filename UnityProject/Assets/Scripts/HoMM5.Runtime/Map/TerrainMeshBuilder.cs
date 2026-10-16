using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoMM5.Map
{
    /// <summary>
    /// Builds the render mesh for one terrain plane.
    ///
    /// The engine's ground is a grid of flat steps, so a naive two-triangles-per-
    /// cell grid shows a diagonal seam across every step and, at a cliff, leaves
    /// a hole you can see straight through.  Cells whose corners straddle a
    /// ground KIND boundary are therefore emitted as a vertical wall plus flat
    /// caps, and everything else stays a plain quad.
    /// </summary>
    public class TerrainMeshBuilder
    {
        public const int DefaultChunkSize = 16;

        readonly H5Terrain _t;
        readonly int _chunkSize;

        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Vector3> _nor = new List<Vector3>();
        readonly List<Color32> _col = new List<Color32>();
        readonly List<int> _tri = new List<int>();

        public TerrainMeshBuilder(H5Terrain terrain, int chunkSize = DefaultChunkSize)
        {
            _t = terrain;
            _chunkSize = Mathf.Max(1, chunkSize);
        }

        /// <summary>HoMM5 is Z-up; Unity is Y-up.  Swap the two axes.</summary>
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, z, y);

        public int ChunksPerSide => Mathf.CeilToInt(_t.Tiles / (float)_chunkSize);

        /// <summary>Build one chunk.  Returns null for an empty one.</summary>
        public Mesh BuildChunk(int chunkX, int chunkY)
        {
            var x0 = chunkX * _chunkSize;
            var y0 = chunkY * _chunkSize;
            var x1 = Mathf.Min(x0 + _chunkSize, _t.Tiles);
            var y1 = Mathf.Min(y0 + _chunkSize, _t.Tiles);
            if (x1 <= x0 || y1 <= y0) return null;

            _pos.Clear(); _uv.Clear(); _nor.Clear(); _col.Clear(); _tri.Clear();

            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    if (_t.IsCutCell(x, y)) EmitCutCell(x, y);
                    else EmitFlatCell(x, y);
                }

            if (_tri.Count == 0 || _pos.Count == 0) return null;

            var mesh = new Mesh { name = $"terrain_{chunkX}_{chunkY}" };
            mesh.indexFormat = _pos.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_pos);
            mesh.SetUVs(0, _uv);
            mesh.SetNormals(_nor);
            mesh.SetColors(_col);
            mesh.SetTriangles(_tri, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Build every chunk of the plane under <paramref name="parent"/>.</summary>
        public int BuildAll(Transform parent, Material material)
        {
            var built = 0;
            var n = ChunksPerSide;
            for (var cy = 0; cy < n; cy++)
            {
                for (var cx = 0; cx < n; cx++)
                {
                    var mesh = BuildChunk(cx, cy);
                    if (mesh == null) continue;

                    var go = new GameObject($"chunk_{cx}_{cy}");
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = material;
                    mr.receiveShadows = true;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    built++;
                }
            }
            return built;
        }

        // -- cells ---------------------------------------------------------

        void EmitFlatCell(int cx, int cy)
        {
            var h00 = _t.HeightAt(cx, cy);
            var h10 = _t.HeightAt(cx + 1, cy);
            var h01 = _t.HeightAt(cx, cy + 1);
            var h11 = _t.HeightAt(cx + 1, cy + 1);

            var i = _pos.Count;
            Add(cx, cy, h00, null);
            Add(cx + 1, cy, h10, null);
            Add(cx + 1, cy + 1, h11, null);
            Add(cx, cy + 1, h01, null);
            AddQuad(i);
        }

        void EmitCutCell(int cx, int cy)
        {
            var h00 = _t.HeightAt(cx, cy);
            var h10 = _t.HeightAt(cx + 1, cy);
            var h01 = _t.HeightAt(cx, cy + 1);
            var h11 = _t.HeightAt(cx + 1, cy + 1);
            var hi = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
            var lo = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));

            // Flat caps at both levels, then the wall between them.
            EmitCap(cx, cy, hi);
            EmitCap(cx, cy, lo);

            // A plateau dropped onto uneven ground inherits that unevenness, so
            // the wall follows each corner's own two heights rather than a mean.
            EmitWall(cx, cy, cx + 1, cy, hi, lo);
            EmitWall(cx + 1, cy, cx + 1, cy + 1, hi, lo);
            EmitWall(cx + 1, cy + 1, cx, cy + 1, hi, lo);
            EmitWall(cx, cy + 1, cx, cy, hi, lo);
        }

        void EmitCap(int cx, int cy, float h)
        {
            var i = _pos.Count;
            var n = Vector3.up;
            Add(cx, cy, h, n);
            Add(cx + 1, cy, h, n);
            Add(cx + 1, cy + 1, h, n);
            Add(cx, cy + 1, h, n);
            AddQuad(i);
        }

        void EmitWall(int ax, int ay, int bx, int by, float hi, float lo)
        {
            if (hi - lo < 0.01f) return;
            var i = _pos.Count;
            // The wall faces away from the cell interior.
            var along = ToUnity(bx - ax, by - ay, 0f);
            var normal = Vector3.Cross(along, Vector3.up).normalized;

            Add(ax, ay, hi, normal);
            Add(bx, by, hi, normal);
            Add(bx, by, lo, normal);
            Add(ax, ay, lo, normal);
            AddQuad(i);
        }

        void AddQuad(int i)
        {
            // Wound so the face is front-facing after the Z-up to Y-up swap:
            // the swap reverses orientation, which turns the source's
            // counter-clockwise winding into Unity's clockwise front face.
            _tri.Add(i); _tri.Add(i + 2); _tri.Add(i + 1);
            _tri.Add(i); _tri.Add(i + 3); _tri.Add(i + 2);
        }

        void Add(int vx, int vy, float h, Vector3? normal)
        {
            _pos.Add(ToUnity(vx, vy, h));
            _uv.Add(new Vector2(vx * 0.25f, vy * 0.25f));
            _nor.Add(normal ?? Vector3.up);
            _col.Add(ColorFor(vx, vy));
        }

        /// <summary>
        /// Tint by ground kind.  The shipped maps carry no splat weights that
        /// survive import, so kind is what the renderer has to work with: water
        /// reads blue, plateau brown, ordinary ground green, ramps olive.
        /// </summary>
        Color32 ColorFor(int x, int y)
        {
            switch (_t.GroundKind(x, y))
            {
                case 0: return new Color32(52, 92, 138, 255);
                case 2: return new Color32(142, 118, 88, 255);
                default: return _t.IsRamp(x, y) ? new Color32(126, 148, 84, 255)
                                               : new Color32(96, 132, 72, 255);
            }
        }
    }
}
