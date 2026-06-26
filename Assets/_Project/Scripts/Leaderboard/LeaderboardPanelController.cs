using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Project.Achievements;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Leaderboard
{
    /// <summary>Панель таблицы лидеров на главном экране: вкладки периода, фильтры, скролл, sticky-строка игрока.</summary>
    public sealed class LeaderboardPanelController : MonoBehaviour
    {
        public const string PanelRootName = "LeaderboardPanelRoot";
        private const string OpenButtonName = "RatingButton";

        [SerializeField] private RectTransform sheetRect;
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;
        [SerializeField] private Toggle tabDay;
        [SerializeField] private Toggle tabWeek;
        [SerializeField] private Toggle tabMonth;
        [SerializeField] private Toggle tabAllTime;
        [SerializeField] private LeaderboardFilterButton typeFilterButton;
        [SerializeField] private LeaderboardFilterButton viewFilterButton;
        [SerializeField] private LeaderboardFilterPickerModal filterPicker;
        [SerializeField] private LeaderboardRewardsBarView rewardsBar;
        [SerializeField] private RectTransform scrollContent;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private LeaderboardRowView podiumGoldPrefab;
        [SerializeField] private LeaderboardRowView podiumSilverPrefab;
        [SerializeField] private LeaderboardRowView podiumBronzePrefab;
        [SerializeField] private LeaderboardRowView standardRowPrefab;
        [SerializeField] private LeaderboardRowView stickyPlayerRow;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_FontAsset uiFont;
        [SerializeField] private float slideDuration = 0.38f;
        [SerializeField] private float hiddenAnchoredY = -1400f;
        [SerializeField, Range(0.12f, 1f)] private float inactiveTabAlpha = 0.48f;

        private Button _openButton;
        private bool _wiredOpenButton;
        private Coroutine _slideRoutine;
        private Vector2 _sheetShownPos;
        private LeaderboardPeriod _currentPeriod = LeaderboardPeriod.Week;
        private LeaderboardType _currentType = LeaderboardType.Tournament;
        private string _currentViewId;
        private bool _refreshInFlight;
        private CancellationTokenSource _refreshCts;
        private readonly List<LeaderboardRowView> _spawnedRows = new List<LeaderboardRowView>();

        private static LeaderboardPanelController s_buttonOwner;

        private void Awake()
        {
            if (!TryClaimSingletonInstance())
                return;

            ResolveRefsBestEffort();
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, uiFont);

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

            WirePeriodTabs();
            WireFilters();
            TryBindOpenButton();
            _currentViewId = LeaderboardFilterCatalog.DefaultView(_currentType).Id;
            MainMenuHudLayering.EnsurePanelSubModalsOnTop(transform);
        }

        private void OnEnable()
        {
            TryBindOpenButton();
        }

        private void OnDestroy()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            if (_openButton != null)
                _openButton.onClick.RemoveListener(TogglePanel);
            if (s_buttonOwner == this)
                s_buttonOwner = null;
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Hide);
            if (dimmerButton != null)
                dimmerButton.onClick.RemoveListener(Hide);
        }

        private bool TryClaimSingletonInstance()
        {
            var all = FindObjectsByType<LeaderboardPanelController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length <= 1)
                return true;

            LeaderboardPanelController keeper = this;
            var bestSiblingIndex = int.MaxValue;
            foreach (var panel in all)
            {
                if (panel == null)
                    continue;
                var idx = panel.transform.GetSiblingIndex();
                if (idx < bestSiblingIndex)
                {
                    bestSiblingIndex = idx;
                    keeper = panel;
                }
            }

            if (keeper != this)
            {
                Destroy(gameObject);
                return false;
            }

            foreach (var panel in all)
            {
                if (panel != null && panel != this)
                    Destroy(panel.gameObject);
            }

            return true;
        }

        private void ResolveRefsBestEffort()
        {
            if (sheetRect == null)
            {
                var tr = transform.Find("LeaderboardSheet");
                if (tr != null) sheetRect = tr as RectTransform;
            }

            if (rootCanvasGroup == null)
                rootCanvasGroup = GetComponent<CanvasGroup>();

            if (closeButton == null)
            {
                var b = transform.Find("LeaderboardSheet/Header/CloseButton");
                if (b != null) closeButton = b.GetComponent<Button>();
            }

            if (dimmerButton == null)
            {
                var d = transform.Find("LeaderboardDimmer");
                if (d != null) dimmerButton = d.GetComponent<Button>();
            }

            if (tabDay == null)
                tabDay = transform.Find("LeaderboardSheet/PeriodTabs/TabDay")?.GetComponent<Toggle>();
            if (tabWeek == null)
                tabWeek = transform.Find("LeaderboardSheet/PeriodTabs/TabWeek")?.GetComponent<Toggle>();
            if (tabMonth == null)
                tabMonth = transform.Find("LeaderboardSheet/PeriodTabs/TabMonth")?.GetComponent<Toggle>();
            if (tabAllTime == null)
                tabAllTime = transform.Find("LeaderboardSheet/PeriodTabs/TabAllTime")?.GetComponent<Toggle>();

            if (typeFilterButton == null)
                typeFilterButton = transform.Find("LeaderboardSheet/FilterBar/TypeFilter")?.GetComponent<LeaderboardFilterButton>();
            if (viewFilterButton == null)
                viewFilterButton = transform.Find("LeaderboardSheet/FilterBar/ViewFilter")?.GetComponent<LeaderboardFilterButton>();
            if (filterPicker == null)
                filterPicker = transform.Find("LeaderboardFilterPicker")?.GetComponent<LeaderboardFilterPickerModal>();
            if (rewardsBar == null)
                rewardsBar = transform.Find("LeaderboardSheet/RewardsBar")?.GetComponent<LeaderboardRewardsBarView>();
            if (scrollContent == null)
                scrollContent = transform.Find("LeaderboardSheet/ListArea/ScrollList/Viewport/Content") as RectTransform;
            if (scrollRect == null)
                scrollRect = transform.Find("LeaderboardSheet/ListArea/ScrollList")?.GetComponent<ScrollRect>();
            if (stickyPlayerRow == null)
                stickyPlayerRow = transform.Find("LeaderboardSheet/StickyPlayerRow")?.GetComponent<LeaderboardRowView>();
            if (statusText == null)
                statusText = transform.Find("LeaderboardSheet/StatusText")?.GetComponent<TMP_Text>();

            if (podiumGoldPrefab == null)
                podiumGoldPrefab = transform.Find("LeaderboardPodiumRowGoldTemplate")?.GetComponent<LeaderboardRowView>();
            if (podiumSilverPrefab == null)
                podiumSilverPrefab = transform.Find("LeaderboardPodiumRowSilverTemplate")?.GetComponent<LeaderboardRowView>();
            if (podiumBronzePrefab == null)
                podiumBronzePrefab = transform.Find("LeaderboardPodiumRowBronzeTemplate")?.GetComponent<LeaderboardRowView>();
            if (standardRowPrefab == null)
                standardRowPrefab = transform.Find("LeaderboardStandardRowTemplate")?.GetComponent<LeaderboardRowView>();
        }

        private void WirePeriodTabs()
        {
            if (tabDay != null)
                tabDay.onValueChanged.AddListener(v => { if (v) SwitchPeriod(LeaderboardPeriod.Day); });
            if (tabWeek != null)
                tabWeek.onValueChanged.AddListener(v => { if (v) SwitchPeriod(LeaderboardPeriod.Week); });
            if (tabMonth != null)
                tabMonth.onValueChanged.AddListener(v => { if (v) SwitchPeriod(LeaderboardPeriod.Month); });
            if (tabAllTime != null)
                tabAllTime.onValueChanged.AddListener(v => { if (v) SwitchPeriod(LeaderboardPeriod.AllTime); });

            if (tabWeek != null)
                tabWeek.SetIsOnWithoutNotify(true);
            RefreshPeriodTabVisuals();
        }

        private void WireFilters()
        {
            if (typeFilterButton != null)
            {
                typeFilterButton.SetHeader("TYPE");
                typeFilterButton.ConfigurePickerHost(OpenFilterPicker);
                typeFilterButton.OnSelectionChanged += OnTypeChanged;
                typeFilterButton.ConfigureStaticOptions(
                    BuildTypeOptions(),
                    LeaderboardTypeIds.ToId(_currentType),
                    LeaderboardTypeLabelById);
            }

            if (viewFilterButton != null)
            {
                viewFilterButton.SetHeader("VIEW");
                viewFilterButton.ConfigurePickerHost(OpenFilterPicker);
                viewFilterButton.OnSelectionChanged += OnViewChanged;
                viewFilterButton.SetTypeContext(_currentType, _currentViewId);
            }
        }

        private static IReadOnlyList<LeaderboardViewOption> BuildTypeOptions() => new[]
        {
            new LeaderboardViewOption(LeaderboardTypeIds.ToId(LeaderboardType.Tournament), LeaderboardFilterCatalog.TypeLabel(LeaderboardType.Tournament)),
            new LeaderboardViewOption(LeaderboardTypeIds.ToId(LeaderboardType.Duel), LeaderboardFilterCatalog.TypeLabel(LeaderboardType.Duel)),
            new LeaderboardViewOption(LeaderboardTypeIds.ToId(LeaderboardType.Mine), LeaderboardFilterCatalog.TypeLabel(LeaderboardType.Mine)),
        };

        private static string LeaderboardTypeLabelById(string id) =>
            LeaderboardFilterCatalog.TypeLabel(LeaderboardTypeIds.FromId(id));

        private void OpenFilterPicker(
            IReadOnlyList<LeaderboardViewOption> options,
            string selectedId,
            Action<string> onPick)
        {
            MainMenuHudLayering.EnsurePanelSubModalsOnTop(transform);
            filterPicker?.Show(options, selectedId, onPick);
        }

        private void OnTypeChanged(string typeId)
        {
            _currentType = LeaderboardTypeIds.FromId(typeId);
            _currentViewId = LeaderboardFilterCatalog.DefaultView(_currentType).Id;
            typeFilterButton?.SetSelection(typeId);
            viewFilterButton?.SetTypeContext(_currentType, _currentViewId);
            _ = RefreshFromServerAsync();
        }

        private void OnViewChanged(string viewId)
        {
            _currentViewId = viewId;
            _ = RefreshFromServerAsync();
        }

        private void SwitchPeriod(LeaderboardPeriod period)
        {
            _currentPeriod = period;
            tabDay?.SetIsOnWithoutNotify(period == LeaderboardPeriod.Day);
            tabWeek?.SetIsOnWithoutNotify(period == LeaderboardPeriod.Week);
            tabMonth?.SetIsOnWithoutNotify(period == LeaderboardPeriod.Month);
            tabAllTime?.SetIsOnWithoutNotify(period == LeaderboardPeriod.AllTime);
            RefreshPeriodTabVisuals();
            _ = RefreshFromServerAsync();
        }

        private void RefreshPeriodTabVisuals()
        {
            ApplyToggleTabMuted(tabDay, _currentPeriod != LeaderboardPeriod.Day);
            ApplyToggleTabMuted(tabWeek, _currentPeriod != LeaderboardPeriod.Week);
            ApplyToggleTabMuted(tabMonth, _currentPeriod != LeaderboardPeriod.Month);
            ApplyToggleTabMuted(tabAllTime, _currentPeriod != LeaderboardPeriod.AllTime);
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

        private void TryBindOpenButton()
        {
            if (_wiredOpenButton)
                return;
            if (s_buttonOwner != null && s_buttonOwner != this)
                return;

            var btnTr = FindDeepChild(transform.root, OpenButtonName);
            if (btnTr == null)
                return;
            _openButton = btnTr.GetComponent<Button>();
            if (_openButton == null)
                return;

            _openButton.onClick.RemoveListener(TogglePanel);
            _openButton.onClick.AddListener(TogglePanel);
            _wiredOpenButton = true;
            s_buttonOwner = this;
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
            filterPicker?.Hide();
            MainMenuHudLayering.BringPanelToFront(transform);
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.blocksRaycasts = true;
                rootCanvasGroup.interactable = true;
            }

            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: true));
            _ = RefreshFromServerAsync();
        }

        private void Hide()
        {
            filterPicker?.Hide();
            _refreshCts?.Cancel();
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

        private async System.Threading.Tasks.Task RefreshFromServerAsync()
        {
            if (_refreshInFlight)
                return;
            _refreshInFlight = true;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            var ct = _refreshCts.Token;

            SetStatus("Загрузка…");

            try
            {
                var response = await LeaderboardService.GetAsync(_currentPeriod, _currentType, _currentViewId, ct);
                if (ct.IsCancellationRequested)
                    return;

                if (response == null || !response.ok)
                {
                    SetStatus(FormatError(response?.err));
                    return;
                }

                ApplyResponse(response);
                SetStatus(string.Empty);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            finally
            {
                _refreshInFlight = false;
            }
        }

        private void ApplyResponse(LeaderboardGetRpcResponse response)
        {
            rewardsBar?.Bind(response.rewards);

            ClearRows();
            if (scrollContent == null)
                return;

            var currentUserId = Project.Nakama.NakamaBootstrap.Instance?.Session?.UserId ?? string.Empty;
            var entries = response.entries ?? Array.Empty<LeaderboardEntryDto>();

            for (var i = 0; i < entries.Length; i++)
            {
                var dto = entries[i];
                if (dto == null)
                    continue;

                var prefab = PickRowPrefab(dto.rank);
                if (prefab == null)
                    continue;

                var row = Instantiate(prefab, scrollContent);
                row.gameObject.SetActive(true);
                var entry = LeaderboardEntry.FromDto(dto, currentUserId);
                row.Bind(entry);
                _spawnedRows.Add(row);
            }

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;

            var selfDto = response.self_entry;
            if (stickyPlayerRow != null)
            {
                if (selfDto != null)
                {
                    stickyPlayerRow.gameObject.SetActive(true);
                    stickyPlayerRow.Bind(LeaderboardEntry.FromDto(selfDto, currentUserId));
                }
                else
                {
                    stickyPlayerRow.gameObject.SetActive(false);
                }
            }

            AchievementsTmpMaterialRepair.RepairHierarchy(scrollContent, uiFont);
            if (stickyPlayerRow != null)
                AchievementsTmpMaterialRepair.RepairHierarchy(stickyPlayerRow.transform, uiFont);
        }

        private LeaderboardRowView PickRowPrefab(int rank) => rank switch
        {
            1 => podiumGoldPrefab,
            2 => podiumSilverPrefab,
            3 => podiumBronzePrefab,
            _ => standardRowPrefab,
        };

        private void ClearRows()
        {
            foreach (var row in _spawnedRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _spawnedRows.Clear();
        }

        private void SetStatus(string message)
        {
            if (statusText == null)
                return;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            statusText.text = message ?? string.Empty;
        }

        private static string FormatError(string err)
        {
            if (string.IsNullOrWhiteSpace(err))
                return "Не удалось загрузить таблицу";
            return err switch
            {
                "nakama_not_ready" => "Нет соединения с сервером",
                "nakama_not_initialized" => "Сервер не инициализирован",
                "unauthorized" => "Требуется авторизация",
                _ => "Ошибка: " + err,
            };
        }
    }
}
