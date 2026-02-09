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
        [SerializeField] private string gameSceneName = "GameScene"; // Your existing GameScene with all UI

        [Header("Initialization Options")]
        [SerializeField] private bool autoStartInitialization = true;
        [SerializeField] private bool loadGameSceneWhenDone = true;

        [Header("UI Elements")]
        [SerializeField] private GameObject retryPanel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button continueAnywayButton;

        private bool isInitializing = false;

        private void Start()
        {
            Debug.Log("ServiceInitializer: Starting in LoadingScene");

            // Subscribe to ServiceManager events
            ServiceManager.ServiceInitializationUpdate += OnServiceUpdate;
            ServiceManager.InitializationProgress += OnProgressUpdate;
            ServiceManager.AllServicesReady += OnAllServicesReady;

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
                isInitializing = false;
                return;
            }

            // Reset services
            ServiceManager.Instance.ResetAllServices();

            UpdateProgress(0, "Starting service initialization...");
            Debug.Log("ServiceInitializer: Starting initialization sequence");

            // Restart the coroutine
            ServiceManager.Instance.StartCoroutine(ServiceManager.Instance.RestartInitialization());
        }

        private void OnServiceUpdate(string status)
        {
            UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), status);
        }

        private void OnProgressUpdate(float progress)
        {
            UpdateProgress(progress, null);
        }

        private async void OnAllServicesReady()
        {
            Debug.Log("ServiceInitializer: All services ready!");

            // Check if critical services failed
            if (!ServiceManager.Instance.AreCriticalServicesReady())
            {
                var failedServices = ServiceManager.Instance.GetFailedCriticalServices();
                string failureMessage = $"Critical services failed: {string.Join(", ", failedServices)}";

                UpdateProgress(ServiceManager.Instance.GetInitializationProgress(), failureMessage);
                ShowRetryPanel();
                isInitializing = false;
                return;
            }

            UpdateProgress(1.0f, "All services ready! Loading game...");

            // Wait a moment to show completed progress
            await Task.Delay(1500);

            isInitializing = false;

            // Load GameScene with all UI panels
            if (loadGameSceneWhenDone)
            {
                Debug.Log($"ServiceInitializer: Loading GameScene '{gameSceneName}' - UIManager will start in MainMenu");
                SceneManager.LoadScene(gameSceneName);
            }
        }

        private void ProceedWithoutServices()
        {
            Debug.LogWarning("ServiceInitializer: Proceeding without all services initialized");

            // Hide retry panel
            if (retryPanel != null)
            {
                retryPanel.SetActive(false);
            }

            // Continue to game scene even with initialization failures
            Debug.Log($"ServiceInitializer: Force loading GameScene '{gameSceneName}'");
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

            if (statusText != null && !string.IsNullOrEmpty(status))
            {
                statusText.text = status;
            }

            if (!string.IsNullOrEmpty(status))
            {
                Debug.Log($"ServiceInitializer: {progress:P0} - {status}");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            ServiceManager.ServiceInitializationUpdate -= OnServiceUpdate;
            ServiceManager.InitializationProgress -= OnProgressUpdate;
            ServiceManager.AllServicesReady -= OnAllServicesReady;

            // Clean up button listeners
            if (retryButton != null)
                retryButton.onClick.RemoveListener(StartInitialization);

            if (continueAnywayButton != null)
                continueAnywayButton.onClick.RemoveListener(ProceedWithoutServices);
        }
    }
}