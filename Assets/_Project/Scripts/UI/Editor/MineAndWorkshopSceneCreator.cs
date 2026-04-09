using System.Collections.Generic;
using System.IO;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Project.UI.Editor
{
    public static class MineAndWorkshopSceneCreator
    {
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string MineScenePath = ScenesDir + "/MineScene.unity";
        private const string WorkshopScenePath = ScenesDir + "/WorkshopScene.unity";
        private const string MonsterCatalogPath = "Assets/_Project/Data/Match3/Monsters/MainMonsterCatalog.asset";
        private const string MonsterFrameCatalogPath = "Assets/_Project/Data/Match3/Monsters/MainMonsterFrameCatalog.asset";

        [MenuItem("Tools/UI/Создать сцены Шахты и Мастерской")]
        public static void CreateMineAndWorkshopScenes()
        {
            EnsureDir(ScenesDir);
            CreateMineScene();
            CreateWorkshopScene();
            EnsureBuildSettingsIncludes(MineScenePath);
            EnsureBuildSettingsIncludes(WorkshopScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateMineScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            var cam = cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            EnsureEventSystem();

            var canvasGo = CreateCanvasRoot("MineCanvas");
            var root = canvasGo.transform as RectTransform;

            var bg = CreatePanel(root, "MineBackground", new Color(0.10f, 0.10f, 0.14f, 0.95f),
                new Vector2(0f, 0f), new Vector2(1f, 1f));
            CreateText(bg, "Title", "Шахта", 44, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.34f, 0.90f), new Vector2(0.80f, 0.98f));
            CreateHeaderResources(bg);

            var liftPanel = CreatePanel(bg, "FloorLift", new Color(0.06f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.03f, 0.12f), new Vector2(0.17f, 0.88f));
            var liftLayout = liftPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            liftLayout.padding = new RectOffset(8, 8, 8, 8);
            liftLayout.spacing = 6f;
            liftLayout.childAlignment = TextAnchor.UpperCenter;
            liftLayout.childControlHeight = true;
            liftLayout.childControlWidth = true;
            liftLayout.childForceExpandHeight = false;
            liftLayout.childForceExpandWidth = true;

            var scrollRoot = CreatePanel(bg, "CardsScrollView", new Color(0.05f, 0.05f, 0.08f, 0.88f),
                new Vector2(0.20f, 0.12f), new Vector2(0.96f, 0.88f));
            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 25f;

            var viewport = CreatePanel(scrollRoot, "Viewport", new Color(0f, 0f, 0f, 0.01f),
                new Vector2(0f, 0f), new Vector2(1f, 1f));
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = CreatePanel(viewport, "CardsContent", new Color(0f, 0f, 0f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = new Vector2(8f, 0f);
            contentRt.offsetMax = new Vector2(-8f, 0f);
            var contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 10f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            scrollRect.viewport = viewport;
            scrollRect.content = contentRt;

            for (var floor = 1; floor <= 12; floor++)
            {
                var liftBtn = CreateButton(liftPanel, "LiftFloor_" + floor, floor.ToString(),
                    new Color(0.18f, 0.18f, 0.24f, 0.95f), Color.white, Vector2.zero, Vector2.one);
                var liftLe = liftBtn.gameObject.AddComponent<LayoutElement>();
                liftLe.minHeight = 44f;
                liftLe.preferredHeight = 44f;

                var row = CreatePanel(content, "Floor_" + floor, new Color(0.16f, 0.16f, 0.22f, 0.95f),
                    Vector2.zero, Vector2.one);
                var le = row.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 110f;
                le.preferredHeight = 110f;

                CreateText(row, "Label", floor + " этаж", 22, Color.white, TextAnchor.UpperLeft,
                    new Vector2(0.03f, 0.60f), new Vector2(0.22f, 0.96f));

                var monsterSlot = CreatePanel(row, "MonsterSlot", new Color(0.36f, 0.14f, 0.14f, 0.96f),
                    new Vector2(0.24f, 0.14f), new Vector2(0.47f, 0.86f));
                CreateText(monsterSlot, "MonsterLabel", "БОТ", 20, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

                var silhouette = CreatePanel(row, "BossSilhouette", new Color(0.07f, 0.07f, 0.10f, 0.92f),
                    new Vector2(0.49f, 0.14f), new Vector2(0.61f, 0.86f));
                CreateText(silhouette, "SilhouetteLabel", "?", 28, new Color(0.75f, 0.75f, 0.82f), TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one);

                var rewards = CreatePanel(row, "RewardsPanel", new Color(0.10f, 0.10f, 0.16f, 0.85f),
                    new Vector2(0.63f, 0.14f), new Vector2(0.79f, 0.86f));
                CreateText(rewards, "RewardText", "XP 0\nG 0\nOre 0", 14, new Color(0.95f, 0.90f, 0.70f),
                    TextAnchor.MiddleLeft, new Vector2(0.10f, 0.05f), new Vector2(0.90f, 0.95f));

                CreateText(row, "StateText", floor == 1 ? "Монстр готов" : "Барьер",
                    18, new Color(0.96f, 0.78f, 0.45f), TextAnchor.MiddleCenter,
                    new Vector2(0.81f, 0.14f), new Vector2(0.97f, 0.86f));
            }

            CreateSceneLoadButton(bg, "BackButton", "Назад", "MainMenu",
                new Color(0.20f, 0.20f, 0.26f, 0.95f), new Vector2(0.03f, 0.03f), new Vector2(0.18f, 0.10f));

            var controllerHost = new GameObject("MineSceneControllerHost", typeof(MineSceneController));
            var hostRt = controllerHost.AddComponent<RectTransform>();
            hostRt.SetParent(bg, false);
            hostRt.anchorMin = hostRt.anchorMax = new Vector2(0.5f, 0.5f);
            hostRt.sizeDelta = Vector2.zero;
            var controller = controllerHost.GetComponent<MineSceneController>();
            var catalog = AssetDatabase.LoadAssetAtPath<Project.Match3.MonsterCatalog>(MonsterCatalogPath);
            var frameCatalog = AssetDatabase.LoadAssetAtPath<Project.Match3.MonsterFrameCatalog>(MonsterFrameCatalogPath);
            if (controller != null)
            {
                var so = new SerializedObject(controller);
                if (catalog != null)
                    so.FindProperty("monsterCatalog").objectReferenceValue = catalog;
                if (frameCatalog != null)
                    so.FindProperty("monsterFrameCatalog").objectReferenceValue = frameCatalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controller);
            }

            EditorSceneManager.SaveScene(scene, MineScenePath);
        }

        private static void CreateWorkshopScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            var cam = cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.07f, 0.06f);
            EnsureEventSystem();

            var canvasGo = CreateCanvasRoot("WorkshopCanvas");
            var root = canvasGo.transform as RectTransform;
            var bg = CreatePanel(root, "WorkshopBackground", new Color(0.12f, 0.09f, 0.09f, 0.95f),
                new Vector2(0f, 0f), new Vector2(1f, 1f));

            CreateText(bg, "Title", "Мастерская", 44, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.28f, 0.90f), new Vector2(0.72f, 0.98f));

            var slots = CreatePanel(bg, "CraftSlots", new Color(0.18f, 0.14f, 0.14f, 0.92f),
                new Vector2(0.12f, 0.20f), new Vector2(0.88f, 0.82f));
            var grid = slots.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.cellSize = new Vector2(170f, 170f);
            grid.spacing = new Vector2(16f, 16f);
            grid.padding = new RectOffset(24, 24, 24, 24);

            for (var i = 0; i < 8; i++)
            {
                var slot = CreatePanel(slots, "Slot_" + i, new Color(0.24f, 0.19f, 0.19f, 1f), Vector2.zero, Vector2.one);
                CreateText(slot, "SlotLabel", "Слот " + (i + 1), 20, new Color(1f, 0.93f, 0.75f), TextAnchor.MiddleCenter,
                    new Vector2(0f, 0f), new Vector2(1f, 1f));
            }

            CreateText(bg, "Hint", "Заглушка мастерской: сюда можно подключить крафт и инвентарь.",
                22, new Color(0.94f, 0.92f, 0.86f), TextAnchor.MiddleCenter,
                new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.18f));

            CreateSceneLoadButton(bg, "BackButton", "Назад", "MainMenu",
                new Color(0.22f, 0.18f, 0.18f, 0.95f), new Vector2(0.03f, 0.03f), new Vector2(0.18f, 0.10f));

            EditorSceneManager.SaveScene(scene, WorkshopScenePath);
        }

        private static void CreateHeaderResources(Transform parent)
        {
            var header = CreatePanel(parent, "HeaderResources", new Color(0.05f, 0.07f, 0.12f, 0.92f),
                new Vector2(0.20f, 0.90f), new Vector2(0.96f, 0.985f));
            var names = new[] { "Energy", "ore", "Gold", "ingots", "matter", "keys" };
            var width = 1f / names.Length;
            for (var i = 0; i < names.Length; i++)
            {
                var entry = new GameObject(names[i], typeof(RectTransform));
                var entryRt = entry.GetComponent<RectTransform>();
                entryRt.SetParent(header, false);
                entryRt.anchorMin = new Vector2(i * width, 0f);
                entryRt.anchorMax = new Vector2((i + 1) * width, 1f);
                entryRt.offsetMin = new Vector2(2f, 2f);
                entryRt.offsetMax = new Vector2(-2f, -2f);
                CreateText(entryRt, "Label", names[i], 12, new Color(0.82f, 0.88f, 0.98f), TextAnchor.UpperCenter,
                    new Vector2(0f, 0.48f), new Vector2(1f, 1f));
                CreateText(entryRt, "Value", "—", 15, Color.white, TextAnchor.LowerCenter,
                    new Vector2(0f, 0f), new Vector2(1f, 0.58f));
            }
        }

        private static GameObject CreateCanvasRoot(string name)
        {
            var canvasGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvasGo;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private static Text CreateText(Transform parent, string name, string value, int size, Color color, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = GetBuiltinFont();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            return text;
        }

        private static Font GetBuiltinFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch
            {
                // ignore and try legacy fallback below
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        private static Button CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.GetComponent<Image>().color = bgColor;
            CreateText(go.transform, "Label", label, 18, textColor, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            return go.GetComponent<Button>();
        }

        private static void CreateSceneLoadButton(Transform parent, string name, string label, string sceneName, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(SceneLoadButton));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.GetComponent<Image>().color = color;
            CreateText(go.transform, "Label", label, 24, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            var loader = go.GetComponent<SceneLoadButton>();
            var so = new SerializedObject(loader);
            so.FindProperty("sceneName").stringValue = sceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loader);
        }

        private static void EnsureBuildSettingsIncludes(string scenePath)
        {
            var current = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in current)
            {
                if (s.path == scenePath)
                    return;
            }
            current.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = current.ToArray();
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                existing = go.GetComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            var oldModule = existing.GetComponent<StandaloneInputModule>();
            if (oldModule != null)
                Object.DestroyImmediate(oldModule);
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                _ = existing.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            if (existing.GetComponent<StandaloneInputModule>() == null)
                _ = existing.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
