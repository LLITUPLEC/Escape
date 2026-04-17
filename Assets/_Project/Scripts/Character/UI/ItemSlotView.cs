using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class ItemSlotView : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text stackLabel;

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>Показывает число при стеке &gt; 1. При отсутствии stackLabel — без эффекта.</summary>
        public void SetStackCount(int amount)
        {
            if (stackLabel == null) return;
            if (amount <= 1)
            {
                stackLabel.gameObject.SetActive(false);
                return;
            }

            stackLabel.gameObject.SetActive(true);
            stackLabel.text = amount > 999 ? "999+" : amount.ToString();
        }

        /// <summary>Для drag/drop: чтобы события шли на фон ячейки, а не на иконку.</summary>
        public void SetIconRaycast(bool raycast)
        {
            if (icon != null) icon.raycastTarget = raycast;
        }

        public void SetBackground(Sprite sprite)
        {
            if (background == null) return;
            background.sprite = sprite;
            background.enabled = sprite != null;
        }
    }
}

