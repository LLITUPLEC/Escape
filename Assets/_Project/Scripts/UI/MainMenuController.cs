using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button duelButton;
        [SerializeField] private Button botsButton;

        public void Bind(Button duel, Button bots)
        {
            duelButton = duel;
            botsButton = bots;
        }

        private void Awake()
        {
            if (duelButton != null) duelButton.onClick.AddListener(OnDuelClicked);
            if (botsButton != null) botsButton.onClick.AddListener(OnBotsClicked);
        }

        private void OnDuelClicked()
        {
            SceneManager.LoadScene("DuelRoom");
        }

        private void OnBotsClicked()
        {
            Debug.Log("Режим 'Боты' пока заглушка.");
        }
    }
}

