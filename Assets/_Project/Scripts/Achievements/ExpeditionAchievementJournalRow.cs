using System;
using System.Collections;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    public sealed class ExpeditionAchievementJournalRow : MonoBehaviour
    {
        private static readonly Color ProgressTrack = new Color32(60, 46, 33, 255);
        private Image _icon, _mount, _accent, _fill, _claimFrame, _claimTag;
        private TMP_Text _title, _condition, _progress, _status;
        private AchievementRewardDisplay _reward;
        private Coroutine _mountMotion;
        public string ChainId { get; private set; }
        public int CurrentStep { get; private set; }
        public int ClaimedSteps { get; private set; }

        public static ExpeditionAchievementJournalRow Create(RectTransform parent, Action<string, int> open)
        {
            var rect = ExpeditionUi.Rect(parent, "JournalRow", 0, 0, 1, 1);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.gameObject.AddComponent<Image>();
            ExpeditionUi.StyleButton(button);
            button.image.color = new Color32(194, 156, 112, 255);
            var row = rect.gameObject.AddComponent<ExpeditionAchievementJournalRow>();
            row.Build(rect);
            button.onClick.AddListener(() => open(row.ChainId, row.CurrentStep));
            return row;
        }

        private void Build(RectTransform root)
        {
            _claimFrame = ReadyPlate(root, "ClaimReadyFrame", 0, 0, 1, 1);
            ExpeditionUi.Fill(root, "LeatherPocket", .022f, .031f, .978f, .969f, new Color32(41, 29, 23, 255));
            _mount = ExpeditionUi.Panel(root, "MedalMount", .045f, .35f, .307f, .93f);
            _accent = ExpeditionUi.Fill(root, "MedalAccent", .333f, .705f, .951f, .716f, ExpeditionTheme.Amber);
            _icon = ExpeditionUi.Fill(_mount.transform, "MedalIcon", .045f, .045f, .955f, .955f, Color.white);
            _icon.preserveAspect = true;

            _title = Label(root, "MedalTitle", 31, 21, .33f, .72f, .964f, .956f, wrap: true);
            _title.fontStyle = FontStyles.Bold;
            _condition = Label(root, "MedalCondition", 24, 19, .33f, .368f, .964f, .70f, wrap: true);
            _condition.fontStyle = FontStyles.Italic;
            _condition.color = ExpeditionAchievementsLayout.ConditionColor;
            _reward = new AchievementRewardDisplay(ExpeditionUi.Rect(root, "MedalRewards", .33f, .042f, .964f, .365f), 22);

            _progress = Label(root, "MedalProgress", 22, 15, .045f, .229f, .307f, .344f, TextAlignmentOptions.Center);
            _progress.fontStyle = FontStyles.Bold;
            var track = ExpeditionUi.Fill(root, "MedalProgressTrack", .051f, .195f, .301f, .222f, ProgressTrack);
            _fill = ExpeditionUi.Fill(track.transform, "MedalProgressFill", 0, 0, 0, 1, ExpeditionTheme.Amber);
            _claimTag = ReadyPlate(root, "ClaimReadyTag", .04f, .036f, .312f, .184f);
            _status = Label(root, "MedalStatus", 19, 15, .04f, .037f, .312f, .191f, TextAlignmentOptions.Center, wrap: true);
        }

        public void Bind(AchievementChainDefinition chain, Sprite icon)
        {
            var sameMedal = ChainId == chain.ChainId;
            if (!sameMedal) ResetMount();
            var previouslyClaimed = ClaimedSteps;
            ChainId = chain.ChainId;
            gameObject.name = "JournalRow-" + ChainId;
            var count = chain.Thresholds.Length;
            var step = 0;
            while (step < count && AchievementProgressStorage.IsStepClaimed(ChainId, step)) step++;
            ClaimedSteps = step;
            var completed = step == count;
            CurrentStep = completed ? count - 1 : step;

            var target = AchievementUiRules.StepTarget(chain, CurrentStep);
            var progress = completed ? target : AchievementUiRules.StepProgress(chain, CurrentStep, AchievementProgressStorage.GetStat(chain.StatKey));
            var claimable = !completed && AchievementRewardClaim.CanClaimStep(chain, CurrentStep);
            _title.text = chain.TitleRu;
            _condition.text = At(chain.Descriptions, CurrentStep);
            _reward.Bind(chain, CurrentStep);
            _progress.text = $"{progress} / {target}";
            if (_claimFrame.enabled != claimable)
            {
                _claimFrame.enabled = _claimTag.enabled = claimable;
                _claimFrame.canvasRenderer.SetAlpha(1f);
            }
            _status.text = completed ? "Получено" : claimable ? "ЗАБРАТЬ" : $"Ступень {CurrentStep + 1} из {count}";
            _status.color = completed ? ExpeditionAchievementsLayout.ConditionColor : claimable ? new Color32(38, 24, 13, 255) : ExpeditionTheme.Muted;
            _status.fontStyle = claimable ? FontStyles.Bold : FontStyles.Normal;
            _fill.color = completed ? ExpeditionAchievementsLayout.ConditionColor : ExpeditionTheme.Amber;
            _fill.rectTransform.anchorMax = new Vector2(target > 0 ? (float)progress / target : completed ? 1 : 0, 1);
            _icon.sprite = icon != null ? icon : ExpeditionTheme.Icon("trophy");
            _icon.enabled = _icon.sprite != null;
            _mount.sprite = ExpeditionArt.Frame(completed || claimable) ?? ExpeditionTheme.Plate;
            var tier = chain.TierAccentColors != null && CurrentStep < chain.TierAccentColors.Length
                ? chain.TierAccentColors[CurrentStep] : ExpeditionTheme.Amber;
            _accent.color = claimable ? ExpeditionTheme.Amber : Color.Lerp(tier, new Color32(164, 122, 74, 255), .55f);
            if (sameMedal && ClaimedSteps > previouslyClaimed && isActiveAndEnabled && Application.isPlaying)
            {
                ResetMount();
                _mountMotion = StartCoroutine(MountMedal());
            }
        }

        internal void SetClaimFrameIntensity(float opacity)
        {
            if (_claimFrame.enabled && _claimFrame.gameObject.activeInHierarchy && !_claimFrame.canvasRenderer.cull)
                _claimFrame.canvasRenderer.SetAlpha(opacity);
        }

        private static Image ReadyPlate(Transform root, string name, float x0, float y0, float x1, float y1)
        {
            var plate = ExpeditionUi.Panel(root, name, x0, y0, x1, y1);
            var brass = ExpeditionArt.Frame(true);
            plate.sprite = brass != null ? brass : ExpeditionTheme.Plate;
            plate.color = brass != null ? Color.white : ExpeditionTheme.Amber;
            plate.enabled = false;
            return plate;
        }

        private IEnumerator MountMedal()
        {
            const float duration = .36f;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var progress = elapsed / duration;
                var scale = Mathf.Lerp(.89f, 1f, progress) + Mathf.Sin(progress * Mathf.PI) * .16f;
                _mount.rectTransform.localScale = Vector3.one * scale;
                yield return null;
            }
            _mount.rectTransform.localScale = Vector3.one;
            _mountMotion = null;
        }

        private void OnDisable() => ResetMount();

        private void ResetMount()
        {
            if (_mountMotion != null) StopCoroutine(_mountMotion);
            _mountMotion = null;
            if (_mount != null) _mount.rectTransform.localScale = Vector3.one;
        }

        private static string At(string[] values, int index) =>
            values != null && index < values.Length ? values[index] ?? string.Empty : string.Empty;

        private static TMP_Text Label(Transform root, string name, float size, float minimum,
            float x0, float y0, float x1, float y1, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var text = ExpeditionUi.Text(root, name, "", size, x0, y0, x1, y1, alignment);
            text.margin = Vector4.zero;
            text.richText = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = minimum;
            text.fontSizeMax = size;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return text;
        }
    }
}
