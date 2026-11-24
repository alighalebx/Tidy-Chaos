using UnityEngine;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// High-level categories used for reticle feedback and interaction priority.
    /// </summary>
    public enum InteractionCategory
    {
        None = 0,
        Grabbable = 1,
        Collectible = 2,
        Furniture = 3,
        Exit = 4
    }

    /// <summary>
    /// Contract for world objects the player can interact with via raycast (wearables, drawers, doors).
    /// </summary>
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        InteractionCategory Category { get; }
        bool CanInteract { get; }
        void Interact(GameObject interactor);
    }
}
