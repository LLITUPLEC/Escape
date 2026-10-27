using System;
using System.IO;
using System.Linq;
using System.Text;
using Project.Match3;
using Project.Character.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace Project.Editor
{
    /// <summary>Local editor QA mailbox. Requests only operate on this project and never run shell commands.</summary>
    [InitializeOnLoad]
    public static class ExpeditionEditorBridge
    {
        [Serializable] private sealed class Request { public string id; public string command; public string scene; public string target; }
        [Serializable] private sealed class Reply { public string id; public bool ok; public string message; public string path; public bool playing; public bool compiling; }
        private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.cache/qa/bridge"));
        private static double _nextPoll;

        static ExpeditionEditorBridge() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            _nextPoll = EditorApplication.timeSinceStartup + .5;
            var mailbox = Path.Combine(Root, "request.json");
            if (!File.Exists(mailbox)) return;
            Request request = null;
            try
            {
                request = JsonUtility.FromJson<Request>(File.ReadAllText(mailbox));
                File.Delete(mailbox);
                var reply = Execute(request);
                reply.id = request.id;
                reply.playing = EditorApplication.isPlaying;
                reply.compiling = EditorApplication.isCompiling;
                Directory.CreateDirectory(Root);
                File.WriteAllText(Path.Combine(Root, "reply.json"), JsonUtility.ToJson(reply, true));
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(Path.Combine(Root, "reply.json"), JsonUtility.ToJson(new Reply { id = request?.id, ok = false, message = ex.ToString(), playing = EditorApplication.isPlaying }, true));
            }
        }

        private static Reply Execute(Request request)
        {
            switch (request.command)
            {
                case "status": return new Reply { ok = true, message = SceneManager.GetActiveScene().name + "; " + Screen.width + "x" + Screen.height };
                case "refresh-editor":
                {
                    EditorApplication.CallbackFunction refresh = null;
                    refresh = () => { EditorApplication.update -= refresh; AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); };
                    EditorApplication.update += refresh;
                    return new Reply { ok = true, message = "Editor asset refresh queued" };
                }
                case "prepare": ExpeditionArtImporter.Setup(); return new Reply { ok = true, message = "Art and font prepared" };
                case "prepare-reference":
                {
                    ExpeditionUiArtImporter.Prepare();
                    ExpeditionArtImporter.Setup();
                    var iconCount = Resources.LoadAll<Sprite>("Expedition/UI/ui-icons-atlas").Length;
                    var serif = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Resources/Expedition/ExpeditionSerif.asset");
                    return new Reply { ok = iconCount == 20 && serif != null && Project.UI.ExpeditionArt.Frame() != null,
                        message = "Reference UI: icons=" + iconCount + "; serif=" + (serif != null) + "; textured frame=" + (Project.UI.ExpeditionArt.Frame() != null) };
                }
                case "optimize-package": ExpeditionPackageOptimizer.Prepare(); return new Reply { ok = true, message = "Reference fonts remapped; dependency report saved" };
                case "build-android": ExpeditionAndroidBuild.Begin(); return new Reply { ok = true, message = "Android development APK scheduled", path = ExpeditionAndroidBuild.StatusPath };
                case "build-release": ExpeditionAndroidBuild.BeginRelease(); return new Reply { ok = true, message = "Android release APK scheduled", path = ExpeditionAndroidBuild.StatusPath };
                case "fontdiag":
                    var font = Project.UI.ExpeditionTheme.Font;
                    var regular = TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, false, FontStyles.Normal, FontWeight.Regular, out _);
                    var bold = TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, false, FontStyles.Bold, FontWeight.Bold, out _);
                    var weighted = TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, false, FontStyles.Normal, FontWeight.Bold, out _);
                    return new Reply { ok = regular != null && bold != null && weighted != null, message = "Ellipsis regular/bold/weight: " + (regular != null) + "/" + (bold != null) + "/" + (weighted != null) };
                case "font-build-state": return CheckEmptyFont();
                case "polish": return new Reply { ok = true, message = ExpeditionPolishQa.Run(request.target) };
                case "iteration": return new Reply { ok = true, message = ExpeditionIterationQa.Run(request.target) };
                case "world-probe": return new Reply { ok = true, message = ExpeditionWorldQa.Run(request.target) };
                case "startup-probe": return new Reply { ok = true, message = ExpeditionStartupQa.StartProbe() };
                case "battle-polish": return new Reply { ok = true, message = ExpeditionBattlePolishQa.Run(request.target) };
                case "match-states": return new Reply { ok = true, message = ExpeditionMatchStatesQa.Run(request.target) };
                case "result-integration": return new Reply { ok = true, message = ExpeditionResultIntegrationQa.Run(request.target) };
                case "meta-rows": return new Reply { ok = true, message = request.target == "show" ? ExpeditionMetaRowsQa.ShowLeaderboard() : request.target == "restore" ? ExpeditionMetaRowsQa.RestoreLeaderboard() : request.target == "achievements" ? ExpeditionMetaRowsQa.RunAchievementStates() : ExpeditionMetaRowsQa.VerifyLeaderboard() };
                case "workshop-slots": return new Reply { ok = true, message = request.target == "restore" ? ExpeditionWorkshopSlotsQa.Restore() : request.target == "verify" ? ExpeditionWorkshopSlotsQa.Verify() : ExpeditionWorkshopSlotsQa.Show(request.target) };
                case "inventory-pages": return new Reply { ok = true, message = request.target == "diagnose" ? ExpeditionInventoryPagesQa.Diagnose() : request.target == "restore" ? ExpeditionInventoryPagesQa.Restore() : request.target == "verify" ? ExpeditionInventoryPagesQa.Verify() : ExpeditionInventoryPagesQa.Show(request.target) };
                case "selected-audio": return new Reply { ok = true, message = ExpeditionSelectedAudioQa.Run() };
                case "ability-art": return new Reply { ok = true, message = ExpeditionAbilityFxImporter.Prepare() };
                case "sound-settings": return new Reply { ok = true, message = ExpeditionAudioSettingsQa.Run(request.target) };
                case "ability-fx": return new Reply { ok = true, message = ExpeditionAbilityFxQa.Run() };
                case "ability-fx-0310": return new Reply { ok = true, message = ExpeditionAbilityFxQa.Run(".cache/qa/legacy-0.3.10/ability-fx") };
                case "ability-fx-0311":
                {
                    foreach (var figure in UnityEngine.Object.FindObjectsByType<ExpeditionCombatantFigure>(FindObjectsSortMode.None))
                        if (figure.name == "ExpeditionPlayerFigure") figure.BindPreview(Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"), true);
                    return new Reply { ok = true, message = ExpeditionAbilityFxQa.Run(".cache/qa/legacy-0.3.11/ability-fx") };
                }
                case "stakes-038": return new Reply { ok = true, message = ExpeditionNotificationStakeQa.Capture038() };
                case "arena-json-contract": return new Reply { ok = true, message = ExpeditionArenaJsonContractQa.Run() };
                case "resource-amounts": return new Reply { ok = true, message = ExpeditionResourceAmountQa.Run() };
                case "daily-art": return new Reply { ok = true, message = ExpeditionDailyArtImporter.Prepare() };
                case "daily": return new Reply { ok = true, message = ExpeditionDailyQa.Run(request.target) };
                case "stakes-039": return new Reply { ok = true, message = ExpeditionNotificationStakeQa.Capture(".cache/qa/legacy-0.3.9/stakes") };
                case "consumables-039": return new Reply { ok = true, message = ExpeditionConsumableQa.Run() };
                case "contracts-039": return new Reply { ok = true, message = ExpeditionRpcContracts039.Run() };
                case "friends-badge-0310": return new Reply { ok = true, message = ExpeditionFriendsBadgeQa.Run() };
                case "tournament-queue-0310": return new Reply { ok = true, message = ExpeditionTournamentQueueQa0310.Run(request.target) };
                case "tournament-queue-0313": return new Reply { ok = true, message = ExpeditionTournamentQueueQa0313.Run(request.target) };
                case "global-boss-0313": return new Reply { ok = true, message = ExpeditionGlobalBossQa0313.Run(request.target) };
                case "global-boss-0314": return new Reply { ok = true, message = ExpeditionGlobalBossQa0314.Run(request.target) };
                case "shop-0314": return new Reply { ok = true, message = ExpeditionShopQa0314.Run(request.target) };
                case "shop-0313": return new Reply { ok = true, message = ExpeditionShopQa0313.Run(request.target) };
                case "mail-0313": return new Reply { ok = true, message = ExpeditionMailQa0313.Run(request.target) };
                case "combatant-grounding-0311": return new Reply { ok = true, message = ExpeditionCombatantGroundingQa.Run() };
                case "arena-radio-0311": return new Reply { ok = true, message = ExpeditionArenaRadioQa0311.Run(request.target) };
                case "tutorial-0311": return new Reply { ok = true, message = TutorialQa.Run(request.target) };
                case "tutorial-0312": return new Reply { ok = true, message = TutorialQa.Run(request.target) };
                case "daily-login-0312": return new Reply { ok = true, message = ExpeditionDailyLoginQa0312.Run(request.target) };
                case "daily-login-0313": return new Reply { ok = true, message = ExpeditionDailyLoginQa0313.Run(request.target) };
                case "connection-0312": return new Reply { ok = true, message = ServerConnectionGuardQa0312.Run(request.target) };
                case "content-gates-0311": return new Reply { ok = true, message = ProgressiveContentQa0311.Run() };
                case "appearance-039": return new Reply { ok = true, message = ExpeditionAppearanceCapture039.Run(request.target) };
                case "completed-media":
                    ExpeditionMediaCompletionQa.RunAll();
                    return new Reply { ok = true, message = "All completed media validated; mobile Hero and registration previews captured." };
                case "appearance-preview":
                    ExpeditionAppearanceCapture.CapturePreviews();
                    return new Reply { ok = true, message = "Paired-only Hero appearance previews captured." };
                case "notification-stakes":
                    return new Reply { ok = true, message = request.target switch
                    {
                        "setup" => ExpeditionNotificationStakeOfflineFixture.Setup(),
                        "fixture-status" => ExpeditionNotificationStakeOfflineFixture.Status(),
                        "restore" => ExpeditionNotificationStakeOfflineFixture.Restore(),
                        "play" => ExpeditionNotificationStakeQa.Run(),
                        _ => ExpeditionNotificationStakeQa.Capture()
                    } };
                case "release-battle-status":
                {
                    var battle = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
                    var activeNetworkMatch = false;
                    if (battle != null)
                    {
                        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var type = battle.GetType();
                        var match = type.GetField("_match", flags)?.GetValue(battle);
                        var ended = type.GetField("_gameEnded", flags)?.GetValue(battle) is bool done && done;
                        activeNetworkMatch = match != null && !ended;
                    }
                    return new Reply { ok = !activeNetworkMatch, message = SceneManager.GetActiveScene().name + "; active network match=" + activeNetworkMatch + "; manager=" + (battle != null) };
                }
                case "release-view":
                {
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                    var restore = request.target == "restore";
                    if (!restore)
                    {
                        SessionState.SetString("Escape.ReleaseCapture.StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Project/Scenes/MainMenu.unity");
                    }
                    typeof(ExpeditionNotificationStakeOfflineFixture).GetMethod(restore ? "RestoreGameView" : "SetGameView", flags).Invoke(null, null);
                    if (restore)
                    {
                        var previous = SessionState.GetString("Escape.ReleaseCapture.StartScene", "");
                        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
                    }
                    return new Reply { ok = true, message = restore ? "Release capture settings restored." : "Real-game release capture set to 1080x1920; MainMenu Play start; original Editor scene preserved." };
                }
                case "release-capture-view":
                    return new Reply { ok = true, message = ExpeditionReleaseCaptureView.Run(request.target) };
                case "release-hero":
                {
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    Project.UI.ExpeditionPresentation.OpenCharacter();
                    var layout = UnityEngine.Object.FindFirstObjectByType<CharacterExpeditionLayout>(FindObjectsInactive.Include);
                    if (layout == null) throw new InvalidOperationException("Wait for the Hero page to build.");
                    layout.SelectHeroTab(1);
                    var panel = layout.GetComponent<CharacterAppearancePanel>();
                    if (panel == null) throw new InvalidOperationException("Appearance page missing.");
                    for (var i = 0; i < Project.Character.CharacterAppearance.Themes.Count; i++)
                        if (Project.Character.CharacterAppearance.Themes[i].Id == request.target) { panel.SelectTheme(i); break; }
                    return new Reply { ok = true, message = "Real Hero preview opened; no cosmetic Apply or account changes." };
                }
                case "release-daily":
                {
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    foreach (var view in UnityEngine.Object.FindObjectsByType<Project.UI.ExpeditionDailyPanel>(FindObjectsSortMode.None))
                        UnityEngine.Object.Destroy(view.gameObject);
                    if (request.target == "close") return new Reply { ok = true, message = "Daily preview closed; no claim or account actions." };
                    var name = request.target == "quests" ? "ExpeditionDailyQuests" : "ExpeditionDailyLogin";
                    var button = GameObject.Find(name)?.GetComponent<Button>();
                    if (button == null) throw new InvalidOperationException("Wait for the Refuge shortcut to build.");
                    button.onClick.Invoke();
                    return new Reply { ok = true, message = "Opened the actual daily shortcut; server read only, no claim." };
                }
                case "chrome": return new Reply { ok = true, message = request.target switch
                {
                    "hud" => ExpeditionChromeQa.RunHud(), "aura" => ExpeditionChromeQa.ShowAura(),
                    "aura-verify" => ExpeditionChromeQa.VerifyAuraAndClose(), "aura-restore" => ExpeditionChromeQa.RestoreAura(),
                    "close-show" => ExpeditionChromeQa.ShowCloseIcon(), "close-verify" => ExpeditionChromeQa.VerifyCloseIcon(),
                    "close-restore" => ExpeditionChromeQa.RestoreCloseIcon(), "active-close" => ExpeditionChromeQa.DiagnoseActiveClose(),
                    "close-affix" => ExpeditionChromeQa.ShowAffix(), "affix-show" => ExpeditionChromeQa.ShowAffix(),
                    "affix-open" => ExpeditionChromeQa.OpenAffix(), "affix-verify" => ExpeditionChromeQa.VerifyAffixAndClose(),
                    "affix-restore" => ExpeditionChromeQa.RestoreAffix(),
                    _ => ExpeditionChromeQa.RunCloseIcon()
                } };
                case "achievements-probe": return new Reply { ok = true, message = ExpeditionAchievementsQa.Run(request.target) };
                case "bracket-probe": return new Reply { ok = true, message = ExpeditionArenaBracketQa.Run(request.target) };
                case "prepare-resource-icons": return new Reply { ok = true, message = Project.UI.Editor.ExpeditionResourceIconsPreparation.Prepare() };
                case "mine-travel-probe": return new Reply { ok = true, message = ExpeditionMineTravelQa.StartProbe() };
                case "equipment-probe": return new Reply { ok = true, message = ExpeditionCharacterEquipmentQa.Run(request.target) };
                case "battle-top-probe": return new Reply { ok = true, message = ExpeditionBattleTopQa.Run(request.target) };
                case "ability-cost":
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    var abilityPanel = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>();
                    if (abilityPanel == null) throw new InvalidOperationException("Ability panel required");
                    // UI-only fixture before starting a match. No RPC or authoritative state changes.
                    abilityPanel.Refresh(new PlayerStats { mana = 100 }, false, false, 20, 20, 30, 40, 30);
                    var abilityButtons = new[] { abilityPanel.petardButton, abilityPanel.crossButton, abilityPanel.squareButton, abilityPanel.shieldButton, abilityPanel.furyButton };
                    var expectedCosts = new[] { "30", "20", "20", "40", "30" };
                    var actualCosts = abilityButtons.Select(b => b.transform.Find("AbilityCost").GetComponent<TMP_Text>().text).ToArray();
                    return new Reply { ok = actualCosts.SequenceEqual(expectedCosts), message = "Ability cost labels after Refresh: " + string.Join(", ", actualCosts) };
                case "mine-depth":
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    var minePresentation = UnityEngine.Object.FindFirstObjectByType<Project.UI.ExpeditionMinePresentation>();
                    if (minePresentation == null) throw new InvalidOperationException("Mine presentation required");
                    var route = minePresentation.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "ConnectedLandings");
                    var firstLanding = route.Find("Landing_01") as RectTransform;
                    var finalLanding = route.Find("Landing_12") as RectTransform;
                    var depthScroll = route.GetComponentInParent<ScrollRect>();
                    minePresentation.SelectFloor(12, true);
                    Canvas.ForceUpdateCanvases();
                    var bottomScroll = depthScroll.verticalNormalizedPosition;
                    minePresentation.SelectFloor(1, true);
                    Canvas.ForceUpdateCanvases();
                    var topScroll = depthScroll.verticalNormalizedPosition;
                    var depthPass = firstLanding.anchoredPosition.y > finalLanding.anchoredPosition.y && bottomScroll < .01f && topScroll > .99f;
                    return new Reply { ok = depthPass, message = "Mine order 1 above 12=" + depthPass + "; scroll first/last=" + topScroll.ToString("0.00") + "/" + bottomScroll.ToString("0.00") };
                case "shortcut-state": return CheckShortcutState();
                case "drag-cancel": return CheckDragCancel();
                case "inventory-scroll":
                    if (!EditorApplication.isPlaying || EventSystem.current == null) throw new InvalidOperationException("Play mode and an open character screen required");
                    var inventoryHandle = UnityEngine.Object.FindObjectsByType<CharacterDragSlotHandle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(h => h.Kind == CharacterDragSlotKind.Inventory && h.GetComponentInParent<CharacterScreenView>().HasInventoryItem(h.InventoryIndex));
                    if (inventoryHandle == null) throw new InvalidOperationException("A filled inventory cell is required");
                    var inventoryView = inventoryHandle.GetComponentInParent<CharacterScreenView>();
                    var inventoryScroll = inventoryHandle.GetComponentInParent<ScrollRect>();
                    if (inventoryScroll == null) throw new InvalidOperationException("Backpack ScrollRect missing");
                    inventoryView.TryGetInventoryItem(inventoryHandle.InventoryIndex, out var originalId, out _);
                    var beforeScroll = inventoryScroll.verticalNormalizedPosition;
                    var slotRect = (RectTransform)inventoryHandle.transform;
                    var slotPoint = RectTransformUtility.WorldToScreenPoint(null, slotRect.TransformPoint(slotRect.rect.center));
                    var gesture = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = -1, position = slotPoint, pressPosition = slotPoint, pointerDrag = inventoryHandle.gameObject };
                    ExecuteEvents.Execute(inventoryHandle.gameObject, gesture, ExecuteEvents.pointerDownHandler);
                    gesture.position = slotPoint + Vector2.up * 24;
                    inventoryHandle.OnBeginDrag(gesture);
                    gesture.position = slotPoint + Vector2.up * 180;
                    gesture.delta = Vector2.up * 156;
                    inventoryHandle.OnDrag(gesture);
                    var afterScroll = inventoryScroll.verticalNormalizedPosition;
                    inventoryHandle.OnEndDrag(gesture);
                    inventoryScroll.StopMovement();
                    inventoryScroll.verticalNormalizedPosition = beforeScroll;
                    inventoryView.TryGetInventoryItem(inventoryHandle.InventoryIndex, out var afterId, out _);
                    return new Reply { ok = afterScroll < beforeScroll && originalId == afterId, message = "Filled-cell vertical gesture: " + beforeScroll.ToString("0.00") + " -> " + afterScroll.ToString("0.00") + "; item preserved=" + (originalId == afterId) };
                case "play": EditorApplication.isPlaying = true; return new Reply { ok = true, message = "Starting play mode" };
                case "stop": EditorApplication.isPlaying = false; return new Reply { ok = true, message = "Stopping play mode" };
                case "scene":
                    if (!new[] { "MainMenu", "ArenaMenu", "MineScene", "WorkshopScene", "DuelMatch3" }.Contains(request.scene)) throw new ArgumentException("Unsupported QA scene");
                    if (EditorApplication.isPlaying) SceneManager.LoadScene(request.scene);
                    else
                    {
                        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Active scene has unsaved changes; preserved.");
                        EditorSceneManager.OpenScene("Assets/_Project/Scenes/" + request.scene + ".unity");
                    }
                    return new Reply { ok = true, message = "Opening " + request.scene };
                case "solo":
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    Match3LaunchContext.SetMode(Match3LaunchMode.SoloBot);
                    SceneManager.LoadScene("DuelMatch3");
                    return new Reply { ok = true, message = "Opening local solo board" };
                case "hierarchy":
                    var builder = new StringBuilder();
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Describe(root.transform, builder, 0);
                    var treePath = Path.Combine(Root, "hierarchy.txt");
                    File.WriteAllText(treePath, builder.ToString());
                    return new Reply { ok = true, path = treePath, message = "Scene hierarchy exported" };
                case "capture":
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required for real Game view capture");
                    var tag = string.IsNullOrEmpty(request.target) ? SceneManager.GetActiveScene().name : request.target;
                    if (tag.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid screenshot name");
                    if (tag.StartsWith("publish-", StringComparison.Ordinal))
                    {
                        // Hide the local account alias in public showcase shots. UI-only; never rename the account.
                        var labels = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                        var aliases = new HashSet<string>(labels.Where(t => t.name == "ProfileName" && !string.IsNullOrWhiteSpace(t.text)).Select(t => t.text));
                        foreach (var label in labels) if (aliases.Contains(label.text)) label.text = "Скиталец";
                        foreach (var nickname in UnityEngine.Object.FindObjectsByType<CharacterNicknameEditor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        {
                            var label = new SerializedObject(nickname).FindProperty("nameLabel")?.objectReferenceValue as TMP_Text;
                            if (label != null) label.text = "Скиталец";
                        }
                        Canvas.ForceUpdateCanvases();
                    }
                    if (tag.StartsWith("publish-", StringComparison.Ordinal))
                    {
                        var nativeCapture = ExpeditionReleaseCaptureView.Capture(tag);
                        return new Reply { ok = true, path = nativeCapture, message = "Captured actual 1080x1920 Game-view render texture; full native frame, no image edits." };
                    }
                    var screenshot = Path.Combine(Root, "screenshots", tag + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(screenshot));
                    ScreenCapture.CaptureScreenshot(screenshot);
                    return new Reply { ok = true, path = screenshot, message = "Capture scheduled" };
                case "click":
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
                    var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(b => b.name == request.target && b.interactable).ToArray();
                    if (buttons.Length != 1) throw new InvalidOperationException("Expected one active button, found " + buttons.Length);
                    if (!AllowedButton(request.target)) throw new InvalidOperationException("Only local navigation is allowed in the QA mailbox");
                    buttons[0].onClick.Invoke();
                    return new Reply { ok = true, message = "Invoked navigation " + request.target };
                case "tap":
                    if (!EditorApplication.isPlaying || !AllowedButton(request.target)) throw new InvalidOperationException("Only supported UI buttons are allowed");
                    var targets = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(b => b.name == request.target && b.IsInteractable()).ToArray();
                    if (targets.Length != 1 || EventSystem.current == null) throw new InvalidOperationException("One active button and EventSystem required");
                    var target = targets[0];
                    var targetCanvas = target.GetComponentInParent<Canvas>();
                    var targetRect = (RectTransform)target.transform;
                    var position = RectTransformUtility.WorldToScreenPoint(targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera, targetRect.TransformPoint(targetRect.rect.center));
                    var pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left, pointerId = -1 };
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointer, hits);
                    var handler = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
                    if (handler != target.gameObject) throw new InvalidOperationException("Button is obscured at center: " + (hits.Count > 0 ? hits[0].gameObject.name : "no raycast hit"));
                    pointer.pointerCurrentRaycast = hits[0];
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(handler, pointer, ExecuteEvents.pointerClickHandler);
                    return new Reply { ok = true, message = "Raycast tap " + request.target };
                case "tab":
                    if (!EditorApplication.isPlaying || !request.target.StartsWith("Tab")) throw new InvalidOperationException("Only local tabs are allowed");
                    var tabs = UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(t => t.name == request.target && t.interactable).ToArray();
                    if (tabs.Length != 1) throw new InvalidOperationException("Expected one active tab, found " + tabs.Length);
                    tabs[0].isOn = true;
                    return new Reply { ok = true, message = "Selected tab " + request.target };
                default: throw new ArgumentException("Unknown QA request");
            }
        }

        private static bool AllowedButton(string name) => name == "TutorialReplayButton" || name.StartsWith("ExpeditionNav") || name.StartsWith("ExpeditionOpen") || name == "CloseButton" || name == "BackButton" || name == "SettingsButton" || name.StartsWith("Tab") || name.StartsWith("InventoryFilter_") || name.StartsWith("Diff_") || name == "DossierAction" || name == "ChooseRecipe" || name == "CloseRecipes" || name == "StartBoss" || name == "QuitBtn" || name == "BtnStay" || name == "BtnYes" || name == "AffixButton" || name == "Close" || name == "NicknameButton" || name == "Cancel" || name == "Btn_Закрыть" || name == "match3Arena" || name == "match3Arena_Ore" || name == "match3Arena_Gold" || name.StartsWith("Depth_");

        private static Reply CheckShortcutState()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
            var binding = UnityEngine.Object.FindObjectsByType<Project.UI.ExpeditionShortcutBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Single(b => b.name == "ExpeditionOpenAchievements");
            var source = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Single(b => b.name == "BottomButtonAchiev");
            var sourceBadge = source.transform.Find("PendingClaimBadge");
            var sourceCount = sourceBadge.GetComponentInChildren<TMP_Text>(true);
            var sourceGroup = sourceBadge.GetComponent<CanvasGroup>();
            var oldText = sourceCount.text;
            var oldAlpha = sourceGroup.alpha;
            var oldEnabled = source.interactable;
            try
            {
                // UI-only fixture; no achievement or account data is mutated.
                sourceCount.text = "3";
                sourceGroup.alpha = 1;
                source.interactable = false;
                binding.Refresh();
                var badge = binding.transform.Find("ExpeditionPendingBadge");
                var count = badge != null ? badge.GetComponentInChildren<TMP_Text>() : null;
                var passed = badge != null && badge.gameObject.activeSelf && count != null && count.text == "3" && !binding.GetComponent<Button>().interactable;
                return new Reply { ok = passed, message = "Shortcut reward badge and disabled state mirrored=" + passed };
            }
            finally
            {
                sourceCount.text = oldText;
                sourceGroup.alpha = oldAlpha;
                source.interactable = oldEnabled;
                binding.Refresh();
            }
        }

        private static Reply CheckEmptyFont()
        {
            var prepared = Project.UI.ExpeditionTheme.Font;
            TMP_FontAsset empty = null;
            try
            {
                empty = TMP_FontAsset.CreateFontAsset(prepared.sourceFontFile, 72, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic);
                var method = typeof(Project.UI.ExpeditionTheme).GetMethod("PrepareEllipsis", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                method.Invoke(null, new object[] { empty });
                var character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, empty, false, FontStyles.Bold, FontWeight.Bold, out _);
                var setting = new SerializedObject(prepared).FindProperty("m_ClearDynamicDataOnBuild");
                var keepAtlas = setting != null && !setting.boolValue;
                return new Reply { ok = character != null && keepAtlas, message = "Empty dynamic font recovers bold ellipsis=" + (character != null) + "; prepared atlas retained on build=" + keepAtlas };
            }
            finally
            {
                if (empty != null)
                {
                    var material = empty.material;
                    var atlases = empty.atlasTextures;
                    UnityEngine.Object.DestroyImmediate(empty);
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                    foreach (var atlas in atlases) if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
                }
            }
        }

        private static Reply CheckDragCancel()
        {
            if (!EditorApplication.isPlaying || EventSystem.current == null) throw new InvalidOperationException("Open character screen in Play mode first");
            var handle = UnityEngine.Object.FindObjectsByType<CharacterDragSlotHandle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).First(h =>
            {
                var view = h.GetComponentInParent<CharacterScreenView>();
                return h.Kind == CharacterDragSlotKind.Inventory && view != null && view.HasInventoryItem(h.InventoryIndex);
            });
            var view = handle.GetComponentInParent<CharacterScreenView>();
            var screen = handle.GetComponentInParent<CharacterScreenController>();
            var controller = screen.GetComponent<CharacterSheetDragController>();
            if (controller == null) controller = screen.GetComponentInChildren<CharacterSheetDragController>(true);
            if (controller == null) throw new InvalidOperationException("Drag controller missing");
            var start = RectTransformUtility.WorldToScreenPoint(null, handle.transform.position);
            var gesture = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = -1, position = start, pressPosition = start, pointerDrag = handle.gameObject };
            view.TryGetInventoryItem(handle.InventoryIndex, out var before, out _);
            ExecuteEvents.Execute(handle.gameObject, gesture, ExecuteEvents.pointerDownHandler);
            gesture.position = start + Vector2.right * 40;
            handle.OnBeginDrag(gesture);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var active = typeof(CharacterSheetDragController).GetField("_activeDragSource", flags);
            var ghost = typeof(CharacterSheetDragController).GetField("_dragGhostRoot", flags);
            var started = (CharacterDragSlotHandle)active.GetValue(controller) == handle;
            screen.Close();
            var cachedGhost = (RectTransform)ghost.GetValue(controller);
            var cancelled = (CharacterDragSlotHandle)active.GetValue(controller) == null && (cachedGhost == null || !cachedGhost.gameObject.activeSelf);
            handle.OnEndDrag(gesture); // A late pointer-up after closing must not move/equip the item.
            view.TryGetInventoryItem(handle.InventoryIndex, out var after, out _);
            screen.Open();
            return new Reply { ok = started && cancelled && before == after, message = "Drag started=" + started + "; close cleared source/ghost=" + cancelled + "; item preserved=" + (before == after) };
        }

        private static void Describe(Transform node, StringBuilder text, int depth)
        {
            if (depth > 11) return;
            text.Append(' ', depth * 2).Append(node.gameObject.activeSelf ? "+ " : "- ").Append(node.name);
            var rt = node as RectTransform;
            if (rt != null) text.Append(" rect=").Append(rt.rect).Append(" anchors=").Append(rt.anchorMin).Append("..").Append(rt.anchorMax);
            var canvas = node.GetComponent<Canvas>();
            if (canvas != null) text.Append(" CANVAS order=").Append(canvas.sortingOrder).Append(" mode=").Append(canvas.renderMode);
            var label = node.GetComponent<TMP_Text>();
            if (label != null) text.Append(" text=").Append((label.text ?? "").Replace('\n', '|')).Append(" font=").Append(label.fontSize);
            var oldText = node.GetComponent<Text>();
            if (oldText != null) text.Append(" text=").Append((oldText.text ?? "").Replace('\n', '|')).Append(" font=").Append(oldText.fontSize);
            if (node.GetComponent<Button>() != null) text.Append(" BUTTON");
            text.AppendLine();
            for (var i = 0; i < node.childCount; i++) Describe(node.GetChild(i), text, depth + 1);
        }
    }
}
