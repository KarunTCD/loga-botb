using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;
using LoGa.LudoEngine.Utilities;
using FMODUnity;
using FMOD.Studio;
using System.Linq;
using System.Collections;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Master Controller for the Battle of Boyne game
    /// Manages application phases, business logic, and coordinates all game systems
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        #region Enums and Data Structures
        public enum ApplicationPhase
        {
            Initializing,    // Services loading, validation
            MainMenu,        // Welcome screen
            HardwareSetup,   // Device connection and validation
            Tutorial,        // First-time user guidance
            ModeSelection,   // Player vs Spectator choice
            PlayerMode,      // Active gameplay as player
            SpectatorMode    // Watching another player
        }

        public enum GameMode
        {
            Inactive,
            Player,
            Spectator
        }

        public enum GameState
        {
            Running,
            Suspended
        }

        public enum GameplayState
        {
            Wander,    // Normal exploration
            Interact,  // POI dialogue/music
            Combat,    // Mercenary combat
            Recovery,  // Berry collection for health
            Paused     // Game paused
        }

        #endregion

        #region Instance and Core Fields

        public static GameManager Instance { get; private set; }

        [Header("Core Systems")]
        [SerializeField] private UIManager uiManager;
        [SerializeField] private MapManager mapManager;
        [SerializeField] private POIManager poiManager;

        [Header("Combat System Audio")]
        [SerializeField] private EventReference mercenaryEncounterEvent;
        [SerializeField] private EventReference mercenaryFootstepsEvent;
        [SerializeField] private EventReference mercenaryAttackEvent;
        [SerializeField] private EventReference attackImpactEvent;
        [SerializeField] private EventReference heartbeatEvent;
        [SerializeField] private EventReference berryAmbientEvent;
        [SerializeField] private EventReference berryCollectionEvent;

        [Header("Universal Audio")]
        [SerializeField] private EventReference mainAmbientEvent;

        [Header("Combat Settings")]
        [SerializeField] private float combatTriggerCheckInterval = 2f;
        [SerializeField] private float approachDuration = 4f;

        #endregion

        #region State Variables

        // Application state
        private ApplicationPhase currentPhase = ApplicationPhase.Initializing;
        private GameMode currentMode = GameMode.Inactive;
        private GameplayState currentGameplayState = GameplayState.Wander;
        private GameplayState previousGameplayState = GameplayState.Wander;
        private GameState gameState = GameState.Suspended;

        // Session data
        private string currentSessionId;
        private bool isPlayerInPOIProximity = false;

        // Health system
        private int maxHealth;
        private int playerHealth;

        // System initialization flags
        private bool audioInitialized = false;
        private bool systemsReady = false;
        private bool hasDataConfiguration = false;

        #endregion

        #region Audio Instances

        private EventInstance mainAmbientInstance;
        private EventInstance mercenaryEncounterInstance;
        private EventInstance currentFootstepsInstance;
        private EventInstance currentAttackInstance;
        private EventInstance sharedBerryInstance;
        private EventInstance heartbeatInstance;

        #endregion

        #region Combat System

        private List<Mercenary> activeMercenaries = new List<Mercenary>();
        private List<Berry> activeBerries = new List<Berry>();
        private float combatCheckTimer = 0f;
        private bool isInCombat = false;
        private int currentAttackIndex = 0;
        private int currentCombatType = 0;
        private Mercenary currentAttackingMercenary;

        // Combat artifact combinations based on rewards from JSON
        private readonly Dictionary<string, List<int>> combatTriggers = new Dictionary<string, List<int>>
        {
            { "combat_oak_crops", new List<int> { 1, 4 } },               // Oak (1) + Modern Crops (4)
            { "combat_royal_artifacts", new List<int> { 9, 10 } }         // Royal Seal (9) + Golden Sun (10)
        };

        #endregion

        #region Spectator System

        private Vector2 spectatorLocation = Vector2.zero;
        private float spectatorHeading = 0f;
        private bool isReceivingSpectatorData = false;

        #endregion

        #region Service References

        private IGameDataService gameDataService;
        private IGameDataService GameDataService
        {
            get
            {
                if (gameDataService == null)
                    gameDataService = ServiceLocator.GetService<IGameDataService>();
                return gameDataService;
            }
        }

        private IStorageService StorageService => ServiceLocator.GetService<IStorageService>();
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();
        private IAnalyticsService AnalyticsService => ServiceLocator.GetService<IAnalyticsService>();

        #endregion

        #region Public Properties

        public ApplicationPhase CurrentPhase => currentPhase;
        public GameMode CurrentMode => currentMode;
        public GameplayState CurrentGameplayState => currentGameplayState;
        public GameState CurrentGameState => gameState;
        public string CurrentSessionId => currentSessionId;
        public bool IsSpectatorMode => currentMode == GameMode.Spectator;
        public int PlayerHealth => playerHealth;
        public bool IsInCombat => isInCombat;
        public Vector2 SpectatorLocation => spectatorLocation;
        public float SpectatorHeading => spectatorHeading;
        public bool IsReceivingSpectatorData => isReceivingSpectatorData;
        public bool SystemsReady => systemsReady;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                LoadHealthFromPreferences();
                Debug.Log("GameManager: Instance created");
            }
            else
            {
                Destroy(gameObject);
                Debug.LogWarning("GameManager: Duplicate instance destroyed");
            }
        }

        private async void Start()
        {
            Debug.Log("GameManager: Starting master controller initialization");

            try
            {
                await InitializeSystems();
                StartApplicationFlow();
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Critical initialization failure - {e.Message}");
                HandleCriticalFailure("System initialization failed");
            }
        }

        private void Update()
        {
            if (!systemsReady) return;

            if (currentMode == GameMode.Player && gameState == GameState.Running)
            {
                UpdateGameplayMode();
            }
        }

        #endregion

        #region System Initialization

        private async Task InitializeSystems()
        {
            Debug.Log("GameManager: Initializing all systems");

            if (GameDataService == null)
            {
                Debug.LogWarning("GameManager: GameDataService not available - proceeding without data configuration");
            }
            else if (!GameDataService.IsDataLoaded)
            {
                Debug.Log("GameManager: Waiting for game data to load...");
                float timeout = 5f;
                float elapsed = 0f;
                while (!GameDataService.IsDataLoaded && elapsed < timeout)
                {
                    await Task.Delay(100);
                    elapsed += 0.1f;
                }

                if (!GameDataService.IsDataLoaded)
                {
                    Debug.LogWarning("GameManager: Game data not loaded within timeout - proceeding with defaults");
                }
            }

            if (GameDataService != null && GameDataService.IsDataLoaded)
            {
                ApplyGameDataConfiguration();
                hasDataConfiguration = true;
            }

            if (!InitializeUIManager())
            {
                throw new Exception("UIManager initialization failed");
            }

            if (!await WaitForCriticalServices())
            {
                throw new Exception("Critical services failed to initialize");
            }

            if (!await InitializeAudioSystems())
            {
                Debug.LogWarning("GameManager: Audio systems failed to initialize - continuing without audio");
            }

            SubscribeToEvents();

            systemsReady = true;
            Debug.Log("GameManager: All systems initialized successfully");
        }

        private void ApplyGameDataConfiguration()
        {
            if (GameDataService?.GameConfig == null) return;

            var config = GameDataService.GameConfig;

            Debug.Log("GameManager: Applying data game configuration");

            var bounds = config.gameBounds;
            if (bounds != null)
            {
                Debug.Log($"GameManager: Game bounds configured");
                Debug.Log($"  North: {bounds.north}, South: {bounds.south}");
                Debug.Log($"  East: {bounds.east}, West: {bounds.west}");
            }

            Debug.Log($"GameManager: Default time layer: {config.defaultTimeLayer}");
            Debug.Log($"GameManager: Navigation settings - Base cues: {config.baseMaxActiveCues}, Max cues: {config.maxMaxActiveCues}");
        }

        private bool InitializeUIManager()
        {
            if (uiManager == null)
            {
                Debug.LogError("GameManager: UIManager reference not assigned in inspector");
                return false;
            }

            try
            {
                uiManager.Initialize(this);
                Debug.Log("GameManager: UIManager initialized successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: UIManager initialization failed - {e.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForCriticalServices()
        {
            Debug.Log("GameManager: Waiting for critical services");

            try
            {
                var timeoutTask = Task.Delay(10000);

                var audioTask = ServiceLocator.GetInitializedService<IAudioService>();
                var locationTask = ServiceLocator.GetInitializedService<ILocationService>();
                var headTrackingTask = ServiceLocator.GetInitializedService<IHeadTrackingService>();
                var storageTask = ServiceLocator.GetInitializedService<IStorageService>();

                await Task.WhenAny(
                    Task.WhenAll(audioTask, locationTask, headTrackingTask, storageTask),
                    timeoutTask
                );

                if (timeoutTask.IsCompleted)
                {
                    Debug.LogError("GameManager: Service initialization timeout");
                    return false;
                }

                var audioService = await audioTask;
                var locationService = await locationTask;
                var headTrackingService = await headTrackingTask;
                var storageService = await storageTask;

                bool allServicesReady = audioService != null && locationService != null &&
                                       headTrackingService != null && storageService != null;

                if (allServicesReady)
                {
                    Debug.Log("GameManager: All critical services ready");
                    return true;
                }
                else
                {
                    Debug.LogError($"GameManager: Missing services - Audio:{audioService != null}, Location:{locationService != null}, HeadTracking:{headTrackingService != null}, Storage:{storageService != null}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Service initialization error - {e.Message}");
                return false;
            }
        }

        private async Task<bool> InitializeAudioSystems()
        {
            try
            {
                var audioService = await ServiceLocator.GetInitializedService<IAudioService>();
                if (audioService == null)
                {
                    Debug.LogError("GameManager: AudioService not available");
                    return false;
                }

                if (!InitializeCombatAudio())
                {
                    Debug.LogError("GameManager: Combat audio initialization failed");
                    return false;
                }

                if (!InitializeAmbientMusic())
                {
                    Debug.LogError("GameManager: Ambient music initialization failed");
                    return false;
                }

                audioInitialized = true;
                Debug.Log("GameManager: Audio systems initialized successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Audio initialization error - {e.Message}");
                return false;
            }
        }

        private bool InitializeCombatAudio()
        {
            if (AudioService == null) return false;

            try
            {
                if (!heartbeatEvent.IsNull)
                {
                    heartbeatInstance = AudioService.CreateAudioInstance(heartbeatEvent);
                    if (heartbeatInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogError("GameManager: Failed to create heartbeat instance");
                        return false;
                    }
                }

                if (!berryAmbientEvent.IsNull)
                {
                    sharedBerryInstance = AudioService.CreateAudioInstance(berryAmbientEvent);
                    if (sharedBerryInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogError("GameManager: Failed to create berry ambient instance");
                        return false;
                    }
                }

                Debug.Log("GameManager: Combat audio initialized");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Combat audio initialization error - {e.Message}");
                return false;
            }
        }

        private bool InitializeAmbientMusic()
        {
            if (AudioService == null) return false;

            try
            {
                if (!mainAmbientEvent.IsNull)
                {
                    mainAmbientInstance = AudioService.CreateAudioInstance(mainAmbientEvent);
                    if (mainAmbientInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogError("GameManager: Failed to create ambient music instance");
                        return false;
                    }
                    Debug.Log("GameManager: Ambient music initialized");
                    return true;
                }
                else
                {
                    Debug.LogError("GameManager: Main ambient event not assigned");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Ambient music initialization error - {e.Message}");
                return false;
            }
        }

        private void SubscribeToEvents()
        {
            try
            {
                if (TimeLayerManager.Instance != null)
                {
                    TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;
                    Debug.Log("GameManager: Subscribed to TimeLayerManager events");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Event subscription error - {e.Message}");
            }
        }

        private void StartApplicationFlow()
        {
            Debug.Log("GameManager: Starting application flow");
            TransitionToPhase(ApplicationPhase.MainMenu);
        }

        private void HandleCriticalFailure(string reason)
        {
            Debug.LogError($"GameManager: Critical failure - {reason}");
            uiManager?.ShowError($"Critical system failure: {reason}");
        }

        #endregion

        #region Phase Management

        public void TransitionToPhase(ApplicationPhase newPhase)
        {
            if (!systemsReady)
            {
                Debug.LogWarning("GameManager: Phase transition requested before systems ready");
                return;
            }

            // CRITICAL FIX: Allow re-entering same phase (for cleanup/reset scenarios)
            if (currentPhase == newPhase)
            {
                Debug.LogWarning($"GameManager: Re-entering phase {newPhase} (cleanup scenario)");

                // Allow this for cleanup scenarios (going back to HardwareSetup after cancellation)
                // But skip the transition if we're already stable in this phase
                if (newPhase != ApplicationPhase.HardwareSetup && newPhase != ApplicationPhase.MainMenu)
                {
                    Debug.Log($"GameManager: Already in phase {newPhase} - ignoring");
                    return;
                }
            }

            if (!IsValidPhaseTransition(currentPhase, newPhase))
            {
                Debug.LogError($"GameManager: Invalid phase transition {currentPhase} → {newPhase}");
                return;
            }

            Debug.Log($"GameManager: Phase transition {currentPhase} → {newPhase}");

            try
            {
                ExitCurrentPhase();
                EnterNewPhase(newPhase);
                currentPhase = newPhase;

                uiManager?.OnPhaseChanged(newPhase);
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Phase transition error - {e.Message}");
                HandleCriticalFailure($"Phase transition failed: {e.Message}");
            }
        }

        private bool IsValidPhaseTransition(ApplicationPhase from, ApplicationPhase to)
        {
            return (from, to) switch
            {
                (ApplicationPhase.Initializing, ApplicationPhase.MainMenu) => true,
                (ApplicationPhase.MainMenu, ApplicationPhase.HardwareSetup) => true,
                (ApplicationPhase.HardwareSetup, ApplicationPhase.Tutorial) => true,
                (ApplicationPhase.HardwareSetup, ApplicationPhase.ModeSelection) => true,
                (ApplicationPhase.Tutorial, ApplicationPhase.ModeSelection) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.PlayerMode) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.SpectatorMode) => true,
                (ApplicationPhase.HardwareSetup, ApplicationPhase.MainMenu) => true,
                (ApplicationPhase.Tutorial, ApplicationPhase.HardwareSetup) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.HardwareSetup) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.Tutorial) => true,
                (ApplicationPhase.PlayerMode, ApplicationPhase.MainMenu) => true,
                (ApplicationPhase.SpectatorMode, ApplicationPhase.MainMenu) => true,
                _ => false
            };
        }

        private void ExitCurrentPhase()
        {
            switch (currentPhase)
            {
                case ApplicationPhase.PlayerMode:
                    ExitPlayerMode();
                    break;
                case ApplicationPhase.SpectatorMode:
                    ExitSpectatorMode();
                    break;
            }
        }

        private void EnterNewPhase(ApplicationPhase phase)
        {
            switch (phase)
            {
                case ApplicationPhase.MainMenu:
                    EnterMainMenu();
                    break;
                case ApplicationPhase.PlayerMode:
                    EnterPlayerMode();
                    break;
                case ApplicationPhase.SpectatorMode:
                    EnterSpectatorMode();
                    break;
            }
        }

        private void EnterMainMenu()
        {
            Debug.Log("GameManager: Entering MainMenu phase");
            SuspendGameplaySystems();
        }

        private void EnterPlayerMode()
        {
            Debug.Log("GameManager: Entering PlayerMode phase");
            ActivateGameplaySystems();
            SetInternalGameMode(GameMode.Player);
            StartAmbientMusic();
        }

        private void EnterSpectatorMode()
        {
            Debug.Log("GameManager: Entering SpectatorMode phase");
            ActivateGameplaySystems();
            SetInternalGameMode(GameMode.Spectator);
            StartAmbientMusic();
        }

        private void ExitPlayerMode()
        {
            Debug.Log("GameManager: Exiting PlayerMode phase");
            SuspendGameplaySystems();
            StopAmbientMusic();
            SetInternalGameMode(GameMode.Inactive);
            CleanupPlayerSession();
        }

        private void ExitSpectatorMode()
        {
            Debug.Log("GameManager: Exiting SpectatorMode phase");
            SuspendGameplaySystems();
            StopAmbientMusic();
            SetInternalGameMode(GameMode.Inactive);
            CleanupSpectatorSession();
        }

        private void SuspendGameplaySystems()
        {
            if (poiManager != null) poiManager.enabled = false;
            if (mapManager != null) mapManager.enabled = false;

            currentGameplayState = GameplayState.Paused;
            gameState = GameState.Suspended;

            Debug.Log("GameManager: Gameplay systems suspended");
        }

        private void ActivateGameplaySystems()
        {
            if (poiManager != null) poiManager.enabled = true;
            if (mapManager != null) mapManager.enabled = true;

            currentGameplayState = GameplayState.Wander;
            gameState = GameState.Running;

            Debug.Log("GameManager: Gameplay systems activated");
        }

        private void SetInternalGameMode(GameMode mode)
        {
            currentMode = mode;
            Debug.Log($"GameManager: Internal game mode set to {mode}");
        }

        #endregion

        #region Business Operations

        public async Task<bool> StartPlayerMode()
        {
            Debug.Log("GameManager: StartPlayerMode requested");

            try
            {
                currentSessionId = System.Guid.NewGuid().ToString();
                Debug.Log($"GameManager: Generated session ID: {currentSessionId}");

                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();
                if (firebaseService == null)
                {
                    Debug.LogError("GameManager: Firebase service not available");
                    return false;
                }

                bool sessionInitialized = await firebaseService.InitializeSession(currentSessionId, "Player");
                if (!sessionInitialized)
                {
                    Debug.LogError("GameManager: Failed to initialize Firebase session");
                    return false;
                }

                if (!await InitializeLocationServices())
                {
                    Debug.LogError("GameManager: Failed to initialize location services");
                    return false;
                }

                if (!await InitializeHeadTracking())
                {
                    Debug.LogError("GameManager: Failed to initialize head tracking");
                    return false;
                }

                TransitionToPhase(ApplicationPhase.PlayerMode);

                if (poiManager != null)
                {
                    poiManager.PlayWelcomeGreeting();
                }

                Debug.Log("GameManager: Player mode started successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: StartPlayerMode failed - {e.Message}");
                CleanupFailedPlayerStart();
                return false;
            }
        }

        public async Task<bool> StartSpectatorMode(string sessionId)
        {
            Debug.Log($"GameManager: StartSpectatorMode requested for session {sessionId}");

            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    Debug.LogError("GameManager: Invalid session ID provided");
                    return false;
                }

                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();
                if (firebaseService == null)
                {
                    Debug.LogError("GameManager: Firebase service not available");
                    return false;
                }

                bool connected = await firebaseService.ConnectToSession(
                    sessionId,
                    OnSpectatorPositionUpdated,
                    OnSpectatorPOIsUpdated
                );

                if (!connected)
                {
                    Debug.LogError("GameManager: Failed to connect to spectator session");
                    return false;
                }

                currentSessionId = sessionId;

                if (LocationService != null && LocationService.IsRunning)
                {
                    LocationService.StopLocationUpdates();
                }

                TransitionToPhase(ApplicationPhase.SpectatorMode);

                Debug.Log("GameManager: Spectator mode started successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: StartSpectatorMode failed - {e.Message}");
                CleanupFailedSpectatorStart();
                return false;
            }
        }

        public void StartHardwareSetup()
        {
            Debug.Log("GameManager: Starting hardware setup");
            TransitionToPhase(ApplicationPhase.HardwareSetup);
        }

        public void StartTutorial()
        {
            Debug.Log("GameManager: Starting tutorial");
            TransitionToPhase(ApplicationPhase.Tutorial);
        }

        public void CompleteHardwareSetup()
        {
            Debug.Log("GameManager: Hardware setup completed");

            bool shouldShowTutorial = !PlayerPrefs.HasKey("TutorialCompleted");
            ApplicationPhase nextPhase = shouldShowTutorial ?
                ApplicationPhase.Tutorial :
                ApplicationPhase.ModeSelection;

            TransitionToPhase(nextPhase);
        }

        public void CompleteTutorial()
        {
            Debug.Log("GameManager: Tutorial completed");

            PlayerPrefs.SetString("TutorialCompleted", "true");
            PlayerPrefs.Save();

            TransitionToPhase(ApplicationPhase.ModeSelection);
        }

        public void TogglePause()
        {
            if (gameState == GameState.Running)
            {
                previousGameplayState = currentGameplayState; // store current state
                TransitionToGameplayState(GameplayState.Paused);
                gameState = GameState.Suspended;
                Debug.Log("Game paused");
            }
            else if (gameState == GameState.Suspended)
            {
                gameState = GameState.Running;
                // Resume previous state
                TransitionToGameplayState(previousGameplayState);
                Debug.Log("Game resumed");
            }
        }

        #endregion

        #region Pause Management

        private bool isPaused = false;
        public bool IsPaused => isPaused;

        /// <summary>
        /// Pause or unpause the game
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (isPaused == paused) return;

            isPaused = paused;

            if (paused)
            {
                PauseGame();
            }
            else
            {
                ResumeGame();
            }

            Debug.Log($"GameManager: Game {(paused ? "PAUSED" : "RESUMED")}");
        }

        /// <summary>
        /// Toggle pause state
        /// </summary>
        private void PauseGame()
        {
            isPaused = true;

            // Suspend navigation audio (using your existing methods)
            SuspendNavigationAudio("game_paused");

            Debug.Log("GameManager: Game paused - audio suspended");
        }

        private void ResumeGame()
        {
            isPaused = false;

            // Resume navigation audio (using your existing methods)
            ResumeNavigationAudio("game_resumed");

            Debug.Log("GameManager: Game resumed - audio resumed");
        }

        #endregion


        #region Service Helpers

        private async Task<bool> InitializeLocationServices()
        {
            try
            {
                var locationService = await ServiceLocator.GetInitializedService<ILocationService>();
                if (locationService == null)
                {
                    Debug.LogError("GameManager: Location service not available");
                    return false;
                }

                if (!locationService.IsRunning)
                {
                    locationService.StartLocationUpdates();
                }

                Debug.Log("GameManager: Location services initialized");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Location service initialization error - {e.Message}");
                return false;
            }
        }

        private async Task<bool> InitializeHeadTracking()
        {
            try
            {
                var headTrackingService = await ServiceLocator.GetInitializedService<IHeadTrackingService>();
                if (headTrackingService == null)
                {
                    Debug.LogError("GameManager: Head tracking service not available");
                    return false;
                }

                bool isCurrentlyTracking = !string.IsNullOrEmpty(headTrackingService.ActiveProviderName) &&
                                          headTrackingService.ActiveProviderName != "None";

                if (!isCurrentlyTracking)
                {
                    headTrackingService.StartTracking();
                }

                Debug.Log("GameManager: Head tracking initialized");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Head tracking initialization error - {e.Message}");
                return false;
            }
        }

        public bool IsPlayerWithinGameBounds(float latitude, float longitude)
        {
            if (GameDataService?.GameConfig?.gameBounds == null)
            {
                return true;
            }

            return GameDataService.GameConfig.gameBounds.IsWithinBounds(latitude, longitude);
        }

        #endregion

        #region Session Cleanup

        private void CleanupFailedPlayerStart()
        {
            currentSessionId = null;
        }

        private void CleanupFailedSpectatorStart()
        {
            currentSessionId = null;
            isReceivingSpectatorData = false;
            spectatorLocation = Vector2.zero;
            spectatorHeading = 0f;
        }

        private void CleanupPlayerSession()
        {
            if (LocationService != null && LocationService.IsRunning)
            {
                LocationService.StopLocationUpdates();
            }

            if (HeadTrackingService != null)
            {
                bool isCurrentlyTracking = !string.IsNullOrEmpty(HeadTrackingService.ActiveProviderName) &&
                                          HeadTrackingService.ActiveProviderName != "None";

                if (isCurrentlyTracking)
                {
                    HeadTrackingService.StopTracking();
                }
            }

            currentSessionId = null;
        }

        private void CleanupSpectatorSession()
        {
            isReceivingSpectatorData = false;
            spectatorLocation = Vector2.zero;
            spectatorHeading = 0f;
            currentSessionId = null;
        }

        #endregion

        #region Audio Management

        private void StartAmbientMusic()
        {
            if (!audioInitialized || AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    PLAYBACK_STATE playbackState;
                    mainAmbientInstance.getPlaybackState(out playbackState);

                    if (playbackState != PLAYBACK_STATE.PLAYING)
                    {
                        AudioService.PlayAudio(mainAmbientInstance, Vector3.zero);
                        Debug.Log("GameManager: Ambient music started");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Failed to start ambient music - {e.Message}");
            }
        }

        private void StopAmbientMusic()
        {
            if (!audioInitialized || AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    AudioService.StopAudio(mainAmbientInstance, true);
                    Debug.Log("GameManager: Ambient music stopped");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Failed to stop ambient music - {e.Message}");
            }
        }

        public void SuspendNavigationAudio(string reason)
        {
            Debug.Log($"GameManager: Suspending navigation audio - {reason}");
            TransitionToGameplayState(GameplayState.Paused);
        }

        public void ResumeNavigationAudio(string reason)
        {
            Debug.Log($"GameManager: Resuming navigation audio - {reason}");
            TransitionToGameplayState(GameplayState.Wander);
        }

        private void OnTimeLayerChanged(TimeLayer newLayer)
        {
            if (!audioInitialized || AudioService == null) return;

            try
            {
                if (AudioService != null && AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    AudioService.SetParameter(mainAmbientInstance, "TimeLayer", newLayer.layerIndex);
                    Debug.Log($"GameManager: Ambient music updated for layer {newLayer.layerIndex}");
                }
                // ADD ANALYTICS EVENT
                AnalyticsService?.TrackEvent($"time_travel_to_{newLayer.layerName.Replace(" ", "_").ToLower()}");

                // Store time travel event
                if (StorageService != null)
                {
                    string travelKey = $"TimeLayer_{newLayer.layerIndex}_Visited";
                    StorageService.Save(travelKey, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Failed to update ambient music for time layer - {e.Message}");
            }
        }

        #endregion

        #region Health Management

        private void LoadHealthFromPreferences()
        {
            try
            {
                // Set max from JSON
                if (GameDataService != null && GameDataService.IsDataLoaded)
                {
                    maxHealth = GameDataService.GameConfig.maxPlayerHealth;
                }
                else
                {
                    // Emergency fallback if JSON not loaded
                    maxHealth = 3;
                    Debug.LogWarning("GameManager: JSON not loaded, using hardcoded maxHealth=3");
                }

                // Load current health from PlayerPrefs (or default to max)
                if (StorageService != null)
                {
                    playerHealth = StorageService.Load<int>("PlayerHealth", maxHealth);
                    Debug.Log($"GameManager: Loaded health {playerHealth}/{maxHealth}");
                }
                else
                {
                    playerHealth = maxHealth;
                    Debug.LogWarning("GameManager: StorageService not available - defaulting to full health");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error loading health - {e.Message}");
                maxHealth = 3;
                playerHealth = maxHealth;
            }
        }

        private void SaveHealthToPreferences()
        {
            try
            {
                if (StorageService != null)
                {
                    StorageService.Save("PlayerHealth", playerHealth);
                    Debug.Log($"GameManager: Saved health to preferences: {playerHealth}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error saving health - {e.Message}");
            }
        }

        public void TakeDamage()
        {
            if (playerHealth > 0)
            {
                playerHealth--;
                SaveHealthToPreferences();
                UpdateHeartbeat();

                // Single damage event
                AnalyticsService?.TrackEvent($"player_hit_health_{playerHealth}");

                Debug.Log($"GameManager: Player took damage. Health: {playerHealth}");
            }
        }

        public void RestoreHealth(int amount = 1)
        {
            int oldHealth = playerHealth;
            playerHealth = Mathf.Min(playerHealth + amount, maxHealth);
            SaveHealthToPreferences();
            UpdateHeartbeat();
            // Only track if health actually increased
            if (playerHealth > oldHealth)
            {
                AnalyticsService?.TrackEvent($"player_healed_to_health_{playerHealth}");
            }

            Debug.Log($"GameManager: Player restored health. Health: {playerHealth}");

            if (playerHealth >= maxHealth && currentGameplayState == GameplayState.Recovery)
            {
                Debug.Log("GameManager: Player fully healed. Returning to wander mode.");
                TransitionToGameplayState(GameplayState.Wander);
            }
        }

        private void UpdateHeartbeat()
        {
            if (!audioInitialized || AudioService == null) return;

            try
            {
                if (!AudioService.IsInstanceValid(heartbeatInstance))
                {
                    Debug.Log("GameManager: Heartbeat instance invalid - skipping update");
                    return;
                }

                AudioService.SetParameter(heartbeatInstance, "Health", playerHealth);
                Debug.Log($"GameManager: Heartbeat parameter set to: {playerHealth}");

                if (playerHealth < maxHealth && currentGameplayState != GameplayState.Wander)
                {
                    PLAYBACK_STATE playbackState;
                    heartbeatInstance.getPlaybackState(out playbackState);
                    if (playbackState != PLAYBACK_STATE.PLAYING)
                    {
                        AudioService.PlayAudio(heartbeatInstance, Vector3.zero);
                        Debug.Log("GameManager: Heartbeat audio started");
                    }
                }
                else
                {
                    AudioService.StopAudio(heartbeatInstance, true);
                    Debug.Log("GameManager: Heartbeat audio stopped");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error updating heartbeat - {e.Message}");
            }
        }

        #endregion

        #region Gameplay State Management

        public void TransitionToGameplayState(GameplayState newState)
        {
            if (currentGameplayState == newState) return;

            if (!IsValidGameplayStateTransition(currentGameplayState, newState))
            {
                Debug.LogError($"GameManager: Invalid gameplay state transition {currentGameplayState} → {newState}");
                return;
            }

            Debug.Log($"GameManager: Gameplay state transition {currentGameplayState} → {newState}");

            ExitGameplayState(currentGameplayState);
            currentGameplayState = newState;
            EnterGameplayState(newState);
        }

        private bool IsValidGameplayStateTransition(GameplayState from, GameplayState to)
        {
            return (from, to) switch
            {
                (GameplayState.Wander, GameplayState.Interact) => true,
                (GameplayState.Wander, GameplayState.Combat) => true,
                (GameplayState.Interact, GameplayState.Wander) => true,
                (GameplayState.Combat, GameplayState.Recovery) => true,
                (GameplayState.Combat, GameplayState.Wander) => true,
                (GameplayState.Recovery, GameplayState.Wander) => true,
                (_, GameplayState.Paused) => true,
                (GameplayState.Paused, _) => true,
                _ => false
            };
        }

        private void ExitGameplayState(GameplayState state)
        {
            switch (state)
            {
                case GameplayState.Combat:
                    CleanupCombat();
                    break;
                case GameplayState.Recovery:
                    CleanupRecovery();
                    break;
            }
        }

        private void EnterGameplayState(GameplayState state)
        {
            switch (state)
            {
                case GameplayState.Wander:
                    EnablePOIManager();
                    if (AudioService != null && AudioService.IsInstanceValid(heartbeatInstance))
                    {
                        AudioService.StopAudio(heartbeatInstance, true);
                        Debug.Log("GameManager: Heartbeat stopped - entering wander mode");
                    }
                    break;

                case GameplayState.Interact:
                    break;

                case GameplayState.Combat:
                    DisablePOIManager();
                    StartCombat();
                    break;

                case GameplayState.Recovery:
                    StartRecovery();
                    UpdateHeartbeat();
                    break;
            }
        }

        private void EnablePOIManager()
        {
            Debug.Log("GameManager: Enabling POIManager and resuming POI audio");
            if (poiManager != null)
            {
                poiManager.enabled = true;
                poiManager.ResumeAllPOIAudio();
            }
        }

        private void DisablePOIManager()
        {
            Debug.Log("GameManager: Disabling POIManager and silencing POI audio");
            if (poiManager != null)
            {
                poiManager.SilenceAllPOIAudio();
                poiManager.ClearAllNavigationState();
                poiManager.enabled = false;
            }
        }

        #endregion

        #region Gameplay Update Loop

        private void UpdateGameplayMode()
        {
            try
            {
                CheckForProximityStateTransitions();

                switch (currentGameplayState)
                {
                    case GameplayState.Wander:
                        UpdateWanderMode();
                        break;
                    case GameplayState.Interact:
                        break;
                    case GameplayState.Combat:
                        UpdateCombatMode();
                        break;
                    case GameplayState.Recovery:
                        UpdateRecoveryMode();
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error in gameplay update - {e.Message}");
            }
        }

        private void CheckForProximityStateTransitions()
        {
            if (currentGameplayState != GameplayState.Wander && currentGameplayState != GameplayState.Interact)
                return;

            bool nowInProximity = IsPlayerInPOIProximity();

            if (nowInProximity && !isPlayerInPOIProximity && currentGameplayState == GameplayState.Wander)
            {
                Debug.Log("GameManager: Player entered POI proximity - transitioning to Interact mode");
                TransitionToGameplayState(GameplayState.Interact);
                isPlayerInPOIProximity = true;
            }
            else if (!nowInProximity && isPlayerInPOIProximity && currentGameplayState == GameplayState.Interact)
            {
                Debug.Log("GameManager: Player left POI proximity - transitioning to Wander mode");
                TransitionToGameplayState(GameplayState.Wander);
                isPlayerInPOIProximity = false;
            }
        }

        private bool IsPlayerInPOIProximity()
        {
            return poiManager != null && poiManager.HasPOIInProximity();
        }

        #endregion

        #region Combat System

        private void UpdateWanderMode()
        {
            combatCheckTimer += Time.deltaTime;
            if (combatCheckTimer >= combatTriggerCheckInterval)
            {
                CheckForCombatTriggers();
                combatCheckTimer = 0f;
            }
        }

        private void CheckForCombatTriggers()
        {
            if (StorageService == null) return;

            if (isInCombat) return;

            foreach (var trigger in combatTriggers)
            {
                string combatId = trigger.Key;

                if (StorageService.Load<bool>($"{combatId}_completed"))
                    continue;

                bool allRewardsUnlocked = trigger.Value.All(rewardId =>
                    StorageService.Load<bool>($"Reward{rewardId}Unlocked"));

                if (allRewardsUnlocked)
                {
                    Debug.Log($"GameManager: Combat trigger activated: {combatId}");
                    Debug.Log($"Required rewards unlocked: {string.Join(", ", trigger.Value)}");

                    StorageService.Save($"{combatId}_completed", true);
                    currentCombatType = GetCombatTypeIndex(combatId);

                    // Track specific combat trigger with reward IDs
                    string rewardList = string.Join("_", trigger.Value);
                    AnalyticsService?.TrackEvent($"combat_triggered_{combatId}_rewards_{rewardList}");

                    TransitionToGameplayState(GameplayState.Combat);
                    return;
                }
            }
        }

        private int GetCombatTypeIndex(string combatId)
        {
            return combatId switch
            {
                "combat_oak_crops" => 1,
                "combat_royal_artifacts" => 2,
                _ => 1
            };
        }

        private void StartCombat()
        {
            if (!audioInitialized)
            {
                Debug.LogError("GameManager: Cannot start combat - audio not initialized");
                TransitionToGameplayState(GameplayState.Wander);
                return;
            }

            isInCombat = true;
            currentAttackIndex = 0;
            activeMercenaries.Clear();

            Debug.Log("GameManager: Starting combat sequence");
            StartMercenaryEncounter();
        }

        private void StartMercenaryEncounter()
        {
            if (AudioService == null || mercenaryEncounterEvent.IsNull) return;

            try
            {
                mercenaryEncounterInstance = AudioService.CreateAudioInstance(mercenaryEncounterEvent);
                AudioService.SetParameter(mercenaryEncounterInstance, "CombatType", currentCombatType);
                mercenaryEncounterInstance.setCallback(OnMercenaryDialogueComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                mercenaryEncounterInstance.start();
                Debug.Log($"GameManager: Mercenary encounter started - Combat Type: {currentCombatType}");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error starting mercenary encounter - {e.Message}");
            }
        }

        private void UpdateCombatMode()
        {
            // Combat audio runs entirely on FMOD timeline - no updates needed
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnMercenaryDialogueComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("GameManager: Mercenary dialogue completed via FMOD callback - starting attacks");
                Instance.StartCoroutine(Instance.StartAttackAfterDelay(10f));
            }
            return FMOD.RESULT.OK;
        }

        private IEnumerator StartAttackAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            StartAttackSequence();
        }

        private void StartAttackSequence()
        {
            currentAttackIndex = 0;
            Debug.Log("GameManager: Starting attack sequence");
            ExecuteNextAttack();
        }

        private void ExecuteNextAttack()
        {
            if (currentAttackIndex >= 3)
            {
                Debug.Log("GameManager: All 3 attacks completed - concluding combat");
                ConcludeCombat();
                return;
            }

            var attackingMercenary = CreateMercenaryForAttack();
            if (attackingMercenary == null)
            {
                Debug.LogError("GameManager: Failed to create mercenary - aborting attack");
                return;
            }

            activeMercenaries.Add(attackingMercenary);
            Debug.Log($"GameManager: Executing attack {currentAttackIndex + 1}/3 from {attackingMercenary.id}");

            StartCoroutine(ExecuteAttackCoroutine(attackingMercenary));
            currentAttackIndex++;
        }

        private Mercenary CreateMercenaryForAttack()
        {
            if (HeadTrackingService == null)
            {
                Debug.LogError("GameManager: HeadTrackingService not available for mercenary creation");
                return null;
            }

            float playerHeading = HeadTrackingService.CurrentHeading;
            float[] possibleOffsets = { -60f, 0f, 60f };
            float randomOffset = possibleOffsets[UnityEngine.Random.Range(0, possibleOffsets.Length)];
            float attackBearing = NormalizeAngle(playerHeading + randomOffset);

            var mercenary = new Mercenary($"mercenary_{currentAttackIndex}", attackBearing);
            Debug.Log($"GameManager: Created mercenary for attack {currentAttackIndex + 1} - Player facing: {playerHeading:F0}°, Attack from: {attackBearing:F0}°");

            return mercenary;
        }

        private float NormalizeAngle(float angle)
        {
            while (angle < 0f) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }

        private IEnumerator ExecuteAttackCoroutine(Mercenary attacker)
        {
            currentAttackingMercenary = attacker;
            attacker.StartApproach();

            currentFootstepsInstance = new EventInstance();
            currentAttackInstance = new EventInstance();

            if (!mercenaryFootstepsEvent.IsNull && AudioService != null)
            {
                currentFootstepsInstance = AudioService.CreateAudioInstance(mercenaryFootstepsEvent);
                if (currentFootstepsInstance.handle != IntPtr.Zero)
                {
                    Vector3 startPos = attacker.GetCurrentAudioPosition();
                    AudioService.PlayAudio(currentFootstepsInstance, startPos);
                    Debug.Log("GameManager: Started footsteps");
                }
            }

            for (float t = 0; t < approachDuration; t += Time.deltaTime)
            {
                float progress = t / approachDuration;
                attacker.UpdateApproach(progress);
                Vector3 currentPos = attacker.GetCurrentAudioPosition();

                if (AudioService != null && AudioService.IsInstanceValid(currentFootstepsInstance))
                {
                    AudioService.Update3DAttributes(currentFootstepsInstance, currentPos);
                }

                yield return null;
            }

            Debug.Log("GameManager: Approach complete - starting attack sound");

            if (!mercenaryAttackEvent.IsNull && AudioService != null)
            {
                currentAttackInstance = AudioService.CreateAudioInstance(mercenaryAttackEvent);
                if (currentAttackInstance.handle != IntPtr.Zero)
                {
                    currentAttackInstance.setCallback(OnAttackSoundComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                    AudioService.PlayAudio(currentAttackInstance, attacker.GetCurrentAudioPosition());
                    Debug.Log("GameManager: Attack sound started");
                }
                else
                {
                    Invoke(nameof(HandleAttackSoundComplete), 1f);
                }
            }
            else
            {
                Invoke(nameof(HandleAttackSoundComplete), 1f);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnAttackSoundComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("GameManager: Attack sound completed via FMOD callback");
                Instance.HandleAttackSoundComplete();
            }
            return FMOD.RESULT.OK;
        }

        private void HandleAttackSoundComplete()
        {
            if (AudioService != null && AudioService.IsInstanceValid(currentFootstepsInstance))
            {
                AudioService.StopAudio(currentFootstepsInstance, false);
                AudioService.ReleaseAudio(currentFootstepsInstance);
                Debug.Log("GameManager: Stopped footsteps");
            }

            if (currentAttackingMercenary != null)
            {
                bool playerSucceeded = CheckPlayerDefense(currentAttackingMercenary.bearing);
                PlayImpactSound(playerSucceeded, currentAttackingMercenary.GetCurrentAudioPosition());
                OnAttackComplete(playerSucceeded);
            }
        }

        private bool CheckPlayerDefense(float attackBearing)
        {
            if (HeadTrackingService == null) return false;

            float playerHeading = HeadTrackingService.CurrentHeading;
            float angleDifference = Mathf.Abs(Mathf.DeltaAngle(playerHeading, attackBearing));
            bool playerSucceeded = angleDifference <= 30f;

            Debug.Log($"GameManager: Defense check - Attack from: {attackBearing}°, Player facing: {playerHeading}°, Success: {playerSucceeded}");
            return playerSucceeded;
        }

        private void PlayImpactSound(bool playerBlocked, Vector3 position)
        {
            if (!attackImpactEvent.IsNull && AudioService != null)
            {
                var impactInstance = AudioService.CreateAudioInstance(attackImpactEvent);
                AudioService.SetParameter(impactInstance, "HitResult", playerBlocked ? 0 : 1);
                AudioService.PlayAudio(impactInstance, position);
                Debug.Log($"GameManager: Impact sound played - {(playerBlocked ? "BLOCKED" : "HIT")}");
            }
        }

        private void OnAttackComplete(bool playerSucceeded)
        {
            if (playerSucceeded)
            {
                Debug.Log("GameManager: Player BLOCKED the attack!");
            }
            else
            {
                Debug.Log("GameManager: Player was HIT!");
                TakeDamage();
            }

            if (currentAttackIndex < 3)
            {
                Debug.Log($"GameManager: Combat continues - attack {currentAttackIndex + 1}/3 next");
                Invoke(nameof(ExecuteNextAttack), 1f);
            }
            else
            {
                Debug.Log("GameManager: All attacks completed - concluding combat");
                ConcludeCombat();
            }
        }

        private void ConcludeCombat()
        {
            Debug.Log($"GameManager: Combat concluded - Final health: {playerHealth}/{maxHealth}");

            bool playerWon = playerHealth >= maxHealth;

            // Track combat result
            AnalyticsService?.TrackEvent($"combat_completed_{(playerWon ? "won" : "lost")}_health_{playerHealth}");

            // Store combat completion
            if (StorageService != null)
            {
                string combatKey = $"CombatCompleted_{currentCombatType}";
                StorageService.Save(combatKey, true);
            }

            if (playerHealth >= maxHealth)
            {
                Debug.Log("GameManager: Player at full health - returning to wander mode");
                TransitionToGameplayState(GameplayState.Wander);
            }
            else
            {
                Debug.Log("GameManager: Player damaged - entering recovery mode");
                TransitionToGameplayState(GameplayState.Recovery);
            }
        }

        private void CleanupCombat()
        {
            Debug.Log("GameManager: Cleaning up combat");

            isInCombat = false;
            activeMercenaries.Clear();
            currentAttackingMercenary = null;

            if (AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mercenaryEncounterInstance))
                {
                    AudioService.StopAudio(mercenaryEncounterInstance, true);
                    AudioService.ReleaseAudio(mercenaryEncounterInstance);
                }

                if (currentFootstepsInstance.handle != IntPtr.Zero)
                {
                    if (AudioService.IsInstanceValid(currentFootstepsInstance))
                    {
                        AudioService.StopAudio(currentFootstepsInstance, false);
                        AudioService.ReleaseAudio(currentFootstepsInstance);
                    }
                    currentFootstepsInstance = new EventInstance();
                }

                if (currentAttackInstance.handle != IntPtr.Zero)
                {
                    if (AudioService.IsInstanceValid(currentAttackInstance))
                    {
                        currentAttackInstance.setCallback(null, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                        AudioService.StopAudio(currentAttackInstance, false);
                        AudioService.ReleaseAudio(currentAttackInstance);
                    }
                    currentAttackInstance = new EventInstance();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error cleaning up combat audio - {e.Message}");
            }
        }

        #endregion

        #region Recovery System

        private void StartRecovery()
        {
            Debug.Log("GameManager: Starting recovery mode - spawning berry");
            SpawnBerryNearPlayer();
            UpdateHeartbeat();
        }

        private void SpawnBerryNearPlayer()
        {
            if (LocationService == null || LocationService.GetCurrentLocation() == Vector2.zero)
            {
                // to prevent players from being stuck in berry mode return them to normal gameplay, restoring full health
                Debug.LogError("GameManager: Cannot spawn berry - LocationService not available");
                playerHealth = maxHealth;
                SaveHealthToPreferences();
                TransitionToGameplayState(GameplayState.Wander);
                return;
            }

            activeBerries.Clear();
            Vector2 playerLocation = LocationService.GetCurrentLocation();
            var (distance, angle) = Berry.GetSafeSpawnParameters();

            var berry = new Berry($"berry_{activeBerries.Count}", playerLocation, angle, distance);
            activeBerries.Add(berry);

            Debug.Log($"GameManager: Berry spawned at distance: {distance:F1}m, angle: {angle:F0}°");
            StartBerryAudio(berry);
        }

        private void StartBerryAudio(Berry berry)
        {
            if (currentGameplayState != GameplayState.Recovery) return;

            if (!berryAmbientEvent.IsNull && AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                Vector3 berryPosition = berry.GetAudioPosition();
                AudioService.Update3DAttributes(sharedBerryInstance, berryPosition);

                PLAYBACK_STATE playbackState;
                sharedBerryInstance.getPlaybackState(out playbackState);
                if (playbackState != PLAYBACK_STATE.PLAYING)
                {
                    AudioService.PlayAudio(sharedBerryInstance, berryPosition);
                }

                Debug.Log("GameManager: Berry spatial audio started");
            }
        }

        private void UpdateRecoveryMode()
        {
            if (activeBerries.Count == 0) return;

            Berry currentBerry = activeBerries[0];

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                Vector3 berryPosition = currentBerry.GetAudioPosition();
                AudioService.Update3DAttributes(sharedBerryInstance, berryPosition);
            }

            if (currentBerry.CheckCollection())
            {
                CollectBerry(currentBerry);
            }
        }

        private void CollectBerry(Berry berry)
        {
            Debug.Log($"GameManager: Berry {berry.id} collected!");

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                AudioService.StopAudio(sharedBerryInstance, false);
            }

            PlayBerryCollectionSound(berry.GetAudioPosition());
            activeBerries.Remove(berry);

            // Track berry collection
            AnalyticsService?.TrackEvent("berry_collected");

            RestoreHealth(1);

            if (currentGameplayState == GameplayState.Recovery && playerHealth < maxHealth)
            {
                Debug.Log("GameManager: Still need healing - spawning next berry");
                Invoke(nameof(SpawnBerryNearPlayer), 2f);
            }
        }

        private void PlayBerryCollectionSound(Vector3 position)
        {
            if (!berryCollectionEvent.IsNull && AudioService != null)
            {
                var collectionInstance = AudioService.CreateAudioInstance(berryCollectionEvent);
                AudioService.PlayAudio(collectionInstance, position);
                Debug.Log("GameManager: Berry collection sound played");
            }
        }

        private void CleanupRecovery()
        {
            Debug.Log("GameManager: Cleaning up recovery mode");

            activeBerries.Clear();

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                AudioService.StopAudio(sharedBerryInstance, true);
            }
        }

        #endregion

        #region Spectator System

        private void OnSpectatorPositionUpdated(float latitude, float longitude, float heading)
        {
            spectatorLocation = new Vector2(latitude, longitude);
            spectatorHeading = heading;
            isReceivingSpectatorData = true;

            if (uiManager != null)
                uiManager.UpdateLocationDisplay(latitude, longitude);

            Debug.Log($"GameManager: Spectator position updated: {latitude:F6}, {longitude:F6}, heading: {heading:F1}°");
        }

        private void OnSpectatorPOIsUpdated(List<string> characterIds)
        {
            if (poiManager != null)
                poiManager.UpdateUnlockedPOIs(characterIds);
        }

        #endregion

        #region Cleanup and Destruction

        private async void OnApplicationQuit()
        {
            try
            {
                Debug.Log("GameManager: Application quitting - performing cleanup");

                var firebaseService = ServiceLocator.GetService<IFirebaseService>();

                if (TimeLayerManager.Instance != null)
                {
                    TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
                }

                if (AudioService != null && AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    AudioService.StopAudio(mainAmbientInstance, true);
                    AudioService.ReleaseAudio(mainAmbientInstance);
                }

                if (firebaseService != null && firebaseService.IsInitialized)
                {
                    if (currentMode == GameMode.Player && !string.IsNullOrEmpty(currentSessionId))
                    {
                        var task = firebaseService.DeleteSession(currentSessionId);
                        var delayTask = Task.Delay(500);
                        await Task.WhenAny(task, delayTask);
                    }
                    else if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
                    {
                        firebaseService.DisconnectFromSession(currentSessionId);
                    }
                }

                Debug.Log("GameManager: Cleanup completed");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error during application quit - {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            systemsReady = false;
            Debug.Log("GameManager: Instance destroyed");
        }

        #endregion
    }
}