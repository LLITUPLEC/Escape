using System.Collections.Generic;
using System.IO;
using System;
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
            point.blendStyleIndex = 0;
            point.pointLightInnerRadius = 0.35f;
            point.pointLightOuterRadius = 2.8f;
            point.falloffIntensity = 0.5f;
            point.shadowIntensity = 0f;
            pointGo.transform.position = new Vector3(0f, 0f, 0f);
            ConfigurePointLightNormalMap(point);

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

            var activePipeline = QualitySettings.renderPipeline ?? GraphicsSettings.currentRenderPipeline;
            if (activePipeline is not UniversalRenderPipelineAsset urpAsset)
            {
                note = "Активный Render Pipeline не является URP Asset.";
                return false;
            }

            var urpSo = new SerializedObject(urpAsset);
            var rendererListProp = urpSo.FindProperty("m_RendererDataList");
            if (rendererListProp == null || !rendererListProp.isArray || rendererListProp.arraySize == 0)
            {
                note = "m_RendererDataList пуст или недоступен.";
                return false;
            }

            for (var i = 0; i < rendererListProp.arraySize; i++)
            {
                var element = rendererListProp.GetArrayElementAtIndex(i);
                var data = element != null ? element.objectReferenceValue as ScriptableRendererData : null;
                if (data == null) continue;

                if (!Is2DRendererData(data))
                    continue;

                camData.SetRenderer(i);
                note = $"Назначен renderer index {i} ({data.name}).";
                EditorUtility.SetDirty(camData);
                var syncNote = SyncRendererIndexAcrossUsedUrpAssets(i, urpAsset);
                if (!string.IsNullOrEmpty(syncNote))
                    note = $"{note} {syncNote}";
                return true;
            }

            if (!TryCreateAndAssign2DRenderer(urpAsset, camData, rendererListProp, urpSo, out var createdIndex, out note))
                return false;

            var createdSyncNote = SyncRendererIndexAcrossUsedUrpAssets(createdIndex, urpAsset);
            if (!string.IsNullOrEmpty(createdSyncNote))
                note = $"{note} {createdSyncNote}";

            return true;
        }

        private static bool TryCreateAndAssign2DRenderer(
            UniversalRenderPipelineAsset urpAsset,
            UniversalAdditionalCameraData camData,
            SerializedProperty rendererListProp,
            SerializedObject urpSo,
            out int assignedIndex,
            out string note)
        {
            assignedIndex = -1;
            note = "2D Renderer отсутствует, автосоздание не удалось.";

            if (!TryCreate2DRendererAsset(urpAsset, out var created, out note))
                return false;

            var newIndex = rendererListProp.arraySize;
            rendererListProp.InsertArrayElementAtIndex(newIndex);
            var newElement = rendererListProp.GetArrayElementAtIndex(newIndex);
            newElement.objectReferenceValue = created;
            urpSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urpAsset);

            camData.SetRenderer(newIndex);
            EditorUtility.SetDirty(camData);

            assignedIndex = newIndex;
            note = $"Создан и назначен 2D Renderer: {created.name} (index {newIndex}).";
            return true;
        }

        private static bool TryCreate2DRendererAsset(
            UniversalRenderPipelineAsset urpAsset,
            out ScriptableRendererData created,
            out string note)
        {
            created = null;
            note = string.Empty;

            var renderer2DType = GetRenderer2DDataType();
            if (renderer2DType == null || !typeof(ScriptableRendererData).IsAssignableFrom(renderer2DType))
            {
                note = "Тип Renderer2DData не найден в текущей версии URP.";
                return false;
            }

            created = ScriptableObject.CreateInstance(renderer2DType) as ScriptableRendererData;
            if (created == null)
            {
                note = "Не удалось создать экземпляр Renderer2DData.";
                return false;
            }

            created.name = $"Auto_2D_Renderer_{urpAsset.name}";

            var urpPath = AssetDatabase.GetAssetPath(urpAsset);
            var urpDir = Path.GetDirectoryName(urpPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(urpDir))
            {
                UnityEngine.Object.DestroyImmediate(created);
                created = null;
                note = "Не удалось определить директорию URP Asset.";
                return false;
            }

            var rendererPath = AssetDatabase.GenerateUniqueAssetPath($"{urpDir}/{created.name}.asset");
            AssetDatabase.CreateAsset(created, rendererPath);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static string SyncRendererIndexAcrossUsedUrpAssets(int targetIndex, UniversalRenderPipelineAsset activeAsset)
        {
            if (targetIndex < 0)
                return string.Empty;

            var assets = GetUsedUrpAssets(activeAsset);
            var synced = 0;
            var skipped = 0;

            foreach (var asset in assets)
            {
                if (asset == null || asset == activeAsset)
                    continue;

                if (Ensure2DRendererAtIndex(asset, targetIndex))
                    synced++;
                else
                    skipped++;
            }

            if (synced == 0 && skipped == 0)
                return string.Empty;

            if (skipped > 0)
                return $"Синхронизация quality-pipeline: добавлено {synced}, пропущено {skipped}.";

            return $"Синхронизация quality-pipeline: добавлено {synced}.";
        }

        private static List<UniversalRenderPipelineAsset> GetUsedUrpAssets(UniversalRenderPipelineAsset activeAsset)
        {
            var assets = new List<UniversalRenderPipelineAsset>();

            void AddIfUrp(RenderPipelineAsset pipelineAsset)
            {
                if (pipelineAsset is UniversalRenderPipelineAsset urp && !assets.Contains(urp))
                    assets.Add(urp);
            }

            AddIfUrp(activeAsset);
            AddIfUrp(GraphicsSettings.currentRenderPipeline);

            var qualityNames = QualitySettings.names;
            for (var i = 0; i < qualityNames.Length; i++)
                AddIfUrp(QualitySettings.GetRenderPipelineAssetAt(i));

            return assets;
        }

        private static bool Ensure2DRendererAtIndex(UniversalRenderPipelineAsset urpAsset, int targetIndex)
        {
            var urpSo = new SerializedObject(urpAsset);
            var rendererListProp = urpSo.FindProperty("m_RendererDataList");
            if (rendererListProp == null || !rendererListProp.isArray || targetIndex < 0)
                return false;

            if (targetIndex < rendererListProp.arraySize)
            {
                var existingAtIndex = rendererListProp.GetArrayElementAtIndex(targetIndex).objectReferenceValue as ScriptableRendererData;
                if (Is2DRendererData(existingAtIndex))
                    return true;
                if (existingAtIndex != null)
                    return false;
            }

            while (rendererListProp.arraySize <= targetIndex)
            {
                var insertAt = rendererListProp.arraySize;
                rendererListProp.InsertArrayElementAtIndex(insertAt);
                var inserted = rendererListProp.GetArrayElementAtIndex(insertAt);
                inserted.objectReferenceValue = null;
            }

            if (!TryCreate2DRendererAsset(urpAsset, out var created, out _))
                return false;

            rendererListProp.GetArrayElementAtIndex(targetIndex).objectReferenceValue = created;
            urpSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urpAsset);
            return true;
        }

        private static bool Is2DRendererData(ScriptableRendererData data)
        {
            if (data == null)
                return false;

            var typeName = data.GetType().Name;
            return typeName.Contains("2D") || typeName.Contains("Renderer2DData");
        }

        private static void ConfigurePointLightNormalMap(Light2D pointLight)
        {
            if (pointLight == null)
                return;

            var lightSo = new SerializedObject(pointLight);

            var useNormalMapProp = lightSo.FindProperty("m_UseNormalMap");
            if (useNormalMapProp != null)
                useNormalMapProp.boolValue = true;

            var normalMapQualityProp = lightSo.FindProperty("m_NormalMapQuality");
            if (normalMapQualityProp != null && normalMapQualityProp.propertyType == SerializedPropertyType.Enum)
                normalMapQualityProp.enumValueIndex = Math.Max(normalMapQualityProp.enumValueIndex, 1);

            var normalMapDistanceProp = lightSo.FindProperty("m_NormalMapDistance");
            if (normalMapDistanceProp != null)
                normalMapDistanceProp.floatValue = Mathf.Max(normalMapDistanceProp.floatValue, 6f);

            lightSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pointLight);
        }

        private static Type GetRenderer2DDataType()
        {
            var type = Type.GetType("UnityEngine.Rendering.Universal.Renderer2DData, Unity.RenderPipelines.Universal.Runtime");
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("UnityEngine.Rendering.Universal.Renderer2DData");
                if (type != null)
                    return type;
            }

            return null;
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

