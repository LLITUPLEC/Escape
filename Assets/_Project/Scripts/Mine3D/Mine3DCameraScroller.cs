using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Mine3D
{
    /// <summary>
    /// Камера двигается только по оси Y (вверх/вниз по шахте).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mine3DCameraScroller : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float minY = -48f;
        [SerializeField] private float maxY = 2f;
        [SerializeField] private float dragSensitivity = 0.035f;
        [SerializeField] private float wheelSensitivity = 1.2f;
        [Tooltip("Как у ScrollRect: жест вверх двигает камеру вниз по шахте.")]
        [SerializeField] private bool invertDrag = true;
        [SerializeField] private float smooth = 14f;
        [SerializeField] private float startY = 0f;

        private float _targetY;
        private bool _dragging;
        private Vector2 _lastPointer;

        public float MinY
        {
            get => minY;
            set => minY = value;
        }

        public float MaxY
        {
            get => maxY;
            set => maxY = value;
        }

        public void Configure(Camera cam, float min, float max, float initialY)
        {
            targetCamera = cam;
            minY = min;
            maxY = max;
            startY = initialY;
            _targetY = Mathf.Clamp(initialY, minY, maxY);
            ApplyImmediate();
        }

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
            _targetY = Mathf.Clamp(startY, minY, maxY);
            ApplyImmediate();
        }

        private void Update()
        {
            if (targetCamera == null) return;

            HandleWheel();
            HandleDrag();

            var pos = targetCamera.transform.position;
            var nextY = Mathf.Lerp(pos.y, _targetY, 1f - Mathf.Exp(-smooth * Time.deltaTime));
            targetCamera.transform.position = new Vector3(pos.x, nextY, pos.z);
        }

        private void HandleWheel()
        {
            var scroll = ReadScrollDelta();
            if (Mathf.Abs(scroll) < 0.001f) return;
            // Колесо вверх = контент вверх = камера вниз по шахте.
            var sign = invertDrag ? -1f : 1f;
            _targetY = Mathf.Clamp(_targetY + scroll * wheelSensitivity * sign, minY, maxY);
        }

        private void HandleDrag()
        {
            if (!TryGetPointer(out var pos, out var pressed, out var held))
                return;

            if (pressed)
            {
                if (IsPointerOverUi())
                {
                    _dragging = false;
                    return;
                }

                // Тап по капсуле/барьеру — не начинаем скролл (иначе EventSystem не даст OnPointerClick).
                if (IsPointerOverFloorInteractable(pos))
                {
                    _dragging = false;
                    return;
                }

                _dragging = true;
                _lastPointer = pos;
                return;
            }

            if (_dragging && held)
            {
                var dy = pos.y - _lastPointer.y;
                _lastPointer = pos;
                // Жест вверх (dy > 0) → камера вниз (как скролл ленты).
                var sign = invertDrag ? -1f : 1f;
                _targetY = Mathf.Clamp(_targetY + dy * dragSensitivity * sign, minY, maxY);
                return;
            }

            if (!held)
                _dragging = false;
        }

        private void ApplyImmediate()
        {
            if (targetCamera == null) return;
            var pos = targetCamera.transform.position;
            targetCamera.transform.position = new Vector3(pos.x, _targetY, pos.z);
        }

        private bool IsPointerOverFloorInteractable(Vector2 screenPos)
        {
            if (targetCamera == null) return false;
            var ray = targetCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 80f))
                return false;
            return hit.collider != null
                   && hit.collider.GetComponentInParent<Mine3DFloorInteractable>() != null;
        }

        private static bool IsPointerOverUi()
        {
            if (EventSystem.current == null) return false;

            var ped = new PointerEventData(EventSystem.current);
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                ped.position = Touchscreen.current.primaryTouch.position.ReadValue();
            else if (Mouse.current != null)
                ped.position = Mouse.current.position.ReadValue();
            else
                return false;
#else
            ped.position = Input.mousePosition;
#endif
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);
            for (var i = 0; i < results.Count; i++)
            {
                var go = results[i].gameObject;
                if (go == null) continue;
                // World-space FloorUi не блокирует свайп по шахте.
                if (go.GetComponentInParent<Mine3DFloorView>() != null)
                    continue;
                if (go.GetComponentInParent<Canvas>() != null)
                    return true;
            }
            return false;
        }

        private static float ReadScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.scroll.ReadValue().y * 0.01f;
            return 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        private static bool TryGetPointer(out Vector2 pos, out bool pressed, out bool held)
        {
            pos = default;
            pressed = false;
            held = false;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                pos = Touchscreen.current.primaryTouch.position.ReadValue();
                pressed = Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
                held = true;
                return true;
            }

            if (Mouse.current != null)
            {
                pos = Mouse.current.position.ReadValue();
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                return true;
            }

            return false;
#else
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                pos = t.position;
                pressed = t.phase == TouchPhase.Began;
                held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary || t.phase == TouchPhase.Began;
                return true;
            }

            pos = Input.mousePosition;
            pressed = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            return true;
#endif
        }
    }
}
