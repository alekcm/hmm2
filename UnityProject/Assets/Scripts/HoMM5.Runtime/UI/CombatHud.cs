using HoMM5.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// The battle HUD: initiative queue, unit info, the Wait/Defend/Quick
    /// Combat/Flee buttons, the battle log and the result panel. Built in code
    /// like the adventure HUD.
    /// </summary>
    public class CombatHud : MonoBehaviour
    {
        public CombatController Controller;

        GameObject _root;
        RectTransform _queueRow;
        Text _roundLabel;
        Text _info;
        Text _log;
        GameObject _resultPanel;
        Text _resultText;
        Button _continueButton;

        void Awake()
        {
            BuildCanvas();
        }

        void Start()
        {
            if (Controller == null)
                Controller = FindAnyObjectByType<CombatController>();
        }

        void Update()
        {
            // The log is cheap to refresh from the state's tail.
            if (Controller == null || Controller.State == null || _log == null) return;
            var log = Controller.State.Log;
            var count = Mathf.Min(7, log.Count);
            var text = count == 0 ? "" : string.Join("\n", log.GetRange(log.Count - count, count));
            if (_log.text != text) _log.text = text;
        }

        public void Show() { if (_root != null) _root.SetActive(true); }
        public void Hide() { if (_root != null) _root.SetActive(false); }

        // -- state ---------------------------------------------------------

        public void UpdateState(CombatGameState state, CombatUnit selected)
        {
            if (state == null || _root == null) return;

            _roundLabel.text = "Round " + state.Round;

            // Initiative queue: one chip per living unit in current order.
            foreach (Transform child in _queueRow) Destroy(child.gameObject);
            var units = state.Units;
            foreach (var u in units)
            {
                if (u == null || !u.IsAlive) continue;
                var chip = new GameObject("chip", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)chip.transform;
                rt.SetParent(_queueRow, false);
                rt.sizeDelta = new Vector2(54, 44);
                var img = chip.GetComponent<Image>();
                img.color = u.Side == CombatSide.Attacker
                    ? new Color(0.55f, 0.22f, 0.16f, 0.95f) : new Color(0.16f, 0.26f, 0.5f, 0.95f);
                if (u == state.Active) img.color = Color.Lerp(img.color, Color.yellow, 0.45f);

                var label = MakeText(chip.transform, "t", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
                                     new Vector2(2, 2), new Vector2(-2, -2));
                var name = u.DisplayName;
                if (name.Length > 9) name = name.Substring(0, 9);
                label.text = name + "\n" + u.Count;
                label.fontSize = 11;
            }

            var unit = selected != null ? selected : state.Active;
            _info.text = unit != null ? Describe(unit) : "";
        }

        static string Describe(CombatUnit u)
        {
            var lines = new System.Collections.Generic.List<string>
            {
                $"{u.DisplayName}  x{u.Count}   ({(u.Side == CombatSide.Attacker ? "attacker" : "defender")})",
                $"HP {u.HealthLeft}/{u.MaxHealth} per creature, {u.TotalHealth} total",
                $"Attack {u.AttackSkill}   Defence {u.DefenceSkill}{(u.Defended ? " (defending)" : "")}",
                $"Damage {u.MinDamage}-{u.MaxDamage}   Speed {u.Speed}   Initiative {u.Initiative}",
            };
            if (u.IsRanged) lines.Add($"Shots {u.ShotsLeft}/{(u.Def != null ? u.Def.Shots : 0)}");
            if (u.Def != null && !string.IsNullOrEmpty(u.Def.Description))
                lines.Add("", u.Def.Description);
            return string.Join("\n", lines);
        }

        // -- buttons -------------------------------------------------------

        void OnWait() => Controller?.OnWaitPressed();
        void OnDefend() => Controller?.OnDefendPressed();
        void OnQuick() => Controller?.OnQuickCombatPressed();
        void OnFlee() => Controller?.OnFleePressed();

        public void ShowResult(CombatResult result, System.Action continueAction)
        {
            if (_resultPanel == null) return;
            _resultPanel.SetActive(true);
            var outcome = result != null ? result.Outcome : CombatOutcome.Fled;
            _resultText.text =
                (outcome == CombatOutcome.AttackerWon ? "Victory!"
                 : outcome == CombatOutcome.DefenderWon ? "Defeat."
                 : "Retreated.") +
                (result != null && result.Experience > 0 ? $"\n+{result.Experience} experience" : "") +
                (result != null && result.CreaturesKilledByAttacker > 0
                    ? $"\n{result.CreaturesKilledByAttacker} enemy creatures slain" : "");
            _continueButton.onClick.RemoveAllListeners();
            _continueButton.onClick.AddListener(() => continueAction?.Invoke());
        }

        // -- construction --------------------------------------------------

        void BuildCanvas()
        {
            var canvasGo = new GameObject("CombatHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _root = canvasGo;

            // Top bar: round + queue.
            var top = MakePanel(_root.transform, "Top", new Vector2(0, 1), new Vector2(0, -64), new Vector2(0, 0));
            var topRt = (RectTransform)top.transform;
            topRt.anchorMin = new Vector2(0, 1);
            topRt.anchorMax = new Vector2(1, 1);
            topRt.sizeDelta = new Vector2(0, 64);

            _roundLabel = MakeText(top.transform, "Round", 16, FontStyle.Bold, TextAnchor.MiddleLeft,
                                   new Vector2(12, 0), new Vector2(110, 0));
            _roundLabel.text = "Round 1";

            var queueGo = new GameObject("Queue", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _queueRow = (RectTransform)queueGo.transform;
            _queueRow.SetParent(top.transform, false);
            _queueRow.anchorMin = new Vector2(0, 0);
            _queueRow.anchorMax = new Vector2(1, 1);
            _queueRow.offsetMin = new Vector2(120, 6);
            _queueRow.offsetMax = new Vector2(-16, -6);
            var layout = queueGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // Left: unit info + log.
            var left = MakePanel(_root.transform, "Left", new Vector2(0, 0), new Vector2(16, 120), new Vector2(376, 320));
            _info = MakeText(left.transform, "Info", 14, FontStyle.Normal, TextAnchor.UpperLeft,
                             new Vector2(12, -12), new Vector2(-12, -150));
            _log = MakeText(left.transform, "Log", 12, FontStyle.Normal, TextAnchor.LowerLeft,
                            new Vector2(12, 10), new Vector2(-12, -156));
            _log.color = new Color(0.80f, 0.78f, 0.72f);

            // Bottom: action buttons.
            MakeButton(_root.transform, "Wait", new Vector2(300, -20), OnWait);
            MakeButton(_root.transform, "Defend", new Vector2(430, -20), OnDefend);
            MakeButton(_root.transform, "Quick Combat", new Vector2(585, -20), OnQuick);
            MakeButton(_root.transform, "Flee", new Vector2(760, -20), OnFlee);

            // Result modal (hidden until the battle ends).
            _resultPanel = MakePanel(_root.transform, "Result", new Vector2(0, 0), Vector2.zero, Vector2.zero);
            var rr = (RectTransform)_resultPanel.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(420, 260);
            _resultText = MakeText(_resultPanel.transform, "Text", 18, FontStyle.Bold, TextAnchor.MiddleCenter,
                                   new Vector2(20, 70), new Vector2(-20, -20));
            _continueButton = MakeButtonObject(_resultPanel.transform, "Continue",
                                               new Vector2(0, -80), new Vector2(180, 46), "Continue",
                                               () => { });
            var crt = (RectTransform)_continueButton.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0, -80);
            _resultPanel.SetActive(false);
        }

        void MakeButton(Transform parent, string label, Vector2 pos, System.Action action)
        {
            MakeButtonObject(parent, label, pos, new Vector2(130, 42), label, action);
        }

        static Button MakeButtonObject(Transform parent, string name, Vector2 pos, Vector2 size,
                                       string label, System.Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.30f, 0.24f, 0.13f, 0.95f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => action?.Invoke());
            var text = MakeText(go.transform, "Label", 14, FontStyle.Bold, TextAnchor.MiddleCenter,
                                new Vector2(4, 2), new Vector2(-4, -2));
            text.text = label;
            text.raycastTarget = false;
            return button;
        }

        // -- ugui helpers (mirrors HoMM5Hud) --------------------------------

        static GameObject MakePanel(Transform parent, string name, Vector2 anchor,
                                    Vector2 offsetMin, Vector2 offsetMax)
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
