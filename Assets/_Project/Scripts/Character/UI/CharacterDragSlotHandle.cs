using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Character.UI
{
    public enum CharacterDragSlotKind
    {
        Inventory = 0,
        Equipment = 1,
    }

    /// <summary>Точка drag/drop для ячейки инвентаря или слота экипировки.</summary>
    public sealed class CharacterDragSlotHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
    {
        private CharacterSheetDragController _controller;
        private CharacterDragSlotKind _kind;
        private int _invIndex;
        private EquipmentSlotId _eqSlot;
        private bool _dragStarted;
        private CharacterInventoryScrollRelay _inventoryGesture;

        public CharacterDragSlotKind Kind => _kind;
        public int InventoryIndex => _invIndex;
        public EquipmentSlotId EquipmentSlot => _eqSlot;

        public void CancelInteraction()
        {
            _dragStarted = false;
            _inventoryGesture?.CancelGesture();
        }

        private void OnDisable() { CancelInteraction(); }

        public void Configure(CharacterSheetDragController controller, CharacterDragSlotKind kind, int invIndex, EquipmentSlotId eqSlot)
        {
            _controller = controller;
            _kind = kind;
            _invIndex = invIndex;
            _eqSlot = eqSlot;
            _inventoryGesture = kind == CharacterDragSlotKind.Inventory ? GetComponent<CharacterInventoryScrollRelay>() : null;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_inventoryGesture != null && _inventoryGesture.BeginGesture(eventData))
            {
                _dragStarted = false;
                return;
            }
            if (_controller == null || !_controller.CanBeginInteraction(this))
            {
                _dragStarted = false;
                return;
            }

            _dragStarted = true;
            _controller.OnSlotBeginDrag(this, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_inventoryGesture != null && _inventoryGesture.IsScrolling)
            {
                _inventoryGesture.DragScroll(eventData);
                return;
            }
            if (!_dragStarted) return;
            _controller?.OnSlotDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_inventoryGesture != null)
            {
                var wasScrolling = _inventoryGesture.IsScrolling;
                _inventoryGesture.EndGesture(eventData);
                if (wasScrolling) { _dragStarted = false; return; }
            }
            if (!_dragStarted) return;
            _dragStarted = false;
            _controller?.OnSlotEndDrag(this, eventData);
        }

        public void OnDrop(PointerEventData eventData)
        {
            var src = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CharacterDragSlotHandle>() : null;
            if (src == null || !src._dragStarted || _controller == null || src._controller != _controller || src == this) return;
            _controller.TryMoveFromDrop(src, this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragStarted || (_inventoryGesture != null && _inventoryGesture.BlocksClick)) return;
            if (_controller == null || !_controller.CanBeginInteraction(this)) return;
            _controller.NotifySlotClick(this);
        }
    }
}
