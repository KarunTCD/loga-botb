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
            SetupButtons();
        }
        
        private void OnEnable()
        {
            // Called when panel is activated
            SetupUI();
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

            // Setup pause button
            if (pauseButtonText != null)
                pauseButtonText.text = "| |";

            // Ensure pause button is visible initially
            if (pauseButton != null)
            {
                pauseButton.gameObject.SetActive(true);
            }
        }

        private void SetupButtons()
        {
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPausePressed);
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
                    {
                        soundWaves[i].gameObject.AddComponent<CanvasGroup>();
                    }
                }
            }

            int cycleCount = 0;
            while (true)
            {
                cycleCount++;
                
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
            if (canvasGroup == null)
            {
                yield break;
            }
            
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

            // Update provider text to show device type
            if (providerText != null)
            {
                string provider = headTrackingService.ActiveProviderName ?? "Phone Sensor";
                
                // Map provider names to user-friendly device names
                string deviceName = provider switch
                {
                    "AirPodsHeadTrackingProvider" => "AirPods",
                    "MMRLHeadTrackingProvider" => "MMRL Device",
                    "PhoneOrientationProvider" => "Phone Sensor",
                    _ => provider
                };
                
                providerText.text = $"Tracking: {deviceName}";
            }
        }

        #endregion

        #region Button Handlers

        /// <summary>
        /// ROBUST PAUSE: Direct call to UIManager.PauseGame()
        /// No timers, no debouncing - UIManager handles all validation
        /// </summary>
        private void OnPausePressed()
        {
            Debug.Log("AudioPlayModeUI: Pause button pressed");

            if (uiManager == null)
            {
                Debug.LogError("AudioPlayModeUI: UIManager not set");
                return;
            }

            // Simple direct call - UIManager handles all state validation
            uiManager.PauseGame();

            // Note: Pause button will be hidden by OnGamePaused() callback
        }

        #endregion

        #region Public Interface

        /// <summary>
        /// Called by UIManager when game is paused
        /// ROBUST: Hide pause button so it can't be clicked while paused
        /// </summary>
        public void OnGamePaused()
        {
            Debug.Log("AudioPlayModeUI: Game paused - hiding pause button");

            if (pauseButton != null)
            {
                pauseButton.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Called by UIManager when game is resumed
        /// ROBUST: Show pause button again
        /// </summary>
        public void OnGameResumed()
        {
            Debug.Log("AudioPlayModeUI: Game resumed - showing pause button");

            if (pauseButton != null)
            {
                pauseButton.gameObject.SetActive(true);
            }
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            if (pauseButton != null)
                pauseButton.onClick.RemoveListener(OnPausePressed);

            if (waveAnimationCoroutine != null)
                StopCoroutine(waveAnimationCoroutine);
        }

        #endregion
    }
}