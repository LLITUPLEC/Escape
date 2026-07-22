using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Неоновая пульсирующая обводка (двойной Outline) для выбранного UI-элемента.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiNeonPulseOutline : MonoBehaviour
    {
        [SerializeField] private Color neonColor = new Color(0.22f, 1f, 0.32f, 1f);
        [SerializeField, Min(0f)] private float minAlpha = 0.42f;
        [SerializeField, Min(0f)] private float maxAlpha = 0.98f;
        [SerializeField, Min(0.05f)] private float pulseSpeed = 1.15f;
        [SerializeField] private Vector2 coreDistance = new Vector2(2.5f, -2.5f);
        [SerializeField] private Vector2 glowDistance = new Vector2(5.5f, -5.5f);
        [SerializeField, Range(0f, 1f)] private float glowAlphaScale = 0.48f;

        private Outline _core;
        private Outline _glow;
        private bool _highlight;

        public void SetHighlight(bool active)
        {
            _highlight = active;
            EnsureBuilt();
            if (_core != null)
                _core.enabled = active;
            if (_glow != null)
                _glow.enabled = active;
            if (active)
                Tick();
        }

        private void OnEnable()
        {
            if (_highlight)
                SetHighlight(true);
        }

        private void OnDisable()
        {
            if (_core != null)
                _core.enabled = false;
            if (_glow != null)
                _glow.enabled = false;
        }

        private void OnValidate()
        {
            if (minAlpha > maxAlpha)
                (minAlpha, maxAlpha) = (maxAlpha, minAlpha);
            if (pulseSpeed < 0.05f)
                pulseSpeed = 0.05f;
        }

        private void Update()
        {
            if (_highlight)
                Tick();
        }

        private void EnsureBuilt()
        {
            if (_core != null && _glow != null)
                return;

            var outlines = GetComponents<Outline>();
            if (outlines.Length >= 1)
                _core = outlines[0];
            if (outlines.Length >= 2)
                _glow = outlines[1];

            if (_core == null)
                _core = gameObject.AddComponent<Outline>();
            if (_glow == null)
                _glow = gameObject.AddComponent<Outline>();

            _core.useGraphicAlpha = true;
            _glow.useGraphicAlpha = true;
            _core.effectDistance = coreDistance;
            _glow.effectDistance = glowDistance;
        }

        private void Tick()
        {
            if (_core == null || _glow == null)
                return;

            var t = Time.unscaledTime * pulseSpeed;
            var k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
            // Snappier neon pulse (smoothstep).
            k = k * k * (3f - 2f * k);

            var a = Mathf.Lerp(minAlpha, maxAlpha, k);
            var core = neonColor;
            core.a = a;
            _core.effectColor = core;
            _core.effectDistance = coreDistance;

            var glow = neonColor;
            glow.a = a * glowAlphaScale;
            _glow.effectColor = glow;
            _glow.effectDistance = glowDistance;
        }
    }
}
