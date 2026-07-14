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
        // ONLY for actual location changes - not for game logic updates
        public event Action<Vector2> LocationChanged;
        public event Action<bool> LocationAvailabilityChanged;
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }

        [Header("GPS Accuracy Settings")]
        [SerializeField] private bool useKalmanFilter = true;
        [SerializeField] private float processNoise = 0.1f;
        [SerializeField] private float measurementNoise = 5.0f;
        [SerializeField] private float goodAccuracyThreshold = 5f;
        [SerializeField] private float poorAccuracyThreshold = 25f;

        [Header("GPS Update Settings - ONLY affects GPS accuracy")]
        [SerializeField] private float gpsUpdateInterval = 0.5f;
        [SerializeField] private float significantMoveThreshold = 1.0f;

        // PUBLIC PROPERTIES
        public Vector2 CurrentLocation { get; private set; }
        public float CurrentLatitude => CurrentLocation.x;
        public float CurrentLongitude => CurrentLocation.y;
        public bool IsLocationAvailable { get; private set; }
        public float PositionAccuracy { get; private set; }

        // Internal GPS tracking
        private Coroutine locationUpdateCoroutine;
        private Vector2 lastSignificantLocation;
        private float lastUpdateTime;

        // Kalman Filter state
        private SimpleKalmanFilter latFilter;
        private SimpleKalmanFilter lonFilter;
        private bool filtersInitialized = false;

        // Spectator injection
        private bool isInjectionActive = false;
        private Vector2 injectedLocation;

        private IPermissionService PermissionService => ServiceLocator.GetService<IPermissionService>();

        public async Task<bool> InitializeAsync()
        {
            if (IsInitialized) return true;

            // Check permissions
            PermissionService.CheckLocationPermission();

            if (!PermissionService.HasLocationPermission)
            {
                Debug.Log("Location permission not granted, requesting...");

                TaskCompletionSource<bool> permissionTCS = new TaskCompletionSource<bool>();

                void PermissionResultHandler(bool result)
                {
                    PermissionService.LocationPermissionResult -= PermissionResultHandler;
                    permissionTCS.SetResult(result);
                }

                PermissionService.LocationPermissionResult += PermissionResultHandler;
                PermissionService.RequestLocationPermission();

                bool permissionGranted = await permissionTCS.Task;

                if (!permissionGranted)
                {
                    Debug.LogWarning("Location permission denied by user");
                    return false;
                }
            }

            if (!Input.location.isEnabledByUser)
            {
                Debug.LogWarning("Location services not enabled by user");
                return false;
            }

            // Start location service with high accuracy
            Input.location.Start(1f, 1f);

            // Wait for location initialization
            TaskCompletionSource<bool> locationTCS = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForLocationInit(locationTCS));

            bool initialized = await locationTCS.Task;
            return initialized;
        }

        private IEnumerator WaitForLocationInit(TaskCompletionSource<bool> tcs)
        {
            int maxWait = 20;

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
                CurrentLocation = new Vector2(locationData.latitude, locationData.longitude);
                lastSignificantLocation = CurrentLocation;
                PositionAccuracy = locationData.horizontalAccuracy;
                IsLocationAvailable = true;

                // Initialize Kalman filters
                if (useKalmanFilter)
                {
                    latFilter = new SimpleKalmanFilter(CurrentLocation.x, processNoise, measurementNoise);
                    lonFilter = new SimpleKalmanFilter(CurrentLocation.y, processNoise, measurementNoise);
                    filtersInitialized = true;
                    Debug.Log("Kalman filters initialized");
                }

                IsInitialized = true;
                lastUpdateTime = Time.time;

                // Trigger initial availability event
                LocationAvailabilityChanged?.Invoke(true);

                Debug.Log($"Location services initialized: {CurrentLocation.x:F8}, {CurrentLocation.y:F8}, accuracy: {PositionAccuracy:F1}m");
                // AUTO-START location updates (like HeadTracking auto-starts)
                StartLocationUpdates();
                Debug.Log("LocationService: Auto-started location updates after initialization");
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

            StopLocationUpdates();
            locationUpdateCoroutine = StartCoroutine(UpdateLocationRoutine());
            IsRunning = true;
            Debug.Log("Location updates started");
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

        // PUBLIC ACCESS METHODS
        public Vector2 GetCurrentLocation()
        {
            return CurrentLocation;
        }

        public bool IsLocationReady()
        {
            return IsInitialized && IsLocationAvailable;
        }

        public float GetPositionAccuracy()
        {
            return PositionAccuracy;
        }

        #region Spectator Injection

        /// <summary>
        /// SpectatorManager calls this every frame to override GPS with the player's position.
        /// POIManager polls GetCurrentLocation() and gets the injected value transparently.
        /// </summary>
        public void InjectLocation(Vector2 location)
        {
            injectedLocation = location;
            if (!isInjectionActive)
            {
                isInjectionActive = true;
                Debug.Log("LocationService: Injection mode activated.");
            }
            // Override CurrentLocation so all consumers (POIManager etc.) see player position
            CurrentLocation = injectedLocation;
        }

        public void ClearInjection()
        {
            isInjectionActive = false;
            Debug.Log("LocationService: Injection cleared.");
        }

        #endregion

        // ONLY for GPS accuracy, not game timing
        private IEnumerator UpdateLocationRoutine()
        {
            while (true)
            {
                UpdateGPSLocation();
                CheckForThreeFingerTap();
                yield return new WaitForSeconds(gpsUpdateInterval);
            }
        }

        private void CheckForThreeFingerTap()
        {
            if (Input.touchCount == 3)
            {
                bool allTouches = true;

                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.GetTouch(i).phase != TouchPhase.Began)
                    {
                        allTouches = false;
                        break;
                    }
                }

                if (allTouches)
                {
                    ToggleKalmanFilter();
                }
            }
        }

        private void UpdateGPSLocation()
        {
            // Spectator mode — do not overwrite injected location with real GPS
            if (isInjectionActive) return;

            if (Input.location.status != LocationServiceStatus.Running)
            {
                return;
            }

            var locationData = Input.location.lastData;
            Vector2 rawGpsPosition = new Vector2(locationData.latitude, locationData.longitude);
            float rawAccuracy = locationData.horizontalAccuracy;

            float deltaTime = Time.time - lastUpdateTime;
            Vector2 newLocation;

            if (useKalmanFilter && filtersInitialized && deltaTime > 0)
            {
                // Apply Kalman filtering
                float dynamicNoise = CalculateDynamicNoise(rawAccuracy);
                float newLat = latFilter.Update(rawGpsPosition.x, dynamicNoise, deltaTime);
                float newLon = lonFilter.Update(rawGpsPosition.y, dynamicNoise, deltaTime);
                newLocation = new Vector2(newLat, newLon);

                if (Time.frameCount % 300 == 0)
                {
                    Debug.Log($"Kalman GPS Update: {newLocation.x:F8}, {newLocation.y:F8}, Accuracy: {rawAccuracy:F1}m");
                }
            }
            else
            {
                newLocation = rawGpsPosition;
            }

            // Update current location (always available for polling)
            CurrentLocation = newLocation;
            PositionAccuracy = rawAccuracy;
            lastUpdateTime = Time.time;

            // Only trigger LocationChanged event for SIGNIFICANT movement
            float distanceMoved = GetDistanceMeters(newLocation, lastSignificantLocation);

            if (distanceMoved >= significantMoveThreshold)
            {
                lastSignificantLocation = newLocation;
                LocationChanged?.Invoke(newLocation);
            }
        }

        private float CalculateDynamicNoise(float gpsAccuracy)
        {
            if (gpsAccuracy <= goodAccuracyThreshold)
            {
                return measurementNoise;
            }
            else if (gpsAccuracy <= poorAccuracyThreshold)
            {
                float factor = Mathf.Lerp(1f, 5f, (gpsAccuracy - goodAccuracyThreshold) / (poorAccuracyThreshold - goodAccuracyThreshold));
                return measurementNoise * factor;
            }
            else
            {
                return measurementNoise * 10f;
            }
        }

        private float GetDistanceMeters(Vector2 a, Vector2 b)
        {
            const float earthRadius = 6378137f;

            float lat1 = a.x * Mathf.Deg2Rad;
            float lat2 = b.x * Mathf.Deg2Rad;
            float dLat = (b.x - a.x) * Mathf.Deg2Rad;
            float dLon = (b.y - a.y) * Mathf.Deg2Rad;

            float sinLat = Mathf.Sin(dLat * 0.5f);
            float sinLon = Mathf.Sin(dLon * 0.5f);

            float h = sinLat * sinLat +
                    Mathf.Cos(lat1) * Mathf.Cos(lat2) *
                    sinLon * sinLon;

            float c = 2f * Mathf.Atan2(Mathf.Sqrt(h), Mathf.Sqrt(1f - h));

            return earthRadius * c;
        }

        public void Reset()
        {
            Debug.Log("LocationService: Reset called");

            if (IsRunning)
            {
                StopLocationUpdates();
            }

            // NOTE: Must stop Input.location to clear Failed state
            if (Input.location.status != LocationServiceStatus.Stopped)
            {
                Debug.Log($"LocationService: Stopping Input.location (status: {Input.location.status})");
                Input.location.Stop();
            }

            IsInitialized = false;
            IsLocationAvailable = false;
            filtersInitialized = false;
            isInjectionActive = false;
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

        [ContextMenu("Toggle Kalman Filter")]
        public void ToggleKalmanFilter()
        {
            useKalmanFilter = !useKalmanFilter;
            string status = useKalmanFilter ? "ENABLED" : "DISABLED";
            Debug.Log($"Kalman Filter: {status}");

            if (!useKalmanFilter)
            {
                filtersInitialized = false;
                Debug.Log("Using RAW GPS data");
            }
            else if (IsInitialized)
            {
                latFilter = new SimpleKalmanFilter(CurrentLocation.x, processNoise, measurementNoise);
                lonFilter = new SimpleKalmanFilter(CurrentLocation.y, processNoise, measurementNoise);
                filtersInitialized = true;
                Debug.Log("Kalman filters re-initialized");
            }
        }

        [ContextMenu("Debug Current Status")]
        public void DebugCurrentStatus()
        {
            Debug.Log($"=== Location Service Status ===");
            Debug.Log($"Initialized: {IsInitialized}");
            Debug.Log($"Running: {IsRunning}");
            Debug.Log($"GPS Status: {Input.location.status}");
            Debug.Log($"Current Location: {CurrentLocation.x:F8}, {CurrentLocation.y:F8}");
            Debug.Log($"Accuracy: {PositionAccuracy:F1}m");
            Debug.Log($"Kalman Filter: {(useKalmanFilter ? "ON" : "OFF")}");
            Debug.Log($"Injection Active: {isInjectionActive}");
            Debug.Log($"GPS Update Interval: {gpsUpdateInterval:F1}s (ONLY affects GPS accuracy)");
        }
    }

    // Kalman Filter unchanged
    [System.Serializable]
    public class SimpleKalmanFilter
    {
        private float estimate;
        private float errorEstimate;
        private float processNoise;
        private float measurementNoise;

        public SimpleKalmanFilter(float initialValue, float processNoise, float measurementNoise)
        {
            this.estimate = initialValue;
            this.errorEstimate = 1f;
            this.processNoise = processNoise;
            this.measurementNoise = measurementNoise;
        }

        public float Update(float measurement, float dynamicMeasurementNoise, float deltaTime)
        {
            float predictedEstimate = estimate;
            float predictedError = errorEstimate + (processNoise * deltaTime);

            float kalmanGain = predictedError / (predictedError + dynamicMeasurementNoise);
            estimate = predictedEstimate + kalmanGain * (measurement - predictedEstimate);
            errorEstimate = (1 - kalmanGain) * predictedError;

            return estimate;
        }

        public float GetEstimate() => estimate;
        public float GetErrorEstimate() => errorEstimate;
    }
}