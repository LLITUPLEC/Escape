using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Match3
{
    /// <summary>
    /// Triggers callback when pointer is held down continuously for configured duration.
    /// </summary>
    public sealed class Match3LongPressActivator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Min(0.1f)] private float holdSeconds = 3f;

        private Action _onLongPress;
        private bool _isPressed;
        private bool _isTriggered;
        private float _pressedAt;

        public void Configure(float holdDurationSeconds, Action onLongPress)
        {
            holdSeconds = Mathf.Max(0.1f, holdDurationSeconds);
            _onLongPress = onLongPress;
        }

        private void OnDisable()
        {
            ResetHold();
        }

        private void Update()
        {
            if (!_isPressed || _isTriggered) return;
            if (Time.unscaledTime - _pressedAt < holdSeconds) return;

            _isTriggered = true;
            _isPressed = false;
            _onLongPress?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
            _isTriggered = false;
            _pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetHold();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ResetHold();
        }

        private void ResetHold()
        {
            _isPressed = false;
            _isTriggered = false;
            _pressedAt = 0f;
        }
    }
}
