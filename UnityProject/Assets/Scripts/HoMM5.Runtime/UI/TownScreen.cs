using System.Collections.Generic;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The town screen: what stands in the town, what can be built next, and the
    /// creatures the dwellings have bred.
    ///
    /// Built in code like the rest of the HUD, so the project ships without
    /// hand-authored UI assets. The shipped screen's 3D interior (each
    /// <c>AdvMapTownShared</c> record carries an <c>Interior</c> scene with a
    /// model per building) is not rendered yet -- this is the functional screen
    /// over the same records, with the same names, costs and prerequisites.
    /// </summary>
    public class TownScreen : MonoBehaviour
    {
        [System.NonSerialized] public GameDatabase Database;
        [System.NonSerialized] public AdventureMapGameState GameState;

        H5TownState _town;
        GameObject _root;
        UnityEngine.UI.Text _header;
        UnityEngine.UI.Text _treasury;
        RectTransform _buildList;
        RectTransform _dwellingList;

        public bool IsOpen => _root != null && _root.activeSelf;
        public H5TownState Town => _town;

        /// <summary>Create the panel under an existing canvas.</summary>
        public static TownScreen Create(Transform canvas)
        {
            var go = new GameObject("TownScreen");
            go.transform.SetParent(canvas, false);
            var screen = go.AddComponent<TownScreen>();
            screen.Build();
            screen._root.SetActive(false);
            return screen;
        }

        public void Show(H5TownState town)
        {
            _town = town;
            if (_root == null) return;
            _root.SetActive(town != null);
            if (town != null) Refresh();
        }

        public void Hide()
        {
            _town = null;
            if (_root != null) _root.SetActive(false);
        }

        // -- construction ---------------------------------------------------

        void Build()
        {
            _root = MakePanel(transform, "TownPanel", new Vector2(-560, -340), new Vector2(560, 340));
            _root.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.97f);

            _header = MakeText(_root.transform, "Header", 24, FontStyle.Bold, TextAnchor.UpperLeft,
                               new Vector2(24, -18), new Vector2(-24, -56));
            _treasury = MakeText(_root.transform, "Treasury", 14, FontStyle.Normal, TextAnchor.UpperRight,
                                 new Vector2(24, -60), new Vector2(-24, -84));

            MakeLabel(_root.transform, "BuildHeader", "Construction", new Vector2(24, -92), new Vector2(-580, -118));
            _buildList = MakeList(_root.transform, "BuildScroll", new Vector2(24, 96), new Vector2(-580, -122));

            MakeLabel(_root.transform, "DwellHeader", "Dwellings", new Vector2(584, -92), new Vector2(-24, -118));
            _dwellingList = MakeList(_root.transform, "DwellScroll", new Vector2(584, 96), new Vector2(-24, -122));

            var status = MakeText(_root.transform, "Status", 13, FontStyle.Italic, TextAnchor.LowerLeft,
                                  new Vector2(24, 56), new Vector2(-420, 90));
            status.text = "One building per day. Dwellings breed every week; recruits join a hero in " +
                          "town, or the garrison when none is.";
            status.color = new Color(0.66f, 0.64f, 0.58f);

            MakeButton(_root.transform, "CloseButton", "Close (Esc)", new Vector2(1, 0),
                       new Vector2(-176, 20), new Vector2(-24, 64), Hide);
        }

        // -- content --------------------------------------------------------

        /// <summary>Re-read the town and redraw both columns.</summary>
        public void Refresh()
        {
            if (_town == null || Database == null || GameState == null) return;
            _header.text = _town.Summary(Database);
            _treasury.text = GameState.Resources != null ? GameState.Resources.ToString() : "";
            Rebuild(_buildList, BuildRows());
            Rebuild(_dwellingList, DwellingRows());
        }

        struct Row
        {
            public string Label;
            public string Note;
            public string ActionLabel;
            public System.Action Action;
            public Color Tint;
            public float Height;
        }

        static readonly Color StandingTint = new Color(0.12f, 0.15f, 0.12f, 0.92f);
        static readonly Color OpenTint = new Color(0.17f, 0.16f, 0.20f, 0.92f);
        static readonly Color LockedTint = new Color(0.09f, 0.09f, 0.11f, 0.72f);

        /// <summary>One row per building slot: what stands, what comes next.</summary>
        List<Row> BuildRows()
        {
            var rows = new List<Row>();
            var resources = GameState.Resources;
            foreach (var slot in _town.Slots)
            {
                if (slot == null) continue;
                var standing = _town.BuiltIn(Database, slot.type);
                var next = _town.NextTier(Database, slot.type);
                if (standing == null && next == null) continue;

                var row = new Row
                {
                    Label = standing != null ? standing.DisplayName : SlotName(slot.type),
                    Note = standing != null ? "built" : "empty",
                    Tint = standing != null ? StandingTint : LockedTint,
                    Height = 32,
                };

                if (next != null)
                {
                    var blocker = _town.BuildBlocker(Database, next, resources);
                    row.Label += "   ->   " + next.DisplayName;
                    row.Note = next.CostText() + (blocker != null ? "   (" + blocker + ")" : "");
                    row.Height = 38;
                    if (blocker == null)
                    {
                        row.Tint = OpenTint;
                        row.ActionLabel = "Build";
                        var building = next;
                        row.Action = () =>
                        {
                            GameState.BuildInTown(_town, building);
                            Refresh();
                        };
                    }
                    else
                    {
                        row.Tint = LockedTint;
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>One row per creature a built dwelling can recruit.</summary>
        List<Row> DwellingRows()
        {
            var rows = new List<Row>();
            foreach (var dwelling in _town.Dwellings(Database))
            {
                var pool = _town.PoolOf(dwelling.type);
                foreach (var creatureId in _town.Recruitables(Database, dwelling.type))
                {
                    var creature = Database.GetCreature(creatureId);
                    var name = creature != null ? creature.DisplayName : creatureId;
                    var price = creature != null && creature.cost != null ? creature.cost.ToString() : "free";
                    var slotType = dwelling.type;
                    var id = creatureId;
                    var available = pool;
                    rows.Add(new Row
                    {
                        Label = $"{name}    waiting {available}",
                        Note = price + " each",
                        ActionLabel = available > 0 ? "Recruit all" : null,
                        Tint = available > 0 ? OpenTint : LockedTint,
                        Height = 34,
                        Action = available > 0
                            ? (System.Action)(() =>
                              {
                                  // 0 recruits the whole pool.
                                  GameState.RecruitFromTown(_town, slotType, id, 0);
                                  Refresh();
                              })
                            : null,
                    });
                }
            }
            if (rows.Count == 0)
                rows.Add(new Row
                {
                    Label = "No dwellings built yet.",
                    Tint = LockedTint,
                    Height = 32,
                });
            return rows;
        }

        void Rebuild(RectTransform parent, List<Row> rows)
        {
            if (parent == null) return;
            foreach (Transform child in parent) Destroy(child.gameObject);
            foreach (var row in rows)
            {
                var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = row.Tint;
                var le = go.GetComponent<LayoutElement>();
                le.minHeight = row.Height;
                le.preferredHeight = row.Height;

                var label = MakeText(go.transform, "Label", 14, FontStyle.Normal, TextAnchor.MiddleLeft,
                                     new Vector2(10, 4), new Vector2(-230, -4));
                label.text = row.Label;

                var note = MakeText(go.transform, "Note", 12, FontStyle.Normal, TextAnchor.MiddleRight,
                                    new Vector2(180, 4), new Vector2(-150, -4));
                note.text = row.Note;
                note.color = new Color(0.70f, 0.68f, 0.62f);

                if (row.Action != null)
                    MakeButton(go.transform, "Action", row.ActionLabel, new Vector2(1, 0.5f),
                               new Vector2(-144, 5), new Vector2(-8, -5), row.Action);
            }
        }

        static string SlotName(string slotType)
        {
            switch (slotType)
            {
                case "TB_TOWN_HALL": return "Town Hall";
                case "TB_FORT": return "Fort";
                case "TB_MARKETPLACE": return "Marketplace";
                case "TB_TAVERN": return "Tavern";
                case "TB_BLACKSMITH": return "Blacksmith";
                case "TB_MAGIC_GUILD": return "Magic Guild";
                case "TB_SHIPYARD": return "Shipyard";
                case "TB_GRAIL": return "Grail";
                default:
                    return slotType != null && slotType.StartsWith("TB_DWELLING_")
                        ? "Dwelling " + slotType.Substring("TB_DWELLING_".Length)
                        : GameDatabase.PrettifyEnum(slotType);
            }
        }

        // -- widget helpers -------------------------------------------------

        static GameObject MakePanel(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        static void MakeLabel(Transform parent, string name, string text,
                              Vector2 offsetMin, Vector2 offsetMax)
        {
            var label = MakeText(parent, name, 17, FontStyle.Bold, TextAnchor.UpperLeft, offsetMin, offsetMax);
            label.text = text;
        }

        static UnityEngine.UI.Text MakeText(Transform parent, string name, int size, FontStyle style,
                                            TextAnchor align, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.GetComponent<UnityEngine.UI.Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = new Color(0.92f, 0.90f, 0.84f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform MakeList(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                                           typeof(ContentSizeFitter));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(rt, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;

            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 3;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = go.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        static void MakeButton(Transform parent, string name, string label, Vector2 anchor,
                               Vector2 offsetMin, Vector2 offsetMax, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.30f, 0.24f, 0.13f, 1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var text = MakeText(go.transform, "Label", 13, FontStyle.Bold, TextAnchor.MiddleCenter,
                                new Vector2(4, 2), new Vector2(-4, -2));
            text.text = label;
        }
    }
}
