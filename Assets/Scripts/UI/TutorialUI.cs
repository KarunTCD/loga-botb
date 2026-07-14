using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Minimal UI for gameplay tutorial
    /// Shows status text and back button
    /// </summary>
    public class TutorialUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI providerText;
        [SerializeField] private Button backButton;

        private UIManager uiManager;
        private IHeadTrackingService headTrackingService;

        private void Start()
        {
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
            
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackButtonPressed);
            }
        }

        private void Update()
        {
            UpdateProviderDisplay();
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("TutorialUI: UIManager reference set");
        }

        /// <summary>
        /// Show minimal tutorial UI - just status text
        /// </summary>
        public void ShowTutorialInProgress()
        {
            Debug.Log("TutorialUI: Showing tutorial in progress");

            if (titleText != null)
            {
                titleText.text = "Tutorial";
            }

            if (statusText != null)
            {
                statusText.text = "Follow the audio guidance to learn the basics of navigation.";
            }

            if (backButton != null)
            {
                backButton.gameObject.SetActive(true);
            }
        }

        private void UpdateProviderDisplay()
        {
            if (providerText == null || headTrackingService == null) return;

            string provider = headTrackingService.ActiveProviderName ?? "Phone Sensor";

            string deviceName = provider switch
            {
                "AirPodsHeadTrackingProvider" => "AirPods",
                "MMRLHeadTrackingProvider"    => "MMRL Device",
                "PhoneOrientationProvider"    => "Phone Sensor",
                _                             => provider
            };

            providerText.text = $"Tracking: {deviceName}";
        }

        /// <summary>
        /// Update status text (optional - can be called by TutorialManager)
        /// </summary>
        public void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void OnBackButtonPressed()
        {
            Debug.Log("TutorialUI: Back button pressed - exiting tutorial");

            if (uiManager != null)
            {
                uiManager.OnBackButtonPressed();
            }
        }

        private void OnDestroy()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackButtonPressed);
            }
        }
    }
}