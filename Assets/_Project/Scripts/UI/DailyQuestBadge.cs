using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Counts ready quest rewards and the all-three bonus. Owns no navigation or claim callbacks.</summary>
    [DisallowMultipleComponent]
    public sealed class DailyQuestBadge : MonoBehaviour
    {
        private CanvasGroup _group;
        private TMP_Text _count;
        private CancellationTokenSource _cts;
        private string _scope;
        private bool _refreshing, _expiredRequested;
        private int _generation;
        private float _nextRefresh, _nextTick;
        public int PendingCount { get; private set; }
        public static DailyQuestBadge Bind(Button button)
        {
            if (button == null) return null;
            var badge = ExpeditionUi.Ensure<DailyQuestBadge>(button.gameObject); badge.Refresh(); return badge;
        }
        private void OnEnable()
        {
            _generation++; _refreshing = false; _cts = new CancellationTokenSource();
            DailyRewardsService.Changed += Refresh;
            _scope = LocalAccountCacheScope.Current; _nextRefresh = 0; _expiredRequested = false; Refresh();
        }
        private void OnDisable()
        {
            DailyRewardsService.Changed -= Refresh;
            _generation++; _cts?.Cancel(); _cts?.Dispose(); _cts = null; _refreshing = false;
        }
        private void OnApplicationFocus(bool focused) { if (focused) _nextRefresh = 0; }
        private void Update()
        {
            if (!Application.isPlaying || Time.realtimeSinceStartup < _nextTick) return;
            _nextTick = Time.realtimeSinceStartup + .5f;
            var scope = LocalAccountCacheScope.Current;
            if (_scope != scope) { _scope = scope; _nextRefresh = 0; _expiredRequested = false; }
            var state = DailyRewardsService.Current;
            if (state != null && DailyRewardsService.EstimatedServerTime >= state.reset_at)
            { if (!_expiredRequested) { _expiredRequested = true; _nextRefresh = 0; } }
            else _expiredRequested = false;
            Refresh();
            if (!_refreshing && !DailyRewardsService.Busy && scope != null && Time.realtimeSinceStartup >= _nextRefresh)
            { _refreshing = true; _nextRefresh = Time.realtimeSinceStartup + 45; _ = Fetch(_generation, _cts.Token); }
        }
        private async Task Fetch(int generation, CancellationToken token)
        {
            try { await DailyRewardsService.RefreshAsync(token); }
            catch (OperationCanceledException) { }
            finally { if (this != null && generation == _generation) { _refreshing = false; Refresh(); } }
        }
        public void Refresh() => Present(DailyRewardsService.ReadyRewardCount);
        private void Present(int count)
        {
            if (_group == null) Build();
            PendingCount = Math.Max(0, count); _count.text = PendingCount > 99 ? "99+" : PendingCount.ToString(); _group.alpha = PendingCount > 0 ? 1 : 0;
            _group.transform.SetAsLastSibling();
        }
        private void Build()
        {
            var root = ExpeditionUi.Rect(transform, "ExpeditionDailyQuestBadge", 1, 1, 1, 1);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one; root.anchoredPosition = new Vector2(-10, -8); root.sizeDelta = new Vector2(52, 48); root.SetAsLastSibling();
            _group = root.gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = _group.interactable = false;
            var frame = root.gameObject.AddComponent<Image>(); frame.sprite = ExpeditionArt.Frame(true) ?? ExpeditionTheme.Plate;
            frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 4; frame.color = Color.white; frame.raycastTarget = false;
            _count = ExpeditionUi.Text(root, "Count", "", 30, .04f, .03f, .96f, .97f, TextAlignmentOptions.Center);
            _count.color = new Color32(35, 28, 18, 255); _count.margin = Vector4.zero; _count.enableAutoSizing = true; _count.fontSizeMin = 24; _count.fontSizeMax = 30;
        }
    }
}
