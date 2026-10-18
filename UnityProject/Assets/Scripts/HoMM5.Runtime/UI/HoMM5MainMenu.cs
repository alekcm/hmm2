using System;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// Runtime front door for the imported game data.  The campaign and mission
    /// rows are read from Campaign XDB records exported by the Python importer;
    /// no scenario is selected by an alphabetical-map heuristic.
    /// </summary>
    public sealed class HoMM5MainMenu : MonoBehaviour
    {
        GameDatabase _database;
        Action<string> _startMap;
        Action _returnToMenu;
        Canvas _canvas;
        GameObject _menuPanel;
        GameObject _returnButton;
        RectTransform _content;
        bool _built;

        public void Initialize(GameDatabase database, Action<string> startMap, Action returnToMenu)
        {
            _database = database;
            _startMap = startMap;
            _returnToMenu = returnToMenu;
            EnsureBuilt();
            RebuildRows();
        }

        public void Show()
        {
            EnsureBuilt();
            RebuildRows();
            _menuPanel.SetActive(true);
            _returnButton.SetActive(false);
        }

        public void Hide()
        {
            EnsureBuilt();
            _menuPanel.SetActive(false);
            _returnButton.SetActive(true);
        }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            var canvasGo = new GameObject("MainMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _menuPanel = new GameObject("MenuPanel", typeof(RectTransform), typeof(Image));
            var panelRt = (RectTransform)_menuPanel.transform;
            panelRt.SetParent(canvasGo.transform, false);
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            _menuPanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.98f);

            var frame = new GameObject("CampaignFrame", typeof(RectTransform), typeof(Image));
            var frameRt = (RectTransform)frame.transform;
            frameRt.SetParent(_menuPanel.transform, false);
            frameRt.anchorMin = new Vector2(0.5f, 0.5f);
            frameRt.anchorMax = new Vector2(0.5f, 0.5f);
            frameRt.pivot = new Vector2(0.5f, 0.5f);
            frameRt.sizeDelta = new Vector2(980f, 760f);
            frame.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.12f, 0.97f);

            MakeLabel(frame.transform, "Title", "HEROES OF MIGHT AND MAGIC V", 30, FontStyle.Bold,
                      TextAnchor.MiddleCenter, new Vector2(20f, -28f), new Vector2(-20f, -82f));
            MakeLabel(frame.transform, "Subtitle", "TRIBES OF THE EAST", 18, FontStyle.Normal,
                      TextAnchor.MiddleCenter, new Vector2(20f, -78f), new Vector2(-20f, -120f));
            MakeLabel(frame.transform, "Prompt", "Select a campaign mission", 16, FontStyle.Normal,
                      TextAnchor.UpperLeft, new Vector2(32f, -142f), new Vector2(-32f, -178f));

            var scrollGo = new GameObject("CampaignScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(RectMask2D));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(frame.transform, false);
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(28f, 28f);
            scrollRt.offsetMax = new Vector2(-28f, -190f);
            scrollGo.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.045f, 0.55f);

            var contentGo = new GameObject("CampaignContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(scrollRt, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(0f, 0f);
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = _content;
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _returnButton = MakeButton(canvasGo.transform, "ReturnToMenu", "MAIN MENU", 15,
                                       Vector2.zero, Vector2.zero, _returnToMenu);
            var returnRt = (RectTransform)_returnButton.transform;
            returnRt.anchorMin = new Vector2(1f, 1f);
            returnRt.anchorMax = new Vector2(1f, 1f);
            returnRt.pivot = new Vector2(1f, 1f);
            returnRt.sizeDelta = new Vector2(172f, 40f);
            returnRt.anchoredPosition = new Vector2(-18f, -18f);
            _returnButton.SetActive(false);
        }

        void RebuildRows()
        {
            if (!_built || _content == null) return;
            foreach (Transform child in _content) Destroy(child.gameObject);

            var added = 0;
            if (_database != null && _database.Campaigns != null)
            {
                var ids = new List<string>(_database.Campaigns.Keys);
                ids.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                {
                    var campaign = _database.Campaigns[id];
                    if (campaign == null || campaign.missions == null) continue;
                    var campaignLabel = string.IsNullOrEmpty(campaign.fullName) ? campaign.name : campaign.fullName;
                    MakeHeader(campaignLabel, campaign.description);
                    foreach (var mission in campaign.missions)
                    {
                        if (mission == null || string.IsNullOrEmpty(mission.mapId)) continue;
                        var label = string.IsNullOrEmpty(mission.name)
                            ? mission.mapId
                            : mission.name + "  (" + mission.mapId + ")";
                        var mapId = mission.mapId;
                        MakeMissionButton(label, mission.description, mapId);
                        added++;
                    }
                }
            }

            // A partial import can legitimately contain maps without Campaign
            // records.  Keep the executable usable without silently selecting
            // one; expose every imported map as an explicit choice instead.
            if (added == 0 && _database != null && _database.MapIndex != null)
            {
                MakeHeader("Imported maps", "Campaign records are not present in this import.");
                foreach (var map in _database.MapIndex)
                {
                    if (map == null || string.IsNullOrEmpty(map.id)) continue;
                    MakeMissionButton(map.id + "  (" + map.tileX + "x" + map.tileY + ")", "", map.id);
                }
            }
        }

        void MakeHeader(string title, string description)
        {
            var go = new GameObject("CampaignHeader", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 50f;
            le.preferredHeight = 50f;
            var label = MakeLabel(go.transform, "Label", title, 18, FontStyle.Bold,
                                  TextAnchor.UpperLeft, new Vector2(8f, -4f), new Vector2(-8f, -26f));
            if (!string.IsNullOrEmpty(description))
            {
                var desc = MakeLabel(go.transform, "Description", description, 12, FontStyle.Normal,
                                     TextAnchor.LowerLeft, new Vector2(8f, 2f), new Vector2(-8f, 22f));
                desc.color = new Color(0.68f, 0.70f, 0.76f);
            }
        }

        void MakeMissionButton(string title, string description, string mapId)
        {
            var go = MakeButton(_content, "Mission_" + mapId, title, 15,
                                new Vector2(0f, 0f), new Vector2(0f, 0f),
                                () =>
                                {
                                    Hide();
                                    _startMap?.Invoke(mapId);
                                });
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 42f;
            le.preferredHeight = 42f;
            if (!string.IsNullOrEmpty(description))
                go.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.20f, 0.95f);
        }

        static GameObject MakeButton(Transform parent, string name, string label, int size,
                                     Vector2 offsetMin, Vector2 offsetMax, Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (parent is RectTransform)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = offsetMin;
                rt.offsetMax = offsetMax;
            }
            var image = go.GetComponent<Image>();
            image.color = new Color(0.13f, 0.16f, 0.23f, 0.96f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(() => action());
            var text = MakeLabel(go.transform, "Label", label, size, FontStyle.Normal,
                                 TextAnchor.MiddleCenter, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            text.raycastTarget = false;
            return go;
        }

        static Text MakeLabel(Transform parent, string name, string value, int size,
                              FontStyle style, TextAnchor alignment,
                              Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.90f, 0.84f);
            text.text = value ?? "";
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}
