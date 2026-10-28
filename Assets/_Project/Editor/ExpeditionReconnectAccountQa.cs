using System;
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
using Project.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Editor
{
    /// <summary>Actual recovery/cancel wire contract and delayed authenticated lifetime checks. Only connection/transport are intercepted.</summary>
    public static class ExpeditionReconnectAccountQa
    {
        private const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Output = ".cache/qa/reconnect-0317/account";
        [Serializable] private sealed class Wire { public string match_id, request_id; public int session_epoch; }
        [Serializable] private sealed class Trace { public string rpc, json; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, preferenceWrites, accountWrites;
            public string utc, error;
            public List<string> checks = new();
            public List<Trace> actualSerializedRequests = new();
        }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single); Debug.Log(Run());
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Output); var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            var originalScene = SceneManager.GetActiveScene(); var originalContext = SynchronizationContext.Current;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = "MainMenu"; SceneManager.SetActiveScene(scene);
            var bootField = Field(typeof(NakamaBootstrap), "<Instance>k__BackingField", S); var previousBoot = bootField.GetValue(null);
            var epochField = Field(typeof(NakamaBootstrap), "_localSessionEpoch", S); var previousEpoch = epochField.GetValue(null);
            var transportField = Field(typeof(Match3RecoveryService), "EditorTransportOverride", S); var previousTransport = transportField.GetValue(null);
            var requireField = Field(typeof(Match3RecoveryService), "EditorRequireOverride", S); var previousRequire = requireField.GetValue(null);
            var launchFields = typeof(Match3LaunchContext).GetFields(S).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null));
            var terminals = (HashSet<string>)Field(typeof(Match3RecoveryService), "TerminalMatches", S).GetValue(null);
            var terminalOrder = (Queue<string>)Field(typeof(Match3RecoveryService), "TerminalOrder", S).GetValue(null);
            var oldTerminals = terminals.ToArray(); var oldOrder = terminalOrder.ToArray();
            var queue = (ConcurrentQueue<Action>)Field(typeof(MainThreadDispatcher), "Queue", S).GetValue(null);
            NakamaConnectionConfig config = null;
            try
            {
                Check(queue.IsEmpty, "Fresh offline QA owns an empty dispatcher queue before creating callbacks", report);
                SynchronizationContext.SetSynchronizationContext(null);
                Match3LaunchContext.ClearRecoveredMatch();
                VerifyGate(report);
                var identity = new GameObject("DisabledRecoveryAccountIdentity"); identity.SetActive(false);
                var boot = identity.AddComponent<NakamaBootstrap>(); config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; boot.Config = config;
                Set(boot, "<Session>k__BackingField", Session("qa-user-a")); bootField.SetValue(null, boot); epochField.SetValue(null, 7);
                var gate = Get<SessionOwnershipClaimGate>(boot, "_ownershipClaim");
                var calls = new List<(string rpc, Wire payload)>();
                Func<string, string, CancellationToken, Task<string>> success = (rpc, json, ct) =>
                {
                    calls.Add((rpc, JsonUtility.FromJson<Wire>(json)));
                    report.actualSerializedRequests.Add(new Trace { rpc = rpc, json = json });
                    return Task.FromResult(rpc == Match3RecoveryService.CancelRpc ? "{\"ok\":true,\"accepted\":true,\"terminal\":true,\"match_cancelled\":true}" : "{\"ok\":true,\"active\":false}");
                };
                transportField.SetValue(null, success); requireField.SetValue(null, new Func<CancellationToken, Task>(_ => Task.CompletedTask));
                var recovery = Match3RecoveryService.RecoverAsync("qa-match-a", "qa-request-a", CancellationToken.None); Settle(recovery, queue);
                Check(recovery.Result.ok && calls.Count == 1 && calls[0].rpc == "duel_match3_recover" && calls[0].payload.match_id == "qa-match-a" && calls[0].payload.request_id == "qa-request-a" && calls[0].payload.session_epoch == 7,
                    "Actual RecoverAsync serializes authenticated epoch and exact optional identities to the read-only recovery RPC", report);
                var cancel = Match3RecoveryService.CancelAsync("qa-match-a", "qa-request-a", CancellationToken.None); Settle(cancel, queue);
                Check(cancel.Result.terminal && cancel.Result.match_cancelled && calls.Count == 2 && calls[1].rpc == "duel_match3_cancel" && calls[1].payload.request_id == "qa-request-a",
                    "Actual CancelAsync uses the acknowledgement RPC and parses a terminal cancellation without a fresh launch", report);
                var cold = Match3RecoveryService.RecoverAsync(null, null, CancellationToken.None); Settle(cold, queue);
                var raw = report.actualSerializedRequests.Last();
                Check(cold.Result.ok && raw.rpc == Match3RecoveryService.RecoverRpc && !raw.json.Contains("\"match_id\"") && !raw.json.Contains("\"request_id\"") && calls.Last().payload.session_epoch == 7,
                    "Actual cold-start Recover(null,null) JSON physically omits optional identity fields instead of serializing rejected empty strings", report);
                var requestOnly = Match3RecoveryService.CancelAsync(null, "qa-pending-request", CancellationToken.None); Settle(requestOnly, queue); raw = report.actualSerializedRequests.Last();
                Check(raw.rpc == Match3RecoveryService.CancelRpc && !raw.json.Contains("\"match_id\"") && raw.json.Contains("\"request_id\":\"qa-pending-request\"") && calls.Last().payload.session_epoch == 7,
                    "Actual request-only cancellation JSON supplies a valid request identity and physically omits match_id", report);
                var matchOnly = Match3RecoveryService.RecoverAsync("qa-match-only", null, CancellationToken.None); Settle(matchOnly, queue); raw = report.actualSerializedRequests.Last();
                Check(raw.json.Contains("\"match_id\":\"qa-match-only\"") && !raw.json.Contains("\"request_id\""),
                    "Actual match-only recovery JSON physically omits request_id; all four wire variants are exercised through production transport", report);

                var beforeRequire = new TaskCompletionSource<bool>(); var count = calls.Count;
                requireField.SetValue(null, new Func<CancellationToken, Task>(_ => beforeRequire.Task));
                var staleBeforeSend = Match3RecoveryService.CancelAsync("qa-stale-cancel", null, CancellationToken.None);
                Check(!staleBeforeSend.IsCompleted && calls.Count == count, "Cancel waits for connection before sending any transport request", report);
                var priorOwner = Match3AccountOperation.Capture(); gate.BeginAuthentication();
                beforeRequire.SetResult(true); Settle(staleBeforeSend, queue, true);
                Check(calls.Count == count && !priorOwner.IsSameAuthentication, "Same-user relogin during Require cannot turn an old Cancel into a new-session server side effect", report);

                var tokenRefresh = new TaskCompletionSource<bool>(); requireField.SetValue(null, new Func<CancellationToken, Task>(_ => tokenRefresh.Task));
                var beforeRefresh = Match3AccountOperation.Capture(); var refreshed = Match3RecoveryService.RecoverAsync(null, null, CancellationToken.None);
                Set(boot, "<Session>k__BackingField", Session("qa-user-a")); tokenRefresh.SetResult(true); Settle(refreshed, queue);
                Check(!beforeRefresh.IsCurrent && beforeRefresh.IsSameAuthentication && refreshed.Result.ok && calls.Count == count + 1,
                    "A legitimate token object refresh in the same authentication lifetime remains recoverable", report);
                requireField.SetValue(null, new Func<CancellationToken, Task>(_ => Task.CompletedTask));

                var delayed = new TaskCompletionSource<string>();
                transportField.SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, json, ct) => delayed.Task));
                var oldAccount = Match3AccountOperation.Capture(); var lateRecovery = Match3RecoveryService.RecoverAsync(null, null, CancellationToken.None);
                gate.BeginAuthentication(); Set(boot, "<Session>k__BackingField", Session("qa-user-b"));
                delayed.SetResult("{\"ok\":true,\"active\":true,\"match_id\":\"qa-old-match\",\"mode\":\"pve\"}"); Settle(lateRecovery, queue, true);
                Check(!oldAccount.IsCurrent && !Match3LaunchContext.TryPeekRecoveredMatch(out _), "Late other-account active reply is cancelled before it can arm a launch", report);
                delayed = new TaskCompletionSource<string>(); var lateSameUser = Match3RecoveryService.CancelAsync("qa-old-match", null, CancellationToken.None);
                gate.BeginAuthentication(); delayed.SetResult("{\"ok\":true,\"terminal\":true,\"match_cancelled\":true}"); Settle(lateSameUser, queue, true);
                Check(lateSameUser.IsCanceled, "A late acknowledgement from a previous login of the same user cannot become current data", report);

                count = calls.Count; transportField.SetValue(null, success);
                gate.BeginAuthentication(); Set(boot, "<Session>k__BackingField", null);
                var loggedOut = Match3RecoveryService.RecoverAsync(null, null, CancellationToken.None); Settle(loggedOut, queue, true);
                Check(calls.Count == count, "A logged-out operation fails before connection or transport", report);
                Set(boot, "<Session>k__BackingField", Session("qa-user-b"));

                VerifyLaunch(boot, gate, calls, report);
                VerifyCoordinator(boot, transportField, queue, scene, report);
                Check(boot.Client == null && boot.Socket == null && !identity.activeInHierarchy && queue.IsEmpty, "All tests leave no authenticated client/socket or queued gameplay/account callbacks", report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                transportField.SetValue(null, previousTransport); requireField.SetValue(null, previousRequire);
                foreach (var pair in launchFields) pair.Key.SetValue(null, pair.Value);
                terminals.Clear(); foreach (var key in oldTerminals) terminals.Add(key);
                terminalOrder.Clear(); foreach (var key in oldOrder) terminalOrder.Enqueue(key);
                bootField.SetValue(null, previousBoot); epochField.SetValue(null, previousEpoch);
                SynchronizationContext.SetSynchronizationContext(originalContext);
                EditorSceneManager.CloseScene(scene, true); if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
                File.WriteAllText(Output + "/account-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return "PASS " + report.checks.Count + " recovery account/wire/lifetime checks; no network, account or preference writes. " + Output;
        }

        private static void VerifyGate(Report report)
        {
            var gate = new SessionOwnershipClaimGate(); var generation = gate.Generation;
            Check(gate.NeedsClaim("a") && !gate.NeedsClaim(null), "Only a real authenticated user needs an initial ownership claim", report);
            Check(gate.Acknowledge("a", generation) && !gate.NeedsClaim("a"), "One accepted claim acknowledges that authentication lifetime", report);
            for (var i = 0; i < 10; i++) Check(!gate.NeedsClaim("a") && gate.Generation == generation, "Socket reconnect does not re-claim or bump authentication generation " + i, report);
            var next = gate.BeginAuthentication(); Check(next > generation && gate.NeedsClaim("a") && !gate.Acknowledge("a", generation), "Explicit same-user login rejects an old claim acknowledgement", report);
            Check(gate.Acknowledge("a", next) && !gate.NeedsClaim("a"), "New explicit-login claim can settle exactly its own generation", report);
            var logout = gate.BeginAuthentication(); Check(!gate.Acknowledge("a", next) && logout > next && gate.NeedsClaim("b"), "Logout invalidates delayed acknowledgements and the next account starts unclaimed", report);
        }

        private static void VerifyLaunch(NakamaBootstrap boot, SessionOwnershipClaimGate gate, List<(string rpc, Wire payload)> calls, Report report)
        {
            var owner = Match3AccountOperation.Capture(); var count = calls.Count;
            Match3RecoveryReply Reply() => new() { ok = true, active = true, match_id = "qa-durable-match", mode = "pve", bot_id = "mine_5", floor = 5, difficulty = "hard" };
            var terminal = Reply(); terminal.terminal = true; Check(!Match3RecoveryService.CanRestore(terminal, owner.OwnerScope, owner.Generation), "Terminal replies cannot restore a battle", report);
            var practice = Reply(); practice.global_boss_practice = true; Check(!Match3RecoveryService.CanRestore(practice, owner.OwnerScope, owner.Generation), "Practice attempts cannot become durable recovered joins", report);
            practice = Reply(); practice.mode = "global_boss_practice"; Check(!Match3RecoveryService.CanRestore(practice, owner.OwnerScope, owner.Generation), "Legacy practice mode is also excluded from recovery", report);
            var finished = Reply(); Match3RecoveryService.RememberTerminal(finished.match_id);
            Check(!Match3RecoveryService.CanRestore(finished, owner.OwnerScope, owner.Generation), "Known terminal match is deduplicated inside this owner lifetime", report);
            var live = Reply(); live.match_id = "qa-current-match";
            Check(Match3LaunchContext.ArmRecoveredMatch(live, owner.OwnerScope, owner.Generation), "A current account can arm only a server-confirmed durable descriptor", report);
            Check(Match3LaunchContext.ConsumeMode() == Match3LaunchMode.SoloBot, "Recovered PvE enters the existing solo presentation mode", report);
            Match3LaunchContext.ConsumeSoloMine(out var bot, out var floor, out var difficulty, out var autoStart);
            Check(bot == "mine_5" && floor == 5 && difficulty == "hard" && !autoStart && calls.Count == count, "Recovered metadata restores its own floor/difficulty with auto-start/payment disabled", report);
            Check(Match3LaunchContext.TryPeekRecoveredMatch(out var armed) && ReferenceEquals(armed, live), "Consuming presentation mode does not lose the authoritative recovered match identity", report);
            gate.BeginAuthentication(); Check(!Match3LaunchContext.TryPeekRecoveredMatch(out _), "A same-user authentication generation change clears an armed recovery before scene entry", report);
            Check(!Match3LaunchContext.ArmRecoveredMatch(live, owner.OwnerScope, owner.Generation), "An old authentication operation cannot re-arm a recovered scene", report);
            owner = Match3AccountOperation.Capture(); Check(Match3LaunchContext.ArmRecoveredMatch(live, owner.OwnerScope, owner.Generation), "Current login can explicitly accept a fresh descriptor", report);
            Match3LaunchContext.SetSoloMine("mine_11", 11, "easy", true, "MineScene");
            Check(!Match3LaunchContext.TryPeekRecoveredMatch(out _), "Explicit new selection cannot retain a hidden earlier recovered descriptor", report);
            Match3LaunchContext.ConsumeSoloMine(out bot, out floor, out difficulty, out autoStart);
            Check(bot == "mine_11" && floor == 11 && difficulty == "easy" && autoStart && calls.Count == count, "Explicit selection retains its own metadata; context manipulation alone never calls a paid RPC", report);
            Match3LaunchContext.ClearRecoveredMatch();
        }

        private static void VerifyCoordinator(NakamaBootstrap boot, FieldInfo transport, ConcurrentQueue<Action> queue, Scene scene, Report report)
        {
            var host = new GameObject("DisabledColdRecoveryCoordinator"); host.SetActive(false);
            var coordinator = host.AddComponent<Match3RecoveryCoordinator>(); var lifetime = new CancellationTokenSource(); Set(coordinator, "_lifetime", lifetime);
            var restore = typeof(Match3RecoveryCoordinator).GetMethod("RestoreAsync", I);
            try
            {
                Set(coordinator, "_checking", true);
                transport.SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, json, ct) => Task.FromException<string>(new InvalidOperationException("offline test failure"))));
                var failed = (Task)restore.Invoke(coordinator, null); Settle(failed, queue);
                Check(!Get<bool>(coordinator, "_checking") && Get<float>(coordinator, "_nextAttempt") > Time.unscaledTime, "RPC failure releases the coordinator lock and schedules a bounded retry", report);
                transport.SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, json, ct) => Task.FromResult("{\"ok\":true,\"pending_admission\":true}")));
                Set(coordinator, "_checking", true); var pending = (Task)restore.Invoke(coordinator, null); Settle(pending, queue);
                Check(!Get<bool>(coordinator, "_checking") && Get<string>(coordinator, "_checkedOwner") == null, "Unresolved paid admission is retried without launching or marking recovery complete", report);
                var late = new TaskCompletionSource<string>(); transport.SetValue(null, new Func<string, string, CancellationToken, Task<string>>((rpc, json, ct) => late.Task));
                Set(coordinator, "_checking", true); var navigation = (Task)restore.Invoke(coordinator, null); scene.name = "QaUserNavigatedAway";
                late.SetResult("{\"ok\":true,\"active\":true,\"match_id\":\"qa-late-scene\",\"mode\":\"pve\"}"); Settle(navigation, queue);
                Check(!Get<bool>(coordinator, "_checking") && !Match3LaunchContext.TryPeekRecoveredMatch(out _) && SceneManager.GetActiveScene().name == "QaUserNavigatedAway",
                    "A cold-start response after scene navigation cannot replace the user's current scene", report);
                scene.name = "MainMenu";
            }
            finally { lifetime.Cancel(); lifetime.Dispose(); Set(coordinator, "_lifetime", null); UnityEngine.Object.DestroyImmediate(host); }
        }
        private static global::Nakama.ISession Session(string uid)
        {
            var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
            Field(typeof(global::Nakama.Session), "<UserId>k__BackingField", I).SetValue(session, uid); return session;
        }
        private static void Settle(Task task, ConcurrentQueue<Action> queue, bool cancelled = false)
        {
            for (var i = 0; i < 16 && !task.IsCompleted; i++) while (queue.TryDequeue(out var action)) action();
            if (!task.IsCompleted) throw new InvalidOperationException("Offline continuation did not settle; no blocking wait is allowed.");
            try { task.GetAwaiter().GetResult(); if (cancelled) throw new InvalidOperationException("Expected authenticated-lifetime cancellation."); }
            catch (OperationCanceledException) when (cancelled) { }
        }
        private static FieldInfo Field(Type owner, string name, BindingFlags flags) => owner.GetField(name, flags) ?? throw new MissingFieldException(owner.FullName, name);
        private static T Get<T>(object owner, string name) => (T)Field(owner.GetType(), name, I).GetValue(owner);
        private static void Set(object owner, string name, object value) => Field(owner.GetType(), name, I).SetValue(owner, value);
        private static void Check(bool ok, string message, Report report) { if (!ok) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
