using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Лёгкий UI-огонь вокруг рамки аватара на время «Ярости».
    /// Плотный ряд аддитивных Image по периметру + мягкое свечение.
    /// Когда выключен — GameObject неактивен, Update не крутится.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Match3AvatarFuryFx : MonoBehaviour
    {
        public const string FxRootName = "FuryFx";
        private const string CatalogResourcesPath = "Match3/FuryFxCatalog";
        private const string AdditiveShaderName = "UI/Additive";
        private const int BuildVersion = 3;
        private const int TopCount = 5;
        private const int BottomCount = 5;
        private const int SideCount = 4; // на каждую боковую сторону, без углов
        private const float FrameFps = 12f;

        [SerializeField] private Image borderImage;
        [SerializeField] private Sprite[] flameFrames;
        [SerializeField] private Color borderTint = new Color(1f, 0.22f, 0.05f, 1f);
        [SerializeField] private Color flameTint = new Color(1f, 0.35f, 0.08f, 0.95f);
        [SerializeField] private Color glowColor = new Color(1f, 0.12f, 0.02f, 0.55f);
        [SerializeField, Min(0.05f)] private float pulseSpeed = 2.4f;
        [SerializeField, Min(0.1f)] private float flameSize = 0.34f;
        [SerializeField, Range(0f, 0.2f)] private float edgeOutset = 0.04f;

        private RectTransform _fxRoot;
        private Image _glow;
        private Image[] _edgeGlows;
        private Image[] _flames;
        private int[] _frameOffsets;
        private int _builtVersion = -1;
        private Color _borderBaseColor = Color.white;
        private bool _borderColorCached;
        private bool _active;
        private int _lastFrame = -1;
        private float _seed;

        private static Material _additiveMat;
        private static Sprite _softSprite;
        private static Sprite[] _sharedFrames;

        private static int FlameCount => TopCount + BottomCount + SideCount * 2;

        public static Match3AvatarFuryFx Ensure(Image avatarImage, Image borderImage = null)
        {
            if (avatarImage == null) return null;

            var existing = avatarImage.GetComponent<Match3AvatarFuryFx>();
            if (existing == null)
                existing = avatarImage.gameObject.AddComponent<Match3AvatarFuryFx>();

            if (borderImage == null)
            {
                var borderTr = avatarImage.transform.Find("border");
                if (borderTr != null)
                    borderImage = borderTr.GetComponent<Image>();
            }

            existing.borderImage = borderImage;
            existing.EnsureBuilt();
            return existing;
        }

        public void SetActive(bool active)
        {
            EnsureBuilt();
            if (_active == active && _fxRoot != null && _fxRoot.gameObject.activeSelf == active)
                return;

            _active = active;
            enabled = active;

            if (_fxRoot != null)
                _fxRoot.gameObject.SetActive(active);

            if (borderImage != null)
            {
                if (!_borderColorCached)
                {
                    _borderBaseColor = borderImage.color;
                    _borderColorCached = true;
                }

                borderImage.color = active ? borderTint : _borderBaseColor;
            }

            if (active)
            {
                _seed = Random.Range(0f, 1000f);
                _lastFrame = -1;
                Tick(forceSprite: true);
            }
        }

        private void Awake()
        {
            if (borderImage == null)
            {
                var borderTr = transform.Find("border");
                if (borderTr != null)
                    borderImage = borderTr.GetComponent<Image>();
            }

            EnsureBuilt();
            if (!_active && _fxRoot != null)
                _fxRoot.gameObject.SetActive(false);
            enabled = _active;
        }

        private void OnDisable()
        {
            if (!_active && borderImage != null && _borderColorCached)
                borderImage.color = _borderBaseColor;
        }

        private void OnDestroy()
        {
            if (borderImage != null && _borderColorCached)
                borderImage.color = _borderBaseColor;
        }

        private void Update()
        {
            if (_active)
                Tick(forceSprite: false);
        }

        public void EnsureBuilt()
        {
            ResolveFrames();
            if (_fxRoot != null &&
                _flames != null &&
                _flames.Length == FlameCount &&
                _builtVersion == BuildVersion)
                return;

            var avatarRt = transform as RectTransform;
            if (avatarRt == null) return;

            var existing = transform.Find(FxRootName) as RectTransform;
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            var rootGo = new GameObject(FxRootName, typeof(RectTransform));
            _fxRoot = rootGo.GetComponent<RectTransform>();
            _fxRoot.SetParent(avatarRt, false);
            Stretch(_fxRoot);
            _fxRoot.SetAsLastSibling();

            var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var glowRt = glowGo.GetComponent<RectTransform>();
            glowRt.SetParent(_fxRoot, false);
            Stretch(glowRt);
            glowRt.offsetMin = new Vector2(-22f, -22f);
            glowRt.offsetMax = new Vector2(22f, 22f);
            _glow = glowGo.GetComponent<Image>();
            _glow.sprite = GetOrCreateSoftSprite();
            _glow.raycastTarget = false;
            _glow.color = glowColor;
            _glow.material = null;

            BuildEdgeGlows();

            var mat = GetOrCreateAdditiveMaterial();
            var frames = flameFrames;
            var count = FlameCount;
            _flames = new Image[count];
            _frameOffsets = new int[count];

            var slots = BuildPerimeterSlots();
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject("Flame_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_fxRoot, false);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                // Pivot у основания — языки растут наружу/вверх от рамки.
                rt.pivot = new Vector2(0.5f, 0.08f);

                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.material = mat;
                img.color = flameTint;
                if (frames != null && frames.Length > 0)
                    img.sprite = frames[i % frames.Length];
                img.preserveAspect = true;

                _flames[i] = img;
                _frameOffsets[i] = (i * 3) % Mathf.Max(1, frames != null ? frames.Length : 7);
                PlaceFlame(rt, slots[i], avatarRt);
            }

            _builtVersion = BuildVersion;
            rootGo.SetActive(false);
        }

        private void BuildEdgeGlows()
        {
            var soft = GetOrCreateSoftSprite();
            _edgeGlows = new Image[4];

            // top, bottom, left, right — тонкие полосы, связывают языки в «кольцо».
            Vector2[] aMin =
            {
                new Vector2(0.02f, 0.88f),
                new Vector2(0.02f, -0.06f),
                new Vector2(-0.08f, 0.06f),
                new Vector2(0.92f, 0.06f),
            };
            Vector2[] aMax =
            {
                new Vector2(0.98f, 1.12f),
                new Vector2(0.98f, 0.12f),
                new Vector2(0.08f, 0.94f),
                new Vector2(1.08f, 0.94f),
            };

            for (var i = 0; i < 4; i++)
            {
                var go = new GameObject("EdgeGlow_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(_fxRoot, false);
                rt.anchorMin = aMin[i];
                rt.anchorMax = aMax[i];
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var img = go.GetComponent<Image>();
                img.sprite = soft;
                img.raycastTarget = false;
                img.color = glowColor;
                _edgeGlows[i] = img;
            }
        }

        private void Tick(bool forceSprite)
        {
            if (_fxRoot == null) return;

            var t = Time.unscaledTime;
            var pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((t * pulseSpeed + _seed) * Mathf.PI * 2f));
            var flicker = 0.85f + 0.15f * Mathf.PerlinNoise(t * 5.5f + _seed, _seed * 0.13f);

            if (_glow != null)
            {
                var c = glowColor;
                c.a = glowColor.a * pulse * flicker;
                _glow.color = c;
                var breath = 1f + 0.08f * Mathf.Sin(t * pulseSpeed * Mathf.PI * 2f + 0.4f);
                _glow.rectTransform.localScale = new Vector3(breath, breath, 1f);
            }

            if (_edgeGlows != null)
            {
                for (var i = 0; i < _edgeGlows.Length; i++)
                {
                    var eg = _edgeGlows[i];
                    if (eg == null) continue;
                    var ec = glowColor;
                    var edgePulse = 0.65f + 0.35f * Mathf.Sin(t * (pulseSpeed * 1.15f) * Mathf.PI * 2f + i * 0.9f + _seed);
                    ec.a = glowColor.a * 0.85f * edgePulse * flicker;
                    eg.color = ec;
                }
            }

            if (borderImage != null && _active)
            {
                var bc = borderTint;
                bc.r = Mathf.Clamp01(borderTint.r * (0.85f + 0.2f * flicker));
                bc.g = Mathf.Clamp01(borderTint.g * (0.7f + 0.4f * pulse));
                borderImage.color = bc;
            }

            var frames = flameFrames;
            if (frames == null || frames.Length == 0 || _flames == null) return;

            var frame = Mathf.FloorToInt(t * FrameFps) % frames.Length;
            if (!forceSprite && frame == _lastFrame)
            {
                // Между сменой кадров всё равно чуть «дышим» альфой — дёшево и живо.
                for (var i = 0; i < _flames.Length; i++)
                {
                    var img = _flames[i];
                    if (img == null) continue;
                    var aPulse = 0.72f + 0.28f * Mathf.Sin(t * (pulseSpeed + i * 0.31f) * Mathf.PI * 2f + i);
                    var col = flameTint;
                    col.a = flameTint.a * aPulse * flicker;
                    img.color = col;
                }
                return;
            }

            _lastFrame = frame;

            for (var i = 0; i < _flames.Length; i++)
            {
                var img = _flames[i];
                if (img == null) continue;
                var idx = (frame + _frameOffsets[i]) % frames.Length;
                var sp = frames[idx];
                if (sp != null && img.sprite != sp)
                    img.sprite = sp;

                var aPulse = 0.72f + 0.28f * Mathf.Sin(t * (pulseSpeed + i * 0.31f) * Mathf.PI * 2f + i);
                var col = flameTint;
                col.a = flameTint.a * aPulse * flicker;
                img.color = col;
            }
        }

        private struct FlameSlot
        {
            public Vector2 Uv;   // 0..1 по аватару
            public float Angle; // Z rotation
            public float Scale;
        }

        private FlameSlot[] BuildPerimeterSlots()
        {
            var slots = new FlameSlot[FlameCount];
            var o = edgeOutset;
            var i = 0;

            // Верх: огонь вверх.
            for (var k = 0; k < TopCount; k++)
            {
                var t = TopCount == 1 ? 0.5f : k / (float)(TopCount - 1);
                slots[i++] = new FlameSlot
                {
                    Uv = new Vector2(Mathf.Lerp(0.06f, 0.94f, t), 1f + o),
                    Angle = Mathf.Lerp(-12f, 12f, t),
                    Scale = 1.05f,
                };
            }

            // Низ: огонь вверх от нижней кромки.
            for (var k = 0; k < BottomCount; k++)
            {
                var t = BottomCount == 1 ? 0.5f : k / (float)(BottomCount - 1);
                slots[i++] = new FlameSlot
                {
                    Uv = new Vector2(Mathf.Lerp(0.08f, 0.92f, t), -o * 0.35f),
                    Angle = Mathf.Lerp(8f, -8f, t),
                    Scale = 0.92f,
                };
            }

            // Левая сторона: языки наружу/вверх вдоль кромки.
            for (var k = 0; k < SideCount; k++)
            {
                var t = (k + 1) / (float)(SideCount + 1);
                slots[i++] = new FlameSlot
                {
                    Uv = new Vector2(-o, Mathf.Lerp(0.12f, 0.88f, t)),
                    Angle = 55f + (t - 0.5f) * 18f,
                    Scale = 0.95f,
                };
            }

            // Правая сторона.
            for (var k = 0; k < SideCount; k++)
            {
                var t = (k + 1) / (float)(SideCount + 1);
                slots[i++] = new FlameSlot
                {
                    Uv = new Vector2(1f + o, Mathf.Lerp(0.12f, 0.88f, t)),
                    Angle = -55f - (t - 0.5f) * 18f,
                    Scale = 0.95f,
                };
            }

            return slots;
        }

        private void PlaceFlame(RectTransform rt, FlameSlot slot, RectTransform avatarRt)
        {
            var w = Mathf.Max(40f, avatarRt.rect.width);
            var h = Mathf.Max(40f, avatarRt.rect.height);
            var size = Mathf.Min(w, h) * flameSize * slot.Scale;
            rt.sizeDelta = new Vector2(size, size * 1.35f);
            rt.anchoredPosition = new Vector2((slot.Uv.x - 0.5f) * w, (slot.Uv.y - 0.5f) * h);
            rt.localRotation = Quaternion.Euler(0f, 0f, slot.Angle);
            rt.localScale = Vector3.one;
        }

        private void ResolveFrames()
        {
            if (flameFrames != null && flameFrames.Length > 0) return;

            if (_sharedFrames != null && _sharedFrames.Length > 0)
            {
                flameFrames = _sharedFrames;
                return;
            }

            var catalog = Resources.Load<Match3FuryFxCatalog>(CatalogResourcesPath);
            if (catalog != null && catalog.flameFrames != null && catalog.flameFrames.Length > 0)
            {
                _sharedFrames = catalog.flameFrames;
                flameFrames = _sharedFrames;
                return;
            }

#if UNITY_EDITOR
            var loaded = new Sprite[7];
            var any = false;
            for (var i = 0; i < loaded.Length; i++)
            {
                loaded[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"Assets/_Project/img/Flame/f{i + 1}.png");
                if (loaded[i] != null) any = true;
            }

            if (any)
            {
                _sharedFrames = loaded;
                flameFrames = _sharedFrames;
            }
#endif
        }

        private static Material GetOrCreateAdditiveMaterial()
        {
            if (_additiveMat != null) return _additiveMat;
            var shader = Shader.Find(AdditiveShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[Match3] Shader UI/Additive не найден — огонь Ярости без additive.");
                return null;
            }

            _additiveMat = new Material(shader)
            {
                name = "Match3FuryAdditive (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _additiveMat;
        }

        private static Sprite GetOrCreateSoftSprite()
        {
            if (_softSprite != null) return _softSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Match3FuryGlowSoft",
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
                    var a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, 2.1f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply(false, true);
            _softSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            _softSprite.name = "Match3FuryGlowSoftSprite";
            _softSprite.hideFlags = HideFlags.HideAndDontSave;
            return _softSprite;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }
    }
}
