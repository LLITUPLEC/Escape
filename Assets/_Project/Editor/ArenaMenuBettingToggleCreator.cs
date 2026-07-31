#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Project.UI;

/// <summary>
/// Menu: Tools → Arena → Добавить BettingToggle в ArenaMenuWorld
/// Встраивает switch-toggle в Background2D (правый верхний угол) в префаб сцены арены.
/// </summary>
public static class ArenaMenuBettingToggleCreator
{
    private const string PrefabPath = "Assets/_Project/Prefabs/MainMenu/ArenaMenuWorld.prefab";
    private const string ToggleName = "BettingToggle";

    [MenuItem("Tools/Arena/Добавить BettingToggle в ArenaMenuWorld")]
    public static void EmbedToggle()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var bg = root.transform.Find("Background2D");
            if (bg == null)
            {
                Debug.LogError("[ArenaMenuBettingToggle] Background2D не найден в " + PrefabPath);
                return;
            }

            var existing = bg.Find(ToggleName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            BuildToggle(bg);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ArenaMenuBettingToggle] BettingToggle добавлен в Background2D (" + PrefabPath + ")");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Unity batch: -executeMethod ArenaMenuBettingToggleCreator.EmbedToggleBatch</summary>
    public static void EmbedToggleBatch() => EmbedToggle();

    private static void BuildToggle(Transform background2D)
    {
        var go = new GameObject(ToggleName, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(background2D, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(150f, 70f);
        rt.anchoredPosition = new Vector2(-95f, -70f);

        var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.layer = 5;
        bgGo.transform.SetParent(go.transform, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var white = ModalPanelCloseButton.WhiteSprite();
        var bgImg = bgGo.GetComponent<Image>();
        bgImg.sprite = white;
        bgImg.color = new Color(0.45f, 0.45f, 0.48f, 1f);
        bgImg.raycastTarget = true;

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handleGo.layer = 5;
        handleGo.transform.SetParent(go.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0.5f, 0.5f);
        handleRt.anchorMax = new Vector2(0.5f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(60f, 60f);
        handleRt.anchoredPosition = new Vector2(-40f, 0f);
        var handleImg = handleGo.GetComponent<Image>();
        handleImg.sprite = white;
        handleImg.color = Color.white;
        handleImg.raycastTarget = false;

        var toggle = go.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        toggle.targetGraphic = bgImg;
        toggle.graphic = null;
        toggle.isOn = false;

        var visual = go.AddComponent<SwitchToggleVisual>();
        var so = new SerializedObject(visual);
        so.FindProperty("toggle").objectReferenceValue = toggle;
        so.FindProperty("background").objectReferenceValue = bgImg;
        so.FindProperty("handle").objectReferenceValue = handleRt;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
