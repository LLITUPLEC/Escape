using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Offline regression coverage for Race admission and tournament recovery, identity and return navigation.</summary>
    public static class ExpeditionModeRecoveryQa
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
            const string output = ".cache/qa/mode-regressions-2026-10-28";
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/native-aggregate.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Mode recovery QA failed; inspect " + output + "/native-aggregate.json");
            Debug.Log("PASS: all four mode recovery QA groups. No player build or publication.");
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
