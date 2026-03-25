using Project.Match3;
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

        private Button _duel;
        private Button _match3;
        private Button _bots;
        private Button _back;

        private void Awake()
        {
            _duel = FindButton(duelButtonPath, "DuelButton");
            _match3 = FindButton(match3ButtonPath, "match3Button");
            _bots = FindButton(botsButtonPath, "BotsButton");
            _back = FindButton(backButtonPath, "BackButton");

            if (_duel != null) _duel.onClick.AddListener(GoDuel);
            if (_match3 != null) _match3.onClick.AddListener(GoMatch3);
            if (_bots != null) _bots.onClick.AddListener(GoBots);
            if (_back != null) _back.onClick.AddListener(BackToMainMenu);
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
            Match3LaunchContext.SetMode(Match3LaunchMode.SoloBot);
            if (string.IsNullOrWhiteSpace(match3SceneName)) return;
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
            var all = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in all)
            {
                if (b != null && b.name == fallbackName)
                    return b;
            }
            return null;
        }
    }
}

