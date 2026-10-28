using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Live controls for the mine-light Shelter composition; artwork never contains labels or hit targets.</summary>
    public static class ExpeditionShelterStyle
    {
        public static Image ThinFrame(Image image)
        {
            if (image == null) return null;
            image.pixelsPerUnitMultiplier = 5.5f;
            return image;
        }

        public static Button ResourcePlus(Transform parent, UnityAction action)
        {
            // The generous transparent hit area stays usable on phones; only the small gold octagon is painted.
            var hit = ExpeditionUi.Fill(parent, "Plus", 1, .5f, 1, .5f, Color.clear);
            hit.raycastTarget = true;
            var rect = hit.rectTransform; rect.pivot = new Vector2(1, .5f);
            rect.sizeDelta = new Vector2(56, 64); rect.anchoredPosition = new Vector2(-5, 0);
            var face = ExpeditionUi.Rect(rect, "GoldOctagon", .18f, .22f, .86f, .78f).gameObject.AddComponent<ExpeditionResourcePlusGraphic>();
            face.raycastTarget = false;
            var button = hit.gameObject.AddComponent<Button>(); button.targetGraphic = face;
            var colors = button.colors; colors.normalColor = colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1); colors.pressedColor = new Color(.78f, .72f, .59f);
            colors.disabledColor = new Color(.6f, .6f, .6f, .8f); colors.fadeDuration = .1f; button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            Project.Audio.ExpeditionAudio.BindButton(button);
            return button;
        }

        public static Button Pill(Transform parent, string name, string title, Sprite icon,
            float x0, float y0, float x1, float y1, UnityAction action, float fontSize = 31)
        {
            var button = ExpeditionUi.Button(parent, name, title, x0, y0, x1, y1, action, false, fontSize);
            ThinFrame(button.GetComponent<Image>());
            var label = button.GetComponentInChildren<TMP_Text>();
            ExpeditionUi.SetRect(label.rectTransform, .31f, .10f, .96f, .90f);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.margin = Vector4.zero;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true; label.fontSizeMin = fontSize * .76f; label.fontSizeMax = fontSize;
            var art = ExpeditionUi.Fill(button.transform, "Icon", .035f, .10f, .29f, .90f, Color.white);
            art.sprite = icon; art.preserveAspect = true; art.raycastTarget = false;
            return button;
        }
    }
}
