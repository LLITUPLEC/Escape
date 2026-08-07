using Project.Match3;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class ArenaMenuModePanelBinder : MonoBehaviour
    {
        [Header("Paths (relative to scene)")]
        [SerializeField] private string match3ButtonPath = "ArenaMenuWorld/Background2D/ModePanel/match3Button";
        [Tooltip("Опционально. Если пусто — ищется кнопка с именем match3ProButton. §14 PvP Pro.")]
        [SerializeField] private string match3ProButtonPath = "";
        [SerializeField] private string botsButtonPath = "ArenaMenuWorld/Background2D/ModePanel/BotsButton";
        [SerializeField] private string backButtonPath = "ArenaMenuWorld/Background2D/BackButton";

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string match3SceneName = "DuelMatch3";
        [SerializeField] private bool hideBotsButton = true;

        private Button _match3;
        private Button _match3Pro;
        private Button _bots;
        private Button _back;
        private Text _botsLabelText;
        private TMP_Text _botsLabelTmp;
        private string _botsLabelDefault = "Боты";
        private bool _botsBusy;

        private void Awake()
        {
            _match3 = FindButton(match3ButtonPath, "match3Button");
            _match3Pro = string.IsNullOrWhiteSpace(match3ProButtonPath)
                ? FindButton("", "match3ProButton")
                : FindButton(match3ProButtonPath, "match3ProButton");
            _bots = FindButton(botsButtonPath, "BotsButton");
            _back = FindButton(backButtonPath, "BackButton");
            CacheBotsButtonLabel();

            if (hideBotsButton && _bots != null)
            {
                _bots.gameObject.SetActive(false);
                _bots = null;
            }

            if (_match3 != null) _match3.onClick.AddListener(GoMatch3);
            if (_match3Pro != null) _match3Pro.onClick.AddListener(GoMatch3Pro);
            if (_bots != null) _bots.onClick.AddListener(GoBots);
            if (_back != null) _back.onClick.AddListener(BackToMainMenu);
        }

        private void OnDisable()
        {
            _botsBusy = false;
            if (_bots != null)
                _bots.interactable = true;
            SetBotsButtonText(_botsLabelDefault);
        }

        private void OnDestroy()
        {
            if (_match3 != null) _match3.onClick.RemoveListener(GoMatch3);
            if (_match3Pro != null) _match3Pro.onClick.RemoveListener(GoMatch3Pro);
            if (_bots != null) _bots.onClick.RemoveListener(GoBots);
            if (_back != null) _back.onClick.RemoveListener(BackToMainMenu);
        }

        private void GoMatch3()
        {
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(false);
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            if (string.IsNullOrWhiteSpace(match3SceneName)) return;
            SceneManager.LoadScene(match3SceneName);
        }

        private void GoMatch3Pro()
        {
            Match3LaunchContext.SetPvpProForNextMultiplayerMatch(true);
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            if (string.IsNullOrWhiteSpace(match3SceneName)) return;
            SceneManager.LoadScene(match3SceneName);
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

            // fallback by name
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

    }
}
