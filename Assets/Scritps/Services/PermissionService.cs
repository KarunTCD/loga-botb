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
        [Header("Bluetooth Permissions")]
        [SerializeField] private bool debugPermissions = true;

        public event Action<bool> LocationPermissionResult;
        public event Action<bool> BluetoothPermissionResult;

        public bool HasLocationPermission { get; private set; }
        public bool HasBluetoothPermissions { get; private set; }
        public bool IsInitialized { get; private set; }

        // Add method for MMRL provider to use
        public async Task<bool> RequestBluetoothPermissions()
        {
            if (debugPermissions)
                Debug.Log("Requesting Bluetooth permissions for MMRL provider...");

#if PLATFORM_ANDROID
            // First check if we already have permissions
            if (HasAllBluetoothPermissions())
            {
                if (debugPermissions)
                    Debug.Log("All Bluetooth permissions already granted");
                return true;
            }

            // Create TaskCompletionSource for the async result
            var tcs = new TaskCompletionSource<bool>();

            // One-time event handler
            void BluetoothPermissionHandler(bool result)
            {
                BluetoothPermissionResult -= BluetoothPermissionHandler;
                tcs.SetResult(result);
            }

            BluetoothPermissionResult += BluetoothPermissionHandler;

            // Start timeout timer
            var timeoutTimer = new System.Threading.Timer(_ =>
            {
                BluetoothPermissionResult -= BluetoothPermissionHandler;
                tcs.TrySetResult(false);
                Debug.LogWarning("Bluetooth permission request timed out");
            }, null, 15000, System.Threading.Timeout.Infinite); // 15 second timeout for multiple permissions

            tcs.Task.ContinueWith(_ => timeoutTimer.Dispose());

            // Request the permissions
            RequestBluetoothPermissionsInternal();

            return await tcs.Task;
#else
            if (debugPermissions)
                Debug.Log("Bluetooth permissions not required on this platform");
            return true;
#endif
        }

        public Task<bool> InitializeAsync()
        {
            // Check current permissions
            CheckLocationPermission();
            CheckBluetoothPermissions();

            if (HasLocationPermission)
            {
                IsInitialized = true;
                return Task.FromResult(true);
            }

            // Same existing logic for location permission
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
                Debug.LogWarning("Location permission request timed out");
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

            if (debugPermissions)
                Debug.Log($"Location Permission: {HasLocationPermission}");

            LocationPermissionResult?.Invoke(HasLocationPermission);
        }

        public void RequestLocationPermission()
        {
#if PLATFORM_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                if (debugPermissions)
                    Debug.Log("Requesting location permission...");

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

        // New Bluetooth permission methods
        private void CheckBluetoothPermissions()
        {
#if PLATFORM_ANDROID
            HasBluetoothPermissions = HasAllBluetoothPermissions();
#else
            HasBluetoothPermissions = true;
#endif

            if (debugPermissions)
                Debug.Log($"Bluetooth Permissions: {HasBluetoothPermissions}");
        }

        private void RequestBluetoothPermissionsInternal()
        {
#if PLATFORM_ANDROID
            if (debugPermissions)
                Debug.Log("Requesting Bluetooth permissions...");

            // Get Android API level
            int apiLevel = GetAndroidAPILevel();

            if (apiLevel >= 31) // Android 12+
            {
                RequestAndroid12BluetoothPermissions();
            }
            else
            {
                // For older Android versions, location permission is sufficient
                if (HasLocationPermission)
                {
                    HasBluetoothPermissions = true;
                    BluetoothPermissionResult?.Invoke(true);
                }
                else
                {
                    // Request location permission first
                    RequestLocationPermission();
                    // The location permission callback will handle setting Bluetooth permissions
                }
            }
#endif
        }

#if PLATFORM_ANDROID
        private void RequestAndroid12BluetoothPermissions()
        {
            // For Android 12+, we need specific Bluetooth permissions
            string[] bluetoothPermissions = {
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.BLUETOOTH_CONNECT",
                "android.permission.BLUETOOTH_ADVERTISE"
            };

            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnBluetoothPermissionGranted;
            callbacks.PermissionDenied += OnBluetoothPermissionDenied;

            // Request all Bluetooth permissions
            foreach (string permission in bluetoothPermissions)
            {
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    Permission.RequestUserPermission(permission, callbacks);
                }
            }

            // Check if all are already granted
            if (HasAllBluetoothPermissions())
            {
                HasBluetoothPermissions = true;
                BluetoothPermissionResult?.Invoke(true);
            }
        }

        private bool HasAllBluetoothPermissions()
        {
            int apiLevel = GetAndroidAPILevel();

            if (apiLevel >= 31) // Android 12+
            {
                return Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN") &&
                       Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT") &&
                       Permission.HasUserAuthorizedPermission(Permission.FineLocation);
            }
            else
            {
                // For older Android, just need location permission
                return HasLocationPermission;
            }
        }

        private int GetAndroidAPILevel()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return version.GetStatic<int>("SDK_INT");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to get Android API level: {e.Message}");
                return 23; // Default to API 23 if we can't determine
            }
        }

        private void OnPermissionGranted(string permissionName)
        {
            if (debugPermissions)
                Debug.Log($"Permission granted: {permissionName}");

            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = true;
                IsInitialized = true;
                LocationPermissionResult?.Invoke(true);

                // For older Android versions, location permission enables Bluetooth
                if (GetAndroidAPILevel() < 31)
                {
                    HasBluetoothPermissions = true;
                }
            }
        }

        private void OnPermissionDenied(string permissionName)
        {
            if (debugPermissions)
                Debug.LogWarning($"Permission denied: {permissionName}");

            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = false;
                IsInitialized = false;
                LocationPermissionResult?.Invoke(false);
            }
        }

        private void OnBluetoothPermissionGranted(string permissionName)
        {
            if (debugPermissions)
                Debug.Log($"Bluetooth permission granted: {permissionName}");

            // Check if all Bluetooth permissions are now granted
            if (HasAllBluetoothPermissions())
            {
                HasBluetoothPermissions = true;
                BluetoothPermissionResult?.Invoke(true);
            }
        }

        private void OnBluetoothPermissionDenied(string permissionName)
        {
            if (debugPermissions)
                Debug.LogWarning($"Bluetooth permission denied: {permissionName}");

            HasBluetoothPermissions = false;
            BluetoothPermissionResult?.Invoke(false);
        }
#endif

        private void OnApplicationFocus(bool focus)
        {
            if (focus)
            {
                CheckLocationPermission();
                CheckBluetoothPermissions();
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