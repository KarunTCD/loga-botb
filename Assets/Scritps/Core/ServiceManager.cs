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
        [SerializeField] private PermissionService permissionServicePrefab;
        [SerializeField] private Services.LocationService locationServicePrefab;
        [SerializeField] private HeadTrackingService headTrackingServicePrefab;
        [SerializeField] private AudioService audioServicePrefab;
        [SerializeField] private FirebaseService firebaseServicePrefab;

        private List<GameObject> createdServices = new List<GameObject>();
        private Dictionary<Type, IService> serviceInstances = new Dictionary<Type, IService>();
        private Dictionary<Type, bool> serviceInitStatus = new Dictionary<Type, bool>();

        // Service criticality levels for locative audio game
        private Dictionary<Type, bool> criticalServices = new Dictionary<Type, bool>
        {
            { typeof(IAudioService), true },         // Critical - game is audio-based
            { typeof(IPermissionService), true },    // Critical - needed for location/bluetooth
            { typeof(ILocationService), true },      // Critical - locative game needs GPS
            { typeof(IHeadTrackingService), true },  // Critical - spatial audio needs head tracking
            { typeof(IFirebaseService), false }      // Optional - offline mode available
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

            CreateService<IPermissionService, PermissionService>(permissionServicePrefab);
            CreateService<ILocationService, Services.LocationService>(locationServicePrefab);
            CreateService<IHeadTrackingService, HeadTrackingService>(headTrackingServicePrefab);
            CreateService<IAudioService, AudioService>(audioServicePrefab);
            CreateService<IFirebaseService, FirebaseService>(firebaseServicePrefab);

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
                serviceObj = new GameObject(typeof(T).Name);
                serviceObj.transform.SetParent(transform);
                serviceObj.AddComponent<U>();
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

            var servicesToInitialize = new List<(Type type, IService service, string name)>
            {
                (typeof(IPermissionService), serviceInstances[typeof(IPermissionService)], "Device Permissions"),
                (typeof(IAudioService), serviceInstances[typeof(IAudioService)], "Audio System"),
                (typeof(ILocationService), serviceInstances[typeof(ILocationService)], "GPS Location"),
                (typeof(IHeadTrackingService), serviceInstances[typeof(IHeadTrackingService)], "Head Tracking"),
                (typeof(IFirebaseService), serviceInstances[typeof(IFirebaseService)], "Online Features")
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
}