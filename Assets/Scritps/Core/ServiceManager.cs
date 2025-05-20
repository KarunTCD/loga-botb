using System;
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

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            // Create services immediately
            CreateServices();
        }

        private void CreateServices()
        {
            // Create service instances and register them
            //CreateService<IConfigService, ConfigService>(configServicePrefab);
            CreateService<IPermissionService, PermissionService>(permissionServicePrefab);
            CreateService<ILocationService, Services.LocationService>(locationServicePrefab);
            CreateService<IHeadTrackingService, HeadTrackingService>(headTrackingServicePrefab);
            CreateService<IAudioService, AudioService>(audioServicePrefab);
            CreateService<IFirebaseService, FirebaseService>(firebaseServicePrefab);
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

            // Initialize status tracking
            serviceInitStatus[typeof(T)] = false;

            return service;
        }

        // Track initialization status
        public void MarkServiceInitialized<T>()
        {
            Type serviceType = typeof(T);
            if (serviceInitStatus.ContainsKey(serviceType))
            {
                serviceInitStatus[serviceType] = true;
                Debug.Log($"Service marked as initialized: {serviceType.Name}");
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