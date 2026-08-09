using System;
using System.Collections;
using System.Threading;
using Project.Achievements;
using Project.Match3;
using Project.Nakama;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Friends
{
    /// <summary>
    /// DDOL: входящие предложения «Спуск» от друзей.
    /// Prep-таймер 5 с — уже в сцене DuelMatch3 (Match3SearchingPanel).
    /// </summary>
    public sealed class FriendsRaceInviteHost : MonoBehaviour
    {
        public const long NotificationCodeInvite = 10020;
        public const long NotificationCodeUpdate = 10021;

        private static FriendsRaceInviteHost _instance;

        private Canvas _canvas;
        private GameObject _invitePanel;
        private TMP_Text _inviteTitle;
        private TMP_Text _inviteBody;
        private TMP_Text _inviteTimer;
        private Button _acceptBtn;
        private Button _declineBtn;

        private string _pendingInviteId;
        private string _pendingFromUsername;
        private long _inviteExpiresAtUnix;
        private bool _busy;
        private Coroutine _inviteTickRoutine;
        private string _launchingMatchId;

        private static readonly string[] MatchScenes = { "DuelMatch3" };

        public static FriendsRaceInviteHost Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("FriendsRaceInviteHost");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FriendsRaceInviteHost>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            BuildUi();
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (_instance == this) _instance = null;
        }

        private void OnSceneChanged(Scene prev, Scene next)
        {
            if (!IsInMatchScene(next.name)) return;
            // Вход в любой матч (PvE/PvP/Спуск) — аннулируем активные предложения, иначе accept
            // может создать второй матч, пока игрок уже в бою.
            InvalidatePendingForMatchEntry();
        }

        /// <summary>Скрыть UI и сбросить исходящие/входящие предложения на сервере.</summary>
        public static void InvalidatePendingForMatchEntry()
        {
            var host = _instance;
            if (host != null)
                host.HideInviteUi();
            _ = ClearPendingOnServerAsync();
        }

        private static async System.Threading.Tasks.Task ClearPendingOnServerAsync()
        {
            try
            {
                await FriendsService.ClearPendingRaceInvitesAsync("match_enter", CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsRace] clear on match enter: " + e.Message);
            }
        }

        public static bool IsInMatchScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            for (var i = 0; i < MatchScenes.Length; i++)
            {
                if (string.Equals(sceneName, MatchScenes[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public void HandleNotification(long code, string contentJson)
        {
            if (string.IsNullOrWhiteSpace(contentJson)) return;
            MainThreadDispatcher.Enqueue(() => HandleNotificationOnMain(code, contentJson));
        }

        private void HandleNotificationOnMain(long code, string contentJson)
        {
            if (IsInMatchScene(SceneManager.GetActiveScene().name))
                return;

            try
            {
                if (code == NotificationCodeInvite)
                {
                    var msg = JsonUtility.FromJson<RaceInviteNotif>(contentJson);
                    if (msg == null || string.IsNullOrWhiteSpace(msg.invite_id)) return;
                    ShowIncomingInvite(msg);
                }
                else if (code == NotificationCodeUpdate)
                {
                    var msg = JsonUtility.FromJson<RaceInviteUpdateNotif>(contentJson);
                    if (msg == null) return;
                    if (string.Equals(msg.status, "match_ready", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(msg.match_id))
                    {
                        HideInviteUi();
                        var myId = NakamaBootstrap.Instance?.Session?.UserId ?? "";
                        string oppId;
                        string oppName;
                        if (!string.IsNullOrEmpty(myId) && string.Equals(myId, msg.from_user_id, StringComparison.Ordinal))
                        {
                            oppId = msg.to_user_id;
                            oppName = msg.to_username;
                        }
                        else
                        {
                            oppId = msg.from_user_id;
                            oppName = msg.from_username;
                        }

                        LaunchFriendRaceMatch(
                            msg.match_id,
                            oppId,
                            oppName,
                            msg.prep_seconds > 0 ? msg.prep_seconds : 5);
                    }
                    else if (string.Equals(msg.status, "declined", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = string.IsNullOrWhiteSpace(msg.by_username) ? "друг" : msg.by_username;
                        ShowBriefStatus("Спуск", $"«{name}» не хочет");
                    }
                    else if (string.Equals(msg.status, "cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(_pendingInviteId)
                            && !string.IsNullOrEmpty(msg.invite_id)
                            && !string.Equals(_pendingInviteId, msg.invite_id, StringComparison.Ordinal))
                            return;
                        HideInviteUi();
                        // Не спамим тостом в матче — HandleNotificationOnMain уже отсекает match scene.
                        ShowBriefStatus("Спуск", "Предложение отменено");
                    }
                    else if (string.Equals(msg.status, "charge_failed", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowBriefStatus("Спуск", "Не удалось списать вход у друга");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsRace] notification parse: " + e.Message);
            }
        }

        private void ShowIncomingInvite(RaceInviteNotif msg)
        {
            _pendingInviteId = msg.invite_id;
            _pendingFromUsername = string.IsNullOrWhiteSpace(msg.from_username) ? "Друг" : msg.from_username.Trim();
            // long: float unix теряет точность (~128с) и мог мгновенно «протухать» TTL.
            var now = UnixNow();
            _inviteExpiresAtUnix = msg.expires_at > now ? msg.expires_at : (now + 60);

            if (_inviteTitle != null) _inviteTitle.text = "Спуск";
            if (_inviteBody != null)
                _inviteBody.text = $"«{_pendingFromUsername}» предлагает матч";
            if (_acceptBtn != null) _acceptBtn.gameObject.SetActive(true);
            if (_declineBtn != null) _declineBtn.gameObject.SetActive(true);
            if (_invitePanel != null) _invitePanel.SetActive(true);
            SetInviteButtonsInteractable(true);

            if (_inviteTickRoutine != null) StopCoroutine(_inviteTickRoutine);
            _inviteTickRoutine = StartCoroutine(InviteExpiryTick());
        }

        private IEnumerator InviteExpiryTick()
        {
            while (_invitePanel != null && _invitePanel.activeSelf && !string.IsNullOrEmpty(_pendingInviteId))
            {
                var left = _inviteExpiresAtUnix - UnixNow();
                if (_inviteTimer != null)
                    _inviteTimer.text = left > 0 ? $"{Mathf.CeilToInt(left)} с" : "0 с";
                if (left <= 0)
                {
                    HideInviteUi();
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private void HideInviteUi()
        {
            if (_invitePanel != null) _invitePanel.SetActive(false);
            _pendingInviteId = null;
            if (_inviteTickRoutine != null)
            {
                StopCoroutine(_inviteTickRoutine);
                _inviteTickRoutine = null;
            }
        }

        private void ShowBriefStatus(string title, string body)
        {
            if (IsInMatchScene(SceneManager.GetActiveScene().name)) return;
            if (_inviteTitle != null) _inviteTitle.text = title;
            if (_inviteBody != null) _inviteBody.text = body;
            if (_inviteTimer != null) _inviteTimer.text = string.Empty;
            if (_invitePanel != null) _invitePanel.SetActive(true);
            SetInviteButtonsInteractable(false);
            if (_acceptBtn != null) _acceptBtn.gameObject.SetActive(false);
            if (_declineBtn != null) _declineBtn.gameObject.SetActive(false);
            if (_inviteTickRoutine != null) StopCoroutine(_inviteTickRoutine);
            _inviteTickRoutine = StartCoroutine(HideInviteAfterDelay(2.8f));
        }

        private IEnumerator HideInviteAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_acceptBtn != null) _acceptBtn.gameObject.SetActive(true);
            if (_declineBtn != null) _declineBtn.gameObject.SetActive(true);
            HideInviteUi();
        }

        private async void OnAcceptClicked()
        {
            if (_busy || string.IsNullOrWhiteSpace(_pendingInviteId)) return;
            _busy = true;
            SetInviteButtonsInteractable(false);
            try
            {
                var result = await FriendsService.RespondRaceInviteAsync(_pendingInviteId, accept: true, CancellationToken.None);
                if (!result.Ok)
                {
                    if (_inviteBody != null)
                        _inviteBody.text = FriendsService.DescribeError(result.Err);
                    SetInviteButtonsInteractable(true);
                    return;
                }

                HideInviteUi();
                if (!string.IsNullOrWhiteSpace(result.MatchId))
                {
                    LaunchFriendRaceMatch(
                        result.MatchId,
                        result.OpponentUserId,
                        result.OpponentUsername,
                        result.PrepSeconds > 0 ? result.PrepSeconds : 5);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsRace] accept failed: " + e.Message);
                SetInviteButtonsInteractable(true);
            }
            finally
            {
                _busy = false;
            }
        }

        private async void OnDeclineClicked()
        {
            if (_busy || string.IsNullOrWhiteSpace(_pendingInviteId)) return;
            _busy = true;
            SetInviteButtonsInteractable(false);
            var inviteId = _pendingInviteId;
            HideInviteUi();
            try
            {
                await FriendsService.RespondRaceInviteAsync(inviteId, accept: false, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendsRace] decline failed: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void LaunchFriendRaceMatch(string matchId, string opponentUserId, string opponentUsername, int prepSeconds)
        {
            if (string.IsNullOrWhiteSpace(matchId)) return;
            if (!string.IsNullOrEmpty(_launchingMatchId)
                && string.Equals(_launchingMatchId, matchId, StringComparison.Ordinal))
                return;
            _launchingMatchId = matchId;
            var name = string.IsNullOrWhiteSpace(opponentUsername) ? "соперник" : opponentUsername.Trim();
            Match3LaunchContext.ArmFriendRaceJoin(matchId, name, opponentUserId, prepSeconds);
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
            Match3LaunchContext.SetPvpRaceForNextMultiplayerMatch(true);
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            SceneManager.LoadScene("DuelMatch3");
        }

        private void SetInviteButtonsInteractable(bool on)
        {
            if (_acceptBtn != null) _acceptBtn.interactable = on;
            if (_declineBtn != null) _declineBtn.interactable = on;
        }

        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void BuildUi()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = short.MaxValue - 20;
            gameObject.AddComponent<GraphicRaycaster>();
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _invitePanel = BuildCornerCard(
                "InviteCard",
                out _inviteTitle,
                out _inviteBody,
                out _inviteTimer,
                withButtons: true,
                out _acceptBtn,
                out _declineBtn);
            _invitePanel.SetActive(false);

            if (_acceptBtn != null) _acceptBtn.onClick.AddListener(OnAcceptClicked);
            if (_declineBtn != null) _declineBtn.onClick.AddListener(OnDeclineClicked);
        }

        private GameObject BuildCornerCard(
            string name,
            out TMP_Text title,
            out TMP_Text body,
            out TMP_Text timer,
            bool withButtons,
            out Button acceptBtn,
            out Button declineBtn)
        {
            acceptBtn = null;
            declineBtn = null;

            var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            card.transform.SetParent(transform, false);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-28f, 28f);
            rt.sizeDelta = new Vector2(460f, 0f);

            var img = card.GetComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.96f);

            var outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.55f, 0.85f, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);

            var vl = card.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(22, 22, 18, 18);
            vl.spacing = 10f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            card.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = MakeTmp(card.transform, "Title", "Спуск", 32f, FontStyles.Bold);
            title.color = new Color(0.78f, 0.88f, 1f, 1f);

            body = MakeTmp(card.transform, "Body", "", 26f, FontStyles.Normal);
            body.color = Color.white;
            body.textWrappingMode = TextWrappingModes.Normal;
            var bodyLe = body.gameObject.AddComponent<LayoutElement>();
            bodyLe.minHeight = 40f;
            bodyLe.preferredHeight = 56f;

            timer = MakeTmp(card.transform, "Timer", "", 40f, FontStyles.Bold);
            timer.alignment = TextAlignmentOptions.Center;
            timer.color = new Color(0.95f, 0.82f, 0.35f, 1f);
            var timerLe = timer.gameObject.AddComponent<LayoutElement>();
            timerLe.preferredHeight = 28f;

            if (withButtons)
            {
                var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(card.transform, false);
                row.GetComponent<LayoutElement>().preferredHeight = 56f;
                var hl = row.GetComponent<HorizontalLayoutGroup>();
                hl.spacing = 12f;
                hl.childForceExpandWidth = true;
                hl.childControlWidth = true;
                hl.childControlHeight = true;

                declineBtn = MakeBtn(row.transform, "Decline", "Не хочу", new Color(0.30f, 0.32f, 0.36f, 1f));
                acceptBtn = MakeBtn(row.transform, "Accept", "Принять", new Color(0.20f, 0.52f, 0.34f, 1f));
            }

            return card;
        }

        private static Button MakeBtn(Transform parent, string name, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 56f;
            var img = go.GetComponent<Image>();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var tmp = MakeTmp(go.transform, "Label", label, 26f, FontStyles.Bold);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return btn;
        }

        private static TMP_Text MakeTmp(Transform parent, string name, string text, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.enableAutoSizing = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            var font = AchievementUiFontLoader.Resolve(null);
            if (font != null) tmp.font = font;
            return tmp;
        }

        [Serializable]
        private sealed class RaceInviteNotif
        {
            public string invite_id;
            public string from_user_id;
            public string from_username;
            public long expires_at;
            public string kind;
            public int prep_seconds;
        }

        [Serializable]
        private sealed class RaceInviteUpdateNotif
        {
            public string invite_id;
            public string match_id;
            public string status;
            public string kind;
            public int prep_seconds;
            public string from_user_id;
            public string from_username;
            public string to_user_id;
            public string to_username;
            public string by_user_id;
            public string by_username;
            public string err;
        }
    }
}
