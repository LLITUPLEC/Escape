using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Presentation primitives for the matching and outcome screens; never supplies match data.</summary>
    public static class ExpeditionMatchStates
    {
        public static RectTransform PrepareOverlay(Component owner, int sortingOrder)
        {
            // A nested camera canvas cannot draw above the shell's overlay canvas. Keep the
            // actual bound panel as a scene-owned overlay root, including its existing callbacks.
            owner.transform.SetParent(null, false);
            var canvas = ExpeditionUi.Ensure<Canvas>(owner.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = sortingOrder;
            var scaler = ExpeditionUi.Ensure<CanvasScaler>(owner.gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            ExpeditionUi.Ensure<GraphicRaycaster>(owner.gameObject);
            var fill = ExpeditionUi.Ensure<Image>(owner.gameObject);
            fill.sprite = null; fill.color = new Color32(5, 19, 24, 255); fill.raycastTarget = true;
            var safe = owner.transform.Find("ExpeditionMatchStateSafeArea") as RectTransform;
            if (safe != null) return safe;
            var backdrop = ExpeditionUi.Fill(owner.transform, "ExpeditionMatchStateBackdrop", 0, 0, 1, 1, Color.white);
            backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
            if (backdrop.sprite != null)
            {
                var fit = backdrop.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = backdrop.sprite.rect.width / backdrop.sprite.rect.height;
            }
            else backdrop.color = ExpeditionTheme.Panel;
            backdrop.transform.SetAsFirstSibling();
            ExpeditionUi.Fill(owner.transform, "ExpeditionMatchStateShade", 0, 0, 1, 1, new Color(.005f, .025f, .03f, .28f));
            safe = ExpeditionUi.Rect(owner.transform, "ExpeditionMatchStateSafeArea", 0, 0, 1, 1);
            safe.gameObject.AddComponent<ExpeditionSafeArea>();
            return safe;
        }

        public static Image Panel(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var frame = ExpeditionUi.Panel(parent, name, x0, y0, x1, y1);
            ExpeditionUi.Fill(frame.transform, "OpaqueInnerWell", .025f, .045f, .975f, .955f, new Color32(8, 25, 30, 255));
            return frame;
        }

        public static Image Portrait(Transform parent, string name, float x0, float y0, float x1, float y1, bool unknown, bool upperBody = false)
        {
            var frame = Panel(parent, name, x0, y0, x1, y1);
            var well = ExpeditionUi.Rect(frame.transform, "PortraitCrop", .065f, .045f, .935f, .955f);
            well.gameObject.AddComponent<RectMask2D>();
            var hero = ExpeditionUi.Fill(well, "Portrait", 0, 0, 1, 1, unknown ? new Color(0, 0, 0, .72f) : Color.white);
            hero.sprite = Resources.Load<Sprite>("Expedition/Art/hero"); hero.preserveAspect = true;
            if (!unknown && name == "LocalExplorerPortrait") Project.Character.UI.CharacterAppearanceImage.Bind(hero);
            if (upperBody) CropUpperBody(hero);
            if (unknown)
            {
                var question = ExpeditionUi.Text(well, "UnknownOpponent", "?", 150, .1f, .20f, .9f, .73f, TextAlignmentOptions.Center);
                question.color = ExpeditionTheme.Muted;
            }
            return hero;
        }

        public static void CropUpperBody(Image portrait, float zoom = 2.2f)
        {
            if (portrait == null || portrait.sprite == null) return;
            var rect = portrait.rectTransform;
            rect.anchorMin = new Vector2((1 - zoom) * .5f, 1);
            rect.anchorMax = new Vector2((1 + zoom) * .5f, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
            var fit = ExpeditionUi.Ensure<AspectRatioFitter>(portrait.gameObject);
            fit.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            fit.aspectRatio = portrait.sprite.rect.width / portrait.sprite.rect.height;
            fit.enabled = true;
        }

        public static void StyleCancel(Button button)
        {
            if (button == null) return;
            ExpeditionUi.StyleButton(button, false, 42);
            if (button.targetGraphic is Image image)
            {
                // A red tint over a petrol plate turns almost black. Start from the
                // light painted amber plate; compensate its yellow in the muted red tint.
                if (ExpeditionArt.Frame(true) != null) image.sprite = ExpeditionArt.Frame(true);
                image.color = new Color(.72f, .34f, .90f, 1);
                image.canvasRenderer.SetColor(Color.white);
            }
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1.04f, 1);
            colors.pressedColor = new Color(.80f, .77f, .73f, 1);
            button.colors = colors;
        }

        public static void Place(TMP_Text label, Transform parent, float x0, float y0, float x1, float y1,
            float size, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            if (label == null) return;
            label.transform.SetParent(parent, false);
            ExpeditionUi.SetRect(label.rectTransform, x0, y0, x1, y1);
            label.font = ExpeditionTheme.Font; label.fontSize = size;
            label.enableAutoSizing = true; label.fontSizeMin = Mathf.Max(22, size * .68f); label.fontSizeMax = size;
            label.color = ExpeditionTheme.Ivory; label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.margin = Vector4.zero; label.raycastTarget = false;
        }

        public static void ScrollBar(ScrollRect scroll, Transform parent, float x0, float y0, float x1, float y1)
        {
            var track = ExpeditionUi.Fill(parent, "RewardScrollBar", x0, y0, x1, y1, new Color32(6, 18, 22, 255));
            track.raycastTarget = true;
            var area = ExpeditionUi.Rect(track.transform, "SlidingArea", .1f, .02f, .9f, .98f);
            var handle = ExpeditionUi.Fill(area, "Handle", 0, 0, 1, 1, ExpeditionTheme.Amber);
            handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
    }

    /// <summary>Static low-vertex rotor; the search panel rotates one transform, with no per-frame mesh generation.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionMatchSpinner : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .49f;
            var center = rectTransform.rect.center;
            const int segments = 48;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2 / segments; var b = (i + 1) * Mathf.PI * 2 / segments;
                var outer = i < 32 ? ExpeditionTheme.Amber : (Color)new Color32(63, 84, 82, 255);
                RingQuad(vh, center, radius * .83f, radius, a, b, outer);
                RingQuad(vh, center, radius * .66f, radius * .76f, a, b, new Color32(112, 123, 110, 255));
                if (i % 12 < 7) RingQuad(vh, center, radius * .15f, radius * .58f, a, b, new Color32(116, 106, 78, 255));
            }
        }

        private static void RingQuad(VertexHelper vh, Vector2 p, float inner, float outer, float a, float b, Color tint)
        {
            var i = vh.currentVertCount;
            var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            vh.AddVert(p + da * inner, tint, Vector2.zero); vh.AddVert(p + da * outer, tint, Vector2.zero);
            vh.AddVert(p + db * outer, tint, Vector2.zero); vh.AddVert(p + db * inner, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionOutcomeGlow : MaskableGraphic
    {
        private bool _celebration;
        public bool Celebration
        {
            get => _celebration;
            set { if (_celebration != value) { _celebration = value; SetVerticesDirty(); } }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var p = r.center;
            var first = vh.currentVertCount;
            var bright = color; bright.a *= .52f;
            var clear = color; clear.a = 0;
            vh.AddVert(p, bright, Vector2.zero);
            const int count = 32;
            for (var i = 0; i <= count; i++)
            {
                var angle = i * Mathf.PI * 2 / count;
                vh.AddVert(p + new Vector2(Mathf.Cos(angle) * r.width * .5f, Mathf.Sin(angle) * r.height * .5f), clear, Vector2.zero);
                if (i > 0) vh.AddTriangle(first, first + i, first + i + 1);
            }
            if (!_celebration) return;
            for (var ray = 0; ray < 16; ray++)
            {
                var a = ray * Mathf.PI * 2 / 16;
                var i = vh.currentVertCount;
                var tint = color; tint.a *= .15f;
                vh.AddVert(p, tint, Vector2.zero);
                vh.AddVert(p + new Vector2(Mathf.Cos(a - .035f) * r.width * .5f, Mathf.Sin(a - .035f) * r.height * .5f), clear, Vector2.zero);
                vh.AddVert(p + new Vector2(Mathf.Cos(a + .035f) * r.width * .5f, Mathf.Sin(a + .035f) * r.height * .5f), clear, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
            }
        }
    }

    public sealed class ExpeditionRewardRowsObserver : MonoBehaviour
    {
        public Match3GameOverPanel owner;
        public bool observeDimensions;
        private void OnTransformChildrenChanged() { if (owner != null) owner.RequestRewardRefresh(); }
        private void OnRectTransformDimensionsChange() { if (observeDimensions && owner != null) owner.RequestRewardRefresh(); }
    }
}
