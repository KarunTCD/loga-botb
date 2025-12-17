using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    public class AudioPlayModeUI : MonoBehaviour
    {
        [Header("Main Message")]
        [SerializeField] private TextMeshProUGUI mainMessageText;
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Header("Headphone Graphic")]
        [SerializeField] private Image headphoneImage;
        [SerializeField] private RectTransform[] soundWaves; // 3-4 circles
        [SerializeField] private float waveAnimationSpeed = 1.5f;
        [SerializeField] private float wavePulseScale = 1.3f;

        [Header("Orientation Marker")]
        [SerializeField] private RectTransform orientationCircle;
        [SerializeField] private RectTransform playerMarker; // Arrow/dot showing direction
        [SerializeField] private TextMeshProUGUI providerText;
        [SerializeField] private float markerRadius = 30f;

        [Header("Controls")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private TextMeshProUGUI pauseButtonText;

        [Header("Session Info (Optional)")]
        [SerializeField] private TextMeshProUGUI sessionIdText;
        [SerializeField] private Button shareButton;
        [SerializeField] private GameObject sessionInfoContainer;

        [Header("Witty Messages")]
        [SerializeField]
        private string[] cheekyMessages = new string[]
        {
            "Time to keep your phone in your pocket",
            "Put me away and let your ears do the walking",
            "Screen time's over - it's sound time now",
            "Trust your ears, not your eyes",
            "Phone goes in pocket. History comes alive."
        };

        [SerializeField]
        private string[] subtitles = new string[]
        {
            "and listen to the whispers of history...",
            "the battlefield awaits your ears",
            "follow the sounds through time",
            "let spatial audio guide you",
            "Sound will be your compass"
        };

        private UIManager uiManager;
        private IHeadTrackingService headTrackingService;
        private bool isPaused = false;
        private Coroutine waveAnimationCoroutine;

        private void Start()
        {
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
            SetupUI();
            SetupButtons();
            StartSoundWaveAnimation();
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
        }

        private void SetupUI()
        {
            // Random witty message
            int messageIndex = Random.Range(0, cheekyMessages.Length);
            if (mainMessageText != null)
                mainMessageText.text = cheekyMessages[messageIndex];

            if (subtitleText != null)
                subtitleText.text = subtitles[messageIndex];

            // Hide session info by default (can show on demand)
            if (sessionInfoContainer != null)
                sessionInfoContainer.SetActive(false);

            // Setup pause button
            if (pauseButtonText != null)
                pauseButtonText.text = "| |";
        }

        private void SetupButtons()
        {
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPausePressed);

            if (shareButton != null)
                shareButton.onClick.AddListener(OnSharePressed);
        }

        private void Update()
        {
            UpdateOrientationMarker();
        }

        #region Sound Wave Animation

        private void StartSoundWaveAnimation()
        {
            if (soundWaves == null || soundWaves.Length == 0)
            {
                Debug.LogWarning("AudioPlayModeUI: No sound waves assigned for animation");
                return;
            }

            waveAnimationCoroutine = StartCoroutine(AnimateSoundWaves());
        }

        private IEnumerator AnimateSoundWaves()
        {
            float delayBetweenWaves = 0.3f;

            // Initialize all waves
            for (int i = 0; i < soundWaves.Length; i++)
            {
                if (soundWaves[i] != null)
                {
                    soundWaves[i].localScale = Vector3.zero;

                    // Add CanvasGroup if missing
                    if (soundWaves[i].GetComponent<CanvasGroup>() == null)
                        soundWaves[i].gameObject.AddComponent<CanvasGroup>();
                }
            }

            while (true)
            {
                // Animate each wave with staggered start
                for (int i = 0; i < soundWaves.Length; i++)
                {
                    if (soundWaves[i] != null)
                    {
                        StartCoroutine(AnimateSingleWave(soundWaves[i], i * delayBetweenWaves));
                    }
                }

                // Wait for full animation cycle
                yield return new WaitForSeconds(2f);
            }
        }

        private IEnumerator AnimateSingleWave(RectTransform wave, float startDelay)
        {
            yield return new WaitForSeconds(startDelay);

            CanvasGroup canvasGroup = wave.GetComponent<CanvasGroup>();
            float animationDuration = 1.5f;
            float elapsed = 0f;

            Vector3 startScale = Vector3.one * 0.5f;
            Vector3 endScale = Vector3.one * wavePulseScale;

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / animationDuration;

                // Scale expands
                wave.localScale = Vector3.Lerp(startScale, endScale, progress);

                // Fade in then out
                if (progress < 0.3f)
                {
                    canvasGroup.alpha = Mathf.Lerp(0f, 0.8f, progress / 0.3f);
                }
                else
                {
                    canvasGroup.alpha = Mathf.Lerp(0.8f, 0f, (progress - 0.3f) / 0.7f);
                }

                yield return null;
            }

            // Reset
            wave.localScale = Vector3.zero;
            canvasGroup.alpha = 0f;
        }

        #endregion

        #region Orientation Marker

        private void UpdateOrientationMarker()
        {
            if (headTrackingService == null || playerMarker == null || orientationCircle == null)
                return;

            // Get current heading (0-360)
            float heading = headTrackingService.CurrentHeading;

            // Convert to radians and position on circle
            // Subtract 90 to start at top (Unity UI coordinates)
            float angleRad = (heading - 90f) * Mathf.Deg2Rad;

            Vector2 markerPosition = new Vector2(
                Mathf.Cos(angleRad) * markerRadius,
                Mathf.Sin(angleRad) * markerRadius
            );

            playerMarker.anchoredPosition = markerPosition;

            // Rotate marker to point outward
            playerMarker.localRotation = Quaternion.Euler(0, 0, heading);

            // Update provider text
            if (providerText != null)
            {
                string provider = headTrackingService.ActiveProviderName ?? "None";
                providerText.text = $"Tracking: {provider}";
            }
        }

        #endregion

        #region Button Handlers

        private void OnPausePressed()
        {
            Debug.Log("AudioPlayModeUI: Pause button pressed");

            if (uiManager != null)
            {
                uiManager.ShowPauseMenu();
            }
            else
            {
                Debug.LogError("AudioPlayModeUI: UIManager reference not set");
            }
        }

        private void OnSharePressed()
        {
            if (sessionIdText != null && !string.IsNullOrEmpty(sessionIdText.text))
            {
                // Copy session ID to clipboard
                string sessionId = sessionIdText.text.Replace("Session: ", "");
                GUIUtility.systemCopyBuffer = sessionId;

                Debug.Log($"AudioPlayModeUI: Session ID copied: {sessionId}");

                // Show brief feedback
                StartCoroutine(ShowShareFeedback());
            }
        }

        private IEnumerator ShowShareFeedback()
        {
            if (shareButton != null)
            {
                var buttonText = shareButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    string originalText = buttonText.text;
                    buttonText.text = "Copied!";
                    yield return new WaitForSeconds(1.5f);
                    buttonText.text = originalText;
                }
            }
        }

        #endregion

        #region Public Interface

        public void UpdateSessionId(string sessionId)
        {
            if (sessionIdText != null)
            {
                sessionIdText.text = $"Session: {sessionId}";
            }
        }

        public void ShowSessionInfo(bool show)
        {
            if (sessionInfoContainer != null)
            {
                sessionInfoContainer.SetActive(show);
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(OnPausePressed);

            if (shareButton != null)
                shareButton.onClick.RemoveListener(OnSharePressed);

            if (waveAnimationCoroutine != null)
                StopCoroutine(waveAnimationCoroutine);
        }

        #endregion
    }
}