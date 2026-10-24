using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace Project.Editor
{
    /// <summary>Idle-only UI fixture. Bet tests run real UI listeners but intercept the transport boundary; no RPC or paid entry.</summary>
    public static class ExpeditionArenaBracketQa
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] SavedFields = { "_bracketRoot", "_rowsRoot", "_bracketTitle", "_cdText", "_prizeFooterText", "_headerTemplate", "_pairRowTemplate", "_lastTournament", "_myUserId", "_bettingUiEnabled", "_betsOpen", "_bettingRound", "_betContextId", "_lastBracketSignature" };
        private static ArenaMatch8Bridge _bridge;
        private static readonly Dictionary<string, object> Saved = new Dictionary<string, object>();
        private static readonly Dictionary<int, int> SavedPicks = new Dictionary<int, int>();
        private static bool _wasEnabled;
        private static GameObject _fixtureRoot;
        private static object _productionTournament;
        private static Button.ButtonClickedEvent _savedExitAction;
        private static bool _savedExitInteractable;
        private static int _queueExitClicks;
        private static UnityAction<bool> _bettingOptionListener;
        private static Toggle _bettingOption;
        private static bool _savedOptionOn, _hadBettingPreference;
        private static GameObject _savedSceneBody;
        private static bool _sceneBodyWasActive;
        private static int _savedBettingPreference;
        private static readonly List<Vector2Int> BetRequests = new List<Vector2Int>();
        private const string BettingPreference = "arena.betting_ui_enabled";

        /// <summary>Decode and display the real isolated-backend poll, without ApplyPoll/RPC/match arming.</summary>
        public static string RunProductionSnapshot(string reportPath, int index)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required.");
            var report = JsonUtility.FromJson<BackendReport>(File.ReadAllText(reportPath));
            if (report?.arena_ui_snapshots == null || index < 0 || index >= report.arena_ui_snapshots.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            var snapshot = report.arena_ui_snapshots[index];
            Prepare();
            var pollType = typeof(ArenaMatch8Bridge).GetNestedType("ArenaPollResponse", BindingFlags.NonPublic);
            var poll = JsonUtility.FromJson(snapshot.poll_json, pollType);
            if (poll == null || !(bool)Get(poll, "ok")) throw new InvalidOperationException("Actual production poll did not decode successfully.");
            var tournament = Get(poll, "tournament");
            if (tournament == null || !(bool)Get(tournament, "active")) throw new InvalidOperationException("Snapshot has no active tournament.");
            Set(_bridge, "_myUserId", snapshot.viewer_uid);
            Set(_bridge, "_lastTournament", tournament);
            Set(_bridge, "_bettingUiEnabled", true);
            typeof(ArenaMatch8Bridge).GetMethod("ApplyQueueLockState", Private).Invoke(_bridge, new object[] { false });
            typeof(ArenaMatch8Bridge).GetMethod("SyncMyBetsFromTournament", Private).Invoke(_bridge, new[] { tournament });
            typeof(ArenaMatch8Bridge).GetField("_activeBridge", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _bridge);
            typeof(ArenaMatch8Bridge).GetMethod("SetParticipationLocked", Private).Invoke(_bridge, new object[] { true });
            try { typeof(ArenaMatch8Bridge).GetMethod("ShowBracket", Private).Invoke(_bridge, new[] { tournament }); }
            finally { _fixtureRoot = (GameObject)Get(_bridge, "_bracketRoot"); }
            _productionTournament = tournament;
            foreach (var toggle in _fixtureRoot.GetComponentsInChildren<Toggle>(true)) toggle.interactable = false;
            Canvas.ForceUpdateCanvases();
            var result = snapshot.name + ": " + VerifyProduction();
            Record("production-" + index, true, result);
            return result;
        }

        public static string RunBettingSnapshot(string reportPath, int index)
        {
            Restore();
            RunProductionSnapshot(reportPath, index);
            // The actual option and PairRowBetSelect callbacks run unchanged. Only
            // the final transport invocation is replaced; a clicked checkbox can
            // never send a bet or modify a real tournament from this fixture.
            BetRequests.Clear();
            Set(_bridge, "EditorBetRequestOverride", new Action<int, int>((slot, side) => BetRequests.Add(new Vector2Int(slot, side))));
            _bettingOption = (Toggle)Get(_bridge, "_bettingToggle");
            if (_bettingOption == null) throw new InvalidOperationException("Actual arena betting option is missing.");
            _savedOptionOn = _bettingOption.isOn;
            _hadBettingPreference = PlayerPrefs.HasKey(BettingPreference);
            _savedBettingPreference = PlayerPrefs.GetInt(BettingPreference, 0);
            _bettingOptionListener = (UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityAction<bool>), _bridge,
                typeof(ArenaMatch8Bridge).GetMethod("OnBettingToggleChanged", Private));
            _bettingOption.onValueChanged.AddListener(_bettingOptionListener);
            _bettingOption.isOn = false;
            _bettingOption.isOn = true;
            if (!(bool)Get(_bridge, "_bettingUiEnabled")) throw new InvalidOperationException("Actual source option did not enable betting presentation.");
            Canvas.ForceUpdateCanvases();
            return "Real backend DTO and real option callbacks loaded. Run betting-verify after one rendered frame; all bet requests are intercepted before RPC. Temporary preference is restored by restore.";
        }

        private static string VerifyBetting()
        {
            if (_bridge == null || _productionTournament == null || _bettingOptionListener == null)
                throw new InvalidOperationException("Load betting|absolute-report-path|snapshot-index first.");
            if (EventSystem.current == null) throw new InvalidOperationException("An active EventSystem is required.");
            Canvas.ForceUpdateCanvases();
            VerifyProduction();
            var viewer = (string)Get(_bridge, "_myUserId");
            var open = (bool)Get(_productionTournament, "bets_open");
            var eligible = new List<PairRowBetSelect>();
            foreach (var tag in _fixtureRoot.GetComponentsInChildren<ExpeditionArenaPairTag>(false))
            {
                var selection = tag.GetComponent<PairRowBetSelect>();
                if (selection == null) throw new InvalidOperationException("Real pair selection component is missing.");
                var own = tag.UidA == viewer || tag.UidB == viewer;
                var expected = tag.Current && open && !own;
                foreach (var name in new[] { "BetA", "BetB" })
                {
                    var toggle = tag.transform.Find(name)?.GetComponent<Toggle>();
                    if (toggle == null) throw new InvalidOperationException("Actual " + name + " is missing.");
                    if (expected)
                    {
                        var image = toggle.targetGraphic as Image;
                        if (!toggle.gameObject.activeInHierarchy || !toggle.IsInteractable() || image == null
                            || !image.enabled || image.color.a < .9f || !image.raycastTarget || image.depth < 0)
                            throw new InvalidOperationException("Eligible checkbox is not visibly interactive: " + tag.Round + "/" + tag.Slot + "/" + name);
                        AssertBetRaycast(toggle);
                    }
                    else if (toggle.gameObject.activeInHierarchy && toggle.IsInteractable())
                        throw new InvalidOperationException("An own, historical, future or closed-round checkbox became interactive.");
                }
                if (expected) eligible.Add(selection);
            }
            if (open && eligible.Count == 0) throw new InvalidOperationException("Production open window has no foreign current-round checkbox.");
            var clicked = 0;
            foreach (var selection in eligible)
            {
                var a = selection.transform.Find("BetA").GetComponent<Toggle>();
                var b = selection.transform.Find("BetB").GetComponent<Toggle>();
                selection.SetSideWithoutNotify(-1); Picks().Remove(selection.Slot); BetRequests.Clear();
                ClickBet(a);
                AssertBetSelection(selection, 0, a, b);
                ClickBet(b);
                AssertBetSelection(selection, 1, a, b);
                ClickBet(b);
                AssertBetSelection(selection, -1, a, b);
                var requests = BetRequests.Select(value => value.y).ToArray();
                if (BetRequests.Any(value => value.x != selection.Slot) || requests.Length != 3
                    || requests[0] != 0 || requests[1] != 1 || requests[2] != -1)
                    throw new InvalidOperationException("Actual A/B/cancel callbacks did not preserve the authoritative slot, or duplicated a request.");
                clicked++;
            }
            // Exercise both preference states through the original option listener.
            _bettingOption.isOn = false;
            if ((bool)Get(_bridge, "_bettingUiEnabled") || _fixtureRoot.GetComponentsInChildren<Toggle>(false).Length != 0)
                throw new InvalidOperationException("Option OFF did not hide actual pair checkboxes.");
            _bettingOption.isOn = true;
            if (!(bool)Get(_bridge, "_bettingUiEnabled")) throw new InvalidOperationException("Option ON did not restore its real callback.");
            VerifySettledBetReconciliation(eligible.FirstOrDefault());
            VerifyConnectorLayout();
            var result = "Actual production DTO: " + eligible.Count + " eligible foreign pairs, " + clicked
                + " A/B/cancel click sequences, rendered checkbox raycasts, exact slot/callback values, option OFF/ON and authoritative failed/settled selection reconciliation verified; closed/own/history rules and noncrossing UID routes preserved. RPC calls: 0.";
            Record("betting-verify", true, result);
            return result;
        }

        private static void VerifySettledBetReconciliation(PairRowBetSelect selection)
        {
            if (selection == null) return;
            var pending = (Dictionary<int, int>)Get(_bridge, "_pendingBetVersions");
            var sync = typeof(ArenaMatch8Bridge).GetMethod("SyncMyBetsFromTournament", Private);
            var complete = typeof(ArenaMatch8Bridge).GetMethod("CompleteBetRequest", Private);
            sync.Invoke(_bridge, new[] { _productionTournament });
            var expected = Picks().TryGetValue(selection.Slot, out var original) ? original : -1;
            pending[selection.Slot] = 200;
            Picks()[selection.Slot] = expected == 0 ? 1 : 0;
            var optimistic = Picks()[selection.Slot];
            sync.Invoke(_bridge, new[] { _productionTournament });
            if (!Picks().TryGetValue(selection.Slot, out var held) || held != optimistic)
                throw new InvalidOperationException("A same-round authoritative poll erased an in-flight local intention.");
            complete.Invoke(_bridge, new object[] { selection.Slot, 199, null, false });
            if (!pending.ContainsKey(selection.Slot) || Picks()[selection.Slot] != optimistic)
                throw new InvalidOperationException("A stale completion overwrote the newer local intention.");
            // Actual failed response handler restores the last authoritative poll,
            // then an unchanged-signature poll must keep that settled result.
            complete.Invoke(_bridge, new object[] { selection.Slot, 200, null, false });
            sync.Invoke(_bridge, new[] { _productionTournament });
            var actual = Picks().TryGetValue(selection.Slot, out var settled) ? settled : -1;
            if (pending.ContainsKey(selection.Slot) || actual != expected)
                throw new InvalidOperationException("A failed bet left a false optimistic check after the authoritative poll.");
            typeof(ArenaMatch8Bridge).GetMethod("ApplyBettingUiToAllRows", Private).Invoke(_bridge, null);
        }

        private static void VerifyConnectorLayout()
        {
            var tags = _fixtureRoot.GetComponentsInChildren<ExpeditionArenaPairTag>(false);
            var routes = _fixtureRoot.GetComponentsInChildren<ExpeditionArenaConnectorTag>(false);
            if (routes.Length != 6) throw new InvalidOperationException("The complete tree must retain 6 source routes.");
            var quarters = routes.Where(route => route.SourceRound == "qf" && !string.IsNullOrEmpty(route.WinnerUid)).ToArray();
            var intervals = new List<Vector2>();
            foreach (var target in quarters.GroupBy(route => route.TargetSlot))
            {
                var centers = target.Select(route => tags.Single(tag => tag.Round == "qf" && tag.Slot == route.SourceSlot))
                    .Select(tag => { var rect = (RectTransform)tag.transform; return (rect.anchorMin.y + rect.anchorMax.y) * .5f; }).ToArray();
                intervals.Add(new Vector2(centers.Min(), centers.Max()));
            }
            if (intervals.Count == 2 && Mathf.Max(intervals[0].x, intervals[1].x) < Mathf.Min(intervals[0].y, intervals[1].y))
                throw new InvalidOperationException("Shuffled historical quarters produce overlapping destination trunks.");
        }

        private static string VerifyBetPollOrdering()
        {
            if (_bridge == null || _productionTournament == null || _bettingOptionListener == null)
                throw new InvalidOperationException("Load an open betting snapshot 0 or 2 first.");
            var viewer = (string)Get(_bridge, "_myUserId");
            var tag = _fixtureRoot.GetComponentsInChildren<ExpeditionArenaPairTag>(false)
                .FirstOrDefault(pair => pair.Current && pair.UidA != viewer && pair.UidB != viewer);
            if (tag == null || !(bool)Get(_productionTournament, "bets_open")) throw new InvalidOperationException("An open foreign pair is required.");
            var originalTournament = Get(_bridge, "_lastTournament");
            var originalBets = Get(originalTournament, "my_bets");
            var pending = (Dictionary<int, int>)Get(_bridge, "_pendingBetVersions");
            var savedPending = new Dictionary<int, int>(pending);
            var savedPicks = new Dictionary<int, int>(Picks());
            var sync = typeof(ArenaMatch8Bridge).GetMethod("SyncMyBetsFromTournament", Private);
            var complete = typeof(ArenaMatch8Bridge).GetMethod("CompleteBetRequest", Private);
            var pollGuard = typeof(ArenaMatch8Bridge).GetMethod("IsCurrentPollVersion", Private);
            if (pollGuard == null) throw new InvalidOperationException("Apply the deferred poll-generation fix first.");
            var round = (string)Get(_bridge, "_bettingRound");
            var betType = typeof(ArenaMatch8Bridge).GetNestedType("ArenaSideBet", BindingFlags.NonPublic);
            var emptyBets = Array.CreateInstance(betType, 0);
            var acceptedBets = Array.CreateInstance(betType, 1);
            var accepted = Activator.CreateInstance(betType, true);
            Set(accepted, "round", round); Set(accepted, "slot", tag.Slot); Set(accepted, "side", "a"); Set(accepted, "pick_uid", tag.UidA);
            acceptedBets.SetValue(accepted, 0);
            var responseType = typeof(ArenaMatch8Bridge).GetNestedType("ArenaPlaceBetResponse", BindingFlags.NonPublic);
            var acceptedResponse = Activator.CreateInstance(responseType, true);
            Set(acceptedResponse, "ok", true); Set(acceptedResponse, "my_bets", acceptedBets);
            var clearResponse = Activator.CreateInstance(responseType, true);
            Set(clearResponse, "ok", true); Set(clearResponse, "my_bets", emptyBets);
            var oldSnapshot = JsonUtility.FromJson(JsonUtility.ToJson(originalTournament), originalTournament.GetType());
            Set(oldSnapshot, "my_bets", emptyBets);
            try
            {
                pending.Clear(); Picks().Clear();
                Set(originalTournament, "my_bets", emptyBets);
                var oldPoll = (int)Get(_bridge, "_pollVersion");
                pending[tag.Slot] = 301; Picks()[tag.Slot] = 1; // B is pending after an earlier write A.
                complete.Invoke(_bridge, new object[] { tag.Slot, 300, acceptedResponse, false });
                if ((bool)pollGuard.Invoke(_bridge, new object[] { oldPoll }))
                {
                    sync.Invoke(_bridge, new[] { oldSnapshot });
                    throw new InvalidOperationException("A pre-acknowledgement poll is still admitted.");
                }
                if (!ReferenceEquals(Get(originalTournament, "my_bets"), acceptedBets) || Picks()[tag.Slot] != 1 || pending[tag.Slot] != 301)
                    throw new InvalidOperationException("Accepted A was not cached while preserving newer pending B.");
                complete.Invoke(_bridge, new object[] { tag.Slot, 301, null, false });
                if (pending.ContainsKey(tag.Slot) || !Picks().TryGetValue(tag.Slot, out var restored) || restored != 0)
                    throw new InvalidOperationException("Failed B did not roll back to acknowledged A.");
                var freshPoll = (int)Get(_bridge, "_pollVersion") + 1;
                Set(_bridge, "_pollVersion", freshPoll); // Same operation as a new real PollOnce request.
                if (!(bool)pollGuard.Invoke(_bridge, new object[] { freshPoll })) throw new InvalidOperationException("Fresh poll was rejected.");
                sync.Invoke(_bridge, new[] { originalTournament });
                if (Picks()[tag.Slot] != 0) throw new InvalidOperationException("Fresh authoritative poll lost accepted A.");
                pending[tag.Slot] = 302; Picks().Remove(tag.Slot);
                complete.Invoke(_bridge, new object[] { tag.Slot, 302, clearResponse, false });
                if ((bool)pollGuard.Invoke(_bridge, new object[] { freshPoll }) || Picks().ContainsKey(tag.Slot)
                    || ((Array)Get(originalTournament, "my_bets")).Length != 0)
                    throw new InvalidOperationException("An accepted cancellation can be overwritten by an older poll.");
                var result = "Deterministic actual completion + production poll-admission guard passed: old poll rejected after accepted A; newer pending B preserved; failed B restores accepted A; fresh poll admitted; accepted cancel remains empty. No ApplyPoll, match entry or RPC invoked.";
                Record("betting-race-verify", true, result); return result;
            }
            finally
            {
                Set(originalTournament, "my_bets", originalBets); Set(_bridge, "_lastTournament", originalTournament);
                pending.Clear(); foreach (var value in savedPending) pending[value.Key] = value.Value;
                Picks().Clear(); foreach (var value in savedPicks) Picks()[value.Key] = value.Value;
                Set(_bridge, "_pollVersion", (int)Get(_bridge, "_pollVersion") + 1); // Never revive a pre-fixture response.
                typeof(ArenaMatch8Bridge).GetMethod("ApplyBettingUiToAllRows", Private).Invoke(_bridge, null);
            }
        }

        private static PointerEventData AssertBetRaycast(Toggle toggle)
        {
            var canvas = toggle.GetComponentInParent<Canvas>();
            var point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                toggle.GetComponent<RectTransform>().TransformPoint(toggle.GetComponent<RectTransform>().rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(toggle.transform))
                throw new InvalidOperationException("Checkbox raycast failed at " + point + ": " + string.Join(" > ", hits.Take(5).Select(hit => hit.gameObject.name)));
            return pointer;
        }

        private static void ClickBet(Toggle toggle)
        {
            var pointer = AssertBetRaycast(toggle);
            ExecuteEvents.Execute(toggle.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Canvas.ForceUpdateCanvases();
        }

        private static void AssertBetSelection(PairRowBetSelect selection, int side, Toggle a, Toggle b)
        {
            if (selection.SelectedSide != side || a.isOn != (side == 0) || b.isOn != (side == 1))
                throw new InvalidOperationException("Actual mutually exclusive checkbox selection is incorrect.");
            if (side < 0 ? Picks().ContainsKey(selection.Slot) : !Picks().TryGetValue(selection.Slot, out var saved) || saved != side)
                throw new InvalidOperationException("Bridge local pick did not follow the real checkbox event.");
            foreach (var toggle in new[] { a, b })
                if (!(toggle.graphic is ExpeditionArenaCheckGraphic) || Mathf.Abs(toggle.graphic.canvasRenderer.GetAlpha() - (toggle.isOn ? 1 : 0)) > .01f)
                    throw new InvalidOperationException("Native checkbox check is missing or has the wrong visible state.");
        }

        public static string VerifyProduction()
        {
            if (_fixtureRoot == null || _productionTournament == null) throw new InvalidOperationException("Load a real backend snapshot first.");
            Canvas.ForceUpdateCanvases();
            var tags = _fixtureRoot.GetComponentsInChildren<ExpeditionArenaPairTag>(false);
            if (tags.Length != 7) throw new InvalidOperationException("Expected the complete 4/2/1 bracket.");
            var checkedRows = 0;
            var knownHealth = 0;
            foreach (var round in new[] { "qf", "sf", "final" })
            {
                var rows = Get(_productionTournament, round == "final" ? "final_pairs" : round) as Array;
                if (rows == null) continue;
                foreach (var pair in rows)
                {
                    var slot = (int)Get(pair, "slot");
                    var tag = tags.SingleOrDefault(t => t.Round == round && t.Slot == slot);
                    if (tag == null) throw new InvalidOperationException("Real pair missing from UI: " + round + "/" + slot);
                    foreach (var side in new[] { "a", "b" })
                    {
                        var name = tag.transform.Find(side == "a" ? "LeftName" : "RightName")?.GetComponent<TMP_Text>();
                        if (name == null || name.text != (string)Get(pair, "display_" + side)) throw new InvalidOperationException("Historical participant name changed or became a placeholder.");
                        var fill = tag.transform.Find("Hp" + side.ToUpperInvariant() + "/Fill")?.GetComponent<Image>();
                        var text = tag.transform.Find("HpStatus" + side.ToUpperInvariant())?.GetComponent<TMP_Text>();
                        var hp = (float)Get(pair, "hp_" + side);
                        var maximum = (float)Get(pair, "hp_max_" + side);
                        var known = (bool)Get(pair, "hp_known_" + side) && maximum > 0;
                        if (fill == null || text == null || fill.enabled != known) throw new InvalidOperationException("Actual HP visibility differs from the production DTO.");
                        if (known)
                        {
                            knownHealth++;
                            if (Mathf.Abs(fill.fillAmount - Mathf.Clamp01(hp / maximum)) > .001f || fill.sprite == null
                                || fill.sprite.texture != Texture2D.whiteTexture || fill.rectTransform.rect.height <= 0)
                                throw new InvalidOperationException("Actual HP fill ratio/sprite/height is incorrect.");
                            if (text.text != $"{hp:0}/{maximum:0}") throw new InvalidOperationException("Actual HP label differs from the DTO.");
                        }
                        else if (text.text != "HP —") throw new InvalidOperationException("Unknown HP was invented.");
                        var winner = (string)Get(pair, "winner_uid");
                        if ((string)Get(pair, "status") == "done")
                        {
                            var expected = winner == (string)Get(pair, "uid_" + side) ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                            if (Vector4.Distance(name.color, expected) > .01f) throw new InvalidOperationException("Winner/loser name styling is incorrect.");
                        }
                    }
                    checkedRows++;
                }
            }
            foreach (var line in _fixtureRoot.GetComponentsInChildren<ExpeditionArenaConnectorTag>(false))
            {
                if (string.IsNullOrEmpty(line.WinnerUid)) continue;
                var target = tags.Single(t => t.Round == line.TargetRound && t.Slot == line.TargetSlot);
                if (target.UidA != line.WinnerUid && target.UidB != line.WinnerUid)
                    throw new InvalidOperationException("Connector points to another player's next-round pair.");
            }
            if (!ArenaMatch8Bridge.IsParticipationLocked || !_bridge.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Tournament must lock navigation while keeping the bridge object active.");
            foreach (var name in new[] { "ExpeditionResourceHeader", "ExpeditionNavigation" })
            {
                var root = Resources.FindObjectsOfTypeAll<RectTransform>().FirstOrDefault(t => t.gameObject.scene == _bridge.gameObject.scene && t.name == name);
                var group = root != null ? root.GetComponent<CanvasGroup>() : null;
                if (group == null || group.alpha > .01f || group.interactable || group.blocksRaycasts)
                    throw new InvalidOperationException("HUD/navigation was not visually and interactively locked.");
            }
            return "Production DTO decoded; " + checkedRows + " real rows, " + knownHealth + " known HP values, actual winner routes and hidden/locked HUD verified; no RPC/match invoked.";
        }

        [Serializable] private sealed class BackendReport { public BackendSnapshot[] arena_ui_snapshots; }
        [Serializable] private sealed class BackendSnapshot { public string name, viewer_uid, poll_json; }

        public static string Run(string target)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required.");
            if (target.StartsWith("production|", StringComparison.Ordinal))
            {
                var parts = target.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[2], out var index)) throw new ArgumentException("Use production|absolute-report-path|snapshot-index.");
                return RunProductionSnapshot(parts[1], index);
            }
            if (target.StartsWith("betting|", StringComparison.Ordinal))
            {
                var parts = target.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[2], out var index)) throw new ArgumentException("Use betting|absolute-report-path|snapshot-index.");
                try { return RunBettingSnapshot(parts[1], index); }
                catch (Exception error)
                {
                    Record("betting-setup", false, error.ToString());
                    try { Restore(); }
                    catch (Exception cleanupError) { Debug.LogWarning("[ArenaBetQA] Cleanup after original failure: " + cleanupError); }
                    throw;
                }
            }
            try
            {
                string result;
                if (target == "restore") { Restore(); result = "Original UI/data restored. Normal bridge polling resumes if originally enabled."; }
                else if (target == "verify") result = Verify();
                else if (target == "verify-production") result = VerifyProduction();
                else if (target == "betting-verify") result = VerifyBetting();
                else if (target == "betting-race-verify") result = VerifyBetPollOrdering();
                else if (target == "queue-lock" || target == "queue-verify") result = QueueLock(target == "queue-lock");
                else
                {
                    if (target != "qf" && target != "sf" && target != "final") throw new ArgumentException("Use qf, sf, final, verify or restore.");
                    Prepare();
                    var state = Tournament(target);
                    Set(_bridge, "_lastTournament", state);
                    Set(_bridge, "_bettingRound", target);
                    Set(_bridge, "_betsOpen", true);
                    Picks().Clear(); Picks()[2] = 0;
                    typeof(ArenaMatch8Bridge).GetMethod("ShowBracket", Private).Invoke(_bridge, new[] { state });
                    _fixtureRoot = (GameObject)Get(_bridge, "_bracketRoot");
                    foreach (var row in _fixtureRoot.GetComponentsInChildren<PairRowBetSelect>(true))
                    {
                        var tag = row.GetComponent<ExpeditionArenaPairTag>();
                        if (tag != null && tag.Current && tag.Slot == 2) row.SetSideWithoutNotify(0);
                    }
                    // Keep the production selection artwork, but make any real bet callback unreachable.
                    foreach (var toggle in _fixtureRoot.GetComponentsInChildren<Toggle>(true)) toggle.interactable = false;
                    Canvas.ForceUpdateCanvases();
                    result = "UI-only " + target + " bracket: 7 pairs, authoritative HP fixture, no entry/match/RPC invoked. " + Verify();
                }
                Record(target, true, result); return result;
            }
            catch (Exception error) { Record(target, false, error.ToString()); throw; }
        }

        private static string QueueLock(bool create)
        {
            if (create)
            {
                Prepare();
                typeof(ArenaMatch8Bridge).GetField("_activeBridge", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _bridge);
                typeof(ArenaMatch8Bridge).GetMethod("SetParticipationLocked", Private).Invoke(_bridge, new object[] { true });
                typeof(ArenaMatch8Bridge).GetMethod("ApplyQueueLockState", Private).Invoke(_bridge, new object[] { true });
                var exit = (Button)Get(_bridge, "_leaveQueueButton");
                if (exit == null) throw new InvalidOperationException("Real queue exit control is missing.");
                if (_savedExitAction == null) { _savedExitAction = exit.onClick; _savedExitInteractable = exit.interactable; }
                exit.onClick = new Button.ButtonClickedEvent();
                exit.onClick.AddListener(() => _queueExitClicks++); exit.interactable = true;
                _queueExitClicks = 0;
                return "Queue fixture created; run queue-verify after one rendered frame to assert actual raycasts. Exit callback replaced locally; no RPC invoked.";
            }
            if (_bridge == null || !ArenaMatch8Bridge.IsParticipationLocked) throw new InvalidOperationException("Queue lock is not active.");
            Canvas.ForceUpdateCanvases();
            var leave = (Button)Get(_bridge, "_leaveQueueButton");
            if (leave == null || !leave.gameObject.activeInHierarchy || !leave.interactable)
                throw new InvalidOperationException("Queue exit is not available.");
            if (!_bridge.gameObject.activeInHierarchy) throw new InvalidOperationException("Queue lock disabled the bridge object.");
            foreach (var point in new[] { new Vector2(.5f, .95f), new Vector2(.5f, .5f), new Vector2(.5f, .025f) })
            {
                var hits = new List<RaycastResult>();
                var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(point.x * Screen.width, point.y * Screen.height) };
                EventSystem.current.RaycastAll(pointer, hits);
                if (hits.Count == 0 || hits[0].gameObject.name != "BlockAllUnderlyingInput")
                    throw new InvalidOperationException("Queue input probe failed: " + QueueRaycastDetails(pointer.position, hits));
            }
            var corners = new Vector3[4]; leave.GetComponent<RectTransform>().GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * .5f;
            var exitHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = center }, exitHits);
            if (exitHits.Count == 0 || !exitHits[0].gameObject.transform.IsChildOf(leave.transform))
                throw new InvalidOperationException("Queue exit probe failed: " + QueueRaycastDetails(center, exitHits));
            leave.onClick.Invoke();
            if (_queueExitClicks < 1) throw new InvalidOperationException("Queue exit local callback is missing.");
            return "Only the real queue exit is reachable; 3 underlying HUD/nav points blocked, bridge remains active. Exit callback temporarily replaced; no leave/refund RPC invoked.";
        }

        private static string QueueRaycastDetails(Vector2 point, List<RaycastResult> hits)
        {
            var blocker = Resources.FindObjectsOfTypeAll<Image>().FirstOrDefault(i => i != null && i.name == "BlockAllUnderlyingInput" && i.gameObject.scene == _bridge.gameObject.scene);
            var top = hits.Count == 0 ? "none" : string.Join(" > ", hits.Take(4).Select(h => h.gameObject.name + "(sort=" + h.sortingOrder + ",depth=" + h.depth + ")"));
            var state = blocker == null ? "missing" : "active=" + blocker.gameObject.activeInHierarchy + ",raycast=" + blocker.raycastTarget + ",depth=" + blocker.depth + ",culled=" + blocker.canvasRenderer.cull + ",cullTransparent=" + blocker.canvasRenderer.cullTransparentMesh;
            return "point=" + point + ",screen=" + Screen.width + "x" + Screen.height + ",hits=" + top + ",blocker=" + state;
        }

        private static void Prepare()
        {
            if (_bridge != null) return;
            _bridge = UnityEngine.Object.FindFirstObjectByType<ArenaMatch8Bridge>(FindObjectsInactive.Include);
            if (_bridge == null) throw new InvalidOperationException("Open the idle Arena menu first.");
            var existing = (GameObject)Get(_bridge, "_bracketRoot");
            var state = Get(_bridge, "_lastTournament");
            if ((bool)Get(_bridge, "_queueUiLocked") || (bool)Get(_bridge, "_busyJoin") ||
                (state != null && (bool)Get(state, "active")) || (existing != null && existing.activeSelf))
            { _bridge = null; throw new InvalidOperationException("Fixture requires idle menu with no queued/active tournament."); }
            _wasEnabled = _bridge.enabled;
            _savedSceneBody = Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(rect => rect.name == "ExpeditionScenePageHost" && rect.gameObject.scene == _bridge.gameObject.scene)?.gameObject;
            _sceneBodyWasActive = _savedSceneBody != null && _savedSceneBody.activeSelf;
            foreach (var name in SavedFields) Saved[name] = Get(_bridge, name);
            SavedPicks.Clear(); foreach (var pick in Picks()) SavedPicks[pick.Key] = pick.Value;
            // OnDisable only stops polling and closes the local stake chooser; no leave/refund call.
            _bridge.enabled = false;
            foreach (var name in new[] { "_bracketRoot", "_rowsRoot", "_bracketTitle", "_cdText", "_prizeFooterText", "_headerTemplate", "_pairRowTemplate" }) Set(_bridge, name, null);
            Set(_bridge, "_myUserId", "qa-player-1");
            Set(_bridge, "_bettingUiEnabled", true);
        }

        private static object Tournament(string round)
        {
            var type = typeof(ArenaMatch8Bridge).GetNestedType("ArenaTournamentState", BindingFlags.NonPublic);
            var value = Activator.CreateInstance(type, true);
            Set(value, "active", true); Set(value, "id", "ui-only-qa");
            Set(value, "phase", "countdown"); Set(value, "next_round", round); Set(value, "kind", "gold");
            Set(value, "bet_tier", "600"); Set(value, "countdown_left", 18); Set(value, "join_match_id", "");
            Set(value, "bets_open", true); Set(value, "betting_round", round);
            Set(value, "prize_base_gold", 3000); Set(value, "prize_preview_gold", 3600); Set(value, "bet_score", 1); Set(value, "bet_wins", 2); Set(value, "bet_losses", 1);
            Set(value, "qf", Pairs("qf", 4, round != "qf", true));
            Set(value, "sf", Pairs("sf", 2, round == "final", round != "qf"));
            Set(value, "final_pairs", Pairs("final", 1, false, round == "final"));
            return value;
        }

        private static Array Pairs(string round, int count, bool done, bool participantsKnown)
        {
            var type = typeof(ArenaMatch8Bridge).GetNestedType("ArenaPairRow", BindingFlags.NonPublic);
            var list = Array.CreateInstance(type, participantsKnown ? count : 0);
            for (var i = 0; i < list.Length; i++)
            {
                var pair = Activator.CreateInstance(type, true);
                var a = i == 0 ? "qa-player-1" : "qa-player-" + (i * 2 + 1);
                var b = "qa-player-" + (i * 2 + 2);
                Set(pair, "slot", i + 1); Set(pair, "uid_a", a); Set(pair, "uid_b", b);
                Set(pair, "display_a", i == 0 ? "Искатель" : "Разломщик" + (i + 1));
                Set(pair, "display_b", "Странник" + (i + 1)); Set(pair, "status", done ? "done" : "pending");
                Set(pair, "winner_uid", done ? a : ""); Set(pair, "match_id", "");
                // Unequal maxima deliberately reject the old /150 heuristic. Unknown is never full.
                Set(pair, "hp_a", done ? 730f : 0f); Set(pair, "hp_max_a", done ? 1000f : 0f); Set(pair, "hp_known_a", done);
                Set(pair, "hp_b", 0f); Set(pair, "hp_max_b", done ? 700f : 0f); Set(pair, "hp_known_b", done);
                list.SetValue(pair, i);
            }
            return list;
        }

        private static string Verify()
        {
            if (_fixtureRoot == null) throw new InvalidOperationException("Create qf/sf/final fixture first.");
            Canvas.ForceUpdateCanvases();
            var tags = _fixtureRoot.GetComponentsInChildren<ExpeditionArenaPairTag>(false);
            if (tags.Length != 7) throw new InvalidOperationException("Expected 7 visible bracket pairs, got " + tags.Length);
            var unknown = 0; var known = 0;
            foreach (var tag in tags)
            {
                foreach (var side in new[] { "A", "B" })
                {
                    var fill = tag.transform.Find("Hp" + side + "/Fill")?.GetComponent<Image>();
                    var label = tag.transform.Find("HpStatus" + side)?.GetComponent<TMP_Text>();
                    if (fill == null || label == null) throw new InvalidOperationException("Missing HP presentation.");
                    if (label.text == "HP —") { if (fill.enabled) throw new InvalidOperationException("Unknown HP must not show a fabricated fill."); unknown++; }
                    else
                    {
                        known++;
                        var expected = side == "A" ? .73f : 0;
                        if (!fill.enabled || Mathf.Abs(fill.fillAmount - expected) > .001f) throw new InvalidOperationException("HP ratio differs from authoritative fixture.");
                    }
                }
            }
            if (_fixtureRoot.GetComponentsInChildren<Toggle>(true).Any(t => t.interactable)) throw new InvalidOperationException("Fixture must not allow real bet callbacks.");
            var rect = _fixtureRoot.GetComponent<RectTransform>();
            if (rect.anchorMin.x < 0 || rect.anchorMin.y < 0 || rect.anchorMax.x > 1 || rect.anchorMax.y > 1) throw new InvalidOperationException("Bracket lies outside screen safe area.");
            ExpeditionWorldQa.Run("tournament-hidden");
            return "Verified 7 pairs, " + known + " known HP and " + unknown + " unknown HP values, bets blocked, bounds inside screen; arena menu and header/navigation hidden.";
        }

        private static void Restore()
        {
            if (_bridge == null) return;
            Set(_bridge, "EditorBetRequestOverride", null);
            ((Dictionary<int, int>)Get(_bridge, "_pendingBetVersions")).Clear();
            if (_bettingOptionListener != null)
            {
                if (_bettingOption != null)
                {
                    _bettingOption.onValueChanged.RemoveListener(_bettingOptionListener);
                    _bettingOption.SetIsOnWithoutNotify(_savedOptionOn);
                }
                if (_hadBettingPreference) PlayerPrefs.SetInt(BettingPreference, _savedBettingPreference);
                else PlayerPrefs.DeleteKey(BettingPreference);
                PlayerPrefs.Save();
                Set(_bridge, "EditorBetRequestOverride", null);
                _bettingOptionListener = null; _bettingOption = null; BetRequests.Clear();
            }
            if (_savedExitAction != null)
            {
                var exit = (Button)Get(_bridge, "_leaveQueueButton");
                if (exit != null) { exit.onClick = _savedExitAction; exit.interactable = _savedExitInteractable; }
                _savedExitAction = null;
                typeof(ArenaMatch8Bridge).GetMethod("ApplyQueueLockState", Private).Invoke(_bridge, new object[] { false });
            }
            typeof(ArenaMatch8Bridge).GetMethod("SetParticipationLocked", Private).Invoke(_bridge, new object[] { false });
            var partialRoot = (GameObject)Get(_bridge, "_bracketRoot");
            var originalRoot = Saved.TryGetValue("_bracketRoot", out var savedRoot) ? savedRoot as GameObject : null;
            if (_fixtureRoot == null && partialRoot != originalRoot) _fixtureRoot = partialRoot;
            if (_fixtureRoot != null && _fixtureRoot != originalRoot) { _fixtureRoot.SetActive(false); UnityEngine.Object.Destroy(_fixtureRoot); }
            // ShowBracket can fail halfway through construction, before its
            // overlay has an OnDisable capable of clearing this shared flag.
            ExpeditionPresentation.SetTournamentVisible(false);
            foreach (var saved in Saved) Set(_bridge, saved.Key, saved.Value);
            Picks().Clear(); foreach (var pick in SavedPicks) Picks()[pick.Key] = pick.Value;
            _bridge.enabled = _wasEnabled;
            if (!_wasEnabled) typeof(ArenaMatch8Bridge).GetField("_activeBridge", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            _bridge = null; _fixtureRoot = null; _productionTournament = null; Saved.Clear(); SavedPicks.Clear();
            var restoredBody = _savedSceneBody; var expectedActive = _sceneBodyWasActive;
            _savedSceneBody = null;
            if (restoredBody != null && restoredBody.activeSelf != expectedActive)
                throw new InvalidOperationException("Arena page visibility differs from its pre-fixture state: expected=" + expectedActive + ", actual=" + restoredBody.activeSelf);
        }

        private static Dictionary<int, int> Picks() => (Dictionary<int, int>)Get(_bridge, "_myBetSidesBySlot");
        private static object Get(object value, string field) => value.GetType().GetField(field, Private | BindingFlags.Public).GetValue(value);
        private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private | BindingFlags.Public).SetValue(value, data);
        private static void Record(string target, bool ok, string message)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), ".utmp/expedition/qa"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "arena-bracket-" + target + ".json"), JsonUtility.ToJson(new Report { target = target, ok = ok, message = message, utc = DateTime.UtcNow.ToString("O") }, true));
        }
        [Serializable] private sealed class Report { public string target; public bool ok; public string message; public string utc; }
    }
}
