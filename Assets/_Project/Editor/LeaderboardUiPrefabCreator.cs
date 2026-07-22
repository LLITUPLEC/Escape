#if UNITY_EDITOR
using System.IO;
using Project.Leaderboard;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools → Escape → Создать UI таблицы лидеров — генерирует префаб и опционально вставляет в MainMenuHudOverlay.
/// </summary>
public static class LeaderboardUiPrefabCreator
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/Leaderboard/LeaderboardPanel.prefab";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/MainMenu/MainMenuHudOverlay.prefab";
    private const string UiFontPath = "Assets/_Project/Fonts/TF2CSecondary SDF.asset";

    [MenuItem("Tools/Escape/Создать UI таблицы лидеров")]
    public static void CreatePrefabOnly()
    {
        BuildAndSavePrefab();
    }

    [MenuItem("Tools/Escape/Вставить LeaderboardPanel в MainMenuHudOverlay")]
    public static void MergeIntoHud()
    {
        BuildAndSavePrefab();
        var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (panelPrefab == null)
        {
            EditorUtility.DisplayDialog("Leaderboard", "Сначала создайте префаб.", "OK");
            return;
        }

        var hudRoot = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            RemoveAllLeaderboardRoots(hudRoot.transform);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, hudRoot.transform);
            inst.name = LeaderboardPanelController.PanelRootName;
            inst.transform.SetAsLastSibling();
            PrefabUtility.SaveAsPrefabAsset(hudRoot, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hudRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Leaderboard", "Готово: LeaderboardPanel добавлен в MainMenuHudOverlay.", "OK");
    }

    [MenuItem("Tools/Escape/Удалить дубликаты LeaderboardPanel в HUD")]
    public static void RemoveDuplicateLeaderboardPanelsInHud()
    {
        var hudRoot = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            var removed = RemoveDuplicateLeaderboardRoots(hudRoot.transform);
            if (removed == 0)
            {
                EditorUtility.DisplayDialog("Leaderboard", "Дубликатов не найдено — в HUD одна панель.", "OK");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(hudRoot, HudPrefabPath);
            EditorUtility.DisplayDialog("Leaderboard", $"Удалено дубликатов: {removed}.", "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hudRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void RemoveAllLeaderboardRoots(Transform hudRoot)
    {
        if (hudRoot == null)
            return;

        for (var i = hudRoot.childCount - 1; i >= 0; i--)
        {
            var child = hudRoot.GetChild(i);
            if (string.Equals(child.name, LeaderboardPanelController.PanelRootName, System.StringComparison.Ordinal))
                Object.DestroyImmediate(child.gameObject);
        }
    }

    private static int RemoveDuplicateLeaderboardRoots(Transform hudRoot)
    {
        if (hudRoot == null)
            return 0;

        var found = new System.Collections.Generic.List<GameObject>();
        for (var i = 0; i < hudRoot.childCount; i++)
        {
            var child = hudRoot.GetChild(i);
            if (string.Equals(child.name, LeaderboardPanelController.PanelRootName, System.StringComparison.Ordinal))
                found.Add(child.gameObject);
        }

        for (var i = 1; i < found.Count; i++)
            Object.DestroyImmediate(found[i]);

        return Mathf.Max(0, found.Count - 1);
    }

    private static void BuildAndSavePrefab()
    {
        if (EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog(
                "Leaderboard",
                "Дождитесь окончания компиляции скриптов и повторите команду меню.",
                "OK");
            return;
        }

        EnsureFolders();
        RemovePrefabAssetIfExists(PrefabPath);

        var root = new GameObject(LeaderboardPanelController.PanelRootName, typeof(RectTransform));
        StretchFull(root.GetComponent<RectTransform>());
        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        root.AddComponent<LeaderboardPanelController>();

        var dim = new GameObject("LeaderboardDimmer", typeof(RectTransform));
        dim.transform.SetParent(root.transform, false);
        StretchFull(dim.GetComponent<RectTransform>());
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.62f);
        dimImg.raycastTarget = true;
        dim.AddComponent<Button>().transition = Selectable.Transition.None;

        var sheet = BuildSheet(root.transform);
        var gold = BuildRowTemplate(root.transform, "LeaderboardPodiumRowGoldTemplate", LeaderboardRowStyle.Gold, 168f);
        var silver = BuildRowTemplate(root.transform, "LeaderboardPodiumRowSilverTemplate", LeaderboardRowStyle.Silver, 152f);
        var bronze = BuildRowTemplate(root.transform, "LeaderboardPodiumRowBronzeTemplate", LeaderboardRowStyle.Bronze, 144f);
        var standard = BuildRowTemplate(root.transform, "LeaderboardStandardRowTemplate", LeaderboardRowStyle.Standard, 92f);
        BuildFilterPicker(root.transform);

        WireRootReferences(root);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
            Selection.activeObject = prefab;
    }

    private static GameObject BuildSheet(Transform rootParent)
    {
        var sheet = new GameObject("LeaderboardSheet", typeof(RectTransform));
        sheet.transform.SetParent(rootParent, false);
        var sheetRt = sheet.GetComponent<RectTransform>();
        sheetRt.anchorMin = new Vector2(0f, 0f);
        sheetRt.anchorMax = new Vector2(1f, 1f);
        sheetRt.offsetMin = new Vector2(24f, 32f);
        sheetRt.offsetMax = new Vector2(-24f, -32f);
        var sheetImg = sheet.AddComponent<Image>();
        sheetImg.color = new Color(0.05f, 0.06f, 0.09f, 0.98f);
        sheetImg.raycastTarget = true;

        var sheetVl = sheet.AddComponent<VerticalLayoutGroup>();
        sheetVl.padding = new RectOffset(12, 12, 12, 12);
        sheetVl.spacing = 8f;
        sheetVl.childAlignment = TextAnchor.UpperCenter;
        sheetVl.childControlHeight = true;
        sheetVl.childControlWidth = true;
        sheetVl.childForceExpandWidth = true;

        BuildHeader(sheet.transform);
        BuildPeriodTabs(sheet.transform);
        BuildFilterBar(sheet.transform);
        BuildRewardsBar(sheet.transform);
        BuildListArea(sheet.transform);
        BuildStickyRow(sheet.transform);
        BuildStatusText(sheet.transform);

        return sheet;
    }

    private static void BuildHeader(Transform parent)
    {
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(parent, false);
        var hl = header.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandWidth = true;
        hl.spacing = 12f;
        header.AddComponent<LayoutElement>().preferredHeight = 48f;

        var title = MakeTmp(header.transform, "Title", "РЕЙТИНГ", 26f, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var closeBtnGo = new GameObject("CloseButton", typeof(RectTransform));
        closeBtnGo.transform.SetParent(header.transform, false);
        closeBtnGo.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 40f);
        var closeImg = closeBtnGo.AddComponent<Image>();
        closeImg.color = new Color(0.28f, 0.3f, 0.34f, 1f);
        closeBtnGo.AddComponent<Button>();
        var closeLbl = MakeTmp(closeBtnGo.transform, "Label", "X", 24f, FontStyles.Bold);
        StretchFull(closeLbl.rectTransform);
        closeLbl.alignment = TextAlignmentOptions.Center;
    }

    private static void BuildPeriodTabs(Transform parent)
    {
        var tabBar = new GameObject("PeriodTabs", typeof(RectTransform));
        tabBar.transform.SetParent(parent, false);
        var tabHl = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabHl.spacing = 6f;
        tabHl.childAlignment = TextAnchor.MiddleCenter;
        tabHl.childForceExpandWidth = true;
        tabBar.AddComponent<LayoutElement>().preferredHeight = 44f;

        MakeToggle(tabBar.transform, "TabDay", "ДЕНЬ", false);
        MakeToggle(tabBar.transform, "TabWeek", "НЕДЕЛЯ", true);
        MakeToggle(tabBar.transform, "TabMonth", "МЕСЯЦ", false);
        MakeToggle(tabBar.transform, "TabAllTime", "ВСЕ ВРЕМЯ", false);
    }

    private static void BuildFilterBar(Transform parent)
    {
        var bar = new GameObject("FilterBar", typeof(RectTransform));
        bar.transform.SetParent(parent, false);
        var hl = bar.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandWidth = true;
        bar.AddComponent<LayoutElement>().preferredHeight = 52f;

        BuildFilterButton(bar.transform, "TypeFilter", "TYPE", "Турнир");
        BuildFilterButton(bar.transform, "ViewFilter", "VIEW", "Турнир руды");
    }

    private static void BuildFilterButton(Transform parent, string name, string header, string value)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.11f, 0.14f, 0.98f);
        go.AddComponent<Button>();
        go.AddComponent<LeaderboardFilterButton>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 48f;

        var headerTmp = MakeTmp(go.transform, "HeaderLabel", header, 14f, FontStyles.Bold);
        SetRect(headerTmp.rectTransform, new Vector2(0.06f, 0.55f), new Vector2(0.94f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        headerTmp.color = new Color(0.55f, 0.58f, 0.62f, 1f);

        var valueTmp = MakeTmp(go.transform, "ValueLabel", value, 18f, FontStyles.Bold);
        SetRect(valueTmp.rectTransform, new Vector2(0.06f, 0.05f), new Vector2(0.84f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var chevron = MakeTmp(go.transform, "Chevron", "▼", 16f, FontStyles.Bold);
        SetRect(chevron.rectTransform, new Vector2(0.84f, 0.1f), new Vector2(0.96f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        chevron.alignment = TextAlignmentOptions.Center;
    }

    private static void BuildRewardsBar(Transform parent)
    {
        var bar = new GameObject("RewardsBar", typeof(RectTransform));
        bar.transform.SetParent(parent, false);
        var img = bar.AddComponent<Image>();
        img.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
        bar.AddComponent<LeaderboardRewardsBarView>();
        var le = bar.AddComponent<LayoutElement>();
        le.preferredHeight = 108f;

        var title = MakeTmp(bar.transform, "Title", "TOP 3 REWARDS", 16f, FontStyles.Bold);
        SetRect(title.rectTransform, new Vector2(0.02f, 0.72f), new Vector2(0.98f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        title.alignment = TextAlignmentOptions.Center;

        var tiersRoot = new GameObject("Tiers", typeof(RectTransform));
        tiersRoot.transform.SetParent(bar.transform, false);
        SetRect(tiersRoot.GetComponent<RectTransform>(), new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var hl = tiersRoot.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childForceExpandWidth = true;

        var tierViews = new LeaderboardRewardTierView[3];
        for (var i = 0; i < 3; i++)
            tierViews[i] = BuildRewardTier(tiersRoot.transform, "Tier" + (i + 1));

        var rewardsView = bar.GetComponent<LeaderboardRewardsBarView>();
        var so = new SerializedObject(rewardsView);
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("tiers").arraySize = 3;
        for (var i = 0; i < 3; i++)
            so.FindProperty("tiers").GetArrayElementAtIndex(i).objectReferenceValue = tierViews[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static LeaderboardRewardTierView BuildRewardTier(Transform parent, string name)
    {
        var tier = new GameObject(name, typeof(RectTransform));
        tier.transform.SetParent(parent, false);
        tier.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var view = tier.AddComponent<LeaderboardRewardTierView>();

        var medal = new GameObject("Medal", typeof(RectTransform));
        medal.transform.SetParent(tier.transform, false);
        SetRect(medal.GetComponent<RectTransform>(), new Vector2(0.1f, 0.45f), new Vector2(0.9f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var medalImg = medal.AddComponent<Image>();
        medalImg.color = Color.white;

        var lines = new TMP_Text[3];
        for (var i = 0; i < 3; i++)
        {
            var line = MakeTmp(tier.transform, "RewardLine" + i, "—", 13f, FontStyles.Normal);
            SetRect(line.rectTransform, new Vector2(0.05f, 0.28f - i * 0.12f), new Vector2(0.95f, 0.4f - i * 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            line.alignment = TextAlignmentOptions.Center;
            lines[i] = line;
        }

        var so = new SerializedObject(view);
        so.FindProperty("medalImage").objectReferenceValue = medalImg;
        so.FindProperty("rewardLines").arraySize = 3;
        for (var i = 0; i < 3; i++)
            so.FindProperty("rewardLines").GetArrayElementAtIndex(i).objectReferenceValue = lines[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    private static void BuildListArea(Transform parent)
    {
        var area = new GameObject("ListArea", typeof(RectTransform));
        area.transform.SetParent(parent, false);
        var le = area.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 420f;

        var scrollGo = new GameObject("ScrollList", typeof(RectTransform));
        scrollGo.transform.SetParent(area.transform, false);
        StretchFull(scrollGo.GetComponent<RectTransform>());
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<RectMask2D>();
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = new Vector2(0f, 800f);
        contentRt.anchoredPosition = Vector2.zero;

        var vl = content.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f;
        vl.padding = new RectOffset(4, 4, 4, 4);
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlHeight = true;
        vl.childControlWidth = true;
        vl.childForceExpandWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
    }

    private static void BuildStickyRow(Transform parent)
    {
        var sticky = BuildRowTemplate(parent, "StickyPlayerRow", LeaderboardRowStyle.Sticky, 124f, active: true);
        sticky.GetComponent<LayoutElement>().preferredHeight = 124f;
    }

    private static void BuildStatusText(Transform parent)
    {
        var status = MakeTmp(parent, "StatusText", "", 16f, FontStyles.Italic);
        status.alignment = TextAlignmentOptions.Center;
        status.color = new Color(1f, 0.55f, 0.45f, 1f);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        status.gameObject.SetActive(false);
    }

    private static GameObject BuildFilterPicker(Transform rootParent)
    {
        var pickerRoot = new GameObject("LeaderboardFilterPicker", typeof(RectTransform));
        pickerRoot.transform.SetParent(rootParent, false);
        StretchFull(pickerRoot.GetComponent<RectTransform>());
        var cg = pickerRoot.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        var modal = pickerRoot.AddComponent<LeaderboardFilterPickerModal>();

        var dim = new GameObject("Dimmer", typeof(RectTransform));
        dim.transform.SetParent(pickerRoot.transform, false);
        StretchFull(dim.GetComponent<RectTransform>());
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(pickerRoot.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(520f, 520f);
        panel.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 0.98f);

        var list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(panel.transform, false);
        StretchFull(list.GetComponent<RectTransform>());
        var vl = list.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 4f;
        vl.padding = new RectOffset(10, 10, 10, 10);
        vl.childControlHeight = true;
        vl.childControlWidth = true;
        vl.childForceExpandWidth = true;
        list.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var optionPrefab = new GameObject("OptionButtonTemplate", typeof(RectTransform));
        optionPrefab.transform.SetParent(pickerRoot.transform, false);
        optionPrefab.SetActive(false);
        var optImg = optionPrefab.AddComponent<Image>();
        optImg.color = new Color(0.1f, 0.11f, 0.14f, 0.96f);
        var optBtn = optionPrefab.AddComponent<Button>();
        optBtn.transition = Selectable.Transition.ColorTint;
        optionPrefab.AddComponent<LayoutElement>().preferredHeight = 44f;
        var optLbl = MakeTmp(optionPrefab.transform, "Label", "Option", 18f, FontStyles.Bold);
        StretchFull(optLbl.rectTransform);
        optLbl.alignment = TextAlignmentOptions.Center;

        var so = new SerializedObject(modal);
        so.FindProperty("canvasGroup").objectReferenceValue = cg;
        so.FindProperty("dimmerButton").objectReferenceValue = dimBtn;
        so.FindProperty("listRoot").objectReferenceValue = list.GetComponent<RectTransform>();
        so.FindProperty("optionButtonPrefab").objectReferenceValue = optBtn;
        so.ApplyModifiedPropertiesWithoutUndo();

        return pickerRoot;
    }

    private static GameObject BuildRowTemplate(
        Transform parent,
        string name,
        LeaderboardRowStyle style,
        float height,
        bool active = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.SetActive(active);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        StretchFull(bg.GetComponent<RectTransform>());
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.08f, 0.09f, 0.11f, 0.88f);
        bgImg.raycastTarget = false;

        var glow = new GameObject("FrameGlow", typeof(RectTransform));
        glow.transform.SetParent(go.transform, false);
        StretchFull(glow.GetComponent<RectTransform>());
        var glowImg = glow.AddComponent<Image>();
        glowImg.color = new Color(1f, 0.84f, 0f, 0.35f);
        glowImg.raycastTarget = false;
        glowImg.enabled = style != LeaderboardRowStyle.Standard;

        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 6, 6);
        hl.spacing = 8f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;

        var yourRankGroup = new GameObject("YourRankGroup", typeof(RectTransform));
        yourRankGroup.transform.SetParent(go.transform, false);
        yourRankGroup.AddComponent<LayoutElement>().preferredWidth = style == LeaderboardRowStyle.Sticky ? 108f : 0f;
        var yourRankLbl = MakeTmp(yourRankGroup.transform, "YourRankLabel", "YOUR RANK", 12f, FontStyles.Bold);
        SetRect(yourRankLbl.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        yourRankLbl.alignment = TextAlignmentOptions.Center;
        yourRankGroup.SetActive(style == LeaderboardRowStyle.Sticky);

        TextMeshProUGUI rankTmp;
        if (style == LeaderboardRowStyle.Sticky)
        {
            rankTmp = MakeTmp(yourRankGroup.transform, "Rank", "1", 48f, FontStyles.Bold);
            SetRect(rankTmp.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rankTmp.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            rankTmp = MakeTmp(go.transform, "Rank", "1", style == LeaderboardRowStyle.Standard ? 28f : 48f, FontStyles.Bold);
            rankTmp.gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;
            rankTmp.alignment = TextAlignmentOptions.Midline;
        }

        var deltaGroup = new GameObject("DeltaGroup", typeof(RectTransform));
        var deltaIconGo = new GameObject("DeltaIcon", typeof(RectTransform));
        deltaIconGo.transform.SetParent(deltaGroup.transform, false);
        SetRect(deltaIconGo.GetComponent<RectTransform>(), new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var deltaIcon = deltaIconGo.AddComponent<Image>();
        deltaIcon.color = new Color(0.3f, 0.8f, 0.32f, 1f);
        var deltaTxt = MakeTmp(deltaGroup.transform, "DeltaText", "▲2", 14f, FontStyles.Bold);
        StretchFull(deltaTxt.rectTransform);
        deltaTxt.alignment = TextAlignmentOptions.Center;
        var newBadge = MakeTmp(deltaGroup.transform, "NewBadge", "NEW", 11f, FontStyles.Bold);
        StretchFull(newBadge.rectTransform);
        newBadge.alignment = TextAlignmentOptions.Center;
        newBadge.color = new Color(0.35f, 0.95f, 0.45f, 1f);
        newBadge.gameObject.SetActive(false);

        var avatarFrameGo = new GameObject("AvatarFrame", typeof(RectTransform));
        avatarFrameGo.transform.SetParent(go.transform, false);
        var avatarLe = avatarFrameGo.AddComponent<LayoutElement>();
        avatarLe.preferredWidth = style == LeaderboardRowStyle.Standard ? 56f : 72f;
        avatarLe.preferredHeight = style == LeaderboardRowStyle.Standard ? 56f : 72f;
        var frameImg = avatarFrameGo.AddComponent<Image>();
        frameImg.color = Color.white;
        var avatarGo = new GameObject("Avatar", typeof(RectTransform));
        avatarGo.transform.SetParent(avatarFrameGo.transform, false);
        SetRect(avatarGo.GetComponent<RectTransform>(), new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var avatarImg = avatarGo.AddComponent<Image>();
        avatarImg.color = new Color(0.4f, 0.45f, 0.5f, 1f);

        var infoCol = new GameObject("Info", typeof(RectTransform));
        infoCol.transform.SetParent(go.transform, false);
        infoCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var nick = MakeTmp(infoCol.transform, "Nickname", "Player", style == LeaderboardRowStyle.Standard ? 20f : 24f, FontStyles.Bold);
        SetRect(nick.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        var secondaryGroup = new GameObject("SecondaryStat", typeof(RectTransform));
        secondaryGroup.transform.SetParent(infoCol.transform, false);
        SetRect(secondaryGroup.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0.45f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        var secondaryTxt = MakeTmp(secondaryGroup.transform, "Value", "0", 14f, FontStyles.Normal);
        StretchFull(secondaryTxt.rectTransform);

        var scoreGroup = new GameObject("ScoreGroup", typeof(RectTransform));
        scoreGroup.transform.SetParent(go.transform, false);
        scoreGroup.AddComponent<LayoutElement>().preferredWidth = style == LeaderboardRowStyle.Standard ? 150f : 190f;
        var trophyGo = new GameObject("Trophy", typeof(RectTransform));
        trophyGo.transform.SetParent(scoreGroup.transform, false);
        SetRect(trophyGo.GetComponent<RectTransform>(), new Vector2(0f, 0.2f), new Vector2(0.22f, 0.8f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var trophyImg = trophyGo.AddComponent<Image>();
        trophyImg.color = Color.white;
        var scoreTmp = MakeTmp(scoreGroup.transform, "Score", "0", style == LeaderboardRowStyle.Standard ? 22f : 28f, FontStyles.Bold);
        SetRect(scoreTmp.rectTransform, new Vector2(0.2f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        scoreTmp.alignment = TextAlignmentOptions.MidlineRight;

        deltaGroup.transform.SetParent(go.transform, false);
        deltaGroup.AddComponent<LayoutElement>().preferredWidth = 52f;
        deltaGroup.SetActive(style == LeaderboardRowStyle.Standard);

        var rowView = go.AddComponent<LeaderboardRowView>();
        var so = new SerializedObject(rowView);
        so.FindProperty("rowStyle").enumValueIndex = (int)style;
        so.FindProperty("rowBackground").objectReferenceValue = bgImg;
        so.FindProperty("frameGlow").objectReferenceValue = glowImg;
        so.FindProperty("yourRankGroup").objectReferenceValue = yourRankGroup;
        so.FindProperty("yourRankLabel").objectReferenceValue = yourRankLbl;
        so.FindProperty("rankText").objectReferenceValue = rankTmp;
        so.FindProperty("deltaGroup").objectReferenceValue = deltaGroup;
        so.FindProperty("deltaIcon").objectReferenceValue = deltaIcon;
        so.FindProperty("deltaText").objectReferenceValue = deltaTxt;
        so.FindProperty("newBadge").objectReferenceValue = newBadge.gameObject;
        so.FindProperty("avatarImage").objectReferenceValue = avatarImg;
        so.FindProperty("avatarFrame").objectReferenceValue = frameImg;
        so.FindProperty("nicknameText").objectReferenceValue = nick;
        so.FindProperty("secondaryStatGroup").objectReferenceValue = secondaryGroup;
        so.FindProperty("secondaryStatText").objectReferenceValue = secondaryTxt;
        so.FindProperty("trophyIcon").objectReferenceValue = trophyImg;
        so.FindProperty("scoreText").objectReferenceValue = scoreTmp;
        so.ApplyModifiedPropertiesWithoutUndo();

        return go;
    }

    private static void WireRootReferences(GameObject root)
    {
        var uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontPath);
        var ctrl = root.GetComponent<LeaderboardPanelController>();
        if (ctrl != null)
        {
            var so = new SerializedObject(ctrl);
            so.FindProperty("uiFont").objectReferenceValue = uiFont;
            so.FindProperty("sheetRect").objectReferenceValue = root.transform.Find("LeaderboardSheet")?.GetComponent<RectTransform>();
            so.FindProperty("rootCanvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("closeButton").objectReferenceValue = root.transform.Find("LeaderboardSheet/Header/CloseButton")?.GetComponent<Button>();
            so.FindProperty("dimmerButton").objectReferenceValue = root.transform.Find("LeaderboardDimmer")?.GetComponent<Button>();
            so.FindProperty("tabDay").objectReferenceValue = root.transform.Find("LeaderboardSheet/PeriodTabs/TabDay")?.GetComponent<Toggle>();
            so.FindProperty("tabWeek").objectReferenceValue = root.transform.Find("LeaderboardSheet/PeriodTabs/TabWeek")?.GetComponent<Toggle>();
            so.FindProperty("tabMonth").objectReferenceValue = root.transform.Find("LeaderboardSheet/PeriodTabs/TabMonth")?.GetComponent<Toggle>();
            so.FindProperty("tabAllTime").objectReferenceValue = root.transform.Find("LeaderboardSheet/PeriodTabs/TabAllTime")?.GetComponent<Toggle>();
            so.FindProperty("typeFilterButton").objectReferenceValue = root.transform.Find("LeaderboardSheet/FilterBar/TypeFilter")?.GetComponent<LeaderboardFilterButton>();
            so.FindProperty("viewFilterButton").objectReferenceValue = root.transform.Find("LeaderboardSheet/FilterBar/ViewFilter")?.GetComponent<LeaderboardFilterButton>();
            so.FindProperty("filterPicker").objectReferenceValue = root.transform.Find("LeaderboardFilterPicker")?.GetComponent<LeaderboardFilterPickerModal>();
            so.FindProperty("rewardsBar").objectReferenceValue = root.transform.Find("LeaderboardSheet/RewardsBar")?.GetComponent<LeaderboardRewardsBarView>();
            so.FindProperty("scrollContent").objectReferenceValue = root.transform.Find("LeaderboardSheet/ListArea/ScrollList/Viewport/Content")?.GetComponent<RectTransform>();
            so.FindProperty("scrollRect").objectReferenceValue = root.transform.Find("LeaderboardSheet/ListArea/ScrollList")?.GetComponent<ScrollRect>();
            so.FindProperty("stickyPlayerRow").objectReferenceValue = root.transform.Find("LeaderboardSheet/StickyPlayerRow")?.GetComponent<LeaderboardRowView>();
            so.FindProperty("statusText").objectReferenceValue = root.transform.Find("LeaderboardSheet/StatusText")?.GetComponent<TMP_Text>();
            so.FindProperty("podiumGoldPrefab").objectReferenceValue = root.transform.Find("LeaderboardPodiumRowGoldTemplate")?.GetComponent<LeaderboardRowView>();
            so.FindProperty("podiumSilverPrefab").objectReferenceValue = root.transform.Find("LeaderboardPodiumRowSilverTemplate")?.GetComponent<LeaderboardRowView>();
            so.FindProperty("podiumBronzePrefab").objectReferenceValue = root.transform.Find("LeaderboardPodiumRowBronzeTemplate")?.GetComponent<LeaderboardRowView>();
            so.FindProperty("standardRowPrefab").objectReferenceValue = root.transform.Find("LeaderboardStandardRowTemplate")?.GetComponent<LeaderboardRowView>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        WireFilterButton(root.transform.Find("LeaderboardSheet/FilterBar/TypeFilter"));
        WireFilterButton(root.transform.Find("LeaderboardSheet/FilterBar/ViewFilter"));

        var picker = root.transform.Find("LeaderboardFilterPicker")?.GetComponent<LeaderboardFilterPickerModal>();
        if (picker != null)
        {
            var pso = new SerializedObject(picker);
            pso.FindProperty("uiFont").objectReferenceValue = uiFont;
            pso.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void RemovePrefabAssetIfExists(string assetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
    }

    private static void WireFilterButton(Transform tr)
    {
        if (tr == null) return;
        var btn = tr.GetComponent<LeaderboardFilterButton>();
        if (btn == null) return;
        var so = new SerializedObject(btn);
        so.FindProperty("button").objectReferenceValue = tr.GetComponent<Button>();
        so.FindProperty("labelText").objectReferenceValue = tr.Find("HeaderLabel")?.GetComponent<TMP_Text>();
        so.FindProperty("valueText").objectReferenceValue = tr.Find("ValueLabel")?.GetComponent<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Toggle MakeToggle(Transform parent, string name, string label, bool isOn)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
        var toggle = go.AddComponent<Toggle>();
        toggle.isOn = isOn;
        toggle.transition = Selectable.Transition.ColorTint;
        go.AddComponent<UiNeonPulseOutline>();

        var txt = MakeTmp(go.transform, "Label", label, 15f, FontStyles.Bold);
        StretchFull(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredHeight = 40f;
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
        AssignFont(tmp);
        return tmp;
    }

    private static void AssignFont(TextMeshProUGUI tmp)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontPath);
        if (fa == null) fa = TMP_Settings.defaultFontAsset;
        if (fa == null) return;
        tmp.font = fa;
        if (fa.material != null) tmp.fontSharedMaterial = fa.material;
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

    /// <summary>Unity batch: -executeMethod LeaderboardUiPrefabCreator.BuildLeaderboardBatch</summary>
    public static void BuildLeaderboardBatch()
    {
        RemoveDuplicateLeaderboardPanelsInHud();
        MergeIntoHud();
    }
}
#endif
