using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Frame-observed local UI probe. Does not invoke dossier, purchases, server or resource actions.</summary>
    public static class ExpeditionMineTravelQa
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static ExpeditionMinePresentation _view;
        private static ScrollRect _scroll;
        private static RectTransform _content, _lift, _first, _third, _last;
        private static Report _report;
        private static readonly List<Sample> Samples = new List<Sample>();
        private static readonly List<string> Checks = new List<string>();
        private static int _stage, _lastFrame, _phaseFrame, _manualFrame, _cancelKind;
        private static double _started;
        private static bool _sawIntermediate;
        private static float _manualView, _manualLift;
        private static string _outputRoot;
        private static PointerEventData _pointer;

        [Serializable]
        public sealed class Sample
        {
            public string checkpoint;
            public int frame, selectedFloor;
            public float realtime, viewY, liftY, normalized, viewportHeight, landingHeight;
            public bool travelling;
        }

        [Serializable]
        public sealed class Report
        {
            public string state, scene, startedUtc, finishedUtc, error, layoutRevision;
            public bool completed, passed, midScreenshotWritten, finishScreenshotWritten;
            public string midScreenshot, finishScreenshot;
            public string[] checks;
            public Sample[] samples;
        }

        public static string StartProbe()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Unpaused MineScene Play mode required");
            if (_report != null && !_report.completed)
                throw new InvalidOperationException("Mine travel probe already running");
            _view = UnityEngine.Object.FindFirstObjectByType<ExpeditionMinePresentation>();
            if (_view == null || !_view.isActiveAndEnabled)
                throw new InvalidOperationException("Active expedition mine view required");
            if (!Read<bool>("_selectedInitialProgress"))
                throw new InvalidOperationException("Wait for the mine catalog and initial server progress before starting this UI-only probe");
            if (EventSystem.current == null) throw new InvalidOperationException("Scene EventSystem required");
            _scroll = Read<ScrollRect>("_scroll");
            _content = Read<RectTransform>("_content");
            _lift = Read<RectTransform>("_liftCarrier");
            _first = _content.Find("Landing_01") as RectTransform;
            _third = _content.Find("Landing_03") as RectTransform;
            _last = _content.Find("Landing_12") as RectTransform;
            if (_scroll == null || _first == null || _third == null || _last == null || _lift == null)
                throw new InvalidOperationException("Mine route geometry is incomplete");
            _outputRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.cache/qa/bridge"));
            Directory.CreateDirectory(_outputRoot);
            Samples.Clear(); Checks.Clear();
            _report = new Report
            {
                state = "running", scene = _view.gameObject.scene.name, layoutRevision = "mine-0.3.3",
                startedUtc = DateTime.UtcNow.ToString("O"),
                midScreenshot = ".cache/qa/bridge/screenshots/mine-travel-mid.png",
                finishScreenshot = ".cache/qa/bridge/screenshots/mine-travel-finish.png"
            };
            Directory.CreateDirectory(Path.Combine(_outputRoot, "screenshots"));
            foreach (var file in new[] { "mine-travel-mid.png", "mine-travel-finish.png" })
            {
                var path = Path.Combine(_outputRoot, "screenshots", file);
                if (File.Exists(path)) File.Delete(path);
            }
            _started = EditorApplication.timeSinceStartup;
            _stage = 0; _cancelKind = 0; _sawIntermediate = false;
            _lastFrame = Time.frameCount - 1;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= StopForReload;
            AssemblyReloadEvents.beforeAssemblyReload += StopForReload;
            Save();
            return "Started actual-frame mine travel probe; poll .cache/qa/bridge/mine-travel-play.json until completed. No RPC/resource actions.";
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying || _view == null)
                    throw new InvalidOperationException("Play mode ended or mine view was removed");
                if (EditorApplication.timeSinceStartup - _started > 35)
                    throw new TimeoutException("Mine probe exceeded 35 seconds; inspect GameView frame rate and active scene");
                if (EditorApplication.isPaused || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                switch (_stage)
                {
                    case 0:
                        _view.ReflowRoute();
                        Snap(1);
                        Require(_first.anchoredPosition.y > _last.anchoredPosition.y, "physical floor 1 above floor 12");
                        var visibleFloors = _scroll.viewport.rect.height / _first.rect.height;
                        Require(visibleFloors >= 1.95f && visibleFloors <= 3, "viewport spans two to three landing heights");
                        VerifyComposition();
                        Require(Near(_lift.anchoredPosition.y, _first.anchoredPosition.y), "initial cabin at actual floor 1 geometry");
                        Require(_scroll.verticalNormalizedPosition > .999f, "initial camera at top");
                        Record("initial_snap1");
                        _view.SelectFloor(12, true);
                        _phaseFrame = Time.frameCount;
                        Record("tap12_started");
                        _stage = 1;
                        break;
                    case 1:
                        var progress12 = Mathf.InverseLerp(_first.anchoredPosition.y, _last.anchoredPosition.y, _lift.anchoredPosition.y);
                        if (!_sawIntermediate && Travelling && progress12 > .18f && progress12 < .95f)
                        {
                            _sawIntermediate = true;
                            Require(_content.anchoredPosition.y > 0 && _scroll.verticalNormalizedPosition > .001f,
                                "observed in-flight camera and cabin before arrival");
                            Record("tap12_intermediate");
                            Capture("mine-travel-mid.png");
                        }
                        if (Travelling) break;
                        Require(_sawIntermediate && Time.frameCount > _phaseFrame, "travel observed across actual rendered frames");
                        Require(Near(_lift.anchoredPosition.y, _last.anchoredPosition.y), "cabin arrived at observed floor12 position");
                        Require(_scroll.verticalNormalizedPosition < .001f, "camera arrived at actual scroll bottom");
                        Record("tap12_completed");
                        Snap(1); _view.SelectFloor(12, true);
                        _stage = 2;
                        break;
                    case 2:
                        if (!Travelling) throw new InvalidOperationException("Retarget probe missed the active trip");
                        var progress = Mathf.InverseLerp(_first.anchoredPosition.y, _last.anchoredPosition.y, _lift.anchoredPosition.y);
                        if (progress < .20f) break;
                        var beforeView = _content.anchoredPosition.y;
                        var beforeLift = _lift.anchoredPosition.y;
                        Record("retarget_before");
                        _view.SelectFloor(3, true);
                        Require(Mathf.Abs(_content.anchoredPosition.y - beforeView) < _scroll.viewport.rect.height * .08f,
                            "retarget starts at current camera position without teleport");
                        Require(Mathf.Abs(_lift.anchoredPosition.y - beforeLift) < _first.rect.height * .08f,
                            "retarget starts at current cabin position without teleport");
                        Record("retarget_after");
                        _stage = 3;
                        break;
                    case 3:
                        if (Travelling) break;
                        Require(_view.SelectedFloor == 3 && Near(_lift.anchoredPosition.y, _third.anchoredPosition.y),
                            "retarget arrived at actual floor3 geometry");
                        // Measure the landing itself; clipped damp-wall decoration intentionally extends above its room.
                        var center = _scroll.viewport.InverseTransformPoint(_third.TransformPoint(_third.rect.center));
                        Require(Mathf.Abs(center.y - _scroll.viewport.rect.center.y) < 2,
                            "actual selected floor3 rect center aligned with viewport");
                        Record("retarget3_completed");
                        BeginCancellationTrip();
                        break;
                    case 4:
                        if (!Travelling) throw new InvalidOperationException("Cancellation probe missed the active trip");
                        var cancellationProgress = Mathf.InverseLerp(_first.anchoredPosition.y, _last.anchoredPosition.y, _lift.anchoredPosition.y);
                        if (cancellationProgress < .12f) break;
                        DispatchCancellation();
                        Require(!Travelling, CancellationName + " cancelled actual coroutine");
                        _scroll.OnEndDrag(_pointer);
                        _scroll.StopMovement();
                        var range = Mathf.Max(0, _content.rect.height - _scroll.viewport.rect.height);
                        _content.anchoredPosition = new Vector2(0, Mathf.Clamp(_content.anchoredPosition.y + 37, 1, range - 1));
                        _manualView = _content.anchoredPosition.y; _manualLift = _lift.anchoredPosition.y;
                        _manualFrame = Time.frameCount;
                        Record(CancellationName + "_manual_offset");
                        _stage = 5;
                        break;
                    case 5:
                        Require(!Travelling && Near(_content.anchoredPosition.y, _manualView) && Near(_lift.anchoredPosition.y, _manualLift),
                            CancellationName + " manual position retained at frame " + Time.frameCount);
                        if (Time.frameCount - _manualFrame < 6) break;
                        Record(CancellationName + "_retained_6frames");
                        if (++_cancelKind < 3) BeginCancellationTrip();
                        else
                        {
                            Snap(3); Record("final_floor3"); Capture("mine-travel-finish.png");
                            _phaseFrame = Time.frameCount; _stage = 6;
                        }
                        break;
                    case 6:
                        if (Time.frameCount - _phaseFrame < 2) break;
                        var mid = Path.Combine(_outputRoot, "screenshots/mine-travel-mid.png");
                        var finish = Path.Combine(_outputRoot, "screenshots/mine-travel-finish.png");
                        if (!File.Exists(mid) || !File.Exists(finish)) break;
                        _report.midScreenshotWritten = new FileInfo(mid).Length > 0;
                        _report.finishScreenshotWritten = new FileInfo(finish).Length > 0;
                        Require(_report.midScreenshotWritten && _report.finishScreenshotWritten, "two actual GameView PNG files written");
                        Finish(null);
                        break;
                }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        private static string CancellationName => _cancelKind == 0 ? "initializePotentialDrag" : _cancelKind == 1 ? "beginDrag" : "scrollWheel";

        private static void VerifyComposition()
        {
            var shaft = _content.Find("DedicatedLiftShaft") as RectTransform;
            var cabin = _lift.Find("LiftCabin") as RectTransform;
            Require(shaft != null && cabin != null, "dedicated shaft and physical cabin exist");
            var shaftBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_content, shaft);
            var cabinBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_content, cabin);
            Require(cabinBounds.min.x >= shaftBounds.min.x && cabinBounds.max.x <= shaftBounds.max.x,
                "physical cabin fits entirely inside dedicated shaft");
            var cage = cabin.Find("PhysicalHoistCage")?.GetComponent<Image>();
            Require(cage != null && cage.sprite != null, "painted physical hoist sprite imported");
            var room = _first.Find("ChamberBounds") as RectTransform;
            var firstArt = room != null ? room.Find("RockChamber")?.GetComponent<Image>() : null;
            var thirdArt = _third.Find("ChamberBounds/RockChamber")?.GetComponent<Image>();
            Require(firstArt != null && firstArt.sprite != null && thirdArt != null && thirdArt.sprite != null &&
                    firstArt.sprite != thirdArt.sprite, "dry and damp room art both imported and used");
            var barrier = room.Find("ChainedUnknownPassage/PaintedRubbleAndMist")?.GetComponent<Image>();
            Require(barrier != null && barrier.sprite != null, "painted full-width rubble and mist barrier imported");
            var oldBackdrop = _view.transform.Find("MineDiorama")?.GetComponent<Image>();
            Require(oldBackdrop == null || !oldBackdrop.enabled, "local mine view leaves full-bleed background to shared scene shell");

            // Exercise the real timer renderer with a reversible view-only state copy. No
            // controller/server/resource data changes; restore before any frame is yielded.
            var rows = Read<Array>("_landings");
            var firstRow = rows.GetValue(0);
            var stateField = firstRow.GetType().GetField("state");
            var original = (ExpeditionMinePresentation.FloorState)stateField.GetValue(firstRow);
            var preview = original;
            preview.hasCatalog = true; preview.unlocked = true; preview.respawnSeconds = 125;
            try
            {
                _view.SetFloor(preview);
                var timer = _first.Find("RespawnTimerOnLanding") as RectTransform;
                Require(timer != null && timer.gameObject.activeSelf && timer.parent == _first,
                    "respawn timer belongs to its actual landing and becomes visible");
                var timerBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_first, timer);
                Require(timerBounds.min.y > _first.rect.yMin + _first.rect.height * .28f &&
                        timerBounds.max.y < _first.rect.yMax,
                    "respawn timer bounds stay inside chamber above standing floor");
                Require(timer.GetComponentInChildren<Text>().text.Contains("02:05"),
                    "actual timer renderer formats 125 seconds as 02:05");
            }
            finally { _view.SetFloor(original); }
        }

        private static void BeginCancellationTrip()
        {
            Snap(1); _view.SelectFloor(12, true); _stage = 4;
        }

        private static void DispatchCancellation()
        {
            var canvas = _scroll.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var world = _scroll.viewport.TransformPoint(_scroll.viewport.rect.center);
            _pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(camera, world),
                scrollDelta = new Vector2(0, -1)
            };
            var raycaster = _scroll.GetComponentInParent<GraphicRaycaster>();
            if (raycaster != null) _pointer.pointerPressRaycast = new RaycastResult { module = raycaster, worldPosition = world };
            if (_cancelKind == 0) _scroll.OnInitializePotentialDrag(_pointer);
            else if (_cancelKind == 1) _scroll.OnBeginDrag(_pointer);
            else _scroll.OnScroll(_pointer);
            Record(CancellationName + "_dispatched");
        }

        private static void Snap(int floor)
        {
            _view.SelectFloor(floor, false);
            typeof(ExpeditionMinePresentation).GetMethod("SnapRouteToFloor", PrivateInstance).Invoke(_view, new object[] { floor });
        }

        private static bool Travelling => Read<Coroutine>("_tapTravel") != null;
        private static T Read<T>(string field) => (T)typeof(ExpeditionMinePresentation).GetField(field, PrivateInstance).GetValue(_view);
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 1;
        private static void Require(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            Checks.Add(description);
        }

        private static void Record(string checkpoint)
        {
            Samples.Add(new Sample
            {
                checkpoint = checkpoint, frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
                selectedFloor = _view.SelectedFloor, viewY = _content.anchoredPosition.y,
                liftY = _lift.anchoredPosition.y, normalized = _scroll.verticalNormalizedPosition,
                viewportHeight = _scroll.viewport.rect.height, landingHeight = _first.rect.height,
                travelling = Travelling
            });
        }

        private static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(_outputRoot, "screenshots", name));
        private static void Save()
        {
            _report.checks = Checks.ToArray(); _report.samples = Samples.ToArray();
            File.WriteAllText(Path.Combine(_outputRoot, "mine-travel-play.json"), JsonUtility.ToJson(_report, true));
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= StopForReload;
            if (_view != null) _view.CancelTapTravel();
            _report.completed = true; _report.passed = error == null;
            _report.state = error == null ? "passed" : "failed";
            _report.error = error; _report.finishedUtc = DateTime.UtcNow.ToString("O");
            Save();
        }

        private static void StopForReload()
        {
            if (_report != null && !_report.completed) Finish("Domain reload interrupted the Play probe");
        }
    }
}
