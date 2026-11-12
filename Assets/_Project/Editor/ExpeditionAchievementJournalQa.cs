using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Achievements;
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
    /// <summary>Exercises the shipped achievement prefab with temporary, offline progress.</summary>
    public static class ExpeditionAchievementJournalQa
    {
        private const string Output = ".cache/qa/achievement-journal";
        private const string Prefab = "Assets/_Project/Prefabs/UI/Achievements/AchievementsPanel.prefab";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable] private sealed class Report
        {
            public bool passed;
            public string error;
            public List<string> checks = new();
            public List<string> screenshots = new();
        }

        public static void RunBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || NakamaBootstrap.Instance != null)
                throw new InvalidOperationException("Journal QA requires an offline Edit Mode session.");
            Directory.CreateDirectory(Output);
            var report = new Report();
            var original = EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var fields = new Dictionary<FieldInfo, object>();
            try
            {
                var catalog = AchievementCatalog.Chains;
                var counters = new Dictionary<string, int>(StringComparer.Ordinal);
                var claims = new HashSet<string>(StringComparer.Ordinal);
                Check(ReferenceEquals(catalog, AchievementCatalog.Chains), "Fallback catalog identity remains stable across reads", report);
                VerifyCatalogAndIcons(catalog, report);
                Replace(fields, typeof(AchievementProgressStorage), "_loadedScope", LocalAccountCacheScope.Current);
                Replace(fields, typeof(AchievementProgressStorage), "_stats", counters);
                Replace(fields, typeof(AchievementProgressStorage), "_claimedStepTokens", claims);
                Profile(1080, 1920, catalog, counters, claims, report);
                Profile(1080, 2400, catalog, counters, claims, report);
                Check(NakamaBootstrap.Instance == null, "Journal QA did not create a network bootstrap", report);
                report.passed = true;
                Debug.Log($"PASS achievement Journal QA: {report.checks.Count} checks and {report.screenshots.Count} portrait renders.");
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                throw;
            }
            finally
            {
                foreach (var pair in fields) pair.Key.SetValue(null, pair.Value);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(report, true));
            }
        }

        private static void VerifyCatalogAndIcons(AchievementChainDefinition[] catalog, Report report)
        {
            var source = File.ReadAllText("Server/nakama/data/achievement_catalog.example.json");
            var parse = typeof(AchievementCatalogService).GetMethod("ParseCatalogChains", Static);
            var parsed = (AchievementChainDefinition[])parse.Invoke(null,
                new object[] { source.Insert(source.IndexOf('{') + 1, "\"ok\":true,"), null });
            var releaseChains = parsed.Where(entry => entry.Tab == AchievementTab.Resources
                || entry.ChainId is "dnn.flower_bets" or "dnn.petard_50" or "sl.blocked_damage" or "sl.critical_hits"
                or "obs.cleanse_fistula" or "obs.wipe_soot" or "obs.sever_vein").ToArray();
            Check(releaseChains.Length == 17 && releaseChains.Count(entry => entry.Tab == AchievementTab.Resources) == 10,
                "Canonical server catalog has 17 new medal chains, including ten Resources chains", report);

            var icons = AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(AchievementIconCatalog.MainCatalogAssetPath);
            var sprites = new HashSet<Sprite>();
            foreach (var entry in releaseChains)
            {
                var fallback = catalog.Single(chain => chain.ChainId == entry.ChainId);
                Check(fallback.Tab == entry.Tab && fallback.TitleRu == entry.TitleRu && fallback.StatKey == entry.StatKey
                    && fallback.UsesTotalThresholds && entry.UsesTotalThresholds
                    && fallback.Thresholds.SequenceEqual(entry.Thresholds)
                    && fallback.Descriptions.SequenceEqual(entry.Descriptions)
                    && fallback.RewardTexts.SequenceEqual(entry.RewardTexts),
                    "Offline catalog matches server chain " + entry.ChainId, report);
                Check(Enumerable.Range(0, entry.Thresholds.Length).All(step =>
                        AchievementUiRules.CumulativeRequired(entry, step) == entry.Thresholds[step]
                        && AchievementUiRules.StepProgress(entry, step, entry.Thresholds[step] - 1) == entry.Thresholds[step] - 1),
                    "All medal tiers show and claim at their exact cumulative total: " + entry.ChainId, report);
                var definition = icons.Get(entry.ChainId);
                Check(definition != null && definition.Icon != null && sprites.Add(definition.Icon),
                    "Unique imported sprite for " + entry.ChainId, report);
            }
        }

        private static void Profile(int width, int height, AchievementChainDefinition[] catalog,
            Dictionary<string, int> counters, HashSet<string> claims, Report report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var priorTexture = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            try
            {
                counters.Clear();
                claims.Clear();
                var chain = catalog.Single(entry => entry.ChainId == "obs.cross");
                Check(chain.Thresholds.Length >= 4, "Fixture chain has past, current and future steps", report);
                var current = 2;
                counters[chain.StatKey] = AchievementUiRules.CumulativeRequired(chain, current) - 1;
                claims.Add(chain.ChainId + ":0");
                claims.Add(chain.ChainId + ":1");

                var camera = new GameObject("JournalQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = height * .5f;
                camera.targetTexture = target;
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel;
                var canvas = new GameObject("JournalQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                // Match the shipped 1080×1920 CanvasScaler's half-width/half-height setting on the offscreen target.
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Mathf.Sqrt(width / 1080f * (height / 1920f));
                canvas.scaleFactor = scaler.scaleFactor;
                var backdrop = ExpeditionUi.Fill(canvas.transform, "Backdrop", 0, 0, 1, 1, Color.white);
                backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/workshop-backdrop");
                var pageHost = ExpeditionUi.Rect(canvas.transform, "ExpeditionAchievementsPageHost", 0, .105f, 1, .922f);
                var modalHost = ExpeditionUi.Rect(canvas.transform, "ExpeditionModalHost", 0, 0, 1, 1);
                var events = new GameObject("JournalQaEvents", typeof(EventSystem)).GetComponent<EventSystem>();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var controller = copy.GetComponent<AchievementsPanelController>();
                Call(controller, "Awake");
                controller.AttachToShell(pageHost, modalHost);
                modalHost.SetAsLastSibling();
                var canvasGroup = copy.GetComponent<CanvasGroup>();
                canvasGroup.alpha = 1;
                canvasGroup.interactable = canvasGroup.blocksRaycasts = true;
                typeof(AchievementsPanelController).GetField("_visible", Instance).SetValue(controller, true);
                var sheet = copy.transform.Find("AchievementsSheet");
                (sheet as RectTransform).anchoredPosition = Vector2.zero;
                var scroll = sheet.Find("JournalScroll").GetComponent<ScrollRect>();
                var tabs = sheet.Find("JournalTabs");
                var sections = new[]
                {
                    (AchievementTab.Obsession, "obsession", "Одержимость"),
                    (AchievementTab.Slaughter, "slaughter", "Бойня"),
                    (AchievementTab.Dnn, "dnn", "ДНН"),
                    (AchievementTab.Resources, "resources", "Ресурсы")
                };
                Check(AchievementSections.All.Select(section => (section.Tab, section.Id, section.TitleRu)).SequenceEqual(sections),
                    "The section registry contains the four categories in display order", report);
                var parseCategory = typeof(AchievementCatalog).GetMethod("TabFromCategoryId", Static);
                Check(tabs.GetComponentsInChildren<Button>(false).Length == sections.Length, "Exactly four current tabs are visible", report);
                foreach (var (section, id, caption) in sections)
                {
                    Check((AchievementTab)parseCategory.Invoke(null, new object[] { id.ToUpperInvariant() }) == section,
                        "Server category maps through the registry: " + id, report);
                    var tab = Button(tabs, "JournalTab-" + id);
                    Check(tab.GetComponentsInChildren<TMP_Text>().Any(label => label.text == caption), "Category caption: " + id, report);
                    Click(tab, events);
                    Flush(scroll);
                    var expected = catalog.Where(entry => entry.Tab == section).ToArray();
                    Check(scroll.content.GetComponentsInChildren<Button>().Count(row => row.name.StartsWith("JournalRow-", StringComparison.Ordinal)) == expected.Length,
                        "Correct catalog entries for " + id, report);
                    foreach (var entry in expected)
                    {
                        var row = Button(scroll.content, "JournalRow-" + entry.ChainId);
                        Check(Mathf.Approximately(((RectTransform)row.transform).rect.height, 228f),
                            "Album medal slot is 50 percent taller: " + entry.ChainId, report);
                        Check(Find(row.transform, "MedalRewardIcon").GetComponent<Image>().sprite != null,
                            "Medal slot shows a reward icon: " + entry.ChainId, report);
                        var title = Label(row.transform, "MedalTitle");
                        var condition = Label(row.transform, "MedalCondition");
                        Check(title.text.StartsWith(entry.TitleRu, StringComparison.Ordinal) && title.color.grayscale >= .55f
                            && ((title.fontStyle & FontStyles.Bold) != 0 || (int)title.fontWeight >= 600),
                            "Light bold title: " + entry.ChainId, report);
                        Check((condition.fontStyle & FontStyles.Italic) != 0 && condition.color.g > condition.color.r && condition.color.g > condition.color.b,
                            "Green italic condition: " + entry.ChainId, report);
                        Click(row, events);
                        var modal = OpenModal(modalHost);
                        Check(modal.ChainId == entry.ChainId && Label(modal.transform, "MedalTitle").text.StartsWith(entry.TitleRu, StringComparison.Ordinal),
                            "Row opens its own medal: " + entry.ChainId, report);
                        Check(Find(modal.transform, "MedalRewardIcon").GetComponent<Image>().sprite != null,
                            "Detail shows a reward icon: " + entry.ChainId, report);
                        Click(Button(modal.transform, "Close"), events);
                        Check(!modal.gameObject.activeSelf && controller.IsVisible, "Closing detail keeps Journal open: " + entry.ChainId, report);
                    }
                    if (section == AchievementTab.Resources || (height == 2400 && section != AchievementTab.Obsession))
                        Capture(canvas, camera, target, $"{width}x{height}-{id}", report);
                    if (height == 1920 && section == AchievementTab.Resources)
                    {
                        Click(Button(scroll.content, "JournalRow-res.ore_earned"), events);
                        var modal = OpenModal(modalHost);
                        Capture(canvas, camera, target, $"{width}x{height}-{id}-detail", report);
                        Click(Button(modal.transform, "Close"), events);
                    }
                }

                Click(Button(tabs, "JournalTab-slaughter"), events);
                Flush(scroll);
                Check(scroll.content.rect.height > scroll.viewport.rect.height, "Slaughter has a genuinely scrollable Journal", report);
                scroll.verticalNormalizedPosition = .37f;
                Flush(scroll);
                var position = scroll.verticalNormalizedPosition;
                var selected = catalog.Where(entry => entry.Tab == AchievementTab.Slaughter).Skip(3).First();
                var scrolledRow = Button(scroll.content, "JournalRow-" + selected.ChainId);
                Click(scrolledRow, events);
                var scrolledModal = OpenModal(modalHost);
                Click(Button(scrolledModal.transform, "Close"), events);
                Flush(scroll);
                Check(position is > .05f and < .95f && Mathf.Abs(scroll.verticalNormalizedPosition - position) < .01f
                    && scrolledRow == Button(scroll.content, "JournalRow-" + selected.ChainId),
                    "Detail closes without rebuilding the active category or resetting its scroll", report);

                Click(Button(tabs, "JournalTab-obsession"), events);
                Flush(scroll);
                var crossRow = Button(scroll.content, "JournalRow-" + chain.ChainId);
                var targetCount = AchievementUiRules.StepTarget(chain, current);
                var claimFrame = Find(crossRow.transform, "ClaimReadyFrame").GetComponent<Image>();
                var claimTag = Find(crossRow.transform, "ClaimReadyTag").GetComponent<Image>();
                Check(Progress(crossRow.transform).Contains($"{targetCount - 1}/{targetCount}"), "Journal shows the delta for the first unclaimed step", report);
                Check(!claimFrame.enabled && !claimTag.enabled, "An in-progress medal has no claim highlight", report);
                Capture(canvas, camera, target, $"{width}x{height}-journal", report);
                Click(crossRow, events);
                var detail = OpenModal(modalHost);
                var body = detail.transform;
                Check(detail.StepIndex == current, "Row defaults to the first unclaimed step", report);
                Check(Label(body, "MedalCondition").text.Contains(chain.Descriptions[current]), "Detail includes the current full condition", report);
                Check(Label(body, "MedalReward").text.Contains(chain.RewardTexts[current].Replace("Награда:", "").Trim()),
                    "Detail includes the current full reward", report);
                Check(Progress(body).Contains($"{targetCount - 1}/{targetCount}"), "Detail shows current delta progress", report);
                Check(!ClaimAvailable(body), "An in-progress step cannot claim its reward", report);
                for (var i = 0; i < chain.Thresholds.Length; i++)
                {
                    var step = Find(body, "ChainStep" + (i + 1));
                    Check(!string.IsNullOrWhiteSpace(Label(step, "ChainStepStatus").text), "Step status is shown in chain: " + i, report);
                    if (i < current) Check(Label(step, "ChainStepStatus").text.Contains("Получено"), "Earlier earned step is marked received: " + i, report);
                }
                Capture(canvas, camera, target, $"{width}x{height}-detail", report);
                Click(Button(body, "ChainStep4"), events);
                Check(detail.StepIndex == 3 && Label(body, "MedalCondition").text.Contains(chain.Descriptions[3]) && !ClaimAvailable(body),
                    "Future step can be inspected without enabling its claim", report);
                Click(Button(body, "Close"), events);
                counters[chain.StatKey]++;
                Call(controller, "OnAchievementLifecycleChanged");
                Check(crossRow == Button(scroll.content, "JournalRow-" + chain.ChainId), "Progress refresh reuses the visible row", report);
                Check(claimFrame.enabled && claimTag.enabled && !claimFrame.raycastTarget && !claimTag.raycastTarget
                    && Label(crossRow.transform, "MedalStatus").text == "ЗАБРАТЬ",
                    "Claimable medal shows a full gold frame and an action label without blocking the row", report);
                Capture(canvas, camera, target, $"{width}x{height}-claimable", report);
                Click(crossRow, events);
                detail = OpenModal(modalHost);
                Check(AchievementRewardClaim.CanClaimStep(chain, current) && ClaimAvailable(detail.transform), "Eligible current reward has a claim action", report);
                string requestedChain = null;
                var requestedStep = -1;
                detail.Show(chain, current, null, (id, step) => { requestedChain = id; requestedStep = step; });
                Click(Button(detail.transform, "ClaimReward"), events);
                Check(!detail.IsVisible && requestedChain == chain.ChainId && requestedStep == current && claims.Count == current,
                    "Claim dispatches the selected identity without modifying fixture claim tokens", report);
                claims.Clear();
                Call(controller, "OnAchievementLifecycleChanged");
                Click(crossRow, events);
                detail = OpenModal(modalHost);
                Click(Button(detail.transform, "ChainStep3"), events);
                Check(!AchievementRewardClaim.CanClaimStep(chain, current) && !ClaimAvailable(detail.transform),
                    "Completed later reward still requires previous claims", report);
                Click(Button(detail.transform, "Close"), events);
                for (var i = 0; i < chain.Thresholds.Length; i++) claims.Add(chain.ChainId + ":" + i);
                counters[chain.StatKey] = AchievementUiRules.CumulativeRequired(chain, chain.Thresholds.Length - 1);
                Call(controller, "OnAchievementLifecycleChanged");
                Check(!claimFrame.enabled && !claimTag.enabled, "The claimed medal loses its claim highlight", report);
                Click(crossRow, events);
                detail = OpenModal(modalHost);
                Check(Label(detail.transform, "MedalCondition").text.Contains(chain.Descriptions.Last()) && !ClaimAvailable(detail.transform),
                    "Completed Journal row opens final step with no duplicate claim", report);
                counters[chain.StatKey] = 0;
                Call(controller, "OnAchievementLifecycleChanged");
                var finalTarget = AchievementUiRules.StepTarget(chain, chain.Thresholds.Length - 1);
                Check(detail.IsVisible && Label(detail.transform, "MedalStatus").text == "Получено"
                    && Progress(detail.transform).Contains($"{finalTarget}/{finalTarget}"),
                    "A previously claimed medal keeps full progress while its counter is stale", report);
            }
            finally
            {
                RenderTexture.active = priorTexture;
                EditorSceneManager.CloseScene(scene, true);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static AchievementStepDetailModal OpenModal(Transform modalHost)
        {
            var modal = modalHost.GetComponentInChildren<AchievementStepDetailModal>();
            if (modal == null || !modal.gameObject.activeInHierarchy) throw new InvalidOperationException("Row did not open a modal on the shell overlay.");
            return modal;
        }

        private static bool ClaimAvailable(Transform root)
        {
            var claim = Button(root, "ClaimReward");
            return claim.gameObject.activeInHierarchy && claim.IsInteractable();
        }

        private static string Progress(Transform root) => Label(root, "MedalProgress").text.Replace(" ", "");
        private static Button Button(Transform root, string name) => Find(root, name).GetComponent<Button>();
        private static TMP_Text Label(Transform root, string name) => Find(root, name).GetComponent<TMP_Text>();

        private static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name)
            ?? throw new InvalidOperationException("Missing Journal element " + name);

        private static void Click(Button button, EventSystem events)
        {
            if (button == null || !button.isActiveAndEnabled || !button.IsInteractable())
                throw new InvalidOperationException("Journal button is not interactive: " + button?.name);
            button.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
        }

        private static void Flush(ScrollRect scroll)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var child in canvas.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            else camera.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                var path = Output + "/" + name + ".png";
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static void Replace(Dictionary<FieldInfo, object> previous, Type type, string name, object value)
        {
            var field = type.GetField(name, Static);
            previous.Add(field, field.GetValue(null));
            field.SetValue(null, value);
        }

        private static void Call(object target, string method) => target.GetType().GetMethod(method, Instance).Invoke(target, null);

        private static void Check(bool passed, string message, Report report)
        {
            if (!passed) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }
    }
}
