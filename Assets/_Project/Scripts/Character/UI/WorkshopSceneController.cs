using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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

        [Header("Layout")]
        [Tooltip("Если выключено — не трогаем якоря CraftSlots/Hint из кода (остаётся настройка сцены).")]
        [SerializeField] private bool applyDefaultWorkshopAnchors;
        [Tooltip("Слоты в раскладке как EquipmentRoot (колонны + центр + оружие снизу).")]
        [SerializeField] private bool applyEquipmentStyleCraftLayout = true;
        [Tooltip("Перезаписывать размер шрифта подписей слотов (иначе только текст SlotRu).")]
        [SerializeField] private bool overrideSlotLabelStyle = true;

        private CancellationTokenSource _cts;
        private CharacterGetRpcResponse _profile;

        private int _selectedSlot = -1;
        private string _selectedOutputDefId;

        private RectTransform _recipeContent;
        private Text _recipeHeader;
        private Text _itemStatsText;
        private Text _detailText;
        private Button _createButton;
        private Text _createButtonLabel;
        private Text _createButtonStatusIcon;
        private Button _claimButton;
        private Button _rushButton;
        private readonly List<GameObject> _recipeRows = new();

        private const float WorkshopSlotSquareSize = 200f;
        private const float WorkshopColumnWidth = 220f;
        private const float WorkshopWeaponsRowWidth = 440f;
        private const float WorkshopWeaponsRowHeight = 220f;
        private const float WorkshopCharacterPlaceholderSize = 400f;
        private static readonly Color WorkshopShortfallColor = new Color(204f / 255f, 0.2f, 0.2f, 1f);
        private static readonly Color WorkshopCanCraftColor = new Color(0.35f, 0.92f, 0.4f, 1f);
        private static readonly Color WorkshopCannotCraftColor = new Color(0.92f, 0.28f, 0.24f, 1f);

        private Image[] _slotIcons;
        private Text[] _slotTimers;
        private Outline[] _slotSelectionOutlines;

        private void Awake()
        {
            Project.Nakama.NakamaBootstrap.EnsureExists();
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

            EnsureEquipmentStyleCraftSlots();

            var slotsRt = craftSlotsRoot as RectTransform;
            if (applyDefaultWorkshopAnchors && slotsRt != null)
            {
                slotsRt.anchorMin = new Vector2(0.02f, 0.22f);
                slotsRt.anchorMax = new Vector2(0.48f, 0.88f);
                slotsRt.offsetMin = Vector2.zero;
                slotsRt.offsetMax = Vector2.zero;
            }

            if (applyDefaultWorkshopAnchors && hintText != null)
            {
                var hr = hintText.rectTransform;
                hr.anchorMin = new Vector2(0.02f, 0.02f);
                hr.anchorMax = new Vector2(0.98f, 0.18f);
                hr.offsetMin = Vector2.zero;
                hr.offsetMax = Vector2.zero;
            }

            WorkshopRecipePanelSetup.NormalizeRecipePanelLayout(workshopBackground);

            WorkshopRecipePanelSetup.Refs r;
            if (WorkshopRecipePanelSetup.TryBindExisting(workshopBackground, out r))
                ApplyWorkshopPanelRefs(r, workshopBackground);
            else
                ApplyWorkshopPanelRefs(WorkshopRecipePanelSetup.Build(workshopBackground), workshopBackground);

            WireRecipePanelButtons();
        }

        private void ApplyWorkshopPanelRefs(WorkshopRecipePanelSetup.Refs r, RectTransform workshopBackground)
        {
            _recipeHeader = r.recipeHeader;
            _itemStatsText = r.itemStatsText;
            _recipeContent = r.recipeContent;
            _detailText = r.detailText;
            _createButton = r.createButton;
            _claimButton = r.claimButton;
            _rushButton = r.rushButton;
            WorkshopRecipePanelSetup.EnsureCreateButtonStatusUi(_createButton, out _createButtonLabel, out _createButtonStatusIcon);
            if (_itemStatsText != null)
            {
                WorkshopRecipePanelSetup.ApplyItemStatsTextLayout(_itemStatsText.rectTransform);
                _itemStatsText.fontSize = 33;
            }
            if (_rushButton == null)
                _rushButton = WorkshopRecipePanelSetup.EnsureRushButton(workshopBackground);
        }

        private void WireRecipePanelButtons()
        {
            if (_createButton != null)
            {
                _createButton.onClick.RemoveAllListeners();
                _createButton.onClick.AddListener(() => _ = OnCreateClicked());
            }
            if (_claimButton != null)
            {
                _claimButton.onClick.RemoveAllListeners();
                _claimButton.onClick.AddListener(() => _ = OnClaimClicked());
            }
            if (_rushButton != null)
            {
                _rushButton.onClick.RemoveAllListeners();
                _rushButton.onClick.AddListener(() => _ = OnRushClicked());
            }
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

        private void EnsureEquipmentStyleCraftSlots()
        {
            if (craftSlotsRoot == null || !applyEquipmentStyleCraftLayout) return;

            var root = craftSlotsRoot as RectTransform;
            if (root == null) return;

            var grid = root.GetComponent<GridLayoutGroup>();
            if (grid != null)
                Destroy(grid);

            const string layoutName = "EquipmentWorkshopLayout";
            RectTransform layoutRt;
            var existing = root.Find(layoutName) as RectTransform;
            if (existing != null)
            {
                layoutRt = existing;
            }
            else
            {
                var layoutGo = new GameObject(layoutName, typeof(RectTransform));
                layoutRt = layoutGo.GetComponent<RectTransform>();
                layoutRt.SetParent(root, false);
                layoutRt.anchorMin = Vector2.zero;
                layoutRt.anchorMax = Vector2.one;
                layoutRt.offsetMin = new Vector2(6f, 6f);
                layoutRt.offsetMax = new Vector2(-6f, -6f);

                var center = new GameObject("CharacterPlaceholder", typeof(RectTransform), typeof(Image));
                center.GetComponent<RectTransform>().SetParent(layoutRt, false);
                center.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 0.55f);
                CreateUiText("Ph", center.transform, "?", 52, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                CreateWorkshopColumn(layoutRt, "LeftColumn");
                CreateWorkshopColumn(layoutRt, "RightColumn");
                CreateWorkshopWeaponsRow(layoutRt, "WeaponsRow");

                void MoveUnder(string slotName, Transform parent)
                {
                    var s = root.Find(slotName);
                    if (s == null || parent == null) return;
                    s.SetParent(parent, false);
                }

                var left = layoutRt.Find("LeftColumn");
                var right = layoutRt.Find("RightColumn");
                var bottom = layoutRt.Find("WeaponsRow");
                MoveUnder("Slot_0", left);
                MoveUnder("Slot_1", left);
                MoveUnder("Slot_2", left);
                MoveUnder("Slot_3", right);
                MoveUnder("Slot_4", right);
                MoveUnder("Slot_5", right);
                MoveUnder("Slot_6", bottom);
                MoveUnder("Slot_7", bottom);
            }

            ApplyEquipmentWorkshopLayoutSettings(layoutRt);
        }

        private void ApplyEquipmentWorkshopLayoutSettings(RectTransform layoutRt)
        {
            if (layoutRt == null) return;

            ApplyTopCenterRect(layoutRt.Find("CharacterPlaceholder") as RectTransform, WorkshopCharacterPlaceholderSize, WorkshopCharacterPlaceholderSize);

            ApplyStretchLeftRect(layoutRt.Find("LeftColumn") as RectTransform, WorkshopColumnWidth);
            ApplyStretchRightRect(layoutRt.Find("RightColumn") as RectTransform, WorkshopColumnWidth);
            ApplyBottomCenterRect(layoutRt.Find("WeaponsRow") as RectTransform, WorkshopWeaponsRowWidth, WorkshopWeaponsRowHeight);

            ConfigureWorkshopColumnLayout(layoutRt.Find("LeftColumn"));
            ConfigureWorkshopColumnLayout(layoutRt.Find("RightColumn"));
            ConfigureWorkshopWeaponsRowLayout(layoutRt.Find("WeaponsRow"));
        }

        private static void ApplyTopCenterRect(RectTransform rt, float width, float height)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = new Vector2(-width * 0.5f, -height);
            rt.offsetMax = new Vector2(width * 0.5f, 0f);
        }

        private static void ApplyStretchLeftRect(RectTransform rt, float width)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(width, 0f);
        }

        private static void ApplyStretchRightRect(RectTransform rt, float width)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = new Vector2(-width, 0f);
            rt.offsetMax = Vector2.zero;
        }

        private static void ApplyBottomCenterRect(RectTransform rt, float width, float height)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = new Vector2(-width * 0.5f, 0f);
            rt.offsetMax = new Vector2(width * 0.5f, height);
        }

        private static RectTransform CreateWorkshopColumn(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.AddComponent<VerticalLayoutGroup>();
            return rt;
        }

        private static RectTransform CreateWorkshopWeaponsRow(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.AddComponent<HorizontalLayoutGroup>();
            return rt;
        }

        private static void ConfigureWorkshopColumnLayout(Transform column)
        {
            if (column == null) return;
            var v = column.GetComponent<VerticalLayoutGroup>();
            if (v == null) v = column.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(0, 0, 0, 0);
            v.spacing = 0f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = true;
            v.childForceExpandWidth = true;
        }

        private static void ConfigureWorkshopWeaponsRowLayout(Transform row)
        {
            if (row == null) return;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            if (h == null) h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.spacing = 0f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = true;
        }

        private static void ApplyWorkshopSlotLayout(RectTransform slotRt)
        {
            if (slotRt == null) return;
            var le = slotRt.GetComponent<LayoutElement>();
            if (le == null) le = slotRt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = WorkshopSlotSquareSize;
            le.minHeight = le.preferredHeight = WorkshopSlotSquareSize;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;

            var aspect = slotRt.GetComponent<AspectRatioFitter>();
            if (aspect == null) aspect = slotRt.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            aspect.aspectRatio = 1f;
        }

        private static void ApplyWorkshopSlotIconLayout(RectTransform iconRt)
        {
            if (iconRt == null) return;
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.offsetMin = new Vector2(20f, 20f);
            iconRt.offsetMax = new Vector2(-20f, -20f);
        }

        private void WireCraftSlots()
        {
            if (craftSlotsRoot == null) return;
            _slotIcons = new Image[8];
            _slotTimers = new Text[8];
            _slotSelectionOutlines = new Outline[8];

            for (var i = 0; i < 8; i++)
            {
                var slot = craftSlotsRoot.Find("Slot_" + i);
                if (slot == null)
                    slot = FindSlotRecursive(craftSlotsRoot, "Slot_" + i);
                if (slot == null) continue;

                if (applyEquipmentStyleCraftLayout)
                    ApplyWorkshopSlotLayout(slot as RectTransform);

                var labelTr = slot.Find("SlotLabel");
                var label = labelTr != null ? labelTr.GetComponent<Text>() : null;
                if (label != null)
                {
                    label.text = SlotRu[i];
                    if (overrideSlotLabelStyle)
                    {
                        label.fontSize = 18;
                        label.alignment = TextAnchor.UpperCenter;
                    }
                }

                var iconTr = slot.Find("Icon");
                Image iconImg;
                if (iconTr == null)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    var irt = iconGo.GetComponent<RectTransform>();
                    irt.SetParent(slot, false);
                    iconImg = iconGo.GetComponent<Image>();
                    iconImg.color = new Color(1f, 1f, 1f, 0.15f);
                }
                else
                {
                    iconImg = iconTr.GetComponent<Image>();
                }

                if (applyEquipmentStyleCraftLayout)
                    ApplyWorkshopSlotIconLayout(iconTr != null ? iconTr as RectTransform : iconImg.rectTransform);

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
                    timerTx.fontSize = overrideSlotLabelStyle ? 15 : 14;
                    timerTx.alignment = TextAnchor.MiddleCenter;
                    timerTx.color = new Color(0.9f, 0.85f, 0.6f);
                }
                else
                {
                    timerTx = timerTr.GetComponent<Text>();
                    if (overrideSlotLabelStyle && timerTx.fontSize < 15)
                        timerTx.fontSize = 15;
                }

                _slotTimers[i] = timerTx;

                var img = slot.GetComponent<Image>();
                var btn = slot.GetComponent<Button>();
                if (btn == null) btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;

                var outline = slot.GetComponent<Outline>();
                if (outline == null)
                    outline = slot.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.98f, 0.82f, 0.22f, 1f);
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = true;
                outline.enabled = false;
                _slotSelectionOutlines[i] = outline;

                var idx = i;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnSlotClicked(idx));
            }
        }

        private static Transform FindSlotRecursive(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var f = FindSlotRecursive(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
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
                        $"Руда: {p.ore}   Золото: {p.gold}   Слитки: зел. {CountDef(resp, "ingot_green")}, син. {CountDef(resp, "ingot_blue")}, фиол. {CountDef(resp, "ingot_purple")}, тесс. {CountDef(resp, "tesseract")}\n" +
                        "Крафт по таймеру; готовое — «Забрать в сундук».";
                }
            }

            UpdateSlotTimersAndIcons();
            if (_selectedSlot >= 0) RebuildRecipeList();
            UpdateDetailPanel();
            UpdateSlotSelectionVisual();
            UpdateItemStatsVisibility();
        }

        private void OnSlotClicked(int slotIndex)
        {
            _selectedSlot = slotIndex;
            _selectedOutputDefId = null;
            if (_recipeHeader != null)
                _recipeHeader.text = "Слот: " + SlotRu[slotIndex];
            RebuildRecipeList();
            UpdateDetailPanel();
            UpdateSlotSelectionVisual();
            UpdateItemStatsVisibility();
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

        private async Task OnRushClicked()
        {
            if (_profile == null || !_profile.ok || _selectedSlot < 0) return;
            if (WorkshopState(_selectedSlot) != WorkshopSlotState.Busy) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var resp = await CharacterProfileService.WorkshopCraftRushAsync(_selectedSlot, ct).ConfigureAwait(true);
            if (!resp.ok)
            {
                if (hintText != null) hintText.text = "Ошибка: " + ErrToRu(resp.err);
                return;
            }
            _profile = resp;
            ApplyProfile(resp);
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
                if (!IsRecipeLearnedForCraft(def.CraftRecipeId)) continue;
                if (def.Tier < 1 || def.Tier > 3) continue;

                var line = new GameObject("Recipe_" + def.ItemId, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = line.GetComponent<RectTransform>();
                rt.SetParent(_recipeContent, false);
                line.GetComponent<Image>().color = RecipeRowQualityTint(def.Quality);
                var le = line.AddComponent<LayoutElement>();
                le.minHeight = 40f;
                var bt = line.GetComponent<Button>();
                var title = string.IsNullOrEmpty(def.DisplayName) ? def.ItemId : def.DisplayName;
                var lineTitle = title + " (T" + def.Tier + ", " + WorkshopCraftRules.QualityRu(def.Quality) + ")";
                CreateUiText("Txt", line.transform, lineTitle, 21, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 3f), new Vector2(-10f, -3f));
                var idCopy = def.ItemId;
                bt.onClick.AddListener(() =>
                {
                    _selectedOutputDefId = idCopy;
                    UpdateDetailPanel();
                });
                _recipeRows.Add(line);
            }

            if (_recipeRows.Count == 0 && _recipeHeader != null)
                _recipeHeader.text = SlotRu[_selectedSlot] + ": нет изученных рецептов для этого слота";

            UpdateItemStatsVisibility();
        }

        private bool SlotHasLearnedRecipes(int slotIndex)
        {
            if (itemCatalog == null || _profile == null || !_profile.ok || slotIndex < 0)
                return false;

            foreach (var def in itemCatalog.EnumerateDefinitions())
            {
                if (def == null || def.Kind != ItemKind.Equipment) continue;
                if ((int)def.Slot != slotIndex) continue;
                if (string.IsNullOrEmpty(def.CraftRecipeId)) continue;
                if (!IsRecipeLearnedForCraft(def.CraftRecipeId)) continue;
                if (def.Tier < 1 || def.Tier > 3) continue;
                return true;
            }

            return false;
        }

        private void UpdateItemStatsVisibility()
        {
            if (_itemStatsText == null) return;
            var show = _selectedSlot >= 0 && SlotHasLearnedRecipes(_selectedSlot);
            _itemStatsText.gameObject.SetActive(show);
        }

        private void UpdateSlotSelectionVisual()
        {
            if (_slotSelectionOutlines == null) return;
            for (var i = 0; i < _slotSelectionOutlines.Length; i++)
            {
                var outline = _slotSelectionOutlines[i];
                if (outline != null)
                    outline.enabled = i == _selectedSlot;
            }
        }

        private void UpdateDetailPanel()
        {
            if (_detailText == null) return;
            if (_profile == null || !_profile.ok || _selectedSlot < 0)
            {
                _detailText.text = "";
                UpdateItemStatsPreview(null);
                UpdateCreateButtonVisual(false, null, false, null);
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
                UpdateItemStatsPreview(def);
                UpdateCreateButtonVisual(false, null, false, null);
                if (_createButton != null) _createButton.interactable = false;
                if (_rushButton != null) _rushButton.gameObject.SetActive(false);
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
                var craftTitle = (def != null && !string.IsNullOrEmpty(def.DisplayName)) ? def.DisplayName : (def != null ? def.ItemId : oid);
                _detailText.text = "Идёт крафт: " + craftTitle;
                UpdateItemStatsPreview(def);
                UpdateCreateButtonVisual(false, null, false, null);
                if (_createButton != null) _createButton.interactable = false;
                if (_claimButton != null) _claimButton.gameObject.SetActive(false);
                if (_rushButton != null)
                {
                    _rushButton.gameObject.SetActive(true);
                    _rushButton.interactable = (_profile.progression?.gold ?? 0) >= 500;
                }
                return;
            }

            if (_claimButton != null) _claimButton.gameObject.SetActive(false);
            if (_rushButton != null) _rushButton.gameObject.SetActive(false);

            if (string.IsNullOrEmpty(_selectedOutputDefId))
            {
                _detailText.text = "Выберите рецепт в списке.";
                UpdateItemStatsPreview(null);
                UpdateCreateButtonVisual(false, null, false, null);
                if (_createButton != null) _createButton.interactable = false;
                return;
            }

            var od = itemCatalog.Get(_selectedOutputDefId);
            var tier = od != null ? od.Tier : 1;
            if (tier < 1) tier = 1;
            if (tier > 3) tier = 3;
            var quality = od != null ? od.Quality : ItemQualityTier.Normal;

            WorkshopCraftRules.GetCraftCost(od, out var needOre, out var needGold, out var needIngotN, out var ingotId, out var needTess);

            var sb = new StringBuilder();
            sb.AppendLine($"Требования ({WorkshopCraftRules.QualityRu(quality)}, T{tier}):");
            sb.AppendLine($"Руда ≥ {needOre}, золото ≥ {needGold}");
            if (needIngotN > 0 && !string.IsNullOrEmpty(ingotId))
                sb.AppendLine($"{ingotId} × {needIngotN}");
            if (needTess > 0)
                sb.AppendLine($"tesseract × {needTess}");

            AppendFodderLines(sb, od, _selectedSlot);

            sb.AppendLine($"Время: {FormatDuration(WorkshopCraftRules.CraftDurationSecondsForTier(tier))}");

            var ore = _profile.progression?.ore ?? 0;
            var gold = _profile.progression?.gold ?? 0;
            var ing = string.IsNullOrEmpty(ingotId) ? 0 : CountDef(_profile, ingotId);
            var tess = CountDef(_profile, "tesseract");
            var fodderOk = CraftFodderOk(od, _selectedSlot);
            var resOk = ore >= needOre && gold >= needGold && ing >= needIngotN && tess >= needTess;
            var okRes = fodderOk && resOk;
            sb.Append(okRes ? "\nУсловий достаточно." : "\nНе хватает ресурсов или поглощаемого предмета.");
            _detailText.text = sb.ToString();
            UpdateItemStatsPreview(od);
            var recipeLearned = IsRecipeLearnedForCraft(od != null ? od.CraftRecipeId : "");
            var canCraft = okRes && recipeLearned;
            UpdateCreateButtonVisual(canCraft, od, true, BuildCraftShortfallLines(od, ore, gold, ing, tess, needOre, needGold, needIngotN, ingotId, needTess, fodderOk, resOk));
            if (_createButton != null)
                _createButton.interactable = canCraft;
        }

        private void UpdateCreateButtonVisual(bool canCraft, ItemDefinition od, bool recipeSelected, string shortfallText)
        {
            if (_createButton == null) return;
            WorkshopRecipePanelSetup.EnsureCreateButtonStatusUi(_createButton, out _createButtonLabel, out _createButtonStatusIcon);

            var showStatus = recipeSelected && od != null;
            if (_createButtonStatusIcon != null)
            {
                _createButtonStatusIcon.gameObject.SetActive(showStatus);
                if (showStatus)
                {
                    _createButtonStatusIcon.text = canCraft ? "✓" : "✗";
                    _createButtonStatusIcon.color = canCraft ? WorkshopCanCraftColor : WorkshopCannotCraftColor;
                }
            }

            if (_createButtonLabel == null) return;
            if (!showStatus)
            {
                _createButtonLabel.text = "Создать";
                _createButtonLabel.color = new Color(0.94f, 0.92f, 0.86f);
                return;
            }

            if (canCraft)
            {
                _createButtonLabel.text = "Создать";
                _createButtonLabel.color = new Color(0.94f, 0.92f, 0.86f);
                return;
            }

            if (string.IsNullOrEmpty(shortfallText))
            {
                _createButtonLabel.text = "Создать";
                _createButtonLabel.color = new Color(0.94f, 0.92f, 0.86f);
                return;
            }

            var shortfallHex = ColorUtility.ToHtmlStringRGB(WorkshopShortfallColor);
            _createButtonLabel.supportRichText = true;
            _createButtonLabel.text = $"Создать\n<color=#{shortfallHex}>{shortfallText}</color>";
            _createButtonLabel.color = new Color(0.94f, 0.92f, 0.86f);
        }

        private string BuildCraftShortfallLines(
            ItemDefinition od,
            long ore, long gold, int ing, int tess,
            int needOre, int needGold, int needIngotN, string ingotId, int needTess,
            bool fodderOk, bool resOk)
        {
            if (od == null) return "";
            var parts = new List<string>();
            if (!resOk)
            {
                if (ore < needOre) parts.Add($"руда {ore}/{needOre}");
                if (gold < needGold) parts.Add($"золото {gold}/{needGold}");
                if (needIngotN > 0 && !string.IsNullOrEmpty(ingotId) && ing < needIngotN)
                    parts.Add($"{IngotShortRu(ingotId)} {ing}/{needIngotN}");
                if (needTess > 0 && tess < needTess)
                    parts.Add($"тесс. {tess}/{needTess}");
            }

            if (!fodderOk)
                parts.Add(FodderShortRu(od));

            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }

        private static string IngotShortRu(string ingotId) => ingotId switch
        {
            "ingot_green" => "слиток зел.",
            "ingot_blue" => "слиток син.",
            "ingot_purple" => "слиток фиол.",
            _ => ingotId
        };

        private string FodderShortRu(ItemDefinition od)
        {
            if (od == null) return "нет поглощаемого предмета";
            var t = od.Tier < 1 ? 1 : od.Tier > 3 ? 3 : od.Tier;
            return od.Quality switch
            {
                ItemQualityTier.Normal when t == 2 => "нужна легенда T1",
                ItemQualityTier.Normal when t == 3 => "нужна легенда T2",
                ItemQualityTier.Rare => $"нужен обычный T{t}",
                ItemQualityTier.Epic => $"нужен редкий T{t}",
                ItemQualityTier.Legendary => $"нужен эпик T{t}",
                _ => "нет поглощаемого предмета"
            };
        }

        private void UpdateItemStatsPreview(ItemDefinition def)
        {
            if (_itemStatsText == null) return;
            UpdateItemStatsVisibility();
            if (!_itemStatsText.gameObject.activeSelf) return;
            if (def == null)
            {
                _itemStatsText.text = "Выберите рецепт — здесь будут характеристики создаваемого предмета.";
                return;
            }

            _itemStatsText.text = BuildCraftStatsSummary(def);
        }

        private static string BuildCraftStatsSummary(ItemDefinition def)
        {
            if (def == null) return "";
            var order = new[] { StatId.Hp, StatId.Damage, StatId.Armor, StatId.Healing, StatId.CritChance };
            var lines = new List<string>();
            foreach (var sid in order)
            {
                var v = def.GetStatValue(sid);
                if (Mathf.Abs(v) < 0.0001f) continue;
                lines.Add(FormatCraftStatLine(sid, v));
            }

            var title = string.IsNullOrEmpty(def.DisplayName) ? def.ItemId : def.DisplayName;
            var head = $"{title}  ·  T{def.Tier}, {WorkshopCraftRules.QualityRu(def.Quality)}";
            if (lines.Count == 0)
                return head + "\n— в каталоге нет бонусов к статам (или они нулевые).";
            return head + "\n" + string.Join("\n", lines);
        }

        private static string FormatCraftStatLine(string statId, float value)
        {
            var title = StatNameRu(statId);
            if (statId == StatId.CritChance)
                return $"{title}: {Mathf.RoundToInt(value * 100f)}%";
            return $"{title}: {Mathf.RoundToInt(value)}";
        }

        private static string StatNameRu(string statId) => statId switch
        {
            StatId.Hp => "Здоровье",
            StatId.Damage => "Урон",
            StatId.Armor => "Броня",
            StatId.Healing => "Лечение",
            StatId.CritChance => "Шанс крита",
            _ => statId
        };

        private static Color RecipeRowQualityTint(ItemQualityTier q) => q switch
        {
            ItemQualityTier.Normal => new Color(0.14f, 0.22f, 0.16f, 0.92f),
            ItemQualityTier.Rare => new Color(0.13f, 0.17f, 0.26f, 0.92f),
            ItemQualityTier.Epic => new Color(0.22f, 0.15f, 0.26f, 0.92f),
            ItemQualityTier.Legendary => new Color(0.26f, 0.20f, 0.12f, 0.92f),
            _ => new Color(0.20f, 0.17f, 0.16f, 0.95f),
        };

        private static void AppendFodderLines(StringBuilder sb, ItemDefinition od, int slotIndex)
        {
            if (od == null || od.Kind != ItemKind.Equipment) return;
            var q = od.Quality;
            var t = od.Tier < 1 ? 1 : od.Tier > 3 ? 3 : od.Tier;
            if (q == ItemQualityTier.Normal)
            {
                if (t == 2)
                    sb.AppendLine("Поглощение: легендарный предмет T1 в этом слоте (экип или сундук).");
                else if (t == 3)
                    sb.AppendLine("Поглощение: легендарный предмет T2 в этом слоте (экип или сундук).");
                return;
            }

            if (q == ItemQualityTier.Rare)
                sb.AppendLine($"Поглощение: обычный (normal) предмет T{t} в этом слоте.");
            else if (q == ItemQualityTier.Epic)
                sb.AppendLine($"Поглощение: редкий (rare) предмет T{t} в этом слоте.");
            else if (q == ItemQualityTier.Legendary)
                sb.AppendLine($"Поглощение: эпический (epic) предмет T{t} в этом слоте.");
        }

        private bool CraftFodderOk(ItemDefinition od, int slotIndex)
        {
            if (od == null || od.Kind != ItemKind.Equipment) return false;
            var q = od.Quality;
            var t = od.Tier < 1 ? 1 : od.Tier > 3 ? 3 : od.Tier;
            if (q == ItemQualityTier.Normal)
            {
                if (t == 1) return true;
                if (t == 2) return HasLegendFodder(slotIndex, 1);
                if (t == 3) return HasLegendFodder(slotIndex, 2);
                return false;
            }

            if (q == ItemQualityTier.Rare) return HasQualityFodder(slotIndex, t, ItemQualityTier.Normal);
            if (q == ItemQualityTier.Epic) return HasQualityFodder(slotIndex, t, ItemQualityTier.Rare);
            if (q == ItemQualityTier.Legendary) return HasQualityFodder(slotIndex, t, ItemQualityTier.Epic);
            return false;
        }

        private bool HasQualityFodder(int slotIndex, int tier, ItemQualityTier needQuality)
        {
            if (_profile == null || itemCatalog == null) return false;
            if (_profile.equipment_def_ids != null && slotIndex >= 0 && slotIndex < _profile.equipment_def_ids.Length)
            {
                var id = _profile.equipment_def_ids[slotIndex];
                if (QualityFodderMatches(id, slotIndex, tier, needQuality)) return true;
            }

            if (_profile.inventory_def_ids == null || _profile.inventory_counts == null) return false;
            var len = Math.Min(_profile.inventory_def_ids.Length, _profile.inventory_counts.Length);
            for (var i = 0; i < len; i++)
            {
                if (_profile.inventory_counts[i] < 1) continue;
                var id = _profile.inventory_def_ids[i];
                if (QualityFodderMatches(id, slotIndex, tier, needQuality)) return true;
            }

            return false;
        }

        private bool QualityFodderMatches(string defId, int slotIndex, int tier, ItemQualityTier needQuality)
        {
            if (string.IsNullOrEmpty(defId)) return false;
            var def = itemCatalog.Get(defId);
            if (def == null || def.Kind != ItemKind.Equipment) return false;
            if (def.Quality != needQuality) return false;
            if (def.Tier != tier) return false;
            return (int)def.Slot == slotIndex;
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

        /// <summary>
        /// craft_recipe_id: recipe_drop_{цвет}_{Slot}, recipe_gold_{Slot}; миграция с recipe_drop_t* / recipe_gold_t*.
        /// </summary>
        private bool IsRecipeLearnedForCraft(string craftRecipeId)
        {
            if (string.IsNullOrEmpty(craftRecipeId)) return false;
            if (IsLearned(craftRecipeId)) return true;
            foreach (var alt in AlternateLearnedIdsForCraftRecipeId(craftRecipeId))
            {
                if (IsLearned(alt)) return true;
            }

            return false;
        }

        private static IEnumerable<string> AlternateLearnedIdsForCraftRecipeId(string craftRecipeId)
        {
            var mNew = Regex.Match(craftRecipeId, @"^recipe_drop_(green|blue|purple)_(.+)$", RegexOptions.IgnoreCase);
            if (mNew.Success)
            {
                for (var t = 1; t <= 3; t++)
                    yield return $"recipe_drop_t{t}_{mNew.Groups[1].Value}_{mNew.Groups[2].Value}";
            }

            var mOld = Regex.Match(craftRecipeId, @"^recipe_drop_t[123]_(green|blue|purple)_(.+)$", RegexOptions.IgnoreCase);
            if (mOld.Success)
                yield return $"recipe_drop_{mOld.Groups[1].Value}_{mOld.Groups[2].Value}";

            var gNew = Regex.Match(craftRecipeId, @"^recipe_gold_(.+)$", RegexOptions.IgnoreCase);
            if (gNew.Success)
            {
                for (var t = 1; t <= 3; t++)
                    yield return $"recipe_gold_t{t}_{gNew.Groups[1].Value}";
            }

            var gOld = Regex.Match(craftRecipeId, @"^recipe_gold_t[123]_(.+)$", RegexOptions.IgnoreCase);
            if (gOld.Success)
                yield return $"recipe_gold_{gOld.Groups[1].Value}";
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
                case "missing_normal_fodder": return "нужен обычный предмет этого тира в слоте";
                case "missing_rare_fodder": return "нужен редкий предмет этого тира в слоте";
                case "missing_epic_fodder": return "нужен эпический предмет этого тира в слоте";
                case "not_enough_tesseract": return "мало тессерактов";
                case "bad_craft_cost": return "некорректная стоимость крафта в каталоге";
                case "session_stale": return "сессия устарела";
                case "session_epoch_required": return "нужен session_epoch";
                case "craft_already_ready": return "уже готово — заберите предмет";
                case "not_enough_matter": return "мало материи";
                case "energy_full": return "энергия на максимуме";
                case "bad_mode": return "неверный режим покупки";
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

        public static string QualityRu(ItemQualityTier q)
        {
            return q switch
            {
                ItemQualityTier.Normal => "обычный",
                ItemQualityTier.Rare => "редкий",
                ItemQualityTier.Epic => "эпический",
                ItemQualityTier.Legendary => "легендарный",
                _ => "обычный"
            };
        }

        /// <summary>Синхронно с duel_match3.lua workshop_craft_cost_from_def (каталог craft_* или масштаб от зелёного normal).</summary>
        public static void GetCraftCost(ItemDefinition od, out int ore, out int gold, out int ingotN, out string ingotId, out int tessN)
        {
            tessN = 0;
            ingotId = T1NormalIngotDefId;
            ore = gold = ingotN = 0;
            if (od == null || od.Kind != ItemKind.Equipment)
            {
                GetGreenNormalCost(1, out ore, out gold, out ingotN, out ingotId);
                return;
            }

            var tier = od.Tier < 1 ? 1 : od.Tier > 3 ? 3 : od.Tier;
            var hasCatalog =
                od.CraftOre > 0 ||
                od.CraftGold > 0 ||
                od.CraftIngotN > 0 ||
                od.CraftTesseractN > 0 ||
                !string.IsNullOrEmpty(od.CraftIngotDef);

            if (hasCatalog)
            {
                ore = od.CraftOre;
                gold = od.CraftGold;
                ingotN = od.CraftIngotN;
                ingotId = od.CraftIngotDef ?? "";
                tessN = od.CraftTesseractN;
                return;
            }

            GetGreenNormalCost(tier, out ore, out gold, out ingotN, out ingotId);
            var qm = od.Quality switch
            {
                ItemQualityTier.Normal => 1.0,
                ItemQualityTier.Rare => 1.45,
                ItemQualityTier.Epic => 1.95,
                ItemQualityTier.Legendary => 2.6,
                _ => 1.0
            };
            ore = Mathf.RoundToInt(ore * (float)qm);
            gold = Mathf.RoundToInt(gold * (float)qm);
            if (od.Quality == ItemQualityTier.Legendary)
            {
                ingotN = 0;
                ingotId = "";
                tessN = 1;
            }
            else
            {
                ingotN = Mathf.Max(1, Mathf.RoundToInt(ingotN * (float)qm));
                if (od.Quality == ItemQualityTier.Rare) ingotId = "ingot_blue";
                else if (od.Quality == ItemQualityTier.Epic) ingotId = "ingot_purple";
                else ingotId = "ingot_green";
            }
        }
    }
}
