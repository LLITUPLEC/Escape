using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Represents one player's sidebar: avatar placeholder, name, HP and Mana bars.
    /// Assign all references in the Inspector (or let Match3PrefabCreator do it).
    /// </summary>
    public sealed class Match3PlayerPanel : MonoBehaviour
    {
        [Header("Avatar")]
        [SerializeField] public Image avatarImage;
        [SerializeField] public Text  avatarPlaceholderText;  // shows "?" until real sprite assigned

        [Header("Name")]
        [SerializeField] public Text nameText;

        [Header("HP")]
        [SerializeField] public Image hpFill;   // Image.Type = Filled, Horizontal
        [SerializeField] public Text  hpText;

        [Header("Mana")]
        [SerializeField] public Image manaFill;
        [SerializeField] public Text  manaText;

        [Header("Combat Stats")]
        [SerializeField] public Text combatStatsText;
        [SerializeField] public Text buffStateText;

        [Header("Damage Popup")]
        [SerializeField] public RectTransform damagePopupAnchor;
        [SerializeField] public DamagePopupView damagePopup;

        // ─── API ──────────────────────────────────────────────────────────────────

        public void SetPlayerName(string playerName)
        {
            if (nameText != null) nameText.text = playerName;
        }

        public void UpdateStats(int hp, int maxHp, int mana, int maxMana)
        {
            ApplyBarFill(hpFill, maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f);
            ApplyBarFill(manaFill, maxMana > 0 ? Mathf.Clamp01((float)mana / maxMana) : 0f);

            if (hpText   != null) hpText.text   = $"{hp}/{maxHp}";
            if (manaText != null) manaText.text  = $"{mana}/{maxMana}";
        }

        public void UpdateCombatStats(int damageBonus, int armor, int healBonus, int critChancePercent)
        {
            if (combatStatsText == null) return;
            combatStatsText.text =
                $"Урон:   {Mathf.Max(0, damageBonus)}\n" +
                $"Броня:  {Mathf.Max(0, armor)}\n" +
                $"Хил:     {Mathf.Max(0, healBonus)}\n" +
                $"Крит:   {Mathf.Max(0, critChancePercent)}%";
        }

        public void UpdateBuffState(int shieldStacks, int shieldTurnsRemaining)
        {
            if (buffStateText == null) return;
            buffStateText.text = shieldStacks > 0 ? $"Щит x{shieldStacks} ({Mathf.Max(0, shieldTurnsRemaining)})" : string.Empty;
        }

        public void ShowDamagePopup(int damageAmount, bool isCrit)
        {
            if (damagePopup == null) return;
            damagePopup.Play(damageAmount, isCrit);
        }

        private static void ApplyBarFill(Image fillImage, float ratio)
        {
            if (fillImage == null) return;
            ratio = Mathf.Clamp01(ratio);

            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillClockwise = true;
            fillImage.fillAmount = ratio;

            // Fallback for cases where Filled type behaves inconsistently with no source sprite.
            var rt = fillImage.rectTransform;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(ratio, 1f);
                rt.offsetMin = new Vector2(1f, 1f);
                rt.offsetMax = new Vector2(-1f, -1f);
            }

            var frameRt = fillImage.transform.parent as RectTransform;
            if (frameRt != null)
            {
                var outline = frameRt.GetComponent<Outline>();
                if (outline == null) outline = frameRt.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.85f, 0.85f, 0.9f, 0.45f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
        }
    }
}
