using System;
using UnityEngine;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public interface IPermissionService
{
    event Action<bool> LocationPermissionResult;
    bool HasLocationPermission { get; }
    void CheckLocationPermission();
    void RequestLocationPermission();
}