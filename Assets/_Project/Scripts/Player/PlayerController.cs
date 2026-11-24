using UnityEngine;

namespace WheresMyLeftSock.Player
{
    public enum CrouchMode
    {
        Hold = 0,
        Toggle = 1
    }

    /// <summary>
    /// Crisp first-person CharacterController movement (walk, sprint, crouch with ceiling check,
    /// jump with coyote/buffer, head-bob, strafe tilt, and sprint FOV boost).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Pivot transform that handles vertical camera pitch, crouch height, and strafe tilt.")]
        [SerializeField] private Transform cameraRoot;

        [Tooltip("Main first-person camera inside the player hierarchy.")]
        [SerializeField] private Camera playerCamera;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5.5f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float crouchSpeed = 3.0f;
        [SerializeField] private float groundAcceleration = 28f;
        [SerializeField] private float groundDeceleration = 32f;
        [SerializeField] private float airAcceleration = 10f;
        [SerializeField] private bool canSprintWhileCrouching = false;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight = 1.15f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float groundedStickForce = -2f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.1f;

        [Header("Crouch")]
        [SerializeField] private CrouchMode crouchMode = CrouchMode.Hold;
        [SerializeField] private float standingHeight = 1.8f;
        [SerializeField] private float crouchingHeight = 1.0f;
        [SerializeField] private float standingCameraY = 1.6f;
        [SerializeField] private float crouchingCameraY = 0.85f;
        [SerializeField] private float crouchTransitionSpeed = 14f;
        [SerializeField] private LayerMask ceilingCheckMask = ~0;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float gamepadLookSpeed = 140f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private bool lockCursorOnStart = true;

        [Header("Camera Juice")]
        [SerializeField] private float baseFov = 70f;
        [SerializeField] private float sprintFovBoost = 8f;
        [SerializeField] private float fovLerpSpeed = 10f;
        [SerializeField] private bool enableHeadBob = true;
        [SerializeField] private float walkBobFrequency = 9f;
        [SerializeField] private float walkBobAmplitude = 0.025f;
        [SerializeField] private float sprintBobFrequency = 13f;
        [SerializeField] private float sprintBobAmplitude = 0.055f;
        [SerializeField] private bool enableStrafeTilt = true;
        [SerializeField] private float maxStrafeTiltDegrees = 2.0f;
        [SerializeField] private float strafeTiltSpeed = 10f;

        [Header("Clutter Push")]
        [Tooltip("Gentle impulse strength applied when walking into non-held dynamic Rigidbodies.")]
        [SerializeField] private float pushClutterPower = 2.2f;

        private readonly Collider[] _ceilingOverlapBuffer = new Collider[16];

        private CharacterController _controller;
        private PlayerInputHandler _input;
        private InteractionManager _interactionManager;

        private bool _controlsEnabled = true;
        private bool _wantsCrouch;
        private bool _isCrouching;

        private float _yaw;
        private float _pitch;
        private float _currentHeight;
        private float _currentCameraY;
        private float _verticalVelocity;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _bobTimer;
        private float _currentStrafeTilt;

        private float _externalSpeedMultiplier = 1f;
        private float _speedMultiplierTimer;

        private Vector3 _horizontalVelocity;
        private Vector3 _currentVelocity;
        private Vector3 _currentBobOffset;
        private Vector3 _cameraRootDefaultLocalPos;
        private Vector3 _cameraDefaultLocalPos;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching => _isCrouching;
        public float CurrentHorizontalSpeed => _horizontalVelocity.magnitude;
        public Vector3 Velocity => _currentVelocity;
        public Transform CameraTransform => playerCamera != null ? playerCamera.transform : cameraRoot;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputHandler>();
            _interactionManager = GetComponent<InteractionManager>();

            ResolveCameraReferences();
        }

        private void Start()
        {
            _currentHeight = standingHeight;
            _currentCameraY = standingCameraY;
            ApplyControllerHeight(_currentHeight);

            _yaw = transform.eulerAngles.y;

            if (cameraRoot != null)
            {
                _cameraRootDefaultLocalPos = cameraRoot.localPosition;
                float rawPitch = cameraRoot.localEulerAngles.x;
                if (rawPitch > 180f)
                {
                    rawPitch -= 360f;
                }

                _pitch = Mathf.Clamp(rawPitch, minPitch, maxPitch);
            }

            if (playerCamera != null)
            {
                _cameraDefaultLocalPos = playerCamera.transform.localPosition;
                playerCamera.fieldOfView = baseFov;
            }

            if (lockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void Update()
        {
            UpdateSpeedMultiplierTimer();

            if (!_controlsEnabled)
            {
                return;
            }

            HandleLook();
            HandleCrouch();
            HandleMovement();
            HandleCameraJuice();
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0.5f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            crouchSpeed = Mathf.Max(0.5f, crouchSpeed);

            standingHeight = Mathf.Max(0.6f, standingHeight);
            crouchingHeight = Mathf.Clamp(crouchingHeight, 0.4f, standingHeight - 0.1f);
            standingCameraY = Mathf.Max(0.3f, standingCameraY);
            crouchingCameraY = Mathf.Clamp(crouchingCameraY, 0.2f, standingCameraY - 0.05f);

            if (minPitch > maxPitch)
            {
                minPitch = maxPitch - 10f;
            }

            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            if (_controller != null && !Application.isPlaying)
            {
                ApplyControllerHeight(standingHeight);
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic)
            {
                return;
            }

            if (_interactionManager != null && body == _interactionManager.HeldBody)
            {
                return;
            }

            if (hit.moveDirection.y < -0.3f)
            {
                return;
            }

            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (pushDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            body.WakeUp();
            float massFactor = 1f / Mathf.Clamp(body.mass, 0.5f, 4f);
            Vector3 pushVel = pushDir.normalized * (pushClutterPower * massFactor);
            Vector3 currentBodyVel = body.linearVelocity;
            body.linearVelocity = new Vector3(
                Mathf.Lerp(currentBodyVel.x, pushVel.x, 0.5f),
                currentBodyVel.y,
                Mathf.Lerp(currentBodyVel.z, pushVel.z, 0.5f));
        }

        /// <summary>
        /// Enables or disables player locomotion and look input (useful for menus and end screens).
        /// </summary>
        public void SetControlsEnabled(bool enabled)
        {
            _controlsEnabled = enabled;
            _input.SetGameplayInputEnabled(enabled);

            if (!enabled)
            {
                _horizontalVelocity = Vector3.zero;
                _currentVelocity = Vector3.zero;
                IsSprinting = false;
            }
        }

        /// <summary>
        /// Locks or unlocks the mouse cursor for first-person look vs UI interaction.
        /// </summary>
        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// Temporarily scales horizontal movement speed (used by hazards like puddles or toys).
        /// </summary>
        public void ApplySpeedMultiplier(float multiplier, float duration)
        {
            _externalSpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 2f);
            _speedMultiplierTimer = Mathf.Max(0f, duration);
        }

        private void ResolveCameraReferences()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (cameraRoot == null && playerCamera != null)
            {
                Transform parent = playerCamera.transform.parent;
                cameraRoot = (parent != null && parent != transform) ? parent : playerCamera.transform;
            }

            if (cameraRoot == null)
            {
                Debug.LogWarning(
                    $"[PlayerController] No CameraRoot or Camera found under '{name}'. Assign them in the Inspector.",
                    this);
            }
        }

        private void UpdateSpeedMultiplierTimer()
        {
            if (_speedMultiplierTimer <= 0f)
            {
                return;
            }

            _speedMultiplierTimer -= Time.deltaTime;
            if (_speedMultiplierTimer <= 0f)
            {
                _externalSpeedMultiplier = 1f;
                _speedMultiplierTimer = 0f;
            }
        }

        private void HandleLook()
        {
            if (cameraRoot == null)
            {
                return;
            }

            if (lockCursorOnStart && Cursor.lockState != CursorLockMode.Locked)
            {
                if (_input.GrabPressedThisFrame)
                {
                    SetCursorLocked(true);
                }

                return;
            }

            Vector2 look = _input.LookInput;
            if (look.sqrMagnitude < 0.000001f)
            {
                return;
            }

            if (_input.IsLookUsingPointer)
            {
                _yaw += look.x * mouseSensitivity;
                _pitch -= look.y * mouseSensitivity;
            }
            else
            {
                _yaw += look.x * gamepadLookSpeed * Time.deltaTime;
                _pitch -= look.y * gamepadLookSpeed * Time.deltaTime;
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        private void HandleCrouch()
        {
            if (crouchMode == CrouchMode.Hold)
            {
                _wantsCrouch = _input.IsCrouchHeld;
            }
            else if (_input.CrouchPressedThisFrame)
            {
                _wantsCrouch = !_wantsCrouch;
            }

            if (_wantsCrouch)
            {
                _isCrouching = true;
            }
            else if (_isCrouching && CanStandUp())
            {
                _isCrouching = false;
            }

            float targetHeight = _isCrouching ? crouchingHeight : standingHeight;
            float targetCamY = _isCrouching ? crouchingCameraY : standingCameraY;
            float lerpT = 1f - Mathf.Exp(-crouchTransitionSpeed * Time.deltaTime);

            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, lerpT);
            _currentCameraY = Mathf.Lerp(_currentCameraY, targetCamY, lerpT);
            ApplyControllerHeight(_currentHeight);
        }

        private void HandleMovement()
        {
            bool grounded = _controller.isGrounded;
            _coyoteTimer = grounded ? coyoteTime : _coyoteTimer - Time.deltaTime;
            _jumpBufferTimer = _input.JumpPressedThisFrame ? jumpBufferTime : _jumpBufferTimer - Time.deltaTime;

            Vector2 moveInput = _input.MoveInput;
            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            bool hasMoveInput = moveInput.sqrMagnitude > 0.01f;
            IsSprinting = _input.IsSprintHeld && hasMoveInput && (!_isCrouching || canSprintWhileCrouching);

            float targetSpeed = _isCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            targetSpeed *= _externalSpeedMultiplier;

            Vector3 wishDir = transform.right * moveInput.x + transform.forward * moveInput.y;
            Vector3 targetHorizontalVel = wishDir * targetSpeed;

            float accel = grounded
                ? (hasMoveInput ? groundAcceleration : groundDeceleration)
                : airAcceleration;

            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHorizontalVel, accel * Time.deltaTime);

            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStickForce;
            }

            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
            }

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 frameMotion = (_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime;
            CollisionFlags flags = _controller.Move(frameMotion);

            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            {
                _verticalVelocity = 0f;
            }

            IsGrounded = _controller.isGrounded;
            _currentVelocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
        }

        private void HandleCameraJuice()
        {
            float targetTilt = enableStrafeTilt ? -_input.MoveInput.x * maxStrafeTiltDegrees : 0f;
            float tiltT = 1f - Mathf.Exp(-strafeTiltSpeed * Time.deltaTime);
            _currentStrafeTilt = Mathf.Lerp(_currentStrafeTilt, targetTilt, tiltT);

            if (cameraRoot != null)
            {
                cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, _currentStrafeTilt);
            }

            if (playerCamera != null)
            {
                bool activeSprint = IsSprinting && _horizontalVelocity.sqrMagnitude > (walkSpeed * walkSpeed * 0.5f);
                float targetFov = baseFov + (activeSprint ? sprintFovBoost : 0f);
                float fovT = 1f - Mathf.Exp(-fovLerpSpeed * Time.deltaTime);
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, fovT);
            }

            Vector3 targetBob = Vector3.zero;
            if (enableHeadBob && IsGrounded && _horizontalVelocity.magnitude > 0.25f)
            {
                float freq = IsSprinting ? sprintBobFrequency : walkBobFrequency;
                float amp = IsSprinting ? sprintBobAmplitude : walkBobAmplitude;
                _bobTimer += Time.deltaTime * freq;
                targetBob = new Vector3(
                    Mathf.Cos(_bobTimer * 0.5f) * amp * 0.5f,
                    Mathf.Sin(_bobTimer) * amp,
                    0f);
            }
            else
            {
                _bobTimer = 0f;
            }

            float bobDecayT = 1f - Mathf.Exp(-12f * Time.deltaTime);
            _currentBobOffset = Vector3.Lerp(_currentBobOffset, targetBob, bobDecayT);

            if (cameraRoot == null)
            {
                return;
            }

            if (playerCamera != null && playerCamera.transform != cameraRoot)
            {
                cameraRoot.localPosition = new Vector3(
                    _cameraRootDefaultLocalPos.x,
                    _currentCameraY,
                    _cameraRootDefaultLocalPos.z);
                playerCamera.transform.localPosition = _cameraDefaultLocalPos + _currentBobOffset;
            }
            else
            {
                cameraRoot.localPosition = new Vector3(
                    _cameraRootDefaultLocalPos.x + _currentBobOffset.x,
                    _currentCameraY + _currentBobOffset.y,
                    _cameraRootDefaultLocalPos.z + _currentBobOffset.z);
            }
        }

        private bool CanStandUp()
        {
            float radius = Mathf.Max(0.05f, _controller.radius * 0.9f);
            float lowerCenterY = Mathf.Max(radius, crouchingHeight - radius);
            float upperCenterY = standingHeight - radius;

            if (upperCenterY <= lowerCenterY)
            {
                return true;
            }

            Vector3 bottom = transform.position + Vector3.up * lowerCenterY;
            Vector3 top = transform.position + Vector3.up * upperCenterY;

            int hitCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                _ceilingOverlapBuffer,
                ceilingCheckMask,
                QueryTriggerInteraction.Ignore);

            Rigidbody heldBody = _interactionManager != null ? _interactionManager.HeldBody : null;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _ceilingOverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                if (col.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (heldBody != null && col.attachedRigidbody == heldBody)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void ApplyControllerHeight(float height)
        {
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }
    }
}
