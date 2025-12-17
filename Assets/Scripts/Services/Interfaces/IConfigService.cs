using System;

namespace LoGa.LudoEngine.Services
{
    public interface IConfigService : IService
    {
        string GetConfig(string key, string defaultValue = "");
        string GetApiKey(string ketName, string defaultValue = ""); // Function to get specific API keys
        event Action<string> ConfigChanged; // Event for config changes(for optional use)
        void ReloadConfig();
    }

}