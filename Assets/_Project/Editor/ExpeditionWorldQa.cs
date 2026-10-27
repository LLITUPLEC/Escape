using System;
using System.Linq;
using Project.UI;
using Project.Leaderboard;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    public static class ExpeditionWorldQa
    {
        private static GameObject[] _podiumCards;
        private static bool[] _podiumActive;
        private static TMP_Text[] _podiumLabels;
        private static string[] _podiumText;
        public static string Run(string target)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play required");
            Canvas.ForceUpdateCanvases();
            if (target == "tournament-hidden" || target == "tournament-restored")
            {
                var scene = SceneManager.GetActiveScene();
                var body = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Single(r => r.name == "ExpeditionScenePageHost" && r.gameObject.scene == scene);
                var hidden = target == "tournament-hidden";
                var bridge = UnityEngine.Object.FindObjectsByType<ArenaMatch8Bridge>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Single(b => b.gameObject.scene == scene);
                if (!bridge.gameObject.activeInHierarchy) throw new InvalidOperationException("Tournament menu hiding disabled the live arena binder");
                if (body.gameObject.activeSelf == hidden) throw new InvalidOperationException(hidden ? "Arena menu remains behind tournament" : "Arena menu did not restore after tournament");
                foreach (var name in new[] { "ExpeditionNavigation", "ExpeditionResourceHeader" })
                    if (GameObject.Find(name) == null) throw new InvalidOperationException("Tournament hides shared chrome: " + name);
                for (var i = 0; i < 5; i++)
                    if (GameObject.Find("ExpeditionNav" + i) == null) throw new InvalidOperationException("Tournament hides navigation button " + i);
                return hidden ? "Arena menu hidden behind tournament; header and all five navigation buttons remain visible" : "Arena menu restored after tournament disable/destroy; header/navigation remain visible";
            }
            if (target == "podium-restore")
            {
                if (_podiumCards != null)
                {
                    for (var i = 0; i < _podiumCards.Length; i++) if (_podiumCards[i] != null) _podiumCards[i].SetActive(_podiumActive[i]);
                    for (var i = 0; i < _podiumLabels.Length; i++) if (_podiumLabels[i] != null) _podiumLabels[i].text = _podiumText[i];
                }
                _podiumCards = null;
                return "Original podium visibility and labels restored; no leaderboard data or RPC changed";
            }
            if (target == "podium")
            {
                var controller = UnityEngine.Object.FindObjectsByType<LeaderboardPanelController>(FindObjectsSortMode.None).Single(c => c.IsVisible);
                var adapter = controller.GetComponent<ExpeditionMetaPanels>();
                if (adapter == null) throw new InvalidOperationException("Open rating page");
                _podiumCards = (GameObject[])typeof(ExpeditionMetaPanels).GetField("_podiumCards", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
                _podiumActive = _podiumCards.Select(g => g.activeSelf).ToArray();
                _podiumLabels = _podiumCards.SelectMany(g => g.GetComponentsInChildren<TMP_Text>(true)).ToArray();
                _podiumText = _podiumLabels.Select(t => t.text).ToArray();
                ExpeditionMetaPanels.BindLeaderboardPodium(adapter.transform, new[] {
                    new LeaderboardEntryDto {rank=1,nickname="Искатель",score=24},
                    new LeaderboardEntryDto {rank=2,nickname="Кремень",score=18},
                    new LeaderboardEntryDto {rank=3,nickname="Факел",score=12}});
                Canvas.ForceUpdateCanvases();
                var panorama = ExpeditionUi.Find(adapter.transform, "ExpeditionPagePanorama/Illustration");
                if (panorama == null) panorama = ExpeditionUi.Find(adapter.transform, "ExpeditionPagePanorama")?.Find("Illustration");
                var source = panorama as RectTransform;
                var uv = new[] { new Vector2(.50f,.47f),new Vector2(.27f,.43f),new Vector2(.73f,.426f) };
                for (var i = 0; i < 3; i++)
                {
                    var portrait = _podiumCards[i].transform.Find("Portrait").GetComponent<Image>();
                    var r = portrait.rectTransform.rect;
                    var height = Mathf.Min(r.height, r.width * portrait.sprite.rect.height / portrait.sprite.rect.width);
                    var feet = portrait.rectTransform.TransformPoint(new Vector2(r.center.x, r.center.y - height * .482f));
                    var ground = source.TransformPoint(Vector2.Scale(uv[i] - source.pivot, source.rect.size));
                    if (Mathf.Abs(feet.y - ground.y) > source.rect.height * .065f) throw new InvalidOperationException("Podium feet do not meet illustrated platform " + (i+1));
                }
                return "Three UI-only podium figures meet the actual illustrated platform surfaces; call podium-restore after capture";
            }
            if (target == "battle")
            {
                var background = GameObject.Find("ExpeditionBattleBackdrop")?.GetComponent<Image>();
                if (background == null || background.sprite == null || background.sprite.name != "battle-world-v5") throw new InvalidOperationException("Wrong battle world");
                CoversScreen(background.rectTransform);
                if (GameObject.Find("ExpeditionCombatantStage") != null || GameObject.Find("CombatBoardTransition") != null) throw new InvalidOperationException("Clipped battle scene remains");
                return "Single portrait battle world covers physical screen, no second scene or transition band";
            }
            var world = UnityEngine.Object.FindObjectsByType<ExpeditionSceneWorld>(FindObjectsSortMode.None).Single();
            if (target != "chrome" && world.Art != target) throw new InvalidOperationException("World: expected " + target + ", got " + world.Art);
            CoversScreen(world.Illustration.rectTransform);
            var worldCanvas = world.GetComponent<Canvas>();
            if (worldCanvas == null || worldCanvas.renderMode != RenderMode.ScreenSpaceOverlay || worldCanvas.sortingOrder != 5)
                throw new InvalidOperationException("World must draw above legacy background at sorting order5");
            var activeScene = SceneManager.GetActiveScene();
            foreach (var legacy in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (legacy.gameObject.scene != activeScene || legacy == worldCanvas) continue;
                if ((legacy.name == "MainMenuCanvas" || legacy.name == "Background2D" || (activeScene.name == "MainMenu" && legacy.name == "Canvas"))
                    && worldCanvas.sortingOrder <= legacy.sortingOrder)
                    throw new InvalidOperationException("Legacy main-menu canvas covers world: " + legacy.name + " order=" + legacy.sortingOrder);
            }
            if (activeScene.name == "MineScene" || activeScene.name == "WorkshopScene")
            {
                var backgroundName = activeScene.name == "MineScene" ? "MineBackground" : "WorkshopBackground";
                var legacyBackground = UnityEngine.Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Single(i => i.name == backgroundName && i.gameObject.scene == activeScene);
                var legacyCanvas = legacyBackground.GetComponentInParent<Canvas>();
                var headerCanvas = GameObject.Find("ExpeditionResourceHeader")?.GetComponentInParent<Canvas>();
                if (legacyCanvas == null || headerCanvas == null || legacyCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    || legacyCanvas.sortingOrder != 20 || legacyCanvas.sortingOrder <= worldCanvas.sortingOrder || legacyCanvas.sortingOrder >= headerCanvas.sortingOrder)
                    throw new InvalidOperationException("Legacy scene UI must draw above world5 and below header70 at order20");
            }
            foreach (var buttonName in new[] { "ExpeditionOpenSupplies", "ExpeditionOpenSettings" })
            {
                var button = GameObject.Find(buttonName)?.GetComponent<Button>();
                if (button == null || button.targetGraphic == button.GetComponent<Image>() || button.GetComponent<Image>().color.a > 0) throw new InvalidOperationException("Header action still framed");
            }
            foreach (var button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.name.StartsWith("ExpeditionNav")))
            {
                var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (label == null) throw new InvalidOperationException("Nav label missing");
                label.ForceMeshUpdate();
                if (label.isTextOverflowing || label.textBounds.size.x > label.rectTransform.rect.width + 1) throw new InvalidOperationException("Navigation word does not fit: " + label.text);
            }
            if (target == "character-backdrop")
            {
                if (GameObject.Find("ExpeditionCharacterIdentity") != null) throw new InvalidOperationException("Duplicate hero nickname visible");
                foreach (var name in new[] { "ExpeditionCharacterWorld", "ExpeditionStageBackdrop" })
                {
                    var image = GameObject.Find(name)?.GetComponent<Image>();
                    if (image != null && image.enabled) throw new InvalidOperationException("Local character background remains");
                }
            }
            return "Full-screen world " + world.Art + "; borderless header actions and all five navigation words fit";
        }

        private static void CoversScreen(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            if (min.x > 1 || min.y > 1 || max.x < Screen.width - 1 || max.y < Screen.height - 1) throw new InvalidOperationException("World does not cover screen: " + min + " to " + max);
        }
    }
}
