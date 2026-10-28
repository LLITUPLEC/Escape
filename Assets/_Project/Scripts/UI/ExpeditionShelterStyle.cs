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
