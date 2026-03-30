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
        [Tooltip("Если включено — позиция камеры сзади задаётся только горизонтальным углом (орбита), без поворота вместе с персонажем.")]
        [SerializeField] private bool useOrbitYawForOffset;
        private float _orbitYawDeg;
        [Header("Коллизия со стенами")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private float probeRadius = 0.22f;
        [SerializeField] private float minDistanceFromTarget = 1.1f;
        [SerializeField] private float surfacePadding = 0.12f;

        private Transform _target;

        public void SetTarget(Transform target)
        {
            _target = target;
            if (useOrbitYawForOffset)
                SyncOrbitYawFromTargetForward();
            SnapToTarget();
        }

        /// <summary> Режим дуэли: камера не «крутится» за поворотом персонажа; горизонтальный угол задаётся орбитой (перетаскивание). </summary>
        public void ConfigureForDuelOrbitCamera()
        {
            useOrbitYawForOffset = true;
            if (_target != null)
                SyncOrbitYawFromTargetForward();
        }

        public void AddOrbitYawDegrees(float delta) => _orbitYawDeg += delta;

        private void SyncOrbitYawFromTargetForward()
        {
            if (_target == null) return;
            var f = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-8f) return;
            f.Normalize();
            _orbitYawDeg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
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
            Vector3 back;
            if (useOrbitYawForOffset)
            {
                var yawRot = Quaternion.Euler(0f, _orbitYawDeg, 0f);
                back = yawRot * (Vector3.back * distance);
            }
            else
                back = -_target.forward * distance;
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

