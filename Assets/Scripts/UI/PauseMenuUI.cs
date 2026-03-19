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

        public bool IsVisible => pauseMenuPanel != null && pauseMenuPanel.activeSelf;

        private void Start()
        {
            SetupButtonListeners();
            
            if (titleText != null)
                titleText.text = "Game Paused";
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
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

        public bool Show()
        {
            if (pauseMenuPanel == null)
            {
                Debug.LogError("PauseMenuUI: pauseMenuPanel not assigned - cannot show!");
                return false;
            }

            if (pauseMenuPanel.activeSelf)
                return true;

            pauseMenuPanel.SetActive(true);

            if (!pauseMenuPanel.activeSelf)
            {
                Debug.LogError("PauseMenuUI: Failed to activate pause menu panel!");
                return false;
            }

            pauseMenuPanel.transform.SetAsLastSibling();

            if (panelGroup == null)
            {
                panelGroup = pauseMenuPanel.GetComponent<CanvasGroup>();
                
                if (panelGroup == null)
                {
                    panelGroup = pauseMenuPanel.AddComponent<CanvasGroup>();
                }
            }

            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }

            if (backgroundDimmer != null)
            {
                Color dimColor = backgroundDimmer.color;
                dimColor.a = 0.7f;
                backgroundDimmer.color = dimColor;
                backgroundDimmer.gameObject.SetActive(true);
            }

            SetButtonsInteractable(true);

            return true;
        }

        public void Hide()
        {
            if (pauseMenuPanel == null)
                return;

            if (!pauseMenuPanel.activeSelf)
                return;

            pauseMenuPanel.SetActive(false);

            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }

            if (backgroundDimmer != null)
            {
                backgroundDimmer.gameObject.SetActive(false);
            }
        }

        private void OnResume()
        {
            if (uiManager != null)
            {
                uiManager.OnPauseResume();
            }
        }

        private void OnShare()
        {
            if (uiManager != null)
            {
                uiManager.OnPauseShare();
            }
        }

        private void OnSettings()
        {
            if (uiManager != null)
            {
                uiManager.OnPauseSettings();
            }
        }

        private void OnExit()
        {
            if (uiManager != null)
            {
                uiManager.OnPauseExit();
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
        }
    }
}