using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;
using System.Collections.Generic;

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
        public string id;
        public string characterName;
        public float latitude;
        public float longitude;
        public RectTransform marker;
        public int characterId;

        [Header("Portal Settings")]
        public PortalType portalType = PortalType.None;
        public EventReference portalActivationAudio;
        [Range(1, 2)] // Limit to reasonable jumps
        public int portalJumpDistance = 1; // How many layers to jump (default 1)

        [Header("Reward Settings")]
        public bool hasReward = false;          // Does this POI give a reward?
        public int rewardId = 0;               // Specific reward ID (0 = no reward)
        public string rewardName = "";         // For debugging/display

        [Header("Completion State")]
        private bool isCompleted = false;
        private bool shouldBeRemoved = false; // Flag for delayed removal

        // FIXED: Static dictionary for FMOD callback memory management
        private static Dictionary<IntPtr, POI> activeInstances = new Dictionary<IntPtr, POI>();

        [Header("Portal Variant Settings")]
        public bool hasMultipleVariants = false;  // True for fox/raven portals
        public int narrationVariantCount = 2;     // Number of narration variants

        public bool IsCompleted => isCompleted;
        public bool ShouldBeRemoved => shouldBeRemoved;

        // Private state - no external access to avoid state conflicts
        private float proximityRadius;
        private float dialogueRadius;
        public bool isInProximity { get; private set; }
        private bool isAudioPlaying = false;
        private bool hasBeenTriggered = false;

        // Audio references
        public EventReference characterAudioEvent;
        public EventInstance characterAudioInstance;
        private EventInstance sharedCueInstance;

        // Character audio parameters
        private const string ZONE_PARAMETER = "Zone";

        // Parameter tracking for completion detection
        public static bool narrationJustCompleted = false;
        public static IntPtr completedInstanceHandle = IntPtr.Zero;
        private static TimeLayer pendingTransitionLayer = null;
        private static IntPtr pendingPortalInstance = IntPtr.Zero;

        private bool isInitialized;
        private bool isDiscovered;
        private bool wasPlayingBeforeSilence = false;
        private bool dialogueStarted = false;

        // Public properties (read-only)
        public bool IsDiscovered => isDiscovered;
        public bool IsPortal => portalType != PortalType.None;

        // FIXED: Cache service reference with null checking
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

        /// <summary>
        /// Initialize POI from JSON data - NEW METHOD for strict JSON separation
        /// </summary>
        public void InitializeFromData(GameDataService.POIData poiData, GameObject gameObject)
        {
            // Basic data
            this.id = poiData.characterId.ToString();
            this.characterName = poiData.characterName;
            this.characterId = poiData.characterId;
            this.latitude = poiData.latitude;
            this.longitude = poiData.longitude;

            // Portal configuration
            this.portalType = poiData.portalType switch
            {
                "Forward" => PortalType.Forward,
                "Backward" => PortalType.Backward,
                _ => PortalType.None
            };
            this.portalJumpDistance = poiData.portalJumpDistance;

            // Audio events - get from GameDataService
            var gameDataService = ServiceLocator.GetService<IGameDataService>();
            if (gameDataService != null)
            {
                if (!string.IsNullOrEmpty(poiData.characterAudioEvent))
                {
                    this.characterAudioEvent = gameDataService.GetAudioEventReference(poiData.characterAudioEvent);
                }

                if (!string.IsNullOrEmpty(poiData.portalActivationAudio))
                {
                    this.portalActivationAudio = gameDataService.GetAudioEventReference(poiData.portalActivationAudio);
                }
            }

            // Reward configuration
            this.hasReward = poiData.hasReward;
            if (poiData.hasReward && poiData.reward != null)
            {
                this.rewardId = poiData.reward.rewardId;
                this.rewardName = poiData.reward.rewardName;
            }

            // Variant configuration
            this.hasMultipleVariants = poiData.hasMultipleVariants;
            this.narrationVariantCount = poiData.narrationVariantCount;

            // Set marker reference from GameObject
            this.marker = gameObject.GetComponentInChildren<RectTransform>();
            if (this.marker == null)
            {
                Debug.LogWarning($"POI {characterName}: No RectTransform found for marker");
            }

            Debug.Log($"POI: Initialized {characterName} from JSON data (ID: {characterId}, Reward: {rewardId})");
        }

        /// <summary>
        /// Initialize POI with proximity settings from POIManager
        /// FIXED: Added service validation and better error handling
        /// </summary>
        public bool Initialize(float proximityRadius, float dialogueRadius)
        {
            // FIXED: Validate AudioService before proceeding
            if (AudioService == null)
            {
                Debug.LogError($"AudioService not available during POI initialization for {characterName}");
                return false;
            }

            this.proximityRadius = proximityRadius;
            this.dialogueRadius = dialogueRadius;

            if (!characterAudioEvent.IsNull)
            {
                characterAudioInstance = AudioService.CreateAudioInstance(characterAudioEvent);

                // FIXED: Validate instance creation
                if (characterAudioInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError($"Failed to create audio instance for {characterName}");
                    return false;
                }

                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 0.0f);

                // Set narration variant for portals with multiple variants
                if (hasMultipleVariants)
                {
                    int selectedVariant = UnityEngine.Random.Range(1, narrationVariantCount + 1);
                    AudioService.SetParameter(characterAudioInstance, "NarrationVariant", selectedVariant);
                    Debug.Log($"Portal {characterName} - Selected variant: {selectedVariant}");
                }

                // FIXED: Register this POI instance for callbacks with proper cleanup tracking
                activeInstances[characterAudioInstance.handle] = this;

                // Register for timeline marker callbacks (destination markers)
                characterAudioInstance.setCallback(NarrationCompleteCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
            }

            // Show marker
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
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                // Find which POI this belongs to
                if (activeInstances.TryGetValue(instancePtr, out POI poi))
                {
                    Debug.Log($"NARRATION COMPLETE: {poi.characterName}!");

                    // Set flag for POIManager to detect
                    narrationJustCompleted = true;
                    completedInstanceHandle = instancePtr;
                }
            }

            return FMOD.RESULT.OK;
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        static FMOD.RESULT PortalTransitionCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                // Check if this is the portal instance we're waiting for
                if (instancePtr == pendingPortalInstance && pendingTransitionLayer != null)
                {
                    Debug.Log($"Portal audio complete - transitioning to {pendingTransitionLayer.layerName}");

                    // Trigger the actual time layer transition
                    TimeLayerManager.Instance.TransitionToLayer(pendingTransitionLayer);

                    // Clear pending data
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
                // Regular POI - fade out music and mark for immediate removal
                if (AudioService != null)
                {
                    AudioService.StopAudio(characterAudioInstance, true); // Allow fade out
                }
                shouldBeRemoved = true;
            }
        }

        public void SetSharedCueInstance(EventInstance instance)
        {
            sharedCueInstance = instance;
        }

        /// <summary>
        /// Update proximity state with pre-calculated data from POIManager
        /// </summary>
        public void UpdateProximity(POIUpdateData data, float zoneValue)
        {
            if (!isInitialized || AudioService == null) return;

            if (TimeLayerManager.Instance != null && TimeLayerManager.Instance.IsTransitioning)
                return;

            bool wasInProximity = isInProximity;
            isInProximity = (data.distance <= proximityRadius);

            // ENTERING proximity
            if (isInProximity && !wasInProximity)
            {
                AudioService.PlayAudio(characterAudioInstance, data.audioPosition);
                isAudioPlaying = true;
                dialogueStarted = true;
                Debug.Log($"Entered proximity - started audio for {characterName}");
            }
            // LEAVING proximity  
            else if (!isInProximity && wasInProximity)
            {
                // Player walked away during dialogue - trigger completion if dialogue started
                if (dialogueStarted && !isCompleted)
                {
                    Debug.Log($"Player walked away from {characterName} - triggering completion");
                    narrationJustCompleted = true;
                    completedInstanceHandle = characterAudioInstance.handle;
                }

                AudioService.StopAudio(characterAudioInstance, true);
                isAudioPlaying = false;
                hasBeenTriggered = false;
                dialogueStarted = false; // Reset for next interaction
                Debug.Log($"Left proximity - stopped audio for {characterName}");
            }

            // Update audio while in proximity (continuous)
            if (isInProximity)
            {
                AudioService.Update3DAttributes(characterAudioInstance, data.audioPosition);
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, zoneValue);
            }
        }

        /// <summary>
        /// Execute navigation cue with configuration determined by POIManager
        /// </summary>
        public void ExecuteNavigationCue(Vector3 position, NavigationCueConfig config)
        {
            if (!isInitialized || isInProximity || AudioService == null) return;

            // Simple execution - all logic determined by POIManager
            switch (config.cueType)
            {
                case NavigationCueType.DistanceBased:
                    AudioService.PlayNavigationCue(sharedCueInstance, position, characterId,
                        Vector3.Distance(Vector3.zero, position), config.isTargeted, config.maxDistance, config.cueIndex);
                    Debug.Log($"[{characterName}] Distance-based cue: Index {config.cueIndex}");
                    break;

                case NavigationCueType.Sequential:
                    AudioService.PlayNavigationCue(sharedCueInstance, position, characterId,
                        Vector3.Distance(Vector3.zero, position), config.isTargeted, config.maxDistance, config.cueIndex);
                    Debug.Log($"[{characterName}] Sequential cue: {config.cueIndex}/4");
                    break;

                case NavigationCueType.Targeted:
                    AudioService.PlayNavigationCue(sharedCueInstance, position, characterId,
                        Vector3.Distance(Vector3.zero, position), config.isTargeted, config.maxDistance, config.cueIndex);
                    Debug.Log($"[{characterName}] Targeted cue: Index {config.cueIndex}");
                    break;
            }
        }

        public bool CheckNarrationCompletion()
        {
            if (narrationJustCompleted && completedInstanceHandle == characterAudioInstance.handle)
            {
                // Reset the static flags
                narrationJustCompleted = false;
                completedInstanceHandle = IntPtr.Zero;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update targeting visual state (targeting logic handled by POIManager)
        /// </summary>
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
            // FIXED: Add state validation before portal activation
            if (GameManager.Instance?.CurrentGameplayState != GameManager.GameplayState.Interact)
            {
                Debug.LogWarning($"Portal activation blocked for {characterName} - not in interact mode (current: {GameManager.Instance?.CurrentGameplayState})");
                return;
            }

            TimeLayer targetLayer = CalculateTargetLayer();

            if (targetLayer != null && TimeLayerManager.Instance.CanTransitionTo(targetLayer))
            {
                ActivatePortal(targetLayer);
            }
            else
            {
                Debug.Log($"{portalType} portal: No valid transition available");
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

            Debug.Log($"{portalType} portal ({characterName}) activated - starting transition to {targetLayer.layerName}");

            if (!portalActivationAudio.IsNull && AudioService != null)
            {
                var portalInstance = AudioService.CreateAudioInstance(portalActivationAudio);
                int portalTypeValue = portalType == PortalType.Forward ? 1 : 2;
                AudioService.SetParameter(portalInstance, "PortalType", portalTypeValue);

                // Register callback for portal transition completion
                portalInstance.setCallback(PortalTransitionCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

                // Store target layer info for the callback
                pendingTransitionLayer = targetLayer;
                pendingPortalInstance = portalInstance.handle;

                AudioService.PlayAudio(portalInstance, Vector3.zero);
            }
            else
            {
                // No portal audio, transition immediately
                TimeLayerManager.Instance.TransitionToLayer(targetLayer);
            }
        }

        // Discovery and unlock methods
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

        /// <summary>
        /// Clean up POI resources
        /// FIXED: Proper callback cleanup and memory management
        /// </summary>
        public void Cleanup()
        {
            if (!isInitialized) return;

            // FIXED: Critical cleanup - remove callback and static dictionary entry
            if (characterAudioInstance.handle != IntPtr.Zero)
            {
                // Clear callback to prevent memory issues
                characterAudioInstance.setCallback(null, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

                // Remove from static dictionary to prevent memory leak
                activeInstances.Remove(characterAudioInstance.handle);
            }

            if (AudioService != null)
            {
                AudioService.StopAudio(characterAudioInstance);
                AudioService.ReleaseAudio(characterAudioInstance);
            }

            // Hide marker
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                Debug.Log($"Hiding marker for {characterName}");
            }

            isAudioPlaying = false;
            hasBeenTriggered = false;
            isInitialized = false; // Mark as uninitialized

            Debug.Log($"POI cleanup complete: {characterName}");
        }
    }
}