using UnityEngine;

namespace Project.Match3
{
    [CreateAssetMenu(menuName = "Project/Match3/Monster Frame Catalog", fileName = "MonsterFrameCatalog")]
    public sealed class MonsterFrameCatalog : ScriptableObject
    {
        [SerializeField] private Sprite frameEasy;
        [SerializeField] private Sprite frameMedium;
        [SerializeField] private Sprite frameHard;
        [SerializeField] private Sprite frameBoss;

        public Sprite GetFrame(string difficulty, bool isBoss)
        {
            if (isBoss && frameBoss != null)
                return frameBoss;

            switch (difficulty)
            {
                case "medium":
                    return frameMedium != null ? frameMedium : frameEasy;
                case "hard":
                    return frameHard != null ? frameHard : frameMedium != null ? frameMedium : frameEasy;
                default:
                    return frameEasy;
            }
        }
    }
}
