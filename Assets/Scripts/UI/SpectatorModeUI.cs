using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Spectator Mode UI Component - displays watched player's session info
    /// Shows connection status and player location data
    /// </summary>
    public class SpectatorModeUI : MonoBehaviour
    {
        [Header("Session Information")]
        [SerializeField] private TextMeshProUGUI sessionIdText;
        [SerializeField] private TextMeshProUGUI connectionStatusText;
        [SerializeField] private Image connectionStatusIndicator;

        [Header("Player Information")]
        [SerializeField] private TextMeshProUGUI playerLocationText;
        [SerializeField] private TextMeshProUGUI playerHealthText;
        [SerializeField] private TextMeshProUGUI playerStatusText;
        [SerializeField] private Slider playerHealthBar;

        [Header("Connection Controls")]
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button returnToMenuButton;
        [SerializeField] private Button reconnectButton;

        [Header("Status Colors")]
        [SerializeField] private Color connectedColor = Color.green;
        [SerializeField] private Color disconnectedColor = Color.red;
        [SerializeField] private Color connectingColor = Color.yellow;

        [Header("Instructions")]
        [SerializeField] private TextMeshProUGUI instructionsText;
        [SerializeField] private CanvasGroup instructionsGroup;
        [SerializeField] private Button hideInstructionsButton;
        [SerializeField] private Button showInstructionsButton;

        #region State Management

        public enum ConnectionState
        {
            Connecting,
            Connected,
            Disconnected,
            Reconnecting,
            Error
        }

        private UIManager uiManager;
        private string currentSessionId = "";
        private ConnectionState currentConnectionState = ConnectionState.Connecting;
        private bool instructionsVisible = true;
        private float lastDataReceiveTime;

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
            Debug.Log("SpectatorModeUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            SetupInstructions();
            UpdateConnectionStatus(ConnectionState.Connecting);

            if (playerLocationText != null)
                playerLocationText.text = "Player Location: Waiting for data...";

            if (playerHealthText != null)
                playerHealthText.text = "Player Health: Unknown";

            if (playerStatusText != null)
                playerStatusText.text = "Player Status: Connecting...";
        }

        private void SetupButtonListeners()
        {
            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(OnDisconnectButtonPressed);
            if (returnToMenuButton != null)
                returnToMenuButton.onClick.AddListener(OnReturnToMenuButtonPressed);
            if (reconnectButton != null)
                reconnectButton.onClick.AddListener(OnReconnectButtonPressed);
            if (hideInstructionsButton != null)
                hideInstructionsButton.onClick.AddListener(OnHideInstructionsPressed);
            if (showInstructionsButton != null)
                showInstructionsButton.onClick.AddListener(OnShowInstructionsPressed);
        }

        private void SetupInstructions()
        {
            if (instructionsText != null)
            {
                instructionsText.text = "SPECTATOR MODE:\n\n" +
                                      "👁️ WATCHING:\n" +
                                      "• You are watching another player's journey\n" +
                                      "• See their location and health in real-time\n" +
                                      "• Experience their audio as they do\n\n" +
                                      "🎧 AUDIO:\n" +
                                      "• Wear headphones for best experience\n" +
                                      "• You'll hear what the player hears\n" +
                                      "• Turn your head to explore the audio space\n\n" +
                                      "📍 LOCATION:\n" +
                                      "• Player position shown on map\n" +
                                      "• Location updates in real-time\n" +
                                      "• Connection status displayed above\n\n" +
                                      "🔗 CONNECTION:\n" +
                                      "• Stay connected to see live updates\n" +
                                      "• Reconnect if connection is lost\n" +
                                      "• Disconnect to choose a different session";
            }

            ShowInstructions(true);
        }
        #endregion

        #region Public Interface

        public void UpdateSessionDisplay(string sessionId)
        {
            currentSessionId = sessionId;

            if (sessionIdText != null)
            {
                sessionIdText.text = $"Watching Session: {sessionId}";
            }

            UpdateConnectionStatus(ConnectionState.Connecting);
            lastDataReceiveTime = Time.time;

            Debug.Log($"SpectatorModeUI: Now watching session {sessionId}");
        }

        public void UpdateLocationDisplay(float latitude, float longitude)
        {
            if (playerLocationText != null)
            {
                playerLocationText.text = $"Player Location:\nLat: {latitude:F6}\nLon: {longitude:F6}";
            }

            // Update connection status to connected when receiving data
            if (currentConnectionState != ConnectionState.Connected)
            {
                UpdateConnectionStatus(ConnectionState.Connected);
            }

            lastDataReceiveTime = Time.time;
        }

        public void UpdatePlayerHealth(int currentHealth, int maxHealth)
        {
            if (playerHealthText != null)
            {
                playerHealthText.text = $"Player Health: {currentHealth}/{maxHealth}";

                // Color code health
                float healthPercentage = (float)currentHealth / maxHealth;
                if (healthPercentage > 0.6f)
                    playerHealthText.color = Color.green;
                else if (healthPercentage > 0.3f)
                    playerHealthText.color = Color.yellow;
                else
                    playerHealthText.color = Color.red;
            }

            if (playerHealthBar != null)
            {
                playerHealthBar.maxValue = maxHealth;
                playerHealthBar.value = currentHealth;

                // Update health bar color
                var fillImage = playerHealthBar.fillRect.GetComponent<Image>();
                if (fillImage != null)
                {
                    float healthPercentage = (float)currentHealth / maxHealth;
                    fillImage.color = Color.Lerp(Color.red, Color.green, healthPercentage);
                }
            }

            lastDataReceiveTime = Time.time;
        }

        public void UpdatePlayerStatus(string status)
        {
            if (playerStatusText != null)
            {
                playerStatusText.text = $"Player Status: {status}";
            }

            lastDataReceiveTime = Time.time;
        }

        public void UpdatePlayerStatus(GameManager.GameplayState gameplayState)
        {
            string statusMessage = gameplayState switch
            {
                GameManager.GameplayState.Wander => "Exploring battlefield",
                GameManager.GameplayState.Interact => "At Point of Interest",
                GameManager.GameplayState.Combat => "⚔️ In Combat ⚔️",
                GameManager.GameplayState.Recovery => "🍓 Collecting berries",
                GameManager.GameplayState.Paused => "Game Paused",
                _ => "Active"
            };

            UpdatePlayerStatus(statusMessage);
        }

        public void UpdateConnectionStatus(ConnectionState newState)
        {
            currentConnectionState = newState;

            string statusText = "";
            Color indicatorColor = connectingColor;

            switch (newState)
            {
                case ConnectionState.Connecting:
                    statusText = "Connecting...";
                    indicatorColor = connectingColor;
                    break;
                case ConnectionState.Connected:
                    statusText = "Connected";
                    indicatorColor = connectedColor;
                    break;
                case ConnectionState.Disconnected:
                    statusText = "Disconnected";
                    indicatorColor = disconnectedColor;
                    break;
                case ConnectionState.Reconnecting:
                    statusText = "Reconnecting...";
                    indicatorColor = connectingColor;
                    break;
                case ConnectionState.Error:
                    statusText = "Connection Error";
                    indicatorColor = disconnectedColor;
                    break;
            }

            if (connectionStatusText != null)
            {
                connectionStatusText.text = statusText;
                connectionStatusText.color = indicatorColor;
            }

            if (connectionStatusIndicator != null)
            {
                connectionStatusIndicator.color = indicatorColor;
            }

            UpdateButtonVisibility();
            Debug.Log($"SpectatorModeUI: Connection status updated to {newState}");
        }

        public void OnConnectionLost(string reason)
        {
            Debug.LogWarning($"SpectatorModeUI: Connection lost - {reason}");

            UpdateConnectionStatus(ConnectionState.Disconnected);

            if (playerStatusText != null)
            {
                playerStatusText.text = $"Connection lost: {reason}";
                playerStatusText.color = disconnectedColor;
            }
        }

        #endregion

        #region Button Event Handlers

        private void OnDisconnectButtonPressed()
        {
            Debug.Log("SpectatorModeUI: Disconnect button pressed");

            StartCoroutine(AnimateButtonPress(disconnectButton));

            // Update UI immediately
            UpdateConnectionStatus(ConnectionState.Disconnected);

            // Report to UIManager to handle actual disconnection
            if (uiManager != null)
            {
                uiManager.OnBackButtonPressed(); // This will take us back to mode selection
            }
            else
            {
                Debug.LogError("SpectatorModeUI: UIManager reference not set");
            }
        }

        private void OnReturnToMenuButtonPressed()
        {
            Debug.Log("SpectatorModeUI: Return to menu button pressed");

            StartCoroutine(AnimateButtonPress(returnToMenuButton));

            // Report to UIManager
            if (uiManager != null)
            {
                uiManager.OnExitToMainMenu();
            }
            else
            {
                Debug.LogError("SpectatorModeUI: UIManager reference not set");
            }
        }

        private void OnReconnectButtonPressed()
        {
            Debug.Log("SpectatorModeUI: Reconnect button pressed");

            StartCoroutine(AnimateButtonPress(reconnectButton));

            if (string.IsNullOrEmpty(currentSessionId))
            {
                Debug.LogError("SpectatorModeUI: No session ID to reconnect to");
                return;
            }

            UpdateConnectionStatus(ConnectionState.Reconnecting);

            // Report reconnection request to UIManager
            if (uiManager != null)
            {
                uiManager.OnSpectatorModeSelected(currentSessionId);
            }
            else
            {
                Debug.LogError("SpectatorModeUI: UIManager reference not set");
            }
        }

        private void OnHideInstructionsPressed()
        {
            Debug.Log("SpectatorModeUI: Hide instructions pressed");
            ShowInstructions(false);
        }

        private void OnShowInstructionsPressed()
        {
            Debug.Log("SpectatorModeUI: Show instructions pressed");
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

        #region UI State Management

        private void UpdateButtonVisibility()
        {
            bool isConnected = (currentConnectionState == ConnectionState.Connected);
            bool isDisconnected = (currentConnectionState == ConnectionState.Disconnected ||
                                  currentConnectionState == ConnectionState.Error);
            bool isConnecting = (currentConnectionState == ConnectionState.Connecting ||
                               currentConnectionState == ConnectionState.Reconnecting);

            if (disconnectButton != null)
                disconnectButton.interactable = isConnected;

            if (reconnectButton != null)
            {
                reconnectButton.gameObject.SetActive(isDisconnected);
                reconnectButton.interactable = !string.IsNullOrEmpty(currentSessionId);
            }

            if (returnToMenuButton != null)
                returnToMenuButton.interactable = !isConnecting;
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

        #region Connection Monitoring

        private void Update()
        {
            // Monitor connection health based on data receive time
            if (currentConnectionState == ConnectionState.Connected)
            {
                float timeSinceLastData = Time.time - lastDataReceiveTime;

                // If no data for 10 seconds, consider connection lost
                if (timeSinceLastData > 10f)
                {
                    OnConnectionLost("No data received");
                }
                // Warning if no data for 5 seconds
                else if (timeSinceLastData > 5f && connectionStatusText != null)
                {
                    connectionStatusText.text = "Connection unstable...";
                    connectionStatusText.color = connectingColor;
                }
            }

            // Update displays with GameManager data if available
            UpdateDisplaysFromGameManager();
        }

        private void UpdateDisplaysFromGameManager()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsSpectatorMode)
                return;

            // Update spectator location display
            var location = GameManager.Instance.SpectatorLocation;
            var heading = GameManager.Instance.SpectatorHeading;
            var isReceiving = GameManager.Instance.IsReceivingSpectatorData;

            if (isReceiving && (location.x != 0 || location.y != 0))
            {
                UpdateLocationDisplay(location.x, location.y);
            }
        }

        #endregion

        #region Data Reception Feedback

        public void OnDataReceived()
        {
            lastDataReceiveTime = Time.time;

            if (currentConnectionState != ConnectionState.Connected)
            {
                UpdateConnectionStatus(ConnectionState.Connected);
            }
        }

        public void OnPlayerPOIsUpdated(System.Collections.Generic.List<string> poiIds)
        {
            // Update UI to show player's discovered POIs
            Debug.Log($"SpectatorModeUI: Player has {poiIds.Count} unlocked POIs");
            lastDataReceiveTime = Time.time;
        }

        #endregion

        #region Error Handling

        public void ShowConnectionError(string errorMessage)
        {
            Debug.LogError($"SpectatorModeUI: Connection error - {errorMessage}");

            UpdateConnectionStatus(ConnectionState.Error);

            if (playerStatusText != null)
            {
                playerStatusText.text = $"Error: {errorMessage}";
                playerStatusText.color = disconnectedColor;
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            // Remove button listeners
            if (disconnectButton != null)
                disconnectButton.onClick.RemoveListener(OnDisconnectButtonPressed);
            if (returnToMenuButton != null)
                returnToMenuButton.onClick.RemoveListener(OnReturnToMenuButtonPressed);
            if (reconnectButton != null)
                reconnectButton.onClick.RemoveListener(OnReconnectButtonPressed);
            if (hideInstructionsButton != null)
                hideInstructionsButton.onClick.RemoveListener(OnHideInstructionsPressed);
            if (showInstructionsButton != null)
                showInstructionsButton.onClick.RemoveListener(OnShowInstructionsPressed);

            Debug.Log("SpectatorModeUI: Cleanup completed");
        }

        #endregion
    }
}