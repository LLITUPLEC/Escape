using UnityEngine;

namespace Project.Match3
{
    [CreateAssetMenu(menuName = "Project/Match3/Monster Definition", fileName = "MonsterDefinition")]
    public sealed class MonsterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string botId = "mine_1";
        [SerializeField] private string displayName = "Монстр";
        [SerializeField] private int floor = 1;

        [Header("Visual")]
        [SerializeField] private Sprite icon;
        [SerializeField] private Sprite frameEasy;
        [SerializeField] private Sprite frameMedium;
        [SerializeField] private Sprite frameHard;
        [SerializeField] private Sprite frameBoss;

        public string BotId => botId;
        public string DisplayName => displayName;
        public int Floor => floor;
        public Sprite Icon => icon;

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
