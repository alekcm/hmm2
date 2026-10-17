using System;
using System.Collections.Generic;
using HoMM5.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>Native XDB layout + DDS-derived PNGs from the small `ui` import stage.</summary>
    public sealed class H5UiSkin : IDisposable
    {
        public sealed class Asset
        {
            public string png;
            public int width;
            public int height;
            public string source;
        }
        public sealed class Window
        {
            public float x, y, width, height;
            public string horizontal, vertical;
            public string normal, pushed, disabled;
        }
        public sealed class Catalog
        {
            public int version;
            public Dictionary<string, Asset> assets = new Dictionary<string, Asset>();
            public Dictionary<string, Window> windows = new Dictionary<string, Window>();
            public Dictionary<string, string> heroPortraits = new Dictionary<string, string>();
        }

        readonly Catalog _catalog;
        readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        readonly List<Texture2D> _textures = new List<Texture2D>();

        H5UiSkin(Catalog catalog) { _catalog = catalog; }

        public static H5UiSkin Load()
        {
            var json = H5Paths.ReadAllText(H5Paths.Import("UI/skin.json"));
            if (json == null) return null;
            try
            {
                var catalog = JsonConvert.DeserializeObject<Catalog>(json);
                return catalog != null && catalog.version == 1 && catalog.assets != null && catalog.windows != null
                    ? new H5UiSkin(catalog) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HoMM5] UI skin could not load: " + e.Message);
                return null;
            }
        }

        public Window GetWindow(string key) => _catalog.windows.TryGetValue(key, out var window) ? window : null;

        public Sprite GetPortrait(string sharedHref)
        {
            if (string.IsNullOrEmpty(sharedHref) || _catalog.heroPortraits == null) return null;
            var path = sharedHref.Split('#')[0].Replace('\\', '/').ToLowerInvariant();
            return _catalog.heroPortraits.TryGetValue(path, out var key) ? GetSprite(key) : null;
        }

        public Sprite GetSprite(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_sprites.TryGetValue(key, out var cached)) return cached;
            if (!_catalog.assets.TryGetValue(key, out var asset)) return null;
            var bytes = H5Paths.ReadAllBytes(H5Paths.Import(asset.png));
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            { name = key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (!texture.LoadImage(bytes)) { UnityEngine.Object.Destroy(texture); return null; }
            _textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f, 100f);
            sprite.name = key;
            _sprites[key] = sprite;
            return sprite;
        }

        /// <summary>Native HIGH_END aligns the far edge; y is down in the game.</summary>
        public static void Place(RectTransform rect, Window window)
        {
            var hx = window.horizontal == "EPA_HIGH_END" ? 1f : window.horizontal == "ERA_CENTER" ? 0.5f : 0f;
            var vy = window.vertical == "EPA_HIGH_END" ? 0f : window.vertical == "ERA_CENTER" ? 0.5f : 1f;
            rect.anchorMin = rect.anchorMax = new Vector2(hx, vy);
            rect.pivot = new Vector2(hx, vy);
            rect.anchoredPosition = new Vector2(window.x, -window.y);
            rect.sizeDelta = new Vector2(window.width, window.height);
        }

        public Image CreateWindow(Transform parent, string key, bool raycast = false)
        {
            var window = GetWindow(key);
            if (window == null) return null;
            var go = new GameObject(key, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, window);
            var image = go.GetComponent<Image>();
            image.sprite = GetSprite(window.normal);
            image.color = image.sprite != null ? Color.white : new Color(0.04f, 0.04f, 0.05f, 0.95f);
            image.raycastTarget = raycast;
            return image;
        }

        public Button CreateButton(Transform parent, string key, Action action)
        {
            var image = CreateWindow(parent, key, true);
            if (image == null) return null;
            var window = GetWindow(key);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = image.sprite,
                pressedSprite = GetSprite(window.pushed) ?? image.sprite,
                disabledSprite = GetSprite(window.disabled) ?? image.sprite
            };
            // DDSs contain transparent square padding; do not let it cover
            // neighbouring buttons on the round command dial.
            image.alphaHitTestMinimumThreshold = 0.15f;
            button.onClick.AddListener(() => { HoMM5.Audio.H5Audio.Click(); action?.Invoke(); });
            return button;
        }

        public void Dispose()
        {
            foreach (var sprite in _sprites.Values) UnityEngine.Object.Destroy(sprite);
            foreach (var texture in _textures) UnityEngine.Object.Destroy(texture);
            _sprites.Clear();
            _textures.Clear();
        }
    }
}
