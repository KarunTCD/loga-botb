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

        [Header("Distance Thresholds")]
        public float proximityRadius = 30f;   // Distance to start hearing character audio
        public float dialogueRadius = 10f;    // Distance to start hearing dialogue

        [Header("Portal Settings")]
        public PortalType portalType = PortalType.None;
        public EventReference portalActivationAudio;

        private bool isTargeted = false;
        public bool IsTargeted => isTargeted;

        // Audio references
        public EventReference characterAudioEvent;
        private EventInstance characterAudioInstance;
        private EventInstance sharedCueInstance;

        // Character audio parameters
        private const string ZONE_PARAMETER = "Zone";

        private bool isInitialized;
        private bool isDiscovered;
        private bool isInProximity;
        private bool hasBeenTriggered = false;

        public bool IsDiscovered => isDiscovered;
        public bool IsPortal => portalType != PortalType.None;

        // Services - accessed through ServiceLocator
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        public void Initialize()
        {
            if (!characterAudioEvent.IsNull)
            {
                characterAudioInstance = AudioService.CreateAudioInstance(characterAudioEvent);
                // Initialize at Zone 0 (outside range)
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 0.0f);
            }

            // Show the marker if it was disabled before
            if (marker != null)
            {
                marker.gameObject.SetActive(true);
                Debug.Log($"Showing marker for {characterName}");
            }

            isInitialized = true;
            Debug.Log($"Audio initialized for {characterName}");
        }

        public void SetSharedCueInstance(EventInstance instance)
        {
            sharedCueInstance = instance;
        }

        // Main proximity update method
        public void UpdateProximity(float distance, Vector3 audioPosition)
        {
            if (!isInitialized) return;

            bool wasInProximity = isInProximity;
            isInProximity = (distance <= proximityRadius);

            if (isInProximity && !wasInProximity)
            {
                // Just entered proximity - start character audio
                AudioService.PlayAudio(characterAudioInstance, audioPosition);
                Debug.Log($"Entered proximity of {characterName}");
            }
            else if (!isInProximity && wasInProximity)
            {
                // Just left proximity - stop character audio completely
                AudioService.StopAudio(characterAudioInstance, true);
                hasBeenTriggered = false; // Reset portal trigger when leaving proximity
                Debug.Log($"Exited proximity of {characterName}");
            }

            if (isInProximity)
            {
                // Update audio position and zone continuously while in proximity
                AudioService.Update3DAttributes(characterAudioInstance, audioPosition);
                UpdateAudioBasedOnDistance(distance);

                // Check for portal activation if this is a portal POI
                if (IsPortal && distance <= dialogueRadius && !hasBeenTriggered)
                {
                    CheckPortalActivation();
                }
            }
        }

        // Calculate zone from distance
        private void UpdateAudioBasedOnDistance(float distance)
        {
            if (!isInitialized) return;

            // Calculate continuous zone value based on distance
            float zoneValue = CalculateZoneFromDistance(distance);

            // Single parameter update - smooth transitions
            AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, zoneValue);

            Debug.Log($"{characterName} - Distance: {distance:F1}m → Zone: {zoneValue:F2}");
        }

        // Convert distance to zone value
        private float CalculateZoneFromDistance(float distance)
        {
            if (distance > proximityRadius)
            {
                return 0.0f; // Outside proximity - silent
            }
            else if (distance > dialogueRadius)
            {
                // Smooth transition from outer zone (1.0) to dialogue zone (2.0)
                float t = 1.0f - ((distance - dialogueRadius) / (proximityRadius - dialogueRadius));
                return Mathf.Lerp(1.0f, 2.0f, t);
            }
            else
            {
                return 2.0f; // Full dialogue zone
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

        // In POI.cs - modify ActivatePortal method
        private void ActivatePortal(TimeLayer targetLayer)
        {
            hasBeenTriggered = true;

            Debug.Log($"{portalType} portal ({characterName}) activated - transitioning to {targetLayer.layerName}");

            // Play portal activation audio with parameters
            if (!portalActivationAudio.IsNull)
            {
                var portalInstance = AudioService.CreateAudioInstance(portalActivationAudio);

                // Set portal type parameter (1 = Forward/Raven, 2 = Backward/Fox)
                int portalTypeValue = portalType == PortalType.Forward ? 1 : 2;
                AudioService.SetParameter(portalInstance, "PortalType", portalTypeValue);

                // Set trigger to activate the sound
                AudioService.SetParameter(portalInstance, "Trigger", 1.0f);

                AudioService.PlayAudio(portalInstance, Vector3.zero);
            }

            // Trigger the time transition
            TimeLayerManager.Instance.TransitionToLayer(targetLayer);
        }

        // Navigation cue methods (for wander mode)
        public void PlayNavigationCue(Vector3 position, float distance, float maxDistance)
        {
            if (!isInitialized || isInProximity) return;

            // Calculate the cue variant based on normalized distance
            int cueVariant = CalculateCueVariant(distance, maxDistance);

            AudioService.PlayNavigationCue(sharedCueInstance, position, characterId, distance, isTargeted, cueVariant);
        }

        // helper funciton to determine normalized distance
        private int CalculateCueVariant(float distance, float maxDistance)
        {
            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);

            if (normalizedDistance <= 0.25f)
                return 1;
            else if (normalizedDistance <= 0.50f)
                return 2;
            else if (normalizedDistance <= 0.75f)
                return 3;
            else
                return 4;
        }

        // Targeting methods (for wander mode)
        public void SetAsTarget(Vector3 position)
        {
            isTargeted = true;
            // Visual feedback
            if (marker != null)
            {
                marker.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            }
        }

        public void ClearAsTarget()
        {
            isTargeted = false;
            // Reset visual feedback
            if (marker != null)
            {
                marker.transform.localScale = Vector3.one;
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

        // Cleanup
        public void Cleanup()
        {
            if (!isInitialized) return;

            AudioService.StopAudio(characterAudioInstance);
            AudioService.ReleaseAudio(characterAudioInstance);

            // remove the marker 
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
                Debug.Log($"Hiding marker for {characterName}");
            }
        }
    }
}