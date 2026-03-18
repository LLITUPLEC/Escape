using System.IO;
using System;
using Project.Duel;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            var playerPrefab = EnsurePlayerPrefab();
            var mainMenuPrefab = EnsureMainMenuPrefab();
            var duelRoomPrefab = EnsureDuelRoomPrefab();

            GenerateMainMenuScene(config, mainMenuPrefab);
            GenerateDuelRoomScene(playerPrefab, duelRoomPrefab);

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

        private static GameObject EnsurePlayerPrefab()
        {
            var assetPath = $"{PrefabsPath}/Player.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null) return existing;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Player";
            UObject.DestroyImmediate(go.GetComponent<Collider>()); // CharacterController будет коллайдером

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            go.AddComponent<Project.Player.PlayerMovementController>();
            go.AddComponent<Project.Player.NetworkTransformView>();
            go.AddComponent<Project.Player.PlayerAnimatorDriver>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
            UObject.DestroyImmediate(go);
            return prefab;
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

            var duelBtn = CreateButton(panel.transform, "DuelButton", "Дуэль", new Vector2(0.5f, 0.58f));
            var botsBtn = CreateButton(panel.transform, "BotsButton", "Боты", new Vector2(0.5f, 0.42f));

            var controller = root.AddComponent<MainMenuController>();
            controller.Bind(duelBtn.GetComponent<Button>(), botsBtn.GetComponent<Button>());

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

        private static GameObject EnsureDuelRoomPrefab()
        {
            var assetPath = $"{PrefabsPath}/DuelRoom.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null) return existing;

            var root = new GameObject("DuelRoom");

            // Пол
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(8f, 1f, 44f);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);

            // Перегородка
            var divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            divider.name = "Divider";
            divider.transform.SetParent(root.transform, false);
            // Низкая "перегородка/забор", чтобы игроки могли видеть друг друга поверх.
            divider.transform.localScale = new Vector3(0.25f, 1.2f, 44f);
            divider.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            // Стены по краям
            CreateWall(root.transform, "WallLeft", new Vector3(-4f, 1.25f, 0f), new Vector3(0.25f, 2.5f, 44f));
            CreateWall(root.transform, "WallRight", new Vector3(4f, 1.25f, 0f), new Vector3(0.25f, 2.5f, 44f));

            // Двери (2 на каждую сторону)
            CreateDoor(root.transform, "Door_L_1", new Vector3(-2f, 1.0f, -10f), openAfter: 4f);
            CreateDoor(root.transform, "Door_L_2", new Vector3(-2f, 1.0f, 10f), openAfter: 9f);
            CreateDoor(root.transform, "Door_R_1", new Vector3(2f, 1.0f, -10f), openAfter: 4f);
            CreateDoor(root.transform, "Door_R_2", new Vector3(2f, 1.0f, 10f), openAfter: 9f);

            // Спавны
            var spawns = new GameObject("Spawns");
            spawns.transform.SetParent(root.transform, false);

            var spawnLeft = new GameObject("SpawnLeft");
            spawnLeft.transform.SetParent(spawns.transform, false);
            spawnLeft.transform.localPosition = new Vector3(-2f, 0f, -19f);

            var spawnRight = new GameObject("SpawnRight");
            spawnRight.transform.SetParent(spawns.transform, false);
            spawnRight.transform.localPosition = new Vector3(2f, 0f, -19f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            UObject.DestroyImmediate(root);
            return prefab;
        }

        private static void CreateWall(Transform parent, string name, Vector3 localPos, Vector3 localScale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPos;
            wall.transform.localScale = localScale;
        }

        private static void CreateDoor(Transform parent, string name, Vector3 localPos, float openAfter)
        {
            var doorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorGo.name = name;
            doorGo.transform.SetParent(parent, false);
            doorGo.transform.localPosition = localPos;
            doorGo.transform.localScale = new Vector3(3.4f, 2.0f, 0.35f);

            var door = doorGo.AddComponent<Door>();
            var cond = doorGo.AddComponent<TimerOpenCondition>();
            cond.Configure(door, openAfter);
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

        private static void GenerateDuelRoomScene(GameObject playerPrefab, GameObject duelRoomPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = "DuelRoom";

            var roomInstance = (GameObject)PrefabUtility.InstantiatePrefab(duelRoomPrefab);

            EnsureEventSystem();

            // Менеджер дуэли
            var mgrGo = new GameObject("DuelRoomManager");
            var mgr = mgrGo.AddComponent<DuelRoomManager>();
            mgr.PlayerPrefab = playerPrefab;

            var spawnLeft = roomInstance.transform.Find("Spawns/SpawnLeft");
            var spawnRight = roomInstance.transform.Find("Spawns/SpawnRight");
            mgr.SetSpawns(spawnLeft, spawnRight);

            // UI статус (поиск соперника)
            var ui = CreateDuelStatusUI();
            mgr.SetStatusUI(ui.GetComponent<Project.Duel.DuelStatusUI>());

            // HUD (кнопка "Выйти", подтверждение, баннер победы)
            var hud = CreateDuelHud(mgr);
            mgr.SetHud(hud.GetComponent<Project.Duel.DuelHudController>());

            // Камера в сцене пусть останется дефолтной, SimpleFollowCamera добавится runtime
            EditorSceneManager.SaveScene(scene, $"{ScenesPath}/DuelRoom.unity");
        }

        private static GameObject CreateDuelStatusUI()
        {
            var root = new GameObject("DuelStatusUI");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(root.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var group = canvasGo.AddComponent<CanvasGroup>();

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(panel.transform, false);
            var txt = textGo.GetComponent<Text>();
            txt.text = "Поиск соперника…";
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 64;

            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900, 180);
            trt.anchoredPosition = Vector2.zero;

            var status = root.AddComponent<Project.Duel.DuelStatusUI>();
            status.Bind(group, txt);

            return root;
        }

        private static GameObject CreateDuelHud(DuelRoomManager room)
        {
            var root = new GameObject("DuelHUD");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(root.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // Exit button
            var exit = new GameObject("ExitButton", typeof(Image), typeof(Button));
            exit.transform.SetParent(canvasGo.transform, false);
            var exitRt = exit.GetComponent<RectTransform>();
            exitRt.anchorMin = new Vector2(0f, 1f);
            exitRt.anchorMax = new Vector2(0f, 1f);
            exitRt.pivot = new Vector2(0f, 1f);
            exitRt.anchoredPosition = new Vector2(40f, -40f);
            exitRt.sizeDelta = new Vector2(260f, 96f);
            exit.GetComponent<Image>().color = new Color(0.95f, 0.33f, 0.28f, 1f);

            var exitText = new GameObject("Text", typeof(Text));
            exitText.transform.SetParent(exit.transform, false);
            var exitLabel = exitText.GetComponent<Text>();
            exitLabel.text = "Выйти";
            exitLabel.alignment = TextAnchor.MiddleCenter;
            exitLabel.color = Color.white;
            exitLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            exitLabel.fontSize = 44;
            var exitTextRt = exitText.GetComponent<RectTransform>();
            exitTextRt.anchorMin = Vector2.zero;
            exitTextRt.anchorMax = Vector2.one;
            exitTextRt.offsetMin = Vector2.zero;
            exitTextRt.offsetMax = Vector2.zero;

            // Confirm panel
            var confirmRoot = new GameObject("Confirm", typeof(CanvasGroup));
            confirmRoot.transform.SetParent(canvasGo.transform, false);
            var confirmGroup = confirmRoot.GetComponent<CanvasGroup>();
            confirmGroup.alpha = 0f;
            confirmGroup.interactable = false;
            confirmGroup.blocksRaycasts = false;

            var dim = new GameObject("Dim", typeof(Image));
            dim.transform.SetParent(confirmRoot.transform, false);
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(confirmRoot.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(860f, 420f);
            panelRt.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.17f, 1f);

            var question = new GameObject("Question", typeof(Text));
            question.transform.SetParent(panel.transform, false);
            var q = question.GetComponent<Text>();
            q.text = "Выйти из дуэли?\nСопернику засчитается победа.";
            q.alignment = TextAnchor.MiddleCenter;
            q.color = Color.white;
            q.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            q.fontSize = 44;
            var qrt = question.GetComponent<RectTransform>();
            qrt.anchorMin = new Vector2(0.5f, 1f);
            qrt.anchorMax = new Vector2(0.5f, 1f);
            qrt.pivot = new Vector2(0.5f, 1f);
            qrt.sizeDelta = new Vector2(820f, 220f);
            qrt.anchoredPosition = new Vector2(0f, -40f);

            var yes = CreateConfirmButton(panel.transform, "Yes", "Да", new Vector2(0.28f, 0.18f), new Color(0.18f, 0.75f, 0.35f, 1f));
            var no = CreateConfirmButton(panel.transform, "No", "Нет", new Vector2(0.72f, 0.18f), new Color(0.35f, 0.40f, 0.48f, 1f));

            // Banner (win)
            var bannerRoot = new GameObject("Banner", typeof(CanvasGroup));
            bannerRoot.transform.SetParent(canvasGo.transform, false);
            var bannerGroup = bannerRoot.GetComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.interactable = false;
            bannerGroup.blocksRaycasts = false;

            var bannerPanel = new GameObject("Panel", typeof(Image));
            bannerPanel.transform.SetParent(bannerRoot.transform, false);
            var brt = bannerPanel.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(900f, 260f);
            brt.anchoredPosition = new Vector2(0f, 460f);
            bannerPanel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

            var bannerText = new GameObject("Text", typeof(Text));
            bannerText.transform.SetParent(bannerPanel.transform, false);
            var bt = bannerText.GetComponent<Text>();
            bt.text = "Победа!";
            bt.alignment = TextAnchor.MiddleCenter;
            bt.color = Color.white;
            bt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bt.fontSize = 56;
            var btrt = bannerText.GetComponent<RectTransform>();
            btrt.anchorMin = Vector2.zero;
            btrt.anchorMax = Vector2.one;
            btrt.offsetMin = Vector2.zero;
            btrt.offsetMax = Vector2.zero;

            var controller = root.AddComponent<Project.Duel.DuelHudController>();
            controller.Bind(
                room,
                exit.GetComponent<Button>(),
                confirmGroup,
                yes.GetComponent<Button>(),
                no.GetComponent<Button>(),
                bannerGroup,
                bt);

            return root;
        }

        private static GameObject CreateConfirmButton(Transform parent, string name, string label, Vector2 anchor, Color color)
        {
            var btnGo = new GameObject(name, typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(300f, 110f);
            rt.anchoredPosition = Vector2.zero;

            btnGo.GetComponent<Image>().color = color;

            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(btnGo.transform, false);
            var txt = textGo.GetComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 50;

            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btnGo;
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
            var duelRoom = $"{ScenesPath}/DuelRoom.unity";

            var scenes = new[]
            {
                new EditorBuildSettingsScene(mainMenu, true),
                new EditorBuildSettingsScene(duelRoom, true),
            };
            EditorBuildSettings.scenes = scenes;
        }
    }
}

