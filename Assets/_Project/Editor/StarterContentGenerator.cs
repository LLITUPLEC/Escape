using System;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Project.Editor
{
    public static class StarterContentGenerator
    {
        private const string Root = "Assets/_Project";
        private const string ScenesPath = Root + "/Scenes";
        private const string PrefabsPath = Root + "/Prefabs";
        private const string ResourcesPath = Root + "/Resources";

        [MenuItem("Tools/Project/Generate Starter Content")]
        public static void Generate()
        {
            EnsureFolders();

            var config = EnsureNakamaConfig();
            var mainMenuPrefab = EnsureMainMenuPrefab();

            GenerateMainMenuScene(config, mainMenuPrefab);

            AddScenesToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Готово", "Сцены и префабы созданы.\nОткройте сцену MainMenu и нажмите Play.", "OK");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "_Project");
            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root, "Resources");
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Scripts");
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static NakamaConnectionConfig EnsureNakamaConfig()
        {
            var assetPath = $"{ResourcesPath}/NakamaConnectionConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<NakamaConnectionConfig>(assetPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<NakamaConnectionConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
            return config;
        }

        private static GameObject EnsureMainMenuPrefab()
        {
            var assetPath = $"{PrefabsPath}/MainMenuScreen.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null) return existing;

            var root = new GameObject("MainMenuScreen");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(root.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);

            CreateButton(panel.transform, "BotsButton", "Боты", new Vector2(0.5f, 0.42f));

            root.AddComponent<MainMenuController>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            UObject.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchor)
        {
            var btnGo = new GameObject(name, typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(520, 140);
            rt.anchoredPosition = Vector2.zero;

            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.18f, 0.55f, 0.95f, 1f);

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(btnGo.transform, false);
            var txt = textGo.GetComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 56;

            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btnGo;
        }

        private static void GenerateMainMenuScene(NakamaConnectionConfig config, GameObject mainMenuPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = "MainMenu";

            // Nakama bootstrap (DontDestroy)
            var net = new GameObject("NakamaBootstrap");
            var bootstrap = net.AddComponent<NakamaBootstrap>();
            bootstrap.Config = config;

            EnsureEventSystem();
            PrefabUtility.InstantiatePrefab(mainMenuPrefab);

            EditorSceneManager.SaveScene(scene, $"{ScenesPath}/MainMenu.unity");
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = UObject.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null) return;

            var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));

            // Если проект на новом Input System — поставим InputSystemUIInputModule, иначе StandaloneInputModule.
            var inputSystemUIModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemUIModuleType != null)
            {
                go.AddComponent(inputSystemUIModuleType);
            }
            else
            {
                go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private static void AddScenesToBuildSettings()
        {
            var mainMenu = $"{ScenesPath}/MainMenu.unity";

            var scenes = new[]
            {
                new EditorBuildSettingsScene(mainMenu, true),
            };
            EditorBuildSettings.scenes = scenes;
        }
    }
}
