using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Project.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class SceneLoadButton : MonoBehaviour
    {
        [SerializeField] private string sceneName = "MainMenu";

        private void Awake()
        {
            EnsureEventSystemExists();
            var btn = GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(Load);
        }

        private void OnDestroy()
        {
            var btn = GetComponent<Button>();
            if (btn != null)
                btn.onClick.RemoveListener(Load);
        }

        private void Load()
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return;
            SceneManager.LoadScene(sceneName);
        }

        private static void EnsureEventSystemExists()
        {
            var es = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (es != null)
            {
                EnsureCompatibleInputModule(es);
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var newEs = go.GetComponent<EventSystem>();
            EnsureCompatibleInputModule(newEs);
            DontDestroyOnLoad(go);
        }

        private static void EnsureCompatibleInputModule(EventSystem es)
        {
            if (es == null) return;
#if ENABLE_INPUT_SYSTEM
            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null)
                Object.Destroy(standalone);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            if (es.GetComponent<StandaloneInputModule>() == null)
                es.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}

