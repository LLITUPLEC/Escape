using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
using Project.Nakama;
using Project.Utils;
using Project.Audio;
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
        private const string RpcArenaPlaceBet = "duel_arena_place_bet";
        private const string BracketPrefabResourcesPath = "UI/ArenaBracketOverlay";
        private const string PrefBettingEnabled = "arena.betting_ui_enabled";

        [Header("Buttons (by GameObject name)")]
        [SerializeField] private string match3ArenaButtonName = "match3Arena";
        [SerializeField] private string match3ArenaOreButtonName = "match3Arena_Ore";
        [SerializeField] private string match3ArenaGoldButtonName = "match3Arena_Gold";
        [SerializeField] private string queueTextObjectName = "queue";
        [SerializeField] private string duelMatch3SceneName = "DuelMatch3";
        [SerializeField] private string bettingToggleObjectName = "BettingToggle";

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

        [Header("Arena Bet Modal")]
        [SerializeField] private Sprite iconBetPanelClear;
        [SerializeField] private Sprite iconBetRow1;
        [SerializeField] private Sprite iconBetRow2;
        [SerializeField] private Sprite iconBetRow3;
        [SerializeField] private Color betPanelColor = new Color32(0xCD, 0xD5, 0xD3, 0xFF);

        private const string BetPanelClearPath = "Assets/_Project/img/madals/arena_bet/arena_bet_clear.png";
        private const string BetRow1Path = "Assets/_Project/img/madals/arena_bet/arena_bet_1row.png";
        private const string BetRow2Path = "Assets/_Project/img/madals/arena_bet/arena_bet_2row.png";
        private const string BetRow3Path = "Assets/_Project/img/madals/arena_bet/arena_bet_3row.png";

        private const string ArenaKindSmith = "smith";
        private const string ArenaKindOre = "ore";
        private const string ArenaKindGold = "gold";

        private Button _arenaButton;
        private Button _arenaOreButton;
        private Button _arenaGoldButton;
        private TMP_Text _queueTmp;
        private TMP_Text _queueTournamentTitle;
        private Button _leaveQueueButton;
        private GameObject _modePanelGo;
        private GameObject _backButtonGo;
        private GameObject _statsCardGo;
        private bool _queueUiLocked;
        private bool _participationLocked;
        private GameObject _participationBlocker;
        private static ArenaMatch8Bridge _activeBridge;
        public static bool IsParticipationLocked => _activeBridge != null && _activeBridge._participationLocked;

        private GameObject _modalRoot;
        private Canvas _overlayCanvas;
        private Transform _modalPanel;
        private TMP_Text _modalTitle;
        private Transform _modalCloseButtonTr;
        private GameObject _bracketRoot;
        private Transform _rowsRoot;
        private TMP_Text _bracketTitle;
        private TMP_Text _cdText;
        private TMP_Text _prizeFooterText;
        private TMP_Text _headerTemplate;
        private Transform _pairRowTemplate;
        private ArenaTournamentState _lastTournament;
        private Toggle _bettingToggle;
        private bool _bettingUiEnabled;
#if UNITY_EDITOR
        internal Action<int, int> EditorBetRequestOverride;
#endif
        private bool _betsOpen;
        private string _bettingRound = "";
        private string _myUserId = "";
        private readonly Dictionary<int, int> _myBetSidesBySlot = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _pendingBetVersions = new Dictionary<int, int>();
        private Task _betWriteTail = Task.CompletedTask;
        private int _nextBetVersion;
        private int _betLifetime;
        private string _betContextId = "";
        private GameObject _toastRoot;
        private TMP_Text _toastText;
        private Coroutine _toastRoutine;

        private Coroutine _pollRoutine;
        private bool _busyJoin;
        private int _pollVersion;
        private string _selectedArenaKind = ArenaKindSmith;
        /// <summary>Последняя выбранная/просматриваемая ставка — для poll (отдельные очереди по bet_tier).</summary>
        private string _previewBetTier = "";
        private int _smithRulesVersion = 2;
        private SmithOffer[] _smithOffers;

        /// <summary>Без Sprite у UI Image режим «Filled» + Fill Amount часто визуально не работает (сплошной прямоугольник).</summary>
        private static Sprite _arenaBracketHpSprite;

        private string _sceneArmMidGuard;
        private float _suppressFightReloadUntil;
        private static bool _registeredSceneHooks;
        private static string _lastPrevSceneNameForArenaUi;
        private string _lastBracketSignature = string.Empty;

        /// <summary>Блокировка кликов по меню арены до прихода актуальной сетки после победы в 1/4 или 1/2.</summary>
        private GameObject _arenaReturnBlockerRoot;
        private Coroutine _arenaReturnBlockerRoutine;

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
            public int rules_version;
            public SmithOffer[] smith_offers;
            public ArenaTournamentState tournament;
            public string err;
        }

        [Serializable]
        private sealed class SmithOffer
        {
            public string bet_tier;
            public int entry_amount;
            public int champion_ingots;
            public int consolation_ingots;
            public string recipe_rarity;
            public int recipe_fallback_ingots;
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
            public bool bets_open;
            public string betting_round;
            public ArenaSideBet[] my_bets;
            public int bet_score;
            public int bet_wins;
            public int bet_losses;
            public int prize_base_gold;
            public int prize_base_ore;
            public int prize_preview_gold;
            public int prize_preview_ore;
            public int rules_version;
            public int prize_ingots;
            public int consolation_ingots;
            public string recipe_rarity;
            public int recipe_fallback_ingots;
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
            public string avatar_a;
            public string avatar_b;
            public float hp_a;
            public float hp_b;
            public float hp_max_a;
            public float hp_max_b;
            public bool hp_known_a;
            public bool hp_known_b;
            public string status;
            public string winner_uid;
        }

        [Serializable]
        private sealed class ArenaSideBet
        {
            public string round;
            public int slot;
            public string side;
            public string pick_uid;
        }

        [Serializable]
        private sealed class ArenaPlaceBetResponse
        {
            public bool ok;
            public string err;
            public bool cleared;
            public string round;
            public int slot;
            public string side;
            public string pick_uid;
            public ArenaSideBet[] my_bets;
            public int bet_score;
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
            SetQueuePresentation(false);
            BindBettingToggle();

            EnsureBetModalBuilt();
            EnsureBracketBuilt();
        }

        private void BindBettingToggle()
        {
            if (_bettingToggle != null)
                _bettingToggle.onValueChanged.RemoveListener(OnBettingToggleChanged);

            _bettingToggle = FindComponentByGameObjectName<Toggle>(bettingToggleObjectName);
            // Настройка игрока: переживает выход из сцены / возврат с боя (1/4 → 1/2).
            _bettingUiEnabled = PlayerPrefs.GetInt(PrefBettingEnabled, 0) != 0;
            if (_bettingToggle != null)
            {
                _bettingToggle.SetIsOnWithoutNotify(_bettingUiEnabled);
                // SetIsOnWithoutNotify не шлёт onValueChanged — SwitchToggleVisual иначе остаётся «выкл».
                var visual = _bettingToggle.GetComponent<SwitchToggleVisual>();
                if (visual != null) visual.Refresh();
                _bettingToggle.onValueChanged.AddListener(OnBettingToggleChanged);
                // Если уже в очереди — держим скрытым (как ModePanel).
                if (_queueUiLocked)
                    _bettingToggle.gameObject.SetActive(false);
            }
        }

        private void OnBettingToggleChanged(bool isOn)
        {
            _bettingUiEnabled = isOn;
            PlayerPrefs.SetInt(PrefBettingEnabled, isOn ? 1 : 0);
            PlayerPrefs.Save();
            ApplyBettingUiToAllRows();
            UpdatePrizeFooter(_lastTournament);
            if (_lastTournament != null && _bracketRoot != null) UpdateTournamentSummary(_lastTournament);
        }

        private void ApplyBettingUiToAllRows()
        {
            if (_rowsRoot == null) return;
            var rows = CollectBracketRows(_rowsRoot);
            for (var i = 0; i < rows.Count; i++)
                ApplyBettingUiToRow(rows[i]);
        }

        private void ApplyBettingUiToRow(Transform row)
        {
            if (row == null) return;
            var select = row.GetComponent<PairRowBetSelect>();
            var pairView = row.GetComponent<ExpeditionArenaPairTag>();
            if (pairView != null && !pairView.Current)
            {
                if (select != null) select.ApplyState(false, false, -1);
                ExpeditionArenaOverlays.SetBetControlsVisible(row, false);
                return;
            }
            if (select == null)
            {
                var a = FindDeep(row, PairRowBetSelect.BetAName);
                var b = FindDeep(row, PairRowBetSelect.BetBName);
                var show = _bettingUiEnabled && _betsOpen;
                if (a != null) a.gameObject.SetActive(show);
                if (b != null) b.gameObject.SetActive(show);
                return;
            }

            var slot = select.Slot;
            var ownPair = !string.IsNullOrEmpty(_myUserId)
                          && (string.Equals(select.UidA, _myUserId, StringComparison.Ordinal)
                              || string.Equals(select.UidB, _myUserId, StringComparison.Ordinal));
            var showBets = _bettingUiEnabled && (_betsOpen || _myBetSidesBySlot.ContainsKey(slot)) && !ownPair;
            var interactable = _bettingUiEnabled && _betsOpen && !ownPair;
            var side = -1;
            if (_myBetSidesBySlot.TryGetValue(slot, out var s))
                side = s;
            select.ApplyState(showBets, interactable, side);
            ExpeditionArenaOverlays.ApplyPairCard(row);
            ExpeditionArenaOverlays.SetBetControlsVisible(row, showBets);
        }

        private void SyncMyBetsFromTournament(ArenaTournamentState t)
        {
            var round = t?.betting_round ?? "";
            var contextId = t?.id ?? "";
            if (!string.Equals(_bettingRound, round, StringComparison.Ordinal) || !string.Equals(_betContextId, contextId, StringComparison.Ordinal))
                _pendingBetVersions.Clear();
            _betsOpen = t != null && t.bets_open;
            _bettingRound = round;
            _betContextId = contextId;
            MergeAuthoritativeBets(t?.my_bets);
        }

        private void MergeAuthoritativeBets(ArenaSideBet[] bets)
        {
            var optimistic = new Dictionary<int, int>();
            foreach (var pending in _pendingBetVersions)
                if (_myBetSidesBySlot.TryGetValue(pending.Key, out var side)) optimistic[pending.Key] = side;
            _myBetSidesBySlot.Clear();
            foreach (var b in bets ?? Array.Empty<ArenaSideBet>())
            {
                if (b == null || b.slot <= 0) continue;
                var side = -1;
                if (string.Equals(b.side, "a", StringComparison.OrdinalIgnoreCase)) side = 0;
                else if (string.Equals(b.side, "b", StringComparison.OrdinalIgnoreCase)) side = 1;
                if (side >= 0)
                    _myBetSidesBySlot[b.slot] = side;
            }
            // Preserve an optimistic selection only while its own request is in
            // flight, including an optimistic cancellation (missing local key).
            foreach (var pending in _pendingBetVersions)
            {
                _myBetSidesBySlot.Remove(pending.Key);
                if (optimistic.TryGetValue(pending.Key, out var side)) _myBetSidesBySlot[pending.Key] = side;
            }
        }

        private void EnsureMyUserId()
        {
            if (!string.IsNullOrEmpty(_myUserId)) return;
            try
            {
                _myUserId = NakamaBootstrap.Instance?.Session?.UserId ?? "";
            }
            catch
            {
                _myUserId = "";
            }
        }

        private void OnPairBetSideChanged(int slot, int side)
        {
            if (!_betsOpen || !_bettingUiEnabled) return;
            if (side < 0)
                _myBetSidesBySlot.Remove(slot);
            else
                _myBetSidesBySlot[slot] = side;
#if UNITY_EDITOR
            if (EditorBetRequestOverride != null) { EditorBetRequestOverride(slot, side); return; }
#endif
            var version = ++_nextBetVersion;
            _pendingBetVersions[slot] = version;
            _betWriteTail = PlaceBetFireAndForget(_betWriteTail, slot, side, version, _betLifetime, _bettingRound, _betContextId, LocalAccountCacheScope.Current);
        }

        private async Task PlaceBetFireAndForget(Task previous, int slot, int side, int version, int lifetime, string requestedRound, string contextId, string requestedScope)
        {
            ArenaPlaceBetResponse response = null;
            try
            {
                // Send rapid A -> B -> cancel in the same order the player chose.
                // A late response must never overwrite a newer local intention.
                await previous;
                if (!CanApplyBetResult(lifetime, requestedRound, contextId, requestedScope)
                    || !_pendingBetVersions.ContainsKey(slot)) return;
                if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady) return;
                string payload;
                if (side < 0)
                {
                    payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch()
                              + ",\"slot\":" + slot + ",\"clear\":true}";
                }
                else
                {
                    var sideStr = side == 0 ? "a" : "b";
                    payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch()
                              + ",\"slot\":" + slot + ",\"side\":\"" + sideStr + "\"}";
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcArenaPlaceBet, payload);
                response = JsonUtility.FromJson<ArenaPlaceBetResponse>(rpc?.Payload ?? "{}");
                if (response == null || !response.ok) Debug.LogWarning("[Arena8] place_bet: " + (response?.err ?? "failed"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] place_bet: " + e.Message);
            }
            finally
            {
                var completedResponse = response;
                MainThreadDispatcher.Enqueue(() =>
                {
                    if (!CanApplyBetResult(lifetime, requestedRound, contextId, requestedScope)) return;
                    CompleteBetRequest(slot, version, completedResponse);
                });
            }
        }

        private bool CanApplyBetResult(int lifetime, string round, string contextId, string scope)
        {
            return this != null && isActiveAndEnabled && lifetime == _betLifetime && string.Equals(round, _bettingRound, StringComparison.Ordinal)
                && string.Equals(contextId, _betContextId, StringComparison.Ordinal) && LocalAccountCacheScope.IsCurrent(scope);
        }

        private void CompleteBetRequest(int slot, int version, ArenaPlaceBetResponse response, bool notify = true)
        {
            // This method is reached only through the lifetime/round/context/
            // account guard in the transport completion. Keep every acknowledged
            // snapshot, even if a newer local choice is still pending: if that
            // newer write fails, its rollback must retain the accepted choice.
            if (response != null && response.ok && response.my_bets != null
                && _lastTournament != null && string.Equals(_lastTournament.id, _betContextId, StringComparison.Ordinal)
                && string.Equals(_lastTournament.betting_round ?? "", _bettingRound, StringComparison.Ordinal))
            {
                _lastTournament.my_bets = response.my_bets;
                // A poll issued before this accepted write may still carry the
                // previous bets. Its queued main-thread apply is now obsolete.
                _pollVersion++;
            }
            if (!_pendingBetVersions.TryGetValue(slot, out var current) || current != version) return;
            _pendingBetVersions.Remove(slot);
            MergeAuthoritativeBets(response != null && response.ok && response.my_bets != null ? response.my_bets : _lastTournament?.my_bets);
            ApplyBettingUiToAllRows();
            if (notify && (response == null || !response.ok))
            {
                ExpeditionAudio.Play(ExpeditionCue.UiBlocked);
                ShowToast(response?.err == "bets_closed" ? "Прогнозы закрыты: раунд уже начался" : "Прогноз не принят. Проверьте соединение", 2.6f);
            }
        }

        private static void OnAnyActiveSceneChanged(Scene prev, Scene next)
        {
            _lastPrevSceneNameForArenaUi = prev.IsValid() ? prev.name : "";
        }

        private void OnEnable()
        {
            _activeBridge = this;
            if (_lastPrevSceneNameForArenaUi == duelMatch3SceneName)
                _suppressFightReloadUntil = Time.realtimeSinceStartup + 8f;

            BindBettingToggle();

            if (Match3LaunchContext.ArenaMenuAwaitBracketOverlay)
            {
                SetParticipationLocked(true);
                EnsureArenaReturnBlocker();
                if (_arenaReturnBlockerRoot != null)
                {
                    _arenaReturnBlockerRoot.SetActive(true);
                    _arenaReturnBlockerRoot.transform.SetAsLastSibling();
                }
                if (_arenaReturnBlockerRoutine != null)
                {
                    StopCoroutine(_arenaReturnBlockerRoutine);
                    _arenaReturnBlockerRoutine = null;
                }
                _arenaReturnBlockerRoutine = StartCoroutine(ArenaReturnBlockerTimeoutRoutine());
                _ = PollOnceFireAndForget();
            }

            if (_pollRoutine != null)
                StopCoroutine(_pollRoutine);
            _pollRoutine = StartCoroutine(PollLoop());
        }

        private void OnDisable()
        {
            _pollVersion++;
            _betLifetime++;
            _pendingBetVersions.Clear();
            CloseBetModal();
            SetQueuePresentation(false);
            HideBracket();
            SetParticipationLocked(false);
            if (_activeBridge == this) _activeBridge = null;
            if (_arenaReturnBlockerRoutine != null)
            {
                StopCoroutine(_arenaReturnBlockerRoutine);
                _arenaReturnBlockerRoutine = null;
            }
            if (_pollRoutine != null)
            {
                StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }
            if (_bettingToggle != null)
                _bettingToggle.onValueChanged.RemoveListener(OnBettingToggleChanged);
        }

        private void OnDestroy()
        {
            SetParticipationLocked(false);
            if (_participationBlocker != null) DestroyPresentationObject(_participationBlocker);
        }

        private void SetParticipationLocked(bool locked)
        {
            _participationLocked = locked;
            if (locked && _participationBlocker == null)
            {
                var go = new GameObject("ArenaParticipationInputLock", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(go, gameObject.scene);
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 290;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = .5f;
                var blocker = ExpeditionUi.Fill(go.transform, "BlockAllUnderlyingInput", 0, 0, 1, 1, new Color(0, 0, 0, 1f / 255f));
                blocker.raycastTarget = true;
                blocker.canvasRenderer.cullTransparentMesh = false;
                _participationBlocker = go;
            }
            if (_participationBlocker != null) _participationBlocker.SetActive(locked);
            if (_activeBridge == null || _activeBridge == this)
                ExpeditionPresentation.SetArenaParticipationLocked(locked);
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

        private void EnsureArenaReturnBlocker()
        {
            if (_arenaReturnBlockerRoot != null)
                return;
            var canvas = EnsureOverlayCanvas();
            if (canvas == null)
                return;
            var go = new GameObject("ArenaReturnBracketBlocker", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.03f, 0.05f, 0.09f, 0.74f);
            img.raycastTarget = true;
            var grp = go.GetComponent<CanvasGroup>();
            grp.interactable = true;
            grp.blocksRaycasts = true;
            grp.alpha = 1f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lblRt = labelGo.GetComponent<RectTransform>();
            lblRt.SetParent(go.transform, false);
            lblRt.anchorMin = new Vector2(0.06f, 0.42f);
            lblRt.anchorMax = new Vector2(0.94f, 0.58f);
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "Загрузка турнирной сетки…";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 22f;
            tmp.color = new Color(0.92f, 0.94f, 1f, 1f);
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;

            go.SetActive(false);
            _arenaReturnBlockerRoot = go;
        }

        private void ReleaseArenaReturnBlockerAfterBracketShown()
        {
            if (!Match3LaunchContext.ArenaMenuAwaitBracketOverlay)
                return;
            Match3LaunchContext.ClearArenaMenuAwaitBracketOverlay();
            if (_arenaReturnBlockerRoutine != null)
            {
                StopCoroutine(_arenaReturnBlockerRoutine);
                _arenaReturnBlockerRoutine = null;
            }
            if (_arenaReturnBlockerRoot != null)
                _arenaReturnBlockerRoot.SetActive(false);
            if (_bracketRoot != null && _bracketRoot.activeInHierarchy)
                _bracketRoot.transform.SetAsLastSibling();
        }

        private IEnumerator ArenaReturnBlockerTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(10f);
            Match3LaunchContext.ClearArenaMenuAwaitBracketOverlay();
            if (_arenaReturnBlockerRoot != null)
                _arenaReturnBlockerRoot.SetActive(false);
            _arenaReturnBlockerRoutine = null;
        }

        private async Task PollOnceFireAndForget()
        {
            if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady)
                return;
            var requestVersion = ++_pollVersion;
            var requestedScope = LocalAccountCacheScope.Current;
            try
            {
                var betPart = string.IsNullOrWhiteSpace(_previewBetTier)
                    ? ""
                    : ",\"bet_tier\":\"" + _previewBetTier.Trim() + "\"";
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() +
                              ",\"arena_kind\":\"" + (_selectedArenaKind ?? ArenaKindSmith) + "\"" + betPart + "}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaPoll, payload);
                var raw = rpc?.Payload ?? "{}";
                var model = JsonUtility.FromJson<ArenaPollResponse>(raw);
                if (model == null || !model.ok)
                    return;

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (this != null && isActiveAndEnabled && IsCurrentPollVersion(requestVersion)
                        && LocalAccountCacheScope.IsCurrent(requestedScope)) ApplyPoll(model);
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] poll: " + e.Message);
            }
        }

        private bool IsCurrentPollVersion(int version) => version == _pollVersion;

        private void ApplyPoll(ArenaPollResponse m)
        {
            if (this == null || !isActiveAndEnabled || m == null) return;
            ApplyPollPresentation(m, true);
        }

        // Keep the server snapshot renderer separate from authentication, polling
        // and scene changes so the actual transitions can be exercised offline.
        private void ApplyPollPresentation(ArenaPollResponse m, bool allowSceneArm)
        {
            if (m == null) return;
            if (string.Equals(m.queue_kind, ArenaKindSmith, StringComparison.OrdinalIgnoreCase))
            {
                _smithRulesVersion = m.rules_version > 0 ? m.rules_version : 1;
                _smithOffers = m.smith_offers;
            }
            var tournamentActive = m.tournament != null && m.tournament.active && !m.tournament.eliminated;
            SetParticipationLocked(_busyJoin || m.in_queue || tournamentActive);
            UpdateQueueUi(m);

            if (m.tournament != null && m.tournament.active && m.tournament.eliminated)
            {
                _lastBracketSignature = string.Empty;
                _lastTournament = null;
                HideBracket();
                return;
            }

            if (m.tournament != null && m.tournament.active && !m.tournament.eliminated)
            {
                EnsureMyUserId();
                _lastTournament = m.tournament;
                var sig = BuildBracketSignature(m.tournament);
                if (sig != _lastBracketSignature || _bracketRoot == null || !_bracketRoot.activeSelf)
                {
                    // Полная пересборка — берём ставки с сервера.
                    SyncMyBetsFromTournament(m.tournament);
                    _lastBracketSignature = sig;
                    ShowBracket(m.tournament);
                }
                else
                {
                    // Poll is authoritative for settled/failed writes. Only an
                    // in-flight slot retains its optimistic local intention.
                    SyncMyBetsFromTournament(m.tournament);
                    RefreshBracketInPlace(m.tournament);
                    ApplyBettingUiToAllRows();
                }

                if (allowSceneArm)
                {
                    ReleaseArenaReturnBlockerAfterBracketShown();
                    TryArmFightScene(m.tournament);
                }
            }
            else
            {
                _lastBracketSignature = string.Empty;
                _lastTournament = null;
                _betsOpen = false;
                _bettingRound = "";
                _betContextId = "";
                _pendingBetVersions.Clear();
                _myBetSidesBySlot.Clear();
                HideBracket();
            }
        }

        private static string BuildBracketSignature(ArenaTournamentState t)
        {
            if (t == null) return string.Empty;
            // countdown_left меняется каждый poll — не включаем, иначе ряды пересобираются и сбрасываются ставки.
            return $"{t.id ?? ""}|{t.phase}|{t.next_round ?? ""}|{t.join_match_id ?? ""}|{RowsSignature(t.qf)}{RowsSignature(t.sf)}{RowsSignature(t.final_pairs)}";
        }

        private static string RowsSignature(ArenaPairRow[] rows)
        {
            if (rows == null || rows.Length == 0) return string.Empty;
            var s = string.Empty;
            foreach (var r in rows)
            {
                if (r == null) continue;
                // HP обновляются отдельно в RefreshBracketInPlace (каждый poll), чтобы полоски «дышали» без пересборки рядов.
                s += $"{r.slot}:{r.uid_a ?? ""}:{r.uid_b ?? ""}:{r.status}:{r.match_id ?? ""}:{r.winner_uid ?? ""};";
            }

            return s;
        }

        private void RefreshArenaCountdownOnly(ArenaTournamentState t)
        {
            if (_bracketRoot == null || t == null) return;
            ApplyBracketOverlayTitle(t);
            var cd = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            if (cd != null)
            {
                if (t.phase == "countdown")
                    cd.text = "До начала боя · " + Mathf.Max(0, t.countdown_left) + " сек";
                else
                    cd.text = PhaseTitle(t.phase);
            }
            UpdatePrizeFooter(t);
        }

        // Размеры блока приза/ставок (PrizeFooter). Править здесь — или в префабе ArenaBracketOverlay → PrizeFooter.
        private const float PrizeFooterFontSize = 20f;
        private const float PrizeFooterPreferredHeight = 112f;
        private const float PrizeFooterMinHeight = 96f;

        private void EnsurePrizeFooter()
        {
            if (_bracketRoot == null) return;

            if (_prizeFooterText == null)
            {
                var existing = _bracketRoot.transform.Find("PrizeFooter");
                if (existing != null)
                    _prizeFooterText = existing.GetComponent<TMP_Text>();
            }

            if (_prizeFooterText == null)
            {
                var go = CreateTmp(_bracketRoot.transform, "", PrizeFooterFontSize, FontStyles.Normal);
                go.name = "PrizeFooter";
                var tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.85f, 0.9f, 0.95f, 1f);
                go.transform.SetAsLastSibling();
                _prizeFooterText = tmp;
                go.SetActive(false);
            }

            _prizeFooterText.fontSize = PrizeFooterFontSize;
            var le = _prizeFooterText.GetComponent<LayoutElement>();
            if (le == null) le = _prizeFooterText.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = PrizeFooterPreferredHeight;
            le.minHeight = PrizeFooterMinHeight;
            le.flexibleWidth = 1f;
        }

        private void UpdatePrizeFooter(ArenaTournamentState t)
        {
            EnsurePrizeFooter();
            if (_prizeFooterText == null) return;

            var show = _bettingUiEnabled
                       && t != null
                       && t.active
                       && !t.eliminated
                       && string.Equals(t.phase, "countdown", StringComparison.Ordinal);
            _prizeFooterText.gameObject.SetActive(show);
            if (!show) return;

            var prizeLine = FormatPrizePreviewLine(t);
            var wins = Mathf.Max(0, t.bet_wins);
            var losses = Mathf.Max(0, t.bet_losses);
            _prizeFooterText.text = prizeLine + "\nСтавки: зашло " + wins + " · мимо " + losses;
        }

        private static string FormatPrizePreviewLine(ArenaTournamentState t)
        {
            if (t == null) return "Потенциальный приз: —";
            var kind = (t.kind ?? "").Trim().ToLowerInvariant();
            var g = Mathf.Max(0, t.prize_preview_gold);
            var o = Mathf.Max(0, t.prize_preview_ore);
            var baseG = Mathf.Max(0, t.prize_base_gold);
            var baseO = Mathf.Max(0, t.prize_base_ore);
            var deltaScore = t.bet_score;
            var deltaPct = deltaScore * 20;
            var deltaSuffix = deltaScore == 0
                ? ""
                : (deltaScore > 0 ? $" (+{deltaPct}%)" : $" ({deltaPct}%)");

            if (kind == ArenaKindOre)
                return $"Потенциальный приз: {o} руды{deltaSuffix}";
            if (kind == ArenaKindGold)
                return $"Потенциальный приз: {g} золота{deltaSuffix}";

            // smith — золото и руда
            if (g > 0 || o > 0 || baseG > 0 || baseO > 0)
                return $"Потенциальный приз: {g} зол. / {o} руды{deltaSuffix}" +
                    (t.rules_version >= 2 ? $"\n+ {t.prize_ingots} слитков; рецепт с побеждённого открытого этажа\nБез нового рецепта: ещё {t.recipe_fallback_ingots} слитков\nПосле проигрыша: {t.consolation_ingots} слитков" : "");
            return $"Потенциальный приз: —{deltaSuffix}";
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

            var list = CollectBracketRows(_rowsRoot);
            foreach (var row in list)
            {
                var tag = row.GetComponent<ExpeditionArenaPairTag>();
                if (tag == null) continue;
                var rows = tag.Round == "qf" ? t.qf : tag.Round == "sf" ? t.sf : t.final_pairs;
                if (rows == null) continue;
                foreach (var pair in rows)
                    if (pair != null && pair.slot == tag.Slot) { BindPairRowData(row, pair); break; }
            }
            UpdateTournamentSummary(t);
        }

        private static List<Transform> CollectBracketRows(Transform rowsRoot)
        {
            var list = new List<Transform>();
            if (rowsRoot == null)
                return list;
            foreach (var ch in rowsRoot.GetComponentsInChildren<Transform>(true))
                if (ch.name == "BracketRow" && ch.gameObject.activeInHierarchy) list.Add(ch);

            return list;
        }

        /// <summary>
        /// Ищет потомка по имени на любой глубине (Left_div/LeftName, Middle_div/BetA и т.п.).
        /// </summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            var direct = root.Find(name);
            if (direct != null)
                return direct;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Image ResolveHpFillImage(Transform row, string barName)
        {
            if (row == null || string.IsNullOrEmpty(barName))
                return null;

            var bar = FindDeep(row, barName);
            if (bar == null)
                return null;

            var fill = bar.Find("Fill")?.GetComponent<Image>();
            if (fill != null)
                return fill;

            var img = bar.GetComponent<Image>();
            return img != null && img.type == Image.Type.Filled ? img : null;
        }

        /// <summary>Привязать данные пары к уже созданному ряду (prefab или процедурный).</summary>
        private void BindPairRowData(Transform row, ArenaPairRow pr)
        {
            if (row == null || pr == null)
                return;

            var leftName = FindDeep(row, "LeftName")?.GetComponent<TMP_Text>();
            if (leftName == null) leftName = FindDeep(row, "Txt")?.GetComponent<TMP_Text>();
            if (leftName != null)
                leftName.text = string.IsNullOrEmpty(pr.uid_a) ? " - " : pr.display_a ?? "";

            var rightName = FindDeep(row, "RightName")?.GetComponent<TMP_Text>();
            if (rightName != null)
                rightName.text = string.IsNullOrEmpty(pr.uid_b) ? " - " : pr.display_b ?? "";

            var hpA = ResolveHpFillImage(row, "HpA");
            if (hpA != null)
                SetHp(hpA, pr.hp_a, pr.hp_max_a, pr.hp_known_a);
            else
            {
                var legacyHp = FindDeep(row, "Hp")?.GetComponent<Image>();
                if (legacyHp != null)
                    SetHp(legacyHp, pr.hp_a, pr.hp_max_a, pr.hp_known_a);
            }

            var hpB = ResolveHpFillImage(row, "HpB");
            if (hpB != null)
                SetHp(hpB, pr.hp_b, pr.hp_max_b, pr.hp_known_b);

            var hpTextA = FindDeep(row, "HpStatusA")?.GetComponent<TMP_Text>();
            var hpTextB = FindDeep(row, "HpStatusB")?.GetComponent<TMP_Text>();
            if (hpTextA != null) hpTextA.text = pr.hp_known_a && pr.hp_max_a > 0 ? $"{pr.hp_a:0}/{pr.hp_max_a:0}" : "HP —";
            if (hpTextB != null) hpTextB.text = pr.hp_known_b && pr.hp_max_b > 0 ? $"{pr.hp_b:0}/{pr.hp_max_b:0}" : "HP —";

            var status = FindDeep(row, "Status")?.GetComponent<TMP_Text>();
            if (status != null)
                status.text = FormatPairStatus(pr.status);
            ExpeditionArenaOverlays.UpdatePair(row, pr.uid_a, pr.uid_b, pr.winner_uid, pr.status,
                pr.uid_a == _myUserId || pr.uid_b == _myUserId, pr.avatar_a, pr.avatar_b);
        }

        private void UpdateQueueUi(ArenaPollResponse m)
        {
            if (m != null && m.in_queue && !string.IsNullOrWhiteSpace(m.queue_kind))
                _selectedArenaKind = m.queue_kind.Trim().ToLowerInvariant();
            if (m != null && m.in_queue && !string.IsNullOrWhiteSpace(m.queue_bet_tier))
                _previewBetTier = m.queue_bet_tier.Trim().ToLowerInvariant();

            // queue_count also describes other players' next public queue while
            // our bracket is running. Membership, never population, owns this UI.
            var inTournament = m?.tournament != null && m.tournament.active && !m.tournament.eliminated;
            var inOwnQueue = m != null && m.in_queue && !inTournament;
            if (inOwnQueue)
            {
                var max = m.queue_max > 0 ? m.queue_max : 8;
                SetQueuePresentation(true, $"{Mathf.Clamp(m.queue_count, 0, max)}/{max}", true);
            }
            else
                SetQueuePresentation(_busyJoin && !inTournament, "Вход в турнир…");
        }

        private void SetQueuePresentation(bool visible, string status = "", bool canLeave = false)
        {
            if (visible)
            {
                EnsureLeaveQueueButtonBuilt();
                var canvas = EnsureOverlayCanvas();
                if (_queueTmp != null) _queueTmp.transform.SetParent(canvas.transform, false);
                if (_queueTournamentTitle == null)
                {
                    var plate = ExpeditionUi.Fill(canvas.transform, "ArenaQueueTournamentTitleFrame", .055f, .79f, .945f, .90f, Color.clear);
                    var frame = ExpeditionUi.Rect(plate.transform, "DarkMetalFrame", 0, 0, 1, 1).gameObject.AddComponent<ExpeditionDailyLoginFrame>();
                    frame.raycastTarget = false; frame.Configure(false, false);
                    _queueTournamentTitle = ExpeditionUi.Text(plate.transform, "ArenaQueueTournamentTitle", "", 38,
                        .035f, .12f, .965f, .88f, TextAlignmentOptions.Center);
                }
                _queueTournamentTitle.text = QueueTournamentTitle(_selectedArenaKind, _previewBetTier);
                ExpeditionArenaOverlays.ApplyQueue(_queueTmp, _leaveQueueButton, _queueTournamentTitle);
                if (_queueTmp != null) _queueTmp.text = status;
            }
            if (_queueTournamentTitle != null)
            {
                _queueTournamentTitle.gameObject.SetActive(visible);
                if (_queueTournamentTitle.transform.parent != null && _queueTournamentTitle.transform.parent.name == "ArenaQueueTournamentTitleFrame")
                    _queueTournamentTitle.transform.parent.gameObject.SetActive(visible);
            }
            if (_queueTmp != null) _queueTmp.gameObject.SetActive(visible);
            ApplyQueueLockState(visible && canLeave);
        }

        private static string QueueTournamentTitle(string kind, string tier)
        {
            kind = (kind ?? ArenaKindSmith).Trim().ToLowerInvariant();
            var title = kind == ArenaKindOre ? "Турнир руды" : kind == ArenaKindGold ? "Турнир золота" : "Турнир кузнеца";
            var label = TournamentTierLabel(kind, tier);
            if (label.Length == 0) return title;
            return title + "\n" + (kind == ArenaKindOre || kind == ArenaKindGold ? "Ставка " : "") + label;
        }

        private static string TournamentTierLabel(string kind, string tier)
        {
            kind = (kind ?? ArenaKindSmith).Trim().ToLowerInvariant();
            tier = (tier ?? "").Trim().ToLowerInvariant();
            if (kind == ArenaKindOre || kind == ArenaKindGold)
            {
                if (int.TryParse(tier, out var amount) && amount > 0)
                    return amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) +
                        (kind == ArenaKindOre ? " руды" : " золота");
                return "";
            }
            return tier == "green" ? "Зелёные слитки" : tier == "blue" ? "Синие слитки" : tier == "purple" ? "Фиолетовые слитки" : "";
        }

        private void ApplyQueueLockState(bool inQueue)
        {
            if (_queueUiLocked == inQueue)
            {
                if (_leaveQueueButton != null)
                    _leaveQueueButton.gameObject.SetActive(inQueue);
                if (_bettingToggle != null)
                    _bettingToggle.gameObject.SetActive(!inQueue);
                return;
            }

            _queueUiLocked = inQueue;
            if (_modePanelGo != null) _modePanelGo.SetActive(!inQueue);
            if (_backButtonGo != null) _backButtonGo.SetActive(!inQueue);
            if (_statsCardGo != null) _statsCardGo.SetActive(!inQueue);
            if (_bettingToggle != null) _bettingToggle.gameObject.SetActive(!inQueue);
            if (_leaveQueueButton != null) _leaveQueueButton.gameObject.SetActive(inQueue);
        }

        private void TryArmFightScene(ArenaTournamentState t)
        {
            if (!ServerConnectionGuard.CanPlay) return;
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
            var oppUid = ResolveArenaOpponentUserId(t, myUid);
            Match3LaunchContext.ArmArenaJoin(t.join_match_id, oppHint, t.join_opponent_is_bot, oppUid);

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

        private static string ResolveArenaOpponentUserId(ArenaTournamentState t, string myUserId)
        {
            if (t == null || string.IsNullOrEmpty(t.join_match_id) || string.IsNullOrEmpty(myUserId))
                return null;
            var mid = t.join_match_id.Trim();
            return ScanArenaRowsForOpponentUid(t.qf, mid, myUserId)
                   ?? ScanArenaRowsForOpponentUid(t.sf, mid, myUserId)
                   ?? ScanArenaRowsForOpponentUid(t.final_pairs, mid, myUserId);
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

        private static string ScanArenaRowsForOpponentUid(ArenaPairRow[] rows, string matchId, string myUid)
        {
            if (rows == null || rows.Length == 0) return null;
            foreach (var pr in rows)
            {
                if (pr == null || string.IsNullOrEmpty(pr.match_id)) continue;
                if (!string.Equals(pr.match_id.Trim(), matchId, StringComparison.Ordinal)) continue;

                if (string.Equals(pr.uid_a, myUid, StringComparison.Ordinal))
                    return string.IsNullOrWhiteSpace(pr.uid_b) ? null : pr.uid_b.Trim();
                if (string.Equals(pr.uid_b, myUid, StringComparison.Ordinal))
                    return string.IsNullOrWhiteSpace(pr.uid_a) ? null : pr.uid_a.Trim();
            }

            return null;
        }

        private void OpenBetModal(string kind)
        {
            if (!ServerConnectionGuard.CanPlay) { ServerConnectionGuard.ShowUnavailable(); return; }
            if (_busyJoin || _participationLocked)
                return;
            EnsureBetModalBuilt();
            if (_modalRoot == null) return;
            _selectedArenaKind = string.IsNullOrWhiteSpace(kind) ? ArenaKindSmith : kind.Trim().ToLowerInvariant();
            RebuildBetModalRowsForKind(_selectedArenaKind);
            _modalRoot.SetActive(true);
            _modalRoot.transform.SetAsLastSibling();
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
            // Legacy entry: базовая ставка турнира руды.
            await QueueJoinAsync("500");
        }

        private async Task QueueJoinAsync(string tier)
        {
            if (!ServerConnectionGuard.CanPlay) { ServerConnectionGuard.ShowUnavailable(); return; }
            if (_busyJoin || _participationLocked)
                return;
            _busyJoin = true;
            _pollVersion++;
            SetParticipationLocked(true);
            CloseBetModal();
            _previewBetTier = string.IsNullOrWhiteSpace(tier) ? "" : tier.Trim().ToLowerInvariant();
            SetQueuePresentation(true, "Вход в турнир…");
            var requestedScope = LocalAccountCacheScope.Current;
            try
            {
                // The modal can be opened during reconnect. A paid choice must
                // wait for authentication instead of silently ignoring the tap.
                NakamaBootstrap.EnsureExists();
                await ServerConnectionGuard.RequireAsync(default);
                if (this == null || !isActiveAndEnabled) return;
                if (!string.IsNullOrEmpty(requestedScope) && !LocalAccountCacheScope.IsCurrent(requestedScope)) return;
                requestedScope = LocalAccountCacheScope.Current;
                if (string.IsNullOrEmpty(requestedScope)) throw new InvalidOperationException("Нет активного аккаунта");
                var payload = JsonUtility.ToJson(new ArenaJoinPayload
                {
                    bet_tier = tier,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                    arena_kind = _selectedArenaKind ?? ArenaKindSmith,
                });
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaJoin, payload);
                if (this == null || !isActiveAndEnabled || !LocalAccountCacheScope.IsCurrent(requestedScope)) return;
                var raw = rpc?.Payload ?? "{}";
                var resp = JsonUtility.FromJson<ArenaJoinResponse>(raw);
                if (resp != null && resp.ok)
                {
                    if (!resp.in_tournament) ExpeditionAudio.Play(ExpeditionCue.QueueEnter);
                    _pollVersion++; // A pre-join idle snapshot cannot unlock a paid entry.
                    SetParticipationLocked(true);
                    CloseBetModal();
                    var qMax = resp.queue_max > 0 ? resp.queue_max : 8;
                    SetQueuePresentation(!resp.in_tournament, $"{Mathf.Clamp(resp.queue_count, 0, qMax)}/{qMax}", true);
                }
                else
                {
                    var err = resp?.err ?? raw;
                    ExpeditionAudio.Play(ExpeditionCue.UiBlocked);
                    Debug.LogWarning("[Arena8] join failed: " + err);
                    if (string.Equals(resp?.err, "not_enough_ingots", StringComparison.Ordinal))
                        ShowToast("Недостаточно слитков для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "not_enough_ore", StringComparison.Ordinal))
                        ShowToast("Недостаточно руды для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "not_enough_gold", StringComparison.Ordinal))
                        ShowToast("Недостаточно золота для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "bet_tier_mismatch", StringComparison.Ordinal))
                        ShowToast("Очередь занята другой ставкой", 2.6f);
                    else if (string.Equals(resp?.err, "tournaments_locked", StringComparison.Ordinal))
                        ShowToast("Турнир пока недоступен", 2.6f);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] join: " + e.Message);
                ExpeditionAudio.Play(ExpeditionCue.UiBlocked);
                if (this != null && isActiveAndEnabled) ShowToast("Не удалось подтвердить вход. Проверяем очередь…", 3f);
            }
            finally
            {
                _busyJoin = false;
                if (this != null && isActiveAndEnabled && _participationLocked) _ = PollOnceFireAndForget();
            }
        }

        /// <summary>Покинуть очередь с возвратом слитков (RPC).</summary>
        public async void LeaveQueueClicked()
        {
            if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady)
                return;
            var requestedScope = LocalAccountCacheScope.Current;
            try
            {
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() +
                              ",\"arena_kind\":\"" + (_selectedArenaKind ?? ArenaKindSmith) + "\"}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaLeave, payload);
                if (this == null || !isActiveAndEnabled || !LocalAccountCacheScope.IsCurrent(requestedScope)) return;
                var raw = rpc?.Payload ?? "{}";
                var resp = JsonUtility.FromJson<ArenaLeaveResponse>(raw);
                if (resp != null && resp.ok)
                {
                    _pollVersion++; // Ignore polls issued before this confirmed refund.
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (this == null || !isActiveAndEnabled || !LocalAccountCacheScope.IsCurrent(requestedScope)) return;
                        ApplyConfirmedQueueLeave();
                    });
                    _ = PollOnceFireAndForget();
                }
                else
                {
                    Debug.LogWarning("[Arena8] leave failed: " + (resp?.err ?? raw));
                    ExpeditionAudio.Play(ExpeditionCue.UiBlocked);
                    ShowToast("Не удалось выйти из очереди", 2.2f);
                    _ = PollOnceFireAndForget();
                }
            }
            catch (Exception e)
            {
                if (this == null || !isActiveAndEnabled || !LocalAccountCacheScope.IsCurrent(requestedScope)) return;
                Debug.LogWarning("[Arena8] leave: " + e.Message);
                ExpeditionAudio.Play(ExpeditionCue.UiBlocked);
            }
        }

        private void EnsureLeaveQueueButtonBuilt()
        {
            if (_leaveQueueButton != null || _queueTmp == null)
                return;

            var canvas = EnsureOverlayCanvas();
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
            ExpeditionArenaOverlays.ApplyQueue(_queueTmp, _leaveQueueButton);
            _leaveQueueButton.gameObject.SetActive(false);
        }

        private void ApplyConfirmedQueueLeave()
        {
            SetQueuePresentation(false);
            SetParticipationLocked(false);
        }

        /// <summary>Presentation hook: reuse this scene's three existing ingot sprites for its smith mode card.</summary>
        public static void ApplySmithTournamentButtonIcons(Transform button)
        {
            if (button == null || !button.gameObject.scene.IsValid()) return;
            foreach (var root in button.gameObject.scene.GetRootGameObjects())
            {
                var bridge = root.GetComponentInChildren<ArenaMatch8Bridge>(true);
                if (bridge == null) continue;
                ExpeditionArenaOverlays.ApplySmithIcons(button, bridge.iconGreenIngot, bridge.iconBlueIngot, bridge.iconPurpleIngot);
                return;
            }
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

            var canvas = EnsureOverlayCanvas();
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
            prect.pivot = new Vector2(0.5f, 0.5f);
            prect.anchoredPosition = Vector2.zero;
            prect.sizeDelta = new Vector2(793f, 1197f);

            var panelBg = panel.AddComponent<Image>();
            var panelSprite = iconBetPanelClear != null ? iconBetPanelClear : LoadSpriteAsset(BetPanelClearPath);
            if (panelSprite != null)
            {
                panelBg.sprite = panelSprite;
                panelBg.type = Image.Type.Sliced;
            }
            panelBg.color = betPanelColor;

            var vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(85, 105, 240, 50);
            vl.spacing = 22.24f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.reverseArrangement = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childScaleWidth = false;
            vl.childScaleHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            _modalPanel = panel.transform;
            var titleGo = CreateTmp(panel.transform, "Ставка турнира и награда при победе", 26, FontStyles.Bold);
            titleGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            _modalTitle = titleGo.GetComponent<TMP_Text>();
            titleGo.SetActive(false);

            var closeBtn = CreateButton(panel.transform, "Закрыть", () =>
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
            });
            _modalCloseButtonTr = closeBtn != null ? closeBtn.transform : null;

            _modalRoot.SetActive(false);
            RebuildBetModalRowsForKind(_selectedArenaKind);
        }

        private Canvas EnsureOverlayCanvas()
        {
            if (_overlayCanvas != null) return _overlayCanvas;
            // Never attach scene UI to the first arbitrary (possibly hidden or persistent) canvas.
            var go = new GameObject("ArenaSceneOverlays", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(go, gameObject.scene);
            _overlayCanvas = go.GetComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 300;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            return _overlayCanvas;
        }

        private void RebuildBetModalRowsForKind(string kind)
        {
            if (_modalPanel == null)
                return;

            // DestroyImmediate: Destroy() откладывается до конца кадра и ломает индекс спрайтов Row_*.
            for (var i = _modalPanel.childCount - 1; i >= 0; i--)
            {
                var ch = _modalPanel.GetChild(i);
                if (ch == null) continue;
                if (ch == _modalCloseButtonTr) continue;
                if (ch == _modalTitle?.transform) continue;
                if (ch.name.StartsWith("Row_", StringComparison.Ordinal))
                    DestroyImmediate(ch.gameObject);
            }

            var k = string.IsNullOrWhiteSpace(kind) ? ArenaKindSmith : kind.Trim().ToLowerInvariant();
            if (_modalTitle != null)
                _modalTitle.text = k == ArenaKindSmith
                    ? "Ставка турнира и награда при победе"
                    : (k == ArenaKindOre ? "Турнир руды: ставка и награда при победе" : "Турнир золота: ставка и награда при победе");

            var rowIndex = 0;
            if (k == ArenaKindOre)
            {
                MakeFixedBetRow(_modalPanel, rowIndex++, "500", "х 500", "2500", iconOre);
                MakeFixedBetRow(_modalPanel, rowIndex++, "2000", "х 2000", "10000", iconOre);
            }
            else if (k == ArenaKindGold)
            {
                MakeFixedBetRow(_modalPanel, rowIndex++, "600", "х 600", "3000", iconGold);
                MakeFixedBetRow(_modalPanel, rowIndex++, "2400", "х 2400", "12000", iconGold);
            }
            else
            {
                MakeBetRow(_modalPanel, rowIndex++, "green", "х " + SmithEntryAmount("green", 10), "300 · 300", iconGreenIngot);
                MakeBetRow(_modalPanel, rowIndex++, "blue", "х " + SmithEntryAmount("blue", 15), "600 · 600", iconBlueIngot);
                MakeBetRow(_modalPanel, rowIndex++, "purple", "х " + SmithEntryAmount("purple", 20), "1200 · 1200", iconPurpleIngot);
            }
            ExpeditionArenaOverlays.ApplyStakeChooser(_modalRoot.transform, _modalTitle, _modalCloseButtonTr, k, _smithRulesVersion);
        }

        private int SmithEntryAmount(string tier, int fallback)
        {
            if (_smithOffers != null)
                foreach (var offer in _smithOffers)
                    if (offer != null && offer.bet_tier == tier && offer.entry_amount > 0)
                        return offer.entry_amount;
            return _smithRulesVersion >= 2 ? fallback : 50;
        }

        private Sprite GetBetRowBgSprite(int rowIndexZeroBased)
        {
            if (rowIndexZeroBased <= 0)
                return iconBetRow1 != null ? iconBetRow1 : LoadSpriteAsset(BetRow1Path);
            if (rowIndexZeroBased == 1)
                return iconBetRow2 != null ? iconBetRow2 : LoadSpriteAsset(BetRow2Path);
            return iconBetRow3 != null ? iconBetRow3 : LoadSpriteAsset(BetRow3Path);
        }

        private static Sprite LoadSpriteAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
#if UNITY_EDITOR
            var byAssetPath = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (byAssetPath != null)
                return byAssetPath;
#endif
            var resourcesPath = assetPath.Replace("Assets/Resources/", string.Empty);
            if (resourcesPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - 4);
            return Resources.Load<Sprite>(resourcesPath);
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

        private void MakeFixedBetRow(Transform parent, int rowIndex, string betTier, string cost, string prize, Sprite icon)
        {
            var tier = string.IsNullOrWhiteSpace(betTier) ? "500" : betTier.Trim();
            var row = new GameObject("Row_fixed_" + tier);
            row.transform.SetParent(parent, false);
            if (_modalCloseButtonTr != null)
                row.transform.SetSiblingIndex(_modalCloseButtonTr.GetSiblingIndex());
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 250f);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 240f;
            rowLe.preferredHeight = 250f;
            rowLe.flexibleHeight = 0f;

            var rowHl = row.AddComponent<HorizontalLayoutGroup>();
            rowHl.spacing = 10f;
            rowHl.childAlignment = TextAnchor.MiddleLeft;
            rowHl.childForceExpandWidth = false;
            rowHl.childForceExpandHeight = false;
            rowHl.padding = new RectOffset(8, 8, 8, 8);

            var rowImg = row.AddComponent<Image>();
            ApplyBetRowBg(rowImg, rowIndex);

            if (icon != null)
            {
                var ico = new GameObject("ResIcon");
                ico.transform.SetParent(row.transform, false);
                var img = ico.AddComponent<Image>();
                img.sprite = icon;
                var irt = ico.GetComponent<RectTransform>();
                irt.sizeDelta = new Vector2(120f, 120f);
                var le = ico.AddComponent<LayoutElement>();
                le.preferredWidth = 120f;
                le.preferredHeight = 120f;
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
            prizeHl.childForceExpandWidth = false;
            prizeHl.childForceExpandHeight = false;

            if (icon != null)
                CreateIconImage(prizeGo.transform, icon);
            CreateTmp(prizeGo.transform, prize, 22, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(() => _ = QueueJoinAsync(tier));
        }

        private void EnsureToastBuilt()
        {
            if (_toastRoot != null)
                return;
            var canvas = EnsureOverlayCanvas();
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
            ExpeditionArenaOverlays.ApplyToast(_toastRoot.transform, _toastText);
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

        private void MakeBetRow(Transform parent, int rowIndex, string tier, string cost, string prize, Sprite ingotSp)
        {
            var row = new GameObject("Row_" + tier);
            row.transform.SetParent(parent, false);
            if (_modalCloseButtonTr != null)
                row.transform.SetSiblingIndex(_modalCloseButtonTr.GetSiblingIndex());
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 250f);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 240f;
            rowLe.preferredHeight = 250f;
            rowLe.flexibleHeight = 0f;

            var rowHl = row.AddComponent<HorizontalLayoutGroup>();
            rowHl.spacing = 10f;
            rowHl.childAlignment = TextAnchor.MiddleLeft;
            rowHl.childForceExpandWidth = false;
            rowHl.childForceExpandHeight = false;
            rowHl.padding = new RectOffset(8, 8, 8, 8);

            var rowImg = row.AddComponent<Image>();
            ApplyBetRowBg(rowImg, rowIndex);

            if (ingotSp != null)
            {
                var icon = new GameObject("ResIcon");
                icon.transform.SetParent(row.transform, false);
                var img = icon.AddComponent<Image>();
                img.sprite = ingotSp;
                var irt = icon.GetComponent<RectTransform>();
                irt.sizeDelta = new Vector2(120f, 120f);
                var le = icon.AddComponent<LayoutElement>();
                le.preferredWidth = 120f;
                le.preferredHeight = 120f;
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
            prizeHl.childForceExpandWidth = false;
            prizeHl.childForceExpandHeight = false;

            if (iconOre != null)
                CreateIconImage(prizeGo.transform, iconOre);
            CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

            if (iconGold != null)
                CreateIconImage(prizeGo.transform, iconGold);
            CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(() =>
            {
                if (tier == "green") OnPickBetGreen();
                else if (tier == "blue") OnPickBetBlue();
                else OnPickBetPurple();
            });
        }

        private void ApplyBetRowBg(Image rowImg, int rowIndexZeroBased)
        {
            if (rowImg == null)
                return;
            var sp = GetBetRowBgSprite(rowIndexZeroBased);
            if (sp != null)
            {
                rowImg.sprite = sp;
                rowImg.type = Image.Type.Sliced;
                rowImg.color = Color.white;
            }
            else
            {
                rowImg.color = new Color(0.12f, 0.28f, 0.52f, 0.55f);
            }
        }

        private static GameObject CreateIconImage(Transform parent, Sprite sp)
        {
            var icon = new GameObject("Ic");
            icon.transform.SetParent(parent, false);
            var img = icon.AddComponent<Image>();
            img.sprite = sp;
            var rt = icon.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120f, 120f);
            var le = icon.AddComponent<LayoutElement>();
            le.preferredWidth = 120f;
            le.preferredHeight = 120f;
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

            var canvas = EnsureOverlayCanvas();
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
            _prizeFooterText = _bracketRoot.transform.Find("PrizeFooter")?.GetComponent<TMP_Text>();

            // Fallback: if prefab missing OR prefab does not contain required nodes, build procedurally.
            if (_rowsRoot == null || _cdText == null)
            {
                DestroyPresentationObject(_bracketRoot);
                _bracketRoot = BuildBracketProcedural(canvas.transform);
                _bracketTitle = _bracketRoot.transform.Find("Title")?.GetComponent<TMP_Text>();
                _cdText = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
                _rowsRoot = _bracketRoot.transform.Find("ArenaRows");
                _headerTemplate = null;
                _pairRowTemplate = null;
                _prizeFooterText = null;
            }

            EnsurePrizeFooter();
            ExpeditionArenaOverlays.ApplyBracket(_bracketRoot.transform);
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
            SetQueuePresentation(false);
            EnsureBracketBuilt();
            if (_bracketRoot == null)
                return;

            _bracketRoot.SetActive(true);

            ApplyBracketOverlayTitle(t);

            if (_cdText != null)
            {
                if (t.phase == "countdown")
                    _cdText.text = "До начала боя · " + Mathf.Max(0, t.countdown_left) + " сек";
                else
                    _cdText.text = PhaseTitle(t.phase);
            }

            UpdatePrizeFooter(t);

            if (_rowsRoot == null)
                return;

            ClearRowsKeepingTemplates(_rowsRoot);

            var current = t.phase == "countdown" ? (string.IsNullOrEmpty(t.next_round) ? "qf" : t.next_round) : t.phase;
            BuildBracketColumn("qf", "1/4 ФИНАЛА", t.qf, 4, current);
            BuildBracketColumn("sf", "1/2 ФИНАЛА", t.sf, 2, current);
            BuildBracketColumn("final", "ФИНАЛ", t.final_pairs, 1, current);
            ExpeditionArenaOverlays.BuildBracketConnections(_rowsRoot);
            UpdateTournamentSummary(t);
            ExpeditionArenaOverlays.ApplyBracket(_bracketRoot.transform);
        }

        private void BuildBracketColumn(string round, string title, ArenaPairRow[] pairs, int count, string current)
        {
            var bounds = ExpeditionArenaOverlays.BracketColumnBounds(round);
            var x0 = bounds.x; var x1 = bounds.y;
            var column = ExpeditionUi.Rect(_rowsRoot, "Round_" + round, x0, 0, x1, 1);
            ExpeditionUi.Text(_rowsRoot, "RoundLabel_" + round, title, round == "qf" ? 22 : 19, x0, 1.01f, x1, 1.07f, TextAlignmentOptions.Center).color = ExpeditionTheme.Muted;
            for (var i = 0; i < count; i++)
            {
                var actual = pairs != null && i < pairs.Length ? pairs[i] : null;
                var pair = actual ?? new ArenaPairRow { slot = i + 1, display_a = " - ", display_b = " - ", status = "pending" };
                var row = CreatePairRowFromTemplate(column, pair, round, current == round && actual != null);
                var center = count == 4 ? .875f - i * .25f : count == 2 ? .75f - i * .5f : .5f;
                ExpeditionUi.SetRect(row as RectTransform, 0, center - .105f, 1, center + .105f);
            }
        }

        private void UpdateTournamentSummary(ArenaTournamentState tournament)
        {
            var summary = _bracketRoot.transform.Find("ExpeditionTournamentSummary")?.GetComponent<TMP_Text>();
            if (summary == null) return;
            ArenaPairRow own = null;
            foreach (var pair in RowsForCurrentBracketPhase(tournament) ?? Array.Empty<ArenaPairRow>())
                if (pair != null && (pair.uid_a == _myUserId || pair.uid_b == _myUserId)) { own = pair; break; }
            var opponent = own == null ? "Ожидаем следующую пару" : "Ваш соперник: " + (own.uid_a == _myUserId ? own.display_b : own.display_a);
            var status = own != null && own.status == "done" ? "Бой завершён · ожидаем остальных" : tournament.phase == "countdown" ? "Ожидаем начала боя" : "Турнир продолжается";
            var tierLabel = TournamentTierLabel(tournament.kind, tournament.bet_tier);
            if (string.IsNullOrEmpty(tierLabel)) tierLabel = "—";
            var bettingHint = !_bettingUiEnabled ? "Прогнозы выключены"
                : tournament.bets_open ? "Прогноз: выберите игрока в чужой паре"
                : tournament.phase == "final" || (tournament.phase == "countdown" && tournament.next_round == "final") ? "В финале прогнозов нет"
                : "Прогнозы доступны до начала раунда";
            summary.enableAutoSizing = true;
            summary.fontSizeMin = 22;
            summary.fontSizeMax = 26;
            summary.text = opponent + "\n" + status + " · Взнос: " + tierLabel.ToLowerInvariant() + "\n" + bettingHint;
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
                ch.gameObject.SetActive(false);
                DestroyPresentationObject(ch.gameObject);
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
            header.gameObject.name = "RoundHeader";

            foreach (var pr in rows)
                CreatePairRowFromTemplate(parent, pr);
        }

        private Transform CreatePairRowFromTemplate(Transform parent, ArenaPairRow pr, string round = "", bool current = false)
        {
            if (parent == null || pr == null) return null;

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

            ExpeditionArenaOverlays.ApplyPairCard(row);
            var tag = ExpeditionUi.Ensure<ExpeditionArenaPairTag>(row.gameObject);
            tag.Round = round; tag.Slot = pr.slot; tag.Current = current;
            BindPairRowData(row, pr);

            var select = row.GetComponent<PairRowBetSelect>();
            if (select == null)
                select = row.gameObject.AddComponent<PairRowBetSelect>();
            select.Configure(pr.slot > 0 ? pr.slot : row.GetSiblingIndex(), pr.uid_a, pr.uid_b, OnPairBetSideChanged);
            ApplyBettingUiToRow(row);
            return row;
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
            // A framed plate was retained by ??= on generated bars, making the
            // thin red fill almost transparent. HP always uses a solid sprite.
            img.sprite = ArenaBracketHpWhiteSprite();
            img.overrideSprite = null;
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

        private static void SetHp(Image img, float hp, float maxHp = 0, bool known = false)
        {
            if (img == null)
                return;
            EnsureHpFillRenderable(img);

            img.fillAmount = known && maxHp > 0 ? Mathf.Clamp01(hp / maxHp) : 0;
            img.enabled = known && maxHp > 0;
            img.rectTransform.offsetMin = new Vector2(1.5f, .5f);
            img.rectTransform.offsetMax = new Vector2(-1.5f, -.5f);
        }

        private void HideBracket()
        {
            if (_bracketRoot != null)
                _bracketRoot.SetActive(false);
        }

        private static void DestroyPresentationObject(GameObject target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
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
