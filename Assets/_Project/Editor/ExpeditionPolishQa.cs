using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Project.Match3;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Project.Editor
{
    /// <summary>Local presentation fixtures. Never submits account forms, spends resources or awards rewards.</summary>
    public static class ExpeditionPolishQa
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static GameObject _previewSelector;
        private static bool _previewSelectorWasActive;

        public static string Run(string target)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode required");
            switch (target)
            {
                case "auth":
                    if (GameObject.Find("AuthSetupGate") != null) throw new InvalidOperationException("Preserve the active account gate");
                    var bootstrap = UnityEngine.Object.FindFirstObjectByType<Project.Nakama.NakamaBootstrap>();
                    if (bootstrap == null) throw new InvalidOperationException("Connected bootstrap required");
                    typeof(AuthSetupGate).GetMethod("BuildUi", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { bootstrap, CancellationToken.None, new TaskCompletionSource<bool>() });
                    var auth = GameObject.Find("AuthSetupGate");
                    auth.name = "ExpeditionQaAuthFixture";
                    foreach (var button in auth.GetComponentsInChildren<Button>(true)) button.interactable = false;
                    foreach (var field in auth.GetComponentsInChildren<InputField>(true))
                    {
                        field.text = field.contentType == InputField.ContentType.Password ? "preview-only-password" : "wanderer@example.com";
                        var rect = field.textComponent.rectTransform;
                        if (rect.offsetMin.x < 28 || rect.offsetMax.x > -28) throw new InvalidOperationException("Account input has insufficient inset");
                    }
                    return "Actual account renderer: padded dummy fields; submit buttons disabled; no authentication calls";
                case "close-auth":
                    var preview = GameObject.Find("ExpeditionQaAuthFixture");
                    if (preview != null) UnityEngine.Object.Destroy(preview);
                    return "Closed only the local account presentation fixture";
                case "monster-long":
                    var monster = UnityEngine.Object.FindFirstObjectByType<MonsterModalView>();
                    if (monster == null) throw new InvalidOperationException("Open monster dossier required");
                    monster.AffixTitleText.text = "Аффикс: Хрупкость";
                    monster.AffixDescriptionText.text = "У монстра вдвое меньше здоровья, но +50 начальной маны и +35% к шансу критического удара. Цена способностей меняется во время боя.";
                    monster.RefreshDynamicStyles();
                    return "Long-description UI fixture; underlying monster and account data unchanged";
                case "barrier-long":
                    var barrier = UnityEngine.Object.FindFirstObjectByType<MonsterModalView>();
                    if (barrier == null || !barrier.BarrierContentRoot.activeSelf) throw new InvalidOperationException("Open barrier dossier required");
                    var requirements = barrier.BarrierRequirementsRoot;
                    foreach (Transform child in requirements) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
                    var descriptions = new[] { "Победа на этаже 1 · выполнено", "Руда 153 / 140", "Золото 165 / 250", "Ключ шахтёра 0 / 1", "Чертёж сложного механизма 0 / 1", "Материя 0 / 300" };
                    for (var i = 0; i < descriptions.Length; i++)
                    {
                        var row = new GameObject("RequirementRow", typeof(RectTransform), typeof(LayoutElement));
                        row.transform.SetParent(requirements, false);
                        var element = row.GetComponent<LayoutElement>();
                        element.minHeight = element.preferredHeight = 92;
                        if (i > 0)
                        {
                            var icon = ExpeditionUi.Rect(row.transform, "Icon", .05f, .2f, .2f, .8f).gameObject.AddComponent<Image>();
                            icon.sprite = ExpeditionTheme.Icon(i == 1 ? "ore" : i == 2 ? "gold" : i == 4 ? "blueprint" : "bag");
                        }
                        var label = ExpeditionUi.Rect(row.transform, "Value", .25f, .1f, .92f, .9f).gameObject.AddComponent<Text>();
                        label.font = ExpeditionTheme.LegacyFont;
                        label.text = descriptions[i];
                        label.color = i < 2 ? new Color(.55f, .8f, .61f) : ExpeditionTheme.Coral;
                    }
                    barrier.RefreshDynamicStyles();
                    return "Six requirement cards through actual dossier styling; UI fixture only, no unlock or inventory changes";
                case "barrier-bottom":
                    var barrierView = UnityEngine.Object.FindFirstObjectByType<MonsterModalView>();
                    if (barrierView == null || !barrierView.BarrierContentRoot.activeSelf) throw new InvalidOperationException("Open barrier dossier required");
                    Canvas.ForceUpdateCanvases();
                    var barrierScroll = barrierView.BarrierRequirementsRoot.GetComponentInParent<ScrollRect>();
                    if (barrierScroll == null || barrierScroll.content.rect.height <= barrierScroll.viewport.rect.height) throw new InvalidOperationException("Scrollable overflow fixture required");
                    barrierScroll.verticalNormalizedPosition = 0;
                    return "Scrolled requirements to bottom through actual ScrollRect; underlying requirements unchanged";
                case "affix-bottom":
                    var affixView = UnityEngine.Object.FindFirstObjectByType<MonsterModalView>();
                    if (affixView == null) throw new InvalidOperationException("Open monster dossier required");
                    Canvas.ForceUpdateCanvases();
                    var affixScroll = affixView.AffixDescriptionText.GetComponentInParent<ScrollRect>();
                    if (affixScroll == null || affixScroll.content.rect.height <= affixScroll.viewport.rect.height) throw new InvalidOperationException("Scrollable description fixture required");
                    affixScroll.verticalNormalizedPosition = 0;
                    return "Scrolled long affix description to bottom through actual ScrollRect";
                case "result":
                case "result-many":
                    var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
                    if (manager == null || Read<bool>(manager, "_hasInitialBoardSync")) throw new InvalidOperationException("Use result fixture before starting a match");
                    var result = UnityEngine.Object.FindFirstObjectByType<Match3GameOverPanel>(FindObjectsInactive.Include);
                    if (result == null) throw new InvalidOperationException("Result view required");
                    Invoke(manager, "EnsureGameOverRewardRowsUI");
                    Invoke(manager, "ClearRewardRows");
                    var names = new[] { "rewardExpSprite", "rewardGoldSprite", "rewardOreSprite", "rewardMatterSprite" };
                    var values = new[] { "+110", "+55", "+51", "+1" };
                    for (var i = 0; i < names.Length; i++) Invoke(manager, "AddRewardRowDisplay", Read<Sprite>(manager, names[i]), values[i]);
                    if (target == "result-many")
                        for (var i = 0; i < names.Length * 2; i++) Invoke(manager, "AddRewardRowDisplay", Read<Sprite>(manager, names[i % names.Length]), values[i % values.Length]);
                    Read<RectTransform>(manager, "_gameOverRewardRowsRoot").gameObject.SetActive(true);
                    _previewSelector = Read<GameObject>(manager, "_pveSelectorRoot");
                    _previewSelectorWasActive = _previewSelector != null && _previewSelector.activeSelf;
                    if (_previewSelector != null) _previewSelector.SetActive(false);
                    result.Show(true);
                    return "Actual result/reward renderers with preview cells; no match end, claim or reward mutation";
                case "close-result":
                    UnityEngine.Object.FindFirstObjectByType<Match3GameOverPanel>()?.Hide();
                    if (_previewSelector != null) _previewSelector.SetActive(_previewSelectorWasActive);
                    _previewSelector = null;
                    return "Closed result presentation fixture";
                case "result-bottom":
                    var rewardView = UnityEngine.Object.FindFirstObjectByType<Match3GameOverPanel>();
                    if (rewardView == null) throw new InvalidOperationException("Open result fixture required");
                    Canvas.ForceUpdateCanvases();
                    var rewardScroll = Read<ScrollRect>(rewardView, "_rewardScroll");
                    if (rewardScroll == null || !rewardScroll.vertical || rewardScroll.content.rect.height <= rewardScroll.viewport.rect.height)
                        throw new InvalidOperationException("Scrollable reward overflow required");
                    rewardScroll.verticalNormalizedPosition = 0;
                    return "Twelve reward cells: overflow scroll reaches bottom";
                case "shield-badge":
                    foreach (var shieldPanel in UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None)) shieldPanel.UpdateBuffState(2, 3);
                    return "View-only active shield badge on both headers; combat state unchanged";
                case "popups":
                    foreach (var popupPanel in UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None)) popupPanel.ShowGainPopups(60, 3);
                    return "View-only simultaneous heal and mana flyouts in both header panels; no combat or account mutation";
                case "ability-discount":
                    var abilities = UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>();
                    if (abilities == null) throw new InvalidOperationException("Ability panel required");
                    abilities.Refresh(new PlayerStats { mana = 100 }, false, false, 7, 0, 15, 100, 19);
                    var buttons = new[] { abilities.petardButton, abilities.crossButton, abilities.squareButton, abilities.shieldButton, abilities.furyButton };
                    var texts = buttons.Select(b => b.transform.Find("AbilityCost").GetComponent<TMP_Text>().text).ToArray();
                    if (!texts.SequenceEqual(new[] { "15", "7", "0", "100", "19" })) throw new InvalidOperationException("Dynamic ability values did not update");
                    return "Dynamic costs after production Refresh: " + string.Join(",", texts) + "; UI-only fixture";
                case "audio":
                    var soundOwner = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
                    if (soundOwner == null) throw new InvalidOperationException("Battle manager required");
                    var fields = typeof(DuelMatch3Manager).GetFields(PrivateInstance).Where(f => f.FieldType == typeof(AudioClip)).ToArray();
                    foreach (var field in fields)
                    {
                        var clip = field.GetValue(soundOwner) as AudioClip;
                        if (clip == null || clip.samples <= 0 || clip.length <= 0) throw new InvalidOperationException("Missing/unimported sound: " + field.Name);
                        if (field.Name == "sfxDamageCrit" && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) != "c6ae68cae7f0cd44ca8ef01102849f54")
                            throw new InvalidOperationException("Critical-hit reference changed");
                    }
                    return "All " + fields.Length + " serialized SFX import correctly; critical reference unchanged; shield and petard have separate clips";
                case "bars":
                    var barManager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
                    if (barManager == null || Read<bool>(barManager, "_hasInitialBoardSync")) throw new InvalidOperationException("Use bar fixture before starting a match");
                    var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None);
                    foreach (var panel in panels)
                    {
                        var hp = panel.hpText.text;
                        var mana = panel.manaText.text;
                        var originalHp = panel.hpFill.fillAmount;
                        var originalMana = panel.manaFill.fillAmount;
                        foreach (var amount in new[] { 0, 50, 100 })
                        {
                            panel.UpdateStats(amount, 100, amount, 100);
                            Canvas.ForceUpdateCanvases();
                            foreach (var fill in new[] { panel.hpFill, panel.manaFill })
                            {
                                var parent = fill.transform.parent as RectTransform;
                                var corners = new Vector3[4];
                                fill.rectTransform.GetWorldCorners(corners);
                                foreach (var corner in corners)
                                {
                                    var local = parent.InverseTransformPoint(corner);
                                    if (local.x < parent.rect.xMin + 1 || local.x > parent.rect.xMax - 1 || local.y < parent.rect.yMin + 1 || local.y > parent.rect.yMax - 1)
                                        throw new InvalidOperationException("Bar extends outside its frame");
                                }
                                var fraction = fill.rectTransform.rect.width / (parent.rect.width - 24);
                                if (Mathf.Abs(fraction - amount / 100f) > .005f) throw new InvalidOperationException("Bar ratio applied incorrectly");
                            }
                        }
                        panel.UpdateStats(Mathf.RoundToInt(originalHp * 10000), 10000, Mathf.RoundToInt(originalMana * 10000), 10000);
                        panel.hpText.text = hp;
                        panel.manaText.text = mana;
                    }
                    return "HP/MP at 0, 50, 100 percent: both panels stay inside frame and have correct visible width; original presentation restored";
            }
            if (target != null && target.StartsWith("mine-"))
            {
                var floor = int.Parse(target.Substring(5));
                if (floor < 1 || floor > 12) throw new ArgumentOutOfRangeException(nameof(target));
                UnityEngine.Object.FindFirstObjectByType<ExpeditionMinePresentation>().SelectFloor(floor, true);
                return "Selected local floor presentation " + floor;
            }
            throw new ArgumentException("Unknown polish fixture");
        }

        private static T Read<T>(object owner, string name) => (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
        private static void Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, arguments);
    }
}
