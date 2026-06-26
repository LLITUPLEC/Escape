using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Leaderboard
{
    /// <summary>Кнопка-фильтр с модальным списком вариантов.</summary>
    public sealed class LeaderboardFilterButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private string headerLabel = "TYPE";

        private Action<IReadOnlyList<LeaderboardViewOption>, string, Action<string>> _openPicker;
        private LeaderboardType _type;
        private string _currentId;
        private IReadOnlyList<LeaderboardViewOption> _staticOptions;
        private bool _useStaticOptions;
        private Func<string, string> _labelResolver;

        public event Action<string> OnSelectionChanged;

        public string CurrentId => _currentId;

        private void Awake()
        {
            if (button == null)
                button = GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(OpenPicker);
        }

        private void OnDestroy()
        {
            if (button != null)
                button.onClick.RemoveListener(OpenPicker);
        }

        public void ConfigurePickerHost(Action<IReadOnlyList<LeaderboardViewOption>, string, Action<string>> openPicker)
        {
            _openPicker = openPicker;
        }

        public void SetHeader(string header)
        {
            headerLabel = header;
            if (labelText != null)
                labelText.text = header;
        }

        public void ConfigureStaticOptions(
            IReadOnlyList<LeaderboardViewOption> options,
            string selectedId,
            Func<string, string> labelResolver)
        {
            _useStaticOptions = true;
            _staticOptions = options;
            _labelResolver = labelResolver;
            _currentId = selectedId;
            RefreshValueLabel();
        }

        public void SetTypeContext(LeaderboardType type, string viewId)
        {
            _useStaticOptions = false;
            _type = type;
            if (string.IsNullOrWhiteSpace(viewId) || !LeaderboardFilterCatalog.IsValidView(type, viewId))
                viewId = LeaderboardFilterCatalog.DefaultView(type).Id;
            _currentId = viewId;
            _labelResolver = id => LeaderboardFilterCatalog.ViewLabel(_type, id);
            RefreshValueLabel();
        }

        public void SetSelection(string id)
        {
            _currentId = id;
            RefreshValueLabel();
        }

        private void RefreshValueLabel()
        {
            if (valueText == null)
                return;
            if (_labelResolver != null)
                valueText.text = _labelResolver(_currentId);
            else
                valueText.text = _currentId;
        }

        private void OpenPicker()
        {
            var options = _useStaticOptions
                ? _staticOptions
                : LeaderboardFilterCatalog.ViewsForType(_type);
            _openPicker?.Invoke(options, _currentId, id =>
            {
                if (string.IsNullOrWhiteSpace(id))
                    return;
                _currentId = id;
                RefreshValueLabel();
                OnSelectionChanged?.Invoke(_currentId);
            });
        }
    }
}
