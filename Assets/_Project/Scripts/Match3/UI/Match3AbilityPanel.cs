using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Ability buttons panel.
    /// </summary>
    public sealed class Match3AbilityPanel : MonoBehaviour
    {
        private const float AbilitySpacingPx = 3f;
        private const float AbilityPaddingPx = 6f;
        private const float AbilityVerticalRatio = 0.72f;

        [Header("Petard ability")]
        [SerializeField] public Button petardButton;
        [SerializeField] public Text   petardCooldownText;

        [Header("Cross ability")]
        [SerializeField] public Button crossButton;
        [SerializeField] public Text   crossCooldownText;

        [Header("Square ability")]
        [SerializeField] public Button squareButton;
        [SerializeField] public Text   squareCooldownText;

        [Header("Shield ability")]
        [SerializeField] public Button shieldButton;
        [SerializeField] public Text   shieldCooldownText;

        [Header("Fury ability")]
        [SerializeField] public Button furyButton;
        [SerializeField] public Text   furyCooldownText;

        [Header("Hint bar (shown while waiting for cell click)")]
        [SerializeField] public GameObject abilityHint;

        /// <summary>Fired when Cross button is clicked.</summary>
        public event Action OnCrossClicked;
        /// <summary>Fired when Square button is clicked.</summary>
        public event Action OnSquareClicked;
        /// <summary>Fired when Petard button is clicked.</summary>
        public event Action OnPetardClicked;
        /// <summary>Fired when Shield button is clicked.</summary>
        public event Action OnShieldClicked;
        /// <summary>Fired when Fury button is clicked.</summary>
        public event Action OnFuryClicked;

        private readonly Dictionary<Button, Color> _baseButtonColors = new Dictionary<Button, Color>();
        private AbilityType? _selectedAbility;
        private bool _petardBound;
        private bool _crossBound;
        private bool _squareBound;
        private bool _shieldBound;
        private bool _furyBound;

        private void Awake()
        {
            BindButtonListeners();
            CacheButtonColor(petardButton);
            CacheButtonColor(crossButton);
            CacheButtonColor(squareButton);
            CacheButtonColor(shieldButton);
            CacheButtonColor(furyButton);

            // UI requirement: abilities should use only icon sprites, no text.
            if (petardCooldownText != null) petardCooldownText.text = string.Empty;
            if (crossCooldownText != null) crossCooldownText.text = string.Empty;
            if (squareCooldownText != null) squareCooldownText.text = string.Empty;
            if (shieldCooldownText != null) shieldCooldownText.text = string.Empty;
            if (furyCooldownText != null) furyCooldownText.text = string.Empty;
            ApplySelectedVisuals();
            ApplyAdaptiveButtonLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyAdaptiveButtonLayout();
        }

        // ─── API ──────────────────────────────────────────────────────────────────

        public void Refresh(
            PlayerStats stats,
            bool isMyTurn,
            bool gameEnded,
            int crossCost,
            int squareCost,
            int petardCost,
            int shieldCost,
            int furyCost)
        {
            BindButtonListeners();
            CacheButtonColor(petardButton);
            CacheButtonColor(crossButton);
            CacheButtonColor(squareButton);
            CacheButtonColor(shieldButton);
            CacheButtonColor(furyButton);
            bool active = isMyTurn && !gameEnded;
            bool petardHasMana = stats.mana >= petardCost;
            bool crossHasMana = stats.mana >= crossCost;
            bool squareHasMana = stats.mana >= squareCost;
            bool shieldHasMana = stats.mana >= shieldCost;
            bool furyHasMana = stats.mana >= furyCost;
            bool petardCooldown = stats.petardCooldown > 0;
            bool crossCooldown = stats.crossCooldown > 0;
            bool squareCooldown = stats.squareCooldown > 0;
            bool shieldCooldown = stats.shieldCooldown > 0;
            bool furyCooldown = stats.furyCooldown > 0;

            bool petardSelectable = active && !petardCooldown && petardHasMana;
            bool crossSelectable = active && !crossCooldown && crossHasMana;
            bool squareSelectable = active && !squareCooldown && squareHasMana;
            bool shieldSelectable = active && !shieldCooldown && shieldHasMana;
            bool furySelectable = active && !furyCooldown && furyHasMana;

            if (petardButton != null) petardButton.interactable = petardSelectable;
            if (crossButton != null) crossButton.interactable = crossSelectable || _selectedAbility == AbilityType.Cross;
            if (squareButton != null) squareButton.interactable = squareSelectable || _selectedAbility == AbilityType.Square;
            if (shieldButton != null) shieldButton.interactable = shieldSelectable;
            if (furyButton != null) furyButton.interactable = furySelectable;

            if (petardCooldownText != null) petardCooldownText.text = string.Empty;
            if (crossCooldownText != null) crossCooldownText.text = string.Empty;
            if (squareCooldownText != null) squareCooldownText.text = string.Empty;
            if (shieldCooldownText != null) shieldCooldownText.text = string.Empty;
            if (furyCooldownText != null) furyCooldownText.text = string.Empty;

            ApplyCooldownVisuals(petardButton, petardCooldown);
            ApplyCooldownVisuals(crossButton, crossCooldown);
            ApplyCooldownVisuals(squareButton, squareCooldown);
            ApplyCooldownVisuals(shieldButton, shieldCooldown);
            ApplyCooldownVisuals(furyButton, furyCooldown);
            ApplySelectedVisuals();
            ApplyAdaptiveButtonLayout();
        }

        public void ShowHint(bool show)
        {
            if (abilityHint != null) abilityHint.SetActive(show);
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
            if (!_shieldBound && shieldButton != null)
            {
                shieldButton.onClick.AddListener(() => OnShieldClicked?.Invoke());
                _shieldBound = true;
            }
            if (!_furyBound && furyButton != null)
            {
                furyButton.onClick.AddListener(() => OnFuryClicked?.Invoke());
                _furyBound = true;
            }
        }

        private void ApplySelectedVisuals()
        {
            ApplySelectedVisualForButton(petardButton, _selectedAbility == AbilityType.Petard);
            ApplySelectedVisualForButton(crossButton,  _selectedAbility == AbilityType.Cross);
            ApplySelectedVisualForButton(squareButton, _selectedAbility == AbilityType.Square);
            ApplySelectedVisualForButton(shieldButton, _selectedAbility == AbilityType.Shield);
            ApplySelectedVisualForButton(furyButton, _selectedAbility == AbilityType.Fury);
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

        private void ApplyAdaptiveButtonLayout()
        {
            var panelRt = transform as RectTransform;
            if (panelRt == null) return;

            var buttons = new List<Button>(5);
            if (petardButton != null) buttons.Add(petardButton);
            if (crossButton != null) buttons.Add(crossButton);
            if (squareButton != null) buttons.Add(squareButton);
            if (shieldButton != null) buttons.Add(shieldButton);
            if (furyButton != null) buttons.Add(furyButton);
            if (buttons.Count == 0) return;

            var panelWidth = panelRt.rect.width;
            var panelHeight = panelRt.rect.height;
            if (panelWidth <= 1f || panelHeight <= 1f) return;

            var sideByWidth = (panelWidth - AbilityPaddingPx * 2f - AbilitySpacingPx * (buttons.Count - 1)) / buttons.Count;
            var sideByHeight = panelHeight * AbilityVerticalRatio;
            var side = Mathf.Max(22f, Mathf.Min(sideByWidth, sideByHeight));
            var rowWidth = buttons.Count * side + (buttons.Count - 1) * AbilitySpacingPx;
            var startX = -rowWidth * 0.5f + side * 0.5f;
            var y = 0f;

            for (var i = 0; i < buttons.Count; i++)
            {
                var rt = buttons[i].transform as RectTransform;
                if (rt == null) continue;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(side, side);
                rt.anchoredPosition = new Vector2(startX + i * (side + AbilitySpacingPx), y);
            }
        }
    }
}
