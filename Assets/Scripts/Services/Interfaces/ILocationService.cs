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

        // Spectator mode — inject player's position instead of real GPS
        void InjectLocation(Vector2 location);
        void ClearInjection();
    }
}