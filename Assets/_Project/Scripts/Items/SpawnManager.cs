using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.Items
{
    /// <summary>
    /// Spec 4.3: every run hides the 5 required items in random HidingSpots (never decoy spots, spread over at least
    /// 3 rooms), puts 3-5 decoys in obvious spots, and makes sure UnderClutter spots actually have clutter on top.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnManager : MonoBehaviour
    {
        [SerializeField] private List<ItemData> requiredItems = new List<ItemData>();
        [SerializeField] private List<ItemData> decoyItems = new List<ItemData>();
        [Range(0, 8)] [SerializeField] private int decoyCount = 4;
        [SerializeField] private int minRoomsForRequired = 3;
        [Tooltip("Dropped on top of UnderClutter spots that have nothing covering them.")]
        [SerializeField] private List<GameObject> coverClutter = new List<GameObject>();
        [Tooltip("-1 = random every run.")]
        [SerializeField] private int seed = -1;
        [SerializeField] private bool logPlacements = true;

        private readonly List<WearableItem> _spawned = new List<WearableItem>();
        private System.Random _rng;

        public IReadOnlyList<WearableItem> Spawned => _spawned;

        private void Start()
        {
            _rng = seed >= 0 ? new System.Random(seed) : new System.Random();
            if (OutfitManager.Instance != null) OutfitManager.Instance.SetRequired(requiredItems);
            SpawnAll();
        }

        private void SpawnAll()
        {
            var spots = new List<HidingSpot>(FindObjectsByType<HidingSpot>(FindObjectsSortMode.None));
            foreach (HidingSpot s in spots) s.isOccupied = false;
            Shuffle(spots);

            var required = new List<ItemData>(requiredItems);
            Shuffle(required);
            var usedRooms = new HashSet<Room>();
            foreach (ItemData item in required)
            {
                if (item == null) continue;
                bool needNewRoom = usedRooms.Count < minRoomsForRequired;
                HidingSpot pick = spots.Find(s => !s.isOccupied && s.concealmentType != ConcealmentType.ObviousDecoySpot &&
                                                  item.AllowsRoom(s.room) && (!needNewRoom || !usedRooms.Contains(s.room)))
                                  ?? spots.Find(s => !s.isOccupied && s.concealmentType != ConcealmentType.ObviousDecoySpot && item.AllowsRoom(s.room))
                                  ?? spots.Find(s => !s.isOccupied && s.concealmentType != ConcealmentType.ObviousDecoySpot);
                if (pick == null) continue;
                usedRooms.Add(pick.room);
                Spawn(item, pick);
            }

            var decoys = new List<ItemData>(decoyItems);
            Shuffle(decoys);
            for (int i = 0; i < Mathf.Min(decoyCount, decoys.Count); i++)
            {
                ItemData item = decoys[i];
                if (item == null) continue;
                HidingSpot pick = spots.Find(s => !s.isOccupied && s.concealmentType == ConcealmentType.ObviousDecoySpot && item.AllowsRoom(s.room))
                                  ?? spots.Find(s => !s.isOccupied && s.concealmentType == ConcealmentType.ObviousDecoySpot)
                                  ?? spots.Find(s => !s.isOccupied);
                if (pick != null) Spawn(item, pick);
            }
        }

        private void Spawn(ItemData item, HidingSpot spot)
        {
            spot.isOccupied = true;
            var root = new GameObject("Item_" + item.itemId);
            root.transform.SetParent(spot.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.rotation = Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f);
            ItemVisuals.Build(root.transform, item);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; // rides along with drawers, can't tunnel through floors
            var box = root.AddComponent<BoxCollider>();
            Bounds b = ItemVisuals.LocalBounds(root);
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * 0.08f);

            var w = root.AddComponent<WearableItem>();
            w.data = item;
            w.spot = spot;
            _spawned.Add(w);

            if (spot.concealmentType == ConcealmentType.UnderClutter)
            {
                EnsureCovered(spot, b.size.y);
            }

#if UNITY_EDITOR
            if (logPlacements)
            {
                Debug.Log($"[SpawnManager] {item.displayName} -> {spot.spotID} ({spot.room}, {spot.concealmentType})");
            }
#else
            _ = logPlacements;
#endif
        }

        private void EnsureCovered(HidingSpot spot, float itemHeight)
        {
            Vector3 p = spot.transform.position;
            Collider[] hits = Physics.OverlapBox(p + Vector3.up * 0.3f, new Vector3(0.22f, 0.28f, 0.22f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider h in hits)
            {
                if (h.GetComponentInParent<PhysicsClutter>() != null) return; // already hidden under something
            }

            if (coverClutter.Count == 0) return;
            GameObject prefab = coverClutter[_rng.Next(coverClutter.Count)];
            if (prefab == null) return;
            Instantiate(prefab, p + Vector3.up * (itemHeight + 0.03f), Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
