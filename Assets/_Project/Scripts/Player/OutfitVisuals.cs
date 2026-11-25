using System.Collections;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.Player
{
    /// <summary>
    /// Shows what you've put on: slippers turn into the sock you found, pajama legs become the pants, the top
    /// becomes the shirt, keys dangle from the hip. Decoys show too (clown pants!).
    /// </summary>
    public class OutfitVisuals : MonoBehaviour
    {
        private Transform _hips;

        private void Start()
        {
            Transform visual = transform.Find("Visual");
            _hips = visual != null ? visual.Find("Hips") : null;
            if (OutfitManager.Instance != null) OutfitManager.Instance.OnItemCollected += HandleCollected;
        }

        private void OnDestroy()
        {
            if (OutfitManager.Instance != null) OutfitManager.Instance.OnItemCollected -= HandleCollected;
        }

        private void HandleCollected(WearSlot slot, ItemData item, bool accepted)
        {
            if (!accepted || _hips == null) return;
            switch (slot)
            {
                case WearSlot.LeftFoot:
                    Foot("LegL", item);
                    break;
                case WearSlot.RightFoot:
                    Foot("LegR", item);
                    break;
                case WearSlot.Legs:
                    Paint("LegL/Segment", item.color, true);
                    Paint("LegR/Segment", item.color, true);
                    Paint("LegL/Cuff", item.accentColor, false);
                    Paint("LegR/Cuff", item.accentColor, false);
                    break;
                case WearSlot.Torso:
                    Paint("Torso", item.color, true);
                    Paint("ArmL/Segment", item.color, false);
                    Paint("ArmR/Segment", item.color, false);
                    Paint("ArmL/Cuff", item.accentColor, false);
                    Paint("ArmR/Cuff", item.accentColor, false);
                    foreach (Transform t in _hips)
                    {
                        if (t.name == "Button") t.gameObject.SetActive(false);
                    }

                    break;
                case WearSlot.Accessory:
                    Transform old = _hips.Find("WornKeys");
                    if (old != null) Destroy(old.gameObject);
                    var keys = new GameObject("WornKeys").transform;
                    keys.SetParent(_hips, false);
                    keys.localPosition = new Vector3(0.33f, 0.62f, 0.05f);
                    keys.localRotation = Quaternion.Euler(-80f, 0f, 90f);
                    keys.localScale = Vector3.one * 0.9f;
                    ItemVisuals.Build(keys, item);
                    StartCoroutine(Punch(keys));
                    break;
            }
        }

        private void Foot(string leg, ItemData item)
        {
            Paint(leg + "/Slipper", item.color, true);
            Transform l = _hips.Find(leg);
            if (l == null) return;
            foreach (Transform t in l)
            {
                if (t.name == "BunnyEar") t.gameObject.SetActive(false);
            }
        }

        private void Paint(string path, Color c, bool punch)
        {
            Transform t = _hips.Find(path);
            if (t == null) return;
            ItemVisuals.Tint(t.GetComponent<Renderer>(), c);
            if (punch) StartCoroutine(Punch(t));
        }

        private static IEnumerator Punch(Transform t)
        {
            Vector3 s0 = t.localScale;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f)
            {
                if (t == null) yield break;
                t.localScale = s0 * (1f + Mathf.Sin(k * Mathf.PI) * 0.35f * (1f - k));
                yield return null;
            }

            if (t != null) t.localScale = s0;
        }
    }
}
