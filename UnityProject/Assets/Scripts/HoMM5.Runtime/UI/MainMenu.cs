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
        public Action QuitRequested;

        GameObject _root;
        UnityEngine.UI.Text _title;
        RectTransform _body;

        // Which step of the setup the panel is on.
        int _step;
        NewGameSetup _pending = new NewGameSetup();
        List<MapIndexEntry> _maps = new List<MapIndexEntry>();
        List<TownTypeDef> _factions = new List<TownTypeDef>();
        List<HeroClassDef> _heroes = new List<HeroClassDef>();

        public bool IsOpen => _root != null && _root.activeSelf;

        public static MainMenu Create(Transform canvas)
        {
            var go = new GameObject("MainMenu");
            go.transform.SetParent(canvas, false);
            var menu = go.AddComponent<MainMenu>();
            menu.Build();
            return menu;
        }

        // -- construction ---------------------------------------------------

        void Build()
        {
            _root = MakePanel(transform, "MenuPanel", new Vector2(-500, -330), new Vector2(500, 330));
            _root.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.98f);

            _title = MakeText(_root.transform, "Title", 34, FontStyle.Bold, TextAnchor.MiddleCenter,
                              new Vector2(0, -26), new Vector2(0, -84));
            _title.text = "Heroes of Might and Magic V";
            _title.color = new Color(0.86f, 0.78f, 0.58f);

            _body = MakeScroll(_root.transform, "Body", new Vector2(40, 70), new Vector2(-40, -96));

            var back = MakeButton(_root.transform, "BackButton", "Back", new Vector2(0, 0),
                                  new Vector2(-40, 22), new Vector2(40, 58), OnBack);
            back.gameObject.SetActive(false);
            _backButton = back;

            ShowRoot();
        }

        Button _backButton;

        void ShowBack(bool visible)
        {
            if (_backButton != null) _backButton.gameObject.SetActive(visible);
        }

        // -- step 0: the root menu -----------------------------------------

        public void ShowRoot()
        {
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

            if (Database == null || !Database.IsLoaded)
            {
                rows.Clear();
                rows.Add(new MenuRow
                {
                    Label = "Import not found",
                    Note = "Run: python -m homm5import.cli all",
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

            _heroes.Clear();
            if (Database != null && Database.HeroClasses != null)
            {
                // HeroClassDef carries no faction: the eight classes are not
                // tagged to towns in the imported data. The faction's own class
                // is offered first, then the rest, so the natural choice is on
                // top. ClassForTown maps a TOWN_* id to the class that serves it.
                var preferred = H5Map.AdventureMapGameState.ClassForTown(_pending.TownType);
                HeroClassDef first = null;
                foreach (var kv in Database.HeroClasses)
                {
                    var heroClass = kv.Value;
                    if (heroClass == null) continue;
                    if (string.Equals(heroClass.id, preferred, StringComparison.OrdinalIgnoreCase))
                        first = heroClass;
                    else
                        _heroes.Add(heroClass);
                }
                if (first != null) _heroes.Insert(0, first);
            }

            var rows = new List<MenuRow>();
            foreach (var heroClass in _heroes)
            {
                if (heroClass == null) continue;
                var captured = heroClass;
                rows.Add(new MenuRow
                {
                    Label = string.IsNullOrEmpty(heroClass.name) ? heroClass.id : heroClass.name,
                    Note = heroClass.id,
                    Action = () => PickHero(captured),
                });
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow { Label = "No hero classes were imported", Note = "" });
            Render(rows);
        }

        void PickHero(HeroClassDef heroClass)
        {
            _pending.HeroClass = heroClass != null ? heroClass.id : "";
            _pending.HeroClassName = heroClass != null && !string.IsNullOrEmpty(heroClass.name)
                ? heroClass.name : _pending.HeroClass;
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
                new MenuRow { Label = "Hero", Note = _pending.HeroClassName },
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
        }

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
        public string HeroClass = "";
        public string HeroClassName = "";
        public string PlayerName = "Player 1";
        public string OpponentName = "Computer";

        /// <summary>0 easiest .. 3 hardest, matching DefaultStats' rows.</summary>
        public int Difficulty = 2;
    }
}
