using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LoGa.LudoEngine.UI
{
    public class PlayModeUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI sessionIdText;
        [SerializeField] private Button copyButton;

        private void Start()
        {
            copyButton.onClick.AddListener(HandleCopyButton);
        }

        public void UpdateSessionId(string sessionId)
        {
            sessionIdText.text = $"Session ID: {sessionId}";
        }

        private void HandleCopyButton()
        {
            string sessionId = sessionIdText.text.Split(':')[1].Trim();
            GUIUtility.systemCopyBuffer = sessionId;
            Debug.Log("Session ID copied to clipboard");
        }

        private void OnDestroy()
        {
            copyButton.onClick.RemoveListener(HandleCopyButton);
        }
    }
}