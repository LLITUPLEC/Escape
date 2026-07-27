using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Обрабатывает drop между слотами и вызывает RPC перемещения.</summary>
    public sealed class CharacterSheetDragController : MonoBehaviour
    {
        private CharacterScreenView _view;
        private ItemCatalog _catalog;
        private CancellationTokenSource _cts;
        private CharacterDragSlotHandle _activeDragSource;
        private bool _dropConsumed;

        private Canvas _dragCanvas;
        private RectTransform _dragGhostRoot;
        private Image _dragGhostImage;

        public event System.Action<CharacterGetRpcResponse> ProfileUpdated;
        public event System.Action<CharacterDragSlotHandle> SlotClicked;

        public void Initialize(CharacterScreenView view, ItemCatalog catalog, CancellationTokenSource cts)
        {
            _view = view;
            _catalog = catalog;
            _cts = cts;
        }

        public bool CanBeginInteraction(CharacterDragSlotHandle handle)
        {
            if (_view == null || handle == null) return false;
            if (handle.Kind == CharacterDragSlotKind.Inventory) return _view.HasInventoryItem(handle.InventoryIndex);
            return _view.HasEquipmentItem(handle.EquipmentSlot);
        }

        public void OnSlotBeginDrag(CharacterDragSlotHandle handle, PointerEventData eventData)
        {
            if (!CanBeginInteraction(handle)) return;
            _activeDragSource = handle;
            _dropConsumed = false;
            CreateOrUpdateDragGhost(handle, eventData);
        }

        public void OnSlotDrag(PointerEventData eventData)
        {
            if (_dragGhostRoot == null || eventData == null) return;
            _dragGhostRoot.position = eventData.position;
        }

        public void OnSlotEndDrag(CharacterDragSlotHandle handle, PointerEventData eventData)
        {
            if (handle != _activeDragSource)
            {
                _activeDragSource = null;
                _dropConsumed = false;
                DestroyDragGhost();
                return;
            }

            if (!_dropConsumed && handle.Kind == CharacterDragSlotKind.Inventory && eventData != null && _view != null)
            {
                var equipRoot = _view.GetEquipmentRootRectTransform();
                if (equipRoot != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(equipRoot, eventData.position, eventData.pressEventCamera) &&
                    _view.TryGetInventoryItem(handle.InventoryIndex, out _, out var itemDef) &&
                    itemDef != null &&
                    itemDef.Equippable)
                {
                    _ = TryEquipFromInventoryAsync(handle.InventoryIndex, itemDef.Slot);
                }
            }

            _activeDragSource = null;
            _dropConsumed = false;
            DestroyDragGhost();
        }

        public void TryMoveFromDrop(CharacterDragSlotHandle a, CharacterDragSlotHandle b)
        {
            _dropConsumed = true;
            _ = TryMoveFromDropAsync(a, b);
        }

        public void NotifySlotClick(CharacterDragSlotHandle handle)
        {
            if (!CanBeginInteraction(handle)) return;
            SlotClicked?.Invoke(handle);
        }

        public void TryEquipFromInventory(int inventoryIndex)
        {
            if (_view == null) return;
            if (!_view.TryGetInventoryItem(inventoryIndex, out _, out var itemDef) || itemDef == null || !itemDef.Equippable) return;
            _ = TryEquipFromInventoryAsync(inventoryIndex, itemDef.Slot);
        }

        public void TryUnequipToInventory(EquipmentSlotId slotId, int inventoryIndex)
        {
            _ = TryUnequipToInventoryAsync(slotId, inventoryIndex);
        }

        public void TryUnequipToFirstFree(EquipmentSlotId slotId)
        {
            if (_view == null) return;
            var index = _view.FindFirstEmptyInventoryIndex();
            if (index < 0) return;
            _ = TryUnequipToInventoryAsync(slotId, index);
        }

        private async Task TryMoveFromDropAsync(CharacterDragSlotHandle a, CharacterDragSlotHandle b)
        {
            if (_view == null || a == null || b == null || a == b) return;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;

            if (!IsDropAllowed(a, b))
                return;

            Task<CharacterGetRpcResponse> t;
            if (a.Kind == CharacterDragSlotKind.Inventory && b.Kind == CharacterDragSlotKind.Equipment)
                t = CharacterProfileService.MoveInvToEquipAsync(a.InventoryIndex, (int)b.EquipmentSlot, ct);
            else if (a.Kind == CharacterDragSlotKind.Equipment && b.Kind == CharacterDragSlotKind.Inventory)
                t = CharacterProfileService.MoveEquipToInvAsync((int)a.EquipmentSlot, b.InventoryIndex, ct);
            else if (a.Kind == CharacterDragSlotKind.Inventory && b.Kind == CharacterDragSlotKind.Inventory)
                t = CharacterProfileService.SwapInventoryAsync(a.InventoryIndex, b.InventoryIndex, ct);
            else if (a.Kind == CharacterDragSlotKind.Equipment && b.Kind == CharacterDragSlotKind.Equipment)
                t = CharacterProfileService.SwapEquipmentAsync((int)a.EquipmentSlot, (int)b.EquipmentSlot, ct);
            else
                return;

            var resp = await t.ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (resp != null && resp.ok)
                {
                    _view.ApplyCharacterResponse(resp, _catalog);
                    ProfileUpdated?.Invoke(resp);
                }
            });
        }

        /// <summary>
        /// Инв→экип / экип↔экип только в свой слот предмета.
        /// Инв↔инв всегда ок (сервер сам стакает одинаковые).
        /// </summary>
        private bool IsDropAllowed(CharacterDragSlotHandle source, CharacterDragSlotHandle target)
        {
            if (source.Kind == CharacterDragSlotKind.Inventory && target.Kind == CharacterDragSlotKind.Equipment)
            {
                if (!_view.TryGetInventoryItem(source.InventoryIndex, out _, out var invDef) || invDef == null)
                    return false;
                return invDef.Equippable && invDef.Slot == target.EquipmentSlot;
            }

            if (source.Kind == CharacterDragSlotKind.Equipment && target.Kind == CharacterDragSlotKind.Equipment)
            {
                if (source.EquipmentSlot == target.EquipmentSlot)
                    return false;

                ItemDefinition srcDef = null;
                ItemDefinition dstDef = null;
                var hasSrc = _view.TryGetEquipmentItem(source.EquipmentSlot, out _, out srcDef);
                var hasDst = _view.TryGetEquipmentItem(target.EquipmentSlot, out _, out dstDef);

                if (hasSrc && (srcDef == null || srcDef.Slot != target.EquipmentSlot))
                    return false;
                if (hasDst && (dstDef == null || dstDef.Slot != source.EquipmentSlot))
                    return false;
                return hasSrc || hasDst;
            }

            if (source.Kind == CharacterDragSlotKind.Equipment && target.Kind == CharacterDragSlotKind.Inventory)
                return true;

            if (source.Kind == CharacterDragSlotKind.Inventory && target.Kind == CharacterDragSlotKind.Inventory)
                return source.InventoryIndex != target.InventoryIndex;

            return false;
        }

        private async Task TryEquipFromInventoryAsync(int inventoryIndex, EquipmentSlotId slotId)
        {
            if (_view == null) return;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            var resp = await CharacterProfileService.MoveInvToEquipAsync(inventoryIndex, (int)slotId, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (resp != null && resp.ok)
                {
                    _view.ApplyCharacterResponse(resp, _catalog);
                    ProfileUpdated?.Invoke(resp);
                }
            });
        }

        private async Task TryUnequipToInventoryAsync(EquipmentSlotId slotId, int inventoryIndex)
        {
            if (_view == null || inventoryIndex < 0) return;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            var resp = await CharacterProfileService.MoveEquipToInvAsync((int)slotId, inventoryIndex, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (resp != null && resp.ok)
                {
                    _view.ApplyCharacterResponse(resp, _catalog);
                    ProfileUpdated?.Invoke(resp);
                }
            });
        }

        private void CreateOrUpdateDragGhost(CharacterDragSlotHandle handle, PointerEventData eventData)
        {
            if (_view == null || handle == null || eventData == null) return;

            ItemDefinition itemDef = null;
            if (handle.Kind == CharacterDragSlotKind.Inventory)
                _view.TryGetInventoryItem(handle.InventoryIndex, out _, out itemDef);
            else
                _view.TryGetEquipmentItem(handle.EquipmentSlot, out _, out itemDef);

            if (itemDef == null || itemDef.Icon == null) return;

            EnsureDragCanvas();
            if (_dragCanvas == null) return;

            if (_dragGhostRoot == null)
            {
                var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                _dragGhostRoot = ghostGo.GetComponent<RectTransform>();
                _dragGhostRoot.SetParent(_dragCanvas.transform, false);
                _dragGhostRoot.sizeDelta = new Vector2(88f, 88f);
                _dragGhostImage = ghostGo.GetComponent<Image>();
                _dragGhostImage.raycastTarget = false;
                var cg = ghostGo.GetComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.alpha = 0.92f;
            }

            _dragGhostImage.sprite = itemDef.Icon;
            _dragGhostImage.enabled = true;
            _dragGhostRoot.position = eventData.position;
            _dragGhostRoot.gameObject.SetActive(true);
        }

        private void EnsureDragCanvas()
        {
            if (_dragCanvas != null) return;

            var existing = GetComponentInParent<Canvas>();
            if (existing != null)
            {
                _dragCanvas = existing;
                return;
            }

            var canvasGo = new GameObject("CharacterDragCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _dragCanvas = canvasGo.GetComponent<Canvas>();
            _dragCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _dragCanvas.sortingOrder = short.MaxValue;
        }

        private void DestroyDragGhost()
        {
            if (_dragGhostRoot != null) _dragGhostRoot.gameObject.SetActive(false);
        }

        private static Task RunOnMainThreadAsync(System.Action a)
        {
            try
            {
                return MainThreadDispatcher.RunAsync(() =>
                {
                    a?.Invoke();
                    return true;
                });
            }
            catch
            {
                a?.Invoke();
                return Task.CompletedTask;
            }
        }
    }
}
