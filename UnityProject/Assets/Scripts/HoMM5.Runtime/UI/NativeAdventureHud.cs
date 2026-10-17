using System.Collections.Generic;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>Original adventure-screen chrome over the existing gameplay callbacks.</summary>
    public sealed class NativeAdventureHud : MonoBehaviour
    {
        H5UiSkin _skin;
        HoMM5Hud _owner;
        readonly List<Text> _values = new List<Text>();
        Text _date;
        string _calendar;
        string _turnOwner;
        Transform _roster;
        readonly List<GameObject> _rosterItems = new List<GameObject>();
        string _rosterSignature;
        Button _endTurn;
        float _refreshAt;
        public H5Minimap Minimap { get; private set; }

        public static NativeAdventureHud Create(Transform parent, HoMM5Hud owner)
        {
            var skin = H5UiSkin.Load();
            if (skin == null) return null;
            if (skin.GetWindow("resources") == null || skin.GetWindow("minimap") == null ||
                skin.GetWindow("dial") == null || skin.GetWindow("endTurn") == null)
            { skin.Dispose(); return null; }
            var go = new GameObject("NativeAdventureHUD", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var hud = go.AddComponent<NativeAdventureHud>();
            hud._skin = skin;
            hud._owner = owner;
            hud.Build();
            return hud;
        }

        void Build()
        {
            var resources = _skin.CreateWindow(transform, "resources", true);
            foreach (var name in new[] { "wood", "ore", "mercury", "crystals", "sulfur", "gems", "gold" })
            {
                _skin.CreateWindow(resources.transform, "resource:" + name);
                var text = MakeText(resources.transform, "value:" + name, 16);
                var layout = _skin.GetWindow("value:" + name);
                if (layout != null) H5UiSkin.Place(text.rectTransform, layout);
                text.alignment = TextAnchor.MiddleLeft;
                _values.Add(text);
            }
            _date = MakeText(transform, "Date", 13);
            _date.rectTransform.anchorMin = _date.rectTransform.anchorMax = new Vector2(0.5f, 1);
            _date.rectTransform.pivot = new Vector2(0.5f, 1);
            _date.rectTransform.sizeDelta = new Vector2(520, 22);
            _date.rectTransform.anchoredPosition = new Vector2(0, -50);

            var frame = _skin.CreateWindow(transform, "minimap");
            Minimap = H5Minimap.Create(frame.transform, Vector2.zero, new Vector2(181, 181));
            var mapRect = Minimap.GetComponent<RectTransform>();
            mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = Vector2.zero;
            mapRect.anchoredPosition = new Vector2(23, 12);

            _skin.CreateWindow(transform, "dial");
            _endTurn = _skin.CreateButton(transform, "endTurn", () => _owner.EndDayRequested?.Invoke());
            _skin.CreateButton(transform, "Menu", () => _owner.MenuRequested?.Invoke());
            _skin.CreateButton(transform, "NextHero", () => _owner.NextHeroRequested?.Invoke());
            _skin.CreateButton(transform, "Scenario", () => _owner.ScenarioRequested?.Invoke());
            _skin.CreateButton(transform, "Spellbook", () => _owner.HeroScreenRequested?.Invoke());
            _skin.CreateButton(transform, "Underground", () => _owner.FloorRequested?.Invoke());
            // Path planning / queued movement is not implemented: do not make
            // the original Move Hero icon pretend to execute a route.
            var move = _skin.CreateButton(transform, "MoveHero", null);
            if (move != null) move.interactable = false;

            var roster = new GameObject("HeroTownRoster", typeof(RectTransform));
            var rr = roster.GetComponent<RectTransform>();
            rr.SetParent(transform, false);
            rr.anchorMin = rr.anchorMax = rr.pivot = Vector2.zero;
            rr.anchoredPosition = new Vector2(249, 13);
            rr.sizeDelta = new Vector2(400, 152);
            _roster = rr;
        }

        public void SetDate(int day, int week, int month)
        {
            _calendar = $"Day {day}   ·   Week {week}   ·   Month {month}";
            if (_date != null) _date.text = _calendar + _turnOwner;
        }

        public void SetCurrentPlayer(string name, bool isLocal)
        {
            _turnOwner = string.IsNullOrEmpty(name) ? "" : "   ·   " + name + (isLocal ? "" : " (thinking)");
            if (_date != null) _date.text = _calendar + _turnOwner;
        }

        public void SetResources(PlayerResourceInventory resources)
        {
            if (resources == null) return;
            var values = new[] { resources.wood, resources.ore, resources.mercury, resources.crystal,
                resources.sulfur, resources.gem, resources.gold };
            for (var i = 0; i < _values.Count; i++) _values[i].text = values[i].ToString();
        }

        void Update()
        {
            if (_owner == null || _owner.GameState == null || Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 0.25f;
            var state = _owner.GameState;
            if (_endTurn != null) _endTurn.interactable = !state.HasMovingHero &&
                (state.Players.Players.Count == 0 || state.IsLocalTurn);
            RefreshRoster(state);
        }

        void RefreshRoster(AdventureMapGameState state)
        {
            var heroes = state.HeroesOf(state.LocalPlayer);
            var towns = state.OwnedTowns();
            var signature = state.MapId;
            foreach (var hero in heroes) signature += "|" + hero.StateKey + ":" + hero.GetEntityId().ToString();
            foreach (var town in towns) signature += "|" + town.ObjectId + ":" + town.TownType + ":" + town.Name;
            if (signature == _rosterSignature) return;
            _rosterSignature = signature;
            foreach (var item in _rosterItems) Destroy(item);
            _rosterItems.Clear();
            for (var column = 0; column < Mathf.Min(Mathf.Max(heroes.Count, towns.Count), 5); column++)
            {
                var frame = new GameObject("ListBorder", typeof(RectTransform), typeof(Image));
                var rect = frame.GetComponent<RectTransform>();
                rect.SetParent(_roster, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(column * 67, 0);
                rect.sizeDelta = new Vector2(61, 152);
                frame.GetComponent<Image>().sprite = _skin.GetSprite("rosterFrame");
                frame.GetComponent<Image>().raycastTarget = false;
                _rosterItems.Add(frame);
            }
            for (var i = 0; i < Mathf.Min(heroes.Count, 5); i++)
            {
                var hero = heroes[i];
                var button = RosterButton(i, 76, "Hero");
                var hs = state.GetHeroState(hero);
                Sprite face = null;
                if (_owner.Database != null && hs != null)
                    foreach (var entry in _owner.Database.Heroes)
                        if (entry.Value != null && entry.Value.DisplayName == hs.HeroName)
                        { face = _skin.GetSprite("portrait:" + entry.Key); break; }
                if (face == null && hero.Def != null) face = _skin.GetPortrait(hero.Def.shared);
                if (face != null) { button.image.sprite = face; button.image.color = Color.white; }
                else MakeText(button.transform, "Name", 11).text = hs != null ? hs.HeroName : "Hero";
                button.onClick.AddListener(() =>
                {
                    if (_owner.Controller == null || hero == null) return;
                    _owner.Controller.Select(hero);
                    _owner.Controller.FocusOn(hero.transform.position);
                });
            }
            for (var i = 0; i < Mathf.Min(towns.Count, 5); i++)
            {
                var town = towns[i];
                var button = RosterButton(i, 8, "Town");
                var icon = _skin.GetSprite("town:" + town.TownType);
                if (icon != null) { button.image.sprite = icon; button.image.color = Color.white; }
                else MakeText(button.transform, "TownName", 10).text = town.Name;
                button.onClick.AddListener(() => _owner.OpenTown(town));
            }
        }

        Button RosterButton(int column, float y, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_roster, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(61, 61);
            rect.anchoredPosition = new Vector2(column * 67, y);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.10f, 0.08f, 0.95f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            _rosterItems.Add(go);
            return button;
        }

        static Text MakeText(Transform parent, string name, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.91f, 0.80f);
            text.raycastTarget = false;
            return text;
        }

        void OnDestroy() { _skin?.Dispose(); }
    }
}
