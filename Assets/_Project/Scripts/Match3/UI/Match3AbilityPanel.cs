using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Ability buttons panel (Cross 5×5 and Square 3×3).
    /// Wire up button references in the Inspector or via Match3PrefabCreator.
    /// </summary>
    public sealed class Match3AbilityPanel : MonoBehaviour
    {
        [Header("Cross ability")]
        [SerializeField] public Button crossButton;
        [SerializeField] public Text   crossCooldownText;

        [Header("Square ability")]
        [SerializeField] public Button squareButton;
        [SerializeField] public Text   squareCooldownText;

        [Header("Hint bar (shown while waiting for cell click)")]
        [SerializeField] public GameObject abilityHint;

        /// <summary>Fired when Cross button is clicked.</summary>
        public event Action OnCrossClicked;
        /// <summary>Fired when Square button is clicked.</summary>
        public event Action OnSquareClicked;

        private void Awake()
        {
            if (crossButton  != null) crossButton.onClick.AddListener(()  => OnCrossClicked?.Invoke());
            if (squareButton != null) squareButton.onClick.AddListener(() => OnSquareClicked?.Invoke());
        }

        // ─── API ──────────────────────────────────────────────────────────────────

        public void Refresh(PlayerStats stats, bool isMyTurn, bool gameEnded, int abilityCost)
        {
            bool active      = isMyTurn && !gameEnded;
            bool crossReady  = stats.crossCooldown  == 0 && stats.mana >= abilityCost;
            bool squareReady = stats.squareCooldown == 0 && stats.mana >= abilityCost;

            if (crossButton  != null) crossButton.interactable  = active && crossReady;
            if (squareButton != null) squareButton.interactable  = active && squareReady;

            if (crossCooldownText != null)
                crossCooldownText.text = stats.crossCooldown > 0
                    ? $"⟳{stats.crossCooldown} ход" : $"{abilityCost} мп";

            if (squareCooldownText != null)
                squareCooldownText.text = stats.squareCooldown > 0
                    ? $"⟳{stats.squareCooldown} ход" : $"{abilityCost} мп";
        }

        public void ShowHint(bool show)
        {
            if (abilityHint != null) abilityHint.SetActive(show);
        }
    }
}
