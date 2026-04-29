using UnityEngine;

namespace Project.Achievements
{
    /// <summary>
    /// Вешается на кнопку <c>BottomButtonAchiev</c>: размер фона бейджа, спрайт и цвет.
    /// Без компонента используются значения по умолчанию в <see cref="AchievementsPanelController"/>.
    /// </summary>
    public sealed class BottomButtonAchievementBadgeConfig : MonoBehaviour
    {
        [SerializeField] private Vector2 badgeSpriteSize = new Vector2(44f, 44f);
        [SerializeField] private Sprite badgeSprite;
        [SerializeField] private Color badgeColor = new Color(0.85f, 0.22f, 0.26f, 0.96f);

        public Vector2 BadgeSpriteSize => badgeSpriteSize;
        public Sprite BadgeSprite => badgeSprite;
        public Color BadgeColor => badgeColor;
    }
}
