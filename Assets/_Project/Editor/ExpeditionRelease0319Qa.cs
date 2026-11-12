using System;
using System.IO;
using System.Reflection;
using Project.Character;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Exercises the production arena gate, Shelter shortcut and daily reward views offline.</summary>
    public static class ExpeditionRelease0319Qa
    {
        private const string Output = ".cache/qa/release0319/ui";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            VerifyArenaAndShortcuts();
            Debug.Log(ExpeditionShelterConceptCQa.Run(Output));
            Debug.Log(ExpeditionDailyLoginQa0313.Run("capture"));
            Debug.Log("PASS 0.3.19 arena and Shelter QA, including locked and unlocked states");
        }

        private static void VerifyArenaAndShortcuts()
        {
            var previous = SceneManager.GetActiveScene();
            var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "ArenaMenu";
            SceneManager.SetActiveScene(scene);
            var current = typeof(PlayerGuidanceService).GetField("_current", Static);
            var scope = typeof(PlayerGuidanceService).GetField("_scope", Static);
            var oldCurrent = current.GetValue(null);
            var oldScope = scope.GetValue(null);
            RenderTexture target = null;
            try
            {
                scope.SetValue(null, LocalAccountCacheScope.Current);
                current.SetValue(null, new PlayerGuidanceService.State { ok = true, schema = 1, mine_wins = 9, tutorial_status = "completed" });
                var canvas = new GameObject("ArenaQaCanvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 70;
                var content = ExpeditionUi.Rect(canvas.transform, "ArenaContent", 0, 0, 1, 1);
                var modes = ExpeditionUi.Rect(content, "ModePanel", 0, 0, 1, 1);
                var names = new[] { "match3Button", "match3ProButton", "match3Arena_Race", "match3Arena", "match3Arena_Ore", "match3Arena_Gold" };
                var cards = Array.ConvertAll(names, name => ExpeditionUi.Button(modes, name, "", 0, 0, 1, 1, null));
                var owner = new GameObject("ArenaQaPresenter");
                owner.SetActive(false);
                var presentation = owner.AddComponent<ExpeditionPresentation>();
                var type = typeof(ExpeditionPresentation);
                type.GetField("_scene", Instance).SetValue(presentation, scene);
                type.GetField("_content", Instance).SetValue(presentation, content);
                type.GetField("_sceneBody", Instance).SetValue(presentation, content);
                type.GetMethod("BuildArena", Instance).Invoke(presentation, null);
                var courtyard = content.Find("ArenaCourtyard");
                Check(courtyard != null && courtyard.GetSiblingIndex() < modes.GetSiblingIndex(),
                    "Courtyard art covers the arena gates.");
                for (var i = 0; i < cards.Length; i++)
                {
                    var shape = i < 2 ? ExpeditionArenaGateFrame.Shape.Arch : i == 2
                        ? ExpeditionArenaGateFrame.Shape.Shaft : ExpeditionArenaGateFrame.Shape.Shield;
                    Check(cards[i].targetGraphic is ExpeditionArenaGateFrame frame && frame.Silhouette == shape && frame.raycastTarget,
                        "Arena gate has the wrong entrance shape or lost its hit surface: " + names[i]);
                }

                foreach (var caption in new[] { "Рейтинг", "Друзья", "Почта" })
                {
                    var shortcut = ExpeditionShelterShortcut.Create(content, "ShortcutProbe", caption, "shelter-mail", 0, .3f, null);
                    var label = shortcut.transform.Find("Caption").GetComponent<TMP_Text>();
                    Check(label.horizontalAlignment == HorizontalAlignmentOptions.Center, "Shortcut label is not centered: " + caption);
                    UnityEngine.Object.DestroyImmediate(shortcut.gameObject);
                }

                var camera = new GameObject("ArenaQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 960;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                    foreach (var native in root.GetComponentsInChildren<Canvas>(true))
                    {
                        native.renderMode = RenderMode.ScreenSpaceCamera;
                        native.worldCamera = camera;
                        native.planeDistance = 1;
                        native.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    }
                }
                foreach (var card in cards) card.transform.Find("OfflinePlayCover").GetComponent<Image>().enabled = false;
                Canvas.ForceUpdateCanvases();
                Check(cards[0].IsInteractable() && cards[1].IsInteractable() && cards[2].IsInteractable(), "Base arena modes were disabled by the tournament gate.");
                for (var i = 3; i < cards.Length; i++)
                {
                    var card = cards[i];
                    var label = card.transform.Find("ExpeditionModeDescription").GetComponent<TMP_Text>();
                    label.ForceMeshUpdate();
                    Check(!card.IsInteractable() && card.GetComponent<CanvasGroup>().alpha == 1, "Locked card lost its opaque frame or click gate.");
                    Check(label.text.Contains("10 побед в шахте") && !label.isTextOverflowing, "Unlock requirement is missing or clipped.");
                }
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
                try
                {
                    pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Output + "/arena-locked.png", pixels.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); }

                current.SetValue(null, new PlayerGuidanceService.State { ok = true, schema = 1, mine_wins = 10, tournaments_unlocked = true, tutorial_status = "completed" });
                for (var i = 3; i < cards.Length; i++)
                {
                    var card = cards[i];
                    typeof(ProgressiveContentButton).GetMethod("Refresh", Instance).Invoke(card.GetComponent<ProgressiveContentButton>(), null);
                    var label = card.transform.Find("ExpeditionModeDescription").GetComponent<TMP_Text>();
                    Check(card.IsInteractable() && !label.text.Contains("10 побед в шахте"), "Original tournament description or click state did not return.");
                }
            }
            finally
            {
                current.SetValue(null, oldCurrent);
                scope.SetValue(null, oldScope);
                RenderTexture.active = previousTarget;
                EditorSceneManager.CloseScene(scene, true);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                SceneManager.SetActiveScene(previous);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
