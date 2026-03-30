using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
using Project.Nakama;
using Project.Networking;
using Project.Utils;
using Project.Player;
using UnityEngine;
using Project;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;
using NavKeypad;

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
        [SerializeField] private Match3SearchingPanel searchingPanelPrefab;
        [Tooltip("Если назначен — инстанциирует этот префаб вместо генерации UI из кода")]
        [SerializeField] private GameObject keypadModalPrefab;

        [Header("3D Keypad (фокус камеры)")]
        [SerializeField] private float keypadCameraLerpSeconds = 0.55f;
        [SerializeField] private float keypadCameraDistance = 0.5f;
        [Tooltip("Панель «быки : коровы» справа от клавиатуры")]
        [SerializeField] private bool showKeypadWorldAttemptLog = true;
        [Header("Mobile")]
        [SerializeField] private bool showMobileExitFromKeypad = true;

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
        private string _duelMatchmakerTicket;
        private bool _isQuitting;
        private bool _matchEnded;
        private bool _isSoloMode;
        private Coroutine _searchCountdownRoutine;
        private Text _searchCountdownText;

        private DuelKeypadModal _keypadModal;
        private Match3SearchingPanel _searchingPanel;

        private readonly Dictionary<int, Door> _doorsById = new();
        private readonly Dictionary<int, SlidingDoor> _slidingDoorsById = new();
        private bool[] _doorOpenedById;

        private GameObject _finishSphere;
        private bool _finishSphereCreated;

        private PlayerMovementController _localMover;

        private bool _keypadFocusActive;
        private Keypad _activeKeypad;
        private int _activeDoorId;
        private UnityAction _activeKeypadGrantedHandler;
        private Action<string, int, int> _activeKeypadWrongGuessHandler;
        private Vector3 _camWorldPosRestore;
        private Quaternion _camWorldRotRestore;
        private Transform _camParentRestore;
        private SimpleFollowCamera _playerFollowCam;
        private DuelCameraDragOrbit _duelCameraDragOrbit;
        private KeypadInteractionFPV _runtimeKeypadFpv;
        private bool _runtimeKeypadFpvOwned;
        private Coroutine _keypadFocusCameraCo;
        private GameObject _keypadWorldLogGo;
        private bool _keypadGuessInFlight;
        private readonly Dictionary<int, List<string>> _attemptHistoryByDoor = new();
        private CanvasGroup _mobileExitKeypadGroup;
        private Button _mobileExitKeypadButton;
        private readonly Dictionary<int, string> _soloPinsByDoor = new();
        private readonly System.Random _soloRng = new();

        private const float KeypadInteractDistance = 2.6f;
        private const int DuelMatchmakingTimeoutSeconds = 30;
        private const int LeftDoor1Id = DuelDoorPins.LeftDoor1Id;
        private const int LeftDoor2Id = DuelDoorPins.LeftDoor2Id;
        private const int RightDoor1Id = DuelDoorPins.RightDoor1Id;
        private const int RightDoor2Id = DuelDoorPins.RightDoor2Id;
        private const int MaxDoorAttempts = DuelKeypadWorldLog.MaxAttempts;

        private async void Start()
        {
            _cts = new CancellationTokenSource();
            _sendEvery = sendRateHz <= 0f ? 0.1f : (1f / sendRateHz);
            if (statusUI != null) statusUI.SetVisible(false);
            ShowMatchmakingOverlay("Поиск соперника...");

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

                HideMatchmakingOverlay();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowMatchmakingOverlay("Ошибка подключения.\nПроверьте интернет и нажмите Отмена.");
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
            _ = CancelDuelMatchmakerTicketAsync();

            if (_searchingPanel != null)
                _searchingPanel.OnCancelClicked -= OnSearchCancelClicked;
            StopSearchCountdown();
        }

        private void ShowMatchmakingOverlay(string text)
        {
            EnsureSearchingPanel();
            EnsureSearchCountdownText();
            _searchingPanel?.Show(text);
        }

        private void HideMatchmakingOverlay()
        {
            StopSearchCountdown();
            if (_searchCountdownText != null)
                _searchCountdownText.gameObject.SetActive(false);
            _searchingPanel?.Hide();
        }

        private void StartSearchCountdown(string baseText, int timeoutSeconds)
        {
            StopSearchCountdown();
            _searchCountdownRoutine = StartCoroutine(SearchCountdownRoutine(baseText, timeoutSeconds));
        }

        private void StopSearchCountdown()
        {
            if (_searchCountdownRoutine == null) return;
            StopCoroutine(_searchCountdownRoutine);
            _searchCountdownRoutine = null;
        }

        private IEnumerator SearchCountdownRoutine(string baseText, int timeoutSeconds)
        {
            var left = Mathf.Max(1, timeoutSeconds);
            while (left > 0)
            {
                _searchingPanel?.Show($"{baseText}\nАвто-переход в соло через {left}с");
                SetCountdownText(left);
                yield return new WaitForSecondsRealtime(1f);
                left--;
            }
            SetCountdownText(0);
            _searchCountdownRoutine = null;
        }

        private void SetCountdownText(int secondsLeft)
        {
            EnsureSearchCountdownText();
            if (_searchCountdownText == null) return;
            if (secondsLeft > 0)
            {
                _searchCountdownText.gameObject.SetActive(true);
                _searchCountdownText.text = $"Соло через: {secondsLeft:00}с";
            }
            else
            {
                _searchCountdownText.gameObject.SetActive(false);
            }
        }

        private void EnsureSearchingPanel()
        {
            if (_searchingPanel != null || searchingPanelPrefab == null) return;

            Transform parent = null;
            Canvas topCanvas = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var s = c.transform.lossyScale;
                if (Mathf.Abs(s.x) < 0.01f || Mathf.Abs(s.y) < 0.01f) continue;
                if (topCanvas == null || c.sortingOrder > topCanvas.sortingOrder)
                    topCanvas = c;
            }
            if (topCanvas != null) parent = topCanvas.transform;
            if (parent == null) return;

            _searchingPanel = Instantiate(searchingPanelPrefab, parent, false);
            _searchingPanel.name = "MatchmakingOverlay";
            if (_searchingPanel.transform is RectTransform rt)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            _searchingPanel.transform.SetAsLastSibling();
            _searchingPanel.OnCancelClicked += OnSearchCancelClicked;
            EnsureSearchCountdownText();
            _searchingPanel.Hide();
        }

        private void EnsureSearchCountdownText()
        {
            if (_searchingPanel == null) return;
            if (_searchCountdownText != null) return;
            var root = _searchingPanel.transform as RectTransform;
            if (root == null) return;

            var go = new GameObject("DuelSearchCountdownText");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(0.5f, 0.70f);
            rt.anchorMax = new Vector2(0.5f, 0.70f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420f, 56f);

            var txt = go.AddComponent<Text>();
            txt.font = Match3BoardView.GetFont();
            txt.fontSize = 36;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(1f, 0.92f, 0.4f, 1f);
            txt.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            go.transform.SetAsLastSibling();
            go.SetActive(false);
            _searchCountdownText = txt;
        }

        private void OnSearchCancelClicked()
        {
            if (_isQuitting || _matchEnded) return;
            QuitMatchAndReturnToMenu();
        }

        private void Update()
        {
            if (_localView != null)
            {
                HandleKeypadFocusCancel();
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

            if (_keypadFocusActive)
            {
                ExitKeypadFocus();
                return;
            }

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
            if (!TryGetDoorIdFromCubeName(cubeTf.name, out var doorId, out var codeLen)) return;
            if (_doorOpenedById[doorId]) return;
            if (!_isSoloMode && _match == null) return;
            if (_isSoloMode)
            {
                var soloPin = GetSoloPin(doorId, codeLen);
                _localMover.enabled = false;
                EnsureKeypadModal();
                _keypadModal.Show(soloPin, codeLen, () =>
                {
                    OpenDoorAndSync(doorId, sendNetwork: false, attemptsUsed: GetCurrentDoorAttemptNumber(doorId));
                    if (_localMover != null && !_matchEnded) _localMover.enabled = true;
                }, doorId, onClosed: () =>
                {
                    if (_localMover != null && !_matchEnded) _localMover.enabled = true;
                });
                return;
            }

            if (!TryGetSortedDuelPair(out var uaF, out var ubF))
            {
                Debug.LogWarning("[Duel] Клавиатура: нет пары user id для PIN (ждите соперника).");
                return;
            }

            _localMover.enabled = false;
            EnsureKeypadModal();

            _keypadModal.ShowDuel(_match.Id, uaF, ubF, doorId, codeLen, () =>
            {
                OpenDoorAndSync(doorId, sendNetwork: true, attemptsUsed: GetCurrentDoorAttemptNumber(doorId));
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            }, onClosed: () =>
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
            _slidingDoorsById.Clear();

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

            foreach (var sd in FindObjectsByType<SlidingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var id = MapSideDoorIndexToId(sd.gameObject.name);
                if (id != 0) _slidingDoorsById[id] = sd;
            }
        }

        /// <summary>Имена вида SlidingDoor_left_1 / SlidingDoor_right_2.</summary>
        private static int MapSideDoorIndexToId(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return 0;
            var n = objectName.ToLowerInvariant();
            var isLeft = n.Contains("left") || n.Contains("_l_") || n.Contains("door_l");
            var isRight = n.Contains("right") || n.Contains("_r_") || n.Contains("door_r");
            if (!isLeft && !isRight) return 0;

            var idx = ExtractTrailingIndex(objectName);
            if (idx != 1 && idx != 2) return 0;

            if (isLeft)
                return idx == 1 ? LeftDoor1Id : LeftDoor2Id;
            return idx == 1 ? RightDoor1Id : RightDoor2Id;
        }

        private static int ExtractTrailingIndex(string name)
        {
            var lastUnd = name.LastIndexOf('_');
            if (lastUnd < 0 || lastUnd + 1 >= name.Length) return 0;
            var tail = name.Substring(lastUnd + 1);
            var digits = new StringBuilder(4);
            foreach (var ch in tail)
            {
                if (ch >= '0' && ch <= '9') digits.Append(ch);
            }
            if (digits.Length == 0 || !int.TryParse(digits.ToString(), out var idx)) return 0;
            return idx;
        }

        private void EnsureFinishSphere()
        {
            if (_finishSphereCreated) return;

            Vector3 mid;

            if (_doorsById.TryGetValue(LeftDoor2Id, out var doorL2) &&
                _doorsById.TryGetValue(RightDoor2Id, out var doorR2))
            {
                mid = (doorL2.transform.position + doorR2.transform.position) * 0.5f;
            }
            else if (_slidingDoorsById.TryGetValue(LeftDoor2Id, out var sdL2) &&
                     _slidingDoorsById.TryGetValue(RightDoor2Id, out var sdR2))
            {
                mid = (sdL2.transform.position + sdR2.transform.position) * 0.5f;
            }
            else if (_slidingDoorsById.TryGetValue(LeftDoor1Id, out var sdL1) &&
                     _slidingDoorsById.TryGetValue(RightDoor1Id, out var sdR1))
            {
                // Только первые лифты — финиш дальше по коридору.
                mid = (sdL1.transform.position + sdR1.transform.position) * 0.5f;
            }
            else if (spawnLeft != null && spawnRight != null)
            {
                mid = (spawnLeft.position + spawnRight.position) * 0.5f;
            }
            else return;

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

        private void HandleKeypadFocusCancel()
        {
            if (!_keypadFocusActive || _matchEnded) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                ExitKeypadFocus();
#else
            if (Input.GetKeyDown(KeyCode.Escape))
                ExitKeypadFocus();
#endif
        }

        private void HandleLocalKeypadClick()
        {
            if (_matchEnded) return;
            if (_doorOpenedById == null || _doorOpenedById.Length < 5) return;
            if (_keypadFocusActive) return;

            if (_localMover == null && _localView != null) _localMover = _localView.GetComponent<PlayerMovementController>();
            if (_localMover == null) return;

            if (!TryGetClick(out var clickPos))
                return;

            if (IsPointerOverUiThisFrame())
                return;

            var cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(clickPos);
            if (!Physics.Raycast(ray, out var hit, 22f, ~0, QueryTriggerInteraction.Collide))
                return;

            if (hit.transform == null) return;

            // 3D-клавиатура: без UI-модалки, режим ввода на месте.
            if (TryGetKeypadFromHierarchy(hit.collider.transform, out var navKeypad, out var keypadRoot))
            {
                if (Vector3.Distance(_localView.transform.position, keypadRoot.position) > KeypadInteractDistance)
                    return;

                if (!TryGetDoorIdFromKeypadHierarchy(keypadRoot, out var kDoorId, out var kCodeLen))
                    return;

                if (_doorOpenedById[kDoorId]) return;

                EnterKeypadFocus(navKeypad, kDoorId, kCodeLen);
                return;
            }

            EnsureKeypadModal();
            if (_keypadModal.IsOpen) return;

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
            if (!TryGetDoorIdFromCubeName(cubeName, out var doorId, out var codeLen))
                return;

            if (_doorOpenedById[doorId]) return;
            if (!_isSoloMode && _match == null) return;
            if (_isSoloMode)
            {
                var soloPin = GetSoloPin(doorId, codeLen);
                _localMover.enabled = false;
                _keypadModal.Show(soloPin, codeLen, () =>
                {
                    OpenDoorAndSync(doorId, sendNetwork: false, attemptsUsed: GetCurrentDoorAttemptNumber(doorId));
                    if (_localMover != null && !_matchEnded) _localMover.enabled = true;
                }, doorId, onClosed: () =>
                {
                    if (_localMover != null && !_matchEnded) _localMover.enabled = true;
                });
                return;
            }

            if (!TryGetSortedDuelPair(out var uaC, out var ubC))
            {
                Debug.LogWarning("[Duel] Клавиатура: нет пары user id для PIN (ждите соперника).");
                return;
            }

            // “Домофон” блокирует управление, пока ввод не будет успешным.
            _localMover.enabled = false;
            _keypadModal.ShowDuel(_match.Id, uaC, ubC, doorId, codeLen, () =>
            {
                OpenDoorAndSync(doorId, sendNetwork: true, attemptsUsed: GetCurrentDoorAttemptNumber(doorId));
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            }, onClosed: () =>
            {
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
            });
        }

        private static bool TryGetClick(out Vector2 position)
        {
            position = default;
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
            {
                position = ts.primaryTouch.position.ReadValue();
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }
            return false;
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                position = Input.GetTouch(0).position;
                return true;
            }
            if (!Input.GetMouseButtonDown(0)) return false;
            position = Input.mousePosition;
            return true;
#endif
        }

        private static bool IsPointerOverUiThisFrame()
        {
            if (EventSystem.current == null) return false;
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
                return EventSystem.current.IsPointerOverGameObject(ts.primaryTouch.touchId.ReadValue());
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return EventSystem.current.IsPointerOverGameObject();
            return false;
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            return Input.GetMouseButtonDown(0) && EventSystem.current.IsPointerOverGameObject();
#endif
        }

        private bool TryGetDoorIdFromCubeName(string cubeName, out int doorId, out int codeLen)
        {
            doorId = 0;
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

            return DuelDoorPins.TryGetCodeLengthForDoorId(doorId, out codeLen);
        }

        private static bool TryGetKeypadFromHierarchy(Transform hitTransform, out Keypad keypad, out Transform keypadRoot)
        {
            keypad = null;
            keypadRoot = null;
            var t = hitTransform;
            while (t != null)
            {
                if (t.TryGetComponent<Keypad>(out keypad))
                {
                    keypadRoot = t;
                    return true;
                }
                t = t.parent;
            }
            return false;
        }

        private bool TryGetDoorIdFromKeypadHierarchy(Transform keypadRoot, out int doorId, out int codeLen)
        {
            doorId = 0;
            codeLen = 0;

            var link = keypadRoot.GetComponentInParent<DuelKeypadDoorLink>();
            if (link != null && link.TryGetDoorConfig(out doorId, out codeLen))
                return true;

            var t = keypadRoot;
            while (t != null)
            {
                if (TryParseKeypadDoorFromObjectName(t.name, out doorId, out codeLen))
                    return true;
                t = t.parent;
            }
            return false;
        }

        private bool TryParseKeypadDoorFromObjectName(string objectName, out int doorId, out int codeLen)
        {
            doorId = 0;
            codeLen = 0;
            if (string.IsNullOrEmpty(objectName)) return false;

            var leftMarker = objectName.IndexOf("Keypad_left_", StringComparison.OrdinalIgnoreCase) >= 0;
            var rightMarker = objectName.IndexOf("Keypad_right_", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!leftMarker && !rightMarker) return false;

            var idx = ExtractTrailingIndex(objectName);
            if (idx != 1 && idx != 2) return false;

            if (leftMarker)
                doorId = idx == 1 ? LeftDoor1Id : LeftDoor2Id;
            else
                doorId = idx == 1 ? RightDoor1Id : RightDoor2Id;

            return DuelDoorPins.TryGetCodeLengthForDoorId(doorId, out codeLen);
        }

        private void EnterKeypadFocus(Keypad keypad, int doorId, int codeLen)
        {
            if (_keypadFocusActive || keypad == null) return;

            _keypadFocusActive = true;
            _activeKeypad = keypad;
            _activeDoorId = doorId;
            _keypadGuessInFlight = false;

            keypad.EnsureDisplayReference();
            if (_isSoloMode)
            {
                var pin = GetSoloPin(doorId, codeLen);
                if (int.TryParse(pin, out var pinValue))
                    keypad.ApplyCombinationAndReset(pinValue, codeLen);
                else
                    keypad.ConfigureDuelSession(codeLen);
            }
            else
            {
                keypad.ConfigureDuelSession(codeLen);
                keypad.SetServerGuessInvoker(On3DKeypadGuessSubmitted);
            }
            _activeKeypadWrongGuessHandler = OnActiveKeypadWrongGuess;
            keypad.WrongGuessSubmitted += _activeKeypadWrongGuessHandler;

            if (_localMover != null) _localMover.enabled = false;
            SetMobileExitKeypadVisible(true);

            var cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null)
            {
                _keypadFocusActive = false;
                _activeKeypad = null;
                _activeDoorId = 0;
                SetMobileExitKeypadVisible(false);
                if (_localMover != null && !_matchEnded) _localMover.enabled = true;
                return;
            }

            _camWorldPosRestore = cam.transform.position;
            _camWorldRotRestore = cam.transform.rotation;
            _camParentRestore = cam.transform.parent;

            _playerFollowCam = cam.GetComponent<SimpleFollowCamera>();
            if (_playerFollowCam != null) _playerFollowCam.enabled = false;
            _duelCameraDragOrbit = cam.GetComponent<DuelCameraDragOrbit>();
            if (_duelCameraDragOrbit != null) _duelCameraDragOrbit.enabled = false;

            _runtimeKeypadFpv = cam.GetComponent<KeypadInteractionFPV>();
            if (_runtimeKeypadFpv == null)
            {
                _runtimeKeypadFpv = cam.gameObject.AddComponent<KeypadInteractionFPV>();
                _runtimeKeypadFpvOwned = true;
            }
            else _runtimeKeypadFpvOwned = false;

            _runtimeKeypadFpv.enabled = false;

            if (_keypadFocusCameraCo != null)
            {
                StopCoroutine(_keypadFocusCameraCo);
                _keypadFocusCameraCo = null;
            }

            _keypadFocusCameraCo = StartCoroutine(KeypadFocusCameraRoutine(cam, keypad.transform));

            _activeKeypadGrantedHandler = OnActiveKeypadAccessGranted;
            _activeKeypad.OnAccessGranted.AddListener(_activeKeypadGrantedHandler);

            if (showKeypadWorldAttemptLog)
            {
                if (_keypadWorldLogGo != null) Destroy(_keypadWorldLogGo);
                _keypadWorldLogGo = new GameObject("KeypadWorldAttemptLog");
                _keypadWorldLogGo.transform.SetParent(keypad.transform, false);
                var log = _keypadWorldLogGo.AddComponent<DuelKeypadWorldLog>();
                log.Initialize(keypad, keypad.transform, cam, GetAttemptHistory(doorId));
            }
        }

        private IEnumerator KeypadFocusCameraRoutine(Camera cam, Transform keypadRoot)
        {
            var ktr = keypadRoot;
            var startPos = cam.transform.position;
            var startRot = cam.transform.rotation;

            var focusCenterTr = ktr.Find("KeypadFocusCenter");
            var bttn5 = FindChildTransformByName(ktr, "bttn5");
            var vp = ktr.Find("KeypadViewpoint") ?? ktr.Find("CameraAnchor");

            Vector3 endPos;
            Quaternion endRot;

            if (focusCenterTr == null && bttn5 == null && vp != null)
            {
                endPos = vp.position;
                endRot = vp.rotation;
            }
            else
            {
                var focusTr = focusCenterTr != null ? focusCenterTr : bttn5 != null ? bttn5 : ktr;
                var lookAt = focusTr.position;
                endPos = lookAt - ktr.forward * keypadCameraDistance + ktr.up * 0.03f;
                var toTarget = lookAt - endPos;
                if (toTarget.sqrMagnitude > 0.0001f)
                    endRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                else
                    endRot = cam.transform.rotation;
            }

            var dur = Mathf.Max(0.05f, keypadCameraLerpSeconds);
            var t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                cam.transform.position = Vector3.Lerp(startPos, endPos, u);
                cam.transform.rotation = Quaternion.Slerp(startRot, endRot, u);
                yield return null;
            }

            cam.transform.position = endPos;
            cam.transform.rotation = endRot;

            if (_keypadFocusActive && _runtimeKeypadFpv != null)
                _runtimeKeypadFpv.enabled = true;

            _keypadFocusCameraCo = null;
        }

        private static Transform FindChildTransformByName(Transform root, string exactName)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name == exactName)
                    return tr;
            }
            return null;
        }

        private void OnActiveKeypadAccessGranted()
        {
            if (!_keypadFocusActive) return;
            OpenDoorAndSync(_activeDoorId, sendNetwork: true, attemptsUsed: GetCurrentDoorAttemptNumber(_activeDoorId));
            ExitKeypadFocus();
        }

        private void On3DKeypadGuessSubmitted(string guess)
        {
            if (_isSoloMode) return;
            if (_match == null || !_keypadFocusActive || _keypadGuessInFlight) return;
            var history = GetAttemptHistory(_activeDoorId);
            if (history.Count >= MaxDoorAttempts)
            {
                _activeKeypad?.AbortPendingGuess();
                if (hud != null) hud.ShowBanner($"Лимит попыток исчерпан ({MaxDoorAttempts})");
                return;
            }
            _keypadGuessInFlight = true;
            KeypadGuessAwait(guess);
        }

        private List<string> GetAttemptHistory(int doorId)
        {
            if (!_attemptHistoryByDoor.TryGetValue(doorId, out var history))
            {
                history = new List<string>();
                _attemptHistoryByDoor[doorId] = history;
            }

            return history;
        }

        private void OnActiveKeypadWrongGuess(string guess, int bulls, int cows)
        {
            if (_activeDoorId <= 0) return;
            var history = GetAttemptHistory(_activeDoorId);
            if (history.Count >= MaxDoorAttempts) return;
            history.Add(BullsCowsScoring.FormatAttemptLine(history.Count + 1, guess, bulls, cows));
        }

        private void ClearLocalKeypadHistories()
        {
            _attemptHistoryByDoor.Clear();
            if (_keypadModal != null)
                _keypadModal.ClearAllHistory();
        }

        private async void KeypadGuessAwait(string guess)
        {
            try
            {
                if (!TryGetSortedDuelPair(out var ua, out var ub))
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        _keypadGuessInFlight = false;
                        _activeKeypad?.AbortPendingGuess();
                        Debug.LogWarning("[Duel] duel_keypad_guess: нет пары user id.");
                    });
                    return;
                }

                var r = await DuelKeypadRpc.GuessAsync(_match.Id, _activeDoorId, guess, ua, ub);
                MainThreadDispatcher.Enqueue(() =>
                {
                    _keypadGuessInFlight = false;
                    if (!_keypadFocusActive || _activeKeypad == null) return;
                    _activeKeypad.ApplyServerGuessOutcome(r, guess);
                });
            }
            catch (Exception e)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    _keypadGuessInFlight = false;
                    _activeKeypad?.AbortPendingGuess();
                    Debug.LogException(e);
                });
            }
        }

        private void ExitKeypadFocus()
        {
            if (!_keypadFocusActive) return;

            if (_keypadFocusCameraCo != null)
            {
                StopCoroutine(_keypadFocusCameraCo);
                _keypadFocusCameraCo = null;
            }

            if (_keypadWorldLogGo != null)
            {
                Destroy(_keypadWorldLogGo);
                _keypadWorldLogGo = null;
            }

            if (_activeKeypad != null && _activeKeypadGrantedHandler != null)
                _activeKeypad.OnAccessGranted.RemoveListener(_activeKeypadGrantedHandler);
            if (_activeKeypad != null && _activeKeypadWrongGuessHandler != null)
                _activeKeypad.WrongGuessSubmitted -= _activeKeypadWrongGuessHandler;

            if (_activeKeypad != null)
                _activeKeypad.ClearDuelNetworking();

            _activeKeypadGrantedHandler = null;
            _activeKeypadWrongGuessHandler = null;
            _activeKeypad = null;
            _activeDoorId = 0;
            _keypadGuessInFlight = false;

            var cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                if (_runtimeKeypadFpv != null)
                {
                    _runtimeKeypadFpv.enabled = false;
                    if (_runtimeKeypadFpvOwned)
                        Destroy(_runtimeKeypadFpv);
                    _runtimeKeypadFpv = null;
                    _runtimeKeypadFpvOwned = false;
                }

                cam.transform.SetParent(_camParentRestore, true);
                cam.transform.position = _camWorldPosRestore;
                cam.transform.rotation = _camWorldRotRestore;
            }

            if (_playerFollowCam != null)
            {
                _playerFollowCam.enabled = true;
                _playerFollowCam = null;
            }

            if (_duelCameraDragOrbit != null)
            {
                _duelCameraDragOrbit.enabled = true;
            }

            _keypadFocusActive = false;
            SetMobileExitKeypadVisible(false);

            if (_localMover != null && !_matchEnded) _localMover.enabled = true;
        }

        private void EnsureMobileExitKeypadButton()
        {
            if (!showMobileExitFromKeypad || !Application.isMobilePlatform)
                return;
            if (_mobileExitKeypadButton != null && _mobileExitKeypadGroup != null)
                return;

            Canvas targetCanvas = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (targetCanvas == null || c.sortingOrder > targetCanvas.sortingOrder)
                    targetCanvas = c;
            }
            if (targetCanvas == null) return;

            var go = new GameObject("MobileExitKeypadButton");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(targetCanvas.transform, false);
            rt.anchorMin = new Vector2(0.91f, 0.83f);
            rt.anchorMax = new Vector2(0.98f, 0.93f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.65f);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(ExitKeypadFocus);

            var labelGo = new GameObject("Text");
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;

            var txt = labelGo.AddComponent<Text>();
            txt.text = "X";
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 42;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.raycastTarget = false;

            _mobileExitKeypadGroup = go.AddComponent<CanvasGroup>();
            _mobileExitKeypadButton = btn;
            SetMobileExitKeypadVisible(false);
        }

        private void SetMobileExitKeypadVisible(bool visible)
        {
            EnsureMobileExitKeypadButton();
            if (_mobileExitKeypadGroup == null) return;
            _mobileExitKeypadGroup.alpha = visible ? 1f : 0f;
            _mobileExitKeypadGroup.interactable = visible;
            _mobileExitKeypadGroup.blocksRaycasts = visible;
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

        private void OpenDoorAndSync(int doorId, bool sendNetwork, int attemptsUsed = 0)
        {
            if (_doorOpenedById == null || doorId <= 0 || doorId >= _doorOpenedById.Length) return;
            if (_doorOpenedById[doorId]) return;

            var opened = false;
            if (_doorsById.TryGetValue(doorId, out var door) && door != null)
            {
                door.SetOpen(true, instant: true);
                opened = true;
            }
            else if (_slidingDoorsById.TryGetValue(doorId, out var sliding) && sliding != null)
            {
                sliding.OpenDoor();
                opened = true;
            }

            if (opened) _doorOpenedById[doorId] = true;

            if (sendNetwork && !_matchEnded && !_isSoloMode)
            {
                _ = SendDoorOpenedAsync(doorId, attemptsUsed);
            }
        }

        private async Task SendDoorOpenedAsync(int doorId, int attemptsUsed)
        {
            if (_match == null) return;
            if (!NakamaBootstrap.Instance.Socket.IsConnected) return;

            var msg = new NetDoorOpenedState { doorId = doorId, attemptsUsed = Mathf.Max(1, attemptsUsed) };
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

            ExitKeypadFocus();
            if (_keypadModal != null && _keypadModal.IsOpen) _keypadModal.Close();
            ClearLocalKeypadHistories();

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

        /// <summary>Два UUID участников дуэли, отсортированные Ordinal (как на сервере для владельца storage).</summary>
        private bool TryGetSortedDuelPair(out string userA, out string userB)
        {
            userA = userB = null;
            var ids = new HashSet<string>();
            if (!string.IsNullOrEmpty(_myUserId))
                ids.Add(_myUserId);
            if (!string.IsNullOrEmpty(_opponentUserId))
                ids.Add(_opponentUserId);
            if (_match?.Presences != null)
            {
                foreach (var p in _match.Presences)
                {
                    if (p != null && !string.IsNullOrEmpty(p.UserId))
                        ids.Add(p.UserId);
                }
            }

            if (ids.Count < 2)
                return false;

            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            userA = list[0];
            userB = list[1];
            return true;
        }

        private async Task TryEnsureDuelKeypadPinsAsync()
        {
            if (_isSoloMode) return;
            if (_match == null) return;
            if (!TryGetSortedDuelPair(out var a, out var b)) return;
            try
            {
                await DuelKeypadRpc.EnsurePinsAsync(_match.Id, a, b);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Duel] duel_match_ensure_pins: " + e.Message +
                                 " (проверьте Server/nakama/modules/duel_keypad.lua на Nakama)");
            }
        }

        private async Task FindMatchAndJoinAsync(CancellationToken ct)
        {
            _mmTcs = new TaskCompletionSource<IMatchmakerMatched>();

            // Любые игроки, 2 человека.
            ct.ThrowIfCancellationRequested();
            Debug.Log("[Duel] Matchmaker: enqueue ticket...");
            var ticket = await NakamaBootstrap.Instance.Socket.AddMatchmakerAsync(query: "*", minCount: 2, maxCount: 2);
            _duelMatchmakerTicket = ticket?.Ticket;
            StartSearchCountdown("Поиск соперника...", DuelMatchmakingTimeoutSeconds);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(DuelMatchmakingTimeoutSeconds), ct);
            var matchedTask = _mmTcs.Task;
            var completed = await Task.WhenAny(matchedTask, timeoutTask);
            StopSearchCountdown();
            if (completed != matchedTask)
            {
                ct.ThrowIfCancellationRequested();
                _mmTcs = null;
                await CancelDuelMatchmakerTicketAsync();
                StartSoloDuelMode();
                return;
            }

            var matched = await matchedTask;
            _mmTcs = null;
            _duelMatchmakerTicket = null;

            ct.ThrowIfCancellationRequested();
            Debug.Log($"[Duel] Matchmaker matched. Joining match...");
            _match = await NakamaBootstrap.Instance.Socket.JoinMatchAsync(matched);
            Debug.Log($"[Duel] Joined match: {_match.Id}. Presences: {CountPresences(_match.Presences)}");
            await TryEnsureDuelKeypadPinsAsync();
            // В некоторых реализациях SDK список presences может не содержать себя или быть пустым.
            // Локального игрока спавним всегда.
            EnsureLocalSpawn();
            SpawnInitialPresences(_match);
        }

        private async Task CancelDuelMatchmakerTicketAsync()
        {
            var ticket = _duelMatchmakerTicket;
            _duelMatchmakerTicket = null;
            if (string.IsNullOrWhiteSpace(ticket)) return;
            try
            {
                var socket = NakamaBootstrap.Instance?.Socket;
                if (socket != null && socket.IsConnected)
                    await socket.RemoveMatchmakerAsync(ticket);
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        private void StartSoloDuelMode()
        {
            _isSoloMode = true;
            _match = null;
            _opponentUserId = "solo";
            _preferLeftUntilRemoteKnown = false;
            GenerateSoloPins();
            EnsureLocalSpawn();
            if (_playersByUserId.TryGetValue(_myUserId, out var local))
                local.transform.position = _spawnLeftPos;
            if (hud != null) hud.ShowBanner("Соперник не найден.\nСоло-режим.");
        }

        private void GenerateSoloPins()
        {
            _soloPinsByDoor.Clear();
            for (var doorId = 1; doorId <= 4; doorId++)
            {
                if (!DuelDoorPins.TryGetCodeLengthForDoorId(doorId, out var codeLen)) continue;
                _soloPinsByDoor[doorId] = GenerateNumericCode(codeLen);
            }
        }

        private string GetSoloPin(int doorId, int fallbackLen)
        {
            if (_soloPinsByDoor.TryGetValue(doorId, out var pin) && !string.IsNullOrEmpty(pin))
                return pin;
            var len = Mathf.Max(1, fallbackLen);
            var generated = GenerateNumericCode(len);
            _soloPinsByDoor[doorId] = generated;
            return generated;
        }

        private string GenerateNumericCode(int codeLen)
        {
            var chars = new char[Mathf.Max(1, codeLen)];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = (char)('0' + _soloRng.Next(0, 10));
            return new string(chars);
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
            follow.ConfigureForDuelOrbitCamera();
            follow.SetTarget(target);
            _duelCameraDragOrbit = cam.GetComponent<DuelCameraDragOrbit>();
            if (_duelCameraDragOrbit == null)
                _duelCameraDragOrbit = cam.gameObject.AddComponent<DuelCameraDragOrbit>();
            if (hud != null)
                hud.EnsureJumpButton(_localMover);
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

                _ = TryEnsureDuelKeypadPinsAsync();
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
                var attemptsUsed = doorMsg.attemptsUsed;
                var openedByUserId = state.UserPresence?.UserId;

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_matchEnded) return;
                    OpenDoorAndSync(doorId, sendNetwork: false);
                    if (!string.IsNullOrEmpty(openedByUserId) &&
                        !string.Equals(openedByUserId, _myUserId, StringComparison.Ordinal) &&
                        hud != null)
                    {
                        var displayDoor = GetDoorDisplayIndex(doorId);
                        var safeAttempts = Mathf.Max(1, attemptsUsed);
                        hud.ShowBanner($"Игрок 2 открыл дверь №{displayDoor} с {safeAttempts} попытки");
                    }
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
                ExitKeypadFocus();
                ClearLocalKeypadHistories();
                _match = null;
                SceneManager.LoadScene("ArenaMenu");
            }
        }

        private void OnOpponentLeft()
        {
            if (_matchEnded) return;
            _matchEnded = true;

            ExitKeypadFocus();
            if (_keypadModal != null && _keypadModal.IsOpen) _keypadModal.Close();
            ClearLocalKeypadHistories();

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
            SceneManager.LoadScene("ArenaMenu");
        }

        private static int GetDoorDisplayIndex(int doorId)
        {
            return doorId == LeftDoor2Id || doorId == RightDoor2Id ? 2 : 1;
        }

        private int GetCurrentDoorAttemptNumber(int doorId)
        {
            // У нас в истории хранятся только неудачные попытки, успешная = следующая.
            var wrongAttempts = 0;
            if (_attemptHistoryByDoor.TryGetValue(doorId, out var history3D) && history3D != null)
                wrongAttempts = Mathf.Max(wrongAttempts, history3D.Count);

            if (_keypadModal != null)
                wrongAttempts = Mathf.Max(wrongAttempts, _keypadModal.GetWrongAttemptsCount(doorId));

            return wrongAttempts + 1;
        }
    }
}

