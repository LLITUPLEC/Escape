using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class SceneLoadButton : MonoBehaviour
    {
        [SerializeField] private string sceneName = "MainMenu";

        private void Awake()
        {
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
    }
}

