using UnityEngine;
using TMPro;
using System.Collections;

namespace Project.Match3
{
    /// <summary>Top HUD: whose turn it is and the countdown timer.</summary>
    public sealed class Match3GameHUD : MonoBehaviour
    {
        [SerializeField] public TMP_Text turnText;
        [SerializeField] public TMP_Text timerText;
        [SerializeField] public TMP_Text extraTurnText;
        [SerializeField] public TMP_Text affixIconText;
        [SerializeField] public TMP_Text affixEffectText;
        private Coroutine _extraTurnRoutine;

        private void Awake()
        {
            ResolveReferences();
            if (extraTurnText == null)
            {
                var go = new GameObject("ExtraTurnText");
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(transform, false);
                rt.anchorMin = new Vector2(0f, -0.35f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;

                extraTurnText = go.AddComponent<TextMeshProUGUI>();
                extraTurnText.font = TMP_Settings.defaultFontAsset;
                extraTurnText.fontSize = 22;
                extraTurnText.alignment = TextAlignmentOptions.Center;
                extraTurnText.text = string.Empty;
                extraTurnText.gameObject.SetActive(false);
            }

            if (affixIconText == null)
            {
                var iconGo = new GameObject("AffixIconText");
                var iconRt = iconGo.AddComponent<RectTransform>();
                iconRt.SetParent(transform, false);
                iconRt.anchorMin = new Vector2(0.72f, 0.0f);
                iconRt.anchorMax = new Vector2(0.80f, 1.0f);
                iconRt.offsetMin = new Vector2(2f, 2f);
                iconRt.offsetMax = new Vector2(-2f, -2f);
                affixIconText = iconGo.AddComponent<TextMeshProUGUI>();
                affixIconText.font = TMP_Settings.defaultFontAsset;
                affixIconText.fontSize = 20;
                affixIconText.alignment = TextAlignmentOptions.Center;
                affixIconText.text = string.Empty;
            }
            if (affixEffectText == null)
            {
                var txtGo = new GameObject("AffixEffectText");
                var txtRt = txtGo.AddComponent<RectTransform>();
                txtRt.SetParent(transform, false);
                txtRt.anchorMin = new Vector2(0.80f, 0.0f);
                txtRt.anchorMax = new Vector2(1.0f, 1.0f);
                txtRt.offsetMin = new Vector2(0f, 2f);
                txtRt.offsetMax = new Vector2(0f, -2f);
                affixEffectText = txtGo.AddComponent<TextMeshProUGUI>();
                affixEffectText.font = TMP_Settings.defaultFontAsset;
                affixEffectText.fontSize = 12;
                affixEffectText.alignment = TextAlignmentOptions.Left;
                affixEffectText.text = string.Empty;
            }
        }

        private void ResolveReferences()
        {
            turnText ??= transform.Find("TurnText")?.GetComponent<TMP_Text>();
            timerText ??= transform.Find("TimerText")?.GetComponent<TMP_Text>();
            affixIconText ??= transform.Find("AffixIconText")?.GetComponent<TMP_Text>();
            affixEffectText ??= transform.Find("AffixEffectText")?.GetComponent<TMP_Text>();
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

        public void SetAffixInfo(string iconText, string effectText)
        {
            if (affixIconText != null)
                affixIconText.text = string.IsNullOrWhiteSpace(iconText) ? string.Empty : iconText;
            if (affixEffectText != null)
                affixEffectText.text = string.IsNullOrWhiteSpace(effectText) ? string.Empty : effectText;
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
