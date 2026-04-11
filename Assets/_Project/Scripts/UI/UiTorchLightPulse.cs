using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class UiTorchLightPulse : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private RectTransform targetRect;

        [Header("Alpha Pulse")]
        [SerializeField] private float minAlpha = 0.30f;
        [SerializeField] private float maxAlpha = 0.48f;
        [SerializeField] private float pulseSpeed = 1.8f;

        [Header("Scale Pulse")]
        [SerializeField] private float minScale = 0.92f;
        [SerializeField] private float maxScale = 1.10f;
        [SerializeField] private float smoothing = 10f;

        private float _seed;
        private float _currentAlpha;
        private float _currentScale = 1f;

        private void Awake()
        {
            EnsureTargets();
            _seed = Random.Range(0f, 1000f);

            if (targetGraphic != null)
                _currentAlpha = targetGraphic.color.a;
            if (targetRect != null)
                _currentScale = targetRect.localScale.x;
        }

        private void Reset()
        {
            EnsureTargets();
        }

        private void OnValidate()
        {
            if (minAlpha > maxAlpha) (minAlpha, maxAlpha) = (maxAlpha, minAlpha);
            if (minScale > maxScale) (minScale, maxScale) = (maxScale, minScale);
            if (pulseSpeed < 0f) pulseSpeed = 0f;
            if (smoothing < 0f) smoothing = 0f;
            EnsureTargets();
        }

        private void Update()
        {
            if (targetGraphic == null || targetRect == null)
                return;

            var t = Time.unscaledTime * pulseSpeed + _seed;
            var wave = Mathf.PerlinNoise(t, _seed * 0.37f);

            var targetAlpha = Mathf.Lerp(minAlpha, maxAlpha, wave);
            var targetScale = Mathf.Lerp(minScale, maxScale, wave);

            var lerp = 1f - Mathf.Exp(-smoothing * Mathf.Max(0f, Time.unscaledDeltaTime));
            _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, lerp);
            _currentScale = Mathf.Lerp(_currentScale, targetScale, lerp);

            var c = targetGraphic.color;
            c.a = _currentAlpha;
            targetGraphic.color = c;

            targetRect.localScale = new Vector3(_currentScale, _currentScale, 1f);
        }

        private void EnsureTargets()
        {
            if (targetGraphic == null)
                targetGraphic = GetComponent<Graphic>();
            if (targetRect == null)
                targetRect = transform as RectTransform;
        }
    }
}
