using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovementController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float runMultiplier = 1.6f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float jumpHeight = 1.2f;
        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string groundedBoolParam = "Grounded";

        private CharacterController _cc;
        private float _vy;

        public float MoveSpeed => moveSpeed;
        public float RunMultiplier => runMultiplier;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
        }

        private void Update()
        {
            var input = ReadMoveInput();
            input = Vector2.ClampMagnitude(input, 1f);

            var isRunning = ReadRunInput();
            var speed = moveSpeed * (isRunning ? runMultiplier : 1f);
            var move = (transform.right * input.x + transform.forward * input.y) * speed;

            if (_cc.isGrounded && _vy < 0f) _vy = -1f;
            if (_cc.isGrounded && ReadJumpPressed())
            {
                // v = sqrt(2*g*h). gravity отрицательная.
                _vy = Mathf.Sqrt(Mathf.Max(0.01f, jumpHeight) * -2f * gravity);
                if (animator != null) animator.SetTrigger(jumpTrigger);
            }
            _vy += gravity * Time.deltaTime;
            move.y = _vy;

            _cc.Move(move * Time.deltaTime);

            if (animator != null)
            {
                animator.SetBool(groundedBoolParam, _cc.isGrounded);

                // 0..1 нормализованная скорость для blend tree.
                var planar = new Vector2(move.x, move.z).magnitude;
                var max = moveSpeed * runMultiplier;
                var normalized = max <= 0.0001f ? 0f : Mathf.Clamp01(planar / max);
                animator.SetFloat(speedParam, normalized, 0.1f, Time.deltaTime);
            }
        }

        private static Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;

            var x = 0f;
            var y = 0f;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) y -= 1f;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) y += 1f;
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static bool ReadRunInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            return k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        private static bool ReadJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            return k.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }
    }
}

