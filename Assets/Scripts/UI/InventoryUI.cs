using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Game;
using LoGa.LudoEngine.Services;
using System.Collections.Generic;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Inventory UI - displays collected characters and artifacts
    /// Tab-based interface with scrollable lists
    /// All row creation handled inline - no separate InventoryItemRow script needed
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject inventoryPanel;
        
        [Header("Tab Buttons")]
        [SerializeField] private Button charactersTabButton;
        [SerializeField] private Button artifactsTabButton;
        
        [Header("Tab Content")]
        [SerializeField] private GameObject charactersContent;
        [SerializeField] private GameObject artifactsContent;
        
        [Header("List Containers")]
        [SerializeField] private Transform charactersListContainer;
        [SerializeField] private Transform artifactsListContainer;
        
        [Header("Item Row Prefab")]
        [SerializeField] private GameObject itemRowPrefab;
        
        [Header("Exit Button")]
        [SerializeField] private Button exitButton;
        
        [Header("Empty States (Optional)")]
        [SerializeField] private GameObject charactersEmptyState;
        [SerializeField] private GameObject artifactsEmptyState;

        private UIManager uiManager;
        private ItemType currentTab = ItemType.Character;
        
        // Cache for playing audio instances
        private Dictionary<int, FMOD.Studio.EventInstance> playingAudioInstances = new Dictionary<int, FMOD.Studio.EventInstance>();

        private IAudioService AudioService => ServiceLocator.GetService<IAudioService>();

        private void Start()
        {
            SetupButtonListeners();
            
            // Start on Characters tab
            SwitchTab(ItemType.Character);
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("InventoryUI: UIManager reference set");
        }

        private void SetupButtonListeners()
        {
            if (charactersTabButton != null)
                charactersTabButton.onClick.AddListener(() => SwitchTab(ItemType.Character));
            
            if (artifactsTabButton != null)
                artifactsTabButton.onClick.AddListener(() => SwitchTab(ItemType.Artifact));
            
            if (exitButton != null)
                exitButton.onClick.AddListener(OnExit);
        }

        /// <summary>
        /// Show inventory panel and refresh contents
        /// </summary>
        public void Show()
        {
            if (inventoryPanel != null)
            {
                inventoryPanel.SetActive(true);
                RefreshCurrentTab();
                Debug.Log("InventoryUI: Panel shown");
            }
        }

        /// <summary>
        /// Hide inventory panel and stop all playing audio
        /// </summary>
        public void Hide()
        {
            StopAllPlayingAudio();
            
            if (inventoryPanel != null)
            {
                inventoryPanel.SetActive(false);
                Debug.Log("InventoryUI: Panel hidden");
            }
        }

        private void OnExit()
        {
            Debug.Log("InventoryUI: Exit button pressed");
            
            if (uiManager != null)
            {
                uiManager.OnInventoryClose();
            }
        }

        /// <summary>
        /// Switch between Characters and Artifacts tabs
        /// </summary>
        private void SwitchTab(ItemType tab)
        {
            currentTab = tab;
            
            bool showCharacters = (tab == ItemType.Character);
            
            // Toggle content visibility
            if (charactersContent != null)
                charactersContent.SetActive(showCharacters);
            
            if (artifactsContent != null)
                artifactsContent.SetActive(!showCharacters);
            
            // Update tab button states (visual feedback for active tab)
            UpdateTabButtonStates(showCharacters);
            
            // Refresh the active tab
            RefreshCurrentTab();
            
            Debug.Log($"InventoryUI: Switched to {tab} tab");
        }

        private void UpdateTabButtonStates(bool charactersActive)
        {
            // Visual feedback: darker color for active tab
            if (charactersTabButton != null)
            {
                var colors = charactersTabButton.colors;
                colors.normalColor = charactersActive ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                charactersTabButton.colors = colors;
            }
            
            if (artifactsTabButton != null)
            {
                var colors = artifactsTabButton.colors;
                colors.normalColor = charactersActive ? Color.white : new Color(0.7f, 0.7f, 0.7f);
                artifactsTabButton.colors = colors;
            }
        }

        /// <summary>
        /// Refresh the currently active tab
        /// </summary>
        private void RefreshCurrentTab()
        {
            if (InventoryManager.Instance == null)
            {
                Debug.LogError("InventoryUI: InventoryManager not found!");
                return;
            }

            var inventory = InventoryManager.Instance.GetInventory();
            var items = inventory.GetItemsByType(currentTab);

            Transform container = currentTab == ItemType.Character ? 
                charactersListContainer : artifactsListContainer;
            
            GameObject emptyState = currentTab == ItemType.Character ? 
                charactersEmptyState : artifactsEmptyState;

            // Clear existing items
            ClearContainer(container);

            // Show empty state or populate items
            if (items == null || items.Count == 0)
            {
                if (emptyState != null)
                    emptyState.SetActive(true);
                    
                Debug.Log($"InventoryUI: No {currentTab} items found");
            }
            else
            {
                if (emptyState != null)
                    emptyState.SetActive(false);

                // Create item rows
                foreach (var item in items)
                {
                    CreateItemRow(item, container);
                }
                
                Debug.Log($"InventoryUI: Created {items.Count} {currentTab} rows");
            }
        }

        /// <summary>
        /// Clear all children from container
        /// </summary>
        private void ClearContainer(Transform container)
        {
            if (container == null) return;

            foreach (Transform child in container)
            {
                Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Create a single item row in the list
        /// All setup done inline - no separate InventoryItemRow script needed
        /// </summary>
        private void CreateItemRow(InventoryItem item, Transform parent)
        {
            // Skip tutorial character items
            if (!string.IsNullOrEmpty(item.sourceCharacterId) && item.sourceCharacterId == "tutorial_character")
                return;

            if (itemRowPrefab == null || parent == null)
            {
                Debug.LogError("InventoryUI: Item row prefab or parent not assigned!");
                return;
            }

            GameObject rowObj = Instantiate(itemRowPrefab, parent);
            rowObj.name = $"Row_{item.type}_{item.itemId}";
            
            // Find UI components in the row (flexible search - tries direct child first, then recursive)
            TextMeshProUGUI nameText = FindChildComponent<TextMeshProUGUI>(rowObj.transform, "NameText");
            Button playButton = FindChildComponent<Button>(rowObj.transform, "PlayButton");
            Image iconImage = FindChildComponent<Image>(rowObj.transform, "Icon");

            // Set item name
            if (nameText != null)
            {
                nameText.text = item.name;
            }
            else
            {
                Debug.LogWarning($"InventoryUI: NameText not found in row prefab for {item.name}");
            }

            // Setup based on item type
            if (item.type == ItemType.Character)
            {
                SetupCharacterRow(item, playButton, iconImage);
            }
            else // Artifact
            {
                SetupArtifactRow(item, playButton, iconImage);
            }
        }

        /// <summary>
        /// Setup row for character item (play button visible if has audio)
        /// </summary>
        private void SetupCharacterRow(InventoryItem item, Button playButton, Image iconImage)
        {
            // CHARACTER: Show play button if has audio
            if (playButton != null)
            {
                bool hasAudio = !string.IsNullOrEmpty(item.audioClip);
                playButton.gameObject.SetActive(hasAudio);
                
                if (hasAudio)
                {
                    // Setup play/pause toggle (lambda captures item + button)
                    playButton.onClick.AddListener(() => ToggleAudio(item, playButton));
                    UpdatePlayButtonIcon(playButton, false); // Start with "play" icon
                }
            }

            // Hide icon for characters
            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Setup row for artifact item (icon visible, no play button)
        /// </summary>
        private void SetupArtifactRow(InventoryItem item, Button playButton, Image iconImage)
        {
            // ARTIFACT: Hide play button
            if (playButton != null)
            {
                playButton.gameObject.SetActive(false);
            }

            // Show icon (placeholder if sprite not assigned)
            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(true);
                // Icon sprite should be assigned in prefab or loaded dynamically
                // For now, it will show whatever sprite is assigned in the prefab
                
                // TODO: Load artifact-specific sprites based on item.itemId
                // Example: iconImage.sprite = Resources.Load<Sprite>($"Icons/Artifact_{item.itemId}");
            }
        }

        /// <summary>
        /// Helper to find child component by name (flexible searching)
        /// Tries direct child first, then falls back to recursive search
        /// </summary>
        private T FindChildComponent<T>(Transform parent, string childName) where T : Component
        {
            // Try direct child first
            Transform child = parent.Find(childName);
            if (child != null)
            {
                T component = child.GetComponent<T>();
                if (component != null)
                    return component;
            }
            
            // Fallback: Search recursively if not found at first level
            return parent.GetComponentInChildren<T>();
        }

        /// <summary>
        /// Toggle audio playback for a character item
        /// </summary>
        private void ToggleAudio(InventoryItem item, Button playButton)
        {
            if (AudioService == null)
            {
                Debug.LogError("InventoryUI: AudioService not available!");
                return;
            }

            // Check if this item is currently playing
            bool isPlaying = playingAudioInstances.ContainsKey(item.itemId);

            if (isPlaying)
            {
                // Stop the audio
                StopAudio(item.itemId);
                UpdatePlayButtonIcon(playButton, false); // Show "play" icon
            }
            else
            {
                // Stop any other playing audio first (only one at a time)
                StopAllPlayingAudio();

                // Play the audio
                PlayAudio(item);
                UpdatePlayButtonIcon(playButton, true); // Show "pause" icon
            }
        }

        /// <summary>
        /// Play character audio with Zone = 1.0 (music only)
        /// </summary>
        private void PlayAudio(InventoryItem item)
        {
            if (string.IsNullOrEmpty(item.audioClip))
            {
                Debug.LogWarning($"InventoryUI: No audio clip for item {item.name}");
                return;
            }

            var gameDataService = ServiceLocator.GetService<IGameDataService>();
            if (gameDataService == null)
            {
                Debug.LogError("InventoryUI: GameDataService not available!");
                return;
            }

            // Get audio event reference from string
            FMODUnity.EventReference audioEvent = gameDataService.GetAudioEventReference(item.audioClip);
            
            if (audioEvent.IsNull)
            {
                Debug.LogError($"InventoryUI: Failed to load audio event: {item.audioClip}");
                return;
            }

            // Create and play audio instance
            var audioInstance = AudioService.CreateAudioInstance(audioEvent);
            
            if (audioInstance.handle == System.IntPtr.Zero)
            {
                Debug.LogError($"InventoryUI: Failed to create audio instance for {item.name}");
                return;
            }

            // Set Zone parameter to 1.0 (music only)
            AudioService.SetParameter(audioInstance, "Zone", 1.0f);
            
            // Play audio at origin (non-spatial for inventory)
            AudioService.PlayAudio(audioInstance, Vector3.zero);
            
            // Cache the instance
            playingAudioInstances[item.itemId] = audioInstance;
            
            Debug.Log($"InventoryUI: Playing audio for {item.name} (Zone: 1.0)");
        }

        /// <summary>
        /// Stop audio for a specific item
        /// </summary>
        private void StopAudio(int itemId)
        {
            if (!playingAudioInstances.ContainsKey(itemId))
                return;

            var instance = playingAudioInstances[itemId];
            
            if (AudioService != null && AudioService.IsInstanceValid(instance))
            {
                AudioService.StopAudio(instance, true); // Fade out
                AudioService.ReleaseAudio(instance);
            }

            playingAudioInstances.Remove(itemId);
            Debug.Log($"InventoryUI: Stopped audio for item {itemId}");
        }

        /// <summary>
        /// Stop all currently playing audio
        /// </summary>
        private void StopAllPlayingAudio()
        {
            if (AudioService == null) return;

            foreach (var kvp in playingAudioInstances)
            {
                if (AudioService.IsInstanceValid(kvp.Value))
                {
                    AudioService.StopAudio(kvp.Value, true);
                    AudioService.ReleaseAudio(kvp.Value);
                }
            }

            playingAudioInstances.Clear();
            
            if (playingAudioInstances.Count > 0)
            {
                Debug.Log("InventoryUI: Stopped all playing audio");
            }
        }

        /// <summary>
        /// Update play button icon (play vs pause)
        /// Changes button text to show play ▶ or pause ⏸
        /// </summary>
        private void UpdatePlayButtonIcon(Button playButton, bool isPlaying)
        {
            if (playButton == null) return;
            
            // Change button text (▶ = play, ⏸ = pause)
            var buttonText = playButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = isPlaying ? "⏸" : "▶";
            }
        }

        private void OnDestroy()
        {
            // Stop all audio on destroy
            StopAllPlayingAudio();

            // Remove button listeners
            if (charactersTabButton != null)
                charactersTabButton.onClick.RemoveAllListeners();
            
            if (artifactsTabButton != null)
                artifactsTabButton.onClick.RemoveAllListeners();
            
            if (exitButton != null)
                exitButton.onClick.RemoveAllListeners();
                
            Debug.Log("InventoryUI: Destroyed and cleaned up");
        }
    }
}