using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.MainMenu
{
    public sealed class MainMenuDragCamera : MonoBehaviour
    {
        [Header("Pan Input")]
        [SerializeField] private float dragSpeed = 0.012f;
        [SerializeField] private bool invertDrag = false;

        [Header("Cinematic Bounds (camera-relative XZ)")]
        [SerializeField] private float horizontalLimit = 7f;
        [SerializeField] private Vector2 depthBounds = new Vector2(-5f, 6f);

        [Header("Motion")]
        [SerializeField] private float smooth = 12f;

        private bool _isDragging;
        private Vector2 _lastPointerPos;
        private float _fixedY;
        private Quaternion _fixedRotation;
        private Vector3 _origin;
        private Vector3 _rightOnPlane;
        private Vector3 _forwardOnPlane;
        private Vector3 _targetPosition;

        private void Awake()
        {
            _fixedY = transform.position.y;
            _fixedRotation = transform.rotation;
            _origin = transform.position;
            _rightOnPlane = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            _forwardOnPlane = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _targetPosition = transform.position;
        }

        private void LateUpdate()
        {
            transform.rotation = _fixedRotation;

            var hasPointer = TryGetPointerPosition(out var pointerPos);
            if (!hasPointer)
            {
                _isDragging = false;
                return;
            }

            if (!_isDragging)
            {
                _isDragging = true;
                _lastPointerPos = pointerPos;
                return;
            }

            var delta = pointerPos - _lastPointerPos;
            _lastPointerPos = pointerPos;

            if (delta.sqrMagnitude < 0.0001f)
            {
                transform.position = Vector3.Lerp(transform.position, _targetPosition, 1f - Mathf.Exp(-smooth * Time.deltaTime));
                return;
            }

            var dir = invertDrag ? 1f : -1f;
            var move = (_rightOnPlane * delta.x + _forwardOnPlane * delta.y) * (dragSpeed * dir);

            // Ограничиваем движение не по миру, а по "рельсам" относительно текущего направления камеры.
            var pos = _targetPosition + move;
            var offset = pos - _origin;
            var side = Vector3.Dot(offset, _rightOnPlane);
            var depth = Vector3.Dot(offset, _forwardOnPlane);
            side = Mathf.Clamp(side, -Mathf.Abs(horizontalLimit), Mathf.Abs(horizontalLimit));
            depth = Mathf.Clamp(depth, Mathf.Min(depthBounds.x, depthBounds.y), Mathf.Max(depthBounds.x, depthBounds.y));
            pos = _origin + _rightOnPlane * side + _forwardOnPlane * depth;
            pos.y = _fixedY;
            _targetPosition = pos;
            transform.position = Vector3.Lerp(transform.position, _targetPosition, 1f - Mathf.Exp(-smooth * Time.deltaTime));
        }

        private static bool TryGetPointerPosition(out Vector2 pointerPos)
        {
            #if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touch = ts.primaryTouch;
                if (touch.press.isPressed)
                {
                    pointerPos = touch.position.ReadValue();
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                pointerPos = mouse.position.ReadValue();
                return true;
            }
            #else
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began || t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    pointerPos = t.position;
                    return true;
                }
            }

            if (Input.GetMouseButton(0))
            {
                pointerPos = Input.mousePosition;
                return true;
            }
            #endif

            pointerPos = default;
            return false;
        }
    }
}

