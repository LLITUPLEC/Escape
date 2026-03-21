using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace Project.Match3
{
    /// <summary>Top HUD: whose turn it is and the countdown timer.</summary>
    public sealed class Match3GameHUD : MonoBehaviour
    {
        [SerializeField] public Text turnText;
        [SerializeField] public Text timerText;
        [SerializeField] public Text extraTurnText;
        private Coroutine _extraTurnRoutine;

        private void Awake()
        {
            if (extraTurnText != null) return;

            var go = new GameObject("ExtraTurnText");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0f, -0.35f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            extraTurnText = go.AddComponent<Text>();
            extraTurnText.font = turnText != null ? turnText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            extraTurnText.fontSize = 22;
            extraTurnText.alignment = TextAnchor.MiddleCenter;
            extraTurnText.text = string.Empty;
            extraTurnText.gameObject.SetActive(false);
        }

        public void SetTurn(string text)
        {
            if (turnText != null) turnText.text = text;
        }

        public void SetTimer(string text)
        {
            if (timerText != null) timerText.text = text;
        }

        public void ShowExtraTurnMessage(string message, Color color, float duration)
        {
            if (extraTurnText == null) return;
            if (_extraTurnRoutine != null) StopCoroutine(_extraTurnRoutine);
            _extraTurnRoutine = StartCoroutine(ShowExtraTurnRoutine(message, color, duration));
        }

        private IEnumerator ShowExtraTurnRoutine(string message, Color color, float duration)
        {
            extraTurnText.text = message;
            extraTurnText.color = color;
            extraTurnText.gameObject.SetActive(true);
            yield return new WaitForSeconds(Mathf.Max(0.2f, duration));
            extraTurnText.gameObject.SetActive(false);
            _extraTurnRoutine = null;
        }
    }
}
