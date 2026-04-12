using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class UiViewportSpriteCuller : MonoBehaviour
    {
        [SerializeField] private RectTransform viewport;
        [SerializeField] private bool includeInactiveChildren = true;
        [SerializeField] private bool requireFullContainment = true;
        [SerializeField] private float viewportInsetPixels = 2f;

        private readonly List<SpriteRenderer> _targets = new();
        private readonly List<bool> _initialStates = new();

        private void Awake()
        {
            CacheTargets();
            ResolveViewportIfNeeded();
        }

        private void OnEnable()
        {
            CacheTargets();
            ResolveViewportIfNeeded();
        }

        private void LateUpdate()
        {
            if (!ResolveViewportIfNeeded())
                return;

            var cam = ResolveCameraForViewport(viewport);
            var viewportRect = GetViewportScreenRect(viewport, cam);
            viewportRect = InsetRect(viewportRect, Mathf.Max(0f, viewportInsetPixels));
            if (viewportRect.width <= 0.1f || viewportRect.height <= 0.1f)
                return;

            for (var i = 0; i < _targets.Count; i++)
            {
                var sr = _targets[i];
                if (sr == null)
                    continue;

                if (!_initialStates[i])
                    continue;

                var spriteRect = GetBoundsScreenRect(sr.bounds, cam);
                sr.enabled = requireFullContainment
                    ? ContainsRect(viewportRect, spriteRect)
                    : spriteRect.Overlaps(viewportRect, true);
            }
        }

        private void OnDisable()
        {
            for (var i = 0; i < _targets.Count; i++)
            {
                var sr = _targets[i];
                if (sr == null)
                    continue;
                sr.enabled = _initialStates[i];
            }
        }

        public void SetViewport(RectTransform viewportRect)
        {
            viewport = viewportRect;
        }

        private void CacheTargets()
        {
            _targets.Clear();
            _initialStates.Clear();

            var renderers = GetComponentsInChildren<SpriteRenderer>(includeInactiveChildren);
            for (var i = 0; i < renderers.Length; i++)
            {
                var sr = renderers[i];
                if (sr == null)
                    continue;

                _targets.Add(sr);
                _initialStates.Add(sr.enabled);
            }
        }

        private bool ResolveViewportIfNeeded()
        {
            if (viewport != null)
                return true;

            var scroll = FindFirstObjectByType<ScrollRect>(FindObjectsInactive.Include);
            viewport = scroll != null ? scroll.viewport : null;
            return viewport != null;
        }

        private static Camera ResolveCameraForViewport(RectTransform viewportRect)
        {
            if (viewportRect == null)
                return null;

            var canvas = viewportRect.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;

            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private static Rect GetViewportScreenRect(RectTransform rectTransform, Camera cam)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            var max = min;
            for (var i = 1; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect GetBoundsScreenRect(Bounds bounds, Camera cam)
        {
            var c = bounds.center;
            var e = bounds.extents;
            var worldCorners = new[]
            {
                new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
            };

            var min = RectTransformUtility.WorldToScreenPoint(cam, worldCorners[0]);
            var max = min;
            for (var i = 1; i < worldCorners.Length; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, worldCorners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool ContainsRect(Rect container, Rect target)
        {
            return target.xMin >= container.xMin &&
                   target.yMin >= container.yMin &&
                   target.xMax <= container.xMax &&
                   target.yMax <= container.yMax;
        }

        private static Rect InsetRect(Rect rect, float inset)
        {
            if (inset <= 0f)
                return rect;

            return Rect.MinMaxRect(
                rect.xMin + inset,
                rect.yMin + inset,
                rect.xMax - inset,
                rect.yMax - inset);
        }
    }
}
