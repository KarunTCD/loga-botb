using System.Collections;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace LoGa.LudoEngine.Services
{
    public class AudioService : MonoBehaviour, IAudioService
    {
        private List<string> loadedBankPaths = new List<string>();
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

        // Load FMOD banks for a specific site
        public bool LoadBanksForSite(string siteId)
        {
            try
            {
                Debug.Log($"AudioService: Loading banks for site: {siteId}");

                // Unload any previously loaded banks
                UnloadAllBanks();

                // Build path to site's audio folder
                string audioFolderPath = Path.Combine(
                    Application.streamingAssetsPath,
                    "Sites",
                    siteId,
                    "Audio"
                );

                Debug.Log($"AudioService: Looking for banks in: {audioFolderPath}");

                // Verify folder exists
                if (!Directory.Exists(audioFolderPath))
                {
                    Debug.LogError($"AudioService: Audio folder not found: {audioFolderPath}");
                    return false;
                }

                // Load Master bank
                string masterBankPath = Path.Combine(audioFolderPath, "Master.bank");
                if (!File.Exists(masterBankPath))
                {
                    Debug.LogError($"AudioService: Master.bank not found at: {masterBankPath}");
                    return false;
                }

                Debug.Log($"AudioService: Loading Master.bank from: {masterBankPath}");
                FMODUnity.RuntimeManager.LoadBank(masterBankPath, true);
                loadedBankPaths.Add(masterBankPath);
                Debug.Log("AudioService: ✓ Master.bank loaded");

                // Load Master.strings bank
                string stringsBankPath = Path.Combine(audioFolderPath, "Master.strings.bank");
                if (File.Exists(stringsBankPath))
                {
                    Debug.Log($"AudioService: Loading Master.strings.bank");
                    FMODUnity.RuntimeManager.LoadBank(stringsBankPath, true);
                    loadedBankPaths.Add(stringsBankPath);
                    Debug.Log("AudioService: ✓ Master.strings.bank loaded");
                }
                else
                {
                    Debug.LogWarning($"AudioService: Master.strings.bank not found (optional)");
                }

                Debug.Log($"AudioService: ✅ Successfully loaded {loadedBankPaths.Count} banks for site: {siteId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"AudioService: ❌ Failed to load banks for site {siteId}");
                Debug.LogError($"Exception: {e.Message}");
                Debug.LogError($"Stack trace: {e.StackTrace}");
                return false;
            }
        }

        // Unload all the banks added
        public void UnloadAllBanks()
        {
            if (loadedBankPaths.Count == 0)
            {
                Debug.Log("AudioService: No banks to unload");
                return;
            }

            Debug.Log($"AudioService: Unloading {loadedBankPaths.Count} banks");

            foreach (string bankPath in loadedBankPaths)
            {
                try
                {
                    FMODUnity.RuntimeManager.UnloadBank(bankPath);
                    Debug.Log($"AudioService: ✓ Unloaded bank: {Path.GetFileName(bankPath)}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"AudioService: Error unloading bank {Path.GetFileName(bankPath)}: {e.Message}");
                }
            }

            loadedBankPaths.Clear();
            Debug.Log("AudioService: ✅ All banks unloaded");
        }

        // Replace the ListAllParameters method in AudioService with this corrected version:
        public void ListAllParameters(EventInstance instance, string poiName)
        {
            if (!IsInstanceValid(instance)) return;

            EventDescription eventDesc;
            FMOD.RESULT result = instance.getDescription(out eventDesc);

            Debug.Log($"🔍 [{poiName}] Event description result: {result}");

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"Failed to get event description: {result}");
                return;
            }

            int paramCount;
            result = eventDesc.getParameterDescriptionCount(out paramCount);

            Debug.Log($"🔍 [{poiName}] Parameter count: {paramCount}, Result: {result}");

            // Try to access your known parameters directly
            TestParameterAccess(instance, poiName);
        }

        // Replace the test method with this corrected version:
        public void TestParameterAccess(EventInstance instance, string poiName)
        {
            Debug.Log($"🧪 Testing parameter access for {poiName}:");

            // Test common parameter names that might exist
            string[] testNames = { "Zone", "NarrationComplete", "zone", "narrationcomplete", "Trigger", "trigger" };

            foreach (string testName in testNames)
            {
                float value;
                FMOD.RESULT result = instance.getParameterByName(testName, out value);
                Debug.Log($"   Parameter '{testName}': Value={value}, Result={result}");
            }
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

        // Get parameter value from FMOD event instance
        public float GetParameter(EventInstance instance, string paramName)
        {
            if (!IsInstanceValid(instance)) return 0f;

            float value;
            FMOD.RESULT result = instance.getParameterByName(paramName, out value);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"Failed to get parameter '{paramName}': {result}");
                return 0f;
            }

            return value;
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

        /// <summary>
        /// FIXED: Enhanced instance validation with proper handle checking
        /// </summary>
        public bool IsInstanceValid(EventInstance instance)
        {
            // FIXED: First check if handle is valid (non-zero)
            if (instance.handle == IntPtr.Zero)
            {
                return false;
            }

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

        public void Reset()
        {
            Debug.Log("AudioService: Reset called");

            // Reset initialization flag only
            IsInitialized = false;
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