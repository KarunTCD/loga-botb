using System;

namespace LoGa.LudoEngine.Services
{
    public interface IPermissionService : IService
    {
        event Action<bool> LocationPermissionResult;
        bool HasLocationPermission { get; }
        void CheckLocationPermission();
        void RequestLocationPermission();
    }
}