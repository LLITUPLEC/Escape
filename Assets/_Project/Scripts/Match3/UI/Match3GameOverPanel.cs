using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Game-over overlay with result, reward and a back-to-menu button.</summary>
    public sealed class Match3GameOverPanel : MonoBehaviour
    {
        [SerializeField] public Text   titleText;
        [SerializeField] public Text   rewardText;
        [SerializeField] public Button backButton;

        /// <summary>Fired when the player presses "Back to menu".</summary>
        public event Action OnBackClicked;

        private void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => OnBackClicked?.Invoke());
        }

        public void Show(bool won)
        {
            gameObject.SetActive(true);

            if (titleText != null)
            {
                titleText.text  = won ? "Победа!" : "Поражение!";
                titleText.color = won
                    ? new Color(1f, 0.90f, 0.25f)
                    : new Color(0.85f, 0.35f, 0.35f);
            }

            if (rewardText != null)
                rewardText.text = won ? "+100 опыта\n+50 золота" : "+25 опыта";
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
