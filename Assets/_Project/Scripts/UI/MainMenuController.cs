using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
using Project.Nakama;
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
        [SerializeField] private Button duelButton;
        [SerializeField] private Button botsButton;
        [SerializeField] private Button match3Button;
        [Header("Online Badge")]
        [SerializeField] private RectTransform onlineBadgeParent;
        [SerializeField] private GameObject onlineBadgePrefab;
        [SerializeField] private string onlineBadgeResourcePath = "OnlinePlayersBadge";
        [SerializeField] private float onlinePollSeconds = 5f;
        [SerializeField] private float match3StatsPollSeconds = 5f;

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcMatch3StatsGet = "duel_match3_stats_get";
        private const string RpcMatch3PveCatalogGet = "duel_match3_pve_catalog_get";
        private Text _onlineCountText;
        private CancellationTokenSource _onlineCts;
        private GameObject _onlineBadgeInstance;
        private RectTransform _onlineBadgeRect;
        private int _lastOnlineCount = -1;
        private Coroutine _badgePulseRoutine;
        private RectTransform _match3StatsRoot;
        private Text _match3PlayedText;
        private Text _match3WinsText;
        private Text _match3LossesText;
        private Button _match3StatsToggleButton;
        private Image _match3StatsToggleImage;
        [SerializeField] private Texture2D eyeTexture;
        [SerializeField] private Sprite eyeOpenSprite;
        [SerializeField] private Sprite eyeClosedSprite;
        private bool _match3StatsVisible;
        private RectTransform _profileHudRoot;
        private Text _profileLevelText;
        private Text _profileGoldText;
        private Text _profileXpText;
        private Image _profileXpFill;
        private RectTransform _profileXpFillRt;
        private int[] _levelXp = { 0, 100, 240, 420, 650, 940, 1300, 1740, 2280, 2920 };

        public void Bind(Button duel, Button bots, Button match3 = null)
        {
            duelButton   = duel;
            botsButton   = bots;
            match3Button = match3;
        }

        private void Awake()
        {
            if (duelButton   != null) duelButton.onClick.AddListener(OnDuelClicked);
            if (botsButton   != null) botsButton.onClick.AddListener(OnBotsClicked);
            if (match3Button != null) match3Button.onClick.AddListener(OnMatch3Clicked);
            TryAutoAssignEyeSpritesInEditor();
            EnsureOnlineBadge();
            EnsureMatch3StatsCard();
            EnsureMatch3StatsToggleButton();
            EnsureProfileHud();
            ApplySafeAreaClamp();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            TryAutoAssignEyeSpritesInEditor();
        }
#endif

        private void OnEnable()
        {
            _onlineCts = new CancellationTokenSource();
            _ = OnlineLoopAsync(_onlineCts.Token);
            _ = RefreshMatch3StatsCardAsync(_onlineCts.Token);
            _ = RefreshProfileHudAsync(_onlineCts.Token);
            ApplySafeAreaClamp();
        }

        private void OnDisable()
        {
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

        private void OnDuelClicked()
        {
            SceneManager.LoadScene("DuelRoom");
        }

        private void OnBotsClicked()
        {
            Match3LaunchContext.SetMode(Match3LaunchMode.SoloBot);
            SceneManager.LoadScene("DuelMatch3");
        }

        private void OnMatch3Clicked()
        {
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            SceneManager.LoadScene("DuelMatch3");
        }

        private void EnsureMatch3StatsCard()
        {
            var parent = FindCanvasRoot();
            if (parent == null) return;

            if (_match3StatsRoot == null)
            {
                var cardGo = new GameObject("Match3StatsCard");
                _match3StatsRoot = cardGo.AddComponent<RectTransform>();
                _match3StatsRoot.SetParent(parent, false);
            }
            // Keep fixed card size and move it up by 125 px.
            _match3StatsRoot.anchorMin = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.anchorMax = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.pivot = new Vector2(0.5f, 0.5f);
            _match3StatsRoot.sizeDelta = new Vector2(320f, 420f);
            _match3StatsRoot.anchoredPosition = new Vector2(0f, 125f);
            _match3StatsRoot.gameObject.SetActive(_match3StatsVisible);

            if (_match3PlayedText != null && _match3WinsText != null && _match3LossesText != null)
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
            if (_match3PlayedText == null || _match3WinsText == null || _match3LossesText == null) return;
            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsGet, "{}");
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                var model = JsonUtility.FromJson<Match3StatsRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                _match3PlayedText.text = Mathf.Max(0, model.played).ToString();
                _match3WinsText.text = Mathf.Max(0, model.wins).ToString();
                _match3LossesText.text = Mathf.Max(0, model.losses).ToString();
            }
            catch
            {
                SetMatch3StatsUnknown();
            }
        }

        private void SetMatch3StatsUnknown()
        {
            if (_match3PlayedText != null) _match3PlayedText.text = "—";
            if (_match3WinsText != null) _match3WinsText.text = "—";
            if (_match3LossesText != null) _match3LossesText.text = "—";
        }

        private void EnsureProfileHud()
        {
            if (_profileHudRoot != null && _profileLevelText != null && _profileGoldText != null && _profileXpText != null && _profileXpFill != null)
                return;

            var parent = FindCanvasRoot();
            if (parent == null) return;

            if (_profileHudRoot == null)
            {
                var cardGo = new GameObject("ProfileProgressHud");
                _profileHudRoot = cardGo.AddComponent<RectTransform>();
                _profileHudRoot.SetParent(parent, false);
            }

            var avatar = FindAvatarRect(parent);
            if (avatar != null)
            {
                _profileHudRoot.anchorMin = avatar.anchorMin;
                _profileHudRoot.anchorMax = avatar.anchorMax;
                _profileHudRoot.pivot = avatar.pivot;
                _profileHudRoot.sizeDelta = new Vector2(Mathf.Max(220f, avatar.rect.width), 74f);
            }
            else
            {
                _profileHudRoot.anchorMin = new Vector2(0.12f, 0.78f);
                _profileHudRoot.anchorMax = new Vector2(0.12f, 0.78f);
                _profileHudRoot.pivot = new Vector2(0.5f, 0.5f);
                _profileHudRoot.sizeDelta = new Vector2(280f, 74f);
            }
            _profileHudRoot.anchoredPosition = new Vector2(-60f, 170f);

            if (_profileLevelText != null) return;

            var bg = _profileHudRoot.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.09f, 0.15f, 0.88f);
            var outline = _profileHudRoot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.20f, 0.70f, 0.95f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);

            _profileLevelText = CreateStatsText("LevelText", _profileHudRoot, "Lvl 1", 16, new Color(0.80f, 0.96f, 1f));
            Anchor(_profileLevelText.rectTransform, new Vector2(0.05f, 0.52f), new Vector2(0.48f, 0.94f), TextAnchor.MiddleLeft);
            _profileGoldText = CreateStatsText("GoldText", _profileHudRoot, "Gold: 0", 16, new Color(1f, 0.88f, 0.35f));
            Anchor(_profileGoldText.rectTransform, new Vector2(0.52f, 0.52f), new Vector2(0.95f, 0.94f), TextAnchor.MiddleRight);

            _profileXpText = CreateStatsText("XpText", _profileHudRoot, "XP 0/100", 13, Color.white);
            Anchor(_profileXpText.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.40f), TextAnchor.MiddleCenter);

            var barBgGo = new GameObject("XpBarBg");
            var barBgRt = barBgGo.AddComponent<RectTransform>();
            barBgRt.SetParent(_profileHudRoot, false);
            barBgRt.anchorMin = new Vector2(0.05f, 0.40f);
            barBgRt.anchorMax = new Vector2(0.95f, 0.50f);
            barBgRt.offsetMin = barBgRt.offsetMax = Vector2.zero;
            var barBg = barBgGo.AddComponent<Image>();
            barBg.color = new Color(0.08f, 0.11f, 0.16f, 0.95f);

            var fillGo = new GameObject("XpBarFill");
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.SetParent(barBgRt, false);
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);
            _profileXpFillRt = fillRt;
            _profileXpFill = fillGo.AddComponent<Image>();
            _profileXpFill.color = new Color(0.24f, 0.75f, 0.95f, 1f);
        }

        private static RectTransform FindAvatarRect(RectTransform root)
        {
            if (root == null) return null;
            var direct = root.Find("MainMenuScreen/Canvas/Panel/Avatar");
            if (direct is RectTransform rtDirect) return rtDirect;
            var fallback = root.Find("Panel/Avatar");
            if (fallback is RectTransform rtFallback) return rtFallback;
            return null;
        }

        private async Task RefreshProfileHudAsync(CancellationToken ct)
        {
            EnsureProfileHud();
            if (_profileLevelText == null || _profileGoldText == null || _profileXpText == null || _profileXpFill == null) return;
            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetProfileUnknown();
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                {
                    SetProfileUnknown();
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3PveCatalogGet, "{}");
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetProfileUnknown();
                    return;
                }

                var model = JsonUtility.FromJson<PveCatalogHudRpcResponse>(payload);
                if (model == null || !model.ok || model.progression == null)
                {
                    SetProfileUnknown();
                    return;
                }

                if (model.level_xp != null && model.level_xp.Length > 1)
                    _levelXp = model.level_xp;

                var level = Mathf.Max(1, model.progression.level);
                var xp = Mathf.Max(0, model.progression.xp);
                var gold = Mathf.Max(0, model.progression.gold);
                var currentReq = level >= 1 && level <= _levelXp.Length ? _levelXp[level - 1] : 0;
                var nextReq = level < _levelXp.Length ? _levelXp[level] : currentReq;
                var denom = Mathf.Max(1, nextReq - currentReq);
                var frac = level >= _levelXp.Length ? 1f : Mathf.Clamp01((xp - currentReq) / (float)denom);

                _profileLevelText.text = $"Lvl {level}";
                _profileGoldText.text = $"Gold: {gold}";
                _profileXpText.text = level >= _levelXp.Length ? $"XP {xp} (MAX)" : $"XP {xp}/{nextReq}";
                if (_profileXpFillRt != null)
                    _profileXpFillRt.anchorMax = new Vector2(frac, 1f);
            }
            catch
            {
                SetProfileUnknown();
            }
        }

        private void SetProfileUnknown()
        {
            if (_profileLevelText != null) _profileLevelText.text = "Lvl —";
            if (_profileGoldText != null) _profileGoldText.text = "Gold: —";
            if (_profileXpText != null) _profileXpText.text = "XP —";
            if (_profileXpFillRt != null) _profileXpFillRt.anchorMax = new Vector2(0f, 1f);
        }

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
            if (_profileHudRoot != null && _profileHudRoot.gameObject.activeSelf)
                ClampRectToSafeArea(_profileHudRoot, canvasRect, safe, paddingPx);
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
            if (match3Button == null) return;

            var root = match3Button.transform as RectTransform;
            if (root == null) return;

            var eyeGo = new GameObject("StatsToggleEye");
            var rt = eyeGo.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(34f, 34f);
            rt.anchoredPosition = new Vector2(28f, 0f);

            var bg = eyeGo.AddComponent<Image>();
            bg.color = new Color(0x8C / 255f, 0xBD / 255f, 0xF1 / 255f, 0.92f);
            var btn = eyeGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            _match3StatsToggleButton = btn;

            var iconGo = new GameObject("EyeIcon");
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.14f, 0.14f);
            iconRt.anchorMax = new Vector2(0.86f, 0.86f);
            iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
            _match3StatsToggleImage = iconGo.AddComponent<Image>();
            _match3StatsToggleImage.raycastTarget = false;
            _match3StatsToggleImage.preserveAspect = true;
            UpdateMatch3StatsToggleVisual();

            if (_match3StatsToggleImage.sprite == null)
            {
                var lbl = CreateStatsText("EyeText", rt, "eye", 13, Color.white);
                Anchor(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), TextAnchor.MiddleCenter);
            }
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

        private void EnsureOnlineBadge()
        {
            if (_onlineCountText != null) return;

            if (onlineBadgePrefab == null && !string.IsNullOrEmpty(onlineBadgeResourcePath))
            {
                onlineBadgePrefab = Resources.Load<GameObject>(onlineBadgeResourcePath);
            }

            if (onlineBadgePrefab == null) return;

            var parent = onlineBadgeParent != null ? onlineBadgeParent : FindCanvasRoot();
            if (parent == null) return;

            if (_onlineBadgeInstance == null)
            {
                _onlineBadgeInstance = Instantiate(onlineBadgePrefab, parent, false);
                _onlineBadgeInstance.name = "OnlinePlayersBadge";
            }
            _onlineBadgeRect = _onlineBadgeInstance.transform as RectTransform;

            var allTexts = _onlineBadgeInstance.GetComponentsInChildren<Text>(true);
            foreach (var t in allTexts)
            {
                if (t != null && string.Equals(t.gameObject.name, "CountText", StringComparison.Ordinal))
                {
                    _onlineCountText = t;
                    break;
                }
            }
            if (_onlineCountText != null)
                _onlineCountText.text = "—";
        }

        private async Task OnlineLoopAsync(CancellationToken ct)
        {
            var nextStatsRefreshAt = 0f;
            var nextProfileRefreshAt = 0f;
            while (!ct.IsCancellationRequested)
            {
                await RefreshOnlineCountAsync(ct);
                if (Time.unscaledTime >= nextStatsRefreshAt)
                {
                    await RefreshMatch3StatsCardAsync(ct);
                    nextStatsRefreshAt = Time.unscaledTime + Mathf.Max(2f, match3StatsPollSeconds);
                }
                if (Time.unscaledTime >= nextProfileRefreshAt)
                {
                    await RefreshProfileHudAsync(ct);
                    nextProfileRefreshAt = Time.unscaledTime + Mathf.Max(2f, match3StatsPollSeconds);
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
            if (_onlineCountText == null) return;

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcOnlinePingAndCount, "{}");

                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var model = JsonUtility.FromJson<OnlineCountRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var count = Mathf.Max(1, model.count);
                _onlineCountText.text = count.ToString();
                if (_lastOnlineCount >= 0 && _lastOnlineCount != count)
                {
                    TriggerBadgePulse();
                }
                _lastOnlineCount = count;
            }
            catch
            {
                _onlineCountText.text = "—";
            }
        }

        private static RectTransform FindCanvasRoot()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            return canvas != null ? canvas.transform as RectTransform : null;
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

        [Serializable]
        private sealed class PveCatalogHudRpcResponse
        {
            public bool ok;
            public PveProgressHudInfo progression;
            public int[] level_xp;
            public string err;
        }

        [Serializable]
        private sealed class PveProgressHudInfo
        {
            public int level = 1;
            public int xp;
            public int gold;
        }
    }
}

