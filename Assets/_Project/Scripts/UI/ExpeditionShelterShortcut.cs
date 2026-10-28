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
            float left, float right, UnityAction action, float bottom = .712f, float top = .768f)
        {
            var root = ExpeditionUi.Rect(parent, name, left, bottom, right, top);
            var hit = ExpeditionShelterStyle.ThinFrame(ExpeditionUi.StylePanel(root.gameObject.AddComponent<Image>(), Color.white));
            hit.raycastTarget = true;
            var icon = ExpeditionUi.Fill(root, "ShortcutIcon", .035f, .10f, .31f, .90f, Color.white);
            icon.sprite = Resources.Load<Sprite>("Expedition/Icons/" + resource);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .95f, .82f);
            colors.pressedColor = new Color(.72f, .66f, .54f);
            colors.disabledColor = new Color(.45f, .45f, .45f, .7f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            var label = ExpeditionUi.Text(root, "Caption", caption, 34, .335f, .10f, .96f, .90f, TextAlignmentOptions.MidlineLeft);
            label.color = ExpeditionTheme.Ivory;
            label.enableAutoSizing = true;
            label.fontSizeMin = 26;
            label.fontSizeMax = 34;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.margin = Vector4.zero;
            label.raycastTarget = false;
            return button;
        }
    }
}
