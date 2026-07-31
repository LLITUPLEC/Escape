using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Project.Achievements;
using Project.UI;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Friends
{
    /// <summary>Модалка «Друзья»: вкладки Друзья / Онлайн, slide-sheet как у достижений.</summary>
    public sealed class FriendsPanelController : MonoBehaviour
    {
        public const string PanelRootName = "FriendsPanelRoot";
        private const string OpenButtonName = "BottomButtonFriends";

        [SerializeField] private TMP_FontAsset uiFont;
        [SerializeField] private float slideDuration = 0.38f;
        [SerializeField] private float hiddenAnchoredY = -1400f;
        [SerializeField, Range(0.12f, 1f)] private float inactiveTabAlpha = 0.48f;

        private RectTransform _sheetRect;
        private CanvasGroup _rootCanvasGroup;
        private Button _dimmerButton;
        private Button _closeButton;
        private Toggle _tabFriends;
        private Toggle _tabOnline;
        private GameObject _friendsPage;
        private GameObject _onlinePage;
        private RectTransform _friendsContent;
        private RectTransform _onlineContent;
        private ScrollRect _friendsScroll;
        private ScrollRect _onlineScroll;
        private TMP_Text _friendsStatusText;
        private TMP_Text _onlineStatusText;
        private TMP_Text _onlineFooterText;
        private Button _addFriendButton;
        private Button _refreshOnlineButton;
        private GameObject _addFriendOverlay;
        private TMP_InputField _addFriendInput;
        private TMP_Text _addFriendFeedback;
        private GameObject _actionPopupRoot;
        private TMP_Text _actionPopupTitle;

        private Button _openButton;
        private bool _wiredOpenButton;
        private Coroutine _slideRoutine;
        private Vector2 _sheetShownPos;
        private FriendsTab _currentTab = FriendsTab.Friends;
        private bool _friendsRefreshInFlight;
        private bool _onlineRefreshInFlight;
        private CancellationTokenSource _friendsCts;
        private CancellationTokenSource _onlineCts;
        private readonly List<FriendsPlayerRowView> _friendRows = new List<FriendsPlayerRowView>();
        private readonly List<FriendsPlayerRowView> _onlineRows = new List<FriendsPlayerRowView>();
        private FriendListEntry _actionTarget;

        private static FriendsPanelController s_buttonOwner;

        public static FriendsPanelController Ensure(Transform hudRoot)
        {
            if (hudRoot == null)
                return null;

            var existing = hudRoot.Find(PanelRootName);
            if (existing != null)
            {
                var ctrl = existing.GetComponent<FriendsPanelController>();
                if (ctrl != null)
                    return ctrl;
                return existing.gameObject.AddComponent<FriendsPanelController>();
            }

            var go = new GameObject(PanelRootName, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hudRoot, false);
            var rt = go.GetComponent<RectTransform>();
            StretchFull(rt);
            return go.AddComponent<FriendsPanelController>();
        }

        private void Awake()
        {
            if (!TryClaimSingletonInstance())
                return;

            if (_sheetRect == null)
                BuildUi();

            uiFont = AchievementUiFontLoader.Resolve(uiFont);
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, uiFont);

            if (_sheetRect != null)
            {
                _sheetShownPos = _sheetRect.anchoredPosition;
                _sheetRect.anchoredPosition = new Vector2(_sheetShownPos.x, hiddenAnchoredY);
            }

            if (_rootCanvasGroup != null)
            {
                _rootCanvasGroup.alpha = 0f;
                _rootCanvasGroup.blocksRaycasts = false;
                _rootCanvasGroup.interactable = false;
            }

            gameObject.SetActive(true);

            _closeButton = ModalPanelCloseButton.EnsureTopRight(
                _closeButton,
                _sheetRect,
                transform,
                "FriendsSheet/Header/CloseButton",
                uiFont,
                Hide);
            ModalPanelCloseButton.EnsureDimmerRaycast(_dimmerButton);
            if (_dimmerButton != null)
            {
                _dimmerButton.onClick.RemoveListener(Hide);
                _dimmerButton.onClick.AddListener(Hide);
            }

            WireTabs();
            WireToolbar();
            TryBindOpenButton();
            SwitchTab(FriendsTab.Friends, refresh: false);
            MainMenuHudLayering.EnsurePanelSubModalsOnTop(transform);
        }

        private void OnEnable()
        {
            TryBindOpenButton();
        }

        private void OnDestroy()
        {
            _friendsCts?.Cancel();
            _friendsCts?.Dispose();
            _onlineCts?.Cancel();
            _onlineCts?.Dispose();
            if (_openButton != null)
                _openButton.onClick.RemoveListener(TogglePanel);
            if (s_buttonOwner == this)
                s_buttonOwner = null;
            if (_closeButton != null)
                _closeButton.onClick.RemoveListener(Hide);
            if (_dimmerButton != null)
                _dimmerButton.onClick.RemoveListener(Hide);
        }

        private bool TryClaimSingletonInstance()
        {
            var all = FindObjectsByType<FriendsPanelController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length <= 1)
                return true;

            FriendsPanelController keeper = this;
            var bestSiblingIndex = int.MaxValue;
            foreach (var panel in all)
            {
                if (panel == null) continue;
                var idx = panel.transform.GetSiblingIndex();
                if (idx < bestSiblingIndex)
                {
                    bestSiblingIndex = idx;
                    keeper = panel;
                }
            }

            if (keeper != this)
            {
                Destroy(gameObject);
                return false;
            }

            foreach (var panel in all)
            {
                if (panel != null && panel != this)
                    Destroy(panel.gameObject);
            }

            return true;
        }

        private void BuildUi()
        {
            if (_sheetRect != null)
                return;

            uiFont = AchievementUiFontLoader.Resolve(uiFont);
            _rootCanvasGroup = GetComponent<CanvasGroup>();
            if (_rootCanvasGroup == null)
                _rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();

            var rootRt = transform as RectTransform;
            StretchFull(rootRt);

            _dimmerButton = MakeDimmer(transform);
            _sheetRect = MakeSheet(transform);
            var header = MakeHeader(_sheetRect);
            MakeTabBar(_sheetRect);
            _friendsPage = MakeFriendsPage(_sheetRect);
            _onlinePage = MakeOnlinePage(_sheetRect);
            MakeAddFriendOverlay(_sheetRect);
            MakeActionPopup(transform);

            _closeButton = header.Find("CloseButton")?.GetComponent<Button>();
            AchievementsTmpMaterialRepair.RepairHierarchy(transform, uiFont);
        }

        private Button MakeDimmer(Transform parent)
        {
            var go = new GameObject("FriendsDimmer", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            StretchFull(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            return btn;
        }

        private RectTransform MakeSheet(Transform parent)
        {
            var go = new GameObject("FriendsSheet", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.08f);
            rt.anchorMax = new Vector2(0.96f, 0.92f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = new Color(0.08f, 0.09f, 0.12f, 0.98f);
            return rt;
        }

        private Transform MakeHeader(RectTransform sheet)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(sheet, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 72f);
            rt.anchoredPosition = Vector2.zero;

            var title = MakeTmp(go.transform, "Title", "Друзья", 36f, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(24f, 0f);
            titleRt.offsetMax = new Vector2(-72f, 0f);
            title.alignment = TextAlignmentOptions.MidlineLeft;

            var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(go.transform, false);
            return go.transform;
        }

        private Transform MakeTabBar(RectTransform sheet)
        {
            var go = new GameObject("TabBar", typeof(RectTransform), typeof(ToggleGroup), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(sheet, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 56f);
            rt.anchoredPosition = new Vector2(0f, -78f);

            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(16, 16, 4, 4);
            hl.spacing = 10f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;

            var group = go.GetComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            _tabFriends = MakeTab(go.transform, "TabFriends", "Друзья", group);
            _tabOnline = MakeTab(go.transform, "TabOnline", "Онлайн", group);
            return go.transform;
        }

        private Toggle MakeTab(Transform parent, string name, string label, ToggleGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = new Color(0.16f, 0.18f, 0.24f, 1f);
            var toggle = go.GetComponent<Toggle>();
            toggle.group = group;
            toggle.targetGraphic = img;
            toggle.isOn = false;

            var tmp = MakeTmp(go.transform, "Label", label, 26f, FontStyles.Bold);
            StretchFull(tmp.rectTransform);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return toggle;
        }

        private GameObject MakeFriendsPage(RectTransform sheet)
        {
            var page = MakePageRoot(sheet, "FriendsPage");
            var toolbar = MakeToolbar(page.transform, "Toolbar");
            _addFriendButton = MakeToolbarButton(toolbar, "AddFriendButton", "+", new Color(0.22f, 0.48f, 0.30f, 1f));

            var (scroll, content) = MakeScroll(page.transform, "FriendsScroll", top: -58f, bottom: 54f);
            _friendsScroll = scroll;
            _friendsContent = content;

            _friendsStatusText = MakeTmp(page.transform, "StatusText", "", 22f, FontStyles.Normal);
            PlaceStatus(_friendsStatusText.rectTransform, 12f);
            _friendsStatusText.alignment = TextAlignmentOptions.Center;
            _friendsStatusText.gameObject.SetActive(false);
            return page;
        }

        private GameObject MakeOnlinePage(RectTransform sheet)
        {
            var page = MakePageRoot(sheet, "OnlinePage");
            var toolbar = MakeToolbar(page.transform, "Toolbar");
            _refreshOnlineButton = MakeToolbarButton(toolbar, "RefreshButton", "Обновить", new Color(0.24f, 0.40f, 0.62f, 1f));
            var refreshLe = _refreshOnlineButton.GetComponent<LayoutElement>();
            if (refreshLe != null)
            {
                refreshLe.preferredWidth = 180f;
                refreshLe.minWidth = 140f;
            }

            var (scroll, content) = MakeScroll(page.transform, "OnlineScroll", top: -58f, bottom: 88f);
            _onlineScroll = scroll;
            _onlineContent = content;

            _onlineFooterText = MakeTmp(page.transform, "FooterText", "Нажмите «Обновить»", 20f, FontStyles.Normal);
            var footerRt = _onlineFooterText.rectTransform;
            footerRt.anchorMin = new Vector2(0f, 0f);
            footerRt.anchorMax = new Vector2(1f, 0f);
            footerRt.pivot = new Vector2(0.5f, 0f);
            footerRt.sizeDelta = new Vector2(-24f, 36f);
            footerRt.anchoredPosition = new Vector2(0f, 14f);
            _onlineFooterText.alignment = TextAlignmentOptions.Center;
            _onlineFooterText.color = new Color(0.72f, 0.74f, 0.78f, 1f);

            _onlineStatusText = MakeTmp(page.transform, "StatusText", "", 22f, FontStyles.Normal);
            PlaceStatus(_onlineStatusText.rectTransform, 52f);
            _onlineStatusText.alignment = TextAlignmentOptions.Center;
            _onlineStatusText.gameObject.SetActive(false);
            page.SetActive(false);
            return page;
        }

        private GameObject MakePageRoot(RectTransform sheet, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(sheet, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, 8f);
            rt.offsetMax = new Vector2(0f, -140f);
            return go;
        }

        private RectTransform MakeToolbar(Transform page, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(page, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 52f);
            rt.anchoredPosition = Vector2.zero;
            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(16, 16, 4, 4);
            hl.spacing = 10f;
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            return rt;
        }

        private Button MakeToolbarButton(Transform toolbar, string name, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(toolbar, false);
            var img = go.GetComponent<Image>();
            img.sprite = ModalPanelCloseButton.WhiteSprite();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 56f;
            le.preferredHeight = 44f;
            le.minWidth = 56f;
            le.minHeight = 44f;

            var tmp = MakeTmp(go.transform, "Label", label, 28f, FontStyles.Bold);
            StretchFull(tmp.rectTransform);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return btn;
        }

        private (ScrollRect scroll, RectTransform content) MakeScroll(Transform page, string name, float top, float bottom)
        {
            var scrollGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(page, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(12f, bottom);
            scrollRt.offsetMax = new Vector2(-12f, top);
            var scrollImg = scrollGo.GetComponent<Image>();
            scrollImg.sprite = ModalPanelCloseButton.WhiteSprite();
            scrollImg.color = new Color(0.05f, 0.06f, 0.08f, 0.55f);
            scrollImg.raycastTarget = true;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            StretchFull(viewportRt);
            var vpImg = viewportGo.GetComponent<Image>();
            vpImg.sprite = ModalPanelCloseButton.WhiteSprite();
            vpImg.color = new Color(1f, 1f, 1f, 0.01f);
            vpImg.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);
            var vl = contentGo.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(8, 8, 8, 8);
            vl.spacing = 8f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandHeight = false;
            vl.childForceExpandWidth = true;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return (scroll, contentRt);
        }

        private void MakeAddFriendOverlay(RectTransform sheet)
        {
            _addFriendOverlay = new GameObject("AddFriendOverlay", typeof(RectTransform), typeof(Image));
            _addFriendOverlay.transform.SetParent(sheet, false);
            var rt = _addFriendOverlay.GetComponent<RectTransform>();
            StretchFull(rt);
            var dim = _addFriendOverlay.GetComponent<Image>();
            dim.sprite = ModalPanelCloseButton.WhiteSprite();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_addFriendOverlay.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(520f, 0f);
            panel.GetComponent<Image>().sprite = ModalPanelCloseButton.WhiteSprite();
            panel.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.17f, 1f);
            var vl = panel.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(20, 20, 18, 16);
            vl.spacing = 12f;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = MakeTmp(panel.transform, "Title", "Добавить друга", 28f, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Center;

            var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            inputGo.transform.SetParent(panel.transform, false);
            inputGo.GetComponent<Image>().sprite = ModalPanelCloseButton.WhiteSprite();
            inputGo.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);
            inputGo.GetComponent<LayoutElement>().preferredHeight = 48f;
            _addFriendInput = inputGo.GetComponent<TMP_InputField>();

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(inputGo.transform, false);
            StretchFull(textArea.GetComponent<RectTransform>(), 10f, 8f);

            var placeholder = MakeTmp(textArea.transform, "Placeholder", "UserName", 24f, FontStyles.Italic);
            StretchFull(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            var inputText = MakeTmp(textArea.transform, "Text", "", 24f, FontStyles.Normal);
            StretchFull(inputText.rectTransform);
            inputText.alignment = TextAlignmentOptions.MidlineLeft;

            _addFriendInput.textViewport = textArea.GetComponent<RectTransform>();
            _addFriendInput.textComponent = inputText;
            _addFriendInput.placeholder = placeholder;
            _addFriendInput.fontAsset = AchievementUiFontLoader.Resolve(uiFont);

            _addFriendFeedback = MakeTmp(panel.transform, "Feedback", "", 18f, FontStyles.Normal);
            _addFriendFeedback.alignment = TextAlignmentOptions.Center;
            _addFriendFeedback.color = new Color(1f, 0.82f, 0.55f, 1f);

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            buttons.GetComponent<LayoutElement>().preferredHeight = 48f;
            var hl = buttons.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childForceExpandWidth = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;

            var cancel = MakeToolbarButton(buttons.transform, "Cancel", "Отмена", new Color(0.28f, 0.30f, 0.34f, 1f));
            var confirm = MakeToolbarButton(buttons.transform, "Confirm", "Добавить", new Color(0.22f, 0.48f, 0.30f, 1f));
            cancel.onClick.AddListener(HideAddFriendOverlay);
            confirm.onClick.AddListener(() => _ = SubmitAddFriendAsync());

            _addFriendOverlay.SetActive(false);
        }

        private void MakeActionPopup(Transform root)
        {
            _actionPopupRoot = new GameObject("FriendsActionPopup", typeof(RectTransform), typeof(Image), typeof(Button));
            _actionPopupRoot.transform.SetParent(root, false);
            StretchFull(_actionPopupRoot.GetComponent<RectTransform>());
            var dim = _actionPopupRoot.GetComponent<Image>();
            dim.sprite = ModalPanelCloseButton.WhiteSprite();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimBtn = _actionPopupRoot.GetComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(HideActionPopup);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_actionPopupRoot.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(480f, 0f);
            panel.GetComponent<Image>().sprite = ModalPanelCloseButton.WhiteSprite();
            panel.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.17f, 1f);
            var vl = panel.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(18, 18, 16, 14);
            vl.spacing = 10f;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _actionPopupTitle = MakeTmp(panel.transform, "Title", "Действие", 26f, FontStyles.Bold);
            _actionPopupTitle.alignment = TextAlignmentOptions.Center;

            var duelBtn = MakeToolbarButton(panel.transform, "DuelInvite", "Предложить дуэль", new Color(0.55f, 0.28f, 0.22f, 1f));
            var tourneyBtn = MakeToolbarButton(panel.transform, "TournamentInvite", "В группу турнира", new Color(0.24f, 0.40f, 0.62f, 1f));
            var duelLe = duelBtn.GetComponent<LayoutElement>();
            var tourneyLe = tourneyBtn.GetComponent<LayoutElement>();
            if (duelLe != null) { duelLe.preferredHeight = 48f; duelLe.flexibleWidth = 1f; duelLe.preferredWidth = -1f; }
            if (tourneyLe != null) { tourneyLe.preferredHeight = 48f; tourneyLe.flexibleWidth = 1f; tourneyLe.preferredWidth = -1f; }

            duelBtn.onClick.AddListener(() => OnPlayerActionChosen("duel"));
            tourneyBtn.onClick.AddListener(() => OnPlayerActionChosen("tournament"));

            _actionPopupRoot.SetActive(false);
        }

        private void WireTabs()
        {
            if (_tabFriends != null)
                _tabFriends.onValueChanged.AddListener(v => { if (v) SwitchTab(FriendsTab.Friends); });
            if (_tabOnline != null)
                _tabOnline.onValueChanged.AddListener(v => { if (v) SwitchTab(FriendsTab.Online); });
            _tabFriends?.SetIsOnWithoutNotify(true);
            RefreshTabVisuals();
        }

        private void WireToolbar()
        {
            if (_addFriendButton != null)
                _addFriendButton.onClick.AddListener(ShowAddFriendOverlay);
            if (_refreshOnlineButton != null)
                _refreshOnlineButton.onClick.AddListener(() => _ = RefreshOnlineAsync());
        }

        private void SwitchTab(FriendsTab tab, bool refresh = true)
        {
            _currentTab = tab;
            _tabFriends?.SetIsOnWithoutNotify(tab == FriendsTab.Friends);
            _tabOnline?.SetIsOnWithoutNotify(tab == FriendsTab.Online);
            RefreshTabVisuals();

            if (_friendsPage != null) _friendsPage.SetActive(tab == FriendsTab.Friends);
            if (_onlinePage != null) _onlinePage.SetActive(tab == FriendsTab.Online);

            HideAddFriendOverlay();
            HideActionPopup();

            if (!refresh)
                return;

            if (tab == FriendsTab.Friends)
                _ = RefreshFriendsAsync();
        }

        private void RefreshTabVisuals()
        {
            ApplyToggleTabMuted(_tabFriends, _currentTab != FriendsTab.Friends);
            ApplyToggleTabMuted(_tabOnline, _currentTab != FriendsTab.Online);
        }

        private void ApplyToggleTabMuted(Toggle t, bool muted)
        {
            if (t == null) return;
            var cg = t.GetComponent<CanvasGroup>();
            if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = muted ? inactiveTabAlpha : 1f;
            cg.blocksRaycasts = true;
            cg.interactable = true;
        }

        private void TryBindOpenButton()
        {
            if (_wiredOpenButton)
                return;
            if (s_buttonOwner != null && s_buttonOwner != this)
                return;

            var btnTr = FindDeepChild(transform.root, OpenButtonName);
            if (btnTr == null)
                return;
            _openButton = btnTr.GetComponent<Button>();
            if (_openButton == null)
                return;

            _openButton.onClick.RemoveListener(TogglePanel);
            _openButton.onClick.AddListener(TogglePanel);
            _wiredOpenButton = true;
            s_buttonOwner = this;
        }

        private void TogglePanel()
        {
            var visible = _rootCanvasGroup != null && _rootCanvasGroup.alpha > 0.01f;
            if (visible) Hide();
            else Show();
        }

        private void Show()
        {
            HideAddFriendOverlay();
            HideActionPopup();
            MainMenuHudLayering.BringPanelToFront(transform);
            if (_rootCanvasGroup != null)
            {
                _rootCanvasGroup.blocksRaycasts = true;
                _rootCanvasGroup.interactable = true;
            }

            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: true));

            if (_currentTab == FriendsTab.Friends)
                _ = RefreshFriendsAsync();
        }

        private void Hide()
        {
            HideAddFriendOverlay();
            HideActionPopup();
            _friendsCts?.Cancel();
            _onlineCts?.Cancel();
            if (_slideRoutine != null)
                StopCoroutine(_slideRoutine);
            _slideRoutine = StartCoroutine(AnimateSlide(show: false));
        }

        private IEnumerator AnimateSlide(bool show)
        {
            float fromA = _rootCanvasGroup != null ? _rootCanvasGroup.alpha : 0f;
            float toA = show ? 1f : 0f;
            Vector2 fromY = _sheetRect != null ? _sheetRect.anchoredPosition : Vector2.zero;
            Vector2 toY = show ? _sheetShownPos : new Vector2(_sheetShownPos.x, hiddenAnchoredY);

            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / slideDuration);
                var ease = k * k * (3f - 2f * k);
                if (_rootCanvasGroup != null)
                    _rootCanvasGroup.alpha = Mathf.Lerp(fromA, toA, ease);
                if (_sheetRect != null)
                    _sheetRect.anchoredPosition = Vector2.Lerp(fromY, toY, ease);
                yield return null;
            }

            if (_rootCanvasGroup != null)
            {
                _rootCanvasGroup.alpha = toA;
                _rootCanvasGroup.blocksRaycasts = show;
                _rootCanvasGroup.interactable = show;
            }

            if (_sheetRect != null)
                _sheetRect.anchoredPosition = toY;
            _slideRoutine = null;
        }

        private async System.Threading.Tasks.Task RefreshFriendsAsync()
        {
            if (_friendsRefreshInFlight)
                return;
            _friendsRefreshInFlight = true;
            _friendsCts?.Cancel();
            _friendsCts?.Dispose();
            _friendsCts = new CancellationTokenSource();
            var ct = _friendsCts.Token;

            SetFriendsStatus("Загрузка...");

            try
            {
                var result = await FriendsService.ListFriendsAsync(ct);
                if (ct.IsCancellationRequested)
                    return;

                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    if (!result.Ok)
                    {
                        ClearFriendRows();
                        SetFriendsStatus(FriendsService.DescribeError(result.Err));
                        return;
                    }

                    ApplyFriends(result.Friends);
                    SetFriendsStatus(result.Friends.Length == 0 ? "Нет друзей" : string.Empty);
                });
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            finally
            {
                _friendsRefreshInFlight = false;
            }
        }

        private async System.Threading.Tasks.Task RefreshOnlineAsync()
        {
            if (_onlineRefreshInFlight)
                return;
            _onlineRefreshInFlight = true;
            _onlineCts?.Cancel();
            _onlineCts?.Dispose();
            _onlineCts = new CancellationTokenSource();
            var ct = _onlineCts.Token;

            SetOnlineStatus("Загрузка...");

            try
            {
                var result = await FriendsService.ListOnlineAsync(ct, 50);
                if (ct.IsCancellationRequested)
                    return;

                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    if (!result.Ok)
                    {
                        ClearOnlineRows();
                        SetOnlineStatus(FriendsService.DescribeError(result.Err));
                        if (_onlineFooterText != null)
                            _onlineFooterText.text = string.Empty;
                        return;
                    }

                    ApplyOnline(result.Players);
                    SetOnlineStatus(result.Players.Length == 0 ? "Никого нет онлайн" : string.Empty);
                    if (_onlineFooterText != null)
                        _onlineFooterText.text = $"Всего {result.Total}, показано {result.Shown}";
                });
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            finally
            {
                _onlineRefreshInFlight = false;
            }
        }

        private void ApplyFriends(FriendListEntry[] friends)
        {
            ClearFriendRows();
            if (_friendsContent == null || friends == null)
                return;

            foreach (var entry in friends)
            {
                if (entry == null) continue;
                var row = FriendsPlayerRowView.Create(_friendsContent, uiFont, showFriendControls: true);
                row.BindFriend(entry, OnRemoveFriendClicked, OnActionFriendClicked);
                _friendRows.Add(row);
            }

            if (_friendsScroll != null)
                _friendsScroll.verticalNormalizedPosition = 1f;
            AchievementsTmpMaterialRepair.RepairHierarchy(_friendsContent, uiFont);
        }

        private void ApplyOnline(OnlinePlayerEntry[] players)
        {
            ClearOnlineRows();
            if (_onlineContent == null || players == null)
                return;

            foreach (var entry in players)
            {
                if (entry == null) continue;
                var row = FriendsPlayerRowView.Create(_onlineContent, uiFont, showFriendControls: false);
                row.BindOnline(entry);
                _onlineRows.Add(row);
            }

            if (_onlineScroll != null)
                _onlineScroll.verticalNormalizedPosition = 1f;
            AchievementsTmpMaterialRepair.RepairHierarchy(_onlineContent, uiFont);
        }

        private void ClearFriendRows()
        {
            foreach (var row in _friendRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }
            _friendRows.Clear();
        }

        private void ClearOnlineRows()
        {
            foreach (var row in _onlineRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }
            _onlineRows.Clear();
        }

        private void ShowAddFriendOverlay()
        {
            HideActionPopup();
            if (_addFriendOverlay == null) return;
            if (_addFriendInput != null) _addFriendInput.text = string.Empty;
            if (_addFriendFeedback != null) _addFriendFeedback.text = string.Empty;
            _addFriendOverlay.SetActive(true);
            _addFriendOverlay.transform.SetAsLastSibling();
        }

        private void HideAddFriendOverlay()
        {
            if (_addFriendOverlay != null)
                _addFriendOverlay.SetActive(false);
        }

        private async System.Threading.Tasks.Task SubmitAddFriendAsync()
        {
            var username = _addFriendInput != null ? _addFriendInput.text : string.Empty;
            if (_addFriendFeedback != null)
                _addFriendFeedback.text = "Отправка...";

            var ct = CancellationToken.None;
            try
            {
                var result = await FriendsService.AddFriendByUsernameAsync(username, ct);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!result.Ok)
                    {
                        if (_addFriendFeedback != null)
                            _addFriendFeedback.text = FriendsService.DescribeError(result.Err);
                        return;
                    }

                    HideAddFriendOverlay();
                    SetFriendsStatus("Заявка отправлена");
                    _ = RefreshFriendsAsync();
                });
            }
            catch (Exception e)
            {
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (_addFriendFeedback != null)
                        _addFriendFeedback.text = FriendsService.DescribeError(e.Message);
                });
            }
        }

        private void OnRemoveFriendClicked(FriendsPlayerRowView row)
        {
            if (row == null) return;
            _ = DeleteFriendAsync(row.UserId, row.Username);
        }

        private async System.Threading.Tasks.Task DeleteFriendAsync(string userId, string username)
        {
            SetFriendsStatus("Удаление...");
            try
            {
                var result = await FriendsService.DeleteFriendAsync(userId, username, CancellationToken.None);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!result.Ok)
                    {
                        SetFriendsStatus(FriendsService.DescribeError(result.Err));
                        return;
                    }

                    SetFriendsStatus(string.Empty);
                    _ = RefreshFriendsAsync();
                });
            }
            catch (Exception e)
            {
                await MainThreadDispatcher.RunAsync(() =>
                    SetFriendsStatus(FriendsService.DescribeError(e.Message)));
            }
        }

        private void OnActionFriendClicked(FriendsPlayerRowView row)
        {
            if (row == null) return;
            _actionTarget = new FriendListEntry
            {
                UserId = row.UserId,
                Username = row.Username,
                Online = true,
                State = FriendRelationState.Mutual,
            };
            ShowActionPopup();
        }

        private void ShowActionPopup()
        {
            HideAddFriendOverlay();
            if (_actionPopupRoot == null) return;
            if (_actionPopupTitle != null)
            {
                var name = string.IsNullOrWhiteSpace(_actionTarget?.Username) ? "игрок" : _actionTarget.Username;
                _actionPopupTitle.text = name;
            }

            _actionPopupRoot.SetActive(true);
            _actionPopupRoot.transform.SetAsLastSibling();
            MainMenuHudLayering.EnsurePanelSubModalsOnTop(transform);
        }

        private void HideActionPopup()
        {
            if (_actionPopupRoot != null)
                _actionPopupRoot.SetActive(false);
            _actionTarget = null;
        }

        private void OnPlayerActionChosen(string kind)
        {
            var name = string.IsNullOrWhiteSpace(_actionTarget?.Username) ? "игроку" : _actionTarget.Username;
            HideActionPopup();
            var message = kind == "tournament"
                ? $"Приглашение в группу турнира для «{name}» скоро будет доступно"
                : $"Предложение дуэли для «{name}» скоро будет доступно";
            SetFriendsStatus(message);
        }

        private void SetFriendsStatus(string message)
        {
            if (_friendsStatusText == null) return;
            _friendsStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _friendsStatusText.text = message ?? string.Empty;
        }

        private void SetOnlineStatus(string message)
        {
            if (_onlineStatusText == null) return;
            _onlineStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _onlineStatusText.text = message ?? string.Empty;
        }

        private TMP_Text MakeTmp(Transform parent, string name, string text, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            var fa = AchievementUiFontLoader.Resolve(uiFont);
            if (fa != null)
            {
                tmp.font = fa;
                if (fa.material != null)
                    tmp.fontSharedMaterial = fa.material;
            }

            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.richText = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void PlaceStatus(RectTransform rt, float bottom)
        {
            rt.anchorMin = new Vector2(0.08f, 0f);
            rt.anchorMax = new Vector2(0.92f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 40f);
            rt.anchoredPosition = new Vector2(0f, bottom);
        }

        private static void StretchFull(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeepChild(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
