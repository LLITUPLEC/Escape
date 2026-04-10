using System.Collections.Generic;
using System.IO;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Project.UI.Editor
{
    public static class TorchPrefabCreator
    {
        private const string PrefabsDir = "Assets/_Project/Prefabs/Environment";
        private const string PrefabPath = PrefabsDir + "/Torch_Base.prefab";

        private const string TorchBaseSpritePath = "Assets/_Project/Shaders/Particles/flame b.png";
        private const string TorchFlameSheetPath = "Assets/_Project/Shaders/Particles/flame n.png";

        [MenuItem("Tools/VFX/Создать префаб факела (Torch_Base)")]
        public static void CreateTorchPrefab()
        {
            EnsureDir(PrefabsDir);

            var baseSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TorchBaseSpritePath);
            var flameSprites = LoadSortedFlameSprites(TorchFlameSheetPath);

            var root = new GameObject("Torch_Base", typeof(SpriteRenderer));
            ConfigureTorchBase(root.GetComponent<SpriteRenderer>(), baseSprite);

            var fire = CreateTorchFire(root.transform, flameSprites);
            var lightRoot = CreateTorchLight(root.transform);

            // Немного подправим относительные позиции для более читаемого результата.
            fire.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            lightRoot.transform.localPosition = new Vector3(0f, 0.26f, 0f);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
        }

        private static void ConfigureTorchBase(SpriteRenderer sr, Sprite baseSprite)
        {
            if (sr == null)
                return;

            sr.sprite = baseSprite;
            sr.color = Color.white;
            sr.sortingOrder = 20;
        }

        private static GameObject CreateTorchFire(Transform parent, IReadOnlyList<Sprite> flameSprites)
        {
            var go = new GameObject("Torch_Fire", typeof(ParticleSystem), typeof(ParticleSystemRenderer));
            go.transform.SetParent(parent, false);

            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.48f;
            main.startSpeed = 0.05f;
            main.startSize = 0.62f;
            main.maxParticles = 1;
            main.startColor = new Color(1f, 0.82f, 0.56f, 0.92f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 1, 1, short.MaxValue, main.startLifetime.constant)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.045f;
            shape.length = 0.02f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.35f, 1f),
                    new Keyframe(1f, 0.15f)));

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
                new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(new Color(1f, 0.88f, 0.55f), 0f),
                        new GradientColorKey(new Color(1f, 0.47f, 0.1f), 0.55f),
                        new GradientColorKey(new Color(0.22f, 0.04f, 0.02f), 1f),
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.95f, 0.1f),
                        new GradientAlphaKey(0.7f, 0.8f),
                        new GradientAlphaKey(0f, 1f),
                    }
                });

            var textureSheet = ps.textureSheetAnimation;
            textureSheet.enabled = flameSprites != null && flameSprites.Count > 0;
            if (textureSheet.enabled)
            {
                textureSheet.mode = ParticleSystemAnimationMode.Sprites;
                textureSheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                textureSheet.fps = 14f;
                textureSheet.cycleCount = 1;
                textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
                textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                    flameSprites.Count - 0.01f,
                    AnimationCurve.Linear(0f, 0f, 1f, 1f));

                for (var i = 0; i < flameSprites.Count; i++)
                    textureSheet.AddSprite(flameSprites[i]);
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 30;
            renderer.alignment = ParticleSystemRenderSpace.Facing;

            return go;
        }

        private static GameObject CreateTorchLight(Transform parent)
        {
            var go = new GameObject("Torch_Light", typeof(Light2D), typeof(TorchFlicker));
            go.transform.SetParent(parent, false);

            var light2D = go.GetComponent<Light2D>();
            light2D.lightType = Light2D.LightType.Point;
            light2D.color = new Color(1f, 0.68f, 0.32f, 1f);
            light2D.intensity = 1.05f;
            light2D.pointLightInnerRadius = 0.12f;
            light2D.pointLightOuterRadius = 2.6f;
            light2D.falloffIntensity = 0.55f;
            light2D.overlapOperation = Light2D.OverlapOperation.AlphaBlend;
            light2D.shadowIntensity = 0f;

            return go;
        }

        private static List<Sprite> LoadSortedFlameSprites(string texturePath)
        {
            var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath);
            var sprites = new List<Sprite>();
            foreach (var a in assets)
            {
                if (a is Sprite s)
                    sprites.Add(s);
            }

            sprites.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;

                if (Mathf.Abs(a.rect.x - b.rect.x) > 0.01f)
                    return a.rect.x < b.rect.x ? -1 : 1;
                if (Mathf.Abs(a.rect.y - b.rect.y) > 0.01f)
                    return a.rect.y < b.rect.y ? -1 : 1;
                return string.CompareOrdinal(a.name, b.name);
            });

            return sprites;
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

