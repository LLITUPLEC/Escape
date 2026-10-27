using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
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
    /// <summary>Native login card renders without accounts, reward claims or network components.</summary>
    public static class ExpeditionDailyLoginQa0313
    {
        private const string Output = ".cache/qa/legacy-0.3.13/login-ui";
        [Serializable] private sealed class Shot { public string path, state; public int width, height, checkedDays; }
        [Serializable] private sealed class Report { public bool passed, networkOrAccountActions, questLayoutChanged; public int assertions; public string checkedUtc, error; public List<Shot> screenshots = new(); }
        public static string Run(string target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            if (!string.IsNullOrEmpty(target) && target != "capture") throw new ArgumentException("Use capture.");
            Directory.CreateDirectory(Output); var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            var before = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            try
            {
                foreach (var height in new[] { 1920, 2400 }) Capture(height, report);
                var after = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Check(report, before.Length == after.Length && before.All(after.Contains), "Nakama bootstrap changed."); report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); throw; }
            finally { File.WriteAllText(Output + "/login0313-report.json", JsonUtility.ToJson(report, true)); }
            return "PASS: " + report.assertions + " intact login frame/claim-state checks, " + report.screenshots.Count + " native renders, no reward/account actions.";
        }
        private static void Check(Report report, bool result, string message) { report.assertions++; if (!result) throw new InvalidOperationException(message); }
        private static void Capture(int height, Report report)
        {
            var previous = SceneManager.GetActiveScene(); var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture target = null;
            try
            {
                camera = new GameObject("Login0312Camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = height * .5f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("Login0312Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var background = ExpeditionUi.Fill(canvas.transform, "Background", 0, 0, 1, 1, Color.white); background.sprite = Resources.Load<Sprite>("Expedition/Art/hub-backdrop");
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(MineRewardFormat.MainItemCatalogAssetPath);
                Check(report, catalog != null, "Shipping item catalog missing.");
                var fixture = typeof(ExpeditionDailyQa).GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Static);
                Check(report, fixture != null, "Existing canonical daily fixture missing.");
                foreach (var sample in new[] { "day7-ready", "day7-claimed" })
                {
                    var state = (DailyRewardsService.State)fixture.Invoke(null, new object[] { false });
                    state.login.rewards[6].items = state.login.rewards[6].items.Concat(new[] { new DailyRewardsService.Item { def_id = "global_boss_teleport", amount = 1 } }).ToArray();
                    Check(report, state.login.rewards[6].items.Length == 4 && state.login.rewards[6].items.Single(i => i.def_id == "global_boss_teleport").amount == 1, "Day7 must preserve three existing prizes and add one teleport.");
                    state.login.total_claimed = sample == "day1-ready" ? 0 : sample == "day3-ready" ? 2 : sample == "day3-claimed" ? 3 : sample == "day7-ready" ? 6 : 7;
                    state.login.claimed_today = sample.EndsWith("claimed", StringComparison.Ordinal); state.login.can_claim = !state.login.claimed_today;
                    state.login.next_day = state.login.total_claimed % 7 + 1;
                    var view = ExpeditionDailyPanel.Open(canvas.transform, false, state, catalog);
                    foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                    Canvas.ForceUpdateCanvases();
                    var panel = view.transform.Find("DailyPanel");
                    var frames = panel.GetComponentsInChildren<ExpeditionDailyLoginFrame>(true).Where(f => !f.Checkmark).ToArray();
                    var marks = panel.GetComponentsInChildren<ExpeditionDailyLoginFrame>(false).Where(f => f.Checkmark).ToArray();
                    var expectedDays = state.login.total_claimed % 7; if (state.login.claimed_today && expectedDays == 0) expectedDays = 7;
                    Check(report, frames.Length == 8, "Expected seven intact day frames and one current reward frame.");
                    Check(report, marks.Length == expectedDays + (state.login.claimed_today ? 1 : 0), "Claimed green checkmarks disagree with server state.");
                    Check(report, panel.GetComponentsInChildren<Image>(true).All(i => i.name != "CardWell"), "Old rectangular card well still cuts the login bevel.");
                    foreach (var frame in frames)
                    {
                        Check(report, !frame.raycastTarget, "Decorative frame blocks touch.");
                        VerifyMesh(frame, report);
                    }
                    foreach (var mark in marks)
                    {
                        Check(report, !mark.raycastTarget && mark.Claimed, "Claim marker interaction/state is wrong.");
                        Check(report, Mathf.Abs(mark.rectTransform.rect.width - mark.rectTransform.rect.height) < .5f, "Green claim check stretched.");
                        Check(report, mark.rectTransform.rect.width < 100, "Claim check expanded outside its reserved slot.");
                        VerifyMesh(mark, report);
                    }
                    var claim = panel.Find("ClaimLogin").GetComponent<Button>();
                    Check(report, claim.interactable == state.login.can_claim, "Claim button semantics changed.");
                    foreach (var icon in panel.GetComponentsInChildren<Image>().Where(i => i.name == "DailyRewardIcon")) Check(report, icon.sprite != null, "Reward icon missing.");
                    foreach (var text in panel.GetComponentsInChildren<TMP_Text>()) { text.ForceMeshUpdate(); Check(report, !text.isTextOverflowing, "Daily text overflows: " + text.name); }
                    if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render();
                    RenderTexture.active = target; var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
                    try { pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0); pixels.Apply(); var path = Output + "/" + sample + "-1080x" + height + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(new Shot { path = path, state = sample, width = 1080, height = height, checkedDays = expectedDays }); }
                    finally { UnityEngine.Object.DestroyImmediate(pixels); }
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
                }
            }
            finally
            {
                RenderTexture.active = previousTarget; if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static void VerifyMesh(ExpeditionDailyLoginFrame graphic, Report report)
        {
            using var vertices = new VertexHelper();
            typeof(ExpeditionDailyLoginFrame).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { vertices });
            Check(report, vertices.currentVertCount > 60 && vertices.currentIndexCount > 60, "Frame/check geometry missing.");
            var bounds = graphic.GetPixelAdjustedRect(); var vertex = UIVertex.simpleVert;
            for (var i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                Check(report, !float.IsNaN(vertex.position.x) && !float.IsNaN(vertex.position.y) && vertex.position.x >= bounds.xMin - .05f && vertex.position.x <= bounds.xMax + .05f && vertex.position.y >= bounds.yMin - .05f && vertex.position.y <= bounds.yMax + .05f, "Frame geometry clipped outside its card.");
            }
        }
    }
}
