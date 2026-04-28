using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Game-over overlay with result, reward and a back-to-menu button.</summary>
    public sealed class Match3GameOverPanel : MonoBehaviour
    {
        [SerializeField] public TMP_Text   titleText;
        [SerializeField] public TMP_Text   rewardText;
        [SerializeField] public Button backButton;

        /// <summary>Fired when the player presses "Back to menu".</summary>
        public event Action OnBackClicked;

        private void Awake()
        {
            ResolveReferences();
            if (backButton != null)
                backButton.onClick.AddListener(() => OnBackClicked?.Invoke());
        }

        private void ResolveReferences()
        {
            titleText ??= transform.Find("TitleText")?.GetComponent<TMP_Text>();
            rewardText ??= transform.Find("RewardText")?.GetComponent<TMP_Text>();
        }

        public void Show(bool won, string customRewardText = null, string customTitle = null)
        {
            gameObject.SetActive(true);

            if (titleText != null)
            {
                titleText.text = !string.IsNullOrWhiteSpace(customTitle)
                    ? customTitle
                    : (won ? "Победа!" : "Поражение!");
                titleText.color = won
                    ? new Color(1f, 0.90f, 0.25f)
                    : new Color(0.85f, 0.35f, 0.35f);
            }

            if (rewardText != null)
            {
                rewardText.text = string.IsNullOrEmpty(customRewardText) ? string.Empty : customRewardText;
                rewardText.gameObject.SetActive(!string.IsNullOrEmpty(rewardText.text));
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
