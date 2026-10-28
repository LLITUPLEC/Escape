using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Production code and native UI regression checks for the 0.3.18 candidate; no player build or publication.</summary>
    public static class ExpeditionRelease0318Qa
    {
        [Serializable] private sealed class Phase { public string name, result, error; public bool passed; }
        [Serializable] private sealed class Report
        {
            public string utc;
            public bool passed = true, playerBuilt, publicationPerformed;
            public List<Phase> phases = new();
        }
        public static void RunBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            Run(report, "account-lifetime-and-wire", ExpeditionReconnectAccountQa.Run);
            Run(report, "battle-reconnect-and-race-admission", ExpeditionBattleReconnectQa.Run);
            Run(report, "battle-entry-and-tournament-return", ExpeditionBattleEntryQa.Run);
            Run(report, "tournament-and-race-identity", ExpeditionTournamentIdentityQa.Run);
            Run(report, "boss-actions-and-privileged-rows", ExpeditionBossActionRowsQa0318.Run);
            Run(report, "disposable-boss-practice", ExpeditionGlobalBossPracticeQa.Run);
            Run(report, "appearance-cache-and-catalog", ExpeditionAppearanceQa.Run);
            Run(report, "resources-and-appearance-regressions", ExpeditionReconnectUiQa.Run);
            Run(report, "monster-avatar-assets-and-binding", ExpeditionMonsterAvatarQa0318.Run);
            Run(report, "appearance-dossier-and-practice-ui", ExpeditionRelease0318UiQa.Run);
            Directory.CreateDirectory(".cache/qa/release0318");
            File.WriteAllText(".cache/qa/release0318/native-aggregate.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Release0.3.18 QA failed; inspect .cache/qa/release0318/native-aggregate.json");
            Debug.Log("PASS: all ten release0.3.18 native QA groups; no player build or publication.");
        }
        private static void Run(Report report, string name, Func<string> action)
        {
            var phase = new Phase { name = name };
            try { phase.result = action(); phase.passed = true; Debug.Log("PASS " + name + ": " + phase.result); }
            catch (Exception error) { phase.error = error.ToString(); report.passed = false; Debug.LogWarning(name + ": " + error); }
            report.phases.Add(phase);
        }
    }
}
