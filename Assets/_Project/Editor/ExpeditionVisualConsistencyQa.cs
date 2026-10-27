using System;
using System.Linq;
using Project.Match3;
using Project.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    public static class ExpeditionVisualConsistencyQa
    {
        public static void ValidateBatch() => Debug.Log(Run());

        public static string Run()
        {
            var health = ExpeditionArt.Icon("stat-health");
            var healing = ExpeditionArt.Icon("stat-healing");
            Require(health != null && healing != null && health != healing, "Health and healing must have different imported sprites.");
            Require(health == Resources.Load<Sprite>("Expedition/Icons/health-red") && health == ExpeditionArt.Icon("health") && health == ExpeditionArt.Icon("heart"), "Health aliases do not resolve to the shared red heart.");
            Require(healing == Resources.Load<Sprite>("Expedition/Icons/healing-cross") && healing == ExpeditionArt.Icon("healing"), "Healing aliases do not resolve to the shared medical cross.");
            var host = new GameObject("VisualConsistencyQa", typeof(RectTransform));
            try
            {
                var panel = host.AddComponent<Match3PlayerPanel>();
                var frame = ExpeditionUi.Rect(host.transform, "StatsFrame", 0, 0, 1, 1);
                panel.combatStatsName = ExpeditionUi.Text(frame, "CombatStatsName", "", 22, 0, 0, 1, 1);
                panel.combatStatsValue = ExpeditionUi.Text(frame, "CombatStatsValue", "", 22, 0, 0, 1, 1);
                panel.UseExpeditionStyle();
                panel.UpdateCombatStats(123, 45, 67, 8.5f);
                var stats = host.GetComponentInChildren<ExpeditionCombatStatIcons>(true);
                Require(stats != null && stats.Icons.Length == 4, "Battle must show four stat icons without a duplicate health value.");
                var keys = new[] { "stat-damage", "stat-armor", "stat-healing", "stat-critical" };
                for (var i = 0; i < keys.Length; i++)
                    Require(stats.Icons[i].sprite == ExpeditionArt.Icon(keys[i]), "Hero and battle stat icons differ: " + keys[i]);
                var values = stats.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(t => t.text).ToArray();
                Require(new[] { "123", "45", "67", 8.5f.ToString("0.#") + "%" }.All(values.Contains), "Battle stat values changed during presentation.");
                Require(!panel.combatStatsName.gameObject.activeSelf && !panel.combatStatsValue.gameObject.activeSelf, "Legacy word labels are still visible under the icons.");
                panel.UpdateCombatStats(123, 45, 67, 8.5f, false);
                Require(!stats.Icons[2].transform.parent.gameObject.activeSelf, "Hidden healing still has a visible icon cell.");

                var target = ExpeditionUi.Rect(host.transform, "Monster", 0, 0, 1, 1).gameObject.AddComponent<Image>();
                target.sprite = health;
                target.color = Color.white;
                var easy = ExpeditionDifficultyAura.Set(target, "easy");
                var shared = easy.Glow.sprite;
                Require(shared != null && shared.texture.width == 64 && !easy.Glow.raycastTarget, "Aura must use one small non-interactive shared texture.");
                Require(easy.Glow.transform.GetSiblingIndex() < target.transform.GetSiblingIndex(), "Aura is covering the monster instead of sitting behind it.");
                var yellow = easy.Glow.color;
                var hard = ExpeditionDifficultyAura.Set(target, "Тяжёлая");
                Require(hard.Glow.sprite == shared && hard.Difficulty == "hard", "Difficulty created another texture or failed to normalize localized text.");
                Require(hard.Glow.color.g < yellow.g && hard.Glow.color.b < yellow.b && target.color == Color.white && target.sprite == health, "Difficulty does not change aura color independently of monster art.");
                Require(ExpeditionDifficultyAura.Normalize("Средняя") == "medium", "Medium difficulty is not recognized.");
                ExpeditionDifficultyAura.Set(target, null);
                Require(!hard.enabled && !hard.Glow.enabled, "PvP/disabled difficulty left a monster aura visible.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            return "PASS visual consistency: shared red heart/medical cross; four battle stat icons and live values; one 64x64 difficulty glow shared across three severities, behind unchanged monster artwork.";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
