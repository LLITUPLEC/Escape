using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    /// <summary>Actual arena snapshot renderer in an isolated Edit-mode scene. Never authenticates, polls, joins or leaves on the server.</summary>
    public static class ExpeditionTournamentQueueQa0313
    {
        private const int Width = 1080;
        private static int Height = 1920;
        private static string Output => ".cache/qa/legacy-0.3.13/tournament-queue/" + Height;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type BridgeType = typeof(ArenaMatch8Bridge);
        private static readonly Type PollType = BridgeType.GetNestedType("ArenaPollResponse", BindingFlags.NonPublic);
        [Serializable] private sealed class Pair
        {
            public int slot; public string uid_a, uid_b, display_a, display_b, status = "pending";
            public bool hp_known_a = true, hp_known_b = true;
            public float hp_a = 1200, hp_b = 1200, hp_max_a = 1200, hp_max_b = 1200;
        }
        [Serializable] private sealed class Tournament
        {
            public bool active, eliminated;
            public string id, phase = "countdown", next_round = "qf", bet_tier, kind;
            public string join_match_id = "offline-qa-must-never-arm-a-scene";
            public int countdown_left = 27, rules_version = 2, prize_ingots = 20, consolation_ingots = 2, recipe_fallback_ingots = 10;
            public string recipe_rarity = "green";
            public Pair[] qf;
        }
        [Serializable] private sealed class Poll
        {
            public bool ok = true, in_queue;
            public int queue_count, queue_max = 8, rules_version = 2;
            public string queue_kind, queue_bet_tier;
            public Tournament tournament;
        }
        [Serializable] private sealed class Check { public string name, details; public bool passed; }
        [Serializable] private sealed class Shot { public string path, title, counter, bracketSummary; public bool queueVisible, leaveVisible, bracketVisible; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkOrAccountActions, preferenceWrites, sceneArmActions;
            public string checkedUtc, error, pendingJoinFixture;
            public int width = Width, height = Height;
            public List<Check> checks = new List<Check>();
            public List<Shot> screenshots = new List<Shot>();
        }

        [MenuItem("Tools/Expedition/Capture Tournament Queue 0313")]
        public static void Capture() { Run("capture"); }

        public static string Run(string target)
        {
            if (target != "capture" && target != "tall" && !string.IsNullOrEmpty(target)) throw new ArgumentException("Use target capture or tall.");
            Height = target == "tall" ? 2400 : 1920;
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Tournament QA requires Edit mode.");
            Directory.CreateDirectory(Output);
            var previousScene = SceneManager.GetActiveScene();
            var previousTarget = RenderTexture.active;
            var activeField = BridgeType.GetField("_activeBridge", BindingFlags.Static | BindingFlags.NonPublic);
            var previousActive = activeField.GetValue(null);
            var bootstraps = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            RenderTexture texture = null; Camera camera = null;
            try
            {
                camera = new GameObject("Tournament0313QaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel;
                camera.orthographic = true; camera.orthographicSize = Height * .5f; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10);
                texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("Tournament0313QaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1; canvas.scaleFactor = 1;
                var background = ExpeditionUi.Fill(canvas.transform, "Background", 0, 0, 1, 1, Color.white);
                background.sprite = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
                if (background.sprite == null) background.color = ExpeditionTheme.Panel;
                ExpeditionUi.Text(canvas.transform, "Header", "АРЕНА", 42, .05f, .93f, .95f, .98f, TextAlignmentOptions.Center).color = ExpeditionTheme.Amber;
                var mode = ExpeditionUi.Rect(canvas.transform, "ModePanel", .035f, .32f, .965f, .87f).gameObject;
                var back = ExpeditionUi.Button(canvas.transform, "BackButton", "Назад", .24f, .075f, .76f, .13f, null).gameObject;
                var stats = ExpeditionUi.Rect(mode.transform, "Match3StatsCard", 0, 0, 1, .1f).gameObject;
                var queue = ExpeditionUi.Text(mode.transform, "queue", "", 42, .08f, .19f, .92f, .25f, TextAlignmentOptions.Center);
                var bridgeGo = new GameObject("InactiveOfflineArenaBridge"); bridgeGo.SetActive(false);
                var bridge = bridgeGo.AddComponent<ArenaMatch8Bridge>();
                // Inactive MonoBehaviour: Awake/OnEnable cannot create NakamaBootstrap or start polling.
                Set(bridge, "_overlayCanvas", canvas); Set(bridge, "_queueTmp", queue);
                Set(bridge, "_modePanelGo", mode); Set(bridge, "_backButtonGo", back); Set(bridge, "_statsCardGo", stats);
                Set(bridge, "_myUserId", "offline-queue-player");
                activeField.SetValue(null, bridge);
                CheckState(report, !bridgeGo.activeInHierarchy, "Offline bridge lifecycle remains inactive");

                var tiers = new[] { new[] { "ore", "500", "Турнир руды", "500 руды" }, new[] { "ore", "2000", "Турнир руды", "2 000 руды" },
                    new[] { "gold", "600", "Турнир золота", "600 золота" }, new[] { "gold", "2400", "Турнир золота", "2 400 золота" },
                    new[] { "smith", "green", "Турнир кузнеца", "Зелёные слитки" }, new[] { "smith", "blue", "Турнир кузнеца", "Синие слитки" },
                    new[] { "smith", "purple", "Турнир кузнеца", "Фиолетовые слитки" } };
                Set(bridge, "_selectedArenaKind", "smith"); Set(bridge, "_previewBetTier", "green"); Set(bridge, "_busyJoin", true);
                var pendingFixture = new Poll { queue_kind = "smith", queue_count = 6, tournament = new Tournament { active = false } };
                report.pendingJoinFixture = JsonUtility.ToJson(pendingFixture);
                Apply(bridge, pendingFixture);
                AssertQueue(report, bridge, true, false, "Pending join does not expose leave before server acknowledgement");
                CheckState(report, queue.text == "Вход в турнир…", "Pending join preserves its own status over an idle poll");
                Save(report, bridge, camera, texture, "queue-joining"); Set(bridge, "_busyJoin", false);
                foreach (var tier in tiers)
                {
                    Apply(bridge, Queue(tier[0], tier[1], 3));
                    AssertQueue(report, bridge, true, true, tier[0] + "/" + tier[1] + " owns count and leave");
                    var title = Get<TMP_Text>(bridge, "_queueTournamentTitle");
                    var actualTitle = title.text.Replace('\u00a0', ' ').Replace('\u202f', ' ');
                    CheckState(report, actualTitle.Contains(tier[2]) && actualTitle.Contains(tier[3]) && queue.text == "3/8", tier[0] + "/" + tier[1] + " name and selected level");
                    CheckState(report, title.transform.parent.name == "ArenaQueueTournamentTitleFrame" && title.transform.parent.parent == canvas.transform && queue.transform.parent == canvas.transform, "Queue stays outside hidden menu: " + tier[1]);
                    Save(report, bridge, camera, texture, "queue-" + tier[0] + "-" + tier[1]);
                }
                foreach (var kind in new[] { "ore", "gold", "smith" })
                {
                    var tier = kind == "ore" ? "2000" : kind == "gold" ? "2400" : "purple";
                    Apply(bridge, Queue(kind, tier, 7));
                    var tournament = Bracket(kind, tier);
                    var snapshot = new Poll { queue_kind = kind, queue_bet_tier = tier, queue_count = 6, tournament = tournament };
                    Apply(bridge, snapshot);
                    AssertQueue(report, bridge, false, false, kind + " bracket excludes a populated foreign queue");
                    var bracket = Get<GameObject>(bridge, "_bracketRoot");
                    CheckState(report, bracket != null && bracket.activeInHierarchy, kind + " bracket is visible");
                    var summary = bracket.transform.Find("ExpeditionTournamentSummary")?.GetComponent<TMP_Text>();
                    var expectedStake = kind == "ore" ? "2 000 руды" : kind == "gold" ? "2 400 золота" : "фиолетовые слитки";
                    CheckState(report, summary != null && summary.text.Replace('\u00a0', ' ').Replace('\u202f', ' ').Contains("Взнос: " + expectedStake),
                        kind + " bracket has a human-readable stake instead of a raw tier id");
                    Save(report, bridge, camera, texture, "bracket-" + kind);
                    var rows = Get<Transform>(bridge, "_rowsRoot"); var rowCount = rows.childCount;
                    snapshot.queue_count = 7; Apply(bridge, snapshot);
                    CheckState(report, rows.childCount == rowCount && bracket.activeInHierarchy, kind + " repeated poll keeps bracket without duplicate rows");
                    AssertQueue(report, bridge, false, false, kind + " repeated foreign count never reappears");
                    Invoke(bridge, "OnDisable");
                    CheckState(report, !bracket.activeSelf && !Get<bool>(bridge, "_participationLocked"), kind + " disable clears overlays and input lock");
                    AssertQueue(report, bridge, false, false, kind + " disable clears queue");
                    // Exactly the same signature used to refresh a hidden bracket without showing it again.
                    Apply(bridge, snapshot);
                    CheckState(report, bracket.activeInHierarchy && Get<bool>(bridge, "_participationLocked"), kind + " reconnect restores unchanged bracket");
                    Save(report, bridge, camera, texture, "reconnected-" + kind);
                    snapshot.in_queue = true; Apply(bridge, snapshot);
                    AssertQueue(report, bridge, false, false, kind + " active bracket takes precedence over inconsistent queue membership");
                    snapshot.in_queue = false; tournament.eliminated = true; Apply(bridge, snapshot);
                    CheckState(report, !bracket.activeSelf && !Get<bool>(bridge, "_participationLocked"), kind + " elimination restores menu");
                    AssertQueue(report, bridge, false, false, kind + " elimination does not show another queue");
                    snapshot.in_queue = true; Apply(bridge, snapshot);
                    AssertQueue(report, bridge, true, true, kind + " eliminated old bracket permits a new own queue");
                    Invoke(bridge, "ApplyConfirmedQueueLeave");
                    AssertQueue(report, bridge, false, false, kind + " confirmed leave clears queue and title");
                    CheckState(report, !Get<bool>(bridge, "_participationLocked") && mode.activeSelf && back.activeSelf, kind + " confirmed leave restores navigation");
                    Apply(bridge, new Poll { queue_kind = kind, queue_count = 6 });
                    AssertQueue(report, bridge, false, false, kind + " post-leave idle poll cannot resurrect foreign count");
                }
                Apply(bridge, Queue("smith", "blue", 0));
                CheckState(report, queue.gameObject.activeInHierarchy && queue.text == "0/8", "Authoritative membership remains visible with zero count");
                var clamped = Queue("smith", "blue", 99); clamped.queue_max = 0; Apply(bridge, clamped);
                CheckState(report, queue.text == "8/8", "Invalid queue max falls back to eight and count clamps");
                Invoke(bridge, "OnDisable");
                AssertQueue(report, bridge, false, false, "Disable while queued clears all independently parented queue controls");
                Apply(bridge, Queue("smith", "blue", 4));
                AssertQueue(report, bridge, true, true, "Reconnect in own queue restores title, count and leave");
                Invoke(bridge, "ApplyConfirmedQueueLeave");
                Save(report, bridge, camera, texture, "left-queue");

                var iconPaths = new[] { "ignot_green", "ignot_blue", "ignot_purple" };
                var fields = new[] { "iconGreenIngot", "iconBlueIngot", "iconPurpleIngot" };
                for (var i = 0; i < fields.Length; i++) Set(bridge, fields[i], AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/img/resources_hud/" + iconPaths[i] + ".png"));
                var card = ExpeditionUi.Button(mode.transform, "match3Arena", "", .045f, .35f, .955f, .65f, null);
                ExpeditionUi.Fill(card.transform, "ExpeditionModeIcon", .045f, .46f, .24f, .89f, Color.white);
                ExpeditionUi.Text(card.transform, "ModeTitle", "Турнир кузнеца", 37, .25f, .48f, .95f, .9f, TextAlignmentOptions.MidlineLeft);
                ExpeditionUi.Text(card.transform, "ModeNote", "От 10 слитков · слитки и новые рецепты", 25, .045f, .06f, .955f, .47f, TextAlignmentOptions.Center);
                ArenaMatch8Bridge.ApplySmithTournamentButtonIcons(card.transform);
                ArenaMatch8Bridge.ApplySmithTournamentButtonIcons(card.transform);
                CheckState(report, !card.transform.Find("ExpeditionModeIcon").gameObject.activeSelf, "Smith's old anvil is replaced");
                var images = new List<Image>();
                foreach (var image in card.GetComponentsInChildren<Image>(true)) if (image.name.StartsWith("ExpeditionSmithIngot", StringComparison.Ordinal)) images.Add(image);
                CheckState(report, images.Count == 3, "Smith icon helper is idempotent with exactly three images");
                Canvas.ForceUpdateCanvases();
                foreach (var image in images) CheckState(report, image.gameObject.activeInHierarchy && image.enabled && image.sprite != null &&
                    image.rectTransform.rect.width > 20 && image.rectTransform.rect.height > 20 && !image.raycastTarget, "Visible existing sprite: " + image.name);
                Save(report, bridge, camera, texture, "smith-button-icons");
                var after = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                CheckState(report, after.Length == bootstraps.Length, "No Nakama bootstrap was created");
                foreach (var existing in bootstraps) CheckState(report, Array.IndexOf(after, existing) >= 0, "Existing Nakama lifecycle was preserved");
                CheckState(report, SceneManager.GetActiveScene() == scene && string.IsNullOrEmpty(Get<string>(bridge, "_sceneArmMidGuard")), "No fight was armed and no scene was loaded");
                report.passed = true;
            }
            catch (Exception ex) { report.error = ex.ToString(); throw; }
            finally
            {
                try
                {
                    RenderTexture.active = previousTarget;
                    if (camera != null) camera.targetTexture = null;
                    if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                    // Destroy fixture objects while the isolated scene is still active;
                    // presentation cleanup cannot affect the user's open scene.
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        SceneManager.SetActiveScene(scene);
                        foreach (var root in scene.GetRootGameObjects()) if (root != null) UnityEngine.Object.DestroyImmediate(root);
                        EditorSceneManager.CloseScene(scene, true);
                    }
                    if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                }
                finally { activeField.SetValue(null, previousActive); File.WriteAllText(Path.Combine(Output, "tournament0313-capture-report.json"), JsonUtility.ToJson(report, true)); }
            }
            return JsonUtility.ToJson(report);
        }

        private static Poll Queue(string kind, string tier, int count) => new Poll { in_queue = true, queue_kind = kind, queue_bet_tier = tier, queue_count = count };
        private static Tournament Bracket(string kind, string tier)
        {
            var pairs = new Pair[4];
            for (var i = 0; i < pairs.Length; i++) pairs[i] = new Pair { slot = i + 1, uid_a = i == 0 ? "offline-queue-player" : "qa-a" + i,
                uid_b = "qa-b" + i, display_a = i == 0 ? "Вы" : "Игрок" + (i * 2 + 1), display_b = "Игрок" + (i * 2 + 2) };
            return new Tournament { active = true, id = "offline-" + kind, kind = kind, bet_tier = tier, qf = pairs };
        }
        private static void Apply(ArenaMatch8Bridge bridge, Poll fixture) => Invoke(bridge, "ApplyPollPresentation", JsonUtility.FromJson(JsonUtility.ToJson(fixture), PollType), false);
        private static void AssertQueue(Report report, ArenaMatch8Bridge bridge, bool visible, bool leave, string name)
        {
            var text = Get<TMP_Text>(bridge, "_queueTmp"); var title = Get<TMP_Text>(bridge, "_queueTournamentTitle"); var button = Get<Button>(bridge, "_leaveQueueButton");
            CheckState(report, text != null && text.gameObject.activeInHierarchy == visible && title != null && title.gameObject.activeInHierarchy == visible &&
                button != null && button.gameObject.activeInHierarchy == leave && Get<bool>(bridge, "_queueUiLocked") == leave, name,
                "expected visible=" + visible + ",leave=" + leave + "; actual count=" + Visible(text) + ",title=" + Visible(title) + ",leave=" + Visible(button) +
                ",locked=" + Get<bool>(bridge, "_queueUiLocked") + ",counter=" + (text != null ? text.text : "<missing>"));
        }
        private static string Visible(Component component) => component == null ? "missing" : component.gameObject.activeInHierarchy.ToString();
        private static void Save(Report report, ArenaMatch8Bridge bridge, Camera camera, RenderTexture texture, string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var field in new[] { "_queueTmp", "_queueTournamentTitle" })
            {
                var label = Get<TMP_Text>(bridge, field);
                if (label == null || !label.gameObject.activeInHierarchy) continue;
                label.ForceMeshUpdate();
                CheckState(report, label.textInfo.characterCount > 0 && !label.isTextOverflowing, "Readable queue text: " + name + "/" + field);
                var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                CheckState(report, RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y >= 0 && RectTransformUtility.WorldToScreenPoint(camera, corners[2]).y <= Height,
                    "Queue text fits capture viewport: " + name + "/" + field);
            }
            var queueLabel = Get<TMP_Text>(bridge, "_queueTmp");
            if (queueLabel != null && queueLabel.gameObject.activeInHierarchy)
            {
                var heading = Get<TMP_Text>(bridge, "_queueTournamentTitle");
                var plate = heading.transform.parent as RectTransform;
                CheckState(report, plate != null && plate.name == "ArenaQueueTournamentTitleFrame" && plate.gameObject.activeInHierarchy, "Visible dark title frame: " + name);
                var metal = plate.GetComponentInChildren<ExpeditionDailyLoginFrame>();
                CheckState(report, metal != null && !metal.raycastTarget && !metal.Highlighted, "Dark neutral metal instead of gold-on-gold: " + name);
                var plateCorners = new Vector3[4]; plate.GetWorldCorners(plateCorners);
                CheckState(report, RectTransformUtility.WorldToScreenPoint(camera, plateCorners[0]).y > Height * .74f && RectTransformUtility.WorldToScreenPoint(camera, plateCorners[2]).y < Height * .94f, "Title frame stays near portrait top: " + name);
                var countCorners = new Vector3[4]; var headingCorners = new Vector3[4];
                queueLabel.rectTransform.GetWorldCorners(countCorners); heading.rectTransform.GetWorldCorners(headingCorners);
                CheckState(report, headingCorners[0].y > countCorners[2].y, "Tournament name is above its count: " + name);
                var leaveButton = Get<Button>(bridge, "_leaveQueueButton");
                if (leaveButton != null && leaveButton.gameObject.activeInHierarchy)
                {
                    var leaveCorners = new Vector3[4]; (leaveButton.transform as RectTransform).GetWorldCorners(leaveCorners);
                    CheckState(report, countCorners[0].y > leaveCorners[2].y, "Count is above its leave button: " + name);
                }
            }
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture });
            else camera.Render();
            RenderTexture.active = texture;
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var path = Path.Combine(Output, name + ".png");
            try { pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
            var counter = Get<TMP_Text>(bridge, "_queueTmp"); var title = Get<TMP_Text>(bridge, "_queueTournamentTitle");
            var leave = Get<Button>(bridge, "_leaveQueueButton"); var bracket = Get<GameObject>(bridge, "_bracketRoot");
            var summary = bracket != null && bracket.activeInHierarchy ? bracket.transform.Find("ExpeditionTournamentSummary")?.GetComponent<TMP_Text>() : null;
            if (summary != null)
            {
                summary.ForceMeshUpdate();
                CheckState(report, summary.textInfo.characterCount > 0 && !summary.isTextOverflowing, "Readable bracket summary without overflow: " + name,
                    "text=" + summary.text + "; rect=" + summary.rectTransform.rect + "; fontSize=" + summary.fontSize);
            }
            report.screenshots.Add(new Shot { path = path, title = title != null ? title.text : "", counter = counter != null ? counter.text : "",
                bracketSummary = summary != null ? summary.text : "",
                queueVisible = counter != null && counter.gameObject.activeInHierarchy, leaveVisible = leave != null && leave.gameObject.activeInHierarchy,
                bracketVisible = bracket != null && bracket.activeInHierarchy });
        }
        private static object Invoke(object owner, string name, params object[] args) => BridgeType.GetMethod(name, Hidden).Invoke(owner, args);
        private static T Get<T>(object owner, string name) => (T)BridgeType.GetField(name, Hidden).GetValue(owner);
        private static void Set(object owner, string name, object value) => BridgeType.GetField(name, Hidden).SetValue(owner, value);
        private static void CheckState(Report report, bool passed, string name, string details = null)
        {
            report.checks.Add(new Check { name = name, passed = passed, details = details });
            if (!passed) throw new InvalidOperationException(name + (string.IsNullOrEmpty(details) ? "" : " / " + details));
        }
    }
}
