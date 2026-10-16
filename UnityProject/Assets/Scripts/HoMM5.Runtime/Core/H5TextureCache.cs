using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HoMM5.Core
{
    /// <summary>
    /// Loads the PNGs the importer wrote from the game's .dds files and hands
    /// out cached <see cref="Texture2D"/>s keyed by the source .dds path.
    ///
    /// The map JSON stores texture references in exactly the form the texture
    /// manifest keys them ("/Textures/.../mushroom04.tga.dds"), so a lookup is a
    /// single dictionary hit -- no path massaging on either side.
    /// </summary>
    public class H5TextureCache
    {
        [System.Serializable]
        public class Entry
        {
            public int width;
            public int height;
            public string png;
        }

        Dictionary<string, Entry> _manifest = new Dictionary<string, Entry>();
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        readonly Shader _shader;

        public int LoadedCount => _textures.Count;
        public int ManifestCount => _manifest.Count;
        public int MissingCount { get; private set; }

        /// <summary>A texture no map object referenced; used as a visible placeholder.</summary>
        Texture2D _missing;

        public H5TextureCache(Shader shader)
        {
            _shader = shader != null ? shader : Shader.Find("Standard");
        }

        /// <summary>Read Textures/_manifest.json. Safe to call when there is none.</summary>
        public bool LoadManifest()
        {
            _manifest.Clear();
            var path = Path.Combine(H5Paths.Root, "Textures", "_manifest.json");
            if (!File.Exists(path)) return false;

            try
            {
                // JsonUtility cannot deserialize a Dictionary, so the manifest is
                // parsed by hand -- it is a flat object of key -> {width,height,png}.
                _manifest = ParseFlat(File.ReadAllText(path));
                return _manifest.Count > 0;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HoMM5] could not read texture manifest: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Minimal parser for the flat manifest object. JsonUtility has no
        /// Dictionary support and a full JSON dependency is not worth one file.
        /// </summary>
        static Dictionary<string, Entry> ParseFlat(string json)
        {
            var map = new Dictionary<string, Entry>();
            var i = json.IndexOf('{');
            if (i < 0) return map;
            i++;

            while (i < json.Length)
            {
                while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r' ||
                                           json[i] == '\t' || json[i] == ',')) i++;
                if (i >= json.Length || json[i] != '"') break;

                var key = ReadString(json, ref i);
                while (i < json.Length && json[i] != '{' && json[i] != '}') i++;
                if (i >= json.Length || json[i] == '}') break;

                var objStart = i;
                var depth = 0;
                while (i < json.Length)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                    i++;
                }
                var obj = json.Substring(objStart, i - objStart);

                var entry = new Entry();
                entry.png = ReadField(obj, "png");
                int w, h;
                if (int.TryParse(ReadField(obj, "width"), out w)) entry.width = w;
                if (int.TryParse(ReadField(obj, "height"), out h)) entry.height = h;
                if (!string.IsNullOrEmpty(entry.png)) map[key] = entry;
            }
            return map;
        }

        static string ReadString(string s, ref int i)
        {
            // i points at the opening quote.
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    var c = s[i + 1];
                    if (c == 'u' && i + 5 < s.Length)
                    {
                        sb.Append((char)System.Convert.ToInt32(s.Substring(i + 2, 4), 16));
                        i += 6;
                        continue;
                    }
                    sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c);
                    i += 2;
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            i++; // closing quote
            return sb.ToString();
        }

        static string ReadField(string obj, string field)
        {
            var k = obj.IndexOf("\"" + field + "\"", System.StringComparison.Ordinal);
            if (k < 0) return null;
            var colon = obj.IndexOf(':', k + field.Length + 2);
            if (colon < 0) return null;
            var i = colon + 1;
            while (i < obj.Length && (obj[i] == ' ' || obj[i] == '\t')) i++;
            if (i >= obj.Length) return null;
            if (obj[i] == '"') return ReadString(obj, ref i);
            var start = i;
            while (i < obj.Length && obj[i] != ',' && obj[i] != '}' && obj[i] != ' ') i++;
            return obj.Substring(start, i - start);
        }

        /// <summary>
        /// The texture for a source .dds path, or the placeholder when the import
        /// did not produce one. Returns null only if there is no shader at all.
        /// </summary>
        public Texture2D Get(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            Texture2D cached;
            if (_textures.TryGetValue(sourcePath, out cached)) return cached;

            var tex = Load(sourcePath);
            _textures[sourcePath] = tex;
            return tex;
        }

        /// <summary>A shared Standard material textured with the given source .dds.</summary>
        public Material MaterialFor(string sourcePath)
        {
            return MaterialFor(sourcePath, null);
        }

        /// <summary>
        /// A shared material for a source .dds with the model's material settings
        /// applied. Materials are cached per (texture, alpha mode, two-sided) so
        /// a map full of identical trees makes one, not a thousand.
        /// </summary>
        public Material MaterialFor(string sourcePath, HoMM5.Data.MaterialDef def)
        {
            var tex = Get(sourcePath);
            if (tex == null) return null;

            var mode = def != null && !string.IsNullOrEmpty(def.alphaMode)
                ? def.alphaMode : "AM_OPAQUE";
            var twoSided = def != null && def.twoSided;
            var key = sourcePath + "|" + mode + "|" + twoSided;
            Material m;
            if (_materials.TryGetValue(key, out m)) return m;

            m = new Material(_shader) { name = "H5_" + Path.GetFileNameWithoutExtension(sourcePath) };
            m.mainTexture = tex;
            ApplyAlphaMode(m, mode);
            if (def != null && def.selfIllum)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white * 0.6f);
            }
            if (twoSided) TwoSidedMaterials++;
            _materials[key] = m;
            return m;
        }

        /// <summary>How many materials asked for back faces. Counted, not applied:
        /// the Built-in Standard shader has no cull property.</summary>
        public int TwoSidedMaterials { get; private set; }

        /// <summary>
        /// Map HoMM5's AlphaMode onto the Standard shader's modes.
        ///
        /// AM_ALPHA_TEST is the common case -- 5244 of 11638 materials in the
        /// data -- and is what stops foliage, fences and railings from rendering
        /// as solid rectangles. AM_OVERLAY and AM_DECAL are effect passes with no
        /// Standard equivalent, so they fall back to ordinary transparency.
        /// </summary>
        static void ApplyAlphaMode(Material m, string mode)
        {
            switch (mode)
            {
                case "AM_ALPHA_TEST":
                    m.SetFloat("_Mode", 1f);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.DisableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;

                case "AM_TRANSPARENT":
                case "AM_OVERLAY":
                case "AM_DECAL":
                    SetTransparent(m, zWrite: false);
                    break;

                case "AM_OVERLAY_ZWRITE":
                    SetTransparent(m, zWrite: true);
                    break;

                default:   // AM_OPAQUE and anything unrecognised
                    m.SetFloat("_Mode", 0f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.DisableKeyword("_ALPHATEST_ON");
                    m.DisableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.renderQueue = -1;
                    break;
            }
        }

        static void SetTransparent(Material m, bool zWrite)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", zWrite ? 1 : 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        Texture2D Load(string sourcePath)
        {
            Entry entry;
            if (!_manifest.TryGetValue(sourcePath, out entry))
            {
                MissingCount++;
                return Placeholder();
            }

            var file = Path.Combine(H5Paths.Root, "Textures", entry.png);
            if (!File.Exists(file))
            {
                MissingCount++;
                return Placeholder();
            }

            try
            {
                var bytes = File.ReadAllBytes(file);
                // LoadImage resizes the texture to match the PNG, so the 2x2 seed
                // is only there to satisfy the constructor.
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                {
                    name = entry.png,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
                if (tex.LoadImage(bytes))
                {
                    tex.Apply(false, false);
                    return tex;
                }
                Object.Destroy(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HoMM5] texture {sourcePath} failed to load: {e.Message}");
            }
            MissingCount++;
            return Placeholder();
        }

        /// <summary>
        /// Magenta-and-black checkerboard. Deliberately ugly: a silently blank
        /// object is indistinguishable from an untextured one.
        /// </summary>
        Texture2D Placeholder()
        {
            if (_missing != null) return _missing;
            _missing = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "H5_MissingTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            _missing.SetPixel(0, 0, Color.magenta);
            _missing.SetPixel(1, 1, Color.magenta);
            _missing.SetPixel(1, 0, Color.black);
            _missing.SetPixel(0, 1, Color.black);
            _missing.Apply();
            return _missing;
        }

        public void Dispose()
        {
            foreach (var kv in _textures) if (kv.Value != null) Object.Destroy(kv.Value);
            foreach (var kv in _materials) if (kv.Value != null) Object.Destroy(kv.Value);
            _textures.Clear();
            _materials.Clear();
        }
    }
}
