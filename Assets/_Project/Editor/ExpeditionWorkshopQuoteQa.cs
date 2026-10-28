using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Character.UI;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Actual workshop click -> service JSON -> delayed reply -> requirements gating. Only transport is replaced.</summary>
    public static class ExpeditionWorkshopQuoteQa
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        [Serializable] private sealed class Request { public int slot_index, expected_ingot_n, session_epoch; public string output_def_id; }
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites, preferenceWrites; public List<string> checks = new(); }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single); Debug.Log(Run());
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var report = new Report(); var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var bootField = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S); var previousBoot = bootField.GetValue(null);
            var hook = typeof(CharacterProfileService).GetField("EditorCraftStartTransportOverride", S); var previousHook = hook.GetValue(null);
            var epoch = typeof(NakamaBootstrap).GetField("_localSessionEpoch", S); var previousEpoch = epoch.GetValue(null);
            NakamaConnectionConfig config = null;
            try
            {
                var owner = new GameObject("DisabledQuoteIdentity"); owner.SetActive(false);
                var bootstrap = owner.AddComponent<NakamaBootstrap>(); config = ScriptableObject.CreateInstance<NakamaConnectionConfig>(); config.host = "offline.invalid"; bootstrap.Config = config;
                var session = (global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session));
                var user = typeof(global::Nakama.Session).GetField("<UserId>k__BackingField", I);
                user.SetValue(session, "11111111-2222-3333-4444-555555555555"); Set(bootstrap, "<Session>k__BackingField", session);
                bootField.SetValue(null, bootstrap); epoch.SetValue(null, 7);
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
                Check(catalog != null && bootstrap.Client == null, "Disabled identity provides a scope without initializing a network client", report);
                var canvas = new GameObject("QuoteContractCanvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var background = ExpeditionUi.Panel(canvas.transform, "WorkshopBackground", 0, 0, 1, 1).rectTransform;
                var hint = Text(background, "Hint"); var slots = ExpeditionUi.Rect(background, "CraftSlots", 0, 0, 1, 1);
                for (var index = 0; index < 8; index++)
                {
                    var slot = ExpeditionUi.Panel(slots, "Slot_" + index, 0, 0, 1, 1); slot.gameObject.AddComponent<Button>();
                    Text(slot.transform, "SlotLabel"); Text(slot.transform, "Timer"); ExpeditionUi.Fill(slot.transform, "Icon", 0, 0, 1, 1, Color.white);
                }
                var controllerRoot = new GameObject("DisabledQuoteWorkshop"); controllerRoot.SetActive(false);
                var controller = controllerRoot.AddComponent<WorkshopSceneController>(); controller.enabled = false;
                Set(controller, "itemCatalog", catalog); Set(controller, "hintText", hint); Set(controller, "workshopBackground", background); Set(controller, "craftSlotsRoot", slots);
                Set(controller, "applyDefaultWorkshopAnchors", false); Set(controller, "applyEquipmentStyleCraftLayout", false);
                Call(controller, "BuildExtraUiIfNeeded"); Call(controller, "WireCraftSlots");
                Set(controller, "_expeditionLayout", ExpeditionWorkshopLayout.Install(background, slots));
                var ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Equipment).Select(item => item.ItemId)
                    .Concat(new[] { "ingot_green", "ingot_blue", "ingot_purple", "tesseract" }).ToArray();
                var profile = new CharacterGetRpcResponse { ok = true, inventory_capacity = 50, progression = new Progression { level = 12, gold = 1000000, ore = 1000000 },
                    equipment_def_ids = new string[8], inventory_def_ids = ids, inventory_counts = ids.Select(id => id.StartsWith("ingot_", StringComparison.Ordinal) ? 1000 : 1).ToArray(),
                    learned_recipe_ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Recipe).Select(item => item.ItemId).ToArray(),
                    workshop_output_def_ids = new string[8], workshop_ends_at = new int[8] };
                var purple = Array.IndexOf(ids, "ingot_purple"); profile.inventory_counts[purple] = 590;
                Set(controller, "_profile", profile); Call(controller, "ApplyProfile", profile);
                var legs = catalog.Get("eq_t1_epic_legs"); var feet = catalog.Get("eq_t1_epic_feet");
                void Select(ItemDefinition item) { Set(controller, "_selectedSlot", (int)item.Slot); Set(controller, "_selectedOutputDefId", item.ItemId); Call(controller, "UpdateDetailPanel"); }
                int Shown(ItemDefinition item) { var values = new object[] { item, 0, 0, 0, null, 0 }; Call(controller, "GetDisplayedCraftCost", values); return (int)values[3]; }
                var requests = new List<Request>(); TaskCompletionSource<CharacterGetRpcResponse> pending = null;
                hook.SetValue(null, new Func<string, CancellationToken, Task<CharacterGetRpcResponse>>((json, token) =>
                { requests.Add(JsonUtility.FromJson<Request>(json)); pending = new TaskCompletionSource<CharacterGetRpcResponse>(); return pending.Task; }));
                Task Click() => (Task)Call(controller, "OnCreateClicked");
                void Complete(Task task, CharacterGetRpcResponse response)
                {
                    pending.SetResult(response); Check(task.IsCompleted, "Quote continuation settles synchronously on its owning Unity context", report); task.GetAwaiter().GetResult();
                }
                CharacterGetRpcResponse Changed(ItemDefinition item, int amount) => new() { ok = false, err = "craft_quote_changed", craft_quote = new WorkshopCraftQuote { schema = 1, output_def_id = item.ItemId, ingot_n = amount } };
                Select(legs); var button = Get<Button>(controller, "_createButton");
                Check(Shown(legs) == legs.CraftIngotN && button.interactable, "Initial displayed requirements and enough-material gating use the selected item price; profile=" +
                    (Get<CharacterGetRpcResponse>(controller, "_profile") != null) + ";shown=" + Shown(legs) + ";asset=" + legs.CraftIngotN + ";button=" + button.interactable + ";detail=" + Get<Text>(controller, "_detailText").text, report);
                var first = Click();
                Check(requests.Count == 1 && requests[0].slot_index == (int)legs.Slot && requests[0].output_def_id == legs.ItemId && requests[0].expected_ingot_n == Shown(legs) && requests[0].session_epoch == 7,
                    "Actual Create click sends exactly the displayed selected-item price and slot through production service JSON", report);
                Complete(first, Changed(legs, 600));
                Check(requests.Count == 1 && Shown(legs) == 600 && legs.CraftIngotN == 560 && !button.interactable && Get<Text>(controller, "_detailText").text.Contains("× 600") && hint.text.Contains("Создать"),
                    "Changed quote updates visible requirements and insufficient-material gating, preserves the asset and requires another explicit click", report);
                profile.inventory_counts[purple] = 620; Call(controller, "UpdateDetailPanel"); Check(button.interactable, "Updated price enables Create only after enough material exists", report);
                var second = Click(); Check(requests.Count == 2 && requests[1].expected_ingot_n == 600, "A second explicit click sends the revised visible price without an automatic retry", report);
                Complete(second, new CharacterGetRpcResponse { ok = false, err = "not_enough_ore" });

                profile.inventory_counts[purple] = 1000;
                Select(legs); var late = Click(); Select(feet); var beforeFeet = Shown(feet); var beforeHint = hint.text;
                Complete(late, Changed(legs, 900));
                Check(Shown(feet) == beforeFeet && Shown(legs) == 600 && hint.text == beforeHint && !Get<bool>(controller, "_craftRequestInFlight") && button.interactable,
                    "Late recipe-A quote cannot rewrite selected recipe B, replace its hint, or leave current controls locked", report);

                Select(feet); var superseded = Click(); beforeHint = hint.text;
                Set(controller, "_craftQuoteGeneration", Get<int>(controller, "_craftQuoteGeneration") + 1);
                Complete(superseded, Changed(feet, 999));
                Check(Shown(feet) == feet.CraftIngotN && hint.text == beforeHint && !Get<bool>(controller, "_craftRequestInFlight"),
                    "A superseded generation cannot publish quote data even for the same account and recipe; its own lock still releases", report);

                Select(feet); var wrongAccount = Click(); beforeHint = hint.text;
                user.SetValue(session, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"); Call(controller, "UpdateDetailPanel");
                Complete(wrongAccount, Changed(feet, 999));
                Check(Shown(feet) == feet.CraftIngotN && Shown(legs) == legs.CraftIngotN && hint.text == beforeHint && !Get<bool>(controller, "_craftRequestInFlight"),
                    "Account/generation switch discards late quote data and clears only its own in-flight lock", report);
                Select(feet); var lateException = Click(); beforeHint = hint.text;
                user.SetValue(session, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff"); Call(controller, "UpdateDetailPanel");
                pending.SetException(new InvalidOperationException("offline simulated transport loss"));
                Check(lateException.IsCompleted, "Delayed transport exception settles on its owner context", report); lateException.GetAwaiter().GetResult();
                Check(hint.text == beforeHint && !Get<bool>(controller, "_craftRequestInFlight"), "Late old-account transport errors cannot overwrite the new account hint or leave it locked", report);
                var calls = requests.Count;
                var invalid = CharacterProfileService.WorkshopCraftStartAsync((int)legs.Slot, legs.ItemId, -1, CancellationToken.None);
                Check(invalid.IsCompleted && invalid.Result.err == "invalid_craft_quote" && requests.Count == calls, "Client rejects an invalid displayed price before its transport boundary", report);
                Check(bootstrap.Client == null && !owner.activeInHierarchy && !controllerRoot.activeInHierarchy, "All quote checks ran without network, account writes or enabled gameplay controllers", report);
                report.passed = true;
            }
            finally
            {
                hook.SetValue(null, previousHook); bootField.SetValue(null, previousBoot); epoch.SetValue(null, previousEpoch);
                EditorSceneManager.CloseScene(scene, true); if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
                Directory.CreateDirectory(".cache/qa/workshop-quote"); File.WriteAllText(".cache/qa/workshop-quote/native-quote-contract.json", JsonUtility.ToJson(report, true));
            }
            return "PASS workshop quote: " + report.checks.Count + " actual click/service/requirements/context checks, no network/account/preference actions.";
        }
        private static Text Text(Transform parent, string name)
        { var text = ExpeditionUi.Rect(parent, name, 0, 0, 1, 1).gameObject.AddComponent<Text>(); text.font = ExpeditionTheme.LegacyFont; text.fontSize = 21; text.raycastTarget = false; return text; }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, I).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, I).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, I).Invoke(target, args);
        private static void Check(bool value, string message, Report report) { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
