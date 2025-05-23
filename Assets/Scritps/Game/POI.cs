using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
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
        public float proximityRadius = 20f;   // Distance to start hearing character audio
        public float dialogueRadius = 10f;    // Distance to start hearing dialogue

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

        public bool IsDiscovered => isDiscovered;

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

            isInitialized = true;
            Debug.Log($"Audio initialized for {characterName}");
        }

        public void SetSharedCueInstance(EventInstance instance)
        {
            sharedCueInstance = instance;
        }

        // NEW: Main proximity update method
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
                Debug.Log($"Exited proximity of {characterName}");
            }

            if (isInProximity)
            {
                // Update audio position and zone continuously while in proximity
                AudioService.Update3DAttributes(characterAudioInstance, audioPosition);
                UpdateAudioBasedOnDistance(distance);
            }
        }

        // NEW: Calculate zone from distance
        private void UpdateAudioBasedOnDistance(float distance)
        {
            if (!isInitialized) return;

            // Calculate continuous zone value based on distance
            float zoneValue = CalculateZoneFromDistance(distance);

            // Single parameter update - smooth transitions
            AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, zoneValue);

            Debug.Log($"{characterName} - Distance: {distance:F1}m → Zone: {zoneValue:F2}");
        }

        // NEW: Convert distance to zone value
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

        // Navigation cue methods (for wander mode)
        public void PlayNavigationCue(Vector3 position, float distance)
        {
            if (!isInitialized || isInProximity) return;

            AudioService.PlayNavigationCue(sharedCueInstance, position, characterId, distance, isTargeted);
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
        }
    }
}