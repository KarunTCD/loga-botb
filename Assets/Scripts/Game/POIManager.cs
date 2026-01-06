using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using FMOD.Studio;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;

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
        public int characterId;
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

        [Header("Audio System")]
        [SerializeField] private EventReference sharedCueEvent;

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

        [Header("Reward System")]
        [SerializeField] private EventReference welcomeGreetingEvent;
        [SerializeField] private EventReference rewardAnnouncementEvent;

        [Header("UI References")]
        [SerializeField] private GameObject targetingIndicator;
        [SerializeField] private TextMeshProUGUI targetingText;
        [SerializeField] private TextMeshProUGUI zoneText;
        [SerializeField] private TextMeshProUGUI completionText;

        private bool isUsingJSONData = false;
        private IGameDataService gameDataService;

        private TimeLayer currentLayer;
        private List<POI> activePOIs = new List<POI>();
        private Dictionary<POI, POIUpdateData> poiDataCache = new Dictionary<POI, POIUpdateData>();

        private List<POI> activeCuePOIs = new List<POI>();
        private TargetingState targetingState = new TargetingState { mode = TargetingMode.None };

        private EventInstance sharedCueInstance;
        private EventInstance welcomeInstance;
        private EventInstance rewardInstance;

        private bool isInitialized = false;
        private int totalCompletedPOIs;
        private int currentMaxActiveCues;
        private static bool isRewardAudioPlaying = false;

        private float cueTimer = 0f;
        private int currentCueIndex = 0;
        private bool isInCyclePause = false;
        private float cyclePauseTimer = 0f;
        // ❌ REMOVED: private int sequentialCueIndex = 0; (no longer used - POI handles its own)
        private int updateFrameCounter = 0;

        private Dictionary<int, bool> discoveredThisSession = new Dictionary<int, bool>();
        private Dictionary<int, bool> proximityReachedThisSession = new Dictionary<int, bool>();

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
                Debug.Log("POIManager: Starting initialization...");

                gameDataService = ServiceLocator.GetService<IGameDataService>();
                isUsingJSONData = (gameDataService != null && gameDataService.IsDataLoaded);

                Debug.Log($"POIManager: Data source determined - Using {(isUsingJSONData ? "JSON" : "Editor")} mode");

                if (isUsingJSONData)
                {
                    ApplyJSONConfiguration();
                }
                else
                {
                    Debug.LogWarning("POIManager: Using editor fallback values");
                    LoadProgressionData();
                }

                Debug.Log("POIManager: Waiting for AudioService initialization...");
                audioService = await ServiceLocator.GetInitializedService<IAudioService>();
                if (audioService == null)
                {
                    Debug.LogError("POIManager: AudioService failed to initialize");
                    return;
                }

                if (!InitializeAudioComponents())
                {
                    Debug.LogError("POIManager: Failed to initialize audio components");
                    return;
                }

                TimeLayerManager.Instance.TimeLayerChanging += OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;

                OnTimeLayerChanged(TimeLayerManager.Instance.CurrentLayer);

                isInitialized = true;
                Debug.Log($"POIManager: Initialization complete in {(isUsingJSONData ? "JSON" : "Editor")} mode");
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager initialization failed: {e.Message}");
            }
        }

        private void ApplyJSONConfiguration()
        {
            if (!isUsingJSONData || gameDataService?.GameConfig == null)
            {
                Debug.LogError("POIManager: ApplyJSONConfiguration called but JSON data not available");
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

            LoadProgressionData();
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

            if (!rewardAnnouncementEvent.IsNull)
            {
                rewardInstance = AudioService.CreateAudioInstance(rewardAnnouncementEvent);
                if (rewardInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError("Failed to create reward instance");
                    return false;
                }
            }

            if (!welcomeGreetingEvent.IsNull)
            {
                welcomeInstance = AudioService.CreateAudioInstance(welcomeGreetingEvent);
                if (welcomeInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError("Failed to create welcome instance");
                    return false;
                }
            }

            return true;
        }

        public void PlayWelcomeGreeting()
        {
            if (!isInitialized || StorageService == null || AudioService == null) return;

            bool hasPlayedWelcome = StorageService.Load<bool>("HasPlayedWelcomeDialogue");
            if (!hasPlayedWelcome && AudioService.IsInstanceValid(welcomeInstance))
            {
                GameManager.Instance?.SuspendNavigationAudio("oak_greeting");
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

        private void Update()
        {
            if (!isInitialized) return;

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
            activeCuePOIs.Clear();
            ClearTargeting();
            isInCyclePause = false;
            cyclePauseTimer = 0f;
            currentCueIndex = 0;
            cueTimer = 0f;
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
                ClearTargeting();
            }
        }

        private void UpdateLockedTargeting(List<POI> eligiblePOIs)
        {
            if (!eligiblePOIs.Contains(targetingState.targetPOI))
            {
                ClearTargeting();
                return;
            }

            if (poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData data))
            {
                if (data.angleDifference > targetBreakAngle)
                {
                    ClearTargeting();
                }
                else
                {
                    targetingState.angleDifference = data.angleDifference;
                }
            }
        }

        // ✅ UPDATED: HandleTargetedNavigation - Use POI's cycling method
        private void HandleTargetedNavigation()
        {
            var targetPOI = targetingState.targetPOI;
            if (!poiDataCache.TryGetValue(targetPOI, out POIUpdateData data)) return;

            cueTimer += Time.deltaTime;

            if (cueTimer >= cueStagingDelay)
            {
                cueTimer = 0f;

                // ✅ CHANGED: Get next cue index from the POI itself
                int cueIndex = targetPOI.GetNextNavigationCueIndex();

                var config = new NavigationCueConfig
                {
                    cueType = NavigationCueType.Sequential,
                    cueIndex = cueIndex,  // ✅ POI provides its own cycling index
                    maxDistance = maxTargetingDistance,
                    isTargeted = true
                };

                targetPOI.ExecuteNavigationCue(data.audioPosition, config);
            }
        }

        // ✅ UPDATED: HandleStandardNavigation - Always use cue_index = 0
        private void HandleStandardNavigation(List<POI> eligiblePOIs)
        {
            activeCuePOIs = eligiblePOIs;

            if (activeCuePOIs.Count == 0) return;

            cueTimer += Time.deltaTime;

            if (isInCyclePause)
            {
                cyclePauseTimer += Time.deltaTime;
                if (cyclePauseTimer >= cyclePauseDelay)
                {
                    isInCyclePause = false;
                    cyclePauseTimer = 0f;
                    currentCueIndex = 0;
                    cueTimer = cueStagingDelay;
                }
                return;
            }

            if (cueTimer >= cueStagingDelay && currentCueIndex < activeCuePOIs.Count)
            {
                cueTimer = 0f;

                var poi = activeCuePOIs[currentCueIndex];
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    // ✅ CHANGED: Always use cue_index = 0 in wander mode (no distance calculation)
                    var config = new NavigationCueConfig
                    {
                        cueType = NavigationCueType.DistanceBased,
                        cueIndex = 0,  // ✅ ALWAYS 0 in wander mode
                        maxDistance = maxTargetingDistance,
                        isTargeted = false
                    };

                    poi.ExecuteNavigationCue(data.audioPosition, config);
                }

                currentCueIndex++;

                if (currentCueIndex >= activeCuePOIs.Count)
                {
                    isInCyclePause = true;
                    cyclePauseTimer = 0f;

                    Debug.Log($"Completed cycle of {activeCuePOIs.Count}/{currentMaxActiveCues} cues, starting {cyclePauseDelay}s pause");
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

            poi.MarkAsCompleted();
            UpdateProgressionTracking(poi);

            AnalyticsService?.TrackEvent($"character_unlocked_{poi.characterId}");

            if (StorageService != null)
            {
                string unlockKey = $"Character{poi.characterId}Unlocked";
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
            if (StorageService == null || !isUsingJSONData || gameDataService == null) return;

            int totalPOIsInGame = GetTotalPOICountFromJSON();

            if (totalPOIsInGame <= 0) return;

            int unlockedCount = 0;
            for (int i = 1; i <= totalPOIsInGame; i++)
            {
                if (StorageService.Load<bool>($"Character{i}Unlocked"))
                {
                    unlockedCount++;
                }
            }

            if (unlockedCount >= totalPOIsInGame)
            {
                StorageService.Save("GameCompleted", true);
                AnalyticsService?.TrackEvent("game_completed_all_characters_unlocked");
                TrackFinalInventoryState();
                TriggerGameEndSequence();
            }
        }

        private int GetTotalPOICountFromJSON()
        {
            if (!isUsingJSONData || gameDataService == null) return 0;

            try
            {
                int totalCount = 0;
                var timeLayerIds = new[] { "modern", "battle_1690", "neolithic" };

                foreach (var layerId in timeLayerIds)
                {
                    var pois = gameDataService.GetPOIsForTimeLayer(layerId);
                    totalCount += pois.Count;
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
                            StorageService.Save($"Character{poi.characterId}Discovered", true);
                        }
                    }

                    FirebaseService.SaveDiscoveredPOI(GameManager.Instance.CurrentSessionId, poi.characterId.ToString());
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

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = $"Locked onto {targetingState.targetPOI.characterName}";

            AnalyticsService?.TrackEvent($"character_targeted_{targetingState.targetPOI.characterId}");

            Debug.Log($"Successfully locked onto {targetingState.targetPOI.characterName} after {targetingState.timer:F2}s");
        }

        // ✅ UPDATED: ClearTargeting - Reset POI's cue cycle
        private void ClearTargeting()
        {
            if (targetingState.mode == TargetingMode.Locked && targetingState.targetPOI != null)
            {
                targetingState.targetPOI.UpdateTargetingState(false);

                // ✅ NEW: Reset POI's cue cycle when unlocking
                targetingState.targetPOI.ResetNavigationCueIndex();
            }

            Debug.Log($"Clearing targeting: {targetingState.targetPOI?.characterName ?? "None"}");

            targetingState = new TargetingState { mode = TargetingMode.None };

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = "";
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
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Mode: {(isUsingJSONData ? "JSON" : "Editor")}";
            }
            else if (targetingState.mode == TargetingMode.Potential)
            {
                float progress = (targetingState.timer / targetLockTime) * 100f;
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"Targeting: {targetingState.targetPOI.characterName}\n" +
                               $"Progress: {progress:F1}%\n" +
                               $"MaxCues: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Mode: {(isUsingJSONData ? "JSON" : "Editor")}";
            }
            else
            {
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"POIs: {activePOIs.Count}\n" +
                               $"MaxCues: {currentMaxActiveCues} (Completed: {totalCompletedPOIs})\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Location: {location.x:F6}, {location.y:F6}\n" +
                               $"Mode: {(isUsingJSONData ? "JSON" : "Editor")}";
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
            Debug.Log($"POIManager: Loading {newLayer.layerName} layer in {(isUsingJSONData ? "JSON" : "Editor")} mode");

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
                debugText.text = $"Layer: {newLayer.layerName}\nPOIs: {activePOIs.Count}\nMode: {(isUsingJSONData ? "JSON" : "Editor")}";
            }
        }

        private void LoadLayerPOIs(TimeLayer layer)
        {
            activePOIs.Clear();

            if (isUsingJSONData)
            {
                Debug.Log($"POIManager: JSON mode - loading POIs from JSON data for {layer.layerName}");
                LoadPOIsFromJSON(layer);

                if (layer.pois != null && layer.pois.Count > 0)
                {
                    Debug.Log($"POIManager: Cleaning up {layer.pois.Count} unused editor POIs");
                    foreach (var editorPOI in layer.pois)
                    {
                        editorPOI?.Cleanup();
                    }
                    layer.pois.Clear();
                }
            }
            else
            {
                Debug.Log($"POIManager: Editor fallback mode - loading POIs for {layer.layerName}");

                if (layer.pois != null && layer.pois.Count > 0)
                {
                    int skippedCount = 0;
                    int loadedCount = 0;

                    foreach (var poi in layer.pois)
                    {
                        if (IsPOICompleted(poi.characterId))
                        {
                            Debug.Log($"POIManager: Skipping completed editor POI - {poi.characterName} (ID: {poi.characterId})");
                            skippedCount++;
                            continue;
                        }

                        activePOIs.Add(poi);
                        loadedCount++;
                    }

                    Debug.Log($"POIManager: Added {loadedCount} editor POIs, skipped {skippedCount} completed");
                }
                else
                {
                    Debug.LogWarning($"POIManager: No editor POIs configured for {layer.layerName}");
                }
            }

            InitializePOIs();
            Debug.Log($"POIManager: Loaded {activePOIs.Count} POIs for {layer.layerName} (Mode: {(isUsingJSONData ? "JSON" : "Editor")})");
        }

        private void LoadPOIsFromJSON(TimeLayer layer)
        {
            if (!isUsingJSONData || gameDataService == null)
            {
                Debug.LogError("POIManager: LoadPOIsFromJSON called but not in JSON mode");
                return;
            }

            string jsonLayerId = MapTimeLayerToJsonId(layer.layerName);
            var poiDataList = gameDataService.GetPOIsForTimeLayer(jsonLayerId);

            Debug.Log($"POIManager: Found {poiDataList.Count} total POIs in JSON for layer {jsonLayerId}");

            int skippedCount = 0;
            int loadedCount = 0;

            foreach (var poiData in poiDataList)
            {
                if (IsPOICompleted(poiData.characterId))
                {
                    Debug.Log($"POIManager: Skipping already-completed POI - {poiData.characterName} (ID: {poiData.characterId})");
                    skippedCount++;
                    continue;
                }

                POI poi = CreatePOIFromJSONData(poiData);
                if (poi != null)
                {
                    activePOIs.Add(poi);
                    loadedCount++;
                    Debug.Log($"POIManager: ✓ Created new JSON POI - {poiData.characterName} (ID: {poiData.characterId})");
                }
            }

            Debug.Log($"POIManager: 📊 Loaded {loadedCount} new POIs, skipped {skippedCount} completed POIs");
        }

        private POI CreatePOIFromJSONData(GameDataService.POIData poiData)
        {
            try
            {
                GameObject prefab = GetPrefabForCharacter(poiData.characterName, poiData.characterId);
                GameObject poiObject = Instantiate(prefab, transform);
                poiObject.name = $"JSON_Character_{poiData.characterId}_{poiData.characterName}";

                POI poi = new POI();

                poi.id = poiData.characterId.ToString();
                poi.characterName = poiData.characterName;
                poi.characterId = poiData.characterId;
                poi.latitude = poiData.latitude;
                poi.longitude = poiData.longitude;

                if (gameDataService != null)
                {
                    poi.characterAudioEvent = gameDataService.GetAudioEventReference(poiData.characterAudioEvent);
                }

                poi.portalType = poiData.portalType switch
                {
                    "Forward" => PortalType.Forward,
                    "Backward" => PortalType.Backward,
                    _ => PortalType.None
                };
                poi.portalJumpDistance = poiData.portalJumpDistance;

                if (!string.IsNullOrEmpty(poiData.portalActivationAudio) && gameDataService != null)
                {
                    poi.portalActivationAudio = gameDataService.GetAudioEventReference(poiData.portalActivationAudio);
                }

                poi.hasReward = poiData.hasReward;
                if (poiData.hasReward && poiData.reward != null)
                {
                    poi.rewardId = poiData.reward.rewardId;
                    poi.rewardName = poiData.reward.rewardName;
                }

                poi.hasMultipleVariants = poiData.hasMultipleVariants;
                poi.narrationVariantCount = poiData.narrationVariantCount;

                // ✅ NEW: Initialize POI from JSON data (includes maxNavigationCues)
                poi.InitializeFromData(poiData, poiObject);

                RectTransform markerTransform = poiObject.GetComponentInChildren<RectTransform>();
                if (markerTransform != null)
                {
                    poi.marker = markerTransform;
                }

                Debug.Log($"POIManager: Successfully created JSON POI - {poiData.characterName} (ID: {poiData.characterId}, Reward: {poiData.reward?.rewardId ?? 0})");
                return poi;
            }
            catch (Exception e)
            {
                Debug.LogError($"POIManager: Failed to create POI from JSON data for {poiData.characterName}: {e.Message}");
                return null;
            }
        }

        private GameObject GetPrefabForCharacter(string characterName, int characterId)
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

        private string MapTimeLayerToJsonId(string timeLayerName)
        {
            return timeLayerName.ToLower() switch
            {
                "modern era" => "modern",
                "modern" => "modern",
                "battle of the boyne 1690" => "battle_1690",
                "battle of the boyne" => "battle_1690",
                "battle 1690" => "battle_1690",
                "neolithic era" => "neolithic",
                "neolithic" => "neolithic",
                "ancient" => "neolithic",
                _ => "modern"
            };
        }

        private void InitializePOIs()
        {
            if (AudioService == null)
            {
                Debug.LogError("Cannot initialize POIs - AudioService not available");
                return;
            }

            try
            {
                if (sharedCueInstance.handle != IntPtr.Zero && AudioService.IsInstanceValid(sharedCueInstance))
                {
                    AudioService.ReleaseAudio(sharedCueInstance);
                }

                sharedCueInstance = AudioService.CreateAudioInstance(sharedCueEvent);
                if (sharedCueInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError("Failed to create shared cue instance");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error creating shared cue instance: " + e.Message);
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
                    poi.SetSharedCueInstance(sharedCueInstance);
                    successfullyInitialized.Add(poi);
                }
                else
                {
                    Debug.LogError($"Failed to initialize POI: {poi.characterName}");
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

            if (AudioService != null && sharedCueInstance.handle != IntPtr.Zero)
            {
                AudioService.StopAudio(sharedCueInstance, false);
                AudioService.ReleaseAudio(sharedCueInstance);
            }
        }

        public void UpdateUnlockedPOIs(List<string> unlockedPOIIds)
        {
            foreach (var poi in activePOIs)
            {
                bool isUnlocked = unlockedPOIIds.Contains(poi.characterId.ToString());
                poi.SetUnlocked(isUnlocked);
            }
        }

        private bool IsPOICompleted(int characterId)
        {
            if (StorageService == null)
            {
                Debug.LogWarning("POIManager: StorageService not available for unlock check - assuming not completed");
                return false;
            }

            string characterUnlockKey = $"Character{characterId}Unlocked";
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
            Debug.Log($"Data Source: {(isUsingJSONData ? "JSON" : "Editor")}");
        }

        [ContextMenu("Debug POI Distances")]
        public void DebugPOIDistances()
        {
            Debug.Log($"=== POI Distance Debug ===");
            Debug.Log($"Data Source: {(isUsingJSONData ? "JSON" : "Editor")}");
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
            Debug.Log($"🔄 Progression reset. Max cues: {currentMaxActiveCues}");
        }

        [ContextMenu("Debug Data Source")]
        public void DebugDataSource()
        {
            Debug.Log($"=== POI Manager Data Source Debug ===");
            Debug.Log($"Using JSON Data: {isUsingJSONData}");
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

        private void OnDestroy()
        {
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanging -= OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            CleanupCurrentLayerPOIs();

            if (AudioService != null && rewardInstance.handle != IntPtr.Zero)
            {
                AudioService.StopAudio(rewardInstance, true);
                AudioService.ReleaseAudio(rewardInstance);
            }
        }
    }
}