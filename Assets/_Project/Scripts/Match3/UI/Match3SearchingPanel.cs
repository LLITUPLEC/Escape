using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Full-screen overlay shown while waiting for an opponent.</summary>
    public sealed class Match3SearchingPanel : MonoBehaviour
    {
        [SerializeField] public Text statusText;
        [SerializeField] public Button cancelButton;

        /// <summary>Fired when the player presses Cancel.</summary>
        public event Action OnCancelClicked;

        private void Awake()
        {
            if (cancelButton != null)
                cancelButton.onClick.AddListener(() => OnCancelClicked?.Invoke());
        }

        public void Show(string message)
        {
            gameObject.SetActive(true);
            if (statusText != null) statusText.text = message;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
