using System.Collections.Generic;
using Project.Friends;
using Project.Leaderboard;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Cached presentation adapters for social and account panels. Existing controllers own all actions/data.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionMetaPanels : MonoBehaviour
    {
        private RectTransform _sheet;
        private bool _built;
        private bool _changingGeometry;
        private bool _geometryDirty;
        private Vector4 _sheetBounds = new Vector4(.025f, .025f, .975f, .975f);
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;
        private int _pageKind;
        private bool _pageAttached;
        private GameObject _friendsEmpty;
        private GameObject _onlineEmpty;
        private GameObject _ratingEmpty;
        private readonly List<Toggle> _tabs = new List<Toggle>();
        private readonly List<TMP_Text> _tabLabels = new List<TMP_Text>();
        private readonly List<Image> _tabRules = new List<Image>();

        private static ExpeditionMetaPanels Ensure(Transform root)
        {
            return root != null ? ExpeditionUi.Ensure<ExpeditionMetaPanels>(root.gameObject) : null;
        }

        public static void ApplyFriends(Transform root)
        {
            var adapter = Ensure(root);
            if (adapter == null) return;
            if (!adapter._built) adapter.BuildFriends();
            adapter.RefreshGeometry();
            adapter.RefreshTabs();
        }

        public static void ApplyLeaderboard(Transform root)
        {
            var adapter = Ensure(root);
            if (adapter == null) return;
            if (!adapter._built) adapter.BuildLeaderboard();
            adapter.RefreshGeometry();
            adapter.RefreshTabs();
        }

        public static void ApplySettings(Transform root, RectTransform panel)
        {
            var adapter = Ensure(root);
            if (adapter == null || panel == null) return;
            if (!adapter._built) adapter.BuildSettings(panel);
            adapter._sheet = panel;
            adapter._sheetBounds = new Vector4(.06f, .13f, .94f, .88f);
            // The settings controller manages keyboard lifting, so its open hook establishes the base rectangle.
            SetSafeBounds(panel, .06f, .13f, .94f, .88f);
            adapter.RefreshGeometry();
        }

        public static void ApplyEnergy(Transform root, UnityAction close)
        {
            var adapter = Ensure(root);
            if (adapter == null) return;
            if (!adapter._built) adapter.BuildEnergy(close);
            adapter._sheetBounds = new Vector4(.055f, .24f, .945f, .79f);
            if (adapter._sheet != null) SetSafeBounds(adapter._sheet, .055f, .24f, .945f, .79f);
            adapter.RefreshGeometry();
        }

        public static void UpdateTabs(Transform root)
        {
            var adapter = root != null ? root.GetComponent<ExpeditionMetaPanels>() : null;
            if (adapter != null) adapter.RefreshTabs();
        }

        public static void AttachPage(Transform root, RectTransform pageHost)
        {
            if (root == null || pageHost == null) return;
            root.SetParent(pageHost, false);
            ExpeditionUi.SetRect(root as RectTransform, 0, 0, 1, 1);
            var adapter = Ensure(root);
            adapter._pageAttached = true;
            var world = ExpeditionUi.Find(root, "ExpeditionPageWorld")?.GetComponent<Image>();
            if (world != null) world.enabled = false;
            var panorama = ExpeditionUi.Find(root, "ExpeditionPagePanorama");
            if (panorama != null) panorama.gameObject.SetActive(false);
            adapter.RefreshGeometry();
        }

        private void StylePageScene(string panorama, string fallback)
        {
            DisableLayout(transform as RectTransform);
            DisableLayout(_sheet);
            var image = _sheet.GetComponent<Image>(); if (image != null) { image.enabled = false; image.raycastTarget = false; }
            var oldFill = _sheet.Find("ExpeditionOpaqueFill"); if (oldFill != null) oldFill.gameObject.SetActive(false);
            var dimmer = transform.Find(_pageKind == 1 ? "FriendsDimmer" : "LeaderboardDimmer");
            if (dimmer != null) dimmer.gameObject.SetActive(false);
            var world = ExpeditionUi.Rect(_sheet, "ExpeditionPageWorld", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            world.sprite = Resources.Load<Sprite>("Expedition/Art/" + fallback); world.raycastTarget = true;
            world.enabled = !_pageAttached;
            world.color = world.sprite != null ? Color.white : ExpeditionTheme.Panel;
            world.transform.SetAsFirstSibling();
            if (string.IsNullOrEmpty(panorama)) return;
            var bannerRoot = ExpeditionUi.Rect(_sheet, "ExpeditionPagePanorama", 0, _pageKind == 1 ? .685f : .495f, 1, _pageKind == 1 ? 1 : .769f);
            ExpeditionUi.Ensure<RectMask2D>(bannerRoot.gameObject);
            var banner = ExpeditionUi.Rect(bannerRoot, "Illustration", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            banner.sprite = Resources.Load<Sprite>("Expedition/Art/" + panorama);
            if (banner.sprite == null) banner.sprite = world.sprite;
            banner.raycastTarget = false;
            if (banner.sprite != null)
            {
                var aspect = banner.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                aspect.aspectRatio = banner.sprite.rect.width / banner.sprite.rect.height;
            }
            bannerRoot.SetSiblingIndex(1);
            if (_pageAttached || _pageKind == 2) bannerRoot.gameObject.SetActive(false);
        }

        // Kept as the controller's existing response hook; ranking now lives exclusively in rows.
        public static void BindLeaderboardPodium(Transform root, LeaderboardEntryDto[] entries)
        {
            var adapter = root != null ? root.GetComponent<ExpeditionMetaPanels>() : null;
            if (adapter == null || adapter._pageKind != 2) return;
            var rewards = adapter._sheet.Find("RewardsBar");
            var list = adapter._sheet.Find("ListArea") as RectTransform;
            if (list != null) ExpeditionUi.SetRect(list, .025f, .119f, .975f,
                rewards != null && rewards.gameObject.activeSelf ? .652f : .758f);
        }

        private static GameObject CreateEmptyState(Transform parent, string iconName, string title, string hint)
        {
            var root = ExpeditionUi.Rect(parent, "ExpeditionEmptyState", .12f, .24f, .88f, .78f);
            var icon = ExpeditionUi.Rect(root, "Icon", .37f, .52f, .63f, .98f).gameObject.AddComponent<Image>();
            icon.sprite = ExpeditionTheme.Icon(iconName); icon.preserveAspect = true; icon.raycastTarget = false;
            icon.color = new Color(.64f, .70f, .69f, .66f); icon.enabled = icon.sprite != null;
            var heading = ExpeditionUi.Text(root, "Title", title, 36, .01f, .28f, .99f, .52f, TextAlignmentOptions.Center);
            heading.color = ExpeditionTheme.Ivory;
            var description = ExpeditionUi.Text(root, "Hint", hint, 25, .02f, .015f, .98f, .29f, TextAlignmentOptions.Center);
            description.color = ExpeditionTheme.Muted;
            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        public static void SetFriendsEmpty(Transform root, bool online, bool empty)
        {
            var adapter = root != null ? root.GetComponent<ExpeditionMetaPanels>() : null;
            var state = adapter != null ? online ? adapter._onlineEmpty : adapter._friendsEmpty : null;
            if (state != null) state.SetActive(empty);
        }

        public static void SetLeaderboardEmpty(Transform root, bool empty)
        {
            var adapter = root != null ? root.GetComponent<ExpeditionMetaPanels>() : null;
            if (adapter != null && adapter._ratingEmpty != null) adapter._ratingEmpty.SetActive(empty);
        }

        private void BuildFriends()
        {
            _sheet = transform.Find("FriendsSheet") as RectTransform;
            if (_sheet == null) return;
            _pageKind = 1;
            StylePageScene("friends-backdrop", "hub-backdrop");
            StyleSheetHeader(_sheet, "Друзья", "");
            var header = _sheet.Find("Header") as RectTransform;
            if (header != null) ExpeditionUi.SetRect(header, .025f, .675f, .58f, .745f);
            BuildTabs(_sheet.Find("TabBar") as RectTransform, new[] { "TabFriends", "TabOnline" }, new[] { "Друзья", "Кто в сети" });
            Place(_sheet, "TabBar", .025f, .602f, .975f, .667f);
            var board = ExpeditionUi.Panel(_sheet, "ExpeditionFriendsBoard", .018f, .012f, .982f, .597f, Color.white);
            board.raycastTarget = false;
            // The board belongs behind the two original pages and their real toolbar/actions.
            board.transform.SetSiblingIndex(1);
            foreach (var pageName in new[] { "FriendsPage", "OnlinePage" })
            {
                var page = _sheet.Find(pageName) as RectTransform;
                if (page == null) continue;
                ExpeditionUi.SetRect(page, .035f, .025f, .965f, .587f);
                var toolbar = page.Find("Toolbar") as RectTransform;
                if (toolbar != null)
                {
                    DisableLayout(toolbar); ExpeditionUi.SetRect(toolbar, .02f, .89f, .98f, .985f);
                    var button = toolbar.GetComponentInChildren<Button>(true);
                    if (button != null)
                    {
                        ExpeditionUi.SetRect(button.transform as RectTransform, .49f, .08f, .99f, .92f);
                        ExpeditionUi.StyleButton(button, true, 31);
                        ExpeditionUi.Label(button, pageName == "FriendsPage" ? "+ Добавить друга" : "Обновить список");
                    }
                }
                var scroll = page.GetComponentInChildren<ScrollRect>(true);
                if (scroll != null)
                {
                    ExpeditionUi.SetRect(scroll.transform as RectTransform, .005f, pageName == "OnlinePage" ? .14f : .08f, .995f, .88f);
                    StyleScrollSurface(scroll);
                    var empty = CreateEmptyState(scroll.viewport != null ? scroll.viewport : scroll.transform,
                        pageName == "FriendsPage" ? "hero" : "friends",
                        pageName == "FriendsPage" ? "Нет друзей" : "Пока никого нет в сети",
                        pageName == "FriendsPage" ? "Добавьте друга по имени, чтобы отправлять приглашения." : "Нажмите «Обновить список», чтобы проверить снова.");
                    if (pageName == "FriendsPage") _friendsEmpty = empty; else _onlineEmpty = empty;
                    var stack = scroll.content != null ? scroll.content.GetComponent<VerticalLayoutGroup>() : null;
                    if (stack != null) { stack.spacing = 12; stack.padding = new RectOffset(8, 8, 10, 10); }
                }
                foreach (var label in page.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.name != "StatusText" && label.name != "FooterText") continue;
                    Text(label, 26, ExpeditionTheme.Muted);
                    var onlineStatus = pageName == "OnlinePage" && label.name == "StatusText";
                    ExpeditionUi.SetRect(label.rectTransform, .035f, onlineStatus ? .075f : .005f, .965f, onlineStatus ? .135f : .075f);
                }
            }
            foreach (var popupName in new[] { "FriendsSheet/AddFriendOverlay", "FriendsActionPopup", "FriendsRemoveConfirm" })
            { var popup = transform.Find(popupName); if (popup != null) StylePopup(popup); }
            _built = true;
        }

        private void BuildLeaderboard()
        {
            _sheet = transform.Find("LeaderboardSheet") as RectTransform;
            if (_sheet == null) return;
            _pageKind = 2;
            StylePageScene(null, "arena-backdrop");
            StyleSheetHeader(_sheet, "Рейтинг", "");
            Place(_sheet, "Header", .025f, .918f, .70f, .99f);
            BuildTabs(_sheet.Find("PeriodTabs") as RectTransform,
                new[] { "TabDay", "TabWeek", "TabMonth", "TabAllTime" }, new[] { "День", "Неделя", "Месяц", "Всё время" });
            Place(_sheet, "PeriodTabs", .03f, .848f, .97f, .914f);
            var filters = _sheet.Find("FilterBar") as RectTransform;
            if (filters != null)
            {
                DisableLayout(filters); ExpeditionUi.SetRect(filters, .04f, .771f, .96f, .836f);
                Place(filters, "TypeFilter", 0, 0, .485f, 1); Place(filters, "ViewFilter", .515f, 0, 1, 1);
                foreach (var button in filters.GetComponentsInChildren<Button>(true))
                {
                    ExpeditionUi.StyleButton(button, false, 28);
                    foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (label.name == "Chevron")
                        { Text(label, 20, ExpeditionTheme.Ivory); ExpeditionUi.SetRect(label.rectTransform, .9f, .15f, .98f, .85f); continue; }
                        var heading = label.name == "Label" || label.name == "HeaderLabel";
                        Text(label, heading ? 18 : 29, heading ? ExpeditionTheme.Muted : ExpeditionTheme.Ivory);
                        ExpeditionUi.SetRect(label.rectTransform, .035f, heading ? .66f : .04f, .89f, heading ? .98f : .71f);
                    }
                }
            }
            // The shared world supplies the backdrop; no podium figures or local scene banner.
            var oldPodium = _sheet.Find("ExpeditionLeaderboardPodium");
            if (oldPodium != null) oldPodium.gameObject.SetActive(false);
            var rewards = _sheet.Find("RewardsBar") as RectTransform;
            if (rewards != null)
            {
                DisableLayout(rewards); ExpeditionUi.SetRect(rewards, .035f, .660f, .965f, .752f);
                var tiers = rewards.Find("Tiers") as RectTransform;
                if (tiers != null)
                {
                    DisableLayout(tiers); ExpeditionUi.SetRect(tiers, .015f, .02f, .985f, .76f);
                    var count = tiers.childCount;
                    for (var i = 0; i < count; i++)
                    {
                        var tier = tiers.GetChild(i) as RectTransform;
                        ExpeditionUi.SetRect(tier, i / (float)count + .01f, 0, (i + 1) / (float)count - .01f, 1);
                        var medal = tier.Find("Medal"); if (medal != null) medal.gameObject.SetActive(false);
                        var line = 0;
                        foreach (var label in tier.GetComponentsInChildren<TMP_Text>(true))
                        { Text(label, 21, ExpeditionTheme.Ivory); ExpeditionUi.SetRect(label.rectTransform, .02f, .66f - line * .32f, .98f, .98f - line * .32f); line++; }
                    }
                }
                var title = rewards.Find("Title")?.GetComponent<TMP_Text>();
                if (title != null) { Text(title, 22, ExpeditionTheme.Ivory); ExpeditionUi.SetRect(title.rectTransform, .03f, .76f, .97f, .99f); }
            }
            var listArea = _sheet.Find("ListArea") as RectTransform;
            if (listArea != null)
            {
                ExpeditionUi.SetRect(listArea, .025f, .119f, .975f, .652f);
                var scroll = listArea.GetComponentInChildren<ScrollRect>(true);
                if (scroll != null)
                {
                    ExpeditionUi.SetRect(scroll.transform as RectTransform, 0, 0, 1, 1); scroll.horizontal = false;
                    StyleScrollSurface(scroll);
                    _ratingEmpty = CreateEmptyState(scroll.viewport != null ? scroll.viewport : scroll.transform,
                        "swords", "Пока нет участников", "Выберите другой период или станьте первым в этом рейтинге.");
                    var stack = scroll.content != null ? scroll.content.GetComponent<VerticalLayoutGroup>() : null;
                    if (stack != null) { stack.spacing = 12; stack.padding = new RectOffset(12, 12, 8, 12); }
                }
            }
            var ownRow = _sheet.Find("StickyPlayerRow") as RectTransform;
            if (ownRow != null) { ExpeditionUi.SetRect(ownRow, .027f, .036f, .973f, .112f); ApplyLeaderboardRow(ownRow.GetComponent<LeaderboardRowView>()); }
            var status = _sheet.Find("StatusText")?.GetComponent<TMP_Text>();
            if (status != null) { Text(status, 25, ExpeditionTheme.Muted); ExpeditionUi.SetRect(status.rectTransform, .05f, .002f, .95f, .03f); }
            _built = true;
        }
        private void BuildSettings(RectTransform panel)
        {
            DisableLayout(panel);
            StyleOpaqueSurface(panel, ExpeditionTheme.Panel);
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true)) Text(label, 32, ExpeditionTheme.Ivory);
            Place(panel, "Title", .06f, .91f, .82f, .98f);
            var title = panel.Find("Title")?.GetComponent<TMP_Text>();
            if (title != null) { title.text = "АККАУНТ"; Text(title, 44, ExpeditionTheme.Ivory); }
            Place(panel, "Status", .065f, .765f, .935f, .89f);
            Place(panel, "LinkedEmailLine", .065f, .68f, .935f, .765f);
            Place(panel, "EmailInputSlot", .065f, .575f, .935f, .64f);
            Place(panel, "PasswordInputSlot", .065f, .455f, .935f, .52f);
            var emailLabel = ExpeditionUi.Text(panel, "ExpeditionEmailCaption", "E-MAIL", 23, .065f, .642f, .935f, .675f);
            emailLabel.color = ExpeditionTheme.Muted;
            var passwordLabel = ExpeditionUi.Text(panel, "ExpeditionPasswordCaption", "ПАРОЛЬ", 23, .065f, .522f, .935f, .555f);
            passwordLabel.color = ExpeditionTheme.Muted;
            Place(panel, "LinkButton", .065f, .35f, .935f, .425f);
            Place(panel, "LoginButton", .065f, .252f, .935f, .327f);
            Place(panel, "LogoutEmailButton", .065f, .154f, .935f, .229f);
            Place(panel, "DeleteAccountButton", .065f, .047f, .935f, .105f);
            Place(panel, "CloseButton", .855f, .922f, .955f, .982f);
            foreach (var input in panel.GetComponentsInChildren<InputField>(true)) StyleInput(input);
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
            {
                ExpeditionUi.StyleButton(button, button.name == "LinkButton", button.name == "DeleteAccountButton" ? 28 : 34);
                if (button.name == "CloseButton") ExpeditionUi.Label(button, "×");
                if (button.name == "LogoutEmailButton") ExpeditionUi.Label(button, "Выйти из аккаунта");
                if (button.name == "DeleteAccountButton")
                {
                    var colors = button.colors;
                    colors.normalColor = new Color32(103, 57, 54, 255);
                    button.colors = colors;
                }
            }
            _built = true;
        }

        private void BuildEnergy(UnityAction close)
        {
            _sheet = transform.Find("Panel") as RectTransform;
            if (_sheet == null) return;
            DisableLayout(_sheet);
            StyleOpaqueSurface(_sheet, ExpeditionTheme.Panel);
            Place(_sheet, "Title", .055f, .885f, .84f, .97f);
            var title = _sheet.Find("Title")?.GetComponent<Text>();
            if (title != null) { title.text = "ЗАПАС ЭНЕРГИИ"; LegacyText(title, 42, ExpeditionTheme.Ivory); }
            ExpeditionUi.Button(_sheet, "ExpeditionCloseEnergy", "×", .86f, .89f, .96f, .975f, close, false, 42);
            var choice = _sheet.Find("ChoiceBlock") as RectTransform;
            if (choice != null)
            {
                DisableLayout(choice);
                ExpeditionUi.SetRect(choice, .045f, .125f, .955f, .85f);
                var rows = choice.GetComponentsInChildren<Button>(true);
                var rowIndex = 0;
                foreach (var button in rows)
                {
                    if (button.transform.parent != choice) continue;
                    var row = button.transform as RectTransform;
                    DisableLayout(row);
                    ExpeditionUi.SetRect(row, 0, rowIndex == 0 ? .53f : .025f, 1, rowIndex == 0 ? .99f : .485f);
                    ExpeditionUi.StyleButton(button, false, 36);
                    var stepper = row.Find("Stepper") as RectTransform;
                    if (stepper != null)
                    {
                        ExpeditionUi.SetRect(stepper, .025f, .15f, .175f, .84f);
                        foreach (var step in stepper.GetComponentsInChildren<Button>(true)) ExpeditionUi.StyleButton(step, false, 38);
                    }
                    Place(row, "Cost", .29f, .4f, .5f, .76f);
                    Place(row, "Eq", .51f, .4f, .57f, .76f);
                    Place(row, "Gr", .72f, .4f, .95f, .76f);
                    var iconIndex = 0;
                    for (var i = 0; i < row.childCount; i++)
                    {
                        var child = row.GetChild(i);
                        if (child.name != "Ic") continue;
                        ExpeditionUi.SetRect(child as RectTransform, iconIndex == 0 ? .2f : .625f, .43f, iconIndex == 0 ? .29f : .715f, .74f);
                        iconIndex++;
                    }
                    var caption = ExpeditionUi.Text(row, "ExpeditionOfferCaption", rowIndex == 0 ? "ЗА МАТЕРИЮ" : "ЗА ЗОЛОТО", 23, .2f, .77f, .95f, .94f);
                    caption.color = ExpeditionTheme.Muted;
                    var hint = ExpeditionUi.Text(row, "ExpeditionOfferAction", "ВЫБРАТЬ ОБМЕН  ›", 27, .22f, .08f, .95f, .29f, TextAlignmentOptions.Center);
                    hint.color = ExpeditionTheme.Amber;
                    rowIndex++;
                }
            }
            var confirm = _sheet.Find("Confirm") as RectTransform;
            if (confirm != null)
            {
                DisableLayout(confirm);
                ExpeditionUi.SetRect(confirm, .06f, .15f, .94f, .83f);
                Place(confirm, "Q", .03f, .45f, .97f, .93f);
                var question = confirm.Find("Q")?.GetComponent<Text>();
                if (question != null) { LegacyText(question, 40, ExpeditionTheme.Ivory); question.horizontalOverflow = HorizontalWrapMode.Wrap; }
                Place(confirm, "YesNo", .03f, .12f, .97f, .34f);
                foreach (var button in confirm.GetComponentsInChildren<Button>(true)) ExpeditionUi.StyleButton(button, button.name == "Да", 38);
            }
            var footer = ExpeditionUi.Text(_sheet, "ExpeditionEnergyHint", "КНОПКИ + / − МЕНЯЮТ КОЛИЧЕСТВО ПАКЕТОВ", 22, .06f, .03f, .94f, .09f, TextAlignmentOptions.Center);
            footer.color = ExpeditionTheme.Muted;
            _built = true;
        }

        public static void ApplyFriendRow(FriendsPlayerRowView row)
        {
            if (row == null) return;
            var rt = row.transform as RectTransform;
            DisableLayout(rt);
            ExpeditionUi.StylePanel(row.GetComponent<Image>(), Color.white);
            var le = row.GetComponent<LayoutElement>();
            if (le != null) { le.preferredHeight = le.minHeight = 148; le.flexibleHeight = 0; }
            var portrait = row.transform.Find("ExpeditionAvatar") as RectTransform;
            if (portrait == null)
            {
                portrait = ExpeditionUi.Panel(rt, "ExpeditionAvatar", .02f, .10f, .17f, .90f, Color.white).rectTransform;
                var image = ExpeditionUi.Rect(portrait, "Portrait", .07f, .07f, .93f, .93f).gameObject.AddComponent<Image>();
                image.sprite = ExpeditionTheme.Icon("hero"); image.preserveAspect = true; image.raycastTarget = false; image.enabled = image.sprite != null;
            }
            Place(rt, "Name", .20f, .48f, .66f, .90f);
            Place(rt, "Status", .20f, .13f, .62f, .46f);
            foreach (var label in row.GetComponentsInChildren<TMP_Text>(true))
            {
                Text(label, label.name == "Name" ? 36 : label.name == "Status" ? 25 : 27, label.name == "Name" ? ExpeditionTheme.Ivory : label.color);
                if (label.name == "Name" || label.name == "Status") label.alignment = TextAlignmentOptions.MidlineLeft;
            }
            foreach (var button in row.GetComponentsInChildren<Button>(true))
            {
                var primary = button.name == "AcceptButton" || button.name == "ActionButton";
                ExpeditionUi.StyleButton(button, primary, 27);
                ExpeditionUi.SetRect(button.transform as RectTransform, button.name == "RemoveButton" ? .91f : .66f,
                    .27f, button.name == "RemoveButton" ? .98f : .90f, .76f);
                if (button.name == "ActionButton") ExpeditionUi.Label(button, "Пригласить");
                foreach (var label in button.GetComponentsInChildren<Text>(true)) LegacyText(label, 28, primary ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory);
            }
        }

        public static void ApplyLeaderboardRow(LeaderboardRowView row)
        {
            if (row != null) row.ApplyExpeditionPresentation();
        }

        public static void ApplyLeaderboardRewards(Transform rewards)
        {
            if (rewards == null) return;
            foreach (var label in rewards.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.text == "TOP 3 REWARDS") label.text = "НАГРАДЫ ПЕРВОЙ ТРОЙКЕ";
                Text(label, label.name == "Title" ? 22 : 21, ExpeditionTheme.Ivory);
            }
        }

        public static void StyleFilterPicker(Transform picker, string selectedLabel)
        {
            if (picker == null) return;
            foreach (var layout in picker.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                var rt = layout.transform as RectTransform;
                if (rt != null) { rt.anchorMin = new Vector2(.07f, .5f); rt.anchorMax = new Vector2(.93f, .5f); rt.sizeDelta = new Vector2(0, rt.sizeDelta.y); }
                layout.padding = new RectOffset(24, 24, 24, 24);
                layout.spacing = 12;
                ExpeditionUi.StylePanel(layout.GetComponent<Image>(), ExpeditionTheme.Panel);
                var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(layout.gameObject);
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                foreach (var button in layout.GetComponentsInChildren<Button>(true))
                {
                    var caption = button.GetComponentInChildren<TMP_Text>(true);
                    ExpeditionUi.StyleButton(button, caption != null && caption.text == selectedLabel, 36);
                    var le = ExpeditionUi.Ensure<LayoutElement>(button.gameObject);
                    le.preferredHeight = le.minHeight = 90;
                }
            }
        }

        public static void StylePopup(Transform overlay)
        {
            if (overlay == null) return;
            var panel = overlay.Find("Panel") as RectTransform;
            if (panel == null) return;
            StyleOpaqueSurface(panel, ExpeditionTheme.Panel);
            panel.anchorMin = new Vector2(.07f, .5f);
            panel.anchorMax = new Vector2(.93f, .5f);
            panel.sizeDelta = new Vector2(0, panel.sizeDelta.y);
            panel.anchoredPosition = Vector2.zero;
            var stack = panel.GetComponent<VerticalLayoutGroup>();
            if (stack != null) { stack.padding = new RectOffset(28, 28, 28, 28); stack.spacing = 20; stack.childForceExpandHeight = false; }
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                Text(label, label.name == "Title" ? 40 : 30, label.name == "Feedback" ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory);
                if (label.transform.parent == panel)
                {
                    var le = ExpeditionUi.Ensure<LayoutElement>(label.gameObject);
                    le.minHeight = le.preferredHeight = label.name == "Message" ? 120 : label.name == "Feedback" ? 60 : 68;
                }
            }
            foreach (var legacy in panel.GetComponentsInChildren<Text>(true))
            {
                LegacyText(legacy, 32, ExpeditionTheme.Ivory);
                if (legacy.name == "Msg")
                {
                    var le = legacy.GetComponent<LayoutElement>();
                    if (le != null) le.minHeight = le.preferredHeight = 210;
                }
            }
            foreach (var input in panel.GetComponentsInChildren<TMP_InputField>(true))
            {
                ExpeditionUi.StylePanel(input.GetComponent<Image>(), ExpeditionTheme.Teal);
                ExpeditionSystemDialogs.ApplyInputPadding(input);
                var le = input.GetComponent<LayoutElement>();
                if (le != null) le.minHeight = le.preferredHeight = 90;
            }
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
            {
                ExpeditionUi.StyleButton(button, button.name == "Confirm" || button.name == "Accept" || button.name == "Нет", 34);
                var le = ExpeditionUi.Ensure<LayoutElement>(button.gameObject);
                le.preferredHeight = le.minHeight = 80;
            }
            foreach (var row in panel.GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                var le = ExpeditionUi.Ensure<LayoutElement>(row.gameObject);
                le.minHeight = le.preferredHeight = 84;
            }
            var fitter = ExpeditionUi.Ensure<ContentSizeFitter>(panel.gameObject);
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void StyleSheetHeader(RectTransform sheet, string caption, string subcaption)
        {
            DisableLayout(sheet);
            if (_pageKind == 0) StyleOpaqueSurface(sheet, ExpeditionTheme.Panel);
            var header = sheet.Find("Header") as RectTransform;
            if (header != null)
            {
                header.gameObject.SetActive(true);
                DisableLayout(header);
                ExpeditionUi.StylePanel(ExpeditionUi.Ensure<Image>(header.gameObject), Color.white);
                ExpeditionUi.SetRect(header, .045f, .915f, .955f, .985f);
                var title = header.Find("Title")?.GetComponent<TMP_Text>();
                if (title != null) { title.text = caption; Text(title, 54, ExpeditionTheme.Ivory); ExpeditionUi.SetRect(title.rectTransform, .16f, .08f, .97f, .95f); }
                var icon = ExpeditionUi.Rect(header, "ExpeditionSectionIcon", .025f, .15f, .14f, .85f).gameObject.AddComponent<Image>();
                icon.sprite = ExpeditionTheme.Icon(_pageKind == 1 ? "friends" : "trophy"); icon.preserveAspect = true; icon.raycastTarget = false;
                icon.enabled = icon.sprite != null;
            }
            var closeRoot = sheet.Find("CloseButton");
            var close = closeRoot != null ? closeRoot.GetComponent<Button>() : null;
            if (close == null)
            {
                closeRoot = sheet.Find("Header/CloseButton");
                close = closeRoot != null ? closeRoot.GetComponent<Button>() : null;
            }
            if (close != null)
            {
                if (_pageKind != 0) { close.gameObject.SetActive(false); return; }
                close.transform.SetParent(sheet, false);
                ExpeditionUi.SetRect(close.transform as RectTransform, .865f, .938f, .96f, .985f);
                ExpeditionUi.StyleButton(close, false, 42);
                ExpeditionUi.Label(close, "×");
            }
        }

        private void BuildTabs(RectTransform bar, string[] names, string[] captions)
        {
            if (bar == null) return;
            DisableLayout(bar);
            ExpeditionUi.SetRect(bar, .04f, .848f, .96f, .903f);
            for (var i = 0; i < names.Length; i++)
            {
                var tab = bar.Find(names[i])?.GetComponent<Toggle>();
                if (tab == null) continue;
                ExpeditionUi.SetRect(tab.transform as RectTransform, i / (float)names.Length + .005f, 0, (i + 1) / (float)names.Length - .005f, 1);
                var image = tab.GetComponent<Image>();
                if (image != null) { ExpeditionUi.StylePanel(image, ExpeditionTheme.Teal); tab.targetGraphic = image; }
                tab.transition = Selectable.Transition.None;
                tab.spriteState = default;
                if (tab.graphic != null && tab.graphic != image) tab.graphic.gameObject.SetActive(false);
                tab.graphic = null;
                tab.toggleTransition = Toggle.ToggleTransition.None;
                var label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label == null) label = ExpeditionUi.Text(tab.transform, "ExpeditionPeriodCaption", captions[i], 32, .02f, .1f, .98f, .94f, TextAlignmentOptions.Center);
                label.gameObject.SetActive(true);
                label.text = captions[i];
                ExpeditionUi.SetRect(label.rectTransform, .02f, .1f, .98f, .94f);
                Text(label, names.Length == 4 ? 29 : 36, ExpeditionTheme.Ivory);
                var rule = ExpeditionUi.Panel(tab.transform, "ExpeditionActivePeriod", .06f, .035f, .94f, .095f, ExpeditionTheme.Amber);
                _tabs.Add(tab);
                _tabLabels.Add(label);
                _tabRules.Add(rule);
            }
        }

        private void RefreshTabs()
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                if (tab == null) continue;
                var active = tab.isOn;
                var group = tab.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1;
                var image = tab.targetGraphic as Image;
                if (image != null)
                {
                    image.sprite = active ? ExpeditionArt.Frame(true) : ExpeditionTheme.Plate;
                    image.overrideSprite = null; image.pixelsPerUnitMultiplier = 4f; image.color = Color.white;
                    image.CrossFadeAlpha(1f, 0, true);
                    image.canvasRenderer.SetAlpha(1f);
                }
                _tabLabels[i].color = active ? new Color32(31, 26, 17, 255) : ExpeditionTheme.Ivory;
                _tabRules[i].enabled = false;
            }
        }

        private void RefreshGeometry()
        {
            if (Application.isPlaying && _built) _geometryDirty = true;
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
            var slide = _sheet.anchoredPosition;
            if (_pageKind != 0)
            {
                if (_pageAttached) ExpeditionUi.SetRect(_sheet, 0, 0, 1, 1);
                else SetSafeBounds(_sheet, 0, .105f, 1, .922f);
            }
            else SetSafeBounds(_sheet, _sheetBounds.x, _sheetBounds.y, _sheetBounds.z, _sheetBounds.w);
            // Social panels animate their position; account forms may be lifted above the keyboard.
            _sheet.anchoredPosition = slide;
            _sheet.ForceUpdateRectTransforms();
            _lastScreenSize = dimensions;
            _lastSafeArea = safe;
            _geometryDirty = false;
            _changingGeometry = false;
        }

        private static void SetSafeBounds(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            var safe = Screen.safeArea;
            var w = Mathf.Max(1, Screen.width);
            var h = Mathf.Max(1, Screen.height);
            if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0
                || safe.xMax > w + 1 || safe.yMax > h + 1)
                safe = new Rect(0, 0, w, h);
            var safeWidth = safe.width / w;
            var safeHeight = safe.height / h;
            var min = new Vector2(Mathf.Clamp01(safe.xMin / w + safeWidth * x0), Mathf.Clamp01(safe.yMin / h + safeHeight * y0));
            var max = new Vector2(Mathf.Clamp01(safe.xMin / w + safeWidth * x1), Mathf.Clamp01(safe.yMin / h + safeHeight * y1));
            if (max.x <= min.x || max.y <= min.y) { min = new Vector2(x0, y0); max = new Vector2(x1, y1); }
            ExpeditionUi.SetRect(panel, min.x, min.y, max.x, max.y);
        }

        public static void StyleOpaqueSurface(RectTransform panel, Color color)
        {
            if (panel == null) return;
            ExpeditionUi.StylePanel(ExpeditionUi.Ensure<Image>(panel.gameObject), color);
            var fill = panel.Find("ExpeditionOpaqueFill") as RectTransform;
            if (ExpeditionArt.Frame() != null)
            { if (fill != null) fill.gameObject.SetActive(false); return; }
            if (fill == null) fill = ExpeditionUi.Rect(panel, "ExpeditionOpaqueFill", .008f, .008f, .992f, .992f);
            fill.gameObject.SetActive(true);
            fill.SetAsFirstSibling();
            ExpeditionUi.Ensure<LayoutElement>(fill.gameObject).ignoreLayout = true;
            var image = ExpeditionUi.Ensure<Image>(fill.gameObject);
            image.sprite = ModalPanelCloseButton.WhiteSprite();
            image.overrideSprite = null;
            image.type = Image.Type.Simple;
            color.a = 1f;
            image.color = color;
            image.raycastTarget = false;
        }

        public static void StyleScrollSurface(ScrollRect scroll)
        {
            if (scroll == null) return;
            StyleOpaqueSurface(scroll.transform as RectTransform, new Color32(15, 36, 43, 255));
            var viewport = scroll.viewport;
            if (viewport == null) return;
            var mask = viewport.GetComponent<Mask>();
            if (mask != null) mask.showMaskGraphic = false;
            else
            {
                ExpeditionUi.Ensure<RectMask2D>(viewport.gameObject);
                var graphic = viewport.GetComponent<Image>();
                if (graphic != null) { graphic.enabled = false; graphic.raycastTarget = false; }
            }
        }

        private static void StyleInput(InputField input)
        {
            ExpeditionUi.StylePanel(input.GetComponent<Image>(), ExpeditionTheme.Teal);
            if (input.textComponent != null) LegacyText(input.textComponent, 34, ExpeditionTheme.Ivory);
            if (input.placeholder is Text placeholder) LegacyText(placeholder, 32, ExpeditionTheme.Muted);
            ExpeditionSystemDialogs.ApplyInputPadding(input);
            var colors = input.colors;
            colors.normalColor = colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            input.colors = colors;
        }

        private static void Text(TMP_Text label, float size, Color color)
        {
            label.font = ExpeditionTheme.Font;
            label.fontStyle = FontStyles.Normal;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * .78f;
            label.fontSizeMax = size;
            label.color = color;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void LegacyText(Text label, int size, Color color)
        {
            if (ExpeditionTheme.LegacyFont != null) label.font = ExpeditionTheme.LegacyFont;
            label.fontSize = size;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.RoundToInt(size * .78f);
            label.resizeTextMaxSize = size;
            label.color = color;
            label.raycastTarget = false;
        }

        private static void Place(Transform root, string name, float x0, float y0, float x1, float y1)
        {
            var target = root.Find(name) as RectTransform;
            if (target != null) ExpeditionUi.SetRect(target, x0, y0, x1, y1);
        }

        private static void DisableLayout(RectTransform rt)
        {
            if (rt == null) return;
            var layout = rt.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = rt.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }
    }
}
