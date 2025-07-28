using System;
using System.Collections;
using UnityEngine;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public class PhoneHeadTrackingProvider : MonoBehaviour, IHeadTrackingProvider
    {
        // Provider Identity
        public string ProviderName => "Phone Sensors (Advanced)";
        public int Priority => 50; // A value lower than external sensors 
        public bool IsAvailable => SystemInfo.supportsGyroscope;
        public bool IsConnected { get; private set; }
        public float CurrentHeading => markerAngle;

        [Header("Calibration Settings")]
        [SerializeField] private float calibrationThreshold = 5f;
        [SerializeField] private float calibrationLerpSpeed = 0.05f;
        [SerializeField] private int calibrationCheckInterval = 300;
        [SerializeField] private bool enablePeriodicCalibration = true;
        [SerializeField] private float compassStartupDelay = 3f;

        [Header("Sensor Fusion")]
        [SerializeField] private bool enableSensorFusion = true;
        [SerializeField] private float magneticDeclination = 3.5f;
        [SerializeField] private float headingNoiseThreshold = 2.0f;
        [SerializeField] private float stationaryNoiseThreshold = 0.5f; // Stricter threshold when stationary
        [SerializeField] private float minSmoothingFactor = 0.01f; // Faster response
        [SerializeField] private float maxSmoothingFactor = 0.1f;  // More smoothing
        [SerializeField] private float rotationThreshold = 1.0f;   // Degrees/second

        // Events
        public event Action<float> HeadingUpdated;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> StatusMessage;

        // Internal tracking variables
        private bool gyroEnabled = false;
        private bool compassEnabled = false;
        private bool accelerometerEnabled = false;
        private float currentAngle = 0f;
        private float markerAngle = 0f;
        private float trueNorthOffset = 0f;
        private float targetTrueNorthOffset = 0f;
        private Coroutine compassInitCoroutine;

        // Compass tracking
        private float lastCompassHeading = 0f;
        private float calibrationLerpFactor = 1f; // 0 to 1

        // Sensor fusion variables
        private Vector3 rawAcceleration;
        private float gyroRotationRate;
        private float headingVelocity = 0f;
        private float headingStabilityTimer = 0f;

        // Path tracking and drift correction
        private float cumulativeRotation = 0f;
        private float totalRotationSinceCalibration = 0f;
        private float lastCalibrationTime = 0f;
        private bool isTrackingActive = false;

        // Enhanced 3D tracking - minimal implementation for phone
        public Quaternion CurrentOrientation => Quaternion.Euler(0, CurrentHeading, 0);
        public Vector3 CurrentEulerAngles => new Vector3(0, CurrentHeading, 0);

        public Task<bool> InitializeAsync()
        {
            try
            {
                StatusMessage?.Invoke("Initializing advanced phone sensors...");

                if (!IsAvailable)
                {
                    StatusMessage?.Invoke("Gyroscope not supported on this device");
                    return Task.FromResult(false);
                }

                // Your existing initialization logic
                InitializeSensors();

                if (gyroEnabled)
                {
                    compassInitCoroutine = StartCoroutine(InitializeCompass());
                    lastCalibrationTime = Time.time;

                    IsConnected = true;
                    ConnectionStatusChanged?.Invoke(true);

                    StatusMessage?.Invoke("Advanced phone sensors initialized with sensor fusion");
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"Failed to initialize: {e.Message}");
                return Task.FromResult(false);
            }
        }

        // ------------------------------------------

        public void StartTracking()
        {
            if (!IsConnected) return;

            isTrackingActive = true;
            StatusMessage?.Invoke("Started advanced head tracking with sensor fusion");
        }

        // ------------------------------------------

        public void StopTracking()
        {
            isTrackingActive = false;
            StatusMessage?.Invoke("Stopped advanced head tracking");
        }

        // -----------------------------------------------

        public void Update()
        {
            // Skip if provider is not available
            if (!IsAvailable) return;

            // Update sensor readings
            UpdateSensorData();

            if (enableSensorFusion && compassEnabled)
            {
                // Use sensor fusion for enhanced heading
                UpdateFusedHeading();
            }
            else
            {
                // Use standard gyro update
                UpdateGyroHeading();
            }

            // Apply current calibration
            markerAngle = (currentAngle + trueNorthOffset + 360f) % 360f;

            // Track total rotation for drift detection
            totalRotationSinceCalibration += Mathf.Abs(gyroRotationRate * Time.deltaTime * Mathf.Rad2Deg);

            // Force calibration after extended rotation or time
            bool shouldForceCalibrate =
                (totalRotationSinceCalibration > 720f) ||                // >2 full rotations
                (Time.time - lastCalibrationTime > 30f && compassEnabled); // >30 seconds

            if (shouldForceCalibrate && compassEnabled && Input.compass.trueHeading != 0)
            {
                PerformCompassCalibration(false); // false = less aggressive
                totalRotationSinceCalibration = 0f;
                lastCalibrationTime = Time.time;
            }

            // Regular calibration check
            if (enablePeriodicCalibration && Time.frameCount % calibrationCheckInterval == 0 && compassEnabled)
            {
                PerformCompassCalibration(true); // true = check against threshold
            }

            // Apply smooth calibration if in progress
            if (calibrationLerpFactor < 1f)
            {
                calibrationLerpFactor += calibrationLerpSpeed;
                if (calibrationLerpFactor > 1f) calibrationLerpFactor = 1f;

                trueNorthOffset = Mathf.Lerp(trueNorthOffset, targetTrueNorthOffset, calibrationLerpFactor);
            }

            // Add event for heading updates
            HeadingUpdated?.Invoke(markerAngle);
        }

        // ----------------------------------------------------

        public void CalibrateToHeading(float targetHeading)
        {
        }

        // ----------------------------------------------------

        public void Cleanup()
        {
            isTrackingActive = false;

            if (compassInitCoroutine != null)
            {
                StopCoroutine(compassInitCoroutine);
                compassInitCoroutine = null;
            }

            if (gyroEnabled)
            {
                Input.gyro.enabled = false;
                gyroEnabled = false;
            }

            if (compassEnabled)
            {
                Input.compass.enabled = false;
                compassEnabled = false;
            }

            IsConnected = false;
            ConnectionStatusChanged?.Invoke(false);
        }

        // ------------------------------------------------

        private void InitializeSensors()
        {
            // Initialize gyroscope
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                gyroEnabled = true;
                Debug.Log("Gyroscope enabled");
            }
            else
            {
                Debug.LogWarning("No gyroscope found on device");
            }

            // Initialize accelerometer
            if (SystemInfo.supportsAccelerometer)
            {
                accelerometerEnabled = true;
                Debug.Log("Accelerometer enabled");
            }
        }

        // -------------------------------------------

        private IEnumerator InitializeCompass()
        {
            Debug.Log("Initializing compass...");

            // Enable compass
            Input.compass.enabled = true;
            compassEnabled = true;

            // Wait for compass to start
            for (int i = 0; i < 3; i++)
            {
                Debug.Log($"Compass enabled: {Input.compass.enabled}, Heading: {Input.compass.trueHeading}");
                yield return new WaitForSeconds(1f);
            }

            // Wait a bit longer for compass to stabilize
            yield return new WaitForSeconds(compassStartupDelay);

            // Try to get initial calibration
            float compassHeading = Input.compass.trueHeading;
            Debug.Log($"Initial compass heading: {compassHeading}");

            if (compassHeading != 0)
            {
                // Initial calibration
                lastCompassHeading = compassHeading;
                trueNorthOffset = compassHeading - currentAngle;
                targetTrueNorthOffset = trueNorthOffset;

                Debug.Log($"Initial calibration complete. Offset: {trueNorthOffset}");
            }
            else
            {
                Debug.LogWarning("Compass not providing readings. Will need manual calibration.");
            }
        }

        // ------------------------------------------

        private void UpdateSensorData()
        {
            // Update acceleration data
            if (accelerometerEnabled)
            {
                rawAcceleration = Input.acceleration;
            }

            // Update gyroscope data
            if (gyroEnabled)
            {
                gyroRotationRate = Input.gyro.rotationRateUnbiased.y;
            }
        }

        // ------------------------------------------

        private void UpdateGyroHeading()
        {
            // Track cumulative rotation to prevent shortest-path issues
            cumulativeRotation -= gyroRotationRate * Time.deltaTime * Mathf.Rad2Deg;

            // Calculate new angle based on gyro rotation
            float newAngle = currentAngle - gyroRotationRate * Time.deltaTime * Mathf.Rad2Deg;

            // Use a stricter threshold when device is stationary
            float allowedThreshold = IsDeviceStationary() ? stationaryNoiseThreshold : headingNoiseThreshold;

            // Ignore minor changes (noise filtering)
            float deltaAngle = Mathf.Abs(newAngle - currentAngle);
            if (deltaAngle < allowedThreshold)
            {
                return; // Skip this update - likely just sensor noise
            }

            // Update current angle directly from cumulative rotation
            // This preserves direction of rotation without taking shortcuts
            currentAngle = newAngle;

            // Normalize angle
            currentAngle = (currentAngle + 360f) % 360f;
        }

        // ------------------------------------------
        private void UpdateFusedHeading()
        {
            float targetHeading;

            // Determine device stability state
            bool isStationary = IsDeviceStationary();

            if (isStationary)
            {
                // When stationary, gradually increase compass influence
                headingStabilityTimer += Time.deltaTime;
                float compassInfluence = Mathf.Clamp01(headingStabilityTimer / 3.0f); // Full influence after 3 seconds

                // Weighted average with increasing compass weight when stationary
                float compassWeight = Mathf.Lerp(0.05f, 0.2f, compassInfluence);
                targetHeading = BlendAngles(currentAngle, Input.compass.trueHeading + magneticDeclination, compassWeight);
            }
            else
            {
                // Reset stability timer when moving
                headingStabilityTimer = 0;

                // Track cumulative rotation
                cumulativeRotation -= gyroRotationRate * Time.deltaTime * Mathf.Rad2Deg;

                // Update current angle
                float newAngle = currentAngle - gyroRotationRate * Time.deltaTime * Mathf.Rad2Deg;
                currentAngle = (newAngle + 360f) % 360f;

                // Small compass correction to prevent drift (using adjusted compass heading)
                float adjustedCompassHeading = (Input.compass.trueHeading + magneticDeclination + 360f) % 360f;
                targetHeading = BlendAngles(currentAngle, adjustedCompassHeading, 0.05f);
            }

            // Calculate rotation speed (absolute value)
            float rotationSpeed = Mathf.Abs(gyroRotationRate * Mathf.Rad2Deg);

            // Adjust smoothing factor based on rotation speed
            // Fast rotation = less smoothing = quicker response
            float adaptiveSmoothingFactor = Mathf.Lerp(
                maxSmoothingFactor,  // More smoothing when slow/still
                minSmoothingFactor,  // Less smoothing when rotating quickly
                Mathf.Clamp01(rotationSpeed / rotationThreshold)
            );

            // Apply smoothing to reduce jitter
            currentAngle = Mathf.SmoothDampAngle(
                currentAngle,
                targetHeading,
                ref headingVelocity,
                adaptiveSmoothingFactor
            );

            // Normalize
            currentAngle = (currentAngle + 360f) % 360f;
        }

        // ------------------------------------------

        // New method to centralize calibration logic
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
                float newOffset = (compassHeading - currentAngle + 360f) % 360f;

                // Calculate current drift
                float currentDrift = Mathf.Abs(Mathf.DeltaAngle(markerAngle, compassHeading));

                // Apply calibration if drift exceeds threshold or if forced
                if (!checkThreshold || currentDrift > calibrationThreshold)
                {
                    // Begin smooth calibration
                    targetTrueNorthOffset = newOffset;
                    calibrationLerpFactor = 0f; // Start transition

                    //Debug.Log($"Drift correction: {currentDrift:F1}°. " +
                             //$"Current: {markerAngle:F1}°, Compass: {compassHeading:F1}°");
                }
            }
        }

        // ------------------------------------------
        // Helper method to blend angles properly
        private float BlendAngles(float angle1, float angle2, float weight2)
        {
            float weight1 = 1.0f - weight2;

            float x = weight1 * Mathf.Cos(angle1 * Mathf.Deg2Rad) + weight2 * Mathf.Cos(angle2 * Mathf.Deg2Rad);
            float y = weight1 * Mathf.Sin(angle1 * Mathf.Deg2Rad) + weight2 * Mathf.Sin(angle2 * Mathf.Deg2Rad);

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        // ------------------------------------------

        private bool IsDeviceStationary()
        {
            if (!accelerometerEnabled) return true;

            // Check if the device is relatively still
            float accelerationMagnitude = rawAcceleration.magnitude;
            return Mathf.Abs(accelerationMagnitude - 1f) < 0.1f;
        }

        // ------------------------------------------

        private float NormalizeAngle(float angle)
        {
            return (angle + 360f) % 360f;
        }
    }
}