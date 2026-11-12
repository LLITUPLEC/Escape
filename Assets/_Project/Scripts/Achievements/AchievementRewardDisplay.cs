using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    internal sealed class AchievementRewardDisplay
    {
        private static readonly Regex RewardSeparator = new(@"(?:\s*,\s*|\s+и\s+)(?=\+)");
        private readonly List<(RectTransform Root, TMP_Text Text, Image Icon)> _lines = new();
        private readonly RectTransform _root;
        private readonly TMP_FontAsset _font;
        private readonly float _size;

        public AchievementRewardDisplay(RectTransform root, float size, TMP_FontAsset font = null)
        {
            _root = root;
            _font = font;
            _size = size;
            var stack = root.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.MiddleLeft;
            stack.childControlHeight = stack.childControlWidth = true;
            stack.childForceExpandHeight = false;
            stack.childForceExpandWidth = true;
        }

        public void Bind(AchievementChainDefinition chain, int step)
        {
            var text = chain.RewardTexts != null && step < chain.RewardTexts.Length
                ? chain.RewardTexts[step] ?? string.Empty : string.Empty;
            const string prefix = "Награда:";
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(prefix.Length).TrimStart();

            var visible = !string.IsNullOrWhiteSpace(text);
            _root.gameObject.SetActive(visible);
            if (!visible) return;
            var parts = RewardSeparator.Split(text);
            var types = chain.RewardTypes != null && step < chain.RewardTypes.Length ? chain.RewardTypes[step] : null;
            while (_lines.Count < parts.Length) BuildLine();
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                line.Root.gameObject.SetActive(i < parts.Length);
                if (i >= parts.Length) continue;
                line.Text.text = parts[i];
                line.Icon.sprite = IconFor(types?.Length == parts.Length ? types[i] : null, parts[i]);
                line.Icon.gameObject.SetActive(line.Icon.sprite != null);
            }
        }

        private void BuildLine()
        {
            var root = ExpeditionUi.Rect(_root, "RewardLine", 0, 0, 1, 1);
            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var label = ExpeditionUi.Text(root, "MedalReward", "", _size, 0, 0, 1, 1);
            if (_font != null) label.font = _font;
            label.margin = Vector4.zero;
            label.color = ExpeditionTheme.Amber;
            label.richText = false;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = _size * .76f;
            label.fontSizeMax = _size;
            label.gameObject.AddComponent<LayoutElement>().minWidth = 0;
            var icon = ExpeditionUi.Fill(root, "MedalRewardIcon", 0, 0, 1, 1, Color.white);
            icon.preserveAspect = true;
            var size = icon.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = _size * 1.08f;
            size.minHeight = size.preferredHeight = _size * 1.08f;
            _lines.Add((root, label, icon));
        }

        private static Sprite IconFor(string type, string text)
        {
            var key = type switch
            {
                "gold" or "ore" or "matter" => type,
                "hp_flat" or "hp_pct" => "health",
                "damage_flat" or "damage_pct" => "damage",
                "armor_flat" or "armor_pct" => "armor",
                "heal_flat" or "heal_pct" => "healing",
                "crit_flat" or "crit_pct" => "critical",
                _ => LegacyIconKey(text),
            };
            return ExpeditionResourceIcons.Get(key);
        }

        private static string LegacyIconKey(string text)
        {
            text = text.ToLowerInvariant();
            if (text.Contains("золот")) return "gold";
            if (text.Contains("руд")) return "ore";
            if (text.Contains("матери")) return "matter";
            if (text.Contains("здоров")) return "health";
            if (text.Contains("крит")) return "critical";
            if (text.Contains("урон")) return "damage";
            if (text.Contains("брон")) return "armor";
            return text.Contains("лечен") ? "healing" : null;
        }
    }
}
