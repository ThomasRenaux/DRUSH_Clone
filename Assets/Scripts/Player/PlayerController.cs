using UnityEngine;
using UnityEngine.InputSystem;
namespace Drush.Gameplay.Player
{
    /// <summary>
    /// Third-person player controller built on top of a CharacterController.
    /// Handles movement (walk, sprint, crouch), jumping, gravity,
    /// and synchronizes all animation parameters with an Animator (Synty BaseLocomotion).
    /// Inputs are read through the new Input System (project-wide actions).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        //  ======================================================
        //  INSPECTOR-EXPOSED PARAMETERS
        //  ======================================================

        [Header("Movement")]
        [SerializeField] private float forwardSpeed = 4f;
        [SerializeField] private float backwardSpeed = 2f;
        [SerializeField] private float strafeSpeed = 2f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private float crouchMultiplier = 0.5f;
        [SerializeField] private float crouchHeight = 1.2f;
        [SerializeField] private float rotationSpeed = 10f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpForce = 1.2f;
        [SerializeField] private float gravity = -15f;

        [Header("Animation")]
        [SerializeField] private Animator animator; // Reference to the character's Animator (assign in the Inspector)

        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions; // Optional: override project-wide Input Actions (useful for testing)

        //  ======================================================
        //  ANIMATOR PARAMETER HASHES
        //  Pre-computed hashes to avoid costly string comparisons every frame.
        //  Maps to the Synty BaseLocomotion blend tree parameters.
        //  ======================================================

        // -- Core locomotion --
        private static readonly int AnimMoveSpeed = Animator.StringToHash("MoveSpeed");                       // Movement speed (float)
        private static readonly int AnimCurrentGait = Animator.StringToHash("CurrentGait");                   // Gait type: 0=Idle, 1=Walk, 2=Run, 3=Sprint
        private static readonly int AnimStrafeDirectionX = Animator.StringToHash("StrafeDirectionX");         // Horizontal strafe direction (-1 to 1)
        private static readonly int AnimStrafeDirectionZ = Animator.StringToHash("StrafeDirectionZ");         // Vertical strafe direction (-1 to 1)
        private static readonly int AnimForwardStrafe = Animator.StringToHash("ForwardStrafe");               // Forward/backward input component
        private static readonly int AnimIsStrafing = Animator.StringToHash("IsStrafing");                     // Is the player moving sideways or backwards?

        // -- Character states --
        private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");                     // Is the character touching the ground?
        private static readonly int AnimIsJumping = Animator.StringToHash("IsJumping");                       // Is the character in the ascending phase of a jump?
        private static readonly int AnimIsCrouching = Animator.StringToHash("IsCrouching");                   // Is the character crouching?
        private static readonly int AnimIsStopped = Animator.StringToHash("IsStopped");                       // Is the character standing still?
        private static readonly int AnimIsWalking = Animator.StringToHash("IsWalking");                       // Is the character walking (not sprinting)?

        // -- Falling --
        private static readonly int AnimFallingDuration = Animator.StringToHash("FallingDuration");           // Time spent falling in seconds

        // -- Start and rotation --
        private static readonly int AnimIsStarting = Animator.StringToHash("IsStarting");                     // Did the player just start moving?
        private static readonly int AnimIsTurningInPlace = Animator.StringToHash("IsTurningInPlace");         // Is the character rotating without translating?
        private static readonly int AnimLocomotionStartDirection = Animator.StringToHash("LocomotionStartDirection"); // Start direction angle in degrees

        // -- Shuffle (small movements) --
        private static readonly int AnimShuffleDirectionX = Animator.StringToHash("ShuffleDirectionX");       // Shuffle direction X
        private static readonly int AnimShuffleDirectionZ = Animator.StringToHash("ShuffleDirectionZ");       // Shuffle direction Z

        // -- Lean --
        private static readonly int AnimLeanValue = Animator.StringToHash("LeanValue");                       // Body lean during turns (-1 to 1)
        private static readonly int AnimCameraRotationOffset = Animator.StringToHash("CameraRotationOffset"); // Angular offset between camera and character

        // -- Input state --
        private static readonly int AnimMovementInputPressed = Animator.StringToHash("MovementInputPressed"); // Input is currently held down
        private static readonly int AnimMovementInputHeld = Animator.StringToHash("MovementInputHeld");       // Input has been held for more than one frame

        //  ======================================================
        //  INTERNAL VARIABLES
        //  ======================================================

        private CharacterController controller;
        private Transform cameraTransform;

        private InputAction moveAction;   // "Move" action (joystick/WASD) — returns a Vector2
        private InputAction jumpAction;   // "Jump" action (spacebar)
        private InputAction sprintAction; // "Sprint" action (shift)
        private InputAction crouchAction; // "Crouch" action (ctrl/c)

        private Vector2 moveInput;
        private Vector2 prevMoveInput;   // Movement input from the previous frame (used to detect transitions)
        private Vector3 velocity;        // Accumulated vertical velocity (gravity + jump)
        private bool isSprinting;
        private bool isCrouching;

        // Test seam: when true, ReadInput sources moveInput / sprintHeld from the
        // fields below instead of the InputAction asset. Used by PlayMode tests
        // running in -batchmode -nographics where runtime-built InputActionAssets
        // do not reliably resolve their bindings against virtual keyboard devices.
        // No effect on production builds (default = false).
        [System.NonSerialized] internal bool testInputOverride;
        [System.NonSerialized] internal Vector2 testMoveInput;
        [System.NonSerialized] internal bool testSprintHeld;
        private bool isGrounded;         // Is the player touching the ground?
        private float fallingTimer;      // Time spent falling (in seconds)
        private float prevYaw;           // Y rotation from the previous frame (used to compute rotation delta)
        private float originalHeight;    // Original CharacterController height (before crouching)
        private Vector3 originalCenter;  // Original CharacterController center (before crouching)

        //  ======================================================
        //  INITIALIZATION
        //  ======================================================

        /// <summary>
        /// Called once when the GameObject is instantiated.
        /// Initializes references, retrieves input actions, and locks the cursor.
        /// </summary>
        private void Awake()
        {
            // Retrieve the CharacterController and save its original geometry
            controller = GetComponent<CharacterController>();
            originalHeight = controller.height;
            originalCenter = controller.center;

            // Reference to the main camera for camera-relative movement
            cameraTransform = Camera.main.transform;

            // Disable root motion: movement is handled manually via the CharacterController
            if (animator != null)
                animator.applyRootMotion = false;

            // Retrieve input actions (use injected asset if available, otherwise project-wide)
            var actions = inputActions != null ? inputActions : InputSystem.actions;
            if (inputActions != null) inputActions.Enable();
            moveAction = actions.FindAction("Move");
            jumpAction = actions.FindAction("Jump");
            sprintAction = actions.FindAction("Sprint");
            crouchAction = actions.FindAction("Crouch");

            // Lock and hide the cursor for FPS/TPS control
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Initialize previous yaw for lean calculation
            prevYaw = transform.eulerAngles.y;
        }

        //  ======================================================
        //  MAIN LOOP (called every frame)
        //  ======================================================

        /// <summary>
        /// Player update pipeline, executed every frame in this exact order:
        /// 1. Read inputs
        /// 2. Gravity and ground detection
        /// 3. Jump
        /// 4. Crouch
        /// 5. Movement and rotation
        /// 6. Animation update
        /// </summary>
        private void Update()
        {
            ReadInput();
            HandleGravityAndGround();
            HandleJump();
            HandleCrouch();
            HandleMovement();
            UpdateAnimator();
        }

        //  ======================================================
        //  INPUT READING
        //  ======================================================

        /// <summary>
        /// Reads raw input action values.
        /// Sprinting is automatically disabled if the player is crouching.
        /// </summary>
        private void ReadInput()
        {
            moveInput = testInputOverride ? testMoveInput : moveAction.ReadValue<Vector2>();

            bool wantsToSprint = testInputOverride ? testSprintHeld : sprintAction.IsPressed();

            // If the player wants to sprint while crouching, try to stand up first
            if (wantsToSprint && isCrouching && CanStandUp())
                isCrouching = false;

            isSprinting = wantsToSprint && !isCrouching;
        }

        //  ======================================================
        //  GRAVITY AND GROUND DETECTION
        //  ======================================================

        /// <summary>
        /// Handles gravity and ground detection using a SphereCast.
        /// SphereCast is more reliable than CharacterController.isGrounded because it
        /// uses a slightly shrunk sphere (0.9x radius) to avoid false positives on edges.
        /// </summary>
        private void HandleGravityAndGround()
        {
            // Compute the bottom point of the CharacterController capsule (center of the lower sphere)
            Vector3 bottomSphere = transform.position + controller.center
                + Vector3.up * (-controller.height / 2f + controller.radius);

            // Check distance = skinWidth + small margin to compensate for micro-offsets
            float checkDist = controller.skinWidth + 0.15f;

            // SphereCast downward to detect the ground
            isGrounded = Physics.SphereCast(
                bottomSphere,
                controller.radius * 0.9f, // Reduced radius to avoid edge-catching
                Vector3.down,
                out _,                    // We don't need the hit info
                checkDist
            );

            // Continuously apply gravity
            velocity.y += gravity * Time.deltaTime;

            // When grounded and falling, apply a small negative velocity
            // to keep the character pinned to the ground (prevents "bouncing")
            if (isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
            }
        }

        //  ======================================================
        //  JUMP
        //  ======================================================

        /// <summary>
        /// Triggers a jump if the player presses the jump button,
        /// is grounded, and is not crouching.
        /// Physics formula: v = sqrt(2 * height * gravity) to reach the desired height.
        /// </summary>
        private void HandleJump()
        {
            if (jumpAction.WasPressedThisFrame() && isGrounded && !isCrouching)
            {
                velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
            }
        }

        //  ======================================================
        //  CROUCH
        //  ======================================================

        /// <summary>
        /// Handles the crouch/stand toggle and smoothly transitions the CharacterController height.
        /// Both height and center are interpolated using Lerp for a fluid visual transition.
        /// </summary>
        private void HandleCrouch()
        {
            // Toggle: each press switches the crouch state
            // When sprinting, the slide is only allowed if moving forward (y > 0)
            if (crouchAction.WasPressedThisFrame())
            {
                if (isSprinting && moveInput.y <= 0.1f)
                    return; // Block slide when sprinting backward or sideways only

                if (isCrouching && !CanStandUp())
                    return; // Block standing up if there's not enough headroom

                isCrouching = !isCrouching;
            }

            // If crouching was canceled externally (e.g. sprint), still check headroom
            if (!isCrouching && !CanStandUp() && controller.height < originalHeight - 0.01f)
                isCrouching = true;

            // Smoothly interpolate the collider height toward the target
            float targetHeight = isCrouching ? crouchHeight : originalHeight;
            if (!Mathf.Approximately(controller.height, targetHeight))
            {
                float t = 10f * Time.deltaTime; // Smoothing factor (higher = faster transition)
                controller.height = Mathf.Lerp(controller.height, targetHeight, t);

                // Proportionally adjust the center so the collider stays properly positioned
                float heightRatio = controller.height / originalHeight;
                controller.center = new Vector3(originalCenter.x, originalCenter.y * heightRatio, originalCenter.z);
            }
        }

        /// <summary>
        /// Checks whether there is enough vertical clearance above the player
        /// to stand back up from a crouched position.
        /// Uses a SphereCast from the current capsule top toward the standing height.
        /// </summary>
        private bool CanStandUp()
        {
            float heightDifference = originalHeight - controller.height;
            if (heightDifference <= 0f)
                return true;

            // Cast upward from the top of the current (crouched) capsule
            Vector3 origin = transform.position + controller.center
                + Vector3.up * (controller.height / 2f - controller.radius);

            return !Physics.SphereCast(
                origin,
                controller.radius * 0.9f,
                Vector3.up,
                out _,
                heightDifference
            );
        }

        //  ======================================================
        //  MOVEMENT AND ROTATION
        //  ======================================================

        /// <summary>
        /// Computes and applies player movement in camera-relative coordinates.
        /// The character faces its movement direction when moving forward,
        /// and faces the camera direction when strafing or moving backward.
        /// </summary>
        private void HandleMovement()
        {
            // Project camera axes onto the horizontal plane (zero out the Y component)
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            // World-space move direction = input combined with camera axes
            Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;

            // --- Character rotation ---
            if (moveInput.y > 0.1f)
            {
                // Moving forward: rotate the character toward the movement direction
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            else if (moveDirection.sqrMagnitude > 0.01f)
            {
                // Strafing/backing up: face the camera's forward direction (not the movement direction)
                Quaternion targetRotation = Quaternion.LookRotation(forward);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            // --- Speed calculation based on movement direction ---
            float baseSpeed;
            if (moveInput.y > 0.1f)
                baseSpeed = forwardSpeed;      // Moving forward
            else if (moveInput.y < -0.1f)
                baseSpeed = backwardSpeed;     // Moving backward (slower)
            else
                baseSpeed = strafeSpeed;       // Strafing (intermediate speed)

            // Apply state-based speed multipliers
            if (isCrouching)
                baseSpeed *= crouchMultiplier;

            float currentSpeed = isSprinting ? baseSpeed * sprintMultiplier : baseSpeed;

            // Final movement vector: horizontal (movement) + vertical (gravity/jump)
            Vector3 horizontalMove = moveDirection.normalized * currentSpeed;
            controller.Move((horizontalMove + Vector3.up * velocity.y) * Time.deltaTime);
        }

        //  ======================================================
        //  ANIMATOR UPDATE
        //  ======================================================

        /// <summary>
        /// Synchronizes all Animator parameters with the player's current state.
        /// Compatible with the Synty BaseLocomotion animation system, which expects
        /// a large number of parameters to drive its blend trees and transitions.
        /// </summary>
        private void UpdateAnimator()
        {
            if (animator == null) return;

            // --- Compute actual horizontal speed (without the Y component) ---
            Vector3 horizontalVelocity = controller.velocity;
            horizontalVelocity.y = 0f;
            float speed2D = horizontalVelocity.magnitude;

            // --- Detect stopped state (this frame and the previous one) ---
            bool stopped = moveInput.magnitude < 0.01f;
            bool wasStoppedLastFrame = prevMoveInput.magnitude < 0.01f;

            // --- Gait calculation ---
            // Determines which animation set to use in the blend tree
            // 0 = Idle, 1 = Walk (or crouching), 2 = Run, 3 = Sprint
            int gait = 0;
            if (!stopped)
            {
                if (isSprinting) gait = 3;
                else if (isCrouching) gait = 1;
                else gait = 2;
            }

            // --- Falling duration counter ---
            // Used by the Animator to trigger long-fall animations
            if (!isGrounded && velocity.y < 0f)
                fallingTimer += Time.deltaTime;
            else
                fallingTimer = 0f;

            // --- Lean calculation (body tilt during turns) ---
            // Measures the Y rotation delta between this frame and the previous one
            float currentYaw = transform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(prevYaw, currentYaw);
            float leanValue = Mathf.Clamp(yawDelta / 10f, -1f, 1f); // Normalized between -1 and 1
            prevYaw = currentYaw;

            // --- Start detection (player just began moving) ---
            bool isStarting = !stopped && wasStoppedLastFrame;

            // --- Locomotion start direction ---
            // Input angle in degrees relative to forward (atan2 converts Vector2 to an angle)
            float startDir = 0f;
            if (isStarting)
                startDir = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg;

            // --- Movement input states ---
            bool pressed = !stopped;                   // Currently held down
            bool held = !stopped && !wasStoppedLastFrame; // Held for at least 2 frames

            // --- Turning in place (rotating without translating) ---
            bool turningInPlace = stopped && Mathf.Abs(yawDelta) > 0.5f;

            // --- Camera/character angular offset ---
            // Lets the Animator know if the character is facing a different direction than the camera
            float camYaw = cameraTransform.eulerAngles.y;
            float cameraRotationOffset = Mathf.DeltaAngle(currentYaw, camYaw);

            // ─────────────────────────────────────────────────────
            //  SET ANIMATOR PARAMETERS
            // ─────────────────────────────────────────────────────

            // Core locomotion (float values use 0.1s damping for smooth transitions)
            animator.SetFloat(AnimMoveSpeed, speed2D, 0.1f, Time.deltaTime);
            animator.SetInteger(AnimCurrentGait, gait);
            animator.SetFloat(AnimStrafeDirectionX, moveInput.x, 0.1f, Time.deltaTime);
            animator.SetFloat(AnimStrafeDirectionZ, moveInput.y, 0.1f, Time.deltaTime);
            animator.SetFloat(AnimShuffleDirectionX, moveInput.x, 0.1f, Time.deltaTime);
            animator.SetFloat(AnimShuffleDirectionZ, moveInput.y, 0.1f, Time.deltaTime);
            animator.SetFloat(AnimForwardStrafe, moveInput.y, 0.1f, Time.deltaTime);

            // Player is considered strafing if moving sideways or backwards
            bool strafing = Mathf.Abs(moveInput.x) > 0.1f || moveInput.y < -0.1f;
            animator.SetFloat(AnimIsStrafing, strafing ? 1f : 0f);
            animator.SetFloat(AnimLocomotionStartDirection, startDir);

            // Character state booleans
            animator.SetBool(AnimIsGrounded, isGrounded);
            animator.SetBool(AnimIsJumping, !isGrounded && velocity.y > 0f); // Jumping = airborne AND ascending
            animator.SetBool(AnimIsCrouching, isCrouching);
            animator.SetBool(AnimIsStopped, stopped);
            animator.SetBool(AnimIsWalking, !stopped && !isSprinting);
            animator.SetBool(AnimIsStarting, false);
            animator.SetBool(AnimIsTurningInPlace, turningInPlace);

            // Input states (used by Animator transitions)
            animator.SetBool(AnimMovementInputPressed, pressed);
            animator.SetBool(AnimMovementInputHeld, held);

            // Advanced parameters (falling, lean, camera offset)
            animator.SetFloat(AnimFallingDuration, fallingTimer);
            animator.SetFloat(AnimLeanValue, leanValue, 0.2f, Time.deltaTime);    // Higher damping for smooth lean
            animator.SetFloat(AnimCameraRotationOffset, cameraRotationOffset);

            // Save current input for transition detection on the next frame
            prevMoveInput = moveInput;
        }
    }
}