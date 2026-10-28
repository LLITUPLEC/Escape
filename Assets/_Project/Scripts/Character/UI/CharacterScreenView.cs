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

        public const int InventoryPageSize = 25;
        public const int InventoryMaximum = 50;
        public int InventoryCapacity { get; private set; } = InventoryPageSize;
        public int InventorySlotPrice { get; private set; } = 3;
        public long PendingLootCount { get; private set; }
        public event System.Action<int> InventorySlotUnlockRequested;
        private bool _dragConfigured;
        private CharacterExpeditionLayout _expeditionLayout;
        private Canvas _screenCanvas;
        private bool _shellAttached;
        private bool _targetVisible;
        private bool _hasBeenVisible;
        private bool _transitioning;
        private RectTransform _transitionPanel;

        internal CharacterExpeditionLayout ApplyExpeditionLayout(TMP_Text level, Button close, TMP_Text nickname, Button editNickname)
        {
            EnsureBuilt();
            _expeditionLayout = Project.UI.ExpeditionUi.Ensure<CharacterExpeditionLayout>(gameObject);
            _expeditionLayout.Build(GetPanelRootRectTransform(), equipmentRoot as RectTransform, inventoryRoot as RectTransform,
                _equipmentSlots, _inventorySlots, new[] { hpText, damageText, armorText, healText, critText },
                level, close, nickname, editNickname, profileLoadErrorText);
            return _expeditionLayout;
        }

        public void SetVisible(bool visible)
        {
            _transitionPanel = GetPanelRootRectTransform();
            if (root == null) root = Project.UI.ExpeditionUi.Ensure<CanvasGroup>(gameObject);
            if (visible)
            {
                _screenCanvas = Project.UI.ExpeditionUi.Ensure<Canvas>(gameObject);
                _screenCanvas.overrideSorting = !_shellAttached;
                _screenCanvas.sortingOrder = _shellAttached ? 0 : 71;
                _screenCanvas.enabled = true;
                Project.UI.ExpeditionUi.Ensure<GraphicRaycaster>(gameObject);
                if (_expeditionLayout != null) _expeditionLayout.RefreshLayout();
                _hasBeenVisible = true;
            }
            else
            {
                CancelInventoryGesture();
            }
            _targetVisible = visible;
            root.interactable = visible;
            root.blocksRaycasts = visible;
            if (!_hasBeenVisible)
            {
                root.alpha = 0;
                _transitioning = false;
                if (_screenCanvas != null) _screenCanvas.enabled = false;
                return;
            }
            _transitioning = true;
        }

        public void AttachToShell(RectTransform pageHost)
        {
            if (pageHost == null) return;
            transform.SetParent(pageHost, false);
            Project.UI.ExpeditionUi.SetRect(transform as RectTransform, 0, 0, 1, 1);
            _shellAttached = true;
            _screenCanvas = GetComponent<Canvas>();
            if (_screenCanvas != null) { _screenCanvas.overrideSorting = false; _screenCanvas.sortingOrder = 0; _screenCanvas.enabled = true; }
            if (_expeditionLayout != null) _expeditionLayout.SetShellLayout(true);
        }

        private void Update()
        {
            if (!_transitioning || root == null) return;
            var target = _targetVisible ? 1f : 0f;
            root.alpha = Mathf.MoveTowards(root.alpha, target, Time.unscaledDeltaTime / .18f);
            if (_transitionPanel != null) _transitionPanel.anchoredPosition = new Vector2(0, (1f - root.alpha) * -12f);
            if (!Mathf.Approximately(root.alpha, target)) return;
            _transitioning = false;
            if (!_targetVisible && _screenCanvas != null) _screenCanvas.enabled = false;
        }

        public void CancelInventoryGesture() { if (_expeditionLayout != null) _expeditionLayout.CancelInventoryGesture(); }

        public void EnsureBuilt()
        {
            if (_equipmentSlots.Count == 0) CollectOrBuildEquipment();
            if (_inventorySlots.Count == 0) CollectOrBuildInventory();
            _expeditionLayout?.RefreshLayout();
        }

        public void SetStats(int hp, int damage, int armor, int healing, float critChance01)
        {
            ClearProfileLoadError();
            if (hpText != null) hpText.text = hp.ToString();
            if (damageText != null) damageText.text = damage.ToString();
            if (armorText != null) armorText.text = armor.ToString();
            if (healText != null) healText.text = healing.ToString();
            if (critText != null)
                critText.text = (Mathf.Clamp01(critChance01) * 100f).ToString("0.00") + "%";
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
            var declaredCapacity = profile.inventory_capacity > 0 ? profile.inventory_capacity : profile.inventory_def_ids?.Length ?? InventoryPageSize;
            InventoryCapacity = Mathf.Clamp(declaredCapacity, InventoryPageSize, InventoryMaximum);
            InventorySlotPrice = profile.inventory_slot_price_matter > 0 ? profile.inventory_slot_price_matter : 3;
            PendingLootCount = 0;
            if (profile.pending_loot != null)
                foreach (var row in profile.pending_loot) if (row != null && row.amount > 0) PendingLootCount += row.amount;
            BindEquipmentAndInventory(profile.equipment_def_ids, profile.inventory_def_ids, profile.inventory_counts, catalog);
            _expeditionLayout?.SetConsumables(profile.consumables);
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
            _expeditionLayout?.RefreshEmptyEquipment(_equipmentDefIds);
            _expeditionLayout?.SetEquipmentSet(EquipmentSetBonus.Evaluate(_equipmentDefIds, catalog));

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
            foreach (var slot in _inventorySlots)
            {
                var handle = slot.GetComponent<CharacterDragSlotHandle>();
                if (handle != null) handle.enabled = slot.InventorySlotIndex < InventoryCapacity;
            }
            _expeditionLayout?.RefreshInventory();
        }

        public void RequestInventorySlotUnlock(int index)
        {
            if (index == InventoryCapacity && index < InventoryMaximum) InventorySlotUnlockRequested?.Invoke(index);
        }

        public void MoveDraggedItemToPage(CharacterDragSlotHandle source, int page)
        {
            if (source == null || page < 0 || page > 1) return;
            if (source.Kind == CharacterDragSlotKind.Inventory && source.InventoryIndex / InventoryPageSize == page) return;
            var start = page * InventoryPageSize;
            var end = Mathf.Min(start + InventoryPageSize, InventoryCapacity);
            var drag = GetComponent<CharacterSheetDragController>();
            if (drag == null) return;
            for (var i = start; i < end; i++)
            {
                if (HasInventoryItem(i)) continue;
                drag.TryMoveFromDrop(source, _inventorySlots[i].GetComponent<CharacterDragSlotHandle>());
                return;
            }
        }

        public bool TryGetInventoryItem(int inventoryIndex, out string itemId, out ItemDefinition itemDef)
        {
            itemId = null;
            itemDef = null;
            if (inventoryIndex < 0 || inventoryIndex >= InventoryCapacity || inventoryIndex >= _inventoryDefIds.Length) return false;
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
            if (inventoryIndex < 0 || inventoryIndex >= InventoryCapacity || inventoryIndex >= _inventoryDefIds.Length) return false;
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
            for (var i = 0; i < InventoryCapacity; i++)
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
                var h = Project.UI.ExpeditionUi.Ensure<CharacterDragSlotHandle>(root);
                var serverIdx = slot.InventorySlotIndex >= 0 ? slot.InventorySlotIndex : i;
                h.Configure(drag, CharacterDragSlotKind.Inventory, serverIdx, default);
            }

            foreach (var eq in _equipmentSlots)
            {
                eq.SetItemIconRaycast(false);
                var btn = eq.GetComponentInChildren<UnityEngine.UI.Button>(true);
                var target = btn != null ? btn.gameObject : eq.gameObject;
                EnsureRaycastTarget(target);
                var h = Project.UI.ExpeditionUi.Ensure<CharacterDragSlotHandle>(target);
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
            inventorySize = InventoryMaximum;
            if (inventoryRoot == null) return;

            _inventorySlots.Clear();
            _inventorySlots.AddRange(inventoryRoot.GetComponentsInChildren<ItemSlotView>(true));
            SortInventorySlots(_inventorySlots);
            if (_inventorySlots.Count > inventorySize)
                _inventorySlots.RemoveRange(inventorySize, _inventorySlots.Count - inventorySize);

            var template = inventorySlotPrefab != null ? inventorySlotPrefab : _inventorySlots.Count > 0 ? _inventorySlots[0] : null;
            if (template == null) return;
            for (var i = _inventorySlots.Count; i < inventorySize; i++)
            {
                var inst = Instantiate(template, inventoryRoot, false);
                inst.name = "Cell_" + i;
                inst.SetInventorySlotIndex(i);
                inst.SetIcon(null); inst.SetStackCount(0);
                _inventorySlots.Add(inst);
            }
            for (var i = 0; i < _inventorySlots.Count; i++) _inventorySlots[i].SetInventorySlotIndex(i);
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

