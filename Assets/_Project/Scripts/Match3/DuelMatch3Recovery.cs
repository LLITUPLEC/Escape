using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Match3
{
    public sealed partial class DuelMatch3Manager
    {
        private NakamaBootstrap _battleBootstrap;
        private string _battleOwnerScope, _admissionRequestId, _admissionMatchId;
        private int _battleAuthenticationGeneration, _matchTransportGeneration;
        private bool _battleOwnerBound;
        private volatile bool _networkSnapshotPending, _snapshotTransportReady;
        private bool _cancelPreparationPending, _cancelPreparationIntent;
        private float _nextMatchRecovery;
        private Task _pendingLaunchTask;
        private Coroutine _matchRevealRoutine;
        private Action<IMatchState> _socketStateHandler;
        private Action<IMatchPresenceEvent> _socketPresenceHandler;
        private Action<IMatchmakerMatched> _socketMatchmakerHandler;
#if UNITY_EDITOR
        internal static Func<string, CancellationToken, Task<IMatch>> EditorJoinOverride;
        internal static Func<string, long, byte[], Task> EditorStateTransportOverride;
        internal static Action<string> EditorNavigationOverride;
        internal static Func<string, CancellationToken, Task<PublicPlayerAppearance.State>> EditorRecoveredAppearanceOverride;
#endif

        private async Task<IMatch> JoinRecoveredTransportAsync(string matchId, CancellationToken ct)
        {
#if UNITY_EDITOR
            if (EditorJoinOverride != null) return await EditorJoinOverride(matchId, ct);
#endif
            return await NakamaBootstrap.Instance.Socket.JoinMatchAsync(matchId);
        }
        private void LoadMatchReturnScene()
        {
            var scene = _isArenaTournamentMatch || !_isSoloBotMode ? "ArenaMenu" : Match3LaunchContext.ConsumeSoloReturnScene();
#if UNITY_EDITOR
            if (EditorNavigationOverride != null) { EditorNavigationOverride(scene); return; }
#endif
            SceneManager.LoadScene(scene);
        }

        private void QueueMatchCallback(IMatchState state, Action callback)
            => QueueMatchCallback(state.MatchId, callback);
        private void QueueMatchCallback(string matchId, Action callback)
        {
            var generation = Volatile.Read(ref _matchTransportGeneration);
            MainThreadDispatcher.Enqueue(() => { if (IsCurrentMatchCallback(matchId, generation)) callback(); });
        }

        private bool BattleOwnerIsCurrent => !_battleOwnerBound ||
            ReferenceEquals(_battleBootstrap, NakamaBootstrap.Instance) && _battleBootstrap != null &&
            _battleAuthenticationGeneration == _battleBootstrap.AuthenticationGeneration && _battleBootstrap.GameplaySessionAllowed &&
            LocalAccountCacheScope.IsCurrent(_battleOwnerScope) && _battleBootstrap.Session?.UserId == _myUserId;

        private void BindCurrentBattleOwner()
        {
            var current = Match3AccountOperation.Capture();
            if (!current.IsCurrent || _battleOwnerBound && !BattleOwnerIsCurrent)
                throw new OperationCanceledException("Battle belongs to another authenticated lifetime.");
            _battleBootstrap = current.Bootstrap; _battleOwnerScope = current.OwnerScope;
            _battleAuthenticationGeneration = current.Generation; _battleOwnerBound = true;
        }
        private bool IsCurrentMatchCallback(string matchId, int generation) => this != null && !_isLeavingToMenu &&
            BattleOwnerIsCurrent && generation == Volatile.Read(ref _matchTransportGeneration) && _match != null && _match.Id == matchId;

        private void SuspendBattlePresentation()
        {
            Interlocked.Increment(ref _matchTransportGeneration);
            _networkSnapshotPending = true; _snapshotTransportReady = false; _inputBlocked = true;
            if (_remoteSyncRoutine != null) { StopCoroutine(_remoteSyncRoutine); _remoteSyncRoutine = null; }
            _pendingBoardSyncs.Clear(); _silentBoardSyncs.Clear();
            _abilityFx?.CancelTransient(); _pendingAbility = null; _selX = _selY = -1;
            _boardView?.ClearSelections(); _boardView?.SetDimmed(true);
            _abilityPanel?.SetSelectedAbility(null); _abilityPanel?.ShowHint(false);
            RefreshAbilityUi(false, _gameEnded);
        }
        private void StopMatchReveal()
        {
            if (_matchRevealRoutine != null) { StopCoroutine(_matchRevealRoutine); _matchRevealRoutine = null; }
        }
        private void CompleteNetworkCheckpoint()
        {
            if (!_networkSnapshotPending || !_snapshotTransportReady || _cancelPreparationIntent || !BattleOwnerIsCurrent) return;
            _networkSnapshotPending = false;
            if (!_matchRevealPending) _searchingPanel?.Hide();
        }

        private async Task RecoverCurrentMatchAsync()
        {
            if (_reconnectInFlight || _gameEnded || _pendingGameOver || _useLocalBotSimulation || _match == null ||
                _cancelPreparationPending || !BattleOwnerIsCurrent) return;
            _reconnectInFlight = true;
            var matchId = _match.Id;
            SuspendBattlePresentation(); StopMatchReveal();
            var generation = _matchTransportGeneration;
            try
            {
                _searchingPanel?.Show("Восстановление связи…\nПроверяем состояние боя.");
                _searchingPanel?.SetCancelState(_matchRevealPending ? "Отмена" : "Сдаться", true);
                await Match3RecoveryService.RequireConnectionAsync(_cts.Token);
                if (!IsCurrentMatchCallback(matchId, generation)) return;
                Match3RecoveryReply recovery = null;
                if (!_isGlobalBossPractice)
                {
                    recovery = await Match3RecoveryService.RecoverAsync(matchId, null, _cts.Token);
                    if (!IsCurrentMatchCallback(matchId, generation)) return;
                    if (recovery == null || !recovery.ok) throw new InvalidOperationException(recovery?.err ?? "recovery_unconfirmed");
                    if (recovery.pending_admission) throw new InvalidOperationException("admission_pending");
                    if (!recovery.active || recovery.terminal) { FinishUnavailableMatch(); return; }
                    if (recovery.match_id != matchId) throw new InvalidOperationException("recovery_match_changed");
                    ApplyRecoveredMetadata(recovery);
                }
                var socket = NakamaBootstrap.Instance.Socket;
                ReplaceMatchSocketHooks(socket);
                var joined = await JoinRecoveredTransportAsync(matchId, _cts.Token);
                if (!IsCurrentMatchCallback(matchId, generation) || !ReferenceEquals(socket, NakamaBootstrap.Instance.Socket)) return;
                _match = joined; _snapshotTransportReady = true;
                if (recovery != null && !recovery.battle_started && recovery.can_cancel)
                {
                    _revealedMatchId = null;
                    BeginConfirmedMatchPresentation();
                }
                else
                {
                    _matchRevealPending = false;
                    RequestSnapshot();
                    if (_snapshotRetryRoutine != null) StopCoroutine(_snapshotRetryRoutine);
#if UNITY_EDITOR
                    if (!_offlinePresentationQa)
#endif
                        _snapshotRetryRoutine = StartCoroutine(RequestSnapshotUntilSynced());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (IsCurrentMatchCallback(matchId, generation))
                {
                    _searchingPanel?.Show("Связь ещё не восстановлена.\nПовторяем подключение к текущему бою…");
                    Debug.LogWarning("[Match3] Recovery: " + e.Message);
                }
            }
            finally { _nextMatchRecovery = Time.unscaledTime + 3; _reconnectInFlight = false; }
        }

        private void FinishUnavailableMatch()
        {
            if (_match != null) Match3RecoveryService.RememberTerminal(_match.Id);
            StopMatchReveal(); _matchRevealPending = false; _gameEnded = true; _match = null;
            _networkSnapshotPending = true; _inputBlocked = true;
            Match3LaunchContext.ClearRecoveredMatch();
            _searchingPanel?.Show("Бой уже завершён или отменён сервером.\nМожно вернуться в меню.");
            _searchingPanel?.SetCancelState("В меню", true);
        }

        private void ApplyRecoveredMetadata(Match3RecoveryReply info)
        {
            _admissionRequestId = string.IsNullOrEmpty(info.request_id) ? null : info.request_id;
            _isSoloBotMode = info.IsSoloBattle;
            _launchMode = _isSoloBotMode ? Match3LaunchMode.SoloBot : Match3LaunchMode.Multiplayer;
            _pvpRaceQueue = info.pvp_race; _pvpProQueue = info.pvp_pro && !_pvpRaceQueue;
            _isArenaTournamentMatch = !string.IsNullOrEmpty(info.arena_tournament_id);
            _isGlobalBoss = info.is_global_boss; _isGlobalBossPractice = false;
            _activePveBotFloor = info.is_global_boss ? 0 : Mathf.Clamp(info.floor, 1, 12);
            _activePveAffix = info.affix ?? string.Empty;
            _preferredSoloDifficulty = string.IsNullOrEmpty(info.difficulty) ? "easy" : info.difficulty;
            if (_isSoloBotMode)
            {
                Match3LaunchContext.SetSoloReturnContext(info.floor, info.is_global_boss ? "MainMenu" : "MineScene");
                _pveProgress ??= new PveProgressInfo(); _pveProgress.mine ??= new MineProgressInfo();
                _pveProgress.mine.current_difficulty = _preferredSoloDifficulty;
            }
            var previousOpponent = _opUserId;
            if (!string.IsNullOrEmpty(info.opponent_user_id)) _opUserId = info.opponent_user_id;
            else if (!string.IsNullOrEmpty(info.bot_user_id)) _opUserId = info.bot_user_id;
            _opponentIsServerBot = info.opponent_is_bot || !string.IsNullOrEmpty(_opUserId) && _opUserId.StartsWith("zz-bot-", StringComparison.Ordinal);
            _opDisplayName = !string.IsNullOrEmpty(info.opponent_name) ? info.opponent_name :
                !string.IsNullOrEmpty(info.bot_name) ? info.bot_name : !string.IsNullOrEmpty(info.name) ? info.name : "Соперник";
            // A discovered match may predate the recovery descriptor's bracket identities.
            // Keep the matching menu snapshot rather than replacing it with a mine bot or placeholder.
            if (_isArenaTournamentMatch && Match3LaunchContext.TryPeekArenaJoin(out var arenaMatch, out var arenaName, out var arenaBot, out var arenaUser)
                && string.Equals(arenaMatch, info.match_id, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(arenaName)) _opDisplayName = arenaName;
                if (string.IsNullOrEmpty(_opUserId) && !string.IsNullOrWhiteSpace(arenaUser)) _opUserId = arenaUser;
                _opponentIsServerBot |= arenaBot;
            }
            if (_isSoloBotMode || _opponentIsServerBot || !string.Equals(previousOpponent, _opUserId, StringComparison.Ordinal))
                _remoteAppearance = null;
            if (_isSoloBotMode && !_isGlobalBoss)
                _opDisplayName = MonsterNames.Resolve(_opDisplayName, presentationMonsterCatalog != null ? presentationMonsterCatalog.GetByFloor(_activePveBotFloor) : null, _activePveBotFloor);
            _myPanel?.SetPlayerName(ResolveMyDisplayName()); _opPanel?.SetPlayerName(_opDisplayName);
            ShowPveSelector(false); EnsureRaceHudApplied(); ApplyExpeditionPortraits();
            if (_isGlobalBoss) InstallGlobalBossHud();
            if (!_isSoloBotMode && !_opponentIsServerBot && !string.IsNullOrEmpty(_opUserId) && _cts != null)
                _ = RefreshRecoveredOpponentAppearanceAsync(_opUserId, _cts.Token);
        }

        private async Task RefreshRecoveredOpponentAppearanceAsync(string userId, CancellationToken ct)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(1500);
                Task<PublicPlayerAppearance.State> request;
#if UNITY_EDITOR
                if (EditorRecoveredAppearanceOverride != null) request = EditorRecoveredAppearanceOverride(userId, timeout.Token);
                else
#endif
                    request = PublicPlayerAppearance.GetAsync(userId, timeout.Token);
                var appearance = await request;
                if (this == null || ct.IsCancellationRequested || !BattleOwnerIsCurrent || _gameEnded || _isLeavingToMenu ||
                    _isSoloBotMode || _opponentIsServerBot || _opUserId != userId) return;
                _remoteAppearance = appearance;
                ApplyExpeditionPortraits();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning("[Match3] Recovered opponent appearance: " + e.Message); }
        }

        private async Task<bool> TryRestoreExistingMatchAsync(CancellationToken ct, bool explicitLaunch = false)
        {
            if (_isGlobalBossPractice) return false;
            Match3RecoveryReply reply;
            if (explicitLaunch && Match3LaunchContext.TryPeekRecoveredMatch(out var armed)) reply = armed;
            else
            {
                reply = await Match3RecoveryService.RecoverAsync(null, null, ct);
                if (reply == null || !reply.ok)
                    throw new InvalidOperationException(reply?.err ?? "recovery_unconfirmed");
            }
            if (!BattleOwnerIsCurrent) throw new OperationCanceledException("Authentication changed.", ct);
            if (reply.pending_admission)
            {
                // Race is paid in the arena menu before matchmaking. That unbound
                // receipt is permission to continue this explicit queue launch,
                // rather than an in-flight match creation that must be recovered.
                if (explicitLaunch && _pvpRaceQueue && !_isSoloBotMode && reply.pvp_race && reply.mode == "pvp" &&
                    !reply.active && !reply.terminal && string.IsNullOrEmpty(reply.match_id) && !string.IsNullOrEmpty(reply.request_id))
                {
                    _admissionRequestId = reply.request_id;
                    return false;
                }
                throw new InvalidOperationException("admission_pending");
            }
            if (!Match3RecoveryService.CanRestore(reply, _battleOwnerScope, _battleAuthenticationGeneration)) return false;
            ApplyRecoveredMetadata(reply);
            var socket = NakamaBootstrap.Instance.Socket;
            ReplaceMatchSocketHooks(socket);
            var match = await JoinRecoveredTransportAsync(reply.match_id, ct);
            if (!BattleOwnerIsCurrent || !ReferenceEquals(socket, NakamaBootstrap.Instance.Socket)) throw new OperationCanceledException("Transport changed.", ct);
            _match = match; _admissionMatchId = reply.match_id; _snapshotTransportReady = true;
            Match3LaunchContext.ClearRecoveredMatch();
            if (Match3LaunchContext.TryPeekArenaJoin(out var arenaMatchId, out _, out _, out _) && arenaMatchId == reply.match_id)
                Match3LaunchContext.ConsumeArenaJoinArm();
            if (reply.battle_started || reply.started && !reply.can_cancel)
            {
                _matchRevealPending = false; StartGameWaitingServer();
                _searchingPanel?.Show("Восстанавливаем текущий бой…");
                _searchingPanel?.SetCancelState("Сдаться", true);
            }
            else { _revealedMatchId = null; BeginConfirmedMatchPresentation(); }
            return true;
        }

        private void OnSearchCancelClicked()
        {
            if (_gameEnded || _pendingGameOver) { QuitToMenu(); return; }
            if (_matchRevealPending || _match == null || _cancelPreparationIntent) _ = CancelPreparationAsync();
            else { _searchingPanel?.Hide(); ShowQuitConfirmDialog(); }
        }

        private async Task CancelPreparationAsync()
        {
            if (_cancelPreparationPending || _isLeavingToMenu || !BattleOwnerIsCurrent) return;
            _cancelPreparationPending = _cancelPreparationIntent = _isLeavingToMenu = true;
            StopMatchReveal(); SuspendBattlePresentation();
            var cancelGeneration = _matchTransportGeneration;
            var confirmed = false;
            try
            {
                _searchingPanel?.Show("Отменяем подготовку боя…\nЖдём подтверждения сервера.");
                _searchingPanel?.SetCancelState("Отмена…", false);
                _mmTcs?.TrySetCanceled(); await CancelMatchmakerTicketAsync();
                var pending = _pendingLaunchTask;
                if (pending != null && !pending.IsCompleted)
                {
                    var finished = await Task.WhenAny(pending, Task.Delay(12000, _cts.Token));
                    if (finished != pending) throw new TimeoutException("Admission confirmation pending.");
                    try { await pending; } catch (OperationCanceledException) { } catch (Exception) { }
                }
                if (this == null || _cts.IsCancellationRequested || !BattleOwnerIsCurrent || cancelGeneration != _matchTransportGeneration) return;
                var matchId = _match?.Id ?? _admissionMatchId;
                if (_isGlobalBossPractice)
                {
                    if (!string.IsNullOrEmpty(matchId) && NakamaBootstrap.Instance?.Socket?.IsConnected == true)
                        await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(matchId, M3Op.PlayerLeft, Array.Empty<byte>());
                    confirmed = true;
                }
                else
                {
                    if (string.IsNullOrEmpty(matchId) && string.IsNullOrEmpty(_admissionRequestId))
                    {
                        // Never send a blind cancellation: a late request must not cancel a newer admission.
                        var current = await Match3RecoveryService.RecoverAsync(null, null, _cts.Token);
                        if (this == null || _cts.IsCancellationRequested || !BattleOwnerIsCurrent || cancelGeneration != _matchTransportGeneration) return;
                        if (current == null || !current.ok) throw new InvalidOperationException(current?.err ?? "cancel_lookup_unconfirmed");
                        matchId = current.match_id;
                        _admissionRequestId = string.IsNullOrEmpty(current.request_id) ? null : current.request_id;
                        if (string.IsNullOrEmpty(matchId) && string.IsNullOrEmpty(_admissionRequestId))
                        {
                            if (current.pending_admission || current.active) throw new InvalidOperationException("admission_reference_pending");
                            confirmed = true;
                        }
                    }
                    for (var attempt = 0; !confirmed && attempt < 3; attempt++)
                    {
                        var ack = await Match3RecoveryService.CancelAsync(matchId, _admissionRequestId, _cts.Token);
                        if (this == null || _cts.IsCancellationRequested || !BattleOwnerIsCurrent || cancelGeneration != _matchTransportGeneration) return;
                        if (ack != null && ack.ok && ack.accepted && ack.terminal && (!ack.refund_pending || ack.refund_recorded))
                        {
                            if (ack.progression != null) PlayerResourcesService.PatchCachedFromProgression(ack.progression);
                            else if (ack.progression_pending) _ = PlayerResourcesService.GetAsync(CancellationToken.None);
                            if (!string.IsNullOrEmpty(ack.match_id)) matchId = ack.match_id;
                            if (!string.IsNullOrEmpty(matchId)) Match3RecoveryService.RememberTerminal(matchId);
                            if (ack.refund_pending && ack.refund_recorded)
                            {
                                _searchingPanel?.Show("Возврат сохранён.\nОстаток будет зачислен после расхода ресурса.");
                                await Task.Delay(2000, _cts.Token);
                                if (this == null || _cts.IsCancellationRequested || !BattleOwnerIsCurrent || cancelGeneration != _matchTransportGeneration) return;
                            }
                            confirmed = true; break;
                        }
                        if (ack?.err == "battle_already_started" || ack?.err == "cancel_not_allowed")
                        {
                            _cancelPreparationIntent = false; _matchRevealPending = false;
                            _searchingPanel?.Show("Бой уже начался.\nДля выхода из боя подтвердите сдачу.");
                            await Task.Delay(1000, _cts.Token);
                            return;
                        }
                        if (ack == null || !ack.ok || !ack.refund_pending) throw new InvalidOperationException(ack?.err ?? "cancel_unconfirmed");
                        await Task.Delay(250, _cts.Token);
                    }
                }
                if (!confirmed) throw new InvalidOperationException("Refund confirmation pending.");
                _gameEnded = true;
                Match3LaunchContext.ClearRecoveredMatch(); Match3LaunchContext.ClearArenaJoinArm(); Match3LaunchContext.ClearFriendRaceJoinArm();
                if (_match != null && NakamaBootstrap.Instance?.Socket?.IsConnected == true)
                    try { await NakamaBootstrap.Instance.Socket.LeaveMatchAsync(_match.Id); } catch { }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (BattleOwnerIsCurrent) _searchingPanel?.Show("Отмена ещё не подтверждена.\nВосстановите связь и повторите отмену.");
                Debug.LogWarning("[Match3] Preparation cancel: " + e.Message);
            }
            finally
            {
                _cancelPreparationPending = _isLeavingToMenu = false;
                if (confirmed && BattleOwnerIsCurrent)
                    LoadMatchReturnScene();
                else if (_cancelPreparationIntent) _searchingPanel?.SetCancelState("Повторить отмену", true);
                else { _searchingPanel?.SetCancelState("Сдаться", true); _nextMatchRecovery = 0; }
            }
        }
    }
}
