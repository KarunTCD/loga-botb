using UnityEngine;
using UnityEngine.UI;
using LoGa.LudoEngine.Core;
using TMPro;

namespace LoGa.LudoEngine.UI
{
    public class SpectatorModeUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TextMeshProUGUI spectatingText;
        [SerializeField] private TextMeshProUGUI locationText;
        [SerializeField] private Button disconnectButton;

        [Header("References")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private UIManager uiManager;

        private void Start()
        {
            SetupButtonListeners();
            UpdateSessionDisplay();
        }

        private void SetupButtonListeners()
        {
            if (disconnectButton != null)
            {
                disconnectButton.onClick.AddListener(OnDisconnectButtonClick);
            }
        }

        private void UpdateSessionDisplay()
        {
            if (spectatingText != null && gameManager != null)
            {
                spectatingText.text = $"Spectating: {gameManager.CurrentSessionId}";
            }
        }

        private void OnDisconnectButtonClick()
        {
            // Disconnect from session
            if (gameManager != null)
            {
                gameManager.ExitSpectatorMode();
            }

            // Return to mode selection
            if (uiManager != null)
            {
                uiManager.ShowModeSelection();
            }
        }

        public void UpdateLocationDisplay(float latitude, float longitude)
        {
            if (locationText != null)
            {
                locationText.text = $"Lat: {latitude:F6}\nLon: {longitude:F6}";
            }
        }

        public void UpdateDiscoveredPOIs(int count, int total)
        {
            // Could update a counter or progress bar showing discovered POIs
        }

        private void OnDestroy()
        {
            if (disconnectButton != null)
                disconnectButton.onClick.RemoveListener(OnDisconnectButtonClick);
        }
    }
}