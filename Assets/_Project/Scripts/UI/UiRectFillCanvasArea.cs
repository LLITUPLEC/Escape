using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// Делает RectTransform квадратом: ширина = ширина родителя (Canvas), высота = ширина.
    /// Вертикально центрируется в области ниже topBlock (если задан).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiRectFillCanvasArea : MonoBehaviour
    {
        [SerializeField] private RectTransform topBlock;
        [SerializeField] private float bottomInset;
        [SerializeField] private float topExtraInset;

        private RectTransform _rt;
        private RectTransform _parentRt;
        private DrivenRectTransformTracker _tracker;
        private Vector2 _lastSizeDelta = Vector2.negativeInfinity;
        private Vector2 _lastAnchoredPosition = Vector2.negativeInfinity;
        private float _lastParentWidth = float.NaN;
        private bool _applyScheduled;

        public void Configure(RectTransform top, float bottom = 0f, float topExtra = 0f)
        {
            topBlock = top;
            bottomInset = bottom;
            topExtraInset = topExtra;
            RequestApply();
        }

        private void OnEnable()
        {
            RequestApply();
        }

        private void OnDisable()
        {
            _tracker.Clear();
#if UNITY_EDITOR
            UnscheduleEditorApply();
#endif
            _applyScheduled = false;
        }

        private void LateUpdate()
        {
            var parentRt = transform.parent as RectTransform;
            if (parentRt != null && !Mathf.Approximately(parentRt.rect.width, _lastParentWidth))
                RequestApply();

            if (_applyScheduled)
                Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RequestApply();
        }
#endif

        private void RequestApply()
        {
            _applyScheduled = true;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                ScheduleEditorApply();
#endif
        }

#if UNITY_EDITOR
        private void ScheduleEditorApply()
        {
            UnityEditor.EditorApplication.delayCall -= RunDeferredApply;
            UnityEditor.EditorApplication.delayCall += RunDeferredApply;
        }

        private void UnscheduleEditorApply()
        {
            UnityEditor.EditorApplication.delayCall -= RunDeferredApply;
        }

        private void RunDeferredApply()
        {
            UnscheduleEditorApply();
            if (this == null || !isActiveAndEnabled) return;
            Apply();
        }
#endif

        public void Apply()
        {
            _applyScheduled = false;
            _rt = (RectTransform)transform;
            _parentRt = _rt.parent as RectTransform;
            if (_parentRt == null) return;

            var width = _parentRt.rect.width;
            if (width <= 0f) return;

            var height = width;
            var topInset = topExtraInset;
            if (topBlock != null)
                topInset += MeasureTopBlockInset();

            var sizeDelta = new Vector2(0f, height);
            var anchoredPosition = new Vector2(0f, -(topInset - bottomInset) * 0.5f);

            if (Approximately(sizeDelta, _lastSizeDelta)
                && Approximately(anchoredPosition, _lastAnchoredPosition)
                && Mathf.Approximately(width, _lastParentWidth))
                return;

            _lastSizeDelta = sizeDelta;
            _lastAnchoredPosition = anchoredPosition;
            _lastParentWidth = width;

            _tracker.Clear();
            _tracker.Add(
                this,
                _rt,
                DrivenTransformProperties.Anchors
                | DrivenTransformProperties.AnchoredPosition
                | DrivenTransformProperties.SizeDelta
                | DrivenTransformProperties.Pivot);

            _rt.anchorMin = new Vector2(0f, 0.5f);
            _rt.anchorMax = new Vector2(1f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = sizeDelta;
            _rt.anchoredPosition = anchoredPosition;
        }

        private float MeasureTopBlockInset()
        {
            var parentCorners = new Vector3[4];
            _parentRt.GetWorldCorners(parentCorners);
            var parentTopY = parentCorners[1].y;

            var blockCorners = new Vector3[4];
            topBlock.GetWorldCorners(blockCorners);
            var blockBottomY = blockCorners[0].y;

            return Mathf.Max(0f, parentTopY - blockBottomY);
        }

        private static bool Approximately(Vector2 a, Vector2 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
        }
    }
}
