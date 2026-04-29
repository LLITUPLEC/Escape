using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

        internal TMP_FontAsset AchievementUiFontReference => achievementUiFont;

        private void Awake()
        {
            AchievementProgressStorage.EnsureLoaded();
            ResolveRefsBestEffort();
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, achievementUiFont);
            if (sheetRect != null)
            {
                _sheetShownPos = sheetRect.anchoredPosition;
                sheetRect.anchoredPosition = new Vector2(_sheetShownPos.x, hiddenAnchoredY);
            }

            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 0f;
                rootCanvasGroup.blocksRaycasts = false;
                rootCanvasGroup.interactable = false;
            }

            gameObject.SetActive(true);

            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);
            if (dimmerButton != null)
                dimmerButton.onClick.AddListener(Hide);

            WireTabs();
            TryBindOpenButton();
            _stepDetailModal = AchievementStepDetailModal.Ensure(transform, achievementUiFont);
            SwitchTab(AchievementTab.Obsession);
        }

        private void OnEnable()
        {
            TryBindOpenButton();
            AchievementLifecycle.OnDataChanged += OnAchievementLifecycleChanged;
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
            RefreshGrid(_currentTab);
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

            foreach (var row in _spawnedRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _spawnedRows.Clear();

            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain.Tab != tab)
                    continue;
                var inst = Instantiate(chainRowPrefab, host);
                inst.gameObject.SetActive(true);
                inst.Bind(chain, OpenStepDetail);
                _spawnedRows.Add(inst);
            }

            AchievementsTmpMaterialRepair.RepairHierarchy(host, achievementUiFont);
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

            _stepDetailModal.Show(def.Descriptions[stepIndex], reward, canClaim, claimed, onClaim);
        }

        private async void ClaimStepRoutineAsync(string chainId, int stepIndex)
        {
            await AchievementRewardClaim.TryClaimStepAsync(chainId, stepIndex, CancellationToken.None);
            RefreshGrid(_currentTab);
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
            var visible = rootCanvasGroup != null && rootCanvasGroup.alpha > 0.01f;
            if (visible)
                Hide();
            else
                Show();
        }

        private void Show()
        {
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.blocksRaycasts = true;
                rootCanvasGroup.interactable = true;
            }

            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: true));
        }

        private void Hide()
        {
            _stepDetailModal?.Hide();
            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: false));
        }

        private IEnumerator AnimateSlide(bool show)
        {
            float fromA = rootCanvasGroup != null ? rootCanvasGroup.alpha : 0f;
            float toA = show ? 1f : 0f;
            Vector2 fromY = sheetRect != null ? sheetRect.anchoredPosition : Vector2.zero;
            Vector2 toY = show ? _sheetShownPos : new Vector2(_sheetShownPos.x, hiddenAnchoredY);

            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / slideDuration);
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
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, achievementUiFont);
        }
#endif
    }
}
