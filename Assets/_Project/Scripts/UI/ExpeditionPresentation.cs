using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Project.Achievements;
using Project.Character;
using Project.Character.UI;
using Project.Friends;
using Project.Leaderboard;
using Project.Match3;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Scene composition for the shipped Expedition presentation. Gameplay keeps its original controllers.</summary>
    [DefaultExecutionOrder(9000)]
    public sealed class ExpeditionPresentation : MonoBehaviour
    {
        private static bool _registered;
        private Scene _scene;
        private RectTransform _content;
        private Canvas _canvas;
        private RectTransform _modalHost;
        private MainMenuController _menu;
        private RectTransform _sceneBody;
        private bool _tournamentVisible;
        private bool _arenaParticipationLocked;
        private GameObject _startupCover;
        private RectTransform _characterHost;
        private CharacterScreenController _character;
        private AchievementsPanelController _achievements;
        private LeaderboardPanelController _rating;
        private FriendsPanelController _friends;
        private ExpeditionSceneWorld _world;
        private GameObject _embers;
        private readonly Button[] _navButtons = new Button[5];
        private readonly Image[] _navRules = new Image[5];
        private static bool _openHeroAfterLoad;
        private static bool _openAchievementsAfterLoad, _openSettingsAfterLoad;

        private void OnEnable()
        {
            _scene = gameObject.scene;
            Register(); // Keep editor hot reloads subscribed for the next scene as well.
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            _registered = false;
            _openHeroAfterLoad = false;
            _openAchievementsAfterLoad = _openSettingsAfterLoad = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;
            SceneManager.sceneLoaded += OnLoaded;
        }

        private static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "MainMenu" && scene.name != "ArenaMenu" && scene.name != "MineScene" && scene.name != "WorkshopScene" && scene.name != "DuelMatch3") return;
            foreach (var root in scene.GetRootGameObjects()) if (root.GetComponent<ExpeditionPresentation>() != null) return;
            var host = new GameObject("ExpeditionPresentation");
            SceneManager.MoveGameObjectToScene(host, scene);
            var presentation = host.AddComponent<ExpeditionPresentation>();
            presentation._scene = scene;
            presentation._startupCover = ExpeditionStartupCover.Show(scene);
        }

        private IEnumerator Start()
        {
            yield return null; // Original Awake/Start bindings, including modal factories, must settle first.
            if (_scene.name == "DuelMatch3")
            {
                ExpeditionBattleLayout.Install(_scene);
                if (_startupCover != null) Destroy(_startupCover);
                yield break;
            }
            _menu = InScene<MainMenuController>();
            _canvas = NewCanvas("ExpeditionUi", 70);
            _content = ExpeditionUi.Rect(_canvas.transform, "ExpeditionSafeArea", 0, 0, 1, 1);
            _content.gameObject.AddComponent<ExpeditionSafeArea>();
            _modalHost = NewCanvas("ExpeditionModalHost", 250).transform as RectTransform;
            var waitForInitialHydration = _scene.name == "MainMenu" && !ExpeditionResourceHeader.AccountHasHydratedThisSession;
            var header = _content.gameObject.AddComponent<ExpeditionResourceHeader>();
            header.Build(_content, _modalHost, _menu, _scene.name == "MainMenu");
            _sceneBody = ExpeditionUi.Rect(_content, "ExpeditionScenePageHost", 0, 0, 1, 1);
            _characterHost = ExpeditionUi.Rect(_content, "ExpeditionCharacterPageHost", 0, .105f, 1, .922f);
            var achievementsHost = ExpeditionUi.Rect(_content, "ExpeditionAchievementsPageHost", 0, .105f, 1, .922f);
            var ratingHost = ExpeditionUi.Rect(_content, "ExpeditionRatingPageHost", 0, .105f, 1, .922f);
            var friendsHost = ExpeditionUi.Rect(_content, "ExpeditionFriendsPageHost", 0, .105f, 1, .922f);

            if (_scene.name == "MainMenu") BuildShelter();
            else if (_scene.name == "ArenaMenu") BuildArena();
            else BuildSceneBack();
            _character = InScene<CharacterScreenController>();
            if (_character != null)
            {
                _character.AttachToShell(_characterHost);
                var heroFigure = ExpeditionUi.Find(_characterHost, "ExpeditionHero") as RectTransform;
                if (_world != null && heroFigure != null) _world.Ground("character-backdrop", heroFigure, new Vector2(.5f, .015f), new Vector2(.5f, .48f));
                _character.VisibilityChanged += CharacterVisibility;
                if (_openHeroAfterLoad) { _openHeroAfterLoad = false; _character.Open(); }
            }
            _achievements = InScene<AchievementsPanelController>();
            _rating = InScene<LeaderboardPanelController>();
            _friends = InScene<FriendsPanelController>();
            if (_achievements != null) { _achievements.AttachToShell(achievementsHost); _achievements.VisibilityChanged += AchievementsVisibility; }
            if (_rating != null) { _rating.AttachToShell(ratingHost); _rating.VisibilityChanged += RatingVisibility; }
            if (_friends != null) { _friends.AttachToShell(friendsHost); _friends.VisibilityChanged += FriendsVisibility; }
            FriendsNavigationBadge.Bind(FindButton("ExpeditionOpenFriends"), _friends);
            if (_openAchievementsAfterLoad && _achievements != null) { _openAchievementsAfterLoad = false; _achievements.Open(); }
            if (_openSettingsAfterLoad && _scene.name == "MainMenu") { _openSettingsAfterLoad = false; Invoke(FindButton("SettingsButton")); }
            _characterHost.SetAsLastSibling();
            var nav = _content.Find("ExpeditionNavigation");
            if (nav != null) nav.SetAsLastSibling();
            var top = _content.Find("ExpeditionResourceHeader");
            if (top != null) top.SetAsLastSibling();
            if (_scene.name == "ArenaMenu") _arenaParticipationLocked = ArenaMatch8Bridge.IsParticipationLocked;
            RefreshPageVisibility();
            foreach (var root in _scene.GetRootGameObjects()) ExpeditionCloseButton.StyleWithin(root.transform);
            while (waitForInitialHydration && _startupCover != null && (!header.HydratedCurrentAccount ||
                NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.InitialSetupCompleted || !ServerConnectionGuard.CanPlay))
            {
                var failed = header.HydrationFailed || NakamaBootstrap.Instance != null && NakamaBootstrap.Instance.InitialSetupFailed;
                ExpeditionStartupCover.SetLoadingState(_startupCover, failed, header.RetryHydration);
                yield return null;
            }
            if (_startupCover != null) Destroy(_startupCover);
            if (_scene.name == "MainMenu") PlayerGuidanceCoordinator.ShelterReady();
        }

        private Canvas NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(go, _scene);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            return canvas;
        }

        private void Background(string art)
        {
            if (_world == null)
            {
                var canvas = NewCanvas("ExpeditionWorld", 5);
                ExpeditionUi.Fill(canvas.transform, "WorldFill", 0, 0, 1, 1, new Color(.02f, .04f, .05f));
                _world = canvas.gameObject.AddComponent<ExpeditionSceneWorld>();
                _world.Build(canvas.transform as RectTransform);
                var embers = ExpeditionUi.Rect(canvas.transform, "ForgeEmbers", 0, 0, 1, 1).gameObject.AddComponent<ExpeditionAmbientWorld>();
                embers.raycastTarget = false;
                _embers = embers.gameObject;
            }
            _world.Show(art);
            _embers.SetActive(art == "hub-backdrop" || art == "workshop-backdrop");
        }

        private void BuildShelter()
        {
            Background("shelter-mine-light");
            var oldHud = _menu != null ? _menu.transform : null;
            var rating = FindButton("RatingButton", "LeaderboardButton", "BottomButtonRating");
            var friends = FindButton("FriendsButton", "BottomButtonFriends");
            var aura = Find("aura_btn") as RectTransform;
            if (oldHud != null)
            {
                var redirector = ExpeditionUi.Ensure<ExpeditionLegacyHudRedirector>(oldHud.gameObject);
                redirector.Configure(_modalHost);
                var group = ExpeditionUi.Ensure<CanvasGroup>(oldHud.gameObject);
                group.alpha = 0;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            var shadow = ExpeditionUi.Fill(_sceneBody, "ShelterContactShadow", .6f, .345f, .9f, .355f, new Color(0, 0, 0, .30f));
            shadow.sprite = ExpeditionCombatantFigure.ContactShadow; shadow.raycastTarget = false;
            var hero = ExpeditionUi.Rect(_sceneBody, "ShelterHero", .56f, .35f, .94f, .735f).gameObject.AddComponent<Image>();
            hero.sprite = Resources.Load<Sprite>("Expedition/Art/hero");
            CharacterAppearanceImage.Bind(hero);
            hero.preserveAspect = true;
            hero.raycastTarget = false;
            _world.PlaceShelterFigure(hero.rectTransform, new Vector2(.76f, .35f));

            BindShortcut(ExpeditionShelterShortcut.Create(_sceneBody, "ExpeditionOpenRating", "Рейтинг", "shelter-rating", .025f, .325f, () => Invoke(rating), .710f, .768f), rating);
            BindShortcut(ExpeditionShelterShortcut.Create(_sceneBody, "ExpeditionOpenFriends", "Друзья", "shelter-friends", .025f, .325f, () => Invoke(friends), .645f, .703f), friends);
            DailyShortcut("ExpeditionDailyLogin", "Награда за вход", "daily-login", .15f, .495f, false);
            DailyShortcut("ExpeditionDailyQuests", "Задания дня", "daily-quests", .515f, .86f, true);
            EventShortcut("ExpeditionOpenShop", "Магазин", "Expedition/Shop/0313/global_boss_teleport", .135f, .485f, () => ExpeditionShopPanel.Open());
            EventShortcut("ExpeditionOpenGlobalBoss", "Мировой босс", GlobalBossService.Art, .515f, .865f, () => ExpeditionGlobalBossPanel.Open(_modalHost));
            var mail = ExpeditionShelterShortcut.Create(_sceneBody, "ExpeditionOpenMail", "Почта", "shelter-mail", .025f, .325f, () => ExpeditionMailPanel.Open(_modalHost), .580f, .638f);
            ExpeditionMailBadge.Bind(mail);
            if (aura != null)
            {
                // Keep the original controller, live timer, rotation and modal callback.
                // The real Button is on aura_btn/border, not on aura_btn itself.
                aura.SetParent(_sceneBody, false);
                ExpeditionUi.SetRect(aura, .075f, .480f, .215f, .565f);
                var auraBorder = aura.Find("border") as RectTransform;
                if (auraBorder != null) ExpeditionUi.SetRect(auraBorder, .20f, .31f, .80f, .89f);
                var auraTimer = aura.Find("timer") as RectTransform;
                if (auraTimer != null)
                {
                    ExpeditionUi.SetRect(auraTimer, 0, .12f, 1, .30f);
                    foreach (var t in auraTimer.GetComponentsInChildren<TMP_Text>(true))
                    { t.font = ExpeditionTheme.Font; t.fontSize = 19; t.alignment = TextAlignmentOptions.Center; t.color = ExpeditionTheme.Ivory; t.raycastTarget = false; t.margin = Vector4.zero; }
                    foreach (var t in auraTimer.GetComponentsInChildren<Text>(true))
                    { t.font = ExpeditionTheme.LegacyFont; t.fontSize = 19; t.alignment = TextAnchor.MiddleCenter; t.color = ExpeditionTheme.Ivory; t.raycastTarget = false; }
                }
                var auraTitle = ExpeditionUi.Text(aura, "ExpeditionAuraTitle", "АУРА МИРА", 18, -.05f, -.08f, 1.05f, .12f, TextAlignmentOptions.Center);
                auraTitle.margin = Vector4.zero; auraTitle.color = ExpeditionTheme.Amber;
            }
            var online = Find("OnlinePlayersBadge");
            if (online != null)
            {
                var status = ExpeditionUi.Rect(_sceneBody, "ExpeditionOnline", .035f, .440f, .325f, .465f);
                status.gameObject.AddComponent<ExpeditionOnlineStatus>().Bind(online);
            }

            var card = ExpeditionShelterStyle.ThinFrame(ExpeditionUi.Panel(_sceneBody, "ShelterJourney", .025f, .113f, .975f, .264f));
            var preview = ExpeditionUi.Rect(card.transform, "MinePreview", .015f, .055f, .505f, .945f).gameObject.AddComponent<RawImage>();
            var mineArt = Resources.Load<Sprite>("Expedition/Art/mine-backdrop");
            preview.texture = mineArt != null ? mineArt.texture : null;
            preview.uvRect = new Rect(0, .36f, 1, .36f);
            preview.raycastTarget = false;
            var journeyTitle = ExpeditionUi.Text(card.transform, "Title", "Шахта ждёт", 43, .525f, .66f, .975f, .90f);
            journeyTitle.fontStyle = FontStyles.Bold; journeyTitle.margin = Vector4.zero;
            journeyTitle.enableAutoSizing = true; journeyTitle.fontSizeMin = 36; journeyTitle.fontSizeMax = 43;
            journeyTitle.textWrappingMode = TextWrappingModes.NoWrap;
            ExpeditionUi.Text(card.transform, "Hint", "Новые этажи и редкая руда", 26, .525f, .45f, .975f, .67f);
            PaintedIcon(card.transform, "OreReward", "ore", .53f, .07f, .69f, .46f).sprite = ExpeditionResourceIcons.Get("ore");
            var enter = ExpeditionUi.Button(card.transform, "ExpeditionOpenMine", "В ШАХТУ ›", .705f, .105f, .97f, .43f, () => Load("MineScene"), true, 31);
            ExpeditionShelterStyle.ThinFrame(enter.GetComponent<Image>());
            enter.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Bold;
            var border = ExpeditionUi.Panel(card.transform, "JourneySteelRim", 0, 0, 1, 1, new Color(1, 1, 1, 1));
            border.fillCenter = false;
            ExpeditionShelterStyle.ThinFrame(border);

            BuildNavigation();
        }

        private void DailyShortcut(string name, string title, string icon, float x0, float x1, bool quests)
        {
            var button = ExpeditionShelterStyle.Pill(_sceneBody, name, title, Resources.Load<Sprite>("Expedition/Daily/039/" + icon),
                x0, .790f, x1, .840f, () => ExpeditionDailyPanel.Open(_modalHost, quests), 29);
            button.transform.Find("Icon").name = "DailyIcon";
            if (quests) DailyQuestBadge.Bind(button);
            else ExpeditionDailyLoginBadge.Bind(button);
        }

        private void EventShortcut(string name, string title, string resource, float x0, float x1, UnityEngine.Events.UnityAction action)
        {
            var button = ExpeditionShelterStyle.Pill(_sceneBody, name, title, Resources.Load<Sprite>(resource), x0, .276f, x1, .334f, action, 32);
            button.transform.Find("Icon").name = "EventIcon";
        }

        private void WorldSign(string name, string text, string icon, float x0, float y0, float x1, float y1, UnityEngine.Events.UnityAction action)
        {
            var button = ExpeditionUi.Button(_sceneBody, name, text, x0, y0, x1, y1, action, false, 29);
            var label = button.GetComponentInChildren<TMP_Text>();
            ExpeditionUi.SetRect(label.rectTransform, .27f, .05f, .97f, .95f);
            PaintedIcon(button.transform, "SignIcon", icon, .025f, .16f, .26f, .84f);
        }

        private static Image PaintedIcon(Transform parent, string name, string key, float x0, float y0, float x1, float y1)
        {
            var image = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.sprite = ExpeditionTheme.Icon(key);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            return image;
        }

        private static void BindShortcut(Button target, Button source) => target.gameObject.AddComponent<ExpeditionShortcutBinding>().Bind(source);

        private void BuildNavigation()
        {
            var tray = ExpeditionUi.Panel(_content, "ExpeditionNavigation", .005f, .008f, .995f, .103f);
            if (_scene.name == "MainMenu") ExpeditionShelterStyle.ThinFrame(tray);
            var labels = new[] { "Убежище", "Достижения", "Герой", "Арена", "Мастерская" };
            var icons = new[] { "home", "", "hero", "swords", "anvil" };
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var button = ExpeditionUi.Button(tray.transform, "ExpeditionNav" + i, labels[i], i / 5f + .006f, .04f, (i + 1) / 5f - .006f, .96f, () =>
                {
                    if (index == 0) { if (_scene.name == "MainMenu") ClosePages(); else Load("MainMenu"); }
                    else if (index == 1) ShowAchievements();
                    else if (index == 2) ShowCharacter();
                    else if (index == 3) Load("ArenaMenu");
                    else if (PlayerGuidanceService.WorkshopUnlocked) Load("WorkshopScene");
                }, false, 28);
                _navButtons[i] = button;
                if (_scene.name == "MainMenu") ExpeditionShelterStyle.ThinFrame(button.GetComponent<Image>());
                if (i == 4) ProgressiveContentButton.Bind(button, true);
                if (i == 1) AchievementNavigationBadge.Bind(button);
                var text = button.GetComponentInChildren<TMP_Text>();
                ExpeditionUi.SetRect(text.rectTransform, .01f, .105f, .99f, .37f);
                text.margin = new Vector4(1, 0, 1, 0);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.enableAutoSizing = true;
                text.fontSizeMin = 16;
                text.fontSizeMax = i == 1 || i == 4 ? 24 : 28;
                text.margin = Vector4.zero;
                if (i == 1 && ExpeditionArt.Icon("trophy") == null)
                {
                    var award = ExpeditionUi.Rect(button.transform, "Icon", .20f, .38f, .80f, .91f).gameObject.AddComponent<ExpeditionNavigationIcon>();
                    award.Kind = 5; award.color = ExpeditionTheme.Amber; award.raycastTarget = false;
                }
                else PaintedIcon(button.transform, "Icon", i == 1 ? "trophy" : icons[i], .20f, .38f, .80f, .91f);
                var rule = ExpeditionUi.Rect(button.transform, "ActiveAmberRim", .12f, .025f, .88f, .047f).gameObject.AddComponent<Image>();
                rule.raycastTarget = false;
                rule.color = ExpeditionTheme.Amber;
                _navRules[i] = rule;
            }
            SetNavigation(_scene.name == "MineScene" ? -1 : _scene.name == "WorkshopScene" ? 4 : _scene.name == "ArenaMenu" ? 3 : 0);
        }

        private void ShowAchievements()
        {
            if (ArenaMatch8Bridge.IsParticipationLocked) return;
            if (_achievements != null) _achievements.Open();
            else { _openAchievementsAfterLoad = true; Load("MainMenu"); }
        }
        public static void OpenSettings()
        {
            if (ArenaMatch8Bridge.IsParticipationLocked) return;
            var presentation = UnityEngine.Object.FindFirstObjectByType<ExpeditionPresentation>();
            if (presentation == null) return;
            if (presentation._scene.name == "MainMenu") Invoke(presentation.FindButton("SettingsButton"));
            else { _openSettingsAfterLoad = true; Load("MainMenu"); }
        }

        private void ShowCharacter()
        {
            if (ArenaMatch8Bridge.IsParticipationLocked) return;
            if (_character != null) _character.Open();
            else { _openHeroAfterLoad = true; Load("MainMenu"); }
        }
        public static void OpenCharacter()
        {
            var presentation = UnityEngine.Object.FindFirstObjectByType<ExpeditionPresentation>();
            if (presentation != null) presentation.ShowCharacter();
        }
        private void CharacterVisibility(bool visible)
        {
            if (visible) { _achievements?.Close(); _rating?.Close(); _friends?.Close(); }
            RefreshPageVisibility();
        }
        private void AchievementsVisibility(bool visible) { if (visible) { _character?.Close(); _rating?.Close(); _friends?.Close(); } RefreshPageVisibility(); }
        private void RatingVisibility(bool visible) { if (visible) { _character?.Close(); _achievements?.Close(); _friends?.Close(); } RefreshPageVisibility(); }
        private void FriendsVisibility(bool visible) { if (visible) { _character?.Close(); _achievements?.Close(); _rating?.Close(); } RefreshPageVisibility(); }
        private void ClosePages() { _character?.Close(); _achievements?.Close(); _rating?.Close(); _friends?.Close(); }
        /// <summary>Hide scene menu content while its separate tournament overlay is visible.</summary>
        public static void SetTournamentVisible(bool visible)
        {
            var activeScene = SceneManager.GetActiveScene();
            foreach (var root in activeScene.GetRootGameObjects())
            {
                var presentation = root.GetComponent<ExpeditionPresentation>();
                if (presentation == null) continue;
                if (presentation._tournamentVisible == visible) return;
                presentation._tournamentVisible = visible;
                presentation.RefreshPageVisibility();
                return;
            }
        }

        /// <summary>Only the tournament-owned exit/bracket controls remain available during participation.</summary>
        public static void SetArenaParticipationLocked(bool locked)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var presentation = root.GetComponent<ExpeditionPresentation>();
                if (presentation == null) continue;
                presentation._arenaParticipationLocked = locked;
                presentation.RefreshPageVisibility();
                return;
            }
        }

        private void RefreshPageVisibility()
        {
            var hero = _character != null && _character.IsVisible;
            var any = hero || (_achievements != null && _achievements.IsVisible) || (_rating != null && _rating.IsVisible) || (_friends != null && _friends.IsVisible);
            if (_sceneBody != null) _sceneBody.gameObject.SetActive(!any && !_tournamentVisible && !_arenaParticipationLocked);
            if (_content != null)
            {
                _content.GetComponent<ExpeditionResourceHeader>()?.SetShelterExpanded(!any && !_tournamentVisible && !_arenaParticipationLocked);
                foreach (var name in new[] { "ExpeditionResourceHeader", "ExpeditionNavigation" })
                {
                    var chrome = _content.Find(name);
                    if (chrome == null) continue;
                    var group = ExpeditionUi.Ensure<CanvasGroup>(chrome.gameObject);
                    group.alpha = _arenaParticipationLocked ? 0 : 1;
                    group.interactable = group.blocksRaycasts = !_arenaParticipationLocked;
                }
            }
            Background(hero ? "character-backdrop" : _achievements != null && _achievements.IsVisible ? "achievements-backdrop" :
                _rating != null && _rating.IsVisible ? "arena-backdrop" : _friends != null && _friends.IsVisible ? "friends-backdrop" :
                _scene.name == "MainMenu" ? "shelter-mine-light" : _scene.name == "MineScene" ? "mine-backdrop" : _scene.name == "WorkshopScene" ? "workshop-backdrop" : "arena-backdrop");
            SetNavigation(hero ? 2 : _achievements != null && _achievements.IsVisible ? 1 : _scene.name == "MineScene" ? -1 : _scene.name == "WorkshopScene" ? 4 : _scene.name == "ArenaMenu" ? 3 : 0);
        }
        private void SetNavigation(int active)
        {
            for (var i = 0; i < _navButtons.Length; i++)
            {
                if (_navButtons[i] == null) continue;
                var label = _navButtons[i].GetComponentInChildren<TMP_Text>();
                if (label != null) label.color = i == active ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                if (_navRules[i] != null) _navRules[i].enabled = i == active;
            }
        }
        private void OnDestroy()
        {
            if (_character != null) _character.VisibilityChanged -= CharacterVisibility;
            if (_achievements != null) _achievements.VisibilityChanged -= AchievementsVisibility;
            if (_rating != null) _rating.VisibilityChanged -= RatingVisibility;
            if (_friends != null) _friends.VisibilityChanged -= FriendsVisibility;
        }

        private void BuildSceneBack()
        {
            Background(_scene.name == "MineScene" ? "mine-backdrop" : "workshop-backdrop");
            var oldBackground = Find(_scene.name == "MineScene" ? "MineBackground" : "WorkshopBackground")?.GetComponent<Image>();
            if (oldBackground != null)
            {
                oldBackground.enabled = false; oldBackground.raycastTarget = false;
                // Overlay worlds must share the render mode with scene UI; overlay
                // canvases otherwise cover camera-space UI regardless of sortingOrder.
                var sceneCanvas = oldBackground.GetComponentInParent<Canvas>();
                if (sceneCanvas != null)
                {
                    sceneCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    sceneCanvas.worldCamera = null;
                    sceneCanvas.sortingOrder = 20;
                }
            }
            // The mine and workshop presenters own their complete content area.
            foreach (var button in SceneObjects<Button>())
                if (button.name == "BackButton") button.gameObject.SetActive(false);
            BuildNavigation();
        }

        private void BuildArena()
        {
            Background("arena-backdrop");
            var mode = Find("ModePanel") as RectTransform;
            if (mode == null) return;
            var buttons = new[] { FindButton("match3Button"), FindButton("match3ProButton"), FindButton("match3Arena_Race"), FindButton("match3Arena"), FindButton("match3Arena_Ore"), FindButton("match3Arena_Gold") };
            mode.SetParent(_sceneBody, false);
            ExpeditionUi.SetRect(mode, .035f, .265f, .965f, .704f);
            var list = ExpeditionUi.Rect(mode, "ExpeditionModeList", 0, 0, 1, 1);
            var titles = new[] { "КЛАССИКА", "PRO", "СПУСК", "ТУРНИР КУЗНЕЦА", "ТУРНИР РУДЫ", "ТУРНИР ЗОЛОТА" };
            var notes = new[] { "Равные характеристики · решает мастерство", "Уровень и экипировка участвуют в бою", "Кто первым наберёт 200 маны", "От 10 слитков · слитки и новые рецепты", "8 участников · ценные материалы", "8 участников · золотой приз" };
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null) continue;
                button.transform.SetParent(list, false);
                var column = i % 2;
                var row = i / 2;
                ExpeditionUi.SetRect(button.transform as RectTransform, column * .5f + .006f, 1f - (row + 1) / 3f + .009f, (column + 1) * .5f - .006f, 1f - row / 3f - .009f);
                foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) if (graphic.transform != button.transform) graphic.enabled = false;
                ExpeditionUi.StyleButton(button, false, 35);
                PaintedIcon(button.transform, "ExpeditionModeIcon", i == 0 || i == 1 ? "swords" : i == 2 ? "pickaxe" : i == 3 ? "anvil" : i == 4 ? "ore" : "gold", .045f, .46f, .24f, .89f);
                if (i == 3) ArenaMatch8Bridge.ApplySmithTournamentButtonIcons(button.transform);
                if (i >= 3) ProgressiveContentButton.Bind(button, false);
                GameplayConnectionButton.Bind(button);
                ExpeditionUi.Text(button.transform, "ExpeditionModeTitle", titles[i], i < 3 ? 38 : 29, .25f, .48f, .95f, .9f).fontStyle = FontStyles.Bold;
                var note = ExpeditionUi.Text(button.transform, "ExpeditionModeDescription", notes[i], 28, .045f, .06f, .95f, .47f);
                note.color = ExpeditionTheme.Muted;
            }
            for (var i = mode.childCount - 1; i >= 0; i--)
                if (mode.GetChild(i) != list) mode.GetChild(i).gameObject.SetActive(false);
            var modeImage = mode.GetComponent<Image>();
            if (modeImage != null) { modeImage.color = Color.clear; modeImage.raycastTarget = false; }

            ExpeditionUi.Text(_sceneBody, "ArenaTitle", "Арена", 62, .055f, .828f, .94f, .888f).fontStyle = FontStyles.Bold;
            var caption = ExpeditionUi.Text(_sceneBody, "ArenaSubtitle", "Выбери свой способ победить", 30, .055f, .79f, .94f, .83f);
            caption.color = ExpeditionTheme.Ivory;
            var queue = Find("queue") as RectTransform;
            if (queue != null)
            {
                queue.SetParent(_sceneBody, false);
                ExpeditionUi.SetRect(queue, .045f, .119f, .955f, .16f);
                var queueText = queue.GetComponent<TMP_Text>();
                if (queueText != null) { queueText.font = ExpeditionTheme.Font; queueText.fontSize = 32; queueText.color = ExpeditionTheme.Ivory; }
            }
            var oldBack = FindButton("BackButton");
            if (oldBack != null)
            {
                oldBack.gameObject.SetActive(false);
            }
            var toggle = Find("BettingToggle") as RectTransform;
            if (toggle != null)
            {
                toggle.SetParent(_sceneBody, false);
                ExpeditionUi.SetRect(toggle, .055f, .194f, .945f, .24f);
                ExpeditionUi.Ensure<ExpeditionBettingToggle>(toggle.gameObject).Build();
            }
            // Other decoration belongs to the retired world canvas; its actual modal children can still be raised.
            var oldCanvas = Find("Background2D")?.GetComponent<Canvas>();
            if (oldCanvas != null)
            {
                var redirect = oldCanvas.gameObject.AddComponent<ExpeditionLegacyHudRedirector>();
                redirect.Configure(_modalHost);
                var retired = ExpeditionUi.Ensure<CanvasGroup>(oldCanvas.gameObject);
                retired.alpha = 0;
                retired.blocksRaycasts = false;
            }
            BuildNavigation();
        }

        private Button FindButton(params string[] names)
        {
            foreach (var name in names) { var found = Find(name); if (found != null && found.TryGetComponent<Button>(out var button)) return button; }
            return null;
        }
        private Transform Find(string name)
        {
            foreach (var root in _scene.GetRootGameObjects()) { var found = ExpeditionUi.Find(root.transform, name); if (found != null) return found; }
            return null;
        }
        private T InScene<T>() where T : Component { foreach (var component in SceneObjects<T>()) return component; return null; }
        private T[] SceneObjects<T>() where T : Component
        {
            var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return Array.FindAll(all, component => component.gameObject.scene == _scene);
        }
        private static void Load(string scene)
        {
            if (!ArenaMatch8Bridge.IsParticipationLocked) ExpeditionSceneTransition.Load(scene);
        }
        private static void Invoke(Button button) { if (button != null && button.interactable) button.onClick.Invoke(); }
    }

    /// <summary>Moves modal children out of the retired HUD without touching its live gameplay bindings.</summary>
    public sealed class ExpeditionLegacyHudRedirector : MonoBehaviour
    {
        private RectTransform _host;
        private bool _dirty;
        public void Configure(RectTransform host) { _host = host; _dirty = true; MoveModals(); }
        private void OnTransformChildrenChanged() => _dirty = true;
        private void LateUpdate() { if (_dirty) MoveModals(); }
        private void MoveModals()
        {
            if (_host == null) return;
            _dirty = false;
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                var name = child.name;
                if (child.GetComponent<AchievementsPanelController>() != null || child.GetComponent<LeaderboardPanelController>() != null || child.GetComponent<FriendsPanelController>() != null || name.IndexOf("Modal", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("PanelRoot", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Toast", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0)
                    child.SetParent(_host, false);
            }
        }
    }

    /// <summary>Presentation for the existing arena preference toggle; its listeners remain intact.</summary>
    public sealed class ExpeditionBettingToggle : MonoBehaviour
    {
        private Toggle _toggle;
        private Image _track, _thumb;
        private TMP_Text _state;
        private bool _last;
        public void Build()
        {
            _toggle = GetComponent<Toggle>();
            if (_toggle == null || _track != null) return;
            var legacy = GetComponent<SwitchToggleVisual>();
            if (legacy != null) legacy.enabled = false;
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            var plate = ExpeditionUi.Ensure<Image>(gameObject);
            plate.enabled = true;
            plate.sprite = plate.overrideSprite = null;
            plate.type = Image.Type.Simple;
            plate.color = new Color32(9, 27, 31, 210);
            plate.raycastTarget = true;
            plate.canvasRenderer.cullTransparentMesh = false;
            _toggle.targetGraphic = plate;
            _toggle.graphic = null;
            _toggle.transition = Selectable.Transition.None;
            ExpeditionUi.Text(transform, "ExpeditionBettingLabel", "Ставки на бои", 30, .025f, .08f, .56f, .92f);
            _state = ExpeditionUi.Text(transform, "ExpeditionBettingState", "", 23, .55f, .08f, .78f, .92f, TextAlignmentOptions.Right);
            _state.margin = Vector4.zero;
            _track = ExpeditionUi.Fill(transform, "ExpeditionBettingTrack", .82f, .32f, .975f, .68f, ExpeditionTheme.Teal);
            _thumb = ExpeditionUi.Fill(_track.transform, "Thumb", .06f, .13f, .46f, .87f, ExpeditionTheme.Ivory);
            _track.raycastTarget = _thumb.raycastTarget = false;
            _toggle.onValueChanged.AddListener(Refresh);
            Refresh(_toggle.isOn);
        }
        private void LateUpdate()
        {
            // Some arena flows restore the saved preference with SetIsOnWithoutNotify.
            if (_toggle != null && _last != _toggle.isOn) Refresh(_toggle.isOn);
        }
        private void Refresh(bool value)
        {
            _last = value;
            if (_track == null) return;
            _track.color = value ? ExpeditionTheme.Amber : ExpeditionTheme.Teal;
            _thumb.color = value ? ExpeditionTheme.Panel : ExpeditionTheme.Ivory;
            ExpeditionUi.SetRect(_thumb.rectTransform, value ? .54f : .06f, .13f, value ? .94f : .46f, .87f);
            _state.text = value ? "ВКЛ." : "ВЫКЛ.";
            _state.color = value ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
        }
        private void OnDestroy() { if (_toggle != null) _toggle.onValueChanged.RemoveListener(Refresh); }
    }

    public sealed class ExpeditionSafeArea : MonoBehaviour
    {
        private Rect _last;
        private Vector2Int _screen;
        private void OnEnable() => Apply();
        private void Update() { if (_last != Screen.safeArea || _screen.x != Screen.width || _screen.y != Screen.height) Apply(); }
        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0 || safe.xMin < 0 || safe.yMin < 0 || safe.xMax > Screen.width || safe.yMax > Screen.height)
                safe = new Rect(0, 0, Screen.width, Screen.height);
            _last = safe;
            _screen = new Vector2Int(Screen.width, Screen.height);
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    public sealed class ExpeditionResourceHeader : MonoBehaviour
    {
        private TMP_Text _name, _energy, _gold, _ore, _level, _matter, _xpValue;
        private RectTransform _headerRoot, _profileBar, _resourceRow, _avatarRoot, _suppliesRoot, _settingsRoot, _xpTrack;
        private bool _shelterStyle, _shelterExpanded = true;
        private Vector2 _profileSize;
        public bool ShelterStyle => _shelterStyle;
#if UNITY_EDITOR
        internal Action<string> EditorResourceClickOverride;
#endif
        private TMP_Text _drawer;
        private readonly TMP_Text[] _drawerValues = new TMP_Text[4];
        private Image _xp;
        private RectTransform _modalHost;
        private MainMenuController _menu;
        private GameObject _drawerRoot;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _profileCts;
        private string _accountScope;
        private float _nextScopeCheck;
        private int _profileVersion;
        private EnergyHeaderPurchaseController _energyPurchase;
        private bool _hydrated, _hydrating, _hydrationFailed;
        private static string _hydratedAccountScope;
        public static bool AccountHasHydratedThisSession => Project.Nakama.LocalAccountCacheScope.IsCurrent(_hydratedAccountScope);
        public bool HydratedCurrentAccount => _hydrated && Project.Nakama.LocalAccountCacheScope.IsCurrent(_accountScope);
        public bool HydrationFailed => _hydrationFailed;

        public void Build(RectTransform parent, RectTransform modalHost, MainMenuController menu, bool shelterStyle = false)
        {
            _menu = menu;
            _modalHost = modalHost;
            _cts = new CancellationTokenSource();
            _shelterStyle = shelterStyle;
            if (shelterStyle) BuildShelterHeader(parent);
            else
            {
            var header = ExpeditionUi.Panel(parent, "ExpeditionResourceHeader", .014f, .929f, .986f, .995f);
            var avatar = ExpeditionUi.Button(header.transform, "ExpeditionOpenHero", "", .004f, -.035f, .135f, 1.035f, ExpeditionPresentation.OpenCharacter);
            var portrait = ExpeditionUi.Rect(avatar.transform, "Portrait", .04f, .04f, .96f, .96f).gameObject.AddComponent<Image>();
            portrait.sprite = ExpeditionTheme.Icon("hero");
            CharacterAppearanceImage.Bind(portrait, true);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            _name = ExpeditionUi.Text(header.transform, "ProfileName", "Загрузка…", 26, .146f, .53f, .315f, .88f);
            _name.margin = Vector4.zero;
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.enableAutoSizing = true;
            _name.fontSizeMin = 20;
            _name.fontSizeMax = 26;
            _level = ExpeditionUi.Text(header.transform, "Level", "Уровень —", 19, .146f, .28f, .315f, .51f);
            _level.margin = Vector4.zero;
            _level.textWrappingMode = TextWrappingModes.NoWrap;
            _level.color = ExpeditionTheme.Muted;
            var track = ExpeditionUi.Fill(header.transform, "XpTrack", .146f, .16f, .309f, .205f, new Color32(5, 19, 22, 255));
            track.gameObject.AddComponent<RectMask2D>();
            _xp = ExpeditionUi.Fill(track.transform, "XpFill", 0, 0, 0, 1, ExpeditionTheme.Amber);
            _energy = Resource(header.transform, "Энергия +", .325f, .496f, OpenEnergy);
            _gold = Resource(header.transform, "Золото", .498f, .669f, OpenDrawer);
            _ore = Resource(header.transform, "Руда", .671f, .842f, OpenDrawer);
            var supplies = ExpeditionUi.Button(header.transform, "ExpeditionOpenSupplies", "Запасы", .846f, .17f, .916f, .83f, OpenDrawer, false, 18);
            var suppliesLabel = supplies.GetComponentInChildren<TMP_Text>();
            suppliesLabel.gameObject.SetActive(false);
            var bag = ExpeditionUi.Rect(supplies.transform, "Bag", .02f, .02f, .98f, .98f).gameObject.AddComponent<Image>();
            bag.sprite = ExpeditionTheme.Icon("bag"); bag.preserveAspect = true; bag.raycastTarget = false;
            IconOnly(supplies, bag);
            var settings = ExpeditionUi.Button(header.transform, "ExpeditionOpenSettings", "", .922f, .17f, .992f, .83f, ExpeditionPresentation.OpenSettings, false, 18);
            var gearRect = ExpeditionUi.Rect(settings.transform, "SettingsIcon", .02f, .02f, .98f, .98f);
            var settingsSprite = ExpeditionArt.Icon("settings");
            if (settingsSprite != null)
            {
                var gear = gearRect.gameObject.AddComponent<Image>();
                gear.sprite = settingsSprite; gear.preserveAspect = true; gear.raycastTarget = false;
                IconOnly(settings, gear);
            }
            else
            {
                var gear = gearRect.gameObject.AddComponent<ExpeditionNavigationIcon>();
                gear.Kind = 6; gear.color = ExpeditionTheme.Ivory; gear.raycastTarget = false;
                IconOnly(settings, gear);
            }
            }
            PlayerResourcesService.CachedResourcesChanged += Refresh;
            CharacterAppearanceService.UnlockRewardsGranted += OnUnlockRewardsGranted;
            NicknameService.UsernameChanged += OnName;
            ServerConnectionGuard.Changed += OnConnectionChanged;
            _accountScope = Project.Nakama.LocalAccountCacheScope.Current;
            Refresh();
            BeginProfileRefresh();
            // MineSceneController already refreshes resources; the cache event above updates this header.
            if (_menu == null && gameObject.scene.name != "MineScene") StartCoroutine(ResourcePoll());
        }

        private void BuildShelterHeader(RectTransform parent)
        {
            _headerRoot = ExpeditionUi.Rect(parent, "ExpeditionResourceHeader", .014f, .852f, .986f, .995f);
            _profileBar = ExpeditionShelterStyle.ThinFrame(ExpeditionUi.Panel(_headerRoot, "ProfileBar", 0, .40f, 1, 1)).rectTransform;
            var avatar = ExpeditionUi.Button(_profileBar, "ExpeditionOpenHero", "", 0, 0, .16f, 1, ExpeditionPresentation.OpenCharacter);
            _avatarRoot = avatar.transform as RectTransform;
            var hit = avatar.GetComponent<Image>(); hit.sprite = null; hit.color = Color.clear;
            avatar.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
            var maskRoot = ExpeditionUi.Rect(avatar.transform, "PortraitMask", .035f, .035f, .965f, .965f);
            var disc = maskRoot.gameObject.AddComponent<ExpeditionCircleGraphic>(); disc.raycastTarget = false;
            maskRoot.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = ExpeditionUi.Fill(maskRoot, "Portrait", 0, 0, 1, 1, Color.white);
            portrait.sprite = ExpeditionTheme.Icon("hero"); portrait.preserveAspect = true;
            CharacterAppearanceImage.Bind(portrait, true);
            var rim = ExpeditionUi.Rect(avatar.transform, "PortraitRim", 0, 0, 1, 1).gameObject.AddComponent<ExpeditionCircleGraphic>();
            rim.Ring = true; rim.color = new Color32(208, 162, 83, 255); rim.raycastTarget = false;
            avatar.targetGraphic = rim;
            _name = ExpeditionUi.Text(_profileBar, "ProfileName", "Загрузка…", 39, .18f, .51f, .69f, .91f);
            _name.fontStyle = FontStyles.Bold; _name.margin = Vector4.zero;
            _name.textWrappingMode = TextWrappingModes.NoWrap; _name.enableAutoSizing = true; _name.fontSizeMin = 25; _name.fontSizeMax = 39;
            _level = ExpeditionUi.Text(_profileBar, "Level", "Уровень —", 26, .18f, .27f, .46f, .52f);
            _level.margin = Vector4.zero; _level.textWrappingMode = TextWrappingModes.NoWrap;
            _level.enableAutoSizing = true; _level.fontSizeMin = 19; _level.fontSizeMax = 26;
            _xpValue = ExpeditionUi.Text(_profileBar, "XpValue", "—", 21, .48f, .22f, .70f, .45f, TextAlignmentOptions.MidlineRight);
            _xpValue.margin = Vector4.zero; _xpValue.color = ExpeditionTheme.Muted;
            _xpValue.enableAutoSizing = true; _xpValue.fontSizeMin = 15; _xpValue.fontSizeMax = 21;
            var track = ExpeditionUi.Fill(_profileBar, "XpTrack", .18f, .14f, .69f, .205f, new Color32(5, 19, 22, 255));
            _xpTrack = track.rectTransform; track.gameObject.AddComponent<RectMask2D>();
            _xp = ExpeditionUi.Fill(track.transform, "XpFill", 0, 0, 0, 1, ExpeditionTheme.Amber);
            var supplies = SquareIcon(_profileBar, "ExpeditionOpenSupplies", "bag", OpenDrawer);
            _suppliesRoot = supplies.transform as RectTransform;
            var settings = SquareIcon(_profileBar, "ExpeditionOpenSettings", "settings", ExpeditionPresentation.OpenSettings);
            _settingsRoot = settings.transform as RectTransform;
            _resourceRow = ExpeditionUi.Rect(_headerRoot, "ResourceRow", 0, 0, 1, .34f);
            _ore = ResourceCard("ore", 0); _gold = ResourceCard("gold", 1); _matter = ResourceCard("matter", 2); _energy = ResourceCard("energy", 3);
            LayoutShelterHeader();
        }

        private static Button SquareIcon(Transform parent, string name, string resource, UnityEngine.Events.UnityAction action)
        {
            var button = ExpeditionUi.Button(parent, name, "", 0, 0, 1, 1, action);
            button.GetComponentInChildren<TMP_Text>().gameObject.SetActive(false);
            ExpeditionShelterStyle.ThinFrame(button.GetComponent<Image>());
            var icon = ExpeditionUi.Fill(button.transform, "Icon", .12f, .12f, .88f, .88f, Color.white);
            icon.sprite = ExpeditionTheme.Icon(resource); icon.preserveAspect = true; icon.raycastTarget = false;
            return button;
        }

        private TMP_Text ResourceCard(string key, int index)
        {
            var cell = ExpeditionShelterStyle.ThinFrame(ExpeditionUi.Panel(_resourceRow, "Resource_" + key, index / 4f + .002f, 0, (index + 1) / 4f - .002f, 1));
            var icon = ExpeditionUi.Fill(cell.transform, "ResourceIcon", .025f, .10f, .275f, .90f, Color.white);
            icon.sprite = ExpeditionResourceIcons.Get(key); icon.preserveAspect = true;
            var value = ExpeditionUi.Text(cell.transform, "Value", "—", 30, .28f, .12f, .775f, .88f, TextAlignmentOptions.Center);
            value.margin = Vector4.zero; value.textWrappingMode = TextWrappingModes.NoWrap;
            value.enableAutoSizing = true; value.fontSizeMin = 20; value.fontSizeMax = 30;
            var plus = ExpeditionUi.Button(cell.transform, "Plus", "+", 1, .5f, 1, .5f,
                () => { if (key == "energy") OpenEnergy(); else OpenResourceShop(key); }, true, 27);
            var rect = plus.transform as RectTransform; rect.pivot = new Vector2(1, .5f); rect.sizeDelta = new Vector2(44, 44); rect.anchoredPosition = new Vector2(-12, 0);
            ExpeditionShelterStyle.ThinFrame(plus.GetComponent<Image>());
            var plusLabel = plus.GetComponentInChildren<TMP_Text>(); plusLabel.margin = Vector4.zero;
            plusLabel.enableAutoSizing = true; plusLabel.fontSizeMin = 22; plusLabel.fontSizeMax = 27;
            plusLabel.textWrappingMode = TextWrappingModes.NoWrap;
            return value;
        }

        private void OpenResourceShop(string resource)
        {
#if UNITY_EDITOR
            if (EditorResourceClickOverride != null) { EditorResourceClickOverride(resource); return; }
#endif
            var shop = UnityEngine.Object.FindFirstObjectByType<ExpeditionShopPanel>();
            if (shop == null && _modalHost != null) shop = ExpeditionShopPanel.Open(_modalHost);
            shop?.SelectTab(resource == "matter" ? "rub" : "resources");
        }

        public void SetShelterExpanded(bool expanded)
        {
            if (!_shelterStyle || _headerRoot == null || expanded == _shelterExpanded) return;
            _shelterExpanded = expanded;
            ExpeditionUi.SetRect(_headerRoot, .014f, expanded ? .852f : .929f, .986f, .995f);
            ExpeditionUi.SetRect(_profileBar, 0, expanded ? .40f : 0, 1, 1);
            _resourceRow.gameObject.SetActive(expanded);
            _profileSize = Vector2.zero; LayoutShelterHeader();
        }

        private void LateUpdate() { if (_shelterStyle) LayoutShelterHeader(); }
        private void LayoutShelterHeader()
        {
            if (_profileBar == null) return;
            var size = _profileBar.rect.size; if (size.x <= 0 || size.y <= 0 || size == _profileSize) return;
            _profileSize = size;
            var side = size.y * .82f; var gap = size.y * .11f; var pad = size.y * .07f;
            void Square(RectTransform rect, float x, float edge)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f); rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = Vector2.one * edge; rect.anchoredPosition = new Vector2(x, 0);
            }
            Square(_avatarRoot, size.y * .52f, size.y * 1.04f);
            Square(_settingsRoot, size.x - pad - side * .5f, side);
            Square(_suppliesRoot, size.x - pad - side * 1.5f - gap, side);
            var left = size.y * 1.13f / size.x; var right = (size.x - pad * 2 - side * 2 - gap) / size.x;
            ExpeditionUi.SetRect(_name.rectTransform, left, .51f, right, .91f);
            ExpeditionUi.SetRect(_level.rectTransform, left, .27f, Mathf.Lerp(left, right, .52f), .53f);
            ExpeditionUi.SetRect(_xpValue.rectTransform, Mathf.Lerp(left, right, .51f), .23f, right, .45f);
            ExpeditionUi.SetRect(_xpTrack, left, .14f, right, .205f);
        }

        private static void IconOnly(Button button, Graphic icon)
        {
            var hitArea = button.GetComponent<Image>();
            hitArea.sprite = null; hitArea.overrideSprite = null; hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            button.targetGraphic = icon;
        }

        private void Update()
        {
            if (_cts == null || Time.unscaledTime < _nextScopeCheck) return;
            _nextScopeCheck = Time.unscaledTime + .5f;
            if (SyncAccountScope()) Refresh();
        }

        private bool SyncAccountScope()
        {
            var scope = Project.Nakama.LocalAccountCacheScope.Current;
            if (string.Equals(scope, _accountScope, StringComparison.Ordinal)) return false;
            _accountScope = scope;
            _hydrated = _hydrationFailed = false;
            _name.text = "Загрузка…";
            _level.text = "Уровень —";
            if (_xpValue != null) _xpValue.text = "—";
            SetXpFraction(0);
            BeginProfileRefresh();
            return true;
        }

        private void BeginProfileRefresh()
        {
            _profileCts?.Cancel();
            _profileCts?.Dispose();
            _profileCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _hydrationFailed = false;
            _ = RefreshProfile(_profileCts.Token, _accountScope, ++_profileVersion);
        }
        public void RetryHydration()
        {
            if (_hydrating || _cts == null || _cts.IsCancellationRequested) return;
            _ = RetryInitialSetup();
            BeginProfileRefresh();
        }
        private async Task RetryInitialSetup()
        {
            try { if (NakamaBootstrap.Instance != null) await NakamaBootstrap.Instance.CompleteInitialSetupAsync(_cts.Token); }
            catch { /* The explicit loading error remains visible; the connection guard retries independently. */ }
        }
        private void OnConnectionChanged(bool available) { if (available && !HydratedCurrentAccount) RetryHydration(); }

        private bool CanApplyProfile(CancellationToken token, string scope, int version) =>
            !token.IsCancellationRequested && this != null && version == _profileVersion &&
            string.Equals(scope, _accountScope, StringComparison.Ordinal) &&
            Project.Nakama.LocalAccountCacheScope.IsCurrent(scope);

        private TMP_Text Resource(Transform parent, string title, float left, float right, UnityEngine.Events.UnityAction action)
        {
            var button = ExpeditionUi.Button(parent, "ExpeditionOpen" + title.Replace(" ", ""), "", left, .07f, right, .93f, action, false, 34);
            var label = button.GetComponentInChildren<TMP_Text>();
            label.gameObject.SetActive(false);
            var contents = ExpeditionUi.Rect(button.transform, "ResourceContents", .13f, .19f, .87f, .81f);
            var row = contents.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 6;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var icon = ExpeditionUi.Rect(contents, "ResourceIcon", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            icon.sprite = ExpeditionResourceIcons.Get(title.StartsWith("Энергия") ? "energy" : title == "Золото" ? "gold" : "ore");
            icon.preserveAspect = true; icon.raycastTarget = false;
            var iconSize = icon.gameObject.AddComponent<LayoutElement>();
            iconSize.minWidth = iconSize.preferredWidth = 34;
            iconSize.minHeight = iconSize.preferredHeight = 42;
            var value = ExpeditionUi.Text(contents, "Value", "—", 29, 0, 0, 1, 1, TextAlignmentOptions.Center);
            value.margin = Vector4.zero;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.enableAutoSizing = true;
            value.fontSizeMin = 23;
            value.fontSizeMax = 29;
            value.overflowMode = TextOverflowModes.Overflow;
            var valueSize = value.gameObject.AddComponent<LayoutElement>();
            valueSize.minWidth = 0;
            valueSize.flexibleWidth = 1;
            valueSize.minHeight = valueSize.preferredHeight = 42;
            return value;
        }

        private IEnumerator ResourcePoll()
        {
            while (true)
            {
                var refresh = RefreshResources(_cts.Token);
                while (!refresh.IsCompleted) yield return null;
                yield return new WaitForSecondsRealtime(5);
            }
        }
        private async Task RefreshResources(CancellationToken token)
        {
            try { await PlayerResourcesService.GetAsync(token); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Keep the last account-scoped cached snapshot visible while offline. */ }
        }
        private void OnUnlockRewardsGranted(int matter)
        {
            if (matter > 0 && _cts != null && !_cts.IsCancellationRequested) _ = RefreshResources(_cts.Token);
        }
        private async Task RefreshProfile(CancellationToken token, string scope, int version)
        {
            _hydrating = true;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var profileTask = CharacterProfileService.GetAsync(timeout.Token);
                var nicknameTask = NicknameService.GetStatusAsync(timeout.Token);
                var resourcesTask = PlayerResourcesService.GetAsync(timeout.Token);
                await Task.WhenAll(profileTask, nicknameTask, resourcesTask);
                if (!CanApplyProfile(token, scope, version)) return;
                var profile = profileTask.Result; var name = nicknameTask.Result; var resources = resourcesTask.Result;
                if (!TryApplyHydration(profile, name, resources, scope, version, token))
                    throw new InvalidOperationException("Initial account snapshot is incomplete.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (CanApplyProfile(token, scope, version)) _hydrationFailed = !_hydrated; }
            finally { if (version == _profileVersion) _hydrating = false; }
        }
        private bool TryApplyHydration(CharacterGetRpcResponse profile, NicknameStatusRpcResponse name,
            PlayerResourcesRpcResponse resources, string scope, int version, CancellationToken token)
        {
            if (!CanApplyProfile(token, scope, version) || profile?.ok != true || profile.progression == null ||
                name?.ok != true || string.IsNullOrWhiteSpace(name.username) || resources?.ok != true) return false;
            _level.text = "Уровень " + profile.progression.level;
            PlayerLevelXpTable.GetBarState(profile.progression.xp, out _, out var fraction, out _);
            if (_xpValue != null)
            {
                var level = Mathf.Clamp(profile.progression.level, 1, PlayerLevelXpTable.MaxLevel);
                if (level == PlayerLevelXpTable.MaxLevel) { _xpValue.text = "MAX"; fraction = 1; }
                else
                {
                    var start = PlayerLevelXpTable.Thresholds[level - 1]; var span = PlayerLevelXpTable.Thresholds[level] - start;
                    _xpValue.text = Mathf.Clamp(profile.progression.xp - start, 0, span) + " / " + span;
                }
            }
            SetXpFraction(fraction); _name.text = name.username;
            _hydrated = true; _hydrationFailed = false; _hydratedAccountScope = scope;
            RenderResources(resources);
            return true;
        }
        private void OnName(string value)
        {
            // The legacy event carries no account scope. Fetch under the current scope
            // so a queued notification from a previous login cannot overwrite this name.
            if (_cts == null || _cts.IsCancellationRequested) return;
            if (!SyncAccountScope()) BeginProfileRefresh();
        }
        private void SetXpFraction(float fraction)
        {
            if (_xp == null) return;
            ExpeditionUi.SetRect(_xp.rectTransform, 0, 0, float.IsNaN(fraction) ? 0 : Mathf.Clamp01(fraction), 1);
        }
        private void Refresh()
        {
            if (_energy == null) return;
            SyncAccountScope();
            if (!HydratedCurrentAccount || !PlayerResourcesService.TryReadCached(out var value))
            {
                RenderResources(null);
                return;
            }
            RenderResources(value);
        }
        private void RenderResources(PlayerResourcesRpcResponse value)
        {
            if (value == null)
            {
                _energy.text = _gold.text = _ore.text = "—";
                if (_matter != null) _matter.text = "—";
                if (_drawer != null) _drawer.text = "Загрузка запасов…";
                return;
            }
            _energy.text = _shelterStyle ? Short(value.energy) + " / " + value.energy_max : Short(value.energy);
            _gold.text = Short(value.gold);
            _ore.text = Short(value.ore);
            if (_matter != null) _matter.text = Short(value.matter);
            if (_drawer != null)
            {
                _drawer.text = "Слитки: " + value.ingots + "    Ключи: " + value.keys + "\nКлючи шахтёра: " + value.miner_key + "\nТёмные ключи: " + value.dark_key;
                var quantities = new long[] { value.gold, value.ore, value.matter, value.energy };
                for (var i = 0; i < quantities.Length; i++) if (_drawerValues[i] != null) _drawerValues[i].text = Short(quantities[i]);
            }
        }
        private static string Short(long value) => CompactResourceAmount.Format(value);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHydratedAccount() => _hydratedAccountScope = null;
        private void OpenDrawer()
        {
            if (_drawerRoot == null)
            {
                var scrim = ExpeditionUi.Panel(_modalHost, "ExpeditionSupplies", 0, 0, 1, 1, new Color(.025f, .07f, .08f, .92f));
                scrim.raycastTarget = true;
                _drawerRoot = scrim.gameObject;
                var panel = ExpeditionUi.Panel(scrim.transform, "SuppliesPanel", .07f, .2f, .93f, .78f);
                ExpeditionUi.Text(panel.transform, "Title", "ЗАПАСЫ", 64, .06f, .83f, .94f, .96f).fontStyle = FontStyles.Bold;
                var keys = new[] { "gold", "ore", "matter", "energy" };
                var titles = new[] { "Золото", "Руда", "Материя", "Энергия" };
                for (var i = 0; i < keys.Length; i++)
                {
                    var left = i % 2 == 0 ? .06f : .515f;
                    var bottom = i < 2 ? .62f : .415f;
                    var cell = ExpeditionUi.Panel(panel.transform, "Supply_" + keys[i], left, bottom, left + .425f, bottom + .18f);
                    var icon = ExpeditionUi.Rect(cell.transform, "Icon", .08f, .31f, .37f, .91f).gameObject.AddComponent<Image>();
                    icon.sprite = ExpeditionResourceIcons.Get(keys[i]); icon.preserveAspect = true; icon.raycastTarget = false;
                    _drawerValues[i] = ExpeditionUi.Text(cell.transform, "Value", "—", 38, .40f, .30f, .92f, .90f, TextAlignmentOptions.Center);
                    _drawerValues[i].enableAutoSizing = true; _drawerValues[i].fontSizeMin = 24; _drawerValues[i].fontSizeMax = 38;
                    ExpeditionUi.Text(cell.transform, "ResourceName", titles[i], 24, .1f, .07f, .9f, .30f, TextAlignmentOptions.Center).color = ExpeditionTheme.Muted;
                }
                _drawer = ExpeditionUi.Text(panel.transform, "Resources", "Загрузка запасов…", 28, .08f, .19f, .92f, .395f);
                _drawer.lineSpacing = 7;
                ExpeditionUi.Button(panel.transform, "ExpeditionOpenSuppliesClose", "ЗАКРЫТЬ", .06f, .045f, .94f, .17f, () => _drawerRoot.SetActive(false), false, 42);
            }
            _drawerRoot.SetActive(true);
            _drawerRoot.transform.SetAsLastSibling();
            Refresh();
        }
        private void OpenEnergy()
        {
#if UNITY_EDITOR
            if (EditorResourceClickOverride != null) { EditorResourceClickOverride("energy"); return; }
#endif
            if (_menu != null) { _menu.OpenEnergyBuyDialog(); return; }
            _energyPurchase ??= new EnergyHeaderPurchaseController(_modalHost, ExpeditionResourceIcons.Get("energy"), ExpeditionResourceIcons.Get("matter"), ExpeditionResourceIcons.Get("gold"), async token => { await PlayerResourcesService.GetAsync(token); }, _cts.Token);
            _energyPurchase.ShowPurchaseDialog();
        }
        private void OnDestroy()
        {
            PlayerResourcesService.CachedResourcesChanged -= Refresh;
            CharacterAppearanceService.UnlockRewardsGranted -= OnUnlockRewardsGranted;
            NicknameService.UsernameChanged -= OnName;
            ServerConnectionGuard.Changed -= OnConnectionChanged;
            _profileCts?.Cancel();
            _profileCts?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
