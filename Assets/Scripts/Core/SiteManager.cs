using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Core
{
    public class SiteManager : MonoBehaviour
    {
        public static SiteManager Instance { get; private set; }

        [Header("Current Site")]
        [SerializeField] private string currentSiteId = null;

        [Header("Site Data")]
        private SiteMetadataList siteMetadataList;

        // Services
        private IAudioService audioService;
        private IGameDataService gameDataService;

        // Events
        public event Action<string> OnSiteLoaded;
        public event Action OnSiteUnloaded;

        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Get services
            audioService = ServiceLocator.GetService<IAudioService>();
            gameDataService = ServiceLocator.GetService<IGameDataService>();

            // Load site metadata
            LoadSiteMetadata();
        }

        /// <summary>
        /// Load list of available sites
        /// </summary>
        public bool LoadSiteMetadata()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Sites", "site_metadata.json");

                Debug.Log($"SiteManager: Loading site metadata from: {path}");

                if (!File.Exists(path))
                {
                    Debug.LogError($"SiteManager: site_metadata.json not found at: {path}");
                    return false;
                }

                string json = File.ReadAllText(path);
                siteMetadataList = JsonUtility.FromJson<SiteMetadataList>(json);

                if (siteMetadataList == null || siteMetadataList.sites == null)
                {
                    Debug.LogError("SiteManager: Failed to parse site_metadata.json");
                    return false;
                }

                Debug.Log($"SiteManager: ✅ Loaded {siteMetadataList.sites.Count} sites:");
                foreach (var site in siteMetadataList.sites)
                {
                    Debug.Log($"  - {site.name} ({site.id}) {(site.isDebug ? "[DEBUG]" : "")}");
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"SiteManager: ❌ Failed to load site metadata: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a specific site (banks + data + POIs)
        /// </summary>
        public bool LoadSite(string siteId)
        {
            Debug.Log($"========================================");
            Debug.Log($"SiteManager: Loading site: {siteId}");
            Debug.Log($"========================================");

            // 1. Unload current site if any
            if (currentSiteId != null)
            {
                UnloadCurrentSite();
            }

            // 2. Load FMOD banks
            Debug.Log($"SiteManager: Step 1/3 - Loading FMOD banks...");
            if (!audioService.LoadBanksForSite(siteId))
            {
                Debug.LogError($"SiteManager: ❌ Failed to load audio banks for site: {siteId}");
                return false;
            }
            Debug.Log($"SiteManager: ✓ Banks loaded");

            // 3. Load site JSON data
            Debug.Log($"SiteManager: Step 2/3 - Loading site data JSON...");
            string jsonPath = Path.Combine(Application.streamingAssetsPath, "Sites", siteId, "site_data.json");

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"SiteManager: ❌ site_data.json not found at: {jsonPath}");
                audioService.UnloadAllBanks();
                return false;
            }

            string json = File.ReadAllText(jsonPath);

            // TODO: Parse and use site data
            // For now, just verify it's valid JSON
            try
            {
                var testParse = JsonUtility.FromJson<Dictionary<string, object>>(json);
                Debug.Log($"SiteManager: ✓ Site data JSON valid ({json.Length} characters)");
            }
            catch (Exception e)
            {
                Debug.LogError($"SiteManager: ❌ Invalid site_data.json: {e.Message}");
                audioService.UnloadAllBanks();
                return false;
            }

            // 4. TODO: Create POIs from site data
            Debug.Log($"SiteManager: Step 3/3 - Creating POIs... (TODO)");

            // Store current site
            currentSiteId = siteId;

            Debug.Log($"========================================");
            Debug.Log($"SiteManager: ✅ Site '{siteId}' loaded successfully!");
            Debug.Log($"========================================");

            OnSiteLoaded?.Invoke(siteId);
            return true;
        }

        /// <summary>
        /// Unload current site
        /// </summary>
        public void UnloadCurrentSite()
        {
            if (currentSiteId == null)
            {
                Debug.Log("SiteManager: No site to unload");
                return;
            }

            Debug.Log($"SiteManager: Unloading site: {currentSiteId}");

            // TODO: Destroy POIs when POI system is integrated

            // Unload FMOD banks
            audioService.UnloadAllBanks();

            string previousSite = currentSiteId;
            currentSiteId = null;

            Debug.Log($"SiteManager: ✅ Site '{previousSite}' unloaded");

            OnSiteUnloaded?.Invoke();
        }

        /// <summary>
        /// Get list of available sites
        /// </summary>
        public List<SiteMetadata> GetAvailableSites()
        {
            return siteMetadataList?.sites ?? new List<SiteMetadata>();
        }

        /// <summary>
        /// Get currently loaded site ID
        /// </summary>
        public string GetCurrentSiteId()
        {
            return currentSiteId;
        }

        /// <summary>
        /// Get site metadata by ID
        /// </summary>
        public SiteMetadata GetSiteMetadata(string siteId)
        {
            return siteMetadataList?.sites?.Find(s => s.id == siteId);
        }

        // JSON Data Structures
        [Serializable]
        public class SiteMetadataList
        {
            public List<SiteMetadata> sites;
        }

        [Serializable]
        public class SiteMetadata
        {
            public string id;
            public string name;
            public string description;
            public string folderName;
            public LocationData centerLocation;
            public float activationRadius;
            public bool isDebug;
        }

        [Serializable]
        public class LocationData
        {
            public float latitude;
            public float longitude;
        }
    }
}