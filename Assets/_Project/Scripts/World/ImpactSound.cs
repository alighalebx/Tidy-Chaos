using UnityEngine;
using WheresMyLeftSock.Core;

namespace WheresMyLeftSock.World
{
    /// <summary>Thud when a physics prop hits something hard enough. Heavier props = lower pitch.</summary>
    public class ImpactSound : MonoBehaviour
    {
        [SerializeField] private float minSpeed = 1.6f;
        private Rigidbody _rb;
        private float _next;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision c)
        {
            if (Time.time < _next || Time.timeSinceLevelLoad < 1.5f) return; // skip spawn settling
            float v = c.relativeVelocity.magnitude;
            if (v < minSpeed) return;
            _next = Time.time + 0.12f;
            float mass = _rb != null ? _rb.mass : 1f;
            float vol = Mathf.Clamp01((v - minSpeed) / 6f) * 0.85f + 0.1f;
            float pitch = Mathf.Clamp(1.35f - mass * 0.08f, 0.55f, 1.45f) * Random.Range(0.9f, 1.1f);
            Sfx.PlayAt("thud", c.GetContact(0).point, vol, pitch);
        }
    }
}
