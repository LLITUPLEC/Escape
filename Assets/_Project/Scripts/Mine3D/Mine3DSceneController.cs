using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Project.Mine3D
{
    /// <summary>
    /// UI сложности + привязка запечённой / runtime шахты. FloorLift намеренно отсутствует.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class Mine3DSceneController : MonoBehaviour
    {
        private const float CameraFov = 100f;

        [SerializeField] private Mine3DShaftController shaftController;
        [SerializeField] private Mine3DCameraScroller cameraScroller;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Transform worldRoot;

        private void Awake()
        {
            EnsureEventSystem();
            EnsureUiOverlay();
            EnsureWorld();
            if (GetComponent<Mine3DWorldClickInput>() == null)
                gameObject.AddComponent<Mine3DWorldClickInput>();
            // AutoInstall срабатывает только на первой сцене — ставим контроллер явно.
            Project.UI.MineSceneController.EnsureInstalled();
        }

        private void Start()
        {
            FixExistingFloorUi();
            WireDifficultyTabsFallback();
            RebindAllFloorInteractables();
            Mine3DUiBevel.ApplyDifficultySelection(
                shaftController != null ? shaftController.CurrentDifficulty : Mine3DShaftController.Easy);

            var mine = Project.UI.MineSceneController.EnsureInstalled();
            mine?.NotifyMine3DWorldReady();
        }

        /// <summary>
        /// Подтягивает старые запечённые FloorUi (позиция Label / GraphicRaycaster) без пересоздания сцены.
        /// </summary>
        private static void FixExistingFloorUi()
        {
            var views = FindObjectsByType<Mine3DFloorView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (view == null) continue;

                if (view.FloorUi != null)
                {
                    var h = Mine3DGeometryBuilder.GetFloorOpenHeight(Mathf.Max(1, view.Floor));
                    Mine3DGeometryBuilder.ApplyReadableFloorUiTransform(view.FloorUi, h);

                    var gr = view.FloorUi.GetComponent<GraphicRaycaster>();
                    if (gr != null)
                        Destroy(gr);

                    for (var ti = 0; ti < view.FloorUi.childCount; ti++)
                    {
                        var child = view.FloorUi.GetChild(ti) as RectTransform;
                        if (child == null) continue;
                        var s = child.localScale;
                        child.localScale = new Vector3(
                            Mathf.Abs(s.x) < 0.0001f ? 1f : Mathf.Abs(s.x),
                            Mathf.Abs(s.y) < 0.0001f ? 1f : Mathf.Abs(s.y),
                            Mathf.Abs(s.z) < 0.0001f ? 1f : Mathf.Abs(s.z));
                    }

                    var label = view.FloorUi.Find("Label")?.GetComponent<Text>();
                    if (label != null)
                    {
                        label.text = view.Floor + " этаж";
                        label.raycastTarget = false;
                        // При scale.x < 0 у родителя UpperLeft визуально справа — якорим в UpperRight.
                        label.alignment = TextAnchor.UpperRight;
                        var labelRt = label.rectTransform;
                        labelRt.anchorMin = new Vector2(0.38f, 0.72f);
                        labelRt.anchorMax = new Vector2(0.96f, 0.95f);
                        labelRt.offsetMin = Vector2.zero;
                        labelRt.offsetMax = Vector2.zero;
                    }

                    var state = view.FloorUi.Find("StateText")?.GetComponent<Text>();
                    if (state != null)
                        state.alignment = TextAnchor.MiddleCenter;
                }

                view.BindInteractables();
            }
        }

        private void EnsureWorld()
        {
            if (worldRoot == null)
            {
                var existing = GameObject.Find("Mine3DWorld");
                worldRoot = existing != null
                    ? existing.transform
                    : new GameObject("Mine3DWorld").transform;
            }

            if (shaftController == null)
                shaftController = GetComponent<Mine3DShaftController>()
                                 ?? gameObject.AddComponent<Mine3DShaftController>();

            if (cameraScroller == null)
                cameraScroller = GetComponent<Mine3DCameraScroller>()
                                 ?? gameObject.AddComponent<Mine3DCameraScroller>();

            if (sceneCamera == null)
                sceneCamera = Camera.main;

            // Сцена уже собрана Tools → Создать сцену MineScene3D — не сносим геометрию.
            if (shaftController.ShaftRoot != null)
            {
                ApplyCameraDefaults(sceneCamera, shaftController.ShaftRoot);
                shaftController.SetDifficultyImmediate(shaftController.CurrentDifficulty);
                return;
            }

            var existingShaft = GameObject.Find("MineShaftRoot");
            if (existingShaft != null)
            {
                shaftController.Bind(existingShaft.transform);
                shaftController.SetDifficultyImmediate(Mine3DShaftController.Easy);
                ApplyCameraDefaults(sceneCamera, existingShaft.transform);
                return;
            }

            BuildDemoWorld();
        }

        private void BuildDemoWorld()
        {
            var rock = Mine3DGeometryBuilder.CreateLitMaterial("MineRock", new Color(0.27f, 0.22f, 0.17f), 0.06f, 0.18f);
            var darkRock = Mine3DGeometryBuilder.CreateLitMaterial("MineDarkRock", new Color(0.11f, 0.09f, 0.07f), 0.04f, 0.10f);
            var wood = Mine3DGeometryBuilder.CreateLitMaterial("MineWood", new Color(0.30f, 0.18f, 0.09f), 0.02f, 0.18f);
            var metal = Mine3DGeometryBuilder.CreateLitMaterial("MineMetal", new Color(0.30f, 0.30f, 0.28f), 0.7f, 0.35f);
            var rust = Mine3DGeometryBuilder.CreateLitMaterial("MineRust", new Color(0.42f, 0.22f, 0.10f), 0.3f, 0.22f);
            var lamp = Mine3DGeometryBuilder.CreateUnlitMaterial("MineLamp", new Color(1f, 0.96f, 0.85f));
            var barrier = Mine3DGeometryBuilder.CreateLitMaterial("MineBarrier", new Color(0.20f, 0.16f, 0.12f), 0.55f, 0.25f);
            var easy = Mine3DGeometryBuilder.CreateLitMaterial("MonsterEasy", new Color(0.25f, 0.55f, 0.28f), 0.2f, 0.4f);
            var medium = Mine3DGeometryBuilder.CreateLitMaterial("MonsterMedium", new Color(0.55f, 0.45f, 0.18f), 0.2f, 0.4f);
            var hard = Mine3DGeometryBuilder.CreateLitMaterial("MonsterHard", new Color(0.55f, 0.16f, 0.14f), 0.2f, 0.4f);

            var built = Mine3DGeometryBuilder.Build(worldRoot, rock, metal, rust, lamp, barrier, easy, medium, hard, wood, darkRock);
            shaftController.Bind(built.Root);
            shaftController.SetDifficultyImmediate(Mine3DShaftController.Easy);

            if (sceneCamera != null)
            {
                var lookY = built.TopFloorCenterY;
                sceneCamera.orthographic = false;
                sceneCamera.fieldOfView = CameraFov;
                sceneCamera.nearClipPlane = 0.1f;
                sceneCamera.farClipPlane = 120f;
                sceneCamera.transform.position = new Vector3(0f, lookY, built.CameraZ);
                sceneCamera.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
                sceneCamera.clearFlags = CameraClearFlags.SolidColor;
                sceneCamera.backgroundColor = new Color(0.06f, 0.045f, 0.035f);

                var minY = built.BottomFloorCenterY - Mine3DGeometryBuilder.GetFloorOpenHeight(12) * 0.35f;
                var maxY = built.TopFloorCenterY + Mine3DGeometryBuilder.GetFloorOpenHeight(1) * 0.35f;
                cameraScroller.Configure(sceneCamera, minY, maxY, lookY);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.14f, 0.12f, 0.10f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.06f, 0.045f, 0.035f);
            RenderSettings.fogDensity = 0.035f;
            EnsureFillLight();
        }

        private void ApplyCameraDefaults(Camera cam, Transform shaftRoot)
        {
            if (cam == null) return;
            cam.orthographic = false;
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;

            if (shaftRoot == null || cameraScroller == null) return;
            var topY = Mine3DGeometryBuilder.FloorCenterY(1);
            var bottomY = Mine3DGeometryBuilder.FloorCenterY(Mine3DGeometryBuilder.FloorCount);
            var minY = bottomY - Mine3DGeometryBuilder.GetFloorOpenHeight(12) * 0.35f;
            var maxY = topY + Mine3DGeometryBuilder.GetFloorOpenHeight(1) * 0.35f;
            var lookY = cam.transform.position.y;
            cameraScroller.Configure(cam, minY, maxY, lookY);
        }

        private static void RebindAllFloorInteractables()
        {
            var views = FindObjectsByType<Mine3DFloorView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < views.Length; i++)
                views[i]?.BindInteractables();
        }

        private void WireDifficultyTabsFallback()
        {
            WireOneDiff("easy");
            WireOneDiff("medium");
            WireOneDiff("hard");
        }

        private void WireOneDiff(string id)
        {
            var go = GameObject.Find("Diff_" + id);
            if (go == null) return;
            var btn = go.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnDifficultyTabFallback(id));
        }

        private void OnDifficultyTabFallback(string difficulty)
        {
            var mine = FindFirstObjectByType<Project.UI.MineSceneController>(FindObjectsInactive.Include);
            if (mine != null)
            {
                mine.RequestDifficultyFromUi(difficulty);
                return;
            }

            if (shaftController != null)
            {
                shaftController.SetDifficultyAnimated(difficulty);
                Mine3DUiBevel.ApplyDifficultySelection(difficulty);
            }
        }

        private void EnsureUiOverlay()
        {
            if (GameObject.Find("DifficultyTabs") != null)
                return;

            var canvasGo = new GameObject("Mine3DCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform as RectTransform;

            var tabs = CreateUiPanel(root, "DifficultyTabs", new Color(0.12f, 0.10f, 0.09f, 0.96f),
                new Vector2(0.06f, 0.895f), new Vector2(0.94f, 0.985f));
            Mine3DUiBevel.StyleTabsHousing(tabs);
            var hl = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f;
            hl.padding = new RectOffset(14, 14, 10, 10);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlHeight = true;
            hl.childControlWidth = true;
            hl.childForceExpandHeight = true;
            hl.childForceExpandWidth = true;

            Mine3DUiBevel.CreateBevelTab(tabs, "easy", "ЛЁГКАЯ", new Color(0.18f, 0.55f, 0.28f, 0.98f));
            Mine3DUiBevel.CreateBevelTab(tabs, "medium", "СРЕДНЯЯ", new Color(0.22f, 0.22f, 0.24f, 0.98f));
            Mine3DUiBevel.CreateBevelTab(tabs, "hard", "ТЯЖЁЛАЯ", new Color(0.22f, 0.22f, 0.24f, 0.98f));

            CreateUiText(root, "Hint", "свайп / колесо — этажи   ·   рычаги — поворот шахты",
                22, new Color(0.75f, 0.78f, 0.82f, 0.85f), TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.845f), new Vector2(0.9f, 0.888f));

            CreateBackButton(root);
        }

        private static void CreateBackButton(Transform parent)
        {
            var go = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.03f, 0.02f);
            rt.anchorMax = new Vector2(0.28f, 0.08f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            CreateUiText(go.transform, "Label", "Назад", 30, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));
        }

        private static RectTransform CreateUiPanel(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private static void CreateUiText(Transform parent, string name, string value, int size, Color color,
            TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
        }

        private void EnsureFillLight()
        {
            if (GameObject.Find("Mine3D_AmbientFill") != null) return;
            var go = new GameObject("Mine3D_AmbientFill");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.28f;
            light.color = new Color(0.55f, 0.48f, 0.40f);
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(40f, -20f, 0f);
        }

        private static void EnsureEventSystem()
        {
            var es = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                es = go.GetComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            var old = es.GetComponent<StandaloneInputModule>();
            if (old != null)
                Destroy(old);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            if (es.GetComponent<StandaloneInputModule>() == null)
                es.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
