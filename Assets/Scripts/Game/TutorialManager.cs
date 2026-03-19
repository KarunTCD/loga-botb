using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using System;

namespace LoGa.LudoEngine.Game
{
    /// <summary>
    /// Tutorial Manager - FULLY CALLBACK-DRIVEN
    /// NO Update() loop - all transitions via FMOD timeline marker callbacks
    /// Reuses: POIManager (navigation), GameManager (combat + berry)
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        #region Tutorial Phase Definition

        public enum TutorialPhase
        {
            Inactive,
            Introduction,
            NavigationStart,
            TargetLocking,
            TargetLocked,
            Approaching,
            ProximityReached,
            CharacterFound,
            ListeningToDialogue,
            InteractionComplete,
            RewardExplanation,       // LONG tutorial only
            CombatIntroduction,      // LONG tutorial only
            CombatInProgress,        // LONG tutorial only
            CombatComplete,          // LONG tutorial only
            BerryRecovery,           // LONG tutorial only
            Complete
        }

        #endregion

        #region Serialized Fields

        [Header("Tutorial Configuration")]
        [SerializeField] private bool enableDebugLogs = true;

        [Header("Manager References")]
        [SerializeField] private POIManager poiManager;
        [SerializeField] private GameManager gameManager;

        [Header("Phase Timing")]
        [SerializeField] private float introductionDelay = 1.0f;
        [SerializeField] private float navigationStartDelay = 12.0f;
        [SerializeField] private float targetLockingTransitionDelay = 4.0f;

        #endregion

        #region State

        private TutorialPhase currentPhase = TutorialPhase.Inactive;
        private bool isActive = false;

        // Navigation tracking
        private bool hasLockedOnce = false;
        private bool hasLostLock = false;
        
        // Combat tracking
        private int attacksCompleted = 0;
        private int consecutiveDefensesAchieved = 0;
        private bool hasPlayedFirstSuccessDialogue = false;

        // Marker callback flags - what to do when current dialogue finishes
        private bool shouldPlayRewardNext = false;
        private bool shouldStartCombatNext = false;
        private bool shouldStartBerryPhaseNext = false;
        private bool shouldPlayFinalMessageNext = false;
        private bool shouldCompleteTutorialNext = false;

        #endregion

        #region References

        private IAudioService audioService;
        private POI tutorialPOI;

        // FMOD narrator
        private EventInstance narratorInstance;
        private int currentDialogueID = -1;

        // Pending dialogue (for preDelay)
        private string pendingInvokeMethod = null;
        private GameDataService.TutorialDialogueConfig pendingDialogueConfig;

        // Static reference for marker callback
        private static TutorialManager instance;

        #endregion

        #region Initialization

        private void Awake()
        {
            instance = this;

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

            // Subscribe to POIManager events (navigation)
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

            // Subscribe to GameManager events (combat + berry)
            if (gameManager != null)
            {
                gameManager.TutorialAttackCompleted += OnTutorialAttackCompleted;
                gameManager.TutorialCombatCompleted += OnTutorialCombatCompleted;
                gameManager.TutorialBerryCollected += OnTutorialBerryCollected;

                LogDebug("Subscribed to GameManager tutorial events");
            }

            isActive = true;
            currentPhase = TutorialPhase.Inactive;

            // Reset state
            hasLockedOnce = false;
            hasLostLock = false;
            currentDialogueID = -1;
            attacksCompleted = 0;
            consecutiveDefensesAchieved = 0;
            hasPlayedFirstSuccessDialogue = false;
            
            // Reset callback flags
            shouldPlayRewardNext = false;
            shouldStartCombatNext = false;
            shouldStartBerryPhaseNext = false;
            shouldPlayFinalMessageNext = false;
            shouldCompleteTutorialNext = false;

            // Find tutorial POI (spawned by POIManager.EnterTutorialMode())
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

            // Unsubscribe from POIManager events
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

            // Unsubscribe from GameManager events
            if (gameManager != null)
            {
                gameManager.TutorialAttackCompleted -= OnTutorialAttackCompleted;
                gameManager.TutorialCombatCompleted -= OnTutorialCombatCompleted;
                gameManager.TutorialBerryCollected -= OnTutorialBerryCollected; 
            }

            // Stop narrator audio
            if (audioService != null && audioService.IsInstanceValid(narratorInstance))
            {
                audioService.StopAudio(narratorInstance, false);
            }

            CancelInvoke();
        }

        #endregion

        #region Phase Progression - Navigation

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

            if (currentPhase == TutorialPhase.TargetLocked)
            {
                LogDebug("Player made significant progress - transitioning to Approaching");
                StartApproachingPhase();
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

        #region Phase Progression - Combat

        private void StartRewardPhase()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.RewardExplanation;
            LogDebug("Phase: RewardExplanation");

            // Set flag for combat introduction (triggered by marker callback)
            shouldStartCombatNext = true;
            PlayNarratorDialogue("rewardExplanation");
        }

        private void StartCombatIntroduction()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.CombatIntroduction;
            LogDebug("Phase: CombatIntroduction - mercenary warning");

            PlayNarratorDialogue("mercenaryWarning");

            // After warning finishes, start combat
            Invoke(nameof(TriggerTutorialCombat), 3.0f);
        }

        private void TriggerTutorialCombat()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.CombatInProgress;
            LogDebug("Phase: CombatInProgress");

            // Tell GameManager to start tutorial combat
            if (gameManager != null)
            {
                gameManager.StartTutorialCombat();
            }
        }

        private void OnTutorialAttackCompleted(int attackNumber, bool wasDefended, int consecutiveDefenses)
        {
            if (!isActive || currentPhase != TutorialPhase.CombatInProgress) return;

            attacksCompleted = attackNumber;
            consecutiveDefensesAchieved = consecutiveDefenses;

            LogDebug($"Attack {attackNumber} complete - defended: {wasDefended}, consecutive: {consecutiveDefenses}");

            // ATTACK 1: Always hits (unavoidable) → afterFirstHit
            if (attackNumber == 1)
            {
                PlayNarratorDialogue("afterFirstHit");
                return;
            }

            // ATTACK 2+: Conditional feedback
            if (wasDefended)
            {
                // First successful defense → defenseSuccess1
                if (consecutiveDefenses == 1 && !hasPlayedFirstSuccessDialogue)
                {
                    hasPlayedFirstSuccessDialogue = true;
                    PlayNarratorDialogue("defenseSuccess1");
                }
                // Second consecutive defense → defenseSuccessFinal (combat ends after this)
                else if (consecutiveDefenses == 2)
                {
                    PlayNarratorDialogue("defenseSuccessFinal");
                }
            }
            else
            {
                // Failed defense → pick ONE random fail dialogue
                string[] failOptions = { "defenseFail1", "defenseFail2", "defenseFail3", "defenseFail4" };
                string randomFail = failOptions[UnityEngine.Random.Range(0, failOptions.Length)];
                PlayNarratorDialogue(randomFail);
            }
        }

        private void OnTutorialCombatCompleted()
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.CombatComplete;
            LogDebug("Phase: CombatComplete");

            // Set flag for berry phase (triggered by marker callback)
            shouldStartBerryPhaseNext = true;
            PlayNarratorDialogue("combatComplete");
        }

        #endregion

        #region Phase Progression - Berry

        private void StartBerryPhase()
        {
            if (!isActive) return;

            // Check if player needs healing
            int playerHealth = gameManager?.PlayerHealth ?? 3;
            int maxHealth = 3;

            if (playerHealth >= maxHealth)
            {
                LogDebug("Player at full health - skipping berry phase, going to completion");
                shouldPlayFinalMessageNext = true;
                PlayNarratorDialogue("complete");
                return;
            }

            currentPhase = TutorialPhase.BerryRecovery;
            LogDebug("Phase: BerryRecovery");

            PlayNarratorDialogue("lowHealthBerryIntro");

            // Tell GameManager to spawn berry after dialogue
            Invoke(nameof(TriggerBerrySpawn), 2.0f);
        }

        private void TriggerBerrySpawn()
        {
            if (!isActive) return;

            LogDebug("Telling GameManager to spawn berry");

            if (gameManager != null)
            {
                gameManager.StartTutorialRecovery();
            }
        }

        private void OnTutorialBerryCollected()
        {
            if (!isActive || currentPhase != TutorialPhase.BerryRecovery) return;

            LogDebug("Tutorial berry collected");

            // Set flag for final message (triggered by marker callback)
            shouldPlayFinalMessageNext = true;
            PlayNarratorDialogue("berryCollected");
        }

        #endregion

        #region POI Event Handlers (Navigation)

        private void OnTutorialTargetLocked(POI poi)
        {
            if (!isActive || currentPhase != TutorialPhase.TargetLocking) return;
            OnTargetLocked();
        }

        private void OnTutorialTargetUnlocked(POI poi)
        {
            if (!isActive) return;
            OnTargetLost();
        }

        private void OnTutorialProgressMade(POI poi, float progressDistance)
        {
            if (!isActive) return;
            if (currentPhase != TutorialPhase.TargetLocked && currentPhase != TutorialPhase.Approaching) return;
            OnProgressMade();
        }

        private void OnTutorialProximityEntered(POI poi)
        {
            if (!isActive) return;

            currentPhase = TutorialPhase.ProximityReached;
            PlayNarratorDialogue("proximityReached");
        }

        private void OnTutorialProximityExited(POI poi)
        {
            LogDebug("Player left proximity");
        }

        private void OnTutorialInnerZoneEntered(POI poi)
        {
            if (!isActive || currentPhase != TutorialPhase.ProximityReached) return;

            currentPhase = TutorialPhase.CharacterFound;
            PlayNarratorDialogue("characterFound");

            Invoke(nameof(TransitionToListeningToDialogue), 0.1f);
        }

        private void TransitionToListeningToDialogue()
        {
            if (gameManager != null && gameManager.IsSuspended)
            {
                Invoke(nameof(TransitionToListeningToDialogue), 0.1f);
                return;
            }

            currentPhase = TutorialPhase.ListeningToDialogue;
        }

        private void OnTutorialNarrationComplete(POI poi)
        {
            if (!isActive || currentPhase != TutorialPhase.ListeningToDialogue) return;

            LogDebug("Character dialogue completed");

            var gameDataService = ServiceLocator.GetService<IGameDataService>();
            string tutorialType = gameDataService?.Tutorial?.tutorialType ?? "short";

            if (tutorialType == "short")
            {
                // SHORT: interactionComplete → complete (triggered by marker callback)
                shouldPlayFinalMessageNext = true;
                PlayNarratorDialogue("interactionComplete");
            }
            else
            {
                // LONG: interactionComplete → rewardExplanation (triggered by marker callback)
                shouldPlayRewardNext = true;
                PlayNarratorDialogue("interactionComplete");
            }
        }

        #endregion

        #region Narrator Dialogue System

        private void PlayNarratorDialogue(string dialogueKey)
        {
            if (audioService == null || !audioService.IsInstanceValid(narratorInstance))
            {
                Debug.LogError("Cannot play narration - audio service or instance invalid");
                return;
            }

            GameDataService.TutorialDialogueConfig config = GetDialogueConfig(dialogueKey);
            if (config == null)
            {
                Debug.LogError($"No config found for dialogue: {dialogueKey}");
                return;
            }

            if (config.id == currentDialogueID)
            {
                Debug.Log($"Dialogue {config.id} already playing - ignoring");
                return;
            }

            if (currentDialogueID != -1 && config.id != currentDialogueID)
            {
                Debug.Log($"Interrupting DialogueID {currentDialogueID} with {config.id}");
                audioService.StopAudio(narratorInstance, false);
            }

            if (!string.IsNullOrEmpty(pendingInvokeMethod))
            {
                CancelInvoke(pendingInvokeMethod);
                pendingInvokeMethod = null;
            }

            if (config.preDelay > 0f)
            {
                Debug.Log($"Scheduling narration in {config.preDelay}s");
                pendingInvokeMethod = nameof(PlayNarrationAudioDelayed);
                Invoke(pendingInvokeMethod, config.preDelay);
                pendingDialogueConfig = config;
            }
            else
            {
                PlayNarrationAudio(config);
            }
        }

        private void PlayNarrationAudioDelayed()
        {
            pendingInvokeMethod = null;
            if (pendingDialogueConfig != null)
            {
                PlayNarrationAudio(pendingDialogueConfig);
                pendingDialogueConfig = null;
            }
        }

        private void PlayNarrationAudio(GameDataService.TutorialDialogueConfig config)
        {
            Debug.Log($"[TutorialManager] Playing narrator dialogue ID: {config.id}");

            if (config.suspendGameplay && gameManager != null)
            {
                gameManager.SuspendGameplay(GameManager.SuspensionReason.Tutorial);
            }

            currentDialogueID = config.id;

            narratorInstance.setCallback(OnNarratorMarkerCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
            audioService.SetParameter(narratorInstance, "DialogueID", config.id);
            audioService.PlayAudio(narratorInstance, Vector3.zero);

            LogDebug($"Playing narrator dialogue ID: {config.id} (suspended: {config.suspendGameplay})");
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnNarratorMarkerCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
            {
                // Resume gameplay
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.ResumeGameplay(GameManager.SuspensionReason.Tutorial);
                }

                // Handle phase transitions via callback flags
                if (instance != null && instance.isActive)
                {
                    if (instance.shouldPlayRewardNext)
                    {
                        instance.shouldPlayRewardNext = false;
                        instance.StartRewardPhase();
                    }
                    else if (instance.shouldStartCombatNext)
                    {
                        instance.shouldStartCombatNext = false;
                        instance.StartCombatIntroduction();
                    }
                    else if (instance.shouldStartBerryPhaseNext)
                    {
                        instance.shouldStartBerryPhaseNext = false;
                        instance.StartBerryPhase();
                    }
                    else if (instance.shouldPlayFinalMessageNext)
                    {
                        instance.shouldPlayFinalMessageNext = false;
                        instance.PlayNarratorDialogue("complete");
                        
                        // Completion will be triggered by the NEXT marker callback
                        // Set a flag so the next marker knows to complete tutorial
                        instance.shouldCompleteTutorialNext = true;
                    }
                    else if (instance.shouldCompleteTutorialNext)
                    {
                        // FINAL DIALOGUE FINISHED - Complete tutorial
                        instance.shouldCompleteTutorialNext = false;
                        instance.CompleteTutorial();
                    }
                }

                Debug.Log("TutorialManager: Narrator dialogue finished (timeline marker) - gameplay resumed");
            }

            return FMOD.RESULT.OK;
        }

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
                "rewardExplanation" => dialogues.rewardExplanation,
                "mercenaryWarning" => dialogues.mercenaryWarning,
                "afterFirstHit" => dialogues.afterFirstHit,
                "attackIncoming2" => dialogues.attackIncoming2,
                "defenseSuccess1" => dialogues.defenseSuccess1,
                "defenseFail1" => dialogues.defenseFail1,
                "attackIncoming3" => dialogues.attackIncoming3,
                "defenseSuccessFinal" => dialogues.defenseSuccessFinal,
                "defenseFail2" => dialogues.defenseFail2,
                "defenseFail3" => dialogues.defenseFail3,
                "defenseFail4" => dialogues.defenseFail4,
                "combatComplete" => dialogues.combatComplete,
                "lowHealthBerryIntro" => dialogues.lowHealthBerryIntro,
                "berryCollected" => dialogues.berryCollected,
                _ => null
            };
        }

        #endregion

        #region Tutorial Completion

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
            
            shouldPlayRewardNext = false;
            shouldStartCombatNext = false;
            shouldStartBerryPhaseNext = false;
            shouldPlayFinalMessageNext = false;
            shouldCompleteTutorialNext = false;
        }

        private void OnDestroy()
        {
            StopTutorial();

            if (audioService != null && narratorInstance.handle != IntPtr.Zero)
            {
                audioService.ReleaseAudio(narratorInstance);
            }

            if (instance == this)
            {
                instance = null;
            }
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
    }
}