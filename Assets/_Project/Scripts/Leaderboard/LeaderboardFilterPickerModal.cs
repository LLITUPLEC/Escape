using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Leaderboard
{
    /// <summary>Модальный список вариантов фильтра.</summary>
    public sealed class LeaderboardFilterPickerModal : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Button dimmerButton;
        [SerializeField] private RectTransform listRoot;
        [SerializeField] private Button optionButtonPrefab;
        [SerializeField] private TMP_FontAsset uiFont;

        private readonly List<Button> _spawned = new List<Button>();
        private Action<string> _onPick;

        private void Awake()
        {
            HideImmediate();
            if (dimmerButton != null)
                dimmerButton.onClick.AddListener(Hide);
        }

        private void OnDestroy()
        {
            if (dimmerButton != null)
                dimmerButton.onClick.RemoveListener(Hide);
        }

        public void Show(
            IReadOnlyList<LeaderboardViewOption> options,
            string selectedId,
            Action<string> onPick)
        {
            _onPick = onPick;
            ClearOptions();

            if (options == null || listRoot == null || optionButtonPrefab == null)
                return;

            foreach (var opt in options)
            {
                var btn = Instantiate(optionButtonPrefab, listRoot);
                btn.gameObject.SetActive(true);
                var tmp = btn.GetComponentInChildren<TMP_Text>();
                if (tmp != null)
                {
                    tmp.text = opt.Label;
                    if (uiFont != null)
                        tmp.font = uiFont;
                }

                var captured = opt.Id;
                var isSelected = string.Equals(captured, selectedId, StringComparison.Ordinal);
                var img = btn.GetComponent<Image>();
                if (img != null)
                    img.color = isSelected
                        ? new Color(0.12f, 0.32f, 0.14f, 0.98f)
                        : new Color(0.1f, 0.11f, 0.14f, 0.96f);

                btn.onClick.AddListener(() =>
                {
                    _onPick?.Invoke(captured);
                    Hide();
                });
                _spawned.Add(btn);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            HideImmediate();
            _onPick = null;
        }

        private void HideImmediate()
        {
            ClearOptions();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        private void ClearOptions()
        {
            foreach (var b in _spawned)
            {
                if (b != null)
                    Destroy(b.gameObject);
            }

            _spawned.Clear();
        }
    }
}
