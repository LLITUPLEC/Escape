using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Делает кнопку закрытия модальной панели заметной и кликабельной:
    /// Image без sprite в Unity часто не рисуется и не принимает raycast.
    /// </summary>
    public static class ModalPanelCloseButton
    {
        private static Sprite s_whiteSprite;

        public static Sprite WhiteSprite()
        {
            if (s_whiteSprite != null)
                return s_whiteSprite;

            var t = Texture2D.whiteTexture;
            s_whiteSprite = Sprite.Create(
                t,
                new Rect(0f, 0f, t.width, t.height),
                new Vector2(0.5f, 0.5f),
                100f);
            s_whiteSprite.name = "ModalPanelClose_WhiteRuntime";
            return s_whiteSprite;
        }

        /// <summary>
        /// Ставит Close в правый верхний угол sheet и гарантирует фон + подпись «X».
        /// </summary>
        public static Button EnsureTopRight(
            Button existing,
            RectTransform sheet,
            Transform searchRoot,
            string closePathUnderRoot,
            TMP_FontAsset font,
            UnityEngine.Events.UnityAction onClick)
        {
            if (sheet == null)
                return existing;

            var btn = existing;
            if (btn == null && searchRoot != null && !string.IsNullOrEmpty(closePathUnderRoot))
            {
                var tr = searchRoot.Find(closePathUnderRoot);
                if (tr != null)
                    btn = tr.GetComponent<Button>();
            }

            if (btn == null)
            {
                var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(sheet, false);
                btn = go.GetComponent<Button>();
            }

            var rt = btn.transform as RectTransform;
            if (rt == null)
                return btn;

            // Вне HorizontalLayoutGroup Header — иначе размер/позиция ломаются baked overrides.
            if (rt.parent != sheet)
                rt.SetParent(sheet, false);
            rt.SetAsLastSibling();

            var le = btn.GetComponent<LayoutElement>();
            if (le == null)
                le = btn.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.preferredWidth = 56f;
            le.preferredHeight = 56f;
            le.minWidth = 56f;
            le.minHeight = 56f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(56f, 56f);
            rt.anchoredPosition = new Vector2(-10f, -10f);
            rt.localScale = Vector3.one;

            var img = btn.GetComponent<Image>();
            if (img == null)
                img = btn.gameObject.AddComponent<Image>();
            img.sprite = WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = new Color(0.22f, 0.24f, 0.30f, 0.96f);
            img.raycastTarget = true;
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            btn.interactable = true;

            var labelTr = btn.transform.Find("Label");
            TextMeshProUGUI tmp;
            if (labelTr == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(btn.transform, false);
                tmp = labelGo.GetComponent<TextMeshProUGUI>();
                var lrt = labelGo.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
            }
            else
            {
                tmp = labelTr.GetComponent<TextMeshProUGUI>();
            }

            if (tmp != null)
            {
                if (font != null)
                    tmp.font = font;
                tmp.text = "X";
                tmp.fontSize = 28f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.95f, 0.95f, 0.92f, 1f);
                tmp.raycastTarget = false;
            }

            btn.onClick.RemoveListener(onClick);
            btn.onClick.AddListener(onClick);
            return btn;
        }

        public static void EnsureDimmerRaycast(Button dimmer)
        {
            if (dimmer == null)
                return;
            var img = dimmer.GetComponent<Image>();
            if (img == null)
                return;
            if (img.sprite == null)
                img.sprite = WhiteSprite();
            img.raycastTarget = true;
        }
    }
}
