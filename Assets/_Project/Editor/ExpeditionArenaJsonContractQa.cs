using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Project.Editor
{
    public static class ExpeditionArenaJsonContractQa
    {
        [Serializable] private sealed class Result { public string fixture, error, kind; public bool passed; public int offers; }
        [Serializable] private sealed class Report { public bool passed; public string checkedUtc; public List<Result> results = new(); }
        public static string Run()
        {
            var modelType = typeof(Project.UI.ArenaMatch8Bridge).GetNestedType("ArenaPollResponse", BindingFlags.NonPublic);
            if (modelType == null) throw new InvalidOperationException("Actual arena poll response model missing.");
            var report = new Report { passed = true, checkedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var file in Directory.GetFiles("Assets/_Project/Editor/Fixtures/arena-038", "*.json"))
            {
                var result = new Result { fixture = Path.GetFileName(file), offers = -1 };
                try
                {
                    var model = JsonUtility.FromJson(File.ReadAllText(file), modelType);
                    var offers = modelType.GetField("smith_offers").GetValue(model) as Array;
                    result.offers = offers?.Length ?? 0;
                    result.kind = modelType.GetField("queue_kind").GetValue(model) as string;
                    result.passed = (bool)modelType.GetField("ok").GetValue(model) &&
                        result.kind == (result.fixture.StartsWith("smith", StringComparison.Ordinal) ? "smith" : "gold") &&
                        result.offers == (result.kind == "smith" ? 3 : 0);
                }
                catch (Exception error) { result.error = error.Message; }
                report.passed &= result.passed; report.results.Add(result);
            }
            report.passed &= report.results.Count == 4;
            var json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(".cache/qa");
            File.WriteAllText(".cache/qa/arena-native-json-contract.json", json);
            return json;
        }
    }
}
