using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Tutorial
{
    public enum TutorialExitReason { Completed, Skipped }
    /// <summary>Navigation boundary only. The host owns account-scoped persistence through Exited.</summary>
    public static class TutorialLauncher
    {
        public const string SceneName = "Tutorial";
        public static bool ManualReplay { get; private set; }
        public static bool IsRunning { get; private set; }
        public static event Action<bool> Opening;
        public static event Action<TutorialExitReason> Exited;
        public static bool Open(bool manualReplay)
        {
            if (IsRunning) return false;
            if (!Application.CanStreamedLevelBeLoaded(SceneName)) { Debug.LogError("Tutorial scene is missing. Run TutorialSceneTools.Prepare before building."); return false; }
            ManualReplay = manualReplay; IsRunning = true;
            try { Opening?.Invoke(manualReplay); SceneManager.LoadScene(SceneName); return true; }
            catch { IsRunning = false; throw; }
        }
        internal static void MarkEntered() { IsRunning = true; }
        internal static void Exit(TutorialExitReason reason)
        {
            if (!IsRunning) return; IsRunning = false;
            try { Exited?.Invoke(reason); }
            finally { SceneManager.LoadScene("MainMenu"); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { IsRunning = ManualReplay = false; Opening = null; Exited = null; }
    }
}
