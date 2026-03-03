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

        [Header("Transition Settings - Will be overridden by JSON")]
        [SerializeField] private float transitionDuration = 3f;
        private EventReference timePortalAudio;

        private IGameDataService gameDataService;
        private List<TimeLayer> timeLayers = new List<TimeLayer>();
        private TimeLayer currentLayer;
        private bool isTransitioning = false;
        private bool isInitialized = false;

        public event System.Action<TimeLayer, TimeLayer> TimeLayerChanging;
        public event System.Action<TimeLayer> TimeLayerChanged;

        public TimeLayer CurrentLayer => currentLayer;
        public bool IsTransitioning => isTransitioning;
        public int CurrentLayerIndex => currentLayer?.layerIndex ?? -1;
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
            gameDataService = ServiceLocator.GetService<IGameDataService>();

            if (gameDataService == null)
            {
                Debug.LogError("TimeLayerManager: CRITICAL - GameDataService not found! Cannot function without JSON data.");
                enabled = false;
                return;
            }

            // Subscribe to JSON loading
            gameDataService.OnDataLoaded += InitializeFromJSON;

            Debug.Log("TimeLayerManager: Waiting for JSON data - no fallback available");
        }

        private void InitializeFromJSON()
        {
            Debug.Log("TimeLayerManager: JSON data loaded, initializing...");

            var config = gameDataService.GameConfig;
            var timeLayerDataList = gameDataService.GetAllTimeLayerData();

            if (timeLayerDataList == null || timeLayerDataList.Count == 0)
            {
                Debug.LogError("TimeLayerManager: CRITICAL - No time layers in JSON! Game cannot start.");
                enabled = false;
                return;
            }

            if (!string.IsNullOrEmpty(config.timePortalAudioEvent))
            {
                timePortalAudio = gameDataService.GetAudioEventReference(config.timePortalAudioEvent);
                Debug.Log("TimeLayerManager: ✓ Time portal audio loaded from JSON");
            }
            else
            {
                Debug.Log("TimeLayerManager: No time portal audio event in JSON - transitions will be silent");
            }

            // Build layers from JSON data
            timeLayers = BuildLayersFromJSON(timeLayerDataList);

            // Set current layer from JSON default
            int defaultIndex = config.defaultTimeLayer;
            SetCurrentLayerByIndex(defaultIndex);

            isInitialized = true;

            Debug.Log($"TimeLayerManager: Initialized with {timeLayers.Count} layers from JSON");
            Debug.Log($"TimeLayerManager: Current layer: '{currentLayer.layerName}' (Index: {currentLayer.layerIndex})");

            // Trigger initial layer load
            TimeLayerChanged?.Invoke(currentLayer);
        }

        private List<TimeLayer> BuildLayersFromJSON(List<GameDataService.TimeLayerData> layerDataList)
        {
            var layers = new List<TimeLayer>();

            foreach (var data in layerDataList.OrderBy(l => l.layerIndex))
            {
                TimeLayer layer = new TimeLayer
                {
                    layerName = data.layerName,
                    layerIndex = data.layerIndex,
                    pois = new List<POI>()
                };

                // Set ambient audio from JSON
                if (!string.IsNullOrEmpty(data.ambientAudioEvent))
                {
                    layer.ambientSound = gameDataService.GetAudioEventReference(data.ambientAudioEvent);
                }

                layers.Add(layer);
                Debug.Log($"TimeLayerManager: Built layer '{layer.layerName}' (Index: {layer.layerIndex})");
            }

            return layers;
        }

        private void SetCurrentLayerByIndex(int index)
        {
            var targetLayer = timeLayers.FirstOrDefault(l => l.layerIndex == index);

            if (targetLayer != null)
            {
                currentLayer = targetLayer;
                Debug.Log($"TimeLayerManager: Set current layer to '{currentLayer.layerName}' (Index: {index})");
            }
            else
            {
                Debug.LogError($"TimeLayerManager: CRITICAL - Invalid default layer index {index}!");
                if (timeLayers.Count > 0)
                {
                    currentLayer = timeLayers[0];
                    Debug.LogWarning($"TimeLayerManager: Fallback to first available layer: '{currentLayer.layerName}'");
                }
            }
        }

        // Public interface - only works after JSON loads
        public bool CanTransitionTo(TimeLayer targetLayer)
        {
            if (!isInitialized) return false;
            if (isTransitioning) return false;
            if (targetLayer == currentLayer) return false;
            if (targetLayer == null) return false;
            return timeLayers.Any(l => l.layerIndex == targetLayer.layerIndex);
        }

        public bool CanTransitionTo(int layerIndex)
        {
            if (!isInitialized) return false;
            var targetLayer = GetLayerByIndex(layerIndex);
            return CanTransitionTo(targetLayer);
        }

        public void TransitionToLayer(TimeLayer newLayer)
        {
            if (!isInitialized)
            {
                Debug.LogError("TimeLayerManager: Cannot transition - not initialized with JSON data yet");
                return;
            }

            if (!CanTransitionTo(newLayer))
            {
                Debug.LogWarning($"Cannot transition to layer: {newLayer?.layerName}");
                return;
            }

            StartCoroutine(PerformTransition(newLayer));
        }

        public void TransitionToLayer(int layerIndex)
        {
            var targetLayer = GetLayerByIndex(layerIndex);
            if (targetLayer != null)
            {
                TransitionToLayer(targetLayer);
            }
        }

        private System.Collections.IEnumerator PerformTransition(TimeLayer newLayer)
        {
            isTransitioning = true;
            TimeLayer previousLayer = currentLayer;

            Debug.Log($"Transitioning from {previousLayer.layerName} to {newLayer.layerName}");

            GameManager.Instance?.SuspendGameplay(GameManager.SuspensionReason.TimeTravel);
            TimeLayerChanging?.Invoke(previousLayer, newLayer);

            yield return new WaitForSeconds(transitionDuration);

            currentLayer = newLayer;
            TimeLayerChanged?.Invoke(newLayer);

            isTransitioning = false;
            Debug.Log($"Transition to {newLayer.layerName} complete");
        }

        private TimeLayer GetLayerByIndex(int layerIndex)
        {
            return timeLayers.FirstOrDefault(l => l.layerIndex == layerIndex);
        }

        public TimeLayer GetForwardLayer(int jumpDistance = 1)
        {
            if (!isInitialized) return null;
            int targetIndex = currentLayer.layerIndex + jumpDistance;
            return GetLayerByIndex(targetIndex);
        }

        public TimeLayer GetBackwardLayer(int jumpDistance = 1)
        {
            if (!isInitialized) return null;
            int targetIndex = currentLayer.layerIndex - jumpDistance;
            return GetLayerByIndex(targetIndex);
        }

        public bool CanGoBackward() => GetBackwardLayer() != null;
        public bool CanGoForward() => GetForwardLayer() != null;

        public List<TimeLayer> GetAllTimeLayers() => timeLayers;
        public EventReference GetTimePortalEvent() => timePortalAudio;

        public void OnPOILayerLoadComplete()
        {
            GameManager.Instance?.ResumeGameplay(GameManager.SuspensionReason.TimeTravel);
            Debug.Log("TimeLayerManager: POI layer loaded, navigation resumed");
        }

        public void ReloadCurrentLayer()
        {
            if (!isInitialized || currentLayer == null)
            {
                Debug.LogError("TimeLayerManager: Cannot reload - not initialized or no current layer");
                return;
            }

            Debug.Log($"TimeLayerManager: Reloading current layer: {currentLayer.layerName}");
            TimeLayerChanged?.Invoke(currentLayer);
        }

        public void CompleteReset()
        {
            Debug.Log("TimeLayerManager: COMPLETE RESET");

            // Clear all layers
            if (timeLayers != null)
            {
                foreach (var layer in timeLayers)
                {
                    layer?.pois?.ForEach(poi => poi?.Cleanup());
                    layer?.pois?.Clear();
                }
                timeLayers.Clear();
            }

            // Reset state
            currentLayer = null;
            isTransitioning = false;
            isInitialized = false;

            Debug.Log("TimeLayerManager: Complete reset finished - ready for new site");
        }

        private void OnDestroy()
        {
            if (gameDataService != null)
            {
                gameDataService.OnDataLoaded -= InitializeFromJSON;
            }

            if (timeLayers != null)
            {
                foreach (var layer in timeLayers)
                {
                    layer?.pois?.ForEach(poi => poi?.Cleanup());
                    layer?.pois?.Clear();
                }
            }

            Debug.Log("TimeLayerManager: Cleanup completed");
        }
    }
}