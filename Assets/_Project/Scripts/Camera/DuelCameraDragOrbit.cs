using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace Project
{
    /// <summary>
    /// Горизонтальный поворот камеры вокруг цели только при перетаскивании по «пустому» экрану.
    /// UI (джойстик, кнопки и т.д.) блокирует вращение через EventSystem raycast.
    /// </summary>
    [RequireComponent(typeof(SimpleFollowCamera))]
    public sealed class DuelCameraDragOrbit : MonoBehaviour
    {
        [SerializeField] private float degreesPerScreenPixel = 0.12f;
        private SimpleFollowCamera _follow;
        private bool _dragMouse;
        private Vector2 _lastMousePos;
        private int _orbitFingerIndex = -1;
        private Vector2 _lastTouchPos;

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Enable();
#endif
        }

        private void Awake() => _follow = GetComponent<SimpleFollowCamera>();

        private void Update()
        {
            if (_follow == null || !_follow.enabled || !_follow.isActiveAndEnabled)
                return;

#if ENABLE_INPUT_SYSTEM
            HandleMouse();
            HandleTouchesEnhanced();
#else
            HandleLegacyMouse();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleMouse()
        {
            var m = Mouse.current;
            if (m == null) return;

            const int pointerId = -1;
            if (m.leftButton.wasPressedThisFrame)
            {
                if (!IsPointerOverUi(pointerId))
                {
                    _dragMouse = true;
                    _lastMousePos = m.position.ReadValue();
                }
            }

            if (m.leftButton.wasReleasedThisFrame)
                _dragMouse = false;

            if (_dragMouse && m.leftButton.isPressed)
            {
                var p = m.position.ReadValue();
                var d = p - _lastMousePos;
                _lastMousePos = p;
                ApplyYaw(-d.x);
            }
        }

        private void HandleTouchesEnhanced()
        {
            foreach (var touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                var fingerIndex = touch.finger.index;

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        if (_orbitFingerIndex < 0 && !IsPointerOverUi(fingerIndex))
                        {
                            _orbitFingerIndex = fingerIndex;
                            _lastTouchPos = touch.screenPosition;
                        }

                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (fingerIndex == _orbitFingerIndex)
                        {
                            var p = touch.screenPosition;
                            var d = p - _lastTouchPos;
                            _lastTouchPos = p;
                            ApplyYaw(-d.x);
                        }

                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (fingerIndex == _orbitFingerIndex)
                            _orbitFingerIndex = -1;
                        break;
                }
            }
        }
#else
        private void HandleLegacyMouse()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (!IsPointerOverUi(-1))
                {
                    _dragMouse = true;
                    _lastMousePos = Input.mousePosition;
                }
            }

            if (Input.GetMouseButtonUp(0))
                _dragMouse = false;

            if (_dragMouse && Input.GetMouseButton(0))
            {
                var p = (Vector2)Input.mousePosition;
                var d = p - _lastMousePos;
                _lastMousePos = p;
                ApplyYaw(-d.x);
            }
        }
#endif

        private static bool IsPointerOverUi(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }

        private void ApplyYaw(float deltaPixels)
        {
            if (Mathf.Abs(deltaPixels) < 0.01f)
                return;
            _follow.AddOrbitYawDegrees(deltaPixels * degreesPerScreenPixel);
        }
    }
}
