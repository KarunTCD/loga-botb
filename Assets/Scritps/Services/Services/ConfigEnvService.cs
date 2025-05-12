using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

public class ConfigEnvService : MonoBehaviour, IConfigEnvService
{
    // Event for config changes
    public event Action<string> ConfigChanged;

    // Dictionary to store configuration values
    private Dictionary<string, string> configValues = new Dictionary<string, string>();

    // Initialize the service
    public void Initialize()
    {
        LoadConfig();
    }

    private void LoadConfig()
    {
        // Set the path to the .env file
        string envFilePath = Path.Combine(Application.dataPath, ".env");

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
            }

            return;
        }

        ParseConfigFile(envFilePath);
    }

    private void ParseConfigFile(string filePath)
    {
        try
        {
            // Read all lines from the file
            string[] lines = File.ReadAllLines(filePath);

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

                // Log (remove in production)
                if (value.Length > 0)
                    Debug.Log($"Loaded config: {key}");
                else
                    Debug.LogWarning($"Empty value for config: {key}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading config file: {ex.Message}");
        }
    }

    // IConfigService implementation
    public string GetConfig(string key, string defaultValue = "")
    {
        if (configValues.TryGetValue(key, out string value))
            return value;

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
        LoadConfig();
        ConfigChanged?.Invoke("all");
    }
}