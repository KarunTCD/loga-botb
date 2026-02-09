using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Manages multi-site system - coordinates loading sites and their data
    /// Singleton manager that persists across scenes
    /// </summary>
    public class SiteManager : MonoBehaviour
    {
        public static SiteManager Instance { get; private set; }

        [Header("Site Management")]
        private List<Site> availableSites;
        private Site currentSite;

        // Service references
        private IAudioService audioService;
        private IGameDataService gameDataService;

        public Site CurrentSite => currentSite;
        public List<Site> AvailableSites => availableSites;

        // Events
        public event Action<Site> OnSiteLoaded;
        public event Action OnSiteUnloaded;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("SiteManager: Instance created");
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // Get services (after ServiceManager initializes them)
            audioService = ServiceLocator.GetService<IAudioService>();
            gameDataService = ServiceLocator.GetService<IGameDataService>();

            if (audioService == null)
            {
                Debug.LogError("SiteManager: AudioService not found!");
            }

            if (gameDataService == null)
            {
                Debug.LogError("SiteManager: GameDataService not found!");
            }

            // Load list of available sites
            LoadSiteMetadata();
        }

        /// <summary>
        /// Load site_metadata.json to get list of available sites
        /// </summary>
        private void LoadSiteMetadata()
        {
            try
            {
                string path = Path.Combine(
                    Application.streamingAssetsPath,
                    "Sites",
                    "site_metadata.json"
                );

                Debug.Log($"SiteManager: Loading site metadata from: {path}");
                Debug.Log($"SiteManager: StreamingAssetsPath = {Application.streamingAssetsPath}");

                if (!File.Exists(path))
                {
                    Debug.LogError($"SiteManager: site_metadata.json not found at: {path}");

                    // Check if Sites folder exists
                    string sitesFolder = Path.Combine(Application.streamingAssetsPath, "Sites");
                    Debug.LogError($"SiteManager: Sites folder exists? {Directory.Exists(sitesFolder)}");

                    if (Directory.Exists(sitesFolder))
                    {
                        string[] files = Directory.GetFiles(sitesFolder);
                        Debug.LogError($"SiteManager: Files in Sites folder: {string.Join(", ", files)}");
                    }

                    availableSites = new List<Site>();
                    return;
                }

                Debug.Log($"SiteManager: File exists, reading...");
                string json = File.ReadAllText(path);
                Debug.Log($"SiteManager: JSON length: {json.Length} characters");
                Debug.Log($"SiteManager: JSON content: {json}");

                SiteMetadataList metadata = JsonUtility.FromJson<SiteMetadataList>(json);
                Debug.Log($"SiteManager: Parsed metadata, sites = {metadata?.sites?.Count ?? 0}");

                availableSites = metadata.sites;

                if (availableSites == null)
                {
                    Debug.LogError($"SiteManager: availableSites is null after parsing!");
                    availableSites = new List<Site>();
                    return;
                }

                Debug.Log($"SiteManager: Loaded {availableSites.Count} sites:");
                foreach (var site in availableSites)
                {
                    Debug.Log($"  - {site.name} ({site.id}) {(site.isDebug ? "[DEBUG]" : "")}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"SiteManager: Failed to load site metadata: {e.Message}");
                Debug.LogError($"SiteManager: Stack trace: {e.StackTrace}");
                availableSites = new List<Site>();
            }
        }

        /// <summary>
        /// Load a specific site (banks + data)
        /// Called by UI when user selects a site
        /// </summary>
        public async Task<bool> LoadSite(string siteId)
        {
            Debug.Log($"========================================");
            Debug.Log($"SiteManager: Loading site: {siteId}");
            Debug.Log($"========================================");

            // Find site in metadata
            Site site = availableSites.Find(s => s.id == siteId);
            if (site == null)
            {
                Debug.LogError($"SiteManager: Site '{siteId}' not found in metadata");
                return false;
            }

            // 1. Unload previous site if any
            if (currentSite != null)
            {
                UnloadCurrentSite();
            }

            // 2. Load FMOD banks
            Debug.Log($"SiteManager: Step 1/2 - Loading FMOD banks...");
            if (audioService == null || !audioService.LoadBanksForSite(site.folderName))
            {
                Debug.LogError($"SiteManager: Failed to load audio banks");
                return false;
            }
            Debug.Log($"SiteManager: ✓ Banks loaded");

            // 3. Load site data into GameDataService (await async call)
            Debug.Log($"SiteManager: Step 2/2 - Loading site data...");
            bool dataLoaded = await gameDataService.LoadSiteData(site.folderName);

            if (!dataLoaded)
            {
                Debug.LogError($"SiteManager: Failed to load site data");
                audioService?.UnloadAllBanks();
                return false;
            }
            Debug.Log($"SiteManager: ✓ Site data loaded");

            // 4. Set current site
            currentSite = site;

            // 5. Notify TimeLayerManager to reload (if it exists)
            if (TimeLayerManager.Instance != null)
            {
                Debug.Log("SiteManager: Notifying TimeLayerManager to reload");
                TimeLayerManager.Instance.ReloadCurrentLayer();
            }

            // 6. Fire event
            OnSiteLoaded?.Invoke(site);

            Debug.Log($"========================================");
            Debug.Log($"SiteManager: Site '{site.name}' loaded successfully!");
            Debug.Log($"========================================");

            return true;
        }

        /// <summary>
        /// Unload current site
        /// </summary>
        public void UnloadCurrentSite()
        {
            if (currentSite == null) return;

            Debug.Log($"SiteManager: COMPLETELY unloading site: {currentSite.name}");

            // 1. Unload banks
            audioService?.UnloadAllBanks();

            // 2. Clear game data  
            gameDataService?.ClearSiteData();

            // 3. CRITICAL: Trigger complete system reset
            GameManager.Instance?.ResetForSiteChange();

            // 4. Reset POIManager completely
            if (FindObjectOfType<POIManager>() != null)
            {
                FindObjectOfType<POIManager>().CompleteReset();
            }

            // 5. Reset TimeLayerManager
            if (TimeLayerManager.Instance != null)
            {
                TimeLayerManager.Instance.CompleteReset();
            }

            string previousSite = currentSite.name;
            currentSite = null;

            OnSiteUnloaded?.Invoke();
            Debug.Log($"SiteManager: Site '{previousSite}' COMPLETELY unloaded and reset");
        }

        /// <summary>
        /// Get site metadata by ID
        /// </summary>
        public Site GetSiteMetadata(string siteId)
        {
            return availableSites?.Find(s => s.id == siteId);
        }

        /// <summary>
        /// Check if a site is currently loaded
        /// </summary>
        public bool IsSiteLoaded()
        {
            return currentSite != null && gameDataService != null && gameDataService.IsDataLoaded;
        }
    }
}