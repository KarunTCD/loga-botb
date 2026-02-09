using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// UI for selecting game site
    /// </summary>
    public class SiteSelectionUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Transform siteListContainer;
        [SerializeField] private GameObject siteButtonPrefab;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private Button backButton;
        
        [Header("Loading")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private TextMeshProUGUI loadingText;
        
        private UIManager uiManager;
        private List<GameObject> siteButtons = new List<GameObject>();
        
        private void Start()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackButtonPressed);
            }
            
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(false);
            }
            
            if (titleText != null)
            {
                titleText.text = "Select Game Site";
            }
        }
        
        public void SetUIManager(UIManager manager)
        {
            uiManager = manager;
            Debug.Log("SiteSelectionUI: UIManager reference set");
        }
        
        /// <summary>
        /// Populate site list from SiteManager
        /// </summary>
        public void InitializeSiteList()
        {
            Debug.Log("SiteSelectionUI: Initializing site list");
            
            // Clear existing buttons
            ClearSiteList();
            
            // Get available sites from SiteManager
            if (SiteManager.Instance == null)
            {
                Debug.LogError("SiteSelectionUI: SiteManager not found!");
                return;
            }
            
            var sites = SiteManager.Instance.AvailableSites;
            
            if (sites == null || sites.Count == 0)
            {
                Debug.LogError("SiteSelectionUI: No sites available!");
                return;
            }
            
            Debug.Log($"SiteSelectionUI: Creating buttons for {sites.Count} sites");
            
            // Create button for each site
            foreach (var site in sites)
            {
                CreateSiteButton(site);
            }
        }
        
        private void CreateSiteButton(Site site)
        {
            if (siteButtonPrefab == null || siteListContainer == null)
            {
                Debug.LogError("SiteSelectionUI: Button prefab or container not assigned!");
                return;
            }
            
            GameObject buttonObj = Instantiate(siteButtonPrefab, siteListContainer);
            buttonObj.name = $"SiteButton_{site.id}";
            
            // Set button text
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                string debugBadge = site.isDebug ? " [DEBUG]" : "";
                buttonText.text = $"{site.name}{debugBadge}\n<size=18>{site.description}</size>";
            }
            
            // Add click handler
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnSiteButtonPressed(site));
            }
            
            siteButtons.Add(buttonObj);
            
            Debug.Log($"SiteSelectionUI: Created button for {site.name}");
        }
        
        private async void OnSiteButtonPressed(Site site)
        {
            Debug.Log($"SiteSelectionUI: User selected site: {site.name}");
            
            // Show loading
            ShowLoading($"Loading {site.name}...");
            
            // Load site (await async call)
            bool success = await SiteManager.Instance.LoadSite(site.id);
            
            if (success)
            {
                Debug.Log($"SiteSelectionUI: Site loaded successfully");
                
                // Hide loading
                HideLoading();
                
                // Notify UIManager
                if (uiManager != null)
                {
                    uiManager.OnSiteSelected();
                }
                else
                {
                    Debug.LogError("SiteSelectionUI: UIManager reference not set!");
                }
            }
            else
            {
                Debug.LogError($"SiteSelectionUI: Failed to load site");
                HideLoading();
                ShowError("Failed to load site. Please try again.");
            }
        }
        
        private void OnBackButtonPressed()
        {
            Debug.Log("SiteSelectionUI: Back button pressed");
            
            if (uiManager != null)
            {
                uiManager.OnBackButtonPressed();
            }
        }
        
        private void ShowLoading(string message)
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(true);
            }
            
            if (loadingText != null)
            {
                loadingText.text = message;
            }
            
            // Disable all site buttons during loading
            SetButtonsInteractable(false);
        }
        
        private void HideLoading()
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(false);
            }
            
            // Re-enable buttons
            SetButtonsInteractable(true);
        }
        
        private void ShowError(string message)
        {
            Debug.LogError($"SiteSelectionUI: {message}");
            
            if (loadingText != null)
            {
                loadingText.text = message;
                loadingText.color = Color.red;
            }
        }
        
        private void SetButtonsInteractable(bool interactable)
        {
            foreach (var buttonObj in siteButtons)
            {
                Button button = buttonObj.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable = interactable;
                }
            }
            
            if (backButton != null)
            {
                backButton.interactable = interactable;
            }
        }
        
        private void ClearSiteList()
        {
            foreach (var buttonObj in siteButtons)
            {
                if (buttonObj != null)
                {
                    Destroy(buttonObj);
                }
            }
            
            siteButtons.Clear();
        }
        
        private void OnDestroy()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackButtonPressed);
            }
        }
    }
}