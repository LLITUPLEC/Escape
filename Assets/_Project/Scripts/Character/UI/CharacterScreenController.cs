using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class CharacterScreenController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private CharacterScreenView view;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text levelText;

        [Header("Start")]
        [SerializeField] private bool startHidden = true;

        private CancellationTokenSource _cts;
        private bool _visible;

        private void Awake()
        {
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            _cts = new CancellationTokenSource();
            if (startHidden) SetVisible(false);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void Toggle()
        {
            if (_visible) Close();
            else Open();
        }

        public void Open()
        {
            _ = OpenAsync();
        }

        public void Close()
        {
            SetVisible(false);
        }

        /// <summary>Например, когда открытие перенесено на BottomHeaderLogo в главном меню.</summary>
        public void HideOpenButton()
        {
            if (openButton != null) openButton.gameObject.SetActive(false);
        }

        private async Task OpenAsync()
        {
            SetVisible(true);
            if (view != null) view.EnsureBuilt();

            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            var profile = await CharacterProfileService.GetAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            await RunOnMainThreadAsync(() =>
            {
                if (!_visible) return;
                if (profile == null || !profile.ok) return;

                if (levelText != null && profile.progression != null)
                    levelText.text = profile.progression.level.ToString();

                if (view != null && profile.stats != null)
                    view.SetStats(profile.stats.hp, profile.stats.damage, profile.stats.armor, profile.stats.healing, profile.stats.crit_chance);
            });
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (view != null) view.SetVisible(visible);
            else gameObject.SetActive(visible);
        }

        private static Task RunOnMainThreadAsync(System.Action a)
        {
            // В проекте уже есть MainThreadDispatcher; используем мягкую зависимость.
            try
            {
                var t = Project.Utils.MainThreadDispatcher.RunAsync(() =>
                {
                    a?.Invoke();
                    return true;
                });
                return t;
            }
            catch
            {
                a?.Invoke();
                return Task.CompletedTask;
            }
        }
    }
}

