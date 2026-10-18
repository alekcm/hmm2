using System;
using System.Collections.Generic;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The adventure spellbook entry point from the native HUD.
    ///
    /// The chrome and the page/part order are the values exported from
    /// ``UI/SpellBook/SpellBook.(SpellBookDesc).xdb``.  The book and spell
    /// artwork are the shipped DDS records; this is deliberately separate from
    /// the combat spell list and from the old hero-skills dialog, which was not
    /// the button's original target.
    /// </summary>
    public sealed class AdventureSpellbook : MonoBehaviour
    {
        readonly List<GameObject> _pageItems = new List<GameObject>();
        readonly List<Sprite> _iconSprites = new List<Sprite>();
        H5UiSkin _skin;
        HoMM5Hud _owner;
        H5UiSkin.SpellbookDesc _desc;
        GameObject _root;
        RectTransform _page;
        RawImage _modelImage;
        RenderTexture _modelTexture;
        Camera _modelCamera;
        GameObject _modelRoot;
        H5TextureCache _textures;
        MeshCache _meshes;
        Text _title;
        Text _points;
        Text _details;
        int _pageIndex;
        int _tabIndex;
        string _schoolFilter;
        readonly List<Button> _tabButtons = new List<Button>();
        readonly List<Button> _schoolButtons = new List<Button>();
        H5MapObject _hero;

        public bool IsOpen => _root != null && _root.activeSelf;

        public static AdventureSpellbook Create(Transform parent, HoMM5Hud owner)
        {
            var skin = H5UiSkin.Load();
            if (skin == null || skin.Spellbook == null || skin.GetSprite("spellbookBook") == null)
            {
                skin?.Dispose();
                return null;
            }

            var go = new GameObject("AdventureSpellbook", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var book = go.AddComponent<AdventureSpellbook>();
            book._skin = skin;
            book._desc = skin.Spellbook;
            book._owner = owner;
            book.Build();
            go.SetActive(false);
            return book;
        }

        public void AttachTextures(H5TextureCache textures)
        {
            _textures = textures;
            if (IsOpen) BuildModel();
        }

        public void AttachMeshes(MeshCache meshes)
        {
            _meshes = meshes;
            if (IsOpen) BuildModel();
        }

        void Build()
        {
            _root = gameObject;
            var blocker = _root.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.58f);
            blocker.raycastTarget = true;

            var bookGo = new GameObject("OriginalBook", typeof(RectTransform), typeof(Image));
            var bookRect = bookGo.GetComponent<RectTransform>();
            bookRect.SetParent(_root.transform, false);
            bookRect.anchorMin = bookRect.anchorMax = bookRect.pivot = new Vector2(0.5f, 0.5f);
            bookRect.sizeDelta = new Vector2(920f, 820f);
            bookRect.anchoredPosition = Vector2.zero;
            var bookImage = bookGo.GetComponent<Image>();
            bookImage.sprite = _skin.GetSprite("spellbookBook");
            bookImage.color = Color.white;
            bookImage.preserveAspect = true;
            bookImage.raycastTarget = false;

            var modelGo = new GameObject("OriginalBookModel", typeof(RectTransform), typeof(RawImage));
            var modelRect = modelGo.GetComponent<RectTransform>();
            modelRect.SetParent(_root.transform, false);
            modelRect.anchorMin = modelRect.anchorMax = modelRect.pivot = new Vector2(0.5f, 0.5f);
            modelRect.sizeDelta = new Vector2(920f, 820f);
            modelRect.anchoredPosition = Vector2.zero;
            _modelImage = modelGo.GetComponent<RawImage>();
            _modelImage.color = Color.white;
            _modelImage.raycastTarget = false;
            modelGo.SetActive(false);

            var panel = new GameObject("SpellbookContent", typeof(RectTransform));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(_root.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 610f);
            panelRect.anchoredPosition = new Vector2(0f, -5f);

            _title = MakeText(panelRect, "AdventureTitle", 22, FontStyle.Bold,
                              TextAnchor.MiddleCenter, new Vector2(-380f, 265f), new Vector2(380f, 303f));
            _title.text = "Spell Book";
            // SpellBookDesc exposes exactly two spell modes. Hero abilities
            // do not have a standard spellbook part.
            var combatTab = MakeButton(panelRect, "CombatTab", "Combat", new Vector2(-130f, 270f),
                                       new Vector2(150f, 32f), () => SetTab(0));
            var adventureTab = MakeButton(panelRect, "AdventureTab", "Adventure", new Vector2(30f, 270f),
                                          new Vector2(150f, 32f), () => SetTab(1));
            _tabButtons.Add(combatTab);
            _tabButtons.Add(adventureTab);
            var schools = new[]
            {
                "MAGIC_SCHOOL_DARK", "MAGIC_SCHOOL_DESTRUCTIVE", "MAGIC_SCHOOL_LIGHT",
                "MAGIC_SCHOOL_SUMMONING", "MAGIC_SCHOOL_SPECIAL"
            };
            var schoolLabels = new[] { "Dark", "Destructive", "Light", "Summoning", "Special" };
            for (var i = 0; i < schools.Length; i++)
            {
                var school = schools[i];
                var button = MakeButton(panelRect, "School_" + i, schoolLabels[i],
                    new Vector2(-240f + i * 120f, 228f), new Vector2(112f, 26f),
                    () => SetSchool(school));
                _schoolButtons.Add(button);
            }
            _points = MakeText(panelRect, "SpellPoints", 16, FontStyle.Bold,
                               TextAnchor.MiddleCenter, new Vector2(235f, -298f), new Vector2(495f, -264f));
            _details = MakeText(panelRect, "SpellDetails", 14, FontStyle.Normal,
                                TextAnchor.UpperCenter, new Vector2(-270f, -260f), new Vector2(270f, -190f));
            _details.text = "Select a spell";

            _page = new GameObject("SpellPage", typeof(RectTransform)).GetComponent<RectTransform>();
            _page.SetParent(panelRect, false);
            _page.anchorMin = _page.anchorMax = _page.pivot = new Vector2(0.5f, 0.5f);
            _page.sizeDelta = new Vector2(660f, 420f);
            _page.anchoredPosition = new Vector2(0f, 10f);

            var close = MakeButton(panelRect, "CloseBook", "Close", new Vector2(300f, -282f),
                                   new Vector2(100f, 34f), Hide);
            var previous = MakeButton(panelRect, "PreviousPage", "‹", new Vector2(-335f, -4f),
                                      new Vector2(42f, 58f), () => SetPage(_pageIndex - 1));
            var next = MakeButton(panelRect, "NextPage", "›", new Vector2(335f, -4f),
                                  new Vector2(42f, 58f), () => SetPage(_pageIndex + 1));

            // Use the authored arrow texture when available, while retaining a
            // text fallback for a partial UI import.
            var arrow = _skin.GetSprite("spellbookArrow");
            if (arrow != null)
            {
                SetButtonSprite(previous, arrow);
                SetButtonSprite(next, arrow);
            }
        }

        /// <summary>
        /// Render the imported SpellBook2 model instead of displaying its source
        /// texture atlas as a flat image. The source model has one mesh/material
        /// for each authored part (page, book, bookmarks and controls); the
        /// separate camera keeps that geometry compatible with the overlay HUD.
        /// </summary>
        void BuildModel()
        {
            if (_modelImage == null || _meshes == null || _textures == null || _desc == null ||
                string.IsNullOrEmpty(_desc.geometryUid)) return;
            if (_modelRoot != null) return;
            var meshes = _meshes.Get(_desc.geometryUid);
            if (meshes == null || meshes.Length == 0) return;

            _modelTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
            {
                name = "AdventureSpellbookRender",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _modelTexture.Create();
            _modelImage.texture = _modelTexture;

            _modelRoot = new GameObject("SpellBook2Model");
            const int layer = 30;
            _modelRoot.layer = layer;
            var modelMaterials = _desc.modelTextures ?? new List<string>();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var hasBounds = false;
            for (var i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null) continue;
                var part = new GameObject("SpellBookPart_" + i);
                part.layer = layer;
                part.transform.SetParent(_modelRoot.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var renderer = part.AddComponent<MeshRenderer>();
                var source = modelMaterials.Count > 0
                    ? modelMaterials[Mathf.Min(i, modelMaterials.Count - 1)] : null;
                renderer.sharedMaterial = _textures.MaterialFor(source);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds)
            {
                Destroy(_modelRoot);
                _modelRoot = null;
                return;
            }

            _modelRoot.transform.position = -bounds.center;
            var lightGo = new GameObject("SpellBookLight");
            lightGo.layer = layer;
            lightGo.transform.SetParent(_modelRoot.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

            var cameraGo = new GameObject("SpellBookCamera");
            cameraGo.layer = layer;
            _modelCamera = cameraGo.AddComponent<Camera>();
            _modelCamera.clearFlags = CameraClearFlags.SolidColor;
            _modelCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _modelCamera.cullingMask = 1 << layer;
            _modelCamera.orthographic = true;
            _modelCamera.aspect = 1f;
            _modelCamera.nearClipPlane = 0.1f;
            _modelCamera.farClipPlane = 100f;
            _modelCamera.targetTexture = _modelTexture;
            cameraGo.transform.position = new Vector3(24f, 0f, 0f);
            cameraGo.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            _modelCamera.orthographicSize = 5.0f;
            _modelCamera.enabled = false;
            _modelImage.gameObject.SetActive(true);
            // The model contains the authored page and frame. Keep the flat
            // texture only as a fallback for a machine without the mesh import.
            var flat = _root.transform.Find("OriginalBook");
            if (flat != null) flat.gameObject.SetActive(false);
        }

        public void Show(H5MapObject hero)
        {
            if (_root == null) return;
            _hero = hero;
            _tabIndex = 0;
            _schoolFilter = null;
            _pageIndex = 0;
            BuildModel();
            _root.SetActive(true);
            if (_modelCamera != null) _modelCamera.enabled = true;
            RefreshPage();
        }

        public void Toggle(H5MapObject hero)
        {
            if (IsOpen) Hide();
            else Show(hero);
        }

        public void Hide()
        {
            if (_modelCamera != null) _modelCamera.enabled = false;
            if (_root != null) _root.SetActive(false);
        }

        void SetTab(int tab)
        {
            _tabIndex = Mathf.Clamp(tab, 0, 1);
            _schoolFilter = null;
            _pageIndex = 0;
            RefreshPage();
        }

        void SetSchool(string school)
        {
            _tabIndex = 0;
            _schoolFilter = school;
            _pageIndex = 0;
            RefreshPage();
        }

        void SetPage(int page)
        {
            _pageIndex = Mathf.Clamp(page, 0, 1);
            RefreshPage();
        }

        void RefreshPage()
        {
            foreach (var item in _pageItems)
                if (item != null) Destroy(item);
            _pageItems.Clear();
            foreach (var sprite in _iconSprites)
                if (sprite != null) Destroy(sprite);
            _iconSprites.Clear();

            if (_owner == null || _owner.GameState == null || _owner.Database == null)
                return;
            if (_hero == null || !_hero.IsHero)
                _hero = FirstLocalHero();
            var state = _hero != null ? _owner.GameState.GetHeroState(_hero) : null;
            var spells = BookSpells(state);
            var firstPageSize = _desc != null && _desc.firstPageParts != null
                ? _desc.firstPageParts.Count : 6;
            var secondPageSize = _desc != null && _desc.secondPageParts != null
                ? _desc.secondPageParts.Count : firstPageSize;
            if (firstPageSize < 1) firstPageSize = 6;
            if (secondPageSize < 1) secondPageSize = firstPageSize;
            var start = _pageIndex == 0 ? 0 : firstPageSize;
            var pageSize = _pageIndex == 0 ? firstPageSize : secondPageSize;
            var shown = 0;
            for (var i = start; i < spells.Count && shown < pageSize; i++, shown++)
                AddSpellSlot(spells[i], shown, pageSize);

            if (_points != null)
            {
                var mana = state != null ? state.Mana : 0f;
                var max = state != null ? state.MaxMana : 0f;
                _points.text = $"Spell points  {mana:0}/{max:0}";
            }
            if (_title != null)
                _title.text = TabName() + (spells.Count == 0 ? "" :
                    $"   —   page {_pageIndex + 1}");
            for (var i = 0; i < _tabButtons.Count; i++)
                if (_tabButtons[i] != null) _tabButtons[i].interactable = i != _tabIndex;
            for (var i = 0; i < _schoolButtons.Count; i++)
                if (_schoolButtons[i] != null)
                    _schoolButtons[i].interactable = _tabIndex != 0 || _schoolFilter == null;
        }

        string TabName()
        {
            return _tabIndex == 1 ? "Adventure Spells" : "Combat Spells";
        }

        List<SpellDef> BookSpells(H5HeroState state)
        {
            var result = new List<SpellDef>();
            if (state == null || state.Spells == null) return result;
            foreach (var id in state.Spells)
            {
                var spell = _owner.Database.GetSpell(id);
                if (spell == null) continue;
                var combat = string.Equals(spell.category, "Combat_Spells", StringComparison.OrdinalIgnoreCase);
                var adventure = string.Equals(spell.category, "Adventure_Spells", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(spell.school, "MAGIC_SCHOOL_ADVENTURE", StringComparison.OrdinalIgnoreCase);
                if ((_tabIndex == 0 && !combat) || (_tabIndex == 1 && (!adventure || combat))) continue;
                if (_tabIndex == 0 && !string.IsNullOrEmpty(_schoolFilter) &&
                    !string.Equals(spell.school, _schoolFilter, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(spell);
            }
            return result;
        }

        H5MapObject FirstLocalHero()
        {
            var heroes = _owner.GameState.HeroesOf(_owner.GameState.LocalPlayer);
            return heroes.Count > 0 ? heroes[0] : null;
        }

        void AddSpellSlot(SpellDef spell, int slot, int pageSize)
        {
            var cell = new GameObject("SpellSlot_" + slot, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = cell.GetComponent<RectTransform>();
            rect.SetParent(_page, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var col = slot % 3;
            var row = slot / 3;
            rect.sizeDelta = new Vector2(205f, 190f);
            rect.anchoredPosition = new Vector2(-215f + col * 215f, 105f - row * 210f);
            var image = cell.GetComponent<Image>();
            image.color = new Color(0.08f, 0.07f, 0.04f, 0.25f);
            image.raycastTarget = true;
            var button = cell.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                if (_details != null)
                    _details.text = spell.DisplayName + "\n" + (spell.description ?? "") +
                                    $"\nMana: {spell.manaCost}";
            });
            _pageItems.Add(cell);

            var icon = MakeSpellSprite(spell.icon);
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.SetParent(cell.transform, false);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
                iconRect.pivot = new Vector2(0.5f, 1f);
                iconRect.sizeDelta = new Vector2(128f, 128f);
                iconRect.anchoredPosition = new Vector2(0f, -4f);
                var iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                _pageItems.Add(iconGo);
            }
            var label = MakeText(cell.transform, "Name", 13, FontStyle.Bold,
                                 TextAnchor.MiddleCenter, new Vector2(-100f, 8f), new Vector2(100f, 38f));
            label.text = spell.DisplayName;
            label.raycastTarget = false;
            var cost = MakeText(cell.transform, "Cost", 11, FontStyle.Normal,
                                TextAnchor.MiddleCenter, new Vector2(-100f, -4f), new Vector2(100f, 18f));
            cost.text = spell.manaCost + " mana";
            cost.raycastTarget = false;
            _pageItems.Add(label.gameObject);
            _pageItems.Add(cost.gameObject);
        }

        Sprite MakeSpellSprite(string source)
        {
            if (_owner == null || _owner.Textures == null || string.IsNullOrEmpty(source)) return null;
            var texture = _owner.Textures.Get(source);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                       Vector2.one * 0.5f, 100f);
            sprite.name = "AdventureSpell_" + source;
            _iconSprites.Add(sprite);
            return sprite;
        }

        static Text MakeText(Transform parent, string name, int size, FontStyle style,
                             TextAnchor alignment, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(max.x - min.x, max.y - min.y);
            rect.anchoredPosition = new Vector2((min.x + max.x) * 0.5f,
                                                (min.y + max.y) * 0.5f);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.96f, 0.91f, 0.76f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 position,
                                 Vector2 size, Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.12f, 0.06f, 0.85f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action?.Invoke());
            var text = MakeText(go.transform, "Label", 20, FontStyle.Bold,
                                TextAnchor.MiddleCenter,
                                new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                                new Vector2(size.x * 0.5f, size.y * 0.5f));
            text.text = label;
            text.raycastTarget = false;
            return button;
        }

        static void SetButtonSprite(Button button, Sprite sprite)
        {
            if (button == null || sprite == null) return;
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
            }
        }

        void OnDestroy()
        {
            foreach (var sprite in _iconSprites)
                if (sprite != null) Destroy(sprite);
            if (_modelCamera != null) Destroy(_modelCamera.gameObject);
            if (_modelRoot != null) Destroy(_modelRoot);
            if (_modelTexture != null)
            {
                _modelTexture.Release();
                Destroy(_modelTexture);
            }
            _skin?.Dispose();
        }
    }
}
