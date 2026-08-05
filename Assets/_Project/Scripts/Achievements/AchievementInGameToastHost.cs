using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Верхний некликабельный тост + лёгкий «фейерверк», пока есть отдельный тост главного меню для «получено».</summary>
    public sealed class AchievementInGameToastHost : MonoBehaviour
    {
        private static AchievementInGameToastHost _instance;

        private readonly Queue<AchievementUnlockInfo> _queue = new Queue<AchievementUnlockInfo>();
        private Coroutine _routine;

        private CanvasGroup _panelGroup;
        private RectTransform _panelRt;
        private TMP_Text _titleTmp;
        private TMP_Text _rewardTmp;
        private Canvas _internalCanvas;
        private GraphicRaycaster _internalRaycaster;

        private const float ShowSeconds = 4.35f;
        private const float SlidePixels = 110f;
        private const float ToastFontPx = 25f;

        public static AchievementInGameToastHost Ensure()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("AchievementInGameToastHost");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AchievementInGameToastHost>();
            return _instance;
        }

        private void Awake()
        {
            // Prefer external, scene-stylable hierarchy if present.
            if (!TryBindExternalHierarchy())
                BuildUi();
            AchievementLifecycle.OnAwaitingClaim += Handle;
            SceneManager.activeSceneChanged += HandleSceneChanged;
        }

        private void OnDestroy()
        {
            AchievementLifecycle.OnAwaitingClaim -= Handle;
            SceneManager.activeSceneChanged -= HandleSceneChanged;
            if (_instance == this)
                _instance = null;
        }

        private void HandleSceneChanged(Scene prev, Scene next)
        {
            // DDOL-хост переживает смену сцены; внешний toast в старой сцене уничтожается.
            // Останавливаем анимации, иначе корутины пишут в destroyed RectTransform/CanvasGroup.
            StopToastAnimations();

            if (TryBindExternalHierarchy())
                SetInternalVisible(false);
            else
            {
                EnsureInternalPanel();
                SetInternalVisible(true);
            }

            if (_queue.Count > 0 && _routine == null && isActiveAndEnabled)
                _routine = StartCoroutine(RunQueue());
        }

        private void StopToastAnimations()
        {
            StopAllCoroutines();
            _routine = null;
            if (_panelGroup != null)
                _panelGroup.alpha = 0f;
        }

        private void EnsureInternalPanel()
        {
            if (_internalCanvas == null)
            {
                BuildUi();
                return;
            }

            // После внешнего bind ссылки указывали на scene objects — вернуть internal.
            if (_panelGroup == null || _panelRt == null)
            {
                var pan = transform.Find("ToastPanel");
                if (pan != null)
                {
                    _panelGroup = pan.GetComponent<CanvasGroup>();
                    _panelRt = pan.GetComponent<RectTransform>();
                    _titleTmp = pan.Find("ToastTitle")?.GetComponent<TMP_Text>();
                    _rewardTmp = pan.Find("ToastRewardLine")?.GetComponent<TMP_Text>();
                }
                else
                {
                    BuildUi();
                }
            }
        }

        private void SetInternalVisible(bool visible)
        {
            if (_internalCanvas != null) _internalCanvas.enabled = visible;
            if (_internalRaycaster != null) _internalRaycaster.enabled = visible;
        }

        private bool TryBindExternalHierarchy()
        {
            var root = GameObject.Find("AchievementInGameToast");
            if (root == null) return false;

            var cg = root.GetComponent<CanvasGroup>();
            var rt = root.GetComponent<RectTransform>();
            if (cg == null || rt == null) return false;

            var title = root.transform.Find("ToastTitle")?.GetComponent<TMP_Text>();
            var reward = root.transform.Find("ToastRewardLine")?.GetComponent<TMP_Text>();
            if (title == null || reward == null) return false;

            _panelGroup = cg;
            _panelRt = rt;
            _titleTmp = title;
            _rewardTmp = reward;
            _titleTmp.richText = true;
            _rewardTmp.richText = true;
            return true;
        }

        private void Handle(AchievementUnlockInfo info)
        {
            if (info == null || info.NoticeKind != AchievementNoticeKind.CriterionMetAwaitClaim)
                return;
            _queue.Enqueue(info);
            if (_routine == null && isActiveAndEnabled)
                _routine = StartCoroutine(RunQueue());
        }

        private IEnumerator RunQueue()
        {
            while (_queue.Count > 0)
            {
                var info = _queue.Dequeue();
                const string headline = "Достижение выполнено!";
                var line2 = info.Title ?? string.Empty;
                if (_titleTmp != null)
                    _titleTmp.text = $"<b>{headline}</b>\n{line2}";
                if (_rewardTmp != null)
                    _rewardTmp.text = string.IsNullOrEmpty(info.RewardLine) ? "Награда ждёт в разделе «Достижения»." : $"Награда: {info.RewardLine}";

                yield return StartCoroutine(PresentOneCoroutine());
                yield return null;
            }

            _routine = null;
        }

        private IEnumerator PresentOneCoroutine()
        {
            if (_panelGroup != null && _panelRt != null)
            {
                PlaySmallFeuxFx(_panelRt);
                yield return StartCoroutine(CoAnimateSlideInPop());
                if (_panelGroup == null || _panelRt == null)
                    yield break;
                yield return new WaitForSecondsRealtime(ShowSeconds);
                if (_panelGroup == null || _panelRt == null)
                    yield break;
                yield return StartCoroutine(CoAnimateOut());
            }
            else
            {
                yield return null;
            }
        }

        private IEnumerator CoAnimateSlideInPop()
        {
            if (_panelGroup == null || _panelRt == null)
                yield break;

            var fromPos = _panelRt.anchoredPosition;
            var toPos = fromPos + new Vector2(0f, SlidePixels);
            var t = 0f;
            const float dur = 0.38f;
            _panelGroup.alpha = 0f;
            _panelRt.localScale = Vector3.one * 0.92f;
            _panelRt.anchoredPosition = fromPos;
            while (t < dur)
            {
                if (_panelGroup == null || _panelRt == null)
                    yield break;
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / dur);
                var ease = 1f - Mathf.Pow(1f - k, 3f);
                _panelGroup.alpha = ease;
                _panelRt.anchoredPosition = Vector2.Lerp(fromPos, toPos, ease);
                var s = Mathf.Lerp(0.92f, 1.08f, ease);
                if (ease > 0.88f)
                    s = Mathf.Lerp(1.08f, 1f, (ease - 0.88f) / 0.12f);
                else if (ease > 0.55f)
                    s = Mathf.Lerp(0.98f, 1.08f, (ease - 0.55f) / 0.33f);
                _panelRt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            if (_panelGroup == null || _panelRt == null)
                yield break;
            _panelGroup.alpha = 1f;
            _panelRt.anchoredPosition = toPos;
            _panelRt.localScale = Vector3.one;
        }

        private IEnumerator CoAnimateOut()
        {
            if (_panelGroup == null || _panelRt == null)
                yield break;

            var fromPos = _panelRt.anchoredPosition;
            var toPos = fromPos - new Vector2(0f, SlidePixels * 0.65f);
            var t = 0f;
            const float dur = 0.28f;
            while (t < dur)
            {
                if (_panelGroup == null || _panelRt == null)
                    yield break;
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / dur);
                _panelGroup.alpha = 1f - k;
                _panelRt.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
                yield return null;
            }

            if (_panelGroup == null || _panelRt == null)
                yield break;
            _panelGroup.alpha = 0f;
            _panelRt.anchoredPosition = fromPos - new Vector2(0f, SlidePixels);
            _panelRt.localScale = Vector3.one;
        }

        private void PlaySmallFeuxFx(RectTransform host)
        {
            if (host == null) return;

            var go = new GameObject("FeuxBurst", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.92f);
            rt.sizeDelta = new Vector2(480f, 80f);

            StartCoroutine(FeuxCo(rt));
        }

        private IEnumerator FeuxCo(RectTransform root)
        {
            var sprites = Resources.Load<Sprite>("UI/achievement_sparkle");
            var sparks = Mathf.Clamp(UnityEngine.Random.Range(10, 16), 8, 20);
            for (var i = 0; i < sparks; i++)
            {
                if (root == null) yield break;
                var g = new GameObject("Spark", typeof(RectTransform), typeof(Image));
                g.transform.SetParent(root, false);
                var sr = g.GetComponent<RectTransform>();
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
                sr.pivot = new Vector2(0.5f, 0.5f);
                sr.sizeDelta = new Vector2(UnityEngine.Random.Range(4f, 9f), UnityEngine.Random.Range(4f, 9f));
                sr.anchoredPosition =
                    UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(12f, 56f);

                var img = g.GetComponent<Image>();
                img.raycastTarget = false;
                img.color =
                    new Color(
                        UnityEngine.Random.Range(0.6f, 1f),
                        UnityEngine.Random.Range(0.75f, 1f),
                        0.95f,
                        0.92f);

                img.sprite =
                    sprites
                    ??
                    Sprite.Create(
                        Texture2D.whiteTexture,
                        new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                        new Vector2(0.5f, 0.5f),
                        120f);

                StartCoroutine(FlySpark(sr));
            }

            yield return new WaitForSecondsRealtime(2.2f);
            if (root != null)
                Destroy(root.gameObject);
        }

        private static IEnumerator FlySpark(RectTransform sr)
        {
            if (sr == null) yield break;
            var v = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(60f, 170f);
            var t = 0f;
            const float d = 0.65f;
            var start = sr.anchoredPosition;
            while (t < d)
            {
                // parent FeuxBurst / scene toast может уничтожиться раньше конца анимации
                if (sr == null) yield break;
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / d);
                sr.anchoredPosition = start + v * Mathf.SmoothStep(0f, 1f, k);
                sr.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.35f, k);
                if (TryGet(sr, out var img) && img != null)
                    img.color = new Color(img.color.r, img.color.g, img.color.b, 1f - k);
                yield return null;
            }

            if (sr != null)
                Destroy(sr.gameObject);
        }

        private static bool TryGet(RectTransform r, out Image img)
        {
            img = null;
            if (r == null) return false;
            img = r.GetComponent<Image>();
            return img != null;
        }

        private void BuildUi()
        {
            if (_internalCanvas == null)
            {
                var canv = gameObject.AddComponent<Canvas>();
                _internalCanvas = canv;
                canv.renderMode = RenderMode.ScreenSpaceOverlay;
                canv.overrideSorting = true;
                canv.sortingOrder = 6200;

                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                _internalRaycaster = gameObject.AddComponent<GraphicRaycaster>();
            }

            var existing = transform.Find("ToastPanel");
            if (existing != null)
            {
                _panelGroup = existing.GetComponent<CanvasGroup>();
                _panelRt = existing.GetComponent<RectTransform>();
                _titleTmp = existing.Find("ToastTitle")?.GetComponent<TMP_Text>();
                _rewardTmp = existing.Find("ToastRewardLine")?.GetComponent<TMP_Text>();
                if (_panelGroup != null)
                    _panelGroup.alpha = 0f;
                return;
            }

            var pan = new GameObject("ToastPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            pan.transform.SetParent(transform, false);
            _panelGroup = pan.GetComponent<CanvasGroup>();
            _panelRt = pan.GetComponent<RectTransform>();
            _panelGroup.blocksRaycasts = false;
            _panelGroup.interactable = false;

            var img = pan.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0.06f, 0.085f, 0.12f, 0.93f);

            _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = new Vector2(0.5f, 1f);
            _panelRt.sizeDelta = new Vector2(680f, 156f);
            _panelRt.anchoredPosition = new Vector2(0f, -44f);

            // TF2CSecondary SDF — см. Assets/_Project/Fonts/TF2CSecondary SDF.asset
            var fo = AchievementUiFontLoader.Resolve(null);

            void MakeTexts()
            {
                var titleGo =
                    new GameObject("ToastTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
                titleGo.transform.SetParent(_panelRt, false);
                var tr = titleGo.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(20f, 48f);
                tr.offsetMax = new Vector2(-20f, -18f);

                var tt = titleGo.GetComponent<TextMeshProUGUI>();
                tt.font = fo;
                tt.fontSize = ToastFontPx;
                tt.fontStyle = FontStyles.Normal;
                tt.richText = true;
                tt.alignment = TextAlignmentOptions.Center;
                tt.textWrappingMode = TextWrappingModes.Normal;
                tt.color = new Color(0.96f, 0.93f, 0.74f);

                AchievementsTmpMaterialRepair.RepairHierarchy(titleGo.transform, fo);

                _titleTmp = tt;

                var subGo =
                    new GameObject("ToastRewardLine", typeof(RectTransform), typeof(TextMeshProUGUI));
                subGo.transform.SetParent(_panelRt, false);
                var rt2 = subGo.GetComponent<RectTransform>();
                rt2.anchorMin = new Vector2(0f, 0f);
                rt2.pivot = new Vector2(0.5f, 0f);
                rt2.anchorMax = new Vector2(1f, 0f);
                rt2.offsetMin = new Vector2(20f, 10f);
                rt2.offsetMax = new Vector2(-20f, 44f);

                var rw = subGo.GetComponent<TextMeshProUGUI>();
                rw.font = fo;
                rw.fontSize = ToastFontPx;
                rw.alignment = TextAlignmentOptions.Bottom;
                rw.color = new Color(0.75f, 1f, 0.78f);
                rw.richText = true;

                AchievementsTmpMaterialRepair.RepairHierarchy(subGo.transform, fo);
                _rewardTmp = rw;
            }

            MakeTexts();

            if (_panelGroup != null)
                _panelGroup.alpha = 0f;
            if (_panelRt != null)
                _panelRt.anchoredPosition -= new Vector2(0f, SlidePixels);

            AchievementsTmpMaterialRepair.RepairHierarchy(transform, fo);
        }
    }
}
