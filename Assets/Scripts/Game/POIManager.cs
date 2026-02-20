using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using FMOD.Studio;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;
using System.Collections;

namespace LoGa.LudoEngine.Game
{
    public enum TargetingMode
    {
        None,
        Potential,
        Locked
    }

    [System.Serializable]
    public struct TargetingState
    {
        public TargetingMode mode;
        public POI targetPOI;
        public float timer;
        public float angleDifference;
    }

    [System.Serializable]
    public class CharacterPrefabMapping
    {
        public string characterName;
        public string characterId;
        public GameObject prefab;
    }

    [System.Serializable]
    public struct ProgressionInfo
    {
        public int completedPOIs;
        public int currentMaxCues;
        public int nextThreshold;
        public int maxPossible;
        public float progressToNext;
    }

    public class POIManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapManager mapManager;
        [SerializeField] private TextMeshProUGUI debugText;

        [Header("POI Prefabs")]
        [SerializeField] private GameObject defaultPOIPrefab;
        [SerializeField] private List<CharacterPrefabMapping> characterPrefabs;

        private float proximityRadius;
        private float dialogueRadius;
        private float maxCueRadius;
        private float discoveryDistance;
        private float cueStagingDelay;
        private float cyclePauseDelay;
        private int baseMaxActiveCues;
        private int maxMaxActiveCues;
        private int completionsToIncrease;
        private float targetLockTime;
        private float targetLockAngle;
        private float targetBreakAngle;
        private float maxTargetingDistance;

        [Header("UI References")]
        [SerializeField] private GameObject targetingIndicator;
        [SerializeField] private TextMeshProUGUI targetingText;
        [SerializeField] private TextMeshProUGUI zoneText;
        [SerializeField] private TextMeshProUGUI completionText;
        [SerializeField] private TextMeshProUGUI directionDebugText;

        private IGameDataService gameDataService;

        private TimeLayer currentLayer;
        private List<POI> activePOIs = new List<POI>();
        private Dictionary<POI, POIUpdateData> poiDataCache = new Dictionary<POI, POIUpdateData>();

        private List<POI> activeCuePOIs = new List<POI>();
        private TargetingState targetingState = new TargetingState { mode = TargetingMode.None };

        private EventReference welcomeGreetingEvent;
        private EventReference rewardAnnouncementEvent;
        private EventReference targetingFeedbackEvent;
        private EventInstance welcomeInstance;
        private EventInstance rewardInstance;
        private EventInstance targetingFeedbackInstance;

        private bool isInitialized = false;
        private int totalCompletedPOIs;
        private int currentMaxActiveCues;
        private static bool isRewardAudioPlaying = false;

        private float cueTimer = 0f;
        private int currentCueIndex = 0;
        private bool isInCyclePause = false;
        private float cyclePauseTimer = 0f;
        private int updateFrameCounter = 0;
        private bool waitingForNextCue = false;

        private Dictionary<string, bool> discoveredThisSession = new Dictionary<string, bool>();
        private Dictionary<string, bool> proximityReachedThisSession = new Dictionary<string, bool>();

        private IStorageService storageService;
        private IAudioService audioService;
        private ILocationService locationService;
        private IHeadTrackingService headTrackingService;
        private IFirebaseService firebaseService;
        private IAnalyticsService analyticsService;

        public int CurrentMaxActiveCues => currentMaxActiveCues;

        private IStorageService StorageService
        {
            get
            {
                if (storageService == null)
                    storageService = ServiceLocator.GetService<IStorageService>();
                return storageService;
            }
        }

        private IAudioService AudioService
        {
            get
            {
                if (audioService == null)
                    audioService = ServiceLocator.GetService<IAudioService>();
                return audioService;
            }
        }

        private ILocationService LocationService
        {
            get
            {
                if (locationService == null)
                    locationService = ServiceLocator.GetService<ILocationService>();
                return locationService;
            }
        }

        private IHeadTrackingService HeadTrackingService
        {
            get
            {
                if (headTrackingService == null)
                    headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
                return headTrackingService;
            }
        }

        private IFirebaseService FirebaseService
        {
            get
            {
                if (firebaseService == null)
                    firebaseService = ServiceLocator.GetService<IFirebaseService>();
                return firebaseService;
            }
        }

        private IAnalyticsService AnalyticsService
        {
            get
            {
                if (analyticsService == null)
                    analyticsService = ServiceLocator.GetService<IAnalyticsService>();
                return analyticsService;
            }
        }

        private async void Start()
        {
            try
            {
                Debug.Log("POIManager: Starting - waiting for site selection");

                gameDataService = ServiceLocator.GetService<IGameDataService>();
                if (gameDataService == null)
                {
                    Debug.LogError("POIManager: CRITICAL - GameDataService not found!");
                    return;
                }

                Debug.Log("POIManager: GameDataService obtained");

                // Load default progression values
                LoadProgressionData();

                // Subscribe to site loading event
                if (SiteManager.Instance != null)
                {
                    SiteManager.Instance.OnSiteLoaded += OnSiteLoaded;
                    Debug.Log("POIManager: Subscribed to OnSiteLoaded event");
                }
                else
                {
                    Debug.LogError("POIManager: SiteManager.Instance not found!");
                }

                Debug.Log("POIManager: Waiting for site selection...");
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager Start failed: {e.Message}");
            }
        }

        private async void OnSiteLoaded(Site site)
        {
            try
            {
                Debug.Log($"POIManager: Site '{site.name}' loaded, now initializing audio...");

                // Wait for AudioService to be ready
                audioService = await ServiceLocator.GetInitializedService<IAudioService>();
                if (audioService == null)
                {
                    Debug.LogError("POIManager: AudioService failed to initialize");
                    return;
                }

                // CRITICAL: Wrap audio initialization in try-catch, let no exceptions loose in event handlers, will cause event accummulation
                bool audioInitialized = false;
                try
                {
                    audioInitialized = InitializeAudioComponents();
                }
                catch (Exception audioEx)
                {
                    Debug.LogError($"POIManager: Audio initialization failed: {audioEx.Message} - continuing without audio");
                    audioInitialized = false; // Continue anyway
                }

                if (!audioInitialized)
                {
                    Debug.LogWarning("POIManager: Audio components failed to initialize - POIs will load without audio");
                }

                // Subscribe to time layer events
                TimeLayerManager.Instance.TimeLayerChanging += OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;

                // Load current layer (now that JSON data is loaded)
                OnTimeLayerChanged(TimeLayerManager.Instance.CurrentLayer);

                isInitialized = true;
                Debug.Log("POIManager: Full initialization complete after site load");
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager OnSiteLoaded failed: {e.Message}");
                Debug.LogError($"POIManager Stack trace: {e.StackTrace}");
            }
        }
        private void ApplyJSONConfiguration()
        {
            if (gameDataService?.GameConfig == null)
            {
                Debug.LogError("POIManager: Cannot apply JSON configuration - GameConfig not available");
                return;
            }

            var config = gameDataService.GameConfig;

            proximityRadius = config.proximityRadius;
            dialogueRadius = config.dialogueRadius;
            maxCueRadius = config.maxCueRadius;
            cueStagingDelay = config.cueStagingDelay;
            cyclePauseDelay = config.cyclePauseDelay;
            baseMaxActiveCues = config.baseMaxActiveCues;
            maxMaxActiveCues = config.maxMaxActiveCues;
            completionsToIncrease = config.navigationUpgradeThreshold;
            targetLockTime = config.targetLockTime;
            targetLockAngle = config.targetLockAngle;
            targetBreakAngle = config.targetBreakAngle;
            maxTargetingDistance = config.maxTargetingDistance;

            RecalculateMaxActiveCues();

            Debug.Log($"POIManager: Applied JSON configuration");
            Debug.Log($"  - Proximity: {proximityRadius}m, Dialogue: {dialogueRadius}m");
            Debug.Log($"  - Max Active Cues: {currentMaxActiveCues} (Base: {baseMaxActiveCues}, Max: {maxMaxActiveCues})");
        }

        private void LoadProgressionData()
        {
            if (StorageService == null)
            {
                Debug.LogWarning("StorageService not available - using default progression values");
                currentMaxActiveCues = baseMaxActiveCues;
                return;
            }

            totalCompletedPOIs = StorageService.Load<int>("TotalCompletedPOIs", 0);
            currentMaxActiveCues = StorageService.Load<int>("CurrentMaxActiveCues", baseMaxActiveCues);

            Debug.Log($"Loaded progression: {totalCompletedPOIs} completed POIs, max cues: {currentMaxActiveCues}");
        }

        private void RecalculateMaxActiveCues()
        {
            currentMaxActiveCues = totalCompletedPOIs >= completionsToIncrease ? maxMaxActiveCues : baseMaxActiveCues;
        }

        private bool InitializeAudioComponents()
        {
            if (AudioService == null)
            {
                Debug.LogError("Cannot initialize audio components - AudioService not available");
                return false;
            }

            // LOAD from JSON instead of inspector
            var config = gameDataService?.GameConfig;
            if (config == null)
            {
                Debug.LogWarning("POIManager: No JSON config available - skipping audio initialization");
                return true; // Don't fail, just skip
            }

            // Load reward announcement event (optional)
            if (!string.IsNullOrEmpty(config.rewardAnnouncementEvent))
            {
                rewardAnnouncementEvent = gameDataService.GetAudioEventReference(config.rewardAnnouncementEvent);
                if (!rewardAnnouncementEvent.IsNull)
                {
                    rewardInstance = AudioService.CreateAudioInstance(rewardAnnouncementEvent);
                    if (rewardInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogWarning("Failed to create reward instance - continuing without reward audio");
                    }
                    else
                    {
                        Debug.Log("Reward instance created from JSON");
                    }
                }
            }
            else
            {
                Debug.Log("POIManager: No reward announcement event in JSON - skipping");
            }

            // Load welcome greeting event (optional)
            if (!string.IsNullOrEmpty(config.welcomeGreetingEvent))
            {
                welcomeGreetingEvent = gameDataService.GetAudioEventReference(config.welcomeGreetingEvent);
                if (!welcomeGreetingEvent.IsNull)
                {
                    welcomeInstance = AudioService.CreateAudioInstance(welcomeGreetingEvent);
                    if (welcomeInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogWarning("Failed to create welcome instance - continuing without welcome audio");
                    }
                    else
                    {
                        Debug.Log("Welcome instance created from JSON");
                    }
                }
            }
            else
            {
                Debug.Log("POIManager: No welcome greeting event in JSON - skipping");
            }

            // Load targeting feedback event
            if (!string.IsNullOrEmpty(config.targetingFeedbackSound))
            {
                targetingFeedbackEvent = gameDataService.GetAudioEventReference(config.targetingFeedbackSound);
                if (!targetingFeedbackEvent.IsNull)
                {
                    targetingFeedbackInstance = AudioService.CreateAudioInstance(targetingFeedbackEvent);
                    if (targetingFeedbackInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogWarning("Failed to create targeting feedback instance - continuing without targeting audio");
                    }
                    else
                    {
                        Debug.Log("POIManager: Targeting feedback instance created from JSON");
                    }
                }
                else
                {
                    Debug.LogWarning("POIManager: Targeting feedback event not found in banks");
                }
            }
            else
            {
                Debug.Log("POIManager: No targeting feedback event in JSON - skipping");
            }

            return true;
        }

        public void PlayWelcomeGreeting()
        {
            if (!isInitialized || StorageService == null || AudioService == null) return;

            bool hasPlayedWelcome = StorageService.Load<bool>("HasPlayedWelcomeDialogue");
            if (!hasPlayedWelcome && AudioService.IsInstanceValid(welcomeInstance))
            {
                GameManager.Instance?.SuspendNavigationAudio("greeting_audio");
                welcomeInstance.setCallback(OnWelcomeComplete, EVENT_CALLBACK_TYPE.STOPPED);
                AudioService.PlayAudio(welcomeInstance, Vector3.zero);
                StorageService.Save("HasPlayedWelcomeDialogue", true);
                AnalyticsService?.TrackEvent("welcome_greeting_played");
                Debug.Log("Battle Oak greeting started - navigation suspended");
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnWelcomeComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.STOPPED)
            {
                GameManager.Instance?.ResumeNavigationAudio("oak_greeting_complete");
                Debug.Log("Oak greeting finished - navigation resumed");
            }
            return FMOD.RESULT.OK;
        }

        private void DebugPOIStatus()
        {
            Debug.Log("=== POI DEBUG STATUS ===");
            Debug.Log($"POIManager initialized: {isInitialized}");
            Debug.Log($"Active POIs count: {activePOIs?.Count ?? 0}");
            Debug.Log($"Current time layer: {TimeLayerManager.Instance?.CurrentLayer?.layerIndex ?? -1}");

            if (activePOIs != null && activePOIs.Count > 0)
            {
                foreach (var poi in activePOIs)
                {
                    if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                    {
                        Debug.Log($"  POI '{poi.characterName}' - Distance: {data.distance:F1}m, InProximity: {poi.isInProximity}, Bearing: {data.bearing:F1}°");
                    }
                    else
                    {
                        Debug.Log($"  POI '{poi.characterName}' - NO DATA IN CACHE");
                    }
                }
            }

            Debug.Log($"Targeting mode: {targetingState.mode}");
            Debug.Log($"Target POI: {targetingState.targetPOI?.characterName ?? "None"}");
            Debug.Log("========================");
        }

        private void Update()
        {
            if (!isInitialized) return;

            //Debug.Log($"GameplayState: {GameManager.Instance?.CurrentGameplayState}, POIs: {activePOIs.Count}");

            if (TimeLayerManager.Instance.IsTransitioning ||
                GameManager.Instance?.CurrentMode != GameManager.GameMode.Player)
                return;

            if (LocationService == null) return;

            Vector2 currentLocation = LocationService.GetCurrentLocation();
            if (currentLocation == Vector2.zero) return;

            updateFrameCounter++;

            UpdatePOIDataCache(currentLocation.x, currentLocation.y);
            UpdatePOIProximity();

            if (GameManager.Instance.CurrentGameplayState == GameManager.GameplayState.Wander)
            {
                UpdateNavigationAndTargeting(currentLocation.x, currentLocation.y);
            }

            CheckNarrationCompletions();

            if (!activePOIs.Any(poi => poi.isInProximity))
            {
                RemoveCompletedPOIs();
            }

            if (updateFrameCounter % 60 == 0)
            {
                //DebugPOIStatus();
                UpdateDiscoveryLogic();
            }

            if (updateFrameCounter % 12 == 0)
            {
                UpdateDebugDisplay(currentLocation);
            }
        }

        public void SilenceAllPOIAudio()
        {
            foreach (var poi in activePOIs)
            {
                if (poi.isInProximity)
                {
                    poi.SilenceAudio();
                }
            }
        }

        public void ResumeAllPOIAudio()
        {
            foreach (var poi in activePOIs)
            {
                if (poi.isInProximity && poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    poi.ResumeAudio(data.audioPosition);
                }
            }
        }

        public void ClearAllNavigationState()
        {
            // Clear debug text for all POIs
            foreach (var poi in activePOIs)
            {
                poi.ClearDirectionDebug();
            }

            ClearStandardNavigation(forcePause: false); 
            ClearTargeting(false); // No sound (clearing for other reasons)
         
        }

        private void UpdatePOIDataCache(float currentLat, float currentLon)
        {
            if (HeadTrackingService == null) return;

            float headingAngle = HeadTrackingService.CurrentHeading;
            poiDataCache.Clear();

            foreach (var poi in activePOIs)
            {
                float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
                float bearing = CalculateBearing(currentLat, currentLon, poi.latitude, poi.longitude);
                Vector3 audioPosition = CalculateAudioPosition(poi, currentLat, currentLon, headingAngle);
                float angleDifference = Mathf.Abs(Mathf.DeltaAngle(headingAngle, bearing));

                poiDataCache[poi] = new POIUpdateData
                {
                    distance = distance,
                    bearing = bearing,
                    audioPosition = audioPosition,
                    angleDifference = angleDifference
                };
            }
        }

        private void UpdatePOIProximity()
        {
            bool foundProximityPOI = false;
            foreach (var poi in activePOIs)
            {
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    float zoneValue = CalculateZoneFromDistance(data.distance);
                    bool wasInProximity = poi.isInProximity;

                    poi.UpdateProximity(data, zoneValue);

                    if (zoneValue > 0 && !wasInProximity && !proximityReachedThisSession.ContainsKey(poi.characterId))
                    {
                        proximityReachedThisSession[poi.characterId] = true;
                        AnalyticsService?.TrackEvent($"character_music_started_{poi.characterId}");

                        if (zoneText != null)
                            zoneText.text = $"Zone: {zoneValue:F2} ({poi.characterName})";
                        foundProximityPOI = true;
                    }
                    else if (zoneValue > 0 && !foundProximityPOI)
                    {
                        if (zoneText != null)
                            zoneText.text = $"Zone: {zoneValue:F2} ({poi.characterName})";
                        foundProximityPOI = true;
                    }
                }
            }

            if (!foundProximityPOI && zoneText != null)
            {
                zoneText.text = "Zone: 0 (No proximity)";
            }
        }

        private void UpdateNavigationAndTargeting(float currentLat, float currentLon)
        {
            if (activePOIs.Count == 0) return;

            if (isRewardAudioPlaying)
            {
                return;
            }

            var proximityPOI = poiDataCache
                .Where(p => p.Value.distance <= proximityRadius)
                .OrderBy(p => p.Value.distance)
                .Select(p => p.Key)
                .FirstOrDefault();

            if (proximityPOI != null)
            {
                ClearAllNavigationState();
                return;
            }

            var eligiblePOIs = GetEligibleNavigationPOIs();

            if (eligiblePOIs.Count == 0)
            {
                ClearAllNavigationState();
                return;
            }

            UpdateTargetingLogic(eligiblePOIs);

            if (targetingState.mode == TargetingMode.Locked)
            {
                HandleTargetedNavigation();
            }
            else
            {
                HandleStandardNavigation(eligiblePOIs);
            }
        }

        private List<POI> GetEligibleNavigationPOIs()
        {
            return poiDataCache
                .Where(p => p.Value.distance > proximityRadius && p.Value.distance <= maxCueRadius)
                .OrderBy(p => p.Value.distance)
                .Take(currentMaxActiveCues)
                .Select(p => p.Key)
                .ToList();
        }

        private void UpdateTargetingLogic(List<POI> eligiblePOIs)
        {
            switch (targetingState.mode)
            {
                case TargetingMode.None:
                    CheckForPotentialTarget(eligiblePOIs);
                    break;

                case TargetingMode.Potential:
                    UpdatePotentialTargeting();
                    break;

                case TargetingMode.Locked:
                    UpdateLockedTargeting(eligiblePOIs);
                    break;
            }
        }

        private void CheckForPotentialTarget(List<POI> eligiblePOIs)
        {
            foreach (var poi in eligiblePOIs)
            {
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data) &&
                    data.angleDifference <= targetLockAngle)
                {
                    SetPotentialTarget(poi, data);
                    return;
                }
            }
        }

        private void UpdatePotentialTargeting()
        {
            if (!poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData data))
            {
                ClearTargeting();
                return;
            }

            if (data.angleDifference <= targetLockAngle)
            {
                targetingState.timer += Time.deltaTime;
                targetingState.angleDifference = data.angleDifference;

                UpdateTargetingUI();

                if (targetingState.timer >= targetLockTime)
                {
                    LockTarget();
                }
            }
            else
            {
                ClearTargeting(false); // No unlock sound (was just potential, not locked)
            }
        }

        private void UpdateLockedTargeting(List<POI> eligiblePOIs)
        {
            if (!eligiblePOIs.Contains(targetingState.targetPOI))
            {
                ClearTargeting(true); // true = breaking lock (play unlock sound)
                return;
            }

            if (poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData currentTargetData))
            {
                // Check for break condition (angle > break angle)
                if (currentTargetData.angleDifference > targetBreakAngle)
                {
                    ClearTargeting(true); // true = breaking lock (play unlock sound)
                    return;
                }

                // Check for auto-switch to closer POI
                CheckForAutoSwitch(eligiblePOIs, currentTargetData);

                // Update angle tracking
                targetingState.angleDifference = currentTargetData.angleDifference;
            }
        }

        /// <summary>
        /// Check if a closer POI is within lock angle and auto-switch to it
        /// </summary>
        private void CheckForAutoSwitch(List<POI> eligiblePOIs, POIUpdateData currentTargetData)
        {
            foreach (var poi in eligiblePOIs)
            {
                // Skip if this is the current target
                if (poi == targetingState.targetPOI) continue;

                if (poiDataCache.TryGetValue(poi, out POIUpdateData poiData))
                {
                    // Check if this POI is:
                    // 1. Closer than current target
                    // 2. Within lock angle
                    if (poiData.distance < currentTargetData.distance &&
                        poiData.angleDifference <= targetLockAngle)
                    {
                        Debug.Log($"Auto-switching from {targetingState.targetPOI.characterName} ({currentTargetData.distance:F1}m) to {poi.characterName} ({poiData.distance:F1}m)");

                        // Reset old target's cue index
                        targetingState.targetPOI.UpdateTargetingState(false);
                        targetingState.targetPOI.ResetNavigationCueIndex();
                        targetingState.targetPOI.StopNavigationCue();

                        // Switch to new target
                        targetingState.targetPOI = poi;
                        targetingState.angleDifference = poiData.angleDifference;

                        // Update visual state
                        poi.UpdateTargetingState(true);

                        // Play lock sound (player remains in locked state)
                        if (AudioService != null && AudioService.IsInstanceValid(targetingFeedbackInstance))
                        {
                            AudioService.SetParameter(targetingFeedbackInstance, "LockState", 1.0f); // 1.0 = lock
                            AudioService.PlayAudio(targetingFeedbackInstance, Vector3.zero);
                            Debug.Log("Targeting feedback: LOCK (auto-switch)");
                        }

                        if (targetingText != null)
                            targetingText.text = $"Switched to {poi.characterName}";

                        AnalyticsService?.TrackEvent($"character_auto_switched_{poi.characterId}");

                        return; // Only switch once per frame
                    }
                }
            }
        }

        private void HandleTargetedNavigation()
        {
            var targetPOI = targetingState.targetPOI;
            if (!poiDataCache.TryGetValue(targetPOI, out POIUpdateData data)) return;

            Debug.Log($" Targeted: Waiting={targetPOI.IsWaitingForCueCompletion()}, Completed={targetPOI.CheckNavigationCueCompletion()}, DelayActive={waitingForNextCue}");

            // Check if previous targeted cue completed
            if (targetPOI.CheckNavigationCueCompletion())
            {
                Debug.Log($" Targeted cue completed via command instrument - starting {cueStagingDelay}s delay");
                cueTimer = 0f;
                waitingForNextCue = true;
                return;
            }

            // Handle delay phase after command instrument completion
            if (waitingForNextCue)
            {
                cueTimer += Time.deltaTime;

                if (cueTimer >= cueStagingDelay)
                {
                    Debug.Log($" Targeted delay complete - executing next cue");

                    int sequentialCueIndex = targetPOI.GetNextNavigationCueIndex();

                    var config = new NavigationCueConfig
                    {
                        cueType = NavigationCueType.Sequential,
                        cueIndex = sequentialCueIndex,
                        maxDistance = maxTargetingDistance,
                        isTargeted = true
                    };

                    targetPOI.ExecuteNavigationCue(data.audioPosition, config);

                    cueTimer = 0f;
                    waitingForNextCue = false;
                    Debug.Log($" TARGETED SEQUENTIAL cue executed: index {sequentialCueIndex}");
                }
                return;
            }

            // If no cue is playing and no delay active, start first targeted cue
            if (!targetPOI.IsWaitingForCueCompletion() && !waitingForNextCue)
            {
                Debug.Log($" Starting first targeted cue");

                int sequentialCueIndex = targetPOI.GetNextNavigationCueIndex();

                var config = new NavigationCueConfig
                {
                    cueType = NavigationCueType.Sequential,
                    cueIndex = sequentialCueIndex,
                    maxDistance = maxTargetingDistance,
                    isTargeted = true
                };

                targetPOI.ExecuteNavigationCue(data.audioPosition, config);
                Debug.Log($" TARGETED SEQUENTIAL cue started: index {sequentialCueIndex}");
            }
        }

        private void HandleStandardNavigation(List<POI> eligiblePOIs)
        {
            activeCuePOIs = eligiblePOIs;
            if (activeCuePOIs.Count == 0) return;

            // Check if any POI's cue completed via command instrument
            bool anyCompleted = false;
            foreach (var poi in activeCuePOIs)
            {
                if (poi.CheckNavigationCueCompletion())
                {
                    // Check if this was the last cue in the cycle
                    if (currentCueIndex >= activeCuePOIs.Count)
                    {
                        // Last cue completed - go directly to cycle pause (skip gap)
                        isInCyclePause = true;
                        cyclePauseTimer = 0f;
                        currentCueIndex = 0;
                        waitingForNextCue = false;
                        Debug.Log($"Navigation cycle complete - starting {cyclePauseDelay}s pause");
                    }
                    else
                    {
                        // Not last cue - start gap delay
                        cueTimer = 0f;
                        waitingForNextCue = true;
                        Debug.Log($"Standard cue completed via command instrument for {poi.characterName} - starting {cueStagingDelay}s delay");
                    }
                    anyCompleted = true;
                    break;
                }
            }

            if (anyCompleted) return;

            // Handle delay phase AFTER cue completion (this is the GAP)
            if (waitingForNextCue)
            {
                cueTimer += Time.deltaTime;

                if (cueTimer >= cueStagingDelay)
                {
                    waitingForNextCue = false;
                    cueTimer = 0f;
                    Debug.Log($"Gap complete - ready for next POI");
                }
                return;
            }

            // Handle cycle pause (pause BETWEEN cycles)
            if (isInCyclePause)
            {
                cyclePauseTimer += Time.deltaTime;

                if (cyclePauseTimer >= cyclePauseDelay)
                {
                    isInCyclePause = false;
                    cyclePauseTimer = 0f;
                    currentCueIndex = 0;
                    cueTimer = 0f;
                    Debug.Log("Exiting cycle pause - starting new cycle");
                }
                return;
            }

            // Execute next cue
            if (currentCueIndex < activeCuePOIs.Count && !waitingForNextCue)
            {
                var poi = activeCuePOIs[currentCueIndex];

                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    var config = new NavigationCueConfig
                    {
                        cueType = NavigationCueType.DistanceBased,
                        cueIndex = 1,
                        maxDistance = maxTargetingDistance,
                        isTargeted = false
                    };

                    poi.ExecuteNavigationCue(data.audioPosition, config);
                    currentCueIndex++;
                    waitingForNextCue = true; // Wait for command instrument completion
                    Debug.Log($"Standard navigation cue executed for {poi.characterName} - POI {currentCueIndex}/{activeCuePOIs.Count}");
                }
            }
        }

        private void CheckNarrationCompletions()
        {
            var poisToCheck = activePOIs.ToList();

            foreach (var poi in poisToCheck)
            {
                if (poi.CheckNarrationCompletion())
                {
                    OnPOINarrationComplete(poi);
                }
            }
        }

        private void OnPOINarrationComplete(POI poi)
        {
            Debug.Log($"NARRATION COMPLETE: {poi.characterName} has finished their dialogue!");

            // PORTAL HANDLING: Allow activation but don't complete/remove
            if (poi.IsPortal)
            {
                Debug.Log($"Portal {poi.characterName} activated - triggering time travel");

                // Trigger portal functionality WITHOUT marking as completed
                poi.TriggerPortalActivation();

                // Track portal usage
                AnalyticsService?.TrackEvent($"portal_used_{poi.characterId}");

                return; // Don't run normal completion logic - keep portal active
            }

            poi.MarkAsCompleted();
            UpdateProgressionTracking(poi);

            AnalyticsService?.TrackEvent($"character_unlocked_{poi.characterId}");

            if (StorageService != null)
            {
                string unlockKey = $"Character_{poi.characterId}_Unlocked";
                StorageService.Save(unlockKey, true);

                CheckGameCompletion();
            }

            if (poi.hasReward && poi.rewardId > 0)
            {
                HandlePOIReward(poi);
            }

            RemoveCompletedPOI(poi);

            Debug.Log("POIManager: POI completion finished");
        }

        private void UpdateProgressionTracking(POI poi)
        {
            if (!poi.IsPortal && StorageService != null)
            {
                totalCompletedPOIs++;
                StorageService.Save("TotalCompletedPOIs", totalCompletedPOIs);
                UpdateMaxActiveCues();
                StorageService.Save("CurrentMaxActiveCues", currentMaxActiveCues);
                Debug.Log($"Total completed POIs: {totalCompletedPOIs}");
            }
        }

        private void HandlePOIReward(POI poi)
        {
            Debug.Log($"Starting reward handling for {poi.characterName} (Reward ID: {poi.rewardId})");

            if (StorageService != null)
            {
                string rewardUnlockKey = $"Reward{poi.rewardId}Unlocked";
                StorageService.Save(rewardUnlockKey, true);
                Debug.Log($"Unlocked reward: {rewardUnlockKey}");

                string characterUnlockKey = $"Character_{poi.characterId}_Unlocked";
                StorageService.Save(characterUnlockKey, true);
            }

            AnalyticsService?.TrackEvent($"reward_unlocked_{poi.rewardId}");

            try
            {
                var inventoryItem = new InventoryItem
                {
                    itemId = poi.rewardId,
                    name = poi.rewardName,
                    description = $"Artifact from {currentLayer.layerName}",
                    type = ItemType.Artifact,
                    audioClip = poi.characterAudioEvent,
                    sourceTimeLayer = currentLayer.layerName,
                    sourceCharacterId = poi.characterId
                };
                InventoryManager.Instance.AddItem(inventoryItem);
                Debug.Log($"Added artifact to inventory: {poi.rewardName} (Reward ID: {poi.rewardId})");
            }
            catch (System.NullReferenceException)
            {
                Debug.LogWarning("InventoryManager not available - skipping inventory add");
            }

            PlayRewardAnnouncement(poi.rewardId);
        }

        private void CheckGameCompletion()
        {
            if (StorageService == null || gameDataService == null) return;

            int totalNonPortalPOIs = GetTotalPOICountFromJSON();

            if (totalNonPortalPOIs <= 0) return;

            // Get all character IDs and count unlocked ones
            var allTimeLayerData = gameDataService.GetAllTimeLayerData();
            int unlockedCount = 0;

            foreach (var layer in allTimeLayerData)
            {
                foreach (var poi in layer.pois)
                {
                    // Skip portals from completion check
                    bool isPortal = !string.IsNullOrEmpty(poi.portalType) && poi.portalType != "None";
                    if (!isPortal && StorageService.Load<bool>($"Character_{poi.characterId}_Unlocked"))
                    {
                        unlockedCount++;
                    }
                }
            }

            if (unlockedCount >= totalNonPortalPOIs)
            {
                StorageService.Save("GameCompleted", true);
                AnalyticsService?.TrackEvent("game_completed_all_characters_unlocked");
                TrackFinalInventoryState();
                TriggerGameEndSequence();
            }
        }

        private int GetTotalPOICountFromJSON()
        {
            if (gameDataService == null) return 0;

            try
            {
                int totalCount = 0;
                var allTimeLayerData = gameDataService.GetAllTimeLayerData();

                foreach (var layer in allTimeLayerData)
                {
                    // Count only non-portal characters for completion
                    foreach (var poi in layer.pois)
                    {
                        bool isPortal = !string.IsNullOrEmpty(poi.portalType) && poi.portalType != "None";
                        if (!isPortal)
                        {
                            totalCount++;
                        }
                    }
                }

                return totalCount;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to get total POI count from JSON: {e.Message}");
                return 0;
            }
        }

        private void TrackFinalInventoryState()
        {
            try
            {
                if (InventoryManager.Instance != null)
                {
                    var inventory = InventoryManager.Instance.GetInventory();
                    if (inventory != null)
                    {
                        var characters = inventory.GetItemsByType(ItemType.Character);
                        var artifacts = inventory.GetItemsByType(ItemType.Artifact);

                        AnalyticsService?.TrackEvent($"session_end_inventory_characters_{characters.Count}_artifacts_{artifacts.Count}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to track final inventory state: {e.Message}");
            }
        }

        private void TriggerGameEndSequence()
        {
            Debug.Log("POIManager: All characters unlocked - triggering end sequence");
            AnalyticsService?.TrackEvent("game_end_sequence_triggered");
        }

        private void RemoveCompletedPOI(POI poi)
        {
            Debug.Log($"Immediately removing completed POI: {poi.characterName}");
            poi.Cleanup();
            activePOIs.Remove(poi);
            if (currentLayer?.pois != null) currentLayer.pois.Remove(poi);
            if (poiDataCache.ContainsKey(poi)) poiDataCache.Remove(poi);
        }

        private void UpdateMaxActiveCues()
        {
            int newMax = baseMaxActiveCues;

            if (totalCompletedPOIs >= completionsToIncrease)
            {
                newMax = maxMaxActiveCues;
            }

            if (newMax != currentMaxActiveCues)
            {
                currentMaxActiveCues = newMax;
                AnalyticsService?.TrackEvent($"navigation_upgraded_to_{currentMaxActiveCues}_cues");
                Debug.Log($"Navigation complexity increased! Max active POIs: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})");
            }
        }

        private void PlayRewardAnnouncement(int rewardId)
        {
            Debug.Log($"Attempting to play reward announcement for ID: {rewardId}");

            if (AudioService == null)
            {
                Debug.LogError("AudioService not available for reward announcement");
                return;
            }

            if (rewardInstance.handle == IntPtr.Zero)
            {
                Debug.LogError("Reward instance handle is zero - not initialized properly");
                return;
            }

            if (!AudioService.IsInstanceValid(rewardInstance))
            {
                Debug.LogError("Reward instance is invalid! Check rewardAnnouncementEvent assignment in inspector");
                return;
            }

            isRewardAudioPlaying = true;
            rewardInstance.setCallback(OnRewardAudioComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER | EVENT_CALLBACK_TYPE.STOPPED);

            AudioService.SetParameter(rewardInstance, "RewardID", rewardId);
            AudioService.PlayAudio(rewardInstance, Vector3.zero);

            Debug.Log($"Battle Oak announces reward: ID {rewardId} - navigation cues paused");
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnRewardAudioComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                isRewardAudioPlaying = false;
                Debug.Log("Reward audio finished - navigation cues can resume");
            }
            return FMOD.RESULT.OK;
        }

        private void RemoveCompletedPOIs()
        {
            var poisToRemove = activePOIs.Where(poi => poi.ShouldBeRemoved).ToList();

            if (poisToRemove.Count > 0)
            {
                Debug.Log($"Removing {poisToRemove.Count} completed POIs from map");

                foreach (var poi in poisToRemove)
                {
                    poi.Cleanup();
                    activePOIs.Remove(poi);

                    if (currentLayer?.pois != null)
                    {
                        currentLayer.pois.Remove(poi);
                    }
                }

                foreach (var poi in poisToRemove)
                {
                    if (poiDataCache.ContainsKey(poi))
                    {
                        poiDataCache.Remove(poi);
                    }
                }
            }
        }

        private void UpdateDiscoveryLogic()
        {
            if (FirebaseService == null) return;

            foreach (var poi in activePOIs)
            {
                if (poi.IsDiscovered) continue;

                if (poiDataCache.TryGetValue(poi, out POIUpdateData data) &&
                    data.distance <= discoveryDistance)
                {
                    poi.SetDiscovered(true);

                    if (!discoveredThisSession.ContainsKey(poi.characterId))
                    {
                        discoveredThisSession[poi.characterId] = true;
                        AnalyticsService?.TrackEvent($"character_discovered_{poi.characterId}");

                        if (StorageService != null)
                        {
                            StorageService.Save($"Character_{poi.characterId}_Discovered", true);
                        }
                    }

                    FirebaseService.SaveDiscoveredPOI(GameManager.Instance.CurrentSessionId, poi.characterId);
                    Debug.Log($"Discovered POI: {poi.characterName} (ID: {poi.characterId})");
                }
            }
        }

        private void SetPotentialTarget(POI poi, POIUpdateData data)
        {
            targetingState = new TargetingState
            {
                mode = TargetingMode.Potential,
                targetPOI = poi,
                timer = 0f,
                angleDifference = data.angleDifference
            };

            if (targetingIndicator != null)
                targetingIndicator.SetActive(true);

            Debug.Log($"Started targeting {poi.characterName} - angle: {data.angleDifference:F1}°");
        }

        private void LockTarget()
        {
            targetingState.mode = TargetingMode.Locked;
            targetingState.targetPOI.UpdateTargetingState(true);

            // Clear standard navigation state when locking target
            ClearStandardNavigation(forcePause: false);

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = $"Locked onto {targetingState.targetPOI.characterName}";

            // Play lock sound
            if (AudioService != null && AudioService.IsInstanceValid(targetingFeedbackInstance))
            {
                AudioService.SetParameter(targetingFeedbackInstance, "LockState", 1.0f); // 1.0 = lock
                AudioService.PlayAudio(targetingFeedbackInstance, Vector3.zero);
                Debug.Log("Targeting feedback: LOCK");
            }

            AnalyticsService?.TrackEvent($"character_targeted_{targetingState.targetPOI.characterId}");

            Debug.Log($"Successfully locked onto {targetingState.targetPOI.characterName} - cleared standard navigation state");
        }

        /// <summary>
        /// Clear targeting state
        /// </summary>
        /// <param name="playUnlockSound">True if breaking lock (play unlock sound), false if auto-switching or clearing for other reasons</param>
        private void ClearTargeting(bool playUnlockSound = false)
        {
            if (targetingState.mode == TargetingMode.Locked && targetingState.targetPOI != null)
            {
                targetingState.targetPOI.UpdateTargetingState(false);
                targetingState.targetPOI.ResetNavigationCueIndex();

                // Stop the targeted POI's navigation cue
                targetingState.targetPOI.StopNavigationCue();

                // Play unlock sound only if breaking lock (not auto-switching)
                if (playUnlockSound && AudioService != null && AudioService.IsInstanceValid(targetingFeedbackInstance))
                {
                    AudioService.SetParameter(targetingFeedbackInstance, "LockState", 0.0f); // 0.0 = unlock
                    AudioService.PlayAudio(targetingFeedbackInstance, Vector3.zero);
                    Debug.Log("Targeting feedback: UNLOCK");
                }

                // Clear standard navigation with forced pause when returning from locked mode
                ClearStandardNavigation(forcePause: true);
            }

            Debug.Log($"Clearing targeting: {targetingState.targetPOI?.characterName ?? "None"} (playUnlockSound: {playUnlockSound})");

            targetingState = new TargetingState { mode = TargetingMode.None };

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = "";
        }

        /// <summary>
        /// Clear all standard navigation state and stop all navigation cue audio
        /// </summary>
        /// <param name="forcePause">If true, force a cycle pause delay before resuming</param>
        private void ClearStandardNavigation(bool forcePause = false)
        {
            Debug.Log($"ClearStandardNavigation called (forcePause: {forcePause})");

            // Stop all navigation cues
            foreach (var poi in activeCuePOIs)
            {
                poi.StopNavigationCue();
            }

            // Clear all standard navigation state
            activeCuePOIs.Clear();
            currentCueIndex = 0;
            waitingForNextCue = false;
            cueTimer = 0f;

            // Force pause if requested (used when returning from targeted mode)
            if (forcePause)
            {
                isInCyclePause = true;
                cyclePauseTimer = 0f;
                Debug.Log($" Forcing {cyclePauseDelay}s pause before standard navigation resumes");
            }
            else
            {
                isInCyclePause = false;
                cyclePauseTimer = 0f;
            }

            Debug.Log("✓ Standard navigation state cleared");
        }

        private void UpdateTargetingUI()
        {
            if (targetingText != null)
            {
                float progress = (targetingState.timer / targetLockTime) * 100f;
                targetingText.text = $"Targeting {targetingState.targetPOI.characterName}\n" +
                                   $"Progress: {progress:F0}%\n" +
                                   $"Time: {targetingState.timer:F2}s / {targetLockTime:F1}s\n" +
                                   $"Angle: {targetingState.angleDifference:F1}°";
            }
        }

        private void UpdateDebugDisplay(Vector2 location)
        {
            if (debugText == null || HeadTrackingService == null) return;

            var currentLayer = TimeLayerManager.Instance.CurrentLayer;

            if (targetingState.mode == TargetingMode.Locked)
            {
                var data = poiDataCache[targetingState.targetPOI];
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"Target: {targetingState.targetPOI.characterName}\n" +
                               $"Dist: {data.distance:F0}m | Angle: {data.angleDifference:F1}°\n" +
                               $"MaxCues: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n";
            }
            else if (targetingState.mode == TargetingMode.Potential)
            {
                float progress = (targetingState.timer / targetLockTime) * 100f;
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"Targeting: {targetingState.targetPOI.characterName}\n" +
                               $"Progress: {progress:F1}%\n" +
                               $"MaxCues: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n";
            }
            else
            {
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"POIs: {activePOIs.Count}\n" +
                               $"MaxCues: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Location: {location.x:F6}, {location.y:F6}\n";
            }
        }

        private float CalculateZoneFromDistance(float distance)
        {
            if (distance > proximityRadius)
            {
                return 0.0f;
            }
            else if (distance > dialogueRadius)
            {
                float t = 1.0f - ((distance - dialogueRadius) / (proximityRadius - dialogueRadius));
                return Mathf.Lerp(0.0f, 1.0f, t);
            }
            else
            {
                float t = distance / dialogueRadius;
                return Mathf.Lerp(2.0f, 1.0f, t);
            }
        }

        private void OnTimeLayerChanging(TimeLayer from, TimeLayer to)
        {
            Debug.Log($"POIManager: Preparing transition from {from.layerName} to {to.layerName}");

            if (GameManager.Instance?.CurrentGameplayState != GameManager.GameplayState.Wander)
            {
                Debug.Log("POIManager: Forcing wander mode before time layer transition");
                GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Wander);
            }

            AnalyticsService?.TrackEvent($"time_travel_from_{from.layerName.Replace(" ", "_").ToLower()}_to_{to.layerName.Replace(" ", "_").ToLower()}");

            ClearAllNavigationState();
            CleanupCurrentLayerPOIs();

            if (debugText != null)
            {
                debugText.text = $"Transitioning: {from.layerName} → {to.layerName}";
            }
        }

        private void OnTimeLayerChanged(TimeLayer newLayer)
        {
            Debug.Log($"POIManager: Loading {newLayer.layerName} layer");

            currentLayer = newLayer;

            discoveredThisSession.Clear();
            proximityReachedThisSession.Clear();

            LoadLayerPOIs(newLayer);

            AnalyticsService?.TrackEvent($"time_layer_loaded_{newLayer.layerName.Replace(" ", "_").ToLower()}");

            if (StorageService != null)
            {
                string travelKey = $"TimeLayer_{newLayer.layerIndex}_Visited";
                StorageService.Save(travelKey, true);
            }

            TimeLayerManager.Instance?.OnPOILayerLoadComplete();

            if (debugText != null)
            {
                debugText.text = $"Layer: {newLayer.layerName}\nPOIs: {activePOIs.Count}";
            }
        }

        private void LoadLayerPOIs(TimeLayer layer)
        {
            activePOIs.Clear();

            // Wait for JSON data to be loaded
            if (gameDataService == null || !gameDataService.IsDataLoaded)
            {
                Debug.LogWarning($"POIManager: JSON data not loaded yet for {layer.layerName} - waiting...");
                return;
            }

            Debug.Log($"POIManager: Loading POIs from JSON for {layer.layerName}");

            // Apply JSON configuration on first load
            if (gameDataService != null && gameDataService.IsDataLoaded)
            {
                Debug.Log("POIManager: Applying JSON configuration");
                ApplyJSONConfiguration();
            }

            // Load POIs from JSON
            LoadPOIsFromJSON(layer);

            InitializePOIs();
            Debug.Log($"POIManager: Loaded {activePOIs.Count} POIs for {layer.layerName}");
        }

        private void LoadPOIsFromJSON(TimeLayer layer)
        {
            if (gameDataService == null)
            {
                Debug.LogError("POIManager: GameDataService not available");
                return;
            }

            // Use layer index to get the correct JSON time layer
            var allTimeLayerData = gameDataService.GetAllTimeLayerData();

            if (layer.layerIndex >= 0 && layer.layerIndex < allTimeLayerData.Count)
            {
                var jsonTimeLayer = allTimeLayerData[layer.layerIndex];

                Debug.Log($"POIManager: Loading POIs for layer '{layer.layerName}' (Index: {layer.layerIndex}, JSON ID: {jsonTimeLayer.id})");

                var poiDataList = jsonTimeLayer.pois; // Get POIs directly from the time layer

                Debug.Log($"POIManager: Found {poiDataList.Count} POIs in JSON for layer index {layer.layerIndex}");

                int skippedCount = 0;
                int loadedCount = 0;

                foreach (var poiData in poiDataList)
                {
                    if (IsPOICompleted(poiData.characterId))
                    {
                        Debug.Log($"POIManager: Skipping completed POI - {poiData.characterName} (ID: {poiData.characterId})");
                        skippedCount++;
                        continue;
                    }

                    POI poi = CreatePOIFromJSONData(poiData);
                    if (poi != null)
                    {
                        activePOIs.Add(poi);
                        loadedCount++;
                        Debug.Log($"POIManager: ✓ Loaded POI - {poiData.characterName} (ID: {poiData.characterId})");
                    }
                }

                Debug.Log($"POIManager: Loaded {loadedCount} POIs, skipped {skippedCount} completed");
            }
            else
            {
                Debug.LogError($"POIManager: Invalid layer index {layer.layerIndex}, available layers: {allTimeLayerData.Count}");
            }
        }

        private POI CreatePOIFromJSONData(GameDataService.POIData poiData)
        {
            try
            {
                GameObject prefab = GetPrefabForCharacter(poiData.characterName, poiData.characterId);
                GameObject poiObject = Instantiate(prefab, transform);
                poiObject.name = $"JSON_Character_{poiData.characterId}_{poiData.characterName}";

                POI poi = new POI();

                poi.characterName = poiData.characterName;
                poi.characterId = poiData.characterId;
                poi.latitude = poiData.latitude;
                poi.longitude = poiData.longitude;

                // Character audio event name stored as string (loaded from bank)
                poi.characterAudioEvent = poiData.characterAudioEvent;
                poi.navigationCueEvent = poiData.navigationCueEvent;

                poi.portalType = poiData.portalType switch
                {
                    "Forward" => PortalType.Forward,
                    "Backward" => PortalType.Backward,
                    _ => PortalType.None
                };
                poi.portalJumpDistance = poiData.portalJumpDistance;

                // Only load portal audio if actually a portal
                if (poi.portalType != PortalType.None &&
                    !string.IsNullOrEmpty(poiData.portalActivationAudio) &&
                    gameDataService != null)
                {
                    poi.portalActivationAudio = gameDataService.GetAudioEventReference(poiData.portalActivationAudio);
                    Debug.Log($"POIManager: Loaded portal audio for {poi.characterName}");
                }

                // Explicit handling of reward data
                poi.hasReward = poiData.hasReward;
                if (poi.hasReward && poiData.reward != null)
                {
                    poi.rewardId = poiData.reward.id;
                    poi.rewardName = poiData.reward.name;
                    Debug.Log($"POIManager: {poi.characterName} has reward - ID: {poi.rewardId}, Name: {poi.rewardName}");
                }
                else
                {
                    poi.rewardId = 0;
                    poi.rewardName = "";
                }

                poi.hasMultipleVariants = poiData.hasMultipleVariants;
                poi.narrationVariantCount = poiData.narrationVariantCount;

                // Initialize POI from JSON data (includes maxNavigationCues)
                poi.InitializeFromData(poiData, poiObject);

                RectTransform markerTransform = poiObject.GetComponentInChildren<RectTransform>();
                if (markerTransform != null)
                {
                    poi.marker = markerTransform;
                }

                Debug.Log($"POIManager: Successfully created JSON POI - {poiData.characterName} (ID: {poiData.characterId}, Reward: {poiData.reward?.id ?? 0})");
                return poi;
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager: Failed to create POI from JSON data for {poiData.characterName}: {e.Message}");
                return null;
            }
        }

        private GameObject GetPrefabForCharacter(string characterName, string characterId)
        {
            var mapping = characterPrefabs?.Find(m =>
                m.characterName.Equals(characterName, StringComparison.OrdinalIgnoreCase) ||
                m.characterId == characterId);

            if (mapping?.prefab != null)
            {
                Debug.Log($"POIManager: Found specific prefab for {characterName} (ID: {characterId})");
                return mapping.prefab;
            }

            if (defaultPOIPrefab != null)
            {
                Debug.Log($"POIManager: Using default prefab for {characterName} (ID: {characterId})");
                return defaultPOIPrefab;
            }

            Debug.LogWarning($"POIManager: No prefab found for {characterName} (ID: {characterId}) - creating empty GameObject");
            return new GameObject($"POI_{characterName}");
        }

        private void InitializePOIs()
        {
            if (AudioService == null)
            {
                Debug.LogError("Cannot initialize POIs - AudioService not available");
                return;
            }

            var successfullyInitialized = new List<POI>();
            foreach (var poi in activePOIs)
            {
                Debug.Log($"Initializing {poi.characterName} in {currentLayer.layerName}");

                if (poi.Initialize(proximityRadius, dialogueRadius))
                {
                    if (mapManager != null && poi.marker != null)
                    {
                        Vector2 poiPosition = mapManager.GetScreenPosition(poi.latitude, poi.longitude);
                        poi.marker.anchoredPosition = poiPosition;
                    }
                    poi.SetDirectionDebugText(directionDebugText); // Pass debug text reference to POI
                    successfullyInitialized.Add(poi);
                }
            }

            activePOIs = successfullyInitialized;
            Debug.Log($"Successfully initialized {activePOIs.Count} POIs for {currentLayer.layerName}");
        }

        private void CleanupCurrentLayerPOIs()
        {
            foreach (var poi in activePOIs)
            {
                poi.Cleanup();
            }

            activePOIs.Clear();
            poiDataCache.Clear();
        }

        public void UpdateUnlockedPOIs(List<string> unlockedPOIIds)
        {
            foreach (var poi in activePOIs)
            {
                bool isUnlocked = unlockedPOIIds.Contains(poi.characterId);
                poi.SetUnlocked(isUnlocked);
            }
        }

        private bool IsPOICompleted(string characterId)
        {
            if (StorageService == null)
            {
                Debug.LogWarning("POIManager: StorageService not available for unlock check - assuming not completed");
                return false;
            }

            string characterUnlockKey = $"Character_{characterId}_Unlocked";
            bool isUnlocked = StorageService.Load<bool>(characterUnlockKey);

            return isUnlocked;
        }

        public bool HasPOIInProximity()
        {
            return activePOIs.Any(poi => poi.isInProximity);
        }

        private Vector3 CalculateAudioPosition(POI poi, float currentLat, float currentLon, float headingAngle)
        {
            float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
            float bearing = CalculateBearing(currentLat, currentLon, poi.latitude, poi.longitude);
            float relativeAngle = bearing - headingAngle;
            float angleRad = relativeAngle * Mathf.Deg2Rad;

            return new Vector3(
                distance * Mathf.Sin(angleRad),
                0,
                distance * Mathf.Cos(angleRad)
            );
        }

        private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
        {
            float earthRadius = 6371e3f;
            float lat1Rad = lat1 * Mathf.Deg2Rad;
            float lat2Rad = lat2 * Mathf.Deg2Rad;
            float latDiff = (lat2 - lat1) * Mathf.Deg2Rad;
            float lonDiff = (lon2 - lon1) * Mathf.Deg2Rad;
            float a = Mathf.Sin(latDiff / 2) * Mathf.Sin(latDiff / 2) +
                     Mathf.Cos(lat1Rad) * Mathf.Cos(lat2Rad) *
                     Mathf.Sin(lonDiff / 2) * Mathf.Sin(lonDiff / 2);
            float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
            return earthRadius * c;
        }

        private float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            var dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            var lat1Rad = lat1 * Mathf.Deg2Rad;
            var lat2Rad = lat2 * Mathf.Deg2Rad;
            var y = Mathf.Sin(dLon) * Mathf.Cos(lat2Rad);
            var x = Mathf.Cos(lat1Rad) * Mathf.Sin(lat2Rad) -
                    Mathf.Sin(lat1Rad) * Mathf.Cos(lat2Rad) * Mathf.Cos(dLon);
            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        [ContextMenu("Debug Targeting Status")]
        public void DebugTargetingStatus()
        {
            Debug.Log($"=== POI Targeting Debug ===");
            Debug.Log($"Targeting Mode: {targetingState.mode}");
            Debug.Log($"Target POI: {targetingState.targetPOI?.characterName ?? "None"}");
            Debug.Log($"Targeting Timer: {targetingState.timer:F2}s");
            Debug.Log($"Angle Difference: {targetingState.angleDifference:F1}°");
            Debug.Log($"Target Lock Time: {targetLockTime}s");
            Debug.Log($"Active POIs: {activePOIs.Count}");
            Debug.Log($"POI Data Cache: {poiDataCache.Count} entries");
        }

        [ContextMenu("Debug POI Distances")]
        public void DebugPOIDistances()
        {
            Debug.Log($"=== POI Distance Debug ===");
            foreach (var entry in poiDataCache)
            {
                var poi = entry.Key;
                var data = entry.Value;
                Debug.Log($"{poi.characterName} (ID: {poi.characterId}): {data.distance:F1}m, Angle: {data.angleDifference:F1}°, Reward: {poi.rewardId}");
            }
        }

        public ProgressionInfo GetProgressionInfo()
        {
            return new ProgressionInfo
            {
                completedPOIs = totalCompletedPOIs,
                currentMaxCues = currentMaxActiveCues,
                nextThreshold = completionsToIncrease,
                maxPossible = maxMaxActiveCues,
                progressToNext = totalCompletedPOIs < completionsToIncrease ?
                    (float)totalCompletedPOIs / completionsToIncrease : 1.0f
            };
        }

        [ContextMenu("Reset Progression")]
        public void ResetProgression()
        {
            totalCompletedPOIs = 0;
            currentMaxActiveCues = baseMaxActiveCues;
            Debug.Log($"Progression reset. Max cues: {currentMaxActiveCues}");
        }

        [ContextMenu("Debug Data Source")]
        public void DebugDataSource()
        {
            Debug.Log($"=== POI Manager Data Source Debug ===");
            Debug.Log($"GameDataService Available: {gameDataService != null}");
            Debug.Log($"GameDataService Data Loaded: {gameDataService?.IsDataLoaded ?? false}");
            Debug.Log($"Current Layer: {currentLayer?.layerName ?? "None"}");
            Debug.Log($"Active POIs: {activePOIs.Count}");

            if (activePOIs.Count > 0)
            {
                Debug.Log("Active POI Details:");
                foreach (var poi in activePOIs.Take(5))
                {
                    Debug.Log($"  - {poi.characterName} (ID: {poi.characterId}) Reward: {poi.rewardId}");
                }
            }
        }

        public void CompleteReset()
        {
            Debug.Log("POIManager: COMPLETE RESET - destroying all state");

            // Stop all audio
            StopAllAudio();

            // Clear all collections
            CleanupCurrentLayerPOIs();
            ClearAllNavigationState();

            // Reset all flags
            isInitialized = false;

            // Clear progression (site-specific)
            totalCompletedPOIs = 0;
            currentMaxActiveCues = baseMaxActiveCues;

            // Clear session tracking
            discoveredThisSession.Clear();
            proximityReachedThisSession.Clear();

            // Unsubscribe from events (will resubscribe when site loads)
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanging -= OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            Debug.Log("POIManager: Complete reset finished");
        }

        /// <summary>
        /// Stop all POI audio - called when exiting gameplay
        /// </summary>
        public void StopAllAudio()
        {
            Debug.Log("POIManager: Stopping all POI audio");

            // Stop all individual POI audio
            foreach (var poi in activePOIs)
            {
                if (poi.characterAudioInstance.handle != IntPtr.Zero && AudioService != null)
                {
                    AudioService.StopAudio(poi.characterAudioInstance, false); // Immediate stop
                    Debug.Log($"POIManager: Stopped character audio for {poi.characterName}");
                }

                // NEW: Stop individual navigation cue instances
                if (AudioService != null && AudioService.IsInstanceValid(poi.navigationCueInstance))
                {
                    AudioService.StopAudio(poi.navigationCueInstance, false);
                    Debug.Log($"POIManager: Stopped navigation cue for {poi.characterName}");
                }
            }

            // Stop reward and welcome audio
            if (AudioService != null)
            {
                if (AudioService.IsInstanceValid(rewardInstance))
                {
                    AudioService.StopAudio(rewardInstance, false);
                }

                if (AudioService.IsInstanceValid(welcomeInstance))
                {
                    AudioService.StopAudio(welcomeInstance, false);
                }
            }

            Debug.Log("POIManager: All POI audio stopped");
        }

        private void OnDestroy()
        {
            // Unsubscribe from SiteManager
            if (SiteManager.Instance != null)
            {
                SiteManager.Instance.OnSiteLoaded -= OnSiteLoaded;
            }

            // Existing cleanup
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanging -= OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            CleanupCurrentLayerPOIs();

            if (AudioService != null)
            {
                if (rewardInstance.handle != IntPtr.Zero)
                {
                    AudioService.StopAudio(rewardInstance, true);
                    AudioService.ReleaseAudio(rewardInstance);
                }

                if (targetingFeedbackInstance.handle != IntPtr.Zero)
                {
                    AudioService.StopAudio(targetingFeedbackInstance, false);
                    AudioService.ReleaseAudio(targetingFeedbackInstance);
                }
            }
        }
    }
}