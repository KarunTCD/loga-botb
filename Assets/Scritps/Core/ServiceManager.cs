using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    public class ServiceManager : MonoBehaviour
    {
        private static ServiceManager instance;
        public static ServiceManager Instance => instance;

        // Service prefabs
        [SerializeField] private ConfigService configServicePrefab;
        [SerializeField] private PermissionService permissionServicePrefab;
        [SerializeField] private Services.LocationService locationServicePrefab;
        [SerializeField] private HeadTrackingService headTrackingServicePrefab;
        [SerializeField] private AudioService audioServicePrefab;
        [SerializeField] private FirebaseService firebaseServicePrefab;

        private List<GameObject> createdServices = new List<GameObject>();

        // Initialization status tracking
        private Dictionary<Type, bool> serviceInitStatus = new Dictionary<Type, bool>();

        // Track if services have been initialized
        public bool ServicesInitialized { get; private set; } = false;

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            // Create services immediately but don't initialize yet
            CreateServices();
        }

        private async void Start()
        {
            // Initialize services in the correct order
            await InitializeServicesSequentially();
        }

        private void CreateServices()
        {
            Debug.Log("Creating service instances...");

            // Create service instances and register them (but don't initialize yet)
            //CreateService<IConfigService, ConfigService>(configServicePrefab);
            CreateService<IPermissionService, PermissionService>(permissionServicePrefab);
            CreateService<ILocationService, Services.LocationService>(locationServicePrefab);
            CreateService<IHeadTrackingService, HeadTrackingService>(headTrackingServicePrefab);
            CreateService<IAudioService, AudioService>(audioServicePrefab);
            CreateService<IFirebaseService, FirebaseService>(firebaseServicePrefab);

            Debug.Log($"Created {createdServices.Count} service instances");
        }

        private async Task InitializeServicesSequentially()
        {
            Debug.Log("Starting sequential service initialization...");

            try
            {
                // Step 1: Initialize PermissionService FIRST (critical for other services)
                Debug.Log("📋 Initializing PermissionService...");
                var permissionService = ServiceLocator.GetService<IPermissionService>();
                if (permissionService != null)
                {
                    bool permissionSuccess = await permissionService.InitializeAsync();
                    MarkServiceInitialized<IPermissionService>(permissionSuccess);

                    if (!permissionSuccess)
                    {
                        Debug.LogError("PermissionService failed to initialize - location permission may be denied");
                        // Continue with other services anyway, but warn user
                    }
                    else
                    {
                        Debug.Log("PermissionService initialized successfully");
                    }
                }

                // Step 2: Initialize LocationService (depends on permissions)
                Debug.Log("Initializing LocationService...");
                var locationService = ServiceLocator.GetService<ILocationService>();
                if (locationService != null)
                {
                    bool locationSuccess = await locationService.InitializeAsync();
                    MarkServiceInitialized<ILocationService>(locationSuccess);

                    if (!locationSuccess)
                    {
                        Debug.LogWarning("LocationService failed to initialize - GPS features may not work");
                    }
                    else
                    {
                        Debug.Log("LocationService initialized successfully");
                    }
                }

                // Step 3: Initialize HeadTrackingService (may need Bluetooth permissions)
                Debug.Log("🎯 Initializing HeadTrackingService...");
                var headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
                if (headTrackingService != null)
                {
                    bool headTrackingSuccess = await headTrackingService.InitializeAsync();
                    MarkServiceInitialized<IHeadTrackingService>(headTrackingSuccess);

                    if (!headTrackingSuccess)
                    {
                        Debug.LogWarning("HeadTrackingService failed to initialize - head tracking may not work");
                    }
                    else
                    {
                        Debug.Log("HeadTrackingService initialized successfully");
                    }
                }

                // Step 4: Initialize remaining services (parallel is OK for these)
                Debug.Log("🎵 Initializing AudioService...");
                var audioService = ServiceLocator.GetService<IAudioService>();
                if (audioService != null)
                {
                    bool audioSuccess = await audioService.InitializeAsync();
                    MarkServiceInitialized<IAudioService>(audioSuccess);
                    Debug.Log(audioSuccess ? "AudioService initialized" : "AudioService failed");
                }

                Debug.Log("Initializing FirebaseService...");
                var firebaseService = ServiceLocator.GetService<IFirebaseService>();
                if (firebaseService != null)
                {
                    bool firebaseSuccess = await firebaseService.InitializeAsync();
                    MarkServiceInitialized<IFirebaseService>(firebaseSuccess);
                    Debug.Log(firebaseSuccess ? "FirebaseService initialized" : "FirebaseService failed");
                }

                ServicesInitialized = true;

                // Final status report
                Debug.Log("Service initialization complete!");
                Debug.Log($"Initialization Progress: {GetInitializationProgress():P0}");

                if (AreAllServicesInitialized())
                {
                    Debug.Log("All services initialized successfully!");
                }
                else
                {
                    Debug.LogWarning("Some services failed to initialize - check logs above");
                    LogFailedServices();
                }

            }
            catch (Exception e)
            {
                Debug.LogError($"Service initialization failed with exception: {e.Message}");
                ServicesInitialized = true; // Mark as done even if failed
            }
        }

        private void LogFailedServices()
        {
            foreach (var kvp in serviceInitStatus)
            {
                if (!kvp.Value)
                {
                    Debug.LogWarning($"Failed service: {kvp.Key.Name}");
                }
            }
        }

        private T CreateService<T, U>(U prefab) where U : MonoBehaviour, T
        {
            GameObject serviceObj;

            if (prefab != null)
            {
                // Instantiate from prefab
                serviceObj = Instantiate(prefab.gameObject, transform);
                serviceObj.name = typeof(T).Name; // Rename for clarity
            }
            else
            {
                // Create new GameObject
                serviceObj = new GameObject(typeof(T).Name);
                serviceObj.transform.SetParent(transform);
                serviceObj.AddComponent<U>();
            }

            createdServices.Add(serviceObj);
            var service = serviceObj.GetComponent<T>();

            // Register with service locator
            ServiceLocator.RegisterService<T>(service);

            // Initialize status tracking (false by default)
            serviceInitStatus[typeof(T)] = false;

            Debug.Log($"Created service: {typeof(T).Name}");
            return service;
        }

        // Updated to accept success/failure status
        public void MarkServiceInitialized<T>(bool success = true)
        {
            Type serviceType = typeof(T);
            if (serviceInitStatus.ContainsKey(serviceType))
            {
                serviceInitStatus[serviceType] = success;
                string status = success ? "SUCCESS" : "FAILED";
                Debug.Log($"Service initialization result: {serviceType.Name} - {status}");
            }
        }

        public bool IsServiceInitialized<T>()
        {
            Type serviceType = typeof(T);
            if (serviceInitStatus.TryGetValue(serviceType, out bool initialized))
            {
                return initialized;
            }
            return false;
        }

        public bool AreAllServicesInitialized()
        {
            return serviceInitStatus.Values.All(initialized => initialized);
        }

        public float GetInitializationProgress()
        {
            if (serviceInitStatus.Count == 0)
                return 0;

            int initializedCount = serviceInitStatus.Values.Count(v => v);
            return (float)initializedCount / serviceInitStatus.Count;
        }

        // Public method to wait for services to be ready
        public async Task WaitForServicesAsync()
        {
            while (!ServicesInitialized)
            {
                await Task.Delay(100);
            }
        }

        private void OnDestroy()
        {
            // Clean up services
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
}