using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System.Collections;

namespace LoGa.LudoEngine.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button inventoryButton;
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
            if (inventoryButton != null)
                inventoryButton.onClick.AddListener(OnInventoryButtonClick);
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
                versionInfoText.text = $"Voices of the Boyne v{Application.version}";

            if (requirementsText != null)
            {
#if UNITY_IOS
                requirementsText.text =
                    "Requirements:\n" +
                    "• iPhone or iPad with iOS 12 or later\n" +
                    "• Location services enabled\n" +
                    "• Bluetooth enabled (optional)\n" +
                    "• Headphones recommended\n" +
                    "• Compatible external sensor (optional)";
#elif UNITY_ANDROID
        requirementsText.text =
            "Requirements:\n" +
            "• Android device running Android 7.0 or later\n" +
            "• Location services enabled\n" +
            "• Bluetooth enabled (optional)\n" +
            "• Headphones recommended\n" +
            "• Compatible external sensor (optional)";
#else
        requirementsText.text =
            "Requirements:\n" +
            "• Location services enabled\n" +
            "• Headphones recommended";
#endif
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

        private void OnInventoryButtonClick()
        {
            Debug.Log("Main Menu: Inventory button clicked");
            if (uiManager != null)
            {
                uiManager.OnInventoryButtonPressed();
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
                // Delete ALL PlayerPrefs
                storageService.ResetToDefaults();

                // Track analytics
                var analyticsService = ServiceLocator.GetService<IAnalyticsService>();
                analyticsService?.TrackEvent("game_progress_reset");

                Debug.Log("All progress deleted successfully");

                // CRITICAL: Reload scene to reinitialize from JSON defaults
                StartCoroutine(ReloadSceneAfterReset());
            }
            else
            {
                Debug.LogError("StorageService not available");
            }

            if (confirmationDialog != null)
                confirmationDialog.SetActive(false);
        }

        private IEnumerator ReloadSceneAfterReset()
        {
            Debug.Log("MainMenuUI: Reloading scene after reset...");

            // Show brief feedback
            if (requirementsText != null)
            {
                requirementsText.text = "Progress reset! Reloading...";
                requirementsText.color = Color.green;
            }

            yield return new WaitForSeconds(1.5f);

            // Reload current scene
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
            );
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
            if (inventoryButton != null)
                inventoryButton.onClick.RemoveListener(OnInventoryButtonClick);
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