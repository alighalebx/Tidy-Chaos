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
        private bool _dropArcActive;
        private float _dropArcT;
        private Vector3 _dropStart;
        private Vector3 _dropEnd;

        public bool IsCollected => _collected;
        public bool IsCarriedByPet { get; private set; }

        public string InteractionPrompt => data != null ? data.displayName : "Item";
        public InteractionCategory Category => InteractionCategory.Collectible;

        public bool CanInteract => !_collected && !IsCarriedByPet &&
                                   (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing);

        public void AttachToPetMouth(Transform mouthAnchor)
        {
            if (_collected || mouthAnchor == null) return;
            _dropArcActive = false;
            IsCarriedByPet = true;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            if (spot != null)
            {
                spot.isOccupied = false;
                spot = null;
            }

            transform.SetParent(mouthAnchor, false);
            transform.localPosition = new Vector3(0f, -0.04f, 0.08f);
            transform.localRotation = Quaternion.Euler(18f, 0f, -18f);
        }

        public void DropFromPet(Vector3 worldPos)
        {
            DropFromPet(worldPos, worldPos);
        }

        public void DropFromPet(Vector3 startPos, Vector3 landPos)
        {
            if (_collected) return;
            IsCarriedByPet = false;
            transform.SetParent(null, true);
            transform.position = startPos;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = true;

            _dropStart = startPos;
            _dropEnd = landPos;
            _dropArcT = 0f;
            _dropArcActive = true;
        }

        public void ReclaimFromPet(GameObject interactor)
        {
            if (_collected) return;
            IsCarriedByPet = false;
            _dropArcActive = false;
            Interact(interactor);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;
            _collected = true;
            _dropArcActive = false;
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
            if (_dropArcActive)
            {
                _dropArcT += Time.deltaTime / 0.38f;
                float u = Mathf.Clamp01(_dropArcT);
                Vector3 pos = Vector3.Lerp(_dropStart, _dropEnd, u);
                pos.y += Mathf.Sin(u * Mathf.PI) * 0.65f;
                transform.position = pos;
                transform.Rotate(0f, 540f * Time.deltaTime, 0f, Space.World);
                if (u >= 1f)
                {
                    _dropArcActive = false;
                    transform.position = _dropEnd;
                    transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                }
            }

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
