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
    public enum TargetingMode
    {
        None,
        Potential,
        Locked
    }

    [System.Serializable]
    public struct TargetingState
    {
        public TargetingMode mode;
        public POI targetPOI;
        public float timer;
        public float angleDifference;
    }

    public class POIManager : MonoBehaviour
    {
        [Header("Time Layer Configuration")]
        [SerializeField] private MapManager mapManager;
        [SerializeField] private TextMeshProUGUI debugText;

        [Header("Audio System")]
        [SerializeField] private EventReference sharedCueEvent;
        [SerializeField] private EventReference mainAmbientEvent;

        [Header("Distance Thresholds")]
        [SerializeField] private float proximityRadius = 20f;
        [SerializeField] private float dialogueRadius = 10f;
        [SerializeField] private float maxCueRadius = 500000f;
        [SerializeField] private float discoveryDistance = 20f;

        [Header("Navigation System")]
        [SerializeField] private float cueStagingDelay = 2f;
        [SerializeField] private float cyclePauseDelay = 6f;
        [SerializeField] private int maxActiveCues = 3;

        [Header("Target Locking")]
        [SerializeField] private float targetLockTime = 2.0f;
        [SerializeField] private float targetLockAngle = 15.0f;
        [SerializeField] private float targetBreakAngle = 30.0f;
        [SerializeField] private GameObject targetingIndicator;
        [SerializeField] private TextMeshProUGUI targetingText;
        [SerializeField] private TextMeshProUGUI zoneText;

        [Header("Frequency Control")]
        [SerializeField] private float maxTargetingDistance = 200f;

        // Current layer data
        private TimeLayer currentLayer;
        private List<POI> activePOIs = new List<POI>();

        // Audio instances
        private EventInstance ambientMusicInstance;
        private EventInstance sharedCueInstance;

        // Centralized data cache - calculated once, used everywhere
        private Dictionary<POI, POIUpdateData> poiDataCache = new Dictionary<POI, POIUpdateData>();
        
        // Navigation state
        private List<POI> activeCuePOIs = new List<POI>();
        private float cueTimer = 0f;
        private int currentCueIndex = 0;
        private bool isInCyclePause = false;
        private float cyclePauseTimer = 0f;

        // Centralized targeting state
        private TargetingState targetingState = new TargetingState { mode = TargetingMode.None };
        
        // Sequential cue tracking for targeted POI
        private int sequentialCueIndex = 0;
        
        // Optimization counters
        private int updateFrameCounter = 0;

        // Services
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();
        private IFirebaseService FirebaseService => ServiceLocator.GetService<IFirebaseService>();

        private void Start()
        {
            InitializeAmbientMusic();

            // Subscribe to time layer changes
            TimeLayerManager.Instance.TimeLayerChanging += OnTimeLayerChanging;
            TimeLayerManager.Instance.TimeLayerChanged += OnTimeLayerChanged;

            // Initialize with current layer
            OnTimeLayerChanged(TimeLayerManager.Instance.CurrentLayer);
        }

        private void Update()
        {
            // Skip if transitioning or not in player mode
            if (TimeLayerManager.Instance.IsTransitioning ||
                GameManager.Instance?.CurrentMode != GameManager.GameMode.Player)
                return;

            Vector2 currentLocation = LocationService.GetCurrentLocation();
            if (currentLocation == Vector2.zero) return;

            updateFrameCounter++;

            // EVERY FRAME - Calculate data once for all POIs
            UpdatePOIDataCache(currentLocation.x, currentLocation.y);

            // EVERY FRAME - Update POI proximity (smooth audio transitions)
            UpdatePOIProximity();

            // EVERY FRAME - Navigation and targeting logic (responsive)
            UpdateNavigationAndTargeting(currentLocation.x, currentLocation.y);

            // OCCASIONAL - Discovery logic (1Hz)
            if (updateFrameCounter % 60 == 0)
            {
                UpdateDiscoveryLogic();
            }

            // OCCASIONAL - Debug display (5Hz)
            if (updateFrameCounter % 12 == 0)
            {
                UpdateDebugDisplay(currentLocation);
            }
        }

        /// <summary>
        /// Calculate all POI data once per frame - single source of truth
        /// </summary>
        private void UpdatePOIDataCache(float currentLat, float currentLon)
        {
            float headingAngle = HeadTrackingService.CurrentHeading;
            poiDataCache.Clear();

            foreach (var poi in activePOIs)
            {
                float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
                float bearing = CalculateBearing(currentLat, currentLon, poi.latitude, poi.longitude);
                Vector3 audioPosition = CalculateAudioPosition(poi, currentLat, currentLon, headingAngle);
                float angleDifference = Mathf.Abs(Mathf.DeltaAngle(headingAngle, bearing));

                poiDataCache[poi] = new POIUpdateData
                {
                    distance = distance,
                    bearing = bearing,
                    audioPosition = audioPosition,
                    angleDifference = angleDifference
                };
            }
        }

        /// <summary>
        /// Update POI proximity using cached data with centralized zone calculation
        /// </summary>
        private void UpdatePOIProximity()
        {
            bool foundProximityPOI = false;
            foreach (var poi in activePOIs)
            {
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    // Calculate zone value in POIManager for consistency
                    float zoneValue = CalculateZoneFromDistance(data.distance);
                    poi.UpdateProximity(data, zoneValue);

                    // Only display zone for POIs that are actually in proximity (zone > 0)
                    if (zoneValue > 0 && !foundProximityPOI)
                    {
                        zoneText.text = $"Zone: {zoneValue:F2} ({poi.characterName})";
                        foundProximityPOI = true;
                    }
                }
            }

            // Clear zone text if no POI is in proximity
            if (!foundProximityPOI)
            {
                zoneText.text = "Zone: 0 (No proximity)";
            }
        }

        /// <summary>
        /// Centralized navigation and targeting logic
        /// </summary>
        private void UpdateNavigationAndTargeting(float currentLat, float currentLon)
        {
            if (activePOIs.Count == 0) return;

            // Check for proximity POI (interact mode)
            var proximityPOI = poiDataCache
                .Where(p => p.Value.distance <= proximityRadius)
                .OrderBy(p => p.Value.distance)
                .Select(p => p.Key)
                .FirstOrDefault();

            if (proximityPOI != null)
            {
                // INTERACT MODE - Clear navigation
                ClearAllNavigationState();
                return;
            }

            // WANDER MODE - Handle navigation and targeting
            var eligiblePOIs = GetEligibleNavigationPOIs();

            if (eligiblePOIs.Count == 0)
            {
                ClearAllNavigationState();
                return;
            }

            // Update targeting logic with cached data
            UpdateTargetingLogic(eligiblePOIs);

            // Handle navigation cues based on current targeting state
            if (targetingState.mode == TargetingMode.Locked)
            {
                HandleTargetedNavigation();
            }
            else
            {
                HandleStandardNavigation(eligiblePOIs);
            }
        }

        /// <summary>
        /// Get POIs eligible for navigation cues using cached distances
        /// </summary>
        private List<POI> GetEligibleNavigationPOIs()
        {
            return poiDataCache
                .Where(p => p.Value.distance > proximityRadius && p.Value.distance <= maxCueRadius)
                .OrderBy(p => p.Value.distance)
                .Take(maxActiveCues)
                .Select(p => p.Key)
                .ToList();
        }

        /// <summary>
        /// Centralized targeting logic with improved state management
        /// </summary>
        private void UpdateTargetingLogic(List<POI> eligiblePOIs)
        {
            switch (targetingState.mode)
            {
                case TargetingMode.None:
                    CheckForPotentialTarget(eligiblePOIs);
                    break;

                case TargetingMode.Potential:
                    UpdatePotentialTargeting();
                    break;

                case TargetingMode.Locked:
                    UpdateLockedTargeting(eligiblePOIs);
                    break;
            }
        }

        private void CheckForPotentialTarget(List<POI> eligiblePOIs)
        {
            foreach (var poi in eligiblePOIs)
            {
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data) && 
                    data.angleDifference <= targetLockAngle)
                {
                    SetPotentialTarget(poi, data);
                    return;
                }
            }
        }

        private void UpdatePotentialTargeting()
        {
            if (!poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData data))
            {
                ClearTargeting();
                return;
            }

            if (data.angleDifference <= targetLockAngle)
            {
                targetingState.timer += Time.deltaTime;
                targetingState.angleDifference = data.angleDifference;

                UpdateTargetingUI();

                if (targetingState.timer >= targetLockTime)
                {
                    LockTarget();
                }
            }
            else
            {
                ClearTargeting();
            }
        }

        private void UpdateLockedTargeting(List<POI> eligiblePOIs)
        {
            if (!eligiblePOIs.Contains(targetingState.targetPOI))
            {
                ClearTargeting();
                return;
            }

            if (poiDataCache.TryGetValue(targetingState.targetPOI, out POIUpdateData data))
            {
                if (data.angleDifference > targetBreakAngle)
                {
                    ClearTargeting();
                }
                else
                {
                    targetingState.angleDifference = data.angleDifference;
                }
            }
        }

        /// <summary>
        /// Handle navigation cues for targeted POI - always sequential
        /// </summary>
        private void HandleTargetedNavigation()
        {
            var targetPOI = targetingState.targetPOI;
            if (!poiDataCache.TryGetValue(targetPOI, out POIUpdateData data)) return;

            // Use the same staging delay as standard navigation for consistency
            cueTimer += Time.deltaTime;

            if (cueTimer >= cueStagingDelay)
            {
                cueTimer = 0f;

                // Always use sequential cues for targeted POI - increment index each time
                sequentialCueIndex = (sequentialCueIndex % 4) + 1;

                var config = new NavigationCueConfig
                {
                    cueType = NavigationCueType.Sequential,
                    cueIndex = sequentialCueIndex,
                    maxDistance = maxTargetingDistance,
                    isTargeted = true
                };

                targetPOI.ExecuteNavigationCue(data.audioPosition, config);
            }
        }

        /// <summary>
        /// Handle standard alternating navigation cues
        /// </summary>
        private void HandleStandardNavigation(List<POI> eligiblePOIs)
        {
            activeCuePOIs = eligiblePOIs;

            if (activeCuePOIs.Count == 0) return;

            cueTimer += Time.deltaTime;

            if (isInCyclePause)
            {
                cyclePauseTimer += Time.deltaTime;
                if (cyclePauseTimer >= cyclePauseDelay)
                {
                    isInCyclePause = false;
                    cyclePauseTimer = 0f;
                    currentCueIndex = 0;
                    cueTimer = cueStagingDelay;
                }
                return;
            }

            if (cueTimer >= cueStagingDelay && currentCueIndex < activeCuePOIs.Count)
            {
                cueTimer = 0f;

                var poi = activeCuePOIs[currentCueIndex];
                if (poiDataCache.TryGetValue(poi, out POIUpdateData data))
                {
                    var config = new NavigationCueConfig
                    {
                        cueType = NavigationCueType.DistanceBased,
                        cueIndex = CalculateDistanceBasedCueIndex(data.distance),
                        maxDistance = maxTargetingDistance,
                        isTargeted = false
                    };

                    poi.ExecuteNavigationCue(data.audioPosition, config);
                }

                currentCueIndex++;

                if (currentCueIndex >= activeCuePOIs.Count)
                {
                    isInCyclePause = true;
                    cyclePauseTimer = 0f;
                }
            }
        }

        /// <summary>
        /// Calculate distance-based cue index
        /// </summary>
        private int CalculateDistanceBasedCueIndex(float distance)
        {
            float normalizedDistance = Mathf.Clamp01(distance / maxTargetingDistance);
            if (normalizedDistance <= 0.25f) return 1;      // Close
            else if (normalizedDistance <= 0.5f) return 2;  // Medium  
            else if (normalizedDistance <= 0.75f) return 3; // Far
            else return 4;                                   // Very far
        }

        /// <summary>
        /// Discovery logic using cached distances
        /// </summary>
        private void UpdateDiscoveryLogic()
        {
            foreach (var poi in activePOIs)
            {
                if (poi.IsDiscovered) continue;

                if (poiDataCache.TryGetValue(poi, out POIUpdateData data) && 
                    data.distance <= discoveryDistance)
                {
                    poi.SetDiscovered(true);
                    FirebaseService.SaveDiscoveredPOI(GameManager.Instance.CurrentSessionId, poi.id);
                    Debug.Log($"Discovered POI: {poi.characterName}");
                }
            }
        }

        /// <summary>
        /// Targeting state management methods
        /// </summary>
        private void SetPotentialTarget(POI poi, POIUpdateData data)
        {
            targetingState = new TargetingState
            {
                mode = TargetingMode.Potential,
                targetPOI = poi,
                timer = 0f,
                angleDifference = data.angleDifference
            };

            if (targetingIndicator != null)
                targetingIndicator.SetActive(true);

            Debug.Log($"Started targeting {poi.characterName} - angle: {data.angleDifference:F1}°");
        }

        private void LockTarget()
        {
            targetingState.mode = TargetingMode.Locked;
            targetingState.targetPOI.UpdateTargetingState(true);

            // Reset sequential cue tracking
            sequentialCueIndex = 0;

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = $"Locked onto {targetingState.targetPOI.characterName}";

            Debug.Log($"Successfully locked onto {targetingState.targetPOI.characterName} after {targetingState.timer:F2}s");
        }

        private void ClearTargeting()
        {
            if (targetingState.mode == TargetingMode.Locked && targetingState.targetPOI != null)
            {
                targetingState.targetPOI.UpdateTargetingState(false);
            }

            Debug.Log($"Clearing targeting: {targetingState.targetPOI?.characterName ?? "None"}");

            targetingState = new TargetingState { mode = TargetingMode.None };

            // Reset sequential tracking
            sequentialCueIndex = 0;

            if (targetingIndicator != null)
                targetingIndicator.SetActive(false);

            if (targetingText != null)
                targetingText.text = "";
        }

        private void ClearAllNavigationState()
        {
            activeCuePOIs.Clear();
            ClearTargeting();
            isInCyclePause = false;
            cyclePauseTimer = 0f;
            currentCueIndex = 0;
            cueTimer = 0f;
        }

        private void UpdateTargetingUI()
        {
            if (targetingText != null)
            {
                float progress = (targetingState.timer / targetLockTime) * 100f;
                targetingText.text = $"Targeting {targetingState.targetPOI.characterName}\n" +
                                   $"Progress: {progress:F0}%\n" +
                                   $"Time: {targetingState.timer:F2}s / {targetLockTime:F1}s\n" +
                                   $"Angle: {targetingState.angleDifference:F1}°";
            }
        }

        /// <summary>
        /// Enhanced debug display with targeting information
        /// </summary>
        private void UpdateDebugDisplay(Vector2 location)
        {
            if (debugText == null) return;

            var currentLayer = TimeLayerManager.Instance.CurrentLayer;

            if (targetingState.mode == TargetingMode.Locked)
            {
                var data = poiDataCache[targetingState.targetPOI];
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"Target: {targetingState.targetPOI.characterName}\n" +
                               $"Dist: {data.distance:F0}m | Angle: {data.angleDifference:F1}°\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Provider: {HeadTrackingService.ActiveProviderName}";
            }
            else if (targetingState.mode == TargetingMode.Potential)
            {
                float progress = (targetingState.timer / targetLockTime) * 100f;
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"Targeting: {targetingState.targetPOI.characterName}\n" +
                               $"Progress: {progress:F1}%\n" +
                               $"Timer: {targetingState.timer:F2}s / {targetLockTime:F1}s\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°";
            }
            else
            {
                debugText.text = $"Layer: {currentLayer.layerName}\n" +
                               $"POIs: {activePOIs.Count}\n" +
                               $"Head: {HeadTrackingService.CurrentHeading:F0}°\n" +
                               $"Provider: {HeadTrackingService.ActiveProviderName}\n" +
                               $"Location: {location.x:F6}, {location.y:F6}";
            }
        }

        private float CalculateZoneFromDistance(float distance)
        {
            if (distance > proximityRadius)  // > 20m (outside music zone)
            {
                return 0.0f; // No audio
            }
            else if (distance > dialogueRadius)  // 10m-20m (music zone)
            {
                // Map 20m→10m to Zone 0.0→1.0 (music fades in)
                float t = 1.0f - ((distance - dialogueRadius) / (proximityRadius - dialogueRadius));
                return Mathf.Lerp(0.0f, 1.0f, t);  // Use 0-1 range for music zone
            }
            else  // <= 10m (dialogue zone)
            {
                // Map 10m→0m to Zone 1.0→2.0 (music ducks, narration fades in)
                float t = distance / dialogueRadius;  // Closer to 0m = higher zone value
                return Mathf.Lerp(2.0f, 1.0f, t);    // Use 1-2 range for dialogue zone
            }
        }

        /// <summary>
        /// Time layer management methods
        /// </summary>
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

        private void OnTimeLayerChanging(TimeLayer from, TimeLayer to)
        {
            Debug.Log($"POIManager: Preparing transition from {from.layerName} to {to.layerName}");

            // Clear all state
            ClearAllNavigationState();
            CleanupCurrentLayerPOIs();

            if (debugText != null)
            {
                debugText.text = $"Transitioning: {from.layerName} → {to.layerName}";
            }
        }

        private void OnTimeLayerChanged(TimeLayer newLayer)
        {
            Debug.Log($"POIManager: Loading {newLayer.layerName} layer");

            currentLayer = newLayer;
            LoadLayerPOIs(newLayer);
            SwitchLayerAmbientMusic(newLayer);

            if (debugText != null)
            {
                debugText.text = $"Layer: {newLayer.layerName}\nPOIs: {activePOIs.Count}";
            }
        }

        private void LoadLayerPOIs(TimeLayer layer)
        {
            activePOIs.Clear();

            if (layer.pois != null)
            {
                activePOIs.AddRange(layer.pois);
            }

            InitializePOIs();
            Debug.Log($"Loaded {activePOIs.Count} POIs for {layer.layerName}");
        }

        private void InitializePOIs()
        {
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

            foreach (var poi in activePOIs)
            {
                Debug.Log($"Initializing {poi.characterName} in {currentLayer.layerName}");
                poi.Initialize(proximityRadius, dialogueRadius);
                Vector2 poiPosition = mapManager.GetScreenPosition(poi.latitude, poi.longitude);
                poi.marker.anchoredPosition = poiPosition;
                poi.SetSharedCueInstance(sharedCueInstance);
            }
        }

        private void SwitchLayerAmbientMusic(TimeLayer layer)
        {
            if (ambientMusicInstance.isValid())
            {
                AudioService.SetParameter(ambientMusicInstance, "TimeLayer", layer.layerIndex);
                Debug.Log($"Switched ambient music to layer {layer.layerIndex} ({layer.layerName})");
            }
            else
            {
                Debug.LogError("Ambient music instance is not valid!");
            }
        }

        private void CleanupCurrentLayerPOIs()
        {
            foreach (var poi in activePOIs)
            {
                poi.Cleanup();
            }

            activePOIs.Clear();
            poiDataCache.Clear();

            if (sharedCueInstance.isValid())
            {
                AudioService.StopAudio(sharedCueInstance, false);
                AudioService.ReleaseAudio(sharedCueInstance);
            }
        }

        /// <summary>
        /// Public methods for external access
        /// </summary>
        public void UpdateUnlockedPOIs(List<string> unlockedPOIs)
        {
            foreach (var poi in activePOIs)
            {
                bool isUnlocked = unlockedPOIs.Contains(poi.id);
                poi.SetUnlocked(isUnlocked);
            }
        }

        /// <summary>
        /// Utility calculation methods - centralized for consistency
        /// </summary>
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

        /// <summary>
        /// Debug methods for testing and troubleshooting
        /// </summary>
        [ContextMenu("Debug Targeting Status")]
        public void DebugTargetingStatus()
        {
            Debug.Log($"=== POI Targeting Debug ===");
            Debug.Log($"Targeting Mode: {targetingState.mode}");
            Debug.Log($"Target POI: {targetingState.targetPOI?.characterName ?? "None"}");
            Debug.Log($"Targeting Timer: {targetingState.timer:F2}s");
            Debug.Log($"Angle Difference: {targetingState.angleDifference:F1}°");
            Debug.Log($"Target Lock Time: {targetLockTime}s");
            Debug.Log($"Active POIs: {activePOIs.Count}");
            Debug.Log($"POI Data Cache: {poiDataCache.Count} entries");
        }

        [ContextMenu("Debug POI Distances")]
        public void DebugPOIDistances()
        {
            Debug.Log($"=== POI Distance Debug ===");
            foreach (var entry in poiDataCache)
            {
                var poi = entry.Key;
                var data = entry.Value;
                Debug.Log($"{poi.characterName}: {data.distance:F1}m, Angle: {data.angleDifference:F1}°");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.TimeLayerChanging -= OnTimeLayerChanging;
                TimeLayerManager.Instance.TimeLayerChanged -= OnTimeLayerChanged;
            }

            // Cleanup resources
            CleanupCurrentLayerPOIs();

            if (ambientMusicInstance.isValid())
            {
                AudioService.StopAudio(ambientMusicInstance, true);
                AudioService.ReleaseAudio(ambientMusicInstance);
            }
        }
    }
}