using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;

namespace LoGa.LudoEngine.Core
{
    public class GameManager : MonoBehaviour
    {
        // Game modes and states (unchanged)
        public enum GameMode
        {
            Inactive,
            Player,
            Spectator
        }

        public enum GameState
        {
            Running,
            Suspended
        }

        public static GameManager Instance { get; private set; }

        [SerializeField] private MapManager mapManager;
        [SerializeField] private POIManager poiManager;
        [SerializeField] private UIManager uiManager;

        private GameMode currentMode = GameMode.Inactive;
        private string currentSessionId;
        public GameState gameState = GameState.Suspended;

        // Public properties
        public GameMode CurrentMode => currentMode;
        public string CurrentSessionId => currentSessionId;
        public bool IsSpectatorMode => currentMode == GameMode.Spectator;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // Suspend game systems at start
            SuspendGame();
        }

        public void SetGameMode(GameMode mode)
        {
            currentMode = mode;

            // Configure all systems based on mode
            switch (mode)
            {
                case GameMode.Player:
                    StartGameAsPlayer();
                    break;

                case GameMode.Spectator:
                    StartGameAsSpectator();
                    break;

                default:
                    SuspendGame();
                    break;
            }
        }

        private async void SuspendGame()
        {
            currentMode = GameMode.Inactive;

            // Get required services (with initialization)
            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();
            var headTrackingService = await ServiceLocator.GetInitializedService<IHeadTrackingService>();

            // Stop location updates if service is initialized
            if (locationService != null && locationService.IsRunning)
            {
                locationService.StopLocationUpdates();
            }

            // Stop head tracking if service is initialized
            if (headTrackingService != null)
            {
                headTrackingService.StopTracking();
            }

            // Disable game components
            mapManager.enabled = false;
            poiManager.enabled = false;
        }

        private async void StartGameAsPlayer()
        {
            currentMode = GameMode.Player;

            // Get required services (with initialization)
            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();
            var headTrackingService = await ServiceLocator.GetInitializedService<IHeadTrackingService>();

            // Start location updates if service is initialized
            if (locationService != null)
            {
                locationService.StartLocationUpdates();
            }
            else
            {
                Debug.LogError("Cannot start location updates - location service not initialized");
            }

            // Start head tracking if service is initialized
            if (headTrackingService != null)
            {
                headTrackingService.StartTracking();
            }
            else
            {
                Debug.LogError("Cannot start head tracking - service not initialized");
            }

            // Enable game components
            mapManager.enabled = true;
            poiManager.enabled = true;
        }

        private async void StartGameAsSpectator()
        {
            currentMode = GameMode.Spectator;

            // Get location service (with initialization)
            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();

            // Stop location updates if service is running
            if (locationService != null && locationService.IsRunning)
            {
                locationService.StopLocationUpdates();
            }

            // Enable components needed for spectator mode
            mapManager.enabled = true;
            poiManager.enabled = true;
        }

        public async Task<bool> StartPlayerMode()
        {
            try
            {
                // Generate a new session ID
                currentSessionId = System.Guid.NewGuid().ToString();

                // Get and initialize Firebase service
                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService == null)
                {
                    Debug.LogError("Failed to initialize Firebase service");
                    return false;
                }

                // Initialize Firebase session
                bool initialized = await firebaseService.InitializeSession(currentSessionId, "Player");

                if (initialized)
                {
                    SetGameMode(GameMode.Player);
                    return true;
                }
                else
                {
                    throw new System.Exception("Failed to initialize session");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start player mode: {e.Message}");
                SuspendGame();
                return false;
            }
        }

        public async Task<bool> StartSpectatorMode(string sessionId)
        {
            try
            {
                // Get and initialize Firebase service
                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService == null)
                {
                    Debug.LogError("Failed to initialize Firebase service");
                    return false;
                }

                // Connect to Firebase session as spectator
                bool connected = await firebaseService.ConnectToSession(
                    sessionId,
                    OnSpectatorPositionUpdated,
                    OnSpectatorPOIsUpdated);

                if (connected)
                {
                    currentSessionId = sessionId;
                    SetGameMode(GameMode.Spectator);
                    return true;
                }
                else
                {
                    uiManager.ShowConnectionError();
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start spectator mode: {e.Message}");
                SuspendGame();
                return false;
            }
        }

        private void OnSpectatorPositionUpdated(float latitude, float longitude, float heading)
        {
            // Update UI display
            uiManager.UpdateLocationDisplay(latitude, longitude);

            // Update POI proximity
            poiManager.UpdateProximity(latitude, longitude);
        }

        private void OnSpectatorPOIsUpdated(List<string> poiIds)
        {
            // Update discovered POIs
            poiManager.UpdateUnlockedPOIs(poiIds);
        }

        public async void ExitSpectatorMode()
        {
            if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
            {
                // Get Firebase service (with initialization)
                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService != null)
                {
                    // Disconnect from session
                    firebaseService.DisconnectFromSession(currentSessionId);
                    currentSessionId = null;
                }
            }

            // Reset game state
            SetGameMode(GameMode.Inactive);
        }

        private async void OnApplicationQuit()
        {
            try
            {
                // Get Firebase service (without waiting for initialization)
                var firebaseService = ServiceLocator.GetService<IFirebaseService>();

                // Only proceed if service is already initialized
                if (firebaseService != null && firebaseService.IsInitialized)
                {
                    // Clean up Firebase session if in player mode
                    if (currentMode == GameMode.Player && !string.IsNullOrEmpty(currentSessionId))
                    {
                        // We can't truly await here because Unity will kill the app
                        // But we can at least start the operation
                        var task = firebaseService.DeleteSession(currentSessionId);

                        // Optional: Wait a short time to give the operation a chance to complete
                        var delayTask = Task.Delay(500); // Half a second
                        await Task.WhenAny(task, delayTask);
                    }
                    else if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
                    {
                        firebaseService.DisconnectFromSession(currentSessionId);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during application quit: {e.Message}");
            }
        }
    }
}