using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.UI
{
    /// <summary>
    /// UI for selecting game site with proximity detection
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

        [Header("Proximity Highlighting")]
        [SerializeField] private float highlightScale = 1.15f;
        [SerializeField] private string youAreHereText = ">> YOU ARE HERE";
        [SerializeField] private string nearestSiteText = ">> NEAREST SITE";
        [SerializeField] private string nearbySiteText = ">> NEARBY";

        private UIManager uiManager;
        private ILocationService locationService;
        private List<GameObject> siteButtons = new List<GameObject>();
        private Site nearestSite;

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

            // Get location service
            locationService = ServiceLocator.GetService<ILocationService>();
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

            // Calculate proximity for each site
            CalculateProximity(sites);

            // Create button for each site
            foreach (var site in sites)
            {
                CreateSiteButton(site);
            }
        }

        /// <summary>
        /// Calculate distance to each site and determine nearest
        /// </summary>
        private void CalculateProximity(List<Site> sites)
        {
            if (locationService == null)
            {
                Debug.LogWarning("SiteSelectionUI: LocationService not available - skipping proximity detection");
                return;
            }

            Vector2 currentLocation = locationService.GetCurrentLocation();

            if (currentLocation == Vector2.zero)
            {
                Debug.LogWarning("SiteSelectionUI: Current location not available - skipping proximity detection");
                return;
            }

            float currentLat = currentLocation.x;
            float currentLon = currentLocation.y;

            Debug.Log($"SiteSelectionUI: Current location: ({currentLat:F6}, {currentLon:F6})");

            // Calculate distance to each site
            float minDistance = float.MaxValue;

            foreach (var site in sites)
            {
                if (site.centerLocation == null)
                {
                    Debug.LogWarning($"SiteSelectionUI: Site {site.name} has no center location");
                    continue;
                }

                float distance = CalculateDistance(
                    currentLat, currentLon,
                    site.centerLocation.latitude, site.centerLocation.longitude
                );

                Debug.Log($"SiteSelectionUI: Distance to {site.name}: {distance:F1}m");

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestSite = site;
                }
            }

            if (nearestSite != null)
            {
                Debug.Log($"SiteSelectionUI: Nearest site is {nearestSite.name} ({minDistance:F1}m away)");
            }
        }

        /// <summary>
        /// Calculate distance between two lat/lon points in meters
        /// </summary>
        private float CalculateDistance(float lat1, float lon1, float lat2, float lon2)
        {
            const float earthRadius = 6371000f; // meters

            float lat1Rad = lat1 * Mathf.Deg2Rad;
            float lat2Rad = lat2 * Mathf.Deg2Rad;
            float latDiff = (lat2 - lat1) * Mathf.Deg2Rad;
            float lonDiff = (lon2 - lon1) * Mathf.Deg2Rad;

            float a = Mathf.Sin(latDiff / 2) * Mathf.Sin(latDiff / 2) +
                     Mathf.Cos(lat1Rad) * Mathf.Cos(lat2Rad) *
                     Mathf.Sin(lonDiff / 2) * Mathf.Sin(lonDiff / 2);

            float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));

            return earthRadius * c;
        }

        /// <summary>
        /// Check if player is within activation radius of a site
        /// </summary>
        private bool IsWithinActivationRadius(Site site)
        {
            if (locationService == null || site.centerLocation == null)
                return false;

            Vector2 currentLocation = locationService.GetCurrentLocation();

            if (currentLocation == Vector2.zero)
                return false;

            float distance = CalculateDistance(
                currentLocation.x, currentLocation.y,
                site.centerLocation.latitude, site.centerLocation.longitude
            );

            return distance <= site.activationRadius;
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

            // Check proximity status
            bool isNearest = (nearestSite != null && site.id == nearestSite.id);
            bool isWithinRadius = IsWithinActivationRadius(site);

            // Set button text with proximity indicators
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                string debugBadge = site.isDebug ? " [DEBUG]" : "";
                string proximityIndicator = "";

                if (isNearest && isWithinRadius)
                {
                    // Player is AT this site
                    proximityIndicator = $"\n<size=22><b>{youAreHereText}</b></size>";
                }
                else if (isNearest)
                {
                    // This is the closest site
                    proximityIndicator = $"\n<size=20>{nearestSiteText}</size>";
                }
                else if (isWithinRadius)
                {
                    // Player is within range
                    proximityIndicator = $"\n<size=20>{nearbySiteText}</size>";
                }

                buttonText.text = $"<b>{site.name}</b>{debugBadge}{proximityIndicator}\n<size=16>{site.description}</size>";
            }

            // Apply scale highlighting for "YOU ARE HERE"
            if (isNearest && isWithinRadius)
            {
                buttonObj.transform.localScale = Vector3.one * highlightScale;
            }

            // Add click handler
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnSiteButtonPressed(site));
            }

            // Move nearest site to top of list
            if (isNearest)
            {
                buttonObj.transform.SetAsFirstSibling();
            }

            siteButtons.Add(buttonObj);

            Debug.Log($"SiteSelectionUI: Created button for {site.name} (Nearest: {isNearest}, WithinRadius: {isWithinRadius})");
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
            nearestSite = null;
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