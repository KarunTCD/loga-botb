using System.Collections;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;

public class AudioService : MonoBehaviour, IAudioService
{
    public bool IsInitialized { get; private set; }

    private EventInstance navigationCueInstance;

    public void Initialize()
    {
        // Any initialization needed
        IsInitialized = true;
        Debug.Log("Audio service initialized");
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

    // Play navigation cue with character ID
    public void PlayNavigationCue(EventInstance instance, Vector3 position, int characterId, float distance, bool isTargeted)
    {
        if (!IsInstanceValid(instance)) return;

        // Update 3D position
        Update3DAttributes(instance, position);

        // Set parameters
        instance.setParameterByName("Character_ID", characterId);
        instance.setParameterByName("Distance", distance);
        instance.setParameterByName("Is_Target", isTargeted ? 1.0f : 0.0f);

        // Set trigger parameter
        instance.setParameterByName("Trigger", 1.0f);
        instance.start();

        // Reset trigger parameter after a delay
        StartCoroutine(ResetTriggerAfterDelay(instance, "Trigger", 0.1f));
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

    // Set parameters on audio instance
    public void SetParameter(EventInstance instance, string paramName, float value)
    {
        if (!IsInstanceValid(instance)) return;

        instance.setParameterByName(paramName, value);
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
        // Cleanup any resources
        ServiceLocator.UnregisterService<IAudioService>();
    }
}