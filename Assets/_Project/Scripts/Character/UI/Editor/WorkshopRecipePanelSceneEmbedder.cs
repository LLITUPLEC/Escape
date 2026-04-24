using Project.Character.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project.Character.UI.Editor
{
    /// <summary>Добавляет <c>WorkshopRecipePanel</c> в иерархию сцены мастерской, чтобы настраивать в инспекторе без ожидания Play Mode.</summary>
    public static class WorkshopRecipePanelSceneEmbedder
    {
        private const string WorkshopScenePath = "Assets/_Project/Scenes/WorkshopScene.unity";

        [MenuItem("Tools/Workshop/Встроить WorkshopRecipePanel в сцену", priority = 50)]
        public static void EmbedPanelInWorkshopScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(WorkshopScenePath, OpenSceneMode.Single);
            bool found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "WorkshopCanvas") continue;
                found = true;
                var bg = root.transform.Find("WorkshopBackground");
                if (bg == null)
                {
                    Debug.LogError("[WorkshopRecipePanel] WorkshopCanvas/WorkshopBackground не найден.");
                    return;
                }
                var bgRt = bg.GetComponent<RectTransform>();
                if (bgRt == null) return;
                if (WorkshopRecipePanelSetup.TryBindExisting(bgRt, out _))
                {
                    Debug.Log("[WorkshopRecipePanel] Панель уже на сцене.");
                    return;
                }
                WorkshopRecipePanelSetup.Build(bgRt);
                var ws = root.GetComponent<WorkshopSceneController>();
                if (ws != null)
                {
                    var wso = new SerializedObject(ws);
                    wso.FindProperty("workshopBackground").objectReferenceValue = bgRt;
                    wso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(ws);
                }
                Debug.Log("[WorkshopRecipePanel] Панель добавлена и сцена сохранена.");
            }
            if (!found)
                Debug.LogError("[WorkshopRecipePanel] Корень WorkshopCanvas не найден.");
            EditorSceneManager.SaveScene(scene);
        }
    }
}
