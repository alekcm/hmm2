using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// Input needs to know whether the pointer is over the UI before a click can
    /// be treated as a map click.  Cached once per frame instead of per query.
    /// </summary>
    public static class HudInput
    {
        public static bool IsPointerOverUi
        {
            get
            {
                if (EventSystem.current == null) return false;
                return EventSystem.current.IsPointerOverGameObject();
            }
        }
    }

    /// <summary>
    /// The adventure-map HUD: what is selected, and a list of maps to load.
    ///
    /// Built in code rather than in a prefab so the project ships without any
    /// hand-authored scene assets that an importer could invalidate.
    /// </summary>
    public class HoMM5Hud : MonoBehaviour
    {
        public AdventureMapController Controller;
        public GameDatabase Database;

        Text _title;
        Text _details;
        Text _stats;
        GameObject _panel;
        GameObject _mapListPanel;
        ScrollRect _mapScroll;
        RectTransform _mapListContent;

        void Start()
        {
            BuildCanvas();
            if (Controller != null) Controller.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy()
        {
            if (Controller != null) Controller.SelectionChanged -= OnSelectionChanged;
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

            BuildSelectionPanel(canvasGo.transform);
            BuildStatsLabel(canvasGo.transform);
            BuildMapList(canvasGo.transform);
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
