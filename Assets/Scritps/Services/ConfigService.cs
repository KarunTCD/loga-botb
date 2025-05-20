using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;
using System.Threading.Tasks;

namespace LoGa.LudoEngine.Services
{
    public class ConfigService : MonoBehaviour, IConfigService
    {
        public bool IsInitialized { get; private set; }
        private bool isInitializing = false;

        // Event for config changes
        public event Action<string> ConfigChanged;

        // Dictionary to store configuration values
        private Dictionary<string, string> configValues = new Dictionary<string, string>();

        // Initialize the service
        public Task<bool> InitializeAsync()
        {
            try
            {
                //LoadConfig();
                isInitializing = true;
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize config service: {e.Message}");
                isInitializing = false;
                return Task.FromResult(false);
            }
        }

        private void LoadConfig()
        {
            // Set the path to the .env file
            string envFilePath = Path.Combine(Application.dataPath, ".env");
            bool configLoaded = false;

            // Check if .env file exists
            if (!File.Exists(envFilePath))
            {
                Debug.LogWarning(".env file not found at: " + envFilePath);

                // Look for .env.template as a fallback
                string templatePath = Path.Combine(Application.dataPath, ".env.template");
                if (File.Exists(templatePath))
                {
                    Debug.LogWarning("Found .env.template - using placeholder values. Replace with real values for production.");
                    ParseConfigFile(templatePath);
                    configLoaded = true;
                }
            }
            else
            {
                ParseConfigFile(envFilePath);
                configLoaded = true;
                Debug.Log("Found .env!");
            }

            if (!configLoaded)
            {
                throw new FileNotFoundException("No configuration file found. Please create a .env file in the Application.dataPath directory.");
            }
        }

        private void ParseConfigFile(string filePath)
        {
            try
            {
                // Read all lines from the file
                string[] lines = File.ReadAllLines(filePath);
                int validConfigCount = 0;

                // Parse each line
                foreach (string line in lines)
                {
                    // Skip comments and empty lines
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    // Split by the first equals sign
                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;

                    string key = line.Substring(0, equalsIndex).Trim();
                    string value = line.Substring(equalsIndex + 1).Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                        value = value.Substring(1, value.Length - 2);

                    // Store in dictionary
                    configValues[key] = value;
                    validConfigCount++;

                    // Log (remove in production)
                    if (value.Length > 0)
                        Debug.Log($"Loaded config: {key}");
                    else
                        Debug.LogWarning($"Empty value for config: {key}");
                }

                Debug.Log($"Config loaded: {validConfigCount} valid entries from {filePath}");

                if (validConfigCount == 0)
                {
                    Debug.LogWarning("No valid configuration entries found in the config file.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error parsing config file: {ex.Message}");
                throw; // Re-throw to handle in the Initialize method
            }
        }

        // IConfigService implementation
        public string GetConfig(string key, string defaultValue = "")
        {
            if (!IsInitialized)
            {
                Debug.LogWarning($"Config service not initialized when getting key: {key}");
            }

            if (configValues.TryGetValue(key, out string value))
                return value;

            Debug.LogWarning($"Config key not found: {key}, using default value");
            return defaultValue;
        }

        public string GetApiKey(string keyName, string defaultValue = "")
        {
            return GetConfig(keyName, defaultValue);
        }

        // Make it possible to reload configuration at runtime
        public void ReloadConfig()
        {
            configValues.Clear();

            try
            {
                LoadConfig();
                ConfigChanged?.Invoke("all");
                Debug.Log("Configuration reloaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to reload configuration: {ex.Message}");
            }
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                ServiceLocator.UnregisterService<IConfigService>();// Only unregister during actual application quit
            }
        }
    }
}