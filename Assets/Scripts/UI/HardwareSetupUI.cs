using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Hardware Setup UI Component - SIMPLIFIED VERSION
    /// Pure UI - All business logic delegated to HardwareManager
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

        [Header("Development Settings")]
        [SerializeField] private bool fieldTestMode = false;
        [SerializeField] private float autoProgressDelay = 2f;

        #region State Management

        public enum SetupState
        {
            Idle,
            Initializing,
            ValidatingServices,
            DetectingHardware,
            Connected,
            Complete,
            Error,
            Skipped,
            Cancelled
        }

        private SetupState currentState = SetupState.Idle;
        private UIManager uiManager;
        private HardwareManager hardwareManager;

        // CRITICAL: Session tracking
        private int currentSessionId = 0;
        private bool isSessionActive = false;

        #endregion

        #region State

        // CRITICAL: Track setup coroutine
        private Coroutine setupSequenceCoroutine;

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

        public void SetHardwareManager(HardwareManager manager)
        {
            this.hardwareManager = manager;
            Debug.Log("HardwareSetupUI: HardwareManager reference set");
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

        #region Main Hardware Setup Flow - SIMPLIFIED

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

            // Subscribe to HardwareManager events
            if (hardwareManager != null)
            {
                hardwareManager.OnSetupComplete += OnHardwareSetupComplete;
                hardwareManager.OnSetupFailed += OnHardwareSetupFailed;
                hardwareManager.OnStatusUpdate += OnHardwareStatusUpdate;
                hardwareManager.OnProviderDetected += OnHardwareProviderDetected;
            }

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
                Debug.Log($"HardwareSetupUI: Session {sessionId} cancelled before setup");
                yield break;
            }

            // Just call HardwareManager and let it handle everything
            if (hardwareManager != null)
            {
                TransitionToState(SetupState.ValidatingServices);

                // Start async setup (events will notify us)
                _ = hardwareManager.BeginSetup();
            }
            else
            {
                Debug.LogError("HardwareSetupUI: HardwareManager not assigned!");
                ShowError("Hardware manager not available");
                TransitionToState(SetupState.Error);
            }
        }

        #endregion

        #region HardwareManager Event Handlers

        /// <summary>
        /// Called when HardwareManager completes setup successfully
        /// </summary>
        private void OnHardwareSetupComplete()
        {
            if (!IsSessionValid(currentSessionId))
            {
                Debug.Log("HardwareSetupUI: Ignoring setup complete - session invalid");
                return;
            }

            Debug.Log("HardwareSetupUI: Hardware setup completed");

            TransitionToState(SetupState.Complete);
            UpdateStatus("Hardware setup complete!");
            UpdateInstructions("Device ready for gameplay");

            StartCoroutine(AutoCompleteSetup(currentSessionId));
        }

        /// <summary>
        /// Called when HardwareManager fails
        /// </summary>
        private void OnHardwareSetupFailed(string error)
        {
            if (!IsSessionValid(currentSessionId))
            {
                Debug.Log("HardwareSetupUI: Ignoring setup failed - session invalid");
                return;
            }

            Debug.LogError($"HardwareSetupUI: Hardware setup failed - {error}");

            ShowError(error);
            TransitionToState(SetupState.Error);
        }

        /// <summary>
        /// Called when HardwareManager updates status
        /// </summary>
        private void OnHardwareStatusUpdate(string status)
        {
            if (!IsSessionValid(currentSessionId))
            {
                return;
            }

            UpdateStatus(status);
        }

        /// <summary>
        /// Called when HardwareManager detects a provider
        /// </summary>
        private void OnHardwareProviderDetected(string providerName)
        {
            if (!IsSessionValid(currentSessionId))
            {
                return;
            }

            Debug.Log($"HardwareSetupUI: Provider detected - {providerName}");
            UpdateProvider(providerName);

            if (!string.IsNullOrEmpty(providerName) && providerName != "None")
            {
                TransitionToState(SetupState.Connected);
            }
        }

        #endregion

        #region Setup Completion

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

        #region Button Event Handlers

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

        #region Session Management

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

            // Cancel hardware manager setup
            if (hardwareManager != null)
            {
                hardwareManager.CancelSetup();
            }

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
            // Unsubscribe from HardwareManager events
            if (hardwareManager != null)
            {
                hardwareManager.OnSetupComplete -= OnHardwareSetupComplete;
                hardwareManager.OnSetupFailed -= OnHardwareSetupFailed;
                hardwareManager.OnStatusUpdate -= OnHardwareStatusUpdate;
                hardwareManager.OnProviderDetected -= OnHardwareProviderDetected;
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
            bool showSkip = (currentState == SetupState.DetectingHardware);
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

        #region Lifecycle

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