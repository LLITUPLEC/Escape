using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.Match3;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Arm before navigation; inspect every will-render callback in the first three distinct target frames.</summary>
    public static class ExpeditionStartupQa
    {
        [Serializable]
        private sealed class Frame
        {
            public int frame, callbacks, coverOrder, worldOrder, headerOrder, legacyOrder, legacyVisibleGraphics;
            public double firstCallbackMs, lastCallbackMs;
            public bool protectedAtEveryCallback = true;
            public bool coverOpaque, coverFullScreen, coverAboveLegacy, worldArtPresent, worldFullScreen, headerReady, legacyHidden;
            public float legacyGroupAlpha = -1;
            public string firstProtection, lastProtection, screenshot;
        }

        [Serializable]
        private sealed class Report
        {
            public string state, error, armedUtc, finishedUtc, targetScene = "MainMenu";
            public int armedFrame, sceneLoadedFrame = -1, screenWidth, screenHeight;
            public double sceneLoadedMs;
            public bool completed, passed, screenshotsWritten, directRpcSpendOrPlayerPrefs = false;
            public Frame[] frames;
        }

        private static readonly List<Frame> Frames = new List<Frame>();
        private static readonly Vector3[] Corners = new Vector3[4];
        private static Report _report;
        private static Scene _scene;
        private static bool _armed, _sceneArrived;
        private static double _started;
        private static string _root;

        /// <summary>Only arms observation. The caller may then perform its existing MainMenu navigation.</summary>
        public static string Arm()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Unpaused Play mode required");
            if (_armed) throw new InvalidOperationException("Startup probe already armed");
            foreach (var manager in UnityEngine.Object.FindObjectsByType<DuelMatch3Manager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (typeof(DuelMatch3Manager).GetField("_match", flags)?.GetValue(manager) != null ||
                    !string.IsNullOrEmpty(typeof(DuelMatch3Manager).GetField("_matchmakerTicket", flags)?.GetValue(manager) as string))
                    throw new InvalidOperationException("Refusing navigation while a real match or matchmaking ticket exists");
            }
            Frames.Clear(); _sceneArrived = false; _scene = default;
            _started = EditorApplication.timeSinceStartup;
            _root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/expedition"));
            Directory.CreateDirectory(Path.Combine(_root, "screenshots"));
            for (var i = 1; i <= 3; i++)
            {
                var file = Path.Combine(_root, "screenshots/startup-first-" + i.ToString("00") + ".png");
                if (File.Exists(file)) File.Delete(file);
            }
            _report = new Report { state = "armed", armedUtc = DateTime.UtcNow.ToString("O"), armedFrame = Time.frameCount,
                screenWidth = Screen.width, screenHeight = Screen.height };
            _armed = true;
            SceneManager.sceneLoaded += SceneLoaded;
            Canvas.willRenderCanvases += BeforeRender;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Save();
            return "Armed before MainMenu load; observe .utmp/expedition/startup-qa.json. No RPC/spend/PlayerPrefs calls.";
        }

        /// <summary>Arms first, then performs only ordinary MainMenu navigation.</summary>
        public static string StartProbe()
        {
            var message = Arm();
            try { SceneManager.LoadScene("MainMenu", LoadSceneMode.Single); }
            catch (Exception error) { Finish(error.ToString()); throw; }
            return message;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_armed || scene.name != "MainMenu") return;
            _scene = scene; _sceneArrived = true;
            _report.sceneLoadedFrame = Time.frameCount;
            _report.sceneLoadedMs = ElapsedMs;
            _report.state = "observing_first_renders";
            Save();
        }

        private static double ElapsedMs => (EditorApplication.timeSinceStartup - _started) * 1000;

        private static void BeforeRender()
        {
            if (!_armed || !_sceneArrived || !_scene.IsValid() || !_scene.isLoaded) return;
            try
            {
                Frame sample = null;
                foreach (var existing in Frames) if (existing.frame == Time.frameCount) { sample = existing; break; }
                if (sample == null)
                {
                    if (Frames.Count == 3) return;
                    sample = new Frame { frame = Time.frameCount, firstCallbackMs = ElapsedMs };
                    Frames.Add(sample);
                    sample.screenshot = ".utmp/expedition/screenshots/startup-first-" + Frames.Count.ToString("00") + ".png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(_root, "screenshots/startup-first-" + Frames.Count.ToString("00") + ".png"));
                }
                sample.callbacks++; sample.lastCallbackMs = ElapsedMs;
                Inspect(sample);
                var protectedNow = (sample.coverOpaque && sample.coverFullScreen && sample.coverAboveLegacy) ||
                    (sample.worldArtPresent && sample.worldFullScreen && sample.headerReady && sample.legacyHidden && sample.legacyVisibleGraphics == 0);
                sample.protectedAtEveryCallback &= protectedNow;
                var protection = sample.coverOpaque && sample.coverFullScreen && sample.coverAboveLegacy ? "opaque_startup_cover" :
                    protectedNow ? "complete_world_header_and_hidden_legacy" : "UNPROTECTED";
                if (sample.callbacks == 1) sample.firstProtection = protection;
                sample.lastProtection = protection;
                if (!protectedNow) Finish("An unprotected MainMenu will-render callback was observed at frame " + Time.frameCount);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static void Inspect(Frame sample)
        {
            var menu = InScene<MainMenuController>();
            var legacy = menu != null ? menu.transform : Find("MainMenuHUD");
            var legacyCanvas = legacy != null ? legacy.GetComponentInParent<Canvas>()?.rootCanvas : null;
            var group = legacy != null ? legacy.GetComponent<CanvasGroup>() : null;
            sample.legacyOrder = legacyCanvas != null ? legacyCanvas.sortingOrder : 0;
            sample.legacyGroupAlpha = group != null ? group.alpha : -1;
            sample.legacyHidden = legacy != null && (!legacy.gameObject.activeInHierarchy || group != null && group.alpha <= .001f);
            sample.legacyVisibleGraphics = 0;
            if (legacy != null)
                foreach (var graphic in legacy.GetComponentsInChildren<Graphic>(false))
                    if (graphic.isActiveAndEnabled && graphic.color.a * graphic.canvasRenderer.GetInheritedAlpha() > .001f)
                        sample.legacyVisibleGraphics++;

            var cover = Find("ExpeditionStartupCover");
            var coverCanvas = cover != null ? cover.GetComponent<Canvas>() : null;
            var opaque = cover != null ? cover.Find("OpaqueCover")?.GetComponent<Image>() : null;
            var coverLive = LiveOverlay(coverCanvas);
            sample.coverOrder = coverCanvas != null ? coverCanvas.sortingOrder : -1;
            sample.coverOpaque = coverLive && Solid(opaque);
            sample.coverFullScreen = coverLive && opaque != null && CoversPhysicalScreen(opaque.rectTransform);
            sample.coverAboveLegacy = coverLive && (legacyCanvas == null || legacyCanvas.renderMode != RenderMode.ScreenSpaceOverlay ||
                coverCanvas.sortingLayerID == legacyCanvas.sortingLayerID && coverCanvas.sortingOrder > legacyCanvas.sortingOrder);

            var world = InScene<ExpeditionSceneWorld>();
            var worldCanvas = world != null ? world.GetComponent<Canvas>() : null;
            var worldFill = world != null ? world.transform.Find("WorldFill")?.GetComponent<Image>() : null;
            sample.worldOrder = worldCanvas != null ? worldCanvas.sortingOrder : -1;
            sample.worldArtPresent = world != null && world.gameObject.activeInHierarchy && world.Illustration != null &&
                world.Illustration.isActiveAndEnabled && world.Illustration.sprite != null && world.Illustration.color.a > .99f;
            sample.worldFullScreen = LiveOverlay(worldCanvas) && Solid(worldFill) && CoversPhysicalScreen(worldFill.rectTransform);
            var header = Find("ExpeditionResourceHeader");
            var headerCanvas = header != null ? header.GetComponentInParent<Canvas>()?.rootCanvas : null;
            sample.headerOrder = headerCanvas != null ? headerCanvas.sortingOrder : -1;
            var headerImage = header != null ? header.GetComponent<Image>() : null;
            sample.headerReady = LiveOverlay(headerCanvas) && header != null && header.gameObject.activeInHierarchy &&
                headerImage != null && headerImage.isActiveAndEnabled && headerImage.sprite != null &&
                headerImage.canvasRenderer.GetInheritedAlpha() > .99f && worldCanvas != null &&
                headerCanvas.sortingLayerID == worldCanvas.sortingLayerID && headerCanvas.sortingOrder > worldCanvas.sortingOrder;
        }

        private static bool LiveOverlay(Canvas canvas) => canvas != null && canvas.isActiveAndEnabled && canvas.isRootCanvas &&
            canvas.renderMode == RenderMode.ScreenSpaceOverlay;

        private static bool Solid(Image image) => image != null && image.isActiveAndEnabled && image.sprite == null &&
            image.color.a * image.canvasRenderer.GetInheritedAlpha() >= .999f;

        private static bool CoversPhysicalScreen(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (var i = 0; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(camera, Corners[i]);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return min.x <= 1 && min.y <= 1 && max.x >= Screen.width - 1 && max.y >= Screen.height - 1;
        }

        private static T InScene<T>() where T : Component
        {
            foreach (var root in _scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<T>(true)) return component;
            return null;
        }
        private static Transform Find(string name)
        {
            foreach (var root in _scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child;
            return null;
        }

        private static void Tick()
        {
            if (!_armed) return;
            if (!EditorApplication.isPlaying) { Finish("Play mode ended before three rendered frames"); return; }
            if (EditorApplication.timeSinceStartup - _started > 20) { Finish("Startup observation or PNG write exceeded 20 seconds"); return; }
            if (Frames.Count != 3 || Time.frameCount <= Frames[2].frame) return;
            Canvas.willRenderCanvases -= BeforeRender; // All callbacks of each of the first three frames have now been inspected.
            if (Time.frameCount < Frames[2].frame + 2) return;
            foreach (var sample in Frames)
            {
                var file = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + sample.screenshot));
                if (!File.Exists(file) || new FileInfo(file).Length == 0) return;
            }
            _report.screenshotsWritten = true;
            Finish(null);
        }

        private static void BeforeReload() { if (_armed) Finish("Domain reload interrupted startup observation"); }
        private static void Finish(string error)
        {
            if (!_armed) return;
            _armed = false;
            SceneManager.sceneLoaded -= SceneLoaded;
            Canvas.willRenderCanvases -= BeforeRender;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            _report.completed = true; _report.error = error;
            _report.passed = error == null && Frames.Count == 3 && _report.screenshotsWritten;
            _report.state = _report.passed ? "passed" : "failed";
            _report.finishedUtc = DateTime.UtcNow.ToString("O");
            Save();
        }
        private static void Save()
        {
            _report.frames = Frames.ToArray();
            File.WriteAllText(Path.Combine(_root, "startup-qa.json"), JsonUtility.ToJson(_report, true));
        }
    }
}
