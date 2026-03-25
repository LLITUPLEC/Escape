using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class ArenaMenuStatsController : MonoBehaviour
    {
        [Header("UI Roots")]
        [SerializeField] private string hudRootName = "ArenaMenuWorld";
        [SerializeField] private string statsCardName = "Match3StatsCard";

        [Header("Polling")]
        [SerializeField, Min(1f)] private float match3StatsPollSeconds = 6f;

        [Header("Debug")]
        [SerializeField] private bool debug = false;

        private const string RpcMatch3StatsGet = "duel_match3_stats_get";

        private RectTransform _statsRoot;
        private Text _playedText;
        private Text _winsText;
        private Text _lossesText;
        private TMP_Text _playedTmp;
        private TMP_Text _winsTmp;
        private TMP_Text _lossesTmp;
        private CancellationTokenSource _cts;

        private void Awake()
        {
            Bind();
        }

        private void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _ = LoopAsync(_cts.Token);
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void Bind()
        {
            _statsRoot = FindStatsRoot();
            if (_statsRoot == null)
            {
                if (debug) Debug.Log("[ArenaMenuStats] Stats root not found.");
                return;
            }

            _playedText = FindTextUnder(_statsRoot, "PlayedValue");
            _winsText = FindTextUnder(_statsRoot, "WinsValue");
            _lossesText = FindTextUnder(_statsRoot, "LossesValue");
            _playedTmp = FindTmpTextUnder(_statsRoot, "PlayedValue");
            _winsTmp = FindTmpTextUnder(_statsRoot, "WinsValue");
            _lossesTmp = FindTmpTextUnder(_statsRoot, "LossesValue");

            if (debug)
            {
                Debug.Log("[ArenaMenuStats] Bound: " +
                          $"Played(Text={_playedText != null}, TMP={_playedTmp != null}) " +
                          $"Wins(Text={_winsText != null}, TMP={_winsTmp != null}) " +
                          $"Losses(Text={_lossesText != null}, TMP={_lossesTmp != null}).");
            }
        }

        private RectTransform FindStatsRoot()
        {
            // Prefer under a known HUD root, but allow any object by name.
            if (!string.IsNullOrWhiteSpace(hudRootName))
            {
                var hudGo = GameObject.Find(hudRootName);
                if (hudGo != null)
                {
                    var rt = FindRectTransformChildByName(hudGo.transform, statsCardName);
                    if (rt != null) return rt;
                }
            }

            var any = GameObject.Find(statsCardName);
            return any != null ? any.transform as RectTransform : null;
        }

        private bool HasBindings()
        {
            var hasPlayed = _playedText != null || _playedTmp != null;
            var hasWins = _winsText != null || _winsTmp != null;
            var hasLosses = _lossesText != null || _lossesTmp != null;
            return hasPlayed && hasWins && hasLosses;
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            // Slight delay to let NakamaBootstrap initialize if scene was loaded quickly.
            try { await Task.Delay(250, ct); } catch { return; }

            while (!ct.IsCancellationRequested)
            {
                if (!HasBindings())
                    Bind();

                await RefreshAsync(ct);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1f, match3StatsPollSeconds)), ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            if (!HasBindings()) return;

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetUnknown();
                    if (debug) Debug.Log("[ArenaMenuStats] NakamaBootstrap.Instance == null");
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                {
                    SetUnknown();
                    if (debug) Debug.Log($"[ArenaMenuStats] Nakama not ready. IsReady={NakamaBootstrap.Instance.IsReady}");
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsGet, "{}");

                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetUnknown();
                    if (debug) Debug.Log("[ArenaMenuStats] RPC payload empty/null.");
                    return;
                }

                var model = JsonUtility.FromJson<Match3StatsRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    SetUnknown();
                    if (debug) Debug.Log($"[ArenaMenuStats] RPC not ok. payload={payload}");
                    return;
                }

                SetText(ref _playedText, ref _playedTmp, Mathf.Max(0, model.played).ToString());
                SetText(ref _winsText, ref _winsTmp, Mathf.Max(0, model.wins).ToString());
                SetText(ref _lossesText, ref _lossesTmp, Mathf.Max(0, model.losses).ToString());
            }
            catch (Exception e)
            {
                SetUnknown();
                if (debug) Debug.Log("[ArenaMenuStats] Exception: " + e.Message);
            }
        }

        private void SetUnknown()
        {
            SetText(ref _playedText, ref _playedTmp, "—");
            SetText(ref _winsText, ref _winsTmp, "—");
            SetText(ref _lossesText, ref _lossesTmp, "—");
        }

        private static void SetText(ref Text uiText, ref TMP_Text tmpText, string value)
        {
            if (uiText != null) uiText.text = value;
            if (tmpText != null) tmpText.text = value;
        }

        private static Text FindTextUnder(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<Text>(true);
            foreach (var t in all)
            {
                if (t != null && t.gameObject.name == name)
                    return t;
            }
            return null;
        }

        private static TMP_Text FindTmpTextUnder(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in all)
            {
                if (t != null && t.gameObject.name == name)
                    return t;
            }
            return null;
        }

        private static RectTransform FindRectTransformChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<RectTransform>(true);
            foreach (var rt in all)
            {
                if (rt != null && rt.gameObject.name == name)
                    return rt;
            }
            return null;
        }

        [Serializable]
        private sealed class Match3StatsRpcResponse
        {
            public bool ok;
            public int played;
            public int wins;
            public int losses;
            public string err;
        }
    }
}

