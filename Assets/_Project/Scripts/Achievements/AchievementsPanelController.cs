using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Журнал достижений, вкладки и получение наград за ступени.</summary>
    public sealed class AchievementsPanelController : MonoBehaviour
    {
        public const string PanelRootName = "AchievementsPanelRoot";
        private const string OpenButtonName = "BottomButtonAchiev";

        [SerializeField] private RectTransform sheetRect;
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;
        [Header("Иконки достижений (одна на chain id)")]
        [SerializeField] private AchievementIconCatalog iconCatalog;

        [Header("Шрифт UI (TF2CSecondary: Assets/_Project/Fonts)")]
        [SerializeField] private TMP_FontAsset achievementUiFont;

        [SerializeField] private float slideDuration = 0.38f;
        [SerializeField] private float hiddenAnchoredY = -1400f;

        private Button _openButton;
        private readonly List<ExpeditionAchievementJournalRow> _spawnedRows = new();
        private readonly Dictionary<AchievementTab, float> _scrollPositions = new();
        private Coroutine _slideRoutine;
        private Vector2 _sheetShownPos;
        private AchievementTab _currentTab = AchievementTab.Obsession;
        private bool _hasSelectedTab;
        private bool _wiredOpenButton;
        private AchievementStepDetailModal _stepDetailModal;
        private RectTransform _modalHost;
        private ExpeditionAchievementsLayout _expeditionLayout;
        private bool _visible;
        private bool _shellAttached;
        public bool IsVisible => _visible;
        public event Action<bool> VisibilityChanged;

        private RectTransform _pendingBadgeRt;
        private CanvasGroup _pendingBadgeCanvasGroup;
        private TMP_Text _pendingBadgeText;
        private BottomButtonAchievementBadgeConfig _openButtonBadgeConfig;

        internal TMP_FontAsset AchievementUiFontReference => achievementUiFont;

        private void Awake()
        {
            AchievementProgressStorage.EnsureLoaded();
            ResolveRefsBestEffort();
            if (ExpeditionTheme.Font != null) achievementUiFont = ExpeditionTheme.Font;
            closeButton = ModalPanelCloseButton.EnsureTopRight(
                closeButton,
                sheetRect,
                transform,
                "AchievementsSheet/Header/CloseButton",
                achievementUiFont,
                Hide);
            _expeditionLayout = ExpeditionUi.Ensure<ExpeditionAchievementsLayout>(gameObject);
            _expeditionLayout.Build(sheetRect, SwitchTab);
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, achievementUiFont);
            if (sheetRect != null)
            {
                _sheetShownPos = Vector2.zero;
                sheetRect.anchoredPosition = new Vector2(_sheetShownPos.x, hiddenAnchoredY);
            }

            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 0f;
                rootCanvasGroup.blocksRaycasts = false;
                rootCanvasGroup.interactable = false;
            }

            gameObject.SetActive(true);

            ModalPanelCloseButton.EnsureDimmerRaycast(dimmerButton);
            if (dimmerButton != null)
                dimmerButton.onClick.AddListener(Hide);

            TryBindOpenButton();
            EnsurePendingClaimBadge();
            SwitchTab(AchievementSections.All[0].Tab);
        }

        private void OnEnable()
        {
            TryBindOpenButton();
            EnsurePendingClaimBadge();
            RefreshClaimBadges();
            AchievementLifecycle.OnDataChanged += OnAchievementLifecycleChanged;
            _ = AchievementCatalogService.RefreshBeforePanelOpenAsync(CancellationToken.None);
        }

        private void OnDisable()
        {
            AchievementLifecycle.OnDataChanged -= OnAchievementLifecycleChanged;
            if (_stepDetailModal != null) _stepDetailModal.Hide();
        }

        private void OnDestroy()
        {
            if (_stepDetailModal != null && !_stepDetailModal.transform.IsChildOf(transform))
            {
                _stepDetailModal.Hide();
                if (Application.isPlaying) Destroy(_stepDetailModal.gameObject);
                else DestroyImmediate(_stepDetailModal.gameObject);
            }
            if (_openButton != null)
                _openButton.onClick.RemoveListener(TogglePanel);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Hide);
            if (dimmerButton != null)
                dimmerButton.onClick.RemoveListener(Hide);
        }

        private void OnAchievementLifecycleChanged()
        {
            if (_visible)
            {
                RefreshGrid(_currentTab);
                RefreshOpenDetail();
            }
            RefreshClaimBadges();
        }

        private void Update()
        {
            if (!_visible) return;
            // Keep the rim visible between peaks so availability is readable at a glance.
            var opacity = .82f + .18f * Mathf.Cos(Time.unscaledTime * (2f * Mathf.PI / 2.4f));
            foreach (var row in _spawnedRows)
                row.SetClaimFrameIntensity(opacity);
        }

        private void ResolveRefsBestEffort()
        {
            if (sheetRect == null)
            {
                var tr = transform.Find("AchievementsSheet");
                if (tr != null) sheetRect = tr as RectTransform;
            }

            if (rootCanvasGroup == null)
                rootCanvasGroup = GetComponent<CanvasGroup>();

            if (closeButton == null)
            {
                var b = transform.Find("AchievementsSheet/Header/CloseButton");
                if (b != null) closeButton = b.GetComponent<Button>();
            }

            if (dimmerButton == null)
            {
                var d = transform.Find("AchievementsDimmer");
                if (d != null) dimmerButton = d.GetComponent<Button>();
            }
        }

        private void SwitchTab(AchievementTab tab)
        {
            if (_hasSelectedTab)
            {
                if (tab == _currentTab) return;
                _scrollPositions[_currentTab] = _expeditionLayout.ScrollPosition;
            }
            _currentTab = tab;
            _hasSelectedTab = true;
            _stepDetailModal?.Hide();
            _expeditionLayout.SetActiveTab(tab);
            RefreshGrid(tab);
            _expeditionLayout.RestoreScrollPosition(_scrollPositions.TryGetValue(tab, out var position) ? position : 1f);
        }

        private void RefreshGrid(AchievementTab tab)
        {
            var host = _expeditionLayout?.Content;
            if (host == null) return;

            var rowIndex = 0;
            var collected = 0;
            var total = 0;
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain?.Thresholds == null || chain.Thresholds.Length == 0 || chain.Tab != tab) continue;
                if (rowIndex == _spawnedRows.Count)
                    _spawnedRows.Add(ExpeditionAchievementJournalRow.Create(host, OpenStepDetail));
                var row = _spawnedRows[rowIndex++];
                row.Bind(chain, GetMedalIcon(chain.ChainId));
                row.gameObject.SetActive(true);
                collected += row.ClaimedSteps;
                total += chain.Thresholds.Length;
            }

            for (var i = rowIndex; i < _spawnedRows.Count; i++)
                _spawnedRows[i].gameObject.SetActive(false);
            _expeditionLayout.SetEmpty(rowIndex == 0);
            _expeditionLayout.PresentCollection(collected, total);
        }

        private Sprite GetMedalIcon(string chainId) => iconCatalog != null ? iconCatalog.GetDisplayIcon(chainId) : null;

        private void OpenStepDetail(string chainId, int stepIndex)
        {
            var chain = AchievementCatalog.FindChain(chainId);
            if (chain?.Thresholds == null || stepIndex < 0 || stepIndex >= chain.Thresholds.Length) return;
            if (_stepDetailModal == null)
                _stepDetailModal = AchievementStepDetailModal.Ensure(_modalHost != null ? _modalHost : transform, achievementUiFont);
            _stepDetailModal.Show(chain, stepIndex, GetMedalIcon(chainId), ClaimStepRoutineAsync);
        }

        private void RefreshOpenDetail()
        {
            if (_stepDetailModal == null || !_stepDetailModal.IsVisible) return;
            var chainId = _stepDetailModal.ChainId;
            _stepDetailModal.Refresh(AchievementCatalog.FindChain(chainId), GetMedalIcon(chainId));
        }

        private async void ClaimStepRoutineAsync(string chainId, int stepIndex)
        {
            await AchievementRewardClaim.TryClaimStepAsync(chainId, stepIndex, CancellationToken.None);
            if (this == null) return;
            RefreshGrid(_currentTab);
            RefreshOpenDetail();
            RefreshClaimBadges();
        }

        private void TryBindOpenButton()
        {
            if (_wiredOpenButton) return;
            var canvasRoot = transform.root;
            var btnTr = FindDeepChild(canvasRoot, OpenButtonName);
            if (btnTr == null)
                return;
            _openButton = btnTr.GetComponent<Button>();
            if (_openButton == null)
                return;
            _openButton.onClick.AddListener(TogglePanel);
            _wiredOpenButton = true;
            EnsurePendingClaimBadge();
            RefreshClaimBadges();
        }

        private void EnsurePendingClaimBadge()
        {
            if (_openButton == null || _pendingBadgeRt != null)
                return;

            var parentRt = _openButton.transform as RectTransform;
            if (parentRt == null)
                return;

            if (_openButtonBadgeConfig == null)
                _openButtonBadgeConfig = _openButton.GetComponent<BottomButtonAchievementBadgeConfig>();

            var cfg = _openButtonBadgeConfig;
            var badgeSize = cfg != null ? cfg.BadgeSpriteSize : new Vector2(44f, 44f);
            var badgeBgColor = cfg != null
                ? cfg.BadgeColor
                : new Color(0.85f, 0.22f, 0.26f, 0.96f);

            var root = new GameObject("PendingClaimBadge", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(parentRt, false);
            root.transform.SetAsLastSibling();
            _pendingBadgeRt = root.GetComponent<RectTransform>();
            _pendingBadgeCanvasGroup = root.GetComponent<CanvasGroup>();
            _pendingBadgeRt.anchorMin = new Vector2(1f, 1f);
            _pendingBadgeRt.anchorMax = new Vector2(1f, 1f);
            _pendingBadgeRt.pivot = new Vector2(1f, 1f);
            _pendingBadgeRt.anchoredPosition = new Vector2(-10f, -8f);
            _pendingBadgeRt.sizeDelta = badgeSize;
            _pendingBadgeCanvasGroup.blocksRaycasts = false;
            _pendingBadgeCanvasGroup.interactable = false;

            var bgGo = new GameObject("BadgeBg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(_pendingBadgeRt, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var img = bgGo.GetComponent<Image>();
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            if (cfg != null && cfg.BadgeSprite != null)
            {
                img.sprite = cfg.BadgeSprite;
                img.color = badgeBgColor;
            }
            else
            {
                var t = Texture2D.whiteTexture;
                img.sprite = Sprite.Create(
                    t,
                    new Rect(0f, 0f, t.width, t.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                img.color = badgeBgColor;
            }

            var labelGo =
                new GameObject("BadgeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(_pendingBadgeRt, false);
            var lr = labelGo.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            var fo = AchievementUiFontReference;
            tmp.font = fo;
            tmp.fontSize = 22f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14f;
            tmp.fontSizeMax = 24f;
            tmp.alignment = TextAlignmentOptions.Midline;
            tmp.color = new Color(0.98f, 0.98f, 0.93f);

            AchievementsTmpMaterialRepair.RepairHierarchy(labelGo.transform, fo);
            _pendingBadgeText = tmp;

            RefreshClaimBadges();
        }

        private void RefreshClaimBadges()
        {
            EnsurePendingClaimBadge();
            if (_pendingBadgeText != null && _pendingBadgeCanvasGroup != null)
            {
                var count = AchievementUiPending.CountEligibleClaimSteps();
                _pendingBadgeText.text = count > 99 ? "99+" : count.ToString();
                _pendingBadgeCanvasGroup.alpha = count > 0 ? 1f : 0f;
            }
            _expeditionLayout?.RefreshBadges();
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeepChild(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        private void TogglePanel()
        {
            if (_visible)
                Hide();
            else
                Show();
        }

        private async void Show()
        {
            if (!_shellAttached)
            {
                var host = GameObject.Find("ExpeditionAchievementsPageHost");
                var modalHost = GameObject.Find("ExpeditionModalHost");
                if (host != null) AttachToShell(host.transform as RectTransform, modalHost != null ? modalHost.transform as RectTransform : null);
            }
            if (_visible) return;
            _visible = true;
            _expeditionLayout?.RefreshGeometry();
            if (sheetRect != null) _sheetShownPos = Vector2.zero;
            MainMenuHudLayering.BringPanelToFront(transform);
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.blocksRaycasts = true;
                rootCanvasGroup.interactable = true;
            }

            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            if (sheetRect != null && (rootCanvasGroup == null || rootCanvasGroup.alpha <= .01f)) sheetRect.anchoredPosition = new Vector2(0, -12);
            _slideRoutine = StartCoroutine(AnimateSlide(show: true));
            VisibilityChanged?.Invoke(true);
            RefreshGrid(_currentTab);
            RefreshClaimBadges();
            await AchievementCatalogService.RefreshBeforePanelOpenAsync(CancellationToken.None);
            if (this == null) return;
            RefreshGrid(_currentTab);
            RefreshClaimBadges();
        }

        public void Open() { Show(); }
        public void Close() { Hide(); }
        public void AttachToShell(RectTransform pageHost, RectTransform modalHost = null)
        {
            if (pageHost == null) return;
            _shellAttached = true;
            transform.SetParent(pageHost, false);
            ExpeditionUi.SetRect(transform as RectTransform, 0, 0, 1, 1);
            _modalHost = modalHost;
            if (_stepDetailModal != null && modalHost != null && _stepDetailModal.transform.parent != modalHost)
            {
                _stepDetailModal.transform.SetParent(modalHost, false);
                ExpeditionUi.SetRect(_stepDetailModal.transform as RectTransform, 0, 0, 1, 1);
            }
            if (_expeditionLayout != null) _expeditionLayout.SetShellLayout();
        }

        private void Hide()
        {
            _stepDetailModal?.Hide();
            if (!_visible) return;
            _visible = false;
            if (rootCanvasGroup != null) { rootCanvasGroup.blocksRaycasts = false; rootCanvasGroup.interactable = false; }
            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: false));
            VisibilityChanged?.Invoke(false);
        }

        private IEnumerator AnimateSlide(bool show)
        {
            float fromA = rootCanvasGroup != null ? rootCanvasGroup.alpha : 0f;
            float toA = show ? 1f : 0f;
            Vector2 fromY = sheetRect != null ? sheetRect.anchoredPosition : Vector2.zero;
            Vector2 toY = show ? Vector2.zero : new Vector2(0, -12);

            float t = 0f;
            var pageDuration = Mathf.Clamp(slideDuration, .08f, .18f);
            while (t < pageDuration)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / pageDuration);
                var ease = k * k * (3f - 2f * k);
                if (rootCanvasGroup != null)
                    rootCanvasGroup.alpha = Mathf.Lerp(fromA, toA, ease);
                if (sheetRect != null)
                    sheetRect.anchoredPosition = Vector2.Lerp(fromY, toY, ease);
                yield return null;
            }

            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = toA;
                rootCanvasGroup.blocksRaycasts = show;
                rootCanvasGroup.interactable = show;
            }

            if (sheetRect != null)
                sheetRect.anchoredPosition = toY;
            _slideRoutine = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            if (iconCatalog == null)
            {
                iconCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(
                    AchievementIconCatalog.MainCatalogAssetPath);
            }
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, achievementUiFont);
        }
#endif
    }
}
