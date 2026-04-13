using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class MineSceneController : MonoBehaviour
    {
        [SerializeField] private MonsterCatalog monsterCatalog;
        [SerializeField] private MonsterFrameCatalog monsterFrameCatalog;
        [SerializeField] private AffixCatalog affixCatalog;
        [SerializeField] private Sprite keyIconSprite;
        [SerializeField] private Sprite oreIconSprite;
        [SerializeField] private Sprite goldIconSprite;
        [SerializeField] private Sprite matterIconSprite;
        [SerializeField] private Sprite ingotsIconSprite;
        [SerializeField] private Sprite expIconSprite;
        [Tooltip("Необязательно: если пусто, загружается Resources/UI/MonsterModal.")]
        [SerializeField] private GameObject monsterModalPrefab;
        private const string RpcPveCatalogGet = "duel_match3_pve_catalog_get";
        private const string RpcMineSummon = "duel_mine_summon";
        private const string RpcMineAffixReroll = "duel_mine_affix_reroll";
        private const string RpcMineBarrierUnlock = "duel_mine_barrier_unlock";
        private const int AffixRerollGoldCost = 100;
        private const int SummonEnergyCost = 5;
        private const int SummonGoldCost = 50;
        private const float CountdownTickSeconds = 1f;
        private const float ServerRefreshIntervalSeconds = 15f;
        private const float ResourcesRefreshIntervalSeconds = 5f;
        private const string DuelMatch3SceneName = "DuelMatch3";
        private const string HudKeyIconAssetPath = "Assets/_Project/img/resources_hud/key.png";
        private const string HudOreIconAssetPath = "Assets/_Project/img/resources_hud/ore.png";
        private const string HudGoldIconAssetPath = "Assets/_Project/img/resources_hud/gold.png";
        private const string HudMatterIconAssetPath = "Assets/_Project/img/resources_hud/matter.png";
        private const string HudIngotsIconAssetPath = "Assets/_Project/img/resources_hud/ingots.png";
        private const string HudExpIconAssetPath = "Assets/_Project/img/resources_hud/exp.png";

        private readonly Dictionary<int, FloorRowRefs> _rows = new();
        private readonly Dictionary<int, Button> _liftButtons = new();
        private readonly Dictionary<int, PveBotInfo> _botByFloor = new();
        private readonly Dictionary<int, MineFloorInfo> _mineByFloor = new();
        private string _difficulty = "easy";
        private CancellationTokenSource _cts;
        private ScrollRect _cardsScroll;

        private GameObject _modalRoot;
        private Text _modalTitle;
        private Text _modalSupplementalInfo;
        private Text _modalBarrierInfo;
        private Button _modalFightButton;
        private Button _modalDismissButton;
        private Button _modalCloseButton;
        private Image _modalAffixIcon;
        private Text _modalAffixIconText;
        private Text _modalAffixTitleText;
        private Text _modalAffixDescriptionText;
        private Text[] _modalStatTexts;
        private GameObject _monsterContentRoot;
        private GameObject _barrierContentRoot;
        private Text _modalMonsterRewardsTitle;
        private RectTransform _modalMonsterRewardsColumnsRoot;
        private RectTransform _modalBarrierRequirementsRoot;
        private Text _modalBarrierRequirementsTitle;
        private int _selectedFloor;
        private bool _modalCanSummon;
        private bool _modalCanUnlock;
        private bool _refreshInFlight;
        private bool _summonInFlight;
        private bool _rerollInFlight;
        private bool _unlockInFlight;
        private float _countdownAccumulator;
        private float _serverRefreshAccumulator;
        private float _resourcesRefreshAccumulator;
        private ProgressionInfo _progression;
        private PlayerResourcesRpcResponse _lastResources;
        private Transform _headerResourcesRoot;
        private Sprite _lockSprite;
        private Sprite _oreSprite;
        private Sprite _goldSprite;
        private Sprite _matterSprite;
        private Sprite _ingotsSprite;
        private Sprite _expSprite;
        private readonly ResourceValueBinding _energyBinding = new("Energy");
        private readonly ResourceValueBinding _oreBinding = new("ore");
        private readonly ResourceValueBinding _goldBinding = new("Gold");
        private readonly ResourceValueBinding _ingotsBinding = new("ingots");
        private readonly ResourceValueBinding _matterBinding = new("matter");
        private readonly ResourceValueBinding _keysBinding = new("keys");
        private static readonly Dictionary<int, BarrierRequirement> BarrierRequirements = new()
        {
            [2] = new BarrierRequirement { ore = 100 },
            [3] = new BarrierRequirement { ore = 350 },
            [4] = new BarrierRequirement { ore = 800 },
            [5] = new BarrierRequirement { ore = 1500, key_id = "miner_key", key_amount = 1, gold = 2000 },
            [6] = new BarrierRequirement { ore = 2500 },
            [7] = new BarrierRequirement { ore = 3800 },
            [8] = new BarrierRequirement { ore = 5500 },
            [9] = new BarrierRequirement { ore = 7500, key_id = "dark_key", key_amount = 1, gold = 10000 },
            [10] = new BarrierRequirement { ore = 10000 },
            [11] = new BarrierRequirement { ore = 13000 },
            [12] = new BarrierRequirement { ore = 17000, matter = 500, gold = 25000 },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            var scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.name, "MineScene", StringComparison.OrdinalIgnoreCase))
                return;

            if (FindFirstObjectByType<MineSceneController>(FindObjectsInactive.Include) != null)
                return;

            var go = new GameObject("MineSceneController");
            go.AddComponent<MineSceneController>();
        }

        private void Awake()
        {
            if (affixCatalog == null)
                affixCatalog = Resources.Load<AffixCatalog>("Match3/AffixCatalog");
            EnsureHudIconReferences();
            _cardsScroll = FindFirstObjectByType<ScrollRect>(FindObjectsInactive.Include);
            CacheRows();
            EnsureModal();
            EnsureHeaderResources();
        }

        private void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _ = RefreshAsync(_cts.Token);
            _ = RefreshResourcesAsync(_cts.Token);
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _refreshInFlight = false;
            _summonInFlight = false;
            _rerollInFlight = false;
            _unlockInFlight = false;
            _countdownAccumulator = 0f;
            _serverRefreshAccumulator = 0f;
            _resourcesRefreshAccumulator = 0f;
        }

        private void Update()
        {
            if (_rows.Count == 0 || _cts == null || _cts.IsCancellationRequested)
                return;

            var dt = Mathf.Max(0f, Time.unscaledDeltaTime);
            _countdownAccumulator += dt;
            _serverRefreshAccumulator += dt;
            _resourcesRefreshAccumulator += dt;

            var changed = false;
            while (_countdownAccumulator >= CountdownTickSeconds)
            {
                _countdownAccumulator -= CountdownTickSeconds;
                foreach (var kv in _mineByFloor)
                {
                    var floorInfo = kv.Value;
                    if (floorInfo == null || floorInfo.respawn_left_seconds <= 0) continue;
                    floorInfo.respawn_left_seconds = Mathf.Max(0, floorInfo.respawn_left_seconds - 1);
                    changed = true;
                }
            }

            if (changed)
            {
                ApplyRows();
                if (_modalRoot != null && _modalRoot.activeSelf)
                    OpenMonsterModal(_selectedFloor);
            }

            if (_serverRefreshAccumulator >= ServerRefreshIntervalSeconds && !_refreshInFlight)
            {
                _serverRefreshAccumulator = 0f;
                _ = RefreshAsync(_cts.Token);
            }

            if (_resourcesRefreshAccumulator >= ResourcesRefreshIntervalSeconds)
            {
                _resourcesRefreshAccumulator = 0f;
                _ = RefreshResourcesAsync(_cts.Token);
            }
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            if (_refreshInFlight) return;
            _refreshInFlight = true;
            try
            {
                if (NakamaBootstrap.Instance == null) return;
                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                    return;

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcPveCatalogGet, "{}", canceller: ct);
                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload)) return;

                var model = JsonUtility.FromJson<MineCatalogResponse>(payload);
                if (model == null || !model.ok) return;
                _progression = model.progression;

                _botByFloor.Clear();
                _mineByFloor.Clear();
                if (model.bots != null)
                {
                    foreach (var b in model.bots)
                    {
                        if (b == null) continue;
                        _botByFloor[Mathf.Max(1, b.floor)] = b;
                    }
                }
                if (model.mine_floors != null)
                {
                    foreach (var m in model.mine_floors)
                    {
                        if (m == null) continue;
                        _mineByFloor[Mathf.Max(1, m.floor)] = m;
                    }
                }
                _difficulty = model.mine_difficulty;
                if (string.IsNullOrWhiteSpace(_difficulty))
                    _difficulty = model.progression != null && model.progression.mine != null ? model.progression.mine.current_difficulty : "easy";
                if (string.IsNullOrWhiteSpace(_difficulty))
                    _difficulty = "easy";

                ApplyRows();
                ApplyResourcesFallbackFromProgression();
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Refresh failed: " + e.Message);
            }
            finally
            {
                _refreshInFlight = false;
            }
        }

        private void CacheRows()
        {
            _rows.Clear();
            _liftButtons.Clear();
            for (var floor = 1; floor <= 12; floor++)
            {
                var rowGo = GameObject.Find("Floor_" + floor);
                if (rowGo == null) continue;

                var refs = new FloorRowRefs();
                refs.root = rowGo.transform;
                refs.stateText = FindTextByName(refs.root, "StateText") ?? FindTextByName(refs.root, "Barrier");
                var rowLayout = refs.root.GetComponent<LayoutElement>();
                if (rowLayout != null)
                    rowLayout.preferredHeight = 500f;
                var rewardsPanel = refs.root.Find("RewardsPanel");
                if (rewardsPanel != null)
                    Destroy(rewardsPanel.gameObject);
                refs.monsterButton = EnsureMonsterButton(refs.root, floor);
                refs.monsterButton.onClick.RemoveAllListeners();
                refs.lockButton = EnsureLockButton(refs.root, floor);
                refs.lockButton.onClick.RemoveAllListeners();
                var lockImage = refs.lockButton.GetComponent<Image>();
                var lockLabel = refs.lockButton.GetComponentInChildren<Text>(true);
                ApplyLockButtonSprite(lockImage, lockLabel);
                var f = floor;
                refs.monsterButton.onClick.AddListener(() => OpenMonsterModal(f));
                refs.lockButton.onClick.AddListener(() => OpenMonsterModal(f));
                EnsureTorchViewportCulling(refs.root);
                refs.root.SetSiblingIndex(floor - 1); // 1-й этаж вверху, глубже — ниже.
                _rows[floor] = refs;

                var lift = GameObject.Find("LiftFloor_" + floor);
                if (lift != null)
                {
                    var btn = lift.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => FocusFloor(f));
                        _liftButtons[floor] = btn;
                    }
                }
            }
        }

        private Button EnsureMonsterButton(Transform rowRoot, int floor)
        {
            var existing = rowRoot.Find("MonsterButton");
            if (existing != null)
                return existing.GetComponent<Button>() ?? existing.gameObject.AddComponent<Button>();

            var go = new GameObject("MonsterButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(rowRoot, false);
            rt.anchorMin = new Vector2(0.36f, 0.15f);
            rt.anchorMax = new Vector2(0.64f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.50f, 0.20f, 0.20f, 0.95f);
            var btn = go.GetComponent<Button>();

            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.SetParent(go.transform, false);
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = txtGo.GetComponent<Text>();
            txt.font = GetBuiltinFont();
            txt.fontSize = 16;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = "БОТ";
            txt.raycastTarget = false;

            return btn;
        }

        private Button EnsureLockButton(Transform rowRoot, int floor)
        {
            var existing = rowRoot.Find("LockButton");
            if (existing != null)
                return existing.GetComponent<Button>() ?? existing.gameObject.AddComponent<Button>();

            var go = new GameObject("LockButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(rowRoot, false);
            rt.anchorMin = new Vector2(0.43f, 0.22f);
            rt.anchorMax = new Vector2(0.57f, 0.78f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.26f, 0.22f, 0.16f, 0.95f);
            var btn = go.GetComponent<Button>();

            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.SetParent(go.transform, false);
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = txtGo.GetComponent<Text>();
            txt.font = GetBuiltinFont();
            txt.fontSize = 30;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(1f, 0.94f, 0.75f, 1f);
            txt.text = "🔒";
            txt.raycastTarget = false;

            ApplyLockButtonSprite(img, txt);
            return btn;
        }

        private void ApplyLockButtonSprite(Image iconImage, Text fallbackLabel)
        {
            if (iconImage == null)
                return;

            _lockSprite ??= LoadSpriteAsset(HudKeyIconAssetPath);
            if (_lockSprite != null)
            {
                iconImage.sprite = _lockSprite;
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
                iconImage.color = Color.white;
                if (fallbackLabel != null)
                    fallbackLabel.gameObject.SetActive(false);
                return;
            }

            iconImage.sprite = null;
            iconImage.color = new Color(0.26f, 0.22f, 0.16f, 0.95f);
            if (fallbackLabel != null)
                fallbackLabel.gameObject.SetActive(true);
        }

        private void ApplyRows()
        {
            foreach (var kv in _rows)
            {
                var floor = kv.Key;
                var refs = kv.Value;
                _mineByFloor.TryGetValue(floor, out var mf);
                var unlocked = mf != null && mf.unlocked;
                var respawn = mf != null ? Mathf.Max(0, mf.respawn_left_seconds) : 0;
                ApplyFloorLightingState(refs.root, !unlocked);

                if (!unlocked)
                {
                    if (refs.stateText != null) refs.stateText.text = "Барьер";
                    SetButtonVisible(refs.monsterButton, false);
                    SetButtonVisible(refs.lockButton, true);
                    refs.lockButton.interactable = true;
                    continue;
                }

                SetButtonVisible(refs.monsterButton, true);
                SetButtonVisible(refs.lockButton, false);

                if (respawn > 0)
                {
                    if (refs.stateText != null) refs.stateText.text = "До появления: " + FormatSeconds(respawn);
                    SetButtonLabel(refs.monsterButton, "КД");
                    refs.monsterButton.interactable = true;
                }
                else
                {
                    if (refs.stateText != null) refs.stateText.text = "Монстр готов";
                    SetButtonLabel(refs.monsterButton, "БОТ");
                    refs.monsterButton.interactable = true;
                }

                _botByFloor.TryGetValue(floor, out var bot);
                ApplyMonsterVisual(refs, bot, mf);
            }
        }

        private static void ApplyFloorLightingState(Transform rowRoot, bool isLocked)
        {
            if (rowRoot == null)
                return;

            var lighting = rowRoot.GetComponent<UiFloorTorchLighting>();
            if (lighting != null)
                lighting.SetLockedState(isLocked);
        }

        private void EnsureTorchViewportCulling(Transform rowRoot)
        {
            if (rowRoot == null)
                return;

            var torchRoot = rowRoot.Find("Torch_Prefab");
            if (torchRoot == null)
                return;

            var culler = torchRoot.GetComponent<UiViewportSpriteCuller>();
            if (culler == null)
                culler = torchRoot.gameObject.AddComponent<UiViewportSpriteCuller>();

            if (_cardsScroll != null && _cardsScroll.viewport != null)
                culler.SetViewport(_cardsScroll.viewport);
        }

        private void ApplyMonsterVisual(FloorRowRefs refs, PveBotInfo bot, MineFloorInfo floorInfo)
        {
            if (refs == null || refs.monsterButton == null) return;

            var frame = refs.monsterButton.GetComponent<Image>();
            var icon = FindImageByName(refs.monsterButton.transform, "Icon");
            if (icon == null) icon = EnsureIconImage(refs.monsterButton.transform);

            var def = ResolveMonsterDefinition(bot);
            if (icon != null)
            {
                icon.sprite = def != null ? def.Icon : null;
                icon.color = icon.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            }

            if (frame != null)
            {
                var isBoss = floorInfo != null && floorInfo.is_boss;
                var frameSprite = monsterFrameCatalog != null
                    ? monsterFrameCatalog.GetFrame(_difficulty, isBoss)
                    : (def != null ? def.GetFrame(_difficulty, isBoss) : null);
                frame.sprite = frameSprite;
                frame.type = frameSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            }
        }

        private MonsterDefinition ResolveMonsterDefinition(PveBotInfo bot)
        {
            if (monsterCatalog == null || bot == null) return null;
            var byId = monsterCatalog.GetByBotId(bot.id);
            if (byId != null) return byId;
            return monsterCatalog.GetByFloor(bot.floor);
        }

        private void FocusFloor(int floor)
        {
            if (!_rows.ContainsKey(floor))
                return;

            if (_cardsScroll != null && _cardsScroll.content != null)
            {
                var normalized = 1f - Mathf.Clamp01((floor - 1f) / 11f);
                _cardsScroll.verticalNormalizedPosition = normalized;
            }

            OpenMonsterModal(floor);
        }

        private void EnsureModal()
        {
            if (_modalRoot != null) return;

            var parent = FindMonsterModalParent();
            if (parent == null) return;

            var prefab = monsterModalPrefab != null
                ? monsterModalPrefab
                : Resources.Load<GameObject>("UI/MonsterModal");
            if (prefab == null)
            {
                Debug.LogError(
                    "[MineScene] Не найден префаб модалки. Выполните в редакторе: Tools/UI/Создать префаб Monster Modal " +
                    "(создаёт Assets/_Project/Resources/UI/MonsterModal.prefab) или назначьте поле Monster Modal Prefab на MineSceneController.");
                return;
            }

            _modalRoot = Instantiate(prefab, parent, false);
            var view = _modalRoot.GetComponent<MonsterModalView>();
            if (view == null)
            {
                Debug.LogError("[MineScene] У префаба MonsterModal нет компонента MonsterModalView.");
                Destroy(_modalRoot);
                _modalRoot = null;
                return;
            }

            _modalTitle = view.TitleText;
            _modalCloseButton = view.CloseButton;
            _modalSupplementalInfo = view.SupplementalInfoText;
            _modalBarrierInfo = view.BarrierInfoText;
            _modalFightButton = view.FightButton;
            _modalDismissButton = view.DismissButton;
            _modalAffixIcon = view.AffixIcon;
            _modalAffixIconText = view.AffixIconGlyph;
            _modalAffixTitleText = view.AffixTitleText;
            _modalAffixDescriptionText = view.AffixDescriptionText;
            _modalStatTexts = view.StatTexts;
            _monsterContentRoot = view.MonsterContentRoot;
            _barrierContentRoot = view.BarrierContentRoot;
            _modalMonsterRewardsTitle = view.RewardsSectionTitle;
            _modalMonsterRewardsColumnsRoot = view.RewardsDynamicRoot;
            _modalBarrierRequirementsTitle = view.BarrierRequirementsSectionTitle;
            _modalBarrierRequirementsRoot = view.BarrierRequirementsRoot;

            _modalCloseButton.onClick.AddListener(() => _modalRoot.SetActive(false));
            _modalFightButton.onClick.AddListener(OnFightClicked);
            _modalDismissButton.onClick.AddListener(HandleSecondaryButtonClicked);
            _modalRoot.SetActive(false);
        }

        private static Transform FindMonsterModalParent()
        {
            var canvasGo = GameObject.Find("MineCanvas");
            if (canvasGo != null)
                return canvasGo.transform;
            var bg = GameObject.Find("MineBackground");
            if (bg != null)
                return bg.transform;
            return FindFirstObjectByType<Canvas>()?.transform;
        }

        private void OpenMonsterModal(int floor)
        {
            if (_modalRoot == null) return;
            _selectedFloor = floor;
            _mineByFloor.TryGetValue(floor, out var mine);
            _botByFloor.TryGetValue(floor, out var bot);

            if (_modalSupplementalInfo != null)
                _modalSupplementalInfo.text = string.Empty;

            var unlocked = mine != null && mine.unlocked;
            var respawn = mine != null ? Mathf.Max(0, mine.respawn_left_seconds) : 0;
            var affix = mine != null ? mine.affix : "";
            ApplyAffixVisual(affix);

            if (!unlocked)
            {
                var req = GetBarrierRequirement(floor);
                if (_monsterContentRoot != null)
                    _monsterContentRoot.SetActive(false);
                if (_barrierContentRoot != null)
                    _barrierContentRoot.SetActive(true);
                _modalTitle.text = $"Барьер этажа {floor}";
                if (_modalBarrierInfo != null)
                    _modalBarrierInfo.text = BuildBarrierInfoText(floor, req);
                PopulateBarrierRequirements(req);
                _modalCanUnlock = req != null;
                _modalCanSummon = false;
                _modalFightButton.interactable = false;
                SetButtonLabel(_modalDismissButton, req != null ? "Разбить" : "Закрыть");
                _modalDismissButton.interactable = true;
                _modalRoot.SetActive(true);
                return;
            }

            if (_monsterContentRoot != null)
                _monsterContentRoot.SetActive(true);
            if (_barrierContentRoot != null)
                _barrierContentRoot.SetActive(false);

            var name = bot != null ? bot.name : ("Монстр " + floor);
            var hp = 150 + Mathf.Max(0, bot != null ? bot.hp_bonus : 0);
            var dmg = bot != null ? bot.base_damage : 0;
            var armor = bot != null ? bot.base_armor : 0;
            var crit = bot != null ? bot.base_crit : 0f;
            var mana = bot != null ? bot.start_mana : 0;

            _modalTitle.text = name;
            ApplyMonsterStatTexts(hp, dmg, armor, crit, mana, respawn);

            PopulateMonsterRewards(bot);

            _modalCanUnlock = false;
            _modalCanSummon = respawn > 0 && bot != null && !IsMineBossFloor(floor);
            _modalFightButton.interactable = respawn <= 0 && bot != null;
            SetButtonLabel(_modalDismissButton, _modalCanSummon
                ? $"Призвать ({SummonEnergyCost} эн / {SummonGoldCost} зол)"
                : $"Прогнать ({AffixRerollGoldCost} зол)");
            _modalDismissButton.interactable = true;
            _modalRoot.SetActive(true);
        }

        private void ApplyMonsterStatTexts(int hp, int dmg, int armor, float crit, int mana, int respawnSec)
        {
            if (_modalStatTexts == null || _modalStatTexts.Length < 6)
                return;
            _modalStatTexts[0].text = $"HP: {hp}";
            _modalStatTexts[1].text = $"Урон: {dmg}";
            _modalStatTexts[2].text = $"Броня: {armor}";
            _modalStatTexts[3].text = $"Крит: {Mathf.RoundToInt(crit * 100f)}%";
            _modalStatTexts[4].text = $"Мана: {mana}";
            _modalStatTexts[5].text = respawnSec > 0 ? $"Появится: {FormatSeconds(respawnSec)}" : "—";
        }

        private async void OnFightClicked()
        {
            _botByFloor.TryGetValue(_selectedFloor, out var bot);
            if (bot == null) return;

            try
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    var resources = _lastResources ?? await PlayerResourcesService.GetAsync(_cts.Token);
                    if (resources != null && resources.ok)
                    {
                        _lastResources = resources;
                        ApplyHeaderResourceValues(resources);
                        if (resources.energy < 15)
                        {
                            if (_modalSupplementalInfo != null)
                                _modalSupplementalInfo.text += $"\n\nНе хватает энергии: нужно 15, доступно {resources.energy}.";
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // if precheck fails, allow server-authoritative check below
            }

            Match3LaunchContext.SetSoloMine(bot.id, _selectedFloor, _difficulty, true);
            SceneManager.LoadScene(DuelMatch3SceneName);
        }

        private async void HandleSecondaryButtonClicked()
        {
            if (_modalCanUnlock)
            {
                if (_unlockInFlight) return;
                await UnlockSelectedFloorAsync();
                return;
            }

            _mineByFloor.TryGetValue(_selectedFloor, out var mineUnlocked);
            var unlockedFloor = mineUnlocked != null && mineUnlocked.unlocked;
            if (!unlockedFloor)
            {
                if (_modalRoot != null)
                    _modalRoot.SetActive(false);
                return;
            }

            if (_modalCanSummon)
            {
                if (_summonInFlight)
                    return;
                await SummonSelectedFloorAsync();
                return;
            }

            if (_rerollInFlight)
                return;

            await RerollAffixAsync();
        }

        private async Task SummonSelectedFloorAsync()
        {
            if (NakamaBootstrap.Instance == null || _cts == null || _cts.IsCancellationRequested)
                return;

            _summonInFlight = true;
            _modalDismissButton.interactable = false;

            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                    return;

                var request = new SummonRequest
                {
                    floor = _selectedFloor,
                    difficulty = _difficulty,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch()
                };
                var reqJson = JsonUtility.ToJson(request);
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMineSummon, reqJson, canceller: _cts.Token);
                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                var model = JsonUtility.FromJson<SummonResponse>(payload);
                if (model == null)
                    return;

                if (!model.ok)
                {
                    if (_modalSupplementalInfo != null)
                        _modalSupplementalInfo.text += "\n\n" + DescribeSummonError(model);
                    return;
                }

                if (_mineByFloor.TryGetValue(_selectedFloor, out var mine) && mine != null)
                {
                    mine.respawn_left_seconds = 0;
                }

                ApplyRows();
                OpenMonsterModal(_selectedFloor);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Summon failed: " + e.Message);
            }
            finally
            {
                _summonInFlight = false;
                if (_modalDismissButton != null)
                    _modalDismissButton.interactable = true;
            }
        }

        private static bool IsMineBossFloor(int floor) => floor == 4 || floor == 8 || floor == 12;

        private async Task RerollAffixAsync()
        {
            if (NakamaBootstrap.Instance == null || _cts == null || _cts.IsCancellationRequested)
                return;

            _rerollInFlight = true;
            _modalDismissButton.interactable = false;

            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                    return;

                var request = new AffixRerollRequest
                {
                    floor = _selectedFloor,
                    difficulty = _difficulty,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch()
                };
                var reqJson = JsonUtility.ToJson(request);
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMineAffixReroll, reqJson, canceller: _cts.Token);
                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                var model = JsonUtility.FromJson<AffixRerollResponse>(payload);
                if (model == null)
                    return;

                if (!model.ok)
                {
                    if (_modalSupplementalInfo != null)
                        _modalSupplementalInfo.text += "\n\n" + DescribeAffixRerollError(model);
                    return;
                }

                if (_mineByFloor.TryGetValue(_selectedFloor, out var mine) && mine != null && !string.IsNullOrWhiteSpace(model.affix))
                    mine.affix = model.affix;

                ApplyRows();
                await RefreshResourcesAsync(_cts.Token);
                OpenMonsterModal(_selectedFloor);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Affix reroll failed: " + e.Message);
            }
            finally
            {
                _rerollInFlight = false;
                if (_modalDismissButton != null)
                    _modalDismissButton.interactable = true;
            }
        }

        private static string DescribeSummonError(SummonResponse response)
        {
            if (response == null) return "Не удалось призвать монстра.";
            switch (response.err)
            {
                case "not_enough_energy":
                    return $"Не хватает энергии: нужно {response.required}, доступно {response.energy}.";
                case "not_enough_gold":
                    return $"Не хватает золота: нужно {response.required}, доступно {response.gold}.";
                case "barrier_locked":
                    return "Этаж пока закрыт барьером.";
                case "boss_summon_forbidden":
                    return "Призыв недоступен на этажах с боссом (4, 8, 12): ждите окончания таймера.";
                default:
                    return "Не удалось призвать монстра.";
            }
        }

        private static string DescribeAffixRerollError(AffixRerollResponse response)
        {
            if (response == null) return "Не удалось сменить аффикс.";
            switch (response.err)
            {
                case "not_enough_gold":
                    return $"Не хватает золота: нужно {response.required}, доступно {response.gold}.";
                case "barrier_locked":
                    return "Этаж пока закрыт барьером.";
                default:
                    return "Не удалось сменить аффикс.";
            }
        }

        private async Task UnlockSelectedFloorAsync()
        {
            if (NakamaBootstrap.Instance == null || _cts == null || _cts.IsCancellationRequested)
                return;

            _unlockInFlight = true;
            _modalDismissButton.interactable = false;
            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                    return;

                var request = new BarrierUnlockRequest
                {
                    floor = _selectedFloor,
                    difficulty = _difficulty,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch()
                };
                var reqJson = JsonUtility.ToJson(request);
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMineBarrierUnlock, reqJson, canceller: _cts.Token);
                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                var model = JsonUtility.FromJson<BarrierUnlockResponse>(payload);
                if (model == null)
                    return;

                if (!model.ok)
                {
                    if (_modalSupplementalInfo != null)
                        _modalSupplementalInfo.text += "\n\n" + DescribeUnlockError(model);
                    return;
                }

                await RefreshAsync(_cts.Token);
                await RefreshResourcesAsync(_cts.Token);
                OpenMonsterModal(_selectedFloor);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Unlock failed: " + e.Message);
            }
            finally
            {
                _unlockInFlight = false;
                if (_modalDismissButton != null)
                    _modalDismissButton.interactable = true;
            }
        }

        private static string DescribeUnlockError(BarrierUnlockResponse response)
        {
            if (response == null) return "Не удалось разбить барьер.";
            switch (response.err)
            {
                case "level_too_low":
                    return $"Нужен уровень {response.required_level}.";
                case "prev_floor_locked":
                    return "Сначала разбейте барьер на предыдущем этаже.";
                case "not_enough_ore":
                    return $"Не хватает руды: нужно {response.required}, есть {response.ore}.";
                case "not_enough_gold":
                    return $"Не хватает золота: нужно {response.required}, есть {response.gold}.";
                case "not_enough_matter":
                    return $"Не хватает материи: нужно {response.required}, есть {response.matter}.";
                case "not_enough_key_item":
                    return $"Не хватает ключа {response.key_id}: нужно {response.required}, есть {response.have}.";
                default:
                    return "Не удалось разбить барьер.";
            }
        }

        private async Task RefreshResourcesAsync(CancellationToken ct)
        {
            try
            {
                var model = await PlayerResourcesService.GetAsync(ct);
                if (model == null || !model.ok)
                    return;
                _lastResources = model;
                ApplyHeaderResourceValues(model);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Resource refresh failed: " + e.Message);
            }
        }

        private void ApplyResourcesFallbackFromProgression()
        {
            if (_lastResources != null || _progression == null)
                return;

            _lastResources = new PlayerResourcesRpcResponse
            {
                ok = true,
                energy = _progression.energy,
                energy_max = _progression.energy_max,
                ore = _progression.ore,
                gold = _progression.gold,
                ingots = _progression.ingots,
                matter = _progression.matter,
                keys = (_progression.key_items != null ? _progression.key_items.miner_key : 0) +
                       (_progression.key_items != null ? _progression.key_items.dark_key : 0),
            };
            ApplyHeaderResourceValues(_lastResources);
        }

        private void EnsureHeaderResources()
        {
            if (_headerResourcesRoot == null)
                _headerResourcesRoot = FindHeaderResourcesRoot();
            if (_headerResourcesRoot == null)
                return;

            BindHeaderResource(_energyBinding, _headerResourcesRoot);
            BindHeaderResource(_oreBinding, _headerResourcesRoot);
            BindHeaderResource(_goldBinding, _headerResourcesRoot);
            BindHeaderResource(_ingotsBinding, _headerResourcesRoot);
            BindHeaderResource(_matterBinding, _headerResourcesRoot);
            BindHeaderResource(_keysBinding, _headerResourcesRoot);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            TryAutoAssignHudIconReferencesInEditor();
        }
#endif

        private void EnsureHudIconReferences()
        {
            _lockSprite = keyIconSprite;
            _oreSprite = oreIconSprite;
            _goldSprite = goldIconSprite;
            _matterSprite = matterIconSprite;
            _ingotsSprite = ingotsIconSprite;
            _expSprite = expIconSprite;

            if (_lockSprite == null) _lockSprite = LoadSpriteAsset(HudKeyIconAssetPath);
            if (_oreSprite == null) _oreSprite = LoadSpriteAsset(HudOreIconAssetPath);
            if (_goldSprite == null) _goldSprite = LoadSpriteAsset(HudGoldIconAssetPath);
            if (_matterSprite == null) _matterSprite = LoadSpriteAsset(HudMatterIconAssetPath);
            if (_ingotsSprite == null) _ingotsSprite = LoadSpriteAsset(HudIngotsIconAssetPath);
            if (_expSprite == null) _expSprite = LoadSpriteAsset(HudExpIconAssetPath);
        }

#if UNITY_EDITOR
        private void TryAutoAssignHudIconReferencesInEditor()
        {
            var changed = false;
            if (keyIconSprite == null)
            {
                keyIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudKeyIconAssetPath);
                changed |= keyIconSprite != null;
            }

            if (oreIconSprite == null)
            {
                oreIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudOreIconAssetPath);
                changed |= oreIconSprite != null;
            }

            if (goldIconSprite == null)
            {
                goldIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudGoldIconAssetPath);
                changed |= goldIconSprite != null;
            }

            if (matterIconSprite == null)
            {
                matterIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudMatterIconAssetPath);
                changed |= matterIconSprite != null;
            }

            if (ingotsIconSprite == null)
            {
                ingotsIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudIngotsIconAssetPath);
                changed |= ingotsIconSprite != null;
            }

            if (expIconSprite == null)
            {
                expIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudExpIconAssetPath);
                changed |= expIconSprite != null;
            }

            if (changed)
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static Transform FindHeaderResourcesRoot()
        {
            var go = GameObject.Find("HeaderResources");
            return go != null ? go.transform : null;
        }

        private Transform CreateHeaderResourcesRoot()
        {
            var bg = GameObject.Find("MineBackground");
            var parent = bg != null ? bg.transform : FindFirstObjectByType<Canvas>()?.transform;
            if (parent == null) return null;

            var rootGo = new GameObject("HeaderResources", typeof(RectTransform), typeof(Image));
            var rt = rootGo.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.20f, 0.90f);
            rt.anchorMax = new Vector2(0.96f, 0.985f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rootGo.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.92f);

            var entries = new[] { "Energy", "ore", "Gold", "ingots", "matter", "keys" };
            var width = 1f / entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = new GameObject(entries[i], typeof(RectTransform));
                var entryRt = entry.GetComponent<RectTransform>();
                entryRt.SetParent(rt, false);
                entryRt.anchorMin = new Vector2(i * width, 0f);
                entryRt.anchorMax = new Vector2((i + 1) * width, 1f);
                entryRt.offsetMin = new Vector2(2f, 2f);
                entryRt.offsetMax = new Vector2(-2f, -2f);

                var label = CreateText(entryRt, "Label", entries[i], 12, TextAnchor.UpperCenter, new Vector2(0f, 0.48f), new Vector2(1f, 1f));
                label.color = new Color(0.82f, 0.88f, 0.98f);
                var value = CreateText(entryRt, "Value", "—", 15, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(1f, 0.58f));
                value.color = Color.white;
            }

            return rt;
        }

        private void ApplyHeaderResourceValues(PlayerResourcesRpcResponse model)
        {
            if (model == null) return;
            SetHeaderResourceText(_energyBinding, $"{Mathf.Max(0, model.energy)}/{Mathf.Max(0, model.energy_max)}");
            SetHeaderResourceText(_oreBinding, FormatCompact(model.ore));
            SetHeaderResourceText(_goldBinding, FormatCompact(model.gold));
            SetHeaderResourceText(_ingotsBinding, FormatCompact(model.ingots));
            SetHeaderResourceText(_matterBinding, FormatCompact(model.matter));
            SetHeaderResourceText(_keysBinding, FormatCompact(model.keys));
        }

        private static void BindHeaderResource(ResourceValueBinding binding, Transform headerRoot)
        {
            if (binding == null || headerRoot == null || binding.IsBound) return;
            var entryRoot = FindChildByName(headerRoot, binding.entryName);
            if (entryRoot == null) return;
            var valueRoot = FindChildByName(entryRoot, "Value") ?? entryRoot;
            binding.uiText = FindAnyTextOnOrUnder(valueRoot);
        }

        private static void SetHeaderResourceText(ResourceValueBinding binding, string value)
        {
            if (binding?.uiText != null)
                binding.uiText.text = value;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
                if (t != null && string.Equals(t.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        private static Text FindAnyTextOnOrUnder(Transform root)
        {
            if (root == null) return null;
            var self = root.GetComponent<Text>();
            return self != null ? self : root.GetComponentInChildren<Text>(true);
        }

        private static string FormatCompact(long value)
        {
            var safe = Math.Max(0L, value);
            if (safe < 1000) return safe.ToString();
            if (safe < 1000000) return (safe / 1000f).ToString("0.#") + "K";
            return (safe / 1000000f).ToString("0.#") + "M";
        }

        private BarrierRequirement GetBarrierRequirement(int floor)
        {
            BarrierRequirements.TryGetValue(Mathf.Clamp(floor, 1, 12), out var req);
            return req;
        }

        private string BuildBarrierInfoText(int floor, BarrierRequirement req)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Этаж {floor} закрыт барьером.");
            if (_progression != null)
                sb.AppendLine($"Ваш уровень: {_progression.level}");
            return sb.ToString();
        }

        private void PopulateBarrierRequirements(BarrierRequirement req)
        {
            if (_modalBarrierRequirementsRoot == null)
                return;

            ClearDynamicRows(_modalBarrierRequirementsRoot);

            if (_modalBarrierRequirementsTitle != null)
                _modalBarrierRequirementsTitle.gameObject.SetActive(true);

            var entries = new List<RewardEntry>(8);

            if (req == null)
            {
                entries.Add(new RewardEntry { icon = null, text = "Требования не найдены.", color = Color.white });
                RenderIconValueGrid(_modalBarrierRequirementsRoot, entries, 3, stackIconOverValue: false);
                _modalBarrierRequirementsRoot.gameObject.SetActive(true);
                return;
            }

            var hasAny = false;
            if (req.level > 0)
            {
                var haveLevel = Mathf.Max(0, _progression != null ? _progression.level : 0);
                var levelOk = haveLevel >= req.level;
                entries.Add(new RewardEntry { icon = null, text = $"Уровень {haveLevel}/{req.level}", color = GetEnoughColor(levelOk) });
                hasAny = true;
            }

            if (req.ore > 0)
            {
                _oreSprite ??= LoadSpriteAsset(HudOreIconAssetPath);
                var haveOre = Mathf.Max(0L, _lastResources != null ? _lastResources.ore : (_progression != null ? _progression.ore : 0));
                entries.Add(new RewardEntry { icon = _oreSprite, text = $"{haveOre}/{req.ore}", color = GetEnoughColor(haveOre >= req.ore) });
                hasAny = true;
            }

            if (req.gold > 0)
            {
                _goldSprite ??= LoadSpriteAsset(HudGoldIconAssetPath);
                var haveGold = Mathf.Max(0L, _lastResources != null ? _lastResources.gold : (_progression != null ? _progression.gold : 0));
                entries.Add(new RewardEntry { icon = _goldSprite, text = $"{haveGold}/{req.gold}", color = GetEnoughColor(haveGold >= req.gold) });
                hasAny = true;
            }

            if (req.matter > 0)
            {
                _matterSprite ??= LoadSpriteAsset(HudMatterIconAssetPath);
                var haveMatter = Mathf.Max(0L, _lastResources != null ? _lastResources.matter : (_progression != null ? _progression.matter : 0));
                entries.Add(new RewardEntry { icon = _matterSprite, text = $"{haveMatter}/{req.matter}", color = GetEnoughColor(haveMatter >= req.matter) });
                hasAny = true;
            }

            if (!string.IsNullOrWhiteSpace(req.key_id) && req.key_amount > 0)
            {
                _lockSprite ??= LoadSpriteAsset(HudKeyIconAssetPath);
                var haveKey = Mathf.Max(0, GetOwnedKeyAmount(req.key_id));
                entries.Add(new RewardEntry { icon = _lockSprite, text = $"{haveKey}/{req.key_amount}", color = GetEnoughColor(haveKey >= req.key_amount) });
                hasAny = true;
            }

            if (!hasAny)
                entries.Add(new RewardEntry { icon = null, text = "Без доп. условий", color = Color.white });

            RenderIconValueGrid(_modalBarrierRequirementsRoot, entries, 3, stackIconOverValue: false);
            _modalBarrierRequirementsRoot.gameObject.SetActive(true);
        }

        private void PopulateMonsterRewards(PveBotInfo bot)
        {
            if (_modalMonsterRewardsColumnsRoot == null)
                return;
            if (_modalMonsterRewardsTitle != null)
                _modalMonsterRewardsTitle.gameObject.SetActive(true);
            ClearDynamicRows(_modalMonsterRewardsColumnsRoot);

            var entries = new List<RewardEntry>(8);

            if (bot == null)
            {
                entries.Add(new RewardEntry { icon = null, text = "Награды: —", color = Color.white });
                _modalMonsterRewardsColumnsRoot.gameObject.SetActive(true);
                RenderIconValueGrid(_modalMonsterRewardsColumnsRoot, entries, 6, stackIconOverValue: true);
                return;
            }

            _oreSprite ??= LoadSpriteAsset(HudOreIconAssetPath);
            _goldSprite ??= LoadSpriteAsset(HudGoldIconAssetPath);
            _matterSprite ??= LoadSpriteAsset(HudMatterIconAssetPath);
            _ingotsSprite ??= LoadSpriteAsset(HudIngotsIconAssetPath);
            _lockSprite ??= LoadSpriteAsset(HudKeyIconAssetPath);

            _expSprite ??= LoadSpriteAsset(HudExpIconAssetPath);
            if (bot.reward_xp > 0)
                entries.Add(new RewardEntry { icon = _expSprite, text = "+" + FormatCompact(bot.reward_xp), color = Color.white });
            if (bot.reward_gold > 0)
                entries.Add(new RewardEntry { icon = _goldSprite, text = "+" + FormatCompact(bot.reward_gold), color = Color.white });
            if (bot.reward_ore > 0)
                entries.Add(new RewardEntry { icon = _oreSprite, text = "+" + FormatCompact(bot.reward_ore), color = Color.white });
            if (bot.reward_ingots > 0)
                entries.Add(new RewardEntry { icon = _ingotsSprite, text = "+" + FormatCompact(bot.reward_ingots), color = Color.white });
            if (!string.IsNullOrWhiteSpace(bot.reward_key_id) && bot.reward_key_amount > 0)
                entries.Add(new RewardEntry { icon = _lockSprite, text = $"{bot.reward_key_id} x{bot.reward_key_amount}", color = Color.white });

            if (bot.reward_matter_min > 0 || bot.reward_matter_max > 0)
            {
                var minMatter = Mathf.Max(0, bot.reward_matter_min);
                var maxMatter = Mathf.Max(minMatter, bot.reward_matter_max);
                var matterText = minMatter == maxMatter
                    ? "+" + FormatCompact(minMatter)
                    : $"{FormatCompact(minMatter)}-{FormatCompact(maxMatter)}";
                entries.Add(new RewardEntry { icon = _matterSprite, text = matterText, color = Color.white });
            }

            if (!string.IsNullOrWhiteSpace(bot.reward_blueprint))
                entries.Add(new RewardEntry { icon = null, text = "Рецепт: " + bot.reward_blueprint, color = Color.white });

            if (bot.reward_tesseract_chance > 0f)
            {
                var pct = Mathf.RoundToInt(bot.reward_tesseract_chance * 100f);
                entries.Add(new RewardEntry { icon = null, text = $"Тессеракт: {pct}%", color = Color.white });
            }

            if (entries.Count == 0)
                entries.Add(new RewardEntry { icon = null, text = "Награды: —", color = Color.white });

            RenderIconValueGrid(_modalMonsterRewardsColumnsRoot, entries, 6, stackIconOverValue: true);
            _modalMonsterRewardsColumnsRoot.gameObject.SetActive(true);
        }

        private void RenderIconValueGrid(RectTransform verticalParent, List<RewardEntry> entries, int columns, bool stackIconOverValue)
        {
            if (verticalParent == null || entries == null || entries.Count == 0)
                return;

            var rowPreferredHeight = stackIconOverValue ? 88f : 36f;
            var rowAlignment = stackIconOverValue ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;

            for (var i = 0; i < entries.Count; i += columns)
            {
                var rowGo = new GameObject("GridRow", typeof(RectTransform));
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.SetParent(verticalParent, false);
                var h = rowGo.AddComponent<HorizontalLayoutGroup>();
                h.spacing = stackIconOverValue ? 6f : 8f;
                h.padding = new RectOffset(0, 0, 0, 0);
                h.childAlignment = rowAlignment;
                h.childControlHeight = true;
                h.childControlWidth = true;
                h.childForceExpandHeight = false;
                h.childForceExpandWidth = true;
                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.preferredHeight = rowPreferredHeight;
                rowLe.flexibleWidth = 1f;

                for (var c = 0; c < columns && i + c < entries.Count; c++)
                {
                    var e = entries[i + c];
                    if (stackIconOverValue)
                        CreateRewardStackCell(rowRt, e.icon, e.text, e.color);
                    else
                        CreateIconValueRow(rowRt, e.icon, e.text, e.color);
                }
            }
        }

        /// <summary>Иконка фиксированного размера сверху, значение снизу — для строки наград.</summary>
        private void CreateRewardStackCell(RectTransform parent, Sprite icon, string value, Color textColor)
        {
            if (parent == null)
                return;

            const float iconSize = 36f;

            var cell = new GameObject("RewardCell", typeof(RectTransform), typeof(LayoutElement));
            var cellRt = cell.GetComponent<RectTransform>();
            cellRt.SetParent(parent, false);
            var cellLe = cell.GetComponent<LayoutElement>();
            cellLe.flexibleWidth = 1f;
            cellLe.minWidth = 40f;

            var v = cell.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.padding = new RectOffset(2, 2, 0, 0);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;

            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.SetParent(cellRt, false);
                var iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.color = Color.white;
                var iconLe = iconGo.GetComponent<LayoutElement>();
                iconLe.preferredWidth = iconSize;
                iconLe.preferredHeight = iconSize;
                iconLe.minWidth = iconSize;
                iconLe.minHeight = iconSize;
                iconLe.flexibleWidth = 0f;
                iconLe.flexibleHeight = 0f;
            }

            var labelGo = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(cellRt, false);
            var label = labelGo.GetComponent<Text>();
            label.font = GetBuiltinFont();
            label.fontSize = 15;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.text = value;
            var labelLe = labelGo.GetComponent<LayoutElement>();
            labelLe.preferredHeight = 22f;
            labelLe.flexibleWidth = 1f;
        }

        private static void ClearDynamicRows(RectTransform root)
        {
            if (root == null)
                return;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null)
                    continue;
                Destroy(child.gameObject);
            }
        }

        private void CreateIconValueRow(RectTransform parent, Sprite icon, string value, Color textColor)
        {
            if (parent == null)
                return;

            var row = new GameObject("RequirementRow", typeof(RectTransform), typeof(LayoutElement));
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.SetParent(parent, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(0, 0, 0, 0);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            var rowLe = row.GetComponent<LayoutElement>();
            rowLe.preferredHeight = 28f;
            rowLe.flexibleWidth = 1f;

            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.SetParent(rowRt, false);
                var iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.color = Color.white;
                var iconLe = iconGo.GetComponent<LayoutElement>();
                iconLe.preferredWidth = 24f;
                iconLe.preferredHeight = 24f;
                iconLe.minWidth = 24f;
                iconLe.minHeight = 24f;
                iconLe.flexibleWidth = 0f;
                iconLe.flexibleHeight = 0f;
            }

            var labelGo = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(rowRt, false);
            var label = labelGo.GetComponent<Text>();
            label.font = GetBuiltinFont();
            label.fontSize = 20;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.text = value;
            var labelLe = labelGo.GetComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
        }

        private static Color GetEnoughColor(bool isEnough)
        {
            return isEnough ? new Color(0.55f, 0.95f, 0.58f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
        }

        private int GetOwnedKeyAmount(string keyId)
        {
            if (_progression != null && _progression.key_items != null)
            {
                if (string.Equals(keyId, "miner_key", StringComparison.OrdinalIgnoreCase))
                    return Mathf.Max(0, _progression.key_items.miner_key);
                if (string.Equals(keyId, "dark_key", StringComparison.OrdinalIgnoreCase))
                    return Mathf.Max(0, _progression.key_items.dark_key);
            }

            if (_lastResources != null)
                return (int)Math.Max(0L, _lastResources.keys);
            return 0;
        }

        private static Sprite LoadSpriteAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

#if UNITY_EDITOR
            var byAssetPath = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (byAssetPath != null)
                return byAssetPath;
#endif

            var resourcesPath = assetPath.Replace("Assets/Resources/", string.Empty);
            if (resourcesPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - 4);
            return Resources.Load<Sprite>(resourcesPath);
        }

        private void ApplyAffixVisual(string affix)
        {
            var normalized = AffixCatalog.Normalize(affix);
            var hasData = TryGetAffixData(normalized, out var title, out var description, out var iconSprite);
            if (_modalAffixTitleText != null)
                _modalAffixTitleText.text = hasData && !string.IsNullOrWhiteSpace(title) ? $"Аффикс: {title}" : "Аффикс: —";
            if (_modalAffixDescriptionText != null)
                _modalAffixDescriptionText.text = hasData ? description : string.Empty;

            if (_modalAffixIcon != null)
            {
                var has = hasData && !string.IsNullOrWhiteSpace(title);
                _modalAffixIcon.sprite = iconSprite;
                _modalAffixIcon.type = iconSprite != null ? Image.Type.Simple : Image.Type.Sliced;
                _modalAffixIcon.preserveAspect = iconSprite != null;
                _modalAffixIcon.color = iconSprite != null
                    ? Color.white
                    : (has ? ColorFromString(normalized) : new Color(0.22f, 0.22f, 0.28f, 0.96f));
            }

            if (_modalAffixIconText != null)
            {
                _modalAffixIconText.gameObject.SetActive(iconSprite == null);
                _modalAffixIconText.text = hasData && !string.IsNullOrWhiteSpace(title) ? title.Substring(0, 1).ToUpperInvariant() : "?";
            }
        }

        private bool TryGetAffixData(string affix, out string title, out string description, out Sprite icon)
        {
            if (affixCatalog != null && affixCatalog.TryGet(affix, out title, out description, out icon))
                return true;
            if (AffixCatalog.TryGetBuiltin(affix, out title, out description))
            {
                icon = null;
                return true;
            }
            title = string.Empty;
            description = string.Empty;
            icon = null;
            return false;
        }

        private static Color ColorFromString(string value)
        {
            var s = AffixCatalog.Normalize(value);
            if (s.Length == 0) return new Color(0.22f, 0.22f, 0.28f, 0.96f);
            var hash = s.GetHashCode();
            var r = 0.35f + ((hash & 0xFF) / 255f) * 0.45f;
            var g = 0.35f + (((hash >> 8) & 0xFF) / 255f) * 0.45f;
            var b = 0.35f + (((hash >> 16) & 0xFF) / 255f) * 0.45f;
            return new Color(r, g, b, 0.96f);
        }

        private static Text FindTextByName(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Text>(true);
            foreach (var t in all)
                if (t != null && t.gameObject.name == name)
                    return t;
            return null;
        }

        private static Image FindImageByName(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Image>(true);
            foreach (var i in all)
                if (i != null && i.gameObject.name == name)
                    return i;
            return null;
        }

        private static Image EnsureIconImage(Transform buttonRoot)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(buttonRoot, false);
            rt.anchorMin = new Vector2(0.14f, 0.14f);
            rt.anchorMax = new Vector2(0.86f, 0.86f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        private static void SetButtonLabel(Button btn, string value)
        {
            if (btn == null) return;
            var t = btn.GetComponentInChildren<Text>(true);
            if (t != null) t.text = value;
        }

        private static void SetButtonVisible(Button btn, bool isVisible)
        {
            if (btn == null)
                return;
            if (btn.gameObject.activeSelf == isVisible)
                return;
            btn.gameObject.SetActive(isVisible);
        }

        private static string FormatSeconds(int seconds)
        {
            var s = Mathf.Max(0, seconds);
            var h = s / 3600;
            var m = (s % 3600) / 60;
            var sec = s % 60;
            if (h > 0) return $"{h:D2}:{m:D2}:{sec:D2}";
            return $"{m:D2}:{sec:D2}";
        }

        private static Text CreateText(Transform parent, string name, string value, int size, TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = GetBuiltinFont();
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = anchor;
            t.text = value;
            return t;
        }

        private static Font GetBuiltinFont()
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) return f;
            }
            catch { }
            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private sealed class MineCatalogResponse
        {
            public bool ok;
            public PveBotInfo[] bots;
            public MineFloorInfo[] mine_floors;
            public ProgressionInfo progression;
            public string mine_difficulty;
            public string err;
        }

        [Serializable]
        private sealed class ProgressionInfo
        {
            public int level;
            public int gold;
            public int ore;
            public int ingots;
            public int matter;
            public int energy;
            public int energy_max;
            public KeyItemsInfo key_items;
            public MineInfo mine;
        }

        [Serializable]
        private sealed class KeyItemsInfo
        {
            public int miner_key;
            public int dark_key;
        }

        [Serializable]
        private sealed class SummonRequest
        {
            public int floor;
            public string difficulty;
            public long session_epoch;
        }

        [Serializable]
        private sealed class AffixRerollRequest
        {
            public int floor;
            public string difficulty;
            public long session_epoch;
        }

        [Serializable]
        private sealed class AffixRerollResponse
        {
            public bool ok;
            public string err;
            public string affix;
            public int required;
            public int gold;
        }

        [Serializable]
        private sealed class SummonResponse
        {
            public bool ok;
            public string err;
            public int required;
            public int energy;
            public int gold;
        }

        [Serializable]
        private sealed class BarrierUnlockRequest
        {
            public int floor;
            public string difficulty;
            public long session_epoch;
        }

        [Serializable]
        private sealed class BarrierUnlockResponse
        {
            public bool ok;
            public string err;
            public int required;
            public int required_level;
            public int ore;
            public int gold;
            public int matter;
            public int have;
            public string key_id;
        }

        [Serializable]
        private sealed class MineInfo
        {
            public string current_difficulty;
        }

        [Serializable]
        private sealed class MineFloorInfo
        {
            public int floor;
            public string bot_id;
            public bool unlocked;
            public int respawn_left_seconds;
            public string affix;
            public bool is_boss;
        }

        [Serializable]
        private sealed class PveBotInfo
        {
            public string id;
            public string name;
            public int floor;
            public int hp_bonus;
            public int start_mana;
            public int reward_xp;
            public int reward_gold;
            public int reward_ore;
            public int reward_ingots;
            public string reward_key_id;
            public int reward_key_amount;
            public string reward_blueprint;
            public int reward_matter_min;
            public int reward_matter_max;
            public float reward_tesseract_chance;
            public int base_damage;
            public int base_armor;
            public float base_crit;
        }

        private sealed class FloorRowRefs
        {
            public Transform root;
            public Text stateText;
            public Button monsterButton;
            public Button lockButton;
        }

        [Serializable]
        private sealed class BarrierRequirement
        {
            public int level;
            public int ore;
            public int gold;
            public int matter;
            public string key_id;
            public int key_amount;
        }

        private sealed class ResourceValueBinding
        {
            public readonly string entryName;
            public Text uiText;

            public ResourceValueBinding(string entryName)
            {
                this.entryName = entryName;
            }

            public bool IsBound => uiText != null;
        }

        private sealed class RewardEntry
        {
            public Sprite icon;
            public string text;
            public Color color;
        }
    }
}
