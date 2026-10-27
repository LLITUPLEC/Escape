using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Real guard/probe/input surface against an in-memory SDK transport; no authentication or network client.</summary>
    public static class ServerConnectionGuardQa0312
    {
        private const string Output = ".cache/qa/legacy-0.3.12/connection";
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type GuardType = typeof(ServerConnectionGuard), BootstrapType = typeof(NakamaBootstrap);
        private static IEnumerator _flow;
        private static Report _report;
        private static ServerConnectionGuard _guard;
        private static NakamaBootstrap _bootstrap;
        private static object _previousGuard, _previousBootstrap;
        private static GameObject _bootstrapRoot, _guardRoot, _uiRoot, _eventsRoot;
        private static EventSystem _events, _previousEvents;
        private static Button _button;
        private static GameplayConnectionButton _cover;
        private static int _ownerClicks, _originalEpoch, _originalPreference;
        private static bool _hadPreference, _background;
        private static double _deadline;
        private static readonly List<TaskCompletionSource<global::Nakama.IApiRpc>> Pending = new();
        [Serializable] private sealed class Check { public string name; public bool passed; }
        [Serializable] private sealed class Report
        {
            public bool running, passed, networkOrAccountActions, preferenceWrites, preferencesRestored;
            public string checkedUtc, error;
            public List<Check> checks = new(); public List<string> screenshots = new();
        }
        public static string Run(string target)
        {
            if (target == "status") return _report == null ? "Connection QA has not run." : JsonUtility.ToJson(_report);
            if (target != "start") throw new ArgumentException("Use start or status.");
            Require(_flow == null, "Connection QA is already running.");
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused && SceneManager.GetActiveScene().name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal), "Use only the isolated notification/stake Play fixture.");
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Require(manager != null && !manager.enabled && UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "The fixture must have a disabled match presenter and no network bootstrap.");
            Require(Application.internetReachability != NetworkReachability.NotReachable, "The host reachability flag must allow testing a confirmed socket lease.");
            Require(Screen.width == 1080 && Screen.height == 1920, "Select the native1080x1920 Game view with Simulator shim disabled before connection QA.");
            Directory.CreateDirectory(Output); Pending.Clear();
            _report = new Report { running = true, checkedUtc = DateTime.UtcNow.ToString("O") };
            _previousGuard = GuardType.GetField("_instance", S).GetValue(null);
            _previousBootstrap = BootstrapType.GetField("<Instance>k__BackingField", S).GetValue(null);
            _originalEpoch = NakamaBootstrap.GetLocalSessionEpoch();
            _hadPreference = PlayerPrefs.HasKey(NakamaBootstrap.SessionEpochLocalPrefKey);
            _originalPreference = PlayerPrefs.GetInt(NakamaBootstrap.SessionEpochLocalPrefKey, 0);
            _background = Application.runInBackground; Application.runInBackground = true;
            try
            {
                _bootstrapRoot = new GameObject("InactiveOfflineConnectionBootstrap"); _bootstrapRoot.SetActive(false);
                _bootstrap = _bootstrapRoot.AddComponent<NakamaBootstrap>();
                CheckThat(! _bootstrapRoot.activeInHierarchy && ReferenceEquals(BootstrapType.GetField("<Instance>k__BackingField", S).GetValue(null), _previousBootstrap), "Inactive bootstrap never runs Awake/authentication");
                BootstrapType.GetField("<Instance>k__BackingField", S).SetValue(null, _bootstrap);
                SetSession((global::Nakama.ISession)FormatterServices.GetUninitializedObject(typeof(global::Nakama.Session)));
                _guardRoot = new GameObject("OfflineConnectionGuard"); _guard = _guardRoot.AddComponent<ServerConnectionGuard>();
                GuardType.GetField("_bootstrap", I).SetValue(_guard, _bootstrap);
                GuardType.GetField("_nextAttempt", I).SetValue(_guard, float.PositiveInfinity);
                CheckThat(_bootstrap.Config == null, "No connection config or client is supplied");
                BuildButton(); _flow = Flow(); _deadline = EditorApplication.timeSinceStartup + 35;
                EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Abort;
                Write(); return "Offline connection probe started; poll status. No real authentication, transport or account actions.";
            }
            catch (Exception error) { Finish(error); throw; }
        }
        private static IEnumerator Flow()
        {
            var lease = new ServerConnectionLease(); var token = new object();
            lease.Confirm(token, 10);
            CheckThat(lease.Allows(token, true, 16.999) && !lease.Allows(token, true, 17) && !lease.Allows(token, true, 9.99), "Lease expires exactly at seven seconds and rejects a backwards clock");
            CheckThat(!lease.Allows(new object(), true, 10) && !lease.Allows(token, false, 10), "Lease is bound to its connected socket identity");
            lease.Revoke(); CheckThat(!lease.Allows(token, true, 10), "Revocation discards previous confirmation");

            SetSocket(null); RefreshCover(); yield return null;
            CheckThat(!ServerConnectionGuard.CanPlay, "No socket blocks play");
            var hit = Hit(); CheckThat(hit == _cover.gameObject, "Offline cover wins the real UI raycast");
            ExecuteEvents.ExecuteHierarchy(hit, new PointerEventData(_events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            CheckThat(_ownerClicks == 0 && _button.interactable, "Cover intercepts clicks without overwriting its owner's interactable flag");
            _button.interactable = false; RefreshCover(); CheckThat(!_button.interactable, "Owner busy/progression lock is preserved while offline");
            AdaptBanner(); yield return null; Shot("connection-unavailable");

            var socket = Success(); SetSocket(socket); var task = Probe(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
            CheckThat(ServerConnectionGuard.CanPlay && socket.RpcCalls == 1 && socket.LastRpcId == "duel_online_ping_and_count" && socket.LastPayload == "{}", "Real WebSocket probe grants play only after the expected ping reply");
            task = Probe(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
            CheckThat(socket.RpcCalls == 1, "Fresh half-second lease coalesces redundant probes");
            RefreshCover(); CheckThat(!_cover.GetComponent<Image>().enabled && !_button.interactable, "Recovery removes only the input cover and keeps the owner's busy lock");
            _button.interactable = true; RefreshCover(); hit = Hit();
            CheckThat(hit == _button.gameObject || hit.transform.IsChildOf(_button.transform), "Recovered owner button wins the real raycast");
            ExecuteEvents.ExecuteHierarchy(hit, new PointerEventData(_events) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            CheckThat(_ownerClicks == 1, "Recovered owner receives one normal click");

            var replacement = Success(); SetSocket(replacement);
            CheckThat(!ServerConnectionGuard.CanPlay, "A connected replacement socket cannot reuse another socket's acknowledgement");
            task = Probe(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
            CheckThat(ServerConnectionGuard.CanPlay && replacement.RpcCalls == 1, "Replacement requires its own successful probe");
            yield return null;

            var blackhole = PendingSocket(out var never); SetSocket(blackhole); var began = Time.realtimeSinceStartup; task = Probe();
            CheckThat(!ServerConnectionGuard.CanPlay, "Connected but unacknowledged blackhole never grants play");
            while (!task.IsCompleted) yield return null;
            CheckThat(Failed(task) && Time.realtimeSinceStartup - began >= 2.8f && _bootstrap.Socket == null && blackhole.CloseCalls == 1 && !ServerConnectionGuard.CanPlay, "Three-second blackhole timeout closes the expected socket and revokes the lease");
            never.TrySetResult(Reply()); RefreshCover();
            CheckThat(Hit() == _cover.gameObject && _button.interactable, "Timeout restores the cover without changing owner state");
            AdaptBanner(); yield return null; Shot("connection-lost");

            var failed = new FakeSocket { Reply = () => Task.FromException<global::Nakama.IApiRpc>(new InvalidOperationException("offline QA transport failure")) };
            SetSocket(failed); task = Probe(); while (!task.IsCompleted) yield return null;
            CheckThat(Failed(task) && failed.CloseCalls == 1 && _bootstrap.Socket == null && !ServerConnectionGuard.CanPlay, "A failed transport probe revokes access and closes its socket");
            var rejected = new FakeSocket { Reply = () => Task.FromResult<global::Nakama.IApiRpc>(new FakeReply("{\"ok\":false}")) };
            SetSocket(rejected); task = Probe(); while (!task.IsCompleted) yield return null;
            CheckThat(Failed(task) && rejected.CloseCalls == 1 && !ServerConnectionGuard.CanPlay, "An unsuccessful RPC payload never confirms connection");

            var old = PendingSocket(out var delayed); SetSocket(old); task = Probe();
            var current = Success(); SetSocket(current); delayed.TrySetResult(Reply()); while (!task.IsCompleted) yield return null;
            CheckThat(Failed(task) && ReferenceEquals(_bootstrap.Socket, current) && current.CloseCalls == 0 && !ServerConnectionGuard.CanPlay, "A late reply from the old socket cannot close or confirm the new socket");
            task = Probe(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
            CheckThat(ServerConnectionGuard.CanPlay, "A later valid probe recovers after a stale reply");
            current.IsConnected = false;
            CheckThat(!ServerConnectionGuard.CanPlay, "Disconnected socket immediately loses access despite a recent reply");
            _report.passed = true;
        }
        private static FakeSocket Success() => new FakeSocket { Reply = () => Task.FromResult<global::Nakama.IApiRpc>(Reply()) };
        private static global::Nakama.IApiRpc Reply() => new FakeReply("{\"ok\":true,\"count\":1,\"session_epoch\":" + _originalEpoch + "}");
        private static FakeSocket PendingSocket(out TaskCompletionSource<global::Nakama.IApiRpc> source)
        {
            source = new TaskCompletionSource<global::Nakama.IApiRpc>(); Pending.Add(source); var task = source.Task;
            return new FakeSocket { Reply = () => task };
        }
        private static Task Probe() => (Task)GuardType.GetMethod("ProbeAsync", I).Invoke(_guard, new object[] { CancellationToken.None });
        private static bool Failed(Task task) { try { task.GetAwaiter().GetResult(); return false; } catch { return true; } }
        private static void SetSocket(global::Nakama.ISocket socket) => BootstrapType.GetField("<Socket>k__BackingField", I).SetValue(_bootstrap, socket);
        private static void SetSession(global::Nakama.ISession session) => BootstrapType.GetField("<Session>k__BackingField", I).SetValue(_bootstrap, session);
        private static void BuildButton()
        {
            _previousEvents = EventSystem.current; _eventsRoot = new GameObject("OfflineConnectionQaEvents", typeof(EventSystem)); _events = _eventsRoot.GetComponent<EventSystem>(); EventSystem.current = _events;
            _uiRoot = new GameObject("OfflineConnectionQaControls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _uiRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.sortingOrder = 9000;
            var scaler = _uiRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            _ownerClicks = 0; _button = ExpeditionUi.Button(_uiRoot.transform, "ConnectionQaOwnerButton", "НАЧАТЬ БОЙ", .22f, .34f, .78f, .42f, () => _ownerClicks++, true, 30);
            GameplayConnectionButton.Bind(_button); _cover = _button.transform.Find("OfflinePlayCover").GetComponent<GameplayConnectionButton>();
        }
        private static void RefreshCover() { typeof(GameplayConnectionButton).GetMethod("Refresh", I).Invoke(_cover, null); Canvas.ForceUpdateCanvases(); }
        private static GameObject Hit()
        {
            Canvas.ForceUpdateCanvases(); var canvas = _uiRoot.GetComponent<Canvas>(); var position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, ((RectTransform)_button.transform).TransformPoint(((RectTransform)_button.transform).rect.center));
            var hits = new List<RaycastResult>(); _events.RaycastAll(new PointerEventData(_events) { position = position }, hits);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }
        private static void AdaptBanner()
        {
            GuardType.GetMethod("UpdateBanner", I).Invoke(_guard, new object[] { false });
            var canvas = (Canvas)GuardType.GetField("_canvas", I).GetValue(_guard);
            Require(canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Keep the production connection notice as an overlay above the battle overlay.");
        }
        private static void Shot(string name)
        {
            AdaptBanner(); Canvas.ForceUpdateCanvases();
            var banner = (RectTransform)GuardType.GetField("_banner", I).GetValue(_guard); Require(banner != null && banner.gameObject.activeInHierarchy, "Offline banner is not visible.");
            var canvas = banner.GetComponentInParent<Canvas>(); var viewport = canvas.pixelRect;
            var corners = new Vector3[4]; banner.GetWorldCorners(corners);
            foreach (var corner in corners) { var point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corner); Require(viewport.Contains(point), "Connection notice is outside its native viewport."); }
            var text = banner.GetComponentInChildren<TMP_Text>(); text.ForceMeshUpdate(); Require(!text.isTextOverflowing, "Connection message overflows.");
            CheckThat(Mathf.Abs(viewport.width - 1080) < 1 && Mathf.Abs(viewport.height - 1920) < 1, "Connection banner uses native1080x1920 viewport: " + name);
            var source = ExpeditionReleaseCaptureView.Capture("publish-connection0312-" + name); var target = Path.GetFullPath(Path.Combine(Output, name + ".png")); File.Copy(source, target, true); _report.screenshots.Add(target);
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { Require(EditorApplication.isPlaying && EditorApplication.timeSinceStartup < _deadline, "Connection QA ended or timed out."); if (_flow.MoveNext()) return; Finish(null); }
            catch (Exception error) { Finish(error); }
        }
        private static void Abort() => Finish(new InvalidOperationException("Connection QA interrupted by assembly reload."));
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Abort; _flow = null;
            try
            {
                foreach (var pending in Pending) pending.TrySetCanceled(); Pending.Clear();
                GuardType.GetField("_instance", S).SetValue(null, _previousGuard); BootstrapType.GetField("<Instance>k__BackingField", S).SetValue(null, _previousBootstrap);
                if (_bootstrap != null) { SetSocket(null); SetSession(null); }
                if (_guard != null)
                {
                    var canvas = (Canvas)GuardType.GetField("_canvas", I).GetValue(_guard); if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                }
                foreach (var go in new[] { _guardRoot, _bootstrapRoot, _uiRoot, _eventsRoot }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (_previousEvents != null) EventSystem.current = _previousEvents;
                BootstrapType.GetField("_localSessionEpoch", S).SetValue(null, _originalEpoch);
                if (_hadPreference)
                {
                    if (!PlayerPrefs.HasKey(NakamaBootstrap.SessionEpochLocalPrefKey) || PlayerPrefs.GetInt(NakamaBootstrap.SessionEpochLocalPrefKey) != _originalPreference)
                    { PlayerPrefs.SetInt(NakamaBootstrap.SessionEpochLocalPrefKey, _originalPreference); PlayerPrefs.Save(); _report.preferenceWrites = true; }
                }
                else if (PlayerPrefs.HasKey(NakamaBootstrap.SessionEpochLocalPrefKey)) { PlayerPrefs.DeleteKey(NakamaBootstrap.SessionEpochLocalPrefKey); PlayerPrefs.Save(); _report.preferenceWrites = true; }
                _report.preferencesRestored = PlayerPrefs.HasKey(NakamaBootstrap.SessionEpochLocalPrefKey) == _hadPreference && (!_hadPreference || PlayerPrefs.GetInt(NakamaBootstrap.SessionEpochLocalPrefKey) == _originalPreference);
            }
            catch (Exception cleanup) { error = new AggregateException(error ?? cleanup, cleanup); }
            Application.runInBackground = _background;
            if (_report != null) { _report.running = false; if (error != null) { _report.passed = false; _report.error = error.ToString(); } Write(); }
        }
        private static void CheckThat(bool condition, string name) { _report.checks.Add(new Check { name = name, passed = condition }); Require(condition, name); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Write() => File.WriteAllText(Path.Combine(Output, "connection-guard-validation.json"), JsonUtility.ToJson(_report, true));
        private sealed class FakeReply : global::Nakama.IApiRpc
        {
            public string HttpKey => null; public string Id => "duel_online_ping_and_count"; public string Payload { get; }
            public FakeReply(string payload) { Payload = payload; }
        }
        private sealed class FakeSocket : global::Nakama.ISocket {
            public bool IsConnected { get; set; } = true; public bool IsConnecting => false;
            public int RpcCalls, CloseCalls; public string LastRpcId, LastPayload; public Func<Task<global::Nakama.IApiRpc>> Reply;
            public event System.Action<System.String> Closed { add {} remove {} }
            public event System.Action Connected { add {} remove {} }
            public event System.Action<global::Nakama.IApiChannelMessage> ReceivedChannelMessage { add {} remove {} }
            public event System.Action<global::Nakama.IChannelPresenceEvent> ReceivedChannelPresence { add {} remove {} }
            public event System.Action<System.Exception> ReceivedError { add {} remove {} }
            public event System.Action<global::Nakama.IMatchmakerMatched> ReceivedMatchmakerMatched { add {} remove {} }
            public event System.Action<global::Nakama.IMatchState> ReceivedMatchState { add {} remove {} }
            public event System.Action<global::Nakama.IMatchPresenceEvent> ReceivedMatchPresence { add {} remove {} }
            public event System.Action<global::Nakama.IApiNotification> ReceivedNotification { add {} remove {} }
            public event System.Action<global::Nakama.IParty> ReceivedParty { add {} remove {} }
            public event System.Action<global::Nakama.IPartyClose> ReceivedPartyClose { add {} remove {} }
            public event System.Action<global::Nakama.IPartyData> ReceivedPartyData { add {} remove {} }
            public event System.Action<global::Nakama.IPartyJoinRequest> ReceivedPartyJoinRequest { add {} remove {} }
            public event System.Action<global::Nakama.IPartyLeader> ReceivedPartyLeader { add {} remove {} }
            public event System.Action<global::Nakama.IPartyMatchmakerTicket> ReceivedPartyMatchmakerTicket { add {} remove {} }
            public event System.Action<global::Nakama.IPartyPresenceEvent> ReceivedPartyPresence { add {} remove {} }
            public event System.Action<global::Nakama.IPartyUpdate> ReceivedPartyUpdate { add {} remove {} }
            public event System.Action<global::Nakama.IStatusPresenceEvent> ReceivedStatusPresence { add {} remove {} }
            public event System.Action<global::Nakama.IStreamPresenceEvent> ReceivedStreamPresence { add {} remove {} }
            public event System.Action<global::Nakama.IStreamState> ReceivedStreamState { add {} remove {} }
            public System.Threading.Tasks.Task AcceptPartyMemberAsync(System.String partyId, global::Nakama.IUserPresence presence) { throw new InvalidOperationException("Unused SDK operation in offline QA: AcceptPartyMemberAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IMatchmakerTicket> AddMatchmakerAsync(System.String query, System.Int32 minCount, System.Int32 maxCount, System.Collections.Generic.Dictionary<System.String, System.String> stringProperties, System.Collections.Generic.Dictionary<System.String, System.Double> numericProperties, System.Nullable<System.Int32> countMultiple) { throw new InvalidOperationException("Unused SDK operation in offline QA: AddMatchmakerAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IPartyMatchmakerTicket> AddMatchmakerPartyAsync(System.String partyId, System.String query, System.Int32 minCount, System.Int32 maxCount, System.Collections.Generic.Dictionary<System.String, System.String> stringProperties, System.Collections.Generic.Dictionary<System.String, System.Double> numericProperties, System.Nullable<System.Int32> countMultiple) { throw new InvalidOperationException("Unused SDK operation in offline QA: AddMatchmakerPartyAsync"); }
            public System.Threading.Tasks.Task ClosePartyAsync(System.String partyId) { throw new InvalidOperationException("Unused SDK operation in offline QA: ClosePartyAsync"); }
            public System.Threading.Tasks.Task CloseAsync() { CloseCalls++; IsConnected = false; return Task.CompletedTask; }
            public System.Threading.Tasks.Task ConnectAsync(global::Nakama.ISession session, System.Boolean appearOnline, System.Int32 connectTimeout, System.String langTag) { throw new InvalidOperationException("Unused SDK operation in offline QA: ConnectAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IMatch> CreateMatchAsync(System.String matchName) { throw new InvalidOperationException("Unused SDK operation in offline QA: CreateMatchAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IParty> CreatePartyAsync(System.Boolean open, System.Boolean hidden, System.Int32 maxSize, System.String label) { throw new InvalidOperationException("Unused SDK operation in offline QA: CreatePartyAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IStatus> FollowUsersAsync(System.Collections.Generic.IEnumerable<global::Nakama.IApiUser> users) { throw new InvalidOperationException("Unused SDK operation in offline QA: FollowUsersAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IStatus> FollowUsersAsync(System.Collections.Generic.IEnumerable<System.String> userIDs, System.Collections.Generic.IEnumerable<System.String> usernames) { throw new InvalidOperationException("Unused SDK operation in offline QA: FollowUsersAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannel> JoinChatAsync(System.String target, global::Nakama.ChannelType type, System.Boolean persistence, System.Boolean hidden) { throw new InvalidOperationException("Unused SDK operation in offline QA: JoinChatAsync"); }
            public System.Threading.Tasks.Task JoinPartyAsync(System.String partyId) { throw new InvalidOperationException("Unused SDK operation in offline QA: JoinPartyAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IMatch> JoinMatchAsync(global::Nakama.IMatchmakerMatched matched) { throw new InvalidOperationException("Unused SDK operation in offline QA: JoinMatchAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IMatch> JoinMatchAsync(System.String matchId, System.Collections.Generic.IDictionary<System.String, System.String> metadata) { throw new InvalidOperationException("Unused SDK operation in offline QA: JoinMatchAsync"); }
            public System.Threading.Tasks.Task LeaveChatAsync(global::Nakama.IChannel channel) { throw new InvalidOperationException("Unused SDK operation in offline QA: LeaveChatAsync"); }
            public System.Threading.Tasks.Task LeaveChatAsync(System.String channelId) { throw new InvalidOperationException("Unused SDK operation in offline QA: LeaveChatAsync"); }
            public System.Threading.Tasks.Task LeaveMatchAsync(global::Nakama.IMatch match) { throw new InvalidOperationException("Unused SDK operation in offline QA: LeaveMatchAsync"); }
            public System.Threading.Tasks.Task LeaveMatchAsync(System.String matchId) { throw new InvalidOperationException("Unused SDK operation in offline QA: LeaveMatchAsync"); }
            public System.Threading.Tasks.Task LeavePartyAsync(System.String partyId) { throw new InvalidOperationException("Unused SDK operation in offline QA: LeavePartyAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IPartyJoinRequest> ListPartyJoinRequestsAsync(System.String partyId) { throw new InvalidOperationException("Unused SDK operation in offline QA: ListPartyJoinRequestsAsync"); }
            public System.Threading.Tasks.Task PromotePartyMemberAsync(System.String partyId, global::Nakama.IUserPresence partyMember) { throw new InvalidOperationException("Unused SDK operation in offline QA: PromotePartyMemberAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> RemoveChatMessageAsync(global::Nakama.IChannel channel, System.String messageId) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemoveChatMessageAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> RemoveChatMessageAsync(System.String channelId, System.String messageId) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemoveChatMessageAsync"); }
            public System.Threading.Tasks.Task RemoveMatchmakerAsync(global::Nakama.IMatchmakerTicket ticket) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemoveMatchmakerAsync"); }
            public System.Threading.Tasks.Task RemoveMatchmakerAsync(System.String ticket) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemoveMatchmakerAsync"); }
            public System.Threading.Tasks.Task RemoveMatchmakerPartyAsync(System.String partyId, System.String ticket) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemoveMatchmakerPartyAsync"); }
            public System.Threading.Tasks.Task RemovePartyMemberAsync(System.String partyId, global::Nakama.IUserPresence presence) { throw new InvalidOperationException("Unused SDK operation in offline QA: RemovePartyMemberAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IApiRpc> RpcAsync(System.String funcId, System.String payload) { RpcCalls++; LastRpcId = funcId; LastPayload = payload; return Reply(); }
            public System.Threading.Tasks.Task<global::Nakama.IApiRpc> RpcAsync(System.String funcId, System.ArraySegment<System.Byte> payload) { throw new InvalidOperationException("Unused SDK operation in offline QA: RpcAsync"); }
            public System.Threading.Tasks.Task SendMatchStateAsync(System.String matchId, System.Int64 opCode, System.String state, System.Collections.Generic.IEnumerable<global::Nakama.IUserPresence> presences) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendMatchStateAsync"); }
            public System.Threading.Tasks.Task SendMatchStateAsync(System.String matchId, System.Int64 opCode, System.ArraySegment<System.Byte> state, System.Collections.Generic.IEnumerable<global::Nakama.IUserPresence> presences) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendMatchStateAsync"); }
            public System.Threading.Tasks.Task SendMatchStateAsync(System.String matchId, System.Int64 opCode, System.Byte[] state, System.Collections.Generic.IEnumerable<global::Nakama.IUserPresence> presences) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendMatchStateAsync"); }
            public System.Threading.Tasks.Task SendPartyDataAsync(System.String partyId, System.Int64 opCode, System.ArraySegment<System.Byte> data) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendPartyDataAsync"); }
            public System.Threading.Tasks.Task SendPartyDataAsync(System.String partyId, System.Int64 opCode, System.String data) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendPartyDataAsync"); }
            public System.Threading.Tasks.Task SendPartyDataAsync(System.String partyId, System.Int64 opCode, System.Byte[] data) { throw new InvalidOperationException("Unused SDK operation in offline QA: SendPartyDataAsync"); }
            public System.Threading.Tasks.Task UnfollowUsersAsync(System.Collections.Generic.IEnumerable<global::Nakama.IApiUser> users) { throw new InvalidOperationException("Unused SDK operation in offline QA: UnfollowUsersAsync"); }
            public System.Threading.Tasks.Task UnfollowUsersAsync(System.Collections.Generic.IEnumerable<System.String> userIDs) { throw new InvalidOperationException("Unused SDK operation in offline QA: UnfollowUsersAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> UpdateChatMessageAsync(global::Nakama.IChannel channel, System.String messageId, System.String content) { throw new InvalidOperationException("Unused SDK operation in offline QA: UpdateChatMessageAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> UpdateChatMessageAsync(System.String channelId, System.String messageId, System.String content) { throw new InvalidOperationException("Unused SDK operation in offline QA: UpdateChatMessageAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IPartyUpdate> UpdatePartyAsync(System.String partyId, System.Boolean open, System.Boolean hidden, System.String label) { throw new InvalidOperationException("Unused SDK operation in offline QA: UpdatePartyAsync"); }
            public System.Threading.Tasks.Task UpdateStatusAsync(System.String status) { throw new InvalidOperationException("Unused SDK operation in offline QA: UpdateStatusAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> WriteChatMessageAsync(global::Nakama.IChannel channel, System.String content) { throw new InvalidOperationException("Unused SDK operation in offline QA: WriteChatMessageAsync"); }
            public System.Threading.Tasks.Task<global::Nakama.IChannelMessageAck> WriteChatMessageAsync(System.String channelId, System.String content) { throw new InvalidOperationException("Unused SDK operation in offline QA: WriteChatMessageAsync"); }
        }

    }
}
