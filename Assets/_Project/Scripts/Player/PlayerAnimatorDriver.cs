using UnityEngine;

namespace Project.Player
{
    /// <summary>
    /// Драйвит Animator по движению transform для удалённых игроков.
    /// Для локального игрока анимации уже управляются PlayerMovementController, поэтому тут пропускаем isLocal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string groundedBoolParam = "Grounded";

        [Header("Tuning")]
        [SerializeField] private float jumpUpVelocityThreshold = 1.2f;
        [SerializeField] private float jumpTriggerCooldown = 0.25f;
        [SerializeField] private float groundedRayLength = 2.0f;
        [SerializeField] private float groundedHitDistance = 0.15f;

        private Vector3 _prevPos;
        private float _lastJumpTriggerTime = -999f;

        private NetworkTransformView _netView;
        private PlayerMovementController _movement;

        private void Awake()
        {
            _netView = GetComponent<NetworkTransformView>();
            _movement = GetComponent<PlayerMovementController>();

            if (animator == null) animator = GetComponentInChildren<Animator>();
            _prevPos = transform.position;
        }

        private void Update()
        {
            if (animator == null) return;
            if (_netView != null && _netView.IsLocal) return;

            var dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

            var pos = transform.position;
            var delta = pos - _prevPos;
            _prevPos = pos;

            var planarSpeed = new Vector2(delta.x, delta.z).magnitude / dt;
            var maxSpeed = (_movement != null) ? (_movement.MoveSpeed * _movement.RunMultiplier) : 4.5f * 1.6f;
            var normalized = maxSpeed <= 0.0001f ? 0f : Mathf.Clamp01(planarSpeed / maxSpeed);

            animator.SetFloat(speedParam, normalized, 0.1f, dt);

            var grounded = ComputeGrounded();
            animator.SetBool(groundedBoolParam, grounded);

            var verticalVel = delta.y / dt;
            if (!grounded && verticalVel > jumpUpVelocityThreshold)
            {
                if (Time.time - _lastJumpTriggerTime >= jumpTriggerCooldown)
                {
                    var st = animator.GetCurrentAnimatorStateInfo(0);
                    if (!st.IsName("Jump"))
                    {
                        animator.SetTrigger(jumpTrigger);
                        _lastJumpTriggerTime = Time.time;
                    }
                }
            }
        }

        private bool ComputeGrounded()
        {
            // Райкаст вниз — самый стабильный способ, если CharacterController не двигается вручную (как у удалённых).
            var origin = transform.position + Vector3.up * 0.05f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, groundedRayLength))
            {
                return hit.distance <= groundedHitDistance;
            }

            return false;
        }
    }
}

