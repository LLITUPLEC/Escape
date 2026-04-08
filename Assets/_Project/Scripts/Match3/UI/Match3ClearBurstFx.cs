using UnityEngine;

namespace Project.Match3.UI
{
    /// <summary>
    /// Lightweight one-system emitter for line-clear bursts.
    /// Designed for mobile: no allocations during emit, no instantiate per cell.
    /// </summary>
    public sealed class Match3ClearBurstFx : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ParticleSystem ps;

        [Header("Burst")]
        [SerializeField] private int minParticles = 8;
        [SerializeField] private int maxParticles = 12;
        // UI is usually in "pixels" units; small values like 1..3 are nearly invisible.
        [SerializeField] private float minSpeed = 180f;
        [SerializeField] private float maxSpeed = 320f;
        [SerializeField] private float upwardBias = 0.65f; // 0..1 (higher = more upward)
        [SerializeField] private float minLifetime = 0.45f;
        [SerializeField] private float maxLifetime = 0.85f;
        [SerializeField] private float minSize = 12f;
        [SerializeField] private float maxSize = 22f;
        [SerializeField] private float horizontalSpread = 0.55f; // 0..1 (lower = tighter cone)
        [SerializeField] private float maxTravelDistance = 220f; // in UI units (approx pixels); 0 = unlimited

        [Header("Auto-fix legacy tiny settings")]
        [SerializeField] private bool autoScaleLegacyValues = true;
        [SerializeField] private float legacyScaleMultiplier = 120f;

        private ParticleSystem.EmitParams _emit;

        private void Awake()
        {
            if (ps == null) ps = GetComponentInChildren<ParticleSystem>(true);

            // If this component was created with the old "world-like" defaults (e.g. size 0.1, speed 3),
            // auto-scale them to UI-friendly units so the effect is visible without manual tuning.
            if (autoScaleLegacyValues)
            {
                if (maxSize > 0f && maxSize <= 1f)
                {
                    minSize *= legacyScaleMultiplier;
                    maxSize *= legacyScaleMultiplier;
                }
                if (maxSpeed > 0f && maxSpeed <= 20f)
                {
                    minSpeed *= legacyScaleMultiplier;
                    maxSpeed *= legacyScaleMultiplier;
                }
            }

            // Make sure particles are drawn above UI.
            if (ps != null)
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.sortingOrder = 500;
                }
            }
        }

        public void EmitBurst(Vector3 worldPos, Color color)
        {
            if (ps == null) return;

            int count = Random.Range(Mathf.Max(1, minParticles), Mathf.Max(1, maxParticles) + 1);
            _emit = default;
            _emit.position = worldPos;
            // Ensure opaque start alpha; shader handles soft edge.
            _emit.startColor = new Color(color.r, color.g, color.b, 1f);

            for (int i = 0; i < count; i++)
            {
                float speed = Random.Range(minSpeed, maxSpeed);

                // Random direction with upward bias (some sideways, some slightly down).
                float up = Mathf.Lerp(-0.15f, 1f, Mathf.Pow(Random.value, 1.35f * Mathf.Lerp(2.5f, 0.75f, Mathf.Clamp01(upwardBias))));
                Vector2 side = Random.insideUnitCircle.normalized;
                // Keep z=0 so particles stay in the UI plane (avoid flying "toward camera").
                Vector3 dir = new Vector3(side.x * Mathf.Clamp01(horizontalSpread), Mathf.Clamp(up, -0.25f, 1f), 0f).normalized;

                float lifetime = Random.Range(minLifetime, maxLifetime);
                if (maxTravelDistance > 0.001f)
                {
                    // Clamp distance ~= speed * lifetime so it doesn't exceed the desired radius.
                    speed = Mathf.Min(speed, maxTravelDistance / Mathf.Max(0.05f, lifetime));
                }

                _emit.velocity = dir * speed;
                _emit.startLifetime = lifetime;
                _emit.startSize = Random.Range(minSize, maxSize);
                _emit.rotation = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                _emit.angularVelocity = Random.Range(-4f, 4f);

                ps.Emit(_emit, 1);
            }
        }
    }
}

