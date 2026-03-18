using UnityEngine;

namespace Project
{
    public sealed class SimpleFollowCamera : MonoBehaviour
    {
        // Приближено примерно в 2 раза относительно старых значений.
        [SerializeField] private float height = 2.75f;
        [SerializeField] private float distance = 3.75f;
        [SerializeField] private float lookHeight = 1.4f;
        [SerializeField] private float smooth = 10f;

        private Transform _target;

        public void SetTarget(Transform target)
        {
            _target = target;
            SnapToTarget();
        }

        private void SnapToTarget()
        {
            if (_target == null) return;
            var desired = GetDesiredPosition();
            transform.position = desired;
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }

        private Vector3 GetDesiredPosition()
        {
            var back = -_target.forward * distance;
            var up = Vector3.up * height;
            return _target.position + up + back;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // Сзади и сверху относительно направления игрока.
            // (Так камера предсказуемо следует за движением и не переходит в орбиту.)
            var desired = GetDesiredPosition();
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smooth * Time.deltaTime));
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }
    }
}

