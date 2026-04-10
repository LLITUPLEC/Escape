using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class TorchFlicker : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Light2D targetLight;

        [Header("Intensity Flicker")]
        [SerializeField] private float minIntensity = 0.85f;
        [SerializeField] private float maxIntensity = 1.25f;
        [SerializeField] private float noiseSpeed = 2.75f;
        [SerializeField] private float smoothing = 12f;

        [Header("Radius Flicker")]
        [SerializeField] private bool affectRadius = true;
        [SerializeField] private float minOuterRadius = 2.2f;
        [SerializeField] private float maxOuterRadius = 2.8f;

        private float _noiseSeed;
        private float _currentIntensity;
        private float _currentOuterRadius;

        private void Awake()
        {
            EnsureTarget();
            _noiseSeed = Random.Range(0f, 1000f);
            if (targetLight != null)
            {
                _currentIntensity = targetLight.intensity;
                _currentOuterRadius = targetLight.pointLightOuterRadius;
            }
        }

        private void Reset()
        {
            EnsureTarget();
        }

        private void OnValidate()
        {
            if (minIntensity > maxIntensity) (minIntensity, maxIntensity) = (maxIntensity, minIntensity);
            if (minOuterRadius > maxOuterRadius) (minOuterRadius, maxOuterRadius) = (maxOuterRadius, minOuterRadius);
            if (noiseSpeed < 0f) noiseSpeed = 0f;
            if (smoothing < 0f) smoothing = 0f;
            EnsureTarget();
        }

        private void Update()
        {
            if (targetLight == null)
                return;

            var t = Time.time * noiseSpeed + _noiseSeed;
            var noise01 = Mathf.PerlinNoise(t, _noiseSeed * 0.37f);

            var targetIntensity = Mathf.Lerp(minIntensity, maxIntensity, noise01);
            _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            targetLight.intensity = _currentIntensity;

            if (!affectRadius)
                return;

            var targetRadius = Mathf.Lerp(minOuterRadius, maxOuterRadius, noise01);
            _currentOuterRadius = Mathf.Lerp(_currentOuterRadius, targetRadius, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            targetLight.pointLightOuterRadius = _currentOuterRadius;
        }

        private void EnsureTarget()
        {
            if (targetLight == null)
                targetLight = GetComponent<Light2D>();
        }
    }
}

