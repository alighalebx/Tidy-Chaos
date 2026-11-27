using System.Collections;
using UnityEngine;
using WheresMyLeftSock.Core;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// The ride idling outside: engine rumble, honks, and the big drive-off (screech + smoke) when you lose
    /// (or a happy honk-honk and drive-off when you make it).
    /// </summary>
    public class RideCar : MonoBehaviour
    {
        [SerializeField] private float topSpeed = 26f;
        [SerializeField] private float acceleration = 14f;

        private AudioSource _engine;
        private bool _leaving;
        private Coroutine _panic;

        public static RideCar Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = Sfx.Get("engine");
            _engine.loop = true;
            _engine.spatialBlend = 1f;
            _engine.rolloffMode = AudioRolloffMode.Linear;
            _engine.minDistance = 4f;
            _engine.maxDistance = 40f;
            _engine.volume = 0.7f;
            _engine.Play();

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged += HandleState;
                GameManager.Instance.OnPauseChanged += HandlePause;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStateChanged -= HandleState;
                GameManager.Instance.OnPauseChanged -= HandlePause;
            }

            if (Instance == this) Instance = null;
        }

        private void HandlePause(bool paused)
        {
            if (_engine == null) return;
            if (paused) _engine.Pause();
            else if (!_leaving) _engine.UnPause();
        }

        private void HandleState(GameState s)
        {
            if (s == GameState.Lose) DriveAway(true, 0f);
            else if (s == GameState.Win) { Honk(2, 0.16f); DriveAway(false, 0.9f); }
        }

        public void Honk(int count, float gap = 0.24f) => StartCoroutine(HonkRoutine(count, gap, "honk"));

        public void HonkLong() => Sfx.PlayAt("honk_long", transform.position, 1f);

        public void Rev()
        {
            Sfx.PlayAt("rev", transform.position, 0.9f);
            if (_engine != null) StartCoroutine(PitchBlip());
        }

        public void SetPanic(bool on)
        {
            if (on && _panic == null) _panic = StartCoroutine(PanicHonks());
            if (!on && _panic != null) { StopCoroutine(_panic); _panic = null; }
        }

        private IEnumerator HonkRoutine(int count, float gap, string clip)
        {
            for (int i = 0; i < count; i++)
            {
                Sfx.PlayAt(clip, transform.position, 1f, Random.Range(0.97f, 1.03f));
                yield return new WaitForSeconds(0.36f + gap);
            }
        }

        private IEnumerator PanicHonks()
        {
            while (!_leaving)
            {
                int n = Random.Range(2, 5);
                for (int i = 0; i < n; i++)
                {
                    Sfx.PlayAt("honk", transform.position, 1f, Random.Range(1f, 1.1f));
                    yield return new WaitForSeconds(0.2f);
                }

                if (Random.value < 0.4f) Rev();
                yield return new WaitForSeconds(Random.Range(0.6f, 1.2f));
            }
        }

        private IEnumerator PitchBlip()
        {
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                _engine.pitch = 1f + Mathf.Sin(t / 1.2f * Mathf.PI) * 0.8f;
                yield return null;
            }

            _engine.pitch = 1f;
        }

        public void DriveAway(bool screech, float delay)
        {
            if (_leaving) return;
            _leaving = true;
            SetPanic(false);
            StartCoroutine(DriveRoutine(screech, delay));
        }

        private IEnumerator DriveRoutine(bool screech, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (screech) Sfx.PlayAt("screech", transform.position, 1f);
            Sfx.PlayAt("rev", transform.position, 1f, 1.1f);

            Vector3 dir = transform.forward;
            dir.y = 0f;
            dir.Normalize();
            Quaternion baseRot = transform.rotation;
            float speed = 0f, travelled = 0f, smokeTimer = 0f, t = 0f;
            while (travelled < 90f)
            {
                float dt = Time.deltaTime;
                t += dt;
                speed = Mathf.Min(topSpeed, speed + acceleration * dt);
                transform.position += dir * speed * dt;
                travelled += speed * dt;

                // goofy squat-and-wheelie on launch
                float tilt = Mathf.Sin(Mathf.Min(1f, t / 0.8f) * Mathf.PI) * (screech ? 6f : 3f);
                transform.rotation = baseRot * Quaternion.Euler(-tilt, 0f, Mathf.Sin(t * 25f) * 0.6f);

                if (_engine != null) _engine.pitch = 1f + speed / topSpeed * 1.4f;

                smokeTimer -= dt;
                if (screech && t < 1.6f && smokeTimer <= 0f)
                {
                    smokeTimer = 0.05f;
                    SpawnSmoke(transform.position - dir * 2.2f + Vector3.up * 0.3f);
                }

                yield return null;
            }

            if (_engine != null) _engine.Stop();
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        private void SpawnSmoke(Vector3 pos)
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(puff.GetComponent<Collider>());
            puff.transform.position = pos + Random.insideUnitSphere * 0.3f;
            puff.transform.localScale = Vector3.one * 0.4f;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
            Renderer r = puff.GetComponent<Renderer>();
            RuntimeMaterials.ApplyLit(r);
            r.SetPropertyBlock(mpb);
            StartCoroutine(AnimateSmokePuff(puff));
        }

        private static IEnumerator AnimateSmokePuff(GameObject puff)
        {
            float t = 0f;
            while (t < 0.9f && puff != null)
            {
                float dt = Time.deltaTime;
                t += dt;
                puff.transform.localScale = Vector3.one * (0.4f + t * 2.2f);
                puff.transform.position += Vector3.up * (dt * 0.8f);
                yield return null;
            }

            if (puff != null)
            {
                Destroy(puff);
            }
        }
    }
}
