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

        public void SetBackground(Sprite sprite)
        {
            if (background == null) return;
            background.sprite = sprite;
            background.enabled = sprite != null;
        }
    }
}

