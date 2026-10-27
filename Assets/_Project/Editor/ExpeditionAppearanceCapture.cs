using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
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
    /// <summary>Renders the shipped Hero prefab/layout in an isolated edit-mode mobile fixture. No authentication, RPC, Apply or preference writes.</summary>
    public static class ExpeditionAppearanceCapture
    {
        private const int Width = 1080, Height = 1920;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public string result, gender, note;
            public int width = Width, height = Height;
            public string[] screenshots;
        }
        [Serializable] private sealed class ServerFixtures { public CharacterAppearanceService.State male, female, male_selected, female_selected; }

        private sealed class CacheSnapshot
        {
            private readonly List<FieldInfo> _fields = new List<FieldInfo>();
            private readonly List<object> _values = new List<object>();
            public void Remember(Type owner, params string[] fields)
            {
                foreach (var name in fields)
                {
                    var field = owner.GetField(name, StaticHidden);
                    Require(field != null, "Missing QA cache field: " + owner.Name + "." + name);
                    _fields.Add(field); _values.Add(field.GetValue(null));
                }
            }
            public void Restore() { for (var i = 0; i < _fields.Count; i++) _fields[i].SetValue(null, _values[i]); }
        }

        // Invoke with Unity -batchmode -screen-width 1080 -screen-height 1920
        // -executeMethod Project.Editor.ExpeditionAppearanceCapture.CapturePreviews -quit.
        // Keep graphics enabled; -nographics cannot produce UI screenshots.
        [MenuItem("Tools/Expedition/Capture Hero Mobile Previews")]
        public static void CapturePreviews()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Capture in Edit mode; this fixture never authenticates.");
            var output = OutputDirectory();
            Directory.CreateDirectory(output);
            foreach (var gender in new[] { "male", "female" })
            {
                var folder = Path.Combine(output, gender);
                if (Directory.Exists(folder)) foreach (var obsolete in Directory.GetFiles(folder, "hero-avatar-*.png")) File.Delete(obsolete);
            }
            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderTexture target = null;
            Camera captureCamera = null;
            var previousTarget = RenderTexture.active;
            var files = new List<string>();
            var beforeBody = CharacterAppearance.BodyId;
            var beforeAvatar = CharacterAppearance.AvatarId;
            var saved = new CacheSnapshot();
            saved.Remember(typeof(AccountGender), "_scope", "_loaded", "_female", "_canChoose", "_unsupported", "Changed");
            saved.Remember(typeof(CharacterAppearance), "_scope", "_loaded", "_female", "_genderLoaded", "_current", "Changed");
            saved.Remember(typeof(CharacterAppearanceService), "_scope", "_loaded", "_saving", "_state", "_lastError", "Changed");
            // Isolate the edit-mode fixture from any scene-owned subscribers. Nothing
            // is added to the production API and every cache/event is restored below.
            Set(typeof(AccountGender), "Changed", null);
            Set(typeof(CharacterAppearance), "Changed", null);
            Set(typeof(CharacterAppearanceService), "Changed", null);
            try
            {
                var camera = new GameObject("AppearanceQaCamera", typeof(Camera)).GetComponent<Camera>();
                captureCamera = camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel;
                camera.orthographic = true; camera.orthographicSize = Height * .5f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("AppearanceQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvas.GetComponent<CanvasScaler>();
                // The render target is already the reference phone resolution.
                // Keep the fixture independent of the current Editor Game-view size.
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
                scaler.referenceResolution = new Vector2(Width, Height); canvas.scaleFactor = 1;
                var backdrop = ExpeditionUi.Fill(canvas.transform, "QaWorld", 0, 0, 1, 1, Color.white);
                backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/character-backdrop");
                if (backdrop.sprite == null) backdrop.color = ExpeditionTheme.Panel;
                var header = ExpeditionUi.Panel(canvas.transform, "QaHeader", .014f, .929f, .986f, .995f);
                var title = ExpeditionUi.Text(header.transform, "QaTitle", "ГЕРОЙ", 37, .04f, .40f, .96f, .92f, TextAlignmentOptions.Center);
                title.color = ExpeditionTheme.Amber;
                ExpeditionUi.Text(header.transform, "QaNotice", "Локальный предпросмотр интерфейса", 21, .04f, .06f, .96f, .42f, TextAlignmentOptions.Center);
                var host = ExpeditionUi.Rect(canvas.transform, "QaHeroPage", 0, .105f, 1, .922f);
                var labels = new[] { "Убежище", "Шахта", "Герой", "Арена", "Мастерская" };
                for (var i = 0; i < labels.Length; i++)
                    ExpeditionUi.Button(canvas.transform, "QaNav" + i, labels[i], .015f + i * .197f, .018f, .199f + i * .197f, .087f, null, i == 2, 23);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CharacterUI/CharacterHudOverlay.prefab");
                Require(prefab != null, "Hero HUD prefab is missing.");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                // Runtime layout reparents the screen, slots and labels. Edit mode
                // forbids changing a connected prefab's hierarchy, so unpack only
                // this disposable clone before calling those production methods.
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var view = instance.GetComponentInChildren<CharacterScreenView>(true);
                Require(view != null, "Hero HUD has no CharacterScreenView.");
                // These production methods only construct/bind presentation. The controller's
                // Play-mode lifecycle never runs in this edit-mode fixture.
                view.EnsureBuilt();
                var build = typeof(CharacterScreenView).GetMethod("ApplyExpeditionLayout", Hidden);
                Require(build != null, "Character layout entry point is missing.");
                var layout = (CharacterExpeditionLayout)build.Invoke(view, new object[] { null, null, null, null });
                view.AttachToShell(host);
                Require(view.transform.parent == host, "Hero fixture did not attach to its render canvas.");
                var group = view.GetComponent<CanvasGroup>();
                if (group != null) { group.alpha = 1; group.interactable = false; group.blocksRaycasts = false; }
                foreach (var image in instance.GetComponentsInChildren<Canvas>(true))
                    if (!image.transform.IsChildOf(view.transform)) image.enabled = false;
                var screenCanvas = view.GetComponent<Canvas>();
                if (screenCanvas != null) { screenCanvas.enabled = true; screenCanvas.overrideSorting = false; }
                view.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                Render(camera, target);
                Canvas.ForceUpdateCanvases();
                var geometry = typeof(CharacterExpeditionLayout).GetMethod("ApplyGeometry", Hidden);
                Require(geometry != null, "Character geometry entry point is missing.");
                geometry.Invoke(layout, null);
                foreach (var fieldName in new[] { "hpText", "damageText", "armorText", "healText", "critText" })
                    if (typeof(CharacterScreenView).GetField(fieldName, Hidden)?.GetValue(view) is TMP_Text value) value.text = "—";
                var panel = view.GetComponent<CharacterAppearancePanel>();
                Require(panel != null, "Appearance panel was not built.");
                SetGenderFixture(false, false); Set(typeof(AccountGender), "_loaded", false);
                CharacterAppearance.RefreshAccount();
                layout.SelectHeroTab(1); panel.SelectTheme(0);
                foreach (var image in panel.GetComponentsInChildren<Image>(true))
                    if (image.name == "AppearanceBodyPreview" || image.name == "AppearanceAvatarPreview" || image.name == "PairedPortrait")
                        Require(!image.enabled, "Unknown account gender exposes appearance artwork: " + image.name);
                for (var gender = 0; gender < 2; gender++)
                {
                    var female = gender == 1;
                    SetGenderFixture(female, false);
                    CharacterAppearance.RefreshAccount();
                    SetServerFixture(female);
                    var folder = Path.Combine(output, female ? "female" : "male");
                    Directory.CreateDirectory(folder);
                    layout.SelectHeroTab(0);
                    foreach (var binding in view.GetComponentsInChildren<CharacterAppearanceImage>(true))
                        typeof(CharacterAppearanceImage).GetMethod("Refresh", Hidden)?.Invoke(binding, null);
                    var hero = ExpeditionUi.Find(view.transform, "ExpeditionHero")?.GetComponent<Image>();
                    if (hero != null) hero.sprite = CharacterAppearance.Body(Resources.Load<Sprite>("Expedition/Art/hero"));
                    var genderFiles = new List<string>();
                    Save(camera, target, folder, "hero-equipment", genderFiles);
                    layout.SelectHeroTab(1);
                    for (var i = 0; i < CharacterAppearance.Themes.Count; i++)
                    {
                        panel.SelectTheme(i);
                        Require(panel.PreviewId == CharacterAppearance.Id(i, female), "Preview ignores the server gender fixture.");
                        Require(CharacterAppearance.AvatarId == CharacterAppearance.BodyId, "Saved avatar differs from skin.");
                        var paired = ExpeditionUi.Find(view.transform, "AppearanceAvatarPreview")?.GetComponent<Image>();
                        Require(paired != null && paired.sprite == CharacterAppearance.Load(panel.PreviewId, true), "Preview avatar is not paired with its skin.");
                        Save(camera, target, folder, "hero-skin-" + CharacterAppearance.Themes[i].Id, genderFiles);
                    }
                    panel.SelectTheme(5);
                    var apply = ExpeditionUi.Find(view.transform, "ApplyAppearance")?.GetComponent<Button>();
                    Require(apply != null && !apply.interactable && apply.GetComponentInChildren<TMP_Text>().text == "ОБРАЗ ЗАКРЫТ", "Locked skin can be applied.");
                    Save(camera, target, folder, "hero-locked-phoenix", genderFiles);
                    SetServerFixture(female, true); layout.SelectHeroTab(1); panel.SelectTheme(1);
                    Require(CharacterAppearance.BodyId == CharacterAppearance.Id(1, female) && CharacterAppearance.AvatarId == CharacterAppearance.BodyId,
                        "Server-selected unlocked skin did not select its paired avatar.");
                    Require(apply.GetComponentInChildren<TMP_Text>().text == "ВЫБРАНО", "Selected unlocked skin is not marked selected.");
                    Save(camera, target, folder, "hero-unlocked-selected-voidranger", genderFiles);
                    files.AddRange(genderFiles);
                    File.WriteAllText(Path.Combine(folder, "capture-report.json"), JsonUtility.ToJson(new Report
                    {
                        result = "PASS", gender = female ? "female" : "male", screenshots = genderFiles.ToArray(),
                        note = "Actual server security-test progress fixtures; ten skins with server conditions, paired avatars and locked previews; no avatar or gender selector. No auth RPC or preferences written."
                    }, true));
                }
                CaptureRegistration(canvas.transform, camera, target, output, files);
                var report = new Report
                {
                    result = "PASS", gender = "male + female", screenshots = files.ToArray(),
                    note = "26 Hero screenshots (equipment + ten skins + locked and selected-unlocked states for both genders) and 2 registration states. Actual server response fixtures, paired avatars checked; caches restored. No RPC or preference writes."
                };
                File.WriteAllText(Path.Combine(output, "capture-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("Hero mobile capture PASS: " + files.Count + " screenshots at " + output);
            }
            finally
            {
                try
                {
                    RenderTexture.active = previousTarget;
                    if (captureCamera != null) captureCamera.targetTexture = null;
                    if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                    if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
                finally
                {
                    saved.Restore();
                    Require(CharacterAppearance.BodyId == beforeBody && CharacterAppearance.AvatarId == beforeAvatar, "Capture changed the saved appearance.");
                }
            }
        }

        private static void Save(Camera camera, RenderTexture target, string directory, string name, List<string> files)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (text.gameObject.scene == camera.gameObject.scene)
                {
                    text.ForceMeshUpdate();
                    var parent = text.transform.parent;
                    var critical = text.name == "AppearanceTitle" || text.name == "AppearanceDescription" || text.name == "AppearanceStatus" || text.name == "AppearanceRequirements" || text.name == "AppearanceCardProgress" ||
                        parent != null && (parent.name.StartsWith("AppearanceTheme", StringComparison.Ordinal) || parent.name.StartsWith("ExpeditionHeroTab", StringComparison.Ordinal));
                    if (critical && text.isTextOverflowing)
                    {
                        var canvas = text.canvas;
                        throw new InvalidOperationException("Mobile text overflows: " + PathOf(text.transform) + " / " + text.text +
                            "; rect=" + text.rectTransform.rect + "; parent=" + (parent as RectTransform)?.rect +
                            "; font=" + text.fontSize + " (auto=" + text.enableAutoSizing + ", min=" + text.fontSizeMin + ", max=" + text.fontSizeMax + ")" +
                            "; canvas=" + (canvas != null ? ((RectTransform)canvas.transform).rect.ToString() : "null") +
                            "; rootCanvas=" + (canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.ToString() : "null") +
                            "; cameraPixels=" + camera.pixelRect + "; target=" + target.width + "x" + target.height);
                    }
                }
            Canvas.ForceUpdateCanvases();
            Render(camera, target);
            RenderTexture.active = target;
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                var path = Path.Combine(directory, name + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG()); files.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static void Render(Camera camera, RenderTexture target)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            else camera.Render();
        }

        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        private static void SetGenderFixture(bool female, bool pending)
        {
            Set(typeof(AccountGender), "_scope", LocalAccountCacheScope.Current);
            Set(typeof(AccountGender), "_loaded", true);
            Set(typeof(AccountGender), "_female", female);
            Set(typeof(AccountGender), "_canChoose", pending);
            Set(typeof(AccountGender), "_unsupported", false);
        }

        private static void SetServerFixture(bool female, bool selected = false)
        {
            var file = Path.GetFullPath("Assets/_Project/Editor/Fixtures/appearance-server-legacy.json");
            Require(File.Exists(file), "Run cosmetics_security_test.lua and export its actual server response fixtures before capture.");
            var fixtures = JsonUtility.FromJson<ServerFixtures>(File.ReadAllText(file));
            var state = selected ? (female ? fixtures?.female_selected : fixtures?.male_selected) : (female ? fixtures?.female : fixtures?.male);
            Require(state != null && state.ok && state.themes?.Length == CharacterAppearance.Themes.Count, "Invalid cosmetic server response fixture.");
            Set(typeof(CharacterAppearanceService), "_scope", LocalAccountCacheScope.Current);
            Set(typeof(CharacterAppearanceService), "_loaded", false);
            Set(typeof(CharacterAppearanceService), "_saving", false);
            var accept = typeof(CharacterAppearanceService).GetMethod("Accept", StaticHidden);
            Require(accept != null && (bool)accept.Invoke(null, new object[] { state }), "Client rejected actual cosmetic-server response fixture.");
            Set(typeof(CharacterAppearanceService), "_lastError", null);
        }

        private static void Set(Type owner, string name, object value)
        {
            var field = owner.GetField(name, StaticHidden);
            Require(field != null, "Missing QA cache field: " + owner.Name + "." + name);
            field.SetValue(null, value);
        }

        private static void CaptureRegistration(Transform canvas, Camera camera, RenderTexture target, string directory, List<string> files)
        {
            SetGenderFixture(false, true);
            var root = ExpeditionUi.Rect(canvas, "RegistrationQaFixture", 0, 0, 1, 1);
            ExpeditionUi.Fill(root, "Dim", 0, 0, 1, 1, new Color32(5, 19, 24, 255));
            var panel = ExpeditionUi.Panel(root, "Panel", .055f, .12f, .945f, .88f).rectTransform;
            var newText = typeof(AuthSetupGate).GetMethod("NewText", StaticHidden);
            var createInput = typeof(AuthSetupGate).GetMethod("CreateInput", StaticHidden);
            Require(newText != null && createInput != null, "Registration presentation helpers are missing.");
            newText.Invoke(null, new object[] { panel, "Title", "Сохранение прогресса", 40, FontStyle.Bold });
            var hint = (Text)newText.Invoke(null, new object[] { panel, "Hint", "Укажите e-mail и пароль.\n• Новый аккаунт — привяжем к этому устройству.\n• Уже играли на другом телефоне — войдёте и восстановите прогресс.\nПол героя выбирается один раз.", 30, FontStyle.Normal });
            var email = (InputField)createInput.Invoke(null, new object[] { panel, "Email", "E-mail", false });
            var pass = (InputField)createInput.Invoke(null, new object[] { panel, "Pass", "Пароль (мин. 8)", true });
            var status = (Text)newText.Invoke(null, new object[] { panel, "Status", "", 15, FontStyle.Normal });
            ExpeditionUi.Button(panel, "CreateAccount", "Создать / привязать", .055f, .17f, .945f, .265f, null, true, 34);
            ExpeditionUi.Button(panel, "RestoreAccount", "У меня уже есть аккаунт", .055f, .045f, .945f, .14f, null, false, 34);
            ExpeditionSystemDialogs.ApplyAuth(root.gameObject, panel, email, pass, status);
            var picker = AccountGenderPicker.Create(panel, .23f, .335f, .77f, .425f);
            ExpeditionUi.SetRect(status.rectTransform, .055f, .275f, .945f, .33f);
            Canvas.ForceUpdateCanvases();
            Require(hint.preferredHeight <= hint.rectTransform.rect.height + 2, "Registration hint overflows on mobile.");
            Require(!picker.SelectedFemale, "New male registration picker state is incorrect.");
            ValidateRegistrationSymbols(picker);
            var folder = Path.Combine(directory, "registration"); Directory.CreateDirectory(folder);
            Save(camera, target, folder, "registration-male-selected", files);
            var femaleButton = picker.transform.Find("ChooseFemale")?.GetComponent<Button>();
            Require(femaleButton != null && femaleButton.interactable, "Female icon is not selectable for a pending registration.");
            femaleButton.onClick.Invoke();
            Require(picker.SelectedFemale, "Female registration icon did not select.");
            ValidateRegistrationSymbols(picker);
            Save(camera, target, folder, "registration-female-selected", files);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }

        private static void ValidateRegistrationSymbols(AccountGenderPicker picker)
        {
            Canvas.ForceUpdateCanvases();
            var symbols = picker.GetComponentsInChildren<AccountGenderSymbol>(true);
            Require(symbols.Length == 2, "Registration requires two native symbols.");
            foreach (var symbol in symbols)
            {
                var rect = symbol.rectTransform.rect;
                Require(symbol.isActiveAndEnabled && symbol.canvas != null && symbol.canvasRenderer != null && rect.width >= 32 && rect.height >= 32,
                    "Registration symbol has no active canvas/renderer or usable rect: " + PathOf(symbol.transform) + "; " + rect);
                symbol.SetAllDirty(); symbol.Rebuild(CanvasUpdate.PreRender);
                var mesh = symbol.canvasRenderer.GetMesh();
                Require(mesh != null && mesh.vertexCount >= 160, "Registration symbol mesh is empty: " + PathOf(symbol.transform) +
                    "; vertices=" + (mesh != null ? mesh.vertexCount : 0));
            }
        }

        private static string OutputDirectory()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var result = Path.Combine(root, ".cache/qa/appearance-legacy");
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "-appearanceQaOutput") result = Path.GetFullPath(args[i + 1]);
            Require(result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Capture output must stay inside the project.");
            return result;
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
