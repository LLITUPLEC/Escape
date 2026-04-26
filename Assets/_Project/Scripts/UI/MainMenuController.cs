using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Online Badge")]
        [SerializeField] private RectTransform onlineBadgeParent;
        [SerializeField] private GameObject onlineBadgePrefab;
        [Header("Resources UI")]
        [SerializeField] private float onlinePollSeconds = 5f;
        [SerializeField] private float resourcesPollSeconds = 5f;
        [SerializeField] private float match3StatsPollSeconds = 5f;
        [Header("Покупка энергии (+): иконки в рантайм-диалоге")]
        [Tooltip("В APK AssetDatabase недоступен, а пути вне Resources не грузятся — задайте те же спрайты, что на строках шапки, либо положите PNG в Assets/Resources/…")]
        [SerializeField] private Sprite energyBuyDialogEnergyIcon;
        [SerializeField] private Sprite energyBuyDialogMatterIcon;
        [SerializeField] private Sprite energyBuyDialogGoldIcon;

        [Header("Debug")]
        [SerializeField] private bool debugUiStats = false;
        [Header("Navigation")]
        [SerializeField] private string mineSceneName = "MineScene";
        [SerializeField] private string workshopSceneName = "WorkshopScene";
        [SerializeField] private string mineButtonName = "MineButton";
        [SerializeField] private string workshopButtonName = "BottomButtonWorkshop";

        private const string HudGoldIconAssetPath = "Assets/_Project/img/resources_hud/gold.png";
        private const string HudMatterIconAssetPath = "Assets/_Project/img/resources_hud/matter.png";
        private const string HudEnergyIconAssetPath = "Assets/_Project/img/resources_hud/energy.png";

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcMatch3StatsGet = "duel_match3_stats_get";
        private const string PrefLastKnownUsername = "nakama.ui.last_known_username";
        private const string PrefLastKnownUserId = "nakama.ui.last_known_user_id";
        private const string PrefUserNameByUserIdPrefix = "nakama.ui.username.by_user_id.";
        private Text _onlineCountText;
        private TMP_Text _onlineCountTmp;
        private Text _playerUsernameText;
        private TMP_Text _playerUsernameTmp;
        private CancellationTokenSource _onlineCts;
        private GameObject _onlineBadgeInstance;
        private RectTransform _onlineBadgeRect;
        private int _lastOnlineCount = -1;
        private string _lastUsername = "";
        private Coroutine _badgePulseRoutine;
        private RectTransform _match3StatsRoot;
        private Text _match3PlayedText;
        private Text _match3WinsText;
        private Text _match3LossesText;
        private TMP_Text _match3PlayedTmp;
        private TMP_Text _match3WinsTmp;
        private TMP_Text _match3LossesTmp;
        private Button _match3StatsToggleButton;
        private Image _match3StatsToggleImage;
        [SerializeField] private Texture2D eyeTexture;
        [SerializeField] private Sprite eyeOpenSprite;
        [SerializeField] private Sprite eyeClosedSprite;

        [Header("Server aura")]
        [Tooltip("Иконка ServerAuraButton. В билде без редактора назначьте здесь (путь по умолчанию: Assets/_Project/img/items/WeaponLeft_green.png).")]
        [SerializeField] private Sprite serverAuraButtonIcon;
        private const string ServerAuraButtonIconAssetPath = "Assets/_Project/img/items/WeaponLeft_green.png";
        private bool _match3StatsVisible;
        private Transform _headerResourcesRoot;
        private readonly ResourceValueBinding _energyBinding = new("Energy");
        private readonly ResourceValueBinding _oreBinding = new("ore");
        private readonly ResourceValueBinding _goldBinding = new("Gold");
        private readonly ResourceValueBinding _ingotsBinding = new("ingots");
        private readonly ResourceValueBinding _matterBinding = new("matter");
        private readonly ResourceValueBinding _keysBinding = new("keys");
        private Button _mineButton;
        private Button _workshopButton;
        private EnergyHeaderPurchaseController _energyHeaderPurchase;
        private Sprite _hudEnergySprite;
        private Sprite _hudMatterSprite;
        private Sprite _hudGoldSprite;

        private GameObject _serverAuraButtonGo;
        private GameObject _serverAuraModalRoot;
        private Text _serverAuraModalBodyText;
        private ServerAuraGetRpcResponse _serverAuraLast;

        private void Awake()
        {
            TryAutoAssignEyeSpritesInEditor();
            TryAutoAssignServerAuraIconInEditor();
            EnsureOnlineBadge();
            EnsurePlayerUsernameLabel();
            EnsureServerAuraButton();
            EnsureHeaderResources();
            EnsureMatch3StatsCard();
            EnsureMatch3StatsToggleButton();
            EnsureMainMenuNavigationButtons();
            ApplySafeAreaClamp();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            TryAutoAssignEyeSpritesInEditor();
            TryAutoAssignServerAuraIconInEditor();
        }
#endif

        private void OnEnable()
        {
            _onlineCts = new CancellationTokenSource();
            _ = OnlineLoopAsync(_onlineCts.Token);
            _ = RefreshHeaderResourcesAsync(_onlineCts.Token);
            _ = RefreshMatch3StatsCardAsync(_onlineCts.Token);
            _ = RefreshPlayerUsernameAsync(_onlineCts.Token);
            _ = ServerAuraLoopAsync(_onlineCts.Token);
            EnsureMainMenuNavigationButtons();
            TryInstallEnergyHeaderPurchase();
            ApplySafeAreaClamp();
        }

        private void OnDisable()
        {
            _energyHeaderPurchase = null;
            _onlineCts?.Cancel();
            _onlineCts?.Dispose();
            _onlineCts = null;
            if (_badgePulseRoutine != null)
            {
                StopCoroutine(_badgePulseRoutine);
                _badgePulseRoutine = null;
            }
            if (_onlineBadgeRect != null)
                _onlineBadgeRect.localScale = Vector3.one;
        }

        private void OnDestroy()
        {
            if (_mineButton != null) _mineButton.onClick.RemoveListener(OpenMineScene);
            if (_workshopButton != null) _workshopButton.onClick.RemoveListener(OpenWorkshopScene);
        }

        private void EnsureMainMenuNavigationButtons()
        {
            var layout = ResolveMainMenuHudLayoutRoot();
            if (layout == null) return;

            if (_mineButton == null)
            {
                var mineRoot = FindChildByName(layout, mineButtonName, StringComparison.OrdinalIgnoreCase);
                if (mineRoot != null)
                {
                    _mineButton = mineRoot.GetComponent<Button>();
                    if (_mineButton != null)
                    {
                        _mineButton.onClick.RemoveListener(OpenMineScene);
                        _mineButton.onClick.AddListener(OpenMineScene);
                    }
                }
            }

            if (_workshopButton == null)
            {
                var workshopRoot = FindChildByName(layout, workshopButtonName, StringComparison.OrdinalIgnoreCase);
                if (workshopRoot != null)
                {
                    _workshopButton = workshopRoot.GetComponent<Button>();
                    if (_workshopButton != null)
                    {
                        _workshopButton.onClick.RemoveListener(OpenWorkshopScene);
                        _workshopButton.onClick.AddListener(OpenWorkshopScene);
                    }
                }
            }
        }

        private void OpenMineScene()
        {
            if (!string.IsNullOrWhiteSpace(mineSceneName) && Application.CanStreamedLevelBeLoaded(mineSceneName))
                SceneManager.LoadScene(mineSceneName);
        }

        private void OpenWorkshopScene()
        {
            if (!string.IsNullOrWhiteSpace(workshopSceneName) && Application.CanStreamedLevelBeLoaded(workshopSceneName))
                SceneManager.LoadScene(workshopSceneName);
        }

        private void EnsureMatch3StatsCard()
        {
            var parent = ResolveMainMenuHudLayoutRoot();
            if (parent == null) return;

            if (_match3StatsRoot == null)
            {
                // Card must be pre-placed in MainMenuHudOverlay prefab.
                _match3StatsRoot = FindRectTransformChildByName(parent, "Match3StatsCard");
                if (_match3StatsRoot == null)
                {
                    if (debugUiStats)
                        Debug.Log("[MainMenu] Match3StatsCard root not found under HUD/Canvas.");
                    return;
                }
            }
            // Keep fixed card size and move it up by 125 px.
            _match3StatsRoot.anchorMin = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.anchorMax = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.pivot = new Vector2(0.5f, 0.5f);
            _match3StatsRoot.sizeDelta = new Vector2(320f, 420f);
            _match3StatsRoot.anchoredPosition = new Vector2(0f, 125f);
            _match3StatsRoot.gameObject.SetActive(_match3StatsVisible);

            if (HasMatch3StatsBindings())
                return;

            _match3PlayedText = FindTextUnder(_match3StatsRoot, "PlayedValue");
            _match3WinsText = FindTextUnder(_match3StatsRoot, "WinsValue");
            _match3LossesText = FindTextUnder(_match3StatsRoot, "LossesValue");
            _match3PlayedTmp = FindTmpTextUnder(_match3StatsRoot, "PlayedValue");
            _match3WinsTmp = FindTmpTextUnder(_match3StatsRoot, "WinsValue");
            _match3LossesTmp = FindTmpTextUnder(_match3StatsRoot, "LossesValue");
            if (debugUiStats)
            {
                Debug.Log("[MainMenu] Match3StatsCard bindings: " +
                          $"Played(Text={_match3PlayedText != null}, TMP={_match3PlayedTmp != null}) " +
                          $"Wins(Text={_match3WinsText != null}, TMP={_match3WinsTmp != null}) " +
                          $"Losses(Text={_match3LossesText != null}, TMP={_match3LossesTmp != null}).");
            }

            // If prefab bindings are present (Text or TMP), we're done.
            if (HasMatch3StatsBindings())
                return;

            var bg = _match3StatsRoot.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.10f, 0.18f, 0.92f);
            var outline = _match3StatsRoot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.74f, 1f, 0.65f);
            outline.effectDistance = new Vector2(1f, -1f);

            var title = CreateStatsText("Title", _match3StatsRoot, "Три-в-ряд", 24, new Color(0.70f, 0.95f, 1f));
            Anchor(title.rectTransform, new Vector2(0.06f, 0.73f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

            var playedLabel = CreateStatsText("PlayedLabel", _match3StatsRoot, "Сыграно:", 18, Color.white);
            Anchor(playedLabel.rectTransform, new Vector2(0.08f, 0.46f), new Vector2(0.62f, 0.68f), TextAnchor.MiddleLeft);
            _match3PlayedText = CreateStatsText("PlayedValue", _match3StatsRoot, "0", 18, new Color(1f, 0.95f, 0.58f));
            Anchor(_match3PlayedText.rectTransform, new Vector2(0.64f, 0.46f), new Vector2(0.92f, 0.68f), TextAnchor.MiddleRight);

            var winsLabel = CreateStatsText("WinsLabel", _match3StatsRoot, "Побед:", 18, Color.white);
            Anchor(winsLabel.rectTransform, new Vector2(0.08f, 0.24f), new Vector2(0.62f, 0.46f), TextAnchor.MiddleLeft);
            _match3WinsText = CreateStatsText("WinsValue", _match3StatsRoot, "0", 18, new Color(0.50f, 1f, 0.50f));
            Anchor(_match3WinsText.rectTransform, new Vector2(0.64f, 0.24f), new Vector2(0.92f, 0.46f), TextAnchor.MiddleRight);

            var lossesLabel = CreateStatsText("LossesLabel", _match3StatsRoot, "Поражений:", 18, Color.white);
            Anchor(lossesLabel.rectTransform, new Vector2(0.08f, 0.02f), new Vector2(0.62f, 0.24f), TextAnchor.MiddleLeft);
            _match3LossesText = CreateStatsText("LossesValue", _match3StatsRoot, "0", 18, new Color(1f, 0.50f, 0.50f));
            Anchor(_match3LossesText.rectTransform, new Vector2(0.64f, 0.02f), new Vector2(0.92f, 0.24f), TextAnchor.MiddleRight);
        }

        private async Task RefreshMatch3StatsCardAsync(CancellationToken ct)
        {
            EnsureMatch3StatsCard();
            if (!HasMatch3StatsBindings()) return;
            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetMatch3StatsUnknown();
                    if (debugUiStats) Debug.Log("[MainMenu] Match3Stats: NakamaBootstrap.Instance == null");
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                {
                    SetMatch3StatsUnknown();
                    if (debugUiStats)
                        Debug.Log("[MainMenu] Match3Stats: Nakama not ready " +
                                  $"IsReady={NakamaBootstrap.Instance.IsReady} " +
                                  $"Client={(NakamaBootstrap.Instance.Client != null)} " +
                                  $"Session={(NakamaBootstrap.Instance.Session != null)}");
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsGet, "{}");
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetMatch3StatsUnknown();
                    if (debugUiStats) Debug.Log("[MainMenu] Match3Stats RPC payload empty/null.");
                    return;
                }

                var model = JsonUtility.FromJson<Match3StatsRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    SetMatch3StatsUnknown();
                    if (debugUiStats)
                        Debug.Log($"[MainMenu] Match3Stats RPC not ok. payload={payload}");
                    return;
                }

                var played = Mathf.Max(0, model.played).ToString();
                var wins = Mathf.Max(0, model.wins).ToString();
                var losses = Mathf.Max(0, model.losses).ToString();
                SetMatch3Text(ref _match3PlayedText, ref _match3PlayedTmp, played);
                SetMatch3Text(ref _match3WinsText, ref _match3WinsTmp, wins);
                SetMatch3Text(ref _match3LossesText, ref _match3LossesTmp, losses);
                if (debugUiStats)
                    Debug.Log($"[MainMenu] Match3Stats OK. played={played} wins={wins} losses={losses} raw={payload}");
            }
            catch
            {
                SetMatch3StatsUnknown();
                if (debugUiStats) Debug.Log("[MainMenu] Match3Stats exception (see previous).");
            }
        }

        private void SetMatch3StatsUnknown()
        {
            SetMatch3Text(ref _match3PlayedText, ref _match3PlayedTmp, "—");
            SetMatch3Text(ref _match3WinsText, ref _match3WinsTmp, "—");
            SetMatch3Text(ref _match3LossesText, ref _match3LossesTmp, "—");
        }

        // ProfileProgressHud удалён: больше не генерируем прогресс в меню.

        private void ApplySafeAreaClamp()
        {
            // На некоторых Android-экранах safe area не совпадает с полным экраном.
            // Мы “поджимаем” UI внутрь, чтобы подписи не уходили за границы.
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) return;

            const float paddingPx = 14f;
            if (_match3StatsRoot != null && _match3StatsRoot.gameObject.activeSelf)
                ClampRectToSafeArea(_match3StatsRoot, canvasRect, safe, paddingPx);
        }

        private static void ClampRectToSafeArea(RectTransform rt, RectTransform canvasRect, Rect safePixels, float paddingPx)
        {
            if (rt == null || canvasRect == null) return;

            var minScreen = safePixels.position;
            var maxScreen = new Vector2(safePixels.position.x + safePixels.width, safePixels.position.y + safePixels.height);

            Vector2 safeLocalMin;
            Vector2 safeLocalMax;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, minScreen, null, out safeLocalMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, maxScreen, null, out safeLocalMax);

            float safeMinX = Mathf.Min(safeLocalMin.x, safeLocalMax.x);
            float safeMaxX = Mathf.Max(safeLocalMin.x, safeLocalMax.x);
            float safeMinY = Mathf.Min(safeLocalMin.y, safeLocalMax.y);
            float safeMaxY = Mathf.Max(safeLocalMin.y, safeLocalMax.y);

            var cornersWorld = new Vector3[4];
            rt.GetWorldCorners(cornersWorld);
            float minX = cornersWorld[0].x;
            float maxX = cornersWorld[0].x;
            float minY = cornersWorld[0].y;
            float maxY = cornersWorld[0].y;

            for (int i = 1; i < 4; i++)
            {
                minX = Mathf.Min(minX, cornersWorld[i].x);
                maxX = Mathf.Max(maxX, cornersWorld[i].x);
                minY = Mathf.Min(minY, cornersWorld[i].y);
                maxY = Mathf.Max(maxY, cornersWorld[i].y);
            }

            // Приводим world-координаты к локальным canvas-координатам
            var local0 = canvasRect.InverseTransformPoint(cornersWorld[0]);
            var local1 = canvasRect.InverseTransformPoint(cornersWorld[1]);
            var local2 = canvasRect.InverseTransformPoint(cornersWorld[2]);
            var local3 = canvasRect.InverseTransformPoint(cornersWorld[3]);
            var cornersLocal = new[] { local0, local1, local2, local3 };

            minX = cornersLocal[0].x;
            maxX = cornersLocal[0].x;
            minY = cornersLocal[0].y;
            maxY = cornersLocal[0].y;

            for (int i = 1; i < 4; i++)
            {
                minX = Mathf.Min(minX, cornersLocal[i].x);
                maxX = Mathf.Max(maxX, cornersLocal[i].x);
                minY = Mathf.Min(minY, cornersLocal[i].y);
                maxY = Mathf.Max(maxY, cornersLocal[i].y);
            }

            float offsetX = 0f;
            if (minX < safeMinX + paddingPx) offsetX = (safeMinX + paddingPx) - minX;
            else if (maxX > safeMaxX - paddingPx) offsetX = (safeMaxX - paddingPx) - maxX;

            float offsetY = 0f;
            if (minY < safeMinY + paddingPx) offsetY = (safeMinY + paddingPx) - minY;
            else if (maxY > safeMaxY - paddingPx) offsetY = (safeMaxY - paddingPx) - maxY;

            if (Mathf.Abs(offsetX) < 0.01f && Mathf.Abs(offsetY) < 0.01f) return;
            rt.anchoredPosition += new Vector2(offsetX, offsetY);
        }

        private void EnsureMatch3StatsToggleButton()
        {
            if (_match3StatsToggleButton != null) return;
            var layout = ResolveMainMenuHudLayoutRoot();
            if (layout == null) return;

            // Prefer a pre-placed toggle somewhere under the HUD (editable in prefab).
            var eyeRoot = FindRectTransformChildByName(layout, "StatsToggleEye");
            if (eyeRoot == null) return;

            _match3StatsToggleButton = eyeRoot.GetComponent<Button>();
            _match3StatsToggleImage = eyeRoot.GetComponentInChildren<Image>(true);
            if (_match3StatsToggleButton == null) return;

            UpdateMatch3StatsToggleVisual();
            _match3StatsToggleButton.onClick.AddListener(ToggleMatch3StatsCard);
        }

        private void ToggleMatch3StatsCard()
        {
            _match3StatsVisible = !_match3StatsVisible;
            EnsureMatch3StatsCard();
            if (_match3StatsRoot != null)
                _match3StatsRoot.gameObject.SetActive(_match3StatsVisible);
            UpdateMatch3StatsToggleVisual();
            ApplySafeAreaClamp();
        }

        private void UpdateMatch3StatsToggleVisual()
        {
            EnsureEyeSpritesReady();
            if (_match3StatsToggleImage == null) return;
            var sprite = _match3StatsVisible ? eyeOpenSprite : eyeClosedSprite;
            if (sprite != null)
                _match3StatsToggleImage.sprite = sprite;
        }

        private void EnsureEyeSpritesReady()
        {
            if (eyeTexture == null) return;

            var w = eyeTexture.width;
            var h = eyeTexture.height;
            if (w <= 0 || h <= 1) return;

            bool looksUnsliced =
                eyeOpenSprite == null ||
                eyeClosedSprite == null ||
                (Mathf.Abs(eyeOpenSprite.rect.height - h) < 0.5f && Mathf.Abs(eyeClosedSprite.rect.height - h) < 0.5f);
            if (!looksUnsliced) return;

            var half = h / 2f;
            // Unity sprite rect origin is bottom-left:
            // lower half = closed eye, upper half = open eye.
            eyeClosedSprite = Sprite.Create(eyeTexture, new Rect(0f, 0f, w, half), new Vector2(0.5f, 0.5f), 100f);
            eyeOpenSprite = Sprite.Create(eyeTexture, new Rect(0f, half, w, h - half), new Vector2(0.5f, 0.5f), 100f);
        }

        private void TryAutoAssignEyeSpritesInEditor()
        {
#if UNITY_EDITOR
            if (eyeTexture == null)
                eyeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/img/eye.png");
            if (eyeOpenSprite != null && eyeClosedSprite != null) return;
            var path = "Assets/_Project/img/eye.png";
            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            Sprite upper = null;
            Sprite lower = null;
            foreach (var obj in sprites)
            {
                if (obj is not Sprite s) continue;
                if (upper == null || s.rect.y > upper.rect.y) upper = s;
                if (lower == null || s.rect.y < lower.rect.y) lower = s;
            }

            eyeOpenSprite ??= upper;
            eyeClosedSprite ??= lower;
#endif
        }

        private void TryAutoAssignServerAuraIconInEditor()
        {
#if UNITY_EDITOR
            if (serverAuraButtonIcon == null)
                serverAuraButtonIcon = AssetDatabase.LoadAssetAtPath<Sprite>(ServerAuraButtonIconAssetPath);
#endif
        }

        private void EnsureOnlineBadge()
        {
            if (_onlineCountText != null || _onlineCountTmp != null) return;

            // Badge is expected to be present in MainMenuHudOverlay prefab as a child.
            var parent = ResolveMainMenuHudLayoutRoot();
            if (parent == null) return;

            if (_onlineBadgeInstance == null)
            {
                var badgeRt = FindRectTransformChildByName(parent, "OnlinePlayersBadge");
                if (badgeRt == null)
                {
                    if (debugUiStats)
                        Debug.Log("[MainMenu] OnlinePlayersBadge root not found under HUD/Canvas.");
                    return;
                }
                _onlineBadgeInstance = badgeRt.gameObject;
            }
            _onlineBadgeRect = _onlineBadgeInstance.transform as RectTransform;

            _onlineCountText = FindTextUnder(_onlineBadgeInstance.transform, "CountText");
            _onlineCountTmp = FindTmpTextUnder(_onlineBadgeInstance.transform, "CountText");
            SetOnlineCountText("—");
            if (debugUiStats)
            {
                Debug.Log("[MainMenu] OnlinePlayersBadge bindings: " +
                          $"CountText(Text={_onlineCountText != null}, TMP={_onlineCountTmp != null}).");
            }
        }

        private void EnsurePlayerUsernameLabel()
        {
            if (_playerUsernameText != null || _playerUsernameTmp != null) return;

            var parent = ResolveMainMenuHudLayoutRoot();
            if (parent == null) return;

            var logoRoot = FindRectTransformChildByName(parent, "BottomHeaderLogo");
            if (logoRoot == null) return;

            _playerUsernameText = FindTextUnder(logoRoot, "Label");
            _playerUsernameTmp = FindTmpTextUnder(logoRoot, "Label");
            var cached = GetCachedUsernameForCurrentContext();
            _lastUsername = string.IsNullOrWhiteSpace(cached) ? "—" : cached;
            SetPlayerUsernameText(_lastUsername);
        }

        private void EnsureServerAuraButton()
        {
            if (_serverAuraButtonGo != null) return;

            var parent = ResolveMainMenuHudLayoutRoot();
            if (parent == null) return;

            var logoRoot = FindRectTransformChildByName(parent, "BottomHeaderLogo");
            if (logoRoot == null) return;

            _serverAuraButtonGo = new GameObject("ServerAuraButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = _serverAuraButtonGo.GetComponent<RectTransform>();
            rt.SetParent(logoRoot, false);
            rt.sizeDelta = new Vector2(46f, 46f);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-6f, 6f);

            var img = _serverAuraButtonGo.GetComponent<Image>();
            TryAutoAssignServerAuraIconInEditor();
            if (serverAuraButtonIcon != null)
            {
                img.sprite = serverAuraButtonIcon;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            else if (debugUiStats)
                Debug.LogWarning("[MainMenu] serverAuraButtonIcon не назначен — кнопка аномалии без спрайта.");

            img.color = Color.white;

            var btn = _serverAuraButtonGo.GetComponent<Button>();
            btn.onClick.AddListener(ShowServerAuraModal);
        }

        private void EnsureServerAuraModal()
        {
            if (_serverAuraModalRoot != null) return;

            var parent = ResolveMainMenuHudLayoutRoot();
            if (parent == null) return;

            _serverAuraModalRoot = new GameObject("ServerAuraModal", typeof(RectTransform), typeof(Image));
            var rootRt = _serverAuraModalRoot.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            var dim = _serverAuraModalRoot.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var pr = panel.GetComponent<RectTransform>();
            pr.SetParent(rootRt, false);
            pr.anchorMin = new Vector2(0.12f, 0.22f);
            pr.anchorMax = new Vector2(0.88f, 0.78f);
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.16f, 0.98f);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.SetParent(pr, false);
            titleRt.anchorMin = new Vector2(0f, 0.86f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(16f, 0f);
            titleRt.offsetMax = new Vector2(-16f, -8f);
            var titleTx = titleGo.GetComponent<Text>();
            titleTx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleTx.fontSize = 35;
            titleTx.fontStyle = FontStyle.Bold;
            titleTx.alignment = TextAnchor.MiddleCenter;
            titleTx.color = new Color(0.95f, 0.88f, 0.65f);
            titleTx.text = "Аномалия сервера";

            var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(Text));
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.SetParent(pr, false);
            bodyRt.anchorMin = new Vector2(0f, 0.14f);
            bodyRt.anchorMax = new Vector2(1f, 0.84f);
            bodyRt.offsetMin = new Vector2(20f, 8f);
            bodyRt.offsetMax = new Vector2(-20f, -8f);
            _serverAuraModalBodyText = bodyGo.GetComponent<Text>();
            _serverAuraModalBodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _serverAuraModalBodyText.fontSize = 35;
            _serverAuraModalBodyText.alignment = TextAnchor.UpperLeft;
            _serverAuraModalBodyText.color = new Color(0.92f, 0.9f, 0.86f);
            _serverAuraModalBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _serverAuraModalBodyText.verticalOverflow = VerticalWrapMode.Overflow;

            var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var cr = closeGo.GetComponent<RectTransform>();
            cr.SetParent(pr, false);
            cr.anchorMin = new Vector2(0.2f, 0.04f);
            cr.anchorMax = new Vector2(0.8f, 0.12f);
            cr.offsetMin = cr.offsetMax = Vector2.zero;
            closeGo.GetComponent<Image>().color = new Color(0.32f, 0.26f, 0.22f, 1f);
            var closeBtn = closeGo.GetComponent<Button>();
            closeBtn.onClick.AddListener(HideServerAuraModal);

            var closeLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var clr = closeLabelGo.GetComponent<RectTransform>();
            clr.SetParent(closeGo.transform, false);
            clr.anchorMin = Vector2.zero;
            clr.anchorMax = Vector2.one;
            clr.offsetMin = clr.offsetMax = Vector2.zero;
            var closeTx = closeLabelGo.GetComponent<Text>();
            closeTx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeTx.fontSize = 35;
            closeTx.alignment = TextAnchor.MiddleCenter;
            closeTx.color = Color.white;
            closeTx.text = "Закрыть";

            var canvas = _serverAuraModalRoot.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 4000;
            _serverAuraModalRoot.AddComponent<GraphicRaycaster>();
            _serverAuraModalRoot.SetActive(false);
        }

        private void ShowServerAuraModal()
        {
            EnsureServerAuraModal();
            if (_serverAuraModalBodyText != null)
                _serverAuraModalBodyText.text = FormatServerAuraModalText(_serverAuraLast);
            _serverAuraModalRoot?.SetActive(true);
        }

        private void HideServerAuraModal()
        {
            if (_serverAuraModalRoot != null)
                _serverAuraModalRoot.SetActive(false);
        }

        private static string FormatServerAuraModalText(ServerAuraGetRpcResponse r)
        {
            if (r == null || !r.ok)
                return "Не удалось загрузить данные аномалии. Проверьте подключение.";
            if (!r.active)
                return "Сейчас нет активной серверной аномалии для PvE match3.";

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(r.title))
                sb.AppendLine(r.title.Trim());
            if (!string.IsNullOrWhiteSpace(r.description))
                sb.AppendLine(r.description.Trim());
            sb.AppendLine();
            if (r.endsAtUnix > 0)
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(r.endsAtUnix).ToLocalTime();
                sb.AppendLine("До: " + dto.ToString("g", CultureInfo.CurrentCulture));
            }
            else if (r.durationHours > 0.5f)
                sb.AppendLine($"Длительность (в конфиге): ~{r.durationHours:0.#} ч");

            void Line(string label, float v, string suffix = "%")
            {
                if (Mathf.Abs(v) < 0.0001f) return;
                sb.AppendLine($"{label}: {(v > 0 ? "+" : "")}{v:0.#}{suffix}");
            }

            Line("Все статы (кроме крита)", r.allStatsPct);
            Line("Крит", r.critPct);
            Line("Здоровье", r.hpPct);
            Line("Урон", r.damagePct);
            Line("Броня", r.armorPct);
            Line("Лечение", r.healingPct);
            Line("Опыт", r.xpBonusPct);
            Line("Таймер респавна монстров (+ ускоряет, − замедляет)", r.mineRespawnWaitPct);

            sb.AppendLine();
            sb.AppendLine("Действует в боях match3 PvE (шахта) на сервере.");
            return sb.ToString().TrimEnd();
        }

        private async Task ServerAuraLoopAsync(CancellationToken ct)
        {
            await Task.Yield();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RefreshServerAuraAsync(ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (debugUiStats)
                        Debug.LogWarning("[MainMenu] Server aura: " + e.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RefreshServerAuraAsync(CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady)
                return;
            var resp = await ServerAuraRpc.GetAsync(NakamaBootstrap.Instance.Client, NakamaBootstrap.Instance.Session, ct)
                .ConfigureAwait(true);
            _serverAuraLast = resp;
            ApplyServerAuraButtonVisual();
        }

        private void ApplyServerAuraButtonVisual()
        {
            if (_serverAuraButtonGo == null) return;
            var img = _serverAuraButtonGo.GetComponent<Image>();
            if (img == null) return;
            img.color = Color.white;
        }

        private void EnsureHeaderResources()
        {
            if (_headerResourcesRoot == null)
            {
                var parent = ResolveMainMenuHudLayoutRoot();
                if (parent == null) return;

                _headerResourcesRoot = FindChildByName(parent, "HeaderResources", StringComparison.OrdinalIgnoreCase);
                if (_headerResourcesRoot == null)
                {
                    if (debugUiStats)
                        Debug.Log("[MainMenu] HeaderResources root not found under HUD/Canvas.");
                    return;
                }
            }

            BindHeaderResource(_energyBinding, _headerResourcesRoot);
            BindHeaderResource(_oreBinding, _headerResourcesRoot);
            BindHeaderResource(_goldBinding, _headerResourcesRoot);
            BindHeaderResource(_ingotsBinding, _headerResourcesRoot);
            BindHeaderResource(_matterBinding, _headerResourcesRoot);
            BindHeaderResource(_keysBinding, _headerResourcesRoot);
        }

        private void TryInstallEnergyHeaderPurchase()
        {
            if (_energyHeaderPurchase != null || _headerResourcesRoot == null) return;
            if (!_energyBinding.IsBound) return;
            EnsureHeaderHudIconsForEnergyPurchase();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _energyHeaderPurchase = new EnergyHeaderPurchaseController(
                canvas.transform,
                _hudEnergySprite,
                _hudMatterSprite,
                _hudGoldSprite,
                async ct => { await RefreshHeaderResourcesAsync(ct).ConfigureAwait(true); },
                _onlineCts != null ? _onlineCts.Token : CancellationToken.None);
            _energyHeaderPurchase.EnsurePlusOnEnergyRow(_headerResourcesRoot);
        }

        private void EnsureHeaderHudIconsForEnergyPurchase()
        {
            if (_hudEnergySprite == null)
                _hudEnergySprite = energyBuyDialogEnergyIcon
                    ?? TryGetHeaderResourceIconSprite(_headerResourcesRoot, "Energy")
                    ?? LoadHudSprite(HudEnergyIconAssetPath);
            if (_hudMatterSprite == null)
                _hudMatterSprite = energyBuyDialogMatterIcon
                    ?? TryGetHeaderResourceIconSprite(_headerResourcesRoot, "matter")
                    ?? LoadHudSprite(HudMatterIconAssetPath);
            if (_hudGoldSprite == null)
                _hudGoldSprite = energyBuyDialogGoldIcon
                    ?? TryGetHeaderResourceIconSprite(_headerResourcesRoot, "Gold")
                    ?? LoadHudSprite(HudGoldIconAssetPath);
        }

        /// <summary>Берёт спрайт с уже размеченной строки ресурса в шапке (работает в сборке без Resources).</summary>
        private static Sprite TryGetHeaderResourceIconSprite(Transform headerRoot, string entryName)
        {
            if (headerRoot == null || string.IsNullOrEmpty(entryName)) return null;
            var entryRoot = FindChildByName(headerRoot, entryName, StringComparison.OrdinalIgnoreCase);
            if (entryRoot == null) return null;
            var valueRoot = FindChildByName(entryRoot, "Value", StringComparison.OrdinalIgnoreCase);
            foreach (var img in entryRoot.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.sprite == null) continue;
                if (valueRoot != null && img.transform.IsChildOf(valueRoot.transform)) continue;
                return img.sprite;
            }

            var iconTr = FindChildByName(entryRoot, "Icon", StringComparison.OrdinalIgnoreCase);
            if (iconTr != null)
            {
                var ic = iconTr.GetComponent<Image>();
                if (ic != null && ic.sprite != null) return ic.sprite;
            }

            return null;
        }

        private static Sprite LoadHudSprite(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
#if UNITY_EDITOR
            var byAssetPath = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (byAssetPath != null)
                return byAssetPath;
#endif
            var resourcesPath = assetPath.Replace("Assets/Resources/", string.Empty);
            if (resourcesPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - 4);
            return Resources.Load<Sprite>(resourcesPath);
        }

        private async Task RefreshHeaderResourcesAsync(CancellationToken ct)
        {
            EnsureHeaderResources();
            if (!HasAnyHeaderResourceBindings()) return;

            if (PlayerResourcesService.TryReadCached(out var cached))
            {
                SetHeaderResourceText(_energyBinding, FormatEnergy(cached.energy));
                SetHeaderResourceText(_oreBinding, FormatCompact(cached.ore));
                SetHeaderResourceText(_goldBinding, FormatCompact(cached.gold));
                SetHeaderResourceText(_ingotsBinding, FormatCompact(cached.ingots));
                SetHeaderResourceText(_matterBinding, FormatCompact(cached.matter));
                SetHeaderResourceText(_keysBinding, FormatCompact(cached.keys));
            }

            try
            {
                var model = await PlayerResourcesService.GetAsync(ct);
                if (model == null || !model.ok)
                {
                    SetHeaderResourcesUnknown();
                    if (debugUiStats)
                        Debug.Log($"[MainMenu] PlayerResources RPC not ok. err={model?.err}");
                    return;
                }

                SetHeaderResourceText(_energyBinding, FormatEnergy(model.energy));
                SetHeaderResourceText(_oreBinding, FormatCompact(model.ore));
                SetHeaderResourceText(_goldBinding, FormatCompact(model.gold));
                SetHeaderResourceText(_ingotsBinding, FormatCompact(model.ingots));
                SetHeaderResourceText(_matterBinding, FormatCompact(model.matter));
                SetHeaderResourceText(_keysBinding, FormatCompact(model.keys));
            }
            catch (OperationCanceledException)
            {
                // Scene is closing or object was disabled.
            }
            catch (Exception e)
            {
                SetHeaderResourcesUnknown();
                if (debugUiStats)
                    Debug.Log("[MainMenu] PlayerResources exception: " + e.Message);
            }
        }

        private void SetHeaderResourcesUnknown()
        {
            SetHeaderResourceText(_energyBinding, "—");
            SetHeaderResourceText(_oreBinding, "—");
            SetHeaderResourceText(_goldBinding, "—");
            SetHeaderResourceText(_ingotsBinding, "—");
            SetHeaderResourceText(_matterBinding, "—");
            SetHeaderResourceText(_keysBinding, "—");
        }

        private bool HasAnyHeaderResourceBindings()
        {
            return _energyBinding.IsBound ||
                   _oreBinding.IsBound ||
                   _goldBinding.IsBound ||
                   _ingotsBinding.IsBound ||
                   _matterBinding.IsBound ||
                   _keysBinding.IsBound;
        }

        private async Task OnlineLoopAsync(CancellationToken ct)
        {
            var nextStatsRefreshAt = 0f;
            var nextUsernameRefreshAt = 0f;
            var nextResourcesRefreshAt = 0f;
            while (!ct.IsCancellationRequested)
            {
                await RefreshOnlineCountAsync(ct);
                if (Time.unscaledTime >= nextStatsRefreshAt)
                {
                    await RefreshMatch3StatsCardAsync(ct);
                    nextStatsRefreshAt = Time.unscaledTime + Mathf.Max(2f, match3StatsPollSeconds);
                }
                if (Time.unscaledTime >= nextUsernameRefreshAt)
                {
                    await RefreshPlayerUsernameAsync(ct);
                    nextUsernameRefreshAt = Time.unscaledTime + Mathf.Max(2f, onlinePollSeconds);
                }
                if (Time.unscaledTime >= nextResourcesRefreshAt)
                {
                    await RefreshHeaderResourcesAsync(ct);
                    nextResourcesRefreshAt = Time.unscaledTime + Mathf.Max(2f, resourcesPollSeconds);
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1f, onlinePollSeconds)), ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task RefreshOnlineCountAsync(CancellationToken ct)
        {
            EnsureOnlineBadge();
            if (_onlineCountText == null && _onlineCountTmp == null) return;

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetOnlineCountText("—");
                    if (debugUiStats) Debug.Log("[MainMenu] OnlineCount: NakamaBootstrap.Instance == null");
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady)
                {
                    SetOnlineCountText("—");
                    if (debugUiStats) Debug.Log($"[MainMenu] OnlineCount: Nakama not ready. IsReady={NakamaBootstrap.Instance.IsReady}");
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcOnlinePingAndCount, "{}");

                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetOnlineCountText("—");
                    if (debugUiStats) Debug.Log("[MainMenu] OnlineCount RPC payload empty/null.");
                    return;
                }

                var model = JsonUtility.FromJson<OnlineCountRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    SetOnlineCountText("—");
                    if (debugUiStats) Debug.Log($"[MainMenu] OnlineCount RPC not ok. payload={payload}");
                    return;
                }

                var count = Mathf.Max(1, model.count);
                SetOnlineCountText(count.ToString());
                if (_lastOnlineCount >= 0 && _lastOnlineCount != count)
                {
                    TriggerBadgePulse();
                }
                _lastOnlineCount = count;
                if (debugUiStats) Debug.Log($"[MainMenu] OnlineCount OK. count={count} raw={payload}");
            }
            catch
            {
                SetOnlineCountText("—");
                if (debugUiStats) Debug.Log("[MainMenu] OnlineCount exception (see previous).");
            }
        }

        private async Task RefreshPlayerUsernameAsync(CancellationToken ct)
        {
            EnsurePlayerUsernameLabel();
            if (_playerUsernameText == null && _playerUsernameTmp == null) return;

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetPlayerUsernameText("—");
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (NakamaBootstrap.Instance.Session == null || NakamaBootstrap.Instance.Client == null)
                {
                    SetPlayerUsernameText("—");
                    return;
                }

                var acc = await NakamaBootstrap.Instance.Client.GetAccountAsync(
                    NakamaBootstrap.Instance.Session,
                    canceller: ct);
                var username = acc?.User?.Username;
                var nextUsername = string.IsNullOrWhiteSpace(username) ? "—" : username;
                if (!string.Equals(_lastUsername, nextUsername, StringComparison.Ordinal))
                {
                    _lastUsername = nextUsername;
                    SetPlayerUsernameText(nextUsername);
                }

                if (!string.IsNullOrWhiteSpace(username))
                {
                    var userId = NakamaBootstrap.Instance.Session?.UserId;
                    CacheKnownUsername(userId, username);
                }
            }
            catch
            {
                var fallback = GetCachedUsernameForCurrentContext();
                var next = string.IsNullOrWhiteSpace(fallback) ? "—" : fallback;
                if (!string.Equals(_lastUsername, next, StringComparison.Ordinal))
                {
                    _lastUsername = next;
                    SetPlayerUsernameText(next);
                }
            }
        }

        private static RectTransform FindCanvasRoot()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            return canvas != null ? canvas.transform as RectTransform : null;
        }

        /// <summary>
        /// Prefer this component's own canvas when <see cref="MainMenuController"/> sits on MainMenuHudOverlay
        /// (avoids picking Background2D / another Canvas via <see cref="FindCanvasRoot"/>).
        /// </summary>
        private RectTransform ResolveMainMenuHudLayoutRoot()
        {
            var selfRt = transform as RectTransform;
            if (selfRt != null)
            {
                if (FindRectTransformChildByName(selfRt, "OnlinePlayersBadge") != null ||
                    FindRectTransformChildByName(selfRt, "Match3StatsCard") != null)
                    return selfRt;
            }

            return FindHudOverlayRoot() ?? FindCanvasRoot();
        }

        private static RectTransform FindHudOverlayRoot()
        {
            const string baseName = "MainMenuHudOverlay";
            var cloneName = baseName + "(Clone)";

            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas == null) continue;
                var n = canvas.gameObject.name;
                if (string.Equals(n, baseName, StringComparison.Ordinal) ||
                    string.Equals(n, cloneName, StringComparison.Ordinal))
                    return canvas.transform as RectTransform;
            }

            var go = GameObject.Find(baseName) ?? GameObject.Find(cloneName);
            return go != null ? go.transform as RectTransform : null;
        }

        private static Text FindTextUnder(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<Text>(true);
            foreach (var t in all)
            {
                if (t != null && t.gameObject.name == name)
                    return t;
            }
            return null;
        }

        private static TMP_Text FindTmpTextUnder(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in all)
            {
                if (t != null && t.gameObject.name == name)
                    return t;
            }
            return null;
        }

        private static Transform FindChildByName(Transform root, string name, StringComparison comparison = StringComparison.Ordinal)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t != null && string.Equals(t.gameObject.name, name, comparison))
                    return t;
            }
            return null;
        }

        private static Text FindAnyTextOnOrUnder(Transform root)
        {
            if (root == null) return null;
            var self = root.GetComponent<Text>();
            return self != null ? self : root.GetComponentInChildren<Text>(true);
        }

        private static TMP_Text FindAnyTmpTextOnOrUnder(Transform root)
        {
            if (root == null) return null;
            var self = root.GetComponent<TMP_Text>();
            return self != null ? self : root.GetComponentInChildren<TMP_Text>(true);
        }

        private static void BindHeaderResource(ResourceValueBinding binding, Transform headerRoot)
        {
            if (binding == null || headerRoot == null || binding.IsBound) return;

            var entryRoot = FindChildByName(headerRoot, binding.entryName, StringComparison.OrdinalIgnoreCase);
            if (entryRoot == null) return;

            var valueRoot = FindChildByName(entryRoot, "Value", StringComparison.OrdinalIgnoreCase) ?? entryRoot;
            binding.uiText = FindAnyTextOnOrUnder(valueRoot);
            binding.tmpText = FindAnyTmpTextOnOrUnder(valueRoot);
        }

        private static void SetHeaderResourceText(ResourceValueBinding binding, string value)
        {
            if (binding == null) return;
            if (binding.uiText != null) binding.uiText.text = value;
            if (binding.tmpText != null) binding.tmpText.text = value;
        }

        private static void SetMatch3Text(ref Text uiText, ref TMP_Text tmpText, string value)
        {
            if (uiText != null) uiText.text = value;
            if (tmpText != null) tmpText.text = value;
        }

        private static string FormatEnergy(int current)
        {
            var safeCurrent = Mathf.Max(0, current);
            return safeCurrent.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatCompact(long value)
        {
            var safeValue = Math.Max(0L, value);
            var abs = Math.Abs((double)safeValue);
            if (abs < 1_000d)
                return safeValue.ToString(CultureInfo.InvariantCulture);
            if (abs < 1_000_000d)
                return FormatCompactScaled(safeValue / 1_000d, "К");
            if (abs < 1_000_000_000d)
                return FormatCompactScaled(safeValue / 1_000_000d, "М");
            if (abs < 1_000_000_000_000d)
                return FormatCompactScaled(safeValue / 1_000_000_000d, "Млрд");
            return FormatCompactScaled(safeValue / 1_000_000_000_000d, "Т");
        }

        private static string FormatCompactScaled(double value, string suffix)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture) + suffix;
        }

        private bool HasMatch3StatsBindings()
        {
            // Accept either UI.Text or TMP_Text for each value.
            var hasPlayed = _match3PlayedText != null || _match3PlayedTmp != null;
            var hasWins = _match3WinsText != null || _match3WinsTmp != null;
            var hasLosses = _match3LossesText != null || _match3LossesTmp != null;
            return hasPlayed && hasWins && hasLosses;
        }

        private void SetOnlineCountText(string value)
        {
            if (_onlineCountText != null) _onlineCountText.text = value;
            if (_onlineCountTmp != null) _onlineCountTmp.text = value;
        }

        private void SetPlayerUsernameText(string value)
        {
            if (_playerUsernameText != null) _playerUsernameText.text = value;
            if (_playerUsernameTmp != null) _playerUsernameTmp.text = value;
        }

        private static void CacheKnownUsername(string userId, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                PlayerPrefs.SetString(PrefLastKnownUserId, userId);
                PlayerPrefs.SetString(PrefUserNameByUserIdPrefix + userId, username);
            }
            PlayerPrefs.SetString(PrefLastKnownUsername, username);
            PlayerPrefs.Save();
        }

        private static string GetCachedUsernameForCurrentContext()
        {
            var currentUserId = NakamaBootstrap.Instance?.Session?.UserId;
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                var perUser = PlayerPrefs.GetString(PrefUserNameByUserIdPrefix + currentUserId, "");
                if (!string.IsNullOrWhiteSpace(perUser))
                    return perUser;
            }

            var lastUserId = PlayerPrefs.GetString(PrefLastKnownUserId, "");
            if (!string.IsNullOrWhiteSpace(currentUserId) &&
                !string.IsNullOrWhiteSpace(lastUserId) &&
                !string.Equals(currentUserId, lastUserId, StringComparison.Ordinal))
            {
                return "";
            }

            return PlayerPrefs.GetString(PrefLastKnownUsername, "");
        }

        private static RectTransform FindRectTransformChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<RectTransform>(true);
            foreach (var rt in all)
            {
                if (rt != null && rt.gameObject.name == name)
                    return rt;
            }
            return null;
        }

        private static Text CreateStatsText(string name, RectTransform parent, string value, int size, Color color)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.font = GetDefaultBuiltinFont();
            txt.fontSize = size;
            txt.color = color;
            txt.text = value;
            txt.raycastTarget = false;
            return txt;
        }

        private static Font GetDefaultBuiltinFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch
            {
                // ignore and try legacy fallback below
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, TextAnchor align)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var txt = rt.GetComponent<Text>();
            if (txt != null) txt.alignment = align;
        }

        private void TriggerBadgePulse()
        {
            if (_onlineBadgeRect == null) return;
            if (_badgePulseRoutine != null)
                StopCoroutine(_badgePulseRoutine);
            _badgePulseRoutine = StartCoroutine(BadgePulseRoutine());
        }

        private System.Collections.IEnumerator BadgePulseRoutine()
        {
            if (_onlineBadgeRect == null) yield break;
            var startScale = _onlineBadgeRect.localScale;
            var peakScale = startScale * 1.12f;
            var up = 0f;
            const float upDur = 0.14f;
            while (up < upDur)
            {
                up += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(up / upDur);
                _onlineBadgeRect.localScale = Vector3.Lerp(startScale, peakScale, t);
                yield return null;
            }

            var down = 0f;
            const float downDur = 0.22f;
            while (down < downDur)
            {
                down += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(down / downDur);
                _onlineBadgeRect.localScale = Vector3.Lerp(peakScale, startScale, t);
                yield return null;
            }

            _onlineBadgeRect.localScale = startScale;
            _badgePulseRoutine = null;
        }

        private sealed class ResourceValueBinding
        {
            public readonly string entryName;
            public Text uiText;
            public TMP_Text tmpText;

            public ResourceValueBinding(string entryName)
            {
                this.entryName = entryName;
            }

            public bool IsBound => uiText != null || tmpText != null;
        }

        [Serializable]
        private sealed class OnlineCountRpcResponse
        {
            public bool ok;
            public int count;
            public string err;
        }

        [Serializable]
        private sealed class Match3StatsRpcResponse
        {
            public bool ok;
            public int played;
            public int wins;
            public int losses;
            public string err;
        }
    }
}

