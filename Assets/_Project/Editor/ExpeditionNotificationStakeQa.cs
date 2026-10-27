using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Achievements;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production UI with local events/callbacks only. Never changes achievement data, authenticates, joins a queue or places a bet.</summary>
    public static class ExpeditionNotificationStakeQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Output = "Design/Validation/Appearance/ui-fixes";
        [Serializable] private sealed class Report
        {
            public bool passed, completed, rpcOrAccountActions;
            public string error, checkedUtc;
            public string[] checks, screenshots;
        }

        private static AchievementInGameToastHost _toast;
        private static HashSet<string> _seenSnapshot;
        private static Dictionary<string, float> _timeSnapshot;
        private static readonly List<string> ToastChecks = new List<string>();
        private static readonly List<string> ToastShots = new List<string>();
        private static string _prefix;
        private static float _originalTimeScale;
        private static int _stage, _beforeCount;
        private static double _started, _stageStarted;
        private static Vector2 _firstPosition;

        public static string Run()
        {
            Require(EditorApplication.isPlaying, "Toast probe requires Play mode in the local solo battle.");
            Require(_toast == null, "A toast probe is already running.");
            var battle = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Require(battle != null && !battle.enabled && Read<bool>(battle, "_useLocalBotSimulation") &&
                battle.gameObject.scene.name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal) &&
                UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null,
                "Use ExpeditionNotificationStakeOfflineFixture.Setup(), then Play; production Solo requires backend and is not this offline fixture.");
            Require(Screen.width == 1080 && Screen.height == 1920, "Set the actual Game view to 1080x1920 first.");
            var toast = AchievementInGameToastHost.Ensure();
            Require(Read<Coroutine>(toast, "_routine") == null && Queue(toast).Count == 0, "Wait for real achievement notices before starting the probe.");
            _toast = toast;
            _seenSnapshot = new HashSet<string>(Read<HashSet<string>>(toast, "_seen"));
            _timeSnapshot = new Dictionary<string, float>(Read<Dictionary<string, float>>(toast, "_queuedAt"));
            _beforeCount = toast.PresentedCount;
            _originalTimeScale = Time.timeScale;
            Time.timeScale = 0; // The production animation must continue while gameplay is paused.
            _prefix = "qa.toast." + Guid.NewGuid().ToString("N");
            _stage = 0; _started = _stageStarted = EditorApplication.timeSinceStartup;
            ToastChecks.Clear(); ToastShots.Clear(); Directory.CreateDirectory(Output);
            try
            {
                Notify(0); Notify(0);
                Require(Queue(toast).Count + (toast.CurrentNotice != null ? 1 : 0) == 1, "Duplicate events entered the presentation queue twice.");
            }
            catch (Exception error) { FinishToast(error); throw; }
            EditorApplication.update += TickToast;
            AssemblyReloadEvents.beforeAssemblyReload += AbortToast;
            EditorApplication.playModeStateChanged += ProbeModeChanged;
            return "Toast probe started: real local-battle frames, duplicate suppression, stable positions, expiry, scene/account isolation; no account actions.";
        }

        private static void TickToast()
        {
            try
            {
                if (_toast == null) { FinishToast(new InvalidOperationException("The toast host was destroyed before the probe completed.")); return; }
                Require(EditorApplication.isPlaying, "Play mode ended before the toast probe completed.");
                Require(EditorApplication.timeSinceStartup - _started < 20, "Toast probe timed out.");
                var elapsed = EditorApplication.timeSinceStartup - _stageStarted;
                if (_stage == 0 && elapsed > .45)
                {
                    var group = Read<CanvasGroup>(_toast, "_panelGroup");
                    Require(group.alpha > .99f && _toast.CurrentNotice?.StepIndex == 0 && _toast.PresentedCount == _beforeCount + 1,
                        "The completion did not become visible promptly in the active battle.");
                    var canvas = Read<Canvas>(_toast, "_internalCanvas");
                    Require(canvas.enabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder >= 6200,
                        "The completion borrows a hidden/lower scene canvas.");
                    Require(!group.blocksRaycasts && _toast.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Toast blocks battle input.");
                    RequireWithinSafeArea(Read<RectTransform>(_toast, "_panelRt"));
                    _firstPosition = Read<RectTransform>(_toast, "_panelRt").anchoredPosition;
                    Shot("achievement-toast-battle");
                    ToastChecks.Add("Completion is visible on actual battle frames while timeScale=0, above battle UI, within safe area and without raycast blocking; duplicate suppressed.");
                    _stage = 1; _stageStarted = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 1 && Read<Coroutine>(_toast, "_routine") == null)
                {
                    Require(Read<CanvasGroup>(_toast, "_panelGroup").alpha == 0, "Toast failed to hide after its lifetime.");
                    Require((Read<RectTransform>(_toast, "_panelRt").anchoredPosition - _firstPosition).sqrMagnitude < .01f, "Hidden toast accumulated a positional offset.");
                    Notify(1); _stage = 2; _stageStarted = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 2 && elapsed > .45)
                {
                    Require(_toast.CurrentNotice?.StepIndex == 1 && _toast.PresentedCount == _beforeCount + 2, "Second completion did not present.");
                    Require((Read<RectTransform>(_toast, "_panelRt").anchoredPosition - _firstPosition).sqrMagnitude < .01f, "Second toast drifted above its original anchor.");
                    Shot("achievement-toast-second");
                    _stage = 25; _stageStarted = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 25 && elapsed > .1)
                {
                    // Both scenes are this offline fixture. Exercise the real scene event
                    // without unloading/reloading battle UI or touching a production controller.
                    var battleScene = SceneManager.GetActiveScene();
                    var transition = SceneManager.CreateScene("ToastQaLocalTransition");
                    Require(SceneManager.SetActiveScene(transition), "Could not enter the temporary local transition scene.");
                    Require(Queue(_toast).Count == 0 && _toast.CurrentNotice == null && Read<CanvasGroup>(_toast, "_panelGroup").alpha == 0, "Scene transition retained a stale visible/queued notification.");
                    Require(SceneManager.SetActiveScene(battleScene), "Could not restore the offline battle as active scene.");
                    SceneManager.UnloadSceneAsync(transition);
                    Notify(1);
                    Require(Queue(_toast).Count == 0, "The same completion replayed after a scene transition.");
                    ToastChecks.Add("The second animation uses the same anchor; actual activeSceneChanged events clear presentation and do not replay a completed token.");
                    _toast.enabled = false;
                    for (var i = 10; i < 20; i++) Notify(i);
                    Require(Queue(_toast).Count == 4, "Burst queue is not bounded to four pending notices.");
                    var times = Read<Dictionary<string, float>>(_toast, "_queuedAt");
                    foreach (var key in times.Keys.ToArray()) times[key] = Time.realtimeSinceStartup - 15;
                    _toast.enabled = true;
                    _stage = 3; _stageStarted = EditorApplication.timeSinceStartup;
                }
                else if (_stage == 3 && elapsed > .2)
                {
                    Require(Queue(_toast).Count == 0 && _toast.CurrentNotice == null && Read<Coroutine>(_toast, "_routine") == null,
                        "Expired notices replayed or left a stuck coroutine handle.");
                    _toast.enabled = false;
                    Notify(20);
                    Field(_toast, "_scope").SetValue(_toast, "qa.previous-owner");
                    Notify(21);
                    Require(Queue(_toast).Count == 1 && Queue(_toast).Peek().StepIndex == 21 && Read<HashSet<string>>(_toast, "_seen").Count == 1,
                        "Account change leaked old queue/dedup state.");
                    ToastChecks.Add("Four-notice bound and 14-second freshness window discard expired bursts; account change clears another owner's queue and dedup tokens.");
                    FinishToast(null);
                }
            }
            catch (Exception error) { FinishToast(error); }
        }

        private static void Notify(int step) => AchievementLifecycle.NotifyAwaitingClaim(new AchievementUnlockInfo
        {
            ChainId = _prefix, StepIndex = step, NoticeKind = AchievementNoticeKind.CriterionMetAwaitClaim,
            Title = step == 0 ? "Выполнить 50 крестов" : "Собрать линию из шести камней",
            RewardLine = "+5 к здоровью"
        });

        private static void Shot(string name)
        {
            var path = Path.GetFullPath(Path.Combine(Output, name + ".png"));
            ScreenCapture.CaptureScreenshot(path); ToastShots.Add(path);
        }

        private static void AbortToast() => FinishToast(new InvalidOperationException("Assembly reload interrupted the presentation probe."));
        private static void ProbeModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) FinishToast(new InvalidOperationException("Play mode ended before the presentation probe completed."));
        }

        private static void FinishToast(Exception error)
        {
            EditorApplication.update -= TickToast;
            AssemblyReloadEvents.beforeAssemblyReload -= AbortToast;
            EditorApplication.playModeStateChanged -= ProbeModeChanged;
            Time.timeScale = _originalTimeScale;
            if (_toast != null)
            {
                Invoke(_toast, "StopPresentation", true);
                var seen = Read<HashSet<string>>(_toast, "_seen"); seen.Clear(); foreach (var token in _seenSnapshot) seen.Add(token);
                var times = Read<Dictionary<string, float>>(_toast, "_queuedAt"); times.Clear(); foreach (var value in _timeSnapshot) times.Add(value.Key, value.Value);
                typeof(AchievementInGameToastHost).GetProperty("PresentedCount").SetValue(_toast, _beforeCount);
                _toast.enabled = true;
            }
            var report = new Report { passed = error == null, completed = true, checkedUtc = DateTime.UtcNow.ToString("O"),
                error = error?.ToString(), checks = ToastChecks.ToArray(), screenshots = ToastShots.ToArray(), rpcOrAccountActions = false };
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "toast-play-validation.json"), JsonUtility.ToJson(report, true));
            _toast = null;
            if (error != null) Debug.LogError("Toast play probe: " + error);
            else Debug.Log("Toast play probe PASS: visible in battle, stable repeated placement, bounded fresh queue, dedup, screen/account isolation.");
        }

        public static string Capture038() => Capture(".cache/qa/legacy-0.3.8/stakes");

        public static string Capture(string destination = Output)
        {
            Directory.CreateDirectory(destination);
            var scene = EditorApplication.isPlaying ? SceneManager.CreateScene("StakePresentationQa")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var previousTarget = RenderTexture.active;
            RenderTexture target = null;
            var checks = new List<string>(); var shots = new List<string>();
            var roots = new List<GameObject>();
            try
            {
                var cameraRoot = new GameObject("StakeQaCamera", typeof(Camera)); roots.Add(cameraRoot); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
                var camera = cameraRoot.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 960;
                camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvasRoot = new GameObject("StakeQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); roots.Add(canvasRoot); SceneManager.MoveGameObjectToScene(canvasRoot, scene);
                var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                var world = ExpeditionUi.Fill(canvas.transform, "ArenaWorld", 0, 0, 1, 1, Color.white);
                world.sprite = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
                foreach (var mode in new[] { "ore", "gold", "smith", "smith-legacy" })
                {
                    var kind = mode == "smith-legacy" ? "smith" : mode;
                    var version = mode == "smith-legacy" ? 1 : 2;
                    var modal = ExpeditionUi.Rect(canvas.transform, "ArenaBetModal", 0, 0, 1, 1);
                    var dim = modal.gameObject.AddComponent<Image>(); dim.color = new Color(0, 0, 0, .7f);
                    var panel = ExpeditionUi.Panel(modal, "Panel", .1f, .1f, .9f, .9f).rectTransform;
                    var title = ExpeditionUi.Text(panel, "Title", "", 39, 0, 0, 1, 1);
                    var close = ExpeditionUi.Button(panel, "CancelStake", "Отмена", 0, 0, 1, 1, null);
                    var tiers = kind == "ore" ? new[] { "500", "2000" } : kind == "gold" ? new[] { "600", "2400" } : new[] { "green", "blue", "purple" };
                    var calls = new Dictionary<string, int>();
                    foreach (var tier in tiers)
                    {
                        var localTier = tier; calls[tier] = 0;
                        var row = ExpeditionUi.Button(panel, "Row_" + tier, "", 0, 0, 1, 1, () => calls[localTier]++);
                        UnityEngine.Object.DestroyImmediate(row.transform.Find("Label").gameObject);
                        var icon = ExpeditionUi.Fill(row.transform, "ResIcon", 0, 0, 1, 1, Color.white);
                        var path = kind == "ore" ? "ore" : kind == "gold" ? "gold" : "ignot_" + tier;
                        icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/resources_hud/" + path + ".png");
                    }
                    ExpeditionTournamentStakeChooser.Apply(modal, title, close.transform, kind, version);
                    var chooser = modal.GetComponent<ExpeditionTournamentStakeChooser>();
                    Require(!chooser.ConfirmButton.interactable && chooser.SelectedTier == null, "Opening a stake chooser preselects a paid entry.");
                    SaveStake(camera, target, canvas.transform, "stake-" + mode + "-choose", shots, destination);
                    foreach (var tier in tiers)
                    {
                        ExpeditionTournamentStakeChooser.Apply(modal, title, close.transform, kind, version);
                        panel.Find("Row_" + tier).GetComponent<Button>().onClick.Invoke();
                        Require(chooser.SelectedTier == tier && chooser.ConfirmButton.interactable && calls.Values.Sum() == Array.IndexOf(tiers, tier), "Choosing a row invoked an entry callback or selected the wrong tier.");
                        var choice = ExpeditionTournamentStakeChooser.Describe(kind, tier, version);
                        Require(choice != null && choice.Stake > 0 && (choice.Gold > 0 || choice.Ore > 0), "Missing production tier amounts.");
                        if (kind == "smith")
                        {
                            var index = Array.IndexOf(tiers,tier);
                            Require(choice.Stake == (version == 1 ? 50 : new[]{10,15,20}[index]) && choice.Gold == choice.Ore && !choice.DifferenceLabel.Contains("+"), "Smith stake/version or mixed-currency difference is incorrect.");
                            Require(choice.IngotPrize == (version == 1 ? 0 : new[]{20,30,40}[index]) && choice.Consolation == (version == 1 ? 0 : new[]{2,3,4}[index]) && choice.RecipeFallbackIngots == (version == 1 ? 0 : new[]{10,15,20}[index]), "Smith crafting rewards differ from current policy or leak into v1.");
                        }
                        else Require((kind == "ore" ? choice.Ore : choice.Gold) == choice.Stake * 5, "Resource prize differs from the current server's x5 base prize.");
                        SaveStake(camera, target, canvas.transform, "stake-" + mode + "-" + tier, shots, destination);
                        chooser.ConfirmButton.onClick.Invoke(); chooser.ConfirmButton.onClick.Invoke();
                        Require(calls[tier] == 1 && !chooser.ConfirmButton.interactable, "Explicit confirmation did not invoke exactly one original local callback.");
                    }
                    checks.Add(mode + ": all current tiers show explicit stake/prize currencies, base net only for same currency, no auto-entry; local selection + exactly-once explicit confirmation.");
                    UnityEngine.Object.DestroyImmediate(modal.gameObject);
                }
                var report = new Report { passed = true, completed = true, checkedUtc = DateTime.UtcNow.ToString("O"), checks = checks.ToArray(), screenshots = shots.ToArray(), rpcOrAccountActions = false };
                File.WriteAllText(Path.Combine(destination, "stake-modal-validation.json"), JsonUtility.ToJson(report, true));
                return "PASS stake modal: all seven tiers plus legacy smith v1, no paid/network callbacks, explicit confirmation once, fourteen real production-UI captures at1080x1920.";
            }
            finally
            {
                RenderTexture.active = previousTarget;
                foreach (var root in roots)
                {
                    var camera = root != null ? root.GetComponent<Camera>() : null;
                    if (camera != null) camera.targetTexture = null;
                }
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                foreach (var root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (EditorApplication.isPlaying) SceneManager.UnloadSceneAsync(scene);
                else EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void SaveStake(Camera camera, RenderTexture target, Transform canvas, string name, List<string> shots, string destination)
        {
            SetLayer(canvas, 31);
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(false))
            {
                text.ForceMeshUpdate();
                Require(!text.isTextOverflowing, "Stake modal text overflows: " + text.name + " / " + text.text);
            }
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            else camera.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply();
                var path = Path.GetFullPath(Path.Combine(destination, name + ".png"));
                File.WriteAllBytes(path, pixels.EncodeToPNG()); shots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static void SetLayer(Transform root, int layer) { root.gameObject.layer = layer; foreach (Transform child in root) SetLayer(child, layer); }
        private static void RequireWithinSafeArea(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var safe = Screen.safeArea;
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                Require(point.x >= safe.xMin && point.x <= safe.xMax && point.y >= safe.yMin && point.y <= safe.yMax, "Toast leaves screen.safeArea.");
            }
        }
        private static Queue<AchievementUnlockInfo> Queue(AchievementInGameToastHost host) => Read<Queue<AchievementUnlockInfo>>(host, "_queue");
        private static FieldInfo Field(object value, string name) => value.GetType().GetField(name, Hidden) ?? throw new MissingFieldException(value.GetType().Name, name);
        private static T Read<T>(object value, string name) => (T)Field(value, name).GetValue(value);
        private static object Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden).Invoke(value, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
