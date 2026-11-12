using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    internal static class ExpeditionShelterSurface
    {
        internal static readonly Color32 Brass = new Color32(162, 112, 65, 255);
        internal static readonly Color32 Iron = new Color32(12, 24, 26, 255);
        internal static readonly Color32 Timber = new Color32(39, 27, 20, 255);

        internal static RectTransform Illustration(Transform parent, string name, string resource,
            float x0, float y0, float x1, float y1)
        {
            var aperture = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1);
            aperture.gameObject.AddComponent<RectMask2D>();
            var art = ExpeditionUi.Fill(aperture, "Painting", 0, 0, 1, 1, Iron);
            art.sprite = Resources.Load<Sprite>("Expedition/Art/" + resource);
            if (art.sprite == null) return aperture;
            art.color = Color.white;
            var aspect = art.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = art.sprite.rect.width / art.sprite.rect.height;
            return aperture;
        }

        internal static RectTransform Inset(Transform parent, string name, Color surface,
            float x0, float y0, float x1, float y1)
        {
            var frame = ExpeditionUi.Fill(parent, name, x0, y0, x1, y1, Brass).rectTransform;
            ExpeditionUi.Fill(frame, "Recess", 0, 0, 1, 1, surface).rectTransform.sizeDelta = new Vector2(-4, -4);
            return frame;
        }
    }
}
