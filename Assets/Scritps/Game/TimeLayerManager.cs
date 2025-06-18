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

        [Header("Time Layer Configuration")]
        [SerializeField] private List<TimeLayer> timeLayers;
        [SerializeField] private int defaultLayerIndex = 0;

        [Header("Transition Settings")]
        [SerializeField] private float transitionDuration = 3f;
        [SerializeField] private EventReference timePortalAudio; // Single portal+transition event

        // Events
        public event System.Action<TimeLayer, TimeLayer> TimeLayerChanging; // (from, to)
        public event System.Action<TimeLayer> TimeLayerChanged;

        private TimeLayer currentLayer;
        private bool isTransitioning = false;

        // Properties
        public TimeLayer CurrentLayer => currentLayer;
        public bool IsTransitioning => isTransitioning;
        public int CurrentLayerIndex => timeLayers.IndexOf(currentLayer);
        public int TotalLayers => timeLayers.Count;

        // Services
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeDefaultLayer();
        }

        private void InitializeDefaultLayer()
        {
            if (timeLayers.Count == 0)
            {
                Debug.LogError("No time layers configured!");
                return;
            }

            int defaultIndex = Mathf.Clamp(defaultLayerIndex, 0, timeLayers.Count - 1);
            currentLayer = timeLayers[defaultIndex];

            Debug.Log($"Starting in layer: {currentLayer.layerName} (Index: {defaultIndex})");
        }

        // Public access methods
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

            // Notify systems that transition is starting
            TimeLayerChanging?.Invoke(previousLayer, newLayer);

            // Portal audio is now handled by the POI that triggered the transition
            // No separate transition audio needed here

            // Wait for transition duration
            yield return new WaitForSeconds(transitionDuration);

            // Update current layer
            currentLayer = newLayer;

            // Notify systems that transition is complete
            TimeLayerChanged?.Invoke(newLayer);

            isTransitioning = false;
            Debug.Log($"Transition to {newLayer.layerName} complete");
        }

        // Portal navigation helpers
        public TimeLayer GetBackwardLayer()
        {
            int currentIndex = CurrentLayerIndex;
            if (currentIndex > 0)
            {
                return timeLayers[currentIndex - 1];
            }
            return null;
        }

        public TimeLayer GetForwardLayer()
        {
            int currentIndex = CurrentLayerIndex;
            if (currentIndex < timeLayers.Count - 1)
            {
                return timeLayers[currentIndex + 1];
            }
            return null;
        }

        public bool CanGoBackward() => GetBackwardLayer() != null;
        public bool CanGoForward() => GetForwardLayer() != null;
    }
}