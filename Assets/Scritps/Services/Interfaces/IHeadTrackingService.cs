using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IHeadTrackingService
{
    event Action<float> HeadingUpdated;
    bool IsInitialized { get; }
    bool IsCalibrated { get; }
    float CurrentHeading { get; }
    void Initialize();
    void StartTracking();
    void StopTracking();
    void CalibrateToNorth();
    void SetDirectionDegrees(float degrees);
}
