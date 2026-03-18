using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Project.Editor
{
    public static class AssignPlayerModelAndAnimations
    {
        private const string MaterialsPath = "Assets/_Project/Materials";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string AnimationsFolder = "Assets/_Project/Animations";
        private const string ControllerPath = AnimationsFolder + "/Player.controller";

        [MenuItem("Tools/Project/Assign Player Model + Animations (from _Project/Materials)")]
        public static void Assign()
        {
            EnsureFolder("Assets/_Project", "Animations");

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                EditorUtility.DisplayDialog("Ошибка", $"Не найден Player prefab: {PlayerPrefabPath}", "OK");
                return;
            }

            var modelGuid = AssetDatabase.FindAssets("t:Model", new[] { MaterialsPath }).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(modelGuid))
            {
                EditorUtility.DisplayDialog("Ошибка", $"Не найден FBX/Model в {MaterialsPath}", "OK");
                return;
            }

            var modelPath = AssetDatabase.GUIDToAssetPath(modelGuid);
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
            {
                EditorUtility.DisplayDialog("Ошибка", $"Не удалось загрузить модель: {modelPath}", "OK");
                return;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var idle = PickClip(clips, "idle", "stand", "стой", "idle_");
            var walk = PickClip(clips, "walk", "walking", "ход");
            var run = PickClip(clips, "run", "running", "бег");
            var jump = PickClip(clips, "jump", "прыж");

            if (idle == null || walk == null || run == null || jump == null)
            {
                var names = clips.Length == 0 ? "(нет клипов)" : string.Join(", ", clips.Select(c => c.name));
                EditorUtility.DisplayDialog(
                    "Не найдены клипы",
                    "Не смог автоматически определить Idle/Walk/Run/Jump в модели.\n" +
                    $"Клипы: {names}\n\n" +
                    "Переименуй клипы или пришли список имён — я настрою подбор.",
                    "OK");
                return;
            }

            // Очень частый случай: клипы в FBX не зациклены => после пары метров проигрываются 1 раз и замирают.
            // Включаем Loop Time для Idle/Walk/Run и реимпортим.
            if (EnsureLoopSettings(modelPath, loopOn: new[] { idle.name, walk.name, run.name }, loopOff: new[] { jump.name }))
            {
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<AnimationClip>()
                    .Where(c => c != null && !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                idle = PickClip(clips, idle.name);
                walk = PickClip(clips, walk.name);
                run = PickClip(clips, run.name);
                jump = PickClip(clips, jump.name);
            }

            var controller = EnsureController(idle, walk, run, jump);

            // Правим prefab через Prefab Contents API.
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                // Отключаем капсулу-рендер, если он есть (коллайдер у нас CharacterController).
                var mr = root.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;

                // Удаляем прошлый Visual (если был).
                var oldVisual = root.transform.Find("Visual");
                if (oldVisual != null) UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);

                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);

                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                modelInstance.transform.SetParent(visual.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                // Предпочитаем Animator внутри модели (обычно Avatar/rig находится там).
                // Если его нет — добавим на корень модели.
                var modelAnimator = modelInstance.GetComponentInChildren<Animator>();
                if (modelAnimator == null) modelAnimator = modelInstance.AddComponent<Animator>();
                modelAnimator.runtimeAnimatorController = controller;
                modelAnimator.applyRootMotion = false;

                // Подцепим Avatar из модели, если он есть.
                var avatar = modelAnimator.avatar;
                if (avatar != null)
                {
                    modelAnimator.avatar = avatar;
                }

                // Если на root уже был Animator, удалим, чтобы не было конфликтов с Avatar/контроллером.
                var rootAnimator = root.GetComponent<Animator>();
                if (rootAnimator != null)
                {
                    UnityEngine.Object.DestroyImmediate(rootAnimator);
                }

                // Драйвит анимации для удалённых игроков (скорость/прыжок/grounded по transform).
                var driver = root.GetComponent<Project.Player.PlayerAnimatorDriver>();
                if (driver == null) root.AddComponent<Project.Player.PlayerAnimatorDriver>();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Готово", "Игроку назначена модель и AnimatorController.\nПроверь Player.prefab.", "OK");
        }

        private static bool EnsureLoopSettings(string modelPath, string[] loopOn, string[] loopOff)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return false;

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0) return false;

            var changed = false;
            for (var i = 0; i < clips.Length; i++)
            {
                var name = clips[i].name;
                if (loopOn != null && loopOn.Any(n => string.Equals(n, name, StringComparison.Ordinal)))
                {
                    if (!clips[i].loopTime)
                    {
                        clips[i].loopTime = true;
                        changed = true;
                    }
                }

                if (loopOff != null && loopOff.Any(n => string.Equals(n, name, StringComparison.Ordinal)))
                {
                    if (clips[i].loopTime)
                    {
                        clips[i].loopTime = false;
                        changed = true;
                    }
                }
            }

            if (!changed) return false;

            importer.clipAnimations = clips;
            EditorUtility.SetDirty(importer);
            return true;
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static AnimationClip PickClip(AnimationClip[] clips, params string[] keywords)
        {
            foreach (var k in keywords)
            {
                var c = clips.FirstOrDefault(x => string.Equals(x.name, k, StringComparison.OrdinalIgnoreCase));
                if (c != null) return c;

                c = clips.FirstOrDefault(x => x.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                if (c != null) return c;
            }
            return clips.FirstOrDefault();
        }

        private static AnimatorController EnsureController(AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip jump)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // Очистим слои/стейтмашину до одного blend tree.
            var layer = controller.layers[0];
            var sm = layer.stateMachine;
            sm.states = Array.Empty<ChildAnimatorState>();
            sm.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
            sm.entryTransitions = Array.Empty<AnimatorTransition>();

            // Параметр скорости 0..1
            if (controller.parameters.All(p => p.name != "Speed"))
            {
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            }
            if (controller.parameters.All(p => p.name != "Jump"))
            {
                controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            }
            if (controller.parameters.All(p => p.name != "Grounded"))
            {
                controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            }

            var locomotion = sm.AddState("Locomotion", new Vector3(300, 120, 0));
            locomotion.motion = CreateOrUpdateBlendTree(controller, idle, walk, run);
            sm.defaultState = locomotion;

            var jumpState = sm.AddState("Jump", new Vector3(520, 60, 0));
            jumpState.motion = jump;

            // AnyState -> Jump по триггеру
            var anyToJump = sm.AddAnyStateTransition(jumpState);
            anyToJump.hasExitTime = false;
            anyToJump.hasFixedDuration = true;
            anyToJump.duration = 0.05f;
            anyToJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            // Jump -> Locomotion по выходному времени
            var jumpToLoc = jumpState.AddTransition(locomotion);
            // Возвращаемся в locomotion только когда приземлились.
            jumpToLoc.hasExitTime = false;
            jumpToLoc.hasFixedDuration = true;
            jumpToLoc.duration = 0.08f;
            jumpToLoc.AddCondition(AnimatorConditionMode.If, 0.5f, "Grounded");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static BlendTree CreateOrUpdateBlendTree(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            // Один 1D blend tree по Speed: 0 idle, 0.5 walk, 1 run.
            var tree = new BlendTree
            {
                name = "LocomotionTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed"
            };

            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.5f);
            tree.AddChild(run, 1f);

            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }
    }
}

