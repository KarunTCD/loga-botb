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
        [SerializeField] private TextMeshProUGUI tutorialDebugText;

        [Header("POI Prefabs")]
        [SerializeField] private GameObject defaultPOIPrefab;
        [SerializeField] private List<CharacterPrefabMapping> characterPrefabs;

        private float proximityRadius;
        private float dialogueRadius;
        private float maxCueRadius;
        private float discoveryDistance;
        private float cueStagingDelay;
        private float targetedCueStagingDelay;
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

        // Tutorial event reporting
        public event Action<POI> TutorialPOIProximityEntered;
        public event Action<POI> TutorialPOIProximityExited;
        public event Action<POI> TutorialPOIInnerZoneEntered;
        public event Action<POI> TutorialPOINarrationComplete;
        public event Action<POI> TutorialPOITargetLocked;
        public event Action<POI> TutorialPOITargetUnlocked;
        public event Action<POI, float> TutorialPOIProgressMade;

        private IGameDataService gameDataService;

        private TimeLayer currentLayer;
        public List<POI> activePOIs = new List<POI>();
        public Dictionary<POI, POIUpdateData> poiDataCache = new Dictionary<POI, POIUpdateData>();

        private List<POI> activeCuePOIs = new List<POI>();
        private bool isCycleLocked = false;
        public TargetingState targetingState = new TargetingState { mode = TargetingMode.None };

        private EventReference welcomeGreetingEvent;
        private EventReference rewardAnnouncementEvent;
        private EventReference targetingFeedbackEvent;
        private EventInstance welcomeInstance;
        private EventInstance rewardInstance;
        private EventInstance targetingFeedbackInstance;

        private bool isInitialized = false;
        private int totalCompletedPOIs;
        private POI lastProximityPOI;
        private int currentMaxActiveCues;
        private static bool isRewardAudioPlaying = false;

        private float cueTimer = 0f;
        private int currentCueIndex = 0;
        private bool isInCyclePause = false;
        private float cyclePauseTimer = 0f;
        private int updateFrameCounter = 0;
        private bool waitingForNextCue = false;
        private bool justLockedTarget = false;

        private Dictionary<string, bool> discoveredThisSession = new Dictionary<string, bool>();
        private Dictionary<string, bool> proximityReachedThisSession = new Dictionary<string, bool>();

        private IStorageService storageService;
        private IAudioService audioService;
        private ILocationService locationService;
        private IHeadTrackingService headTrackingService;
        private IFirebaseService firebaseService;
        private IAnalyticsService analyticsService;

        public int CurrentMaxActiveCues => currentMaxActiveCues;
        public static POIManager Instance { get; private set; }

        // Tutorial mode state
        private bool isTutorialMode = false;
        private POI tutorialPOI = null;
        private float tutorialPOIDistanceWhenLocked = 0f;
        private const float TUTORIAL_PROGRESS_THRESHOLD = 15f;
        private bool tutorialInnerZoneTriggered = false;

        private bool isNavigationSoundsEnabled = false;
        private const string NAVIGATION_SOUNDS_KEY = "Setting_TargetingSounds";

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

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("Multiple POIManager instances detected!");
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

                LoadProgressionData();

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

                audioService = await ServiceLocator.GetInitializedService<IAudioService>();
                if (audioService == null)
                {
                    Debug.LogError("POIManager: AudioService failed to initialize");
                    return;
                }

                bool audioInitialized = false;
                try
                {
                    audioInitialized = InitializeAudioComponents();
                }
                catch (Exception audioEx)
                {
                    Debug.LogError($"POIManager: Audio initialization failed: {audioEx.Message} - continuing without audio");
                    audioInitialized = false;
                }

                if (!audioInitialized)
                {
                    Debug.LogWarning("POIManager: Audio components failed to initialize - POIs will load without audio");
                }

                TimeLayerManager.Instance.TimeLayerChanging += OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;

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
            targetedCueStagingDelay = config.targetedCueStagingDelay;
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
            Debug.Log($"  - Cue Delays: Wander={cueStagingDelay}s, Targeted={targetedCueStagingDelay}s");
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

            isNavigationSoundsEnabled = StorageService.Load<bool>(NAVIGATION_SOUNDS_KEY, false);
            Debug.Log($"POIManager: Targeting sounds enabled: {isNavigationSoundsEnabled}");

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

            var config = gameDataService?.GameConfig;
            if (config == null)
            {
                Debug.LogWarning("POIManager: No JSON config available - skipping audio initialization");
                return true;
            }

            if (!string.IsNullOrEmpty(config.rewardAnnouncementEvent))
            {
                rewardAnnouncementEvent = gameDataService.GetAudioEventReference(config.rewardAnnouncementEvent);
                if (!rewardAnnouncementEvent.IsNull)
                {
                    rewardInstance = AudioService.CreateAudioInstance(rewardAnnouncementEvent);
                    if (rewardInstance.handle == IntPtr.Zero)
                        Debug.LogWarning("Failed to create reward instance - continuing without reward audio");
                    else
                        Debug.Log("Reward instance created from JSON");
                }
            }
            else
            {
                Debug.Log("POIManager: No reward announcement event in JSON - skipping");
            }

            if (!string.IsNullOrEmpty(config.welcomeGreetingEvent))
            {
                welcomeGreetingEvent = gameDataService.GetAudioEventReference(config.welcomeGreetingEvent);
                if (!welcomeGreetingEvent.IsNull)
                {
                    welcomeInstance = AudioService.CreateAudioInstance(welcomeGreetingEvent);
                    if (welcomeInstance.handle == IntPtr.Zero)
                        Debug.LogWarning("Failed to create welcome instance - continuing without welcome audio");
                    else
                        Debug.Log("Welcome instance created from JSON");
                }
            }
            else
            {
                Debug.Log("POIManager: No welcome greeting event in JSON - skipping");
            }

            if (!string.IsNullOrEmpty(config.targetingFeedbackSound))
            {
                targetingFeedbackEvent = gameDataService.GetAudioEventReference(config.targetingFeedbackSound);
                if (!targetingFeedbackEvent.IsNull)
                {
                    targetingFeedbackInstance = AudioService.CreateAudioInstance(targetingFeedbackEvent);
                    if (targetingFeedbackInstance.handle == IntPtr.Zero)
                        Debug.LogWarning("Failed to create targeting feedback instance - continuing without targeting audio");
                    else
                        Debug.Log("POIManager: Targeting feedback instance created from JSON");
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

        // ── CHANGE 4: Targeting feedback queue ───────────────────────────────

        /// <summary>
        /// Play targeting feedback sound. If the event is already playing,
        /// waits for it to finish before applying the new parameter and playing.
        /// Prevents mid-track parameter jumps when lock/unlock fire in quick succession.
        /// </summary>
        private void PlayTargetingFeedback(float lockState)
        {
            if (!isNavigationSoundsEnabled) return;
            if (AudioService == null || !AudioService.IsInstanceValid(targetingFeedbackInstance)) return;

            // If already playing, skip — do not queue, do not wait
            // A skipped duplicate is less harmful than coroutine interference
            targetingFeedbackInstance.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.PLAYING || state == PLAYBACK_STATE.STARTING) return;

            AudioService.SetParameter(targetingFeedbackInstance, "LockState", lockState);
            AudioService.PlayAudio(targetingFeedbackInstance, Vector3.zero);
            Debug.Log($"Targeting feedback: {(lockState >= 1f ? "LOCK" : "UNLOCK")}");
        }

        /// <summary>
        /// Called by SettingsUI when the targeting sounds toggle changes.
        /// </summary>
        public void SetNavigationSoundsEnabled(bool enabled)
        {
            isNavigationSoundsEnabled = enabled;
            StorageService?.Save(NAVIGATION_SOUNDS_KEY, enabled);
            Debug.Log($"POIManager: Targeting sounds {(enabled ? "enabled" : "disabled")}");
        }

        // ─────────────────────────────────────────────────────────────────────

        public void PlayWelcomeGreeting()
        {
            if (!isInitialized || StorageService == null || AudioService == null) return;

            bool hasPlayedWelcome = StorageService.Load<bool>("HasPlayedWelcomeDialogue");
            if (!hasPlayedWelcome && AudioService.IsInstanceValid(welcomeInstance))
            {
                GameManager.Instance?.SuspendGameplay(GameManager.SuspensionReason.Loading);
                welcomeInstance.setCallback(OnWelcomeComplete, EVENT_CALLBACK_TYPE.STOPPED);
                AudioService.PlayAudio(welcomeInstance, Vector3.zero);
                StorageService.Save("HasPlayedWelcomeDialogue", true);
                AnalyticsService?.TrackEvent("welcome_greeting_played");
                Debug.Log("Welcome greeting started - navigation suspended");
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnWelcomeComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.STOPPED)
            {
                GameManager.Instance?.ResumeGameplay(GameManager.SuspensionReason.Loading);
                POIManager.Instance?.ClearAllNavigationState();
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
                        Debug.Log($"  POI '{poi.characterName}' - Distance: {data.distance:F1}m, InProximity: {poi.isInProximity}, Bearing: {data.bearing:F1}°");
                    else
                        Debug.Log($"  POI '{poi.characterName}' - NO DATA IN CACHE");
                }
            }

            Debug.Log($"Targeting mode: {targetingState.mode}");
            Debug.Log($"Target POI: {targetingState.targetPOI?.characterName ?? "None"}");
            Debug.Log("========================");
        }

        private void Update()
        {
            if (!isInitialized) return;

            if (GameManager.Instance == null || GameManager.Instance.CurrentGameState != GameManager.GameState.Running)
                return;

            if (TimeLayerManager.Instance.IsTransitioning)
                return;

            var currentMode = GameManager.Instance?.CurrentMode;
            if (currentMode != GameManager.GameMode.Player &&
                currentMode != GameManager.GameMode.Tutorial)
                return;

            if (LocationService == null) return;

            Vector2 currentLocation = LocationService.GetCurrentLocation();
            if (currentLocation == Vector2.zero) return;

            updateFrameCounter++;

            UpdatePOIDataCache(currentLocation.x, currentLocation.y);
            UpdatePOIProximity();

            bool shouldUpdateNavigation = GameManager.Instance.CurrentGameplayState == GameManager.GameplayState.Wander;

            if (isTutorialMode)
                shouldUpdateNavigation = tutorialPOI == null || !tutorialPOI.isInProximity;

            if (shouldUpdateNavigation)
                UpdateNavigationAndTargeting(currentLocation.x, currentLocation.y);

            CheckNarrationCompletions();

            if (!activePOIs.Any(poi => poi.isInProximity))
                RemoveCompletedPOIs();

            if (updateFrameCounter % 60 == 0)
            {
                UpdateDiscoveryLogic();
                CheckTutorialProgress();
            }
        }

        public void SilenceAllPOIAudio()
        {
            foreach (var poi in activePOIs)
            {
                if (poi.isInProximity)
                    poi.SilenceAudio();
            }
        }

        public void ResumeAllPOIAudio()
        {
            foreach (var poi in activePOIs)
            {
                if (poi.isInProximity && poiDataCache.TryGetValue(poi, out POIUpdateData data))
                    poi.ResumeAudio(data.audioPosition);
            }
        }

        public void ClearAllNavigationState()
        {
            ClearStandardNavigation(forcePause: false);
            ClearTargeting(false);
            foreach (var poi in activePOIs)
                poi.ClearDirectionDebug();
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
                if (isTutorialMode && poi != tutorialPOI)
                    continue;

                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    float zoneValue = CalculateZoneFromDistance(data.distance);
                    bool wasInProximity = poi.isInProximity;

                    poi.UpdateProximity(data, zoneValue);

                    if (isTutorialMode && poi == tutorialPOI)
                    {
                        if (poi.isInProximity && !wasInProximity)
                        {
                            Debug.Log("POIManager: Tutorial POI entered proximity - firing event");
                            TutorialPOIProximityEntered?.Invoke(poi);
                        }
                        else if (!poi.isInProximity && wasInProximity)
                        {
                            Debug.Log("POIManager: Tutorial POI exited proximity - firing event");
                            TutorialPOIProximityExited?.Invoke(poi);
                        }

                        if (poi.isInProximity && !tutorialInnerZoneTriggered && zoneValue >= 1.0f)
                        {
                            Debug.Log($"POIManager: Tutorial POI inner zone entered (Zone: {zoneValue:F2}) - firing event");
                            TutorialPOIInnerZoneEntered?.Invoke(poi);
                            tutorialInnerZoneTriggered = true;
                        }
                    }

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
                zoneText.text = "Zone: 0 (No proximity)";
        }

        private void UpdateNavigationAndTargeting(float currentLat, float currentLon)
        {
            if (activePOIs.Count == 0) return;
            if (isRewardAudioPlaying) return;

            POI proximityPOI = null;

            if (isTutorialMode)
            {
                if (tutorialPOI != null && poiDataCache.ContainsKey(tutorialPOI))
                {
                    if (poiDataCache[tutorialPOI].distance <= proximityRadius)
                        proximityPOI = tutorialPOI;
                }
            }
            else
            {
                proximityPOI = poiDataCache
                    .Where(p => p.Value.distance <= proximityRadius)
                    .OrderBy(p => p.Value.distance)
                    .Select(p => p.Key)
                    .FirstOrDefault();
            }

            if (proximityPOI != null)
            {
                if (proximityPOI != lastProximityPOI)
                {
                    if (isTutorialMode && Time.frameCount % 60 == 0)
                        Debug.Log($"[NAV-CLEAR] Proximity POI detected (NEW): {proximityPOI.characterName}, clearing nav state");

                    ClearAllNavigationState();
                    lastProximityPOI = proximityPOI;
                }
                return;
            }

            if (lastProximityPOI != null && proximityPOI == null)
            {
                if (isTutorialMode && Time.frameCount % 60 == 0)
                    Debug.Log($"[NAV-CLEAR] Exited proximity from {lastProximityPOI.characterName}");
                lastProximityPOI = null;
            }

            var eligiblePOIs = GetEligibleNavigationPOIs();

            if (eligiblePOIs.Count == 0)
            {
                if (isTutorialMode && Time.frameCount % 60 == 0)
                    Debug.LogWarning($"[NAV-CLEAR] No eligible POIs! Cache count: {poiDataCache.Count}");

                ClearAllNavigationState();
                return;
            }

            UpdateTargetingLogic(eligiblePOIs);

            if (targetingState.mode == TargetingMode.Locked)
                HandleTargetedNavigation();
            else
                HandleStandardNavigation(eligiblePOIs);
        }

        private List<POI> GetEligibleNavigationPOIs()
        {
            var eligible = poiDataCache
                .Where(p => p.Value.distance > proximityRadius && p.Value.distance <= maxCueRadius);

            if (isTutorialMode)
                eligible = eligible.Where(p => p.Key == tutorialPOI);

            return eligible
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
                    LockTarget();
            }
            else
            {
                ClearTargeting(false);
            }
        }

        private void UpdateLockedTargeting(List<POI> eligiblePOIs)
        {
            if (!eligiblePOIs.Contains(targetingState.targetPOI))
            {
                ClearTargeting(true);
                return;
            }

            if (poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData currentTargetData))
            {
                if (currentTargetData.angleDifference > targetBreakAngle)
                {
                    ClearTargeting(true);
                    return;
                }

                CheckForAutoSwitch(eligiblePOIs, currentTargetData);
                targetingState.angleDifference = currentTargetData.angleDifference;
            }
        }

        private void CheckForAutoSwitch(List<POI> eligiblePOIs, POIUpdateData currentTargetData)
        {
            foreach (var poi in eligiblePOIs)
            {
                if (poi == targetingState.targetPOI) continue;

                if (poiDataCache.TryGetValue(poi, out POIUpdateData poiData))
                {
                    if (poiData.distance < currentTargetData.distance &&
                        poiData.angleDifference <= targetLockAngle)
                    {
                        Debug.Log($"Auto-switching from {targetingState.targetPOI.characterName} ({currentTargetData.distance:F1}m) to {poi.characterName} ({poiData.distance:F1}m)");

                        targetingState.targetPOI.UpdateTargetingState(false);
                        targetingState.targetPOI.ResetNavigationCueIndex();
                        targetingState.targetPOI.StopNavigationCue();

                        targetingState.targetPOI = poi;
                        targetingState.angleDifference = poiData.angleDifference;
                        poi.UpdateTargetingState(true);

                        // ── replaced with queued play ─────────────────────────
                        PlayTargetingFeedback(1.0f);

                        if (targetingText != null)
                            targetingText.text = $"Switched to {poi.characterName}";

                        AnalyticsService?.TrackEvent($"character_auto_switched_{poi.characterId}");
                        return;
                    }
                }
            }
        }

        private void HandleTargetedNavigation()
        {
            var targetPOI = targetingState.targetPOI;
            if (!poiDataCache.TryGetValue(targetPOI, out POIUpdateData data)) return;

            if (justLockedTarget && waitingForNextCue)
            {
                cueTimer += Time.deltaTime;
                if (cueTimer >= cyclePauseDelay)
                {
                    Debug.Log($"Initial lock delay complete ({cyclePauseDelay}s) - ready for first cue");
                    justLockedTarget = false;
                    waitingForNextCue = false;
                    cueTimer = 0f;
                }
                return;
            }

            if (targetPOI.CheckNavigationCueCompletion())
            {
                Debug.Log($"Targeted cue completed via command instrument - starting {targetedCueStagingDelay}s delay");
                cueTimer = 0f;
                waitingForNextCue = true;
                return;
            }

            if (waitingForNextCue)
            {
                cueTimer += Time.deltaTime;
                if (cueTimer >= targetedCueStagingDelay)
                {
                    Debug.Log($"Targeted delay complete - executing next cue");

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
                    Debug.Log($"TARGETED SEQUENTIAL cue executed: index {sequentialCueIndex}");
                }
                return;
            }

            if (!targetPOI.IsWaitingForCueCompletion() && !waitingForNextCue)
            {
                Debug.Log($"Starting first targeted cue");

                int sequentialCueIndex = targetPOI.GetNextNavigationCueIndex();
                var config = new NavigationCueConfig
                {
                    cueType = NavigationCueType.Sequential,
                    cueIndex = sequentialCueIndex,
                    maxDistance = maxTargetingDistance,
                    isTargeted = true
                };

                targetPOI.ExecuteNavigationCue(data.audioPosition, config);
                Debug.Log($"TARGETED SEQUENTIAL cue started: index {sequentialCueIndex}");
            }
        }

        private void HandleStandardNavigation(List<POI> eligiblePOIs)
        {
            if (!isCycleLocked || activeCuePOIs.Count == 0)
            {
                bool listChanged = activeCuePOIs.Count != eligiblePOIs.Count ||
                                   !activeCuePOIs.SequenceEqual(eligiblePOIs);

                if (listChanged && eligiblePOIs.Count > 0)
                {
                    activeCuePOIs = new List<POI>(eligiblePOIs);
                    isCycleLocked = true;
                    Debug.Log($"Navigation cycle LOCKED with {activeCuePOIs.Count} POIs: {string.Join(", ", activeCuePOIs.Select(p => p.characterName))}");
                }
                else if (eligiblePOIs.Count == 0)
                {
                    activeCuePOIs.Clear();
                    isCycleLocked = false;
                }
            }

            if (activeCuePOIs.Count == 0) return;

            if (!isInCyclePause)
            {
                bool anyCompleted = false;
                foreach (var poi in activeCuePOIs)
                {
                    if (poi.CheckNavigationCueCompletion())
                    {
                        if (currentCueIndex >= activeCuePOIs.Count)
                        {
                            isInCyclePause = true;
                            cyclePauseTimer = 0f;
                            currentCueIndex = 0;
                            waitingForNextCue = false;
                            isCycleLocked = false;
                            Debug.Log($"Navigation cycle complete - UNLOCKED - entering {cyclePauseDelay}s pause");
                        }
                        else
                        {
                            cueTimer = 0f;
                            waitingForNextCue = true;
                            Debug.Log($"Standard cue completed via command instrument for {poi.characterName} - starting {cueStagingDelay}s delay");
                        }
                        anyCompleted = true;
                        break;
                    }
                }

                if (anyCompleted) return;
            }

            if (waitingForNextCue)
            {
                cueTimer += Time.deltaTime;
                if (cueTimer >= cueStagingDelay)
                {
                    waitingForNextCue = false;
                    cueTimer = 0f;
                    Debug.Log($"Gap complete - ready for next POI (list still LOCKED)");
                }
                return;
            }

            if (isInCyclePause)
            {
                cyclePauseTimer += Time.deltaTime;
                if (cyclePauseTimer >= cyclePauseDelay)
                {
                    isInCyclePause = false;
                    cyclePauseTimer = 0f;
                    currentCueIndex = 0;
                    cueTimer = 0f;
                    isCycleLocked = false;
                    Debug.Log("Exiting cycle pause - list will refresh next frame");
                }
                return;
            }

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
                    waitingForNextCue = true;
                    Debug.Log($"Standard cue executed for {poi.characterName} - POI {currentCueIndex}/{activeCuePOIs.Count} [CYCLE LOCKED]");
                }
            }
        }

        public void ScheduleDelayedCompletion(POI poi, float delay)
        {
            StartCoroutine(DelayedCompletionCoroutine(poi, delay));
        }

        private IEnumerator DelayedCompletionCoroutine(POI poi, float delay)
        {
            float currentZone = 0f;
            if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                currentZone = CalculateZoneFromDistance(data.distance);

            Debug.Log($"[POIManager] Marker hit for {poi.characterName} - Zone: {currentZone:F2}");

            if (currentZone >= 1.0f)
            {
                yield return new WaitForSeconds(delay);
                Debug.Log($"[POIManager] {poi.characterName} narration complete after {delay}s delay");
            }
            else
            {
                Debug.Log($"[POIManager] Player in music-only zone (Zone < 1.0) - NO DELAY, completing immediately");
            }

            poi.ClearMarkerPendingFlag();
            POI.narrationJustCompleted = true;
            POI.completedInstanceHandle = poi.characterAudioInstance.handle;
        }

        private void CheckNarrationCompletions()
        {
            var poisToCheck = activePOIs.ToList();
            foreach (var poi in poisToCheck)
            {
                if (poi.CheckNarrationCompletion())
                    OnPOINarrationComplete(poi);
            }
        }

        private void OnPOINarrationComplete(POI poi)
        {
            Debug.Log($"NARRATION COMPLETE: {poi.characterName} has finished their dialogue!");

            AddCharacterToInventory(poi);

            if (poi.IsPortal)
            {
                Debug.Log($"Portal {poi.characterName} activated - triggering time travel");
                poi.TriggerPortalActivation();
                Debug.Log($"POIManager: Removing portal POI {poi.characterName} after activation");
                poi.Cleanup();
                activePOIs.Remove(poi);
                if (poiDataCache.ContainsKey(poi)) poiDataCache.Remove(poi);
                AnalyticsService?.TrackEvent($"portal_used_{poi.characterId}");
                return;
            }

            if (isTutorialMode && poi == tutorialPOI)
            {
                Debug.Log($"Tutorial POI {poi.characterName} narration complete - keeping active");
                TutorialPOINarrationComplete?.Invoke(poi);

                if (poi.hasReward && poi.rewardId > 0)
                    HandlePOIReward(poi);

                Debug.Log($"POIManager: Removing tutorial POI {poi.characterName}");
                poi.Cleanup();
                activePOIs.Remove(poi);
                if (poiDataCache.ContainsKey(poi)) poiDataCache.Remove(poi);
                tutorialPOI = null;
                return;
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
                HandlePOIReward(poi);

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

        private void AddCharacterToInventory(POI poi)
        {
            if (InventoryManager.Instance == null)
            {
                Debug.LogWarning("POIManager: InventoryManager not available");
                return;
            }

            int characterItemId = poi.characterId.GetHashCode();
            if (characterItemId < 0) characterItemId = -characterItemId;

            var characterItem = new InventoryItem
            {
                itemId = characterItemId,
                name = poi.characterName,
                description = $"Met in {currentLayer.layerName}",
                type = ItemType.Character,
                audioClip = poi.characterAudioEvent,
                sourceTimeLayer = currentLayer.layerName,
                sourceCharacterId = poi.characterId,
                isNew = true
            };

            InventoryManager.Instance.AddItem(characterItem);
            Debug.Log($"POIManager: Added character - {poi.characterName} (ID: {characterItemId})");
        }

        private void HandlePOIReward(POI poi)
        {
            Debug.Log($"Starting reward handling for {poi.characterName} (Reward ID: {poi.rewardId})");

            if (StorageService != null)
            {
                string unlockKey = $"reward_{poi.rewardId}_collected";
                StorageService.Save(unlockKey, true);
                Debug.Log($"Unlocked reward: {unlockKey}");

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

        private int GetCharacterItemId(string characterId)
        {
            return characterId.GetHashCode();
        }

        private void CheckGameCompletion()
        {
            if (StorageService == null || gameDataService == null) return;

            int totalNonPortalPOIs = GetTotalPOICountFromJSON();
            if (totalNonPortalPOIs <= 0) return;

            var allTimeLayerData = gameDataService.GetAllTimeLayerData();
            int unlockedCount = 0;

            foreach (var layer in allTimeLayerData)
            {
                foreach (var poi in layer.pois)
                {
                    bool isPortal = !string.IsNullOrEmpty(poi.portalType) && poi.portalType != "None";
                    if (!isPortal && StorageService.Load<bool>($"Character_{poi.characterId}_Unlocked"))
                        unlockedCount++;
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
                    foreach (var poi in layer.pois)
                    {
                        bool isPortal = !string.IsNullOrEmpty(poi.portalType) && poi.portalType != "None";
                        if (!isPortal) totalCount++;
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
                newMax = maxMaxActiveCues;

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
                Debug.LogError("Reward instance is invalid!");
                return;
            }

            isRewardAudioPlaying = true;
            rewardInstance.setCallback(OnRewardAudioComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER | EVENT_CALLBACK_TYPE.STOPPED);
            AudioService.SetParameter(rewardInstance, "RewardID", rewardId);
            AudioService.PlayAudio(rewardInstance, Vector3.zero);
            Debug.Log($"Reward Announcement: ID {rewardId} - navigation cues paused");
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
                    if (currentLayer?.pois != null) currentLayer.pois.Remove(poi);
                }

                foreach (var poi in poisToRemove)
                {
                    if (poiDataCache.ContainsKey(poi)) poiDataCache.Remove(poi);
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
                            StorageService.Save($"Character_{poi.characterId}_Discovered", true);
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

            if (isTutorialMode && targetingState.targetPOI == tutorialPOI)
            {
                if (poiDataCache.TryGetValue(tutorialPOI, out POIUpdateData data))
                {
                    tutorialPOIDistanceWhenLocked = data.distance;
                    Debug.Log($"POIManager: Recorded tutorial POI lock distance: {tutorialPOIDistanceWhenLocked:F1}m");
                }
            }

            ClearStandardNavigation(forcePause: false);

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = $"Locked onto {targetingState.targetPOI.characterName}";

            // ── replaced with queued play ─────────────────────────────────────
            PlayTargetingFeedback(1.0f);

            justLockedTarget = true;
            cueTimer = 0f;
            waitingForNextCue = true;

            AnalyticsService?.TrackEvent($"character_targeted_{targetingState.targetPOI.characterId}");
            Debug.Log($"Successfully locked onto {targetingState.targetPOI.characterName}");

            if (isTutorialMode && targetingState.targetPOI == tutorialPOI)
            {
                Debug.Log("POIManager: Tutorial POI locked - firing event");
                TutorialPOITargetLocked?.Invoke(targetingState.targetPOI);
            }
        }

        private void ClearTargeting(bool playUnlockSound = false)
        {
            if (targetingState.mode == TargetingMode.Locked && targetingState.targetPOI != null)
            {
                POI previousTarget = targetingState.targetPOI;

                targetingState.targetPOI.UpdateTargetingState(false);
                targetingState.targetPOI.ResetNavigationCueIndex();
                targetingState.targetPOI.StopNavigationCue();

                if (playUnlockSound)
                    PlayTargetingFeedback(0.0f); 

                ClearStandardNavigation(forcePause: true);

                if (playUnlockSound && isTutorialMode && previousTarget == tutorialPOI)
                {
                    Debug.Log("POIManager: Tutorial POI unlocked - firing event");
                    TutorialPOITargetUnlocked?.Invoke(previousTarget);
                }
            }

            Debug.Log($"Clearing targeting: {targetingState.targetPOI?.characterName ?? "None"} (playUnlockSound: {playUnlockSound})");

            targetingState = new TargetingState { mode = TargetingMode.None };
            justLockedTarget = false;

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = "";
        }

        private void ClearStandardNavigation(bool forcePause = false)
        {
            foreach (var poi in activeCuePOIs)
                poi.StopNavigationCue();

            activeCuePOIs.Clear();
            currentCueIndex = 0;
            waitingForNextCue = false;
            cueTimer = 0f;
            isCycleLocked = false;

            if (forcePause)
            {
                isInCyclePause = true;
                cyclePauseTimer = 0f;
                Debug.Log($"Forcing {cyclePauseDelay}s pause before standard navigation resumes");
            }
            else
            {
                isInCyclePause = false;
                cyclePauseTimer = 0f;
            }
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
            {
                if (isTutorialMode && tutorialPOI != null && poiDataCache.TryGetValue(tutorialPOI, out POIUpdateData tutData))
                {
                    tutorialDebugText.text = $"TUTORIAL MODE\n" +
                                             $"Distance to Tutorial POI: {tutData.distance:F1}m\n" +
                                             $"Proximity Radius: {proximityRadius:F1}m\n" +
                                             $"Zone: {CalculateZoneFromDistance(tutData.distance):F2}\n" +
                                             $"In Proximity: {tutorialPOI.isInProximity}\n" +
                                             $"Player: ({location.x:F6}, {location.y:F6})\n" +
                                             $"POI: ({tutorialPOI.latitude:F6}, {tutorialPOI.longitude:F6})\n" +
                                             $"Head: {HeadTrackingService.CurrentHeading:F0}°";
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
        }

        private float CalculateZoneFromDistance(float distance)
        {
            if (distance > proximityRadius)
                return 0.0f;
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
                debugText.text = $"Transitioning: {from.layerName} → {to.layerName}";
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
                StorageService.Save($"TimeLayer_{newLayer.layerIndex}_Visited", true);

            TimeLayerManager.Instance?.OnPOILayerLoadComplete();

            if (debugText != null)
                debugText.text = $"Layer: {newLayer.layerName}\nPOIs: {activePOIs.Count}";
        }

        private void LoadLayerPOIs(TimeLayer layer)
        {
            activePOIs.Clear();

            if (gameDataService == null || !gameDataService.IsDataLoaded)
            {
                Debug.LogWarning($"POIManager: JSON data not loaded yet for {layer.layerName} - waiting...");
                return;
            }

            Debug.Log($"POIManager: Loading POIs from JSON for {layer.layerName}");

            if (gameDataService != null && gameDataService.IsDataLoaded)
            {
                Debug.Log("POIManager: Applying JSON configuration");
                ApplyJSONConfiguration();
            }

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

            var allTimeLayerData = gameDataService.GetAllTimeLayerData();

            if (layer.layerIndex >= 0 && layer.layerIndex < allTimeLayerData.Count)
            {
                var jsonTimeLayer = allTimeLayerData[layer.layerIndex];
                Debug.Log($"POIManager: Loading POIs for layer '{layer.layerName}' (Index: {layer.layerIndex}, JSON ID: {jsonTimeLayer.id})");

                var poiDataList = jsonTimeLayer.pois;
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
                poi.characterAudioEvent = poiData.characterAudioEvent;
                poi.navigationCueEvent = poiData.navigationCueEvent;

                poi.portalType = poiData.portalType switch
                {
                    "Forward" => PortalType.Forward,
                    "Backward" => PortalType.Backward,
                    _ => PortalType.None
                };
                poi.portalJumpDistance = poiData.portalJumpDistance;

                if (poi.portalType != PortalType.None &&
                    !string.IsNullOrEmpty(poiData.portalActivationAudio) &&
                    gameDataService != null)
                {
                    poi.portalActivationAudio = gameDataService.GetAudioEventReference(poiData.portalActivationAudio);
                    Debug.Log($"POIManager: Loaded portal audio for {poi.characterName}");
                }

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

                poi.InitializeFromData(poiData, poiObject);

                RectTransform markerTransform = poiObject.GetComponentInChildren<RectTransform>();
                if (markerTransform != null)
                    poi.marker = markerTransform;

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
                    successfullyInitialized.Add(poi);
                }
            }

            activePOIs = successfullyInitialized;
            Debug.Log($"Successfully initialized {activePOIs.Count} POIs for {currentLayer.layerName}");
        }

        private void CleanupCurrentLayerPOIs()
        {
            foreach (var poi in activePOIs)
                poi.Cleanup();

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

            return StorageService.Load<bool>($"Character_{characterId}_Unlocked");
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
                    Debug.Log($"  - {poi.characterName} (ID: {poi.characterId}) Reward: {poi.rewardId}");
            }
        }

        public void CompleteReset()
        {
            Debug.Log("POIManager: COMPLETE RESET - destroying all state");

            StopAllAudio();
            CleanupCurrentLayerPOIs();
            ClearAllNavigationState();

            isInitialized = false;
            totalCompletedPOIs = 0;
            currentMaxActiveCues = baseMaxActiveCues;
            lastProximityPOI = null;

            discoveredThisSession.Clear();
            proximityReachedThisSession.Clear();

            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanging -= OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            Debug.Log("POIManager: Complete reset finished");
        }

        public void StopAllAudio()
        {
            Debug.Log("POIManager: Stopping all POI audio");

            foreach (var poi in activePOIs)
            {
                if (poi.characterAudioInstance.handle != IntPtr.Zero && AudioService != null)
                {
                    AudioService.StopAudio(poi.characterAudioInstance, false);
                    Debug.Log($"POIManager: Stopped character audio for {poi.characterName}");
                }

                if (AudioService != null && AudioService.IsInstanceValid(poi.navigationCueInstance))
                {
                    AudioService.StopAudio(poi.navigationCueInstance, false);
                    Debug.Log($"POIManager: Stopped navigation cue for {poi.characterName}");
                }
            }

            if (AudioService != null)
            {
                if (AudioService.IsInstanceValid(rewardInstance))
                    AudioService.StopAudio(rewardInstance, false);

                if (AudioService.IsInstanceValid(welcomeInstance))
                    AudioService.StopAudio(welcomeInstance, false);
            }

            Debug.Log("POIManager: All POI audio stopped");
        }

        #region Tutorial Mode

        public void TutorialPlayCharacterMusic(POI poi)
        {
            if (!isTutorialMode || poi != tutorialPOI) return;
            Debug.Log("POIManager: Tutorial commanded to play character music");
        }

        public void TutorialPlayCharacterDialogue(POI poi)
        {
            if (!isTutorialMode || poi != tutorialPOI) return;
            Debug.Log("POIManager: Tutorial commanded to play character dialogue");
        }

        public void TutorialStopCharacterAudio(POI poi)
        {
            if (!isTutorialMode || poi != tutorialPOI) return;
            Debug.Log("POIManager: Tutorial commanded to stop character audio");
            poi.SilenceAudio();
        }

        private void CheckTutorialProgress()
        {
            if (!isTutorialMode || tutorialPOI == null) return;
            if (targetingState.mode != TargetingMode.Locked || targetingState.targetPOI != tutorialPOI) return;

            if (poiDataCache.TryGetValue(tutorialPOI, out POIUpdateData data))
            {
                float progressMade = tutorialPOIDistanceWhenLocked - data.distance;

                if (progressMade >= TUTORIAL_PROGRESS_THRESHOLD)
                {
                    Debug.Log($"POIManager: Tutorial progress detected - {progressMade:F1}m closer - firing event");
                    TutorialPOIProgressMade?.Invoke(tutorialPOI, progressMade);
                    tutorialPOIDistanceWhenLocked = data.distance;
                }
            }
        }

        public void EnterTutorialMode()
        {
            Debug.Log("POIManager: Entering tutorial mode");
            isTutorialMode = true;
            tutorialInnerZoneTriggered = false;
            SpawnTutorialPOI();
            Debug.Log($"POIManager: Tutorial mode active - {activePOIs.Count} total POIs (including tutorial POI)");
        }

        public void ExitTutorialMode()
        {
            Debug.Log("POIManager: Exiting tutorial mode");
            isTutorialMode = false;

            if (tutorialPOI != null)
            {
                tutorialPOI.Cleanup();
                activePOIs.Remove(tutorialPOI);
                if (poiDataCache.ContainsKey(tutorialPOI)) poiDataCache.Remove(tutorialPOI);
                tutorialPOI = null;
            }

            lastProximityPOI = null;
            Debug.Log($"POIManager: Tutorial mode exited - {activePOIs.Count} layer POIs remain active");
        }

        private void SpawnTutorialPOI()
        {
            if (gameDataService?.Tutorial == null)
            {
                Debug.LogError("POIManager: No tutorial configuration found in JSON!");
                return;
            }

            var tutorialData = gameDataService.Tutorial;

            if (!tutorialData.enabled)
            {
                Debug.LogWarning("POIManager: Tutorial POI is disabled in configuration");
                return;
            }

            var spawnPosition = SelectTutorialSpawnPosition(tutorialData);
            if (spawnPosition == null)
            {
                Debug.LogError("POIManager: Failed to select tutorial spawn position!");
                return;
            }

            Debug.Log($"POIManager: Spawning tutorial POI at {spawnPosition.name} ({spawnPosition.latitude}, {spawnPosition.longitude})");

            try
            {
                tutorialPOI = CreateTutorialPOI(tutorialData, spawnPosition);

                if (tutorialPOI != null)
                {
                    activePOIs.Add(tutorialPOI);

                    if (tutorialPOI.Initialize(proximityRadius, dialogueRadius))
                    {
                        if (mapManager != null && tutorialPOI.marker != null)
                        {
                            Vector2 poiPosition = mapManager.GetScreenPosition(tutorialPOI.latitude, tutorialPOI.longitude);
                            tutorialPOI.marker.anchoredPosition = poiPosition;
                        }
                        Debug.Log($"POIManager: Tutorial POI spawned successfully at {spawnPosition.name}");
                    }
                    else
                    {
                        Debug.LogError("POIManager: Failed to initialize tutorial POI");
                        activePOIs.Remove(tutorialPOI);
                        tutorialPOI = null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager: Error spawning tutorial POI: {e.Message}");
            }
        }

        private GameDataService.TutorialSpawnPosition SelectTutorialSpawnPosition(GameDataService.TutorialData tutorialData)
        {
            if (tutorialData.spawnPositions == null || tutorialData.spawnPositions.Count < 2)
            {
                Debug.LogError("POIManager: Tutorial must have at least 2 spawn positions!");
                return null;
            }

            if (LocationService == null)
            {
                Debug.LogError("POIManager: LocationService not available!");
                return null;
            }

            Vector2 playerLocation = LocationService.GetCurrentLocation();
            if (playerLocation == Vector2.zero)
            {
                Debug.LogError("POIManager: Player location not available!");
                return null;
            }

            Debug.Log($"POIManager: Player location: ({playerLocation.x:F6}, {playerLocation.y:F6})");

            foreach (var spawnPos in tutorialData.spawnPositions)
            {
                float distance = CalculateDistance(playerLocation.x, playerLocation.y, spawnPos.latitude, spawnPos.longitude);
                Debug.Log($"POIManager: Distance to {spawnPos.name}: {distance:F1}m");

                if (distance >= proximityRadius + 10f)
                {
                    Debug.Log($"POIManager: Selected {spawnPos.name} (distance: {distance:F1}m)");
                    return spawnPos;
                }
            }

            var farthestPosition = tutorialData.spawnPositions
                .OrderByDescending(pos => CalculateDistance(playerLocation.x, playerLocation.y, pos.latitude, pos.longitude))
                .First();

            float farthestDistance = CalculateDistance(playerLocation.x, playerLocation.y, farthestPosition.latitude, farthestPosition.longitude);
            Debug.LogWarning($"POIManager: No position > {proximityRadius + 10f}m found. Using farthest: {farthestPosition.name} ({farthestDistance:F1}m)");

            return farthestPosition;
        }

        private POI CreateTutorialPOI(GameDataService.TutorialData tutorialData, GameDataService.TutorialSpawnPosition spawnPosition)
        {
            try
            {
                GameObject prefab = GetPrefabForCharacter(tutorialData.characterName, tutorialData.characterId);
                GameObject poiObject = Instantiate(prefab, transform);
                poiObject.name = $"Tutorial_POI_{tutorialData.characterId}";

                POI poi = new POI();

                poi.characterName = tutorialData.characterName;
                poi.characterId = tutorialData.characterId;
                poi.latitude = spawnPosition.latitude;
                poi.longitude = spawnPosition.longitude;
                poi.navigationCueEvent = tutorialData.navigationCueEvent;
                poi.characterAudioEvent = tutorialData.characterAudioEvent;

                poi.hasReward = false;
                poi.rewardId = 0;
                poi.rewardName = "";
                poi.portalType = PortalType.None;
                poi.hasMultipleVariants = false;
                poi.narrationVariantCount = 1;

                var poiData = new GameDataService.POIData
                {
                    characterId = tutorialData.characterId,
                    characterName = tutorialData.characterName,
                    latitude = spawnPosition.latitude,
                    longitude = spawnPosition.longitude,
                    navigationCueEvent = tutorialData.navigationCueEvent,
                    characterAudioEvent = tutorialData.characterAudioEvent,
                    navigationCueCount = tutorialData.maxNavigationCues,
                    hasReward = false,
                    reward = null,
                    portalType = "None",
                    portalJumpDistance = 0,
                    portalActivationAudio = "",
                    hasMultipleVariants = false,
                    narrationVariantCount = 1
                };

                poi.InitializeFromData(poiData, poiObject);

                RectTransform markerTransform = poiObject.GetComponentInChildren<RectTransform>();
                if (markerTransform != null)
                    poi.marker = markerTransform;

                Debug.Log($"POIManager: Created tutorial POI at {spawnPosition.name}");
                return poi;
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager: Failed to create tutorial POI: {e.Message}");
                return null;
            }
        }

        public bool IsTutorialMode() => isTutorialMode;
        public POI GetTutorialPOI() => tutorialPOI;

        #endregion

        private void OnDestroy()
        {
            if (SiteManager.Instance != null)
                SiteManager.Instance.OnSiteLoaded -= OnSiteLoaded;

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