using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LoGa.LudoEngine.Services
{
    public interface ILocationService : IService
    {
        bool IsRunning { get; }
        Vector2 CurrentLocation { get; }
        float PositionAccuracy { get; }

        void StartLocationUpdates();
        void StopLocationUpdates();
        Vector2 GetCurrentLocation();
    }
}