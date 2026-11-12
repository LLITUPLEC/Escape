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
        private static readonly Color ClaimedText = new Color32(184, 186, 187, 255), ClaimedIcon = new Color32(175, 175, 175, 255);
        private static readonly Vector2[] Camps =
        {
            new(.18f, .77f), new(.48f, .83f), new(.80f, .71f), new(.69f, .48f),
            new(.33f, .49f), new(.18f, .23f), new(.70f, .19f),
        };
        private static readonly string[] CampRewards =
        {
            "Надёжный запас", "Металл для мастерской", "Редкая материя", "Сила перед спуском",
            "Большой запас", "Стойкость и металл", "Трофей экспедиции",
        };
        private bool _quests, _fixture, _resetRequested;
        private int _selectedQuest = -1, _previewDay = -1, _displayedToday;
        private ItemCatalog _catalog;
        private TMP_Text _subtitle, _status, _focusKicker, _focusTitle, _focusStatus, _bonusStatus;
        private TMP_Text _contractTitle, _contractDescription, _contractProgress, _contractStatus;
        private Image _contractIcon;
        private RewardStrip _focusRewards, _contractRewards;
        private Image _bonusFrame;
        private ExpeditionDailyLoginFrame _focusFrame, _focusClaimed;
        private ExpeditionSupplyFrame _loginClaimFrame;
        private ExpeditionPanelArrival _focusArrival, _contractArrival;
        private readonly QuestCard[] _questCards = new QuestCard[3];
        private readonly DayCard[] _days = new DayCard[7];
        private readonly List<Image>[] _trail = new List<Image>[Camps.Length - 1];
        private Button _loginClaim, _bonusClaim, _questClaim;
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
            if (Application.isPlaying) panel.gameObject.AddComponent<ExpeditionPanelArrival>();
            Label(panel, "Title", _quests ? "Доска заданий" : "Маршрут снабжения", 43, .055f, .925f, .875f, .977f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Button(panel, "Close", "×", .895f, .925f, .973f, .979f, () => Destroy(gameObject), false, 42);
            _subtitle = Label(panel, "DayReset", "Загрузка…", 22, .06f, .88f, .94f, .922f); _subtitle.color = ExpeditionTheme.Muted;
            if (_quests) BuildQuests(panel); else BuildLogin(panel);
            _status = Label(panel, "Status", "", 20, .055f, .025f, .73f, .067f);
            ExpeditionUi.Button(panel, "Refresh", "Обновить", .755f, .025f, .947f, .067f,
                () => { if (!_fixture) _ = DailyRewardsService.RefreshAsync(_cts.Token); }, false, 21);
        }
        private void BuildQuests(RectTransform parent)
        {
            ExpeditionShelterSurface.Illustration(parent, "GuildBoard", "daily-contract-board", .035f, .611f, .965f, .869f);
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var x = .055f + .301f * i;
                var notice = ExpeditionUi.Button(parent, "DailyQuestCard" + i, "", x, .621f, x + .287f, .791f,
                    () => SelectQuest(index), false);
                var root = (RectTransform)notice.transform;
                ExpeditionUi.Fill(root, "NoticePaper", .026f, .032f, .974f, .968f, new Color32(44, 30, 21, 255));
                var title = Label(root, "QuestName", "", 23, .10f, .50f, .90f, .78f, TextAlignmentOptions.Center);
                title.fontStyle = FontStyles.Bold;
                var icon = Picture(root, "QuestEmblem", null, .34f, .77f, .66f, 1.17f);
                var status = Label(root, "QuestState", "", 18, .055f, .381f, .945f, .51f, TextAlignmentOptions.Center);
                var track = ExpeditionUi.Fill(root, "QuestProgressTrack", .07f, .344f, .93f, .375f, new Color32(3, 15, 19, 255));
                var fill = ExpeditionUi.Fill(track.transform, "DailyQuestProgressFill", 0, 0, 1, 1, ExpeditionTheme.Amber);
                fill.sprite = ModalPanelCloseButton.WhiteSprite(); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
                var rewards = new RewardStrip(this, root, "QuestRewards", .06f, .045f, .94f, .323f, RewardStyle.Mini);
                _questCards[i] = new QuestCard { Notice = notice, Icon = icon, Title = title, Status = status, Fill = fill, Rewards = rewards };
            }

            var contract = Card(parent, "PinnedContract", .045f, .245f, .955f, .605f).rectTransform;
            if (Application.isPlaying) _contractArrival = contract.gameObject.AddComponent<ExpeditionPanelArrival>();
            _contractIcon = Picture(contract, "ContractEmblem", null, .035f, .685f, .19f, .951f);
            _contractTitle = Label(contract, "ContractTitle", "", 37, .207f, .805f, .96f, .955f);
            _contractTitle.fontStyle = FontStyles.Bold;
            _contractDescription = Label(contract, "ContractDescription", "", 26, .207f, .64f, .96f, .803f);
            _contractDescription.color = ExpeditionTheme.Ivory;
            _contractProgress = Label(contract, "ContractProgress", "", 25, .053f, .495f, .40f, .602f);
            _contractStatus = Label(contract, "ContractStatus", "", 23, .402f, .495f, .945f, .602f, TextAlignmentOptions.Right);
            _contractRewards = new RewardStrip(this, contract, "ContractRewards", .05f, .10f, .62f, .46f, RewardStyle.Compact);
            _questClaim = ExpeditionUi.Button(contract, "ClaimQuest", "ПОЛУЧИТЬ", .65f, .135f, .958f, .405f,
                () => Claim("quest", _selectedQuest), true, 26);
            FitButton(_questClaim, 26);

            _bonusFrame = Card(parent, "DailyCompletionBonus", .045f, .079f, .955f, .233f, true); var bonus = _bonusFrame.rectTransform;
            Picture(bonus, "CompletionEmblem", ConsumablePresentation.Icon(ConsumablePresentation.Bounty), .035f, .14f, .16f, .86f);
            Label(bonus, "BonusKicker", "Награда за все три задания", 21, .19f, .73f, .965f, .93f).color = ExpeditionTheme.Amber;
            Label(bonus, "BonusName", "Свиток щедрой добычи ×1", 27, .19f, .48f, .965f, .74f).fontStyle = FontStyles.Bold;
            Label(bonus, "BonusDescription", "Золото и руда PvE ×1,5 на 1 час", 20, .19f, .24f, .66f, .47f).color = ExpeditionTheme.Muted;
            _bonusStatus = Label(bonus, "BonusState", "", 18, .19f, .045f, .66f, .24f);
            _bonusClaim = ExpeditionUi.Button(bonus, "ClaimCompletion", "ПОЛУЧИТЬ", .69f, .10f, .965f, .44f, () => Claim("completion", -1), true, 22); FitButton(_bonusClaim, 22);
        }
        private void BuildLogin(RectTransform parent)
        {
            var map = ExpeditionShelterSurface.Illustration(parent, "SupplyMap", "daily-supply-map", .035f, .382f, .965f, .87f);
            ExpeditionUi.Fill(map, "MapInk", 0, 0, 1, 1, new Color32(26, 14, 8, 97));
            for (var segment = 0; segment < _trail.Length; segment++)
            {
                var dots = _trail[segment] = new List<Image>();
                for (var dot = 1; dot <= 8; dot++)
                {
                    var p = Vector2.Lerp(Camps[segment], Camps[segment + 1], dot / 9f);
                    var pin = ExpeditionUi.Fill(map, "RouteDot", p.x, p.y, p.x, p.y, ExpeditionTheme.Muted);
                    pin.rectTransform.sizeDelta = Vector2.one * 8;
                    dots.Add(pin);
                }
            }

            var focus = LoginCard(parent, "DailyCurrentReward", .045f, .154f, .955f, .374f, out _focusFrame).rectTransform;
            ExpeditionUi.Fill(focus, "ChestInterior", .014f, .024f, .986f, .976f, Ink).transform.SetAsFirstSibling();
            if (Application.isPlaying) _focusArrival = focus.gameObject.AddComponent<ExpeditionPanelArrival>();
            _focusKicker = Label(focus, "CurrentDay", "", 22, .035f, .84f, .89f, .96f); _focusKicker.color = ExpeditionTheme.Amber;
            _focusClaimed = ClaimedMark(focus, .905f, .835f, .967f, .96f);
            _focusTitle = Label(focus, "CurrentRewardName", "", 32, .035f, .69f, .965f, .84f); _focusTitle.fontStyle = FontStyles.Bold;
            _focusRewards = new RewardStrip(this, focus, "CurrentRewardItems", .04f, .17f, .96f, .68f, RewardStyle.Featured);
            _focusStatus = Label(focus, "CurrentRewardState", "", 21, .035f, .035f, .965f, .16f, TextAlignmentOptions.Center);
            for (var i = 0; i < Camps.Length; i++)
            {
                var index = i;
                var final = i == Camps.Length - 1;
                var p = Camps[i];
                var halfWidth = final ? .158f : .110f;
                var camp = ExpeditionUi.Button(map, "LoginDayCard" + (i + 1), "", p.x - halfWidth, p.y - .082f, p.x + halfWidth, p.y + .082f,
                    () => PreviewCamp(index + 1));
                var frame = ExpeditionSupplyFrame.Install(camp,
                    final ? ExpeditionSupplyFrame.Silhouette.Trophy : ExpeditionSupplyFrame.Silhouette.Camp);
                var root = (RectTransform)camp.transform;
                var title = Label(root, "DayName", final ? "День 7 · Трофей" : "День " + (i + 1),
                    25, .075f, .48f, .925f, .89f, TextAlignmentOptions.Center);
                title.fontStyle = FontStyles.Bold;
                var state = Label(root, "DayState", "", 18, .13f, .19f, .87f, .48f, TextAlignmentOptions.Center);
                var check = ClaimedMark(root, .43f, .025f, .57f, .21f);
                _days[i] = new DayCard { Camp = camp, Frame = frame, Check = check, Title = title, Status = state };
            }
            _loginClaim = ExpeditionUi.Button(parent, "ClaimLogin", "ПОЛУЧИТЬ НАГРАДУ", .12f, .079f, .88f, .144f,
                () => { if (_previewDay > 0 && _previewDay != _displayedToday) PreviewCamp(_displayedToday); else Claim("login", -1); }, false, 30);
            _loginClaimFrame = ExpeditionSupplyFrame.Install(_loginClaim, ExpeditionSupplyFrame.Silhouette.Claim);
            FitButton(_loginClaim, 30);
            var claimLabel = _loginClaim.GetComponentInChildren<TMP_Text>();
            claimLabel.fontStyle = FontStyles.Bold;
            ExpeditionUi.SetRect(claimLabel.rectTransform, .075f, .08f, .925f, .92f);
        }
        private void Refresh()
        {
            if (_subtitle == null) return;
            var state = _fixture ? _fixtureState : DailyRewardsService.Current;
            var busy = !_fixture && (DailyRewardsService.Busy || state != null && DailyRewardsService.EstimatedServerTime >= state.reset_at);
            if (state == null)
            {
                _status.text = DailyRewardsService.Error ?? "Загрузка наград…";
                if (_loginClaim != null)
                {
                    _loginClaim.interactable = false;
                    _loginClaimFrame.Present(false);
                }
                if (_questClaim != null) _questClaim.interactable = false;
                foreach (var card in _questCards) if (card != null) card.Notice.interactable = false;
                foreach (var day in _days) if (day != null) day.Camp.interactable = false;
                if (_bonusClaim != null) _bonusClaim.interactable = false;
                return;
            }
            if (_fixture || DailyRewardsService.EstimatedServerTime < state.reset_at) _resetRequested = false;
            RefreshTime(state);
            _status.text = !_fixture && DailyRewardsService.Error != null ? DailyRewardsService.Error : busy ? "Обновляем награды…" :
                _quests ? "Награды за задания можно забрать по отдельности." : "Пропуски сохраняют маршрут. После седьмой стоянки он начнётся заново.";
            if (state.receipt?.pending_items?.Length > 0) _status.text = "Предметы ждут свободного места в инвентаре.";
            if (_quests) RefreshQuests(state, busy); else RefreshLogin(state, busy);
        }
        private void RefreshQuests(DailyRewardsService.State state, bool busy)
        {
            if (_selectedQuest < 0)
            {
                _selectedQuest = Array.FindIndex(state.quests, quest => quest.ready && !quest.claimed);
                if (_selectedQuest < 0) _selectedQuest = 0;
            }
            var claimed = 0;
            for (var i = 0; i < 3; i++)
            {
                var q = state.quests[i];
                var card = _questCards[i];
                var selected = i == _selectedQuest;
                card.Notice.interactable = true;
                card.Title.text = QuestName(q);
                card.Title.color = selected ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                card.Icon.sprite = QuestIcon(q);
                card.Status.text = q.claimed ? "Получено" : q.ready ? "Готово" : "В работе";
                card.Status.color = q.claimed ? Green : q.ready ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                card.Fill.fillAmount = q.target > 0 ? Mathf.Clamp01((float)q.progress / q.target) : 0; card.Fill.color = q.claimed ? Green : ExpeditionTheme.Amber;
                Accent(card.Notice.image, selected);
                card.Rewards.Bind(q.reward, q.claimed);
                if (q.claimed) claimed++;
            }
            var current = state.quests[_selectedQuest];
            _contractTitle.text = QuestName(current);
            _contractDescription.text = QuestDescription(current);
            _contractIcon.sprite = QuestIcon(current);
            _contractProgress.text = $"{Math.Min(current.progress, current.target)} / {current.target}";
            _contractStatus.text = current.claimed ? "Награда получена" : current.ready ? "Готово к получению" : "Задание выполняется";
            _contractStatus.color = current.claimed ? Green : current.ready ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            _contractRewards.Bind(current.reward, current.claimed);
            _questClaim.interactable = !busy && current.ready && !current.claimed;
            _questClaim.GetComponentInChildren<TMP_Text>().text = current.claimed ? "ПОЛУЧЕНО" : current.ready ? "ПОЛУЧИТЬ" : "В ПРОЦЕССЕ";
            _bonusStatus.text = state.completion_claimed ? "Бонус получен" : state.completion_ready ? "Бонус готов к получению" : $"Получено наград {claimed} / 3";
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
            if (_displayedToday != today) _previewDay = today;
            _displayedToday = today;
            var preview = _previewDay != today;
            var collected = _previewDay <= completed;
            _focusKicker.text = $"Стоянка {_previewDay} из 7" + (preview ? "" : " · сегодня");
            _focusKicker.color = collected ? ClaimedText : ExpeditionTheme.Amber;
            _focusTitle.text = CampRewards[_previewDay - 1];
            _focusTitle.color = collected ? ClaimedText : ExpeditionTheme.Ivory;
            _focusRewards.Bind(login.rewards[_previewDay - 1], collected);
            _focusStatus.text = collected ? "Припасы получены" : preview ? "Припасы ждут на этой стоянке" :
                today == 7 ? "Особая награда за завершение маршрута" : "Припасы готовы к получению";
            _focusStatus.color = collected ? Green : ExpeditionTheme.Muted;
            _focusFrame.Configure(!preview || _previewDay == 7, collected);
            _focusClaimed.gameObject.SetActive(collected);
            for (var i = 0; i < 7; i++)
            {
                var past = i < completed; var current = i + 1 == today;
                var selected = i + 1 == _previewDay;
                var day = _days[i];
                day.Camp.interactable = true;
                day.Frame.Present(selected || current, past);
                day.Title.color = selected || i == 6 || current ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                day.Status.text = past ? "Получено" : current ? "Сегодня" : i == 6 ? "Финиш" : "Впереди";
                day.Status.color = past ? Green : current ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                day.Check.gameObject.SetActive(past);
                if (i < _trail.Length)
                    foreach (var dot in _trail[i])
                        dot.color = i + 1 < completed ? Green : i + 2 == today ? ExpeditionTheme.Amber : new Color32(199, 173, 127, 190);
            }
            _loginClaim.interactable = preview || !busy && login.can_claim;
            _loginClaimFrame.Present(_loginClaim.interactable, !preview && login.claimed_today);
            _loginClaim.GetComponentInChildren<TMP_Text>().text = preview ? "К СЕГОДНЯШНЕЙ СТОЯНКЕ" :
                login.can_claim ? "ЗАБРАТЬ ПРИПАСЫ" : "СЕГОДНЯ ПОЛУЧЕНО";
        }
        private void SelectQuest(int index)
        {
            if (_selectedQuest == index) return;
            _selectedQuest = index;
            Refresh();
            _contractArrival?.Play();
        }
        private void PreviewCamp(int day)
        {
            if (_previewDay == day || day < 1 || day > Camps.Length) return;
            _previewDay = day;
            Refresh();
            _focusArrival?.Play();
        }
        private static Sprite QuestIcon(DailyRewardsService.Quest q) =>
            ExpeditionTheme.Icon(q.id == "mine" ? "mine" : q.id == "abilities" ? "energy" : "arena");
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
        private sealed class QuestCard { public Button Notice; public Image Icon, Fill; public TMP_Text Title, Status; public RewardStrip Rewards; }
        private sealed class DayCard { public Button Camp; public ExpeditionSupplyFrame Frame; public ExpeditionDailyLoginFrame Check; public TMP_Text Title, Status; }
        private sealed class RewardStrip
        {
            private readonly ExpeditionDailyPanel _owner; private readonly RectTransform _root; private readonly RewardStyle _style;
            private readonly List<RewardTile> _tiles = new List<RewardTile>();
            public RewardStrip(ExpeditionDailyPanel owner, Transform parent, string name, float x0, float y0, float x1, float y1, RewardStyle style)
            { _owner = owner; _root = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1); _style = style; }
            public void Bind(DailyRewardsService.Reward reward, bool claimed = false)
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
                    tile.Icon.color = claimed ? ClaimedIcon : Color.white;
                    tile.Amount.text = (item.def_id == "gold" || item.def_id == "ore" || item.def_id == "matter" ? "" : "×") + item.amount;
                    tile.Amount.color = claimed ? ClaimedText : ExpeditionTheme.Amber;
                    if (tile.Caption != null) tile.Caption.text = _style == RewardStyle.Featured ? FullName(item.def_id) : ShortName(item.def_id);
                }
            }
            private RewardTile BuildTile()
            {
                var root = ExpeditionUi.Rect(_root, "Reward", 0, 0, 1, 1);
                if (_style != RewardStyle.Mini) ExpeditionUi.Fill(root, "RewardWell", 0, 0, 1, 1, new Color32(20, 48, 53, 150));
                var featured = _style == RewardStyle.Featured; var mini = _style == RewardStyle.Mini;
                var icon = featured ? Picture(root, "DailyRewardIcon", null, .08f, .50f, .92f, .98f) : Picture(root, "DailyRewardIcon", null, .015f, .08f, mini ? .64f : .36f, .94f);
                var amount = featured ? Label(root, "RewardAmount", "", 29, .035f, .245f, .965f, .50f, TextAlignmentOptions.Center) :
                    Label(root, "RewardAmount", "", mini ? 22 : 26, mini ? .60f : .39f, mini ? .18f : .42f, .98f, mini ? .86f : .91f, TextAlignmentOptions.Center);
                amount.fontStyle = FontStyles.Bold; amount.color = ExpeditionTheme.Amber; amount.textWrappingMode = TextWrappingModes.NoWrap;
                TMP_Text caption = null;
                if (!mini) caption = featured ? Label(root, "RewardName", "", 19, .025f, .005f, .975f, .245f, TextAlignmentOptions.Center) :
                    Label(root, "RewardName", "", 18, .39f, .055f, .98f, .42f, TextAlignmentOptions.Center);
                if (featured) caption.fontSizeMin = 14;
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
