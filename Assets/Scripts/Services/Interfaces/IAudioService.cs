using UnityEngine;
using FMOD.Studio;
using FMODUnity;

namespace LoGa.LudoEngine.Services
{
    public interface IAudioService : IService
    {
        EventInstance CreateAudioInstance(EventReference eventRef);
        void PlayNavigationCue(EventInstance instance, Vector3 position, int cueIndex, int direction, float normalizedDistance);
        //void StopNavigationCue(EventInstance instance);
        void PlayAudio(EventInstance instance, Vector3 position);
        void StopAudio(EventInstance instance, bool allowFadeOut = true);
        void StopAudioDelayed(EventInstance instance, float delay);
        void ReleaseAudio(EventInstance instance);
        void PauseBus(string busPath);
        void ResumeBus(string busPath);
        void Update3DAttributes(EventInstance instance, Vector3 position);
        void SetParameter(EventInstance instance, string paramName, float value);
        bool IsInstanceValid(EventInstance instance);
        bool IsTrackPlaying(EventInstance instance, string parameterName, float parameterValue);
        float GetParameter(EventInstance instance, string paramName);
        void ListAllParameters(EventInstance instance, string poiName);
        // Multi-site bank management
        bool LoadBanksForSite(string siteId);
        void UnloadAllBanks();
    }
}