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
        [SerializeField] private AudioClip sfxVictory;
        [SerializeField] private AudioClip sfxDefeat;

        [Header("Ability Icon Sprites")]
        [SerializeField] private Sprite petardAbilitySprite;
        [SerializeField] private Sprite crossAbilitySprite;
        [SerializeField] private Sprite squareAbilitySprite;

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

        // ─── Game Constants ───────────────────────────────────────────────────────
        private const int   MaxHp           = 150;
        private const int   MaxMana         = 100;
        private const float TurnDuration    = 30f;
        private const int   CrossAbilityCost  = 20;
        private const int   SquareAbilityCost = 20;
        private const int   PetardAbilityCost = 30;

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
        private Coroutine _snapshotRetryRoutine;
        private bool _hasInitialBoardSync;
        private int _remoteSelX = -1, _remoteSelY = -1;
        private bool _resultRecorded;

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

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private async void Start()
        {
            _cts     = new CancellationTokenSource();
            _board   = new Match3BoardLogic();
            _myStats = new PlayerStats();
            _opStats = new PlayerStats();

            EnsureCamera();
            BuildUI();
            EnsureAudioSource();
            TryAutoAssignSfxInEditor();
            _searchingPanel?.Show("Поиск соперника…");

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
            StartGameWaitingServer();
        }

        // ─── Game Flow ────────────────────────────────────────────────────────────

        private void StartGameWaitingServer()
        {
            _hasInitialBoardSync = false;
            _remoteSelX = _remoteSelY = -1;
            _boardView?.RefreshAll(_board);
            RefreshStatsUI();
            _abilityPanel?.Refresh(_myStats, false, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
            _hud?.SetTurn("Ожидание синхронизации…");
            _hud?.SetTimer("—");
            _boardView?.SetDimmed(true);
            _inputBlocked = true;
            if (_snapshotRetryRoutine != null) StopCoroutine(_snapshotRetryRoutine);
            _snapshotRetryRoutine = StartCoroutine(RequestSnapshotUntilSynced());
        }

        private void BeginMyTurn()
        {
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

            _abilityPanel?.Refresh(_myStats, true, false, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
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
            _abilityPanel?.Refresh(_myStats, false, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
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
                _abilityPanel?.Refresh(_myStats, _isMyTurn, false, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
                return;
            }

            StartCoroutine(SendAbilityRequestRoutine(ability, cx, cy));
        }

        private IEnumerator SendAbilityRequestRoutine(AbilityType ability, int cx, int cy)
        {
            _inputBlocked = true;
            if (_boardView != null && ability != AbilityType.Petard)
            {
                yield return _boardView.AnimateAbilityArea(ability, cx, cy, 0.24f);
            }
            var req = new M3ActionRequest
            {
                actionType = ability == AbilityType.Cross ? 2 : (ability == AbilityType.Square ? 3 : 4),
                fromX = -1, fromY = -1, toX = -1, toY = -1,
                cx = ability == AbilityType.Petard ? -1 : cx,
                cy = ability == AbilityType.Petard ? -1 : cy,
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
            _gameOverPanel?.Show(won);
        }

        private async Task RecordMatch3ResultServerAsync(bool won)
        {
            try
            {
                if (NakamaBootstrap.Instance == null) return;
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts != null ? _cts.Token : CancellationToken.None);
                if (NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null) return;
                var payload = won ? "{\"won\":true}" : "{\"won\":false}";
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
            if (_match == null || _gameEnded) return;
            _ = SendStateAsync(M3Op.ActionRequest,
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)));
        }

        private void SendSelectionSync(int x, int y, bool selected)
        {
            if (_match == null || _gameEnded) return;
            var msg = new M3SelectionSyncMsg { x = x, y = y, selected = selected };
            _ = SendStateAsync(M3Op.SelectionSync, Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg)));
        }

        private void RequestSnapshot()
        {
            if (_match == null || _gameEnded) return;
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
                    if (!_gameEnded) { _gameEnded = true; ShowGameOver(msg.winnerUserId == _myUserId); }
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
                    _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
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
            if (_remoteSyncRoutine != null) StopCoroutine(_remoteSyncRoutine);
            _remoteSyncRoutine = StartCoroutine(ApplyRemoteBoardSync(msg));
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
                        yield return _boardView.AnimateClearByBoardDiff(currentBoard, step.board, 0.25f);
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

            _myStats.hp            = amA ? msg.aHp       : msg.bHp;
            _myStats.mana          = amA ? msg.aMana      : msg.bMana;
            _myStats.crossCooldown  = amA ? msg.aCrossCd  : msg.bCrossCd;
            _myStats.squareCooldown = amA ? msg.aSquareCd : msg.bSquareCd;
            _myStats.petardCooldown = amA ? msg.aPetardCd : msg.bPetardCd;
            _opStats.hp             = amA ? msg.bHp       : msg.aHp;
            _opStats.mana           = amA ? msg.bMana      : msg.aMana;

            _boardView?.RefreshAll(_board);
            if (!usedAnimSteps && _boardView != null)
                yield return _boardView.AnimateBoardTransition(beforeBoard, _board, 0.45f);
            RefreshStatsUI();

            if (_myStats.hp < prevMyHp || _opStats.hp < prevOpHp)
                PlaySfx(sfxDamageHit);
            if (msg.extraTurn)
            {
                PlaySfx(sfxExtraTurn);
                _boardView?.ShowCenterAnnouncement("Дополнительный ход\nза 5+ камней", new Color(0.35f, 1f, 0.35f), 2f);
            }

            bool petardKeepsTurn = msg.actionType == 4 && ((msg.activeUserId == _myUserId) == _isMyTurn);
            if (petardKeepsTurn)
            {
                _inputBlocked = !_isMyTurn;
                _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, CrossAbilityCost, SquareAbilityCost, PetardAbilityCost);
                _remoteSyncRoutine = null;
                yield break;
            }

            bool isMyTurnNow = msg.activeUserId == _myUserId;
            if (isMyTurnNow) BeginMyTurn();
            else             BeginOpponentTurn();

            _remoteSyncRoutine = null;
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
            _myPanel?.UpdateStats(_myStats.hp, MaxHp, _myStats.mana, MaxMana);
            _opPanel?.UpdateStats(_opStats.hp, MaxHp, _opStats.mana, MaxMana);
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
            if (sfxVictory == null)      sfxVictory      = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_victory.wav");
            if (sfxDefeat == null)       sfxDefeat       = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Match3/m3_defeat.wav");
#endif
        }

        private void TryAutoAssignAbilitySpritesInEditor()
        {
#if UNITY_EDITOR
            if (petardAbilitySprite == null) petardAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/ppp.png");
            if (crossAbilitySprite == null) crossAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/qqq.png");
            if (squareAbilitySprite == null) squareAbilitySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/qrr.png");
#endif
        }

        private void ConfigureAbilityButtonsVisuals()
        {
            if (_abilityPanel == null) return;
            EnsurePetardButtonExists();
            ConfigureAbilityButtonIcon(_abilityPanel.petardButton, petardAbilitySprite);
            ConfigureAbilityButtonIcon(_abilityPanel.crossButton, crossAbilitySprite);
            ConfigureAbilityButtonIcon(_abilityPanel.squareButton, squareAbilitySprite);
        }

        private void EnsurePetardButtonExists()
        {
            if (_abilityPanel == null || _abilityPanel.petardButton != null) return;
            var panelRt = _abilityPanel.transform as RectTransform;
            if (panelRt == null) return;

            // Re-layout existing two buttons to center/right and insert petard on the left.
            ReanchorButton(_abilityPanel.crossButton, V2(0.35f, 0.34f), V2(0.65f, 0.80f));
            ReanchorButton(_abilityPanel.squareButton, V2(0.68f, 0.34f), V2(0.98f, 0.80f));

            var petardButton = MakeButton(panelRt, "PetardBtn", string.Empty,
                new Color(0.48f, 0.19f, 0.16f), Color.white, V2(0.02f, 0.34f), V2(0.32f, 0.80f));
            _abilityPanel.petardButton = petardButton;
            _abilityPanel.petardCooldownText = MakeTxt(
                petardButton.transform, "Cd", string.Empty, 11, new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            _abilityPanel.petardCooldownText.gameObject.SetActive(false);
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

        private static void ConfigureAbilityButtonIcon(Button button, Sprite iconSprite)
        {
            if (button == null) return;

            foreach (var text in button.GetComponentsInChildren<Text>(true))
                text.gameObject.SetActive(false);

            var root = button.transform as RectTransform;
            if (root == null) return;

            var iconTf = root.Find("AbilityIcon");
            Image iconImg;
            if (iconTf == null)
            {
                var go = new GameObject("AbilityIcon");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(root, false);
                rt.anchorMin = new Vector2(0.08f, 0.08f);
                rt.anchorMax = new Vector2(0.92f, 0.92f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                iconImg = go.AddComponent<Image>();
                iconImg.raycastTarget = false;
            }
            else
            {
                iconImg = iconTf.GetComponent<Image>();
                if (iconImg == null) iconImg = iconTf.gameObject.AddComponent<Image>();
            }

            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
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
            finally { SceneManager.LoadScene("MainMenu"); }
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

            _abilityPanel = BuildOrInstantiate(abilityPanelPrefab, leftTr, V2(0f, 0f), V2(1f, 0.26f));
            if (_abilityPanel == null) _abilityPanel = BuildAbilityPanelProcedural(leftTr);

            TryAutoAssignAbilitySpritesInEditor();
            ConfigureAbilityButtonsVisuals();
            _abilityPanel.OnPetardClicked += OnPetardClicked;
            _abilityPanel.OnCrossClicked  += OnCrossClicked;
            _abilityPanel.OnSquareClicked += OnSquareClicked;

            // ── Board area ────────────────────────────────────────────────────────
            var boardColTr = MakePanel(root, "BoardCol", Color.clear, V2(0.26f, 0f), V2(0.74f, 1f));

            _hud = BuildOrInstantiate(hudPrefab, boardColTr, V2(0.02f, 0.90f), V2(0.98f, 0.99f));
            if (_hud == null) _hud = BuildHUDProcedural(boardColTr);

            _boardView = BuildOrInstantiate(boardViewPrefab, boardColTr, V2(0.04f, 0.04f), V2(0.96f, 0.89f));
            if (_boardView == null) _boardView = BuildBoardProcedural(boardColTr);

            _boardView.CellClicked += OnCellClicked;
            _boardView.Build();

            // ── Right panel (opponent) ────────────────────────────────────────────
            var rightTr = MakePanel(root, "RightCol", Color.clear, V2(0.74f, 0f), V2(1f, 1f));
            _opPanel = BuildOrInstantiate(opPanelPrefab, rightTr, V2(0f, 0.27f), V2(1f, 1f));
            if (_opPanel == null) _opPanel = BuildPlayerPanelProcedural(rightTr, isLeft: false);

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
            _gameOverPanel.OnBackClicked += () => SceneManager.LoadScene("MainMenu");
            _gameOverPanel.Hide();
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
            txt.alignment = TextAnchor.MiddleCenter;

            var go = bg.gameObject;
            var panel = go.AddComponent<Match3PlayerPanel>();
            panel.avatarImage = avatar.GetComponent<Image>();
            panel.avatarPlaceholderText = txt;

            panel.nameText = MakeTxt(bg, "NameText", isLeft ? "Вы" : "Соперник", 17,
                Color.white, V2(0.05f, 0.62f), V2(0.95f, 0.67f));
            panel.nameText.alignment = TextAnchor.MiddleCenter;

            MakeTxt(bg, "HpLbl", "HP", 13, new Color(1f, 0.45f, 0.45f), V2(0.05f, 0.57f), V2(0.25f, 0.62f));
            panel.hpText = MakeTxt(bg, "HpVal", "150/150", 12, Color.white, V2(0.60f, 0.57f), V2(0.97f, 0.62f));
            panel.hpFill = BuildBar(bg, "HpBar", new Color(0.78f, 0.14f, 0.14f), V2(0.05f, 0.52f), V2(0.95f, 0.57f));

            MakeTxt(bg, "MpLbl", "МП", 13, new Color(0.45f, 0.65f, 1f), V2(0.05f, 0.47f), V2(0.25f, 0.52f));
            panel.manaText = MakeTxt(bg, "MpVal", "0/100", 12, Color.white, V2(0.60f, 0.47f), V2(0.97f, 0.52f));
            panel.manaFill = BuildBar(bg, "MpBar", new Color(0.14f, 0.35f, 0.82f), V2(0.05f, 0.42f), V2(0.95f, 0.47f));

            if (isLeft) BuildLegend(bg, V2(0.03f, 0.02f), V2(0.97f, 0.40f));

            return panel;
        }

        private Match3AbilityPanel BuildAbilityPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "AbilityPanel",
                new Color(0.09f, 0.09f, 0.17f, 0.97f), V2(0f, 0f), V2(1f, 1f));
            var ap = bg.gameObject.AddComponent<Match3AbilityPanel>();

            // Petard (left)
            var petardBg = MakeButton(bg, "PetardBtn", string.Empty,
                new Color(0.48f, 0.19f, 0.16f), Color.white, V2(0.02f, 0.34f), V2(0.32f, 0.80f));
            ap.petardButton = petardBg;
            ap.petardCooldownText = MakeTxt(petardBg.transform, "Cd", string.Empty, 11,
                new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.petardCooldownText.gameObject.SetActive(false);

            // Cross (center)
            var crossBg = MakeButton(bg, "CrossBtn", string.Empty,
                new Color(0.28f, 0.14f, 0.48f), Color.white, V2(0.35f, 0.34f), V2(0.65f, 0.80f));
            ap.crossButton = crossBg;
            ap.crossCooldownText = MakeTxt(crossBg.transform, "Cd", string.Empty, 11,
                new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.crossCooldownText.gameObject.SetActive(false);

            // Square (right)
            var sqBg = MakeButton(bg, "SquareBtn", string.Empty,
                new Color(0.14f, 0.25f, 0.48f), Color.white, V2(0.68f, 0.34f), V2(0.98f, 0.80f));
            ap.squareButton = sqBg;
            ap.squareCooldownText = MakeTxt(sqBg.transform, "Cd", string.Empty, 11,
                new Color(0.9f, 0.85f, 0.5f), V2(0f, 0f), V2(0f, 0f));
            ap.squareCooldownText.gameObject.SetActive(false);

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
            hud.timerText.alignment = TextAnchor.MiddleRight;
            hud.turnText.alignment  = TextAnchor.MiddleLeft;
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

        private Match3SearchingPanel BuildSearchingPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "SearchingPanel", new Color(0f, 0f, 0f, 0.97f),
                V2(0f, 0f), V2(1f, 1f));
            var sp = bg.gameObject.AddComponent<Match3SearchingPanel>();
            sp.statusText = MakeTxt(bg, "ST", "Поиск соперника…", 22, Color.white,
                V2(0.25f, 0.52f), V2(0.75f, 0.62f));
            sp.statusText.alignment = TextAnchor.MiddleCenter;
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
            gop.titleText.alignment = TextAnchor.MiddleCenter;
            gop.rewardText = MakeTxt(bg, "Reward", "+100 опыта\n+50 золота", 19,
                new Color(1f, 0.90f, 0.30f), V2(0.05f, 0.33f), V2(0.95f, 0.60f));
            gop.rewardText.alignment = TextAnchor.MiddleCenter;
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
                s.alignment = TextAnchor.MiddleCenter;
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
                _ => SquareAbilityCost,
            };
        }

        private int GetAbilityCooldown(AbilityType ability)
        {
            return ability switch
            {
                AbilityType.Petard => _myStats.petardCooldown,
                AbilityType.Cross => _myStats.crossCooldown,
                _ => _myStats.squareCooldown,
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

        // ═════════════════════════════════════════════════════════════════════════
        // UI Factory Helpers
        // ═════════════════════════════════════════════════════════════════════════

        private static Vector2 V2(float x, float y) => new Vector2(x, y);

        private static Font _defaultFont;
        private static Font DefaultFont
        {
            get
            {
                if (_defaultFont != null) return _defaultFont;
                _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_defaultFont == null)
                    _defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        private static Text MakeTxt(Transform p, string name, string text,
            int size, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = text; t.font = DefaultFont; t.fontSize = size; t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        private static Text MakeTxt(RectTransform p, string name, string text,
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
                lbl.alignment = TextAnchor.MiddleCenter;
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
    }
}
