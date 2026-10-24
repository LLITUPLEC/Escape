using System;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Scene-owned outcome screen. Rewards and navigation retain their existing authoritative owners.</summary>
    public sealed class Match3GameOverPanel : MonoBehaviour
    {
        [SerializeField] public TMP_Text titleText;
        [SerializeField] public TMP_Text rewardText;
        [SerializeField] public Button backButton;
        [Header("Reward Rows Layout")]
        [SerializeField, Min(1)] public int rewardCellsPerLine = 2;
        [SerializeField, Min(1f)] public float rewardIconPreferredSize = 60f;
        [SerializeField, Min(1f)] public float rewardValueFontSize = 50f;
        public int RewardCellsPerLine => Mathf.Max(1, rewardCellsPerLine);
        public float RewardIconPreferredSize => Mathf.Max(1, rewardIconPreferredSize);
        public float RewardValueFontSize => Mathf.Max(1, rewardValueFontSize);
        public event Action OnBackClicked;

        private bool _expeditionStyle, _rewardLayoutPending, _resetScroll;
        private Button _boundBack;
        private RectTransform _rewardViewport, _rewardRows;
        private ScrollRect _rewardScroll;
        private Image _hero, _noticeFrame, _headingFrame, _victoryWings;
        private Sprite _explorerSprite, _victoryBustSprite;
        private TMP_Text _notice, _emptyRewards;
        private ExpeditionOutcomeGlow _glow;
        private Material _ghostMaterial;

        private void Awake() { ResolveReferences(); BindBackCallback(); }
        private void OnEnable() { if (_expeditionStyle) RequestRewardRefresh(); }

        private void ResolveReferences()
        {
            titleText ??= transform.Find("TitleText")?.GetComponent<TMP_Text>();
            titleText ??= transform.Find("Title")?.GetComponent<TMP_Text>();
            rewardText ??= transform.Find("RewardText")?.GetComponent<TMP_Text>();
            rewardText ??= transform.Find("Reward")?.GetComponent<TMP_Text>();
        }

        public void UseExpeditionStyle()
        {
            ResolveReferences();
            var safe = ExpeditionMatchStates.PrepareOverlay(this, 2600);
            if (_expeditionStyle) { BindBackCallback(); RequestRewardRefresh(); return; }
            _expeditionStyle = true;
            foreach (var group in GetComponentsInChildren<LayoutGroup>(true)) group.enabled = false;
            foreach (var fit in GetComponentsInChildren<ContentSizeFitter>(true)) fit.enabled = false;
            foreach (var outline in GetComponents<Outline>()) outline.enabled = false;
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name == "Stripe") child.gameObject.SetActive(false);
            var canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null) { canvasGroup.alpha = 1; canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }
            rewardCellsPerLine = 3; rewardIconPreferredSize = 98; rewardValueFontSize = 46;
            var heading = ExpeditionMatchStates.Panel(safe, "OutcomeHeadingFrame", .10f, .835f, .90f, .94f);
            _headingFrame = heading;
            _victoryWings = ExpeditionUi.Fill(safe, "VictoryGoldenWings", .045f, .861f, .955f, .922f, Color.white);
            _victoryWings.sprite = ExpeditionArt.Frame(true); _victoryWings.type = Image.Type.Sliced;
            _victoryWings.pixelsPerUnitMultiplier = 3;
            _victoryWings.transform.SetSiblingIndex(heading.transform.GetSiblingIndex());
            _victoryWings.gameObject.SetActive(false);
            if (titleText == null) titleText = ExpeditionUi.Text(heading.transform, "TitleText", "", 82, .08f, .08f, .92f, .88f);
            ExpeditionMatchStates.Place(titleText, heading.transform, .08f, .08f, .92f, .88f, 82);
            titleText.fontStyle = FontStyles.Bold;
            var crest = ExpeditionUi.Fill(safe, "OutcomeCrest", .445f, .927f, .555f, .988f, Color.white);
            crest.sprite = ExpeditionTheme.Icon("swords"); crest.preserveAspect = true;
            _hero = ExpeditionMatchStates.Portrait(safe, "OutcomeExplorerFrame", .15f, .43f, .85f, .835f, false);
            _explorerSprite = _hero.sprite;
            _victoryBustSprite = Resources.Load<Sprite>("Expedition/Art/hero-victory-bust-v1");
            var glowRect = ExpeditionUi.Rect(_hero.transform.parent, "OutcomeLight", -.10f, -.05f, 1.10f, 1.05f);
            _glow = glowRect.gameObject.AddComponent<ExpeditionOutcomeGlow>();
            _glow.color = ExpeditionTheme.Amber; _glow.raycastTarget = false;
            glowRect.SetAsFirstSibling();
            _ghostMaterial = Resources.Load<Material>("Expedition/UI/equipment-ghost");
            _noticeFrame = ExpeditionMatchStates.Panel(safe, "ActualOutcomeNotice", .16f, .395f, .84f, .465f);
            _notice = ExpeditionUi.Text(_noticeFrame.transform, "ActualRefundNotice", "", 31,
                .05f, .12f, .95f, .88f, TextAlignmentOptions.Center);
            _notice.enableAutoSizing = true; _notice.fontSizeMin = 23; _notice.fontSizeMax = 31;
            _noticeFrame.gameObject.SetActive(false);
            var rewardsFrame = ExpeditionMatchStates.Panel(safe, "ActualRewardsFrame", .055f, .185f, .945f, .385f);
            var caption = ExpeditionUi.Text(rewardsFrame.transform, "ActualRewardsCaption", "ПОЛУЧЕНО", 27,
                .05f, .81f, .95f, .96f, TextAlignmentOptions.Center);
            caption.color = ExpeditionTheme.Amber;
            _rewardViewport = ExpeditionUi.Rect(rewardsFrame.transform, "ExpeditionRewardViewport", .035f, .075f, .95f, .78f);
            _rewardViewport.gameObject.AddComponent<RectMask2D>();
            var hit = _rewardViewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear; hit.raycastTarget = true;
            _rewardScroll = _rewardViewport.gameObject.AddComponent<ScrollRect>();
            _rewardScroll.viewport = _rewardViewport; _rewardScroll.horizontal = false;
            _rewardScroll.vertical = true; _rewardScroll.movementType = ScrollRect.MovementType.Clamped;
            _rewardScroll.scrollSensitivity = 45;
            ExpeditionMatchStates.ScrollBar(_rewardScroll, rewardsFrame.transform, .957f, .10f, .971f, .76f);
            var viewportObserver = _rewardViewport.gameObject.AddComponent<ExpeditionRewardRowsObserver>();
            viewportObserver.owner = this; viewportObserver.observeDimensions = true;
            if (rewardText == null) rewardText = ExpeditionUi.Text(_rewardViewport, "RewardText", "", 34, .03f, .04f, .97f, .96f);
            ExpeditionMatchStates.Place(rewardText, _rewardViewport, .03f, .04f, .97f, .96f, 34);
            _emptyRewards = ExpeditionUi.Text(_rewardViewport, "NoActualRewards", "Награды не начислены", 29,
                .05f, .10f, .95f, .90f, TextAlignmentOptions.Center);
            _emptyRewards.color = ExpeditionTheme.Muted; _emptyRewards.gameObject.SetActive(false);
            if (backButton == null)
                backButton = ExpeditionUi.Button(safe, "Back", "ПРОДОЛЖИТЬ", .18f, .075f, .82f, .15f, null, true, 44);
            backButton.transform.SetParent(safe, false);
            ExpeditionUi.SetRect(backButton.transform as RectTransform, .18f, .075f, .82f, .15f);
            ExpeditionUi.StyleButton(backButton, true, 44);
            ExpeditionUi.Label(backButton, "ПРОДОЛЖИТЬ");
            BindBackCallback();
            RequestRewardRefresh();
        }

        public void RequestRewardRefresh() { if (_expeditionStyle) _rewardLayoutPending = true; }
        private void LateUpdate()
        {
            if (!_expeditionStyle || !_rewardLayoutPending) return;
            _rewardLayoutPending = false;
            StyleRewardRows();
        }
        private void OnRectTransformDimensionsChange() { RequestRewardRefresh(); }

        private void StyleRewardRows()
        {
            if (_rewardViewport == null) return;
            if (_rewardRows == null) _rewardRows = ExpeditionUi.Find(transform, "RewardRows") as RectTransform;
            var hasRows = _rewardRows != null && _rewardRows.gameObject.activeSelf && _rewardRows.childCount > 0;
            _emptyRewards.gameObject.SetActive(!hasRows && (rewardText == null || !rewardText.gameObject.activeSelf));
            if (_rewardRows == null) return;
            var rows = _rewardRows;
            var previousY = rows.anchoredPosition.y;
            if (rows.parent != _rewardViewport) rows.SetParent(_rewardViewport, false);
            rows.anchorMin = new Vector2(0, 1); rows.anchorMax = Vector2.one;
            rows.pivot = new Vector2(.5f, 1); rows.sizeDelta = new Vector2(0, rows.sizeDelta.y);
            var rootFitter = ExpeditionUi.Ensure<ContentSizeFitter>(rows.gameObject);
            rootFitter.enabled = true;
            rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var column = ExpeditionUi.Ensure<VerticalLayoutGroup>(rows.gameObject);
            column.enabled = true; column.padding = new RectOffset(10, 10, 8, 8); column.spacing = 18;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            var observer = ExpeditionUi.Ensure<ExpeditionRewardRowsObserver>(rows.gameObject);
            observer.owner = this;
            foreach (Transform line in rows)
            {
                if (line.name != "RewardLine") continue;
                var lineFitter = line.GetComponent<ContentSizeFitter>();
                if (lineFitter != null) lineFitter.enabled = false;
                var horizontal = ExpeditionUi.Ensure<HorizontalLayoutGroup>(line.gameObject);
                horizontal.enabled = true; horizontal.padding = new RectOffset(); horizontal.spacing = 16;
                horizontal.childControlWidth = horizontal.childControlHeight = true;
                horizontal.childForceExpandWidth = true; horizontal.childForceExpandHeight = false;
                var lineSize = ExpeditionUi.Ensure<LayoutElement>(line.gameObject);
                lineSize.minHeight = lineSize.preferredHeight = 166; lineSize.flexibleHeight = 0;
                foreach (Transform row in line)
                {
                    var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                    if (rowLayout != null) rowLayout.enabled = false;
                    var fitter = row.GetComponent<ContentSizeFitter>();
                    if (fitter != null) fitter.enabled = false;
                    var size = ExpeditionUi.Ensure<LayoutElement>(row.gameObject);
                    size.minWidth = 0; size.flexibleWidth = 1;
                    size.minHeight = size.preferredHeight = 166; size.flexibleHeight = 0;
                    var plate = ExpeditionUi.Ensure<Image>(row.gameObject);
                    ExpeditionUi.StylePanel(plate, Color.white); plate.raycastTarget = false;
                    var icon = row.Find("Icon")?.GetComponent<Image>();
                    if (icon != null)
                    {
                        ExpeditionUi.SetRect(icon.rectTransform, .075f, .20f, .425f, .82f);
                        icon.preserveAspect = true; icon.raycastTarget = false;
                    }
                    var value = row.Find("Value")?.GetComponent<TMP_Text>();
                    if (value == null) continue;
                    ExpeditionMatchStates.Place(value, row, icon != null ? .455f : .08f, .16f, .925f, .84f,
                        rewardValueFontSize, TextAlignmentOptions.MidlineLeft);
                    value.fontSizeMin = 23; value.fontSizeMax = rewardValueFontSize;
                    value.color = ExpeditionTheme.Ivory;
                }
            }
            _rewardScroll.content = rows;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rows);
            var range = Mathf.Max(0, rows.rect.height - _rewardViewport.rect.height);
            var overflow = range > .5f;
            var anchor = overflow ? 1f : .5f;
            rows.anchorMin = new Vector2(0, anchor); rows.anchorMax = new Vector2(1, anchor);
            rows.pivot = new Vector2(.5f, anchor);
            rows.anchoredPosition = new Vector2(0, overflow && !_resetScroll ? Mathf.Clamp(previousY, 0, range) : 0);
            _rewardScroll.vertical = overflow;
            if (_resetScroll) _rewardScroll.StopMovement();
            _resetScroll = false;
        }

        public void Show(bool won, string customRewardText = null, string customTitle = null)
        {
            if (!_expeditionStyle) UseExpeditionStyle();
            BindBackCallback();
            gameObject.SetActive(true);
            var draw = !string.IsNullOrWhiteSpace(customTitle) && customTitle.IndexOf("Ничья", StringComparison.OrdinalIgnoreCase) >= 0;
            titleText.text = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            titleText.color = draw ? ExpeditionTheme.Ivory : won ? ExpeditionTheme.Amber : ExpeditionTheme.Coral;
            _headingFrame.sprite = ExpeditionArt.Frame(won) ?? ExpeditionTheme.Plate;
            _headingFrame.color = Color.white;
            _victoryWings.gameObject.SetActive(won);
            var victoryBust = won && _victoryBustSprite != null;
            _hero.sprite = victoryBust ? _victoryBustSprite : _explorerSprite;
            if (victoryBust)
            {
                var fit = _hero.GetComponent<AspectRatioFitter>();
                if (fit != null) fit.enabled = false;
                ExpeditionUi.SetRect(_hero.rectTransform, .01f, .01f, .99f, .99f);
            }
            else ExpeditionMatchStates.CropUpperBody(_hero, 1.4f);
            _hero.color = won ? Color.white : draw ? new Color(.83f, .91f, .92f, 1) : new Color(.75f, .65f, .65f, .90f);
            _hero.material = won ? null : _ghostMaterial;
            _glow.Celebration = won;
            var glowColor = draw ? ExpeditionTheme.Teal : won ? ExpeditionTheme.Amber : ExpeditionTheme.Coral;
            if (!won) glowColor.a = .35f;
            _glow.color = glowColor;
            rewardText.text = customRewardText ?? string.Empty;
            rewardText.gameObject.SetActive(!string.IsNullOrWhiteSpace(rewardText.text));
            var refund = !string.IsNullOrEmpty(customRewardText) && customRewardText.IndexOf("взнос возвращён", StringComparison.OrdinalIgnoreCase) >= 0;
            _noticeFrame.gameObject.SetActive(refund);
            if (refund) _notice.text = customRewardText.Split('\n')[0].Trim();
            _resetScroll = true;
            RequestRewardRefresh();
        }

        private void BindBackCallback()
        {
            if (_boundBack == backButton) return;
            if (_boundBack != null) _boundBack.onClick.RemoveListener(HandleBack);
            _boundBack = backButton;
            if (_boundBack != null) _boundBack.onClick.AddListener(HandleBack);
        }
        private void HandleBack() => OnBackClicked?.Invoke();
        public void Hide() => gameObject.SetActive(false);
    }
}
