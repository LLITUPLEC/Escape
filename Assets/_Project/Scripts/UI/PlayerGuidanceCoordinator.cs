using Project.Character;
using Project.Nakama;
using Project.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.UI
{
    /// <summary>Automatic onboarding is new-account-only. Manual replay is available to every account in Settings.</summary>
    public sealed class PlayerGuidanceCoordinator : MonoBehaviour
    {
        private static PlayerGuidanceCoordinator _instance;
        private string _autoScope, _trainingOwner;
        private float _shellReadyAt = float.PositiveInfinity, _nextTick;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("PlayerGuidanceCoordinator"); _instance = root.AddComponent<PlayerGuidanceCoordinator>();
            DontDestroyOnLoad(root);
        }
        public static void ShelterReady()
        {
            if (_instance != null) _instance._shellReadyAt = Time.realtimeSinceStartup + 1.2f;
            PlayerGuidanceService.RequestRefresh(true);
        }
        private void OnEnable()
        {
            TutorialLauncher.Opening += Opening; TutorialLauncher.Exited += Exited;
            PlayerResourcesService.CachedResourcesChanged += ResourcesChanged;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        private void OnDisable()
        {
            TutorialLauncher.Opening -= Opening; TutorialLauncher.Exited -= Exited;
            PlayerResourcesService.CachedResourcesChanged -= ResourcesChanged;
            SceneManager.sceneLoaded -= SceneLoaded;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) => _shellReadyAt = float.PositiveInfinity;
        private void ResourcesChanged() => PlayerGuidanceService.RequestRefresh(true);
        private void Opening(bool manualReplay) => _trainingOwner = LocalAccountCacheScope.Current;
        private void Exited(TutorialExitReason reason)
        {
            var owner = _trainingOwner; _trainingOwner = null;
            _ = PlayerGuidanceService.RecordTutorialExitAsync(reason == TutorialExitReason.Completed ? "completed" : "skipped", owner);
        }
        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextTick) return;
            _nextTick = Time.realtimeSinceStartup + .5f;
            var scene = SceneManager.GetActiveScene().name;
            if (scene != "MainMenu" && scene != "ArenaMenu" && scene != "WorkshopScene") return;
            PlayerGuidanceService.RequestRefresh();
            if (scene != "MainMenu" || Time.realtimeSinceStartup < _shellReadyAt || ArenaMatch8Bridge.IsParticipationLocked) return;
            var state = PlayerGuidanceService.Current; var scope = LocalAccountCacheScope.Current;
            if (scope == null || scope == _autoScope || state?.tutorial_status != "pending") return;
            var body = GameObject.Find("ExpeditionScenePageHost");
            if (body == null || !body.activeInHierarchy || GameObject.Find("SettingsModal") != null || FindFirstObjectByType<ExpeditionDailyPanel>() != null || FindFirstObjectByType<ExpeditionShopPanel>() != null || FindFirstObjectByType<ExpeditionGlobalBossPanel>() != null || FindFirstObjectByType<ExpeditionMailPanel>() != null) return;
            foreach (var input in FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None)) if (input.isFocused) return;
            if (TutorialLauncher.Open(false)) _autoScope = scope;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _instance = null;
    }
}
