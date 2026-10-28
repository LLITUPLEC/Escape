using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Production reconnect and presentation regressions in one fresh editor process. No build or publication.</summary>
    public static class ExpeditionReconnectReleaseQa
    {
        [Serializable] private sealed class Phase { public string name, result, error; public bool passed; }
        [Serializable] private sealed class Report
        {
            public string utc;
            public bool passed, playerBuilt, publicationPerformed;
            public List<Phase> phases = new();
        }

        public static void RunBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), passed = true };
            // Account QA owns an initially empty dispatcher queue and must run first.
            Run(report, "account-lifetime-and-wire", ExpeditionReconnectAccountQa.Run);
            Run(report, "battle-reconnect-and-cancel", ExpeditionBattleReconnectQa.Run);
            Run(report, "resources-appearance-and-ability-costs", ExpeditionReconnectUiQa.Run);
            Run(report, "startup-and-tournament-regressions", () => { ExpeditionInitialStateQa.RunBatch(); return "Initial hydration and compact tournament footer"; });
            Run(report, "battle-entry-regressions", ExpeditionBattleEntryQa.Run);
            Run(report, "global-boss-practice-regressions", ExpeditionGlobalBossPracticeQa.Run);
            Run(report, "preserved-appearance-mail-fixture", () => { ExpeditionAppearanceMailQa.RunBatch(); return "Preserved informational unlock inbox and hero navigation"; });
            Directory.CreateDirectory(".cache/qa/reconnect-0317");
            File.WriteAllText(".cache/qa/reconnect-0317/native-aggregate.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Reconnect release QA failed; inspect .cache/qa/reconnect-0317/native-aggregate.json");
            Debug.Log("PASS: all seven reconnect release QA groups. No player build or publication.");
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
