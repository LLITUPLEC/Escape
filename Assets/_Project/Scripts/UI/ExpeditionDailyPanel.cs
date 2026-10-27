using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Project.Audio;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Cards show server-owned eligibility. Previewing never changes the cycle or grants a reward.</summary>
    public sealed class ExpeditionDailyPanel : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(7, 23, 28, 255), Green = new Color32(132, 185, 151, 255);
        private bool _quests, _fixture, _resetRequested;
        private ItemCatalog _catalog;
        private TMP_Text _subtitle, _status, _focusKicker, _focusTitle, _focusStatus, _bonusStatus;
        private RewardStrip _focusRewards;
        private Image _bonusFrame;
        private ExpeditionDailyLoginFrame _focusFrame, _focusClaimed;
        private readonly QuestCard[] _questCards = new QuestCard[3];
        private readonly DayCard[] _days = new DayCard[7];
        private readonly Image[] _path = new Image[7];
        private Button _loginClaim, _bonusClaim;
        private CancellationTokenSource _cts;
        private DailyRewardsService.State _fixtureState;

        public static ExpeditionDailyPanel Open(Transform host, bool quests, DailyRewardsService.State fixture = null, ItemCatalog catalog = null)
        {
            var go = ExpeditionUi.Rect(host, quests ? "DailyQuestsModal" : "DailyLoginModal", 0, 0, 1, 1).gameObject;
            var view = go.AddComponent<ExpeditionDailyPanel>();
            view._quests = quests; view._fixture = fixture != null; view._fixtureState = fixture; view._catalog = catalog ?? LoadedCatalog();
            view.Build(); view.Refresh();
            if (!view._fixture) _ = DailyRewardsService.RefreshAsync(view._cts.Token);
            return view;
        }
        private static ItemCatalog LoadedCatalog()
        {
            // MainMenu's serialized Hero catalog is a loaded scene dependency.
            // Editor fixtures can pass the same catalog explicitly.
            foreach (var catalog in Resources.FindObjectsOfTypeAll<ItemCatalog>())
                if (catalog != null && catalog.Get("ingot_green") != null && catalog.Get("ingot_purple") != null) return catalog;
            return null;
        }
        private void Build()
        {
            _cts = new CancellationTokenSource(); DailyRewardsService.Changed += Refresh;
            ExpeditionUi.Fill(transform, "Shade", 0, 0, 1, 1, new Color(0, 0, 0, .84f)).raycastTarget = true;
            var panel = ExpeditionUi.Panel(transform, "DailyPanel", .035f, .075f, .965f, .925f).rectTransform;
            Label(panel, "Title", _quests ? "ЗАДАНИЯ ЭКСПЕДИЦИИ" : "ПРИПАСЫ ЗА ВХОД", 36, .055f, .925f, .875f, .977f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Button(panel, "Close", "×", .895f, .925f, .973f, .979f, () => Destroy(gameObject), false, 42);
            _subtitle = Label(panel, "DayReset", "Загрузка…", 22, .06f, .88f, .94f, .922f); _subtitle.color = ExpeditionTheme.Muted;
            if (_quests) BuildQuests(panel); else BuildLogin(panel);
            _status = Label(panel, "Status", "", 20, .055f, .025f, .73f, .067f);
            ExpeditionUi.Button(panel, "Refresh", "Обновить", .755f, .025f, .947f, .067f,
                () => { if (!_fixture) _ = DailyRewardsService.RefreshAsync(_cts.Token); }, false, 21);
        }
        private void BuildQuests(RectTransform parent)
        {
            for (var i = 0; i < 3; i++)
            {
                var index = i; var y = .642f - .222f * i;
                var frame = Card(parent, "DailyQuestCard" + i, .045f, y, .955f, y + .212f); var root = frame.rectTransform;
                var icon = Picture(root, "QuestEmblem", null, .03f, .66f, .155f, .94f);
                var title = Label(root, "QuestName", "", 31, .18f, .775f, .96f, .94f); title.fontStyle = FontStyles.Bold;
                var description = Label(root, "QuestDescription", "", 23, .18f, .56f, .96f, .775f); description.color = ExpeditionTheme.Muted;
                var progress = Label(root, "QuestProgress", "", 22, .04f, .455f, .34f, .55f);
                var status = Label(root, "QuestState", "", 20, .36f, .455f, .96f, .55f, TextAlignmentOptions.Right);
                var track = ExpeditionUi.Fill(root, "QuestProgressTrack", .04f, .399f, .96f, .433f, new Color32(3, 15, 19, 255)); track.sprite = ModalPanelCloseButton.WhiteSprite();
                var fill = ExpeditionUi.Fill(track.transform, "DailyQuestProgressFill", 0, 0, 1, 1, ExpeditionTheme.Amber);
                fill.sprite = ModalPanelCloseButton.WhiteSprite(); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
                var rewards = new RewardStrip(this, root, "QuestRewards", .035f, .065f, .625f, .35f, RewardStyle.Compact);
                var claim = ExpeditionUi.Button(root, "ClaimQuest", "ПОЛУЧИТЬ", .65f, .08f, .966f, .335f, () => Claim("quest", index), true, 24); FitButton(claim, 24);
                _questCards[i] = new QuestCard { Frame = frame, Icon = icon, Title = title, Description = description, Progress = progress, Status = status,
                    Fill = fill, Rewards = rewards, Claim = claim };
            }
            _bonusFrame = Card(parent, "DailyCompletionBonus", .045f, .077f, .955f, .188f, true); var bonus = _bonusFrame.rectTransform;
            Picture(bonus, "CompletionEmblem", ConsumablePresentation.Icon(ConsumablePresentation.Bounty), .035f, .14f, .16f, .86f);
            Label(bonus, "BonusKicker", "НАГРАДА ЗА ВСЕ ТРИ ЗАДАНИЯ", 19, .19f, .73f, .965f, .93f).color = ExpeditionTheme.Amber;
            Label(bonus, "BonusName", "Свиток щедрой добычи ×1", 27, .19f, .48f, .965f, .74f).fontStyle = FontStyles.Bold;
            Label(bonus, "BonusDescription", "Золото и руда PvE ×1,5 на 1 час", 20, .19f, .24f, .66f, .47f).color = ExpeditionTheme.Muted;
            _bonusStatus = Label(bonus, "BonusState", "", 18, .19f, .045f, .66f, .24f);
            _bonusClaim = ExpeditionUi.Button(bonus, "ClaimCompletion", "ПОЛУЧИТЬ", .69f, .10f, .965f, .44f, () => Claim("completion", -1), true, 22); FitButton(_bonusClaim, 22);
        }
        private void BuildLogin(RectTransform parent)
        {
            var focus = LoginCard(parent, "DailyCurrentReward", .045f, .61f, .955f, .865f, out _focusFrame).rectTransform;
            _focusKicker = Label(focus, "CurrentDay", "", 22, .035f, .84f, .89f, .96f); _focusKicker.color = ExpeditionTheme.Amber;
            _focusClaimed = ClaimedMark(focus, .905f, .835f, .967f, .96f);
            _focusTitle = Label(focus, "CurrentRewardName", "", 32, .035f, .69f, .965f, .84f); _focusTitle.fontStyle = FontStyles.Bold;
            _focusRewards = new RewardStrip(this, focus, "CurrentRewardItems", .04f, .17f, .96f, .68f, RewardStyle.Featured);
            _focusStatus = Label(focus, "CurrentRewardState", "", 21, .035f, .035f, .965f, .16f, TextAlignmentOptions.Center);
            for (var i = 0; i < 7; i++)
            {
                _path[i] = ExpeditionUi.Fill(parent, "CycleStep" + (i + 1), .05f + i * .13f, .592f, .17f + i * .13f, .599f, ExpeditionTheme.Teal); _path[i].sprite = ModalPanelCloseButton.WhiteSprite();
                var final = i == 6; var column = i % 3; var row = i / 3;
                var x = .045f + column * .308f; var y = .447f - row * .113f;
                var frame = LoginCard(parent, "LoginDayCard" + (i + 1), final ? .045f : x, final ? .219f : y, final ? .955f : x + .292f, final ? .326f : y + .105f, out var border);
                var card = frame.rectTransform;
                var title = Label(card, "DayName", "ДЕНЬ " + (i + 1), final ? 27 : 22, .04f, final ? .52f : .73f, final ? .24f : .50f, .94f);
                title.fontStyle = FontStyles.Bold; title.color = final ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                var state = Label(card, "DayState", "", final ? 18 : 16, final ? .035f : .50f, final ? .14f : .73f, final ? .24f : .96f, final ? .49f : .94f, TextAlignmentOptions.Center);
                var rewards = new RewardStrip(this, card, "DayItems", final ? .27f : .03f, .075f, .97f, final ? .93f : .71f, RewardStyle.Mini);
                var check = final ? ClaimedMark(card, .20f, .17f, .248f, .47f) : ClaimedMark(card, .845f, .745f, .96f, .96f);
                _days[i] = new DayCard { Frame = frame, Border = border, Check = check, Status = state, Rewards = rewards };
            }
            Label(parent, "CycleHint", "Пропуски не сбрасывают путь. После 7 получений начнётся новый цикл.", 21, .06f, .172f, .94f, .211f, TextAlignmentOptions.Center).color = ExpeditionTheme.Muted;
            _loginClaim = ExpeditionUi.Button(parent, "ClaimLogin", "ПОЛУЧИТЬ НАГРАДУ", .12f, .091f, .88f, .16f, () => Claim("login", -1), true, 30); FitButton(_loginClaim, 30);
        }
        private void Refresh()
        {
            if (_subtitle == null) return;
            var state = _fixture ? _fixtureState : DailyRewardsService.Current;
            var busy = !_fixture && (DailyRewardsService.Busy || state != null && DailyRewardsService.EstimatedServerTime >= state.reset_at);
            if (state == null)
            {
                _status.text = DailyRewardsService.Error ?? "Загрузка наград…";
                if (_loginClaim != null) _loginClaim.interactable = false;
                foreach (var card in _questCards) if (card != null) card.Claim.interactable = false;
                if (_bonusClaim != null) _bonusClaim.interactable = false;
                return;
            }
            if (_fixture || DailyRewardsService.EstimatedServerTime < state.reset_at) _resetRequested = false;
            RefreshTime(state);
            _status.text = !_fixture && DailyRewardsService.Error != null ? DailyRewardsService.Error : busy ? "Обновляем награды…" :
                _quests ? "Три цели на сегодня. Каждая награда — отдельно." : state.login.can_claim ? "Награда готова — заберите припасы." : "Сегодняшние припасы уже получены.";
            if (state.receipt?.pending_items?.Length > 0) _status.text = "Предметы ждут свободного места в инвентаре.";
            if (_quests) RefreshQuests(state, busy); else RefreshLogin(state, busy);
        }
        private void RefreshQuests(DailyRewardsService.State state, bool busy)
        {
            var claimed = 0;
            for (var i = 0; i < 3; i++)
            {
                var q = state.quests[i]; var card = _questCards[i];
                card.Title.text = QuestName(q); card.Description.text = QuestDescription(q);
                card.Icon.sprite = ExpeditionTheme.Icon(q.id == "mine" ? "mine" : q.id == "abilities" ? "energy" : "arena");
                card.Progress.text = Math.Min(q.progress, q.target) + " / " + q.target;
                card.Status.text = q.claimed ? "Награда получена" : q.ready ? "Готово к получению" : "Выполняется";
                card.Status.color = q.claimed ? Green : q.ready ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                card.Fill.fillAmount = q.target > 0 ? Mathf.Clamp01((float)q.progress / q.target) : 0; card.Fill.color = q.claimed ? Green : ExpeditionTheme.Amber;
                Accent(card.Frame, q.ready && !q.claimed);
                card.Rewards.Bind(q.reward); card.Claim.interactable = !busy && q.ready && !q.claimed;
                card.Claim.GetComponentInChildren<TMP_Text>().text = q.claimed ? "ПОЛУЧЕНО" : q.ready ? "ПОЛУЧИТЬ" : "В ПРОЦЕССЕ";
                if (q.claimed) claimed++;
            }
            _bonusStatus.text = state.completion_claimed ? "Бонус получен" : state.completion_ready ? "Все награды получены — бонус ваш" : "Получено наград: " + claimed + " / 3";
            _bonusStatus.color = state.completion_claimed ? Green : ExpeditionTheme.Amber;
            _bonusClaim.interactable = !busy && state.completion_ready && !state.completion_claimed;
            _bonusClaim.GetComponentInChildren<TMP_Text>().text = state.completion_claimed ? "ПОЛУЧЕНО" : state.completion_ready ? "ЗАБРАТЬ" : "ЗАКРЫТО";
            Accent(_bonusFrame, state.completion_ready && !state.completion_claimed);
        }
        private void RefreshLogin(DailyRewardsService.State state, bool busy)
        {
            var login = state.login;
            var today = login.claimed_today && login.total_claimed > 0 ? (login.total_claimed - 1) % 7 + 1 : login.next_day;
            var completed = login.total_claimed % 7; if (login.claimed_today && completed == 0) completed = 7;
            _focusKicker.text = (login.claimed_today ? "ПОЛУЧЕНО СЕГОДНЯ" : "СЕГОДНЯ") + " · ДЕНЬ " + today + " ИЗ 7";
            _focusTitle.text = new[] { "Надёжный запас", "Металл для мастерской", "Редкая материя", "Сила перед спуском", "Большой запас", "Стойкость и металл", "Трофей экспедиции" }[today - 1];
            _focusRewards.Bind(login.rewards[today - 1]);
            _focusStatus.text = login.claimed_today ? "Припасы получены. Следующая награда — в новый день." : today == 7 ? "Особая награда за завершение пути" : "Ваш подарок за возвращение в убежище";
            _focusStatus.color = login.claimed_today ? Green : ExpeditionTheme.Muted;
            _focusFrame.Configure(true, login.claimed_today); _focusClaimed.gameObject.SetActive(login.claimed_today);
            for (var i = 0; i < 7; i++)
            {
                var past = i < completed; var current = i + 1 == today;
                _days[i].Rewards.Bind(login.rewards[i]);
                _days[i].Status.text = past ? "Получено" : current ? "Сегодня" : i == 6 ? "Особый день" : "Далее";
                _days[i].Status.color = past ? Green : current || i == 6 ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                _days[i].Border.Configure(current || i == 6, past); _days[i].Check.gameObject.SetActive(past);
                if (i < 6) ExpeditionUi.SetRect(_days[i].Status.rectTransform, past ? .45f : .50f, .73f, past ? .83f : .96f, .94f);
                _path[i].color = past ? Green : current ? ExpeditionTheme.Amber : ExpeditionTheme.Teal;
            }
            _loginClaim.interactable = !busy && login.can_claim;
            _loginClaim.GetComponentInChildren<TMP_Text>().text = login.can_claim ? "ПОЛУЧИТЬ · ДЕНЬ " + login.next_day : "СЕГОДНЯ ПОЛУЧЕНО";
        }
        private static string QuestName(DailyRewardsService.Quest q) => q.id == "mine" ? "Разведка шахты" : q.id == "abilities" ? "Боевая практика" : q.id == "pvp" ? "Проверка в бою" : q.label;
        private static string QuestDescription(DailyRewardsService.Quest q) => q.id == "mine" ? "Побеждайте монстров на любых доступных этажах шахты." :
            q.id == "abilities" ? "Применяйте способности. Прогресс учитывается после завершения боя." :
            q.id == "pvp" ? "Победите или сделайте не менее 3 своих ходов в каждом завершённом бою PvP." : q.label;
        private void Claim(string kind, int index)
        {
            if (_fixture) return;
            var state = DailyRewardsService.Current; if (state == null || DailyRewardsService.Busy) return;
            ExpeditionAudio.Play(ExpeditionCue.UiClick);
            _ = DailyRewardsService.ClaimAsync(kind, index >= 0 ? state.quests[index].id : null, _cts.Token);
        }
        private void Update()
        {
            if (_fixture) return;
            var state = DailyRewardsService.Current;
            if (state != null && DailyRewardsService.EstimatedServerTime >= state.reset_at && !_resetRequested)
            { _resetRequested = true; _ = DailyRewardsService.RefreshAsync(_cts.Token); }
            RefreshTime(state);
        }
        private void RefreshTime(DailyRewardsService.State state)
        {
            if (state == null) return;
            var seconds = Math.Max(0, state.reset_at - (_fixture ? state.now : DailyRewardsService.EstimatedServerTime));
            _subtitle.text = "Новый день через " + TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") + " · 00:00 МСК";
        }
        private static Image Card(Transform parent, string name, float x0, float y0, float x1, float y1, bool special = false)
        {
            var frame = ExpeditionUi.Panel(parent, name, x0, y0, x1, y1);
            ExpeditionUi.Fill(frame.transform, "CardWell", .022f, .045f, .978f, .955f, Ink);
            Accent(frame, special); return frame;
        }
        private static void Accent(Image frame, bool highlighted) { frame.sprite = ExpeditionArt.Frame(highlighted) ?? ExpeditionTheme.Plate; frame.color = Color.white; }
        private static Image LoginCard(Transform parent, string name, float x0, float y0, float x1, float y1, out ExpeditionDailyLoginFrame border)
        {
            var frame = ExpeditionUi.Fill(parent, name, x0, y0, x1, y1, Color.clear);
            border = ExpeditionUi.Rect(frame.transform, "IntactLoginFrame", 0, 0, 1, 1).gameObject.AddComponent<ExpeditionDailyLoginFrame>();
            border.raycastTarget = false; border.Configure(false, false); return frame;
        }
        private static ExpeditionDailyLoginFrame ClaimedMark(Transform parent, float x0, float y0, float x1, float y1)
        {
            var slot = ExpeditionUi.Rect(parent, "ClaimedCheckSlot", x0, y0, x1, y1);
            var root = ExpeditionUi.Rect(slot, "ClaimedCheck", 0, 0, 1, 1);
            var mark = root.gameObject.AddComponent<ExpeditionDailyLoginFrame>(); mark.raycastTarget = false; mark.Configure(false, true, true);
            var fit = root.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 1;
            return mark;
        }
        private static Image Picture(Transform p, string name, Sprite sprite, float x0, float y0, float x1, float y1)
        { var image = ExpeditionUi.Fill(p, name, x0, y0, x1, y1, Color.white); image.sprite = sprite; image.preserveAspect = true; return image; }
        private static TMP_Text Label(Transform p, string name, string text, int size, float x0, float y0, float x1, float y1, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var label = ExpeditionUi.Text(p, name, text, size, x0, y0, x1, y1, align);
            label.margin = Vector4.zero; label.enableAutoSizing = true; label.fontSizeMin = size - 3; label.fontSizeMax = size; label.overflowMode = TextOverflowModes.Overflow; return label;
        }
        private static void FitButton(Button button, int size)
        { var label = button.GetComponentInChildren<TMP_Text>(); label.margin = Vector4.zero; label.enableAutoSizing = true; label.fontSizeMin = size - 3; label.fontSizeMax = size; }
        private Sprite RewardIcon(string id)
        {
            if (id == "global_boss_teleport") return ExpeditionShopPanel.TokenIcon;
            if (id == "gold" || id == "ore" || id == "matter") return ExpeditionResourceIcons.Get(id);
            if (ConsumablePresentation.IsKnown(id)) return ConsumablePresentation.Icon(id);
            var definition = _catalog != null ? _catalog.Get(id) : null;
            return definition != null ? _catalog.GetDisplayIcon(definition) : null;
        }
        private static string ShortName(string id) => id == "gold" ? "Золото" : id == "ore" ? "Руда" : id == "matter" ? "Материя" : id == "global_boss_teleport" ? "Телепорт" : id.StartsWith("ingot_", StringComparison.Ordinal) ? "Слитки" : id == ConsumablePresentation.Bounty ? "Свиток" : "Эликсир";
        private static string FullName(string id) => id == "gold" ? "Золото" : id == "ore" ? "Руда" : id == "matter" ? "Материя" :
            id == "ingot_green" ? "Зелёные слитки" : id == "ingot_blue" ? "Синие слитки" : id == "ingot_purple" ? "Фиолетовые слитки" : id == "global_boss_teleport" ? "Телепорт к боссу" :
            ConsumablePresentation.IsKnown(id) ? ConsumablePresentation.Name(id) : id;
        private enum RewardStyle { Mini, Compact, Featured }
        private sealed class QuestCard { public Image Frame, Icon, Fill; public TMP_Text Title, Description, Progress, Status; public RewardStrip Rewards; public Button Claim; }
        private sealed class DayCard { public Image Frame; public ExpeditionDailyLoginFrame Border, Check; public TMP_Text Status; public RewardStrip Rewards; }
        private sealed class RewardStrip
        {
            private readonly ExpeditionDailyPanel _owner; private readonly RectTransform _root; private readonly RewardStyle _style;
            private readonly List<RewardTile> _tiles = new List<RewardTile>();
            public RewardStrip(ExpeditionDailyPanel owner, Transform parent, string name, float x0, float y0, float x1, float y1, RewardStyle style)
            { _owner = owner; _root = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1); _style = style; }
            public void Bind(DailyRewardsService.Reward reward)
            {
                var entries = new List<DailyRewardsService.Item>();
                if (reward != null)
                {
                    if (reward.gold > 0) entries.Add(new DailyRewardsService.Item { def_id = "gold", amount = reward.gold });
                    if (reward.ore > 0) entries.Add(new DailyRewardsService.Item { def_id = "ore", amount = reward.ore });
                    if (reward.matter > 0) entries.Add(new DailyRewardsService.Item { def_id = "matter", amount = reward.matter });
                    foreach (var item in reward.items ?? Array.Empty<DailyRewardsService.Item>()) if (item != null && item.amount > 0) entries.Add(item);
                }
                for (var i = _tiles.Count; i < entries.Count; i++) _tiles.Add(BuildTile());
                for (var i = 0; i < _tiles.Count; i++)
                {
                    var tile = _tiles[i]; tile.Root.gameObject.SetActive(i < entries.Count); if (i >= entries.Count) continue;
                    var item = entries[i]; var gap = entries.Count == 1 ? 0 : .018f;
                    ExpeditionUi.SetRect(tile.Root, i / (float)entries.Count + gap, 0, (i + 1f) / entries.Count - gap, 1);
                    tile.Root.name = "DailyReward-" + item.def_id; tile.Icon.sprite = _owner.RewardIcon(item.def_id);
                    tile.Amount.text = (item.def_id == "gold" || item.def_id == "ore" || item.def_id == "matter" ? "" : "×") + item.amount;
                    if (tile.Caption != null) tile.Caption.text = _style == RewardStyle.Featured ? FullName(item.def_id) : ShortName(item.def_id);
                }
            }
            private RewardTile BuildTile()
            {
                var root = ExpeditionUi.Rect(_root, "Reward", 0, 0, 1, 1);
                if (_style != RewardStyle.Mini) ExpeditionUi.Fill(root, "RewardWell", 0, 0, 1, 1, new Color32(20, 48, 53, 150));
                var featured = _style == RewardStyle.Featured; var mini = _style == RewardStyle.Mini;
                var icon = featured ? Picture(root, "DailyRewardIcon", null, .08f, .43f, .92f, .98f) : Picture(root, "DailyRewardIcon", null, .015f, .08f, mini ? .64f : .36f, .94f);
                var amount = featured ? Label(root, "RewardAmount", "", 31, .035f, .245f, .965f, .445f, TextAlignmentOptions.Center) :
                    Label(root, "RewardAmount", "", mini ? 22 : 26, mini ? .60f : .39f, mini ? .18f : .42f, .98f, mini ? .86f : .91f, TextAlignmentOptions.Center);
                amount.fontStyle = FontStyles.Bold; amount.color = ExpeditionTheme.Amber; amount.textWrappingMode = TextWrappingModes.NoWrap;
                TMP_Text caption = null;
                if (!mini) caption = featured ? Label(root, "RewardName", "", 19, .025f, .005f, .975f, .245f, TextAlignmentOptions.Center) :
                    Label(root, "RewardName", "", 18, .39f, .055f, .98f, .42f, TextAlignmentOptions.Center);
                return new RewardTile { Root = root, Icon = icon, Amount = amount, Caption = caption };
            }
        }
        private sealed class RewardTile { public RectTransform Root; public Image Icon; public TMP_Text Amount, Caption; }
        public static string RewardText(DailyRewardsService.Reward reward)
        {
            if (reward == null) return ""; var result = new StringBuilder();
            void Add(string value) { if (result.Length > 0) result.Append(" · "); result.Append(value); }
            if (reward.gold > 0) Add(reward.gold + " золота"); if (reward.ore > 0) Add(reward.ore + " руды"); if (reward.matter > 0) Add(reward.matter + " материи");
            foreach (var item in reward.items ?? Array.Empty<DailyRewardsService.Item>()) if (item != null) Add(FullName(item.def_id) + " ×" + item.amount);
            return result.ToString();
        }
        private void OnDestroy() { DailyRewardsService.Changed -= Refresh; _cts?.Cancel(); _cts?.Dispose(); }
    }
}
