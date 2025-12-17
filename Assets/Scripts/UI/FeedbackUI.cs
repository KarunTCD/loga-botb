using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Utilities;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Feedback UI Component - displays feedback code and handles user interaction
    /// Simple implementation following UIManager coordination pattern
    /// </summary>
    public class FeedbackUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TextMeshProUGUI instructionText;
        [SerializeField] private TextMeshProUGUI codeText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private Button closeButton;

        [Header("Text Content")]
        [TextArea(3, 5)]
        [SerializeField] private string instructionMessage = "Please provide this code when submitting feedback:";

        private UIManager uiManager;
        private string currentCode;
        private Coroutine statusCoroutine;

        private void Start()
        {
            SetupUI();
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("FeedbackUI: UIManager reference set");
        }

        private void SetupUI()
        {
            if (copyCodeButton != null)
                copyCodeButton.onClick.AddListener(OnCopyCodePressed);

            if (closeButton != null)
                closeButton.onClick.AddListener(OnClosePressed);

            if (instructionText != null)
                instructionText.text = instructionMessage;

            ClearStatus();
        }

        public void ShowFeedbackCode()
        {
            Debug.Log("FeedbackUI: ShowFeedbackCode called");

            // Get the feedback code
            currentCode = FeedbackCodeUtility.GetFeedbackCode();

            if (codeText != null)
            {
                codeText.text = currentCode;
            }

            SetButtonsEnabled(true);
            ClearStatus();

            Debug.Log($"FeedbackUI: Displaying code {currentCode} to user");
        }

        private void OnCopyCodePressed()
        {
            Debug.Log("FeedbackUI: Copy code button pressed");

            if (string.IsNullOrEmpty(currentCode))
            {
                ShowStatus("No code to copy", Color.red);
                return;
            }

            // Copy to clipboard
            GUIUtility.systemCopyBuffer = currentCode;

            ShowStatus("Code copied to clipboard!", Color.green);
            Debug.Log($"FeedbackUI: Copied code {currentCode} to clipboard");
        }

        private void OnClosePressed()
        {
            Debug.Log("FeedbackUI: Close button pressed");

            if (uiManager != null)
            {
                uiManager.OnFeedbackClosed();
            }
            else
            {
                Debug.LogError("FeedbackUI: UIManager reference not set");
            }
        }

        private void ShowStatus(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }

            // Clear status after delay
            if (statusCoroutine != null)
            {
                StopCoroutine(statusCoroutine);
            }
            statusCoroutine = StartCoroutine(ClearStatusAfterDelay(2f));
        }

        private void ClearStatus()
        {
            if (statusText != null)
            {
                statusText.text = "";
                statusText.color = Color.white;
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (copyCodeButton != null)
                copyCodeButton.interactable = enabled;
            if (closeButton != null)
                closeButton.interactable = enabled;
        }

        private IEnumerator ClearStatusAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearStatus();
        }

        public string GetCurrentFeedbackCode()
        {
            return currentCode ?? FeedbackCodeUtility.GetFeedbackCode();
        }

        private void OnDestroy()
        {
            if (copyCodeButton != null)
                copyCodeButton.onClick.RemoveListener(OnCopyCodePressed);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClosePressed);

            Debug.Log("FeedbackUI: Cleanup completed");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [ContextMenu("Test Show Feedback Code")]
        private void TestShowFeedbackCode()
        {
            ShowFeedbackCode();
        }
    }
}
