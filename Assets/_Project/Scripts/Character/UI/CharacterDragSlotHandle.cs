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

        public CharacterDragSlotKind Kind => _kind;
        public int InventoryIndex => _invIndex;
        public EquipmentSlotId EquipmentSlot => _eqSlot;

        public void Configure(CharacterSheetDragController controller, CharacterDragSlotKind kind, int invIndex, EquipmentSlotId eqSlot)
        {
            _controller = controller;
            _kind = kind;
            _invIndex = invIndex;
            _eqSlot = eqSlot;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
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
            if (!_dragStarted) return;
            _controller?.OnSlotDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragStarted) return;
            _dragStarted = false;
            _controller?.OnSlotEndDrag(this, eventData);
        }

        public void OnDrop(PointerEventData eventData)
        {
            var src = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CharacterDragSlotHandle>() : null;
            if (src == null || _controller == null || src == this) return;
            _controller.TryMoveFromDrop(src, this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_controller == null || !_controller.CanBeginInteraction(this)) return;
            _controller.NotifySlotClick(this);
        }
    }
}
