using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.UI;
using LoGa.LudoEngine.Services;
using System;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// UI Manager that handles all UI states in GameScene
    /// Coordinates with GameManager for proper phase synchronization
    /// Services are pre-initialized by LoadingScene before this scene loads
    /// ROBUST PAUSE IMPLEMENTATION: Atomic state changes, validated operations, no timers
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject hardwareSetupPanel;
        [SerializeField] private GameObject siteSelectionPanel;
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private GameObject modeSelectionPanel;
        [SerializeField] private GameObject audioPlayModePanel;
        [SerializeField] private GameObject spectatorModeUI;
        [SerializeField] private GameObject debugPanel;
        [SerializeField] private GameObject mapPanel;
        [SerializeField] private GameObject feedbackPanel;
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject sharePanel;

        [Header("UI Component References")]
        [SerializeField] private MainMenuUI mainMenuUI;
        [SerializeField] private HardwareSetupUI hardwareSetupUI;
        [SerializeField] private SiteSelectionUI siteSelectionUI;
        [SerializeField] private TutorialUI tutorialUI;
        [SerializeField] private ModeSelectionUI modeSelectionUI;
        [SerializeField] private AudioPlayModeUI audioPlayModeUI;
        [SerializeField] private SpectatorModeUI spectatorModeUI_Script;
        [SerializeField] private FeedbackUI feedbackUI;
        [SerializeField] private PauseMenuUI pauseMenuUI;
        [SerializeField] private InventoryUI inventoryUI;
        [SerializeField] private SettingsUI settingsUI;
        [SerializeField] private ShareUI shareUI;

        [Header("Map References")]
        [SerializeField] private RectTransform mapBackground;
        [SerializeField] private RectTransform playerMarker;

        [Header("Map Coordinate Boundaries")]
        [SerializeField] private float northLatitude;
        [SerializeField] private float southLatitude;
        [SerializeField] private float eastLongitude;
        [SerializeField] private float westLongitude;

        [Header("Debug UI")]
        [SerializeField] private TextMeshProUGUI locationText;
        [SerializeField] private TextMeshProUGUI sessionIdText;

        [Header("Transition Settings")]
        [SerializeField] private float transitionDelay = 0.5f;
        [SerializeField] private bool enableDebugLogging = true;

        // Application states
        public enum AppState
        {
            MainMenu,
            HardwareSetup,
            SiteSelection,
            Tutorial,
            ModeSelection,
            PlayMode,
            SpectatorMode,
            Feedback
        }

        // State management
        private AppState currentState = AppState.MainMenu;
        private AppState previousState = AppState.MainMenu;
        private bool isTransitioning = false;
        private bool servicesInitialized = true;
        private bool hardwareSetupCompleted = false;
        private bool tutorialCompleted = false;
        private bool isSyncingWithGameManager = false;

        // ROBUST PAUSE STATE: Single source of truth, no toggles
        private bool isGamePaused = false;

        // GameManager reference
        private GameManager gameManager;
        private HardwareManager hardwareManager;

        // Properties
        public AppState CurrentState => currentState;
        public bool IsTransitioning => isTransitioning;
        public bool AreServicesInitialized => servicesInitialized;
        public bool IsHardwareSetupCompleted => hardwareSetupCompleted;
        public bool IsTutorialCompleted => tutorialCompleted;
        public bool IsGamePaused => isGamePaused;

        public bool IsReadyForGameplay =>
            servicesInitialized &&
            hardwareSetupCompleted;

        /// <summary>
        /// Initialize UIManager with GameManager reference
        /// </summary>
        public void Initialize(GameManager manager, HardwareManager hwManager)
        {
            gameManager = manager;
            hardwareManager = hwManager;
            Debug.Log("[UIManager] Initialized with GameManager and HardwareManager references");
        }

        /// <summary>
        /// Services are pre-initialized, start directly in MainMenu with all panels disabled
        /// </summary>
        private void Start()
        {
            Debug.Log("[UIManager] Starting in GameScene with services pre-initialized");

            servicesInitialized = true;

            // CRITICAL: Disable ALL panels first
            DisableAllPanels();

            // Set UI component references
            SetupUIReferences();

            // Start directly in MainMenu state
            ForceInitialMainMenuState();

            Debug.Log("[UIManager] Started in MainMenu state - ready for user interaction");
        }

        /// <summary>
        /// Disable all UI panels at startup to ensure clean state
        /// </summary>
        private void DisableAllPanels()
        {
            SetPanelActive(mainMenuPanel, false);
            SetPanelActive(hardwareSetupPanel, false);
            SetPanelActive(siteSelectionPanel, false);
            SetPanelActive(tutorialPanel, false);
            SetPanelActive(modeSelectionPanel, false);
            SetPanelActive(audioPlayModePanel, false);
            SetPanelActive(spectatorModeUI, false);
            SetPanelActive(debugPanel, false);
            SetPanelActive(mapPanel, false);
            SetPanelActive(feedbackPanel, false);
            SetPanelActive(inventoryPanel, false);
            SetPanelActive(settingsPanel, false);
            SetPanelActive(sharePanel, false);

            Debug.Log("[UIManager] All panels disabled at startup");
        }

        /// <summary>
        /// Setup UI component references for dependency injection
        /// </summary>
        private void SetupUIReferences()
        {
            if (mainMenuUI != null)
                mainMenuUI.SetUIManager(this);

            if (hardwareSetupUI != null)
            {
                hardwareSetupUI.SetUIManager(this);

                if (hardwareManager != null)
                {
                    hardwareSetupUI.SetHardwareManager(hardwareManager);
                    Debug.Log("[UIManager] HardwareManager reference set in HardwareSetupUI");
                }
                else
                {
                    Debug.LogWarning("[UIManager] HardwareManager reference not available");
                }
            }

            if (siteSelectionUI != null)
                siteSelectionUI.SetUIManager(this);

            if (tutorialUI != null)
                tutorialUI.SetUIManager(this);

            if (modeSelectionUI != null)
                modeSelectionUI.SetUIManager(this);

            if (audioPlayModeUI != null)
                audioPlayModeUI.SetUIManager(this);

            if (spectatorModeUI_Script != null)
                spectatorModeUI_Script.SetUIManager(this);

            if (feedbackUI != null)
                feedbackUI.SetUIManager(this);

            if (pauseMenuUI != null)
                pauseMenuUI.SetUIManager(this);

            if (inventoryUI != null)
                inventoryUI.SetUIManager(this);

            if (settingsUI != null)
                settingsUI.SetUIManager(this);

            if (shareUI != null)
                shareUI.SetUIManager(this);

            Debug.Log("[UIManager] UI component references set up");
        }

        /// <summary>
        /// Force initial main menu state without transition validation
        /// </summary>
        private void ForceInitialMainMenuState()
        {
            currentState = AppState.MainMenu;

            SetPanelActive(mainMenuPanel, true);

            hardwareSetupCompleted = false;
            tutorialCompleted = false;

            Debug.Log("[UIManager] Main menu panel activated - initial state ready");
        }

        /// <summary>
        /// Main state transition method
        /// </summary>
        public void TransitionToState(AppState newState)
        {
            if (currentState == newState || isTransitioning)
            {
                LogDebug($"Ignoring transition request to {newState} (current: {currentState}, transitioning: {isTransitioning})");
                return;
            }

            if (!IsValidTransition(currentState, newState))
            {
                Debug.LogError($"Invalid state transition: {currentState} → {newState}");
                LogValidTransitions(currentState);
                return;
            }

            StartCoroutine(PerformStateTransition(newState));
        }

        /// <summary>
        /// Validate state transitions
        /// </summary>
        private bool IsValidTransition(AppState from, AppState to)
        {
            return (from, to) switch
            {
                // Forward progression
                (AppState.MainMenu, AppState.HardwareSetup) => true,
                (AppState.HardwareSetup, AppState.SiteSelection) => true,
                (AppState.SiteSelection, AppState.Tutorial) => true,
                (AppState.SiteSelection, AppState.ModeSelection) => true,
                (AppState.HardwareSetup, AppState.Tutorial) => true,
                (AppState.HardwareSetup, AppState.ModeSelection) => true,
                (AppState.Tutorial, AppState.ModeSelection) => true,
                (AppState.ModeSelection, AppState.PlayMode) => true,
                (AppState.ModeSelection, AppState.SpectatorMode) => true,

                // Backward navigation
                (AppState.HardwareSetup, AppState.MainMenu) => true,
                (AppState.SiteSelection, AppState.HardwareSetup) => true,
                (AppState.Tutorial, AppState.SiteSelection) => true,
                (AppState.ModeSelection, AppState.SiteSelection) => true,
                (AppState.Tutorial, AppState.HardwareSetup) => true,
                (AppState.ModeSelection, AppState.HardwareSetup) => true,
                (AppState.ModeSelection, AppState.Tutorial) => true,
                (AppState.PlayMode, AppState.MainMenu) => true,
                (AppState.SpectatorMode, AppState.MainMenu) => true,

                // Feedback transitions
                (AppState.MainMenu, AppState.Feedback) => true,
                (AppState.Feedback, AppState.MainMenu) => true,

                // Same state (for resets)
                _ when from == to => true,

                _ => false
            };
        }

        /// <summary>
        /// Log valid transitions for debugging
        /// </summary>
        private void LogValidTransitions(AppState fromState)
        {
            string validTransitions = fromState switch
            {
                AppState.MainMenu => "HardwareSetup, Feedback",
                AppState.HardwareSetup => "MainMenu, SiteSelection",
                AppState.SiteSelection => "HardwareSetup, Tutorial, ModeSelection",
                AppState.Tutorial => "SiteSelection, ModeSelection",
                AppState.ModeSelection => "SiteSelection, Tutorial, PlayMode, SpectatorMode",
                AppState.PlayMode => "MainMenu",
                AppState.SpectatorMode => "MainMenu",
                AppState.Feedback => "MainMenu",
                _ => "None"
            };
            Debug.Log($"Valid transitions from {fromState}: {validTransitions}");
        }

        /// <summary>
        /// Perform the actual state transition
        /// </summary>
        private IEnumerator PerformStateTransition(AppState newState)
        {
            isTransitioning = true;
            AppState oldState = currentState;

            LogDebug($"State transition: {oldState} → {newState}");

            if (newState == AppState.Feedback)
            {
                previousState = oldState;
            }

            ExitState(oldState);

            yield return new WaitForSeconds(transitionDelay);

            currentState = newState;

            yield return StartCoroutine(EnterState(newState));

            isTransitioning = false;
            LogDebug($"State transition complete: {newState}");
        }

        /// <summary>
        /// Exit current state - cleanup and deactivation
        /// </summary>
        private void ExitState(AppState state)
        {
            LogDebug($"Exiting state: {state}");

            switch (state)
            {
                case AppState.MainMenu:
                    SetPanelActive(mainMenuPanel, false);
                    break;

                case AppState.HardwareSetup:
                    SetPanelActive(hardwareSetupPanel, false);
                    break;

                case AppState.SiteSelection:
                    SetPanelActive(siteSelectionPanel, false);
                    break;

                case AppState.Tutorial:
                    SetPanelActive(tutorialPanel, false);
                    break;

                case AppState.ModeSelection:
                    SetPanelActive(modeSelectionPanel, false);
                    break;

                case AppState.PlayMode:
                    SetPanelActive(audioPlayModePanel, false);
                    SetPanelActive(debugPanel, false);
                    SetPanelActive(mapPanel, false);
                    StopCoroutine(MonitorServiceHealth());
                    break;

                case AppState.SpectatorMode:
                    SetPanelActive(spectatorModeUI, false);
                    SetPanelActive(debugPanel, false);
                    SetPanelActive(mapPanel, false);
                    StopCoroutine(MonitorServiceHealth());
                    break;

                case AppState.Feedback:
                    SetPanelActive(feedbackPanel, false);
                    break;
            }
        }

        /// <summary>
        /// Enter new state - activation and initialization
        /// </summary>
        private IEnumerator EnterState(AppState state)
        {
            LogDebug($"Entering state: {state}");

            switch (state)
            {
                case AppState.MainMenu:
                    yield return StartCoroutine(EnterMainMenu());
                    break;

                case AppState.HardwareSetup:
                    yield return StartCoroutine(EnterHardwareSetup());
                    break;

                case AppState.SiteSelection:
                    yield return StartCoroutine(EnterSiteSelection());
                    break;

                case AppState.Tutorial:
                    yield return StartCoroutine(EnterTutorial());
                    break;

                case AppState.ModeSelection:
                    yield return StartCoroutine(EnterModeSelection());
                    break;

                case AppState.PlayMode:
                    yield return StartCoroutine(EnterPlayMode());
                    break;

                case AppState.SpectatorMode:
                    yield return StartCoroutine(EnterSpectatorMode());
                    break;

                case AppState.Feedback:
                    yield return StartCoroutine(EnterFeedback());
                    break;
            }
        }

        // ===============================================
        // State Entry Methods
        // ===============================================

        private IEnumerator EnterMainMenu()
        {
            SetPanelActive(mainMenuPanel, true);

            hardwareSetupCompleted = false;
            tutorialCompleted = false;

            LogDebug("MainMenu active - services are pre-initialized and ready");

            yield return null;
        }

        private IEnumerator EnterHardwareSetup()
        {
            SetPanelActive(hardwareSetupPanel, true);

            yield return null;

            if (hardwareSetupUI != null)
            {
                hardwareSetupUI.StartHardwareSetup();
            }
            else
            {
                Debug.LogError("HardwareSetupUI not assigned!");
                OnHardwareSetupComplete();
            }
        }

        private IEnumerator EnterSiteSelection()
        {
            SetPanelActive(siteSelectionPanel, true);

            yield return null;

            if (siteSelectionUI != null)
            {
                siteSelectionUI.InitializeSiteList();
            }
            else
            {
                Debug.LogError("SiteSelectionUI not assigned!");
            }
        }

        private IEnumerator EnterTutorial()
        {
            SetPanelActive(tutorialPanel, true);

            yield return null;

            if (tutorialUI != null)
            {
                tutorialUI.ShowTutorialInProgress();
            }
            else
            {
                Debug.LogWarning("TutorialUI not assigned - continuing without UI");
            }

            if (gameManager != null)
            {
                gameManager.StartGameplayTutorial();
            }
            else
            {
                Debug.LogError("GameManager reference not set - cannot start tutorial!");
                OnTutorialComplete();
            }
        }

        private IEnumerator EnterModeSelection()
        {
            SetPanelActive(modeSelectionPanel, true);

            if (modeSelectionUI != null)
            {
                modeSelectionUI.ResetUI();
            }

            yield return null;
        }

        private IEnumerator EnterPlayMode()
        {
            SetPanelActive(audioPlayModePanel, true);
            SetPanelActive(debugPanel, false);
            SetPanelActive(mapPanel, false);

            StartCoroutine(MonitorServiceHealth());

            yield return null;
             // signal GameManager that UI is ready — POIManager can start
            GameManager.Instance?.ReadyToPlay();

            Debug.Log("[UIManager] PlayMode UI ready - gameplay activated");
        }

        private IEnumerator EnterSpectatorMode()
        {
            SetPanelActive(spectatorModeUI, true);
            SetPanelActive(debugPanel, true);
            SetPanelActive(mapPanel, true);

            StartCoroutine(MonitorServiceHealth());

            yield return null;
        }

        private IEnumerator EnterFeedback()
        {
            SetPanelActive(feedbackPanel, true);

            if (feedbackUI != null)
            {
                feedbackUI.ShowFeedbackCode();
            }
            else
            {
                Debug.LogError("FeedbackUI not assigned!");
            }

            yield return null;
        }

        // ===============================================
        // Public Methods for UI Components to Call
        // ===============================================

        public void OnPlayButtonPressed()
        {
            LogDebug("Play button pressed - starting hardware setup");

            if (gameManager != null)
                gameManager.StartHardwareSetup();
            else
                TransitionToState(AppState.HardwareSetup);
        }

        public void OnSettingsButtonPressed()
        {
            Debug.Log("[UIManager] Settings button pressed");

            if (settingsUI != null)
                settingsUI.Show();
            else
                Debug.LogError("[UIManager] SettingsUI not assigned!");
        }

        public void OnFeedbackButtonPressed()
        {
            LogDebug("Feedback button pressed - showing feedback code");
            TransitionToState(AppState.Feedback);
        }

        public void OnFeedbackClosed()
        {
            LogDebug("Feedback closed - returning to previous state");
            TransitionToState(previousState);
        }

        public void OnSettingsClose()
        {
            Debug.Log("[UIManager] Settings closed");

            if (settingsUI != null)
                settingsUI.Hide();
        }

        public void OnShareClose()
        {
            Debug.Log("[UIManager] Share panel closed");

            if (shareUI != null)
                shareUI.Hide();
        }

        public void OnExitToMainMenu()
        {
            LogDebug("Exit to main menu requested");
            TransitionToState(AppState.MainMenu);
        }

        public void ShowError(string message)
        {
            Debug.LogError($"[UIManager] Error: {message}");
        }

        public void OnHardwareSetupComplete()
        {
            LogDebug("Hardware setup completed");
            hardwareSetupCompleted = true;

            if (gameManager != null)
                gameManager.CompleteHardwareSetup();
            else
            {
                bool hasCompletedTutorial = PlayerPrefs.HasKey("TutorialCompleted");
                AppState nextState = hasCompletedTutorial ? AppState.ModeSelection : AppState.Tutorial;
                TransitionToState(nextState);
            }
        }

        public void OnSiteSelected()
        {
            LogDebug("Site selected - transitioning to next phase");

            if (gameManager != null)
                gameManager.CompleteSiteSelection();
            else
            {
                bool hasCompletedTutorial = PlayerPrefs.HasKey("TutorialCompleted");
                AppState nextState = hasCompletedTutorial ? AppState.ModeSelection : AppState.Tutorial;
                TransitionToState(nextState);
            }
        }

        public void OnTutorialComplete()
        {
            LogDebug("Tutorial completed");
            tutorialCompleted = true;

            if (gameManager != null)
                gameManager.CompleteTutorial();
            else
                TransitionToState(AppState.ModeSelection);
        }

        public void OnRunTutorialAgain()
        {
            LogDebug("Run tutorial again requested");

            if (gameManager != null)
                gameManager.StartTutorial();
            else
                TransitionToState(AppState.Tutorial);
        }

        public async void OnPlayerModeSelected()
        {
            LogDebug("Player mode selected");

            if (gameManager != null)
            {
                if (currentState != AppState.ModeSelection)
                {
                    Debug.LogError("Cannot start player mode - UI not in correct state");
                    if (modeSelectionUI != null)
                    {
                        modeSelectionUI.ShowError("Invalid state - please try again");
                        modeSelectionUI.SetInteractable(true);
                    }
                    return;
                }

                bool success = await gameManager.StartPlayerMode();

                if (!success)
                {
                    LogDebug("Player mode failed - staying in mode selection");
                    if (modeSelectionUI != null)
                    {
                        modeSelectionUI.ShowError("Failed to start player mode");
                        modeSelectionUI.SetInteractable(true);
                    }
                }
            }
        }

        public async void OnSpectatorModeSelected(string sessionId)
        {
            LogDebug($"Spectator mode selected with session: {sessionId}");

            if (gameManager != null)
            {
                if (currentState != AppState.ModeSelection)
                {
                    Debug.LogError("Cannot start spectator mode - UI not in correct state");
                    if (modeSelectionUI != null)
                    {
                        modeSelectionUI.ShowError("Invalid state - please try again");
                        modeSelectionUI.SetInteractable(true);
                    }
                    return;
                }

                bool success = await gameManager.StartSpectatorMode(sessionId);

                if (!success)
                {
                    LogDebug("Spectator mode failed - staying in mode selection");
                    if (modeSelectionUI != null)
                    {
                        modeSelectionUI.ShowError("Failed to connect to session");
                        modeSelectionUI.SetInteractable(true);
                    }
                }
            }
        }

        public void OnBackButtonPressed()
        {
            LogDebug("Back button pressed");

            switch (currentState)
            {
                case AppState.HardwareSetup:
                    if (gameManager != null)
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
                    else
                        TransitionToState(AppState.MainMenu);
                    break;

                case AppState.SiteSelection:
                    if (gameManager != null)
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.HardwareSetup);
                    else
                        TransitionToState(AppState.HardwareSetup);
                    break;

                case AppState.Tutorial:
                    if (gameManager != null)
                    {
                        gameManager.ExitTutorial();
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.ModeSelection);
                    }
                    else
                    {
                        TransitionToState(AppState.ModeSelection);
                    }
                    break;

                case AppState.ModeSelection:
                    if (tutorialCompleted)
                    {
                        if (gameManager != null)
                            gameManager.TransitionToPhase(GameManager.ApplicationPhase.Tutorial);
                        else
                            TransitionToState(AppState.Tutorial);
                    }
                    else
                    {
                        if (gameManager != null)
                            gameManager.TransitionToPhase(GameManager.ApplicationPhase.SiteSelection);
                        else
                            TransitionToState(AppState.SiteSelection);
                    }
                    break;

                case AppState.PlayMode:
                case AppState.SpectatorMode:
                    if (gameManager != null)
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
                    else
                        TransitionToState(AppState.MainMenu);
                    break;

                case AppState.Feedback:
                    TransitionToState(previousState);
                    break;
            }
        }

        public void ReturnToMainMenu()
        {
            LogDebug("Force returning to main menu");
            TransitionToState(AppState.MainMenu);
        }

        public void ReturnToModeSelection()
        {
            LogDebug("Returning to mode selection");
            TransitionToState(AppState.ModeSelection);
        }

        public void StartTutorialAgain()
        {
            LogDebug("Starting tutorial again from mode selection");

            if (gameManager != null)
                gameManager.TransitionToPhase(GameManager.ApplicationPhase.Tutorial);
            else
                TransitionToState(AppState.Tutorial);
        }

        public void HandleServiceError(string serviceName, string error)
        {
            Debug.LogError($"Service error in {serviceName}: {error}");

            switch (currentState)
            {
                case AppState.HardwareSetup:
                    TransitionToState(AppState.MainMenu);
                    break;

                case AppState.Tutorial:
                    OnTutorialComplete();
                    break;

                case AppState.PlayMode:
                case AppState.SpectatorMode:
                    TransitionToState(AppState.ModeSelection);
                    break;

                case AppState.Feedback:
                    TransitionToState(previousState);
                    break;
            }
        }

        // ===============================================
        // GameManager Event Handlers
        // ===============================================

        public void OnPhaseChanged(GameManager.ApplicationPhase newPhase)
        {
            if (isSyncingWithGameManager) return;

            LogDebug($"GameManager phase changed to: {newPhase}");

            isSyncingWithGameManager = true;
            try
            {
                AppState targetUIState = MapPhaseToUIState(newPhase);

                if (targetUIState != currentState && !isTransitioning)
                {
                    LogDebug($"Syncing UI state to match GameManager phase: {targetUIState}");
                    TransitionToState(targetUIState);
                }
            }
            finally
            {
                isSyncingWithGameManager = false;
            }
        }

        private AppState MapPhaseToUIState(GameManager.ApplicationPhase phase)
        {
            return phase switch
            {
                GameManager.ApplicationPhase.MainMenu => AppState.MainMenu,
                GameManager.ApplicationPhase.HardwareSetup => AppState.HardwareSetup,
                GameManager.ApplicationPhase.SiteSelection => AppState.SiteSelection,
                GameManager.ApplicationPhase.Tutorial => AppState.Tutorial,
                GameManager.ApplicationPhase.ModeSelection => AppState.ModeSelection,
                GameManager.ApplicationPhase.PlayerMode => AppState.PlayMode,
                GameManager.ApplicationPhase.SpectatorMode => AppState.SpectatorMode,
                _ => currentState
            };
        }

        // ===============================================
        // ROBUST PAUSE MENU METHODS
        // ===============================================

        /// <summary>
        /// Pause the game - NOT a toggle, explicit pause operation.
        /// Menu must show successfully before game pauses.
        /// </summary>
        public void PauseGame()
        {
            if (isGamePaused)
            {
                Debug.LogWarning("UIManager: Game already paused - ignoring");
                return;
            }

            if (gameManager == null)
            {
                Debug.LogError("UIManager: Cannot pause - GameManager not assigned!");
                return;
            }

            if (pauseMenuUI == null)
            {
                Debug.LogError("UIManager: Cannot pause - PauseMenuUI not assigned!");
                return;
            }

            Debug.Log("UIManager: Pausing game");

            bool menuShown = pauseMenuUI.Show();

            if (!menuShown)
            {
                Debug.LogError("UIManager: Failed to show pause menu - aborting pause!");
                return;
            }

            gameManager.Pause();
            isGamePaused = true;

            if (audioPlayModeUI != null)
                audioPlayModeUI.OnGamePaused();

            Debug.Log("UIManager: Game paused successfully");
        }

        /// <summary>
        /// Resume the game - NOT a toggle, explicit resume operation.
        /// </summary>
        public void ResumeGame()
        {
            if (!isGamePaused)
            {
                Debug.LogWarning("UIManager: Game not paused - ignoring resume");
                return;
            }

            if (gameManager == null)
            {
                Debug.LogError("UIManager: Cannot resume - GameManager not assigned!");
                return;
            }

            Debug.Log("UIManager: Resuming game");

            if (pauseMenuUI != null)
                pauseMenuUI.Hide();

            gameManager.Resume();
            isGamePaused = false;

            if (audioPlayModeUI != null)
                audioPlayModeUI.OnGameResumed();

            Debug.Log("UIManager: Game resumed successfully");
        }

        /// <summary>
        /// Called by Resume button in pause menu.
        /// </summary>
        public void OnPauseResume()
        {
            Debug.Log("UIManager: Resume button pressed in pause menu");
            ResumeGame();
        }

        /// <summary>
        /// Called by Share button in pause menu.
        /// </summary>
        public void OnPauseShare()
        {
            Debug.Log("[UIManager] Share button pressed from pause menu");

            if (GameManager.Instance == null || string.IsNullOrEmpty(GameManager.Instance.CurrentSessionId))
            {
                Debug.LogWarning("[UIManager] No active session to share");
                return;
            }

            string sessionId = GameManager.Instance.CurrentSessionId;

            if (shareUI != null)
                shareUI.Show(sessionId);
            else
                Debug.LogError("[UIManager] ShareUI not assigned!");
        }

        /// <summary>
        /// Called by Settings button in pause menu.
        /// </summary>
        public void OnPauseSettings()
        {
            Debug.Log("[UIManager] Settings button pressed from pause menu");
            OnSettingsButtonPressed();
        }

        /// <summary>
        /// Called by Exit button in pause menu.
        /// </summary>
        public void OnPauseExit()
        {
            Debug.Log("UIManager: Exit to main menu from pause menu");

            if (isGamePaused)
                ResumeGame();

            if (gameManager != null)
            {
                gameManager.StopAllGameplayAudio();
                gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
            }
            else
            {
                TransitionToState(AppState.MainMenu);
            }
        }

        // Deprecated — kept for backward compatibility
        public void ShowPauseMenu()
        {
            Debug.LogWarning("UIManager: ShowPauseMenu() is deprecated - use PauseGame() instead");
            PauseGame();
        }

        public void HidePauseMenu()
        {
            Debug.LogWarning("UIManager: HidePauseMenu() is deprecated - use ResumeGame() instead");
            ResumeGame();
        }

        // ===============================================
        // Inventory Methods
        // ===============================================

        public void OnInventoryButtonPressed()
        {
            Debug.Log("[UIManager] Inventory button pressed - opening inventory");

            if (inventoryUI != null)
                inventoryUI.Show();
        }

        public void OnInventoryClose()
        {
            Debug.Log("[UIManager] Inventory closed");

            if (inventoryUI != null)
                inventoryUI.Hide();
        }

        // ===============================================
        // Map and Location Methods
        // ===============================================

        public Vector2 GetScreenPosition(float latitude, float longitude)
        {
            float normalizedX = (longitude - westLongitude) / (eastLongitude - westLongitude);
            float normalizedY = (latitude - southLatitude) / (northLatitude - southLatitude);

            float width = mapBackground.rect.width;
            float height = mapBackground.rect.height;

            return new Vector2(
                (normalizedX * width) - (width / 2),
                (normalizedY * height) - (height / 2)
            );
        }

        public void UpdateLocationDisplay(float latitude, float longitude)
        {
            Vector2 position = GetScreenPosition(latitude, longitude);

            if (playerMarker != null)
                playerMarker.anchoredPosition = position;

            if (locationText != null)
                locationText.text = $"Lat: {latitude:F6}\nLon: {longitude:F6}";

            // Notify spectator UI that live data is arriving
            if (currentState == AppState.SpectatorMode && spectatorModeUI_Script != null)
                spectatorModeUI_Script.UpdateLocationDisplay(latitude, longitude);
        }

        public void ShowConnectionError()
        {
            if (modeSelectionUI != null)
            {
                modeSelectionUI.ShowError("Failed to connect to session");
                modeSelectionUI.SetInteractable(true);
            }
        }

        // ===============================================
        // Service Health Monitoring
        // ===============================================

        private IEnumerator MonitorServiceHealth()
        {
            while (currentState == AppState.PlayMode || currentState == AppState.SpectatorMode)
            {
                yield return new WaitForSeconds(5f);

                bool audioOk = ServiceLocator.GetService<IAudioService>() != null;
                bool locationOk = ServiceLocator.GetService<ILocationService>() != null;
                bool headTrackingOk = ServiceLocator.GetService<IHeadTrackingService>() != null;

                if (!audioOk || !locationOk || !headTrackingOk)
                {
                    Debug.LogWarning($"Service health check failed: Audio={audioOk}, Location={locationOk}, HeadTracking={headTrackingOk}");
                }
            }
        }

        // ===============================================
        // Helper Methods
        // ===============================================

        private void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
            {
                panel.SetActive(active);
                LogDebug($"Panel {panel.name} set to {active}");
            }
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[UIManager] {message}");
        }
    }
}