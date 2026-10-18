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
    /// The MainMenu2 XDB panel, logo and button placements are imported from the
    /// shipped data. Runtime-only screens keep their existing data-driven flow,
    /// but root and single/multiplayer menu navigation is no longer a generic
    /// code-positioned replacement.
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
        GameObject _panelObject;
        GameObject _submenuPanelObject;
        UnityEngine.UI.Text _title;
        UnityEngine.UI.Text _status;
        RectTransform _body;
        readonly List<GameObject> _nativeRows = new List<GameObject>();
        readonly List<GameObject> _nativeSubRows = new List<GameObject>();

        // Which step of the setup the panel is on.
        int _step;
        NewGameSetup _pending = new NewGameSetup();
        List<MapIndexEntry> _maps = new List<MapIndexEntry>();
        List<TownTypeDef> _factions = new List<TownTypeDef>();
        List<HeroDef> _heroes = new List<HeroDef>();
        List<CampaignDef> _campaigns = new List<CampaignDef>();
        CampaignDef _selectedCampaign;
        CampaignMissionDef _selectedMission;

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
            var logoImage = _skin != null ? _skin.CreateWindow(overlay, "mainMenu:logo") : null;
            if (logoImage != null)
            {
                logoImage.gameObject.name = "OriginalLogo";
                logoImage.preserveAspect = true;
                logoImage.raycastTarget = false;
            }

            var panelImage = _skin != null ? _skin.CreateWindow(overlay, "mainMenu:panel", true) : null;
            GameObject panel;
            if (panelImage != null)
            {
                panel = panelImage.gameObject;
                panel.name = "MenuPanel";
                panelImage.preserveAspect = true;
                panelImage.color = Color.white;
            }
            else
            {
                panel = MakePanel(overlay, "MenuPanel", new Vector2(-500, -330), new Vector2(500, 330));
                panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.98f);
            }
            _panelObject = panel;

            _title = MakeText(panel.transform, "Title", 25, FontStyle.Bold, TextAnchor.MiddleCenter,
                              new Vector2(12, -48), new Vector2(-12, -12));
            _title.text = "Heroes of Might and Magic V";
            _title.color = _skin != null ? new Color(0.24f, 0.14f, 0.07f) : new Color(0.86f, 0.78f, 0.58f);

            _body = MakeScroll(panel.transform, "Body", new Vector2(12, 38), new Vector2(-12, -12));

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

        // -- authored MainMenu2 submenus ------------------------------------

        void ShowSingleMenu()
        {
            _step = 8;
            Clear();
            RenderSourceSubmenu("single", "SinglePlayer", new[]
            {
                new SourceRow("Campaign", "Campaign", ShowCampaigns),
                new SourceRow("UserCampaign", "User campaigns", () => SetStatus("User campaigns are not imported.")),
                new SourceRow("LoadGame", "Load game", () => LoadRequested?.Invoke()),
                new SourceRow("CustomGame", "Custom game", ShowMaps),
                new SourceRow("HallOfFame", "Hall of Fame", () => SetStatus("Hall of Fame data is not present in the imported runtime.")),
                new SourceRow("Back", "Back", ShowRoot),
            });
        }

        void ShowMultiMenu()
        {
            _step = 9;
            Clear();
            RenderSourceSubmenu("multi", "MultiPlayer", new[]
            {
                new SourceRow("HotSeat", "Hot seat", () => SetStatus("Hot-seat transport is not implemented in this runtime.")),
                new SourceRow("LAN", "LAN", () => SetStatus("LAN transport is not implemented in this runtime.")),
                new SourceRow("UbiCom", "Ubi.com", () => SetStatus("Ubi.com service is not available.")),
                new SourceRow("LoadGame", "Load game", () => LoadRequested?.Invoke()),
                new SourceRow("Back", "Back", ShowRoot),
            });
        }

        void ShowOptionsMenu()
        {
            SetStatus("Options are defined by the original GameOptions screen; runtime binding is not imported yet.");
        }

        void ShowCredits()
        {
            SetStatus("Credits are supplied by the original game data.");
        }

        readonly struct SourceRow
        {
            public readonly string Key;
            public readonly string Label;
            public readonly Action Action;
            public SourceRow(string key, string label, Action action)
            {
                Key = key;
                Label = label;
                Action = action;
            }
        }

        void RenderSourceSubmenu(string prefix, string name, SourceRow[] rows)
        {
            if (_skin == null || _root == null) return;
            _title.gameObject.SetActive(false);
            _body.gameObject.SetActive(false);
            if (_panelObject != null) _panelObject.SetActive(false);
            if (_submenuPanelObject != null) Destroy(_submenuPanelObject);
            var image = _skin.CreateWindow(_root.transform, "mainMenu:" + prefix + ":panel", true);
            if (image == null) return;
            _submenuPanelObject = image.gameObject;
            _submenuPanelObject.name = "Native" + name + "Panel";
            image.preserveAspect = true;
            image.color = Color.white;
            for (var i = 0; i < rows.Length; i++)
            {
                var source = _skin.GetWindow("mainMenu:" + prefix + ":" + rows[i].Key);
                if (source == null) continue;
                var go = new GameObject("Native" + name + "Button_" + rows[i].Key,
                                        typeof(RectTransform), typeof(Image), typeof(Button));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(_submenuPanelObject.transform, false);
                H5UiSkin.Place(rect, source);
                var buttonImage = go.GetComponent<Image>();
                buttonImage.color = new Color(0f, 0f, 0f, 0.01f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = buttonImage;
                var captured = rows[i].Action;
                button.onClick.AddListener(() => { HoMM5.Audio.H5Audio.Click(); captured?.Invoke(); });
                var label = MakeText(go.transform, "Label", 15, FontStyle.Bold,
                                     TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
                label.text = string.IsNullOrEmpty(source.text) ? rows[i].Label : source.text;
                label.color = new Color(0.93f, 0.84f, 0.62f);
                _nativeSubRows.Add(go);
            }
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
                new MenuRow { Label = "Campaign", Note = "Play an imported campaign mission",
                              Action = ShowCampaigns, Bold = true },
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

        // -- campaign flow --------------------------------------------------

        void ShowCampaigns()
        {
            _step = 5;
            _title.text = "Choose a campaign";
            ShowBack(true);
            Clear();
            _selectedCampaign = null;
            _selectedMission = null;

            _campaigns.Clear();
            if (Database != null && Database.Campaigns != null)
                foreach (var kv in Database.Campaigns)
                    if (kv.Value != null && !kv.Value.userCampaign) _campaigns.Add(kv.Value);
            _campaigns.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            var rows = new List<MenuRow>();
            foreach (var campaign in _campaigns)
            {
                var captured = campaign;
                rows.Add(new MenuRow
                {
                    Label = string.IsNullOrEmpty(campaign.fullName) ? campaign.name : campaign.fullName,
                    Note = (campaign.missions != null ? campaign.missions.Count : 0) + " missions",
                    Action = () => PickCampaign(captured),
                });
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow
                {
                    Label = "No campaigns were imported",
                    Note = "Run the gamedata import with the Tribes of the East overlay",
                });
            Render(rows);
        }

        void PickCampaign(CampaignDef campaign)
        {
            _selectedCampaign = campaign;
            ShowCampaignMissions();
        }

        void ShowCampaignMissions()
        {
            _step = 6;
            _title.text = _selectedCampaign != null
                ? (string.IsNullOrEmpty(_selectedCampaign.fullName)
                    ? _selectedCampaign.name : _selectedCampaign.fullName)
                : "Choose a mission";
            ShowBack(true);
            Clear();

            var rows = new List<MenuRow>();
            if (_selectedCampaign != null && _selectedCampaign.missions != null)
            {
                foreach (var mission in _selectedCampaign.missions)
                {
                    if (mission == null || string.IsNullOrEmpty(mission.mapId)) continue;
                    var captured = mission;
                    rows.Add(new MenuRow
                    {
                        Label = string.IsNullOrEmpty(mission.name) ? mission.mapId : mission.name,
                        Note = mission.mapId + "   " + mission.tileX + "x" + mission.tileY,
                        Action = () => PickCampaignMission(captured),
                    });
                }
            }
            if (rows.Count == 0)
                rows.Add(new MenuRow { Label = "No missions were imported", Note = "" });
            Render(rows);
        }

        void PickCampaignMission(CampaignMissionDef mission)
        {
            if (mission == null || string.IsNullOrEmpty(mission.mapId)) return;
            _selectedMission = mission;
            _pending.MapId = mission.mapId;
            _pending.MapName = string.IsNullOrEmpty(mission.name) ? mission.mapId : mission.name;
            _pending.CampaignId = _selectedCampaign != null ? _selectedCampaign.id : "";
            _pending.MissionId = mission.id;
            _pending.Faction = "";
            _pending.FactionName = "";
            _pending.TownType = "";
            _pending.HeroId = "";
            _pending.HeroName = "";
            _pending.HeroClass = "";
            _pending.HeroClassName = "";
            // Campaign maps carry their own hero, owner, army and authored
            // history. Do not replace that source state with the random-map
            // faction/hero picker used by New Game.
            ShowCampaignConfirm();
        }

        void ShowCampaignConfirm()
        {
            _step = 7;
            _title.text = "Start campaign mission";
            ShowBack(true);
            Clear();

            var rows = new List<MenuRow>
            {
                new MenuRow { Label = "Campaign", Note = _selectedCampaign != null
                    ? (_selectedCampaign.fullName ?? _selectedCampaign.name) : "" },
                new MenuRow { Label = "Mission", Note = _pending.MapName },
                new MenuRow { Label = "Map", Note = _pending.MapId },
                new MenuRow { Label = "Start state", Note = "Use the authored hero, army and towns" },
            };
            Render(rows);

            var start = MakeButton(_body, "StartCampaignButton", "  Start mission  ", new Vector2(0.5f, 0),
                                   new Vector2(-130, 16), new Vector2(130, 62), OnStart);
            var startText = start.GetComponentInChildren<UnityEngine.UI.Text>();
            if (startText != null)
            {
                startText.fontSize = 20;
                startText.fontStyle = FontStyle.Bold;
            }
            start.image.color = new Color(0.30f, 0.42f, 0.26f, 1f);
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
            _selectedCampaign = null;
            _selectedMission = null;
            _pending.CampaignId = "";
            _pending.MissionId = "";
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
                case 5: ShowRoot(); break;
                case 6: ShowCampaigns(); break;
                case 7: ShowCampaignMissions(); break;
                case 8: ShowRoot(); break;
                case 9: ShowRoot(); break;
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

        void RenderNativeRoot()
        {
            if (_submenuPanelObject != null)
            {
                Destroy(_submenuPanelObject);
                _submenuPanelObject = null;
            }
            _panelObject.SetActive(true);
            _title.gameObject.SetActive(false);
            _body.gameObject.SetActive(false);
            var rows = new[]
            {
                new { Source = "SingleGame", Label = "Single Game", Action = (Action)ShowSingleMenu },
                new { Source = "MultiPlayer", Label = "Multiplayer", Action = (Action)ShowMultiMenu },
                new { Source = "Options", Label = "Options", Action = (Action)ShowOptionsMenu },
                new { Source = "Credits", Label = "Credits", Action = (Action)ShowCredits },
                new { Source = "Quit", Label = "Quit", Action = (Action)(() => QuitRequested?.Invoke()) },
            };
            for (var i = 0; i < rows.Length; i++)
            {
                var source = _skin.GetWindow("mainMenu:" + rows[i].Source);
                if (source == null) continue;
                var go = new GameObject("NativeMenuButton_" + rows[i].Source,
                                        typeof(RectTransform), typeof(Image), typeof(Button));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(_panelObject.transform, false);
                H5UiSkin.Place(rect, source);
                var image = go.GetComponent<Image>();
                image.color = new Color(0, 0, 0, 0.01f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                var captured = rows[i].Action;
                button.onClick.AddListener(() => { HoMM5.Audio.H5Audio.Click(); captured?.Invoke(); });
                var label = MakeText(go.transform, "Label", 16, FontStyle.Bold,
                                     TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
                label.text = string.IsNullOrEmpty(source.text) ? rows[i].Label : source.text;
                label.color = new Color(0.93f, 0.84f, 0.62f);
                _nativeRows.Add(go);
            }
        }

        void Clear()
        {
            if (_body == null) return;
            for (var i = _body.childCount - 1; i >= 0; i--)
                Destroy(_body.GetChild(i).gameObject);
            for (var i = _nativeRows.Count - 1; i >= 0; i--)
                if (_nativeRows[i] != null) Destroy(_nativeRows[i]);
            _nativeRows.Clear();
            for (var i = _nativeSubRows.Count - 1; i >= 0; i--)
                if (_nativeSubRows[i] != null) Destroy(_nativeSubRows[i]);
            _nativeSubRows.Clear();
            if (_submenuPanelObject != null)
            {
                Destroy(_submenuPanelObject);
                _submenuPanelObject = null;
            }
        }

        void Render(List<MenuRow> rows)
        {
            if (_body == null) return;
            if (_step == 0 && _skin != null && _skin.GetWindow("mainMenu:SingleGame") != null)
            {
                RenderNativeRoot();
                return;
            }
            if (_panelObject != null) _panelObject.SetActive(true);
            _title.gameObject.SetActive(true);
            _body.gameObject.SetActive(true);
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
        public string CampaignId = "";
        public string MissionId = "";
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
