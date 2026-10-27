using Project.Character.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Scrollable presentation of live item/achievement data. All action handlers remain on their original buttons.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionItemDialogs : MonoBehaviour
    {
        private bool _built;
        private RectTransform _panel;
        private RectTransform _content;
        private ScrollRect _scroll;
        private TMP_Text _stats;
        private TMP_Text _statsCaption;

        public static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        public static ExpeditionItemDialogs ApplyInfo(RectTransform panel, TMP_Text title, TMP_Text slot,
            TMP_Text description, TMP_Text stats, Button close, Button equip, Button learn, Button salvage)
        {
            if (panel == null || title == null || stats == null) return null;
            var view = EnsureComponent<ExpeditionItemDialogs>(panel.gameObject);
            view._panel = panel;
            if (!view._built)
            {
                DisableLayout(panel);
                StylePanel(panel);
                if (title != null)
                {
                    ExpeditionUi.SetRect(title.rectTransform, .06f, .9f, .82f, .975f);
                    StyleText(title, 42, title.color, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    FitHeader(title, 32, 42);
                }
                if (slot != null)
                {
                    ExpeditionUi.SetRect(slot.rectTransform, .06f, .83f, .94f, .895f);
                    StyleText(slot, 29, ExpeditionTheme.Muted, TextAlignmentOptions.MidlineLeft);
                }
                view.CreateScroll(panel, .045f, .18f, .955f, .815f);
                view._stats = stats;
                if (description != null)
                {
                    MoveToContent(description, view._content);
                    StyleText(description, 31, ExpeditionTheme.Muted, TextAlignmentOptions.TopLeft);
                    description.richText = true;
                }
                view._statsCaption = ExpeditionUi.Text(view._content, "ExpeditionItemStatsCaption", "ХАРАКТЕРИСТИКИ", 24,
                    0, 0, 1, 1, TextAlignmentOptions.MidlineLeft);
                view._statsCaption.color = ExpeditionTheme.Muted;
                var captionLayout = EnsureComponent<LayoutElement>(view._statsCaption.gameObject);
                captionLayout.preferredHeight = captionLayout.minHeight = 42;
                if (stats != null)
                {
                    MoveToContent(stats, view._content);
                    StyleText(stats, 34, ExpeditionTheme.Ivory, TextAlignmentOptions.TopLeft);
                    stats.richText = true;
                }
                var actions = ExpeditionUi.Rect(panel, "ExpeditionItemActions", .06f, .05f, .94f, .145f);
                var row = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.spacing = 18;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = true;
                row.childForceExpandHeight = true;
                foreach (var button in new[] { equip, learn, salvage })
                {
                    if (button == null) continue;
                    button.transform.SetParent(actions, false);
                    var le = EnsureComponent<LayoutElement>(button.gameObject);
                    le.minWidth = 160;
                    le.flexibleWidth = 1;
                    StyleButton(button, button == equip || button == learn, 34);
                }
                if (close != null)
                {
                    ExpeditionUi.SetRect(close.transform as RectTransform, .865f, .915f, .96f, .975f);
                    StyleButton(close, false, 40);
                    ExpeditionUi.Label(close, "×");
                }
                view._built = true;
            }
            SafeRect(panel, .06f, .135f, .94f, .865f);
            view.RefreshContent();
            return view;
        }

        public static void ApplyAction(RectTransform panel, Button info, Button sell)
        {
            if (panel == null || info == null || sell == null) return;
            var view = EnsureComponent<ExpeditionItemDialogs>(panel.gameObject);
            if (!view._built)
            {
                DisableLayout(panel);
                StylePanel(panel);
                if (info != null)
                {
                    ExpeditionUi.SetRect(info.transform as RectTransform, .06f, .55f, .94f, .92f);
                    StyleButton(info, true, 36);
                }
                if (sell != null)
                {
                    ExpeditionUi.SetRect(sell.transform as RectTransform, .06f, .08f, .94f, .45f);
                    StyleButton(sell, false, 36);
                }
                view._built = true;
            }
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            var parent = panel.parent as RectTransform;
            panel.sizeDelta = new Vector2(parent != null ? Mathf.Min(570, parent.rect.width * .84f) : 570, 260);
        }

        public static void ClampAction(RectTransform panel)
        {
            if (panel == null || !(panel.parent is RectTransform parent)) return;
            var canvas = panel.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var safe = Screen.safeArea;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, camera, out var min)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, camera, out var max)) return;
            // On the character page the action menu stays between the shared HUD and navigation.
            min = Vector2.Max(min, parent.rect.min);
            max = Vector2.Min(max, parent.rect.max);
            var half = panel.rect.size * .5f;
            var position = panel.localPosition;
            var x0 = min.x + half.x + 16;
            var x1 = max.x - half.x - 16;
            var y0 = min.y + half.y + 16;
            var y1 = max.y - half.y - 16;
            position.x = x1 > x0 ? Mathf.Clamp(position.x, x0, x1) : (min.x + max.x) * .5f;
            position.y = y1 > y0 ? Mathf.Clamp(position.y, y0, y1) : (min.y + max.y) * .5f;
            panel.localPosition = position;
        }

        public static void ApplyAchievement(Transform root, TMP_Text title, TMP_Text body, TMP_Text reward,
            Button primary, UnityAction closeAction)
        {
            if (root == null) return;
            var view = EnsureComponent<ExpeditionItemDialogs>(root.gameObject);
            view._panel = root.Find("Panel") as RectTransform;
            if (view._panel == null) return;
            if (!view._built)
            {
                var panel = view._panel;
                DisableLayout(panel);
                StylePanel(panel);
                if (title != null)
                {
                    ExpeditionUi.SetRect(title.rectTransform, .06f, .84f, .84f, .97f);
                    StyleText(title, 40, ExpeditionTheme.Ivory, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    FitHeader(title, 30, 40);
                }
                view.CreateScroll(panel, .045f, .2f, .955f, .82f);
                if (body != null)
                {
                    MoveToContent(body, view._content);
                    StyleText(body, 36, ExpeditionTheme.Ivory, TextAlignmentOptions.TopLeft);
                }
                if (reward != null)
                {
                    MoveToContent(reward, view._content);
                    StyleText(reward, 34, ExpeditionTheme.Amber, TextAlignmentOptions.TopLeft, FontStyles.Bold);
                }
                if (primary != null)
                {
                    ExpeditionUi.SetRect(primary.transform as RectTransform, .065f, .05f, .935f, .155f);
                    StyleButton(primary, true, 36);
                }
                ExpeditionUi.Button(panel, "ExpeditionAchievementClose", "×", .865f, .89f, .965f, .97f, closeAction, false, 40);
                view._built = true;
            }
            SafeRect(view._panel, .065f, .24f, .935f, .76f);
            view.RefreshContent();
        }

        public void RefreshContent()
        {
            if (_statsCaption != null) _statsCaption.gameObject.SetActive(_stats != null && !string.IsNullOrWhiteSpace(_stats.text));
            if (_content != null) LayoutRebuilder.MarkLayoutForRebuild(_content);
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1;
        }

        public void SetStatsCaption(string value)
        {
            if (_statsCaption != null) _statsCaption.text = value ?? string.Empty;
        }

        private void CreateScroll(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            var scrollRect = ExpeditionUi.Rect(panel, "ExpeditionDialogBody", x0, y0, x1, y1);
            var scrollBackground = scrollRect.gameObject.AddComponent<Image>();
            ExpeditionUi.StylePanel(scrollBackground, new Color32(16, 38, 45, 255));
            scrollBackground.raycastTarget = true;
            var viewport = ExpeditionUi.Rect(scrollRect, "Viewport", .025f, .025f, .975f, .975f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _content = ExpeditionUi.Rect(viewport, "Content", 0, 1, 1, 1);
            _content.pivot = new Vector2(.5f, 1);
            var stack = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(20, 20, 18, 24);
            stack.spacing = 20;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandHeight = false;
            stack.childForceExpandWidth = true;
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 42;
        }

        private static void MoveToContent(TMP_Text text, RectTransform content)
        {
            text.transform.SetParent(content, false);
            text.rectTransform.localScale = Vector3.one;
            text.textWrappingMode = TextWrappingModes.Normal;
            var oldLayout = text.GetComponent<LayoutElement>();
            if (oldLayout != null)
            {
                oldLayout.ignoreLayout = false;
                oldLayout.minHeight = oldLayout.preferredHeight = -1;
                oldLayout.flexibleHeight = 0;
            }
        }

        private static void StylePanel(RectTransform panel)
        {
            var image = EnsureComponent<Image>(panel.gameObject);
            ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
            image.raycastTarget = true;
        }

        private static void StyleButton(Button button, bool primary, float size)
        {
            var oldFeedback = button.GetComponent<CharacterModalButtonFx>();
            if (oldFeedback != null) { oldFeedback.StopAllCoroutines(); oldFeedback.enabled = false; }
            ExpeditionUi.StyleButton(button, primary, size);
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                StyleText(label, size, primary ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory, TextAlignmentOptions.Center, FontStyles.Bold);
                ExpeditionUi.SetRect(label.rectTransform, .025f, .04f, .975f, .96f);
            }
        }

        private static void StyleText(TMP_Text text, float size, Color color, TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
        {
            text.font = ExpeditionTheme.Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = false;
        }

        private static void FitHeader(TMP_Text title, float min, float max)
        {
            title.enableAutoSizing = true;
            title.fontSizeMin = min;
            title.fontSizeMax = max;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.maxVisibleLines = 2;
        }

        private static void DisableLayout(RectTransform panel)
        {
            var layout = panel.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private static void SafeRect(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            var safe = Screen.safeArea;
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            var scale = panel.localScale;
            ExpeditionUi.SetRect(panel, (safe.xMin + safe.width * x0) / width, (safe.yMin + safe.height * y0) / height,
                (safe.xMin + safe.width * x1) / width, (safe.yMin + safe.height * y1) / height);
            panel.localScale = scale;
        }
    }
}
