using UnityEngine;

namespace WheresMyLeftSock.Player
{
    /// <summary>
    /// Procedural spring-driven wobble for the pajama bean: leans into acceleration, waddles, flails its arms
    /// when sprinting in a panic, squashes on landing, jiggles its head and faceplants on knockdowns.
    /// Purely visual; all gameplay physics lives on PlayerController's Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public class GoofyBody : MonoBehaviour
    {
        [Header("Rig")]
        public Transform hips;
        public Transform head;
        public Transform armL;
        public Transform armR;
        public Transform legL;
        public Transform legR;
        public Transform[] eyes;

        [Header("Sources")]
        [SerializeField] private PlayerController controller = default;
        [SerializeField] private InteractionManager interaction = default;

        [Header("Lean")]
        [SerializeField] private float leanPerAccel = 1.15f;
        [SerializeField] private float leanPerSpeed = 1.25f;
        [SerializeField] private float maxLean = 24f;
        [SerializeField] private float leanFrequency = 2.6f;
        [SerializeField] private float leanDamping = 0.3f;

        [Header("Waddle")]
        [SerializeField] private float stepsPerMeter = 1.4f;
        [SerializeField] private float waddleRoll = 6.5f;
        [SerializeField] private float waddleBob = 0.045f;
        [SerializeField] private float legSwing = 40f;
        [SerializeField] private float armSwing = 50f;

        [Header("Panic Sprint")]
        [SerializeField] private float flailAngle = 42f;
        [SerializeField] private float flailRate = 2.2f;

        [Header("Jiggle")]
        [SerializeField] private float headFrequency = 3.5f;
        [SerializeField] private float headDamping = 0.25f;
        [SerializeField] private float headLagPerAccel = 0.009f;
        [SerializeField] private float squashFrequency = 4.5f;
        [SerializeField] private float squashDamping = 0.32f;
        [SerializeField] private float limbFrequency = 4f;
        [SerializeField] private float limbDamping = 0.3f;

        [Header("Knockdown")]
        [SerializeField] private float knockFrequency = 2.2f;
        [SerializeField] private float knockDamping = 0.35f;

        [Header("Blink")]
        [SerializeField] private float blinkInterval = 3.2f;

        private struct Spring
        {
            public float X, V;

            public void Step(float target, float freq, float zeta, float dt)
            {
                float w = 2f * Mathf.PI * freq;
                V += (w * w * (target - X) - 2f * zeta * w * V) * dt;
                X += V * dt;
            }
        }

        private struct Spring3
        {
            public Vector3 X, V;

            public void Step(Vector3 target, float freq, float zeta, float dt)
            {
                float w = 2f * Mathf.PI * freq;
                V += (w * w * (target - X) - 2f * zeta * w * V) * dt;
                X += V * dt;
            }
        }

        private Spring _leanPitch, _leanRoll, _squash, _knock;
        private Spring3 _headOffset, _armL, _armR, _legL, _legR;
        private Vector3 _headRest;
        private Vector3[] _eyeRestScale;
        private float _phase;
        private float _blinkTimer;
        private float _blinkT = 1f;

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<PlayerController>();
            }

            if (interaction == null)
            {
                interaction = GetComponentInParent<InteractionManager>();
            }

            if (head != null)
            {
                _headRest = head.localPosition;
            }

            if (eyes != null)
            {
                _eyeRestScale = new Vector3[eyes.Length];
                for (int i = 0; i < eyes.Length; i++)
                {
                    _eyeRestScale[i] = eyes[i] != null ? eyes[i].localScale : Vector3.one;
                }
            }

            _blinkTimer = blinkInterval * Random.Range(0.5f, 1.5f);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.Jumped += OnJumped;
                controller.Landed += OnLanded;
                controller.KnockedDown += OnKnockedDown;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.Jumped -= OnJumped;
                controller.Landed -= OnLanded;
                controller.KnockedDown -= OnKnockedDown;
            }
        }

        private void OnJumped()
        {
            _squash.V -= 3.5f; // stretch
        }

        private void OnLanded(float impactSpeed)
        {
            _squash.V += Mathf.Clamp(impactSpeed * 0.45f, 0.5f, 4f);
        }

        private void OnKnockedDown()
        {
            _squash.V += 2f;
            _headOffset.V += new Vector3(0f, 0.6f, 0f);
        }

        private void Update()
        {
            if (controller == null || hips == null)
            {
                return;
            }

            float frameDt = Mathf.Min(Time.deltaTime, 0.1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(frameDt / 0.008f));
            float dt = frameDt / steps;

            Vector3 vel = controller.Velocity;
            Vector3 planar = new Vector3(vel.x, 0f, vel.z);
            float speed = planar.magnitude;
            Quaternion inv = Quaternion.Inverse(controller.transform.rotation);
            Vector3 la = inv * controller.PlanarAcceleration;
            Vector3 lv = inv * planar;
            bool grounded = controller.IsGrounded;
            bool knocked = controller.IsKnockedDown;
            bool sprint = controller.IsSprinting && speed > 3f;
            bool holding = interaction != null && interaction.IsHoldingObject;
            float crouch = controller.CrouchBlend;
            float move01 = Mathf.Clamp01(speed / 4.5f);

            if (grounded && speed > 0.15f)
            {
                _phase += speed * stepsPerMeter * Mathf.PI * frameDt;
            }

            float s1 = Mathf.Sin(_phase);

            // targets
            float tPitch = Mathf.Clamp(la.z * leanPerAccel + lv.z * leanPerSpeed * (sprint ? 1.7f : 1f), -maxLean, maxLean);
            float tRoll = Mathf.Clamp(-la.x * leanPerAccel, -maxLean, maxLean);
            if (!grounded)
            {
                tPitch *= 0.35f;
            }

            if (controller.IsSkidding)
            {
                tPitch -= 18f; // lean back hard while skidding
            }

            Vector3 tHead = Vector3.ClampMagnitude(new Vector3(-la.x, 0f, -la.z) * headLagPerAccel, 0.12f);

            Vector3 tAL, tAR, tLL, tLR;
            float flail = Time.time * flailRate * Mathf.PI * 2f;
            if (knocked)
            {
                tAL = new Vector3(-30f, 0f, -85f);
                tAR = new Vector3(-30f, 0f, 85f);
                tLL = new Vector3(10f, 0f, -20f);
                tLR = new Vector3(-10f, 0f, 20f);
            }
            else if (!grounded)
            {
                tAL = new Vector3(-25f + Mathf.Sin(flail) * 20f, 0f, -135f);
                tAR = new Vector3(-25f - Mathf.Sin(flail) * 20f, 0f, 135f);
                tLL = new Vector3(-30f, 0f, -10f);
                tLR = new Vector3(20f, 0f, 10f);
            }
            else
            {
                tLL = new Vector3(-s1 * legSwing * move01, 0f, 0f);
                tLR = new Vector3(s1 * legSwing * move01, 0f, 0f);
                if (holding)
                {
                    tAL = new Vector3(-85f + s1 * 6f, 0f, -12f);
                    tAR = new Vector3(-85f - s1 * 6f, 0f, 12f);
                }
                else if (sprint)
                {
                    tAL = new Vector3(-150f + Mathf.Sin(flail) * flailAngle, 0f, -30f - Mathf.Sin(flail * 1.3f) * 25f);
                    tAR = new Vector3(-150f - Mathf.Sin(flail) * flailAngle, 0f, 30f + Mathf.Cos(flail * 1.3f) * 25f);
                }
                else
                {
                    tAL = new Vector3(s1 * armSwing * move01, 0f, -10f - move01 * 6f);
                    tAR = new Vector3(-s1 * armSwing * move01, 0f, 10f + move01 * 6f);
                }

                if (crouch > 0.01f)
                {
                    tLL.z -= 25f * crouch;
                    tLR.z += 25f * crouch;
                }
            }

            for (int i = 0; i < steps; i++)
            {
                _leanPitch.Step(tPitch, leanFrequency, leanDamping, dt);
                _leanRoll.Step(tRoll, leanFrequency, leanDamping, dt);
                _knock.Step(knocked ? controller.KnockdownPitch : 0f, knockFrequency, knockDamping, dt);
                _squash.Step(crouch * 0.32f, squashFrequency, squashDamping, dt);
                _headOffset.Step(tHead, headFrequency, headDamping, dt);
                _armL.Step(tAL, limbFrequency, limbDamping, dt);
                _armR.Step(tAR, limbFrequency, limbDamping, dt);
                _legL.Step(tLL, limbFrequency * 1.4f, limbDamping, dt);
                _legR.Step(tLR, limbFrequency * 1.4f, limbDamping, dt);
            }

            float roll = grounded && !knocked ? s1 * waddleRoll * move01 : 0f;
            float bob = grounded && !knocked ? Mathf.Abs(s1) * waddleBob * move01 : 0f;
            float sq = Mathf.Clamp(_squash.X, -0.4f, 0.5f);

            hips.localPosition = new Vector3(0f, bob, 0f);
            hips.localRotation = Quaternion.Euler(_leanPitch.X + _knock.X, 0f, _leanRoll.X + roll);
            hips.localScale = new Vector3(1f + sq * 0.45f, 1f - sq, 1f + sq * 0.45f);

            if (head != null)
            {
                Vector3 o = _headOffset.X;
                head.localPosition = _headRest + o;
                head.localRotation = Quaternion.Euler(o.z * 160f, 0f, -o.x * 160f - roll * 0.6f);
            }

            SetLimb(armL, _armL.X);
            SetLimb(armR, _armR.X);
            SetLimb(legL, _legL.X);
            SetLimb(legR, _legR.X);

            Blink(frameDt);
        }

        private static void SetLimb(Transform t, Vector3 euler)
        {
            if (t != null)
            {
                t.localRotation = Quaternion.Euler(euler);
            }
        }

        private void Blink(float dt)
        {
            if (eyes == null || eyes.Length == 0)
            {
                return;
            }

            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinkT = 0f;
                _blinkTimer = blinkInterval * Random.Range(0.4f, 1.6f);
            }

            _blinkT = Mathf.Min(1f, _blinkT + dt * 8f);
            float open = controller.IsKnockedDown ? 0.15f : Mathf.Abs(_blinkT * 2f - 1f);
            for (int i = 0; i < eyes.Length; i++)
            {
                if (eyes[i] == null)
                {
                    continue;
                }

                Vector3 s = _eyeRestScale[i];
                eyes[i].localScale = new Vector3(s.x, s.y * Mathf.Max(0.1f, open), s.z);
            }
        }
    }
}
