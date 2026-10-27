using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.Character;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Offline fixtures use the production daily panel. No account, wallet or network mutations.</summary>
    public static class ExpeditionDailyQa
    {
        private static GameObject _fixture;
        private static Canvas _canvas;
        private static string _mode;
        private static DailyRewardsService.State _snapshot;
        private static bool _badgeOnly;
        [Serializable] private sealed class Result
        {
            public bool passed, offline = true, accountOrNetworkActions;
            public string mode, error, checkedUtc, canvasName, canvasViewport, panelScreenRect;
            public int textCount, buttons, rewardIcons, questCards, progressBars, badgeCount, screenWidth, screenHeight;
            public bool bonusBelowQuests;
        }
        public static string Run(string target)
        {
            if (target == "logic") return Logic();
            if (target == "show-login" || target == "show-login-seven" || target == "show-login-claimed" || target == "show-quests" || target == "show-complete" || target == "show-quests-allready" ||
                target == "show-badge-three" || target == "show-badge-bonus" || target == "show-badge-empty") return Show(target);
            if (target == "verify") return Verify();
            if (target == "restore") { Restore(); return "Daily offline fixture removed."; }
            throw new ArgumentException("Use logic; show-login, show-login-seven, show-login-claimed; show-quests, show-quests-allready, show-complete; show-badge-three, show-badge-bonus, show-badge-empty; verify or restore.");
        }
        private static string Logic()
        {
            Require(DailyRewardsService.Valid(Snapshot(false)), "Valid server fixture was rejected.");
            var invalid = Snapshot(false); invalid.reset_at = invalid.now;
            Require(!DailyRewardsService.Valid(invalid), "Expired server day was accepted.");
            invalid = Snapshot(false); invalid.login.next_day = 8;
            Require(!DailyRewardsService.Valid(invalid), "Eighth cycle day was accepted.");
            var text = ExpeditionDailyPanel.RewardText(new DailyRewardsService.Reward { gold = 150, ore = 100 });
            Require(text.Contains("150") && text.Contains("100") && text.Contains("золота") && text.Contains("руды"), "Reward format lost currency amounts.");
            var type = typeof(DailyRewardsService).GetNestedType("Claim", BindingFlags.NonPublic);
            var request = Activator.CreateInstance(type, true);
            type.GetField("kind").SetValue(request, "login");
            var json = JsonUtility.ToJson(request);
            Require(!json.Contains("quest_id") && json.Contains("\"kind\":\"login\"") && json.Contains("session_epoch"), "Login payload is not strict.");
            var mixed = Snapshot(false); Require(DailyRewardsService.CountReadyRewards(mixed, mixed.now) == 1, "Mixed quest count is incorrect.");
            var ready = Snapshot(false); foreach (var q in ready.quests) { q.ready = true; q.claimed = false; q.progress = q.target; }
            Require(DailyRewardsService.CountReadyRewards(ready, ready.now) == 3, "Three ready quest rewards must show3.");
            var complete = Snapshot(true); Require(DailyRewardsService.CountReadyRewards(complete, complete.now) == 1, "Completion bonus must show1, not three claimed quests.");
            complete.completion_claimed = true; Require(DailyRewardsService.CountReadyRewards(complete, complete.now) == 0, "Claimed bonus is still counted.");
            Require(DailyRewardsService.CountReadyRewards(ready, ready.reset_at) == 0, "Expired snapshot keeps a stale badge.");
            ValidateScopeReset(ready);
            return "PASS11: day/catalog, expiry, cycle boundary, reward amounts, strict login JSON; ready counts1/3, finalbonus1, claimed0, midnight0, foreign account0. No network/account actions.";
        }
        private static string Show(string mode)
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Run in unpaused Play mode."); Restore(); _mode = mode;
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Require(manager != null && !manager.enabled && manager.gameObject.scene.name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal) &&
                UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "Use the isolated offline battle fixture only.");
            var board = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>();
            _canvas = board != null ? board.GetComponentInParent<Canvas>()?.rootCanvas : null;
            Require(_canvas != null && _canvas.gameObject.scene == manager.gameObject.scene, "The existing offline battle canvas is missing.");
            // The Simulator may size a newly-created overlay canvas from its
            // emulated Screen dimensions instead of the actual GameView RT.
            // Reuse the already-settled production battle viewport unchanged.
            _fixture = ExpeditionUi.Rect(_canvas.transform, "DailyQaOffline", 0, 0, 1, 1).gameObject;
            _fixture.transform.SetAsLastSibling();
            ExpeditionUi.Fill(_fixture.transform, "PrivateBackground", 0, 0, 1, 1, new Color(.025f, .055f, .065f));
            _snapshot = Snapshot(mode == "show-complete" || mode == "show-badge-bonus");
            if (mode == "show-login-seven") { _snapshot.login.next_day = 7; _snapshot.login.total_claimed = 6; }
            if (mode == "show-login-claimed") { _snapshot.login.next_day = 1; _snapshot.login.total_claimed = 7; _snapshot.login.can_claim = false; _snapshot.login.claimed_today = true; }
            if (mode == "show-quests-allready" || mode == "show-badge-three") foreach (var q in _snapshot.quests) { q.progress = q.target; q.ready = true; q.claimed = false; }
            if (mode == "show-badge-empty") { foreach (var q in _snapshot.quests) { q.ready = false; q.claimed = true; } _snapshot.completion_claimed = true; }
            _badgeOnly = mode.StartsWith("show-badge-", StringComparison.Ordinal);
            if (_badgeOnly)
            {
                var button = ExpeditionUi.Button(_fixture.transform, "DailyShortcut", "Задания дня", .75f, .755f, .955f, .845f, null, false, 21);
                ExpeditionUi.SetRect(button.GetComponentInChildren<TMP_Text>().rectTransform, .025f, .09f, .975f, .32f);
                var icon = ExpeditionUi.Fill(button.transform, "DailyIcon", .22f, .33f, .78f, .92f, Color.white);
                icon.sprite = Resources.Load<Sprite>("Expedition/Daily/039/daily-quests"); icon.preserveAspect = true;
                var badge = DailyQuestBadge.Bind(button); badge.enabled = false;
                typeof(DailyQuestBadge).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(badge, new object[] { DailyRewardsService.CountReadyRewards(_snapshot, _snapshot.now) });
            }
            else
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(MineRewardFormat.MainItemCatalogAssetPath);
                Require(catalog != null, "The real MainItemCatalog is missing.");
                ExpeditionDailyPanel.Open(_fixture.transform, !mode.StartsWith("show-login", StringComparison.Ordinal), _snapshot, catalog);
            }
            Canvas.ForceUpdateCanvases();
            return "Offline production daily panel: " + mode + ". Call verify after a rendered frame; capture GameView through the existing root runner.";
        }
        private static DailyRewardsService.Reward Reward(int gold = 0, int ore = 0, int matter = 0, string id = null, int amount = 1) =>
            new DailyRewardsService.Reward { gold = gold, ore = ore, matter = matter, items = id == null ? null : new[] { new DailyRewardsService.Item { def_id = id, amount = amount } } };
        private static DailyRewardsService.State Snapshot(bool complete) => new DailyRewardsService.State
        {
            ok = true, day = 20748, now = 1792684800, reset_at = 1792702800,
            login = new DailyRewardsService.Login { next_day = 3, total_claimed = 2, can_claim = true, rewards = new[]
            { Reward(150,100), Reward(id:"ingot_green",amount:3), Reward(matter:2), Reward(id:"elixir_assault"), Reward(300,200),
                new DailyRewardsService.Reward { items = new[] { new DailyRewardsService.Item {def_id="ingot_blue",amount=3}, new DailyRewardsService.Item {def_id="elixir_stone",amount=1} } },
                new DailyRewardsService.Reward { items = new[] { new DailyRewardsService.Item {def_id="scroll_pve_bounty",amount=1}, new DailyRewardsService.Item {def_id="ingot_purple",amount=3}, new DailyRewardsService.Item {def_id="elixir_gambit",amount=1} } } } },
            quests = new[]
            {
                new DailyRewardsService.Quest {id="mine",label="Победить монстров в шахте",progress=5,target=5,ready=!complete,claimed=complete,reward=Reward(100,75)},
                new DailyRewardsService.Quest {id="abilities",label="Применить способности в завершённых боях",progress=complete?6:3,target=6,claimed=complete,reward=Reward(75,50)},
                new DailyRewardsService.Quest {id="pvp",label="Завершить PvP: победить или сделать 3 своих хода",progress=2,target=2,claimed=true,reward=Reward(75,id:"ingot_green",amount:2)},
            },
            completion_ready = complete, completion_reward = Reward(id:"scroll_pve_bounty"),
        };
        private static string Verify()
        {
            var report = new Result { mode = _mode, checkedUtc = DateTime.UtcNow.ToString("O") };
            try
            {
                Require(_fixture != null && _fixture.activeInHierarchy, "Offline fixture is absent."); Canvas.ForceUpdateCanvases();
                Require(_canvas != null, "Existing offline canvas is absent.");
                report.canvasName = _canvas.name; report.canvasViewport = _canvas.pixelRect.ToString();
                report.screenWidth = Screen.width; report.screenHeight = Screen.height;
                var viewport = _canvas.pixelRect;
                Require(Mathf.Abs(viewport.x) < 1 && Mathf.Abs(viewport.y) < 1 && Mathf.Abs(viewport.width - 1080) < 1 && Mathf.Abs(viewport.height - 1920) < 1,
                    "Actual battle viewport must be1080x1920: " + viewport + "; Simulator Screen=" + Screen.width + "x" + Screen.height);
                var view = _fixture.GetComponentInChildren<ExpeditionDailyPanel>();
                var panel = view != null ? view.transform.Find("DailyPanel") as RectTransform : null;
                if (!_badgeOnly)
                {
                    Require(panel != null, "Production daily panel is missing."); report.panelScreenRect = ScreenRect(panel).ToString(); WithinViewport(panel, viewport);
                    var cards = panel.Cast<Transform>().Where(t => t.name.StartsWith("DailyQuestCard", StringComparison.Ordinal)).ToArray();
                    report.questCards = cards.Length;
                    if (!_mode.StartsWith("show-login", StringComparison.Ordinal))
                    {
                        Require(cards.Length == 3, "Expected three separate quest cards.");
                        var bonus = panel.Find("DailyCompletionBonus") as RectTransform; Require(bonus != null, "Completion summary is missing.");
                        report.bonusBelowQuests = ScreenRect(bonus).yMax < cards.Min(t => ScreenRect((RectTransform)t).yMin);
                        Require(report.bonusBelowQuests, "Completion bonus is not below all three quests.");
                        var bars = panel.GetComponentsInChildren<Image>().Where(i => i.name == "DailyQuestProgressFill").ToArray(); report.progressBars = bars.Length;
                        Require(bars.Length == 3, "Progress bars are missing.");
                        for (var i = 0; i < 3; i++) Require(bars[i].type == Image.Type.Filled && Mathf.Approximately(bars[i].fillAmount, Mathf.Clamp01((float)_snapshot.quests[i].progress / _snapshot.quests[i].target)), "Progress bar disagrees with server snapshot.");
                    }
                    else
                    {
                        Require(panel.Cast<Transform>().Count(t => t.name.StartsWith("LoginDayCard", StringComparison.Ordinal)) == 7, "Seven login cards are missing.");
                        if (_mode == "show-login-seven" || _mode == "show-login-claimed")
                            Require(panel.Find("DailyCurrentReward").GetComponentsInChildren<Image>().Count(i => i.name == "DailyRewardIcon") == 3, "Day7 current reward does not display all three items.");
                    }
                    var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(MineRewardFormat.MainItemCatalogAssetPath);
                    foreach (var image in panel.GetComponentsInChildren<Image>())
                    {
                        if (image.name == "QuestEmblem" || image.name == "CompletionEmblem") Require(image.sprite != null, "Missing card emblem: " + image.name);
                        if (image.name != "DailyRewardIcon") continue;
                        report.rewardIcons++; Require(image.sprite != null, "Missing real reward icon: " + image.transform.parent.name);
                        var id = image.transform.parent.name.Substring("DailyReward-".Length);
                        var expected = id == "gold" || id == "ore" || id == "matter" ? ExpeditionResourceIcons.Get(id) : ConsumablePresentation.IsKnown(id) ? ConsumablePresentation.Icon(id) : catalog.GetDisplayIcon(catalog.Get(id));
                        Require(image.sprite == expected, "Reward icon differs from the real item: " + id); WithinViewport(image.rectTransform, viewport);
                    }
                    Require(report.rewardIcons >= 6, "Reward icon trays are missing.");
                }
                else
                {
                    var badge = _fixture.GetComponentInChildren<DailyQuestBadge>(); Require(badge != null, "Shortcut badge is missing.");
                    report.badgeCount = badge.PendingCount;
                    Require(report.badgeCount == DailyRewardsService.CountReadyRewards(_snapshot, _snapshot.now), "Badge count differs from ready rewards.");
                    Require(badge.transform.Find("ExpeditionDailyQuestBadge").GetComponent<CanvasGroup>().blocksRaycasts == false, "Badge intercepts button touches.");
                }
                foreach (var text in _fixture.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate(); report.textCount++;
                    Require(!text.isTextOverflowing, "Text overflow " + text.name + " / " + text.text + " / " + text.rectTransform.rect);
                    WithinViewport(text.rectTransform, viewport);
                }
                foreach (var button in _fixture.GetComponentsInChildren<Button>())
                {
                    report.buttons++;
                    Require(((RectTransform)button.transform).rect.height >= 44, "Touch target too short: " + button.name);
                    var physical = ScreenRect((RectTransform)button.transform);
                    Require(physical.height >= 44, "Physical touch target too short: " + button.name + " / " + physical);
                    WithinViewport((RectTransform)button.transform, viewport);
                }
                Require(report.textCount >= (_badgeOnly ? 2 : 14) && report.buttons >= (_badgeOnly ? 1 : 3), "Production daily controls are missing."); report.passed = true;
            }
            catch (Exception ex) { report.error = ex.Message; }
            var path = ".cache/qa/legacy-0.3.10/daily-ui-" + (_mode ?? "none").Replace("show-", "") + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException(report.error);
            return JsonUtility.ToJson(report);
        }
        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity); var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            { var point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void WithinViewport(RectTransform rect, Rect viewport)
        {
            var physical = ScreenRect(rect);
            Require(physical.width > 0 && physical.height > 0 && physical.xMin >= viewport.xMin - 1 && physical.xMax <= viewport.xMax + 1 &&
                physical.yMin >= viewport.yMin - 1 && physical.yMax <= viewport.yMax + 1, "Control outside physical viewport: " + rect.name + " / " + physical + " / viewport " + viewport);
        }
        private static void ValidateScopeReset(DailyRewardsService.State state)
        {
            Require(!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "Logic checks require an offline fixture.");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var saved = new Dictionary<FieldInfo, object>();
            foreach (var name in new[] { "_scope", "_state", "_busy", "_error", "_receivedAt", "Changed" })
            { var field = typeof(DailyRewardsService).GetField(name, flags); saved.Add(field, field.GetValue(null)); }
            try
            {
                typeof(DailyRewardsService).GetField("Changed", flags).SetValue(null, null);
                typeof(DailyRewardsService).GetField("_scope", flags).SetValue(null, "qa-foreign-account");
                typeof(DailyRewardsService).GetField("_state", flags).SetValue(null, state);
                Require(DailyRewardsService.ReadyRewardCount == 0 && DailyRewardsService.Current == null, "Foreign account badge leaked into the current scope.");
            }
            finally { foreach (var pair in saved) pair.Key.SetValue(null, pair.Value); }
        }
        private static void Restore() { if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture); _fixture = null; _canvas = null; _snapshot = null; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
