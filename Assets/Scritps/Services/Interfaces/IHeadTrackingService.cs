using System;

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
    }
}