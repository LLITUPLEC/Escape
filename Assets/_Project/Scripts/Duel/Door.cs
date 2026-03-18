using UnityEngine;

namespace Project.Duel
{
    public sealed class Door : MonoBehaviour
    {
        [SerializeField] private Collider doorCollider;
        [SerializeField] private Transform doorVisual;
        [SerializeField] private float openRaiseY = 3f;
        [SerializeField] private float openSpeed = 6f;
        [SerializeField] private bool startClosed = true;

        private Vector3 _closedLocalPos;
        private Vector3 _openLocalPos;
        private bool _isOpen;

        private void Awake()
        {
            if (doorCollider == null) doorCollider = GetComponent<Collider>();
            if (doorVisual == null) doorVisual = transform;

            _closedLocalPos = doorVisual.localPosition;
            _openLocalPos = _closedLocalPos + Vector3.up * openRaiseY;

            SetOpen(!startClosed, instant: true);
        }

        public void SetOpen(bool open, bool instant = false)
        {
            _isOpen = open;
            if (doorCollider != null) doorCollider.enabled = !_isOpen;

            if (instant)
            {
                doorVisual.localPosition = _isOpen ? _openLocalPos : _closedLocalPos;
            }
        }

        private void Update()
        {
            var target = _isOpen ? _openLocalPos : _closedLocalPos;
            doorVisual.localPosition = Vector3.Lerp(
                doorVisual.localPosition,
                target,
                1f - Mathf.Exp(-openSpeed * Time.deltaTime));
        }
    }
}

