using System;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public interface IPermissionService : IService
    {
        event Action<bool> LocationPermissionResult;
        bool HasLocationPermission { get; }
        Task<bool> RequestBluetoothPermissions();
        void CheckLocationPermission();
        void RequestLocationPermission();
        Task<bool> RequestBluetoothPermissions();
    }
}