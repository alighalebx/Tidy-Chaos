using System;
using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// Marks a prop as grabbable/throwable and enforces Unity PhysX dynamic collider safety
    /// (BoxCollider or convex MeshCollider paired with a non-kinematic Rigidbody).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsClutter : MonoBehaviour
    {
        private const float MinColliderDimension = 0.05f;
        private const float DegenerateMeshThreshold = 0.001f;

        [Header("Prop Settings")]
        [Tooltip("Human-readable label used for hover prompts and debug UI.")]
        [SerializeField] private string propDisplayName = "Clutter";

        [Tooltip("Allow the player to pick up and throw this prop.")]
        [SerializeField] private bool isGrabbable = true;

        [Tooltip("Rigidbody mass in kg applied when collider safety runs.")]
        [Min(0.05f)]
        [SerializeField] private float mass = 1.5f;

        [Tooltip("Multiplier applied to the player's base throw impulse for this prop.")]
        [Min(0.1f)]
        [SerializeField] private float throwForceMultiplier = 1f;

        [Tooltip("Optional forward offset (meters) added to the carry hold distance for larger props.")]
        [SerializeField] private float holdDistanceOffset = 0f;

        [Tooltip("Locks rotation while held (useful for bulky boxes or furniture).")]
        [SerializeField] private bool freezeRotationWhileHeld = false;

        [Header("Collider Safety")]
        [Tooltip("Automatically enforces convex MeshColliders or generates a BoxCollider on Awake.")]
        [SerializeField] private bool enforceSafeCollidersOnAwake = true;

        [Tooltip("Adds a fitted BoxCollider if no solid collider exists on this object or its children.")]
        [SerializeField] private bool autoAddBoxColliderIfMissing = true;

        [Tooltip("Continuous Dynamic prevents fast-thrown props from tunneling through walls or floors.")]
        [SerializeField] private CollisionDetectionMode dynamicCollisionMode = CollisionDetectionMode.ContinuousDynamic;

        private Rigidbody _rigidbody;
        private Collider[] _solidColliders = Array.Empty<Collider>();

        private bool _originalUseGravity;
        private float _originalLinearDamping;
        private float _originalAngularDamping;
        private RigidbodyConstraints _originalConstraints;
        private bool _hasSavedPhysicsState;

        public Rigidbody Body => _rigidbody;
        public Collider[] Colliders => _solidColliders;
        public bool IsGrabbable => isGrabbable;
        public string DisplayName => string.IsNullOrEmpty(propDisplayName) ? gameObject.name : propDisplayName;
        public float ThrowForceMultiplier => throwForceMultiplier;
        public float HoldDistanceOffset => holdDistanceOffset;
        public bool FreezeRotationWhileHeld => freezeRotationWhileHeld;
        public bool IsCurrentlyHeld { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (enforceSafeCollidersOnAwake)
            {
                EnsureSafeDynamicColliders();
            }
            else
            {
                RefreshSolidColliders();
            }
        }

        private void Reset()
        {
            propDisplayName = gameObject.name;
            _rigidbody = GetComponent<Rigidbody>();
            EnsureSafeDynamicColliders();
        }

        private void OnValidate()
        {
            mass = Mathf.Max(0.05f, mass);
            throwForceMultiplier = Mathf.Max(0.1f, throwForceMultiplier);

            MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                MeshCollider mc = meshColliders[i];
                if (mc != null && !mc.convex && BelongsToThisBody(mc))
                {
                    Debug.LogWarning(
                        $"[PhysicsClutter] '{name}' has a non-convex MeshCollider on '{mc.name}'. " +
                        "Right-click PhysicsClutter -> 'Validate & Fix Dynamic Colliders' or enable Convex.",
                        mc);
                }
            }
        }

        /// <summary>
        /// Configures the Rigidbody for smooth dynamic throwing and guarantees all attached colliders
        /// are valid for dynamic PhysX bodies (no concave MeshColliders).
        /// </summary>
        [ContextMenu("Validate & Fix Dynamic Colliders")]
        public void EnsureSafeDynamicColliders()
        {
            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                _rigidbody.mass = Mathf.Max(0.05f, mass);
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                _rigidbody.collisionDetectionMode = dynamicCollisionMode;
            }

            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < allColliders.Length; i++)
            {
                Collider col = allColliders[i];
                if (col == null || !BelongsToThisBody(col))
                {
                    continue;
                }

                if (col is MeshCollider meshCollider)
                {
                    if (meshCollider.sharedMesh == null)
                    {
                        Debug.LogWarning(
                            $"[PhysicsClutter] MeshCollider on '{meshCollider.name}' has no sharedMesh assigned.",
                            meshCollider);
                        continue;
                    }

                    if (!meshCollider.convex)
                    {
                        meshCollider.convex = true;
                    }

                    Vector3 meshSize = meshCollider.sharedMesh.bounds.size;
                    if (meshSize.x < DegenerateMeshThreshold ||
                        meshSize.y < DegenerateMeshThreshold ||
                        meshSize.z < DegenerateMeshThreshold)
                    {
                        Debug.LogWarning(
                            $"[PhysicsClutter] MeshCollider on '{meshCollider.name}' has a near-flat axis ({meshSize}). " +
                            "Consider replacing it with a BoxCollider to avoid PhysX hull baking issues.",
                            meshCollider);
                    }
                }
            }

            RefreshSolidColliders();

            if (_solidColliders.Length == 0 && autoAddBoxColliderIfMissing)
            {
                AddFittedBoxCollider();
                RefreshSolidColliders();
            }
        }

        /// <summary>
        /// Prepares the Rigidbody for spring-damped carry physics.
        /// </summary>
        public void OnGrabbed(float heldLinearDamping, float heldAngularDamping)
        {
            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }

            if (_rigidbody == null)
            {
                return;
            }

            if (!IsCurrentlyHeld)
            {
                _originalUseGravity = _rigidbody.useGravity;
                _originalLinearDamping = _rigidbody.linearDamping;
                _originalAngularDamping = _rigidbody.angularDamping;
                _originalConstraints = _rigidbody.constraints;
                _hasSavedPhysicsState = true;
            }

            _rigidbody.useGravity = false;
            _rigidbody.linearDamping = heldLinearDamping;
            _rigidbody.angularDamping = heldAngularDamping;

            if (freezeRotationWhileHeld)
            {
                _rigidbody.constraints = _originalConstraints | RigidbodyConstraints.FreezeRotation;
            }

            _rigidbody.WakeUp();
            IsCurrentlyHeld = true;
        }

        /// <summary>
        /// Restores the Rigidbody's original gravity, damping, and rotation constraints after release or throw.
        /// </summary>
        public void OnReleased()
        {
            if (_rigidbody != null && _hasSavedPhysicsState)
            {
                _rigidbody.useGravity = _originalUseGravity;
                _rigidbody.linearDamping = _originalLinearDamping;
                _rigidbody.angularDamping = _originalAngularDamping;
                _rigidbody.constraints = _originalConstraints;
                _rigidbody.WakeUp();
            }

            _hasSavedPhysicsState = false;
            IsCurrentlyHeld = false;
        }

        private void RefreshSolidColliders()
        {
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            List<Collider> solidList = new List<Collider>(allColliders.Length);

            for (int i = 0; i < allColliders.Length; i++)
            {
                Collider col = allColliders[i];
                if (col == null || col.isTrigger || !BelongsToThisBody(col))
                {
                    continue;
                }

                if (col is MeshCollider mc && mc.sharedMesh == null)
                {
                    continue;
                }

                solidList.Add(col);
            }

            _solidColliders = solidList.ToArray();
        }

        private bool BelongsToThisBody(Collider col)
        {
            if (col.attachedRigidbody == null)
            {
                return col.GetComponentInParent<Rigidbody>() == _rigidbody;
            }

            return col.attachedRigidbody == _rigidbody;
        }

        private void AddFittedBoxCollider()
        {
            Bounds localBounds = ComputeLocalBounds();
            Vector3 size = new Vector3(
                Mathf.Max(localBounds.size.x, MinColliderDimension),
                Mathf.Max(localBounds.size.y, MinColliderDimension),
                Mathf.Max(localBounds.size.z, MinColliderDimension));

            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = size;
        }

        private Bounds ComputeLocalBounds()
        {
            bool hasBounds = false;
            Bounds combined = default;

            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                {
                    continue;
                }

                EncapsulateLocalBounds(mf.transform, mf.sharedMesh.bounds, ref combined, ref hasBounds);
            }

            if (hasBounds)
            {
                return combined;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                EncapsulateWorldBounds(r.bounds, ref combined, ref hasBounds);
            }

            return hasBounds ? combined : new Bounds(Vector3.zero, Vector3.one * 0.35f);
        }

        private void EncapsulateLocalBounds(Transform sourceTransform, Bounds sourceLocalBounds, ref Bounds target, ref bool hasBounds)
        {
            Vector3 center = sourceLocalBounds.center;
            Vector3 extents = sourceLocalBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                        Vector3 worldCorner = sourceTransform.TransformPoint(localCorner);
                        Vector3 selfLocalPoint = transform.InverseTransformPoint(worldCorner);
                        EncapsulatePoint(selfLocalPoint, ref target, ref hasBounds);
                    }
                }
            }
        }

        private void EncapsulateWorldBounds(Bounds worldBounds, ref Bounds target, ref bool hasBounds)
        {
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                        Vector3 selfLocalPoint = transform.InverseTransformPoint(worldCorner);
                        EncapsulatePoint(selfLocalPoint, ref target, ref hasBounds);
                    }
                }
            }
        }

        private static void EncapsulatePoint(Vector3 point, ref Bounds target, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                target = new Bounds(point, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                target.Encapsulate(point);
            }
        }
    }
}
