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
    /// <summary>Native runtime UI in a disposable scene. No authentication, entry, claims or other account writes.</summary>
    public static class ExpeditionGlobalBossQa0313
    {
        [Serializable] private sealed class Check { public string name; public bool passed; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites; public int width = 1080, height; public string error;
            public List<Check> checks = new(); public List<string> screenshots = new();
        }
        public static string Run(string target)
        {
            if (target != null && target.StartsWith("play-", StringComparison.Ordinal)) return PlayFixture(target.Substring(5));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var height = target == "tall" ? 2400 : 1920; var output = ".cache/qa/legacy-0.3.13/global-boss/" + height;
            Directory.CreateDirectory(output); var report = new Report { height = height };
            var previous = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var bootstraps = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture texture = null;
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
                Require(report, catalog != null && catalog.Get("global_boss_teleport") != null, "Teleport item exists in shipping catalog");
                var sprite = Resources.Load<Sprite>(GlobalBossService.Art);
                Require(report, sprite != null && ExpeditionCombatantGrounding.TryGet(sprite, out var footprint) && footprint.sha256 == "7c8b937a31b7ddc709ffdcc aaff524b46fb015189c14fd24adbd52e9aab7dd7e".Replace(" ", ""), "Original boss art and baked solid-foot geometry");
                camera = new GameObject("Boss0313QaCamera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = height * .5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.transform.position = new Vector3(0, 0, -10);
                texture = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("Boss0313QaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                foreach (var variant in new[] { "active", "no-token", "used", "resume", "dead" })
                {
                    var fixture = new GlobalBossService.State { ok = true, event_id = "qa-week", name = "Сердце Разлома", status = variant == "dead" ? "dead" : "active", hp = variant == "dead" ? 0 : 471503, hp_max = 550000,
                        server_now = 1793059200, starts_at = 1792962000, ends_at = 1793566800, attempts_total = 3, attempts_left = variant == "used" ? 0 : 2, attempts_used = variant == "used" ? 3 : 1,
                        token_count = variant == "no-token" ? 0 : 2, my_damage = 5409, eligibility_progress = 1, eligibility_required = 1, qualified = true,
                        active_attempt = variant == "resume" ? new GlobalBossService.Attempt { resumable = true, match_id = "qa-no-network", attempt_id = "qa-attack" } : null };
                    var panel = ExpeditionGlobalBossPanel.Open(canvas.transform, fixture); Canvas.ForceUpdateCanvases();
                    if (variant == "active" && NakamaBootstrap.Instance == null)
                    {
                        panel.GetComponentsInChildren<Button>().First(b => b.name == "BuyToken").onClick.Invoke();
                        var shop = canvas.GetComponentInChildren<ExpeditionShopPanel>();
                        Require(report, shop != null && shop.transform.parent == panel.transform.parent && shop.transform.GetSiblingIndex() > panel.transform.GetSiblingIndex(), "Boss shop opens on the same modal canvas above boss");
                        UnityEngine.Object.DestroyImmediate(shop.gameObject);
                    }
                    var enter = panel.GetComponentsInChildren<Button>().First(b => b.name == "EnterRift");
                    Require(report, enter.interactable == (variant == "active" || variant == "resume"), "Server entry eligibility: " + variant);
                    foreach (var icon in panel.GetComponentsInChildren<Image>())
                        if (icon.name == "BossArt" || icon.name.EndsWith("Icon", StringComparison.Ordinal)) Require(report, icon.sprite != null, "Imported icon: " + variant + "/" + icon.name);
                    foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
                    {
                        if (string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate();
                        Require(report, !text.isTextOverflowing, "Readable text: " + variant + "/" + text.name);
                    }
                    var path = output + "/boss-" + variant + ".png"; Save(camera, texture, path, height); report.screenshots.Add(path);
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
                var sync = JsonUtility.FromJson<M3BoardSyncMsg>("{\"globalBoss\":true,\"globalBossMyDamage\":5409,\"bossOwnTurn\":7,\"bossScreamCooldown\":2,\"bossScreamTriggered\":true,\"bossScreamDamage\":4287,\"bossPlayerTurnsLeft\":24,\"bHp\":471503,\"bMaxHp\":550000}");
                Require(report, sync.globalBoss && sync.globalBossMyDamage == 5409 && sync.bHp == 471503 && sync.bossScreamTriggered && sync.bossScreamDamage == 4287, "Shipping BoardSync parses new boss fields");
                var terminal = JsonUtility.FromJson<M3GameOverMsg>("{\"globalBoss\":true,\"bossAttemptFinished\":true,\"bossGlobalHp\":0,\"bossGlobalMaxHp\":550000,\"bossMyDamage\":5409,\"bossQualified\":true,\"endReason\":\"boss_defeated\"}");
                Require(report, terminal.globalBoss && terminal.bossAttemptFinished && terminal.bossMyDamage == 5409 && terminal.bossQualified && terminal.endReason == "boss_defeated", "Shipping GameOver parses contribution and shared victory");
                var after = UnityEngine.Object.FindObjectsByType<NakamaBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Require(report, bootstraps.Length == after.Length && bootstraps.All(b => after.Contains(b)), "No account or Nakama lifecycle created"); report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); throw; }
            finally
            {
                RenderTexture.active = previousTexture; if (camera != null) camera.targetTexture = null;
                if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                if (scene.IsValid() && scene.isLoaded) { SceneManager.SetActiveScene(scene); foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene, true); }
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                File.WriteAllText(output + "/boss0313-report.json", JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report);
        }
        private static void Require(Report report, bool passed, string name) { report.checks.Add(new Check { name = name, passed = passed }); if (!passed) throw new InvalidOperationException(name); }
        private static string PlayFixture(string target)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!EditorApplication.isPlaying || !SceneManager.GetActiveScene().name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal) || UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) != null) throw new InvalidOperationException("Use the isolated offline Play fixture only.");
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>(); if (manager == null || manager.enabled) throw new InvalidOperationException("Disabled offline manager required.");
            void Set(string name, object value) => typeof(DuelMatch3Manager).GetField(name, flags).SetValue(manager, value);
            object Call(string name, params object[] args) => typeof(DuelMatch3Manager).GetMethod(name, flags).Invoke(manager, args);
            var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.x).ToArray();
            if (target == "battle")
            {
                Set("_myPanel", panels[0]); Set("_opPanel", panels[1]); Set("_isGlobalBoss", true);
                Set("_boardView", UnityEngine.Object.FindFirstObjectByType<Match3BoardView>()); Set("_hud", UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>());
                Set("_myStats", new PlayerStats { hp = 17148, maxHp = 21435, mana = 80, baseDamage = 2022, baseArmor = 1458, baseHeal = 932, baseCrit = .076f });
                Set("_opStats", new PlayerStats { hp = 471503, maxHp = 550000, mana = 55, baseDamage = 3713, baseArmor = 1000, baseHeal = 0, baseCrit = .08f });
                panels[0].SetPlayerName("Скиталец"); panels[0].UpdateStats(17148, 21435, 80, 100); panels[0].UpdateCombatStats(2022, 1458, 932, 7.6f); panels[0].UpdateAvatarLevel(12);
                panels[1].SetPlayerName("Сердце Разлома"); panels[1].UpdateStats(471503, 550000, 55, 100); panels[1].UpdateCombatStats(3713, 1000, 0, 8, false); panels[1].UpdateAvatarLevel(-1);
                panels[1].avatarImage.sprite = Resources.Load<Sprite>(GlobalBossService.Art); ExpeditionBattlePortrait.MarkFullBody(panels[1].avatarImage, .62f);
                var figure = GameObject.Find("ExpeditionPlayerFigure")?.GetComponent<ExpeditionCombatantFigure>();
                if (figure != null) figure.BindPreview(Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"), true);
                var abilities = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>(); Set("_abilityPanel", abilities);
                Call("TryAutoAssignAbilitySpritesInEditor"); Call("ConfigureAbilityButtonsVisuals"); ExpeditionBattleLayout.ApplyAbilityLayout(abilities);
                foreach (var button in new[] { abilities.petardButton, abilities.crossButton, abilities.squareButton, abilities.shieldButton, abilities.furyButton })
                    if (button == null || !(button.targetGraphic is Image icon) || icon.sprite == null) throw new InvalidOperationException("Actual ability sprite missing in the offline scene.");
                abilities.Refresh(new PlayerStats { mana = 80 }, true, false, 20, 20, 30, 40, 30);
                Call("InstallGlobalBossHud"); Call("UpdateGlobalBossPresentation", new M3BoardSyncMsg { globalBoss = true, globalBossMyDamage = 5409, bossPlayerTurnsLeft = 24, bossOwnTurn = 3 }, false);
                return "Actual battle layout, shared HP, boss sprite and contribution header prepared. No match or transport.";
            }
            if (target == "rules") { GameObject.Find("GlobalBossRules").GetComponent<Button>().onClick.Invoke(); return "Actual boss rules overlay opened."; }
            if (target == "cry") { ExpeditionRiftScream.Play(panels[1], panels[0]); return "Actual transient scream FX played; damage remains presentation-only in this fixture."; }
            if (target == "contribution" || target == "victory" || target == "refund")
            {
                var existing = GameObject.Find("GlobalBossOutcomeModal"); if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
                var rules = GameObject.Find("GlobalBossRulesModal"); if (rules != null) UnityEngine.Object.DestroyImmediate(rules);
                Set("_bossGameOver", new M3GameOverMsg { globalBoss = true, bossAttemptFinished = true, bossGlobalHp = target == "victory" ? 0 : 471503,
                    bossGlobalMaxHp = 550000, bossMyDamage = target == "refund" ? 0 : 5409, bossQualified = target != "refund", bossAttemptRefunded = target == "refund" });
                Call("ShowGlobalBossOutcome"); return "Actual boss outcome: " + target + "; no rewards or claims invoked.";
            }
            if (target == "verify")
            {
                Canvas.ForceUpdateCanvases(); var report = new Report { height = 1920 };
                Require(report, Screen.width == 1080 && Screen.height == 1920, "Native1080x1920 play viewport");
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                    if (text.gameObject.activeInHierarchy && !string.IsNullOrEmpty(text.text) && (text.transform.IsChildOf(panels[1].transform) || text.transform.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("GlobalBoss", StringComparison.Ordinal))))
                    { text.ForceMeshUpdate(); Require(report, !text.isTextOverflowing, "Readable battle/outcome boss label: " + text.name); }
                Require(report, panels[1].combatStatsName == null || !panels[1].combatStatsName.text.Contains("ЛЕЧ"), "Boss has no healing characteristic");
                Require(report, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null && !manager.enabled, "No account or gameplay transport");
                report.passed = true; Directory.CreateDirectory(".cache/qa/legacy-0.3.13/global-boss/play");
                File.WriteAllText(".cache/qa/legacy-0.3.13/global-boss/play/verify-" + (GameObject.Find("GlobalBossOutcomeModal") != null ? "outcome" : "battle") + ".json", JsonUtility.ToJson(report, true));
                return JsonUtility.ToJson(report);
            }
            throw new ArgumentException("Use play-battle/rules/cry/contribution/victory/refund/verify.");
        }
        private static void Save(Camera camera, RenderTexture texture, string path, int height)
        {
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture }); else camera.Render();
            RenderTexture.active = texture; var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
    }
}
