using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class ItemSlotView : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image background;
        [SerializeField] private Image icon;

        public void SetIcon(Sprite sprite)
        {
            if (icon == null) return;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
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

