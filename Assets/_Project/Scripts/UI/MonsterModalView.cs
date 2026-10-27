using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Preserves the prefab's gameplay references and presents a contextual expedition dossier sheet.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class MonsterModalView : MonoBehaviour
    {
        [SerializeField] private float widthOverHeight = 1.16f;
        [SerializeField] private float screenEdgeMargin = 72f;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text supplementalInfoText;
        [SerializeField] private GameObject monsterContentRoot, barrierContentRoot;
        [SerializeField] private Text characteristicsSectionTitle;
        [SerializeField] private Text[] statTexts = new Text[6];
        [SerializeField] private Text rewardsSectionTitle;
        [SerializeField] private RectTransform rewardsDynamicRoot;
        [SerializeField] private Font monsterRewardsValueFont;
        [SerializeField] private Text anomalySectionTitle;
        [SerializeField] private Image affixIcon;
        [SerializeField] private Text affixIconGlyph, affixTitleText, affixDescriptionText;
        [SerializeField] private Text barrierSectionTitle, barrierInfoText, barrierRequirementsSectionTitle;
        [SerializeField] private RectTransform barrierRequirementsRoot;
        [SerializeField] private Button fightButton, dismissButton;
        private bool _expeditionStyle, _barrierMode;
        private RectTransform _sheet;
        private Image _portrait, _sceneMonster, _sceneGate;
        private Text _floorLabel;
        private Coroutine _lateStyleRefresh;
        private ScrollRect _affixDescriptionScroll, _barrierRequirementsScroll;
        public Image BackgroundImage => backgroundImage;
        public Text TitleText => titleText;
        public Button CloseButton => closeButton;
        public Text SupplementalInfoText => supplementalInfoText;
        public GameObject MonsterContentRoot => monsterContentRoot;
        public GameObject BarrierContentRoot => barrierContentRoot;
        public Text CharacteristicsSectionTitle => characteristicsSectionTitle;
        public Text[] StatTexts => statTexts;
        public Text RewardsSectionTitle => rewardsSectionTitle;
        public RectTransform RewardsDynamicRoot => rewardsDynamicRoot;
        public Font MonsterRewardsValueFont => monsterRewardsValueFont;
        public Text AnomalySectionTitle => anomalySectionTitle;
        public Image AffixIcon => affixIcon;
        public Text AffixIconGlyph => affixIconGlyph;
        public Text AffixTitleText => affixTitleText;
        public Text AffixDescriptionText => affixDescriptionText;
        public Text BarrierSectionTitle => barrierSectionTitle;
        public Text BarrierInfoText => barrierInfoText;
        public Text BarrierRequirementsSectionTitle => barrierRequirementsSectionTitle;
        public RectTransform BarrierRequirementsRoot => barrierRequirementsRoot;
        public Button FightButton => fightButton;
        public Button DismissButton => dismissButton;
        public bool IsExpeditionStyle => _expeditionStyle;

        private void OnEnable() { StartCoroutine(CoApplyModalAspect()); }
        private IEnumerator CoApplyModalAspect()
        {
            yield return null;
            ApplyModalAspect();
            if (_expeditionStyle) ApplyDynamicStyles();
        }
        private void OnDisable() { _lateStyleRefresh = null; }
        private void OnRectTransformDimensionsChange() { ApplyModalAspect(); }

        public void ApplyExpeditionStyle()
        {
            if (_expeditionStyle) return;
            _expeditionStyle = true;
            // Disable the legacy column geometry, then move the same bound controls into the new sheet.
            foreach (var group in GetComponentsInChildren<LayoutGroup>(true)) group.enabled = false;
            foreach (var fit in GetComponentsInChildren<ContentSizeFitter>(true)) fit.enabled = false;
            // An overlay shell always draws after a camera canvas, regardless of nested sortingOrder.
            // Detach first so this becomes a real overlay root while the controller keeps its view reference.
            transform.SetParent(null, false);
            var canvas = ExpeditionUi.Ensure<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = 240;
            var scaler = ExpeditionUi.Ensure<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            ExpeditionUi.Ensure<GraphicRaycaster>(gameObject);
            var scrim = NewImage(transform, "ExpeditionDossierScrim", new Color(.005f, .02f, .025f, .26f), 0, 0, 1, 1);
            scrim.transform.SetAsFirstSibling();
            scrim.raycastTarget = true;
            var dismissScrim = scrim.gameObject.AddComponent<Button>();
            dismissScrim.transition = Selectable.Transition.None;
            dismissScrim.onClick.AddListener(() => closeButton?.onClick.Invoke());
            var safe = ExpeditionUi.Rect(transform, "DossierSafeArea", 0, 0, 1, 1);
            safe.gameObject.AddComponent<ExpeditionSafeArea>();
            var environment = NewImage(safe, "SelectedLandingPreview", Color.white, 0, .563f, 1, .929f);
            environment.sprite = Resources.Load<Sprite>("Expedition/Art/mine-encounter-v2");
            environment.gameObject.AddComponent<RectMask2D>();
            _sceneMonster = NewImage(environment.transform, "EncounterMonster", Color.white, .24f, .30f, .65f, .92f);
            _sceneMonster.preserveAspect = true;
            var explorer = NewImage(environment.transform, "Explorer", Color.white, .06f, .06f, .28f, .66f);
            explorer.sprite = Resources.Load<Sprite>("Expedition/Art/hero"); explorer.preserveAspect = true;
            Project.Character.UI.CharacterAppearanceImage.Bind(explorer);
            _sceneGate = NewImage(environment.transform, "EncounterGate", Color.white, .77f, .73f, .99f, .995f);
            _sceneGate.sprite = Resources.Load<Sprite>("Expedition/Art/mine-gate-v2"); _sceneGate.preserveAspect = true;
            ExpeditionLocalAtmosphere.Install(environment.transform, false);
            // The painted frame has a translucent centre. An independent solid backing must be
            // rendered behind it so the selected-floor controls can never bleed through the sheet.
            var backing = NewImage(safe, "DossierOpaqueBacking", new Color32(8, 24, 29, 255), .02f, .115f, .98f, .572f);
            backing.raycastTarget = true;
            _sheet = backgroundImage != null ? backgroundImage.rectTransform : ExpeditionUi.Rect(safe, "DossierSheet", .015f, .112f, .985f, .574f);
            _sheet.SetParent(safe, false);
            Rect(_sheet, .015f, .112f, .985f, .574f);
            if (backgroundImage == null) backgroundImage = _sheet.gameObject.AddComponent<Image>();
            ExpeditionUi.StylePanel(backgroundImage, ExpeditionTheme.Panel);
            backgroundImage.raycastTarget = true;
            var portraitFrame = ExpeditionUi.Panel(_sheet, "MonsterPortraitFrame", .035f, .49f, .405f, .945f);
            // Clip to the inner well, rather than the outer bevel: even a tall original sprite
            // must stay behind the frame and may never cover its bottom edge with its feet.
            var portraitWell = ExpeditionUi.Rect(portraitFrame.transform, "PortraitWell", .065f, .055f, .935f, .945f);
            portraitWell.gameObject.AddComponent<RectMask2D>();
            _portrait = NewImage(portraitWell, "MonsterPortrait", Color.white, 0, .015f, 1, .985f);
            _portrait.preserveAspect = true;
            Place(titleText, _sheet, .435f, .875f, .95f, .957f, 36, ExpeditionTheme.Ivory);
            _floorLabel = NewText(_sheet, "EncounterLocation", "", 24, ExpeditionTheme.Muted, .435f, .815f, .95f, .873f);
            if (monsterContentRoot != null)
            {
                monsterContentRoot.transform.SetParent(_sheet, false);
                Rect((RectTransform)monsterContentRoot.transform, 0, 0, 1, 1);
            }
            if (barrierContentRoot != null)
            {
                barrierContentRoot.transform.SetParent(_sheet, false);
                Rect((RectTransform)barrierContentRoot.transform, 0, 0, 1, 1);
            }
            var monster = monsterContentRoot != null ? monsterContentRoot.transform : _sheet;
            if (characteristicsSectionTitle != null) characteristicsSectionTitle.gameObject.SetActive(false);
            if (anomalySectionTitle != null) anomalySectionTitle.gameObject.SetActive(false);
            for (var i = 0; i < statTexts.Length; i++)
            {
                if (statTexts[i] == null) continue;
                if (i < 3)
                {
                    var x = .43f + i * .181f;
                    var plate = ExpeditionUi.Panel(monster, "StatChip_" + i, x, .69f, x + .171f, .81f);
                    var icon = NewImage(plate.transform, "StatIcon", Color.white, .09f, .25f, .255f, .75f);
                    icon.sprite = ExpeditionTheme.Icon(i == 0 ? "heart" : i == 1 ? "swords" : "shield");
                    icon.preserveAspect = true;
                    Place(statTexts[i], plate.transform, .32f, .18f, .9f, .82f, 21, ExpeditionTheme.Ivory);
                }
                else
                {
                    var x = .43f + (i - 3) * .181f;
                    Place(statTexts[i], monster, x, .637f, x + .175f, .683f, 21, ExpeditionTheme.Muted);
                }
            }
            var affix = ExpeditionUi.Panel(monster, "AnomalyCard", .43f, .46f, .965f, .63f);
            Place(affixIcon, affix.transform, .055f, .22f, .20f, .78f);
            Place(affixTitleText, affix.transform, .245f, .65f, .935f, .88f, 24, ExpeditionTheme.Amber);
            _affixDescriptionScroll = BuildScrollingDescription(affix.transform, affixDescriptionText);
            if (affixDescriptionText != null)
            {
                affixDescriptionText.fontStyle = FontStyle.Normal;
                affixDescriptionText.alignment = TextAnchor.UpperLeft;
            }
            Place(rewardsSectionTitle, monster, .05f, .412f, .95f, .458f, 28, ExpeditionTheme.Ivory);
            BuildScrollingValues(monster, rewardsDynamicRoot, "RewardsViewport", .05f, .23f, .95f, .405f);
            var barrier = barrierContentRoot != null ? barrierContentRoot.transform : _sheet;
            if (barrierSectionTitle != null) barrierSectionTitle.gameObject.SetActive(false);
            Place(barrierInfoText, barrier, .435f, .723f, .95f, .803f, 23, ExpeditionTheme.Muted);
            // The legacy prefab explicitly hides this otherwise populated field.
            if (barrierInfoText != null) barrierInfoText.gameObject.SetActive(true);
            Place(barrierRequirementsSectionTitle, barrier, .45f, .651f, .94f, .712f, 27, ExpeditionTheme.Ivory);
            if (barrierRequirementsSectionTitle != null) barrierRequirementsSectionTitle.text = "Требования";
            _barrierRequirementsScroll = BuildScrollingValues(barrier, barrierRequirementsRoot, "BarrierRequirementsViewport", .435f, .235f, .95f, .64f);
            AttachScrollBar(_barrierRequirementsScroll, barrier, "BarrierRequirementsScrollBar", .957f, .235f, .969f, .64f);
            NewText(barrier, "GateCaption", "ПУТЬ ЗАКРЫТ", 23, ExpeditionTheme.Amber, .065f, .37f, .38f, .425f).alignment = TextAnchor.MiddleCenter;
            Place(supplementalInfoText, _sheet, .43f, .132f, .965f, .22f, 22, new Color32(221, 157, 120, 255));
            Place(fightButton, _sheet, .035f, .035f, .672f, .12f);
            Place(dismissButton, _sheet, .035f, .135f, .405f, .215f);
            Place(closeButton, _sheet, .70f, .035f, .965f, .12f);
            ExpeditionUi.StyleButton(fightButton, true, 29);
            ExpeditionUi.StyleButton(dismissButton, false, 23);
            ExpeditionUi.StyleButton(closeButton, false, 28);
            var closeLabel = closeButton != null ? closeButton.GetComponentInChildren<Text>(true) : null;
            if (closeLabel != null) closeLabel.text = "Закрыть";
            monsterRewardsValueFont = ExpeditionTheme.LegacyFont;
            ApplyModalAspect();
        }

        /// <summary>Only view data; existing controller continues to own costs, cooldowns and all callbacks.</summary>
        public void SetExpeditionContext(Sprite monster, int floor, string difficulty, bool barrier)
        {
            if (!_expeditionStyle) return;
            _barrierMode = barrier;
            ResetScrollToTop(_affixDescriptionScroll);
            if (barrier) ResetScrollToTop(_barrierRequirementsScroll);
            _floorLabel.text = "Этаж " + floor + " · " + difficulty;
            _portrait.sprite = barrier ? Resources.Load<Sprite>("Expedition/Art/mine-gate-v2") : monster;
            _portrait.enabled = _portrait.sprite != null;
            Rect(_portrait.rectTransform, 0, .015f, 1, .985f);
            _sceneMonster.sprite = monster;
            _sceneMonster.enabled = !barrier && monster != null;
            ExpeditionDifficultyAura.Set(_portrait, difficulty, !barrier && monster != null);
            ExpeditionDifficultyAura.Set(_sceneMonster, difficulty, !barrier && monster != null);
            _sceneGate.gameObject.SetActive(barrier);
            if (dismissButton != null) Rect(dismissButton.transform as RectTransform, .035f, barrier ? .035f : .135f, barrier ? .672f : .405f, barrier ? .12f : .215f);
            if (dismissButton != null) ExpeditionUi.StyleButton(dismissButton, barrier, barrier ? 29 : 23);
            // The controller fills text/costs after this call, including when an already-open
            // barrier becomes a monster dossier. Reapply the view once those writes settle.
            RequestLateStyleRefresh();
        }

        public void RefreshDynamicStyles()
        {
            if (!_expeditionStyle) return;
            ApplyDynamicStyles();
            RequestLateStyleRefresh();
        }

        private void RequestLateStyleRefresh()
        {
            if (isActiveAndEnabled && _lateStyleRefresh == null)
                _lateStyleRefresh = StartCoroutine(CoRefreshDynamicStyles());
        }

        private IEnumerator CoRefreshDynamicStyles()
        {
            yield return null;
            _lateStyleRefresh = null;
            if (_expeditionStyle) ApplyDynamicStyles();
        }

        private void ApplyDynamicStyles()
        {
            if (rewardsDynamicRoot != null)
            {
                foreach (var row in rewardsDynamicRoot.GetComponentsInChildren<HorizontalLayoutGroup>(true))
                {
                    row.spacing = 14; row.padding = new RectOffset(0, 0, 0, 0);
                    row.childControlHeight = true; row.childForceExpandHeight = true;
                    var le = row.GetComponent<LayoutElement>();
                    if (le != null) { le.minHeight = 148; le.preferredHeight = 148; }
                }
                foreach (var cell in rewardsDynamicRoot.GetComponentsInChildren<VerticalLayoutGroup>(true))
                {
                    if (cell.transform == rewardsDynamicRoot) continue;
                    // Let the row size each cell, but keep its icon and value in a padded inner well.
                    cell.enabled = false;
                    var image = ExpeditionUi.Ensure<Image>(cell.gameObject);
                    ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel); image.raycastTarget = false;
                    var icon = cell.transform.Find("Icon")?.GetComponent<Image>();
                    if (icon != null)
                    {
                        Rect(icon.rectTransform, .20f, .37f, .80f, .87f);
                        icon.preserveAspect = true; icon.raycastTarget = false;
                    }
                    var value = cell.transform.Find("Value")?.GetComponent<Text>();
                    if (value != null)
                    {
                        Rect(value.rectTransform, .08f, .11f, .92f, .36f);
                        value.font = ExpeditionTheme.LegacyFont; value.fontSize = 28;
                        value.alignment = TextAnchor.MiddleCenter;
                        value.horizontalOverflow = HorizontalWrapMode.Overflow;
                        value.verticalOverflow = VerticalWrapMode.Truncate;
                        value.resizeTextForBestFit = true; value.resizeTextMinSize = 21; value.resizeTextMaxSize = 28;
                        value.raycastTarget = false;
                    }
                }
            }
            StyleBarrierRequirements();
            StyleFooterButton(fightButton, true, false);
            StyleFooterButton(dismissButton, _barrierMode, _barrierMode);
            if (barrierRequirementsSectionTitle != null) barrierRequirementsSectionTitle.text = "Требования";
            foreach (var text in GetComponentsInChildren<Text>(true)) if (text.font != ExpeditionTheme.LegacyFont && ExpeditionTheme.LegacyFont != null) text.font = ExpeditionTheme.LegacyFont;
        }

        private void StyleBarrierRequirements()
        {
            if (barrierRequirementsRoot == null) return;
            var column = barrierRequirementsRoot.GetComponent<VerticalLayoutGroup>();
            if (column != null) { column.spacing = 12; column.padding = new RectOffset(0, 0, 0, 8); }
            for (var i = 0; i < barrierRequirementsRoot.childCount; i++)
            {
                var row = barrierRequirementsRoot.GetChild(i);
                if (row.name != "RequirementRow") continue;
                var layout = row.GetComponent<HorizontalLayoutGroup>();
                if (layout != null) layout.enabled = false;
                var bullet = row.Find("Text");
                if (bullet != null) bullet.gameObject.SetActive(false);
                var image = ExpeditionUi.Ensure<Image>(row.gameObject);
                ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel); image.raycastTarget = false;
                var icon = row.Find("Icon")?.GetComponent<Image>();
                var value = row.Find("Value")?.GetComponent<Text>();
                var le = row.GetComponent<LayoutElement>();
                if (le != null) { le.minHeight = 92; le.preferredHeight = 92; le.flexibleHeight = 0; }
                if (icon != null)
                {
                    Rect(icon.rectTransform, .065f, .24f, .18f, .76f);
                    icon.preserveAspect = true; icon.raycastTarget = false;
                }
                if (value == null) continue;
                Rect(value.rectTransform, icon != null ? .245f : .085f, .19f, .915f, .81f);
                value.font = ExpeditionTheme.LegacyFont; value.fontSize = icon != null ? 28 : 24;
                value.alignment = TextAnchor.MiddleLeft;
                value.horizontalOverflow = HorizontalWrapMode.Wrap;
                value.verticalOverflow = VerticalWrapMode.Truncate;
                value.resizeTextForBestFit = true; value.resizeTextMinSize = 21;
                value.resizeTextMaxSize = icon != null ? 28 : 24;
                value.raycastTarget = false;
            }
        }

        private static void StyleFooterButton(Button button, bool primary, bool captionOnly)
        {
            if (button == null) return;
            ExpeditionUi.StyleButton(button, primary, primary ? 29 : 23);
            var row = button.transform.Find("MineFooterRow");
            if (row == null) return;
            var oldLayout = row.GetComponent<HorizontalLayoutGroup>();
            if (oldLayout != null) oldLayout.enabled = false;
            var prefix = row.Find("Prefix")?.GetComponent<Text>();
            if (prefix != null)
            {
                var fit = prefix.GetComponent<ContentSizeFitter>();
                if (fit != null) fit.enabled = false;
                Rect(prefix.rectTransform, .06f, .15f, captionOnly ? .94f : primary ? .68f : .55f, .85f);
                prefix.alignment = TextAnchor.MiddleCenter;
                prefix.horizontalOverflow = HorizontalWrapMode.Overflow;
                prefix.verticalOverflow = VerticalWrapMode.Truncate;
                prefix.resizeTextForBestFit = true; prefix.resizeTextMinSize = primary ? 23 : 19;
                prefix.resizeTextMaxSize = primary ? 29 : 23;
            }
            var strip = row.Find("CostStrip") as RectTransform;
            if (strip != null)
            {
                var fit = strip.GetComponent<ContentSizeFitter>();
                if (fit != null) fit.enabled = false;
                Rect(strip, primary ? .70f : .56f, .15f, .94f, .85f);
            }
            // Barrier controller restores a legacy pale caption after SetExpeditionContext.
            // Reconfigure against the actual populated labels, including disabled/busy states.
            button.GetComponent<ExpeditionButtonFeedback>()?.Configure(primary);
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                if (label.name == "Amt") label.fontSize = primary ? 24 : 21;
                label.color = primary && button.IsInteractable() ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory;
            }
        }

        private static ScrollRect BuildScrollingValues(Transform parent, RectTransform content, string name, float x0, float y0, float x1, float y1)
        {
            if (content == null) return null;
            var viewport = NewImage(parent, name, Color.clear, x0, y0, x1, y1);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.viewport = viewport.rectTransform;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
            content.SetParent(viewport.transform, false);
            Rect(content, 0, 1, 1, 1); content.pivot = new Vector2(.5f, 1);
            var layout = ExpeditionUi.Ensure<VerticalLayoutGroup>(content.gameObject);
            layout.enabled = true; layout.spacing = 7; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(content.gameObject);
            fitter.enabled = true; fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            return scroll;
        }

        private static ScrollRect BuildScrollingDescription(Transform parent, Text description)
        {
            if (description == null) return null;
            var viewport = NewImage(parent, "AnomalyDescriptionViewport", Color.clear, .245f, .14f, .90f, .61f);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.viewport = viewport.rectTransform;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
            Place(description, viewport.transform, 0, 1, 1, 1, 21, ExpeditionTheme.Muted);
            description.rectTransform.pivot = new Vector2(.5f, 1);
            // A fixed readable size and preferred text height keep every line reachable;
            // best-fit in a fixed box can still truncate once it reaches its minimum size.
            description.resizeTextForBestFit = false;
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Overflow;
            description.alignment = TextAnchor.UpperLeft;
            var legacySize = description.GetComponent<LayoutElement>();
            if (legacySize != null) legacySize.enabled = false;
            var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(description.gameObject);
            fitter.enabled = true; fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = description.rectTransform;
            AttachScrollBar(scroll, parent, "AnomalyDescriptionScrollBar", .917f, .14f, .937f, .61f);
            return scroll;
        }

        private static void AttachScrollBar(ScrollRect scroll, Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            if (scroll == null) return;
            var track = NewImage(parent, name, new Color32(5, 18, 22, 230), x0, y0, x1, y1);
            track.raycastTarget = true;
            var area = ExpeditionUi.Rect(track.transform, "SlidingArea", .15f, .025f, .85f, .975f);
            var handle = NewImage(area, "Handle", ExpeditionTheme.Amber, 0, 0, 1, 1);
            handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.transition = Selectable.Transition.None;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static void ResetScrollToTop(ScrollRect scroll)
        {
            if (scroll == null || scroll.content == null) return;
            scroll.StopMovement();
            // Both content roots are top anchored: this also works before their new preferred
            // height has been calculated after the controller replaces text or requirement rows.
            scroll.content.anchoredPosition = Vector2.zero;
        }

        private static void Place(Component component, Transform parent, float x0, float y0, float x1, float y1)
        {
            if (component == null) return;
            component.transform.SetParent(parent, false);
            Rect(component.transform as RectTransform, x0, y0, x1, y1);
        }
        private static void Place(Text text, Transform parent, float x0, float y0, float x1, float y1, int size, Color color)
        {
            if (text == null) return;
            Place((Component)text, parent, x0, y0, x1, y1);
            text.font = ExpeditionTheme.LegacyFont; text.color = color; text.fontSize = size; text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Max(17, size - 6); text.resizeTextMaxSize = size;
        }
        private static Image NewImage(Transform parent, string name, Color color, float x0, float y0, float x1, float y1)
        {
            var image = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        private static Text NewText(Transform parent, string name, string value, int size, Color color, float x0, float y0, float x1, float y1)
        {
            var text = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Text>();
            text.text = value; Place(text, parent, x0, y0, x1, y1, size, color); return text;
        }
        private static void Rect(RectTransform rect, float x0, float y0, float x1, float y1) => ExpeditionUi.SetRect(rect, x0, y0, x1, y1);
        private void ApplyModalAspect()
        {
            var rt = (RectTransform)transform;
            if (_expeditionStyle)
            {
                // Root overlay dimensions are driven by Canvas/CanvasScaler, including resolution changes.
                if (rt.parent != null) Rect(rt, 0, 0, 1, 1);
                return;
            }
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            var maxW = Mathf.Max(0, parent.rect.width - screenEdgeMargin * 2);
            var maxH = Mathf.Max(0, parent.rect.height - screenEdgeMargin * 2);
            if (maxW <= 1 || maxH <= 1) return;
            var w = Mathf.Min(maxW, maxH * widthOverHeight);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(w, w / Mathf.Max(.01f, widthOverHeight)); rt.anchoredPosition = Vector2.zero;
        }
    }
}
