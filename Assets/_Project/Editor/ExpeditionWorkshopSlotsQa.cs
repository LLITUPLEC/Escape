using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Project.Character;
using Project.Character.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Actual workshop timer/icon writer with a temporary controller-only profile; no craft/claim/RPC calls.</summary>
    public static class ExpeditionWorkshopSlotsQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static WorkshopSceneController _controller;
        private static CharacterGetRpcResponse _originalProfile;
        private static ItemCatalog _catalog;
        private static int _selectedSlot, _lastTimerSecond;
        private static bool _controllerEnabled;
        private static CanvasGroup _group;
        private static bool _addedGroup, _interactable, _blocks;
        private static string _variant;

        public static string Show(string variant = "empty")
        {
            Check(Application.isPlaying, "Open Workshop in Play Mode first; helper does not start Unity/Play.");
            Check(variant == "empty" || variant == "filled", "Use empty or filled.");
            Check(_originalProfile == null, "Restore the previous workshop fixture first.");
            var controller = UnityEngine.Object.FindFirstObjectByType<WorkshopSceneController>();
            Check(controller != null, "Actual WorkshopSceneController is required.");
            var original = Read<CharacterGetRpcResponse>(controller, "_profile");
            var catalog = Read<ItemCatalog>(controller, "itemCatalog");
            var layout = Read<ExpeditionWorkshopLayout>(controller, "_expeditionLayout");
            Check(original != null && original.ok && catalog != null && layout != null, "Wait for the real cached workshop profile/catalog.");
            Check(!Read<bool>(controller, "_craftRequestInFlight"), "Wait for the real craft action to finish before a fixture.");
            var profile = new CharacterGetRpcResponse { ok = true, workshop_output_def_ids = new string[8], workshop_ends_at = new int[8] };
            if (variant == "filled")
            {
                for (var i = 0; i < 8; i++)
                {
                    var slot = i;
                    var definition = catalog.EnumerateDefinitions().FirstOrDefault(d => d != null && d.Kind == ItemKind.Equipment
                        && (int)d.Slot == slot && !string.IsNullOrEmpty(d.CraftRecipeId) && catalog.GetDisplayIcon(d) != null);
                    Check(definition != null, "Actual catalog has no craftable illustrated definition for slot " + i + "; no substitute fabricated.");
                    profile.workshop_output_def_ids[i] = definition.ItemId;
                    profile.workshop_ends_at[i] = Now() + (i % 2 == 0 ? 3671 : -1);
                }
            }
            _controller = controller; _catalog = catalog; _originalProfile = original; _variant = variant;
            _controllerEnabled = controller.enabled;
            _selectedSlot = Read<int>(controller, "_selectedSlot"); _lastTimerSecond = Read<int>(controller, "_lastTimerSecond");
            try
            {
                Read<CancellationTokenSource>(controller, "_cts")?.Cancel();
                controller.enabled = false;
                _group = layout.GetComponent<CanvasGroup>();
                _addedGroup = _group == null;
                if (_addedGroup) _group = layout.gameObject.AddComponent<CanvasGroup>();
                _interactable = _group.interactable; _blocks = _group.blocksRaycasts;
                _group.interactable = false; _group.blocksRaycasts = false;
                Set(controller, "_selectedSlot", -1); Set(controller, "_profile", profile);
                Render(); Canvas.ForceUpdateCanvases();
                return "SHOWN " + variant + ": actual eight workshop slots via real UpdateSlotTimersAndIcons; temporary controller-only profile, catalog sprites preserved, input blocked. Verify after render, then Restore. No RPC/craft/claim/resource actions.";
            }
            catch { Restore(); throw; }
        }

        public static string Verify()
        {
            Check(_controller != null && _originalProfile != null, "Call Show(empty/filled) first.");
            Canvas.ForceUpdateCanvases();
            var profile = Read<CharacterGetRpcResponse>(_controller, "_profile");
            var icons = Read<Image[]>(_controller, "_slotIcons");
            var timers = Read<Text[]>(_controller, "_slotTimers");
            var root = Read<Transform>(_controller, "craftSlotsRoot");
            for (var i = 0; i < 8; i++)
            {
                var slot = root.Find("Slot_" + i) as RectTransform;
                Check(slot != null && slot.gameObject.activeInHierarchy, "Missing actual slot " + i);
                var caption = slot.Find("SlotLabel").GetComponent<Text>();
                var timer = timers[i];
                CheckTextFit(caption, slot); CheckTextFit(timer, slot);
                Check(caption.rectTransform.anchorMin.y > timer.rectTransform.anchorMax.y,
                    "Slot name and state use overlapping vertical areas.");
                var mark = slot.Find("EmptyQueueMark");
                if (_variant == "empty")
                {
                    Check(timer.text == "Пусто" && !icons[i].enabled && mark.gameObject.activeInHierarchy,
                        "Empty slot does not preserve name/Пусто/empty hint.");
                }
                else
                {
                    var definition = _catalog.Get(profile.workshop_output_def_ids[i]);
                    Check(icons[i].enabled && icons[i].sprite == _catalog.GetDisplayIcon(definition) && icons[i].color == Color.white
                        && icons[i].preserveAspect, "Filled slot changed actual catalog icon or tint.");
                    Check(!mark.gameObject.activeInHierarchy && timer.text != "Пусто", "Filled slot retains its empty overlay/state.");
                    Check(i % 2 == 0 ? timer.text.Contains(":") : timer.text == "Готово!", "Busy/ready writer changed.");
                    var iconBounds = icons[i].rectTransform;
                    Check(iconBounds.anchorMin.y >= timer.rectTransform.anchorMax.y
                        && iconBounds.anchorMax.y <= caption.rectTransform.anchorMin.y, "Recipe icon overlaps name/state.");
                }
            }
            Check(!_group.interactable && !_group.blocksRaycasts, "Fixture permits accidental craft/claim input.");
            return "PASS " + _variant + ": eight actual slots retain one-line names/states with rendered glyph bounds inside padded frames; empty hints or original catalog icons, busy timers and ready labels correct. UI-only fixture remains shown; Restore required.";
        }

        public static string RunVariants()
        {
            try { Show("empty"); Verify(); Restore(); Show("filled"); Verify(); }
            finally { Restore(); }
            return "PASS empty+filled: actual eight-slot writer, padded rendered text, preserved catalog sprites and busy/ready states. Original controller profile/selection/input restored; no RPC/craft/claim/spend.";
        }

        public static string Restore()
        {
            try
            {
                if (_controller != null && _originalProfile != null)
                {
                    Set(_controller, "_profile", _originalProfile);
                    Set(_controller, "_selectedSlot", -1);
                    Render();
                }
            }
            finally
            {
                if (_controller != null)
                {
                    Set(_controller, "_selectedSlot", _selectedSlot); Set(_controller, "_lastTimerSecond", _lastTimerSecond);
                    _controller.enabled = _controllerEnabled;
                }
                if (_group != null)
                {
                    if (_addedGroup) UnityEngine.Object.DestroyImmediate(_group);
                    else { _group.interactable = _interactable; _group.blocksRaycasts = _blocks; }
                }
                _controller = null; _originalProfile = null; _catalog = null; _group = null; _variant = null;
                Canvas.ForceUpdateCanvases();
            }
            return "RESTORED: original cached workshop profile/selection/controller/input; no craft/claim/resource or account mutation.";
        }

        private static void CheckTextFit(Text text, RectTransform slot)
        {
            Check(text != null && text.enabled && !string.IsNullOrWhiteSpace(text.text), "Actual slot text missing.");
            var mesh = text.canvasRenderer.GetMesh();
            Check(mesh != null && mesh.vertexCount > 0 && text.cachedTextGenerator.lineCount == 1,
                "Slot text is absent/clipped or wraps to multiple lines: " + text.text);
            var bounds = mesh.bounds;
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
                foreach (var y in new[] { bounds.min.y, bounds.max.y })
                {
                    var p = slot.InverseTransformPoint(text.transform.TransformPoint(new Vector3(x, y, 0)));
                    var inset = slot.rect;
                    Check(p.x >= inset.xMin + 3 && p.x <= inset.xMax - 3 && p.y >= inset.yMin + 3 && p.y <= inset.yMax - 3,
                        "Rendered glyph leaves the padded slot frame: " + text.text);
                }
        }

        private static void Render() => typeof(WorkshopSceneController).GetMethod("UpdateSlotTimersAndIcons", Private).Invoke(_controller, null);
        private static int Now() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
        private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
