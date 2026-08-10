using System.Collections.Generic;
using System.IO;
using Project.Mine3D;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Project.UI.Editor
{
    public static class Mine3DSceneCreator
    {
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string PrefabsDir = "Assets/_Project/Prefabs/Mine3D";
        private const string MaterialsDir = "Assets/_Project/Materials/Mine3D";
        private const string ScenePath = ScenesDir + "/MineScene3D.unity";
        private const string ShaftPrefabPath = PrefabsDir + "/MineShaft3D.prefab";
        private const string FloorPrefabPath = PrefabsDir + "/MineFloor3D.prefab";
        private const float CameraFov = 100f;

        [MenuItem("Tools/UI/Создать сцену MineScene3D")]
        public static void CreateMine3DScene()
        {
            EnsureDir(ScenesDir);
            EnsureDir(PrefabsDir);
            EnsureDir(MaterialsDir);

            var rock = SaveMat("MineRock", new Color(0.22f, 0.2f, 0.18f), 0.08f, 0.22f);
            var metal = SaveMat("MineMetal", new Color(0.28f, 0.3f, 0.32f), 0.65f, 0.45f);
            var rust = SaveMat("MineRust", new Color(0.38f, 0.22f, 0.12f), 0.35f, 0.28f);
            var lamp = SaveUnlit("MineLamp", new Color(0.95f, 0.97f, 1f));
            var barrier = SaveMat("MineBarrier", new Color(0.18f, 0.17f, 0.16f), 0.55f, 0.3f);
            var easy = SaveMat("MonsterEasy", new Color(0.25f, 0.55f, 0.28f), 0.2f, 0.4f);
            var medium = SaveMat("MonsterMedium", new Color(0.55f, 0.45f, 0.18f), 0.2f, 0.4f);
            var hard = SaveMat("MonsterHard", new Color(0.55f, 0.16f, 0.14f), 0.2f, 0.4f);
            var wood = SaveMat("MineWood", new Color(0.30f, 0.18f, 0.09f), 0.02f, 0.18f);
            var darkRock = SaveMat("MineDarkRock", new Color(0.11f, 0.09f, 0.07f), 0.04f, 0.10f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.14f, 0.12f, 0.10f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.06f, 0.045f, 0.035f);
            RenderSettings.fogDensity = 0.035f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.045f, 0.035f);
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            cam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);

            var camData = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
                camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderType = CameraRenderType.Base;

            EnsureEventSystem();

            var world = new GameObject("Mine3DWorld").transform;
            var built = Mine3DGeometryBuilder.Build(
                world, rock, metal, rust, lamp, barrier, easy, medium, hard, wood, darkRock);

            // Префабы для правок — геометрию в сцене оставляем.
            var shaftPrefab = PrefabUtility.SaveAsPrefabAsset(built.Root.gameObject, ShaftPrefabPath);
            var faceEasy = built.Root.Find("Face_easy");
            if (faceEasy != null)
            {
                var floor1 = faceEasy.Find("Floor_1");
                if (floor1 != null)
                {
                    var temp = Object.Instantiate(floor1.gameObject);
                    temp.name = "MineFloor3D";
                    PrefabUtility.SaveAsPrefabAsset(temp, FloorPrefabPath);
                    Object.DestroyImmediate(temp);
                }
            }

            // В редакторе капсулы видны (в Play ApplyRows выставит барьеры/кулдауны).
            ActivateMonsterPreview();

            var fill = new GameObject("Mine3D_AmbientFill");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.28f;
            fillLight.color = new Color(0.55f, 0.48f, 0.40f);
            fillLight.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(40f, -20f, 0f);

            BuildUiOverlay();

            var host = new GameObject("Mine3DSceneControllerHost");
            var controller = host.AddComponent<Mine3DSceneController>();
            var shaft = host.AddComponent<Mine3DShaftController>();
            var scroller = host.AddComponent<Mine3DCameraScroller>();
            host.AddComponent<Mine3DWorldClickInput>();

            var lookY = built.TopFloorCenterY;
            cam.transform.position = new Vector3(0f, lookY, built.CameraZ);
            cam.fieldOfView = CameraFov;

            shaft.Bind(built.Root);
            shaft.SetDifficultyImmediate(Mine3DShaftController.Easy);

            var minY = built.BottomFloorCenterY - Mine3DGeometryBuilder.GetFloorOpenHeight(12) * 0.35f;
            var maxY = built.TopFloorCenterY + Mine3DGeometryBuilder.GetFloorOpenHeight(1) * 0.35f;

            var shaftSo = new SerializedObject(shaft);
            shaftSo.FindProperty("shaftRoot").objectReferenceValue = built.Root;
            shaftSo.FindProperty("currentDifficulty").stringValue = Mine3DShaftController.Easy;
            shaftSo.ApplyModifiedPropertiesWithoutUndo();

            var scrollSo = new SerializedObject(scroller);
            scrollSo.FindProperty("targetCamera").objectReferenceValue = cam;
            scrollSo.FindProperty("minY").floatValue = minY;
            scrollSo.FindProperty("maxY").floatValue = maxY;
            scrollSo.FindProperty("startY").floatValue = lookY;
            scrollSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(controller);
            so.FindProperty("shaftController").objectReferenceValue = shaft;
            so.FindProperty("cameraScroller").objectReferenceValue = scroller;
            so.FindProperty("sceneCamera").objectReferenceValue = cam;
            so.FindProperty("worldRoot").objectReferenceValue = world;
            so.ApplyModifiedPropertiesWithoutUndo();

            var clickSo = new SerializedObject(host.GetComponent<Mine3DWorldClickInput>());
            clickSo.FindProperty("worldCamera").objectReferenceValue = cam;
            clickSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildSettingsIncludes(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("[Mine3D] Создана полная сцена: " + ScenePath
                      + " (FOV=" + CameraFov + ", 12 этажей × 3 сложности). "
                      + "Play через MainMenu → Шахта_2; клик по капсуле открывает MonsterModal."
                      + (shaftPrefab != null ? "" : " (shaft prefab warning)"));
        }

        private static void ActivateMonsterPreview()
        {
            var views = Object.FindObjectsByType<Mine3DFloorView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (view == null) continue;
                view.BindInteractables();

                // В редакторе: easy — открытые этажи-превью; medium/hard — только 1 этаж, остальное барьеры.
                var isEasy = string.Equals(view.Difficulty, Mine3DShaftController.Easy, System.StringComparison.OrdinalIgnoreCase);
                var unlocked = isEasy || view.Floor <= 1;
                view.SetGameplayVisible(unlocked, monsterReady: unlocked);
            }
        }

        private static void BuildUiOverlay()
        {
            var canvasGo = new GameObject("Mine3DCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasGo.transform as RectTransform;

            var tabs = CreatePanel(root, "DifficultyTabs", new Color(0.12f, 0.10f, 0.09f, 0.96f),
                new Vector2(0.06f, 0.895f), new Vector2(0.94f, 0.985f));
            Mine3DUiBevel.StyleTabsHousing(tabs);
            var hl = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f;
            hl.padding = new RectOffset(14, 14, 10, 10);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlHeight = true;
            hl.childControlWidth = true;
            hl.childForceExpandHeight = true;
            hl.childForceExpandWidth = true;

            Mine3DUiBevel.CreateBevelTab(tabs, "easy", "ЛЁГКАЯ", new Color(0.18f, 0.55f, 0.28f, 0.98f));
            Mine3DUiBevel.CreateBevelTab(tabs, "medium", "СРЕДНЯЯ", new Color(0.22f, 0.22f, 0.24f, 0.98f));
            Mine3DUiBevel.CreateBevelTab(tabs, "hard", "ТЯЖЁЛАЯ", new Color(0.22f, 0.22f, 0.24f, 0.98f));

            CreateText(root, "Hint", "свайп / колесо — этажи   ·   рычаги — поворот шахты",
                22, new Color(0.75f, 0.78f, 0.82f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.845f), new Vector2(0.9f, 0.888f));

            CreateSceneLoadButton(root, "BackButton", "Назад", "MainMenu",
                new Color(0.18f, 0.18f, 0.22f, 0.95f),
                new Vector2(0.03f, 0.02f), new Vector2(0.28f, 0.08f));
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

        private static Text CreateText(Transform parent, string name, string value, int size, Color color,
            TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateSceneLoadButton(Transform parent, string name, string label, string sceneName,
            Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(SceneLoadButton));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            CreateText(go.transform, "Label", label, 30, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

            var loader = go.GetComponent<SceneLoadButton>();
            var so = new SerializedObject(loader);
            so.FindProperty("sceneName").stringValue = sceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material SaveMat(string name, Color color, float metallic, float smoothness)
        {
            var path = MaterialsDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                ApplyLit(existing, color, metallic, smoothness);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = Mine3DGeometryBuilder.CreateLitMaterial(name, color, metallic, smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material SaveUnlit(string name, Color color)
        {
            var path = MaterialsDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.HasProperty("_BaseColor")) existing.SetColor("_BaseColor", color);
                if (existing.HasProperty("_Color")) existing.SetColor("_Color", color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = Mine3DGeometryBuilder.CreateUnlitMaterial(name, color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void ApplyLit(Material mat, Color color, float metallic, float smoothness)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
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
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing == null)
                existing = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
            var oldModule = existing.GetComponent<StandaloneInputModule>();
            if (oldModule != null)
                Object.DestroyImmediate(oldModule);
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            if (existing.GetComponent<StandaloneInputModule>() == null)
                existing.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
