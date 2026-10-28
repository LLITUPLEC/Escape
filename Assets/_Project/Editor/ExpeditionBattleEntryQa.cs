using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
    /// <summary>Production UI and launch-state regression probes, using a disabled transport-free identity.</summary>
    public static class ExpeditionBattleEntryQa
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public int checkedMineReturns, revealWaits; public List<string> checks = new(); }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var originalScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = "MineScene";
            SceneManager.SetActiveScene(scene);
            var instance = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S);
            var previousBoot = instance.GetValue(null);
            var savedLaunch = typeof(Match3LaunchContext).GetFields(S).ToDictionary(field => field, field => field.GetValue(null));
            var report = new Report();
            GameObject bootRoot = null;
            NakamaConnectionConfig config = null;
            try
            {
                bootRoot = new GameObject("OfflineIdentityForBattleEntryQa"); bootRoot.SetActive(false);
                var boot = bootRoot.AddComponent<NakamaBootstrap>();
                config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; boot.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                var user = typeof(global::Nakama.Session).GetField("<UserId>k__BackingField", I);
                user.SetValue(session, "11111111-2222-3333-4444-555555555555");
                typeof(NakamaBootstrap).GetField("<Session>k__BackingField", I).SetValue(boot, session);
                instance.SetValue(null, boot);
                foreach (var difficulty in new[] { "easy", "medium", "hard" })
                for (var floor = 1; floor <= 12; floor++)
                {
                    Match3LaunchContext.SetSoloMine("mine_" + floor, floor, difficulty, true, "MineScene");
                    Match3LaunchContext.ConsumeMode();
                    Match3LaunchContext.ConsumeSoloMine(out _, out _, out _, out _);
                    Check(Match3LaunchContext.ConsumeSoloReturnScene() == "MineScene", "Mine scene survives launch consumption");
                    var background = new GameObject("QaMineBackground", typeof(RectTransform));
                    var view = ExpeditionMinePresentation.Install(background.transform, _ => { }, _ => { });
                    view.SetProgress(12, false);
                    view.SetProgress(12, true);
                    Check(view.SelectedFloor == floor, "Return floor " + floor + " / " + difficulty);
                    view.SetProgress(12, true);
                    Check(view.SelectedFloor == floor, "Catalog refresh keeps selection");
                    Check(!Match3LaunchContext.TryConsumeMineReturnFloor("MineScene", out _), "Return consumed exactly once");
                    UnityEngine.Object.DestroyImmediate(background);
                    report.checkedMineReturns++;
                }
                report.checks.Add("All 36 mine floor/difficulty returns survive consumed launch payload and repeated catalog refresh");
                Match3LaunchContext.SetSoloMine("mine_7", 7, "hard", true, "MineScene3D");
                Check(!Match3LaunchContext.TryConsumeMineReturnFloor("MineScene", out _), "Different scene cannot consume 3D return");
                Check(Match3LaunchContext.TryConsumeMineReturnFloor("MineScene3D", out var depth) && depth == 7, "3D return keeps floor");
                Match3LaunchContext.SetSoloMine("mine_7", 7, "hard", true, "MineScene");
                user.SetValue(session, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
                Check(!Match3LaunchContext.TryConsumeMineReturnFloor("MineScene", out _), "Another account cannot inherit mine return");
                report.checks.Add("3D destination and account ownership are respected");
                ValidateMonsterNames();
                report.checks.Add("All 12 mine names follow monstr.txt; legacy server defaults migrate and administrative names survive");

                var panel = new GameObject("QaSearchPanel", typeof(RectTransform)).AddComponent<Match3SearchingPanel>();
                panel.Show("Поиск соперника…");
                var unknown = panel.GetComponentsInChildren<Transform>(true).Single(t => t.name == "UnknownOpponent");
                Check(unknown.gameObject.activeSelf, "Unknown slot during search");
                var art = Resources.Load<Sprite>("Expedition/Art/hero"); Check(art != null, "Shipped portrait exists");
                panel.ShowReady(art, "Друг", true, "Через 2…");
                var opponent = unknown.parent.Find("Portrait").GetComponent<Image>();
                Check(!unknown.gameObject.activeSelf && opponent.sprite == art && opponent.enabled && opponent.color == Color.white, "Ready slot reveals portrait");
                var monster = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_5.asset");
                Check(monster != null && monster.Icon != null, "Shipped monster exists");
                panel.ShowReady(monster.Icon, monster.DisplayName, false, "Через 1…");
                Canvas.ForceUpdateCanvases();
                var fill = opponent.GetComponent<ExpeditionMatchPortraitFill>();
                Check(fill != null && fill.enabled && !opponent.GetComponent<AspectRatioFitter>().enabled, "Monster uses solid-bounds portrait fill");
                fill.Refresh();
                Check(ExpeditionCombatantGrounding.TryGet(monster.Icon, out var footprint), "Monster has baked solid bounds");
                var parent = opponent.transform.parent as RectTransform; var imageRect = opponent.rectTransform.rect; var solid = footprint.SolidBounds;
                Vector2 SolidPoint(float x, float y) => parent.InverseTransformPoint(opponent.rectTransform.TransformPoint(new Vector2(imageRect.xMin + imageRect.width * x, imageRect.yMin + imageRect.height * y)));
                var lower = SolidPoint(solid.xMin, solid.yMin); var upper = SolidPoint(solid.xMax, solid.yMax);
                Check(parent.rect.width > 0 && parent.rect.height > 0 && lower.x <= parent.rect.xMin + .1f && lower.y <= parent.rect.yMin + .1f && upper.x >= parent.rect.xMax - .1f && upper.y >= parent.rect.yMax - .1f, "Monster opaque bounds cover the entire masked matchup slot");
                panel.ShowReady(art, "Друг", true, "Через 1…");
                Check(!fill.enabled && opponent.GetComponent<AspectRatioFitter>().enabled && opponent.rectTransform.pivot.y == 1, "Human crop is restored after monster fill");
                report.checks.Add("Unknown opponent reveals actual named art; solid monster bounds cover the card and human crop restores afterwards");

                var managerRoot = new GameObject("QaDisabledBattleManager"); managerRoot.SetActive(false);
                var manager = managerRoot.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                Set(manager, "_matchRevealPending", true); Set(manager, "_isSoloBotMode", true); Set(manager, "_searchingPanel", panel);
                var queue = (Queue<M3BoardSyncMsg>)Get(manager, "_pendingBoardSyncs"); queue.Enqueue(new M3BoardSyncMsg());
                Call(manager, "TryStartNextBoardSync");
                Check(queue.Count == 1 && Get(manager, "_remoteSyncRoutine") == null, "Initial board and OP17 stay gated during reveal");
                queue.Clear();
                var sequence = (IEnumerator)Call(manager, "PresentConfirmedMatch");
                while (sequence.MoveNext())
                {
                    Check(sequence.Current is WaitForSecondsRealtime && (bool)Get(manager, "_matchRevealPending"), "Each reveal wait keeps input gated");
                    report.revealWaits++;
                }
                Check(report.revealWaits == 2 && !(bool)Get(manager, "_matchRevealPending") && !panel.gameObject.activeSelf, "Two countdown waits finish before battle overlay hides");
                report.checks.Add("Production reveal coroutine waits twice, blocks queued snapshots/turn-ready, then releases battle");
                ValidateTournamentReturns(manager, managerRoot.transform);
                report.checks.Add("All gold/ore/smith tournament rounds return to ArenaMenu after automatic advancement or the actual outcome Continue button; stale solo flags cannot redirect a tournament to the mine, and mine/global boss return destinations survive");
                report.passed = true;
                var path = ".cache/qa/battle-entry/battle-entry-qa.json";
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
                return JsonUtility.ToJson(report);
            }
            finally
            {
                instance.SetValue(null, previousBoot);
                foreach (var saved in savedLaunch) saved.Key.SetValue(null, saved.Value);
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(scene, true);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
            }
        }
        public static void RunBatch()
        {
            // A fresh batch editor starts with an unsaved untitled scene, which
            // Unity refuses to combine with an additive editor QA scene.
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }
        private static void ValidateTournamentReturns(DuelMatch3Manager manager, Transform parent)
        {
            var navigationField = typeof(DuelMatch3Manager).GetField("EditorNavigationOverride", S);
            var previousNavigation = navigationField.GetValue(null);
            var destinations = new List<string>();
            navigationField.SetValue(null, new Action<string>(destinations.Add));
            try
            {
                Set(manager, "_offlinePresentationQa", true);
                Set(manager, "_myStats", new PlayerStats());
                var outcomeRoot = new GameObject("QaTournamentOutcome", typeof(RectTransform)); outcomeRoot.transform.SetParent(parent, false);
                var outcome = outcomeRoot.AddComponent<Match3GameOverPanel>();
                outcome.titleText = new GameObject("Title", typeof(RectTransform)).AddComponent<TMPro.TextMeshProUGUI>();
                outcome.titleText.transform.SetParent(outcomeRoot.transform, false);
                outcome.backButton = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
                outcome.backButton.transform.SetParent(outcomeRoot.transform, false);
                outcome.OnBackClicked += () => Call(manager, "ReturnFromGameOver");
                Set(manager, "_gameOverPanel", outcome);
                foreach (var kind in new[] { "gold", "ore", "smith" })
                foreach (var round in new[] { "qf", "sf", "final" })
                foreach (var won in new[] { false, true })
                {
                    // An old solo flag must never override the authoritative tournament context.
                    Match3LaunchContext.SetSoloMine("mine_7", 7, "hard", false, "MineScene3D");
                    Match3LaunchContext.ClearArenaMenuAwaitBracketOverlay();
                    Match3LaunchContext.ArmArenaJoin("qa-completed-tournament", "Турнирный бот", true, "zz-bot-arena-qa");
                    Set(manager, "_isSoloBotMode", true); Set(manager, "_isArenaTournamentMatch", true);
                    Set(manager, "_gameEnded", false); Set(manager, "_pendingGameOver", true);
                    Set(manager, "_pendingGameOverWon", won); Set(manager, "_pendingGameOverDraw", false);
                    Set(manager, "_arenaGameOverRound", round); Set(manager, "_arenaGameOverKind", kind);
                    outcome.Hide(); var before = destinations.Count;
                    Call(manager, "TryShowDeferredGameOver");
                    var advance = won && round != "final";
                    Check((bool)Get(manager, "_gameEnded"), "Tournament outcome completes: " + kind + "/" + round);
                    if (advance)
                    {
                        Check(destinations.Count == before + 1 && !outcome.gameObject.activeSelf && Match3LaunchContext.ArenaMenuAwaitBracketOverlay,
                            "Intermediate victory automatically opens the bracket: " + kind + "/" + round);
                    }
                    else
                    {
                        Check(destinations.Count == before && outcome.gameObject.activeSelf && outcome.titleText.text == (won ? "Победа в турнире!" : "Вы выбыли из турнира"),
                            "Defeat/final victory keeps its actual outcome modal: " + kind + "/" + round);
                        outcome.backButton.onClick.Invoke();
                        Check(destinations.Count == before + 1, "Outcome Continue navigates exactly once");
                    }
                    Check(destinations[destinations.Count - 1] == "ArenaMenu", "Every tournament outcome returns to the arena");
                    Check(!Match3LaunchContext.TryPeekArenaJoin(out _, out _, out _, out _) && Match3LaunchContext.PeekSoloReturnScene() == "MineScene3D",
                        "Completed arena join is cleared without consuming mine return context");
                }
                Set(manager, "_isArenaTournamentMatch", false);
                foreach (var destination in new[] { "MineScene", "MineScene3D", "MainMenu" })
                {
                    Match3LaunchContext.SetSoloReturnContext(7, destination);
                    Call(manager, "ReturnFromGameOver");
                    Check(destinations[destinations.Count - 1] == destination, "Solo return destination preserved: " + destination);
                }
            }
            finally { navigationField.SetValue(null, previousNavigation); }
        }
        private static void ValidateMonsterNames()
        {
            var legacy = new[] { "Острый скол", "Ржавый гвоздь", "Соляной клещ", "Корунд", "Шлаковый червь", "Кремень", "Пыльный коготь", "Сталеплав", "Липкий ломун", "Тихий шорох", "Тихий шорох", "Жила" };
            var names = File.ReadAllLines("Docs/Design/monstr.txt").Select(line => line.Split('\t'))
                .Where(fields => fields.Length > 2 && int.TryParse(fields[0], out _)).ToDictionary(fields => int.Parse(fields[0]), fields => fields[1]);
            Check(names.Count == 12, "Monster source contains all 12 identities");
            for (var floor = 1; floor <= 12; floor++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_" + floor + ".asset");
                Check(definition != null && definition.DisplayName == names[floor] && MonsterNames.Default(floor) == names[floor], "Catalog/source identity " + floor);
                Check(MonsterNames.Resolve(legacy[floor - 1], definition, floor) == names[floor], "Legacy name migration " + floor);
                var custom = "Хранитель Северной штольни " + floor;
                Check(MonsterNames.Resolve(custom, definition, floor) == custom, "Administrative custom name " + floor);
            }
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, I).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, I).SetValue(target, value);
        private static object Call(object target, string name) => target.GetType().GetMethod(name, I).Invoke(target, null);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
