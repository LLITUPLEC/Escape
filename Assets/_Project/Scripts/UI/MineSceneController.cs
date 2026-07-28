using System;
using System.Collections;
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
        [Tooltip("Иконки ingot_*/свитков в превью наград (как в мастерской). Пусто — в редакторе грузится MainItemCatalog.asset.")]
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private Sprite keyIconSprite;
        [SerializeField] private Sprite oreIconSprite;
        [SerializeField] private Sprite goldIconSprite;
        [SerializeField] private Sprite matterIconSprite;
        [SerializeField] private Sprite ingotsIconSprite;
        [SerializeField] private Sprite expIconSprite;
        [Tooltip("Иконка энергии (стоимость боя/прогона). Если пусто — energy.png из resources_hud.")]
        [SerializeField] private Sprite energyIconSprite;
        [Tooltip("Награды модалки: рецепт (blueprint), общий fallback.")]
        [SerializeField] private Sprite blueprintIconSprite;
        [Tooltip("Иконка рецепта зелёного качества (награда reward_blueprint = green).")]
        [SerializeField] private Sprite recipeGreenSprite;
        [Tooltip("Иконка рецепта синего качества (reward_blueprint = blue).")]
        [SerializeField] private Sprite recipeBlueSprite;
        [Tooltip("Иконка рецепта фиолетового качества (reward_blueprint = purple).")]
        [SerializeField] private Sprite recipePurpleSprite;
        [Tooltip("Иконка золотого рецепта (reward_blueprint = gold).")]
        [SerializeField] private Sprite recipeGoldSprite;
        [Header("Иконки слотов экипировки (8 слотов, порядок как EquipmentSlotId)")]
        [SerializeField] private Sprite slotHelmetSprite;
        [SerializeField] private Sprite slotShouldersSprite;
        [SerializeField] private Sprite slotChestSprite;
        [SerializeField] private Sprite slotGlovesSprite;
        [SerializeField] private Sprite slotLegsSprite;
        [SerializeField] private Sprite slotFeetSprite;
        [SerializeField] private Sprite slotWeaponLeftSprite;
        [SerializeField] private Sprite slotWeaponRightSprite;
        [Tooltip("Награды модалки: шанс тессеракта. Если пусто — matter или tesseract.png.")]
        [SerializeField] private Sprite tesseractIconSprite;
        [Tooltip("Необязательно: если пусто, загружается Resources/UI/MonsterModal.")]
        [SerializeField] private GameObject monsterModalPrefab;
        [Tooltip("Иконка свистка у таймера респауна на этаже. Если пусто — символ в тексте.")]
        [SerializeField] private Sprite summonWhistleIconSprite;
        private const string RpcPveCatalogGet = "duel_match3_pve_catalog_get";
        private const string RpcMineSummon = "duel_mine_summon";
        private const string RpcMineAffixReroll = "duel_mine_affix_reroll";
        private const string RpcMineBarrierUnlock = "duel_mine_barrier_unlock";
        private const string RpcMineSetDifficulty = "duel_mine_set_difficulty";
        private const string MineCatalogCacheKey = "nakama.cache.mine_catalog_pve_v1";
        private const string DifficultyTabsRootName = "DifficultyTabs";
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
        private const string HudEnergyIconAssetPath = "Assets/_Project/img/resources_hud/energy.png";
        private const string HudBlueprintIconAssetPath = "Assets/_Project/img/resources_hud/blueprint.png";
        private const string HudTesseractIconAssetPath = "Assets/_Project/img/resources_hud/tesseract.png";
        private const string HudWhistleIconAssetPath = "Assets/_Project/img/resources_hud/whistle.png";
        private const float BarrierDismissButtonMaxWidth = 450f;
        /// <summary>Пропорции кнопок футера модалки (как у спрайта 230×65).</summary>
        private const float ModalFooterButtonAspect = 230f / 65f;
        private const float InsufficientResourcesToastSeconds = 1.6f;
        private const string MineFooterRowName = "MineFooterRow";
        private static readonly Color BarrierDismissLabelColor = new Color(224f / 255f, 204f / 255f, 137f / 255f, 1f);
        /// <summary>Цвет фона кнопки монстра (#29781B).</summary>
        private static readonly Color MonsterButtonFrameColor = new Color(0x29 / 255f, 0x78 / 255f, 0x1B / 255f, 1f);
        private const float MonsterButtonInset = 40f;

        private readonly Dictionary<int, FloorRowRefs> _rows = new();
        private readonly Dictionary<int, Button> _liftButtons = new();
        private readonly Dictionary<int, PveBotInfo> _botByFloor = new();
        private readonly Dictionary<int, MineFloorInfo> _mineByFloor = new();
        private string _difficulty = "easy";
        private int _unlockedEasy = 1;
        private int _unlockedMedium;
        private int _unlockedHard;
        private bool _setDifficultyInFlight;
        private Transform _difficultyTabsRoot;
        private readonly Dictionary<string, Button> _difficultyButtons = new();
        private readonly Dictionary<string, UiNeonPulseOutline> _difficultyNeon = new();
        private readonly Dictionary<string, CanvasGroup> _difficultyCanvasGroups = new();
        private readonly Dictionary<string, Text> _difficultyLabels = new();
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
        private Font _modalMonsterRewardsValueFont;
        private RectTransform _modalBarrierRequirementsRoot;
        private Text _modalBarrierRequirementsTitle;
        private HorizontalLayoutGroup _modalFooterHorizontalLayout;
        private LayoutElement _modalDismissLayoutElement;
        private Text _modalDismissLabel;
        private bool _modalFooterLayoutCached;
        private TextAnchor _modalFooterDefaultChildAlignment;
        private bool _modalFooterDefaultChildForceExpandWidth;
        private float _modalDismissDefaultPreferredWidth;
        private float _modalDismissDefaultPreferredHeight;
        private float _modalDismissDefaultFlexibleWidth;
        private float _modalDismissDefaultFlexibleHeight;
        private Color _modalDismissDefaultLabelColor;
        private GameObject _summonConfirmRoot;
        private Text _summonConfirmMessageText;
        private int _summonConfirmFloor;
        private GameObject _insufficientResourcesToastRoot;
        private Text _insufficientResourcesToastText;
        private Coroutine _insufficientResourcesToastRoutine;
        private int _monsterModalPresentGeneration;
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
        /// <summary>Сервер: duel_character_get learned_recipe_ids — для скрытия строки рецепта в модалке монстра.</summary>
        private string[] _cachedLearnedRecipeIds = Array.Empty<string>();
        private EnergyHeaderPurchaseController _energyHeaderPurchase;
        private Transform _headerResourcesRoot;
        private Sprite _lockSprite;
        private Sprite _oreSprite;
        private Sprite _goldSprite;
        private Sprite _matterSprite;
        private Sprite _ingotsSprite;
        private Sprite _expSprite;
        private Sprite _blueprintSprite;
        private Sprite _tesseractSprite;
        private Sprite _energySprite;
        private readonly ResourceValueBinding _energyBinding = new("Energy");
        private readonly ResourceValueBinding _oreBinding = new("ore");
        private readonly ResourceValueBinding _goldBinding = new("Gold");
        private readonly ResourceValueBinding _ingotsBinding = new("ingots");
        private readonly ResourceValueBinding _matterBinding = new("matter");
        private readonly ResourceValueBinding _keysBinding = new("keys");
        /// <summary>Синхрон с MINE_BARRIER_REQUIREMENTS в duel_match3.lua (solo.md × ~1,4).</summary>
        private static readonly Dictionary<int, BarrierRequirement> BarrierRequirements = new()
        {
            [2] = new BarrierRequirement { ore = 140 },
            [3] = new BarrierRequirement { ore = 490 },
            [4] = new BarrierRequirement { ore = 1120 },
            [5] = new BarrierRequirement { ore = 2100, gold = 2800 },
            [6] = new BarrierRequirement { ore = 3500 },
            [7] = new BarrierRequirement { ore = 5320, gold = 6500 },
            [8] = new BarrierRequirement { ore = 7700 },
            [9] = new BarrierRequirement { ore = 10500, gold = 14000 },
            [10] = new BarrierRequirement { ore = 14000, gold = 17000 },
            [11] = new BarrierRequirement { ore = 18200, gold = 22000 },
            [12] = new BarrierRequirement { ore = 23800, matter = 300, gold = 35000 },
        };

        /// <summary>Не-боссы: этажи 1,2,3,5,6,7,9,11 (10 без дропа — пересечение с босс-этажами). См. duel_match3.lua MINE_RECIPE_DROP_FLOORS.</summary>
        private static readonly int[] MineRecipeDropFloors = { 1, 2, 3, 5, 6, 7, 9, 11 };

        private static int MineRecipeDropChancePercent(int floor)
        {
            for (var i = 0; i < MineRecipeDropFloors.Length; i++)
            {
                if (MineRecipeDropFloors[i] == floor)
                    return 50 - i * 5;
            }

            return 0;
        }

        /// <summary>В id свитка всегда t1 (экип только T1). Сложность кодируется цветом green/blue/purple.</summary>
        private static int MineTierFromDifficulty(string difficulty) => 1;

        /// <summary>Ожидаемый def_id свитка с монстра этого этажа (сервер: mine_recipe_item_id_for_floor_index). Иначе null.</summary>
        private static string MineExpectedRecipeItemId(int floor, string difficulty)
        {
            var idx = -1;
            for (var i = 0; i < MineRecipeDropFloors.Length; i++)
            {
                if (MineRecipeDropFloors[i] == floor)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
                return null;
            var slots = new[]
            {
                "Helmet", "Chest", "Gloves", "WeaponLeft", "WeaponRight", "Legs", "Shoulders", "Feet"
            };
            if (idx >= slots.Length)
                return null;
            string color;
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase))
                color = "blue";
            else if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
                color = "purple";
            else
                color = "green";
            var tier = MineTierFromDifficulty(difficulty);
            return "recipe_drop_t" + tier + "_" + color + "_" + slots[idx];
        }

        /// <summary>Точное совпадение def_id свитка: recipe_drop_t{тир}_{цвет}_{Slot}.</summary>
        private static bool MineRecipeDropLearned(string expectedRecipeItemId, string[] learned)
        {
            if (string.IsNullOrEmpty(expectedRecipeItemId) || learned == null || learned.Length == 0)
                return false;
            for (var i = 0; i < learned.Length; i++)
            {
                var x = learned[i];
                if (string.IsNullOrEmpty(x))
                    continue;
                if (string.Equals(x, expectedRecipeItemId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>Доля выпадения слитка с обычного монстра (§4.4 / ingot_drop_chance_non_boss в duel_match3.lua).</summary>
        private static float IngotDropChanceNonBoss(int floor)
        {
            var f = Mathf.Clamp(floor, 1, 12);
            var r = f % 4;
            if (r == 1) return 0.25f;
            if (r == 2) return 0.5f;
            if (r == 3) return 0.75f;
            return 1f;
        }

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
            EnsureSummonConfirmDialog();
            EnsureDifficultyTabs();
            EnsureHeaderResources();
            TryApplyCachedMineCatalogSnapshot();
        }

        private void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _ = RefreshAsync(_cts.Token);
            _ = RefreshResourcesAsync(_cts.Token);
            _ = RefreshLearnedRecipesAsync(_cts.Token);
            TryInstallEnergyHeaderPurchase();
        }

        private void OnDisable()
        {
            _energyHeaderPurchase = null;
            if (_insufficientResourcesToastRoutine != null)
            {
                StopCoroutine(_insufficientResourcesToastRoutine);
                _insufficientResourcesToastRoutine = null;
            }
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _refreshInFlight = false;
            _summonInFlight = false;
            _rerollInFlight = false;
            _unlockInFlight = false;
            _setDifficultyInFlight = false;
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
                    RefreshOpenMonsterModalRespawnOnly();
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
                _ = RefreshLearnedRecipesAsync(_cts.Token);
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

                ApplyMineCatalogPayload(payload);
                PlayerPrefs.SetString(MineCatalogCacheKey, payload);
                PlayerPrefs.Save();
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

        private void TryApplyCachedMineCatalogSnapshot()
        {
            try
            {
                var json = PlayerPrefs.GetString(MineCatalogCacheKey, "");
                if (string.IsNullOrWhiteSpace(json))
                    return;
                ApplyMineCatalogPayload(json);
            }
            catch
            {
                // ignored
            }
        }

        private void ApplyMineCatalogPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;
            var model = JsonUtility.FromJson<MineCatalogResponse>(payload);
            if (model == null || !model.ok)
                return;
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
            ApplyUnlockedFromCatalog(model);

            ApplyRows();
            ApplyDifficultyTabVisuals();
            ApplyResourcesFallbackFromProgression();
        }

        private void ApplyUnlockedFromCatalog(MineCatalogResponse model)
        {
            if (model == null)
                return;
            if (model.mine_unlocked != null)
            {
                _unlockedEasy = Mathf.Max(1, model.mine_unlocked.easy);
                _unlockedMedium = Mathf.Max(0, model.mine_unlocked.medium);
                _unlockedHard = Mathf.Max(0, model.mine_unlocked.hard);
                return;
            }

            ApplyUnlockedFromProgression(model.progression);
        }

        private void ApplyUnlockedFromProgression(ProgressionInfo progression)
        {
            _unlockedEasy = 1;
            _unlockedMedium = 0;
            _unlockedHard = 0;
            var unlocked = progression != null && progression.mine != null ? progression.mine.unlocked : null;
            if (unlocked == null)
                return;
            _unlockedEasy = Mathf.Max(1, unlocked.easy);
            _unlockedMedium = Mathf.Max(0, unlocked.medium);
            _unlockedHard = Mathf.Max(0, unlocked.hard);
        }

        private bool IsDifficultyUnlocked(string difficulty)
        {
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase))
                return _unlockedMedium >= 1;
            if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
                return _unlockedHard >= 1;
            return true;
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
                refs.monsterButton = EnsureMonsterButton(refs.root);
                ApplyMonsterButtonChrome(refs.monsterButton);
                refs.monsterButton.onClick.RemoveAllListeners();
                refs.lockButton = EnsureLockButton(refs.root, floor);
                refs.lockButton.onClick.RemoveAllListeners();
                var lockImage = refs.lockButton.GetComponent<Image>();
                var lockLabel = refs.lockButton.GetComponentInChildren<Text>(true);
                ApplyLockButtonSprite(lockImage, lockLabel);
                var f = floor;
                refs.monsterButton.onClick.AddListener(() => OpenMonsterModal(f));
                refs.lockButton.onClick.AddListener(() => OpenMonsterModal(f));
                EnsureCooldownClusterForFloor(refs, f);
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

        private Button EnsureMonsterButton(Transform rowRoot)
        {
            var existing = rowRoot.Find("MonsterButton");
            if (existing != null)
                return existing.GetComponent<Button>() ?? existing.gameObject.AddComponent<Button>();

            var go = new GameObject("MonsterButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(rowRoot, false);
            ApplyMonsterButtonLayout(rt);
            go.GetComponent<Image>().color = MonsterButtonFrameColor;
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
            txt.text = string.Empty;
            txt.raycastTarget = false;

            return btn;
        }

        private static void ApplyMonsterButtonLayout(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(MonsterButtonInset, MonsterButtonInset);
            rt.offsetMax = new Vector2(-MonsterButtonInset, -MonsterButtonInset);
        }

        private static void ApplyMonsterButtonChrome(Button btn)
        {
            if (btn == null)
                return;
            ApplyMonsterButtonLayout(btn.GetComponent<RectTransform>());
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = MonsterButtonFrameColor;
            var label = FindTextByName(btn.transform, "Label");
            if (label != null)
                label.text = string.Empty;
            var icon = FindImageByName(btn.transform, "Icon");
            if (icon != null)
                ApplyMonsterIconLayout(icon.GetComponent<RectTransform>());
        }

        private void EnsureCooldownClusterForFloor(FloorRowRefs refs, int floor)
        {
            if (refs?.root == null || refs.monsterButton == null || refs.cooldownClusterRt != null)
                return;

            var clusterGo = new GameObject("CooldownCluster", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var clusterRt = clusterGo.GetComponent<RectTransform>();
            clusterRt.SetParent(refs.root, false);
            var h = clusterGo.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = true;

            var textHost = new GameObject("TimerTextHost", typeof(RectTransform), typeof(LayoutElement));
            var textHostRt = textHost.GetComponent<RectTransform>();
            textHostRt.SetParent(clusterRt, false);
            var textHostLe = textHost.GetComponent<LayoutElement>();
            textHostLe.flexibleWidth = 1f;
            textHostLe.flexibleHeight = 1f;
            textHostLe.minWidth = 40f;
            textHostLe.minHeight = 44f;

            var whistleGo = new GameObject("SummonWhistle", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            whistleGo.GetComponent<RectTransform>().SetParent(clusterRt, false);
            var whistleImg = whistleGo.GetComponent<Image>();
            whistleImg.sprite = summonWhistleIconSprite ?? LoadSpriteAsset(HudWhistleIconAssetPath);
            whistleImg.preserveAspect = true;
            whistleImg.color = Color.white;
            if (whistleImg.sprite == null)
            {
                var glyph = CreateText(whistleGo.transform, "Glyph", "\u2606", 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
                glyph.color = new Color(1f, 0.92f, 0.35f, 1f);
                glyph.raycastTarget = false;
            }
            var whistleLe = whistleGo.GetComponent<LayoutElement>();
            whistleLe.preferredWidth = 44f;
            whistleLe.preferredHeight = 44f;
            whistleLe.minWidth = 44f;
            whistleLe.minHeight = 44f;
            var whistleBtn = whistleGo.GetComponent<Button>();
            var f = floor;
            whistleBtn.onClick.AddListener(() => OnSummonWhistleClicked(f));

            clusterRt.SetSiblingIndex(refs.monsterButton.transform.GetSiblingIndex());

            refs.cooldownClusterRt = clusterRt;
            refs.cooldownTextHostRt = textHostRt;
            refs.summonWhistleButton = whistleBtn;
            clusterGo.SetActive(false);
        }

        private static void CopyRectTransform(RectTransform from, RectTransform to)
        {
            if (from == null || to == null)
                return;
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.offsetMin = from.offsetMin;
            to.offsetMax = from.offsetMax;
            to.localScale = from.localScale;
        }

        private void ApplyCooldownTimerLayout(FloorRowRefs refs, int floor)
        {
            if (refs.cooldownClusterRt == null || refs.monsterButton == null)
                return;
            var monsterRt = refs.monsterButton.GetComponent<RectTransform>();
            CopyRectTransform(monsterRt, refs.cooldownClusterRt);
            refs.cooldownClusterRt.gameObject.SetActive(true);

            var clusterH = refs.cooldownClusterRt.GetComponent<HorizontalLayoutGroup>();
            if (clusterH != null)
            {
                clusterH.childForceExpandWidth = true;
                clusterH.childForceExpandHeight = true;
            }

            if (refs.cooldownTextHostRt != null)
            {
                var textHostLe = refs.cooldownTextHostRt.GetComponent<LayoutElement>();
                if (textHostLe != null)
                {
                    textHostLe.flexibleHeight = 1f;
                    textHostLe.minHeight = Mathf.Max(textHostLe.minHeight, 44f);
                }
            }

            if (refs.stateText != null)
            {
                if (!refs.stateTextPlacementSaved)
                {
                    refs.stateTextOriginalParent = refs.stateText.transform.parent;
                    refs.stateTextOriginalSiblingIndex = refs.stateText.transform.GetSiblingIndex();
                    refs.stateTextPlacementSaved = true;
                }
                refs.stateText.transform.SetParent(refs.cooldownTextHostRt, false);
                var strt = refs.stateText.GetComponent<RectTransform>();
                strt.anchorMin = Vector2.zero;
                strt.anchorMax = Vector2.one;
                strt.offsetMin = Vector2.zero;
                strt.offsetMax = Vector2.zero;
                refs.stateText.gameObject.SetActive(true);
            }

            var canSummon = CanSummonOnFloor(floor);
            if (refs.summonWhistleButton != null)
            {
                refs.summonWhistleButton.gameObject.SetActive(canSummon);
                refs.summonWhistleButton.interactable = canSummon && !_summonInFlight;
            }
        }

        private static void HideCooldownCluster(FloorRowRefs refs)
        {
            if (refs?.cooldownClusterRt == null)
                return;
            refs.cooldownClusterRt.gameObject.SetActive(false);
        }

        private static void RestoreStateTextToOriginalSlot(FloorRowRefs refs)
        {
            if (refs?.stateText == null || !refs.stateTextPlacementSaved || refs.stateTextOriginalParent == null)
                return;
            if (refs.stateText.transform.parent == refs.stateTextOriginalParent)
                return;
            refs.stateText.transform.SetParent(refs.stateTextOriginalParent, false);
            refs.stateText.transform.SetSiblingIndex(refs.stateTextOriginalSiblingIndex);
        }

        private bool CanSummonOnFloor(int floor)
        {
            if (IsMineBossFloor(floor))
                return false;
            _botByFloor.TryGetValue(floor, out var bot);
            return bot != null;
        }

        private void OnSummonWhistleClicked(int floor)
        {
            if (!CanSummonOnFloor(floor) || _summonInFlight)
                return;
            ShowSummonConfirmDialog(floor);
        }

        private void ShowSummonConfirmDialog(int floor)
        {
            EnsureSummonConfirmDialog();
            if (_summonConfirmRoot == null || _summonConfirmMessageText == null)
                return;
            _summonConfirmFloor = floor;
            _summonConfirmMessageText.text = $"Потратить {SummonEnergyCost} эн / {SummonGoldCost} зол?";
            _summonConfirmRoot.transform.SetAsLastSibling();
            _summonConfirmRoot.SetActive(true);
        }

        private void EnsureSummonConfirmDialog()
        {
            if (_summonConfirmRoot != null)
                return;
            var parent = FindMonsterModalParent();
            if (parent == null)
                return;

            var root = new GameObject("SummonConfirmDialog", typeof(RectTransform), typeof(Image));
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            root.GetComponent<Image>().raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.SetParent(rootRt, false);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(440f, 220f);
            panel.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 1f);

            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(22, 22, 18, 18);
            v.spacing = 18f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;

            _summonConfirmMessageText = CreateText(panel.transform, "Message", string.Empty, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            var msgLe = _summonConfirmMessageText.gameObject.AddComponent<LayoutElement>();
            msgLe.minHeight = 56f;
            msgLe.preferredHeight = 64f;
            msgLe.flexibleWidth = 1f;

            var row = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.GetComponent<RectTransform>().SetParent(panel.transform, false);
            var rowH = row.GetComponent<HorizontalLayoutGroup>();
            rowH.spacing = 24f;
            rowH.childAlignment = TextAnchor.MiddleCenter;
            rowH.childControlHeight = true;
            rowH.childControlWidth = true;
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 48f;

            MakeSummonConfirmButton(row.transform, "Да", OnSummonConfirmYesClicked);
            MakeSummonConfirmButton(row.transform, "Нет", OnSummonConfirmNoClicked);

            root.SetActive(false);
            _summonConfirmRoot = root;
        }

        private void MakeSummonConfirmButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.48f, 0.82f, 1f);
            var btn = go.GetComponent<Button>();
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 130f;
            le.preferredHeight = 42f;
            le.minHeight = 42f;
            var txt = CreateText(go.transform, "Text", label, 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            txt.color = Color.white;
            txt.raycastTarget = false;
            btn.onClick.AddListener(onClick);
        }

        private async void OnSummonConfirmYesClicked()
        {
            if (_summonConfirmRoot != null)
                _summonConfirmRoot.SetActive(false);
            var floor = _summonConfirmFloor;
            _selectedFloor = floor;
            await SummonSelectedFloorAsync();
        }

        private void OnSummonConfirmNoClicked()
        {
            if (_summonConfirmRoot != null)
                _summonConfirmRoot.SetActive(false);
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
                    RestoreStateTextToOriginalSlot(refs);
                    HideCooldownCluster(refs);
                    if (refs.stateText != null)
                    {
                        refs.stateText.text = "Барьер";
                        refs.stateText.gameObject.SetActive(true);
                    }
                    SetButtonVisible(refs.monsterButton, false);
                    SetButtonVisible(refs.lockButton, true);
                    refs.lockButton.interactable = true;
                    continue;
                }

                SetButtonVisible(refs.lockButton, false);

                if (respawn > 0)
                {
                    if (refs.stateText != null)
                        refs.stateText.text = "До появления: " + FormatSeconds(respawn);
                    SetButtonLabel(refs.monsterButton, string.Empty);
                    SetButtonVisible(refs.monsterButton, false);
                    ApplyCooldownTimerLayout(refs, floor);
                }
                else
                {
                    RestoreStateTextToOriginalSlot(refs);
                    HideCooldownCluster(refs);
                    if (refs.stateText != null)
                        refs.stateText.gameObject.SetActive(false);
                    SetButtonLabel(refs.monsterButton, string.Empty);
                    SetButtonVisible(refs.monsterButton, true);
                    refs.monsterButton.interactable = true;
                }

                _botByFloor.TryGetValue(floor, out var bot);
                ApplyMonsterVisual(floor, refs, bot, mf);
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

        private void ApplyMonsterVisual(int floor, FloorRowRefs refs, PveBotInfo bot, MineFloorInfo floorInfo)
        {
            if (refs == null || refs.monsterButton == null) return;

            var frame = refs.monsterButton.GetComponent<Image>();
            var icon = FindImageByName(refs.monsterButton.transform, "Icon");
            if (icon == null) icon = EnsureIconImage(refs.monsterButton.transform);
            else
                ApplyMonsterIconLayout(icon.GetComponent<RectTransform>());

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
            _modalMonsterRewardsValueFont = view.MonsterRewardsValueFont;
            _modalBarrierRequirementsTitle = view.BarrierRequirementsSectionTitle;
            _modalBarrierRequirementsRoot = view.BarrierRequirementsRoot;

            _modalCloseButton.onClick.AddListener(CloseMonsterModal);
            _modalFightButton.onClick.AddListener(OnFightClicked);
            _modalDismissButton.onClick.AddListener(HandleSecondaryButtonClicked);
            EnsureFooterRowWithCostStrip(_modalFightButton, "В бой");
            EnsureFooterRowWithCostStrip(_modalDismissButton, "Прогнать");
            CacheModalFooterDefaultsIfNeeded();
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

        private void CloseMonsterModal()
        {
            _monsterModalPresentGeneration++;
            if (_modalRoot != null)
                _modalRoot.SetActive(false);
        }

        /// <summary>Обновить только строку таймера респавна и кнопки — без ClearDynamicRows (секундный тик респавна).</summary>
        private void RefreshOpenMonsterModalRespawnOnly()
        {
            if (_modalRoot == null || !_modalRoot.activeSelf || _monsterContentRoot == null || !_monsterContentRoot.activeSelf)
                return;

            _mineByFloor.TryGetValue(_selectedFloor, out var mine);
            if (mine == null || !mine.unlocked)
                return;

            var respawn = Mathf.Max(0, mine.respawn_left_seconds);
            if (_modalStatTexts != null && _modalStatTexts.Length >= 6)
                _modalStatTexts[5].text = respawn > 0 ? $"Появится: {FormatSeconds(respawn)}" : "—";

            _botByFloor.TryGetValue(_selectedFloor, out var bot);
            if (_modalFightButton != null)
                _modalFightButton.interactable = respawn <= 0 && bot != null;
            _modalCanSummon = respawn > 0 && bot != null && !IsMineBossFloor(_selectedFloor);
            ApplyDismissButtonCosts(_modalCanSummon, bot);
        }

        private async void OpenMonsterModal(int floor)
        {
            if (_modalRoot == null) return;
            var presentGeneration = ++_monsterModalPresentGeneration;
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
                PopulateBarrierRequirements(floor, req);
                _modalCanUnlock = req != null;
                _modalCanSummon = false;
                ApplyModalFooterBarrierLayout(barrierMode: true);
                SetButtonLabel(_modalDismissButton, req != null ? "Разбить" : "Закрыть");
                PopulateCostStrip(_modalDismissButton, null);
                PopulateCostStrip(_modalFightButton, null);
                _modalDismissButton.interactable = true;
                _modalRoot.SetActive(true);
                return;
            }

            ApplyModalFooterBarrierLayout(barrierMode: false);

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

            try
            {
                if (_cts != null)
                    await RefreshLearnedRecipesAsync(_cts.Token);
            }
            catch (Exception)
            {
                // оставляем кэш
            }

            // После await объект мог быть выключен (OnDisable → Destroy UI); RefreshLearnedRecipesAsync не пробрасывает отмену.
            // Если окно уже закрыли или успели открыть другой этаж — не трогаем UI (иначе «закрыл — снова вылезло»).
            if (this == null || _modalRoot == null || presentGeneration != _monsterModalPresentGeneration)
                return;

            PopulateMonsterRewards(bot);

            _modalCanUnlock = false;
            _modalCanSummon = respawn > 0 && bot != null && !IsMineBossFloor(floor);
            _modalFightButton.interactable = respawn <= 0 && bot != null;
            ApplyFightButtonCosts(bot);
            ApplyDismissButtonCosts(_modalCanSummon, bot);
            _modalDismissButton.interactable = true;
            if (presentGeneration != _monsterModalPresentGeneration)
                return;
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
                        var need = DescribeFirstMissingCost(CoalesceCost(bot.cost_attack, DefaultCostAttack()), resources);
                        if (need != null)
                        {
                            if (_modalSupplementalInfo != null)
                                _modalSupplementalInfo.text += "\n\n" + need;
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
                CloseMonsterModal();
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
                    if (IsSummonInsufficientResourcesError(model.err))
                        ShowInsufficientResourcesToast();
                    else if (_modalSupplementalInfo != null)
                        _modalSupplementalInfo.text += "\n\n" + DescribeSummonError(model);
                    return;
                }

                if (_mineByFloor.TryGetValue(_selectedFloor, out var mine) && mine != null)
                {
                    mine.respawn_left_seconds = 0;
                }

                ApplyRows();
                CloseMonsterModal();
                try
                {
                    await RefreshResourcesAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
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
                    if (IsSummonInsufficientResourcesError(model.err))
                        ShowInsufficientResourcesToast();
                    else if (_modalSupplementalInfo != null)
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

        private static bool IsSummonInsufficientResourcesError(string err)
        {
            if (string.IsNullOrEmpty(err))
                return false;
            return string.Equals(err, "not_enough_energy", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(err, "not_enough_gold", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(err, "not_enough_ore", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(err, "not_enough_matter", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(err, "not_enough_ingots", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(err, "not_enough_key_item", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureInsufficientResourcesToast()
        {
            if (_insufficientResourcesToastRoot != null)
                return;
            var parent = FindMonsterModalParent();
            if (parent == null)
                return;

            var root = new GameObject("InsufficientResourcesToast", typeof(RectTransform), typeof(Image));
            var rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(720f, 72f);
            var img = root.GetComponent<Image>();
            img.color = new Color(0.1f, 0.11f, 0.15f, 0.96f);
            img.raycastTarget = false;

            _insufficientResourcesToastText = CreateText(root.transform, "Msg", "Недостаточно ресурсов", 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _insufficientResourcesToastText.color = new Color(1f, 0.82f, 0.82f, 1f);

            root.SetActive(false);
            _insufficientResourcesToastRoot = root;
        }

        private void ShowInsufficientResourcesToast()
        {
            ShowMineToast("Недостаточно ресурсов");
        }

        private void ShowMineToast(string message)
        {
            EnsureInsufficientResourcesToast();
            if (_insufficientResourcesToastRoot == null)
                return;
            if (_insufficientResourcesToastText != null)
                _insufficientResourcesToastText.text = string.IsNullOrWhiteSpace(message) ? "—" : message;
            _insufficientResourcesToastRoot.transform.SetAsLastSibling();
            _insufficientResourcesToastRoot.SetActive(true);
            if (_insufficientResourcesToastRoutine != null)
                StopCoroutine(_insufficientResourcesToastRoutine);
            _insufficientResourcesToastRoutine = StartCoroutine(HideInsufficientResourcesToastAfterDelay());
        }

        private IEnumerator HideInsufficientResourcesToastAfterDelay()
        {
            yield return new WaitForSecondsRealtime(InsufficientResourcesToastSeconds);
            if (_insufficientResourcesToastRoot != null)
                _insufficientResourcesToastRoot.SetActive(false);
            _insufficientResourcesToastRoutine = null;
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
                case "not_enough_energy":
                    return $"Не хватает энергии: нужно {response.required}, доступно {response.energy}.";
                case "not_enough_ore":
                    return $"Не хватает руды: нужно {response.required}, доступно {response.ore}.";
                case "not_enough_matter":
                    return $"Не хватает материи: нужно {response.required}, доступно {response.matter}.";
                case "not_enough_ingots":
                    return $"Не хватает слитков: нужно {response.required}, доступно {response.ingots}.";
                case "not_enough_key_item":
                    return $"Не хватает ключа {response.key_id}: нужно {response.required}, есть {response.have}.";
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
                case "prev_monster_not_defeated":
                    return $"Сначала победите монстра на этаже {Mathf.Max(1, response.required_prev_floor)}.";
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
                if (_lastResources == null && PlayerResourcesService.TryReadCached(out var cachedHdr))
                {
                    _lastResources = cachedHdr;
                    ApplyHeaderResourceValues(cachedHdr);
                }

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

        private async Task RefreshLearnedRecipesAsync(CancellationToken ct)
        {
            try
            {
                var r = await CharacterProfileService.GetAsync(ct);
                if (r != null && r.ok && r.learned_recipe_ids != null)
                    _cachedLearnedRecipeIds = r.learned_recipe_ids;
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] Learned recipes refresh failed: " + e.Message);
            }
        }

        private void ApplyResourcesFallbackFromProgression()
        {
            if (_lastResources != null || _progression == null)
                return;

            var mk = _progression.key_items != null ? Mathf.Max(0, _progression.key_items.miner_key) : 0;
            var dk = _progression.key_items != null ? Mathf.Max(0, _progression.key_items.dark_key) : 0;
            _lastResources = new PlayerResourcesRpcResponse
            {
                ok = true,
                energy = _progression.energy,
                energy_max = _progression.energy_max,
                ore = _progression.ore,
                gold = _progression.gold,
                ingots = _progression.ingots,
                matter = _progression.matter,
                miner_key = mk,
                dark_key = dk,
                keys = mk + dk,
            };
            ApplyHeaderResourceValues(_lastResources);
        }

        private void TryInstallEnergyHeaderPurchase()
        {
            if (_energyHeaderPurchase != null || _headerResourcesRoot == null) return;
            if (!_energyBinding.IsBound) return;
            EnsureHudIconReferences();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _energyHeaderPurchase = new EnergyHeaderPurchaseController(
                canvas.transform,
                _energySprite,
                _matterSprite,
                _goldSprite,
                async ct => { await RefreshResourcesAsync(ct).ConfigureAwait(true); },
                _cts != null ? _cts.Token : CancellationToken.None);
            _energyHeaderPurchase.EnsurePlusOnEnergyRow(_headerResourcesRoot);
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
            if (itemCatalog == null)
                itemCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemCatalog>(MineRewardFormat.MainItemCatalogAssetPath);
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
            _blueprintSprite = blueprintIconSprite;
            _tesseractSprite = tesseractIconSprite;

            if (_lockSprite == null) _lockSprite = LoadSpriteAsset(HudKeyIconAssetPath);
            if (_oreSprite == null) _oreSprite = LoadSpriteAsset(HudOreIconAssetPath);
            if (_goldSprite == null) _goldSprite = LoadSpriteAsset(HudGoldIconAssetPath);
            if (_matterSprite == null) _matterSprite = LoadSpriteAsset(HudMatterIconAssetPath);
            if (_ingotsSprite == null) _ingotsSprite = LoadSpriteAsset(HudIngotsIconAssetPath);
            if (_expSprite == null) _expSprite = LoadSpriteAsset(HudExpIconAssetPath);
            if (_blueprintSprite == null) _blueprintSprite = LoadSpriteAsset(HudBlueprintIconAssetPath);
            if (_blueprintSprite == null) _blueprintSprite = _ingotsSprite;
            if (_tesseractSprite == null) _tesseractSprite = LoadSpriteAsset(HudTesseractIconAssetPath);
            if (_tesseractSprite == null) _tesseractSprite = _matterSprite;
            _energySprite = energyIconSprite;
            if (_energySprite == null) _energySprite = LoadSpriteAsset(HudEnergyIconAssetPath);
        }

        /// <summary>Иконка рецепта по полю reward_blueprint бота (green / blue / purple / gold).</summary>
        private Sprite ResolveBlueprintRewardSprite(string rewardBlueprint)
        {
            if (string.IsNullOrWhiteSpace(rewardBlueprint))
                return _blueprintSprite;
            switch (rewardBlueprint.Trim().ToLowerInvariant())
            {
                case "green": return recipeGreenSprite != null ? recipeGreenSprite : _blueprintSprite;
                case "blue": return recipeBlueSprite != null ? recipeBlueSprite : _blueprintSprite;
                case "purple": return recipePurpleSprite != null ? recipePurpleSprite : _blueprintSprite;
                case "gold": return recipeGoldSprite != null ? recipeGoldSprite : _blueprintSprite;
                default: return _blueprintSprite;
            }
        }

        /// <summary>Спрайт слота экипировки по индексу 0..7 (как EquipmentSlotId).</summary>
        public Sprite GetEquipmentSlotSprite(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return slotHelmetSprite;
                case 1: return slotShouldersSprite;
                case 2: return slotChestSprite;
                case 3: return slotGlovesSprite;
                case 4: return slotLegsSprite;
                case 5: return slotFeetSprite;
                case 6: return slotWeaponLeftSprite;
                case 7: return slotWeaponRightSprite;
                default: return null;
            }
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

            if (blueprintIconSprite == null)
            {
                blueprintIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudBlueprintIconAssetPath);
                changed |= blueprintIconSprite != null;
            }

            if (tesseractIconSprite == null)
            {
                tesseractIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudTesseractIconAssetPath);
                changed |= tesseractIconSprite != null;
            }

            if (energyIconSprite == null)
            {
                energyIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(HudEnergyIconAssetPath);
                changed |= energyIconSprite != null;
            }

            if (changed)
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private void EnsureDifficultyTabs()
        {
            if (_difficultyTabsRoot != null)
                return;

            var existing = GameObject.Find(DifficultyTabsRootName);
            if (existing != null)
            {
                _difficultyTabsRoot = existing.transform;
                CacheDifficultyTabRefs(_difficultyTabsRoot);
                if (_difficultyButtons.Count == 3)
                {
                    WireDifficultyTabButtons();
                    ApplyDifficultyTabVisuals();
                    return;
                }
            }

            var bg = GameObject.Find("MineBackground");
            var parent = bg != null ? bg.transform : FindFirstObjectByType<Canvas>()?.transform;
            if (parent == null)
                return;

            ShrinkMineContentForDifficultyTabs();

            var rootGo = new GameObject(DifficultyTabsRootName, typeof(RectTransform));
            var rootRt = rootGo.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = new Vector2(0.20f, 0.885f);
            rootRt.anchorMax = new Vector2(0.96f, 0.945f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var hl = rootGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.padding = new RectOffset(4, 4, 2, 2);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlHeight = true;
            hl.childControlWidth = true;
            hl.childForceExpandHeight = true;
            hl.childForceExpandWidth = true;

            CreateDifficultyTabButton(rootRt, "easy", "ЛЁГКАЯ");
            CreateDifficultyTabButton(rootRt, "medium", "СРЕДНЯЯ");
            CreateDifficultyTabButton(rootRt, "hard", "ТЯЖЁЛАЯ");

            _difficultyTabsRoot = rootRt;
            WireDifficultyTabButtons();
            ApplyDifficultyTabVisuals();

            var title = GameObject.Find("Title");
            if (title != null)
            {
                var titleRt = title.GetComponent<RectTransform>();
                if (titleRt != null)
                {
                    titleRt.anchorMin = new Vector2(0.34f, 0.945f);
                    titleRt.anchorMax = new Vector2(0.80f, 0.985f);
                }
            }
        }

        private static void ShrinkMineContentForDifficultyTabs()
        {
            SetRectAnchorMaxY(GameObject.Find("CardsScrollView"), 0.875f);
            SetRectAnchorMaxY(GameObject.Find("FloorLift"), 0.875f);
        }

        private static void SetRectAnchorMaxY(GameObject go, float maxY)
        {
            if (go == null)
                return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                return;
            var max = rt.anchorMax;
            max.y = maxY;
            rt.anchorMax = max;
        }

        private void CreateDifficultyTabButton(Transform parent, string difficultyId, string label)
        {
            var go = new GameObject("Diff_" + difficultyId, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(CanvasGroup));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.14f, 0.16f, 0.20f, 0.98f);
            var le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 40f;
            le.preferredHeight = 44f;

            var txt = CreateText(rt, "Label", label, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            txt.color = Color.white;
            txt.fontStyle = FontStyle.Bold;

            var neon = go.AddComponent<UiNeonPulseOutline>();
            neon.SetHighlight(false);

            _difficultyButtons[difficultyId] = go.GetComponent<Button>();
            _difficultyNeon[difficultyId] = neon;
            _difficultyCanvasGroups[difficultyId] = go.GetComponent<CanvasGroup>();
            _difficultyLabels[difficultyId] = txt;
        }

        private void CacheDifficultyTabRefs(Transform root)
        {
            _difficultyButtons.Clear();
            _difficultyNeon.Clear();
            _difficultyCanvasGroups.Clear();
            _difficultyLabels.Clear();
            if (root == null)
                return;

            CacheOneDifficultyTab(root, "easy");
            CacheOneDifficultyTab(root, "medium");
            CacheOneDifficultyTab(root, "hard");
        }

        private void CacheOneDifficultyTab(Transform root, string difficultyId)
        {
            var tr = root.Find("Diff_" + difficultyId);
            if (tr == null)
                return;
            var btn = tr.GetComponent<Button>();
            if (btn == null)
                return;
            _difficultyButtons[difficultyId] = btn;
            var neon = tr.GetComponent<UiNeonPulseOutline>() ?? tr.gameObject.AddComponent<UiNeonPulseOutline>();
            _difficultyNeon[difficultyId] = neon;
            var cg = tr.GetComponent<CanvasGroup>() ?? tr.gameObject.AddComponent<CanvasGroup>();
            _difficultyCanvasGroups[difficultyId] = cg;
            _difficultyLabels[difficultyId] = FindTextByName(tr, "Label");
        }

        private void WireDifficultyTabButtons()
        {
            foreach (var kv in _difficultyButtons)
            {
                var diff = kv.Key;
                var btn = kv.Value;
                if (btn == null)
                    continue;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnDifficultyTabClicked(diff));
            }
        }

        private void OnDifficultyTabClicked(string difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return;
            if (string.Equals(difficulty, _difficulty, StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsDifficultyUnlocked(difficulty))
            {
                ShowMineToast(DescribeDifficultyLocked(difficulty));
                return;
            }

            _ = SetDifficultyAsync(difficulty);
        }

        private static string DescribeDifficultyLocked(string difficulty)
        {
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase))
                return "Средняя шахта откроется после победы над боссом 12 этажа на Лёгкой.";
            if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
                return "Тяжёлая шахта откроется после победы над боссом 12 этажа на Средней.";
            return "Сложность пока закрыта.";
        }

        private async Task SetDifficultyAsync(string difficulty)
        {
            if (_setDifficultyInFlight || _cts == null || _cts.IsCancellationRequested)
                return;
            if (NakamaBootstrap.Instance == null)
                return;

            _setDifficultyInFlight = true;
            SetDifficultyButtonsInteractable(false);
            try
            {
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                    return;

                var request = new SetDifficultyRequest
                {
                    difficulty = difficulty,
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch()
                };
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMineSetDifficulty, JsonUtility.ToJson(request), canceller: _cts.Token);
                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                var model = JsonUtility.FromJson<SetDifficultyResponse>(payload);
                if (model == null)
                    return;

                if (!model.ok)
                {
                    if (string.Equals(model.err, "difficulty_locked", StringComparison.OrdinalIgnoreCase))
                        ShowMineToast(DescribeDifficultyLocked(difficulty));
                    else
                        ShowMineToast("Не удалось сменить сложность шахты.");
                    if (model.progression != null)
                        ApplyUnlockedFromProgression(model.progression);
                    ApplyDifficultyTabVisuals();
                    return;
                }

                _difficulty = string.IsNullOrWhiteSpace(model.difficulty) ? difficulty : model.difficulty;
                if (model.unlocked != null)
                {
                    _unlockedEasy = Mathf.Max(1, model.unlocked.easy);
                    _unlockedMedium = Mathf.Max(0, model.unlocked.medium);
                    _unlockedHard = Mathf.Max(0, model.unlocked.hard);
                }
                else if (model.progression != null)
                {
                    ApplyUnlockedFromProgression(model.progression);
                }

                if (model.progression != null)
                    _progression = model.progression;

                CloseMonsterModal();
                await RefreshAsync(_cts.Token);
                await RefreshResourcesAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MineScene] SetDifficulty failed: " + e.Message);
                ShowMineToast("Не удалось сменить сложность шахты.");
            }
            finally
            {
                _setDifficultyInFlight = false;
                SetDifficultyButtonsInteractable(true);
                ApplyDifficultyTabVisuals();
            }
        }

        private void SetDifficultyButtonsInteractable(bool interactable)
        {
            foreach (var kv in _difficultyButtons)
            {
                if (kv.Value != null)
                    kv.Value.interactable = interactable;
            }
        }

        private void ApplyDifficultyTabVisuals()
        {
            if (_difficultyButtons.Count == 0)
                return;

            ApplyOneDifficultyTabVisual("easy", "ЛЁГКАЯ");
            ApplyOneDifficultyTabVisual("medium", "СРЕДНЯЯ");
            ApplyOneDifficultyTabVisual("hard", "ТЯЖЁЛАЯ");
        }

        private void ApplyOneDifficultyTabVisual(string difficultyId, string baseLabel)
        {
            var selected = string.Equals(_difficulty, difficultyId, StringComparison.OrdinalIgnoreCase);
            var unlocked = IsDifficultyUnlocked(difficultyId);

            if (_difficultyNeon.TryGetValue(difficultyId, out var neon) && neon != null)
                neon.SetHighlight(selected);

            if (_difficultyCanvasGroups.TryGetValue(difficultyId, out var cg) && cg != null)
            {
                cg.alpha = unlocked ? (selected ? 1f : 0.72f) : 0.38f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }

            if (_difficultyLabels.TryGetValue(difficultyId, out var label) && label != null)
            {
                label.text = unlocked ? baseLabel : (baseLabel + " ·");
                label.color = selected
                    ? new Color(0.55f, 1f, 0.55f, 1f)
                    : (unlocked ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f));
            }

            if (_difficultyButtons.TryGetValue(difficultyId, out var btn) && btn != null)
            {
                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    img.color = selected
                        ? new Color(0.12f, 0.28f, 0.14f, 0.98f)
                        : new Color(0.14f, 0.16f, 0.20f, 0.98f);
                }
            }
        }

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
            SetHeaderResourceText(_energyBinding, Mathf.Max(0, model.energy).ToString());
            SetHeaderResourceText(_oreBinding, FormatCompact(model.ore));
            SetHeaderResourceText(_goldBinding, FormatCompact(model.gold));
            SetHeaderResourceText(_ingotsBinding, FormatCompact(model.ingots));
            SetHeaderResourceText(_matterBinding, FormatCompact(model.matter));
            var keyTotal = model.miner_key + model.dark_key;
            if (keyTotal <= 0 && model.keys > 0)
                keyTotal = model.keys;
            SetHeaderResourceText(_keysBinding, FormatCompact(keyTotal));
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

        private static int BarrierCostMultiplier(string difficulty)
        {
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
                return 5;
            return 1;
        }

        private BarrierRequirement GetBarrierRequirement(int floor)
        {
            if (!BarrierRequirements.TryGetValue(Mathf.Clamp(floor, 1, 12), out var baseReq) || baseReq == null)
                return null;

            var mul = BarrierCostMultiplier(_difficulty);
            return new BarrierRequirement
            {
                level = baseReq.level,
                ore = baseReq.ore * mul,
                gold = baseReq.gold * mul,
                matter = baseReq.matter * mul,
                key_id = baseReq.key_id,
                key_amount = baseReq.key_amount,
            };
        }

        private bool IsPrevMonsterDefeatedForBarrier(int barrierFloor)
        {
            if (barrierFloor <= 1)
                return true;
            return _mineByFloor.TryGetValue(barrierFloor - 1, out var prev) && prev != null && prev.wins > 0;
        }

        private string BuildBarrierInfoText(int floor, BarrierRequirement req)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Этаж {floor} закрыт барьером.");
            var mul = BarrierCostMultiplier(_difficulty);
            if (mul > 1)
                sb.AppendLine($"Стоимость ×{mul} для текущей сложности шахты.");
            return sb.ToString();
        }

        private void PopulateBarrierRequirements(int floor, BarrierRequirement req)
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
            if (floor > 1)
            {
                var prevOk = IsPrevMonsterDefeatedForBarrier(floor);
                entries.Add(new RewardEntry
                {
                    icon = null,
                    text = prevOk ? $"Победа на этаже {floor - 1}: да" : $"Победа на этаже {floor - 1}: нет",
                    color = GetEnoughColor(prevOk),
                });
                hasAny = true;
            }

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
            _blueprintSprite ??= LoadSpriteAsset(HudBlueprintIconAssetPath);
            if (_blueprintSprite == null) _blueprintSprite = _ingotsSprite;
            _tesseractSprite ??= LoadSpriteAsset(HudTesseractIconAssetPath);
            if (_tesseractSprite == null) _tesseractSprite = _matterSprite;

            var isBossFloor = bot.floor == 4 || bot.floor == 8 || bot.floor == 12;
            // Превью как award_pve_victory: base × mine_reward_multiplier(difficulty).
            var rewardXp = MineRewardFormat.ScaleRewardAmount(bot.reward_xp, _difficulty);
            var rewardGold = MineRewardFormat.ScaleRewardAmount(bot.reward_gold, _difficulty);
            var rewardOre = MineRewardFormat.ScaleRewardAmount(bot.reward_ore, _difficulty);
            var rewardIngots = MineRewardFormat.ScaleRewardAmount(bot.reward_ingots, _difficulty);
            if (rewardXp > 0)
                entries.Add(new RewardEntry { icon = _expSprite, text = FormatCompact(rewardXp), color = Color.white });
            if (rewardGold > 0)
                entries.Add(new RewardEntry { icon = _goldSprite, text = FormatCompact(rewardGold), color = Color.white });
            if (rewardOre > 0)
                entries.Add(new RewardEntry { icon = _oreSprite, text = FormatCompact(rewardOre), color = Color.white });
            if (rewardIngots > 0)
            {
                var n = FormatCompact(rewardIngots);
                string ingotLabel;
                if (!isBossFloor)
                {
                    var pct = Mathf.RoundToInt(IngotDropChanceNonBoss(bot.floor) * 100f);
                    ingotLabel = pct >= 100 ? n : $"{n}(~{pct}%)";
                }
                else
                {
                    var doubled = rewardIngots * 2;
                    ingotLabel = FormatCompact(doubled);
                }

                var ingotIcon = MineRewardFormat.IngotIconForDifficulty(_difficulty, itemCatalog, _ingotsSprite);
                entries.Add(new RewardEntry { icon = ingotIcon, text = ingotLabel, color = Color.white });
            }
            if (!string.IsNullOrWhiteSpace(bot.reward_key_id) && bot.reward_key_amount > 0)
                entries.Add(new RewardEntry { icon = _lockSprite, text = $"{bot.reward_key_id} x{bot.reward_key_amount}", color = Color.white });

            if (bot.reward_matter_min > 0 || bot.reward_matter_max > 0)
            {
                var minMatter = MineRewardFormat.ScaleRewardAmount(Mathf.Max(0, bot.reward_matter_min), _difficulty);
                var maxMatter = MineRewardFormat.ScaleRewardAmount(Mathf.Max(bot.reward_matter_min, bot.reward_matter_max), _difficulty);
                maxMatter = Mathf.Max(minMatter, maxMatter);
                var matterText = minMatter == maxMatter
                    ? FormatCompact(minMatter)
                    : $"{FormatCompact(minMatter)}-{FormatCompact(maxMatter)}";
                entries.Add(new RewardEntry { icon = _matterSprite, text = matterText, color = Color.white });
            }

            var recipePct = MineRecipeDropChancePercent(bot.floor);
            var v43RecipeFloor = !isBossFloor && recipePct > 0;

            if (!v43RecipeFloor && !string.IsNullOrWhiteSpace(bot.reward_blueprint))
            {
                var bp = bot.reward_blueprint.Trim();
                entries.Add(new RewardEntry
                {
                    icon = ResolveBlueprintRewardSprite(bp),
                    text = MineRewardFormat.LegacyBlueprintShortLabel(bp),
                    color = Color.white
                });
            }

            if (v43RecipeFloor)
            {
                var expectedId = MineExpectedRecipeItemId(bot.floor, _difficulty);
                var already = !string.IsNullOrEmpty(expectedId) &&
                              MineRecipeDropLearned(expectedId, _cachedLearnedRecipeIds);
                if (!already)
                {
                    var slotRu = MineRewardFormat.RecipeSlotNameRuFromRecipeItemId(expectedId);
                    string recipeLabel;
                    if (recipePct >= 100)
                    {
                        recipeLabel = string.IsNullOrEmpty(slotRu) ? "Рецепт" : $"Рецепт {slotRu}";
                    }
                    else
                    {
                        recipeLabel = string.IsNullOrEmpty(slotRu)
                            ? $"Рецепт(~{recipePct}%)"
                            : $"Рецепт {slotRu}(~{recipePct}%)";
                    }
                    var recipeIcon = string.IsNullOrEmpty(expectedId)
                        ? _blueprintSprite
                        : MineRewardFormat.ItemIconOrFallback(itemCatalog, expectedId, _blueprintSprite);
                    entries.Add(new RewardEntry
                    {
                        icon = recipeIcon,
                        text = recipeLabel,
                        color = new Color(0.95f, 0.92f, 0.75f)
                    });
                }
            }

            if (bot.reward_tesseract_chance > 0f)
            {
                var pct = Mathf.RoundToInt(bot.reward_tesseract_chance * 100f);
                entries.Add(new RewardEntry { icon = _tesseractSprite, text = $"Тессеракт: {pct}%", color = Color.white });
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

            var rowPreferredHeight = stackIconOverValue ? 120f : 72f;
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

            //const float iconSize = 36f;
            const float iconSize = 60f;

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
                iconLe.minWidth = iconSize;
                iconLe.minHeight = iconSize;
                iconLe.preferredWidth = iconSize;
                iconLe.preferredHeight = iconSize;
                iconLe.flexibleWidth = 0f;
                iconLe.flexibleHeight = 0f;
            }

            var labelGo = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(cellRt, false);
            var label = labelGo.GetComponent<Text>();
            label.font = _modalMonsterRewardsValueFont != null ? _modalMonsterRewardsValueFont : GetBuiltinFont();
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 30;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = value;
            var labelLe = labelGo.GetComponent<LayoutElement>();
            labelLe.minHeight = 34f;
            labelLe.preferredHeight = 36f;
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

            const float iconSize = 60f;

            var row = new GameObject("RequirementRow", typeof(RectTransform), typeof(LayoutElement));
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.SetParent(parent, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(0, 0, 0, 0);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            var rowLe = row.GetComponent<LayoutElement>();
            rowLe.preferredHeight = iconSize;
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
                iconLe.minWidth = iconSize;
                iconLe.minHeight = iconSize;
                iconLe.preferredWidth = iconSize;
                iconLe.preferredHeight = iconSize;
                iconLe.flexibleWidth = 0f;
                iconLe.flexibleHeight = 0f;
            }

            var labelGo = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(rowRt, false);
            var label = labelGo.GetComponent<Text>();
            label.font = _modalMonsterRewardsValueFont != null ? _modalMonsterRewardsValueFont : GetBuiltinFont();
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 30;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = value;
            var labelLe = labelGo.GetComponent<LayoutElement>();
            labelLe.minHeight = 34f;
            labelLe.preferredHeight = 36f;
            labelLe.flexibleWidth = 1f;
        }

        private static Color GetEnoughColor(bool isEnough)
        {
            return isEnough ? new Color(0.55f, 0.95f, 0.58f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
        }

        private int GetOwnedKeyAmount(string keyId)
        {
            var fromProg = 0;
            if (_progression != null && _progression.key_items != null)
            {
                if (string.Equals(keyId, "miner_key", StringComparison.OrdinalIgnoreCase))
                    fromProg = Mathf.Max(0, _progression.key_items.miner_key);
                else if (string.Equals(keyId, "dark_key", StringComparison.OrdinalIgnoreCase))
                    fromProg = Mathf.Max(0, _progression.key_items.dark_key);
            }

            var fromRes = 0;
            if (_lastResources != null)
            {
                if (string.Equals(keyId, "miner_key", StringComparison.OrdinalIgnoreCase))
                    fromRes = (int)Math.Max(0L, _lastResources.miner_key);
                else if (string.Equals(keyId, "dark_key", StringComparison.OrdinalIgnoreCase))
                    fromRes = (int)Math.Max(0L, _lastResources.dark_key);
            }

            return Mathf.Max(fromProg, fromRes);
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
            ApplyMonsterIconLayout(rt);
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        private static void ApplyMonsterIconLayout(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void CacheModalFooterDefaultsIfNeeded()
        {
            if (_modalFooterLayoutCached || _modalDismissButton == null)
                return;

            _modalFooterHorizontalLayout = _modalDismissButton.transform.parent != null
                ? _modalDismissButton.transform.parent.GetComponent<HorizontalLayoutGroup>()
                : null;
            _modalDismissLayoutElement = _modalDismissButton.GetComponent<LayoutElement>();
            var prefixFromRow = _modalDismissButton.transform.Find(MineFooterRowName + "/Prefix");
            _modalDismissLabel = prefixFromRow != null
                ? prefixFromRow.GetComponent<Text>()
                : _modalDismissButton.GetComponentInChildren<Text>(true);

            if (_modalFooterHorizontalLayout != null)
            {
                _modalFooterDefaultChildAlignment = _modalFooterHorizontalLayout.childAlignment;
                _modalFooterDefaultChildForceExpandWidth = _modalFooterHorizontalLayout.childForceExpandWidth;
            }

            if (_modalDismissLayoutElement != null)
            {
                _modalDismissDefaultPreferredWidth = _modalDismissLayoutElement.preferredWidth;
                _modalDismissDefaultPreferredHeight = _modalDismissLayoutElement.preferredHeight;
                _modalDismissDefaultFlexibleWidth = _modalDismissLayoutElement.flexibleWidth;
                _modalDismissDefaultFlexibleHeight = _modalDismissLayoutElement.flexibleHeight;
            }

            if (_modalDismissLabel != null)
                _modalDismissDefaultLabelColor = _modalDismissLabel.color;

            _modalFooterLayoutCached = true;
        }

        private void ApplyModalFooterBarrierLayout(bool barrierMode)
        {
            CacheModalFooterDefaultsIfNeeded();
            if (!_modalFooterLayoutCached)
                return;

            if (barrierMode)
            {
                if (_modalFightButton != null)
                    _modalFightButton.gameObject.SetActive(false);

                if (_modalFooterHorizontalLayout != null)
                {
                    _modalFooterHorizontalLayout.childAlignment = TextAnchor.MiddleCenter;
                    _modalFooterHorizontalLayout.childForceExpandWidth = false;
                }

                if (_modalDismissLayoutElement != null)
                {
                    _modalDismissLayoutElement.minWidth = 0f;
                    _modalDismissLayoutElement.preferredWidth = BarrierDismissButtonMaxWidth;
                    _modalDismissLayoutElement.flexibleWidth = 0f;
                    _modalDismissLayoutElement.preferredHeight = BarrierDismissButtonMaxWidth / ModalFooterButtonAspect;
                }

                if (_modalDismissLabel != null)
                    _modalDismissLabel.color = BarrierDismissLabelColor;
            }
            else
            {
                if (_modalFightButton != null)
                    _modalFightButton.gameObject.SetActive(true);

                if (_modalFooterHorizontalLayout != null)
                {
                    _modalFooterHorizontalLayout.childAlignment = _modalFooterDefaultChildAlignment;
                    _modalFooterHorizontalLayout.childForceExpandWidth = _modalFooterDefaultChildForceExpandWidth;
                }

                if (_modalDismissLayoutElement != null)
                {
                    _modalDismissLayoutElement.minWidth = -1f;
                    _modalDismissLayoutElement.preferredWidth = _modalDismissDefaultPreferredWidth;
                    _modalDismissLayoutElement.preferredHeight = _modalDismissDefaultPreferredHeight;
                    _modalDismissLayoutElement.flexibleWidth = _modalDismissDefaultFlexibleWidth;
                    _modalDismissLayoutElement.flexibleHeight = _modalDismissDefaultFlexibleHeight;
                }

                if (_modalDismissLabel != null)
                    _modalDismissLabel.color = _modalDismissDefaultLabelColor;
            }
        }

        private static ResourceCostEntry[] DefaultCostAttack()
        {
            return new[]
            {
                new ResourceCostEntry { resource = "energy", amount = 15 }
            };
        }

        private static ResourceCostEntry[] DefaultCostBanish()
        {
            return new[]
            {
                new ResourceCostEntry { resource = "energy", amount = 5 }
            };
        }

        private static ResourceCostEntry[] CoalesceCost(ResourceCostEntry[] c, ResourceCostEntry[] fallback)
        {
            return c != null && c.Length > 0 ? c : fallback;
        }

        private void ApplyFightButtonCosts(PveBotInfo bot)
        {
            EnsureFooterRowWithCostStrip(_modalFightButton, "В бой");
            SetFooterPrefix(_modalFightButton, "В бой");
            PopulateCostStrip(_modalFightButton, CoalesceCost(bot != null ? bot.cost_attack : null, DefaultCostAttack()));
        }

        private void ApplyDismissButtonCosts(bool canSummon, PveBotInfo bot)
        {
            EnsureFooterRowWithCostStrip(_modalDismissButton, "Прогнать");
            if (canSummon)
            {
                SetFooterPrefix(_modalDismissButton, "Призвать");
                PopulateCostStrip(_modalDismissButton, new[]
                {
                    new ResourceCostEntry { resource = "energy", amount = SummonEnergyCost },
                    new ResourceCostEntry { resource = "gold", amount = SummonGoldCost }
                });
            }
            else
            {
                SetFooterPrefix(_modalDismissButton, "Прогнать");
                PopulateCostStrip(_modalDismissButton, CoalesceCost(bot != null ? bot.cost_banish : null, DefaultCostBanish()));
            }
        }

        private void EnsureFooterRowWithCostStrip(Button btn, string defaultPrefix)
        {
            if (btn == null) return;
            if (btn.transform.Find(MineFooterRowName) != null)
                return;

            Text oldLabel = null;
            for (var i = 0; i < btn.transform.childCount; i++)
            {
                var ch = btn.transform.GetChild(i);
                if (ch.name == "Label")
                {
                    oldLabel = ch.GetComponent<Text>();
                    break;
                }
            }

            var prefixText = defaultPrefix;
            if (oldLabel != null)
            {
                if (!string.IsNullOrEmpty(oldLabel.text))
                    prefixText = oldLabel.text;
                Destroy(oldLabel.gameObject);
            }

            var row = new GameObject(MineFooterRowName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(btn.transform, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = Vector2.zero;
            rowRt.anchorMax = Vector2.one;
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;
            row.transform.SetAsFirstSibling();
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.padding = new RectOffset(6, 6, 4, 4);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlHeight = true;
            hl.childControlWidth = false;
            hl.childForceExpandHeight = true;
            hl.childForceExpandWidth = false;

            var prefixGo = new GameObject("Prefix", typeof(RectTransform), typeof(Text));
            prefixGo.transform.SetParent(row.transform, false);
            var prefix = prefixGo.GetComponent<Text>();
            prefix.font = GetBuiltinFont();
            prefix.fontSize = 20;
            prefix.color = Color.white;
            prefix.alignment = TextAnchor.MiddleCenter;
            prefix.raycastTarget = false;
            prefix.text = prefixText;
            var prefixLe = prefixGo.AddComponent<LayoutElement>();
            prefixLe.preferredWidth = -1f;
            prefixLe.flexibleWidth = 0f;

            var strip = new GameObject("CostStrip", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            strip.transform.SetParent(row.transform, false);
            var stripHl = strip.GetComponent<HorizontalLayoutGroup>();
            stripHl.spacing = 6f;
            stripHl.padding = new RectOffset(0, 0, 0, 0);
            stripHl.childAlignment = TextAnchor.MiddleCenter;
            stripHl.childControlHeight = true;
            stripHl.childControlWidth = true;
            stripHl.childForceExpandHeight = false;
            stripHl.childForceExpandWidth = false;
            var stripLe = strip.AddComponent<LayoutElement>();
            stripLe.flexibleWidth = 0f;
            stripLe.minWidth = 0f;
            var stripFitter = strip.AddComponent<ContentSizeFitter>();
            stripFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            stripFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static void SetFooterPrefix(Button btn, string prefix)
        {
            if (btn == null || string.IsNullOrEmpty(prefix)) return;
            var t = btn.transform.Find(MineFooterRowName + "/Prefix")?.GetComponent<Text>();
            if (t != null) t.text = prefix;
        }

        private void PopulateCostStrip(Button btn, ResourceCostEntry[] costs)
        {
            if (btn == null) return;
            var strip = btn.transform.Find(MineFooterRowName + "/CostStrip");
            if (strip == null) return;
            EnsureCostStripHugsContent(strip);

            for (var i = strip.childCount - 1; i >= 0; i--)
                Destroy(strip.GetChild(i).gameObject);

            if (costs == null || costs.Length == 0)
                return;

            foreach (var e in costs)
            {
                if (e == null || e.amount <= 0) continue;

                var cell = new GameObject("CostCell", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
                cell.transform.SetParent(strip, false);
                var cellHl = cell.GetComponent<HorizontalLayoutGroup>();
                cellHl.spacing = 3f;
                cellHl.padding = new RectOffset(0, 0, 0, 0);
                cellHl.childAlignment = TextAnchor.MiddleCenter;
                cellHl.childControlHeight = true;
                cellHl.childControlWidth = true;
                cellHl.childForceExpandHeight = false;
                cellHl.childForceExpandWidth = false;
                var cellFitter = cell.GetComponent<ContentSizeFitter>();
                cellFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                cellFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var cellLe = cell.AddComponent<LayoutElement>();
                cellLe.flexibleWidth = 0f;
                cellLe.flexibleHeight = 0f;

                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(cell.transform, false);
                var img = iconGo.GetComponent<Image>();
                img.sprite = ResolveResourceIcon(e.resource);
                img.preserveAspect = true;
                img.color = img.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                var iconLe = iconGo.AddComponent<LayoutElement>();
                iconLe.preferredWidth = 26f;
                iconLe.preferredHeight = 26f;
                iconLe.minWidth = 26f;
                iconLe.minHeight = 26f;
                iconLe.flexibleWidth = 0f;
                iconLe.flexibleHeight = 0f;

                var amtGo = new GameObject("Amt", typeof(RectTransform), typeof(Text));
                amtGo.transform.SetParent(cell.transform, false);
                var amt = amtGo.GetComponent<Text>();
                amt.font = GetBuiltinFont();
                amt.fontSize = 19;
                amt.color = Color.white;
                amt.alignment = TextAnchor.MiddleLeft;
                amt.horizontalOverflow = HorizontalWrapMode.Overflow;
                amt.verticalOverflow = VerticalWrapMode.Truncate;
                amt.text = FormatCompact(e.amount);
                amt.raycastTarget = false;
                var amtFitter = amtGo.AddComponent<ContentSizeFitter>();
                amtFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                amtFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var amtLe = amtGo.AddComponent<LayoutElement>();
                amtLe.flexibleWidth = 0f;
                amtLe.flexibleHeight = 0f;
                amtLe.minWidth = 0f;
            }
        }

        /// <summary>
        /// Старые модалки создавали CostStrip с flexibleWidth=1 — полоса растягивалась и разносила иконку и число.
        /// Как в Match3GameOverPanel RewardRow: контент по ширине, без растягивания дочерних ячеек.
        /// </summary>
        private static void EnsureCostStripHugsContent(Transform strip)
        {
            if (strip == null) return;
            var hl = strip.GetComponent<HorizontalLayoutGroup>();
            if (hl != null)
            {
                hl.childControlWidth = true;
                hl.childControlHeight = true;
                hl.childForceExpandWidth = false;
                hl.childForceExpandHeight = false;
            }
            var le = strip.GetComponent<LayoutElement>();
            if (le != null)
                le.flexibleWidth = 0f;
            var fit = strip.GetComponent<ContentSizeFitter>();
            if (fit == null)
                fit = strip.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private Sprite ResolveResourceIcon(string resourceId)
        {
            var r = (resourceId ?? "").Trim().ToLowerInvariant();
            switch (r)
            {
                case "energy":
                    return _energySprite;
                case "gold":
                    return _goldSprite;
                case "ore":
                    return _oreSprite;
                case "matter":
                    return _matterSprite;
                case "ingots":
                    return _ingotsSprite;
                case "miner_key":
                case "dark_key":
                    return _lockSprite;
                default:
                    return null;
            }
        }

        private static string DescribeFirstMissingCost(ResourceCostEntry[] cost, PlayerResourcesRpcResponse r)
        {
            if (r == null || cost == null) return null;
            foreach (var e in cost)
            {
                if (e == null || e.amount <= 0) continue;
                var id = (e.resource ?? "").Trim().ToLowerInvariant();
                switch (id)
                {
                    case "energy":
                        if (r.energy < e.amount)
                            return $"Не хватает энергии: нужно {e.amount}, доступно {r.energy}.";
                        break;
                    case "gold":
                        if (r.gold < e.amount)
                            return $"Не хватает золота: нужно {e.amount}, доступно {r.gold}.";
                        break;
                    case "ore":
                        if (r.ore < e.amount)
                            return $"Не хватает руды: нужно {e.amount}, доступно {r.ore}.";
                        break;
                    case "matter":
                        if (r.matter < e.amount)
                            return $"Не хватает материи: нужно {e.amount}, доступно {r.matter}.";
                        break;
                    case "ingots":
                        if (r.ingots < e.amount)
                            return $"Не хватает слитков: нужно {e.amount}, доступно {r.ingots}.";
                        break;
                    case "miner_key":
                        if (r.miner_key < e.amount)
                            return $"Не хватает ключа miner_key: нужно {e.amount}, доступно {r.miner_key}.";
                        break;
                    case "dark_key":
                        if (r.dark_key < e.amount)
                            return $"Не хватает ключа dark_key: нужно {e.amount}, доступно {r.dark_key}.";
                        break;
                }
            }

            return null;
        }

        private static void SetButtonLabel(Button btn, string value)
        {
            if (btn == null) return;
            var rowPrefix = btn.transform.Find(MineFooterRowName + "/Prefix")?.GetComponent<Text>();
            if (rowPrefix != null)
            {
                rowPrefix.text = value;
                return;
            }

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
            public MineUnlockedInfo mine_unlocked;
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
            public int energy;
            public int energy_max;
            public long ore;
            public long matter;
            public long ingots;
            public string key_id;
            public long have;
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
            public int required_prev_floor;
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
            public MineUnlockedInfo unlocked;
        }

        [Serializable]
        private sealed class MineUnlockedInfo
        {
            public int easy;
            public int medium;
            public int hard;
        }

        [Serializable]
        private sealed class SetDifficultyRequest
        {
            public string difficulty;
            public long session_epoch;
        }

        [Serializable]
        private sealed class SetDifficultyResponse
        {
            public bool ok;
            public string err;
            public string difficulty;
            public MineUnlockedInfo unlocked;
            public ProgressionInfo progression;
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
            public int wins;
        }

        [Serializable]
        private sealed class ResourceCostEntry
        {
            public string resource;
            public int amount;
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
            public ResourceCostEntry[] cost_attack;
            public ResourceCostEntry[] cost_banish;
        }

        private sealed class FloorRowRefs
        {
            public Transform root;
            public Text stateText;
            public Button monsterButton;
            public Button lockButton;
            public RectTransform cooldownClusterRt;
            public RectTransform cooldownTextHostRt;
            public Button summonWhistleButton;
            public Transform stateTextOriginalParent;
            public int stateTextOriginalSiblingIndex;
            public bool stateTextPlacementSaved;
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
