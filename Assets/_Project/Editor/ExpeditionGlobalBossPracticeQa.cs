using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
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
    /// <summary>Offline production boss dossier/outcome binding; never clicks entry or invokes transport.</summary>
    public static class ExpeditionGlobalBossPracticeQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public int checks; }
        public static void RunBatch() => Debug.Log(Run());
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var report = new Report();
            var root = new GameObject("OfflineBossPracticeQa", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)root.transform).sizeDelta = new Vector2(1080, 1920);
            var inactive = new GameObject("InactivePracticeManager"); inactive.SetActive(false);
            try
            {
                var board = JsonUtility.FromJson<M3BoardSyncMsg>("{\"globalBoss\":true,\"globalBossPractice\":true,\"globalBossMyDamage\":5000}");
                var result = JsonUtility.FromJson<M3GameOverMsg>("{\"globalBoss\":true,\"globalBossPractice\":true,\"bossAttemptFinished\":true,\"bossGlobalHp\":45000,\"bossGlobalMaxHp\":50000,\"bossMyDamage\":5000}");
                Require(report, board.globalBossPractice && result.globalBossPractice, "Shipping DTOs preserve practice identity");
                foreach (var status in new[] { "active", "dead", "expired" })
                {
                    var fixture = new GlobalBossService.State { ok = true, event_id = "offline-event", status = status,
                        name = "Сердце Разлома", hp = status == "active" ? 123 : 0, hp_max = 550000,
                        attempts_used = 3, attempts_total = 3, attempts_left = 0, token_count = 0,
                        base_damage = 3500, base_armor = 1000, base_crit = .05f, max_turns = 30 };
                    var view = ExpeditionGlobalBossPanel.Open(root.transform, fixture);
                    Require(report, view.GetComponentsInChildren<Button>(true).Single(b => b.name == "PracticeRift").interactable,
                        "Practice available with no token/attempts and event " + status);
                    var health = view.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "HealthTrack");
                    var fill = view.GetComponentsInChildren<Image>(true).Single(i => i.name == "HealthFill").rectTransform;
                    Require(report, health.anchorMin.y >= .17f && health.anchorMax.y < .65f && fill.offsetMin == new Vector2(4,4) && fill.offsetMax == new Vector2(-4,-4),
                        "Shared HP has bottom space and internal fill padding: " + status);
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }
                var manager = inactive.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                var player = new GameObject("PracticePlayerPanel", typeof(RectTransform)); player.transform.SetParent(root.transform, false);
                var panel = player.AddComponent<Match3PlayerPanel>();
                Set(manager, "_myPanel", panel); Set(manager, "_isGlobalBoss", true); Set(manager, "_isGlobalBossPractice", true);
                Set(manager, "_bossGameOver", result);
                typeof(DuelMatch3Manager).GetMethod("ShowGlobalBossOutcome", Hidden).Invoke(manager, null);
                var texts = root.GetComponentsInChildren<TMP_Text>(true);
                Require(report, texts.Single(t => t.name == "Title").text == "ПРОБНЫЙ БОЙ ЗАВЕРШЁН", "Practice outcome has an explicit trial title");
                Require(report, texts.Single(t => t.name == "Damage").text.Contains("5"), "Outcome displays authoritative damage");
                Require(report, texts.Single(t => t.name == "Percent").text.Contains("ЭТО БЫЛО БЫ 10,00% полного HP босса"), "Hypothetical percent uses full original HP");
                Require(report, texts.Single(t => t.name == "RewardNotice").text.Contains("Урон не засчитан"), "Outcome states result has not been credited");
                Require(report, root.GetComponentsInChildren<Button>(true).Any(b => b.name == "RetryPractice"), "Practice can restart directly from the outcome");
                report.passed = true;
                Directory.CreateDirectory(".cache/qa/global-boss-practice");
                File.WriteAllText(".cache/qa/global-boss-practice/native-ui.json", JsonUtility.ToJson(report, true));
                return "PASS boss practice: " + report.checks + " native DTO/dossier/outcome checks; no transport/account calls.";
            }
            finally { UnityEngine.Object.DestroyImmediate(inactive); UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static void Require(Report report, bool value, string message) { if (!value) throw new InvalidOperationException(message); report.checks++; }

        [Serializable] private sealed class CaptureReport
        {
            public bool passed, networkActions, accountWrites, preferenceWrites;
            public string error, utc;
            public List<string> checks = new(), screenshots = new();
        }
        public static void RunCaptureBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Capture());
        }
        /// <summary>Capture-only native presentation of fixture-bound live dossier and both outcome branches.</summary>
        public static string Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            const string output = ".cache/qa/global-boss-practice";
            Directory.CreateDirectory(output);
            var originalScene = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var report = new CaptureReport { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                foreach (var height in new[] { 1920, 2400 })
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
                    Camera camera = null; RenderTexture target = null;
                    try
                    {
                        camera = new GameObject("OfflineBossCaptureCamera", typeof(Camera)).GetComponent<Camera>();
                        camera.orthographic = true; camera.orthographicSize = height * .5f; camera.transform.position = new Vector3(0, 0, -10);
                        camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor;
                        camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                        target = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                        var root = new GameObject("BossNativeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                        scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                        var backdrop = ExpeditionUi.Fill(root.transform, "BossArenaBackdrop", 0, 0, 1, 1, Color.white);
                        backdrop.sprite = Resources.Load<Sprite>("Expedition/Boss/0314/arena-rift");
                        var fit = backdrop.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                        fit.aspectRatio = backdrop.sprite.rect.width / backdrop.sprite.rect.height;
                        var fixture = new GlobalBossService.State { ok = true, event_id = "offline-capture-event", status = "active", name = "Сердце Разлома",
                            hp = 123450, hp_max = 550000, attempts_used = 3, attempts_total = 3, attempts_left = 0, token_count = 0,
                            base_damage = 3500, base_armor = 1000, base_crit = .05f, max_turns = 30,
                            my_damage = 5000, server_now = 1000, ends_at = 1000 + 86400 };
                        var view = ExpeditionGlobalBossPanel.Open(root.transform, fixture); view.enabled = false;
                        Canvas.ForceUpdateCanvases();
                        var health = view.GetComponentsInChildren<Image>(true).Single(image => image.name == "HealthFill");
                        var track = health.transform.parent as RectTransform;
                        var healthValue = track.Find("HealthValue").GetComponent<TMP_Text>();
                        AssertCapture(Mathf.Abs(health.fillAmount - 123450f / 550000) < .0001f && new string(healthValue.text.Where(character => !char.IsWhiteSpace(character)).ToArray()) == "123450/550000", "Dossier shows authoritative shared HP and correctly scaled fill at " + height, report);
                        AssertCapture(health.rectTransform.offsetMin == new Vector2(4, 4) && health.rectTransform.offsetMax == new Vector2(-4, -4) && track.anchorMin.y >= .17f, "Boss HP track has interior padding and space below it at " + height, report);
                        AssertCapture(view.GetComponentsInChildren<Button>(true).Single(button => button.name == "PracticeRift").interactable && !view.GetComponentsInChildren<Button>(true).Single(button => button.name == "EnterRift").interactable, "Practice remains available after all attempts and tokens are exhausted at " + height, report);
                        CaptureFrame(canvas, camera, target, output + "/dossier-hp-practice-1080x" + height + ".png", report);
                        UnityEngine.Object.DestroyImmediate(view.gameObject);
                        var inactive = new GameObject("DisabledNativeOutcomeManager"); inactive.SetActive(false);
                        var manager = inactive.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                        var player = new GameObject("OutcomeCanvasAnchor", typeof(RectTransform)); player.transform.SetParent(root.transform, false);
                        Set(manager, "_myPanel", player.AddComponent<Match3PlayerPanel>()); Set(manager, "_isGlobalBoss", true);
                        foreach (var practice in new[] { true, false })
                        {
                            Set(manager, "_isGlobalBossPractice", practice);
                            Set(manager, "_bossGameOver", new M3GameOverMsg { globalBoss = true, globalBossPractice = practice, bossAttemptFinished = true,
                                bossGlobalHp = 45000, bossGlobalMaxHp = 50000, bossMyDamage = 5000, bossQualified = !practice });
                            typeof(DuelMatch3Manager).GetMethod("ShowGlobalBossOutcome", Hidden).Invoke(manager, null);
                            var overlay = root.transform.Find("GlobalBossOutcomeModal");
                            foreach (var safe in overlay.GetComponentsInChildren<ExpeditionSafeArea>(true)) { safe.enabled = false; ExpeditionUi.SetRect(safe.transform as RectTransform, 0, 0, 1, 1); }
                            Canvas.ForceUpdateCanvases();
                            var texts = overlay.GetComponentsInChildren<TMP_Text>();
                            AssertCapture(texts.Single(text => text.name == "Percent").text.Contains("10,00%") && texts.Single(text => text.name == "Title").text == (practice ? "ПРОБНЫЙ БОЙ ЗАВЕРШЁН" : "ВКЛАД СОХРАНЁН"), "Outcome preserves authoritative full-HP percent and correct branch title at " + height + "/" + practice, report);
                            AssertCapture(texts.Single(text => text.name == "RewardNotice").text.Contains(practice ? "Урон не засчитан" : "Вы участвуете в награде"), "Outcome clearly explains credited versus hypothetical damage at " + height + "/" + practice, report);
                            CaptureFrame(canvas, camera, target, output + "/outcome-" + (practice ? "practice" : "credited") + "-1080x" + height + ".png", report);
                            UnityEngine.Object.DestroyImmediate(overlay.gameObject);
                        }
                    }
                    finally
                    {
                        if (camera != null) camera.targetTexture = null;
                        RenderTexture.active = previousTexture;
                        if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                RenderTexture.active = previousTexture;
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                File.WriteAllText(output + "/native-capture.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " boss presentation checks and " + report.screenshots.Count + " native captures passed; no network, account or preference actions.";
        }
        private static void CaptureFrame(Canvas canvas, Camera camera, RenderTexture target, string path, CaptureReport report)
        {
            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate(); AssertCapture(!text.isTextOverflowing, "Boss presentation text fits: " + text.name + "/" + target.height, report);
            }
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                var colors = pixels.GetPixels32().Where((_, index) => index % 997 == 0).Select(color => (color.r << 16) | (color.g << 8) | color.b).Distinct().Count();
                AssertCapture(colors > 32, "Native boss capture contains rendered UI: " + Path.GetFileName(path), report);
                File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static void AssertCapture(bool value, string message, CaptureReport report) { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
