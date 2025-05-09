using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public interface ILocationService
{
    event Action<float, float> LocationUpdated;
    bool IsInitialized { get; }
    bool IsRunning { get; }
    Task<bool> Initialize();
    void StartLocationUpdates();
    void StopLocationUpdates();
    Vector2 GetLastKnownLocation();
    float GetPositionAccuracy();
}

