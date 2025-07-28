using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;
using TMPro;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Manages hardware setup UI and validation process
    /// Works with extended GameManager for MMRL connection and head movement validation
    /// </summary>
    public class HardwareSetupManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI instructionText;
        [SerializeField] private TextMeshProUGUI providerText;
        [SerializeField] private Slider validationProgress;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button forceConnectButton;
        [SerializeField] private GameObject progressContainer;

        [Header("Validation Settings")]
        [SerializeField] private float headMovementThreshold = 10f;
        [SerializeField] private float validationTimeout = 10f;
        [SerializeField] private bool enableValidation = true;

        [Header("Field Test Settings")]
        [SerializeField] private bool fieldTestMode = true;
        [SerializeField] private float autoProgressDelay = 3f;

        // Services
        private IHeadTrackingService headTrackingService;

        // Validation state
        private bool isValidating = false;
        private float initialHeading;
        private float maxMovement;
        private float validationStartTime;
        private string connectedProvider = "";

        // States
        private enum SetupState
        {
            Connecting,
            Connected,
            Validating,
            Complete,
            Error
        }
        private SetupState currentState;

        private void Awake()
        {
            // Setup UI event handlers
            if (retryButton != null)
                retryButton.onClick.AddListener(RetryConnection);

            if (skipButton != null)
                skipButton.onClick.AddListener(SkipHardwareSetup);

            if (forceConnectButton != null)
                forceConnectButton.onClick.AddListener(ForceConnection);

            // Hide progress initially
            if (progressContainer != null)
                progressContainer.SetActive(false);
        }

        public void StartHardwareSetup()
        {
            Debug.Log("=== Starting Hardware Setup ===");

            // Get head tracking service
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();

            if (headTrackingService == null)
            {
                ShowError("Head tracking service not available");
                return;
            }

            // Subscribe to events
            headTrackingService.ActiveProviderChanged += OnProviderChanged;
            headTrackingService.HeadingUpdated += OnHeadingUpdated;

            // Update UI for connecting state
            SetState(SetupState.Connecting);
            UpdateStatus("Scanning for MMRL headphones...");
            UpdateInstructions("Make sure your headphones are on and close to your phone");
            UpdateProvider("Searching...");

            // Start head tracking (which will trigger provider selection)
            headTrackingService.StartTracking();

            // Set timeout for connection attempt
            StartCoroutine(ConnectionTimeoutHandler());
        }

        private IEnumerator ConnectionTimeoutHandler()
        {
            yield return new WaitForSeconds(15f); // 15 second timeout

            if (currentState == SetupState.Connecting)
            {
                // Still connecting after timeout - check what we have
                if (!string.IsNullOrEmpty(connectedProvider))
                {
                    // We have some provider, proceed
                    OnConnectionEstablished();
                }
                else
                {
                    // No provider at all
                    ShowError("Connection timeout - no devices found");
                }
            }
        }

        private void OnProviderChanged(string providerName)
        {
            Debug.Log($"Hardware Setup: Provider changed to {providerName}");

            connectedProvider = providerName;
            UpdateProvider(providerName);

            if (providerName == "MMRL 9DOF")
            {
                UpdateStatus("✓ Connected to MMRL!");
                OnConnectionEstablished();
            }
            else if (providerName == "Phone Sensors (Advanced)")
            {
                UpdateStatus("✓ Using phone sensors");
                UpdateInstructions("MMRL not available, using phone gyroscope");
                OnConnectionEstablished();
            }
            else if (providerName == "AirPods Pro (Spatial Audio)")
            {
                UpdateStatus("✓ Connected to AirPods!");
                OnConnectionEstablished();
            }
            else if (!string.IsNullOrEmpty(providerName))
            {
                UpdateStatus($"✓ Connected to {providerName}");
                OnConnectionEstablished();
            }
        }

        private void OnConnectionEstablished()
        {
            SetState(SetupState.Connected);

            if (enableValidation && connectedProvider == "MMRL 9DOF")
            {
                // Start validation for MMRL devices
                StartValidation();
            }
            else
            {
                // Skip validation for other providers
                StartCoroutine(AutoCompleteSetup());
            }
        }

        private void StartValidation()
        {
            Debug.Log("Starting head movement validation...");

            SetState(SetupState.Validating);
            UpdateStatus("Device connected! Validating...");
            UpdateInstructions("NOD YOUR HEAD to confirm this is your device");

            // Show progress bar
            if (progressContainer != null)
                progressContainer.SetActive(true);

            // Initialize validation state
            isValidating = true;
            initialHeading = headTrackingService.CurrentHeading;
            maxMovement = 0f;
            validationStartTime = Time.time;

            if (validationProgress != null)
            {
                validationProgress.value = 0f;
                validationProgress.maxValue = headMovementThreshold;
            }
        }

        private void OnHeadingUpdated(float heading)
        {
            if (!isValidating) return;

            // Calculate movement from initial position
            float movement = Mathf.Abs(Mathf.DeltaAngle(initialHeading, heading));
            maxMovement = Mathf.Max(maxMovement, movement);

            // Update progress bar
            if (validationProgress != null)
            {
                validationProgress.value = maxMovement;
            }

            // Update status text with percentage
            int percentage = Mathf.RoundToInt((maxMovement / headMovementThreshold) * 100f);
            UpdateStatus($"Validation: {percentage}% (keep nodding)");

            // Check if validation complete
            if (maxMovement >= headMovementThreshold)
            {
                ValidationSuccess();
            }
            else if (Time.time - validationStartTime > validationTimeout)
            {
                ValidationTimeout();
            }
        }

        private void ValidationSuccess()
        {
            Debug.Log($"Validation successful! Movement: {maxMovement:F1}°");

            isValidating = false;
            SetState(SetupState.Complete);

            UpdateStatus("✓ Device validated!");
            UpdateInstructions("Head tracking ready and confirmed");

            if (progressContainer != null)
                progressContainer.SetActive(false);

            StartCoroutine(CompleteSetupAfterDelay());
        }

        private void ValidationTimeout()
        {
            Debug.LogWarning($"Validation timeout. Movement detected: {maxMovement:F1}°");

            isValidating = false;

            if (maxMovement < 1f)
            {
                // No movement at all - might be wrong device
                UpdateStatus("⚠️ No head movement detected");
                UpdateInstructions("Wrong device? Using anyway...");
            }
            else
            {
                // Some movement but not enough
                UpdateStatus("⚠️ Partial validation");
                UpdateInstructions("Some movement detected, proceeding...");
            }

            if (progressContainer != null)
                progressContainer.SetActive(false);

            StartCoroutine(CompleteSetupAfterDelay());
        }

        private IEnumerator AutoCompleteSetup()
        {
            UpdateStatus("✓ Hardware ready!");
            UpdateInstructions("Setup complete");

            yield return new WaitForSeconds(autoProgressDelay);
            CompleteSetup();
        }

        private IEnumerator CompleteSetupAfterDelay()
        {
            yield return new WaitForSeconds(2f);
            CompleteSetup();
        }

        private void CompleteSetup()
        {
            Debug.Log("Hardware setup complete");

            // Unsubscribe from events
            if (headTrackingService != null)
            {
                headTrackingService.ActiveProviderChanged -= OnProviderChanged;
                headTrackingService.HeadingUpdated -= OnHeadingUpdated;
            }

            // Notify GameManager
            //if (GameManager.Instance != null)
            //{
            //    GameManager.Instance.OnHardwareSetupComplete();
            //}
        }

        // -----------------------------------------------
        // UI Button Handlers
        // -----------------------------------------------

        private void RetryConnection()
        {
            Debug.Log("Retrying hardware connection...");
            StartHardwareSetup();
        }

        private void SkipHardwareSetup()
        {
            Debug.Log("Skipping hardware setup");

            // Stop any ongoing processes
            isValidating = false;

            if (headTrackingService != null)
            {
                headTrackingService.ActiveProviderChanged -= OnProviderChanged;
                headTrackingService.HeadingUpdated -= OnHeadingUpdated;
            }

            UpdateStatus("Hardware setup skipped");
            UpdateInstructions("Proceeding without head tracking");

            // Complete setup anyway
            StartCoroutine(CompleteSetupAfterDelay());
        }

        private void ForceConnection()
        {
            Debug.Log("Force completing hardware setup");

            isValidating = false;
            UpdateStatus("✓ Hardware setup forced complete");
            UpdateInstructions("Continuing to next step...");

            StartCoroutine(CompleteSetupAfterDelay());
        }

        // -----------------------------------------------
        // UI Update Methods
        // -----------------------------------------------

        private void SetState(SetupState newState)
        {
            currentState = newState;

            // Update button visibility based on state
            UpdateButtonVisibility();
        }

        private void UpdateButtonVisibility()
        {
            bool showRetry = (currentState == SetupState.Error);
            bool showSkip = (currentState == SetupState.Connecting || currentState == SetupState.Validating);
            bool showForce = fieldTestMode && (currentState == SetupState.Connecting || currentState == SetupState.Validating);

            if (retryButton != null)
                retryButton.gameObject.SetActive(showRetry);

            if (skipButton != null)
                skipButton.gameObject.SetActive(showSkip);

            if (forceConnectButton != null)
                forceConnectButton.gameObject.SetActive(showForce);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;

            Debug.Log($"Hardware Setup Status: {message}");
        }

        private void UpdateInstructions(string message)
        {
            if (instructionText != null)
                instructionText.text = message;
        }

        private void UpdateProvider(string provider)
        {
            if (providerText != null)
                providerText.text = $"Provider: {provider}";
        }

        private void ShowError(string error)
        {
            SetState(SetupState.Error);
            UpdateStatus($"❌ {error}");
            UpdateInstructions("Check hardware and try again");

            Debug.LogError($"Hardware Setup Error: {error}");
        }

        // -----------------------------------------------
        // Cleanup
        // -----------------------------------------------

        private void OnDestroy()
        {
            // Cleanup event subscriptions
            if (headTrackingService != null)
            {
                headTrackingService.ActiveProviderChanged -= OnProviderChanged;
                headTrackingService.HeadingUpdated -= OnHeadingUpdated;
            }
        }

        // -----------------------------------------------
        // Public Debug Methods (for testing)
        // -----------------------------------------------

        public void TestConnection()
        {
            if (headTrackingService != null)
            {
                UpdateStatus($"Test: Provider={headTrackingService.ActiveProviderName}, Heading={headTrackingService.CurrentHeading:F1}°");
            }
            else
            {
                UpdateStatus("Test: No head tracking service");
            }
        }

        public void SimulateValidation()
        {
            if (currentState == SetupState.Validating)
            {
                ValidationSuccess();
            }
        }
    }
}