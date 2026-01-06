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
        private int maxNavigationCues = 4;
        private int currentNavigationCueIndex = 0;

        public bool IsCompleted => isCompleted;
        public bool ShouldBeRemoved => shouldBeRemoved;

        private float proximityRadius;
        private float dialogueRadius;
        public bool isInProximity { get; private set; }
        private bool isAudioPlaying = false;
        private bool hasBeenTriggered = false;

        public EventReference characterAudioEvent;
        public EventInstance characterAudioInstance;
        private EventInstance sharedCueInstance;

        private const string ZONE_PARAMETER = "Zone";

        public static bool narrationJustCompleted = false;
        public static IntPtr completedInstanceHandle = IntPtr.Zero;
        private static TimeLayer pendingTransitionLayer = null;
        private static IntPtr pendingPortalInstance = IntPtr.Zero;

        private bool isInitialized;
        private bool isDiscovered;
        private bool wasPlayingBeforeSilence = false;
        private bool dialogueStarted = false;

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

        /// <summary>
        /// Initialize POI from JSON data
        /// </summary>
        public void InitializeFromData(GameDataService.POIData poiData, GameObject gameObject)
        {
            this.id = poiData.characterId.ToString();
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

            this.hasReward = poiData.hasReward;
            if (poiData.hasReward && poiData.reward != null)
            {
                this.rewardId = poiData.reward.rewardId;
                this.rewardName = poiData.reward.rewardName;
            }

            this.hasMultipleVariants = poiData.hasMultipleVariants;
            this.narrationVariantCount = poiData.narrationVariantCount;

            // ? NEW: Initialize navigation cue configuration
            this.maxNavigationCues = poiData.maxNavigationCues > 0 ? poiData.maxNavigationCues : 4;

            this.marker = gameObject.GetComponentInChildren<RectTransform>();
            if (this.marker == null)
            {
                Debug.LogWarning($"POI {characterName}: No RectTransform found for marker");
            }

            Debug.Log($"POI: Initialized {characterName} from JSON (ID: {characterId}, MaxNavCues: {maxNavigationCues})");
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

            if (!characterAudioEvent.IsNull)
            {
                characterAudioInstance = AudioService.CreateAudioInstance(characterAudioEvent);

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

            // ? NEW: Log navigation cue configuration
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
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                if (activeInstances.TryGetValue(instancePtr, out POI poi))
                {
                    Debug.Log($"NARRATION COMPLETE: {poi.characterName}!");
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

        public void SetSharedCueInstance(EventInstance instance)
        {
            sharedCueInstance = instance;
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
                AudioService.PlayAudio(characterAudioInstance, data.audioPosition);
                isAudioPlaying = true;
                dialogueStarted = true;
                Debug.Log($"Entered proximity - started audio for {characterName}");
            }
            else if (!isInProximity && wasInProximity)
            {
                if (dialogueStarted && !isCompleted)
                {
                    Debug.Log($"Player walked away from {characterName} - triggering completion");
                    narrationJustCompleted = true;
                    completedInstanceHandle = characterAudioInstance.handle;
                }

                AudioService.StopAudio(characterAudioInstance, true);
                isAudioPlaying = false;
                hasBeenTriggered = false;
                dialogueStarted = false;
                Debug.Log($"Left proximity - stopped audio for {characterName}");
            }

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
                    Debug.Log($"[{characterName}] Sequential cue: Index {config.cueIndex}");
                    break;

                case NavigationCueType.Targeted:
                    AudioService.PlayNavigationCue(sharedCueInstance, position, characterId,
                        Vector3.Distance(Vector3.zero, position), config.isTargeted, config.maxDistance, config.cueIndex);
                    Debug.Log($"[{characterName}] Targeted cue: Index {config.cueIndex}");
                    break;
            }
        }

        // ? NEW: Get next navigation cue index (sequential cycling)
        public int GetNextNavigationCueIndex()
        {
            int indexToReturn = currentNavigationCueIndex;

            // Increment and wrap around
            currentNavigationCueIndex = (currentNavigationCueIndex + 1) % maxNavigationCues;

            Debug.Log($"POI '{characterName}': Navigation cue {indexToReturn}/{maxNavigationCues - 1}");

            return indexToReturn;
        }

        // ? NEW: Reset navigation cue index (called when targeting clears)
        public void ResetNavigationCueIndex()
        {
            currentNavigationCueIndex = 0;
            Debug.Log($"POI '{characterName}': Navigation cue index reset to 0");
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
            if (GameManager.Instance?.CurrentGameplayState != GameManager.GameplayState.Interact)
            {
                Debug.LogWarning($"Portal activation blocked for {characterName} - not in interact mode");
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

            Debug.Log($"{portalType} portal ({characterName}) activated - transitioning to {targetLayer.layerName}");

            if (!portalActivationAudio.IsNull && AudioService != null)
            {
                var portalInstance = AudioService.CreateAudioInstance(portalActivationAudio);
                int portalTypeValue = portalType == PortalType.Forward ? 1 : 2;
                AudioService.SetParameter(portalInstance, "PortalType", portalTypeValue);

                portalInstance.setCallback(PortalTransitionCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

                pendingTransitionLayer = targetLayer;
                pendingPortalInstance = portalInstance.handle;

                AudioService.PlayAudio(portalInstance, Vector3.zero);
            }
            else
            {
                TimeLayerManager.Instance.TransitionToLayer(targetLayer);
            }
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
            }

            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                Debug.Log($"Hiding marker for {characterName}");
            }

            isAudioPlaying = false;
            hasBeenTriggered = false;
            isInitialized = false;

            Debug.Log($"POI cleanup complete: {characterName}");
        }
    }
}