using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Local edit-mode checks only. Does not build a player or contact the game server.</summary>
    public static class ExpeditionLocalPolishQa
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
            Run(report, "startup-and-tournament", () => { ExpeditionInitialStateQa.RunBatch(); return "Initial hydration, failure/retry and compact tournament footer"; });
            Run(report, "shelter-concept-c", ExpeditionShelterConceptCQa.Run);
            Run(report, "equipment-sets", ExpeditionEquipmentSetQa.Run);
            Run(report, "workshop-quote-contract", ExpeditionWorkshopQuoteQa.Run);
            Run(report, "global-boss-practice", ExpeditionGlobalBossPracticeQa.Run);
            Run(report, "global-boss-native-capture", ExpeditionGlobalBossPracticeQa.Capture);
            Run(report, "presentation", ExpeditionLocalPresentationQa.Run);
            Run(report, "battle-entry-regressions", ExpeditionBattleEntryQa.Run);
            Directory.CreateDirectory(".cache/qa/local-polish");
            File.WriteAllText(".cache/qa/local-polish/native-aggregate.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Local polish QA failed; inspect .cache/qa/local-polish/native-aggregate.json");
            Debug.Log("PASS: all eight local polish QA groups. No player build or publication.");
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
