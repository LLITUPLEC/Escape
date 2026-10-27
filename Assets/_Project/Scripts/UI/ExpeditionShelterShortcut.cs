using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Painted Shelter shortcuts with a full touch area and room for live count badges.</summary>
    public static class ExpeditionShelterShortcut
    {
        public static Button Create(Transform parent, string name, string caption, string resource,
            float left, float right, UnityAction action)
        {
            var root = ExpeditionUi.Rect(parent, name, left, .845f, right, .920f);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var icon = ExpeditionUi.Fill(root, "ShortcutIcon", .12f, .25f, .88f, 1, Color.white);
            icon.sprite = Resources.Load<Sprite>("Expedition/Icons/" + resource);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = icon;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .95f, .82f);
            colors.pressedColor = new Color(.72f, .66f, .54f);
            colors.disabledColor = new Color(.45f, .45f, .45f, .7f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            var label = ExpeditionUi.Text(root, "Caption", caption, 24, 0, 0, 1, .25f, TextAlignmentOptions.Center);
            label.color = ExpeditionTheme.Ivory;
            label.enableAutoSizing = true;
            label.fontSizeMin = 19;
            label.fontSizeMax = 24;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.margin = Vector4.zero;
            label.raycastTarget = false;
            return button;
        }
    }
}
