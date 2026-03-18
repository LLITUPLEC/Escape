using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Duel
{
    public sealed class DuelHudController : MonoBehaviour
    {
        [SerializeField] private Button exitButton;
        [SerializeField] private CanvasGroup confirmGroup;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;
        [SerializeField] private CanvasGroup bannerGroup;
        [SerializeField] private Text bannerText;

        private DuelRoomManager _room;

        public void Bind(
            DuelRoomManager room,
            Button exit,
            CanvasGroup confirm,
            Button yes,
            Button no,
            CanvasGroup banner,
            Text bannerLabel)
        {
            _room = room;
            exitButton = exit;
            confirmGroup = confirm;
            confirmYesButton = yes;
            confirmNoButton = no;
            bannerGroup = banner;
            bannerText = bannerLabel;
        }

        private void Awake()
        {
            if (_room == null)
            {
                _room = FindAnyObjectByType<DuelRoomManager>();
            }

            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);

            SetConfirmVisible(false);
            SetBannerVisible(false);
        }

        private void OnExitClicked()
        {
            Debug.Log("[DuelHUD] Exit clicked");
            SetConfirmVisible(true);
        }

        private void OnConfirmYes()
        {
            Debug.Log("[DuelHUD] Confirm YES");
            SetConfirmVisible(false);
            if (_room == null)
            {
                Debug.LogWarning("[DuelHUD] DuelRoomManager not found; cannot quit.");
                return;
            }
            _room.QuitMatchAndReturnToMenu();
        }

        private void OnConfirmNo()
        {
            Debug.Log("[DuelHUD] Confirm NO");
            SetConfirmVisible(false);
        }

        public void ShowBanner(string text)
        {
            if (bannerText != null) bannerText.text = text ?? string.Empty;
            SetBannerVisible(true);
        }

        private void SetConfirmVisible(bool visible)
        {
            if (confirmGroup == null) return;
            confirmGroup.alpha = visible ? 1f : 0f;
            confirmGroup.interactable = visible;
            confirmGroup.blocksRaycasts = visible;
        }

        private void SetBannerVisible(bool visible)
        {
            if (bannerGroup == null) return;
            bannerGroup.alpha = visible ? 1f : 0f;
            bannerGroup.interactable = visible;
            bannerGroup.blocksRaycasts = visible;
        }
    }
}

