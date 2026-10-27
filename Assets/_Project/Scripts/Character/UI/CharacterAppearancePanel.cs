using System.Text;
using System.Globalization;
using System.Threading;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Locked skins can be previewed. Only a fresh server authorization can equip one.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearancePanel : MonoBehaviour
    {
        private RectTransform _root, _viewport, _content;
        private ScrollRect _scroll;
        private Image _body, _portrait;
        private TMP_Text _title, _description, _requirements, _reward, _collectionBonus, _status, _applyLabel;
        private Button _apply, _restore, _refresh;
        private readonly Button[] _cards = new Button[CharacterAppearance.Themes.Count];
        private readonly Image[] _cardPortraits = new Image[CharacterAppearance.Themes.Count];
        private readonly TMP_Text[] _cardProgress = new TMP_Text[CharacterAppearance.Themes.Count];
        private int _theme;
        private Vector2 _lastViewportSize;
        private bool _applyingGrid;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public string PreviewId => CharacterAppearance.Id(_theme);

        public void Build(RectTransform parent)
        {
            if (_root != null) return;
            _root = ExpeditionUi.Rect(parent, "ExpeditionAppearancePage", .018f, .012f, .982f, .931f);
            var backdrop = ExpeditionUi.Panel(_root, "AppearanceBackdrop", 0, 0, 1, 1);
            ExpeditionUi.Fill(backdrop.transform, "InnerWell", .018f, .01f, .982f, .99f, new Color32(8, 25, 30, 235));
            _body = ExpeditionUi.Fill(_root, "AppearanceBodyPreview", .015f, .49f, .49f, .975f, Color.white); _body.preserveAspect = true;
            var frame = ExpeditionUi.Panel(_root, "AppearancePortraitFrame", .66f, .745f, .94f, .965f);
            _portrait = ExpeditionUi.Fill(frame.transform, "AppearanceAvatarPreview", .055f, .055f, .945f, .945f, Color.white); _portrait.preserveAspect = true;
            _title = Text("AppearanceTitle", "", 33, .50f, .665f, .98f, .745f, ExpeditionTheme.Ivory);
            _description = Text("AppearanceDescription", "", 22, .50f, .59f, .98f, .665f, ExpeditionTheme.Muted);
            _requirements = Text("AppearanceRequirements", "", 22, .49f, .455f, .98f, .59f, ExpeditionTheme.Ivory);
            _requirements.alignment = TextAlignmentOptions.MidlineLeft; _requirements.richText = false;
            _reward = Text("AppearanceUnlockReward", "", 20, .025f, .46f, .475f, .49f, ExpeditionTheme.Amber);
            _status = Text("AppearanceStatus", "", 23, .035f, .414f, .965f, .46f, ExpeditionTheme.Amber);
            _collectionBonus = Text("AppearanceCollectionBonus", "", 20, .035f, .389f, .965f, .414f, ExpeditionTheme.Ivory);
            _viewport = ExpeditionUi.Rect(_root, "AppearanceGridViewport", .03f, .151f, .955f, .385f);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var hit = _viewport.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, .001f); hit.raycastTarget = true;
            _content = ExpeditionUi.Rect(_viewport, "AppearanceGridContent", 0, 0, 1, 1);
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = _viewport; _scroll.content = _content;
            _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 55;
            var track = ExpeditionUi.Panel(_root, "AppearanceGridScrollbar", .963f, .151f, .983f, .385f).rectTransform;
            var handle = ExpeditionUi.Panel(track, "Handle", .12f, 0, .88f, 1, ExpeditionTheme.Amber);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; _scroll.verticalScrollbar = scrollbar;
            for (var i = 0; i < _cards.Length; i++)
            {
                var index = i;
                var card = ExpeditionUi.Button(_content, "AppearanceTheme" + i, CharacterAppearance.Themes[i].Name, 0, 0, 1, 1, () => SelectTheme(index), false, 20);
                var label = card.GetComponentInChildren<TMP_Text>();
                ExpeditionUi.SetRect(label.rectTransform, .50f, .43f, .98f, .96f);
                label.enableAutoSizing = true; label.fontSizeMin = 17; label.fontSizeMax = 20; label.margin = Vector4.zero;
                var picture = ExpeditionUi.Fill(card.transform, "PairedPortrait", .04f, .12f, .48f, .94f, Color.white); picture.preserveAspect = true;
                var progress = ExpeditionUi.Text(card.transform, "AppearanceCardProgress", "", 18, .50f, .07f, .98f, .42f, TextAlignmentOptions.Center);
                progress.enableAutoSizing = true; progress.fontSizeMin = 16; progress.fontSizeMax = 18; progress.margin = Vector4.zero;
                _cards[i] = card; _cardPortraits[i] = picture; _cardProgress[i] = progress;
            }
            _apply = ExpeditionUi.Button(_root, "ApplyAppearance", "ПРИМЕНИТЬ ОБРАЗ", .065f, .079f, .935f, .14f, ApplyPreview, true, 30);
            _applyLabel = _apply.GetComponentInChildren<TMP_Text>();
            _restore = ExpeditionUi.Button(_root, "RestoreClassicAppearance", "Исходный образ", .065f, .015f, .615f, .065f, RestoreDefault, false, 23);
            _refresh = ExpeditionUi.Button(_root, "RefreshAppearances", "Обновить", .64f, .015f, .935f, .065f, RefreshFromServer, false, 23);
            _root.gameObject.SetActive(false);
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
            _root.gameObject.SetActive(true); Refresh(); ScrollToSelected();
        }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
        public void SelectTheme(int index) { _theme = Mathf.Clamp(index, 0, CharacterAppearance.Themes.Count - 1); Refresh(); ScrollToSelected(); }
        public async void ApplyPreview() { await CharacterAppearance.ApplyBodyAsync(PreviewId); if (this != null) Refresh(); }
        private async void RestoreDefault() { await CharacterAppearance.RestoreDefaultAsync(); if (this != null) Refresh(); }
        private void RefreshFromServer() { if (Application.isPlaying) _ = CharacterAppearanceService.RefreshAsync(CancellationToken.None, true); }
        private void OnEnable() { CharacterAppearance.Changed += Refresh; AccountGender.Changed += Refresh; CharacterAppearanceService.Changed += Refresh; }
        private void OnDisable() { CharacterAppearance.Changed -= Refresh; AccountGender.Changed -= Refresh; CharacterAppearanceService.Changed -= Refresh; }
        private void LateUpdate() { if (_viewport != null && _lastViewportSize != _viewport.rect.size) RefreshGridLayout(); }

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
            _reward.text = state == null ? "Награды загружаются…" : state.id == "dawnwarden" ? "Начальный образ\nБез награды за открытие" :
                state.unlock_reward_matter == 0 ? "Награда пока недоступна" :
                (state.reward_granted ? "Получено: " : "За открытие: ") + state.unlock_reward_matter + " материи\n+" +
                state.unlock_reward_hp_percent.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) + "% HP навсегда";
            _collectionBonus.text = !CharacterAppearanceService.IsLoaded ? "Бонус коллекции загружается…" :
                "Коллекция: +" + CharacterAppearanceService.PassiveHpBonusPercent.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU")) +
                "% HP навсегда · награды " + CharacterAppearanceService.TotalRewardedMatter + " материи";
            var applied = CharacterAppearance.BodyId == PreviewId;
            var unlocked = state?.unlocked == true;
            var artReady = _body.sprite != null && _portrait.sprite != null;
            var error = CharacterAppearanceService.LastError;
            _status.text = !known ? "Загружаем героя…" : !CharacterAppearanceService.IsLoaded ? error ?? "Загружаем условия открытия…" :
                CharacterAppearanceService.IsSaving ? "Применяем образ…" : !string.IsNullOrEmpty(error) ? error :
                applied ? "Выбрано · аватарка соответствует образу" : unlocked ? "Образ открыт · аватарка изменится автоматически" : "Предпросмотр · выполните все условия открытия";
            _apply.interactable = CharacterAppearance.CanSave && unlocked && artReady && !applied;
            _applyLabel.text = applied ? "ВЫБРАНО" : !unlocked ? "ОБРАЗ ЗАКРЫТ" : "ПРИМЕНИТЬ ОБРАЗ";
            _restore.interactable = CharacterAppearance.CanSave; _refresh.interactable = !CharacterAppearanceService.IsSaving;
            for (var i = 0; i < _cards.Length; i++)
            {
                var row = CharacterAppearanceService.GetTheme(CharacterAppearance.Themes[i].Id);
                _cardPortraits[i].sprite = CharacterAppearance.Load(CharacterAppearance.Id(i), true);
                _cardPortraits[i].enabled = known && _cardPortraits[i].sprite != null;
                _cardPortraits[i].color = row?.unlocked == true ? Color.white : new Color(.66f, .69f, .69f, .9f);
                _cards[i].interactable = known; ExpeditionUi.StyleButton(_cards[i], i == _theme, 20);
                var completed = 0; if (row?.requirements != null) foreach (var requirement in row.requirements) if (requirement.met) completed++;
                _cardProgress[i].text = row == null ? "Загрузка…" : row.unlocked ? "Открыт" : "Закрыт\n" + completed + "/" + row.requirements.Length + " условий";
                _cardProgress[i].color = i == _theme ? ExpeditionTheme.Panel : row?.unlocked == true ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            }
            RefreshGridLayout();
        }

        private static string Requirements(CharacterAppearanceService.ThemeState state)
        {
            if (state == null) return "Условия загружаются…";
            if (state.requirements.Length == 0) return "Исходный образ\nДоступен сразу";
            var text = new StringBuilder();
            foreach (var requirement in state.requirements)
            {
                if (text.Length > 0) text.Append('\n');
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
            const int columns = 3; const float gap = 12;
            var width = Mathf.Max(1, (_viewport.rect.width - 12 - gap * (columns - 1)) / columns);
            var height = Mathf.Clamp(width * .58f, 140, 210);
            var rows = Mathf.CeilToInt(_cards.Length / (float)columns);
            _content.sizeDelta = new Vector2(0, rows * height + (rows - 1) * gap + 12);
            for (var i = 0; i < _cards.Length; i++)
            {
                var rect = _cards[i].transform as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(6 + i % columns * (width + gap), -6 - i / columns * (height + gap));
            }
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
        private TMP_Text Text(string name, string value, float size, float x0, float y0, float x1, float y1, Color color)
        {
            var text = ExpeditionUi.Text(_root, name, value, size, x0, y0, x1, y1, TextAlignmentOptions.Center);
            text.color = color; text.enableAutoSizing = true; text.fontSizeMin = size * .8f; text.fontSizeMax = size; text.margin = Vector4.zero;
            return text;
        }
    }
}
