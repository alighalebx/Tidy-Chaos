using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Player;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.UI
{
    /// <summary>
    /// Minimal Phase 1 uGUI + TextMeshPro debug HUD: displays the countdown timer,
    /// dynamic crosshair + aimed object prompt, and Win/Lose overlay with a Restart button.
    /// </summary>
    [DisallowMultipleComponent]
    public class Phase1DebugHUD : MonoBehaviour
    {
        [Header("Scene References (Optional — Auto-Resolved If Unset)")]
        [SerializeField] private GameManager gameManager = default;
        [SerializeField] private InteractionManager interactionManager = default;

        [Header("Timer Display")]
        [SerializeField] private TMPro.TMP_Text timerText = default;
        [Min(0f)]
        [SerializeField] private float urgentThresholdSeconds = 12f;
        [SerializeField] private Color normalTimerColor = Color.white;
        [SerializeField] private Color urgentTimerColor = new Color(1f, 0.28f, 0.25f, 1f);

        [Header("Crosshair & Hover Prompt")]
        [SerializeField] private UnityEngine.UI.Image crosshairImage = default;
        [SerializeField] private TMPro.TMP_Text hoverPromptText = default;
        [SerializeField] private Color defaultCrosshairColor = new Color(1f, 1f, 1f, 0.8f);
        [SerializeField] private Color grabbableCrosshairColor = new Color(1f, 0.85f, 0.25f, 1f);
        [SerializeField] private Color holdingCrosshairColor = new Color(0.35f, 0.9f, 1f, 1f);
        [SerializeField] private Color interactableCrosshairColor = new Color(0.35f, 1f, 0.5f, 1f);
        [Min(0.25f)]
        [SerializeField] private float defaultCrosshairScale = 1f;
        [Min(0.25f)]
        [SerializeField] private float activeCrosshairScale = 1.45f;

        [Header("End Screen Overlay")]
        [SerializeField] private GameObject endScreenPanel = default;
        [SerializeField] private TMPro.TMP_Text endTitleText = default;
        [SerializeField] private TMPro.TMP_Text endSubtitleText = default;
        [SerializeField] private UnityEngine.UI.Button restartButton = default;
        [SerializeField] private string winTitle = "CAUGHT THE RIDE!";
        [SerializeField] private string loseTitle = "LEFT IN PAJAMAS!";

        private GameManager _subscribedGameManager;
        private InteractionManager _subscribedInteractionManager;

        private void Awake()
        {
            ResolveDependencies();

            if (restartButton != null)
            {
                restartButton.onClick.AddListener(HandleRestartClicked);
            }
        }

        private void OnEnable()
        {
            ResolveDependencies();
            SubscribeToEvents();
            SyncInitialState();
        }

        private void Start()
        {
            ResolveDependencies();
            SubscribeToEvents();
            SyncInitialState();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void OnDestroy()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(HandleRestartClicked);
            }
        }

        private void ResolveDependencies()
        {
            if (gameManager == null)
            {
                gameManager = GameManager.Instance != null
                    ? GameManager.Instance
                    : FindFirstObjectByType<GameManager>();
            }

            if (interactionManager == null)
            {
                interactionManager = FindFirstObjectByType<InteractionManager>();
            }
        }

        private void SubscribeToEvents()
        {
            if (_subscribedGameManager != gameManager)
            {
                if (_subscribedGameManager != null)
                {
                    _subscribedGameManager.OnGameStateChanged -= HandleGameStateChanged;
                    _subscribedGameManager.OnTimerUpdated -= HandleTimerUpdated;
                    _subscribedGameManager.OnPauseChanged -= HandlePauseChanged;
                    _subscribedGameManager.OnIntroSwoopChanged -= HandleIntroSwoopChanged;
                }

                _subscribedGameManager = gameManager;
                if (_subscribedGameManager != null)
                {
                    _subscribedGameManager.OnGameStateChanged += HandleGameStateChanged;
                    _subscribedGameManager.OnTimerUpdated += HandleTimerUpdated;
                    _subscribedGameManager.OnPauseChanged += HandlePauseChanged;
                    _subscribedGameManager.OnIntroSwoopChanged += HandleIntroSwoopChanged;
                }
            }

            if (_subscribedInteractionManager != interactionManager)
            {
                if (_subscribedInteractionManager != null)
                {
                    _subscribedInteractionManager.OnHoverStateChanged -= HandleHoverStateChanged;
                }

                _subscribedInteractionManager = interactionManager;
                if (_subscribedInteractionManager != null)
                {
                    _subscribedInteractionManager.OnHoverStateChanged += HandleHoverStateChanged;
                }
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (_subscribedGameManager != null)
            {
                _subscribedGameManager.OnGameStateChanged -= HandleGameStateChanged;
                _subscribedGameManager.OnTimerUpdated -= HandleTimerUpdated;
                _subscribedGameManager.OnPauseChanged -= HandlePauseChanged;
                _subscribedGameManager.OnIntroSwoopChanged -= HandleIntroSwoopChanged;
                _subscribedGameManager = null;
            }

            if (_subscribedInteractionManager != null)
            {
                _subscribedInteractionManager.OnHoverStateChanged -= HandleHoverStateChanged;
                _subscribedInteractionManager = null;
            }
        }

        private void SyncInitialState()
        {
            if (gameManager != null)
            {
                HandleTimerUpdated(gameManager.TimeRemaining, gameManager.RoundDuration);
                HandleGameStateChanged(gameManager.CurrentState);
            }
            else if (endScreenPanel != null)
            {
                endScreenPanel.SetActive(false);
            }

            if (interactionManager != null)
            {
                HandleHoverStateChanged(interactionManager.CurrentHoverState);
            }
            else
            {
                HandleHoverStateChanged(default);
            }
        }

        private void HandleTimerUpdated(float timeRemaining, float totalDuration)
        {
            if (timerText == null)
            {
                return;
            }

            float clamped = Mathf.Max(0f, timeRemaining);
            bool isUrgent = clamped <= urgentThresholdSeconds;

            if (isUrgent)
            {
                timerText.text = $"{clamped:0.0}s";
                timerText.color = urgentTimerColor;
            }
            else
            {
                int totalSeconds = Mathf.CeilToInt(clamped);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                timerText.text = $"{minutes:00}:{seconds:00}";
                timerText.color = normalTimerColor;
            }
        }

        private void HandleHoverStateChanged(InteractionHoverState state)
        {
            Color targetColor = defaultCrosshairColor;
            float targetScale = defaultCrosshairScale;
            string prompt = string.Empty;

            if (state.IsHoldingObject)
            {
                targetColor = holdingCrosshairColor;
                targetScale = activeCrosshairScale;
                prompt = $"Holding: {state.Prompt}\n[RMB] Yeet   [Release LMB] Drop";
            }
            else
            {
                switch (state.Category)
                {
                    case InteractionCategory.Grabbable:
                        targetColor = grabbableCrosshairColor;
                        targetScale = activeCrosshairScale;
                        prompt = $"[Hold LMB] Grab {state.Prompt}";
                        break;

                    case InteractionCategory.Collectible:
                        targetColor = interactableCrosshairColor;
                        targetScale = activeCrosshairScale;
                        prompt = $"[E / LMB] Pick Up {state.Prompt}";
                        break;

                    case InteractionCategory.Furniture:
                    case InteractionCategory.Exit:
                        targetColor = interactableCrosshairColor;
                        targetScale = activeCrosshairScale;
                        prompt = $"[E] {state.Prompt}";
                        break;
                }
            }

            if (crosshairImage != null)
            {
                crosshairImage.color = targetColor;
                crosshairImage.rectTransform.localScale = Vector3.one * targetScale;
            }

            if (hoverPromptText != null)
            {
                hoverPromptText.text = prompt;
            }
        }

        private void HandlePauseChanged(bool paused)
        {
            RefreshGameplayReticleVisibility();
        }

        private void HandleIntroSwoopChanged(bool swooping)
        {
            RefreshGameplayReticleVisibility();
        }

        private void RefreshGameplayReticleVisibility()
        {
            bool visible = gameManager != null &&
                           gameManager.CurrentState == GameState.Playing &&
                           !gameManager.IsPaused &&
                           !gameManager.IsIntroSwoopActive;

            if (timerText != null)
            {
                timerText.gameObject.SetActive(gameManager == null || gameManager.CurrentState != GameState.Menu);
            }

            if (crosshairImage != null)
            {
                crosshairImage.gameObject.SetActive(visible);
            }

            if (hoverPromptText != null)
            {
                hoverPromptText.gameObject.SetActive(visible);
            }
        }

        private void HandleGameStateChanged(GameState state)
        {
            RefreshGameplayReticleVisibility();

            if (endScreenPanel == null)
            {
                return;
            }

            if (state == GameState.Win)
            {
                endScreenPanel.SetActive(true);
                if (endTitleText != null)
                {
                    endTitleText.text = winTitle;
                }

                if (endSubtitleText != null && gameManager != null)
                {
                    endSubtitleText.text = $"Escaped with {gameManager.TimeRemaining:F1}s remaining!";
                }
            }
            else if (state == GameState.Lose)
            {
                endScreenPanel.SetActive(true);
                if (endTitleText != null)
                {
                    endTitleText.text = loseTitle;
                }

                if (endSubtitleText != null)
                {
                    endSubtitleText.text = "Time ran out — your ride drove off!";
                }
            }
            else
            {
                endScreenPanel.SetActive(false);
            }
        }

        private void HandleRestartClicked()
        {
            ResolveDependencies();
            gameManager?.RestartRun();
        }
    }
}
