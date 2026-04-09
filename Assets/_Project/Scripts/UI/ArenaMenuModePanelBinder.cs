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
        [SerializeField] private string duelButtonPath = "ArenaMenuWorld/Background2D/ModePanel/DuelButton";
        [SerializeField] private string match3ButtonPath = "ArenaMenuWorld/Background2D/ModePanel/match3Button";
        [SerializeField] private string botsButtonPath = "ArenaMenuWorld/Background2D/ModePanel/BotsButton";
        [SerializeField] private string backButtonPath = "ArenaMenuWorld/Background2D/BackButton";

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string duelSceneName = "DuelRoom";
        [SerializeField] private string match3SceneName = "DuelMatch3";
        [SerializeField] private bool hideBotsButton = true;

        private Button _duel;
        private Button _match3;
        private Button _bots;
        private Button _back;
        private Text _botsLabelText;
        private TMP_Text _botsLabelTmp;
        private string _botsLabelDefault = "Боты";
        private bool _botsBusy;

        private void Awake()
        {
            _duel = FindButton(duelButtonPath, "DuelButton");
            _match3 = FindButton(match3ButtonPath, "match3Button");
            _bots = FindButton(botsButtonPath, "BotsButton");
            _back = FindButton(backButtonPath, "BackButton");
            CacheBotsButtonLabel();

            if (hideBotsButton && _bots != null)
            {
                _bots.gameObject.SetActive(false);
                _bots = null;
            }

            if (_duel != null) _duel.onClick.AddListener(GoDuel);
            if (_match3 != null) _match3.onClick.AddListener(GoMatch3);
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
            if (_duel != null) _duel.onClick.RemoveListener(GoDuel);
            if (_match3 != null) _match3.onClick.RemoveListener(GoMatch3);
            if (_bots != null) _bots.onClick.RemoveListener(GoBots);
            if (_back != null) _back.onClick.RemoveListener(BackToMainMenu);
        }

        private void GoDuel()
        {
            if (string.IsNullOrWhiteSpace(duelSceneName)) return;
            SceneManager.LoadScene(duelSceneName);
        }

        private void GoMatch3()
        {
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
            var go = GameObject.Find(fullPath);
            if (go != null)
                return go.GetComponent<Button>();

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

