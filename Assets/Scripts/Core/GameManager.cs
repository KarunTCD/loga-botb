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
            SiteSelection,   // Select what instance of the game to load based on current location  
            Tutorial,        // First-time user guidance
            ModeSelection,   // Player vs Spectator choice
            PlayerMode,      // Active gameplay as player
            SpectatorMode    // Watching another player
        }

        public enum GameMode
        {
            Inactive,
            Player,
            Spectator,
            Tutorial
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
        }

        public enum SuspensionReason
        {
            None,
            Tutorial,
            PauseMenu,
            TimeTravel,
            Loading
        }

        #endregion

        #region Instance and Core Fields

        public static GameManager Instance { get; private set; }

        [Header("Core Systems")]
        [SerializeField] private UIManager uiManager;
        [SerializeField] private MapManager mapManager;
        [SerializeField] private POIManager poiManager;
        [SerializeField] private HardwareManager hardwareManager;
        [SerializeField] private TutorialManager tutorialManager;

        private EventReference mercenaryEncounterEvent;
        private EventReference mercenaryDefeatEvent;
        private EventReference mercenaryFootstepsEvent;
        private EventReference mercenaryAttackEvent;
        private EventReference attackImpactEvent;
        private EventReference heartbeatEvent;
        private EventReference berryAmbientEvent;
        private EventReference berryCollectionEvent;

        [Header("Universal Audio")]
        private EventReference mainAmbientEvent;

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
        private bool isPaused = false;

        // Suspension tracking
        private SuspensionReason suspensionReason = SuspensionReason.None;

        // FMOD buses to pause during suspension (all except Voice for tutorial narrator)
        private readonly string[] pausableBuses = new string[]
        {
            "bus:/Music",
            "bus:/SFX",
            "bus:/Ambient"
        };

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
        private EventInstance mercenaryDefeatInstance;
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
        private Mercenary currentAttackingMercenary;
        private GameDataService.CombatConfiguration combatConfig;
        private GameDataService.CombatEncounter currentCombatEncounter;
        private float playerHeadingAtAttackStart = 0f;
        
        // Tutorial combat state
        private bool isTutorialCombat = false;
        private int tutorialAttackNumber = 0;
        private int consecutiveDefenses = 0;

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
        public bool IsPaused => isPaused;

        /// <summary>
        /// Check if gameplay is currently suspended
        /// </summary>
        public bool IsSuspended => gameState == GameState.Suspended;

        /// <summary>
        /// Get the reason for current suspension (None if not suspended)
        /// </summary>
        public SuspensionReason CurrentSuspensionReason => suspensionReason;

        #endregion

        #region Public Events
        public event Action OnGamePaused;
        public event Action OnGameResumed;
        // Tutorial events for TutorialManager to listen to
        public event Action<int, bool, int> TutorialAttackCompleted;  // (attackNumber, wasDefended, consecutiveDefenses)
        public event Action TutorialCombatCompleted;
        public event Action TutorialBerryCollected;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Screen.sleepTimeout = SleepTimeout.NeverSleep; // keep the screens on
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

            // Stop all gameplay updates when suspended
            if (gameState != GameState.Running) return;

            if (currentMode == GameMode.Player  || currentMode == GameMode.Tutorial)
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
                Debug.Log("GameManager: Game data not loaded yet - will load after site selection");
            }

            if (GameDataService != null && GameDataService.IsDataLoaded)
            {
                // Data already loaded (shouldn't happen in multi-site flow, but handle it)
                ApplyGameDataConfiguration();
                hasDataConfiguration = true;
                Debug.Log("GameManager: Data was already loaded during services initialization");
            }
            else
            {
                // Normal multi-site flow: data loads after site selection
                Debug.Log("GameManager: Data will be loaded after site selection");
                hasDataConfiguration = false;
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

            // Load ambient event from JSON
            string ambientEventPath = config.ambientAudioEvent;
            if (!string.IsNullOrEmpty(ambientEventPath))
            {
                mainAmbientEvent = GameDataService.GetAudioEventReference(ambientEventPath);
                Debug.Log($"GameManager: Loaded ambient event from JSON: {ambientEventPath}");
            }

            Debug.Log($"GameManager: Default time layer: {config.defaultTimeLayer}");
            Debug.Log($"GameManager: Navigation settings - Base cues: {config.baseMaxActiveCues}, Max cues: {config.maxMaxActiveCues}");
            
            // Load combat configuration
            ApplyCombatConfiguration(GameDataService?.CombatConfig);
        }

        private void ApplyCombatConfiguration(GameDataService.CombatConfiguration config)
        {
            if (config == null)
            {
                Debug.Log("GameManager: No combat configuration - combat system disabled for this site");
                return;
            }
            
            combatConfig = config;
            
            int encounterCount = combatConfig.encounters?.Count ?? 0;
            Debug.Log($"GameManager: Loaded combat config - approachDuration: {combatConfig.approachDuration}s, " +
                    $"attackDelay: {combatConfig.attackDelayAfterIntro}s, attackCount: {combatConfig.attackCount}, " +
                    $"encounters: {encounterCount}");
            
            if (encounterCount == 0)
            {
                Debug.LogWarning("GameManager: No combat encounters in configuration!");
            }

            // INITIALIZE COMBAT AUDIO NOW (after config is loaded)
            InitializeCombatAudio();
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
                uiManager.Initialize(this, hardwareManager);  
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
                Debug.Log("GameManager: InitializeAudioSystems() called");

                var audioService = await ServiceLocator.GetInitializedService<IAudioService>();
                if (audioService == null)
                {
                    Debug.LogError("GameManager: AudioService not available");
                    return false;
                }

                Debug.Log("GameManager: AudioService obtained successfully");

                // Set audioInitialized regardless of combat events
                audioInitialized = true;
                Debug.Log("GameManager: Audio systems initialized successfully - audioInitialized = TRUE");

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
            Debug.Log("GameManager: InitializeCombatAudio() called");

            if (AudioService == null)
            {
                Debug.LogWarning("GameManager: AudioService not available - skipping combat audio");
                return true; // Don't fail, just skip
            }

            // LOAD from JSON instead of inspector
            if (combatConfig?.audioEvents == null) 
            {
                Debug.Log("GameManager: No combat audio events in config - skipping combat audio");
                return true; // Don't fail, just skip
            }

            var combatEvents = combatConfig.audioEvents;
            int successCount = 0;

            // Combat sequence events need persistent instances
            LoadCombatEventWithInstance(combatEvents.mercenaryEncounter, ref mercenaryEncounterEvent, ref mercenaryEncounterInstance, "mercenary encounter", ref successCount);
            LoadCombatEventWithInstance(combatEvents.mercenaryDefeat, ref mercenaryDefeatEvent, ref mercenaryDefeatInstance, "mercenary defeat", ref successCount); 
            LoadCombatEventWithInstance(combatEvents.heartbeat, ref heartbeatEvent, ref heartbeatInstance, "heartbeat", ref successCount);
            LoadCombatEventWithInstance(combatEvents.berryAmbient, ref berryAmbientEvent, ref sharedBerryInstance, "berry ambient", ref successCount);

            // Per-attack events use fresh instances
            LoadCombatEventRef(combatEvents.mercenaryFootsteps, ref mercenaryFootstepsEvent, "mercenary footsteps", ref successCount);
            LoadCombatEventRef(combatEvents.mercenaryAttack, ref mercenaryAttackEvent, "mercenary attack", ref successCount);
            LoadCombatEventRef(combatEvents.attackImpact, ref attackImpactEvent, "attack impact", ref successCount);
            LoadCombatEventRef(combatEvents.berryCollection, ref berryCollectionEvent, "berry collection", ref successCount);

            Debug.Log($"GameManager: Combat audio initialization complete - {successCount}/8 events loaded"); 
            return true; // Always succeed, even if 0 events loaded
        }

        private void LoadCombatEventWithInstance(string eventName, ref EventReference eventRef, ref EventInstance instance, string displayName, ref int successCount)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.Log($"GameManager: No {displayName} event in JSON - skipping");
                return;
            }

            eventRef = GameDataService.GetAudioEventReference(eventName);
            if (!eventRef.IsNull)
            {
                instance = AudioService.CreateAudioInstance(eventRef);
                if (instance.handle != IntPtr.Zero)
                {
                    successCount++;
                    Debug.Log($"GameManager: {displayName} instance created from JSON");
                }
                else
                {
                    Debug.LogWarning($"GameManager: Failed to create {displayName} instance - continuing without it");
                }
            }
            else
            {
                Debug.LogWarning($"GameManager: Failed to load {displayName} event - continuing without it");
            }
        }

        private void LoadCombatEventRef(string eventName, ref EventReference eventRef, string displayName, ref int successCount)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.Log($"GameManager: No {displayName} event in JSON - skipping");
                return;
            }

            eventRef = GameDataService.GetAudioEventReference(eventName);
            if (!eventRef.IsNull)
            {
                successCount++;
                Debug.Log($"GameManager: {displayName} event loaded from JSON");
            }
            else
            {
                Debug.LogWarning($"GameManager: Failed to load {displayName} event - continuing without it");
            }
        }

        private void InitializeAndStartAmbientAudio()
        {
            if (!audioInitialized || AudioService == null)
            {
                Debug.LogWarning("GameManager: Audio not ready for ambient initialization");
                return;
            }

            if (InitializeAmbientMusic())
            {
                StartAmbientMusic();
                Debug.Log("GameManager: Ambient audio initialized and started");
            }
            else
            {
                Debug.LogError("GameManager: Failed to initialize ambient audio");
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

                    if (TimeLayerManager.Instance != null && TimeLayerManager.Instance.CurrentLayer != null)
                    {
                        int layerIndex = TimeLayerManager.Instance.CurrentLayer.layerIndex;
                        AudioService.SetParameter(mainAmbientInstance, "TimeLayer", layerIndex);
                        Debug.Log($"GameManager: Ambient instance created with TimeLayer {layerIndex}");
                    }
                    else
                    {
                        Debug.LogWarning("GameManager: Ambient instance created without TimeLayer set");
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
                // Forward progression
                (ApplicationPhase.Initializing, ApplicationPhase.MainMenu) => true,
                (ApplicationPhase.MainMenu, ApplicationPhase.HardwareSetup) => true,
                (ApplicationPhase.HardwareSetup, ApplicationPhase.SiteSelection) => true,  
                (ApplicationPhase.SiteSelection, ApplicationPhase.Tutorial) => true,  
                (ApplicationPhase.SiteSelection, ApplicationPhase.ModeSelection) => true,  
                (ApplicationPhase.HardwareSetup, ApplicationPhase.Tutorial) => true,
                (ApplicationPhase.HardwareSetup, ApplicationPhase.ModeSelection) => true,
                (ApplicationPhase.Tutorial, ApplicationPhase.ModeSelection) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.PlayerMode) => true,
                (ApplicationPhase.ModeSelection, ApplicationPhase.SpectatorMode) => true,

                // Backward navigation
                (ApplicationPhase.HardwareSetup, ApplicationPhase.MainMenu) => true,
                (ApplicationPhase.SiteSelection, ApplicationPhase.HardwareSetup) => true, 
                (ApplicationPhase.Tutorial, ApplicationPhase.SiteSelection) => true,  
                (ApplicationPhase.ModeSelection, ApplicationPhase.SiteSelection) => true, 
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
                case ApplicationPhase.Tutorial:
                    // Cleanup handled in ExitTutorial()
                    break;
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
                case ApplicationPhase.Tutorial:
                    EnterTutorial();  
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

        private void EnterTutorial()
        {
            Debug.Log("GameManager: Entering Tutorial phase");
            // Tutorial activation is handled by UIManager calling StartGameplayTutorial()
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
            Debug.Log("GameManager: Exiting PlayerMode - triggering complete site unload");

            // CRITICAL: Unload site first (triggers complete reset)
            if (SiteManager.Instance != null)
            {
                SiteManager.Instance.UnloadCurrentSite();
            }

            // Suspend remaining systems
            SuspendGameplaySystems();
            SetInternalGameMode(GameMode.Inactive);

            Debug.Log("GameManager: PlayerMode exit complete");
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

            gameState = GameState.Suspended;

            // Don't change currentGameplayState - it stays as Wander/Interact/etc
            // This allows us to return to the correct state when resuming

            Debug.Log($"GameManager: Gameplay systems suspended (state remains {currentGameplayState})");
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
                // Services should already be running - just verify
                if (hardwareManager == null || !await hardwareManager.EnsureServicesRunning())
                {
                    Debug.LogError("GameManager: Hardware services not available");
                    return false;
                }

                currentSessionId = System.Guid.NewGuid().ToString();
                Debug.Log($"GameManager: Generated session ID: {currentSessionId}");

                // Make Firebase optional with timeout
                bool firebaseAvailable = false;
                try
                {
                    var firebaseTask = ServiceLocator.GetInitializedService<IFirebaseService>();
                    var timeoutTask = Task.Delay(5000);

                    var completedTask = await Task.WhenAny(firebaseTask, timeoutTask);

                    if (completedTask == firebaseTask && firebaseTask.Result != null)
                    {
                        var firebaseService = firebaseTask.Result;
                        
                        var sessionTask = firebaseService.InitializeSession(currentSessionId, "Player");
                        var sessionTimeoutTask = Task.Delay(3000);
                        
                        var sessionCompleted = await Task.WhenAny(sessionTask, sessionTimeoutTask);
                        
                        if (sessionCompleted == sessionTask && sessionTask.Result)
                        {
                            firebaseAvailable = true;
                            Debug.Log("GameManager: Firebase session initialized successfully");
                        }
                        else
                        {
                            Debug.LogWarning("GameManager: Firebase session initialization timed out - continuing offline");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("GameManager: Firebase service not available - continuing offline");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"GameManager: Firebase initialization failed - continuing offline: {e.Message}");
                }

                TransitionToPhase(ApplicationPhase.PlayerMode);

                if (poiManager != null)
                {
                    poiManager.PlayWelcomeGreeting();
                }

                Debug.Log($"GameManager: Player mode started successfully (Firebase: {(firebaseAvailable ? "Online" : "Offline")})");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: StartPlayerMode failed - {e.Message}");
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

        public void CompleteHardwareSetup()
        {
            Debug.Log("GameManager: Hardware setup completed");

            // Services are already started by HardwareManager during setup
            // Just verify they're running
            if (hardwareManager != null)
            {
                var status = hardwareManager.GetStatus();
                Debug.Log($"GameManager: Hardware status - Location: {status.locationActive}, HeadTracking: {status.headTrackingActive}");

                if (!status.servicesRunning)
                {
                    Debug.LogWarning("GameManager: Services not fully running after hardware setup");
                }
            }

            TransitionToPhase(ApplicationPhase.SiteSelection);
        }

        public void StartTutorial()
        {
            Debug.Log("GameManager: Starting tutorial");
            TransitionToPhase(ApplicationPhase.Tutorial);
        }

        public void CompleteSiteSelection()
        {
            Debug.Log("GameManager: Site selection completed");

            // Apply game configuration from loaded site data
            ApplyGameDataConfiguration();

            LoadHealthFromPreferences();

            // initialize and start ambient audio
            InitializeAndStartAmbientAudio();

            // Determine next phase
            bool shouldShowTutorial = !PlayerPrefs.HasKey("TutorialCompleted");
            ApplicationPhase nextPhase = shouldShowTutorial ?
                ApplicationPhase.Tutorial :
                ApplicationPhase.ModeSelection;

            TransitionToPhase(nextPhase);
        }

        public void CompleteTutorial()
        {
            Debug.Log("GameManager: Tutorial completed successfully");

            // Mark as completed
            PlayerPrefs.SetString("TutorialCompleted", "true");
            PlayerPrefs.Save();

            // Cleanup tutorial mode
            ExitTutorial();

            // Transition to mode selection
            TransitionToPhase(ApplicationPhase.ModeSelection);
        }

        /// <summary>
        /// Pause the game - NOT a toggle, explicit pause operation
        /// ROBUST: Safe to call multiple times, validates state before pausing
        /// </summary>
        public void Pause()
        {
            if (isPaused)
            {
                Debug.LogWarning("GameManager: Already paused - ignoring");
                return;
            }

            Debug.Log("GameManager: Pausing game");

            isPaused = true;
            SuspendGameplay(SuspensionReason.PauseMenu);

            // Notify listeners (e.g., UI)
            OnGamePaused?.Invoke();

            Debug.Log("GameManager: Game paused successfully");
        }

        /// <summary>
        /// Resume the game - NOT a toggle, explicit resume operation
        /// ROBUST: Safe to call multiple times, validates state before resuming
        /// </summary>
        public void Resume()
        {
            if (!isPaused)
            {
                Debug.LogWarning("GameManager: Not paused - ignoring resume");
                return;
            }

            Debug.Log("GameManager: Resuming game");

            isPaused = false;
            ResumeGameplay(SuspensionReason.PauseMenu);

            // Notify listeners (e.g., UI)
            OnGameResumed?.Invoke();

            Debug.Log("GameManager: Game resumed successfully");
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
            Debug.Log("StartAmbientMusic() called");
            Debug.Log($"audioInitialized: {audioInitialized}");
            Debug.Log($"AudioService: {AudioService != null}");

            if (!audioInitialized || AudioService == null)
            {
                Debug.LogError("Cannot start ambient - audio not ready");
                return;
            }

            Debug.Log($"mainAmbientInstance valid: {AudioService.IsInstanceValid(mainAmbientInstance)}");
            Debug.Log($"mainAmbientEvent null: {mainAmbientEvent.IsNull}");


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

        private void OnTimeLayerChanged(TimeLayer newLayer)
        {
            if (!audioInitialized || AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    // Simple and direct - no duplication needed
                    AudioService.SetParameter(mainAmbientInstance, "TimeLayer", newLayer.layerIndex);
                    Debug.Log($"GameManager: Ambient music updated for layer {newLayer.layerIndex}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Failed to update ambient music - {e.Message}");
            }
        }

        /// <summary>
        /// Stop all gameplay audio when exiting to main menu
        /// </summary>
        public void StopAllGameplayAudio()
        {
            Debug.Log("GameManager: Stopping all gameplay audio");

            // Stop main ambient music
            if (AudioService != null && AudioService.IsInstanceValid(mainAmbientInstance))
            {
                AudioService.StopAudio(mainAmbientInstance, false);
                Debug.Log("GameManager: Ambient music stopped");
            }

            // Stop POI audio through POIManager
            if (poiManager != null)
            {
                poiManager.StopAllAudio();
            }

            // Stop combat audio
            CleanupCombat();

            // Stop recovery audio
            CleanupRecovery();

            // Stop heartbeat
            if (AudioService != null && AudioService.IsInstanceValid(heartbeatInstance))
            {
                AudioService.StopAudio(heartbeatInstance, false);
            }

            Debug.Log("GameManager: All gameplay audio stopped");
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
            if (!audioInitialized || AudioService == null) 
            {
                Debug.LogWarning("[HEARTBEAT-DEBUG] UpdateHeartbeat() ABORTED - audio not ready");
                return;
            }

            if (heartbeatInstance.handle == IntPtr.Zero)
            {
                Debug.LogWarning("[HEARTBEAT-DEBUG] UpdateHeartbeat() ABORTED - heartbeat instance null");
                return;
            }

            try
            {
                if (!AudioService.IsInstanceValid(heartbeatInstance))
                {
                    Debug.LogWarning("[HEARTBEAT-DEBUG] UpdateHeartbeat() ABORTED - instance invalid");
                    return;
                }

                AudioService.SetParameter(heartbeatInstance, "Health", playerHealth);
                Debug.Log($"GameManager: Heartbeat parameter set to: {playerHealth}");

                if (playerHealth < maxHealth)
                {
                    PLAYBACK_STATE playbackState;
                    heartbeatInstance.getPlaybackState(out playbackState);
                    
                    if (playbackState != PLAYBACK_STATE.PLAYING)
                    {
                        AudioService.PlayAudio(heartbeatInstance, Vector3.zero);
                        Debug.Log("[HEARTBEAT-DEBUG] Heartbeat audio STARTED");
                    }
                    else
                    {
                        Debug.Log("[HEARTBEAT-DEBUG] Heartbeat already playing - skipping start");
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
                Debug.LogError($"[HEARTBEAT-DEBUG] Exception in UpdateHeartbeat: {e.Message}\n{e.StackTrace}");
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
            Debug.Log($"[COMBAT-CHECK] combatConfig null? {combatConfig == null}");
            Debug.Log($"[COMBAT-CHECK] encounters null? {combatConfig?.encounters == null}");
            Debug.Log($"[COMBAT-CHECK] encounter count: {combatConfig?.encounters?.Count ?? 0}");

            // Check if combat configuration exists
            if (combatConfig?.encounters == null || combatConfig.encounters.Count == 0)
            {
                Debug.LogWarning("[COMBAT-CHECK] No combat config - returning"); 
                return;
            }
            
            Debug.Log("[COMBAT-CHECK] Checking encounters...");

            // Check each combat encounter from JSON
            foreach (var encounter in combatConfig.encounters)
            {
                // Check if already completed
                string completionKey = $"combat_type_{encounter.combatType}_completed";
                bool isCompleted = StorageService.Load<bool>(completionKey);

                if (isCompleted)
                {
                    continue; // Skip completed encounters
                }

                // Check if player has all required rewards (UPDATED KEY FORMAT)
                bool hasAllRewards = encounter.requiredRewards.All(rewardId => 
                    StorageService.Load<bool>($"reward_{rewardId}_collected")
                );

                if (hasAllRewards)
                {
                    Debug.Log($"GameManager: Combat trigger activated for type {encounter.combatType}!");
                    Debug.Log($"Required rewards collected: {string.Join(", ", encounter.requiredRewards)}");
                    
                    // Store current encounter
                    currentCombatEncounter = encounter;
                    
                    // Track specific combat trigger
                    string rewardList = string.Join("_", encounter.requiredRewards);
                    AnalyticsService?.TrackEvent($"combat_triggered_type_{encounter.combatType}_rewards_{rewardList}");

                    TransitionToGameplayState(GameplayState.Combat);
                    return; // Only trigger one combat at a time
                }
            }
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

            if (isTutorialCombat)
            {
                tutorialAttackNumber = 0;
                consecutiveDefenses = 0;
                Debug.Log("GameManager: Tutorial combat - skipping intro, starting attacks immediately");
                
                // Tutorial: Skip intro dialogue, start attacks after delay
                float attackDelay = combatConfig?.attackDelayAfterIntro ?? 3f;
                StartCoroutine(StartAttackAfterDelay(attackDelay));
            }
            else
            {
                // Normal combat: Play intro dialogue first
                Debug.Log("GameManager: Normal combat - playing intro dialogue");
                StartMercenaryEncounter();
            }
        }

        private void StartMercenaryEncounter()
        {
            if (AudioService == null || mercenaryEncounterEvent.IsNull) return;

            try
            {
                mercenaryEncounterInstance = AudioService.CreateAudioInstance(mercenaryEncounterEvent);
                
                // Use combat type from current encounter
                if (currentCombatEncounter != null)
                {
                    AudioService.SetParameter(mercenaryEncounterInstance, "CombatType", currentCombatEncounter.combatType);
                }
                
                mercenaryEncounterInstance.setCallback(OnMercenaryDialogueComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                mercenaryEncounterInstance.start();
                Debug.Log($"GameManager: Mercenary encounter started - Combat Type: {currentCombatEncounter?.combatType ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error starting mercenary encounter - {e.Message}");
            }
        }

        /// <summary>
        /// Start tutorial combat - same as real combat but with tutorial flag
        /// </summary>
        public void StartTutorialCombat()
        {
            Debug.Log("GameManager: Starting tutorial combat");
            isTutorialCombat = true;
            tutorialAttackNumber = 0;
            TransitionToGameplayState(GameplayState.Combat);
        }

        /// <summary>
        /// Start tutorial recovery (berry spawning) - reuses existing recovery system
        /// </summary>
        public void StartTutorialRecovery()
        {
            Debug.Log("GameManager: Starting tutorial recovery");
            TransitionToGameplayState(GameplayState.Recovery);
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
            // Use delay from combat config
            float attackDelay = combatConfig?.attackDelayAfterIntro ?? 3f;
            yield return new WaitForSeconds(attackDelay);
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
            // NORMAL COMBAT: Check max attack limit
            if (!isTutorialCombat)
            {
                int maxAttacks = combatConfig?.attackCount ?? 3;
                
                if (currentAttackIndex >= maxAttacks)
                {
                    Debug.Log($"GameManager: All {maxAttacks} attacks completed - concluding combat");
                    ConcludeCombat();
                    return;
                }
            }
            // TUTORIAL COMBAT: No limit, continues until 2 consecutive defenses

            var attackingMercenary = CreateMercenaryForAttack();
            if (attackingMercenary == null)
            {
                Debug.LogError("GameManager: Failed to create mercenary - aborting attack");
                return;
            }

            activeMercenaries.Add(attackingMercenary);
            
            if (isTutorialCombat)
            {
                Debug.Log($"GameManager: Executing tutorial attack {tutorialAttackNumber + 1} (consecutive defenses: {consecutiveDefenses})");
            }
            else
            {
                Debug.Log($"GameManager: Executing attack {currentAttackIndex + 1}");
            }

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
            float[] possibleOffsets = { -90f, 90f }; // Only spawn left (-90°) or right (+90°)
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
            
            // Record player heading when attack starts
            if (HeadTrackingService != null)
            {
                playerHeadingAtAttackStart = HeadTrackingService.CurrentHeading;
                Debug.Log($"GameManager: Attack starting - player heading: {playerHeadingAtAttackStart:F0}°");
            }

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

            // Use approach duration from combat config
            float approachDuration = combatConfig?.approachDuration ?? 4f;
            
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

            // Tutorial first attack is ALWAYS unavoidable
            if (isTutorialCombat && tutorialAttackNumber == 0)
            {
                Debug.Log($"[HEARTBEAT-DEBUG] Tutorial attack {tutorialAttackNumber} - FORCED HIT (unavoidable)");
        
                Debug.Log("GameManager: Tutorial first attack - unavoidable");
                return false;
            }

            float currentPlayerHeading = HeadTrackingService.CurrentHeading;
            
            // Calculate which side mercenary is on relative to starting position
            float angleToMercenary = Mathf.DeltaAngle(playerHeadingAtAttackStart, attackBearing);
            bool mercenaryOnLeft = angleToMercenary < 0; // Negative = left side
            
            // Calculate how much player turned from starting position
            float playerTurnAmount = Mathf.DeltaAngle(playerHeadingAtAttackStart, currentPlayerHeading);
            
            // Player needs to turn 10° or more in correct direction
            const float TURN_THRESHOLD = 10f;
            
            bool playerTurnedLeft = playerTurnAmount < -TURN_THRESHOLD;
            bool playerTurnedRight = playerTurnAmount > TURN_THRESHOLD;
            
            bool playerSucceeded = (mercenaryOnLeft && playerTurnedLeft) || (!mercenaryOnLeft && playerTurnedRight);
            
            Debug.Log($"GameManager: Defense check - " +
                    $"Mercenary on {(mercenaryOnLeft ? "LEFT" : "RIGHT")}, " +
                    $"Player turned {playerTurnAmount:F1}° ({(playerTurnAmount < 0 ? "LEFT" : "RIGHT")}), " +
                    $"Result: {(playerSucceeded ? "DEFENDED" : "HIT")}");
            
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
                Debug.Log("GameManager: Player DEFENDED the attack!");
            }
            else
            {
                Debug.Log("GameManager: Player was HIT!");
                TakeDamage();
                
            }

            if (playerSucceeded)
            {
                Debug.Log("GameManager: Player DEFENDED the attack!");
            }
            else
            {
                Debug.Log("GameManager: Player was HIT!");
                TakeDamage();
            }

            // TUTORIAL COMBAT: Track consecutive defenses
            if (isTutorialCombat)
            {
                tutorialAttackNumber++;
                
                if (playerSucceeded)
                {
                    consecutiveDefenses++;
                    Debug.Log($"GameManager: Tutorial - consecutive defenses: {consecutiveDefenses}/2");
                    
                    // END COMBAT: 2 consecutive defenses achieved
                    if (consecutiveDefenses >= 2)
                    {
                        Debug.Log("GameManager: Tutorial combat complete - 2 consecutive defenses!");
                        
                        // Fire event for TutorialManager
                        TutorialCombatCompleted?.Invoke();
                        
                        // SKIP normal combat conclusion (no outro dialogue)
                        CleanupCombat();
                        isTutorialCombat = false;
                        consecutiveDefenses = 0;
                        
                        // Transition based on health
                        if (playerHealth >= maxHealth)
                        {
                            TransitionToGameplayState(GameplayState.Wander);
                        }
                        else
                        {
                            TransitionToGameplayState(GameplayState.Recovery);
                        }
                        return; // Exit here, don't continue to normal combat logic
                    }
                }
                else
                {
                    consecutiveDefenses = 0; // Reset on failure
                    Debug.Log("GameManager: Tutorial - defense failed, consecutive counter reset");
                }
                
                // Fire attack completed event
                TutorialAttackCompleted?.Invoke(tutorialAttackNumber, playerSucceeded, consecutiveDefenses);
                
                // Continue attacking (no limit)
                Invoke(nameof(ExecuteNextAttack), 1f);
                return; // Exit here, tutorial has its own flow
            }

            // NORMAL COMBAT: Fixed number of attacks
            int maxAttacks = combatConfig?.attackCount ?? 3;
            
            if (currentAttackIndex < maxAttacks)
            {
                Debug.Log($"GameManager: Combat continues - attack {currentAttackIndex + 1}/{maxAttacks} next");
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

            // Save completion using combatType
            if (currentCombatEncounter != null)
            {
                string completionKey = $"combat_type_{currentCombatEncounter.combatType}_completed";
                StorageService?.Save(completionKey, true);
                Debug.Log($"GameManager: Saved combat completion: {completionKey}");
            }

            // Play defeat dialogue first, then transition
            PlayMercenaryDefeatDialogue();
        }

        private void PlayMercenaryDefeatDialogue()
        {
            if (AudioService == null || mercenaryDefeatEvent.IsNull || !AudioService.IsInstanceValid(mercenaryDefeatInstance))
            {
                Debug.LogWarning("GameManager: Mercenary defeat audio not available - skipping to transition");
                FinalizeCombatConclusion();
                return;
            }

            try
            {
                // Set CombatType parameter
                if (currentCombatEncounter != null)
                {
                    AudioService.SetParameter(mercenaryDefeatInstance, "CombatType", currentCombatEncounter.combatType);
                }

                mercenaryDefeatInstance.setCallback(OnMercenaryDefeatComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

                // Play defeat dialogue
                AudioService.PlayAudio(mercenaryDefeatInstance, Vector3.zero);
                Debug.Log($"GameManager: Playing mercenary defeat dialogue - Combat Type: {currentCombatEncounter?.combatType ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error playing defeat dialogue - {e.Message}");
                FinalizeCombatConclusion();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnMercenaryDefeatComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null) // Changed to TIMELINE_MARKER
            {
                Debug.Log("GameManager: Mercenary defeat dialogue completed via TIMELINE_MARKER - finalizing combat");
                Instance.FinalizeCombatConclusion();
            }
            return FMOD.RESULT.OK;
        }

        private void FinalizeCombatConclusion()
        {
            Debug.Log("GameManager: Finalizing combat conclusion");

            // Fire tutorial completion event
            if (isTutorialCombat)
            {
                Debug.Log("GameManager: Tutorial combat complete - firing event");
                TutorialCombatCompleted?.Invoke();
                isTutorialCombat = false; // Reset tutorial flag
            }

            // Transition based on health
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

            // Clear current encounter
            currentCombatEncounter = null;
        }

        private void CleanupCombat()
        {
            Debug.Log("GameManager: Cleaning up combat");

            isInCombat = false;
            activeMercenaries.Clear();
            currentAttackingMercenary = null;
            currentCombatEncounter = null; 

            if (AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mercenaryEncounterInstance))
                {
                    AudioService.StopAudio(mercenaryEncounterInstance, true);
                    AudioService.ReleaseAudio(mercenaryEncounterInstance);
                }

                // Cleanup defeat instance
                if (AudioService.IsInstanceValid(mercenaryDefeatInstance))
                {
                    mercenaryDefeatInstance.setCallback(null, EVENT_CALLBACK_TYPE.STOPPED);
                    AudioService.StopAudio(mercenaryDefeatInstance, true);
                    AudioService.ReleaseAudio(mercenaryDefeatInstance);
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

        #region Tutorial Mode

        /// <summary>
        /// Start gameplay tutorial
        /// Called by UIManager when entering tutorial phase
        /// </summary>
        public async void StartGameplayTutorial()
        {
            Debug.Log("GameManager: Starting gameplay tutorial");

            try
            {
                // Set tutorial mode
                SetInternalGameMode(GameMode.Tutorial);
                playerHealth = maxHealth;
                SaveHealthToPreferences();

                // Verify hardware services are running (should be from hardware setup)
                if (hardwareManager == null || !await hardwareManager.EnsureServicesRunning())
                {
                    Debug.LogError("GameManager: Hardware services not available for tutorial");
                    ExitTutorial();
                    return;
                }
                // Activate gameplay systems
                ActivateGameplaySystems();

                // Tell POIManager to enter tutorial mode (spawns only tutorial POI)
                if (poiManager != null)
                {
                    poiManager.EnterTutorialMode();
                    Debug.Log("GameManager: POIManager entered tutorial mode");
                }
                else
                {
                    Debug.LogError("GameManager: POIManager reference not set - tutorial cannot start");
                    ExitTutorial();
                    return;
                }

                // Tell TutorialManager to start tutorial sequence
                if (tutorialManager != null)
                {
                    tutorialManager.StartTutorial();
                    Debug.Log("GameManager: TutorialManager started");
                }
                else
                {
                    Debug.LogError("GameManager: TutorialManager reference not set - tutorial cannot start");
                    ExitTutorial();
                    return;
                }

                Debug.Log("GameManager: Gameplay tutorial started successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Failed to start gameplay tutorial - {e.Message}");
                ExitTutorial();
            }
        }

        /// <summary>
        /// Exit tutorial mode and cleanup
        /// Called when player presses back button or tutorial fails
        /// </summary>
        public void ExitTutorial()
        {
            Debug.Log("GameManager: Exiting tutorial mode");

            try
            {
                StopAllGameplayAudio();

                // resume gameplay so the buses can resume
                if (gameState == GameState.Suspended && suspensionReason == SuspensionReason.Tutorial)
                {
                    ResumeGameplay(SuspensionReason.Tutorial);
                    Debug.Log("GameManager: Resumed gameplay and unpaused audio buses");
                }
                
                // Stop tutorial manager
                if (tutorialManager != null)
                {
                    tutorialManager.StopTutorial();
                }

                // Exit tutorial mode in POI manager
                if (poiManager != null)
                {
                    poiManager.ExitTutorialMode();
                }

                // Reset mode
                SetInternalGameMode(GameMode.Inactive);

                Debug.Log("GameManager: Tutorial mode exited");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error exiting tutorial - {e.Message}");
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

            // Restore full health
            int healthToRestore = maxHealth - playerHealth;
            RestoreHealth(healthToRestore);

            // Fire tutorial berry collected event
            if (currentMode == GameMode.Tutorial)
            {
                Debug.Log("GameManager: Tutorial berry collected - firing event");
                TutorialBerryCollected?.Invoke();
            }

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

        public void ResetForSiteChange()
        {
            Debug.Log("GameManager: COMPLETE RESET for site change");

            // Stop ALL audio
            StopAllGameplayAudio();

            // Reset tutorial manager
            if (tutorialManager != null)
            {
                tutorialManager.Reset();
            }

            // Reset all gameplay state
            currentGameplayState = GameplayState.Wander;
            gameState = GameState.Suspended;
            isPlayerInPOIProximity = false;

            // Reset health to max (from new site data when loaded)
            // playerHealth will be reloaded from new JSON

            // Clear combat state
            CleanupCombat();
            CleanupRecovery();

            currentSessionId = null;

            Debug.Log("GameManager: Complete reset finished");
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

        #region Suspension System

        /// <summary>
        /// Suspend all gameplay - stops manager updates, pauses audio buses
        /// </summary>
        public void SuspendGameplay(SuspensionReason reason)
        {
            Debug.Log($"   SuspendGameplay CALLED - Reason: {reason}");
            Debug.Log($"   Current gameState: {gameState}");
            Debug.Log($"   Current suspensionReason: {suspensionReason}");
            if (gameState == GameState.Suspended)
            {
                Debug.LogWarning($"GameManager: Already suspended by {suspensionReason}, ignoring suspension request from {reason}");
                return;
            }

            gameState = GameState.Suspended;
            suspensionReason = reason;

            Debug.Log($" SUSPENDED - gameState set to: {gameState}, suspensionReason set to: {suspensionReason}");

            // Pause FMOD buses (except Voice bus for tutorial narrator)
            if (AudioService != null)
            {
                foreach (string bus in pausableBuses)
                {
                    AudioService.PauseBus(bus);
                }
            }

            Debug.Log($"GameManager: Gameplay SUSPENDED by {reason} - all managers will stop updating");
        }

        /// <summary>
        /// Resume gameplay - restarts manager updates, resumes audio buses
        /// </summary>
        public void ResumeGameplay(SuspensionReason reason)
        {
            Debug.Log($"   ResumeGameplay CALLED - Reason: {reason}");
            Debug.Log($"   Current gameState: {gameState}");
            Debug.Log($"   Current suspensionReason: {suspensionReason}");
            Debug.Log($"   Reasons match? {suspensionReason == reason}");

            if (gameState == GameState.Running)
            {
                Debug.LogWarning($"GameManager: Already running, ignoring resume request from {reason}");
                return;
            }

            if (suspensionReason != reason)
            {
                Debug.LogWarning($"GameManager: Cannot resume - suspended by {suspensionReason}, not by {reason}");
                return;
            }

            gameState = GameState.Running;
            suspensionReason = SuspensionReason.None;

            Debug.Log($" RESUMED - gameState set to: {gameState}, suspensionReason set to: {suspensionReason}");

            // Resume FMOD buses
            if (AudioService != null)
            {
                foreach (string bus in pausableBuses)
                {
                    AudioService.ResumeBus(bus);
                }
            }

            Debug.Log($"GameManager: Gameplay RESUMED from {reason} - all managers will resume updating");
        }

        #endregion

        #region Cleanup and Destruction

        private async void OnApplicationQuit()
        {
            try
            {
                Debug.Log("GameManager: Application quitting - performing cleanup");
                
                if (hardwareManager != null)
                {
                    hardwareManager.StopServices();
                    Debug.Log("GameManager: Hardware services stopped");
                }

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