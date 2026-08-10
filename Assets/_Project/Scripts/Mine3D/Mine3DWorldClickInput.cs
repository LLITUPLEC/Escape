using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Mine3D
{
    /// <summary>
    /// Вешает PhysicsRaycaster на камеру — клики по 3D-капсулам идут в Mine3DFloorInteractable.
    /// </summary>
    public sealed class Mine3DWorldClickInput : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;

        private void Awake()
        {
            EnsureCameraAndRaycaster();
        }

        private void Start()
        {
            EnsureCameraAndRaycaster();
        }

        private void EnsureCameraAndRaycaster()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (worldCamera == null) return;
            if (worldCamera.GetComponent<PhysicsRaycaster>() == null)
                worldCamera.gameObject.AddComponent<PhysicsRaycaster>();
        }
    }
}
