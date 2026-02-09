using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Core;
namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Play Mode UI Component - displays player session info and controls
    /// Provides session sharing and exit functionality
    /// </summary>
    public class PlayModeUI : MonoBehaviour
    {
        [Header("Session Information")]
        [SerializeField] private TextMeshProUGUI sessionIdText;
        [SerializeField] private Button copySessionButton;
        [SerializeField] private TextMeshProUGUI copyFeedbackText;
        [Header("Game Controls")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button exitButton;

        [Header("Status Display")]
        [SerializeField] private TextMeshProUGUI gameStatusText;
        [SerializeField] private TextMeshProUGUI healthStatusText;
        [SerializeField] private Slider healthBar;

        [Header("Instructions")]
        [SerializeField] private TextMeshProUGUI instructionsText;
        [SerializeField] private CanvasGroup instructionsGroup;
        [SerializeField] private Button hideInstructionsButton;
        [SerializeField] private Button showInstructionsButton;

        [Header("Visual Elements")]
        [SerializeField] private CanvasGroup mainUIGroup;
        [SerializeField] private Color healthyColor = Color.green;
        [SerializeField] private Color damagedColor = Color.red;

        #region State Management

        private UIManager uiManager;
        private string currentSessionId = "";
        private bool isGamePaused = false;
        private bool instructionsVisible = true;
        private Coroutine copyFeedbackCoroutine;

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
            Debug.Log("PlayModeUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            SetupInstructions();
            ClearCopyFeedback();
            UpdateHealthDisplay(3, 3); // Default full health

            if (gameStatusText != null)
                gameStatusText.text = "Game Active";

            if (mainUIGroup != null)
                mainUIGroup.alpha = 1f;
        }

        private void SetupButtonListeners()
        {
            if (copySessionButton != null)
                copySessionButton.onClick.AddListener(OnCopySessionButtonPressed);
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPauseButtonPressed);
            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitButtonPressed);
            if (hideInstructionsButton != null)
                hideInstructionsButton.onClick.AddListener(OnHideInstructionsPressed);
            if (showInstructionsButton != null)
                showInstructionsButton.onClick.AddListener(OnShowInstructionsPressed);
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPauseButtonPressed);
        }

        private void SetupInstructions()
        {
            if (instructionsText != null)
            {
                instructionsText.text = "GAME INSTRUCTIONS:\n\n" +
                                      "🚶 EXPLORATION:\n" +
                                      "• Walk around to discover Points of Interest\n" +
                                      "• Listen for spatial audio cues\n" +
                                      "• Get close to POIs to hear historical accounts\n\n" +
                                      "COMBAT:\n" +
                                      "• Face attacking mercenaries to block\n" +
                                      "• Listen for footsteps and attack sounds\n" +
                                      "• Quick head turns are key to survival\n\n" +
                                      " HEALTH:\n" +
                                      "• Collect berries to restore health\n" +
                                      "• Follow the berry audio to find them\n" +
                                      "• Stay healthy to continue exploring\n\n" +
                                      "SHARING:\n" +
                                      "• Share your Session ID for others to watch\n" +
                                      "• Spectators see your journey in real-time";
            }

            ShowInstructions(true);
        }

        #endregion

        #region Public Interface

        public void UpdateSessionId(string sessionId)
        {
            currentSessionId = sessionId;

            if (sessionIdText != null)
            {
                sessionIdText.text = $"Session ID: {sessionId}";
            }

            Debug.Log($"PlayModeUI: Session ID updated to {sessionId}");
        }

        public void UpdateHealthDisplay(int currentHealth, int maxHealth)
        {
            // Update health text
            if (healthStatusText != null)
            {
                healthStatusText.text = $"Health: {currentHealth}/{maxHealth}";

                // Change color based on health level
                float healthPercentage = (float)currentHealth / maxHealth;
                if (healthPercentage > 0.6f)
                    healthStatusText.color = healthyColor;
                else if (healthPercentage > 0.3f)
                    healthStatusText.color = Color.yellow;
                else
                    healthStatusText.color = damagedColor;
            }

            // Update health bar
            if (healthBar != null)
            {
                healthBar.maxValue = maxHealth;
                healthBar.value = currentHealth;

                // Update health bar color
                var fillImage = healthBar.fillRect.GetComponent<Image>();
                if (fillImage != null)
                {
                    float healthPercentage = (float)currentHealth / maxHealth;
                    fillImage.color = Color.Lerp(damagedColor, healthyColor, healthPercentage);
                }
            }
        }

        public void UpdateGameStatus(string status)
        {
            if (gameStatusText != null)
            {
                gameStatusText.text = status;
            }
        }

        public void UpdateGameStatus(GameManager.GameplayState gameplayState)
        {
            string statusMessage = gameplayState switch
            {
                GameManager.GameplayState.Wander => "Exploring the battlefield",
                GameManager.GameplayState.Interact => "At Point of Interest",
                GameManager.GameplayState.Combat => "⚔ COMBAT ACTIVE ",
                GameManager.GameplayState.Recovery => "Collecting berries",
                GameManager.GameplayState.Paused => "Game Paused",
                _ => "Game Active"
            };

            UpdateGameStatus(statusMessage);
        }

        #endregion

        #region Button Event Handlers

        private void OnCopySessionButtonPressed()
        {
            Debug.Log("PlayModeUI: Copy session button pressed");

            if (string.IsNullOrEmpty(currentSessionId))
            {
                ShowCopyFeedback("No session ID available", false);
                return;
            }

            // Copy to system clipboard
            GUIUtility.systemCopyBuffer = currentSessionId;

            ShowCopyFeedback("Session ID copied to clipboard!", true);
            StartCoroutine(AnimateButtonPress(copySessionButton));

            Debug.Log($"PlayModeUI: Copied session ID '{currentSessionId}' to clipboard");
        }

        private void OnPauseButtonPressed()
        {
            Debug.Log("PlayModeUI: Pause button pressed");

            if (uiManager != null)
            {
                uiManager.ShowPauseMenu();
            }
            else
            {
                Debug.LogError("PlayModeUI: UIManager reference not set");
            }
        }


        private void OnExitButtonPressed()
        {
            Debug.Log("PlayModeUI: Exit button pressed");

            StartCoroutine(AnimateButtonPress(exitButton));

            // Show confirmation or exit immediately
            StartCoroutine(HandleExitRequest());
        }

        private void OnHideInstructionsPressed()
        {
            Debug.Log("PlayModeUI: Hide instructions pressed");
            ShowInstructions(false);
        }

        private void OnShowInstructionsPressed()
        {
            Debug.Log("PlayModeUI: Show instructions pressed");
            ShowInstructions(true);
        }

        #endregion

        #region Instructions Management

        private void ShowInstructions(bool show)
        {
            instructionsVisible = show;

            if (instructionsGroup != null)
            {
                StartCoroutine(AnimateInstructions(show));
            }

            if (hideInstructionsButton != null)
                hideInstructionsButton.gameObject.SetActive(show);
            if (showInstructionsButton != null)
                showInstructionsButton.gameObject.SetActive(!show);
        }

        private IEnumerator AnimateInstructions(bool show)
        {
            float duration = 0.3f;
            float startAlpha = instructionsGroup.alpha;
            float targetAlpha = show ? 1f : 0f;

            instructionsGroup.interactable = show;
            instructionsGroup.blocksRaycasts = show;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                instructionsGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }

            instructionsGroup.alpha = targetAlpha;
        }

        #endregion

        #region Copy Feedback

        private void ShowCopyFeedback(string message, bool isSuccess)
        {
            if (copyFeedbackText != null)
            {
                copyFeedbackText.text = message;
                copyFeedbackText.color = isSuccess ? Color.green : Color.red;

                // Stop any existing feedback coroutine
                if (copyFeedbackCoroutine != null)
                {
                    StopCoroutine(copyFeedbackCoroutine);
                }

                copyFeedbackCoroutine = StartCoroutine(ClearCopyFeedbackAfterDelay(3f));
            }
        }

        private IEnumerator ClearCopyFeedbackAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearCopyFeedback();
        }

        private void ClearCopyFeedback()
        {
            if (copyFeedbackText != null)
            {
                copyFeedbackText.text = "";
            }
        }

        #endregion

        #region Exit Handling

        private IEnumerator HandleExitRequest()
        {
            // Could show confirmation dialog here
            // For now, just add a small delay and then exit

            UpdateGameStatus("Exiting game...");

            yield return new WaitForSeconds(0.5f);

            // Report exit request to UIManager
            if (uiManager != null)
            {
                uiManager.OnExitToMainMenu();
            }
            else
            {
                Debug.LogError("PlayModeUI: UIManager reference not set");
            }
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

        #endregion

        #region Health Updates (Called by GameManager via UIManager)

        public void OnPlayerHealthChanged(int newHealth, int maxHealth)
        {
            UpdateHealthDisplay(newHealth, maxHealth);

            // Provide visual feedback for health changes
            if (newHealth < maxHealth)
            {
                StartCoroutine(FlashHealthWarning());
            }
        }

        private IEnumerator FlashHealthWarning()
        {
            if (healthStatusText != null)
            {
                Color originalColor = healthStatusText.color;

                // Flash red briefly
                healthStatusText.color = Color.red;
                yield return new WaitForSeconds(0.2f);
                healthStatusText.color = originalColor;
                yield return new WaitForSeconds(0.1f);
                healthStatusText.color = Color.red;
                yield return new WaitForSeconds(0.2f);
                healthStatusText.color = originalColor;
            }
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            // Update health display from GameManager if available
            if (GameManager.Instance != null)
            {
                int currentHealth = GameManager.Instance.PlayerHealth;
                UpdateHealthDisplay(currentHealth, 3); // Max health is 3

                var gameplayState = GameManager.Instance.CurrentGameplayState;
                UpdateGameStatus(gameplayState);
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            // Stop any running coroutines
            if (copyFeedbackCoroutine != null)
            {
                StopCoroutine(copyFeedbackCoroutine);
            }

            // Remove button listeners
            if (copySessionButton != null)
                copySessionButton.onClick.RemoveListener(OnCopySessionButtonPressed);
            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(OnPauseButtonPressed);
            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitButtonPressed);
            if (hideInstructionsButton != null)
                hideInstructionsButton.onClick.RemoveListener(OnHideInstructionsPressed);
            if (showInstructionsButton != null)
                showInstructionsButton.onClick.RemoveListener(OnShowInstructionsPressed);

            Debug.Log("PlayModeUI: Cleanup completed");
        }

        #endregion
    }
}