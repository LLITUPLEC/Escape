using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Мягкий «пар + свечение» за UI-кнопкой (Screen Space Overlay).
    /// Рисуется sibling-ом ниже кнопки, чтобы не перекрывать клики и иконку.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSteamGlowFx : MonoBehaviour
    {
        public const string FxRootName = "RatingButtonSteam";

        [SerializeField] private RectTransform targetButton;
        [Tooltip("Опционально. Если пусто — используется мягкий процедурный спрайт (рекомендуется).")]
        [SerializeField] private Sprite softSprite;
        [SerializeField] private Color glowColor = new Color(1f, 0.78f, 0.35f, 0.28f);
        [SerializeField] private Color steamColor = new Color(1f, 0.92f, 0.7f, 0.22f);
        [SerializeField, Min(2)] private int puffCount = 10;
        [SerializeField, Min(0f)] private float padding = 36f;
        [SerializeField, Min(0.1f)] private float glowPulseSpeed = 0.85f;
        [SerializeField, Min(8f)] private float puffSpeed = 22f;
        [SerializeField, Min(20f)] private float puffTravel = 48f;

        [Header("Layout")]
        [Tooltip("Если включено — FX следует за кнопкой (+ offset). Если выключено — оставляем ручную позицию/размер RatingButtonSteam.")]
        [SerializeField] private bool followButton = true;
        [SerializeField] private Vector2 anchoredPositionOffset;
        [SerializeField] private Vector2 sizeDeltaBonus;

        private RectTransform _fxRoot;
        private Image _glow;
        private Puff[] _puffs;
        private bool _ownsFxRoot;
        private static Sprite _runtimeSoftSprite;

        private struct Puff
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Dir;
            public float Phase;
            public float Period;
            public float Size;
            public float SideBias;
        }

        public static UiSteamGlowFx EnsureOnButton(RectTransform button)
        {
            if (button == null) return null;
            var fx = button.GetComponent<UiSteamGlowFx>();
            if (fx == null) fx = button.gameObject.AddComponent<UiSteamGlowFx>();
            fx.targetButton = button;
            // Не подставляем жёсткие UI-спрайты — только мягкий процедурный.
            fx.softSprite = null;
            fx.EnsureBuilt();
            return fx;
        }

        private void Awake()
        {
            if (targetButton == null)
                targetButton = transform as RectTransform;
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            if (_fxRoot != null) _fxRoot.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_fxRoot != null) _fxRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // Удаляем только FX, созданный этим инстансом в рантайме — сценовый RatingButtonSteam не трогаем.
            if (!_ownsFxRoot || _fxRoot == null) return;
            if (Application.isPlaying) Destroy(_fxRoot.gameObject);
            else DestroyImmediate(_fxRoot.gameObject);
            _fxRoot = null;
        }

        private void LateUpdate()
        {
            if (targetButton == null || _fxRoot == null) return;
            if (followButton)
                SyncTransformFollow();
            else
                EnsureSiblingOrderOnly();
            TickVisuals();
        }

        public void EnsureBuilt()
        {
            if (targetButton == null)
                targetButton = transform as RectTransform;
            if (targetButton == null) return;

            var sprite = ResolveSoftSprite();

            if (_fxRoot == null)
            {
                var existing = targetButton.parent != null
                    ? targetButton.parent.Find(FxRootName) as RectTransform
                    : null;
                if (existing != null)
                {
                    _fxRoot = existing;
                    _ownsFxRoot = false;
                }
            }

            if (_fxRoot == null)
            {
                var go = new GameObject(FxRootName, typeof(RectTransform));
                _fxRoot = go.GetComponent<RectTransform>();
                _fxRoot.SetParent(targetButton.parent, false);
                _ownsFxRoot = true;
                if (followButton)
                    SyncTransformFollow();
            }

            if (_glow == null)
            {
                var glowTr = _fxRoot.Find("Glow") as RectTransform;
                if (glowTr == null)
                {
                    var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    glowTr = glowGo.GetComponent<RectTransform>();
                    glowTr.SetParent(_fxRoot, false);
                    Stretch(glowTr);
                }

                _glow = glowTr.GetComponent<Image>();
                _glow.type = Image.Type.Simple;
                _glow.preserveAspect = false;
                _glow.raycastTarget = false;
            }

            _glow.sprite = sprite;
            _glow.color = glowColor;

            if (_puffs == null || _puffs.Length != puffCount || !PuffsStillValid())
                RebuildPuffs(sprite);
            else
                ApplySpriteToPuffs(sprite);

            EnsureSiblingOrderOnly();
            if (followButton)
                SyncTransformFollow();
        }

        private Sprite ResolveSoftSprite()
        {
            // Явно заданный мягкий спрайт ок; жёсткий ui_circle_soft даёт «кружки» — игнорируем по имени.
            if (softSprite != null && !IsHardCircleSprite(softSprite))
                return softSprite;
            return GetOrCreateSoftSprite();
        }

        private static bool IsHardCircleSprite(Sprite sprite)
        {
            if (sprite == null) return false;
            var n = sprite.name ?? "";
            return n.IndexOf("ui_circle_soft", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("knob", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool PuffsStillValid()
        {
            if (_puffs == null) return false;
            for (var i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].Rt == null || _puffs[i].Img == null)
                    return false;
            }
            return true;
        }

        private void ApplySpriteToPuffs(Sprite sprite)
        {
            if (_puffs == null) return;
            for (var i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].Img != null)
                    _puffs[i].Img.sprite = sprite;
            }
        }

        private void RebuildPuffs(Sprite sprite)
        {
            for (var i = _fxRoot.childCount - 1; i >= 0; i--)
            {
                var child = _fxRoot.GetChild(i);
                if (child != null && child.name.StartsWith("Puff"))
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            _puffs = new Puff[puffCount];
            for (var i = 0; i < puffCount; i++)
            {
                var go = new GameObject("Puff" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_fxRoot, false);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.color = steamColor;

                var angle = (i / (float)puffCount) * Mathf.PI * 2f + 0.35f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (dir.x > 0f) dir.x *= 0.55f;
                dir.Normalize();

                var size = Mathf.Lerp(42f, 78f, (i % 5) / 4f);
                rt.sizeDelta = new Vector2(size, size);

                _puffs[i] = new Puff
                {
                    Rt = rt,
                    Img = img,
                    Dir = dir,
                    Phase = Random.value,
                    Period = Mathf.Lerp(1.6f, 2.8f, (i % 7) / 6f),
                    Size = size,
                    SideBias = (i % 2 == 0) ? 1f : -1f,
                };
            }
        }

        private void SyncTransformFollow()
        {
            if (_fxRoot == null || targetButton == null) return;

            _fxRoot.anchorMin = targetButton.anchorMin;
            _fxRoot.anchorMax = targetButton.anchorMax;
            _fxRoot.pivot = targetButton.pivot;
            _fxRoot.anchoredPosition = targetButton.anchoredPosition + anchoredPositionOffset;
            _fxRoot.sizeDelta = targetButton.sizeDelta + Vector2.one * (padding * 2f) + sizeDeltaBonus;
            _fxRoot.localScale = targetButton.localScale;
            _fxRoot.localRotation = targetButton.localRotation;
            EnsureSiblingOrderOnly();
        }

        private void EnsureSiblingOrderOnly()
        {
            if (_fxRoot == null || targetButton == null) return;
            var btnIdx = targetButton.GetSiblingIndex();
            var fxIdx = _fxRoot.GetSiblingIndex();
            if (fxIdx > btnIdx)
                _fxRoot.SetSiblingIndex(btnIdx);
            else if (fxIdx < btnIdx - 1)
                _fxRoot.SetSiblingIndex(btnIdx - 1);
        }

        private void TickVisuals()
        {
            var t = Time.unscaledTime;
            if (_glow != null)
            {
                var pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * glowPulseSpeed * Mathf.PI * 2f));
                var c = glowColor;
                c.a = glowColor.a * pulse;
                _glow.color = c;
                var breath = 1f + 0.06f * Mathf.Sin(t * glowPulseSpeed * Mathf.PI * 2f + 0.7f);
                _glow.rectTransform.localScale = new Vector3(breath, breath, 1f);
            }

            if (_puffs == null) return;
            for (var i = 0; i < _puffs.Length; i++)
            {
                var p = _puffs[i];
                if (p.Rt == null || p.Img == null) continue;

                var life = Mathf.Repeat(t / p.Period + p.Phase, 1f);
                var travel = life * life;
                var lateral = Mathf.Sin((t + p.Phase * 10f) * 1.7f) * 6f * p.SideBias;
                var pos = p.Dir * (puffTravel * travel) + new Vector2(-lateral * 0.35f, lateral);
                pos += p.Dir * 18f;
                p.Rt.anchoredPosition = pos;

                var scale = Mathf.Lerp(0.55f, 1.35f, travel);
                p.Rt.sizeDelta = new Vector2(p.Size * scale, p.Size * scale);

                var a = steamColor.a;
                var fade = life < 0.15f
                    ? life / 0.15f
                    : (life > 0.55f ? 1f - (life - 0.55f) / 0.45f : 1f);
                var col = steamColor;
                col.a = a * Mathf.Clamp01(fade) * (0.7f + 0.3f * Mathf.Sin(t * puffSpeed * 0.05f + p.Phase * 6f));
                p.Img.color = col;
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static Sprite GetOrCreateSoftSprite()
        {
            if (_runtimeSoftSprite != null) return _runtimeSoftSprite;

            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "UiSteamSoftCircle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / center;
                    var dy = (y - center) / center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Более мягкий falloff — «облачко», не диск.
                    var a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, 2.35f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply(false, true);
            _runtimeSoftSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            _runtimeSoftSprite.name = "UiSteamSoftCircleSprite";
            _runtimeSoftSprite.hideFlags = HideFlags.HideAndDontSave;
            return _runtimeSoftSprite;
        }
    }
}
