using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The adventure-map HUD: what is selected, and a list of maps to load.
    ///
    /// Built in code rather than in a prefab so the project ships without any
    /// hand-authored scene assets that an importer could invalidate.
    /// </summary>
    public class HoMM5Hud : MonoBehaviour
    {
        // Assigned at runtime by H5Bootstrap.  GameDatabase is a plain class, so
        // leaving these as serializable fields makes Unity's serialization
        // analyzer flag the one it cannot store; nothing here should round-trip
        // through a scene anyway.
        [System.NonSerialized] public AdventureMapController Controller;
        [System.NonSerialized] public GameDatabase Database;
        [System.NonSerialized] public AdventureMapGameState GameState;

        Text _title;
        Text _details;
        Text _stats;
        Text _log;
        Text _turnDate;
        Text _resourceSummary;
        GameObject _panel;
        public System.Action EndDayRequested;
        public System.Action SaveRequested;
        public System.Action LoadRequested;
        public System.Action TownScreenRequested;

        /// <summary>The town screen, created with the rest of the HUD.</summary>
        public TownScreen Town { get; private set; }
        GameObject _mapListPanel;
        ScrollRect _mapScroll;
        RectTransform _mapListContent;
        H5Minimap _minimap;
        Transform _canvasRoot;
        float _nextDetailsRefresh;

        // Awake, not Start: H5Bootstrap adds this component and then calls
        // PopulateMapList in the same frame. Awake runs synchronously inside
        // AddComponent; Start would not run until the next frame, so the canvas
        // would still be missing and the map list would silently stay empty.
        void Awake()
        {
            BuildCanvas();
        }

        void Start()
        {
            // Awake built the canvas before H5Bootstrap could hand over the data
            // tables, so the town screen gets them here.
            if (Town != null)
            {
                Town.Database = Database;
                Town.GameState = GameState;
            }
            if (Controller != null) Controller.SelectionChanged += OnSelectionChanged;
            if (GameState != null)
            {
                GameState.MessageLogged += OnLogMessage;
                GameState.DialogRequested += ShowDialog;
            }
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf || Controller == null ||
                Controller.Selected == null || Time.unscaledTime < _nextDetailsRefresh) return;
            _nextDetailsRefresh = Time.unscaledTime + 0.25f;
            _details.text = Controller.Selected.Details();
        }

        void OnDestroy()
        {
            if (Controller != null) Controller.SelectionChanged -= OnSelectionChanged;
            if (GameState != null)
            {
                GameState.MessageLogged -= OnLogMessage;
                GameState.DialogRequested -= ShowDialog;
            }
        }

        // -- game events ---------------------------------------------------

        void OnLogMessage(string message)
        {
            if (_log == null) return;
            var lines = GameState != null ? GameState.Log : null;
            if (lines == null || lines.Count == 0)
            {
                _log.text = message;
                return;
            }
            // Show the tail; the log is capped by the game state itself.
            var count = Mathf.Min(6, lines.Count);
            _log.text = string.Join("\n", lines.GetRange(lines.Count - count, count));
        }

        /// <summary>Modal choice (chest, arena, tree of knowledge).</summary>
        public void ShowDialog(H5DialogRequest request)
        {
            if (_canvasRoot != null) H5Dialog.Show(_canvasRoot, request);
        }

        /// <summary>Called after a map load so the minimap tracks the new scene.</summary>
        public void AttachMap(MapSceneBuilder builder)
        {
            if (_minimap != null && builder != null)
                _minimap.Build(builder, Controller, GameState);
        }

        /// <summary>Hide the adventure HUD while a battle runs.</summary>
        public void Show()
        {
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(false);
        }

        void OnSelectionChanged(H5MapObject obj)
        {
            if (_details == null) return;
            if (obj == null)
            {
                _panel.SetActive(false);
                return;
            }
            if (Database != null) obj.AttachDatabase(Database);
            _panel.SetActive(true);
            _title.text = obj.Summary();
            _details.text = obj.Details();
        }

        // -- construction --------------------------------------------------

        void BuildCanvas()
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot = canvasGo.transform;
            BuildSelectionPanel(canvasGo.transform);
            BuildStatsLabel(canvasGo.transform);
            BuildLogLabel(canvasGo.transform);
            BuildMapList(canvasGo.transform);
            BuildTurnControls(canvasGo.transform);
            _minimap = H5Minimap.Create(canvasGo.transform, new Vector2(-16, 16), new Vector2(240, 240));
            Town = TownScreen.Create(canvasGo.transform);
        }

        void BuildSelectionPanel(Transform parent)
        {
            _panel = MakePanel(parent, "SelectionPanel", new Vector2(360, 320),
                               new Vector2(0, 1), new Vector2(1, -1), new Vector2(1, 0));
            _title = MakeText(_panel.transform, "Title", 20, FontStyle.Bold, TextAnchor.UpperLeft,
                              new Vector2(16, -16), new Vector2(-16, -52));
            _details = MakeText(_panel.transform, "Details", 14, FontStyle.Normal, TextAnchor.UpperLeft,
                                new Vector2(16, -56), new Vector2(-16, -16));
            _panel.SetActive(false);
        }

        void BuildStatsLabel(Transform parent)
        {
            _stats = MakeText(parent, "Stats", 14, FontStyle.Normal, TextAnchor.LowerLeft,
                              new Vector2(16, 16), new Vector2(700, 120));
        }

        void BuildLogLabel(Transform parent)
        {
            // Right of the stats block, left of the minimap.
            var panel = MakePanel(parent, "LogPanel", new Vector2(560, 110),
                                  new Vector2(0, 0), new Vector2(710, 16), new Vector2(1270, 126));
            _log = MakeText(panel.transform, "Log", 13, FontStyle.Normal, TextAnchor.LowerLeft,
                            new Vector2(10, 6), new Vector2(-10, -6));
            _log.text = "";
        }

        void BuildTurnControls(Transform parent)
        {
            var bar = new GameObject("TurnControls", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)bar.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(1020, 54);
            rt.anchoredPosition = new Vector2(0, -12);
            bar.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.88f);

            _turnDate = MakeText(bar.transform, "Date", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
                                 new Vector2(14, 6), new Vector2(-700, -6));
            _turnDate.text = "Day 1  |  Week 1  |  Month 1";
            _turnDate.raycastTarget = false;
            _resourceSummary = MakeText(bar.transform, "Resources", 13, FontStyle.Normal,
                                        TextAnchor.MiddleCenter, new Vector2(210, 6), new Vector2(-360, -6));
            _resourceSummary.text = "W 20  O 20  M 10  C 10  S 10  G 10  Gold 20000";
            _resourceSummary.raycastTarget = false;

            var buttonGo = new GameObject("EndDayButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGo.transform;
            buttonRt.SetParent(bar.transform, false);
            buttonRt.anchorMin = new Vector2(1, 0.5f);
            buttonRt.anchorMax = new Vector2(1, 0.5f);
            buttonRt.pivot = new Vector2(1, 0.5f);
            buttonRt.sizeDelta = new Vector2(112, 38);
            buttonRt.anchoredPosition = new Vector2(-8, 0);
            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.34f, 0.25f, 0.12f, 1f);
            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => EndDayRequested?.Invoke());
            var label = MakeText(buttonGo.transform, "Label", 14, FontStyle.Bold, TextAnchor.MiddleCenter,
                                 new Vector2(4, 2), new Vector2(-4, -2));
            label.text = "End Day";
            label.raycastTarget = false;

            MakeBarButton(bar.transform, "SaveButton", "Save (F5)", -128,
                          () => SaveRequested?.Invoke());
            MakeBarButton(bar.transform, "LoadButton", "Load (F9)", -244,
                          () => LoadRequested?.Invoke());
            MakeBarButton(bar.transform, "TownButton", "Town (T)", -360,
                          () => TownScreenRequested?.Invoke());
        }

        static void MakeBarButton(Transform parent, string name, string label, float xOffset,
                                  System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(108, 38);
            rt.anchoredPosition = new Vector2(xOffset, 0);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.24f, 1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
            var text = MakeText(go.transform, "Label", 13, FontStyle.Bold, TextAnchor.MiddleCenter,
                                new Vector2(4, 2), new Vector2(-4, -2));
            text.text = label;
            text.raycastTarget = false;
        }

        void BuildMapList(Transform parent)
        {
            _mapListPanel = MakePanel(parent, "MapListPanel", new Vector2(320, 420),
                                      new Vector2(1, 1), new Vector2(-336, -16), new Vector2(-16, -436));

            var header = MakeText(_mapListPanel.transform, "Header", 16, FontStyle.Bold, TextAnchor.UpperLeft,
                                  new Vector2(12, -12), new Vector2(-12, -36));
            header.text = "Maps";

            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            var rt = (RectTransform)scrollGo.transform;
            rt.SetParent(_mapListPanel.transform, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(8, 8);
            rt.offsetMax = new Vector2(-8, -44);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                                           typeof(ContentSizeFitter));
            _mapListContent = (RectTransform)contentGo.transform;
            _mapListContent.SetParent(rt, false);
            _mapListContent.anchorMin = new Vector2(0, 1);
            _mapListContent.anchorMax = new Vector2(1, 1);
            _mapListContent.pivot = new Vector2(0.5f, 1);
            _mapListContent.sizeDelta = new Vector2(0, 0);

            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _mapScroll = scrollGo.GetComponent<ScrollRect>();
            _mapScroll.content = _mapListContent;
            _mapScroll.vertical = true;
            _mapScroll.horizontal = false;
            _mapScroll.movementType = ScrollRect.MovementType.Clamped;
        }

        /// <summary>Fill the map list from the importer's index.</summary>
        public void PopulateMapList(System.Action<string> onLoad)
        {
            if (_mapListContent == null || Database == null) return;
            foreach (Transform child in _mapListContent) Destroy(child.gameObject);

            foreach (var entry in Database.MapIndex)
            {
                var id = entry.id;
                var label = $"{id}  ({entry.tileX}x{entry.tileY}, {entry.objects} obj)";
                var go = new GameObject("Map_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_mapListContent, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(0.16f, 0.16f, 0.18f, 0.9f);
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onLoad(id));

                var text = MakeText(go.transform, "Label", 13, FontStyle.Normal, TextAnchor.MiddleLeft,
                                    new Vector2(10, 4), new Vector2(-10, -4));
                text.text = label;
                text.raycastTarget = false;

                var le = go.AddComponent<LayoutElement>();
                le.minHeight = 28;
                le.preferredHeight = 28;
            }
        }

        public void SetTurnDate(int day, int week, int month)
        {
            if (_turnDate != null)
                _turnDate.text = $"Day {day}  |  Week {week}  |  Month {month}";
        }

        public void SetResourceSummary(string text)
        {
            if (_resourceSummary != null && !string.IsNullOrEmpty(text))
                _resourceSummary.text = text;
        }

        public void SetStats(string text)
        {
            if (_stats != null) _stats.text = text;
        }

        // -- helpers -------------------------------------------------------

        static GameObject MakePanel(Transform parent, string name, Vector2 size,
                                    Vector2 anchor, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            go.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.85f);
            return go;
        }

        static Text MakeText(Transform parent, string name, int size, FontStyle style,
                             TextAnchor align, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = new Color(0.92f, 0.90f, 0.84f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
