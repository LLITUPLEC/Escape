using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.Match3
{
    /// <summary>
    /// Main controller for the DuelMatch3 scene.
    ///
    /// SETUP (one-time):
    ///  1. Create scene "DuelMatch3" (empty).
    ///  2. Add empty GameObject → attach this component.
    ///  3. Run Tools → Match3 → Создать префабы UI  (creates prefabs).
    ///  4. Assign prefabs in the Inspector (all optional — falls back to
    ///     procedural generation if any are left null).
    ///  5. Add scene to Build Settings.
    ///
    /// NETWORK ARCHITECTURE (client-authoritative relay):
    ///  • Nakama acts as a relay only — no server-side game logic needed.
    ///  • The active player runs timer + calculations locally, then sends
    ///    the full board state + both players' stats to the opponent via
    ///    Nakama match-state messages.
    ///  • This mirrors the existing DuelRoom pattern.
    /// </summary>
    public sealed class DuelMatch3Manager : MonoBehaviour
    {
        // ─── Prefab Fields ────────────────────────────────────────────────────────
        [Header("Prefabs — assign after running Tools → Match3 → Создать префабы UI")]
        [Tooltip("Player panel (avatar, HP, mana) — MY side")]
        [SerializeField] private Match3PlayerPanel  myPanelPrefab;
        [Tooltip("Player panel — OPPONENT side")]
        [SerializeField] private Match3PlayerPanel  opPanelPrefab;
        [Tooltip("Ability buttons (Cross / Square)")]
        [SerializeField] private Match3AbilityPanel abilityPanelPrefab;
        [Tooltip("6×6 board with frame and GridLayout container")]
        [SerializeField] private Match3BoardView    boardViewPrefab;
        [Tooltip("Turn label + countdown timer")]
        [SerializeField] private Match3GameHUD      hudPrefab;
        [Tooltip("'Searching for opponent' overlay")]
        [SerializeField] private Match3SearchingPanel searchingPanelPrefab;
        [Tooltip("Game-over result overlay")]
        [SerializeField] private Match3GameOverPanel  gameOverPanelPrefab;

        [Header("Audio (Match3 SFX)")]
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioClip sfxLineClear;
        [SerializeField] private AudioClip sfxAbilitySquare;
        [SerializeField] private AudioClip sfxAbilityCross;
        [SerializeField] private AudioClip sfxAbilityPetard;
        [SerializeField] private AudioClip sfxCascadeFall;
        [SerializeField] private AudioClip sfxExtraTurn;
        [SerializeField] private AudioClip sfxTimerEnd;
        [SerializeField] private AudioClip sfxDamageHit;
        [SerializeField] private AudioClip sfxDamageCrit;
        [SerializeField] private AudioClip sfxVictory;
        [SerializeField] private AudioClip sfxDefeat;

        [Header("Ability Icon Sprites")]
        [SerializeField] private Sprite petardAbilitySprite;
        [SerializeField] private Sprite crossAbilitySprite;
        [SerializeField] private Sprite squareAbilitySprite;
        [SerializeField] private Sprite shieldAbilitySprite;
        [SerializeField] private Sprite furyAbilitySprite;
        [SerializeField] private Sprite spellBorderSprite;

        [Header("UI Prefabs")]
        [SerializeField] private DamagePopupView damagePopupPrefab;

        // ─── OpCodes (match-3 specific, 10+ to avoid collision with DuelRoom) ─────
        private static class M3Op
        {
            public const long BoardSync     = 10;
            public const long GameOver      = 11;
            public const long PlayerLeft    = 12;
            public const long ActionRequest = 13;
            public const long ActionReject  = 14;
            public const long SelectionSync = 15;
            public const long SnapshotRequest = 16;
        }

        private const string RpcMatch3StatsRecord = "duel_match3_stats_record";
        private const string RpcMatch3PveCreate = "duel_match3_pve_create";
        private const string DefaultPveBotId = "slime_1";

        // ─── Game Constants ───────────────────────────────────────────────────────
        private const int   MaxHp           = 150;
        private const int   MaxMana         = 100;
        private const float TurnDuration    = 30f;
        private const int   CrossAbilityCost  = 20;
        private const int   SquareAbilityCost = 20;
        private const int   PetardAbilityCost = 30;
        private const int   ShieldAbilityCost = 40;
        private const int   FuryAbilityCost   = 30;
        private const int   CrossCooldownTurns = 2;
        private const int   SquareCooldownTurns = 2;
        private const int   PetardCooldownTurns = 1;
        private const int   ShieldCooldownTurns = 1;
        private const int   FuryCooldownTurns   = 2;
        private const int   AbilityBaseDamage = 3;
        private const int   PetardDamage = 15;
        private const int   SkullDamage = 5;
        private const int   AnkhHeal = 1;
        [Header("Balance Tweaks (can adjust before launch)")]
        [SerializeField, Range(0f, 1f)] private float furyCritChance = 0.25f; // 1.0 = 100%

        private const int ShieldDurationTurns = 3;
        private const int ShieldMaxStacks = 3;
        private const int ShieldArmorPerStack = 4;
        private const int ShieldHealPerStack = 3;
        private const string LocalPlayerId = "a-local-player";
        private const string BotPlayerId = "z-bot-player";

        // ─── Nakama ───────────────────────────────────────────────────────────────
        private IMatch    _match;
        private string    _myUserId;
        private string    _opUserId;
        private string    _matchmakerTicket;
        private bool      _isLeavingToMenu;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<IMatchmakerMatched> _mmTcs;

        // ─── Game State ───────────────────────────────────────────────────────────
        private Match3BoardLogic _board;
        private PlayerStats      _myStats;
        private PlayerStats      _opStats;
        private bool  _isMyTurn;
        private float _turnTimer;
        private bool  _gameEnded;
        private Coroutine _remoteSyncRoutine;
        private readonly Queue<M3BoardSyncMsg> _pendingBoardSyncs = new();
        private Coroutine _snapshotRetryRoutine;
        private bool _hasInitialBoardSync;
        private int _remoteSelX = -1, _remoteSelY = -1;
        private bool _resultRecorded;
        private string _lastRewardText;
        private bool _pendingGameOver;
        private bool _pendingGameOverWon;
        private Match3LaunchMode _launchMode = Match3LaunchMode.Multiplayer;
        private bool _isSoloBotMode;
        private bool _useLocalBotSimulation;
        private System.Random _botRandom;
        private Coroutine _botTurnRoutine;

        // Input
        private int          _selX = -1, _selY = -1;
        private AbilityType? _pendingAbility;
        private bool         _inputBlocked;

        // ─── UI Component Instances ───────────────────────────────────────────────
        private Match3PlayerPanel   _myPanel;
        private Match3PlayerPanel   _opPanel;
        private Match3AbilityPanel  _abilityPanel;
        private Match3BoardView     _boardView;
        private Match3GameHUD       _hud;
        private Match3SearchingPanel  _searchingPanel;
        private Match3GameOverPanel   _gameOverPanel;
        private GameObject _pveSelectorRoot;
        private TMP_Text _pveBotTitleText;
        private TMP_Text _pveBotStatsText;
        private Button _pvePrevButton;
        private Button _pveNextButton;
        private Button _pveStartButton;
        private List<PveBotInfo> _pveBots = new();
        private int _selectedPveBotIndex;
        private PveProgressInfo _pveProgress;

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private async void Start()
        {
            _cts     = new CancellationTokenSource();
            _board   = new Match3BoardLogic();
            _myStats = new PlayerStats();
            _opStats = new PlayerStats();
            _launchMode = Match3LaunchContext.ConsumeMode();
            _isSoloBotMode = _launchMode == Match3LaunchMode.SoloBot;
            _useLocalBotSimulation = false;
            _botRandom = new System.Random(Environment.TickCount);

            EnsureCamera();
            BuildUI();
            EnsureAudioSource();
            TryAutoAssignSfxInEditor();
            _searchingPanel?.Show(_isSoloBotMode ? "Подготовка боя с ботом…" : "Поиск соперника…");

            if (_isSoloBotMode)
            {
                try
                {
                    await PreparePveLobbyAsync(_cts.Token);
                }
                catch (OperationCanceledException) { /* destroyed */ }
                catch (Exception e)
                {
                    Debug.LogError("[Match3] Не удалось запустить PVE матч: " + e.Message);
                    MainThreadDispatcher.Enqueue(() =>
                        _searchingPanel?.Show("Ошибка PVE сервера.\nПроверьте подключение и попробуйте снова."));
                }
                return;
            }

            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                _myUserId = NakamaBootstrap.Instance.Session.UserId;
                HookSocket(NakamaBootstrap.Instance.Socket);
                await FindMatchAsync(_cts.Token);
                MainThreadDispatcher.Enqueue(OnMatchFound);
            }
            catch (OperationCanceledException) { /* destroyed */ }
            catch (Exception e)
            {
                Debug.LogException(e);
                MainThreadDispatcher.Enqueue(() =>
                    _searchingPanel?.Show("Ошибка подключения: " + e.Message));
            }
        }

        private void OnDestroy()
        {
            if (_botTurnRoutine != null)
            {
                StopCoroutine(_botTurnRoutine);
                _botTurnRoutine = null;
            }
            _mmTcs?.TrySetCanceled();
            _ = CancelMatchmakerTicketAsync();
            _cts?.Cancel();
            _cts?.Dispose();
            if (NakamaBootstrap.Instance?.Socket != null)
                UnhookSocket(NakamaBootstrap.Instance.Socket);
        }

        private void Update()
        {
            if (_gameEnded) return;

            if (_isMyTurn)
            {
                if (_inputBlocked) return;
                _turnTimer -= Time.deltaTime;
                _hud?.SetTimer(Mathf.CeilToInt(Mathf.Max(0f, _turnTimer)).ToString());
                if (_turnTimer <= 0f) OnTurnTimerExpired();
            }
            else
            {
                if (_turnTimer <= 0f) return;
                _turnTimer -= Time.deltaTime;
                _hud?.SetTimer(Mathf.CeilToInt(Mathf.Max(0f, _turnTimer)).ToString());
            }
        }

        // ─── Matchmaking ──────────────────────────────────────────────────────────

        private async Task FindMatchAsync(CancellationToken ct)
        {
            _mmTcs = new TaskCompletionSource<IMatchmakerMatched>();
            ct.ThrowIfCancellationRequested();
            await CancelMatchmakerTicketAsync();

            // Use "*" — same as existing DuelRoom.
            // Both players enter from DuelMatch3 scene so they naturally pair up.
            var ticket = await NakamaBootstrap.Instance.Socket.AddMatchmakerAsync(
                query: "*",
                minCount: 2,
                maxCount: 2,
                stringProperties: new Dictionary<string, string> { { "mode", "match3" } });
            _matchmakerTicket = ticket?.Ticket;

            var matched = await _mmTcs.Task;
            _mmTcs = null;
            _matchmakerTicket = null; // consumed by successful match
            ct.ThrowIfCancellationRequested();

            _match = await NakamaBootstrap.Instance.Socket.JoinMatchAsync(matched);

            if (matched?.Users != null)
                foreach (var u in matched.Users)
                    if (u.Presence.UserId != _myUserId)
                        _opUserId = u.Presence.UserId;
        }

        private void OnMatchFound()
        {
            _searchingPanel?.Hide();
            _myPanel?.SetPlayerName("Вы");
            _opPanel?.SetPlayerName("Соперник");
            _resultRecorded = false;
            _lastRewardText = null;
            StartGameWaitingServer();
        }

        private async Task PreparePveLobbyAsync(CancellationToken ct)
        {
            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
            _myUserId = NakamaBootstrap.Instance.Session.UserId;
            HookSocket(NakamaBootstrap.Instance.Socket);
            await LoadPveCatalogAsync(ct);
            MainThreadDispatcher.Enqueue(() =>
            {
                _searchingPanel?.Hide();
                ShowPveSelector(true);
                RefreshPveSelectorTexts();
            });
        }

        private async Task StartSoloBotServerAsync(CancellationToken ct, string botId)
        {
            var safeBotId = string.IsNullOrWhiteSpace(botId) ? DefaultPveBotId : botId;
            var payload = JsonUtility.ToJson(new PveCreateRpcRequest
            {
                bot_id = safeBotId,
                session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
            });
            var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                NakamaBootstrap.Instance.Session, RpcMatch3PveCreate, payload);

            var model = JsonUtility.FromJson<PveCreateRpcResponse>(rpc?.Payload ?? "");
            if (model == null || !model.ok || string.IsNullOrEmpty(model.match_id))
            {
                var detail = model != null && !string.IsNullOrEmpty(model.err) ? model.err : "unknown";
                Debug.LogWarning($"duel_match3_pve_create: ok={model?.ok} err={detail} payload={rpc?.Payload}");
                throw new Exception($"pve_create_failed ({detail})");
            }

            _opUserId = string.IsNullOrEmpty(model.bot_user_id) ? ("zz-bot-" + DefaultPveBotId) : model.bot_user_id;
            _match = await NakamaBootstrap.Instance.Socket.JoinMatchAsync(model.match_id);
            var botName = string.IsNullOrEmpty(model.bot_name) ? "Бот" : model.bot_name;
            MainThreadDispatcher.Enqueue(() => OnBotMatchFound(botName));
        }

        private async Task LoadPveCatalogAsync(CancellationToken ct)
        {
            var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                NakamaBootstrap.Instance.Session, "duel_match3_pve_catalog_get", "{}");
            var payload = rpc?.Payload ?? string.Empty;
            var model = JsonUtility.FromJson<PveCatalogRpcResponse>(payload);
            if (model == null || !model.ok || model.bots == null || model.bots.Length == 0)
                throw new Exception("pve_catalog_empty");

            _pveBots = new List<PveBotInfo>(model.bots);
            _pveProgress = model.progression ?? new PveProgressInfo();
            _selectedPveBotIndex = Mathf.Clamp(_selectedPveBotIndex, 0, _pveBots.Count - 1);
            ct.ThrowIfCancellationRequested();
        }

        private void OnBotMatchFound(string botName)
        {
            ShowPveSelector(false);
            _searchingPanel?.Hide();
            _myPanel?.SetPlayerName("Вы");
            _opPanel?.SetPlayerName(botName);
            _resultRecorded = false;
            _lastRewardText = null;
            StartGameWaitingServer();
        }

        private void ShowPveSelector(bool visible)
        {
            if (_pveSelectorRoot != null)
                _pveSelectorRoot.SetActive(visible);
        }

        private void RefreshPveSelectorTexts()
        {
            if (_pveBots == null || _pveBots.Count == 0) return;
            _selectedPveBotIndex = Mathf.Clamp(_selectedPveBotIndex, 0, _pveBots.Count - 1);
            var bot = _pveBots[_selectedPveBotIndex];
            if (_pveBotTitleText != null)
                _pveBotTitleText.text = $"{bot.name} ({bot.id})";
            if (_pveBotStatsText != null)
            {
                _pveBotStatsText.text =
                    $"Сложность: {bot.difficulty}\n" +
                    $"HP бонус: +{bot.hp_bonus}\n" +
                    $"Старт. мана: {bot.start_mana}\n" +
                    $"Награда: +{bot.reward_xp} XP, +{bot.reward_gold} золота";
            }
        }

        private void SelectPrevPveBot()
        {
            if (_pveBots == null || _pveBots.Count == 0) return;
            _selectedPveBotIndex--;
            if (_selectedPveBotIndex < 0) _selectedPveBotIndex = _pveBots.Count - 1;
            RefreshPveSelectorTexts();
        }

        private void SelectNextPveBot()
        {
            if (_pveBots == null || _pveBots.Count == 0) return;
            _selectedPveBotIndex = (_selectedPveBotIndex + 1) % _pveBots.Count;
            RefreshPveSelectorTexts();
        }

        private async void StartSelectedPveBot()
        {
            if (_pveBots == null || _pveBots.Count == 0) return;
            if (_pveStartButton != null) _pveStartButton.interactable = false;
            _searchingPanel?.Show("Создание боя с боссом...");
            try
            {
                var bot = _pveBots[Mathf.Clamp(_selectedPveBotIndex, 0, _pveBots.Count - 1)];
                await StartSoloBotServerAsync(_cts != null ? _cts.Token : CancellationToken.None, bot.id);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _searchingPanel?.Show("Не удалось создать PVE бой.\nПопробуйте ещё раз.");
                ShowPveSelector(true);
                if (_pveStartButton != null) _pveStartButton.interactable = true;
            }
        }

        // ─── Game Flow ────────────────────────────────────────────────────────────

        private void StartGameWaitingServer()
        {
            _hasInitialBoardSync = false;
            _pendingBoardSyncs.Clear();
            _pendingGameOver = false;
            _pendingGameOverWon = false;
            _remoteSelX = _remoteSelY = -1;
            _boardView?.RefreshAll(_board);
            RefreshStatsUI();
            _abilityPanel?.Refresh(_myStats, false, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
            _hud?.SetTurn("Ожидание синхронизации…");
            _hud?.SetTimer("—");
            _boardView?.SetDimmed(true);
            _inputBlocked = true;
            if (_snapshotRetryRoutine != null) StopCoroutine(_snapshotRetryRoutine);
            _snapshotRetryRoutine = StartCoroutine(RequestSnapshotUntilSynced());
        }

        private void BeginMyTurn()
        {
            if (_botTurnRoutine != null)
            {
                StopCoroutine(_botTurnRoutine);
                _botTurnRoutine = null;
            }
            _isMyTurn    = true;
            _turnTimer   = TurnDuration;
            _inputBlocked = false;
            _pendingAbility = null;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _remoteSelX = _remoteSelY = -1;
            _abilityPanel?.ShowHint(false);
            _abilityPanel?.SetSelectedAbility(null);

            _hud?.SetTurn("Ваш ход!");
            _hud?.SetTimer(Mathf.CeilToInt(TurnDuration).ToString());
            _boardView?.SetDimmed(false);

            _abilityPanel?.Refresh(_myStats, true, false, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
        }

        private void BeginOpponentTurn()
        {
            _isMyTurn    = false;
            _turnTimer   = TurnDuration;
            _inputBlocked = true;
            _pendingAbility = null;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _remoteSelX = _remoteSelY = -1;
            _abilityPanel?.ShowHint(false);
            _abilityPanel?.SetSelectedAbility(null);

            _hud?.SetTurn("Ход соперника…");
            _hud?.SetTimer(Mathf.CeilToInt(TurnDuration).ToString());
            _boardView?.SetDimmed(true);
            _abilityPanel?.Refresh(_myStats, false, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);

            if (_useLocalBotSimulation && !_gameEnded)
            {
                if (_botTurnRoutine != null) StopCoroutine(_botTurnRoutine);
                _botTurnRoutine = StartCoroutine(BotTurnRoutine());
            }
        }

        private void OnTurnTimerExpired()
        {
            if (_gameEnded) return;
            PlaySfx(sfxTimerEnd);
            _inputBlocked = true; // ждём серверный тик/обновление
        }

        // ─── Input ────────────────────────────────────────────────────────────────

        private void OnCellClicked(int x, int y)
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;

            if (_pendingAbility.HasValue)
            {
                ExecuteAbility(_pendingAbility.Value, x, y);
                return;
            }

            if (_selX < 0)
            {
                _selX = x; _selY = y;
                _boardView?.SetCellSelected(x, y, true);
                SendSelectionSync(x, y, true);
            }
            else if (_selX == x && _selY == y)
            {
                _boardView?.SetCellSelected(x, y, false);
                SendSelectionSync(x, y, false);
                _selX = _selY = -1;
            }
            else
            {
                int px = _selX, py = _selY;
                _boardView?.SetCellSelected(px, py, false);
                SendSelectionSync(px, py, false);
                _selX = _selY = -1;
                TrySwapCells(px, py, x, y);
            }
        }

        private void OnCellSwiped(int fromX, int fromY, int toX, int toY)
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            if (_pendingAbility.HasValue) return;
            if (Mathf.Abs(fromX - toX) + Mathf.Abs(fromY - toY) != 1) return;

            if (_selX >= 0 && _selY >= 0)
            {
                _boardView?.SetCellSelected(_selX, _selY, false);
                SendSelectionSync(_selX, _selY, false);
                _selX = _selY = -1;
            }

            TrySwapCells(fromX, fromY, toX, toY);
        }

        private void TrySwapCells(int x1, int y1, int x2, int y2)
        {
            if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) != 1)
            {
                // Not adjacent → select new cell.
                _selX = x2; _selY = y2;
                _boardView?.SetCellSelected(x2, y2, true);
                SendSelectionSync(x2, y2, true);
                return;
            }

            _inputBlocked = true;
            var req = new M3ActionRequest
            {
                actionType = 1,
                fromX = x1, fromY = y1,
                toX = x2, toY = y2,
                cx = -1, cy = -1,
            };
            SendActionRequest(req);
        }

        private void ExecuteAbility(AbilityType ability, int cx, int cy)
        {
            _pendingAbility = null;
            _abilityPanel?.SetSelectedAbility(null);
            _abilityPanel?.ShowHint(false);

            if (!IsAbilityAvailable(ability))
            {
                _abilityPanel?.Refresh(_myStats, _isMyTurn, false, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
                return;
            }

            StartCoroutine(SendAbilityRequestRoutine(ability, cx, cy));
        }

        private IEnumerator SendAbilityRequestRoutine(AbilityType ability, int cx, int cy)
        {
            _inputBlocked = true;
            if (_boardView != null && (ability == AbilityType.Cross || ability == AbilityType.Square))
            {
                yield return _boardView.AnimateAbilityArea(ability, cx, cy, 0.24f);
            }
            var req = new M3ActionRequest
            {
                actionType = ability == AbilityType.Cross ? 2 :
                             ability == AbilityType.Square ? 3 :
                             ability == AbilityType.Petard ? 4 :
                             ability == AbilityType.Shield ? 5 : 6,
                fromX = -1, fromY = -1, toX = -1, toY = -1,
                cx = (ability == AbilityType.Cross || ability == AbilityType.Square) ? cx : -1,
                cy = (ability == AbilityType.Cross || ability == AbilityType.Square) ? cy : -1,
            };
            SendActionRequest(req);
        }

        // ─── Game Over ────────────────────────────────────────────────────────────

        private void ShowGameOver(bool won)
        {
            if (!_resultRecorded)
            {
                _resultRecorded = true;
                _ = RecordMatch3ResultServerAsync(won);
            }
            _isMyTurn    = false;
            _inputBlocked = true;
            _boardView?.SetDimmed(false);
            PlaySfx(won ? sfxVictory : sfxDefeat);
            var rewardText = won && !string.IsNullOrEmpty(_lastRewardText) ? _lastRewardText : null;
            _gameOverPanel?.Show(won, rewardText);
        }

        private async Task RecordMatch3ResultServerAsync(bool won)
        {
            try
            {
                if (NakamaBootstrap.Instance == null) return;
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts != null ? _cts.Token : CancellationToken.None);
                if (NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null) return;
                var payload = JsonUtility.ToJson(new StatsRecordRpcRequest
                {
                    won = won,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                });
                await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsRecord, payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Match3] Не удалось записать серверную статистику: " + e.Message);
            }
        }

        // ─── Networking — Send ────────────────────────────────────────────────────

        private void SendActionRequest(M3ActionRequest req)
        {
            if (_gameEnded) return;
            if (_useLocalBotSimulation)
            {
                HandleLocalActionRequest(req, _myUserId);
                return;
            }
            if (_match == null) return;
            _ = SendStateAsync(M3Op.ActionRequest,
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)));
        }

        private void SendSelectionSync(int x, int y, bool selected)
        {
            if (_gameEnded) return;
            if (_useLocalBotSimulation) return;
            if (_match == null) return;
            var msg = new M3SelectionSyncMsg { x = x, y = y, selected = selected };
            _ = SendStateAsync(M3Op.SelectionSync, Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg)));
        }

        private void RequestSnapshot()
        {
            if (_useLocalBotSimulation || _match == null || _gameEnded) return;
            _ = SendStateAsync(M3Op.SnapshotRequest, Encoding.UTF8.GetBytes("{}"));
        }

        private IEnumerator RequestSnapshotUntilSynced()
        {
            const int maxAttempts = 8;
            for (int i = 0; i < maxAttempts && !_hasInitialBoardSync && !_gameEnded; i++)
            {
                RequestSnapshot();
                yield return new WaitForSeconds(1.0f);
            }
            _snapshotRetryRoutine = null;
        }

        private async Task SendStateAsync(long opCode, byte[] data)
        {
            try
            {
                if (_useLocalBotSimulation) return;
                if (_match != null && NakamaBootstrap.Instance?.Socket?.IsConnected == true)
                    await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(_match.Id, opCode, data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Match3] SendState op={opCode}: {e.Message}");
            }
        }

        // ─── Networking — Receive ─────────────────────────────────────────────────

        private void HookSocket(ISocket socket)
        {
            socket.ReceivedMatchmakerMatched += OnMatchmakerMatched;
            socket.ReceivedMatchState        += OnMatchState;
            socket.ReceivedMatchPresence     += OnMatchPresence;
        }

        private void UnhookSocket(ISocket socket)
        {
            socket.ReceivedMatchmakerMatched -= OnMatchmakerMatched;
            socket.ReceivedMatchState        -= OnMatchState;
            socket.ReceivedMatchPresence     -= OnMatchPresence;
        }

        private void OnMatchmakerMatched(IMatchmakerMatched matched)
            => _mmTcs?.TrySetResult(matched);

        private void OnMatchPresence(IMatchPresenceEvent e)
        {
            if (e.Leaves == null) return;
            MainThreadDispatcher.Enqueue(() =>
            {
                foreach (var p in e.Leaves)
                    if (p.UserId != _myUserId && !_gameEnded)
                    { _gameEnded = true; ShowGameOver(won: true); }
            });
        }

        private void OnMatchState(IMatchState state)
        {
            if (_match == null || state.MatchId != _match.Id) return;

            string json;
            try { json = Encoding.UTF8.GetString(state.State); } catch { return; }

            if (state.OpCode == M3Op.BoardSync)
            {
                M3BoardSyncMsg msg;
                try { msg = JsonUtility.FromJson<M3BoardSyncMsg>(json); } catch { return; }
                MainThreadDispatcher.Enqueue(() => OnBoardSyncReceived(msg));
            }
            else if (state.OpCode == M3Op.GameOver)
            {
                M3GameOverMsg msg;
                try { msg = JsonUtility.FromJson<M3GameOverMsg>(json); } catch { return; }
                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_gameEnded || _pendingGameOver) return;
                    if (_isSoloBotMode && string.Equals(msg.winnerUserId, _myUserId, StringComparison.Ordinal))
                    {
                        var rxp = Mathf.Max(0, msg.rewardXp);
                        var rgold = Mathf.Max(0, msg.rewardGold);
                        if (rxp > 0 || rgold > 0)
                        {
                            _pveProgress.xp += rxp;
                            _pveProgress.gold += rgold;
                            if (msg.newLevel > 0) _pveProgress.level = msg.newLevel;
                            _lastRewardText = $"+{rxp} опыта\n+{rgold} золота";
                        }
                    }
                    _pendingGameOver = true;
                    _pendingGameOverWon = msg.winnerUserId == _myUserId;
                    TryShowDeferredGameOver();
                });
            }
            else if (state.OpCode == M3Op.PlayerLeft)
            {
                MainThreadDispatcher.Enqueue(() =>
                { if (!_gameEnded) { _gameEnded = true; ShowGameOver(won: true); } });
            }
            else if (state.OpCode == M3Op.ActionReject)
            {
                M3ActionRejectMsg msg;
                try { msg = JsonUtility.FromJson<M3ActionRejectMsg>(json); } catch { return; }
                MainThreadDispatcher.Enqueue(() =>
                {
                    _inputBlocked = false;
                    _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
                    if (!string.IsNullOrEmpty(msg.reason))
                        Debug.Log($"[Match3] Ход отклонён сервером: {msg.reason}");
                });
            }
            else if (state.OpCode == M3Op.SelectionSync)
            {
                if (state.UserPresence?.UserId == _myUserId) return;
                M3SelectionSyncMsg msg;
                try { msg = JsonUtility.FromJson<M3SelectionSyncMsg>(json); } catch { return; }
                MainThreadDispatcher.Enqueue(() => OnRemoteSelection(msg));
            }
        }

        private void OnBoardSyncReceived(M3BoardSyncMsg msg)
        {
            if (_gameEnded) return;
            _hasInitialBoardSync = true;
            if (_snapshotRetryRoutine != null) { StopCoroutine(_snapshotRetryRoutine); _snapshotRetryRoutine = null; }
            _pendingBoardSyncs.Enqueue(msg);
            TryStartNextBoardSync();
        }

        private void TryStartNextBoardSync()
        {
            if (_remoteSyncRoutine != null) return;
            if (_pendingBoardSyncs.Count == 0)
            {
                TryShowDeferredGameOver();
                return;
            }
            var next = _pendingBoardSyncs.Dequeue();
            _remoteSyncRoutine = StartCoroutine(ApplyRemoteBoardSync(next));
        }

        private void TryShowDeferredGameOver()
        {
            if (_gameEnded || !_pendingGameOver) return;
            if (_remoteSyncRoutine != null || _pendingBoardSyncs.Count > 0) return;
            _pendingGameOver = false;
            _gameEnded = true;
            ShowGameOver(_pendingGameOverWon);
        }

        private void OnRemoteSelection(M3SelectionSyncMsg msg)
        {
            if (msg.selected)
            {
                if (_remoteSelX >= 0 && _remoteSelY >= 0)
                    _boardView?.SetCellSelected(_remoteSelX, _remoteSelY, false);
                _remoteSelX = msg.x;
                _remoteSelY = msg.y;
                _boardView?.SetCellSelected(msg.x, msg.y, true);
            }
            else
            {
                _boardView?.SetCellSelected(msg.x, msg.y, false);
                if (_remoteSelX == msg.x && _remoteSelY == msg.y)
                    _remoteSelX = _remoteSelY = -1;
            }
        }

        private IEnumerator ApplyRemoteBoardSync(M3BoardSyncMsg msg)
        {
            var beforeBoard = _board.ToArray();
            int[] currentBoard = beforeBoard;
            int prevMyHp = _myStats.hp;
            int prevOpHp = _opStats.hp;

            if (_boardView != null)
            {
                if (msg.actionType == 1 &&
                    msg.fromX >= 0 && msg.fromY >= 0 && msg.toX >= 0 && msg.toY >= 0)
                {
                    yield return _boardView.AnimateSwap(msg.fromX, msg.fromY, msg.toX, msg.toY, 0.30f);
                    currentBoard = SwapCellsInBoard(currentBoard, msg.fromX, msg.fromY, msg.toX, msg.toY);
                    _board.FromArray(currentBoard);
                    _boardView.RefreshAll(_board);
                }
                else if (msg.actionType == 2 &&
                         msg.abilityX >= 0 && msg.abilityY >= 0)
                {
                    PlaySfx(sfxAbilityCross);
                    yield return _boardView.AnimateAbilityArea(AbilityType.Cross, msg.abilityX, msg.abilityY, 0.24f);
                }
                else if (msg.actionType == 3 &&
                         msg.abilityX >= 0 && msg.abilityY >= 0)
                {
                    PlaySfx(sfxAbilitySquare);
                    yield return _boardView.AnimateAbilityArea(AbilityType.Square, msg.abilityX, msg.abilityY, 0.24f);
                }
                else if (msg.actionType == 4)
                {
                    PlaySfx(sfxAbilityPetard != null ? sfxAbilityPetard : sfxAbilitySquare);
                }
                else if (msg.actionType == 5)
                {
                    _boardView.ShowCenterAnnouncement("Щит", new Color(0.65f, 0.85f, 1f), 1.2f);
                }
                else if (msg.actionType == 6)
                {
                    _boardView.ShowCenterAnnouncement("Ярость", new Color(1f, 0.55f, 0.25f), 1.2f);
                }
            }

            bool usedAnimSteps = msg.animSteps != null && msg.animSteps.Count > 0;
            if (usedAnimSteps && _boardView != null)
            {
                for (int i = 0; i < msg.animSteps.Count; i++)
                {
                    var step = msg.animSteps[i];
                    if (step?.board == null || step.board.Length < Match3BoardLogic.Size * Match3BoardLogic.Size)
                        continue;

                    if (step.phase == 1)
                    {
                        PlaySfx(sfxLineClear);
                        yield return _boardView.AnimateClearByBoardDiff(currentBoard, step.board, 0.125f);
                        _board.FromArray(step.board);
                        _boardView.RefreshAll(_board);
                        currentBoard = _board.ToArray();
                    }
                    else if (step.phase == 2)
                    {
                        _board.FromArray(step.board);
                        _boardView.RefreshAll(_board);
                        PlaySfx(sfxCascadeFall);
                        yield return _boardView.AnimateDrop(currentBoard, _board, 0.25f);
                        currentBoard = _board.ToArray();
                    }
                }
            }

            _board.FromArray(msg.board);

            var ids  = GetSortedIds();
            bool amA = ids.Count > 0 && _myUserId == ids[0];

            int capA = msg.aMaxHp > 0 ? msg.aMaxHp : MaxHp;
            int capB = msg.bMaxHp > 0 ? msg.bMaxHp : MaxHp;
            _myStats.maxHp          = amA ? capA : capB;
            _opStats.maxHp          = amA ? capB : capA;
            _myStats.hp            = amA ? msg.aHp       : msg.bHp;
            _myStats.mana          = amA ? msg.aMana      : msg.bMana;
            _myStats.crossCooldown  = amA ? msg.aCrossCd  : msg.bCrossCd;
            _myStats.squareCooldown = amA ? msg.aSquareCd : msg.bSquareCd;
            _myStats.petardCooldown = amA ? msg.aPetardCd : msg.bPetardCd;
            _myStats.shieldCooldown = amA ? msg.aShieldCd : msg.bShieldCd;
            _myStats.furyCooldown   = amA ? msg.aFuryCd   : msg.bFuryCd;
            _myStats.shieldT1 = amA ? msg.aShieldT1 : msg.bShieldT1;
            _myStats.shieldT2 = amA ? msg.aShieldT2 : msg.bShieldT2;
            _myStats.shieldT3 = amA ? msg.aShieldT3 : msg.bShieldT3;
            _myStats.furyTurnsRemaining = amA ? msg.aFuryTurns : msg.bFuryTurns;
            _myStats.furyDamageBonus    = amA ? msg.aFuryBonus : msg.bFuryBonus;
            _opStats.hp             = amA ? msg.bHp       : msg.aHp;
            _opStats.mana           = amA ? msg.bMana      : msg.aMana;
            _opStats.crossCooldown  = amA ? msg.bCrossCd  : msg.aCrossCd;
            _opStats.squareCooldown = amA ? msg.bSquareCd : msg.aSquareCd;
            _opStats.petardCooldown = amA ? msg.bPetardCd : msg.aPetardCd;
            _opStats.shieldCooldown = amA ? msg.bShieldCd : msg.aShieldCd;
            _opStats.furyCooldown   = amA ? msg.bFuryCd   : msg.aFuryCd;
            _opStats.shieldT1 = amA ? msg.bShieldT1 : msg.aShieldT1;
            _opStats.shieldT2 = amA ? msg.bShieldT2 : msg.aShieldT2;
            _opStats.shieldT3 = amA ? msg.bShieldT3 : msg.aShieldT3;
            _opStats.furyTurnsRemaining = amA ? msg.bFuryTurns : msg.aFuryTurns;
            _opStats.furyDamageBonus    = amA ? msg.bFuryBonus : msg.aFuryBonus;

            RecalcDerivedBuffs(_myStats);
            RecalcDerivedBuffs(_opStats);

            _boardView?.RefreshAll(_board);
            if (!usedAnimSteps && _boardView != null)
                yield return _boardView.AnimateBoardTransition(beforeBoard, _board, 0.45f);
            RefreshStatsUI();

            if (msg.critTriggered && _boardView != null)
                _boardView.ShowCenterAnnouncement("Критический урон!", new Color(1f, 0.8f, 0.25f), 1.3f);

            // Damage popups (computed by HP delta). Both clients see it on the damaged side.
            var myDamageTaken = Mathf.Max(0, prevMyHp - _myStats.hp);
            var opDamageTaken = Mathf.Max(0, prevOpHp - _opStats.hp);
            if (myDamageTaken > 0 && _myPanel != null)
                _myPanel.ShowDamagePopup(myDamageTaken, msg.critTriggered && opDamageTaken == 0);
            if (opDamageTaken > 0 && _opPanel != null)
                _opPanel.ShowDamagePopup(opDamageTaken, msg.critTriggered && myDamageTaken == 0);

            if (_myStats.hp < prevMyHp || _opStats.hp < prevOpHp)
            {
                if (msg.critTriggered) PlaySfx(sfxDamageCrit);
                else                   PlaySfx(sfxDamageHit);
            }
            if (msg.extraTurn)
            {
                PlaySfx(sfxExtraTurn);
                _boardView?.ShowCenterAnnouncement("Дополнительный ход\nза 5+ камней", new Color(0.35f, 1f, 0.35f), 2f);
            }

            bool petardKeepsTurn = msg.actionType == 4 && ((msg.activeUserId == _myUserId) == _isMyTurn);
            if (petardKeepsTurn)
            {
                _inputBlocked = !_isMyTurn;
                _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
                if (_useLocalBotSimulation && !_isMyTurn && !_gameEnded)
                {
                    if (_botTurnRoutine != null) StopCoroutine(_botTurnRoutine);
                    _botTurnRoutine = StartCoroutine(BotTurnRoutine());
                }
                _remoteSyncRoutine = null;
                TryStartNextBoardSync();
                yield break;
            }

            bool isMyTurnNow = msg.activeUserId == _myUserId;
            if (isMyTurnNow) BeginMyTurn();
            else             BeginOpponentTurn();

            _remoteSyncRoutine = null;
            TryStartNextBoardSync();
        }

        private IEnumerator BotTurnRoutine()
        {
            _botTurnRoutine = null;
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.85f, 1.45f));
            if (_gameEnded || !_isSoloBotMode || _isMyTurn) yield break;

            var action = BuildBotAction();
            if (action == null)
            {
                AdvanceTurnWithoutAction(_opUserId);
                yield break;
            }

            HandleLocalActionRequest(action, _opUserId);
        }

        private M3ActionRequest BuildBotAction()
        {
            var availableAbilities = new List<AbilityType>(5);
            if (CanUseAbility(_opStats, AbilityType.Petard)) availableAbilities.Add(AbilityType.Petard);
            if (CanUseAbility(_opStats, AbilityType.Cross)) availableAbilities.Add(AbilityType.Cross);
            if (CanUseAbility(_opStats, AbilityType.Square)) availableAbilities.Add(AbilityType.Square);
            if (CanUseAbility(_opStats, AbilityType.Shield)) availableAbilities.Add(AbilityType.Shield);
            if (CanUseAbility(_opStats, AbilityType.Fury))   availableAbilities.Add(AbilityType.Fury);

            if (availableAbilities.Count > 0 && _botRandom.NextDouble() < 0.35d)
            {
                var ability = availableAbilities[_botRandom.Next(availableAbilities.Count)];
                return ability switch
                {
                    AbilityType.Petard => new M3ActionRequest
                    {
                        actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1,
                    },
                    AbilityType.Cross => new M3ActionRequest
                    {
                        actionType = 2, fromX = -1, fromY = -1, toX = -1, toY = -1,
                        cx = _botRandom.Next(0, Match3BoardLogic.Size),
                        cy = _botRandom.Next(0, Match3BoardLogic.Size),
                    },
                    AbilityType.Square => new M3ActionRequest
                    {
                        actionType = 3, fromX = -1, fromY = -1, toX = -1, toY = -1,
                        cx = _botRandom.Next(0, Match3BoardLogic.Size),
                        cy = _botRandom.Next(0, Match3BoardLogic.Size),
                    },
                    AbilityType.Shield => new M3ActionRequest
                    {
                        actionType = 5, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1,
                    },
                    _ => new M3ActionRequest
                    {
                        actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1,
                    },
                };
            }

            var swaps = EnumerateValidSwaps();
            if (swaps.Count == 0) return null;
            var pick = swaps[_botRandom.Next(swaps.Count)];
            return new M3ActionRequest
            {
                actionType = 1,
                fromX = pick.fromX,
                fromY = pick.fromY,
                toX = pick.toX,
                toY = pick.toY,
                cx = -1,
                cy = -1,
            };
        }

        private List<(int fromX, int fromY, int toX, int toY)> EnumerateValidSwaps()
        {
            var list = new List<(int fromX, int fromY, int toX, int toY)>();
            var snapshot = _board.ToArray();
            for (var y = 0; y < Match3BoardLogic.Size; y++)
            {
                for (var x = 0; x < Match3BoardLogic.Size; x++)
                {
                    if (x + 1 < Match3BoardLogic.Size && IsSwapValid(snapshot, x, y, x + 1, y))
                        list.Add((x, y, x + 1, y));
                    if (y + 1 < Match3BoardLogic.Size && IsSwapValid(snapshot, x, y, x, y + 1))
                        list.Add((x, y, x, y + 1));
                }
            }
            return list;
        }

        private bool IsSwapValid(int[] boardArray, int x1, int y1, int x2, int y2)
        {
            var sim = new Match3BoardLogic();
            sim.FromArray(boardArray);
            return sim.TrySwap(x1, y1, x2, y2, out _);
        }

        private void HandleLocalActionRequest(M3ActionRequest req, string actorId)
        {
            if (!_isSoloBotMode || _gameEnded || req == null) return;
            if (!_hasInitialBoardSync) return;
            if (!string.Equals(actorId, _myUserId, StringComparison.Ordinal) &&
                !string.Equals(actorId, _opUserId, StringComparison.Ordinal))
                return;
            if (!string.Equals(actorId, GetActiveUserId(), StringComparison.Ordinal)) return;

            var actorStats = string.Equals(actorId, _myUserId, StringComparison.Ordinal) ? _myStats : _opStats;
            var oppStats = ReferenceEquals(actorStats, _myStats) ? _opStats : _myStats;
            if (!ValidateLocalAction(req, actorStats)) return;
            var beforeBoard = _board.ToArray();
            var simBoard = new Match3BoardLogic();
            simBoard.FromArray(beforeBoard);

            var msg = new M3BoardSyncMsg();
            msg.actionType = req.actionType;
            msg.fromX = req.fromX;
            msg.fromY = req.fromY;
            msg.toX = req.toX;
            msg.toY = req.toY;
            msg.abilityX = req.cx;
            msg.abilityY = req.cy;

            bool keepTurn;
            bool extraTurn;
            var actionApplied = ResolveLocalAction(simBoard, req, actorStats, oppStats, msg, out extraTurn, out keepTurn);
            if (!actionApplied)
            {
                if (string.Equals(actorId, _myUserId, StringComparison.Ordinal))
                {
                    _inputBlocked = false;
                    _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost, ShieldAbilityCost, FuryAbilityCost);
                }
                return;
            }

            if (actorStats.hp <= 0 || oppStats.hp <= 0)
            {
                var winnerUserId = actorStats.hp > 0 ? actorId : OpponentOf(actorId);
                _gameEnded = true;
                msg.activeUserId = winnerUserId;
                FillSyncStats(msg);
                msg.board = simBoard.ToArray();
                OnBoardSyncReceived(msg);
                ShowGameOver(string.Equals(winnerUserId, _myUserId, StringComparison.Ordinal));
                return;
            }

            if (!keepTurn)
            {
                if (!extraTurn)
                {
                    SwitchActiveUser();
                    TickEndOfTurnForActive();
                }
                else
                {
                    TickEndOfTurnForActive();
                }
            }

            msg.extraTurn = extraTurn;
            msg.activeUserId = GetActiveUserId();
            msg.board = simBoard.ToArray();
            FillSyncStats(msg);
            OnBoardSyncReceived(msg);
        }

        private bool ValidateLocalAction(M3ActionRequest req, PlayerStats actorStats)
        {
            if (req.actionType == 1)
            {
                if (!InBounds(req.fromX, req.fromY) || !InBounds(req.toX, req.toY)) return false;
                return Mathf.Abs(req.fromX - req.toX) + Mathf.Abs(req.fromY - req.toY) == 1;
            }

            if (req.actionType == 2)
            {
                return InBounds(req.cx, req.cy) &&
                       actorStats.mana >= CrossAbilityCost &&
                       actorStats.crossCooldown <= 0;
            }
            if (req.actionType == 3)
            {
                return InBounds(req.cx, req.cy) &&
                       actorStats.mana >= SquareAbilityCost &&
                       actorStats.squareCooldown <= 0;
            }
            if (req.actionType == 4)
            {
                return actorStats.mana >= PetardAbilityCost &&
                       actorStats.petardCooldown <= 0;
            }
            if (req.actionType == 5)
            {
                return actorStats.mana >= ShieldAbilityCost &&
                       actorStats.shieldCooldown <= 0;
            }
            if (req.actionType == 6)
            {
                return actorStats.mana >= FuryAbilityCost &&
                       actorStats.furyCooldown <= 0;
            }
            return false;
        }

        private bool ResolveLocalAction(
            Match3BoardLogic board,
            M3ActionRequest req,
            PlayerStats actorStats,
            PlayerStats oppStats,
            M3BoardSyncMsg msg,
            out bool extraTurn,
            out bool keepTurn)
        {
            extraTurn = false;
            keepTurn = false;
            msg.animSteps = new List<M3AnimStep>();

            if (req.actionType == 1)
            {
                if (!board.TrySwap(req.fromX, req.fromY, req.toX, req.toY, out var initialMatches))
                    return false;

                if (initialMatches != null && initialMatches.Count > 0)
                {
                    extraTurn = ApplyMatchEffects(board, actorStats, oppStats, initialMatches, extraTurn);
                    board.ClearMatchedCells(initialMatches);
                    msg.animSteps.Add(new M3AnimStep { phase = 1, board = board.ToArray() });
                }

                ResolveCascades(board, actorStats, oppStats, msg, ref extraTurn);
                return true;
            }

            if (req.actionType == 2 || req.actionType == 3 || req.actionType == 4 || req.actionType == 5 || req.actionType == 6)
            {
                SpendAbility(actorStats, req.actionType);

                if (req.actionType == 4)
                {
                    var crit = DealDamage(board, actorStats, oppStats, PetardDamage);
                    if (crit && _boardView != null) _boardView.ShowCenterAnnouncement("Критический урон!", new Color(1f, 0.8f, 0.25f), 1.3f);
                    keepTurn = true;
                    return true;
                }

                if (req.actionType == 5)
                {
                    ApplyShield(actorStats);
                    if (_boardView != null) _boardView.ShowCenterAnnouncement("Щит", new Color(0.65f, 0.85f, 1f), 1.2f);
                    keepTurn = true;
                    return true;
                }

                if (req.actionType == 6)
                {
                    ApplyFury(actorStats);
                    keepTurn = true;
                    if (_boardView != null) _boardView.ShowCenterAnnouncement("Ярость", new Color(1f, 0.55f, 0.25f), 1.2f);
                    return true;
                }

                ApplyAbilityRewards(board, req.actionType, req.cx, req.cy, actorStats, oppStats);
                var ability = req.actionType == 2 ? AbilityType.Cross : AbilityType.Square;
                board.ApplyAbility(ability, req.cx, req.cy);
                msg.animSteps.Add(new M3AnimStep { phase = 1, board = board.ToArray() });
                ResolveCascades(board, actorStats, oppStats, msg, ref extraTurn);
                return true;
            }

            return false;
        }

        private void ResolveCascades(Match3BoardLogic board, PlayerStats actorStats, PlayerStats oppStats, M3BoardSyncMsg msg, ref bool extraTurn)
        {
            while (true)
            {
                var cascade = board.ApplyGravityAndRefill();
                msg.animSteps.Add(new M3AnimStep { phase = 2, board = board.ToArray() });
                if (cascade == null || cascade.Count == 0) break;

                extraTurn = ApplyMatchEffects(board, actorStats, oppStats, cascade, extraTurn);
                board.ClearMatchedCells(cascade);
                msg.animSteps.Add(new M3AnimStep { phase = 1, board = board.ToArray() });
            }
        }

        private bool ApplyMatchEffects(Match3BoardLogic board, PlayerStats actorStats, PlayerStats oppStats, List<MatchResult> matches, bool extraTurn)
        {
            var healedAnyCrossThisTurn = false;
            var pendingHeal = 0;
            foreach (var match in matches)
            {
                if (match.count >= 5) extraTurn = true;
                if (match.type == PieceType.GemRed || match.type == PieceType.GemYellow || match.type == PieceType.GemGreen)
                {
                    actorStats.mana = Mathf.Min(MaxMana, actorStats.mana + GetManaByGemType(match.type) * match.count);
                }
                else if (match.type == PieceType.Skull)
                {
                    var crit = DealDamage(board, actorStats, oppStats, SkullDamage * match.count);
                    if (crit && _boardView != null) _boardView.ShowCenterAnnouncement("Критический урон!", new Color(1f, 0.8f, 0.25f), 1.3f);
                }
                else if (match.type == PieceType.Ankh)
                {
                    pendingHeal += AnkhHeal * match.count;
                    healedAnyCrossThisTurn = true;
                }
            }

            if (healedAnyCrossThisTurn)
                pendingHeal += GetShieldHealBonus(actorStats);

            if (pendingHeal > 0)
                actorStats.hp = Mathf.Min(EffectiveMaxHp(actorStats), actorStats.hp + pendingHeal);

            return extraTurn;
        }

        private void ApplyAbilityRewards(Match3BoardLogic board, int actionType, int cx, int cy, PlayerStats actorStats, PlayerStats oppStats)
        {
            var skulls = 0;
            var healedAnyCrossThisTurn = false;
            var pendingHeal = 0;
            var cells = CollectAbilityCells(actionType, cx, cy);
            foreach (var (x, y) in cells)
            {
                var type = board[x, y];
                if (type == PieceType.GemRed || type == PieceType.GemYellow || type == PieceType.GemGreen)
                    actorStats.mana = Mathf.Min(MaxMana, actorStats.mana + GetManaByGemType(type));
                else if (type == PieceType.Ankh)
                {
                    pendingHeal += AnkhHeal;
                    healedAnyCrossThisTurn = true;
                }
                else if (type == PieceType.Skull)
                    skulls++;
            }

            if (healedAnyCrossThisTurn)
                pendingHeal += GetShieldHealBonus(actorStats);
            if (pendingHeal > 0)
                actorStats.hp = Mathf.Min(EffectiveMaxHp(actorStats), actorStats.hp + pendingHeal);

            {
                var crit = DealDamage(board, actorStats, oppStats, AbilityBaseDamage + SkullDamage * skulls);
                if (crit && _boardView != null) _boardView.ShowCenterAnnouncement("Критический урон!", new Color(1f, 0.8f, 0.25f), 1.3f);
            }
        }

        private List<(int x, int y)> CollectAbilityCells(int actionType, int cx, int cy)
        {
            var cells = new List<(int x, int y)>(12);
            var used = new HashSet<int>();
            void Add(int x, int y)
            {
                if (!InBounds(x, y)) return;
                var key = y * Match3BoardLogic.Size + x;
                if (!used.Add(key)) return;
                cells.Add((x, y));
            }

            if (actionType == 2)
            {
                for (var dx = -2; dx <= 2; dx++) Add(cx + dx, cy);
                for (var dy = -2; dy <= 2; dy++) Add(cx, cy + dy);
            }
            else if (actionType == 3)
            {
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        Add(cx + dx, cy + dy);
            }
            else if (actionType == 4)
            {
                Add(cx, cy);
            }
            return cells;
        }

        private static int GetManaByGemType(PieceType type)
        {
            return type switch
            {
                PieceType.GemRed => 5,
                PieceType.GemYellow => 3,
                PieceType.GemGreen => 1,
                _ => 0,
            };
        }

        private static bool CanUseAbility(PlayerStats stats, AbilityType ability)
        {
            return ability switch
            {
                AbilityType.Petard => stats.mana >= PetardAbilityCost && stats.petardCooldown <= 0,
                AbilityType.Cross => stats.mana >= CrossAbilityCost && stats.crossCooldown <= 0,
                AbilityType.Square => stats.mana >= SquareAbilityCost && stats.squareCooldown <= 0,
                AbilityType.Shield => stats.mana >= ShieldAbilityCost && stats.shieldCooldown <= 0,
                AbilityType.Fury => stats.mana >= FuryAbilityCost && stats.furyCooldown <= 0,
                _ => false,
            };
        }

        private static bool InBounds(int x, int y)
        {
            return x >= 0 && x < Match3BoardLogic.Size && y >= 0 && y < Match3BoardLogic.Size;
        }

        private void SpendAbility(PlayerStats stats, int actionType)
        {
            if (actionType == 2)
            {
                stats.mana = Mathf.Max(0, stats.mana - CrossAbilityCost);
                stats.crossCooldown = CrossCooldownTurns;
            }
            else if (actionType == 3)
            {
                stats.mana = Mathf.Max(0, stats.mana - SquareAbilityCost);
                stats.squareCooldown = SquareCooldownTurns;
            }
            else if (actionType == 4)
            {
                stats.mana = Mathf.Max(0, stats.mana - PetardAbilityCost);
                stats.petardCooldown = PetardCooldownTurns;
            }
            else if (actionType == 5)
            {
                stats.mana = Mathf.Max(0, stats.mana - ShieldAbilityCost);
                stats.shieldCooldown = ShieldCooldownTurns;
            }
            else if (actionType == 6)
            {
                stats.mana = Mathf.Max(0, stats.mana - FuryAbilityCost);
                stats.furyCooldown = FuryCooldownTurns;
            }
        }

        private void TickEndOfTurnForActive()
        {
            var active = GetActiveStats();
            if (active == null) return;
            if (active.crossCooldown > 0) active.crossCooldown--;
            if (active.squareCooldown > 0) active.squareCooldown--;
            if (active.petardCooldown > 0) active.petardCooldown--;
            if (active.shieldCooldown > 0) active.shieldCooldown--;
            if (active.furyCooldown > 0) active.furyCooldown--;
            TickBuffDurations(active);
            RecalcDerivedBuffs(active);
        }

        private void FillSyncStats(M3BoardSyncMsg msg)
        {
            var ids = GetSortedIds();
            if (ids.Count < 2)
            {
                msg.aHp = _myStats.hp;
                msg.aMaxHp = EffectiveMaxHp(_myStats);
                msg.aMana = _myStats.mana;
                msg.aCrossCd = _myStats.crossCooldown;
                msg.aSquareCd = _myStats.squareCooldown;
                msg.aPetardCd = _myStats.petardCooldown;
                msg.aShieldCd = _myStats.shieldCooldown;
                msg.aFuryCd = _myStats.furyCooldown;
                msg.aShieldT1 = _myStats.shieldT1;
                msg.aShieldT2 = _myStats.shieldT2;
                msg.aShieldT3 = _myStats.shieldT3;
                msg.aFuryTurns = _myStats.furyTurnsRemaining;
                msg.aFuryBonus = _myStats.furyDamageBonus;
                msg.bHp = _opStats.hp;
                msg.bMaxHp = EffectiveMaxHp(_opStats);
                msg.bMana = _opStats.mana;
                msg.bCrossCd = _opStats.crossCooldown;
                msg.bSquareCd = _opStats.squareCooldown;
                msg.bPetardCd = _opStats.petardCooldown;
                msg.bShieldCd = _opStats.shieldCooldown;
                msg.bFuryCd = _opStats.furyCooldown;
                msg.bShieldT1 = _opStats.shieldT1;
                msg.bShieldT2 = _opStats.shieldT2;
                msg.bShieldT3 = _opStats.shieldT3;
                msg.bFuryTurns = _opStats.furyTurnsRemaining;
                msg.bFuryBonus = _opStats.furyDamageBonus;
                return;
            }

            bool myFirst = string.Equals(_myUserId, ids[0], StringComparison.Ordinal);
            var first = myFirst ? _myStats : _opStats;
            var second = myFirst ? _opStats : _myStats;
            msg.aHp = first.hp;
            msg.aMaxHp = EffectiveMaxHp(first);
            msg.aMana = first.mana;
            msg.aCrossCd = first.crossCooldown;
            msg.aSquareCd = first.squareCooldown;
            msg.aPetardCd = first.petardCooldown;
            msg.aShieldCd = first.shieldCooldown;
            msg.aFuryCd = first.furyCooldown;
            msg.aShieldT1 = first.shieldT1;
            msg.aShieldT2 = first.shieldT2;
            msg.aShieldT3 = first.shieldT3;
            msg.aFuryTurns = first.furyTurnsRemaining;
            msg.aFuryBonus = first.furyDamageBonus;
            msg.bHp = second.hp;
            msg.bMaxHp = EffectiveMaxHp(second);
            msg.bMana = second.mana;
            msg.bCrossCd = second.crossCooldown;
            msg.bSquareCd = second.squareCooldown;
            msg.bPetardCd = second.petardCooldown;
            msg.bShieldCd = second.shieldCooldown;
            msg.bFuryCd = second.furyCooldown;
            msg.bShieldT1 = second.shieldT1;
            msg.bShieldT2 = second.shieldT2;
            msg.bShieldT3 = second.shieldT3;
            msg.bFuryTurns = second.furyTurnsRemaining;
            msg.bFuryBonus = second.furyDamageBonus;
        }

        private void AdvanceTurnWithoutAction(string actorId)
        {
            if (!string.Equals(actorId, GetActiveUserId(), StringComparison.Ordinal)) return;
            SwitchActiveUser();
            TickEndOfTurnForActive();
            var msg = new M3BoardSyncMsg
            {
                actionType = 0,
                fromX = -1,
                fromY = -1,
                toX = -1,
                toY = -1,
                abilityX = -1,
                abilityY = -1,
                activeUserId = GetActiveUserId(),
                extraTurn = false,
                board = _board.ToArray(),
            };
            FillSyncStats(msg);
            OnBoardSyncReceived(msg);
        }

        private string GetActiveUserId() => _isMyTurn ? _myUserId : _opUserId;
        private PlayerStats GetActiveStats() => _isMyTurn ? _myStats : _opStats;
        private string OpponentOf(string userId) =>
            string.Equals(userId, _myUserId, StringComparison.Ordinal) ? _opUserId : _myUserId;

        private void SwitchActiveUser()
        {
            _isMyTurn = !_isMyTurn;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private List<string> GetSortedIds()
        {
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(_myUserId)) ids.Add(_myUserId);
            if (!string.IsNullOrEmpty(_opUserId)) ids.Add(_opUserId);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private void RefreshStatsUI()
        {
            _myPanel?.UpdateStats(_myStats.hp, EffectiveMaxHp(_myStats), _myStats.mana, MaxMana);
            _opPanel?.UpdateStats(_opStats.hp, EffectiveMaxHp(_opStats), _opStats.mana, MaxMana);
            RefreshCombatStatsUI();
        }

        private void RefreshCombatStatsUI()
        {
            if (_myPanel != null)
            {
                var myDamage = _myStats.furyTurnsRemaining > 0 ? CountSkulls(_board) : 0;
                _myPanel.UpdateCombatStats(myDamage, GetArmor(_myStats), GetShieldHealBonus(_myStats),
                    _myStats.furyTurnsRemaining > 0 ? Mathf.RoundToInt(furyCritChance * 100f) : 0);
                _myPanel.UpdateBuffState(GetShieldStacks(_myStats), Mathf.Max(_myStats.shieldT1, Mathf.Max(_myStats.shieldT2, _myStats.shieldT3)));
            }

            if (_opPanel != null)
            {
                var opDamage = _opStats.furyTurnsRemaining > 0 ? CountSkulls(_board) : 0;
                _opPanel.UpdateCombatStats(opDamage, GetArmor(_opStats), GetShieldHealBonus(_opStats),
                    _opStats.furyTurnsRemaining > 0 ? Mathf.RoundToInt(furyCritChance * 100f) : 0);
                _opPanel.UpdateBuffState(GetShieldStacks(_opStats), Mathf.Max(_opStats.shieldT1, Mathf.Max(_opStats.shieldT2, _opStats.shieldT3)));
            }
        }

        private static int EffectiveMaxHp(PlayerStats s) => s != null && s.maxHp > 0 ? s.maxHp : MaxHp;

        private static int GetShieldStacks(PlayerStats s)
        {
            if (s == null) return 0;
            var c = 0;
            if (s.shieldT1 > 0) c++;
            if (s.shieldT2 > 0) c++;
            if (s.shieldT3 > 0) c++;
            return c;
        }

        private static int CountSkulls(Match3BoardLogic board)
        {
            if (board == null) return 0;
            var skulls = 0;
            for (var y = 0; y < Match3BoardLogic.Size; y++)
            for (var x = 0; x < Match3BoardLogic.Size; x++)
                if (board[x, y] == PieceType.Skull) skulls++;
            return skulls;
        }

        private static int GetArmor(PlayerStats s) => GetShieldStacks(s) * ShieldArmorPerStack;
        private static int GetShieldHealBonus(PlayerStats s) => GetShieldStacks(s) * ShieldHealPerStack;

        private static void RecalcDerivedBuffs(PlayerStats s)
        {
            if (s == null) return;
            if (s.shieldT1 < 0) s.shieldT1 = 0;
            if (s.shieldT2 < 0) s.shieldT2 = 0;
            if (s.shieldT3 < 0) s.shieldT3 = 0;
            if (s.furyTurnsRemaining < 0) s.furyTurnsRemaining = 0;
            if (s.furyTurnsRemaining == 0) s.furyDamageBonus = 0;
        }

        private static void TickBuffDurations(PlayerStats s)
        {
            if (s == null) return;
            if (s.shieldT1 > 0) s.shieldT1--;
            if (s.shieldT2 > 0) s.shieldT2--;
            if (s.shieldT3 > 0) s.shieldT3--;
            if (s.furyTurnsRemaining > 0) s.furyTurnsRemaining--;
            if (s.furyTurnsRemaining <= 0)
            {
                s.furyTurnsRemaining = 0;
                s.furyDamageBonus = 0;
            }
        }

        private static void ApplyShield(PlayerStats actor)
        {
            if (actor == null) return;

            // Add stack up to 3.
            if (actor.shieldT1 <= 0) actor.shieldT1 = ShieldDurationTurns;
            else if (actor.shieldT2 <= 0) actor.shieldT2 = ShieldDurationTurns;
            else if (actor.shieldT3 <= 0) actor.shieldT3 = ShieldDurationTurns;

            // Refresh duration for ALL existing stacks on every cast.
            if (actor.shieldT1 > 0) actor.shieldT1 = ShieldDurationTurns;
            if (actor.shieldT2 > 0) actor.shieldT2 = ShieldDurationTurns;
            if (actor.shieldT3 > 0) actor.shieldT3 = ShieldDurationTurns;

            RecalcDerivedBuffs(actor);
        }

        private static void ApplyFury(PlayerStats actor)
        {
            if (actor == null) return;
            // Fury is a 1-turn buff that applies during the turn of activation.
            actor.furyTurnsRemaining = 1;
            actor.furyDamageBonus = 0;
            RecalcDerivedBuffs(actor);
        }

        private int RollOutgoingDamage(Match3BoardLogic board, PlayerStats attacker, int baseDamage, out bool critTriggered)
        {
            critTriggered = false;
            var dmg = Mathf.Max(0, baseDamage);
            if (attacker != null && attacker.furyTurnsRemaining > 0)
            {
                dmg += CountSkulls(board);
                if (UnityEngine.Random.value < Mathf.Clamp01(furyCritChance))
                {
                    dmg *= 2;
                    critTriggered = true;
                }
            }
            return dmg;
        }

        private bool DealDamage(Match3BoardLogic board, PlayerStats attacker, PlayerStats target, int baseDamage)
        {
            if (target == null) return false;
            var raw = RollOutgoingDamage(board, attacker, baseDamage, out var critTriggered);
            var reduced = Mathf.Max(0, raw - GetArmor(target));
            target.hp = Mathf.Max(0, target.hp - reduced);
            return critTriggered;
        }

        private static int[] SwapCellsInBoard(int[] board, int x1, int y1, int x2, int y2)
        {
            if (board == null || board.Length < Match3BoardLogic.Size * Match3BoardLogic.Size)
                return board;
            var clone = new int[board.Length];
            Array.Copy(board, clone, board.Length);
            int i1 = y1 * Match3BoardLogic.Size + x1;
            int i2 = y2 * Match3BoardLogic.Size + x2;
            int t = clone[i1];
            clone[i1] = clone[i2];
            clone[i2] = t;
            return clone;
        }

        private void EnsureAudioSource()
        {
            if (sfxSource != null) return;
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        private void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            EnsureAudioSource();
            if (sfxSource == null) return;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void TryAutoAssignSfxInEditor()
        {
#if UNITY_EDITOR
            if (sfxLineClear == null)    sfxLineClear    = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_line_clear.wav");
            if (sfxAbilitySquare == null) sfxAbilitySquare = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_ability_square.wav");
            if (sfxAbilityCross == null) sfxAbilityCross = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_ability_cross.wav");
            if (sfxAbilityPetard == null) sfxAbilityPetard = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_ability_square.wav");
            if (sfxCascadeFall == null)  sfxCascadeFall  = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_cascade_fall.wav");
            if (sfxExtraTurn == null)    sfxExtraTurn    = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_extra_turn.wav");
            if (sfxTimerEnd == null)     sfxTimerEnd     = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_timer_end.wav");
            if (sfxDamageHit == null)    sfxDamageHit    = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_damage_hit.wav");
            if (sfxDamageCrit == null)   sfxDamageCrit   = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_damage_crit.wav");
            if (sfxVictory == null)      sfxVictory      = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_victory.wav");
            if (sfxDefeat == null)       sfxDefeat       = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_defeat.wav");
#endif
        }

        private void TryAutoAssignAbilitySpritesInEditor()
        {
#if UNITY_EDITOR
            if (spellBorderSprite == null) spellBorderSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/border_spell.png");
            if (petardAbilitySprite == null) petardAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/petarda.png");
            if (crossAbilitySprite == null) crossAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/cross.png");
            if (squareAbilitySprite == null) squareAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/square.png");
            if (shieldAbilitySprite == null) shieldAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/shield.png");
            if (furyAbilitySprite == null) furyAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/fury.png");
#endif
        }

        private void TryAutoAssignUiPrefabsInEditor()
        {
#if UNITY_EDITOR
            if (damagePopupPrefab == null)
                damagePopupPrefab = AssetDatabase.LoadAssetAtPath<DamagePopupView>("Assets/_Project/Prefabs/UI/DamagePopup.prefab");
#endif
        }

        private void ConfigureAbilityButtonsVisuals()
        {
            if (_abilityPanel == null) return;
            EnsureAllAbilityButtonsExist();
            ConfigureAbilityButtonVisual(_abilityPanel.petardButton, spellBorderSprite, petardAbilitySprite);
            ConfigureAbilityButtonVisual(_abilityPanel.crossButton,  spellBorderSprite, crossAbilitySprite);
            ConfigureAbilityButtonVisual(_abilityPanel.squareButton, spellBorderSprite, squareAbilitySprite);
            ConfigureAbilityButtonVisual(_abilityPanel.shieldButton, spellBorderSprite, shieldAbilitySprite);
            ConfigureAbilityButtonVisual(_abilityPanel.furyButton,   spellBorderSprite, furyAbilitySprite);
        }

        private void EnsureAllAbilityButtonsExist()
        {
            if (_abilityPanel == null) return;
            var panelRt = _abilityPanel.transform as RectTransform;
            if (panelRt == null) return;

            // Layout 5 abilities in one row.
            const float x0 = 0.02f;
            const float w  = 0.18f;
            const float g  = 0.015f;
            Vector2 AMin(int i) => V2(x0 + i * (w + g), 0.18f);
            Vector2 AMax(int i) => V2(x0 + i * (w + g) + w, 0.92f);

            if (_abilityPanel.petardButton == null)
            {
                _abilityPanel.petardButton = MakeButton(panelRt, "PetardBtn", string.Empty, Color.white, Color.white, AMin(0), AMax(0));
                _abilityPanel.petardCooldownText = MakeTxt(_abilityPanel.petardButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
                _abilityPanel.petardCooldownText.gameObject.SetActive(false);
            }
            if (_abilityPanel.crossButton == null)
            {
                _abilityPanel.crossButton = MakeButton(panelRt, "CrossBtn", string.Empty, Color.white, Color.white, AMin(1), AMax(1));
                _abilityPanel.crossCooldownText = MakeTxt(_abilityPanel.crossButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
                _abilityPanel.crossCooldownText.gameObject.SetActive(false);
            }
            if (_abilityPanel.squareButton == null)
            {
                _abilityPanel.squareButton = MakeButton(panelRt, "SquareBtn", string.Empty, Color.white, Color.white, AMin(2), AMax(2));
                _abilityPanel.squareCooldownText = MakeTxt(_abilityPanel.squareButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
                _abilityPanel.squareCooldownText.gameObject.SetActive(false);
            }
            if (_abilityPanel.shieldButton == null)
            {
                _abilityPanel.shieldButton = MakeButton(panelRt, "ShieldBtn", string.Empty, Color.white, Color.white, AMin(3), AMax(3));
                _abilityPanel.shieldCooldownText = MakeTxt(_abilityPanel.shieldButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
                _abilityPanel.shieldCooldownText.gameObject.SetActive(false);
            }
            if (_abilityPanel.furyButton == null)
            {
                _abilityPanel.furyButton = MakeButton(panelRt, "FuryBtn", string.Empty, Color.white, Color.white, AMin(4), AMax(4));
                _abilityPanel.furyCooldownText = MakeTxt(_abilityPanel.furyButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
                _abilityPanel.furyCooldownText.gameObject.SetActive(false);
            }

            ReanchorButton(_abilityPanel.petardButton, AMin(0), AMax(0));
            ReanchorButton(_abilityPanel.crossButton,  AMin(1), AMax(1));
            ReanchorButton(_abilityPanel.squareButton, AMin(2), AMax(2));
            ReanchorButton(_abilityPanel.shieldButton, AMin(3), AMax(3));
            ReanchorButton(_abilityPanel.furyButton,   AMin(4), AMax(4));
        }

        private static void ReanchorButton(Button button, Vector2 aMin, Vector2 aMax)
        {
            if (button == null) return;
            var rt = button.transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void ConfigureAbilityButtonVisual(Button button, Sprite borderSprite, Sprite iconSprite)
        {
            if (button == null) return;

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                text.gameObject.SetActive(false);

            var root = button.transform as RectTransform;
            if (root == null) return;

            var bgImg = button.targetGraphic as Image;
            if (bgImg != null)
            {
                bgImg.sprite = borderSprite;
                bgImg.type = Image.Type.Sliced;
                bgImg.color = Color.white;
            }

            var iconTf = root.Find("AbilityIcon");
            Image iconImg;
            if (iconTf == null)
            {
                var go = new GameObject("AbilityIcon");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(root, false);
                rt.anchorMin = new Vector2(0.16f, 0.16f);
                rt.anchorMax = new Vector2(0.84f, 0.84f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                iconImg = go.AddComponent<Image>();
                iconImg.raycastTarget = false;
            }
            else
            {
                iconImg = iconTf.GetComponent<Image>();
                if (iconImg == null) iconImg = iconTf.gameObject.AddComponent<Image>();
                var rt = iconTf as RectTransform;
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.16f, 0.16f);
                    rt.anchorMax = new Vector2(0.84f, 0.84f);
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
            }

            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.type = Image.Type.Sliced;
            iconImg.transform.localScale = Vector3.one * 0.7f;
        }

        private async void QuitToMenu()
        {
            if (_isLeavingToMenu) return;
            _isLeavingToMenu = true;
            var shouldRecordLoss = !_resultRecorded && !_gameEnded && _hasInitialBoardSync && !string.IsNullOrEmpty(_opUserId);
            _gameEnded = true;
            try
            {
                _mmTcs?.TrySetCanceled();
                await CancelMatchmakerTicketAsync();

                if (shouldRecordLoss)
                {
                    _resultRecorded = true;
                    await RecordMatch3ResultServerAsync(won: false);
                }

                if (_match != null && NakamaBootstrap.Instance?.Socket?.IsConnected == true)
                {
                    await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(
                        _match.Id, M3Op.PlayerLeft, Array.Empty<byte>());
                    await NakamaBootstrap.Instance.Socket.LeaveMatchAsync(_match.Id);
                }
            }
            catch { /* ignore */ }
            finally { SceneManager.LoadScene("ArenaMenu"); }
        }

        private async Task CancelMatchmakerTicketAsync()
        {
            var ticket = _matchmakerTicket;
            _matchmakerTicket = null;
            if (string.IsNullOrWhiteSpace(ticket)) return;

            try
            {
                var socket = NakamaBootstrap.Instance?.Socket;
                if (socket != null && socket.IsConnected)
                    await socket.RemoveMatchmakerAsync(ticket);
            }
            catch
            {
                // ignore cleanup errors during scene transitions
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // UI — Build  (prefab-first, procedural fallback)
        // ═════════════════════════════════════════════════════════════════════════

        private static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.15f);
            cam.orthographic    = true;
            cam.depth           = -1;
        }

        private void BuildUI()
        {
            // Canvas
            var cvGo = new GameObject("Canvas");
            cvGo.transform.SetParent(transform);
            var canvas = cvGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cvGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight  = 0.5f;
            cvGo.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }

            var root = cvGo.transform;

            MakeImg(root, "Bg", new Color(0.08f, 0.08f, 0.15f), V2(0, 0), V2(1, 1));

            // ── Left panel (my player + ability panel) ────────────────────────────
            var leftTr = MakePanel(root, "LeftCol", Color.clear, V2(0f, 0f), V2(0.26f, 1f));
            _myPanel = BuildOrInstantiate(myPanelPrefab, leftTr, V2(0f, 0.27f), V2(1f, 1f));
            if (_myPanel == null) _myPanel = BuildPlayerPanelProcedural(leftTr, isLeft: true);
            EnsureCombatWidgets(_myPanel);

            _abilityPanel = BuildOrInstantiate(abilityPanelPrefab, leftTr, V2(0f, 0f), V2(1f, 0.26f));
            if (_abilityPanel == null) _abilityPanel = BuildAbilityPanelProcedural(leftTr);

            TryAutoAssignAbilitySpritesInEditor();
            TryAutoAssignUiPrefabsInEditor();
            EnsureDamagePopupWidgets(_myPanel);
            ConfigureAbilityButtonsVisuals();
            _abilityPanel.OnPetardClicked += OnPetardClicked;
            _abilityPanel.OnCrossClicked  += OnCrossClicked;
            _abilityPanel.OnSquareClicked += OnSquareClicked;
            _abilityPanel.OnShieldClicked += OnShieldClicked;
            _abilityPanel.OnFuryClicked   += OnFuryClicked;

            // ── Board area ────────────────────────────────────────────────────────
            var boardColTr = MakePanel(root, "BoardCol", Color.clear, V2(0.26f, 0f), V2(0.74f, 1f));

            _hud = BuildOrInstantiate(hudPrefab, boardColTr, V2(0.02f, 0.90f), V2(0.98f, 0.99f));
            if (_hud == null) _hud = BuildHUDProcedural(boardColTr);

            _boardView = BuildOrInstantiate(boardViewPrefab, boardColTr, V2(0.04f, 0.04f), V2(0.96f, 0.89f));
            if (_boardView == null) _boardView = BuildBoardProcedural(boardColTr);

            _boardView.CellClicked += OnCellClicked;
            _boardView.CellSwiped += OnCellSwiped;
            _boardView.Build();

            // ── Right panel (opponent) ────────────────────────────────────────────
            var rightTr = MakePanel(root, "RightCol", Color.clear, V2(0.74f, 0f), V2(1f, 1f));
            _opPanel = BuildOrInstantiate(opPanelPrefab, rightTr, V2(0f, 0.27f), V2(1f, 1f));
            if (_opPanel == null) _opPanel = BuildPlayerPanelProcedural(rightTr, isLeft: false);
            EnsureCombatWidgets(_opPanel);
            EnsureDamagePopupWidgets(_opPanel);

            // ── Quit button ───────────────────────────────────────────────────────
            var quitBtn = MakeButton(root, "QuitBtn", "← Выйти",
                new Color(0.42f, 0.12f, 0.12f), Color.white,
                V2(0.75f, 0f), V2(1f, 0.07f));
            quitBtn.onClick.AddListener(QuitToMenu);

            // ── Overlays ──────────────────────────────────────────────────────────
            _searchingPanel = BuildOrInstantiate(searchingPanelPrefab, root, V2(0f, 0f), V2(1f, 1f));
            if (_searchingPanel == null) _searchingPanel = BuildSearchingPanelProcedural(root);
            _searchingPanel.OnCancelClicked += QuitToMenu;

            _gameOverPanel = BuildOrInstantiate(gameOverPanelPrefab, root, V2(0.22f, 0.24f), V2(0.78f, 0.76f));
            if (_gameOverPanel == null) _gameOverPanel = BuildGameOverPanelProcedural(root);
            _gameOverPanel.OnBackClicked += () => SceneManager.LoadScene("ArenaMenu");
            _gameOverPanel.Hide();

            if (_isSoloBotMode)
            {
                BuildPveSelector(root);
                ShowPveSelector(false);
            }
        }

        // ─── Generic helper: instantiate prefab or return null ────────────────────

        private static T BuildOrInstantiate<T>(T prefab, Transform parent, Vector2 aMin, Vector2 aMax)
            where T : MonoBehaviour
        {
            if (prefab == null) return null;
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            var rt = instance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = aMin; rt.anchorMax = aMax;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            return instance;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Procedural fallbacks (used when prefab fields are null)
        // ═════════════════════════════════════════════════════════════════════════

        private Match3PlayerPanel BuildPlayerPanelProcedural(Transform parent, bool isLeft)
        {
            var bg = MakePanel(parent, isLeft ? "MyPanel" : "OpPanel",
                new Color(0.11f, 0.11f, 0.20f, 0.97f), V2(0f, 0f), V2(1f, 1f));

            var avatar = MakeImg(bg, "Avatar", new Color(0.22f, 0.22f, 0.33f), V2(0.1f, 0.67f), V2(0.9f, 0.96f));
            var txt = MakeTxt(avatar, "T", "?", 52, new Color(0.5f, 0.5f, 0.6f), V2(0, 0), V2(1, 1));
            txt.alignment = TextAlignmentOptions.Center;

            var go = bg.gameObject;
            var panel = go.AddComponent<Match3PlayerPanel>();
            panel.avatarImage = avatar.GetComponent<Image>();
            panel.avatarPlaceholderText = txt;

            panel.nameText = MakeTxt(bg, "NameText", isLeft ? "Вы" : "Соперник", 17,
                Color.white, V2(0.05f, 0.62f), V2(0.95f, 0.67f));
            panel.nameText.alignment = TextAlignmentOptions.Center;

            MakeTxt(bg, "HpLbl", "HP", 13, new Color(1f, 0.45f, 0.45f), V2(0.05f, 0.57f), V2(0.25f, 0.62f));
            panel.hpText = MakeTxt(bg, "HpVal", "150/150", 12, Color.white, V2(0.60f, 0.57f), V2(0.97f, 0.62f));
            panel.hpFill = BuildBar(bg, "HpBar", new Color(0.78f, 0.14f, 0.14f), V2(0.05f, 0.52f), V2(0.95f, 0.57f));

            MakeTxt(bg, "MpLbl", "МП", 13, new Color(0.45f, 0.65f, 1f), V2(0.05f, 0.47f), V2(0.25f, 0.52f));
            panel.manaText = MakeTxt(bg, "MpVal", "0/100", 12, Color.white, V2(0.60f, 0.47f), V2(0.97f, 0.52f));
            panel.manaFill = BuildBar(bg, "MpBar", new Color(0.14f, 0.35f, 0.82f), V2(0.05f, 0.42f), V2(0.95f, 0.47f));

            BuildCombatStatsFrame(panel, bg, V2(0.05f, 0.20f), V2(0.95f, 0.40f));
            if (isLeft) BuildLegend(bg, V2(0.03f, 0.02f), V2(0.97f, 0.18f));

            return panel;
        }

        private static void BuildCombatStatsFrame(Match3PlayerPanel panel, RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            if (panel == null || parent == null) return;
            var frame = MakePanel(parent, "CombatStatsFrame", new Color(0.07f, 0.08f, 0.15f, 0.72f), aMin, aMax);
            var outline = frame.gameObject.GetComponent<Outline>();
            if (outline == null) outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.85f, 0.95f, 0.35f);
            outline.effectDistance = new Vector2(1f, -1f);

            panel.combatStatsText = MakeTxt(frame, "CombatStatsText",
                "Урон:   0\nБроня:  0\nХил:     0\nКрит:   0%", 18, Color.white, V2(0.06f, 0.10f), V2(0.94f, 0.92f));
            panel.combatStatsText.alignment = TextAlignmentOptions.TopLeft;

            panel.buffStateText = MakeTxt(frame, "BuffStateText", string.Empty, 11, new Color(0.62f, 0.86f, 1f), V2(0.45f, 0.76f), V2(0.95f, 0.98f));
            panel.buffStateText.alignment = TextAlignmentOptions.Right;
        }

        private static void EnsureCombatWidgets(Match3PlayerPanel panel)
        {
            if (panel == null) return;
            if (panel.combatStatsText != null && panel.buffStateText != null) return;
            var root = panel.transform as RectTransform;
            if (root == null) return;

            var existing = root.Find("CombatStatsFrame") as RectTransform;
            if (existing != null)
            {
                if (panel.combatStatsText == null)
                    panel.combatStatsText = existing.Find("CombatStatsText")?.GetComponent<TMP_Text>();
                if (panel.buffStateText == null)
                    panel.buffStateText = existing.Find("BuffStateText")?.GetComponent<TMP_Text>();
            }

            if (panel.combatStatsText == null || panel.buffStateText == null)
                BuildCombatStatsFrame(panel, root, V2(0.05f, 0.20f), V2(0.95f, 0.40f));
        }

        private void EnsureDamagePopupWidgets(Match3PlayerPanel panel)
        {
            if (panel == null) return;
            if (panel.damagePopupAnchor != null && panel.damagePopup != null) return;
            if (panel.avatarImage == null) return;

            var avatarRt = panel.avatarImage.rectTransform;
            if (avatarRt == null) return;

            var anchor = avatarRt.Find("DamagePopupAnchor") as RectTransform;
            if (anchor == null)
            {
                var go = new GameObject("DamagePopupAnchor");
                anchor = go.AddComponent<RectTransform>();
                anchor.SetParent(avatarRt, false);
                anchor.anchorMin = new Vector2(0.5f, 0.5f);
                anchor.anchorMax = new Vector2(0.5f, 0.5f);
                anchor.pivot = new Vector2(0.5f, 0.5f);
                anchor.anchoredPosition = new Vector2(0f, 0f);
                anchor.sizeDelta = new Vector2(0f, 0f);
            }
            panel.damagePopupAnchor = anchor;

            if (panel.damagePopup == null && damagePopupPrefab != null)
            {
                var inst = Instantiate(damagePopupPrefab, anchor, false);
                inst.name = "DamagePopup";
                var rt = inst.transform as RectTransform;
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, 40f);
                }
                panel.damagePopup = inst;
            }

            // Runtime fallback for builds where prefab wasn't assigned in Inspector.
            if (panel.damagePopup == null)
                panel.damagePopup = BuildDamagePopupProcedural(anchor);
        }

        private DamagePopupView BuildDamagePopupProcedural(RectTransform anchor)
        {
            if (anchor == null) return null;

            var rootGo = new GameObject("DamagePopup");
            var rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.SetParent(anchor, false);
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = new Vector2(0f, 40f);
            rootRt.sizeDelta = new Vector2(100f, 100f);

            var canvasGroup = rootGo.AddComponent<CanvasGroup>();
            var view = rootGo.AddComponent<DamagePopupView>();

            var critBgRt = MakeImg(rootRt, "CritBg", new Color(1f, 1f, 1f, 0.65f), V2(0f, 0f), V2(1f, 1f));
            var critBg = critBgRt.GetComponent<Image>();
            if (critBg != null) critBg.enabled = false;

            var txt = MakeTxt(rootRt, "Value", "-15", 22, new Color(1f, 0.22f, 0.22f, 1f), V2(0f, 0f), V2(1f, 1f));
            txt.alignment = TextAlignmentOptions.Center;

            // Wire private serialized fields on runtime-built instance.
            var viewType = typeof(DamagePopupView);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            viewType.GetField("valueText", flags)?.SetValue(view, txt);
            viewType.GetField("critBackground", flags)?.SetValue(view, critBg);
            viewType.GetField("canvasGroup", flags)?.SetValue(view, canvasGroup);

            return view;
        }

        private Match3AbilityPanel BuildAbilityPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "AbilityPanel",
                new Color(0.09f, 0.09f, 0.17f, 0.97f), V2(0f, 0f), V2(1f, 1f));
            var ap = bg.gameObject.AddComponent<Match3AbilityPanel>();

            // 5 abilities in one line.
            const float x0 = 0.02f;
            const float w  = 0.18f;
            const float g  = 0.015f;
            Vector2 AMin(int i) => V2(x0 + i * (w + g), 0.18f);
            Vector2 AMax(int i) => V2(x0 + i * (w + g) + w, 0.92f);

            ap.petardButton = MakeButton(bg, "PetardBtn", string.Empty, Color.white, Color.white, AMin(0), AMax(0));
            ap.petardCooldownText = MakeTxt(ap.petardButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.petardCooldownText.gameObject.SetActive(false);

            ap.crossButton = MakeButton(bg, "CrossBtn", string.Empty, Color.white, Color.white, AMin(1), AMax(1));
            ap.crossCooldownText = MakeTxt(ap.crossButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.crossCooldownText.gameObject.SetActive(false);

            ap.squareButton = MakeButton(bg, "SquareBtn", string.Empty, Color.white, Color.white, AMin(2), AMax(2));
            ap.squareCooldownText = MakeTxt(ap.squareButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.squareCooldownText.gameObject.SetActive(false);

            ap.shieldButton = MakeButton(bg, "ShieldBtn", string.Empty, Color.white, Color.white, AMin(3), AMax(3));
            ap.shieldCooldownText = MakeTxt(ap.shieldButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.shieldCooldownText.gameObject.SetActive(false);

            ap.furyButton = MakeButton(bg, "FuryBtn", string.Empty, Color.white, Color.white, AMin(4), AMax(4));
            ap.furyCooldownText = MakeTxt(ap.furyButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.furyCooldownText.gameObject.SetActive(false);

            // Hint (without text)
            var hintGo = new GameObject("AbilityHint");
            var hintRt = hintGo.AddComponent<RectTransform>();
            hintRt.SetParent(bg, false);
            hintRt.anchorMin = V2(0.05f, 0.02f); hintRt.anchorMax = V2(0.95f, 0.24f);
            hintRt.offsetMin = hintRt.offsetMax = Vector2.zero;
            hintGo.AddComponent<Image>().color = new Color(0.8f, 0.9f, 1f, 0.08f);
            hintGo.SetActive(false);
            ap.abilityHint = hintGo;

            return ap;
        }

        private Match3GameHUD BuildHUDProcedural(Transform parent)
        {
            var bg  = MakePanel(parent, "HUD", Color.clear, V2(0f, 0f), V2(1f, 1f));
            var hud = bg.gameObject.AddComponent<Match3GameHUD>();
            hud.turnText  = MakeTxt(bg, "TurnText",  "Поиск...", 20, Color.white, V2(0f, 0f), V2(0.72f, 1f));
            hud.timerText = MakeTxt(bg, "TimerText", "—", 26, new Color(1f, 0.85f, 0.2f), V2(0.74f, 0f), V2(1f, 1f));
            hud.timerText.alignment = TextAlignmentOptions.Right;
            hud.turnText.alignment  = TextAlignmentOptions.Left;
            return hud;
        }

        private Match3BoardView BuildBoardProcedural(Transform parent)
        {
            var frame = MakeImg(parent, "Frame", new Color(0.38f, 0.32f, 0.18f), V2(0f, 0f), V2(1f, 1f));
            var inner = MakeImg(frame, "Inner", new Color(0.17f, 0.15f, 0.11f), V2(0f, 0f), V2(1f, 1f));

            const int cells = Match3BoardLogic.Size, cellPx = 74, gapPx = -1;
            int total = cells * cellPx + (cells - 1) * gapPx;

            var gridGo = new GameObject("CellContainer");
            var gridRt = gridGo.AddComponent<RectTransform>();
            gridRt.SetParent(inner, false);
            gridRt.anchorMin = new Vector2(0.5f, 0.5f); gridRt.anchorMax = new Vector2(0.5f, 0.5f);
            gridRt.pivot     = new Vector2(0.5f, 0.5f); gridRt.sizeDelta = new Vector2(total, total);
            var glg = gridGo.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(cellPx, cellPx); glg.spacing = new Vector2(gapPx, gapPx);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; glg.constraintCount = cells;
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft; glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.UpperLeft;

            var bv = frame.gameObject.AddComponent<Match3BoardView>();
            bv.cellContainer = gridGo.transform;
            return bv;
        }

        private void BuildPveSelector(Transform root)
        {
            _pveSelectorRoot = MakePanel(root, "PveSelector", new Color(0f, 0f, 0f, 0.92f), V2(0f, 0f), V2(1f, 1f)).gameObject;
            var card = MakePanel(_pveSelectorRoot.transform, "Card", new Color(0.08f, 0.10f, 0.18f, 0.98f), V2(0.22f, 0.18f), V2(0.78f, 0.82f));
            MakeTxt(card, "Title", "Выбор босса", 28, new Color(0.8f, 0.95f, 1f), V2(0.05f, 0.84f), V2(0.95f, 0.96f)).alignment = TextAlignmentOptions.Center;
            _pveBotTitleText = MakeTxt(card, "BossName", "Босс", 22, new Color(1f, 0.9f, 0.45f), V2(0.08f, 0.62f), V2(0.92f, 0.74f));
            _pveBotTitleText.alignment = TextAlignmentOptions.Center;
            _pveBotStatsText = MakeTxt(card, "BossStats", "—", 16, Color.white, V2(0.10f, 0.26f), V2(0.90f, 0.62f));
            _pveBotStatsText.alignment = TextAlignmentOptions.TopLeft;

            _pvePrevButton = MakeButton(card, "PrevBoss", "←", new Color(0.18f, 0.28f, 0.55f), Color.white, V2(0.10f, 0.08f), V2(0.28f, 0.18f));
            _pveNextButton = MakeButton(card, "NextBoss", "→", new Color(0.18f, 0.28f, 0.55f), Color.white, V2(0.30f, 0.08f), V2(0.48f, 0.18f));
            _pveStartButton = MakeButton(card, "StartBoss", "В бой", new Color(0.14f, 0.42f, 0.18f), Color.white, V2(0.52f, 0.08f), V2(0.90f, 0.18f));

            _pvePrevButton.onClick.AddListener(SelectPrevPveBot);
            _pveNextButton.onClick.AddListener(SelectNextPveBot);
            _pveStartButton.onClick.AddListener(StartSelectedPveBot);
        }

        private Match3SearchingPanel BuildSearchingPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "SearchingPanel", new Color(0f, 0f, 0f, 0.97f),
                V2(0f, 0f), V2(1f, 1f));
            var sp = bg.gameObject.AddComponent<Match3SearchingPanel>();
            sp.statusText = MakeTxt(bg, "ST", "Поиск соперника…", 22, Color.white,
                V2(0.25f, 0.52f), V2(0.75f, 0.62f));
            sp.statusText.alignment = TextAlignmentOptions.Center;
            var btn = MakeButton(bg, "Cancel", "Отмена",
                new Color(0.45f, 0.12f, 0.12f), Color.white, V2(0.35f, 0.40f), V2(0.65f, 0.47f));
            sp.cancelButton = btn;
            return sp;
        }

        private Match3GameOverPanel BuildGameOverPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "GameOverPanel", new Color(0.05f, 0.05f, 0.10f, 0.96f),
                V2(0.22f, 0.24f), V2(0.78f, 0.76f));
            var gop = bg.gameObject.AddComponent<Match3GameOverPanel>();
            MakeImg(bg, "Stripe", new Color(0.35f, 0.28f, 0.12f), V2(0f, 0.85f), V2(1f, 1f));
            gop.titleText = MakeTxt(bg, "Title", "Победа!", 38, Color.white,
                V2(0.05f, 0.60f), V2(0.95f, 0.88f));
            gop.titleText.alignment = TextAlignmentOptions.Center;
            gop.rewardText = MakeTxt(bg, "Reward", "+100 опыта\n+50 золота", 19,
                new Color(1f, 0.90f, 0.30f), V2(0.05f, 0.33f), V2(0.95f, 0.60f));
            gop.rewardText.alignment = TextAlignmentOptions.Center;
            var backBtn = MakeButton(bg, "Back", "В главное меню",
                new Color(0.18f, 0.28f, 0.55f), Color.white, V2(0.15f, 0.06f), V2(0.85f, 0.28f));
            gop.backButton = backBtn;
            return gop;
        }

        private static void BuildLegend(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var bg = MakePanel(parent, "Legend", new Color(0.09f, 0.09f, 0.16f, 0.6f), aMin, aMax);
            var rows = new[]
            {
                ("♦", new Color(0.90f, 0.18f, 0.18f), "+5 мп/камень"),
                ("♦", new Color(0.95f, 0.82f, 0.12f), "+3 мп/камень"),
                ("●", new Color(0.18f, 0.80f, 0.18f), "+1 мп/камень"),
                ("☠", new Color(0.60f, 0.60f, 0.62f), "-5 жзн сопернику"),
                ("✝", new Color(0.90f, 0.75f, 0.15f), "+1 жзнь вам"),
            };
            float rh = 1f / rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                var (sym, col, desc) = rows[i];
                float y0 = 1f - (i + 1) * rh, y1 = 1f - i * rh;
                var s = MakeTxt(bg, $"S{i}", sym,  13, col,        V2(0.02f, y0), V2(0.16f, y1));
                s.alignment = TextAlignmentOptions.Center;
                MakeTxt(bg, $"D{i}", desc, 10, Color.white, V2(0.18f, y0), V2(0.98f, y1));
            }
        }

        // ─── Ability Callbacks ────────────────────────────────────────────────────

        private bool IsAbilityAvailable(AbilityType ability)
        {
            return _myStats.mana >= GetAbilityCost(ability) && GetAbilityCooldown(ability) == 0;
        }

        private int GetAbilityCost(AbilityType ability)
        {
            return ability switch
            {
                AbilityType.Petard => PetardAbilityCost,
                AbilityType.Cross => CrossAbilityCost,
                AbilityType.Square => SquareAbilityCost,
                AbilityType.Shield => ShieldAbilityCost,
                AbilityType.Fury => FuryAbilityCost,
                _ => 0,
            };
        }

        private int GetAbilityCooldown(AbilityType ability)
        {
            return ability switch
            {
                AbilityType.Petard => _myStats.petardCooldown,
                AbilityType.Cross => _myStats.crossCooldown,
                AbilityType.Square => _myStats.squareCooldown,
                AbilityType.Shield => _myStats.shieldCooldown,
                AbilityType.Fury => _myStats.furyCooldown,
                _ => 0,
            };
        }

        private void SelectOrToggleAbility(AbilityType ability)
        {
            if (_pendingAbility.HasValue && _pendingAbility.Value == ability)
            {
                _pendingAbility = null;
                _abilityPanel?.SetSelectedAbility(null);
                _abilityPanel?.ShowHint(false);
                return;
            }

            if (!IsAbilityAvailable(ability)) return;
            if (_selX >= 0 && _selY >= 0) SendSelectionSync(_selX, _selY, false);
            _pendingAbility = ability;
            _abilityPanel?.SetSelectedAbility(ability);
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _abilityPanel?.ShowHint(true);
        }

        private void OnPetardClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            ExecuteAbility(AbilityType.Petard, -1, -1);
        }

        private void OnCrossClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            SelectOrToggleAbility(AbilityType.Cross);
        }

        private void OnSquareClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            SelectOrToggleAbility(AbilityType.Square);
        }

        private void OnShieldClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            ExecuteAbility(AbilityType.Shield, -1, -1);
        }

        private void OnFuryClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            ExecuteAbility(AbilityType.Fury, -1, -1);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // UI Factory Helpers
        // ═════════════════════════════════════════════════════════════════════════

        private static Vector2 V2(float x, float y) => new Vector2(x, y);

        private static TMP_FontAsset _defaultFont;
        private static TMP_FontAsset DefaultFont
        {
            get
            {
                if (_defaultFont != null) return _defaultFont;
                _defaultFont = TMP_Settings.defaultFontAsset;
                return _defaultFont;
            }
        }

        private static RectTransform MakePanel(Transform p, string name, Color color,
            Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (color.a > 0.001f) go.AddComponent<Image>().color = color;
            return rt;
        }

        private static RectTransform MakeImg(Transform p, string name, Color color,
            Vector2 aMin, Vector2 aMax)
        {
            var rt = MakePanel(p, name, Color.clear, aMin, aMax);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt;
        }

        private static RectTransform MakeImg(RectTransform p, string name, Color color,
            Vector2 aMin, Vector2 aMax) => MakeImg((Transform)p, name, color, aMin, aMax);

        private static TMP_Text MakeTxt(Transform p, string name, string text,
            int size, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.font = DefaultFont; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Left;
            return t;
        }

        private static TMP_Text MakeTxt(RectTransform p, string name, string text,
            int size, Color color, Vector2 aMin, Vector2 aMax)
            => MakeTxt((Transform)p, name, text, size, color, aMin, aMax);

        private static Button MakeButton(Transform p, string name, string label,
            Color bgColor, Color textColor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>(); img.color = bgColor;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb = btn.colors;
            cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.15f);
            cb.pressedColor     = Color.Lerp(bgColor, Color.black, 0.18f);
            cb.disabledColor    = new Color(bgColor.r * 0.45f, bgColor.g * 0.45f, bgColor.b * 0.45f, 0.7f);
            btn.colors = cb;
            if (!string.IsNullOrEmpty(label))
            {
                var lbl = MakeTxt(go.transform, "Lbl", label, 15, textColor, V2(0.04f, 0), V2(0.96f, 1));
                lbl.alignment = TextAlignmentOptions.Center;
            }
            return btn;
        }

        private static Button MakeButton(RectTransform p, string name, string label,
            Color bgColor, Color textColor, Vector2 aMin, Vector2 aMax)
            => MakeButton((Transform)p, name, label, bgColor, textColor, aMin, aMax);

        private static Image BuildBar(Transform p, string name, Color fillColor,
            Vector2 aMin, Vector2 aMax)
        {
            var frameGo = new GameObject(name + "Frame");
            var frameRt = frameGo.AddComponent<RectTransform>();
            frameRt.SetParent(p, false);
            frameRt.anchorMin = aMin; frameRt.anchorMax = aMax;
            frameRt.offsetMin = frameRt.offsetMax = Vector2.zero;
            frameGo.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.98f);

            var trackGo = new GameObject(name + "Track");
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.SetParent(frameGo.transform, false);
            trackRt.anchorMin = Vector2.zero; trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = new Vector2(2, 2); trackRt.offsetMax = new Vector2(-2, -2);
            trackGo.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.12f, 0.95f);

            var fillGo = new GameObject(name + "Fill");
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.SetParent(trackGo.transform, false);
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1, 1); fillRt.offsetMax = new Vector2(-1, -1);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = fillColor; fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal; fillImg.fillAmount = 1f;
            return fillImg;
        }

        private static Image BuildBar(RectTransform p, string name, Color fillColor,
            Vector2 aMin, Vector2 aMax) => BuildBar((Transform)p, name, fillColor, aMin, aMax);

        [Serializable]
        private sealed class PveCreateRpcResponse
        {
            public bool ok;
            public string match_id;
            public string bot_id;
            public string bot_name;
            public string bot_user_id;
            public string err;
        }

        [Serializable]
        private sealed class PveCreateRpcRequest
        {
            public string bot_id;
            public int session_epoch;
        }

        [Serializable]
        private sealed class StatsRecordRpcRequest
        {
            public bool won;
            public int session_epoch;
        }

        [Serializable]
        private sealed class PveCatalogRpcResponse
        {
            public bool ok;
            public PveProgressInfo progression;
            public PveBotInfo[] bots;
            public string err;
        }

        [Serializable]
        private sealed class PveProgressInfo
        {
            public int level = 1;
            public int xp;
            public int gold;
        }

        [Serializable]
        private sealed class PveBotInfo
        {
            public string id;
            public string name;
            public int difficulty;
            public int hp_bonus;
            public int start_mana;
            public int reward_xp;
            public int reward_gold;
        }
    }
}
