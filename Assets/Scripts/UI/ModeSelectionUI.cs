using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Mode Selection UI Component - handles player vs spectator choice
    /// Manages session input and reports selections to UIManager
    /// </summary>
    public class ModeSelectionUI : MonoBehaviour
    {
        [Header("Mode Selection")]
        [SerializeField] private Button playerModeButton;
        [SerializeField] private Button spectatorModeButton;
        [SerializeField] private TextMeshProUGUI modeDescriptionText;

        [Header("Spectator Mode Input")]
        [SerializeField] private TMP_InputField sessionInputField;
        [SerializeField] private Button connectButton;
        [SerializeField] private GameObject spectatorInputContainer;
        [SerializeField] private TextMeshProUGUI sessionInputLabel;

        [Header("Tutorial Access")]
        [SerializeField] private Button runTutorialButton;
        [SerializeField] private TextMeshProUGUI tutorialPromptText;

        [Header("Status Display")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI errorText;

        [Header("Navigation")]
        [SerializeField] private Button backButton;

        [Header("Visual Elements")]
        [SerializeField] private CanvasGroup modeButtonsGroup;
        [SerializeField] private CanvasGroup spectatorInputGroup;

        #region State Management

        private enum SelectionState
        {
            ChoosingMode,
            EnteringSession,
            Connecting,
            Starting
        }

        private SelectionState currentState = SelectionState.ChoosingMode;
        private UIManager uiManager;
        private bool isInteractable = true;

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
            Debug.Log("ModeSelectionUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            SetupInitialState();
            UpdateModeDescriptions();
            ShowTutorialPromptIfNeeded();
        }

        private void SetupButtonListeners()
        {
            if (playerModeButton != null)
                playerModeButton.onClick.AddListener(OnPlayerModeButtonPressed);
            if (spectatorModeButton != null)
                spectatorModeButton.onClick.AddListener(OnSpectatorModeButtonPressed);
            if (connectButton != null)
                connectButton.onClick.AddListener(OnConnectButtonPressed);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackButtonPressed);
            if (runTutorialButton != null)
                runTutorialButton.onClick.AddListener(OnRunTutorialButtonPressed);

            // Setup input field events
            if (sessionInputField != null)
            {
                sessionInputField.onValueChanged.AddListener(OnSessionInputChanged);
                sessionInputField.onEndEdit.AddListener(OnSessionInputEndEdit);
            }
        }

        private void SetupInitialState()
        {
            TransitionToState(SelectionState.ChoosingMode);
            ClearError();

            if (statusText != null)
                statusText.text = "Hardware setup complete! Choose your game mode:";
        }

        #endregion

        #region UI State Management

        private void TransitionToState(SelectionState newState)
        {
            if (currentState == newState) return;

            Debug.Log($"ModeSelectionUI: State transition {currentState} → {newState}");

            currentState = newState;
            UpdateUIForCurrentState();
        }

        private void UpdateUIForCurrentState()
        {
            switch (currentState)
            {
                case SelectionState.ChoosingMode:
                    ShowModeSelection();
                    break;
                case SelectionState.EnteringSession:
                    ShowSpectatorInput();
                    break;
                case SelectionState.Connecting:
                    ShowConnectingState();
                    break;
                case SelectionState.Starting:
                    ShowStartingState();
                    break;
            }
        }

        private void ShowModeSelection()
        {
            SetElementVisible(modeButtonsGroup, true);
            SetElementVisible(spectatorInputGroup, false);
            SetElementVisible(spectatorInputContainer, false);

            SetModeButtonsInteractable(true);

            if (statusText != null)
                statusText.text = "Choose your experience:";
        }

        private void ShowSpectatorInput()
        {
            SetElementVisible(modeButtonsGroup, false);
            SetElementVisible(spectatorInputGroup, true);
            SetElementVisible(spectatorInputContainer, true);

            if (statusText != null)
                statusText.text = "Enter the session ID to watch:";

            if (sessionInputField != null)
            {
                sessionInputField.text = "";
                sessionInputField.interactable = true;
                sessionInputField.ActivateInputField();
            }

            if (connectButton != null)
                connectButton.interactable = false;
        }

        private void ShowConnectingState()
        {
            SetSpectatorInputInteractable(false);

            if (statusText != null)
                statusText.text = "Connecting to session...";
        }

        private void ShowStartingState()
        {
            SetAllInteractable(false);

            if (statusText != null)
                statusText.text = "Starting experience...";
        }

        #endregion

        #region Public Interface

        public void ResetUI()
        {
            Debug.Log("ModeSelectionUI: Resetting UI to initial state");

            SetupInitialState();
            SetAllInteractable(true);
        }

        public void SetInteractable(bool interactable)
        {
            Debug.Log($"ModeSelectionUI: Setting interactable to {interactable}");

            isInteractable = interactable;
            SetAllInteractable(interactable);
        }

        #endregion

        #region Button Event Handlers

        private void OnPlayerModeButtonPressed()
        {
            if (!isInteractable)
            {
                Debug.Log("ModeSelectionUI: Player mode button pressed but UI not interactable");
                return;
            }

            Debug.Log("ModeSelectionUI: Player mode button pressed");

            TransitionToState(SelectionState.Starting);
            StartCoroutine(AnimateButtonPress(playerModeButton));

            // Report to UIManager
            if (uiManager != null)
            {
                uiManager.OnPlayerModeSelected();
            }
            else
            {
                Debug.LogError("ModeSelectionUI: UIManager reference not set");
                ShowError("Internal error - cannot start player mode");
                ResetUI();
            }
        }

        private void OnSpectatorModeButtonPressed()
        {
            if (!isInteractable)
            {
                Debug.Log("ModeSelectionUI: Spectator mode button pressed but UI not interactable");
                return;
            }

            Debug.Log("ModeSelectionUI: Spectator mode button pressed");

            StartCoroutine(AnimateButtonPress(spectatorModeButton));
            TransitionToState(SelectionState.EnteringSession);
        }

        private void OnConnectButtonPressed()
        {
            if (!isInteractable || sessionInputField == null)
            {
                return;
            }

            string sessionId = sessionInputField.text.Trim();

            if (string.IsNullOrEmpty(sessionId))
            {
                ShowError("Please enter a valid session ID");
                return;
            }

            if (!IsValidSessionId(sessionId))
            {
                ShowError("Invalid session ID format");
                return;
            }

            Debug.Log($"ModeSelectionUI: Connecting to session {sessionId}");

            TransitionToState(SelectionState.Connecting);
            StartCoroutine(AnimateButtonPress(connectButton));

            // Report to UIManager
            if (uiManager != null)
            {
                uiManager.OnSpectatorModeSelected(sessionId);
            }
            else
            {
                Debug.LogError("ModeSelectionUI: UIManager reference not set");
                ShowError("Internal error - cannot connect to session");
                TransitionToState(SelectionState.EnteringSession);
            }
        }

        private void OnBackButtonPressed()
        {
            if (!isInteractable)
            {
                return;
            }

            Debug.Log("ModeSelectionUI: Back button pressed");

            StartCoroutine(AnimateButtonPress(backButton));

            // Handle different back behaviors based on current state
            if (currentState == SelectionState.EnteringSession)
            {
                // Go back to mode selection
                TransitionToState(SelectionState.ChoosingMode);
            }
            else
            {
                // Report to UIManager for navigation
                if (uiManager != null)
                {
                    uiManager.OnBackButtonPressed();
                }
                else
                {
                    Debug.LogError("ModeSelectionUI: UIManager reference not set");
                }
            }
        }

        private void OnRunTutorialButtonPressed()
        {
            if (!isInteractable) return;

            Debug.Log("ModeSelectionUI: Run tutorial button pressed");
            StartCoroutine(AnimateButtonPress(runTutorialButton));

            if (uiManager != null)
            {
                uiManager.OnRunTutorialAgain();
            }
            else
            {
                Debug.LogError("ModeSelectionUI: UIManager reference not set");
            }
        }

        #endregion

        #region Input Field Handlers

        private void OnSessionInputChanged(string value)
        {
            // Update connect button availability based on input
            bool hasValidInput = !string.IsNullOrEmpty(value.Trim());

            if (connectButton != null)
            {
                connectButton.interactable = hasValidInput && isInteractable;
            }

            // Clear error when user starts typing
            if (hasValidInput)
            {
                ClearError();
            }
        }

        private void OnSessionInputEndEdit(string value)
        {
            // Auto-connect if Enter key was pressed and input is valid
            if (Input.inputString.Contains("\n") && !string.IsNullOrEmpty(value.Trim()))
            {
                if (IsValidSessionId(value.Trim()))
                {
                    OnConnectButtonPressed();
                }
            }
        }

        #endregion

        #region Content Updates

        private void UpdateModeDescriptions()
        {
            if (modeDescriptionText != null)
            {
                modeDescriptionText.text = "Choose your experience:\n\n" +
                                         "• PLAYER MODE: Walk the battlefield yourself, collect artifacts, and experience the full interactive journey\n\n" +
                                         "• SPECTATOR MODE: Watch another player's journey in real-time and share the experience together";
            }
        }

        private void ShowTutorialPromptIfNeeded()
        {
            bool hasCompletedTutorial = PlayerPrefs.HasKey("TutorialCompleted");

            if (tutorialPromptText != null)
            {
                if (hasCompletedTutorial)
                {
                    tutorialPromptText.text = "Want to review the tutorial again?";
                }
                else
                {
                    tutorialPromptText.text = "The tutorial was shown before this step.";
                }
            }

            if (runTutorialButton != null)
            {
                runTutorialButton.gameObject.SetActive(true);

                var buttonText = runTutorialButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText == null)
                    buttonText = runTutorialButton.GetComponentInChildren<TextMeshProUGUI>();

                if (buttonText != null)
                {
                    buttonText.text = hasCompletedTutorial ? "Run Tutorial Again" : "Review Tutorial";
                }
            }
        }

        #endregion

        #region Validation

        private bool IsValidSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            // Basic validation - adjust based on your session ID format
            if (sessionId.Length < 8)
                return false;

            // Could add more specific validation here (GUID format, etc.)
            return true;
        }

        #endregion

        #region Visual Feedback

        private IEnumerator AnimateButtonPress(Button button)
        {
            if (button == null) yield break;

            Transform buttonTransform = button.transform;
            Vector3 originalScale = buttonTransform.localScale;
            Vector3 pressedScale = originalScale * 0.95f;

            // Scale down
            float duration = 0.1f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                buttonTransform.localScale = Vector3.Lerp(originalScale, pressedScale, elapsed / duration);
                yield return null;
            }

            // Scale back up
            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                buttonTransform.localScale = Vector3.Lerp(pressedScale, originalScale, elapsed / duration);
                yield return null;
            }

            buttonTransform.localScale = originalScale;
        }

        private void SetElementVisible(CanvasGroup group, bool visible)
        {
            if (group != null)
            {
                group.alpha = visible ? 1f : 0f;
                group.interactable = visible;
                group.blocksRaycasts = visible;
            }
        }

        private void SetElementVisible(GameObject obj, bool visible)
        {
            if (obj != null)
            {
                obj.SetActive(visible);
            }
        }

        #endregion

        #region Interactivity Management

        private void SetAllInteractable(bool interactable)
        {
            SetModeButtonsInteractable(interactable);
            SetSpectatorInputInteractable(interactable);
            SetNavigationInteractable(interactable);
        }

        private void SetModeButtonsInteractable(bool interactable)
        {
            if (playerModeButton != null)
                playerModeButton.interactable = interactable;
            if (spectatorModeButton != null)
                spectatorModeButton.interactable = interactable;
        }

        private void SetSpectatorInputInteractable(bool interactable)
        {
            if (sessionInputField != null)
                sessionInputField.interactable = interactable;
            if (connectButton != null)
                connectButton.interactable = interactable && !string.IsNullOrEmpty(sessionInputField?.text?.Trim());
        }

        private void SetNavigationInteractable(bool interactable)
        {
            if (backButton != null)
                backButton.interactable = interactable;
            if (runTutorialButton != null)
                runTutorialButton.interactable = interactable;
        }

        #endregion

        #region Error Display

        public void ShowError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
                errorText.color = Color.red;
            }

            Debug.LogError($"ModeSelectionUI Error: {message}");

            // Auto-clear error after delay
            StartCoroutine(ClearErrorAfterDelay(4f));
        }

        public void ClearError()
        {
            if (errorText != null)
            {
                errorText.text = "";
            }
        }

        private IEnumerator ClearErrorAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearError();
        }

        #endregion

        #region Connection Feedback

        public void OnConnectionAttemptFailed(string reason)
        {
            Debug.Log($"ModeSelectionUI: Connection attempt failed - {reason}");

            ShowError($"Connection failed: {reason}");
            TransitionToState(SelectionState.EnteringSession);
            SetSpectatorInputInteractable(true);
        }

        public void OnPlayerModeStartFailed(string reason)
        {
            Debug.Log($"ModeSelectionUI: Player mode start failed - {reason}");

            ShowError($"Failed to start player mode: {reason}");
            TransitionToState(SelectionState.ChoosingMode);
            SetAllInteractable(true);
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            // Remove button listeners
            if (playerModeButton != null)
                playerModeButton.onClick.RemoveListener(OnPlayerModeButtonPressed);
            if (spectatorModeButton != null)
                spectatorModeButton.onClick.RemoveListener(OnSpectatorModeButtonPressed);
            if (connectButton != null)
                connectButton.onClick.RemoveListener(OnConnectButtonPressed);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackButtonPressed);
            if (runTutorialButton != null)
                runTutorialButton.onClick.RemoveListener(OnRunTutorialButtonPressed);

            // Remove input field listeners
            if (sessionInputField != null)
            {
                sessionInputField.onValueChanged.RemoveListener(OnSessionInputChanged);
                sessionInputField.onEndEdit.RemoveListener(OnSessionInputEndEdit);
            }

            Debug.Log("ModeSelectionUI: Cleanup completed");
        }

        #endregion
    }
}




























































































