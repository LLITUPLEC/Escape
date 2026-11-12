using System;
using System.Collections.Generic;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    [DisallowMultipleComponent]
    public sealed class ExpeditionAchievementsLayout : MonoBehaviour
    {
        public static readonly Color ConditionColor = new Color32(169, 206, 172, 255);
        private static readonly Color JournalSurface = new Color32(34, 25, 20, 248);
        private readonly Dictionary<AchievementTab, JournalTab> _tabs = new();
        private RectTransform _sheet;
        private ScrollRect _scroll;
        private TMP_Text _empty;
        private TMP_Text _collection;
        private Image _backdrop;
        private ExpeditionPanelArrival _pageArrival;
        private bool _inShell, _geometryDirty;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;

        public RectTransform Content => _scroll != null ? _scroll.content : null;
        public float ScrollPosition => _scroll != null ? _scroll.verticalNormalizedPosition : 1f;

        private sealed class JournalTab
        {
            public Button Button;
            public TMP_Text Label;
            public Image Emblem;
            public ExpeditionGoldCountBadge Badge;
        }

        public void Build(RectTransform sheet, Action<AchievementTab> selectTab)
        {
            if (_sheet != null || sheet == null) return;
            _sheet = sheet;
            DisableLayout(transform as RectTransform);
            DisableLayout(sheet);
            var image = sheet.GetComponent<Image>();
            if (image != null) image.enabled = false;
            var outline = sheet.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            foreach (var name in new[] { "Header", "TabBar", "Pages", "ExpeditionOpaqueFill" })
            {
                var old = sheet.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }
            var dimmer = transform.Find("AchievementsDimmer");
            if (dimmer != null) dimmer.gameObject.SetActive(false);

            ExpeditionUi.SetRect(sheet, 0, 0, 1, 1);
            _backdrop = ExpeditionUi.Fill(sheet, "JournalBackdrop", 0, 0, 1, 1, Color.white);
            _backdrop.sprite = Resources.Load<Sprite>("Expedition/Art/achievements-backdrop");
            _backdrop.color = _backdrop.sprite != null ? Color.white : ExpeditionTheme.Panel;
            _backdrop.transform.SetAsFirstSibling();

            var header = ExpeditionShelterSurface.Illustration(sheet, "JournalHeader", "medal-album", .03f, .827f, .97f, .987f);
            ExpeditionUi.Fill(header, "CoverTitleBacking", 0, 0, .49f, 1, new Color32(24, 14, 10, 220));
            var title = ExpeditionUi.Text(header, "JournalTitle", "Альбом медалей", 47, .042f, .40f, .49f, .92f);
            title.fontStyle = FontStyles.Bold;
            title.enableAutoSizing = true;
            title.fontSizeMin = 33;
            title.fontSizeMax = 47;
            _collection = ExpeditionUi.Text(header, "CollectedMedals", "", 24, .044f, .10f, .49f, .40f);
            _collection.color = ExpeditionTheme.Amber;

            var tabBar = ExpeditionUi.Rect(sheet, "JournalTabs", .03f, .754f, .97f, .824f);
            var sections = AchievementSections.All;
            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                var left = i / (float)sections.Count;
                var right = (i + 1) / (float)sections.Count;
                var button = ExpeditionUi.Button(tabBar, "JournalTab-" + section.Id, section.TitleRu,
                    left + .004f, 0, right - .004f, 1, () => selectTab(section.Tab), false, 31);
                var label = button.transform.Find("Label").GetComponent<TMP_Text>();
                ExpeditionUi.SetRect(label.rectTransform, .024f, .04f, .976f, .47f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 22;
                label.fontSizeMax = 31;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.fontStyle = FontStyles.Bold;
                var emblem = ExpeditionUi.Fill(button.transform, "BookmarkEmblem", .32f, .46f, .68f, .95f, Color.white);
                emblem.preserveAspect = true;
                emblem.sprite = ExpeditionTheme.Icon(section.Tab switch
                {
                    AchievementTab.Obsession => "mine",
                    AchievementTab.Slaughter => "arena",
                    AchievementTab.Dnn => "trophy",
                    _ => "bag",
                });
                emblem.enabled = emblem.sprite != null;
                var badge = ExpeditionGoldCountBadge.Build(button.transform, "JournalClaimBadge", "ClaimCount", new Vector2(-7, -5));
                _tabs.Add(section.Tab, new JournalTab { Button = button, Label = label, Emblem = emblem, Badge = badge });
            }

            var scrollSurface = ExpeditionUi.Fill(sheet, "JournalScroll", .03f, .018f, .97f, .748f, JournalSurface);
            scrollSurface.raycastTarget = true;
            _scroll = scrollSurface.gameObject.AddComponent<ScrollRect>();
            ExpeditionUi.Fill(scrollSurface.transform, "AlbumBinding", .493f, .003f, .507f, .997f, new Color32(20, 13, 10, 255));
            ExpeditionUi.Fill(scrollSurface.transform, "AlbumBindingSeam", .499f, .003f, .501f, .997f, new Color32(123, 78, 45, 170));
            var viewport = ExpeditionUi.Rect(scrollSurface.transform, "Viewport", .009f, .008f, .991f, .992f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = ExpeditionUi.Rect(viewport, "Content", 0, 1, 1, 1);
            content.pivot = new Vector2(.5f, 1);
            var grid = content.gameObject.AddComponent<AchievementAlbumGrid>();
            grid.padding = new RectOffset(8, 8, 6, 12);
            grid.spacing = new Vector2(22, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            if (Application.isPlaying) _pageArrival = content.gameObject.AddComponent<ExpeditionPanelArrival>();
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = viewport;
            _scroll.content = content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 55;

            _empty = ExpeditionUi.Text(sheet, "JournalEmpty", "В этом разделе пока нет медалей", 30,
                .07f, .55f, .93f, .73f, TextAlignmentOptions.Center);
            _empty.color = ExpeditionTheme.Muted;
            _empty.gameObject.SetActive(false);
            _geometryDirty = true;
        }

        public void SetEmpty(bool empty)
        {
            if (_empty != null) _empty.gameObject.SetActive(empty);
        }

        public void PresentCollection(int collected, int total)
        {
            _collection.text = $"Медалей собрано {collected} / {total}";
        }

        public void SetActiveTab(AchievementTab selected)
        {
            foreach (var entry in _tabs)
            {
                var tab = entry.Value;
                var active = entry.Key == selected;
                tab.Button.image.sprite = ExpeditionArt.Frame(active) ?? ExpeditionTheme.Plate;
                tab.Label.color = active ? new Color32(35, 27, 18, 255) : ExpeditionTheme.Ivory;
                tab.Emblem.color = active ? Color.white : new Color32(184, 167, 143, 255);
            }
            _pageArrival?.Play();
        }

        public void RefreshBadges()
        {
            foreach (var entry in _tabs)
                entry.Value.Badge.Present(AchievementUiPending.CountEligibleClaimSteps(entry.Key));
        }

        public void RestoreScrollPosition(float position)
        {
            if (_scroll == null) return;
            _scroll.StopMovement();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        public void SetShellLayout()
        {
            _inShell = true;
            if (_backdrop != null) _backdrop.enabled = false;
            _geometryDirty = true;
        }

        public void RefreshGeometry() => _geometryDirty = true;

        private void LateUpdate()
        {
            if (_sheet == null || Screen.width <= 0 || Screen.height <= 0) return;
            var screen = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            if (!_geometryDirty && screen == _lastScreenSize && safe == _lastSafeArea) return;
            var slide = _sheet.anchoredPosition;
            if (_inShell)
                ExpeditionUi.SetRect(_sheet, 0, 0, 1, 1);
            else
            {
                if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0 ||
                    safe.xMax > screen.x + 1 || safe.yMax > screen.y + 1)
                    safe = new Rect(0, 0, screen.x, screen.y);
                var bottom = safe.yMin / screen.y + .105f;
                var top = safe.yMax / screen.y - .078f;
                if (bottom < top)
                    ExpeditionUi.SetRect(_sheet, safe.xMin / screen.x, bottom, safe.xMax / screen.x, top);
                else
                    ExpeditionUi.SetRect(_sheet, 0, .105f, 1, .922f);
            }
            _sheet.anchoredPosition = slide;
            _lastScreenSize = screen;
            _lastSafeArea = Screen.safeArea;
            _geometryDirty = false;
        }

        private static void DisableLayout(RectTransform rect)
        {
            if (rect == null) return;
            var layout = rect.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = rect.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }
    }
}
