using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.UI
{
    /// <summary>
    /// Арена: кнопка «Глазик» открывает Match3StatsCard (выезд слева).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMenuStatsController : MonoBehaviour
    {
        [Header("UI Roots")]
        [SerializeField] private string hudRootName = "ArenaMenuWorld";
        [SerializeField] private string backgroundName = "Background2D";
        [SerializeField] private string statsCardName = "Match3StatsCard";
        [SerializeField] private string eyeButtonName = "StatsToggleEye";

        [Header("Eye (StatsToggleEye)")]
        [Tooltip("Source Image для кнопки StatsToggleEye")]
        [SerializeField] private Sprite statsToggleEyeSourceImage;
        [SerializeField] private Vector2 statsToggleEyeSize = new Vector2(100f, 100f);

        [Header("Polling")]
        [SerializeField, Min(1f)] private float match3StatsPollSeconds = 6f;

        [Header("Debug")]
        [SerializeField] private bool debug;

        private const string RpcMatch3StatsGet = "duel_match3_stats_get";
        private const string DefaultEyeSpritePath = "Assets/_Project/img/affix/4 fragility.png";

        private Match3StatsCardView _card;
        private Button _eyeButton;
        private Image _eyeImage;
        private CancellationTokenSource _cts;
        private Match3StatsRpcResponse _lastModel;

        private void Awake()
        {
            EnsureEyeButton();
            EnsureCard();
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

        private async Task LoopAsync(CancellationToken ct)
        {
            try { await Task.Delay(250, ct); } catch { return; }

            while (!ct.IsCancellationRequested)
            {
                EnsureEyeButton();
                EnsureCard();
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

        private void EnsureEyeButton()
        {
            var parent = ResolveHudParent();
            if (parent == null) return;

            if (_eyeButton == null)
            {
                var eyeRt = FindRectTransformChildByName(parent, eyeButtonName);
                if (eyeRt == null)
                {
                    var go = new GameObject(eyeButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
                    go.layer = 5;
                    eyeRt = go.GetComponent<RectTransform>();
                    eyeRt.SetParent(parent, false);
                    // Слева, чтобы кнопка торчала у стенки.
                    eyeRt.anchorMin = new Vector2(0f, 0.5f);
                    eyeRt.anchorMax = new Vector2(0f, 0.5f);
                    eyeRt.pivot = new Vector2(0f, 0.5f);
                    eyeRt.anchoredPosition = new Vector2(12f, 80f);
                    var img = go.GetComponent<Image>();
                    img.color = Color.white;
                    img.preserveAspect = true;
                    go.GetComponent<Button>().targetGraphic = img;
                }

                _eyeButton = eyeRt.GetComponent<Button>();
                _eyeImage = eyeRt.GetComponent<Image>();
                if (_eyeButton == null) return;
                _eyeButton.onClick.RemoveListener(OnEyeClicked);
                _eyeButton.onClick.AddListener(OnEyeClicked);
            }

            ApplyEyeSourceImageAndSize();
        }

        private void EnsureCard()
        {
            if (_card != null) return;
            var parent = ResolveHudParent();
            if (parent == null) return;

            var existing = FindRectTransformChildByName(parent, statsCardName);
            if (existing != null && existing.Find("Panel") == null)
            {
                // Старый префаб: мигрируем иерархию на месте.
                var migrator = existing.GetComponent<Match3StatsCardView>()
                               ?? existing.gameObject.AddComponent<Match3StatsCardView>();
                migrator.EnsureRuntimeHierarchy();
            }

            if (existing == null)
            {
                var prefab = Resources.Load<GameObject>(Match3StatsCardView.ResourcesPrefabName);
                if (prefab != null)
                {
                    var inst = Instantiate(prefab, parent, false);
                    inst.name = statsCardName;
                    existing = inst.transform as RectTransform;
                }
                else
                {
                    var go = new GameObject(statsCardName, typeof(RectTransform), typeof(Match3StatsCardView));
                    go.layer = 5;
                    existing = go.GetComponent<RectTransform>();
                    existing.SetParent(parent, false);
                }
            }

            _card = existing.GetComponent<Match3StatsCardView>();
            if (_card == null)
                _card = existing.gameObject.AddComponent<Match3StatsCardView>();
            _card.EnsureRuntimeHierarchy();
            _card.Closed -= OnCardClosed;
            _card.Closed += OnCardClosed;
            if (_lastModel != null)
                ApplyModel(_lastModel);
        }

        private Transform ResolveHudParent()
        {
            Transform hud = null;
            if (!string.IsNullOrWhiteSpace(hudRootName))
            {
                var go = GameObject.Find(hudRootName);
                if (go != null) hud = go.transform;
            }
            if (hud == null) return null;
            if (!string.IsNullOrWhiteSpace(backgroundName))
            {
                var bg = hud.Find(backgroundName);
                if (bg != null) return bg;
            }
            return hud;
        }

        private void OnEyeClicked()
        {
            EnsureCard();
            if (_card == null) return;
            if (_card.IsOpen)
            {
                _card.Hide();
                return;
            }
            if (_lastModel != null)
                ApplyModel(_lastModel);
            else
                _ = RefreshAsync(CancellationToken.None);
            _card.Show();
            UpdateEyeVisual(open: true);
        }

        private void OnCardClosed()
        {
            UpdateEyeVisual(open: false);
        }

        private void UpdateEyeVisual(bool open)
        {
            // Пока статистика открыта — кнопку прячем, чтобы не перекрывала таблицу.
            if (_eyeButton != null)
                _eyeButton.gameObject.SetActive(!open);
            if (!open)
                ApplyEyeSourceImageAndSize();
        }

        private void ApplyEyeSourceImageAndSize()
        {
#if UNITY_EDITOR
            if (statsToggleEyeSourceImage == null)
                statsToggleEyeSourceImage = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultEyeSpritePath);
#endif
            if (_eyeButton == null) return;

            var eyeRt = _eyeButton.transform as RectTransform;
            if (eyeRt != null)
            {
                var size = statsToggleEyeSize;
                if (size.x < 1f) size.x = 100f;
                if (size.y < 1f) size.y = 100f;
                eyeRt.sizeDelta = size;
            }

            if (_eyeImage == null)
                _eyeImage = _eyeButton.GetComponent<Image>();
            if (_eyeImage == null) return;

            if (statsToggleEyeSourceImage != null)
                _eyeImage.sprite = statsToggleEyeSourceImage;
            _eyeImage.color = Color.white;
            _eyeImage.preserveAspect = true;
            _eyeImage.raycastTarget = true;
            if (_eyeButton.targetGraphic == null)
                _eyeButton.targetGraphic = _eyeImage;
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            try
            {
                if (NakamaBootstrap.Instance == null) return;
                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady
                    || NakamaBootstrap.Instance.Client == null
                    || NakamaBootstrap.Instance.Session == null)
                    return;

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsGet, "{}");
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload)) return;

                var model = JsonUtility.FromJson<Match3StatsRpcResponse>(payload);
                if (model == null || !model.ok) return;
                _lastModel = model;
                if (_card != null && _card.IsOpen)
                    ApplyModel(model);
            }
            catch (Exception e)
            {
                if (debug) Debug.Log("[ArenaMenuStats] Exception: " + e.Message);
            }
        }

        private void ApplyModel(Match3StatsRpcResponse model)
        {
            if (_card == null || model == null) return;
            _card.SetTitle("Статистика");
            _card.BindStats(
                model.played,
                model.wins,
                model.losses,
                model.modes,
                model.arena_tournaments,
                model.mine_total_wins,
                model.mine_floors);
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
            public Match3StatsCardView.ModeRow[] modes;
            public Match3StatsCardView.ArenaRow[] arena_tournaments;
            public int mine_total_wins;
            public Match3StatsCardView.MineFloorRow[] mine_floors;
            public string err;
        }
    }
}
