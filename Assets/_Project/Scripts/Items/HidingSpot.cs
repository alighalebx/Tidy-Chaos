using UnityEngine;

namespace WheresMyLeftSock.Items
{
    public enum Room
    {
        Bedroom = 0,
        Bathroom = 1,
        LivingRoom = 2,
        Hallway = 3,
        Kitchen = 4
    }

    public enum ConcealmentType
    {
        UnderClutter = 0,
        InsideDrawer = 1,
        HighShelf = 2,
        FloorHidden = 3,
        ObviousDecoySpot = 4
    }

    /// <summary>
    /// Lightweight spawn anchor for hidden objective items / decoys (spec 4.3).
    /// Spots inside drawers/cabinets are parented to the moving part, so a spawned item
    /// should be parented to the spot and made kinematic until collected.
    /// </summary>
    public class HidingSpot : MonoBehaviour
    {
        public string spotID;
        public Room room;
        public ConcealmentType concealmentType;
        public bool isOccupied;

        private void OnDrawGizmos()
        {
            switch (concealmentType)
            {
                case ConcealmentType.UnderClutter: Gizmos.color = new Color(1f, 0.6f, 0.1f); break;
                case ConcealmentType.InsideDrawer: Gizmos.color = new Color(0.3f, 0.6f, 1f); break;
                case ConcealmentType.HighShelf: Gizmos.color = new Color(0.7f, 0.3f, 1f); break;
                case ConcealmentType.FloorHidden: Gizmos.color = new Color(0.2f, 0.9f, 0.4f); break;
                default: Gizmos.color = new Color(1f, 0.2f, 0.3f); break;
            }

            Gizmos.DrawSphere(transform.position, 0.07f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.3f);
        }
    }
}
