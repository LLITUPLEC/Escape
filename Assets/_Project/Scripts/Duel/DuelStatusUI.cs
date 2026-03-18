using UnityEngine;
using UnityEngine.UI;

namespace Project.Duel
{
    public sealed class DuelStatusUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Text label;

        public void Bind(CanvasGroup g, Text t)
        {
            group = g;
            label = t;
        }

        private void Awake()
        {
            if (group == null) group = GetComponentInChildren<CanvasGroup>();
            if (label == null) label = GetComponentInChildren<Text>();
        }

        public void SetVisible(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        public void SetText(string text)
        {
            if (label == null) return;
            label.text = text;
        }
    }
}

