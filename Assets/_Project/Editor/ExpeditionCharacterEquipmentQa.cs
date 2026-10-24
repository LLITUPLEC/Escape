using System;
using System.Collections.Generic;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Exercises the production equipment binder with local data; never calls equip or resource services.</summary>
    public static class ExpeditionCharacterEquipmentQa
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string GhostShader = "UI/ExpeditionEquipmentGhost";
        private static Snapshot _snapshot;

        private sealed class Snapshot
        {
            public CharacterScreenView View;
            public ItemCatalog Catalog;
            public string[] Equipment, Inventory;
            public int[] Counts;
            public CanvasGroup Group;
            public bool Interactable, BlocksRaycasts;
            public GameObject Selection;
        }

        static ExpeditionCharacterEquipmentQa()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreOnExit;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) RestoreOnExit();
            };
        }

        public static string Run(string fixture)
        {
            if (fixture == "restore") return Restore();
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode and an opened, loaded character page are required.");
            if (fixture != "all" && fixture != "empty" && fixture != "rarities") throw new ArgumentException("Use all, empty, rarities or restore.");
            EnsureSnapshot();
            try
            {
                if (fixture == "empty") return ShowEmpty();
                if (fixture == "rarities") return ShowRarities();
                var empty = ShowEmpty();
                var rarities = ShowRarities();
                var restored = Restore();
                return empty + "\n" + rarities + "\n" + restored;
            }
            catch
            {
                RestoreOnExit();
                throw;
            }
            finally
            {
                if (fixture == "all") RestoreOnExit();
            }
        }

        public static string Restore()
        {
            var saved = _snapshot;
            if (saved == null) return "No equipment fixture snapshot is active.";
            try
            {
                if (saved.View == null) return "Fixture view expired; no persistent account data was changed.";
                saved.View.BindEquipmentAndInventory(saved.Equipment, saved.Inventory, saved.Counts, saved.Catalog);
                Require(ReferenceEquals(Read<ItemCatalog>(saved.View, "_activeCatalog"), saved.Catalog), "Restore changed the cached catalog.");
                Require(Equal(Read<string[]>(saved.View, "_equipmentDefIds"), saved.Equipment), "Equipment restore differs from the snapshot.");
                Require(Equal(Read<string[]>(saved.View, "_inventoryDefIds"), saved.Inventory), "Inventory restore differs from the snapshot.");
                Require(Equal(Read<int[]>(saved.View, "_inventoryCounts"), saved.Counts), "Stack count restore differs from the snapshot.");
                return "PASS restore: original cached catalog, equipment, inventory and counts restored; no RPC was submitted.";
            }
            finally
            {
                if (saved.Group != null)
                {
                    saved.Group.interactable = saved.Interactable;
                    saved.Group.blocksRaycasts = saved.BlocksRaycasts;
                }
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && saved.Selection != null && saved.Selection.activeInHierarchy)
                    EventSystem.current.SetSelectedGameObject(saved.Selection);
                _snapshot = null;
            }
        }

        private static void EnsureSnapshot()
        {
            if (_snapshot != null)
            {
                Require(_snapshot.View != null, "Previous fixture view was destroyed; call restore before starting another fixture.");
                return;
            }
            CharacterScreenView view = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<CharacterScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var controller = candidate.GetComponent<CharacterScreenController>();
                if (controller != null && controller.IsVisible) { view = candidate; break; }
            }
            Require(view != null, "Open the character page and wait for its actual profile before running the fixture.");
            var catalog = Read<ItemCatalog>(view, "_activeCatalog");
            Require(catalog != null, "Character profile/catalog has not finished binding.");
            var equipment = Read<string[]>(view, "_equipmentDefIds");
            var inventory = Read<string[]>(view, "_inventoryDefIds");
            var counts = Read<int[]>(view, "_inventoryCounts");
            Require(equipment != null && equipment.Length == 8 && inventory != null && counts != null && counts.Length == inventory.Length,
                "Unexpected cached equipment/inventory shape.");
            var group = Read<CanvasGroup>(view, "root");
            Require(group != null, "Character input group is missing.");
            _snapshot = new Snapshot
            {
                View = view, Catalog = catalog, Equipment = (string[])equipment.Clone(), Inventory = (string[])inventory.Clone(), Counts = (int[])counts.Clone(),
                Group = group, Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts,
                Selection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null
            };
            // The screenshot can show catalog items the account does not own; prevent input from submitting them.
            group.interactable = group.blocksRaycasts = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static string ShowEmpty()
        {
            var saved = _snapshot;
            saved.View.BindEquipmentAndInventory(new string[8], saved.Inventory, saved.Counts, saved.Catalog);
            var slots = Read<List<EquipmentSlotView>>(saved.View, "_equipmentSlots");
            Require(slots.Count == 8, "Expected eight actual equipment slot bindings.");
            var seen = new HashSet<EquipmentSlotId>();
            foreach (var slot in slots)
            {
                Require(seen.Add(slot.SlotId), "Duplicate equipment slot binding: " + slot.SlotId);
                VerifyNoCaptions(slot);
                var ghost = ChildImage(slot, "ExpeditionEmptySlot");
                Require(ghost.enabled && ghost.sprite != null && ghost.gameObject.activeInHierarchy, "Missing empty hint: " + slot.SlotId);
                Require(ghost.color.a > .1f && ghost.color.a <= .55f, "Empty hint is not visibly subdued: " + slot.SlotId);
                Require(ghost.material != null && ghost.material.shader != null && ghost.material.shader.name == GhostShader,
                    "Empty hint is missing the real grayscale material: " + slot.SlotId);
                Require(!ShaderUtil.ShaderHasError(ghost.material.shader), "Ghost shader has import/compiler errors.");
                var icon = ItemIcon(slot);
                Require(!icon.enabled && icon.sprite == null, "Empty slot still shows an item icon: " + slot.SlotId);
                Require(!ChildImage(slot, "ExpeditionQualityRim").enabled, "Empty slot still has an equipped rarity rim: " + slot.SlotId);
            }
            return "PASS empty: eight distinct slots, no visible captions, gray subdued ghost material, no item icons or rarity rims. UI-only; clicks disabled until restore.";
        }

        private static string ShowRarities()
        {
            var saved = _snapshot;
            var ids = new string[8];
            var definitions = new ItemDefinition[8];
            var missing = new List<string>();
            for (var i = 0; i < ids.Length; i++)
            {
                var requested = (ItemQualityTier)(i % 3); // Actual green, blue and purple definitions in their compatible slots.
                foreach (var definition in saved.Catalog.EnumerateDefinitions())
                {
                    if (definition.Kind != ItemKind.Equipment || !definition.Equippable || (int)definition.Slot != i || definition.Quality != requested ||
                        string.IsNullOrEmpty(definition.ItemId) || saved.Catalog.GetDisplayIcon(definition) == null) continue;
                    definitions[i] = definition; ids[i] = definition.ItemId; break;
                }
                if (definitions[i] == null) missing.Add(((EquipmentSlotId)i) + "/" + requested);
            }
            Require(definitions[0] != null, "The cached catalog has no displayable Normal helmet; helmet coverage cannot be claimed.");
            saved.View.BindEquipmentAndInventory(ids, saved.Inventory, saved.Counts, saved.Catalog);
            var qualities = new HashSet<ItemQualityTier>();
            var details = new List<string>();
            foreach (var slot in Read<List<EquipmentSlotView>>(saved.View, "_equipmentSlots"))
            {
                var definition = definitions[(int)slot.SlotId];
                if (definition == null) continue;
                VerifyNoCaptions(slot);
                var icon = ItemIcon(slot);
                Require(icon.enabled && icon.gameObject.activeInHierarchy && icon.sprite == saved.Catalog.GetDisplayIcon(definition), "Actual catalog icon did not bind: " + definition.ItemId);
                Require(NearWhite(icon.color), "Real equipped icon is tinted: " + definition.ItemId);
                Require(icon.material == null || icon.material.shader == null || icon.material.shader.name != GhostShader, "Real item inherited grayscale: " + definition.ItemId);
                Require(!ChildImage(slot, "ExpeditionEmptySlot").enabled, "Equipped slot still shows its hint: " + definition.ItemId);
                var rim = ChildImage(slot, "ExpeditionQualityRim");
                var wash = ChildImage(slot, "ExpeditionQualityFill");
                Require(rim.enabled && !rim.fillCenter && rim.color.a > .5f, "Rarity rim is missing: " + definition.ItemId);
                Require(wash.enabled && wash.color.a > .05f, "Rarity background is missing: " + definition.ItemId);
                VerifyQualityHue(definition.Quality, rim.color, definition.ItemId + " rim");
                VerifyQualityHue(definition.Quality, wash.color, definition.ItemId + " background");
                qualities.Add(definition.Quality);
                details.Add(slot.SlotId + "=" + definition.ItemId + (definition.Icon == null ? " (catalog fallback icon)" : ""));
            }
            Require(qualities.Contains(ItemQualityTier.Normal) && qualities.Contains(ItemQualityTier.Rare) && qualities.Contains(ItemQualityTier.Epic),
                "The cached catalog did not provide displayable green, blue and purple equipment; missing: " + string.Join(", ", missing));
            return "PASS rarities: green/blue/purple background and rims, actual white icons without ghost material; helmet=true. " + string.Join("; ", details) +
                (missing.Count > 0 ? "; PARTIAL slot coverage, unavailable definitions: " + string.Join(", ", missing) : "; all eight slots covered") + ". UI-only; clicks disabled until restore.";
        }

        private static void VerifyNoCaptions(EquipmentSlotView slot)
        {
            foreach (var caption in slot.GetComponentsInChildren<TMP_Text>(true))
                Require(!caption.enabled || !caption.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(caption.text), "Visible equipment caption remains: " + slot.SlotId + "/" + caption.name);
            var stripe = slot.transform.Find("ExpeditionQuality");
            if (stripe != null && stripe.TryGetComponent<Image>(out var image)) Require(!image.enabled || !image.gameObject.activeInHierarchy, "Old equipment rarity stripe remains.");
        }

        private static void VerifyQualityHue(ItemQualityTier quality, Color color, string context)
        {
            var expected = quality == ItemQualityTier.Normal ? color.g > color.r && color.g > color.b
                : quality == ItemQualityTier.Rare ? color.b > color.r && color.b > color.g
                : quality == ItemQualityTier.Epic && color.r > color.g && color.b > color.g;
            Require(expected, "Expected green/blue/purple hue is absent: " + context);
        }

        private static Image ItemIcon(EquipmentSlotView slot)
        {
            var itemView = Read<ItemSlotView>(slot, "itemView");
            Require(itemView != null, "Equipment ItemSlotView binding is missing: " + slot.SlotId);
            var icon = Read<Image>(itemView, "icon");
            Require(icon != null, "Equipment icon binding is missing: " + slot.SlotId);
            return icon;
        }

        private static Image ChildImage(EquipmentSlotView slot, string name)
        {
            var child = slot.transform.Find(name);
            var image = child != null ? child.GetComponent<Image>() : null;
            Require(image != null, "Presentation child missing: " + slot.SlotId + "/" + name);
            return image;
        }

        private static T Read<T>(object owner, string name)
        {
            var field = owner.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return (T)field.GetValue(owner);
        }

        private static bool Equal<T>(T[] actual, T[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length) return false;
            for (var i = 0; i < actual.Length; i++) if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i])) return false;
            return true;
        }

        private static bool NearWhite(Color color) => Mathf.Abs(color.r - 1) < .01f && Mathf.Abs(color.g - 1) < .01f && Mathf.Abs(color.b - 1) < .01f && Mathf.Abs(color.a - 1) < .01f;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void RestoreOnExit() { if (_snapshot != null) { try { Restore(); } catch (Exception error) { Debug.LogException(error); } } }
    }
}
