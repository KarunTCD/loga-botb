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

                // CRITICAL: FMOD expects RELATIVE paths from StreamingAssets root
                // NOT absolute paths
                string relativeMasterBankPath = Path.Combine("Sites", siteId, "Audio", "Master.bank");
                string relativeStringsBankPath = Path.Combine("Sites", siteId, "Audio", "Master.strings.bank");

                Debug.Log($"AudioService: Loading Master.bank with relative path: {relativeMasterBankPath}");

                // Load Master bank (FMOD will prepend StreamingAssets path automatically)
                FMODUnity.RuntimeManager.LoadBank(relativeMasterBankPath, true);
                loadedBankPaths.Add(relativeMasterBankPath);
                Debug.Log("AudioService: ✓ Master.bank loaded");


                // Load Master.strings bank
                Debug.Log($"AudioService: Loading Master.strings.bank with relative path: {relativeStringsBankPath}");
                FMODUnity.RuntimeManager.LoadBank(relativeStringsBankPath, true);
                loadedBankPaths.Add(relativeStringsBankPath);
                Debug.Log("AudioService: ✓ Master.strings.bank loaded");

                Debug.Log($"AudioService: Successfully loaded {loadedBankPaths.Count} banks for site: {siteId}");

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"AudioService: Failed to load banks for site {siteId}");
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
                    // Use the same relative path we used to load
                    FMODUnity.RuntimeManager.UnloadBank(bankPath);
                    Debug.Log($"AudioService: ✓ Unloaded bank: {Path.GetFileName(bankPath)}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"AudioService: Error unloading bank {Path.GetFileName(bankPath)}: {e.Message}");
                }
            }

            loadedBankPaths.Clear();
            Debug.Log("AudioService: All banks unloaded");
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

        //// <summary>
        /// Play navigation cue with 4 core parameters for binaural audio
        /// </summary>
        public void PlayNavigationCue(EventInstance instance, Vector3 position, int cueIndex, int direction, float normalizedDistance)
        {
            if (!IsInstanceValid(instance))
            {
                Debug.LogWarning("Cannot play navigation cue - invalid instance");
                return;
            }

            // Set 3D position
            Update3DAttributes(instance, position);

            // Set the 4 core parameters for binaural navigation
            instance.setParameterByName("CueIndex", cueIndex);
            instance.setParameterByName("Direction", direction);
            instance.setParameterByName("NormalizedDistance", normalizedDistance);
            instance.setParameterByName("Trigger", 1.0f);

            // Start the instance
            FMOD.RESULT result = instance.start();

            Debug.Log($"🎵 Navigation cue started: Cue={cueIndex}, Dir={direction}, Dist={normalizedDistance:F3}, Result={result}");
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

        #region Bus Control

        /// <summary>
        /// Pause an FMOD bus (e.g., bus:/Music, bus:/SFX)
        /// </summary>
        public void PauseBus(string busPath)
        {
            try
            {
                Bus bus = RuntimeManager.GetBus(busPath);

                if (bus.isValid())
                {
                    bus.setPaused(true);
                    Debug.Log($"AudioService: Paused bus: {busPath}");
                }
                else
                {
                    Debug.LogWarning($"AudioService: Bus {busPath} is not valid - cannot pause");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AudioService: Failed to pause bus {busPath}: {e.Message}");
            }
        }

        /// <summary>
        /// Resume an FMOD bus
        /// </summary>
        public void ResumeBus(string busPath)
        {
            try
            {
                Bus bus = RuntimeManager.GetBus(busPath);

                if (bus.isValid())
                {
                    bus.setPaused(false);
                    Debug.Log($"AudioService: Resumed bus: {busPath}");
                }
                else
                {
                    Debug.LogWarning($"AudioService: Bus {busPath} is not valid - cannot resume");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AudioService: Failed to resume bus {busPath}: {e.Message}");
            }
        }

        #endregion

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