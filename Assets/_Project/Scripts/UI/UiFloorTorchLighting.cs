using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class UiFloorTorchLighting : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Image targetImage;
        [SerializeField] private Material lightingMaterial;
        [SerializeField] private RectTransform torchLightLeft;
        [SerializeField] private RectTransform torchLightRight;
        [SerializeField] private bool followTorchAnchors = true;
        [SerializeField] private Vector2 manualLightLeftUv = new(0.07f, 0.5f);
        [SerializeField] private Vector2 manualLightRightUv = new(0.93f, 0.5f);

        [Header("Ambient")]
        [SerializeField] [Range(0f, 1f)] private float ambient = 0.2f;
        [SerializeField] [Range(0f, 1f)] private float lockedAmbient = 0f;

        [Header("Torch Light")]
        [SerializeField] private Color lightColor = new(1f, 0.85f, 0.58f, 1f);
        [SerializeField] private float minIntensity = 0.52f;
        [SerializeField] private float maxIntensity = 0.86f;
        [SerializeField] private float minRadius = 0.33f;
        [SerializeField] private float maxRadius = 0.66f;
        [SerializeField] private float softness = 2.1f;
        [SerializeField] private float pulseSpeed = 1.9f;
        [SerializeField] private float lockedMinIntensity = 0f;
        [SerializeField] private float lockedMaxIntensity = 0.4f;

        private bool _isLocked;

        private Material _runtimeMaterial;
        private float _seedA;
        private float _seedB;
        private float _seedC;
        private float _seedD;

        private void Awake()
        {
            EnsureReferences();
            EnsureSeeds();
            EnsureMaterialInstance();
            Apply();
        }

        private void OnEnable()
        {
            EnsureReferences();
            EnsureSeeds();
            EnsureMaterialInstance();
            Apply();
        }

        private void OnDisable()
        {
            ReleaseRuntimeMaterial();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            if (minIntensity > maxIntensity) (minIntensity, maxIntensity) = (maxIntensity, minIntensity);
            if (minRadius > maxRadius) (minRadius, maxRadius) = (maxRadius, minRadius);
            if (lockedMinIntensity > lockedMaxIntensity) (lockedMinIntensity, lockedMaxIntensity) = (lockedMaxIntensity, lockedMinIntensity);
            if (softness < 0.01f) softness = 0.01f;
            if (pulseSpeed < 0f) pulseSpeed = 0f;

            EnsureReferences();
            EnsureMaterialInstance();
            Apply();
        }

        private void Update()
        {
            EnsureMaterialInstance();
            Apply();
        }

        private void EnsureReferences()
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        private void EnsureSeeds()
        {
            if (_seedA != 0f || _seedB != 0f || _seedC != 0f || _seedD != 0f)
                return;

            _seedA = Random.Range(1f, 500f);
            _seedB = Random.Range(500f, 1000f);
            _seedC = Random.Range(1000f, 1500f);
            _seedD = Random.Range(1500f, 2000f);
        }

        private void EnsureMaterialInstance()
        {
            if (targetImage == null || lightingMaterial == null)
                return;

            if (_runtimeMaterial == null || _runtimeMaterial.shader != lightingMaterial.shader)
            {
                ReleaseRuntimeMaterial();
                _runtimeMaterial = new Material(lightingMaterial)
                {
                    name = lightingMaterial.name + " (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
                targetImage.material = _runtimeMaterial;
            }
            else if (targetImage.material != _runtimeMaterial)
            {
                targetImage.material = _runtimeMaterial;
            }
        }

        private void ReleaseRuntimeMaterial()
        {
            if (targetImage != null && lightingMaterial != null)
                targetImage.material = lightingMaterial;

            if (_runtimeMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(_runtimeMaterial);
            else
                DestroyImmediate(_runtimeMaterial);

            _runtimeMaterial = null;
        }

        private void Apply()
        {
            if (targetImage == null || _runtimeMaterial == null)
                return;

            var rt = targetImage.rectTransform;
            var leftUv = followTorchAnchors
                ? ToUv(rt, torchLightLeft, new Vector2(0.07f, 0.5f))
                : Clamp01(manualLightLeftUv);
            var rightUv = followTorchAnchors
                ? ToUv(rt, torchLightRight, new Vector2(0.93f, 0.5f))
                : Clamp01(manualLightRightUv);

            var t = Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
            var noiseLeft = Mathf.PerlinNoise(_seedA, t * pulseSpeed + _seedB);
            var noiseRight = Mathf.PerlinNoise(_seedC, t * pulseSpeed + _seedD);

            var minIntensityValue = _isLocked ? lockedMinIntensity : minIntensity;
            var maxIntensityValue = _isLocked ? lockedMaxIntensity : maxIntensity;
            var ambientValue = _isLocked ? lockedAmbient : ambient;

            var leftIntensity = Mathf.Lerp(minIntensityValue, maxIntensityValue, noiseLeft);
            var rightIntensity = Mathf.Lerp(minIntensityValue, maxIntensityValue, noiseRight);
            var leftRadius = Mathf.Lerp(minRadius, maxRadius, noiseLeft);
            var rightRadius = Mathf.Lerp(minRadius, maxRadius, noiseRight);

            ApplyToMaterial(_runtimeMaterial, ambientValue, leftUv, rightUv, leftRadius, rightRadius, leftIntensity, rightIntensity);

            // Для Maskable=true UI часто рендерит через materialForRendering (копия со стэнсилом/клипом).
            // Прокидываем параметры и туда, чтобы пульсация не "замирала" под маской.
            var renderingMat = targetImage.materialForRendering;
            if (renderingMat != null && renderingMat != _runtimeMaterial)
            {
                ApplyToMaterial(renderingMat, ambientValue, leftUv, rightUv, leftRadius, rightRadius, leftIntensity, rightIntensity);
            }
        }

        private void ApplyToMaterial(
            Material material,
            float ambientValue,
            Vector2 leftUv,
            Vector2 rightUv,
            float leftRadius,
            float rightRadius,
            float leftIntensity,
            float rightIntensity)
        {
            material.SetFloat("_Ambient", ambientValue);
            material.SetColor("_LightColor", lightColor);
            material.SetFloat("_LightSoftness", softness);
            material.SetVector("_Light1", new Vector4(leftUv.x, leftUv.y, leftRadius, leftIntensity));
            material.SetVector("_Light2", new Vector4(rightUv.x, rightUv.y, rightRadius, rightIntensity));
        }

        public void SetLockedState(bool isLocked)
        {
            _isLocked = isLocked;
        }

        private static Vector2 ToUv(RectTransform root, RectTransform light, Vector2 fallbackUv)
        {
            if (root == null || light == null)
                return fallbackUv;

            var rect = root.rect;
            if (Mathf.Abs(rect.width) < 0.001f || Mathf.Abs(rect.height) < 0.001f)
                return fallbackUv;

            var local = root.InverseTransformPoint(light.position);
            var u = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
            var v = Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }

        private static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }
    }
}
