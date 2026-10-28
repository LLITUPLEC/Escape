using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using Project.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production reconnect/checkpoint/input/cancel methods with offline transport boundaries, no real account or scene navigation.</summary>
    public static class ExpeditionBattleReconnectQa
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private const string Output = ".cache/qa/reconnect-0317/battle";
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites, preferenceWrites; public string utc, error; public List<string> checks = new(); }
        private sealed class FixtureMatch : global::Nakama.IMatch
        {
            public string Id { get; }
            public FixtureMatch(string id) => Id = id;
            public bool Authoritative => true; public string Label => "offline-authoritative-fixture";
            public IEnumerable<global::Nakama.IUserPresence> Presences => Array.Empty<global::Nakama.IUserPresence>();
            public int Size => 2; public global::Nakama.IUserPresence Self => null;
            public void UpdatePresences(global::Nakama.IMatchPresenceEvent presenceEvent) { }
        }
        private sealed class Packet { public string match; public long op; public M3ActionRequest action; }
        private sealed class FixtureState : global::Nakama.IMatchState
        {
            public string MatchId { get; set; } public long OpCode { get; set; }
            public byte[] State { get; set; } public global::Nakama.IUserPresence UserPresence => null;
        }
        public static void RunBatch()
        { EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single); Debug.Log(Run()); }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Output); var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            var originalScene = SceneManager.GetActiveScene(); var originalContext = SynchronizationContext.Current;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var bootField = Field(typeof(NakamaBootstrap), "<Instance>k__BackingField", S); var previousBoot = bootField.GetValue(null);
            var guardField = Field(typeof(ServerConnectionGuard), "_instance", S); var previousGuard = guardField.GetValue(null);
            var hooks = new[] { Field(typeof(Match3RecoveryService), "EditorTransportOverride", S), Field(typeof(Match3RecoveryService), "EditorRequireOverride", S),
                Field(typeof(DuelMatch3Manager), "EditorJoinOverride", S), Field(typeof(DuelMatch3Manager), "EditorStateTransportOverride", S), Field(typeof(DuelMatch3Manager), "EditorNavigationOverride", S) };
            var previousHooks = hooks.Select(h => h.GetValue(null)).ToArray();
            var launch = typeof(Match3LaunchContext).GetFields(S).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null));
            var terminals = (HashSet<string>)Field(typeof(Match3RecoveryService), "TerminalMatches", S).GetValue(null);
            var terminalOrder = (Queue<string>)Field(typeof(Match3RecoveryService), "TerminalOrder", S).GetValue(null);
            var oldTerminals = terminals.ToArray(); var oldOrder = terminalOrder.ToArray();
            var queue = (ConcurrentQueue<Action>)Field(typeof(MainThreadDispatcher), "Queue", S).GetValue(null);
            var cts = new CancellationTokenSource(); NakamaConnectionConfig config = null;
            try
            {
                Check(queue.IsEmpty, "Reconnect fixture begins with an empty dispatcher", report);
                SynchronizationContext.SetSynchronizationContext(null);
                var root = new GameObject("InactiveReconnectFixture"); root.SetActive(false);
                var boot = root.AddComponent<NakamaBootstrap>(); config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; boot.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                Field(typeof(global::Nakama.Session), "<UserId>k__BackingField", I).SetValue(session, "qa-reconnect-player");
                Set(boot, "<Session>k__BackingField", session); bootField.SetValue(null, boot);
                var socketType = typeof(ServerConnectionGuardQa0312).GetNestedType("FakeSocket", BindingFlags.NonPublic);
                object Socket() => Activator.CreateInstance(socketType, true);
                var socket = Socket(); Set(boot, "<Socket>k__BackingField", socket);
                var guard = root.AddComponent<ServerConnectionGuard>(); Set(guard, "_bootstrap", boot); guardField.SetValue(null, guard);
                var lease = Get<ServerConnectionLease>(guard, "_lease"); lease.Confirm(socket, Time.realtimeSinceStartupAsDouble);
                Check(ServerConnectionGuard.CanPlay, "Connected fixture socket has a real production heartbeat lease without a network client", report);
                var manager = root.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                Set(manager, "_cts", cts); Set(manager, "_offlinePresentationQa", true); Set(manager, "_board", new Match3BoardLogic());
                Set(manager, "_myStats", new PlayerStats()); Set(manager, "_opStats", new PlayerStats());
                Set(manager, "_myUserId", session.UserId); Set(manager, "_opUserId", "zz-bot-mine_5");
                Call(manager, "BindCurrentBattleOwner"); Set(manager, "_match", new FixtureMatch("qa-reconnect-match"));
                Set(manager, "_isSoloBotMode", true); Set(manager, "_hasInitialBoardSync", true);
                var boardRoot = new GameObject("Board", typeof(RectTransform)); boardRoot.transform.SetParent(root.transform, false);
                var board = boardRoot.AddComponent<Match3BoardView>();
                board.cellContainer = new GameObject("Cells", typeof(RectTransform), typeof(GridLayoutGroup)).transform; board.cellContainer.SetParent(boardRoot.transform, false); board.Build();
                Set(manager, "_boardView", board);
                var abilityRoot = new GameObject("AbilityPanel", typeof(RectTransform)); abilityRoot.transform.SetParent(root.transform, false);
                var ability = abilityRoot.AddComponent<Match3AbilityPanel>(); ability.UseExpeditionStyle();
                Button Button(string name) { var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(abilityRoot.transform, false); return go.GetComponent<Button>(); }
                ability.crossButton = Button("Cross"); ability.squareButton = Button("Square"); ability.petardButton = Button("Petard"); ability.shieldButton = Button("Shield"); ability.furyButton = Button("Fury");
                Set(manager, "_abilityPanel", ability); ability.OnCrossClicked += () => Call(manager, "OnCrossClicked");
                var packets = new List<Packet>(); var calls = new List<string>(); var joins = new List<string>(); var navigation = new List<string>();
                hooks[3].SetValue(null, new Func<string, long, byte[], Task>((match, op, data) =>
                { packets.Add(new Packet { match = match, op = op, action = op == 13 ? JsonUtility.FromJson<M3ActionRequest>(System.Text.Encoding.UTF8.GetString(data)) : null }); return Task.CompletedTask; }));
                hooks[4].SetValue(null, new Action<string>(navigation.Add));
                hooks[2].SetValue(null, new Func<string, CancellationToken, Task<global::Nakama.IMatch>>((match, ct) =>
                { joins.Add(match); return Task.FromResult<global::Nakama.IMatch>(new FixtureMatch(match)); }));
                Match3RecoveryReply Descriptor(string id) => new() { ok = true, active = true, started = true, battle_started = true, match_id = id,
                    mode = "pve", bot_id = "mine_5", floor = 5, difficulty = "hard", bot_user_id = "zz-bot-mine_5", opponent_is_bot = true, bot_name = "Монстр" };
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                { calls.Add(rpc); return Task.FromResult(JsonUtility.ToJson(Descriptor(Get<global::Nakama.IMatch>(manager, "_match").Id))); }));
                foreach (var seconds in new[] { 5, 30 })
                {
                    var stale = 0; QueueCallback(manager, "qa-reconnect-match", () => stale++);
                    var connection = new TaskCompletionSource<bool>(); hooks[1].SetValue(null, new Func<CancellationToken, Task>(_ => connection.Task));
                    lease.Revoke(); var before = packets.Count(p => p.op == 13);
                    var recover = (Task)Call(manager, "RecoverCurrentMatchAsync");
                    Call(manager, "OnCellClicked", 0, 0); Call(manager, "OnCellClicked", 1, 0); Call(manager, "OnCrossClicked");
                    Check(Get<bool>(manager, "_networkSnapshotPending") && packets.Count(p => p.op == 13) == before && !ability.crossButton.interactable,
                        "During simulated" + seconds + "s disconnect both actual tile input and abilities remain paused", report);
                    while (queue.TryDequeue(out var pending)) pending(); Check(stale == 0, "Pre-rejoin queued callback cannot cross the new transport generation /" + seconds, report);
                    Call(manager, "OnMatchState", new FixtureState { MatchId = "qa-reconnect-match", OpCode = 10,
                        State = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(Snapshot(session.UserId, seconds))) });
                    Check(queue.IsEmpty && Get<bool>(manager, "_networkSnapshotPending"), "A pre-join board packet is rejected before enqueue and cannot be promoted by a later Join completion /" + seconds, report);
                    socket = Socket(); Set(boot, "<Socket>k__BackingField", socket); lease.Confirm(socket, Time.realtimeSinceStartupAsDouble);
                    connection.SetResult(true); Settle(recover, queue);
                    Check(joins.Last() == "qa-reconnect-match" && Get<bool>(manager, "_networkSnapshotPending") && packets.Any(p => p.op == 16),
                        "Join of the same paid match requests a snapshot and never unlocks input by itself /" + seconds, report);
                    Apply(manager, Snapshot(session.UserId, seconds), report);
                    Check(!Get<bool>(manager, "_networkSnapshotPending") && !Get<bool>(manager, "_inputBlocked") && Get<bool>(manager, "_isMyTurn") && ability.crossButton.interactable,
                        "Actual ApplyRemoteBoardSync checkpoint clears network/input locks and refreshes ability controls /" + seconds, report);
                    Call(manager, "OnCellClicked", 0, 0); Call(manager, "OnCellClicked", 1, 0);
                    Check(packets.Count(p => p.op == 13) == before + 1 && packets.Last(p => p.op == 13).action.actionType == 1,
                        "Actual OnCellClicked sends the next swap after the" + seconds + "s recovery", report);
                    Apply(manager, Snapshot("zz-bot-mine_5", seconds + 1), report);
                    Check(Get<bool>(manager, "_inputBlocked") && !ability.crossButton.interactable, "Opponent response still blocks local controls /" + seconds, report);
                    Apply(manager, Snapshot(session.UserId, seconds + 2), report);
                    ability.crossButton.onClick.Invoke(); Check(Get<AbilityType?>(manager, "_pendingAbility") == AbilityType.Cross, "Production ability button accepts selection on the next own turn /" + seconds, report);
                    Set(manager, "_boardView", null); Pump((IEnumerator)Call(manager, "SendAbilityRequestRoutine", AbilityType.Cross, 0, 0)); Set(manager, "_boardView", board);
                    Check(packets.Count(p => p.op == 13) == before + 2 && packets.Last(p => p.op == 13).action.actionType == 2,
                        "The selected ability emits its actual action request without replaying the old action /" + seconds, report);
                }
                hooks[1].SetValue(null, new Func<CancellationToken, Task>(_ => Task.CompletedTask));
                Set(manager, "_match", new FixtureMatch("qa-cancel-before-ready")); Set(manager, "_gameEnded", false); Set(manager, "_matchRevealPending", true);
                Set(manager, "_admissionRequestId", "qa-pending-admission"); Set(manager, "_admissionMatchId", "qa-cancel-before-ready");
                var admission = new TaskCompletionSource<bool>(); Set(manager, "_pendingLaunchTask", admission.Task);
                var cancelAck = new TaskCompletionSource<string>(); var cancelCalls = 0;
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                { calls.Add(rpc); Check(rpc == Match3RecoveryService.CancelRpc && payload.Contains("qa-pending-admission") && payload.Contains("qa-cancel-before-ready"), "Cancel serializes the exact known admission and match", report); cancelCalls++; return cancelAck.Task; }));
                var reveal = (IEnumerator)Call(manager, "PresentConfirmedMatch"); Check(reveal.MoveNext(), "Production reveal is waiting before its first readiness acknowledgement", report);
                var ready = packets.Count(p => p.op == 17); var cancel = (Task)Call(manager, "CancelPreparationAsync");
                Check(cancelCalls == 0 && navigation.Count == 0 && !Get<bool>(manager, "_gameEnded"), "Cancel waits for an in-flight create acknowledgement before any refund or navigation", report);
                var repeated = (Task)Call(manager, "CancelPreparationAsync"); Check(repeated.IsCompleted && cancelCalls == 0, "A repeated Cancel cannot start a second in-flight cancellation", report);
                admission.SetResult(true); Until(() => cancelCalls == 1, queue);
                Check(cancelCalls == 1 && !cancel.IsCompleted && navigation.Count == 0, "Known admission is cancelled once and scene stays until authoritative acknowledgement", report);
                Pump(reveal); Check(packets.Count(p => p.op == 17) == ready && Get<bool>(manager, "_matchRevealPending"), "Cancelled reveal cannot finish, drain queued boards, or send OP17", report);
                cancelAck.SetResult("{\"ok\":true,\"accepted\":true,\"terminal\":true,\"match_cancelled\":true}"); Settle(cancel, queue);
                Check(Get<bool>(manager, "_gameEnded") && navigation.Count == 1 && cancelCalls == 1 && !calls.Any(c => c == "duel_match3_forfeit"), "Only a terminal cancel acknowledgement leaves the scene; forfeit is never substituted", report);
                Set(manager, "_match", new FixtureMatch("qa-cancel-first-action-race")); Set(manager, "_gameEnded", false);
                Set(manager, "_cancelPreparationIntent", false); Set(manager, "_matchRevealPending", true); Set(manager, "_pendingLaunchTask", Task.CompletedTask);
                Set(manager, "_admissionRequestId", "qa-start-race");
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                { calls.Add(rpc); return Task.FromResult("{\"ok\":false,\"err\":\"battle_already_started\"}"); }));
                var denied = (Task)Call(manager, "CancelPreparationAsync"); Settle(denied, queue);
                Check(!Get<bool>(manager, "_gameEnded") && !Get<bool>(manager, "_cancelPreparationIntent") && Get<bool>(manager, "_networkSnapshotPending") && navigation.Count == 1,
                    "Server first-action win denies cancellation and returns to snapshot recovery without leaving or pretending a refund", report);
                Check(!calls.Contains("duel_match3_forfeit"), "A denied cancellation never silently turns into surrender", report);
                void Variant(string matchId)
                {
                    Set(manager, "_match", new FixtureMatch(matchId)); Set(manager, "_gameEnded", false);
                    Set(manager, "_cancelPreparationIntent", false); Set(manager, "_matchRevealPending", true);
                    Set(manager, "_admissionRequestId", matchId + "-receipt"); Set(manager, "_pendingLaunchTask", Task.CompletedTask);
                }
                Variant("qa-unrecorded-refund"); var beforeNavigation = navigation.Count;
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                    Task.FromResult("{\"ok\":true,\"accepted\":true,\"terminal\":true,\"match_cancelled\":true,\"refund_pending\":true,\"refund_recorded\":false}")));
                var unrecorded = (Task)Call(manager, "CancelPreparationAsync"); Settle(unrecorded, queue);
                Check(!Get<bool>(manager, "_gameEnded") && navigation.Count == beforeNavigation && Get<bool>(manager, "_cancelPreparationIntent"),
                    "A pending refund without durable credit acknowledgement never permits scene exit", report);
                Variant("qa-write-failed-refund");
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) => Task.FromResult("{\"ok\":false,\"err\":\"write_failed\"}")));
                var writeFailed = (Task)Call(manager, "CancelPreparationAsync"); Settle(writeFailed, queue);
                Check(!Get<bool>(manager, "_gameEnded") && navigation.Count == beforeNavigation, "A failed canonical cancellation write cannot be presented as a successful refund or leave", report);
                Variant("qa-recorded-cap-refund");
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                    Task.FromResult("{\"ok\":true,\"accepted\":true,\"terminal\":true,\"match_cancelled\":true,\"refund_pending\":true,\"refund_recorded\":true}")));
                var recorded = (Task)Call(manager, "CancelPreparationAsync");
                Check(!recorded.IsCompleted && navigation.Count == beforeNavigation, "A durable cap-limited refund shows its saved-credit notice before leaving", report);
                Settle(recorded, queue);
                Check(Get<bool>(manager, "_gameEnded") && navigation.Count == beforeNavigation + 1,
                    "Terminal plus canonical durable refund credit allows exit even while the capped remainder awaits room", report);
                Set(manager, "_gameEnded", false); Set(manager, "_cancelPreparationIntent", false);
                Set(manager, "_match", null); Set(manager, "_preferredSoloFloor", 11);
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                { calls.Add(rpc); return Task.FromResult(JsonUtility.ToJson(Descriptor("qa-cold-existing-paid-match"))); }));
                var cold = (Task<bool>)Call(manager, "TryRestoreExistingMatchAsync", CancellationToken.None, false); Settle(cold, queue);
                Check(cold.Result && Get<global::Nakama.IMatch>(manager, "_match").Id == "qa-cold-existing-paid-match" && Get<int>(manager, "_activePveBotFloor") == 5,
                    "Cold/next-create recovery joins the existing paid match and its actual floor instead of paying for selected floor11", report);
                Check(calls.All(c => c == Match3RecoveryService.RecoverRpc || c == Match3RecoveryService.CancelRpc) && Get<bool>(manager, "_networkSnapshotPending"),
                    "Recovery/cancellation emit no create, wager, resource spend or forfeit RPC; cold join still waits for checkpoint", report);
                Match3LaunchContext.ClearRecoveredMatch(); Set(manager, "_match", null);
                Match3RecoveryReply PendingRace(string request = "qa-paid-race") => new()
                    { ok = true, pending_admission = true, mode = "pvp", pvp_race = true, request_id = request };
                Match3RecoveryReply pendingReply = PendingRace();
                hooks[0].SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, payload, ct) =>
                { calls.Add(rpc); return Task.FromResult(JsonUtility.ToJson(pendingReply)); }));
                Set(manager, "_isSoloBotMode", false); Set(manager, "_pvpRaceQueue", true); Set(manager, "_admissionRequestId", null);
                var beforeRaceJoins = joins.Count;
                var prepaidRace = (Task<bool>)Call(manager, "TryRestoreExistingMatchAsync", CancellationToken.None, true); Settle(prepaidRace, queue);
                Check(!prepaidRace.Result && Get<string>(manager, "_admissionRequestId") == "qa-paid-race" && joins.Count == beforeRaceJoins,
                    "Explicit Race launch adopts its confirmed paid receipt and continues matchmaking without recovery_unconfirmed, a new debit or a premature Join", report);
                pendingReply = PendingRace("qa-lost-create-request");
                var retryRace = (Task<bool>)Call(manager, "TryRestoreExistingMatchAsync", CancellationToken.None, true); Settle(retryRace, queue);
                Check(!retryRace.Result && Get<string>(manager, "_admissionRequestId") == "qa-lost-create-request",
                    "Retry of an unbound Race admission preserves its existing exact request identity for fallback creation and cancellation", report);
                void PendingBlocked(string reason, Match3RecoveryReply reply, bool explicitLaunch = true, bool raceQueue = true, bool solo = false)
                {
                    pendingReply = reply; Set(manager, "_pvpRaceQueue", raceQueue); Set(manager, "_isSoloBotMode", solo);
                    var blocked = (Task<bool>)Call(manager, "TryRestoreExistingMatchAsync", CancellationToken.None, explicitLaunch);
                    var rejected = false;
                    try { Settle(blocked, queue); }
                    catch (InvalidOperationException e) when (e.Message == "admission_pending") { rejected = true; }
                    Check(rejected && joins.Count == beforeRaceJoins, reason, report);
                }
                PendingBlocked("Background discovery cannot turn a pending Race payment into a fresh launch", PendingRace(), false);
                PendingBlocked("A non-Race launch cannot adopt a pending Race payment", PendingRace(), raceQueue: false);
                PendingBlocked("A solo mine launch cannot adopt a pending Race payment", PendingRace(), solo: true);
                PendingBlocked("An untyped pending mine reservation still blocks creation", new Match3RecoveryReply { ok = true, pending_admission = true, request_id = "mine-request" });
                var pendingMatch = PendingRace(); pendingMatch.match_id = "match-being-created";
                PendingBlocked("A Race match already being created is never treated as an unbound queue receipt", pendingMatch);
                PendingBlocked("A pending receipt without an exact cancellation reference cannot continue the queue", PendingRace(string.Empty));
                var activePending = PendingRace(); activePending.active = true;
                PendingBlocked("Conflicting active/pending descriptors cannot start another queue", activePending);
                var terminalPending = PendingRace(); terminalPending.terminal = true;
                PendingBlocked("A terminal pending descriptor cannot start another queue", terminalPending);
                Check(boot.Client == null && !root.activeInHierarchy, "All checks use disabled controllers and offline transport; no real authentication/account/preferences", report);
                report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); }
            finally
            {
                for (var i = 0; i < hooks.Length; i++) hooks[i].SetValue(null, previousHooks[i]);
                foreach (var f in launch) f.Key.SetValue(null, f.Value);
                terminals.Clear(); foreach (var key in oldTerminals) terminals.Add(key); terminalOrder.Clear(); foreach (var key in oldOrder) terminalOrder.Enqueue(key);
                bootField.SetValue(null, previousBoot); guardField.SetValue(null, previousGuard);
                Drain(queue); SynchronizationContext.SetSynchronizationContext(originalContext); cts.Cancel(); cts.Dispose();
                EditorSceneManager.CloseScene(scene, true); if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
                File.WriteAllText(Output + "/battle-reconnect-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return "PASS battle reconnect: " + report.checks.Count + " actual checkpoint/input/reveal/cancel checks; simulated5s/30s outages, no real account/network actions.";
        }
        private static M3BoardSyncMsg Snapshot(string active, int order) => new()
        {
            board = Enumerable.Range(0, 36).Select(i => i % 5 + 1).ToArray(), activeUserId = active, roundNumber = 8,
            serverNowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + order, turnEndsAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30000,
            aHp = 150, bHp = 150, aMaxHp = 150, bMaxHp = 150, aMana = 100, bMana = 100, aLevel = 1, bLevel = 1, turnDurationSeconds = 30
        };
        private static void Apply(DuelMatch3Manager manager, M3BoardSyncMsg msg, Report report)
        {
            Set(manager, "_awaitingPresentationSnapshot", true); Set(manager, "_snapshotTransportReady", true); Set(manager, "_matchRevealPending", true);
            Call(manager, "OnBoardSyncReceived", msg);
            var pending = Get<Queue<M3BoardSyncMsg>>(manager, "_pendingBoardSyncs"); Check(pending.Count == 1, "Authoritative checkpoint passes production duplicate/recovery queue gate", report);
            var accepted = pending.Dequeue(); Set(manager, "_matchRevealPending", false); Pump((IEnumerator)Call(manager, "ApplyRemoteBoardSync", accepted));
        }
        private static void QueueCallback(DuelMatch3Manager manager, string match, Action action) =>
            typeof(DuelMatch3Manager).GetMethod("QueueMatchCallback", I, null, new[] { typeof(string), typeof(Action) }, null).Invoke(manager, new object[] { match, action });
        private static void Pump(IEnumerator routine)
        { var n = 0; while (routine.MoveNext()) { if (++n > 200) throw new InvalidOperationException("Unexpected presentation wait."); if (routine.Current is IEnumerator nested) Pump(nested); } }
        private static void Drain(ConcurrentQueue<Action> queue) { while (queue.TryDequeue(out var action)) action(); }
        private static void Settle(Task task, ConcurrentQueue<Action> queue)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 3) { Drain(queue); Thread.Sleep(1); }
            Drain(queue); if (!task.IsCompleted) throw new InvalidOperationException("Offline asynchronous operation did not settle."); task.GetAwaiter().GetResult();
        }
        private static void Until(Func<bool> condition, ConcurrentQueue<Action> queue)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && watch.Elapsed.TotalSeconds < 3) { Drain(queue); Thread.Sleep(1); }
            if (!condition()) throw new InvalidOperationException("Offline boundary did not become ready.");
        }
        private static FieldInfo Field(Type type, string name, BindingFlags flags) => type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name);
        private static T Get<T>(object target, string name) => (T)Field(target.GetType(), name, I).GetValue(target);
        private static void Set(object target, string name, object value) => Field(target.GetType(), name, I).SetValue(target, value);
        private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, I).Invoke(target, values);
        private static void Check(bool condition, string message, Report report) { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
