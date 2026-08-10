using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Mine3D
{
    /// <summary>
    /// Клик по капсуле / барьеру → MonsterModal (через PhysicsRaycaster + EventSystem).
    /// </summary>
    public sealed class Mine3DFloorInteractable : MonoBehaviour, IPointerClickHandler
    {
        public int Floor;
        public string Difficulty;
        public Mine3DFloorView FloorView;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Floor < 1) return;

            var shaft = FindFirstObjectByType<Mine3DShaftController>(FindObjectsInactive.Include);
            if (shaft != null
                && !string.IsNullOrEmpty(Difficulty)
                && !string.Equals(Difficulty, shaft.CurrentDifficulty, System.StringComparison.OrdinalIgnoreCase))
                return;

            var mine = Project.UI.MineSceneController.EnsureInstalled();
            if (mine == null)
            {
                Debug.LogWarning("[Mine3D] Клик по этажу " + Floor + ", но MineSceneController не установлен.");
                return;
            }

            mine.OpenFloorFromWorld(Floor, Difficulty);
        }
    }
}
