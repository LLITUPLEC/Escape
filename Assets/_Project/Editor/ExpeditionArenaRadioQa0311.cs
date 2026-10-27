using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production arena renderer/widgets, Edit mode, two native viewports, all requests intercepted before RPC.</summary>
    public static class ExpeditionArenaRadioQa0311
    {
        private const string Output = ".cache/qa/legacy-0.3.11/tournament-ui";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Bridge = typeof(ArenaMatch8Bridge);
        private static readonly Type PollType = Bridge.GetNestedType("ArenaPollResponse", BindingFlags.NonPublic);
        [Serializable] private sealed class Pair
        {
            public int slot; public string uid_a, uid_b, display_a, display_b, status = "pending", winner_uid;
            public bool hp_known_a = true, hp_known_b = true;
            public float hp_a = 1200, hp_b = 1080, hp_max_a = 1200, hp_max_b = 1200;
        }
        [Serializable] private sealed class Bet { public int slot; public string round, side, pick_uid; }
        [Serializable] private sealed class Tournament
        {
            public bool active = true, bets_open;
            public string id = "offline-radio0311", phase, next_round, kind = "smith", bet_tier = "purple", betting_round;
            public int countdown_left = 23, rules_version = 2, prize_ingots = 40, consolation_ingots = 4, recipe_fallback_ingots = 20;
            public Pair[] qf, sf, final_pairs; public Bet[] my_bets;
        }
        [Serializable] private sealed class Poll { public bool ok = true; public bool in_queue; public string queue_kind = "smith"; public int queue_count = 7, queue_max = 8, rules_version = 2; public Tournament tournament; }
        [Serializable] private sealed class Check { public string name, details; public bool passed; }
        [Serializable] private sealed class Frame { public string viewport, phase, round; public int slot; public float width, portraitAWidth, portraitBWidth; }
        [Serializable] private sealed class Shot { public string path, phase; public int width, height; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkOrAccountActions, preferenceWrites;
            public string checkedUtc, error;
            public List<Check> checks = new List<Check>(); public List<Frame> frames = new List<Frame>(); public List<Shot> screenshots = new List<Shot>();
        }
        [MenuItem("Tools/Expedition/Capture Equal Arena Radio 0311")]
        public static void Capture() { Run("capture"); }
        public static string Run(string target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Arena0311 QA requires Edit mode.");
            if (!string.IsNullOrEmpty(target) && target != "capture") throw new ArgumentException("Use target capture.");
            Directory.CreateDirectory(Output);
            var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            var before = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            try
            {
                foreach (var height in new[] { 1920, 2400 }) CaptureViewport(height, report);
                var after = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                CheckThat(report, before.Length == after.Length && before.All(after.Contains), "No Nakama bootstrap created or replaced");
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); throw; }
            finally { File.WriteAllText(Path.Combine(Output, "arena0311-report.json"), JsonUtility.ToJson(report, true)); }
            return JsonUtility.ToJson(report);
        }

        private static void CaptureViewport(int height, Report report)
        {
            var previousScene = SceneManager.GetActiveScene(); var previousRt = RenderTexture.active;
            var previousEvents = EventSystem.current; var previousSelected = previousEvents != null ? previousEvents.currentSelectedGameObject : null;
            var activeField = Bridge.GetField("_activeBridge", BindingFlags.NonPublic | BindingFlags.Static); var previousBridge = activeField.GetValue(null);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            RenderTexture texture = null; Camera camera = null; EventSystem events = null;
            try
            {
                camera = new GameObject("Arena0311Camera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.orthographic = true;
                camera.orthographicSize = height * .5f; camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.transform.position = new Vector3(0, 0, -10);
                texture = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("Arena0311Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.sortingOrder = 300;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; canvas.scaleFactor = 1;
                var background = ExpeditionUi.Fill(canvas.transform, "Background", 0, 0, 1, 1, Color.white); background.raycastTarget = false;
                background.sprite = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
                events = new GameObject("Arena0311EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
                // EventSystem, like BaseRaycaster, is not ExecuteAlways. Register
                // only this fixture through its normal lifecycle before selecting
                // it; the public current setter rejects unregistered instances.
                typeof(EventSystem).GetMethod("OnEnable", Hidden).Invoke(events, null);
                EventSystem.current = events;
                var bridgeGo = new GameObject("InactiveOfflineArena0311"); bridgeGo.SetActive(false); var bridge = bridgeGo.AddComponent<ArenaMatch8Bridge>();
                Set(bridge, "_overlayCanvas", canvas); Set(bridge, "_myUserId", "qa-viewer"); Set(bridge, "_bettingUiEnabled", true); activeField.SetValue(null, bridge);
                var requests = new List<Vector2Int>(); Set(bridge, "EditorBetRequestOverride", new Action<int, int>((slot, side) => requests.Add(new Vector2Int(slot, side))));
                CheckThat(report, !bridgeGo.activeInHierarchy, "Offline lifecycle remains inactive " + height);

                Apply(bridge, Fixture("qf", true)); AdaptCanvases(scene, camera);
                var foreign = Tags(bridge).Single(t => t.Round == "qf" && t.Slot == 2); var selection = foreign.GetComponent<PairRowBetSelect>();
                var a = Toggle(foreign, "A"); var b = Toggle(foreign, "B");
                requests.Clear(); a.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
                CheckThat(report, selection.SelectedSide == 0 && a.isOn && !b.isOn, "Pointer A selects exactly one radio " + height);
                events.SetSelectedGameObject(a.gameObject);
                a.OnMove(new AxisEventData(events) { moveDir = MoveDirection.Down, moveVector = Vector2.down });
                CheckThat(report, events.currentSelectedGameObject == b.gameObject, "Keyboard down navigates A to B " + height);
                b.OnSubmit(new BaseEventData(events));
                CheckThat(report, selection.SelectedSide == 1 && !a.isOn && b.isOn, "Keyboard submit switches exclusively to B " + height);
                CheckThat(report, b.GetComponentsInChildren<ExpeditionArenaRadioGraphic>(true).First(g => !g.DotOnly).Focused, "Keyboard focus is visibly represented " + height);
                b.OnSubmit(new BaseEventData(events));
                CheckThat(report, selection.SelectedSide == -1, "Existing selected-radio cancel semantics retained " + height);
                a.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
                CheckThat(report, requests.SequenceEqual(new[] { new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(2, -1), new Vector2Int(2, 0) }),
                    "Exact A/B/cancel callbacks, original slot, no duplicate requests " + height);
                CheckRaycast(a, events, camera, report, height);
                Save(bridge, camera, texture, "qf-enabled-selected-a", height, report);

                Apply(bridge, Fixture("closed", false)); AdaptCanvases(scene, camera);
                foreign = Tags(bridge).Single(t => t.Round == "qf" && t.Slot == 2); a = Toggle(foreign, "A"); b = Toggle(foreign, "B");
                var beforeCount = requests.Count; a.OnSubmit(new BaseEventData(events)); b.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
                CheckThat(report, requests.Count == beforeCount && !a.IsInteractable() && a.isOn && !b.isOn, "Disabled selected radio never submits or changes " + height);
                CheckThat(report, a.GetComponentsInChildren<ExpeditionArenaRadioGraphic>(true).All(g => !g.Available), "Disabled ring and dot have muted state " + height);
                Save(bridge, camera, texture, "qf-closed-disabled", height, report);

                Apply(bridge, Fixture("sf", true)); AdaptCanvases(scene, camera);
                foreign = Tags(bridge).Single(t => t.Round == "sf" && t.Slot == 2); selection = foreign.GetComponent<PairRowBetSelect>();
                a = Toggle(foreign, "A"); b = Toggle(foreign, "B"); beforeCount = requests.Count;
                events.SetSelectedGameObject(b.gameObject); b.OnSubmit(new BaseEventData(events));
                CheckThat(report, selection.SelectedSide == 1 && requests.Count == beforeCount + 1 && requests.Last() == new Vector2Int(2, 1)
                    && selection.UidB == "qa-a2" && Get<string>(bridge, "_bettingRound") == "sf", "Semifinal repeated slot2 binds current-round users and callback " + height);
                foreach (var history in Tags(bridge).Where(t => !t.Current))
                {
                    beforeCount = requests.Count; Toggle(history, "A").OnSubmit(new BaseEventData(events));
                    CheckThat(report, !Toggle(history, "A").gameObject.activeInHierarchy && !Toggle(history, "B").gameObject.activeInHierarchy && requests.Count == beforeCount,
                        "Historical/future round cannot submit " + height + "/" + history.Round + "/" + history.Slot);
                }
                Save(bridge, camera, texture, "sf-selected-b-shuffled-history", height, report);
                Apply(bridge, Fixture("final", false)); AdaptCanvases(scene, camera);
                CheckThat(report, Tags(bridge).All(t => !Toggle(t, "A").gameObject.activeInHierarchy && !Toggle(t, "B").gameObject.activeInHierarchy), "Final and historical bets remain closed " + height);
                Save(bridge, camera, texture, "final-equal-frames", height, report);
            }
            finally
            {
                RenderTexture.active = previousRt;
                if (camera != null) camera.targetTexture = null;
                if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                SceneManager.SetActiveScene(scene);
                foreach (var root in scene.GetRootGameObjects()) foreach (var raycaster in root.GetComponentsInChildren<BaseRaycaster>(true))
                    RaycasterManager.GetRaycasters().Remove(raycaster);
                if (events != null) typeof(EventSystem).GetMethod("OnDisable", Hidden).Invoke(events, null);
                foreach (var root in scene.GetRootGameObjects()) if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(scene, true); if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                activeField.SetValue(null, previousBridge);
                if (previousEvents != null) { EventSystem.current = previousEvents; previousEvents.SetSelectedGameObject(previousSelected); }
            }
        }

        private static Tournament Fixture(string phase, bool open)
        {
            var quarters = new Pair[4];
            for (var i = 0; i < 4; i++) quarters[i] = new Pair { slot = i + 1, uid_a = i == 0 ? "qa-viewer" : "qa-a" + (i + 1), uid_b = "qa-b" + (i + 1),
                display_a = i == 0 ? "Вы" : "Кузнец" + (i + 1), display_b = "Сталкер" + (i + 1) };
            var semis = new[] {
                new Pair { slot = 1, uid_a = "qa-a3", uid_b = "qa-viewer", display_a = "Кузнец3", display_b = "Вы" },
                new Pair { slot = 2, uid_a = "qa-a4", uid_b = "qa-a2", display_a = "Кузнец4", display_b = "Кузнец2" } };
            var final = new[] { new Pair { slot = 1, uid_a = "qa-viewer", uid_b = "qa-a4", display_a = "Вы", display_b = "Кузнец4" } };
            if (phase == "sf" || phase == "final") foreach (var q in quarters) { q.status = "done"; q.winner_uid = q.uid_a; }
            if (phase == "final") { semis[0].status = semis[1].status = "done"; semis[0].winner_uid = "qa-viewer"; semis[1].winner_uid = "qa-a4"; }
            return new Tournament { phase = phase == "closed" ? "qf" : phase == "final" ? "final" : "countdown", next_round = phase == "sf" ? "sf" : phase == "final" ? "final" : "qf",
                betting_round = phase == "sf" ? "sf" : phase == "final" ? "final" : "qf", bets_open = open, qf = quarters, sf = phase == "qf" || phase == "closed" ? null : semis,
                final_pairs = phase == "final" ? final : null, my_bets = phase == "closed" ? new[] { new Bet { round = "qf", slot = 2, side = "a", pick_uid = "qa-a2" } } : null };
        }
        private static void Apply(ArenaMatch8Bridge bridge, Tournament tournament)
        {
            var model = JsonUtility.FromJson(JsonUtility.ToJson(new Poll { tournament = tournament }), PollType);
            Bridge.GetMethod("ApplyPollPresentation", Hidden).Invoke(bridge, new[] { model, (object)false });
        }
        private static void AdaptCanvases(Scene scene, Camera camera)
        {
            // Match the production300/290 sorting while rendering to a native
            // camera target instead of the Editor's unrelated Game-view size.
            foreach (var root in scene.GetRootGameObjects()) foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler != null) scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                // BaseRaycaster is not ExecuteAlways: its OnEnable registration
                // does not run in Edit mode. Reproduce precisely that registry
                // step for this isolated scene, and remove only these at cleanup.
                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null && !RaycasterManager.GetRaycasters().Contains(raycaster))
                    typeof(BaseRaycaster).GetMethod("OnEnable", Hidden).Invoke(raycaster, null);
            }
            Canvas.ForceUpdateCanvases();
        }
        private static ExpeditionArenaPairTag[] Tags(ArenaMatch8Bridge bridge) => Get<GameObject>(bridge, "_bracketRoot").GetComponentsInChildren<ExpeditionArenaPairTag>(false);
        private static Toggle Toggle(ExpeditionArenaPairTag tag, string side) => tag.transform.Find("Bet" + side).GetComponent<Toggle>();
        private static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]); var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void CheckRaycast(Toggle toggle, EventSystem events, Camera camera, Report report, int height)
        {
            Canvas.ForceUpdateCanvases();
            // GraphicRaycaster intentionally ignores depth==-1 graphics until
            // the canvas has really been rendered at the target resolution.
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = camera.targetTexture }); else camera.Render();
            var rect = ScreenRect(toggle.transform as RectTransform, camera);
            var results = new List<RaycastResult>(); events.RaycastAll(new PointerEventData(events) { position = rect.center }, results);
            CheckThat(report, results.Count > 0 && (results[0].gameObject == toggle.gameObject || results[0].gameObject.transform.IsChildOf(toggle.transform)), "Radio hit target wins actual raycast " + height,
                "hits=" + string.Join(",", results.Take(4).Select(r => r.gameObject.name)) + ";registry=" + RaycasterManager.GetRaycasters().Count +
                ";depth=" + toggle.targetGraphic.depth + ";cull=" + toggle.targetGraphic.canvasRenderer.cull + ";rect=" + rect + ";camera=" + camera.pixelRect + ";screen=" + Screen.width + "x" + Screen.height);
        }
        private static void Save(ArenaMatch8Bridge bridge, Camera camera, RenderTexture texture, string phase, int height, Report report)
        {
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture }); else camera.Render();
            var tags = Tags(bridge); var widths = new List<float>(); var portraitWidths = new List<float>();
            foreach (var tag in tags)
            {
                var width = ScreenRect(tag.transform as RectTransform, camera).width;
                var pa = ScreenRect(tag.transform.Find("HeadAFrame") as RectTransform, camera).width; var pb = ScreenRect(tag.transform.Find("HeadBFrame") as RectTransform, camera).width;
                widths.Add(width); portraitWidths.Add(pa); portraitWidths.Add(pb);
                report.frames.Add(new Frame { viewport = "1080x" + height, phase = phase, round = tag.Round, slot = tag.Slot, width = width, portraitAWidth = pa, portraitBWidth = pb });
                foreach (var side in new[] { "A", "B" })
                {
                    var toggle = Toggle(tag, side); if (!toggle.gameObject.activeInHierarchy) continue;
                    var hit = ScreenRect(toggle.transform as RectTransform, camera);
                    CheckThat(report, hit.width >= 44 && hit.height >= 44, "Radio physical hit area44px " + height + "/" + phase + "/" + tag.Round + "/" + tag.Slot + side, hit.ToString());
                    var ring = toggle.GetComponentsInChildren<ExpeditionArenaRadioGraphic>().Single(g => !g.DotOnly);
                    var dot = toggle.GetComponentsInChildren<ExpeditionArenaRadioGraphic>().Single(g => g.DotOnly);
                    var mesh = ring.canvasRenderer.GetMesh(); var vertexCount = mesh != null ? mesh.vertexCount : 0;
                    CheckThat(report, vertexCount >= 200 && !ring.raycastTarget && ring.Selected == toggle.isOn && (dot.canvasRenderer.GetAlpha() > .5f) == toggle.isOn,
                        "Real circular mesh, checked state and dot alpha " + height + "/" + phase + "/" + tag.Slot + side);
                    CheckThat(report, ExpeditionUi.Find(toggle.transform, "ExpeditionBetCheck") == null, "No checkbox tick in radio " + height + "/" + phase + "/" + tag.Slot + side);
                }
            }
            CheckThat(report, tags.Length == 7 && tags.Count(t => t.Round == "qf") == 4 && tags.Count(t => t.Round == "sf") == 2 && tags.Count(t => t.Round == "final") == 1,
                "Complete8-player3-stage bracket " + height + "/" + phase);
            CheckThat(report, widths.Min() >= 250 && widths.Max() - widths.Min() < .5f, "Every stage has equal physical card width " + height + "/" + phase, widths.Min() + ".." + widths.Max());
            CheckThat(report, portraitWidths.Min() >= 45 && portraitWidths.Max() - portraitWidths.Min() < .5f, "All14portrait frames have equal physical width " + height + "/" + phase,
                portraitWidths.Min() + ".." + portraitWidths.Max());
            CheckConnectors(bridge, report, height + "/" + phase);
            var path = Path.Combine(Output, phase + "-1080x" + height + ".png"); RenderTexture.active = texture;
            var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
            report.screenshots.Add(new Shot { path = path, phase = phase, width = 1080, height = height });
        }
        private static void CheckConnectors(ArenaMatch8Bridge bridge, Report report, string name)
        {
            var tags = Tags(bridge); var rows = Get<Transform>(bridge, "_rowsRoot"); var routes = rows.GetComponentsInChildren<ExpeditionArenaConnectorTag>();
            CheckThat(report, routes.Length == 6, "Six current source routes " + name);
            foreach (var route in routes)
            {
                var from = ExpeditionArenaOverlays.BracketColumnBounds(route.SourceRound).y; var to = ExpeditionArenaOverlays.BracketColumnBounds(route.TargetRound).x;
                var source = tags.Single(t => t.Round == route.SourceRound && t.Slot == route.SourceSlot);
                var line = route.GetComponentInChildren<Image>().rectTransform; var center = ((RectTransform)source.transform).anchorMin.y + .105f;
                CheckThat(report, Mathf.Abs(line.anchorMin.x - from) < .0001f && Mathf.Abs((line.anchorMin.y + line.anchorMax.y) * .5f - center) < .0001f,
                    "Connector attaches to equal-column source edge " + name + "/" + route.SourceRound + route.SourceSlot);
                var destination = tags.Single(t => t.Round == route.TargetRound && t.Slot == route.TargetSlot);
                if (!string.IsNullOrEmpty(route.WinnerUid)) CheckThat(report, destination.UidA == route.WinnerUid || destination.UidB == route.WinnerUid, "Shuffled connector follows winner UID " + name + route.SourceSlot);
                var bus = rows.Find("BracketBus_" + route.TargetRound + "_" + route.TargetSlot);
                CheckThat(report, bus != null && bus.GetComponentsInChildren<Image>().Any(i => Mathf.Abs(i.rectTransform.anchorMax.x - to) < .0001f), "Connector reaches next equal-column edge " + name + route.SourceSlot);
            }
        }
        private static T Get<T>(object target, string name) => (T)Bridge.GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => Bridge.GetField(name, Hidden).SetValue(target, value);
        private static void CheckThat(Report report, bool passed, string name, string details = "")
        {
            report.checks.Add(new Check { name = name, passed = passed, details = details });
            if (!passed) throw new InvalidOperationException(name + " / " + details);
        }
    }
}
