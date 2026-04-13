using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Ссылки на элементы модалки монстра (шахта). Раскладка задаётся в префабе.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class MonsterModalView : MonoBehaviour
    {
        [Tooltip("Ширина к высоте окна (1.16 при пропорции 1.16×1).")]
        [SerializeField] private float widthOverHeight = 1.16f;
        [Tooltip("Минимальные отступы от краёв родителя (Canvas) в пикселях.")]
        [SerializeField] private float screenEdgeMargin = 72f;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text supplementalInfoText;

        [SerializeField] private GameObject monsterContentRoot;
        [SerializeField] private GameObject barrierContentRoot;

        [SerializeField] private Text characteristicsSectionTitle;
        [SerializeField] private Text[] statTexts = new Text[6];
        [SerializeField] private Text rewardsSectionTitle;
        [SerializeField] private RectTransform rewardsDynamicRoot;
        [Tooltip("Шрифт для подписей наград (рантайм-ячейки Icon/Value под RewardsDynamic).")]
        [SerializeField] private Font monsterRewardsValueFont;
        [SerializeField] private Text anomalySectionTitle;
        [SerializeField] private Image affixIcon;
        [SerializeField] private Text affixIconGlyph;
        [SerializeField] private Text affixTitleText;
        [SerializeField] private Text affixDescriptionText;

        [SerializeField] private Text barrierSectionTitle;
        [SerializeField] private Text barrierInfoText;
        [SerializeField] private Text barrierRequirementsSectionTitle;
        [SerializeField] private RectTransform barrierRequirementsRoot;

        [SerializeField] private Button fightButton;
        [SerializeField] private Button dismissButton;

        public Image BackgroundImage => backgroundImage;
        public Text TitleText => titleText;
        public Button CloseButton => closeButton;
        public Text SupplementalInfoText => supplementalInfoText;
        public GameObject MonsterContentRoot => monsterContentRoot;
        public GameObject BarrierContentRoot => barrierContentRoot;
        public Text CharacteristicsSectionTitle => characteristicsSectionTitle;
        public Text[] StatTexts => statTexts;
        public Text RewardsSectionTitle => rewardsSectionTitle;
        public RectTransform RewardsDynamicRoot => rewardsDynamicRoot;
        public Font MonsterRewardsValueFont => monsterRewardsValueFont;
        public Text AnomalySectionTitle => anomalySectionTitle;
        public Image AffixIcon => affixIcon;
        public Text AffixIconGlyph => affixIconGlyph;
        public Text AffixTitleText => affixTitleText;
        public Text AffixDescriptionText => affixDescriptionText;
        public Text BarrierSectionTitle => barrierSectionTitle;
        public Text BarrierInfoText => barrierInfoText;
        public Text BarrierRequirementsSectionTitle => barrierRequirementsSectionTitle;
        public RectTransform BarrierRequirementsRoot => barrierRequirementsRoot;
        public Button FightButton => fightButton;
        public Button DismissButton => dismissButton;

        private void OnEnable()
        {
            StartCoroutine(CoApplyModalAspect());
        }

        private IEnumerator CoApplyModalAspect()
        {
            yield return null;
            ApplyModalAspect();
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyModalAspect();
        }

        /// <summary>
        /// Центрирует модалку и задаёт размер с фиксированным соотношением сторон, вписываясь в родителя с полями.
        /// </summary>
        private void ApplyModalAspect()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null)
                return;

            var m = Mathf.Max(0f, screenEdgeMargin);
            var maxW = Mathf.Max(0f, parent.rect.width - 2f * m);
            var maxH = Mathf.Max(0f, parent.rect.height - 2f * m);
            if (maxW <= 1f || maxH <= 1f)
                return;

            var wh = Mathf.Max(0.01f, widthOverHeight);
            var w = maxW;
            var h = w / wh;
            if (h > maxH)
            {
                h = maxH;
                w = h * wh;
            }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
