using UnityEngine;
using System.Collections.Generic;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;

namespace LoGa.LudoEngine.Game
{
    /// <summary>
    /// Master Tutorial Controller - Controls all tutorial flow
    /// POIManager, CombatManager, etc are servants that report to this
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        #region Tutorial Phase Definition

        public enum TutorialPhase
        {
            Inactive,              // Tutorial not running
            Introduction,          // Welcome message
            NavigationStart,       // "Listen for the sound..."
            TargetLocking,         // Waiting for player to lock target
            TargetLocked,          // Player locked target successfully
            Approaching,           // Player walking toward target (with progress tracking)
            ProximityReached,      // Player entered 20m music zone
            CharacterFound,        // Player entered 5m dialogue zone
            ListeningToDialogue,   // Character speaking
            Complete               // Tutorial finished
        }

        #endregion

        #region Serialized Fields

        [Header("Tutorial Configuration")]
        [SerializeField] private bool enableDebugLogs = true;

        [Header("Phase Timing")]
        [SerializeField] private float introductionDelay = 1.0f;
        [SerializeField] private float navigationStartDelay = 12.0f;
        [SerializeField] private float targetLockingTransitionDelay = 4.0f;

        #endregion

        #region State

        private TutorialPhase currentPhase = TutorialPhase.Inactive;
        private bool isActive = false;
        private bool hasLockedOnce = false;
        private bool hasLostLock = false;
        private bool waitingForInteractionComplete = false;
        private bool waitingForFinalComplete = false;

        // Track distance for progress detection
        private float distanceWhenLocked = 0f;
        private const float PROGRESS_THRESHOLD = 15f;

        #endregion

        #region References

        [Header("Manager References")]
        [SerializeField] private POIManager poiManager;
        [SerializeField] private GameManager gameManager;

        private IAudioService audioService;

        // Tutorial POI reference
        private POI tutorialPOI;

        // FMOD narrator instance
        private EventInstance narratorInstance;

        // Dialogue management
        private int currentDialogueID = -1;

        // NEW: Pending invoke for preDelay handling
        private string pendingInvokeMethod = null;

        #endregion

        #region Initialization

        private void Awake()
        {
            if (poiManager == null)
            {
                Debug.LogError("TutorialManager: POIManager reference not assigned!");
            }

            if (gameManager == null)
            {
                Debug.LogError("TutorialManager: GameManager reference not assigned!");
            }
        }

        private async void Start()
        {
            LogDebug("Initializing TutorialManager");

            audioService = await ServiceLocator.GetInitializedService<IAudioService>();

            if (audioService == null)
            {
                Debug.LogError("TutorialManager: AudioService not available!");
                return;
            }

            LogDebug("TutorialManager initialized - ready to start tutorial");
        }

        private void InitializeNarratorAudio()
        {
            LogDebug("Initializing narrator audio");

            var gameDataService = ServiceLocator.GetService<IGameDataService>();

            if (gameDataService?.Tutorial == null)
            {
                Debug.LogError("TutorialManager: No tutorial configuration in site JSON!");
                return;
            }

            string narratorEventPath = gameDataService.Tutorial.narratorEvent;

            if (string.IsNullOrEmpty(narratorEventPath))
            {
                Debug.LogError("TutorialManager: Narrator event path is empty!");
                return;
            }

            EventReference narratorEventRef = gameDataService.GetAudioEventReference(narratorEventPath);

            if (narratorEventRef.IsNull)
            {
                Debug.LogError($"TutorialManager: Failed to load narrator event: {narratorEventPath}");
                return;
            }

            narratorInstance = audioService.CreateAudioInstance(narratorEventRef);

            if (narratorInstance.handle == IntPtr.Zero)
            {
                Debug.LogError("TutorialManager: Failed to create narrator instance!");
                return;
            }

            LogDebug("Narrator audio initialized successfully");
        }

        #endregion

        #region Tutorial Control

        public void StartTutorial()
        {
            if (isActive)
            {
                Debug.LogWarning("TutorialManager: Tutorial already active!");
                return;
            }

            LogDebug("Starting tutorial");

            if (audioService == null)
            {
                Debug.LogError("TutorialManager: AudioService not available!");
                return;
            }

            // Initialize narrator audio
            if (narratorInstance.handle == IntPtr.Zero)
            {
                InitializeNarratorAudio();

                if (narratorInstance.handle == IntPtr.Zero)
                {
                    Debug.LogError("TutorialManager: Failed to initialize narrator audio!");
                    return;
                }
            }

            // Subscribe to POI events
            if (poiManager != null)
            {
                poiManager.TutorialPOIProximityEntered += OnTutorialProximityEntered;
                poiManager.TutorialPOIProximityExited += OnTutorialProximityExited;
                poiManager.TutorialPOIInnerZoneEntered += OnTutorialInnerZoneEntered;
                poiManager.TutorialPOINarrationComplete += OnTutorialNarrationComplete;
                poiManager.TutorialPOITargetLocked += OnTutorialTargetLocked;
                poiManager.TutorialPOITargetUnlocked += OnTutorialTargetUnlocked;
                poiManager.TutorialPOIProgressMade += OnTutorialProgressMade;

                LogDebug("Subscribed to POIManager tutorial events");
            }

            isActive = true;
            currentPhase = TutorialPhase.Inactive;

            // Reset state
            hasLockedOnce = false;
            hasLostLock = false;
            currentDialogueID = -1;
            waitingForInteractionComplete = false;
            waitingForFinalComplete = false;

            // Find tutorial POI
            tutorialPOI = FindTutorialPOI();
            if (tutorialPOI == null)
            {
                Debug.LogError("TutorialManager: Cannot find tutorial POI!");
                return;
            }

            // Start with introduction
            Invoke(nameof(StartIntroduction), introductionDelay);
        }

        public void StopTutorial()
        {
            if (!isActive) return;

            LogDebug("Stopping tutorial");

            isActive = false;
            currentPhase = TutorialPhase.Inactive;
            tutorialPOI = null;

            // Unsubscribe from POI events
            if (poiManager != null)
            {
                poiManager.TutorialPOIProximityEntered -= OnTutorialProximityEntered;
                poiManager.TutorialPOIProximityExited -= OnTutorialProximityExited;
                poiManager.TutorialPOIInnerZoneEntered -= OnTutorialInnerZoneEntered;
                poiManager.TutorialPOINarrationComplete -= OnTutorialNarrationComplete;
                poiManager.TutorialPOITargetLocked -= OnTutorialTargetLocked;
                poiManager.TutorialPOITargetUnlocked -= OnTutorialTargetUnlocked;
                poiManager.TutorialPOIProgressMade -= OnTutorialProgressMade;
            }

            // Stop narrator audio
            if (audioService != null && audioService.IsInstanceValid(narratorInstance))
            {
                audioService.StopAudio(narratorInstance, false);
            }

            CancelInvoke();
        }

        #endregion

        #region Phase Progression

        private void StartIntroduction()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.Introduction;
            LogDebug("Phase: Introduction");

            PlayNarratorDialogue("introduction");

            Invoke(nameof(StartNavigationPhase), navigationStartDelay);
        }

        private void StartNavigationPhase()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.NavigationStart;
            LogDebug("Phase: NavigationStart");

            PlayNarratorDialogue("navigationStart");

            Invoke(nameof(TransitionToTargetLocking), targetLockingTransitionDelay);
        }

        private void TransitionToTargetLocking()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.TargetLocking;
            LogDebug("Phase: TargetLocking - waiting for player action");
        }

        private void OnTargetLocked()
        {
            LogDebug($"OnTargetLocked - Phase: {currentPhase}, HasLockedOnce: {hasLockedOnce}");

            if (!isActive || currentPhase != TutorialPhase.TargetLocking) return;

            currentPhase = TutorialPhase.TargetLocked;
            LogDebug("Phase: TargetLocked");

            // Record distance
            if (tutorialPOI != null && poiManager != null && poiManager.poiDataCache.TryGetValue(tutorialPOI, out POIUpdateData data))
            {
                distanceWhenLocked = data.distance;
                LogDebug($"Distance when locked: {distanceWhenLocked:F1}m");
            }

            if (!hasLockedOnce)
            {
                hasLockedOnce = true;
                PlayNarratorDialogue("targetLockSuccess");
            }
            else if (hasLostLock)
            {
                PlayNarratorDialogue("targetRelockSuccess");
            }
        }

        private void OnTargetLost()
        {
            LogDebug($"OnTargetLost - Phase: {currentPhase}");

            if (!isActive || !hasLockedOnce) return;

            PlayNarratorDialogue("targetLost");
            hasLostLock = true;
            currentPhase = TutorialPhase.TargetLocking;
        }

        private void OnProgressMade()
        {
            if (!isActive) return;

            // Transition to Approaching if not already there
            if (currentPhase == TutorialPhase.TargetLocked)
            {
                LogDebug("Player made significant progress - transitioning to Approaching");
                StartApproachingPhase();
            }
            else if (currentPhase == TutorialPhase.Approaching)
            {
                // Already in approaching - just log (dialogue won't repeat due to currentDialogueID check)
                LogDebug("Player making more progress (already approaching - dialogue won't repeat)");
            }
        }

        private void StartApproachingPhase()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.Approaching;
            LogDebug("Phase: Approaching");

            PlayNarratorDialogue("approaching");
        }

        #endregion

        #region POI Event Handlers

        private void OnTutorialTargetLocked(POI poi)
        {
            LogDebug($"OnTutorialTargetLocked - Phase: {currentPhase}");

            if (!isActive || currentPhase != TutorialPhase.TargetLocking) return;

            OnTargetLocked();
        }

        private void OnTutorialTargetUnlocked(POI poi)
        {
            LogDebug($"OnTutorialTargetUnlocked - Phase: {currentPhase}");

            if (!isActive) return;

            OnTargetLost();
        }

        private void OnTutorialProgressMade(POI poi, float progressDistance)
        {
            LogDebug($"OnTutorialProgressMade - Phase: {currentPhase}, Progress: {progressDistance:F1}m");

            // Allow in both TargetLocked AND Approaching phases
            if (!isActive) return;

            if (currentPhase != TutorialPhase.TargetLocked && currentPhase != TutorialPhase.Approaching)
            {
                LogDebug($"   Ignoring progress - wrong phase: {currentPhase}");
                return;
            }

            OnProgressMade();
        }

        private void OnTutorialProximityEntered(POI poi)
        {
            LogDebug($"OnTutorialProximityEntered - Phase: {currentPhase}");

            if (!isActive) return;

            // Transition to proximity phase
            currentPhase = TutorialPhase.ProximityReached;
            LogDebug("Phase: ProximityReached (20m music zone)");

            // Play narrator dialogue (music starts automatically in POI.UpdateProximity)
            PlayNarratorDialogue("proximityReached");
        }

        private void OnTutorialProximityExited(POI poi)
        {
            LogDebug("OnTutorialProximityExited - player left proximity");
            // Could handle if needed
        }

        private void OnTutorialInnerZoneEntered(POI poi)
        {
            Debug.Log($" OnTutorialInnerZoneEntered - Phase: {currentPhase}");

            if (!isActive || currentPhase != TutorialPhase.ProximityReached)
            {
                Debug.LogWarning($" BLOCKED - Phase: {currentPhase}, Expected: ProximityReached");
                return;
            }

            Debug.Log(" Inner zone entered - transitioning to CharacterFound");
            currentPhase = TutorialPhase.CharacterFound;
            LogDebug("Phase: CharacterFound (5m dialogue zone)");

            // Play "let's hear what they have to say" narrator message
            PlayNarratorDialogue("characterFound");

            // After characterFound narration finishes, transition to ListeningToDialogue
            // This happens automatically when the narrator callback resumes gameplay
            Invoke(nameof(TransitionToListeningToDialogue), 0.1f); // Small delay to let narration start
        }

        private void TransitionToListeningToDialogue()
        {
            // Wait for characterFound narration to complete
            if (gameManager != null && gameManager.IsSuspended)
            {
                // Still playing narration, wait
                Invoke(nameof(TransitionToListeningToDialogue), 0.1f);
                return;
            }

            Debug.Log("🎭 Transitioning to ListeningToDialogue phase");
            currentPhase = TutorialPhase.ListeningToDialogue;
            LogDebug("Phase: ListeningToDialogue - character dialogue should be playing");
        }

        private void OnTutorialNarrationComplete(POI poi)
        {
            Debug.Log($"🎬 OnTutorialNarrationComplete FIRED - POI: {poi?.characterName ?? "NULL"}");
            Debug.Log($"   isActive: {isActive}");
            Debug.Log($"   currentPhase: {currentPhase}");
            Debug.Log($"   Expected phase: {TutorialPhase.ListeningToDialogue}");
            Debug.Log($"   Phase check passes? {isActive && currentPhase == TutorialPhase.ListeningToDialogue}");

            if (!isActive || currentPhase != TutorialPhase.ListeningToDialogue)
            {
                Debug.LogWarning($" BLOCKED - Not in correct phase for completion");
                return;
            }

            // Character dialogue finished
            LogDebug("Character dialogue completed");

            // Play completion message
            Debug.Log(" About to play interactionComplete dialogue");
            PlayNarratorDialogue("interactionComplete");

            // Transition to next phase based on tutorial type
            var gameDataService = ServiceLocator.GetService<IGameDataService>();
            string tutorialType = gameDataService?.Tutorial?.tutorialType ?? "short";

            if (tutorialType == "short")
            {
                Debug.Log(" Short tutorial - will transition to complete after interactionComplete");
                waitingForInteractionComplete = true;
            }
            else
            {
                Debug.Log(" Long tutorial - will transition to combat/berry phases after interactionComplete");
                // For long tutorial, transition to combat phase
                // (This will be implemented when combat tutorial is added)
                currentPhase = TutorialPhase.Complete; // Placeholder for now
            }
        }

        private void CompleteTutorial()
        {
            LogDebug("Tutorial completed!");

            PlayerPrefs.SetString("TutorialCompleted", "true");
            PlayerPrefs.Save();

            if (gameManager != null)
            {
                gameManager.CompleteTutorial();
            }
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            if (!isActive || poiManager == null) return;

            // Cache tutorial POI if needed
            if (tutorialPOI == null)
            {
                tutorialPOI = FindTutorialPOI();
                if (tutorialPOI == null) return;
            }

            // SHORT TUTORIAL ONLY: Check if we should play final message (after interactionComplete finishes)
            if (waitingForInteractionComplete)
            {
                bool isSuspended = gameManager != null && gameManager.IsSuspended;

                if (Time.frameCount % 60 == 0) // Log once per second
                {
                    Debug.Log($" [SHORT TUTORIAL] Waiting for interactionComplete - IsSuspended: {isSuspended}");
                }

                if (gameManager != null && !gameManager.IsSuspended)
                {
                    Debug.Log(" [SHORT TUTORIAL] InteractionComplete finished - playing final message");
                    waitingForInteractionComplete = false;
                    waitingForFinalComplete = true;
                    PlayNarratorDialogue("complete");
                }
            }

            // SHORT TUTORIAL ONLY: Check if tutorial is complete (after complete dialogue finishes)
            if (waitingForFinalComplete)
            {
                bool isSuspended = gameManager != null && gameManager.IsSuspended;

                if (Time.frameCount % 60 == 0) // Log once per second
                {
                    Debug.Log($"⏳ [SHORT TUTORIAL] Waiting for complete - IsSuspended: {isSuspended}");
                }

                if (gameManager != null && !gameManager.IsSuspended)
                {
                    Debug.Log(" [SHORT TUTORIAL] Final message finished - completing tutorial");
                    waitingForFinalComplete = false;
                    CompleteTutorial();
                }
            }
        }

        #endregion

        #region Audio Management

        /// <summary>
        /// Play narrator dialogue with new config system
        /// </summary>
        private void PlayNarratorDialogue(string dialogueKey)
        {
            Debug.Log($" PlayNarratorDialogue CALLED - Key: {dialogueKey}");

            if (audioService == null || !audioService.IsInstanceValid(narratorInstance))
            {
                Debug.LogError("Cannot play narration - audio service or instance invalid");
                return;
            }

            // Get dialogue config
            GameDataService.TutorialDialogueConfig config = GetDialogueConfig(dialogueKey);
            if (config == null)
            {
                Debug.LogError($"No config found for dialogue: {dialogueKey}");
                return;
            }

            // Check if this is the same dialogue already playing
            if (config.id == currentDialogueID)
            {
                Debug.Log($"Dialogue {config.id} already playing - ignoring");
                return;
            }

            // Stop previous narrator dialogue if different
            if (currentDialogueID != -1 && config.id != currentDialogueID)
            {
                Debug.Log($" Interrupting DialogueID {currentDialogueID} with {config.id}");
                audioService.StopAudio(narratorInstance, false);
            }

            // Cancel any pending invoke from previous dialogue
            if (!string.IsNullOrEmpty(pendingInvokeMethod))
            {
                CancelInvoke(pendingInvokeMethod);
                pendingInvokeMethod = null;
            }

            // Schedule narration to play after preDelay
            if (config.preDelay > 0f)
            {
                Debug.Log($" Scheduling narration in {config.preDelay}s (update keeps running)");
                pendingInvokeMethod = nameof(PlayNarrationAudioDelayed);
                Invoke(pendingInvokeMethod, config.preDelay);

                // Store config for delayed playback
                pendingDialogueConfig = config;
            }
            else
            {
                PlayNarrationAudio(config);
            }
        }

        // NEW: Delayed playback method
        private GameDataService.TutorialDialogueConfig pendingDialogueConfig;

        private void PlayNarrationAudioDelayed()
        {
            pendingInvokeMethod = null;
            if (pendingDialogueConfig != null)
            {
                PlayNarrationAudio(pendingDialogueConfig);
                pendingDialogueConfig = null;
            }
        }

        /// <summary>
        /// Actually play the narration audio (after preDelay)
        /// </summary>
        private void PlayNarrationAudio(GameDataService.TutorialDialogueConfig config)
        {
            Debug.Log($" Playing narration - DialogueID: {config.id}, Suspend: {config.suspendGameplay}");

            // Suspend gameplay if configured (AFTER preDelay)
            if (config.suspendGameplay && gameManager != null)
            {
                Debug.Log($" Suspending gameplay for DialogueID {config.id}");
                gameManager.SuspendGameplay(GameManager.SuspensionReason.Tutorial);
            }

            currentDialogueID = config.id;

            // Set callback to detect timeline marker (dialogue completion)
            narratorInstance.setCallback(OnNarratorMarkerCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);

            // Set which dialogue to play
            audioService.SetParameter(narratorInstance, "DialogueID", config.id);

            // Play narrator (on Voice bus - not paused)
            audioService.PlayAudio(narratorInstance, Vector3.zero);

            LogDebug($"Playing narrator dialogue ID: {config.id} (suspended: {config.suspendGameplay})");
        }

        /// <summary>
        /// FMOD callback - fired when narrator dialogue timeline marker is reached
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnNarratorMarkerCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            Debug.Log($" OnNarratorMarkerCallback FIRED - Type: {type}");

            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                Debug.Log($" Timeline marker detected - about to call ResumeGameplay(Tutorial)");

                // Resume gameplay when dialogue finishes
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.ResumeGameplay(GameManager.SuspensionReason.Tutorial);
                }

                Debug.Log("TutorialManager: Narrator dialogue finished (timeline marker) - gameplay resumed");

                // NEW: Trigger postDelay action if pending
                // We can't access instance variables from static callback, so use a workaround
                // The Update() loop will check for postDelay actions
            }

            return FMOD.RESULT.OK;
        }

        /// <summary>
        /// Get dialogue configuration from JSON
        /// </summary>
        private GameDataService.TutorialDialogueConfig GetDialogueConfig(string dialogueKey)
        {
            var gameDataService = ServiceLocator.GetService<IGameDataService>();

            if (gameDataService?.Tutorial?.dialogues == null)
            {
                Debug.LogError("No tutorial dialogues configuration available");
                return null;
            }

            var dialogues = gameDataService.Tutorial.dialogues;

            return dialogueKey switch
            {
                "introduction" => dialogues.introduction,
                "navigationStart" => dialogues.navigationStart,
                "targetLockSuccess" => dialogues.targetLockSuccess,
                "targetLost" => dialogues.targetLost,
                "targetRelockSuccess" => dialogues.targetRelockSuccess,
                "approaching" => dialogues.approaching,
                "proximityReached" => dialogues.proximityReached,
                "characterFound" => dialogues.characterFound,
                "interactionComplete" => dialogues.interactionComplete,
                "complete" => dialogues.complete,
                _ => null
            };
        }

        #endregion

        #region Helper Methods

        private POI FindTutorialPOI()
        {
            foreach (var poi in poiManager.activePOIs)
            {
                if (poi.characterId == "tutorial_character")
                {
                    return poi;
                }
            }
            return null;
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[TutorialManager] {message}");
            }
        }

        #endregion

        #region Public Properties

        public TutorialPhase CurrentPhase => currentPhase;
        public bool IsActive => isActive;

        #endregion

        #region Cleanup

        public void Reset()
        {
            LogDebug("Resetting tutorial manager");

            if (isActive)
            {
                StopTutorial();
            }

            if (audioService != null && narratorInstance.handle != IntPtr.Zero)
            {
                audioService.StopAudio(narratorInstance, false);
                audioService.ReleaseAudio(narratorInstance);
                narratorInstance = default(EventInstance);
            }

            currentDialogueID = -1;
            tutorialPOI = null;
            pendingDialogueConfig = null;
            pendingInvokeMethod = null;
        }

        private void OnDestroy()
        {
            StopTutorial();

            if (audioService != null && narratorInstance.handle != IntPtr.Zero)
            {
                audioService.ReleaseAudio(narratorInstance);
            }
        }

        #endregion
    }
}