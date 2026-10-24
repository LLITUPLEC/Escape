using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Visual styling for the existing connection, account, session and server-aura dialogs.</summary>
    public static class ExpeditionSystemDialogs
    {
        private static Font UiFont
        {
            get
            {
                var font = ExpeditionTheme.Font;
                return font != null && font.sourceFontFile != null
                    ? font.sourceFontFile : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        public static void ApplyConnection(GameObject root, Text wait)
        {
            if (root == null || wait == null) return;
            StyleRoot(root);
            var plate = ExpeditionUi.Panel(root.transform, "ExpeditionConnectionPlate", .065f, .405f, .935f, .595f);
            plate.raycastTarget = true;
            SetSafeBounds(plate.rectTransform, .065f, .405f, .935f, .595f);
            ExpeditionUi.Text(plate.transform, "Eyebrow", "ЭКСПЕДИЦИЯ / СВЯЗЬ", 24, .055f, .73f, .945f, .91f).color = ExpeditionTheme.Amber;
            wait.transform.SetParent(plate.transform, false);
            ExpeditionUi.SetRect(wait.rectTransform, .055f, .25f, .945f, .71f);
            StyleText(wait, 42, ExpeditionTheme.Ivory, TextAnchor.MiddleLeft);
            ExpeditionUi.Panel(plate.transform, "SignalRule", .055f, .15f, .3f, .162f, ExpeditionTheme.Amber);
        }

        public static void ApplyAuth(GameObject root, RectTransform panel, InputField email, InputField password, Text status)
        {
            if (root == null || panel == null) return;
            StyleRoot(root);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            StylePlate(panel);
            SetSafeBounds(panel, .055f, .12f, .945f, .88f);
            ExpeditionUi.Text(panel, "ExpeditionAccountEyebrow", "ЭКСПЕДИЦИЯ / АККАУНТ", 23, .055f, .945f, .945f, .983f).color = ExpeditionTheme.Amber;
            ExpeditionUi.Panel(panel, "ExpeditionAccountRule", .055f, .985f, .24f, .989f, ExpeditionTheme.Amber);
            PlaceText(panel, "Title", 46, .055f, .84f, .945f, .935f, ExpeditionTheme.Ivory, TextAnchor.MiddleLeft);
            PlaceText(panel, "Hint", 30, .055f, .65f, .945f, .825f, ExpeditionTheme.Muted, TextAnchor.UpperLeft);
            StyleInput(email, .055f, .55f, .945f, .625f);
            StyleInput(password, .055f, .445f, .945f, .52f);
            if (status != null)
            {
                ExpeditionUi.SetRect(status.rectTransform, .055f, .295f, .945f, .43f);
                StyleText(status, 27, ExpeditionTheme.Coral, TextAnchor.UpperLeft);
            }
            var buttons = panel.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                ExpeditionUi.SetRect(buttons[i].transform as RectTransform, .055f, i == 0 ? .17f : .045f, .945f, i == 0 ? .265f : .14f);
                ExpeditionUi.StyleButton(buttons[i], i == 0, 34);
                StyleButtonLabels(buttons[i], i == 0, 34);
            }
        }

        public static void ApplySession(GameObject root, RectTransform panel, Text message, Button confirm)
        {
            if (root == null || panel == null) return;
            StyleRoot(root);
            StylePlate(panel);
            SetSafeBounds(panel, .065f, .27f, .935f, .73f);
            ExpeditionUi.Text(panel, "ExpeditionSessionEyebrow", "ЭКСПЕДИЦИЯ / АККАУНТ", 23, .06f, .905f, .94f, .975f).color = ExpeditionTheme.Muted;
            ExpeditionUi.Text(panel, "ExpeditionSessionTitle", "Сессия завершена", 45, .06f, .735f, .94f, .91f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Panel(panel, "ExpeditionSessionRule", .06f, .704f, .28f, .712f, ExpeditionTheme.Amber);
            if (message != null)
            {
                ExpeditionUi.SetRect(message.rectTransform, .06f, .285f, .94f, .66f);
                StyleText(message, 34, ExpeditionTheme.Ivory, TextAnchor.UpperLeft);
            }
            if (confirm != null)
            {
                ExpeditionUi.SetRect(confirm.transform as RectTransform, .06f, .07f, .94f, .215f);
                ExpeditionUi.StyleButton(confirm, true, 38);
                StyleButtonLabels(confirm, true, 38);
            }
        }

        public static void ApplyAura(GameObject root, Text header, RectTransform stats, Text footer)
        {
            if (root == null || stats == null) return;
            var panel = root.transform.Find("Panel") as RectTransform;
            if (panel == null) return;
            StyleRoot(root);
            var group = ExpeditionUi.Ensure<CanvasGroup>(root);
            group.alpha = 1;
            group.interactable = group.blocksRaycasts = true;
            group.ignoreParentGroups = true;
            StylePlate(panel);
            SetSafeBounds(panel, .055f, .09f, .945f, .91f);
            PlaceText(panel, "Title", 46, .055f, .90f, .945f, .975f, ExpeditionTheme.Ivory, TextAnchor.MiddleLeft);
            if (header != null)
            {
                ExpeditionUi.SetRect(header.rectTransform, .055f, .65f, .945f, .87f);
                StyleText(header, 32, ExpeditionTheme.Ivory, TextAnchor.UpperLeft);
            }
            if (footer != null)
            {
                ExpeditionUi.SetRect(footer.rectTransform, .055f, .12f, .945f, .195f);
                StyleText(footer, 25, ExpeditionTheme.Muted, TextAnchor.UpperLeft);
            }
            var scrollRoot = panel.Find("ExpeditionAuraScroll") as RectTransform;
            if (scrollRoot == null)
            {
                scrollRoot = ExpeditionUi.Rect(panel, "ExpeditionAuraScroll", .055f, .215f, .945f, .625f);
                var hit = scrollRoot.gameObject.AddComponent<Image>();
                ExpeditionUi.StylePanel(hit, new Color32(13, 33, 40, 255));
                hit.raycastTarget = true;
                var viewport = ExpeditionUi.Rect(scrollRoot, "Viewport", .02f, .02f, .98f, .98f);
                viewport.gameObject.AddComponent<RectMask2D>();
                stats.SetParent(viewport, false);
                stats.anchorMin = new Vector2(0, 1);
                stats.anchorMax = Vector2.one;
                stats.pivot = new Vector2(.5f, 1);
                stats.anchoredPosition = Vector2.zero;
                stats.sizeDelta = Vector2.zero;
                var stack = ExpeditionUi.Ensure<VerticalLayoutGroup>(stats.gameObject);
                stack.enabled = true;
                stack.padding = new RectOffset(18, 18, 12, 18);
                stack.spacing = 12;
                stack.childControlHeight = stack.childControlWidth = true;
                stack.childForceExpandHeight = false;
                stack.childForceExpandWidth = true;
                var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(stats.gameObject);
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = stats;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 45;
            }
            for (var i = 0; i < stats.childCount; i++) StyleAuraRow(stats.GetChild(i));
            var closeRoot = panel.Find("CloseButton");
            var close = closeRoot != null ? closeRoot.GetComponent<Button>() : null;
            if (close != null)
            {
                ExpeditionUi.SetRect(close.transform as RectTransform, .055f, .035f, .945f, .105f);
                ExpeditionUi.StyleButton(close, true, 36);
                StyleButtonLabels(close, true, 36);
            }
            LayoutRebuilder.MarkLayoutForRebuild(stats);
            var existingScroll = scrollRoot.GetComponent<ScrollRect>();
            if (existingScroll != null) existingScroll.verticalNormalizedPosition = 1;
        }

        private static void StyleAuraRow(Transform row)
        {
            var height = ExpeditionUi.Ensure<LayoutElement>(row.gameObject);
            height.minHeight = height.preferredHeight = 72;
            var stack = row.GetComponent<HorizontalLayoutGroup>();
            if (stack != null) stack.spacing = 20;
            foreach (var label in row.GetComponentsInChildren<Text>(true))
            {
                StyleText(label, label.name == "Value" ? 31 : 28,
                    label.name == "Value" ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory,
                    label.name == "Value" ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 23;
                label.resizeTextMaxSize = label.name == "Value" ? 31 : 28;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private static void StyleRoot(GameObject root)
        {
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = .5f;
            }
            var dim = root.transform.Find("Dim");
            var image = dim != null ? dim.GetComponent<Image>() : root.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = new Color(.025f, .065f, .08f, .96f);
                image.raycastTarget = true;
            }
        }

        private static void StylePlate(RectTransform panel)
        {
            var image = ExpeditionUi.Ensure<Image>(panel.gameObject);
            ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
            image.raycastTarget = true;
        }

        private static void StyleInput(InputField input, float x0, float y0, float x1, float y1)
        {
            if (input == null) return;
            var slot = input.transform.parent as RectTransform;
            if (slot != null) ExpeditionUi.SetRect(slot, x0, y0, x1, y1);
            ExpeditionUi.SetRect(input.transform as RectTransform, 0, 0, 1, 1);
            var image = input.GetComponent<Image>();
            if (image != null) { ExpeditionUi.StylePanel(image, new Color32(13, 33, 40, 255)); image.raycastTarget = true; input.targetGraphic = image; }
            input.caretColor = ExpeditionTheme.Amber;
            input.customCaretColor = true;
            input.selectionColor = new Color(.3f, .62f, .65f, .45f);
            if (input.textComponent != null) StyleText(input.textComponent, 34, ExpeditionTheme.Ivory, TextAnchor.MiddleLeft);
            var placeholder = input.placeholder as Text;
            if (placeholder != null) { StyleText(placeholder, 32, ExpeditionTheme.Muted, TextAnchor.MiddleLeft); placeholder.fontStyle = FontStyle.Normal; }
            ApplyInputPadding(input);
        }

        public static void ApplyInputPadding(InputField input)
        {
            if (input == null) return;
            if (input.textComponent != null)
            {
                InsetInputContent(input.textComponent.rectTransform);
                if (input.lineType == InputField.LineType.SingleLine)
                {
                    input.textComponent.alignment = TextAnchor.MiddleLeft;
                    input.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                    input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
                }
            }
            if (input.placeholder != null) InsetInputContent(input.placeholder.rectTransform);
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = ExpeditionTheme.Amber;
            input.selectionColor = new Color(.3f, .62f, .65f, .45f);
            input.ForceLabelUpdate();
        }

        public static void ApplyInputPadding(TMP_InputField input)
        {
            if (input == null) return;
            var viewport = input.textViewport;
            var hasViewport = viewport != null && viewport != input.transform;
            if (hasViewport) InsetInputContent(viewport);
            if (input.textComponent != null)
            {
                var content = input.textComponent.rectTransform;
                if (content != viewport)
                {
                    if (hasViewport && content.parent == viewport) ExpeditionUi.SetRect(content, 0, 0, 1, 1);
                    else InsetInputContent(content);
                }
                input.textComponent.margin = Vector4.zero;
            }
            if (input.placeholder != null)
            {
                var content = input.placeholder.rectTransform;
                if (content != viewport)
                {
                    if (hasViewport && content.parent == viewport) ExpeditionUi.SetRect(content, 0, 0, 1, 1);
                    else InsetInputContent(content);
                }
                if (input.placeholder is TMP_Text text) text.margin = Vector4.zero;
            }
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = ExpeditionTheme.Amber;
            input.selectionColor = new Color(.3f, .62f, .65f, .45f);
            input.ForceLabelUpdate();
        }

        private static void InsetInputContent(RectTransform content)
        {
            ExpeditionUi.SetRect(content, 0, 0, 1, 1);
            content.offsetMin = new Vector2(36, 10);
            content.offsetMax = new Vector2(-36, -10);
        }

        private static void StyleButtonLabels(Button button, bool primary, int size)
        {
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                StyleText(text, size, primary ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory, TextAnchor.MiddleCenter);
                ExpeditionUi.SetRect(text.rectTransform, .025f, .04f, .975f, .96f);
            }
        }

        private static void PlaceText(Transform panel, string name, int size, float x0, float y0, float x1, float y1, Color color, TextAnchor alignment)
        {
            var child = panel.Find(name);
            var text = child != null ? child.GetComponent<Text>() : null;
            if (text == null) return;
            ExpeditionUi.SetRect(text.rectTransform, x0, y0, x1, y1);
            StyleText(text, size, color, alignment);
        }

        private static void StyleText(Text text, int size, Color color, TextAnchor alignment)
        {
            text.font = UiFont;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.12f;
            text.raycastTarget = false;
        }

        private static void SetSafeBounds(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            ExpeditionUi.Ensure<ExpeditionSystemDialogViewport>(panel.gameObject).Configure(new Rect(x0, y0, x1 - x0, y1 - y0));
        }
    }

    public sealed class ExpeditionSystemDialogViewport : MonoBehaviour
    {
        private Rect _bounds, _safe;
        private Vector2Int _screen;
        public void Configure(Rect bounds) { _bounds = bounds; Apply(); }
        private void Update()
        {
            if (_safe != Screen.safeArea || _screen.x != Screen.width || _screen.y != Screen.height) Apply();
        }
        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            _safe = Screen.safeArea;
            _screen = new Vector2Int(Screen.width, Screen.height);
            var safe = _safe;
            if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0 ||
                safe.xMax > Screen.width || safe.yMax > Screen.height)
                safe = new Rect(0, 0, Screen.width, Screen.height);
            ExpeditionUi.SetRect(transform as RectTransform,
                (safe.xMin + safe.width * _bounds.xMin) / Screen.width,
                (safe.yMin + safe.height * _bounds.yMin) / Screen.height,
                (safe.xMin + safe.width * _bounds.xMax) / Screen.width,
                (safe.yMin + safe.height * _bounds.yMax) / Screen.height);
        }
    }
}

