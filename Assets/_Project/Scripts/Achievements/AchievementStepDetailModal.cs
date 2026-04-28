using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Модалка с текстом условия и награды для шага цепочки (открывается по тапу по слоту).</summary>
    public sealed class AchievementStepDetailModal : MonoBehaviour
    {
        private CanvasGroup _rootCg;
        private TMP_Text _titleTmp;
        private TMP_Text _bodyTmp;
        private TMP_Text _rewardTmp;

        public static AchievementStepDetailModal Ensure(Transform parent, TMP_FontAsset font)
        {
            if (parent == null) return null;
            var existing = parent.GetComponentInChildren<AchievementStepDetailModal>(true);
            if (existing != null)
            {
                existing._font = font;
                return existing;
            }

            var go = new GameObject("AchievementStepDetailModal", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0f;

            var modal = go.AddComponent<AchievementStepDetailModal>();
            modal._font = font;
            modal._rootCg = cg;
            modal.Build(go.transform);
            go.SetActive(false);
            return modal;
        }

        private TMP_FontAsset _font;

        private void Build(Transform root)
        {
            var dim = new GameObject("Dimmer", typeof(RectTransform));
            dim.transform.SetParent(root, false);
            Stretch(dim.GetComponent<RectTransform>());
            var dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.62f);
            var dimBtn = dim.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Hide);

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 360f);
            var pImg = panel.AddComponent<Image>();
            pImg.color = new Color(0.09f, 0.10f, 0.14f, 0.98f);

            var vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(20, 20, 18, 14);
            vl.spacing = 12f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;

            _titleTmp = MakeTmp(panel.transform, "Title", "Что сделать", 22f, FontStyles.Bold);
            _bodyTmp = MakeTmp(panel.transform, "Body", "", 18f, FontStyles.Normal);
            _bodyTmp.textWrappingMode = TextWrappingModes.Normal;
            var bodyLe = _bodyTmp.gameObject.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            bodyLe.minHeight = 80f;

            _rewardTmp = MakeTmp(panel.transform, "Reward", "", 17f, FontStyles.Italic);
            _rewardTmp.color = new Color(0.55f, 0.92f, 0.48f, 1f);

            var btnGo = new GameObject("OkButton", typeof(RectTransform));
            btnGo.transform.SetParent(panel.transform, false);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(200f, 44f);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 44f;
            btnLe.minHeight = 44f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.28f, 0.52f, 0.85f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(Hide);
            var btnLbl = MakeTmp(btnGo.transform, "Label", "Понятно", 18f, FontStyles.Bold);
            Stretch(btnLbl.rectTransform);
            btnLbl.alignment = TextAlignmentOptions.Center;

            AchievementsTmpMaterialRepair.RepairHierarchy(root, _font);
        }

        private TMP_Text MakeTmp(Transform parent, string name, string text, float size, FontStyles fs)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = fs;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            var fa = AchievementUiFontLoader.Resolve(_font);
            if (fa != null)
            {
                tmp.font = fa;
                if (fa.material != null)
                    tmp.fontSharedMaterial = fa.material;
            }

            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void Show(string requirementText, string rewardText)
        {
            if (_titleTmp != null)
                _titleTmp.text = "Условие";
            if (_bodyTmp != null)
                _bodyTmp.text = requirementText ?? string.Empty;
            if (_rewardTmp != null)
                _rewardTmp.text = string.IsNullOrEmpty(rewardText) ? string.Empty : rewardText;
            gameObject.SetActive(true);
            if (_rootCg != null)
            {
                _rootCg.alpha = 1f;
                _rootCg.blocksRaycasts = true;
                _rootCg.interactable = true;
            }

            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_rootCg != null)
            {
                _rootCg.alpha = 0f;
                _rootCg.blocksRaycasts = false;
                _rootCg.interactable = false;
            }

            gameObject.SetActive(false);
        }
    }
}
