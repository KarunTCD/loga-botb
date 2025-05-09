using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadTrackingService : MonoBehaviour, IHeadTrackingService
{
    public event Action<float> HeadingUpdated;

    // All your existing configuration parameters
    [Header("Calibration Settings")]
    [SerializeField] private float calibrationThreshold = 15f;
    [SerializeField] private float calibrationLerpSpeed = 0.05f;
    [SerializeField] private int calibrationCheckInterval = 900;
    [SerializeField] private bool enablePeriodicCalibration = true;
    [SerializeField] private float compassStartupDelay = 3f;

    [Header("Sensor Fusion")]
    [SerializeField] private bool enableSensorFusion = true;
    [SerializeField] private float magneticDeclination = 3.5f;
    [SerializeField] private float headingSmoothingFactor = 0.1f;
    [SerializeField] private float headingNoiseThreshold = 2.0f;
    [SerializeField] private float stationaryNoiseThreshold = 0.5f; // Stricter threshold when stationary
    [SerializeField] private float minSmoothingFactor = 0.01f; // Faster response
    [SerializeField] private float maxSmoothingFactor = 0.1f;  // More smoothing
    [SerializeField] private float rotationThreshold = 1.0f;   // Degrees/second

    // Internal tracking variables
    private bool gyroEnabled = false;
    private bool compassEnabled = false;
    private bool accelerometerEnabled = false;
    private float currentAngle = 0f;
    private float markerAngle = 0f;
    private float trueNorthOffset = 0f;
    private float targetTrueNorthOffset = 0f;
    private bool isCalibrated = false;
    private Coroutine compassInitCoroutine;

    // Compass tracking
    private float lastCompassHeading = 0f;
    private bool hasValidCompassReading = false;
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

    public bool IsInitialized => gyroEnabled || compassEnabled;
    public bool IsCalibrated => isCalibrated;
    public float CurrentHeading => markerAngle;

    public void Initialize()
    {
        InitializeSensors();
        compassInitCoroutine = StartCoroutine(InitializeCompass());
        lastCalibrationTime = Time.time;
    }

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
            hasValidCompassReading = true;
            trueNorthOffset = compassHeading - currentAngle;
            targetTrueNorthOffset = trueNorthOffset;
            isCalibrated = true;

            Debug.Log($"Initial calibration complete. Offset: {trueNorthOffset}");
        }
        else
        {
            Debug.LogWarning("Compass not providing readings. Will need manual calibration.");
        }
    }

    public void StartTracking()
    {
        // Currently empty, tracking happens in Update
    }

    public void StopTracking()
    {
        // Currently empty
    }

    private void Update()
    {
        // Skip if no gyro is available
        if (!gyroEnabled) return;

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
            targetHeading = BlendAngles(currentAngle, adjustedCompassHeading, 0.02f);
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

                Debug.Log($"Drift correction: {currentDrift:F1}°. " +
                         $"Current: {markerAngle:F1}°, Compass: {compassHeading:F1}°");
            }
        }
    }

    // Helper method to blend angles properly
    private float BlendAngles(float angle1, float angle2, float weight2)
    {
        float weight1 = 1.0f - weight2;

        float x = weight1 * Mathf.Cos(angle1 * Mathf.Deg2Rad) + weight2 * Mathf.Cos(angle2 * Mathf.Deg2Rad);
        float y = weight1 * Mathf.Sin(angle1 * Mathf.Deg2Rad) + weight2 * Mathf.Sin(angle2 * Mathf.Deg2Rad);

        return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
    }

    private bool IsDeviceStationary()
    {
        if (!accelerometerEnabled) return true;

        // Check if the device is relatively still
        float accelerationMagnitude = rawAcceleration.magnitude;
        return Mathf.Abs(accelerationMagnitude - 1f) < 0.1f;
    }

    // Public method for manual calibration
    public void CalibrateToNorth()
    {
        if (compassEnabled && Input.compass.trueHeading != 0)
        {
            // Use compass for calibration
            float compassHeading = (Input.compass.trueHeading + magneticDeclination + 360f) % 360f;

            targetTrueNorthOffset = (compassHeading - currentAngle + 360f) % 360f;

            // Immediate transition for manual calibration
            trueNorthOffset = targetTrueNorthOffset;
            calibrationLerpFactor = 1f;
            isCalibrated = true;

            // Reset drift tracking
            totalRotationSinceCalibration = 0f;
            lastCalibrationTime = Time.time;

            Debug.Log($"Manual calibration using compass. Heading: {compassHeading:F1}°, Offset: {trueNorthOffset:F1}°");
        }
        else
        {
            // Manual calibration - set current direction as north
            currentAngle = 0f;
            trueNorthOffset = 0f;
            targetTrueNorthOffset = 0f;
            calibrationLerpFactor = 1f;
            isCalibrated = true;

            // Reset drift tracking
            totalRotationSinceCalibration = 0f;
            lastCalibrationTime = Time.time;

            Debug.Log("Manual calibration - current direction set as north");
        }
    }

    // Utility method to manually set direction (for testing or landmarks)
    public void SetDirectionDegrees(float degrees)
    {
        targetTrueNorthOffset = (degrees - currentAngle + 360f) % 360f;

        // Apply immediately for manual calibration
        trueNorthOffset = targetTrueNorthOffset;
        calibrationLerpFactor = 1f;
        isCalibrated = true;

        // Reset drift tracking
        totalRotationSinceCalibration = 0f;
        lastCalibrationTime = Time.time;

        Debug.Log($"Direction manually set to {degrees:F1}°. Offset: {trueNorthOffset:F1}°");
    }

    private void OnDisable()
    {
        if (compassInitCoroutine != null)
        {
            StopCoroutine(compassInitCoroutine);
        }

        Input.gyro.enabled = false;
        Input.compass.enabled = false;

        ServiceLocator.UnregisterService<IHeadTrackingService>();
    }

    // Helper methods 
    private float NormalizeAngle(float angle)
    {
        return (angle + 360f) % 360f;
    }
}