using Project.UI;
using TMPro;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>Compatibility binding for old achievement prefabs; one global overlay owns all notices.</summary>
    public sealed class AchievementToastPresenter : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private TMP_Text titleTmp;
        [SerializeField] private TMP_Text rewardTmp;
        [SerializeField] private float showSeconds = 4f;
        [SerializeField] private float slidePixels = 120f;

        private void Awake() => HideLegacyPanel();
        private void OnEnable()
        {
            HideLegacyPanel();
            AchievementInGameToastHost.Ensure();
        }

        private void HideLegacyPanel()
        {
            // Its former panel-relative queue could stop while hidden and its
            // cumulative slide offsets moved later notices above the viewport.
            if (canvasGroup == null) return;
            canvasGroup.alpha = 0;
            canvasGroup.interactable = canvasGroup.blocksRaycasts = false;
        }
    }
}
