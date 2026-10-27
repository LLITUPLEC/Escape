using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Shared, inexpensive visual primitives for the Expedition art direction.</summary>
    public static class ExpeditionTheme
    {
        public static readonly Color Panel = Hex(0x142E36);
        public static readonly Color Teal = Hex(0x285A60);
        public static readonly Color Ivory = Hex(0xE7DDC8);
        public static readonly Color Amber = Hex(0xEDAA53);
        public static readonly Color Muted = Hex(0xA3B6B5);
        public static readonly Color Coral = Hex(0xD56B51);
        private static Sprite _plate;
        private static TMP_FontAsset _font;

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.Load<TMP_FontAsset>("Expedition/ExpeditionSerif");
                if (_font == null) _font = TMP_Settings.defaultFontAsset;
                PrepareEllipsis(_font);
                return _font;
            }
        }

        public static UnityEngine.Font LegacyFont => Font != null ? Font.sourceFontFile : null;
        public static Sprite Icon(string key) => ExpeditionArt.Icon(key);

        private static void PrepareEllipsis(TMP_FontAsset font)
        {
            if (font == null) return;
            font.ReadFontAssetDefinition();
            // This TMP version looks up styled ellipses only in the weight cache/alternate
            // typeface, even when the main atlas contains U+2026. Cache punctuation alone
            // so the letters keep their normal synthetic bold and italic rendering.
            if (!font.characterLookupTable.TryGetValue(0x2026, out var ellipsis))
            {
                // A dynamic font can arrive with empty tables in a player build.
                font.TryAddCharacters("…", out _);
                if (!font.characterLookupTable.TryGetValue(0x2026, out ellipsis)) return;
            }
            for (uint style = 0; style < 4; style++)
            for (uint weight = 1; weight <= 9; weight++)
                font.characterLookupTable[((0x80u | (style << 4) | weight) << 24) | 0x2026u] = ellipsis;
        }

        public static Sprite Plate
        {
            get
            {
                var frame = ExpeditionArt.Frame();
                if (frame != null) return frame;
                if (_plate != null) return _plate;
                const int side = 48;
                var texture = new Texture2D(side, side, TextureFormat.RGBA32, false) { name = "ExpeditionBevel", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[side * side];
                for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    var cut = Mathf.Min(x, side - 1 - x) + Mathf.Min(y, side - 1 - y) < 6;
                    var edge = x < 2 || y < 2 || x > side - 3 || y > side - 3;
                    var shade = edge ? (byte)203 : (byte)255;
                    pixels[y * side + x] = new Color32(shade, shade, shade, cut ? (byte)0 : (byte)255);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _plate = Sprite.Create(texture, new Rect(0, 0, side, side), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
                _plate.name = "ExpeditionBevel";
                _plate.hideFlags = HideFlags.HideAndDontSave;
                return _plate;
            }
        }

        private static Color Hex(uint value) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);
    }

    public static class ExpeditionUi
    {
        // Use Unity's overloaded null comparison: GetComponent can return a missing native wrapper in the Editor.
        public static T Ensure<T>(GameObject gameObject) where T : Component
        {
            var component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        public static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            SetRect(rt, x0, y0, x1, y1);
            return rt;
        }

        public static void SetRect(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        public static Image StylePanel(Image image, Color fill)
        {
            if (image == null) return null;
            image.sprite = ExpeditionTheme.Plate;
            image.overrideSprite = null;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            var hasArt = ExpeditionArt.Frame() != null;
            // The painted plate already contains its petrol colour and steel highlights.
            // Preserve the supplied alpha; multiplying it by the old dark flat fill crushes the artwork.
            image.color = hasArt && fill.maxColorComponent > .09f
                ? new Color(1, 1, 1, fill.a) : fill;
            return image;
        }

        public static Image Panel(Transform parent, string name, float x0, float y0, float x1, float y1, Color? color = null)
        {
            var image = Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return StylePanel(image, color ?? ExpeditionTheme.Panel);
        }

        public static Image Fill(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
        {
            var image = Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TMP_Text Text(Transform parent, string name, string value, float size, float x0, float y0, float x1, float y1, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = ExpeditionTheme.Font;
            label.text = value;
            label.fontSize = size;
            label.color = ExpeditionTheme.Ivory;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.margin = new Vector4(7, 2, 7, 2);
            return label;
        }

        public static Button Button(Transform parent, string name, string value, float x0, float y0, float x1, float y1, UnityEngine.Events.UnityAction action, bool primary = false, float size = 40)
        {
            var rt = Rect(parent, name, x0, y0, x1, y1);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.gameObject.AddComponent<Image>();
            Text(rt, "Label", value, size, .025f, .04f, .975f, .96f, TextAlignmentOptions.Center);
            StyleButton(button, primary, size);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        public static void StyleButton(Button button, bool primary = false, float fontSize = 40f)
        {
            if (button == null) return;
            Project.Audio.ExpeditionAudio.BindButton(button);
            if (ExpeditionCloseButton.IsIconClose(button)) { ExpeditionCloseButton.Apply(button); return; }
            var image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image != null)
            {
                StylePanel(image, Color.white);
                if (ExpeditionArt.Frame(primary) != null) image.sprite = ExpeditionArt.Frame(primary);
                image.raycastTarget = true;
                button.targetGraphic = image;
            }
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = default;
            var colors = button.colors;
            var painted = ExpeditionArt.Frame(primary) != null;
            colors.normalColor = painted ? Color.white : primary ? ExpeditionTheme.Amber : ExpeditionTheme.Panel;
            colors.highlightedColor = painted ? new Color(1.12f, 1.1f, 1.04f) : primary ? new Color(1f, .79f, .44f) : ExpeditionTheme.Teal;
            colors.pressedColor = painted ? new Color(.78f, .75f, .66f) : primary ? new Color(.78f, .48f, .19f) : new Color(.12f, .27f, .29f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(.55f, .53f, .46f, .8f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .10f;
            button.colors = colors;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name.IndexOf("Cooldown", System.StringComparison.OrdinalIgnoreCase) >= 0 || label.name == "cd") continue;
                label.font = ExpeditionTheme.Font;
                label.fontSize = fontSize;
                label.color = primary ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory;
                label.raycastTarget = false;
            }
            foreach (var label in button.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (ExpeditionTheme.LegacyFont != null) label.font = ExpeditionTheme.LegacyFont;
                label.fontSize = Mathf.RoundToInt(fontSize);
                label.color = primary ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory;
                label.raycastTarget = false;
            }
            if (Application.isPlaying)
                Ensure<ExpeditionButtonFeedback>(button.gameObject).Configure(primary);
        }

        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        public static void Label(Button button, string value)
        {
            var tmp = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (tmp != null) tmp.text = value;
            else
            {
                var old = button != null ? button.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
                if (old != null) old.text = value;
            }
            if (ExpeditionCloseButton.IsIconClose(button)) ExpeditionCloseButton.Apply(button);
        }

        public static void IconOnly(Button button, Graphic icon)
        {
            if (button == null || icon == null) return;
            var hit = Ensure<Image>(button.gameObject);
            hit.sprite = ModalPanelCloseButton.WhiteSprite(); hit.overrideSprite = null;
            hit.type = Image.Type.Simple; hit.color = new Color(0, 0, 0, 1f / 255f);
            hit.raycastTarget = true; hit.canvasRenderer.cullTransparentMesh = false;
            button.targetGraphic = icon;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ExpeditionButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Vector3 _scale;
        private Selectable _selectable;
        private bool _pressed;
        private float _amount = 1;
        private bool _primary, _lastInteractable;
        private TMP_Text[] _primaryTmp;
        private Text[] _primaryLegacy;
        private void Awake() { _scale = transform.localScale; _selectable = GetComponent<Selectable>(); }
        public void Configure(bool primary)
        {
            _primary = primary;
            if (!primary) return;
            _primaryTmp = GetComponentsInChildren<TMP_Text>(true);
            _primaryLegacy = GetComponentsInChildren<Text>(true);
            _lastInteractable = _selectable != null && _selectable.IsInteractable();
            RefreshLabels(_lastInteractable);
        }
        private void RefreshLabels(bool enabledState)
        {
            var tint = enabledState ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory;
            if (_primaryTmp != null) foreach (var label in _primaryTmp) if (label != null) label.color = tint;
            if (_primaryLegacy != null) foreach (var label in _primaryLegacy) if (label != null) label.color = tint;
        }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable == null || !_selectable.IsInteractable()) return;
            if (Mathf.Abs(_amount - 1) < .001f) _scale = transform.localScale;
            _pressed = true;
        }
        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
        public void OnPointerExit(PointerEventData eventData) => _pressed = false;
        private void Update()
        {
            if (_primary && _selectable != null)
            {
                var activeState = _selectable.IsInteractable();
                if (activeState != _lastInteractable) { _lastInteractable = activeState; RefreshLabels(activeState); }
            }
            var target = _pressed ? .96f : 1f;
            if (Mathf.Abs(_amount - target) < .0001f) return;
            _amount = Mathf.MoveTowards(_amount, target, Time.unscaledDeltaTime * .65f);
            transform.localScale = _scale * _amount;
        }
        private void OnDisable() => ResetScale();
        private void ResetScale() { _pressed = false; _amount = 1; if (_scale != Vector3.zero) transform.localScale = _scale; }
    }
}
