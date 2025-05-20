using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LoGa.LudoEngine.Services
{
    public interface ILocationService : IService
    {
        event Action<float, float> LocationUpdated;
        bool IsRunning { get; }
        void StartLocationUpdates();
        void StopLocationUpdates();
        Vector2 GetLastKnownLocation();
        float GetPositionAccuracy();
    }
}