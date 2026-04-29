using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    public sealed class AchievementChainSlotView : MonoBehaviour
    {
        [SerializeField] private Image frameImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private Image fillImage;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressTmp;
        [SerializeField] private TMP_Text rewardTmp;
        [SerializeField] private GameObject lockOverlay;
        [Header("Optional runtime color overrides")]
        [SerializeField] private bool overrideFrameColorByState;
        [SerializeField] private bool overrideFillColorsByTier;
        [Header("Claimed visuals")]
        [SerializeField] private bool showClaimedOverlay = true;
        [SerializeField] private Color claimedOverlayColor = new Color(0.45f, 0.95f, 0.55f, 0.22f);

        private CanvasGroup _rootCanvasGroup;
        private Button _infoButton;
        private Action<string, int> _onStepClick;
        private string _chainId;
        private int _stepIndex;

        private void Awake()
        {
            _rootCanvasGroup = GetComponent<CanvasGroup>();
        }

        public void Apply(
            string chainId,
            int stepIndex,
            Color tierAccent,
            string requirementText,
            string rewardLine,
            int numerator,
            int denominator,
            bool lockedByChain,
            bool thresholdMet,
            bool rewardClaimed,
            bool canClaimReward,
            Action<string, int> onStepClick)
        {
            if (rewardTmp != null)
                rewardTmp.text = rewardLine ?? string.Empty;

            var grayLocked = lockedByChain && !thresholdMet;

            if (progressTmp != null)
                progressTmp.text = denominator > 0 ? numerator + "/" + denominator : string.Empty;

            if (progressSlider != null)
            {
                progressSlider.wholeNumbers = true;
                progressSlider.minValue = 0;
                progressSlider.maxValue = Mathf.Max(1, denominator);
                progressSlider.value = Mathf.Clamp(numerator, 0, denominator);
                progressSlider.interactable = false;

                var fill = progressSlider.fillRect != null ? progressSlider.fillRect.GetComponent<Image>() : null;
                if (fill != null && overrideFillColorsByTier)
                    fill.color = tierAccent;

                var bg = progressSlider.transform.Find("Background");
                if (bg != null)
                {
                    var bgImg = bg.GetComponent<Image>();
                    if (bgImg != null)
                        bgImg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
                }
            }

            if (fillImage != null && overrideFillColorsByTier)
                fillImage.color = tierAccent;

            if (frameImage != null && overrideFrameColorByState)
            {
                Color frameCol = grayLocked
                    ? new Color(0.35f, 0.35f, 0.37f, 1f)
                    : canClaimReward
                        ? new Color(0.95f, 0.76f, 0.22f, 1f)
                        : tierAccent;
                frameImage.color = frameCol;
            }

            if (iconImage != null)
            {
                iconImage.color = grayLocked ? new Color(0.35f, 0.35f, 0.38f, 1f) : Color.white;
                iconImage.raycastTarget = false;
            }

            if (rewardTmp != null)
                rewardTmp.raycastTarget = false;
            if (progressTmp != null)
                progressTmp.raycastTarget = false;
            if (progressSlider != null)
            {
                foreach (var g in progressSlider.GetComponentsInChildren<Graphic>(true))
                    g.raycastTarget = false;
            }

            if (lockOverlay != null)
            {
                var showOverlay = grayLocked || (showClaimedOverlay && rewardClaimed);
                lockOverlay.SetActive(showOverlay);
                var lockImg = lockOverlay.GetComponent<Image>();
                if (lockImg != null)
                {
                    if (rewardClaimed && !grayLocked)
                        lockImg.color = claimedOverlayColor;
                    lockImg.raycastTarget = false;
                }
            }

            if (_rootCanvasGroup != null)
                _rootCanvasGroup.alpha = rewardClaimed ? 1f : grayLocked ? 0.55f : 1f;

            gameObject.name = "Step_" + stepIndex + "_" + chainId;

            _chainId = chainId;
            _stepIndex = stepIndex;
            _onStepClick = onStepClick;
            EnsureInfoButton();
            if (_infoButton != null)
            {
                _infoButton.onClick.RemoveListener(HandleSlotClick);
                if (_onStepClick != null)
                    _infoButton.onClick.AddListener(HandleSlotClick);
                _infoButton.interactable = _onStepClick != null;
            }
        }

        private void HandleSlotClick()
        {
            _onStepClick?.Invoke(_chainId, _stepIndex);
        }

        private void EnsureInfoButton()
        {
            if (_infoButton != null)
                return;
            _infoButton = GetComponent<Button>();
            if (_infoButton == null)
                _infoButton = gameObject.AddComponent<Button>();
            _infoButton.transition = Selectable.Transition.None;
            if (frameImage != null)
            {
                frameImage.raycastTarget = true;
                _infoButton.targetGraphic = frameImage;
            }
        }
    }
}
