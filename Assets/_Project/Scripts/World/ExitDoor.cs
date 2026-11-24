using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// Front exit door interactable (E tap, or optional trigger walk-through) that triggers Win.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExitDoor : MonoBehaviour, IInteractable
    {
        [Header("Exit Door Settings")]
        [SerializeField] private string promptText = "Leave House";

        [Tooltip("Optional: also trigger Win when the Player enters a trigger collider on this GameObject.")]
        [SerializeField] private bool alsoWinOnTriggerEnter = false;

        [Tooltip("Optional override; falls back to GameManager.Instance if unset.")]
        [SerializeField] private GameManager gameManager = default;

        public string InteractionPrompt => string.IsNullOrEmpty(promptText) ? "Exit" : promptText;
        public InteractionCategory Category => InteractionCategory.Exit;

        public bool CanInteract
        {
            get
            {
                GameManager gm = ResolveGameManager();
                return gm != null && gm.CurrentState == GameState.Playing;
            }
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract)
            {
                return;
            }

            ResolveGameManager()?.TriggerWin();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!alsoWinOnTriggerEnter || !CanInteract || other == null)
            {
                return;
            }

            if (other.GetComponentInParent<PlayerController>() != null)
            {
                Interact(other.gameObject);
            }
        }

        private GameManager ResolveGameManager()
        {
            if (gameManager == null)
            {
                gameManager = GameManager.Instance != null
                    ? GameManager.Instance
                    : FindFirstObjectByType<GameManager>();
            }

            return gameManager;
        }
    }
}
