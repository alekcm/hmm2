using System.Collections.Generic;
using HoMM5.Core;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>Owns per-map GPU paint arrays; original coverage is bilinearly blended.</summary>
    public sealed class H5TerrainRendering : MonoBehaviour
    {
        Texture2DArray _ground;
        Texture2DArray _coverage;
        Material _material;
        public int TexturesBound { get; private set; }

        public Material Configure(H5Terrain terrain, H5TextureCache textures)
        {
            if (terrain == null || textures == null || terrain.LayerCount == 0 ||
                terrain.LayerCount > 64 || !SystemInfo.supports2DArrayTextures) return null;
            // A Resources reference keeps this shader in standalone builds.
            var shader = Resources.Load<Shader>("HoMM5/TerrainBlend");
            if (shader == null || !shader.isSupported) return null;
            var order = new List<int>();
            for (var i = 1; i < terrain.LayerCount; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                var pa = terrain.Layers[a] != null ? terrain.Layers[a].priority : 0;
                var pb = terrain.Layers[b] != null ? terrain.Layers[b].priority : 0;
                var comparison = pa.CompareTo(pb);
                return comparison != 0 ? comparison : a.CompareTo(b);
            });
            order.Insert(0, 0);
            const int resolution = 256;
            var count = terrain.LayerCount;
            var stride = terrain.VertexCount;
            _ground = new Texture2DArray(resolution, resolution, count, TextureFormat.RGBA32, true, false)
            { name = "H5 Ground", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            _coverage = new Texture2DArray(stride, stride, count, TextureFormat.RGBA32, false, true)
            { name = "H5 Paint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var colors = new Color[resolution * resolution];
            var weights = new Color32[stride * stride];
            for (var slice = 0; slice < count; slice++)
            {
                var layer = order[slice];
                var source = terrain.Layers[layer] != null ? terrain.Layers[layer].texture : null;
                var texture = textures.Has(source) ? textures.Get(source) : null;
                if (texture != null) TexturesBound++;
                for (var y = 0; y < resolution; y++)
                    for (var x = 0; x < resolution; x++)
                        colors[y * resolution + x] = texture != null
                            ? texture.GetPixelBilinear((x + 0.5f) / resolution, (y + 0.5f) / resolution)
                            : new Color(0.28f, 0.36f, 0.20f);
                _ground.SetPixels(colors, slice, 0);
                for (var y = 0; y < stride; y++)
                    for (var x = 0; x < stride; x++)
                    {
                        var w = terrain.LayerWeightAt(layer, x, y);
                        weights[y * stride + x] = new Color32(w, w, w, 255);
                    }
                _coverage.SetPixels32(weights, slice, 0);
            }
            _ground.Apply(true, true);
            _coverage.Apply(false, true);
            _material = new Material(shader) { name = "H5 Blended Terrain" };
            _material.SetTexture("_Ground", _ground);
            _material.SetTexture("_Coverage", _coverage);
            _material.SetInt("_LayerCount", count);
            _material.SetFloat("_WeightSize", stride);
            _material.SetFloat("_TileSize", TerrainMeshBuilder.TileSize);
            return _material;
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_ground != null) Destroy(_ground);
            if (_coverage != null) Destroy(_coverage);
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
        }
    }
}
