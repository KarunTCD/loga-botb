using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LoGa.LudoEngine.Services
{
    public interface IHeadTrackingProvider
    {
        // Provider Identity
        string ProviderName { get; }
        int Priority { get; } // Higher = preferred (MMRL=100, Phone=50)

        // Availability & Connection
        bool IsAvailable { get; } // property to determine if provider can work on this device
        bool IsConnected { get; } // property to determine if provider currently connected/working

        // Events
        event Action<float> HeadingUpdated;
        event Action<bool> ConnectionStatusChanged;
        event Action<string> StatusMessage; // For debugging/UI

        // Lifecycle
        Task<bool> InitializeAsync();
        void StartTracking();
        void StopTracking();
        void Cleanup();

        // 3D Orientation (optional - only for MMRL)
        Quaternion CurrentOrientation { get; }
        Vector3 CurrentEulerAngles { get; }

        // 3D Orientation (optional - only for MMRL)
        //Quaternion CurrentOrientation { get; }
        //Vector3 CurrentEulerAngles { get; }

        // Advanced calibration
        void CalibrateToHeading(float targetHeading);

        // Data Access
        float CurrentHeading { get; }
    }
}