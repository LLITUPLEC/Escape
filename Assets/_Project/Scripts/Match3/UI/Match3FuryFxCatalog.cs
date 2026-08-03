using UnityEngine;

namespace Project.Match3
{
    /// <summary>
    /// Каталог спрайтов огня для UI-эффекта «Ярость».
    /// Лежит в Resources/Match3/FuryFxCatalog.
    /// </summary>
    [CreateAssetMenu(menuName = "Escape/Match3/Fury Fx Catalog", fileName = "FuryFxCatalog")]
    public sealed class Match3FuryFxCatalog : ScriptableObject
    {
        public Sprite[] flameFrames;
    }
}
