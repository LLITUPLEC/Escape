using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Presentation only. The existing board, sprites, combat state, inputs and VFX keep their owners.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionBattleLayout : MonoBehaviour
    {
        private bool _installed;
        private bool _resizing;
        private Match3BoardView _board;
        private RectTransform _content;
        private Match3GameHUD _hud;

        public static ExpeditionBattleLayout Install(Scene scene)
        {
            if (!scene.IsValid()) return null;
            Match3BoardView board = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                board = root.GetComponentInChildren<Match3BoardView>(true);
                if (board != null) break;
            }
            if (board == null) return null;
            var canvas = board.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            var view = ExpeditionUi.Ensure<ExpeditionBattleLayout>(canvas.gameObject);
            if (!view._installed) view.Build(canvas.transform, board);
            return view;
        }

        private void Build(Transform canvas, Match3BoardView board)
        {
            _installed = true;
            _board = board;
            var background = ExpeditionUi.Panel(canvas, "ExpeditionBattleBackdrop", 0, 0, 1, 1, ExpeditionTheme.Panel);
            background.transform.SetAsFirstSibling();
            var art = Resources.Load<Sprite>("Expedition/Art/battle-world-v4");
            if (art == null) art = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
            if (art != null)
            {
                background.sprite = art;
                background.type = Image.Type.Simple;
                background.color = Color.white;
                var fit = background.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = art.rect.width / art.rect.height;
            }
            var shade = ExpeditionUi.Fill(canvas, "ExpeditionBattleAtmosphere", 0, 0, 1, 1, new Color(0.01f, 0.025f, 0.03f, .04f));
            shade.transform.SetSiblingIndex(background.transform.GetSiblingIndex() + 1);
            var oldBackground = canvas.Find("Bg")?.GetComponent<Image>();
            if (oldBackground != null) oldBackground.enabled = false;
            _content = ExpeditionUi.Rect(canvas, "ExpeditionBattleSafeArea", 0, 0, 1, 1);
            _content.gameObject.AddComponent<ExpeditionSafeArea>();
            var viewport = _content.gameObject.AddComponent<ExpeditionBattleViewport>();
            viewport.owner = this;
            // The single portrait environment continues from the fighting platform
            // behind the board and abilities; no clipped second scene or transition band.

            var playerPanels = canvas.GetComponentsInChildren<Match3PlayerPanel>(true);
            System.Array.Sort(playerPanels, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            for (var i = 0; i < playerPanels.Length; i++)
            {
                var player = playerPanels[i];
                player.transform.SetParent(_content, false);
                ExpeditionUi.SetRect(player.transform as RectTransform, i == 0 ? .012f : .51f, .918f, i == 0 ? .49f : .988f, .992f);
                player.UseExpeditionStyle();
                var figure = ExpeditionUi.Rect(_content, i == 0 ? "ExpeditionPlayerFigure" : "ExpeditionOpponentFigure",
                    i == 0 ? .012f : .50f, .731f, i == 0 ? .50f : .988f, .917f).gameObject.AddComponent<Image>();
                figure.preserveAspect = true;
                figure.raycastTarget = false;
                figure.gameObject.AddComponent<ExpeditionCombatantFigure>().Bind(player.avatarImage, i == 0);
            }
            // Portrait frames, status badges and stat popovers sit above the figures.
            foreach (var player in playerPanels) player.transform.SetAsLastSibling();

            board.transform.SetParent(_content, false);
            ExpeditionUi.SetRect(board.transform as RectTransform, .04f, .17f, .96f, .655f);
            var frame = board.transform.Find("Frame") as RectTransform;
            if (frame != null)
            {
                ExpeditionUi.SetRect(frame, 0f, 0f, 1f, 1f);
                var aspect = frame.GetComponent<AspectRatioFitter>();
                // Square sizing is handled on actual viewport changes, so tall phones cannot
                // grow the height-driven frame beyond the available screen width.
                if (aspect != null) aspect.enabled = false;
                var image = frame.GetComponent<Image>();
                if (image != null) ExpeditionUi.StylePanel(image, new Color32(60, 91, 92, 255));
                var tray = frame.Find("Bg")?.GetComponent<Image>();
                if (tray != null) { tray.sprite = null; tray.color = new Color32(12, 30, 37, 255); }
            }
            foreach (var border in board.GetComponentsInChildren<RawImage>(true))
                if (border.name == "GridBorder") border.color = new Color(0.34f, 0.58f, 0.57f, 0.25f);

            var hud = canvas.GetComponentInChildren<Match3GameHUD>(true);
            _hud = hud;
            if (hud != null)
            {
                hud.transform.SetParent(_content, false);
                ExpeditionUi.SetRect(hud.transform as RectTransform, .20f, .661f, .80f, .735f);
                hud.UseExpeditionStyle();
                var hudImage = hud.GetComponent<Image>();
                if (hudImage == null) hudImage = hud.gameObject.AddComponent<Image>();
                ExpeditionUi.StylePanel(hudImage, ExpeditionTheme.Panel);
                hudImage.pixelsPerUnitMultiplier = 5;
                hudImage.raycastTarget = false;
                Put(hud.turnText, hud.transform, .04f, .61f, .96f, .97f, 27, ExpeditionTheme.Ivory);
                Put(hud.timerText, hud.transform, .04f, .015f, .96f, .64f, 60, ExpeditionTheme.Amber);
                Put(hud.timerPhaseText, hud.transform, .05f, .08f, .95f, .60f, 25, ExpeditionTheme.Muted);
                hud.BindCompactTurnSeparator(null);
                if (hud.turnText != null)
                {
                    hud.turnText.alignment = TextAlignmentOptions.Center;
                    hud.turnText.textWrappingMode = TextWrappingModes.NoWrap;
                    hud.turnText.enableAutoSizing = true;
                    hud.turnText.fontSizeMin = 17;
                    hud.turnText.fontSizeMax = 27;
                }
                if (hud.timerText != null)
                {
                    hud.timerText.alignment = TextAlignmentOptions.Center;
                    hud.timerText.textWrappingMode = TextWrappingModes.NoWrap;
                    hud.timerText.overflowMode = TextOverflowModes.Overflow;
                }
                if (hud.timerPhaseText != null) hud.timerPhaseText.alignment = TextAlignmentOptions.Center;
                Put(hud.extraTurnText, _content, .06f, .632f, .94f, .656f, 26, ExpeditionTheme.Amber);
                LayoutRaceBanner(hud.raceGoalText, false);
                LayoutRaceBanner(hud.raceLastTurnText, true);
                LayoutRaceBonus(hud.raceManaBonusText != null ? hud.raceManaBonusText.rectTransform : null);
                if (hud.affixButton != null)
                {
                    hud.affixButton.transform.SetParent(_content, false);
                    ExpeditionUi.SetRect(hud.affixButton.transform as RectTransform, .855f, .664f, .96f, .718f);
                    ExpeditionUi.IconOnly(hud.affixButton, hud.affixButtonIconImage != null ? (Graphic)hud.affixButtonIconImage : hud.affixButtonLabel);
                }
            }

            var abilities = canvas.GetComponentInChildren<Match3AbilityPanel>(true);
            if (abilities != null)
            {
                abilities.transform.SetParent(_content, false);
                ExpeditionUi.SetRect(abilities.transform as RectTransform, .025f, .05f, .975f, .16f);
                abilities.UseExpeditionStyle();
                ApplyAbilityLayout(abilities);
                if (abilities.abilityHint != null)
                {
                    abilities.abilityHint.transform.SetParent(_content, false);
                    ExpeditionUi.SetRect(abilities.abilityHint.transform as RectTransform, .1f, .61f, .9f, .66f);
                    var image = abilities.abilityHint.GetComponent<Image>();
                    if (image != null) ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
                    foreach (var text in abilities.abilityHint.GetComponentsInChildren<TMP_Text>(true)) StyleText(text, 28, ExpeditionTheme.Ivory);
                }
            }
            var quit = ExpeditionUi.Find(canvas, "QuitBtn")?.GetComponent<Button>();
            if (quit != null)
            {
                quit.transform.SetParent(_content, false);
                ExpeditionUi.SetRect(quit.transform as RectTransform, .81f, .006f, .975f, .042f);
                ExpeditionUi.StyleButton(quit, false, 21);
            }
            ExpeditionUi.Text(_content, "ExpeditionBattleCaption", "Разлом · тактический бой", 23, .06f, .008f, .78f, .04f, TextAlignmentOptions.Center);
            StyleOverlays(canvas, _content);
            ExpeditionCloseButton.StyleWithin(canvas);
            ResizeBoard();
        }

        public static void ApplyPlayerContents(Match3PlayerPanel panel)
        {
            var root = panel.transform;
            var opponent = root is RectTransform panelRect && panelRect.anchorMin.x >= .5f;
            var x0 = opponent ? .03f : .36f;
            var x1 = opponent ? .64f : .97f;
            foreach (var cut in root.GetComponentsInChildren<RightCornerCutEffect>(true)) cut.enabled = false;
            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
                image.raycastTarget = false;
            }
            var body = HeaderPlate(root, "ExpeditionHeaderBody", opponent ? .005f : .315f, .025f, opponent ? .685f : .995f, .975f);
            var portraitPlate = HeaderPlate(root, "ExpeditionPortraitFrame", opponent ? .66f : .005f, .025f, opponent ? .995f : .34f, .975f);
            body.transform.SetAsFirstSibling();
            portraitPlate.transform.SetSiblingIndex(body.transform.GetSiblingIndex() + 1);
            if (panel.avatarImage != null)
            {
                var oldParent = panel.avatarImage.transform.parent;
                panel.avatarImage.transform.SetParent(root, false);
                if (oldParent != null && oldParent != root && oldParent.name == "Avatar")
                {
                    var oldPlate = oldParent.GetComponent<Image>();
                    if (oldPlate != null) oldPlate.enabled = false;
                }
                ExpeditionUi.SetRect(panel.avatarImage.rectTransform, opponent ? .68f : .025f, .065f, opponent ? .975f : .32f, .935f);
                panel.avatarImage.preserveAspect = true;
                panel.avatarImage.enabled = true;
                panel.avatarImage.color = panel.avatarImage.sprite != null ? Color.white : ExpeditionTheme.Teal;
                ExpeditionUi.Ensure<ExpeditionBattlePortrait>(panel.avatarImage.gameObject).Bind(panel.avatarImage);
                ExpeditionUi.Ensure<ExpeditionBattleResponse>(panel.gameObject).Bind(panel.avatarImage);
            }
            foreach (var decoration in root.GetComponentsInChildren<Image>(true))
                if (decoration.name == "border") decoration.enabled = false;
            Put(panel.nameText, root, x0 + .012f, .71f, x1 - .07f, .95f, 25, ExpeditionTheme.Ivory);
            if (panel.nameText != null)
            {
                panel.nameText.alignment = TextAlignmentOptions.MidlineLeft;
                panel.nameText.textWrappingMode = TextWrappingModes.NoWrap;
                panel.nameText.enableAutoSizing = true;
                panel.nameText.fontSizeMin = 16;
                panel.nameText.fontSizeMax = 25;
            }
            var info = root.Find("ExpeditionHeaderInfo")?.GetComponent<TMP_Text>();
            if (info == null) info = ExpeditionUi.Text(root, "ExpeditionHeaderInfo", "i", 20, x1 - .065f, .73f, x1 - .005f, .94f, TextAlignmentOptions.Center);
            info.color = ExpeditionTheme.Muted;
            info.gameObject.SetActive(!panel.IsRaceHudMode);
            StyleShieldBadge(panel, opponent);
            var hpTrack = ExpeditionUi.Find(root, "HpBarTrack");
            var manaTrack = ExpeditionUi.Find(root, "MpBarTrack");
            var raceTrack = ExpeditionUi.Find(root, "MpBarTrack_Race");
            StyleTrack(hpTrack, root, x0, .405f, x1, .67f, new Color32(197, 53, 40, 255));
            StyleTrack(manaTrack, root, x0, .105f, x1, .37f, new Color32(18, 130, 175, 255));
            StyleTrack(raceTrack, root, x0, .23f, x1, .62f, new Color32(18, 130, 175, 255));
            panel.RefreshExpeditionBars();
            foreach (var labelName in new[] { "HpLabel", "MpLabel" })
            {
                var label = ExpeditionUi.Find(root, labelName);
                if (label != null) label.gameObject.SetActive(false);
            }
            StyleValue(panel.hpText);
            StyleValue(panel.manaText);
            var statsFrame = panel.combatStatsName != null ? panel.combatStatsName.transform.parent as RectTransform : null;
            if (panel.combatStatsName != null)
            {
                if (statsFrame == null || statsFrame == root)
                {
                    statsFrame = ExpeditionUi.Rect(root, "ExpeditionCombatStatsPopover", .015f, -1.15f, .985f, -.35f);
                    panel.combatStatsName.transform.SetParent(statsFrame, false);
                }
                statsFrame.SetParent(root, false);
                ExpeditionUi.SetRect(statsFrame, .015f, -1.15f, .985f, -.35f);
                var statsBackground = ExpeditionUi.Ensure<Image>(statsFrame.gameObject);
                ExpeditionUi.StylePanel(statsBackground, ExpeditionTheme.Panel);
                statsBackground.pixelsPerUnitMultiplier = 5;
                statsBackground.raycastTarget = true;
                var title = statsFrame.Find("ExpeditionStatsTitle")?.GetComponent<TMP_Text>();
                if (title == null) title = ExpeditionUi.Text(statsFrame, "ExpeditionStatsTitle", "ХАРАКТЕРИСТИКИ", 18, .05f, .72f, .95f, .94f, TextAlignmentOptions.Center);
                title.color = ExpeditionTheme.Amber;
                foreach (var stats in new[] { panel.combatStatsName, panel.combatStatsValue })
                {
                    if (stats == null) continue;
                    ExpeditionUi.SetRect(stats.rectTransform, .035f, .08f, .965f, .70f);
                    StyleText(stats, 22, ExpeditionTheme.Muted);
                    StyleCombatStats(stats);
                }
                ExpeditionUi.Ensure<ExpeditionCombatStatsPopover>(panel.gameObject).Bind(panel, statsFrame);
                statsFrame.SetAsLastSibling();
            }
            if (panel.combatStatsValue != null) panel.combatStatsValue.gameObject.SetActive(false);
            if (panel.avatarLevelText != null)
            {
                panel.avatarLevelText.transform.SetParent(panel.avatarImage != null ? panel.avatarImage.transform : root, false);
                ExpeditionUi.SetRect(panel.avatarLevelText.rectTransform, .73f, .015f, .99f, .245f);
                StyleText(panel.avatarLevelText, 17, ExpeditionTheme.Amber);
                panel.avatarLevelText.alignment = TextAlignmentOptions.Center;
            }
            StylePopupAnchors(panel);
        }

        private static Image HeaderPlate(Transform root, string name, float x0, float y0, float x1, float y1)
        {
            var image = root.Find(name)?.GetComponent<Image>();
            if (image == null) image = ExpeditionUi.Panel(root, name, x0, y0, x1, y1, ExpeditionTheme.Panel);
            ExpeditionUi.SetRect(image.rectTransform, x0, y0, x1, y1);
            ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
            image.pixelsPerUnitMultiplier = 5;
            image.raycastTarget = true;
            return image;
        }

        private static void StyleShieldBadge(Match3PlayerPanel panel, bool opponent)
        {
            if (panel.buffStateText == null) return;
            var root = panel.transform;
            var badge = root.Find("ExpeditionShieldBadge")?.GetComponent<Image>();
            if (badge == null) badge = ExpeditionUi.Panel(root, "ExpeditionShieldBadge", 0, 0, 1, 1, ExpeditionTheme.Panel);
            ExpeditionUi.SetRect(badge.rectTransform, opponent ? .50f : .005f, -.29f, opponent ? .995f : .50f, -.025f);
            ExpeditionUi.StylePanel(badge, ExpeditionTheme.Panel);
            badge.pixelsPerUnitMultiplier = 7;
            badge.raycastTarget = false;
            Put(panel.buffStateText, badge.transform, .06f, .05f, .94f, .95f, 22, ExpeditionTheme.Amber);
            panel.buffStateText.alignment = TextAlignmentOptions.Center;
            panel.buffStateText.textWrappingMode = TextWrappingModes.NoWrap;
            panel.buffStateText.enableAutoSizing = true;
            panel.buffStateText.fontSizeMin = 18;
            panel.buffStateText.fontSizeMax = 22;
            badge.gameObject.SetActive(!panel.IsRaceHudMode && !string.IsNullOrEmpty(panel.buffStateText.text));
        }

        private static void StylePopupAnchors(Match3PlayerPanel panel)
        {
            if (panel.gainPopup == null && panel.damagePopup == null) return;
            // Avatars sit against the screen edges. Keep both gain arcs and the
            // damage punch inside the panel without moving the source portraits.
            var anchor = panel.transform.Find("ExpeditionPopupAnchor") as RectTransform;
            if (anchor == null)
                anchor = ExpeditionUi.Rect(panel.transform, "ExpeditionPopupAnchor", .5f, .4f, .5f, .4f);
            anchor.SetAsLastSibling();
            if (panel.gainPopup != null)
            {
                ReparentPopup(panel.gainPopup.transform as RectTransform, anchor);
                panel.gainPopupAnchor = anchor;
            }
            if (panel.damagePopup != null)
            {
                ReparentPopup(panel.damagePopup.transform as RectTransform, anchor);
                panel.damagePopupAnchor = anchor;
            }
        }

        private static void ReparentPopup(RectTransform popup, RectTransform anchor)
        {
            if (popup == null || popup.parent == anchor) return;
            // DamagePopupView caches this position in Awake; gain flyouts are
            // animated in local space. Preserve both while replacing their parent.
            var position = popup.anchoredPosition;
            var size = popup.rect.size;
            popup.SetParent(anchor, false);
            popup.anchorMin = popup.anchorMax = new Vector2(.5f, .5f);
            popup.sizeDelta = size;
            popup.anchoredPosition = position;
        }

        private static void StyleCombatStats(TMP_Text text)
        {
            text.alignment = TextAlignmentOptions.Midline;
            text.margin = new Vector4(6, 3, 6, 7);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = 22;
            text.overflowMode = TextOverflowModes.Truncate;
        }

        private static void StyleTrack(Transform track, Transform root, float x0, float y0, float x1, float y1, Color fillColor)
        {
            if (track == null) return;
            var oldParent = track.parent;
            track.SetParent(root, false);
            if (oldParent != null && oldParent != root && (oldParent.name == "HpBarFrame" || oldParent.name == "MpBarFrame"))
            {
                var oldFrame = oldParent.GetComponent<Image>();
                if (oldFrame != null) oldFrame.enabled = false;
            }
            ExpeditionUi.SetRect(track as RectTransform, x0, y0, x1, y1);
            var image = track.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = true;
                ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
                image.pixelsPerUnitMultiplier = 6;
                image.raycastTarget = false;
            }
            var outline = track.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            foreach (var fill in track.GetComponentsInChildren<Image>(true))
                if (fill.name == "HpBarFill" || fill.name == "MpBarFill")
                {
                    fill.sprite = null;
                    fill.type = Image.Type.Simple;
                    fill.overrideSprite = null;
                    fill.color = fillColor;
                    fill.transform.localScale = Vector3.one;
                    fill.transform.localRotation = Quaternion.identity;
                }
        }

        private static void StyleValue(TMP_Text value)
        {
            if (value == null) return;
            value.transform.localScale = Vector3.one;
            value.transform.localRotation = Quaternion.identity;
            foreach (var prefix in new[] { "HP ", "Мана " })
                if (value.text.StartsWith(prefix)) value.text = value.text.Substring(prefix.Length);
            StyleText(value, 23, ExpeditionTheme.Ivory);
            value.alignment = TextAlignmentOptions.Center;
            value.enableAutoSizing = true;
            value.fontSizeMin = 17;
            value.fontSizeMax = 23;
            value.margin = new Vector4(8, 1, 8, 1);
            value.overflowMode = TextOverflowModes.Overflow;
        }

        public static void ApplyAbilityLayout(Match3AbilityPanel panel)
        {
            var buttons = new[] { panel.petardButton, panel.crossButton, panel.squareButton, panel.shieldButton, panel.furyButton };
            var names = new[] { "Петарда", "Крест", "Квадрат", "Щит", "Ярость" };
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null) continue;
                ExpeditionUi.SetRect(button.transform as RectTransform, i * .2f + .006f, .03f, (i + 1) * .2f - .006f, .97f);
                var rootImage = ExpeditionUi.Ensure<Image>(button.gameObject);
                rootImage.raycastTarget = true;
                button.targetGraphic = rootImage;
                ExpeditionUi.StyleButton(button, false, 24);
                foreach (var oldName in new[] { "Label", "Icon" })
                {
                    var old = button.transform.Find(oldName);
                    if (old != null) old.gameObject.SetActive(false);
                }
                var icon = button.transform.Find("AbilityIcon")?.GetComponent<Image>();
                if (icon != null)
                {
                    // The authored ability sprite is deliberately retained.
                    ExpeditionUi.SetRect(icon.rectTransform, .10f, .075f, .90f, .825f);
                    icon.raycastTarget = false;
                    icon.preserveAspect = true;
                }
                var name = button.transform.Find("ExpeditionAbilityName")?.GetComponent<TMP_Text>();
                if (name == null) name = ExpeditionUi.Text(button.transform, "ExpeditionAbilityName", names[i], 23, .01f, .8f, .99f, .99f, TextAlignmentOptions.Center);
                name.text = names[i];
                var cost = button.transform.Find("AbilityCost")?.GetComponent<TMP_Text>();
                if (cost != null)
                {
                    StyleText(cost, 24, ExpeditionTheme.Amber);
                    cost.alignment = TextAlignmentOptions.Center;
                    if (icon != null) ExpeditionUi.Ensure<ExpeditionAbilityCostCorner>(button.gameObject).Bind(icon, cost);
                }
                var block = button.transform.Find("CooldownBlock") as RectTransform;
                if (block != null) ExpeditionUi.SetRect(block, .10f, .075f, .90f, .825f);
                var cooldown = button.transform.Find("CooldownCenterText")?.GetComponent<TMP_Text>();
                if (cooldown == null) cooldown = ExpeditionUi.Text(button.transform, "CooldownCenterText", "", 31, .06f, .34f, .94f, .73f, TextAlignmentOptions.Center);
                ExpeditionUi.SetRect(cooldown.rectTransform, .12f, .30f, .88f, .64f);
                StyleText(cooldown, 31, ExpeditionTheme.Amber);
                cooldown.alignment = TextAlignmentOptions.Center;
                cooldown.gameObject.SetActive(false);
                var selection = button.transform.Find("ExpeditionSelectedRule");
                if (selection == null)
                {
                    var rule = ExpeditionUi.Panel(button.transform, "ExpeditionSelectedRule", .025f, .975f, .975f, .995f, ExpeditionTheme.Amber);
                    rule.gameObject.SetActive(false);
                }
            }
        }

        public static void LayoutRaceBanner(TMP_Text label, bool lastTurn)
        {
            if (label == null) return;
            var view = label.GetComponentInParent<ExpeditionBattleLayout>();
            if (view == null) return;
            Put(label, view._content != null ? view._content : view.transform, lastTurn ? .06f : .26f, lastTurn ? .632f : .885f,
                lastTurn ? .94f : .74f, lastTurn ? .656f : .911f, lastTurn ? 27 : 22, ExpeditionTheme.Amber);
            label.alignment = TextAlignmentOptions.Center;
        }

        public static void LayoutRaceBonus(RectTransform label)
        {
            if (label == null) return;
            var view = label.GetComponentInParent<ExpeditionBattleLayout>();
            if (view == null) return;
            var text = label.GetComponent<TMP_Text>();
            Put(text, view._content != null ? view._content : view.transform, .25f, .842f, .75f, .875f, 24, ExpeditionTheme.Amber);
            if (text != null) text.alignment = TextAlignmentOptions.Center;
        }

        private static void StyleOverlays(Transform canvas, Transform safeContent)
        {
            foreach (var search in canvas.GetComponentsInChildren<Match3SearchingPanel>(true))
            {
                search.UseExpeditionStyle();
            }
            foreach (var result in canvas.GetComponentsInChildren<Match3GameOverPanel>(true))
            {
                result.transform.SetParent(canvas, false);
                result.transform.SetAsLastSibling();
                result.UseExpeditionStyle();
            }
            foreach (var dialog in new[] { "QuitConfirmOverlay", "PveSelector" })
            {
                var root = canvas.Find(dialog);
                var card = root != null ? root.Find("Card") as RectTransform : null;
                if (card == null) continue;
                root.SetAsLastSibling();
                var safeDialog = ExpeditionUi.Rect(root, "ExpeditionDialogSafeArea", 0, 0, 1, 1);
                safeDialog.gameObject.AddComponent<ExpeditionSafeArea>();
                card.SetParent(safeDialog, false);
                ExpeditionUi.SetRect(card, .09f, .30f, .91f, .70f);
                var image = card.GetComponent<Image>();
                if (image != null) ExpeditionUi.StylePanel(image, ExpeditionTheme.Panel);
                foreach (var text in card.GetComponentsInChildren<TMP_Text>(true)) StyleText(text, 30, ExpeditionTheme.Ivory);
                foreach (var button in card.GetComponentsInChildren<Button>(true)) ExpeditionUi.StyleButton(button, button.name == "BtnStay" || button.name == "StartBoss", 28);
                if (dialog == "PveSelector") StylePveDossier(card);
            }
        }

        private static void StylePveDossier(RectTransform card)
        {
            var title = card.Find("Title")?.GetComponent<TMP_Text>();
            if (title != null) title.text = "РАЗВЕДКА / ПРОТИВНИК";
            Put(title, card, .06f, .86f, .94f, .97f, 26, ExpeditionTheme.Amber);
            Put(card.Find("BossName")?.GetComponent<TMP_Text>(), card, .34f, .66f, .94f, .83f, 34, ExpeditionTheme.Ivory);
            Put(card.Find("BossStats")?.GetComponent<TMP_Text>(), card, .34f, .285f, .94f, .65f, 27, ExpeditionTheme.Muted);
            foreach (var name in new[] { "PrevBoss", "NextBoss", "StartBoss" })
            {
                var button = card.Find(name) as RectTransform;
                if (button == null) continue;
                ExpeditionUi.SetRect(button, name == "PrevBoss" ? .06f : name == "NextBoss" ? .23f : .43f,
                    .065f, name == "PrevBoss" ? .21f : name == "NextBoss" ? .38f : .94f, .205f);
            }
            var error = card.Find("ErrorToast") as RectTransform;
            if (error != null) ExpeditionUi.SetRect(error, .055f, .215f, .945f, .28f);
        }

        public static void SetPveOpponentPreview(Transform selector, Sprite portrait)
        {
            if (selector == null) return;
            var card = ExpeditionUi.Find(selector, "Card") as RectTransform;
            if (card == null) return;
            var image = card.Find("ExpeditionOpponentPortrait")?.GetComponent<Image>();
            if (image == null)
            {
                image = ExpeditionUi.Panel(card, "ExpeditionOpponentPortrait", .055f, .305f, .30f, .82f, Color.white);
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }
            image.sprite = portrait;
            image.enabled = portrait != null;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_installed) ResizeBoard();
        }

        internal void ResizeBoard()
        {
            if (_resizing || _board == null || _board.cellContainer == null) return;
            _resizing = true;
            var boardRect = _board.transform as RectTransform;
            var frame = _board.transform.Find("Frame") as RectTransform;
            if (frame != null && boardRect != null)
            {
                var side = Mathf.Max(1f, Mathf.Min(boardRect.rect.width, boardRect.rect.height));
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
                frame.sizeDelta = Vector2.one * side;
                frame.anchoredPosition = Vector2.zero;
                PlaceTurnStatus(frame);
            }
            var grid = _board.cellContainer.GetComponent<GridLayoutGroup>();
            var rect = _board.cellContainer as RectTransform;
            if (grid != null && rect != null && rect.rect.width > 1)
            {
                var side = Mathf.Floor(Mathf.Min((rect.rect.width - grid.padding.horizontal - grid.spacing.x * 5f) / 6f,
                    (rect.rect.height - grid.padding.vertical - grid.spacing.y * 5f) / 6f));
                grid.cellSize = Vector2.one * Mathf.Max(1f, side);
            }
            _resizing = false;
        }

        private void PlaceTurnStatus(RectTransform frame)
        {
            if (_hud == null || _content == null || _content.rect.height < 1) return;
            var corners = new Vector3[4];
            frame.GetWorldCorners(corners);
            var top = _content.InverseTransformPoint(corners[1]).y - _content.rect.yMin;
            var height = _content.rect.height * .074f;
            var bottom = top + _content.rect.height * .006f;
            var status = _hud.transform as RectTransform;
            status.anchorMin = new Vector2(.20f, 0);
            status.anchorMax = new Vector2(.80f, 0);
            status.pivot = new Vector2(.5f, 0);
            status.sizeDelta = new Vector2(0, height);
            status.anchoredPosition = new Vector2(0, bottom);
            if (_hud.affixButton != null)
            {
                var affix = _hud.affixButton.transform as RectTransform;
                affix.anchorMin = new Vector2(.855f, 0);
                affix.anchorMax = new Vector2(.96f, 0);
                affix.pivot = new Vector2(.5f, 0);
                affix.sizeDelta = new Vector2(0, height * .72f);
                affix.anchoredPosition = new Vector2(0, bottom + height * .06f);
            }
        }

        private static void Put(TMP_Text text, Transform parent, float x0, float y0, float x1, float y1, float size, Color color)
        {
            if (text == null) return;
            text.transform.SetParent(parent, false);
            ExpeditionUi.SetRect(text.rectTransform, x0, y0, x1, y1);
            StyleText(text, size, color);
        }

        private static void StyleText(TMP_Text text, float size, Color color)
        {
            if (text == null) return;
            text.font = ExpeditionTheme.Font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.margin = new Vector4(3, 0, 3, 0);
        }
    }

    /// <summary>Safe-area changes are layout events; combat frames never poll or rebuild geometry.</summary>
    internal sealed class ExpeditionBattleViewport : MonoBehaviour
    {
        public ExpeditionBattleLayout owner;
        private void OnRectTransformDimensionsChange() { if (owner != null) owner.ResizeBoard(); }
    }
}
