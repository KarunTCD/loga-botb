using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Share UI - Session sharing panel
    /// Displays session ID and allows sharing via native OS share sheet
    /// Shows over Pause Menu
    /// </summary>
    public class ShareUI : MonoBehaviour
    {
        [Header("UI Panel")]
        [SerializeField] private GameObject sharePanel;
        
        [Header("Session ID Display")]
        [SerializeField] private TextMeshProUGUI sessionIdLabel;
        [SerializeField] private TextMeshProUGUI sessionIdText;
        
        [Header("Instructions")]
        [SerializeField] private TextMeshProUGUI instructionText;
        
        [Header("Buttons")]
        [SerializeField] private Button shareButton;
        [SerializeField] private Button copyButton;
        [SerializeField] private Button closeButton;
        
        [Header("Status Feedback")]
        [SerializeField] private TextMeshProUGUI statusText;

        private UIManager uiManager;
        private string currentSessionId;

        private void Start()
        {
            SetupButtonListeners();
            
            // Set instruction text
            if (instructionText != null)
            {
                instructionText.text = "Share your session ID with friends so they can spectate your journey:";
            }
            
            // Hide panel initially
            if (sharePanel != null)
            {
                sharePanel.SetActive(false);
            }
            
            ClearStatus();
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("ShareUI: UIManager reference set");
        }

        private void SetupButtonListeners()
        {
            if (shareButton != null)
            {
                shareButton.onClick.AddListener(OnShare);
            }
            
            if (copyButton != null)
            {
                copyButton.onClick.AddListener(OnCopy);
            }
            
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnClose);
            }
        }

        public void Show(string sessionId)
        {
            currentSessionId = sessionId;
            
            if (sharePanel != null)
            {
                sharePanel.SetActive(true);
                sharePanel.transform.SetAsLastSibling();
            }
            
            // Update session ID display
            if (sessionIdText != null)
            {
                sessionIdText.text = currentSessionId;
            }
            
            if (sessionIdLabel != null)
            {
                sessionIdLabel.text = "Session ID:";
            }
            
            ClearStatus();
            
            Debug.Log($"ShareUI: Panel shown with session ID: {sessionId}");
        }

        public void Hide()
        {
            if (sharePanel != null)
            {
                sharePanel.SetActive(false);
            }
            
            ClearStatus();
            
            Debug.Log("ShareUI: Panel hidden");
        }

        private void OnShare()
        {
            Debug.Log("ShareUI: Share button pressed");
            
            if (string.IsNullOrEmpty(currentSessionId))
            {
                ShowStatus("No session ID available", Color.red);
                return;
            }
            
            // Create share message
            string shareMessage = $"Join me on Voices of the Boyne!\n\nMy Session ID: {currentSessionId}\n\nUse this ID in Spectator Mode to follow my journey.";
            
            // Use native share sheet
            NativeShare(shareMessage);
            
            ShowStatus("Opening share menu...", Color.green);
        }

        private void OnCopy()
        {
            Debug.Log("ShareUI: Copy button pressed");
            
            if (string.IsNullOrEmpty(currentSessionId))
            {
                ShowStatus("No session ID to copy", Color.red);
                return;
            }
            
            // Copy to clipboard
            GUIUtility.systemCopyBuffer = currentSessionId;
            
            ShowStatus("Session ID copied to clipboard!", Color.green);
            
            Debug.Log($"ShareUI: Copied session ID to clipboard: {currentSessionId}");
        }

        private void OnClose()
        {
            Debug.Log("ShareUI: Close button pressed");
            
            if (uiManager != null)
            {
                uiManager.OnShareClose();
            }
        }

        private void NativeShare(string text)
        {
#if UNITY_ANDROID
            AndroidNativeShare(text);
#elif UNITY_IOS
            IOSNativeShare(text);
#else
            // Fallback for editor/other platforms - just copy to clipboard
            GUIUtility.systemCopyBuffer = text;
            ShowStatus("Copied to clipboard (share not available in editor)", Color.yellow);
            Debug.LogWarning("ShareUI: Native share not available on this platform - copied to clipboard instead");
#endif
        }

#if UNITY_ANDROID
        private void AndroidNativeShare(string text)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text);
                    
                    using (AndroidJavaObject chooser = intent.CallStatic<AndroidJavaObject>("createChooser", intent, "Share Session ID"))
                    {
                        currentActivity.Call("startActivity", chooser);
                    }
                }
                
                Debug.Log("ShareUI: Android native share invoked");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ShareUI: Android share failed - {e.Message}");
                ShowStatus("Share failed - copied to clipboard instead", Color.red);
                GUIUtility.systemCopyBuffer = text;
            }
        }
#endif

#if UNITY_IOS
        private void IOSNativeShare(string text)
        {
            // iOS share requires a plugin or native code
            // For now, copy to clipboard and show message
            GUIUtility.systemCopyBuffer = text;
            ShowStatus("Copied to clipboard - use iOS share from clipboard", Color.yellow);
            Debug.Log("ShareUI: iOS native share - copied to clipboard (requires native plugin for full functionality)");
            
            // TODO: Implement iOS share sheet using native plugin
            // Example: https://github.com/yasirkula/UnityNativeShare
        }
#endif

        private void ShowStatus(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }
            
            // Auto-clear after 3 seconds
            CancelInvoke(nameof(ClearStatus));
            Invoke(nameof(ClearStatus), 3f);
        }

        private void ClearStatus()
        {
            if (statusText != null)
            {
                statusText.text = "";
            }
        }

        private void OnDestroy()
        {
            if (shareButton != null)
                shareButton.onClick.RemoveListener(OnShare);
            
            if (copyButton != null)
                copyButton.onClick.RemoveListener(OnCopy);
            
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClose);
            
            Debug.Log("ShareUI: Destroyed and cleaned up");
        }
    }
}