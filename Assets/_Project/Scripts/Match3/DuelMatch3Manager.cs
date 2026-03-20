using System;
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

        // ─── OpCodes (match-3 specific, 10+ to avoid collision with DuelRoom) ─────
        private static class M3Op
        {
            public const long BoardSync  = 10;
            public const long GameOver   = 11;
            public const long PlayerLeft = 12;
        }

        // ─── Game Constants ───────────────────────────────────────────────────────
        private const int   MaxHp           = 150;
        private const int   MaxMana         = 150;
        private const float TurnDuration    = 30f;
        private const int   AbilityCost     = 20;
        private const int   AbilityCooldown = 2;
        private const int   SkullDamage     = 5;
        private const int   AnkhHeal        = 1;

        // Mana per gem matched (index = (int)PieceType)
        private static readonly int[] GemMana = { 0, 5, 3, 1, 0, 0 };

        // ─── Nakama ───────────────────────────────────────────────────────────────
        private IMatch    _match;
        private string    _myUserId;
        private string    _opUserId;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<IMatchmakerMatched> _mmTcs;

        // ─── Game State ───────────────────────────────────────────────────────────
        private Match3BoardLogic _board;
        private PlayerStats      _myStats;
        private PlayerStats      _opStats;
        private bool  _isMyTurn;
        private float _turnTimer;
        private bool  _gameEnded;

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
            _cts?.Cancel();
            _cts?.Dispose();
            if (NakamaBootstrap.Instance?.Socket != null)
                UnhookSocket(NakamaBootstrap.Instance.Socket);
        }

        private void Update()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            _turnTimer -= Time.deltaTime;
            _hud?.SetTimer(Mathf.CeilToInt(Mathf.Max(0f, _turnTimer)).ToString());
            if (_turnTimer <= 0f) OnTurnTimerExpired();
        }

        // ─── Matchmaking ──────────────────────────────────────────────────────────

        private async Task FindMatchAsync(CancellationToken ct)
        {
            _mmTcs = new TaskCompletionSource<IMatchmakerMatched>();
            ct.ThrowIfCancellationRequested();

            // Use "*" — same as existing DuelRoom.
            // Both players enter from DuelMatch3 scene so they naturally pair up.
            await NakamaBootstrap.Instance.Socket.AddMatchmakerAsync(
                query: "*",
                minCount: 2,
                maxCount: 2);

            var matched = await _mmTcs.Task;
            _mmTcs = null;
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
            StartGame();
        }

        // ─── Game Flow ────────────────────────────────────────────────────────────

        private void StartGame()
        {
            int seed = Math.Abs((_match?.Id ?? "seed").GetHashCode());
            _board.Init(seed);

            var ids     = GetSortedIds();
            var firstId = ids.Count >= 2
                ? (seed % 2 == 0 ? ids[0] : ids[1])
                : _myUserId;
            _isMyTurn = firstId == _myUserId;

            _boardView?.RefreshAll(_board);
            RefreshStatsUI();
            _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, AbilityCost);

            if (_isMyTurn) BeginMyTurn();
            else           BeginOpponentTurn();
        }

        private void BeginMyTurn()
        {
            _isMyTurn    = true;
            _turnTimer   = TurnDuration;
            _inputBlocked = false;
            _pendingAbility = null;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _abilityPanel?.ShowHint(false);

            _hud?.SetTurn("Ваш ход!");
            _hud?.SetTimer(Mathf.CeilToInt(TurnDuration).ToString());

            TickAbilityCooldowns(_myStats);
            _abilityPanel?.Refresh(_myStats, true, false, AbilityCost);
        }

        private void BeginOpponentTurn()
        {
            _isMyTurn    = false;
            _inputBlocked = true;
            _pendingAbility = null;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _abilityPanel?.ShowHint(false);

            _hud?.SetTurn("Ход соперника…");
            _hud?.SetTimer("—");
            _abilityPanel?.Refresh(_myStats, false, _gameEnded, AbilityCost);
        }

        private void OnTurnTimerExpired()
        {
            if (_gameEnded) return;
            _inputBlocked = true;
            SendBoardSync(extraTurn: false);
            BeginOpponentTurn();
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
            }
            else if (_selX == x && _selY == y)
            {
                _boardView?.SetCellSelected(x, y, false);
                _selX = _selY = -1;
            }
            else
            {
                int px = _selX, py = _selY;
                _boardView?.SetCellSelected(px, py, false);
                _selX = _selY = -1;
                TrySwapCells(px, py, x, y);
            }
        }

        private void TrySwapCells(int x1, int y1, int x2, int y2)
        {
            if (!_board.TrySwap(x1, y1, x2, y2, out var initialMatches))
            {
                // Not adjacent or no matches → select the new cell instead
                _selX = x2; _selY = y2;
                _boardView?.SetCellSelected(x2, y2, true);
                return;
            }
            _inputBlocked = true;
            ResolveAndFinishTurn(initialMatches);
        }

        private void ExecuteAbility(AbilityType ability, int cx, int cy)
        {
            _pendingAbility = null;
            _abilityPanel?.ShowHint(false);

            if (_myStats.mana < AbilityCost) { _abilityPanel?.Refresh(_myStats, _isMyTurn, false, AbilityCost); return; }
            int cd = ability == AbilityType.Cross ? _myStats.crossCooldown : _myStats.squareCooldown;
            if (cd > 0) { _abilityPanel?.Refresh(_myStats, _isMyTurn, false, AbilityCost); return; }

            _myStats.mana -= AbilityCost;
            if (ability == AbilityType.Cross) _myStats.crossCooldown  = AbilityCooldown;
            else                              _myStats.squareCooldown = AbilityCooldown;

            _inputBlocked = true;
            _board.ApplyAbility(ability, cx, cy);
            ResolveAndFinishTurn(new List<MatchResult>());
        }

        // ─── Match Resolution ─────────────────────────────────────────────────────

        private void ResolveAndFinishTurn(List<MatchResult> initialMatches)
        {
            bool extraTurn = false;

            if (initialMatches != null && initialMatches.Count > 0)
            {
                ApplyMatchEffects(initialMatches, ref extraTurn);
                _board.ClearMatchedCells(initialMatches);
            }

            while (true)
            {
                var cascade = _board.ApplyGravityAndRefill();
                _boardView?.RefreshAll(_board);
                if (cascade.Count == 0) break;
                ApplyMatchEffects(cascade, ref extraTurn);
                _board.ClearMatchedCells(cascade);
            }

            _boardView?.RefreshAll(_board);
            RefreshStatsUI();
            _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, AbilityCost);

            if (CheckGameOver()) return;

            SendBoardSync(extraTurn);

            if (extraTurn) BeginMyTurn();
            else           BeginOpponentTurn();
        }

        private void ApplyMatchEffects(List<MatchResult> matches, ref bool extraTurn)
        {
            foreach (var m in matches)
            {
                if (m.count >= 5) extraTurn = true;
                switch (m.type)
                {
                    case PieceType.GemRed:
                    case PieceType.GemYellow:
                    case PieceType.GemGreen:
                        _myStats.mana = Mathf.Min(MaxMana, _myStats.mana + GemMana[(int)m.type] * m.count);
                        break;
                    case PieceType.Skull:
                        _opStats.hp = Mathf.Max(0, _opStats.hp - SkullDamage * m.count);
                        break;
                    case PieceType.Ankh:
                        _myStats.hp = Mathf.Min(MaxHp, _myStats.hp + AnkhHeal * m.count);
                        break;
                }
            }
        }

        private void TickAbilityCooldowns(PlayerStats stats)
        {
            if (stats.crossCooldown  > 0) stats.crossCooldown--;
            if (stats.squareCooldown > 0) stats.squareCooldown--;
        }

        // ─── Game Over ────────────────────────────────────────────────────────────

        private bool CheckGameOver()
        {
            if (_myStats.hp <= 0)
            {
                _gameEnded = true;
                _ = SendStateAsync(M3Op.GameOver,
                    Encoding.UTF8.GetBytes(JsonUtility.ToJson(new M3GameOverMsg { winnerUserId = _opUserId })));
                ShowGameOver(won: false);
                return true;
            }
            if (_opStats.hp <= 0)
            {
                _gameEnded = true;
                _ = SendStateAsync(M3Op.GameOver,
                    Encoding.UTF8.GetBytes(JsonUtility.ToJson(new M3GameOverMsg { winnerUserId = _myUserId })));
                ShowGameOver(won: true);
                return true;
            }
            return false;
        }

        private void ShowGameOver(bool won)
        {
            _isMyTurn    = false;
            _inputBlocked = true;
            _gameOverPanel?.Show(won);
        }

        // ─── Networking — Send ────────────────────────────────────────────────────

        private void SendBoardSync(bool extraTurn)
        {
            if (_match == null || _gameEnded) return;
            var ids  = GetSortedIds();
            bool amA = ids.Count > 0 && _myUserId == ids[0];

            var msg = new M3BoardSyncMsg
            {
                board     = _board.ToArray(),
                aHp       = amA ? _myStats.hp            : _opStats.hp,
                aMana     = amA ? _myStats.mana           : _opStats.mana,
                aCrossCd  = amA ? _myStats.crossCooldown  : _opStats.crossCooldown,
                aSquareCd = amA ? _myStats.squareCooldown : _opStats.squareCooldown,
                bHp       = amA ? _opStats.hp             : _myStats.hp,
                bMana     = amA ? _opStats.mana            : _myStats.mana,
                bCrossCd  = amA ? _opStats.crossCooldown  : _myStats.crossCooldown,
                bSquareCd = amA ? _opStats.squareCooldown : _myStats.squareCooldown,
                extraTurn    = extraTurn,
                activeUserId = _myUserId,
            };

            _ = SendStateAsync(M3Op.BoardSync,
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg)));
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
            if (state.UserPresence?.UserId == _myUserId) return;

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
        }

        private void OnBoardSyncReceived(M3BoardSyncMsg msg)
        {
            if (_gameEnded) return;

            _board.FromArray(msg.board);

            var ids  = GetSortedIds();
            bool amA = ids.Count > 0 && _myUserId == ids[0];

            _myStats.hp            = amA ? msg.aHp       : msg.bHp;
            _myStats.mana          = amA ? msg.aMana      : msg.bMana;
            _myStats.crossCooldown  = amA ? msg.aCrossCd  : msg.bCrossCd;
            _myStats.squareCooldown = amA ? msg.aSquareCd : msg.bSquareCd;
            _opStats.hp             = amA ? msg.bHp       : msg.aHp;
            _opStats.mana           = amA ? msg.bMana      : msg.aMana;

            _boardView?.RefreshAll(_board);
            RefreshStatsUI();
            _abilityPanel?.Refresh(_myStats, _isMyTurn, _gameEnded, AbilityCost);

            if (CheckGameOver()) return;

            if (!msg.extraTurn)
            {
                TickAbilityCooldowns(_myStats);
                BeginMyTurn();
            }
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

        private async void QuitToMenu()
        {
            _gameEnded = true;
            try
            {
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
            _searchingPanel = BuildOrInstantiate(searchingPanelPrefab, root, V2(0.25f, 0.35f), V2(0.75f, 0.65f));
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
            panel.manaText = MakeTxt(bg, "MpVal", "0/150", 12, Color.white, V2(0.60f, 0.47f), V2(0.97f, 0.52f));
            panel.manaFill = BuildBar(bg, "MpBar", new Color(0.14f, 0.35f, 0.82f), V2(0.05f, 0.42f), V2(0.95f, 0.47f));

            if (isLeft) BuildLegend(bg, V2(0.03f, 0.02f), V2(0.97f, 0.40f));

            return panel;
        }

        private Match3AbilityPanel BuildAbilityPanelProcedural(Transform parent)
        {
            var bg = MakePanel(parent, "AbilityPanel",
                new Color(0.09f, 0.09f, 0.17f, 0.97f), V2(0f, 0f), V2(1f, 1f));
            var ap = bg.gameObject.AddComponent<Match3AbilityPanel>();

            MakeTxt(bg, "Lbl", "Способности:", 12, new Color(0.75f, 0.75f, 0.85f),
                V2(0.05f, 0.82f), V2(0.95f, 1f));

            // Cross
            var crossBg = MakeButton(bg, "CrossBtn", "",
                new Color(0.28f, 0.14f, 0.48f), Color.white, V2(0.05f, 0.40f), V2(0.50f, 0.80f));
            ap.crossButton = crossBg;
            MakeTxt(crossBg.transform, "Icon", "✝ Крест\n5×5", 12, Color.white, V2(0.05f, 0.5f), V2(0.95f, 1f));
            ap.crossCooldownText = MakeTxt(crossBg.transform, "Cd", "20 мп", 11,
                new Color(0.9f, 0.85f, 0.5f), V2(0.05f, 0f), V2(0.95f, 0.48f));

            // Square
            var sqBg = MakeButton(bg, "SquareBtn", "",
                new Color(0.14f, 0.25f, 0.48f), Color.white, V2(0.52f, 0.40f), V2(0.97f, 0.80f));
            ap.squareButton = sqBg;
            MakeTxt(sqBg.transform, "Icon", "□ Кв-т\n3×3", 12, Color.white, V2(0.05f, 0.5f), V2(0.95f, 1f));
            ap.squareCooldownText = MakeTxt(sqBg.transform, "Cd", "20 мп", 11,
                new Color(0.9f, 0.85f, 0.5f), V2(0.05f, 0f), V2(0.95f, 0.48f));

            // Hint
            var hintGo = new GameObject("AbilityHint");
            var hintRt = hintGo.AddComponent<RectTransform>();
            hintRt.SetParent(bg, false);
            hintRt.anchorMin = V2(0.05f, 0f); hintRt.anchorMax = V2(0.95f, 0.38f);
            hintRt.offsetMin = hintRt.offsetMax = Vector2.zero;
            hintGo.AddComponent<Image>().color = new Color(0.45f, 0.30f, 0f, 0.85f);
            var hintTxt = MakeTxt(hintGo.transform, "HT", "☞ Кликните клетку", 12,
                Color.white, V2(0.03f, 0f), V2(0.97f, 1f));
            hintTxt.alignment = TextAnchor.MiddleCenter;
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
            var inner = MakeImg(frame, "Inner", new Color(0.17f, 0.15f, 0.11f), V2(0.025f, 0.015f), V2(0.975f, 0.985f));

            const int cells = Match3BoardLogic.Size, cellPx = 74, gapPx = 4;
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
            var bg = MakePanel(parent, "SearchingPanel", new Color(0f, 0f, 0f, 0.88f),
                V2(0.25f, 0.35f), V2(0.75f, 0.65f));
            var sp = bg.gameObject.AddComponent<Match3SearchingPanel>();
            sp.statusText = MakeTxt(bg, "ST", "Поиск соперника…", 22, Color.white,
                V2(0.05f, 0.35f), V2(0.95f, 0.70f));
            sp.statusText.alignment = TextAnchor.MiddleCenter;
            var btn = MakeButton(bg, "Cancel", "Отмена",
                new Color(0.45f, 0.12f, 0.12f), Color.white, V2(0.20f, 0.08f), V2(0.80f, 0.30f));
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

        private void OnCrossClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            if (_myStats.crossCooldown  > 0 || _myStats.mana < AbilityCost) return;
            _pendingAbility = AbilityType.Cross;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _abilityPanel?.ShowHint(true);
        }

        private void OnSquareClicked()
        {
            if (!_isMyTurn || _gameEnded || _inputBlocked) return;
            if (_myStats.squareCooldown > 0 || _myStats.mana < AbilityCost) return;
            _pendingAbility = AbilityType.Square;
            _selX = _selY = -1;
            _boardView?.ClearSelections();
            _abilityPanel?.ShowHint(true);
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
            var trackGo = new GameObject(name + "Track");
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.SetParent(p, false);
            trackRt.anchorMin = aMin; trackRt.anchorMax = aMax;
            trackRt.offsetMin = trackRt.offsetMax = Vector2.zero;
            trackGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.9f);

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
