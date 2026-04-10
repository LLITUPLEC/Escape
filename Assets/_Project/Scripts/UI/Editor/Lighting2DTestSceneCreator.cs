using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project.UI.Editor
{
    public static class Lighting2DTestSceneCreator
    {
        private const string ScenesDir = "Assets/_Project/Scenes";
        private const string ScenePath = ScenesDir + "/Lighting2D_Test.unity";

        private const string MaterialsDir = "Assets/_Project/Materials/LightingTest";
        private const string FloorMaterialPath = MaterialsDir + "/Floor1_SpriteLit_Test.mat";

        private const string FloorSpritePath = "Assets/_Project/img/floors/floor_1.png";
        private const string FloorNormalPath = "Assets/_Project/img/floors/floor_1_Normal.png";

        [MenuItem("Tools/VFX/Создать тест-сцену 2D освещения (floor_1)")]
        public static void CreateLighting2DTestScene()
        {
            EnsureDir(ScenesDir);
            EnsureDir(MaterialsDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            var camData = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
                camData = camGo.AddComponent<UniversalAdditionalCameraData>();

            var rendererConfigured = TryAssign2DRenderer(camData, out var rendererNote);

            var floorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FloorSpritePath);
            var floorNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorNormalPath);
            var floorMaterial = CreateOrUpdateSpriteLitMaterial(floorSprite, floorNormal);

            var floorGo = new GameObject("Floor_1_Test", typeof(SpriteRenderer));
            var floorSr = floorGo.GetComponent<SpriteRenderer>();
            floorSr.sprite = floorSprite;
            floorSr.sharedMaterial = floorMaterial;
            floorSr.sortingOrder = 0;
            floorGo.transform.position = Vector3.zero;
            floorGo.transform.localScale = new Vector3(5f, 2.777f, 1f);

            var globalGo = new GameObject("Global Light 2D", typeof(Light2D));
            var global = globalGo.GetComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 0.18f;
            global.color = new Color(0.72f, 0.76f, 1f, 1f);

            var pointGo = new GameObject("Point Light 2D (Mouse)", typeof(Light2D), typeof(MouseFollowLight2D));
            var point = pointGo.GetComponent<Light2D>();
            point.lightType = Light2D.LightType.Point;
            point.color = new Color(1f, 0.74f, 0.42f, 1f);
            point.intensity = 1.35f;
            point.pointLightInnerRadius = 0.35f;
            point.pointLightOuterRadius = 2.8f;
            point.falloffIntensity = 0.5f;
            point.shadowIntensity = 0f;
            pointGo.transform.position = new Vector3(0f, 0f, 0f);

            var follow = pointGo.GetComponent<MouseFollowLight2D>();
            var so = new SerializedObject(follow);
            so.FindProperty("targetCamera").objectReferenceValue = cam;
            so.FindProperty("zWorld").floatValue = 0f;
            so.FindProperty("clampToViewport").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(follow);

            CreateLabelAnchor("InfoAnchor", new Vector3(0f, -4.7f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildSettingsIncludes(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);

            if (!rendererConfigured)
            {
                Debug.LogWarning("[Lighting2D_Test] Сцена создана, но не удалось автоматически назначить 2D Renderer для камеры. " +
                                 "Открой Main Camera -> Universal Additional Camera Data -> Renderer и выбери 2D Renderer. " +
                                 rendererNote);
            }
            else
            {
                Debug.Log("[Lighting2D_Test] Сцена готова. " + rendererNote);
            }
        }

        private static Material CreateOrUpdateSpriteLitMaterial(Sprite mainSprite, Texture normalTex)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (mat == null)
            {
                var shader = FindSpriteLitShader();
                mat = new Material(shader) { name = "Floor1_SpriteLit_Test" };
                AssetDatabase.CreateAsset(mat, FloorMaterialPath);
            }

            var targetShader = FindSpriteLitShader();
            if (mat.shader != targetShader)
                mat.shader = targetShader;

            var mainTex = mainSprite != null ? mainSprite.texture : null;
            if (mainTex != null && mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", mainTex);
            if (mainTex != null && mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", mainTex);
            if (normalTex != null && mat.HasProperty("_NormalMap"))
                mat.SetTexture("_NormalMap", normalTex);
            if (mat.HasProperty("_UseNormalMap"))
                mat.SetFloat("_UseNormalMap", normalTex != null ? 1f : 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Shader FindSpriteLitShader()
        {
            var candidates = new[]
            {
                "Universal Render Pipeline/2D/Sprite-Lit-Default",
                "Universal Render Pipeline/2D/Sprite-Lit Default",
                "Universal Render Pipeline/2D/Sprite-Lit",
            };

            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                    return shader;
            }

            throw new System.InvalidOperationException("Не найден шейдер Sprite-Lit для URP 2D.");
        }

        private static bool TryAssign2DRenderer(UniversalAdditionalCameraData camData, out string note)
        {
            note = "2D Renderer автопоиск не дал результата.";
            if (camData == null)
                return false;

            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urpAsset)
            {
                note = "В GraphicsSettings не назначен URP Asset.";
                return false;
            }

            var rendererDataListProp = typeof(UniversalRenderPipelineAsset).GetProperty("rendererDataList");
            if (rendererDataListProp == null)
            {
                note = "Не удалось получить rendererDataList у URP Asset.";
                return false;
            }

            if (rendererDataListProp.GetValue(urpAsset) is not ScriptableRendererData[] list || list.Length == 0)
            {
                note = "rendererDataList пуст.";
                return false;
            }

            for (var i = 0; i < list.Length; i++)
            {
                var data = list[i];
                if (data == null) continue;

                var t = data.GetType().Name;
                if (!t.Contains("2D"))
                    continue;

                camData.SetRenderer(i);
                note = $"Назначен renderer index {i} ({data.name}).";
                EditorUtility.SetDirty(camData);
                return true;
            }

            return false;
        }

        private static void CreateLabelAnchor(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
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
            if (string.IsNullOrWhiteSpace(dir))
                return;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}

