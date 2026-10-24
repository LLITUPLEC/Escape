using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Scene-loaded runs before the first render; legacy prefabs never flash while their adapters settle.</summary>
    public static class ExpeditionStartupCover
    {
        public static GameObject Show(Scene scene)
        {
            var root = new GameObject("ExpeditionStartupCover", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4500; // Authentication/session UI retains its higher independent ordering.
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            ExpeditionUi.Fill(root.transform, "OpaqueCover", 0, 0, 1, 1, new Color32(7, 20, 24, 255)).raycastTarget = true;
            var image = ExpeditionUi.Rect(root.transform, "CurrentWorld", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            var art = scene.name == "MainMenu" ? "hub-backdrop" : scene.name == "MineScene" ? "mine-backdrop" :
                scene.name == "WorkshopScene" ? "workshop-backdrop" : scene.name == "DuelMatch3" ? "battle-world-v4" : "arena-backdrop";
            image.sprite = Resources.Load<Sprite>("Expedition/Art/" + art);
            image.raycastTarget = false;
            if (image.sprite != null)
            {
                var fit = image.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
            }
            return root;
        }
    }
}
