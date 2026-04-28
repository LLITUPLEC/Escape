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
        private const string BracketPrefabResourcesPath = "UI/ArenaBracketOverlay";

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
        private Transform _rowsRoot;
        private TMP_Text _cdText;
        private TMP_Text _headerTemplate;
        private Transform _pairRowTemplate;
        private GameObject _toastRoot;
        private TMP_Text _toastText;
        private Coroutine _toastRoutine;

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
            public string ingot_def;
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
            public string next_round;
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
                // HP can come as float; keep precision so bracket refreshes while HP changes.
                s += $"{r.slot}:{r.status}:{Mathf.RoundToInt(r.hp_a * 100f)}:{Mathf.RoundToInt(r.hp_b * 100f)}:{r.match_id ?? ""};";
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
                {
                    var err = resp?.err ?? raw;
                    Debug.LogWarning("[Arena8] join failed: " + err);
                    if (string.Equals(resp?.err, "not_enough_ingots", StringComparison.Ordinal))
                        ShowToast("Недостаточно слитков для ставки", 2.6f);
                    else if (string.Equals(resp?.err, "bet_tier_mismatch", StringComparison.Ordinal))
                        ShowToast("Очередь занята другой ставкой", 2.6f);
                }
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
            // Click on dim background closes modal.
            var dimBtn = _modalRoot.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(() =>
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
            });

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

            MakeBetRow(panel.transform, "green", "50 зелёных слитков", "300 руды · 300 золота", iconGreenIngot);
            MakeBetRow(panel.transform, "blue", "50 синих слитков", "600 руды · 600 золота", iconBlueIngot);
            MakeBetRow(panel.transform, "purple", "50 фиолетовых слитков", "1200 руды · 1200 золота", iconPurpleIngot);

            var closeBtn = CreateButton(panel.transform, "Закрыть", () =>
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
            });

            _modalRoot.SetActive(false);
        }

        private void EnsureToastBuilt()
        {
            if (_toastRoot != null)
                return;
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                return;

            _toastRoot = new GameObject("ArenaToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _toastRoot.transform.SetParent(canvas.transform, false);
            var rt = _toastRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -22f);
            rt.sizeDelta = new Vector2(720f, 64f);

            var bg = _toastRoot.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            bg.raycastTarget = false;

            var cg = _toastRoot.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_toastRoot.transform, false);
            var txt = txtGo.GetComponent<TextMeshProUGUI>();
            txt.text = "";
            txt.fontSize = 26;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 8f);
            trt.offsetMax = new Vector2(-14f, -8f);

            _toastText = txt;
            _toastRoot.SetActive(false);
        }

        private void ShowToast(string msg, float seconds)
        {
            EnsureToastBuilt();
            if (_toastRoot == null || _toastText == null)
                return;

            if (_toastRoutine != null)
            {
                StopCoroutine(_toastRoutine);
                _toastRoutine = null;
            }
            _toastRoutine = StartCoroutine(ToastRoutine(msg, seconds));
        }

        private IEnumerator ToastRoutine(string msg, float seconds)
        {
            _toastRoot.SetActive(true);
            _toastText.text = msg ?? "";
            var cg = _toastRoot.GetComponent<CanvasGroup>();
            if (cg == null) yield break;
            cg.alpha = 1f;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, seconds));
            cg.alpha = 0f;
            _toastRoot.SetActive(false);
            _toastRoutine = null;
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
            var prizeOreTxt = CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

            if (iconGold != null)
                CreateIconImage(prizeGo.transform, iconGold);
            var prizeGoldTxt = CreateTmp(prizeGo.transform, tier == "green" ? "300" : (tier == "blue" ? "600" : "1200"), 22, FontStyles.Bold);

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

            var prefab = Resources.Load<GameObject>(BracketPrefabResourcesPath);
            if (prefab != null)
                _bracketRoot = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            else
                _bracketRoot = new GameObject("ArenaBracketOverlay", typeof(RectTransform));

            if (_bracketRoot.transform.parent != canvas.transform)
                _bracketRoot.transform.SetParent(canvas.transform, false);

            _cdText = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
            _rowsRoot = _bracketRoot.transform.Find("ArenaRows");
            _headerTemplate = _rowsRoot != null
                ? _rowsRoot.Find("RoundHeaderTemplate")?.GetComponent<TMP_Text>()
                : null;
            _pairRowTemplate = _rowsRoot != null
                ? _rowsRoot.Find("PairRowTemplate")
                : null;

            // Fallback: if prefab missing OR prefab does not contain required nodes, build procedurally.
            if (_rowsRoot == null || _cdText == null)
            {
                UnityEngine.Object.Destroy(_bracketRoot);
                _bracketRoot = BuildBracketProcedural(canvas.transform);
                _cdText = _bracketRoot.transform.Find("ArenaCd")?.GetComponent<TMP_Text>();
                _rowsRoot = _bracketRoot.transform.Find("ArenaRows");
                _headerTemplate = null;
                _pairRowTemplate = null;
            }

            _bracketRoot.SetActive(false);
        }

        private static GameObject BuildBracketProcedural(Transform canvas)
        {
            var root = new GameObject("ArenaBracketOverlay");
            root.transform.SetParent(canvas, false);
            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var cg = root.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var vl = root.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(24, 24, 90, 24);
            vl.spacing = 12f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlHeight = true;
            vl.childControlWidth = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.06f, 0.12f, 0.88f);
            bg.raycastTarget = true;

            var titleGo = CreateTmp(root.transform, "Турнир Match3", 28, FontStyles.Bold);
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 44f;
            titleLe.flexibleWidth = 1f;

            var cdGo = CreateTmp(root.transform, "-", 36, FontStyles.Bold);
            cdGo.name = "ArenaCd";
            var cdLe = cdGo.AddComponent<LayoutElement>();
            cdLe.preferredHeight = 52f;
            cdLe.flexibleWidth = 1f;

            var rowsGo = new GameObject("ArenaRows");
            rowsGo.transform.SetParent(root.transform, false);
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

            return root;
        }

        private void ShowBracket(ArenaTournamentState t)
        {
            EnsureBracketBuilt();
            if (_bracketRoot == null)
                return;

            _bracketRoot.SetActive(true);

            if (_cdText != null)
            {
                if (t.phase == "countdown")
                    _cdText.text = Mathf.Max(0, t.countdown_left).ToString();
                else
                    _cdText.text = PhaseTitle(t.phase);
            }

            if (_rowsRoot == null)
                return;

            ClearRowsKeepingTemplates(_rowsRoot);

            // Show only current/upcoming round (no history).
            if (t.phase == "qf")
                LayoutBracketRoundFromTemplate(_rowsRoot, "1/4", t.qf);
            else if (t.phase == "sf")
                LayoutBracketRoundFromTemplate(_rowsRoot, "1/2", t.sf);
            else if (t.phase == "final")
                LayoutBracketRoundFromTemplate(_rowsRoot, "Финал", t.final_pairs);
            else if (t.phase == "countdown")
            {
                // During countdown show the upcoming bracket (already prepared on server).
                if (t.next_round == "sf")
                    LayoutBracketRoundFromTemplate(_rowsRoot, "1/2", t.sf);
                else if (t.next_round == "final")
                    LayoutBracketRoundFromTemplate(_rowsRoot, "Финал", t.final_pairs);
                else
                    LayoutBracketRoundFromTemplate(_rowsRoot, "1/4", t.qf);
            }
        }

        private void ClearRowsKeepingTemplates(Transform rowsRoot)
        {
            if (rowsRoot == null) return;
            for (var i = rowsRoot.childCount - 1; i >= 0; i--)
            {
                var ch = rowsRoot.GetChild(i);
                if (ch == null) continue;
                if (ch.name == "RoundHeaderTemplate" || ch.name == "PairRowTemplate")
                    continue;
                Destroy(ch.gameObject);
            }

            var hdrT = rowsRoot.Find("RoundHeaderTemplate");
            if (hdrT != null) hdrT.gameObject.SetActive(false);
            var rowT = rowsRoot.Find("PairRowTemplate");
            if (rowT != null) rowT.gameObject.SetActive(false);
        }

        private void LayoutBracketRoundFromTemplate(Transform parent, string title, ArenaPairRow[] rows)
        {
            if (parent == null || rows == null || rows.Length == 0)
                return;

            var hdrT = _headerTemplate != null ? _headerTemplate : parent.Find("RoundHeaderTemplate")?.GetComponent<TMP_Text>();
            TMP_Text header;
            if (hdrT != null)
            {
                header = UnityEngine.Object.Instantiate(hdrT, parent);
                header.gameObject.name = "RoundHeader";
                header.gameObject.SetActive(true);
                header.text = title;
            }
            else
            {
                var headerGo = CreateTmp(parent, title, 22, FontStyles.Bold);
                header = headerGo.GetComponent<TMP_Text>();
            }

            foreach (var pr in rows)
                CreatePairRowFromTemplate(parent, pr);
        }

        private void CreatePairRowFromTemplate(Transform parent, ArenaPairRow pr)
        {
            if (parent == null || pr == null) return;

            var rowT = _pairRowTemplate != null ? _pairRowTemplate : parent.Find("PairRowTemplate");
            Transform row;
            if (rowT != null)
            {
                row = UnityEngine.Object.Instantiate(rowT, parent);
                row.gameObject.name = "BracketRow";
                row.gameObject.SetActive(true);
            }
            else
            {
                // Old procedural fallback
                var rowGo = new GameObject("BracketRow", typeof(RectTransform));
                rowGo.transform.SetParent(parent, false);
                row = rowGo.transform;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, 72f);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.minHeight = 64f;
                rowLe.preferredHeight = 72f;
                rowLe.flexibleWidth = 1f;
                var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleCenter;
                hl.spacing = 10f;
                hl.padding = new RectOffset(10, 10, 6, 6);
                hl.childForceExpandHeight = true;
                hl.childForceExpandWidth = false;
            }

            var leftName = row.Find("LeftName")?.GetComponent<TMP_Text>();
            if (leftName == null) leftName = row.Find("Txt")?.GetComponent<TMP_Text>();
            if (leftName != null) leftName.text = pr.display_a ?? "";

            var rightName = row.Find("RightName")?.GetComponent<TMP_Text>();
            if (rightName != null) rightName.text = pr.display_b ?? "";

            var hpA = row.Find("HpA/Fill")?.GetComponent<Image>();
            if (hpA != null) SetHp(hpA, pr.hp_a);
            else
            {
                // legacy name: "Hp" was a fill image itself
                var legacy = row.Find("Hp")?.GetComponent<Image>();
                if (legacy != null) SetHp(legacy, pr.hp_a);
            }

            var hpB = row.Find("HpB/Fill")?.GetComponent<Image>();
            if (hpB != null) SetHp(hpB, pr.hp_b);

            var status = row.Find("Status")?.GetComponent<TMP_Text>();
            if (status != null) status.text = FormatPairStatus(pr.status);
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
            // Server may send hp as 0..150 or as normalized 0..1. Support both.
            if (hp <= 1.01f)
                img.fillAmount = Mathf.Clamp01(hp);
            else
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
