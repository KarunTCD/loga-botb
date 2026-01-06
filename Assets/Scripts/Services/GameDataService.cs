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

        [Header("Audio Event Lookup")]
        [SerializeField] private AudioEventLookup audioEventLookup;

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
            public int characterId;        // Primary identifier (integer)
            public string characterName;   // Display name
            public float latitude;
            public float longitude;
            public string characterAudioEvent;
            public string portalType;
            public int portalJumpDistance;
            public string portalActivationAudio;
            public bool hasReward;
            public POIRewardData reward;
            public bool hasMultipleVariants;
            public int narrationVariantCount;
            public int maxNavigationCues = 4;  // Default: 4 navigation cue snippets
        }

        [System.Serializable]
        public class POIRewardData
        {
            public int rewardId;          // Primary identifier (integer)  
            public string rewardName;     // Display name
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
                InitializationProgress = 0.1f;

                bool success = await LoadGameDataAsync();

                if (success)
                {
                    IsInitialized = true;
                    InitializationProgress = 1f;
                    DebugLog("GameDataService: Initialization complete");

                    // Debug AudioEventLookup status
                    if (audioEventLookup != null)
                    {
                        DebugLog($"GameDataService: AudioEventLookup available with {audioEventLookup.TotalMappingCount} mappings");
                    }
                    else
                    {
                        Debug.LogWarning("GameDataService: No AudioEventLookup assigned - audio events will not work");
                    }

                    return true;
                }
                else
                {
                    Debug.LogError("GameDataService: Failed to load game data");
                    return false;
                }
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

        #endregion

        #region AudioEventLookup Management

        /// <summary>
        /// Set the AudioEventLookup reference (called by ServiceManager)
        /// </summary>
        public void SetAudioEventLookup(AudioEventLookup lookup)
        {
            audioEventLookup = lookup;
            if (lookup != null)
            {
                DebugLog($"GameDataService: AudioEventLookup assigned with {lookup.TotalMappingCount} mappings");
                DebugLog($"  Character events: {lookup.characterAudioEvents.Count}");
                DebugLog($"  Portal events: {lookup.portalAudioEvents.Count}");

                // Debug log all mappings for verification
                if (enableDebugLogs)
                {
                    foreach (var mapping in lookup.characterAudioEvents)
                    {
                        DebugLog($"  Character audio: '{mapping.eventName}' → {mapping.eventReference}");
                    }
                    foreach (var mapping in lookup.portalAudioEvents)
                    {
                        DebugLog($"  Portal audio: '{mapping.eventName}' → {mapping.eventReference}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("GameDataService: AudioEventLookup set to null");
            }
        }

        /// <summary>
        /// Get AudioEventLookup reference (for external access)
        /// </summary>
        public AudioEventLookup GetAudioEventLookup()
        {
            return audioEventLookup;
        }

        /// <summary>
        /// Check if AudioEventLookup is available
        /// </summary>
        public bool HasAudioEventLookup()
        {
            return audioEventLookup != null;
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

        public POIData GetPOIData(int poiId)
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

            if (audioEventLookup != null)
            {
                var eventRef = audioEventLookup.GetEventReference(eventName);

                if (eventRef.IsNull)
                {
                    Debug.LogWarning($"GameDataService: Audio event '{eventName}' not found in AudioEventLookup");
                }
                else
                {
                    DebugLog($"GameDataService: Found audio event '{eventName}' → {eventRef}");
                }

                return eventRef;
            }

            // No lookup available - this is the main issue
            Debug.LogError($"GameDataService: No AudioEventLookup assigned, cannot convert '{eventName}'");
            return new EventReference();
        }

        #endregion

        #region File Loading

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
                        DebugLog($"GameDataService: Loaded JSON from StreamingAssets (Android)");
                        return www.downloadHandler.text;
                    }
                }
            }
            // Other platforms
            else if (System.IO.File.Exists(path))
            {
                string content = await Task.Run(() => System.IO.File.ReadAllText(path));
                DebugLog($"GameDataService: Loaded JSON from StreamingAssets");
                return content;
            }

            // Fallback to Resources
            string resourceName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset != null)
            {
                DebugLog($"GameDataService: Loaded JSON from Resources folder");
                return asset.text;
            }

            throw new Exception($"Could not load {fileName} from StreamingAssets or Resources");
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

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [ContextMenu("Log Data Summary")]
        public void LogDataSummary()
        {
            if (!IsDataLoaded)
            {
                Debug.Log("GameDataService: No data loaded yet");
                return;
            }

            Debug.Log("=== GAME DATA SUMMARY ===");
            Debug.Log($"Time Layers: {rawGameData.timeLayers?.Count ?? 0}");

            int totalPOIs = 0;
            int rewardPOIs = 0;
            foreach (var layer in rawGameData.timeLayers ?? new List<TimeLayerData>())
            {
                if (layer.pois != null)
                {
                    totalPOIs += layer.pois.Count;
                    rewardPOIs += layer.pois.Count(p => p.hasReward);
                }
                Debug.Log($"  - {layer.layerName}: {layer.pois?.Count ?? 0} POIs");
            }

            Debug.Log($"Total POIs: {totalPOIs}");
            Debug.Log($"POIs with Rewards: {rewardPOIs}");
            Debug.Log($"Proximity Radius: {GameConfig.proximityRadius}m");
            Debug.Log($"Dialogue Radius: {GameConfig.dialogueRadius}m");

            // Audio Event Lookup status
            if (audioEventLookup != null)
            {
                Debug.Log($"AudioEventLookup: {audioEventLookup.TotalMappingCount} mappings available");
            }
            else
            {
                Debug.Log("AudioEventLookup: NOT ASSIGNED");
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [ContextMenu("Debug Audio Mappings")]
        public void DebugAudioMappings()
        {
            if (audioEventLookup == null)
            {
                Debug.LogError("GameDataService: No AudioEventLookup assigned");
                return;
            }

            Debug.Log($"=== AUDIO EVENT MAPPINGS ({audioEventLookup.TotalMappingCount}) ===");
            audioEventLookup.DebugAllMappings();

            // Test some common events from JSON
            var testEvents = new[] { "oak_audio", "celtic_farmer_audio", "modern_raven_audio", "battle_fox_audio", "portal_audio" };

            Debug.Log("=== TESTING JSON AUDIO EVENTS ===");
            foreach (var eventName in testEvents)
            {
                var eventRef = GetAudioEventReference(eventName);
                Debug.Log($"Test '{eventName}': {(eventRef.IsNull ? "NOT FOUND" : "FOUND")}");
            }
        }

        #endregion
    }
}