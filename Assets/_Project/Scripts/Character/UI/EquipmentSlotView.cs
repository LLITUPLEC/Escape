using Project.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class EquipmentSlotView : MonoBehaviour
    {
        private static readonly Color EmptySlotColor = new Color(0.16f, 0.14f, 0.20f, 0.95f);

        [SerializeField] private EquipmentSlotId slotId;
        [SerializeField] private Button button;
        [SerializeField] private ItemSlotView itemView;
        [SerializeField] private Image slotImage;

        public EquipmentSlotId SlotId => slotId;

        private void Awake()
        {
            if (slotImage == null) slotImage = GetComponent<Image>();
        }

        public void Init(EquipmentSlotId id)
        {
            slotId = id;
        }

        public void Set(ItemDefinition item, Sprite resolvedIcon = null)
        {
            if (itemView == null) return;
            var sprite = resolvedIcon != null ? resolvedIcon : item != null ? item.Icon : null;
            itemView.SetIcon(sprite);
            ApplyQualityColor(item);
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null) button.interactable = interactable;
        }

        public void SetItemIconRaycast(bool raycast)
        {
            if (itemView != null) itemView.SetIconRaycast(raycast);
        }

        private void ApplyQualityColor(ItemDefinition item)
        {
            if (slotImage == null) slotImage = GetComponent<Image>();
            if (slotImage == null) return;
            slotImage.color = item != null ? QualitySlotColor(item.Quality) : EmptySlotColor;
        }

        /// <summary>Цвет рамки слота по качеству экипировки.</summary>
        public static Color QualitySlotColor(ItemQualityTier quality) => quality switch
        {
            ItemQualityTier.Normal => new Color(0.25f, 0.72f, 0.32f, 0.95f),
            ItemQualityTier.Rare => new Color(0.28f, 0.48f, 0.95f, 0.95f),
            ItemQualityTier.Epic => new Color(0.62f, 0.28f, 0.88f, 0.95f),
            ItemQualityTier.Legendary => new Color(0.95f, 0.72f, 0.22f, 0.95f),
            _ => EmptySlotColor,
        };
    }
}

