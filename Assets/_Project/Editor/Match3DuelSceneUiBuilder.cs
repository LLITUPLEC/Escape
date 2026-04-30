#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Project.Character;
using Project.Match3;
using Project.UI;

/// <summary>
/// Menu: Tools → Match3 → Собрать UI в сцене DuelMatch3.
/// Builds the same hierarchy as <see cref="DuelMatch3Manager"/> runtime UI under DuelMatch3Manager/Canvas
/// so you can edit layout in the scene. At play time the manager detects this tree and binds references.
/// </summary>
public static class Match3DuelSceneUiBuilder
{
    private const string ScenePath = "Assets/_Project/Scenes/DuelMatch3.unity";
    private const string PrefabDir = "Assets/_Project/Prefabs/Match3";

    private static Vector2 V2(float x, float y) => new Vector2(x, y);

    [MenuItem("Tools/Match3/Собрать UI в сцене DuelMatch3")]
    public static void BuildDuelMatch3SceneUi()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"[Match3] Сцена не найдена: {ScenePath}");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
        if (manager == null)
        {
            Debug.LogError("[Match3] DuelMatch3Manager не найден в сцене DuelMatch3.");
            return;
        }

        EnsureDefaultPrefabsOnManager(manager);

        var mgrTr = manager.transform;
        var oldCanvas = mgrTr.Find("Canvas");
        if (oldCanvas != null)
            UnityEngine.Object.DestroyImmediate(oldCanvas.gameObject);

        var so = new SerializedObject(manager);
        var myPf = so.FindProperty("myPanelPrefab").objectReferenceValue as Match3PlayerPanel;
        var opPf = so.FindProperty("opPanelPrefab").objectReferenceValue as Match3PlayerPanel;
        var abilityPf = so.FindProperty("abilityPanelPrefab").objectReferenceValue as Match3AbilityPanel;
        var boardPf = so.FindProperty("boardViewPrefab").objectReferenceValue as Match3BoardView;
        var hudPf = so.FindProperty("hudPrefab").objectReferenceValue as Match3GameHUD;
        var searchPf = so.FindProperty("searchingPanelPrefab").objectReferenceValue as Match3SearchingPanel;
        var goPf = so.FindProperty("gameOverPanelPrefab").objectReferenceValue as Match3GameOverPanel;

        var cvGo = new GameObject("Canvas");
        cvGo.transform.SetParent(mgrTr, false);
        var canvas = cvGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.planeDistance = 10f;
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("MainCamera");
            if (tagged != null) mainCam = tagged.GetComponent<Camera>();
        }
        if (mainCam == null)
            mainCam = UnityEngine.Object.FindFirstObjectByType<Camera>();
        canvas.worldCamera = mainCam;

        var scaler = cvGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        cvGo.AddComponent<GraphicRaycaster>();

        var root = cvGo.transform;

        MakeImg(root, "Bg", new Color(0.08f, 0.08f, 0.15f), V2(0, 0), V2(1, 1));

        var leftTr = MakePanel(root, "LeftCol", Color.clear, V2(0f, 0f), V2(0.26f, 1f));
        InstantiateOrFail(myPf, leftTr, V2(0f, 0.27f), V2(1f, 1f), "Match3PlayerPanel (my)");
        InstantiateOrFail(abilityPf, leftTr, V2(0f, 0f), V2(1f, 0.26f), "Match3AbilityPanel");

        var boardColTr = MakePanel(root, "BoardCol", Color.clear, V2(0.26f, 0f), V2(0.74f, 1f));
        InstantiateOrFail(hudPf, boardColTr, V2(0.02f, 0.90f), V2(0.98f, 0.99f), "Match3GameHUD");
        InstantiateOrFail(boardPf, boardColTr, V2(0.04f, 0.04f), V2(0.96f, 0.89f), "Match3BoardView");

        var rightTr = MakePanel(root, "RightCol", Color.clear, V2(0.74f, 0f), V2(1f, 1f));
        InstantiateOrFail(opPf, rightTr, V2(0f, 0.27f), V2(1f, 1f), "Match3PlayerPanel (op)");

        EnsureCombatStatsFramesOnPlayerPanels(root);

        MakeButton(root, "QuitBtn", "← Выйти",
            new Color(0.42f, 0.12f, 0.12f), Color.white,
            V2(0.75f, 0f), V2(1f, 0.07f));

        InstantiateOrFail(searchPf, root, V2(0f, 0f), V2(1f, 1f), "Match3SearchingPanel");
        InstantiateOrFail(goPf, root, V2(0.22f, 0.24f), V2(0.78f, 0.76f), "Match3GameOverPanel");

        TryDeactivateDirectChildNamed(root, "Match3SearchingPanel");
        TryDeactivateDirectChildNamed(root, "Match3GameOverPanel");

        EnsureEventSystemInScene();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = cvGo;
        Debug.Log("[Match3] UI собран в DuelMatch3.unity под DuelMatch3Manager/Canvas. Сохраните сцену при необходимости.");
    }

    private static void EnsureDefaultPrefabsOnManager(DuelMatch3Manager manager)
    {
        var so = new SerializedObject(manager);
        void Set<T>(string propName, string path) where T : UnityEngine.Object
        {
            var p = so.FindProperty(propName);
            if (p == null) return;
            if (p.objectReferenceValue != null) return;
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) p.objectReferenceValue = a;
        }

        Set<Match3PlayerPanel>("myPanelPrefab", $"{PrefabDir}/Match3PlayerPanel.prefab");
        Set<Match3PlayerPanel>("opPanelPrefab", $"{PrefabDir}/Match3PlayerPanel.prefab");
        Set<Match3AbilityPanel>("abilityPanelPrefab", $"{PrefabDir}/Match3AbilityPanel.prefab");
        Set<Match3BoardView>("boardViewPrefab", $"{PrefabDir}/Match3BoardView.prefab");
        Set<Match3GameHUD>("hudPrefab", "Assets/_Project/Resources/UI/Match3GameHUD.prefab");
        Set<Match3SearchingPanel>("searchingPanelPrefab", $"{PrefabDir}/Match3SearchingPanel.prefab");
        Set<Match3GameOverPanel>("gameOverPanelPrefab", $"{PrefabDir}/Match3GameOverPanel.prefab");
        Set<ItemCatalog>("itemCatalog", MineRewardFormat.MainItemCatalogAssetPath);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// «Собрать UI» подставляет префабы как есть — если в Match3PlayerPanel.prefab ещё нет блока боевой статистики,
    /// добавляем тот же узел, что и <see cref="DuelMatch3Manager.BuildCombatStatsFrame"/>, и прописываем ссылки в инспекторе.
    /// </summary>
    private static void EnsureCombatStatsFramesOnPlayerPanels(Transform canvasRoot)
    {
        foreach (var panel in canvasRoot.GetComponentsInChildren<Match3PlayerPanel>(true))
        {
            if (panel.transform.Find("CombatStatsFrame") != null)
                continue;

            var frame = MakePanel(panel.transform, "CombatStatsFrame", new Color(0.07f, 0.08f, 0.15f, 0.72f),
                V2(0.05f, 0.20f), V2(0.95f, 0.40f));
            var outline = frame.gameObject.GetComponent<Outline>();
            if (outline == null) outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.85f, 0.95f, 0.35f);
            outline.effectDistance = new Vector2(1f, -1f);

            var csName = MakeTxt(frame, "CombatStatsName",
                "Урон:\nБроня:\nЛечение:\nКрит:", 18, Color.white, V2(0.06f, 0.10f), V2(0.50f, 0.92f));
            csName.alignment = TextAlignmentOptions.TopLeft;

            var csVal = MakeTxt(frame, "CombatStatsValue", "0\n0\n0\n0%", 18, Color.white, V2(0.52f, 0.10f), V2(0.94f, 0.92f));
            csVal.alignment = TextAlignmentOptions.TopRight;

            var buffText = MakeTxt(frame, "BuffStateText", string.Empty, 11, new Color(0.62f, 0.86f, 1f),
                V2(0.45f, 0.76f), V2(0.95f, 0.98f));
            buffText.alignment = TextAlignmentOptions.Right;

            var so = new SerializedObject(panel);
            var pCsN = so.FindProperty("combatStatsName");
            var pCsV = so.FindProperty("combatStatsValue");
            var pBf = so.FindProperty("buffStateText");
            if (pCsN != null) pCsN.objectReferenceValue = csName;
            if (pCsV != null) pCsV.objectReferenceValue = csVal;
            if (pBf != null) pBf.objectReferenceValue = buffText;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
        }
    }

    /// <summary>Handles <c>Name</c> and <c>Name (Clone)</c> after <see cref="PrefabUtility.InstantiatePrefab"/>.</summary>
    private static void TryDeactivateDirectChildNamed(Transform root, string objectName)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            var n = c.name;
            if (n == objectName || n.StartsWith(objectName + " (", StringComparison.Ordinal))
            {
                c.gameObject.SetActive(false);
                return;
            }
        }
    }

    private static void InstantiateOrFail<T>(T prefab, Transform parent, Vector2 aMin, Vector2 aMax, string label)
        where T : MonoBehaviour
    {
        if (prefab == null)
        {
            Debug.LogError($"[Match3] Префаб не задан: {label}. Запустите Tools → Match3 → Создать префабы UI.");
            return;
        }

        var go = PrefabUtility.InstantiatePrefab(prefab.gameObject, parent) as GameObject;
        if (go == null)
        {
            Debug.LogError($"[Match3] Не удалось создать экземпляр: {label}");
            return;
        }

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    private static void EnsureEventSystemInScene()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        esGo.AddComponent<StandaloneInputModule>();
#endif
    }

    /// <summary>Same structure as <see cref="DuelMatch3Manager.BuildPveSelector"/>.</summary>
    private static void BuildPveSelectorBlock(Transform root)
    {
        var pve = MakePanel(root, "PveSelector", new Color(0f, 0f, 0f, 0.92f), V2(0f, 0f), V2(1f, 1f));
        var card = MakePanel(pve, "Card", new Color(0.08f, 0.10f, 0.18f, 0.98f), V2(0.22f, 0.18f), V2(0.78f, 0.82f));
        var title = MakeTxt(card, "Title", "Выбор босса", 28, new Color(0.8f, 0.95f, 1f), V2(0.05f, 0.84f), V2(0.95f, 0.96f));
        title.alignment = TextAlignmentOptions.Center;
        var bossName = MakeTxt(card, "BossName", "Босс", 22, new Color(1f, 0.9f, 0.45f), V2(0.08f, 0.62f), V2(0.92f, 0.74f));
        bossName.alignment = TextAlignmentOptions.Center;
        var bossStats = MakeTxt(card, "BossStats", "—", 16, Color.white, V2(0.10f, 0.26f), V2(0.90f, 0.62f));
        bossStats.alignment = TextAlignmentOptions.TopLeft;

        MakeButton(card, "PrevBoss", "←", new Color(0.18f, 0.28f, 0.55f), Color.white, V2(0.10f, 0.08f), V2(0.28f, 0.18f));
        MakeButton(card, "NextBoss", "→", new Color(0.18f, 0.28f, 0.55f), Color.white, V2(0.30f, 0.08f), V2(0.48f, 0.18f));
        MakeButton(card, "StartBoss", "В бой", new Color(0.14f, 0.42f, 0.18f), Color.white, V2(0.52f, 0.08f), V2(0.90f, 0.18f));

        var toast = MakePanel(card, "ErrorToast", new Color(0.16f, 0.02f, 0.03f, 0.94f), V2(0.08f, 0.02f), V2(0.92f, 0.16f));
        if (toast.gameObject.GetComponent<CanvasGroup>() == null)
            toast.gameObject.AddComponent<CanvasGroup>();
        var cg = toast.gameObject.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        var toastTxt = MakeTxt(toast, "Text", string.Empty, 15, Color.white, V2(0.04f, 0.10f), V2(0.96f, 0.90f));
        toastTxt.alignment = TextAlignmentOptions.Center;

        pve.gameObject.SetActive(false);
    }

    private static RectTransform MakePanel(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (color.a > 0.001f)
            go.AddComponent<Image>().color = color;
        return rt;
    }

    private static RectTransform MakeImg(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax)
    {
        var rt = MakePanel(parent, name, Color.clear, aMin, aMax);
        rt.gameObject.AddComponent<Image>().color = color;
        return rt;
    }

    private static TMP_Text MakeTxt(Transform parent, string name, string text, int size, Color color,
        Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.font = TMP_Settings.defaultFontAsset;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        return t;
    }

    private static Button MakeButton(Transform parent, string name, string label, Color bgColor, Color textColor,
        Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.15f);
        cb.pressedColor = Color.Lerp(bgColor, Color.black, 0.18f);
        cb.disabledColor = new Color(bgColor.r * 0.45f, bgColor.g * 0.45f, bgColor.b * 0.45f, 0.7f);
        btn.colors = cb;
        if (!string.IsNullOrEmpty(label))
        {
            var lbl = MakeTxt(go.transform, "Lbl", label, 15, textColor, V2(0.04f, 0), V2(0.96f, 1));
            lbl.alignment = TextAlignmentOptions.Center;
        }

        return btn;
    }
}
#endif
