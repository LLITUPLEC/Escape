using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.UI;

namespace Project.Character.UI
{
    /// <summary>Forge environment, eight production queues, selected recipe and an on-demand learned recipe drawer.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionWorkshopLayout : MonoBehaviour
    {
        public static readonly Color Ink = new Color32(11, 27, 31, 255);
        public static readonly Color Panel = new Color32(24, 49, 56, 255);
        public static readonly Color Ivory = new Color32(235, 223, 198, 255);
        public static readonly Color Muted = new Color32(157, 183, 176, 255);
        public static readonly Color Amber = new Color32(237, 170, 83, 255);
        public struct MaterialState { public Sprite icon; public string name; public long owned, required; }
        public struct PrerequisiteState { public Sprite icon; public string name; public ItemQualityTier quality; public bool owned; }
        private readonly Image[] _slots = new Image[8];
        private readonly Text[] _emptyMarks = new Text[8];
        private readonly Image[] _progress = new Image[8];
        private readonly Text[] _timers = new Text[8];
        private readonly bool[] _occupied = new bool[8], _ready = new bool[8];
        private readonly Image[] _materialIcons = new Image[4];
        private readonly Text[] _materialValues = new Text[4];
        private readonly RectTransform[] _materialCells = new RectTransform[4];
        private Text _queueTitle, _duration, _fodder, _emptyItemHint;
        private Text _fodderStatus;
        private RectTransform _fodderRoot;
        private Image _fodderIcon, _fodderFrame;
        private Image _itemPreview;
        private GameObject _picker;
        private Button _claimReady, _chooseRecipe;
        private bool _built;
        private RectTransform _safeContent, _recipePanel, _recipeScroll;
        private Text _recipeHeader;
        public RectTransform RecipePanel => _recipePanel;
        public RectTransform RecipeScroll => _recipeScroll;
        public Text RecipeHeader => _recipeHeader;

        public static ExpeditionWorkshopLayout Install(RectTransform background, Transform slots, Action claimReady = null)
        {
            if (background == null || slots == null) return null;
            var view = ExpeditionUi.Ensure<ExpeditionWorkshopLayout>(background.gameObject);
            if (!view._built) view.Build(background, slots, claimReady);
            view.ApplyRecipePanelLayout();
            return view;
        }

        private void Build(RectTransform background, Transform slots, Action claimReady)
        {
            _built = true;
            // The shared full-screen environment also owns forge embers.
            _safeContent = ExpeditionUi.Rect(background, "WorkshopSafeArea", 0, 0, 1, 1);
            _safeContent.gameObject.AddComponent<ExpeditionSafeArea>();
            _recipePanel = background.Find("WorkshopRecipePanel") as RectTransform;
            foreach (var name in new[] { "Title", "BackButton" }) { var old = background.Find(name); if (old != null) old.gameObject.SetActive(false); }
            var hint = background.Find("Hint")?.GetComponent<Text>();
            if (hint != null)
            {
                hint.transform.SetParent(_safeContent, false);
                Rect(hint.rectTransform, .05f, .594f, .95f, .623f);
                StyleText(hint, 21, Ivory, TextAnchor.MiddleCenter);
            }
            var queue = Plate(_safeContent, "ProductionQueues", .018f, .363f, .982f, .591f);
            _queueTitle = Text(queue.transform, "QueueTitle", "Очередь 0/8", 33, Ivory, TextAnchor.MiddleLeft, .035f, .83f, .53f, .97f, true);
            _claimReady = Button(queue.transform, "ClaimReady", "ЗАБРАТЬ ГОТОВОЕ", false, .58f, .845f, .965f, .967f);
            _claimReady.onClick.AddListener(() => claimReady?.Invoke());
            _claimReady.interactable = false;
            slots.SetParent(queue.transform, false);
            Rect(slots as RectTransform, .025f, .04f, .975f, .82f);
            var slotsImage = slots.GetComponent<Image>();
            if (slotsImage != null) { slotsImage.sprite = null; slotsImage.color = Color.clear; slotsImage.raycastTarget = false; }
            foreach (var group in slots.GetComponents<LayoutGroup>()) group.enabled = false;
            foreach (var fitter in slots.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
            for (var i = 0; i < 8; i++) BuildSlot(slots, i);
            var equipment = slots.Find("EquipmentWorkshopLayout");
            if (equipment != null) equipment.gameObject.SetActive(false);
            BuildSelectedRecipe();
            BuildRecipePicker(background);
        }

        private void BuildSlot(Transform slots, int index)
        {
            var slot = Find(slots, "Slot_" + index);
            if (slot == null) return;
            slot.SetParent(slots, false);
            var column = index % 4;
            var top = index < 4;
            Rect(slot as RectTransform, column * .25f + .006f, top ? .515f : .015f, (column + 1) * .25f - .006f, top ? .985f : .485f);
            var aspect = slot.GetComponent<AspectRatioFitter>();
            if (aspect != null) aspect.enabled = false;
            var element = slot.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;
            var image = slot.GetComponent<Image>();
            if (image != null) { ExpeditionUi.StylePanel(image, Ink); image.raycastTarget = true; }
            _slots[index] = image;
            // StyleButton touches every child Text. Apply it before the slot-specific geometry/type sizes.
            var button = slot.GetComponent<Button>();
            if (button != null) { StyleButton(button, false); if (image != null) ExpeditionUi.StylePanel(image, Ink); }
            var label = slot.Find("SlotLabel")?.GetComponent<Text>();
            if (label != null) { Rect(label.rectTransform, .09f, .715f, .91f, .91f); StyleSlotText(label, 18, Muted); }
            var icon = slot.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                Rect(icon.rectTransform, .14f, .305f, .86f, .705f);
                icon.type = UnityEngine.UI.Image.Type.Simple;
                icon.preserveAspect = true; icon.raycastTarget = false; icon.enabled = icon.sprite != null;
            }
            var timer = slot.Find("Timer")?.GetComponent<Text>();
            if (timer != null) { Rect(timer.rectTransform, .09f, .075f, .91f, .275f); StyleSlotText(timer, 18, Amber); }
            _timers[index] = timer;
            var outline = slot.GetComponent<Outline>();
            if (outline != null) { outline.effectColor = Amber; outline.effectDistance = new Vector2(2, -2); }
            _emptyMarks[index] = Text(slot, "EmptyQueueMark", "+", 36, new Color32(104, 126, 121, 255), TextAnchor.MiddleCenter, .25f, .31f, .75f, .70f);
            var track = Image(slot, "ProgressTrack", new Color32(9, 20, 23, 255), .09f, .285f, .91f, .31f);
            _progress[index] = Image(track.transform, "ActualProgress", Amber, 0, .1f, 0, .9f);
            track.gameObject.SetActive(false);
        }

        private void BuildSelectedRecipe()
        {
            if (_recipePanel == null) return;
            _recipePanel.SetParent(_safeContent, false);
            var image = _recipePanel.GetComponent<Image>();
            if (image != null) ExpeditionUi.StylePanel(image, Panel);
            var itemFrame = Plate(_recipePanel, "SelectedItemFrame", .022f, .265f, .345f, .947f);
            _itemPreview = Image(itemFrame.transform, "ActualItemIcon", Color.white, .045f, .035f, .955f, .965f);
            _itemPreview.preserveAspect = true;
            _itemPreview.enabled = false;
            _emptyItemHint = Text(_recipePanel, "ChooseItemHint", "Выберите изученный рецепт. Новые рецепты можно найти в шахте.",
                28, Ivory, TextAnchor.MiddleLeft, .37f, .55f, .965f, .95f);
            _chooseRecipe = Button(_recipePanel, "ChooseRecipe", "РЕЦЕПТЫ ›", false, .023f, .06f, .345f, .235f);
            _chooseRecipe.onClick.AddListener(() => OpenRecipes());
            _duration = Text(_recipePanel, "CraftDuration", "", 20, Ivory, TextAnchor.MiddleLeft, .365f, .221f, .97f, .269f);
            _fodderRoot = ExpeditionUi.Rect(_recipePanel, "PrerequisiteItem", .37f, .27f, .97f, .382f);
            _fodderFrame = Plate(_fodderRoot, "PrerequisiteFrame", 0, .025f, .09f, .975f);
            _fodderIcon = Image(_fodderFrame.transform, "PrerequisiteIcon", Color.white, .08f, .08f, .92f, .92f);
            _fodderIcon.preserveAspect = true;
            _fodder = Text(_fodderRoot, "FodderRequirement", "", 21, Ivory, TextAnchor.MiddleLeft, .115f, .45f, 1, 1);
            _fodderStatus = Text(_fodderRoot, "PrerequisiteStatus", "", 17, Muted, TextAnchor.MiddleLeft, .115f, 0, 1, .47f);
            _fodderRoot.gameObject.SetActive(false);
            for (var i = 0; i < 4; i++)
            {
                var cell = ExpeditionUi.Rect(_recipePanel, "Material_" + i, .37f, .34f, .66f, .52f);
                _materialCells[i] = cell;
                _materialIcons[i] = Image(cell, "Icon", Color.white, 0, .1f, .28f, .9f);
                _materialIcons[i].preserveAspect = true;
                _materialValues[i] = Text(cell, "OwnedRequired", "", 23, Ivory, TextAnchor.MiddleLeft, .30f, .04f, 1, .96f);
                cell.gameObject.SetActive(false);
            }
        }

        private void BuildRecipePicker(RectTransform background)
        {
            if (_recipePanel == null) return;
            _recipeHeader = _recipePanel.Find("RecipeHeader")?.GetComponent<Text>();
            _recipeScroll = _recipePanel.Find("RecipeScroll") as RectTransform;
            _picker = ExpeditionUi.Rect(background, "LearnedRecipePicker", 0, 0, 1, 1).gameObject;
            var canvas = _picker.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 245;
            _picker.AddComponent<GraphicRaycaster>();
            var scrim = Image(_picker.transform, "DismissScrim", new Color(.01f, .025f, .03f, .74f), 0, 0, 1, 1);
            scrim.raycastTarget = true;
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(CloseRecipes);
            var safe = ExpeditionUi.Rect(_picker.transform, "SafeArea", 0, 0, 1, 1);
            safe.gameObject.AddComponent<ExpeditionSafeArea>();
            var sheet = Plate(safe, "RecipeDrawer", .035f, .16f, .965f, .84f);
            if (_recipeHeader != null)
            {
                _recipeHeader.transform.SetParent(sheet.transform, false);
                Rect(_recipeHeader.rectTransform, .035f, .89f, .83f, .973f);
                StyleText(_recipeHeader, 32, Ivory, TextAnchor.MiddleLeft);
            }
            Button(sheet.transform, "CloseRecipes", "×", false, .86f, .905f, .965f, .972f).onClick.AddListener(CloseRecipes);
            Text(sheet.transform, "PickerHint", "Изученные рецепты выбранной очереди", 22, Muted, TextAnchor.MiddleLeft, .04f, .84f, .96f, .9f);
            if (_recipeScroll != null)
            {
                _recipeScroll.SetParent(sheet.transform, false);
                Rect(_recipeScroll, .03f, .04f, .97f, .83f);
                var image = _recipeScroll.GetComponent<Image>();
                if (image != null) { image.sprite = null; image.color = new Color32(11, 27, 31, 255); image.raycastTarget = true; }
                var scroll = _recipeScroll.GetComponent<ScrollRect>();
                if (scroll != null) { scroll.scrollSensitivity = 45; scroll.movementType = ScrollRect.MovementType.Clamped; }
                var layout = _recipeScroll.Find("Viewport/Content")?.GetComponent<VerticalLayoutGroup>();
                if (layout != null) { layout.spacing = 9; layout.padding = new RectOffset(10, 10, 8, 8); }
            }
            _picker.SetActive(false);
        }

        public void ApplyRecipePanelLayout()
        {
            if (_recipePanel == null) return;
            Rect(_recipePanel, .018f, .112f, .982f, .355f);
            var stats = _recipePanel.Find("ItemStatsText")?.GetComponent<Text>();
            if (stats != null) ApplyItemStatsLayout(stats.rectTransform);
            var details = _recipePanel.Find("DetailText")?.GetComponent<Text>();
            if (details != null) { Rect(details.rectTransform, .37f, .34f, .965f, .535f); StyleText(details, 23, Muted, TextAnchor.UpperLeft); }
            var buttons = _recipePanel.Find("Buttons") as RectTransform;
            if (buttons != null)
            {
                Rect(buttons, .37f, .055f, .972f, .218f);
                var group = buttons.GetComponent<HorizontalLayoutGroup>();
                if (group != null) { group.spacing = 8; group.childForceExpandWidth = true; group.childForceExpandHeight = true; }
                foreach (var button in buttons.GetComponentsInChildren<Button>(true)) StyleButton(button, button.name != "RushButton");
            }
        }

        public void ApplyItemStatsLayout(RectTransform stats)
        {
            Rect(stats, .37f, .55f, .965f, .95f);
            var text = stats.GetComponent<Text>();
            if (text == null) return;
            StyleText(text, 25, Ivory, TextAnchor.UpperLeft);
            text.lineSpacing = 1.02f;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 19; text.resizeTextMaxSize = 25;
        }

        public void SetItemPreview(Sprite icon, string duration, bool chooseEnabled)
        {
            if (_itemPreview != null)
            {
                _itemPreview.sprite = icon != null ? icon : ExpeditionTheme.Icon("blueprint");
                _itemPreview.enabled = _itemPreview.sprite != null;
                _itemPreview.color = icon != null ? Color.white : new Color(1, 1, 1, .45f);
            }
            if (_emptyItemHint != null)
            {
                _emptyItemHint.gameObject.SetActive(icon == null);
                _emptyItemHint.text = chooseEnabled ? "Выберите изученный рецепт. Новые рецепты можно найти в шахте." : "Загружаем сведения о снаряжении…";
            }
            if (_duration != null) _duration.text = duration ?? "";
            if (_chooseRecipe != null) _chooseRecipe.interactable = chooseEnabled;
            foreach (var cell in _materialCells) if (cell != null) cell.gameObject.SetActive(false);
            if (_fodder != null) _fodder.text = "";
            if (_fodderRoot != null) _fodderRoot.gameObject.SetActive(false);
            var details = _recipePanel != null ? _recipePanel.Find("DetailText") : null;
            if (details != null) details.gameObject.SetActive(true);
        }

        public void SetMaterials(MaterialState[] materials, PrerequisiteState prerequisite)
        {
            var count = Mathf.Min(materials?.Length ?? 0, 4);
            for (var i = 0; i < 4; i++)
            {
                var show = i < count;
                _materialCells[i].gameObject.SetActive(show);
                if (!show) continue;
                var col = i % 2; var row = i / 2;
                var y0 = count <= 2 ? .382f : row == 0 ? .462f : .382f;
                var y1 = count <= 2 ? .54f : row == 0 ? .54f : .46f;
                Rect(_materialCells[i], .37f + col * .305f, y0, .66f + col * .305f, y1);
                var material = materials[i];
                _materialIcons[i].sprite = material.icon;
                _materialIcons[i].enabled = material.icon != null;
                _materialValues[i].text = material.owned + "/" + material.required;
                _materialValues[i].color = material.owned >= material.required ? Ivory : new Color32(218, 123, 94, 255);
            }
            var hasPrerequisite = !string.IsNullOrEmpty(prerequisite.name);
            _fodderRoot.gameObject.SetActive(hasPrerequisite);
            if (hasPrerequisite)
            {
                _fodderIcon.sprite = prerequisite.icon;
                _fodderIcon.enabled = prerequisite.icon != null;
                SetRarityBorder(_fodderFrame.transform, prerequisite.quality);
                _fodder.text = prerequisite.name;
                _fodder.color = EquipmentSlotView.QualitySlotColor(prerequisite.quality);
                _fodderStatus.text = prerequisite.owned ? "Есть · будет поглощён" : "Нет в наличии · требуется для ковки";
                _fodderStatus.color = prerequisite.owned ? Muted : new Color32(218, 123, 94, 255);
            }
            var details = _recipePanel.Find("DetailText");
            if (details != null) details.gameObject.SetActive(false);
        }

        public void OpenRecipes() { if (_picker != null) _picker.SetActive(true); }
        public void CloseRecipes() { if (_picker != null) _picker.SetActive(false); }

        public void StyleRecipeRow(GameObject row, bool selected)
        {
            if (row.GetComponent<Button>() == null) return;
            var image = row.GetComponent<Image>();
            if (image != null) { ExpeditionUi.StylePanel(image, selected ? new Color32(66, 87, 77, 255) : Panel); image.raycastTarget = true; }
            var marker = row.transform.Find("SelectedRecipeMarker")?.GetComponent<Image>();
            if (marker == null) marker = Image(row.transform, "SelectedRecipeMarker", Amber, .008f, .15f, .015f, .85f);
            marker.gameObject.SetActive(selected);
            var element = row.GetComponent<LayoutElement>();
            if (element != null) { element.minHeight = 118; element.preferredHeight = 118; }
            var text = row.transform.Find("Txt")?.GetComponent<Text>();
            if (text != null) { Rect(text.rectTransform, .15f, .1f, .735f, .94f); StyleText(text, 27, Ivory, TextAnchor.MiddleLeft); }
            var owned = row.transform.Find("OwnedStatus")?.GetComponent<TextMeshProUGUI>();
            if (owned != null) { Rect(owned.rectTransform, .75f, .1f, .975f, .9f); owned.font = ExpeditionTheme.Font; owned.fontSize = 22; owned.enableAutoSizing = true; owned.fontSizeMin = 18; owned.fontSizeMax = 22; }
        }

        public static void SetRecipeArtwork(GameObject row, Sprite icon, ItemQualityTier quality)
        {
            if (row == null) return;
            SetRarityBorder(row.transform, quality);
            var frame = row.transform.Find("RecipeIconFrame")?.GetComponent<Image>();
            if (frame == null) frame = Plate(row.transform, "RecipeIconFrame", .022f, .12f, .125f, .88f);
            SetRarityBorder(frame.transform, quality);
            var preview = frame.transform.Find("RecipeResultIcon")?.GetComponent<Image>();
            if (preview == null) preview = Image(frame.transform, "RecipeResultIcon", Color.white, .07f, .07f, .93f, .93f);
            preview.sprite = icon;
            preview.preserveAspect = true;
            preview.enabled = icon != null;
        }

        private static void SetRarityBorder(Transform parent, ItemQualityTier quality)
        {
            var border = parent.Find("RarityBorder")?.GetComponent<Image>();
            if (border == null) border = Image(parent, "RarityBorder", Color.white, 0, 0, 1, 1);
            ExpeditionUi.StylePanel(border, EquipmentSlotView.QualitySlotColor(quality));
            border.color = EquipmentSlotView.QualitySlotColor(quality);
            border.fillCenter = false;
            border.pixelsPerUnitMultiplier = 7;
            border.raycastTarget = false;
            border.transform.SetAsLastSibling();
        }

        public void SetQueueState(int index, bool occupied, bool ready, float progress = 0f)
        {
            if (index < 0 || index >= 8) return;
            _occupied[index] = occupied; _ready[index] = ready;
            if (_slots[index] != null) _slots[index].color = ready ? new Color(.93f, 1f, .87f) : Color.white;
            if (_emptyMarks[index] != null) _emptyMarks[index].gameObject.SetActive(!occupied);
            if (_timers[index] != null) _timers[index].color = ready ? new Color32(176, 210, 139, 255) : occupied ? Ivory : Muted;
            if (_progress[index] != null)
            {
                _progress[index].transform.parent.gameObject.SetActive(occupied && !ready);
                var rect = _progress[index].rectTransform;
                rect.anchorMax = new Vector2(Mathf.Clamp01(progress), .9f);
            }
            var count = 0; var finished = 0;
            for (var i = 0; i < 8; i++) { if (_occupied[i]) count++; if (_ready[i]) finished++; }
            if (_queueTitle != null) _queueTitle.text = "Очередь " + count + "/8";
            if (_claimReady != null) _claimReady.interactable = finished > 0;
        }

        public static bool TryApplyRecipePanel(RectTransform background)
        {
            var view = background != null ? background.GetComponent<ExpeditionWorkshopLayout>() : null;
            if (view == null || !view._built) return false;
            view.ApplyRecipePanelLayout(); return true;
        }

        public static void StyleButton(Button button, bool primary)
        {
            ExpeditionUi.StyleButton(button, primary, 25);
            foreach (var text in button.GetComponentsInChildren<Text>(true))
                StyleText(text, 25, primary && button.IsInteractable() ? Ink : Ivory, TextAnchor.MiddleCenter);
        }
        private static Transform Find(Transform root, string name) => ExpeditionUi.Find(root, name);
        private static void StyleSlotText(Text text, int size, Color color)
        {
            StyleText(text, size, color, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Normal;
            text.lineSpacing = 1f;
            text.alignByGeometry = true;
            text.resizeTextMinSize = 13;
            text.resizeTextMaxSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }
        private static void StyleText(Text text, int size, Color color, TextAnchor alignment)
        {
            text.font = ExpeditionTheme.LegacyFont; text.color = color; text.fontSize = size; text.alignment = alignment;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Max(16, size - 5); text.resizeTextMaxSize = size;
        }
        private static Image Plate(Transform parent, string name, float x0, float y0, float x1, float y1) => ExpeditionUi.StylePanel(Image(parent, name, Color.white, x0, y0, x1, y1), Panel);
        private static Image Image(Transform parent, string name, Color color, float x0, float y0, float x1, float y1)
        {
            var image = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        private static Text Text(Transform parent, string name, string value, int size, Color color, TextAnchor align, float x0, float y0, float x1, float y1, bool bold = false)
        {
            var text = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<UnityEngine.UI.Text>();
            text.text = value; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; StyleText(text, size, color, align); return text;
        }
        private static Button Button(Transform parent, string name, string value, bool primary, float x0, float y0, float x1, float y1)
        {
            var image = Image(parent, name, Color.white, x0, y0, x1, y1); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            Text(button.transform, "Label", value, 24, primary ? Ink : Ivory, TextAnchor.MiddleCenter, .025f, .04f, .975f, .96f, true);
            StyleButton(button, primary);
            return button;
        }
        private static void Rect(RectTransform rect, float x0, float y0, float x1, float y1) => ExpeditionUi.SetRect(rect, x0, y0, x1, y1);
    }
}
