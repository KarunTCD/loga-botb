using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;

namespace LoGa.LudoEngine.Services
{
    public class HeadTrackingService : MonoBehaviour, IHeadTrackingService
    {
        [Header("Provider Configuration")]
        [SerializeField] private List<GameObject> providerPrefabs = new List<GameObject>();
        [SerializeField] private float providerSwitchDelay = 2f;
        [SerializeField] private bool enableAutomaticSwitching = true;
        [SerializeField] private bool enableDebugLogging = true;

        // IService Implementation
        public bool IsInitialized { get; private set; }

        // IHeadTrackingService Implementation (unchanged interface for POIManager)
        public event Action<float> HeadingUpdated;
        public event Action<string> ActiveProviderChanged;

        public float CurrentHeading => activeProvider?.CurrentHeading ?? 0f;
        public bool IsCalibrated => activeProvider?.IsCalibrated ?? false;
        public string ActiveProviderName => activeProvider?.ProviderName ?? "None";
        public IReadOnlyList<string> AvailableProviderNames =>
            availableProviders.Select(p => p.ProviderName).ToList().AsReadOnly();

        // Provider Management
        private List<IHeadTrackingProvider> availableProviders = new List<IHeadTrackingProvider>();
        private IHeadTrackingProvider activeProvider;
        private Coroutine switchingCoroutine;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.Log("Initing Head Tracking Service");

                await DiscoverAndInitializeProviders();
                SelectBestProvider();

                IsInitialized = true;
                Debug.Log($"Head Tracking Service initialized with {availableProviders.Count} providers");
                Debug.Log($"Active provider: {ActiveProviderName}");

                return activeProvider != null;
            }
            catch(Exception e)
            {
                Debug.LogError($"Failed to initialize Head Tracking Servie: {e.Message}");
                return false;
            }
        }

        public void StartTracking() => activeProvider?.StartTracking();
        public void StopTracking() => activeProvider?.StopTracking();
        public void CalibrateToNorth() => activeProvider?.CalibrateToNorth();
        public void SetDirectionDegrees(float degrees) => activeProvider?.SetDirectionDegrees(degrees);

        // -----------------------------------------------

        private async Task DiscoverAndInitializeProviders()
        {
            Debug.Log($"Discovering providers from {providerPrefabs.Count} prefabs..");

            foreach (var prefab in providerPrefabs)
            {
                if (prefab == null) continue;

                try
                {
                    var providerObject = Instantiate(prefab, transform);
                    var provider = providerObject.GetComponent<IHeadTrackingProvider>();

                    if (provider == null)
                    {
                        Debug.Log($"Prefab {prefab.name} doesn't implement IHeadTrackingProvider");
                        Destroy(providerObject);
                        continue;
                    }

                    if (!provider.IsAvailable)
                    {
                        Debug.Log($"Provider {provider.ProviderName} not available on this device");
                        Destroy(providerObject);
                        continue;
                    }

                    Debug.Log($"Initializing {provider.ProviderName} (Priority: {provider.Priority})...");
                    bool initialized = await provider.InitializeAsync();

                    if (initialized)
                    {
                        // Subscribe to provider events
                        provider.HeadingUpdated += OnProviderHeadingUpdated;
                        provider.ConnectionStatusChanged += OnProviderConnectionChanged;
                        if (enableDebugLogging)
                            provider.StatusMessage += OnProviderStatusMessage;

                        availableProviders.Add(provider);
                        Debug.Log($"{provider.ProviderName} initialized successfully");
                    }
                    else
                    {
                        Debug.Log($"Failed to initialize {provider.ProviderName}");
                        Destroy(providerObject);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error initializing provider {prefab.name}: {e.Message}");
                }
            }

            // Sort by priority (highest first)
            availableProviders = availableProviders.OrderByDescending(p => p.Priority).ToList();

            Debug.Log("Available providers:");
            foreach(var provider in availableProviders)
            {
                Debug.Log($"{provider.ProviderName} (Priority: {provider.Priority}, Connected: {provider.IsConnected})");
            }
        }

        // -----------------------------------------------

        private void SelectBestProvider()
        {
            // Stop current provider
            if (activeProvider != null)
            {
                activeProvider.StopTracking();
                Debug.Log($"Stopped {activeProvider.ProviderName}");
            }

            // Find highest priority connected provider
            var newProvider = availableProviders
                .Where(p => p.IsConnected)
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();

            if (newProvider != activeProvider)
            {
                string previousProvider = activeProvider?.ProviderName ?? "None";
                activeProvider = newProvider;

                if (activeProvider != null)
                {
                    activeProvider.StartTracking();
                    Debug.Log($"Provider switch: {previousProvider} -> {activeProvider.ProviderName}");
                    ActiveProviderChanged?.Invoke(activeProvider.ProviderName);
                }
                else
                {
                    Debug.LogWarning("No connected providers available!");
                    ActiveProviderChanged?.Invoke("None");
                }
            }
        }

        // -----------------------------------------------

        private void OnProviderConnectionChanged(bool isConnected)
        {
            if (!enableAutomaticSwitching) return;

            Debug.Log($"Provider connection changed: {isConnected}");

            // Debounce rapid connection changes
            if (switchingCoroutine != null)
                StopCoroutine(switchingCoroutine);

            switchingCoroutine = StartCoroutine(DelayedProviderSwitch());
        }

        // -----------------------------------------------

        private System.Collections.IEnumerator DelayedProviderSwitch()
        {
            yield return new WaitForSeconds(providerSwitchDelay);
            SelectBestProvider();
            switchingCoroutine = null;
        }

        // -----------------------------------------------

        private void OnProviderHeadingUpdated(float heading)
        {
            HeadingUpdated?.Invoke(heading);
        }

        // -----------------------------------------------

        private void OnProviderStatusMessage(string message)
        {
            Debug.Log($"[{activeProvider?.ProviderName}] {message}");
        }

        // -----------------------------------------------

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                foreach (var provider in availableProviders)
                {
                    try
                    {
                        provider.Cleanup();
                    }
                    catch(Exception e)
                    {
                        Debug.LogError($"Error cleaning up {provider.ProviderName}: {e}");
                    }
                }

                ServiceLocator.UnregisterService<IHeadTrackingService>();
            }
        }
    }
}