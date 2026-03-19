using System;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Manages hardware initialization and service coordination
    /// Centralizes all hardware-related business logic
    /// </summary>
    public class HardwareManager : MonoBehaviour
    {
        #region Events

        /// <summary>
        /// Fired when hardware setup completes successfully
        /// </summary>
        public event Action OnSetupComplete;

        /// <summary>
        /// Fired when hardware setup fails
        /// </summary>
        public event Action<string> OnSetupFailed;

        /// <summary>
        /// Fired when status updates occur (for UI display)
        /// </summary>
        public event Action<string> OnStatusUpdate;

        /// <summary>
        /// Fired when a head tracking provider is detected
        /// </summary>
        public event Action<string> OnProviderDetected;

        #endregion

        #region Service References

        private ILocationService locationService;
        private IHeadTrackingService headTrackingService;

        #endregion

        #region State

        private bool isSetupInProgress = false;
        private bool servicesStarted = false;

        #endregion

        #region Properties

        /// <summary>
        /// Is LocationService currently running
        /// </summary>
        public bool IsLocationActive => locationService?.IsRunning ?? false;

        /// <summary>
        /// Is HeadTrackingService currently tracking
        /// </summary>
        public bool IsHeadTrackingActive
        {
            get
            {
                if (headTrackingService == null) return false;
                string provider = headTrackingService.ActiveProviderName;
                return !string.IsNullOrEmpty(provider) && provider != "None";
            }
        }

        /// <summary>
        /// Currently active head tracking provider name
        /// </summary>
        public string ActiveProvider => headTrackingService?.ActiveProviderName ?? "None";

        /// <summary>
        /// Are both services running
        /// </summary>
        public bool AreServicesRunning => IsLocationActive && IsHeadTrackingActive;

        #endregion

        #region Public API

        /// <summary>
        /// Begin hardware setup sequence
        /// Validates services, starts them, and detects hardware
        /// </summary>
        public async Task<bool> BeginSetup()
        {
            if (isSetupInProgress)
            {
                Debug.LogWarning("HardwareManager: Setup already in progress");
                return false;
            }

            isSetupInProgress = true;

            Debug.Log("HardwareManager: Beginning hardware setup");
            EmitStatus("Initializing hardware setup...");

            try
            {
                // Step 1: Validate services exist
                if (!ValidateServices())
                {
                    EmitFailure("Required services not available");
                    return false;
                }

                EmitStatus("Services validated successfully");
                await Task.Delay(500); // Brief delay for UI

                // Step 2: Initialize and start services
                bool servicesStarted = await StartServicesInternal();

                if (!servicesStarted)
                {
                    EmitFailure("Failed to start hardware services");
                    return false;
                }

                EmitStatus("Hardware services started");
                await Task.Delay(500); // Brief delay for UI

                // Step 3: Wait for hardware detection (headtracking provider)
                bool hardwareDetected = await WaitForHardwareDetection();

                if (!hardwareDetected)
                {
                    Debug.LogWarning("HardwareManager: No hardware detected - proceeding anyway");
                    EmitStatus("No external hardware detected - using phone sensors");
                }

                // Success!
                EmitStatus("Hardware setup complete");
                OnSetupComplete?.Invoke();

                Debug.Log("HardwareManager: Hardware setup completed successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"HardwareManager: Setup failed with exception - {e.Message}");
                EmitFailure($"Setup error: {e.Message}");
                return false;
            }
            finally
            {
                isSetupInProgress = false;
            }
        }

        /// <summary>
        /// Cancel ongoing setup
        /// </summary>
        public void CancelSetup()
        {
            Debug.Log("HardwareManager: Setup cancelled");
            isSetupInProgress = false;
        }

        /// <summary>
        /// Stop all hardware services
        /// Called when exiting gameplay
        /// </summary>
        public void StopServices()
        {
            Debug.Log("HardwareManager: Stopping services");

            try
            {
                if (locationService != null && locationService.IsRunning)
                {
                    locationService.StopLocationUpdates();
                    Debug.Log("HardwareManager: Location service stopped");
                }

                if (headTrackingService != null && IsHeadTrackingActive)
                {
                    headTrackingService.StopTracking();
                    Debug.Log("HardwareManager: Head tracking stopped");
                }

                servicesStarted = false;
                Debug.Log("HardwareManager: Services stopped successfully");
            }
            catch (Exception e)
            {
                Debug.LogError($"HardwareManager: Error stopping services - {e.Message}");
            }
        }

        /// <summary>
        /// Ensure services are running - restart if needed
        /// Called before entering gameplay to guarantee services are active
        /// </summary>
        public async Task<bool> EnsureServicesRunning()
        {
            Debug.Log("HardwareManager: Ensuring services are running");

            // Services should always be running after Hardware Setup
            // This is just a safety check
            if (AreServicesRunning)
            {
                Debug.Log("HardwareManager: Services confirmed running");
                return true;
            }

            // If services not running, something went wrong - restart them
            Debug.LogWarning("HardwareManager: Services not running - restarting (this shouldn't happen)");

            bool locationStarted = await StartLocationService();
            bool headTrackingStarted = await StartHeadTrackingService();

            if (locationStarted && headTrackingStarted)
            {
                Debug.Log("HardwareManager: Services restarted successfully");
                return true;
            }

            Debug.LogError("HardwareManager: Failed to restart services");
            return false;
        }

        /// <summary>
        /// Get current hardware status
        /// </summary>
        public HardwareSetupStatus GetStatus()
        {
            return new HardwareSetupStatus
            {
                locationActive = IsLocationActive,
                headTrackingActive = IsHeadTrackingActive,
                activeProvider = ActiveProvider,
                servicesRunning = AreServicesRunning
            };
        }

        #endregion

        #region Internal Implementation

        /// <summary>
        /// Validate that required services are available
        /// </summary>
        private bool ValidateServices()
        {
            Debug.Log("HardwareManager: Validating services");

            locationService = ServiceLocator.GetService<ILocationService>();
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();

            if (locationService == null)
            {
                Debug.LogError("HardwareManager: LocationService not available");
                return false;
            }

            if (headTrackingService == null)
            {
                Debug.LogError("HardwareManager: HeadTrackingService not available");
                return false;
            }

            Debug.Log("HardwareManager: Services validated");
            return true;
        }

        /// <summary>
        /// Start LocationService and HeadTrackingService
        /// </summary>
        private async Task<bool> StartServicesInternal()
        {
            Debug.Log("HardwareManager: Starting services");

            bool locationStarted = await StartLocationService();
            bool headTrackingStarted = await StartHeadTrackingService();

            servicesStarted = locationStarted && headTrackingStarted;

            return servicesStarted;
        }

        /// <summary>
        /// Start LocationService
        /// </summary>
        private async Task<bool> StartLocationService()
        {
            try
            {
                if (locationService.IsRunning)
                {
                    Debug.Log("HardwareManager: Location service already running");
                    return true;
                }

                Debug.Log("HardwareManager: Starting location service");
                locationService.StartLocationUpdates();

                // Wait briefly for initialization
                await Task.Delay(500);

                if (locationService.IsRunning)
                {
                    Debug.Log("HardwareManager: Location service started successfully");
                    return true;
                }
                else
                {
                    Debug.LogError("HardwareManager: Location service failed to start");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"HardwareManager: Error starting location service - {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start HeadTrackingService
        /// </summary>
        private async Task<bool> StartHeadTrackingService()
        {
            try
            {
                if (IsHeadTrackingActive)
                {
                    Debug.Log($"HardwareManager: Head tracking already active ({ActiveProvider})");
                    return true;
                }

                Debug.Log("HardwareManager: Starting head tracking");

                // Subscribe to provider change events
                headTrackingService.ActiveProviderChanged += OnHeadTrackingProviderChanged;

                // Start tracking
                headTrackingService.StartTracking();

                // Wait briefly for initialization
                await Task.Delay(500);

                if (IsHeadTrackingActive)
                {
                    Debug.Log($"HardwareManager: Head tracking started successfully ({ActiveProvider})");
                    return true;
                }
                else
                {
                    Debug.LogWarning("HardwareManager: Head tracking started but no provider active yet");
                    return true; // Not an error - provider may connect later
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"HardwareManager: Error starting head tracking - {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wait for hardware detection (headtracking provider connection)
        /// </summary>
        private async Task<bool> WaitForHardwareDetection()
        {
            Debug.Log("HardwareManager: Waiting for hardware detection");
            EmitStatus("Scanning for MMRL headphones...");

            float timeout = 12f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                if (IsHeadTrackingActive && ActiveProvider != "Phone")
                {
                    Debug.Log($"HardwareManager: External hardware detected - {ActiveProvider}");
                    EmitStatus($"Connected to {ActiveProvider}");
                    return true;
                }

                await Task.Delay(500);
                elapsed += 0.5f;
            }

            // Timeout - check if phone provider is active
            if (IsHeadTrackingActive && ActiveProvider == "Phone")
            {
                Debug.Log("HardwareManager: Using phone sensors (no external hardware)");
                return true; // Success - phone is valid
            }

            Debug.LogWarning("HardwareManager: Hardware detection timeout");
            return false;
        }

        /// <summary>
        /// Handle head tracking provider changes
        /// </summary>
        private void OnHeadTrackingProviderChanged(string providerName)
        {
            Debug.Log($"HardwareManager: Provider changed to {providerName}");
            OnProviderDetected?.Invoke(providerName);

            if (!string.IsNullOrEmpty(providerName) && providerName != "None")
            {
                EmitStatus($"Connected to {providerName}");
            }
        }

        /// <summary>
        /// Emit status update event
        /// </summary>
        private void EmitStatus(string status)
        {
            Debug.Log($"HardwareManager: {status}");
            OnStatusUpdate?.Invoke(status);
        }

        /// <summary>
        /// Emit failure event
        /// </summary>
        private void EmitFailure(string error)
        {
            Debug.LogError($"HardwareManager: {error}");
            OnSetupFailed?.Invoke(error);
        }

        #endregion

        #region Lifecycle

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (headTrackingService != null)
            {
                headTrackingService.ActiveProviderChanged -= OnHeadTrackingProviderChanged;
            }

            Debug.Log("HardwareManager: Destroyed");
        }

        #endregion

        #region Debug Methods

        [ContextMenu("Debug Hardware Status")]
        public void DebugStatus()
        {
            Debug.Log("=== Hardware Manager Status ===");
            Debug.Log($"Location Active: {IsLocationActive}");
            Debug.Log($"Head Tracking Active: {IsHeadTrackingActive}");
            Debug.Log($"Active Provider: {ActiveProvider}");
            Debug.Log($"Services Running: {AreServicesRunning}");
            Debug.Log($"Setup In Progress: {isSetupInProgress}");
        }

        #endregion
    }

    /// <summary>
    /// Hardware status data structure
    /// </summary>
    [System.Serializable]
    public struct HardwareSetupStatus
    {
        public bool locationActive;
        public bool headTrackingActive;
        public string activeProvider;
        public bool servicesRunning;
    }
}