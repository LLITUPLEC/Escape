using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>One frame-free close symbol, preserving the original button and its callbacks.</summary>
    [DefaultExecutionOrder(30000), DisallowMultipleComponent]
    public sealed class ExpeditionCloseButton : MonoBehaviour
    {
        private Button _button;
        private Image _hit;
        private ExpeditionCloseGlyph _glyph;
        private Graphic[] _oldGraphics;
        private bool _dirty;

        public static bool IsIconClose(Button button)
        {
            if (button == null) return false;
            if (button.GetComponent<ExpeditionCloseButton>() != null) return true;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                if (label.GetComponentInParent<Button>(true) == button && IsCross(label.text)) return true;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
                if (label.GetComponentInParent<Button>(true) == button && IsCross(label.text)) return true;
            return false;
        }

        private static bool IsCross(string value) => value != null &&
            (value.Trim() == "X" || value.Trim() == "x" || value.Trim() == "×" || value.Trim() == "✕" || value.Trim() == "✖");

        public static void Apply(Button button)
        {
            if (button == null) return;
            var view = ExpeditionUi.Ensure<ExpeditionCloseButton>(button.gameObject);
            view.Configure();
        }

        public static void StyleWithin(Transform root)
        {
            if (root == null) return;
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (IsIconClose(button)) Apply(button);
        }

        private void OnEnable() => _dirty = true;
        private void OnTransformChildrenChanged() => _dirty = true;
        private void LateUpdate()
        {
            if (_dirty || _button == null || _glyph == null || _hit == null ||
                _button.targetGraphic != _glyph || _hit.sprite != ModalPanelCloseButton.WhiteSprite()) Configure();
        }

        private void Configure()
        {
            _button = GetComponent<Button>();
            if (_button == null) return;
            _hit = ExpeditionUi.Ensure<Image>(gameObject);
            _hit.enabled = true;
            _hit.sprite = ModalPanelCloseButton.WhiteSprite();
            _hit.overrideSprite = null;
            _hit.type = Image.Type.Simple;
            _hit.color = new Color(0, 0, 0, 1f / 255f);
            _hit.raycastTarget = true;
            _hit.canvasRenderer.cullTransparentMesh = false;
            _glyph = transform.Find("ExpeditionCloseGlyph")?.GetComponent<ExpeditionCloseGlyph>();
            if (_glyph == null)
                _glyph = ExpeditionUi.Rect(transform, "ExpeditionCloseGlyph", .23f, .23f, .77f, .77f).gameObject.AddComponent<ExpeditionCloseGlyph>();
            _glyph.raycastTarget = false;
            _glyph.color = Color.white;
            _oldGraphics = GetComponentsInChildren<Graphic>(true);
            foreach (var graphic in _oldGraphics)
                if (graphic != _hit && graphic != _glyph && graphic.GetComponentInParent<Button>(true) == _button)
                { graphic.enabled = false; graphic.raycastTarget = false; }
            _button.targetGraphic = _glyph;
            _button.transition = Selectable.Transition.ColorTint;
            _button.spriteState = default;
            var colors = _button.colors;
            colors.normalColor = new Color32(225, 103, 83, 255);
            colors.highlightedColor = new Color32(255, 157, 124, 255);
            colors.pressedColor = new Color32(158, 61, 48, 255);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color32(119, 94, 88, 170);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .1f;
            _button.colors = colors;
            _dirty = false;
        }
    }

    /// <summary>Two beveled metal strokes. No font, bitmap or extra texture.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionCloseGlyph : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = GetPixelAdjustedRect();
            var side = Mathf.Min(r.width, r.height);
            var c = r.center;
            var arm = side * .42f;
            var dark = new Color(.18f, .08f, .06f, color.a);
            Stroke(mesh, c + new Vector2(-arm, -arm - side * .04f), c + new Vector2(arm, arm - side * .04f), side * .23f, dark);
            Stroke(mesh, c + new Vector2(-arm, arm - side * .04f), c + new Vector2(arm, -arm - side * .04f), side * .23f, dark);
            Stroke(mesh, c + new Vector2(-arm, -arm), c + new Vector2(arm, arm), side * .17f, color);
            Stroke(mesh, c + new Vector2(-arm, arm), c + new Vector2(arm, -arm), side * .17f, color);
        }
        private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
        {
            var d = (to - from).normalized;
            var n = new Vector2(-d.y, d.x) * width * .5f;
            var start = mesh.currentVertCount;
            mesh.AddVert(from - n, tint, Vector2.zero); mesh.AddVert(from + n, tint, Vector2.zero);
            mesh.AddVert(to + n, tint, Vector2.zero); mesh.AddVert(to - n, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
