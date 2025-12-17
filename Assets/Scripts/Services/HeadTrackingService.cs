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

        [Header("Advanced Provider Management")]
        [SerializeField] private bool enableProviderReliabilityTracking = true;
        [SerializeField] private float reliabilityDecayRate = 0.1f;
        [SerializeField] private float reliabilityBoostRate = 0.05f;
        [SerializeField] private float minReliabilityThreshold = 0.3f;

        // IService Implementation
        public bool IsInitialized { get; private set; }

        // IHeadTrackingService Implementation
        public event Action<float> HeadingUpdated;
        public event Action<string> ActiveProviderChanged;

        public float CurrentHeading => activeProvider?.CurrentHeading ?? 0f;
        public string ActiveProviderName => activeProvider?.ProviderName ?? "None";
        public IReadOnlyList<string> AvailableProviderNames =>
            availableProviders.Select(p => p.ProviderName).ToList().AsReadOnly();

        // Provider Management
        private List<IHeadTrackingProvider> availableProviders = new List<IHeadTrackingProvider>();
        private IHeadTrackingProvider activeProvider;
        private IHeadTrackingProvider lastWorkingProvider;
        private Coroutine switchingCoroutine;

        // Enhanced provider tracking
        private Dictionary<IHeadTrackingProvider, float> providerReliabilityScores = new Dictionary<IHeadTrackingProvider, float>();
        private Dictionary<IHeadTrackingProvider, float> lastHeadingUpdateTime = new Dictionary<IHeadTrackingProvider, float>();
        private Dictionary<IHeadTrackingProvider, int> connectionFailureCount = new Dictionary<IHeadTrackingProvider, int>();

        // Resource management tracking
        private bool isCompassInUse = false;
        private IHeadTrackingProvider compassOwner = null;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.Log("Initializing Head Tracking Service with resource-aware provider management...");

                await DiscoverAndInitializeProviders();
                SelectBestProvider();

                IsInitialized = true;
                Debug.Log($"Head Tracking Service initialized with {availableProviders.Count} providers");
                Debug.Log($"Active provider: {ActiveProviderName}");

                return activeProvider != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize Head Tracking Service: {e.Message}");
                return false;
            }
        }

        public void StartTracking()
        {
            if (activeProvider != null)
            {
                activeProvider.StartTracking();
                if (enableDebugLogging)
                    Debug.Log($"Started tracking with {activeProvider.ProviderName}");
            }
        }

        public void StopTracking()
        {
            if (activeProvider != null)
            {
                activeProvider.StopTracking();
                if (enableDebugLogging)
                    Debug.Log($"Stopped tracking with {activeProvider.ProviderName}");
            }
        }

        // Enhanced provider discovery (same as before)
        private async Task DiscoverAndInitializeProviders()
        {
            Debug.Log($"Discovering providers from {providerPrefabs.Count} prefabs...");

            foreach (var prefab in providerPrefabs)
            {
                if (prefab == null)
                {
                    if (enableDebugLogging)
                        Debug.Log("Null prefab found - skipping (platform-specific provider not available)");
                    continue;
                }

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

                        // Initialize reliability tracking
                        if (enableProviderReliabilityTracking)
                        {
                            providerReliabilityScores[provider] = 1.0f;
                            lastHeadingUpdateTime[provider] = Time.time;
                            connectionFailureCount[provider] = 0;
                        }

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
                    continue;
                }
            }

            // Sort by priority (highest first)
            availableProviders = availableProviders.OrderByDescending(p => p.Priority).ToList();

            Debug.Log($"Provider discovery complete. Available providers: {availableProviders.Count}");
            foreach (var provider in availableProviders)
            {
                Debug.Log($"  {provider.ProviderName} (Priority: {provider.Priority}, Connected: {provider.IsConnected})");
            }

            if (availableProviders.Count == 0)
            {
                Debug.LogWarning("No head tracking providers available on this platform!");
            }
        }

        // ORIGINAL: Resource-aware provider selection
        private void SelectBestProvider()
        {
            // Store last working provider for fallback
            if (activeProvider?.IsConnected == true)
            {
                lastWorkingProvider = activeProvider;
            }

            // Find best provider considering priority and reliability
            IHeadTrackingProvider newProvider;

            if (enableProviderReliabilityTracking)
            {
                newProvider = availableProviders
                    .Where(p => p.IsConnected && GetProviderReliability(p) > minReliabilityThreshold)
                    .OrderByDescending(p => p.Priority * GetProviderReliability(p))
                    .FirstOrDefault();
            }
            else
            {
                newProvider = availableProviders
                    .Where(p => p.IsConnected)
                    .OrderByDescending(p => p.Priority)
                    .FirstOrDefault();
            }

            if (newProvider != activeProvider)
            {
                string previousProvider = activeProvider?.ProviderName ?? "None";

                // CRITICAL: Stop active provider but DON'T cleanup (preserve connections)
                if (activeProvider != null)
                {
                    activeProvider.StopTracking();

                    // Release shared resources only
                    ReleaseSharedResources(activeProvider);

                    if (enableDebugLogging)
                        Debug.Log($"Stopped previous provider: {previousProvider}");
                }

                // CRITICAL: Deactivate competing providers without destroying their connections
                foreach (var provider in availableProviders)
                {
                    if (provider != newProvider && provider.IsConnected)
                    {
                        provider.StopTracking();
                        ReleaseSharedResources(provider);

                        if (enableDebugLogging)
                            Debug.Log($"Deactivated competing provider: {provider.ProviderName}");
                    }
                }

                activeProvider = newProvider;

                if (activeProvider != null)
                {
                    // Acquire shared resources for the new active provider
                    AcquireSharedResources(activeProvider);

                    activeProvider.StartTracking();
                    if (enableDebugLogging)
                        Debug.Log($"Provider switch: {previousProvider} -> {activeProvider.ProviderName}");
                    ActiveProviderChanged?.Invoke(activeProvider.ProviderName);
                }
                else
                {
                    Debug.LogWarning("No connected providers available!");

                    // Try fallback to last working provider
                    if (lastWorkingProvider?.IsConnected == true)
                    {
                        activeProvider = lastWorkingProvider;
                        AcquireSharedResources(activeProvider);
                        activeProvider.StartTracking();
                        if (enableDebugLogging)
                            Debug.Log($"Falling back to {activeProvider.ProviderName}");
                        ActiveProviderChanged?.Invoke(activeProvider.ProviderName);
                    }
                    else
                    {
                        ActiveProviderChanged?.Invoke("None");
                    }
                }
            }
        }

        // Resource management methods
        private void AcquireSharedResources(IHeadTrackingProvider provider)
        {
            if (UsesCompass(provider))
            {
                if (!isCompassInUse)
                {
                    if (enableDebugLogging)
                        Debug.Log($"Acquiring compass for {provider.ProviderName}");

                    // Enable compass for this provider
                    Input.compass.enabled = true;
                    isCompassInUse = true;
                    compassOwner = provider;
                }
                else if (compassOwner != provider)
                {
                    if (enableDebugLogging)
                        Debug.Log($"Compass already in use by {compassOwner?.ProviderName}, transferring to {provider.ProviderName}");

                    compassOwner = provider;
                }
            }
        }

        private void ReleaseSharedResources(IHeadTrackingProvider provider)
        {
            if (UsesCompass(provider) && compassOwner == provider)
            {
                if (enableDebugLogging)
                    Debug.Log($"Releasing compass from {provider.ProviderName}");

                Input.compass.enabled = false;
                isCompassInUse = false;
                compassOwner = null;
            }
        }

        private bool UsesCompass(IHeadTrackingProvider provider)
        {
            // Check if provider uses compass (Phone and AirPods providers do)
            return provider.ProviderName.Contains("Phone") ||
                   provider.ProviderName.Contains("AirPods");
        }

        // Enhanced connection change handling
        private void OnProviderConnectionChanged(bool isConnected)
        {
            if (!enableAutomaticSwitching) return;

            Debug.Log($"Provider connection changed: {isConnected}");

            // Update reliability for the active provider if we can identify connection changes
            if (enableProviderReliabilityTracking && activeProvider != null)
            {
                UpdateProviderReliability(activeProvider, isConnected);
            }

            // Debounce rapid connection changes
            if (switchingCoroutine != null)
                StopCoroutine(switchingCoroutine);

            switchingCoroutine = StartCoroutine(DelayedProviderSwitch());
        }

        private System.Collections.IEnumerator DelayedProviderSwitch()
        {
            yield return new WaitForSeconds(providerSwitchDelay);
            SelectBestProvider();
            switchingCoroutine = null;
        }

        private void OnProviderHeadingUpdated(float heading)
        {
            // Update reliability tracking
            if (enableProviderReliabilityTracking)
            {
                var updatingProvider = availableProviders.FirstOrDefault(p =>
                    Mathf.Approximately(p.CurrentHeading, heading));

                if (updatingProvider != null)
                {
                    UpdateProviderReliability(updatingProvider, true);
                    lastHeadingUpdateTime[updatingProvider] = Time.time;
                }
            }

            // Simply pass through the heading
            HeadingUpdated?.Invoke(heading);
        }

        private void OnProviderStatusMessage(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[{activeProvider?.ProviderName}] {message}");
            }
        }

        // Reliability tracking methods
        private void UpdateProviderReliability(IHeadTrackingProvider provider, bool positiveEvent)
        {
            if (!providerReliabilityScores.ContainsKey(provider))
                providerReliabilityScores[provider] = 1.0f;

            if (positiveEvent)
            {
                providerReliabilityScores[provider] = Mathf.Min(1.0f,
                    providerReliabilityScores[provider] + reliabilityBoostRate);
                connectionFailureCount[provider] = 0;
            }
            else
            {
                providerReliabilityScores[provider] = Mathf.Max(0.0f,
                    providerReliabilityScores[provider] - reliabilityDecayRate);
                connectionFailureCount[provider] = connectionFailureCount.GetValueOrDefault(provider, 0) + 1;
            }
        }

        private float GetProviderReliability(IHeadTrackingProvider provider)
        {
            if (!enableProviderReliabilityTracking || !providerReliabilityScores.ContainsKey(provider))
                return 1.0f;

            float baseReliability = providerReliabilityScores[provider];

            // Factor in recent activity
            float timeSinceLastUpdate = Time.time - lastHeadingUpdateTime.GetValueOrDefault(provider, Time.time);
            if (timeSinceLastUpdate > 5.0f)
            {
                baseReliability *= 0.8f;
            }

            // Factor in connection failures
            int failures = connectionFailureCount.GetValueOrDefault(provider, 0);
            if (failures > 0)
            {
                baseReliability *= Mathf.Pow(0.9f, failures);
            }

            return baseReliability;
        }

        // Public methods for external access
        public void ForceProviderSwitch(string providerName)
        {
            var provider = availableProviders.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null && provider.IsConnected)
            {
                if (activeProvider != null)
                {
                    activeProvider.StopTracking();
                    ReleaseSharedResources(activeProvider);
                }

                activeProvider = provider;
                AcquireSharedResources(activeProvider);
                activeProvider.StartTracking();
                ActiveProviderChanged?.Invoke(activeProvider.ProviderName);

                if (enableDebugLogging)
                    Debug.Log($"Manually switched to provider: {providerName}");
            }
            else
            {
                Debug.LogWarning($"Cannot switch to {providerName} - provider not available or not connected");
            }
        }

        public Dictionary<string, float> GetProviderReliabilityScores()
        {
            if (!enableProviderReliabilityTracking)
                return new Dictionary<string, float>();

            return availableProviders.ToDictionary(
                p => p.ProviderName,
                p => GetProviderReliability(p)
            );
        }

        public void CalibrateActiveProvider(float targetHeading = 0f)
        {
            if (activeProvider != null)
            {
                activeProvider.CalibrateToHeading(targetHeading);
                if (enableDebugLogging)
                    Debug.Log($"Calibrated {activeProvider.ProviderName} to {targetHeading}°");
            }
            else
            {
                Debug.LogWarning("No active provider to calibrate");
            }
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                // Release shared resources first
                if (isCompassInUse)
                {
                    Input.compass.enabled = false;
                    isCompassInUse = false;
                    compassOwner = null;
                }

                // NOW it's safe to cleanup providers
                foreach (var provider in availableProviders)
                {
                    try
                    {
                        provider.Cleanup();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error cleaning up {provider.ProviderName}: {e}");
                    }
                }

                ServiceLocator.UnregisterService<IHeadTrackingService>();
            }
        }

        // Debug Methods
        public void ShowProviderStatus()
        {
            Debug.Log($"=== Head Tracking Service Status ===");
            Debug.Log($"Initialized: {IsInitialized}");
            Debug.Log($"Active Provider: {ActiveProviderName}");
            Debug.Log($"Available Providers: {availableProviders.Count}");
            Debug.Log($"Compass In Use: {isCompassInUse} (Owner: {compassOwner?.ProviderName ?? "None"})");

            foreach (var provider in availableProviders)
            {
                string status = provider.IsConnected ? "Connected" : "Disconnected";
                string active = provider == activeProvider ? " [ACTIVE]" : "";
                Debug.Log($"  • {provider.ProviderName} (Priority: {provider.Priority}) {status}{active}");
            }

            if (enableProviderReliabilityTracking)
            {
                Debug.Log("=== Reliability Scores ===");
                foreach (var provider in availableProviders)
                {
                    float reliability = GetProviderReliability(provider);
                    Debug.Log($"  • {provider.ProviderName}: {reliability:P1}");
                }
            }
        }

        public void ForceProviderReEvaluation()
        {
            Debug.Log("Forcing provider re-evaluation...");
            SelectBestProvider();
        }

        public void ResetReliabilityScores()
        {
            if (enableProviderReliabilityTracking)
            {
                foreach (var provider in availableProviders)
                {
                    providerReliabilityScores[provider] = 1.0f;
                    connectionFailureCount[provider] = 0;
                }
                Debug.Log("Reset all provider reliability scores to 100%");
            }
            else
            {
                Debug.Log("Reliability tracking is disabled");
            }
        }
    }
}