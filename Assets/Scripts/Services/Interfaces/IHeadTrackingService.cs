using System;
using System.Collections.Generic;

namespace LoGa.LudoEngine.Services
{
    public interface IHeadTrackingService : IService
    {
        event Action<float> HeadingUpdated;
        float CurrentHeading { get; }
        void StartTracking();
        void StopTracking();

        // Provider management
        string ActiveProviderName { get; }
        IReadOnlyList<string> AvailableProviderNames { get; }
        event Action<string> ActiveProviderChanged;

        // Spectator mode — inject player's heading instead of real sensor
        void InjectHeading(float heading);
        void ClearInjection();
    }
}