using System;
using System.Collections.Generic;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Achievements
{
    /// <summary>The selected medal, its reward and the other stages of the same achievement.</summary>
    [DisallowMultipleComponent]
    public sealed class AchievementStepDetailModal : MonoBehaviour
    {
        private sealed class ChainStepRow
        {
            public Button Button;
            public Image Selection;
            public TMP_Text Title;
            public TMP_Text Status;
        }

        private readonly List<ChainStepRow> _rows = new List<ChainStepRow>();
        private TMP_FontAsset _font;
        private Image _medalIcon;
        private TMP_Text _title, _stage, _condition, _status, _progress;
        private AchievementRewardDisplay _reward;
        private RectTransform _progressFill, _content, _chainRoot;
        private ScrollRect _scroll;
        private Button _close, _claim;
        private AchievementChainDefinition _chain;
        private int _stepIndex = -1;
        private string _accountScope;
        private Action<string, int> _onClaim;
        private GameObject _previousSelection;

        public string ChainId => _chain?.ChainId;
        public int StepIndex => _stepIndex;
        public bool IsVisible => gameObject.activeSelf;

        public static AchievementStepDetailModal Ensure(Transform parent, TMP_FontAsset font)
        {
            if (parent == null) return null;
            var existing = parent.GetComponentInChildren<AchievementStepDetailModal>(true);
            if (existing != null) return existing;
            var root = ExpeditionUi.Rect(parent, "AchievementStepDetailModal", 0, 0, 1, 1);
            root.gameObject.SetActive(false);
            var modal = root.gameObject.AddComponent<AchievementStepDetailModal>();
            modal._font = AchievementUiFontLoader.Resolve(font);
            modal.Build();
            return modal;
        }

        public void Show(AchievementChainDefinition chain, int stepIndex, Sprite icon, Action<string, int> onClaim)
        {
            if (!ValidStep(chain, stepIndex)) return;
            if (!IsVisible) _previousSelection = EventSystem.current?.currentSelectedGameObject;
            _accountScope = LocalAccountCacheScope.Current;
            _chain = chain;
            _stepIndex = stepIndex;
            _onClaim = onClaim;
            BindIcon(icon);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            Render();
            ResetScroll();
            if (Application.isPlaying) _close.Select();
        }

        public void Refresh(AchievementChainDefinition chain, Sprite icon)
        {
            if (!IsVisible) return;
            if (!ValidStep(chain, _stepIndex) || !string.Equals(ChainId, chain.ChainId, StringComparison.Ordinal)
                || !IsCurrentAccount())
            {
                Hide();
                return;
            }

            _chain = chain;
            BindIcon(icon);
            Render();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            var events = EventSystem.current;
            var selected = events?.currentSelectedGameObject;
            var restoreSelection = selected != null && selected.transform.IsChildOf(transform);
            gameObject.SetActive(false);
            if (restoreSelection)
                events.SetSelectedGameObject(_previousSelection != null && _previousSelection.activeInHierarchy
                    ? _previousSelection : null);
            _previousSelection = null;
            _onClaim = null;
            _accountScope = null;
            _chain = null;
            _stepIndex = -1;
        }

        private void Build()
        {
            var dim = ExpeditionUi.Fill(transform, "Dimmer", 0, 0, 1, 1, new Color(0, 0, 0, .75f));
            dim.raycastTarget = true;
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.targetGraphic = dim;
            dimButton.transition = Selectable.Transition.None;
            dimButton.navigation = new Navigation { mode = Navigation.Mode.None };
            dimButton.onClick.AddListener(Hide);

            var panel = ExpeditionUi.Panel(transform, "Panel", .055f, .212f, .945f, .84f);
            panel.raycastTarget = true;
            var body = panel.rectTransform;
            if (Application.isPlaying) body.gameObject.AddComponent<ExpeditionPanelArrival>();
            var cover = ExpeditionShelterSurface.Illustration(body, "MedalCover", "medal-album", .024f, .567f, .976f, .985f);
            ExpeditionUi.Fill(cover, "CoverShade", 0, 0, 1, 1, new Color32(19, 12, 8, 136));
            ExpeditionUi.Fill(cover, "TitleBacking", 0, 0, 1, .26f, new Color32(24, 14, 10, 237));
            var mount = ExpeditionUi.Rect(body, "MedalIconMount", .30f, .685f, .70f, .974f);
            var iconFrame = ExpeditionUi.Panel(mount, "MedalIconFrame", 0, 0, 1, 1);
            var fit = iconFrame.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1;
            iconFrame.color = new Color32(216, 172, 120, 255);
            _medalIcon = ExpeditionUi.Fill(iconFrame.transform, "MedalIcon", .048f, .048f, .952f, .952f, Color.white);
            _medalIcon.preserveAspect = true;
            _title = Label(body, "MedalTitle", "", 43, .066f, .626f, .934f, .681f,
                ExpeditionTheme.Ivory, FontStyles.Bold, TextAlignmentOptions.Center);
            FitSingleLine(_title, 31, 43);
            _stage = Label(body, "MedalStage", "", 26, .12f, .572f, .88f, .624f,
                ExpeditionTheme.Amber, alignment: TextAlignmentOptions.Center);
            _close = ExpeditionUi.Button(body, "Close", "×", .856f, .912f, .972f, .979f, Hide, false, 39);

            var scrollArea = ExpeditionUi.Panel(body, "MedalScroll", .045f, .115f, .955f, .559f);
            scrollArea.color = new Color32(186, 158, 122, 255);
            scrollArea.raycastTarget = true;
            var viewport = ExpeditionUi.Rect(scrollArea.transform, "Viewport", .025f, .024f, .975f, .976f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _content = ExpeditionUi.Rect(viewport, "Content", 0, 1, 1, 1);
            _content.pivot = new Vector2(.5f, 1f);
            BuildStack(_content, new RectOffset(18, 18, 18, 18), 14);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 42;

            _condition = BodyLabel("MedalCondition", "", 34, ExpeditionAchievementsLayout.ConditionColor, FontStyles.Italic);
            _status = BodyLabel("MedalStatus", "", 28, ExpeditionTheme.Muted);
            _progress = BodyLabel("MedalProgress", "", 29, ExpeditionTheme.Ivory, FontStyles.Bold);
            var track = ExpeditionUi.Fill(_content, "MedalProgressTrack", 0, 0, 1, 1, new Color32(7, 22, 27, 255));
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 10f;
            _progressFill = ExpeditionUi.Fill(track.transform, "Fill", 0, 0, 0, 1, ExpeditionTheme.Amber).rectTransform;
            _reward = new AchievementRewardDisplay(ExpeditionUi.Rect(_content, "MedalRewards", 0, 0, 1, 1), 31, _font);
            BodyLabel("ChainHeading", "Ступени медали", 33, ExpeditionTheme.Ivory, FontStyles.Bold);
            _chainRoot = ExpeditionUi.Rect(_content, "AchievementChain", 0, 0, 1, 1);
            var steps = _chainRoot.gameObject.AddComponent<AchievementAlbumGrid>();
            steps.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            steps.constraintCount = 4;
            steps.spacing = new Vector2(9, 9);
            steps.CellHeight = 128f;

            _claim = ExpeditionUi.Button(body, "ClaimReward", "Получить награду", .065f, .028f, .935f, .097f, Claim, true, 32);
            var claimLabel = _claim.GetComponentInChildren<TMP_Text>();
            if (_font != null) claimLabel.font = _font;
            claimLabel.fontStyle = FontStyles.Bold;
        }

        private void Render()
        {
            var selectedControl = EventSystem.current?.currentSelectedGameObject;
            var selectionWasInModal = selectedControl != null && selectedControl.transform.IsChildOf(transform);
            var total = AchievementProgressStorage.GetStat(_chain.StatKey);
            var count = _chain.Thresholds.Length;
            var target = AchievementUiRules.StepTarget(_chain, _stepIndex);
            var claimed = AchievementProgressStorage.IsStepClaimed(ChainId, _stepIndex);
            var progress = claimed ? target : AchievementUiRules.StepProgress(_chain, _stepIndex, total);
            var canClaim = AchievementRewardClaim.CanClaimStep(_chain, _stepIndex);
            var reached = total >= AchievementUiRules.CumulativeRequired(_chain, _stepIndex);
            _title.text = _chain.TitleRu;
            _stage.text = $"Ступень {_stepIndex + 1} из {count}";
            _condition.text = TextAt(_chain.Descriptions, _stepIndex);
            _status.text = claimed ? "Получено" : canClaim ? "Награда доступна" :
                reached ? "Сначала получите награды предыдущих ступеней" :
                AchievementUiRules.IsSlotLocked(_chain, _stepIndex, total) ? "Предстоящая ступень" : "В процессе";
            _status.color = claimed ? ExpeditionAchievementsLayout.ConditionColor :
                canClaim ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            _progress.text = $"Прогресс · {progress} / {target}";
            ExpeditionUi.SetRect(_progressFill, 0, 0, claimed ? 1 : AchievementUiRules.SliderRatio(_chain, _stepIndex, total), 1);
            _reward.Bind(_chain, _stepIndex);
            var showClaim = canClaim && _onClaim != null;
            _claim.gameObject.SetActive(showClaim);
            ExpeditionUi.SetRect(_scroll.transform as RectTransform, .045f, showClaim ? .115f : .035f, .955f, .559f);

            while (_rows.Count < count) BuildStepRow(_rows.Count);
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Button.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var isClaimed = AchievementProgressStorage.IsStepClaimed(ChainId, i);
                var isClaimable = AchievementRewardClaim.CanClaimStep(_chain, i);
                var isReached = total >= AchievementUiRules.CumulativeRequired(_chain, i);
                var isLocked = AchievementUiRules.IsSlotLocked(_chain, i, total);
                row.Selection.enabled = i == _stepIndex;
                row.Button.image.color = i == _stepIndex ? new Color32(225, 177, 116, 255) : new Color32(181, 167, 143, 255);
                row.Title.color = i == _stepIndex ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                row.Status.text = isClaimed ? "Получено" : isClaimable ? "Награда готова" :
                    isReached ? "Выполнено" : isLocked ? "Впереди" : "В процессе";
                row.Status.color = isClaimed ? ExpeditionAchievementsLayout.ConditionColor :
                    isClaimable ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                var previous = i == 0 ? _close : _rows[i - 1].Button;
                var next = i + 1 < count ? _rows[i + 1].Button : showClaim ? _claim : _close;
                SetNavigation(row.Button, previous, next);
            }

            var last = _rows[count - 1].Button;
            SetNavigation(_close, showClaim ? _claim : last, _rows[0].Button);
            SetNavigation(_claim, last, _close);
            LayoutRebuilder.MarkLayoutForRebuild(_content);
            if (Application.isPlaying && selectionWasInModal && !selectedControl.activeInHierarchy)
                _close.Select();
        }

        private void BuildStepRow(int index)
        {
            var surface = ExpeditionUi.Panel(_chainRoot, "ChainStep" + (index + 1), 0, 0, 1, 1);
            surface.raycastTarget = true;
            var button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            var colors = button.colors;
            colors.highlightedColor = colors.selectedColor = new Color(1f, .93f, .80f);
            colors.pressedColor = new Color(.78f, .71f, .61f);
            button.colors = colors;
            button.onClick.AddListener(() => SelectStep(index));
            Project.Audio.ExpeditionAudio.BindButton(button);
            var emblem = ExpeditionUi.Fill(surface.transform, "StageSeal", .29f, .43f, .71f, .91f, new Color32(202, 157, 100, 255));
            emblem.sprite = ExpeditionTheme.Icon("trophy");
            emblem.preserveAspect = true;
            emblem.enabled = emblem.sprite != null;
            var row = new ChainStepRow
            {
                Button = button,
                Selection = ExpeditionUi.Fill(surface.transform, "SelectedStep", .12f, .095f, .88f, .118f, ExpeditionTheme.Amber),
                Title = Label(surface.transform, "ChainStepTitle", (index + 1).ToString(), 28, .06f, .59f, .28f, .91f,
                    ExpeditionTheme.Ivory, FontStyles.Bold, TextAlignmentOptions.Center),
                Status = Label(surface.transform, "ChainStepStatus", "", 22, .05f, .135f, .95f, .46f,
                    ExpeditionTheme.Muted, FontStyles.Normal, TextAlignmentOptions.Center),
            };
            FitSingleLine(row.Status, 16, 22);
            _rows.Add(row);
        }

        private void SelectStep(int index)
        {
            if (!IsCurrentAccount()) { Hide(); return; }
            if (index == _stepIndex || !ValidStep(_chain, index)) return;
            _stepIndex = index;
            Render();
            ResetScroll();
            if (Application.isPlaying) _close.Select();
        }

        private void Claim()
        {
            if (!IsCurrentAccount()) { Hide(); return; }
            if (_onClaim == null || !AchievementRewardClaim.CanClaimStep(_chain, _stepIndex)) return;
            var action = _onClaim;
            var chainId = ChainId;
            var step = _stepIndex;
            Hide();
            action(chainId, step);
        }

        private bool IsCurrentAccount() => string.Equals(_accountScope, LocalAccountCacheScope.Current, StringComparison.Ordinal);

        private void BindIcon(Sprite icon)
        {
            _medalIcon.sprite = icon != null ? icon : ExpeditionTheme.Icon("trophy");
            _medalIcon.enabled = _medalIcon.sprite != null;
        }

        private void ResetScroll()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
        }

        private static void BuildStack(RectTransform host, RectOffset padding, float spacing)
        {
            var layout = host.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        private TMP_Text BodyLabel(string name, string value, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var label = Label(_content, name, value, size, 0, 0, 1, 1, color, style, TextAlignmentOptions.TopLeft);
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private TMP_Text Label(Transform parent, string name, string value, float size, float x0, float y0, float x1, float y1,
            Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = ExpeditionUi.Text(parent, name, value, size, x0, y0, x1, y1, alignment);
            if (_font != null) label.font = _font;
            label.richText = false;
            label.margin = Vector4.zero;
            label.color = color;
            label.fontStyle = style;
            return label;
        }

        private static void FitSingleLine(TMP_Text label, float min, float max)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = min;
            label.fontSizeMax = max;
        }

        private static void SetNavigation(Button button, Selectable up, Selectable down)
        {
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up, selectOnDown = down, selectOnLeft = up, selectOnRight = down,
            };
        }

        private static bool ValidStep(AchievementChainDefinition chain, int stepIndex) =>
            chain?.Thresholds != null && stepIndex >= 0 && stepIndex < chain.Thresholds.Length;

        private static string TextAt(string[] values, int index) =>
            values != null && index < values.Length ? values[index] ?? string.Empty : string.Empty;

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Hide();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                Hide();
#endif
        }
    }
}
