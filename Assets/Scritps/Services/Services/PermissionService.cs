using System;
using UnityEngine;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public class PermissionService : MonoBehaviour, IPermissionService
{
    public event Action<bool> LocationPermissionResult;

    public bool HasLocationPermission { get; private set; }

    public void CheckLocationPermission()
    {
#if PLATFORM_ANDROID
        HasLocationPermission = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
#elif UNITY_IOS
        HasLocationPermission = Input.location.isEnabledByUser;
#else
        HasLocationPermission = true; // Default for editor/desktop
#endif

        LocationPermissionResult?.Invoke(HasLocationPermission);
    }

    public void RequestLocationPermission()
    {
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnPermissionGranted;
            callbacks.PermissionDenied += OnPermissionDenied;
            Permission.RequestUserPermission(Permission.FineLocation, callbacks);
        }
        else
        {
            HasLocationPermission = true;
            LocationPermissionResult?.Invoke(true);
        }
#elif UNITY_IOS
        // iOS permissions are requested automatically when Input.location is used
        HasLocationPermission = Input.location.isEnabledByUser;
        LocationPermissionResult?.Invoke(HasLocationPermission);
#else
        // For editor/desktop, we assume permission is granted
        HasLocationPermission = true;
        LocationPermissionResult?.Invoke(true);
#endif
    }

#if PLATFORM_ANDROID
    private void OnPermissionGranted(string permissionName)
    {
        if (permissionName == Permission.FineLocation)
        {
            HasLocationPermission = true;
            LocationPermissionResult?.Invoke(true);
        }
    }

    private void OnPermissionDenied(string permissionName)
    {
        if (permissionName == Permission.FineLocation)
        {
            HasLocationPermission = false;
            LocationPermissionResult?.Invoke(false);
        }
    }
#endif

    private void OnApplicationFocus(bool focus)
    {
        // Check permission state when app regains focus
        if (focus)
        {
            CheckLocationPermission();
        }
    }

    private void OnDisable()
    {
        ServiceLocator.UnregisterService<IPermissionService>();
    }
}