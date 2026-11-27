using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Player
{
    public enum CameraMode
    {
        IntroCar = 0,
        IntroSwoop = 1,
        Gameplay = 2,
        WinShowcase = 3,
        LoseShowcase = 4
    }

    /// <summary>
    /// Over-the-shoulder orbit camera with wall collision (ignores loose clutter), sprint FOV kick, shake,
    /// and runtime cinematic modes (Menu car framing, wake-up swoop, Win/Lose showcases).
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

        [Header("Cinematic Transitions & Showcases")]
        [SerializeField] private float introSwoopDuration = 0.75f;
        [SerializeField] private float winShowcaseDistance = 2.45f;
        [SerializeField] private float winShowcaseHeight = 1.26f;
        [SerializeField] private float winShowcaseOrbitAmplitude = 15f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private PlayerController _controller;
        private Rigidbody _targetRb;
        private GoofyBody _goofyBody;
        private GameManager _subscribedGm;
        private Renderer[] _targetRenderers;
        private bool _targetHidden;
        private float _yaw;
        private float _pitch;
        private float _currentDistance;
        private float _shake;
        private float _extraFov;
        private Vector3 _pivot;
        private bool _initialized;

        // Cinematic mode state
        private float _modeTime;
        private float _swoopT;
        private Vector3 _swoopStartPos;
        private Vector3 _swoopStartLook;
        private float _swoopStartFov = 54f;
        private float _showcaseBaseYaw;
        private float _showcaseRadius = 2.1f;
        private float _showcaseTargetPlayerYaw;
        private RideCar _carRef;
        private Vector3 _loseCamAnchorPos;
        private Vector3 _loseFallbackLook;

        public const string MouseSensitivityPrefKey = "WMLS_MouseSensitivity";
        public const float DefaultMouseSensitivity = 0.12f;

        public Transform CameraTransform => cam != null ? cam.transform : transform;
        public Camera Camera => cam;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public CameraMode CurrentMode { get; private set; } = CameraMode.Gameplay;
        public bool IsTargetHidden => _targetHidden;

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
                _targetRb = target.GetComponent<Rigidbody>();
                if (input == null)
                {
                    input = target.GetComponent<PlayerInputHandler>();
                }

                _goofyBody = target.GetComponentInChildren<GoofyBody>(true);
                RefreshTargetRenderers();
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

            EnsureGameManagerSubscription();
            if (_subscribedGm != null)
            {
                HandleGameStateChanged(_subscribedGm.CurrentState);
            }
        }

        private void OnDestroy()
        {
            if (_subscribedGm != null)
            {
                _subscribedGm.OnGameStateChanged -= HandleGameStateChanged;
                _subscribedGm = null;
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

        private void EnsureGameManagerSubscription()
        {
            GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
            if (gm == null || _subscribedGm == gm) return;

            if (_subscribedGm != null)
            {
                _subscribedGm.OnGameStateChanged -= HandleGameStateChanged;
            }
            _subscribedGm = gm;
            _subscribedGm.OnGameStateChanged += HandleGameStateChanged;
        }

        private void HandleGameStateChanged(GameState state)
        {
            if (!_initialized) return;

            switch (state)
            {
                case GameState.Menu:
                    EnterIntroCar();
                    break;
                case GameState.Playing:
                    EnterIntroSwoop();
                    break;
                case GameState.Win:
                    EnterWinShowcase();
                    break;
                case GameState.Lose:
                    EnterLoseShowcase();
                    break;
            }
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

            EnsureGameManagerSubscription();

            switch (CurrentMode)
            {
                case CameraMode.IntroCar:
                    UpdateIntroCar();
                    break;
                case CameraMode.IntroSwoop:
                    UpdateIntroSwoop();
                    break;
                case CameraMode.WinShowcase:
                    UpdateWinShowcase();
                    break;
                case CameraMode.LoseShowcase:
                    UpdateLoseShowcase();
                    break;
                default:
                    UpdateGameplay();
                    break;
            }
        }

        // ───────────────────────────── Mode: Intro Car ─────────────────────────────

        private void EnterIntroCar()
        {
            CurrentMode = CameraMode.IntroCar;
            _modeTime = 0f;
            _carRef = ResolveRideCar();
            _goofyBody?.SetPoseMode(BodyPoseMode.Normal);
            SetTargetHidden(false);
            ApplyIntroCarPose(snap: true);
        }

        private void UpdateIntroCar()
        {
            SetTargetHidden(false);
            _modeTime += Time.unscaledDeltaTime;
            ApplyIntroCarPose(snap: false);
        }

        private void ApplyIntroCarPose(bool snap)
        {
            if (_carRef == null) _carRef = ResolveRideCar();
            if (_carRef == null)
            {
                UpdateGameplay();
                return;
            }

            Vector3 carPos = _carRef.transform.position;
            Vector3 carFwd = Vector3.ProjectOnPlane(_carRef.transform.forward, Vector3.up);
            if (carFwd.sqrMagnitude < 0.01f) carFwd = Vector3.left;
            carFwd.Normalize();
            Vector3 carRight = Vector3.Cross(Vector3.up, carFwd).normalized;

            Vector3 lookTarget = carPos + Vector3.up * 0.90f + carRight * 0.85f;
            float swayX = Mathf.Sin(Time.unscaledTime * 0.75f) * 0.14f;
            float swayY = Mathf.Sin(Time.unscaledTime * 1.15f) * 0.06f;

            Vector3 rawPos = carPos + carFwd * 4.8f - carRight * 3.8f + Vector3.up * (2.10f + swayY) + carFwd * swayX;
            Vector3 toCam = rawPos - lookTarget;
            float clearDist = CastDistance(lookTarget, toCam.normalized, toCam.magnitude, _carRef.transform);
            Vector3 finalPos = lookTarget + toCam.normalized * Mathf.Max(1.8f, clearDist - 0.1f);
            Quaternion finalRot = Quaternion.LookRotation((lookTarget - finalPos).normalized, Vector3.up);

            if (snap)
            {
                cam.transform.SetPositionAndRotation(finalPos, finalRot);
                cam.fieldOfView = 56f;
            }
            else
            {
                float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
                cam.transform.position = Vector3.Lerp(cam.transform.position, finalPos, k);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, finalRot, k);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 56f, k);
            }
        }

        // ───────────────────────────── Mode: Intro Swoop ─────────────────────────────

        private void EnterIntroSwoop()
        {
            if (target == null || cam == null)
            {
                CurrentMode = CameraMode.Gameplay;
                return;
            }

            CurrentMode = CameraMode.IntroSwoop;
            _modeTime = 0f;
            _swoopT = 0f;

            _subscribedGm?.SetIntroSwoopActive(true);
            _goofyBody?.SetPoseMode(BodyPoseMode.Normal);
            SetTargetHidden(false);

            _yaw = target.eulerAngles.y;
            _pitch = startPitch;
            _pivot = DesiredPivot();
            _currentDistance = distance;

            _swoopStartLook = target.position + Vector3.up * 1.18f;
            float[] candidateOffsets = { 28f, -28f, 0f, 55f, -55f };
            Vector3 bestDir = Quaternion.Euler(0f, 28f, 0f) * target.forward;
            float bestClear = 0f;

            for (int i = 0; i < candidateOffsets.Length; i++)
            {
                Vector3 candidate = (Quaternion.Euler(0f, candidateOffsets[i], 0f) * target.forward).normalized;
                float clear = CastDistance(_swoopStartLook, candidate, 1.85f);
                if (clear >= 1.25f)
                {
                    bestDir = candidate;
                    bestClear = clear;
                    break;
                }
                if (clear > bestClear)
                {
                    bestClear = clear;
                    bestDir = candidate;
                }
            }

            Vector3 desiredStartOffset = bestDir * Mathf.Clamp(bestClear - 0.12f, 0.85f, 1.75f) + Vector3.up * 0.16f;
            float startSafe = CastDistance(_swoopStartLook, desiredStartOffset.normalized, desiredStartOffset.magnitude);
            _swoopStartPos = _swoopStartLook + desiredStartOffset.normalized * Mathf.Max(0.8f, startSafe);
            _swoopStartFov = 54f;

            cam.transform.SetPositionAndRotation(_swoopStartPos, Quaternion.LookRotation((_swoopStartLook - _swoopStartPos).normalized, Vector3.up));
            cam.fieldOfView = _swoopStartFov;
        }

        private void UpdateIntroSwoop()
        {
            SetTargetHidden(false);
            if (_subscribedGm != null && _subscribedGm.IsPaused) return;

            float dt = Time.deltaTime;
            _swoopT = Mathf.Clamp01(_swoopT + dt / Mathf.Max(0.1f, introSwoopDuration));
            float e = 1f - Mathf.Pow(1f - _swoopT, 3f);

            _pivot = DesiredPivot();
            ComputeGameplayPose(_pivot, out Vector3 endPos, out Quaternion endRot, out float endDist);

            // Interpolate horizontally around the pivot so the camera sweeps cleanly past the shoulder without clipping through the head
            Vector3 startRel = _swoopStartPos - _pivot;
            Vector3 endRel = endPos - _pivot;
            Vector2 startXZ = new Vector2(startRel.x, startRel.z);
            Vector2 endXZ = new Vector2(endRel.x, endRel.z);

            float startYaw = Mathf.Atan2(startXZ.x, startXZ.y) * Mathf.Rad2Deg;
            float endYaw = Mathf.Atan2(endXZ.x, endXZ.y) * Mathf.Rad2Deg;
            float deltaYaw = Mathf.DeltaAngle(startYaw, endYaw);
            // Bias near-180 swings slightly toward shoulder side so it never cuts straight through center
            if (Mathf.Abs(Mathf.Abs(deltaYaw) - 180f) < 6f)
            {
                deltaYaw = 172f;
            }
            float curYaw = startYaw + deltaYaw * e;

            float curDistXZ = Mathf.Lerp(startXZ.magnitude, endXZ.magnitude, e);
            float curYOff = Mathf.Lerp(startRel.y, endRel.y, e);
            Vector3 rawOffset = (Quaternion.Euler(0f, curYaw, 0f) * Vector3.forward) * curDistXZ + Vector3.up * curYOff;

            float safeDist = CastDistance(_pivot, rawOffset.normalized, rawOffset.magnitude);
            Vector3 pos = _pivot + rawOffset.normalized * Mathf.Max(minDistance, safeDist);

            Quaternion lookAtBean = Quaternion.LookRotation((_swoopStartLook - pos).normalized, Vector3.up);
            Quaternion rot = Quaternion.Slerp(lookAtBean, endRot, Mathf.SmoothStep(0f, 1f, e));

            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = Mathf.Lerp(_swoopStartFov, baseFov, e);

            if (_swoopT >= 1f)
            {
                _currentDistance = endDist;
                CurrentMode = CameraMode.Gameplay;
                _subscribedGm?.SetIntroSwoopActive(false);
            }
        }

        // ───────────────────────────── Mode: Gameplay ─────────────────────────────

        private void UpdateGameplay()
        {
            if (_subscribedGm != null && _subscribedGm.IsPaused)
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

            ComputeGameplayPose(_pivot, out Vector3 pos, out Quaternion finalRot, out float wantedDist, smoothZoomOut: true, dt: dt);

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

        private void ComputeGameplayPose(Vector3 pivotPoint, out Vector3 pos, out Quaternion rot, out float resolvedDist, bool smoothZoomOut = false, float dt = 0f)
        {
            rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 right = rot * Vector3.right;
            Vector3 back = rot * Vector3.back;

            float shoulder = CastDistance(pivotPoint, right, shoulderOffset);
            Vector3 shoulderPos = pivotPoint + right * shoulder;
            float wanted = CastDistance(shoulderPos, back, distance);

            if (smoothZoomOut)
            {
                if (wanted < _currentDistance) _currentDistance = wanted;
                else _currentDistance = Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-zoomOutSharpness * dt));
                resolvedDist = Mathf.Max(minDistance, _currentDistance);
            }
            else
            {
                resolvedDist = Mathf.Max(minDistance, wanted);
            }

            pos = shoulderPos + back * resolvedDist;
        }

        // ───────────────────────────── Mode: Win Showcase ─────────────────────────────

        private void EnterWinShowcase()
        {
            CurrentMode = CameraMode.WinShowcase;
            _modeTime = 0f;
            RefreshTargetRenderers();
            SetTargetHidden(false);
            _goofyBody?.SetPoseMode(BodyPoseMode.Celebrate);
            FindBestShowcaseYaw(out _showcaseBaseYaw, out _showcaseRadius);
            _showcaseTargetPlayerYaw = _showcaseBaseYaw - 16f;
        }

        private void UpdateWinShowcase()
        {
            RefreshTargetRenderers();
            SetTargetHidden(false);

            float dt = Time.unscaledDeltaTime;
            _modeTime += dt;

            // Smoothly turn the pajama bean to face the camera in a front-3/4 celebratory hero pose
            Quaternion desiredPlayerRot = Quaternion.Euler(0f, _showcaseTargetPlayerYaw, 0f);
            Quaternion nextPlayerRot = Quaternion.Slerp(target.rotation, desiredPlayerRot, 1f - Mathf.Exp(-8f * dt));
            if (_targetRb != null)
            {
                _targetRb.angularVelocity = Vector3.zero;
                _targetRb.rotation = nextPlayerRot;
            }
            target.rotation = nextPlayerRot;

            Vector3 charCenter = target.position + Vector3.up * 0.92f;
            float orbitYaw = _showcaseBaseYaw + Mathf.Sin(_modeTime * 0.55f) * winShowcaseOrbitAmplitude;
            Vector3 flatDir = (Quaternion.Euler(0f, orbitYaw, 0f) * Vector3.forward).normalized;

            float camHeight = target.position.y + winShowcaseHeight;
            Vector3 rawOffset = flatDir * _showcaseRadius + Vector3.up * (camHeight - charCenter.y);
            float clearDist = CastDistance(charCenter, rawOffset.normalized, rawOffset.magnitude);
            Vector3 camPos = charCenter + rawOffset.normalized * Mathf.Max(1.25f, clearDist - 0.08f);

            // Shift look target rightward so the pajama bean sits cleanly in the left third of the viewport
            Vector3 lookForward = (charCenter - camPos).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, lookForward).normalized;
            Vector3 framedLook = charCenter + camRight * 0.44f;
            Quaternion camRot = Quaternion.LookRotation((framedLook - camPos).normalized, Vector3.up);

            float k = 1f - Mathf.Exp(-10f * dt);
            cam.transform.position = Vector3.Lerp(cam.transform.position, camPos, k);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, camRot, k);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 50f, k);
        }

        private void FindBestShowcaseYaw(out float bestYaw, out float bestRadius)
        {
            Vector3 charCenter = target.position + Vector3.up * 0.92f;
            float preferredFrontYaw = target.eulerAngles.y;
            bestYaw = preferredFrontYaw;
            bestRadius = winShowcaseDistance;
            float bestScore = -1f;

            for (int i = 0; i < 12; i++)
            {
                float candidateYaw = preferredFrontYaw + i * 30f;
                float minClear = float.MaxValue;
                float[] fan = { -15f, 0f, 15f };
                for (int f = 0; f < fan.Length; f++)
                {
                    Vector3 dir = (Quaternion.Euler(0f, candidateYaw + fan[f], 0f) * Vector3.forward).normalized;
                    Vector3 angledOffset = dir * winShowcaseDistance + Vector3.up * (winShowcaseHeight - 0.92f);
                    float c = CastDistance(charCenter, angledOffset.normalized, angledOffset.magnitude);
                    if (c < minClear) minClear = c;
                }

                float alignBonus = Mathf.Abs(Mathf.DeltaAngle(candidateYaw, preferredFrontYaw)) < 45f ? 0.15f : 0f;
                float score = minClear + alignBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestYaw = candidateYaw;
                    bestRadius = Mathf.Clamp(minClear - 0.12f, 1.35f, winShowcaseDistance);
                }
            }
        }

        // ───────────────────────────── Mode: Lose Showcase ─────────────────────────────

        private void EnterLoseShowcase()
        {
            CurrentMode = CameraMode.LoseShowcase;
            _modeTime = 0f;
            RefreshTargetRenderers();
            SetTargetHidden(false);
            _goofyBody?.SetPoseMode(BodyPoseMode.Defeat);

            _carRef = ResolveRideCar();
            if (_carRef != null)
            {
                Vector3 carPos = _carRef.transform.position;
                Vector3 carFwd = Vector3.ProjectOnPlane(_carRef.transform.forward, Vector3.up);
                if (carFwd.sqrMagnitude < 0.01f) carFwd = Vector3.left;
                carFwd.Normalize();
                Vector3 carRight = Vector3.Cross(Vector3.up, carFwd).normalized;

                Vector3 lookTarget = carPos + Vector3.up * 0.85f;
                Vector3 desiredPos = carPos + carFwd * 3.4f - carRight * 3.6f + Vector3.up * 1.85f;
                Vector3 toCam = desiredPos - lookTarget;
                float clear = CastDistance(lookTarget, toCam.normalized, toCam.magnitude, _carRef.transform);
                _loseCamAnchorPos = lookTarget + toCam.normalized * Mathf.Max(2.0f, clear - 0.1f);
            }
            else
            {
                FindBestShowcaseYaw(out _showcaseBaseYaw, out _showcaseRadius);
                Vector3 charCenter = target.position + Vector3.up * 0.72f;
                Vector3 flatDir = (Quaternion.Euler(0f, _showcaseBaseYaw, 0f) * Vector3.forward).normalized;
                _loseCamAnchorPos = charCenter + flatDir * _showcaseRadius + Vector3.up * 0.45f;
                _loseFallbackLook = charCenter;
            }
        }

        private void UpdateLoseShowcase()
        {
            SetTargetHidden(false);
            float dt = Time.unscaledDeltaTime;
            _modeTime += dt;

            Vector3 lookTarget = _carRef != null
                ? _carRef.transform.position + Vector3.up * 0.85f
                : (_loseFallbackLook != Vector3.zero ? _loseFallbackLook : target.position + Vector3.up * 0.72f);

            Vector3 lookDir = (lookTarget - _loseCamAnchorPos).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, lookDir).normalized;
            Vector3 framedLook = lookTarget + camRight * 0.65f;
            Quaternion desiredRot = Quaternion.LookRotation((framedLook - _loseCamAnchorPos).normalized, Vector3.up);

            float k = 1f - Mathf.Exp(-9f * dt);
            cam.transform.position = Vector3.Lerp(cam.transform.position, _loseCamAnchorPos, k);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, desiredRot, k);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 58f, k);
        }

        // ───────────────────────────── Input & Helpers ─────────────────────────────

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

        private float CastDistance(Vector3 origin, Vector3 dir, float maxDist, Transform extraIgnore = null)
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

                if (extraIgnore != null && hit.collider.transform.IsChildOf(extraIgnore))
                {
                    continue;
                }

                Rigidbody rb = hit.rigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    continue; // don't zoom in for pillows, boxes, or dynamic actors
                }

                if (hit.distance <= 0f)
                {
                    continue;
                }

                best = Mathf.Min(best, hit.distance);
            }

            return best;
        }

        private RideCar ResolveRideCar()
        {
            return RideCar.Instance != null ? RideCar.Instance : FindFirstObjectByType<RideCar>();
        }

        private void RefreshTargetRenderers()
        {
            if (target == null) return;
            GoofyBody body = _goofyBody != null ? _goofyBody : target.GetComponentInChildren<GoofyBody>(true);
            _targetRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : target.GetComponentsInChildren<Renderer>(true);
        }

        private void SetTargetHidden(bool hidden)
        {
            if (hidden == _targetHidden)
            {
                if (!hidden && _targetRenderers != null)
                {
                    // Ensure newly equipped runtime OutfitVisuals renderers are never left disabled
                    for (int i = 0; i < _targetRenderers.Length; i++)
                    {
                        if (_targetRenderers[i] != null && !_targetRenderers[i].enabled)
                        {
                            _targetRenderers[i].enabled = true;
                        }
                    }
                }
                return;
            }

            if (_targetRenderers == null) RefreshTargetRenderers();
            if (_targetRenderers == null) return;

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
