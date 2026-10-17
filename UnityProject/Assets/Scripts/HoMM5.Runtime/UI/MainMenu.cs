using System;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The main menu and the new-game setup, in one panel.
    ///
    /// This is the front door the game was missing: before it, <c>H5Bootstrap</c>
    /// loaded the first map in the index on <c>Start</c> and there was no way to
    /// choose anything. The flow here is the one the shipped game uses --
    /// New Game, then a map, then a faction, then a hero, then play -- with
    /// Continue and Load alongside it.
    ///
    /// Built in code like the rest of the HUD, so the project still ships
    /// without hand-authored UI assets. The shipped menu's painted art and
    /// animated background are not reproduced; the choices and the data behind
    /// them are.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [System.NonSerialized] public GameDatabase Database;

        /// <summary>Raised when the player commits a new game.</summary>
        public Action<NewGameSetup> StartRequested;
        public Action LoadRequested;
        public Action ResumeRequested;
        public Action QuitRequested;

        H5UiSkin _skin;
        GameObject _root;
        UnityEngine.UI.Text _title;
        UnityEngine.UI.Text _status;
        RectTransform _body;

        // Which step of the setup the panel is on.
        int _step;
        NewGameSetup _pending = new NewGameSetup();
        List<MapIndexEntry> _maps = new List<MapIndexEntry>();
        List<TownTypeDef> _factions = new List<TownTypeDef>();
        List<HeroDef> _heroes = new List<HeroDef>();

        public bool IsOpen => _root != null && _root.activeInHierarchy;

        public static MainMenu Create(Transform canvas, GameDatabase database = null)
        {
            var go = new GameObject("MainMenu", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var menu = go.AddComponent<MainMenu>();
            menu.Database = database;
            menu.Build();
            return menu;
        }

        // -- construction ---------------------------------------------------

        void Build()
        {
            // A full-screen raycast target prevents clicks through the menu.
            _root = new GameObject("MenuOverlay", typeof(RectTransform), typeof(Image));
            var overlay = _root.GetComponent<RectTransform>();
            overlay.SetParent(transform, false);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0.02f, 0.025f, 0.04f, 1f);
            _skin = H5UiSkin.Load();
            if (_skin != null)
            {
                var logo = new GameObject("OriginalLogo", typeof(RectTransform), typeof(Image));
                var lr = logo.GetComponent<RectTransform>();
                lr.SetParent(overlay, false);
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1);
                lr.pivot = new Vector2(0.5f, 1);
                lr.anchoredPosition = new Vector2(0, -12);
                lr.sizeDelta = new Vector2(380, 112);
                var li = logo.GetComponent<Image>();
                li.sprite = _skin.GetSprite("menuLogo");
                li.preserveAspect = true;
                li.raycastTarget = false;
            }
            var panel = MakePanel(overlay, "MenuPanel", new Vector2(-500, -330), new Vector2(500, 330));
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = _skin != null ? _skin.GetSprite("menuPanel") : null;
            panelImage.color = panelImage.sprite != null ? Color.white : new Color(0.05f, 0.06f, 0.09f, 0.98f);

            _title = MakeText(panel.transform, "Title", 34, FontStyle.Bold, TextAnchor.MiddleCenter,
                              new Vector2(0, -84), new Vector2(0, -26));
            _title.rectTransform.anchorMin = new Vector2(0, 1);
            _title.rectTransform.anchorMax = Vector2.one;
            _title.text = "Heroes of Might and Magic V";
            _title.color = _skin != null ? new Color(0.24f, 0.14f, 0.07f) : new Color(0.86f, 0.78f, 0.58f);

            _body = MakeScroll(panel.transform, "Body", new Vector2(40, 70), new Vector2(-40, -96));

            var back = MakeButton(panel.transform, "BackButton", "Back", new Vector2(0.5f, 0),
                                  new Vector2(-40, 22), new Vector2(40, 58), OnBack);
            back.gameObject.SetActive(false);
            _backButton = back;

            _status = MakeText(panel.transform, "Status", 14, FontStyle.Normal, TextAnchor.MiddleCenter,
                               new Vector2(18, 62), new Vector2(-18, 92));
            if (_skin != null) _status.color = new Color(0.35f, 0.14f, 0.08f);
            _status.rectTransform.anchorMin = Vector2.zero;
            _status.rectTransform.anchorMax = new Vector2(1, 0);
            ShowRoot();
        }

        public void SetStatus(string message)
        {
            if (_status != null) _status.text = message ?? "";
        }

        Button _backButton;

        void ShowBack(bool visible)
        {
            if (_backButton != null) _backButton.gameObject.SetActive(visible);
        }

        // -- step 0: the root menu -----------------------------------------

        public void ShowRoot()
        {
            SetStatus("");
            _step = 0;
            _title.text = "Heroes of Might and Magic V";
            ShowBack(false);
            Clear();

            var rows = new List<MenuRow>
            {
                new MenuRow { Label = "New Game", Note = "Choose a map, a faction and a hero",
                              Action = ShowMaps, Bold = true },
                new MenuRow { Label = "Continue", Note = "Load the last quick save (F9 in game)",
                              Action = () => { if (LoadRequested != null) LoadRequested(); } },
                new MenuRow { Label = "Quit", Note = "Close the game",
                              Action = () => { if (QuitRequested != null) QuitRequested(); } },
            };

            if (ResumeRequested != null)
                rows.Insert(0, new MenuRow { Label = "Resume", Note = "Return to the current scenario",
                    Action = () => ResumeRequested?.Invoke(), Bold = true });

            if (Database == null || !Database.IsLoaded)
            {
                rows.Clear();
                rows.Add(new MenuRow
                {
                    Label = "Import not found",
                    Note = "Run: python import_to_unity.py",
                    Bold = true,
                });
            }

            Render(rows);
        }

        // -- step 1: the map ------------------------------------------------

        void ShowMaps()
        {
            _step = 1;
            _title.text = "Choose a scenario";
            ShowBack(true);
            Clear();

            _maps.Clear();
            if (Database != null && Database.MapIndex != null)
            {
                foreach (var entry in Database.MapIndex)
                {
                    // Combat arenas are battlefield scenery, not scenarios;
                    // offering one here would start a game with no town.
                    if (entry == null || entry.arena) continue;
                    _maps.Add(entry);
                }
            }

            var rows = new List<MenuRow>();
            foreach (var map in _maps)
            {
                var captured = map;
                rows.Add(new MenuRow
                {
                    Label = map.id,
                    Note = $"{map.tileX}x{map.tileY}   {map.objects} objects",
                    Action = () => PickMap(captured),
                });
            }
            if (rows.Count > 1)
            {
                // The shipped Multiplayer scenarios author a PlayerID per town,
                // and six of the forty are written for exactly two players.
                // Saying so in the list is the difference between picking a
                // duel map and picking one with six opponents on it.
                rows.Insert(0, new MenuRow
                {
                    Label = "Tip",
                    Note = "A1M3, A1S1, A2M4, A2M12, A2M13 and S4 are written for two players",
                });
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow { Label = "No maps were imported", Note = "" });
            Render(rows);
        }

        void PickMap(MapIndexEntry map)
        {
            _pending.MapId = map != null ? map.id : "";
            _pending.MapName = _pending.MapId;
            ShowFactions();
        }

        // -- step 2: the faction (the castle) -------------------------------

        void ShowFactions()
        {
            _step = 2;
            _title.text = "Choose your faction";
            ShowBack(true);
            Clear();

            _factions.Clear();
            if (Database != null && Database.TownTypes != null)
            {
                foreach (var kv in Database.TownTypes)
                {
                    var townType = kv.Value;
                    // Only the eight playable factions: RANDOM_TYPE, NO_TYPE and
                    // SPECIAL have no buildings to run a town on.
                    if (townType == null || string.IsNullOrEmpty(townType.faction)) continue;
                    if (townType.faction == "RandomTown") continue;
                    if (townType.buildings == null || townType.buildings.Count == 0) continue;
                    _factions.Add(townType);
                }
            }
            _factions.Sort((a, b) => string.CompareOrdinal(a.faction, b.faction));

            var rows = new List<MenuRow>();
            foreach (var townType in _factions)
            {
                var captured = townType;
                rows.Add(new MenuRow
                {
                    Label = string.IsNullOrEmpty(townType.name) ? townType.faction : townType.name,
                    Note = $"{townType.faction}   {townType.buildings.Count} buildings   " +
                           $"{(townType.buildings != null ? townType.buildings.Count : 0)} slots",
                    Action = () => PickFaction(captured),
                });
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow { Label = "No town types were imported", Note = "" });
            Render(rows);
        }

        void PickFaction(TownTypeDef townType)
        {
            _pending.TownType = townType != null ? townType.id : "";
            _pending.Faction = townType != null ? townType.faction : "";
            _pending.FactionName = townType != null && !string.IsNullOrEmpty(townType.name)
                ? townType.name : _pending.Faction;
            ShowHeroes();
        }

        // -- step 3: the hero ----------------------------------------------

        void ShowHeroes()
        {
            _step = 3;
            _title.text = "Choose your hero";
            ShowBack(true);
            Clear();

            // The named heroes of the chosen faction (the random-map pools).
            // Their class, specialty and starting stats come with the record.
            _heroes.Clear();
            if (Database != null)
                _heroes.AddRange(Database.HeroesForTown(_pending.TownType));

            var rows = new List<MenuRow>();
            foreach (var hero in _heroes)
            {
                if (hero == null) continue;
                var captured = hero;
                var heroClass = Database.GetHeroClass(hero.heroClass);
                var className = heroClass != null ? heroClass.DisplayName
                                                  : GameDatabase.PrettifyEnum(hero.heroClass);
                var note = string.IsNullOrEmpty(hero.specializationName)
                    ? className
                    : className + " · " + hero.specializationName;
                rows.Add(new MenuRow
                {
                    Label = hero.DisplayName,
                    Note = note,
                    Action = () => PickHero(captured),
                });
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow { Label = "This faction has no named heroes", Note = "" });
            Render(rows);
        }

        void PickHero(HeroDef hero)
        {
            _pending.HeroId = hero != null ? hero.id : "";
            _pending.HeroName = hero != null ? hero.DisplayName : "";
            _pending.HeroClass = hero != null ? hero.heroClass ?? "" : "";
            var heroClass = hero != null && Database != null ? Database.GetHeroClass(hero.heroClass) : null;
            _pending.HeroClassName = heroClass != null ? heroClass.DisplayName : _pending.HeroClass;
            ShowConfirm();
        }

        // -- step 4: confirm ------------------------------------------------

        void ShowConfirm()
        {
            _step = 4;
            _title.text = "Start the game";
            ShowBack(true);
            Clear();

            var rows = new List<MenuRow>
            {
                new MenuRow { Label = "Scenario", Note = _pending.MapName },
                new MenuRow { Label = "Faction", Note = _pending.FactionName },
                new MenuRow { Label = "Hero", Note = _pending.HeroName + " · " + _pending.HeroClassName },
                new MenuRow { Label = "Opponent", Note = "Computer" },
            };
            Render(rows);

            var start = MakeButton(_body, "StartButton", "  Start  ", new Vector2(0.5f, 0),
                                   new Vector2(-110, 16), new Vector2(110, 62), OnStart);
            var startText = start.GetComponentInChildren<UnityEngine.UI.Text>();
            if (startText != null)
            {
                startText.fontSize = 20;
                startText.fontStyle = FontStyle.Bold;
            }
            start.image.color = new Color(0.30f, 0.42f, 0.26f, 1f);
        }

        void OnStart()
        {
            if (string.IsNullOrEmpty(_pending.MapId)) return;
            _pending.PlayerName = "Player 1";
            _pending.OpponentName = "Computer";
            if (StartRequested != null) StartRequested(_pending);
        }

        void OnBack()
        {
            switch (_step)
            {
                case 1: ShowRoot(); break;
                case 2: ShowMaps(); break;
                case 3: ShowFactions(); break;
                case 4: ShowHeroes(); break;
                default: ShowRoot(); break;
            }
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Show()
        {
            if (_root == null) return;
            _root.SetActive(true);
            ShowRoot();
        }

        // -- rows -----------------------------------------------------------

        struct MenuRow
        {
            public string Label;
            public string Note;
            public Action Action;
            public bool Bold;
        }

        void Clear()
        {
            if (_body == null) return;
            for (var i = _body.childCount - 1; i >= 0; i--)
                Destroy(_body.GetChild(i).gameObject);
        }

        void Render(List<MenuRow> rows)
        {
            if (_body == null) return;
            var y = 0f;
            foreach (var row in rows)
            {
                var go = new GameObject("Row", typeof(RectTransform));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(_body, false);
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(0.5f, 1);
                rect.offsetMin = new Vector2(8, -y - 46);
                rect.offsetMax = new Vector2(-8, -y);
                y += 52;

                var image = go.AddComponent<Image>();
                image.color = row.Action != null
                    ? new Color(0.16f, 0.17f, 0.22f, 0.95f)
                    : new Color(0.10f, 0.10f, 0.13f, 0.8f);

                var label = MakeText(go.transform, "Label", row.Bold ? 19 : 16,
                                     row.Bold ? FontStyle.Bold : FontStyle.Normal,
                                     TextAnchor.MiddleLeft,
                                     new Vector2(16, 0), new Vector2(-320, 0));
                label.text = row.Label ?? "";
                if (_skin != null) label.color = new Color(0.98f, 0.91f, 0.75f);

                var note = MakeText(go.transform, "Note", 13, FontStyle.Italic,
                                    TextAnchor.MiddleRight,
                                    new Vector2(300, 0), new Vector2(-16, 0));
                note.text = row.Note ?? "";
                note.color = new Color(0.62f, 0.62f, 0.58f);

                if (row.Action != null)
                {
                    var captured = row.Action;
                    var button = go.AddComponent<Button>();
                    button.onClick.AddListener(() => { HoMM5.Audio.H5Audio.Click(); captured(); });
                }
            }
            // The content must fit its rows. A fixed 2000px left Start far
            // below short lists and clipped long scenario lists.
            _body.sizeDelta = new Vector2(0, y + 80f);
            _body.anchoredPosition = Vector2.zero;
        }

        void OnDestroy() { _skin?.Dispose(); }

        // -- widgets --------------------------------------------------------

        static GameObject MakePanel(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return go;
        }

        static UnityEngine.UI.Text MakeText(Transform parent, string name, int size, FontStyle style,
                                            TextAnchor anchor, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var text = go.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = new Color(0.9f, 0.88f, 0.82f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform MakeScroll(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            go.AddComponent<RectMask2D>();
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(go.transform, false);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 2000);

            scroll.content = contentRect;
            return contentRect;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 anchor,
                                 Vector2 offsetMin, Vector2 offsetMax, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.22f, 0.24f, 0.30f, 1f);

            var text = MakeText(go.transform, "Label", 15, FontStyle.Bold, TextAnchor.MiddleCenter,
                                Vector2.zero, Vector2.zero);
            text.text = label;

            var button = go.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(() => { HoMM5.Audio.H5Audio.Click(); onClick(); });
            return button;
        }
    }

    /// <summary>What the player chose in the menu, handed to the game state.</summary>
    [System.Serializable]
    public class NewGameSetup
    {
        public string MapId = "";
        public string MapName = "";
        public string Faction = "";
        public string FactionName = "";
        public string TownType = "";
        /// <summary>Id of the named roster hero (heroes.json key); "" when none.</summary>
        public string HeroId = "";
        public string HeroName = "";
        public string HeroClass = "";
        public string HeroClassName = "";
        public string PlayerName = "Player 1";
        public string OpponentName = "Computer";

        /// <summary>0 easiest .. 3 hardest, matching DefaultStats' rows.</summary>
        public int Difficulty = 2;
    }
}
