using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.UI
{
    /// <summary>
    /// Выезжающая слева карточка статистики Match3.
    /// Иерархия и стили — в prefab Assets/_Project/Resources/Match3StatsCard.prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Match3StatsCardView : MonoBehaviour
    {
        public const string ResourcesPrefabName = "Match3StatsCard";

        private static readonly Color ColMuted = new Color(0.72f, 0.80f, 0.90f, 1f);
        private static readonly Color ColAccent = new Color(0.78f, 0.92f, 1f, 1f);
        private static readonly Color ColYellow = new Color(1f, 0.90f, 0.54f, 1f);
        private static readonly Color ColGreen = new Color(0.49f, 1f, 0.49f, 1f);
        private static readonly Color ColRed = new Color(1f, 0.49f, 0.49f, 1f);
        private static readonly Color ColHeaderBg = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ColRowAlt = new Color(1f, 1f, 1f, 0.03f);

        [Header("Roots")]
        [SerializeField] private RectTransform dimmer;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dimmerButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private RectTransform contentRoot;

        [Header("Slide")]
        [SerializeField, Min(0.05f)] private float slideSeconds = 0.28f;
        [SerializeField] private float panelLeftPadding = 0f;
        [SerializeField] private float panelTopBottomMargin = 100f;

        public bool IsOpen { get; private set; }

        public event Action Closed;

        private Coroutine _slideRoutine;
        private float _hiddenX;
        private readonly List<GameObject> _dynamicRows = new List<GameObject>(64);

        private void Awake()
        {
            EnsureRuntimeHierarchy();
            ResolveRefs();
            WireButtons();
            ApplyPanelLayout();
            SnapHidden(instant: true);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Если в инстансе ещё старая карточка без Panel — собираем иерархию на лету.
        /// После Tools → Match3 → Rebuild Match3StatsCard Prefab структура будет из префаба.
        /// </summary>
        public void EnsureRuntimeHierarchy()
        {
            if (transform.Find("Panel") != null) return;

            // Убрать старые подписи Played/Wins/Losses (сразу, иначе дубли с deferred Destroy).
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i);
                if (ch != null) DestroyImmediate(ch.gameObject);
            }

            var dimmerGo = CreateUi(transform, "Dimmer");
            Stretch(dimmerGo.GetComponent<RectTransform>());
            var dimmerImg = dimmerGo.AddComponent<Image>();
            dimmerImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimmerGo.AddComponent<Button>().transition = Selectable.Transition.None;

            var panelGo = CreateUi(transform, "Panel");
            var panelRt = panelGo.GetComponent<RectTransform>();
            // Левый якорь (не stretch-X): иначе anchoredPosition.x=0 ставит левый край в центр родителя.
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.sizeDelta = new Vector2(0f, -panelTopBottomMargin * 2f);
            panelRt.anchoredPosition = Vector2.zero;
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.18f, 0.96f);
            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.74f, 1f, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);

            var title = CreateTmp(panelGo.transform, "Title", "Статистика", 36, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0.04f, 1f);
            titleRt.anchorMax = new Vector2(0.85f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);
            titleRt.sizeDelta = new Vector2(0f, 48f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(0.78f, 0.92f, 1f, 1f);

            var closeGo = CreateUi(panelGo.transform, "CloseButton");
            var closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-12f, -12f);
            closeRt.sizeDelta = new Vector2(56f, 56f);
            closeGo.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.34f, 1f);
            closeGo.AddComponent<Button>();
            var closeLabel = CreateTmp(closeGo.transform, "Label", "X", 28, FontStyles.Bold);
            Stretch(closeLabel.rectTransform);
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            var scrollGo = CreateUi(panelGo.transform, "Scroll");
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(24f, 24f);
            scrollRt.offsetMax = new Vector2(-24f, -72f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = CreateUi(scrollGo.transform, "Viewport");
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            Stretch(viewportRt);
            viewportGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = CreateUi(viewportGo.transform, "Content");
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            SetupContentLayout(contentGo);

            scroll.viewport = viewportRt;
            scroll.content = contentRt;

            dimmer = dimmerGo.GetComponent<RectTransform>();
            panel = panelRt;
            closeButton = closeGo.GetComponent<Button>();
            dimmerButton = dimmerGo.GetComponent<Button>();
            titleText = title;
            contentRoot = contentRt;
            bodyText = null;
            WireButtons();
        }

        private void WireButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Hide);
                closeButton.onClick.AddListener(Hide);
            }
            if (dimmerButton != null)
            {
                dimmerButton.onClick.RemoveListener(Hide);
                dimmerButton.onClick.AddListener(Hide);
            }
        }

        private static GameObject CreateUi(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TMP_Text CreateTmp(Transform parent, string name, string text, float size, FontStyles style)
        {
            var go = CreateUi(parent, name);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void Show()
        {
            ResolveRefs();
            ApplyPanelLayout();
            // После смены ширины родителя пересчитать старт за экраном.
            if (!IsOpen && panel != null)
                panel.anchoredPosition = new Vector2(_hiddenX, 0f);
            gameObject.SetActive(true);
            IsOpen = true;
            if (_slideRoutine != null) StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(SlideRoutine(show: true));
        }

        public void Hide()
        {
            if (!IsOpen && !gameObject.activeSelf)
            {
                Closed?.Invoke();
                return;
            }
            IsOpen = false;
            if (_slideRoutine != null) StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(SlideRoutine(show: false));
        }

        public void SetTitle(string title)
        {
            ResolveRefs();
            if (titleText != null)
                titleText.text = string.IsNullOrWhiteSpace(title) ? "Статистика" : title;
        }

        public void BindStats(
            int played, int wins, int losses,
            ModeRow[] modes,
            ArenaRow[] arenas,
            int mineTotalWins,
            MineFloorRow[] mineFloors)
        {
            ResolveRefs();
            EnsureContentHost();
            ClearDynamicRows();

            AddSectionTitle("Итого");
            AddSummaryStrip(played, wins, losses);

            AddSpacer(10f);
            AddSectionTitle("Режимы");
            AddTableHeader(new[] { "Режим", "Сыгр.", "Поб.", "Пор." }, new[] { 1.6f, 0.7f, 0.7f, 0.7f });
            if (modes != null)
            {
                for (var i = 0; i < modes.Length; i++)
                {
                    var m = modes[i];
                    if (m == null) continue;
                    AddTableRow(
                        new[]
                        {
                            m.label ?? m.id ?? "?",
                            Mathf.Max(0, m.played).ToString(),
                            Mathf.Max(0, m.wins).ToString(),
                            Mathf.Max(0, m.losses).ToString(),
                        },
                        new[] { 1.6f, 0.7f, 0.7f, 0.7f },
                        new[] { ColMuted, Color.white, ColGreen, ColRed },
                        new[] { TextAlignmentOptions.MidlineLeft, TextAlignmentOptions.Center, TextAlignmentOptions.Center, TextAlignmentOptions.Center },
                        alt: i % 2 == 1);
                }
            }

            AddSpacer(10f);
            AddSectionTitle("Турниры арены");
            AddTableHeader(new[] { "Турнир", "Сыгр." }, new[] { 1.8f, 0.8f });
            var arenaTotal = 0;
            if (arenas != null)
            {
                for (var i = 0; i < arenas.Length; i++)
                {
                    var a = arenas[i];
                    if (a == null) continue;
                    var n = Mathf.Max(0, a.played);
                    arenaTotal += n;
                    AddTableRow(
                        new[] { a.label ?? a.id ?? "?", n.ToString() },
                        new[] { 1.8f, 0.8f },
                        new[] { ColMuted, ColYellow },
                        new[] { TextAlignmentOptions.MidlineLeft, TextAlignmentOptions.Center },
                        alt: i % 2 == 1);
                }
            }
            AddKeyValueRow("Всего турниров", arenaTotal.ToString(), ColYellow);

            AddSpacer(10f);
            AddSectionTitle("Шахта (этажи)");
            AddKeyValueRow("Побед на этажах", Mathf.Max(0, mineTotalWins).ToString(), ColGreen);
            if (mineFloors != null && mineFloors.Length > 0)
            {
                AddTableHeader(new[] { "Сложн.", "Этаж", "Поб." }, new[] { 1.2f, 0.8f, 0.8f });
                var rowIndex = 0;
                for (var i = 0; i < mineFloors.Length; i++)
                {
                    var f = mineFloors[i];
                    if (f == null || f.wins <= 0) continue;
                    AddTableRow(
                        new[] { DiffRu(f.difficulty), f.floor.ToString(), f.wins.ToString() },
                        new[] { 1.2f, 0.8f, 0.8f },
                        new[] { ColMuted, Color.white, ColGreen },
                        new[] { TextAlignmentOptions.Center, TextAlignmentOptions.Center, TextAlignmentOptions.Center },
                        alt: rowIndex % 2 == 1);
                    rowIndex++;
                }
            }
            else
            {
                AddHintRow("Пока нет побед на этажах.");
            }
        }

        private IEnumerator SlideRoutine(bool show)
        {
            if (panel == null)
            {
                if (!show)
                {
                    gameObject.SetActive(false);
                    Closed?.Invoke();
                }
                yield break;
            }

            if (dimmer != null)
                dimmer.gameObject.SetActive(true);

            var start = panel.anchoredPosition.x;
            var end = show ? panelLeftPadding : _hiddenX;
            var t = 0f;
            var dur = Mathf.Max(0.05f, slideSeconds);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                var x = Mathf.Lerp(start, end, k);
                panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);
                if (dimmer != null)
                {
                    var img = dimmer.GetComponent<Image>();
                    if (img != null)
                    {
                        var c = img.color;
                        c.a = Mathf.Lerp(show ? 0f : 0.55f, show ? 0.55f : 0f, k);
                        img.color = c;
                    }
                }
                yield return null;
            }

            panel.anchoredPosition = new Vector2(end, panel.anchoredPosition.y);
            if (!show)
            {
                gameObject.SetActive(false);
                Closed?.Invoke();
            }
            _slideRoutine = null;
        }

        private void SnapHidden(bool instant)
        {
            ResolveRefs();
            ApplyPanelLayout();
            if (panel != null)
                panel.anchoredPosition = new Vector2(_hiddenX, panel.anchoredPosition.y);
            if (dimmer != null)
            {
                var img = dimmer.GetComponent<Image>();
                if (img != null)
                {
                    var c = img.color;
                    c.a = 0f;
                    img.color = c;
                }
            }
            IsOpen = false;
            if (instant) { /* no-op */ }
        }

        private void ApplyPanelLayout()
        {
            var root = transform as RectTransform;
            if (root != null)
            {
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                root.anchoredPosition = Vector2.zero;
                root.pivot = new Vector2(0.5f, 0.5f);
                root.localScale = Vector3.one;
            }

            if (dimmer != null)
            {
                // Dimmer строго в границах Match3StatsCard (stretch + нулевые отступы).
                dimmer.anchorMin = Vector2.zero;
                dimmer.anchorMax = Vector2.one;
                dimmer.pivot = new Vector2(0.5f, 0.5f);
                dimmer.offsetMin = Vector2.zero;
                dimmer.offsetMax = Vector2.zero;
                dimmer.anchoredPosition = Vector2.zero;
                dimmer.sizeDelta = Vector2.zero;
                dimmer.localScale = Vector3.one;
            }

            if (panel == null) return;

            // Панель: якорь слева + высота stretch с margin; ширина = ширина родителя.
            // Нельзя stretch-X + pivot(0,*) + анимация anchoredPosition — ломает left/right.
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.localScale = Vector3.one;

            Canvas.ForceUpdateCanvases();
            var parentW = root != null ? root.rect.width : 0f;
            if (parentW < 1f && root != null && root.parent is RectTransform parentRt)
                parentW = parentRt.rect.width;
            var w = Mathf.Max(320f, parentW);
            var margin = Mathf.Max(0f, panelTopBottomMargin);
            var keepX = panel.anchoredPosition.x;
            panel.sizeDelta = new Vector2(w, -margin * 2f);
            panel.anchoredPosition = new Vector2(keepX, 0f);

            _hiddenX = -w - 40f;
        }

        private void ResolveRefs()
        {
            if (dimmer == null)
            {
                var t = transform.Find("Dimmer");
                if (t != null) dimmer = t as RectTransform;
            }
            if (panel == null)
            {
                var t = transform.Find("Panel");
                if (t != null) panel = t as RectTransform;
            }
            if (closeButton == null && panel != null)
                closeButton = panel.Find("CloseButton")?.GetComponent<Button>();
            // LiberationSans не содержит ✕ (\u2715) — всегда ASCII "X".
            if (closeButton != null)
            {
                var label = closeButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null && label.text != "X")
                    label.text = "X";
            }
            if (dimmerButton == null && dimmer != null)
                dimmerButton = dimmer.GetComponent<Button>();
            if (titleText == null && panel != null)
                titleText = panel.Find("Title")?.GetComponent<TMP_Text>();
            if (contentRoot == null && panel != null)
            {
                var content = panel.Find("Scroll/Viewport/Content");
                if (content != null) contentRoot = content as RectTransform;
            }
            if (bodyText == null && contentRoot != null)
            {
                var body = contentRoot.Find("BodyText");
                if (body != null) bodyText = body.GetComponent<TMP_Text>();
            }
        }

        private void EnsureContentHost()
        {
            if (contentRoot == null && panel != null)
            {
                var content = panel.Find("Scroll/Viewport/Content");
                if (content != null) contentRoot = content as RectTransform;
            }
            if (contentRoot == null) return;

            SetupContentLayout(contentRoot.gameObject);
            if (bodyText != null)
                bodyText.gameObject.SetActive(false);
            else
            {
                var legacy = contentRoot.Find("BodyText");
                if (legacy != null) legacy.gameObject.SetActive(false);
            }
        }

        private static void SetupContentLayout(GameObject contentGo)
        {
            if (contentGo == null) return;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 4, 12);
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void ClearDynamicRows()
        {
            for (var i = 0; i < _dynamicRows.Count; i++)
            {
                var go = _dynamicRows[i];
                if (go != null) Destroy(go);
            }
            _dynamicRows.Clear();

            if (contentRoot == null) return;
            for (var i = contentRoot.childCount - 1; i >= 0; i--)
            {
                var ch = contentRoot.GetChild(i);
                if (ch == null) continue;
                if (ch.name == "BodyText")
                {
                    ch.gameObject.SetActive(false);
                    continue;
                }
                if (ch.name.StartsWith("Stat", StringComparison.Ordinal))
                    Destroy(ch.gameObject);
            }
        }

        private void Track(GameObject go)
        {
            if (go != null) _dynamicRows.Add(go);
        }

        private void AddSpacer(float height)
        {
            if (contentRoot == null) return;
            var go = CreateUi(contentRoot, "StatSpacer");
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            Track(go);
        }

        private void AddSectionTitle(string text)
        {
            if (contentRoot == null) return;
            var tmp = CreateTmp(contentRoot, "StatSection", text, 30, FontStyles.Bold);
            tmp.color = ColAccent;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            Track(tmp.gameObject);
        }

        private void AddHintRow(string text)
        {
            if (contentRoot == null) return;
            var tmp = CreateTmp(contentRoot, "StatHint", text, 24, FontStyles.Italic);
            tmp.color = ColMuted;
            tmp.alignment = TextAlignmentOptions.Center;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 36f;
            le.preferredHeight = 36f;
            Track(tmp.gameObject);
        }

        private void AddKeyValueRow(string label, string value, Color valueColor)
        {
            AddTableRow(
                new[] { label, value },
                new[] { 1.8f, 0.8f },
                new[] { ColMuted, valueColor },
                new[] { TextAlignmentOptions.MidlineLeft, TextAlignmentOptions.Center },
                alt: false,
                height: 36f);
        }

        private void AddSummaryStrip(int played, int wins, int losses)
        {
            if (contentRoot == null) return;
            var row = CreateUi(contentRoot, "StatSummary");
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 78f;
            le.preferredHeight = 78f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;
            hl.padding = new RectOffset(0, 0, 2, 2);

            AddSummaryCell(row.transform, "Сыграно", Mathf.Max(0, played).ToString(), ColYellow);
            AddSummaryCell(row.transform, "Побед", Mathf.Max(0, wins).ToString(), ColGreen);
            AddSummaryCell(row.transform, "Поражений", Mathf.Max(0, losses).ToString(), ColRed);
            Track(row);
        }

        private void AddSummaryCell(Transform parent, string label, string value, Color valueColor)
        {
            var cell = CreateUi(parent, "Cell");
            cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var bg = cell.AddComponent<Image>();
            bg.color = ColHeaderBg;
            var vl = cell.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = true;
            vl.spacing = 2f;
            vl.padding = new RectOffset(4, 4, 6, 6);

            var labelTmp = CreateTmp(cell.transform, "Label", label, 20, FontStyles.Normal);
            labelTmp.color = ColMuted;
            labelTmp.alignment = TextAlignmentOptions.Center;

            var valueTmp = CreateTmp(cell.transform, "Value", value, 32, FontStyles.Bold);
            valueTmp.color = valueColor;
            valueTmp.alignment = TextAlignmentOptions.Center;
        }

        private void AddTableHeader(string[] labels, float[] flex)
        {
            AddTableRow(
                labels,
                flex,
                null,
                null,
                alt: false,
                height: 34f,
                header: true);
        }

        private void AddTableRow(
            string[] cells,
            float[] flex,
            Color[] colors,
            TextAlignmentOptions[] aligns,
            bool alt,
            float height = 34f,
            bool header = false)
        {
            if (contentRoot == null || cells == null || cells.Length == 0) return;
            var row = CreateUi(contentRoot, header ? "StatHeader" : "StatRow");
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            if (header || alt)
            {
                var bg = row.AddComponent<Image>();
                bg.color = header ? ColHeaderBg : ColRowAlt;
                bg.raycastTarget = false;
            }

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 4f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;
            hl.padding = new RectOffset(6, 6, 0, 0);

            for (var i = 0; i < cells.Length; i++)
            {
                var align = aligns != null && i < aligns.Length
                    ? aligns[i]
                    : (header || i > 0 ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft);
                var color = colors != null && i < colors.Length
                    ? colors[i]
                    : (header ? ColAccent : Color.white);
                var style = header ? FontStyles.Bold : FontStyles.Normal;
                var size = header ? 22f : 24f;

                var tmp = CreateTmp(row.transform, "C" + i, cells[i] ?? "", size, style);
                tmp.color = color;
                tmp.alignment = align;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                tmp.raycastTarget = false;

                var cellLe = tmp.gameObject.AddComponent<LayoutElement>();
                cellLe.flexibleWidth = flex != null && i < flex.Length ? flex[i] : 1f;
                cellLe.minWidth = 40f;
            }

            Track(row);
        }

        private static string DiffRu(string d)
        {
            return (d ?? "").ToLowerInvariant() switch
            {
                "easy" => "Лёгк.",
                "medium" => "Сред.",
                "hard" => "Слож.",
                _ => string.IsNullOrEmpty(d) ? "?" : d,
            };
        }

        [Serializable]
        public sealed class ModeRow
        {
            public string id;
            public string label;
            public int played;
            public int wins;
            public int losses;
        }

        [Serializable]
        public sealed class ArenaRow
        {
            public string id;
            public string label;
            public int played;
        }

        [Serializable]
        public sealed class MineFloorRow
        {
            public string difficulty;
            public int floor;
            public int wins;
        }

#if UNITY_EDITOR
        [MenuItem("Tools/Match3/Rebuild Match3StatsCard Prefab")]
        public static void RebuildPrefabMenu()
        {
            RebuildPrefabAsset();
        }

        public static GameObject RebuildPrefabAsset()
        {
            const string path = "Assets/_Project/Resources/Match3StatsCard.prefab";
            var root = new GameObject("Match3StatsCard", typeof(RectTransform), typeof(Match3StatsCardView));
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var dimmerGo = CreateUi(root.transform, "Dimmer");
            var dimmerRt = dimmerGo.GetComponent<RectTransform>();
            Stretch(dimmerRt);
            var dimmerImg = dimmerGo.AddComponent<Image>();
            dimmerImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimmerGo.AddComponent<Button>().transition = Selectable.Transition.None;

            var panelGo = CreateUi(root.transform, "Panel");
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.sizeDelta = new Vector2(1080f, -200f);
            panelRt.anchoredPosition = Vector2.zero;
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.18f, 0.96f);
            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.74f, 1f, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);

            var title = CreateTmp(panelGo.transform, "Title", "Статистика", 36, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0.04f, 1f);
            titleRt.anchorMax = new Vector2(0.85f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);
            titleRt.sizeDelta = new Vector2(0f, 48f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(0.78f, 0.92f, 1f, 1f);

            var closeGo = CreateUi(panelGo.transform, "CloseButton");
            var closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-12f, -12f);
            closeRt.sizeDelta = new Vector2(56f, 56f);
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.25f, 0.28f, 0.34f, 1f);
            closeGo.AddComponent<Button>();
            var closeLabel = CreateTmp(closeGo.transform, "Label", "X", 28, FontStyles.Bold);
            Stretch(closeLabel.rectTransform);
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            var scrollGo = CreateUi(panelGo.transform, "Scroll");
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(24f, 24f);
            scrollRt.offsetMax = new Vector2(-24f, -72f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = CreateUi(scrollGo.transform, "Viewport");
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            Stretch(viewportRt);
            viewportGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = CreateUi(viewportGo.transform, "Content");
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            SetupContentLayout(contentGo);

            scroll.viewport = viewportRt;
            scroll.content = contentRt;

            var view = root.GetComponent<Match3StatsCardView>();
            var so = new SerializedObject(view);
            so.FindProperty("dimmer").objectReferenceValue = dimmerRt;
            so.FindProperty("panel").objectReferenceValue = panelRt;
            so.FindProperty("closeButton").objectReferenceValue = closeGo.GetComponent<Button>();
            so.FindProperty("dimmerButton").objectReferenceValue = dimmerGo.GetComponent<Button>();
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("contentRoot").objectReferenceValue = contentRt;
            so.FindProperty("bodyText").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[Match3StatsCard] Prefab rebuilt: " + path);
            return prefab;
        }

#endif
    }
}
