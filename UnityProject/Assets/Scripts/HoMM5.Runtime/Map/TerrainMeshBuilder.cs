using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoMM5.Map
{
    /// <summary>
    /// A continuous height-field mesh. Flags are gameplay metadata, not a
    /// license to replace a height cell with a sealed box.
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

        // Triangles bucketed by painted-texture layer, so one chunk can carry
        // several ground textures as submeshes. Slot i always means layer i of
        // the terrain, so every chunk shares one material array.
        readonly List<List<int>> _slots = new List<List<int>>();
        readonly int _slotCount;

        public TerrainMeshBuilder(H5Terrain terrain, int chunkSize = DefaultChunkSize, bool blendLayers = false)
        {
            _t = terrain;
            _chunkSize = Mathf.Max(1, chunkSize);
            _slotCount = blendLayers ? 1 : Mathf.Max(1, terrain != null ? terrain.LayerCount : 1);
            for (var i = 0; i < _slotCount; i++) _slots.Add(new List<int>());
        }

        /// <summary>
        /// World units per map tile. Two, from two independent readings of the
        /// shipped data: the engine halves an object's world point to get its
        /// tile, and objects whose blocked footprint is 2 tiles measure a median
        /// 4.00 units across.
        ///
        /// Heights are deliberately NOT scaled by this. They are already in world
        /// units -- ground 2.0, shore ring 1.6, water 1.5, one level a step of
        /// 2.0 -- so multiplying them too would leave the relief exactly as steep
        /// as it was and the whole change would do nothing.
        ///
        /// Getting this wrong is what turned gentle ramps into mountains: whole
        /// maps sit at heights of tens of units (Orcish_Medium3 has 9399 of its
        /// 9409 vertices above 10, peaking at 58.9), so relief of ~48 units
        /// across 96 tiles reads as a 26 degree average gradient at one unit a
        /// tile and a 14 degree one at two.
        /// </summary>
        public const float TileSize = 2f;

        /// <summary>HoMM5 is Z-up; Unity is Y-up.  Swap the two axes.</summary>
        public static Vector3 ToUnity(float x, float y, float z)
            => new Vector3(x * TileSize, z, y * TileSize);

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
            for (var i = 0; i < _slotCount; i++) _slots[i].Clear();

            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    EmitFlatCell(x, y);
                }

            if (_tri.Count == 0 || _pos.Count == 0) return null;

            var mesh = new Mesh { name = $"terrain_{chunkX}_{chunkY}" };
            mesh.indexFormat = _pos.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_pos);
            mesh.SetUVs(0, _uv);
            mesh.SetNormals(_nor);
            mesh.SetColors(_col);
            mesh.subMeshCount = _slotCount;
            for (var i = 0; i < _slotCount; i++) mesh.SetTriangles(_slots[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Build every chunk of the plane under <paramref name="parent"/>.</summary>
        public int BuildAll(Transform parent, Material[] materials)
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
                    mr.sharedMaterials = materials;
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
            AddQuad(i, _slotCount == 1 ? 0 : _t.DominantLayerAt(cx, cy));
        }

        void AddQuad(int i, int slot)
        {
            // Wound so the face is front-facing after the Z-up to Y-up swap:
            // the swap reverses orientation, which turns the source's
            // counter-clockwise winding into Unity's clockwise front face.
            if (slot < 0 || slot >= _slotCount) slot = 0;
            var tri = _slots[slot];
            tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
            tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
            _tri.Add(i);   // kept only as a "did anything get built" counter
        }

        void Add(int vx, int vy, float h, Vector3? normal)
        {
            _pos.Add(ToUnity(vx, vy, h));
            _uv.Add(new Vector2(vx * 0.25f, vy * 0.25f));
            _nor.Add(normal ?? _t.NormalAt(vx, vy));
            _col.Add(ColorFor(vx, vy));
        }

        // Textured ground must not be multiplied by a diagnostic green/brown
        // vertex tint. The fallback material also uses neutral vertex colours.
        Color32 ColorFor(int x, int y) => new Color32(255, 255, 255, 255);
    }
}
