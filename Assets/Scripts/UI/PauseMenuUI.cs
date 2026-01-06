using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Pause Menu UI Component - handles game pause state
    /// Provides Resume, Share, Settings, and Exit functionality
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject pauseMenuPanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button shareButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        [Header("Visual Feedback (Optional)")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Image backgroundDimmer;
        [SerializeField] private TextMeshProUGUI titleText;

        private UIManager uiManager;
        private bool isVisible = false;

        private void Start()
        {
            InitializeUI();
            SetupButtonListeners();
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("PauseMenuUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            // Start hidden
            HidePauseMenu();

            // Set title if available
            if (titleText != null)
                titleText.text = "Game Paused";

            // Setup canvas group if available
            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
        }

        private void SetupButtonListeners()
        {
            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResume);

            if (shareButton != null)
                shareButton.onClick.AddListener(OnShare);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettings);

            if (exitButton != null)
                exitButton.onClick.AddListener(OnExit);
        }

        public void ShowPauseMenu()
        {
            if (isVisible) return;

            Debug.Log("PauseMenuUI: Showing pause menu");
            isVisible = true;

            // Show panel
            if (pauseMenuPanel != null)
                pauseMenuPanel.SetActive(true);

            // Fade in canvas group
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }

            // Dim background
            if (backgroundDimmer != null)
            {
                Color dimColor = backgroundDimmer.color;
                dimColor.a = 0.7f;
                backgroundDimmer.color = dimColor;
                backgroundDimmer.gameObject.SetActive(true);
            }

            // Make sure buttons are interactable
            SetButtonsInteractable(true);
        }

        public void HidePauseMenu()
        {
            if (!isVisible && pauseMenuPanel != null && !pauseMenuPanel.activeSelf)
                return;

            Debug.Log("PauseMenuUI: Hiding pause menu");
            isVisible = false;

            // Hide panel
            if (pauseMenuPanel != null)
                pauseMenuPanel.SetActive(false);

            // Fade out canvas group
            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }

            // Remove dimmer
            if (backgroundDimmer != null)
            {
                backgroundDimmer.gameObject.SetActive(false);
            }
        }

        private void OnResume()
        {
            Debug.Log("PauseMenuUI: Resume button pressed");

            if (uiManager != null)
            {
                uiManager.OnPauseResume();
            }
            else
            {
                Debug.LogError("PauseMenuUI: UIManager reference not set");
            }
        }

        private void OnShare()
        {
            Debug.Log("PauseMenuUI: Share button pressed");

            if (uiManager != null)
            {
                uiManager.OnPauseShare();
            }
            else
            {
                Debug.LogError("PauseMenuUI: UIManager reference not set");
            }
        }

        private void OnSettings()
        {
            Debug.Log("PauseMenuUI: Settings button pressed");

            if (uiManager != null)
            {
                uiManager.OnPauseSettings();
            }
            else
            {
                Debug.LogError("PauseMenuUI: UIManager reference not set");
            }
        }

        private void OnExit()
        {
            Debug.Log("PauseMenuUI: Exit button pressed");

            if (uiManager != null)
            {
                uiManager.OnPauseExit();
            }
            else
            {
                Debug.LogError("PauseMenuUI: UIManager reference not set");
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (resumeButton != null)
                resumeButton.interactable = interactable;
            if (shareButton != null)
                shareButton.interactable = interactable;
            if (settingsButton != null)
                settingsButton.interactable = interactable;
            if (exitButton != null)
                exitButton.interactable = interactable;
        }

        private void OnDestroy()
        {
            if (resumeButton != null)
                resumeButton.onClick.RemoveListener(OnResume);
            if (shareButton != null)
                shareButton.onClick.RemoveListener(OnShare);
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OnSettings);
            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExit);

            Debug.Log("PauseMenuUI: Cleanup completed");
        }
    }
}