using System;
public interface IConfigEnvService
{ 
    void Initialize();
    string GetConfig(string key, string defaultValue = "");
    string GetApiKey(string ketName, string defaultValue = ""); // Function to get specific API keys
    event Action<string> ConfigChanged; // Event for config changes(for optional use)
    void ReloadConfig();
}
