using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Utils;
using UnityEngine;

namespace Project.Character.UI
{
    /// <summary>Обрабатывает drop между слотами и вызывает RPC перемещения.</summary>
    public sealed class CharacterSheetDragController : MonoBehaviour
    {
        private CharacterScreenView _view;
        private ItemCatalog _catalog;
        private CancellationTokenSource _cts;

        public void Initialize(CharacterScreenView view, ItemCatalog catalog, CancellationTokenSource cts)
        {
            _view = view;
            _catalog = catalog;
            _cts = cts;
        }

        public void OnSlotBeginDrag(CharacterDragSlotHandle _)
        {
        }

        public void OnSlotEndDrag(CharacterDragSlotHandle _)
        {
        }

        public void TryMoveFromDrop(CharacterDragSlotHandle a, CharacterDragSlotHandle b)
        {
            _ = TryMoveFromDropAsync(a, b);
        }

        private async Task TryMoveFromDropAsync(CharacterDragSlotHandle a, CharacterDragSlotHandle b)
        {
            if (_view == null) return;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;

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
                    _view.ApplyCharacterResponse(resp, _catalog);
            });
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
