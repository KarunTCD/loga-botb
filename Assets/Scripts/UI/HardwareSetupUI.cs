using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Hardware Setup UI Component - APP STORE SAFE VERSION
    /// FIXED: Race conditions, coroutine leaks, state desync
    /// </summary>
    public class HardwareSetupUI : MonoBehaviour
    {
        [Header("Status Display")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI instructionText;
        [SerializeField] private TextMeshProUGUI providerText;
        [SerializeField] private TextMeshProUGUI errorText;

        [Header("Progress Visualization")]
        [SerializeField] private Slider validationProgress;
        [SerializeField] private GameObject progressContainer;
        [SerializeField] private Image progressFill;
        [SerializeField] private Color progressColor = Color.green;

        [Header("Interactive Elements")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button forceConnectButton;
        [SerializeField] private Button backButton;

        [Header("Validation Settings")]
        [SerializeField] private float headMovementThreshold = 15f;
        [SerializeField] private float validationTimeout = 12f;
        [SerializeField] private bool enableValidation = true;

        [Header("Development Settings")]
        [SerializeField] private bool fieldTestMode = false;
        [SerializeField] private float autoProgressDelay = 2f;

        #region State Management

        public enum SetupState
        {
            Idle,            // NEW: Not active
            Initializing,
            ValidatingServices,
            DetectingHardware,
            Connected,
            Validating,
            Complete,
            Error,
            Skipped,
            Cancelled        // NEW: Explicitly cancelled
        }

        private SetupState currentState = SetupState.Idle;
        private UIManager uiManager;

        // CRITICAL: Session tracking
        private int currentSessionId = 0;
        private bool isSessionActive = false;

        #endregion

        #region Service References

        private IHeadTrackingService headTrackingService;
        private IAudioService audioService;
        private ILocationService locationService;
        private IStorageService storageService;

        #endregion

        #region Validation State

        private bool isValidating = false;
        private float initialHeading;
        private float maxMovementDetected;
        private float validationStartTime;
        private string connectedProviderName = "";

        // CRITICAL: Track all active coroutines
        private Coroutine setupSequenceCoroutine;
        private Coroutine validationCoroutine;
        private Coroutine timeoutCoroutine;
        private Coroutine connectionPollingCoroutine;

        #endregion

        #region Initialization

        private void Start()
        {
            InitializeUI();
            SetupButtonListeners();
        }

        public void SetUIManager(UIManager uiManager)
        {
            this.uiManager = uiManager;
            Debug.Log("HardwareSetupUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            SetProgressVisibility(false);
            SetAllButtonsInteractable(true);
            ClearError();

            if (progressFill != null)
                progressFill.color = progressColor;
        }

        private void SetupButtonListeners()
        {
            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryButtonPressed);
            if (skipButton != null)
                skipButton.onClick.AddListener(OnSkipButtonPressed);
            if (forceConnectButton != null)
                forceConnectButton.onClick.AddListener(OnForceConnectButtonPressed);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackButtonPressed);
        }

        #endregion

        #region Main Hardware Setup Flow - FIXED

        public void StartHardwareSetup()
        {
            Debug.Log($"HardwareSetupUI: StartHardwareSetup called (Current session: {currentSessionId}, Active: {isSessionActive})");

            // CRITICAL: Cancel any existing session first
            if (isSessionActive)
            {
                Debug.LogWarning("HardwareSetupUI: Cancelling existing session before starting new one");
                CancelCurrentSession();
            }

            // Start new session
            currentSessionId++;
            isSessionActive = true;

            Debug.Log($"HardwareSetupUI: Starting NEW session {currentSessionId}");

            TransitionToState(SetupState.Initializing);
            UpdateStatus("Initializing hardware setup...");
            UpdateInstructions("Please wait while we prepare the system");

            // Start the setup sequence
            setupSequenceCoroutine = StartCoroutine(BeginSetupSequence(currentSessionId));
        }

        private IEnumerator BeginSetupSequence(int sessionId)
        {
            Debug.Log($"HardwareSetupUI: BeginSetupSequence started for session {sessionId}");

            yield return new WaitForSeconds(0.5f);

            // CHECK: Session still valid?
            if (!IsSessionValid(sessionId))
            {
                Debug.Log($"HardwareSetupUI: Session {sessionId} cancelled before validation");
                yield break;
            }

            // Step 1: Validate services
            TransitionToState(SetupState.ValidatingServices);
            bool servicesValid = ValidateServicesSync();

            if (!servicesValid)
            {
                if (IsSessionValid(sessionId))
                {
                    ShowError("Service validation failed");
                    TransitionToState(SetupState.Error);
                }
                yield break;
            }

            yield return new WaitForSeconds(1f);

            // CHECK: Session still valid?
            if (!IsSessionValid(sessionId))
            {
                Debug.Log($"HardwareSetupUI: Session {sessionId} cancelled after validation");
                yield break;
            }

            // Step 2: Start hardware detection
            yield return StartCoroutine(DetectAndConnectHardware(sessionId));
        }

        #endregion

        #region Service Validation - SIMPLIFIED

        private bool ValidateServicesSync()
        {
            UpdateStatus("Validating system services...");
            UpdateInstructions("Checking audio, location, and head tracking systems");

            try
            {
                // Get service references
                headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
                audioService = ServiceLocator.GetService<IAudioService>();
                locationService = ServiceLocator.GetService<ILocationService>();
                storageService = ServiceLocator.GetService<IStorageService>();

                // Check for missing services
                if (headTrackingService == null || audioService == null ||
                    locationService == null || storageService == null)
                {
                    Debug.LogError("HardwareSetupUI: Missing critical services");
                    return false;
                }

                UpdateStatus("Services validated successfully");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"HardwareSetupUI: Service validation error - {e.Message}");
                return false;
            }
        }

        #endregion

        #region Hardware Detection - FIXED

        private IEnumerator DetectAndConnectHardware(int sessionId)
        {
            if (!IsSessionValid(sessionId)) yield break;

            TransitionToState(SetupState.DetectingHardware);
            UpdateStatus("Scanning for MMRL headphones...");

            if (headTrackingService != null)
            {
                // Check if already connected FIRST
                string currentProvider = headTrackingService.ActiveProviderName;
                if (!string.IsNullOrEmpty(currentProvider) && currentProvider != "None")
                {
                    Debug.Log($"HardwareSetupUI: Provider already active: {currentProvider}");
                    OnProviderChanged(currentProvider, sessionId);
                    yield break;
                }

                // Subscribe to events
                headTrackingService.ActiveProviderChanged += OnProviderChangedEvent;
                headTrackingService.HeadingUpdated += OnHeadingUpdated;
                headTrackingService.StartTracking();

                // Start timeout
                timeoutCoroutine = StartCoroutine(HandleConnectionTimeout(sessionId));

                // Start polling as backup
                connectionPollingCoroutine = StartCoroutine(PollForConnection(sessionId));
            }

            yield return null;
        }

        private void OnProviderChangedEvent(string providerName)
        {
            // This is called by the event - pass current session ID
            OnProviderChanged(providerName, currentSessionId);
        }

        private IEnumerator PollForConnection(int sessionId)
        {
            while (IsSessionValid(sessionId) && currentState == SetupState.DetectingHardware)
            {
                if (headTrackingService != null)
                {
                    string provider = headTrackingService.ActiveProviderName;
                    if (!string.IsNullOrEmpty(provider) && provider != "None")
                    {
                        Debug.Log($"HardwareSetupUI: Provider detected via polling: {provider}");
                        OnProviderChanged(provider, sessionId);
                        yield break;
                    }
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private IEnumerator HandleConnectionTimeout(int sessionId)
        {
            yield return new WaitForSeconds(15f);

            if (!IsSessionValid(sessionId)) yield break;

            if (currentState == SetupState.DetectingHardware)
            {
                if (!string.IsNullOrEmpty(connectedProviderName))
                {
                    Debug.Log("HardwareSetupUI: Timeout but provider available - proceeding");
                    OnConnectionEstablished(sessionId);
                }
                else
                {
                    Debug.Log("HardwareSetupUI: Connection timeout with no provider");
                    ShowError("No compatible devices found");
                    TransitionToState(SetupState.Error);
                }
            }
        }

        private void OnProviderChanged(string providerName, int sessionId)
        {
            if (!IsSessionValid(sessionId))
            {
                Debug.Log($"HardwareSetupUI: Ignoring provider change - session {sessionId} invalid");
                return;
            }

            Debug.Log($"HardwareSetupUI: Provider changed to '{providerName}' (Session: {sessionId})");

            connectedProviderName = providerName;
            UpdateProvider(providerName);

            if (string.IsNullOrEmpty(providerName) || providerName == "None")
            {
                UpdateStatus("No device connected...");
                return;
            }

            // Stop timeout
            StopCoroutineSafely(ref timeoutCoroutine);
            StopCoroutineSafely(ref connectionPollingCoroutine);

            // Update status
            UpdateStatus($"Connected to {providerName}");
            UpdateInstructions("Head tracking device connected");

            // Proceed
            StartCoroutine(DelayedConnectionEstablished(sessionId));
        }

        private IEnumerator DelayedConnectionEstablished(int sessionId)
        {
            yield return new WaitForSeconds(1f);

            if (IsSessionValid(sessionId))
            {
                OnConnectionEstablished(sessionId);
            }
        }

        private void OnConnectionEstablished(int sessionId)
        {
            if (!IsSessionValid(sessionId)) return;

            TransitionToState(SetupState.Connected);

            // Skip validation for simplicity (or enable if needed)
            StartCoroutine(AutoCompleteSetup(sessionId));
        }

        #endregion

        #region Setup Completion - FIXED

        private IEnumerator AutoCompleteSetup(int sessionId)
        {
            if (!IsSessionValid(sessionId)) yield break;

            UpdateStatus("Hardware setup complete!");
            UpdateInstructions("Device ready for gameplay");

            yield return new WaitForSeconds(autoProgressDelay);

            if (IsSessionValid(sessionId))
            {
                CompleteSetup();
            }
        }

        private void CompleteSetup()
        {
            Debug.Log($"HardwareSetupUI: Hardware setup completed successfully (Session: {currentSessionId})");

            TransitionToState(SetupState.Complete);
            CleanupSession();

            // Report completion to UIManager
            if (uiManager != null)
            {
                uiManager.OnHardwareSetupComplete();
            }
            else
            {
                Debug.LogError("HardwareSetupUI: UIManager reference not set");
                ShowError("Internal error - cannot proceed");
            }
        }

        #endregion

        #region Button Event Handlers - FIXED

        private void OnRetryButtonPressed()
        {
            Debug.Log("HardwareSetupUI: Retry button pressed");

            CancelCurrentSession();
            ClearError();

            // Restart
            StartHardwareSetup();
        }

        private void OnSkipButtonPressed()
        {
            Debug.Log("HardwareSetupUI: Skip button pressed");

            CancelCurrentSession();
            TransitionToState(SetupState.Skipped);

            UpdateStatus("Hardware setup skipped");
            UpdateInstructions("Proceeding without validation...");

            StartCoroutine(CompleteAfterSkip());
        }

        private IEnumerator CompleteAfterSkip()
        {
            yield return new WaitForSeconds(1f);
            CompleteSetup();
        }

        private void OnForceConnectButtonPressed()
        {
            Debug.Log("HardwareSetupUI: Force connect pressed");

            CancelCurrentSession();
            UpdateStatus("Connection forced - setup complete");
            StartCoroutine(CompleteAfterSkip());
        }

        private void OnBackButtonPressed()
        {
            Debug.Log("HardwareSetupUI: Back button pressed - CANCELLING SESSION");

            // CRITICAL: Cancel session and cleanup
            CancelCurrentSession();

            // Report to UIManager
            if (uiManager != null)
            {
                uiManager.OnBackButtonPressed();
            }
        }

        #endregion

        #region Session Management - CRITICAL

        /// <summary>
        /// Check if a session ID is still valid (not cancelled)
        /// </summary>
        private bool IsSessionValid(int sessionId)
        {
            bool valid = isSessionActive && sessionId == currentSessionId;

            if (!valid)
            {
                Debug.Log($"HardwareSetupUI: Session {sessionId} is INVALID (current: {currentSessionId}, active: {isSessionActive})");
            }

            return valid;
        }

        /// <summary>
        /// Cancel the current session and stop all coroutines
        /// </summary>
        private void CancelCurrentSession()
        {
            Debug.Log($"HardwareSetupUI: CANCELLING SESSION {currentSessionId}");

            isSessionActive = false;

            // Stop ALL coroutines
            StopAllCoroutinesSafely();

            // Cleanup
            CleanupSession();

            // Reset to idle
            TransitionToState(SetupState.Cancelled);

            Debug.Log("HardwareSetupUI: Session cancellation complete");
        }

        /// <summary>
        /// Stop all coroutines safely
        /// </summary>
        private void StopAllCoroutinesSafely()
        {
            int stoppedCount = 0;

            if (StopCoroutineSafely(ref setupSequenceCoroutine)) stoppedCount++;
            if (StopCoroutineSafely(ref validationCoroutine)) stoppedCount++;
            if (StopCoroutineSafely(ref timeoutCoroutine)) stoppedCount++;
            if (StopCoroutineSafely(ref connectionPollingCoroutine)) stoppedCount++;

            Debug.Log($"HardwareSetupUI: Stopped {stoppedCount} coroutines");
        }

        /// <summary>
        /// Safely stop a single coroutine
        /// </summary>
        private bool StopCoroutineSafely(ref Coroutine coroutine)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
                coroutine = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cleanup session resources
        /// </summary>
        private void CleanupSession()
        {
            isValidating = false;

            // Unsubscribe from events
            if (headTrackingService != null)
            {
                headTrackingService.ActiveProviderChanged -= OnProviderChangedEvent;
                headTrackingService.HeadingUpdated -= OnHeadingUpdated;
            }

            SetProgressVisibility(false);
        }

        #endregion

        #region UI State Management

        private void TransitionToState(SetupState newState)
        {
            if (currentState == newState) return;

            Debug.Log($"HardwareSetupUI: State transition {currentState} → {newState}");

            currentState = newState;
            UpdateButtonVisibility();
        }

        private void UpdateButtonVisibility()
        {
            bool showRetry = (currentState == SetupState.Error);
            bool showSkip = (currentState == SetupState.DetectingHardware || currentState == SetupState.Validating);
            bool showForce = fieldTestMode && (currentState != SetupState.Complete);
            bool showBack = (currentState != SetupState.ValidatingServices &&
                           currentState != SetupState.Complete &&
                           currentState != SetupState.Cancelled);

            SetButtonVisible(retryButton, showRetry);
            SetButtonVisible(skipButton, showSkip);
            SetButtonVisible(forceConnectButton, showForce);
            SetButtonVisible(backButton, showBack);
        }

        private void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private void SetAllButtonsInteractable(bool interactable)
        {
            if (retryButton != null) retryButton.interactable = interactable;
            if (skipButton != null) skipButton.interactable = interactable;
            if (forceConnectButton != null) forceConnectButton.interactable = interactable;
            if (backButton != null) backButton.interactable = interactable;
        }

        private void SetProgressVisibility(bool visible)
        {
            if (progressContainer != null)
            {
                progressContainer.SetActive(visible);
            }
        }

        #endregion

        #region Display Updates

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                // Replace checkmark with [OK] for font compatibility
                message = message.Replace("✓", "[OK]").Replace("⚠️", "[!]");
                statusText.text = message;
            }

            Debug.Log($"HardwareSetupUI Status: {message}");
        }

        private void UpdateInstructions(string message)
        {
            if (instructionText != null)
            {
                instructionText.text = message;
            }
        }

        private void UpdateProvider(string providerName)
        {
            if (providerText != null)
            {
                providerText.text = $"Active Provider: {providerName}";
            }
        }

        private void ShowError(string errorMessage)
        {
            if (errorText != null)
            {
                errorText.text = $"ERROR: {errorMessage}";
                errorText.color = Color.red;
            }

            Debug.LogError($"HardwareSetupUI Error: {errorMessage}");
        }

        private void ClearError()
        {
            if (errorText != null)
            {
                errorText.text = "";
            }
        }

        #endregion

        #region Unused Validation Methods (Keep for reference)

        private void OnHeadingUpdated(float heading)
        {
            // Not used currently
        }

        #endregion

        #region Lifecycle - FIXED

        private void OnDisable()
        {
            Debug.Log("HardwareSetupUI: OnDisable - cancelling session");
            CancelCurrentSession();
        }

        private void OnDestroy()
        {
            CancelCurrentSession();

            // Remove button listeners
            if (retryButton != null)
                retryButton.onClick.RemoveListener(OnRetryButtonPressed);
            if (skipButton != null)
                skipButton.onClick.RemoveListener(OnSkipButtonPressed);
            if (forceConnectButton != null)
                forceConnectButton.onClick.RemoveListener(OnForceConnectButtonPressed);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackButtonPressed);

            Debug.Log("HardwareSetupUI: Cleanup completed");
        }

        #endregion
    }
}