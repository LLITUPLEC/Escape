using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Project.Duel
{
    /// <summary>
    /// Контроллер модального окна «домофон».
    /// UI берётся из дочерней иерархии (префаб), элементы ищутся по именам.
    /// Если префаб не найден — автоматически генерирует fallback-UI.
    /// </summary>
    public sealed class DuelKeypadModal : MonoBehaviour
    {
        private const int MaxAttempts = 6;

        // ─── Serialized refs (назначаются из префаба или находятся автоматически) ──
        [Header("UI References (auto-resolved if empty)")]
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private Image         dimmerImage;
        [SerializeField] private Button        dimmerButton;
        [SerializeField] private Button        closeButton;
        [SerializeField] private Text          lockedLabel;
        [SerializeField] private Transform     cellsRoot;
        [SerializeField] private Transform     gridRoot;
        [SerializeField] private Transform     bottomRow;
        [SerializeField] private Text          attemptsLog;
        [SerializeField] private Text          counterText;
        [SerializeField] private RectTransform barFill;
        [SerializeField] private Image         barFillImage;
        [SerializeField] private Text          hintText;

        // ─── Runtime state ────────────────────────────────────────────────────
        private readonly List<(Image bg, Text txt)> _cells = new();

        private Action _onCorrect;
        private Action _onClosed;
        private string _pin;
        private int    _codeLen;
        private string _input = "";
        private int    _currentDoorId = -1;
        private bool   _isOpen;
        private int    _openedFrame;

        private readonly Dictionary<int, List<string>> _historyPerDoor = new();

        public bool IsOpen => _isOpen;

        // ─── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            EnsureEventSystem();
            gameObject.SetActive(false);

            if (panelRect == null)
                AutoResolveRefs();

            if (panelRect == null)
                BuildFallbackUI();

            WireButtons();
        }

        /// <summary>
        /// Открыть модальное окно для конкретной двери.
        /// doorId используется для сохранения/восстановления истории попыток в рамках сессии.
        /// </summary>
        public void Show(string pin, int codeLen, Action onCorrect,
                         int doorId = -1, Action onClosed = null)
        {
            _pin          = pin;
            _codeLen      = Mathf.Max(1, codeLen);
            _onCorrect    = onCorrect;
            _onClosed     = onClosed;
            _currentDoorId = doorId;
            _input        = "";
            _isOpen       = true;
            _openedFrame  = Time.frameCount;

            gameObject.SetActive(true);
            RebuildCells();
            RefreshAll();
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen    = false;
            _onCorrect = null;
            var cb = _onClosed;
            _onClosed  = null;
            gameObject.SetActive(false);
            cb?.Invoke();
        }

        public void ClearAllHistory()
        {
            _historyPerDoor.Clear();
        }

        private void Update()
        {
            if (!_isOpen) return;
            if (Time.frameCount == _openedFrame) return;
            if (EscPressed()) Close();
        }

        // ─── Auto-resolve refs from child hierarchy ───────────────────────────
        private void AutoResolveRefs()
        {
            panelRect    = DeepFind<RectTransform>("Panel");
            dimmerImage  = DeepFind<Image>("Dimmer");
            dimmerButton = DeepFind<Button>("Dimmer");
            closeButton  = DeepFind<Button>("CloseBtn");
            lockedLabel  = DeepFind<Text>("LockedLabel");
            cellsRoot    = DeepFind<Transform>("CellsRoot");
            gridRoot     = DeepFind<Transform>("GridRoot");
            bottomRow    = DeepFind<Transform>("BottomRow");
            attemptsLog  = DeepFind<Text>("AttemptsLog");
            counterText  = DeepFind<Text>("CounterText");
            barFill      = DeepFind<RectTransform>("BarFill");
            barFillImage = DeepFind<Image>("BarFill");
            hintText     = DeepFind<Text>("HintText");
        }

        private T DeepFind<T>(string objName) where T : Component
        {
            var all = GetComponentsInChildren<T>(true);
            foreach (var c in all)
                if (c.gameObject.name == objName) return c;
            return null;
        }

        private void WireButtons()
        {
            if (dimmerButton != null)
                dimmerButton.onClick.AddListener(Close);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (gridRoot != null)
            {
                foreach (var btn in gridRoot.GetComponentsInChildren<Button>(true))
                {
                    var label = btn.GetComponentInChildren<Text>(true);
                    if (label == null) continue;
                    var txt = label.text.Trim();
                    if (int.TryParse(txt, out var digit))
                    {
                        var d = digit;
                        btn.onClick.AddListener(() => PressDigit(d));
                    }
                }
            }

            if (bottomRow != null)
            {
                foreach (var btn in bottomRow.GetComponentsInChildren<Button>(true))
                {
                    var label = btn.GetComponentInChildren<Text>(true);
                    if (label == null) continue;
                    var txt = label.text.Trim().ToUpperInvariant();
                    if (txt.Contains("DEL"))
                        btn.onClick.AddListener(PressDelete);
                    else if (txt.Contains("ВВОД") || txt.Contains("OK") || txt.Contains("ENTER"))
                        btn.onClick.AddListener(PressEnter);
                    else if (int.TryParse(txt, out var d))
                    {
                        var digit = d;
                        btn.onClick.AddListener(() => PressDigit(digit));
                    }
                }
            }
        }

        // ─── Input logic ──────────────────────────────────────────────────────
        private void PressDigit(int d)
        {
            if (!_isOpen || _input.Length >= _codeLen) return;
            _input += d.ToString();
            RefreshInput();
        }

        private void PressDelete()
        {
            if (!_isOpen || _input.Length == 0) return;
            _input = _input.Substring(0, _input.Length - 1);
            RefreshInput();
        }

        private void PressEnter()
        {
            if (!_isOpen || _input.Length < _codeLen) return;
            Submit();
        }

        private void Submit()
        {
            var guess = _input;
            var (digs, pos) = Score(_pin, guess);
            var history = GetHistory(_currentDoorId);
            history.Add(MakeLine(history.Count + 1, guess, digs, pos));

            if (string.Equals(guess, _pin, StringComparison.Ordinal))
            {
                var cb = _onCorrect;
                _onCorrect = null;
                _onClosed  = null;
                _isOpen    = false;
                gameObject.SetActive(false);
                cb?.Invoke();
                return;
            }

            _input = "";
            RefreshAll();
        }

        // ─── History per door ─────────────────────────────────────────────────
        private List<string> GetHistory(int doorId)
        {
            if (doorId < 0) doorId = 0;
            if (!_historyPerDoor.TryGetValue(doorId, out var list))
            {
                list = new List<string>();
                _historyPerDoor[doorId] = list;
            }
            return list;
        }

        // ─── Refresh ──────────────────────────────────────────────────────────
        private void RebuildCells()
        {
            if (cellsRoot == null) return;
            foreach (Transform ch in cellsRoot) Destroy(ch.gameObject);
            _cells.Clear();

            var green = new Color(0.24f, 0.67f, 0.24f, 1f);
            for (var i = 0; i < _codeLen; i++)
            {
                var go = new GameObject("C" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(cellsRoot, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(42f, 50f);
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = 42f;
                var bg = go.GetComponent<Image>();
                bg.color = new Color(green.r, green.g, green.b, 0.1f);

                var tGo = new GameObject("T", typeof(RectTransform), typeof(Text));
                tGo.transform.SetParent(go.transform, false);
                var tRt = tGo.GetComponent<RectTransform>();
                tRt.anchorMin = Vector2.zero;
                tRt.anchorMax = Vector2.one;
                tRt.offsetMin = tRt.offsetMax = Vector2.zero;
                var txt = tGo.GetComponent<Text>();
                SetFont(txt);
                txt.fontSize  = 22;
                txt.color     = green;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.fontStyle = FontStyle.Bold;

                _cells.Add((bg, txt));
            }
        }

        private void RefreshAll()
        {
            RefreshInput();
            RefreshLog();
            RefreshCounter();
            RefreshBar();
        }

        private void RefreshInput()
        {
            if (lockedLabel != null)
                lockedLabel.gameObject.SetActive(_input.Length == 0);

            var green = new Color(0.24f, 0.67f, 0.24f, 1f);
            for (var i = 0; i < _cells.Count; i++)
            {
                var (bg, txt) = _cells[i];
                if (i < _input.Length)
                {
                    txt.text = _input[i].ToString();
                    bg.color = new Color(green.r, green.g, green.b, 0.28f);
                }
                else
                {
                    txt.text = "";
                    bg.color = new Color(green.r, green.g, green.b,
                                         i == _input.Length ? 0.20f : 0.08f);
                }
            }
        }

        private void RefreshLog()
        {
            if (attemptsLog == null) return;
            var history = GetHistory(_currentDoorId);
            attemptsLog.text = string.Join("\n", history);
        }

        private void RefreshCounter()
        {
            if (counterText == null) return;
            var history = GetHistory(_currentDoorId);
            counterText.text = $"ПОПЫТОК:   {history.Count} / {MaxAttempts}";
        }

        private void RefreshBar()
        {
            if (barFill == null || barFillImage == null) return;
            var history = GetHistory(_currentDoorId);
            var frac = Mathf.Clamp01(1f - (float)history.Count / MaxAttempts);
            barFill.anchorMax = new Vector2(frac, 1f);

            Color col;
            if (frac > 0.6f)
                col = Color.Lerp(new Color(0.9f, 0.78f, 0.1f), new Color(0.15f, 0.85f, 0.24f), (frac - 0.6f) / 0.4f);
            else if (frac > 0.3f)
                col = Color.Lerp(new Color(0.85f, 0.3f, 0.1f), new Color(0.9f, 0.78f, 0.1f), (frac - 0.3f) / 0.3f);
            else
                col = Color.Lerp(new Color(0.55f, 0.05f, 0.05f), new Color(0.85f, 0.3f, 0.1f), frac / 0.3f);
            barFillImage.color = col;
        }

        // ─── Scoring ──────────────────────────────────────────────────────────
        private static string MakeLine(int n, string guess, int digs, int pos)
        {
            var dc = digs > 0 ? "#E8A030" : "#604838";
            var pc = pos  > 0 ? "#38C090" : "#604838";
            return $"{n})  {guess}  \u2014  <color={dc}>{digs}</color>  :  <color={pc}>{pos}</color>";
        }

        private static (int digs, int pos) Score(string pin, string guess)
        {
            var n = pin.Length;
            if (guess.Length != n) return (0, 0);
            var pos = 0;
            var cc = new int[10];
            var gc = new int[10];
            for (var i = 0; i < n; i++)
            {
                if (pin[i] == guess[i]) pos++;
                cc[pin[i]   - '0']++;
                gc[guess[i] - '0']++;
            }
            var digs = 0;
            for (var d = 0; d < 10; d++) digs += Math.Min(cc[d], gc[d]);
            return (digs, pos);
        }

        // ─── Hotkeys ──────────────────────────────────────────────────────────
        private static bool EscPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ─── Font ─────────────────────────────────────────────────────────────
        private static Font _cachedFont;

        private static void SetFont(Text t)
        {
            if (_cachedFont == null)
            {
                try { _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* */ }
                if (_cachedFont == null)
                    try { _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { /* */ }
            }
            if (_cachedFont != null) t.font = _cachedFont;
        }

        // ─── EventSystem ──────────────────────────────────────────────────────
        private static void EnsureEventSystem()
        {
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
#if ENABLE_INPUT_SYSTEM
            if (!es.GetComponent<InputSystemUIInputModule>())
                es.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            if (!es.GetComponent<StandaloneInputModule>())
                es.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Fallback: если префаб не содержит нужных элементов, генерируем UI
        // ═══════════════════════════════════════════════════════════════════════
        private void BuildFallbackUI()
        {
            var font = _cachedFont;
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* */ }
                if (font == null)
                    try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { /* */ }
                _cachedFont = font;
            }

            // Canvas
            var canvasGo = new GameObject("ModalCanvas",
                typeof(UnityEngine.Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<UnityEngine.Canvas>();
            canvas.renderMode     = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder   = 2000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            // Root (stretch)
            var root = MakeGO<RectTransform>("Root", canvasGo.transform);
            Stretch(root.GetComponent<RectTransform>());

            // Dimmer
            var dimGo = MakeGO<Image>("Dimmer", root.transform);
            Stretch(dimGo.GetComponent<RectTransform>());
            dimmerImage = dimGo.GetComponent<Image>();
            dimmerImage.color = new Color(0, 0, 0, 0.70f);
            dimmerButton = dimGo.AddComponent<Button>();
            dimmerButton.transition = Selectable.Transition.None;
            dimmerButton.targetGraphic = dimmerImage;

            // Hint
            var hintGo = MakeGO<Text>("HintText", root.transform);
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0f);
            hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.sizeDelta = new Vector2(500, 28);
            hintRt.anchoredPosition = new Vector2(0, 16);
            hintText = hintGo.GetComponent<Text>();
            if (font) hintText.font = font;
            hintText.text      = "Esc  —  закрыть";
            hintText.fontSize  = 13;
            hintText.color     = new Color(1, 1, 1, 0.35f);
            hintText.alignment = TextAnchor.MiddleCenter;

            // Panel (70% width)
            var panelW = 1920f * 0.70f;
            var panelH = panelW / 1.82f;
            var panelGo = MakeGO<Image>("Panel", root.transform);
            panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(panelW, panelH);
            panelGo.GetComponent<Image>().color = Hex("221A0E");
            // Panel должен блокировать клики Dimmer'а
            panelGo.AddComponent<Button>().transition = Selectable.Transition.None;
            panelGo.GetComponent<Button>().targetGraphic = panelGo.GetComponent<Image>();

            var hlg = panelGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.spacing = 0;

            // ── Left column ──
            var lc = MakeGO<Image>("LC", panelGo.transform);
            lc.GetComponent<Image>().color = Hex("221A0E");
            lc.AddComponent<LayoutElement>().flexibleWidth = 0.56f;
            var lcVlg = lc.AddComponent<VerticalLayoutGroup>();
            lcVlg.childAlignment = TextAnchor.UpperCenter;
            lcVlg.childControlWidth = lcVlg.childControlHeight = true;
            lcVlg.spacing = 5;
            lcVlg.padding = new RectOffset(10, 10, 8, 8);

            BuildFallbackLeftColumn(lc.transform, font, panelW);

            // ── Separator ──
            var sep = MakeGO<Image>("VSep", panelGo.transform);
            sep.GetComponent<Image>().color = new Color(1, 1, 1, 0.07f);
            var sepLe = sep.AddComponent<LayoutElement>();
            sepLe.preferredWidth = 1; sepLe.flexibleWidth = 0;

            // ── Right column ──
            var rc = MakeGO<Image>("RC", panelGo.transform);
            rc.GetComponent<Image>().color = Hex("1D1609");
            rc.AddComponent<LayoutElement>().flexibleWidth = 0.44f;
            var rcVlg = rc.AddComponent<VerticalLayoutGroup>();
            rcVlg.childAlignment = TextAnchor.UpperLeft;
            rcVlg.childControlWidth = rcVlg.childControlHeight = true;
            rcVlg.spacing = 4;
            rcVlg.padding = new RectOffset(14, 14, 12, 10);

            BuildFallbackRightColumn(rc.transform, font);
        }

        private void BuildFallbackLeftColumn(Transform p, Font font, float panelW)
        {
            var btnW = Mathf.Clamp((panelW * 0.56f - 30f) / 3f, 56f, 110f);
            var btnH = Mathf.Clamp(btnW * 0.75f, 42f, 82f);

            // Title row
            var tr = FixedH("TR", p, 36);
            var trHlg = tr.AddComponent<HorizontalLayoutGroup>();
            trHlg.childControlWidth = trHlg.childControlHeight = true;
            trHlg.childAlignment = TextAnchor.MiddleCenter;

            FillText("AL", tr.transform, font, "▽", 13, Hex("C8A870"), TextAnchor.MiddleRight, flex: 1f);
            FillText("TT", tr.transform, font, "В Х О Д", 17, Hex("C8A870"), TextAnchor.MiddleCenter, flex: 5f, style: FontStyle.Bold);
            FillText("AR", tr.transform, font, "▽", 13, Hex("C8A870"), TextAnchor.MiddleLeft, flex: 1f);

            // Close ×
            var xGo = MakeGO<Image>("CloseBtn", tr.transform);
            var xLe = xGo.AddComponent<LayoutElement>();
            xLe.preferredWidth = 30; xLe.flexibleWidth = 0;
            xGo.GetComponent<Image>().color = new Color(0.7f, 0.14f, 0.1f, 0.5f);
            closeButton = xGo.AddComponent<Button>();
            closeButton.targetGraphic = xGo.GetComponent<Image>();
            FillChildText(xGo.transform, font, "×", 20, new Color(1, 0.38f, 0.38f));

            // Separator
            FixedH("TS", p, 1).AddComponent<Image>().color = new Color(0.77f, 0.66f, 0.44f, 0.22f);

            // Display
            var disp = FixedH("Disp", p, 68);
            disp.AddComponent<Image>().color = Hex("152215");

            var lockGo = MakeGO<Text>("LockedLabel", disp.transform);
            Stretch(lockGo.GetComponent<RectTransform>());
            lockedLabel = lockGo.GetComponent<Text>();
            if (font) lockedLabel.font = font;
            lockedLabel.text      = "З А Б Л О К И Р О В А Н О";
            lockedLabel.fontSize  = 14;
            lockedLabel.color     = new Color(0.24f, 0.67f, 0.24f, 0.38f);
            lockedLabel.alignment = TextAnchor.MiddleCenter;
            lockedLabel.fontStyle = FontStyle.Bold;

            var cellsGo = MakeGO<RectTransform>("CellsRoot", disp.transform);
            var cRt = cellsGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.04f, 0.10f);
            cRt.anchorMax = new Vector2(0.96f, 0.90f);
            cRt.offsetMin = cRt.offsetMax = Vector2.zero;
            var cHlg = cellsGo.AddComponent<HorizontalLayoutGroup>();
            cHlg.childAlignment = TextAnchor.MiddleCenter;
            cHlg.childControlWidth = false; cHlg.childControlHeight = true;
            cHlg.spacing = 6;
            cellsRoot = cellsGo.transform;

            // Grid 1-9
            var gridGo = MakeGO<RectTransform>("GridRoot", p);
            var gridLe = gridGo.AddComponent<LayoutElement>();
            gridLe.preferredHeight = btnH * 3 + 10f;
            gridLe.minHeight = gridLe.preferredHeight;
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize = new Vector2(btnW, btnH);
            grid.spacing = new Vector2(5, 5);
            grid.childAlignment = TextAnchor.UpperCenter;
            gridRoot = gridGo.transform;

            for (var d = 1; d <= 9; d++)
                MakeButton(gridGo.transform, d.ToString(), btnW, btnH, font, Hex("3A2A14"), 22);

            // Bottom row
            var bot = FixedH("BottomRow", p, (int)btnH);
            var botHlg = bot.AddComponent<HorizontalLayoutGroup>();
            botHlg.childControlWidth = false; botHlg.childControlHeight = true;
            botHlg.childAlignment = TextAnchor.MiddleCenter;
            botHlg.spacing = 5;
            bottomRow = bot.transform;

            MakeButton(bot.transform, "◄  DEL", btnW, btnH, font, Hex("4A2A10"), 15);
            MakeButton(bot.transform, "0", btnW, btnH, font, Hex("3A2A14"), 22);
            MakeButton(bot.transform, "✓  ВВОД", btnW, btnH, font, Hex("1A4A1A"), 15);
        }

        private void BuildFallbackRightColumn(Transform p, Font font)
        {
            // Header
            var hdr = FixedH("Hdr", p, 30);
            var hdrHlg = hdr.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.childControlWidth = false; hdrHlg.childControlHeight = true;
            hdrHlg.childAlignment = TextAnchor.MiddleLeft;
            hdrHlg.spacing = 8;

            var dot = MakeGO<RectTransform>("Dot", hdr.transform);
            dot.GetComponent<RectTransform>().sizeDelta = new Vector2(12, 12);
            dot.AddComponent<LayoutElement>().preferredWidth = 12;
            dot.AddComponent<Image>().color = new Color(0.2f, 0.9f, 0.3f);

            FillText("HT", hdr.transform, font, "ПОПЫТКИ", 17, Hex("C8A870"), TextAnchor.MiddleLeft,
                     style: FontStyle.Bold, prefW: 200);

            FixedH("HS1", p, 1).AddComponent<Image>().color = new Color(1, 1, 1, 0.07f);

            // Log
            var logGo = MakeGO<Text>("AttemptsLog", p);
            var logLe = logGo.AddComponent<LayoutElement>();
            logLe.flexibleHeight = 1f; logLe.minHeight = 60f;
            attemptsLog = logGo.GetComponent<Text>();
            if (font) attemptsLog.font = font;
            attemptsLog.supportRichText  = true;
            attemptsLog.verticalOverflow = VerticalWrapMode.Overflow;
            attemptsLog.fontSize  = 17;
            attemptsLog.color     = Hex("9A8860");
            attemptsLog.alignment = TextAnchor.UpperLeft;

            // Spacer
            MakeGO<RectTransform>("Sp", p).gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            FixedH("HS2", p, 1).AddComponent<Image>().color = new Color(1, 1, 1, 0.07f);

            // Counter
            var ctrGo = FixedH("Ctr", p, 28);
            counterText = ctrGo.AddComponent<Text>();
            if (font) counterText.font = font;
            counterText.supportRichText = true;
            counterText.fontSize  = 17;
            counterText.color     = Hex("C8A870");
            counterText.alignment = TextAnchor.MiddleLeft;
            counterText.fontStyle = FontStyle.Bold;

            // Bar label
            FillText("BL", p, font, "З А Щ И Т А   З А М К А", 11, Hex("685540"),
                     TextAnchor.MiddleLeft, fixH: 20);

            // Bar
            var barCont = FixedH("Bar", p, 12);
            barCont.AddComponent<Image>().color = Hex("100C06");
            var fillGo = MakeGO<Image>("BarFill", barCont.transform);
            barFill = fillGo.GetComponent<RectTransform>();
            barFill.anchorMin = new Vector2(0, 0);
            barFill.anchorMax = new Vector2(1, 1);
            barFill.offsetMin = new Vector2(1, 1);
            barFill.offsetMax = new Vector2(-1, -1);
            barFillImage = fillGo.GetComponent<Image>();
            barFillImage.color = new Color(0.18f, 0.84f, 0.24f);
        }

        // ─── Fallback helpers ─────────────────────────────────────────────────
        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        private static GameObject MakeGO<T>(string n, Transform parent) where T : Component
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (typeof(T) != typeof(RectTransform))
                go.AddComponent<T>();
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static GameObject FixedH(string n, Transform parent, int h)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, h);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = h;
            return go;
        }

        private static void FillText(string n, Transform parent, Font font, string text,
                                      int size, Color col, TextAnchor anchor,
                                      float flex = 0f, FontStyle style = FontStyle.Normal,
                                      int fixH = 0, float prefW = 0f)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            if (flex > 0) le.flexibleWidth = flex;
            if (prefW > 0) le.preferredWidth = prefW;
            if (fixH > 0) { le.preferredHeight = le.minHeight = fixH; }
            var t = go.GetComponent<Text>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = col;
            t.alignment = anchor; t.fontStyle = style;
        }

        private static void FillChildText(Transform parent, Font font, string text, int size, Color col)
        {
            var go = new GameObject("T", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = col;
            t.alignment = TextAnchor.MiddleCenter;
        }

        private static void MakeButton(Transform parent, string label, float w, float h,
                                        Font font, Color bg, int fontSize)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = h;
            go.GetComponent<Image>().color = bg;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            var cols = btn.colors;
            cols.highlightedColor = new Color(bg.r * 1.4f, bg.g * 1.4f, bg.b * 1.3f, 1f);
            cols.pressedColor = new Color(bg.r * 0.65f, bg.g * 0.65f, bg.b * 0.65f, 1f);
            btn.colors = cols;

            FillChildText(go.transform, font, label, fontSize, Hex("C8A870"));
        }
    }
}
