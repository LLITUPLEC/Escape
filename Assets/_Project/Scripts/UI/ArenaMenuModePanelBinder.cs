using System;
using System.Threading;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class ArenaMenuModePanelBinder : MonoBehaviour
    {
        private const int RaceEntryMatterFallback = 2;
        private const string RpcRaceEnter = "duel_match3_race_enter";
        private const string RpcRaceInfo = "duel_match3_race_info";

        private const string IconExpPath = "Assets/_Project/img/resources_hud/exp.png";
        private const string IconOrePath = "Assets/_Project/img/resources_hud/ore.png";
        private const string IconGoldPath = "Assets/_Project/img/resources_hud/gold.png";
        private const string IconMatterPath = "Assets/_Project/img/resources_hud/matter.png";
        private const string IconIngotsPath = "Assets/_Project/img/resources_hud/ingots.png";
        private const string IconKeyPath = "Assets/_Project/img/resources_hud/key.png";
        private const string IconBlueprintPath = "Assets/_Project/img/resources_hud/blueprint.png";
        private const string IconTesseractPath = "Assets/_Project/img/resources_hud/tesseract.png";
        private const string IconEnergyPath = "Assets/_Project/img/resources_hud/energy.png";
        private const string BtnYesBgPath = "Assets/_Project/img/modals/arena_bet/arena_bet_3row.png";
        private const string BtnNoBgPath = "Assets/_Project/img/modals/arena_bet/arena_bet_1row.png";

        [Header("Paths (relative to scene)")]
        [SerializeField] private string match3ButtonPath = "ArenaMenuWorld/Background2D/ModePanel/match3Button";
        [Tooltip("Опционально. Если пусто — ищется кнопка с именем match3ProButton. §14 PvP Pro.")]
        [SerializeField] private string match3ProButtonPath = "";
        [Tooltip("Опционально. Если пусто — ищется кнопка match3Arena_Race («Спуск»).")]
        [SerializeField] private string match3RaceButtonPath = "";
        [SerializeField] private string botsButtonPath = "ArenaMenuWorld/Background2D/ModePanel/BotsButton";
        [SerializeField] private string backButtonPath = "ArenaMenuWorld/Background2D/BackButton";

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string match3SceneName = "DuelMatch3";
        [SerializeField] private bool hideBotsButton = true;

        [Header("Race modal icons (optional — иначе грузятся из assets в Editor)")]
        [SerializeField] private Sprite iconXp;
        [SerializeField] private Sprite iconOre;
        [SerializeField] private Sprite iconGold;
        [SerializeField] private Sprite iconMatter;
        [SerializeField] private Sprite iconIngots;
        [SerializeField] private Sprite iconKey;
        [SerializeField] private Sprite iconBlueprint;
        [SerializeField] private Sprite iconTesseract;
        [SerializeField] private Sprite iconEnergy;

        [Header("Race modal buttons (нужны для APK — AssetDatabase в билде недоступен)")]
        [SerializeField] private Sprite raceConfirmYesButtonSprite;
        [SerializeField] private Sprite raceConfirmNoButtonSprite;

        private Button _match3;
        private Button _match3Pro;
        private Button _match3Race;
        private Button _bots;
        private Button _back;
        private Text _botsLabelText;
        private TMP_Text _botsLabelTmp;
        private string _botsLabelDefault = "Боты";
        private bool _botsBusy;
        private bool _raceBusy;
        private CancellationTokenSource _cts;

        private GameObject _raceConfirmRoot;
        private TMP_Text _raceConfirmText;
        private TMP_Text _raceErrorText;
        private TMP_Text _raceCostCaption;
        private TMP_Text _raceRewardCaption;
        private TMP_Text _raceGoalText;
        private RectTransform _raceCostRow;
        private RectTransform _raceRewardRow;
        private LayoutElement _racePanelLayout;
        private RaceInfoRpcResponse _cachedRaceInfo;

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureRaceIcons();
            EnsureRaceButtonSprites();
        }
#endif

        private void Awake()
        {
            _cts = new CancellationTokenSource();
            _match3 = FindButton(match3ButtonPath, "match3Button");
            _match3Pro = string.IsNullOrWhiteSpace(match3ProButtonPath)
                ? FindButton("", "match3ProButton")
                : FindButton(match3ProButtonPath, "match3ProButton");
            _match3Race = string.IsNullOrWhiteSpace(match3RaceButtonPath)
                ? FindButton("", "match3Arena_Race")
                : FindButton(match3RaceButtonPath, "match3Arena_Race");
            _bots = FindButton(botsButtonPath, "BotsButton");
            _back = FindButton(backButtonPath, "BackButton");
            CacheBotsButtonLabel();

            if (hideBotsButton && _bots != null)
            {
                _bots.gameObject.SetActive(false);
                _bots = null;
            }

            EnsureRaceIcons();
            BindModeButtons();
        }

        private void Start()
        {
            if (_match3Race == null)
                BindModeButtons();
        }

        private void BindModeButtons()
        {
            if (_match3 == null)
                _match3 = FindButton(match3ButtonPath, "match3Button");
            if (_match3Pro == null)
            {
                _match3Pro = string.IsNullOrWhiteSpace(match3ProButtonPath)
                    ? FindButton("", "match3ProButton")
                    : FindButton(match3ProButtonPath, "match3ProButton");
            }
            if (_match3Race == null)
            {
                _match3Race = string.IsNullOrWhiteSpace(match3RaceButtonPath)
                    ? FindButton("", "match3Arena_Race")
                    : FindButton(match3RaceButtonPath, "match3Arena_Race");
            }

            if (_match3 != null)
            {
                _match3.onClick.RemoveListener(GoMatch3);
                _match3.onClick.AddListener(GoMatch3);
            }
            if (_match3Pro != null)
            {
                _match3Pro.onClick.RemoveListener(GoMatch3Pro);
                _match3Pro.onClick.AddListener(GoMatch3Pro);
            }
            if (_match3Race != null)
            {
                _match3Race.onClick.RemoveListener(OpenRaceEnterConfirm);
                _match3Race.onClick.AddListener(OpenRaceEnterConfirm);
            }
            if (_bots != null)
            {
                _bots.onClick.RemoveListener(GoBots);
                _bots.onClick.AddListener(GoBots);
            }
            if (_back != null)
            {
                _back.onClick.RemoveListener(BackToMainMenu);
                _back.onClick.AddListener(BackToMainMenu);
            }
        }

        private void OnDisable()
        {
            _botsBusy = false;
            _raceBusy = false;
            if (_bots != null)
                _bots.interactable = true;
            SetBotsButtonText(_botsLabelDefault);
            HideRaceConfirm();
        }

        private void OnDestroy()
        {
            if (_match3 != null) _match3.onClick.RemoveListener(GoMatch3);
            if (_match3Pro != null) _match3Pro.onClick.RemoveListener(GoMatch3Pro);
            if (_match3Race != null) _match3Race.onClick.RemoveListener(OpenRaceEnterConfirm);
            if (_bots != null) _bots.onClick.RemoveListener(GoBots);
            if (_back != null) _back.onClick.RemoveListener(BackToMainMenu);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void GoMatch3()
        {
            Match3LaunchContext.SetPvpRaceForNextMultiplayerMatch(false);
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            if (string.IsNullOrWhiteSpace(match3SceneName)) return;
            SceneManager.LoadScene(match3SceneName);
        }

        private void GoMatch3Pro()
        {
            Match3LaunchContext.SetPvpRaceForNextMultiplayerMatch(false);
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(true);
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            if (string.IsNullOrWhiteSpace(match3SceneName)) return;
            SceneManager.LoadScene(match3SceneName);
        }

        private async void OpenRaceEnterConfirm()
        {
            if (_raceBusy) return;
            EnsureRaceConfirmModal();
            EnsureRaceIcons();

            if (_raceConfirmText != null)
                _raceConfirmText.text = "За спуск нужно заплатить!";
            if (_raceErrorText != null)
            {
                _raceErrorText.text = string.Empty;
                _raceErrorText.gameObject.SetActive(false);
            }
            if (_raceCostCaption != null) _raceCostCaption.text = "Стоимость";
            if (_raceRewardCaption != null) _raceRewardCaption.text = "Награда за победу";
            if (_raceGoalText != null)
            {
                _raceGoalText.text = "Цель - набрать первым 200 маны";
                _raceGoalText.gameObject.SetActive(true);
            }
            PopulateResourceRow(_raceCostRow, FallbackEntry());
            PopulateResourceRow(_raceRewardRow, FallbackRewards());

            if (_raceConfirmRoot != null)
            {
                _raceConfirmRoot.transform.SetAsLastSibling();
                _raceConfirmRoot.SetActive(true);
            }

            try
            {
                if (NakamaBootstrap.Instance == null) return;
                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcRaceInfo, "{}", canceller: _cts.Token);
                var info = JsonUtility.FromJson<RaceInfoRpcResponse>(rpc?.Payload ?? "");
                if (info == null || !info.ok) return;
                _cachedRaceInfo = info;
                ApplyRaceInfoToModal(info);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ArenaMenu] race_info failed: " + e.Message);
            }
        }

        private void ApplyRaceInfoToModal(RaceInfoRpcResponse info)
        {
            if (info == null) return;
            var entry = info.entry != null && info.entry.Length > 0 ? info.entry : FallbackEntry();
            var rewards = info.rewards != null && info.rewards.Length > 0 ? info.rewards : FallbackRewards();
            if (_raceConfirmText != null)
                _raceConfirmText.text = "За спуск нужно заплатить!";
            if (_raceCostCaption != null)
                _raceCostCaption.text = FormatCostCaption(entry);
            if (_raceRewardCaption != null)
                _raceRewardCaption.text = "Награда за победу";
            if (_raceGoalText != null)
            {
                var goal = info.goal_mana > 0 ? info.goal_mana : 200;
                _raceGoalText.text = $"Цель - набрать первым {goal} маны";
                _raceGoalText.gameObject.SetActive(true);
            }
            PopulateResourceRow(_raceCostRow, entry);
            PopulateResourceRow(_raceRewardRow, rewards);
        }

        private static string FormatCostCaption(RaceResourceLine[] entry)
        {
            if (entry == null || entry.Length == 0) return "Стоимость";
            if (entry.Length == 1)
                return $"Стоимость {entry[0].amount} {ResourceRuName(entry[0].resource)}";
            return "Стоимость";
        }

        private async void ConfirmRaceEnterAndGo()
        {
            if (_raceBusy) return;
            _raceBusy = true;
            if (_match3Race != null) _match3Race.interactable = false;
            if (_raceErrorText != null)
            {
                _raceErrorText.text = string.Empty;
                _raceErrorText.gameObject.SetActive(false);
            }

            try
            {
                if (NakamaBootstrap.Instance == null)
                    throw new Exception("nakama_not_initialized");

                await NakamaBootstrap.Instance.EnsureConnectedAsync(_cts.Token);
                var payload = JsonUtility.ToJson(new RaceEnterRpcRequest
                {
                    session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                });
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcRaceEnter, payload, canceller: _cts.Token);
                var model = JsonUtility.FromJson<PlayerResourcesRpcResponse>(rpc?.Payload ?? "");
                if (model == null || !model.ok)
                {
                    var err = model != null && !string.IsNullOrEmpty(model.err) ? model.err : "unknown";
                    ShowRaceEnterError(err, model != null ? model.required : RaceEntryMatterFallback, model != null ? model.resource : "matter");
                    return;
                }

                PlayerResourcesService.PatchCachedFromProgression(new Progression
                {
                    gold = model.gold,
                    ore = model.ore,
                    ingots = model.ingots,
                    matter = model.matter,
                    keys = model.keys,
                    energy = model.energy,
                    energy_max = model.energy_max,
                });

                HideRaceConfirm();
                Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
                Match3LaunchContext.SetPvpRaceForNextMultiplayerMatch(true);
                Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
                if (!string.IsNullOrWhiteSpace(match3SceneName))
                    SceneManager.LoadScene(match3SceneName);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                ShowRaceEnterError(e.Message, RaceEntryMatterFallback, "matter");
            }
            finally
            {
                _raceBusy = false;
                if (_match3Race != null) _match3Race.interactable = true;
            }
        }

        private void ShowRaceEnterError(string err, int required, string resource)
        {
            if (_raceErrorText == null) return;
            string msg;
            if (!string.IsNullOrEmpty(err) && err.StartsWith("not_enough_", StringComparison.OrdinalIgnoreCase))
            {
                var res = string.IsNullOrEmpty(resource) ? err.Substring("not_enough_".Length) : resource;
                msg = $"Недостаточно: {ResourceRuName(res)} (нужно {Mathf.Max(1, required)}).";
            }
            else
                msg = "Не удалось войти: " + err;
            _raceErrorText.text = msg;
            _raceErrorText.gameObject.SetActive(true);
        }

        private void EnsureRaceConfirmModal()
        {
            if (_raceConfirmRoot != null) return;

            var parent = ResolveUiModalParent();
            if (parent == null)
            {
                Debug.LogError("[ArenaMenu] Race confirm: no Canvas found for modal.");
                return;
            }

            _raceConfirmRoot = new GameObject("RaceEnterConfirm", typeof(RectTransform));
            var rootRt = _raceConfirmRoot.GetComponent<RectTransform>();
            rootRt.SetParent(parent, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            rootRt.localScale = Vector3.one;
            rootRt.SetAsLastSibling();

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.SetParent(rootRt, false);
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            dim.GetComponent<Image>().raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(HideRaceConfirm);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.SetParent(rootRt, false);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(760f, 420f);
            panel.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 1f);
            panel.GetComponent<Image>().raycastTarget = true;
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 24, 24);
            v.spacing = 12f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _racePanelLayout = panel.AddComponent<LayoutElement>();
            _racePanelLayout.preferredWidth = 760f;
            _racePanelLayout.minWidth = 760f;

            _raceConfirmText = CreateTmp(panel.transform, "Question", "За спуск нужно заплатить!", 30f, FontStyles.Bold);
            _raceCostCaption = CreateTmp(panel.transform, "CostCaption", "Стоимость", 22f, FontStyles.Normal);
            _raceCostCaption.color = new Color(1f, 0.85f, 0.45f, 1f);
            _raceCostRow = CreateResourceRow(panel.transform, "CostRow");
            _raceRewardCaption = CreateTmp(panel.transform, "RewardCaption", "Награда за победу", 22f, FontStyles.Normal);
            _raceRewardCaption.color = new Color(0.55f, 0.9f, 0.7f, 1f);
            _raceRewardRow = CreateResourceRow(panel.transform, "RewardRow");
            _raceGoalText = CreateTmp(panel.transform, "GoalText", "Цель - набрать первым 200 маны", 28f, FontStyles.Bold);
            _raceGoalText.color = new Color(0xF0 / 255f, 1f, 0f, 1f); // #F0FF00

            _raceErrorText = CreateTmp(panel.transform, "Error", "", 20f, FontStyles.Normal);
            _raceErrorText.color = new Color(1f, 0.45f, 0.4f, 1f);
            _raceErrorText.gameObject.SetActive(false);

            var yn = new GameObject("YesNo", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            yn.transform.SetParent(panel.transform, false);
            yn.GetComponent<LayoutElement>().preferredHeight = 64f;
            var h = yn.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;

            EnsureRaceButtonSprites();
            AddModalBtn(
                yn.transform,
                "Чёрт с ним, паэхали",
                ConfirmRaceEnterAndGo,
                new Color(0x74 / 255f, 0xF9 / 255f, 0x23 / 255f, 1f),
                raceConfirmYesButtonSprite);
            AddModalBtn(
                yn.transform,
                "Сами ебитесь",
                HideRaceConfirm,
                new Color(0xFF / 255f, 0x8B / 255f, 0x80 / 255f, 1f),
                raceConfirmNoButtonSprite);

            _raceConfirmRoot.SetActive(false);
        }

        private static RectTransform CreateResourceRow(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 96f;
            go.GetComponent<LayoutElement>().minHeight = 88f;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return go.GetComponent<RectTransform>();
        }

        private void PopulateResourceRow(RectTransform row, RaceResourceLine[] lines)
        {
            if (row == null) return;
            for (var i = row.childCount - 1; i >= 0; i--)
                Destroy(row.GetChild(i).gameObject);

            if (lines == null || lines.Length == 0)
            {
                CreateTmp(row, "Empty", "—", 22f, FontStyles.Normal);
                return;
            }

            foreach (var line in lines)
            {
                if (line == null || line.amount <= 0) continue;
                CreateResourceChip(row, line);
            }
        }

        private void CreateResourceChip(Transform parent, RaceResourceLine line)
        {
            var go = new GameObject("Chip_" + line.resource, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 88f;
            go.GetComponent<LayoutElement>().preferredWidth = 96f;
            go.GetComponent<Image>().color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 8, 6);
            v.spacing = 4f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(go.transform, false);
            iconGo.GetComponent<LayoutElement>().preferredWidth = 40f;
            iconGo.GetComponent<LayoutElement>().preferredHeight = 40f;
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = SpriteForResource(line.resource);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null)
                icon.color = new Color(1f, 1f, 1f, 0.15f);

            var amount = CreateTmp(go.transform, "Amount", line.amount.ToString(), 22f, FontStyles.Bold);
            amount.alignment = TextAlignmentOptions.Center;
            var amountLe = amount.GetComponent<LayoutElement>();
            amountLe.preferredHeight = 26f;
            amountLe.minHeight = 22f;
        }

        private Sprite SpriteForResource(string resource)
        {
            EnsureRaceIcons();
            switch ((resource ?? "").Trim().ToLowerInvariant())
            {
                case "xp":
                case "exp": return iconXp;
                case "ore": return iconOre;
                case "gold": return iconGold;
                case "matter": return iconMatter;
                case "ingots":
                case "ingot": return iconIngots;
                case "keys":
                case "key": return iconKey;
                case "blueprint": return iconBlueprint;
                case "recipe": return iconBlueprint;
                case "tesseract": return iconTesseract;
                case "energy": return iconEnergy;
                default: return iconMatter;
            }
        }

        private void EnsureRaceIcons()
        {
            if (iconXp == null) iconXp = LoadHudSprite(IconExpPath);
            if (iconOre == null) iconOre = LoadHudSprite(IconOrePath);
            if (iconGold == null) iconGold = LoadHudSprite(IconGoldPath);
            if (iconMatter == null) iconMatter = LoadHudSprite(IconMatterPath);
            if (iconIngots == null) iconIngots = LoadHudSprite(IconIngotsPath);
            if (iconKey == null) iconKey = LoadHudSprite(IconKeyPath);
            if (iconBlueprint == null) iconBlueprint = LoadHudSprite(IconBlueprintPath);
            if (iconTesseract == null) iconTesseract = LoadHudSprite(IconTesseractPath);
            if (iconEnergy == null) iconEnergy = LoadHudSprite(IconEnergyPath);
        }

        private void EnsureRaceButtonSprites()
        {
            if (raceConfirmYesButtonSprite == null)
                raceConfirmYesButtonSprite = LoadHudSprite(BtnYesBgPath);
            if (raceConfirmNoButtonSprite == null)
                raceConfirmNoButtonSprite = LoadHudSprite(BtnNoBgPath);
        }

        private static Sprite LoadHudSprite(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
#if UNITY_EDITOR
            var byPath = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (byPath != null) return byPath;
#endif
            var resourcesPath = assetPath.Replace("Assets/Resources/", string.Empty);
            if (resourcesPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - 4);
            return Resources.Load<Sprite>(resourcesPath);
        }

        private static RaceResourceLine[] FallbackEntry()
        {
            return new[] { new RaceResourceLine { resource = "matter", amount = RaceEntryMatterFallback } };
        }

        private static RaceResourceLine[] FallbackRewards()
        {
            return new[]
            {
                new RaceResourceLine { resource = "xp", amount = 200 },
                new RaceResourceLine { resource = "matter", amount = 10 },
            };
        }

        private static string ResourceRuName(string resource)
        {
            switch ((resource ?? "").Trim().ToLowerInvariant())
            {
                case "xp":
                case "exp": return "опыта";
                case "ore": return "руды";
                case "gold": return "золота";
                case "matter": return "материи";
                case "ingots":
                case "ingot": return "слитков";
                case "keys":
                case "key": return "ключей";
                case "blueprint": return "чертежей";
                case "recipe": return "рецептов";
                case "tesseract": return "тессерактов";
                case "energy": return "энергии";
                default: return resource;
            }
        }

        private Transform ResolveUiModalParent()
        {
            Canvas canvas = null;
            if (_match3Race != null)
                canvas = _match3Race.GetComponentInParent<Canvas>();
            if (canvas == null && _match3 != null)
                canvas = _match3.GetComponentInParent<Canvas>();
            if (canvas == null && _match3Pro != null)
                canvas = _match3Pro.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                Canvas best = null;
                var bestScore = int.MinValue;
                foreach (var c in all)
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    var score = c.sortingOrder;
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay) score += 1000;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                canvas = best;
            }
            return canvas != null ? canvas.rootCanvas.transform : null;
        }

        private void HideRaceConfirm()
        {
            if (_raceConfirmRoot != null)
                _raceConfirmRoot.SetActive(false);
        }

        private static TMP_Text CreateTmp(Transform parent, string name, string text, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 8f;
            return tmp;
        }

        private static void AddModalBtn(Transform parent, string label, Action onClick, Color tint, Sprite bg)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 60f;
            var img = go.GetComponent<Image>();
            img.color = tint;
            if (bg != null)
            {
                img.sprite = bg;
                img.type = Image.Type.Sliced;
                img.preserveAspect = false;
            }
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var labelTmp = CreateTmp(go.transform, "Label", label, 22f, FontStyles.Bold);
            labelTmp.textWrappingMode = TextWrappingModes.Normal;
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 14f;
            labelTmp.fontSizeMax = 22f;
            // middle-stretch
            var labelRt = labelTmp.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(1f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(0f, 40f);
            labelRt.offsetMin = new Vector2(8f, labelRt.offsetMin.y);
            labelRt.offsetMax = new Vector2(-8f, labelRt.offsetMax.y);
            var labelLe = labelTmp.GetComponent<LayoutElement>();
            if (labelLe != null)
                UnityEngine.Object.Destroy(labelLe);
        }

        private void GoBots()
        {
            if (_botsBusy) return;
            _botsBusy = true;
            if (_bots != null) _bots.interactable = false;
            SetBotsButtonText("Загрузка...");
            Match3LaunchContext.SetMode(Match3LaunchMode.SoloBot);
            if (!string.IsNullOrWhiteSpace(match3SceneName))
                SceneManager.LoadScene(match3SceneName);
        }

        private void BackToMainMenu()
        {
            if (string.IsNullOrWhiteSpace(mainMenuSceneName)) return;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private static Button FindButton(string fullPath, string fallbackName)
        {
            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                var go = GameObject.Find(fullPath);
                if (go != null)
                    return go.GetComponent<Button>();
            }

            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in all)
            {
                if (b != null && b.name == fallbackName)
                    return b;
            }
            return null;
        }

        private void CacheBotsButtonLabel()
        {
            if (_bots == null) return;

            _botsLabelTmp = _bots.GetComponentInChildren<TMP_Text>(true);
            _botsLabelText = _bots.GetComponentInChildren<Text>(true);
            if (_botsLabelTmp != null && !string.IsNullOrWhiteSpace(_botsLabelTmp.text))
                _botsLabelDefault = _botsLabelTmp.text;
            else if (_botsLabelText != null && !string.IsNullOrWhiteSpace(_botsLabelText.text))
                _botsLabelDefault = _botsLabelText.text;
        }

        private void SetBotsButtonText(string value)
        {
            if (_botsLabelTmp != null) _botsLabelTmp.text = value;
            if (_botsLabelText != null) _botsLabelText.text = value;
        }

        [Serializable]
        private sealed class RaceEnterRpcRequest
        {
            public int session_epoch;
        }

        [Serializable]
        private sealed class RaceResourceLine
        {
            public string resource;
            public int amount;
            public string id;
        }

        [Serializable]
        private sealed class RaceInfoRpcResponse
        {
            public bool ok;
            public string err;
            public int goal_mana;
            public int mana_bonus_every;
            public RaceResourceLine[] entry;
            public RaceResourceLine[] rewards;
        }
    }
}
