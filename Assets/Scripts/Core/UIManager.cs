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

        // GameManager reference
        private GameManager gameManager;

        // Properties
        public AppState CurrentState => currentState;
        public bool IsTransitioning => isTransitioning;
        public bool AreServicesInitialized => servicesInitialized;
        public bool IsHardwareSetupCompleted => hardwareSetupCompleted;
        public bool IsTutorialCompleted => tutorialCompleted;

        public bool IsReadyForGameplay =>
            servicesInitialized &&
            hardwareSetupCompleted;

        /// <summary>
        /// Initialize UIManager with GameManager reference
        /// </summary>
        public void Initialize(GameManager manager)
        {
            gameManager = manager;
            Debug.Log("[UIManager] Initialized with GameManager reference");
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
                hardwareSetupUI.SetUIManager(this);

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

            Debug.Log("[UIManager] UI component references set up");
        }

        /// <summary>
        /// Force initial main menu state without transition validation
        /// </summary>
        private void ForceInitialMainMenuState()
        {
            currentState = AppState.MainMenu;

            // Directly activate main menu panel
            SetPanelActive(mainMenuPanel, true);

            // Reset initialization flags
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

            // Validate transition
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

                // All other transitions are invalid
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

            // Store previous state for feedback navigation
            if (newState == AppState.Feedback)
            {
                previousState = oldState;
            }

            // Exit current state
            ExitState(oldState);

            // Wait for transition delay
            yield return new WaitForSeconds(transitionDelay);

            // Update current state
            currentState = newState;

            // Enter new state
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

            // Reset initialization flags when returning to main menu
            hardwareSetupCompleted = false;
            tutorialCompleted = false;

            LogDebug("MainMenu active - services are pre-initialized and ready");

            yield return null;
        }

        private IEnumerator EnterHardwareSetup()
        {
            SetPanelActive(hardwareSetupPanel, true);

            yield return null;

            // Start hardware setup process (services already initialized)
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

            // Initialize site selection UI
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

            // Start tutorial
            if (tutorialUI != null)
            {
                tutorialUI.StartTutorial();
            }
            else
            {
                Debug.LogError("TutorialUI not assigned!");
                OnTutorialComplete();
            }
        }

        private IEnumerator EnterModeSelection()
        {
            SetPanelActive(modeSelectionPanel, true);

            // Reset mode selection UI
            if (modeSelectionUI != null)
            {
                modeSelectionUI.ResetUI();
            }

            yield return null;
        }

        private IEnumerator EnterPlayMode()
        {
            SetPanelActive(audioPlayModePanel, true);
            SetPanelActive(debugPanel, false); // Hidden by default in audio mode
            SetPanelActive(mapPanel, false);   // No map in audio-focused mode

            // Update session ID display
            if (audioPlayModeUI != null && gameManager != null)
            {
                audioPlayModeUI.UpdateSessionId(gameManager.CurrentSessionId);
            }

            // Start service health monitoring
            StartCoroutine(MonitorServiceHealth());

            yield return null;
        }

        private IEnumerator EnterSpectatorMode()
        {
            SetPanelActive(spectatorModeUI, true);
            SetPanelActive(debugPanel, true);
            SetPanelActive(mapPanel, true);

            // Update spectator UI
            if (spectatorModeUI_Script != null && gameManager != null)
            {
                spectatorModeUI_Script.UpdateSessionDisplay(gameManager.CurrentSessionId);
            }

            // Start service health monitoring
            StartCoroutine(MonitorServiceHealth());

            yield return null;
        }

        private IEnumerator EnterFeedback()
        {
            SetPanelActive(feedbackPanel, true);

            // Show the feedback code
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
            {
                gameManager.StartHardwareSetup();
            }
            else
            {
                TransitionToState(AppState.HardwareSetup);
            }
        }

        public void OnSettingsButtonPressed()
        {
            LogDebug("Settings button pressed");
            // TODO: Implement settings panel
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

        public void OnExitToMainMenu()
        {
            LogDebug("Exit to main menu requested");
            TransitionToState(AppState.MainMenu);
        }

        public void ShowError(string message)
        {
            Debug.LogError($"[UIManager] Error: {message}");
            // TODO: Implement proper error display UI
        }

        public void OnHardwareSetupComplete()
        {
            LogDebug("Hardware setup completed");
            hardwareSetupCompleted = true;

            if (gameManager != null)
            {
                gameManager.CompleteHardwareSetup();
            }
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
            {
                gameManager.CompleteSiteSelection();
            }
            else
            {
                // Fallback
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
            {
                gameManager.CompleteTutorial();
            }
            else
            {
                TransitionToState(AppState.ModeSelection);
            }
        }

        public void OnRunTutorialAgain()
        {
            LogDebug("Run tutorial again requested");

            if (gameManager != null)
            {
                gameManager.StartTutorial();
            }
            else
            {
                TransitionToState(AppState.Tutorial);
            }
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
                    // CRITICAL: Tell GameManager to reset its phase FIRST
                    if (gameManager != null)
                    {
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
                    }
                    else
                    {
                        TransitionToState(AppState.MainMenu);
                    }
                    break;

                case AppState.SiteSelection:  
                    if (gameManager != null)
                    {
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.HardwareSetup);
                    }
                    else
                    {
                        TransitionToState(AppState.HardwareSetup);
                    }
                    break;

                case AppState.Tutorial:
                    if (gameManager != null)
                    {
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.SiteSelection);  
                    }
                    else
                    {
                        TransitionToState(AppState.SiteSelection);
                    }
                    break;

                case AppState.ModeSelection:
                    if (tutorialCompleted)
                    {
                        if (gameManager != null)
                        {
                            gameManager.TransitionToPhase(GameManager.ApplicationPhase.Tutorial);
                        }
                        else
                        {
                            TransitionToState(AppState.Tutorial);
                        }
                    }
                    else
                    {
                        if (gameManager != null)
                        {
                            gameManager.TransitionToPhase(GameManager.ApplicationPhase.SiteSelection);  
                        }
                        else
                        {
                            TransitionToState(AppState.SiteSelection);  
                        }
                    }
                    break;

                case AppState.PlayMode:
                case AppState.SpectatorMode:
                    if (gameManager != null)
                    {
                        gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
                    }
                    else
                    {
                        TransitionToState(AppState.MainMenu);
                    }
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
        // Pause Menu Methods
        // ===============================================

        public void ShowPauseMenu()
        {
            Debug.Log("UIManager: Showing pause menu");

            // Show the pause menu UI
            if (pauseMenuUI != null)
            {
                pauseMenuUI.ShowPauseMenu();
            }
            else
            {
                Debug.LogError("UIManager: PauseMenuUI not assigned!");
                return;
            }

            // Pause the game via GameManager
            if (gameManager != null)
            {
                gameManager.SetPaused(true);
            }
            else
            {
                Debug.LogError("UIManager: GameManager reference not set!");
            }
        }

        public void HidePauseMenu()
        {
            Debug.Log("UIManager: Hiding pause menu");

            if (pauseMenuUI != null)
            {
                pauseMenuUI.HidePauseMenu();
            }
        }

        public void OnPauseResume()
        {
            Debug.Log("UIManager: Resume requested from pause menu");

            // Hide the pause menu
            HidePauseMenu();

            // Resume the game via GameManager
            if (gameManager != null)
            {
                gameManager.SetPaused(false);
            }
            else
            {
                Debug.LogError("UIManager: GameManager reference not set!");
            }
        }

        public void OnPauseShare()
        {
            Debug.Log("PauseMenu: Share button pressed (TODO: implement share flow)");
            // TODO: Implement sharing session ID or game state
    
        }

        public void OnPauseSettings()
        {
            Debug.Log("PauseMenu: Settings button pressed (TODO: show settings menu)");
            // TODO: Implement settings panel overlay on pause menu

        }

        public void OnPauseExit()
        {
            Debug.Log("PauseMenu: Exit to main menu");

            // Hide pause menu first
            HidePauseMenu();

            // NOTE: Stop all audio BEFORE transitioning
            if (gameManager != null)
            {
                // Stop all gameplay audio
                gameManager.StopAllGameplayAudio();

                // Unpause the game state
                gameManager.SetPaused(false);

                // Transition back to main menu
                gameManager.TransitionToPhase(GameManager.ApplicationPhase.MainMenu);
            }
            else
            {
                Debug.LogError("UIManager: GameManager reference not set!");
                TransitionToState(AppState.MainMenu);
            }
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
            {
                locationText.text = $"Lat: {latitude:F6}\nLon: {longitude:F6}";
            }

            if (currentState == AppState.SpectatorMode && spectatorModeUI_Script != null)
            {
                spectatorModeUI_Script.UpdateLocationDisplay(latitude, longitude);
            }
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
            {
                Debug.Log($"[UIManager] {message}");
            }
        }
    }
}




























































































