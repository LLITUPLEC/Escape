using System.Collections.Generic;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>The character page uses the shared shell; the original slots remain the live drag targets.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterExpeditionLayout : MonoBehaviour
    {
        private static readonly string[] SlotIcons = { "slot-helmet", "slot-shoulders", "slot-chest", "slot-gloves", "slot-legs", "slot-feet", "slot-hand-left", "slot-hand-right" };
        private static readonly string[] StatNames = { "Здоровье", "Урон", "Броня", "Лечение", "Крит. шанс" };
        private static readonly string[] StatIcons = { "stat-health", "stat-damage", "stat-armor", "stat-healing", "stat-critical" };
        private readonly List<RectTransform> _slots = new List<RectTransform>();
        private readonly List<int> _slotIds = new List<int>();
        private readonly List<Image> _slotBackgrounds = new List<Image>();
        private readonly List<Image> _slotWashes = new List<Image>();
        private readonly List<Image> _slotGhosts = new List<Image>();
        private readonly List<Image> _slotRims = new List<Image>();
        private readonly List<Image> _slotItemIcons = new List<Image>();
        private readonly List<Image> _setRims = new List<Image>();
        private RectTransform _setBadge;
        private TMP_Text _setBadgeText;
        private string _activeSetKey = string.Empty;
        private float _setPulseStarted;
        private bool _setPulseRunning;
        public bool EquipmentSetActive => !string.IsNullOrEmpty(_activeSetKey);
        public int SetActivationCount { get; private set; }
        private readonly List<RectTransform> _bagSlots = new List<RectTransform>();
        private readonly List<int> _bagDataIndices = new List<int>();
        private readonly List<Image> _bagRarity = new List<Image>();
        private readonly List<TMP_Text> _bagTiers = new List<TMP_Text>();
        private readonly List<Button> _bagLocks = new List<Button>();
        private readonly List<TMP_Text> _bagLockLabels = new List<TMP_Text>();
        private readonly Button[] _pages = new Button[2];
        private readonly TMP_Text[] _pageLabels = new TMP_Text[2];
        private TMP_Text _bagFooter;
        private int _page;
        public int CurrentInventoryPage => _page;
        public void SelectInventoryPage(int page)
        {
            page = Mathf.Clamp(page, 0, 1);
            if (_page == page) return;
            if (_dragController != null) _dragController.CancelActiveDrag();
            CancelInventoryGesture(); _page = page; _resetBagScroll = true; RefreshInventory();
        }
        private readonly List<int> _visibleBagSlots = new List<int>();
        private readonly Button[] _filters = new Button[4];
        private readonly TMP_Text[] _filterLabels = new TMP_Text[4];
        private ConsumableStatusStrip _consumables;
        public void SetConsumables(ConsumableStatus status){if(_consumables!=null)_consumables.Bind(status);}
        private CharacterScreenView _view;
        private CharacterSheetDragController _dragController;
        private RectTransform _screenRoot, _panel, _equipment, _stageBackdrop, _statsBar, _bag, _bagViewport, _bagContent, _bagScrollbar;
        private ScrollRect _bagScroll;
        private TMP_Text _emptyBag;
        private TMP_FontAsset _font;
        private Sprite _slotFrame;
        private bool _built, _applying, _geometryDirty, _resetBagScroll, _inShell;
        private int _filter;
        private Vector2 _lastRootSize;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;
        private CharacterAppearancePanel _appearancePanel;
        private readonly Button[] _heroTabs = new Button[2];
        public int CurrentHeroTab { get; private set; }

        public void SelectHeroTab(int tab)
        {
            if (!_built) return;
            tab = Mathf.Clamp(tab, 0, 1);
            if (_dragController != null) _dragController.CancelActiveDrag();
            CancelInventoryGesture();
            CurrentHeroTab = tab;
            _equipment.gameObject.SetActive(tab == 0);
            _statsBar.gameObject.SetActive(tab == 0);
            _bag.gameObject.SetActive(tab == 0);
            if (tab == 0) _appearancePanel.Hide(); else _appearancePanel.Show();
            for (var i = 0; i < _heroTabs.Length; i++) ExpeditionUi.StyleButton(_heroTabs[i], i == tab, 27);
            RefreshLayout();
        }

        public void Build(RectTransform panel, RectTransform equipment, RectTransform bag,
            IReadOnlyList<EquipmentSlotView> equipmentSlots, IReadOnlyList<ItemSlotView> inventorySlots,
            TMP_Text[] values, TMP_Text level, Button close, TMP_Text nickname, Button editNickname, TMP_Text profileError)
        {
            if (_built || panel == null || equipment == null || bag == null) return;
            _view = GetComponent<CharacterScreenView>();
            _dragController = GetComponent<CharacterSheetDragController>();
            _screenRoot = transform as RectTransform;
            _panel = panel; _equipment = equipment; _bag = bag;
            _font = ExpeditionTheme.Font;
            _slotFrame = Resources.Load<Sprite>("Expedition/Art/ui-slot-frame");
            if (_slotFrame == null) _slotFrame = ExpeditionTheme.Plate;
            var ghostMaterial = Resources.Load<Material>("Expedition/UI/equipment-ghost");
            DisableLayout(_screenRoot); DisableLayout(panel); DisableLayout(equipment); DisableLayout(bag);
            HideSurface(_screenRoot); HideSurface(panel); HideSurface(equipment);
            if (close != null) close.gameObject.SetActive(false);
            if (level != null) level.gameObject.SetActive(false); // The shared profile HUD already shows the real level.

            var background = MakeImage("ExpeditionCharacterWorld", panel);
            Area(background.rectTransform, 0, 0, 1, 1);
            background.sprite = Resources.Load<Sprite>("Expedition/Art/character-backdrop");
            background.color = background.sprite != null ? Color.white : ExpeditionTheme.Panel;
            background.raycastTarget = true;
            background.transform.SetAsFirstSibling();

            equipment.SetParent(panel, false);
            Area(equipment, 0, .495f, 1, 1);
            ExpeditionUi.Ensure<RectMask2D>(equipment.gameObject);
            _stageBackdrop = MakeImage("ExpeditionStageBackdrop", equipment).rectTransform;
            _stageBackdrop.GetComponent<Image>().sprite = background.sprite;
            var hero = MakeImage("ExpeditionHero", equipment);
            hero.sprite = Resources.Load<Sprite>("Expedition/Art/hero");
            CharacterAppearanceImage.Bind(hero);
            hero.preserveAspect = true;
            Area(hero.rectTransform, .235f, .175f, .765f, .985f);

            for (var i = 0; i < equipmentSlots.Count; i++)
            {
                var slot = equipmentSlots[i];
                if (slot == null) continue;
                var rt = slot.transform as RectTransform;
                rt.SetParent(equipment, false); rt.localScale = Vector3.one;
                DisableLayout(rt);
                var layout = rt.GetComponent<LayoutElement>();
                if (layout != null) layout.ignoreLayout = true;
                Image itemIcon = null;
                foreach (var text in rt.GetComponentsInChildren<TMP_Text>(true))
                    if (text.name != "StackLabel") { text.enabled = false; text.raycastTarget = false; }
                foreach (var image in rt.GetComponentsInChildren<Image>(true))
                {
                    if (image.transform == rt) continue;
                    if (image.name == "Icon")
                    {
                        Area(image.rectTransform, .075f, .075f, .925f, .925f);
                        image.preserveAspect = true; image.raycastTarget = false; image.color = Color.white;
                        itemIcon = image;
                    }
                    else image.enabled = false;
                }
                var frame = SetFrame(rt, _slotFrame, Color.white, true);
                var oldOutline = rt.GetComponent<Outline>();
                if (oldOutline != null) oldOutline.enabled = false;
                var button = rt.GetComponent<Button>();
                if (button != null) NeutralButton(button, frame);
                var wash = MakeImage("ExpeditionQualityFill", rt);
                wash.sprite = ModalPanelCloseButton.WhiteSprite();
                Area(wash.rectTransform, .065f, .065f, .935f, .935f);
                wash.transform.SetAsFirstSibling();
                var ghost = MakeImage("ExpeditionEmptySlot", rt);
                ghost.sprite = ExpeditionTheme.Icon(SlotIcons[(int)slot.SlotId]);
                ghost.enabled = ghost.sprite != null;
                ghost.preserveAspect = true;
                ghost.material = ghostMaterial;
                ghost.color = new Color(.71f, .75f, .74f, .42f);
                Area(ghost.rectTransform, .17f, .17f, .83f, .83f);
                var rim = MakeImage("ExpeditionQualityRim", rt);
                SetFrame(rim.rectTransform, _slotFrame, Color.white, false);
                rim.fillCenter = false;
                Area(rim.rectTransform, 0, 0, 1, 1);
                rim.enabled = false;
                var setRim = MakeImage("ExpeditionSetActivationRim", rt);
                SetFrame(setRim.rectTransform, _slotFrame, ExpeditionTheme.Amber, false);
                setRim.fillCenter = false;
                Area(setRim.rectTransform, -.035f, -.035f, 1.035f, 1.035f);
                setRim.enabled = false; _setRims.Add(setRim);
                _slots.Add(rt); _slotIds.Add((int)slot.SlotId); _slotBackgrounds.Add(frame);
                _slotWashes.Add(wash); _slotGhosts.Add(ghost); _slotRims.Add(rim); _slotItemIcons.Add(itemIcon);
            }
            for (var i = equipment.childCount - 1; i >= 0; i--)
            {
                var child = equipment.GetChild(i);
                if (child != _stageBackdrop && child != hero.transform && !_slots.Contains(child as RectTransform)) child.gameObject.SetActive(false);
            }
            _stageBackdrop.SetAsFirstSibling(); hero.transform.SetSiblingIndex(1);
            _setBadge = MakeImage("ExpeditionEquipmentSetBadge", equipment).rectTransform;
            SetFrame(_setBadge, ExpeditionTheme.Plate, new Color(.66f, .78f, .69f, 1), false);
            Area(_setBadge, .45f, .04f, .55f, .17f);
            _setBadgeText = Label("Label", _setBadge, "8/8\n+25%", 23, new Color32(166, 233, 189, 255));
            Area(_setBadgeText.rectTransform, .035f, .08f, .965f, .92f);
            _setBadgeText.enableAutoSizing = true; _setBadgeText.fontSizeMin = 17; _setBadgeText.fontSizeMax = 23;
            _setBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
            _setBadge.gameObject.SetActive(false);

            // Identity is already visible in the shared profile HUD.
            var identity = MakeRect("ExpeditionCharacterIdentity", panel);
            Area(identity, .265f, .968f, .735f, .997f);
            if (nickname != null)
            {
                Move(nickname.rectTransform, identity, .01f, 0, .99f, 1);
                nickname.gameObject.SetActive(false);
            }
            identity.gameObject.SetActive(false);
            if (editNickname != null)
                editNickname.gameObject.SetActive(false);

            var stats = MakeImage("ExpeditionCharacterStats", panel).rectTransform;
            _statsBar = stats;
            Area(stats, .025f, .401f, .975f, .491f);
            SetFrame(stats, ExpeditionTheme.Plate, Color.white, false);
            for (var i = 0; values != null && i < values.Length && i < 5; i++)
            {
                var cell = MakeRect("Stat" + i, stats);
                Area(cell, i / 5f + .014f, .13f, (i + 1) / 5f - .014f, .87f);
                var icon = MakeImage("Icon", cell);
                icon.sprite = ExpeditionTheme.Icon(StatIcons[i]); icon.preserveAspect = true;
                icon.enabled = icon.sprite != null;
                Area(icon.rectTransform, .04f, .40f, .34f, .96f);
                var caption = Label("Caption", cell, StatNames[i], 23, ExpeditionTheme.Ivory);
                Area(caption.rectTransform, .015f, .035f, .985f, .36f);
                caption.textWrappingMode = TextWrappingModes.NoWrap;
                caption.margin = Vector4.zero;
                if (values[i] != null)
                {
                    Move(values[i].rectTransform, cell, .37f, .38f, .98f, .98f);
                    TextStyle(values[i], 36, ExpeditionTheme.Ivory, TextAlignmentOptions.MidlineLeft);
                    values[i].textWrappingMode = TextWrappingModes.NoWrap;
                    values[i].margin = Vector4.zero;
                }
                if (i > 0)
                {
                    var divider = MakeImage("StatDivider" + i, stats);
                    divider.sprite = ModalPanelCloseButton.WhiteSprite();
                    divider.color = new Color(.36f, .5f, .49f, .6f);
                    Area(divider.rectTransform, i / 5f - .0008f, .19f, i / 5f + .0008f, .81f);
                }
            }

            bag.SetParent(panel, false);
            Area(bag, .018f, .011f, .982f, .393f);
            SetFrame(bag, ExpeditionTheme.Plate, Color.white, true);
            var bagTitle = Label("ExpeditionInventoryTitle", bag, "Инвентарь", 48, ExpeditionTheme.Ivory);
            Area(bagTitle.rectTransform, .035f, .857f, .305f, .985f);
            bagTitle.alignment = TextAlignmentOptions.MidlineLeft;
            var captions = new[] { "Всё", "Снаряжение", "Чертежи", "Расходники" };
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var rt = MakeRect("ExpeditionInventoryFilter" + i, bag);
                Area(rt, .32f + i * .1625f, .875f, .4725f + i * .1625f, .97f);
                var surface = SetFrame(rt, ExpeditionTheme.Plate, Color.white, true);
                var button = ExpeditionUi.Ensure<Button>(rt.gameObject);
                NeutralButton(button, surface);
                var label = Label("Label", rt, captions[i], i==0?25:21, ExpeditionTheme.Ivory);
                Area(label.rectTransform, .025f, .04f, .975f, .96f);
                button.onClick.AddListener(() => SelectFilter(index));
                _filters[i] = button; _filterLabels[i] = label;
            }
            _bagViewport = MakeRect("ExpeditionBagViewport", bag);
            ExpeditionUi.Ensure<RectMask2D>(_bagViewport.gameObject);
            var viewportRaycast = ExpeditionUi.Ensure<Image>(_bagViewport.gameObject);
            viewportRaycast.sprite = ModalPanelCloseButton.WhiteSprite();
            viewportRaycast.color = new Color(0, 0, 0, .001f); viewportRaycast.raycastTarget = true;
            _bagContent = MakeRect("ExpeditionBagContent", _bagViewport);
            _bagContent.anchorMin = new Vector2(0, 1); _bagContent.anchorMax = Vector2.one;
            _bagContent.pivot = new Vector2(.5f, 1); _bagContent.anchoredPosition = Vector2.zero;
            _bagScroll = ExpeditionUi.Ensure<ScrollRect>(bag.gameObject);
            _bagScroll.viewport = _bagViewport; _bagScroll.content = _bagContent;
            _bagScroll.horizontal = false; _bagScroll.vertical = true; _bagScroll.scrollSensitivity = 60;
            _bagScroll.movementType = ScrollRect.MovementType.Clamped;
            _bagScrollbar = MakeImage("ExpeditionBagScrollbar", bag).rectTransform;
            SetFrame(_bagScrollbar, ExpeditionTheme.Plate, new Color(.55f, .65f, .63f), true);
            var slidingArea = MakeRect("SlidingArea", _bagScrollbar);
            Area(slidingArea, .12f, .015f, .88f, .985f);
            var handle = MakeImage("Handle", slidingArea);
            SetFrame(handle.rectTransform, ExpeditionTheme.Plate, ExpeditionTheme.Amber, true);
            Area(handle.rectTransform, 0, 0, 1, 1);
            var scrollbar = ExpeditionUi.Ensure<Scrollbar>(_bagScrollbar.gameObject);
            scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _bagScroll.verticalScrollbar = scrollbar;
            _bagScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            for (var i = 0; i < inventorySlots.Count; i++)
            {
                var slot = inventorySlots[i];
                if (slot == null) continue;
                var rt = slot.transform as RectTransform;
                rt.SetParent(_bagContent, false); rt.localScale = Vector3.one;
                DisableLayout(rt);
                slot.SetBackground(_slotFrame); slot.SetBackgroundColor(Color.white);
                var surface = SetFrame(rt, _slotFrame, Color.white, true);
                var outline = rt.GetComponent<Outline>(); if (outline != null) outline.enabled = false;
                foreach (var image in rt.GetComponentsInChildren<Image>(true))
                    if (image.name == "Icon") { Area(image.rectTransform, .075f, .12f, .925f, .94f); image.preserveAspect = true; image.raycastTarget = false; }
                foreach (var stack in rt.GetComponentsInChildren<TMP_Text>(true))
                    if (stack.name == "StackLabel") { Area(stack.rectTransform, .5f, .10f, .88f, .30f); TextStyle(stack, 27, ExpeditionTheme.Ivory, TextAlignmentOptions.BottomRight); stack.margin = Vector4.zero; }
                var rarity = MakeImage("ExpeditionQuality", rt);
                rarity.sprite = ModalPanelCloseButton.WhiteSprite(); Area(rarity.rectTransform, .055f, .04f, .084f, .21f);
                var tier = Label("ExpeditionTier", rt, "", 23, ExpeditionTheme.Ivory);
                Area(tier.rectTransform, .11f, .035f, .56f, .205f); tier.alignment = TextAlignmentOptions.BottomLeft;
                var dataIndex = slot.InventorySlotIndex >= 0 ? slot.InventorySlotIndex : i;
                _bagSlots.Add(rt); _bagDataIndices.Add(dataIndex); _bagRarity.Add(rarity); _bagTiers.Add(tier);
                ExpeditionUi.Ensure<CharacterInventoryScrollRelay>(rt.gameObject).Configure(_bagScroll, _view, dataIndex);
                var lockRect = MakeRect("ExpeditionInventoryLock", rt);
                Area(lockRect, .04f, .04f, .96f, .96f);
                var lockSurface = SetFrame(lockRect, ExpeditionTheme.Plate, new Color(.60f, .67f, .65f, 1), true);
                var lockButton = ExpeditionUi.Ensure<Button>(lockRect.gameObject);
                NeutralButton(lockButton, lockSurface);
                var lockIcon = MakeImage("LockIcon", lockRect); lockIcon.sprite = ExpeditionTheme.Icon("lock");
                lockIcon.preserveAspect = true; lockIcon.color = ExpeditionTheme.Muted;
                Area(lockIcon.rectTransform, .34f, .57f, .66f, .86f);
                if (lockIcon.sprite == null)
                {
                    lockIcon.enabled = false;
                    // Graphic forbids a second renderer on the Image GameObject;
                    // cover the very same icon rect with a dedicated native child.
                    var nativeLock = MakeRect("NativeLock", lockIcon.rectTransform);
                    Area(nativeLock, 0, 0, 1, 1);
                    ExpeditionUi.Ensure<CanvasRenderer>(nativeLock.gameObject);
                    var padlock = ExpeditionUi.Ensure<ExpeditionNavigationIcon>(nativeLock.gameObject);
                    padlock.Kind = 7; padlock.color = ExpeditionTheme.Muted; padlock.raycastTarget = false;
                }
                var lockLabel = Label("UnlockLabel", lockRect, "", 22, ExpeditionTheme.Muted);
                Area(lockLabel.rectTransform, .08f, .12f, .92f, .56f);
                lockLabel.textWrappingMode = TextWrappingModes.Normal;
                var captured = dataIndex;
                lockButton.onClick.AddListener(() => _view.RequestInventorySlotUnlock(captured));
                _bagLocks.Add(lockButton); _bagLockLabels.Add(lockLabel);
            }
            _bagFooter = Label("ExpeditionInventoryFooter", bag, "", 23, ExpeditionTheme.Muted);
            Area(_bagFooter.rectTransform, .04f, .018f, .70f, .089f);
            _bagFooter.alignment = TextAlignmentOptions.MidlineLeft;
            for (var i = 0; i < 2; i++)
            {
                var pageIndex = i;
                var rt = MakeRect("ExpeditionInventoryPage" + i, bag);
                Area(rt, .73f + i * .115f, .014f, .835f + i * .115f, .10f);
                var pageButton = ExpeditionUi.Ensure<Button>(rt.gameObject);
                NeutralButton(pageButton, SetFrame(rt, ExpeditionTheme.Plate, Color.white, true));
                pageButton.onClick.AddListener(() => SelectInventoryPage(pageIndex));
                ExpeditionUi.Ensure<CharacterInventoryPageDrop>(rt.gameObject).Configure(_view, pageIndex);
                _pages[i] = pageButton;
                _pageLabels[i] = Label("Label", rt, (i + 1).ToString(), 30, ExpeditionTheme.Ivory);
                Area(_pageLabels[i].rectTransform, .08f, .06f, .92f, .94f);
            }
            _emptyBag = Label("ExpeditionInventoryEmpty", _bagViewport, "", 30, ExpeditionTheme.Muted);
            Area(_emptyBag.rectTransform, .05f, .35f, .95f, .65f);
            for (var i = bag.childCount - 1; i >= 0; i--)
            {
                var child = bag.GetChild(i);
                if (child != _bagViewport && child != _bagScrollbar && child != bagTitle.transform && !child.name.StartsWith("ExpeditionInventoryFilter") && !child.name.StartsWith("ExpeditionInventoryPage") && child != _bagFooter.transform) child.gameObject.SetActive(false);
            }
            _consumables=ConsumableStatusStrip.Ensure(bag);_consumables.Bind(null);
            if (profileError != null) Move(profileError.rectTransform, panel, .03f, .395f, .97f, .417f);
            for (var i = panel.childCount - 1; i >= 0; i--)
            {
                var child = panel.GetChild(i);
                if (child != background.transform && child != equipment && child != bag && child != stats && child != identity && child != profileError?.transform) child.gameObject.SetActive(false);
            }
            _built = true; _geometryDirty = true; _resetBagScroll = true;
            _appearancePanel = ExpeditionUi.Ensure<CharacterAppearancePanel>(gameObject);
            _appearancePanel.Build(panel);
            var tabNames = new[] { "Снаряжение", "Образы" };
            for (var i = 0; i < _heroTabs.Length; i++)
            {
                var index = i;
                _heroTabs[i] = ExpeditionUi.Button(panel, "ExpeditionHeroTab" + i, tabNames[i],
                    .025f + i * .485f, .944f, .49f + i * .485f, .996f, () => SelectHeroTab(index), i == 0, 27);
                var tabLabel = _heroTabs[i].GetComponentInChildren<TMP_Text>();
                tabLabel.enableAutoSizing = true; tabLabel.fontSizeMin = 18; tabLabel.fontSizeMax = 27;
                tabLabel.textWrappingMode = TextWrappingModes.NoWrap;
                tabLabel.margin = Vector4.zero;
            }
            SelectHeroTab(0);
            RefreshEmptyEquipment(null);
            RefreshInventory();
        }

        public void SetShellLayout(bool attached)
        {
            _inShell = attached;
            var world = _panel != null ? _panel.Find("ExpeditionCharacterWorld")?.GetComponent<Image>() : null;
            if (world != null) world.enabled = !attached;
            if (_stageBackdrop != null) _stageBackdrop.GetComponent<Image>().enabled = !attached;
            RefreshLayout();
        }
        public void RefreshLayout() { if (Application.isPlaying && _built) _geometryDirty = true; }
        private void OnEnable() { RefreshLayout(); }
        private void OnRectTransformDimensionsChange() { if (!_applying) RefreshLayout(); }
        public void CancelInventoryGesture()
        {
            if (_bagScroll != null) _bagScroll.StopMovement();
            foreach (var slot in _bagSlots)
            {
                var handle = slot.GetComponent<CharacterDragSlotHandle>();
                if (handle != null) handle.CancelInteraction();
                else { var relay = slot.GetComponent<CharacterInventoryScrollRelay>(); if (relay != null) relay.CancelGesture(); }
            }
        }
        private void SelectFilter(int filter)
        {
            if (_filter == filter) return;
            if (_dragController != null) _dragController.CancelActiveDrag();
            CancelInventoryGesture(); _filter = filter; _resetBagScroll = true; RefreshInventory();
        }
        public void RefreshEmptyEquipment(string[] ids)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var id = _slotIds[i];
                var occupied = ids != null && id < ids.Length && !string.IsNullOrEmpty(ids[id]);
                ItemDefinition item = null;
                var hasQuality = occupied && _view != null && _view.TryGetEquipmentItem((EquipmentSlotId)id, out _, out item);
                var quality = hasQuality ? EquipmentSlotView.QualitySlotColor(item.Quality) : Color.white;
                _slotBackgrounds[i].color = Color.white;
                _slotGhosts[i].enabled = !occupied && _slotGhosts[i].sprite != null;
                _slotWashes[i].color = hasQuality ? new Color(quality.r, quality.g, quality.b, .23f) : new Color(.35f, .40f, .40f, .12f);
                _slotRims[i].enabled = hasQuality;
                _slotRims[i].color = new Color(quality.r, quality.g, quality.b, .92f);
                if (_slotItemIcons[i] != null)
                {
                    _slotItemIcons[i].color = Color.white;
                    _slotItemIcons[i].enabled = occupied && _slotItemIcons[i].sprite != null;
                }
            }
        }

        public void SetEquipmentSet(EquipmentSetBonus.State state)
        {
            if (_setBadge == null) return;
            var nextKey = state.Key;
            var activated = state.Active && nextKey != _activeSetKey;
            _activeSetKey = nextKey;
            _setBadge.gameObject.SetActive(state.Active);
            if (activated)
            {
                SetActivationCount++;
                _setPulseStarted = Time.unscaledTime;
                _setPulseRunning = true;
                _setBadgeText.text = "8/8\n+25%";
            }
            if (!state.Active) ApplySetActivationFrame(2);
            else if (activated) ApplySetActivationFrame(0);
        }

        // A single short pulse on accepted 7/8 -> 8/8 (or a different set), never an endless per-slot animation.
        // The rarity rim remains intact beneath this separate, non-interactive overlay.
        public void ApplySetActivationFrame(float elapsed)
        {
            var progress = Mathf.Clamp01(elapsed / 1.15f);
            if (progress >= 1) _setPulseRunning = false;
            var pulse = EquipmentSetActive ? Mathf.Sin(progress * Mathf.PI * 3) * (1 - progress) : 0;
            var alpha = EquipmentSetActive && progress < 1 ? .30f + Mathf.Abs(pulse) * .62f : 0;
            foreach (var rim in _setRims)
            {
                rim.enabled = alpha > .001f;
                rim.color = new Color(1, .83f, .40f, alpha);
            }
            if (_setBadge != null) _setBadge.localScale = Vector3.one * (1 + Mathf.Max(0, pulse) * .045f);
        }
        public void RefreshInventory()
        {
            if (!_built) return;
            _visibleBagSlots.Clear();
            for (var i = 0; i < _bagSlots.Count; i++)
            {
                var item = default(ItemDefinition);
                var occupied = _view != null && _view.TryGetInventoryItem(_bagDataIndices[i], out _, out item);
                var dataIndex = _bagDataIndices[i];
                var locked = _view != null && dataIndex >= _view.InventoryCapacity;
                var visible = dataIndex / CharacterScreenView.InventoryPageSize == _page &&
                    (_filter == 0 || occupied && (_filter == 1 ? item.Kind == ItemKind.Equipment : _filter==2?item.Kind==ItemKind.Recipe:item.Kind==ItemKind.Consumable));
                _bagLocks[i].gameObject.SetActive(locked);
                var next = locked && _view != null && dataIndex == _view.InventoryCapacity;
                _bagLocks[i].interactable = next;
                _bagLockLabels[i].text = next ? "Открыть\n" + _view.InventorySlotPrice + " материи" : "Закрыто";
                _bagLockLabels[i].color = next ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                var padlock = _bagLocks[i].transform.Find("LockIcon/NativeLock")?.GetComponent<ExpeditionNavigationIcon>();
                if (padlock != null) padlock.color = _bagLockLabels[i].color;
                _bagSlots[i].gameObject.SetActive(visible);
                _bagRarity[i].enabled = occupied;
                if (occupied) _bagRarity[i].color = EquipmentSlotView.QualitySlotColor(item.Quality);
                _bagTiers[i].text = occupied && item.Kind == ItemKind.Equipment ? "T" + item.Tier : "";
                if (visible) _visibleBagSlots.Add(i);
            }
            for (var i = 0; i < _filters.Length; i++)
            {
                var selected = i == _filter;
                var image = _filters[i].targetGraphic as Image;
                if (image != null)
                {
                    var frame = selected ? ExpeditionArt.Frame(true) : ExpeditionTheme.Plate;
                    image.sprite = frame != null ? frame : ExpeditionTheme.Plate;
                    image.color = Color.white;
                }
                _filterLabels[i].color = selected ? new Color32(31, 26, 17, 255) : ExpeditionTheme.Ivory;
            }
            for (var i = 0; i < _pages.Length; i++)
            {
                var image = _pages[i].targetGraphic as Image;
                image.sprite = i == _page ? ExpeditionArt.Frame(true) : ExpeditionTheme.Plate;
                image.color = Color.white;
                _pageLabels[i].color = i == _page ? new Color32(31, 26, 17, 255) : ExpeditionTheme.Ivory;
            }
            if (_view != null)
            {
                _bagFooter.text = _view.PendingLootCount > 0 ? "Ожидают места: " + _view.PendingLootCount
                    : "Открыто ячеек: " + _view.InventoryCapacity + "/" + CharacterScreenView.InventoryMaximum;
                _bagFooter.color = _view.PendingLootCount > 0 ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            }
            _emptyBag.text = _filter == 1 ? "В рюкзаке нет снаряжения" : _filter==3?"В рюкзаке нет расходников":"В рюкзаке нет чертежей";
            _emptyBag.gameObject.SetActive(_visibleBagSlots.Count == 0);
            _geometryDirty = true;
        }
        private void LateUpdate()
        {
            if (!Application.isPlaying || !_built) return;
            if (_setPulseRunning) ApplySetActivationFrame(Time.unscaledTime - _setPulseStarted);
            var dimensions = new Vector2Int(Screen.width, Screen.height);
            var size = _screenRoot != null ? _screenRoot.rect.size : Vector2.zero;
            var safe = Screen.safeArea;
            if (!_geometryDirty && dimensions == _lastScreenSize && safe == _lastSafeArea && size == _lastRootSize) return;
            ApplyGeometry(); _lastScreenSize = dimensions; _lastSafeArea = safe; _lastRootSize = size; _geometryDirty = false;
        }
        private void ApplyGeometry()
        {
            if (_applying || _panel == null) return;
            _applying = true;
            Area(_screenRoot, 0, 0, 1, 1);
            Area(_panel, 0, 0, 1, 1);
            if (!_inShell)
            {
                var safe = Screen.safeArea;
                var w = Mathf.Max(1, Screen.width); var h = Mathf.Max(1, Screen.height);
                if (safe.width <= 0 || safe.height <= 0 || safe.xMax > w + 1 || safe.yMax > h + 1 || safe.xMin < 0 || safe.yMin < 0) safe = new Rect(0, 0, w, h);
                Area(_panel, safe.xMin / w, safe.yMin / h + .105f, safe.xMax / w, safe.yMax / h - .078f);
            }
            _panel.ForceUpdateRectTransforms();
            // Two complete square rows match the reference at rest; all remaining cells stay scrollable.
            const int columns = 4; const float gap = 13;
            var panelHeight = Mathf.Max(1, _panel.rect.height);
            var bagWidth = _panel.rect.width * .964f;
            var cell = Mathf.Max(1, (bagWidth - 74 - 12 - (columns - 1) * gap) / columns);
            var visibleRowsHeight = cell * 2 + gap + 12;
            var bagHeight = Mathf.Min((visibleRowsHeight + 80) / .72f, panelHeight * .55f);
            var bagBottom = panelHeight * .011f;
            Area(_bag, .018f, bagBottom / panelHeight, .982f, (bagBottom + bagHeight) / panelHeight);
            var statsBottom = bagBottom + bagHeight + 14;
            var statsTop = statsBottom + panelHeight * .09f;
            Area(_statsBar, .025f, statsBottom / panelHeight, .975f, statsTop / panelHeight);
            Area(_equipment, 0, (statsTop + 7) / panelHeight, 1, .936f);
            _equipment.ForceUpdateRectTransforms(); _bag.ForceUpdateRectTransforms();
            var width = _equipment.rect.width; var height = _equipment.rect.height;
            var side = Mathf.Min(width * .225f, height * .265f);
            for (var i = 0; i < _slots.Count; i++)
            {
                var id = _slotIds[i]; var weapon = id >= 6;
                var size = weapon ? Mathf.Min(width * .175f, height * .19f) : side;
                var position = weapon ? new Vector2(id == 6 ? .365f : .635f, .102f)
                    : new Vector2(id < 3 ? .139f : .861f, .80f - (id < 3 ? id : id - 3) * .284f);
                _slots[i].anchorMin = _slots[i].anchorMax = position; _slots[i].pivot = new Vector2(.5f, .5f);
                _slots[i].anchoredPosition = Vector2.zero; _slots[i].sizeDelta = new Vector2(size, size);
            }
            if (_setBadge != null)
            {
                // A small buff sits between the hand slots, below the figure's feet.
                // Size it from the real gap so it cannot cover gear or the character.
                var handSize = Mathf.Min(width * .175f, height * .19f);
                _setBadge.anchorMin = _setBadge.anchorMax = new Vector2(.5f, .102f);
                _setBadge.pivot = new Vector2(.5f, .5f);
                _setBadge.anchoredPosition = Vector2.zero;
                _setBadge.sizeDelta = new Vector2(Mathf.Clamp(width * .27f - handSize - 16, 24, 108), Mathf.Min(84, handSize * .72f));
            }
            // Align the illustrated platform with the hero's feet while retaining its original aspect.
            var backdrop = _stageBackdrop.GetComponent<Image>().sprite;
            var stageImageHeight = backdrop != null ? width * backdrop.rect.height / backdrop.rect.width : height;
            _stageBackdrop.anchorMin = _stageBackdrop.anchorMax = new Vector2(.5f, 0);
            _stageBackdrop.pivot = new Vector2(.5f, 0);
            _stageBackdrop.sizeDelta = new Vector2(width, stageImageHeight);
            _stageBackdrop.anchoredPosition = new Vector2(0, height * .18f - stageImageHeight * .48f);
            _bagViewport.anchorMin = new Vector2(0, 0); _bagViewport.anchorMax = new Vector2(1, .72f);
            _bagViewport.offsetMin = new Vector2(20, 76); _bagViewport.offsetMax = new Vector2(-54, -4);
            _bagScrollbar.anchorMin = new Vector2(1, 0); _bagScrollbar.anchorMax = new Vector2(1, .72f);
            _bagScrollbar.offsetMin = new Vector2(-42, 80); _bagScrollbar.offsetMax = new Vector2(-14, -10);
            _bagViewport.ForceUpdateRectTransforms();
            cell = Mathf.Max(1, (_bagViewport.rect.width - 12 - (columns - 1) * gap) / columns);
            var rows = Mathf.Max(1, Mathf.CeilToInt(_visibleBagSlots.Count / (float)columns));
            var contentHeight = rows * cell + (rows - 1) * gap + 12;
            _bagContent.sizeDelta = new Vector2(0, contentHeight);
            for (var i = 0; i < _visibleBagSlots.Count; i++)
            {
                var rt = _bagSlots[_visibleBagSlots[i]];
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.sizeDelta = new Vector2(cell, cell);
                rt.anchoredPosition = new Vector2(6 + (i % columns) * (cell + gap), -6 - (i / columns) * (cell + gap));
            }
            if (_resetBagScroll) { _bagContent.anchoredPosition = Vector2.zero; _bagScroll.verticalNormalizedPosition = 1; _resetBagScroll = false; }
            _applying = false;
        }
        private TMP_Text Label(string name, Transform parent, string value, float size, Color color)
        {
            var rt = MakeRect(name, parent); var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font; label.text = value; TextStyle(label, size, color, TextAlignmentOptions.Center); return label;
        }
        private static void TextStyle(TMP_Text label, float size, Color color, TextAlignmentOptions alignment)
        {
            label.font = ExpeditionTheme.Font; label.fontSize = size; label.fontSizeMin = size * .8f; label.fontSizeMax = size;
            label.enableAutoSizing = true; label.fontStyle = FontStyles.Normal; label.color = color;
            label.alignment = alignment; label.raycastTarget = false; label.overflowMode = TextOverflowModes.Ellipsis;
        }
        private static RectTransform MakeRect(string name, Transform parent)
        { var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); return rt; }
        private static Image MakeImage(string name, Transform parent)
        { var image = MakeRect(name, parent).gameObject.AddComponent<Image>(); image.raycastTarget = false; return image; }
        private static Image SetFrame(RectTransform rt, Sprite sprite, Color color, bool raycast)
        {
            var image = ExpeditionUi.Ensure<Image>(rt.gameObject); image.enabled = true; image.sprite = sprite; image.overrideSprite = null;
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4; image.color = color; image.raycastTarget = raycast; return image;
        }
        private static void HideSurface(RectTransform rt)
        { if (rt == null) return; var image = rt.GetComponent<Image>(); if (image != null) { image.enabled = false; image.raycastTarget = false; } }
        private static void NeutralButton(Button button, Image target)
        {
            button.targetGraphic = target; var colors = button.colors; colors.normalColor = colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.04f); colors.pressedColor = new Color(.78f, .81f, .80f); button.colors = colors;
        }
        private static void DisableLayout(RectTransform rt)
        {
            if (rt == null) return;
            var layout = rt.GetComponent<LayoutGroup>(); if (layout != null) layout.enabled = false;
            var fitter = rt.GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
            var aspect = rt.GetComponent<AspectRatioFitter>(); if (aspect != null) aspect.enabled = false;
        }
        private static void Move(RectTransform rt, Transform parent, float x0, float y0, float x1, float y1)
        { if (rt == null) return; rt.SetParent(parent, false); rt.localScale = Vector3.one; Area(rt, x0, y0, x1, y1); }
        private static void Area(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return; rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(.5f, .5f); rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
        }
    }
    /// <summary>Drop onto a page number to move to its first free unlocked cell.</summary>
    public sealed class CharacterInventoryPageDrop : MonoBehaviour, IDropHandler
    {
        private CharacterScreenView _view;
        private int _page;
        public void Configure(CharacterScreenView view, int page) { _view = view; _page = page; }
        public void OnDrop(PointerEventData eventData)
        {
            if (_view == null || eventData.pointerDrag == null) return;
            var source = eventData.pointerDrag.GetComponent<CharacterDragSlotHandle>();
            if (source != null) _view.MoveDraggedItemToPage(source, _page);
        }
    }
    /// <summary>Vertical drags scroll; holding an item or dragging it sideways keeps item transfer.</summary>
    public sealed class CharacterInventoryScrollRelay : MonoBehaviour, IPointerDownHandler
    {
        private ScrollRect _scroll;
        private CharacterScreenView _view;
        private int _index;
        private bool _scrolling;
        private bool _hasPress;
        private float _pressedAt;
        private float _suppressClickUntil;

        public bool IsScrolling => _scrolling;
        public bool BlocksClick => _scrolling || Time.unscaledTime < _suppressClickUntil;

        public void Configure(ScrollRect scroll, CharacterScreenView view, int inventoryIndex)
        { _scroll = scroll; _view = view; _index = inventoryIndex; }

        public void OnPointerDown(PointerEventData eventData)
        {
            _hasPress = true;
            _pressedAt = Time.unscaledTime;
            _suppressClickUntil = 0;
            _scrolling = false;
            if (_scroll != null) _scroll.StopMovement();
        }

        public bool BeginGesture(PointerEventData eventData)
        {
            if (_scroll == null || eventData == null) return false;
            var hasItem = _view != null && _view.HasInventoryItem(_index);
            var delta = eventData.position - eventData.pressPosition;
            var held = _hasPress && Time.unscaledTime - _pressedAt >= .3f;
            var horizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.15f;
            _scrolling = !hasItem || (!held && !horizontal);
            if (!_scrolling) return false;
            _scroll.OnInitializePotentialDrag(eventData);
            _scroll.OnBeginDrag(eventData);
            return true;
        }

        public void DragScroll(PointerEventData eventData) { if (_scrolling && _scroll != null) _scroll.OnDrag(eventData); }
        public void CancelGesture()
        {
            if (_scrolling && _scroll != null && EventSystem.current != null)
                _scroll.OnEndDrag(new PointerEventData(EventSystem.current));
            _scrolling = false;
            _hasPress = false;
            _suppressClickUntil = Time.unscaledTime + .15f;
        }

        public void EndGesture(PointerEventData eventData)
        {
            if (_scrolling && _scroll != null) _scroll.OnEndDrag(eventData);
            _scrolling = false;
            _hasPress = false;
            _suppressClickUntil = Time.unscaledTime + .15f;
        }
    }
}
