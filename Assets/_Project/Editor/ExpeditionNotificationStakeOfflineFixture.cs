using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Temporary UI-only Play start scene. The real Duel manager remains disabled: no Start, match, auth or account service is invoked.</summary>
    public static class ExpeditionNotificationStakeOfflineFixture
    {
        private const string Key = "Escape.NotificationStakeQa.";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= ModeChanged;
            EditorApplication.playModeStateChanged += ModeChanged;
        }

        public static string Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare the offline scene in Edit mode.");
            if (SessionState.GetBool(Key + "active", false)) throw new InvalidOperationException("Restore the preceding offline fixture first.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Editor/NotificationStakeQaFixture.unity");
            SessionState.SetString(Key + "path", path);
            SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + "active", true);
            SessionState.SetBool(Key + "hasBackgroundSetting", false);
            try
            {
                var root = new GameObject("OfflineBattlePresentation"); SceneManager.MoveGameObjectToScene(root, scene);
                var manager = root.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                var canvasRoot = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasRoot.transform.SetParent(root.transform, false);
                var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                canvasRoot.GetComponent<GraphicRaycaster>().enabled = false; // This presentation fixture has no gameplay input owner.
                Prefab("Match3BoardView", canvasRoot.transform, scene);
                var player = Prefab("Match3PlayerPanel", canvasRoot.transform, scene); player.name = "FixturePlayer";
                ExpeditionUi.SetRect(player.transform as RectTransform, .01f, .82f, .48f, .99f);
                var opponent = Prefab("Match3PlayerPanel", canvasRoot.transform, scene); opponent.name = "FixtureOpponent";
                ExpeditionUi.SetRect(opponent.transform as RectTransform, .52f, .82f, .99f, .99f);
                Prefab("Match3GameHUD", canvasRoot.transform, scene);
                Prefab("Match3AbilityPanel", canvasRoot.transform, scene);
                var outcome = Prefab("Match3GameOverPanel", canvasRoot.transform, scene);
                outcome.SetActive(false);
                var camera = new GameObject("OfflineQaCamera", typeof(Camera), typeof(AudioListener)); SceneManager.MoveGameObjectToScene(camera, scene);
                camera.tag = "MainCamera";
                camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
                camera.GetComponent<Camera>().backgroundColor = ExpeditionTheme.Panel;
                // Only presentation prefabs and the disabled manager are saved. Never clone a production scene/controller graph.
                if (scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<NakamaBootstrap>(true) != null))
                    throw new InvalidOperationException("The offline fixture unexpectedly contains a network bootstrap.");
                if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save the temporary UI-only scene.");
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                SetGameView();
                SessionState.SetBool(Key + "ready", false);
                return "Offline toast fixture prepared. Original open/unsaved scenes are preserved. Enter Play; the temporary start scene has only battle UI and a disabled Duel manager, at1080x1920.";
            }
            catch { RestoreSettings(); throw; }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject Prefab(string name, Transform parent, Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Match3/" + name + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing production UI prefab: " + name);
            var value = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); value.transform.SetParent(parent, false); return value;
        }

        private static void ModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += ConfigureRunning;
            if (state == PlayModeStateChange.EnteredEditMode) RestoreSettings();
        }

        private static void ConfigureRunning()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                var expected = Path.GetFileNameWithoutExtension(SessionState.GetString(Key + "path", ""));
                if (!EditorApplication.isPlaying || scene.name != expected) throw new InvalidOperationException("Play did not use the isolated UI fixture scene.");
                if (UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) != null)
                    throw new InvalidOperationException("Offline toast fixture found a network bootstrap; stop before running a probe.");
                var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
                if (manager == null || manager.enabled) throw new InvalidOperationException("The offline Duel manager must remain disabled.");
                if (!SessionState.GetBool(Key + "hasBackgroundSetting", false))
                {
                    SessionState.SetBool(Key + "backgroundSetting", Application.runInBackground);
                    SessionState.SetBool(Key + "hasBackgroundSetting", true);
                }
                Application.runInBackground = true; // PlayerSettings defaults false; the probe must render while Codex is foreground.
                typeof(DuelMatch3Manager).GetField("_useLocalBotSimulation", Instance).SetValue(manager, true);
                var board = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>(); board.Build();
                var local = new Match3BoardLogic(); local.Init(241026); board.RefreshAll(local);
                ExpeditionBattleLayout.Install(scene);
                var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.x).ToArray();
                for (var i = 0; i < panels.Length; i++)
                {
                    panels[i].SetPlayerName(i == 0 ? "Скиталец" : "Страж горна");
                    panels[i].UpdateStats(i == 0 ? 820 : 650, 1000, i == 0 ? 80 : 55, 100);
                    panels[i].UpdateCombatStats(42, 18, 7, 12.5f); panels[i].UpdateAvatarLevel(12);
                    if (panels[i].avatarImage != null)
                    {
                        panels[i].avatarImage.sprite = Resources.Load<Sprite>(i == 0 ? "Expedition/Appearance/dawnwarden-male-avatar" : "Expedition/Monsters/mine-8");
                        panels[i].avatarImage.color = Color.white;
                    }
                }
                var hud = UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>(); hud.SetTurn("Ваш ход!"); hud.SetTimerPhase(false); hud.SetTimer("18", 18);
                var abilities = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>();
                abilities.Refresh(new PlayerStats { mana = 80 }, true, false, 20, 20, 30, 40, 30);
                Canvas.ForceUpdateCanvases();
                SessionState.SetBool(Key + "ready", true);
                Debug.Log("Offline toast fixture READY: actual battle prefabs/layout; disabled manager; no Nakama/auth/network controller; " + Screen.width + "x" + Screen.height);
            }
            catch (Exception error)
            {
                Debug.LogError(error); SessionState.SetString(Key + "error", error.ToString());
                EditorApplication.isPlaying = false;
            }
        }

        public static string Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                return "Stopping the offline presentation fixture; original start scene and Game view will restore on returning to Edit mode.";
            }
            RestoreSettings();
            return "Offline fixture removed; original open/unsaved scenes, Play start scene and Game view selection/zoom preserved/restored.";
        }

        public static string Status() => "Prepared=" + SessionState.GetBool(Key + "active", false) +
            "; ready=" + SessionState.GetBool(Key + "ready", false) + "; scene=" + SceneManager.GetActiveScene().name +
            "; view=" + Screen.width + "x" + Screen.height + "; error=" + SessionState.GetString(Key + "error", "");

        private static void RestoreSettings()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            var previous = SessionState.GetString(Key + "startScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            if (SessionState.GetBool(Key + "hasBackgroundSetting", false))
                Application.runInBackground = SessionState.GetBool(Key + "backgroundSetting", false);
            try { RestoreGameView(); }
            finally
            {
                var path = SessionState.GetString(Key + "path", "");
                // The exact generated asset path was recorded before creation; never touch user-owned scenes.
                if (path.StartsWith("Assets/_Project/Editor/NotificationStakeQaFixture", StringComparison.Ordinal) && path.EndsWith(".unity", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(path);
                SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ready", false);
            }
        }

        private static object Sizes()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(type);
            return singleton.GetProperty("instance", Static).GetValue(null);
        }

        private static object Group(object sizes, string groupName) => sizes.GetType().GetMethod("GetGroup", Instance).Invoke(sizes,
            new[] { Enum.Parse(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType", true), groupName) });

        private static EditorWindow GameView(bool create)
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            var existing = Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().FirstOrDefault();
            return existing != null || !create ? existing : EditorWindow.GetWindow(type);
        }

        private static void SetGameView()
        {
            SessionState.SetInt(Key + "focusedWindow", EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetInstanceID() : 0);
            var view = GameView(false); SessionState.SetBool(Key + "createdGameView", view == null); view = view != null ? view : GameView(true);
            var index = view.GetType().GetProperty("selectedSizeIndex", Instance);
            if (index == null) throw new MissingMemberException("GameView.selectedSizeIndex");
            SessionState.SetInt(Key + "viewId", view.GetInstanceID()); SessionState.SetInt(Key + "viewIndex", (int)index.GetValue(view));
            var zoom = view.GetType().GetField("m_ZoomArea", Instance)?.GetValue(view);
            var scale = zoom?.GetType().GetProperty("scale", Instance);
            if (scale?.GetValue(zoom) is Vector2 value) SessionState.SetString(Key + "zoom", JsonUtility.ToJson(new Zoom { x = value.x, y = value.y }));
            var sizes = Sizes();
            var groupValue = sizes.GetType().GetProperty("currentGroupType", Instance)?.GetValue(sizes);
            var groupName = groupValue?.ToString() ?? (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "Android" : "Standalone");
            SessionState.SetString(Key + "group", groupName);
            var group = Group(sizes, groupName);
            var built = (int)group.GetType().GetMethod("GetBuiltinCount", Instance).Invoke(group, null);
            var custom = (int)group.GetType().GetMethod("GetCustomCount", Instance).Invoke(group, null);
            var selected = -1;
            for (var i = 0; i < built + custom; i++)
            {
                var item = group.GetType().GetMethod("GetGameViewSize", Instance).Invoke(group, new object[] { i });
                var mode = item.GetType().GetProperty("sizeType", Instance)?.GetValue(item)?.ToString();
                if (mode == "FixedResolution" && (int)item.GetType().GetProperty("width", Instance).GetValue(item) == 1080 &&
                    (int)item.GetType().GetProperty("height", Instance).GetValue(item) == 1920) { selected = i; break; }
            }
            SessionState.SetInt(Key + "addedSize", -1);
            if (selected < 0)
            {
                var assembly = typeof(UnityEditor.Editor).Assembly;
                var sizeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var sizeClass = assembly.GetType("UnityEditor.GameViewSize", true);
                var item = Activator.CreateInstance(sizeClass, Instance, null, new object[] { Enum.Parse(sizeType, "FixedResolution"), 1080, 1920, "Escape toast QA" }, null);
                group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { item });
                selected = built + custom; SessionState.SetInt(Key + "addedSize", custom);
            }
            index.SetValue(view, selected); view.Show(); view.Focus(); view.Repaint();
        }

        [Serializable] private sealed class Zoom { public float x, y; }

        private static void RestoreGameView()
        {
            var view = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetInstanceID() == SessionState.GetInt(Key + "viewId", 0));
            if (view != null)
            {
                view.GetType().GetProperty("selectedSizeIndex", Instance)?.SetValue(view, SessionState.GetInt(Key + "viewIndex", 0));
                var zoom = view.GetType().GetField("m_ZoomArea", Instance)?.GetValue(view);
                var scale = zoom?.GetType().GetProperty("scale", Instance);
                var json = SessionState.GetString(Key + "zoom", "");
                if (scale != null && scale.CanWrite && !string.IsNullOrEmpty(json))
                {
                    var value = JsonUtility.FromJson<Zoom>(json); scale.SetValue(zoom, new Vector2(value.x, value.y));
                }
                if (SessionState.GetBool(Key + "createdGameView", false)) view.Close(); else view.Repaint();
            }
            var custom = SessionState.GetInt(Key + "addedSize", -1);
            var name = SessionState.GetString(Key + "group", "");
            if (custom >= 0 && !string.IsNullOrEmpty(name))
            {
                var group = Group(Sizes(), name);
                group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { custom });
            }
            var previousFocus = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetInstanceID() == SessionState.GetInt(Key + "focusedWindow", 0));
            if (previousFocus != null) previousFocus.Focus();
        }
    }
}
