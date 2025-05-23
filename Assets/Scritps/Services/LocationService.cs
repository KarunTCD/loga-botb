using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;

namespace LoGa.LudoEngine.Services
{
    public class LocationService : MonoBehaviour, ILocationService
    {
        public event Action<float, float> LocationUpdated;

        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }

        [Header("EKF Settings")]
        [SerializeField] private bool useEKF = true;
        [SerializeField] private float processNoisePosition = 0.1f;
        [SerializeField] private float processNoiseVelocity = 0.1f;
        [SerializeField] private float measurementNoiseGPS = 5.0f;
        [SerializeField] private float measurementNoiseAccel = 0.05f;
        [SerializeField] private float accelThreshold = 0.03f;
        // GPS accuracy thresholds for trust levels (tune these)
        float gpsAccuracyTrustThreshold = 5; // your original good accuracy threshold
        float gpsAccuracyPoorThreshold =  15f; // beyond this, GPS is very poor
        float accelScaleFactor = 0.000001f;

        // Current location data
        private float currentLat;
        private float currentLon;
        private float positionAccuracy;
        private Coroutine locationUpdateCoroutine;

        // EKF state variables and sensor data
        private Vector3 rawAcceleration;
        private Vector2 lastGPSPosition;
        private float lastGPSTime;
        private Vector2 ekfPosition;
        private Vector2 ekfVelocity;
        private Matrix4x4 ekfCovariance;
        private bool ekfInitialized = false;

        private IPermissionService PermissionService => ServiceLocator.GetService<IPermissionService>();

        public async Task<bool> InitializeAsync()
        {
            if (IsInitialized) return true;

            // First check if we have location permission
            PermissionService.CheckLocationPermission();

            if (!PermissionService.HasLocationPermission)
            {
                Debug.Log("Location permission not granted, requesting...");

                // Request permission and wait for the result
                TaskCompletionSource<bool> permissionTCS = new TaskCompletionSource<bool>();

                void PermissionResultHandler(bool result)
                {
                    PermissionService.LocationPermissionResult -= PermissionResultHandler;
                    permissionTCS.SetResult(result);
                }

                // Subscribe to permission result event
                PermissionService.LocationPermissionResult += PermissionResultHandler;

                // Request permission
                PermissionService.RequestLocationPermission();

                // Wait for permission result
                bool permissionGranted = await permissionTCS.Task;

                if (!permissionGranted)
                {
                    Debug.LogWarning("Location permission denied by user");
                    return false;
                }
            }

            // Check if location services are enabled
            if (!Input.location.isEnabledByUser)
            {
                Debug.LogWarning("Location services not enabled by user");
                return false;
            }

            // Initialize EKF covariance with high uncertainty
            ekfCovariance = MultiplyMatrixByScalar(Matrix4x4.identity, 100f);

            // Start location service
            Input.location.Start(0.1f, 0.1f);

            // Wait for location initialization
            TaskCompletionSource<bool> locationTCS = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForLocationInit(locationTCS));

            // Wait for initialization result
            bool initialized = await locationTCS.Task;
            return initialized;
        }

        private IEnumerator WaitForLocationInit(TaskCompletionSource<bool> tcs)
        {
            int maxWait = 20; // Wait up to 20 seconds

            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(1);
                maxWait--;
                Debug.Log($"Waiting for location services... {maxWait}s remaining");
            }

            if (maxWait <= 0)
            {
                Debug.LogError("Location services initialization timed out");
                tcs.SetResult(false);
                yield break;
            }

            if (Input.location.status == LocationServiceStatus.Failed)
            {
                Debug.LogError("Location services failed to initialize");
                tcs.SetResult(false);
                yield break;
            }

            if (Input.location.status == LocationServiceStatus.Running)
            {
                // Initialize with first location
                var locationData = Input.location.lastData;
                currentLat = locationData.latitude;
                currentLon = locationData.longitude;
                positionAccuracy = locationData.horizontalAccuracy;

                // Initialize EKF state
                ekfPosition = new Vector2(currentLat, currentLon);
                ekfVelocity = Vector2.zero;
                lastGPSPosition = ekfPosition;
                lastGPSTime = Time.time;
                ekfInitialized = true;

                IsInitialized = true;
                Debug.Log($"Location services initialized: {currentLat:F6}, {currentLon:F6}, accuracy: {positionAccuracy}m");
                tcs.SetResult(true);
            }
            else
            {
                tcs.SetResult(false);
            }
        }

        public void StartLocationUpdates()
        {
            if (!IsInitialized)
            {
                Debug.LogError("Location service not initialized");
                return;
            }

            StopLocationUpdates(); // Stop any existing updates

            locationUpdateCoroutine = StartCoroutine(UpdateLocationRoutine());
            IsRunning = true;
        }

        public void StopLocationUpdates()
        {
            if (locationUpdateCoroutine != null)
            {
                StopCoroutine(locationUpdateCoroutine);
                locationUpdateCoroutine = null;
            }

            IsRunning = false;
        }

        public Vector2 GetLastKnownLocation()
        {
            return new Vector2(currentLat, currentLon);
        }

        public float GetPositionAccuracy()
        {
            return positionAccuracy;
        }

        private IEnumerator UpdateLocationRoutine()
        {
            while (true)
            {
                UpdateLocation();
                yield return null; // Update every frame for maximum responsiveness
            }
        }

        private void UpdateLocation()
        {
            // Update acceleration data
            if (SystemInfo.supportsAccelerometer)
            {
                rawAcceleration = Input.acceleration;
            }

            bool hasNewGPS = false;
            Vector2 gpsPosition = Vector2.zero;

            // Check for new GPS data
            if (Input.location.status == LocationServiceStatus.Running)
            {
                var locationData = Input.location.lastData;
                positionAccuracy = locationData.horizontalAccuracy;

                gpsPosition = new Vector2(locationData.latitude, locationData.longitude);

                // Check if this is actually new GPS data
                if (Vector2.Distance(gpsPosition, lastGPSPosition) > 0.000001f) // Small threshold for numerical precision
                {
                    hasNewGPS = true;

                    // Calculate velocity if we have previous GPS position
                    if (Time.time > lastGPSTime)
                    {
                        float dt = Time.time - lastGPSTime;
                        Vector2 gpsVelocity = (gpsPosition - lastGPSPosition) / dt;

                        // Use GPS velocity to improve EKF
                        if (useEKF && ekfInitialized)
                        {
                            // Only update velocity if movement is significant (reduces noise)
                            if (gpsVelocity.magnitude > 0.0001f)
                            {
                                ekfVelocity = Vector2.Lerp(ekfVelocity, gpsVelocity, 0.3f);
                            }
                        }
                    }

                    lastGPSPosition = gpsPosition;
                    lastGPSTime = Time.time;
                }
            }

            // Apply Extended Kalman Filter if enabled
            if (useEKF && ekfInitialized)
            {
                UpdateEKF(hasNewGPS, gpsPosition);

                // Update current position from EKF
                currentLat = ekfPosition.x;
                currentLon = ekfPosition.y;
            }
            else if (hasNewGPS)
            {
                // Use raw GPS data if EKF not enabled
                currentLat = gpsPosition.x;
                currentLon = gpsPosition.y;
            }

            // Notify subscribers of location update
            if (hasNewGPS || (useEKF && ekfInitialized))
            {
                LocationUpdated?.Invoke(currentLat, currentLon);
            }
        }

        private void UpdateEKF(bool hasNewGPS, Vector2 gpsPosition)
        {
            float dt = Time.deltaTime;

            // ==================== PREDICTION STEP ====================

            // State transition matrix F (linear approximation)
            Matrix4x4 F = Matrix4x4.identity;
            F[0, 2] = dt; // Position.x += Velocity.x * dt
            F[1, 3] = dt; // Position.y += Velocity.y * dt

            // Predict state
            Vector2 predictedPosition = ekfPosition + ekfVelocity * dt;
            Vector2 predictedVelocity = ekfVelocity;

            // Process noise covariance Q
            Matrix4x4 Q = Matrix4x4.zero;
            Q[0, 0] = processNoisePosition * dt;
            Q[1, 1] = processNoisePosition * dt;
            Q[2, 2] = processNoiseVelocity * dt;
            Q[3, 3] = processNoiseVelocity * dt;

            // Predict covariance
            Matrix4x4 predictedCovariance = AddMatrix(MultiplyMatrix(MultiplyMatrix(F, ekfCovariance), TransposeMatrix(F)), Q);

            // ==================== UPDATE STEP ====================

            if (hasNewGPS)
            {
                // Determine GPS noise covariance based on accuracy:
                float gpsNoise;
                if (positionAccuracy <= gpsAccuracyTrustThreshold)
                {
                    // Good accuracy, trust GPS fully
                    gpsNoise = Mathf.Max(measurementNoiseGPS, positionAccuracy);
                }
                else if (positionAccuracy <= gpsAccuracyPoorThreshold)
                {
                    // Moderate accuracy, inflate noise moderately (e.g., 5x)
                    gpsNoise = Mathf.Max(measurementNoiseGPS * 5f, positionAccuracy * 5f);
                }
                else
                {
                    // Very poor accuracy, inflate noise heavily (e.g., 50x)
                    gpsNoise = Mathf.Max(measurementNoiseGPS * 50f, positionAccuracy * 50f);
                }

                // Measurement matrix H for GPS (observes position)
                Matrix4x4 H_gps = Matrix4x4.zero;
                H_gps[0, 0] = 1;
                H_gps[1, 1] = 1;

                // Measurement noise covariance R for GPS
                Matrix4x4 R_gps = Matrix4x4.zero;
                R_gps[0, 0] = gpsNoise;
                R_gps[1, 1] = gpsNoise;

                // Innovation (measurement residual)
                Vector2 innovation = gpsPosition - predictedPosition;

                // Innovation covariance
                Matrix4x4 S = AddMatrix(MultiplyMatrix(MultiplyMatrix(H_gps, predictedCovariance), TransposeMatrix(H_gps)), R_gps);

                // Kalman gain
                Matrix4x4 K = MultiplyMatrix(MultiplyMatrix(predictedCovariance, TransposeMatrix(H_gps)), InverseMatrix(S));

                // Update state
                Vector4 stateCorrection = MultiplyMatrixVector(K, new Vector4(innovation.x, innovation.y, 0, 0));
                predictedPosition.x += stateCorrection.x;
                predictedPosition.y += stateCorrection.y;
                predictedVelocity.x += stateCorrection.z;
                predictedVelocity.y += stateCorrection.w;

                // Update covariance
                Matrix4x4 I = Matrix4x4.identity;
                predictedCovariance = MultiplyMatrix(SubtractMatrix(I, MultiplyMatrix(K, H_gps)), predictedCovariance);
            }

            // ==================== ACCELEROMETER UPDATE ====================

            if (SystemInfo.supportsAccelerometer)
            {
                Vector2 worldAccel = new Vector2(rawAcceleration.x, rawAcceleration.z);

                if (worldAccel.magnitude > accelThreshold)
                {
                    Vector2 accelDegrees = worldAccel * accelScaleFactor;

                    Matrix4x4 H_accel = Matrix4x4.zero;
                    H_accel[0, 2] = 1;
                    H_accel[1, 3] = 1;

                    Matrix4x4 R_accel = Matrix4x4.zero;
                    R_accel[0, 0] = measurementNoiseAccel;
                    R_accel[1, 1] = measurementNoiseAccel;

                    Vector2 expectedVelocity = predictedVelocity;
                    Vector2 velocityInnovation = accelDegrees - expectedVelocity;

                    Matrix4x4 S_accel = AddMatrix(MultiplyMatrix(MultiplyMatrix(H_accel, predictedCovariance), TransposeMatrix(H_accel)), R_accel);
                    Matrix4x4 K_accel = MultiplyMatrix(MultiplyMatrix(predictedCovariance, TransposeMatrix(H_accel)), InverseMatrix(S_accel));

                    Vector4 accelCorrection = MultiplyMatrixVector(K_accel, new Vector4(velocityInnovation.x, velocityInnovation.y, 0, 0));
                    predictedPosition.x += accelCorrection.x;
                    predictedPosition.y += accelCorrection.y;
                    predictedVelocity.x += accelCorrection.z;
                    predictedVelocity.y += accelCorrection.w;

                    Matrix4x4 I = Matrix4x4.identity;
                    predictedCovariance = MultiplyMatrix(SubtractMatrix(I, MultiplyMatrix(K_accel, H_accel)), predictedCovariance);
                }
            }

            // Store updated state
            ekfPosition = predictedPosition;
            ekfVelocity = predictedVelocity;
            ekfCovariance = predictedCovariance;
        }

        // Matrix helper functions
        private Matrix4x4 MultiplyMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 result = Matrix4x4.zero;

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        result[i, j] += a[i, k] * b[k, j];
                    }
                }
            }

            return result;
        }

        private Matrix4x4 TransposeMatrix(Matrix4x4 m)
        {
            Matrix4x4 result = Matrix4x4.zero;

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    result[i, j] = m[j, i];
                }
            }

            return result;
        }

        private Matrix4x4 InverseMatrix(Matrix4x4 m)
        {
            // For 2x2 matrix inversion (since we only use 2x2 blocks effectively)
            float det = m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0];

            if (Mathf.Abs(det) < 0.0001f)
            {
                // Determinant close to zero, use regularization
                return MultiplyMatrixByScalar(Matrix4x4.identity, 0.01f);
            }

            Matrix4x4 result = Matrix4x4.zero;
            result[0, 0] = m[1, 1] / det;
            result[0, 1] = -m[0, 1] / det;
            result[1, 0] = -m[1, 0] / det;
            result[1, 1] = m[0, 0] / det;

            return result;
        }

        private Vector4 MultiplyMatrixVector(Matrix4x4 m, Vector4 v)
        {
            return m * v;
        }

        private Matrix4x4 AddMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 result = Matrix4x4.zero;

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    result[i, j] = a[i, j] + b[i, j];
                }
            }

            return result;
        }

        private Matrix4x4 SubtractMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 result = Matrix4x4.zero;

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    result[i, j] = a[i, j] - b[i, j];
                }
            }

            return result;
        }

        private Matrix4x4 MultiplyMatrixByScalar(Matrix4x4 matrix, float scalar)
        {
            Matrix4x4 result = Matrix4x4.zero;

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    result[i, j] = matrix[i, j] * scalar;
                }
            }

            return result;
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                StopLocationUpdates();
                if (IsInitialized)
                {
                    Input.location.Stop();
                    IsInitialized = false;
                }

                ServiceLocator.UnregisterService<ILocationService>();
            }
        }
    }
}