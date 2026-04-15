using System;
using System.Collections.Generic;
using TMPro;
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

        private static readonly Color AbilityDimmedIcon = new Color(0.42f, 0.42f, 0.42f, 1f);
        //private static readonly Color AbilityCostTextColor = new Color(34f / 255f, 88f / 255f, 207f / 255f, 1f);
        private static readonly Color AbilityCostTextColor = new Color(0f, 0f, 0f, 0.98f);
        private static readonly Color CooldownCenterTextColor = new Color(1f, 86f / 255f, 0f, 1f);

        [Header("Petard ability")]
        [SerializeField] public Button petardButton;
        [SerializeField] public TMP_Text petardCooldownText;

        [Header("Cross ability")]
        [SerializeField] public Button crossButton;
        [SerializeField] public TMP_Text crossCooldownText;

        [Header("Square ability")]
        [SerializeField] public Button squareButton;
        [SerializeField] public TMP_Text squareCooldownText;

        [Header("Shield ability")]
        [SerializeField] public Button shieldButton;
        [SerializeField] public TMP_Text shieldCooldownText;

        [Header("Fury ability")]
        [SerializeField] public Button furyButton;
        [SerializeField] public TMP_Text furyCooldownText;

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

        private AbilityType? _selectedAbility;
        private bool _petardBound;
        private bool _crossBound;
        private bool _squareBound;
        private bool _shieldBound;
        private bool _furyBound;

        private void Awake()
        {
            BindButtonListeners();

            // UI requirement: abilities use icons; old CD labels stay hidden.
            if (petardCooldownText != null) petardCooldownText.gameObject.SetActive(false);
            if (crossCooldownText != null) crossCooldownText.gameObject.SetActive(false);
            if (squareCooldownText != null) squareCooldownText.gameObject.SetActive(false);
            if (shieldCooldownText != null) shieldCooldownText.gameObject.SetActive(false);
            if (furyCooldownText != null) furyCooldownText.gameObject.SetActive(false);
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

            SetCostLabel(petardButton, petardCost);
            SetCostLabel(crossButton, crossCost);
            SetCostLabel(squareButton, squareCost);
            SetCostLabel(shieldButton, shieldCost);
            SetCostLabel(furyButton, furyCost);

            bool petardDimmed = !active || petardCooldown || !petardHasMana;
            bool crossDimmed = !active || crossCooldown || !crossHasMana;
            bool squareDimmed = !active || squareCooldown || !squareHasMana;
            bool shieldDimmed = !active || shieldCooldown || !shieldHasMana;
            bool furyDimmed = !active || furyCooldown || !furyHasMana;

            StripLegacyOutline(petardButton);
            StripLegacyOutline(crossButton);
            StripLegacyOutline(squareButton);
            StripLegacyOutline(shieldButton);
            StripLegacyOutline(furyButton);

            ApplyAbilityIconVisual(petardButton, _selectedAbility == AbilityType.Petard, petardDimmed);
            ApplyAbilityIconVisual(crossButton, _selectedAbility == AbilityType.Cross, crossDimmed);
            ApplyAbilityIconVisual(squareButton, _selectedAbility == AbilityType.Square, squareDimmed);
            ApplyAbilityIconVisual(shieldButton, _selectedAbility == AbilityType.Shield, shieldDimmed);
            ApplyAbilityIconVisual(furyButton, _selectedAbility == AbilityType.Fury, furyDimmed);

            ApplyCooldownOverlayRow(crossButton, crossCooldown, stats.crossCooldown);
            ApplyCooldownOverlayRow(squareButton, squareCooldown, stats.squareCooldown);
            ApplyCooldownOverlayRow(furyButton, furyCooldown, stats.furyCooldown);

            ApplyAdaptiveButtonLayout();
        }

        public void ShowHint(bool show)
        {
            if (abilityHint != null) abilityHint.SetActive(show);
        }

        public void SetSelectedAbility(AbilityType? ability)
        {
            _selectedAbility = ability;
        }

        private static void StripLegacyOutline(Button button)
        {
            if (button == null) return;
            var outline = button.GetComponent<Outline>();
            if (outline != null)
                Destroy(outline);
        }

        private static Image GetAbilityIconImage(Button button)
        {
            if (button == null) return null;
            var t = button.transform.Find("AbilityIcon");
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static void ApplyAbilityIconVisual(Button button, bool selected, bool dimmed)
        {
            var img = GetAbilityIconImage(button);
            if (img == null) return;

            var baseCol = dimmed ? AbilityDimmedIcon : Color.white;
            img.color = selected ? Color.Lerp(baseCol, Color.white, 0.38f) : baseCol;
        }

        private static void SetCostLabel(Button button, int cost)
        {
            if (button == null) return;
            var tr = button.transform.Find("AbilityCost");
            if (tr == null) return;
            var tmp = tr.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = cost > 0 ? cost.ToString() : string.Empty;
                tmp.color = AbilityCostTextColor;
            }
        }

        /// <summary>Крест / квадрат / ярость: слой блокировки и число КД (см. DuelMatch3Manager.cdBlockSprite).</summary>
        private static void ApplyCooldownOverlayRow(Button button, bool onCooldown, int cooldownTurns)
        {
            if (button == null) return;
            var blockTf = button.transform.Find("CooldownBlock");
            var txtTf = button.transform.Find("CooldownCenterText");
            var txt = txtTf != null ? txtTf.GetComponent<TMP_Text>() : null;

            if (blockTf != null)
            {
                var img = blockTf.GetComponent<Image>();
                var hasBlock = img != null && img.sprite != null;
                var show = onCooldown && hasBlock;
                if (img != null)
                    img.enabled = show;
                blockTf.gameObject.SetActive(show);
            }

            if (txt != null)
            {
                txt.color = CooldownCenterTextColor;
                txt.gameObject.SetActive(onCooldown);
                txt.text = onCooldown ? Mathf.Max(0, cooldownTurns).ToString() : string.Empty;
            }
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
