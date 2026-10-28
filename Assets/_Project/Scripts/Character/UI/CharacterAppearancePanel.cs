using System.Text;
using System.Threading;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>The catalog remains primary; a selected skin opens a complete, server-authorized dossier.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearancePanel : MonoBehaviour
    {
        private RectTransform _root, _viewport, _content, _detailsRoot, _detailsSheet, _detailsContent;
        private ScrollRect _scroll;
        private Image _body, _portrait;
        private TMP_Text _title, _description, _requirements, _reward, _collectionBonus, _status, _applyLabel, _catalogSummary;
        private Button _apply, _restore, _refresh;
        private readonly Button[] _cards = new Button[CharacterAppearance.Themes.Count];
        private readonly Image[] _cardPortraits = new Image[CharacterAppearance.Themes.Count];
        private readonly TMP_Text[] _cardProgress = new TMP_Text[CharacterAppearance.Themes.Count];
        private int _theme;
        private Vector2 _lastViewportSize, _lastDetailsSize;
        private bool _applyingGrid;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool DetailsOpen => _detailsRoot != null && _detailsRoot.gameObject.activeSelf;
        public int GridColumns { get; private set; } = 3;
        public string PreviewId => CharacterAppearance.Id(_theme);

        public void Build(RectTransform parent)
        {
            if (_root != null) return;
            _root = ExpeditionUi.Rect(parent, "ExpeditionAppearancePage", .018f, .012f, .982f, .931f);
            var backdrop = ExpeditionUi.Panel(_root, "AppearanceBackdrop", 0, 0, 1, 1);
            ExpeditionUi.Fill(backdrop.transform, "InnerWell", .018f, .01f, .982f, .99f, new Color32(8, 25, 30, 235));
            var heading = Text(_root, "AppearanceCatalogTitle", "ОБРАЗЫ ГЕРОЯ", 46, .045f, .924f, .955f, .98f, ExpeditionTheme.Ivory);
            heading.fontStyle = FontStyles.Bold;
            _catalogSummary = Text(_root, "AppearanceCatalogSummary", "", 29, .045f, .862f, .955f, .925f, ExpeditionTheme.Muted);
            _viewport = ExpeditionUi.Rect(_root, "AppearanceGridViewport", .035f, .105f, .952f, .85f);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var hit = _viewport.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, .001f); hit.raycastTarget = true;
            _content = ExpeditionUi.Rect(_viewport, "AppearanceGridContent", 0, 0, 1, 1);
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = _viewport; _scroll.content = _content;
            _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 55;
            var track = ExpeditionUi.Panel(_root, "AppearanceGridScrollbar", .96f, .105f, .98f, .85f).rectTransform;
            var handle = ExpeditionUi.Panel(track, "Handle", .12f, 0, .88f, 1, ExpeditionTheme.Amber);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; _scroll.verticalScrollbar = scrollbar;
            for (var i = 0; i < _cards.Length; i++)
            {
                var index = i;
                var card = ExpeditionUi.Button(_content, "AppearanceTheme" + i, CharacterAppearance.Themes[i].Name, 0, 0, 1, 1, () => SelectTheme(index), false, 32);
                var label = card.GetComponentInChildren<TMP_Text>();
                ExpeditionUi.SetRect(label.rectTransform, .045f, .15f, .955f, .44f);
                label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = 32; label.margin = Vector4.zero;
                var picture = ExpeditionUi.Fill(card.transform, "PairedPortrait", .105f, .47f, .895f, .945f, Color.white); picture.preserveAspect = true;
                var progress = ExpeditionUi.Text(card.transform, "AppearanceCardProgress", "", 27, .045f, .025f, .955f, .15f, TextAlignmentOptions.Center);
                progress.enableAutoSizing = true; progress.fontSizeMin = 25; progress.fontSizeMax = 27; progress.margin = Vector4.zero;
                _cards[i] = card; _cardPortraits[i] = picture; _cardProgress[i] = progress;
            }
            _restore = ExpeditionUi.Button(_root, "RestoreClassicAppearance", "Исходный образ", .045f, .025f, .615f, .083f, RestoreDefault, false, 36);
            _refresh = ExpeditionUi.Button(_root, "RefreshAppearances", "Обновить", .64f, .025f, .955f, .083f, RefreshFromServer, false, 36);
            foreach (var button in new[] { _restore, _refresh })
            {
                var label = button.GetComponentInChildren<TMP_Text>();
                ExpeditionUi.SetRect(label.rectTransform, .06f, .11f, .94f, .89f);
                label.margin = Vector4.zero; label.alignment = TextAlignmentOptions.Midline;
                label.enableAutoSizing = true; label.fontSizeMin = 32; label.fontSizeMax = 36; label.fontSize = 36;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            BuildDetails();
            _root.gameObject.SetActive(false);
        }

        private void BuildDetails()
        {
            _detailsRoot = ExpeditionUi.Rect(_root, "AppearanceDetailsModal", 0, 0, 1, 1);
            var shade = ExpeditionUi.Fill(_detailsRoot, "Shade", 0, 0, 1, 1, new Color(0, 0, 0, .82f)); shade.raycastTarget = true;
            shade.gameObject.AddComponent<Button>().onClick.AddListener(CloseDetails);
            _detailsSheet = ExpeditionUi.Panel(_detailsRoot, "AppearanceDetailsSheet", .018f, .018f, .982f, .982f).rectTransform;
            _detailsSheet.GetComponent<Image>().raycastTarget = true;
            _body = ExpeditionUi.Fill(_detailsSheet, "AppearanceBodyPreview", .04f, .755f, .30f, .90f, Color.white); _body.preserveAspect = true;
            var frame = ExpeditionUi.Panel(_detailsSheet, "AppearancePortraitFrame", .82f, .79f, .95f, .89f);
            _portrait = ExpeditionUi.Fill(frame.transform, "AppearanceAvatarPreview", .055f, .055f, .945f, .945f, Color.white); _portrait.preserveAspect = true;
            _title = Text(_detailsSheet, "AppearanceTitle", "", 52, .05f, .907f, .90f, .978f, ExpeditionTheme.Ivory); _title.fontStyle = FontStyles.Bold;
            _description = Text(_detailsSheet, "AppearanceDescription", "", 34, .325f, .765f, .80f, .90f, ExpeditionTheme.Muted);
            _detailsContent = ExpeditionUi.Rect(_detailsSheet, "AppearanceDetailsContent", .05f, .165f, .95f, .746f);
            _requirements = DetailText("AppearanceRequirements", 36, ExpeditionTheme.Ivory); _requirements.richText = false;
            _reward = DetailText("AppearanceUnlockReward", 34, ExpeditionTheme.Amber);
            _status = DetailText("AppearanceStatus", 30, ExpeditionTheme.Muted);
            _collectionBonus = DetailText("AppearanceCollectionBonus", 30, ExpeditionTheme.Ivory);
            _apply = ExpeditionUi.Button(_detailsSheet, "ApplyAppearance", "ПРИМЕНИТЬ ОБРАЗ", .05f, .052f, .95f, .125f, ApplyPreview, true, 46);
            _applyLabel = _apply.GetComponentInChildren<TMP_Text>();
            ExpeditionUi.Button(_detailsSheet, "CloseAppearanceDetails", "×", .905f, .959f, .974f, .993f, CloseDetails, false, 40);
            _detailsRoot.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (Application.isPlaying)
            {
                if (!AccountGender.IsLoaded) _ = AccountGender.RefreshAsync(CancellationToken.None);
                _ = CharacterAppearanceService.RefreshAsync(CancellationToken.None, true);
            }
            var selected = CharacterAppearance.BodyId;
            for (var i = 0; i < CharacterAppearance.Themes.Count; i++) if (CharacterAppearance.Id(i) == selected) { _theme = i; break; }
            _root.gameObject.SetActive(true); CloseDetails(); Canvas.ForceUpdateCanvases(); Refresh(); ScrollToSelected();
        }
        public void Hide() { CloseDetails(); if (_root != null) _root.gameObject.SetActive(false); }
        public void CloseDetails() { if (_detailsRoot != null) _detailsRoot.gameObject.SetActive(false); }
        public void SelectTheme(int index)
        {
            _theme = Mathf.Clamp(index, 0, CharacterAppearance.Themes.Count - 1);
            _detailsRoot.gameObject.SetActive(true); Refresh(); _scroll.StopMovement();
        }
        public async void ApplyPreview() { await CharacterAppearance.ApplyBodyAsync(PreviewId); if (this != null) Refresh(); }
        private async void RestoreDefault() { await CharacterAppearance.RestoreDefaultAsync(); if (this != null) Refresh(); }
        private void RefreshFromServer() { if (Application.isPlaying) _ = CharacterAppearanceService.RefreshAsync(CancellationToken.None, true); }
        private void OnEnable() { CharacterAppearance.Changed += Refresh; AccountGender.Changed += Refresh; CharacterAppearanceService.Changed += Refresh; }
        private void OnDisable() { CharacterAppearance.Changed -= Refresh; AccountGender.Changed -= Refresh; CharacterAppearanceService.Changed -= Refresh; }
        private void LateUpdate()
        {
            if (_viewport != null && _lastViewportSize != _viewport.rect.size) RefreshGridLayout();
            if (DetailsOpen && _lastDetailsSize != _detailsContent.rect.size) RefreshDetailsLayout();
        }

        private void Refresh()
        {
            if (_root == null) return;
            var definition = CharacterAppearance.Themes[_theme];
            var state = CharacterAppearanceService.GetTheme(definition.Id);
            var known = AccountGender.IsLoaded;
            _body.sprite = CharacterAppearance.Load(PreviewId, false); _body.enabled = known && _body.sprite != null;
            _portrait.sprite = CharacterAppearance.Load(PreviewId, true); _portrait.enabled = known && _portrait.sprite != null;
            _title.text = definition.Name; _description.text = definition.Description;
            _requirements.text = Requirements(state);
            _reward.text = state == null ? "Награды загружаются…" : state.id == "dawnwarden" ? "Начальный образ · без награды за открытие" :
                (state.reward_granted ? "Получено: " : "За открытие: ") + CharacterAppearanceService.FormatThemeReward(state) +
                "\n" + CharacterAppearanceService.FormatThemeBonus(state);
            _collectionBonus.text = CharacterAppearanceService.FormatCollectionBonus();
            var applied = CharacterAppearance.BodyId == PreviewId;
            var unlocked = state?.unlocked == true;
            var artReady = _body.sprite != null && _portrait.sprite != null;
            var error = CharacterAppearanceService.LastError;
            _status.text = !known ? "Загружаем героя…" : !CharacterAppearanceService.IsLoaded ? error ?? "Загружаем условия открытия…" :
                CharacterAppearanceService.IsSaving ? "Применяем образ…" : !string.IsNullOrEmpty(error) ? error :
                applied ? "Выбрано · аватарка соответствует образу" : unlocked ? "Образ открыт · аватарка изменится автоматически" : "Выполните все условия открытия";
            _apply.interactable = CharacterAppearance.CanSave && unlocked && artReady && !applied;
            _applyLabel.text = applied ? "ВЫБРАНО" : !unlocked ? "ОБРАЗ ЗАКРЫТ" : "ПРИМЕНИТЬ ОБРАЗ";
            _restore.interactable = CharacterAppearance.CanSave; _refresh.interactable = !CharacterAppearanceService.IsSaving;
            var opened = 0;
            for (var i = 0; i < _cards.Length; i++)
            {
                var row = CharacterAppearanceService.GetTheme(CharacterAppearance.Themes[i].Id);
                _cardPortraits[i].sprite = CharacterAppearance.Load(CharacterAppearance.Id(i), true);
                _cardPortraits[i].enabled = known && _cardPortraits[i].sprite != null;
                _cardPortraits[i].color = row?.unlocked == true ? Color.white : new Color(.66f, .69f, .69f, .9f);
                _cards[i].interactable = known; ExpeditionUi.StyleButton(_cards[i], i == _theme, 32);
                var completed = 0; if (row?.requirements != null) foreach (var requirement in row.requirements) if (requirement.met) completed++;
                if (row?.unlocked == true) opened++;
                _cardProgress[i].text = row == null ? "Загрузка…" : row.unlocked ? "Открыт" : completed + "/" + row.requirements.Length + " условий";
                _cardProgress[i].color = i == _theme ? ExpeditionTheme.Panel : row?.unlocked == true ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            }
            _catalogSummary.text = CharacterAppearanceService.IsLoaded ? "Открыто " + opened + " / " + _cards.Length + " · нажмите на образ, чтобы узнать больше" : "Загружаем коллекцию…";
            RefreshGridLayout(); RefreshDetailsLayout();
        }

        private static string Requirements(CharacterAppearanceService.ThemeState state)
        {
            if (state == null) return "Условия загружаются…";
            if (state.requirements == null || state.requirements.Length == 0) return "Исходный образ · доступен сразу";
            var text = new StringBuilder("Условия открытия\n");
            foreach (var requirement in state.requirements)
            {
                if (text.Length > "Условия открытия\n".Length) text.Append('\n');
                text.Append(requirement.met ? "+ " : "• ").Append(requirement.label).Append("  ")
                    .Append(Mathf.Min(requirement.current, requirement.required)).Append('/').Append(requirement.required);
            }
            if (state.unlocked) text.Append("\nОбраз открыт навсегда");
            return text.ToString();
        }

        public void RefreshGridLayout()
        {
            if (_viewport == null || _applyingGrid) return;
            _applyingGrid = true;
            _viewport.ForceUpdateRectTransforms(); _lastViewportSize = _viewport.rect.size;
            GridColumns = Mathf.Clamp(Mathf.FloorToInt((_viewport.rect.width + 12) / 300), 3, 5);
            const float gap = 12;
            var width = Mathf.Max(1, (_viewport.rect.width - 12 - gap * (GridColumns - 1)) / GridColumns);
            var height = Mathf.Max(285, width * 1.18f);
            var rows = Mathf.CeilToInt(_cards.Length / (float)GridColumns);
            _content.sizeDelta = new Vector2(0, rows * height + (rows - 1) * gap + 12);
            for (var i = 0; i < _cards.Length; i++)
            {
                var rect = _cards[i].transform as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(6 + i % GridColumns * (width + gap), -6 - i / GridColumns * (height + gap));
            }
            _content.anchoredPosition = new Vector2(0, Mathf.Clamp(_content.anchoredPosition.y, 0, Mathf.Max(0, _content.rect.height - _viewport.rect.height)));
            _applyingGrid = false;
        }
        private void ScrollToSelected()
        {
            RefreshGridLayout();
            var rect = _cards[_theme].transform as RectTransform;
            var top = -rect.anchoredPosition.y; var bottom = top + rect.rect.height;
            var current = _content.anchoredPosition.y; var viewport = _viewport.rect.height;
            var target = top < current ? top : bottom > current + viewport ? bottom - viewport : current;
            _content.anchoredPosition = new Vector2(0, Mathf.Clamp(target, 0, Mathf.Max(0, _content.rect.height - viewport)));
            _scroll.StopMovement();
        }
        private static TMP_Text Text(Transform host, string name, string value, float size, float x0, float y0, float x1, float y1, Color color)
        {
            var text = ExpeditionUi.Text(host, name, value, size, x0, y0, x1, y1, TextAlignmentOptions.Center);
            text.color = color; text.enableAutoSizing = true; text.fontSizeMin = size * .8f; text.fontSizeMax = size; text.margin = Vector4.zero;
            return text;
        }
        private TMP_Text DetailText(string name, float size, Color color)
        {
            var text = ExpeditionUi.Text(_detailsContent, name, "", size, 0, 0, 1, 1, TextAlignmentOptions.TopLeft);
            text.color = color; text.margin = Vector4.zero; text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
        private void RefreshDetailsLayout()
        {
            if (_detailsContent == null) return;
            _detailsContent.ForceUpdateRectTransforms(); _lastDetailsSize = _detailsContent.rect.size;
            var texts = new[] { _requirements, _reward, _status, _collectionBonus };
            var sizes = new[] { 36f, 34f, 30f, 30f };
            var width = Mathf.Max(1, _detailsContent.rect.width); var available = _detailsContent.rect.height;
            var heights = new float[texts.Length]; const float gap = 14;
            for (var pass = 0; pass <= 16; pass++)
            {
                var total = 0f;
                for (var i = 0; i < texts.Length; i++)
                {
                    texts[i].fontSize = Mathf.Max(27, sizes[i] * (1 - pass * .025f));
                    heights[i] = Mathf.Ceil(texts[i].GetPreferredValues(texts[i].text, width, float.PositiveInfinity).y) + 4;
                    total += heights[i] + (i == 0 ? 0 : gap);
                }
                if (total <= available) break;
            }
            var top = 0f;
            for (var i = 0; i < texts.Length; i++)
            {
                var rect = texts[i].rectTransform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
                rect.sizeDelta = new Vector2(0, heights[i]); rect.anchoredPosition = new Vector2(0, -top); top += heights[i] + gap;
            }
        }
    }
}
