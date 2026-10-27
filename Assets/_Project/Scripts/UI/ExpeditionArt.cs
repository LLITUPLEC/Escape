using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Shared, imported raster artwork for the Expedition interface.</summary>
    public static class ExpeditionArt
    {
        private const string Root = "Expedition/UI/";
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static bool _iconsLoaded;
        private static bool _utilityIconsLoaded;
        private static Sprite _panel;
        private static Sprite _primary;

        /// <summary>Returns a sculpted object icon, or null for an unknown key.</summary>
        public static Sprite Icon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_iconsLoaded)
            {
                var atlas = Resources.LoadAll<Sprite>(Root + "ui-icons-atlas");
                foreach (var sprite in atlas)
                    if (sprite != null) Icons[sprite.name] = sprite;
                // Do not retain a missing import across an Editor assembly/asset refresh.
                _iconsLoaded = atlas.Length > 0;
            }
            var canonical = CanonicalKey(key);
            if ((canonical == "friends" || canonical == "health-red" || canonical == "healing-cross") &&
                (!Icons.TryGetValue(canonical, out var standalone) || standalone == null))
            {
                standalone = Resources.Load<Sprite>("Expedition/Icons/" + canonical);
                if (standalone != null) Icons[canonical] = standalone;
            }
            if (!_utilityIconsLoaded && (canonical == "trophy" || canonical == "settings"))
            {
                foreach (var sprite in Resources.LoadAll<Sprite>(Root + "utility-icons-atlas"))
                    if (sprite != null) Icons[sprite.name] = sprite;
                // Retry after an asset refresh if either sprite was not imported yet.
                _utilityIconsLoaded = Icons.TryGetValue("trophy", out var trophy) && trophy != null &&
                    Icons.TryGetValue("settings", out var settings) && settings != null;
            }
            return Icons.TryGetValue(canonical, out var icon) && icon != null ? icon : null;
        }

        /// <summary>Baked petrol metal or amber plate. Use Image.Type.Sliced and a white tint.</summary>
        public static Sprite Frame(bool primary = false)
        {
            if (primary)
            {
                if (_primary == null) _primary = LoadFrame("primary-button-v2");
                return _primary;
            }
            if (_panel == null) _panel = LoadFrame("panel-frame");
            return _panel;
        }

        private static Sprite LoadFrame(string name)
        {
            // These textures contain a single cropped sub-sprite so transparent
            // generation padding cannot shrink a live panel's visible bounds.
            var sprites = Resources.LoadAll<Sprite>(Root + name);
            return sprites.Length > 0 ? sprites[0] : null;
        }

        private static string CanonicalKey(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "slot-helmet": return "helmet";
                case "slot-shoulders": return "shoulders";
                case "slot-chest": return "chest";
                case "slot-gloves": return "gloves";
                case "slot-legs": return "legs";
                case "slot-feet": case "feet": return "boots";
                case "slot-hand-left": case "hand-left": return "left-hand";
                case "slot-hand-right": case "hand-right": return "right-hand";
                case "stat-health": case "health": case "heart": return "health-red";
                case "stat-healing": case "healing": return "healing-cross";
                case "stat-damage": case "damage": case "sword": return "swords";
                case "stat-armor": case "armor": return "shield";
                case "stat-critical": case "critical": return "energy";
                case "achievements": case "achievement": case "trophy": return "trophy";
                case "settings": case "gear": return "settings";
                case "mine": return "pickaxe";
                case "workshop": case "forge": return "anvil";
                case "arena": return "swords";
                case "character": return "hero";
                case "inventory": case "supplies": return "bag";
                default: return key;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            Icons.Clear();
            _iconsLoaded = false;
            _utilityIconsLoaded = false;
            _panel = null;
            _primary = null;
        }
    }
}
