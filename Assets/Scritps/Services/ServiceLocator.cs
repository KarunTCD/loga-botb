// script used to manage all the services used in the game following the service locator pattern
using System;
using System.Collections.Generic;
using UnityEngine;

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
}