using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
    public class TimeLayerManager : MonoBehaviour
    {
        public static TimeLayerManager Instance { get; private set; }

        [Header("Editor Fallback Configuration (JSON will override completely)")]
        [SerializeField] private List<TimeLayer> timeLayers; // Keep original name to preserve inspector data
        [SerializeField] private int defaultLayerIndex = 0;

        [Header("Transition Settings")]
        [SerializeField] private float transitionDuration = 3f;
        [SerializeField] private EventReference timePortalAudio;

        // STRICT SEPARATION: Track data source
        private bool isUsingJSONData = false;
        private IGameDataService gameDataService;
        // Note: timeLayers list will be either JSON or editor layers, never mixed

        public event System.Action<TimeLayer, TimeLayer> TimeLayerChanging;
        public event System.Action<TimeLayer> TimeLayerChanged;

        private TimeLayer currentLayer;
        private bool isTransitioning = false;

        public TimeLayer CurrentLayer => currentLayer;
        public bool IsTransitioning => isTransitioning;
        public int CurrentLayerIndex => timeLayers.IndexOf(currentLayer);
        public int TotalLayers => timeLayers.Count;

        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // STRICT SEPARATION: Determine data source ONCE at startup
            gameDataService = ServiceLocator.GetService<IGameDataService>();
            isUsingJSONData = (gameDataService != null && gameDataService.IsDataLoaded);

            Debug.Log($"TimeLayerManager: Data source determined - Using {(isUsingJSONData ? "JSON" : "Editor")} mode");

            if (isUsingJSONData)
            {
                InitializeFromGameData();
            }
            else
            {
                InitializeFromEditorData();
            }

            // Validate we have a current layer
            if (currentLayer == null)
            {
                Debug.LogError("TimeLayerManager: No current layer set after initialization!");
                if (timeLayers != null && timeLayers.Count > 0)
                {
                    currentLayer = timeLayers[0];
                    Debug.LogWarning($"TimeLayerManager: Defaulting to first layer: {currentLayer.layerName}");
                }
            }

            Debug.Log($"TimeLayerManager: Initialized with {timeLayers?.Count ?? 0} layers in {(isUsingJSONData ? "JSON" : "Editor")} mode");
            Debug.Log($"TimeLayerManager: Current layer: {currentLayer?.layerName ?? "None"}");
        }

        #region JSON Data Integration - STRICT SEPARATION

        /// <summary>
        /// Initialize completely from JSON data, ignore all editor settings
        /// </summary>
        private void InitializeFromGameData()
        {
            if (!isUsingJSONData || gameDataService?.GameConfig == null)
            {
                Debug.LogError("TimeLayerManager: InitializeFromGameData called but JSON data not available");
                InitializeFromEditorData(); // Fallback
                return;
            }

            var config = gameDataService.GameConfig;
            var timeLayerDataList = gameDataService.GetAllTimeLayerData();

            Debug.Log($"TimeLayerManager: JSON mode - completely replacing editor configuration");
            Debug.Log($"TimeLayerManager: Loading {timeLayerDataList.Count} time layers from JSON");

            // Apply JSON configuration (override editor values completely)
            defaultLayerIndex = config.defaultTimeLayer;

            // Create time layers from JSON data ONLY
            if (timeLayerDataList != null && timeLayerDataList.Count > 0)
            {
                timeLayers = CreateTimeLayersFromJSONData(timeLayerDataList);
                Debug.Log($"TimeLayerManager: Created {timeLayers.Count} time layers from JSON data");
            }
            else
            {
                Debug.LogError("TimeLayerManager: No time layer data in JSON!");
                timeLayers = new List<TimeLayer>();
            }

            // Set default layer from JSON configuration
            SetDefaultLayerFromData(config.defaultTimeLayer);

            Debug.Log($"TimeLayerManager: JSON initialization complete - {timeLayers.Count} layers, default: {currentLayer?.layerName}");
        }

        /// <summary>
        /// Create time layers from JSON data only, no editor references
        /// </summary>
        private List<TimeLayer> CreateTimeLayersFromJSONData(List<GameDataService.TimeLayerData> timeLayerDataList)
        {
            var newTimeLayers = new List<TimeLayer>();

            foreach (var layerData in timeLayerDataList.OrderBy(l => l.layerIndex))
            {
                TimeLayer timeLayer = new TimeLayer();

                // Set basic properties from JSON
                timeLayer.layerName = layerData.layerName;
                timeLayer.layerIndex = layerData.layerIndex;

                // Convert ambient audio event through GameDataService lookup
                if (!string.IsNullOrEmpty(layerData.ambientAudioEvent))
                {
                    timeLayer.ambientSound = gameDataService.GetAudioEventReference(layerData.ambientAudioEvent);
                    Debug.Log($"TimeLayerManager: Set ambient audio for {timeLayer.layerName}: {layerData.ambientAudioEvent}");
                }

                // Initialize empty POI list - POIs will be created by POIManager from JSON
                timeLayer.pois = new List<POI>();

                newTimeLayers.Add(timeLayer);

                Debug.Log($"TimeLayerManager: Created JSON time layer '{timeLayer.layerName}' (Index: {timeLayer.layerIndex})");
            }

            return newTimeLayers;
        }

        /// <summary>
        /// Set default layer from JSON configuration
        /// </summary>
        private void SetDefaultLayerFromData(int defaultIndex)
        {
            if (timeLayers != null && defaultIndex >= 0 && defaultIndex < timeLayers.Count)
            {
                currentLayer = timeLayers[defaultIndex];
                Debug.Log($"TimeLayerManager: Set default layer to '{currentLayer.layerName}' from JSON (Index: {defaultIndex})");
            }
            else
            {
                Debug.LogError($"TimeLayerManager: Invalid default layer index {defaultIndex}, using first layer");
                if (timeLayers != null && timeLayers.Count > 0)
                {
                    currentLayer = timeLayers[0];
                }
            }
        }

        #endregion

        #region Editor Data Fallback - STRICT SEPARATION

        /// <summary>
        /// Initialize from editor data only when JSON is not available
        /// </summary>
        private void InitializeFromEditorData()
        {
            Debug.Log("TimeLayerManager: Editor fallback mode - using inspector configuration");

            if (timeLayers == null || timeLayers.Count == 0)
            {
                Debug.LogError("TimeLayerManager: No editor time layers configured!");
                return;
            }

            // Use editor configuration directly (timeLayers list remains unchanged)
            // defaultLayerIndex is already set from inspector
            defaultLayerIndex = Mathf.Clamp(defaultLayerIndex, 0, timeLayers.Count - 1);

            // Set default layer from editor configuration
            currentLayer = timeLayers[defaultLayerIndex];

            Debug.Log($"TimeLayerManager: Using editor configuration - {timeLayers.Count} layers, starting in layer: {currentLayer.layerName} (Index: {defaultLayerIndex})");

            // Log editor POI counts for validation
            foreach (var layer in timeLayers)
            {
                Debug.Log($"TimeLayerManager: Editor layer '{layer.layerName}' has {layer.pois?.Count ?? 0} POIs");
            }
        }

        #endregion

        #region Core Methods

        public List<TimeLayer> GetAllTimeLayers() => timeLayers;
        public EventReference GetTimePortalEvent() => timePortalAudio;

        public bool CanTransitionTo(TimeLayer targetLayer)
        {
            if (isTransitioning) return false;
            if (targetLayer == currentLayer) return false;
            if (targetLayer == null) return false;
            return timeLayers.Contains(targetLayer);
        }

        public bool CanTransitionTo(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= timeLayers.Count) return false;
            return CanTransitionTo(timeLayers[layerIndex]);
        }

        public void TransitionToLayer(TimeLayer newLayer)
        {
            if (!CanTransitionTo(newLayer))
            {
                Debug.LogWarning($"Cannot transition to layer: {newLayer?.layerName}");
                return;
            }

            StartCoroutine(PerformTransition(newLayer));
        }

        public void TransitionToLayer(int layerIndex)
        {
            if (CanTransitionTo(layerIndex))
            {
                TransitionToLayer(timeLayers[layerIndex]);
            }
        }

        private System.Collections.IEnumerator PerformTransition(TimeLayer newLayer)
        {
            isTransitioning = true;
            TimeLayer previousLayer = currentLayer;

            Debug.Log($"Transitioning from {previousLayer.layerName} to {newLayer.layerName}");

            GameManager.Instance?.SuspendNavigationAudio("time_portal_transition");

            TimeLayerChanging?.Invoke(previousLayer, newLayer);

            yield return new WaitForSeconds(transitionDuration);

            currentLayer = newLayer;

            TimeLayerChanged?.Invoke(newLayer);

            isTransitioning = false;
            Debug.Log($"Transition to {newLayer.layerName} complete");
        }

        public TimeLayer GetForwardLayer(int jumpDistance = 1)
        {
            int targetIndex = CurrentLayerIndex + jumpDistance;
            if (targetIndex < timeLayers.Count)
                return timeLayers[targetIndex];
            return null;
        }

        public TimeLayer GetBackwardLayer(int jumpDistance = 1)
        {
            int targetIndex = CurrentLayerIndex - jumpDistance;
            if (targetIndex >= 0)
                return timeLayers[targetIndex];
            return null;
        }

        public bool CanGoBackward() => GetBackwardLayer() != null;
        public bool CanGoForward() => GetForwardLayer() != null;

        public void OnPOILayerLoadComplete()
        {
            // Resume navigation now that POIs are loaded
            GameManager.Instance?.ResumeNavigationAudio("poi_layer_load_complete");
            Debug.Log("TimeLayerManager: POI layer loaded, navigation resumed");
            
        }

        /// <summary>
        /// Reload the current time layer (used after reset)
        /// </summary>
        public void ReloadCurrentLayer()
        {
            if (currentLayer == null)
            {
                Debug.LogError("TimeLayerManager: Cannot reload - no current layer");
                return;
            }

            Debug.Log($"TimeLayerManager: Reloading current layer: {currentLayer.layerName}");

            // Force POIManager to reload POIs for this layer
            TimeLayerChanged?.Invoke(currentLayer);
        }

        #endregion

        #region Debug and Validation

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void LogTimeLayerInfo()
        {
            Debug.Log("=== TIME LAYER INFO ===");
            Debug.Log($"Data Source: {(isUsingJSONData ? "JSON" : "Editor")}");
            Debug.Log($"Current Layer: {currentLayer?.layerName} (Index: {CurrentLayerIndex})");
            Debug.Log($"Total Layers: {timeLayers?.Count ?? 0}");

            if (timeLayers != null)
            {
                for (int i = 0; i < timeLayers.Count; i++)
                {
                    var layer = timeLayers[i];
                    Debug.Log($"  {i}: {layer.layerName} - {layer.pois?.Count ?? 0} POIs");
                }
            }
        }

        /// <summary>
        /// Validate that we don't have mixed data sources
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void ValidateDataIntegrity()
        {
            if (isUsingJSONData)
            {
                // In JSON mode, layers should not have pre-populated POIs
                foreach (var layer in timeLayers)
                {
                    if (layer.pois != null && layer.pois.Count > 0)
                    {
                        Debug.LogWarning($"TimeLayerManager: JSON mode but layer '{layer.layerName}' has {layer.pois.Count} POIs - these should be empty in JSON mode");
                    }
                }
            }
            else
            {
                // In Editor mode, we should have editor layers
                if (timeLayers == null || timeLayers.Count == 0)
                {
                    Debug.LogError("TimeLayerManager: Editor mode but no editor time layers configured");
                }
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            // Clean up any remaining POIs in layers (whether JSON or Editor)
            if (timeLayers != null)
            {
                foreach (var layer in timeLayers)
                {
                    if (layer?.pois != null)
                    {
                        foreach (var poi in layer.pois)
                        {
                            poi?.Cleanup();
                        }
                        layer.pois.Clear();
                    }
                }
            }

            Debug.Log("TimeLayerManager: Cleanup completed");
        }

        #endregion
    }
}