using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMODUnity;

namespace LoGa.LudoEngine.Services
{
    public interface IGameDataService : IService
    {
        // Configuration
        GameDataService.GameConfigurationData GameConfig { get; }
        bool IsDataLoaded { get; }

        // Data Loading
        Task<bool> LoadGameDataAsync();

        // Time Layer Operations
        List<GameDataService.TimeLayerData> GetAllTimeLayerData();
        GameDataService.TimeLayerData GetTimeLayerData(string layerId);
        GameDataService.TimeLayerData GetTimeLayerData(int layerIndex);
        GameDataService.TimeLayerData GetDefaultTimeLayerData();

        // POI Operations
        List<GameDataService.POIData> GetPOIsForTimeLayer(string layerId);
        List<GameDataService.POIData> GetPOIsForTimeLayer(int layerIndex);

        // CHANGED: Use int characterId instead of string
        GameDataService.POIData GetPOIData(int characterId);

        // Audio Event Conversion
        EventReference GetAudioEventReference(string eventName);

        // Events
        event Action<GameDataService.GameConfigurationData> OnGameConfigLoaded;
        event Action OnDataLoaded;
    }
}