using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LoGa.LudoEngine.Services;
using TMPro;

namespace LoGa.LudoEngine.Core
{
    public class ServiceInitializer : MonoBehaviour
    {
        [SerializeField] private Slider progressBar;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("Initialization Options")]
        [SerializeField] private bool autoStartInitialization = true;
        [SerializeField] private bool loadGameSceneWhenDone = true;
        [SerializeField] private int maxRetryAttempts = 3;
        [SerializeField] private float retryDelay = 1.0f;

        [Header("UI Elements")]
        [SerializeField] private GameObject retryPanel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button continueAnywayButton;

        private bool isInitializing = false;
        private bool failedCriticalService = false;

        private void Start()
        {
            if (autoStartInitialization)
            {
                StartInitialization();
            }

            // Setup button callbacks
            if (retryButton != null)
            {
                retryButton.onClick.AddListener(StartInitialization);
            }

            if (continueAnywayButton != null)
            {
                continueAnywayButton.onClick.AddListener(ProceedWithoutServices);
            }

            // Hide retry panel initially
            if (retryPanel != null)
            {
                retryPanel.SetActive(false);
            }
        }

        public void StartInitialization()
        {
            if (isInitializing)
                return;

            isInitializing = true;
            failedCriticalService = false;

            // Hide retry panel if shown
            if (retryPanel != null)
            {
                retryPanel.SetActive(false);
            }

            // Check if ServiceManager exists
            if (ServiceManager.Instance == null)
            {
                UpdateProgress(0, "Error: Service Manager not found");
                Debug.LogError("ServiceManager not found. Please ensure it's created before ServiceInitializer");
                ShowRetryPanel();
                return;
            }

            // Begin initialization sequence
            InitializeServicesAsync();
        }

        private async void InitializeServicesAsync()
        {
            try
            {
                // Initialize services in sequence
                //await InitializeConfigServiceAsync();
                await InitializePermissionServiceAsync();
                await InitializeLocationServiceAsync();
                await InitializeHeadTrackingServiceAsync();
                await InitializeAudioServiceAsync();
                await InitializeFirebaseServiceAsync();

                // Check if any critical services failed
                if (failedCriticalService)
                {
                    UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Some services failed to initialize");
                    ShowRetryPanel();
                    isInitializing = false;
                    return;
                }

                // All services initialized
                UpdateProgress(1.0f, "Initialization complete");

                // Wait a moment to show completed progress
                await Task.Delay(500);

                isInitializing = false;

                // Load game scene if configured
                if (loadGameSceneWhenDone)
                {
                    SceneManager.LoadScene(gameSceneName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during service initialization: {e.Message}");
                UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Initialization error");
                ShowRetryPanel();
                isInitializing = false;
            }
        }

        private async Task InitializeConfigServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Initializing configuration...");

            try
            {
                var service = ServiceLocator.GetService<IConfigService>();
                bool success = await service.InitializeAsync();

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<IConfigService>();
                }
                else
                {
                    Debug.LogError("Config service initialization failed");
                    failedCriticalService = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize ConfigService: {e.Message}");
                failedCriticalService = true;
            }
        }

        private async Task InitializePermissionServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Checking permissions...");

            try
            {
                var service = ServiceLocator.GetService<IPermissionService>();
                bool success = await service.InitializeAsync();

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<IPermissionService>();
                }
                else
                {
                    UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Location permission denied");
                    failedCriticalService = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize PermissionService: {e.Message}");
                failedCriticalService = true;
            }
        }

        private async Task InitializeLocationServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Initializing location services...");

            try
            {
                var service = ServiceLocator.GetService<ILocationService>();
                bool success = await service.InitializeAsync();

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<ILocationService>();
                }
                else
                {
                    UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Location services initialization failed");
                    failedCriticalService = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize LocationService: {e.Message}");
                failedCriticalService = true;
            }
        }

        private async Task InitializeHeadTrackingServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Initializing head tracking...");

            try
            {
                var service = ServiceLocator.GetService<IHeadTrackingService>();
                bool success = await service.InitializeAsync();

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<IHeadTrackingService>();
                }
                else
                {
                    Debug.LogWarning("Head tracking service initialization incomplete");
                    // Not marking as critical failure
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize HeadTrackingService: {e.Message}");
                // Not marking as critical failure
            }
        }

        private async Task InitializeAudioServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Initializing audio system...");

            try
            {
                var service = ServiceLocator.GetService<IAudioService>();
                bool success = await service.InitializeAsync();

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<IAudioService>();
                }
                else
                {
                    Debug.LogError("Audio service initialization failed");
                    failedCriticalService = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize AudioService: {e.Message}");
                failedCriticalService = true;
            }
        }

        private async Task InitializeFirebaseServiceAsync()
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Connecting to cloud services...");

            try
            {
                var service = ServiceLocator.GetService<IFirebaseService>();
                bool success = false;

                // Try multiple times with the configured retry policy
                for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
                {
                    if (attempt > 1)
                    {
                        UpdateProgress(ServiceManager.Instance.GetInitializationProgress(),
                            $"Retrying cloud connection ({attempt}/{maxRetryAttempts})...");
                        await Task.Delay(Mathf.RoundToInt(retryDelay * 1000));
                    }

                    success = await service.InitializeAsync();
                    if (success) break;
                }

                if (success)
                {
                    ServiceManager.Instance.MarkServiceInitialized<IFirebaseService>();
                }
                else
                {
                    UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), "Cloud service connection failed");
                    // Firebase failure is not critical for game functionality
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize FirebaseService: {e.Message}");
                // Firebase failure is not critical
            }
        }

        private void ProceedWithoutServices()
        {
            // Hide retry panel
            if (retryPanel != null)
            {
                retryPanel.SetActive(false);
            }

            // Continue to game scene even with initialization failures
            SceneManager.LoadScene(gameSceneName);
        }

        private void ShowRetryPanel()
        {
            if (retryPanel != null)
            {
                retryPanel.SetActive(true);
            }
        }

        private void UpdateProgress(float progress, string status)
        {
            if (progressBar != null)
            {
                progressBar.value = progress;
            }

            if (statusText != null)
            {
                statusText.text = status;
            }

            Debug.Log($"Initialization: {progress:P0} - {status}");
        }
    }
}