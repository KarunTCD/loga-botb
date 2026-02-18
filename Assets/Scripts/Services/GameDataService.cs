using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
using FMODUnity;

namespace LoGa.LudoEngine.Services
{
    public class GameDataService : MonoBehaviour, IGameDataService
    {
        [Header("Configuration")]
        [SerializeField] private string jsonFileName = "game_data.json";

        [SerializeField] private bool enableDebugLogs = true;

        #region JSON Data Structures

        [System.Serializable]
        private class GameData
        {
            public GameConfigurationData gameConfiguration;
            public List<TimeLayerData> timeLayers;
        }

        [System.Serializable]
        public class GameConfigurationData
        {
            public int defaultTimeLayer;
            public float proximityRadius;
            public float dialogueRadius;
            public float maxCueRadius;
            public float targetLockTime;
            public float targetLockAngle;
            public float targetBreakAngle;
            public float cueStagingDelay;
            public float cyclePauseDelay;
            public int baseMaxActiveCues;
            public int maxMaxActiveCues;
            public int navigationUpgradeThreshold;
            public float maxTargetingDistance;
            public int maxPlayerHealth;
            public GameBoundsData gameBounds;
            public string ambientAudioEvent;

            public string welcomeGreetingEvent;
            public string rewardAnnouncementEvent;
            public string timePortalAudioEvent;
            public CombatAudioEvents combatAudioEvents;
        }

        [System.Serializable]
        public class CombatAudioEvents
        {
            public string mercenaryEncounter;
            public string mercenaryFootsteps;
            public string mercenaryAttack;
            public string attackImpact;
            public string heartbeat;
            public string berryAmbient;
            public string berryCollection;
        }

        [System.Serializable]
        public class GameBoundsData
        {
            public float north;
            public float south;
            public float east;
            public float west;

            public bool IsWithinBounds(float lat, float lon)
            {
                return lat >= south && lat <= north && lon >= west && lon <= east;
            }

            public Vector2 GetCenter()
            {
                return new Vector2((north + south) / 2f, (east + west) / 2f);
            }
        }

        [System.Serializable]
        public class TimeLayerData
        {
            public string id;
            public string layerName;
            public int layerIndex;
            public string ambientAudioEvent;
            public List<POIData> pois;
        }

        [System.Serializable]
        public class POIData
        {
            public string characterId;
            public string characterName;
            public float latitude;
            public float longitude;
            public string characterAudioEvent;
            public string navigationCueEvent;    
            public int navigationCueCount;
            public string portalType;
            public int portalJumpDistance;
            public string portalActivationAudio;
            public bool hasReward;
            public POIRewardData reward;
            public bool hasMultipleVariants;
            public int narrationVariantCount;
        }

        [System.Serializable]
        public class POIRewardData
        {
            public int id;           
            public string name;       
            public string audioEvent; 
        }

        #endregion

        #region Service Properties

        public string ServiceName => "Game Data Service";
        public bool IsInitialized { get; private set; }
        public float InitializationProgress { get; private set; }

        public GameConfigurationData GameConfig { get; private set; }
        public bool IsDataLoaded { get; private set; }

        private GameData rawGameData;

        public event Action<GameConfigurationData> OnGameConfigLoaded;
        public event Action OnDataLoaded;

        #endregion

        #region Service Lifecycle

        public async Task<bool> InitializeAsync()
        {
            DebugLog("GameDataService: Starting initialization");

            try
            {
                InitializationProgress = 0.5f;

                // Don't load game_data.json automatically
                // Data will be loaded when SiteManager calls LoadSiteData()

                IsInitialized = true;
                InitializationProgress = 1f;
                DebugLog("GameDataService: Initialization complete (waiting for site selection)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameDataService: Initialization failed - {e.Message}");
                return false;
            }
        }

        public async Task<bool> LoadGameDataAsync()
        {
            try
            {
                InitializationProgress = 0.3f;

                string jsonText = await LoadJsonFile(jsonFileName);
                InitializationProgress = 0.7f;

                rawGameData = JsonUtility.FromJson<GameData>(jsonText);

                if (rawGameData?.gameConfiguration == null)
                {
                    throw new Exception("Invalid game data structure - missing gameConfiguration");
                }

                if (rawGameData.timeLayers == null || rawGameData.timeLayers.Count == 0)
                {
                    throw new Exception("Invalid game data structure - missing timeLayers");
                }

                GameConfig = rawGameData.gameConfiguration;
                IsDataLoaded = true;

                DebugLog($"GameDataService: Loaded {rawGameData.timeLayers.Count} time layers");
                DebugLog($"GameDataService: Default time layer: {GameConfig.defaultTimeLayer}");
                DebugLog($"GameDataService: Proximity radius: {GameConfig.proximityRadius}m");

                OnGameConfigLoaded?.Invoke(GameConfig);
                OnDataLoaded?.Invoke();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameDataService: Failed to load data - JSON parse error: {e.Message}");
                return false;
            }
        }

        public void Reset()
        {
            Debug.Log("GameDataService: Reset called");

            // Clear loaded data
            rawGameData = null;
            GameConfig = null;
            IsDataLoaded = false;

            // Reset initialization
            IsInitialized = false;
            InitializationProgress = 0f;
        }

        #endregion

        #region Data Access Methods

        public List<TimeLayerData> GetAllTimeLayerData()
        {
            return rawGameData?.timeLayers ?? new List<TimeLayerData>();
        }

        public TimeLayerData GetTimeLayerData(string layerId)
        {
            return rawGameData?.timeLayers?.Find(l => l.id == layerId);
        }

        public TimeLayerData GetTimeLayerData(int layerIndex)
        {
            return rawGameData?.timeLayers?.Find(l => l.layerIndex == layerIndex);
        }

        public TimeLayerData GetDefaultTimeLayerData()
        {
            return GetTimeLayerData(GameConfig?.defaultTimeLayer ?? 0);
        }

        public List<POIData> GetAllPOIData()
        {
            var allPOIs = new List<POIData>();
            if (rawGameData?.timeLayers != null)
            {
                foreach (var layer in rawGameData.timeLayers)
                {
                    if (layer.pois != null)
                    {
                        allPOIs.AddRange(layer.pois);
                    }
                }
            }
            return allPOIs;
        }

        public List<POIData> GetPOIsForTimeLayer(string layerId)
        {
            var layer = GetTimeLayerData(layerId);
            return layer?.pois ?? new List<POIData>();
        }

        public List<POIData> GetPOIsForTimeLayer(int layerIndex)
        {
            var layer = GetTimeLayerData(layerIndex);
            return layer?.pois ?? new List<POIData>();
        }

        public POIData GetPOIData(string poiId)
        {
            if (rawGameData?.timeLayers == null) return null;

            foreach (var layer in rawGameData.timeLayers)
            {
                var poi = layer.pois?.Find(p => p.characterId == poiId);
                if (poi != null) return poi;
            }
            return null;
        }

        public bool IsLocationWithinGameBounds(float latitude, float longitude)
        {
            return GameConfig?.gameBounds?.IsWithinBounds(latitude, longitude) ?? false;
        }

        public EventReference GetAudioEventReference(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogWarning("GameDataService: Cannot get audio event reference for empty event name");
                return new EventReference();
            }

            // Create EventReference directly from event name
            EventReference eventRef = RuntimeManager.PathToEventReference(eventName);

            if (eventRef.IsNull)
            {
                Debug.LogWarning($"GameDataService: Audio event '{eventName}' not found in loaded banks");
            }
            else
            {
                Debug.Log($"GameDataService: Found audio event '{eventName}'");
            }

            return eventRef;
        }

        #endregion

        #region File Loading

        /// <summary>
        /// SINGLE FILE READING METHOD - Used by all data loading methods
        /// Handles Android, PC, and Resources fallback
        /// </summary>
        private async Task<string> LoadJsonFile(string fileName)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);

            // Android platform
            if (Application.platform == RuntimePlatform.Android)
            {
                using (var www = UnityEngine.Networking.UnityWebRequest.Get(path))
                {
                    var operation = www.SendWebRequest();
                    while (!operation.isDone) await Task.Yield();

                    if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        DebugLog($"GameDataService: Loaded JSON from StreamingAssets (Android): {fileName}");
                        return www.downloadHandler.text;
                    }
                }
            }
            // Other platforms
            else if (System.IO.File.Exists(path))
            {
                string content = await Task.Run(() => System.IO.File.ReadAllText(path));
                DebugLog($"GameDataService: Loaded JSON from StreamingAssets: {fileName}");
                return content;
            }

            // Fallback to Resources
            string resourceName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset != null)
            {
                DebugLog($"GameDataService: Loaded JSON from Resources folder: {fileName}");
                return asset.text;
            }

            throw new Exception($"Could not load {fileName} from StreamingAssets or Resources");
        }

        /// <summary>
        /// Load data from a specific site folder
        /// Called by SiteManager when site is selected
        /// Uses LoadJsonFile() for platform compatibility
        /// </summary>
        public async Task<bool> LoadSiteData(string siteFolderName)
        {
            try
            {
                Debug.Log($"GameDataService: Loading site data for: {siteFolderName}");

                // Build relative path for LoadJsonFile
                string relativePath = System.IO.Path.Combine("Sites", siteFolderName, "site_data.json");

                // Use the existing LoadJsonFile helper (handles Android, PC, Resources)
                string json = await LoadJsonFile(relativePath);

                // Parse JSON
                rawGameData = JsonUtility.FromJson<GameData>(json);

                if (rawGameData?.gameConfiguration == null)
                {
                    Debug.LogError("GameDataService: Invalid site data - missing gameConfiguration");
                    return false;
                }

                if (rawGameData.timeLayers == null || rawGameData.timeLayers.Count == 0)
                {
                    Debug.LogError("GameDataService: Invalid site data - missing timeLayers");
                    return false;
                }

                // Update cached config
                GameConfig = rawGameData.gameConfiguration;
                IsDataLoaded = true;

                DebugLog($"GameDataService: Loaded site data for {siteFolderName}");
                DebugLog($"  Time layers: {rawGameData.timeLayers.Count}");
                DebugLog($"  POIs: {GetAllPOIData().Count}");
                DebugLog($"  Max health: {GameConfig.maxPlayerHealth}");
                DebugLog($"  Proximity radius: {GameConfig.proximityRadius}m");

                // Fire events
                OnGameConfigLoaded?.Invoke(GameConfig);
                OnDataLoaded?.Invoke();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"GameDataService: Failed to load site data: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clear currently loaded site data
        /// Called by SiteManager when unloading site
        /// </summary>
        public void ClearSiteData()
        {
            DebugLog("GameDataService: Clearing site data");

            rawGameData = null;
            GameConfig = null;
            IsDataLoaded = false;

            DebugLog("GameDataService: ✓ Site data cleared");
        }

        #endregion

        #region Utility

        private void DebugLog(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log(message);
            }
        }
        
        #endregion
    }
}