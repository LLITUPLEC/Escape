using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>A reusable card over the real step buttons; binding never creates extra rewards or actions.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionAchievementCard : MonoBehaviour
    {
        private AchievementChainRowView _row;
        private Image _art;
        private TMP_Text _title, _description, _step, _progress, _status, _reward;
        private Image _progressFill;
        private Button _currentButton;
        private Button _claimButton;
        private Image _claimSurface;
        private LayoutElement _height;
        private Image[] _frames;
        private Image[] _connectors;
        private TMP_Text[] _targets, _states;
        private RectTransform[] _stepRects;
        private Image[] _stepWashes;
        private GameObject[] _stepChecks;
        private int _currentStep;
        private bool _built;

        public int CurrentStep => _currentStep;
        public TMP_Text ProgressLabel => _progress;
        public TMP_Text DescriptionLabel => _description;
        public TMP_Text[] TargetLabels => _targets;
        public TMP_Text[] StateLabels => _states;
        public Button ClaimButton => _claimButton;
        public GameObject[] ClaimedChecks => _stepChecks;
        public Image[] StepConnectors => _connectors;
        private static readonly Color ClaimedGreen = new Color32(137, 223, 161, 255);

        public void SetHeight(float height)
        {
            if (_height == null) _height = ExpeditionUi.Ensure<LayoutElement>(gameObject);
            _height.minHeight = _height.preferredHeight = height;
            _height.flexibleHeight = 0;
        }

        public void Bind(AchievementChainRowView row, AchievementChainDefinition chain, Sprite icon)
        {
            if (row == null || chain?.Thresholds == null) return;
            _row = row;
            if (!_built) Build(row);
            var count = chain.Thresholds.Length;
            if (count == 0) return;
            var total = AchievementProgressStorage.GetStat(chain.StatKey);
            _currentStep = 0;
            while (_currentStep < count - 1 && AchievementProgressStorage.IsStepClaimed(chain.ChainId, _currentStep)) _currentStep++;
            var allClaimed = AchievementProgressStorage.IsStepClaimed(chain.ChainId, _currentStep);
            var claimable = AchievementRewardClaim.CanClaimStep(chain, _currentStep);
            var target = AchievementUiRules.StepTarget(chain, _currentStep);
            var progress = AchievementUiRules.StepProgress(chain, _currentStep, total);
            _title.text = chain.TitleRu;
            _art.sprite = icon != null ? icon : ExpeditionTheme.Icon("trophy");
            _art.overrideSprite = null;
            _art.type = Image.Type.Simple;
            _art.preserveAspect = true;
            _art.rectTransform.localScale = Vector3.one;
            _art.color = Color.white;
            _art.enabled = _art.sprite != null;
            _description.text = At(chain.Descriptions, _currentStep);
            _step.text = $"Ступень {_currentStep + 1} из {count}";
            _progress.text = progress + " / " + target;
            _status.text = allClaimed ? "Все получено" : claimable ? "Получить" : "В процессе";
            _status.alignment = claimable ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            _status.color = allClaimed ? ClaimedGreen : claimable ? new Color32(35, 28, 18, 255) : ExpeditionTheme.Muted;
            _claimButton.interactable = claimable;
            _claimButton.gameObject.SetActive(claimable);
            _reward.text = At(chain.RewardTexts, _currentStep);
            var fill = _progressFill.rectTransform;
            ExpeditionUi.SetRect(fill, 0, 0, target > 0 ? progress / (float)target : 0, 1);
            _progressFill.enabled = progress > 0;
            var available = Mathf.Min(count, _stepRects.Length);
            for (var i = 0; i < _stepRects.Length; i++)
            {
                if (i >= available) { _stepRects[i].gameObject.SetActive(false); continue; }
                _stepRects[i].gameObject.SetActive(true);
                var x0 = .028f + i * .944f / available;
                var x1 = .028f + (i + 1) * .944f / available;
                ExpeditionUi.SetRect(_stepRects[i], x0 + .019f, .035f, x1 - .019f, .285f);
                var claimed = AchievementProgressStorage.IsStepClaimed(chain.ChainId, i);
                var reached = total >= AchievementUiRules.CumulativeRequired(chain, i);
                var eligible = !claimed && AchievementRewardClaim.CanClaimStep(chain, i);
                _frames[i].sprite = reached && !claimed ? ExpeditionArt.Frame(true) : ExpeditionTheme.Plate;
                _frames[i].overrideSprite = null;
                _frames[i].color = Color.white;
                _frames[i].type = Image.Type.Sliced;
                _frames[i].pixelsPerUnitMultiplier = 4;
                _stepWashes[i].enabled = claimed;
                _stepChecks[i].SetActive(claimed);
                _targets[i].text = AchievementUiRules.StepTarget(chain, i).ToString();
                _targets[i].color = reached && !claimed ? new Color32(35, 28, 18, 255) : ExpeditionTheme.Ivory;
                _states[i].text = claimed ? "Получено" : eligible ? "Получить" : reached ? "Выполнено" : i == _currentStep ? "Текущая" : "Далее";
                _states[i].color = claimed ? ClaimedGreen : _targets[i].color;
                ExpeditionUi.SetRect(_states[i].rectTransform, claimed ? .235f : .035f, .105f, .965f, .43f);
                var group = _stepRects[i].GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1;
            }
            for (var i = 0; i < _connectors.Length; i++)
            {
                var show = i < available - 1;
                _connectors[i].gameObject.SetActive(show);
                if (!show) continue;
                var center = .028f + (i + 1) * .944f / available;
                ExpeditionUi.SetRect(_connectors[i].rectTransform, center - .014f, .113f, center + .014f, .207f);
                var claimed = AchievementProgressStorage.IsStepClaimed(chain.ChainId, i);
                var reached = total >= AchievementUiRules.CumulativeRequired(chain, i);
                var color = claimed ? ClaimedGreen : reached ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
                _connectors[i].color = color;
                for (var child = 0; child < _connectors[i].transform.childCount; child++)
                    _connectors[i].transform.GetChild(child).GetComponent<Image>().color = color;
            }
        }

        private void Build(AchievementChainRowView row)
        {
            var root = transform as RectTransform;
            var oldLayout = GetComponent<LayoutGroup>(); if (oldLayout != null) oldLayout.enabled = false;
            var surface = ExpeditionUi.Ensure<Image>(gameObject);
            ExpeditionUi.StylePanel(surface, ExpeditionTheme.Panel);
            surface.raycastTarget = false;
            _height = ExpeditionUi.Ensure<LayoutElement>(gameObject);
            _art = MakeImage("ExpeditionChainIllustration", root, .035f, .59f, .18f, .94f);
            _art.preserveAspect = true;
            _title = Text("ExpeditionChainTitle", root, .205f, .835f, .965f, .965f, 42, TextAlignmentOptions.MidlineLeft);
            _description = Text("ExpeditionChainDescription", root, .205f, .60f, .965f, .83f, 32, TextAlignmentOptions.TopLeft);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.overflowMode = TextOverflowModes.Truncate;
            _step = Text("ExpeditionChainStep", root, .205f, .49f, .68f, .59f, 28, TextAlignmentOptions.MidlineLeft);
            _progress = Text("ExpeditionChainProgress", root, .69f, .48f, .965f, .60f, 40, TextAlignmentOptions.MidlineRight);
            var track = MakeImage("ExpeditionChainTrack", root, .205f, .445f, .965f, .462f);
            track.color = new Color32(7, 21, 25, 255);
            _progressFill = MakeImage("Fill", track.rectTransform, 0, 0, 0, 1);
            _progressFill.color = ExpeditionTheme.Amber;
            _claimSurface = MakeImage("ExpeditionClaimReward", root, .035f, .329f, .378f, .435f);
            ExpeditionUi.StylePanel(_claimSurface, Color.white);
            _claimSurface.sprite = ExpeditionArt.Frame(true);
            _claimSurface.raycastTarget = true;
            _claimButton = ExpeditionUi.Ensure<Button>(_claimSurface.gameObject);
            _claimButton.targetGraphic = _claimSurface;
            _claimButton.transition = Selectable.Transition.None;
            _claimButton.onClick.AddListener(OpenCurrentStep);
            _status = Text("ExpeditionChainStatus", root, .055f, .345f, .36f, .423f, 26, TextAlignmentOptions.MidlineLeft);
            _reward = Text("ExpeditionChainReward", root, .445f, .325f, .965f, .435f, 26, TextAlignmentOptions.MidlineRight);
            _reward.textWrappingMode = TextWrappingModes.Normal;
            _reward.color = ExpeditionTheme.Amber;
            var hit = MakeImage("ExpeditionCurrentStepAction", root, .205f, .60f, .965f, .835f);
            hit.color = Color.clear; hit.raycastTarget = true;
            _currentButton = ExpeditionUi.Ensure<Button>(hit.gameObject);
            _currentButton.targetGraphic = hit;
            _currentButton.transition = Selectable.Transition.None;
            _currentButton.onClick.AddListener(OpenCurrentStep);
            var slots = row.Slots;
            var count = slots != null ? slots.Length : 0;
            _frames = new Image[count]; _targets = new TMP_Text[count];
            _states = new TMP_Text[count]; _stepRects = new RectTransform[count];
            _stepWashes = new Image[count]; _stepChecks = new GameObject[count];
            _connectors = new Image[Mathf.Max(0, count - 1)];
            for (var i = 0; i < count; i++)
            {
                var slot = slots[i]; if (slot == null) continue;
                slot.SetExpeditionPresentation();
                _stepRects[i] = slot.transform as RectTransform;
                // Prefab arrows and old decorations may extend outside their slots.
                _frames[i] = FindImage(slot.transform, "Frame");
                if (_frames[i] == null) _frames[i] = MakeImage("Frame", _stepRects[i], 0, 0, 1, 1);
                ExpeditionUi.SetRect(_frames[i].rectTransform, 0, 0, 1, 1);
                _frames[i].raycastTarget = true;
                var button = ExpeditionUi.Ensure<Button>(slot.gameObject); button.targetGraphic = _frames[i];
                _stepWashes[i] = MakeImage("ExpeditionClaimedWash", _stepRects[i], .055f, .085f, .945f, .91f);
                _stepWashes[i].color = new Color32(17, 62, 43, 245);
                _stepWashes[i].transform.SetSiblingIndex(_frames[i].transform.GetSiblingIndex() + 1);
                var oldIcon = FindImage(slot.transform, "Icon");
                if (oldIcon != null) { oldIcon.enabled = false; oldIcon.gameObject.SetActive(false); }
                _targets[i] = Text("ProgressText", _stepRects[i], .035f, .435f, .965f, .94f, 38, TextAlignmentOptions.Center);
                _states[i] = Text("ExpeditionStepState", _stepRects[i], .035f, .105f, .965f, .43f, 24, TextAlignmentOptions.Center);
                _states[i].overflowMode = TextOverflowModes.Overflow;
                _stepChecks[i] = MakeCheck(_stepRects[i]);
            }
            for (var i = 0; i < _connectors.Length; i++) _connectors[i] = MakeConnector(root, i);
            foreach (var label in row.GetComponentsInChildren<TMP_Text>(true))
                if (label.name.StartsWith("Arrow", System.StringComparison.Ordinal)) label.gameObject.SetActive(false);
            _built = true;
        }

        private void OpenCurrentStep() { _row?.OpenStep(_currentStep); }
        private static GameObject MakeCheck(RectTransform slot)
        {
            // A real green tick without depending on a missing atlas sprite or a font glyph.
            var rt = Child("ExpeditionClaimedCheck", slot);
            ExpeditionUi.SetRect(rt, .065f, .14f, .235f, .405f);
            var shortArm = MakeImage("Short", rt, .04f, .18f, .46f, .39f);
            shortArm.color = ClaimedGreen;
            shortArm.rectTransform.localRotation = Quaternion.Euler(0, 0, -42);
            var longArm = MakeImage("Long", rt, .30f, .39f, 1f, .60f);
            longArm.color = ClaimedGreen;
            longArm.rectTransform.localRotation = Quaternion.Euler(0, 0, 43);
            return rt.gameObject;
        }
        private static Image MakeConnector(RectTransform root, int index)
        {
            var arrow = MakeImage("ExpeditionStepConnector_" + index, root, 0, 0, 1, 1);
            // The root has no visible fill; these three tiny bars make a font-independent right arrow.
            arrow.color = Color.clear;
            var shaft = MakeImage("Shaft", arrow.transform, .02f, .455f, .82f, .545f);
            var upper = MakeImage("Upper", arrow.transform, .60f, .55f, .99f, .64f);
            var lower = MakeImage("Lower", arrow.transform, .60f, .36f, .99f, .45f);
            upper.rectTransform.localRotation = Quaternion.Euler(0, 0, -43);
            lower.rectTransform.localRotation = Quaternion.Euler(0, 0, 43);
            // Keep an invisible root so its tint can report state without drawing a block in the gap.
            arrow.enabled = false;
            return arrow;
        }
        private static string At(string[] values, int index) => values != null && index >= 0 && index < values.Length ? values[index] : string.Empty;
        private static Image FindImage(Transform root, string name) { var child = root.Find(name); return child != null ? child.GetComponent<Image>() : null; }
        private static RectTransform Child(string name, Transform parent)
        {
            var existing = parent.Find(name) as RectTransform;
            return existing != null ? existing : ExpeditionUi.Rect(parent, name, 0, 0, 1, 1);
        }
        private static Image MakeImage(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            var rt = Child(name, parent); ExpeditionUi.SetRect(rt, x0, y0, x1, y1);
            var image = ExpeditionUi.Ensure<Image>(rt.gameObject); image.sprite = null; image.raycastTarget = false;
            return image;
        }
        private static TMP_Text Text(string name, Transform parent, float x0, float y0, float x1, float y1, float size, TextAlignmentOptions alignment)
        {
            var rt = Child(name, parent); ExpeditionUi.SetRect(rt, x0, y0, x1, y1);
            var text = ExpeditionUi.Ensure<TextMeshProUGUI>(rt.gameObject);
            text.font = ExpeditionTheme.Font; text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .86f;
            text.enableAutoSizing = true; text.fontStyle = FontStyles.Normal;
            text.color = ExpeditionTheme.Ivory; text.alignment = alignment; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }
    }
}
