using UnityEngine;
using WheresMyLeftSock.Core;

namespace WheresMyLeftSock.World
{
    /// <summary>Thud when a physics prop hits something hard enough. Heavier props = lower pitch.</summary>
    public class ImpactSound : MonoBehaviour
    {
        private const float PerObjectCooldown = 0.20f;
        private const float GlobalMinGap = 0.055f;
        private const float GlobalWindowDuration = 0.30f;

        private static float _lastGlobalImpactTime;
        private static readonly float[] _recentImpactTimes = new float[4];
        private static int _recentImpactIndex;

        [SerializeField] private float minSpeed = 1.6f;
        private Rigidbody _rb;
        private float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobalRateLimiter()
        {
            _lastGlobalImpactTime = 0f;
            _recentImpactIndex = 0;
            for (int i = 0; i < _recentImpactTimes.Length; i++)
            {
                _recentImpactTimes[i] = 0f;
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision c)
        {
            if (Time.time < _next || Time.timeSinceLevelLoad < 1.5f) return; // skip spawn settling
            float v = c.relativeVelocity.magnitude;
            if (v < minSpeed) return;

            float now = Time.unscaledTime;
            if (now < _lastGlobalImpactTime)
            {
                ResetGlobalRateLimiter();
            }

            if (now - _lastGlobalImpactTime < GlobalMinGap) return;
            if (now - _recentImpactTimes[_recentImpactIndex] < GlobalWindowDuration) return;

            _next = Time.time + PerObjectCooldown;
            _lastGlobalImpactTime = now;
            _recentImpactTimes[_recentImpactIndex] = now;
            _recentImpactIndex = (_recentImpactIndex + 1) % _recentImpactTimes.Length;

            float mass = _rb != null ? _rb.mass : 1f;
            float vol = Mathf.Clamp01((v - minSpeed) / 6f) * 0.85f + 0.1f;
            float pitch = Mathf.Clamp(1.35f - mass * 0.08f, 0.55f, 1.45f) * Random.Range(0.9f, 1.1f);
            Sfx.PlayAt("thud", c.GetContact(0).point, vol, pitch);
        }
    }
}
