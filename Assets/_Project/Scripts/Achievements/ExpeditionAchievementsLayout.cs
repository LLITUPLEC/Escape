using System.Collections.Generic;
using TMPro;
using Project.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Expedition presentation around the existing three achievement pages and reward handlers.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionAchievementsLayout : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(20, 46, 54, 255);
        private static readonly Color Teal = new Color32(40, 90, 96, 255);
        private static readonly Color Paper = new Color32(216, 197, 162, 255);
        private static readonly Color Amber = new Color32(237, 170, 83, 255);
        private static readonly Color Muted = new Color32(137, 173, 170, 255);
        private RectTransform _sheet;
        private TMP_FontAsset _font;
        private Toggle[] _tabs;
        private TMP_Text[] _tabLabels;
        private Image[] _tabBackgrounds;
        private Image[] _tabRules;
        private bool _built;
        private bool _changingGeometry;
        private bool _geometryDirty;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;
        private bool _inShell;
        private GameObject _worldDraw, _panoramaDraw;
        private readonly List<ExpeditionAchievementCard> _cards = new List<ExpeditionAchievementCard>();

        public void Build(RectTransform sheet, Toggle obsession, Toggle slaughter, Toggle dnn,
            RectTransform contentObsession, RectTransform contentSlaughter, RectTransform contentDnn,
            Button close, TMP_FontAsset font)
        {
            if (_built || sheet == null) return;
            _sheet = sheet;
            _font = ExpeditionTheme.Font;
            if (_font == null) _font = font != null ? font : TMP_Settings.defaultFontAsset;
            // The prefab used a vertical stack for these sections. Their new anchors are manual.
            DisableLayout(transform as RectTransform);
            DisableLayout(sheet);
            var surface = sheet.GetComponent<Image>();
            if (surface != null) { surface.enabled = false; surface.raycastTarget = false; }
            var oldFill = sheet.Find("ExpeditionOpaqueFill"); if (oldFill != null) oldFill.gameObject.SetActive(false);
            var dimmer = transform.Find("AchievementsDimmer"); if (dimmer != null) dimmer.gameObject.SetActive(false);
            var outline = sheet.GetComponent<Outline>(); if (outline != null) outline.enabled = false;
            Area(sheet, 0, 0, 1, 1);
            var world = MakeRect("ExpeditionAchievementWorld", sheet).gameObject.AddComponent<Image>();
            _worldDraw = world.gameObject;
            Area(world.rectTransform, 0, 0, 1, 1);
            world.sprite = Resources.Load<Sprite>("Expedition/Art/workshop-backdrop"); world.color = Color.white; world.raycastTarget = true;
            world.transform.SetAsFirstSibling();
            var bannerRoot = MakeRect("ExpeditionAchievementPanorama", sheet);
            _panoramaDraw = bannerRoot.gameObject;
            Area(bannerRoot, 0, .742f, 1, 1);
            ExpeditionUi.Ensure<RectMask2D>(bannerRoot.gameObject);
            var banner = MakeRect("Illustration", bannerRoot).gameObject.AddComponent<Image>();
            Area(banner.rectTransform, 0, 0, 1, 1);
            banner.sprite = Resources.Load<Sprite>("Expedition/Art/achievements-backdrop");
            if (banner.sprite == null) banner.sprite = world.sprite;
            banner.raycastTarget = false;
            if (banner.sprite != null)
            {
                var aspect = banner.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                aspect.aspectRatio = banner.sprite.rect.width / banner.sprite.rect.height;
            }
            bannerRoot.SetSiblingIndex(1);
            var header = sheet.Find("Header") as RectTransform;
            if (header == null) header = MakeRect("Header", sheet);
            DisableLayout(header);
            SetImage(header, Color.white, false);
            Area(header, .025f, .905f, .69f, .982f);
            TMP_Text title = null;
            foreach (var candidate in header.GetComponentsInChildren<TMP_Text>(true))
            {
                if (close != null && candidate.transform.IsChildOf(close.transform)) continue;
                title = candidate; break;
            }
            if (title == null) title = Label("ExpeditionAchievementTitle", header, "Достижения", 52, Paper);
            title.transform.SetParent(header, false); title.text = "Достижения";
            Area(title.rectTransform, .16f, .08f, .98f, .94f);
            StyleText(title, 52, ExpeditionTheme.Ivory, TextAlignmentOptions.MidlineLeft);
            var icon = MakeRect("ExpeditionAchievementIcon", header).gameObject.AddComponent<Image>();
            Area(icon.rectTransform, .025f, .15f, .14f, .85f);
            icon.sprite = ExpeditionTheme.Icon("trophy"); icon.preserveAspect = true; icon.raycastTarget = false; icon.enabled = icon.sprite != null;
            if (close != null) close.gameObject.SetActive(false);
            for (var i = 0; i < header.childCount; i++)
            {
                var child = header.GetChild(i);
                if (child != title.transform && child != icon.transform) child.gameObject.SetActive(false);
            }            var tabBar = sheet.Find("TabBar") as RectTransform;
            if (tabBar == null) tabBar = MakeRect("TabBar", sheet);
            DisableLayout(tabBar);
            Area(tabBar, .035f, .822f, .965f, .892f);
            _tabs = new[] { obsession, slaughter, dnn };
            _tabLabels = new TMP_Text[3];
            _tabBackgrounds = new Image[3];
            _tabRules = new Image[3];
            var captions = new[] { "Одержимость", "Бойня", "ДНН" };
            for (var i = 0; i < _tabs.Length; i++)
            {
                var tab = _tabs[i];
                if (tab == null) continue;
                var rt = tab.transform as RectTransform;
                rt.SetParent(tabBar, false);
                DisableLayout(rt);
                Area(rt, i / 3f + .006f, 0, (i + 1) / 3f - .006f, 1);
                _tabBackgrounds[i] = SetImage(rt, Teal, true);
                tab.targetGraphic = _tabBackgrounds[i];
                if (tab.graphic != null && tab.graphic != tab.targetGraphic) tab.graphic.gameObject.SetActive(false);
                // The explicit section text stays visible for active and inactive tabs.
                var label = Label("ExpeditionTabLabel", rt, captions[i], 30, Paper);
                Area(label.rectTransform, .035f, .12f, .965f, .92f);
                _tabLabels[i] = label;
                var rule = MakeImage("ExpeditionActiveTabRule", rt, Amber);
                Area(rule.rectTransform, .07f, .035f, .93f, .095f);
                _tabRules[i] = rule;
                // Old prefab text/checkmarks are decoration. Keep claim badges and the new elements.
                for (var c = 0; c < tab.transform.childCount; c++)
                {
                    var child = tab.transform.GetChild(c);
                    if (child == label.transform || child == rule.transform || child.name == "has_revard") continue;
                    child.gameObject.SetActive(false);
                }
                var colors = tab.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.12f, 1.12f, 1.08f);
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(.78f, .83f, .8f);
                tab.colors = colors;
            }
            var pages = sheet.Find("Pages") as RectTransform;
            if (pages != null)
            {
                DisableLayout(pages);
                Area(pages, .025f, .037f, .975f, .805f);
            }
            var contentRoots = new[] { contentObsession, contentSlaughter, contentDnn };
            foreach (var content in contentRoots)
            {
                if (content == null) continue;
                var layout = ExpeditionUi.Ensure<VerticalLayoutGroup>(content.gameObject);
                layout.enabled = true;
                layout.padding = new RectOffset(10, 10, 12, 20);
                layout.spacing = 18;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(content.gameObject);
                fitter.enabled = true;
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = content.GetComponentInParent<ScrollRect>(true);
                if (scroll != null)
                {
                    scroll.horizontal = false;
                    scroll.vertical = true;
                    scroll.movementType = ScrollRect.MovementType.Clamped;
                    ExpeditionMetaPanels.StyleScrollSurface(scroll);
                    var scrollRt = scroll.transform as RectTransform;
                    if (scrollRt != null && scrollRt.parent == pages) Area(scrollRt, 0, 0, 1, 1);
                }
            }
            var footer = Label("ExpeditionAchievementFooter", sheet, "Нажмите на ступень, чтобы открыть условия и награду", 22, Muted);
            Area(footer.rectTransform, .06f, .008f, .94f, .036f);
            _built = true;
            _geometryDirty = true;
            SetActiveTab(AchievementTab.Obsession);
        }

        public void SetActiveTab(AchievementTab selected)
        {
            if (!_built) return;
            var index = selected == AchievementTab.Obsession ? 0 : selected == AchievementTab.Slaughter ? 1 : 2;
            for (var i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                var active = i == index;
                _tabBackgrounds[i].sprite = active ? ExpeditionArt.Frame(true) : ExpeditionTheme.Plate;
                _tabBackgrounds[i].color = Color.white;
                _tabLabels[i].color = active ? new Color32(31, 26, 17, 255) : ExpeditionTheme.Ivory;
                _tabRules[i].enabled = false;
                var group = _tabs[i].GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
            }
        }

        public void StyleRow(AchievementChainRowView row, string title, AchievementChainDefinition chain = null, Sprite icon = null)
        {
            if (row == null || chain == null) return;
            var card = ExpeditionUi.Ensure<ExpeditionAchievementCard>(row.gameObject);
            if (!_cards.Contains(card)) _cards.Add(card);
            card.Bind(row, chain, icon);
            card.SetHeight(CardHeight());
        }

        private float CardHeight() => Mathf.Max(360, _sheet != null ? _sheet.rect.height * .285f : 360);

        private void SetSafeSheetBounds()
        {
            if (_inShell) { Area(_sheet, 0, 0, 1, 1); return; }
            var safe = Screen.safeArea;
            var w = Mathf.Max(1, Screen.width);
            var h = Mathf.Max(1, Screen.height);
            // Device Simulator may expose the previous device's rectangle during a resize.
            if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0
                || safe.xMax > w + 1 || safe.yMax > h + 1)
                safe = new Rect(0, 0, w, h);
            var min = new Vector2(Mathf.Clamp01(safe.xMin / w), Mathf.Clamp01(safe.yMin / h) + .105f);
            var max = new Vector2(Mathf.Clamp01(safe.xMax / w), Mathf.Clamp01(safe.yMax / h) - .078f);
            if (max.x <= min.x || max.y <= min.y) { min = new Vector2(0, .105f); max = new Vector2(1, .922f); }
            Area(_sheet, min.x, min.y, max.x, max.y);
        }

        public void RefreshGeometry()
        {
            if (Application.isPlaying && _built) _geometryDirty = true;
        }

        public void SetShellLayout(bool attached)
        {
            _inShell = attached;
            if (_worldDraw != null) _worldDraw.SetActive(!attached);
            if (_panoramaDraw != null) _panoramaDraw.SetActive(!attached);
            RefreshGeometry();
        }

        private void OnEnable() { RefreshGeometry(); }
        private void OnTransformParentChanged() { RefreshGeometry(); }
        private void OnRectTransformDimensionsChange() { if (!_changingGeometry) RefreshGeometry(); }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !_built || _sheet == null || Screen.width <= 0 || Screen.height <= 0) return;
            var dimensions = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            if (!_geometryDirty && dimensions == _lastScreenSize && safe == _lastSafeArea) return;
            _changingGeometry = true;
            var slidePosition = _sheet.anchoredPosition;
            var root = transform as RectTransform;
            if (root != null) { Area(root, 0, 0, 1, 1); root.localScale = Vector3.one; }
            SetSafeSheetBounds();
            _sheet.anchoredPosition = slidePosition;
            _sheet.ForceUpdateRectTransforms();
            var height = CardHeight();
            for (var i = _cards.Count - 1; i >= 0; i--)
            {
                if (_cards[i] == null) _cards.RemoveAt(i);
                else _cards[i].SetHeight(height);
            }
            _lastScreenSize = dimensions;
            _lastSafeArea = safe;
            _geometryDirty = false;
            _changingGeometry = false;
        }

        private TMP_Text Label(string name, Transform parent, string text, float size, Color color)
        {
            var rt = MakeRect(name, parent);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            StyleText(label, size, color, TextAlignmentOptions.Center);
            return label;
        }

        private static void StyleText(TMP_Text text, float size, Color color, TextAlignmentOptions alignment)
        {
            if (ExpeditionTheme.Font != null) text.font = ExpeditionTheme.Font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .75f;
            text.fontSizeMax = size;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image MakeImage(string name, Transform parent, Color color)
        {
            return SetImage(MakeRect(name, parent), color, false);
        }

        private static Image SetImage(RectTransform rt, Color color, bool raycast)
        {
            var image = ExpeditionUi.Ensure<Image>(rt.gameObject);
            image.sprite = ExpeditionTheme.Plate;
            image.pixelsPerUnitMultiplier = 4f;
            image.type = Image.Type.Sliced;
            image.color = ExpeditionArt.Frame() != null ? Color.white : color;
            image.raycastTarget = raycast;
            return image;
        }

        private static void DisableLayout(RectTransform rt)
        {
            if (rt == null) return;
            var layout = rt.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = rt.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private static void Area(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
