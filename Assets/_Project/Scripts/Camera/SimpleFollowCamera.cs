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
        [Header("Коллизия со стенами")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private float probeRadius = 0.22f;
        [SerializeField] private float minDistanceFromTarget = 1.1f;
        [SerializeField] private float surfacePadding = 0.12f;

        private Transform _target;

        public void SetTarget(Transform target)
        {
            _target = target;
            SnapToTarget();
        }

        private void SnapToTarget()
        {
            if (_target == null) return;
            var desired = ResolveObstacles(GetDesiredPositionRaw());
            transform.position = desired;
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }

        private Vector3 GetDesiredPositionRaw()
        {
            var back = -_target.forward * distance;
            var up = Vector3.up * height;
            return _target.position + up + back;
        }

        private Vector3 ResolveObstacles(Vector3 desiredWorld)
        {
            var origin = _target.position + Vector3.up * lookHeight;
            var toCam = desiredWorld - origin;
            var dist = toCam.magnitude;
            if (dist < 0.02f) return desiredWorld;

            var dir = toCam / dist;
            var wantDist = Mathf.Max(minDistanceFromTarget, dist);
            if (Physics.SphereCast(origin, probeRadius, dir, out var hit, wantDist, obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                var clamped = Mathf.Max(minDistanceFromTarget * 0.35f, hit.distance - surfacePadding);
                return origin + dir * clamped;
            }

            return desiredWorld;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // Сзади и сверху относительно направления игрока.
            // (Так камера предсказуемо следует за движением и не переходит в орбиту.)
            var desired = ResolveObstacles(GetDesiredPositionRaw());
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smooth * Time.deltaTime));
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }
    }
}

