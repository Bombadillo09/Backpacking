using Backpacking.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backpacking.Player
{
    /// <summary>
    /// First-person movement for a hiker: walk, sprint, crouch and jump, with speed
    /// reduced when climbing steep ground. The transform's origin sits at the feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Transform cameraPivot;

        [Header("Speed (m/s)")]
        [SerializeField] float walkSpeed = 2.2f;
        [SerializeField] float sprintSpeed = 4.5f;
        [SerializeField] float crouchSpeed = 1.1f;
        [SerializeField] float acceleration = 12f;
        [SerializeField, Range(0f, 1f)] float airControl = 0.2f;

        [Header("Slopes")]
        [Tooltip("Uphill incline (degrees) where slowdown begins.")]
        [SerializeField] float slowdownStartAngle = 8f;
        [Tooltip("Uphill incline (degrees) where slowdown is strongest.")]
        [SerializeField] float slowdownFullAngle = 35f;
        [SerializeField, Range(0.1f, 1f)] float steepestSpeedMultiplier = 0.35f;

        [Header("Jump & Gravity")]
        [SerializeField] float jumpHeight = 0.7f;
        [SerializeField] float gravity = -15f;

        [Header("Crouch")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.1f;
        [Tooltip("Distance from the top of the capsule down to the eyes.")]
        [SerializeField] float eyeOffsetFromTop = 0.12f;
        [Tooltip("How fast the capsule changes height, in m/s.")]
        [SerializeField] float crouchTransitionSpeed = 3f;

        [Header("Look")]
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float mouseSensitivity = 0.1f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] float gamepadLookSpeed = 160f;
        [SerializeField] float maxPitch = 85f;

        CharacterController controller;
        InputAction moveAction, lookAction, jumpAction, sprintAction, crouchAction;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;

        bool cursorWasNeeded;
        bool invertY;

        public bool IsGrounded => controller.isGrounded;
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float HorizontalSpeed => horizontalVelocity.magnitude;
        public Transform CameraPivot => cameraPivot;

        /// <summary>Scales all movement speeds, e.g. when exhausted. Set by other systems.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>Whether sprinting is currently allowed. Set by other systems.</summary>
        public bool CanSprint { get; set; } = true;
        /// <summary>Extra view rotation in degrees (x yaw, y pitch, z roll), e.g. shivering. Set by other systems.</summary>
        public Vector3 ViewOffset { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(FirstPersonController)} on {name} has no Input Actions asset assigned.", this);
                enabled = false;
                return;
            }

            InputActionMap map = inputActions.FindActionMap("Player", throwIfNotFound: true);
            moveAction = map.FindAction("Move", throwIfNotFound: true);
            lookAction = map.FindAction("Look", throwIfNotFound: true);
            jumpAction = map.FindAction("Jump", throwIfNotFound: true);
            sprintAction = map.FindAction("Sprint", throwIfNotFound: true);
            crouchAction = map.FindAction("Crouch", throwIfNotFound: true);
            GameInput.Initialize(inputActions);
        }

        void OnEnable()
        {
            moveAction?.actionMap.Enable();
            SetCursorLocked(true);
            GameSettings.Changed += ApplySettings;
            ApplySettings();
        }

        void OnDisable()
        {
            moveAction?.actionMap.Disable();
            SetCursorLocked(false);
            GameSettings.Changed -= ApplySettings;
        }

        void ApplySettings()
        {
            mouseSensitivity = GameSettings.MouseSensitivity;
            invertY = GameSettings.InvertMouseY;
            if (cameraPivot != null && cameraPivot.TryGetComponent(out Camera view))
                view.fieldOfView = GameSettings.FieldOfView;
        }

        void Update()
        {
            HandleCursor();
            bool locked = PlayerControlLock.MovementLocked;
            if (!locked && Cursor.lockState == CursorLockMode.Locked)
                Look();
            if (!locked)
                UpdateCrouch();
            Move(locked);
            cameraPivot.localRotation = Quaternion.Euler(pitch + ViewOffset.y, ViewOffset.x, ViewOffset.z);
        }

        void Look()
        {
            Vector2 delta = lookAction.ReadValue<Vector2>();
            bool fromPointer = lookAction.activeControl?.device is Pointer;
            delta *= fromPointer ? mouseSensitivity : gamepadLookSpeed * Time.deltaTime;
            if (invertY)
                delta.y = -delta.y;

            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        }

        void Move(bool locked)
        {
            Vector2 input = locked ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            Vector3 wishDirection = transform.right * input.x + transform.forward * input.y;

            // Sprinting only makes sense moving forward.
            IsSprinting = CanSprint && sprintAction.IsPressed() && input.y > 0.1f && !IsCrouching;
            float speed = (IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            if (wishDirection.sqrMagnitude > 0.0001f)
                speed *= UphillSpeedMultiplier(wishDirection.normalized);

            float accel = controller.isGrounded ? acceleration : acceleration * airControl;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wishDirection * speed, accel * Time.deltaTime);

            if (controller.isGrounded)
            {
                // Press into the ground hard enough to stay attached when walking downhill.
                if (verticalVelocity < 0f)
                    verticalVelocity = -2f - HorizontalSpeed;

                if (!locked && jumpAction.WasPressedThisFrame() && !IsCrouching)
                    verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            verticalVelocity += gravity * Time.deltaTime;

            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
        }

        float UphillSpeedMultiplier(Vector3 moveDirection)
        {
            if (!controller.isGrounded || !TryGetGroundNormal(out Vector3 normal))
                return 1f;

            // Metres of climb per metre travelled horizontally along the move direction.
            float grade = -Vector3.Dot(normal, moveDirection) / Mathf.Max(normal.y, 0.01f);
            if (grade <= 0f)
                return 1f;

            float incline = Mathf.Atan(grade) * Mathf.Rad2Deg;
            float t = Mathf.InverseLerp(slowdownStartAngle, slowdownFullAngle, incline);
            return Mathf.Lerp(1f, steepestSpeedMultiplier, t);
        }

        bool TryGetGroundNormal(out Vector3 normal)
        {
            // The ray starts inside our own capsule, so it won't hit it.
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                return true;
            }
            normal = Vector3.up;
            return false;
        }

        void UpdateCrouch()
        {
            if (crouchAction.WasPressedThisFrame())
            {
                if (!IsCrouching)
                    IsCrouching = true;
                else if (HasHeadroomToStand())
                    IsCrouching = false;
            }

            float targetHeight = IsCrouching ? crouchHeight : standingHeight;
            float height = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            cameraPivot.localPosition = new Vector3(0f, height - eyeOffsetFromTop, 0f);
        }

        bool HasHeadroomToStand()
        {
            float radius = controller.radius;
            Vector3 top = transform.position + Vector3.up * (controller.height - radius);
            float distance = standingHeight - controller.height + 0.05f;
            return !Physics.SphereCast(top, radius * 0.95f, Vector3.up, out _, distance, ~0, QueryTriggerInteraction.Ignore);
        }

        void HandleCursor()
        {
            // Menus free the cursor while open, and we take it back when they close.
            bool cursorNeeded = PlayerControlLock.CursorNeeded;
            if (cursorNeeded != cursorWasNeeded)
                SetCursorLocked(!cursorNeeded);
            cursorWasNeeded = cursorNeeded;
            if (cursorNeeded)
                return;

            // Take the cursor back if the window lost it (Alt-Tab and the like). Esc opens the pause menu.
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
