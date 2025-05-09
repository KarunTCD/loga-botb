using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModeSelectionUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_InputField sessionInputField;
    [SerializeField] private Button playButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TextMeshProUGUI errorText;

    [Header("References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private UIManager uiManager;

    private void Start()
    {
        SetupButtonListeners();
        ClearError();
    }

    private void SetupButtonListeners()
    {
        playButton.onClick.AddListener(OnPlayButtonClick);
        joinButton.onClick.AddListener(OnJoinButtonClick);
    }

    private async void OnPlayButtonClick()
    {
        // Disable buttons to prevent double-click
        SetInteractable(false);

        // Signal game manager to start player mode and await result
        bool success = await gameManager.StartPlayerMode();

        // Handle failure
        if (!success)
        {
            SetInteractable(true);
            ShowError("Failed to start player mode");
        }
    }

    private async void OnJoinButtonClick()
    {
        string sessionId = sessionInputField.text.Trim();
        if (string.IsNullOrEmpty(sessionId))
        {
            ShowError("Please enter a session ID");
            return;
        }

        // Disable buttons to prevent double-click
        SetInteractable(false);

        // Begin spectator mode connection and await result
        bool success = await gameManager.StartSpectatorMode(sessionId);

        // Handle connection failure
        if (!success)
        {
            SetInteractable(true);
            ShowError("Failed to connect to session");
        }
    }

    public void SetInteractable(bool interactable)
    {
        playButton.interactable = interactable;
        joinButton.interactable = interactable;
        sessionInputField.interactable = interactable;
    }

    public void ShowError(string message)
    {
        errorText.text = message;
    }

    public void ClearError()
    {
        errorText.text = "";
    }

    private void OnDestroy()
    {
        playButton.onClick.RemoveListener(OnPlayButtonClick);
        joinButton.onClick.RemoveListener(OnJoinButtonClick);
    }
}