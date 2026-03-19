using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;
using System.Collections.Generic;
using TMPro;

namespace LoGa.LudoEngine.Game
{
    public enum PortalType
    {
        None,     // Regular POI
        Forward,  // Takes player forward in time
        Backward  // Takes player backward in time
    }

    public enum NavigationCueType
    {
        DistanceBased,
        Sequential,
        Targeted
    }

    [System.Serializable]
    public struct POIUpdateData
    {
        public float distance;
        public float bearing;
        public Vector3 audioPosition;
        public float angleDifference;
    }

    [System.Serializable]
    public struct NavigationCueConfig
    {
        public NavigationCueType cueType;
        public int cueIndex;
        public float maxDistance;
        public bool isTargeted;
    }

    [System.Serializable]
    public class POI
    {
        // Basic POI data
        public string characterName;
        public float latitude;
        public float longitude;
        public RectTransform marker;
        public string characterId;

        [Header("Portal Settings")]
        public PortalType portalType = PortalType.None;
        public EventReference portalActivationAudio;
        [Range(1, 2)]
        public int portalJumpDistance = 1;

        [Header("Reward Settings")]
        public bool hasReward = false;
        public int rewardId = 0;
        public string rewardName = "";

        [Header("Completion State")]
        private bool isCompleted = false;
        private bool shouldBeRemoved = false;

        private static Dictionary<IntPtr, POI> activeInstances = new Dictionary<IntPtr, POI>();

        [Header("Portal Variant Settings")]
        public bool hasMultipleVariants = false;
        public int narrationVariantCount = 2;

        // ? NEW: Navigation cue cycling fields
        [Header("Navigation Cue Settings")]
        private bool waitingForCueCompletion = false; // completion monitoring
        private float lastTriggerValue = 0f; // for Command Instrument detection
        private int maxNavigationCues;
        private int currentNavigationCueIndex = 0;
        public bool IsCompleted => isCompleted;
        public bool ShouldBeRemoved => shouldBeRemoved;

        private float proximityRadius;
        private float dialogueRadius;
        public bool isInProximity { get; private set; }

        private float maxZoneReached = 0f;  // Track highest Zone value during proximity
        private bool isMarkerCompletionPending = false;  // Prevent walk-away during marker delay
        private const float NARRATION_ENGAGEMENT_THRESHOLD = 1.4f;  // Zone threshold for completion
        private bool isAudioPlaying = false;
        private bool hasBeenTriggered = false;

        public string characterAudioEvent;
        public EventInstance characterAudioInstance;
        public string navigationCueEvent;
        public EventInstance navigationCueInstance;

        private const string ZONE_PARAMETER = "Zone";

        public static bool narrationJustCompleted = false;
        public static IntPtr completedInstanceHandle = IntPtr.Zero;
        private static TimeLayer pendingTransitionLayer = null;
        private static IntPtr pendingPortalInstance = IntPtr.Zero;

        private bool isInitialized;
        private bool isDiscovered;
        private bool wasPlayingBeforeSilence = false;

        public bool IsDiscovered => isDiscovered;
        public bool IsPortal => portalType != PortalType.None;

        private IAudioService audioService;
        private IAudioService AudioService
        {
            get
            {
                if (audioService == null)
                    audioService = ServiceLocator.GetService<IAudioService>();
                return audioService;
            }
        }
        private TextMeshProUGUI directionDebugText;

        /// <summary>
        /// Initialize POI from JSON data
        /// </summary>
        public void InitializeFromData(GameDataService.POIData poiData, GameObject gameObject)
        {
            this.characterName = poiData.characterName;
            this.characterId = poiData.characterId;
            this.latitude = poiData.latitude;
            this.longitude = poiData.longitude;

            this.portalType = poiData.portalType switch
            {
                "Forward" => PortalType.Forward,
                "Backward" => PortalType.Backward,
                _ => PortalType.None
            };
            this.portalJumpDistance = poiData.portalJumpDistance;

            // Store event names as strings from JSON
            this.characterAudioEvent = poiData.characterAudioEvent;
            this.navigationCueEvent = poiData.navigationCueEvent;

            this.hasReward = poiData.hasReward;
            if (poiData.hasReward && poiData.reward != null)
            {
                this.rewardId = poiData.reward.id;
                this.rewardName = poiData.reward.name;
            }

            this.hasMultipleVariants = poiData.hasMultipleVariants;
            this.narrationVariantCount = poiData.narrationVariantCount;

            // Load navigation cue count from JSON
            this.maxNavigationCues = poiData.navigationCueCount > 0 ? poiData.navigationCueCount : 4;

            this.marker = gameObject.GetComponentInChildren<RectTransform>();
            if (this.marker == null)
            {
                Debug.LogWarning($"POI {characterName}: No RectTransform found for marker");
            }

            Debug.Log($"POI: Initialized {characterName} from JSON (ID: {characterId}, NavCues: {maxNavigationCues}, NavEvent: {navigationCueEvent})");
        }

        /// <summary>
        /// Initialize POI with proximity settings from POIManager
        /// </summary>
        public bool Initialize(float proximityRadius, float dialogueRadius)
        {
            if (AudioService == null)
            {
                Debug.LogError($"AudioService not available during POI initialization for {characterName}");
                return false;
            }

            this.proximityRadius = proximityRadius;
            this.dialogueRadius = dialogueRadius;

            // Initialize character audio instance from string event name
            if (!string.IsNullOrEmpty(characterAudioEvent))
            {
                var gameDataService = ServiceLocator.GetService<IGameDataService>();
                if (gameDataService != null)
                {
                    EventReference charEvent = gameDataService.GetAudioEventReference(characterAudioEvent);
                    characterAudioInstance = AudioService.CreateAudioInstance(charEvent);

                    if (characterAudioInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogError($"Failed to create audio instance for {characterName}");
                        return false;
                    }

                    AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 0.0f);

                    if (hasMultipleVariants)
                    {
                        int selectedVariant = UnityEngine.Random.Range(1, narrationVariantCount + 1);
                        AudioService.SetParameter(characterAudioInstance, "NarrationVariant", selectedVariant);
                        Debug.Log($"Portal {characterName} - Selected variant: {selectedVariant}");
                    }

                    activeInstances[characterAudioInstance.handle] = this;
                    characterAudioInstance.setCallback(NarrationCompleteCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                }
            }

            // Initialize navigation cue instance from string event name
            if (!string.IsNullOrEmpty(navigationCueEvent))
            {
                var gameDataService = ServiceLocator.GetService<IGameDataService>();
                if (gameDataService != null)
                {
                    EventReference navEvent = gameDataService.GetAudioEventReference(navigationCueEvent);
                    navigationCueInstance = AudioService.CreateAudioInstance(navEvent);

                    if (navigationCueInstance.handle == IntPtr.Zero)
                    {
                        Debug.LogError($"Failed to create navigation cue instance for {characterName}");
                    }
                    else
                    {
                        Debug.Log($"Created navigation cue instance for {characterName}: {navigationCueEvent}");
                    }
                }
            }

            Debug.Log($"POI '{characterName}': Configured with {maxNavigationCues} navigation cues");

            if (marker != null)
            {
                marker.gameObject.SetActive(true);
                Debug.Log($"Showing marker for {characterName}");
            }

            isInitialized = true;
            Debug.Log($"POI initialized: {characterName}");
            return true;
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        static FMOD.RESULT NarrationCompleteCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            Debug.LogError($" MARKER HIT - Instance: {instancePtr}, In activeInstances: {activeInstances.ContainsKey(instancePtr)}");

            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                if (activeInstances.TryGetValue(instancePtr, out POI poi))
                {
                    Debug.Log($"NARRATION MARKER HIT: {poi.characterName} - scheduling delayed completion");
                    
                    // Set pending flag to block walk-away completion
                    poi.isMarkerCompletionPending = true;

                    // Get delay from config
                    float delay = 2.0f;
                    var gameDataService = ServiceLocator.GetService<IGameDataService>();
                    if (gameDataService?.GameConfig != null)
                    {
                        delay = gameDataService.GameConfig.narrationCompleteDelay;
                    }
                    
                    // Use POIManager to run coroutine
                    if (POIManager.Instance != null)
                    {
                        POIManager.Instance.ScheduleDelayedCompletion(poi, delay);
                    }
                    else
                    {
                        Debug.LogError("POIManager.Instance is null - completing immediately as fallback");
                        poi.isMarkerCompletionPending = false; // Clear flag
                        narrationJustCompleted = true;
                        completedInstanceHandle = poi.characterAudioInstance.handle;
                    }
                }
            }

            return FMOD.RESULT.OK;
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        static FMOD.RESULT PortalTransitionCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                if (instancePtr == pendingPortalInstance && pendingTransitionLayer != null)
                {
                    Debug.Log($"Portal audio complete - transitioning to {pendingTransitionLayer.layerName}");
                    TimeLayerManager.Instance.TransitionToLayer(pendingTransitionLayer);
                    pendingTransitionLayer = null;
                    pendingPortalInstance = IntPtr.Zero;
                }
            }

            return FMOD.RESULT.OK;
        }

        public void MarkAsCompleted()
        {
            isCompleted = true;

            if (IsPortal)
            {
                CheckPortalActivation();
            }
            else
            {
                if (AudioService != null)
                {
                    AudioService.StopAudio(characterAudioInstance, true);
                }
                shouldBeRemoved = true;
            }
        }

        public void UpdateProximity(POIUpdateData data, float zoneValue)
        {
            if (!isInitialized || AudioService == null) return;

            if (TimeLayerManager.Instance != null && TimeLayerManager.Instance.IsTransitioning)
                return;

            bool wasInProximity = isInProximity;
            isInProximity = (data.distance <= proximityRadius);

            if (isInProximity && !wasInProximity)
            {
                // Don't reset tracking if marker delay is pending
                // (Player walked away then came back during 2-second delay)
                if (!isMarkerCompletionPending)
                {
                    maxZoneReached = 0f;
                    Debug.Log($"Entered proximity - started audio for {characterName}");
                }
                else
                {
                    Debug.Log($"[RE-ENTRY] {characterName} - marker delay still pending, preserving maxZone: {maxZoneReached:F2}");
                }
                AudioService.PlayAudio(characterAudioInstance, data.audioPosition);
                isAudioPlaying = true;
            }
            else if (!isInProximity && wasInProximity)
            {
                bool playerEngagedWithNarration = maxZoneReached >= NARRATION_ENGAGEMENT_THRESHOLD;
                bool markerDelayPending = isMarkerCompletionPending;

                Debug.Log($"[WALK-AWAY] {characterName} - maxZoneReached: {maxZoneReached:F2}, " +
                  $"threshold: {NARRATION_ENGAGEMENT_THRESHOLD:F2}, " +
                  $"playerEngaged: {playerEngagedWithNarration}");
                
                // Only complete if:
                // 1. Player engaged with narration (Zone ≥ 1.4)
                // 2. No marker completion pending (prevents double-completion)
                if (!isCompleted && playerEngagedWithNarration && !markerDelayPending)
                {
                    Debug.Log($"Player walked away from {characterName} after hearing narration - triggering completion (IMMEDIATE, NO DELAY)");
                    narrationJustCompleted = true;
                    completedInstanceHandle = characterAudioInstance.handle;
                }
                else if (markerDelayPending)
                {
                    Debug.Log($"[WALK-AWAY-BLOCKED] Marker completion pending for {characterName} - walk-away completion skipped");
                }
                else if (!playerEngagedWithNarration)
                {
                    Debug.Log($"Player walked away from {characterName} without hearing narration (maxZone: {maxZoneReached:F2}) - NO COMPLETION");
                }

                AudioService.StopAudio(characterAudioInstance, true);
                isAudioPlaying = false;
                hasBeenTriggered = false;
                // Don't reset maxZone or flag if marker delay pending
                if (!isMarkerCompletionPending)
                {
                    maxZoneReached = 0f;
                }
                Debug.Log($"Left proximity - stopped audio for {characterName}");
            }

            if (isInProximity)
            {
                // WHILE IN PROXIMITY - Track max Zone reached
                if (zoneValue > maxZoneReached)
                {
                    float previousMax = maxZoneReached;  
                    maxZoneReached = zoneValue;
                    
                    // Log when CROSSING threshold upward
                    if (zoneValue >= NARRATION_ENGAGEMENT_THRESHOLD && 
                        previousMax < NARRATION_ENGAGEMENT_THRESHOLD)
                    {
                        Debug.Log($"[ZONE-THRESHOLD] {characterName} crossed narration threshold - maxZone: {maxZoneReached:F2}");
                    }
                }

                AudioService.Update3DAttributes(characterAudioInstance, data.audioPosition);
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, zoneValue);
            }
        }

        /// <summary>
        /// Execute navigation cue with configuration determined by POIManager
        /// </summary>
        public void ExecuteNavigationCue(Vector3 position, NavigationCueConfig config)
        {
            Debug.Log($"POI.ExecuteNavigationCue called for {characterName}");

            if (!isInitialized || isInProximity || AudioService == null)
            {
                Debug.LogWarning($"BLOCKED - Init: {isInitialized}, Proximity: {isInProximity}, AudioService: {AudioService != null}");
                return;
            }

            if (!AudioService.IsInstanceValid(navigationCueInstance))
            {
                Debug.LogError($"Navigation cue instance invalid for {characterName}");
                return;
            }

            // Calculate direction from position (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW)
            float angle = Mathf.Atan2(position.x, position.z) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            int direction = Mathf.RoundToInt(angle / 45f) % 8;

            float distance = Vector3.Distance(Vector3.zero, position);
            float normalizedDistance = distance / config.maxDistance;

            if (directionDebugText != null)
            {
                string[] directionNames = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };
                directionDebugText.text = $" {characterName}\n" +
                                         $"Angle: {angle:F1}°\n" +
                                         $"Direction: {direction} ({directionNames[direction]})\n" +
                                         $"Distance: {distance:F1}m\n" +
                                         $"3D Pos: X={position.x:F1}, Z={position.z:F1}\n" +
                                         $"Cue Index: {config.cueIndex}";
            }

            // Use updated AudioService method (encapsulates all FMOD calls)
            AudioService.PlayNavigationCue(navigationCueInstance, position, config.cueIndex, direction, normalizedDistance);

            waitingForCueCompletion = true;

            Debug.Log($"[{characterName}] Navigation cue executed: Index {config.cueIndex}, Direction {direction}, Distance {normalizedDistance:F3}");
        }

        // Get next navigation cue index (sequential cycling)
        public int GetNextNavigationCueIndex()
        {
            int indexToReturn = currentNavigationCueIndex;

            // Increment and wrap around
            currentNavigationCueIndex = (currentNavigationCueIndex + 1) % maxNavigationCues;

            // Add 1 to convert from 0-based to 1-based indexing
            int fmodCueIndex = indexToReturn + 1;

            Debug.Log($"POI '{characterName}': Navigation cue {fmodCueIndex}/{maxNavigationCues} (internal: {indexToReturn})");

            return fmodCueIndex; 
        }

        // Reset navigation cue index (called when targeting clears)
        public void ResetNavigationCueIndex()
        {
            currentNavigationCueIndex = 0;
            Debug.Log($"POI '{characterName}': Navigation cue index reset to 0");
        }

        public bool CheckNavigationCueCompletion()
        {
            if (!waitingForCueCompletion) return false;

            if (!AudioService.IsInstanceValid(navigationCueInstance))
            {
                waitingForCueCompletion = false;
                return false;
            }

            float explicitValue, finalValue;
            FMOD.RESULT result = navigationCueInstance.getParameterByName("Trigger", out explicitValue, out finalValue);

            if (result == FMOD.RESULT.OK && finalValue < 0.1f)
            {
                waitingForCueCompletion = false;
                Debug.Log($"Navigation cue completed for {characterName} via Command Instrument");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stop this POI's navigation cue audio if playing
        /// </summary>
        public void StopNavigationCue()
        {
            if (AudioService != null && AudioService.IsInstanceValid(navigationCueInstance))
            {
                AudioService.StopAudio(navigationCueInstance, false);
                waitingForCueCompletion = false; // Also clear the waiting flag
                Debug.Log($"Stopped navigation cue for {characterName}");
            }
        }

        public bool IsWaitingForCueCompletion()
        {
            return waitingForCueCompletion;
        }

        public bool CheckNarrationCompletion()
        {
            if (narrationJustCompleted && completedInstanceHandle == characterAudioInstance.handle)
            {
                narrationJustCompleted = false;
                completedInstanceHandle = IntPtr.Zero;
                return true;
            }
            return false;
        }

        public void UpdateTargetingState(bool isTargeted)
        {
            if (marker != null)
            {
                if (isTargeted)
                {
                    marker.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                    Debug.Log($"POI {characterName} marked as target");
                }
                else
                {
                    marker.transform.localScale = Vector3.one;
                    Debug.Log($"POI {characterName} target cleared");
                }
            }
        }

        private void CheckPortalActivation()
        {
            Debug.Log($" CheckPortalActivation called for {characterName}");
            Debug.Log($"   GameplayState: {GameManager.Instance?.CurrentGameplayState}");
            Debug.Log($"   PortalType: {portalType}");

            if (GameManager.Instance?.CurrentGameplayState != GameManager.GameplayState.Interact)
            {
                Debug.LogWarning($"Portal activation blocked for {characterName} - not in interact mode");
                Debug.LogWarning($"   Current state: {GameManager.Instance?.CurrentGameplayState}");
                return;
            }

            TimeLayer targetLayer = CalculateTargetLayer();
            Debug.Log($"   TargetLayer: {targetLayer?.layerName ?? "None"}");

            if (targetLayer != null && TimeLayerManager.Instance.CanTransitionTo(targetLayer))
            {
                Debug.Log($" Activating portal to {targetLayer.layerName}");
                ActivatePortal(targetLayer);
            }
            else
            {
                Debug.LogError($" Portal activation failed");
                Debug.LogError($"   TargetLayer null: {targetLayer == null}");
                Debug.LogError($"   CanTransition: {TimeLayerManager.Instance?.CanTransitionTo(targetLayer) ?? false}");
            }
        }

        private TimeLayer CalculateTargetLayer()
        {
            return portalType switch
            {
                PortalType.Forward => TimeLayerManager.Instance.GetForwardLayer(portalJumpDistance),
                PortalType.Backward => TimeLayerManager.Instance.GetBackwardLayer(portalJumpDistance),
                _ => null
            };
        }

        private void ActivatePortal(TimeLayer targetLayer)
        {
            hasBeenTriggered = true;

            Debug.Log($"{portalType} portal ({characterName}) activated - transitioning to {targetLayer.layerName}");

            // Get portal audio from TimeLayerManager 
            EventReference portalAudio = TimeLayerManager.Instance.GetTimePortalEvent();

            if (!portalAudio.IsNull && AudioService != null)
            {
                Debug.Log($" Creating portal audio instance from TimeLayerManager...");

                var portalInstance = AudioService.CreateAudioInstance(portalAudio);

                if (portalInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError($" Failed to create portal audio instance!");
                    TimeLayerManager.Instance.TransitionToLayer(targetLayer);
                    return;
                }

                Debug.Log($" Portal audio instance created successfully");

                int portalTypeValue = portalType == PortalType.Forward ? 1 : 2;
                AudioService.SetParameter(portalInstance, "PortalType", portalTypeValue);
                Debug.Log($" Set PortalType parameter to: {portalTypeValue}");

                portalInstance.setCallback(PortalTransitionCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

                pendingTransitionLayer = targetLayer;
                pendingPortalInstance = portalInstance.handle;

                AudioService.PlayAudio(portalInstance, Vector3.zero);
                Debug.Log($" Portal audio started - waiting for completion");
            }
            else
            {
                Debug.LogWarning($" No portal audio available - transitioning immediately");
                Debug.LogWarning($"   TimeLayerManager portal audio null: {portalAudio.IsNull}");
                Debug.LogWarning($"   AudioService null: {AudioService == null}");

                TimeLayerManager.Instance.TransitionToLayer(targetLayer);
            }
        }

        /// <summary>
        /// Trigger portal activation without marking POI as completed
        /// Called for portal characters after narration ends
        /// </summary>
        public void TriggerPortalActivation()
        {
            if (!IsPortal)
            {
                Debug.LogWarning($"TriggerPortalActivation called on non-portal character: {characterName}");
                return;
            }

            Debug.Log($" Portal {characterName} activation triggered");

            // Run portal logic without marking as completed
            CheckPortalActivation();
        }

        public void SetDiscovered(bool discovered)
        {
            isDiscovered = discovered;
            if (marker != null)
            {
                marker.gameObject.SetActive(true);
            }
        }

        public void SetUnlocked(bool unlocked)
        {
            isDiscovered = unlocked;
            if (marker != null)
            {
                marker.gameObject.SetActive(unlocked);
            }
        }

        public void SilenceAudio()
        {
            if (isAudioPlaying && AudioService != null)
            {
                wasPlayingBeforeSilence = true;
                AudioService.StopAudio(characterAudioInstance, true);
            }
        }

        public void ResumeAudio(Vector3 audioPosition)
        {
            if (wasPlayingBeforeSilence && isInProximity && AudioService != null)
            {
                AudioService.PlayAudio(characterAudioInstance, audioPosition);
                wasPlayingBeforeSilence = false;
            }
        }

        public void SetDirectionDebugText(TextMeshProUGUI debugText)
        {
            directionDebugText = debugText;
        }

        public void ClearDirectionDebug()
        {
            if (directionDebugText != null)
            {
                directionDebugText.text = "";
            }
        }
        public void ClearMarkerPendingFlag()
        {
            isMarkerCompletionPending = false;
            maxZoneReached = 0f;  // Safe to reset now - delay complete
            Debug.Log($"[MARKER-FLAG] Cleared pending flag for {characterName}");
        }

        public void Cleanup()
        {
            if (!isInitialized) return;

            if (characterAudioInstance.handle != IntPtr.Zero)
            {
                characterAudioInstance.setCallback(null, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                activeInstances.Remove(characterAudioInstance.handle);
            }

            if (AudioService != null)
            {
                AudioService.StopAudio(characterAudioInstance);
                AudioService.ReleaseAudio(characterAudioInstance);

                AudioService.StopAudio(navigationCueInstance, false);
                AudioService.ReleaseAudio(navigationCueInstance);

            }

            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                Debug.Log($"Hiding marker for {characterName}");
            }

            isAudioPlaying = false;
            hasBeenTriggered = false;
            waitingForCueCompletion = false;
            isInitialized = false;
            maxZoneReached = 0f;
            isMarkerCompletionPending = false;

            Debug.Log($"POI cleanup complete: {characterName}");
        }

    }
}