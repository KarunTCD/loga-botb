using System;
using System.Collections;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Manages spectator mode. Receives player lat/lon/heading from Firebase
    /// and injects them into LocationService and HeadTrackingService so
    /// POIManager runs the full game locally using the player's position.
    /// No audio streaming, no combat, no persistence writes.
    /// GameManager remains the sole entry point — Activate/Deactivate are
    /// only ever called by GameManager.
    /// </summary>
    public class SpectatorManager : MonoBehaviour
    {
        public static SpectatorManager Instance { get; private set; }

        #region Inspector

        [Header("Smoothing")]
        [SerializeField] private float locationLerpSpeed = 2f;
        [SerializeField] private float headingLerpSpeed = 5f;

        #endregion

        #region State

        private string watchedSessionId;
        private bool isActive = false;
        private bool hasFirstFix = false;

        private Vector2 targetLocation;
        private float targetHeading;
        private Vector2 currentInjectedLocation;
        private float currentInjectedHeading;

        #endregion

        #region Service References

        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();

        #endregion

        #region Public Properties

        public bool IsActive => isActive;
        public bool HasFirstFix => hasFirstFix;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("SpectatorManager: Multiple instances detected!");
                Destroy(gameObject);
                return;
            }
        }

        private void OnDestroy()
        {
            Deactivate();

            if (Instance == this)
                Instance = null;
        }

        #endregion

        #region Activation

        /// <summary>
        /// Called by GameManager.EnterSpectatorMode after Firebase connects.
        /// Stops real GPS and head tracking, begins injecting player position.
        /// </summary>
        public void Activate(string sessionId)
        {
            watchedSessionId = sessionId;
            isActive = true;
            hasFirstFix = false;

            // Stop real GPS — spectator uses injected player location
            var locationService = LocationService;
            if (locationService != null && locationService.IsRunning)
            {
                locationService.StopLocationUpdates();
                Debug.Log("SpectatorManager: Real GPS stopped — using injected player location.");
            }

            // Stop real head tracking — spectator uses player's heading
            var headTrackingService = HeadTrackingService;
            if (headTrackingService != null)
            {
                headTrackingService.StopTracking();
                Debug.Log("SpectatorManager: Real head tracking stopped — using injected player heading.");
            }

            StartCoroutine(SmoothInjectionLoop());

            Debug.Log($"SpectatorManager: Activated for session {sessionId}");
        }

        /// <summary>
        /// Called by GameManager.ExitSpectatorMode.
        /// Clears injection and logs restoration of sensors.
        /// Actual sensor restart is handled by HardwareManager on next session.
        /// </summary>
        public void Deactivate()
        {
            if (!isActive) return;

            isActive = false;
            hasFirstFix = false;
            StopAllCoroutines();

            LocationService?.ClearInjection();
            HeadTrackingService?.ClearInjection();

            Debug.Log("SpectatorManager: Deactivated — injection cleared.");
        }

        #endregion

        #region Position Reception

        /// <summary>
        /// Registered as Firebase callback in GameManager.OnSpectatorPositionUpdated.
        /// Called on Unity main thread via GameManager's existing callback path.
        /// </summary>
        public void OnPlayerPositionReceived(float lat, float lon, float heading)
        {
            targetLocation = new Vector2(lat, lon);
            targetHeading = heading;

            if (!hasFirstFix)
            {
                // Snap immediately on first fix — no lerp on cold start
                currentInjectedLocation = targetLocation;
                currentInjectedHeading = targetHeading;
                hasFirstFix = true;

                InjectNow();
                Debug.Log($"SpectatorManager: First fix received — {lat:F6}, {lon:F6}, heading: {heading:F1}°");
            }
        }

        #endregion

        #region Injection Loop

        /// <summary>
        /// Runs every frame while active, lerping toward the latest Firebase values.
        /// Smooth movement prevents POIManager audio from snapping between positions.
        /// </summary>
        private IEnumerator SmoothInjectionLoop()
        {
            while (isActive)
            {
                if (hasFirstFix)
                {
                    currentInjectedLocation = Vector2.Lerp(
                        currentInjectedLocation,
                        targetLocation,
                        Time.deltaTime * locationLerpSpeed);

                    currentInjectedHeading = Mathf.LerpAngle(
                        currentInjectedHeading,
                        targetHeading,
                        Time.deltaTime * headingLerpSpeed);

                    InjectNow();
                }

                yield return null;
            }
        }

        private void InjectNow()
        {
            LocationService?.InjectLocation(currentInjectedLocation);
            HeadTrackingService?.InjectHeading(currentInjectedHeading);
        }

        #endregion
    }
}