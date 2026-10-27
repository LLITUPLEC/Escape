using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Project.UI;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production inventory binder, local UI data only. Never calls purchase/move/claim/delivery RPCs.</summary>
    public static class ExpeditionInventoryPagesQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static CharacterScreenView _view;
        private static CharacterExpeditionLayout _layout;
        private static CharacterGetRpcResponse _saved;
        private static ItemCatalog _catalog;
        private static CanvasGroup _group;
        private static bool _interactable, _blocks;
        private static int _page, _filter, _frame, _capacity;
        private static string _item;

        static ExpeditionInventoryPagesQa()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreOnExit;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) RestoreOnExit(); };
        }

        /// <summary>Read-only diagnostics; does not open, close, click, invoke or bypass any runtime guard.</summary>
        public static string Diagnose()
        {
            var report = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            report.AppendLine("Scene=" + scene.name + " loaded=" + scene.isLoaded + " playing=" + Application.isPlaying + " frame=" + Time.frameCount);
            foreach (var controller in UnityEngine.Object.FindObjectsByType<CharacterScreenController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var sameGoView = controller.GetComponent<CharacterScreenView>();
                var wiredView = ReadAny(controller, "view") as CharacterScreenView;
                report.AppendLine("Character=" + PathOf(controller) + " active=" + controller.gameObject.activeInHierarchy + " enabled=" + controller.enabled + " IsVisible=" + controller.IsVisible);
                report.AppendLine("  sameGOView=" + PathOf(sameGoView) + " serializedView=" + PathOf(wiredView));
                if (wiredView != null)
                {
                    var group = ReadAny(wiredView, "root") as CanvasGroup;
                    report.AppendLine("  view targetVisible=" + ReadAny(wiredView, "_targetVisible") + " hasBeenVisible=" + ReadAny(wiredView, "_hasBeenVisible") +
                        " group=" + PathOf(group) + (group != null ? " alpha=" + group.alpha + " interact=" + group.interactable + " block=" + group.blocksRaycasts : ""));
                }
            }
            foreach (var presentation in UnityEngine.Object.FindObjectsByType<ExpeditionPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var character = ReadAny(presentation, "_character") as UnityEngine.Object;
                report.AppendLine("Presentation=" + PathOf(presentation) + " active=" + presentation.gameObject.activeInHierarchy + " enabled=" + presentation.enabled + " _character=" + PathOf(character));
                report.AppendLine("  _scene=" + ReadAny(presentation, "_scene") + " _arenaParticipationLocked=" + ReadAny(presentation, "_arenaParticipationLocked") +
                    " _tournamentVisible=" + ReadAny(presentation, "_tournamentVisible"));
            }
            var activeBridge = typeof(ArenaMatch8Bridge).GetField("_activeBridge", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as ArenaMatch8Bridge;
            report.AppendLine("ArenaMatch8Bridge.IsParticipationLocked=" + ArenaMatch8Bridge.IsParticipationLocked + " _activeBridge=" + PathOf(activeBridge) +
                " _participationLocked=" + (activeBridge != null ? ReadAny(activeBridge, "_participationLocked") : "null"));
            foreach (var bridge in UnityEngine.Object.FindObjectsByType<ArenaMatch8Bridge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.AppendLine("  Bridge=" + PathOf(bridge) + " active=" + bridge.gameObject.activeInHierarchy + " enabled=" + bridge.enabled + " locked=" + ReadAny(bridge, "_participationLocked"));
            var navs = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(b => b.name == "ExpeditionNav2").ToArray();
            report.AppendLine("ExpeditionNav2 buttons=" + navs.Length);
            foreach (var button in navs)
            {
                report.AppendLine("Nav2=" + PathOf(button) + " active=" + button.gameObject.activeInHierarchy + " enabled=" + button.enabled + " interactable=" + button.interactable);
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    report.AppendLine("  persistent[" + i + "]=" + PathOf(button.onClick.GetPersistentTarget(i)) + "." + button.onClick.GetPersistentMethodName(i));
                // Inspect the backing runtime invocations, without PrepareInvoke
                // (which would rebuild UnityEvent's internal execution cache).
                var calls = ReadAny(button.onClick, "m_Calls", "_calls");
                var runtime = ReadAny(calls, "m_RuntimeCalls", "_runtimeCalls") as IEnumerable;
                var index = 0;
                if (runtime != null)
                {
                    foreach (var invocation in runtime)
                    {
                        var found = false;
                        for (var type = invocation?.GetType(); type != null; type = type.BaseType)
                        {
                            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                            {
                                if (!(field.GetValue(invocation) is Delegate handler)) continue;
                                foreach (var callback in handler.GetInvocationList())
                                {
                                    found = true;
                                    report.AppendLine("  runtime[" + index + "]=" + callback.Method.DeclaringType?.FullName + "." + callback.Method.Name + " target=" + DescribeTarget(callback.Target));
                                }
                            }
                        }
                        if (!found) report.AppendLine("  runtime[" + index + "] type=" + invocation?.GetType().FullName + " (delegate field unavailable)");
                        index++;
                    }
                }
                report.AppendLine("  runtimeCount=" + index + " invocationCache=" + (calls != null ? calls.GetType().FullName : "null"));
            }
            report.Append("Read-only: no UnityEvent.Invoke/guard mutation/navigation/RPC.");
            return report.ToString();
        }

        private static object ReadAny(object source, params string[] names)
        {
            if (source == null) return null;
            for (var type = source.GetType(); type != null; type = type.BaseType)
                foreach (var name in names)
                {
                    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    if (field != null) return field.GetValue(source);
                }
            return null;
        }
        private static string DescribeTarget(object target)
        {
            if (target == null) return "static";
            if (target is UnityEngine.Object unity) return PathOf(unity);
            var result = new StringBuilder(target.GetType().FullName);
            foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var value = field.GetValue(target);
                if (value is UnityEngine.Object obj) result.Append(" ").Append(field.Name).Append("=").Append(PathOf(obj));
                else if (value is int || value is bool) result.Append(" ").Append(field.Name).Append("=").Append(value);
            }
            return result.ToString();
        }
        private static string PathOf(UnityEngine.Object obj)
        {
            if (ReferenceEquals(obj, null)) return "null";
            if (obj == null) return "destroyed:" + obj.GetType().Name;
            var component = obj as Component;
            var go = component != null ? component.gameObject : obj as GameObject;
            if (go == null) return obj.GetType().Name + ":" + obj.name;
            var path = go.name;
            for (var parent = go.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return "[" + go.scene.name + "]" + path + (component != null ? " (" + component.GetType().Name + ")" : "");
        }

        public static string Show(string variant)
        {
            if (variant != "locked" && variant != "partial" && variant != "full") throw new ArgumentException("Use locked, partial or full.");
            if (_view != null) Restore();
            Check(Application.isPlaying, "Use an actual loaded character page in Play mode.");
            _view = UnityEngine.Object.FindObjectsByType<CharacterScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.GetComponent<CharacterScreenController>()?.IsVisible == true);
            Check(_view != null, "Open and load the character page first.");
            _catalog = Read<ItemCatalog>(_view, "_activeCatalog");
            Check(_catalog != null, "Actual cached item catalog is missing.");
            var actual = Read<CharacterGetRpcResponse>(_view.GetComponent<CharacterScreenController>(), "_lastProfile");
            Check(actual != null && actual.ok, "Actual character profile is missing.");
            _saved = new CharacterGetRpcResponse { ok = true, equipment_def_ids = (string[])Read<string[]>(_view, "_equipmentDefIds").Clone(),
                inventory_def_ids = (string[])Read<string[]>(_view, "_inventoryDefIds").Clone(), inventory_counts = (int[])Read<int[]>(_view, "_inventoryCounts").Clone(),
                inventory_capacity = _view.InventoryCapacity, inventory_slot_price_matter = _view.InventorySlotPrice, pending_loot = actual.pending_loot };
            _layout = _view.GetComponent<CharacterExpeditionLayout>(); _page = _layout.CurrentInventoryPage; _filter = Read<int>(_layout, "_filter");
            _group = Read<CanvasGroup>(_view, "root"); _interactable = _group.interactable; _blocks = _group.blocksRaycasts;
            _group.interactable = _group.blocksRaycasts = false;
            _item = _catalog.EnumerateDefinitions().First(d => !string.IsNullOrEmpty(d.ItemId) && _catalog.GetDisplayIcon(d) != null).ItemId;
            _capacity = variant == "locked" ? 25 : variant == "partial" ? 28 : 50;
            var ids = new string[50]; var counts = new int[50];
            for (var i = 25; i < _capacity; i++) { ids[i] = _item; counts[i] = 1; }
            _view.ApplyCharacterResponse(new CharacterGetRpcResponse { ok = true, equipment_def_ids = _saved.equipment_def_ids,
                inventory_def_ids = ids, inventory_counts = counts, inventory_capacity = _capacity, inventory_slot_price_matter = 3,
                pending_loot = new[] { new PendingInventoryLoot { def_id = _item, amount = 5 } } }, _catalog);
            typeof(CharacterExpeditionLayout).GetMethod("SelectFilter", Hidden).Invoke(_layout, new object[] { 0 });
            _layout.SelectInventoryPage(1); _layout.RefreshInventory(); _frame = Time.frameCount;
            return "Local " + variant + " fixture shown through actual ApplyCharacterResponse; verify after a rendered frame, then restore. Input blocked; no RPC/currency mutation.";
        }

        public static string Verify()
        {
            Check(_view != null && Time.frameCount > _frame, "Show a fixture and allow one rendered frame.");
            var slots = Read<List<ItemSlotView>>(_view, "_inventorySlots");
            Check(slots.Count == 50 && _view.InventoryCapacity == _capacity, "Expected fifty unique cells and authoritative capacity.");
            var indices = new HashSet<int>(); var visible = 0; var locked = 0; var next = 0;
            foreach (var slot in slots)
            {
                var index = slot.InventorySlotIndex; Check(indices.Add(index), "Duplicate global inventory index " + index);
                var handle = slot.GetComponent<CharacterDragSlotHandle>();
                Check(handle != null && handle.InventoryIndex == index, "Drag index differs from data index " + index);
                Check(handle.enabled == (index < _capacity), "Locked slot accepts drops or unlocked slot lost drag " + index);
                var lockButton = slot.transform.Find("ExpeditionInventoryLock").GetComponent<Button>();
                Check(lockButton.gameObject.activeSelf == (index >= _capacity), "Lock state differs at " + index);
                if (slot.gameObject.activeInHierarchy) visible++;
                if (index < 25) { Check(!slot.gameObject.activeSelf, "First page leaked into page two."); continue; }
                if (index >= _capacity)
                {
                    locked++;
                    Check(lockButton.interactable == (index == _capacity), "Purchase is not restricted to next cell " + index);
                    if (lockButton.interactable) { next++; Check(lockButton.GetComponentInChildren<TMP_Text>().text.Contains("3"), "Next-cell price is missing."); }
                }
                else
                {
                    Check(_view.TryGetInventoryItem(index, out var id, out _) && id == _item, "Second-page item did not bind " + index);
                    var icon = slot.transform.Find("Icon").GetComponent<Image>();
                    Check(icon.enabled && icon.sprite == _catalog.GetDisplayIcon(_catalog.Get(_item)) && icon.color == Color.white, "Wrong actual item icon " + index);
                }
            }
            Check(visible == 25 && locked == 50 - _capacity && next == (_capacity < 50 ? 1 : 0), "Unexpected visible/locked/purchase count.");
            var footer = _layout.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "ExpeditionInventoryFooter");
            Check(footer.text.Contains("5") && footer.text.Contains("Ожидают"), "Durable pending-loot notice disappeared.");
            var pageDrop = _layout.GetComponentsInChildren<CharacterInventoryPageDrop>(true);
            Check(pageDrop.Length == 2, "Cross-page drop targets are missing.");
            return "PASS actual binder: capacity=" + _capacity + ", fifty unique global data/drag bindings, page two has25cells, next-only3matter lock, original item icons, pending5; no RPC was submitted.";
        }

        public static string Restore()
        {
            if (_view == null) return "No inventory-page fixture retained.";
            try
            {
                _view.ApplyCharacterResponse(_saved, _catalog); _layout.SelectInventoryPage(_page);
                typeof(CharacterExpeditionLayout).GetMethod("SelectFilter", Hidden).Invoke(_layout, new object[] { _filter });
                Check(_view.InventoryCapacity == _saved.inventory_capacity, "Capacity restore failed.");
                Check(Read<string[]>(_view, "_inventoryDefIds").SequenceEqual(_saved.inventory_def_ids), "Inventory restore failed.");
                Check(Read<int[]>(_view, "_inventoryCounts").SequenceEqual(_saved.inventory_counts), "Counts restore failed.");
                return "PASS original inventory/equipment/catalog/capacity/pending metadata restored; no RPC or purchase.";
            }
            finally { if (_group != null) { _group.interactable = _interactable; _group.blocksRaycasts = _blocks; } _view = null; _layout = null; _saved = null; }
        }
        private static void RestoreOnExit() { try { Restore(); } catch { _view = null; } }
        private static T Read<T>(object source, string name) => (T)source.GetType().GetField(name, Hidden).GetValue(source);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
