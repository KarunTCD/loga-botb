using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using FMOD.Studio;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
    public class POIManager : MonoBehaviour
    {
        [Header("Time Layer Configuration")]
        [SerializeField] private MapManager mapManager;
        [SerializeField] private TextMeshProUGUI debugText;

        [Header("Audio System")]
        [SerializeField] private EventReference sharedCueEvent;
        [SerializeField] private EventReference mainAmbientEvent; // Single ambient event with layer parameter

        [Header("Navigation System")]
        [SerializeField] private float cueStagingDelay = 2f;
        [SerializeField] private float cyclePauseDelay = 6f;
        [SerializeField] private int maxActiveCues = 3;
        [SerializeField] private float proximityRadius = 20f;
        [SerializeField] private float dialogueRadius = 10f;
        [SerializeField] private float maxCueRadius = 500000f;
        [SerializeField] private float discoveryDistance = 20f;

        [Header("Target Locking")]
        [SerializeField] private float targetLockTime = 3.0f;
        [SerializeField] private float targetLockAngle = 15.0f;
        [SerializeField] private float targetBreakAngle = 30.0f;
        [SerializeField] private GameObject targetingIndicator;
        [SerializeField] private TextMeshProUGUI targetingText;

        [Header("Frequency Control")]
        [SerializeField] private float minCueInterval = 1.0f;
        [SerializeField] private float maxCueInterval = 5.0f;
        [SerializeField] private float maxTargetingDistance = 200f;

        // Current layer data
        private TimeLayer currentLayer;
        private List<POI> activePOIs = new List<POI>();

        // Single ambient music instance for all layers
        private EventInstance ambientMusicInstance;

        // Navigation cue system
        private EventInstance sharedCueInstance;
        private List<POI> activeCuePOIs = new List<POI>();
        private float cueTimer = 0f;
        private int currentCueIndex = 0;
        private bool isInCyclePause = false;
        private float cyclePauseTimer = 0f;

        // Target tracking fields
        private POI potentialTargetPOI = null;
        private POI targetedPOI = null;
        private float targetingTimer = 0f;

        // Services
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();
        private IFirebaseService FirebaseService => ServiceLocator.GetService<IFirebaseService>();

        private void Start()
        {
            // Initialize the single ambient music system
            InitializeAmbientMusic();

            // Subscribe to time layer changes
            TimeLayerManager.Instance.TimeLayerChanging += OnTimeLayerChanging;
            TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;

            // Subscribe to location updates
            LocationService.LocationUpdated += OnLocationUpdated;

            // Initialize with current layer
            OnTimeLayerChanged(TimeLayerManager.Instance.CurrentLayer);
        }

        private void InitializeAmbientMusic()
        {
            if (!mainAmbientEvent.IsNull)
            {
                ambientMusicInstance = AudioService.CreateAudioInstance(mainAmbientEvent);
                AudioService.PlayAudio(ambientMusicInstance, Vector3.zero);
                Debug.Log("Started main ambient music system");
            }
            else
            {
                Debug.LogError("Main ambient event not assigned!");
            }
        }

        /// <summary>
        /// Called BEFORE time layer transition starts - handles cleanup
        /// </summary>
        private void OnTimeLayerChanging(TimeLayer from, TimeLayer to)
        {
            Debug.Log($"POIManager: Preparing transition from {from.layerName} to {to.layerName}");

            // Clear targeting state
            ClearTargetedPOI();
            ClearPotentialTargetPOI();

            // Stop navigation cues
            activeCuePOIs.Clear();
            isInCyclePause = false;
            cyclePauseTimer = 0f;
            currentCueIndex = 0;
            cueTimer = 0f;

            // Clean up current POIs (but NOT ambient music)
            CleanupCurrentLayerPOIs();

            // Update debug display
            if (debugText != null)
            {
                debugText.text = $"Transitioning: {from.layerName} → {to.layerName}";
            }
        }

        /// <summary>
        /// Called AFTER time layer transition completes - handles loading new layer
        /// </summary>
        private void OnTimeLayerChanged(TimeLayer newLayer)
        {
            Debug.Log($"POIManager: Loading {newLayer.layerName} layer");

            currentLayer = newLayer;

            // Load POIs for the new layer
            LoadLayerPOIs(newLayer);

            // Switch ambient music via FMOD parameter
            SwitchLayerAmbientMusic(newLayer);

            // Update debug display
            if (debugText != null)
            {
                debugText.text = $"Layer: {newLayer.layerName}\nPOIs: {activePOIs.Count}";
            }
        }

        private void LoadLayerPOIs(TimeLayer layer)
        {
            activePOIs.Clear();

            // Load all POIs for this layer
            if (layer.pois != null)
            {
                activePOIs.AddRange(layer.pois);
            }

            // Initialize all POIs
            InitializePOIs();

            Debug.Log($"Loaded {activePOIs.Count} POIs for {layer.layerName}");
        }

        private void InitializePOIs()
        {
            // Create the shared cue instance for this layer
            try
            {
                if (sharedCueInstance.isValid())
                {
                    AudioService.ReleaseAudio(sharedCueInstance);
                }
                sharedCueInstance = AudioService.CreateAudioInstance(sharedCueEvent);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error creating shared cue instance: " + e.Message);
            }

            // Initialize each POI
            foreach (var poi in activePOIs)
            {
                Debug.Log($"Initializing {poi.characterName} in {currentLayer.layerName}");
                poi.Initialize();
                Vector2 poiPosition = mapManager.GetScreenPosition(poi.latitude, poi.longitude);
                poi.marker.anchoredPosition = poiPosition;
                poi.SetSharedCueInstance(sharedCueInstance);
            }
        }

        /// <summary>
        /// Switch ambient music using FMOD parameter instead of separate events
        /// </summary>
        private void SwitchLayerAmbientMusic(TimeLayer layer)
        {
            if (ambientMusicInstance.isValid())
            {
                // Set layer parameter - FMOD handles the switching internally
                AudioService.SetParameter(ambientMusicInstance, "TimeLayer", layer.layerIndex);
                Debug.Log($"Switched ambient music to layer {layer.layerIndex} ({layer.layerName})");
            }
            else
            {
                Debug.LogError("Ambient music instance is not valid!");
            }
        }

        /// <summary>
        /// Clean up POIs only, not ambient music
        /// </summary>
        private void CleanupCurrentLayerPOIs()
        {
            // Clean up POIs
            foreach (var poi in activePOIs)
            {
                poi.Cleanup();
            }

            activePOIs.Clear();
            activeCuePOIs.Clear();

            // Clean up shared cue instance
            if (sharedCueInstance.isValid())
            {
                AudioService.StopAudio(sharedCueInstance, false);
                AudioService.ReleaseAudio(sharedCueInstance);
            }

            // Note: We do NOT stop ambientMusicInstance here - it continues playing
        }

        private void OnLocationUpdated(float latitude, float longitude)
        {
            UpdateProximity(latitude, longitude);
        }

        private float CalculateTargetCueInterval(float distance)
        {
            float normalizedDistance = Mathf.Clamp01(distance / maxTargetingDistance);
            float frequencyFactor = Mathf.Pow(normalizedDistance, 1.5f);
            float interval = Mathf.Lerp(minCueInterval, maxCueInterval, frequencyFactor);
            return interval;
        }

        public void UpdateUnlockedPOIs(List<string> unlockedPOIs)
        {
            foreach (var poi in activePOIs)
            {
                bool isUnlocked = unlockedPOIs.Contains(poi.id);
                poi.SetUnlocked(isUnlocked);
            }
        }

        public void UpdateProximity(float currentLat, float currentLon)
        {
            // Skip if not in player mode or transitioning
            if (GameManager.Instance != null && GameManager.Instance.CurrentMode != GameManager.GameMode.Player)
                return;

            if (TimeLayerManager.Instance.IsTransitioning)
                return;

            // 🔧 FIX: Create a copy of the POI list for safe iteration
            var poisToUpdate = new List<POI>(activePOIs);

            Dictionary<POI, float> poiDistances = new Dictionary<POI, float>();
            float headingAngle = HeadTrackingService.CurrentHeading;

            // Now iterate over the copy, not the original list
            foreach (var poi in poisToUpdate)
            {
                // Double-check POI is still in active list (in case it was removed during transition)
                if (!activePOIs.Contains(poi))
                {
                    continue; // Skip POIs that are no longer active
                }

                float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
                poiDistances.Add(poi, distance);

                // Check for discovery
                if (distance <= discoveryDistance && !poi.IsDiscovered)
                {
                    poi.SetDiscovered(true);
                    FirebaseService.SaveDiscoveredPOI(GameManager.Instance.CurrentSessionId, poi.id);
                    Debug.Log($"Discovered POI: {poi.characterName}");
                }

                // Calculate audio position
                Vector3 audioPosition = CalculateAudioPosition(poi, currentLat, currentLon, headingAngle);

                // Update POI proximity - this might trigger portal activation
                poi.UpdateProximity(distance, audioPosition);

                // If a transition started during this update, exit early
                if (TimeLayerManager.Instance.IsTransitioning)
                {
                    Debug.Log("Time transition started during proximity update - exiting early");
                    return;
                }
            }

            // Only continue with navigation logic if we have valid active POIs
            if (activePOIs.Count == 0)
            {
                Debug.Log("No active POIs after proximity update");
                return;
            }

            // Check for interact mode
            var proximityPOI = poiDistances
                .Where(p => p.Value <= proximityRadius)
                .OrderBy(p => p.Value)
                .Select(p => p.Key)
                .FirstOrDefault();

            if (proximityPOI != null)
            {
                // INTERACT MODE
                activeCuePOIs.Clear();
                if (targetedPOI != null)
                {
                    ClearTargetedPOI();
                }
            }
            else
            {
                // WANDER MODE
                UpdateNavigationCues(poiDistances, currentLat, currentLon);
            }
        }

        // Function that manages navigation cues (WANDER MODE)
        private void UpdateNavigationCues(Dictionary<POI, float> poiDistances, float currentLat, float currentLon)
        {
            // Find eligible POIs from current time layer
            var eligiblePOIs = poiDistances
                .Where(p => p.Value > proximityRadius && p.Value <= maxCueRadius)
                .OrderBy(p => p.Value)
                .Take(maxActiveCues)
                .Select(p => p.Key)
                .ToList();

            // STEP 1: HANDLE ALREADY TARGETED POI
            if (targetedPOI != null)
            {
                // Check if target is still valid
                if (!eligiblePOIs.Contains(targetedPOI))
                {
                    ClearTargetedPOI(); // No longer valid
                }
                else
                {
                    // Check if player turned away
                    float bearing = CalculateBearing(currentLat, currentLon, targetedPOI.latitude, targetedPOI.longitude);
                    float angleDifference = Mathf.Abs(Mathf.DeltaAngle(HeadTrackingService.CurrentHeading, bearing));

                    if (angleDifference > targetBreakAngle)
                    {
                        ClearTargetedPOI(); // Player turned away
                    }
                    else
                    {
                        // Still targeting this POI
                        // Calculate aggressive distance-based cue interval
                        float distance = poiDistances[targetedPOI];
                        float targetCueInterval = CalculateTargetCueInterval(distance);

                        // Update timer
                        cueTimer += Time.deltaTime;

                        // Check if it's time to play the cue
                        if (cueTimer >= targetCueInterval)
                        {
                            cueTimer = 0f;

                            // Play targeted navigation cue
                            Vector3 position = CalculateAudioPosition(targetedPOI, currentLat, currentLon, HeadTrackingService.CurrentHeading);
                            targetedPOI.PlayNavigationCue(position, distance, maxTargetingDistance);

                            // Update debug text with improved frequency info
                            if (debugText != null)
                            {
                                float frequency = 1.0f / targetCueInterval;
                                var currentLayer = TimeLayerManager.Instance.CurrentLayer;
                                debugText.text = $"Layer: {currentLayer.layerName}\nTargeted: {targetedPOI.characterName}\nDistance: {distance:F0}m\nInterval: {targetCueInterval:F2}s ({frequency:F1} Hz)";
                            }
                        }

                        return; // Skip standard cues
                    }
                }
            }

            // No active target, check for potential target
            if (potentialTargetPOI == null && eligiblePOIs.Count > 0)
            {
                // Check if player is facing any eligible POI
                foreach (var poi in eligiblePOIs)
                {
                    float bearing = CalculateBearing(currentLat, currentLon, poi.latitude, poi.longitude);
                    float angleDifference = Mathf.Abs(Mathf.DeltaAngle(HeadTrackingService.CurrentHeading, bearing));

                    if (angleDifference <= targetLockAngle)
                    {
                        // Player is facing this POI
                        potentialTargetPOI = poi;
                        targetingTimer = 0f;

                        // Show targeting feedback
                        if (targetingIndicator != null)
                        {
                            targetingIndicator.SetActive(true);
                        }

                        break;
                    }
                }
            }
            else if (potentialTargetPOI != null)
            {
                // Check if still facing potential target
                float bearing = CalculateBearing(currentLat, currentLon, potentialTargetPOI.latitude, potentialTargetPOI.longitude);
                float angleDifference = Mathf.Abs(Mathf.DeltaAngle(HeadTrackingService.CurrentHeading, bearing));

                if (angleDifference <= targetLockAngle)
                {
                    // Still facing target, increment timer
                    targetingTimer += Time.deltaTime;

                    // Update targeting progress
                    if (targetingText != null)
                    {
                        float progress = (targetingTimer / targetLockTime) * 100f;
                        targetingText.text = $"Targeting {potentialTargetPOI.characterName}: {progress:F0}%";
                    }

                    // Check if target is locked
                    if (targetingTimer >= targetLockTime)
                    {
                        // Target locked!
                        SetTargetedPOI(potentialTargetPOI);
                        return;
                    }
                }
                else
                {
                    // No longer facing potential target
                    ClearPotentialTargetPOI();
                }
            }

            // STANDARD ALTERNATING CUES WITH CYCLE (if no target)
            activeCuePOIs = eligiblePOIs;

            if (activeCuePOIs.Count > 0)
            {
                cueTimer += Time.deltaTime;

                // Check if we're in a cycle pause
                if (isInCyclePause)
                {
                    cyclePauseTimer += Time.deltaTime;

                    if (cyclePauseTimer >= cyclePauseDelay)
                    {
                        // End cycle pause, reset for new cycle
                        isInCyclePause = false;
                        cyclePauseTimer = 0f;
                        currentCueIndex = 0; // Start new cycle from first POI
                        cueTimer = cueStagingDelay; // Trigger immediate play
                    }

                    // Update debug text during pause
                    if (debugText != null)
                    {
                        var currentLayer = TimeLayerManager.Instance.CurrentLayer;
                        float remainingPause = cyclePauseDelay - cyclePauseTimer;
                        debugText.text = $"Layer: {currentLayer.layerName}\nCycle pause: {remainingPause:F1}s remaining";
                    }

                    return; // Don't play cues during pause
                }

                // Normal cue playing logic
                if (cueTimer >= cueStagingDelay)
                {
                    cueTimer = 0f;

                    if (currentCueIndex < activeCuePOIs.Count)
                    {
                        var poi = activeCuePOIs[currentCueIndex];
                        Vector3 position = CalculateAudioPosition(poi, currentLat, currentLon, HeadTrackingService.CurrentHeading);
                        float distance = poiDistances[poi];

                        // Play cue
                        poi.PlayNavigationCue(position, distance, maxTargetingDistance);

                        // Update debug text
                        if (debugText != null)
                        {
                            var currentLayer = TimeLayerManager.Instance.CurrentLayer;
                            debugText.text = $"Layer: {currentLayer.layerName}\nPlaying: {poi.characterName} ({currentCueIndex + 1}/{activeCuePOIs.Count})";
                        }

                        // Move to next POI
                        currentCueIndex++;

                        // Check if we've completed a full cycle
                        if (currentCueIndex >= activeCuePOIs.Count)
                        {
                            // Start cycle pause
                            isInCyclePause = true;
                            cyclePauseTimer = 0f;

                            Debug.Log($"Completed cycle of {activeCuePOIs.Count} cues, starting {cyclePauseDelay}s pause");
                        }
                    }
                }
            }
        }

        // Helper methods for targeting
        private void SetTargetedPOI(POI poi)
        {
            targetedPOI = poi;
            potentialTargetPOI = null;
            targetingTimer = 0f;

            // Configure POI as target
            Vector3 position = CalculateAudioPosition(
                poi, mapManager.CurrentLat, mapManager.CurrentLon, HeadTrackingService.CurrentHeading);
            poi.SetAsTarget(position);

            // Clear UI indicators
            if (targetingIndicator != null)
            {
                targetingIndicator.SetActive(false);
            }

            if (targetingText != null)
            {
                targetingText.text = $"Locked onto {poi.characterName}";
            }
        }

        private void ClearTargetedPOI()
        {
            if (targetedPOI != null)
            {
                targetedPOI.ClearAsTarget();
                targetedPOI = null;

                if (targetingText != null)
                {
                    targetingText.text = "Target lost";
                }
            }
        }

        private void ClearPotentialTargetPOI()
        {
            potentialTargetPOI = null;
            targetingTimer = 0f;

            if (targetingIndicator != null)
            {
                targetingIndicator.SetActive(false);
            }

            if (targetingText != null)
            {
                targetingText.text = "";
            }
        }

        private Vector3 CalculateAudioPosition(POI poi, float currentLat, float currentLon, float headingAngle)
        {
            float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
            float bearing = CalculateBearing(currentLat, currentLon, poi.latitude, poi.longitude);
            float relativeAngle = bearing - headingAngle;
            float angleRad = relativeAngle * Mathf.Deg2Rad;

            return new Vector3(
                distance * Mathf.Sin(angleRad),
                0,
                distance * Mathf.Cos(angleRad)
            );
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            LocationService.LocationUpdated -= OnLocationUpdated;
        }

        // Distance calculation function
        private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
        {
            float earthRadius = 6371e3f;
            float lat1Rad = lat1 * Mathf.Deg2Rad;
            float lat2Rad = lat2 * Mathf.Deg2Rad;
            float latDiff = (lat2 - lat1) * Mathf.Deg2Rad;
            float lonDiff = (lon2 - lon1) * Mathf.Deg2Rad;
            float a = Mathf.Sin(latDiff / 2) * Mathf.Sin(latDiff / 2) +
                     Mathf.Cos(lat1Rad) * Mathf.Cos(lat2Rad) *
                     Mathf.Sin(lonDiff / 2) * Mathf.Sin(lonDiff / 2);
            float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
            return earthRadius * c;
        }

        // Bearing calculation function
        private float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            var dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            var lat1Rad = lat1 * Mathf.Deg2Rad;
            var lat2Rad = lat2 * Mathf.Deg2Rad;
            var y = Mathf.Sin(dLon) * Mathf.Cos(lat2Rad);
            var x = Mathf.Cos(lat1Rad) * Mathf.Sin(lat2Rad) -
                    Mathf.Sin(lat1Rad) * Mathf.Cos(lat2Rad) * Mathf.Cos(dLon);
            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
    }
}