using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Top HUD: whose turn it is and the countdown timer.</summary>
    public sealed class Match3GameHUD : MonoBehaviour
    {
        [SerializeField] public Text turnText;
        [SerializeField] public Text timerText;

        public void SetTurn(string text)
        {
            if (turnText != null) turnText.text = text;
        }

        public void SetTimer(string text)
        {
            if (timerText != null) timerText.text = text;
        }
    }
}
