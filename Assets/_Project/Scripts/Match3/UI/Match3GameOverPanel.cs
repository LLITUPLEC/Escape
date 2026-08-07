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

        [Header("Reward Rows Layout")]
        [Tooltip("Сколько RewardRow в одной RewardLine.")]
        [SerializeField, Min(1)] public int rewardCellsPerLine = 2;
        [Tooltip("Preferred width/height у Icon внутри RewardRow.")]
        [SerializeField, Min(1f)] public float rewardIconPreferredSize = 60f;
        [Tooltip("Font size у Value внутри RewardRow.")]
        [SerializeField, Min(1f)] public float rewardValueFontSize = 50f;

        public int RewardCellsPerLine => Mathf.Max(1, rewardCellsPerLine);
        public float RewardIconPreferredSize => Mathf.Max(1f, rewardIconPreferredSize);
        public float RewardValueFontSize => Mathf.Max(1f, rewardValueFontSize);

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
                if (!string.IsNullOrWhiteSpace(customTitle) &&
                    customTitle.IndexOf("Ничья", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    titleText.color = new Color(0.75f, 0.85f, 1f);
                else
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
