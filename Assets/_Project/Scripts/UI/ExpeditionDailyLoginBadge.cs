using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Small live login-reward dot. Reads the shared daily snapshot; never polls or claims a reward.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionDailyLoginBadge : MaskableGraphic
    {
        private float _nextRefresh;
        public bool IsVisible { get; private set; }
        public static ExpeditionDailyLoginBadge Bind(Button button)
        {
            var rect = ExpeditionUi.Rect(button.transform, "ExpeditionDailyLoginBadge", 1, 1, 1, 1);
            rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-10, -8); rect.sizeDelta = new Vector2(24, 24);
            var badge = rect.gameObject.AddComponent<ExpeditionDailyLoginBadge>();
            badge.color = new Color32(244, 69, 48, 255); badge.raycastTarget = false; badge.Refresh();
            return badge;
        }
        protected override void OnEnable() { base.OnEnable(); DailyRewardsService.Changed += Refresh; Refresh(); }
        protected override void OnDisable() { DailyRewardsService.Changed -= Refresh; base.OnDisable(); }
        private void Update()
        {
            if (!Application.isPlaying || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .5f; Refresh();
        }
        public void Refresh()
        {
            var state = DailyRewardsService.Current;
            var visible = DailyRewardsService.Valid(state) && DailyRewardsService.EstimatedServerTime < state.reset_at && state.login.can_claim;
            if (visible == IsVisible) return;
            IsVisible = visible; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (!IsVisible) return;
            var rect = rectTransform.rect; var center = rect.center; var radius = Mathf.Min(rect.width, rect.height) * .5f;
            mesh.AddVert(center, color, Vector2.zero);
            const int sides = 20;
            for (var i = 0; i <= sides; i++)
            {
                var angle = i * Mathf.PI * 2 / sides;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
