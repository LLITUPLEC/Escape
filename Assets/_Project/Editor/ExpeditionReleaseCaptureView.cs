using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Editor viewport ownership only. Never edits scene objects, gameplay, preferences or network state.</summary>
    public static class ExpeditionReleaseCaptureView
    {
        private const string Key = "Escape.ReleaseCaptureView.";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static int _remaining;
        private static double _next;
        [Serializable] private sealed class Zoom { public float x, y; }
        [Serializable] private sealed class ViewInfo { public string type, title, target, renderTarget, texture, simulationState; public int id, selectedSize; public Rect position; }
        [Serializable] private sealed class Report
        {
            public string checkedUtc, mainView, focusedWindow, safeArea, mainDisplay, error;
            public int width, height;
            public bool playing, ready1080;
            public ViewInfo[] views;
            public string[] canvases, cameras;
        }
        [Serializable] private sealed class CaptureInfo
        {
            public string capturedUtc, scene, source, textureName, path;
            public int width, height, screenWidth, screenHeight, frame;
            public bool nativeWholeTexture = true, croppedOrResampled, gameplayOrAccountActions, readbackYNormalized;
        }

        public static string Run(string mode)
        {
            if (!string.IsNullOrEmpty(mode) && mode.StartsWith("capture-", StringComparison.Ordinal)) return Capture(mode.Substring(8));
            if (mode == "restore") return Restore();
            if (mode == "size" || mode == "fix")
            {
                var view = GameView(true);
                Save(view);
                FixNow(view);
                _remaining = 5; _next = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Settle;
                EditorApplication.update += Settle;
                return "Actual GameView takes viewport ownership after Play startup; Simulator shims yield on their normal Update path. Five editor frames scheduled; request status before capture.";
            }
            return Diagnose();
        }

        /// <summary>Reads the complete native Game view, normalizing its graphics-API row origin for PNG. No camera render, crop, resampling or UI changes.</summary>
        public static string Capture(string tag)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Actual runtime capture requires Play mode.");
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("publish-", StringComparison.Ordinal) || tag.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
                throw new ArgumentException("Use a publish- prefixed capture name with letters, numbers and hyphens.");
            var view = GameView(false);
            if (view == null) throw new InvalidOperationException("A rendered Game view is required.");
            var texture = PlayType.GetField("m_TargetTexture", Instance)?.GetValue(view) as RenderTexture;
            if (texture == null || !texture.IsCreated() || texture.width != 1080 || texture.height != 1920)
                throw new InvalidOperationException("Expected the actual 1080x1920 GameView render texture; observed " + (texture != null ? texture.width + "x" + texture.height : "none") + ".");
            var previous = RenderTexture.active;
            var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
                // The native Editor GameView RT uses a top-origin on D3D, unlike
                // ordinary Camera.Render fixtures. PNG readback needs the same
                // orientation the Game view uses on screen. Reorder whole rows;
                // every pixel value and the full native rectangle are preserved.
                var normalizeY = SystemInfo.graphicsUVStartsAtTop;
                if (normalizeY)
                {
                    var values = pixels.GetPixels32(); var row = new Color32[texture.width];
                    for (var y = 0; y < texture.height / 2; y++)
                    {
                        var opposite = (texture.height - 1 - y) * texture.width;
                        Array.Copy(values, y * texture.width, row, 0, texture.width);
                        Array.Copy(values, opposite, values, y * texture.width, texture.width);
                        Array.Copy(row, 0, values, opposite, texture.width);
                    }
                    pixels.SetPixels32(values); pixels.Apply();
                }
                var directory = Path.GetFullPath(".cache/qa/bridge/screenshots"); Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, tag + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG());
                File.WriteAllText(Path.Combine(directory, tag + ".json"), JsonUtility.ToJson(new CaptureInfo
                {
                    capturedUtc = DateTime.UtcNow.ToString("O"), scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    source = "UnityEditor.GameView / PlayModeView.m_TargetTexture / complete native rendered pixels",
                    textureName = texture.name, path = path, width = texture.width, height = texture.height,
                    screenWidth = Screen.width, screenHeight = Screen.height, frame = Time.frameCount, readbackYNormalized = normalizeY
                }, true));
                return path;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static Type PlayType => typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PlayModeView", true);
        private static EditorWindow MainView() => PlayType.GetMethod("GetMainPlayModeView", Static).Invoke(null, null) as EditorWindow;
        private static EditorWindow GameView(bool create)
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            var saved = SessionState.GetInt(Key + "viewId", 0);
            var views = Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().ToArray();
            var value = views.FirstOrDefault(v => v.GetInstanceID() == saved) ?? views.FirstOrDefault();
            return value != null || !create ? value : EditorWindow.GetWindow(type);
        }

        private static object Sizes()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizes", true);
            return typeof(ScriptableSingleton<>).MakeGenericType(type).GetProperty("instance", Static).GetValue(null);
        }
        private static object Group(object sizes, object value) => sizes.GetType().GetMethod("GetGroup", Instance).Invoke(sizes, new[] { value });

        private static void Save(EditorWindow view)
        {
            if (SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", true);
            SessionState.SetInt(Key + "viewId", view.GetInstanceID());
            SessionState.SetInt(Key + "sizeIndex", (int)view.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(view));
            SessionState.SetInt(Key + "targetDisplay", (int)PlayType.GetProperty("targetDisplay", Instance).GetValue(view));
            SessionState.SetInt(Key + "mainView", MainView() != null ? MainView().GetInstanceID() : 0);
            SessionState.SetInt(Key + "focusedWindow", EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetInstanceID() : 0);
            SessionState.SetInt(Key + "customSize", -1);
            var zoom = view.GetType().GetField("m_ZoomArea", Instance)?.GetValue(view);
            if (zoom?.GetType().GetProperty("scale", Instance)?.GetValue(zoom) is Vector2 scale)
                SessionState.SetString(Key + "zoom", JsonUtility.ToJson(new Zoom { x = scale.x, y = scale.y }));
            Write("release-capture-before.json");
        }

        private static void FixNow(EditorWindow view)
        {
            var type = view.GetType();
            var groupValue = type.GetProperty("currentSizeGroupType", Static).GetValue(null);
            var group = Group(Sizes(), groupValue);
            var built = (int)group.GetType().GetMethod("GetBuiltinCount", Instance).Invoke(group, null);
            var custom = (int)group.GetType().GetMethod("GetCustomCount", Instance).Invoke(group, null);
            var index = -1;
            for (var i = 0; i < built + custom; i++)
            {
                var size = group.GetType().GetMethod("GetGameViewSize", Instance).Invoke(group, new object[] { i });
                if (size.GetType().GetProperty("sizeType", Instance)?.GetValue(size)?.ToString() == "FixedResolution" &&
                    (int)size.GetType().GetProperty("width", Instance).GetValue(size) == 1080 &&
                    (int)size.GetType().GetProperty("height", Instance).GetValue(size) == 1920) { index = i; break; }
            }
            if (index < 0)
            {
                var assembly = typeof(UnityEditor.Editor).Assembly;
                var modeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
                var size = Activator.CreateInstance(sizeType, Instance, null,
                    new object[] { Enum.Parse(modeType, "FixedResolution"), 1080, 1920, "Escape release capture" }, null);
                group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size });
                index = built + custom;
                SessionState.SetInt(Key + "customSize", custom);
                SessionState.SetString(Key + "group", groupValue.ToString());
            }
            type.GetProperty("selectedSizeIndex", Instance).SetValue(view, index);
            type.GetMethod("UpdateZoomAreaAndParent", Instance).Invoke(view, null);
            PlayType.GetProperty("targetDisplay", Instance).SetValue(view, 0);
            PlayType.GetProperty("targetSize", Instance).SetValue(view, new Vector2(1080, 1920));
            view.Show(); view.Focus();
            // OnFocus -> PlayModeView.SetFocus -> native HostView.SetAsLastPlayModeView
            // and SetMainPlayModeViewSize. Merely setting the dropdown before Play does
            // not survive the Simulator window's Enter-Play focus policy.
            type.GetMethod("OnFocus", Instance).Invoke(view, null);
            foreach (var simulator in SimulatorViews())
                simulator.GetType().GetMethod("Update", Instance)?.Invoke(simulator, null);
            view.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void Settle()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + .09;
            try
            {
                var view = GameView(false);
                if (view == null) throw new InvalidOperationException("The release Game view was closed.");
                FixNow(view);
                Canvas.ForceUpdateCanvases();
                if (--_remaining > 0) return;
                EditorApplication.update -= Settle;
                Write("release-capture-view.json");
            }
            catch (Exception error)
            {
                EditorApplication.update -= Settle;
                Write("release-capture-view.json", error.ToString());
                Debug.LogError(error);
            }
        }

        private static IEnumerable<EditorWindow> SimulatorViews() => Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(v => v.GetType().FullName == "UnityEditor.DeviceSimulation.SimulatorWindow");

        private static string Diagnose()
        {
            var report = Build(); Write("release-capture-view.json");
            return "Screen=" + report.width + "x" + report.height + "; display=" + report.mainDisplay +
                "; main=" + report.mainView + "; focused=" + report.focusedWindow + "; ready1080=" + report.ready1080 +
                "; detail=.cache/qa/bridge/release-capture-view.json";
        }

        private static Report Build(string error = null)
        {
            var main = MainView();
            var views = Resources.FindObjectsOfTypeAll<EditorWindow>().Where(v => PlayType.IsAssignableFrom(v.GetType())).Select(v =>
            {
                var texture = PlayType.GetField("m_TargetTexture", Instance)?.GetValue(v) as RenderTexture;
                return new ViewInfo { id = v.GetInstanceID(), type = v.GetType().FullName, title = v.titleContent.text, position = v.position,
                    target = PlayType.GetProperty("targetSize", Instance)?.GetValue(v)?.ToString(),
                    renderTarget = v.GetType().GetProperty("targetRenderSize", Instance)?.GetValue(v)?.ToString(),
                    selectedSize = v.GetType().GetProperty("selectedSizeIndex", Instance)?.GetValue(v) is int index ? index : -1,
                    simulationState = v.GetType().GetField("m_State", Instance)?.GetValue(v)?.ToString(),
                    texture = texture != null ? texture.width + "x" + texture.height : "none" };
            }).ToArray();
            return new Report { checkedUtc = DateTime.UtcNow.ToString("O"), playing = EditorApplication.isPlaying, width = Screen.width, height = Screen.height,
                safeArea = Screen.safeArea.ToString(), mainView = main != null ? main.GetType().FullName + "#" + main.GetInstanceID() : "none",
                focusedWindow = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().FullName : "none",
                mainDisplay = Display.main.renderingWidth + "x" + Display.main.renderingHeight,
                ready1080 = Screen.width == 1080 && Screen.height == 1920 && main != null && main.GetType().FullName == "UnityEditor.GameView",
                views = views, error = error,
                canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas).Select(c => c.name + " / " + c.renderMode + " / pixels=" + c.pixelRect + " / scale=" + c.scaleFactor).ToArray(),
                cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Select(c => c.name + " / pixels=" + c.pixelRect + " / target=" + (c.targetTexture != null ? c.targetTexture.width + "x" + c.targetTexture.height : "screen")).ToArray() };
        }

        private static void Write(string file, string error = null)
        {
            var directory = Path.GetFullPath(".cache/qa/bridge"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, file), JsonUtility.ToJson(Build(error), true));
        }

        private static string Restore()
        {
            EditorApplication.update -= Settle;
            if (!SessionState.GetBool(Key + "active", false)) return "No release viewport override is active.";
            var view = GameView(false);
            if (view != null)
            {
                view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, SessionState.GetInt(Key + "sizeIndex", 0));
                PlayType.GetProperty("targetDisplay", Instance).SetValue(view, SessionState.GetInt(Key + "targetDisplay", 0));
                view.GetType().GetMethod("UpdateZoomAreaAndParent", Instance).Invoke(view, null);
                var zoom = view.GetType().GetField("m_ZoomArea", Instance)?.GetValue(view);
                var property = zoom?.GetType().GetProperty("scale", Instance);
                var json = SessionState.GetString(Key + "zoom", "");
                if (property != null && property.CanWrite && !string.IsNullOrEmpty(json))
                { var scale = JsonUtility.FromJson<Zoom>(json); property.SetValue(zoom, new Vector2(scale.x, scale.y)); }
                view.Repaint();
            }
            var custom = SessionState.GetInt(Key + "customSize", -1);
            if (custom >= 0)
            {
                var enumType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType", true);
                var group = Group(Sizes(), Enum.Parse(enumType, SessionState.GetString(Key + "group", "Standalone")));
                group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { custom });
            }
            var main = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(v => v.GetInstanceID() == SessionState.GetInt(Key + "mainView", 0));
            if (main != null)
            {
                main.Focus(); main.GetType().GetMethod("OnFocus", Instance)?.Invoke(main, null);
                foreach (var simulator in SimulatorViews()) simulator.GetType().GetMethod("Update", Instance)?.Invoke(simulator, null);
            }
            var focus = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(v => v.GetInstanceID() == SessionState.GetInt(Key + "focusedWindow", 0));
            if (focus != null) focus.Focus();
            SessionState.SetBool(Key + "active", false);
            return "Release viewport ownership, Game view resolution/zoom and prior focused editor view restored. No scene/gameplay/account state changed.";
        }
    }
}
