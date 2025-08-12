using System.Collections;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;
using System;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public class AudioService : MonoBehaviour, IAudioService
    {
        public bool IsInitialized { get; private set; }

        public Task<bool> InitializeAsync()
        {
            try
            {
                // Any initialization needed
                IsInitialized = true;
                Debug.Log("Audio service initialized");
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize audio service: {e.Message}");
                IsInitialized = false;
                return Task.FromResult(false);
            }
        }

        // Create and manage audio instances
        public EventInstance CreateAudioInstance(EventReference eventRef)
        {
            if (eventRef.IsNull)
            {
                Debug.LogWarning("Attempted to create audio instance with null event reference");
                return new EventInstance();
            }

            return RuntimeManager.CreateInstance(eventRef);
        }

        // Enhanced navigation cue method with manual cue index control
        public void PlayNavigationCue(EventInstance instance, Vector3 position, int characterId, float distance, bool isTargeted, float maxDistance, int cueIndex = 0)
        {
            if (!IsInstanceValid(instance)) return;

            // Update 3D position
            Update3DAttributes(instance, position);

            // Set existing parameters (keep all your current logic)
            instance.setParameterByName("Character_ID", characterId);
            instance.setParameterByName("Is_Target", isTargeted ? 1.0f : 0.0f);

            // IMPORTANT: Always set distance for volume automation (keep your existing behavior)
            UpdateDistanceBanding(instance, distance, maxDistance);

            // NEW: Always set cue index - code determines the value
            instance.setParameterByName("CueIndex", cueIndex);

            if (cueIndex > 0)
            {
                Debug.Log($"Sequential cue: Character {characterId}, CueIndex {cueIndex}, Distance: {distance:F1}m, Normalized: {distance / maxDistance:F3}");
            }
            else
            {
                Debug.Log($"Distance-based cue: Character {characterId}, Distance: {distance:F1}m, Normalized: {distance / maxDistance:F3}");
            }

            // Set trigger parameter (keep existing)
            instance.setParameterByName("Trigger", 1.0f);
            instance.start();

            // Reset trigger parameter after a delay (keep existing)
            StartCoroutine(ResetTriggerAfterDelay(instance, "Trigger", 0.1f));
        }

        // Original method for backward compatibility (your existing calls won't break)
        public void PlayNavigationCue(EventInstance instance, Vector3 position, int characterId, float distance, bool isTargeted, float maxDistance)
        {
            // Calculate cue index based on normalized distance for distance-based mode
            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
            int distanceBasedCueIndex = CalculateDistanceBasedCueIndex(normalizedDistance);

            // Call enhanced version with distance-based cue index
            PlayNavigationCue(instance, position, characterId, distance, isTargeted, maxDistance, distanceBasedCueIndex);
        }

        // Helper method to calculate cue index from normalized distance
        private int CalculateDistanceBasedCueIndex(float normalizedDistance)
        {
            // Map normalized distance (0-1) to cue indices (1-4)
            if (normalizedDistance <= 0.25f) return 1;      // Close
            else if (normalizedDistance <= 0.5f) return 2;  // Medium
            else if (normalizedDistance <= 0.75f) return 3; // Far
            else return 4;                                   // Very far
        }

        // Stop navigation cue by setting Character_ID to 0 (None)
        public void StopNavigationCue(EventInstance instance)
        {
            //if (!IsInstanceValid(instance)) return;

            // Setting to 0 ("None") will stop any playing sounds
            //instance.setParameterByName("Character_ID", 0);

        }

        // Play regular audio
        public void PlayAudio(EventInstance instance, Vector3 position)
        {
            if (!IsInstanceValid(instance)) return;

            Update3DAttributes(instance, position);

            // Check if already playing
            PLAYBACK_STATE playbackState;
            instance.getPlaybackState(out playbackState);

            if (playbackState != PLAYBACK_STATE.PLAYING)
            {
                instance.start();
            }
        }

        // Stop audio with optional fade out
        public void StopAudio(EventInstance instance, bool allowFadeOut = true)
        {
            if (!IsInstanceValid(instance)) return;

            FMOD.Studio.STOP_MODE stopMode = allowFadeOut ?
                FMOD.Studio.STOP_MODE.ALLOWFADEOUT :
                FMOD.Studio.STOP_MODE.IMMEDIATE;

            instance.stop(stopMode);
        }

        public void StopAudioDelayed(EventInstance instance, float delay)
        {
            StartCoroutine(StopAudioAfterDelay(instance, delay));
        }

        private IEnumerator StopAudioAfterDelay(EventInstance instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            StopAudio(instance, true);
        }

        // Clean up audio instance
        public void ReleaseAudio(EventInstance instance)
        {
            if (IsInstanceValid(instance))
            {
                instance.release();
            }
        }

        // Update 3D position for audio
        public void Update3DAttributes(EventInstance instance, Vector3 position)
        {
            if (!IsInstanceValid(instance)) return;

            instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
        }

        // Method to handle distance bands (KEEP EXACTLY AS IS for volume automation)
        public void UpdateDistanceBanding(EventInstance instance, float distance, float maxDistance)
        {
            if (!IsInstanceValid(instance)) return;

            // Use the passed maxDistance instead of hardcoded value
            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);

            // Set single distance parameter with linear interpolation preserved
            instance.setParameterByName("NormalizedDistance", normalizedDistance);

            Debug.Log($"Distance: {distance:F1}m → Normalized: {normalizedDistance:F3} (Max: {maxDistance:F0}m)");
        }

        // Set parameters on audio instance(for external use)
        public void SetParameter(EventInstance instance, string paramName, float value)
        {
            if (!IsInstanceValid(instance)) return;

            instance.setParameterByName(paramName, value);
        }

        // Add SetEventProperty method (was missing from your original)
        public void SetEventProperty(EventInstance instance, EVENT_PROPERTY property, float value)
        {
            if (!IsInstanceValid(instance)) return;

            instance.setProperty(property, value);
        }

        // Reset trigger parameter after delay
        private IEnumerator ResetTriggerAfterDelay(EventInstance instance, string parameterName, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (IsInstanceValid(instance))
            {
                instance.setParameterByName(parameterName, 0.0f);
            }
        }

        // Helper to check if instance is valid
        public bool IsInstanceValid(EventInstance instance)
        {
            // For FMOD Studio 2.02
            try
            {
                PLAYBACK_STATE state;
                FMOD.RESULT result = instance.getPlaybackState(out state);
                return result == FMOD.RESULT.OK;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // Helper to test if a track is still playing
        public bool IsTrackPlaying(EventInstance instance, string parameterName, float parameterValue)
        {
            if (!IsInstanceValid(instance)) return false;

            // Check current parameter value
            float currentValue;
            FMOD.RESULT result = instance.getParameterByName(parameterName, out currentValue);

            if (result != FMOD.RESULT.OK) return false;

            // Parameter values match - track is active
            return Mathf.Approximately(currentValue, parameterValue);
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                ServiceLocator.UnregisterService<IAudioService>();// Only unregister during actual application quit
            }
        }
    }
}