using System;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;
using System.Threading.Tasks;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

namespace LoGa.LudoEngine.Services
{
    public class PermissionService : MonoBehaviour, IPermissionService
    {
        public event Action<bool> LocationPermissionResult;
        public bool HasLocationPermission { get; private set; }

        public bool IsInitialized { get; private set; }

#if PLATFORM_ANDROID
        private const string BluetoothScanPermission = "android.permission.BLUETOOTH_SCAN";
        private const string BluetoothConnectPermission = "android.permission.BLUETOOTH_CONNECT";
        private const string BluetoothAdvertisePermission = "android.permission.BLUETOOTH_ADVERTISE";
#endif

        public Task<bool> InitializeAsync()
        {
            CheckLocationPermission();

            if (HasLocationPermission)
            {
                IsInitialized = true;
                return Task.FromResult(true);
            }

            var tcs = new TaskCompletionSource<bool>();

            void PermissionResultHandler(bool result)
            {
                LocationPermissionResult -= PermissionResultHandler;
                IsInitialized = result;
                tcs.SetResult(result);
            }

            LocationPermissionResult += PermissionResultHandler;

            var timeoutTimer = new System.Threading.Timer(_ =>
            {
                LocationPermissionResult -= PermissionResultHandler;
                IsInitialized = false;
                tcs.TrySetResult(false);
                Debug.LogWarning("Permission request timed out");
            }, null, 10000, System.Threading.Timeout.Infinite);

            tcs.Task.ContinueWith(_ => timeoutTimer.Dispose());

            RequestLocationPermission();

            return tcs.Task;
        }

        public void CheckLocationPermission()
        {
#if PLATFORM_ANDROID
            HasLocationPermission = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
#elif UNITY_IOS
            HasLocationPermission = Input.location.isEnabledByUser;
#else
            HasLocationPermission = true;
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
                IsInitialized = true;
                LocationPermissionResult?.Invoke(true);
            }
#elif UNITY_IOS
            HasLocationPermission = Input.location.isEnabledByUser;
            IsInitialized = HasLocationPermission;
            LocationPermissionResult?.Invoke(HasLocationPermission);
#else
            HasLocationPermission = true;
            IsInitialized = true;
            LocationPermissionResult?.Invoke(true);
#endif
        }

#if PLATFORM_ANDROID
        private void OnPermissionGranted(string permissionName)
        {
            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = true;
                IsInitialized = true;
                LocationPermissionResult?.Invoke(true);
            }
        }

        private void OnPermissionDenied(string permissionName)
        {
            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = false;
                IsInitialized = false;
                LocationPermissionResult?.Invoke(false);
            }
        }
#endif

        public Task<bool> RequestBluetoothPermissions()
        {
            var tcs = new TaskCompletionSource<bool>();

            #if PLATFORM_ANDROID
                        var permissionsToRequest = new System.Collections.Generic.List<string>();
                        var granted = true;

                        if (!Permission.HasUserAuthorizedPermission(BluetoothScanPermission))
                        {
                            permissionsToRequest.Add(BluetoothScanPermission);
                            granted = false;
                        }

                        if (!Permission.HasUserAuthorizedPermission(BluetoothConnectPermission))
                        {
                            permissionsToRequest.Add(BluetoothConnectPermission);
                            granted = false;
                        }

                        if (!Permission.HasUserAuthorizedPermission(BluetoothAdvertisePermission))
                        {
                            permissionsToRequest.Add(BluetoothAdvertisePermission);
                            granted = false;
                        }

                        if (permissionsToRequest.Count > 0)
                        {
                            PermissionCallbacks callbacks = new PermissionCallbacks();
                            callbacks.PermissionGranted += (perm) => Debug.Log($"Bluetooth Permission Granted: {perm}");
                            callbacks.PermissionDenied += (perm) => Debug.LogWarning($"Bluetooth Permission Denied: {perm}");
                            callbacks.PermissionDeniedAndDontAskAgain += (perm) => Debug.LogWarning($"Bluetooth Permission Denied Forever: {perm}");

                            callbacks.PermissionGranted += (_) =>
                            {
                                // Check again if all are granted after user response
                                bool allGranted = Permission.HasUserAuthorizedPermission(BluetoothScanPermission) &&
                                                  Permission.HasUserAuthorizedPermission(BluetoothConnectPermission) &&
                                                  Permission.HasUserAuthorizedPermission(BluetoothAdvertisePermission);
                                tcs.TrySetResult(allGranted);
                            };

                            Permission.RequestUserPermissions(permissionsToRequest.ToArray(), callbacks);
                        }
                        else
                        {
                            Debug.Log("All required Bluetooth permissions already granted.");
                            tcs.SetResult(true);
                        }
            #else
                Debug.Log("Bluetooth permission request skipped (not Android platform).");
                tcs.SetResult(true);
            #endif

            return tcs.Task;
        }


        private void OnApplicationFocus(bool focus)
        {
            if (focus)
            {
                CheckLocationPermission();
                IsInitialized = HasLocationPermission;
            }
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                ServiceLocator.UnregisterService<IPermissionService>();
            }
        }
    }
}
