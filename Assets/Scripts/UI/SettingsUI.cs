using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// Settings UI - Volume control and gameplay preferences.
    /// Controls Master, SFX, and Ambience volume via FMOD buses.
    /// Controls targeting sounds (lock/unlock feedback) preference.
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

        [Header("Targeting Sounds Toggle")]
        [SerializeField] private Toggle targetingSoundsToggle;
        [SerializeField] private TextMeshProUGUI targetingSoundsLabel;

        [Header("Close Button")]
        [SerializeField] private Button closeButton;

        [Header("Volume Settings")]
        [SerializeField] private float defaultVolume = 0.8f;
        [SerializeField] private float minVolume = 0f;
        [SerializeField] private float maxVolume = 1f;

        private UIManager uiManager;
        private IAudioService audioService;

        // Storage keys
        private const string MASTER_VOLUME_KEY   = "Volume_Master";
        private const string SFX_VOLUME_KEY      = "Volume_SFX";
        private const string AMBIENCE_VOLUME_KEY  = "Volume_Ambience";
        private const string TARGETING_SOUNDS_KEY = "Setting_TargetingSounds";

        private void Start()
        {
            audioService = ServiceLocator.GetService<IAudioService>();

            SetupSliders();
            SetupToggle();
            SetupButtonListeners();
            LoadAllSettings();

            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("SettingsUI: UIManager reference set");
        }

        private void SetupSliders()
        {
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.minValue = minVolume;
                masterVolumeSlider.maxValue = maxVolume;
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.minValue = minVolume;
                sfxVolumeSlider.maxValue = maxVolume;
                sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            }

            if (ambienceVolumeSlider != null)
            {
                ambienceVolumeSlider.minValue = minVolume;
                ambienceVolumeSlider.maxValue = maxVolume;
                ambienceVolumeSlider.onValueChanged.AddListener(OnAmbienceVolumeChanged);
            }
        }

        private void SetupToggle()
        {
            if (targetingSoundsToggle != null)
                targetingSoundsToggle.onValueChanged.AddListener(OnTargetingSoundsToggleChanged);

            if (targetingSoundsLabel != null)
                targetingSoundsLabel.text = "Targeting Sounds";
        }

        private void SetupButtonListeners()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(OnClose);
        }

        private void LoadAllSettings()
        {
            var storageService = ServiceLocator.GetService<IStorageService>();

            if (storageService != null)
            {
                float masterVolume   = storageService.Load<float>(MASTER_VOLUME_KEY,   defaultVolume);
                float sfxVolume      = storageService.Load<float>(SFX_VOLUME_KEY,      defaultVolume);
                float ambienceVolume = storageService.Load<float>(AMBIENCE_VOLUME_KEY, defaultVolume);

                if (masterVolumeSlider != null)   masterVolumeSlider.value   = masterVolume;
                if (sfxVolumeSlider != null)      sfxVolumeSlider.value      = sfxVolume;
                if (ambienceVolumeSlider != null) ambienceVolumeSlider.value = ambienceVolume;

                // Targeting sounds — default on
                // Suppress callback during initial load to avoid POIManager call before game is ready
                bool targetingSoundsEnabled = storageService.Load<bool>(TARGETING_SOUNDS_KEY, false);
                if (targetingSoundsToggle != null)
                {
                    targetingSoundsToggle.onValueChanged.RemoveListener(OnTargetingSoundsToggleChanged);
                    targetingSoundsToggle.isOn = targetingSoundsEnabled;
                    targetingSoundsToggle.onValueChanged.AddListener(OnTargetingSoundsToggleChanged);
                }

                Debug.Log($"SettingsUI: Loaded — Master:{masterVolume:F2} SFX:{sfxVolume:F2} Ambience:{ambienceVolume:F2} TargetingSounds:{targetingSoundsEnabled}");
            }
            else
            {
                Debug.LogWarning("SettingsUI: StorageService not available - using defaults");

                if (masterVolumeSlider != null)    masterVolumeSlider.value    = defaultVolume;
                if (sfxVolumeSlider != null)       sfxVolumeSlider.value       = defaultVolume;
                if (ambienceVolumeSlider != null)  ambienceVolumeSlider.value  = defaultVolume;
                if (targetingSoundsToggle != null) targetingSoundsToggle.isOn  = false;
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

        // ── Volume Handlers ──────────────────────────────────────────────────

        private void OnMasterVolumeChanged(float value)
        {
            audioService?.SetBusVolume("bus:/", value);

            if (masterVolumeText != null)
                masterVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";

            SaveValue(MASTER_VOLUME_KEY, value);
            Debug.Log($"SettingsUI: Master volume → {value:F2}");
        }

        private void OnSFXVolumeChanged(float value)
        {
            audioService?.SetBusVolume("bus:/SFX", value);

            if (sfxVolumeText != null)
                sfxVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";

            SaveValue(SFX_VOLUME_KEY, value);
            Debug.Log($"SettingsUI: SFX volume → {value:F2}");
        }

        private void OnAmbienceVolumeChanged(float value)
        {
            audioService?.SetBusVolume("bus:/Ambient", value);

            if (ambienceVolumeText != null)
                ambienceVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";

            SaveValue(AMBIENCE_VOLUME_KEY, value);
            Debug.Log($"SettingsUI: Ambience volume → {value:F2}");
        }

        // ── Targeting Sounds Handler ─────────────────────────────────────────

        private void OnTargetingSoundsToggleChanged(bool enabled)
        {
            SaveValue(TARGETING_SOUNDS_KEY, enabled);

            if (POIManager.Instance != null)
                POIManager.Instance.SetNavigationSoundsEnabled(enabled);

            Debug.Log($"SettingsUI: Targeting sounds → {(enabled ? "enabled" : "disabled")}");
        }

        // ── Shared Helpers ───────────────────────────────────────────────────

        private void SaveValue(string key, object value)
        {
            ServiceLocator.GetService<IStorageService>()?.Save(key, value);
        }

        private void OnClose()
        {
            Debug.Log("SettingsUI: Close button pressed");
            uiManager?.OnSettingsClose();
        }

        private void OnDestroy()
        {
            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);
            if (ambienceVolumeSlider != null)
                ambienceVolumeSlider.onValueChanged.RemoveListener(OnAmbienceVolumeChanged);
            if (targetingSoundsToggle != null)
                targetingSoundsToggle.onValueChanged.RemoveListener(OnTargetingSoundsToggleChanged);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClose);

            Debug.Log("SettingsUI: Destroyed and cleaned up");
        }
    }
}