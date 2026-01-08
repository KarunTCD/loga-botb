using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    public class ServiceManager : MonoBehaviour
    {
        private static ServiceManager instance;
        public static ServiceManager Instance => instance;

        // Events for loading screen
        public static event Action<string> ServiceInitializationUpdate;
        public static event Action<float> InitializationProgress;
        public static event Action AllServicesReady;

        // Service prefabs
        [SerializeField] private StorageService storageServicePrefab;
        [SerializeField] private PermissionService permissionServicePrefab;
        [SerializeField] private Services.LocationService locationServicePrefab;
        [SerializeField] private HeadTrackingService headTrackingServicePrefab;
        [SerializeField] private AudioService audioServicePrefab;
        [SerializeField] private FirebaseService firebaseServicePrefab;
        [SerializeField] private AnalyticsService analyticsServicePrefab;

        [Header("Audio Configuration")]
        [SerializeField] private AudioEventLookup audioEventLookup;

        private List<GameObject> createdServices = new List<GameObject>();
        private Dictionary<Type, IService> serviceInstances = new Dictionary<Type, IService>();
        private Dictionary<Type, bool> serviceInitStatus = new Dictionary<Type, bool>();

        // Service criticality levels for locative audio game
        private Dictionary<Type, bool> criticalServices = new Dictionary<Type, bool>
        {
            { typeof(IGameDataService), true },      // CRITICAL - needed first for configuration
            { typeof(IStorageService), true },       // Critical - game needs storage service
            { typeof(IAudioService), true },         // Critical - game is audio-based
            { typeof(IPermissionService), true },    // Critical - needed for location/bluetooth
            { typeof(ILocationService), true },      // Critical - locative game needs GPS
            { typeof(IHeadTrackingService), true },  // Critical - spatial audio needs head tracking
            { typeof(IFirebaseService), false },      // Optional - offline mode available
            { typeof(IAnalyticsService), false }
        };

        public bool AreAllServicesReady { get; private set; } = false;

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            // Create services immediately (synchronous)
            CreateServices();

            // Start initialization process
            StartCoroutine(InitializeAllServices());
        }

        private void CreateServices()
        {
            Debug.Log("Creating service instances...");

            // NOTE: Create GameDataService FIRST - no prefab needed, created programmatically
            CreateService<IGameDataService, GameDataService>(null);

            CreateService<IStorageService, StorageService>(storageServicePrefab);
            CreateService<IPermissionService, PermissionService>(permissionServicePrefab);
            CreateService<ILocationService, Services.LocationService>(locationServicePrefab);
            CreateService<IHeadTrackingService, HeadTrackingService>(headTrackingServicePrefab);
            CreateService<IAudioService, AudioService>(audioServicePrefab);
            CreateService<IFirebaseService, FirebaseService>(firebaseServicePrefab);
            CreateService<IAnalyticsService, AnalyticsService>(analyticsServicePrefab);

            Debug.Log($"Created {createdServices.Count} service instances");
        }

        private T CreateService<T, U>(U prefab) where U : MonoBehaviour, T where T : class, IService
        {
            GameObject serviceObj;

            if (prefab != null)
            {
                serviceObj = Instantiate(prefab.gameObject, transform);
                serviceObj.name = typeof(T).Name;
            }
            else
            {
                // Create GameObject programmatically (for GameDataService)
                serviceObj = new GameObject(typeof(T).Name);
                serviceObj.transform.SetParent(transform);
                var component = serviceObj.AddComponent<U>();

                // Special case for GameDataService - assign AudioEventLookup
                if (typeof(T) == typeof(IGameDataService))
                {
                    var gameDataService = component as GameDataService;
                    if (gameDataService != null)
                    {
                        if (audioEventLookup != null)
                        {
                            gameDataService.SetAudioEventLookup(audioEventLookup);
                            Debug.Log($"ServiceManager: AudioEventLookup assigned to GameDataService with {audioEventLookup.TotalMappingCount} mappings");
                        }
                        else
                        {
                            Debug.LogWarning("ServiceManager: AudioEventLookup is null - audio events won't work");
                        }
                    }
                }
            }

            createdServices.Add(serviceObj);
            var service = serviceObj.GetComponent<T>();

            // Register with service locator
            ServiceLocator.RegisterService<T>(service);

            // Track service instance
            serviceInstances[typeof(T)] = service;
            serviceInitStatus[typeof(T)] = false;

            return service;
        }

        private IEnumerator InitializeAllServices()
        {
            yield return new WaitForEndOfFrame(); // Let all Awake() methods complete

            Debug.Log("Starting service initialization sequence...");

            // CRITICAL: Initialize GameDataService FIRST
            var servicesToInitialize = new List<(Type type, IService service, string name)>
            {
                (typeof(IGameDataService), serviceInstances[typeof(IGameDataService)], "Game Data"),
                (typeof(IStorageService), serviceInstances[typeof(IStorageService)], "Local Storage"),
                (typeof(IPermissionService), serviceInstances[typeof(IPermissionService)], "Device Permissions"),
                (typeof(IAudioService), serviceInstances[typeof(IAudioService)], "Audio System"),
                (typeof(ILocationService), serviceInstances[typeof(ILocationService)], "GPS Location"),
                (typeof(IHeadTrackingService), serviceInstances[typeof(IHeadTrackingService)], "Head Tracking"),
                (typeof(IFirebaseService), serviceInstances[typeof(IFirebaseService)], "Online Features"),
                (typeof(IFirebaseService), serviceInstances[typeof(IAnalyticsService)], "Analytic Features")
            };

            for (int i = 0; i < servicesToInitialize.Count; i++)
            {
                var (type, service, name) = servicesToInitialize[i];

                ServiceInitializationUpdate?.Invoke($"Initializing {name}...");

                // Use coroutine to handle async initialization
                yield return StartCoroutine(InitializeServiceCoroutine(service, type, name));

                // Update progress
                float progress = (float)(i + 1) / servicesToInitialize.Count;
                InitializationProgress?.Invoke(progress);

                yield return null; // Allow frame to process
            }

            AreAllServicesReady = true;
            ServiceInitializationUpdate?.Invoke("Initialization complete!");
            AllServicesReady?.Invoke();

            Debug.Log("Service initialization complete");
        }

        private IEnumerator InitializeServiceCoroutine(IService service, Type serviceType, string serviceName)
        {
            bool success = false;
            bool completed = false;

            // Start the async initialization
            var initTask = service.InitializeAsync();

            // Wait for completion
            while (!completed)
            {
                if (initTask.IsCompleted)
                {
                    success = initTask.Result;
                    completed = true;
                }
                else
                {
                    yield return null; // Wait one frame
                }
            }

            serviceInitStatus[serviceType] = success;

            if (success)
            {
                Debug.Log($"Successfully initialized {serviceName}");
            }
            else
            {
                Debug.LogWarning($"Failed to initialize {serviceName}");
            }
        }

        /// <summary>
        /// Restart initialization after reset
        /// Called when user clicks retry
        /// </summary>
        public IEnumerator RestartInitialization()
        {
            Debug.Log("ServiceManager: Restarting initialization");

            // Small delay to ensure reset completed
            yield return new WaitForSeconds(0.5f);

            // Restart the initialization coroutine
            yield return StartCoroutine(InitializeAllServices());
        }


        /// <summary>
        /// Reset all services to allow retry after failed initialization
        /// Called when user clicks retry button after initialization failure
        /// </summary>
        public void ResetAllServices()
        {
            Debug.Log("ServiceManager: Resetting all services for retry");

            int resetCount = 0;
            List<string> failedResets = new List<string>();

            foreach (var kvp in serviceInstances)
            {
                try
                {
                    kvp.Value.Reset();
                    resetCount++;
                    Debug.Log($"  ✓ Reset {kvp.Key.Name}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"  ✗ Error resetting {kvp.Key.Name}: {e.Message}");
                    failedResets.Add(kvp.Key.Name);
                }
            }

            // Clear initialization status
            foreach (var key in serviceInitStatus.Keys.ToList())
            {
                serviceInitStatus[key] = false;
            }

            // Reset ready flag
            AreAllServicesReady = false;

            Debug.Log($"ServiceManager: Reset complete - {resetCount}/{serviceInstances.Count} services reset");

            if (failedResets.Count > 0)
            {
                Debug.LogWarning($"ServiceManager: Failed to reset: {string.Join(", ", failedResets)}");
            }
        }

        #region State Management Integration

        // Service state queries
        public bool IsHeadTrackingConnected()
        {
            var service = ServiceLocator.GetService<IHeadTrackingService>();
            if (service == null) return false;

            string provider = service.ActiveProviderName;
            return !string.IsNullOrEmpty(provider) && provider != "None";
        }

        public bool IsLocationServiceActive()
        {
            var service = ServiceLocator.GetService<ILocationService>();
            return service?.IsRunning ?? false;
        }

        public bool IsAudioServiceReady()
        {
            return IsServiceInitialized<IAudioService>();
        }

        public bool IsFirebaseConnected()
        {
            var service = ServiceLocator.GetService<IFirebaseService>();
            return service?.IsInitialized ?? false;
        }

        public Vector2 GetCurrentLocation()
        {
            var service = ServiceLocator.GetService<ILocationService>();
            return service?.GetCurrentLocation() ?? Vector2.zero;
        }

        public string GetHeadTrackingProvider()
        {
            var service = ServiceLocator.GetService<IHeadTrackingService>();
            return service?.ActiveProviderName ?? "None";
        }

        public float GetCurrentHeading()
        {
            var service = ServiceLocator.GetService<IHeadTrackingService>();
            return service?.CurrentHeading ?? 0f;
        }

        // Hardware verification method
        public HardwareStatus GetHardwareStatus()
        {
            return new HardwareStatus
            {
                headTrackingConnected = IsHeadTrackingConnected(),
                headTrackingProvider = GetHeadTrackingProvider(),
                currentHeading = GetCurrentHeading(),
                locationActive = IsLocationServiceActive(),
                currentLocation = GetCurrentLocation(),
                audioReady = IsAudioServiceReady(),
                firebaseConnected = IsFirebaseConnected(),
                allSystemsReady = AreAllServicesReady
            };
        }

        // Utility method for state-first pattern
        public void CheckServiceState<T>(Action<bool> onReadyCallback, Action onNotReadyCallback = null) where T : class, IService
        {
            bool isReady = IsServiceInitialized<T>();

            if (isReady)
            {
                onReadyCallback?.Invoke(true);
            }
            else
            {
                onNotReadyCallback?.Invoke();
            }
        }

        #endregion

        // Public API for service status
        public bool IsServiceInitialized<T>() where T : class, IService
        {
            return serviceInitStatus.TryGetValue(typeof(T), out bool initialized) && initialized;
        }

        private bool IsServiceInitialized(Type serviceType)
        {
            return serviceInitStatus.TryGetValue(serviceType, out bool initialized) && initialized;
        }

        public bool AreCriticalServicesReady()
        {
            foreach (var kvp in criticalServices)
            {
                if (kvp.Value && !IsServiceInitialized(kvp.Key))
                    return false;
            }
            return true;
        }

        public List<string> GetFailedCriticalServices()
        {
            var failed = new List<string>();
            foreach (var kvp in criticalServices)
            {
                if (kvp.Value && !IsServiceInitialized(kvp.Key))
                    failed.Add(GetFriendlyServiceName(kvp.Key));
            }
            return failed;
        }

        private string GetFriendlyServiceName(Type serviceType)
        {
            return serviceType.Name switch
            {
                nameof(IGameDataService) => "Game Data",
                nameof(IStorageService) => "Local Storage",
                nameof(IAudioService) => "Audio System",
                nameof(IPermissionService) => "Device Permissions",
                nameof(ILocationService) => "GPS Location",
                nameof(IHeadTrackingService) => "Head Tracking",
                nameof(IFirebaseService) => "Online Features",
                _ => serviceType.Name
            };
        }

        public float GetInitializationProgress()
        {
            if (serviceInitStatus.Count == 0) return 0;
            int initializedCount = 0;
            foreach (var status in serviceInitStatus.Values)
            {
                if (status) initializedCount++;
            }
            return (float)initializedCount / serviceInitStatus.Count;
        }

        // Add back the missing method for compatibility
        public void MarkServiceInitialized<T>() where T : class, IService
        {
            Type serviceType = typeof(T);
            if (serviceInitStatus.ContainsKey(serviceType))
            {
                serviceInitStatus[serviceType] = true;
                Debug.Log($"Service marked as initialized: {serviceType.Name}");
            }
        }

        #region Debug Methods

        [ContextMenu("Debug AudioEventLookup")]
        public void DebugAudioEventLookup()
        {
            if (audioEventLookup != null)
            {
                Debug.Log($"AudioEventLookup has {audioEventLookup.TotalMappingCount} mappings:");
                Debug.Log($"Character Events: {audioEventLookup.characterAudioEvents.Count}");
                Debug.Log($"Portal Events: {audioEventLookup.portalAudioEvents.Count}");

                audioEventLookup.DebugAllMappings();
            }
            else
            {
                Debug.LogError("AudioEventLookup is null in ServiceManager");
            }
        }

        [ContextMenu("Debug Hardware Status")]
        public void DebugHardwareStatus()
        {
            var status = GetHardwareStatus();
            Debug.Log($"Hardware Status:\n" +
                     $"Head Tracking: {status.headTrackingProvider} ({(status.headTrackingConnected ? "Connected" : "Disconnected")})\n" +
                     $"Location: {(status.locationActive ? "Active" : "Inactive")} - {status.currentLocation}\n" +
                     $"Audio: {(status.audioReady ? "Ready" : "Not Ready")}\n" +
                     $"Firebase: {(status.firebaseConnected ? "Connected" : "Disconnected")}\n" +
                     $"All Systems: {(status.allSystemsReady ? "Ready" : "Not Ready")}");
        }

        #endregion

        private void OnDestroy()
        {
            foreach (var serviceObj in createdServices)
            {
                if (serviceObj != null)
                {
                    Destroy(serviceObj);
                }
            }

            createdServices.Clear();
            ServiceLocator.ClearAllServices();
        }
    }

    // Simple data structure for hardware status
    [System.Serializable]
    public struct HardwareStatus
    {
        public bool headTrackingConnected;
        public string headTrackingProvider;
        public float currentHeading;
        public bool locationActive;
        public Vector2 currentLocation;
        public bool audioReady;
        public bool firebaseConnected;
        public bool allSystemsReady;
    }
}