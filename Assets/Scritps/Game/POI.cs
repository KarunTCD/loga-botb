using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

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

        // Private state - no external access to avoid state conflicts
        private float proximityRadius;
        private float dialogueRadius;
        private bool isInProximity;
        private bool isAudioPlaying = false;
        private bool hasBeenTriggered = false;

        // Audio references
        public EventReference characterAudioEvent;
        private EventInstance characterAudioInstance;
        private EventInstance sharedCueInstance;

        // Character audio parameters
        private const string ZONE_PARAMETER = "Zone";

        private bool isInitialized;
        private bool isDiscovered;

        // Public properties (read-only)
        public bool IsDiscovered => isDiscovered;
        public bool IsPortal => portalType != PortalType.None;

        // Services
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        /// <summary>
        /// Initialize POI with proximity settings from POIManager
        /// </summary>
        public void Initialize(float proximityRadius, float dialogueRadius)
        {
            this.proximityRadius = proximityRadius;
            this.dialogueRadius = dialogueRadius;

            if (!characterAudioEvent.IsNull)
            {
                characterAudioInstance = AudioService.CreateAudioInstance(characterAudioEvent);
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 0.0f);
            }

            // Show marker
            if (marker != null)
            {
                marker.gameObject.SetActive(true);
                Debug.Log($"Showing marker for {characterName}");
            }

            isInitialized = true;
            Debug.Log($"POI initialized: {characterName}");
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
            if (!isInitialized) return;

            bool wasInProximity = isInProximity;
            isInProximity = (data.distance <= proximityRadius);

            // Handle proximity transitions
            if (isInProximity && !isAudioPlaying)
            {
                AudioService.PlayAudio(characterAudioInstance, data.audioPosition);
                isAudioPlaying = true;
                Debug.Log($"Started audio for {characterName}");
            }
            else if (!isInProximity && isAudioPlaying)
            {
                AudioService.StopAudio(characterAudioInstance, true);
                isAudioPlaying = false;
                hasBeenTriggered = false;
                Debug.Log($"Stopped audio for {characterName}");
            }

            if (isInProximity)
            {
                // Update 3D position and apply zone value calculated by POIManager
                AudioService.Update3DAttributes(characterAudioInstance, data.audioPosition);
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, zoneValue);

                Debug.Log($"{characterName} - Distance: {data.distance:F1}m → Zone: {zoneValue:F2}");

                // Check portal activation
                if (IsPortal && data.distance <= dialogueRadius && !hasBeenTriggered)
                {
                    CheckPortalActivation();
                }
            }
        }

        /// <summary>
        /// Execute navigation cue with configuration determined by POIManager
        /// </summary>
        public void ExecuteNavigationCue(Vector3 position, NavigationCueConfig config)
        {
            if (!isInitialized || isInProximity) return;

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
                PortalType.Forward => TimeLayerManager.Instance.GetForwardLayer(),
                PortalType.Backward => TimeLayerManager.Instance.GetBackwardLayer(),
                _ => null
            };
        }

        private void ActivatePortal(TimeLayer targetLayer)
        {
            hasBeenTriggered = true;

            Debug.Log($"{portalType} portal ({characterName}) activated - transitioning to {targetLayer.layerName}");

            if (!portalActivationAudio.IsNull)
            {
                var portalInstance = AudioService.CreateAudioInstance(portalActivationAudio);
                int portalTypeValue = portalType == PortalType.Forward ? 1 : 2;
                AudioService.SetParameter(portalInstance, "PortalType", portalTypeValue);
                AudioService.SetParameter(portalInstance, "Trigger", 1.0f);
                AudioService.PlayAudio(portalInstance, Vector3.zero);
            }

            TimeLayerManager.Instance.TransitionToLayer(targetLayer);
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

        /// <summary>
        /// Clean up POI resources
        /// </summary>
        public void Cleanup()
        {
            if (!isInitialized) return;

            AudioService.StopAudio(characterAudioInstance);
            AudioService.ReleaseAudio(characterAudioInstance);

            // Hide marker
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                Debug.Log($"Hiding marker for {characterName}");
            }

            isAudioPlaying = false;
            hasBeenTriggered = false;
        }
    }
}