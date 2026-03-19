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
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
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
        [Tooltip("Если назначен — инстанциирует этот префаб вместо генерации UI из кода")]
        [SerializeField] private GameObject keypadModalPrefab;

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
        private bool _isQuitting;
        private bool _matchEnded;

        private DuelKeypadModal _keypadModal;

        private readonly Dictionary<int, Door> _doorsById = new();
        private bool[] _doorOpenedById;

        private GameObject _finishSphere;
        private bool _finishSphereCreated;

        private PlayerMovementController _localMover;

        private const float KeypadInteractDistance = 1f;
        private const int LeftDoor1Id = 1;
        private const int LeftDoor2Id = 2;
        private const int RightDoor1Id = 3;
        private const int RightDoor2Id = 4;

        private const string PinDoor1 = "27";  // 2 цифры
        private const string PinDoor2 = "441"; // 3 цифры

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

            // Двери управляются через код — отключаем автотаймеры.
            InitializeDoorsAndDisableTimers();
            // В конце коридора создаём “сферу победы”.
            EnsureFinishSphere();

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
            if (_localView != null)
            {
                HandleLocalKeypadHotkeyF();
                HandleLocalKeypadClick();
            }

            if (_match == null || _localView == null) return;
            if (_matchEnded) return;
            if (Time.unscaledTime < _nextSendAt) return;

            _nextSendAt = Time.unscaledTime + _sendEvery;
            _ = SendTransformAsync(_localView.transform);
        }

        private void HandleLocalKeypadHotkeyF()
        {
            if (_matchEnded) return;
            if (_doorOpenedById == null || _doorOpenedById.Length < 5) return;

            if (_localMover == null && _localView != null) _localMover = _localView.GetComponent<PlayerMovementController>();
            if (_localMover == null) return;

            var pressedF = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            pressedF = kb != null && kb.fKey.wasPressedThisFrame;
#else
            pressedF = Input.GetKeyDown(KeyCode.F);
#endif
            if (!pressedF) return;

            // Если модалка уже открыта — F закрывает её и возвращает управление.
            if (_keypadModal != null && _keypadModal.IsOpen)
            {
                _keypadModal.Close();
                return;
            }

            // Ищем ближайшую "сферу домофона" в радиусе.
            // Временно игнорируем ограничение по расстоянию: пусть ближайшая кнопка находится "где угодно" в комнате.
            var nearest = FindNearestKeypadSphere(_localView.transform.position, 1000f);
            if (nearest == null) return;

            var cubeTf = nearest.transform.parent;
            if (cubeTf == null) return;
            if (!TryGetDoorIdFromCubeName(cubeTf.name, out var doorId, out var pinCode, out var codeLen)) return;
            if (_doorOpenedById[doorId]) return;

            _localMover.enabled = false;
            EnsureKeypadModal();

            _keypadModal.Show(pinCode, codeLen, () =>
            {
                OpenDoorAndSync(doorId, sendNetwork: true);
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            }, doorId: doorId, onClosed: () =>
            {
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            });
        }

        private SphereCollider FindNearestKeypadSphere(Vector3 origin, float radius)
        {
            var cols = Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Collide);
            var best = (SphereCollider)null;
            var bestDist = float.PositiveInfinity;

            foreach (var c in cols)
            {
                if (c == null) continue;
                // Нам нужна именно "Sphere" внутри Cube.
                var sphereTf = c.transform;
                while (sphereTf != null && !sphereTf.name.StartsWith("Sphere", StringComparison.Ordinal)) sphereTf = sphereTf.parent;
                if (sphereTf == null) continue;

                var dist = Vector3.Distance(origin, sphereTf.position);

                if (c is SphereCollider sc)
                {
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = sc;
                    }
                }
            }

            return best;
        }

        private void InitializeDoorsAndDisableTimers()
        {
            // Массив индексов 1..4 (двери в префабе).
            _doorOpenedById = new bool[5];
            _doorsById.Clear();

            foreach (var timer in FindObjectsByType<TimerOpenCondition>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                timer.enabled = false;
            }

            foreach (var door in FindObjectsByType<Door>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var n = door.name;
                var id =
                    n.Contains("Door_L_1") ? LeftDoor1Id :
                    n.Contains("Door_L_2") ? LeftDoor2Id :
                    n.Contains("Door_R_1") ? RightDoor1Id :
                    n.Contains("Door_R_2") ? RightDoor2Id :
                    0;
                if (id != 0) _doorsById[id] = door;
            }
        }

        private void EnsureFinishSphere()
        {
            if (_finishSphereCreated) return;
            if (_doorsById.Count == 0) return;
            if (!_doorsById.TryGetValue(LeftDoor2Id, out var doorL2)) return;
            if (!_doorsById.TryGetValue(RightDoor2Id, out var doorR2)) return;

            var mid = (doorL2.transform.position + doorR2.transform.position) * 0.5f;
            // Сдвигаем “за” вторую дверь по оси Z (коридор собран вдоль Z).
            mid += Vector3.forward * 8f;
            mid.y = Mathf.Max(mid.y, 0.5f);

            _finishSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _finishSphere.name = "FinishSphere";
            _finishSphere.transform.position = mid;
            _finishSphere.transform.localScale = Vector3.one;

            var sphereCollider = _finishSphere.GetComponent<SphereCollider>();
            if (sphereCollider != null) sphereCollider.isTrigger = true;

            var rb = _finishSphere.GetComponent<Rigidbody>();
            if (rb == null) rb = _finishSphere.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var trigger = _finishSphere.GetComponent<DuelFinishTrigger>();
            if (trigger == null) trigger = _finishSphere.AddComponent<DuelFinishTrigger>();
            trigger.SetRoom(this);

            _finishSphereCreated = true;
        }

        private void HandleLocalKeypadClick()
        {
            if (_matchEnded) return;
            if (_doorOpenedById == null || _doorOpenedById.Length < 5) return;

            EnsureKeypadModal();
            if (_keypadModal.IsOpen) return;

            if (_localMover == null && _localView != null) _localMover = _localView.GetComponent<PlayerMovementController>();
            if (_localMover == null) return;

            if (!TryGetClick(out var clickPos))
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(clickPos);
            if (!Physics.Raycast(ray, out var hit, 10f, ~0, QueryTriggerInteraction.Collide))
                return;

            if (hit.transform == null) return;

            var sphereTf = hit.transform;
            while (sphereTf != null && !sphereTf.name.StartsWith("Sphere", StringComparison.Ordinal)) sphereTf = sphereTf.parent;
            if (sphereTf == null)
            {
                // На всякий случай: если клик попал в коллайдер самого Cube, пробуем найти дочернюю Sphere.
                if (hit.transform.name.StartsWith("Cube_", StringComparison.Ordinal) || hit.transform.name.StartsWith("Cube_left_", StringComparison.Ordinal))
                {
                    sphereTf = hit.transform.Find("Sphere");
                }
                if (sphereTf == null) return;
            }
            var cubeTf = sphereTf.parent;
            if (cubeTf == null) return;

            var cubeName = cubeTf.name;
            if (!TryGetDoorIdFromCubeName(cubeName, out var doorId, out var pinCode, out var codeLen))
                return;

            if (_doorOpenedById[doorId]) return;

            // “Домофон” блокирует управление, пока ввод не будет успешным.
            _localMover.enabled = false;
            _keypadModal.Show(pinCode, codeLen, () =>
            {
                OpenDoorAndSync(doorId, sendNetwork: true);
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            }, doorId: doorId, onClosed: () =>
            {
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            });
        }

        private static bool TryGetClick(out Vector2 position)
        {
            position = default;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return false;
            if (!mouse.leftButton.wasPressedThisFrame) return false;

            position = mouse.position.ReadValue();
            return true;
#else
            if (!Input.GetMouseButtonDown(0)) return false;
            position = Input.mousePosition;
            return true;
#endif
        }

        private bool TryGetDoorIdFromCubeName(string cubeName, out int doorId, out string pinCode, out int codeLen)
        {
            doorId = 0;
            pinCode = null;
            codeLen = 0;

            // Ожидаем имена вида: Cube_left_1 / Cube_left_2 / Cube_right_1 / Cube_right_2
            if (string.IsNullOrEmpty(cubeName)) return false;

            var lastUnd = cubeName.LastIndexOf('_');
            if (lastUnd < 0 || lastUnd + 1 >= cubeName.Length) return false;
            var tail = cubeName.Substring(lastUnd + 1);
            var digits = new StringBuilder(4);
            foreach (var ch in tail)
            {
                if (ch >= '0' && ch <= '9') digits.Append(ch);
            }
            if (digits.Length == 0) return false;
            if (!int.TryParse(digits.ToString(), out var idx)) return false;

            var isLeft = cubeName.Contains("Cube_left_");
            var isRight = cubeName.Contains("Cube_right_");
            if (!isLeft && !isRight) return false;

            if (isLeft)
            {
                doorId = idx == 1 ? LeftDoor1Id : idx == 2 ? LeftDoor2Id : 0;
            }
            else if (isRight)
            {
                doorId = idx == 1 ? RightDoor1Id : idx == 2 ? RightDoor2Id : 0;
            }

            if (doorId == 0) return false;

            var isDoor1 = doorId == LeftDoor1Id || doorId == RightDoor1Id;
            pinCode = isDoor1 ? PinDoor1 : PinDoor2;
            codeLen = isDoor1 ? 2 : 3;
            return true;
        }

        private void EnsureKeypadModal()
        {
            if (_keypadModal != null) return;

            if (keypadModalPrefab != null)
            {
                var go = Instantiate(keypadModalPrefab, transform, false);
                go.name = "KeypadModal";
                _keypadModal = go.GetComponent<DuelKeypadModal>();
            }

            if (_keypadModal == null)
            {
                var go = new GameObject("DuelKeypadModal");
                go.transform.SetParent(transform, false);
                _keypadModal = go.AddComponent<DuelKeypadModal>();
            }
        }

        private void OpenDoorAndSync(int doorId, bool sendNetwork)
        {
            if (_doorOpenedById == null || doorId <= 0 || doorId >= _doorOpenedById.Length) return;
            if (_doorOpenedById[doorId]) return;

            if (_doorsById.TryGetValue(doorId, out var door) && door != null)
            {
                door.SetOpen(true, instant: true);
                _doorOpenedById[doorId] = true;
            }

            if (sendNetwork && !_matchEnded)
            {
                _ = SendDoorOpenedAsync(doorId);
            }
        }

        private async Task SendDoorOpenedAsync(int doorId)
        {
            if (_match == null) return;
            if (!NakamaBootstrap.Instance.Socket.IsConnected) return;

            var msg = new NetDoorOpenedState { doorId = doorId };
            var json = JsonUtility.ToJson(msg);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(_match.Id, OpCodes.DoorOpened, bytes);
            }
            catch
            {
                // ignore
            }
        }

        public void NotifyPlayerReachedFinish(string winnerUserId)
        {
            EndMatch(winnerUserId, sendNetwork: string.Equals(winnerUserId, _myUserId, StringComparison.Ordinal));
        }

        private async void EndMatch(string winnerUserId, bool sendNetwork)
        {
            if (_matchEnded) return;
            _matchEnded = true;

            if (_keypadModal != null && _keypadModal.IsOpen) _keypadModal.Close();

            if (_localMover != null) _localMover.enabled = false;

            var isWinner = string.Equals(winnerUserId, _myUserId, StringComparison.Ordinal);
            var text = isWinner ? "Победа!" : "Поражение!";
            if (hud != null) hud.ShowBanner(text);
            else if (statusUI != null)
            {
                statusUI.SetVisible(true);
                statusUI.SetText(text);
            }

            if (sendNetwork)
            {
                await SendMatchWonAsync(winnerUserId);
            }
        }

        private async Task SendMatchWonAsync(string winnerUserId)
        {
            if (_match == null) return;
            if (!NakamaBootstrap.Instance.Socket.IsConnected) return;

            var msg = new NetMatchWonState { winnerUserId = winnerUserId };
            var json = JsonUtility.ToJson(msg);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await NakamaBootstrap.Instance.Socket.SendMatchStateAsync(_match.Id, OpCodes.MatchWon, bytes);
            }
            catch
            {
                // ignore
            }
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
                    // стороны фиксируются после получения userIds
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

            var identity = go.GetComponent<DuelPlayerIdentity>();
            if (identity == null) identity = go.AddComponent<DuelPlayerIdentity>();
            identity.Set(userId, isLocal);

            var view = go.GetComponent<NetworkTransformView>();
            if (view != null) view.SetLocal(isLocal);

            var mover = go.GetComponent<PlayerMovementController>();
            if (mover != null) mover.enabled = isLocal;

            _playersByUserId[userId] = go;

            if (isLocal)
            {
                _localView = view;
                _localMover = mover;
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

            if (state.OpCode == OpCodes.DoorOpened)
            {
                if (_matchEnded) return;

                NetDoorOpenedState doorMsg = null;
                try
                {
                    var json = Encoding.UTF8.GetString(state.State);
                    doorMsg = JsonUtility.FromJson<NetDoorOpenedState>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    return;
                }

                if (doorMsg == null) return;
                var doorId = doorMsg.doorId;

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_matchEnded) return;
                    OpenDoorAndSync(doorId, sendNetwork: false);
                });
                return;
            }

            if (state.OpCode == OpCodes.MatchWon)
            {
                if (_matchEnded) return;

                NetMatchWonState wonMsg = null;
                try
                {
                    var json = Encoding.UTF8.GetString(state.State);
                    wonMsg = JsonUtility.FromJson<NetMatchWonState>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    return;
                }

                if (wonMsg == null) return;
                var winnerUserId = wonMsg.winnerUserId;

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_matchEnded) return;
                    EndMatch(winnerUserId, sendNetwork: false);
                });
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

            if (_keypadModal != null && _keypadModal.IsOpen) _keypadModal.Close();

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

