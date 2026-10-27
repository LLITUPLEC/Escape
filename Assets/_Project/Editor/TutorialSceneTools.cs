using System;
using System.Collections.Generic;
using System.IO;
using Project.Match3;
using Project.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Explicit authoring only. Never opens a real battle scene or network bootstrap.</summary>
    public static class TutorialSceneTools
    {
        public const string ScenePath = "Assets/_Project/Scenes/Tutorial.unity";
        [MenuItem("Tools/Expedition/Prepare Tutorial Scene")]
        public static void PrepareMenu() { Debug.Log(Prepare()); }
        public static string Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare Tutorial in Edit mode.");
            var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(ScenePath); var close = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (close) scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                if (!File.Exists(ScenePath))
                {
                    var root = new GameObject("TutorialRoot"); SceneManager.MoveGameObjectToScene(root, scene);
                    var controller = root.AddComponent<TutorialController>();
                    var canvas = new GameObject("TutorialCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                    canvas.transform.SetParent(root.transform, false); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                    var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Match3/Match3BoardView.prefab");
                    if (prefab == null) throw new InvalidOperationException("Safe board prefab is missing.");
                    var boardRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); boardRoot.transform.SetParent(canvas.transform, false);
                    var board = boardRoot.GetComponent<Match3BoardView>(); if (board == null) throw new InvalidOperationException("Board view is missing.");
                    var so = new SerializedObject(controller); so.FindProperty("boardView").objectReferenceValue = board; so.FindProperty("tutorialCanvas").objectReferenceValue = canvas;
                    var icons = so.FindProperty("abilityIcons"); icons.arraySize = 5; var iconFiles = new[] { 3, 2, 1, 4, 5 };
                    for (var i = 0; i < 5; i++)
                    {
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/butt_spells/" + iconFiles[i] + "_spell.png");
                        if (sprite == null) throw new InvalidOperationException("Actual ability artwork is missing: " + iconFiles[i]);
                        icons.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var camera = new GameObject("TutorialCamera", typeof(Camera)); camera.tag = "MainCamera"; camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor = new Color32(6, 21, 27, 255);
                    canvas.worldCamera = camera.GetComponent<Camera>(); canvas.planeDistance = 1;
                    AssertSafe(scene);
                    Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)); if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save Tutorial scene.");
                }
                // Upgrade the already-shipped Tutorial scene in place. Only safe
                // presentation prefabs are authored; no live match is instantiated.
                var changed = EnsureBattlePanels(scene);
                AssertSafe(scene);
                if (changed && !EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save upgraded Tutorial scene.");
                var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes); var found = false;
                for (var i = 0; i < list.Count; i++) if (list[i].path == ScenePath) { list[i] = new EditorBuildSettingsScene(ScenePath, true); found = true; }
                if (!found) list.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray();
                return "Tutorial scene prepared and enabled in Build Settings. Original scenes/unsaved work preserved. No auth, match or account API was invoked.";
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (close && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
        private static bool EnsureBattlePanels(Scene scene)
        {
            TutorialController controller = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                controller = root.GetComponentInChildren<TutorialController>(true);
                if (controller != null) break;
            }
            if (controller == null) throw new InvalidOperationException("Tutorial controller is missing.");
            var so = new SerializedObject(controller);
            var canvas = so.FindProperty("tutorialCanvas").objectReferenceValue as Canvas;
            if (canvas == null) throw new InvalidOperationException("Tutorial canvas is missing.");
            var changed = false;
            var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Match3/Match3PlayerPanel.prefab");
            var damagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/DamagePopup.prefab");
            if (panelPrefab == null || damagePrefab == null) throw new InvalidOperationException("Shipping battle presentation prefabs are missing.");
            foreach (var field in new[] { "playerPanel", "enemyPanel" })
            {
                var panel = so.FindProperty(field).objectReferenceValue as Match3PlayerPanel;
                if (panel == null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, scene);
                    go.name = field == "playerPanel" ? "TutorialPlayerPanel" : "TutorialEnemyPanel";
                    go.transform.SetParent(canvas.transform, false); panel = go.GetComponent<Match3PlayerPanel>();
                    so.FindProperty(field).objectReferenceValue = panel; changed = true;
                }
                if (panel.damagePopup == null)
                {
                    var popup = (GameObject)PrefabUtility.InstantiatePrefab(damagePrefab, scene);
                    popup.name = "DamagePopup"; popup.transform.SetParent(panel.avatarImage.transform, false);
                    var popupRect = popup.transform as RectTransform;
                    popupRect.anchorMin = popupRect.anchorMax = popupRect.pivot = new Vector2(.5f, .5f);
                    popupRect.anchoredPosition = new Vector2(0, 40);
                    panel.damagePopup = popup.GetComponent<DamagePopupView>(); panel.damagePopupAnchor = panel.avatarImage.rectTransform;
                    changed = true;
                }
                if (panel.avatarImage.sprite == null)
                {
                    panel.avatarImage.sprite = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-avatar"); changed = true;
                }
                if (panel.gainPopup == null) panel.ResolveGainPopupFromHierarchy();
                if (panel.gainPopup == null || panel.damagePopup == null) throw new InvalidOperationException("Real battle popup widgets are missing.");
                EditorUtility.SetDirty(panel);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }
        internal static void AssertSafe(Scene scene)
        {
            var controllers = 0;
            foreach (var root in scene.GetRootGameObjects()) foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) throw new InvalidOperationException("Tutorial has a missing script.");
                var name = component.GetType().Name;
                if (name == "NakamaBootstrap" || name == "DuelMatch3Manager" || name == "CharacterScreenController" || name == "ArenaMatch8Bridge")
                    throw new InvalidOperationException("Unsafe tutorial component: " + name);
                if (component is TutorialController) controllers++;
            }
            if (controllers != 1) throw new InvalidOperationException("Tutorial requires exactly one isolated controller.");
        }
    }
}
