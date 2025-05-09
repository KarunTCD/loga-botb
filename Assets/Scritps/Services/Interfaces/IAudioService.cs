using UnityEngine;
using FMOD.Studio;
using FMODUnity;

public interface IAudioService
{
    bool IsInitialized { get; }
    void Initialize();
    EventInstance CreateAudioInstance(EventReference eventRef);
    void PlayNavigationCue(EventInstance instance, Vector3 position, int characterId, float distance, bool isTargeted);
    void StopNavigationCue(EventInstance instance);
    void PlayAudio(EventInstance instance, Vector3 position);
    void StopAudio(EventInstance instance, bool allowFadeOut = true);
    void StopAudioDelayed(EventInstance instance, float delay);
    void ReleaseAudio(EventInstance instance);
    void Update3DAttributes(EventInstance instance, Vector3 position);
    void SetParameter(EventInstance instance, string paramName, float value);
    bool IsInstanceValid(EventInstance instance);
    bool IsTrackPlaying(EventInstance instance, string parameterName, float parameterValue);
}