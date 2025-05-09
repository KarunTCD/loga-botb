using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    private bool isGameRunning;
    private GameMode currentMode = GameMode.Inactive;
    private string currentSessionId;
    public GameState gameState = GameState.Suspended;

    // Services
    private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
    private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();
    private IFirebaseService FirebaseService => ServiceLocator.GetService<IFirebaseService>();
    private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

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

    private void SuspendGame()
    {
        isGameRunning = false;
        currentMode = GameMode.Inactive;

        // Stop location updates
        if (LocationService.IsRunning)
        {
            LocationService.StopLocationUpdates();
        }

        // Stop head tracking
        if (HeadTrackingService.IsInitialized)
        {
            HeadTrackingService.StopTracking();
        }

        // Disable game components
        mapManager.enabled = false;
        poiManager.enabled = false;
    }

    private void StartGameAsPlayer()
    {
        isGameRunning = true;
        currentMode = GameMode.Player;

        // Start location updates
        if (LocationService.IsInitialized)
        {
            LocationService.StartLocationUpdates();
        }

        // Start head tracking
        if (HeadTrackingService.IsInitialized)
        {
            HeadTrackingService.StartTracking();
        }

        // Enable game components
        mapManager.enabled = true;
        poiManager.enabled = true;
    }

    private void StartGameAsSpectator()
    {
        isGameRunning = true;
        currentMode = GameMode.Spectator;

        // No need for location or head tracking
        if (LocationService.IsRunning)
        {
            LocationService.StopLocationUpdates();
        }

        // Enable components needed for spectator mode
        mapManager.enabled = true;
        poiManager.enabled = true;
    }

    public async Task<bool> StartPlayerMode()
    {
        try
        {
            currentSessionId = System.Guid.NewGuid().ToString();

            // Initialize Firebase session
            bool initialized = await FirebaseService.InitializeSession(currentSessionId, "Player");

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
            // Connect to Firebase session as spectator
            bool connected = await FirebaseService.ConnectToSession(
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

    public void ExitSpectatorMode()
    {
        if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
        {
            // Disconnect from session
            FirebaseService.DisconnectFromSession(currentSessionId);
            currentSessionId = null;
        }

        // Reset game state
        SetGameMode(GameMode.Inactive);
    }

    private async void OnApplicationQuit()
    {
        try
        {
            // Clean up Firebase session if in player mode
            if (currentMode == GameMode.Player && !string.IsNullOrEmpty(currentSessionId))
            {
                // We can't truly await here because Unity will kill the app
                // But we can at least start the operation
                var task = FirebaseService.DeleteSession(currentSessionId);

                // Optional: Wait a short time to give the operation a chance to complete
                var delayTask = Task.Delay(500); // Half a second
                await Task.WhenAny(task, delayTask);
            }
            else if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
            {
                FirebaseService.DisconnectFromSession(currentSessionId);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during application quit: {e.Message}");
        }
    }
}