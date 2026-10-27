using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.UI;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Actual BoardSync -> manager -> HUD round binding in a disabled, transport-free fixture.</summary>
    public static class ExpeditionBattleRoundQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public int checks; public List<string> modes = new(); }
        public static string Run()
        {
            var report = new Report();
            var root = new GameObject("InactiveBattleRoundQa"); root.SetActive(false);
            try
            {
                var manager = root.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                var hudRoot = new GameObject("QaRoundHud", typeof(RectTransform)); hudRoot.transform.SetParent(root.transform, false);
                var hud = hudRoot.AddComponent<Match3GameHUD>();
                hud.turnText = ExpeditionUi.Text(hud.transform, "TurnText", "", 23, 0, 0, 1, 1);
                hud.timerText = ExpeditionUi.Text(hud.transform, "TimerText", "", 52, 0, 0, 1, 1);
                hud.timerPhaseText = ExpeditionUi.Text(hud.transform, "TimerPhaseText", "", 25, 0, 0, 1, 1);
                // Production scenes already provide these bound labels. Supply them
                // explicitly in the inactive edit fixture so TMP's lazy Awake material
                // creation is not required by the legacy procedural outline setters.
                hud.raceLastTurnText = ExpeditionUi.Text(hud.transform, "RaceLastTurnText", "", 25, 0, 0, 1, 1);
                hud.raceManaBonusText = ExpeditionUi.Text(hud.transform, "RaceManaBonusText", "", 25, 0, 0, 1, 1);
                hud.raceGoalText = ExpeditionUi.Text(hud.transform, "RaceGoalText", "", 25, 0, 0, 1, 1);
                hud.UseExpeditionStyle();
                Set(manager, "_hud", hud); Set(manager, "_board", new Match3BoardLogic());
                Set(manager, "_myStats", new PlayerStats()); Set(manager, "_opStats", new PlayerStats());
                Set(manager, "_myUserId", "hero"); Set(manager, "_opUserId", "peer");
                Set(manager, "_hasInitialBoardSync", true);
                foreach (var mode in new[] { "classic", "pro", "race", "mine", "tournament", "boss" })
                {
                    hud.ResetRoundNumber();
                    Set(manager, "_pvpRaceQueue", mode == "race");
                    Set(manager, "_isSoloBotMode", mode == "mine" || mode == "boss");
                    Set(manager, "_isGlobalBoss", mode == "boss");
                    Apply(manager, Snapshot(1, "hero", mode));
                    Require(report, hud.roundText.text == "1 ход" && hud.turnText.text.Contains("Ваш ход"), mode + " first player is round1");
                    Apply(manager, Snapshot(1, "peer", mode));
                    Require(report, hud.roundText.text == "1 ход" && hud.turnText.text.Contains("соперника"), mode + " response is still round1");
                    var extra = Snapshot(1, "peer", mode); extra.extraTurn = true; extra.actionType = 4;
                    Apply(manager, extra);
                    Require(report, hud.roundText.text == "1 ход", mode + " extra/free action does not increment round");
                    Apply(manager, Snapshot(2, "hero", mode));
                    Require(report, hud.roundText.text == "2 ход", mode + " server round2 shown on control return");
                    Apply(manager, Snapshot(7, "peer", mode));
                    Apply(manager, Snapshot(7, "peer", mode));
                    Require(report, hud.roundText.text == "7 ход", mode + " reconnect adopts and preserves authoritative round7");
                    Apply(manager, Snapshot(2, "hero", mode));
                    Require(report, hud.roundText.text == "7 ход", mode + " stale snapshot cannot rewind displayed round");
                    hud.SetTimerPhase(true);
                    Require(report, hud.roundText.gameObject.activeSelf && hud.roundText.text == "7 ход", mode + " round remains visible during animation");
                    hud.SetTimerPhase(false);
                    report.modes.Add(mode);
                }
                var dto = JsonUtility.FromJson<M3BoardSyncMsg>(JsonUtility.ToJson(Snapshot(42, "hero", "classic")));
                Require(report, dto.roundNumber == 42, "JsonUtility retains the authoritative round DTO");
                hud.ResetRoundNumber(); Apply(manager, Snapshot(0, "hero", "boss"));
                Require(report, hud.RoundNumber == 0 && string.IsNullOrEmpty(hud.roundText.text) && !hud.roundText.gameObject.activeSelf,
                    "Legacy boss snapshot does not invent a historical round");
                report.passed = true;
                const string path = "Design/Release-0.3.15/media/validation/battle-round-hud-qa.json";
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
                return "PASS battle round HUD: " + report.checks + " actual manager/BoardSync/label checks across " + string.Join(", ", report.modes) + "; no network/account actions.";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void RunBatch() => Debug.Log(Run());
        private static M3BoardSyncMsg Snapshot(int round, string active, string mode) => new M3BoardSyncMsg
        {
            board = Enumerable.Repeat(1, 36).ToArray(), roundNumber = round, activeUserId = active,
            aHp = 150, bHp = 150, aMaxHp = 150, bMaxHp = 150,
            globalBoss = mode == "boss", pvpRace = mode == "race", raceGoalMana = mode == "race" ? 200 : 0,
            turnDurationSeconds = 30, turnPaused = true
        };
        private static void Apply(DuelMatch3Manager manager, M3BoardSyncMsg msg)
        {
            ((HashSet<M3BoardSyncMsg>)manager.GetType().GetField("_silentBoardSyncs", Private).GetValue(manager)).Add(msg);
            var routine = (IEnumerator)manager.GetType().GetMethod("ApplyRemoteBoardSync", Private).Invoke(manager, new object[] { msg });
            var steps = 0;
            while (routine.MoveNext()) if (++steps > 20) throw new InvalidOperationException("Unexpected animated/network wait in round fixture.");
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static void Require(Report report, bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks++;
        }
    }
}
