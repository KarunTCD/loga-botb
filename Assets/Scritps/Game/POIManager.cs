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
        [SerializeField] private List<POI> pois;
        [SerializeField] private MapManager mapManager;
        [SerializeField] private TextMeshProUGUI debugText;

        [Header("Navigation System")]
        [SerializeField] private EventReference sharedCueEvent;
        [SerializeField] private float cueStagingDelay = 2f; // Short delay between cues in cycle
        [SerializeField] private float cyclePauseDelay = 6f;   // Longer pause between cycles
        [SerializeField] private int maxActiveCues = 3;
        [SerializeField] private float proximityRadius = 20f;
        [SerializeField] private float dialogueRadius = 10f;
        [SerializeField] private float maxCueRadius = 500000f;
        [SerializeField] private float discoveryDistance = 20f; // Discovery distance for character to be considered unlocked

        [Header("Target Locking")]
        [SerializeField] private float targetLockTime = 3.0f;
        [SerializeField] private float targetLockAngle = 15.0f;
        [SerializeField] private float targetBreakAngle = 30.0f;
        [SerializeField] private GameObject targetingIndicator;
        [SerializeField] private TextMeshProUGUI targetingText;

        [Header("Frequency Control")]
        [SerializeField] private float minCueInterval = 1.0f;   // Very frequent when very close
        [SerializeField] private float maxCueInterval = 5.0f;   // Slow when far away
        [SerializeField] private float maxTargetingDistance = 200f; // Same as volume max distance

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
            // Create the shared cue instance once
            try
            {
                sharedCueInstance = AudioService.CreateAudioInstance(sharedCueEvent);
            }
            catch (System.Exception e)
            {
                Debug.Log("Error " + e.Message);
            }

            InitializePOIs();

            // Subscribe to location updates
            LocationService.LocationUpdated += OnLocationUpdated;
        }

        private void OnLocationUpdated(float latitude, float longitude)
        {
            UpdateProximity(latitude, longitude);
        }

        private void InitializePOIs()
        {
            foreach (var poi in pois)
            {
                Debug.Log($"Initializing {poi.characterName}");
                poi.Initialize();
                Vector2 poiPosition = mapManager.GetScreenPosition(poi.latitude, poi.longitude);
                poi.marker.anchoredPosition = poiPosition;
                // Distribute to all POIs
                poi.SetSharedCueInstance(sharedCueInstance);
            }
        }

        // NEW: Calculate aggressive distance-based cue interval
        private float CalculateTargetCueInterval(float distance)
        {
            // Normalize distance (0-1 range)
            float normalizedDistance = Mathf.Clamp01(distance / maxTargetingDistance);

            // Use exponential curve for more aggressive frequency changes
            // Same pattern as volume: front-loaded changes where they matter most
            float frequencyFactor = Mathf.Pow(normalizedDistance, 1.5f);

            // Calculate interval (closer = shorter interval = more frequent)
            float interval = Mathf.Lerp(minCueInterval, maxCueInterval, frequencyFactor);

            return interval;
        }

        public void UpdateUnlockedPOIs(List<string> unlockedPOIs)
        {
            foreach (var poi in pois)
            {
                bool isUnlocked = unlockedPOIs.Contains(poi.id);
                poi.SetUnlocked(isUnlocked);
            }
        }

        public void UpdateProximity(float currentLat, float currentLon)
        {
            // Skip if not in player mode
            if (GameManager.Instance != null && GameManager.Instance.CurrentMode != GameManager.GameMode.Player)
                return;

            Dictionary<POI, float> poiDistances = new Dictionary<POI, float>();
            float headingAngle = HeadTrackingService.CurrentHeading;

            foreach (var poi in pois)
            {
                float distance = CalculateDistance(currentLat, currentLon, poi.latitude, poi.longitude);
                poiDistances.Add(poi, distance);

                // Check for discovery
                if (distance <= discoveryDistance && !poi.IsDiscovered)
                {
                    poi.SetDiscovered(true);
                    FirebaseService.SaveDiscoveredPOI(GameManager.Instance.CurrentSessionId, poi.id);
                    Debug.Log($"Discovered POI: {poi.characterName}");
                }

                // Calculate audio position for each POI
                Vector3 audioPosition = CalculateAudioPosition(poi, currentLat, currentLon, headingAngle);

                // NEW: Let each POI handle its own character audio based on distance
                poi.UpdateProximity(distance, audioPosition);
            }

            // Check if any POI is in proximity for wander mode management
            var proximityPOI = poiDistances
                .Where(p => p.Value <= proximityRadius)
                .OrderBy(p => p.Value)
                .Select(p => p.Key)
                .FirstOrDefault();

            if (proximityPOI != null)
            {
                // We're in INTERACT MODE - character audio is handled by POI.UpdateProximity()
                // Clear any active navigation cues
                activeCuePOIs.Clear();

                // Clear targeting if we had one
                if (targetedPOI != null)
                {
                    ClearTargetedPOI();
                }
            }
            else
            {
                // We're in WANDER MODE - run navigation cues
                UpdateNavigationCues(poiDistances, currentLat, currentLon);
            }
        }

        //function that manages navigation cues (WANDER MODE)
        private void UpdateNavigationCues(Dictionary<POI, float> poiDistances, float currentLat, float currentLon)
        {
            // Find eligible POIs as before
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
                        // UPDATED: Calculate aggressive distance-based cue interval
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
                            targetedPOI.PlayNavigationCue(position, distance);

                            // Update debug text with improved frequency info
                            if (debugText != null)
                            {
                                float frequency = 1.0f / targetCueInterval;
                                debugText.text = $"Targeted: {targetedPOI.characterName}\nDistance: {distance:F0}m\nInterval: {targetCueInterval:F2}s ({frequency:F1} Hz)";
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
                        float remainingPause = cyclePauseDelay - cyclePauseTimer;
                        debugText.text = $"Cycle pause: {remainingPause:F1}s remaining";
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
                        poi.PlayNavigationCue(position, distance);

                        // Update debug text
                        if (debugText != null)
                        {
                            debugText.text = $"Playing: {poi.characterName} ({currentCueIndex + 1}/{activeCuePOIs.Count})";
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

        // Add these helper methods for targeting
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

            // Play a sound to indicate successful targeting
            // You could add this to SoundManager
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

            // Clean up all POIs
            foreach (var poi in pois)
            {
                poi.Cleanup();
            }

            // Clean up shared cue instance
            if (sharedCueInstance.isValid())
            {
                AudioService.StopAudio(sharedCueInstance, false);
                AudioService.ReleaseAudio(sharedCueInstance);
            }
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