using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Items
{
    /// <summary>
    /// A hidden clothing item / keys. E or LMB-tap collects it instantly: pop SFX, equips via OutfitManager,
    /// pops out of the world (spec 4.2). Kinematic so it rides along inside drawers and never falls through.
    /// </summary>
    public class WearableItem : MonoBehaviour, IInteractable
    {
        public ItemData data;
        public HidingSpot spot;

        private bool _collected;
        private float _popT = -1f;
        private Vector3 _baseScale;

        public string InteractionPrompt => data != null ? data.displayName : "Item";
        public InteractionCategory Category => InteractionCategory.Collectible;

        public bool CanInteract => !_collected &&
                                   (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing);

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;
            _collected = true;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (spot != null) spot.isOccupied = false;

            bool accepted = OutfitManager.Instance == null || OutfitManager.Instance.Equip(data);
            string sfx = data != null ? (data.IsTrap ? (data.shape == ItemShape.WetSock ? "squelch" : "wah") : data.pickupSfx) : "pop";
            Sfx.PlayAt(sfx, transform.position, 0.9f, Random.Range(0.95f, 1.08f));
            if (accepted && data != null && data.IsTrap) Sfx.PlayAt("pop", transform.position, 0.4f, 0.8f);

            _baseScale = transform.localScale;
            transform.SetParent(null, true);
            _popT = 0f;
        }

        private void Update()
        {
            if (_popT < 0f) return;
            _popT += Time.deltaTime;
            float k = _popT / 0.3f;
            float s = Mathf.Max(0f, 1f + Mathf.Sin(Mathf.Min(k, 1f) * Mathf.PI) * 0.7f - k);
            transform.localScale = _baseScale * s;
            transform.position += Vector3.up * Time.deltaTime * 2.5f;
            transform.Rotate(0f, 720f * Time.deltaTime, 0f, Space.World);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
