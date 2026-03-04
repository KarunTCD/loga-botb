using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Analytics;
using Firebase.Extensions;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;

namespace LoGa.LudoEngine.Services
{
    public class AnalyticsService : MonoBehaviour, IAnalyticsService
    {
        public bool IsInitialized { get; private set; } = false;

        [Header("Analytics Configuration")]
        [SerializeField] private bool enableDebugLogging = false;

        private bool consentGiven = false;
        private IStorageService storageService;

        private IStorageService StorageService
        {
            get
            {
                if (storageService == null)
                    storageService = ServiceLocator.GetService<IStorageService>();
                return storageService;
            }
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.Log("AnalyticsService: Initializing Firebase Analytics");

                // Add timeout to Firebase check
                try
                {
                    var dependencyTask = Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
                    var timeoutTask = Task.Delay(3000); // 3 second timeout

                    var completedTask = await Task.WhenAny(dependencyTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        Debug.LogWarning("AnalyticsService: Firebase dependency check timed out - likely offline");
                        IsInitialized = false;
                        return false; // Don't block game, just mark as not initialized
                    }

                    var dependencyResult = await dependencyTask;
                    
                    if (dependencyResult != Firebase.DependencyStatus.Available)
                    {
                        Debug.LogWarning($"AnalyticsService: Firebase Analytics dependencies not available: {dependencyResult}");
                        IsInitialized = false;
                        return false; // Don't block game
                    }

                    Debug.Log("AnalyticsService: Firebase Analytics dependencies available");
                }
                catch (Exception firebaseEx)
                {
                    Debug.LogWarning($"AnalyticsService: Firebase check failed (likely offline): {firebaseEx.Message}");
                    IsInitialized = false;
                    return false; // Don't block game
                }

                // Enable for debug mode
                if (enableDebugLogging)
                {
                    FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                    Debug.Log("AnalyticsService: Debug logging enabled");
                }

                // Set consent to true and save it
                SetAnalyticsConsent(true);

                // Wait for Firebase to process the consent change
                await Task.Delay(1000);

                // Set feedback code
                string feedbackCode = FeedbackCodeUtility.GetFeedbackCode();
                SetFeedbackCode(feedbackCode);

                TrackEvent("game_start");

                IsInitialized = true;
                Debug.Log("AnalyticsService: Initialized successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AnalyticsService: Initialization failed (continuing without analytics): {e.Message}");
                IsInitialized = false;
                return false; // Return false but don't crash
            }
        }


        public void TrackEvent(string eventName)
        {
            if (!IsInitialized || !consentGiven) return;

            try
            {
                FirebaseAnalytics.LogEvent(eventName);

                if (enableDebugLogging)
                {
                    Debug.Log($"AnalyticsService: Tracked event '{eventName}'");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"AnalyticsService: Failed to track event '{eventName}' - {e.Message}");
            }
        }

        public void TrackEventWithData(string eventName, Dictionary<string, object> parameters)
        {
            if (!IsInitialized || !consentGiven) return;

            try
            {
                var firebaseParams = new List<Firebase.Analytics.Parameter>();

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        if (param.Value is string stringValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, stringValue));
                        }
                        else if (param.Value is int intValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, intValue));
                        }
                        else if (param.Value is long longValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, longValue));
                        }
                        else if (param.Value is double doubleValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, doubleValue));
                        }
                        else if (param.Value is float floatValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, (double)floatValue));
                        }
                        else if (param.Value is bool boolValue)
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, boolValue ? "true" : "false"));
                        }
                        else
                        {
                            firebaseParams.Add(new Firebase.Analytics.Parameter(param.Key, param.Value?.ToString() ?? "null"));
                        }
                    }
                }

                FirebaseAnalytics.LogEvent(eventName, firebaseParams.ToArray());

                if (enableDebugLogging)
                {
                    Debug.Log($"AnalyticsService: Tracked event '{eventName}' with {firebaseParams.Count} parameters");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"AnalyticsService: Failed to track event '{eventName}' - {e.Message}");
            }
        }

        public void SetUserProperty(string propertyName, string value)
        {
            if (!IsInitialized || !consentGiven) return;

            try
            {
                FirebaseAnalytics.SetUserProperty(propertyName, value);

                if (enableDebugLogging)
                {
                    Debug.Log($"AnalyticsService: Set user property '{propertyName}' = '{value}'");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"AnalyticsService: Failed to set user property '{propertyName}' - {e.Message}");
            }
        }

        public void SetFeedbackCode(string feedbackCode)
        {
            if (!IsInitialized || !consentGiven) return;

            try
            {
                SetUserProperty("feedback_code", feedbackCode);
                TrackEvent($"feedback_code_set_{feedbackCode}");
                Debug.Log($"AnalyticsService: Feedback code set as user property: {feedbackCode}");
            }
            catch (Exception e)
            {
                Debug.LogError($"AnalyticsService: Failed to set feedback code - {e.Message}");
            }
        }

        private bool GetUserConsent()
        {
            if (StorageService != null)
            {
                return StorageService.Load<bool>("AnalyticsConsent");
            }
            else
            {
                Debug.LogWarning("AnalyticsService: StorageService not available - defaulting to no consent");
                return false;
            }
        }

        public void SetAnalyticsConsent(bool consent)
        {
            consentGiven = consent;

            if (StorageService != null)
            {
                StorageService.Save("AnalyticsConsent", consent);
            }
            else
            {
                Debug.LogError("AnalyticsService: StorageService not available - cannot save consent");
            }

            FirebaseAnalytics.SetAnalyticsCollectionEnabled(consent);
            Debug.Log($"AnalyticsService: Analytics consent {(consent ? "granted" : "revoked")}");
        }

        private string GetAreaZone(Vector2 location)
        {
            if (IsNearBattlefield(location))
                return "battlefield";
            else if (IsNearRiver(location))
                return "river_area";
            else if (IsNearVillage(location))
                return "village_area";
            else
                return "exploration_area";
        }

        private bool IsNearBattlefield(Vector2 location)
        {
            return false;
        }

        private bool IsNearRiver(Vector2 location)
        {
            return false;
        }

        private bool IsNearVillage(Vector2 location)
        {
            return false;
        }

        public void Reset()
        {
            Debug.Log("AnalyticsService: Reset called");

            // Reset initialization flag
            IsInitialized = false;

            // Note: Don't change consent status - user's choice persists
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                TrackEvent("app_paused");
            }
            else
            {
                TrackEvent("app_resumed");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                TrackEvent("app_focus_gained");
            }
            else
            {
                TrackEvent("app_focus_lost");
            }
        }

        private void OnDestroy()
        {
            if (IsInitialized)
            {
                TrackEvent("game_end");
            }
        }
    }
}