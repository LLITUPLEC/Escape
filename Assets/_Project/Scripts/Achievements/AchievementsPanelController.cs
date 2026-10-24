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
    /// <summary>Панель достижений на главном экране: вкладки, скролл, выезд снизу.</summary>
    /// <remarks>
    /// Корень <see cref="PanelRootName"/>: без LayoutGroup — дочерние RectTransform’ы (Dimmer, Sheet, …) задаются якорями вручную.
    /// Внутри <c>AchievementChainRowPrefabTemplate</c> горизонтальный ряд слотов/стрелок позиционируется вручную; вертикальный стек строк в скролле остаётся через VerticalLayoutGroup на Content + ContentSizeFitter (высота строки — LayoutElement на корне строки).
    /// </remarks>
    public sealed class AchievementsPanelController : MonoBehaviour
    {
        public const string PanelRootName = "AchievementsPanelRoot";
        private const string OpenButtonName = "BottomButtonAchiev";

        [SerializeField] private RectTransform sheetRect;
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;
        [SerializeField] private Toggle tabObsession;
        [SerializeField] private Toggle tabSlaughter;
        [SerializeField] private Toggle tabDnn;

        [SerializeField] private RectTransform contentObsession;
        [SerializeField] private RectTransform contentSlaughter;
        [SerializeField] private RectTransform contentDnn;

        [SerializeField] private AchievementChainRowView chainRowPrefab;

        [Header("Иконки достижений (одна на chain id)")]
        [SerializeField] private AchievementIconCatalog iconCatalog;

        [Header("Шрифт UI (TF2CSecondary: Assets/_Project/Fonts)")]
        [SerializeField] private TMP_FontAsset achievementUiFont;

        [SerializeField] private float slideDuration = 0.38f;
        [SerializeField] private float hiddenAnchoredY = -1400f;

        [Header("TabBar")]
        [SerializeField, Range(0.12f, 1f)] private float inactiveTabAlpha = 0.48f;

        private Button _openButton;
        private readonly List<AchievementChainRowView> _spawnedRows = new List<AchievementChainRowView>();
        private Coroutine _slideRoutine;
        private Vector2 _sheetShownPos;
        private AchievementTab _currentTab = AchievementTab.Obsession;
        private bool _wiredOpenButton;
        private AchievementStepDetailModal _stepDetailModal;
        private ExpeditionAchievementsLayout _expeditionLayout;
        private bool _visible;
        private bool _shellAttached;
        public bool IsVisible => _visible;
        public event Action<bool> VisibilityChanged;

        private RectTransform _pendingBadgeRt;
        private CanvasGroup _pendingBadgeCanvasGroup;
        private TMP_Text _pendingBadgeText;
        private AchievementChainDefinition[] _renderedCatalog;
        private AchievementTab _renderedTab;
        private bool _rowsBuilt;
        private BottomButtonAchievementBadgeConfig _openButtonBadgeConfig;

        private TabRewardBadgeRefs _tabBadgeObsession;
        private TabRewardBadgeRefs _tabBadgeSlaughter;
        private TabRewardBadgeRefs _tabBadgeDnn;

        private struct TabRewardBadgeRefs
        {
            public GameObject Root;
            public TMP_Text CountLabel;
        }

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
            _expeditionLayout.Build(sheetRect, tabObsession, tabSlaughter, tabDnn,
                contentObsession, contentSlaughter, contentDnn, closeButton, achievementUiFont);
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

            WireTabs();
            TryBindOpenButton();
            EnsurePendingClaimBadge();
            _stepDetailModal = AchievementStepDetailModal.Ensure(transform, achievementUiFont);
            SwitchTab(AchievementTab.Obsession);
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
        }

        private void OnDestroy()
        {
            if (_openButton != null)
                _openButton.onClick.RemoveListener(TogglePanel);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Hide);
            if (dimmerButton != null)
                dimmerButton.onClick.RemoveListener(Hide);
        }

        private void OnAchievementLifecycleChanged()
        {
            if (_visible) RefreshGrid(_currentTab);
            RefreshClaimBadges();
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

            if (tabObsession == null)
            {
                var t = transform.Find("AchievementsSheet/TabBar/TabObsession");
                if (t != null) tabObsession = t.GetComponent<Toggle>();
            }

            if (tabSlaughter == null)
            {
                var t = transform.Find("AchievementsSheet/TabBar/TabSlaughter");
                if (t != null) tabSlaughter = t.GetComponent<Toggle>();
            }

            if (tabDnn == null)
            {
                var t = transform.Find("AchievementsSheet/TabBar/TabDnn");
                if (t != null) tabDnn = t.GetComponent<Toggle>();
            }

            if (contentObsession == null)
            {
                var c = transform.Find("AchievementsSheet/Pages/ScrollObsession/Viewport/Content");
                if (c != null) contentObsession = c as RectTransform;
            }

            if (contentSlaughter == null)
            {
                var c = transform.Find("AchievementsSheet/Pages/ScrollSlaughter/Viewport/Content");
                if (c != null) contentSlaughter = c as RectTransform;
            }

            if (contentDnn == null)
            {
                var c = transform.Find("AchievementsSheet/Pages/ScrollDnn/Viewport/Content");
                if (c != null) contentDnn = c as RectTransform;
            }

            if (chainRowPrefab == null)
            {
                var rowHost = transform.Find("AchievementChainRowPrefabTemplate");
                if (rowHost != null)
                    chainRowPrefab = rowHost.GetComponent<AchievementChainRowView>();
            }

            ResolveTabRewardBadge(tabObsession, ref _tabBadgeObsession);
            ResolveTabRewardBadge(tabSlaughter, ref _tabBadgeSlaughter);
            ResolveTabRewardBadge(tabDnn, ref _tabBadgeDnn);
        }

        private void WireTabs()
        {
            if (tabObsession != null)
                tabObsession.onValueChanged.AddListener(v => { if (v) SwitchTab(AchievementTab.Obsession); });
            if (tabSlaughter != null)
                tabSlaughter.onValueChanged.AddListener(v => { if (v) SwitchTab(AchievementTab.Slaughter); });
            if (tabDnn != null)
                tabDnn.onValueChanged.AddListener(v => { if (v) SwitchTab(AchievementTab.Dnn); });

            if (tabObsession != null) tabObsession.SetIsOnWithoutNotify(true);
        }

        private void SwitchTab(AchievementTab tab)
        {
            _currentTab = tab;
            if (tabObsession != null) tabObsession.SetIsOnWithoutNotify(tab == AchievementTab.Obsession);
            if (tabSlaughter != null) tabSlaughter.SetIsOnWithoutNotify(tab == AchievementTab.Slaughter);
            if (tabDnn != null) tabDnn.SetIsOnWithoutNotify(tab == AchievementTab.Dnn);

            var scrollObs = transform.Find("AchievementsSheet/Pages/ScrollObsession")?.gameObject;
            var scrollSl = transform.Find("AchievementsSheet/Pages/ScrollSlaughter")?.gameObject;
            var scrollDn = transform.Find("AchievementsSheet/Pages/ScrollDnn")?.gameObject;
            if (scrollObs != null) scrollObs.SetActive(tab == AchievementTab.Obsession);
            if (scrollSl != null) scrollSl.SetActive(tab == AchievementTab.Slaughter);
            if (scrollDn != null) scrollDn.SetActive(tab == AchievementTab.Dnn);
            RefreshTabVisuals(tab);
            RefreshGrid(tab);
        }

        /// <summary>Неактивные вкладки приглушаем альфой; активная — полная яркость.</summary>
        private void RefreshTabVisuals(AchievementTab tab)
        {
            ApplyToggleTabMuted(tabObsession, tab != AchievementTab.Obsession);
            ApplyToggleTabMuted(tabSlaughter, tab != AchievementTab.Slaughter);
            ApplyToggleTabMuted(tabDnn, tab != AchievementTab.Dnn);
            _expeditionLayout?.SetActiveTab(tab);
        }

        private void ApplyToggleTabMuted(Toggle t, bool muted)
        {
            if (t == null)
                return;
            var cg = t.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = t.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = muted ? inactiveTabAlpha : 1f;
            cg.blocksRaycasts = true;
            cg.interactable = true;
        }

        private void RefreshGrid(AchievementTab tab)
        {
            RectTransform host =
                tab == AchievementTab.Obsession ? contentObsession :
                tab == AchievementTab.Slaughter ? contentSlaughter : contentDnn;
            if (host == null || chainRowPrefab == null)
                return;

            var catalog = AchievementCatalog.Chains;
            var rebuild = !_rowsBuilt || _renderedTab != tab || !ReferenceEquals(_renderedCatalog, catalog);
            if (rebuild)
            {
                foreach (var row in _spawnedRows)
                    if (row != null) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
                _spawnedRows.Clear();
                foreach (var chain in catalog)
                {
                    if (chain == null || chain.Tab != tab) continue;
                    var inst = Instantiate(chainRowPrefab, host);
                    inst.SetExpeditionPresentation();
                    inst.gameObject.SetActive(true);
                    _spawnedRows.Add(inst);
                }
                _renderedCatalog = catalog;
                _renderedTab = tab;
                _rowsBuilt = true;
            }
            var rowIndex = 0;
            foreach (var chain in catalog)
            {
                if (chain == null || chain.Tab != tab) continue;
                var inst = _spawnedRows[rowIndex++];
                var icon = iconCatalog != null ? iconCatalog.GetDisplayIcon(chain.ChainId) : null;
                inst.Bind(chain, OpenStepDetail, icon);
                _expeditionLayout?.StyleRow(inst, chain.TitleRu, chain, icon);
            }
            if (rebuild) AchievementsTmpMaterialRepair.RepairHierarchy(host, achievementUiFont);
        }

        private void OpenStepDetail(string chainId, int stepIndex)
        {
            var def = AchievementCatalog.FindChain(chainId);
            if (def == null || def.Descriptions == null || stepIndex < 0 || stepIndex >= def.Descriptions.Length)
                return;
            if (_stepDetailModal == null)
                _stepDetailModal = AchievementStepDetailModal.Ensure(transform, achievementUiFont);
            var reward = def.RewardTexts != null && stepIndex < def.RewardTexts.Length ? def.RewardTexts[stepIndex] : string.Empty;
            var claimed = AchievementProgressStorage.IsStepClaimed(def.ChainId, stepIndex);
            var canClaim = AchievementRewardClaim.CanClaimStep(def, stepIndex);

            Action onClaim = null;
            if (canClaim)
            {
                onClaim = () => ClaimStepRoutineAsync(def.ChainId, stepIndex);
            }

            var total = AchievementProgressStorage.GetStat(def.StatKey);
            var requirement = def.Descriptions[stepIndex] + "\n\nСтупень " + (stepIndex + 1) + " из " + def.Thresholds.Length
                + " · " + AchievementUiRules.StepProgress(def, stepIndex, total) + "/" + AchievementUiRules.StepTarget(def, stepIndex);
            if (!claimed && !canClaim && total >= AchievementUiRules.CumulativeRequired(def, stepIndex))
                requirement += "\nСначала получите награды предыдущих ступеней.";
            _stepDetailModal.Show(def.TitleRu, requirement, reward, canClaim, claimed, onClaim);
        }

        private async void ClaimStepRoutineAsync(string chainId, int stepIndex)
        {
            await AchievementRewardClaim.TryClaimStepAsync(chainId, stepIndex, CancellationToken.None);
            if (this == null) return;
            RefreshGrid(_currentTab);
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

        private static void ResolveTabRewardBadge(Toggle tab, ref TabRewardBadgeRefs refs)
        {
            if (tab == null)
                return;
            var rootTr = tab.transform.Find("has_revard");
            if (rootTr == null)
                return;
            refs.Root = rootTr.gameObject;
            var countTr = rootTr.Find("count_rev");
            refs.CountLabel = countTr != null ? countTr.GetComponent<TMP_Text>() : null;
        }

        private void RefreshClaimBadges()
        {
            RefreshPendingClaimBadge();
            RefreshTabRewardBadges();
        }

        private void RefreshPendingClaimBadge()
        {
            EnsurePendingClaimBadge();
            ApplyClaimCountBadge(
                AchievementUiPending.CountEligibleClaimSteps(),
                null,
                _pendingBadgeText,
                _pendingBadgeCanvasGroup);
        }

        private void RefreshTabRewardBadges()
        {
            ResolveTabRewardBadge(tabObsession, ref _tabBadgeObsession);
            ResolveTabRewardBadge(tabSlaughter, ref _tabBadgeSlaughter);
            ResolveTabRewardBadge(tabDnn, ref _tabBadgeDnn);

            ApplyTabRewardBadge(AchievementTab.Obsession, _tabBadgeObsession);
            ApplyTabRewardBadge(AchievementTab.Slaughter, _tabBadgeSlaughter);
            ApplyTabRewardBadge(AchievementTab.Dnn, _tabBadgeDnn);
        }

        private static void ApplyTabRewardBadge(AchievementTab tab, TabRewardBadgeRefs refs)
        {
            var n = AchievementUiPending.CountEligibleClaimSteps(tab);
            ApplyClaimCountBadge(n, refs.Root, refs.CountLabel, null);
        }

        private static void ApplyClaimCountBadge(
            int count,
            GameObject activeRoot,
            TMP_Text countLabel,
            CanvasGroup alphaGroup)
        {
            var label = count > 99 ? "99+" : count.ToString();
            if (countLabel != null)
                countLabel.text = label;
            if (activeRoot != null)
                activeRoot.SetActive(count > 0);
            if (alphaGroup != null)
                alphaGroup.alpha = count > 0 ? 1f : 0f;
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
                if (host != null) AttachToShell(host.transform as RectTransform);
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
        public void AttachToShell(RectTransform pageHost)
        {
            if (pageHost == null) return;
            _shellAttached = true;
            transform.SetParent(pageHost, false);
            ExpeditionUi.SetRect(transform as RectTransform, 0, 0, 1, 1);
            if (_expeditionLayout != null) _expeditionLayout.SetShellLayout(true);
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
