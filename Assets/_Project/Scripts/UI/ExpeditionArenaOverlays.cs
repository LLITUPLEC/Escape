using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Round identity prevents a historical slot from using the current round's bet callback.</summary>
    public sealed class ExpeditionArenaPairTag : MonoBehaviour
    {
        public string Round;
        public int Slot;
        public bool Current;
        public bool Highlight;
        public string UidA, UidB, WinnerUid;
    }

    public sealed class ExpeditionArenaConnectorTag : MonoBehaviour
    {
        public string SourceRound, TargetRound, WinnerUid;
        public int SourceSlot, TargetSlot;
    }

    /// <summary>A real checkbox tick without a font glyph or baked art.</summary>
    [RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
    public sealed class ExpeditionArenaCheckGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var width = Mathf.Min(rect.width, rect.height) * .16f;
            AddStroke(mesh, rect, new Vector2(.10f, .49f), new Vector2(.39f, .19f), width);
            AddStroke(mesh, rect, new Vector2(.39f, .19f), new Vector2(.91f, .84f), width);
        }
        private void AddStroke(VertexHelper mesh, Rect rect, Vector2 from, Vector2 to, float width)
        {
            var a = new Vector2(rect.xMin + rect.width * from.x, rect.yMin + rect.height * from.y);
            var b = new Vector2(rect.xMin + rect.width * to.x, rect.yMin + rect.height * to.y);
            var delta = (b - a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * width * .5f;
            var points = new[] { a - normal, a + normal, b + normal, b - normal };
            var quad = new UIVertex[4];
            for (var i = 0; i < 4; i++) { quad[i] = UIVertex.simpleVert; quad[i].position = points[i]; quad[i].color = color; }
            mesh.AddUIVertexQuad(quad);
        }
    }

    /// <summary>Presentation only: tournament/race controllers retain all values, RPCs and selection callbacks.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionArenaOverlays : MonoBehaviour
    {
        private bool _built;
        private RectTransform _panel;
        private RectTransform _costRow;
        private RectTransform _rewardRow;
        private Toggle _betA;
        private Toggle _betB;
        private TMP_Text _betALabel;
        private TMP_Text _betBLabel;
        private GameObject _bracketBlocker;
        private bool _isBracket;

        private static ExpeditionArenaOverlays Ensure(Transform root)
        {
            return root != null ? ExpeditionUi.Ensure<ExpeditionArenaOverlays>(root.gameObject) : null;
        }

        public static void ApplyStakeChooser(Transform root, TMP_Text title, Transform close)
        {
            var view = Ensure(root);
            if (view == null) return;
            view._panel = root.Find("Panel") as RectTransform;
            if (view._panel == null) return;
            var panel = view._panel;
            DisableLayout(panel);
            SafeRect(panel, .05f, .135f, .95f, .865f);
            ExpeditionUi.StylePanel(panel.GetComponent<Image>(), ExpeditionTheme.Panel);
            if (title != null)
            {
                title.gameObject.SetActive(true);
                ExpeditionUi.SetRect(title.rectTransform, .055f, .89f, .945f, .975f);
                StyleText(title, 39, ExpeditionTheme.Ivory);
            }
            if (!view._built)
            {
                var caption = ExpeditionUi.Text(panel, "ExpeditionStakeHint", "ВЫБЕРИТЕ ВЗНОС ДЛЯ УЧАСТИЯ", 24, .06f, .845f, .94f, .885f, TextAlignmentOptions.Center);
                caption.color = ExpeditionTheme.Muted;
                view._built = true;
            }
            if (close != null)
            {
                ExpeditionUi.SetRect(close as RectTransform, .06f, .025f, .94f, .1f);
                ExpeditionUi.StyleButton(close.GetComponent<Button>(), false, 34);
            }
            var rows = new List<RectTransform>();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.name.StartsWith("Row_", System.StringComparison.Ordinal) && child.GetComponent<Button>() != null)
                    rows.Add(child as RectTransform);
            }
            var stride = .705f / Mathf.Max(1, rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                DisableLayout(row);
                var top = .825f - i * stride;
                ExpeditionUi.SetRect(row, .045f, top - stride + .016f, .955f, top);
                ExpeditionUi.StyleButton(row.GetComponent<Button>(), false, 38);
                var rowButton = row.GetComponent<Button>();
                if (rowButton != null)
                {
                    var colors = rowButton.colors;
                    colors.normalColor = colors.selectedColor = ExpeditionTheme.Teal;
                    rowButton.colors = colors;
                }
                Place(row, "ResIcon", .03f, .25f, .185f, .77f);
                var icon = row.Find("ResIcon")?.GetComponent<Image>();
                if (icon != null) { icon.preserveAspect = true; icon.raycastTarget = false; }
                var cost = row.Find("CostCol") as RectTransform;
                if (cost != null)
                {
                    DisableLayout(cost);
                    ExpeditionUi.SetRect(cost, .2f, .17f, .43f, .74f);
                    foreach (var value in cost.GetComponentsInChildren<TMP_Text>(true))
                    { StyleText(value, 40, ExpeditionTheme.Ivory); ExpeditionUi.SetRect(value.rectTransform, 0, 0, 1, 1); }
                }
                var prize = row.Find("PrizeCol") as RectTransform;
                if (prize != null)
                {
                    ExpeditionUi.SetRect(prize, .49f, .19f, .965f, .73f);
                    var line = prize.GetComponent<HorizontalLayoutGroup>();
                    if (line != null)
                    {
                        line.childControlWidth = true;
                        line.childControlHeight = true;
                        line.childForceExpandHeight = false;
                        line.childForceExpandWidth = false;
                        line.spacing = 9;
                        line.childAlignment = TextAnchor.MiddleCenter;
                    }
                    foreach (var resource in prize.GetComponentsInChildren<Image>(true))
                    {
                        resource.preserveAspect = true;
                        resource.raycastTarget = false;
                        var le = ExpeditionUi.Ensure<LayoutElement>(resource.gameObject);
                        le.preferredWidth = le.minWidth = 44;
                        le.preferredHeight = le.minHeight = 44;
                    }
                    foreach (var value in prize.GetComponentsInChildren<TMP_Text>(true))
                    {
                        StyleText(value, 35, ExpeditionTheme.Amber);
                        var le = ExpeditionUi.Ensure<LayoutElement>(value.gameObject);
                        le.flexibleWidth = 1;
                        le.minWidth = 55;
                        le.preferredHeight = 72;
                    }
                }
                var costCaption = ExpeditionUi.Text(row, "ExpeditionStakeCost", "ВЗНОС", 22, .2f, .73f, .43f, .94f, TextAlignmentOptions.Center);
                costCaption.color = ExpeditionTheme.Muted;
                var prizeCaption = ExpeditionUi.Text(row, "ExpeditionStakePrize", "НАГРАДА ПОБЕДИТЕЛЮ", 22, .49f, .73f, .965f, .94f, TextAlignmentOptions.Center);
                prizeCaption.color = ExpeditionTheme.Muted;
                var arrow = ExpeditionUi.Text(row, "ExpeditionStakeArrow", "→", 40, .42f, .2f, .49f, .72f, TextAlignmentOptions.Center);
                arrow.color = ExpeditionTheme.Muted;
            }
        }

        public static void ApplyBracket(Transform root)
        {
            var view = Ensure(root);
            if (view == null) return;
            view._isBracket = true;
            var rt = root as RectTransform;
            DisableLayout(rt);
            SafeRect(rt, .025f, .12f, .975f, .89f);
            var background = ExpeditionUi.Ensure<Image>(root.gameObject);
            background.sprite = null;
            background.color = Color.clear;
            background.raycastTarget = true;
            if (!view._built)
            {
                var backdrop = ExpeditionUi.Fill(root, "ExpeditionBracketBackdrop", 0, 0, 1, 1, Color.white);
                backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/arena-backdrop");
                backdrop.type = Image.Type.Simple;
                backdrop.raycastTarget = false;
                backdrop.enabled = false; // The arena environment already fills the physical screen.
                backdrop.transform.SetAsFirstSibling();
                var shade = ExpeditionUi.Fill(root, "ExpeditionBracketShade", 0, 0, 1, 1, new Color(.015f, .035f, .045f, .30f));
                shade.raycastTarget = false;
                shade.enabled = false;
                shade.transform.SetSiblingIndex(1);
                var footerPanel = ExpeditionUi.Panel(root, "ExpeditionTournamentFooter", .025f, .015f, .975f, .205f, ExpeditionTheme.Panel);
                footerPanel.raycastTarget = false;
                var portrait = ExpeditionUi.Fill(footerPanel.transform, "Portrait", .025f, .12f, .17f, .88f, Color.white);
                portrait.sprite = ExpeditionTheme.Icon("hero"); portrait.preserveAspect = true; portrait.raycastTarget = false;
                ExpeditionUi.Text(root, "ExpeditionTournamentSummary", "Ожидаем участников", 26, .20f, .095f, .94f, .19f, TextAlignmentOptions.MidlineLeft);
                var countdownPlate = ExpeditionUi.Fill(root, "ExpeditionCountdownPlate", .055f, .855f, .945f, .937f, new Color32(8, 23, 27, 250));
                countdownPlate.raycastTarget = false;
                if (root.parent != null)
                {
                    var blocker = ExpeditionUi.Fill(root.parent, "ExpeditionBracketInputBlocker", 0, 0, 1, 1, new Color(0, 0, 0, .15f));
                    blocker.raycastTarget = true;
                    view._bracketBlocker = blocker.gameObject;
                    blocker.transform.SetSiblingIndex(root.GetSiblingIndex());
                }
                view._built = true;
            }
            Place(root, "Title", .04f, .947f, .96f, .997f);
            var title = root.Find("Title")?.GetComponent<TMP_Text>();
            if (title != null) StyleText(title, 44, ExpeditionTheme.Ivory);
            Place(root, "ArenaCd", .07f, .862f, .93f, .930f);
            var countdown = root.Find("ArenaCd")?.GetComponent<TMP_Text>();
            if (countdown != null)
            {
                StyleText(countdown, 56, ExpeditionTheme.Amber);
                countdown.fontStyle = FontStyles.Bold;
                countdown.fontSizeMin = 48;
                countdown.margin = Vector4.zero;
                countdown.transform.SetAsLastSibling();
            }
            Place(root, "ArenaRows", .03f, .245f, .97f, .80f);
            var rows = root.Find("ArenaRows");
            if (rows != null)
            {
                DisableLayout(rows as RectTransform);
            }
            Place(root, "PrizeFooter", .20f, .025f, .94f, .095f);
            var footer = root.Find("PrizeFooter")?.GetComponent<TMP_Text>();
            if (footer != null)
            {
                StyleText(footer, 20, ExpeditionTheme.Ivory);
                footer.transform.SetAsLastSibling();
            }
            ExpeditionPresentation.SetTournamentVisible(root.gameObject.activeInHierarchy);
        }

        public static void ApplyPairCard(Transform row)
        {
            var view = Ensure(row);
            if (view == null) return;
            if (!view._built) view.BuildPairCard();
            view.RefreshBets();
        }

        private void BuildPairCard()
        {
            var rt = transform as RectTransform;
            DisableLayout(rt);
            var le = ExpeditionUi.Ensure<LayoutElement>(gameObject);
            le.preferredHeight = le.minHeight = 180;
            le.flexibleHeight = 0;
            var background = ExpeditionUi.Ensure<Image>(gameObject);
            ExpeditionUi.StylePanel(background, new Color32(29, 65, 72, 255));
            background.raycastTarget = false;
            var left = EnsurePairText("LeftName");
            var right = EnsurePairText("RightName");
            var status = EnsurePairText("Status");
            var vs = EnsurePairText("Vs");
            vs.gameObject.SetActive(false);
            Move(left.rectTransform, rt, .245f, .74f, .95f, .95f);
            Move(right.rectTransform, rt, .245f, .26f, .95f, .47f);
            Move(status.rectTransform, rt, .245f, .465f, .94f, .535f);
            StyleText(left, 24, ExpeditionTheme.Ivory);
            StyleText(right, 24, ExpeditionTheme.Ivory);
            left.alignment = right.alignment = TextAlignmentOptions.MidlineLeft;
            left.textWrappingMode = right.textWrappingMode = TextWrappingModes.NoWrap;
            left.fontSizeMin = right.fontSizeMin = 17;
            left.overflowMode = right.overflowMode = TextOverflowModes.Ellipsis;
            StyleText(status, 12, ExpeditionTheme.Muted);
            BuildPairHead("HeadA", .54f, .96f);
            BuildPairHead("HeadB", .045f, .465f);
            var hpA = EnsureHp("HpA");
            var hpB = EnsureHp("HpB");
            Move(hpA, rt, .25f, .65f, .92f, .695f);
            Move(hpB, rt, .25f, .17f, .92f, .215f);
            StyleHp(hpA, new Color32(158, 71, 56, 255));
            StyleHp(hpB, new Color32(158, 71, 56, 255));
            ExpeditionUi.Text(rt, "HpStatusA", "HP —", 13, .245f, .535f, .90f, .65f, TextAlignmentOptions.MidlineLeft).color = ExpeditionTheme.Muted;
            ExpeditionUi.Text(rt, "HpStatusB", "HP —", 13, .245f, .055f, .90f, .17f, TextAlignmentOptions.MidlineLeft).color = ExpeditionTheme.Muted;
            _betA = EnsureBet("BetA");
            _betB = EnsureBet("BetB");
            Move(_betA.transform as RectTransform, rt, .84f, .74f, .985f, .95f);
            Move(_betB.transform as RectTransform, rt, .84f, .255f, .985f, .465f);
            _betALabel = BuildBetVisual(_betA);
            _betBLabel = BuildBetVisual(_betB);
            _betA.onValueChanged.AddListener(OnBetVisualChanged);
            _betB.onValueChanged.AddListener(OnBetVisualChanged);
            foreach (var name in new[] { "Left_div", "Middle_div", "Right_div" })
            {
                var old = transform.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }
            _built = true;
        }

        private void BuildPairHead(string name, float y0, float y1)
        {
            var frame = ExpeditionUi.Panel(transform, name + "Frame", .025f, y0, .22f, y1, ExpeditionTheme.Panel);
            frame.raycastTarget = false;
            frame.pixelsPerUnitMultiplier = 7;
            var head = ExpeditionUi.Fill(frame.transform, name, .06f, .06f, .94f, .94f, Color.white);
            head.sprite = ExpeditionTheme.Icon("hero");
            head.preserveAspect = true;
            head.raycastTarget = false;
            var badge = ExpeditionUi.Fill(frame.transform, "ResultBadge", .5f, .5f, .5f, .5f, new Color32(7, 22, 24, 245));
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(.96f, .06f);
            badge.rectTransform.pivot = new Vector2(1, 0);
            badge.rectTransform.sizeDelta = new Vector2(28, 28);
            badge.rectTransform.anchoredPosition = Vector2.zero;
            badge.raycastTarget = false;
            var rim = badge.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color32(184, 205, 183, 255); rim.effectDistance = new Vector2(1, 1);
            var tick = ExpeditionUi.Rect(badge.transform, "WinnerTick", 0, 0, 1, 1);
            BadgeStroke(tick, "Short", new Vector2(.32f, .43f), new Vector2(10, 4), -45, new Color32(107, 227, 133, 255));
            BadgeStroke(tick, "Long", new Vector2(.62f, .52f), new Vector2(17, 4), 45, new Color32(107, 227, 133, 255));
            var cross = ExpeditionUi.Rect(badge.transform, "LoserCross", 0, 0, 1, 1);
            BadgeStroke(cross, "Slash", Vector2.one * .5f, new Vector2(18, 4), 45, new Color32(224, 111, 93, 255));
            BadgeStroke(cross, "Backslash", Vector2.one * .5f, new Vector2(18, 4), -45, new Color32(224, 111, 93, 255));
            badge.gameObject.SetActive(false);
        }

        private static void BadgeStroke(Transform parent, string name, Vector2 anchor, Vector2 size, float angle, Color color)
        {
            var image = ExpeditionUi.Fill(parent, name, 0, 0, 1, 1, color);
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0, 0, angle);
        }

        public static void UpdatePair(Transform row, string uidA, string uidB, string winner, string status, bool ownPair)
        {
            var tag = row.GetComponent<ExpeditionArenaPairTag>();
            if (tag != null)
            {
                tag.Highlight = ownPair || !string.IsNullOrEmpty(winner);
                tag.UidA = uidA; tag.UidB = uidB; tag.WinnerUid = winner;
            }
            foreach (var side in new[] { "A", "B" })
            {
                var won = !string.IsNullOrEmpty(winner) && winner == (side == "A" ? uidA : uidB);
                var name = ExpeditionUi.Find(row, side == "A" ? "LeftName" : "RightName")?.GetComponent<TMP_Text>();
                if (name != null) name.color = won ? ExpeditionTheme.Amber : status == "done" ? ExpeditionTheme.Muted : ExpeditionTheme.Ivory;
                var frame = row.Find("Head" + side + "Frame")?.GetComponent<Image>();
                if (frame != null) frame.color = won ? new Color(1, .82f, .38f, 1) : Color.white;
                var complete = status == "done" && !string.IsNullOrEmpty(winner);
                if (frame != null)
                {
                    var portrait = frame.transform.Find("Head" + side)?.GetComponent<Image>();
                    if (portrait != null) portrait.color = complete && !won ? new Color(.40f, .48f, .48f, 1) : Color.white;
                    var badge = frame.transform.Find("ResultBadge");
                    if (badge != null)
                    {
                        badge.gameObject.SetActive(complete);
                        badge.Find("WinnerTick").gameObject.SetActive(won);
                        badge.Find("LoserCross").gameObject.SetActive(!won);
                    }
                }
            }
            var label = ExpeditionUi.Find(row, "Status")?.GetComponent<TMP_Text>();
            if (label != null) label.text = status == "done" ? "ЗАВЕРШЁН" : status == "fighting" ? "БОЙ" : "";
        }

        public static void BuildBracketConnections(Transform rows)
        {
            foreach (var title in rows.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!title.name.StartsWith("RoundLabel_", System.StringComparison.Ordinal)) continue;
                var rt = title.rectTransform;
                var plate = ExpeditionUi.Fill(rows, title.name + "Plate", rt.anchorMin.x, rt.anchorMin.y - .008f, rt.anchorMax.x, rt.anchorMax.y + .008f, new Color(.015f, .035f, .04f, .97f));
                plate.raycastTarget = false;
                plate.transform.SetSiblingIndex(title.transform.GetSiblingIndex());
                ExpeditionUi.Fill(plate.transform, "AmberRule", .03f, .91f, .97f, .975f, ExpeditionTheme.Amber).raycastTarget = false;
                title.color = ExpeditionTheme.Ivory;
                title.fontStyle = FontStyles.Bold;
                title.fontSize = title.fontSizeMax = 28; title.fontSizeMin = 24;
                title.enableAutoSizing = true; title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Ellipsis;
            }
            var tags = rows.GetComponentsInChildren<ExpeditionArenaPairTag>(true);
            ArrangeHistoricalQuarters(tags);
            var buses = new Dictionary<string, List<ExpeditionArenaPairTag>>();
            foreach (var left in tags)
            {
                // Old columns are disabled before deferred Destroy; never connect a stale round.
                if (!left.gameObject.activeInHierarchy || (left.Round != "qf" && left.Round != "sf")) continue;
                var y = PairCenter(left);
                var nextRound = left.Round == "qf" ? "sf" : "final";
                var target = FindDestination(tags, left);
                // Later rounds are shuffled by the real server. Draw the actual
                // winner route; an unformed next round remains a neutral guide.
                var targetSlot = target != null ? target.Slot : left.Round == "qf" ? (left.Slot + 1) / 2 : 1;
                var from = left.Round == "qf" ? .39f : .71f;
                var to = left.Round == "qf" ? .46f : .78f;
                var joint = (from + to) * .5f;
                var color = target != null ? new Color(1, .75f, .31f, 1) : new Color(.62f, .48f, .27f, .9f);
                var route = ExpeditionUi.Rect(rows, "BracketRoute_" + left.Round + "_" + left.Slot, 0, 0, 1, 1);
                var proof = route.gameObject.AddComponent<ExpeditionArenaConnectorTag>();
                proof.SourceRound = left.Round; proof.SourceSlot = left.Slot;
                proof.TargetRound = nextRound; proof.TargetSlot = targetSlot;
                proof.WinnerUid = target != null ? left.WinnerUid : "";
                // Each source has one branch. A shared destination trunk below is
                // drawn once, so coincident opaque outlines cannot cover the line.
                FlowLine(route, from, y - .0036f, joint, y + .0036f, color);
                var key = nextRound + "_" + targetSlot;
                if (!buses.TryGetValue(key, out var sources)) { sources = new List<ExpeditionArenaPairTag>(); buses.Add(key, sources); }
                sources.Add(left);
            }
            foreach (var bus in buses)
            {
                var first = bus.Value[0];
                var target = FindDestination(tags, first);
                var targetSlot = target != null ? target.Slot : first.Round == "qf" ? (first.Slot + 1) / 2 : 1;
                var targetY = target != null ? PairCenter(target) : first.Round == "qf" ? .75f - (targetSlot - 1) * .5f : .5f;
                var from = first.Round == "qf" ? .39f : .71f;
                var to = first.Round == "qf" ? .46f : .78f;
                var joint = (from + to) * .5f;
                var low = targetY; var high = targetY;
                foreach (var source in bus.Value) { low = Mathf.Min(low, PairCenter(source)); high = Mathf.Max(high, PairCenter(source)); }
                var color = target != null ? new Color(1, .75f, .31f, 1) : new Color(.62f, .48f, .27f, .9f);
                var trunk = ExpeditionUi.Rect(rows, "BracketBus_" + bus.Key, 0, 0, 1, 1);
                if (high - low > .001f) FlowLine(trunk, joint - .0028f, low, joint + .0028f, high, color);
                FlowLine(trunk, joint, targetY - .0036f, to, targetY + .0036f, color);
            }
            var trophy = ExpeditionUi.Fill(rows, "FinalTrophy", .825f, .68f, .96f, .84f, Color.white);
            trophy.sprite = ExpeditionTheme.Icon("trophy"); trophy.preserveAspect = true; trophy.raycastTarget = false;
        }

        private static float PairCenter(ExpeditionArenaPairTag pair)
        {
            var rt = pair.transform as RectTransform;
            return (rt.anchorMin.y + rt.anchorMax.y) * .5f;
        }

        private static ExpeditionArenaPairTag FindDestination(ExpeditionArenaPairTag[] tags, ExpeditionArenaPairTag source)
        {
            if (string.IsNullOrEmpty(source.WinnerUid)) return null;
            var round = source.Round == "qf" ? "sf" : "final";
            foreach (var candidate in tags)
                if (candidate.gameObject.activeInHierarchy && candidate.Round == round
                    && (candidate.UidA == source.WinnerUid || candidate.UidB == source.WinnerUid)) return candidate;
            return null;
        }

        private static void ArrangeHistoricalQuarters(ExpeditionArenaPairTag[] tags)
        {
            // The server shuffles semifinal entrants. Group the historical cards
            // by their REAL destination for a readable tree; never renumber slots
            // or change the UID/callback identity used by authoritative bets.
            var quarters = new List<ExpeditionArenaPairTag>();
            foreach (var tag in tags)
                if (tag.gameObject.activeInHierarchy && tag.Round == "qf") quarters.Add(tag);
            if (quarters.Count != 4) return;
            foreach (var quarter in quarters) if (FindDestination(tags, quarter) == null) return;
            quarters.Sort((a, b) =>
            {
                var destination = FindDestination(tags, a).Slot.CompareTo(FindDestination(tags, b).Slot);
                return destination != 0 ? destination : a.Slot.CompareTo(b.Slot);
            });
            for (var i = 0; i < quarters.Count; i++)
            {
                var rt = quarters[i].transform as RectTransform;
                var center = .875f - i * .25f;
                ExpeditionUi.SetRect(rt, rt.anchorMin.x, center - .105f, rt.anchorMax.x, center + .105f);
            }
        }

        private static void FlowLine(Transform root, float x0, float y0, float x1, float y1, Color color)
        {
            var line = ExpeditionUi.Fill(root, "BracketConnector", x0, y0, x1, y1, color);
            line.raycastTarget = false;
            var outline = line.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.005f, .02f, .025f, .95f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);
        }

        private TMP_Text EnsurePairText(string name)
        {
            var existing = ExpeditionUi.Find(transform, name)?.GetComponent<TMP_Text>();
            return existing != null ? existing : ExpeditionUi.Text(transform, name, "", 30, 0, 0, 1, 1, TextAlignmentOptions.Center);
        }

        private RectTransform EnsureHp(string name)
        {
            var existing = ExpeditionUi.Find(transform, name) as RectTransform;
            if (existing != null) return existing;
            var bg = ExpeditionUi.Panel(transform, name, 0, 0, 1, 1, ExpeditionTheme.Panel);
            var fill = ExpeditionUi.Panel(bg.transform, "Fill", 0, 0, 1, 1, ExpeditionTheme.Amber);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0;
            return bg.rectTransform;
        }

        private Toggle EnsureBet(string name)
        {
            var existing = ExpeditionUi.Find(transform, name)?.GetComponent<Toggle>();
            if (existing != null) return existing;
            var rt = ExpeditionUi.Rect(transform, name, 0, 0, 1, 1);
            var toggle = rt.gameObject.AddComponent<Toggle>();
            var group = ExpeditionUi.Ensure<ToggleGroup>(gameObject);
            group.allowSwitchOff = true;
            toggle.group = group;
            toggle.isOn = false;
            return toggle;
        }

        private static void StyleHp(RectTransform hp, Color fillColor)
        {
            var background = hp.GetComponent<Image>();
            if (background != null) { background.sprite = null; background.color = new Color(.035f, .07f, .08f, 1); background.raycastTarget = false; }
            var fill = hp.Find("Fill")?.GetComponent<Image>();
            if (fill != null) { fill.color = fillColor; fill.raycastTarget = false; }
        }

        private static TMP_Text BuildBetVisual(Toggle toggle)
        {
            var background = ExpeditionUi.Ensure<Image>(toggle.gameObject);
            background.enabled = true;
            background.sprite = background.overrideSprite = null;
            background.type = Image.Type.Simple;
            background.color = new Color32(13, 32, 36, 255);
            background.raycastTarget = true;
            background.canvasRenderer.cullTransparentMesh = false;
            var rim = ExpeditionUi.Ensure<Outline>(toggle.gameObject);
            rim.effectColor = ExpeditionTheme.Ivory; rim.effectDistance = new Vector2(1.5f, 1.5f);
            toggle.targetGraphic = background;
            toggle.transition = Selectable.Transition.None;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            for (var i = 0; i < toggle.transform.childCount; i++) toggle.transform.GetChild(i).gameObject.SetActive(false);
            var markRoot = ExpeditionUi.Rect(toggle.transform, "ExpeditionBetCheck", .15f, .13f, .85f, .87f);
            ExpeditionUi.Ensure<CanvasRenderer>(markRoot.gameObject);
            var mark = markRoot.gameObject.AddComponent<ExpeditionArenaCheckGraphic>();
            mark.color = new Color32(124, 222, 144, 255); mark.raycastTarget = false;
            toggle.graphic = mark;
            // Keep the old view's private caption reference, but never rely on a font checkmark.
            var caption = ExpeditionUi.Text(toggle.transform, "ExpeditionBetCaption", "", 22, 0, 0, 1, 1);
            caption.gameObject.SetActive(false);
            return caption;
        }

        public static void SetBetControlsVisible(Transform row, bool visible)
        {
            var left = ExpeditionUi.Find(row, "LeftName") as RectTransform;
            var right = ExpeditionUi.Find(row, "RightName") as RectTransform;
            if (left != null) ExpeditionUi.SetRect(left, .245f, .74f, visible ? .815f : .95f, .95f);
            if (right != null) ExpeditionUi.SetRect(right, .245f, .26f, visible ? .815f : .95f, .47f);
        }

        private void OnBetVisualChanged(bool _) { RefreshBets(); }

        private void RefreshBets()
        {
            RefreshBet(_betA, _betALabel);
            RefreshBet(_betB, _betBLabel);
        }

        private static void RefreshBet(Toggle toggle, TMP_Text label)
        {
            if (toggle == null) return;
            var image = toggle.targetGraphic as Image;
            if (image != null)
            {
                image.enabled = true;
                image.color = toggle.isOn ? new Color32(31, 68, 49, 255) : new Color32(13, 32, 36, 255);
                var rim = image.GetComponent<Outline>();
                if (rim != null) rim.effectColor = toggle.isOn ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
            }
            if (toggle.graphic != null) toggle.graphic.canvasRenderer.SetAlpha(toggle.isOn ? 1 : 0);
            if (label != null) label.text = "";
        }

        private void OnDestroy()
        {
            if (_isBracket) ExpeditionPresentation.SetTournamentVisible(false);
            if (_bracketBlocker != null) Destroy(_bracketBlocker);
            if (_betA != null) _betA.onValueChanged.RemoveListener(OnBetVisualChanged);
            if (_betB != null) _betB.onValueChanged.RemoveListener(OnBetVisualChanged);
        }

        private void OnEnable()
        {
            if (_bracketBlocker != null) _bracketBlocker.SetActive(true);
            if (_isBracket) ExpeditionPresentation.SetTournamentVisible(true);
        }
        private void OnDisable()
        {
            if (_bracketBlocker != null) _bracketBlocker.SetActive(false);
            if (_isBracket) ExpeditionPresentation.SetTournamentVisible(false);
        }

        public static void ApplyRace(Transform root)
        {
            var view = Ensure(root);
            if (view == null) return;
            if (!view._built) view.BuildRace();
            if (view._panel == null) return;
            SafeRect(view._panel, .05f, .105f, .95f, .895f);
            ApplyResourceRow(view._costRow);
            ApplyResourceRow(view._rewardRow);
        }

        private void BuildRace()
        {
            _panel = transform.Find("Panel") as RectTransform;
            if (_panel == null) return;
            DisableLayout(_panel);
            ExpeditionUi.StylePanel(_panel.GetComponent<Image>(), ExpeditionTheme.Panel);
            ExpeditionUi.Text(_panel, "ExpeditionRaceTitle", "СПУСК В РАЗЛОМ", 43, .06f, .925f, .94f, .985f, TextAlignmentOptions.Center);
            Place(_panel, "Question", .06f, .84f, .94f, .915f);
            Place(_panel, "CostCaption", .06f, .765f, .94f, .82f);
            Place(_panel, "CostRow", .06f, .62f, .94f, .76f);
            Place(_panel, "RewardCaption", .06f, .56f, .94f, .615f);
            Place(_panel, "RewardRow", .06f, .305f, .94f, .55f);
            Place(_panel, "GoalText", .06f, .225f, .94f, .29f);
            Place(_panel, "Error", .06f, .14f, .94f, .215f);
            Place(_panel, "YesNo", .06f, .045f, .94f, .125f);
            foreach (var label in _panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name == "ExpeditionRaceTitle") continue;
                StyleText(label, label.name == "Question" ? 34 : label.name == "GoalText" ? 32 : 29,
                    label.name == "Error" ? ExpeditionTheme.Coral : label.name.EndsWith("Caption") ? ExpeditionTheme.Muted : ExpeditionTheme.Ivory);
            }
            var yesNo = _panel.Find("YesNo");
            if (yesNo != null)
            {
                var buttons = yesNo.GetComponentsInChildren<Button>(true);
                for (var i = 0; i < buttons.Length; i++)
                {
                    ExpeditionUi.StyleButton(buttons[i], i == 0, 34);
                    ExpeditionUi.Label(buttons[i], i == 0 ? "Начать спуск" : "Отмена");
                }
            }
            _costRow = _panel.Find("CostRow") as RectTransform;
            _rewardRow = _panel.Find("RewardRow") as RectTransform;
            _built = true;
        }

        private static void ApplyResourceRow(RectTransform row)
        {
            if (row == null) return;
            var originalLayout = row.GetComponent<HorizontalLayoutGroup>();
            if (originalLayout != null) originalLayout.enabled = false;
            var children = new List<RectTransform>();
            for (var i = 0; i < row.childCount; i++)
                if (row.GetChild(i).gameObject.activeSelf && row.GetChild(i) is RectTransform child) children.Add(child);
            var columns = Mathf.Clamp(children.Count, 1, 5);
            var rows = Mathf.Max(1, Mathf.CeilToInt(children.Count / (float)columns));
            var cellWidth = Mathf.Min(148, (Mathf.Max(1, row.rect.width) - (columns - 1) * 14) / columns);
            const float cellHeight = 140;
            for (var i = 0; i < children.Count; i++)
            {
                var rt = children[i];
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.pivot = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(cellWidth, cellHeight);
                var itemsInRow = Mathf.Min(columns, children.Count - (i / columns) * columns);
                var rowWidth = itemsInRow * cellWidth + (itemsInRow - 1) * 14;
                rt.anchoredPosition = new Vector2(-rowWidth * .5f + cellWidth * .5f + (i % columns) * (cellWidth + 14),
                    (rows * cellHeight + (rows - 1) * 14) * .5f - cellHeight * .5f - (i / columns) * (cellHeight + 14));
            }
            foreach (var image in row.GetComponentsInChildren<Image>(false))
            {
                if (image.name.StartsWith("Chip_", System.StringComparison.Ordinal)) ExpeditionUi.StylePanel(image, ExpeditionTheme.Teal);
                if (image.name == "Icon")
                {
                    var le = image.GetComponent<LayoutElement>();
                    if (le != null) { le.preferredHeight = le.minHeight = 62; }
                }
            }
            foreach (var label in row.GetComponentsInChildren<TMP_Text>(false))
            {
                StyleText(label, 34, ExpeditionTheme.Ivory);
                var le = label.GetComponent<LayoutElement>();
                if (le != null) { le.preferredHeight = 48; le.minHeight = 40; }
            }
        }

        public static void ApplyQueue(TMP_Text text, Button leave)
        {
            if (text != null) StyleText(text, 42, ExpeditionTheme.Ivory);
            if (leave == null) return;
            ExpeditionUi.StyleButton(leave, false, 34);
            SafeRect(leave.transform as RectTransform, .24f, .075f, .76f, .13f);
        }

        public static void ApplyToast(Transform root, TMP_Text text)
        {
            if (root == null) return;
            Ensure(root);
            SafeRect(root as RectTransform, .07f, .79f, .93f, .86f);
            ExpeditionUi.StylePanel(root.GetComponent<Image>(), ExpeditionTheme.Panel);
            if (text != null) StyleText(text, 32, ExpeditionTheme.Ivory);
        }

        private static void StyleText(TMP_Text text, float size, Color color)
        {
            text.font = ExpeditionTheme.Font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .76f;
            text.fontSizeMax = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void DisableLayout(RectTransform rt)
        {
            if (rt == null) return;
            var layout = rt.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = rt.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private static void Place(Transform root, string name, float x0, float y0, float x1, float y1)
        {
            var rt = root.Find(name) as RectTransform;
            if (rt != null) ExpeditionUi.SetRect(rt, x0, y0, x1, y1);
        }

        private static void Move(RectTransform rt, RectTransform parent, float x0, float y0, float x1, float y1)
        {
            rt.SetParent(parent, false);
            ExpeditionUi.SetRect(rt, x0, y0, x1, y1);
            var position = rt.localPosition;
            position.z = 0;
            rt.localPosition = position;
        }

        private static void SafeRect(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            var safe = Screen.safeArea;
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0 ||
                safe.xMax > width + 1 || safe.yMax > height + 1)
                safe = new Rect(0, 0, width, height);
            ExpeditionUi.SetRect(rt, (safe.xMin + safe.width * x0) / width, (safe.yMin + safe.height * y0) / height,
                (safe.xMin + safe.width * x1) / width, (safe.yMin + safe.height * y1) / height);
        }
    }
}
