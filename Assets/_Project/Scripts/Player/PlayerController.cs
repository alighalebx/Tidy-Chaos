using System;
using UnityEngine;

namespace WheresMyLeftSock.Player
{
    public enum CrouchMode
    {
        Hold = 0,
        Toggle = 1
    }

    /// <summary>
    /// Goofy third-person physics character. Movement is a Rigidbody pushed toward a camera-relative
    /// target velocity with limited acceleration, so you carry momentum, skid when reversing, shove clutter
    /// around with your body, and faceplant if you sprint into a wall. Visual wobble lives in GoofyBody.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ThirdPersonCamera cameraRig = default;

        [Header("Speeds")]
        [SerializeField] private float walkSpeed = 4.6f;
        [SerializeField] private float sprintSpeed = 7.4f;
        [SerializeField] private float crouchSpeed = 2.3f;

        [Header("Acceleration (lower = slidier)")]
        [SerializeField] private float groundAcceleration = 30f;
        [SerializeField] private float groundBraking = 20f;
        [Tooltip("Acceleration used when pushing against your own momentum (big skids).")]
        [SerializeField] private float skidAcceleration = 14f;
        [SerializeField] private float airAcceleration = 7f;
        [SerializeField] private float knockedDownFriction = 4.5f;
        [SerializeField] private float turnSpeedDegrees = 540f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight = 1.0f;
        [SerializeField] private float fallGravityMultiplier = 1.8f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Ground Check")]
        [SerializeField] private float groundCheckDistance = 0.12f;
        [SerializeField] private float maxGroundAngle = 50f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Crouch")]
        [SerializeField] private CrouchMode crouchMode = CrouchMode.Hold;
        [SerializeField] private float standingHeight = 1.6f;
        [SerializeField] private float crouchingHeight = 1.0f;
        [SerializeField] private float crouchTransitionSpeed = 12f;

        [Header("Goofy")]
        [Tooltip("Sprinting into a wall faster than this knocks you on your back.")]
        [SerializeField] private float bonkSpeed = 6.2f;
        [SerializeField] private float bonkKnockdownDuration = 0.9f;
        [SerializeField] private float bonkBounce = 2.6f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnStart = true;

        public event Action Jumped;
        public event Action<float> Landed;
        public event Action KnockedDown;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private PlayerInputHandler _input;
        private InteractionManager _interaction;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private readonly Collider[] _ceilingHits = new Collider[8];

        private bool _controlsEnabled = true;
        private bool _wantsCrouch;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _jumpCooldown;
        private float _knockTimer;
        private float _bonkGrace;
        private float _speedMultiplier = 1f;
        private float _speedMultiplierTimer;
        private float _lastPlanarSpeed;
        private float _lastFallSpeed;
        private Vector3 _lastPlanarVelocity;
        private Vector3 _smoothedAccel;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSkidding { get; private set; }
        public bool IsKnockedDown => _knockTimer > 0f;
        /// <summary>+85 = faceplant forward, -85 = flat on your back.</summary>
        public float KnockdownPitch { get; private set; } = 85f;
        public float CrouchBlend { get; private set; }
        public float CurrentHorizontalSpeed => new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;
        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;
        public Vector3 PlanarAcceleration => _smoothedAccel;
        public Vector3 MoveInputWorld { get; private set; }
        public bool ControlsEnabled => _controlsEnabled;
        public ThirdPersonCamera CameraRig => cameraRig;
        public Transform CameraTransform =>
            cameraRig != null ? cameraRig.CameraTransform : (Camera.main != null ? Camera.main.transform : null);

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _input = GetComponent<PlayerInputHandler>();
            _interaction = GetComponent<InteractionManager>();

            if (cameraRig == null)
            {
                cameraRig = GetComponentInChildren<ThirdPersonCamera>(true);
                if (cameraRig == null)
                {
                    cameraRig = FindFirstObjectByType<ThirdPersonCamera>();
                }
            }

            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ApplyCapsuleHeight(standingHeight);
        }

        private void Start()
        {
            if (lockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void Update()
        {
            if (_speedMultiplierTimer > 0f)
            {
                _speedMultiplierTimer -= Time.deltaTime;
                if (_speedMultiplierTimer <= 0f)
                {
                    _speedMultiplier = 1f;
                }
            }

            if (!_controlsEnabled)
            {
                MoveInputWorld = Vector3.zero;
                IsSprinting = false;
                return;
            }

            if (_input.JumpPressedThisFrame)
            {
                _jumpBufferTimer = jumpBufferTime;
            }

            if (crouchMode == CrouchMode.Hold)
            {
                _wantsCrouch = _input.IsCrouchHeld;
            }
            else if (_input.CrouchPressedThisFrame)
            {
                _wantsCrouch = !_wantsCrouch;
            }

            // camera-relative move direction
            Vector2 move = _input.MoveInput;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            Transform cam = CameraTransform;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : transform.forward;
            if (fwd.sqrMagnitude < 0.01f)
            {
                fwd = transform.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            MoveInputWorld = fwd * move.y + right * move.x;
            IsSprinting = _input.IsSprintHeld && move.sqrMagnitude > 0.04f && !IsCrouching && !IsKnockedDown;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_knockTimer > 0f)
            {
                _knockTimer -= dt;
                if (_knockTimer <= 0f)
                {
                    _bonkGrace = 0.8f; // don't instantly re-bonk while getting up
                }
            }
            else if (_bonkGrace > 0f)
            {
                _bonkGrace -= dt;
            }

            if (_jumpCooldown > 0f)
            {
                _jumpCooldown -= dt;
            }

            bool wasGrounded = IsGrounded;
            IsGrounded = CheckGround(out Vector3 groundNormal);
            _coyoteTimer = IsGrounded ? coyoteTime : _coyoteTimer - dt;
            _jumpBufferTimer -= dt;

            Vector3 v = _rb.linearVelocity;
            if (IsGrounded && !wasGrounded)
            {
                Landed?.Invoke(_lastFallSpeed);
            }

            _lastFallSpeed = Mathf.Max(0f, -v.y);

            UpdateCrouch(dt);

            // --- horizontal: chase target velocity with limited acceleration ---
            Vector3 planar = new Vector3(v.x, 0f, v.z);
            float targetSpeed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            targetSpeed *= _speedMultiplier;
            Vector3 wish = (_controlsEnabled && !IsKnockedDown) ? MoveInputWorld * targetSpeed : Vector3.zero;

            float accel;
            IsSkidding = false;
            if (IsKnockedDown)
            {
                accel = IsGrounded ? knockedDownFriction : 0f;
            }
            else if (!IsGrounded)
            {
                accel = airAcceleration;
            }
            else if (wish.sqrMagnitude < 0.01f)
            {
                accel = groundBraking;
            }
            else if (planar.magnitude > 2.5f && Vector3.Dot(wish.normalized, planar.normalized) < -0.25f)
            {
                accel = skidAcceleration;
                IsSkidding = true;
            }
            else
            {
                accel = groundAcceleration;
            }

            Vector3 change = Vector3.ClampMagnitude(wish - planar, accel * dt);
            if (IsGrounded)
            {
                change = Vector3.ProjectOnPlane(change, groundNormal);
            }

            _rb.AddForce(change, ForceMode.VelocityChange);

            // --- jump ---
            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f && _jumpCooldown <= 0f && !IsKnockedDown && _controlsEnabled)
            {
                float jumpVel = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
                v = _rb.linearVelocity;
                _rb.linearVelocity = new Vector3(v.x, jumpVel, v.z);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                _jumpCooldown = 0.2f;
                IsGrounded = false;
                Jumped?.Invoke();
            }

            // --- snappier falling ---
            if (_rb.linearVelocity.y < 0f && !IsGrounded)
            {
                _rb.AddForce(Physics.gravity * (fallGravityMultiplier - 1f), ForceMode.Acceleration);
            }

            // --- facing ---
            UpdateFacing(dt, wish);

            // --- book-keeping for the wobble rig ---
            Vector3 newPlanar = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Vector3 rawAccel = (newPlanar - _lastPlanarVelocity) / dt;
            _smoothedAccel = Vector3.Lerp(_smoothedAccel, rawAccel, 1f - Mathf.Exp(-18f * dt));
            _lastPlanarVelocity = newPlanar;
            _lastPlanarSpeed = newPlanar.magnitude;
        }

        private void UpdateFacing(float dt, Vector3 wish)
        {
            if (IsKnockedDown)
            {
                return;
            }

            Vector3 faceDir = Vector3.zero;
            bool holding = _interaction != null && _interaction.IsHoldingObject;
            Transform cam = CameraTransform;
            if (holding && cam != null)
            {
                faceDir = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            }
            else if (wish.sqrMagnitude > 0.01f)
            {
                faceDir = wish;
            }

            if (faceDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
            _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, target, turnSpeedDegrees * dt));
        }

        private void UpdateCrouch(float dt)
        {
            bool want = _wantsCrouch && _controlsEnabled && !IsKnockedDown;
            if (want)
            {
                IsCrouching = true;
            }
            else if (IsCrouching && CanStandUp())
            {
                IsCrouching = false;
            }

            CrouchBlend = Mathf.MoveTowards(CrouchBlend, IsCrouching ? 1f : 0f, crouchTransitionSpeed * dt * 0.5f);
            ApplyCapsuleHeight(Mathf.Lerp(standingHeight, crouchingHeight, CrouchBlend));
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsKnockedDown || _bonkGrace > 0f || collision.contactCount == 0)
            {
                return;
            }

            Rigidbody other = collision.rigidbody;
            if (other != null && !other.isKinematic)
            {
                return; // shoving clutter is fine
            }

            ContactPoint contact = collision.GetContact(0);
            if (Mathf.Abs(contact.normal.y) > 0.5f)
            {
                return; // floors / ceilings
            }

            float into = Vector3.Dot(_lastPlanarVelocity, -contact.normal);
            if (_lastPlanarSpeed >= bonkSpeed && into >= bonkSpeed * 0.75f)
            {
                Knockdown(bonkKnockdownDuration, backwards: true);
                _rb.AddForce(contact.normal * bonkBounce + Vector3.up * 2.5f, ForceMode.VelocityChange);
                if (cameraRig != null)
                {
                    cameraRig.AddShake(0.6f);
                }
            }
        }

        /// <summary>Flops the character over for a moment (slips, bonks). Drops whatever you're holding.</summary>
        public void Knockdown(float duration, bool backwards = false)
        {
            bool wasDown = IsKnockedDown;
            _knockTimer = Mathf.Max(_knockTimer, duration);
            KnockdownPitch = backwards ? -85f : 85f;
            if (_interaction != null)
            {
                _interaction.ForceDropHeldObject();
            }

            if (!wasDown)
            {
                KnockedDown?.Invoke();
            }
        }

        public void SetControlsEnabled(bool enabled)
        {
            _controlsEnabled = enabled;
            _input.SetGameplayInputEnabled(enabled);
            if (!enabled)
            {
                MoveInputWorld = Vector3.zero;
                IsSprinting = false;
            }
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Temporarily scales movement speed (hazards).</summary>
        public void ApplySpeedMultiplier(float multiplier, float duration)
        {
            _speedMultiplier = Mathf.Clamp(multiplier, 0.1f, 2f);
            _speedMultiplierTimer = Mathf.Max(0f, duration);
        }

        private bool CheckGround(out Vector3 normal)
        {
            normal = Vector3.up;
            float r = _capsule.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (r + 0.05f);
            int count = Physics.SphereCastNonAlloc(origin, r, Vector3.down, _groundHits, 0.05f + groundCheckDistance,
                groundMask, QueryTriggerInteraction.Ignore);

            float minY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            Rigidbody held = _interaction != null ? _interaction.HeldBody : null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _groundHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (held != null && h.rigidbody == held)
                {
                    continue;
                }

                if (h.normal.y >= minY && h.distance > 0f)
                {
                    normal = h.normal;
                    return true;
                }
            }

            return false;
        }

        private bool CanStandUp()
        {
            float r = _capsule.radius * 0.9f;
            Vector3 bottom = transform.position + Vector3.up * (crouchingHeight - r);
            Vector3 top = transform.position + Vector3.up * (standingHeight - r);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, r, _ceilingHits, ~0, QueryTriggerInteraction.Ignore);
            Rigidbody held = _interaction != null ? _interaction.HeldBody : null;
            for (int i = 0; i < count; i++)
            {
                Collider c = _ceilingHits[i];
                if (c == null || c.transform.IsChildOf(transform) || (held != null && c.attachedRigidbody == held))
                {
                    continue;
                }

                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
                {
                    continue; // clutter on your head just gets shoved
                }

                return false;
            }

            return true;
        }

        private void ApplyCapsuleHeight(float height)
        {
            if (_capsule == null)
            {
                return;
            }

            _capsule.height = height;
            _capsule.center = new Vector3(0f, height * 0.5f, 0f);
        }
    }
}
