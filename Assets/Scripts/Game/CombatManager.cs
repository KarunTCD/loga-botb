using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMODUnity;
using FMOD.Studio;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
    /// <summary>
    /// Owns all combat and recovery logic, health management, and related audio.
    /// GameManager remains the central command — this class only executes what
    /// GameManager delegates to it. TutorialManager events are re-fired through
    /// GameManager for full backward compatibility.
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        #region Events — re-published by GameManager for TutorialManager

        public event Action<int, bool, int> TutorialAttackCompleted;
        public event Action TutorialCombatCompleted;
        public event Action TutorialBerryCollected;

        #endregion

        #region Inspector

        [Header("Combat Settings")]
        [SerializeField] private float combatTriggerCheckInterval = 2f;

        #endregion

        #region Audio Events

        private EventReference mercenaryEncounterEvent;
        private EventReference mercenaryDefeatEvent;
        private EventReference mercenaryFootstepsEvent;
        private EventReference mercenaryAttackEvent;
        private EventReference attackImpactEvent;
        private EventReference heartbeatEvent;
        private EventReference berryAmbientEvent;
        private EventReference berryCollectionEvent;

        #endregion

        #region Audio Instances

        private EventInstance mercenaryEncounterInstance;
        private EventInstance mercenaryDefeatInstance;
        private EventInstance currentFootstepsInstance;
        private EventInstance currentAttackInstance;
        private EventInstance sharedBerryInstance;
        private EventInstance heartbeatInstance;

        #endregion

        #region State

        private int maxHealth;
        private int playerHealth;

        private List<Mercenary> activeMercenaries = new List<Mercenary>();
        private List<Berry> activeBerries = new List<Berry>();
        private float combatCheckTimer = 0f;
        private bool isInCombat = false;
        private int currentAttackIndex = 0;
        private Mercenary currentAttackingMercenary;
        private GameDataService.CombatConfiguration combatConfig;
        private GameDataService.CombatEncounter currentCombatEncounter;
        private float playerHeadingAtAttackStart = 0f;

        private bool isTutorialCombat = false;
        private int tutorialAttackNumber = 0;
        private int consecutiveDefenses = 0;

        private bool isInitialized = false;

        #endregion

        #region Service References

        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();
        private IStorageService StorageService => ServiceLocator.GetService<IStorageService>();
        private IAnalyticsService AnalyticsService => ServiceLocator.GetService<IAnalyticsService>();
        private IGameDataService GameDataService => ServiceLocator.GetService<IGameDataService>();

        #endregion

        #region Public Properties

        public int PlayerHealth => playerHealth;
        public int MaxHealth => maxHealth;
        public bool IsInCombat => isInCombat;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogError("CombatManager: Multiple instances detected!");
                Destroy(gameObject);
                return;
            }
        }

        private void Update()
        {
            if (!isInitialized) return;
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentGameState != GameManager.GameState.Running) return;
            if (GameManager.Instance.CurrentMode != GameManager.GameMode.Player &&
                GameManager.Instance.CurrentMode != GameManager.GameMode.Tutorial) return;
            if (GameManager.Instance.CurrentGameplayState != GameManager.GameplayState.Wander) return;

            combatCheckTimer += Time.deltaTime;
            if (combatCheckTimer >= combatTriggerCheckInterval)
            {
                CheckForCombatTriggers();
                combatCheckTimer = 0f;
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Called by GameManager after JSON config is loaded via CompleteSiteSelection.
        /// </summary>
        public void Initialize(GameDataService.CombatConfiguration config, int maxHp, int currentHp)
        {
            combatConfig = config;
            maxHealth = maxHp;
            playerHealth = currentHp;

            if (config != null)
            {
                InitializeCombatAudio();
                Debug.Log($"CombatManager: Initialized with combat config. Health: {playerHealth}/{maxHealth}");
            }
            else
            {
                Debug.Log("CombatManager: No combat configuration — combat disabled for this site.");
            }

            isInitialized = true;
        }

        private void InitializeCombatAudio()
        {
            if (AudioService == null)
            {
                Debug.LogWarning("CombatManager: AudioService not available — skipping combat audio");
                return;
            }

            if (combatConfig?.audioEvents == null)
            {
                Debug.Log("CombatManager: No combat audio events in config — skipping");
                return;
            }

            var audioEvents = combatConfig.audioEvents;
            int successCount = 0;

            LoadCombatEventWithInstance(audioEvents.mercenaryEncounter, ref mercenaryEncounterEvent, ref mercenaryEncounterInstance, "mercenary encounter", ref successCount);
            LoadCombatEventWithInstance(audioEvents.mercenaryDefeat,    ref mercenaryDefeatEvent,    ref mercenaryDefeatInstance,    "mercenary defeat",    ref successCount);
            LoadCombatEventWithInstance(audioEvents.heartbeat,          ref heartbeatEvent,          ref heartbeatInstance,          "heartbeat",           ref successCount);
            LoadCombatEventWithInstance(audioEvents.berryAmbient,       ref berryAmbientEvent,       ref sharedBerryInstance,        "berry ambient",       ref successCount);

            LoadCombatEventRef(audioEvents.mercenaryFootsteps, ref mercenaryFootstepsEvent, "mercenary footsteps", ref successCount);
            LoadCombatEventRef(audioEvents.mercenaryAttack,    ref mercenaryAttackEvent,    "mercenary attack",    ref successCount);
            LoadCombatEventRef(audioEvents.attackImpact,       ref attackImpactEvent,       "attack impact",       ref successCount);
            LoadCombatEventRef(audioEvents.berryCollection,    ref berryCollectionEvent,    "berry collection",    ref successCount);

            Debug.Log($"CombatManager: Combat audio initialization complete — {successCount}/8 events loaded");
        }

        private void LoadCombatEventWithInstance(string eventName, ref EventReference eventRef,
            ref EventInstance instance, string displayName, ref int successCount)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.Log($"CombatManager: No {displayName} event in JSON — skipping");
                return;
            }

            eventRef = GameDataService.GetAudioEventReference(eventName);
            if (eventRef.IsNull)
            {
                Debug.LogWarning($"CombatManager: Failed to load {displayName} event — continuing without it");
                return;
            }

            instance = AudioService.CreateAudioInstance(eventRef);
            if (instance.handle == IntPtr.Zero)
            {
                Debug.LogWarning($"CombatManager: Failed to create {displayName} instance — continuing without it");
                return;
            }

            successCount++;
            Debug.Log($"CombatManager: {displayName} instance created from JSON");
        }

        private void LoadCombatEventRef(string eventName, ref EventReference eventRef,
            string displayName, ref int successCount)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.Log($"CombatManager: No {displayName} event in JSON — skipping");
                return;
            }

            eventRef = GameDataService.GetAudioEventReference(eventName);
            if (!eventRef.IsNull)
            {
                successCount++;
                Debug.Log($"CombatManager: {displayName} event loaded from JSON");
            }
            else
            {
                Debug.LogWarning($"CombatManager: Failed to load {displayName} event — continuing without it");
            }
        }

        #endregion

        #region Health Management

        public void LoadHealth(int maxHp, int savedHp)
        {
            maxHealth = maxHp;
            playerHealth = savedHp;
            UpdateHeartbeat();
            Debug.Log($"CombatManager: Health loaded — {playerHealth}/{maxHealth}");
        }

        public void TakeDamage()
        {
            if (playerHealth <= 0) return;

            playerHealth--;
            SaveHealthToPreferences();
            UpdateHeartbeat();
            AnalyticsService?.TrackEvent($"player_hit_health_{playerHealth}");
            Debug.Log($"CombatManager: Player took damage. Health: {playerHealth}/{maxHealth}");
        }

        public void RestoreHealth(int amount = 1)
        {
            int oldHealth = playerHealth;
            playerHealth = Mathf.Min(playerHealth + amount, maxHealth);
            SaveHealthToPreferences();
            UpdateHeartbeat();

            if (playerHealth > oldHealth)
                AnalyticsService?.TrackEvent($"player_healed_to_health_{playerHealth}");

            Debug.Log($"CombatManager: Player restored health. Health: {playerHealth}/{maxHealth}");

            if (playerHealth >= maxHealth &&
                GameManager.Instance?.CurrentGameplayState == GameManager.GameplayState.Recovery)
            {
                Debug.Log("CombatManager: Player fully healed — returning to wander mode.");
                GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Wander);
            }
        }

        private void SaveHealthToPreferences()
        {
            StorageService?.Save("PlayerHealth", playerHealth);
        }

        private void UpdateHeartbeat()
        {
            if (AudioService == null)
            {
                Debug.LogWarning("CombatManager: UpdateHeartbeat() aborted — AudioService not available");
                return;
            }

            if (heartbeatInstance.handle == IntPtr.Zero)
            {
                Debug.LogWarning("CombatManager: UpdateHeartbeat() aborted — heartbeat instance null");
                return;
            }

            if (!AudioService.IsInstanceValid(heartbeatInstance))
            {
                Debug.LogWarning("CombatManager: UpdateHeartbeat() aborted — instance invalid");
                return;
            }

            try
            {
                AudioService.SetParameter(heartbeatInstance, "Health", playerHealth);
                Debug.Log($"CombatManager: Heartbeat parameter set to: {playerHealth}");

                if (playerHealth < maxHealth)
                {
                    heartbeatInstance.getPlaybackState(out PLAYBACK_STATE playbackState);
                    if (playbackState != PLAYBACK_STATE.PLAYING)
                    {
                        AudioService.PlayAudio(heartbeatInstance, Vector3.zero);
                        Debug.Log("CombatManager: Heartbeat audio started");
                    }
                }
                else
                {
                    AudioService.StopAudio(heartbeatInstance, true);
                    Debug.Log("CombatManager: Heartbeat audio stopped");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"CombatManager: Exception in UpdateHeartbeat — {e.Message}");
            }
        }

        #endregion

        #region Combat Triggers

        private void CheckForCombatTriggers()
        {
            if (StorageService == null) return;
            if (isInCombat) return;
            if (combatConfig?.encounters == null || combatConfig.encounters.Count == 0) return;

            foreach (var encounter in combatConfig.encounters)
            {
                string completionKey = $"combat_type_{encounter.combatType}_completed";
                bool isCompleted = StorageService.Load<bool>(completionKey);
                if (isCompleted) continue;

                bool hasAllRewards = encounter.requiredRewards.All(rewardId =>
                    StorageService.Load<bool>($"reward_{rewardId}_collected"));

                if (hasAllRewards)
                {
                    Debug.Log($"CombatManager: Combat trigger activated for type {encounter.combatType}!");
                    currentCombatEncounter = encounter;
                    AnalyticsService?.TrackEvent($"combat_triggered_type_{encounter.combatType}_rewards_{string.Join("_", encounter.requiredRewards)}");
                    GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Combat);
                    return;
                }
            }
        }

        #endregion

        #region Combat Flow

        public void StartCombat()
        {
            if (AudioService == null)
            {
                Debug.LogError("CombatManager: Cannot start combat — AudioService not initialized");
                GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Wander);
                return;
            }

            isInCombat = true;
            currentAttackIndex = 0;
            combatCheckTimer = 0f;
            activeMercenaries.Clear();

            if (isTutorialCombat)
            {
                tutorialAttackNumber = 0;
                consecutiveDefenses = 0;
                Debug.Log("CombatManager: Tutorial combat — skipping intro, starting attacks after delay");
                float attackDelay = combatConfig?.attackDelayAfterIntro ?? 3f;
                StartCoroutine(StartAttackAfterDelay(attackDelay));
            }
            else
            {
                Debug.Log("CombatManager: Normal combat — playing intro dialogue");
                StartMercenaryEncounter();
            }
        }

        private void StartMercenaryEncounter()
        {
            if (AudioService == null || mercenaryEncounterEvent.IsNull) return;

            try
            {
                mercenaryEncounterInstance = AudioService.CreateAudioInstance(mercenaryEncounterEvent);

                if (currentCombatEncounter != null)
                    AudioService.SetParameter(mercenaryEncounterInstance, "CombatType", currentCombatEncounter.combatType);

                mercenaryEncounterInstance.setCallback(OnMercenaryDialogueComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                mercenaryEncounterInstance.start();
                Debug.Log($"CombatManager: Mercenary encounter started — Combat Type: {currentCombatEncounter?.combatType ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"CombatManager: Error starting mercenary encounter — {e.Message}");
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnMercenaryDialogueComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("CombatManager: Mercenary dialogue completed via FMOD callback — starting attacks");
                Instance.StartCoroutine(Instance.StartAttackAfterDelay(10f));
            }
            return FMOD.RESULT.OK;
        }

        private IEnumerator StartAttackAfterDelay(float delay)
        {
            float attackDelay = combatConfig?.attackDelayAfterIntro ?? 3f;
            yield return new WaitForSeconds(attackDelay);
            currentAttackIndex = 0;
            Debug.Log("CombatManager: Starting attack sequence");
            ExecuteNextAttack();
        }

        private void ExecuteNextAttack()
        {
            if (!isTutorialCombat)
            {
                int maxAttacks = combatConfig?.attackCount ?? 3;
                if (currentAttackIndex >= maxAttacks)
                {
                    Debug.Log($"CombatManager: All {maxAttacks} attacks completed — concluding combat");
                    ConcludeCombat();
                    return;
                }
            }

            var attackingMercenary = CreateMercenaryForAttack();
            if (attackingMercenary == null)
            {
                Debug.LogError("CombatManager: Failed to create mercenary — aborting attack");
                return;
            }

            activeMercenaries.Add(attackingMercenary);

            if (isTutorialCombat)
                Debug.Log($"CombatManager: Executing tutorial attack {tutorialAttackNumber + 1} (consecutive defenses: {consecutiveDefenses})");
            else
                Debug.Log($"CombatManager: Executing attack {currentAttackIndex + 1}");

            StartCoroutine(ExecuteAttackCoroutine(attackingMercenary));
            currentAttackIndex++;
        }

        private Mercenary CreateMercenaryForAttack()
        {
            if (HeadTrackingService == null)
            {
                Debug.LogError("CombatManager: HeadTrackingService not available for mercenary creation");
                return null;
            }

            float playerHeading = HeadTrackingService.CurrentHeading;
            float[] possibleOffsets = { -90f, 90f };
            float randomOffset = possibleOffsets[UnityEngine.Random.Range(0, possibleOffsets.Length)];
            float attackBearing = NormalizeAngle(playerHeading + randomOffset);

            Debug.Log($"CombatManager: Created mercenary for attack {currentAttackIndex + 1} — Player facing: {playerHeading:F0}°, Attack from: {attackBearing:F0}°");
            return new Mercenary($"mercenary_{currentAttackIndex}", attackBearing);
        }

        private IEnumerator ExecuteAttackCoroutine(Mercenary attacker)
        {
            currentAttackingMercenary = attacker;
            attacker.StartApproach();

            if (HeadTrackingService != null)
            {
                playerHeadingAtAttackStart = HeadTrackingService.CurrentHeading;
                Debug.Log($"CombatManager: Attack starting — player heading: {playerHeadingAtAttackStart:F0}°");
            }

            currentFootstepsInstance = new EventInstance();
            currentAttackInstance = new EventInstance();

            if (!mercenaryFootstepsEvent.IsNull && AudioService != null)
            {
                currentFootstepsInstance = AudioService.CreateAudioInstance(mercenaryFootstepsEvent);
                if (currentFootstepsInstance.handle != IntPtr.Zero)
                {
                    AudioService.PlayAudio(currentFootstepsInstance, attacker.GetCurrentAudioPosition());
                    Debug.Log("CombatManager: Started footsteps");
                }
            }

            float approachDuration = combatConfig?.approachDuration ?? 4f;

            for (float t = 0; t < approachDuration; t += Time.deltaTime)
            {
                float progress = t / approachDuration;
                attacker.UpdateApproach(progress);

                if (AudioService != null && AudioService.IsInstanceValid(currentFootstepsInstance))
                    AudioService.Update3DAttributes(currentFootstepsInstance, attacker.GetCurrentAudioPosition());

                yield return null;
            }

            Debug.Log("CombatManager: Approach complete — starting attack sound");

            if (!mercenaryAttackEvent.IsNull && AudioService != null)
            {
                currentAttackInstance = AudioService.CreateAudioInstance(mercenaryAttackEvent);
                if (currentAttackInstance.handle != IntPtr.Zero)
                {
                    currentAttackInstance.setCallback(OnAttackSoundComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                    AudioService.PlayAudio(currentAttackInstance, attacker.GetCurrentAudioPosition());
                    Debug.Log("CombatManager: Attack sound started");
                }
                else
                {
                    Invoke(nameof(HandleAttackSoundComplete), 1f);
                }
            }
            else
            {
                Invoke(nameof(HandleAttackSoundComplete), 1f);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnAttackSoundComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("CombatManager: Attack sound completed via FMOD callback");
                Instance.HandleAttackSoundComplete();
            }
            return FMOD.RESULT.OK;
        }

        private void HandleAttackSoundComplete()
        {
            if (AudioService != null && AudioService.IsInstanceValid(currentFootstepsInstance))
            {
                AudioService.StopAudio(currentFootstepsInstance, false);
                AudioService.ReleaseAudio(currentFootstepsInstance);
                Debug.Log("CombatManager: Stopped footsteps");
            }

            if (currentAttackingMercenary != null)
            {
                bool playerSucceeded = CheckPlayerDefense(currentAttackingMercenary.bearing);
                PlayImpactSound(playerSucceeded, currentAttackingMercenary.GetCurrentAudioPosition());
                OnAttackComplete(playerSucceeded);
            }
        }

        private bool CheckPlayerDefense(float attackBearing)
        {
            if (HeadTrackingService == null) return false;

            if (isTutorialCombat && tutorialAttackNumber == 0)
            {
                Debug.Log("CombatManager: Tutorial first attack — unavoidable");
                return false;
            }

            float currentPlayerHeading = HeadTrackingService.CurrentHeading;
            float angleToMercenary = Mathf.DeltaAngle(playerHeadingAtAttackStart, attackBearing);
            bool mercenaryOnLeft = angleToMercenary < 0;
            float playerTurnAmount = Mathf.DeltaAngle(playerHeadingAtAttackStart, currentPlayerHeading);
            const float TURN_THRESHOLD = 10f;

            bool playerTurnedLeft = playerTurnAmount < -TURN_THRESHOLD;
            bool playerTurnedRight = playerTurnAmount > TURN_THRESHOLD;
            bool playerSucceeded = (mercenaryOnLeft && playerTurnedLeft) || (!mercenaryOnLeft && playerTurnedRight);

            Debug.Log($"CombatManager: Defense check — " +
                      $"Mercenary on {(mercenaryOnLeft ? "LEFT" : "RIGHT")}, " +
                      $"Player turned {playerTurnAmount:F1}° ({(playerTurnAmount < 0 ? "LEFT" : "RIGHT")}), " +
                      $"Result: {(playerSucceeded ? "DEFENDED" : "HIT")}");

            return playerSucceeded;
        }

        private void PlayImpactSound(bool playerBlocked, Vector3 position)
        {
            if (!attackImpactEvent.IsNull && AudioService != null)
            {
                var impactInstance = AudioService.CreateAudioInstance(attackImpactEvent);
                AudioService.SetParameter(impactInstance, "HitResult", playerBlocked ? 0 : 1);
                AudioService.PlayAudio(impactInstance, position);
                Debug.Log($"CombatManager: Impact sound played — {(playerBlocked ? "BLOCKED" : "HIT")}");
            }
        }

        private void OnAttackComplete(bool playerSucceeded)
        {
            if (playerSucceeded)
                Debug.Log("CombatManager: Player DEFENDED the attack!");
            else
            {
                Debug.Log("CombatManager: Player was HIT!");
                TakeDamage();
            }

            if (isTutorialCombat)
            {
                tutorialAttackNumber++;

                if (playerSucceeded)
                {
                    consecutiveDefenses++;
                    Debug.Log($"CombatManager: Tutorial — consecutive defenses: {consecutiveDefenses}/2");

                    if (consecutiveDefenses >= 2)
                    {
                        Debug.Log("CombatManager: Tutorial combat complete — 2 consecutive defenses!");
                        TutorialCombatCompleted?.Invoke();
                        CleanupCombat();
                        isTutorialCombat = false;
                        consecutiveDefenses = 0;

                        GameManager.Instance.TransitionToGameplayState(
                            playerHealth >= maxHealth
                                ? GameManager.GameplayState.Wander
                                : GameManager.GameplayState.Recovery);
                        return;
                    }
                }
                else
                {
                    consecutiveDefenses = 0;
                    Debug.Log("CombatManager: Tutorial — defense failed, consecutive counter reset");
                }

                TutorialAttackCompleted?.Invoke(tutorialAttackNumber, playerSucceeded, consecutiveDefenses);
                Invoke(nameof(ExecuteNextAttack), 1f);
                return;
            }

            int maxAttacks = combatConfig?.attackCount ?? 3;
            if (currentAttackIndex < maxAttacks)
            {
                Debug.Log($"CombatManager: Combat continues — attack {currentAttackIndex + 1}/{maxAttacks} next");
                Invoke(nameof(ExecuteNextAttack), 1f);
            }
            else
            {
                Debug.Log("CombatManager: All attacks completed — concluding combat");
                ConcludeCombat();
            }
        }

        private void ConcludeCombat()
        {
            Debug.Log($"CombatManager: Combat concluded — Final health: {playerHealth}/{maxHealth}");

            bool playerWon = playerHealth >= maxHealth;
            AnalyticsService?.TrackEvent($"combat_completed_{(playerWon ? "won" : "lost")}_health_{playerHealth}");

            if (currentCombatEncounter != null)
            {
                string completionKey = $"combat_type_{currentCombatEncounter.combatType}_completed";
                StorageService?.Save(completionKey, true);
                Debug.Log($"CombatManager: Saved combat completion: {completionKey}");
            }

            PlayMercenaryDefeatDialogue();
        }

        private void PlayMercenaryDefeatDialogue()
        {
            if (AudioService == null || mercenaryDefeatEvent.IsNull || !AudioService.IsInstanceValid(mercenaryDefeatInstance))
            {
                Debug.LogWarning("CombatManager: Mercenary defeat audio not available — skipping to transition");
                FinalizeCombatConclusion();
                return;
            }

            try
            {
                if (currentCombatEncounter != null)
                    AudioService.SetParameter(mercenaryDefeatInstance, "CombatType", currentCombatEncounter.combatType);

                mercenaryDefeatInstance.setCallback(OnMercenaryDefeatComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                AudioService.PlayAudio(mercenaryDefeatInstance, Vector3.zero);
                Debug.Log($"CombatManager: Playing mercenary defeat dialogue — Combat Type: {currentCombatEncounter?.combatType ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"CombatManager: Error playing defeat dialogue — {e.Message}");
                FinalizeCombatConclusion();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnMercenaryDefeatComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("CombatManager: Mercenary defeat dialogue completed via TIMELINE_MARKER");
                Instance.FinalizeCombatConclusion();
            }
            return FMOD.RESULT.OK;
        }

        private void FinalizeCombatConclusion()
        {
            Debug.Log("CombatManager: Finalizing combat conclusion");

            if (isTutorialCombat)
            {
                Debug.Log("CombatManager: Tutorial combat complete — firing event");
                TutorialCombatCompleted?.Invoke();
                isTutorialCombat = false;
            }

            currentCombatEncounter = null;

            GameManager.Instance.TransitionToGameplayState(
                playerHealth >= maxHealth
                    ? GameManager.GameplayState.Wander
                    : GameManager.GameplayState.Recovery);
        }

        public void CleanupCombat()
        {
            Debug.Log("CombatManager: Cleaning up combat");

            isInCombat = false;
            activeMercenaries.Clear();
            currentAttackingMercenary = null;
            currentCombatEncounter = null;

            if (AudioService == null) return;

            try
            {
                if (AudioService.IsInstanceValid(mercenaryEncounterInstance))
                {
                    AudioService.StopAudio(mercenaryEncounterInstance, true);
                    AudioService.ReleaseAudio(mercenaryEncounterInstance);
                }

                if (AudioService.IsInstanceValid(mercenaryDefeatInstance))
                {
                    mercenaryDefeatInstance.setCallback(null, EVENT_CALLBACK_TYPE.STOPPED);
                    AudioService.StopAudio(mercenaryDefeatInstance, true);
                    AudioService.ReleaseAudio(mercenaryDefeatInstance);
                }

                if (currentFootstepsInstance.handle != IntPtr.Zero)
                {
                    if (AudioService.IsInstanceValid(currentFootstepsInstance))
                    {
                        AudioService.StopAudio(currentFootstepsInstance, false);
                        AudioService.ReleaseAudio(currentFootstepsInstance);
                    }
                    currentFootstepsInstance = new EventInstance();
                }

                if (currentAttackInstance.handle != IntPtr.Zero)
                {
                    if (AudioService.IsInstanceValid(currentAttackInstance))
                    {
                        currentAttackInstance.setCallback(null, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                        AudioService.StopAudio(currentAttackInstance, false);
                        AudioService.ReleaseAudio(currentAttackInstance);
                    }
                    currentAttackInstance = new EventInstance();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"CombatManager: Error cleaning up combat audio — {e.Message}");
            }
        }

        #endregion

        #region Recovery System

        public void StartRecovery()
        {
            Debug.Log("CombatManager: Starting recovery mode — spawning berry");
            SpawnBerryNearPlayer();
            UpdateHeartbeat();
        }

        private void SpawnBerryNearPlayer()
        {
            if (LocationService == null || LocationService.GetCurrentLocation() == Vector2.zero)
            {
                Debug.LogError("CombatManager: Cannot spawn berry — LocationService not available");
                playerHealth = maxHealth;
                SaveHealthToPreferences();
                GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Wander);
                return;
            }

            activeBerries.Clear();
            Vector2 playerLocation = LocationService.GetCurrentLocation();
            var (distance, angle) = Berry.GetSafeSpawnParameters();
            var berry = new Berry($"berry_{activeBerries.Count}", playerLocation, angle, distance);
            activeBerries.Add(berry);

            Debug.Log($"CombatManager: Berry spawned at distance: {distance:F1}m, angle: {angle:F0}°");
            StartBerryAudio(berry);
        }

        private void StartBerryAudio(Berry berry)
        {
            if (GameManager.Instance?.CurrentGameplayState != GameManager.GameplayState.Recovery) return;

            if (!berryAmbientEvent.IsNull && AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                Vector3 berryPosition = berry.GetAudioPosition();
                AudioService.Update3DAttributes(sharedBerryInstance, berryPosition);

                sharedBerryInstance.getPlaybackState(out PLAYBACK_STATE playbackState);
                if (playbackState != PLAYBACK_STATE.PLAYING)
                    AudioService.PlayAudio(sharedBerryInstance, berryPosition);

                Debug.Log("CombatManager: Berry spatial audio started");
            }
        }

        public void UpdateRecovery()
        {
            if (activeBerries.Count == 0) return;

            Berry currentBerry = activeBerries[0];

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
                AudioService.Update3DAttributes(sharedBerryInstance, currentBerry.GetAudioPosition());

            if (currentBerry.CheckCollection())
                CollectBerry(currentBerry);
        }

        private void CollectBerry(Berry berry)
        {
            Debug.Log($"CombatManager: Berry {berry.id} collected!");

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
                AudioService.StopAudio(sharedBerryInstance, false);

            PlayBerryCollectionSound(berry.GetAudioPosition());
            activeBerries.Remove(berry);

            AnalyticsService?.TrackEvent("berry_collected");

            if (GameManager.Instance?.CurrentMode == GameManager.GameMode.Tutorial)
            {
                Debug.Log("CombatManager: Tutorial berry collected — firing event");
                TutorialBerryCollected?.Invoke();
            }

            int healthToRestore = maxHealth - playerHealth;
            RestoreHealth(healthToRestore);

            if (GameManager.Instance?.CurrentGameplayState == GameManager.GameplayState.Recovery
                && playerHealth < maxHealth)
            {
                Debug.Log("CombatManager: Still need healing — spawning next berry");
                Invoke(nameof(SpawnBerryNearPlayer), 2f);
            }
        }

        private void PlayBerryCollectionSound(Vector3 position)
        {
            if (!berryCollectionEvent.IsNull && AudioService != null)
            {
                var collectionInstance = AudioService.CreateAudioInstance(berryCollectionEvent);
                AudioService.PlayAudio(collectionInstance, position);
                Debug.Log("CombatManager: Berry collection sound played");
            }
        }

        public void CleanupRecovery()
        {
            Debug.Log("CombatManager: Cleaning up recovery mode");

            activeBerries.Clear();

            if (AudioService != null && AudioService.IsInstanceValid(sharedBerryInstance))
                AudioService.StopAudio(sharedBerryInstance, true);
        }

        #endregion

        #region Tutorial Entry Points

        /// <summary>
        /// Called by GameManager.StartTutorialCombat — preserves existing call chain.
        /// </summary>
        public void StartTutorialCombat()
        {
            Debug.Log("CombatManager: Starting tutorial combat");
            isTutorialCombat = true;
            tutorialAttackNumber = 0;
            GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Combat);
        }

        /// <summary>
        /// Called by GameManager.StartTutorialRecovery — preserves existing call chain.
        /// </summary>
        public void StartTutorialRecovery()
        {
            Debug.Log("CombatManager: Starting tutorial recovery");
            GameManager.Instance.TransitionToGameplayState(GameManager.GameplayState.Recovery);
        }

        #endregion

        #region Stop All Audio

        public void StopAllAudio()
        {
            Debug.Log("CombatManager: Stopping all combat audio");

            if (AudioService == null) return;

            if (AudioService.IsInstanceValid(mercenaryEncounterInstance))
                AudioService.StopAudio(mercenaryEncounterInstance, false);

            if (AudioService.IsInstanceValid(mercenaryDefeatInstance))
                AudioService.StopAudio(mercenaryDefeatInstance, false);

            if (currentFootstepsInstance.handle != IntPtr.Zero && AudioService.IsInstanceValid(currentFootstepsInstance))
                AudioService.StopAudio(currentFootstepsInstance, false);

            if (currentAttackInstance.handle != IntPtr.Zero && AudioService.IsInstanceValid(currentAttackInstance))
                AudioService.StopAudio(currentAttackInstance, false);

            if (AudioService.IsInstanceValid(sharedBerryInstance))
                AudioService.StopAudio(sharedBerryInstance, false);

            if (heartbeatInstance.handle != IntPtr.Zero && AudioService.IsInstanceValid(heartbeatInstance))
                AudioService.StopAudio(heartbeatInstance, false);

            Debug.Log("CombatManager: All combat audio stopped");
        }

        #endregion

        #region Reset

        public void CompleteReset()
        {
            Debug.Log("CombatManager: Complete reset");

            StopAllAudio();
            CleanupCombat();
            CleanupRecovery();

            isTutorialCombat = false;
            tutorialAttackNumber = 0;
            consecutiveDefenses = 0;
            isInitialized = false;
            combatConfig = null;
            currentCombatEncounter = null;
        }

        #endregion

        #region Helpers

        private float NormalizeAngle(float angle)
        {
            while (angle < 0f) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }

        #endregion

        #region Cleanup

        public void CleanupHeartbeat()
        {
            if (AudioService != null && AudioService.IsInstanceValid(heartbeatInstance))
            {
                AudioService.StopAudio(heartbeatInstance, true);
                Debug.Log("CombatManager: Heartbeat stopped — entering wander mode");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        #endregion
    }
}