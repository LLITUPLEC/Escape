using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Networking;
using Project.Player;
using Project.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Duel
{
    public sealed class DuelRoomManager : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;
        public GameObject PlayerPrefab
        {
            get => playerPrefab;
            set => playerPrefab = value;
        }

        [Header("Spawns")]
        [SerializeField] private Transform spawnLeft;
        [SerializeField] private Transform spawnRight;
        public void SetSpawns(Transform left, Transform right)
        {
            spawnLeft = left;
            spawnRight = right;
        }

        [Header("Networking")]
        [SerializeField] private float sendRateHz = 10f;
        [Header("UI")]
        [SerializeField] private DuelStatusUI statusUI;
        [SerializeField] private DuelHudController hud;

        public void SetStatusUI(DuelStatusUI ui) => statusUI = ui;
        public void SetHud(DuelHudController hudController) => hud = hudController;

        private readonly Dictionary<string, GameObject> _playersByUserId = new();
        private IMatch _match;
        private string _myUserId;
        private float _sendEvery;
        private float _nextSendAt;
        private NetworkTransformView _localView;
        private CancellationTokenSource _cts;
        private Vector3 _spawnLeftPos;
        private Vector3 _spawnRightPos;
        private bool _spawnedLocal;
        private bool _preferLeftUntilRemoteKnown = true;
        private string _opponentUserId;
        private bool _sidesFinal;
        private bool _isQuitting;
        private bool _matchEnded;

        private async void Start()
        {
            _cts = new CancellationTokenSource();
            _sendEvery = sendRateHz <= 0f ? 0.1f : (1f / sendRateHz);
            if (statusUI != null)
            {
                statusUI.SetVisible(true);
                statusUI.SetText("Поиск соперника…");
            }

            if (playerPrefab == null)
            {
                Debug.LogError("Player prefab не назначен в DuelRoomManager.");
                return;
            }

            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                _myUserId = NakamaBootstrap.Instance.Session.UserId;

                HookSocket(NakamaBootstrap.Instance.Socket);
                _spawnLeftPos = spawnLeft != null ? spawnLeft.position : new Vector3(-2f, 0f, -19f);
                _spawnRightPos = spawnRight != null ? spawnRight.position : new Vector3(2f, 0f, -19f);
                await FindMatchAndJoinAsync(_cts.Token);

                if (statusUI != null) statusUI.SetVisible(false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (NakamaBootstrap.Instance != null && NakamaBootstrap.Instance.Socket != null)
            {
                UnhookSocket(NakamaBootstrap.Instance.Socket);
            }
        }

        private void Update()
        {
            if (_match == null || _localView == null) return;
            if (Time.unscaledTime < _nextSendAt) return;

            _nextSendAt = Time.unscaledTime + _sendEvery;
            _ = SendTransformAsync(_localView.transform);
        }

        private void HookSocket(ISocket socket)
        {
            socket.ReceivedMatchmakerMatched += OnMatchmakerMatched;
            socket.ReceivedMatchPresence += OnMatchPresence;
            socket.ReceivedMatchState += OnMatchState;
        }

        private void UnhookSocket(ISocket socket)
        {
            socket.ReceivedMatchmakerMatched -= OnMatchmakerMatched;
            socket.ReceivedMatchPresence -= OnMatchPresence;
            socket.ReceivedMatchState -= OnMatchState;
        }

        private TaskCompletionSource<IMatchmakerMatched> _mmTcs;

        private async Task FindMatchAndJoinAsync(CancellationToken ct)
        {
            _mmTcs = new TaskCompletionSource<IMatchmakerMatched>();

            // Любые игроки, 2 человека.
            ct.ThrowIfCancellationRequested();
            Debug.Log("[Duel] Matchmaker: enqueue ticket...");
            await NakamaBootstrap.Instance.Socket.AddMatchmakerAsync(query: "*", minCount: 2, maxCount: 2);

            var matched = await _mmTcs.Task;
            _mmTcs = null;

            ct.ThrowIfCancellationRequested();
            Debug.Log($"[Duel] Matchmaker matched. Joining match...");
            _match = await NakamaBootstrap.Instance.Socket.JoinMatchAsync(matched);
            Debug.Log($"[Duel] Joined match: {_match.Id}. Presences: {CountPresences(_match.Presences)}");
            // В некоторых реализациях SDK список presences может не содержать себя или быть пустым.
            // Локального игрока спавним всегда.
            EnsureLocalSpawn();
            SpawnInitialPresences(_match);
        }

        private void OnMatchmakerMatched(IMatchmakerMatched matched)
        {
            // Здесь уже есть список участников матча (минимум 2). Используем это, чтобы
            // определить стороны ДО JoinMatch/спавна, иначе первый тик синка может утянуть remote в неправильный спавн.
            try
            {
                var ids = new List<string>();
                if (matched?.Users != null)
                {
                    foreach (var u in matched.Users) ids.Add(u.Presence.UserId);
                }
                ids.Sort(StringComparer.Ordinal);

                if (ids.Count >= 2 && !string.IsNullOrEmpty(_myUserId))
                {
                    _opponentUserId = ids[0] == _myUserId ? ids[1] : ids[0];
                    _sidesFinal = true;
                }
            }
            catch
            {
                // ignore, fallback ниже
            }

            _mmTcs?.TrySetResult(matched);
        }

        private void SpawnInitialPresences(IMatch match)
        {
            // Решаем, кто слева/справа — детерминированно по UserId (когда знаем оба userId).
            var ids = new List<string>();
            if (match.Presences != null)
            {
                foreach (var p in match.Presences) ids.Add(p.UserId);
            }
            if (!ids.Contains(_myUserId) && !string.IsNullOrEmpty(_myUserId)) ids.Add(_myUserId);
            ids.Sort(StringComparer.Ordinal);

            foreach (var userId in ids)
            {
                var isMe = userId == _myUserId;
                var sideIdx = ids.IndexOf(userId); // 0 или 1
                var spawnPos = sideIdx == 0 ? _spawnLeftPos : _spawnRightPos;
                SpawnPlayer(userId, spawnPos, isMe);
            }

            // Если знаем обоих, больше не держим "временно слева".
            if (ids.Count >= 2) _preferLeftUntilRemoteKnown = false;
        }

        private void EnsureLocalSpawn()
        {
            if (_spawnedLocal) return;
            if (string.IsNullOrEmpty(_myUserId)) return;
            _spawnedLocal = true;

            var pos = GetSpawnFor(_myUserId);
            SpawnPlayer(_myUserId, pos, isLocal: true);
        }

        private void SpawnPlayer(string userId, Vector3 position, bool isLocal)
        {
            if (_playersByUserId.ContainsKey(userId)) return;

            var go = Instantiate(playerPrefab, position, Quaternion.identity);
            go.name = isLocal ? "Player (Local)" : $"Player ({userId})";

            var view = go.GetComponent<NetworkTransformView>();
            if (view != null) view.SetLocal(isLocal);

            var mover = go.GetComponent<PlayerMovementController>();
            if (mover != null) mover.enabled = isLocal;

            _playersByUserId[userId] = go;

            if (isLocal)
            {
                _localView = view;
                EnsureCameraFollows(go.transform);
            }
        }

        private void EnsureCameraFollows(Transform target)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = FindAnyObjectByType<Camera>();
            }
            if (cam == null)
            {
                Debug.LogWarning("[Duel] Камера не найдена для follow.");
                return;
            }
            var follow = cam.GetComponent<SimpleFollowCamera>();
            if (follow == null) follow = cam.gameObject.AddComponent<SimpleFollowCamera>();
            follow.SetTarget(target);
        }

        private void OnMatchPresence(IMatchPresenceEvent e)
        {
            // Копируем данные события сразу, чтобы не тащить "e" в главный поток (может быть не thread-safe).
            var matchId = e.MatchId;
            var joins = new string[CountPresences(e.Joins)];
            var leaves = new string[CountPresences(e.Leaves)];
            var ji = 0;
            var li = 0;
            if (e.Joins != null) foreach (var p in e.Joins) joins[ji++] = p.UserId;
            if (e.Leaves != null) foreach (var p in e.Leaves) leaves[li++] = p.UserId;

            // ВАЖНО: Nakama socket callbacks могут приходить не из main thread.
            // Любые Unity API (Transform/Instantiate/Destroy) делаем через диспетчер.
            MainThreadDispatcher.Enqueue(() =>
            {
                if (_match == null || matchId != _match.Id) return;

                Debug.Log($"[Duel] Presence event. Joins={joins.Length} Leaves={leaves.Length}");

                foreach (var leaveUserId in leaves)
                {
                    if (string.IsNullOrEmpty(leaveUserId)) continue;
                    if (_playersByUserId.TryGetValue(leaveUserId, out var go))
                    {
                        Destroy(go);
                        _playersByUserId.Remove(leaveUserId);
                    }

                    if (leaveUserId != _myUserId)
                    {
                        OnOpponentLeft();
                    }
                }

                // Если кто-то зашёл позже, спавним и пересчитываем стороны.
                foreach (var joinUserId in joins)
                {
                    if (string.IsNullOrEmpty(joinUserId)) continue;
                    if (joinUserId == _myUserId) continue;
                    Debug.Log($"[Duel] Remote joined: {joinUserId}");
                    if (string.IsNullOrEmpty(_opponentUserId)) _opponentUserId = joinUserId;
                    SpawnPlayer(joinUserId, GetSpawnFor(joinUserId), isLocal: false);

                    // Когда появился remote, фиксируем стороны детерминированно.
                    _preferLeftUntilRemoteKnown = false;
                    ReassignSidesIfNeeded(joinUserId);
                }
            });
        }

        private void ReassignSidesIfNeeded(string remoteUserId)
        {
            if (string.IsNullOrEmpty(_myUserId) || string.IsNullOrEmpty(remoteUserId)) return;
            if (!_playersByUserId.TryGetValue(_myUserId, out var me)) return;

            // Меньший userId -> слева.
            var myIsLeft = string.CompareOrdinal(_myUserId, remoteUserId) < 0;
            var desired = myIsLeft ? _spawnLeftPos : _spawnRightPos;
            var delta = (me.transform.position - desired).sqrMagnitude;
            if (delta > 0.25f)
            {
                me.transform.position = desired;
            }
        }

        private Vector3 GetSpawnFor(string userId)
        {
            // Пока не знаем оппонента — локального держим слева (чтобы не прыгал).
            if (string.IsNullOrEmpty(_opponentUserId) || string.IsNullOrEmpty(userId))
            {
                return _preferLeftUntilRemoteKnown ? _spawnLeftPos : _spawnRightPos;
            }

            // Детерминированно: меньший userId слева.
            var leftUserId = string.CompareOrdinal(userId, _opponentUserId) < 0 ? userId : _opponentUserId;
            return userId == leftUserId ? _spawnLeftPos : _spawnRightPos;
        }

        private static int CountPresences(IEnumerable<IUserPresence> presences)
        {
            if (presences == null) return 0;
            var c = 0;
            foreach (var _ in presences) c++;
            return c;
        }

        private void OnMatchState(IMatchState state)
        {
            if (_match == null || state.MatchId != _match.Id) return;
            if (state.OpCode == OpCodes.PlayerLeft)
            {
                if (state.UserPresence != null && state.UserPresence.UserId != _myUserId)
                {
                    MainThreadDispatcher.Enqueue(OnOpponentLeft);
                }
                return;
            }
            if (state.OpCode != OpCodes.Transform) return;
            if (state.UserPresence == null) return;
            if (state.UserPresence.UserId == _myUserId) return;

            // Разбор можно сделать в фоне, но применение к Transform — только в main thread.
            NetTransformState msg = null;
            try
            {
                var json = Encoding.UTF8.GetString(state.State);
                msg = JsonUtility.FromJson<NetTransformState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            if (msg == null) return;

            var userId = state.UserPresence.UserId;
            MainThreadDispatcher.Enqueue(() =>
            {
                if (!_playersByUserId.TryGetValue(userId, out var go)) return;
                var view = go.GetComponent<NetworkTransformView>();
                if (view == null) return;
                view.SetTarget(msg);
            });
        }

        private async Task SendTransformAsync(Transform t)
        {
            if (_match == null || _matchEnded || _isQuitting) return;
            if (!NakamaBootstrap.Instance.Socket.IsConnected) return;

            var msg = NetTransformState.From(t);
            var json = JsonUtility.ToJson(msg);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(_match.Id, OpCodes.Transform, bytes);
            }
            catch
            {
                // на старте могут быть гонки при загрузке/выходе из сцены — тут можно молча игнорировать
            }
        }

        public async void QuitMatchAndReturnToMenu()
        {
            if (_isQuitting) return;
            _isQuitting = true;
            Debug.Log("[Duel] Quitting match -> MainMenu");

            try
            {
                if (_match != null &&
                    NakamaBootstrap.Instance != null &&
                    NakamaBootstrap.Instance.Socket != null &&
                    NakamaBootstrap.Instance.Socket.IsConnected)
                {
                    // Сообщим оппоненту, что мы вышли (чтобы он сразу увидел победу).
                    try
                    {
                        await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(_match.Id, OpCodes.PlayerLeft, Array.Empty<byte>());
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        await NakamaBootstrap.Instance.Socket.LeaveMatchAsync(_match.Id);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            finally
            {
                _match = null;
                SceneManager.LoadScene("MainMenu");
            }
        }

        private void OnOpponentLeft()
        {
            if (_matchEnded) return;
            _matchEnded = true;

            if (_localView != null)
            {
                var mover = _localView.GetComponent<PlayerMovementController>();
                if (mover != null) mover.enabled = false;
            }

            if (hud != null)
            {
                hud.ShowBanner("Победа!\nСоперник вышел.");
            }
            else if (statusUI != null)
            {
                statusUI.SetVisible(true);
                statusUI.SetText("Победа! Соперник вышел.");
            }
        }

        public void LeaveToMainMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }
    }
}

