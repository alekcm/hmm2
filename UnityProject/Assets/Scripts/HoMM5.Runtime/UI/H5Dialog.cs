using System;
using HoMM5.Map;
using UnityEngine;
using UnityEngine.UI;

namespace HoMM5.UI
{
    /// <summary>
    /// A modal choice panel: a message and one button per option. Built in
    /// code like the rest of the HUD; destroys itself after a choice.
    /// </summary>
    public class H5Dialog : MonoBehaviour
    {
        /// <summary>Show the dialog on <paramref name="canvas"/>.</summary>
        public static H5Dialog Show(Transform canvas, H5DialogRequest request, Action closed = null)
        {
            if (canvas == null || request == null) return null;
            var go = new GameObject("Dialog", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            // Dim the map behind the dialog.
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var dialog = go.AddComponent<H5Dialog>();
            dialog.BuildPanel(request, closed);
            return dialog;
        }

        void BuildPanel(H5DialogRequest request, Action closed)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(transform, false);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(520, 220);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.96f);

            var textGo = new GameObject("Message", typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(prt, false);
            trt.anchorMin = new Vector2(0, 1);
            trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(20, -120);
            trt.offsetMax = new Vector2(-20, -16);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.alignment = TextAnchor.UpperCenter;
            text.color = new Color(0.93f, 0.90f, 0.82f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = request.Message ?? "";

            var options = request.Options;
            var width = Mathf.Min(220f, (520f - 40f - (options.Count - 1) * 12f) / Mathf.Max(1, options.Count));
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var buttonGo = new GameObject("Option" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var brt = (RectTransform)buttonGo.transform;
                brt.SetParent(prt, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(width, 44);
                brt.anchoredPosition = new Vector2((i - (options.Count - 1) * 0.5f) * (width + 12f), 22);
                var img = buttonGo.GetComponent<Image>();
                img.color = new Color(0.34f, 0.25f, 0.12f, 1f);
                var button = buttonGo.GetComponent<Button>();
                button.targetGraphic = img;
                var action = option.Action;
                button.onClick.AddListener(() =>
                {
                    action?.Invoke();
                    Close(closed);
                });

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var lrt = (RectTransform)labelGo.transform;
                lrt.SetParent(brt, false);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(6, 4);
                lrt.offsetMax = new Vector2(-6, -4);
                var label = labelGo.GetComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 15;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(0.93f, 0.90f, 0.82f);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.text = option.Label ?? "...";
                label.raycastTarget = false;
            }
        }

        void Close(Action closed)
        {
            closed?.Invoke();
            Destroy(gameObject);
        }
    }
}
