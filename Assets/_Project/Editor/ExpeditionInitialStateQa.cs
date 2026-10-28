using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Project.Character;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Uses the shipped hydration, loading-cover, connection-notice and tournament-footer code; no transport, credentials or preferences.</summary>
    public static class ExpeditionInitialStateQa
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites, preferenceWrites;
            public string utc;
            public List<string> checks = new();
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var instance = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S);
            var previousBootstrap = instance.GetValue(null);
            var witness = typeof(ExpeditionResourceHeader).GetField("_hydratedAccountScope", S);
            var previousWitness = witness.GetValue(null);
            NakamaConnectionConfig config = null;
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                var bootRoot = new GameObject("DisabledOfflineBootstrap"); bootRoot.SetActive(false);
                var bootstrap = bootRoot.AddComponent<NakamaBootstrap>();
                config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid";
                bootstrap.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                var userId = typeof(global::Nakama.Session).GetField("<UserId>k__BackingField", I);
                userId.SetValue(session, "11111111-2222-3333-4444-555555555555");
                Set(bootstrap, "<Session>k__BackingField", session); instance.SetValue(null, bootstrap);
                witness.SetValue(null, null);
                var scope = LocalAccountCacheScope.Current;
                Check(scope != null && bootstrap.Client == null && !bootRoot.activeInHierarchy, "Disabled bootstrap has an account scope without running authentication", report);
                Check(!bootstrap.InitialSetupCompleted && !bootstrap.InitialSetupFailed, "Pending initial setup is neither successful nor failed", report);

                var headerRoot = new GameObject("OfflineHydrationHeader"); headerRoot.SetActive(false);
                var header = headerRoot.AddComponent<ExpeditionResourceHeader>();
                foreach (var field in new[] { "_name", "_energy", "_gold", "_ore", "_level" })
                {
                    var label = new GameObject(field, typeof(RectTransform), typeof(TextMeshProUGUI));
                    label.transform.SetParent(headerRoot.transform, false); Set(header, field, label.GetComponent<TMP_Text>());
                }
                Set(header, "_accountScope", scope); Set(header, "_profileVersion", 7);
                Invoke(header, "RenderResources", new object[] { null });
                Check(Label(header, "_energy") == "—" && Label(header, "_gold") == "—" && Label(header, "_ore") == "—", "Unhydrated resources show dashes without loading a persisted default", report);
                var profile = JsonUtility.FromJson<CharacterGetRpcResponse>("{\"ok\":true,\"progression\":{\"level\":12,\"xp\":0}}");
                var name = JsonUtility.FromJson<NicknameStatusRpcResponse>("{\"ok\":true,\"username\":\"Шахтёр QA\"}");
                var resources = JsonUtility.FromJson<PlayerResourcesRpcResponse>("{\"ok\":true,\"energy\":37,\"gold\":246,\"ore\":19}");
                bool Apply(CharacterGetRpcResponse p, NicknameStatusRpcResponse n, PlayerResourcesRpcResponse r, string owner, int version, CancellationToken token) =>
                    (bool)Invoke(header, "TryApplyHydration", new object[] { p, n, r, owner, version, token });
                Check(!Apply(null, name, resources, scope, 7, default) && !Apply(profile, null, resources, scope, 7, default) && !Apply(profile, name, null, scope, 7, default), "All three authoritative snapshots are required before hydration", report);
                Check(!Apply(new CharacterGetRpcResponse { ok = true }, name, resources, scope, 7, default) && !Apply(profile, new NicknameStatusRpcResponse { ok = true }, resources, scope, 7, default) && !Apply(profile, name, new PlayerResourcesRpcResponse(), scope, 7, default), "Incomplete or failed RPC DTOs never reveal account data", report);
                Check(!Apply(profile, name, resources, "previous-account", 7, default) && !Apply(profile, name, resources, scope, 6, default) && !Apply(profile, name, resources, scope, 7, new CancellationToken(true)), "Stale account, superseded request and cancelled response are rejected", report);
                Check(!header.HydratedCurrentAccount && !ExpeditionResourceHeader.AccountHasHydratedThisSession && Label(header, "_energy") == "—", "Rejected responses cannot mark loading complete or expose partial values", report);
                Check(Apply(profile, name, resources, scope, 7, default) && header.HydratedCurrentAccount && ExpeditionResourceHeader.AccountHasHydratedThisSession, "A complete same-account server snapshot opens the hydration gate", report);
                Check(Label(header, "_name") == "Шахтёр QA" && Label(header, "_level") == "Уровень 12" && Label(header, "_energy") == "37" && Label(header, "_gold") == "246" && Label(header, "_ore") == "19", "Hydration renders actual name, level and resources from JsonUtility RPC DTOs", report);
                userId.SetValue(session, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
                Check(!header.HydratedCurrentAccount && !ExpeditionResourceHeader.AccountHasHydratedThisSession && !Apply(profile, name, resources, scope, 7, default), "Changing account immediately invalidates the old hydration witness and in-flight data", report);

                VerifyCover(scene, report);
                VerifyConnectionNotice(report);
                VerifyTournamentFooter(report);
                report.passed = true;
                var path = ".cache/qa/startup-ui/initial-state-and-arena-footer.json";
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
                return report.checks.Count + " startup/loading and tournament footer checks passed. No network, account or preference actions. " + Path.GetFullPath(path);
            }
            finally
            {
                instance.SetValue(null, previousBootstrap); witness.SetValue(null, previousWitness);
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(scene, true);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
            }
        }

        private static void VerifyCover(Scene scene, Report report)
        {
            var cover = ExpeditionStartupCover.Show(scene);
            var view = cover.GetComponent<ExpeditionStartupLoadingView>();
            var status = (TMP_Text)Get(view, "_status"); var retry = (Button)Get(view, "_retry");
            Check(cover.GetComponent<Canvas>().sortingOrder == 4500 && status.text == "Загружаем данные героя…" && !retry.gameObject.activeSelf, "Initial cover is neutral, opaque and below independent authentication UI", report);
            Check(cover.transform.Find("OpaqueCover").GetComponent<Image>().raycastTarget, "Loading cover blocks gameplay input before hydration", report);
            var attempts = 0; Action action = () => attempts++;
            for (var i = 0; i < 20; i++) ExpeditionStartupCover.SetLoadingState(cover, true, action);
            retry.onClick.Invoke();
            Check(status.text.Contains("Не удалось") && retry.gameObject.activeSelf && attempts == 1, "Real hydration failure exposes Retry without accumulating per-frame listeners", report);
            ExpeditionStartupCover.SetLoadingState(cover, false, action);
            Check(!retry.gameObject.activeSelf && !status.text.Contains("Не удалось"), "Retry returns the cover to neutral loading state", report);
        }

        private static void VerifyConnectionNotice(Report report)
        {
            var method = typeof(ServerConnectionGuard).GetMethod("ShouldShowNotice", S);
            bool Notice(bool online, bool force, bool failed, bool wasOnline, bool tutorial = false) =>
                (bool)method.Invoke(null, new object[] { online, force, failed, wasOnline, tutorial });
            Check(!Notice(false, false, false, false), "No false connection error while the first connection is pending", report);
            Check(Notice(false, false, true, false), "An actual first connection failure is visible", report);
            Check(Notice(false, false, false, true), "A dropped previously confirmed connection is visible immediately", report);
            Check(!Notice(true, false, true, true) && !Notice(false, false, true, false, true) && Notice(false, true, false, false), "Recovery, tutorial exclusion and explicit blocked-action notice retain their behavior", report);
        }

        private static void VerifyTournamentFooter(Report report)
        {
            var bridgeRoot = new GameObject("DisabledTournamentTransport"); bridgeRoot.SetActive(false);
            var bridge = bridgeRoot.AddComponent<ArenaMatch8Bridge>();
            var root = new GameObject("BracketFooterFixture", typeof(RectTransform));
            Set(bridge, "_bracketRoot", root);
            var summaryRoot = new GameObject("ExpeditionTournamentSummary", typeof(RectTransform), typeof(TextMeshProUGUI));
            summaryRoot.transform.SetParent(root.transform, false); summaryRoot.GetComponent<TMP_Text>().text = "Old verbose status";
            var stateType = typeof(ArenaMatch8Bridge).GetNestedType("ArenaTournamentState", BindingFlags.NonPublic);
            var state = JsonUtility.FromJson("{\"active\":true,\"phase\":\"qf\",\"kind\":\"gold\",\"prize_preview_gold\":320,\"bet_wins\":2,\"bet_losses\":1}", stateType);
            Set(bridge, "_bettingUiEnabled", false); Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            var footer = (TMP_Text)Get(bridge, "_prizeFooterText");
            Check(footer.gameObject.activeSelf && footer.text == "Возможный приз: 320 золота" && footer.fontSize >= 30, "Waiting footer with betting disabled contains only a readable possible prize", report);
            Set(bridge, "_bettingUiEnabled", true); Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            Check(footer.text == "Возможный приз: 320 золота\nСтавки: верно 2 · неверно 1", "Enabled betting adds only correct/incorrect counts", report);
            Set(state, "phase", "countdown"); Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            Check(footer.gameObject.activeSelf && footer.text.Contains("320"), "Countdown and in-round waiting retain the prize", report);
            Set(state, "kind", "smith"); Set(state, "rules_version", 2); Set(state, "prize_ingots", 7);
            Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            Check(footer.text.StartsWith("Возможный приз: 7 слитков + рецепт", StringComparison.Ordinal), "Smith v2 footer displays its authoritative ingots and recipe reward", report);
            Invoke(bridge, "UpdateTournamentSummary", new[] { state });
            Check(!summaryRoot.activeSelf && summaryRoot.GetComponent<TMP_Text>().text == "", "Legacy verbose bottom summary stays hidden", report);
            Set(state, "eliminated", true); Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            Check(!footer.gameObject.activeSelf, "Eliminated tournament does not show an obtainable prize", report);
            Set(state, "eliminated", false); Set(state, "active", false); Invoke(bridge, "UpdatePrizeFooter", new[] { state });
            Check(!footer.gameObject.activeSelf, "Inactive tournament does not show a waiting prize", report);
        }

        private static object Get(object owner, string field) => owner.GetType().GetField(field, I).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, I | BindingFlags.Public).SetValue(owner, value);
        private static string Label(object owner, string field) => ((TMP_Text)Get(owner, field)).text;
        private static object Invoke(object owner, string method, object[] args) => owner.GetType().GetMethod(method, I).Invoke(owner, args);
        private static void Check(bool passed, string message, Report report)
        {
            if (!passed) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }
    }
}
