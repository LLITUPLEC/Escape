using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Project.Achievements;
using Project.Character;
using Project.Nakama;
using Project.Utils;

// Headless regression harness: compile the real storage/services against deterministic API doubles.
// PlayerPrefs deliberately rejects background access, so it detects the original resource-cache bug.
namespace UnityEngine
{
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
    public static class PlayerPrefs
    {
        public static readonly int MainThread = Thread.CurrentThread.ManagedThreadId;
        private static readonly Dictionary<string, string> Values = new();
        private static void Check() { if (Thread.CurrentThread.ManagedThreadId != MainThread) throw new Exception("PlayerPrefs worker-thread access"); }
        public static string GetString(string key, string fallback = "") { Check(); return Values.TryGetValue(key, out var value) ? value : fallback; }
        public static void SetString(string key, string value) { Check(); Values[key] = value; }
        public static void DeleteKey(string key) { Check(); Values.Remove(key); }
        public static void Save() { Check(); }
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => new JavaScriptSerializer().Deserialize<T>(json);
        public static string ToJson(object value) => new JavaScriptSerializer().Serialize(value);
    }
}
namespace Project.Utils
{
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new();
        public static Task<T> RunAsync<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Enqueue(() => { try { result.SetResult(work()); } catch (Exception ex) { result.SetException(ex); } });
            return result.Task;
        }
        public static void Pump() { while (Queue.TryDequeue(out var work)) work(); }
    }
}
namespace Project.Nakama
{
    public sealed class NakamaConnectionConfig
    {
        public string scheme = "https", host = "prokrust-play.ru";
        public int port = 443;
        public string GetScheme() => scheme;
    }
    public sealed class FakeSession { public string UserId; }
    public sealed class FakeRpc { public string Payload; }
    public sealed class FakeClient
    {
        public readonly ConcurrentQueue<TaskCompletionSource<FakeRpc>> Pending = new();
        public Task<FakeRpc> RpcAsync(FakeSession session, string rpcId, string payload, CancellationToken canceller)
        {
            var result = new TaskCompletionSource<FakeRpc>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(result);
            return result.Task;
        }
    }
    public sealed class NakamaBootstrap
    {
        public static NakamaBootstrap Instance = new();
        public NakamaConnectionConfig Config = new();
        public FakeSession Session = new() { UserId = "alice" };
        public FakeClient Client = new();
        public bool IsReady => Session != null;
        public static int GetLocalSessionEpoch() => 1;
        public async Task EnsureConnectedAsync(CancellationToken ct) { await Task.Delay(2, ct).ConfigureAwait(false); }
    }
}
namespace Project.Achievements
{
    public static class AchievementUiPending
    {
        public static void ClearAllAwaitToastTokens() { }
        public static HashSet<string> CaptureEligibleClaimStepTokens() => new();
        public static int RaiseNewEligibleSince(HashSet<string> before) => 0;
    }
    public static class AchievementLifecycle { public static void NotifyDataChanged() { } }
    public static class AchievementStatKeys
    {
        public const string UsesCross="cross", UsesSquare="square", UsesPetard="petard", UsesShield="shield", UsesFury="fury";
        public const string TournamentSmithWinFinal="smith", TournamentOreWinFinal="ore", TournamentGoldWinFinal="gold", DuelTriWin="duel";
        public const string DuelCrossFinisher="crossfin", DuelSquareFinisher="squarefin", DuelPetardFinisher="petardfin", DnnDoubleFivePlusOneTurn="double";
        public const string Line5="line5", Line6="line6", DnnThreeFivePlusStreak="streak", DnnWinAtOneHp="onehp";
    }
}
internal static class ClientCacheRegression
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _checks++;
        Console.WriteLine("PASS " + description);
    }
    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done())
        {
            MainThreadDispatcher.Pump();
            if (DateTime.UtcNow > deadline) throw new Exception("Async test timed out");
            Thread.Sleep(1);
        }
        MainThreadDispatcher.Pump();
    }
    private static TaskCompletionSource<FakeRpc> WaitRequest()
    {
        var client = NakamaBootstrap.Instance.Client;
        PumpUntil(() => !client.Pending.IsEmpty);
        client.Pending.TryDequeue(out var request);
        return request;
    }
    private static PlayerResourcesRpcResponse Finish(Task<PlayerResourcesRpcResponse> task, long gold)
    {
        WaitRequest().SetResult(new FakeRpc { Payload = "{\"ok\":true,\"gold\":" + gold + "}" });
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }
    public static int Main()
    {
        try
        {
            // Trigger the main-thread sentinel before the first worker request.
            UnityEngine.PlayerPrefs.SetString("achievements.stats.v1", "{\"entries\":[{\"k\":\"wins\",\"v\":500}]}");
            UnityEngine.PlayerPrefs.SetString("nakama.cache.player_resources_v1", "{\"ok\":true,\"gold\":999999}");
            Check(AchievementProgressStorage.GetStat("wins") == 0, "unowned legacy achievements ignored");
            Check(!PlayerResourcesService.TryReadCached(out _), "unowned legacy resource cache ignored");
            AchievementProgressStorage.SetStat("wins", 120);
            AchievementProgressStorage.MarkStepClaimed("duel", 0);
            var aliceScope = LocalAccountCacheScope.Current;
            NakamaBootstrap.Instance.Session = new FakeSession { UserId = "bob" };
            Check(AchievementProgressStorage.GetStat("wins") == 0 && !AchievementProgressStorage.IsStepClaimed("duel", 0), "account switch isolates stats and claims");
            NakamaBootstrap.Instance.Session = new FakeSession { UserId = "alice" };
            NakamaBootstrap.Instance.Config.host = "old.example";
            Check(AchievementProgressStorage.GetStat("wins") == 0, "same user id on a different server gets a separate cache");
            NakamaBootstrap.Instance.Config.host = "prokrust-play.ru";
            Check(AchievementProgressStorage.GetStat("wins") == 120 && AchievementProgressStorage.IsStepClaimed("duel", 0), "returning to account restores its own cache");
            AchievementProgressStorage.MergeFromAuthoritativeSnapshot(Array.Empty<AchievementProgressStorage.StringIntPair>(), Array.Empty<string>());
            Check(AchievementProgressStorage.GetStat("wins") == 0 && !AchievementProgressStorage.IsStepClaimed("duel", 0), "authoritative empty arrays remove stale stats and claims");
            AchievementProgressStorage.SetStat("wins", 80);
            AchievementProgressStorage.MarkStepClaimed("duel", 0);
            AchievementProgressStorage.MergeFromAuthoritativeSnapshot(null, null);
            Check(AchievementProgressStorage.GetStat("wins") == 0 && !AchievementProgressStorage.IsStepClaimed("duel", 0), "authoritative null/empty Lua arrays clear stale progress");
            var normalized = AchievementProgressStorage.NormalizeEmptyProgressArrays("{\"ok\":true,\"achievement_stats_flat\": { },\"achievement_claimed\":{},\"other\":{}}");
            Check(normalized == "{\"ok\":true,\"achievement_stats_flat\": [],\"achievement_claimed\":[],\"other\":{}}", "Lua empty objects normalized only for progress arrays");
            var notifications = 0;
            PlayerResourcesService.CachedResourcesChanged += () =>
            {
                if (Thread.CurrentThread.ManagedThreadId != UnityEngine.PlayerPrefs.MainThread) throw new Exception("UI event on worker thread");
                notifications++;
            };
            var get = PlayerResourcesService.GetAsync(CancellationToken.None);
            Check(Finish(get, 15).gold == 15 && PlayerResourcesService.TryReadCached(out var cached) && cached.gold == 15, "worker RPC persists resources and emits UI event on main thread");
            Check(notifications == 1, "successful resource request emits one cache notification");
            get = PlayerResourcesService.GetAsync(CancellationToken.None);
            var stale = WaitRequest();
            PlayerResourcesService.PatchCachedFromProgression(new Progression { gold = 40, localAccountScope = aliceScope });
            stale.SetResult(new FakeRpc { Payload = "{\"ok\":true,\"gold\":15}" });
            PumpUntil(() => get.IsCompleted);
            Check(get.Result.gold == 40 && PlayerResourcesService.TryReadCached(out cached) && cached.gold == 40, "late poll cannot undo a confirmed resource mutation");
            get = PlayerResourcesService.GetAsync(CancellationToken.None);
            stale = WaitRequest();
            NakamaBootstrap.Instance.Session = new FakeSession { UserId = "bob" };
            stale.SetResult(new FakeRpc { Payload = "{\"ok\":true,\"gold\":777}" });
            PumpUntil(() => get.IsCompleted);
            Check(!get.Result.ok && get.Result.err == "stale_session" && !PlayerResourcesService.TryReadCached(out _), "old account response cannot repaint or cache into new account");
            PlayerResourcesService.PatchCachedFromProgression(new Progression { gold = 1000, localAccountScope = aliceScope });
            Check(!PlayerResourcesService.TryReadCached(out _), "old account progression cannot contaminate new resources");
            var first = PlayerResourcesService.GetAsync(CancellationToken.None);
            var pendingFirst = WaitRequest();
            var queuedSpend = PlayerResourcesService.SpendEnergyAsync(1, "test", CancellationToken.None);
            MainThreadDispatcher.Pump();
            NakamaBootstrap.Instance.Session = new FakeSession { UserId = "carol" };
            pendingFirst.SetResult(new FakeRpc { Payload = "{\"ok\":true,\"gold\":1}" });
            PumpUntil(() => first.IsCompleted && queuedSpend.IsCompleted);
            Check(!queuedSpend.Result.ok && queuedSpend.Result.err == "stale_session" && NakamaBootstrap.Instance.Client.Pending.IsEmpty, "queued old-account spend is rejected before issuing an RPC");
            Console.WriteLine("Client cache regression: " + _checks + " checks passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
