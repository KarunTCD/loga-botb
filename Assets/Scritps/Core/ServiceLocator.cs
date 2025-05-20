// script used to manage all the services used in the game following the service locator pattern
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    public static class ServiceLocator
    {
        // Dictionary that stores different services
        private static readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        // function to register a service
        public static void RegisterService<T>(T service)
        {
            services[typeof(T)] = service;
            Debug.Log($"Service registered: {typeof(T).Name}");
        }

        // -----------------------------------
        // function to get a service
        public static T GetService<T>()
        {
            if (services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            Debug.LogError($"Service not found: {typeof(T).Name}");
            return default;
        }

        // -----------------------------------
        // function to unregister a service
        public static void UnregisterService<T>()
        {
            if (services.ContainsKey(typeof(T)))
            {
                services.Remove(typeof(T));
                Debug.Log($"Service unregistered: {typeof(T).Name}");
            }
        }

        // ----------------------------------
        // function to remove all services
        public static void ClearAllServices()
        {
            services.Clear();
        }

        // ----------------------------------
        // Ensures a service is initialized, waiting for initialization to complete if necessary.
        public static async Task<T> GetInitializedService<T>(int timeoutMs = 10000) where T : IService
        {
            T service = GetService<T>();
            if (service == null)
            {
                Debug.LogError($"Service {typeof(T).Name} not found in ServiceLocator");
                return default;
            }

            // Return immediately if already initialized
            if (service.IsInitialized)
                return service;

            try
            {
                // Create a cancellation token for the timeout
                using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                {
                    // Create tasks for initialization and timeout
                    var initTask = service.InitializeAsync();
                    var timeoutTask = Task.Delay(timeoutMs, cts.Token);

                    // Wait for either initialization to complete or timeout
                    var completedTask = await Task.WhenAny(initTask, timeoutTask);

                    if (completedTask == initTask)
                    {
                        // Cancel the timeout task
                        cts.Cancel();

                        // Check if initialization was successful
                        bool success = await initTask;

                        if (success)
                        {
                            return service;
                        }
                        else
                        {
                            Debug.LogWarning($"Service {typeof(T).Name} initialization failed");
                            return default;
                        }
                    }
                    else
                    {
                        // Timeout occurred
                        Debug.LogWarning($"Initialization timeout for service {typeof(T).Name} after {timeoutMs}ms");
                        return default;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error initializing service {typeof(T).Name}: {e.Message}");
                return default;
            }
        }
    }
}