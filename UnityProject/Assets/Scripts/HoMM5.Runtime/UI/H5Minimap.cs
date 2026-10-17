using HoMM5.Core;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The adventure-map minimap: baked terrain colors, object dots and the
    /// camera's viewport rectangle. Clicking moves the camera there.
    ///
    /// Two textures: the terrain base is painted once per map, the overlay
    /// (dots + camera rect) is repainted at a few hertz -- a map is only a few
    /// hundred pixels across, so the update is trivial.
    /// </summary>
    public class H5Minimap : MonoBehaviour
    {
        public float overlayRefreshSeconds = 0.15f;

        RawImage _base;
        RawImage _overlay;
        Texture2D _baseTex;
        Texture2D _overlayTex;
        MapSceneBuilder _builder;
        AdventureMapController _controller;
        AdventureMapGameState _state;
        H5MapObject[] _objects = new H5MapObject[0];
        float _nextRefresh;

        /// <summary>
        /// Build the panel content under <paramref name="parent"/>. Size is the
        /// parent's rect (call after the layout is set).
        /// </summary>
        public static H5Minimap Create(Transform parent, Vector2 anchorPos, Vector2 size)
        {
            var go = new GameObject("Minimap", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = anchorPos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.9f);

            var mm = go.AddComponent<H5Minimap>();
            mm._base = MakeRaw(go.transform, "Terrain", new Color(1, 1, 1, 1));
            mm._overlay = MakeRaw(go.transform, "Overlay", new Color(1, 1, 1, 1));
            return mm;
        }

        static RawImage MakeRaw(Transform parent, string name, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(3, 3);
            rt.offsetMax = new Vector2(-3, -3);
            var img = go.GetComponent<RawImage>();
            img.color = tint;
            return img;
        }

        public void Build(MapSceneBuilder builder, AdventureMapController controller, AdventureMapGameState state)
        {
            _builder = builder;
            _controller = controller;
            // Rebuilds (map reload) must not stack subscriptions on the same
            // game state.
            if (_state != null) _state.Changed -= RequestRefresh;
            _state = state;
            if (_state != null) _state.Changed += RequestRefresh;

            var terrain = builder != null ? builder.Terrain : null;
            var w = Mathf.Max(2, builder != null && builder.Map != null ? builder.Map.tileX : 16) + 1;
            var h = Mathf.Max(2, builder != null && builder.Map != null ? builder.Map.tileY : 16) + 1;

            if (_baseTex != null) Destroy(_baseTex);
            if (_overlayTex != null) Destroy(_overlayTex);
            _baseTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _overlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            PaintTerrain(terrain, w, h);
            _base.texture = _baseTex;
            _overlay.texture = _overlayTex;

            _objects = builder != null && builder.Root != null
                ? builder.Root.GetComponentsInChildren<H5MapObject>(true)
                : new H5MapObject[0];
            _nextRefresh = 0f;
        }

        void RequestRefresh() => _nextRefresh = 0f;

        void OnDestroy()
        {
            if (_state != null) _state.Changed -= RequestRefresh;
            if (_baseTex != null) Destroy(_baseTex);
            if (_overlayTex != null) Destroy(_overlayTex);
        }

        // -- painting ------------------------------------------------------

        void PaintTerrain(H5Terrain terrain, int w, int h)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                _baseTex.SetPixel(x, y, TerrainColor(terrain, x, y));
            }
            _baseTex.Apply(false);
        }

        static Color TerrainColor(H5Terrain terrain, int x, int y)
        {
            if (terrain == null) return new Color(0.15f, 0.16f, 0.15f);
            if (terrain.IsWater(x, y)) return new Color(0.12f, 0.28f, 0.48f);

            var c = GroundColor(terrain, x, y);
            if (terrain.IsPlateau(x, y)) c = Color.Lerp(c, Color.white, 0.18f);
            else if (terrain.IsRamp(x, y)) c = Color.Lerp(c, Color.white, 0.10f);
            return c;
        }

        /// <summary>Ground tint from the painted layer's texture name.</summary>
        static Color GroundColor(H5Terrain terrain, int x, int y)
        {
            var layer = terrain.LayerCount > 0 ? terrain.DominantLayerAt(x, y) : -1;
            var name = "";
            if (layer >= 0 && terrain.Layers != null && layer < terrain.Layers.Count &&
                terrain.Layers[layer] != null)
                name = (terrain.Layers[layer].texture ?? "").ToLowerInvariant();

            if (name.Contains("sand") || name.Contains("desert")) return new Color(0.71f, 0.62f, 0.38f);
            if (name.Contains("snow") || name.Contains("ice")) return new Color(0.85f, 0.87f, 0.90f);
            if (name.Contains("dirt") || name.Contains("rough") || name.Contains("nidle"))
                return new Color(0.45f, 0.33f, 0.21f);
            if (name.Contains("lava") || name.Contains("volcanic")) return new Color(0.35f, 0.18f, 0.14f);
            if (name.Contains("swamp")) return new Color(0.28f, 0.33f, 0.22f);
            if (name.Contains("rock") || name.Contains("stone") || name.Contains("mountain"))
                return new Color(0.42f, 0.41f, 0.39f);
            if (name.Contains("grass") || name.Contains("moss") || name.Contains("orc"))
                return new Color(0.28f, 0.45f, 0.22f);
            return new Color(0.30f, 0.42f, 0.25f);
        }

        static Color DotColor(H5MapObject obj)
        {
            if (obj == null) return new Color(0.5f, 0.5f, 0.5f);
            switch (obj.Kind)
            {
                case "AdvMapTown": return new Color(0.95f, 0.95f, 0.95f);
                case "AdvMapHero":
                    return obj.IsLocalHero ? new Color(1f, 0.92f, 0.2f) : new Color(0.95f, 0.55f, 0.15f);
                case "AdvMapMine": return new Color(0.85f, 0.62f, 0.25f);
                case "AdvMapMonster": return new Color(0.88f, 0.22f, 0.18f);
                case "AdvMapArtifact": return new Color(0.85f, 0.35f, 0.85f);
                case "AdvMapTreasure": return new Color(0.25f, 0.85f, 0.85f);
                case "AdvMapDwelling": return new Color(0.35f, 0.8f, 0.35f);
                case "AdvMapBuilding": return new Color(0.8f, 0.8f, 0.65f);
                default: return new Color(0.55f, 0.55f, 0.55f);
            }
        }

        void RefreshOverlay()
        {
            var w = _overlayTex.width;
            var h = _overlayTex.height;
            var pixels = new Color[w * h];   // defaults to transparent black

            // Object dots (the map's y axis is the minimap's y axis).
            for (var i = 0; i < _objects.Length; i++)
            {
                var obj = _objects[i];
                if (obj == null || obj.Def == null || !obj.gameObject.activeInHierarchy) continue;
                var x = Mathf.Clamp(Mathf.RoundToInt(obj.Def.x), 0, w - 1);
                var y = Mathf.Clamp(Mathf.RoundToInt(obj.Def.y), 0, h - 1);
                pixels[y * w + x] = DotColor(obj);
            }

            // Camera viewport rectangle.
            if (_controller != null && _builder != null)
            {
                var focus = _controller.Focus;
                var tilesPerWorld = 1f / TerrainMeshBuilder.TileSize;
                var cx = focus.x * tilesPerWorld;
                var cy = focus.z * tilesPerWorld;
                var halfH = Mathf.Tan(_controller.ViewFov * 0.5f * Mathf.Deg2Rad) *
                            _controller.Distance * tilesPerWorld;
                var halfW = halfH * Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
                DrawRect(pixels, w, h, cx - halfW, cy - halfH, cx + halfW, cy + halfH,
                         new Color(1f, 1f, 1f, 0.9f));
            }

            _overlayTex.SetPixels(pixels);
            _overlayTex.Apply(false);
        }

        static void DrawRect(Color[] pixels, int w, int h, float xMin, float yMin, float xMax, float yMax, Color c)
        {
            var x0 = Mathf.Clamp(Mathf.RoundToInt(xMin), 0, w - 1);
            var x1 = Mathf.Clamp(Mathf.RoundToInt(xMax), 0, w - 1);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(yMin), 0, h - 1);
            var y1 = Mathf.Clamp(Mathf.RoundToInt(yMax), 0, h - 1);
            if (x1 < x0) { var t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { var t = y0; y0 = y1; y1 = t; }
            for (var x = x0; x <= x1; x++)
            {
                pixels[y0 * w + x] = c;
                pixels[y1 * w + x] = c;
            }
            for (var y = y0; y <= y1; y++)
            {
                pixels[y * w + x0] = c;
                pixels[y * w + x1] = c;
            }
        }

        // -- interaction ---------------------------------------------------

        void Update()
        {
            if (_overlayTex == null) return;

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, overlayRefreshSeconds);
                RefreshOverlay();
            }

            if (!H5Input.SelectPressed() || !PointerOnMinimap()) return;
            FocusAtPointer();
        }

        bool PointerOnMinimap()
        {
            var rt = (RectTransform)transform;
            return RectTransformUtility.RectangleContainsScreenPoint(
                rt, H5Input.PointerPosition(), null);
        }

        void FocusAtPointer()
        {
            if (_builder == null || _controller == null) return;
            var rt = (RectTransform)_base.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, H5Input.PointerPosition(), null, out var local)) return;

            var w = rt.rect.width;
            var h = rt.rect.height;
            if (w <= 1f || h <= 1f) return;
            var u = Mathf.Clamp01((local.x - rt.rect.xMin) / w);
            var v = Mathf.Clamp01((local.y - rt.rect.yMin) / h);
            var tileX = u * _overlayTex.width;
            var tileY = v * _overlayTex.height;
            _controller.FocusOn(_builder.TileToWorld(tileX, tileY));
        }
    }
}
