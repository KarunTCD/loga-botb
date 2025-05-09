using UnityEngine;
using System.Collections.Generic;

public class ServiceManager : MonoBehaviour
{
    private static ServiceManager instance;

    // Service prefabs or references
    [SerializeField] private PermissionService permissionServicePrefab;
    [SerializeField] private LocationService locationServicePrefab;
    [SerializeField] private HeadTrackingService headTrackingServicePrefab;
    [SerializeField] private AudioService audioServicePrefab;
    [SerializeField] private FirebaseService firebaseServicePrefab;

    private List<GameObject> createdServices = new List<GameObject>();

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeServices();
    }

    private void InitializeServices()
    {
        // Permission service first
        InitializePermissionService();

        // Then the others
        InitializeLocationService();
        InitializeHeadTrackingService();
        InitializeAudioService();
        InitializeFirebaseService();
    }

    private void InitializePermissionService()
    {
        var service = CreateService<IPermissionService, PermissionService>(permissionServicePrefab);
        service.CheckLocationPermission();
    }

    private void InitializeLocationService()
    {
        var service = CreateService<ILocationService, LocationService>(locationServicePrefab);
        service.Initialize();
    }

    private void InitializeHeadTrackingService()
    {
        var service = CreateService<IHeadTrackingService, HeadTrackingService>(headTrackingServicePrefab);
        service.Initialize();
    }

    private void InitializeAudioService()
    {
        var service = CreateService<IAudioService, AudioService>(audioServicePrefab);
        service.Initialize();
    }

    private void InitializeFirebaseService()
    {
        var service = CreateService<IFirebaseService, FirebaseService>(firebaseServicePrefab);

        // We don't initialize Firebase right away since it's async
        // It will be initialized on demand when needed
    }

    private T CreateService<T, U>(U prefab) where U : MonoBehaviour, T
    {
        GameObject serviceObj;

        if (prefab != null)
        {
            // Instantiate from prefab
            serviceObj = Instantiate(prefab.gameObject, transform);
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
        ServiceLocator.RegisterService<T>(service);

        return service;
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