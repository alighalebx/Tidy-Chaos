using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.Core
{
    public enum GameState
    {
        Menu = 0,
        Playing = 1,
        Win = 2,
        Lose = 3
    }

    /// <summary>
    /// Controls the Phase 1 core game loop state machine (Menu, Playing, Win, Lose),
    /// 60-second countdown timer, player control/cursor gating, and scene restarts.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Run Settings")]
        [Min(5f)]
        [SerializeField] private float roundDurationSeconds = 60f;
        [SerializeField] private bool startPlayingOnStart = true;

        [Header("Scene References (Optional — Auto-Resolved If Unset)")]
        [SerializeField] private PlayerController playerController = default;
        [SerializeField] private InteractionManager interactionManager = default;

        public event Action<GameState> OnGameStateChanged;
        public event Action<float, float> OnTimerUpdated;
        public event Action<bool> OnPauseChanged;
        public event Action<bool> OnIntroSwoopChanged;

        public GameState CurrentState { get; private set; } = GameState.Menu;
        public bool IsPaused { get; private set; }
        public bool IsIntroSwoopActive { get; private set; }
        public float TimeRemaining { get; private set; }
        public float RoundDuration => roundDurationSeconds;
        public float NormalizedTimeRemaining =>
            roundDurationSeconds > 0f ? Mathf.Clamp01(TimeRemaining / roundDurationSeconds) : 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[GameManager] Duplicate GameManager on '{name}' destroyed; keeping '{Instance.name}'.",
                    this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveSceneReferences();
            TimeRemaining = roundDurationSeconds;
        }

        private void Start()
        {
            if (startPlayingOnStart)
            {
                StartRun();
            }
            else
            {
                EnterMenu();
            }
        }

        private void Update()
        {
            if (CurrentState != GameState.Playing || IsPaused || IsIntroSwoopActive)
            {
                return;
            }

            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            OnTimerUpdated?.Invoke(TimeRemaining, roundDurationSeconds);

            if (TimeRemaining <= 0f)
            {
                TriggerLose();
            }
        }

        private void OnValidate()
        {
            roundDurationSeconds = Mathf.Max(5f, roundDurationSeconds);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                if (IsPaused)
                {
                    Time.timeScale = 1f;
                }

                Instance = null;
            }
        }

        /// <summary>
        /// Places the game in the Menu state with player controls disabled and cursor unlocked.
        /// </summary>
        public void EnterMenu()
        {
            ClearPauseIfNeeded();
            ClearIntroSwoopIfNeeded();
            TimeRemaining = roundDurationSeconds;
            CurrentState = GameState.Menu;
            SetPlayerActive(false);

            OnTimerUpdated?.Invoke(TimeRemaining, roundDurationSeconds);
            OnGameStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Starts or resets the countdown run and enables player controls.
        /// </summary>
        public void StartRun()
        {
            ClearPauseIfNeeded();
            ClearIntroSwoopIfNeeded();
            TimeRemaining = roundDurationSeconds;
            CurrentState = GameState.Playing;
            SetPlayerActive(true);

            OnTimerUpdated?.Invoke(TimeRemaining, roundDurationSeconds);
            OnGameStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Transitions from Playing to Win when the player exits the house before time expires.
        /// </summary>
        public void TriggerWin()
        {
            if (CurrentState != GameState.Playing)
            {
                return;
            }

            ClearPauseIfNeeded();
            ClearIntroSwoopIfNeeded();
            CurrentState = GameState.Win;
            SetPlayerActive(false);
            OnGameStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Transitions from Playing to Lose when the countdown reaches zero.
        /// </summary>
        public void TriggerLose()
        {
            if (CurrentState != GameState.Playing)
            {
                return;
            }

            ClearPauseIfNeeded();
            ClearIntroSwoopIfNeeded();
            TimeRemaining = 0f;
            CurrentState = GameState.Lose;
            SetPlayerActive(false);
            OnGameStateChanged?.Invoke(CurrentState);
        }

        /// <summary>
        /// Gates timer countdown and movement input during the brief wake-up camera swoop.
        /// </summary>
        public void SetIntroSwoopActive(bool active)
        {
            if (IsIntroSwoopActive == active)
            {
                return;
            }

            IsIntroSwoopActive = active;
            if (CurrentState == GameState.Playing && !IsPaused)
            {
                if (active)
                {
                    SetPlayerActive(false);
                    if (playerController != null)
                    {
                        playerController.SetCursorLocked(true);
                    }
                    else
                    {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                }
                else
                {
                    SetPlayerActive(true);
                }
            }

            OnIntroSwoopChanged?.Invoke(IsIntroSwoopActive);
        }

        /// <summary>
        /// Pauses or resumes the active run without dropping the player's currently held prop.
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (paused && (CurrentState != GameState.Playing || IsIntroSwoopActive))
            {
                return;
            }

            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;

            if (playerController != null)
            {
                playerController.SetControlsEnabled(!paused);
                playerController.SetCursorLocked(!paused);
            }
            else
            {
                Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = paused;
            }

            OnPauseChanged?.Invoke(IsPaused);
        }

        public void TogglePause() => SetPaused(!IsPaused);

        /// <summary>
        /// Reloads the active scene cleanly in both Editor Play Mode and built players.
        /// </summary>
        public void RestartRun()
        {
            Time.timeScale = 1f;
            IsPaused = false;
            ClearIntroSwoopIfNeeded();
            Scene activeScene = SceneManager.GetActiveScene();

            if (activeScene.buildIndex >= 0)
            {
                SceneManager.LoadScene(activeScene.buildIndex);
                return;
            }

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(activeScene.path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    activeScene.path,
                    new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif

            SceneManager.LoadScene(activeScene.name);
        }

        private void ClearPauseIfNeeded()
        {
            Time.timeScale = 1f;
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            OnPauseChanged?.Invoke(false);
        }

        private void ClearIntroSwoopIfNeeded()
        {
            if (!IsIntroSwoopActive)
            {
                return;
            }

            IsIntroSwoopActive = false;
            OnIntroSwoopChanged?.Invoke(false);
        }

        private void ResolveSceneReferences()
        {
            if (playerController == null)
            {
                playerController = FindFirstObjectByType<PlayerController>();
            }

            if (interactionManager == null)
            {
                interactionManager = playerController != null
                    ? playerController.GetComponent<InteractionManager>()
                    : FindFirstObjectByType<InteractionManager>();
            }
        }

        private void SetPlayerActive(bool active)
        {
            if (!active && interactionManager != null)
            {
                interactionManager.ForceDropHeldObject();
            }

            if (playerController != null)
            {
                playerController.SetControlsEnabled(active);
                playerController.SetCursorLocked(active);
            }
            else
            {
                Cursor.lockState = active ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !active;
            }
        }
    }
}
