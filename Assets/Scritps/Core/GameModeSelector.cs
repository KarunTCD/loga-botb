using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Handles game mode selection UI (Player vs Spectator)
    /// Final step before entering gameplay
    /// </summary>
    public class GameModeSelector : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button playerModeButton;
        [SerializeField] private Button spectatorModeButton;
        [SerializeField] private InputField sessionInputField;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private GameObject spectatorInputContainer;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button backButton;

        [Header("Tutorial Access")]
        [SerializeField] private Button runTutorialButton;
        [SerializeField] private Text tutorialPromptText;

        private bool showingSpectatorInput = false;

        private void Awake()
        {
            // Setup button handlers
            if (playerModeButton != null)
                playerModeButton.onClick.AddListener(OnPlayerModeSelected);

            if (spectatorModeButton != null)
                spectatorModeButton.onClick.AddListener(OnSpectatorModeSelected);

            if (connectButton != null)
                connectButton.onClick.AddListener(OnConnectToSession);

            if (backButton != null)
                backButton.onClick.AddListener(OnBackPressed);

            if (runTutorialButton != null)
                runTutorialButton.onClick.AddListener(OnRunTutorialPressed);

            // Hide spectator input initially
            if (spectatorInputContainer != null)
                spectatorInputContainer.SetActive(false);
        }

        private void OnEnable()
        {
            // Reset UI state when panel becomes active
            ResetUI();

            // Show tutorial prompt for first-time users
            ShowTutorialPromptIfNeeded();
        }

        private void ResetUI()
        {
            showingSpectatorInput = false;

            if (spectatorInputContainer != null)
                spectatorInputContainer.SetActive(false);

            if (playerModeButton != null)
                playerModeButton.gameObject.SetActive(true);

            if (spectatorModeButton != null)
                spectatorModeButton.gameObject.SetActive(true);

            if (statusText != null)
                statusText.text = "Hardware setup complete! Choose your game mode:";
        }

        private void ShowTutorialPromptIfNeeded()
        {
            bool hasCompletedTutorial = PlayerPrefs.HasKey("TutorialCompleted");

            if (tutorialPromptText != null)
            {
                if (hasCompletedTutorial)
                {
                    tutorialPromptText.text = "Want to run the tutorial again?";
                }
                else
                {
                    tutorialPromptText.text = "First time? The tutorial was automatically shown.";
                }
            }

            if (runTutorialButton != null)
            {
                runTutorialButton.gameObject.SetActive(hasCompletedTutorial);
                var buttonText = runTutorialButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                    buttonText.text = "Run Tutorial Again";
            }
        }

        // -----------------------------------------------
        // Button Handlers
        // -----------------------------------------------

        private void OnPlayerModeSelected()
        {
            Debug.Log("Player mode selected");

            if (statusText != null)
                statusText.text = "Starting player mode...";

            // Disable buttons to prevent multiple clicks
            SetButtonsInteractable(false);

            // Notify GameManager
            //if (GameManager.Instance != null)
            //{
            //    GameManager.Instance.OnPlayerModeSelected();
            //}
        }

        private void OnSpectatorModeSelected()
        {
            Debug.Log("Spectator mode selected");

            showingSpectatorInput = true;

            // Hide mode selection buttons
            if (playerModeButton != null)
                playerModeButton.gameObject.SetActive(false);

            if (spectatorModeButton != null)
                spectatorModeButton.gameObject.SetActive(false);

            // Show spectator input
            if (spectatorInputContainer != null)
                spectatorInputContainer.SetActive(true);

            if (statusText != null)
                statusText.text = "Enter the session ID to watch:";

            // Focus on input field
            if (sessionInputField != null)
                sessionInputField.ActivateInputField();
        }

        private void OnConnectToSession()
        {
            if (sessionInputField == null) return;

            string sessionId = sessionInputField.text.Trim();

            if (string.IsNullOrEmpty(sessionId))
            {
                if (statusText != null)
                    statusText.text = "❌ Please enter a valid session ID";
                return;
            }

            Debug.Log($"Connecting to spectator session: {sessionId}");

            if (statusText != null)
                statusText.text = "Connecting to session...";

            // Disable input while connecting
            SetSpectatorInputInteractable(false);

            // Notify GameManager
            if (GameManager.Instance != null)
            {
                StartCoroutine(ConnectToSpectatorSession(sessionId));
            }
        }

        private System.Collections.IEnumerator ConnectToSpectatorSession(string sessionId)
        {
            var task = GameManager.Instance.StartSpectatorMode(sessionId);
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Result)
            {
                // Successfully connected
                Debug.Log("Spectator mode started successfully");

                if (statusText != null)
                    statusText.text = "✅ Connected! Entering spectator mode...";
            }
            else
            {
                // Failed to connect
                Debug.LogError("Failed to start spectator mode");

                if (statusText != null)
                    statusText.text = "❌ Failed to connect. Check session ID and try again.";

                // Re-enable input
                SetSpectatorInputInteractable(true);
            }
        }

        private void OnBackPressed()
        {
            if (showingSpectatorInput)
            {
                // Go back to mode selection
                ResetUI();
            }
            else
            {
                // Go back to hardware setup or main menu
                //if (GameManager.Instance != null)
                //{
                //    GameManager.Instance.TransitionToAppState(GameManager.AppState.HardwareSetup);
                //}
            }
        }

        private void OnRunTutorialPressed()
        {
            Debug.Log("Running tutorial again");

            // Notify GameManager to start tutorial
            //if (GameManager.Instance != null)
            //{
            //    GameManager.Instance.TransitionToAppState(GameManager.AppState.Tutorial);
            //}
        }

        // -----------------------------------------------
        // UI State Management
        // -----------------------------------------------

        private void SetButtonsInteractable(bool interactable)
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
                connectButton.interactable = interactable;
        }

        // -----------------------------------------------
        // Public Methods (called by GameManager)
        // -----------------------------------------------

        public void OnPlayerModeStarted(bool success)
        {
            if (success)
            {
                if (statusText != null)
                    statusText.text = "✅ Player mode started! Loading game...";
            }
            else
            {
                if (statusText != null)
                    statusText.text = "❌ Failed to start player mode. Try again.";

                // Re-enable buttons
                SetButtonsInteractable(true);
            }
        }

        public void OnSpectatorModeStarted(bool success)
        {
            if (success)
            {
                if (statusText != null)
                    statusText.text = "✅ Spectator mode active! Watching session...";
            }
            else
            {
                if (statusText != null)
                    statusText.text = "❌ Failed to connect to session.";

                SetSpectatorInputInteractable(true);
            }
        }
    }
}