using System;
using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Player
{
    /// <summary>
    /// Snapshot of what the player's center-screen raycast is currently targeting.
    /// </summary>
    public struct InteractionHoverState : IEquatable<InteractionHoverState>
    {
        public InteractionCategory Category;
        public string Prompt;
        public GameObject Target;
        public bool IsHoldingObject;

        public InteractionHoverState(
            InteractionCategory category,
            string prompt,
            GameObject target,
            bool isHoldingObject)
        {
            Category = category;
            Prompt = prompt ?? string.Empty;
            Target = target;
            IsHoldingObject = isHoldingObject;
        }

        public bool Equals(InteractionHoverState other)
        {
            return Category == other.Category &&
                   IsHoldingObject == other.IsHoldingObject &&
                   Target == other.Target &&
                   string.Equals(Prompt, other.Prompt, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InteractionHoverState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Category;
                hash = (hash * 397) ^ IsHoldingObject.GetHashCode();
                hash = (hash * 397) ^ (Target != null ? Target.GetHashCode() : 0);
                hash = (hash * 397) ^ (Prompt != null ? Prompt.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// Handles center-screen raycast targeting, LMB hold-to-carry physics tracking with wall clearance,
    /// RMB high-impulse "Yeet" throwing, release-flick throwing, and E/LMB interactables.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class InteractionManager : MonoBehaviour
    {
        private struct PendingCollisionRestore
        {
            public PhysicsClutter Clutter;
            public float TimeRemaining;
        }

        [Header("References")]
        [Tooltip("Camera transform used as the raycast and throw origin (auto-resolved from PlayerController if unset).")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("Optional child transform placed in front of the camera for held props.")]
        [SerializeField] private Transform holdPoint = default;

        [Header("Raycast & Layers")]
        [Min(0.5f)]
        [SerializeField] private float interactionRange = 2.5f;
        [Tooltip("Small SphereCast assist radius used when the exact center ray misses thin/small props.")]
        [Min(0f)]
        [SerializeField] private float interactionAssistRadius = 0.06f;
        [SerializeField] private LayerMask interactionLayerMask = ~0;
        [SerializeField] private LayerMask obstructionLayerMask = ~0;

        [Header("Grab & Carry Physics")]
        [Tooltip("When true, hold LMB to carry and release LMB to drop. When false, LMB toggles grab.")]
        [SerializeField] private bool holdToCarry = true;
        [SerializeField] private float holdDistance = 1.75f;
        [SerializeField] private float minHoldDistance = 0.75f;
        [SerializeField] private float wallClearanceRadius = 0.18f;
        [SerializeField] private float holdFollowSpeed = 16f;
        [SerializeField] private float holdVelocitySmoothing = 22f;
        [SerializeField] private float maxCarrySpeed = 18f;
        [SerializeField] private float maxBreakDistance = 3.2f;
        [Tooltip("Grace period right after grabbing so pulling wide props toward HoldPoint doesn't trigger break distance.")]
        [Min(0f)]
        [SerializeField] private float grabBreakGraceTime = 0.35f;
        [SerializeField] private float heldLinearDamping = 4f;
        [SerializeField] private float heldAngularDamping = 6f;
        [SerializeField] private bool alignHeldRotation = false;
        [SerializeField] private float rotationAlignSpeed = 8f;

        [Header("Throw (Yeet) & Release Flick")]
        [SerializeField] private float throwForce = 14f;
        [SerializeField] private float throwUpwardArc = 0.18f;
        [SerializeField] private float throwSpinTorque = 3.5f;
        [Range(0f, 1f)]
        [SerializeField] private float playerVelocityInheritFactor = 0.35f;
        [Range(0f, 1.5f)]
        [SerializeField] private float releaseFlickMultiplier = 0.75f;
        [SerializeField] private float maxFlickSpeed = 10f;
        [SerializeField] private float reEnablePlayerCollisionDelay = 0.15f;

        public event Action<InteractionHoverState> OnHoverStateChanged;
        public event Action<PhysicsClutter> OnObjectGrabbed;
        public event Action<PhysicsClutter, bool> OnObjectReleased;

        private readonly RaycastHit[] _raycastHits = new RaycastHit[16];
        private readonly RaycastHit[] _sphereCastHits = new RaycastHit[16];
        private readonly List<PendingCollisionRestore> _pendingRestores = new List<PendingCollisionRestore>(8);

        private PlayerInputHandler _input;
        private PlayerController _playerController;
        private Collider[] _playerColliders = Array.Empty<Collider>();

        private PhysicsClutter _heldClutter;
        private PhysicsClutter _hoveredClutter;
        private IInteractable _hoveredInteractable;
        private InteractionHoverState _currentHover;
        private Vector3 _smoothedCarryVelocity;
        private float _grabGraceTimer;

        public bool IsHoldingObject => _heldClutter != null;
        public PhysicsClutter HeldClutter => _heldClutter;
        public Rigidbody HeldBody => _heldClutter != null ? _heldClutter.Body : null;
        public InteractionHoverState CurrentHoverState => _currentHover;

        private void Awake()
        {
            _input = GetComponent<PlayerInputHandler>();
            _playerController = GetComponent<PlayerController>();
            _playerColliders = GetComponentsInChildren<Collider>(true);

            ResolveCameraTransform();
        }

        private void Start()
        {
            if (cameraTransform != null && Vector3.Distance(cameraTransform.position, transform.position) > 2.5f)
            {
                Debug.LogWarning(
                    $"[InteractionManager] '{cameraTransform.name}' is {Vector3.Distance(cameraTransform.position, transform.position):F2}m away from '{name}'. " +
                    "Ensure Main Camera Local Position is (0, 0, 0) under CameraRoot (not left at z = -10).",
                    cameraTransform);
            }
        }

        private void OnDisable()
        {
            if (_heldClutter != null)
            {
                ForceDropHeldObject();
            }

            FlushPendingCollisionRestores();
            SetHoverState(default);
        }

        private void Update()
        {
            TickPendingCollisionRestores();

            if (cameraTransform == null)
            {
                ResolveCameraTransform();
                if (cameraTransform == null)
                {
                    return;
                }
            }

            UpdateHoverTarget();
            HandleInteractionInput();
        }

        private void FixedUpdate()
        {
            if (_heldClutter == null || cameraTransform == null)
            {
                return;
            }

            Rigidbody body = _heldClutter.Body;
            if (body == null)
            {
                _heldClutter = null;
                UpdateHoverTarget();
                return;
            }

            Vector3 targetPos = ComputeWallClearedHoldPosition();
            Vector3 toTarget = targetPos - body.worldCenterOfMass;

            if (_grabGraceTimer > 0f)
            {
                _grabGraceTimer -= Time.fixedDeltaTime;
            }
            else if (toTarget.magnitude > maxBreakDistance)
            {
                ReleaseHeldObject(wasThrown: false);
                return;
            }

            Vector3 desiredVelocity = Vector3.ClampMagnitude(toTarget * holdFollowSpeed, maxCarrySpeed);
            float smoothT = 1f - Mathf.Exp(-holdVelocitySmoothing * Time.fixedDeltaTime);
            body.linearVelocity = Vector3.Lerp(body.linearVelocity, desiredVelocity, smoothT);
            _smoothedCarryVelocity = Vector3.Lerp(_smoothedCarryVelocity, body.linearVelocity, smoothT);

            if (alignHeldRotation && !_heldClutter.FreezeRotationWhileHeld)
            {
                Vector3 flatForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (flatForward.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
                    float rotT = 1f - Mathf.Exp(-rotationAlignSpeed * Time.fixedDeltaTime);
                    body.MoveRotation(Quaternion.Slerp(body.rotation, targetRot, rotT));
                    body.angularVelocity = Vector3.Lerp(body.angularVelocity, Vector3.zero, smoothT);
                }
            }
        }

        private void OnValidate()
        {
            interactionRange = Mathf.Max(0.5f, interactionRange);
            interactionAssistRadius = Mathf.Max(0f, interactionAssistRadius);
            minHoldDistance = Mathf.Max(0.25f, minHoldDistance);
            holdDistance = Mathf.Max(minHoldDistance, holdDistance);
            wallClearanceRadius = Mathf.Max(0.05f, wallClearanceRadius);
            maxBreakDistance = Mathf.Max(0.5f, maxBreakDistance);
            grabBreakGraceTime = Mathf.Max(0f, grabBreakGraceTime);
            reEnablePlayerCollisionDelay = Mathf.Max(0f, reEnablePlayerCollisionDelay);
        }

        private void OnDrawGizmosSelected()
        {
            Transform originTransform = cameraTransform;
            if (originTransform == null)
            {
                Camera childCam = GetComponentInChildren<Camera>(true);
                originTransform = childCam != null ? childCam.transform : (Camera.main != null ? Camera.main.transform : null);
            }

            if (originTransform == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Vector3 start = originTransform.position;
            Gizmos.DrawLine(start, start + originTransform.forward * interactionRange);

            Gizmos.color = Color.yellow;
            Vector3 previewHold = holdPoint != null
                ? holdPoint.position
                : start + originTransform.forward * holdDistance;
            Gizmos.DrawWireSphere(previewHold, wallClearanceRadius);
        }

        /// <summary>
        /// Forces the currently held prop to drop immediately without flick velocity (e.g., on end-of-run).
        /// </summary>
        public void ForceDropHeldObject()
        {
            if (_heldClutter == null)
            {
                return;
            }

            PhysicsClutter clutter = _heldClutter;
            _heldClutter = null;
            _smoothedCarryVelocity = Vector3.zero;
            clutter.OnReleased();
            ScheduleCollisionRestore(clutter);
            OnObjectReleased?.Invoke(clutter, false);
            UpdateHoverTarget();
        }

        private void ResolveCameraTransform()
        {
            if (cameraTransform != null)
            {
                return;
            }

            if (_playerController != null && _playerController.CameraTransform != null)
            {
                cameraTransform = _playerController.CameraTransform;
                return;
            }

            Camera childCam = GetComponentInChildren<Camera>(true);
            if (childCam != null)
            {
                cameraTransform = childCam.transform;
            }
        }

        private void UpdateHoverTarget()
        {
            if (_heldClutter != null)
            {
                _hoveredInteractable = null;
                _hoveredClutter = null;
                SetHoverState(new InteractionHoverState(
                    InteractionCategory.Grabbable,
                    _heldClutter.DisplayName,
                    _heldClutter.gameObject,
                    isHoldingObject: true));
                return;
            }

            _hoveredInteractable = null;
            _hoveredClutter = null;

            int hitCount = Physics.RaycastNonAlloc(
                cameraTransform.position,
                cameraTransform.forward,
                _raycastHits,
                interactionRange,
                interactionLayerMask,
                QueryTriggerInteraction.Ignore);

            Collider closestCollider = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _raycastHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closestCollider = hit.collider;
                }
            }

            if (closestCollider != null && TryResolveTarget(closestCollider))
            {
                return;
            }

            if (interactionAssistRadius > 0.001f)
            {
                float assistMaxDistance = Mathf.Min(interactionRange, closestDistance);
                int assistHitCount = Physics.SphereCastNonAlloc(
                    cameraTransform.position,
                    interactionAssistRadius,
                    cameraTransform.forward,
                    _sphereCastHits,
                    assistMaxDistance,
                    interactionLayerMask,
                    QueryTriggerInteraction.Ignore);

                Collider bestAssistCollider = null;
                float bestAssistDistance = float.MaxValue;

                for (int i = 0; i < assistHitCount; i++)
                {
                    RaycastHit hit = _sphereCastHits[i];
                    if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (!HasValidTarget(hit.collider))
                    {
                        continue;
                    }

                    if (hit.distance < bestAssistDistance)
                    {
                        bestAssistDistance = hit.distance;
                        bestAssistCollider = hit.collider;
                    }
                }

                if (bestAssistCollider != null && TryResolveTarget(bestAssistCollider))
                {
                    return;
                }
            }

            SetHoverState(default);
        }

        private bool HasValidTarget(Collider col)
        {
            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract)
            {
                return true;
            }

            PhysicsClutter clutter = col.GetComponentInParent<PhysicsClutter>();
            return clutter != null && clutter.IsGrabbable;
        }

        private bool TryResolveTarget(Collider col)
        {
            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract)
            {
                _hoveredInteractable = interactable;
                GameObject targetObj = (interactable is Component comp) ? comp.gameObject : col.gameObject;
                SetHoverState(new InteractionHoverState(
                    interactable.Category,
                    interactable.InteractionPrompt,
                    targetObj,
                    isHoldingObject: false));
                return true;
            }

            PhysicsClutter clutter = col.GetComponentInParent<PhysicsClutter>();
            if (clutter != null && clutter.IsGrabbable)
            {
                _hoveredClutter = clutter;
                SetHoverState(new InteractionHoverState(
                    InteractionCategory.Grabbable,
                    clutter.DisplayName,
                    clutter.gameObject,
                    isHoldingObject: false));
                return true;
            }

            return false;
        }

        private void HandleInteractionInput()
        {
            if (_input == null || !_input.IsGameplayInputEnabled)
            {
                return;
            }

            if (_heldClutter != null)
            {
                if (_input.ThrowPressedThisFrame)
                {
                    ThrowHeldObject();
                    return;
                }

                bool shouldRelease = holdToCarry
                    ? (_input.GrabReleasedThisFrame || !_input.IsGrabHeld)
                    : _input.GrabPressedThisFrame;

                if (shouldRelease)
                {
                    ReleaseHeldObject(wasThrown: false);
                }

                return;
            }

            if (_input.InteractPressedThisFrame && _hoveredInteractable != null && _hoveredInteractable.CanInteract)
            {
                _hoveredInteractable.Interact(gameObject);
                UpdateHoverTarget();
                return;
            }

            if (_input.GrabPressedThisFrame)
            {
                if (_hoveredInteractable != null &&
                    _hoveredInteractable.CanInteract &&
                    _hoveredInteractable.Category == InteractionCategory.Collectible)
                {
                    _hoveredInteractable.Interact(gameObject);
                    UpdateHoverTarget();
                    return;
                }

                if (_hoveredClutter != null && _hoveredClutter.IsGrabbable)
                {
                    GrabObject(_hoveredClutter);
                }
            }
        }

        private void GrabObject(PhysicsClutter clutter)
        {
            if (clutter == null || clutter.Body == null)
            {
                return;
            }

            CancelPendingCollisionRestore(clutter);
            _heldClutter = clutter;
            _grabGraceTimer = grabBreakGraceTime;
            _smoothedCarryVelocity = Vector3.zero;
            _heldClutter.OnGrabbed(heldLinearDamping, heldAngularDamping);
            SetPlayerCollisionIgnored(_heldClutter, ignored: true);

            OnObjectGrabbed?.Invoke(_heldClutter);
            UpdateHoverTarget();
        }

        private void ReleaseHeldObject(bool wasThrown)
        {
            if (_heldClutter == null)
            {
                return;
            }

            PhysicsClutter clutter = _heldClutter;
            Rigidbody body = clutter.Body;
            _heldClutter = null;

            clutter.OnReleased();
            ScheduleCollisionRestore(clutter);

            if (!wasThrown && body != null)
            {
                Vector3 flickVelocity = Vector3.ClampMagnitude(
                    _smoothedCarryVelocity * releaseFlickMultiplier,
                    maxFlickSpeed);
                body.linearVelocity = flickVelocity;
            }

            _smoothedCarryVelocity = Vector3.zero;
            OnObjectReleased?.Invoke(clutter, wasThrown);
            UpdateHoverTarget();
        }

        private void ThrowHeldObject()
        {
            if (_heldClutter == null)
            {
                return;
            }

            PhysicsClutter clutter = _heldClutter;
            Rigidbody body = clutter.Body;
            float throwMultiplier = clutter.ThrowForceMultiplier;
            _heldClutter = null;
            _smoothedCarryVelocity = Vector3.zero;

            clutter.OnReleased();
            ScheduleCollisionRestore(clutter);

            if (body != null)
            {
                body.WakeUp();

                Vector3 inheritedVel = _playerController != null
                    ? _playerController.Velocity * playerVelocityInheritFactor
                    : Vector3.zero;
                body.linearVelocity = new Vector3(inheritedVel.x, Mathf.Max(0f, inheritedVel.y), inheritedVel.z);

                Vector3 throwDir = (cameraTransform.forward + Vector3.up * throwUpwardArc).normalized;
                body.AddForce(throwDir * (throwForce * throwMultiplier), ForceMode.Impulse);

                if (throwSpinTorque > 0f)
                {
                    body.AddTorque(UnityEngine.Random.insideUnitSphere * throwSpinTorque, ForceMode.Impulse);
                }
            }

            OnObjectReleased?.Invoke(clutter, true);
            UpdateHoverTarget();
        }

        private Vector3 ComputeWallClearedHoldPosition()
        {
            Vector3 origin = cameraTransform.position;
            Vector3 idealTarget = holdPoint != null
                ? holdPoint.position + cameraTransform.forward * _heldClutter.HoldDistanceOffset
                : origin + cameraTransform.forward * Mathf.Max(minHoldDistance, holdDistance + _heldClutter.HoldDistanceOffset);

            Vector3 toIdeal = idealTarget - origin;
            float idealDistance = toIdeal.magnitude;
            if (idealDistance <= 0.001f)
            {
                return idealTarget;
            }

            Vector3 dir = toIdeal / idealDistance;
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                wallClearanceRadius,
                dir,
                _sphereCastHits,
                idealDistance,
                obstructionLayerMask,
                QueryTriggerInteraction.Ignore);

            float closestObstruction = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _sphereCastHits[i];
                if (hit.collider == null || IsPlayerOrHeldCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < closestObstruction)
                {
                    closestObstruction = hit.distance;
                }
            }

            if (closestObstruction < float.MaxValue)
            {
                float safeDistance = Mathf.Max(minHoldDistance, closestObstruction - wallClearanceRadius * 0.5f);
                return origin + dir * safeDistance;
            }

            return idealTarget;
        }

        private bool IsPlayerOrHeldCollider(Collider col)
        {
            if (col.transform.IsChildOf(transform))
            {
                return true;
            }

            if (_heldClutter != null && col.attachedRigidbody == _heldClutter.Body)
            {
                return true;
            }

            return false;
        }

        private void SetPlayerCollisionIgnored(PhysicsClutter clutter, bool ignored)
        {
            if (clutter == null || clutter.Colliders == null)
            {
                return;
            }

            Collider[] propColliders = clutter.Colliders;
            for (int p = 0; p < _playerColliders.Length; p++)
            {
                Collider playerCol = _playerColliders[p];
                if (playerCol == null)
                {
                    continue;
                }

                for (int c = 0; c < propColliders.Length; c++)
                {
                    Collider propCol = propColliders[c];
                    if (propCol == null || playerCol == propCol)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(playerCol, propCol, ignored);
                }
            }
        }

        private void ScheduleCollisionRestore(PhysicsClutter clutter)
        {
            if (clutter == null)
            {
                return;
            }

            if (reEnablePlayerCollisionDelay <= 0f)
            {
                SetPlayerCollisionIgnored(clutter, ignored: false);
                return;
            }

            CancelPendingCollisionRestore(clutter);
            _pendingRestores.Add(new PendingCollisionRestore
            {
                Clutter = clutter,
                TimeRemaining = reEnablePlayerCollisionDelay
            });
        }

        private void CancelPendingCollisionRestore(PhysicsClutter clutter)
        {
            for (int i = _pendingRestores.Count - 1; i >= 0; i--)
            {
                if (_pendingRestores[i].Clutter == clutter)
                {
                    _pendingRestores.RemoveAt(i);
                }
            }
        }

        private void TickPendingCollisionRestores()
        {
            for (int i = _pendingRestores.Count - 1; i >= 0; i--)
            {
                PendingCollisionRestore entry = _pendingRestores[i];
                entry.TimeRemaining -= Time.deltaTime;

                if (entry.TimeRemaining <= 0f)
                {
                    SetPlayerCollisionIgnored(entry.Clutter, ignored: false);
                    _pendingRestores.RemoveAt(i);
                }
                else
                {
                    _pendingRestores[i] = entry;
                }
            }
        }

        private void FlushPendingCollisionRestores()
        {
            for (int i = 0; i < _pendingRestores.Count; i++)
            {
                SetPlayerCollisionIgnored(_pendingRestores[i].Clutter, ignored: false);
            }

            _pendingRestores.Clear();
        }

        private void SetHoverState(InteractionHoverState nextHover)
        {
            if (_currentHover.Equals(nextHover))
            {
                return;
            }

            _currentHover = nextHover;
            OnHoverStateChanged?.Invoke(_currentHover);
        }
    }
}
