using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Ability buttons panel (Petard, Cross and Square).
    /// </summary>
    public sealed class Match3AbilityPanel : MonoBehaviour
    {
        [Header("Petard ability")]
        [SerializeField] public Button petardButton;
        [SerializeField] public Text   petardCooldownText;

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
        /// <summary>Fired when Petard button is clicked.</summary>
        public event Action OnPetardClicked;

        private readonly Dictionary<Button, Color> _baseButtonColors = new Dictionary<Button, Color>();
        private AbilityType? _selectedAbility;
        private bool _petardBound;
        private bool _crossBound;
        private bool _squareBound;

        private void Awake()
        {
            BindButtonListeners();
            CacheButtonColor(petardButton);
            CacheButtonColor(crossButton);
            CacheButtonColor(squareButton);

            // UI requirement: abilities should use only icon sprites, no text.
            if (petardCooldownText != null) petardCooldownText.text = string.Empty;
            if (crossCooldownText != null) crossCooldownText.text = string.Empty;
            if (squareCooldownText != null) squareCooldownText.text = string.Empty;
            ApplySelectedVisuals();
        }

        // ─── API ──────────────────────────────────────────────────────────────────

        public void Refresh(PlayerStats stats, bool isMyTurn, bool gameEnded, int crossCost, int squareCost, int petardCost)
        {
            BindButtonListeners();
            CacheButtonColor(petardButton);
            CacheButtonColor(crossButton);
            CacheButtonColor(squareButton);
            bool active = isMyTurn && !gameEnded;
            bool petardHasMana = stats.mana >= petardCost;
            bool crossHasMana = stats.mana >= crossCost;
            bool squareHasMana = stats.mana >= squareCost;
            bool petardCooldown = stats.petardCooldown > 0;
            bool crossCooldown = stats.crossCooldown > 0;
            bool squareCooldown = stats.squareCooldown > 0;

            bool petardSelectable = active && !petardCooldown && petardHasMana;
            bool crossSelectable = active && !crossCooldown && crossHasMana;
            bool squareSelectable = active && !squareCooldown && squareHasMana;

            if (petardButton != null) petardButton.interactable = petardSelectable;
            if (crossButton != null) crossButton.interactable = crossSelectable || _selectedAbility == AbilityType.Cross;
            if (squareButton != null) squareButton.interactable = squareSelectable || _selectedAbility == AbilityType.Square;

            if (petardCooldownText != null) petardCooldownText.text = string.Empty;
            if (crossCooldownText != null) crossCooldownText.text = string.Empty;
            if (squareCooldownText != null) squareCooldownText.text = string.Empty;

            ApplyCooldownVisuals(petardButton, petardCooldown);
            ApplyCooldownVisuals(crossButton, crossCooldown);
            ApplyCooldownVisuals(squareButton, squareCooldown);
            ApplySelectedVisuals();
        }

        public void ShowHint(bool show)
        {
            if (abilityHint != null) abilityHint.SetActive(false);
        }

        public void SetSelectedAbility(AbilityType? ability)
        {
            _selectedAbility = ability;
            ApplySelectedVisuals();
        }

        private void CacheButtonColor(Button button)
        {
            if (button == null || button.targetGraphic == null) return;
            var img = button.targetGraphic as Image;
            if (img == null) return;
            _baseButtonColors[button] = img.color;
        }

        private void BindButtonListeners()
        {
            if (!_petardBound && petardButton != null)
            {
                petardButton.onClick.AddListener(() => OnPetardClicked?.Invoke());
                _petardBound = true;
            }
            if (!_crossBound && crossButton != null)
            {
                crossButton.onClick.AddListener(() => OnCrossClicked?.Invoke());
                _crossBound = true;
            }
            if (!_squareBound && squareButton != null)
            {
                squareButton.onClick.AddListener(() => OnSquareClicked?.Invoke());
                _squareBound = true;
            }
        }

        private void ApplySelectedVisuals()
        {
            ApplySelectedVisualForButton(petardButton, _selectedAbility == AbilityType.Petard);
            ApplySelectedVisualForButton(crossButton,  _selectedAbility == AbilityType.Cross);
            ApplySelectedVisualForButton(squareButton, _selectedAbility == AbilityType.Square);
        }

        private void ApplySelectedVisualForButton(Button button, bool selected)
        {
            if (button == null || button.targetGraphic == null) return;
            var img = button.targetGraphic as Image;
            if (img == null) return;

            if (!_baseButtonColors.TryGetValue(button, out var baseColor))
                baseColor = img.color;

            img.color = selected
                ? Color.Lerp(baseColor, Color.white, 0.35f)
                : baseColor;
        }

        private static void ApplyCooldownVisuals(Button button, bool cooldownActive)
        {
            if (button == null) return;

            var outline = button.GetComponent<Outline>();
            if (outline == null) outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = cooldownActive ? new Color(1f, 0.18f, 0.18f, 0.95f) : new Color(0f, 0f, 0f, 0f);
            outline.effectDistance = new Vector2(2f, -2f);

            var icon = button.transform.Find("AbilityIcon");
            if (icon == null) return;
            var iconImage = icon.GetComponent<Image>();
            if (iconImage == null) return;
            iconImage.color = cooldownActive ? new Color(0.45f, 0.45f, 0.45f, 1f) : Color.white;
        }
    }
}
