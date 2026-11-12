using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    internal sealed class AchievementAlbumGrid : GridLayoutGroup
    {
        internal const float MedalHeight = 228f;
        internal float CellHeight { get; set; } = MedalHeight;

        public override void SetLayoutHorizontal()
        {
            var width = rectTransform.rect.width - padding.horizontal - spacing.x * (constraintCount - 1);
            m_CellSize = new Vector2(Mathf.Max(1f, width / constraintCount), CellHeight);
            base.SetLayoutHorizontal();
        }
    }
}
