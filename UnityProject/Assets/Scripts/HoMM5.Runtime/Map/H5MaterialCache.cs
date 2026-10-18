using System;
using System.Collections.Generic;
using System.Globalization;
using HoMM5.Core;
using HoMM5.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoMM5.Map
{
    /// <summary>
    /// Runtime material factory for the XDB material array exported by the
    /// offline importer.
    ///
    /// Geometry groups are material slices.  When the source array and emitted
    /// GLB slice count agree, the importer preserves that array order and the
    /// builder applies slot N to mesh N; a single source material is repeated
    /// for all slices.  Ambiguous counts fall back instead of guessing.  It
    /// never selects a texture by basename, by PNG order, or by mesh name.
    /// </summary>
    public sealed class H5MaterialCache : IDisposable
    {
        readonly Dictionary<string, Material> _materials =
            new Dictionary<string, Material>(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> _textures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly HashSet<string> _missingTextures =
            new HashSet<string>(StringComparer.Ordinal);
        readonly Shader _shader;

        public H5MaterialCache()
        {
            _shader = Shader.Find("Standard");
            if (_shader == null)
                Debug.LogError("[HoMM5] Standard shader is unavailable; XDB materials cannot be created.");
        }

        /// <summary>Create or retrieve one Unity material for one XDB material record.</summary>
        public Material Get(MaterialDef definition, Material fallback)
        {
            if (definition == null) return fallback;
            if (_shader == null) return fallback;

            var key = Key(definition);
            if (_materials.TryGetValue(key, out var cached)) return cached;

            var material = new Material(_shader) { name = "H5_XDB_" + Sanitize(key) };
            material.color = Color.white;
            // SpecFactor/MetalMirror/DetailTexture are retained as source
            // metadata.  Their exact HoMM5 effect shader is not confirmed, so
            // do not reinterpret them as Unity Standard parameters.
            material.SetFloat("_Cull", definition.is2Sided ? 0f : 2f);

            ApplyTexture(material, "_MainTex", definition.texture);
            ApplyBump(material, definition.bump);
            ApplyAlpha(material, definition.alphaMode);

            // These flags have direct renderer/depth equivalents.  AddPlaced,
            // Effect and LightingMode remain in MaterialDef as source metadata;
            // their engine-side shader implementations are not established by
            // the currently confirmed decompilation and must not be guessed.
            if (definition.ignoreZBuffer)
            {
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", (int)CompareFunction.Always);
            }

            _materials.Add(key, material);
            return material;
        }

        /// <summary>Apply per-slice shadow flags which belong to MeshRenderer.</summary>
        public static void ConfigureRenderer(MeshRenderer renderer, MaterialDef definition)
        {
            if (renderer == null) return;
            if (definition == null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                return;
            }

            renderer.shadowCastingMode = definition.castShadow
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            renderer.receiveShadows = definition.receiveShadow;
        }

        void ApplyTexture(Material material, string property, TextureDef definition)
        {
            var texture = LoadTexture(definition);
            if (texture == null) return;
            material.SetTexture(property, texture);
        }

        void ApplyBump(Material material, TextureDef definition)
        {
            var texture = LoadTexture(definition);
            if (texture == null) return;
            material.SetTexture("_BumpMap", texture);
            material.EnableKeyword("_NORMALMAP");
        }

        Texture2D LoadTexture(TextureDef definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.png)) return null;
            if (_textures.TryGetValue(definition.png, out var cached)) return cached;
            if (_missingTextures.Contains(definition.png)) return null;

            var path = H5Paths.Textures(definition.png);
            var bytes = H5Paths.ReadAllBytes(path);
            if (bytes == null)
            {
                _missingTextures.Add(definition.png);
                Debug.LogWarning("[HoMM5] material texture was not imported: " + definition.png);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false)
            {
                name = "H5_XDB_Texture_" + Sanitize(definition.png)
            };
            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                _missingTextures.Add(definition.png);
                Debug.LogWarning("[HoMM5] failed to decode material texture: " + path);
                return null;
            }

            ApplyAddressMode(texture, definition.address);
            if (definition.flipY) FlipVertically(texture);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            _textures.Add(definition.png, texture);
            return texture;
        }

        static void ApplyAddressMode(Texture2D texture, string address)
        {
            switch (address)
            {
                case "CLAMP":
                    texture.wrapModeU = TextureWrapMode.Clamp;
                    texture.wrapModeV = TextureWrapMode.Clamp;
                    break;
                case "WRAP_X":
                    texture.wrapModeU = TextureWrapMode.Repeat;
                    texture.wrapModeV = TextureWrapMode.Clamp;
                    break;
                case "WRAP_Y":
                    texture.wrapModeU = TextureWrapMode.Clamp;
                    texture.wrapModeV = TextureWrapMode.Repeat;
                    break;
                case "WRAP":
                    texture.wrapModeU = TextureWrapMode.Repeat;
                    texture.wrapModeV = TextureWrapMode.Repeat;
                    break;
            }
        }

        static void FlipVertically(Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels();
            for (var y = 0; y < height / 2; y++)
            {
                var opposite = height - 1 - y;
                for (var x = 0; x < width; x++)
                {
                    var a = y * width + x;
                    var b = opposite * width + x;
                    var color = pixels[a];
                    pixels[a] = pixels[b];
                    pixels[b] = color;
                }
            }
            texture.SetPixels(pixels);
        }

        static void ApplyAlpha(Material material, string alphaMode)
        {
            // These three mappings are direct Unity render-state equivalents of
            // the corresponding NDb enum names.  AM_OVERLAY/AM_DECAL need the
            // original effect shader's blend implementation; retaining the
            // source value in JSON is safer than silently inventing a blend.
            switch (alphaMode)
            {
                case "AM_ALPHA_TEST":
                    material.SetFloat("_Mode", 1f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 2450;
                    break;
                case "AM_TRANSPARENT":
                    material.SetFloat("_Mode", 3f);
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    break;
                case "AM_OPAQUE":
                case "":
                    material.SetFloat("_Mode", 0f);
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = -1;
                    break;
            }
        }

        static string Key(MaterialDef definition)
        {
            var texture = definition.texture != null ? definition.texture.png ?? "" : "";
            var bump = definition.bump != null ? definition.bump.png ?? "" : "";
            return texture + "\u001f" + bump + "\u001f" + (definition.alphaMode ?? "") + "\u001f" +
                   definition.is2Sided + "\u001f" + definition.ignoreZBuffer + "\u001f" +
                   definition.specFactor.ToString("R", CultureInfo.InvariantCulture) + "\u001f" +
                   definition.metalMirror.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "default";
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        public void Dispose()
        {
            foreach (var material in _materials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            foreach (var texture in _textures.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            _materials.Clear();
            _textures.Clear();
            _missingTextures.Clear();
        }
    }
}
