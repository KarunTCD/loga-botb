using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;

namespace LoGa.LudoEngine.Services
{
    public class FirebaseService : MonoBehaviour, IFirebaseService
    {
        public bool IsInitialized { get; private set; }
        private DatabaseReference dbReference;
        private Dictionary<string, EventHandler<ValueChangedEventArgs>> sessionListeners =
        new Dictionary<string, EventHandler<ValueChangedEventArgs>>();

        public async Task<bool> InitializeAsync()
        {
            // Check if already initialized
            if (IsInitialized)
            {
                return true;
            }

            try
            {
                Debug.Log("Starting Firebase initialization...");

                // First ensure all Firebase dependencies are available
                var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                Debug.Log($"Dependency status: {dependencyStatus}");

                if (dependencyStatus == DependencyStatus.Available)
                {
                    // Initialize Firebase app if needed
                    FirebaseApp app = FirebaseApp.DefaultInstance;
                    if (app == null)
                    {
                        Debug.Log("Creating new Firebase instance");
                        FirebaseApp.Create();
                    }

                    try
                    {
                        Debug.Log("Getting database instance...");
                        // Get database URL from config
                        var dbUrl = "https://battleofboyne-default-rtdb.europe-west1.firebasedatabase.app";
                        var database = FirebaseDatabase.GetInstance(FirebaseApp.DefaultInstance, dbUrl);
                        dbReference = database.RootReference;
                        IsInitialized = true;
                        Debug.Log("Firebase Database initialized successfully");
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Database initialization error: {e}");
                        return false;
                    }
                }
                else
                {
                    Debug.LogError($"Could not resolve dependencies: {dependencyStatus}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Firebase initialization failed: {e}");
                return false;
            }
        }

        public async Task<bool> InitializeSession(string sessionId, string playerName)
        {
            // Check and initialize if needed - rename to InitializeAsync to match the pattern
            if (!IsInitialized)
            {
                bool initialized = await InitializeAsync();
                if (!initialized) return false;
            }

            try
            {
                Dictionary<string, object> sessionData = new Dictionary<string, object>
            {
                { "sessionId", sessionId },
                { "playerName", playerName },
                { "timestamp", ServerValue.Timestamp },
                { "unlockedPOIs", new List<string>() }
            };

                await dbReference.Child("sessions").Child(sessionId).SetValueAsync(sessionData);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize session: {e.Message}");
                return false;
            }
        }

        public async Task<bool> ConnectToSession(string sessionId, Action<float, float, float> onPositionUpdated, Action<List<string>> onPOIsUpdated)
        {
            // Check and initialize if needed - rename to InitializeAsync to match the pattern
            if (!IsInitialized)
            {
                bool initialized = await InitializeAsync();
                if (!initialized) return false;
            }

            try
            {
                var snapshot = await dbReference.Child("sessions").Child(sessionId).GetValueAsync();

                if (snapshot.Exists)
                {
                    // Create listener with correct EventHandler signature
                    EventHandler<ValueChangedEventArgs> listener = (sender, args) =>
                    {
                        HandleSessionUpdate(args, onPositionUpdated, onPOIsUpdated);
                    };

                    // Store listener for later removal
                    sessionListeners[sessionId] = listener;

                    // Start listening for updates
                    dbReference.Child("sessions").Child(sessionId).ValueChanged += listener;

                    return true;
                }

                Debug.LogError("Session not found");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to connect: {e.Message}");
                return false;
            }
        }

        private void HandleSessionUpdate(ValueChangedEventArgs args, Action<float, float, float> onPositionUpdated, Action<List<string>> onPOIsUpdated)
        {
            if (args.DatabaseError != null)
            {
                Debug.LogError($"Database error: {args.DatabaseError.Message}");
                return;
            }

            Dictionary<string, object> data = args.Snapshot.Value as Dictionary<string, object>;
            if (data != null)
            {
                // Extract position data
                if (data.ContainsKey("latitude") && data.ContainsKey("longitude") && data.ContainsKey("heading"))
                {
                    float lat = float.Parse(data["latitude"].ToString());
                    float lon = float.Parse(data["longitude"].ToString());
                    float heading = float.Parse(data["heading"].ToString());

                    onPositionUpdated?.Invoke(lat, lon, heading);
                }

                // Extract POI data
                if (data.ContainsKey("unlockedPOIs"))
                {
                    List<string> unlockedPOIs = new List<string>();

                    // Handle different possible formats
                    if (data["unlockedPOIs"] is List<object> poiList)
                    {
                        foreach (var poi in poiList)
                        {
                            unlockedPOIs.Add(poi.ToString());
                        }
                    }
                    else if (data["unlockedPOIs"] is Dictionary<string, object> poiDict)
                    {
                        foreach (var kvp in poiDict)
                        {
                            unlockedPOIs.Add(kvp.Value.ToString());
                        }
                    }

                    onPOIsUpdated?.Invoke(unlockedPOIs);
                }
            }
        }

        public void UpdatePlayerData(string sessionId, float latitude, float longitude, float heading)
        {
            if (!IsInitialized) return;

            Dictionary<string, object> locationData = new Dictionary<string, object>
        {
            { "latitude", latitude },
            { "longitude", longitude },
            { "heading", heading },
            { "timestamp", ServerValue.Timestamp }
        };

            dbReference.Child("sessions").Child(sessionId).UpdateChildrenAsync(locationData);
        }

        public void SaveDiscoveredPOI(string sessionId, string poiId)
        {
            if (!IsInitialized) return;

            // Get current list first
            dbReference.Child("sessions").Child(sessionId).Child("unlockedPOIs").GetValueAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("Error getting POI list: " + task.Exception);
                    return;
                }

                List<string> unlockedPOIs = new List<string>();
                var snapshot = task.Result;

                if (snapshot.Exists)
                {
                    // Parse existing POIs
                    if (snapshot.Value is List<object> poiList)
                    {
                        foreach (var poi in poiList)
                        {
                            unlockedPOIs.Add(poi.ToString());
                        }
                    }
                    else if (snapshot.Value is Dictionary<string, object> poiDict)
                    {
                        foreach (var kvp in poiDict)
                        {
                            unlockedPOIs.Add(kvp.Value.ToString());
                        }
                    }
                }

                // Add new POI if not already present
                if (!unlockedPOIs.Contains(poiId))
                {
                    unlockedPOIs.Add(poiId);
                    dbReference.Child("sessions").Child(sessionId).Child("unlockedPOIs").SetValueAsync(unlockedPOIs);
                }
            });
        }

        public void DisconnectFromSession(string sessionId)
        {
            if (!IsInitialized) return;

            if (sessionListeners.TryGetValue(sessionId, out var listener))
            {
                dbReference.Child("sessions").Child(sessionId).ValueChanged -= listener;
                sessionListeners.Remove(sessionId);
            }
        }

        public async Task DeleteSession(string sessionId)
        {
            if (!IsInitialized) return;

            try
            {
                // First disconnect
                DisconnectFromSession(sessionId);

                // Then delete
                await dbReference.Child("sessions").Child(sessionId).RemoveValueAsync();
                Debug.Log($"Session {sessionId} deleted from Firebase");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete session: {e.Message}");
            }
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                // Clean up all listeners
                foreach (var kvp in sessionListeners)
                {
                    string sessionId = kvp.Key;
                    var listener = kvp.Value;

                    dbReference.Child("sessions").Child(sessionId).ValueChanged -= listener;
                }

                sessionListeners.Clear();

                ServiceLocator.UnregisterService<IFirebaseService>();
            }
        }
    }
}