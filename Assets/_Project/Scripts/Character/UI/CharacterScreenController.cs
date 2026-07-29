using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Achievements;
using Project.Character;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Character.UI
{
    public sealed class CharacterScreenController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private CharacterScreenView view;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private CharacterSheetDragController dragController;

        [Header("Start")]
        [SerializeField] private bool startHidden = true;

        [Header("Item Modals (prefabs)")]
        [SerializeField] private CharacterItemActionModalView actionModalPrefab;
        [SerializeField] private CharacterItemInfoModalView infoModalPrefab;

        private CancellationTokenSource _cts;
        private bool _visible;
        private CharacterDragSlotHandle _selectedHandle;

        private GameObject _modalOverlay;
        private RectTransform _modalOverlayRect;
        private CharacterItemActionModalView _actionModal;
        private CharacterItemInfoModalView _infoModal;
        private int _modalOpenedFrame = -1000;
        private int _screenOpenedFrame = -1000;
        private Canvas _parentCanvas;
        private bool _prevOverrideSorting;
        private int _prevSortingOrder;
        private const int CharacterScreenSortingOrder = 32760;
        private Coroutine _hideOverlayRoutine;
        private CharacterGetRpcResponse _lastProfile;
        private GameObject _sellConfirmRoot;
        private RectTransform _sellConfirmPanel;
        private TMP_Text _sellConfirmText;
        private bool _sellInFlight;
        private CharacterNicknameEditor _nicknameEditor;

        private void Awake()
        {
            NakamaBootstrap.EnsureExists();
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (dragController == null) dragController = GetComponent<CharacterSheetDragController>();
            if (dragController == null) dragController = gameObject.AddComponent<CharacterSheetDragController>();
            dragController.SlotClicked += HandleSlotClicked;
            dragController.ProfileUpdated += HandleProfileUpdated;
            AchievementLifecycle.OnCombatStatsUpdated += HandleCombatStatsUpdated;
            _parentCanvas = GetComponentInParent<Canvas>(true);

            _nicknameEditor = GetComponent<CharacterNicknameEditor>() ?? gameObject.AddComponent<CharacterNicknameEditor>();
            _nicknameEditor.EnsureWired();

            _cts = new CancellationTokenSource();
            EnsureModalUi();
            if (startHidden) SetVisible(false);
        }

        private void Update()
        {
            if (!_visible) return;
            if (Time.frameCount <= _screenOpenedFrame) return;

            if (TryGetPressPositionThisFrame(out var pressPos))
            {
                if (_nicknameEditor != null && _nicknameEditor.IsEditing)
                    return;

                if (_modalOverlay != null && _modalOverlay.activeSelf)
                {
                    if (Time.frameCount <= _modalOpenedFrame) return;
                    var overAction = _actionModal != null && _actionModal.gameObject.activeSelf && _actionModal.ContainsScreenPoint(pressPos, null);
                    var overInfo = _infoModal != null && _infoModal.gameObject.activeSelf && _infoModal.ContainsScreenPoint(pressPos, null);
                    var overSellConfirm = IsSellConfirmVisible() && ContainsSellConfirmScreenPoint(pressPos);
                    if (!overAction && !overInfo && !overSellConfirm) HideModals();
                    return;
                }

                var panelRect = view != null ? view.GetPanelRootRectTransform() : null;
                var isInsidePanel = panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(panelRect, pressPos, null);
                if (!isInsidePanel) Close();
            }
        }

        private void OnDestroy()
        {
            if (dragController != null)
            {
                dragController.SlotClicked -= HandleSlotClicked;
                dragController.ProfileUpdated -= HandleProfileUpdated;
            }
            AchievementLifecycle.OnCombatStatsUpdated -= HandleCombatStatsUpdated;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void Toggle()
        {
            if (_visible) Close();
            else Open();
        }

        public void Open()
        {
            _ = OpenAsync();
        }

        public void Close()
        {
            if (_nicknameEditor != null) _nicknameEditor.CloseEditor();
            HideModals(true);
            SetVisible(false);
        }

        /// <summary>Например, когда открытие перенесено на BottomHeaderLogo в главном меню.</summary>
        public void HideOpenButton()
        {
            if (openButton != null) openButton.gameObject.SetActive(false);
        }

        private async Task OpenAsync()
        {
            SetVisible(true);
            if (view != null) view.EnsureBuilt();

            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            var profile = await CharacterProfileService.GetAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (!_visible) return;
                if (profile == null || !profile.ok)
                {
                    var err = profile == null ? "null" : (string.IsNullOrEmpty(profile.err) ? "unknown" : profile.err);
                    if (view != null) view.SetProfileLoadError(err);
                    return;
                }

                _lastProfile = profile;

                if (levelText != null && profile.progression != null)
                    levelText.text = profile.progression.level.ToString();

                if (view != null)
                {
                    if (dragController != null)
                    {
                        dragController.Initialize(view, itemCatalog, _cts);
                        view.SetupDrag(dragController);
                    }

                    view.ApplyCharacterResponse(profile, itemCatalog);
                }
            });

            if (_nicknameEditor != null)
                await _nicknameEditor.RefreshAsync(ct).ConfigureAwait(false);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) HideModals(true);
            ConfigureCanvasSortingForScreen(visible);
            if (view != null) view.SetVisible(visible);
            else gameObject.SetActive(visible);
            if (visible) _screenOpenedFrame = Time.frameCount;
        }

        private void HandleSlotClicked(CharacterDragSlotHandle handle)
        {
            if (!_visible || view == null || handle == null) return;
            if (!TryResolveItem(handle, out _)) return;

            _selectedHandle = handle;
            EnsureModalUi();
            ShowActionModalNear(handle.transform as RectTransform);
        }

        private void HandleProfileUpdated(CharacterGetRpcResponse response)
        {
            _lastProfile = response;
            if (view != null && response != null && response.ok)
                view.ApplyCharacterResponse(response, itemCatalog);
            if (_infoModal != null && _infoModal.gameObject.activeSelf) UpdateInfoModal();
        }

        private void HandleCombatStatsUpdated(StatsMap stats)
        {
            if (!_visible || view == null || stats == null) return;
            view.SetStats(stats.hp, stats.damage, stats.armor, stats.healing, stats.crit_chance);
            if (_lastProfile != null)
            {
                if (_lastProfile.stats == null) _lastProfile.stats = new StatsMap();
                _lastProfile.stats.hp = stats.hp;
                _lastProfile.stats.damage = stats.damage;
                _lastProfile.stats.armor = stats.armor;
                _lastProfile.stats.healing = stats.healing;
                _lastProfile.stats.crit_chance = stats.crit_chance;
            }
        }

        private bool TryResolveItem(CharacterDragSlotHandle handle, out ItemDefinition itemDef)
        {
            itemDef = null;
            if (view == null || handle == null) return false;
            if (handle.Kind == CharacterDragSlotKind.Inventory)
                return view.TryGetInventoryItem(handle.InventoryIndex, out _, out itemDef);
            return view.TryGetEquipmentItem(handle.EquipmentSlot, out _, out itemDef);
        }

        private void EnsureModalUi()
        {
            if (_modalOverlay != null) return;

            var parent = view != null ? view.transform as RectTransform : transform as RectTransform;
            if (parent == null) return;

            _modalOverlay = new GameObject("ItemModalOverlay", typeof(RectTransform), typeof(Image));
            _modalOverlayRect = _modalOverlay.GetComponent<RectTransform>();
            _modalOverlayRect.SetParent(parent, false);
            _modalOverlayRect.anchorMin = Vector2.zero;
            _modalOverlayRect.anchorMax = Vector2.one;
            _modalOverlayRect.offsetMin = Vector2.zero;
            _modalOverlayRect.offsetMax = Vector2.zero;
            var overlayImage = _modalOverlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.35f);
            overlayImage.raycastTarget = true;

            _actionModal = InstantiateOrBuildActionModal(_modalOverlayRect);
            _infoModal = InstantiateOrBuildInfoModal(_modalOverlayRect);

            _actionModal.Bind(
                onInfo: () =>
                {
                    if (_selectedHandle == null) return;
                    _actionModal.HideImmediate();
                    HideSellConfirm();
                    _infoModal.ShowCentered();
                    UpdateInfoModal();
                },
                onSell: OnSellPressed);

            _infoModal.Bind(
                onClose: () => HideModals(),
                onEquipToggle: OnEquipTogglePressed,
                onSalvage: () => { Debug.Log("[CharacterScreen] Разбор предмета пока не реализован."); },
                onLearnRecipe: OnLearnRecipePressed);

            EnsureSellConfirmUi();
            _modalOverlay.SetActive(false);
        }

        private void EnsureSellConfirmUi()
        {
            if (_sellConfirmRoot != null || _modalOverlayRect == null) return;

            _sellConfirmRoot = new GameObject("SellConfirm", typeof(RectTransform));
            var rootRt = _sellConfirmRoot.GetComponent<RectTransform>();
            rootRt.SetParent(_modalOverlayRect, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _sellConfirmPanel = panel.GetComponent<RectTransform>();
            _sellConfirmPanel.SetParent(rootRt, false);
            _sellConfirmPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _sellConfirmPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _sellConfirmPanel.pivot = new Vector2(0.5f, 0.5f);
            _sellConfirmPanel.sizeDelta = new Vector2(460f, 220f);
            panel.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 1f);
            panel.GetComponent<Image>().raycastTarget = true;

            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 18, 18);
            v.spacing = 14f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;

            var textGo = new GameObject("Q", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textGo.GetComponent<RectTransform>().SetParent(_sellConfirmPanel, false);
            textGo.GetComponent<LayoutElement>().minHeight = 90f;
            textGo.GetComponent<LayoutElement>().flexibleHeight = 1f;
            _sellConfirmText = textGo.GetComponent<TextMeshProUGUI>();
            _sellConfirmText.fontSize = 24;
            _sellConfirmText.alignment = TextAlignmentOptions.Center;
            _sellConfirmText.color = Color.white;
            _sellConfirmText.textWrappingMode = TextWrappingModes.Normal;
            _sellConfirmText.raycastTarget = false;

            var yn = new GameObject("YesNo", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            yn.GetComponent<RectTransform>().SetParent(_sellConfirmPanel, false);
            yn.GetComponent<LayoutElement>().preferredHeight = 54f;
            yn.GetComponent<LayoutElement>().minHeight = 54f;
            var h = yn.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 24f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandWidth = true;

            var yes = CreateLayoutButton(yn.transform, "Yes", "Да");
            yes.onClick.AddListener(() => _ = ConfirmSellAsync());
            var no = CreateLayoutButton(yn.transform, "No", "Нет");
            no.onClick.AddListener(() =>
            {
                HideSellConfirm();
                if (_selectedHandle != null && _actionModal != null)
                    ShowActionModalNear(_selectedHandle.transform as RectTransform);
            });

            _sellConfirmRoot.SetActive(false);
        }

        private bool IsSellConfirmVisible() =>
            _sellConfirmRoot != null && _sellConfirmRoot.activeSelf;

        private bool ContainsSellConfirmScreenPoint(Vector2 screenPoint) =>
            _sellConfirmPanel != null &&
            RectTransformUtility.RectangleContainsScreenPoint(_sellConfirmPanel, screenPoint, null);

        private void HideSellConfirm()
        {
            if (_sellConfirmRoot != null) _sellConfirmRoot.SetActive(false);
        }

        private void OnSellPressed()
        {
            if (_selectedHandle == null || view == null) return;
            if (!TryResolveItem(_selectedHandle, out var itemDef) || itemDef == null) return;

            var count = 1;
            if (_selectedHandle.Kind == CharacterDragSlotKind.Inventory)
                count = Mathf.Max(1, view.GetInventoryStackCount(_selectedHandle.InventoryIndex));

            var unit = Mathf.Max(0, itemDef.SalePrice);
            if (unit < 1)
            {
                Debug.LogWarning("[CharacterScreen] Предмет нельзя продать (sale_price=0).");
                return;
            }

            var total = (long)unit * count;
            var name = string.IsNullOrEmpty(itemDef.DisplayName) ? itemDef.ItemId : itemDef.DisplayName;

            EnsureSellConfirmUi();
            if (_actionModal != null) _actionModal.HideImmediate();
            if (_infoModal != null) _infoModal.HideImmediate();
            if (_sellConfirmText != null)
                _sellConfirmText.text = $"Точно продать {name} за {total} золотых?";
            if (_sellConfirmRoot != null)
            {
                _sellConfirmRoot.SetActive(true);
                _sellConfirmRoot.transform.SetAsLastSibling();
            }

            if (_modalOverlay != null) _modalOverlay.SetActive(true);
            _modalOpenedFrame = Time.frameCount;
        }

        private async Task ConfirmSellAsync()
        {
            if (_sellInFlight || _selectedHandle == null) return;
            _sellInFlight = true;
            var handle = _selectedHandle;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            CharacterGetRpcResponse resp = null;
            try
            {
                if (handle.Kind == CharacterDragSlotKind.Inventory)
                    resp = await CharacterProfileService.SellInventoryItemAsync(handle.InventoryIndex, ct).ConfigureAwait(false);
                else
                    resp = await CharacterProfileService.SellEquipmentItemAsync((int)handle.EquipmentSlot, ct).ConfigureAwait(false);
            }
            finally
            {
                _sellInFlight = false;
            }

            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (resp == null || !resp.ok)
                {
                    Debug.LogWarning("[CharacterScreen] Sell failed: " + (resp?.err ?? "null"));
                    HideSellConfirm();
                    return;
                }

                _lastProfile = resp;
                if (view != null && itemCatalog != null)
                    view.ApplyCharacterResponse(resp, itemCatalog);
                if (resp.progression != null)
                    PlayerResourcesService.PatchCachedFromProgression(resp.progression);
                HideModals(true);
            });
        }

        private CharacterItemActionModalView InstantiateOrBuildActionModal(Transform parent)
        {
            if (actionModalPrefab != null)
            {
                var inst = Instantiate(actionModalPrefab, parent, false);
                inst.name = "ItemActionModal";
                return inst;
            }

            var go = new GameObject("ItemActionModal", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(CharacterItemActionModalView));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(360f, 200f);
            go.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.98f);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            CreateLayoutButton(rt, "InfoButton", "Информация");
            CreateLayoutButton(rt, "SellButton", "Продать");
            return go.GetComponent<CharacterItemActionModalView>();
        }

        private CharacterItemInfoModalView InstantiateOrBuildInfoModal(Transform parent)
        {
            if (infoModalPrefab != null)
            {
                var inst = Instantiate(infoModalPrefab, parent, false);
                inst.name = "ItemInfoModal";
                return inst;
            }

            var go = new GameObject("ItemInfoModal", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(CharacterItemInfoModalView));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(640f, 520f);
            go.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.15f, 0.99f);

            CreateLabel(rt, "Title", "Предмет", 34, new Vector2(0.06f, 0.84f), new Vector2(0.84f, 0.96f), TextAlignmentOptions.Left);
            CreateLabel(rt, "Slot", "Слот", 24, new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.84f), TextAlignmentOptions.Left);
            var desc = CreateLabel(rt, "Desc", "", 22, new Vector2(0.06f, 0.58f), new Vector2(0.94f, 0.74f), TextAlignmentOptions.TopLeft);
            desc.color = new Color(0.78f, 0.80f, 0.88f, 1f);
            desc.richText = true;
            var stats = CreateLabel(rt, "Stats", "-", 26, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.56f), TextAlignmentOptions.TopLeft);
            stats.richText = true;
            CreateRectButton(rt, "CloseButton", "X", new Vector2(0.88f, 0.88f), new Vector2(0.97f, 0.97f));
            CreateRectButton(rt, "EquipButton", "Надеть", new Vector2(0.06f, 0.05f), new Vector2(0.34f, 0.16f));
            CreateRectButton(rt, "LearnRecipeButton", "Изучить рецепт", new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.16f));
            CreateRectButton(rt, "SalvageButton", "Разобрать", new Vector2(0.66f, 0.05f), new Vector2(0.94f, 0.16f));
            return go.GetComponent<CharacterItemInfoModalView>();
        }

        private void ShowActionModalNear(RectTransform source)
        {
            if (_modalOverlay == null || _actionModal == null || _infoModal == null || _modalOverlayRect == null) return;
            _modalOverlay.SetActive(true);
            HideSellConfirm();
            if (_hideOverlayRoutine != null)
            {
                StopCoroutine(_hideOverlayRoutine);
                _hideOverlayRoutine = null;
            }
            _infoModal.HideImmediate();
            var actionRect = _actionModal.PanelRect;
            if (actionRect == null) return;

            _modalOpenedFrame = Time.frameCount;
            if (source == null)
            {
                _actionModal.ShowAt(Vector2.zero);
                return;
            }

            var worldCenter = source.TransformPoint(source.rect.center);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_modalOverlayRect, screenPoint, null, out var local))
                _actionModal.ShowAt(local);
            else
                _actionModal.ShowAt(Vector2.zero);
        }

        private void UpdateInfoModal()
        {
            if (_selectedHandle == null || view == null || _infoModal == null) return;
            if (!TryResolveItem(_selectedHandle, out var selectedItem) || selectedItem == null)
            {
                HideModals();
                return;
            }

            _infoModal.SetTitle(selectedItem.DisplayName, selectedItem.ColorDisplayName);
            var isRecipe = selectedItem.Kind == ItemKind.Recipe;
            if (isRecipe)
                _infoModal.SetSlot("Рецепт → " + SlotName(selectedItem.RecipeTargetSlot));
            else
                _infoModal.SetSlot("Слот: " + (selectedItem.Equippable ? SlotName(selectedItem.Slot) : "Не экипируется"));
            _infoModal.SetDescription(selectedItem.Description);
            _infoModal.SetStats(BuildStatsDescription(selectedItem));
            var learned = _lastProfile?.learned_recipe_ids != null &&
                          Array.Exists(_lastProfile.learned_recipe_ids, x => x == selectedItem.ItemId);

            if (isRecipe && _selectedHandle.Kind == CharacterDragSlotKind.Inventory)
            {
                _infoModal.SetEquipButton(false, false, string.Empty);
                _infoModal.SetLearnRecipeButton(true, !learned, learned ? "Уже изучено" : "Изучить");
            }
            else
            {
                _infoModal.SetLearnRecipeButton(false, false, string.Empty);
                var canEquipAction = selectedItem.Equippable;
                if (canEquipAction)
                {
                    if (_selectedHandle.Kind == CharacterDragSlotKind.Inventory)
                    {
                        _infoModal.SetEquipButton(true, true, "Надеть");
                    }
                    else
                    {
                        var hasSpace = view.FindFirstEmptyInventoryIndex() >= 0;
                        _infoModal.SetEquipButton(true, hasSpace, hasSpace ? "Снять" : "Снять (нет места)");
                    }
                }
                else _infoModal.SetEquipButton(false, false, string.Empty);
            }
        }

        private void OnLearnRecipePressed()
        {
            _ = LearnRecipeFromModalAsync();
        }

        private async Task LearnRecipeFromModalAsync()
        {
            if (_selectedHandle == null || _selectedHandle.Kind != CharacterDragSlotKind.Inventory || view == null) return;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            var resp = await CharacterProfileService.LearnRecipeAsync(_selectedHandle.InventoryIndex, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (resp == null || !resp.ok || view == null || itemCatalog == null) return;
                _lastProfile = resp;
                view.ApplyCharacterResponse(resp, itemCatalog);
                HideModals(true);
            });
        }

        private void OnEquipTogglePressed()
        {
            if (_selectedHandle == null || dragController == null || view == null) return;
            if (!TryResolveItem(_selectedHandle, out var selectedItem) || selectedItem == null) return;

            if (selectedItem.Kind == ItemKind.Recipe && _selectedHandle.Kind == CharacterDragSlotKind.Inventory)
            {
                OnLearnRecipePressed();
                return;
            }

            if (!selectedItem.Equippable) return;

            if (_selectedHandle.Kind == CharacterDragSlotKind.Inventory)
            {
                dragController.TryEquipFromInventory(_selectedHandle.InventoryIndex);
                HideModals(true);
                return;
            }

            var freeInventoryIndex = view.FindFirstEmptyInventoryIndex();
            if (freeInventoryIndex < 0) return;
            dragController.TryUnequipToInventory(_selectedHandle.EquipmentSlot, freeInventoryIndex);
            HideModals(true);
        }

        private string BuildStatsDescription(ItemDefinition selectedItem)
        {
            if (selectedItem == null) return "-";

            var equipped = GetEquippedInSameSlot(selectedItem, _selectedHandle);
            var showDelta = _selectedHandle == null || _selectedHandle.Kind != CharacterDragSlotKind.Equipment;
            var order = new[] { StatId.Hp, StatId.Damage, StatId.Armor, StatId.Healing, StatId.CritChance };
            var lines = new System.Collections.Generic.List<string>(order.Length);
            foreach (var statId in order)
            {
                var selectedValue = SumStat(selectedItem, statId);
                var equippedValue = SumStat(equipped, statId);
                if (Mathf.Abs(selectedValue) < 0.0001f && Mathf.Abs(equippedValue) < 0.0001f) continue;

                var delta = selectedValue - equippedValue;
                lines.Add(FormatStatLine(statId, selectedValue, delta, showDelta));
            }

            return lines.Count == 0 ? "Нет характеристик" : string.Join("\n", lines);
        }

        private ItemDefinition GetEquippedInSameSlot(ItemDefinition selectedItem, CharacterDragSlotHandle selectedHandle)
        {
            if (view == null || selectedItem == null) return null;
            if (!selectedItem.Equippable) return null;

            if (!view.TryGetEquipmentItem(selectedItem.Slot, out _, out var equipped)) return null;
            if (selectedHandle != null && selectedHandle.Kind == CharacterDragSlotKind.Equipment && selectedHandle.EquipmentSlot == selectedItem.Slot)
                return selectedItem;
            return equipped;
        }

        private static float SumStat(ItemDefinition item, string statId)
        {
            if (item == null || string.IsNullOrEmpty(statId)) return 0f;
            return item.GetStatValue(statId);
        }

        private static string FormatStatLine(string statId, float value, float delta, bool showDelta)
        {
            var title = StatName(statId);
            if (statId == StatId.CritChance)
            {
                var v = Mathf.RoundToInt(value * 100f);
                var d = Mathf.RoundToInt(delta * 100f);
                if (!showDelta) return $"{title} {v}%";
                return $"{title} {v}%({ColoredSigned(d)}%)";
            }

            var iv = Mathf.RoundToInt(value);
            var id = Mathf.RoundToInt(delta);
            if (!showDelta) return $"{title} {iv}";
            return $"{title} {iv}({ColoredSigned(id)})";
        }

        private static string Signed(int value)
        {
            return value >= 0 ? "+" + value : value.ToString();
        }

        private static string ColoredSigned(int value)
        {
            var color = value > 0 ? "#7CFF7C" : value < 0 ? "#FF7B7B" : "#D8D8D8";
            return $"<color={color}>{Signed(value)}</color>";
        }

        private static string SlotName(EquipmentSlotId slot)
        {
            return slot switch
            {
                EquipmentSlotId.Helmet => "Шлем",
                EquipmentSlotId.Shoulders => "Плечи",
                EquipmentSlotId.Chest => "Тело",
                EquipmentSlotId.Gloves => "Перчатки",
                EquipmentSlotId.Legs => "Ноги",
                EquipmentSlotId.Feet => "Ступни",
                EquipmentSlotId.WeaponLeft => "Оружие (Л)",
                EquipmentSlotId.WeaponRight => "Оружие (П)",
                _ => slot.ToString(),
            };
        }

        private static string StatName(string statId)
        {
            return statId switch
            {
                StatId.Hp => "Здоровье",
                StatId.Damage => "Урон",
                StatId.Armor => "Броня",
                StatId.Healing => "Лечение",
                StatId.CritChance => "Шанс крита",
                _ => statId,
            };
        }

        private void HideModals(bool immediate = false)
        {
            _selectedHandle = null;
            if (_hideOverlayRoutine != null)
            {
                StopCoroutine(_hideOverlayRoutine);
                _hideOverlayRoutine = null;
            }

            if (immediate)
            {
                if (_actionModal != null) _actionModal.HideImmediate();
                if (_infoModal != null) _infoModal.HideImmediate();
                HideSellConfirm();
                if (_modalOverlay != null) _modalOverlay.SetActive(false);
                return;
            }

            HideSellConfirm();
            var hasAny = false;
            var maxDelay = 0f;
            if (_actionModal != null && _actionModal.gameObject.activeSelf)
            {
                _actionModal.HideAnimated();
                hasAny = true;
                maxDelay = Mathf.Max(maxDelay, _actionModal.HideDuration);
            }
            if (_infoModal != null && _infoModal.gameObject.activeSelf)
            {
                _infoModal.HideAnimated();
                hasAny = true;
                maxDelay = Mathf.Max(maxDelay, _infoModal.HideDuration);
            }

            if (!hasAny)
            {
                if (_modalOverlay != null) _modalOverlay.SetActive(false);
                return;
            }

            _hideOverlayRoutine = StartCoroutine(HideOverlayAfterDelay(Mathf.Max(0.01f, maxDelay)));
        }

        private static Button CreateLayoutButton(Transform parent, string name, string text)
        {
            var btn = CreateRectButton(parent, name, text, new Vector2(0f, 1f), new Vector2(1f, 1f));
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 54f;
            le.preferredHeight = 54f;
            return btn;
        }

        private static Button CreateRectButton(Transform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.25f, 0.25f, 0.34f, 1f);
            var btn = go.GetComponent<Button>();

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 24;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return btn;
        }

        private static TMP_Text CreateLabel(Transform parent, string name, string text, int size, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = alignment;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static bool TryGetPressPositionThisFrame(out Vector2 pointerPos)
        {
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var t = ts.primaryTouch;
                if (t.press.wasPressedThisFrame)
                {
                    pointerPos = t.position.ReadValue();
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                pointerPos = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                pointerPos = Input.mousePosition;
                return true;
            }
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                pointerPos = Input.GetTouch(0).position;
                return true;
            }
#endif
            pointerPos = default;
            return false;
        }

        private void ConfigureCanvasSortingForScreen(bool visible)
        {
            if (_parentCanvas == null) _parentCanvas = GetComponentInParent<Canvas>(true);
            if (_parentCanvas == null) return;

            if (visible)
            {
                _prevOverrideSorting = _parentCanvas.overrideSorting;
                _prevSortingOrder = _parentCanvas.sortingOrder;
                _parentCanvas.overrideSorting = true;
                _parentCanvas.sortingOrder = CharacterScreenSortingOrder;
                return;
            }

            _parentCanvas.overrideSorting = _prevOverrideSorting;
            _parentCanvas.sortingOrder = _prevSortingOrder;
        }

        private static Task RunOnMainThreadAsync(System.Action a)
        {
            // В проекте уже есть MainThreadDispatcher; используем мягкую зависимость.
            try
            {
                var t = Project.Utils.MainThreadDispatcher.RunAsync(() =>
                {
                    a?.Invoke();
                    return true;
                });
                return t;
            }
            catch
            {
                a?.Invoke();
                return Task.CompletedTask;
            }
        }

        private System.Collections.IEnumerator HideOverlayAfterDelay(float delay)
        {
            var elapsed = 0f;
            while (elapsed < delay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_modalOverlay != null) _modalOverlay.SetActive(false);
            _hideOverlayRoutine = null;
        }

    }
}

