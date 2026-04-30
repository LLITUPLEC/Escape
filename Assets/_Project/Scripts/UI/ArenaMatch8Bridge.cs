using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
using Project.Nakama;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Арена турнира 8 игроков: кнопка match3Arena, модалка ставок, очередь (queue),
    /// опрос сервера и оверлей сетки с HP и таймером.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMatch8Bridge : MonoBehaviour
    {
        private const string RpcArenaJoin = "duel_arena_queue_join";
        private const string RpcArenaLeave = "duel_arena_queue_leave";
        private const string RpcArenaPoll = "duel_arena_queue_poll";
        private const string BracketPrefabResourcesPath = "UI/ArenaBracketOverlay";

        [Header("Buttons (by GameObject name)")]
        [SerializeField] private string match3ArenaButtonName = "match3Arena";
        [SerializeField] private string match3ArenaOreButtonName = "match3Arena_Ore";
        [SerializeField] private string match3ArenaGoldButtonName = "match3Arena_Gold";
        [SerializeField] private string queueTextObjectName = "queue";
        [SerializeField] private string duelMatch3SceneName = "DuelMatch3";

        [Header("Queue lock UI (optional names; hides while in queue)")]
        [SerializeField] private string modePanelObjectName = "ModePanel";
        [SerializeField] private string backButtonObjectName = "BackButton";
        [SerializeField] private string match3StatsCardObjectName = "Match3StatsCard";

        [Header("Icons (опционально, из инспектора)")]
        [SerializeField] private Sprite iconGreenIngot;
        [SerializeField] private Sprite iconBlueIngot;
        [SerializeField] private Sprite iconPurpleIngot;
        [SerializeField] private Sprite iconOre;
        [SerializeField] private Sprite iconGold;

        private const string ArenaKindSmith = "smith";
        private const string ArenaKindOre = "ore";
        private const string ArenaKindGold = "gold";

        private Button _arenaButton;
        private Button _arenaOreButton;
        private Button _arenaGoldButton;
        private TMP_Text _queueTmp;
        private Button _leaveQueueButton;
        private GameObject _modePanelGo;
        private GameObject _backButtonGo;
        private GameObject _statsCardGo;
        private bool _queueUiLocked;

        private GameObject _modalRoot;
        private Transform _modalPanel;
        private TMP_Text _modalTitle;
        private Transform _modalCloseButtonTr;
        private GameObject _bracketRoot;
        private Transform _rowsRoot;
        private TMP_Text _bracketTitle;
        private TMP_Text _cdText;
        private TMP_Text _headerTemplate;
        private Transform _pairRowTemplate;
        private GameObject _toastRoot;
        private TMP_Text _toastText;
        private Coroutine _toastRoutine;

        private Coroutine _pollRoutine;
        private bool _busyJoin;
        private string _selectedArenaKind = ArenaKindSmith;

        /// <summary>Без Sprite у UI Image режим «Filled» + Fill Amount часто визуально не работает (сплошной прямоугольник).</summary>
        private static Sprite _arenaBracketHpSprite;

        private string _sceneArmMidGuard;
        private float _suppressFightReloadUntil;
        private static bool _registeredSceneHooks;
        private static string _lastPrevSceneNameForArenaUi;
        private string _lastBracketSignature = string.Empty;

        /// <summary>JoinMatch недоступен (матч уже удалён) — не грузить DuelMatch8 повторно с тем же id.</summary>
        private static readonly HashSet<string> BlockedArenaJoinMatchIds = new();

        public static void BlockArenaJoinMatchId(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId)) return;
            BlockedArenaJoinMatchIds.Add(matchId.Trim());
        }

        [Serializable]
        private struct ArenaJoinPayload
        {
            public string bet_tier;
            public int session_epoch;
            public string arena_kind;
        }

        [Serializable]
        private sealed class ArenaPollResponse
        {
            public bool ok;
            public int queue_count;
            public int queue_max;
            public bool in_queue;
            public string queue_bet_tier;
            public string queue_kind;
            public ArenaTournamentState tournament;
            public string err;
        }

        [Serializable]
        private sealed class ArenaJoinResponse
        {
            public bool ok;
            public string err;
            public string ingot_def;
            public int queue_count;
            public int queue_max;
            public string bet_tier;
            public bool in_tournament;
        }

        [Serializable]
        private sealed class ArenaLeaveResponse
        {
            public bool ok;
            public string err;
            public int queue_count;
            public int queue_max;
        }

        [Serializable]
        private sealed class ArenaTournamentState
        {
            public bool active;
            public string id;
            public bool eliminated;
            public string phase;
            public string next_round;
            public string bet_tier;
            public string kind;
            public int countdown_left;
            public string join_match_id;
            public bool join_opponent_is_bot;
            public ArenaPairRow[] qf;
            public ArenaPairRow[] sf;
            public ArenaPairRow[] final_pairs;
        }

        [Serializable]
        private sealed class ArenaPairRow
        {
            public int slot;
            public string match_id;
            public string uid_a;
            public string uid_b;
            public string display_a;
            public string display_b;
            public float hp_a;
            public float hp_b;
            public string status;
            public string winner_uid;
        }

        private void Awake()
        {
            if (!_registeredSceneHooks)
            {
                _registeredSceneHooks = true;
                SceneManager.activeSceneChanged += OnAnyActiveSceneChanged;
            }

            NakamaBootstrap.EnsureExists();
            _arenaButton = FindComponentByGameObjectName<Button>(match3ArenaButtonName);
            if (_arenaButton != null)
                _arenaButton.onClick.AddListener(() => OpenBetModal(ArenaKindSmith));

            _arenaOreButton = FindComponentByGameObjectName<Button>(match3ArenaOreButtonName);
            if (_arenaOreButton != null)
                _arenaOreButton.onClick.AddListener(() => OpenBetModal(ArenaKindOre));

            _arenaGoldButton = FindComponentByGameObjectName<Button>(match3ArenaGoldButtonName);
            if (_arenaGoldButton != null)
                _arenaGoldButton.onClick.AddListener(() => OpenBetModal(ArenaKindGold));

            var q = FindComponentByGameObjectName<TMP_Text>(queueTextObjectName);
            if (q != null)
                _queueTmp = q;

            _modePanelGo = FindByName(modePanelObjectName);
            _backButtonGo = FindByName(backButtonObjectName);
            _statsCardGo = FindByName(match3StatsCardObjectName);
            EnsureLeaveQueueButtonBuilt();

            EnsureBetModalBuilt();
            EnsureBracketBuilt();
        }

        private static void OnAnyActiveSceneChanged(Scene prev, Scene next)
        {
            _lastPrevSceneNameForArenaUi = prev.IsValid() ? prev.name : "";
        }

        private void OnEnable()
        {
            if (_lastPrevSceneNameForArenaUi == duelMatch3SceneName)
                _suppressFightReloadUntil = Time.realtimeSinceStartup + 8f;

            if (_pollRoutine != null)
                StopCoroutine(_pollRoutine);
            _pollRoutine = StartCoroutine(PollLoop());
        }

        private void OnDisable()
        {
            if (_pollRoutine != null)
            {
                StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(1.35f);
            while (enabled)
            {
                _ = PollOnceFireAndForget();
                yield return wait;
            }
        }

        private async Task PollOnceFireAndForget()
        {
            if (!NakamaBootstrap.Instance.IsReady)
                return;
            try
            {
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() +
                              ",\"arena_kind\":\"" + (_selectedArenaKind ?? ArenaKindSmith) + "\"}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaPoll, payload);
                var raw = rpc?.Payload ?? "{}";
                var model = JsonUtility.FromJson<ArenaPollResponse>(raw);
                if (model == null || !model.ok)
                    return;

                MainThreadDispatcher.Enqueue(() => ApplyPoll(model));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] poll: " + e.Message);
            }
        }

        private void ApplyPoll(ArenaPollResponse m)
        {
            UpdateQueueUi(m);

            if (m.tournament != null && m.tournament.active && m.tournament.eliminated)
            {
                _lastBracketSignature = string.Empty;
                HideBracket();
                return;
            }

            if (m.tournament != null && m.tournament.active && !m.tournament.eliminated)
            {
                var sig = BuildBracketSignature(m.tournament);
                if (sig != _lastBracketSignature)
                {
                    _lastBracketSignature = sig;
                    ShowBracket(m.tournament);
                }
                else
                    RefreshBracketInPlace(m.tournament);

                TryArmFightScene(m.tournament);
            }
            else
            {
                _lastBracketSignature = string.Empty;
                HideBracket();
            }
        }

        private static string BuildBracketSignature(ArenaTournamentState t)
        {
            if (t == null) return string.Empty;
            return $"{t.phase}|{t.countdown_left}|{t.join_match_id ?? ""}|{RowsSignature(t.qf)}{RowsSignature(t.sf)}{RowsSignature(t.final_pairs)}";
        }

        private static string RowsSignature(ArenaPairRow[] rows)
        {
            if (rows == null || rows.Length == 0) return string.Empty;
            var s = string.Empty;
            foreach (var r in rows)
            {
                if (r == null) continue;
                // HP обновляются отдельно в RefreshBracketInPlace (каждый poll), чтобы полоски «дышали» без пересборки рядов.
                s += $"{r.slot}:{r.status}:{r.match_id ?? ""}:{r.winner_uid ?? ""};";
            }

            return s;
        }

        private void RefreshArenaCountdownOnly(ArenaTournamentState t)
        {
            if (_bracketRoot == null || t == null) return;
            ApplyBracketOverlayTitle(t);
            var cd = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            if (cd == null) return;
            if (t.phase == "countdown")
                cd.text = Mathf.Max(0, t.countdown_left).ToString();
            else
                cd.text = PhaseTitle(t.phase);
        }

        private static string BracketOverlayMainTitle(string kindRaw)
        {
            var k = string.IsNullOrWhiteSpace(kindRaw) ? ArenaKindSmith : kindRaw.Trim().ToLowerInvariant();
            if (k == ArenaKindOre)
                return "Турнир Руды";
            if (k == ArenaKindGold)
                return "Турнир Золота";
            return "Турнир Кузнеца";
        }

        private void ApplyBracketOverlayTitle(ArenaTournamentState t)
        {
            if (_bracketTitle == null || t == null)
                return;
            _bracketTitle.text = BracketOverlayMainTitle(t.kind);
        }

        private ArenaPairRow[] RowsForCurrentBracketPhase(ArenaTournamentState t)
        {
            if (t == null) return null;
            if (t.phase == "qf") return t.qf;
            if (t.phase == "sf") return t.sf;
            if (t.phase == "final") return t.final_pairs;
            if (t.phase == "countdown")
            {
                if (string.Equals(t.next_round, "sf", StringComparison.Ordinal)) return t.sf;
                if (string.Equals(t.next_round, "final", StringComparison.Ordinal)) return t.final_pairs;
                return t.qf;
            }

            return null;
        }

        /// <summary>Перерисовка HP/статусов без пересборки сетки (сервер шлёт актуальные hp_* из mirror_commit).</summary>
        private void RefreshBracketInPlace(ArenaTournamentState t)
        {
            if (_bracketRoot == null || !_bracketRoot.activeInHierarchy || _rowsRoot == null || t == null)
                return;

            RefreshArenaCountdownOnly(t);

            var rows = RowsForCurrentBracketPhase(t);
            if (rows == null || rows.Length == 0)
                return;

            var list = CollectBracketRows(_rowsRoot);
            var n = Mathf.Min(list.Count, rows.Length);
            for (var i = 0; i < n; i++)
                BindPairRowData(list[i], rows[i]);
        }

        private static List<Transform> CollectBracketRows(Transform rowsRoot)
        {
            var list = new List<Transform>();
            if (rowsRoot == null)
                return list;
            for (var i = 0; i < rowsRoot.childCount; i++)
            {
                var ch = rowsRoot.GetChild(i);
                if (ch != null && ch.name == "BracketRow")
                    list.Add(ch);
            }

            return list;
        }

        private static Image ResolveHpFillImage(Transform row, string barName)
        {
            if (row == null || string.IsNullOrEmpty(barName))
                return null;

            var fillPath = barName + "/Fill";
            var fill = row.Find(fillPath)?.GetComponent<Image>();
            if (fill != null)
                return fill;

            var bar = row.Find(barName);
            if (bar == null)
                return null;

            fill = bar.Find("Fill")?.GetComponent<Image>();
            if (fill != null)
                return fill;

            var img = bar.GetComponent<Image>();
            return img != null && img.type == Image.Type.Filled ? img : null;
        }

        /// <summary>Привязать данные пары к уже созданному ряду (prefab или процедурный).</summary>
        private static void BindPairRowData(Transform row, ArenaPairRow pr)
        {
            if (row == null || pr == null)
                return;

            var leftName = row.Find("LeftName")?.GetComponent<TMP_Text>();
            if (leftName == null) leftName = row.Find("Txt")?.GetComponent<TMP_Text>();
            if (leftName != null)
                leftName.text = pr.display_a ?? "";

            var rightName = row.Find("RightName")?.GetComponent<TMP_Text>();
            if (rightName != null)
                rightName.text = pr.display_b ?? "";

            var hpA = ResolveHpFillImage(row, "HpA");
            if (hpA != null)
                SetHp(hpA, pr.hp_a);
            else
            {
                var legacyHp = row.Find("Hp")?.GetComponent<Image>();
                if (legacyHp != null)
                    SetHp(legacyHp, pr.hp_a);
            }

            var hpB = ResolveHpFillImage(row, "HpB");
            if (hpB != null)
                SetHp(hpB, pr.hp_b);

            var status = row.Find("Status")?.GetComponent<TMP_Text>();
            if (status == null)
                status = row.Find("Vs/Status")?.GetComponent<TMP_Text>();
            if (status != null)
                status.text = FormatPairStatus(pr.status);
        }

        private void UpdateQueueUi(ArenaPollResponse m)
        {
            if (_queueTmp == null)
                return;

            if (m != null && m.in_queue && !string.IsNullOrWhiteSpace(m.queue_kind))
                _selectedArenaKind = m.queue_kind.Trim().ToLowerInvariant();

            var show = m.in_queue || (m.queue_count > 0);
            _queueTmp.gameObject.SetActive(show);
            if (show)
                _queueTmp.text = $"{Mathf.Clamp(m.queue_count, 0, m.queue_max > 0 ? m.queue_max : 8)}/{(m.queue_max > 0 ? m.queue_max : 8)}";

            ApplyQueueLockState(m.in_queue);
        }

        private void ApplyQueueLockState(bool inQueue)
        {
            if (_queueUiLocked == inQueue)
            {
                if (_leaveQueueButton != null)
                    _leaveQueueButton.gameObject.SetActive(inQueue);
                return;
            }

            _queueUiLocked = inQueue;
            if (_modePanelGo != null) _modePanelGo.SetActive(!inQueue);
            if (_backButtonGo != null) _backButtonGo.SetActive(!inQueue);
            if (_statsCardGo != null) _statsCardGo.SetActive(!inQueue);
            if (_leaveQueueButton != null) _leaveQueueButton.gameObject.SetActive(inQueue);
        }

        private void TryArmFightScene(ArenaTournamentState t)
        {
            if (string.IsNullOrEmpty(t.join_match_id))
            {
                _sceneArmMidGuard = null;
                return;
            }

            if (Time.realtimeSinceStartup < _suppressFightReloadUntil)
                return;

            if (BlockedArenaJoinMatchIds.Contains(t.join_match_id))
                return;

            if (_sceneArmMidGuard == t.join_match_id)
                return;

            _sceneArmMidGuard = t.join_match_id;
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
            var myUid = NakamaBootstrap.Instance?.Session?.UserId;
            var oppHint = ResolveArenaOpponentDisplay(t, myUid);
            Match3LaunchContext.ArmArenaJoin(t.join_match_id, oppHint, t.join_opponent_is_bot);

            if (!string.IsNullOrEmpty(duelMatch3SceneName))
            {
                // Fail-safe: ensure bracket overlay can't survive scene switch visually.
                HideBracket();
                if (_bracketRoot != null)
                {
                    UnityEngine.Object.Destroy(_bracketRoot);
                    _bracketRoot = null;
                }
                SceneManager.LoadScene(duelMatch3SceneName);
            }
        }

        private static string ResolveArenaOpponentDisplay(ArenaTournamentState t, string myUserId)
        {
            if (t == null || string.IsNullOrEmpty(t.join_match_id) || string.IsNullOrEmpty(myUserId))
                return null;
            var mid = t.join_match_id.Trim();
            var h = ScanArenaRowsForOpponent(t.qf, mid, myUserId)
                    ?? ScanArenaRowsForOpponent(t.sf, mid, myUserId)
                    ?? ScanArenaRowsForOpponent(t.final_pairs, mid, myUserId);
            return string.IsNullOrWhiteSpace(h) ? null : h.Trim();
        }

        private static string ScanArenaRowsForOpponent(ArenaPairRow[] rows, string matchId, string myUid)
        {
            if (rows == null || rows.Length == 0) return null;
            foreach (var pr in rows)
            {
                if (pr == null || string.IsNullOrEmpty(pr.match_id)) continue;
                if (!string.Equals(pr.match_id.Trim(), matchId, StringComparison.Ordinal)) continue;

                if (string.Equals(pr.uid_a, myUid, StringComparison.Ordinal))
                    return pr.display_b;
                if (string.Equals(pr.uid_b, myUid, StringComparison.Ordinal))
                    return pr.display_a;
            }

            return null;
        }

        private void OpenBetModal(string kind)
        {
            if (_busyJoin || _modalRoot == null)
                return;
            _selectedArenaKind = string.IsNullOrWhiteSpace(kind) ? ArenaKindSmith : kind.Trim().ToLowerInvariant();
            RebuildBetModalRowsForKind(_selectedArenaKind);
            _modalRoot.SetActive(true);
        }

        public void CloseBetModal()
        {
            if (_modalRoot != null)
                _modalRoot.SetActive(false);
        }

        public async void OnPickBetGreen()
        {
            await QueueJoinAsync("green");
        }

        public async void OnPickBetBlue()
        {
            await QueueJoinAsync("blue");
        }

        public async void OnPickBetPurple()
        {
            await QueueJoinAsync("purple");
        }

        public async void OnPickBetFixed()
        {
            await QueueJoinAsync("fixed");
        }

        private async Task QueueJoinAsync(string tier)
        {
            if (_busyJoin || !NakamaBootstrap.Instance.IsReady)
                return;
            _busyJoin = true;
            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(default);
                var payload = JsonUtility.ToJson(new ArenaJoinPayload
                {
                    bet_tier = tier,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                    arena_kind = _selectedArenaKind ?? ArenaKindSmith,
                });
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaJoin, payload);
                var raw = rpc?.Payload ?? "{}";
                var resp = JsonUtility.FromJson<ArenaJoinResponse>(raw);
                if (resp != null && resp.ok)
                {
                    CloseBetModal();
                    if (_queueTmp != null)
                    {
                        var qMax = resp.queue_max > 0 ? resp.queue_max : 8;
                        var showQueue = resp.queue_count > 0 && !resp.in_tournament;
                        _queueTmp.gameObject.SetActive(showQueue);
                        if (showQueue)
                            _queueTmp.text =
                                $"{Mathf.Clamp(resp.queue_count, 0, qMax)}/{qMax}";
                    }

                    _ = PollOnceFireAndForget();
                }
                else
                {
                    var err = resp?.err ?? raw;
                    Debug.LogWarning("[Arena8] join failed: " + err);
                    if (string.Equals(resp?.err, "not_enough_ingots", StringComparison.Ordinal))
                        ShowToast("Недостаточно слитков для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "not_enough_ore", StringComparison.Ordinal))
                        ShowToast("Недостаточно руды для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "not_enough_gold", StringComparison.Ordinal))
                        ShowToast("Недостаточно золота для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "bet_tier_mismatch", StringComparison.Ordinal))
                        ShowToast("Очередь занята другой ставкой", 2.6f);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] join: " + e.Message);
            }
            finally
            {
                _busyJoin = false;
            }
        }

        /// <summary>Покинуть очередь с возвратом слитков (RPC).</summary>
        public async void LeaveQueueClicked()
        {
            if (!NakamaBootstrap.Instance.IsReady)
                return;
            try
            {
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() +
                              ",\"arena_kind\":\"" + (_selectedArenaKind ?? ArenaKindSmith) + "\"}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaLeave, payload);
                var raw = rpc?.Payload ?? "{}";
                var resp = JsonUtility.FromJson<ArenaLeaveResponse>(raw);
                if (resp != null && resp.ok)
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (_queueTmp != null)
                            _queueTmp.gameObject.SetActive(false);
                        ApplyQueueLockState(false);
                    });
                    _ = PollOnceFireAndForget();
                }
                else
                {
                    Debug.LogWarning("[Arena8] leave failed: " + (resp?.err ?? raw));
                    ShowToast("Не удалось выйти из очереди", 2.2f);
                    _ = PollOnceFireAndForget();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] leave: " + e.Message);
            }
        }

        private void EnsureLeaveQueueButtonBuilt()
        {
            if (_leaveQueueButton != null || _queueTmp == null)
                return;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            var go = new GameObject("ArenaLeaveQueueButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);

            var rt = go.GetComponent<RectTransform>();
            // Фиксированно: правый нижний угол экрана с полями 100 px.
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-100f, 100f);
            rt.sizeDelta = new Vector2(260f, 54f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.35f, 0.55f, 0.95f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(LeaveQueueClicked);

            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(go.transform, false);
            var tmp = txtGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "Выйти из очереди";
            tmp.fontSize = 22;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            _leaveQueueButton = btn;
            _leaveQueueButton.gameObject.SetActive(false);
        }

        private static GameObject FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var go = GameObject.Find(name);
            if (go != null) return go;
            var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in all)
            {
                if (t != null && string.Equals(t.gameObject.name, name, StringComparison.Ordinal))
                    return t.gameObject;
            }
            return null;
        }

        private void EnsureBetModalBuilt()
        {
            if (_modalRoot != null)
                return;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            _modalRoot = new GameObject("ArenaBetModal");
            _modalRoot.transform.SetParent(canvas.transform, false);
            var rect = _modalRoot.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var dim = _modalRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);
            dim.raycastTarget = true;
            // Click on dim background closes modal.
            var dimBtn = _modalRoot.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(() =>
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
            });

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_modalRoot.transform, false);
            var prect = panel.AddComponent<RectTransform>();
            prect.anchorMin = new Vector2(0.5f, 0.5f);
            prect.anchorMax = new Vector2(0.5f, 0.5f);
            prect.sizeDelta = new Vector2(780f, 540f);

            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.12f, 0.2f, 0.96f);

            var vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(28, 28, 28, 28);
            vl.spacing = 14f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childForceExpandHeight = false;
            vl.childForceExpandWidth = true;

            _modalPanel = panel.transform;
            var titleGo = CreateTmp(panel.transform, "Ставка турнира и награда при победе", 26, FontStyles.Bold);
            titleGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            _modalTitle = titleGo.GetComponent<TMP_Text>();

            var closeBtn = CreateButton(panel.transform, "Закрыть", () =>
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
            });
            _modalCloseButtonTr = closeBtn != null ? closeBtn.transform : null;

            _modalRoot.SetActive(false);
            RebuildBetModalRowsForKind(_selectedArenaKind);
        }

        private void RebuildBetModalRowsForKind(string kind)
        {
            if (_modalPanel == null)
                return;

            // Remove all bet rows from previous modal state (order-independent).
            for (var i = _modalPanel.childCount - 1; i >= 0; i--)
            {
                var ch = _modalPanel.GetChild(i);
                if (ch == null) continue;
                if (ch == _modalCloseButtonTr) continue;
                if (ch == _modalTitle?.transform) continue;
                if (ch.name.StartsWith("Row_", StringComparison.Ordinal)) Destroy(ch.gameObject);
            }

            var k = string.IsNullOrWhiteSpace(kind) ? ArenaKindSmith : kind.Trim().ToLowerInvariant();
            if (_modalTitle != null)
                _modalTitle.text = k == ArenaKindSmith
                    ? "Ставка турнира и награда при победе"
                    : (k == ArenaKindOre ? "Турнир руды: ставка и награда при победе" : "Турнир золота: ставка и награда при победе");

            if (k == ArenaKindOre)
            {
                MakeFlexibleSpacerRow(_modalPanel, "Row_spacer_top");
                MakeFixedBetRow(_modalPanel, "500 руды", "2500 руды", iconOre);
                MakeFlexibleSpacerRow(_modalPanel, "Row_spacer_bottom");
            }
            else if (k == ArenaKindGold)
            {
                MakeFlexibleSpacerRow(_modalPanel, "Row_spacer_top");
                MakeFixedBetRow(_modalPanel, "600 золота", "3000 золота", iconGold);
                MakeFlexibleSpacerRow(_modalPanel, "Row_spacer_bottom");
            }
            else
            {
                MakeBetRow(_modalPanel, "green", "50 зелёных слитков", "300 руды · 300 золота", iconGreenIngot);
                MakeBetRow(_modalPanel, "blue", "50 синих слитков", "600 руды · 600 золота", iconBlueIngot);
                MakeBetRow(_modalPanel, "purple", "50 фиолетовых слитков", "1200 руды · 1200 золота", iconPurpleIngot);
            }
        }

        private void MakeFlexibleSpacerRow(Transform parent, string name)
        {
            if (parent == null) return;
            var sp = new GameObject(string.IsNullOrWhiteSpace(name) ? "Row_spacer" : name);
            sp.transform.SetParent(parent, false);
            if (_modalCloseButtonTr != null)
                sp.transform.SetSiblingIndex(_modalCloseButtonTr.GetSiblingIndex());
            var le = sp.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 0f;
            le.preferredHeight = 0f;
        }

        private void MakeFixedBetRow(Transform parent, string cost, string prize, Sprite icon)
        {
            var row = new GameObject("Row_fixed");
            row.transform.SetParent(parent, false);
            if (_modalCloseButtonTr != null)
                row.transform.SetSiblingIndex(_modalCloseButtonTr.GetSiblingIndex());
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 150f);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 150f;
            rowLe.preferredHeight = 150f;
            rowLe.flexibleHeight = 0f;

            var rowHl = row.AddComponent<HorizontalLayoutGroup>();
            rowHl.spacing = 10f;
            rowHl.childAlignment = TextAnchor.MiddleLeft;
            rowHl.childForceExpandWidth = true;
            rowHl.padding = new RectOffset(8, 8, 8, 8);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.12f, 0.28f, 0.52f, 0.55f);

            if (icon != null)
            {
                var ico = new GameObject("ResIcon");
                ico.transform.SetParent(row.transform, false);
                var img = ico.AddComponent<Image>();
                img.sprite = icon;
                var irt = ico.GetComponent<RectTransform>();
                irt.sizeDelta = new Vector2(48f, 48f);
                var le = ico.AddComponent<LayoutElement>();
                le.preferredWidth = 52f;
                le.preferredHeight = 52f;
            }

            var leftCol = new GameObject("CostCol");
            leftCol.transform.SetParent(row.transform, false);
            var vl = leftCol.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f;
            vl.childAlignment = TextAnchor.MiddleLeft;
            var costTxt = CreateTmp(leftCol.transform, cost, 22, FontStyles.Normal);
            costTxt.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 58f);

            var prizeGo = new GameObject("PrizeCol");
            prizeGo.transform.SetParent(row.transform, false);
            var prizeHl = prizeGo.AddComponent<HorizontalLayoutGroup>();
            prizeHl.spacing = 8f;
            prizeHl.childAlignment = TextAnchor.MiddleRight;

            if (icon != null)
                CreateIconImage(prizeGo.transform, icon);
            CreateTmp(prizeGo.transform, prize, 22, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(OnPickBetFixed);
        }

        private void EnsureToastBuilt()
        {
            if (_toastRoot != null)
                return;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            _toastRoot = new GameObject("ArenaToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _toastRoot.transform.SetParent(canvas.transform, false);
            var rt = _toastRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -22f);
            rt.sizeDelta = new Vector2(720f, 64f);

            var bg = _toastRoot.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            bg.raycastTarget = false;

            var cg = _toastRoot.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_toastRoot.transform, false);
            var txt = txtGo.GetComponent<TextMeshProUGUI>();
            txt.text = "";
            txt.fontSize = 26;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 8f);
            trt.offsetMax = new Vector2(-14f, -8f);

            _toastText = txt;
            _toastRoot.SetActive(false);
        }

        private void ShowToast(string msg, float seconds)
        {
            EnsureToastBuilt();
            if (_toastRoot == null || _toastText == null)
                return;

            if (_toastRoutine != null)
            {
                StopCoroutine(_toastRoutine);
                _toastRoutine = null;
            }
            _toastRoutine = StartCoroutine(ToastRoutine(msg, seconds));
        }

        private IEnumerator ToastRoutine(string msg, float seconds)
        {
            _toastRoot.SetActive(true);
            _toastText.text = msg ?? "";
            var cg = _toastRoot.GetComponent<CanvasGroup>();
            if (cg == null) yield break;
            cg.alpha = 1f;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, seconds));
            cg.alpha = 0f;
            _toastRoot.SetActive(false);
            _toastRoutine = null;
        }

        private void MakeBetRow(Transform parent, string tier, string cost, string prize, Sprite ingotSp)
        {
            var row = new GameObject("Row_" + tier);
            row.transform.SetParent(parent, false);
            if (_modalCloseButtonTr != null)
                row.transform.SetSiblingIndex(_modalCloseButtonTr.GetSiblingIndex());
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 96f);

            var rowHl = row.AddComponent<HorizontalLayoutGroup>();
            rowHl.spacing = 10f;
            rowHl.childAlignment = TextAnchor.MiddleLeft;
            rowHl.childForceExpandWidth = true;
            rowHl.padding = new RectOffset(8, 8, 8, 8);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.12f, 0.28f, 0.52f, 0.55f);

            if (ingotSp != null)
            {
                var icon = new GameObject("IngotIcon");
                icon.transform.SetParent(row.transform, false);
                var img = icon.AddComponent<Image>();
                img.sprite = ingotSp;
                var irt = icon.GetComponent<RectTransform>();
                irt.sizeDelta = new Vector2(48f, 48f);
                var le = icon.AddComponent<LayoutElement>();
                le.preferredWidth = 52f;
                le.preferredHeight = 52f;
            }

            var leftCol = new GameObject("CostCol");
            leftCol.transform.SetParent(row.transform, false);
            var vl = leftCol.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f;
            vl.childAlignment = TextAnchor.MiddleLeft;
            var costTxt = CreateTmp(leftCol.transform, cost, 22, FontStyles.Normal);
            costTxt.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 58f);

            var prizeGo = new GameObject("PrizeCol");
            prizeGo.transform.SetParent(row.transform, false);
            var prizeHl = prizeGo.AddComponent<HorizontalLayoutGroup>();
            prizeHl.spacing = 8f;
            prizeHl.childAlignment = TextAnchor.MiddleRight;

            if (iconOre != null)
                CreateIconImage(prizeGo.transform, iconOre);
            var prizeOreTxt = CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

            if (iconGold != null)
                CreateIconImage(prizeGo.transform, iconGold);
            var prizeGoldTxt = CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(() =>
            {
                if (tier == "green") OnPickBetGreen();
                else if (tier == "blue") OnPickBetBlue();
                else OnPickBetPurple();
            });
        }

        private static GameObject CreateIconImage(Transform parent, Sprite sp)
        {
            var icon = new GameObject("Ic");
            icon.transform.SetParent(parent, false);
            var img = icon.AddComponent<Image>();
            img.sprite = sp;
            var rt = icon.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 28f);
            var le = icon.AddComponent<LayoutElement>();
            le.preferredWidth = 30f;
            le.preferredHeight = 30f;
            return icon;
        }

        private static GameObject CreateTmp(Transform parent, string text, float size, FontStyles fs)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = fs;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = Color.white;
            return go;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.35f, 0.55f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 52f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var tmpGo = CreateTmp(go.transform, label, 22, FontStyles.Bold);
            tmpGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            tmpGo.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            tmpGo.GetComponent<RectTransform>().anchorMax = Vector2.one;
            tmpGo.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            tmpGo.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            return btn;
        }

        private void EnsureBracketBuilt()
        {
            if (_bracketRoot != null)
                return;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            var prefab = Resources.Load<GameObject>(BracketPrefabResourcesPath);
            if (prefab != null)
                _bracketRoot = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            else
                _bracketRoot = new GameObject("ArenaBracketOverlay", typeof(RectTransform));

            if (_bracketRoot.transform.parent != canvas.transform)
                _bracketRoot.transform.SetParent(canvas.transform, false);

            _bracketTitle = _bracketRoot.transform.Find("Title")?.GetComponent<TMP_Text>();
            _cdText = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            _rowsRoot = _bracketRoot.transform.Find("ArenaRows");
            _headerTemplate = _rowsRoot != null
                ? _rowsRoot.Find("RoundHeaderTemplate")?.GetComponent<TMP_Text>()
                : null;
            _pairRowTemplate = _rowsRoot != null
                ? _rowsRoot.Find("PairRowTemplate")
                : null;

            // Fallback: if prefab missing OR prefab does not contain required nodes, build procedurally.
            if (_rowsRoot == null || _cdText == null)
            {
                UnityEngine.Object.Destroy(_bracketRoot);
                _bracketRoot = BuildBracketProcedural(canvas.transform);
                _bracketTitle = _bracketRoot.transform.Find("Title")?.GetComponent<TMP_Text>();
                _cdText = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
                _rowsRoot = _bracketRoot.transform.Find("ArenaRows");
                _headerTemplate = null;
                _pairRowTemplate = null;
            }

            _bracketRoot.SetActive(false);
        }

        private static GameObject BuildBracketProcedural(Transform canvas)
        {
            var root = new GameObject("ArenaBracketOverlay");
            root.transform.SetParent(canvas, false);
            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var cg = root.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var vl = root.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(24, 24, 90, 24);
            vl.spacing = 12f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.06f, 0.12f, 0.88f);
            bg.raycastTarget = true;

            var titleGo = CreateTmp(root.transform, BracketOverlayMainTitle(ArenaKindSmith), 28, FontStyles.Bold);
            titleGo.name = "Title";
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 44f;
            titleLe.flexibleWidth = 1f;

            var cdGo = CreateTmp(root.transform, "-", 36, FontStyles.Bold);
            cdGo.name = "ArenaCd";
            var cdLe = cdGo.AddComponent<LayoutElement>();
            cdLe.preferredHeight = 52f;
            cdLe.flexibleWidth = 1f;

            var rowsGo = new GameObject("ArenaRows");
            rowsGo.transform.SetParent(root.transform, false);
            var rowsRt = rowsGo.AddComponent<RectTransform>();
            rowsRt.anchorMin = Vector2.zero;
            rowsRt.anchorMax = Vector2.one;
            rowsRt.offsetMin = Vector2.zero;
            rowsRt.offsetMax = Vector2.zero;
            var rowsLe = rowsGo.AddComponent<LayoutElement>();
            rowsLe.flexibleHeight = 1f;
            rowsLe.minHeight = 260f;
            rowsLe.flexibleWidth = 1f;

            var rowsVl = rowsGo.AddComponent<VerticalLayoutGroup>();
            rowsVl.spacing = 10f;
            rowsVl.padding = new RectOffset(4, 4, 4, 4);
            rowsVl.childAlignment = TextAnchor.UpperCenter;
            rowsVl.childControlHeight = true;
            rowsVl.childControlWidth = true;
            rowsVl.childForceExpandWidth = true;
            rowsVl.childForceExpandHeight = false;

            return root;
        }

        private void ShowBracket(ArenaTournamentState t)
        {
            EnsureBracketBuilt();
            if (_bracketRoot == null)
                return;

            _bracketRoot.SetActive(true);

            ApplyBracketOverlayTitle(t);

            if (_cdText != null)
            {
                if (t.phase == "countdown")
                    _cdText.text = Mathf.Max(0, t.countdown_left).ToString();
                else
                    _cdText.text = PhaseTitle(t.phase);
            }

            if (_rowsRoot == null)
                return;

            ClearRowsKeepingTemplates(_rowsRoot);

            // Show only current/upcoming round (no history).
            if (t.phase == "qf")
                LayoutBracketRoundFromTemplate(_rowsRoot, "1/4", t.qf);
            else if (t.phase == "sf")
                LayoutBracketRoundFromTemplate(_rowsRoot, "1/2", t.sf);
            else if (t.phase == "final")
                LayoutBracketRoundFromTemplate(_rowsRoot, "Финал", t.final_pairs);
            else if (t.phase == "countdown")
            {
                // During countdown show the upcoming bracket (already prepared on server).
                if (t.next_round == "sf")
                    LayoutBracketRoundFromTemplate(_rowsRoot, "1/2", t.sf);
                else if (t.next_round == "final")
                    LayoutBracketRoundFromTemplate(_rowsRoot, "Финал", t.final_pairs);
                else
                    LayoutBracketRoundFromTemplate(_rowsRoot, "1/4", t.qf);
            }
        }

        private void ClearRowsKeepingTemplates(Transform rowsRoot)
        {
            if (rowsRoot == null) return;
            for (var i = rowsRoot.childCount - 1; i >= 0; i--)
            {
                var ch = rowsRoot.GetChild(i);
                if (ch == null) continue;
                if (ch.name == "RoundHeaderTemplate" || ch.name == "PairRowTemplate")
                    continue;
                Destroy(ch.gameObject);
            }

            var hdrT = rowsRoot.Find("RoundHeaderTemplate");
            if (hdrT != null) hdrT.gameObject.SetActive(false);
            var rowT = rowsRoot.Find("PairRowTemplate");
            if (rowT != null) rowT.gameObject.SetActive(false);
        }

        private void LayoutBracketRoundFromTemplate(Transform parent, string title, ArenaPairRow[] rows)
        {
            if (parent == null || rows == null || rows.Length == 0)
                return;

            var hdrT = _headerTemplate != null ? _headerTemplate : parent.Find("RoundHeaderTemplate")?.GetComponent<TMP_Text>();
            TMP_Text header;
            if (hdrT != null)
            {
                header = UnityEngine.Object.Instantiate(hdrT, parent);
                header.gameObject.name = "RoundHeader";
                header.gameObject.SetActive(true);
                header.text = title;
            }
            else
            {
                var headerGo = CreateTmp(parent, title, 22, FontStyles.Bold);
                header = headerGo.GetComponent<TMP_Text>();
            }

            foreach (var pr in rows)
                CreatePairRowFromTemplate(parent, pr);
        }

        private void CreatePairRowFromTemplate(Transform parent, ArenaPairRow pr)
        {
            if (parent == null || pr == null) return;

            var rowT = _pairRowTemplate != null ? _pairRowTemplate : parent.Find("PairRowTemplate");
            Transform row;
            if (rowT != null)
            {
                row = UnityEngine.Object.Instantiate(rowT, parent);
                row.gameObject.name = "BracketRow";
                row.gameObject.SetActive(true);
            }
            else
            {
                // Old procedural fallback
                var rowGo = new GameObject("BracketRow", typeof(RectTransform));
                rowGo.transform.SetParent(parent, false);
                row = rowGo.transform;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, 72f);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.minHeight = 64f;
                rowLe.preferredHeight = 72f;
                rowLe.flexibleWidth = 1f;
                var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleCenter;
                hl.spacing = 10f;
                hl.padding = new RectOffset(10, 10, 6, 6);
                hl.childForceExpandHeight = true;
                hl.childForceExpandWidth = false;
            }

            BindPairRowData(row, pr);
        }

        private static string PhaseTitle(string phase)
        {
            switch (phase)
            {
                case "qf": return "1/4 финала";
                case "sf": return "Полуфинал";
                case "final": return "Финал";
                case "done": return "Финиш";
                default: return phase ?? "";
            }
        }

        private void LayoutBracketRound(Transform parent, string title, ArenaPairRow[] rows)
        {
            if (rows == null || rows.Length == 0)
                return;

            var header = CreateTmp(parent, title, 22, FontStyles.Bold);
            var hdrRt = header.GetComponent<RectTransform>();
            hdrRt.sizeDelta = new Vector2(0f, 34f);
            var hdrLe = header.AddComponent<LayoutElement>();
            hdrLe.preferredHeight = 34f;
            hdrLe.flexibleWidth = 1f;

            foreach (var pr in rows)
            {
                var row = new GameObject("BracketRow", typeof(RectTransform));
                row.transform.SetParent(parent, false);
                var rowRt = row.GetComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, 72f);

                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.minHeight = 64f;
                rowLe.preferredHeight = 72f;
                rowLe.flexibleWidth = 1f;

                var hl = row.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleCenter;
                hl.spacing = 10f;
                hl.padding = new RectOffset(10, 10, 6, 6);
                hl.childForceExpandHeight = true;
                hl.childForceExpandWidth = false;

                var leftName = CreateTmp(row.transform, pr.display_a ?? "", 20, FontStyles.Normal);
                var lnRt = leftName.GetComponent<RectTransform>();
                lnRt.sizeDelta = new Vector2(200f, 36f);
                var lnLe = leftName.AddComponent<LayoutElement>();
                lnLe.preferredWidth = 200f;
                lnLe.flexibleWidth = 1f;

                var hpA = MakeHpBar(row.transform);
                SetHp(hpA, pr.hp_a);

                var vs = CreateTmp(row.transform, "—", 22, FontStyles.Bold);
                vs.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 32f);
                vs.AddComponent<LayoutElement>().preferredWidth = 32f;

                var hpB = MakeHpBar(row.transform);
                SetHp(hpB, pr.hp_b);

                var rightName = CreateTmp(row.transform, pr.display_b ?? "", 20, FontStyles.Normal);
                rightName.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;
                var rnRt = rightName.GetComponent<RectTransform>();
                rnRt.sizeDelta = new Vector2(200f, 36f);
                var rnLe = rightName.AddComponent<LayoutElement>();
                rnLe.preferredWidth = 200f;
                rnLe.flexibleWidth = 1f;

                var statusGo = CreateTmp(row.transform, FormatPairStatus(pr.status), 18, FontStyles.Italic);
                statusGo.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 28f);
                var stLe = statusGo.AddComponent<LayoutElement>();
                stLe.preferredWidth = 100f;
            }
        }

        private static string FormatPairStatus(string s)
        {
            switch (s)
            {
                case "pending": return "";
                case "fighting": return "Бой";
                case "done": return "Готово";
                default: return s ?? "";
            }
        }

        private static Sprite ArenaBracketHpWhiteSprite()
        {
            if (_arenaBracketHpSprite != null)
                return _arenaBracketHpSprite;

            var t = Texture2D.whiteTexture;
            _arenaBracketHpSprite =
                Sprite.Create(
                    t,
                    new Rect(0, 0, t.width, t.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect);

            _arenaBracketHpSprite.name = "ArenaBracketHpFill";
            return _arenaBracketHpSprite;
        }

        private static void EnsureHpFillRenderable(Image img)
        {
            if (img == null)
                return;
            img.sprite ??= ArenaBracketHpWhiteSprite();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.preserveAspect = false;
            img.maskable = true;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static Image MakeHpBar(Transform parent)
        {
            var go = new GameObject("Hp");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.18f, 1f);
            bg.sprite = ArenaBracketHpWhiteSprite();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140f, 22f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.28f, 0.82f, 0.38f, 1f);
            EnsureHpFillRenderable(fillImg);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 140f;
            le.preferredHeight = 26f;

            return fillImg;
        }

        private static void SetHp(Image img, float hp)
        {
            if (img == null)
                return;
            EnsureHpFillRenderable(img);

            float t;
            // Server может слать hp как 0..150 или нормализовано 0..1.
            if (hp <= 1.01f)
                t = Mathf.Clamp01(hp);
            else
                t = Mathf.Clamp01(hp / 150f);

            img.fillAmount = t;
        }

        private void HideBracket()
        {
            if (_bracketRoot != null)
                _bracketRoot.SetActive(false);
        }

        private static T FindComponentByGameObjectName<T>(string goName) where T : Component
        {
            var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all)
            {
                if (c != null && c.gameObject.name == goName)
                    return c;
            }
            return null;
        }
    }
}
