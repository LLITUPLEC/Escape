#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Project.Match3;

/// <summary>
/// Editor utility.  Menu: Tools → Match3 → Создать префабы UI
///
/// Creates fully-configured prefabs in Assets/_Project/Prefabs/Match3/:
///   Match3PlayerPanel.prefab
///   Match3AbilityPanel.prefab
///   Match3BoardView.prefab
///   Match3GameHUD.prefab
///   Match3SearchingPanel.prefab
///   Match3GameOverPanel.prefab
///
/// After creation, assign them to DuelMatch3Manager in the Inspector.
/// </summary>
public static class Match3PrefabCreator
{
    private const string PrefabDir = "Assets/_Project/Prefabs/Match3";

    [MenuItem("Tools/Match3/Создать префабы UI")]
    public static void CreateAll()
    {
        EnsureFolder();

        CreatePlayerPanel();
        CreateAbilityPanel();
        CreateBoardView();
        CreateGameHUD();
        CreateSearchingPanel();
        CreateGameOverPanel();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Match3] Префабы созданы в {PrefabDir}");
    }

    // ─── Player Panel ─────────────────────────────────────────────────────────

    private static void CreatePlayerPanel()
    {
        var root = MakeRoot("Match3PlayerPanel");
        root.AddComponent<Image>().color = new Color(0.11f, 0.11f, 0.20f, 0.97f);
        var panel = root.AddComponent<Match3PlayerPanel>();

        // Avatar
        var avatarGo = MakeImg(root.transform, "Avatar", new Color(0.22f, 0.22f, 0.33f));
        Anchor(avatarGo.GetComponent<RectTransform>(), V2(0.1f, 0.67f), V2(0.9f, 0.96f));
        panel.avatarImage = avatarGo.GetComponent<Image>();

        var avatarTxt = MakeTxt(avatarGo.transform, "AvatarTxt", "?", 52, new Color(0.5f, 0.5f, 0.6f));
        avatarTxt.alignment = TextAnchor.MiddleCenter;
        Stretch(avatarTxt.GetComponent<RectTransform>());
        panel.avatarPlaceholderText = avatarTxt;

        // Name
        panel.nameText = MakeTxt(root.transform, "NameText", "Игрок", 17, Color.white);
        Anchor(panel.nameText.GetComponent<RectTransform>(), V2(0.05f, 0.62f), V2(0.95f, 0.67f));
        panel.nameText.alignment = TextAnchor.MiddleCenter;

        // HP
        MakeTxt(root.transform, "HpLabel", "HP", 13, new Color(1f, 0.45f, 0.45f));
        Anchor(root.transform.Find("HpLabel").GetComponent<RectTransform>(), V2(0.05f, 0.57f), V2(0.25f, 0.62f));
        panel.hpText = MakeTxt(root.transform, "HpValue", "150/150", 12, Color.white);
        Anchor(panel.hpText.GetComponent<RectTransform>(), V2(0.60f, 0.57f), V2(0.97f, 0.62f));
        panel.hpFill = MakeBar(root.transform, "HpBar",
            new Color(0.78f, 0.14f, 0.14f), V2(0.05f, 0.52f), V2(0.95f, 0.57f));

        // Mana
        MakeTxt(root.transform, "MpLabel", "МП", 13, new Color(0.45f, 0.65f, 1f));
        Anchor(root.transform.Find("MpLabel").GetComponent<RectTransform>(), V2(0.05f, 0.47f), V2(0.25f, 0.52f));
        panel.manaText = MakeTxt(root.transform, "MpValue", "0/100", 12, Color.white);
        Anchor(panel.manaText.GetComponent<RectTransform>(), V2(0.60f, 0.47f), V2(0.97f, 0.52f));
        panel.manaFill = MakeBar(root.transform, "MpBar",
            new Color(0.14f, 0.35f, 0.82f), V2(0.05f, 0.42f), V2(0.95f, 0.47f));

        Save(root, "Match3PlayerPanel");
    }

    // ─── Ability Panel ────────────────────────────────────────────────────────

    private static void CreateAbilityPanel()
    {
        var root = MakeRoot("Match3AbilityPanel");
        var ap   = root.AddComponent<Match3AbilityPanel>();

        // Label
        var lbl = MakeTxt(root.transform, "Label", "Способности:", 12, new Color(0.75f, 0.75f, 0.85f));
        Anchor(lbl.GetComponent<RectTransform>(), V2(0.05f, 0.82f), V2(0.95f, 1f));

        // Cross button
        var crossGo = MakeBtn(root.transform, "CrossBtn", "", new Color(0.28f, 0.14f, 0.48f));
        Anchor(crossGo.GetComponent<RectTransform>(), V2(0.05f, 0.40f), V2(0.50f, 0.80f));
        ap.crossButton = crossGo.GetComponent<Button>();
        MakeTxt(crossGo.transform, "Icon", "✝ Крест\n5×5", 12, Color.white);
        Anchor(crossGo.transform.Find("Icon").GetComponent<RectTransform>(), V2(0.05f, 0.5f), V2(0.95f, 1f));
        ap.crossCooldownText = MakeTxt(crossGo.transform, "Cd", "20 мп", 11, new Color(0.9f, 0.85f, 0.5f));
        Anchor(ap.crossCooldownText.GetComponent<RectTransform>(), V2(0.05f, 0f), V2(0.95f, 0.48f));

        // Square button
        var squareGo = MakeBtn(root.transform, "SquareBtn", "", new Color(0.14f, 0.25f, 0.48f));
        Anchor(squareGo.GetComponent<RectTransform>(), V2(0.52f, 0.40f), V2(0.97f, 0.80f));
        ap.squareButton = squareGo.GetComponent<Button>();
        MakeTxt(squareGo.transform, "Icon", "□ Кв-т\n3×3", 12, Color.white);
        Anchor(squareGo.transform.Find("Icon").GetComponent<RectTransform>(), V2(0.05f, 0.5f), V2(0.95f, 1f));
        ap.squareCooldownText = MakeTxt(squareGo.transform, "Cd", "20 мп", 11, new Color(0.9f, 0.85f, 0.5f));
        Anchor(ap.squareCooldownText.GetComponent<RectTransform>(), V2(0.05f, 0f), V2(0.95f, 0.48f));

        // Hint bar
        var hintGo = new GameObject("AbilityHint");
        hintGo.transform.SetParent(root.transform, false);
        var hintRt = hintGo.AddComponent<RectTransform>();
        hintRt.anchorMin = V2(0.05f, 0f); hintRt.anchorMax = V2(0.95f, 0.38f);
        hintRt.offsetMin = hintRt.offsetMax = Vector2.zero;
        hintGo.AddComponent<Image>().color = new Color(0.45f, 0.30f, 0f, 0.85f);
        var hintTxt = MakeTxt(hintGo.transform, "HintTxt", "☞ Кликните клетку на поле", 12, Color.white);
        hintTxt.alignment = TextAnchor.MiddleCenter;
        Stretch(hintTxt.GetComponent<RectTransform>());
        ap.abilityHint = hintGo;
        hintGo.SetActive(false);

        Save(root, "Match3AbilityPanel");
    }

    // ─── Board View ───────────────────────────────────────────────────────────

    private static void CreateBoardView()
    {
        var root = MakeRoot("Match3BoardView");
        var bv   = root.AddComponent<Match3BoardView>();
        var so   = new SerializedObject(bv);
        so.FindProperty("ballsAtlas").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/img/balls-sprite.png");
        so.ApplyModifiedPropertiesWithoutUndo();

        // Decorative frame
        var frame = MakeImg(root.transform, "Frame", new Color(0.38f, 0.32f, 0.18f));
        Stretch(frame.GetComponent<RectTransform>());

        // Inner background
        var bg = MakeImg(frame.transform, "Bg", new Color(0.17f, 0.15f, 0.11f));
        Anchor(bg.GetComponent<RectTransform>(), V2(0.025f, 0.015f), V2(0.975f, 0.985f));

        // Grid container (cells will be created at runtime via Build())
        const int cells  = Match3BoardLogic.Size;
        const int cellPx = 74;
        const int gapPx  = 4;
        int total = cells * cellPx + (cells - 1) * gapPx; // 464

        var gridGo = new GameObject("CellContainer");
        var gridRt = gridGo.AddComponent<RectTransform>();
        gridRt.SetParent(bg.transform, false);
        gridRt.anchorMin = new Vector2(0.5f, 0.5f);
        gridRt.anchorMax = new Vector2(0.5f, 0.5f);
        gridRt.pivot     = new Vector2(0.5f, 0.5f);
        gridRt.sizeDelta = new Vector2(total, total);

        var glg = gridGo.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(cellPx, cellPx);
        glg.spacing         = new Vector2(gapPx, gapPx);
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = cells;
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment  = TextAnchor.UpperLeft;

        bv.cellContainer = gridGo.transform;

        Save(root, "Match3BoardView");
    }

    // ─── Game HUD ─────────────────────────────────────────────────────────────

    private static void CreateGameHUD()
    {
        var root = MakeRoot("Match3GameHUD");
        var hud  = root.AddComponent<Match3GameHUD>();

        hud.turnText = MakeTxt(root.transform, "TurnText", "Ваш ход!", 20, Color.white);
        Anchor(hud.turnText.GetComponent<RectTransform>(), V2(0f, 0f), V2(0.72f, 1f));
        hud.turnText.alignment = TextAnchor.MiddleLeft;

        hud.timerText = MakeTxt(root.transform, "TimerText", "30", 26, new Color(1f, 0.85f, 0.2f));
        Anchor(hud.timerText.GetComponent<RectTransform>(), V2(0.74f, 0f), V2(1f, 1f));
        hud.timerText.alignment = TextAnchor.MiddleRight;

        Save(root, "Match3GameHUD");
    }

    // ─── Searching Panel ──────────────────────────────────────────────────────

    private static void CreateSearchingPanel()
    {
        var root = MakeRoot("Match3SearchingPanel");
        root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
        var sp = root.AddComponent<Match3SearchingPanel>();

        sp.statusText = MakeTxt(root.transform, "StatusText", "Поиск соперника…", 22, Color.white);
        Anchor(sp.statusText.GetComponent<RectTransform>(), V2(0.05f, 0.35f), V2(0.95f, 0.70f));
        sp.statusText.alignment = TextAnchor.MiddleCenter;

        var cancelGo = MakeBtn(root.transform, "CancelBtn", "Отмена", new Color(0.45f, 0.12f, 0.12f));
        Anchor(cancelGo.GetComponent<RectTransform>(), V2(0.20f, 0.08f), V2(0.80f, 0.30f));
        sp.cancelButton = cancelGo.GetComponent<Button>();

        Save(root, "Match3SearchingPanel");
    }

    // ─── Game Over Panel ──────────────────────────────────────────────────────

    private static void CreateGameOverPanel()
    {
        var root = MakeRoot("Match3GameOverPanel");
        root.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.10f, 0.96f);
        var gop = root.AddComponent<Match3GameOverPanel>();

        // Top stripe
        var stripe = MakeImg(root.transform, "Stripe", new Color(0.35f, 0.28f, 0.12f));
        Anchor(stripe.GetComponent<RectTransform>(), V2(0f, 0.85f), V2(1f, 1f));

        gop.titleText = MakeTxt(root.transform, "TitleText", "Победа!", 38, Color.white);
        Anchor(gop.titleText.GetComponent<RectTransform>(), V2(0.05f, 0.60f), V2(0.95f, 0.88f));
        gop.titleText.alignment = TextAnchor.MiddleCenter;

        gop.rewardText = MakeTxt(root.transform, "RewardText", "+100 опыта\n+50 золота", 19,
            new Color(1f, 0.90f, 0.30f));
        Anchor(gop.rewardText.GetComponent<RectTransform>(), V2(0.05f, 0.33f), V2(0.95f, 0.60f));
        gop.rewardText.alignment = TextAnchor.MiddleCenter;

        var backGo = MakeBtn(root.transform, "BackButton", "В главное меню",
            new Color(0.18f, 0.28f, 0.55f));
        Anchor(backGo.GetComponent<RectTransform>(), V2(0.15f, 0.06f), V2(0.85f, 0.28f));
        gop.backButton = backGo.GetComponent<Button>();

        Save(root, "Match3GameOverPanel");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Match3");
    }

    private static void Save(GameObject root, string prefabName)
    {
        string path = $"{PrefabDir}/{prefabName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[Match3] Saved {path}");
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    private static GameObject MakeRoot(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static GameObject MakeImg(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static Text MakeTxt(Transform parent, string name, string text,
        int size, Color color)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text      = text;
        t.font      = GetFont();
        t.fontSize  = size;
        t.color     = color;
        t.alignment = TextAnchor.MiddleLeft;
        return t;
    }

    private static GameObject MakeBtn(Transform parent, string name, string label, Color bgColor)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.15f);
        cb.pressedColor     = Color.Lerp(bgColor, Color.black, 0.18f);
        btn.colors = cb;

        if (!string.IsNullOrEmpty(label))
        {
            var lbl = MakeTxt(go.transform, "Label", label, 15, Color.white);
            lbl.alignment = TextAnchor.MiddleCenter;
            Stretch(lbl.GetComponent<RectTransform>());
        }
        return go;
    }

    private static Image MakeBar(Transform parent, string name, Color fillColor,
        Vector2 aMin, Vector2 aMax)
    {
        var trackGo = MakeImg(parent, name + "Track", new Color(0.08f, 0.08f, 0.10f, 0.9f));
        Anchor(trackGo.GetComponent<RectTransform>(), aMin, aMax);

        var fillGo = MakeImg(trackGo.transform, name + "Fill", fillColor);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(1, 1); fillRt.offsetMax = new Vector2(-1, -1);

        var fillImg = fillGo.GetComponent<Image>();
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 1f;
        return fillImg;
    }

    private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Anchor(Component c, Vector2 min, Vector2 max)
        => Anchor(c.GetComponent<RectTransform>(), min, max);

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Vector2 V2(float x, float y) => new Vector2(x, y);

    private static Font _font;
    private static Font GetFont()
    {
        if (_font != null) return _font;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return _font;
    }
}
#endif
