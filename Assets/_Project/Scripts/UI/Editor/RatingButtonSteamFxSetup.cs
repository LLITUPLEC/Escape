using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI.Editor
{
    public static class RatingButtonSteamFxSetup
    {
        [MenuItem("Tools/VFX/Добавить пар за RatingButton (MainMenu)")]
        public static void AddSteamBehindRatingButton()
        {
            var button = FindRatingButton();
            if (button == null)
            {
                EditorUtility.DisplayDialog(
                    "Steam FX",
                    "Не найден RatingButton в открытой сцене.\nОткрой MainMenu и повтори.",
                    "OK");
                return;
            }

            // Без ui_circle_soft — только мягкий процедурный спрайт (как при обычном Play).
            var fx = UiSteamGlowFx.EnsureOnButton(button);
            if (fx == null)
            {
                EditorUtility.DisplayDialog("Steam FX", "Не удалось создать эффект.", "OK");
                return;
            }

            EditorUtility.SetDirty(button.gameObject);
            EditorUtility.SetDirty(fx);
            EditorSceneManager.MarkSceneDirty(button.gameObject.scene);

            Selection.activeGameObject = button.gameObject;
            EditorGUIUtility.PingObject(button.gameObject);
            Debug.Log("[Steam FX] Пар/свечение добавлены. Для ручной позиции сними Follow Button на UiSteamGlowFx.");
        }

        private static RectTransform FindRatingButton()
        {
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in buttons)
            {
                if (b != null && b.gameObject.name == "RatingButton")
                    return b.transform as RectTransform;
            }

            return null;
        }
    }
}
