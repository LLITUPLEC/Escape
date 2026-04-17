using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>
    /// Мастерская: 8 слотов как экип (подписи), справа — рецепты по слоту, снизу — требования и «Создать» / «Забрать».
    /// Сервер: duel_workshop_craft_start / duel_workshop_craft_claim, таймер по тиру (CFG).
    /// </summary>
    public sealed class WorkshopSceneController : MonoBehaviour
    {
        private static readonly string[] SlotRu =
        {
            "Шлем", "Плечи", "Тело", "Перчатки", "Ноги", "Ступни", "Оружие (Л)", "Оружие (П)",
        };

        [Header("Data")]
        [SerializeField] private ItemCatalog itemCatalog;

        [Header("Optional (авто-поиск по имени)")]
        [SerializeField] private Text hintText;
        [SerializeField] private Transform craftSlotsRoot;
        [SerializeField] private RectTransform workshopBackground;

        private CancellationTokenSource _cts;
        private CharacterGetRpcResponse _profile;

        private int _selectedSlot = -1;
        private string _selectedOutputDefId;

        private RectTransform _recipeContent;
        private Text _recipeHeader;
        private Text _detailText;
        private Button _createButton;
        private Button _claimButton;
        private readonly List<GameObject> _recipeRows = new();

        private Image[] _slotIcons;
        private Text[] _slotTimers;

        private void Awake()
        {
            ResolveRefs();
            BuildExtraUiIfNeeded();
            WireCraftSlots();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void Update()
        {
            if (_profile == null || !_profile.ok) return;
            UpdateSlotTimersAndIcons();
        }

        private void ResolveRefs()
        {
            if (workshopBackground == null)
            {
                var t = transform.Find("WorkshopBackground");
                workshopBackground = t as RectTransform;
            }

            if (craftSlotsRoot == null && workshopBackground != null)
                craftSlotsRoot = workshopBackground.Find("CraftSlots");

            if (hintText == null && workshopBackground != null)
            {
                var hintTr = workshopBackground.Find("Hint");
                if (hintTr != null) hintText = hintTr.GetComponent<Text>();
            }
        }

        private void BuildExtraUiIfNeeded()
        {
            if (workshopBackground == null || craftSlotsRoot == null) return;

            var slotsRt = craftSlotsRoot as RectTransform;
            if (slotsRt != null)
            {
                slotsRt.anchorMin = new Vector2(0.02f, 0.22f);
                slotsRt.anchorMax = new Vector2(0.48f, 0.88f);
                slotsRt.offsetMin = Vector2.zero;
                slotsRt.offsetMax = Vector2.zero;
            }

            if (hintText != null)
            {
                var hr = hintText.rectTransform;
                hr.anchorMin = new Vector2(0.02f, 0.02f);
                hr.anchorMax = new Vector2(0.98f, 0.18f);
                hr.offsetMin = Vector2.zero;
                hr.offsetMax = Vector2.zero;
            }

            if (workshopBackground.Find("WorkshopRecipePanel") != null)
                return;

            var panel = new GameObject("WorkshopRecipePanel", typeof(RectTransform), typeof(Image));
            var pr = panel.GetComponent<RectTransform>();
            pr.SetParent(workshopBackground, false);
            pr.anchorMin = new Vector2(0.5f, 0.22f);
            pr.anchorMax = new Vector2(0.98f, 0.88f);
            pr.offsetMin = Vector2.zero;
            pr.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.14f, 0.11f, 0.1f, 0.95f);

            _recipeHeader = CreateUiText("RecipeHeader", pr, "Выберите слот слева", 22, TextAnchor.UpperLeft,
                new Vector2(0f, 0.86f), new Vector2(1f, 1f), new Vector2(12f, -8f), new Vector2(-12f, -8f));

            var scrollGo = new GameObject("RecipeScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var sr = scrollGo.GetComponent<RectTransform>();
            sr.SetParent(pr, false);
            sr.anchorMin = new Vector2(0f, 0.28f);
            sr.anchorMax = new Vector2(1f, 0.84f);
            sr.offsetMin = new Vector2(8f, 4f);
            sr.offsetMax = new Vector2(-8f, -4f);
            scrollGo.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.08f, 1f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var vp = viewport.GetComponent<RectTransform>();
            vp.SetParent(sr, false);
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.offsetMin = Vector2.zero;
            vp.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _recipeContent = content.GetComponent<RectTransform>();
            _recipeContent.SetParent(vp, false);
            _recipeContent.anchorMin = new Vector2(0f, 1f);
            _recipeContent.anchorMax = new Vector2(1f, 1f);
            _recipeContent.pivot = new Vector2(0.5f, 1f);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = _recipeContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _detailText = CreateUiText("DetailText", pr, "", 18, TextAnchor.UpperLeft,
                new Vector2(0f, 0f), new Vector2(1f, 0.26f), new Vector2(12f, 8f), new Vector2(-12f, 8f));

            var btnRow = new GameObject("Buttons", typeof(RectTransform));
            var br = btnRow.GetComponent<RectTransform>();
            br.SetParent(pr, false);
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(1f, 0.12f);
            br.offsetMin = new Vector2(12f, 8f);
            br.offsetMax = new Vector2(-12f, 8f);

            _createButton = CreateUiButton(br, "CreateButton", "Создать", new Vector2(0f, 0f), new Vector2(0.48f, 1f), () => _ = OnCreateClicked());
            _claimButton = CreateUiButton(br, "ClaimButton", "Забрать в сундук", new Vector2(0.52f, 0f), new Vector2(1f, 1f), () => _ = OnClaimClicked());
            _claimButton.gameObject.SetActive(false);
        }

        private static Text CreateUiText(string name, Transform parent, string msg, int size, TextAnchor align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var tx = go.GetComponent<Text>();
            tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tx.text = msg;
            tx.fontSize = size;
            tx.color = new Color(0.94f, 0.92f, 0.86f);
            tx.alignment = align;
            tx.horizontalOverflow = HorizontalWrapMode.Wrap;
            tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            return tx;
        }

        private static Button CreateUiButton(RectTransform parent, string name, string label, Vector2 aMin, Vector2 aMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.28f, 0.22f, 0.2f, 1f);
            var b = go.GetComponent<Button>();
            b.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            CreateUiText("Label", go.transform, label, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return b;
        }

        private void WireCraftSlots()
        {
            if (craftSlotsRoot == null) return;
            _slotIcons = new Image[8];
            _slotTimers = new Text[8];

            for (var i = 0; i < 8; i++)
            {
                var slot = craftSlotsRoot.Find("Slot_" + i);
                if (slot == null) continue;

                var labelTr = slot.Find("SlotLabel");
                var label = labelTr != null ? labelTr.GetComponent<Text>() : null;
                if (label != null)
                {
                    label.fontSize = 16;
                    label.alignment = TextAnchor.UpperCenter;
                    label.text = SlotRu[i];
                }

                var iconTr = slot.Find("Icon");
                Image iconImg;
                if (iconTr == null)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    var irt = iconGo.GetComponent<RectTransform>();
                    irt.SetParent(slot, false);
                    irt.anchorMin = new Vector2(0.15f, 0.28f);
                    irt.anchorMax = new Vector2(0.85f, 0.72f);
                    irt.offsetMin = Vector2.zero;
                    irt.offsetMax = Vector2.zero;
                    iconImg = iconGo.GetComponent<Image>();
                    iconImg.color = new Color(1f, 1f, 1f, 0.15f);
                }
                else iconImg = iconTr.GetComponent<Image>();

                _slotIcons[i] = iconImg;

                var timerTr = slot.Find("Timer");
                Text timerTx;
                if (timerTr == null)
                {
                    var timerGo = new GameObject("Timer", typeof(RectTransform), typeof(Text));
                    var trt = timerGo.GetComponent<RectTransform>();
                    trt.SetParent(slot, false);
                    trt.anchorMin = new Vector2(0f, 0f);
                    trt.anchorMax = new Vector2(1f, 0.26f);
                    trt.offsetMin = Vector2.zero;
                    trt.offsetMax = Vector2.zero;
                    timerTx = timerGo.GetComponent<Text>();
                    timerTx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    timerTx.fontSize = 14;
                    timerTx.alignment = TextAnchor.MiddleCenter;
                    timerTx.color = new Color(0.9f, 0.85f, 0.6f);
                }
                else timerTx = timerTr.GetComponent<Text>();

                _slotTimers[i] = timerTx;

                var img = slot.GetComponent<Image>();
                var btn = slot.GetComponent<Button>();
                if (btn == null) btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var idx = i;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnSlotClicked(idx));
            }
        }

        private void Start()
        {
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var resp = await CharacterProfileService.GetAsync(ct).ConfigureAwait(true);
            _profile = resp;
            ApplyProfile(resp);
        }

        private void ApplyProfile(CharacterGetRpcResponse resp)
        {
            if (hintText != null && (resp == null || !resp.ok))
                hintText.text = "Профиль: " + (resp == null ? "null" : resp.err);

            if (resp != null && resp.ok)
            {
                var p = resp.progression;
                if (hintText != null && p != null)
                {
                    hintText.text =
                        $"Руда: {p.ore}   Золото: {p.gold}   Слитки: {CountDef(resp, WorkshopCraftRules.T1NormalIngotDefId)} (нужно {WorkshopCraftRules.T1NormalIngotCount})\n" +
                        "Крафт по таймеру; готовое — «Забрать в сундук». Золотые рецепты / IAP — позже.";
                }
            }

            UpdateSlotTimersAndIcons();
            if (_selectedSlot >= 0) RebuildRecipeList();
            UpdateDetailPanel();
        }

        private void OnSlotClicked(int slotIndex)
        {
            _selectedSlot = slotIndex;
            _selectedOutputDefId = null;
            if (_recipeHeader != null)
                _recipeHeader.text = "Слот: " + SlotRu[slotIndex];
            RebuildRecipeList();
            UpdateDetailPanel();
        }

        private enum WorkshopSlotState { Empty, Busy, Ready }

        private WorkshopSlotState WorkshopState(int slotIndex)
        {
            var wOut = GetWorkshopOut(slotIndex);
            var wEnd = GetWorkshopEnd(slotIndex);
            if (string.IsNullOrEmpty(wOut)) return WorkshopSlotState.Empty;
            if (wEnd > NowUnix()) return WorkshopSlotState.Busy;
            return WorkshopSlotState.Ready;
        }

        private async Task OnCreateClicked()
        {
            if (_profile == null || !_profile.ok || _selectedSlot < 0 || string.IsNullOrEmpty(_selectedOutputDefId))
                return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var resp = await CharacterProfileService.WorkshopCraftStartAsync(_selectedSlot, _selectedOutputDefId, ct).ConfigureAwait(true);
            if (!resp.ok)
            {
                if (hintText != null) hintText.text = "Ошибка: " + ErrToRu(resp.err);
                return;
            }
            _profile = resp;
            ApplyProfile(resp);
        }

        private async Task OnClaimClicked()
        {
            if (_profile == null || !_profile.ok || _selectedSlot < 0) return;
            await ClaimAtSlot(_selectedSlot);
        }

        private async Task ClaimAtSlot(int slotIndex)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var resp = await CharacterProfileService.WorkshopCraftClaimAsync(slotIndex, ct).ConfigureAwait(true);
            if (!resp.ok)
            {
                if (hintText != null) hintText.text = "Ошибка: " + ErrToRu(resp.err);
                return;
            }
            _profile = resp;
            ApplyProfile(resp);
        }

        private void RebuildRecipeList()
        {
            if (_recipeContent == null || itemCatalog == null || _profile == null || !_profile.ok || _selectedSlot < 0)
                return;

            foreach (var row in _recipeRows)
            {
                if (row != null) Destroy(row);
            }
            _recipeRows.Clear();

            foreach (var def in itemCatalog.EnumerateDefinitions())
            {
                if (def == null || def.Kind != ItemKind.Equipment) continue;
                if ((int)def.Slot != _selectedSlot) continue;
                if (string.IsNullOrEmpty(def.CraftRecipeId)) continue;
                if (!IsLearned(def.CraftRecipeId)) continue;
                if (def.Tier < 1 || def.Tier > 3 || def.Quality != ItemQualityTier.Normal) continue;

                var line = new GameObject("Recipe_" + def.ItemId, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = line.GetComponent<RectTransform>();
                rt.SetParent(_recipeContent, false);
                line.GetComponent<Image>().color = new Color(0.22f, 0.18f, 0.16f, 1f);
                var le = line.AddComponent<LayoutElement>();
                le.minHeight = 36f;
                var bt = line.GetComponent<Button>();
                var title = string.IsNullOrEmpty(def.DisplayName) ? def.ItemId : def.DisplayName;
                var lineTitle = title + " (T" + def.Tier + " «зелёный»)";
                CreateUiText("Txt", line.transform, lineTitle, 17, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(8f, 2f), new Vector2(-8f, -2f));
                var idCopy = def.ItemId;
                bt.onClick.AddListener(() =>
                {
                    _selectedOutputDefId = idCopy;
                    UpdateDetailPanel();
                });
                _recipeRows.Add(line);
            }

            if (_recipeRows.Count == 0 && _recipeHeader != null)
                _recipeHeader.text = SlotRu[_selectedSlot] + ": нет доступных рецептов для этого слота (изучите recipe_green и импорт каталога).";
        }

        private void UpdateDetailPanel()
        {
            if (_detailText == null) return;
            if (_profile == null || !_profile.ok || _selectedSlot < 0)
            {
                _detailText.text = "";
                if (_createButton != null) _createButton.interactable = false;
                if (_claimButton != null) _claimButton.gameObject.SetActive(false);
                return;
            }

            var st = WorkshopState(_selectedSlot);
            if (st == WorkshopSlotState.Ready)
            {
                var oid = GetWorkshopOut(_selectedSlot);
                var def = itemCatalog != null ? itemCatalog.Get(oid) : null;
                _detailText.text = "Готово: " + (def != null ? def.DisplayName : oid) + " — нажмите слот или «Забрать».";
                if (_createButton != null) _createButton.interactable = false;
                if (_claimButton != null)
                {
                    _claimButton.gameObject.SetActive(true);
                    _claimButton.interactable = true;
                }
                return;
            }

            if (st == WorkshopSlotState.Busy)
            {
                var oid = GetWorkshopOut(_selectedSlot);
                var def = itemCatalog != null ? itemCatalog.Get(oid) : null;
                _detailText.text = "Идёт крафт: " + (def != null ? def.ItemId : oid);
                if (_createButton != null) _createButton.interactable = false;
                if (_claimButton != null) _claimButton.gameObject.SetActive(false);
                return;
            }

            if (_claimButton != null) _claimButton.gameObject.SetActive(false);

            if (string.IsNullOrEmpty(_selectedOutputDefId))
            {
                _detailText.text = "Выберите рецепт в списке.";
                if (_createButton != null) _createButton.interactable = false;
                return;
            }

            var od = itemCatalog.Get(_selectedOutputDefId);
            var tier = od != null ? od.Tier : 1;
            WorkshopCraftRules.GetGreenNormalCost(tier, out var needOre, out var needGold, out var needIngotN, out var ingotId);

            var sb = new StringBuilder();
            sb.AppendLine($"Требования (зелёный T{tier}):");
            sb.AppendLine($"Руда ≥ {needOre}, золото ≥ {needGold}, {ingotId} × {needIngotN}");
            if (tier == 2)
                sb.AppendLine("Поглощение: легендарный предмет T1 в этом слоте (экип или сундук).");
            else if (tier == 3)
                sb.AppendLine("Поглощение: легендарный предмет T2 в этом слоте (экип или сундук).");

            sb.AppendLine($"Время: {FormatDuration(WorkshopCraftRules.CraftDurationSecondsForTier(tier))}");

            var ore = _profile.progression?.ore ?? 0;
            var gold = _profile.progression?.gold ?? 0;
            var ing = CountDef(_profile, ingotId);
            var fodderOk = tier == 1 ||
                           (tier == 2 && HasLegendFodder(_selectedSlot, 1)) ||
                           (tier == 3 && HasLegendFodder(_selectedSlot, 2));
            var okRes = fodderOk && ore >= needOre && gold >= needGold && ing >= needIngotN;
            sb.Append(okRes ? "\nУсловий достаточно." : "\nНе хватает ресурсов или поглощаемой легенды.");
            _detailText.text = sb.ToString();
            if (_createButton != null)
                _createButton.interactable = okRes && IsLearned(od != null ? od.CraftRecipeId : "");
        }

        private bool HasLegendFodder(int slotIndex, int legendTier)
        {
            if (_profile == null || itemCatalog == null) return false;
            if (_profile.equipment_def_ids != null && slotIndex >= 0 && slotIndex < _profile.equipment_def_ids.Length)
            {
                var id = _profile.equipment_def_ids[slotIndex];
                if (LegendFodderMatches(id, slotIndex, legendTier)) return true;
            }
            if (_profile.inventory_def_ids == null || _profile.inventory_counts == null) return false;
            var len = Math.Min(_profile.inventory_def_ids.Length, _profile.inventory_counts.Length);
            for (var i = 0; i < len; i++)
            {
                if (_profile.inventory_counts[i] < 1) continue;
                var id = _profile.inventory_def_ids[i];
                if (LegendFodderMatches(id, slotIndex, legendTier)) return true;
            }
            return false;
        }

        private bool LegendFodderMatches(string defId, int slotIndex, int legendTier)
        {
            if (string.IsNullOrEmpty(defId)) return false;
            var def = itemCatalog.Get(defId);
            if (def == null || def.Kind != ItemKind.Equipment) return false;
            if (def.Quality != ItemQualityTier.Legendary) return false;
            if (def.Tier != legendTier) return false;
            return (int)def.Slot == slotIndex;
        }

        private void UpdateSlotTimersAndIcons()
        {
            if (_slotTimers == null || _profile == null || !_profile.ok) return;

            for (var i = 0; i < 8; i++)
            {
                var wOut = GetWorkshopOut(i);
                var wEnd = GetWorkshopEnd(i);
                if (_slotIcons != null && _slotIcons[i] != null)
                {
                    if (!string.IsNullOrEmpty(wOut) && itemCatalog != null)
                    {
                        var ic = itemCatalog.GetDisplayIcon(itemCatalog.Get(wOut));
                        _slotIcons[i].sprite = ic;
                        _slotIcons[i].color = Color.white;
                    }
                    else
                    {
                        _slotIcons[i].sprite = null;
                        _slotIcons[i].color = new Color(1f, 1f, 1f, 0.12f);
                    }
                }

                if (_slotTimers[i] == null) continue;
                if (string.IsNullOrEmpty(wOut))
                    _slotTimers[i].text = "";
                else if (wEnd > NowUnix())
                    _slotTimers[i].text = FormatRemaining(wEnd - NowUnix());
                else
                    _slotTimers[i].text = "Готово!";
            }
        }

        private string GetWorkshopOut(int i)
        {
            if (_profile?.workshop_output_def_ids == null || i < 0 || i >= _profile.workshop_output_def_ids.Length)
                return "";
            return _profile.workshop_output_def_ids[i] ?? "";
        }

        private int GetWorkshopEnd(int i)
        {
            if (_profile?.workshop_ends_at == null || i < 0 || i >= _profile.workshop_ends_at.Length)
                return 0;
            return _profile.workshop_ends_at[i];
        }

        private bool IsLearned(string recipeId)
        {
            if (_profile?.learned_recipe_ids == null || string.IsNullOrEmpty(recipeId)) return false;
            for (var i = 0; i < _profile.learned_recipe_ids.Length; i++)
            {
                if (string.Equals(_profile.learned_recipe_ids[i], recipeId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static int CountDef(CharacterGetRpcResponse resp, string defId)
        {
            if (resp.inventory_def_ids == null || resp.inventory_counts == null) return 0;
            var n = 0;
            var len = Math.Min(resp.inventory_def_ids.Length, resp.inventory_counts.Length);
            for (var i = 0; i < len; i++)
            {
                if (resp.inventory_def_ids[i] == defId)
                    n += resp.inventory_counts[i];
            }
            return n;
        }

        private static int NowUnix() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static string FormatRemaining(int sec)
        {
            if (sec < 0) sec = 0;
            var m = sec / 60;
            var s = sec % 60;
            return $"{m}:{s:00}";
        }

        private static string FormatDuration(int sec)
        {
            if (sec < 60) return sec + " с";
            if (sec % 3600 == 0) return sec / 3600 + " ч";
            return sec / 60 + " мин";
        }

        private static string ErrToRu(string err)
        {
            if (string.IsNullOrEmpty(err)) return err;
            switch (err)
            {
                case "recipe_not_learned": return "рецепт не изучен";
                case "not_enough_ore": return "мало руды";
                case "not_enough_gold": return "мало золота";
                case "not_enough_ingots": return "мало слитков";
                case "inventory_full": return "сундук полон";
                case "not_craftable": return "предмет не крафтится";
                case "unknown_item": return "неизвестный предмет";
                case "unsupported_craft_tier": return "этот тир пока не поддержан";
                case "bad_output": return "не указан предмет";
                case "bad_slot_index": return "неверный слот";
                case "wrong_workshop_slot": return "предмет не для этого слота";
                case "workshop_busy": return "слот занят крафтом";
                case "claim_first": return "заберите готовое из слота";
                case "empty_workshop_slot": return "слот пуст";
                case "craft_not_ready": return "ещё не готово";
                case "missing_legend_fodder_t1": return "нужна легенда T1 в этом слоте";
                case "missing_legend_fodder_t2": return "нужна легенда T2 в этом слоте";
                case "unsupported_craft_quality": return "этот тип качества пока не крафтится в мастерской";
                case "session_stale": return "сессия устарела";
                case "session_epoch_required": return "нужен session_epoch";
                default: return err;
            }
        }
    }

    /// <summary>Согласовано с duel_match3_config (стоимость T1 и длительности по тиру).</summary>
    public static class WorkshopCraftRules
    {
        public const int T1NormalOreCost = 40;
        public const int T1NormalGoldCost = 20;
        public const int T1NormalIngotCount = 3;
        public const string T1NormalIngotDefId = "ingot_green";

        /// <summary>Синхронно с duel_match3_config WORKSHOP_T*_NORMAL_COST.</summary>
        public static void GetGreenNormalCost(int tier, out int ore, out int gold, out int ingotN, out string ingotId)
        {
            ingotId = T1NormalIngotDefId;
            switch (tier)
            {
                case 1:
                    ore = T1NormalOreCost;
                    gold = T1NormalGoldCost;
                    ingotN = T1NormalIngotCount;
                    return;
                case 2:
                    ore = 80;
                    gold = 40;
                    ingotN = 6;
                    return;
                case 3:
                    ore = 160;
                    gold = 80;
                    ingotN = 9;
                    return;
                default:
                    ore = T1NormalOreCost;
                    gold = T1NormalGoldCost;
                    ingotN = T1NormalIngotCount;
                    return;
            }
        }

        public static int CraftDurationSecondsForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 60 * 60;
                case 2: return 120 * 60;
                case 3: return 240 * 60;
                default: return 60 * 60;
            }
        }
    }
}
