using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// Renders the two native fog states over the imported terrain: unexplored
    /// cells are opaque and explored-but-not-currently-visible cells are a dark
    /// memory layer. Gameplay visibility comes from AdventureMapGameState and
    /// the source BaseHeroLookRange/Scouting values; this component is only its
    /// scene representation.
    /// </summary>
    public sealed class H5FogOfWar : MonoBehaviour
    {
        MapSceneBuilder _builder;
        MeshFilter _unknownFilter;
        MeshRenderer _unknownRenderer;
        MeshFilter _memoryFilter;
        MeshRenderer _memoryRenderer;
        Material _unknownMaterial;
        Material _memoryMaterial;

        public static H5FogOfWar Create(MapSceneBuilder builder)
        {
            if (builder == null || builder.Root == null || builder.Terrain == null) return null;
            var go = new GameObject("FogOfWar");
            go.transform.SetParent(builder.Root.transform, false);
            var fog = go.AddComponent<H5FogOfWar>();
            fog._builder = builder;
            fog._unknownFilter = CreateLayer(go.transform, "Unexplored", 0.92f,
                                              out fog._unknownRenderer, out fog._unknownMaterial);
            fog._memoryFilter = CreateLayer(go.transform, "ExploredMemory", 0.48f,
                                            out fog._memoryRenderer, out fog._memoryMaterial);
            fog._unknownRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fog._memoryRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return fog;
        }

        static MeshFilter CreateLayer(Transform parent, string name, float alpha,
                                      out MeshRenderer renderer, out Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var filter = go.GetComponent<MeshFilter>();
            renderer = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "H5_" + name };
            material.color = new Color(0.015f, 0.012f, 0.02f, alpha);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            renderer.sharedMaterial = material;
            renderer.receiveShadows = false;
            return filter;
        }

        public void UpdateVisibility(AdventureMapGameState state)
        {
            if (_builder == null || _builder.Terrain == null || state == null) return;
            BuildLayer(_unknownFilter, state, false);
            BuildLayer(_memoryFilter, state, true);

            var views = _builder.Root.GetComponentsInChildren<H5MapObject>(true);
            for (var i = 0; i < views.Length; i++)
            {
                var obj = views[i];
                if (obj == null || obj.Def == null) continue;
                var visible = state.IsObjectVisible(obj.Def);
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++) renderers[r].enabled = visible;
            }
        }

        void BuildLayer(MeshFilter filter, AdventureMapGameState state, bool memory)
        {
            if (filter == null) return;
            var terrain = _builder.Terrain;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var tilesX = terrain.TilesX;
            var tilesY = terrain.TilesY;
            for (var y = 0; y < tilesY; y++)
            for (var x = 0; x < tilesX; x++)
            {
                var explored = state.IsTileExplored(x, y);
                var visible = state.IsTileVisible(x, y);
                var include = memory ? explored && !visible : !explored;
                if (!include) continue;

                var at = vertices.Count;
                vertices.Add(Vertex(terrain, x, y));
                vertices.Add(Vertex(terrain, x + 1, y));
                vertices.Add(Vertex(terrain, x + 1, y + 1));
                vertices.Add(Vertex(terrain, x, y + 1));
                triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 1);
                triangles.Add(at); triangles.Add(at + 3); triangles.Add(at + 2);
            }
            var mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = filter.gameObject.name + "Mesh" };
                filter.sharedMesh = mesh;
            }
            mesh.Clear();
            if (vertices.Count == 0)
            {
                filter.gameObject.SetActive(false);
                return;
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            filter.gameObject.SetActive(true);
        }

        static Vector3 Vertex(H5Terrain terrain, int x, int y)
        {
            var height = terrain.HeightAt(x, y) + 0.12f;
            return TerrainMeshBuilder.ToUnity(x, y, height);
        }

        void OnDestroy()
        {
            if (_unknownMaterial != null) Destroy(_unknownMaterial);
            if (_memoryMaterial != null) Destroy(_memoryMaterial);
        }
    }
}
