using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Tutorial UI Component - guides first-time users through system features
    /// Provides interactive demonstrations and reports to UIManager
    /// </summary>
    public class TutorialUI : MonoBehaviour
    {
        [Header("Display Elements")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Navigation Elements")]
        [SerializeField] private Button nextButton;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button backButton;

        [Header("Progress Visualization")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image progressFill;
        [SerializeField] private Color progressColor = Color.green;

        [Header("Audio Demo")]
        [SerializeField] private AudioSource demoAudioSource;
        [SerializeField] private AudioClip[] demoSounds;
        [SerializeField] private Transform audioTarget;

        [Header("Tutorial Settings")]
        [SerializeField] private bool enableHeadTrackingDemo = true;
        [SerializeField] private bool enableSpatialAudioDemo = true;
        [SerializeField] private float audioDemoRadius = 5f;
        [SerializeField] private float audioDemoSpeed = 30f;

        #region Tutorial Step Definition

        [System.Serializable]
        public class TutorialStep
        {
            [Header("Content")]
            public string title;
            [TextArea(3, 6)]
            public string description;

            [Header("Behavior")]
            public StepType type;
            public float duration = 0f;
            public AudioClip audioClip;

            [Header("Interactive Requirements")]
            public bool requiresHeadMovement;
            public float requiredMovementDegrees = 20f;
            public bool autoAdvance = false;
        }

        public enum StepType
        {
            Introduction,
            HeadTrackingTest,
            SpatialAudioDemo,
            GameplayExplanation,
            InteractionTest,
            Completion
        }

        [Header("Tutorial Content")]
        [SerializeField] private List<TutorialStep> tutorialSteps = new List<TutorialStep>();

        #endregion

        #region State Management

        private UIManager uiManager;
        private int currentStepIndex = 0;
        private bool isRunning = false;
        private bool isInteractive = true;

        // Service references
        private IHeadTrackingService headTrackingService;
        private IAudioService audioService;

        // Head tracking test state
        private bool isMonitoringHeadMovement = false;
        private float initialHeading = 0f;
        private float maxMovementDetected = 0f;
        private float testStartTime = 0f;

        // Audio demo state
        private bool isPlayingAudioDemo = false;
        private Coroutine audioDemoCoroutine;
        private Coroutine headTrackingCoroutine;

        #endregion

        #region Initialization

        private void Start()
        {
            InitializeUI();
            SetupButtonListeners();
            SetupDefaultSteps();
        }

        public void SetUIManager(UIManager uiManager)
        {
            this.uiManager = uiManager;
            Debug.Log("TutorialUI: UIManager reference set");
        }

        private void InitializeUI()
        {
            SetInteractive(true);
            UpdateProgressVisuals();

            if (progressFill != null)
                progressFill.color = progressColor;

            if (demoAudioSource == null && audioTarget != null)
                demoAudioSource = audioTarget.GetComponent<AudioSource>();
        }

        private void SetupButtonListeners()
        {
            if (nextButton != null)
                nextButton.onClick.AddListener(OnNextButtonPressed);
            if (previousButton != null)
                previousButton.onClick.AddListener(OnPreviousButtonPressed);
            if (skipButton != null)
                skipButton.onClick.AddListener(OnSkipButtonPressed);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackButtonPressed);
        }

        private void SetupDefaultSteps()
        {
            if (tutorialSteps.Count == 0)
            {
                CreateDefaultTutorialSteps();
            }
        }

        private void CreateDefaultTutorialSteps()
        {
            tutorialSteps.Add(new TutorialStep
            {
                title = "Welcome to Battle of the Boyne",
                description = "This interactive audio experience will guide you through the historic battlefield using spatial audio and head tracking technology.\n\nYou'll explore points of interest, collect artifacts, and experience combat encounters as you learn about this pivotal moment in Irish history.",
                type = StepType.Introduction,
                duration = 0f,
                autoAdvance = false
            });

            tutorialSteps.Add(new TutorialStep
            {
                title = "Head Tracking Test",
                description = "First, let's test your head tracking system.\n\nThis technology allows the game to know which direction you're facing, creating immersive spatial audio experiences.",
                type = StepType.HeadTrackingTest,
                requiresHeadMovement = true,
                requiredMovementDegrees = 20f
            });

            tutorialSteps.Add(new TutorialStep
            {
                title = "Spatial Audio Demo",
                description = "Now you'll experience how spatial audio works in the game.\n\nYou'll hear sounds positioned around you in 3D space. Try turning your head to follow the sound as it moves!",
                type = StepType.SpatialAudioDemo,
                duration = 10f
            });

            tutorialSteps.Add(new TutorialStep
            {
                title = "Gameplay Overview",
                description = "During the experience, you'll:\n\n• Walk to Points of Interest to hear historical accounts\n• Collect artifacts by getting close to them\n• Defend against mercenary attacks by facing them\n• Collect berries to restore health if injured",
                type = StepType.GameplayExplanation,
                duration = 0f
            });

            tutorialSteps.Add(new TutorialStep
            {
                title = "Ready to Begin!",
                description = "You're now ready to start your journey through the Battle of the Boyne.\n\nRemember to wear headphones for the best experience, and make sure you're in a safe area where you can move freely.",
                type = StepType.Completion,
                duration = 0f
            });
        }

        #endregion

        #region Tutorial Flow Control

        public void StartTutorial()
        {
            Debug.Log("TutorialUI: Starting tutorial flow");

            GetServiceReferences();

            isRunning = true;
            currentStepIndex = 0;

            ShowCurrentStep();
        }

        private void GetServiceReferences()
        {
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
            audioService = ServiceLocator.GetService<IAudioService>();

            if (headTrackingService == null)
            {
                Debug.LogWarning("TutorialUI: HeadTrackingService not available - disabling head tracking demos");
                enableHeadTrackingDemo = false;
            }

            if (audioService == null)
            {
                Debug.LogWarning("TutorialUI: AudioService not available - disabling audio demos");
                enableSpatialAudioDemo = false;
            }

            bool headTrackingWorking = headTrackingService != null &&
                                     !string.IsNullOrEmpty(headTrackingService.ActiveProviderName) &&
                                     headTrackingService.ActiveProviderName != "None";

            Debug.Log($"TutorialUI: Services available - HeadTracking: {headTrackingWorking}, Audio: {audioService != null}");
        }

        private void ShowCurrentStep()
        {
            if (currentStepIndex >= tutorialSteps.Count)
            {
                CompleteTutorial();
                return;
            }

            var step = tutorialSteps[currentStepIndex];

            UpdateStepDisplay(step);
            UpdateProgressVisuals();
            UpdateButtonVisibility();

            HandleStepBehavior(step);

            Debug.Log($"TutorialUI: Showing step {currentStepIndex + 1}/{tutorialSteps.Count}: {step.title}");
        }

        private void UpdateStepDisplay(TutorialStep step)
        {
            if (titleText != null)
                titleText.text = step.title;

            if (descriptionText != null)
                descriptionText.text = step.description;

            if (statusText != null)
                statusText.text = "";
        }

        private void UpdateProgressVisuals()
        {
            float progress = tutorialSteps.Count > 0 ? (float)(currentStepIndex + 1) / tutorialSteps.Count : 0f;

            if (progressText != null)
                progressText.text = $"Step {currentStepIndex + 1} of {tutorialSteps.Count}";

            if (progressSlider != null)
                progressSlider.value = progress;
        }

        private void UpdateButtonVisibility()
        {
            bool isFirstStep = currentStepIndex == 0;
            bool isLastStep = currentStepIndex >= tutorialSteps.Count - 1;

            if (previousButton != null)
                previousButton.gameObject.SetActive(!isFirstStep);

            if (nextButton != null)
            {
                nextButton.gameObject.SetActive(true);
                var buttonText = nextButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText == null)
                    buttonText = nextButton.GetComponentInChildren<TextMeshProUGUI>();

                if (buttonText != null)
                    buttonText.text = isLastStep ? "Finish" : "Next";
            }

            if (skipButton != null)
                skipButton.gameObject.SetActive(!isLastStep);
        }

        #endregion

        #region Step Behavior Handling

        private void HandleStepBehavior(TutorialStep step)
        {
            // Clean up any previous step behavior
            CleanupCurrentStepBehavior();

            switch (step.type)
            {
                case StepType.Introduction:
                    HandleIntroductionStep(step);
                    break;

                case StepType.HeadTrackingTest:
                    HandleHeadTrackingStep(step);
                    break;

                case StepType.SpatialAudioDemo:
                    HandleSpatialAudioStep(step);
                    break;

                case StepType.GameplayExplanation:
                    HandleGameplayExplanationStep(step);
                    break;

                case StepType.InteractionTest:
                    HandleInteractionTestStep(step);
                    break;

                case StepType.Completion:
                    HandleCompletionStep(step);
                    break;
            }

            // Handle auto-advance
            if (step.autoAdvance && step.duration > 0f)
            {
                StartCoroutine(AutoAdvanceAfterDelay(step.duration));
            }
        }

        private void HandleIntroductionStep(TutorialStep step)
        {
            if (step.audioClip != null)
            {
                PlayStepAudio(step.audioClip);
            }
        }

        private void HandleHeadTrackingStep(TutorialStep step)
        {
            if (!enableHeadTrackingDemo || headTrackingService == null)
            {
                if (descriptionText != null)
                {
                    descriptionText.text = step.description + "\n\n⚠️ Head tracking not available - skipping test";
                }
                StartCoroutine(AutoAdvanceAfterDelay(3f));
                return;
            }

            if (step.requiresHeadMovement)
            {
                StartHeadMovementTest(step.requiredMovementDegrees);
            }
        }

        private void HandleSpatialAudioStep(TutorialStep step)
        {
            if (!enableSpatialAudioDemo)
            {
                if (descriptionText != null)
                {
                    descriptionText.text = step.description + "\n\n⚠️ Spatial audio demo not available - continuing...";
                }
                StartCoroutine(AutoAdvanceAfterDelay(3f));
                return;
            }

            if (step.audioClip != null || (demoSounds != null && demoSounds.Length > 0))
            {
                StartSpatialAudioDemo(step);
            }
            else
            {
                StartCoroutine(AutoAdvanceAfterDelay(2f));
            }
        }

        private void HandleGameplayExplanationStep(TutorialStep step)
        {
            if (step.audioClip != null)
            {
                PlayStepAudio(step.audioClip);
            }
        }

        private void HandleInteractionTestStep(TutorialStep step)
        {
            // Could implement gesture recognition or other interaction tests
            if (descriptionText != null)
            {
                descriptionText.text = step.description + "\n\nTap the screen to continue.";
            }
        }

        private void HandleCompletionStep(TutorialStep step)
        {
            if (step.audioClip != null)
            {
                PlayStepAudio(step.audioClip);
            }
        }

        #endregion

        #region Head Tracking Test

        private void StartHeadMovementTest(float requiredDegrees)
        {
            Debug.Log("TutorialUI: Starting head movement test");

            if (headTrackingService == null)
            {
                StartCoroutine(AutoAdvanceAfterDelay(2f));
                return;
            }

            isMonitoringHeadMovement = true;
            initialHeading = headTrackingService.CurrentHeading;
            maxMovementDetected = 0f;
            testStartTime = Time.time;

            UpdateStepStatus("Turn your head left and right...");

            headTrackingCoroutine = StartCoroutine(MonitorHeadMovementCoroutine(requiredDegrees));
        }

        private IEnumerator MonitorHeadMovementCoroutine(float requiredDegrees)
        {
            float timeout = 15f;

            while (isMonitoringHeadMovement && (Time.time - testStartTime) < timeout)
            {
                if (headTrackingService != null)
                {
                    float currentHeading = headTrackingService.CurrentHeading;
                    float movement = Mathf.Abs(Mathf.DeltaAngle(initialHeading, currentHeading));
                    maxMovementDetected = Mathf.Max(maxMovementDetected, movement);

                    float progress = Mathf.Clamp01(maxMovementDetected / requiredDegrees);
                    int percentage = Mathf.RoundToInt(progress * 100f);

                    UpdateStepStatus($"Head movement: {percentage}% ({maxMovementDetected:F0}° detected)");

                    if (maxMovementDetected >= requiredDegrees)
                    {
                        HeadTrackingTestSuccess();
                        yield break;
                    }
                }

                yield return new WaitForSeconds(0.1f);
            }

            // Timeout
            HeadTrackingTestTimeout();
        }

        private void HeadTrackingTestSuccess()
        {
            Debug.Log($"TutorialUI: Head tracking test successful - {maxMovementDetected:F0}° detected");

            isMonitoringHeadMovement = false;
            UpdateStepStatus("✅ Head tracking test complete!");

            if (descriptionText != null)
            {
                descriptionText.text = "Excellent! Your head movements are being tracked accurately.\n\nThis will allow you to look around and interact with the spatial audio environment.";
            }

            StartCoroutine(AutoAdvanceAfterDelay(3f));
        }

        private void HeadTrackingTestTimeout()
        {
            Debug.LogWarning($"TutorialUI: Head tracking test timeout - only {maxMovementDetected:F0}° detected");

            isMonitoringHeadMovement = false;

            if (maxMovementDetected < 1f)
            {
                UpdateStepStatus("⚠️ Limited head movement detected");
            }
            else
            {
                UpdateStepStatus($"⚠️ Partial success - {maxMovementDetected:F0}° detected");
            }

            StartCoroutine(AutoAdvanceAfterDelay(2f));
        }

        #endregion

        #region Spatial Audio Demo

        private void StartSpatialAudioDemo(TutorialStep step)
        {
            Debug.Log("TutorialUI: Starting spatial audio demo");

            if (demoAudioSource == null || audioTarget == null)
            {
                Debug.LogWarning("TutorialUI: Audio demo components not configured");
                StartCoroutine(AutoAdvanceAfterDelay(2f));
                return;
            }

            AudioClip clipToPlay = step.audioClip;
            if (clipToPlay == null && demoSounds != null && demoSounds.Length > 0)
            {
                clipToPlay = demoSounds[0];
            }

            if (clipToPlay != null)
            {
                isPlayingAudioDemo = true;
                UpdateStepStatus("🎵 Listen as the sound moves around you!");
                audioDemoCoroutine = StartCoroutine(PlayMovingAudioDemo(clipToPlay, step.duration > 0 ? step.duration : 8f));
            }
            else
            {
                StartCoroutine(AutoAdvanceAfterDelay(2f));
            }
        }

        private IEnumerator PlayMovingAudioDemo(AudioClip clip, float duration)
        {
            if (demoAudioSource == null || audioTarget == null)
            {
                yield break;
            }

            // Setup audio source
            demoAudioSource.clip = clip;
            demoAudioSource.loop = true;
            demoAudioSource.spatialBlend = 1f; // 3D spatial audio
            demoAudioSource.Play();

            float elapsed = 0f;

            while (elapsed < duration && isPlayingAudioDemo)
            {
                // Move audio source in a circle
                float angle = (elapsed / duration) * 360f * (audioDemoSpeed / 60f);

                Vector3 position = new Vector3(
                    Mathf.Sin(angle * Mathf.Deg2Rad) * audioDemoRadius,
                    0f,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * audioDemoRadius
                );

                audioTarget.localPosition = position;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Stop audio and complete demo
            demoAudioSource.Stop();
            isPlayingAudioDemo = false;

            UpdateStepStatus("✅ Spatial audio demo complete!");

            if (descriptionText != null)
            {
                descriptionText.text = "Great! You've experienced spatial audio.\n\nIn the game, you'll hear historical accounts, environmental sounds, and combat audio positioned around you in 3D space.";
            }

            yield return new WaitForSeconds(2f);

            if (isRunning)
            {
                NextStep();
            }
        }

        #endregion

        #region Audio Helpers

        private void PlayStepAudio(AudioClip clip)
        {
            if (demoAudioSource != null && clip != null)
            {
                demoAudioSource.clip = clip;
                demoAudioSource.loop = false;
                demoAudioSource.spatialBlend = 0f; // 2D audio for instructions
                demoAudioSource.Play();
                Debug.Log("TutorialUI: Playing step audio");
            }
        }

        #endregion

        #region Navigation Controls

        public void NextStep()
        {
            if (!isRunning || !isInteractive) return;

            CleanupCurrentStepBehavior();
            currentStepIndex++;
            ShowCurrentStep();
        }

        public void PreviousStep()
        {
            if (!isRunning || !isInteractive || currentStepIndex <= 0) return;

            CleanupCurrentStepBehavior();
            currentStepIndex--;
            ShowCurrentStep();
        }

        private void CompleteTutorial()
        {
            Debug.Log("TutorialUI: Tutorial completed successfully");

            isRunning = false;
            CleanupCurrentStepBehavior();

            // Mark tutorial as completed
            PlayerPrefs.SetString("TutorialCompleted", "true");
            PlayerPrefs.Save();

            // Report completion to UIManager
            if (uiManager != null)
            {
                uiManager.OnTutorialComplete();
            }
            else
            {
                Debug.LogError("TutorialUI: UIManager reference not set - cannot proceed");
            }
        }

        #endregion

        #region Button Event Handlers

        private void OnNextButtonPressed()
        {
            Debug.Log("TutorialUI: Next button pressed");

            if (currentStepIndex >= tutorialSteps.Count - 1)
            {
                CompleteTutorial();
            }
            else
            {
                NextStep();
            }
        }

        private void OnPreviousButtonPressed()
        {
            Debug.Log("TutorialUI: Previous button pressed");
            PreviousStep();
        }

        private void OnSkipButtonPressed()
        {
            Debug.Log("TutorialUI: Skip button pressed");
            CompleteTutorial();
        }

        private void OnBackButtonPressed()
        {
            Debug.Log("TutorialUI: Back button pressed");

            CleanupCurrentStepBehavior();
            isRunning = false;

            // Report back navigation to UIManager
            if (uiManager != null)
            {
                uiManager.OnBackButtonPressed();
            }
            else
            {
                Debug.LogError("TutorialUI: UIManager reference not set");
            }
        }

        #endregion

        #region Helper Methods

        private IEnumerator AutoAdvanceAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (isRunning)
            {
                NextStep();
            }
        }

        private void UpdateStepStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void SetInteractive(bool interactive)
        {
            isInteractive = interactive;

            if (nextButton != null) nextButton.interactable = interactive;
            if (previousButton != null) previousButton.interactable = interactive;
            if (skipButton != null) skipButton.interactable = interactive;
            if (backButton != null) backButton.interactable = interactive;
        }

        private void CleanupCurrentStepBehavior()
        {
            // Stop head tracking monitoring
            isMonitoringHeadMovement = false;
            if (headTrackingCoroutine != null)
            {
                StopCoroutine(headTrackingCoroutine);
                headTrackingCoroutine = null;
            }

            // Stop audio demo
            isPlayingAudioDemo = false;
            if (audioDemoCoroutine != null)
            {
                StopCoroutine(audioDemoCoroutine);
                audioDemoCoroutine = null;
            }

            // Stop any playing audio
            if (demoAudioSource != null && demoAudioSource.isPlaying)
            {
                demoAudioSource.Stop();
            }

            // Clear status
            UpdateStepStatus("");
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            CleanupCurrentStepBehavior();

            // Remove button listeners
            if (nextButton != null)
                nextButton.onClick.RemoveListener(OnNextButtonPressed);
            if (previousButton != null)
                previousButton.onClick.RemoveListener(OnPreviousButtonPressed);
            if (skipButton != null)
                skipButton.onClick.RemoveListener(OnSkipButtonPressed);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackButtonPressed);

            Debug.Log("TutorialUI: Cleanup completed");
        }

        #endregion
    }
}