using System;
using Project.Achievements;
using Project.Leaderboard;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Порядок слоёв HUD главного меню: chrome-кнопки ниже модальных панелей.</summary>
    public static class MainMenuHudLayering
    {
        private static readonly string[] ChromeDirectChildNames =
        {
            "SettingsButton",
            "MineButton",
            UiSteamGlowFx.FxRootName,
            "RatingButton",
        };

        private static readonly string[] ModalRootNames =
        {
            AchievementsPanelController.PanelRootName,
            LeaderboardPanelController.PanelRootName,
        };

        public static void NormalizeHudOverlayOrder(Transform hudRoot)
        {
            if (hudRoot == null)
                return;

            var chromeIdx = 0;
            foreach (var name in ChromeDirectChildNames)
            {
                var chrome = FindDirectChild(hudRoot, name);
                if (chrome != null)
                    chrome.SetSiblingIndex(chromeIdx++);
            }

            foreach (var modalName in ModalRootNames)
            {
                var modal = FindDirectChild(hudRoot, modalName);
                if (modal != null)
                    modal.SetAsLastSibling();
            }
        }

        public static void BringPanelToFront(Transform panelRoot)
        {
            if (panelRoot == null)
                return;

            var hudRoot = panelRoot.parent;
            if (hudRoot != null)
                NormalizeHudOverlayOrder(hudRoot);

            panelRoot.SetAsLastSibling();
            EnsurePanelSubModalsOnTop(panelRoot);
        }

        public static void EnsurePanelSubModalsOnTop(Transform panelRoot)
        {
            if (panelRoot == null)
                return;

            var picker = panelRoot.Find("LeaderboardFilterPicker");
            if (picker != null)
                picker.SetAsLastSibling();
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }
    }
}
