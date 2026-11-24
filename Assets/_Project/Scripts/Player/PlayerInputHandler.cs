using UnityEngine;
using UnityEngine.InputSystem;

namespace WheresMyLeftSock.Player
{
    /// <summary>
    /// Wraps the project's InputSystem_Actions asset and exposes clean per-frame gameplay queries
    /// for PlayerController and InteractionManager using only UnityEngine.InputSystem.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("Input Action Asset")]
        [Tooltip("Assign Assets/InputSystem_Actions here.")]
        [SerializeField] private InputActionAsset inputActions = default;

        [Header("Action Map & Action Names")]
        [SerializeField] private string playerMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string lookActionName = "Look";
        [SerializeField] private string sprintActionName = "Sprint";
        [SerializeField] private string crouchActionName = "Crouch";
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string grabActionName = "Attack";
        [SerializeField] private string throwActionName = "Throw";
        [SerializeField] private string interactActionName = "Interact";

        private InputActionMap _playerMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _jumpAction;
        private InputAction _grabAction;
        private InputAction _throwAction;
        private InputAction _interactAction;

        private bool _ownsFallbackThrow;
        private bool _gameplayEnabled = true;

        public bool IsInitialized { get; private set; }
        public bool IsGameplayInputEnabled => IsInitialized && _gameplayEnabled;

        public Vector2 MoveInput =>
            CanReadInput(_moveAction) ? _moveAction.ReadValue<Vector2>() : Vector2.zero;

        public Vector2 LookInput =>
            CanReadInput(_lookAction) ? _lookAction.ReadValue<Vector2>() : Vector2.zero;

        public bool IsLookUsingPointer =>
            _lookAction != null &&
            _lookAction.activeControl != null &&
            _lookAction.activeControl.device is Pointer;

        public bool IsSprintHeld =>
            CanReadInput(_sprintAction) && _sprintAction.IsPressed();

        public bool IsCrouchHeld =>
            CanReadInput(_crouchAction) && _crouchAction.IsPressed();

        public bool CrouchPressedThisFrame =>
            CanReadInput(_crouchAction) && _crouchAction.WasPressedThisFrame();

        public bool JumpPressedThisFrame =>
            CanReadInput(_jumpAction) && _jumpAction.WasPressedThisFrame();

        public bool IsGrabHeld =>
            CanReadInput(_grabAction) && _grabAction.IsPressed();

        public bool GrabPressedThisFrame =>
            CanReadInput(_grabAction) && _grabAction.WasPressedThisFrame();

        public bool GrabReleasedThisFrame =>
            CanReadInput(_grabAction) && _grabAction.WasReleasedThisFrame();

        public bool ThrowPressedThisFrame =>
            CanReadInput(_throwAction) && _throwAction.WasPressedThisFrame();

        public bool InteractPressedThisFrame =>
            CanReadInput(_interactAction) && _interactAction.WasPressedThisFrame();

        private void Awake()
        {
            InitializeActions();
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                InitializeActions();
            }

            if (_gameplayEnabled)
            {
                EnableActions();
            }
        }

        private void OnDisable()
        {
            DisableActions();
        }

        private void OnDestroy()
        {
            if (_ownsFallbackThrow && _throwAction != null)
            {
                _throwAction.Disable();
                _throwAction.Dispose();
                _throwAction = null;
                _ownsFallbackThrow = false;
            }
        }

        /// <summary>
        /// Enables or disables only the Player action map without disturbing UI navigation.
        /// </summary>
        public void SetGameplayInputEnabled(bool enabled)
        {
            _gameplayEnabled = enabled;

            if (!IsInitialized)
            {
                return;
            }

            if (_gameplayEnabled && isActiveAndEnabled)
            {
                EnableActions();
            }
            else
            {
                DisableActions();
            }
        }

        private void InitializeActions()
        {
            if (inputActions == null)
            {
                Debug.LogError(
                    $"[PlayerInputHandler] InputActionAsset is not assigned on '{name}'. " +
                    "Assign Assets/InputSystem_Actions in the Inspector.",
                    this);
                IsInitialized = false;
                return;
            }

            _playerMap = inputActions.FindActionMap(playerMapName, throwIfNotFound: false);
            if (_playerMap == null)
            {
                Debug.LogError(
                    $"[PlayerInputHandler] Action Map '{playerMapName}' was not found in '{inputActions.name}'.",
                    this);
                IsInitialized = false;
                return;
            }

            _moveAction = FindOptionalAction(moveActionName);
            _lookAction = FindOptionalAction(lookActionName);
            _sprintAction = FindOptionalAction(sprintActionName);
            _crouchAction = FindOptionalAction(crouchActionName);
            _jumpAction = FindOptionalAction(jumpActionName);
            _grabAction = FindOptionalAction(grabActionName);
            _interactAction = FindOptionalAction(interactActionName);
            _throwAction = _playerMap.FindAction(throwActionName, throwIfNotFound: false);

            if (_throwAction == null)
            {
                _throwAction = new InputAction(throwActionName, InputActionType.Button);
                _throwAction.AddBinding("<Mouse>/rightButton", groups: "Keyboard&Mouse");
                _throwAction.AddBinding("<Gamepad>/rightTrigger", groups: "Gamepad");
                _ownsFallbackThrow = true;
            }

            IsInitialized = true;
        }

        private InputAction FindOptionalAction(string actionName)
        {
            InputAction action = _playerMap.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
            {
                Debug.LogWarning(
                    $"[PlayerInputHandler] Action '{actionName}' not found in Action Map '{playerMapName}'.",
                    this);
            }

            return action;
        }

        private void EnableActions()
        {
            _playerMap?.Enable();
            if (_ownsFallbackThrow)
            {
                _throwAction?.Enable();
            }
        }

        private void DisableActions()
        {
            _playerMap?.Disable();
            if (_ownsFallbackThrow)
            {
                _throwAction?.Disable();
            }
        }

        private bool CanReadInput(InputAction action)
        {
            return IsInitialized && _gameplayEnabled && action != null && action.enabled;
        }
    }
}
