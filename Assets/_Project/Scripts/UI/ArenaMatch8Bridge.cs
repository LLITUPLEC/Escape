using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
using Project.Nakama;
using Project.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Арена турнира 8 игроков: кнопка match3Arena, модалка ставок, очередь (queue),
    /// опрос сервера и оверлей сетки с HP и таймером.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMatch8Bridge : MonoBehaviour
    {
        private const string RpcArenaJoin = "duel_arena_queue_join";
        private const string RpcArenaLeave = "duel_arena_queue_leave";
        private const string RpcArenaPoll = "duel_arena_queue_poll";

        [SerializeField] private string match3ArenaButtonName = "match3Arena";
        [SerializeField] private string queueTextObjectName = "queue";
        [SerializeField] private string duelMatch3SceneName = "DuelMatch3";

        [Header("Icons (опционально, из инспектора)")]
        [SerializeField] private Sprite iconGreenIngot;
        [SerializeField] private Sprite iconBlueIngot;
        [SerializeField] private Sprite iconPurpleIngot;
        [SerializeField] private Sprite iconOre;
        [SerializeField] private Sprite iconGold;

        private Button _arenaButton;
        private TMP_Text _queueTmp;

        private GameObject _modalRoot;
        private GameObject _bracketRoot;

        private Coroutine _pollRoutine;
        private bool _busyJoin;

        private string _sceneArmMidGuard;
        private float _suppressFightReloadUntil;
        private static bool _registeredSceneHooks;
        private static string _lastPrevSceneNameForArenaUi;
        private string _lastBracketSignature = string.Empty;

        /// <summary>JoinMatch недоступен (матч уже удалён) — не грузить DuelMatch8 повторно с тем же id.</summary>
        private static readonly HashSet<string> BlockedArenaJoinMatchIds = new();

        public static void BlockArenaJoinMatchId(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId)) return;
            BlockedArenaJoinMatchIds.Add(matchId.Trim());
        }

        [Serializable]
        private struct ArenaJoinPayload
        {
            public string bet_tier;
            public int session_epoch;
        }

        [Serializable]
        private sealed class ArenaPollResponse
        {
            public bool ok;
            public int queue_count;
            public int queue_max;
            public bool in_queue;
            public string queue_bet_tier;
            public ArenaTournamentState tournament;
            public string err;
        }

        [Serializable]
        private sealed class ArenaJoinResponse
        {
            public bool ok;
            public string err;
            public int queue_count;
            public int queue_max;
            public string bet_tier;
            public bool in_tournament;
        }

        [Serializable]
        private sealed class ArenaLeaveResponse
        {
            public bool ok;
            public string err;
            public int queue_count;
            public int queue_max;
        }

        [Serializable]
        private sealed class ArenaTournamentState
        {
            public bool active;
            public string id;
            public bool eliminated;
            public string phase;
            public string bet_tier;
            public int countdown_left;
            public string join_match_id;
            public bool join_opponent_is_bot;
            public ArenaPairRow[] qf;
            public ArenaPairRow[] sf;
            public ArenaPairRow[] final_pairs;
        }

        [Serializable]
        private sealed class ArenaPairRow
        {
            public int slot;
            public string match_id;
            public string uid_a;
            public string uid_b;
            public string display_a;
            public string display_b;
            public float hp_a;
            public float hp_b;
            public string status;
            public string winner_uid;
        }

        private void Awake()
        {
            if (!_registeredSceneHooks)
            {
                _registeredSceneHooks = true;
                SceneManager.activeSceneChanged += OnAnyActiveSceneChanged;
            }

            NakamaBootstrap.EnsureExists();
            _arenaButton = FindComponentByGameObjectName<Button>(match3ArenaButtonName);
            if (_arenaButton != null)
                _arenaButton.onClick.AddListener(OpenBetModal);

            var q = FindComponentByGameObjectName<TMP_Text>(queueTextObjectName);
            if (q != null)
                _queueTmp = q;

            EnsureBetModalBuilt();
            EnsureBracketBuilt();
        }

        private static void OnAnyActiveSceneChanged(Scene prev, Scene next)
        {
            _lastPrevSceneNameForArenaUi = prev.IsValid() ? prev.name : "";
        }

        private void OnEnable()
        {
            if (_lastPrevSceneNameForArenaUi == duelMatch3SceneName)
                _suppressFightReloadUntil = Time.realtimeSinceStartup + 8f;

            if (_pollRoutine != null)
                StopCoroutine(_pollRoutine);
            _pollRoutine = StartCoroutine(PollLoop());
        }

        private void OnDisable()
        {
            if (_pollRoutine != null)
            {
                StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(1.35f);
            while (enabled)
            {
                _ = PollOnceFireAndForget();
                yield return wait;
            }
        }

        private async Task PollOnceFireAndForget()
        {
            if (!NakamaBootstrap.Instance.IsReady)
                return;
            try
            {
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + "}";
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaPoll, payload);
                var raw = rpc?.Payload ?? "{}";
                var model = JsonUtility.FromJson<ArenaPollResponse>(raw);
                if (model == null || !model.ok)
                    return;

                MainThreadDispatcher.Enqueue(() => ApplyPoll(model));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] poll: " + e.Message);
            }
        }

        private void ApplyPoll(ArenaPollResponse m)
        {
            UpdateQueueUi(m);

            if (m.tournament != null && m.tournament.active && m.tournament.eliminated)
            {
                _lastBracketSignature = string.Empty;
                HideBracket();
                return;
            }

            if (m.tournament != null && m.tournament.active && !m.tournament.eliminated)
            {
                var sig = BuildBracketSignature(m.tournament);
                if (sig != _lastBracketSignature)
                {
                    _lastBracketSignature = sig;
                    ShowBracket(m.tournament);
                }
                else
                    RefreshArenaCountdownOnly(m.tournament);

                TryArmFightScene(m.tournament);
            }
            else
            {
                _lastBracketSignature = string.Empty;
                HideBracket();
            }
        }

        private static string BuildBracketSignature(ArenaTournamentState t)
        {
            if (t == null) return string.Empty;
            return $"{t.phase}|{t.countdown_left}|{t.join_match_id ?? ""}|{RowsSignature(t.qf)}{RowsSignature(t.sf)}{RowsSignature(t.final_pairs)}";
        }

        private static string RowsSignature(ArenaPairRow[] rows)
        {
            if (rows == null || rows.Length == 0) return string.Empty;
            var s = string.Empty;
            foreach (var r in rows)
            {
                if (r == null) continue;
                s += $"{r.slot}:{r.status}:{Mathf.RoundToInt(r.hp_a)}:{Mathf.RoundToInt(r.hp_b)}:{r.match_id ?? ""};";
            }

            return s;
        }

        private void RefreshArenaCountdownOnly(ArenaTournamentState t)
        {
            if (_bracketRoot == null || t == null) return;
            var cd = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            if (cd == null) return;
            if (t.phase == "countdown")
                cd.text = Mathf.Max(0, t.countdown_left).ToString();
            else
                cd.text = PhaseTitle(t.phase);
        }

        private void UpdateQueueUi(ArenaPollResponse m)
        {
            if (_queueTmp == null)
                return;

            var show = m.in_queue || (m.queue_count > 0);
            _queueTmp.gameObject.SetActive(show);
            if (show)
                _queueTmp.text = $"{Mathf.Clamp(m.queue_count, 0, m.queue_max > 0 ? m.queue_max : 8)}/{(m.queue_max > 0 ? m.queue_max : 8)}";
        }

        private void TryArmFightScene(ArenaTournamentState t)
        {
            if (string.IsNullOrEmpty(t.join_match_id))
            {
                _sceneArmMidGuard = null;
                return;
            }

            if (Time.realtimeSinceStartup < _suppressFightReloadUntil)
                return;

            if (BlockedArenaJoinMatchIds.Contains(t.join_match_id))
                return;

            if (_sceneArmMidGuard == t.join_match_id)
                return;

            _sceneArmMidGuard = t.join_match_id;
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
            var myUid = NakamaBootstrap.Instance?.Session?.UserId;
            var oppHint = ResolveArenaOpponentDisplay(t, myUid);
            Match3LaunchContext.ArmArenaJoin(t.join_match_id, oppHint, t.join_opponent_is_bot);

            if (!string.IsNullOrEmpty(duelMatch3SceneName))
                SceneManager.LoadScene(duelMatch3SceneName);
        }

        private static string ResolveArenaOpponentDisplay(ArenaTournamentState t, string myUserId)
        {
            if (t == null || string.IsNullOrEmpty(t.join_match_id) || string.IsNullOrEmpty(myUserId))
                return null;
            var mid = t.join_match_id.Trim();
            var h = ScanArenaRowsForOpponent(t.qf, mid, myUserId)
                    ?? ScanArenaRowsForOpponent(t.sf, mid, myUserId)
                    ?? ScanArenaRowsForOpponent(t.final_pairs, mid, myUserId);
            return string.IsNullOrWhiteSpace(h) ? null : h.Trim();
        }

        private static string ScanArenaRowsForOpponent(ArenaPairRow[] rows, string matchId, string myUid)
        {
            if (rows == null || rows.Length == 0) return null;
            foreach (var pr in rows)
            {
                if (pr == null || string.IsNullOrEmpty(pr.match_id)) continue;
                if (!string.Equals(pr.match_id.Trim(), matchId, StringComparison.Ordinal)) continue;

                if (string.Equals(pr.uid_a, myUid, StringComparison.Ordinal))
                    return pr.display_b;
                if (string.Equals(pr.uid_b, myUid, StringComparison.Ordinal))
                    return pr.display_a;
            }

            return null;
        }

        private void OpenBetModal()
        {
            if (_busyJoin || _modalRoot == null)
                return;
            _modalRoot.SetActive(true);
        }

        public void CloseBetModal()
        {
            if (_modalRoot != null)
                _modalRoot.SetActive(false);
        }

        public async void OnPickBetGreen()
        {
            await QueueJoinAsync("green");
        }

        public async void OnPickBetBlue()
        {
            await QueueJoinAsync("blue");
        }

        public async void OnPickBetPurple()
        {
            await QueueJoinAsync("purple");
        }

        private async Task QueueJoinAsync(string tier)
        {
            if (_busyJoin || !NakamaBootstrap.Instance.IsReady)
                return;
            _busyJoin = true;
            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(default);
                var payload = JsonUtility.ToJson(new ArenaJoinPayload
                {
                    bet_tier = tier,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                });
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaJoin, payload);
                var raw = rpc?.Payload ?? "{}";
                var resp = JsonUtility.FromJson<ArenaJoinResponse>(raw);
                if (resp != null && resp.ok)
                {
                    CloseBetModal();
                    if (_queueTmp != null)
                    {
                        var qMax = resp.queue_max > 0 ? resp.queue_max : 8;
                        var showQueue = resp.queue_count > 0 && !resp.in_tournament;
                        _queueTmp.gameObject.SetActive(showQueue);
                        if (showQueue)
                            _queueTmp.text =
                                $"{Mathf.Clamp(resp.queue_count, 0, qMax)}/{qMax}";
                    }

                    _ = PollOnceFireAndForget();
                }
                else
                    Debug.LogWarning("[Arena8] join failed: " + (resp?.err ?? raw));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] join: " + e.Message);
            }
            finally
            {
                _busyJoin = false;
            }
        }

        /// <summary>Покинуть очередь с возвратом слитков (RPC).</summary>
        public async void LeaveQueueClicked()
        {
            if (!NakamaBootstrap.Instance.IsReady)
                return;
            try
            {
                var payload = "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + "}";
                await NakamaBootstrap.Instance.Client.RpcAsync(NakamaBootstrap.Instance.Session, RpcArenaLeave, payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arena8] leave: " + e.Message);
            }
        }

        private void EnsureBetModalBuilt()
        {
            if (_modalRoot != null)
                return;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            _modalRoot = new GameObject("ArenaBetModal");
            _modalRoot.transform.SetParent(canvas.transform, false);
            var rect = _modalRoot.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var dim = _modalRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);
            dim.raycastTarget = true;

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_modalRoot.transform, false);
            var prect = panel.AddComponent<RectTransform>();
            prect.anchorMin = new Vector2(0.5f, 0.5f);
            prect.anchorMax = new Vector2(0.5f, 0.5f);
            prect.sizeDelta = new Vector2(780f, 540f);

            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.12f, 0.2f, 0.96f);

            var vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(28, 28, 28, 28);
            vl.spacing = 14f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childForceExpandHeight = false;
            vl.childForceExpandWidth = true;

            var titleGo = CreateTmp(panel.transform, "Ставка турнира и награда при победе", 26, FontStyles.Bold);
            titleGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);

            MakeBetRow(panel.transform, "green", "100 зелёных слитков", "600 руды · 600 золота", iconGreenIngot);
            MakeBetRow(panel.transform, "blue", "100 синих слитков", "1200 руды · 1200 золота", iconBlueIngot);
            MakeBetRow(panel.transform, "purple", "100 фиолетовых слитков", "2400 руды · 2400 золота", iconPurpleIngot);

            var closeBtn = CreateButton(panel.transform, "Закрыть", CloseBetModal);

            _modalRoot.SetActive(false);
        }

        private void MakeBetRow(Transform parent, string tier, string cost, string prize, Sprite ingotSp)
        {
            var row = new GameObject("Row_" + tier);
            row.transform.SetParent(parent, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 96f);

            var rowHl = row.AddComponent<HorizontalLayoutGroup>();
            rowHl.spacing = 10f;
            rowHl.childAlignment = TextAnchor.MiddleLeft;
            rowHl.childForceExpandWidth = true;
            rowHl.padding = new RectOffset(8, 8, 8, 8);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.12f, 0.28f, 0.52f, 0.55f);

            if (ingotSp != null)
            {
                var icon = new GameObject("IngotIcon");
                icon.transform.SetParent(row.transform, false);
                var img = icon.AddComponent<Image>();
                img.sprite = ingotSp;
                var irt = icon.GetComponent<RectTransform>();
                irt.sizeDelta = new Vector2(48f, 48f);
                var le = icon.AddComponent<LayoutElement>();
                le.preferredWidth = 52f;
                le.preferredHeight = 52f;
            }

            var leftCol = new GameObject("CostCol");
            leftCol.transform.SetParent(row.transform, false);
            var vl = leftCol.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f;
            vl.childAlignment = TextAnchor.MiddleLeft;
            var costTxt = CreateTmp(leftCol.transform, cost, 22, FontStyles.Normal);
            costTxt.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 58f);

            var prizeGo = new GameObject("PrizeCol");
            prizeGo.transform.SetParent(row.transform, false);
            var prizeHl = prizeGo.AddComponent<HorizontalLayoutGroup>();
            prizeHl.spacing = 8f;
            prizeHl.childAlignment = TextAnchor.MiddleRight;

            if (iconOre != null)
                CreateIconImage(prizeGo.transform, iconOre);
            var prizeOreTxt = CreateTmp(prizeGo.transform, tier == "green" ? "600" : (tier == "blue" ? "1200" : "2400"), 22, FontStyles.Bold);

            if (iconGold != null)
                CreateIconImage(prizeGo.transform, iconGold);
            var prizeGoldTxt = CreateTmp(prizeGo.transform, tier == "green" ? "600" : (tier == "blue" ? "1200" : "2400"), 22, FontStyles.Bold);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            btn.onClick.AddListener(() =>
            {
                if (tier == "green") OnPickBetGreen();
                else if (tier == "blue") OnPickBetBlue();
                else OnPickBetPurple();
            });
        }

        private static GameObject CreateIconImage(Transform parent, Sprite sp)
        {
            var icon = new GameObject("Ic");
            icon.transform.SetParent(parent, false);
            var img = icon.AddComponent<Image>();
            img.sprite = sp;
            var rt = icon.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 28f);
            var le = icon.AddComponent<LayoutElement>();
            le.preferredWidth = 30f;
            le.preferredHeight = 30f;
            return icon;
        }

        private static GameObject CreateTmp(Transform parent, string text, float size, FontStyles fs)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = fs;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = Color.white;
            return go;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.35f, 0.55f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 52f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var tmpGo = CreateTmp(go.transform, label, 22, FontStyles.Bold);
            tmpGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            tmpGo.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            tmpGo.GetComponent<RectTransform>().anchorMax = Vector2.one;
            tmpGo.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            tmpGo.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            return btn;
        }

        private void EnsureBracketBuilt()
        {
            if (_bracketRoot != null)
                return;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            _bracketRoot = new GameObject("ArenaBracketOverlay");
            _bracketRoot.transform.SetParent(canvas.transform, false);
            var rect = _bracketRoot.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var vl = _bracketRoot.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(24, 24, 90, 24);
            vl.spacing = 12f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var bg = _bracketRoot.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.06f, 0.12f, 0.88f);
            bg.raycastTarget = false;

            var titleGo = CreateTmp(_bracketRoot.transform, "Турнир Match3", 28, FontStyles.Bold);
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 44f;
            titleLe.flexibleWidth = 1f;

            var cdGo = CreateTmp(_bracketRoot.transform, "-", 36, FontStyles.Bold);
            cdGo.name = "ArenaCd";
            var cdLe = cdGo.AddComponent<LayoutElement>();
            cdLe.preferredHeight = 52f;
            cdLe.flexibleWidth = 1f;

            var rowsGo = new GameObject("ArenaRows");
            rowsGo.transform.SetParent(_bracketRoot.transform, false);
            var rowsRt = rowsGo.AddComponent<RectTransform>();
            rowsRt.anchorMin = Vector2.zero;
            rowsRt.anchorMax = Vector2.one;
            rowsRt.offsetMin = Vector2.zero;
            rowsRt.offsetMax = Vector2.zero;
            var rowsLe = rowsGo.AddComponent<LayoutElement>();
            rowsLe.flexibleHeight = 1f;
            rowsLe.minHeight = 260f;
            rowsLe.flexibleWidth = 1f;

            var rowsVl = rowsGo.AddComponent<VerticalLayoutGroup>();
            rowsVl.spacing = 10f;
            rowsVl.padding = new RectOffset(4, 4, 4, 4);
            rowsVl.childAlignment = TextAnchor.UpperCenter;
            rowsVl.childControlHeight = true;
            rowsVl.childControlWidth = true;
            rowsVl.childForceExpandWidth = true;
            rowsVl.childForceExpandHeight = false;

            _bracketRoot.SetActive(false);
        }

        private void ShowBracket(ArenaTournamentState t)
        {
            EnsureBracketBuilt();
            if (_bracketRoot == null)
                return;

            _bracketRoot.SetActive(true);

            var cd = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            if (cd != null)
            {
                if (t.phase == "countdown")
                    cd.text = Mathf.Max(0, t.countdown_left).ToString();
                else
                    cd.text = PhaseTitle(t.phase);
            }

            var rowsParent = _bracketRoot.transform.Find("ArenaRows");
            if (rowsParent == null)
                return;

            for (var i = rowsParent.childCount - 1; i >= 0; i--)
                Destroy(rowsParent.GetChild(i).gameObject);

            LayoutBracketRound(rowsParent, "1/4", t.qf);
            LayoutBracketRound(rowsParent, "1/2", t.sf);
            LayoutBracketRound(rowsParent, "Финал", t.final_pairs);
        }

        private static string PhaseTitle(string phase)
        {
            switch (phase)
            {
                case "qf": return "1/4 финала";
                case "sf": return "Полуфинал";
                case "final": return "Финал";
                case "done": return "Финиш";
                default: return phase ?? "";
            }
        }

        private void LayoutBracketRound(Transform parent, string title, ArenaPairRow[] rows)
        {
            if (rows == null || rows.Length == 0)
                return;

            var header = CreateTmp(parent, title, 22, FontStyles.Bold);
            var hdrRt = header.GetComponent<RectTransform>();
            hdrRt.sizeDelta = new Vector2(0f, 34f);
            var hdrLe = header.AddComponent<LayoutElement>();
            hdrLe.preferredHeight = 34f;
            hdrLe.flexibleWidth = 1f;

            foreach (var pr in rows)
            {
                var row = new GameObject("BracketRow", typeof(RectTransform));
                row.transform.SetParent(parent, false);
                var rowRt = row.GetComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, 72f);

                var rowLe = row.AddComponent<LayoutElement>();
                rowLe.minHeight = 64f;
                rowLe.preferredHeight = 72f;
                rowLe.flexibleWidth = 1f;

                var hl = row.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleCenter;
                hl.spacing = 10f;
                hl.padding = new RectOffset(10, 10, 6, 6);
                hl.childForceExpandHeight = true;
                hl.childForceExpandWidth = false;

                var leftName = CreateTmp(row.transform, pr.display_a ?? "", 20, FontStyles.Normal);
                var lnRt = leftName.GetComponent<RectTransform>();
                lnRt.sizeDelta = new Vector2(200f, 36f);
                var lnLe = leftName.AddComponent<LayoutElement>();
                lnLe.preferredWidth = 200f;
                lnLe.flexibleWidth = 1f;

                var hpA = MakeHpBar(row.transform);
                SetHp(hpA, pr.hp_a);

                var vs = CreateTmp(row.transform, "—", 22, FontStyles.Bold);
                vs.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 32f);
                vs.AddComponent<LayoutElement>().preferredWidth = 32f;

                var hpB = MakeHpBar(row.transform);
                SetHp(hpB, pr.hp_b);

                var rightName = CreateTmp(row.transform, pr.display_b ?? "", 20, FontStyles.Normal);
                rightName.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;
                var rnRt = rightName.GetComponent<RectTransform>();
                rnRt.sizeDelta = new Vector2(200f, 36f);
                var rnLe = rightName.AddComponent<LayoutElement>();
                rnLe.preferredWidth = 200f;
                rnLe.flexibleWidth = 1f;

                var statusGo = CreateTmp(row.transform, FormatPairStatus(pr.status), 18, FontStyles.Italic);
                statusGo.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 28f);
                var stLe = statusGo.AddComponent<LayoutElement>();
                stLe.preferredWidth = 100f;
            }
        }

        private static string FormatPairStatus(string s)
        {
            switch (s)
            {
                case "pending": return "";
                case "fighting": return "Бой";
                case "done": return "Готово";
                default: return s ?? "";
            }
        }

        private static Image MakeHpBar(Transform parent)
        {
            var go = new GameObject("Hp");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.18f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140f, 22f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(go.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.28f, 0.82f, 0.38f, 1f);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 140f;
            le.preferredHeight = 26f;

            return fillImg;
        }

        private static void SetHp(Image img, float hp)
        {
            if (img == null)
                return;
            img.fillAmount = Mathf.Clamp01(hp / 150f);
        }

        private void HideBracket()
        {
            if (_bracketRoot != null)
                _bracketRoot.SetActive(false);
        }

        private static T FindComponentByGameObjectName<T>(string goName) where T : Component
        {
            var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all)
            {
                if (c != null && c.gameObject.name == goName)
                    return c;
            }
            return null;
        }
    }
}
