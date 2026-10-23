using UnityEngine;
using UnityEngine.InputSystem;

namespace TheOtherSide
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] Transform cameraPivot;
        [SerializeField] float walkSpeed = 4f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float jumpHeight = 1.2f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float gamepadLookSensitivity = 120f;
        [SerializeField] float minPitch = -80f;
        [SerializeField] float maxPitch = 80f;

        CharacterController _controller;
        float _pitch;
        float _verticalVelocity;

        public Transform CameraPivot => cameraPivot;

        public void Configure(Transform pivot)
        {
            cameraPivot = pivot;
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsGameplayActive)
                return;

            if (!GameInput.IsReady)
                return;

            Look();
            Move();
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

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (_controller.isGrounded && GameInput.JumpPressed)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = planar * speed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }
    }
}
