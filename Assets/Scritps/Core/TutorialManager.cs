using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using LoGa.LudoEngine.Services;
using TMPro;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Manages tutorial flow for first-time users
    /// Teaches spatial audio concepts and head tracking usage
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        [Header("Tutorial UI")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Slider progressSlider;

        [Header("Tutorial Steps")]
        [SerializeField] private List<TutorialStep> tutorialSteps = new List<TutorialStep>();

        [Header("Audio Demo")]
        [SerializeField] private AudioSource demoAudioSource;
        [SerializeField] private AudioClip[] demoSounds;
        [SerializeField] private Transform audioTarget; // Object that moves around user

        [Header("Settings")]
        [SerializeField] private bool enableHeadTrackingDemo = true;
        [SerializeField] private float audioDemoRadius = 5f;
        [SerializeField] private float audioDemoSpeed = 30f; // degrees per second

        // Services
        private IHeadTrackingService headTrackingService;
        private IAudioService audioService;

        // Tutorial state
        private int currentStepIndex = 0;
        private bool isRunning = false;
        private bool headTrackingWorking = false;
        private float initialHeading = 0f;

        [System.Serializable]
        public class TutorialStep
        {
            public string title;
            [TextArea(3, 6)]
            public string description;
            public TutorialType type;
            public float duration = 0f; // 0 = manual progression
            public AudioClip audioClip;
            public bool requiresHeadMovement;
            public float requiredMovementDegrees = 20f;
        }

        public enum TutorialType
        {
            Introduction,
            HeadTrackingTest,
            SpatialAudioDemo,
            GameplayExplanation,
            Completion
        }

        private void Awake()
        {
            // Setup UI handlers
            if (nextButton != null)
                nextButton.onClick.AddListener(NextStep);

            if (previousButton != null)
                previousButton.onClick.AddListener(PreviousStep);

            if (skipButton != null)
                skipButton.onClick.AddListener(SkipTutorial);

            // Initialize demo audio
            if (demoAudioSource == null && audioTarget != null)
                demoAudioSource = audioTarget.GetComponent<AudioSource>();
        }

        public void StartTutorial()
        {
            Debug.Log("=== Starting Tutorial ===");

            // Get services
            headTrackingService = ServiceLocator.GetService<IHeadTrackingService>();
            audioService = ServiceLocator.GetService<IAudioService>();

            // Check if head tracking is working
            headTrackingWorking = headTrackingService != null &&
                                 !string.IsNullOrEmpty(headTrackingService.ActiveProviderName) &&
                                 headTrackingService.ActiveProviderName != "None";

            Debug.Log($"Tutorial: Head tracking available = {headTrackingWorking}");

            // Start tutorial
            isRunning = true;
            currentStepIndex = 0;
            initialHeading = headTrackingService?.CurrentHeading ?? 0f;

            ShowCurrentStep();
        }

        private void ShowCurrentStep()
        {
            if (currentStepIndex >= tutorialSteps.Count)
            {
                CompleteTutorial();
                return;
            }

            var step = tutorialSteps[currentStepIndex];

            // Update UI
            if (titleText != null)
                titleText.text = step.title;

            if (descriptionText != null)
                descriptionText.text = step.description;

            if (progressText != null)
                progressText.text = $"Step {currentStepIndex + 1} of {tutorialSteps.Count}";

            if (progressSlider != null)
            {
                progressSlider.value = (float)(currentStepIndex + 1) / tutorialSteps.Count;
            }

            // Update button visibility
            UpdateButtonVisibility();

            // Handle step-specific logic
            HandleStepLogic(step);

            Debug.Log($"Tutorial: Showing step {currentStepIndex + 1}: {step.title}");
        }

        private void HandleStepLogic(TutorialStep step)
        {
            switch (step.type)
            {
                case TutorialType.Introduction:
                    // Just display text
                    break;

                case TutorialType.HeadTrackingTest:
                    if (headTrackingWorking && enableHeadTrackingDemo)
                    {
                        StartHeadTrackingTest(step);
                    }
                    else
                    {
                        // Skip to next step if no head tracking
                        StartCoroutine(AutoProgressAfterDelay(3f));
                    }
                    break;

                case TutorialType.SpatialAudioDemo:
                    StartSpatialAudioDemo(step);
                    break;

                case TutorialType.GameplayExplanation:
                    // Display gameplay explanation
                    if (step.audioClip != null)
                    {
                        PlayStepAudio(step.audioClip);
                    }
                    break;

                case TutorialType.Completion:
                    // Final step
                    break;
            }

            // Auto-progress if duration is set
            if (step.duration > 0f)
            {
                StartCoroutine(AutoProgressAfterDelay(step.duration));
            }
        }

        private void StartHeadTrackingTest(TutorialStep step)
        {
            if (headTrackingService == null) return;

            Debug.Log("Starting head tracking test");

            if (descriptionText != null)
            {
                descriptionText.text = step.description + "\n\n🎯 Turn your head left and right to test tracking";
            }

            initialHeading = headTrackingService.CurrentHeading;

            if (step.requiresHeadMovement)
            {
                StartCoroutine(MonitorHeadMovement(step.requiredMovementDegrees));
            }
        }

        private IEnumerator MonitorHeadMovement(float requiredDegrees)
        {
            float maxMovement = 0f;
            float startTime = Time.time;

            while (maxMovement < requiredDegrees && Time.time - startTime < 15f) // 15 second timeout
            {
                if (headTrackingService != null)
                {
                    float currentMovement = Mathf.Abs(Mathf.DeltaAngle(initialHeading, headTrackingService.CurrentHeading));
                    maxMovement = Mathf.Max(maxMovement, currentMovement);

                    // Update progress in description
                    if (descriptionText != null)
                    {
                        float progress = Mathf.Clamp01(maxMovement / requiredDegrees);
                        int percentage = Mathf.RoundToInt(progress * 100f);
                        descriptionText.text = $"Head Tracking Test\n\n🎯 Turn your head left and right\n\nProgress: {percentage}% ({maxMovement:F0}° detected)";
                    }

                    if (maxMovement >= requiredDegrees)
                    {
                        // Success!
                        if (descriptionText != null)
                            descriptionText.text = "✅ Head tracking test complete!\n\nGreat! Your head movements are being tracked accurately.";

                        yield return new WaitForSeconds(2f);
                        NextStep();
                        yield break;
                    }
                }

                yield return new WaitForSeconds(0.1f);
            }

            // Timeout or no movement
            if (descriptionText != null)
                descriptionText.text = "⚠️ Head tracking test completed\n\nContinuing with tutorial...";

            yield return new WaitForSeconds(2f);
            NextStep();
        }

        private void StartSpatialAudioDemo(TutorialStep step)
        {
            Debug.Log("Starting spatial audio demo");

            if (step.audioClip != null && demoAudioSource != null)
            {
                // Position audio target and play sound
                StartCoroutine(MovingAudioDemo(step));
            }
            else
            {
                // Just play static audio
                if (step.audioClip != null)
                    PlayStepAudio(step.audioClip);
            }
        }

        private IEnumerator MovingAudioDemo(TutorialStep step)
        {
            if (audioTarget == null || demoAudioSource == null) yield break;

            // Update description
            if (descriptionText != null)
            {
                descriptionText.text = step.description + "\n\n🎵 Listen as the sound moves around you!\n(Turn your head to follow the sound)";
            }

            // Play the audio clip on loop
            demoAudioSource.clip = step.audioClip;
            demoAudioSource.loop = true;
            demoAudioSource.spatialBlend = 1f; // Full 3D
            demoAudioSource.Play();

            // Move audio in a circle around the user
            float duration = step.duration > 0 ? step.duration : 8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float angle = (elapsed / duration) * 360f * audioDemoSpeed / 360f;

                Vector3 position = new Vector3(
                    Mathf.Sin(angle * Mathf.Deg2Rad) * audioDemoRadius,
                    0f,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * audioDemoRadius
                );

                audioTarget.localPosition = position;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Stop audio
            demoAudioSource.Stop();

            // Update description
            if (descriptionText != null)
            {
                descriptionText.text = step.description + "\n\n✅ Spatial audio demo complete!";
            }

            // Auto-progress after a moment
            yield return new WaitForSeconds(1f);
            NextStep();
        }

        private void PlayStepAudio(AudioClip clip)
        {
            if (demoAudioSource != null && clip != null)
            {
                demoAudioSource.clip = clip;
                demoAudioSource.loop = false;
                demoAudioSource.spatialBlend = 0f; // 2D audio for instructions
                demoAudioSource.Play();
            }
        }

        private IEnumerator AutoProgressAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (isRunning) // Only progress if tutorial is still running
                NextStep();
        }

        // -----------------------------------------------
        // Navigation Methods
        // -----------------------------------------------

        public void NextStep()
        {
            if (!isRunning) return;

            currentStepIndex++;
            ShowCurrentStep();
        }

        public void PreviousStep()
        {
            if (!isRunning || currentStepIndex <= 0) return;

            currentStepIndex--;
            ShowCurrentStep();
        }

        public void SkipTutorial()
        {
            Debug.Log("Tutorial skipped by user");
            CompleteTutorial();
        }

        private void CompleteTutorial()
        {
            Debug.Log("Tutorial completed");

            isRunning = false;

            // Stop any playing audio
            if (demoAudioSource != null)
                demoAudioSource.Stop();

            // Mark tutorial as completed
            PlayerPrefs.SetString("TutorialCompleted", "true");

            // Notify GameManager
            //if (GameManager.Instance != null)
            //{
            //    GameManager.Instance.OnTutorialComplete();
            //}
        }

        // -----------------------------------------------
        // UI Updates
        // -----------------------------------------------

        private void UpdateButtonVisibility()
        {
            bool isFirstStep = currentStepIndex == 0;
            bool isLastStep = currentStepIndex >= tutorialSteps.Count - 1;

            if (previousButton != null)
                previousButton.gameObject.SetActive(!isFirstStep);

            if (nextButton != null)
            {
                nextButton.gameObject.SetActive(true);
                var buttonText = nextButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                    buttonText.text = isLastStep ? "Finish" : "Next";
            }

            if (skipButton != null)
                skipButton.gameObject.SetActive(!isLastStep);
        }

        // -----------------------------------------------
        // Public Methods for GameManager
        // -----------------------------------------------

        public void ShowTutorialPrompt()
        {
            // This could show a popup asking if user wants tutorial
            // For now, just start tutorial
            StartTutorial();
        }

        // -----------------------------------------------
        // Cleanup
        // -----------------------------------------------

        private void OnDestroy()
        {
            isRunning = false;

            if (demoAudioSource != null)
                demoAudioSource.Stop();
        }
    }
}