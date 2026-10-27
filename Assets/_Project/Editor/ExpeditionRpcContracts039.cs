using System;
using System.Collections.Generic;
using System.IO;
using Project.Character;
using Project.UI;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Parse real isolated-server payloads with the shipping Unity DTOs; no cache or account changes.</summary>
    public static class ExpeditionRpcContracts039
    {
        [Serializable] private sealed class Check { public string fixture; public bool passed; public string error; }
        [Serializable] private sealed class Report { public bool passed = true; public string checkedUtc; public List<Check> checks = new(); }
        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run contract parsing in Edit mode.");
            var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            const string root = "Assets/_Project/Editor/Fixtures/rpc-039";
            foreach (var file in Directory.GetFiles(root, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (name == "source.json") continue;
                var check = new Check { fixture = name };
                try
                {
                    var json = File.ReadAllText(file);
                    if (name.StartsWith("daily-", StringComparison.Ordinal))
                    {
                        var state = JsonUtility.FromJson<DailyRewardsService.State>(json);
                        check.passed = DailyRewardsService.Valid(state) && state.completion_reward.items?.Length == 1;
                        foreach (var reward in state.login.rewards) check.passed &= reward != null;
                        foreach (var quest in state.quests) check.passed &= quest != null && quest.target > 0 && quest.reward != null;
                        check.passed &= state.login.rewards[5].items?.Length == 2 && state.login.rewards[6].items?.Length == 3;
                        if (name.EndsWith("fullbag.json", StringComparison.Ordinal)) check.passed &= state.receipt?.pending_items?.Length == 1;
                    }
                    else if (name.StartsWith("male-", StringComparison.Ordinal) || name.StartsWith("female-", StringComparison.Ordinal))
                    {
                        var state = JsonUtility.FromJson<CharacterAppearanceService.State>(json);
                        check.passed = state.ok && state.themes?.Length == 10 && state.gender == (name.StartsWith("female-", StringComparison.Ordinal) ? "female" : "male");
                        if (name == "male-all-rewards.json") check.passed &= state.unlocked_prestige_count == 9 && state.rewarded_matter_total == 45 && Mathf.Approximately(state.passive_hp_bonus_percent, 4.5f);
                        foreach (var theme in state.themes) check.passed &= theme != null && (theme.id == "dawnwarden" || theme.requirements?.Length > 0 && theme.unlock_reward_matter == 5 && Mathf.Approximately(theme.unlock_reward_hp_percent, .5f));
                    }
                    else
                    {
                        var state = JsonUtility.FromJson<CharacterGetRpcResponse>(json);
                        check.passed = state.ok && state.consumables?.schema == 1 && state.progression != null && state.stats != null && state.equipment_def_ids?.Length == 8 && state.inventory_def_ids != null && state.inventory_counts?.Length == state.inventory_def_ids.Length;
                        if (name == "bounty-active.json") check.passed &= state.consumables.bounty_active && state.consumables.bounty_ends_at > state.consumables.server_now;
                        if (name == "elixir-armed.json") check.passed &= state.consumables.armed_item_id == "elixir_assault";
                    }
                }
                catch (Exception error) { check.error = error.Message; }
                report.passed &= check.passed; report.checks.Add(check);
            }
            report.passed &= report.checks.Count == 11;
            var result = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(".cache/qa");
            File.WriteAllText(".cache/qa/native-rpc-contracts-039.json", result);
            if (!report.passed) throw new InvalidOperationException(result);
            return "PASS: eleven actual RPC payloads parsed by shipping Unity DTOs, including typed empty arrays, pending loot, cosmetics and consumables.";
        }
    }
}
