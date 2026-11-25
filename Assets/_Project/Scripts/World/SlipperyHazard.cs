using UnityEngine;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// Trigger zone (puddle / legos): the player flops over and keeps sliding on their momentum (spec 4.6).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SlipperyHazard : MonoBehaviour
    {
        [SerializeField] private float knockdownDuration = 1.3f;
        [Tooltip("Only trip if moving at least this fast (m/s), so tiptoeing through works.")]
        [SerializeField] private float minSpeedToSlip = 5.0f;
        [SerializeField] private float slideBoost = 1.15f;
        [SerializeField] private float slowAfterwards = 0.6f;
        [SerializeField] private float slowDuration = 1.0f;
        [SerializeField] private float cooldown = 2.5f;
        [SerializeField] private AudioClip slipSfx = default;

        private float _nextAllowedTime;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            TrySlip(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TrySlip(other);
        }

        private void TrySlip(Collider other)
        {
            if (Time.time < _nextAllowedTime)
            {
                return;
            }

            PlayerController pc = other.GetComponentInParent<PlayerController>();
            if (pc == null || pc.IsKnockedDown || pc.CurrentHorizontalSpeed < minSpeedToSlip)
            {
                return;
            }

            _nextAllowedTime = Time.time + cooldown;
            pc.Knockdown(knockdownDuration, backwards: Random.value < 0.5f);
            pc.ApplySpeedMultiplier(slowAfterwards, knockdownDuration + slowDuration);

            Rigidbody rb = pc.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(v.x * slideBoost, Mathf.Max(v.y, 2.5f), v.z * slideBoost);
            }

            if (pc.CameraRig != null)
            {
                pc.CameraRig.AddShake(0.45f);
            }

            if (slipSfx != null)
            {
                AudioSource.PlayClipAtPoint(slipSfx, transform.position);
            }
            else
            {
                WheresMyLeftSock.Core.Sfx.PlayAt("slip", transform.position, 0.8f);
            }
        }
    }
}
