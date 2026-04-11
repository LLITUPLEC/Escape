using System.IO;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI.Editor
{
    public static class MineSceneLightingConfigurator
    {
        private const string MineScenePath = "Assets/_Project/Scenes/MineScene.unity";
        private const string LightingMaterialPath = "Assets/_Project/Materials/UI/FloorTorchLighting.mat";
        private const string LightingShaderPath = "Assets/_Project/Shaders/UI/MineFloorTorchLit.shader";
        private const string LightingShaderName = "UI/Mine Floor Torch Lit";

        [MenuItem("Tools/VFX/Настроить MineScene под 2D свет")]
        public static void ConfigureMineSceneLighting()
        {
            if (!EnsureMineSceneOpen())
                return;

            var lightingMaterial = EnsureLightingMaterial();
            var configured = 0;
            for (var floorIndex = 1; floorIndex <= 12; floorIndex++)
            {
                if (ConfigureSingleFloor(floorIndex, lightingMaterial))
                    configured++;
            }

            if (configured == 0)
            {
                Debug.LogError("[MineLighting] Не найдено ни одного объекта Floor_1..Floor_12.");
                return;
            }

            var selected = GameObject.Find("Floor_1");
            if (selected != null)
                Selection.activeGameObject = selected;
            Debug.Log($"[MineLighting] Настроено этажей: {configured} (Floor_1..Floor_12).");
        }

        private static bool ConfigureSingleFloor(int floorIndex, Material lightingMaterial)
        {
            var floorName = "Floor_" + floorIndex;
            var floor = GameObject.Find(floorName);
            if (floor == null)
                return false;

            Undo.RegisterFullObjectHierarchyUndo(floor, "Configure " + floorName + " Lighting");

            var floorRect = floor.GetComponent<RectTransform>();
            var floorImage = floor.GetComponent<Image>();
            if (floorRect == null || floorImage == null)
            {
                Debug.LogWarning("[MineLighting] " + floorName + " пропущен: нужен RectTransform + Image.");
                return false;
            }

            floorImage.color = Color.white;
            floorImage.material = lightingMaterial;

            EnsureRectMask2D(floor);

            var fxRoot = EnsureChild(floor.transform, "LightingFx", typeof(RectTransform)).GetComponent<RectTransform>();
            StretchToParent(fxRoot);
            fxRoot.SetSiblingIndex(0);

            CleanupLegacyOverlay(fxRoot);

            var leftLight = EnsureTorchAnchor(fxRoot, "TorchLightLeft", 0.07f);
            var rightLight = EnsureTorchAnchor(fxRoot, "TorchLightRight", 0.93f);

            SetupFloorLightingDriver(floorIndex, floor, floorImage, lightingMaterial, leftLight, rightLight);
            EditorSceneManager.MarkSceneDirty(floor.scene);
            return true;
        }

        private static bool EnsureMineSceneOpen()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.path == MineScenePath)
                return true;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MineScenePath) == null)
            {
                Debug.LogWarning("[MineLighting] MineScene не найден, настройка будет применена к активной сцене.");
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            EditorSceneManager.OpenScene(MineScenePath, OpenSceneMode.Single);
            return true;
        }

        private static void EnsureRectMask2D(GameObject go)
        {
            if (go.GetComponent<RectMask2D>() == null)
                Undo.AddComponent<RectMask2D>(go);
        }

        private static GameObject EnsureChild(Transform parent, string childName, params System.Type[] components)
        {
            var child = parent.Find(childName);
            if (child != null)
                return child.gameObject;

            var go = new GameObject(childName, components);
            Undo.RegisterCreatedObjectUndo(go, "Create " + childName);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private static RectTransform EnsureTorchAnchor(RectTransform parent, string name, float anchorX)
        {
            var go = EnsureChild(parent, name, typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();
            if (image == null)
                image = Undo.AddComponent<Image>(go);

            image.sprite = null;
            image.raycastTarget = false;
            image.color = new Color(0f, 0f, 0f, 0f);
            image.material = null;

            RemoveComponentIfExists<UiTorchLightPulse>(go);
            var rt = go.GetComponent<RectTransform>();
            ConfigureLightRect(rt, anchorX);
            return rt;
        }

        private static void ConfigureLightRect(RectTransform rt, float anchorX)
        {
            rt.anchorMin = new Vector2(anchorX, 0.5f);
            rt.anchorMax = new Vector2(anchorX, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(32f, 32f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private static void SetupFloorLightingDriver(
            int floorIndex,
            GameObject floor,
            Image floorImage,
            Material lightingMaterial,
            RectTransform leftLight,
            RectTransform rightLight)
        {
            var driver = floor.GetComponent<UiFloorTorchLighting>();
            if (driver == null)
                driver = Undo.AddComponent<UiFloorTorchLighting>(floor);

            var so = new SerializedObject(driver);
            so.FindProperty("targetImage").objectReferenceValue = floorImage;
            so.FindProperty("lightingMaterial").objectReferenceValue = lightingMaterial;
            so.FindProperty("torchLightLeft").objectReferenceValue = leftLight;
            so.FindProperty("torchLightRight").objectReferenceValue = rightLight;
            var ambient = Mathf.Clamp01(0.36f - (floorIndex - 1) * 0.03f);
            so.FindProperty("ambient").floatValue = ambient;
            so.FindProperty("followTorchAnchors").boolValue = false;
            so.FindProperty("manualLightLeftUv").vector2Value = new Vector2(0.24f, 0.5f);
            so.FindProperty("manualLightRightUv").vector2Value = new Vector2(0.65f, 0.5f);
            so.FindProperty("minIntensity").floatValue = 0.52f;
            so.FindProperty("maxIntensity").floatValue = 0.86f;
            so.FindProperty("minRadius").floatValue = 0.33f;
            so.FindProperty("maxRadius").floatValue = 0.66f;
            so.FindProperty("softness").floatValue = 2.1f;
            so.FindProperty("pulseSpeed").floatValue = 1.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);
        }

        private static void CleanupLegacyOverlay(RectTransform fxRoot)
        {
            var darkOverlay = fxRoot.Find("DarkOverlay");
            if (darkOverlay != null)
            {
                Undo.DestroyObjectImmediate(darkOverlay.gameObject);
            }
        }

        private static Material EnsureLightingMaterial()
        {
            EnsureDir(Path.GetDirectoryName(LightingMaterialPath)?.Replace("\\", "/"));
            var shader = Shader.Find(LightingShaderName);
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>(LightingShaderPath);
            if (shader == null)
                throw new UnityException("[MineLighting] Не найден шейдер UI/Mine Floor Torch Lit.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(LightingMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "FloorTorchLighting" };
                AssetDatabase.CreateAsset(mat, LightingMaterialPath);
            }

            if (mat.shader != shader)
                mat.shader = shader;
            mat.SetFloat("_Ambient", 0.20f);
            mat.SetColor("_LightColor", new Color(1f, 0.85f, 0.58f, 1f));
            mat.SetFloat("_LightSoftness", 2.1f);
            mat.SetVector("_Light1", new Vector4(0.07f, 0.5f, 0.24f, 0.75f));
            mat.SetVector("_Light2", new Vector4(0.93f, 0.5f, 0.24f, 0.75f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void RemoveComponentIfExists<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null)
                Undo.DestroyObjectImmediate(c);
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
