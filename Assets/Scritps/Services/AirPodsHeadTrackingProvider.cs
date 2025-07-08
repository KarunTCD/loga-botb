using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Services.Plugins.HearXR;

namespace LoGa.LudoEngine.Services
{
    /// <summary>
    /// AirPods Pro head tracking provider with simple compass drift correction
    /// Based on existing PhoneHeadTrackingProvider compass implementation
    /// </summary>
    public class AirPodsHeadTrackingProvider : MonoBehaviour, IHeadTrackingProvider
    {
        // Provider Identity
        public string ProviderName => "AirPods Pro (Spatial Audio)";
        public int Priority => 75;
        public bool IsAvailable => CheckAvailability();
        public bool IsConnected { get; private set; }
        public float CurrentHeading => markerAngle;

        [Header("AirPods Configuration")]
        [SerializeField] private bool enableConnectionMonitoring = true;
        [SerializeField] private float connectionCheckInterval = 2.0f;
        [SerializeField] private bool enableCalibrationOnConnect = true;
        [SerializeField] private float calibrationTimeout = 5.0f;

        [Header("Compass Drift Correction (from Phone Provider)")]
        [SerializeField] private bool enableCompassCorrection = true;
        [SerializeField] private float magneticDeclination = 3.5f;
        [SerializeField] private float calibrationThreshold = 5f;
        [SerializeField] private float calibrationLerpSpeed = 0.05f;
        [SerializeField] private int calibrationCheckInterval = 300;
        [SerializeField] private bool enablePeriodicCalibration = true;
        [SerializeField] private float compassStartupDelay = 3f;

        [Header("Rotation Processing")]
        [SerializeField] private bool enableSmoothing = true;
        [SerializeField] private float smoothingFactor = 0.1f;

        [Header("Debugging")]
        [SerializeField] private bool enableDetailedLogging = false;

        // Events
        public event Action<float> HeadingUpdated;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> StatusMessage;

        // 3D Orientation tracking
        public Quaternion CurrentOrientation { get; private set; } = Quaternion.identity;
        public Vector3 CurrentEulerAngles => CurrentOrientation.eulerAngles;

        // AirPods tracking variables
        private float currentHeading = 0f;
        private float markerAngle = 0f;
        private Quaternion rawRotation = Quaternion.identity;
        private Quaternion baseRotation = Quaternion.identity;
        private Quaternion smoothedRotation = Quaternion.identity;
        private bool isInitialized = false;
        private bool isTracking = false;
        private Coroutine connectionMonitorCoroutine;

        // Compass tracking variables (exactly like PhoneHeadTrackingProvider)
        private bool compassEnabled = false;
        private float trueNorthOffset = 0f;
        private float targetTrueNorthOffset = 0f;
        private float lastCompassHeading = 0f;
        private float calibrationLerpFactor = 1f;
        private float lastCalibrationTime = 0f;
        private Coroutine compassInitCoroutine;

        // Calibration state
        private bool isCalibrating = false;
        private float calibrationStartTime;
        private int calibrationSampleCount = 0;
        private Quaternion calibrationAccumulator = Quaternion.identity;
        private const int CALIBRATION_SAMPLES = 30;

        public Task<bool> InitializeAsync()
        {
            try
            {
                StatusMessage?.Invoke("Initializing AirPods head tracking...");

                // Initialize AirPods
                HeadphoneMotion.Init();

                if (!IsAvailable)
                {
                    StatusMessage?.Invoke("AirPods head tracking not available (requires iOS 14+ and AirPods Pro)");
                    return Task.FromResult(false);
                }

                UpdateConnectionStatus(false);

                // Subscribe to AirPods events
                HeadphoneMotion.OnHeadphoneConnectionChanged += OnHeadphoneConnectionChanged;
                HeadphoneMotion.OnHeadRotationQuaternion += OnHeadRotationReceived;

                // Initialize compass (exactly like PhoneHeadTrackingProvider)
                if (enableCompassCorrection)
                {
                    compassInitCoroutine = StartCoroutine(InitializeCompass());
                }

                // Start AirPods tracking
                HeadphoneMotion.StartTracking();
                isTracking = true;

                if (enableConnectionMonitoring)
                {
                    connectionMonitorCoroutine = StartCoroutine(MonitorConnection());
                }

                lastCalibrationTime = Time.time;
                isInitialized = true;
                StatusMessage?.Invoke("AirPods head tracking initialized successfully");

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"Failed to initialize AirPods provider: {e.Message}");
                Debug.LogError($"AirPods Provider Error: {e}");
                return Task.FromResult(false);
            }
        }

        public void StartTracking()
        {
            if (!isInitialized) return;

            try
            {
                HeadphoneMotion.StartTracking();
                isTracking = true;

                if (enableCalibrationOnConnect && IsConnected)
                {
                    StartCalibration();
                }

                StatusMessage?.Invoke("Started AirPods head tracking");
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"Failed to start tracking: {e.Message}");
                Debug.LogError($"AirPods Start Tracking Error: {e}");
            }
        }

        public void StopTracking()
        {
            try
            {
                HeadphoneMotion.StopTracking();
                isTracking = false;
                StatusMessage?.Invoke("Stopped AirPods head tracking");
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"Error stopping tracking: {e.Message}");
                Debug.LogError($"AirPods Stop Tracking Error: {e}");
            }
        }

        public void CalibrateToHeading(float targetHeading)
        {
            if (!IsConnected) return;

            // Use compass heading if available, otherwise use target
            if (enableCompassCorrection && compassEnabled && Input.compass.trueHeading != 0)
            {
                float compassHeading = (Input.compass.trueHeading + magneticDeclination + 360f) % 360f;
                float airpodsHeading = currentHeading;
                trueNorthOffset = compassHeading - airpodsHeading;
                targetTrueNorthOffset = trueNorthOffset;

                StatusMessage?.Invoke($"Calibrated to compass: {compassHeading:F1}°");
                if (enableDetailedLogging)
                    Debug.Log($"AirPods calibrated to compass - Offset: {trueNorthOffset:F1}°");
            }
            else
            {
                // Manual calibration to target heading
                baseRotation = rawRotation;
                StatusMessage?.Invoke($"AirPods calibrated to current orientation");
            }
        }

        public void Cleanup()
        {
            StopTracking();

            if (connectionMonitorCoroutine != null)
            {
                StopCoroutine(connectionMonitorCoroutine);
                connectionMonitorCoroutine = null;
            }

            if (compassInitCoroutine != null)
            {
                StopCoroutine(compassInitCoroutine);
                compassInitCoroutine = null;
            }

            if (compassEnabled)
            {
                Input.compass.enabled = false;
                compassEnabled = false;
            }

            if (isInitialized)
            {
                try
                {
                    HeadphoneMotion.OnHeadRotationQuaternion -= OnHeadRotationReceived;
                    HeadphoneMotion.OnHeadphoneConnectionChanged -= OnHeadphoneConnectionChanged;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error during AirPods cleanup: {e.Message}");
                }
            }

            isInitialized = false;
            UpdateConnectionStatus(false);
        }

        // -----------------------------------------------
        // Compass Initialization (exactly like PhoneHeadTrackingProvider)
        // -----------------------------------------------

        private IEnumerator InitializeCompass()
        {
            Debug.Log("Initializing compass for AirPods drift correction...");

            Input.compass.enabled = true;
            compassEnabled = true;

            // Wait for compass to start (exactly like phone provider)
            for (int i = 0; i < 3; i++)
            {
                Debug.Log($"Compass enabled: {Input.compass.enabled}, Heading: {Input.compass.trueHeading}");
                yield return new WaitForSeconds(1f);
            }

            // Wait for compass to stabilize
            yield return new WaitForSeconds(compassStartupDelay);

            float compassHeading = Input.compass.trueHeading;
            Debug.Log($"Initial compass heading: {compassHeading}");

            if (compassHeading != 0)
            {
                lastCompassHeading = compassHeading;
                trueNorthOffset = compassHeading - currentHeading;
                targetTrueNorthOffset = trueNorthOffset;
                Debug.Log($"Initial compass calibration complete. Offset: {trueNorthOffset}");
            }
            else
            {
                Debug.LogWarning("Compass not providing readings. Will need manual calibration.");
            }
        }

        // -----------------------------------------------
        // Core Rotation Processing with Compass Correction
        // -----------------------------------------------

        private void OnHeadRotationReceived(Quaternion rotation)
        {
            if (!isTracking) return;

            rawRotation = rotation;

            if (isCalibrating)
            {
                ProcessCalibrationSample(rotation);
                return;
            }

            // Apply base rotation (calibration offset)
            Quaternion correctedRotation;
            if (baseRotation == Quaternion.identity)
            {
                correctedRotation = rotation;
            }
            else
            {
                correctedRotation = rotation * Quaternion.Inverse(baseRotation);
            }

            // Apply smoothing
            if (enableSmoothing)
            {
                smoothedRotation = Quaternion.Lerp(smoothedRotation, correctedRotation, smoothingFactor);
                CurrentOrientation = smoothedRotation;
            }
            else
            {
                CurrentOrientation = correctedRotation;
            }

            // Extract heading from Y rotation
            currentHeading = CurrentOrientation.eulerAngles.y;
            currentHeading = (currentHeading + 360f) % 360f;

            // Apply compass calibration (exactly like PhoneHeadTrackingProvider)
            markerAngle = (currentHeading + trueNorthOffset + 360f) % 360f;

            HeadingUpdated?.Invoke(markerAngle);

            if (enableDetailedLogging && Time.frameCount % 60 == 0)
            {
                Debug.Log($"AirPods Heading: {markerAngle:F1}° (raw: {currentHeading:F1}°, offset: {trueNorthOffset:F1}°)");
            }
        }

        private void Update()
        {
            if (!isInitialized || !isTracking) return;

            // Compass calibration logic (exactly like PhoneHeadTrackingProvider)
            if (enableCompassCorrection && enablePeriodicCalibration &&
                Time.frameCount % calibrationCheckInterval == 0 && compassEnabled)
            {
                PerformCompassCalibration(true);
            }

            // Apply smooth calibration if in progress (exactly like PhoneHeadTrackingProvider)
            if (calibrationLerpFactor < 1f)
            {
                calibrationLerpFactor += calibrationLerpSpeed;
                if (calibrationLerpFactor > 1f) calibrationLerpFactor = 1f;

                trueNorthOffset = Mathf.Lerp(trueNorthOffset, targetTrueNorthOffset, calibrationLerpFactor);
            }
        }

        // -----------------------------------------------
        // Compass Calibration (exactly like PhoneHeadTrackingProvider)
        // -----------------------------------------------

        private void PerformCompassCalibration(bool checkThreshold)
        {
            float compassHeading = Input.compass.trueHeading;

            // Only consider valid readings
            if (compassHeading != 0 && Mathf.Abs(compassHeading - lastCompassHeading) < 45f)
            {
                lastCompassHeading = compassHeading;

                // Adjust heading for magnetic declination
                compassHeading = (compassHeading + magneticDeclination + 360f) % 360f;

                // Calculate what the offset should be
                float newOffset = (compassHeading - currentHeading + 360f) % 360f;

                // Calculate current drift
                float currentDrift = Mathf.Abs(Mathf.DeltaAngle(markerAngle, compassHeading));

                // Apply calibration if drift exceeds threshold or if forced
                if (!checkThreshold || currentDrift > calibrationThreshold)
                {
                    // Begin smooth calibration
                    targetTrueNorthOffset = newOffset;
                    calibrationLerpFactor = 0f; // Start transition

                    if (enableDetailedLogging)
                        Debug.Log($"Compass drift correction: {currentDrift:F1}°. " +
                                 $"Current: {markerAngle:F1}°, Compass: {compassHeading:F1}°");
                }
            }
        }

        // -----------------------------------------------
        // Standard Provider Methods
        // -----------------------------------------------

        private bool CheckAvailability()
        {
            try
            {
                return HeadphoneMotion.IsHeadphoneMotionAvailable();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnHeadphoneConnectionChanged(bool connected)
        {
            UpdateConnectionStatus(connected);

            if (connected && enableCalibrationOnConnect && isTracking)
            {
                Invoke(nameof(StartCalibration), 0.5f);
            }
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (IsConnected != connected)
            {
                IsConnected = connected;
                ConnectionStatusChanged?.Invoke(connected);

                string status = connected ? "AirPods connected with compass correction" : "AirPods disconnected";
                StatusMessage?.Invoke(status);
            }
        }

        private System.Collections.IEnumerator MonitorConnection()
        {
            while (isInitialized)
            {
                yield return new WaitForSeconds(connectionCheckInterval);

                try
                {
                    bool currentConnection = HeadphoneMotion.AreHeadphonesConnected();
                    if (currentConnection != IsConnected)
                    {
                        UpdateConnectionStatus(currentConnection);
                    }
                }
                catch (Exception e)
                {
                    if (enableDetailedLogging)
                        Debug.LogWarning($"Connection monitoring error: {e.Message}");
                }
            }
        }

        // -----------------------------------------------
        // Calibration Methods
        // -----------------------------------------------

        private void StartCalibration()
        {
            if (isCalibrating) return;

            isCalibrating = true;
            calibrationStartTime = Time.time;
            calibrationSampleCount = 0;
            calibrationAccumulator = Quaternion.identity;

            StatusMessage?.Invoke("Starting AirPods calibration...");
        }

        private void ProcessCalibrationSample(Quaternion rotation)
        {
            calibrationSampleCount++;

            if (calibrationSampleCount == 1)
            {
                calibrationAccumulator = rotation;
            }
            else
            {
                calibrationAccumulator = Quaternion.Lerp(calibrationAccumulator, rotation, 1f / calibrationSampleCount);
            }

            if (calibrationSampleCount >= CALIBRATION_SAMPLES ||
                (Time.time - calibrationStartTime) > calibrationTimeout)
            {
                CompleteCalibration();
            }
        }

        private void CompleteCalibration()
        {
            isCalibrating = false;
            baseRotation = calibrationAccumulator;
            StatusMessage?.Invoke($"AirPods calibration complete ({calibrationSampleCount} samples)");
        }

        // -----------------------------------------------
        // Debug Methods
        // -----------------------------------------------

        public void TestConnection()
        {
            try
            {
                bool airpodsAvailable = HeadphoneMotion.IsHeadphoneMotionAvailable();
                bool airpodsConnected = HeadphoneMotion.AreHeadphonesConnected();
                bool compassWorking = compassEnabled && Input.compass.enabled;

                StatusMessage?.Invoke($"AirPods: {airpodsConnected}, Compass: {compassWorking}");

                if (enableDetailedLogging)
                {
                    Debug.Log($"=== AirPods + Compass Test ===");
                    Debug.Log($"AirPods Available: {airpodsAvailable}");
                    Debug.Log($"AirPods Connected: {airpodsConnected}");
                    Debug.Log($"Compass Enabled: {compassWorking}");
                    Debug.Log($"True North Offset: {trueNorthOffset:F1}°");
                    Debug.Log($"Current Heading: {markerAngle:F1}°");

                    if (compassWorking)
                    {
                        float compassHeading = Input.compass.trueHeading;
                        float correctedCompass = (compassHeading + magneticDeclination + 360f) % 360f;
                        float drift = Mathf.DeltaAngle(markerAngle, correctedCompass);
                        Debug.Log($"Compass: {compassHeading:F1}° (corrected: {correctedCompass:F1}°)");
                        Debug.Log($"Current Drift: {drift:F1}°");
                    }
                }
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"Test failed: {e.Message}");
                Debug.LogError($"Test failed: {e.Message}");
            }
        }

        public void ForceCalibration()
        {
            if (IsConnected && isTracking)
            {
                StartCalibration();
                StatusMessage?.Invoke("Manual calibration started");
            }
            else
            {
                StatusMessage?.Invoke("Cannot calibrate - AirPods not connected or not tracking");
            }
        }

        public void ResetBaseRotation()
        {
            baseRotation = Quaternion.identity;
            trueNorthOffset = 0f;
            targetTrueNorthOffset = 0f;
            StatusMessage?.Invoke("Reset AirPods rotation and compass offset");
        }
    }
}