using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Real recovery metadata and artwork, without network calls or account writes.</summary>
    public static class ExpeditionTournamentIdentityQa
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites; public string error; public List<string> checks = new(); }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var report = new Report();
            var bootField = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S); var oldBoot = bootField.GetValue(null);
            var launch = typeof(Match3LaunchContext).GetFields(S).Where(f => !f.IsLiteral && !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(null));
            var cache = (IDictionary)typeof(PublicPlayerAppearance).GetField("Cache", S).GetValue(null);
            var oldCache = new List<DictionaryEntry>(); foreach (DictionaryEntry entry in cache) oldCache.Add(entry);
            var scope = typeof(PublicPlayerAppearance).GetField("_scope", S); var oldScope = scope.GetValue(null);
            var appearanceHook = typeof(DuelMatch3Manager).GetField("EditorRecoveredAppearanceOverride", S); var oldHook = appearanceHook.GetValue(null);
            var root = new GameObject("InactiveTournamentIdentityQa"); root.SetActive(false);
            NakamaConnectionConfig config = null; var cts = new CancellationTokenSource(); DuelMatch3Manager manager = null;
            try
            {
                var boot = root.AddComponent<NakamaBootstrap>(); config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; boot.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                typeof(global::Nakama.Session).GetField("<UserId>k__BackingField", I).SetValue(session, "11111111-2222-3333-4444-555555555555");
                Set(boot, "<Session>k__BackingField", session); bootField.SetValue(null, boot);
                manager = root.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                Set(manager, "_board", new Match3BoardLogic());
                Set(manager, "_myStats", new PlayerStats()); Set(manager, "_opStats", new PlayerStats());
                var panelRoot = new GameObject("Opponent", typeof(RectTransform)); panelRoot.transform.SetParent(root.transform, false);
                var panel = panelRoot.AddComponent<Match3PlayerPanel>();
                panel.nameText = ExpeditionUi.Text(panelRoot.transform, "Name", "", 22, 0, 0, 1, 1);
                var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image)); portrait.transform.SetParent(panelRoot.transform, false);
                panel.avatarImage = portrait.GetComponent<Image>(); Set(manager, "_opPanel", panel);
                var monsters = AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/_Project/Data/Match3/Monsters/MainMonsterCatalog.asset");
                Set(manager, "presentationMonsterCatalog", monsters);
                var hero = Resources.Load<Sprite>("Expedition/Art/hero");
                Require(hero != null && monsters != null && monsters.GetByFloor(1)?.Icon != null, "Shipped hero and mine portrait sources exist", report);
                foreach (var kind in new[] { "gold", "ore", "smith" })
                foreach (var round in new[] { "qf", "sf", "final" })
                {
                    var name = "Турнирный горняк " + kind + "/" + round;
                    var info = Descriptor("qa-" + kind + "-" + round); info.arena_kind = kind; info.arena_round = round; info.opponent_name = name;
                    Match3LaunchContext.ClearArenaJoinArm();
                    Set(manager, "_remoteAppearance", new PublicPlayerAppearance.State { body_id = "phoenix-female" });
                    Apply(manager, info);
                    Require(!Get<bool>(manager, "_isSoloBotMode") && Get<bool>(manager, "_isArenaTournamentMatch") && panel.nameText.text == name && panel.avatarImage.sprite == hero,
                        kind + "/" + round + " restored bot keeps its bracket nickname and common hero body", report);
                    Require(Match3LaunchContext.ArmRecoveredMatch(info, LocalAccountCacheScope.Current, boot.AuthenticationGeneration)
                        && Match3LaunchContext.ConsumeMode() == Match3LaunchMode.Multiplayer, kind + "/" + round + " cold launch retains multiplayer mode", report);
                }
                var legacy = Descriptor("qa-legacy"); legacy.opponent_name = ""; legacy.bot_name = "Mine default";
                Match3LaunchContext.ArmArenaJoin(legacy.match_id, "Ник из турнирной сетки", true, "zz-bot-bracket");
                Apply(manager, legacy);
                Require(panel.nameText.text == "Ник из турнирной сетки" && panel.avatarImage.sprite == hero,
                    "Matching pending arena join preserves a legacy descriptor's bracket identity", report);
                Match3LaunchContext.ClearArenaJoinArm();
                legacy.opponent_name = "Авторитетный ник";
                Match3LaunchContext.ArmArenaJoin("different-match", "Чужой ник", true, "zz-bot-other"); Apply(manager, legacy);
                Require(panel.nameText.text == "Авторитетный ник", "Another match's pending arm cannot replace the recovered opponent", report);
                Match3LaunchContext.ClearArenaJoinArm();

                const string human = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
                var appearance = new PublicPlayerAppearance.State { ok = true, user_id = human, body_id = "phoenix-female", avatar_id = "phoenix-female" };
                var cacheType = typeof(PublicPlayerAppearance).GetNestedType("Cached", BindingFlags.NonPublic);
                var cached = Activator.CreateInstance(cacheType, true); Set(cached, "state", appearance); Set(cached, "expires", DateTime.UtcNow.AddMinutes(1));
                cache.Clear(); cache[human] = cached; scope.SetValue(null, LocalAccountCacheScope.Current);
                Set(manager, "_cts", cts);
                var player = Descriptor("qa-human"); player.mode = "pvp"; player.opponent_user_id = human; player.opponent_is_bot = false;
                player.bot_user_id = ""; player.opponent_name = "Игрок из сетки";
                Apply(manager, player);
                Require(appearance.Body() != null && panel.nameText.text == player.opponent_name && panel.avatarImage.sprite == appearance.Body(),
                    "Already-started human restore loads the actual public costume without replaying the reveal", report);
                var oldContext = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    var pendingAppearance = new TaskCompletionSource<PublicPlayerAppearance.State>();
                    appearanceHook.SetValue(null, new Func<string, CancellationToken, Task<PublicPlayerAppearance.State>>((_, token) => pendingAppearance.Task));
                    Set(manager, "_remoteAppearance", null); Apply(manager, player);
                    Require(panel.avatarImage.sprite == hero, "Uncached recovered human uses common art while appearance is pending", report);
                    var generation = Get<int>(manager, "_matchTransportGeneration");
                    Set(manager, "_offlinePresentationQa", true);
                    typeof(DuelMatch3Manager).GetMethod("StartGameWaitingServer", I).Invoke(manager, null);
                    Set(manager, "_offlinePresentationQa", false);
                    Require(Get<int>(manager, "_matchTransportGeneration") > generation, "Actual cold-restore checkpoint advances the transport generation", report);
                    pendingAppearance.SetResult(appearance);
                    Require(panel.avatarImage.sprite == appearance.Body(), "Delayed human costume survives the cold-restore network checkpoint", report);
                    pendingAppearance = new TaskCompletionSource<PublicPlayerAppearance.State>();
                    Apply(manager, player); Set(manager, "_opUserId", "different-opponent"); panel.avatarImage.sprite = hero;
                    pendingAppearance.SetResult(appearance);
                    Require(panel.avatarImage.sprite == hero, "A delayed response cannot replace another opponent's body", report);
                }
                finally { SynchronizationContext.SetSynchronizationContext(oldContext); appearanceHook.SetValue(null, oldHook); }
                Set(manager, "_cts", null);
                var race = Descriptor("qa-race"); race.arena_tournament_id = ""; race.pvp_race = true; race.opponent_name = "Горняк спуска";
                Apply(manager, race);
                Require(!Get<bool>(manager, "_isSoloBotMode") && Get<bool>(manager, "_pvpRaceQueue") && panel.avatarImage.sprite == hero && panel.nameText.text == race.opponent_name,
                    "Race bot restore retains Race mode and common opponent body", report);
                Require(Match3LaunchContext.ArmRecoveredMatch(race, LocalAccountCacheScope.Current, boot.AuthenticationGeneration)
                    && Match3LaunchContext.ConsumeMode() == Match3LaunchMode.Multiplayer && Match3LaunchContext.ConsumePvpRace(), "Cold Race bot launch retains the Race flag", report);
                var mine = Descriptor("qa-mine"); mine.arena_tournament_id = ""; mine.floor = 5; mine.bot_id = "mine_5"; mine.opponent_name = "Монстр";
                Apply(manager, mine);
                Require(Get<bool>(manager, "_isSoloBotMode") && panel.avatarImage.sprite == monsters.GetByFloor(5).Icon,
                    "Ordinary mine recovery keeps its floor-specific monster body", report);
                Require(Match3LaunchContext.ArmRecoveredMatch(mine, LocalAccountCacheScope.Current, boot.AuthenticationGeneration)
                    && Match3LaunchContext.ConsumeMode() == Match3LaunchMode.SoloBot, "Cold mine launch retains SoloBot mode", report);
                report.passed = true;
                return "Tournament identity recovery: " + report.checks.Count + " checks passed; no network or account writes.";
            }
            catch (Exception error) { report.error = error.ToString(); throw; }
            finally
            {
                if (manager != null) Set(manager, "_cts", null);
                appearanceHook.SetValue(null, oldHook);
                bootField.SetValue(null, oldBoot); foreach (var pair in launch) pair.Key.SetValue(null, pair.Value);
                cache.Clear(); foreach (var pair in oldCache) cache[pair.Key] = pair.Value; scope.SetValue(null, oldScope);
                UnityEngine.Object.DestroyImmediate(root); if (config != null) UnityEngine.Object.DestroyImmediate(config); cts.Dispose();
                Directory.CreateDirectory(".cache/qa/mode-recovery");
                File.WriteAllText(".cache/qa/mode-recovery/tournament-identity.json", JsonUtility.ToJson(report, true));
            }
        }
        private static Match3RecoveryReply Descriptor(string id) => new() { ok = true, active = true, started = true, battle_started = true, match_id = id,
            mode = "pve", arena_tournament_id = "qa-tournament", opponent_user_id = "zz-bot-bracket", bot_user_id = "zz-bot-bracket", opponent_is_bot = true,
            bot_id = "arena_bot", floor = 1 };
        private static void Apply(DuelMatch3Manager manager, Match3RecoveryReply info) => typeof(DuelMatch3Manager).GetMethod("ApplyRecoveredMetadata", I).Invoke(manager, new object[] { info });
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, I).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, I).GetValue(target);
        private static void Require(bool condition, string message, Report report) { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
