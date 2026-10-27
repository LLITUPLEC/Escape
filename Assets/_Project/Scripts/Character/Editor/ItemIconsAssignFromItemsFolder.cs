using UnityEditor;

namespace Project.Character.Editor
{
    /// <summary>Legacy menu alias. Dedicated equipment and existing recipe/material art are preserved.</summary>
    public static class ItemIconsAssignFromItemsFolder
    {
        [MenuItem("Tools/Character/Назначить иконки из img/items (зелёные + рецепты)")]
        public static void Assign()
        {
            Project.Editor.ExpeditionItemMediaImporter.Prepare();
        }
    }
}