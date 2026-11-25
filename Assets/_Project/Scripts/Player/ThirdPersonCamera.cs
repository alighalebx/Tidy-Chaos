using UnityEngine;

namespace WheresMyLeftSock.Player
{
    /// <summary>
    /// Over-the-shoulder orbit camera with wall collision (ignores loose clutter), sprint FOV kick and shake.
    /// Lives under the Player prefab for convenience and detaches itself at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform target = default;
        [SerializeField] private Camera cam = default;
        [SerializeField] private PlayerInputHandler input = default;

        [Header("Framing")]
        [SerializeField] private float pivotHeight = 1.45f;
        [SerializeField] private float crouchPivotHeight = 0.95f;
        [SerializeField] private float shoulderOffset = 0.35f;
        [SerializeField] private float distance = 3.1f;
        [SerializeField] private float minDistance = 0.35f;
        [SerializeField] private float collisionRadius = 0.18f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float followSharpness = 14f;
        [SerializeField] private float verticalFollowSharpness = 7f;
        [SerializeField] private float zoomOutSharpness = 5f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float gamepadLookSpeed = 160f;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float startPitch = 12f;

        [Header("Juice")]
        [SerializeField] private float baseFov = 65f;
        [SerializeField] private float sprintFovBoost = 9f;
        [SerializeField] private float fovSharpness = 6f;
        [SerializeField] private float shakeDecay = 2.5f;
        [SerializeField] private float shakeFrequency = 24f;
        [SerializeField] private float shakeMaxAngle = 4f;
        [SerializeField] private float hideTargetWithin = 0.65f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private PlayerController _controller;
        private Renderer[] _targetRenderers;
        private bool _targetHidden;
        private float _yaw;
        private float _pitch;
        private float _currentDistance;
        private float _shake;
        private float _extraFov;
        private Vector3 _pivot;
        private bool _initialized;

        public const string MouseSensitivityPrefKey = "WMLS_MouseSensitivity";
        public const float DefaultMouseSensitivity = 0.12f;

        public Transform CameraTransform => cam != null ? cam.transform : transform;
        public Camera Camera => cam;
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public float MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = Mathf.Clamp(value, 0.02f, 0.5f);
        }

        private void Awake()
        {
            if (PlayerPrefs.HasKey(MouseSensitivityPrefKey))
            {
                MouseSensitivity = PlayerPrefs.GetFloat(MouseSensitivityPrefKey, mouseSensitivity);
            }

            if (cam == null)
            {
                cam = GetComponentInChildren<Camera>(true);
            }

            // Post FX on (volume lives in the scene: PostFX_SeptemberMorning)
            if (cam != null)
            {
                var camData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                camData.renderPostProcessing = true;
                camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            if (target == null && transform.parent != null)
            {
                target = transform.parent;
            }
        }

        private void Start()
        {
            transform.SetParent(null, true);
            if (target != null)
            {
                _controller = target.GetComponent<PlayerController>();
                if (input == null)
                {
                    input = target.GetComponent<PlayerInputHandler>();
                }

                GoofyBody body = target.GetComponentInChildren<GoofyBody>();
                _targetRenderers = body != null ? body.GetComponentsInChildren<Renderer>() : target.GetComponentsInChildren<Renderer>();
                _yaw = target.eulerAngles.y;
            }

            _pitch = startPitch;
            _pivot = DesiredPivot();
            _currentDistance = distance;
            _initialized = true;
            if (cam != null)
            {
                cam.fieldOfView = baseFov;
            }
        }

        public void SetYawPitch(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        public void AddShake(float amount)
        {
            _shake = Mathf.Clamp01(_shake + amount);
        }

        /// <summary>Extra FOV on top of base/sprint (e.g. panic at low time).</summary>
        public void SetExtraFov(float degrees)
        {
            _extraFov = degrees;
        }

        private Vector3 DesiredPivot()
        {
            float crouch = _controller != null ? _controller.CrouchBlend : 0f;
            return target.position + Vector3.up * Mathf.Lerp(pivotHeight, crouchPivotHeight, crouch);
        }

        private void LateUpdate()
        {
            if (!_initialized || target == null || cam == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            HandleLook(dt);

            // smooth pivot (less vertical follow hides the waddle bob)
            Vector3 desired = DesiredPivot();
            float h = 1f - Mathf.Exp(-followSharpness * dt);
            float vt = 1f - Mathf.Exp(-verticalFollowSharpness * dt);
            _pivot = new Vector3(Mathf.Lerp(_pivot.x, desired.x, h), Mathf.Lerp(_pivot.y, desired.y, vt), Mathf.Lerp(_pivot.z, desired.z, h));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 right = rot * Vector3.right;
            Vector3 back = rot * Vector3.back;

            // pivot -> shoulder, then shoulder -> back
            float shoulder = CastDistance(_pivot, right, shoulderOffset);
            Vector3 shoulderPos = _pivot + right * shoulder;
            float wanted = CastDistance(shoulderPos, back, distance);
            if (wanted < _currentDistance)
            {
                _currentDistance = wanted;
            }
            else
            {
                _currentDistance = Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-zoomOutSharpness * dt));
            }

            _currentDistance = Mathf.Max(minDistance, _currentDistance);

            Vector3 pos = shoulderPos + back * _currentDistance;
            Quaternion finalRot = rot;
            if (_shake > 0.001f)
            {
                float t = Time.time * shakeFrequency;
                float a = _shake * _shake * shakeMaxAngle;
                finalRot *= Quaternion.Euler((Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * a, (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * a,
                    (Mathf.PerlinNoise(t, t) - 0.5f) * 2f * a);
                _shake = Mathf.Max(0f, _shake - shakeDecay * dt);
            }

            cam.transform.SetPositionAndRotation(pos, finalRot);

            bool sprinting = _controller != null && _controller.IsSprinting && _controller.CurrentHorizontalSpeed > 4f;
            float fov = baseFov + (sprinting ? sprintFovBoost : 0f) + _extraFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-fovSharpness * dt));

            SetTargetHidden(_currentDistance < hideTargetWithin);
        }

        private void HandleLook(float dt)
        {
            if (input == null)
            {
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked && input.IsGameplayInputEnabled)
            {
                if (input.GrabPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                if (input.IsLookUsingPointer)
                {
                    return;
                }
            }

            Vector2 look = input.LookInput;
            if (look.sqrMagnitude < 0.000001f)
            {
                return;
            }

            if (input.IsLookUsingPointer)
            {
                _yaw += look.x * mouseSensitivity;
                _pitch -= look.y * mouseSensitivity;
            }
            else
            {
                _yaw += look.x * gamepadLookSpeed * dt;
                _pitch -= look.y * gamepadLookSpeed * dt;
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private float CastDistance(Vector3 origin, Vector3 dir, float maxDist)
        {
            int count = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, _hits, maxDist, collisionMask, QueryTriggerInteraction.Ignore);
            float best = maxDist;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(target))
                {
                    continue;
                }

                Rigidbody rb = hit.rigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    continue; // don't zoom in for pillows and boxes
                }

                if (hit.distance <= 0f)
                {
                    continue;
                }

                best = Mathf.Min(best, hit.distance);
            }

            return best;
        }

        private void SetTargetHidden(bool hidden)
        {
            if (hidden == _targetHidden || _targetRenderers == null)
            {
                return;
            }

            _targetHidden = hidden;
            for (int i = 0; i < _targetRenderers.Length; i++)
            {
                if (_targetRenderers[i] != null)
                {
                    _targetRenderers[i].enabled = !hidden;
                }
            }
        }
    }
}
