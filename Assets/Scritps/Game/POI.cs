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
        // Basic POI data (unchanged)
        public string id;
        public string characterName;
        public float latitude;
        public float longitude;
        public RectTransform marker;
        public int characterId;
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

        public void PlayNavigationCue(Vector3 position, float distance)
        {
            if (!isInitialized || isInProximity) return;

            AudioService.PlayNavigationCue(sharedCueInstance, position, characterId, distance, isTargeted);
        }

        public void EnterProximity(Vector3 position)
        {
            if (!isInitialized || isInProximity) return;

            // When entering proximity of a POI stop the cue
            AudioService.StopNavigationCue(sharedCueInstance);

            // Start character audio in outer zone (Zone = 1)
            AudioService.PlayAudio(characterAudioInstance, position);
            AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 1.0f);

            isInProximity = true;

            Debug.Log($"Entered proximity of {characterName} (Outer Zone - Music)");
        }

        public void ExitProximity()
        {
            if (!isInitialized || !isInProximity) return;

            // Set to Zone 0 (outside range) which should fade out audio
            AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 0.0f);

            // Allow time for the fade out from Zone 1 to Zone 0
            // This requires a MonoBehaviour, will need to handle differently
            DelayedAudioStop(1.0f);

            isInProximity = false;

            Debug.Log($"Exited proximity of {characterName}");
        }

        // Since POI isn't a MonoBehaviour, we need a different way to handle delays
        private void DelayedAudioStop(float delay)
        {
            // Use a static coroutine runner or request through AudioService
            AudioService.StopAudioDelayed(characterAudioInstance, delay);
        }

        public void StartDialogue()
        {
            if (!isInitialized || !isInProximity) return;

            // Check if dialogue is already playing using parameter value
            if (AudioService.IsTrackPlaying(characterAudioInstance, ZONE_PARAMETER, 2.0f))
            {
                Debug.Log($"Dialogue already playing for {characterName}, not restarting");
                return;
            }

            // Check current playback state
            PLAYBACK_STATE playbackState;
            FMOD.RESULT result = characterAudioInstance.getPlaybackState(out playbackState);

            if (result != FMOD.RESULT.OK || playbackState == PLAYBACK_STATE.STOPPING)
            {
                Debug.Log($"Cannot start dialogue with {characterName} - invalid state: {playbackState}");
                return;
            }

            // Set to inner zone (Zone = 2) - this triggers music ducking and activates dialogue
            AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 2.0f);

            Debug.Log($"Started dialogue with {characterName} (Inner Zone - Dialogue Active)");
        }

        public void StopDialogue()
        {
            if (!isInitialized || !isInProximity) return;

            // Check if dialogue is playing using parameter check
            if (!AudioService.IsTrackPlaying(characterAudioInstance, ZONE_PARAMETER, 2.0f))
            {
                // Not in dialogue mode, nothing to stop
                return;
            }

            // Check playback state
            FMOD.Studio.PLAYBACK_STATE playbackState;
            FMOD.RESULT result = characterAudioInstance.getPlaybackState(out playbackState);

            // Only stop dialogue if not in a transitional state
            if (result == FMOD.RESULT.OK &&
                playbackState != FMOD.Studio.PLAYBACK_STATE.STARTING &&
                playbackState != FMOD.Studio.PLAYBACK_STATE.STOPPING)
            {
                // Return to outer zone (Zone = 1) - music returns to full volume, dialogue stops
                AudioService.SetParameter(characterAudioInstance, ZONE_PARAMETER, 1.0f);

                Debug.Log($"Stopped dialogue with {characterName} (Returned to Outer Zone - Music Only)");
            }
            else
            {
                Debug.Log($"Not stopping dialogue with {characterName} - transitional state: {playbackState}");
            }
        }

        public void UpdateAudio(Vector3 position)
        {
            if (!isInitialized) return;

            AudioService.Update3DAttributes(characterAudioInstance, position);
        }

        public void SetAsTarget(Vector3 position)
        {
            isTargeted = true;
            AudioService.SetParameter(sharedCueInstance, "Is_Target", 1.0f);

            // Visual feedback could be added here
            if (marker != null)
            {
                marker.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            }
        }

        public void ClearAsTarget()
        {
            isTargeted = false;
            AudioService.SetParameter(sharedCueInstance, "Is_Target", 0.0f);

            // Reset visual feedback
            if (marker != null)
            {
                marker.transform.localScale = Vector3.one;
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

        public void Cleanup()
        {
            if (!isInitialized) return;

            AudioService.StopAudio(characterAudioInstance);
            AudioService.ReleaseAudio(characterAudioInstance);
        }
    }
}