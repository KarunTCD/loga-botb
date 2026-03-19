using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Settings UI - Volume control panel
    /// Displays over Main Menu or Pause Menu
    /// Controls Master, SFX, and Ambience volume via FMOD buses
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        [Header("UI Panel")]
        [SerializeField] private GameObject settingsPanel;
        
        [Header("Volume Sliders")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Slider ambienceVolumeSlider;
        
        [Header("Volume Labels")]
        [SerializeField] private TextMeshProUGUI masterVolumeText;
        [SerializeField] private TextMeshProUGUI sfxVolumeText;
        [SerializeField] private TextMeshProUGUI ambienceVolumeText;
        
        [Header("Close Button")]
        [SerializeField] private Button closeButton;
        
        [Header("Volume Settings")]
        [SerializeField] private float defaultVolume = 0.8f;
        [SerializeField] private float minVolume = 0f;
        [SerializeField] private float maxVolume = 1f;

        private UIManager uiManager;
        private IAudioService audioService;
        
        // Storage keys for persistent volume
        private const string MASTER_VOLUME_KEY = "Volume_Master";
        private const string SFX_VOLUME_KEY = "Volume_SFX";
        private const string AMBIENCE_VOLUME_KEY = "Volume_Ambience";

        private void Start()
        {
            audioService = ServiceLocator.GetService<IAudioService>();
            
            SetupSliders();
            SetupButtonListeners();
            LoadVolumeSettings();
            
            // Hide panel initially
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("SettingsUI: UIManager reference set");
        }

        private void SetupSliders()
        {
            // Master volume slider
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.minValue = minVolume;
                masterVolumeSlider.maxValue = maxVolume;
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }
            
            // SFX volume slider
            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.minValue = minVolume;
                sfxVolumeSlider.maxValue = maxVolume;
                sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            }
            
            // Ambience volume slider
            if (ambienceVolumeSlider != null)
            {
                ambienceVolumeSlider.minValue = minVolume;
                ambienceVolumeSlider.maxValue = maxVolume;
                ambienceVolumeSlider.onValueChanged.AddListener(OnAmbienceVolumeChanged);
            }
        }

        private void SetupButtonListeners()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnClose);
            }
        }

        private void LoadVolumeSettings()
        {
            var storageService = ServiceLocator.GetService<IStorageService>();
            
            if (storageService != null)
            {
                // Load saved volumes or use defaults
                float masterVolume = storageService.Load<float>(MASTER_VOLUME_KEY, defaultVolume);
                float sfxVolume = storageService.Load<float>(SFX_VOLUME_KEY, defaultVolume);
                float ambienceVolume = storageService.Load<float>(AMBIENCE_VOLUME_KEY, defaultVolume);
                
                // Set slider values (will trigger onValueChanged callbacks)
                if (masterVolumeSlider != null)
                    masterVolumeSlider.value = masterVolume;
                
                if (sfxVolumeSlider != null)
                    sfxVolumeSlider.value = sfxVolume;
                
                if (ambienceVolumeSlider != null)
                    ambienceVolumeSlider.value = ambienceVolume;
                
                Debug.Log($"SettingsUI: Loaded volumes - Master: {masterVolume:F2}, SFX: {sfxVolume:F2}, Ambience: {ambienceVolume:F2}");
            }
            else
            {
                Debug.LogWarning("SettingsUI: StorageService not available - using default volumes");
                
                // Set defaults
                if (masterVolumeSlider != null)
                    masterVolumeSlider.value = defaultVolume;
                if (sfxVolumeSlider != null)
                    sfxVolumeSlider.value = defaultVolume;
                if (ambienceVolumeSlider != null)
                    ambienceVolumeSlider.value = defaultVolume;
            }
        }

        public void Show()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                settingsPanel.transform.SetAsLastSibling();
                Debug.Log("SettingsUI: Panel shown");
            }
        }

        public void Hide()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
                Debug.Log("SettingsUI: Panel hidden");
            }
        }

        private void OnMasterVolumeChanged(float value)
        {
            // Update FMOD master bus volume
            if (audioService != null)
            {
                audioService.SetBusVolume("bus:/", value);
            }
            
            // Update label
            if (masterVolumeText != null)
            {
                masterVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
            }
            
            // Save to storage
            SaveVolume(MASTER_VOLUME_KEY, value);
            
            Debug.Log($"SettingsUI: Master volume set to {value:F2}");
        }

        private void OnSFXVolumeChanged(float value)
        {
            // Update FMOD SFX bus volume
            if (audioService != null)
            {
                audioService.SetBusVolume("bus:/SFX", value);
            }
            
            // Update label
            if (sfxVolumeText != null)
            {
                sfxVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
            }
            
            // Save to storage
            SaveVolume(SFX_VOLUME_KEY, value);
            
            Debug.Log($"SettingsUI: SFX volume set to {value:F2}");
        }

        private void OnAmbienceVolumeChanged(float value)
        {
            // Update FMOD Ambience bus volume
            if (audioService != null)
            {
                audioService.SetBusVolume("bus:/Ambient", value);
            }
            
            // Update label
            if (ambienceVolumeText != null)
            {
                ambienceVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
            }
            
            // Save to storage
            SaveVolume(AMBIENCE_VOLUME_KEY, value);
            
            Debug.Log($"SettingsUI: Ambience volume set to {value:F2}");
        }

        private void SaveVolume(string key, float value)
        {
            var storageService = ServiceLocator.GetService<IStorageService>();
            if (storageService != null)
            {
                storageService.Save(key, value);
            }
        }

        private void OnClose()
        {
            Debug.Log("SettingsUI: Close button pressed");
            
            if (uiManager != null)
            {
                uiManager.OnSettingsClose();
            }
        }

        private void OnDestroy()
        {
            // Remove slider listeners
            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            
            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);
            
            if (ambienceVolumeSlider != null)
                ambienceVolumeSlider.onValueChanged.RemoveListener(OnAmbienceVolumeChanged);
            
            // Remove button listener
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClose);
            
            Debug.Log("SettingsUI: Destroyed and cleaned up");
        }
    }
}