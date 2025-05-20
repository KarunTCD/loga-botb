using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public interface IFirebaseService : IService
    {
        Task<bool> InitializeSession(string sessionId, string playerName);
        Task<bool> ConnectToSession(string sessionId, Action<float, float, float> onPositionUpdated, Action<List<string>> onPOIsUpdated);
        void UpdatePlayerData(string sessionId, float latitude, float longitude, float heading);
        void SaveDiscoveredPOI(string sessionId, string poiId);
        void DisconnectFromSession(string sessionId);
        Task DeleteSession(string sessionId);
    }
}