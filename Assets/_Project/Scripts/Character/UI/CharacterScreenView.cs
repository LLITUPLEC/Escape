using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Project.Character.UI
{
    public sealed class CharacterScreenView : MonoBehaviour
    {
        [Header("Roots")]
        [SerializeField] private CanvasGroup root;

        [Header("Stats")]
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text damageText;
        [SerializeField] private TMP_Text armorText;
        [SerializeField] private TMP_Text healText;
        [SerializeField] private TMP_Text critText;

        [Header("Equipment")]
        [SerializeField] private Transform equipmentRoot;
        [SerializeField] private EquipmentSlotView equipmentSlotPrefab;

        [Header("Inventory (bag)")]
        [SerializeField] private Transform inventoryRoot;
        [SerializeField] private ItemSlotView inventorySlotPrefab;
        [SerializeField, Min(1)] private int inventorySize = 25;

        private readonly List<EquipmentSlotView> _equipmentSlots = new();
        private readonly List<ItemSlotView> _inventorySlots = new();

        public void SetVisible(bool visible)
        {
            if (root == null)
            {
                gameObject.SetActive(visible);
                return;
            }
            root.alpha = visible ? 1f : 0f;
            root.interactable = visible;
            root.blocksRaycasts = visible;
        }

        public void EnsureBuilt()
        {
            if (_equipmentSlots.Count == 0) CollectOrBuildEquipment();
            if (_inventorySlots.Count == 0) CollectOrBuildInventory();
        }

        public void SetStats(int hp, int damage, int armor, int healing, float critChance01)
        {
            if (hpText != null) hpText.text = hp.ToString();
            if (damageText != null) damageText.text = damage.ToString();
            if (armorText != null) armorText.text = armor.ToString();
            if (healText != null) healText.text = healing.ToString();
            if (critText != null) critText.text = Mathf.RoundToInt(Mathf.Clamp01(critChance01) * 100f) + "%";
        }

        private void CollectOrBuildEquipment()
        {
            if (equipmentRoot == null) return;

            // Prefer pre-placed slots inside prefab (so you can fully control visuals/layout).
            _equipmentSlots.AddRange(equipmentRoot.GetComponentsInChildren<EquipmentSlotView>(true));
            if (_equipmentSlots.Count > 0) return;

            // Fallback: auto-build if no slots found.
            if (equipmentSlotPrefab == null) return;
            foreach (EquipmentSlotId slotId in System.Enum.GetValues(typeof(EquipmentSlotId)))
            {
                var inst = Instantiate(equipmentSlotPrefab, equipmentRoot, false);
                inst.name = "Slot_" + slotId;
                inst.Init(slotId);
                _equipmentSlots.Add(inst);
            }
        }

        private void CollectOrBuildInventory()
        {
            if (inventoryRoot == null) return;

            // Prefer cells already placed in the prefab (otherwise we duplicate 25+25).
            // Only direct children: avoids nested ItemSlotView from other UI and keeps order stable.
            for (var i = 0; i < inventoryRoot.childCount; i++)
            {
                var slot = inventoryRoot.GetChild(i).GetComponent<ItemSlotView>();
                if (slot != null) _inventorySlots.Add(slot);
            }

            if (_inventorySlots.Count > 0) return;

            if (inventorySlotPrefab == null) return;
            var count = Mathf.Max(1, inventorySize);
            for (var i = 0; i < count; i++)
            {
                var inst = Instantiate(inventorySlotPrefab, inventoryRoot, false);
                inst.name = "Cell_" + i;
                inst.SetIcon(null);
                _inventorySlots.Add(inst);
            }
        }
    }
}

