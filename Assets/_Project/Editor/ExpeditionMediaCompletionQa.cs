using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    public static class ExpeditionMediaCompletionQa
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string checkedUtc, itemMedia, monstersAndUi, appearances;
        }

        [MenuItem("Tools/Escape/Expedition/Validate all completed media")]
        public static void RunAll()
        {
            ExpeditionItemMediaImporter.Prepare();
            ExpeditionMediaCompletionImporter.Prepare();
            var report = new Report
            {
                passed = true,
                checkedUtc = DateTime.UtcNow.ToString("O"),
                itemMedia = ExpeditionItemMediaImporter.Validate(),
                monstersAndUi = ExpeditionMediaCompletionImporter.Validate(),
                appearances = ExpeditionAppearanceQa.Run()
            };
            var output = Path.GetFullPath("Design/Validation/Appearance");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "unity-media-validation.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[MediaCompletion] " + JsonUtility.ToJson(report));
            ExpeditionAppearanceCapture.CapturePreviews();
        }
    }
}
