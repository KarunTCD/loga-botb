using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LoGa.LudoEngine.Core;
using System.Collections.Generic;

namespace LoGa.LudoEngine.UI
{
    public class SiteSelectionUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject siteSelectionPanel;
        [SerializeField] private Transform siteListContainer;
        [SerializeField] private GameObject siteButtonPrefab;

        [Header("References")]
        private SiteManager siteManager;

        private void Start()
        {
            siteManager = SiteManager.Instance;

            if (siteManager == null)
            {
                Debug.LogError("SiteSelectionUI: SiteManager not found!");
                return;
            }

            // Populate site list
            PopulateSiteList();

            // Show panel
            ShowSiteSelection();
        }

        private void PopulateSiteList()
        {
            var sites = siteManager.GetAvailableSites();

            Debug.Log($"SiteSelectionUI: Populating {sites.Count} sites");

            foreach (var site in sites)
            {
                CreateSiteButton(site);
            }
        }

        private void CreateSiteButton(SiteManager.SiteMetadata site)
        {
            GameObject buttonObj = Instantiate(siteButtonPrefab, siteListContainer);
            buttonObj.name = $"Button_{site.id}";

            // Set button text
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = $"{site.name}\n<size=20>{site.description}</size>";
            }

            // Add click handler
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnSiteSelected(site.id));
            }

            Debug.Log($"SiteSelectionUI: Created button for {site.name}");
        }

        private void OnSiteSelected(string siteId)
        {
            Debug.Log($"SiteSelectionUI: User selected site: {siteId}");

            bool success = siteManager.LoadSite(siteId);

            if (success)
            {
                Debug.Log($"SiteSelectionUI: ✅ Site loaded, hiding selection panel");
                HideSiteSelection();

                // TODO: Start game / show game UI
            }
            else
            {
                Debug.LogError($"SiteSelectionUI: ❌ Failed to load site");
                // TODO: Show error message to user
            }
        }

        public void ShowSiteSelection()
        {
            siteSelectionPanel.SetActive(true);
        }

        public void HideSiteSelection()
        {
            siteSelectionPanel.SetActive(false);
        }
    }
}