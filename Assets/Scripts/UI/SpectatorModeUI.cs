using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Spectator Mode UI — mirrors AudioPlayModeUI in philosophy.
    /// Phone-away, audio-first experience driven by the player's
    /// location and heading received from Firebase via SpectatorManager.
    /// No pause, no health, no combat, no orientation marker — pure audio observation.
    /// </summary>
    public class SpectatorModeUI : MonoBehaviour
    {
        [Header("Main Message")]
        [SerializeField] private TextMeshProUGUI mainMessageText;
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Header("Headphone Graphic")]
        [SerializeField] private Image headphoneImage;
        [SerializeField] private RectTransform[] soundWaves;
        [SerializeField] private float wavePulseScale = 1.3f;

        [Header("Connection Status")]
        [SerializeField] private TextMeshProUGUI connectionStatusText;
        [SerializeField] private Image connectionStatusDot;
        [SerializeField] private Color connectedColor = Color.green;
        [SerializeField] private Color waitingColor = Color.yellow;
        [SerializeField] private Color disconnectedColor = Color.red;

        [Header("Controls")]
        [SerializeField] private Button disconnectButton;

        [Header("Spectator Messages")]
        [SerializeField]
        private string[] spectatorMessages = new string[]
        {
            "You're listening in on history",
            "Follow their journey through sound",
            "The battlefield is alive in their ears",
            "Experience the past through their steps",
            "Hear what they hear, feel what they feel"
        };

        [SerializeField]
        private string[] subtitles = new string[]
        {
            "their footsteps echo through time...",
            "spatial audio connects you both",
            "put your phone away and just listen",
            "the sounds will tell the story",
            "close your eyes and be there with them"
        };

        private UIManager uiManager;
        private Coroutine waveAnimationCoroutine;
        private bool isConnected = false;

        // Connection monitoring
        private float lastDataTime = 0f;
        private const float CONNECTION_TIMEOUT = 10f;
        private const float CONNECTION_WARNING = 5f;

        #region Unity Lifecycle

        private void OnEnable()
        {
            SetupUI();
            StartSoundWaveAnimation();
            SetConnectionState(false);
        }

        private void Update()
        {
            MonitorConnection();
        }

        private void OnDestroy()
        {
            if (disconnectButton != null)
                disconnectButton.onClick.RemoveListener(OnDisconnectPressed);

            if (waveAnimationCoroutine != null)
                StopCoroutine(waveAnimationCoroutine);
        }

        #endregion

        #region Initialization

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;

            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(OnDisconnectPressed);
        }

        private void SetupUI()
        {
            int index = Random.Range(0, spectatorMessages.Length);

            if (mainMessageText != null)
                mainMessageText.text = spectatorMessages[index];

            if (subtitleText != null)
                subtitleText.text = subtitles[index];
        }

        #endregion

        #region Sound Wave Animation

        private void StartSoundWaveAnimation()
        {
            if (soundWaves == null || soundWaves.Length == 0)
            {
                Debug.LogWarning("SpectatorModeUI: No sound waves assigned for animation");
                return;
            }

            waveAnimationCoroutine = StartCoroutine(AnimateSoundWaves());
        }

        private IEnumerator AnimateSoundWaves()
        {
            float delayBetweenWaves = 0.3f;

            for (int i = 0; i < soundWaves.Length; i++)
            {
                if (soundWaves[i] != null)
                {
                    soundWaves[i].localScale = Vector3.zero;

                    if (soundWaves[i].GetComponent<CanvasGroup>() == null)
                        soundWaves[i].gameObject.AddComponent<CanvasGroup>();
                }
            }

            while (true)
            {
                for (int i = 0; i < soundWaves.Length; i++)
                {
                    if (soundWaves[i] != null)
                        StartCoroutine(AnimateSingleWave(soundWaves[i], i * delayBetweenWaves));
                }

                yield return new WaitForSeconds(2f);
            }
        }

        private IEnumerator AnimateSingleWave(RectTransform wave, float startDelay)
        {
            yield return new WaitForSeconds(startDelay);

            CanvasGroup canvasGroup = wave.GetComponent<CanvasGroup>();
            if (canvasGroup == null) yield break;

            float animationDuration = 1.5f;
            float elapsed = 0f;

            Vector3 startScale = Vector3.one * 0.5f;
            Vector3 endScale = Vector3.one * wavePulseScale;

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / animationDuration;

                wave.localScale = Vector3.Lerp(startScale, endScale, progress);

                if (progress < 0.3f)
                    canvasGroup.alpha = Mathf.Lerp(0f, 0.8f, progress / 0.3f);
                else
                    canvasGroup.alpha = Mathf.Lerp(0.8f, 0f, (progress - 0.3f) / 0.7f);

                yield return null;
            }

            wave.localScale = Vector3.zero;
            canvasGroup.alpha = 0f;
        }

        #endregion

        #region Connection Status

        /// <summary>
        /// Called by UIManager.UpdateLocationDisplay when Firebase data arrives.
        /// </summary>
        public void UpdateLocationDisplay(float latitude, float longitude)
        {
            lastDataTime = Time.time;

            if (!isConnected)
                SetConnectionState(true);
        }

        private void SetConnectionState(bool connected)
        {
            isConnected = connected;

            if (connectionStatusText != null)
                connectionStatusText.text = connected ? "Connected" : "Waiting for signal...";

            if (connectionStatusDot != null)
                connectionStatusDot.color = connected ? connectedColor : waitingColor;
        }

        private void MonitorConnection()
        {
            if (!isConnected) return;

            float timeSinceData = Time.time - lastDataTime;

            if (timeSinceData > CONNECTION_TIMEOUT)
            {
                isConnected = false;

                if (connectionStatusText != null)
                    connectionStatusText.text = "Connection lost";

                if (connectionStatusDot != null)
                    connectionStatusDot.color = disconnectedColor;

                Debug.LogWarning("SpectatorModeUI: Connection lost — no data received");
            }
            else if (timeSinceData > CONNECTION_WARNING)
            {
                if (connectionStatusText != null)
                    connectionStatusText.text = "Signal weak...";

                if (connectionStatusDot != null)
                    connectionStatusDot.color = waitingColor;
            }
        }

        #endregion

        #region Button Handlers

        private void OnDisconnectPressed()
        {
            Debug.Log("SpectatorModeUI: Disconnect pressed");

            if (uiManager != null)
                uiManager.OnBackButtonPressed();
            else
                Debug.LogError("SpectatorModeUI: UIManager not set");
        }

        #endregion
    }
}