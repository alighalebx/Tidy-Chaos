using System;
using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.Items
{
    /// <summary>
    /// Tracks what's equipped per slot (spec 4.4) and computes Dignity, Total Score and the end-screen rank.
    /// Matching = full points; Trap = half points + funny tag; Empty = 0.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutfitManager : MonoBehaviour
    {
        public static OutfitManager Instance { get; private set; }

        [Tooltip("The 5 matching items (used for checklist names and max dignity).")]
        [SerializeField] private List<ItemData> requiredItems = new List<ItemData>();
        [Tooltip("Time remaining (s) needed for S rank on a perfect outfit.")]
        [SerializeField] private float sRankTimeThreshold = 20f;

        private readonly Dictionary<WearSlot, ItemData> _equipped = new Dictionary<WearSlot, ItemData>();

        /// <summary>(slot, item, accepted). accepted=false when a trap was picked up but the matching item was already worn.</summary>
        public event Action<WearSlot, ItemData, bool> OnItemCollected;

        public static readonly WearSlot[] Slots = (WearSlot[])Enum.GetValues(typeof(WearSlot));

        public IReadOnlyList<ItemData> RequiredItems => requiredItems;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetRequired(List<ItemData> items)
        {
            requiredItems = items;
        }

        public ItemData RequiredFor(WearSlot slot) => requiredItems.Find(i => i != null && i.slot == slot);

        public ItemData Equipped(WearSlot slot) => _equipped.TryGetValue(slot, out ItemData d) ? d : null;

        /// <summary>Returns true if the item went into its slot.</summary>
        public bool Equip(ItemData item)
        {
            if (item == null) return false;
            ItemData current = Equipped(item.slot);
            bool accepted = !(item.IsTrap && current != null && !current.IsTrap);
            if (accepted)
            {
                _equipped[item.slot] = item;
            }

            OnItemCollected?.Invoke(item.slot, item, accepted);
            return accepted;
        }

        public int DignityScore
        {
            get
            {
                int s = 0;
                foreach (ItemData d in _equipped.Values) s += d.dignityPoints;
                return s;
            }
        }

        public int MaxDignity
        {
            get
            {
                int s = 0;
                foreach (ItemData d in requiredItems) if (d != null) s += d.dignityPoints;
                return s > 0 ? s : 100;
            }
        }

        public int MissingCount
        {
            get
            {
                int n = 0;
                foreach (WearSlot s in Slots) if (!_equipped.ContainsKey(s)) n++;
                return n;
            }
        }

        public int TrapCount
        {
            get
            {
                int n = 0;
                foreach (ItemData d in _equipped.Values) if (d.IsTrap) n++;
                return n;
            }
        }

        public static int TotalScore(int dignity, float timeRemaining) => dignity + Mathf.FloorToInt(Mathf.Max(0f, timeRemaining) * 2f);

        public void Rank(bool missedRide, float timeRemaining, out string letter, out string title)
        {
            if (missedRide)
            {
                letter = "F"; title = "Left in Pajamas";
                return;
            }

            int missing = MissingCount;
            if (missing == 0 && TrapCount == 0)
            {
                if (timeRemaining >= sRankTimeThreshold) { letter = "S"; title = "Model Citizen"; }
                else { letter = "A"; title = "Dressed for Success"; }
            }
            else if (missing == 0)
            {
                letter = "B"; title = "Fashionably Chaotic";
            }
            else if (missing <= 2)
            {
                ItemData forgot = null;
                foreach (WearSlot s in Slots)
                {
                    if (!_equipped.ContainsKey(s)) { forgot = RequiredFor(s); break; }
                }

                letter = "C";
                title = $"Forgot {(forgot != null ? forgot.displayName : "Something")}, Caught the Ride";
            }
            else
            {
                letter = "D"; title = "Basically Still in Pajamas";
            }
        }

        /// <summary>Funny outfit tags for the end screen (trap tags + tags for missing slots).</summary>
        public List<string> FunnyTags()
        {
            var tags = new List<string>();
            foreach (ItemData d in _equipped.Values)
            {
                if (d.IsTrap && !string.IsNullOrEmpty(d.funnyTag)) tags.Add(d.funnyTag);
            }

            bool noLeft = !_equipped.ContainsKey(WearSlot.LeftFoot), noRight = !_equipped.ContainsKey(WearSlot.RightFoot);
            if (noLeft && noRight) tags.Add("Barefoot Bandit");
            else if (noLeft || noRight) tags.Add("One Bare Foot");
            if (!_equipped.ContainsKey(WearSlot.Legs)) tags.Add("Pantsless Wonder");
            if (!_equipped.ContainsKey(WearSlot.Torso)) tags.Add("Pajama Top Chic");
            if (!_equipped.ContainsKey(WearSlot.Accessory)) tags.Add("Locked Out");
            return tags;
        }
    }
}
