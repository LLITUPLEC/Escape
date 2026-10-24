using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Side cutaway mine. Twelve fixed landings are a view of the existing server state.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionMinePresentation : MonoBehaviour
    {
        public struct FloorState
        {
            public int floor;
            public bool unlocked, boss, current, hasCatalog;
            public int respawnSeconds, wins;
            public string name, rewards, barrierCost;
            public Sprite portrait;
        }

        private static readonly Color Ink = new Color32(10, 25, 29, 255);
        private static readonly Color Ivory = new Color32(235, 223, 198, 255);
        private static readonly Color Muted = new Color32(165, 184, 174, 255);
        private static readonly Color Amber = new Color32(237, 170, 83, 255);
        private float _landingHeight = 500f, _geometryViewportHeight;
        private readonly Landing[] _landings = new Landing[12];
        private readonly Image[] _depthMarks = new Image[12];
        private readonly Text[] _depthLabels = new Text[12];
        private Action<int> _openDossier, _summon;
        private ScrollRect _scroll;
        private RectTransform _content, _safeContent;
        private RectTransform _liftCarrier;
        private Coroutine _tapTravel;
        private Text _selectionTitle, _selectionName, _selectionInfo, _selectionRewards, _actionLabel, _summonLabel;
        private Image _selectionPortrait, _selectionGate;
        private Button _action, _summonButton;
        private Sprite _landingArt, _dampLandingArt, _blockedArt, _cabinArt, _gateArt;
        private int _selectedFloor = 1;
        private bool _selectedInitialProgress, _built, _summonBusy;
        public int SelectedFloor => _selectedFloor;

        private IEnumerator Start()
        {
            yield return null;
            ReflowRoute();
        }

        private void OnDisable() { CancelTapTravel(); }

        public static ExpeditionMinePresentation Install(Transform background, Action<int> openDossier, Action<int> summon)
        {
            if (background == null) return null;
            var view = background.GetComponentInChildren<ExpeditionMinePresentation>(true);
            if (view == null)
            {
                var go = new GameObject("ExpeditionMinePresentation", typeof(RectTransform));
                go.transform.SetParent(background, false);
                view = go.AddComponent<ExpeditionMinePresentation>();
            }
            view._openDossier = openDossier;
            view._summon = summon;
            if (!view._built) view.Build();
            return view;
        }

        private void Build()
        {
            _built = true;
            Rect((RectTransform)transform, 0, 0, 1, 1);
            transform.SetAsFirstSibling();
            foreach (var name in new[] { "CardsScrollView", "FloorLift", "Title", "BackButton" })
            {
                var old = transform.parent.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }
            // The shared scene shell owns the full-bleed world; only local rooms belong to this view.
            _landingArt = Resources.Load<Sprite>("Expedition/Art/mine-chamber-v3");
            _dampLandingArt = Resources.Load<Sprite>("Expedition/Art/mine-chamber-damp-v3");
            _blockedArt = Resources.Load<Sprite>("Expedition/Art/mine-blocked-v3");
            _cabinArt = Resources.Load<Sprite>("Expedition/Art/mine-lift-cabin-v3");
            _gateArt = Resources.Load<Sprite>("Expedition/Art/mine-gate-v2");
            _safeContent = ExpeditionUi.Rect(transform, "MineSafeArea", 0, 0, 1, 1);
            _safeContent.gameObject.AddComponent<ExpeditionSafeArea>();
            var tabs = transform.parent.Find("DifficultyTabs");
            if (tabs != null) { tabs.SetParent(_safeContent, false); StyleDifficultyTabs(tabs); }
            BuildRoute();
            BuildOverview();
            BuildSelection();
            SelectFloor(1, false);
        }

        private void BuildRoute()
        {
            var route = Image(_safeContent, "MineCutawayViewport", Color.clear, .012f, .308f, .918f, .874f);
            route.raycastTarget = true;
            route.gameObject.AddComponent<RectMask2D>();
            var routeScroll = route.gameObject.AddComponent<ExpeditionMineRouteScroll>();
            routeScroll.owner = this;
            _scroll = routeScroll;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.viewport = route.rectTransform;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 52f;
            _scroll.decelerationRate = .08f;
            _content = ExpeditionUi.Rect(route.transform, "ConnectedLandings", 0, 1, 1, 1);
            _content.pivot = new Vector2(.5f, 1f);
            _content.sizeDelta = new Vector2(0, 12 * _landingHeight);
            _scroll.content = _content;
            var shaft = ExpeditionUi.Rect(_content, "DedicatedLiftShaft", 0, 0, .33f, 1);
            shaft.gameObject.AddComponent<ExpeditionMineShaftGraphic>().raycastTarget = false;
            for (var floor = 1; floor <= 12; floor++) BuildLanding(floor);
            BuildLift();
            ReflowRoute();
            ExpeditionLocalAtmosphere.Install(route.transform, false);
        }

        private void BuildLanding(int floor)
        {
            var rt = ExpeditionUi.Rect(_content, "Landing_" + floor.ToString("00"), 0, 1, 1, 1);
            rt.pivot = new Vector2(.5f, 1f);
            rt.sizeDelta = new Vector2(0, _landingHeight);
            // Enter at floor one and descend: every landing keeps its floor-bound creature, gate and lift.
            rt.anchoredPosition = new Vector2(0, -(floor - 1) * _landingHeight);
            var room = ExpeditionUi.Rect(rt, "ChamberBounds", .33f, 0, 1, 1);
            room.gameObject.AddComponent<RectMask2D>();
            var damp = floor % 3 == 0 || floor % 4 == 0;
            var art = Image(room, "RockChamber", Color.white, 0, damp ? .035f : 0, 1, damp ? 1.035f : 1);
            art.sprite = damp && _dampLandingArt != null ? _dampLandingArt : _landingArt;
            if (_landingArt == null) art.color = new Color32(23, 48, 52, 255);
            // Mirrored lighting and small seeded surface details vary the fixed chambers without
            // stretching one wide shaft-and-floor picture into twelve thick repeating beams.
            if (floor % 4 == 2) art.rectTransform.localScale = new Vector3(-1, 1, 1);
            var details = ExpeditionUi.Rect(room, "SeededCaveDetail", 0, .25f, 1, 1).gameObject.AddComponent<ExpeditionMineCaveDetail>();
            details.seed = floor; details.raycastTarget = false;
            var hit = Image(rt, "LandingTouch", Color.clear, .015f, .025f, .995f, .98f);
            var button = hit.gameObject.AddComponent<Button>();
            hit.raycastTarget = true;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectFloor(floor, true));
            var shadow = Image(rt, "CreatureContactShadow", new Color(0, 0, 0, .24f), .52f, .245f, .91f, .285f);
            var portrait = Image(rt, "CreatureInChamber", Color.clear, .43f, .265f, .97f, .95f);
            portrait.preserveAspect = true;
            var barrier = ExpeditionUi.Rect(room, "ChainedUnknownPassage", 0, .12f, 1, 1);
            Image(barrier, "UnknownDepth", new Color32(3, 12, 16, 220), 0, .12f, 1, 1);
            var sealedPassage = Image(barrier, "PaintedRubbleAndMist", Color.white, 0, 0, 1, 1);
            sealedPassage.sprite = _blockedArt;
            if (_blockedArt == null) sealedPassage.color = new Color32(5, 16, 19, 245);
            var cost = Plate(rt, "BarrierPlaque", .47f, .17f, .96f, .305f);
            var costLabel = Text(cost.transform, "ActualCost", "", 25, Ivory, TextAnchor.MiddleCenter, .065f, .15f, .935f, .85f);
            var numberPlate = Plate(rt, "LiftFloorPlate", .012f, .58f, .105f, .81f);
            numberPlate.raycastTarget = true;
            var floorButton = numberPlate.gameObject.AddComponent<Button>();
            floorButton.transition = Selectable.Transition.None;
            floorButton.onClick.AddListener(() => SelectFloor(floor, true));
            var number = Text(numberPlate.transform, "FloorNumber", floor.ToString(), 38, Ivory,
                TextAnchor.MiddleCenter, .06f, .04f, .94f, .96f, true);
            var selectedRim = Image(numberPlate.transform, "SelectedAmberRim", Amber, 0, 0, 1, .05f);
            selectedRim.gameObject.SetActive(false);
            var banner = Plate(rt, "BossMarker", .62f, .83f, .97f, .97f);
            Text(banner.transform, "BossSymbol", "ХРАНИТЕЛЬ", 23, Amber, TextAnchor.MiddleCenter, .25f, .15f, .92f, .85f, true);
            var bossIcon = Image(banner.transform, "BossIcon", new Color(1f, .67f, .42f), .07f, .24f, .22f, .76f);
            bossIcon.sprite = ExpeditionTheme.Icon("swords");
            bossIcon.preserveAspect = true;
            banner.gameObject.SetActive(floor % 4 == 0);
            var timerPlate = Plate(rt, "RespawnTimerOnLanding", .44f, .40f, .95f, .57f);
            var stateText = Text(timerPlate.transform, "WorldStatus", "", 27, Ivory, TextAnchor.MiddleCenter, .07f, .15f, .93f, .85f);
            _landings[floor - 1] = new Landing { state = new FloorState { floor = floor, boss = floor % 4 == 0 },
                rect = rt, portrait = portrait, barrier = barrier.gameObject, cost = cost, costLabel = costLabel, number = number,
                selectedRim = selectedRim, status = stateText, shadow = shadow, bossBanner = banner.gameObject, timerPlate = timerPlate.gameObject };
        }

        private void BuildLift()
        {
            _liftCarrier = ExpeditionUi.Rect(_content, "TravellingLift", 0, 1, 1, 1);
            _liftCarrier.pivot = new Vector2(.5f, 1);
            _liftCarrier.sizeDelta = new Vector2(0, _landingHeight);
            // This separate shaft is wide enough for the actual cage. The hero sits behind its
            // foreground posts and safety rail instead of floating in a decorative UI frame.
            var cabin = ExpeditionUi.Rect(_liftCarrier, "LiftCabin", .10f, .23f, .325f, .94f);
            var hero = Image(cabin, "ExplorerAtLift", Color.white, .19f, .09f, .81f, .85f);
            hero.sprite = Resources.Load<Sprite>("Expedition/Art/hero");
            hero.preserveAspect = true;
            var cage = Image(cabin, "PhysicalHoistCage", Color.white, 0, 0, 1, 1);
            cage.sprite = _cabinArt;
            if (_cabinArt == null) cage.color = new Color32(45, 66, 68, 80);
            _liftCarrier.gameObject.SetActive(false);
        }

        private void BuildOverview()
        {
            var strip = Plate(_safeContent, "ShaftDepthOverview", .92f, .325f, .992f, .86f);
            Image(strip.transform, "DepthCable", new Color32(112, 128, 118, 255), .47f, .04f, .5f, .96f);
            for (var floor = 1; floor <= 12; floor++)
            {
                var y = .04f + (12 - floor) * .078f;
                var button = Button(strip.transform, "Depth_" + floor, "", false, .04f, y, .96f, y + .066f);
                var f = floor;
                button.onClick.AddListener(() => SelectFloor(f, true));
                var mark = button.GetComponent<Image>();
                mark.color = new Color32(27, 49, 51, 255);
                var label = Text(button.transform, "DepthNumber", floor.ToString(), 20, floor % 4 == 0 ? new Color32(218, 133, 85, 255) : Muted,
                    TextAnchor.MiddleCenter, 0, 0, 1, 1, floor % 4 == 0);
                _depthMarks[floor - 1] = mark;
                _depthLabels[floor - 1] = label;
            }
        }

        private void BuildSelection()
        {
            var panel = Plate(_safeContent, "SelectedFloor", .018f, .112f, .982f, .307f);
            var picture = Image(panel.transform, "PortraitWell", new Color32(11, 24, 27, 255), .018f, .055f, .37f, .94f);
            picture.gameObject.AddComponent<RectMask2D>();
            _selectionPortrait = Image(picture.transform, "MonsterPortrait", Color.white, -.1f, -.42f, 1.1f, 1.15f);
            _selectionPortrait.preserveAspect = true;
            _selectionGate = Image(picture.transform, "GatePortrait", Color.white, .03f, .06f, .97f, .94f);
            _selectionGate.sprite = _gateArt;
            _selectionGate.preserveAspect = true;
            _selectionTitle = Text(panel.transform, "FloorTitle", "Этаж 1", 35, Ivory, TextAnchor.MiddleLeft, .39f, .77f, .975f, .96f, true);
            _selectionName = Text(panel.transform, "MonsterName", "", 29, Ivory, TextAnchor.MiddleLeft, .39f, .60f, .975f, .79f, true);
            _selectionInfo = Text(panel.transform, "Context", "", 23, Muted, TextAnchor.UpperLeft, .39f, .34f, .97f, .61f);
            _selectionRewards = Text(panel.transform, "Rewards", "", 21, Ivory, TextAnchor.MiddleLeft, .39f, .07f, .65f, .33f);
            _action = Button(panel.transform, "DossierAction", "ПРОДОЛЖИТЬ ›", true, .66f, .07f, .975f, .315f);
            _actionLabel = _action.GetComponentInChildren<Text>();
            _action.onClick.AddListener(() => _openDossier?.Invoke(_selectedFloor));
            _summonButton = Button(panel.transform, "SummonAction", "ПРИЗВАТЬ", false, .39f, .07f, .65f, .315f);
            _summonLabel = _summonButton.GetComponentInChildren<Text>();
            _summonButton.onClick.AddListener(() => _summon?.Invoke(_selectedFloor));
            _summonButton.gameObject.SetActive(false);
        }

        public void SetFloor(FloorState state)
        {
            if (!_built || state.floor < 1 || state.floor > 12) return;
            var row = _landings[state.floor - 1];
            row.state = state;
            row.portrait.sprite = state.portrait;
            row.portrait.enabled = state.hasCatalog && state.unlocked && state.portrait != null;
            row.portrait.color = state.respawnSeconds > 0 ? new Color(.56f, .65f, .63f, .22f) : Color.white;
            row.shadow.enabled = row.portrait.enabled;
            row.barrier.SetActive(!state.unlocked);
            row.cost.gameObject.SetActive(state.hasCatalog && !state.unlocked);
            SetText(row.costLabel, state.barrierCost);
            SetText(row.status, !state.hasCatalog ? "Связь с шахтой…" : !state.unlocked ? "" : state.respawnSeconds > 0
                ? "Возвращение · " + FormatTime(state.respawnSeconds) : "");
            row.timerPlate.SetActive(!state.hasCatalog || state.unlocked && state.respawnSeconds > 0);
            row.bossBanner.SetActive(state.boss && state.unlocked);
            row.status.color = state.boss ? Amber : Muted;
            row.number.color = state.unlocked ? Ivory : Muted;
            RefreshLandingSelection(row);
            if (state.floor == _selectedFloor) RefreshSelection();
        }

        public void SetProgress(int deepestOpenFloor, bool hasCatalog)
        {
            if (_selectedInitialProgress || !hasCatalog) return;
            _selectedInitialProgress = true;
            SelectFloor(Mathf.Clamp(deepestOpenFloor, 1, 12), false);
            SnapRouteToFloor(_selectedFloor);
        }

        public void SetSummonBusy(bool busy) { _summonBusy = busy; RefreshSelection(); }

        public void SelectFloor(int floor, bool scrollToFloor = true)
        {
            _selectedFloor = Mathf.Clamp(floor, 1, 12);
            foreach (var row in _landings) if (row != null) RefreshLandingSelection(row);
            for (var i = 0; i < 12; i++)
            {
                if (_depthMarks[i] != null) _depthMarks[i].color = i + 1 == _selectedFloor ? Amber : new Color32(27, 49, 51, 255);
                if (_depthLabels[i] != null) _depthLabels[i].color = i + 1 == _selectedFloor ? Ink : (i + 1) % 4 == 0 ? new Color32(218, 133, 85, 255) : Muted;
            }
            if (scrollToFloor) TravelToFloor(_selectedFloor);
            else if (_liftCarrier != null) _liftCarrier.anchoredPosition = new Vector2(0, -(_selectedFloor - 1) * _landingHeight);
            RefreshSelection();
        }

        private void RefreshLandingSelection(Landing row)
        {
            var selected = row.state.floor == _selectedFloor;
            row.selectedRim.gameObject.SetActive(selected);
            row.number.color = selected ? Amber : row.state.unlocked ? Ivory : Muted;
            if (selected && _liftCarrier != null) _liftCarrier.gameObject.SetActive(row.state.hasCatalog);
        }

        /// <summary>Called only when the viewport changes size; countdown refreshes never rebuild geometry.</summary>
        public void ReflowRoute()
        {
            if (_content == null || _scroll == null || _landings[11] == null || _liftCarrier == null) return;
            var height = _scroll.viewport.rect.height;
            if (height <= 1 || Mathf.Abs(height - _geometryViewportHeight) < 1) return;
            CancelTapTravel();
            _geometryViewportHeight = height;
            var scrollFloor = _content.anchoredPosition.y / _landingHeight;
            var liftFloor = -_liftCarrier.anchoredPosition.y / _landingHeight;
            _landingHeight = Mathf.Max(280, height * .44f);
            _content.sizeDelta = new Vector2(0, 12 * _landingHeight);
            foreach (var row in _landings)
            {
                row.rect.sizeDelta = new Vector2(0, _landingHeight);
                row.rect.anchoredPosition = new Vector2(0, -(row.state.floor - 1) * _landingHeight);
            }
            _liftCarrier.sizeDelta = new Vector2(0, _landingHeight);
            _liftCarrier.anchoredPosition = new Vector2(0, -liftFloor * _landingHeight);
            _content.anchoredPosition = new Vector2(0, Mathf.Clamp(scrollFloor * _landingHeight, 0, ScrollRange));
        }

        private float ScrollRange => Mathf.Max(0, 12 * _landingHeight - _scroll.viewport.rect.height);
        private float FloorScrollOffset(int floor) => Mathf.Clamp((floor - .5f) * _landingHeight - _scroll.viewport.rect.height * .5f, 0, ScrollRange);

        private void SnapRouteToFloor(int floor)
        {
            if (_scroll == null || _liftCarrier == null) return;
            CancelTapTravel();
            _content.anchoredPosition = new Vector2(0, FloorScrollOffset(floor));
            _liftCarrier.anchoredPosition = new Vector2(0, -(floor - 1) * _landingHeight);
        }

        private void TravelToFloor(int floor)
        {
            if (_scroll == null || _liftCarrier == null) return;
            CancelTapTravel();
            if (!isActiveAndEnabled) { SnapRouteToFloor(floor); return; }
            _tapTravel = StartCoroutine(CoTravelToFloor(floor));
        }

        private IEnumerator CoTravelToFloor(int floor)
        {
            var startView = _content.anchoredPosition.y;
            var startLift = _liftCarrier.anchoredPosition.y;
            var targetView = FloorScrollOffset(floor);
            var targetLift = -(floor - 1) * _landingHeight;
            var duration = .42f + Mathf.Min(.58f, Mathf.Abs(targetLift - startLift) / _landingHeight * .09f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3 - 2 * t);
                _content.anchoredPosition = new Vector2(0, Mathf.Lerp(startView, targetView, eased));
                _liftCarrier.anchoredPosition = new Vector2(0, Mathf.Lerp(startLift, targetLift, eased));
                yield return null;
            }
            _tapTravel = null;
        }

        public void CancelTapTravel()
        {
            if (_tapTravel != null) { StopCoroutine(_tapTravel); _tapTravel = null; }
            if (_scroll != null) _scroll.StopMovement();
        }

        private void RefreshSelection()
        {
            if (_selectionTitle == null) return;
            var state = _landings[_selectedFloor - 1].state;
            SetText(_selectionTitle, "Этаж " + _selectedFloor + (state.boss ? " · Хранитель" : ""));
            SetText(_selectionName, !state.hasCatalog ? "Неизведанная глубина" : state.unlocked ? state.name : "Запечатанный проход");
            SetText(_selectionInfo, !state.hasCatalog ? "Устанавливаем связь с шахтой…" : !state.unlocked
                ? "Откройте проход, чтобы продолжить экспедицию. Условия — в досье барьера."
                : state.respawnSeconds > 0 ? "Этаж исследован. Противник вернётся через " + FormatTime(state.respawnSeconds)
                : state.boss ? "Хранитель перекрывает путь в глубину. Изучите его силы и награды перед боем."
                : "Противник на площадке. Осмотрите его характеристики и награды перед боем.");
            SetText(_selectionRewards, !state.hasCatalog ? "" : !state.unlocked ? state.barrierCost : "Награды:\n" + state.rewards);
            _selectionPortrait.sprite = state.portrait;
            _selectionPortrait.enabled = state.unlocked && state.portrait != null;
            _selectionGate.gameObject.SetActive(state.hasCatalog && !state.unlocked);
            _action.interactable = state.hasCatalog;
            SetText(_actionLabel, !state.hasCatalog ? "ОЖИДАНИЕ…" : !state.unlocked ? "ОТКРЫТЬ ›" : "ПРОДОЛЖИТЬ ›");
            var summon = state.hasCatalog && state.unlocked && state.respawnSeconds > 0 && !state.boss;
            _summonButton.gameObject.SetActive(summon);
            _selectionRewards.gameObject.SetActive(!summon);
            _summonButton.interactable = !_summonBusy;
            SetText(_summonLabel, _summonBusy ? "ПРИЗЫВ…" : "ПРИЗВАТЬ");
        }

        public static void ApplyDifficultyStyle(Transform tabsRoot, string selectedDifficulty)
        {
            if (tabsRoot == null) return;
            foreach (var id in new[] { "easy", "medium", "hard" })
            {
                var tab = tabsRoot.Find("Diff_" + id);
                if (tab == null) continue;
                var selected = string.Equals(id, selectedDifficulty, StringComparison.OrdinalIgnoreCase);
                var pulse = tab.GetComponent<UiNeonPulseOutline>();
                if (pulse != null) { pulse.SetHighlight(false); pulse.enabled = false; }
                var button = tab.GetComponent<Button>();
                if (button != null) ExpeditionUi.StyleButton(button, selected, 27);
                var label = tab.GetComponentInChildren<Text>(true);
                if (label != null) { label.font = ExpeditionTheme.LegacyFont; label.fontSize = 27; label.color = selected ? Ink : Ivory; label.fontStyle = FontStyle.Bold; }
            }
        }

        private static void StyleDifficultyTabs(Transform tabs)
        {
            Rect(tabs as RectTransform, .13f, .88f, .88f, .925f);
            var image = tabs.GetComponent<Image>();
            if (image != null) { image.sprite = null; image.color = Color.clear; image.raycastTarget = false; }
            var layout = tabs.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) { layout.spacing = 8; layout.padding = new RectOffset(); }
        }

        private static Image Plate(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            return ExpeditionUi.StylePanel(Image(parent, name, Color.white, x0, y0, x1, y1), ExpeditionTheme.Panel);
        }

        private static Image Image(Transform parent, string name, Color color, float x0, float y0, float x1, float y1)
        {
            var rect = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        private static Text Text(Transform parent, string name, string value, int size, Color color, TextAnchor align,
            float x0, float y0, float x1, float y1, bool bold = false)
        {
            var text = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<UnityEngine.UI.Text>();
            text.font = ExpeditionTheme.LegacyFont;
            text.text = value; text.fontSize = size; text.color = color; text.alignment = align;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Max(16, size - 5); text.resizeTextMaxSize = size;
            return text;
        }

        private static Button Button(Transform parent, string name, string label, bool primary, float x0, float y0, float x1, float y1)
        {
            var image = Image(parent, name, Color.white, x0, y0, x1, y1);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            Text(image.transform, "Label", label, 25, primary ? Ink : Ivory, TextAnchor.MiddleCenter, .035f, .06f, .965f, .94f, true);
            ExpeditionUi.StyleButton(button, primary, 25);
            return button;
        }

        private static void Rect(RectTransform rect, float x0, float y0, float x1, float y1) => ExpeditionUi.SetRect(rect, x0, y0, x1, y1);
        private static void SetText(Text text, string value) { if (text != null && text.text != value) text.text = value ?? ""; }
        private static string FormatTime(int seconds) => seconds >= 3600 ? (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00") : (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        private sealed class Landing
        {
            public FloorState state;
            public RectTransform rect;
            public Image portrait, cost, selectedRim, shadow;
            public GameObject barrier, bossBanner, timerPlate;
            public Text number, status, costLabel;
        }
    }

    /// <summary>Transfers ownership of the current scroll position to a finger immediately.</summary>
    public sealed class ExpeditionMineRouteScroll : ScrollRect
    {
        public ExpeditionMinePresentation owner;
        public override void OnInitializePotentialDrag(PointerEventData eventData)
        { owner?.CancelTapTravel(); base.OnInitializePotentialDrag(eventData); }
        public override void OnBeginDrag(PointerEventData eventData)
        { owner?.CancelTapTravel(); base.OnBeginDrag(eventData); }
        public override void OnScroll(PointerEventData eventData)
        { owner?.CancelTapTravel(); base.OnScroll(eventData); }
        protected override void OnRectTransformDimensionsChange()
        { base.OnRectTransformDimensionsChange(); owner?.ReflowRoute(); }
    }

    /// <summary>Continuous, static rails and hoist cables in a dedicated shaft, independent of room art.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionMineShaftGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            ExpeditionMineSurfaceMesh.Quad(vh, r, new Color32(4, 13, 17, 238), new Color32(12, 28, 32, 240));
            foreach (var x in new[] { .305f, .95f })
            {
                var rail = new Rect(r.xMin + x * r.width, r.yMin, r.width * .043f, r.height);
                ExpeditionMineSurfaceMesh.Quad(vh, rail, new Color32(33, 45, 47, 255), new Color32(60, 73, 73, 255));
                ExpeditionMineSurfaceMesh.Quad(vh, new Rect(rail.xMin + 2, rail.yMin, 2, rail.height),
                    new Color32(104, 122, 117, 255), new Color32(81, 102, 99, 255));
            }
            foreach (var x in new[] { .625f, .645f })
                ExpeditionMineSurfaceMesh.Quad(vh, new Rect(r.xMin + x * r.width, r.yMin, 2.3f, r.height),
                    new Color32(113, 106, 87, 255), new Color32(145, 130, 102, 255));
            var step = r.height / 24;
            for (var i = 0; i <= 24; i++)
            {
                var y = r.yMin + step * i;
                foreach (var x in new[] { .30f, .945f })
                    ExpeditionMineSurfaceMesh.Quad(vh, new Rect(r.xMin + x * r.width, y - 4, r.width * .06f, 9),
                        new Color32(49, 60, 58, 255), new Color32(96, 103, 90, 255));
            }
        }
    }

    /// <summary>Seeded cobwebs and faint damp mineral growth. The mesh is built only on size/style changes.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionMineCaveDetail : MaskableGraphic
    {
        public int seed;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            for (var i = 0; i < 6; i++)
            {
                var x = Mathf.Repeat(seed * .173f + i * .317f, 1);
                var y = Mathf.Repeat(seed * .231f + i * .419f, 1);
                var center = new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
                var c = i % 2 == 0 ? new Color(.19f, .32f, .18f, .16f) : new Color(.08f, .33f, .37f, .10f);
                ExpeditionMineSurfaceMesh.Stain(vh, center, new Vector2(r.width * .12f, r.height * .17f), c, seed + i);
            }
            if (seed % 3 != 1) return;
            var origin = new Vector2(r.xMax - r.width * .02f, r.yMax - r.height * .02f);
            var radius = Mathf.Min(r.width * .26f, r.height * .45f);
            var web = new Color(.68f, .73f, .66f, .19f);
            for (var spoke = 0; spoke <= 7; spoke++)
            {
                var a = Mathf.PI + Mathf.PI * .5f * spoke / 7;
                ExpeditionMineSurfaceMesh.Line(vh, origin, origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, .75f, web);
            }
            for (var ring = 1; ring <= 4; ring++)
            for (var segment = 0; segment < 7; segment++)
            {
                var a = Mathf.PI + Mathf.PI * .5f * segment / 7;
                var b = Mathf.PI + Mathf.PI * .5f * (segment + 1) / 7;
                var length = radius * ring / 4;
                ExpeditionMineSurfaceMesh.Line(vh, origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length,
                    origin + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * length, .65f, web);
            }
        }
    }

    internal static class ExpeditionMineSurfaceMesh
    {
        public static void Quad(VertexHelper vh, Rect r, Color bottom, Color top)
        {
            var i = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), bottom, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), top, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), top, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (width * .5f);
            var i = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Stain(VertexHelper vh, Vector2 p, Vector2 radius, Color color, int seed)
        {
            var first = vh.currentVertCount;
            vh.AddVert(p, color, Vector2.zero);
            color.a = 0;
            const int sides = 13;
            for (var i = 0; i <= sides; i++)
            {
                var a = i * Mathf.PI * 2 / sides;
                var variation = .84f + .16f * Mathf.Sin(a * 3 + seed);
                vh.AddVert(p + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y) * variation, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(first, first + i, first + i + 1);
            }
        }
    }

}
