using UnityEngine;
using UnityEngine.UI;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject pauseMenuPanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button shareButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        private UIManager uiManager;

        private void Start()
        {
            if (pauseMenuPanel != null)
                pauseMenuPanel.SetActive(false);

            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResume);

            if (shareButton != null)
                shareButton.onClick.AddListener(OnShare);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettings);

            if (exitButton != null)
                exitButton.onClick.AddListener(OnExit);
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("PauseMenuUI: UIManager reference set");
        }

        public void ShowPauseMenu()
        {
            if (pauseMenuPanel != null)
                pauseMenuPanel.SetActive(true);
        }

        public void HidePauseMenu()
        {
            if (pauseMenuPanel != null)
                pauseMenuPanel.SetActive(false);
        }

        private void OnResume()
        {
            uiManager?.OnPauseResume();
        }

        private void OnShare()
        {
            uiManager?.OnPauseShare();
        }

        private void OnSettings()
        {
            uiManager?.OnPauseSettings();
        }

        private void OnExit()
        {
            uiManager?.OnPauseExit();
        }

        private void OnDestroy()
        {
            if (resumeButton != null)
                resumeButton.onClick.RemoveAllListeners();
            if (shareButton != null)
                shareButton.onClick.RemoveAllListeners();
            if (settingsButton != null)
                settingsButton.onClick.RemoveAllListeners();
            if (exitButton != null)
                exitButton.onClick.RemoveAllListeners();
        }
    }
}
