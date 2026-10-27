using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Native runtime UI in a disposable scene. No authentication, entry, claims or other account writes.</summary>
    public static class ExpeditionGlobalBossQa0314
    {
        [Serializable] private sealed class Check { public string name; public bool passed; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites; public int width = 1080, height; public string error;
            public List<Check> checks = new(); public List<string> screenshots = new();
        }
        private const string ServerFixture = "Design/Release-0.3.14/validation/server/multiclient-isolated-global-boss.json";
        [Serializable] private sealed class StateShot { public string name; public GlobalBossService.State response; }
        [Serializable] private sealed class StateShots { public StateShot[] boss_ui_snapshots; }
        [Serializable] private sealed class BoardShot { public string name; public M3BoardSyncMsg response; }
        [Serializable] private sealed class BoardShots { public BoardShot[] boss_ui_snapshots; }
        [Serializable] private sealed class RankingShot { public string name; public GlobalBossLeaderboardService.Page response; }
        [Serializable] private sealed class RankingShots { public RankingShot[] boss_ui_snapshots; }
        private static string FixtureJson => File.ReadAllText(ServerFixture);
        private static GlobalBossService.State ActualState() => JsonUtility.FromJson<StateShots>(FixtureJson).boss_ui_snapshots.Single(s => s.name == "boss-state").response;
        private static M3BoardSyncMsg ActualBoard() => JsonUtility.FromJson<BoardShots>(FixtureJson).boss_ui_snapshots.Single(s => s.name == "boss-running-clock").response;
        private static GlobalBossLeaderboardService.Page ActualRanking() => JsonUtility.FromJson<RankingShots>(FixtureJson).boss_ui_snapshots.Single(s => s.name == "boss-ranking").response;
        private static float _timerStart, _timerStarted;
        private static bool _pausedTimer, _holdingFx, _opponentTimer, _delayTimer;
        private static M3BoardSyncMsg _timerPacket;
        private static System.Collections.IEnumerator _lethalSequence;
        private static float _lethalStarted;
        private static ExpeditionRiftScream _cry;
        private static ExpeditionRiftAdaptation _adaptation;
        private static GameObject _previewHost;
        private static ExpeditionGlobalBossPanel _previewPanel;
        public static string Run(string target)
        {
            if (target != null && target.StartsWith("play-", StringComparison.Ordinal)) return PlayFixture(target.Substring(5));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            if (target == "logic") { var logic = new Report(); Protocol(logic); logic.passed = true; Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss"); File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/contract0314.json", JsonUtility.ToJson(logic, true)); return JsonUtility.ToJson(logic); }
            var height = target == "tall" ? 2400 : 1920; var output = ".cache/qa/legacy-0.3.14/global-boss/" + height;
            Directory.CreateDirectory(output); var report = new Report { height = height };
            var previous = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var bootstraps = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture texture = null;
            try
            {
                Protocol(report);
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
                Require(report, catalog != null && catalog.Get("global_boss_teleport") != null, "Teleport item exists in shipping catalog");
                var sprite = Resources.Load<Sprite>(GlobalBossService.Art);
                Require(report, sprite != null && ExpeditionCombatantGrounding.TryGet(sprite, out var footprint) && footprint.sha256 == "7c8b937a31b7ddc709ffdcc aaff524b46fb015189c14fd24adbd52e9aab7dd7e".Replace(" ", ""), "Original boss art and baked solid-foot geometry");
                camera = new GameObject("Boss0314QaCamera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = height * .5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.transform.position = new Vector3(0, 0, -10);
                texture = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("Boss0314QaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                foreach (var variant in new[] { "dossier", "ranking", "ranking-bottom", "ranking-self", "empty", "actual-ranking", "rewards" })
                {
                    var fixture = ActualState(); var ranking = Ranking(fixture.event_id, variant);
                    fixture.my_damage = ranking.self_entry?.damage ?? 0; fixture.attempts_used = ranking.self_entry?.attempts_used ?? 0; fixture.attempts_left = 3 - fixture.attempts_used;
                    fixture.eligibility_progress = fixture.my_damage; fixture.qualified = fixture.my_damage >= fixture.eligibility_required;
                    var panel = ExpeditionGlobalBossPanel.Open(canvas.transform, fixture, ranking);
                    var participants = variant.StartsWith("ranking", StringComparison.Ordinal) || variant == "empty" || variant == "actual-ranking";
                    if (participants) panel.SelectTab(true);
                    if (variant == "ranking-bottom") { Canvas.ForceUpdateCanvases(); panel.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0; }
                    if (variant == "rewards") panel.GetComponentsInChildren<Button>().Single(b => b.name == "RewardDetails").onClick.Invoke();
                    Canvas.ForceUpdateCanvases();
                    var enter = panel.GetComponentsInChildren<Button>().First(b => b.name == "EnterRift");
                    Require(report, enter.interactable, "Actual server state permits entry: " + variant);
                    if (participants)
                    {
                        var names = panel.GetComponentsInChildren<TMP_Text>().Where(t => t.name == "ParticipantName").ToArray();
                        Require(report, names.Length == (variant == "empty" ? 0 : variant == "actual-ranking" ? ActualRanking().entries.Length : 20), "Expected distinct participant row count: " + variant);
                        Require(report, names.All(t => !t.richText), "Player names never interpret markup");
                        var self = panel.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "PersonalRankText").text;
                        Require(report, variant == "ranking" || variant == "ranking-bottom" ? self.Contains("уточняется") : variant == "empty" ? self.Contains("Нанесите урон") : self.Contains("ВАШЕ МЕСТО"), "Current player rank0/positive/empty semantics: " + variant);
                    }
                    foreach (var icon in panel.GetComponentsInChildren<Image>())
                        if (icon.name == "BossArt" || icon.name == "ParticipantAvatar" || icon.name.StartsWith("AbilityIcon-", StringComparison.Ordinal) || icon.name.StartsWith("StatIcon-", StringComparison.Ordinal) || icon.name.EndsWith("Icon", StringComparison.Ordinal)) Require(report, icon.sprite != null, "Imported icon: " + variant + "/" + icon.name);
                    foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
                    {
                        if (string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate();
                        Require(report, !text.isTextOverflowing, "Readable text: " + variant + "/" + text.name);
                    }
                    foreach (var button in panel.GetComponentsInChildren<Button>())
                    {
                        var rect = ((RectTransform)button.transform).rect;
                        Require(report, rect.width >= 44 && rect.height >= 44, "Touch target44px: " + variant + "/" + button.name);
                    }
                    var path = output + "/boss-" + variant + ".png"; Save(camera, texture, path, height); report.screenshots.Add(path);
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
                var sync = JsonUtility.FromJson<M3BoardSyncMsg>("{\"globalBoss\":true,\"globalBossMyDamage\":5409,\"bossOwnTurn\":7,\"bossScreamCooldown\":2,\"bossScreamTriggered\":true,\"bossScreamDamage\":4287,\"bossPlayerTurnsLeft\":24,\"bHp\":471503,\"bMaxHp\":550000}");
                Require(report, sync.globalBoss && sync.globalBossMyDamage == 5409 && sync.bHp == 471503 && sync.bossScreamTriggered && sync.bossScreamDamage == 4287, "Shipping BoardSync parses new boss fields");
                var terminal = JsonUtility.FromJson<M3GameOverMsg>("{\"globalBoss\":true,\"bossAttemptFinished\":true,\"bossGlobalHp\":0,\"bossGlobalMaxHp\":550000,\"bossMyDamage\":5409,\"bossQualified\":true,\"endReason\":\"boss_defeated\"}");
                Require(report, terminal.globalBoss && terminal.bossAttemptFinished && terminal.bossMyDamage == 5409 && terminal.bossQualified && terminal.endReason == "boss_defeated", "Shipping GameOver parses contribution and shared victory");
                var after = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Require(report, bootstraps.Length == after.Length && bootstraps.All(b => after.Contains(b)), "No account or Nakama lifecycle created"); report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); throw; }
            finally
            {
                RenderTexture.active = previousTexture; if (camera != null) camera.targetTexture = null;
                if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                if (scene.IsValid() && scene.isLoaded) { SceneManager.SetActiveScene(scene); foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene, true); }
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                File.WriteAllText(output + "/boss0314-report.json", JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report);
        }
        private static void Protocol(Report report)
        {
            Require(report, File.Exists(ServerFixture), "Actual isolated server UI snapshots exist");
            var state = ActualState(); var board = ActualBoard(); var ranking = ActualRanking();
            Require(report, state.ok && state.hp_max == 550000 && state.base_damage == 3500 && state.base_armor == 1000 && Mathf.Abs(state.base_crit - .05f) < .0001f && state.max_turns == 30 && state.base_heal == 0, "Actual server dossier stats deserialize without inferred defaults");
            Require(report, board.globalBoss && board.board.Length == 36 && board.bBaseDamage == 3500 && board.bBaseArmor == 1000 && Mathf.Abs(board.bBaseCrit - .05f) < .0001f && board.bBaseHeal == 0, "Shipping BoardSync parses actual boss stats and36cells");
            Require(report, !board.turnPaused && board.turnDurationSeconds == 30 && board.turnEndsAtUnixMs - board.serverNowUnixMs == 30000, "Server first decision has a real30second deadline");
            Require(report, GlobalBossLeaderboardService.Valid(ranking, state.event_id) && ranking.self_entry != null && ranking.self_entry.is_self, "Actual server leaderboard page parses own row");
            var empty = Ranking(state.event_id, "empty"); Require(report, GlobalBossLeaderboardService.Valid(empty, state.event_id), "New nonparticipant rank0 accepted");
            var pending = Ranking(state.event_id, "ranking"); Require(report, GlobalBossLeaderboardService.Valid(pending, state.event_id) && pending.self_entry.rank == 0 && pending.self_entry.damage > 0, "Pending projection self rank0 positive damage accepted");
            pending.entries[0].rank = 0; Require(report, !GlobalBossLeaderboardService.Valid(pending, state.event_id), "Rank0 never accepted as a ranked participant row");
            Require(report, !GlobalBossLeaderboardService.Valid(ranking, "other-week"), "Stale event ranking rejected");
        }
        private static GlobalBossLeaderboardService.Page Ranking(string eventId, string variant)
        {
            if (variant == "actual-ranking") return ActualRanking();
            var entries = variant == "empty" || variant == "dossier" || variant == "rewards" ? Array.Empty<GlobalBossLeaderboardService.Entry>() : Enumerable.Range(1, 20).Select(i => new GlobalBossLeaderboardService.Entry
            { rank = i, user_id = "offline-participant-" + i, display_name = i == 7 ? "Скиталец" : "Покоритель_Разлома_" + i,
                avatar_id = i % 2 == 0 ? "dawnwarden-female" : "dawnwarden-male", damage = 55000 - i * 2137, attempts_used = (i - 1) % 3 + 1, is_self = variant == "ranking-self" && i == 7 }).ToArray();
            var pending = variant == "ranking" || variant == "ranking-bottom";
            var self = variant == "ranking-self" ? entries[6] : new GlobalBossLeaderboardService.Entry { rank = 0, user_id = "offline-current-player", is_self = true, damage = pending ? 5409 : 0, attempts_used = pending ? 1 : 0 };
            return new GlobalBossLeaderboardService.Page { ok = true, event_id = eventId, entries = entries, self_entry = self, updating = pending, next_cursor = entries.Length == 20 ? "offline-next-page" : null };
        }
        private static void Require(Report report, bool passed, string name) { report.checks.Add(new Check { name = name, passed = passed }); if (!passed) throw new InvalidOperationException(name); }
        private static string PlayFixture(string target)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!EditorApplication.isPlaying || !SceneManager.GetActiveScene().name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal) || UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) != null) throw new InvalidOperationException("Use the isolated offline Play fixture only.");
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>(); if (manager == null || manager.enabled) throw new InvalidOperationException("Disabled offline manager required.");
            void Set(string name, object value) => typeof(DuelMatch3Manager).GetField(name, flags).SetValue(manager, value);
            object Call(string name, params object[] args) => typeof(DuelMatch3Manager).GetMethod(name, flags).Invoke(manager, args);
            var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.x).ToArray();
            if (target == "ui-close" || target == "close") { if (_previewHost != null) { _previewHost.SetActive(false); UnityEngine.Object.Destroy(_previewHost); } _previewHost = null; _previewPanel = null; return "Offline dossier preview closed."; }
            if (target == "dossier" || target == "ranking" || target == "ranking-self" || target == "ranking-empty" || target == "ranking-bottom")
            {
                if (_previewHost != null) UnityEngine.Object.DestroyImmediate(_previewHost);
                _previewHost = new GameObject("OfflineBoss0314Preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var previewCanvas = _previewHost.GetComponent<Canvas>(); previewCanvas.renderMode = RenderMode.ScreenSpaceOverlay; previewCanvas.sortingOrder = 11000;
                var scaler = _previewHost.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                var state = ActualState(); var rank = Ranking(state.event_id, target == "ranking-empty" ? "empty" : target);
                state.my_damage = rank.self_entry?.damage ?? 0; state.eligibility_progress = state.my_damage; state.qualified = state.my_damage >= state.eligibility_required;
                state.attempts_used = rank.self_entry?.attempts_used ?? 0; state.attempts_left = 3 - state.attempts_used;
                _previewPanel = ExpeditionGlobalBossPanel.Open(_previewHost.transform, state, rank);
                if (target.StartsWith("ranking", StringComparison.Ordinal)) _previewPanel.SelectTab(true);
                Canvas.ForceUpdateCanvases(); if (target == "ranking-bottom") _previewPanel.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0;
                return "Actual offline dossier/ranking prepared for raw native GameView capture: " + target;
            }
            if (target == "ui-verify")
            {
                if (_previewPanel == null) throw new InvalidOperationException("Open play-dossier/ranking first.");
                Canvas.ForceUpdateCanvases(); var ui = new Report { height = Screen.height };
                foreach (var text in _previewPanel.GetComponentsInChildren<TMP_Text>()) { text.ForceMeshUpdate(); Require(ui, !text.isTextOverflowing, "Native dossier/ranking text: " + text.name); }
                Require(ui, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null && !manager.enabled, "Offline GameView preview performs no account/network work"); ui.passed = true;
                Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss/play"); File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/native-ui-" + (_previewPanel.GetComponentsInChildren<TMP_Text>().Any(t => t.name == "ParticipantName") ? "ranking" : "dossier") + ".json", JsonUtility.ToJson(ui, true)); return JsonUtility.ToJson(ui);
            }
            if (target == "battle" || target == "profile")
            {
                ReleaseFx(); var board = ActualBoard();
                Set("_myPanel", panels[0]); Set("_opPanel", panels[1]); Set("_isGlobalBoss", true);
                Set("_boardView", UnityEngine.Object.FindFirstObjectByType<Match3BoardView>()); Set("_hud", UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>());
                Set("_offlinePresentationQa", true); Set("_hasInitialBoardSync", true); Set("_isMyTurn", true); Set("_inputBlocked", false);
                var logic = new Match3BoardLogic(); logic.FromArray(board.board); Set("_board", logic); UnityEngine.Object.FindFirstObjectByType<Match3BoardView>().RefreshAll(logic);
                Set("_myStats", new PlayerStats { hp = board.aHp, maxHp = board.aMaxHp, mana = 80, baseDamage = board.aBaseDamage, baseArmor = board.aBaseArmor, baseHeal = board.aBaseHeal, baseCrit = board.aBaseCrit });
                Set("_opStats", new PlayerStats { hp = board.bHp, maxHp = board.bMaxHp, mana = board.bMana, baseDamage = 3500, baseArmor = 1000, baseHeal = 0, baseCrit = .05f });
                panels[0].SetPlayerName("Скиталец"); panels[0].UpdateStats(board.aHp, board.aMaxHp, 80, 100); panels[0].UpdateCombatStats(board.aBaseDamage, board.aBaseArmor, board.aBaseHeal, board.aBaseCrit * 100); panels[0].UpdateAvatarLevel(12);
                panels[1].SetPlayerName("Сердце Разлома"); panels[1].UpdateStats(board.bHp, board.bMaxHp, board.bMana, 100); panels[1].UpdateCombatStats(3500, 1000, 0, 5, false); panels[1].UpdateAvatarLevel(-1);
                panels[1].avatarImage.sprite = Resources.Load<Sprite>(GlobalBossService.Art); ExpeditionBattlePortrait.MarkFullBody(panels[1].avatarImage, .62f);
                var figure = GameObject.Find("ExpeditionPlayerFigure")?.GetComponent<ExpeditionCombatantFigure>();
                if (figure != null) figure.BindPreview(Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"), true);
                var bossFigure = GameObject.Find("ExpeditionOpponentFigure")?.GetComponent<ExpeditionCombatantFigure>(); if (bossFigure != null) bossFigure.BindPreview(Resources.Load<Sprite>(GlobalBossService.Art), false);
                var abilities = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>(); Set("_abilityPanel", abilities);
                Call("TryAutoAssignAbilitySpritesInEditor"); Call("ConfigureAbilityButtonsVisuals"); ExpeditionBattleLayout.ApplyAbilityLayout(abilities);
                foreach (var button in new[] { abilities.petardButton, abilities.crossButton, abilities.squareButton, abilities.shieldButton, abilities.furyButton })
                    if (button == null || !(button.targetGraphic is Image icon) || icon.sprite == null) throw new InvalidOperationException("Actual ability sprite missing in the offline scene.");
                abilities.Refresh(new PlayerStats { mana = 80 }, true, false, 20, 20, 30, 40, 30);
                Call("InstallGlobalBossHud"); Call("UpdateGlobalBossPresentation", board, false); Call("SampleServerClockOnReceive", board); Call("ApplyTurnDeadlineFromBoardSync", board); Call("UpdateHudTimerAndPhase");
                return "Actual server profile3500damage/1000armor/5%crit, board, rift background and30second deadline prepared. No match or transport.";
            }
            if (target == "background")
            {
                var background = ExpeditionUi.Find(panels[0].GetComponentInParent<Canvas>().transform, "ExpeditionBattleBackdrop")?.GetComponent<Image>();
                if (background == null || background.sprite != Resources.Load<Sprite>("Expedition/Boss/0314/arena-rift")) throw new InvalidOperationException("Boss arena background was not selected.");
                if (GameObject.Find("ExpeditionOpponentFigure")?.GetComponent<Image>()?.sprite != Resources.Load<Sprite>(GlobalBossService.Art)) throw new InvalidOperationException("Actual boss body missing.");
                return "PASS actual generated rift arena/body selected; no mine backdrop.";
            }
            if (target == "timer" || target == "timer-paused" || target == "timer-opponent" || target == "timer-paused-opponent" || target == "timer-delay" || target == "timer-delay-opponent")
            {
                var packet = ActualBoard(); packet.turnPaused = target.Contains("paused"); _opponentTimer = target.EndsWith("opponent", StringComparison.Ordinal);
                _delayTimer = target.Contains("delay"); _timerPacket = packet;
                if (packet.turnPaused) packet.turnEndsAtUnixMs = 0;
                Set("_hasInitialBoardSync", true); Set("_isMyTurn", !_opponentTimer); Set("_inputBlocked", false); Set("_remoteSyncRoutine", null);
                Call("SampleServerClockOnReceive", packet); Call("ApplyTurnDeadlineFromBoardSync", packet); Call("UpdateHudTimerAndPhase");
                _timerStart = (float)Call("GetRemainingTurnSecondsForHud"); _timerStarted = Time.unscaledTime; _pausedTimer = packet.turnPaused;
                if (_timerStart < 28 || _timerStart > 30.1f) throw new InvalidOperationException("Boss timer did not start at30seconds.");
                return "Timer prepared: " + (_pausedTimer ? "paused animation holds30" : "running decision counts down") + "; wait2seconds, play-timer-verify.";
            }
            if (target == "timer-verify" || target == "timer-delay-verify")
            {
                if (_delayTimer)
                {
                    var beforeApply = (float)Call("GetRemainingTurnSecondsForHud");
                    Call("ApplyTurnDeadlineFromBoardSync", _timerPacket); // Same queued packet, no second receipt clock sample.
                    var afterApply = (float)Call("GetRemainingTurnSecondsForHud");
                    if (afterApply > beforeApply + .02f) throw new InvalidOperationException("Applying a queued packet added animation time back to its deadline.");
                }
                Call("UpdateHudTimerAndPhase"); var remaining = (float)Call("GetRemainingTurnSecondsForHud"); var elapsed = Time.unscaledTime - _timerStarted;
                if (elapsed < 1) throw new InvalidOperationException("Wait at least1second to verify timer behavior.");
                if (_pausedTimer ? Mathf.Abs(remaining - 30) > .02f : !(remaining < _timerStart - .75f && remaining > 0)) throw new InvalidOperationException("Boss paused/countdown semantics differ from server deadline.");
                var hud = UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>(); if (string.IsNullOrEmpty(hud.timerText.text) || hud.timerText.text == "—") throw new InvalidOperationException("Boss timer rendered an empty/dash value.");
                var timerReport = new Report { height = Screen.height }; Require(timerReport, true, "Actual timer " + (_pausedTimer ? "holds full30duringanimation" : "counts down from server deadline") + ": " + remaining); timerReport.passed = true;
                Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss/play"); File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/timer-" + (_pausedTimer ? "paused" : "running") + (_opponentTimer ? "-opponent" : "") + (_delayTimer ? "-delay" : "") + ".json", JsonUtility.ToJson(timerReport, true)); return JsonUtility.ToJson(timerReport);
            }
            if (target == "lethal")
            {
                PlayFixture("profile"); var report = new Report { height = Screen.height };
                Set("_myUserId", "a-offline-hero"); Set("_opUserId", "zz-bot-global_boss"); Set("_gameEnded", false); Set("_pendingGameOver", false);
                var packet = ActualBoard(); packet.actionType = 0; packet.actionActorUserId = "zz-bot-global_boss"; packet.activeUserId = "zz-bot-global_boss";
                packet.bossActionSequence = 100; packet.bossOwnTurn = 7; packet.bossScreamTriggered = true; packet.bossAdaptationTriggered = true; packet.bossScreamDamage = 3773; packet.aHp = 0;
                packet.serverNowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); packet.turnEndsAtUnixMs = 0; packet.turnPaused = true;
                var gate = (Match3PresentationSyncGate)typeof(DuelMatch3Manager).GetField("_presentationSyncGate", flags).GetValue(manager); gate.Reset();
                var silent = (HashSet<M3BoardSyncMsg>)typeof(DuelMatch3Manager).GetField("_silentBoardSyncs", flags).GetValue(manager);
                void StopAdmission() { var routine = (Coroutine)typeof(DuelMatch3Manager).GetField("_remoteSyncRoutine", flags).GetValue(manager); if (routine != null) manager.StopCoroutine(routine); Set("_remoteSyncRoutine", null); }
                Set("_hasInitialBoardSync", true); Set("_awaitingPresentationSnapshot", false); Call("OnBoardSyncReceived", packet);
                Require(report, !silent.Contains(packet), "Explicit boss action0 actor+sequence+flags admitted with animation"); StopAdmission();
                var snapshot = JsonUtility.FromJson<M3BoardSyncMsg>(JsonUtility.ToJson(packet)); snapshot.serverNowUnixMs++;
                Set("_awaitingPresentationSnapshot", true); Call("OnBoardSyncReceived", snapshot);
                Require(report, silent.Contains(snapshot), "Recovery snapshot with retained flags remains quiet"); StopAdmission(); silent.Remove(snapshot);
                Set("_hasInitialBoardSync", false); var initial = JsonUtility.FromJson<M3BoardSyncMsg>(JsonUtility.ToJson(snapshot)); initial.serverNowUnixMs++;
                Call("OnBoardSyncReceived", initial); Require(report, silent.Contains(initial), "Initial snapshot remains quiet"); StopAdmission(); silent.Remove(initial);
                Set("_hasInitialBoardSync", true); Set("_awaitingPresentationSnapshot", false); Set("_lastScreamTurn", -1); Set("_lastAdaptationSequence", -1L);
                _lethalSequence = (System.Collections.IEnumerator)Call("ApplyRemoteBoardSync", packet);
                Require(report, _lethalSequence.MoveNext() && _lethalSequence.Current == null, "Actual sync yields first frame before presentation");
                Require(report, _lethalSequence.MoveNext() && _lethalSequence.Current is WaitForSecondsRealtime wait && wait.waitTime >= 1.35f, "Lethal free ability yields real1.35second impact hold");
                Require(report, ((PlayerStats)typeof(DuelMatch3Manager).GetField("_myStats", flags).GetValue(manager)).hp == 0, "Authoritative lethal HP reached before terminal overlay");
                Require(report, GameObject.Find("GlobalBossOutcomeModal") == null, "Outcome cannot cover lethal impact before hold");
                _cry = UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftScream>(); _adaptation = UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftAdaptation>();
                Require(report, _cry != null && _adaptation != null, "Lethal action0 presents actual scream and adaptation");
                _holdingFx = true; EditorApplication.update += HoldFx; HoldFx(); _lethalStarted = Time.unscaledTime;
                Set("_bossGameOver", new M3GameOverMsg { globalBoss = true, bossAttemptFinished = true, bossGlobalHp = packet.bHp, bossGlobalMaxHp = packet.bMaxHp, bossMyDamage = 5409, bossQualified = true });
                Set("_pendingGameOver", true); report.passed = true; Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss/play"); File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/lethal-hold.json", JsonUtility.ToJson(report, true));
                return "PASS actual admission/silent snapshot checks and actual coroutine paused at native1.35s yield. Offline QA holds FX age.45 for capture; wait>=1.35s then play-lethal-finish.";
            }
            if (target == "lethal-finish")
            {
                if (_lethalSequence == null || Time.unscaledTime - _lethalStarted < 1.35f) throw new InvalidOperationException("Wait for the full lethal1.35second hold.");
                ReleaseFx(); while (_lethalSequence.MoveNext()) { } _lethalSequence = null;
                var report = new Report { height = Screen.height }; Require(report, GameObject.Find("GlobalBossOutcomeModal") != null, "Queued actual outcome appears after full impact hold"); report.passed = true;
                File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/lethal-finish.json", JsonUtility.ToJson(report, true)); return JsonUtility.ToJson(report);
            }
            if (target == "fx" || target == "fx-hold")
            {
                ReleaseFx(); foreach (var fx in UnityEngine.Object.FindObjectsByType<ExpeditionRiftScream>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(fx.gameObject);
                foreach (var fx in UnityEngine.Object.FindObjectsByType<ExpeditionRiftAdaptation>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(fx.gameObject);
                Set("_lastScreamTurn", -1); Set("_lastAdaptationSequence", -1L);
                var packet = new M3BoardSyncMsg { globalBoss = true, globalBossMyDamage = 5409, bossPlayerTurnsLeft = 24, bossOwnTurn = 7, bossActionSequence = 99, bossScreamTriggered = true, bossScreamDamage = 3773, bossAdaptationTriggered = true };
                var hpBefore = ((PlayerStats)typeof(DuelMatch3Manager).GetField("_myStats", flags).GetValue(manager)).hp;
                Call("UpdateGlobalBossPresentation", packet, true); Call("UpdateGlobalBossPresentation", packet, true);
                _cry = UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftScream>(); _adaptation = UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftAdaptation>();
                if (_cry == null || _adaptation == null || UnityEngine.Object.FindObjectsByType<ExpeditionRiftScream>(FindObjectsSortMode.None).Length != 1 || UnityEngine.Object.FindObjectsByType<ExpeditionRiftAdaptation>(FindObjectsSortMode.None).Length != 1) throw new InvalidOperationException("Scream/adaptation missing or repeated by duplicate sequence.");
                var fxReport = new Report { height = Screen.height };
                Require(fxReport, ((PlayerStats)typeof(DuelMatch3Manager).GetField("_myStats", flags).GetValue(manager)).hp == hpBefore, "FX presentation does not calculate/deduct HP");
                Require(fxReport, !_cry.raycastTarget && !_adaptation.raycastTarget, "Boss FX never own gameplay pointer input");
                foreach (var graphic in new MaskableGraphic[] { _cry, _adaptation })
                {
                    Canvas.ForceUpdateCanvases();
                    Require(fxReport, graphic.canvasRenderer != null && graphic.materialForRendering != null && graphic.IsActive() && !graphic.canvasRenderer.cull, "Actual FX has active uncullled CanvasRenderer/material: " + graphic.name);
                    graphic.GetType().GetField("_started", flags).SetValue(graphic, Time.unscaledTime - .45f);
                    using var vertices = new VertexHelper();
                    graphic.GetType().GetMethod("OnPopulateMesh", flags | BindingFlags.DeclaredOnly).Invoke(graphic, new object[] { vertices });
                    Require(fxReport, vertices.currentVertCount > 0 && vertices.currentIndexCount > 0, "Actual production mesh rendered: " + graphic.name);
                }
                if (target == "fx-hold") { _holdingFx = true; EditorApplication.update += HoldFx; HoldFx(); }
                fxReport.passed = true; Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss/play"); File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/fx0314.json", JsonUtility.ToJson(fxReport, true));
                return "PASS actual scream/adaptation deduplicate same sequence; " + (_holdingFx ? "Editor-only capture holds actual presentation age0.45; play-fx-release resumes real expiry." : "real transient FX now playing.");
            }
            if (target == "fx-release") { ReleaseFx(); return "Actual FX lifetime resumed; wait2seconds, play-fx-expired."; }
            if (target == "fx-expired")
            {
                if (_holdingFx || UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftScream>() != null || UnityEngine.Object.FindFirstObjectByType<ExpeditionRiftAdaptation>() != null) throw new InvalidOperationException("Transient boss FX did not expire.");
                return "PASS actual transient FX expire and leave no account/network state.";
            }
            if (target == "rules") { GameObject.Find("GlobalBossRules").GetComponent<Button>().onClick.Invoke(); return "Actual boss rules overlay opened."; }
            if (target == "cry") { ExpeditionRiftScream.Play(panels[1], panels[0]); return "Actual transient scream FX played; damage remains presentation-only in this fixture."; }
            if (target == "contribution" || target == "victory" || target == "refund")
            {
                var existing = GameObject.Find("GlobalBossOutcomeModal"); if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
                var rules = GameObject.Find("GlobalBossRulesModal"); if (rules != null) UnityEngine.Object.DestroyImmediate(rules);
                Set("_bossGameOver", new M3GameOverMsg { globalBoss = true, bossAttemptFinished = true, bossGlobalHp = target == "victory" ? 0 : 471503,
                    bossGlobalMaxHp = 550000, bossMyDamage = target == "refund" ? 0 : 5409, bossQualified = target != "refund", bossAttemptRefunded = target == "refund" });
                Call("ShowGlobalBossOutcome"); return "Actual boss outcome: " + target + "; no rewards or claims invoked.";
            }
            if (target == "verify")
            {
                Canvas.ForceUpdateCanvases(); var report = new Report { height = 1920 };
                Require(report, Screen.width == 1080 && (Screen.height == 1920 || Screen.height == 2400), "Native1080portrait play viewport");
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                    if (text.gameObject.activeInHierarchy && !string.IsNullOrEmpty(text.text) && (text.transform.IsChildOf(panels[1].transform) || text.transform.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("GlobalBoss", StringComparison.Ordinal))))
                    { text.ForceMeshUpdate(); Require(report, !text.isTextOverflowing, "Readable battle/outcome boss label: " + text.name); }
                Require(report, panels[1].combatStatsName == null || !panels[1].combatStatsName.text.Contains("ЛЕЧ"), "Boss has no healing characteristic");
                Require(report, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null && !manager.enabled, "No account or gameplay transport");
                report.passed = true; Directory.CreateDirectory(".cache/qa/legacy-0.3.14/global-boss/play");
                File.WriteAllText(".cache/qa/legacy-0.3.14/global-boss/play/verify-" + (GameObject.Find("GlobalBossOutcomeModal") != null ? "outcome" : "battle") + ".json", JsonUtility.ToJson(report, true));
                return JsonUtility.ToJson(report);
            }
            throw new ArgumentException("Use play-profile/background/timer/timer-paused/timer-verify/fx/fx-hold/fx-release/fx-expired/rules/contribution/victory/refund/verify.");
        }
        private static void HoldFx()
        {
            if (!_holdingFx || !EditorApplication.isPlaying || _cry == null || _adaptation == null) { ReleaseFx(); return; }
            typeof(ExpeditionRiftScream).GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_cry, Time.unscaledTime - .45f);
            typeof(ExpeditionRiftAdaptation).GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_adaptation, Time.unscaledTime - .45f);
        }
        private static void ReleaseFx() { _holdingFx = false; EditorApplication.update -= HoldFx; }
        private static void Save(Camera camera, RenderTexture texture, string path, int height)
        {
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture }); else camera.Render();
            RenderTexture.active = texture; var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
    }
}
