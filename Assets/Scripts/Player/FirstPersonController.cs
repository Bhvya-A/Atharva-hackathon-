using UnityEngine;
using UnityEngine.InputSystem;

namespace TheOtherSide
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraPivot;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float jumpHeight = 1.2f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float gamepadLookSensitivity = 120f;
        [SerializeField] float minPitch = -80f;
        [SerializeField] float maxPitch = 80f;

        [Header("Stairs / grounding")]
        [Tooltip("Largest vertical snap that is treated as a stair step and smoothed out of the camera.")]
        [SerializeField] float maxStepHeight = 0.38f;
        [Tooltip("How long the camera takes to absorb a stair step. Larger is smoother but laggier.")]
        [SerializeField] float stairSmoothTime = 0.18f;
        [Tooltip("Downward velocity applied while grounded so the capsule follows descending steps instead of floating.")]
        [SerializeField] float groundStickVelocity = -3.5f;

        CharacterController _controller;
        PlayerHealth _health;
        float _pitch;
        float _verticalVelocity;
        Vector3 _basePivotLocalPosition = new Vector3(0f, 1.6f, 0f);
        Vector3 _planarVelocity;
        float _smoothedBodyY;
        float _bodyYSmoothVelocity;
        bool _hasSmoothedBodyY;

        public Transform CameraPivot => cameraPivot;
        public Vector3 PlanarVelocity => _planarVelocity;
        public float WalkSpeed => walkSpeed;
        public bool IsGrounded => CheckGrounded();

        public void Configure(Transform pivot)
        {
            cameraPivot = pivot;
            CapturePivotBase();
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<PlayerHealth>();
            CapturePivotBase();
        }

        void OnEnable()
        {
            CapturePivotBase();
        }

        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
                return;

            if (!GameInput.IsReady)
                return;

            if (_health == null)
                _health = GetComponent<PlayerHealth>();

            // Death/respawn lock: the player keeps looking at the death spot and
            // cannot walk or jump until PlayerHealth teleports them back.
            if (_health != null && _health.IsDead)
                return;

            Look();
            Move();
        }

        void LateUpdate()
        {
            if (cameraPivot == null)
                return;

            // Stair smoothing: the capsule still snaps up each tread, but the camera
            // follows a critically damped height instead of the raw capsule. The
            // difference is applied as a local offset, so the body is never slowed
            // and a jump (fast upward velocity) still feels immediate.
            float bodyY = transform.position.y;

            if (!_hasSmoothedBodyY || _verticalVelocity > 0.5f ||
                Mathf.Abs(bodyY - _smoothedBodyY) > maxStepHeight * 2f)
            {
                _smoothedBodyY = bodyY;
                _bodyYSmoothVelocity = 0f;
                _hasSmoothedBodyY = true;
            }
            else
            {
                _smoothedBodyY = Mathf.SmoothDamp(
                    _smoothedBodyY,
                    bodyY,
                    ref _bodyYSmoothVelocity,
                    stairSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);
            }

            float offset = Mathf.Clamp(_smoothedBodyY - bodyY, -maxStepHeight, maxStepHeight);
            cameraPivot.localPosition = _basePivotLocalPosition + Vector3.up * offset;
        }

        /// <summary>
        /// Clears movement state after a respawn teleport so no carried velocity
        /// or downward gravity burst is applied on the first frame back.
        /// </summary>
        public void ResetMotion()
        {
            _verticalVelocity = 0f;
            _pitch = 0f;
            _planarVelocity = Vector3.zero;
            _hasSmoothedBodyY = false;
            _bodyYSmoothVelocity = 0f;

            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.identity;
                cameraPivot.localPosition = _basePivotLocalPosition;
            }
        }

        void Look()
        {
            Vector2 look = GameInput.LookValue;
            bool mouseLook = Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.0001f;
            float mouseX = mouseLook
                ? look.x * lookSensitivity
                : look.x * gamepadLookSensitivity * Time.deltaTime;
            float mouseY = mouseLook
                ? look.y * lookSensitivity
                : look.y * gamepadLookSensitivity * Time.deltaTime;

            transform.Rotate(0f, mouseX, 0f);
            _pitch = Mathf.Clamp(_pitch - mouseY, minPitch, maxPitch);

            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 input = GameInput.MoveValue;
            Vector3 planar = transform.right * input.x + transform.forward * input.y;
            if (planar.sqrMagnitude > 1f)
                planar.Normalize();

            float speed = GameInput.SprintHeld ? sprintSpeed : walkSpeed;

            bool grounded = CheckGrounded();
            bool canJump = grounded && _verticalVelocity <= 0.01f;

            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = groundStickVelocity;

            if (canJump && GameInput.JumpPressed)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 before = transform.position;
            Vector3 velocity = planar * speed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);

            Vector3 delta = transform.position - before;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            _planarVelocity = new Vector3(delta.x / dt, 0f, delta.z / dt);
        }

        bool CheckGrounded()
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            if (_controller.isGrounded)
                return true;

            // CharacterController.isGrounded flickers while descending stairs.
            // A short probe keeps the capsule stuck to the next tread (0.25 m rise).
            Vector3 origin = transform.position + Vector3.up * 0.15f;
            return Physics.Raycast(
                origin,
                Vector3.down,
                0.5f,
                ~0,
                QueryTriggerInteraction.Ignore);
        }

        void CapturePivotBase()
        {
            if (cameraPivot != null)
                _basePivotLocalPosition = cameraPivot.localPosition;
        }
    }
}
