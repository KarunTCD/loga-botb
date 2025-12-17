using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button feedbackButton;
        [SerializeField] private Button resetProgressButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI requirementsText;
        [SerializeField] private TextMeshProUGUI versionInfoText;

        [Header("Reset Confirmation")]
        [SerializeField] private GameObject confirmationDialog;
        [SerializeField] private TextMeshProUGUI confirmationText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        private UIManager uiManager;

        private void Start()
        {
            SetupButtonListeners();
            UpdateUI();

            if (confirmationDialog != null)
                confirmationDialog.SetActive(false);
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("MainMenuUI: UIManager reference set");
        }

        private void SetupButtonListeners()
        {
            if (playButton != null)
                playButton.onClick.AddListener(OnPlayButtonClick);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettingsButtonClick);
            if (feedbackButton != null)
                feedbackButton.onClick.AddListener(OnFeedbackButtonClick);
            if (resetProgressButton != null)
                resetProgressButton.onClick.AddListener(OnResetProgressButtonClick);
            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitButtonClick);

            if (yesButton != null)
                yesButton.onClick.AddListener(OnYesClick);
            if (noButton != null)
                noButton.onClick.AddListener(OnNoClick);
        }

        private void UpdateUI()
        {
            if (versionInfoText != null)
                versionInfoText.text = $"Battle of the Boyne v{Application.version}\nUnity {Application.unityVersion}";

            if (requirementsText != null)
            {
                requirementsText.text = "Hardware Requirements:\n" +
                                      "• Android 7.0+ or iOS 12.0+\n" +
                                      "• Bluetooth LE support\n" +
                                      "• GPS/Location services\n" +
                                      "• Headphones recommended\n" +
                                      "• MMRL device (optional)";
            }
        }

        private void OnPlayButtonClick()
        {
            Debug.Log("Main Menu: Play button clicked");
            if (uiManager != null)
            {
                uiManager.OnPlayButtonPressed();
            }
            else
            {
                Debug.LogError("UIManager reference not set in MainMenuUI");
            }
        }

        private void OnSettingsButtonClick()
        {
            Debug.Log("Main Menu: Settings button clicked");
            if (uiManager != null)
            {
                uiManager.OnSettingsButtonPressed();
            }
            else
            {
                Debug.LogError("UIManager reference not set in MainMenuUI");
            }
        }

        private void OnFeedbackButtonClick()
        {
            Debug.Log("Main Menu: Feedback button clicked");
            if (uiManager != null)
            {
                uiManager.OnFeedbackButtonPressed();
            }
            else
            {
                Debug.LogError("UIManager reference not set in MainMenuUI");
            }
        }

        private void OnResetProgressButtonClick()
        {
            Debug.Log("Main Menu: Reset Progress button clicked");
            if (confirmationDialog != null)
            {
                confirmationDialog.SetActive(true);

                if (confirmationText != null)
                {
                    confirmationText.text = "Delete all progress?\n\nThis cannot be undone.";
                }
            }
        }

        private void OnYesClick()
        {
            Debug.Log("Main Menu: Reset confirmed - deleting all progress");

            var storageService = ServiceLocator.GetService<IStorageService>();

            if (storageService != null)
            {
                storageService.DeleteAll();

                var analyticsService = ServiceLocator.GetService<IAnalyticsService>();
                analyticsService?.TrackEvent("game_progress_reset");

                Debug.Log("All progress deleted successfully");
            }
            else
            {
                Debug.LogError("StorageService not available");
            }

            if (confirmationDialog != null)
                confirmationDialog.SetActive(false);
        }

        private void OnNoClick()
        {
            Debug.Log("Main Menu: Reset cancelled");
            if (confirmationDialog != null)
                confirmationDialog.SetActive(false);
        }

        private void OnExitButtonClick()
        {
            Debug.Log("Main Menu: Exit button clicked");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (playButton != null)
                playButton.onClick.RemoveListener(OnPlayButtonClick);
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OnSettingsButtonClick);
            if (feedbackButton != null)
                feedbackButton.onClick.RemoveListener(OnFeedbackButtonClick);
            if (resetProgressButton != null)
                resetProgressButton.onClick.RemoveListener(OnResetProgressButtonClick);
            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitButtonClick);

            if (yesButton != null)
                yesButton.onClick.RemoveListener(OnYesClick);
            if (noButton != null)
                noButton.onClick.RemoveListener(OnNoClick);
        }
    }
}