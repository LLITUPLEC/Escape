using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Duel.Editor
{
    /// <summary>
    /// Генерирует префаб модального окна «домофон» (KeypadModal.prefab).
    /// После генерации его можно стилизовать вручную в редакторе.
    /// Меню: Tools → Duel → Generate Keypad Prefab
    /// </summary>
    public static class KeypadPrefabGenerator
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/KeypadModal.prefab";

        [MenuItem("Tools/Duel/Generate Keypad Prefab")]
        public static void Generate()
        {
            var font = GetFont();

            // Root GO с DuelKeypadModal
            var root = new GameObject("KeypadModal");

            // Canvas
            var canvasGo = new GameObject("ModalCanvas");
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode     = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder   = 2000;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;

            // Root container
            var uiRoot = MakeStretch("Root", canvasGo.transform);

            // Dimmer
            var dimmer = MakeStretch("Dimmer", uiRoot.transform);
            dimmer.AddComponent<Image>().color = new Color(0, 0, 0, 0.70f);
            var dimBtn = dimmer.AddComponent<Button>();
            dimBtn.transition    = Selectable.Transition.None;
            dimBtn.targetGraphic = dimmer.GetComponent<Image>();

            // Hint
            var hint = Make("HintText", uiRoot.transform);
            var hintRt = hint.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0f);
            hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.sizeDelta = new Vector2(500, 28);
            hintRt.anchoredPosition = new Vector2(0, 16);
            SetText(hint, font, "Esc  —  закрыть", 13, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleCenter);

            // Panel (fixed size, closer to authored mockup)
            const float panelW = 1180f;
            const float panelH = 640f;

            var panel = Make("Panel", uiRoot.transform);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta        = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = Hex("221A0E");

            // Panel blocker — без него клик внутри Panel пролетает на Dimmer
            var panelBtn = panel.AddComponent<Button>();
            panelBtn.transition    = Selectable.Transition.None;
            panelBtn.targetGraphic = panel.GetComponent<Image>();

            var hlg = panel.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.spacing = 0;

            // === Left column ===
            var lc = Make("LC", panel.transform);
            lc.AddComponent<Image>().color = Hex("221A0E");
            lc.AddComponent<LayoutElement>().flexibleWidth = 0.56f;
            var lcVlg = lc.AddComponent<VerticalLayoutGroup>();
            lcVlg.childAlignment = TextAnchor.UpperCenter;
            lcVlg.childControlWidth = lcVlg.childControlHeight = true;
            lcVlg.spacing = 5;
            lcVlg.padding = new RectOffset(10, 10, 8, 8);

            var btnW = Mathf.Clamp((panelW * 0.56f - 30f) / 3f, 56f, 110f);
            var btnH = Mathf.Clamp(btnW * 0.75f, 42f, 82f);

            // Title row
            var tr = FixedH("TR", lc.transform, 36);
            var trHlg = tr.AddComponent<HorizontalLayoutGroup>();
            trHlg.childControlWidth = trHlg.childControlHeight = true;
            trHlg.childAlignment = TextAnchor.MiddleCenter;

            TextEl("AL", tr.transform, font, "▽", 13, Hex("C8A870"), TextAnchor.MiddleRight, flex: 1f);
            TextEl("TT", tr.transform, font, "В Х О Д", 17, Hex("C8A870"), TextAnchor.MiddleCenter, flex: 5f, style: FontStyle.Bold);
            TextEl("AR", tr.transform, font, "▽", 13, Hex("C8A870"), TextAnchor.MiddleLeft, flex: 1f);

            // Close button
            var xGo = Make("CloseBtn", tr.transform);
            var xLe = xGo.AddComponent<LayoutElement>();
            xLe.preferredWidth = 30; xLe.flexibleWidth = 0;
            xGo.AddComponent<Image>().color = new Color(0.7f, 0.14f, 0.1f, 0.5f);
            xGo.AddComponent<Button>().targetGraphic = xGo.GetComponent<Image>();
            ChildText(xGo.transform, font, "×", 20, new Color(1f, 0.38f, 0.38f));

            // Title separator
            FixedH("TS", lc.transform, 1).AddComponent<Image>().color = new Color(0.77f, 0.66f, 0.44f, 0.22f);

            // Display
            var disp = FixedH("Disp", lc.transform, 68);
            disp.AddComponent<Image>().color = Hex("152215");

            // Border
            var brd = Make("Brd", disp.transform);
            var brdRt = brd.GetComponent<RectTransform>();
            brdRt.anchorMin = Vector2.zero; brdRt.anchorMax = Vector2.one;
            brdRt.offsetMin = new Vector2(-1.5f, -1.5f); brdRt.offsetMax = new Vector2(1.5f, 1.5f);
            brd.AddComponent<Image>().color = Hex("255525");
            brd.transform.SetAsFirstSibling();

            // Locked label
            var lockGo = Make("LockedLabel", disp.transform);
            Stretch(lockGo.GetComponent<RectTransform>());
            SetText(lockGo, font, "З А Б Л О К И Р О В А Н О", 14,
                    new Color(0.24f, 0.67f, 0.24f, 0.38f), TextAnchor.MiddleCenter, FontStyle.Bold);

            // Cells root
            var cellsGo = Make("CellsRoot", disp.transform);
            var cellsRt = cellsGo.GetComponent<RectTransform>();
            cellsRt.anchorMin = new Vector2(0.04f, 0.10f);
            cellsRt.anchorMax = new Vector2(0.96f, 0.90f);
            cellsRt.offsetMin = cellsRt.offsetMax = Vector2.zero;
            var cHlg = cellsGo.AddComponent<HorizontalLayoutGroup>();
            cHlg.childAlignment    = TextAnchor.MiddleCenter;
            cHlg.childControlWidth = false; cHlg.childControlHeight = true;
            cHlg.spacing = 6;

            // Grid 1-9
            var gridGo = Make("GridRoot", lc.transform);
            var gridLe = gridGo.AddComponent<LayoutElement>();
            gridLe.preferredHeight = btnH * 3 + 10; gridLe.minHeight = gridLe.preferredHeight;
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize        = new Vector2(btnW, btnH);
            grid.spacing         = new Vector2(5, 5);
            grid.childAlignment  = TextAnchor.UpperCenter;

            for (var d = 1; d <= 9; d++)
                Btn(gridGo.transform, d.ToString(), btnW, btnH, font, Hex("3A2A14"), 22);

            // Bottom row
            var bot = FixedH("BottomRow", lc.transform, (int)btnH);
            var botHlg = bot.AddComponent<HorizontalLayoutGroup>();
            botHlg.childControlWidth = false; botHlg.childControlHeight = true;
            botHlg.childAlignment = TextAnchor.MiddleCenter; botHlg.spacing = 5;

            Btn(bot.transform, "◄  DEL",  btnW, btnH, font, Hex("4A2A10"), 15);
            Btn(bot.transform, "0",        btnW, btnH, font, Hex("3A2A14"), 22);
            Btn(bot.transform, "✓  ВВОД", btnW, btnH, font, Hex("1A4A1A"), 15);

            // === Separator ===
            var sep = Make("VSep", panel.transform);
            sep.AddComponent<Image>().color = new Color(1, 1, 1, 0.07f);
            var sepLe = sep.AddComponent<LayoutElement>();
            sepLe.preferredWidth = 1; sepLe.flexibleWidth = 0;

            // === Right column ===
            var rc = Make("RC", panel.transform);
            rc.AddComponent<Image>().color = Hex("1D1609");
            rc.AddComponent<LayoutElement>().flexibleWidth = 0.44f;
            var rcVlg = rc.AddComponent<VerticalLayoutGroup>();
            rcVlg.childAlignment    = TextAnchor.UpperLeft;
            rcVlg.childControlWidth = rcVlg.childControlHeight = true;
            rcVlg.spacing = 4;
            rcVlg.padding = new RectOffset(14, 14, 12, 10);

            // Header
            var hdr = FixedH("Hdr", rc.transform, 30);
            var hdrHlg = hdr.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.childControlWidth = false; hdrHlg.childControlHeight = true;
            hdrHlg.childAlignment = TextAnchor.MiddleLeft; hdrHlg.spacing = 8;

            var dot = Make("Dot", hdr.transform);
            dot.GetComponent<RectTransform>().sizeDelta = new Vector2(12, 12);
            dot.AddComponent<LayoutElement>().preferredWidth = 12;
            dot.AddComponent<Image>().color = new Color(0.2f, 0.9f, 0.3f);

            TextEl("HT", hdr.transform, font, "ПОПЫТКИ", 17, Hex("C8A870"),
                   TextAnchor.MiddleLeft, style: FontStyle.Bold, prefW: 200);

            FixedH("HS1", rc.transform, 1).AddComponent<Image>().color = new Color(1, 1, 1, 0.07f);

            // Log
            var logGo = Make("AttemptsLog", rc.transform);
            var logLe = logGo.AddComponent<LayoutElement>();
            logLe.flexibleHeight = 1; logLe.minHeight = 60;
            var logTxt = SetText(logGo, font, "", 17, Hex("9A8860"), TextAnchor.UpperLeft);
            logTxt.supportRichText  = true;
            logTxt.verticalOverflow = VerticalWrapMode.Overflow;

            Make("Sp", rc.transform).AddComponent<LayoutElement>().flexibleHeight = 1;

            FixedH("HS2", rc.transform, 1).AddComponent<Image>().color = new Color(1, 1, 1, 0.07f);

            // Counter
            var ctrGo = FixedH("Ctr", rc.transform, 28);
            var ctrTxt = SetText(ctrGo, font, "ПОПЫТОК:   0 / 6", 17, Hex("C8A870"), TextAnchor.MiddleLeft, FontStyle.Bold);
            ctrTxt.gameObject.name = "CounterText";

            // bar label
            TextEl("BL", rc.transform, font, "З А Щ И Т А   З А М К А", 11, Hex("685540"),
                   TextAnchor.MiddleLeft, fixH: 20);

            // Bar
            var barCont = FixedH("Bar", rc.transform, 12);
            barCont.AddComponent<Image>().color = Hex("100C06");
            var fillGo = Make("BarFill", barCont.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1, 1); fillRt.offsetMax = new Vector2(-1, -1);
            fillGo.AddComponent<Image>().color = new Color(0.18f, 0.84f, 0.24f);

            // Attach DuelKeypadModal component & wire serialized fields
            var modal = root.AddComponent<DuelKeypadModal>();
            // SerializedObject позволяет задать приватные [SerializeField] через Editor API
            var so = new SerializedObject(modal);
            so.FindProperty("panelRect").objectReferenceValue    = panelRt;
            so.FindProperty("dimmerImage").objectReferenceValue  = dimmer.GetComponent<Image>();
            so.FindProperty("dimmerButton").objectReferenceValue = dimmer.GetComponent<Button>();
            so.FindProperty("closeButton").objectReferenceValue  = xGo.GetComponent<Button>();
            so.FindProperty("lockedLabel").objectReferenceValue  = lockGo.GetComponent<Text>();
            so.FindProperty("cellsRoot").objectReferenceValue    = cellsGo.transform;
            so.FindProperty("gridRoot").objectReferenceValue     = gridGo.transform;
            so.FindProperty("bottomRow").objectReferenceValue    = bot.transform;
            so.FindProperty("attemptsLog").objectReferenceValue  = logGo.GetComponent<Text>();
            so.FindProperty("counterText").objectReferenceValue  = ctrTxt;
            so.FindProperty("barFill").objectReferenceValue      = fillRt;
            so.FindProperty("barFillImage").objectReferenceValue = fillGo.GetComponent<Image>();
            so.FindProperty("hintText").objectReferenceValue     = hint.GetComponent<Text>();
            so.ApplyModifiedPropertiesWithoutUndo();

            // Save prefab
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[KeypadPrefabGenerator] Prefab saved → {PrefabPath}");
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(PrefabPath);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        private static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject MakeStretch(string name, Transform parent)
        {
            var go = Make(name, parent);
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static GameObject FixedH(string name, Transform parent, int h)
        {
            var go = Make(name, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, h);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = h;
            return go;
        }

        private static Text SetText(GameObject go, Font font, string text, int size, Color col,
                                     TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var t = go.GetComponent<Text>();
            if (t == null) t = go.AddComponent<Text>();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = size; t.color = col;
            t.alignment = anchor; t.fontStyle = style;
            return t;
        }

        private static void TextEl(string name, Transform parent, Font font, string text, int size,
                                    Color col, TextAnchor anchor, float flex = 0f,
                                    FontStyle style = FontStyle.Normal, int fixH = 0, float prefW = 0)
        {
            var go = Make(name, parent);
            var le = go.AddComponent<LayoutElement>();
            if (flex > 0) le.flexibleWidth = flex;
            if (prefW > 0) le.preferredWidth = prefW;
            if (fixH > 0) { le.preferredHeight = le.minHeight = fixH; }
            SetText(go, font, text, size, col, anchor, style);
        }

        private static void ChildText(Transform parent, Font font, string text, int size, Color col)
        {
            var go = Make("T", parent);
            Stretch(go.GetComponent<RectTransform>());
            SetText(go, font, text, size, col, TextAnchor.MiddleCenter);
        }

        private static void Btn(Transform parent, string label, float w, float h,
                                 Font font, Color bg, int fontSize)
        {
            var go = Make("Btn_" + label, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = h;
            go.AddComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            ChildText(go.transform, font, label, fontSize, Hex("C8A870"));
        }

        private static Font GetFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* */ }
            if (f == null)
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { /* */ }
            return f;
        }
    }
}
