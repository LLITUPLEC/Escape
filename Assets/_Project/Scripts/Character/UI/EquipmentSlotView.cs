using Project.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class EquipmentSlotView : MonoBehaviour
    {
        [SerializeField] private EquipmentSlotId slotId;
        [SerializeField] private Button button;
        [SerializeField] private ItemSlotView itemView;

        public EquipmentSlotId SlotId => slotId;

        public void Init(EquipmentSlotId id)
        {
            slotId = id;
        }

        public void Set(ItemDefinition item)
        {
            if (itemView != null) itemView.SetIcon(item != null ? item.Icon : null);
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null) button.interactable = interactable;
        }
    }
}

