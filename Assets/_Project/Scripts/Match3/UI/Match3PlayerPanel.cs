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

        // ─── API ──────────────────────────────────────────────────────────────────

        public void SetPlayerName(string playerName)
        {
            if (nameText != null) nameText.text = playerName;
        }

        public void UpdateStats(int hp, int maxHp, int mana, int maxMana)
        {
            if (hpFill   != null) hpFill.fillAmount   = maxHp   > 0 ? (float)hp   / maxHp   : 0f;
            if (manaFill != null) manaFill.fillAmount  = maxMana > 0 ? (float)mana / maxMana : 0f;
            if (hpText   != null) hpText.text   = $"{hp}/{maxHp}";
            if (manaText != null) manaText.text  = $"{mana}/{maxMana}";
        }
    }
}
