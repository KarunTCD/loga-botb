using System;
using System.Collections.Generic;

namespace LoGa.LudoEngine.Services
{
    public interface IHeadTrackingService : IService
    {
        event Action<float> HeadingUpdated;
        bool IsCalibrated { get; }
        float CurrentHeading { get; }
        void StartTracking();
        void StopTracking();
        void CalibrateToNorth();
        void SetDirectionDegrees(float degrees);

        // Provider management
        string ActiveProviderName { get; }
        IReadOnlyList<string> AvailableProviderNames { get; }
        event Action<string> ActiveProviderChanged; // For debug and UI purposes
    }
}