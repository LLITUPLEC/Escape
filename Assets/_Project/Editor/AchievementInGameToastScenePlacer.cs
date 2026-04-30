#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: Tools → Achievements → Добавить Toast (in-game) в сцену DuelMatch3.
/// Создаёт отдельный объект в иерархии, чтобы его можно было вручную стилизовать.
/// </summary>
public static class AchievementInGameToastScenePlacer
{
    private const string ScenePath = "Assets/_Project/Scenes/DuelMatch3.unity";

    [MenuItem("Tools/Achievements/Добавить Toast (in-game) в DuelMatch3")]
    public static void PlaceToastInDuelMatch3()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[Achievements] Не удалось открыть сцену: {ScenePath}");
            return;
        }

        var existing = GameObject.Find("AchievementInGameToast");
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            Debug.Log("[Achievements] AchievementInGameToast уже есть в сцене.");
            return;
        }

        // Try to parent under DuelMatch3Manager/Canvas if present, else under any Canvas.
        Transform parent = null;
        var mgr = Object.FindFirstObjectByType<Project.Match3.DuelMatch3Manager>();
        if (mgr != null)
            parent = mgr.transform.Find("Canvas");
        if (parent == null)
            parent = Object.FindFirstObjectByType<Canvas>()?.transform;

        var root = new GameObject("AchievementInGameToast", typeof(RectTransform), typeof(CanvasGroup));
        var rt = root.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -44f);
        rt.sizeDelta = new Vector2(680f, 156f);

        var cg = root.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var bg = root.AddComponent<Image>();
        bg.raycastTarget = false;
        bg.color = new Color(0.06f, 0.085f, 0.12f, 0.93f);

        // Title
        var titleGo = new GameObject("ToastTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.SetParent(rt, false);
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(20f, 48f);
        titleRt.offsetMax = new Vector2(-20f, -18f);

        var title = titleGo.GetComponent<TextMeshProUGUI>();
        title.text = "<b>Достижение выполнено!</b>\n...";
        title.font = TMP_Settings.defaultFontAsset;
        title.fontSize = 25f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.96f, 0.93f, 0.74f);
        title.richText = true;

        // Reward
        var rewardGo = new GameObject("ToastRewardLine", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rewardRt = rewardGo.GetComponent<RectTransform>();
        rewardRt.SetParent(rt, false);
        rewardRt.anchorMin = new Vector2(0f, 0f);
        rewardRt.anchorMax = new Vector2(1f, 0f);
        rewardRt.pivot = new Vector2(0.5f, 0f);
        rewardRt.offsetMin = new Vector2(20f, 10f);
        rewardRt.offsetMax = new Vector2(-20f, 44f);

        var reward = rewardGo.GetComponent<TextMeshProUGUI>();
        reward.text = "Награда: ...";
        reward.font = TMP_Settings.defaultFontAsset;
        reward.fontSize = 25f;
        reward.alignment = TextAlignmentOptions.Bottom;
        reward.color = new Color(0.75f, 1f, 0.78f);
        reward.richText = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = root;
        Debug.Log("[Achievements] AchievementInGameToast добавлен в сцену DuelMatch3.");
    }
}
#endif

