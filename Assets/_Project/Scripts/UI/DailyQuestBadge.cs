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
        private ExpeditionGoldCountBadge _presentation;
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
            PendingCount = Math.Max(0, count); _presentation.Present(PendingCount);
            _group.transform.SetAsLastSibling();
        }
        private void Build()
        {
            _presentation = ExpeditionGoldCountBadge.Build(transform, "ExpeditionDailyQuestBadge", "Count", new Vector2(-10, -8));
            _group = _presentation.Group; _count = _presentation.Label;
        }
    }
}
