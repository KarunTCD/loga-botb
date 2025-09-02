using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;
using FMODUnity;
using FMOD.Studio;
using System.Linq;
using System.Collections;

namespace LoGa.LudoEngine.Core
{
    public class GameManager : MonoBehaviour
    {
        // Game modes and states
        public enum GameMode
        {
            Inactive,
            Player,
            Spectator
        }

        public enum GameState
        {
            Running,
            Suspended
        }

        // New gameplay states for combat system
        public enum GameplayState
        {
            Wander,    // Normal exploration
            Interact,  // POI dialogue/music
            Combat,    // Mercenary combat
            Recovery,  // Berry collection for health
            Paused     // Game paused
        }

        public static GameManager Instance { get; private set; }

        [Header("Game Components")]
        [SerializeField] private MapManager mapManager;
        [SerializeField] private POIManager poiManager;
        [SerializeField] private UIManager uiManager;

        [Header("Combat System")]
        [SerializeField] private EventReference mercenaryEncounterEvent;
        [SerializeField] private EventReference mercenaryFootstepsEvent;
        [SerializeField] private EventReference mercenaryAttackEvent;
        [SerializeField] private EventReference attackImpactEvent;
        [SerializeField] private EventReference heartbeatEvent;
        [SerializeField] private EventReference berryAmbientEvent;
        [SerializeField] private EventReference berryCollectionEvent;
        [SerializeField] private float combatTriggerCheckInterval = 2f;
        [SerializeField] private float approachDuration = 4f;

        [Header("Testing")]
        [SerializeField] private bool enableTestingMode = true;
        [SerializeField] private float tapTimeWindow = 0.5f; // Time window to detect multiple taps

        // Game state
        private GameMode currentMode = GameMode.Inactive;
        private GameplayState currentGameplayState = GameplayState.Wander;
        private string currentSessionId;
        public GameState gameState = GameState.Suspended;

        // Health system
        private int playerHealth = 3;
        private const int maxHealth = 3;

        // Audio instances
        private EventInstance mercenaryEncounterInstance;
        private EventInstance currentFootstepsInstance;
        private EventInstance currentAttackInstance;
        private EventInstance sharedBerryInstance;
        private EventInstance heartbeatInstance;

        // Combat system
        private List<Mercenary> activeMercenaries = new List<Mercenary>();
        private List<Berry> activeBerries = new List<Berry>();
        private float combatCheckTimer = 0f;
        private bool isInCombat = false;
        private int currentAttackIndex = 0;
        private int currentCombatType = 0;
        private Mercenary currentAttackingMercenary; // For callback reference

        // Spectator mode data
        private Vector2 spectatorLocation = Vector2.zero;
        private float spectatorHeading = 0f;
        private bool isReceivingSpectatorData = false;

        // Touch testing variables
        private List<float> tapTimes = new List<float>();
        private int lastTapCount = 0;

        // Public properties
        public GameMode CurrentMode => currentMode;
        public GameplayState CurrentGameplayState => currentGameplayState;
        public string CurrentSessionId => currentSessionId;
        public bool IsSpectatorMode => currentMode == GameMode.Spectator;
        public int PlayerHealth => playerHealth;
        public bool IsInCombat => isInCombat;

        public Vector2 SpectatorLocation => spectatorLocation;
        public float SpectatorHeading => spectatorHeading;
        public bool IsReceivingSpectatorData => isReceivingSpectatorData;

        // Services
        private IStorageService StorageService => ServiceLocator.GetService<IStorageService>();
        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();

        // Combat artifact combinations
        private readonly Dictionary<string, List<string>> combatTriggers = new Dictionary<string, List<string>>
        {
            { "combat_ancient_modern_crops", new List<string> { "POI_ancient_crops_Unlocked", "POI_modern_crops_Unlocked" } },
            { "combat_amulet_seal", new List<string> { "POI_amulet_Unlocked", "POI_royal_seal_Unlocked" } },
            { "combat_daisy_grenade", new List<string> { "POI_daisy_chain_Unlocked", "POI_grenade_Unlocked" } },
            { "combat_triskel_acorn", new List<string> { "POI_triskel_Unlocked", "POI_acorn_Unlocked" } },
            { "combat_brigid_sun", new List<string> { "POI_brigid_cross_Unlocked", "POI_golden_sun_Unlocked" } },
            { "combat_addendum", new List<string> { "combat_brigid_sun_completed" } }
        };

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                LoadHealthFromPreferences();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            SuspendGame();
            InitializeCombatAudio();
        }

        private void Update()
        {
            HandleTestingInput();
            if (currentMode == GameMode.Player && gameState == GameState.Running)
            {
                switch (currentGameplayState)
                {
                    case GameplayState.Wander:
                        UpdateWanderMode();
                        break;
                    case GameplayState.Combat:
                        UpdateCombatMode();
                        break;
                    case GameplayState.Recovery:
                        UpdateRecoveryMode();
                        break;
                }
            }
        }

        private void HandleTestingInput()
        {
            // Mobile touch input
            DetectMultiTaps();
        }

        private void DetectMultiTaps()
        {
            // Clean up old taps outside time window
            float currentTime = Time.time;
            tapTimes.RemoveAll(t => currentTime - t > tapTimeWindow);

            // Check for new taps
            if (Input.touchCount > 0)
            {
                foreach (Touch touch in Input.touches)
                {
                    if (touch.phase == TouchPhase.Began)
                    {
                        tapTimes.Add(currentTime);
                        break; // Only process one tap per frame
                    }
                }
            }

            // Process tap count after brief delay to capture all simultaneous taps
            if (tapTimes.Count > 0 && currentTime - tapTimes[tapTimes.Count - 1] > 0.1f)
            {
                int tapCount = tapTimes.Count;
                if (tapCount != lastTapCount && tapCount >= 2)
                {
                    ProcessTapCommand(tapCount);
                    lastTapCount = tapCount;
                }
            }

            // Reset if no recent taps
            if (tapTimes.Count == 0)
            {
                lastTapCount = 0;
            }
        }

        private void ProcessTapCommand(int tapCount)
        {
            switch (tapCount)
            {
                case 2:
                    Debug.Log("[TEST] Two-finger tap detected - Force triggering combat mode");
                    currentCombatType = 1;
                    TransitionToCombatMode();
                    break;

                case 3:
                    Debug.Log("[TEST] Three-finger tap detected - Force triggering recovery mode");
                    TransitionToRecoveryMode();
                    break;

                case 4:
                    Debug.Log("[TEST] Four-finger tap detected - Force taking damage");
                    TakeDamage();
                    break;

                case 5:
                    Debug.Log("[TEST] Five-finger tap detected - Reset health to full");
                    playerHealth = maxHealth;
                    SaveHealthToPreferences();
                    UpdateHeartbeat();
                    Debug.Log($"[TEST] Health reset to: {playerHealth}");
                    break;

                default:
                    Debug.Log($"[TEST] {tapCount}-finger tap detected - No command assigned");
                    break;
            }

            // Clear tap history after processing
            tapTimes.Clear();
        }

        private void InitializeCombatAudio()
        {
            // Create persistent audio instances
            if (!heartbeatEvent.IsNull)
            {
                heartbeatInstance = AudioService.CreateAudioInstance(heartbeatEvent);
                Debug.Log("[TEST] Heartbeat audio instance created");
            }

            if (!berryAmbientEvent.IsNull)
            {
                sharedBerryInstance = AudioService.CreateAudioInstance(berryAmbientEvent);
                Debug.Log("[TEST] Shared berry audio instance created");
            }
        }

        #region Health Management

        private void LoadHealthFromPreferences()
        {
            playerHealth = StorageService.Load<int>("PlayerHealth");
            if (playerHealth <= 0) playerHealth = maxHealth; // Default to full health
            Debug.Log($"[TEST] Loaded health from preferences: {playerHealth}");
        }

        private void SaveHealthToPreferences()
        {
            StorageService.Save("PlayerHealth", playerHealth);
            Debug.Log($"[TEST] Saved health to preferences: {playerHealth}");
        }

        public void TakeDamage()
        {
            if (playerHealth > 0)
            {
                playerHealth--;
                SaveHealthToPreferences();
                UpdateHeartbeat();
                Debug.Log($"[TEST] Player took damage. Health: {playerHealth}");
            }
        }

        public void RestoreHealth(int amount = 1)
        {
            playerHealth = Mathf.Min(playerHealth + amount, maxHealth);
            SaveHealthToPreferences();
            UpdateHeartbeat();
            Debug.Log($"[TEST] Player restored health. Health: {playerHealth}");

            if (playerHealth >= maxHealth && currentGameplayState == GameplayState.Recovery)
            {
                Debug.Log("[TEST] Player fully healed. Returning to wander mode.");
                TransitionToWanderMode();
            }
        }

        private void UpdateHeartbeat()
        {
            if (!AudioService.IsInstanceValid(heartbeatInstance))
            {
                Debug.Log("[TEST] Heartbeat instance invalid - skipping update");
                return;
            }

            // Set health parameter
            AudioService.SetParameter(heartbeatInstance, "Health", playerHealth);
            Debug.Log($"[TEST] Heartbeat parameter set to: {playerHealth}");

            // Start heartbeat only if health is low AND not in wander mode
            if (playerHealth < maxHealth && currentGameplayState != GameplayState.Wander)
            {
                PLAYBACK_STATE playbackState;
                heartbeatInstance.getPlaybackState(out playbackState);
                if (playbackState != PLAYBACK_STATE.PLAYING)
                {
                    AudioService.PlayAudio(heartbeatInstance, Vector3.zero);
                    Debug.Log("[TEST] Heartbeat audio started");
                }
            }
            else
            {
                // Stop heartbeat if full health OR in wander mode
                AudioService.StopAudio(heartbeatInstance, true);
                Debug.Log("[TEST] Heartbeat audio stopped");
            }
        }

        #endregion

        #region State Management

        public void TransitionToState(GameplayState newState)
        {
            if (currentGameplayState == newState) return;

            Debug.Log($"[TEST] State transition: {currentGameplayState} → {newState}");

            // Exit current state
            ExitState(currentGameplayState);

            // Enter new state
            currentGameplayState = newState;
            EnterState(newState);
        }

        private void ExitState(GameplayState state)
        {
            Debug.Log($"[TEST] Exiting state: {state}");
            switch (state)
            {
                case GameplayState.Combat:
                    CleanupCombat();
                    break;
                case GameplayState.Recovery:
                    CleanupRecovery();
                    break;
            }
        }

        public void TransitionToWanderMode()
        {
            TransitionToState(GameplayState.Wander);
        }

        public void TransitionToInteractMode()
        {
            TransitionToState(GameplayState.Interact);
        }

        public void TransitionToCombatMode()
        {
            TransitionToState(GameplayState.Combat);
        }

        public void TransitionToRecoveryMode()
        {
            TransitionToState(GameplayState.Recovery);
        }

        private void EnterState(GameplayState state)
        {
            Debug.Log($"[TEST] Entering state: {state}");
            switch (state)
            {
                case GameplayState.Wander:
                    EnablePOIManager();
                    // Always stop heartbeat in wander mode
                    if (AudioService.IsInstanceValid(heartbeatInstance))
                    {
                        AudioService.StopAudio(heartbeatInstance, true);
                        Debug.Log("[TEST] Heartbeat stopped - entering wander mode");
                    }
                    break;

                case GameplayState.Interact:
                    // POIManager stays enabled for proximity/dialogue logic
                    break;

                case GameplayState.Combat:
                    DisablePOIManager();
                    StartCombat();
                    // Don't start heartbeat here - only after damage
                    break;

                case GameplayState.Recovery:
                    // POIManager stays disabled
                    StartRecovery();
                    UpdateHeartbeat(); // Start heartbeat if health < max
                    break;
            }
        }

        private void EnablePOIManager()
        {
            Debug.Log("[TEST] Enabling POIManager and resuming POI audio");
            if (poiManager != null)
            {
                poiManager.enabled = true;
                poiManager.ResumeAllPOIAudio();
            }
        }

        private void DisablePOIManager()
        {
            Debug.Log("[TEST] Disabling POIManager and silencing POI audio");
            if (poiManager != null)
            {
                poiManager.SilenceAllPOIAudio();
                poiManager.ClearAllNavigationState();
                poiManager.enabled = false;
            }
        }

        #endregion

        #region Combat System

        private void UpdateWanderMode()
        {
            combatCheckTimer += Time.deltaTime;
            if (combatCheckTimer >= combatTriggerCheckInterval)
            {
                CheckForCombatTriggers();
                combatCheckTimer = 0f;
            }
        }

        private void CheckForCombatTriggers()
        {
            foreach (var trigger in combatTriggers)
            {
                string combatId = trigger.Key;

                // Skip completed combats
                if (StorageService.Load<bool>($"{combatId}_completed")) continue;

                // Check artifact combination
                bool allUnlocked = trigger.Value.All(artifact => StorageService.Load<bool>(artifact));

                if (allUnlocked)
                {
                    Debug.Log($"[TEST] Combat trigger activated: {combatId}");
                    StorageService.Save($"{combatId}_completed", true);
                    currentCombatType = GetCombatTypeIndex(combatId);
                    TransitionToCombatMode();
                    return;
                }
            }
        }

        private int GetCombatTypeIndex(string combatId)
        {
            int combatType = combatId switch
            {
                "combat_ancient_modern_crops" => 1,
                "combat_amulet_seal" => 2,
                "combat_daisy_grenade" => 3,
                "combat_triskel_acorn" => 4,
                "combat_brigid_sun" => 5,
                "combat_addendum" => 6,
                _ => 1
            };
            Debug.Log($"[TEST] Combat type index: {combatType} for {combatId}");
            return combatType;
        }

        private void StartCombat()
        {
            isInCombat = true;
            currentAttackIndex = 0;

            Debug.Log("[TEST] Starting combat sequence");

            // Create mercenaries dynamically for each attack
            CreateMercenariesAtCombatStart();

            // Start mercenary encounter (timeline-based audio)
            StartMercenaryEncounter();

            // Don't start heartbeat yet - only after damage
            Debug.Log("[TEST] Combat started!");
        }

        private void CreateMercenariesAtCombatStart()
        {
            Debug.Log("[TEST] Creating mercenaries for simulated combat");
            // Keep empty for now - mercenaries will be created dynamically for each attack
            activeMercenaries.Clear();
        }

        // Create mercenary with dynamic attack direction based on player heading
        private Mercenary CreateMercenaryForAttack()
        {
            float playerHeading = ServiceLocator.GetService<IHeadTrackingService>().CurrentHeading;

            // Generate attack from front, left, or right (not behind)
            float[] possibleOffsets = { -60f, 0f, 60f }; // Left, front, right relative to player
            float randomOffset = possibleOffsets[UnityEngine.Random.Range(0, possibleOffsets.Length)];
            float attackBearing = NormalizeAngle(playerHeading + randomOffset);

            var mercenary = new Mercenary($"mercenary_{currentAttackIndex}", attackBearing);

            Debug.Log($"[TEST] Created mercenary for attack {currentAttackIndex + 1} - Player facing: {playerHeading:F0}°, Attack from: {attackBearing:F0}° (offset: {randomOffset:F0}°)");

            return mercenary;
        }

        private float NormalizeAngle(float angle)
        {
            while (angle < 0f) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }

        private void StartMercenaryEncounter()
        {
            if (!mercenaryEncounterEvent.IsNull)
            {
                mercenaryEncounterInstance = AudioService.CreateAudioInstance(mercenaryEncounterEvent);

                // Only set combat type - timeline handles all volume automation
                AudioService.SetParameter(mercenaryEncounterInstance, "CombatType", currentCombatType);

                mercenaryEncounterInstance.setCallback(OnMercenaryDialogueComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                mercenaryEncounterInstance.start();
                Debug.Log($"[TEST] Mercenary encounter started - Combat Type: {currentCombatType}");
            }
        }

        private void UpdateCombatMode()
        {
            // Combat audio runs entirely on FMOD timeline - no updates needed
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnMercenaryDialogueComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("[TEST] Mercenary dialogue completed via FMOD callback - starting attacks");
                Instance.StartCoroutine(Instance.StartAttackAfterDelay(10f));
            }
            return FMOD.RESULT.OK;
        }

        private IEnumerator StartAttackAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            Instance.StartAttackSequence();
        }

        private void StartAttackSequence()
        {
            currentAttackIndex = 0;
            Debug.Log("[TEST] Starting attack sequence");
            ExecuteNextAttack();
        }

        private void ExecuteNextAttack()
        {
            if (currentAttackIndex >= 3)
            {
                Debug.Log("[TEST] All 3 attacks completed - concluding combat");
                ConcludeCombat();
                return;
            }

            // Create mercenary dynamically based on current player heading
            var attackingMercenary = CreateMercenaryForAttack();
            activeMercenaries.Add(attackingMercenary);

            Debug.Log($"[TEST] Executing attack {currentAttackIndex + 1}/3 from {attackingMercenary.id}");

            // Execute attack with three-stage audio
            StartCoroutine(ExecuteAttackCoroutine(attackingMercenary));

            currentAttackIndex++;
        }

        private System.Collections.IEnumerator ExecuteAttackCoroutine(Mercenary attacker)
        {
            currentAttackingMercenary = attacker;
            attacker.StartApproach();

            // Stage 1: Start footsteps loop
            if (!mercenaryFootstepsEvent.IsNull)
            {
                currentFootstepsInstance = AudioService.CreateAudioInstance(mercenaryFootstepsEvent);
                Vector3 startPos = attacker.GetCurrentAudioPosition();
                AudioService.PlayAudio(currentFootstepsInstance, startPos);
                Debug.Log($"[TEST] Started footsteps at {startPos}");
            }
            else
            {
                Debug.Log("[TEST] Footsteps audio disabled");
            }

            // Update position during approach
            for (float t = 0; t < approachDuration; t += Time.deltaTime)
            {
                float progress = t / approachDuration;
                attacker.UpdateApproach(progress);
                Vector3 currentPos = attacker.GetCurrentAudioPosition();

                if (AudioService.IsInstanceValid(currentFootstepsInstance))
                {
                    AudioService.Update3DAttributes(currentFootstepsInstance, currentPos);
                }

                yield return null;
            }

            Debug.Log("[TEST] Approach complete - starting attack sound");

            // Stage 2: Start attack sound (footsteps still playing)
            if (!mercenaryAttackEvent.IsNull)
            {
                currentAttackInstance = AudioService.CreateAudioInstance(mercenaryAttackEvent);
                currentAttackInstance.setCallback(OnAttackSoundComplete, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
                AudioService.PlayAudio(currentAttackInstance, attacker.GetCurrentAudioPosition());
                Debug.Log("[TEST] Attack sound started with callback registered");
            }
            else
            {
                Debug.Log("[TEST] Attack sound disabled - simulating completion");
                Invoke(nameof(HandleAttackSoundComplete), 1f);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnAttackSoundComplete(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
        {
            if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER && Instance != null)
            {
                Debug.Log("[TEST] Attack sound completed via FMOD callback");
                Instance.HandleAttackSoundComplete();
            }
            return FMOD.RESULT.OK;
        }

        private void HandleAttackSoundComplete()
        {
            // Stop footsteps
            if (AudioService.IsInstanceValid(currentFootstepsInstance))
            {
                AudioService.StopAudio(currentFootstepsInstance, false);
                AudioService.ReleaseAudio(currentFootstepsInstance);
                Debug.Log("[TEST] Stopped footsteps");
            }

            // Check defense and play impact
            bool playerSucceeded = CheckPlayerDefense(currentAttackingMercenary.bearing);
            PlayImpactSound(playerSucceeded, currentAttackingMercenary.GetCurrentAudioPosition());

            Debug.Log($"[TEST] Defense result: {(playerSucceeded ? "SUCCESS" : "FAILED")}");

            OnAttackComplete(playerSucceeded);
        }

        private void PlayImpactSound(bool playerBlocked, Vector3 position)
        {
            if (!attackImpactEvent.IsNull)
            {
                var impactInstance = AudioService.CreateAudioInstance(attackImpactEvent);
                // playerBlocked = true should play MISS (0), playerBlocked = false should play HIT (1)
                AudioService.SetParameter(impactInstance, "HitResult", playerBlocked ? 0 : 1);
                AudioService.PlayAudio(impactInstance, position);
                Debug.Log($"[TEST] Impact: Player {(playerBlocked ? "BLOCKED (miss sound)" : "FAILED (hit sound)")}");
            }
        }

        private bool CheckPlayerDefense(float attackBearing)
        {
            float playerHeading = ServiceLocator.GetService<IHeadTrackingService>().CurrentHeading;
            float angleDifference = Mathf.Abs(Mathf.DeltaAngle(playerHeading, attackBearing));

            // Player succeeds if facing within 30 degrees of the attack direction
            bool playerSucceeded = angleDifference <= 30f;

            Debug.Log($"[TEST] Defense check - Attack from: {attackBearing}°, Player facing: {playerHeading}°, Difference: {angleDifference:F1}°, Success: {playerSucceeded}");

            return playerSucceeded;
        }

        private void OnAttackComplete(bool playerSucceeded)
        {
            if (playerSucceeded)
            {
                Debug.Log("[TEST] Player BLOCKED the attack!");
            }
            else
            {
                Debug.Log("[TEST] Player was HIT!");
                TakeDamage();
                // Don't check for defeat here - let all 3 attacks complete first
            }

            // Continue combat if more attacks remain
            if (currentAttackIndex < 3)
            {
                Debug.Log($"[TEST] Combat continues - attack {currentAttackIndex + 1}/3 next in 1 second");
                Invoke(nameof(ExecuteNextAttack), 1f);
            }
            else
            {
                Debug.Log("[TEST] All 3 attacks completed - concluding combat");
                ConcludeCombat();
            }
        }

        private void ConcludeCombat()
        {
            Debug.Log($"[TEST] Combat concluded - Final health: {playerHealth}/{maxHealth}");

            if (playerHealth >= maxHealth)
            {
                Debug.Log("[TEST] Player at full health - VICTORY! Returning to wander mode");
                TransitionToWanderMode();
            }
            else
            {
                Debug.Log("[TEST] Player damaged - entering recovery for healing");
                TransitionToRecoveryMode();
            }
        }

        private void CleanupCombat()
        {
            Debug.Log("[TEST] Cleaning up combat");
            isInCombat = false;
            activeMercenaries.Clear();
            currentAttackingMercenary = null;

            // Stop mercenary encounter audio
            if (AudioService.IsInstanceValid(mercenaryEncounterInstance))
            {
                AudioService.StopAudio(mercenaryEncounterInstance, true);
                AudioService.ReleaseAudio(mercenaryEncounterInstance);
                Debug.Log("[TEST] Stopped mercenary encounter audio");
            }

            // Stop footsteps if still playing
            if (AudioService.IsInstanceValid(currentFootstepsInstance))
            {
                AudioService.StopAudio(currentFootstepsInstance, false);
                AudioService.ReleaseAudio(currentFootstepsInstance);
                Debug.Log("[TEST] Cleaned up footsteps audio");
            }

            // Stop attack sound if still playing
            if (AudioService.IsInstanceValid(currentAttackInstance))
            {
                AudioService.StopAudio(currentAttackInstance, false);
                AudioService.ReleaseAudio(currentAttackInstance);
                Debug.Log("[TEST] Cleaned up attack audio");
            }
        }

        #endregion

        #region Recovery System

        private void StartRecovery()
        {
            Debug.Log("[TEST] Starting recovery mode - spawning first berry");
            SpawnBerryNearPlayer();
            UpdateHeartbeat(); // Start heartbeat since health < max
        }

        private void SpawnBerryNearPlayer()
        {
            // Clear any existing berries first
            activeBerries.Clear();

            Vector2 playerLocation = LocationService.GetCurrentLocation();

            // Use GPS-safe spawn parameters
            var (distance, angle) = Berry.GetSafeSpawnParameters();

            var berry = new Berry($"berry_{activeBerries.Count}", playerLocation, angle, distance);
            activeBerries.Add(berry);

            Debug.Log($"[TEST] Berry spawned at GPS-safe distance: {distance:F1}m, angle: {angle:F0}°");

            // Start spatial audio immediately
            StartBerryAudio(berry);
        }

        private void StartBerryAudio(Berry berry)
        {
            if (!berryAmbientEvent.IsNull && AudioService.IsInstanceValid(sharedBerryInstance))
            {
                Vector3 berryPosition = berry.GetAudioPosition();

                // Set 3D position and start playing - FMOD handles distance attenuation
                AudioService.Update3DAttributes(sharedBerryInstance, berryPosition);

                // Ensure it's playing
                PLAYBACK_STATE playbackState;
                sharedBerryInstance.getPlaybackState(out playbackState);
                if (playbackState != PLAYBACK_STATE.PLAYING)
                {
                    AudioService.PlayAudio(sharedBerryInstance, berryPosition);
                }

                Debug.Log($"[TEST] Berry spatial audio started at {berryPosition}");
            }
            else
            {
                Debug.Log("[TEST] Berry audio disabled or invalid instance");
            }
        }

        private void UpdateRecoveryMode()
        {
            if (activeBerries.Count == 0) return;

            Berry currentBerry = activeBerries[0];

            // Update 3D position every frame to account for player movement AND heading changes
            if (AudioService.IsInstanceValid(sharedBerryInstance))
            {
                Vector3 berryPosition = currentBerry.GetAudioPosition(); // This now includes player heading
                AudioService.Update3DAttributes(sharedBerryInstance, berryPosition);
            }

            // Check for collection
            if (currentBerry.CheckCollection())
            {
                CollectBerry(currentBerry);
            }
        }

        private void CollectBerry(Berry berry)
        {
            Debug.Log($"[TEST] Berry {berry.id} collected!");

            // Stop ambient audio immediately
            if (AudioService.IsInstanceValid(sharedBerryInstance))
            {
                AudioService.StopAudio(sharedBerryInstance, false);
                Debug.Log("[TEST] Berry ambient audio stopped");
            }

            // Play collection sound
            PlayBerryCollectionSound(berry.GetAudioPosition());

            // Remove collected berry
            activeBerries.Remove(berry);

            // Restore health - this will trigger state transition if fully healed
            RestoreHealth(1);

            Debug.Log($"[TEST] Health after berry collection: {playerHealth}/{maxHealth}");

            // Only spawn next berry if we're still in recovery mode (not fully healed)
            if (currentGameplayState == GameplayState.Recovery && playerHealth < maxHealth)
            {
                Debug.Log("[TEST] Still need healing - spawning next berry in 2 seconds");
                Invoke(nameof(SpawnBerryNearPlayer), 2f);
            }
            else if (playerHealth >= maxHealth)
            {
                Debug.Log("[TEST] Fully healed via berry collection - recovery will end");
                // RestoreHealth() will handle transition to wander mode
            }
        }

        private void PlayBerryCollectionSound(Vector3 position)
        {
            if (!berryCollectionEvent.IsNull)
            {
                var collectionInstance = AudioService.CreateAudioInstance(berryCollectionEvent);
                AudioService.PlayAudio(collectionInstance, position);
                // Let FMOD auto-cleanup one-shot sounds
                Debug.Log("[TEST] Berry collection sound played");
            }
        }

        private void CleanupRecovery()
        {
            Debug.Log("[TEST] Cleaning up recovery mode");

            // Cleanup all berries
            activeBerries.Clear();

            // Stop berry audio
            if (AudioService.IsInstanceValid(sharedBerryInstance))
            {
                AudioService.StopAudio(sharedBerryInstance, true);
                Debug.Log("[TEST] Berry audio stopped and cleaned up");
            }
        }

        #endregion

        #region Original GameManager Methods

        public void SetGameMode(GameMode mode)
        {
            currentMode = mode;

            switch (mode)
            {
                case GameMode.Player:
                    StartGameAsPlayer();
                    break;
                case GameMode.Spectator:
                    StartGameAsSpectator();
                    break;
                default:
                    SuspendGame();
                    break;
            }
        }

        private async void SuspendGame()
        {
            currentMode = GameMode.Inactive;
            gameState = GameState.Suspended;
            isReceivingSpectatorData = false;

            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();
            var headTrackingService = await ServiceLocator.GetInitializedService<IHeadTrackingService>();

            if (locationService != null && locationService.IsRunning)
            {
                locationService.StopLocationUpdates();
            }

            if (headTrackingService != null)
            {
                headTrackingService.StopTracking();
            }

            if (mapManager != null)
                mapManager.enabled = false;
            if (poiManager != null)
                poiManager.enabled = false;

            if (mapManager != null)
                mapManager.SetSpectatorMode(false);
        }

        private async void StartGameAsPlayer()
        {
            currentMode = GameMode.Player;
            gameState = GameState.Running;
            isReceivingSpectatorData = false;

            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();
            var headTrackingService = await ServiceLocator.GetInitializedService<IHeadTrackingService>();

            if (locationService != null)
            {
                locationService.StartLocationUpdates();
            }
            else
            {
                Debug.LogError("Cannot start location updates - location service not initialized");
            }

            if (headTrackingService != null)
            {
                headTrackingService.StartTracking();
            }
            else
            {
                Debug.LogError("Cannot start head tracking - service not initialized");
            }

            if (mapManager != null)
            {
                mapManager.enabled = true;
                mapManager.SetSpectatorMode(false);
            }

            // Only enable POI manager if in wander mode
            if (currentGameplayState == GameplayState.Wander && poiManager != null)
                poiManager.enabled = true;
        }

        private async void StartGameAsSpectator()
        {
            currentMode = GameMode.Spectator;
            gameState = GameState.Running;
            isReceivingSpectatorData = false;

            var locationService = await ServiceLocator.GetInitializedService<ILocationService>();

            if (locationService != null && locationService.IsRunning)
            {
                locationService.StopLocationUpdates();
            }

            if (mapManager != null)
            {
                mapManager.enabled = true;
                mapManager.SetSpectatorMode(true);
            }

            if (poiManager != null)
                poiManager.enabled = true;
        }

        public async Task<bool> StartPlayerMode()
        {
            try
            {
                currentSessionId = System.Guid.NewGuid().ToString();

                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService == null)
                {
                    Debug.LogError("Failed to initialize Firebase service");
                    return false;
                }

                bool initialized = await firebaseService.InitializeSession(currentSessionId, "Player");

                if (initialized)
                {
                    SetGameMode(GameMode.Player);
                    if (poiManager != null)
                        poiManager.PlayWelcomeGreeting();
                    return true;
                }
                else
                {
                    throw new System.Exception("Failed to initialize session");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start player mode: {e.Message}");
                SuspendGame();
                return false;
            }
        }

        public async Task<bool> StartSpectatorMode(string sessionId)
        {
            try
            {
                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService == null)
                {
                    Debug.LogError("Failed to initialize Firebase service");
                    return false;
                }

                bool connected = await firebaseService.ConnectToSession(
                    sessionId,
                    OnSpectatorPositionUpdated,
                    OnSpectatorPOIsUpdated);

                if (connected)
                {
                    currentSessionId = sessionId;
                    SetGameMode(GameMode.Spectator);
                    return true;
                }
                else
                {
                    if (uiManager != null)
                        uiManager.ShowConnectionError();
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start spectator mode: {e.Message}");
                SuspendGame();
                return false;
            }
        }

        private void OnSpectatorPositionUpdated(float latitude, float longitude, float heading)
        {
            spectatorLocation = new Vector2(latitude, longitude);
            spectatorHeading = heading;
            isReceivingSpectatorData = true;

            if (uiManager != null)
                uiManager.UpdateLocationDisplay(latitude, longitude);

            Debug.Log($"Spectator position updated: {latitude:F6}, {longitude:F6}, heading: {heading:F1}°");
        }

        private void OnSpectatorPOIsUpdated(List<string> poiIds)
        {
            if (poiManager != null)
                poiManager.UpdateUnlockedPOIs(poiIds);
        }

        public async void ExitSpectatorMode()
        {
            if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
            {
                var firebaseService = await ServiceLocator.GetInitializedService<IFirebaseService>();

                if (firebaseService != null)
                {
                    firebaseService.DisconnectFromSession(currentSessionId);
                    currentSessionId = null;
                }
            }

            isReceivingSpectatorData = false;
            spectatorLocation = Vector2.zero;
            spectatorHeading = 0f;

            SetGameMode(GameMode.Inactive);
        }

        private async void OnApplicationQuit()
        {
            try
            {
                var firebaseService = ServiceLocator.GetService<IFirebaseService>();

                if (firebaseService != null && firebaseService.IsInitialized)
                {
                    if (currentMode == GameMode.Player && !string.IsNullOrEmpty(currentSessionId))
                    {
                        var task = firebaseService.DeleteSession(currentSessionId);
                        var delayTask = Task.Delay(500);
                        await Task.WhenAny(task, delayTask);
                    }
                    else if (currentMode == GameMode.Spectator && !string.IsNullOrEmpty(currentSessionId))
                    {
                        firebaseService.DisconnectFromSession(currentSessionId);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during application quit: {e.Message}");
            }
        }

        #endregion

        #region Testing UI

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void OnGUI()
        {
            if (!enableTestingMode) return;

            GUILayout.BeginArea(new Rect(10, 10, 350, 280));
            GUILayout.Label($"Current State: {currentGameplayState}");
            GUILayout.Label($"Health: {playerHealth}/{maxHealth}");
            GUILayout.Label($"Combat Active: {isInCombat}");
            GUILayout.Label($"Active Berries: {activeBerries.Count}");
            GUILayout.Label($"Active Mercenaries: {activeMercenaries.Count}");

            GUILayout.Space(10);

            if (Application.isEditor)
            {
                GUILayout.Label("Desktop Test Controls:");
                GUILayout.Label("C - Force Combat");
                GUILayout.Label("R - Force Recovery");
                GUILayout.Label("D - Take Damage");
            }
            else
            {
                GUILayout.Label("Mobile Test Controls:");
                GUILayout.Label("2 Finger Tap - Force Combat");
                GUILayout.Label("3 Finger Tap - Force Recovery");
                GUILayout.Label("4 Finger Tap - Take Damage");
                GUILayout.Label("5 Finger Tap - Reset Health");
                GUILayout.Label($"Recent Taps: {tapTimes.Count}");
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}