using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LoGa.LudoEngine.Core
{
    public class UIManager : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject modeSelectionPanel;
        [SerializeField] private GameObject playModeUI;
        [SerializeField] private GameObject spectatorModeUI;
        [SerializeField] private GameObject debugPanel;
        [SerializeField] private GameObject mapPanel;

        [Header("Mode Selection UI")]
        [SerializeField] private TMP_InputField sessionInputField;
        [SerializeField] private Button playButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private GameObject errorMessage;

        [Header("Map References")]
        [SerializeField] private RectTransform mapBackground;
        [SerializeField] private RectTransform playerMarker;

        [Header("References")]
        [SerializeField] private GameManager gameManager;

        // Map coordinate boundaries
        [SerializeField] private float northLatitude;
        [SerializeField] private float southLatitude;
        [SerializeField] private float eastLongitude;
        [SerializeField] private float westLongitude;

        [Header("Debug UI")]
        [SerializeField] private TextMeshProUGUI locationText;
        [SerializeField] private TextMeshProUGUI sessionIdText;

        private void Start()
        {
            // Set up button listeners
            playButton.onClick.AddListener(OnPlayButtonClicked);
            joinButton.onClick.AddListener(OnJoinButtonClicked);

            // Initial UI state
            HideAllPanels();
            ShowModeSelection();
        }

        // UI State Management
        public void ShowModeSelection()
        {
            HideAllPanels();
            modeSelectionPanel.SetActive(true);
            sessionInputField.text = "";
            errorMessage.SetActive(false);
            EnableModeSelectionButtons(true);
        }

        public void ShowPlayMode()
        {
            modeSelectionPanel.SetActive(false);
            playModeUI.SetActive(true);
            mapPanel.SetActive(true);
            debugPanel.SetActive(true);

            // Update session ID display
            sessionIdText.text = "Session: " + gameManager.CurrentSessionId;
        }

        public void ShowSpectatorMode()
        {
            modeSelectionPanel.SetActive(false);
            spectatorModeUI.SetActive(true);
            mapPanel.SetActive(true);
            debugPanel.SetActive(true);

            // Update session ID display
            sessionIdText.text = "Spectating: " + gameManager.CurrentSessionId;
        }

        // Button handlers
        private async void OnPlayButtonClicked()
        {
            EnableModeSelectionButtons(false);
            await gameManager.StartPlayerMode();
            ShowPlayMode();
        }

        private async void OnJoinButtonClicked()
        {
            string sessionId = sessionInputField.text.Trim();
            if (string.IsNullOrEmpty(sessionId))
            {
                ShowError("Please enter a session ID");
                return;
            }

            EnableModeSelectionButtons(false);
            await gameManager.StartSpectatorMode(sessionId);
        }

        // Map position calculations
        public Vector2 GetMapPosition(float latitude, float longitude)
        {
            // Convert GPS coordinates to normalized position (0-1)
            float normalizedX = (longitude - westLongitude) / (eastLongitude - westLongitude);
            float normalizedY = (latitude - southLatitude) / (northLatitude - southLatitude);

            // Convert to screen position
            float width = mapBackground.rect.width;
            float height = mapBackground.rect.height;

            return new Vector2(
                (normalizedX * width) - (width / 2),
                (normalizedY * height) - (height / 2)
            );
        }

        // Update player marker on map
        public void UpdateLocationDisplay(float latitude, float longitude)
        {
            // Update map position
            Vector2 position = GetMapPosition(latitude, longitude);
            playerMarker.anchoredPosition = position;

            // Update text display
            if (locationText != null)
            {
                locationText.text = $"Lat: {latitude:F6}\nLon: {longitude:F6}";
            }
        }

        // Error handling
        public void ShowConnectionError()
        {
            ShowError("Failed to connect to session");
            EnableModeSelectionButtons(true);
        }

        public void ShowError(string message)
        {
            errorMessage.SetActive(true);
            errorMessage.GetComponentInChildren<TextMeshProUGUI>().text = message;
        }

        // Helper methods
        private void HideAllPanels()
        {
            modeSelectionPanel.SetActive(false);
            playModeUI.SetActive(false);
            spectatorModeUI.SetActive(false);
            mapPanel.SetActive(false);
            debugPanel.SetActive(false);
        }

        private void EnableModeSelectionButtons(bool enable)
        {
            playButton.interactable = enable;
            joinButton.interactable = enable;
            sessionInputField.interactable = enable;
        }

        private void OnDestroy()
        {
            // Clean up event listeners
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
            joinButton.onClick.RemoveListener(OnJoinButtonClicked);
        }
    }
}