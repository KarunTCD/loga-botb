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

        // Add IsInitialized property
        public bool IsInitialized { get; private set; }

        public Task<bool> InitializeAsync()
        {
            // Check if already has permission
            CheckLocationPermission();

            if (HasLocationPermission)
            {
                // Set initialization status to true
                IsInitialized = true;
                return Task.FromResult(true);
            }

            // Create TaskCompletionSource for the async result
            var tcs = new TaskCompletionSource<bool>();

            // One-time event handler
            void PermissionResultHandler(bool result)
            {
                // Remove the handler once we get a result
                LocationPermissionResult -= PermissionResultHandler;

                // Update initialization status based on permission result
                IsInitialized = result;

                // Complete the task with the permission result
                tcs.SetResult(result);
            }

            // Subscribe to the permission result event
            LocationPermissionResult += PermissionResultHandler;

            // Start a timeout timer
            var timeoutTimer = new System.Threading.Timer(_ =>
            {
                // Remove the handler on timeout
                LocationPermissionResult -= PermissionResultHandler;

                // Set initialization to false since we timed out
                IsInitialized = false;

                // Complete the task with failure if not already completed
                tcs.TrySetResult(false);

                Debug.LogWarning("Permission request timed out");
            }, null, 10000, System.Threading.Timeout.Infinite); // 10 second timeout

            // When the task completes (either by result or timeout), dispose the timer
            tcs.Task.ContinueWith(_ => timeoutTimer.Dispose());

            // Request permission
            RequestLocationPermission();

            // Return the task that will complete when permission result is received or timeout occurs
            return tcs.Task;
        }

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
                IsInitialized = true; // Update initialization status
                LocationPermissionResult?.Invoke(true);
            }
#elif UNITY_IOS
        // iOS permissions are requested automatically when Input.location is used
        HasLocationPermission = Input.location.isEnabledByUser;
        IsInitialized = HasLocationPermission; // Update initialization status
        LocationPermissionResult?.Invoke(HasLocationPermission);
#else
        // For editor/desktop, we assume permission is granted
        HasLocationPermission = true;
        IsInitialized = true; // Update initialization status
        LocationPermissionResult?.Invoke(true);
#endif
        }

#if PLATFORM_ANDROID
        private void OnPermissionGranted(string permissionName)
        {
            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = true;
                IsInitialized = true; // Update initialization status
                LocationPermissionResult?.Invoke(true);
            }
        }

        private void OnPermissionDenied(string permissionName)
        {
            if (permissionName == Permission.FineLocation)
            {
                HasLocationPermission = false;
                IsInitialized = false; // Update initialization status
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
                // Update initialization status based on permission
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