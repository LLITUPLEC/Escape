using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Actual avatar pointer handling and boss BoardSync presentation in a disabled offline manager.</summary>
    public static class ExpeditionBossActionRowsQa0318
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public string error; public List<string> checks = new(); }
        public static void RunBatch() => Debug.Log(Run());
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            const string output = ".cache/qa/release0318/boss";
            Directory.CreateDirectory(output); var report = new Report();
            var root = new GameObject("OfflineBossRows0318", typeof(RectTransform), typeof(Canvas));
            var inactive = new GameObject("DisabledBossManager0318"); inactive.SetActive(false);
            try
            {
                var manager = inactive.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                Set(manager, "_offlinePresentationQa", true); Set(manager, "_isGlobalBoss", true);
                var op = new GameObject("BossPanel", typeof(RectTransform)); op.transform.SetParent(root.transform, false);
                var panel = op.AddComponent<Match3PlayerPanel>();
                var avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image)); avatar.transform.SetParent(op.transform, false);
                panel.avatarImage = avatar.GetComponent<Image>(); Set(manager, "_opPanel", panel);
                var boardRoot = new GameObject("Board", typeof(RectTransform)); boardRoot.transform.SetParent(root.transform, false);
                var boardView = boardRoot.AddComponent<Match3BoardView>();
                boardView.cellContainer = new GameObject("Cells", typeof(RectTransform), typeof(GridLayoutGroup)).transform;
                boardView.cellContainer.SetParent(boardRoot.transform, false); boardView.Build(); Set(manager, "_boardView", boardView);
                Call(manager, "EnsureCheatRowsOverlay", new M3BoardSyncMsg { globalBoss = true });
                Check(Get<Match3LongPressActivator>(manager, "_cheatRowsLongPressActivator") == null && !Get<bool>(manager, "_hasCheatRowsAccess"),
                    "A boss packet without privileged rows creates no avatar trigger or overlay access", report);
                var rows = Enumerable.Range(0, 12).Select(i => i % 5 + 1).ToArray();
                Call(manager, "EnsureCheatRowsOverlay", new M3BoardSyncMsg { globalBoss = true, cheatRows = rows });
                var trigger = Get<Match3LongPressActivator>(manager, "_cheatRowsLongPressActivator");
                var view = Get<Match3CheatRowsOverlayView>(manager, "_cheatRowsOverlayView");
                Check(trigger != null && trigger.transform.parent == avatar.transform && view.IsBuilt && !view.IsVisible,
                    "Allowlisted boss rows bind to the opponent avatar and start hidden", report);
                var cells = Get<Transform>(view, "cellContainer");
                Check(cells is RectTransform && cells.GetComponent<GridLayoutGroup>() != null && cells.childCount == 12,
                    "First dynamic overlay owns the live RectTransform and twelve ghost cells", report);
                var legacyRoot = new GameObject("LegacyOverlayFixture"); legacyRoot.transform.SetParent(boardRoot.transform, false);
                var legacyView = legacyRoot.AddComponent<Match3CheatRowsOverlayView>();
                var legacyCells = new GameObject("PlainTransformOverlayFixture"); legacyCells.transform.SetParent(boardRoot.transform, false);
                Set(legacyView, "cellContainer", legacyCells.transform); legacyView.Build(boardView);
                var upgraded = Get<Transform>(legacyView, "cellContainer");
                Check(legacyView.IsBuilt && upgraded is RectTransform && upgraded == legacyCells.transform && upgraded.childCount == 12 &&
                    upgraded.GetComponent<GridLayoutGroup>() != null,
                    "A legacy plain Transform container upgrades without retaining a destroyed Transform wrapper", report);
                UnityEngine.Object.DestroyImmediate(legacyRoot); UnityEngine.Object.DestroyImmediate(legacyCells);
                Check(Mathf.Abs(Get<float>(trigger, "holdSeconds") - 3) < .001f, "Avatar reveal still requires a three second hold", report);
                trigger.OnPointerDown(null); Set(trigger, "_pressedAt", Time.unscaledTime - 2.9f); Call(trigger, "Update");
                Check(!view.IsVisible, "A shorter hold cannot reveal hidden rows", report);
                Set(trigger, "_pressedAt", Time.unscaledTime - 3.1f); Call(trigger, "Update");
                Check(view.IsVisible, "Three second opponent avatar hold reveals the two rows", report);
                trigger.OnPointerUp(null);
                Check(view.IsVisible, "Releasing the completed reveal hold does not immediately hide the rows", report);
                Call(manager, "EnsureCheatRowsOverlay", new M3BoardSyncMsg { globalBoss = true, cheatRows = rows.Reverse().ToArray() });
                Check(view.IsVisible && Get<int[]>(manager, "_latestCheatRows").SequenceEqual(rows.Reverse()),
                    "Subsequent boss packets refresh privileged rows while preserving visibility", report);
                trigger.OnPointerDown(null); trigger.OnPointerUp(null);
                Check(!view.IsVisible, "A simple avatar tap hides already visible rows", report);
                trigger.OnPointerDown(null); trigger.OnPointerUp(null);
                Check(!view.IsVisible, "A simple avatar tap never reveals hidden rows", report);
                trigger.OnPointerDown(null); trigger.OnPointerExit(null); Set(trigger, "_pressedAt", Time.unscaledTime - 4); Call(trigger, "Update"); trigger.OnPointerUp(null);
                Check(!view.IsVisible, "Moving the pointer away cancels the reveal hold", report);
                trigger.OnPointerDown(null); Set(trigger, "_pressedAt", Time.unscaledTime - 4); Call(trigger, "Update"); trigger.OnPointerUp(null);
                trigger.OnPointerDown(null); Set(trigger, "_pressedAt", Time.unscaledTime - 4); Call(trigger, "Update"); trigger.OnPointerUp(null);
                Check(view.IsVisible, "Holding a visible overlay keeps it visible until a simple tap", report);
                // Destroy uses the player lifecycle; edit-mode fixture removes its
                // trigger first so the actual denied-packet path stays log-clean.
                Set(manager, "_cheatRowsLongPressActivator", null); UnityEngine.Object.DestroyImmediate(trigger.gameObject);
                Call(manager, "EnsureCheatRowsOverlay", new M3BoardSyncMsg { globalBoss = true });
                Check(!view.IsVisible && !Get<bool>(manager, "_hasCheatRowsAccess") && Get<int[]>(manager, "_latestCheatRows") == null,
                    "A denied boss snapshot hides rows and clears their cached content", report);
                // Exercise actual BoardSync coroutine without rendering FX or a
                // socket: ability-only packets retain one live boss decision.
                Set(manager, "_opPanel", null); Set(manager, "_myPanel", null); Set(manager, "_boardView", null);
                Set(manager, "_myUserId", "hero"); Set(manager, "_opUserId", "zz-bot-global");
                Set(manager, "_board", new Match3BoardLogic()); Set(manager, "_myStats", new PlayerStats { hp = 31500 });
                Set(manager, "_opStats", new PlayerStats { hp = 550000 }); Set(manager, "_isMyTurn", false);
                Set(manager, "_isSoloBotMode", true); Set(manager, "_turnEndsAtUnixMs", 130000L);
                M3BoardSyncMsg Packet(long sequence, bool adapt) => new()
                {
                    globalBoss = true, board = Enumerable.Range(0, 36).Select(i => i % 5 + 1).ToArray(),
                    actionActorUserId = "zz-bot-global", activeUserId = "zz-bot-global", actionType = 0,
                    bossActionSequence = sequence, bossOwnTurn = 1, bossAdaptationTriggered = adapt,
                    bossScreamTriggered = !adapt, bossScreamDamage = adapt ? 0 : 6300,
                    aHp = adapt ? 31500 : 25200, aMaxHp = 31500, bHp = 550000, bMaxHp = 550000,
                    aLevel = 12, bLevel = 12, roundNumber = 1, turnEndsAtUnixMs = 130000, turnDurationSeconds = 30
                };
                var adaptation = Packet(1, true); var scream = Packet(2, false);
                Check((bool)Call(manager, "KeepsBossTurnWithoutTimerReset", adaptation) && (bool)Call(manager, "KeepsBossTurnWithoutTimerReset", scream),
                    "Adaptation and scream are recognized as separate free boss actions", report);
                var waits = Pump((IEnumerator)Call(manager, "ApplyRemoteBoardSync", adaptation));
                Check(waits == 1 && Get<PlayerStats>(manager, "_myStats").hp == 31500 && Get<long>(manager, "_lastAdaptationSequence") == 1,
                    "Actual adaptation presentation preserves HP and gives its effect a separate animation interval", report);
                Check(!Get<bool>(manager, "_isMyTurn") && Get<long>(manager, "_turnEndsAtUnixMs") == 130000 && Get<bool>(manager, "_inputBlocked"),
                    "Adaptation does not transition turns restart the timer or unlock player input", report);
                waits = Pump((IEnumerator)Call(manager, "ApplyRemoteBoardSync", scream));
                Check(waits == 1 && Get<PlayerStats>(manager, "_myStats").hp == 25200 && Get<int>(manager, "_lastScreamTurn") == 1,
                    "Actual scream presentation applies its6300 HP packet after adaptation with its own animation interval", report);
                Check(!Get<bool>(manager, "_isMyTurn") && Get<long>(manager, "_turnEndsAtUnixMs") == 130000 && Get<bool>(manager, "_inputBlocked"),
                    "Scream keeps the same boss turn and live deadline before the ordinary move", report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inactive); UnityEngine.Object.DestroyImmediate(root);
                File.WriteAllText(output + "/native-action-rows.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return "PASS boss action/rows: " + report.checks.Count + " actual pointer and BoardSync checks; no network/account actions.";
        }
        private static int Pump(IEnumerator routine)
        {
            var steps = 0; var waits = 0;
            while (routine.MoveNext())
            {
                if (++steps > 100) throw new InvalidOperationException("Unexpected offline coroutine wait.");
                if (routine.Current is WaitForSecondsRealtime) waits++;
                else if (routine.Current is IEnumerator nested) waits += Pump(nested);
            }
            return waits;
        }
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, Hidden).Invoke(target, values);
        private static void Check(bool value, string message, Report report) { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
