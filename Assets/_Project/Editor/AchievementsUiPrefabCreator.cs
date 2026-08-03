#if UNITY_EDITOR
using System.IO;
using Project.Achievements;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools → Escape → Создать UI достижений — генерирует префаб и опционально вставляет в MainMenuHudOverlay.
/// </summary>
public static class AchievementsUiPrefabCreator
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Achievements/AchievementsPanel.prefab";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/MainMenu/MainMenuHudOverlay.prefab";
    private const string AchievementUiFontPath = "Assets/_Project/Fonts/TF2CSecondary SDF.asset";

    [MenuItem("Tools/Escape/Создать UI достижений")]
    public static void CreatePrefabOnly()
    {
        BuildAndSavePrefab();
    }

    [MenuItem("Tools/Escape/Вставить AchievementsPanel в MainMenuHudOverlay")]
    public static void MergeIntoHud()
    {
        BuildAndSavePrefab();
        var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (panelPrefab == null)
        {
            EditorUtility.DisplayDialog("Achievements", "Сначала создайте префаб: Tools/Escape/Создать UI достижений", "OK");
            return;
        }

        var hudRoot = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            var existing = hudRoot.transform.Find(AchievementsPanelController.PanelRootName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, hudRoot.transform);
            inst.name = AchievementsPanelController.PanelRootName;
            inst.transform.SetAsLastSibling();
            PrefabUtility.SaveAsPrefabAsset(hudRoot, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hudRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Achievements", "Готово: AchievementsPanel добавлен в MainMenuHudOverlay.", "OK");
    }

    private static void BuildAndSavePrefab()
    {
        EnsureFolders();

        var root = new GameObject(AchievementsPanelController.PanelRootName, typeof(RectTransform));
        StretchFull(root.GetComponent<RectTransform>());
        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        root.AddComponent<AchievementsPanelController>();

        var dim = new GameObject("AchievementsDimmer", typeof(RectTransform));
        dim.transform.SetParent(root.transform, false);
        StretchFull(dim.GetComponent<RectTransform>());
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        dimImg.raycastTarget = true;
        dim.AddComponent<Button>().transition = Selectable.Transition.None;

        var toastHost = new GameObject("AchievementToastHost", typeof(RectTransform));
        toastHost.transform.SetParent(root.transform, false);
        StretchFull(toastHost.GetComponent<RectTransform>());
        toastHost.AddComponent<AchievementToastPresenter>();

        var toastPanel = new GameObject("ToastPanel", typeof(RectTransform));
        toastPanel.transform.SetParent(toastHost.transform, false);
        var toastRt = toastPanel.GetComponent<RectTransform>();
        toastRt.anchorMin = new Vector2(0.5f, 1f);
        toastRt.anchorMax = new Vector2(0.5f, 1f);
        toastRt.pivot = new Vector2(0.5f, 1f);
        toastRt.sizeDelta = new Vector2(560f, 120f);
        toastRt.anchoredPosition = new Vector2(0f, -96f);
        var toastCg = toastPanel.AddComponent<CanvasGroup>();
        toastCg.alpha = 0f;
        var toastBg = toastPanel.AddComponent<Image>();
        toastBg.color = new Color(0.08f, 0.11f, 0.18f, 0.96f);
        toastBg.raycastTarget = false;
        var toastTitle = MakeTmp(toastPanel.transform, "Title", "Достижение!", 22f, FontStyles.Bold);
        SetRect(toastTitle.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, -8f));
        var toastReward = MakeTmp(toastPanel.transform, "Reward", "Награда", 18f, FontStyles.Normal);
        SetRect(toastReward.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 4f));

        var sheet = new GameObject("AchievementsSheet", typeof(RectTransform));
        sheet.transform.SetParent(root.transform, false);
        var sheetRt = sheet.GetComponent<RectTransform>();
        sheetRt.anchorMin = new Vector2(0f, 0f);
        sheetRt.anchorMax = new Vector2(1f, 1f);
        sheetRt.offsetMin = new Vector2(40f, 48f);
        sheetRt.offsetMax = new Vector2(-40f, -48f);
        var sheetImg = sheet.AddComponent<Image>();
        sheetImg.color = new Color(0.06f, 0.07f, 0.11f, 0.98f);
        sheetImg.raycastTarget = true;

        var sheetVl = sheet.AddComponent<VerticalLayoutGroup>();
        sheetVl.padding = new RectOffset(16, 16, 16, 16);
        sheetVl.spacing = 10f;
        sheetVl.childAlignment = TextAnchor.UpperCenter;
        sheetVl.childControlHeight = true;
        sheetVl.childControlWidth = true;
        sheetVl.childForceExpandWidth = true;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(sheet.transform, false);
        var headerHl = header.AddComponent<HorizontalLayoutGroup>();
        headerHl.childAlignment = TextAnchor.MiddleCenter;
        headerHl.childForceExpandWidth = true;
        headerHl.spacing = 12f;
        var headerLe = header.AddComponent<LayoutElement>();
        headerLe.preferredHeight = 52f;

        var title = MakeTmp(header.transform, "Title", "Достижения", 28f, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var closeBtnGo = new GameObject("CloseButton", typeof(RectTransform));
        closeBtnGo.transform.SetParent(header.transform, false);
        var closeRt = closeBtnGo.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(120f, 44f);
        var closeImg = closeBtnGo.AddComponent<Image>();
        closeImg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        var closeBtn = closeBtnGo.AddComponent<Button>();
        closeBtn.transition = Selectable.Transition.ColorTint;
        var closeLbl = MakeTmp(closeBtnGo.transform, "Label", "X", 26f, FontStyles.Bold);
        StretchFull(closeLbl.rectTransform);
        closeLbl.alignment = TextAlignmentOptions.Center;

        var tabBar = new GameObject("TabBar", typeof(RectTransform));
        tabBar.transform.SetParent(sheet.transform, false);
        var tabHl = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabHl.spacing = 8f;
        tabHl.childAlignment = TextAnchor.MiddleCenter;
        tabHl.childForceExpandWidth = true;
        tabBar.AddComponent<LayoutElement>().preferredHeight = 48f;

        var tObs = MakeToggle(tabBar.transform, "TabObsession", "Одержимость", true);
        var tSla = MakeToggle(tabBar.transform, "TabSlaughter", "Бойня", false);
        var tDnn = MakeToggle(tabBar.transform, "TabDnn", "ДНН", false);

        var pages = new GameObject("Pages", typeof(RectTransform));
        pages.transform.SetParent(sheet.transform, false);
        StretchFull(pages.GetComponent<RectTransform>());
        var pagesLe = pages.AddComponent<LayoutElement>();
        pagesLe.flexibleHeight = 1f;
        pagesLe.minHeight = 400f;

        MakeScrollPage(pages.transform, "ScrollObsession", true);
        MakeScrollPage(pages.transform, "ScrollSlaughter", false);
        MakeScrollPage(pages.transform, "ScrollDnn", false);

        var rowTemplate = BuildChainRowTemplate(root.transform);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WirePrefabReferences(PrefabPath);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
            Selection.activeObject = prefab;
    }

    private static void WirePrefabReferences(string prefabPath)
    {
        var uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AchievementUiFontPath);

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var toastHost = root.transform.Find("AchievementToastHost");
            var presenter = toastHost != null ? toastHost.GetComponent<AchievementToastPresenter>() : null;
            if (presenter != null)
            {
                var panel = toastHost.Find("ToastPanel");
                var so = new SerializedObject(presenter);
                so.FindProperty("canvasGroup").objectReferenceValue = panel != null ? panel.GetComponent<CanvasGroup>() : null;
                so.FindProperty("panelRect").objectReferenceValue = panel != null ? panel.GetComponent<RectTransform>() : null;
                so.FindProperty("titleTmp").objectReferenceValue = panel != null ? panel.Find("Title")?.GetComponent<TextMeshProUGUI>() : null;
                so.FindProperty("rewardTmp").objectReferenceValue = panel != null ? panel.Find("Reward")?.GetComponent<TextMeshProUGUI>() : null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var ctrl = root.GetComponent<AchievementsPanelController>();
            if (ctrl != null)
            {
                var so = new SerializedObject(ctrl);
                so.FindProperty("achievementUiFont").objectReferenceValue = uiFont;
                so.FindProperty("sheetRect").objectReferenceValue = root.transform.Find("AchievementsSheet")?.GetComponent<RectTransform>();
                so.FindProperty("rootCanvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
                so.FindProperty("closeButton").objectReferenceValue = root.transform.Find("AchievementsSheet/Header/CloseButton")?.GetComponent<Button>();
                so.FindProperty("dimmerButton").objectReferenceValue = root.transform.Find("AchievementsDimmer")?.GetComponent<Button>();
                so.FindProperty("tabObsession").objectReferenceValue = root.transform.Find("AchievementsSheet/TabBar/TabObsession")?.GetComponent<Toggle>();
                so.FindProperty("tabSlaughter").objectReferenceValue = root.transform.Find("AchievementsSheet/TabBar/TabSlaughter")?.GetComponent<Toggle>();
                so.FindProperty("tabDnn").objectReferenceValue = root.transform.Find("AchievementsSheet/TabBar/TabDnn")?.GetComponent<Toggle>();
                so.FindProperty("contentObsession").objectReferenceValue =
                    root.transform.Find("AchievementsSheet/Pages/ScrollObsession/Viewport/Content")?.GetComponent<RectTransform>();
                so.FindProperty("contentSlaughter").objectReferenceValue =
                    root.transform.Find("AchievementsSheet/Pages/ScrollSlaughter/Viewport/Content")?.GetComponent<RectTransform>();
                so.FindProperty("contentDnn").objectReferenceValue =
                    root.transform.Find("AchievementsSheet/Pages/ScrollDnn/Viewport/Content")?.GetComponent<RectTransform>();
                var rowTr = root.transform.Find("AchievementChainRowPrefabTemplate");
                so.FindProperty("chainRowPrefab").objectReferenceValue =
                    rowTr != null ? rowTr.GetComponent<AchievementChainRowView>() : null;
                var iconCatProp = so.FindProperty("iconCatalog");
                if (iconCatProp != null && iconCatProp.objectReferenceValue == null)
                {
                    iconCatProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(
                        AchievementIconCatalog.MainCatalogAssetPath);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        finally
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject BuildChainRowTemplate(Transform rootParent)
    {
        var template = new GameObject("AchievementChainRowPrefabTemplate", typeof(RectTransform));
        template.transform.SetParent(rootParent, false);
        template.SetActive(false);

        var rowHl = template.AddComponent<HorizontalLayoutGroup>();
        rowHl.spacing = 8f;
        rowHl.childAlignment = TextAnchor.MiddleCenter;
        rowHl.childForceExpandHeight = true;
        rowHl.childForceExpandWidth = false;

        var rowView = template.AddComponent<AchievementChainRowView>();
        var slots = new AchievementChainSlotView[4];
        var arrows = new Graphic[3];

        for (var i = 0; i < 4; i++)
        {
            slots[i] = MakeSlot(template.transform, "Step" + i);
            if (i < 3)
                arrows[i] = MakeArrowGraphic(template.transform, "Arrow" + i);
        }

        var soRow = new SerializedObject(rowView);
        soRow.FindProperty("slots").arraySize = 4;
        for (var i = 0; i < 4; i++)
            soRow.FindProperty("slots").GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        soRow.FindProperty("arrows").arraySize = 3;
        for (var i = 0; i < 3; i++)
            soRow.FindProperty("arrows").GetArrayElementAtIndex(i).objectReferenceValue = arrows[i];
        soRow.ApplyModifiedPropertiesWithoutUndo();

        return template;
    }

    private static AchievementChainSlotView MakeSlot(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(148f, 210f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 148f;
        le.preferredHeight = 210f;

        var cg = go.AddComponent<CanvasGroup>();

        var frame = new GameObject("Frame", typeof(RectTransform));
        frame.transform.SetParent(go.transform, false);
        StretchFull(frame.GetComponent<RectTransform>());
        var frameImg = frame.AddComponent<Image>();
        frameImg.color = new Color(0.4f, 0.75f, 0.45f, 1f);

        var icon = new GameObject("Icon", typeof(RectTransform));
        icon.transform.SetParent(go.transform, false);
        SetRect(icon.GetComponent<RectTransform>(), new Vector2(0.1f, 0.38f), new Vector2(0.9f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var iconImg = icon.AddComponent<Image>();
        iconImg.color = Color.white;

        var reward = MakeTmp(go.transform, "Reward", "Награда", 13f, FontStyles.Normal);
        SetRect(reward.rectTransform, new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.66f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        reward.textWrappingMode = TextWrappingModes.Normal;
        reward.fontSizeMin = 10f;

        var sliderGo = new GameObject("ProgressSlider", typeof(RectTransform));
        sliderGo.transform.SetParent(go.transform, false);
        SetRect(sliderGo.GetComponent<RectTransform>(), new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var slider = sliderGo.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(sliderGo.transform, false);
        StretchFull(bg.GetComponent<RectTransform>());
        bg.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        StretchFull(fillArea.GetComponent<RectTransform>());
        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        StretchFull(fill.GetComponent<RectTransform>());
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.35f, 0.92f, 0.42f, 1f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = fillImg;

        var progressTmp = MakeTmp(go.transform, "ProgressText", "0/10", 14f, FontStyles.Bold);
        SetRect(progressTmp.rectTransform, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        progressTmp.alignment = TextAlignmentOptions.Center;

        var lockGo = new GameObject("LockOverlay", typeof(RectTransform));
        lockGo.transform.SetParent(go.transform, false);
        StretchFull(lockGo.GetComponent<RectTransform>());
        var lockImg = lockGo.AddComponent<Image>();
        lockImg.color = new Color(0f, 0f, 0f, 0.45f);
        lockImg.raycastTarget = false;
        lockGo.SetActive(false);

        var slot = go.AddComponent<AchievementChainSlotView>();
        var so = new SerializedObject(slot);
        so.FindProperty("frameImage").objectReferenceValue = frameImg;
        so.FindProperty("iconImage").objectReferenceValue = iconImg;
        so.FindProperty("fillImage").objectReferenceValue = fillImg;
        so.FindProperty("progressSlider").objectReferenceValue = slider;
        so.FindProperty("progressTmp").objectReferenceValue = progressTmp;
        so.FindProperty("rewardTmp").objectReferenceValue = reward;
        so.FindProperty("lockOverlay").objectReferenceValue = lockGo;
        so.ApplyModifiedPropertiesWithoutUndo();

        return slot;
    }

    private static Graphic MakeArrowGraphic(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(44f, 44f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 44f;
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = ">";
        txt.fontSize = 36f;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = new Color(1f, 0.82f, 0.18f, 1f);
        txt.raycastTarget = false;
        AssignTmpFontAndMaterial(txt);
        return txt;
    }

    private static void MakeScrollPage(Transform parent, string name, bool active)
    {
        var scrollGo = new GameObject(name, typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        StretchFull(scrollGo.GetComponent<RectTransform>());
        scrollGo.SetActive(active);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<RectMask2D>();
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.02f);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = new Vector2(0f, 800f);
        contentRt.anchoredPosition = Vector2.zero;

        var vl = content.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 14f;
        vl.padding = new RectOffset(6, 6, 6, 6);
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlHeight = true;
        vl.childControlWidth = true;
        vl.childForceExpandWidth = true;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
    }

    private static Toggle MakeToggle(Transform parent, string name, string label, bool isOn)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.2f, 0.26f, 1f);
        var toggle = go.AddComponent<Toggle>();
        toggle.isOn = isOn;
        toggle.transition = Selectable.Transition.ColorTint;

        var txt = MakeTmp(go.transform, "Label", label, 18f, FontStyles.Bold);
        StretchFull(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 42f;
        return toggle;
    }

    private static TextMeshProUGUI MakeTmp(Transform parent, string name, string text, float size, FontStyles fs)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = fs;
        tmp.color = Color.white;
        AssignTmpFontAndMaterial(tmp);
        return tmp;
    }

    private static void AssignTmpFontAndMaterial(TextMeshProUGUI tmp)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AchievementUiFontPath);
        if (fa == null)
            fa = TMP_Settings.defaultFontAsset;
        if (fa == null) return;
        tmp.font = fa;
        if (fa.material != null)
            tmp.fontSharedMaterial = fa.material;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPos)
    {
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;
    }

    private static void EnsureFolders()
    {
        var dir = Path.GetDirectoryName(PrefabPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
#endif
