using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Game;
using LoGa.LudoEngine.Utilities;

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
        /// Uses StreamingAssetsHelper for cross-platform compatibility
        /// </summary>
        private async void LoadSiteMetadata()
        {
            try
            {
                Debug.Log($"SiteManager: Loading site metadata");
                Debug.Log($"SiteManager: Platform: {Application.platform}");

                // Use the proven approach from GameDataService
                string json = await StreamingAssetsHelper.LoadTextFileAsync("Sites/site_metadata.json");

                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError($"SiteManager: Failed to load site_metadata.json");
                    availableSites = new List<Site>();
                    return;
                }

                Debug.Log($"SiteManager: JSON length: {json.Length} characters");

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
            Debug.Log($"SiteManager: Loading site: {siteId}");

            Site site = availableSites.Find(s => s.id == siteId);
            if (site == null)
            {
                Debug.LogError($"SiteManager: Site '{siteId}' not found in metadata");
                return false;
            }

            if (currentSite != null)
                UnloadCurrentSite();

            // Banks load from contentFolderName if set, otherwise from folderName
            string contentFolder = string.IsNullOrEmpty(site.contentFolderName)
                ? site.folderName
                : site.contentFolderName;

            Debug.Log($"SiteManager: Loading banks from: {contentFolder}");
            if (audioService == null || !audioService.LoadBanksForSite(contentFolder))
            {
                Debug.LogError($"SiteManager: Failed to load audio banks from {contentFolder}");
                return false;
            }
            Debug.Log($"SiteManager: Banks loaded from {contentFolder}");

            // Game data always loads from site's own folder
            Debug.Log($"SiteManager: Loading game data from: {site.folderName}");
            bool dataLoaded = await gameDataService.LoadSiteData(site.folderName);
            if (!dataLoaded)
            {
                Debug.LogError($"SiteManager: Failed to load site data from {site.folderName}");
                audioService?.UnloadAllBanks();
                return false;
            }
            Debug.Log($"SiteManager: Game data loaded from {site.folderName}");

            currentSite = site;

            if (TimeLayerManager.Instance != null)
            {
                Debug.Log("SiteManager: Notifying TimeLayerManager to reload");
                TimeLayerManager.Instance.ReloadCurrentLayer();
            }

            OnSiteLoaded?.Invoke(site);

            Debug.Log($"SiteManager: Site '{site.name}' loaded successfully (banks: {contentFolder}, data: {site.folderName})");
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