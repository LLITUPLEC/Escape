using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class MouseFollowLight2D : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float zWorld = 0f;
        [SerializeField] private bool clampToViewport = true;

        private void Reset()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Update()
        {
            if (targetCamera == null)
                return;

            var screenPos = ReadPointerPosition();
            if (clampToViewport)
            {
                screenPos.x = Mathf.Clamp(screenPos.x, 0f, Screen.width);
                screenPos.y = Mathf.Clamp(screenPos.y, 0f, Screen.height);
            }

            var world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, GetDepthForCamera(targetCamera)));
            transform.position = new Vector3(world.x, world.y, zWorld);
        }

        private static float GetDepthForCamera(Camera cam)
        {
            // Для ортографической камеры depth можно брать как расстояние до плоскости z=0.
            return Mathf.Abs(cam.transform.position.z);
        }

        private static Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();
            if (Touchscreen.current != null)
                return Touchscreen.current.primaryTouch.position.ReadValue();
#endif
            return Input.mousePosition;
        }
    }
}

