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
    /// Master Controller for the Battle of Boyne game.
    /// Manages application phases, gameplay state, and coordinates all game systems.
    /// Combat and health are delegated to CombatManager.
    /// Spectator position injection is delegated to SpectatorManager.
    /// TutorialManager events are re-published from CombatManager for full backward compatibility.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        #region Enums and Data Structures

        public enum ApplicationPhase
        {
            Initializing,
            MainMenu,
            HardwareSetup,
            SiteSelection,
            Tutorial,
            ModeSelection,
            PlayerMode,
            SpectatorMode
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
            Wander,
            Interact,
            Combat,
            Recovery,
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
        [SerializeField] private CombatManager combatManager;
        [SerializeField] private SpectatorManager spectatorManager;

        [Header("Universal Audio")]
        private EventReference mainAmbientEvent;

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

        // System initialization flags
        private bool audioInitialized = false;
        private bool systemsReady = false;
        private bool hasDataConfiguration = false;

        #endregion

        #region Audio Instances

        private EventInstance mainAmbientInstance;

        #endregion

        #region Spectator State

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
        public int PlayerHealth => combatManager?.PlayerHealth ?? 3;
        public bool IsInCombat => combatManager?.IsInCombat ?? false;
        public Vector2 SpectatorLocation => spectatorLocation;
        public float SpectatorHeading => spectatorHeading;
        public bool IsReceivingSpectatorData => isReceivingSpectatorData;
        public bool SystemsReady => systemsReady;
        public bool IsPaused => isPaused;
        public bool IsSuspended => gameState == GameState.Suspended;
        public SuspensionReason CurrentSuspensionReason => suspensionReason;

        #endregion

        #region Public Events

        public event Action OnGamePaused;
        public event Action OnGameResumed;

        // Tutorial events — re-published from CombatManager so TutorialManager
        // never needs to know CombatManager exists
        public event Action<int, bool, int> TutorialAttackCompleted;
        public event Action TutorialCombatCompleted;
        public event Action TutorialBerryCollected;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
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
            if (gameState != GameState.Running) return;

            if (currentMode == GameMode.Player || currentMode == GameMode.Tutorial)
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
                ApplyGameDataConfiguration();
                hasDataConfiguration = true;
                Debug.Log("GameManager: Data was already loaded during services initialization");
            }
            else
            {
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

            // Delegate combat configuration entirely to CombatManager
            int maxHp = config.maxPlayerHealth;
            int savedHp = StorageService?.Load<int>("PlayerHealth", maxHp) ?? maxHp;
            combatManager?.Initialize(GameDataService?.CombatConfig, maxHp, savedHp);
            Debug.Log("GameManager: Combat configuration delegated to CombatManager");
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

                // Re-publish CombatManager tutorial events — TutorialManager subscribes
                // to GameManager and must never need to know CombatManager exists
                if (combatManager != null)
                {
                    combatManager.TutorialAttackCompleted += (n, d, c) => TutorialAttackCompleted?.Invoke(n, d, c);
                    combatManager.TutorialCombatCompleted += () => TutorialCombatCompleted?.Invoke();
                    combatManager.TutorialBerryCollected += () => TutorialBerryCollected?.Invoke();
                    Debug.Log("GameManager: Subscribed to CombatManager tutorial events");
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

            if (currentPhase == newPhase)
            {
                Debug.LogWarning($"GameManager: Re-entering phase {newPhase} (cleanup scenario)");

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
            SpectatorManager.Instance?.Activate(currentSessionId);
        }

        private void ExitPlayerMode()
        {
            Debug.Log("GameManager: Exiting PlayerMode - triggering complete site unload");

            if (SiteManager.Instance != null)
            {
                SiteManager.Instance.UnloadCurrentSite();
            }

            SuspendGameplaySystems();
            SetInternalGameMode(GameMode.Inactive);

            Debug.Log("GameManager: PlayerMode exit complete");
        }

        private void ExitSpectatorMode()
        {
            Debug.Log("GameManager: Exiting SpectatorMode phase");
            SpectatorManager.Instance?.Deactivate();
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

            Debug.Log($"GameManager: Gameplay systems suspended (state remains {currentGameplayState})");
        }

        private void ActivateGameplaySystems()
        {
            if (poiManager != null) poiManager.enabled = true;
            if (mapManager != null) mapManager.enabled = true;

            currentGameplayState = GameplayState.Wander;
        }

        public void ReadyToPlay()
        {
            // If suspended by the welcome greeting, do not override — let
            // OnWelcomeComplete handle the resume. Only set Running if not
            // currently suspended for a legitimate reason.
            if (gameState == GameState.Suspended && suspensionReason == SuspensionReason.Loading)
            {
                Debug.Log("GameManager: ReadyToPlay called but welcome greeting suspension active — skipping state set, buses will resume via OnWelcomeComplete");
                return;
            }
            gameState = GameState.Running;
            Debug.Log("GameManager: gameState set to Running - POIManager active");

            // Clear any navigation state that accumulated before Running was set.
            // This prevents stale cycle/cue state from blocking the first cue execution.
            poiManager?.ClearAllNavigationState();
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
                if (hardwareManager == null || !await hardwareManager.EnsureServicesRunning())
                {
                    Debug.LogError("GameManager: Hardware services not available");
                    return false;
                }

                currentSessionId = System.Guid.NewGuid().ToString();
                Debug.Log($"GameManager: Generated session ID: {currentSessionId}");

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

                // SpectatorManager.Activate() will stop GPS and head tracking after
                // EnterSpectatorMode calls it — but StopLocationUpdates is called here
                // first as an extra safety measure for the transition
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

            ApplyGameDataConfiguration();
            InitializeAndStartAmbientAudio();

            bool shouldShowTutorial = !PlayerPrefs.HasKey("TutorialCompleted");
            ApplicationPhase nextPhase = shouldShowTutorial ?
                ApplicationPhase.Tutorial :
                ApplicationPhase.ModeSelection;

            TransitionToPhase(nextPhase);
        }

        public void CompleteTutorial()
        {
            Debug.Log("GameManager: Tutorial completed successfully");

            PlayerPrefs.SetString("TutorialCompleted", "true");
            PlayerPrefs.Save();

            ExitTutorial();

            TransitionToPhase(ApplicationPhase.ModeSelection);
        }

        /// <summary>
        /// Pause the game - NOT a toggle, explicit pause operation.
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
            OnGamePaused?.Invoke();
            Debug.Log("GameManager: Game paused successfully");
        }

        /// <summary>
        /// Resume the game - NOT a toggle, explicit resume operation.
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
            Debug.Log("GameManager: StartAmbientMusic() called");
            Debug.Log($"GameManager: audioInitialized: {audioInitialized}");
            Debug.Log($"GameManager: AudioService: {AudioService != null}");

            if (!audioInitialized || AudioService == null)
            {
                Debug.LogError("GameManager: Cannot start ambient - audio not ready");
                return;
            }

            Debug.Log($"GameManager: mainAmbientInstance valid: {AudioService.IsInstanceValid(mainAmbientInstance)}");
            Debug.Log($"GameManager: mainAmbientEvent null: {mainAmbientEvent.IsNull}");

            try
            {
                if (AudioService.IsInstanceValid(mainAmbientInstance))
                {
                    mainAmbientInstance.getPlaybackState(out PLAYBACK_STATE playbackState);

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
        /// Stop all gameplay audio when exiting to main menu.
        /// </summary>
        public void StopAllGameplayAudio()
        {
            Debug.Log("GameManager: Stopping all gameplay audio");

            if (AudioService != null && AudioService.IsInstanceValid(mainAmbientInstance))
            {
                AudioService.StopAudio(mainAmbientInstance, false);
                Debug.Log("GameManager: Ambient music stopped");
            }

            if (poiManager != null)
                poiManager.StopAllAudio();

            combatManager?.StopAllAudio();

            Debug.Log("GameManager: All gameplay audio stopped");
        }

        #endregion

        #region Health Management — delegated to CombatManager

        public void TakeDamage() => combatManager?.TakeDamage();
        public void RestoreHealth(int amount = 1) => combatManager?.RestoreHealth(amount);

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
                    combatManager?.CleanupCombat();
                    break;
                case GameplayState.Recovery:
                    combatManager?.CleanupRecovery();
                    break;
            }
        }

        private void EnterGameplayState(GameplayState state)
        {
            switch (state)
            {
                case GameplayState.Wander:
                    EnablePOIManager();
                    combatManager?.CleanupHeartbeat();
                    break;

                case GameplayState.Interact:
                    break;

                case GameplayState.Combat:
                    DisablePOIManager();
                    combatManager?.StartCombat();
                    break;

                case GameplayState.Recovery:
                    combatManager?.StartRecovery();
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
                        // Combat trigger checking handled by CombatManager.Update()
                        break;
                    case GameplayState.Interact:
                        break;
                    case GameplayState.Combat:
                        // Combat audio runs entirely on FMOD timeline - no updates needed
                        break;
                    case GameplayState.Recovery:
                        combatManager?.UpdateRecovery();
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

        #region Tutorial Mode

        /// <summary>
        /// Start gameplay tutorial. Called by UIManager when entering tutorial phase.
        /// </summary>
        public async void StartGameplayTutorial()
        {
            Debug.Log("GameManager: Starting gameplay tutorial");

            try
            {
                SetInternalGameMode(GameMode.Tutorial);

                // Reset health to max at tutorial start
                if (combatManager != null)
                {
                    combatManager.LoadHealth(combatManager.MaxHealth, combatManager.MaxHealth);
                    Debug.Log("GameManager: Health reset to max for tutorial");
                }

                if (hardwareManager == null || !await hardwareManager.EnsureServicesRunning())
                {
                    Debug.LogError("GameManager: Hardware services not available for tutorial");
                    ExitTutorial();
                    return;
                }

                ActivateGameplaySystems();
                ReadyToPlay();

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
        /// Exit tutorial mode and cleanup. Called when player presses back or tutorial fails.
        /// </summary>
        public void ExitTutorial()
        {
            Debug.Log("GameManager: Exiting tutorial mode");

            try
            {
                StopAllGameplayAudio();

                if (gameState == GameState.Suspended && suspensionReason == SuspensionReason.Tutorial)
                {
                    ResumeGameplay(SuspensionReason.Tutorial);
                    Debug.Log("GameManager: Resumed gameplay and unpaused audio buses");
                }

                if (tutorialManager != null)
                    tutorialManager.StopTutorial();

                if (poiManager != null)
                    poiManager.ExitTutorialMode();

                SetInternalGameMode(GameMode.Inactive);

                Debug.Log("GameManager: Tutorial mode exited");
            }
            catch (Exception e)
            {
                Debug.LogError($"GameManager: Error exiting tutorial - {e.Message}");
            }
        }

        /// <summary>
        /// Start tutorial combat — delegates to CombatManager, preserves existing call chain.
        /// </summary>
        public void StartTutorialCombat()
        {
            Debug.Log("GameManager: StartTutorialCombat — delegating to CombatManager");
            combatManager?.StartTutorialCombat();
        }

        /// <summary>
        /// Start tutorial recovery — delegates to CombatManager, preserves existing call chain.
        /// </summary>
        public void StartTutorialRecovery()
        {
            Debug.Log("GameManager: StartTutorialRecovery — delegating to CombatManager");
            combatManager?.StartTutorialRecovery();
        }

        #endregion

        #region Spectator System — delegated to SpectatorManager

        private void OnSpectatorPositionUpdated(float latitude, float longitude, float heading)
        {
            spectatorLocation = new Vector2(latitude, longitude);
            spectatorHeading = heading;
            isReceivingSpectatorData = true;

            // Delegate position injection to SpectatorManager
            SpectatorManager.Instance?.OnPlayerPositionReceived(latitude, longitude, heading);

            // Keep UIManager map marker updated
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

        #region Site Reset

        public void ResetForSiteChange()
        {
            Debug.Log("GameManager: Complete reset for site change");

            StopAllGameplayAudio();

            if (tutorialManager != null)
                tutorialManager.Reset();

            combatManager?.CompleteReset();

            currentGameplayState = GameplayState.Wander;
            gameState = GameState.Suspended;
            isPlayerInPOIProximity = false;
            currentSessionId = null;

            Debug.Log("GameManager: Complete reset finished");
        }

        #endregion

        #region Suspension System

        /// <summary>
        /// Suspend all gameplay — stops manager updates, pauses audio buses.
        /// </summary>
        public void SuspendGameplay(SuspensionReason reason)
        {
            Debug.Log($"GameManager: SuspendGameplay CALLED - Reason: {reason}");
            Debug.Log($"GameManager: Current gameState: {gameState}");
            Debug.Log($"GameManager: Current suspensionReason: {suspensionReason}");

            if (gameState == GameState.Suspended)
            {
                Debug.LogWarning($"GameManager: Already suspended by {suspensionReason}, ignoring suspension request from {reason}");
                return;
            }

            gameState = GameState.Suspended;
            suspensionReason = reason;

            Debug.Log($"GameManager: SUSPENDED - gameState: {gameState}, suspensionReason: {suspensionReason}");

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
        /// Resume gameplay — restarts manager updates, resumes audio buses.
        /// </summary>
        public void ResumeGameplay(SuspensionReason reason)
        {
            Debug.Log($"GameManager: ResumeGameplay CALLED - Reason: {reason}");
            Debug.Log($"GameManager: Current gameState: {gameState}");
            Debug.Log($"GameManager: Current suspensionReason: {suspensionReason}");
            Debug.Log($"GameManager: Reasons match? {suspensionReason == reason}");

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

            Debug.Log($"GameManager: RESUMED - gameState: {gameState}, suspensionReason: {suspensionReason}");

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