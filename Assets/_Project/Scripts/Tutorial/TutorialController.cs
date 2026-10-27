using System;
using System.Collections;
using System.Collections.Generic;
using Project.Match3;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Project.Tutorial
{
    /// <summary>Interactive, isolated training UI. No Duel manager, account, reward or networking service is used here.</summary>
    public sealed class TutorialController : MonoBehaviour
    {
        [SerializeField] private Match3BoardView boardView;
        [SerializeField] private Canvas tutorialCanvas;
        [SerializeField] private Sprite[] abilityIcons = new Sprite[5];
        [SerializeField] private Match3PlayerPanel playerPanel;
        [SerializeField] private Match3PlayerPanel enemyPanel;
        private bool _built, _resolving, _exited, _exitToMenu = true;
        private int _selectedX = -1, _selectedY = -1;
        private TutorialSession _session;
        private TMP_Text _stepText, _title, _body, _feedback;
        private RectTransform _content, _legend, _modes, _finish, _skipConfirm;
        private Match3AbilityPanel _abilityPanel;
        private Match3AbilityFx _effects;
        private int _shownHp, _shownMana, _shownEnemyHp;
        private TutorialFocusArrow _arrow;
        private Button _next, _back, _restart;
        private readonly Button[] _abilities = new Button[5], _resources = new Button[5];
        private readonly Image[] _steps = new Image[TutorialSession.StepCount];
        public bool Initialized => _built;
        public bool Resolving => _resolving;
        public TutorialSession Session => _session;
        public Match3PlayerPanel PlayerPanel => playerPanel;
        public Match3PlayerPanel EnemyPanel => enemyPanel;
        public Match3AbilityPanel AbilityPanel => _abilityPanel;
        public Match3AbilityFx Effects => _effects;
        public int DisplayedHp => _shownHp;
        public int DisplayedMana => _shownMana;
        public int DisplayedEnemyHp => _shownEnemyHp;
        public event Action<TutorialExitReason> ExitRequested;

        private void Start()
        {
            Initialize(); TutorialLauncher.MarkEntered();
        }
        private void Initialize()
        {
            if (_built) return;
            if (boardView == null || tutorialCanvas == null || playerPanel == null || enemyPanel == null) throw new InvalidOperationException("Prepare the Tutorial scene with TutorialSceneTools.Prepare.");
            EnsureEventSystem();
            _session = new TutorialSession(); _built = true;
            var canvas = tutorialCanvas.transform;
            var backdrop = ExpeditionUi.Fill(canvas, "TutorialBackground", 0, 0, 1, 1, Color.white);
            backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/battle-world-v5") ?? Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
            if (backdrop.sprite == null) backdrop.color = ExpeditionTheme.Panel;
            backdrop.transform.SetAsFirstSibling();
            ExpeditionUi.Fill(canvas, "TutorialShade", 0, 0, 1, 1, new Color(0, .025f, .035f, .55f)).transform.SetSiblingIndex(1);
            _content = ExpeditionUi.Rect(canvas, "TutorialSafeArea", 0, 0, 1, 1); _content.gameObject.AddComponent<ExpeditionSafeArea>();
            Label(_content, "TutorialHeading", "УЧЕБНЫЙ СПУСК", 31, .055f, .95f, .76f, .985f).color = ExpeditionTheme.Amber;
            ExpeditionUi.Button(_content, "TutorialSkip", "Пропустить", .77f, .948f, .95f, .986f, AskSkip, false, 22);
            var coach = ExpeditionUi.Panel(_content, "TutorialCoach", .045f, .8f, .955f, .94f).rectTransform;
            _stepText = Label(coach, "TutorialStep", "", 20, .035f, .79f, .965f, .955f); _stepText.color = ExpeditionTheme.Amber;
            _title = Label(coach, "TutorialLessonTitle", "", 31, .035f, .56f, .965f, .8f); _title.fontStyle = FontStyles.Bold;
            _body = Label(coach, "TutorialInstruction", "", 24, .035f, .16f, .965f, .56f);
            _feedback = Label(coach, "TutorialFeedback", "", 19, .035f, .015f, .965f, .16f); _feedback.color = ExpeditionTheme.Amber;
            BuildBattlePanels();
            boardView.transform.SetParent(_content, false); ExpeditionUi.SetRect(boardView.transform as RectTransform, .07f, .247f, .93f, .675f);
            boardView.Build(); boardView.CellClicked += CellClicked; boardView.CellSwiped += Swapped;
            var frame = boardView.transform.Find("Frame")?.GetComponent<Image>(); if (frame != null) ExpeditionUi.StylePanel(frame, ExpeditionTheme.Panel);
            BuildBattleAbilities();
            _back = ExpeditionUi.Button(_content, "TutorialBack", "Назад", .055f, .039f, .26f, .102f, Back, false, 25);
            _restart = ExpeditionUi.Button(_content, "TutorialRestart", "Заново", .28f, .039f, .52f, .102f, Restart, false, 25);
            _next = ExpeditionUi.Button(_content, "TutorialNext", "Далее", .54f, .039f, .945f, .102f, Next, true, 28);
            for (var i = 0; i < _steps.Length; i++) _steps[i] = ExpeditionUi.Fill(_content, "TutorialProgress" + i, .06f + i * .074f, .117f, .125f + i * .074f, .125f, ExpeditionTheme.Teal);
            BuildLegend(); BuildModes(); BuildFinish();
            _arrow = ExpeditionUi.Rect(_content, "TutorialPointer", 0, 0, 1, 1).gameObject.AddComponent<TutorialFocusArrow>();
            _arrow.color = new Color32(47, 255, 36, 255); _arrow.raycastTarget = false;
            SynchronizePanels(false); Refresh();
        }
        private void BuildBattlePanels()
        {
            foreach (var panel in new[] { playerPanel, enemyPanel })
            {
                var enemy = panel == enemyPanel;
                panel.transform.SetParent(_content, false);
                ExpeditionUi.SetRect(panel.transform as RectTransform, enemy ? .51f : .025f, .707f, enemy ? .975f : .49f, .79f);
                // These optional labels are normally authored by the live battle
                // host. Supply only their view data, then use its shared styler.
                if (panel.buffStateText == null) panel.buffStateText = ExpeditionUi.Text(panel.transform, "BuffStateText", "", 22, 0, 0, 1, 1);
                if (panel.combatStatsName == null) panel.combatStatsName = ExpeditionUi.Text(panel.transform, "CombatStatsName", "", 22, 0, 0, 1, 1);
                if (panel.avatarPlaceholderText != null) panel.avatarPlaceholderText.gameObject.SetActive(false);
                panel.UseExpeditionStyle(); panel.SetPlayerName(enemy ? "Манекен" : "Вы"); panel.UpdateAvatarLevel(1);
                panel.UpdateCombatStats(0, 0, 0, 0); panel.UpdateBuffState(0, 0);
            }
            _effects = Match3AbilityFx.Ensure(playerPanel);
        }
        private void BuildBattleAbilities()
        {
            var root = ExpeditionUi.Rect(_content, "TutorialBattleAbilities", .025f, .137f, .975f, .237f);
            _abilityPanel = root.gameObject.AddComponent<Match3AbilityPanel>(); _abilityPanel.UseExpeditionStyle();
            foreach (var ability in new[] { AbilityType.Petard, AbilityType.Cross, AbilityType.Square, AbilityType.Shield, AbilityType.Fury })
            {
                var current = ability;
                var button = ExpeditionUi.Button(root, "TutorialAbility" + ability, "", 0, 0, 1, 1, () => AbilityClicked(current), false, 20);
                var old = button.GetComponentInChildren<TMP_Text>(); if (old != null) old.gameObject.SetActive(false);
                var icon = ExpeditionUi.Fill(button.transform, "AbilityIcon", 0, 0, 1, 1, Color.white);
                icon.sprite = abilityIcons[(int)ability]; icon.raycastTarget = false; icon.preserveAspect = true;
                ExpeditionUi.Text(button.transform, "AbilityCost", TutorialRules.Cost(ability).ToString(), 24, 0, 0, 1, 1).raycastTarget = false;
                _abilities[(int)ability] = button;
            }
            _abilityPanel.petardButton = _abilities[(int)AbilityType.Petard]; _abilityPanel.crossButton = _abilities[(int)AbilityType.Cross];
            _abilityPanel.squareButton = _abilities[(int)AbilityType.Square]; _abilityPanel.shieldButton = _abilities[(int)AbilityType.Shield]; _abilityPanel.furyButton = _abilities[(int)AbilityType.Fury];
            ExpeditionBattleLayout.ApplyAbilityLayout(_abilityPanel);
            foreach (var button in _abilities)
            {
                var name = button.transform.Find("ExpeditionAbilityName").GetComponent<TMP_Text>();
                // The tutorial row is shorter than the combat row: retain the
                // same title typography without spending its height on margins.
                name.margin = Vector4.zero; name.enableAutoSizing = true;
                name.fontSizeMin = 17; name.fontSizeMax = 23; name.fontSize = 23;
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.overflowMode = TextOverflowModes.Overflow;
            }
        }
        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("TutorialEventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
        private void BuildLegend()
        {
            _legend = ExpeditionUi.Panel(_content, "TutorialResources", .085f, .275f, .915f, .674f).rectTransform;
            var names = new[] { "Канистра", "Компас", "Рюкзак", "Бомба", "Капсула" };
            var values = new[] { "+5 маны за предмет", "+3 маны за предмет", "+1 маны за предмет · не повышает крит", "5 базового урона за предмет", "+1 здоровья за предмет" };
            for (var i = 0; i < 5; i++)
            {
                var type = (PieceType)(i + 1); var y = .79f - i * .19f;
                var button = ExpeditionUi.Button(_legend, "TutorialResource" + type, "", .03f, y, .97f, y + .175f, () => ResourceClicked(type), false, 24);
                var existing = button.GetComponentInChildren<TMP_Text>(); existing.gameObject.SetActive(false);
                var image = ExpeditionUi.Rect(button.transform, "PieceIcon", .025f, .09f, .18f, .91f).gameObject.AddComponent<RawImage>();
                image.texture = boardView.GetBallsAtlasTexture(); image.uvRect = PieceUv(type); image.raycastTarget = false;
                Label(button.transform, "ResourceName", names[i], 27, .22f, .46f, .965f, .89f).fontStyle = FontStyles.Bold;
                Label(button.transform, "ResourceEffect", values[i], 21, .22f, .10f, .965f, .47f).color = ExpeditionTheme.Muted;
                _resources[i] = button;
            }
        }
        private void BuildModes()
        {
            _modes = ExpeditionUi.Panel(_content, "TutorialModes", .085f, .31f, .915f, .65f).rectTransform;
            Label(_modes, "ModeQuestion", "Где снаряжение усилит героя?", 30, .05f, .77f, .95f, .94f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Button(_modes, "TutorialChooseClassic", "Обычная дуэль\nРавные базовые условия", .05f, .43f, .95f, .73f, () => ModeClicked(false), false, 27);
            ExpeditionUi.Button(_modes, "TutorialChoosePro", "Дуэль Pro\nУровень и снаряжение", .05f, .08f, .95f, .38f, () => ModeClicked(true), true, 27);
        }
        private void BuildFinish()
        {
            _finish = ExpeditionUi.Panel(_content, "TutorialFinishCard", .085f, .3f, .915f, .66f).rectTransform;
            Label(_finish, "FinishTitle", "ВЫ ГОТОВЫ К СПУСКУ", 32, .05f, .77f, .95f, .94f).color = ExpeditionTheme.Amber;
            Label(_finish, "FinishSummary", "Собирайте ману и следите за здоровьем.\n\nВыбирайте форму способности и подходящий момент.\n\nОбучение можно повторить: Настройки / Как играть.", 27, .06f, .18f, .94f, .76f);
            Label(_finish, "FinishSafe", "Тренировка завершена. Припасы не расходовались.", 21, .06f, .04f, .94f, .17f).color = ExpeditionTheme.Muted;
        }
        private void Refresh()
        {
            if (!_built) return;
            _stepText.text = "ШАГ " + (_session.Step + 1) + " ИЗ " + TutorialSession.StepCount + " · ТРЕНИРОВКА";
            var titles = new[] { "Добро пожаловать в экспедицию", "Первый ход: соберите ману", "Пять предметов — разные свойства", "Бомбы наносят урон", "Капсулы восстанавливают здоровье", "Пять в ряд — ещё один ход", "Крест: добраться до нужных клеток", "Квадрат: собрать плотную группу", "Когда нужна защита", "Ярость и петарда: ловите момент", "Обычная дуэль и Pro", "Первый спуск впереди" };
            _title.text = titles[_session.Step]; _body.text = Instruction(); _feedback.text = _session.Feedback;
            playerPanel.UpdateStats(_shownHp, TutorialRules.MaxHp, _shownMana, TutorialRules.MaxMana);
            enemyPanel.UpdateStats(_shownEnemyHp, TutorialRules.MaxHp, 0, TutorialRules.MaxMana);
            playerPanel.UpdateBuffState(_session.ShieldStacks, _session.ShieldStacks > 0 ? TutorialRules.ShieldTurns : 0);
            playerPanel.SetFuryVisual(_session.FuryActive);
            _next.interactable = _session.Passed && !_resolving; _next.GetComponentInChildren<TMP_Text>().text = _session.Step == TutorialSession.StepCount - 1 ? "Завершить" : "Далее";
            _back.interactable = _session.Step > 0 && !_resolving; _restart.interactable = !_exited;
            _legend.gameObject.SetActive(_session.Step == 2); _modes.gameObject.SetActive(_session.Step == 10); _finish.gameObject.SetActive(_session.Step == 11);
            for (var i = 0; i < _steps.Length; i++) _steps[i].color = i < _session.Step ? new Color32(132, 185, 151, 255) : i == _session.Step ? ExpeditionTheme.Amber : ExpeditionTheme.Teal;
            _abilityPanel.Refresh(new PlayerStats { mana = _shownMana }, !_resolving && !_session.Passed, false,
                TutorialRules.Cost(AbilityType.Cross), TutorialRules.Cost(AbilityType.Square), TutorialRules.Cost(AbilityType.Petard), TutorialRules.Cost(AbilityType.Shield), TutorialRules.Cost(AbilityType.Fury));
            for (var i = 0; i < _abilities.Length; i++)
            {
                var ability = (AbilityType)i;
                _abilities[i].interactable = !_resolving && !_session.Passed && (_session.Step == 6 && ability == AbilityType.Cross || _session.Step == 7 && ability == AbilityType.Square ||
                    _session.Step == 8 && ability == AbilityType.Shield || _session.Step == 9 && (!_session.FuryActive && ability == AbilityType.Fury || _session.FuryActive && ability == AbilityType.Petard));
            }
            RefreshFocus();
        }
        private void SynchronizePanels(bool popups)
        {
            if (popups)
            {
                playerPanel.ShowGainPopups(Math.Max(0, _session.Hp - _shownHp), Math.Max(0, _session.Mana - _shownMana));
                playerPanel.ShowDamagePopup(Math.Max(0, _shownHp - _session.Hp), false);
                enemyPanel.ShowDamagePopup(Math.Max(0, _shownEnemyHp - _session.EnemyHp), false);
            }
            _shownHp = _session.Hp; _shownMana = _session.Mana; _shownEnemyHp = _session.EnemyHp;
        }
        private string Instruction()
        {
            switch (_session.Step)
            {
                case 0: return "Короткая тренировка без затрат и наград. Попробуем ход, свойства предметов и способности. Подсказки помогут — ошибаться можно.";
                case 1: return "Поменяйте местами подсвеченные предметы: перетащите один к другому или нажмите по очереди. Нужна линия из 3 и более одинаковых.";
                case 2: return "Канистры, компасы и рюкзаки дают ману. Посмотрите значения и нажмите на компас. Рюкзак не увеличивает шанс критического удара.";
                case 3: return "Соберите три бомбы. В этом примере нет брони и бонусов: каждая добавит 5 урона. В настоящем бою итог меняют броня и крит.";
                case 4: return "Соберите три капсулы. Каждая лечит на 1. В шахте и Pro к лечению добавляется бонус героя; щит тоже усиливает лечение.";
                case 5: return "Соберите пять компасов в ряд. Линия из 3 или 4 срабатывает обычно; линия из 5 и более сохраняет вам дополнительный ход.";
                case 6: return _session.Targeting ? "Нажмите центр подсвеченного креста. Он захватывает по 2 клетки от центра по вертикали и горизонтали — максимум 9." : "Крест стоит 20 маны. Собирает свойства клеток и наносит ещё 3 базового урона. Нажмите способность, затем выберите центр.";
                case 7: return _session.Targeting ? "Нажмите центр подсвеченного квадрата 3 × 3. Эта форма удобна, когда нужные предметы лежат рядом." : "Квадрат тоже стоит 20 маны: область 3 × 3, свойства клеток и 3 базового урона. Сравните форму и нажмите квадрат.";
                case 8: return "Манекен готовит удар на 15. Поднимите щит за 40 маны: слой даёт +4 брони и +3 лечения. До 3 слоёв, на 3 своих хода; ход сохраняется.";
                case 9: return !_session.FuryActive ? "На поле бомб: " + _session.CountBombs() + ". Ярость за 30 маны добавит столько же к урону и +18 п.п. к шансу крита до передачи хода. Нажмите ярость." : "Теперь петарда за 30 маны: 15 базового урона плюс ярость, без изменения поля. Ход сохраняется. Крит не гарантирован и удваивает урон.";
                case 10: return "В обычной дуэли базовые условия равны. В шахте и Pro учитываются уровень, снаряжение, достижения и коллекция. В шахте аномалии могут менять цифры.";
                default: return "Вы освоили основы. Нажмите «Завершить», чтобы вернуться в убежище. Настоящие бои начнутся только по вашему выбору.";
            }
        }
        private void CellClicked(int x, int y)
        {
            if (_resolving || _exited || _skipConfirm != null) return;
            if (_session.Targeting)
            {
                var ability = _session.Step == 6 ? AbilityType.Cross : AbilityType.Square;
                if (_session.Target(x, y)) { StartCoroutine(ResolveAbility(ability)); return; }
                Refresh(); return;
            }
            if (_selectedX < 0) { _selectedX = x; _selectedY = y; boardView.SetCellSelected(x, y, true); return; }
            var sx = _selectedX; var sy = _selectedY; _selectedX = _selectedY = -1; Swapped(sx, sy, x, y);
        }
        private void Swapped(int x0, int y0, int x1, int y1)
        {
            if (_resolving || _exited || _skipConfirm != null) return;
            _selectedX = _selectedY = -1;
            if (_session.Swap(x0, y0, x1, y1, out var matches)) { StartCoroutine(ResolveSwap(x0, y0, x1, y1, matches)); return; }
            boardView.RefreshAll(_session.Board); Refresh();
        }
        private IEnumerator ResolveSwap(int x0, int y0, int x1, int y1, List<MatchResult> matches)
        {
            _resolving = true; Refresh();
            yield return boardView.AnimateSwap(x0, y0, x1, y1, .23f); boardView.RefreshAll(_session.Board);
            yield return boardView.AnimateClear(matches, .22f); _session.Board.ClearMatchedCells(matches);
            SynchronizePanels(true); Refresh();
            var before = _session.Board.ToArray(); _session.Board.ApplyGravityAndRefill(); boardView.RefreshAll(_session.Board);
            yield return boardView.AnimateDrop(before, _session.Board, .23f);
            _resolving = false; Refresh();
        }
        private IEnumerator ResolveAbility(AbilityType ability)
        {
            _resolving = true; Refresh(); yield return boardView.AnimateAbilityArea(ability, 2, 2, .3f);
            SynchronizePanels(true); Refresh();
            var before = _session.Board.ToArray(); _session.Board.ApplyGravityAndRefill(); boardView.RefreshAll(_session.Board);
            yield return boardView.AnimateDrop(before, _session.Board, .23f); _resolving = false; Refresh();
        }
        private void AbilityClicked(AbilityType ability)
        {
            if (_resolving || _exited || _skipConfirm != null) return;
            if (!_session.Ability(ability)) { Refresh(); return; }
            if (ability == AbilityType.Petard) { StartCoroutine(ResolvePetard()); return; }
            if (ability == AbilityType.Shield) { StartCoroutine(ResolveShield()); return; }
            // Selecting an area never spends mana. Fury spends at activation,
            // while its real avatar fire stays alive through the following throw.
            if (ability == AbilityType.Fury) SynchronizePanels(false);
            Refresh();
        }
        private IEnumerator ResolvePetard()
        {
            _resolving = true; _shownMana = _session.Mana; Refresh();
            yield return _effects.FlyPetard(playerPanel, enemyPanel, false);
            if (_session.ResolvePetardImpact()) SynchronizePanels(true);
            Refresh(); yield return _effects.WaitForImpact();
            _resolving = false; Refresh();
        }
        private IEnumerator ResolveShield()
        {
            _resolving = true; _shownMana = _session.Mana; Refresh(); _effects.PlayShield(playerPanel);
            yield return new WaitForSecondsRealtime(.48f);
            SynchronizePanels(true); Refresh();
            yield return new WaitForSecondsRealtime(.32f); _resolving = false; Refresh();
        }
        private void ResourceClicked(PieceType type) { if (_resolving || _exited || _skipConfirm != null) return; _session.ChooseResource(type); Refresh(); }
        private void ModeClicked(bool pro) { if (_resolving || _exited || _skipConfirm != null) return; _session.ChooseMode(pro); Refresh(); }
        private void Next()
        {
            if (_resolving || _exited || !_session.Passed || _skipConfirm != null) return;
            if (_session.Step == TutorialSession.StepCount - 1) { Finish(TutorialExitReason.Completed); return; }
            _session.Next(); EnterView();
        }
        private void Back() { if (!_resolving && !_exited && _skipConfirm == null && _session.Back()) EnterView(); }
        public void Restart()
        {
            if (!_built) return; StopAllCoroutines(); _effects?.CancelTransient(); CloseSkip(); _resolving = _exited = false; _session.Restart(); EnterView();
        }
        private void EnterView()
        {
            _selectedX = _selectedY = -1; ClearTransientPopups(playerPanel); ClearTransientPopups(enemyPanel);
            SynchronizePanels(false); boardView.RefreshAll(_session.Board); Refresh();
        }
        private static void ClearTransientPopups(Match3PlayerPanel panel)
        {
            if (panel == null) return;
            if (panel.damagePopup != null)
            {
                panel.damagePopup.StopAllCoroutines();
                var group = panel.damagePopup.GetComponent<CanvasGroup>(); if (group != null) group.alpha = 0;
                foreach (var value in panel.damagePopup.GetComponentsInChildren<TMP_Text>(true)) value.text = "";
            }
            if (panel.gainPopup != null)
            {
                // Let the real owner's short coroutine return its flyout to its
                // pool. Hiding the child prevents an old lesson resurfacing;
                // cancelling the owner would strand its private active list.
                foreach (Transform flyout in panel.gainPopup.transform)
                    if (flyout.name == "GainFlyout") flyout.gameObject.SetActive(false);
            }
        }
        private void Finish(TutorialExitReason reason)
        {
            if (_exited) return; _exited = true; StopAllCoroutines(); _effects?.CancelTransient(); playerPanel.SetFuryVisual(false); ExitRequested?.Invoke(reason);
            if (_exitToMenu) TutorialLauncher.Exit(reason);
        }
        private void AskSkip()
        {
            if (_skipConfirm != null || _exited) return;
            _skipConfirm = ExpeditionUi.Rect(_content, "TutorialSkipConfirmation", 0, 0, 1, 1);
            ExpeditionUi.Fill(_skipConfirm, "Shade", 0, 0, 1, 1, new Color(0, 0, 0, .88f)).raycastTarget = true;
            var panel = ExpeditionUi.Panel(_skipConfirm, "Panel", .07f, .34f, .93f, .66f).rectTransform;
            Label(panel, "SkipTitle", "Пропустить тренировку?", 33, .05f, .73f, .95f, .93f);
            Label(panel, "SkipHint", "Вы сможете повторить её в любой момент:\nНастройки / Как играть.", 27, .05f, .42f, .95f, .73f);
            ExpeditionUi.Button(panel, "TutorialKeepLearning", "Продолжить обучение", .05f, .23f, .95f, .39f, CloseSkip, true, 27);
            ExpeditionUi.Button(panel, "TutorialConfirmSkip", "Пропустить", .05f, .05f, .95f, .21f, () => Finish(TutorialExitReason.Skipped), false, 26);
            _arrow.enabled = false;
        }
        private void CloseSkip() { if (_skipConfirm != null) Destroy(_skipConfirm.gameObject); _skipConfirm = null; if (_arrow != null) RefreshFocus(); }
        private void RefreshFocus()
        {
            if (_arrow == null) return;
            _arrow.From = _arrow.To = null; _arrow.enabled = !_resolving && !_session.Passed && _skipConfirm == null;
            for (var y = 0; y < 6; y++) for (var x = 0; x < 6; x++) boardView.SetCellSelected(x, y, false);
            if (!_arrow.enabled) return;
            if (_session.Step == 1 || _session.Step == 3 || _session.Step == 4 || _session.Step == 5)
            { _arrow.From = Cell(2, 3); _arrow.To = Cell(2, 2); boardView.SetCellSelected(2, 3, true); boardView.SetCellSelected(2, 2, true); }
            else if (_session.Step == 2) _arrow.To = _resources[1].transform as RectTransform;
            else if (_session.Targeting)
            {
                _arrow.To = Cell(2, 2);
                for (var y = 0; y < 6; y++) for (var x = 0; x < 6; x++)
                    if (_session.Step == 6 ? (x == 2 && Math.Abs(y - 2) <= 2 || y == 2 && Math.Abs(x - 2) <= 2) : Math.Abs(x - 2) <= 1 && Math.Abs(y - 2) <= 1) boardView.SetCellSelected(x, y, true);
            }
            else if (_session.Step >= 6 && _session.Step <= 9)
            { var ability = _session.Step == 6 ? AbilityType.Cross : _session.Step == 7 ? AbilityType.Square : _session.Step == 8 ? AbilityType.Shield : _session.FuryActive ? AbilityType.Petard : AbilityType.Fury; _arrow.To = _abilities[(int)ability].transform as RectTransform; }
            else if (_session.Step == 10) _arrow.To = _modes.Find("TutorialChoosePro") as RectTransform;
        }
        private RectTransform Cell(int x, int y) => boardView.cellContainer.Find("Cell_" + x + "_" + y) as RectTransform;
        private static string AbilityName(AbilityType ability) => ability == AbilityType.Cross ? "Крест" : ability == AbilityType.Square ? "Квадрат" : ability == AbilityType.Petard ? "Петарда" : ability == AbilityType.Shield ? "Щит" : "Ярость";
        private static Rect PieceUv(PieceType type) => new Rect((type == PieceType.Ankh ? 0 : (int)type) / 5f, 0, .2f, 1);
        private static TMP_Text Label(Transform parent, string name, string value, int size, float x0, float y0, float x1, float y1)
        {
            var label = ExpeditionUi.Text(parent, name, value, size, x0, y0, x1, y1); label.margin = Vector4.zero;
            label.enableAutoSizing = true; label.fontSizeMax = size; label.fontSizeMin = size - 3; label.overflowMode = TextOverflowModes.Overflow; return label;
        }
        private static Image Bar(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
        {
            var track = ExpeditionUi.Fill(parent, name + "Track", x0, y0, x1, y1, new Color32(3, 15, 19, 255));
            var fill = ExpeditionUi.Fill(track.transform, name, 0, 0, 1, 1, color); fill.sprite = ModalPanelCloseButton.WhiteSprite(); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; return fill;
        }
        private void Update()
        {
            if (!_built || _exited) return;
#if ENABLE_INPUT_SYSTEM
            var back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            var back = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (back) { if (_skipConfirm != null) CloseSkip(); else if (_session.Step > 0) Back(); else AskSkip(); }
        }
        private void OnDestroy() { if (boardView != null) { boardView.CellClicked -= CellClicked; boardView.CellSwiped -= Swapped; } }
    }
}
