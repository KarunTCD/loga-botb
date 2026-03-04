using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LoGa.LudoEngine.Utilities
{
    /// <summary>
    /// Helper class for loading files from StreamingAssets folder
    /// Uses the proven approach from GameDataService.LoadJsonFile()
    /// </summary>
    public static class StreamingAssetsHelper
    {
        /// <summary>
        /// Load text file from StreamingAssets - works on all platforms including Android
        /// This is the EXACT same approach as GameDataService.LoadJsonFile()
        /// </summary>
        /// <param name="relativePath">Path relative to StreamingAssets (e.g., "Sites/site_metadata.json")</param>
        /// <returns>File contents as string, or null if failed</returns>
        public static async Task<string> LoadTextFileAsync(string relativePath)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath);

            Debug.Log($"StreamingAssetsHelper: Loading from {path}");
            Debug.Log($"StreamingAssetsHelper: Platform: {Application.platform}");

            // Android platform - use UnityWebRequest
            if (Application.platform == RuntimePlatform.Android)
            {
                using (var www = UnityWebRequest.Get(path))
                {
                    var operation = www.SendWebRequest();
                    while (!operation.isDone) 
                        await Task.Yield();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"StreamingAssetsHelper: ✓ Loaded from Android StreamingAssets");
                        return www.downloadHandler.text;
                    }
                    else
                    {
                        Debug.LogError($"StreamingAssetsHelper: Failed to load: {www.error}");
                    }
                }
            }
            // Other platforms - use File.ReadAllText
            else if (File.Exists(path))
            {
                string content = await Task.Run(() => File.ReadAllText(path));
                Debug.Log($"StreamingAssetsHelper: ✓ Loaded from StreamingAssets");
                return content;
            }

            // Fallback to Resources
            string resourceName = Path.GetFileNameWithoutExtension(relativePath);
            TextAsset asset = Resources.Load<TextAsset>(resourceName);
            if (asset != null)
            {
                Debug.Log($"StreamingAssetsHelper: ✓ Loaded from Resources folder");
                return asset.text;
            }

            Debug.LogError($"StreamingAssetsHelper: Could not load {relativePath} from StreamingAssets or Resources");
            return null;
        }
        
        /// <summary>
        /// Get platform-appropriate FMOD bank path
        /// Always uses forward slashes for cross-platform compatibility
        /// </summary>
        public static string GetFMODBankPath(string siteId, string bankName)
        {
            // Use forward slashes for all platforms
            return $"Sites/{siteId}/Audio/{bankName}";
        }
    }
}