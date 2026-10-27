using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character.UI;
using Project.Friends;
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
    /// <summary>Production BuildShelter/shortcut factory and badge widgets in a local edit-mode render; no mailbox transport.</summary>
    public static class ExpeditionShelterShortcutQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
        private const string Output = "Design/Release-0.3.15/media/validation/shelter";
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites;
            public int icons, preservedLegacyClicks, factoryCallbackClicks;
            public List<string> checks = new(), screenshots = new();
        }

        public static string Run()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Shelter QA requires Edit mode.");
            Directory.CreateDirectory(Output);
            var report = new Report(); var original = SceneManager.GetActiveScene(); var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = "MainMenu"; SceneManager.SetActiveScene(scene);
            var mailFields = new[] { "_scope", "_state", "_busy", "_nextRefresh", "Changed" }.Select(name => typeof(MailService).GetField(name, StaticHidden)).ToArray();
            var savedMail = mailFields.Select(field => field.GetValue(null)).ToArray();
            Camera camera = null; RenderTexture target = null;
            try
            {
                var fixture = JsonUtility.FromJson<MailService.State>(File.ReadAllText("Design/Release-0.3.15/media/validation/appearance-unlock-mail-fixture.json"));
                Check(MailService.Valid(fixture) && fixture.unread_count > 0, "Actual server unlock inbox fixture is required for the badge.");
                typeof(MailService).GetField("_scope", StaticHidden).SetValue(null, LocalAccountCacheScope.Current);
                typeof(MailService).GetField("_state", StaticHidden).SetValue(null, fixture);
                var canvas = new GameObject("ShelterQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 70;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var content = ExpeditionUi.Rect(canvas.transform, "ExpeditionSafeArea", 0, 0, 1, 1);
                var body = ExpeditionUi.Rect(content, "ExpeditionScenePageHost", 0, 0, 1, 1);
                var modals = ExpeditionUi.Rect(canvas.transform, "ShelterQaModalHost", 0, 0, 1, 1);
                var owner = new GameObject("DisabledShelterQaPresenter"); owner.SetActive(false);
                var presentation = owner.AddComponent<ExpeditionPresentation>(); presentation.enabled = false;
                Set(presentation, "_scene", scene); Set(presentation, "_canvas", canvas);
                Set(presentation, "_content", content); Set(presentation, "_sceneBody", body); Set(presentation, "_modalHost", modals);
                var legacyRoot = new GameObject("OfflineLegacyShortcutCallbacks", typeof(RectTransform));
                var rating = ExpeditionUi.Button(legacyRoot.transform, "RatingButton", "", 0, 0, 1, 1, () => report.preservedLegacyClicks++);
                var friends = ExpeditionUi.Button(legacyRoot.transform, "FriendsButton", "", 0, 0, 1, 1, () => report.preservedLegacyClicks++);
                var ratingBadge = ExpeditionUi.Rect(rating.transform, "PendingClaimBadge", 0, 0, 1, 1);
                ratingBadge.gameObject.AddComponent<CanvasGroup>().alpha = 1;
                ExpeditionUi.Text(ratingBadge, "Count", "2", 25, 0, 0, 1, 1);
                Call(presentation, "BuildShelter");
                var ratingShortcut = body.Find("ExpeditionOpenRating").GetComponent<Button>();
                var friendsShortcut = body.Find("ExpeditionOpenFriends").GetComponent<Button>();
                var mailShortcut = body.Find("ExpeditionOpenMail").GetComponent<Button>();
                ratingShortcut.onClick.Invoke(); friendsShortcut.onClick.Invoke();
                Check(report.preservedLegacyClicks == 2, "Repainted rating/friends shortcuts lost the existing callbacks.");
                foreach (var pair in new[] { (ratingShortcut, "shelter-rating"), (friendsShortcut, "shelter-friends"), (mailShortcut, "shelter-mail") })
                {
                    var picture = pair.Item1.transform.Find("ShortcutIcon").GetComponent<Image>();
                    Check(picture.sprite != null && picture.sprite == Resources.Load<Sprite>("Expedition/Icons/" + pair.Item2) && picture.color == Color.white && picture.preserveAspect, "Shelter shortcut does not use its generated art: " + pair.Item2);
                    Check(pair.Item1.GetComponent<Image>().raycastTarget && !picture.raycastTarget, "Shortcut artwork blocks its full touch target.");
                    report.icons++;
                }
                var state = new FriendsIncomingInviteState(() => "offline-shelter-badge");
                state.SynchronizeAccount(); Check(state.Publish(state.AccountScope, state.Generation, 3), "Incoming invite fixture was rejected.");
                var friendBadge = FriendsNavigationBadge.BindState(friendsShortcut, state);
                Check(friendBadge.IsVisible && friendBadge.PendingCount == 3, "Trusted friends badge disappeared after replacing shortcut art.");
                Check(ratingShortcut.transform.Find("ExpeditionPendingBadge").gameObject.activeSelf, "Existing rating reward badge disappeared.");
                var mailBadge = mailShortcut.GetComponent<ExpeditionMailBadge>();
                Check(mailBadge != null && mailShortcut.transform.Find("ExpeditionMailBadge").GetComponent<CanvasGroup>().alpha > 0, "Unread mail badge disappeared.");
                var callbackRoot = new GameObject("FactoryCallbackProbe", typeof(RectTransform)); callbackRoot.SetActive(false);
                foreach (var key in new[] { "shelter-rating", "shelter-friends", "shelter-mail" })
                    ExpeditionShelterShortcut.Create(callbackRoot.transform, "Probe-" + key, "", key, 0, 1, () => report.factoryCallbackClicks++).onClick.Invoke();
                Check(report.factoryCallbackClicks == 3, "Shortcut factory dropped a supplied navigation callback.");
                UnityEngine.Object.DestroyImmediate(callbackRoot);
                foreach (var binding in body.GetComponentsInChildren<CharacterAppearanceImage>(true)) binding.enabled = false;
                var hero = body.Find("ShelterHero").GetComponent<Image>(); hero.sprite = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"); hero.enabled = true;
                Canvas.ForceUpdateCanvases();
                var world = (ExpeditionSceneWorld)Get(presentation, "_world"); Call(world, "LateUpdate");
                // The actual world layout computes its grounding on an Overlay canvas.
                // Convert only after its production geometry settles for camera-target export.
                camera = new GameObject("ShelterQaCamera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 960;
                camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var native in root.GetComponentsInChildren<Canvas>(true))
                {
                    native.renderMode = RenderMode.ScreenSpaceCamera; native.worldCamera = camera; native.planeDistance = 1;
                    var scaler = native.GetComponent<CanvasScaler>(); if (scaler != null) scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    native.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                }
                // Neutral preview header does not invent account resources or a profile identity.
                var header = ExpeditionUi.Panel(content, "OfflineHeader", .014f, .929f, .986f, .995f);
                ExpeditionUi.Text(header.transform, "ShelterTitle", "УБЕЖИЩЕ · ESCAPE", 34, .03f, .08f, .97f, .92f, TextAlignmentOptions.Center).color = ExpeditionTheme.Amber;
                Save(scene, camera, target, "shelter-generated-shortcuts", report);
                ExpeditionMailPanel.Open(modals, fixture, _ => { });
                Save(scene, camera, target, "appearance-unlock-mail", report);
                report.checks.Add("Production BuildShelter creates3 exact generated icon shortcuts; legacy rating/friends callbacks and all3 factory callbacks survive.");
                report.checks.Add("Rating reward, trusted friends invite and actual unread mail badges remain visible; actual informational unlock inbox renders.");
                report.checks.Add("Offline fixture uses a neutral header and pinned shipped explorer; no profile/network/account actions.");
                report.passed = true;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
                for (var i = 0; i < mailFields.Length; i++) mailFields[i].SetValue(null, savedMail[i]);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                File.WriteAllText(Output + "/shelter-shortcuts-report.json", JsonUtility.ToJson(report, true));
            }
            return "PASS Shelter native capture:3 generated shortcut icons,3 live badges, preserved callbacks and informational unlock-mail render. No account/network actions.";
        }

        private static void Save(Scene scene, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var root in scene.GetRootGameObjects()) foreach (var text in root.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply(); var path = Output + "/" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name) => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
