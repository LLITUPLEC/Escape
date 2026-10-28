using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Project.Achievements;
using Project.Character;
using Project.Character.UI;
using Project.Friends;
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
    /// <summary>Actual Shelter C factory, fresh profile binder and live badges in disposable camera canvases. No transport or persisted account caches.</summary>
    public static class ExpeditionShelterConceptCQa
    {
        private const string Output = ".cache/qa/shelter-concept-c";
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites, preferenceWrites;
            public string utc, error;
            public List<string> checks = new(), screenshots = new(), runtimeErrors = new();
        }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Output);
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            var originalScene = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            void ObserveError(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) report.runtimeErrors.Add(message); }
            var saved = new Dictionary<FieldInfo, object>();
            SaveStatics(saved, typeof(NakamaBootstrap), "<Instance>k__BackingField");
            SaveStatics(saved, typeof(ExpeditionResourceHeader), "_hydratedAccountScope");
            SaveStatics(saved, typeof(AccountGender), "_scope", "_loaded", "_female", "_canChoose", "_unsupported");
            SaveStatics(saved, typeof(MailService), "_scope", "_state", "_busy", "_nextRefresh");
            SaveStatics(saved, typeof(DailyRewardsService), "_scope", "_state", "_busy", "_receivedAt");
            SaveStatics(saved, typeof(PlayerGuidanceService), "_scope", "_current", "_fetching", "_nextRefresh");
            SaveStatics(saved, typeof(AchievementProgressStorage), "_stats", "_claimedStepTokens", "_loadedScope", "_confirmedClaimRevision");
            try
            {
                Application.logMessageReceived += ObserveError;
                Check(Resources.Load<Sprite>("Expedition/Art/shelter-mine-light") != null, "Selected C backdrop exists as shipped artwork without baked UI", report);
                foreach (var height in new[] { 1920, 2400 })
                foreach (var inset in new[] { false, true }) RunProfile(height, inset, report);
                Check(report.runtimeErrors.Count == 0, "Production C construction/render emits no errors: " + string.Join(" | ", report.runtimeErrors.Take(3)), report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                Application.logMessageReceived -= ObserveError;
                foreach (var field in saved) field.Key.SetValue(null, field.Value);
                RenderTexture.active = previousTexture;
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                File.WriteAllText(Output + "/shelter-c-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " Shelter C checks passed with " + report.screenshots.Count + " native 1080x1920/2400 captures, including safe-area profiles. No transport, auth, account or preference actions.";
        }

        private static void RunProfile(int height, bool inset, Report report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = "MainMenu";
            SceneManager.SetActiveScene(scene);
            var previousTexture = RenderTexture.active; Camera camera = null; RenderTexture target = null; NakamaConnectionConfig config = null;
            var name = "1080x" + height + (inset ? "-safe" : "");
            try
            {
                Static(typeof(NakamaBootstrap), "<Instance>k__BackingField", null);
                Static(typeof(AccountGender), "_scope", null); Static(typeof(AccountGender), "_loaded", false);
                SeedAchievements(null);
                var inactive = new GameObject("DisabledShelterIdentityAndLegacyOwner"); inactive.SetActive(false);
                var menu = inactive.AddComponent<MainMenuController>(); menu.enabled = false;
                camera = new GameObject("ShelterCNativeCamera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = height * .5f; camera.transform.position = new Vector3(0, 0, -10);
                camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var root = new GameObject("ShelterCNativeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = ConfigureCanvas(root, camera);
                canvas.sortingOrder = 70; // Match the shipped ExpeditionUi canvas above physical-screen World(5).
                var content = ExpeditionUi.Rect(root.transform, "ExpeditionSafeArea", inset ? 18f / 1080 : 0, inset ? 40f / height : 0, inset ? 1 - 18f / 1080 : 1, inset ? 1 - 80f / height : 1);
                var body = ExpeditionUi.Rect(content, "ExpeditionScenePageHost", 0, 0, 1, 1);
                var modalHost = ExpeditionUi.Rect(root.transform, "ExpeditionModalHost", 0, 0, 1, 1);
                var header = content.gameObject.AddComponent<ExpeditionResourceHeader>(); header.enabled = false;
                var build = typeof(ExpeditionResourceHeader).GetMethods(I).Single(method => method.Name == "Build" && method.GetParameters().Length == 4);
                // No bootstrap exists while Build starts its fetches: every service returns not_initialized synchronously.
                // A disabled legacy menu suppresses the scene-owned resource polling coroutine.
                build.Invoke(header, new object[] { content, modalHost, menu, true });
                Check(Label(header, "_name") == "Загрузка…" && new[] { "_energy", "_gold", "_ore", "_matter" }.All(field => Label(header, field).Contains("—")), "Unhydrated C header has neutral identity and all four resource placeholders " + name, report);

                var bootstrap = inactive.AddComponent<NakamaBootstrap>();
                config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; bootstrap.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                typeof(global::Nakama.Session).GetField("<UserId>k__BackingField", I).SetValue(session, "11111111-2222-3333-4444-555555555555");
                Set(bootstrap, "<Session>k__BackingField", session); Static(typeof(NakamaBootstrap), "<Instance>k__BackingField", bootstrap);
                var scope = LocalAccountCacheScope.Current;
                SeedServices(scope); SeedAchievements(scope);
                Set(header, "_accountScope", scope);
                var profile = JsonUtility.FromJson<CharacterGetRpcResponse>("{\"ok\":true,\"progression\":{\"level\":8,\"xp\":25000}}");
                var nickname = JsonUtility.FromJson<NicknameStatusRpcResponse>("{\"ok\":true,\"username\":\"ProKrust\"}");
                var resources = JsonUtility.FromJson<PlayerResourcesRpcResponse>("{\"ok\":true,\"ore\":12450,\"gold\":2380,\"matter\":160,\"energy\":8,\"energy_max\":100}");
                var applied = (bool)Call(header, "TryApplyHydration", profile, nickname, resources, scope, (int)Get(header, "_profileVersion"), CancellationToken.None);
                Check(applied && header.HydratedCurrentAccount && Label(header, "_name") == "ProKrust" && Label(header, "_level") == "Уровень 8", "Actual complete same-account server DTOs hydrate the C profile " + name, report);
                Check(Label(header, "_ore") == CompactResourceAmount.Format(12450) && Label(header, "_gold") == "2380" && Label(header, "_matter") == "160" && new string(Label(header, "_energy").Where(character => !char.IsWhiteSpace(character)).ToArray()) == "8/100", "All four C resource cells render authoritative wallet values and energy capacity " + name, report);
                var xpText = new string(Label(header, "_xpValue").Where(character => !char.IsWhiteSpace(character)).ToArray());
                Check(xpText.Contains("4292") && xpText.Contains("24950"), "XP label uses actual level thresholds rather than the concept illustration numbers " + name, report);

                var presenterRoot = new GameObject("DisabledShelterCPresentation"); presenterRoot.SetActive(false);
                var presenter = presenterRoot.AddComponent<ExpeditionPresentation>(); presenter.enabled = false;
                Set(presenter, "_scene", scene); Set(presenter, "_canvas", canvas); Set(presenter, "_content", content); Set(presenter, "_sceneBody", body); Set(presenter, "_modalHost", modalHost);
                var clicks = 0;
                var rating = ExpeditionUi.Button(inactive.transform, "RatingButton", "", 0, 0, 1, 1, () => clicks++);
                ExpeditionUi.Button(inactive.transform, "FriendsButton", "", 0, 0, 1, 1, () => clicks++);
                var pending = ExpeditionUi.Rect(rating.transform, "PendingClaimBadge", 0, 0, 1, 1); pending.gameObject.AddComponent<CanvasGroup>().alpha = 1;
                ExpeditionUi.Text(pending, "Count", "2", 25, 0, 0, 1, 1);
                Call(presenter, "BuildShelter");
                var ratingButton = Find(body, "ExpeditionOpenRating").GetComponent<Button>();
                var friendButton = Find(body, "ExpeditionOpenFriends").GetComponent<Button>();
                ratingButton.onClick.Invoke(); friendButton.onClick.Invoke();
                Check(clicks == 2, "Actual rating and friends shortcuts preserve both original button callbacks " + name, report);
                var friendState = new FriendsIncomingInviteState(() => scope); friendState.SynchronizeAccount(); friendState.Publish(friendState.AccountScope, friendState.Generation, 3);
                var friendBadge = FriendsNavigationBadge.BindState(friendButton, friendState);
                Check(friendBadge.IsVisible && friendBadge.PendingCount == 3 && ratingButton.transform.Find("ExpeditionPendingBadge").gameObject.activeSelf, "C layout preserves trusted friend invites and original rating claim badges " + name, report);
                Check(Find(body, "ExpeditionOpenMail").Find("ExpeditionMailBadge").GetComponent<CanvasGroup>().alpha > 0 && Find(body, "ExpeditionDailyQuests").GetComponent<DailyQuestBadge>().PendingCount == 3, "C layout displays server-scoped unread mail and ready daily-quest counts " + name, report);
                var loginBadge = Find(body, "ExpeditionDailyLogin").GetComponentInChildren<ExpeditionDailyLoginBadge>();
                Check(loginBadge != null && loginBadge.IsVisible && !loginBadge.raycastTarget, "C login reward has a live non-blocking red notification dot " + name, report);
                var daily = DailyRewardsService.Current; daily.login.can_claim = false; loginBadge.Refresh();
                Check(!loginBadge.IsVisible, "Claimed login reward clears the C notification dot " + name, report);
                daily.login.can_claim = true; var reset = daily.reset_at; daily.reset_at = daily.now; loginBadge.Refresh();
                Check(!loginBadge.IsVisible, "Expired daily snapshot cannot keep a stale login notification visible " + name, report);
                daily.reset_at = reset; loginBadge.Refresh();
                var resourceClicks = new List<string>();
                Set(header, "EditorResourceClickOverride", new Action<string>(resource => resourceClicks.Add(resource)));
                foreach (var resource in new[] { "ore", "gold", "matter", "energy" })
                {
                    var chip = Find(content, "Resource_" + resource); var plus = chip.Find("Plus").GetComponent<Button>();
                    plus.onClick.Invoke();
                    Check(plus.targetGraphic != null && plus.targetGraphic.raycastTarget && chip.Find("ResourceIcon").GetComponent<Image>().sprite != null, "Resource plus retains a real touch target and canonical icon: " + resource + "/" + name, report);
                }
                Check(resourceClicks.SequenceEqual(new[] { "ore", "gold", "matter", "energy" }), "All four actual Plus.onClick chains reach the correct production resource route before the intercepted external boundary " + name, report);
                Set(header, "EditorResourceClickOverride", null);

                foreach (var binding in root.GetComponentsInChildren<CharacterAppearanceImage>(true))
                {
                    binding.enabled = false; var image = binding.GetComponent<Image>();
                    image.sprite = (bool)Get(binding, "_avatar") ? ExpeditionTheme.Icon("hero") : Resources.Load<Sprite>("Expedition/Art/hero");
                    image.enabled = true; image.color = Color.white;
                }
                // The presentation owns a separate physical-screen world canvas.
                foreach (var other in scene.GetRootGameObjects().SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<Canvas>(true))) ConfigureCanvas(other.gameObject, camera);
                Canvas.ForceUpdateCanvases();
                var world = (ExpeditionSceneWorld)Get(presenter, "_world"); Call(world, "LateUpdate"); Canvas.ForceUpdateCanvases();
                Call(header, "LayoutShelterHeader"); Canvas.ForceUpdateCanvases();
                Check(canvas.sortingOrder > world.GetComponent<Canvas>().sortingOrder, "HUD sorting stays above the independent physical world, as in production " + name, report);
                Save(scene, camera, target, name, report);
                VerifyGeometry(content, body, header, world, name, report);
                profile.progression.level = PlayerLevelXpTable.MaxLevel; profile.progression.xp = PlayerLevelXpTable.Thresholds.Last();
                Call(header, "TryApplyHydration", profile, nickname, resources, scope, (int)Get(header, "_profileVersion"), CancellationToken.None);
                Check(Label(header, "_xpValue") == "MAX", "C profile handles the actual maximum game level without inventing a next XP threshold " + name, report);
                Call(header, "SetShelterExpanded", false);
                Canvas.ForceUpdateCanvases(); Call(header, "LayoutShelterHeader"); Canvas.ForceUpdateCanvases();
                Check(new[] { "ore", "gold", "matter", "energy" }.All(resource => !Find(content, "Resource_" + resource).gameObject.activeInHierarchy), "Hero and other overlays can collapse the C resources row without losing profile data " + name, report);
                VerifyText((RectTransform)Get(header, "_profileBar"), name + "-collapsed-profile", report);
                Call(header, "SetShelterExpanded", true);
                Check(Label(header, "_name") == "ProKrust" && Find(content, "Resource_ore").gameObject.activeInHierarchy, "Expanded C header restores server data after overlay collapse " + name, report);
                Check(bootstrap.Client == null, "No authentication or network client was created by any C UI binding/callback " + name, report);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previousTexture;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
                Static(typeof(NakamaBootstrap), "<Instance>k__BackingField", null);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
            }
        }

        private static void SeedServices(string scope)
        {
            Static(typeof(MailService), "_scope", scope); Static(typeof(MailService), "_busy", false);
            Static(typeof(MailService), "_state", new MailService.State { ok = true, unread_count = 3, messages = Enumerable.Range(1, 3).Select(index => new MailService.Message { id = "offline-mail-" + index, title = "Письмо", body = "Уведомление", created_at = 1000 }).ToArray() });
            Static(typeof(DailyRewardsService), "_scope", scope); Static(typeof(DailyRewardsService), "_busy", false); Static(typeof(DailyRewardsService), "_receivedAt", Time.realtimeSinceStartup);
            Static(typeof(DailyRewardsService), "_state", new DailyRewardsService.State { ok = true, now = 1000, reset_at = 87400, day = 1,
                login = new DailyRewardsService.Login { can_claim = true, next_day = 2, rewards = Enumerable.Range(0, 7).Select(_ => new DailyRewardsService.Reward()).ToArray() },
                quests = Enumerable.Range(1, 3).Select(index => new DailyRewardsService.Quest { id = "offline-quest-" + index, label = "Задание", ready = true, progress = 1, target = 1, reward = new DailyRewardsService.Reward() }).ToArray() });
            Static(typeof(PlayerGuidanceService), "_scope", scope); Static(typeof(PlayerGuidanceService), "_fetching", false);
            Static(typeof(PlayerGuidanceService), "_current", new PlayerGuidanceService.State { ok = true, schema = 1, workshop_unlocked = true, tournaments_unlocked = true, tutorial_status = "completed", mine_wins = 10 });
        }
        private static void SeedAchievements(string scope)
        {
            Static(typeof(AchievementProgressStorage), "_loadedScope", scope);
            Static(typeof(AchievementProgressStorage), "_stats", new Dictionary<string, int>(StringComparer.Ordinal));
            Static(typeof(AchievementProgressStorage), "_claimedStepTokens", new HashSet<string>(StringComparer.Ordinal));
        }
        private static void VerifyGeometry(RectTransform safe, RectTransform body, ExpeditionResourceHeader header, ExpeditionSceneWorld world, string name, Report report)
        {
            Check(world.Art == "shelter-mine-light" && world.Illustration.sprite == Resources.Load<Sprite>("Expedition/Art/shelter-mine-light"), "Actual BuildShelter selects the chosen C background " + name, report);
            Check(Contains(BoundsIn(world.Illustration.rectTransform, world.transform as RectTransform), (world.transform as RectTransform).rect), "C world covers the physical viewport " + name, report);
            var chips = new[] { "ore", "gold", "matter", "energy" }.Select(resource => Find(safe, "Resource_" + resource) as RectTransform).ToArray();
            for (var index = 0; index < chips.Length; index++)
            {
                Check(Contains(safe.rect, BoundsIn(chips[index], safe)), "Resource chip stays inside device safe area " + index + "/" + name, report);
                if (index > 0) Check(BoundsIn(chips[index - 1], safe).xMax <= BoundsIn(chips[index], safe).xMin + .2f, "Four resource chips remain separate columns " + name, report);
                var plus = chips[index].Find("Plus") as RectTransform;
                Check(plus.rect.width >= 40 && plus.rect.height >= 40, "Resource Plus has an accessible touch area " + index + "/" + name, report);
            }
            var hero = Find(body, "ShelterHero").GetComponent<Image>();
            Check(hero.sprite != null && ExpeditionCombatantGrounding.TryGet(hero.sprite, out _), "C explorer uses actual shipped alpha geometry " + name, report);
            var solid = SolidBounds(hero, body); var journey = Find(body, "ShelterJourney") as RectTransform;
            Check(Contains(body.rect, solid), "Explorer opaque bounds remain inside the safe viewport on this phone ratio " + name, report);
            Check(solid.yMin >= BoundsIn(journey, body).yMax - 1, "Explorer feet remain above the journey card " + name, report);
            var shadow = Find(body, "ShelterContactShadow").GetComponent<Image>();
            Check(shadow.enabled && !shadow.raycastTarget && shadow.sprite == ExpeditionCombatantFigure.ContactShadow && shadow.sprite.texture.width == 64 && shadow.sprite.texture.height == 16 && shadow.transform.GetSiblingIndex() < hero.transform.GetSiblingIndex(), "C explorer has the shared lightweight contact shadow drawn behind its boots " + name, report);
            Check(Contains(body.rect, BoundsIn(shadow.rectTransform, body)) && BoundsIn(shadow.rectTransform, body).yMin >= BoundsIn(journey, body).yMax - 1, "Contact shadow stays inside the safe viewport and above the journey card " + name, report);
            foreach (var shortcut in new[] { "ExpeditionOpenRating", "ExpeditionOpenFriends", "ExpeditionOpenMail" })
            {
                var bounds = BoundsIn(Find(body, shortcut) as RectTransform, body);
                Check(Contains(body.rect, bounds) && bounds.xMax <= solid.xMin + 1, "Vertical side shortcut stays visible beside the explorer: " + shortcut + "/" + name, report);
            }
            var nav = Find(safe, "ExpeditionNavigation") as RectTransform;
            Check(Contains(safe.rect, BoundsIn(nav, safe)) && nav.GetComponentsInChildren<Button>().Length == 5, "All five navigation destinations stay inside the safe area " + name, report);
            VerifyText(safe, name, report);
        }
        private static void VerifyText(Transform host, string name, Report report)
        {
            foreach (var text in host.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                var path = string.Join("/", text.GetComponentsInParent<Transform>(true).Reverse().Select(parent => parent.name));
                Check(!text.isTextOverflowing, "C text fits its actual rectangle: " + path + " / " + name + " text=[" + text.text + "] rect=" + text.rectTransform.rect.size + " font=" + text.fontSize + " preferred=" + text.preferredWidth + "x" + text.preferredHeight, report);
            }
        }
        private static Rect SolidBounds(Image image, RectTransform parent)
        {
            ExpeditionCombatantGrounding.TryGet(image.sprite, out var footprint); var solid = footprint.SolidBounds; var rect = image.rectTransform.rect;
            var low = parent.InverseTransformPoint(image.rectTransform.TransformPoint(new Vector2(rect.xMin + rect.width * solid.xMin, rect.yMin + rect.height * solid.yMin)));
            var high = parent.InverseTransformPoint(image.rectTransform.TransformPoint(new Vector2(rect.xMin + rect.width * solid.xMax, rect.yMin + rect.height * solid.yMax)));
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }
        private static Canvas ConfigureCanvas(GameObject root, Camera camera)
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f; }
            return canvas;
        }
        private static void Save(Scene scene, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var hud = scene.GetRootGameObjects().Single(root => root.name == "ShelterCNativeCanvas").GetComponent<Canvas>();
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                var rendered = pixels.GetPixels32();
                var colors = rendered.Where((_, index) => index % 997 == 0).Select(color => (color.r << 16) | (color.g << 8) | color.b).Distinct().Count();
                Check(colors > 32, "Native C capture contains rendered artwork and HUD " + name, report);
                var path = Output + "/shelter-c-" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path);
                // A rich painted background is insufficient evidence of visible controls.
                // Compare the real frame with the same world rendered after disabling only the HUD canvas.
                hud.enabled = false; Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                var backgroundOnly = pixels.GetPixels32(); var changed = 0;
                for (var index = 0; index < rendered.Length; index += 997)
                {
                    var before = rendered[index]; var after = backgroundOnly[index];
                    if (Mathf.Abs(before.r - after.r) + Mathf.Abs(before.g - after.g) + Mathf.Abs(before.b - after.b) > 15) changed++;
                }
                Check(changed > 100, "HUD/hero genuinely render above the background: " + changed + " changed native pixel samples in " + name, report);
                hud.enabled = true; Canvas.ForceUpdateCanvases(); Render();
                var portrait = Find(hud.transform, "ExpeditionOpenHero").Find("PortraitMask/Portrait").GetComponent<Image>();
                var rim = Find(hud.transform, "ExpeditionOpenHero").Find("PortraitRim").GetComponent<ExpeditionCircleGraphic>();
                var dot = hud.GetComponentInChildren<ExpeditionDailyLoginBadge>();
                foreach (var graphic in new MaskableGraphic[] { portrait.transform.parent.GetComponent<ExpeditionCircleGraphic>(), rim, dot })
                {
                    var renderer = graphic.GetComponent<CanvasRenderer>();
                    Check(renderer != null && renderer.GetMesh() != null && renderer.GetMesh().vertexCount > 0, "Real CanvasRenderer mesh exists for visible C graphic: " + graphic.name + "/" + name, report);
                }
                Check(portrait.enabled && portrait.sprite == ExpeditionTheme.Icon("hero"), "Portrait probe uses the actual shipping default avatar sprite " + name, report);
                void VerifyLocalPixels(Graphic graphic, int minimum, string label)
                {
                    try
                    {
                        graphic.enabled = false; Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                        var hidden = pixels.GetPixels32(); var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                        var screen = corners.Select(corner => camera.WorldToScreenPoint(corner)).ToArray();
                        var x0 = Mathf.Clamp(Mathf.FloorToInt(screen.Min(point => point.x)), 0, target.width - 1);
                        var x1 = Mathf.Clamp(Mathf.CeilToInt(screen.Max(point => point.x)), 0, target.width - 1);
                        var y0 = Mathf.Clamp(Mathf.FloorToInt(screen.Min(point => point.y)), 0, target.height - 1);
                        var y1 = Mathf.Clamp(Mathf.CeilToInt(screen.Max(point => point.y)), 0, target.height - 1);
                        var count = 0;
                        for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++)
                        {
                            var index = y * target.width + x; var before = rendered[index]; var after = hidden[index];
                            if (Mathf.Abs(before.r - after.r) + Mathf.Abs(before.g - after.g) + Mathf.Abs(before.b - after.b) > 25) count++;
                        }
                        Check(count > minimum, "Native " + label + " is visible in its own rectangle: " + count + " changed pixels in " + name, report);
                    }
                    finally { graphic.enabled = true; Canvas.ForceUpdateCanvases(); Render(); }
                }
                VerifyLocalPixels(portrait, 100, "avatar portrait");
                VerifyLocalPixels(rim, 20, "circular portrait rim");
                VerifyLocalPixels(dot, 30, "red login notification dot");
            }
            finally { hud.enabled = true; Render(); UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static Transform Find(Transform parent, string name) => ExpeditionUi.Find(parent, name) ?? throw new InvalidOperationException("Missing actual C view: " + name);
        private static object Get(object owner, string name) => owner.GetType().GetField(name, I).GetValue(owner);
        private static string Label(object owner, string name) => ((TMP_Text)Get(owner, name)).text;
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, I).SetValue(owner, value);
        private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, I).Invoke(owner, args);
        private static void Static(Type type, string name, object value) => type.GetField(name, S).SetValue(null, value);
        private static void SaveStatics(Dictionary<FieldInfo, object> saved, Type type, params string[] names) { foreach (var name in names) { var field = type.GetField(name, S); saved.Add(field, field.GetValue(null)); } }
        private static Rect BoundsIn(RectTransform child, RectTransform parent)
        {
            var corners = new Vector3[4]; child.GetWorldCorners(corners); var local = corners.Select(parent.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(local.Min(point => point.x), local.Min(point => point.y), local.Max(point => point.x), local.Max(point => point.y));
        }
        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
        private static void Check(bool value, string message, Report report) { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
