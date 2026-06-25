using System.Collections.Generic;
using System.Text.RegularExpressions;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class CharacterScreenView : MonoBehaviour
    {
        [Header("Roots")]
        [SerializeField] private CanvasGroup root;
        [SerializeField] private RectTransform panelRoot;

        [Header("Stats")]
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text damageText;
        [SerializeField] private TMP_Text armorText;
        [SerializeField] private TMP_Text healText;
        [SerializeField] private TMP_Text critText;
        [Tooltip("Необязательно: сообщение при ошибке duel_character_get (иначе статы останутся нулями без пояснения).")]
        [SerializeField] private TMP_Text profileLoadErrorText;

        [Header("Equipment")]
        [SerializeField] private Transform equipmentRoot;
        [SerializeField] private EquipmentSlotView equipmentSlotPrefab;

        [Header("Inventory (bag)")]
        [SerializeField] private Transform inventoryRoot;
        [SerializeField] private ItemSlotView inventorySlotPrefab;
        [SerializeField, Min(1)] private int inventorySize = 25;

        private readonly List<EquipmentSlotView> _equipmentSlots = new();
        private readonly List<ItemSlotView> _inventorySlots = new();
        private string[] _equipmentDefIds = new string[8];
        private string[] _inventoryDefIds = new string[25];
        private int[] _inventoryCounts = new int[25];
        private ItemCatalog _activeCatalog;

        private bool _dragConfigured;

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
            ClearProfileLoadError();
            if (hpText != null) hpText.text = hp.ToString();
            if (damageText != null) damageText.text = damage.ToString();
            if (armorText != null) armorText.text = armor.ToString();
            if (healText != null) healText.text = healing.ToString();
            if (critText != null) critText.text = Mathf.RoundToInt(Mathf.Clamp01(critChance01) * 100f) + "%";
        }

        public void SetProfileLoadError(string err)
        {
            if (profileLoadErrorText == null) return;
            var s = string.IsNullOrEmpty(err) ? "" : err;
            profileLoadErrorText.text = string.IsNullOrEmpty(s) ? "" : "Ошибка профиля: " + s;
            profileLoadErrorText.gameObject.SetActive(!string.IsNullOrEmpty(s));
        }

        public void ClearProfileLoadError()
        {
            if (profileLoadErrorText == null) return;
            profileLoadErrorText.text = "";
            profileLoadErrorText.gameObject.SetActive(false);
        }

        public void ApplyCharacterResponse(CharacterGetRpcResponse profile, ItemCatalog catalog)
        {
            if (profile == null || !profile.ok) return;
            ClearProfileLoadError();
            if (profile.stats != null)
                SetStats(profile.stats.hp, profile.stats.damage, profile.stats.armor, profile.stats.healing, profile.stats.crit_chance);
            BindEquipmentAndInventory(profile.equipment_def_ids, profile.inventory_def_ids, profile.inventory_counts, catalog);
        }

        public void BindEquipmentAndInventory(string[] equipmentDefIds, string[] inventoryDefIds, ItemCatalog catalog) =>
            BindEquipmentAndInventory(equipmentDefIds, inventoryDefIds, null, catalog);

        public void BindEquipmentAndInventory(string[] equipmentDefIds, string[] inventoryDefIds, int[] inventoryCounts, ItemCatalog catalog)
        {
            EnsureBuilt();
            _activeCatalog = catalog;
            var bySlot = new EquipmentSlotView[8];
            foreach (var s in _equipmentSlots)
            {
                var idx = (int)s.SlotId;
                if (idx >= 0 && idx < 8) bySlot[idx] = s;
            }

            for (var i = 0; i < 8; i++)
            {
                var id = equipmentDefIds != null && i < equipmentDefIds.Length ? equipmentDefIds[i] : null;
                var def = catalog != null && !string.IsNullOrEmpty(id) ? catalog.Get(id) : null;
                var icon = catalog != null ? catalog.GetDisplayIcon(def) : def?.Icon;
                if (bySlot[i] != null) bySlot[i].Set(def, icon);
                _equipmentDefIds[i] = id;
            }

            if (_inventoryDefIds.Length != inventorySize)
                _inventoryDefIds = new string[inventorySize];
            if (_inventoryCounts.Length != inventorySize)
                _inventoryCounts = new int[inventorySize];

            for (var s = 0; s < inventorySize; s++)
            {
                _inventoryDefIds[s] = "";
                _inventoryCounts[s] = 0;
            }

            for (var i = 0; i < _inventorySlots.Count; i++)
            {
                var slotView = _inventorySlots[i];
                var dataIdx = slotView.InventorySlotIndex >= 0 ? slotView.InventorySlotIndex : i;
                if (dataIdx < 0 || dataIdx >= inventorySize) continue;

                var id = inventoryDefIds != null && dataIdx < inventoryDefIds.Length
                    ? inventoryDefIds[dataIdx]
                    : null;
                var def = catalog != null && !string.IsNullOrEmpty(id) ? catalog.Get(id) : null;
                var icon = catalog != null ? catalog.GetDisplayIcon(def) : def?.Icon;
                slotView.SetIcon(icon);
                _inventoryDefIds[dataIdx] = id ?? "";
                var cnt = 0;
                if (!string.IsNullOrEmpty(id))
                {
                    cnt = inventoryCounts != null && dataIdx < inventoryCounts.Length
                        ? inventoryCounts[dataIdx]
                        : 1;
                    if (cnt < 1) cnt = 1;
                }

                _inventoryCounts[dataIdx] = cnt;
                slotView.SetStackCount(cnt);
            }
        }

        public bool TryGetInventoryItem(int inventoryIndex, out string itemId, out ItemDefinition itemDef)
        {
            itemId = null;
            itemDef = null;
            if (inventoryIndex < 0 || inventoryIndex >= _inventoryDefIds.Length) return false;
            itemId = _inventoryDefIds[inventoryIndex];
            if (string.IsNullOrEmpty(itemId)) return false;
            if (inventoryIndex < _inventoryCounts.Length && _inventoryCounts[inventoryIndex] < 1) return false;
            itemDef = _activeCatalog != null ? _activeCatalog.Get(itemId) : null;
            return itemDef != null;
        }

        public bool TryGetEquipmentItem(EquipmentSlotId slotId, out string itemId, out ItemDefinition itemDef)
        {
            itemId = null;
            itemDef = null;
            var index = (int)slotId;
            if (index < 0 || index >= _equipmentDefIds.Length) return false;
            itemId = _equipmentDefIds[index];
            if (string.IsNullOrEmpty(itemId)) return false;
            itemDef = _activeCatalog != null ? _activeCatalog.Get(itemId) : null;
            return itemDef != null;
        }

        public bool HasInventoryItem(int inventoryIndex)
        {
            if (inventoryIndex < 0 || inventoryIndex >= _inventoryDefIds.Length) return false;
            if (string.IsNullOrEmpty(_inventoryDefIds[inventoryIndex])) return false;
            if (inventoryIndex < _inventoryCounts.Length && _inventoryCounts[inventoryIndex] < 1) return false;
            return true;
        }

        public int GetInventoryStackCount(int inventoryIndex)
        {
            if (inventoryIndex < 0 || inventoryIndex >= _inventoryCounts.Length) return 0;
            return _inventoryCounts[inventoryIndex];
        }

        public bool HasEquipmentItem(EquipmentSlotId slotId)
        {
            var index = (int)slotId;
            return index >= 0 && index < _equipmentDefIds.Length && !string.IsNullOrEmpty(_equipmentDefIds[index]);
        }

        public int FindFirstEmptyInventoryIndex()
        {
            for (var i = 0; i < _inventoryDefIds.Length; i++)
            {
                if (string.IsNullOrEmpty(_inventoryDefIds[i])) return i;
            }

            return -1;
        }

        public RectTransform GetEquipmentRootRectTransform()
        {
            return equipmentRoot as RectTransform;
        }

        public RectTransform GetPanelRootRectTransform()
        {
            if (panelRoot != null) return panelRoot;
            var tr = transform.Find("Panel");
            return tr as RectTransform;
        }

        public void SetupDrag(CharacterSheetDragController drag)
        {
            if (drag == null || _dragConfigured) return;
            EnsureBuilt();

            for (var i = 0; i < _inventorySlots.Count; i++)
            {
                var slot = _inventorySlots[i];
                slot.SetIconRaycast(false);
                var root = slot.gameObject;
                EnsureRaycastTarget(root);
                var h = root.GetComponent<CharacterDragSlotHandle>() ?? root.AddComponent<CharacterDragSlotHandle>();
                var serverIdx = slot.InventorySlotIndex >= 0 ? slot.InventorySlotIndex : i;
                h.Configure(drag, CharacterDragSlotKind.Inventory, serverIdx, default);
            }

            foreach (var eq in _equipmentSlots)
            {
                eq.SetItemIconRaycast(false);
                var btn = eq.GetComponentInChildren<UnityEngine.UI.Button>(true);
                var target = btn != null ? btn.gameObject : eq.gameObject;
                EnsureRaycastTarget(target);
                var h = target.GetComponent<CharacterDragSlotHandle>() ?? target.AddComponent<CharacterDragSlotHandle>();
                h.Configure(drag, CharacterDragSlotKind.Equipment, -1, eq.SlotId);
            }

            _dragConfigured = true;
        }

        private static void EnsureRaycastTarget(GameObject root)
        {
            if (root.GetComponent<Graphic>() == null)
            {
                var img = root.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.001f);
                img.raycastTarget = true;
            }
            else
            {
                foreach (var g in root.GetComponents<Graphic>())
                    g.raycastTarget = true;
            }
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

            _inventorySlots.Clear();
            _inventorySlots.AddRange(inventoryRoot.GetComponentsInChildren<ItemSlotView>(true));
            SortInventorySlots(_inventorySlots);
            if (_inventorySlots.Count > inventorySize)
                _inventorySlots.RemoveRange(inventorySize, _inventorySlots.Count - inventorySize);

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

        private static void SortInventorySlots(List<ItemSlotView> slots)
        {
            if (slots == null || slots.Count <= 1) return;
            var allExplicit = true;
            foreach (var s in slots)
            {
                if (s == null || s.InventorySlotIndex < 0)
                {
                    allExplicit = false;
                    break;
                }
            }

            if (allExplicit)
                slots.Sort((a, b) => a.InventorySlotIndex.CompareTo(b.InventorySlotIndex));
            else
                slots.Sort((a, b) => ParseCellNameIndex(a != null ? a.name : "").CompareTo(ParseCellNameIndex(b != null ? b.name : "")));
        }

        private static int ParseCellNameIndex(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return 999;
            var m = Regex.Match(objectName, @"Cell_(\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 999;
        }
    }
}

