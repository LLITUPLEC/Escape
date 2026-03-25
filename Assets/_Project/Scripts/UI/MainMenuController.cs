using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using TMPro;
using UnityEngine;
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
        [SerializeField] private float match3StatsPollSeconds = 5f;
        [Header("Debug")]
        [SerializeField] private bool debugUiStats = false;

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcMatch3StatsGet = "duel_match3_stats_get";
        private const string RpcMatch3PveCatalogGet = "duel_match3_pve_catalog_get";
        private Text _onlineCountText;
        private TMP_Text _onlineCountTmp;
        private CancellationTokenSource _onlineCts;
        private GameObject _onlineBadgeInstance;
        private RectTransform _onlineBadgeRect;
        private int _lastOnlineCount = -1;
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
        private bool _match3StatsVisible;

        private void Awake()
        {
            TryAutoAssignEyeSpritesInEditor();
            EnsureOnlineBadge();
            EnsureMatch3StatsCard();
            EnsureMatch3StatsToggleButton();
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

        private async Task OnlineLoopAsync(CancellationToken ct)
        {
            var nextStatsRefreshAt = 0f;
            while (!ct.IsCancellationRequested)
            {
                await RefreshOnlineCountAsync(ct);
                if (Time.unscaledTime >= nextStatsRefreshAt)
                {
                    await RefreshMatch3StatsCardAsync(ct);
                    nextStatsRefreshAt = Time.unscaledTime + Mathf.Max(2f, match3StatsPollSeconds);
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

        private static void SetMatch3Text(ref Text uiText, ref TMP_Text tmpText, string value)
        {
            if (uiText != null) uiText.text = value;
            if (tmpText != null) tmpText.text = value;
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

