using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LoGa.LudoEngine.Services
{
    public class StorageService : MonoBehaviour, IStorageService
    {
        // Key tracking list stored as JSON
        private const string ALL_KEYS_LIST = "STORAGE_ALL_KEYS";

        // Wrapper class for serializing lists (JsonUtility requirement)
        [Serializable]
        private class KeyList
        {
            public List<string> keys = new List<string>();
        }

        public bool IsInitialized { get; private set; }

        public Task<bool> InitializeAsync()
        {
            try
            {
                Debug.Log("StorageService: Initializing");
                IsInitialized = true;
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"StorageService: Initialization failed - {e.Message}");
                return Task.FromResult(false);
            }
        }

        // ========================================
        // BASIC OPERATIONS
        // ========================================

        public void Save(string key, object value)
        {
            try
            {
                if (value == null)
                {
                    Debug.LogWarning($"StorageService: Attempted to save null value for key '{key}'");
                    return;
                }

                // Track this key
                TrackKey(key);

                // Serialize based on type
                if (value is string stringValue)
                {
                    PlayerPrefs.SetString(key, stringValue);
                }
                else if (value is int intValue)
                {
                    PlayerPrefs.SetInt(key, intValue);
                }
                else if (value is float floatValue)
                {
                    PlayerPrefs.SetFloat(key, floatValue);
                }
                else if (value is bool boolValue)
                {
                    PlayerPrefs.SetInt(key, boolValue ? 1 : 0);
                }
                else
                {
                    // Complex objects - use JsonUtility
                    string json = JsonUtility.ToJson(value);
                    PlayerPrefs.SetString(key, json);
                }

                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogError($"StorageService: Failed to save key '{key}' - {e.Message}");
            }
        }

        public T Load<T>(string key, T defaultValue = default)
        {
            try
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    return defaultValue;
                }

                Type type = typeof(T);

                if (type == typeof(string))
                {
                    return (T)(object)PlayerPrefs.GetString(key, defaultValue?.ToString() ?? "");
                }
                else if (type == typeof(int))
                {
                    return (T)(object)PlayerPrefs.GetInt(key, Convert.ToInt32(defaultValue));
                }
                else if (type == typeof(float))
                {
                    return (T)(object)PlayerPrefs.GetFloat(key, Convert.ToSingle(defaultValue));
                }
                else if (type == typeof(bool))
                {
                    return (T)(object)(PlayerPrefs.GetInt(key, Convert.ToBoolean(defaultValue) ? 1 : 0) == 1);
                }
                else
                {
                    // Complex objects - use JsonUtility
                    string json = PlayerPrefs.GetString(key, "");
                    if (string.IsNullOrEmpty(json))
                    {
                        return defaultValue;
                    }

                    return JsonUtility.FromJson<T>(json);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"StorageService: Failed to load key '{key}' - {e.Message}");
                return defaultValue;
            }
        }

        public bool HasKey(string key)
        {
            return PlayerPrefs.HasKey(key);
        }

        // ========================================
        // BULK OPERATIONS
        // ========================================

        public void DeleteKey(string key)
        {
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                UntrackKey(key);
                PlayerPrefs.Save();
            }
        }

        public void DeleteKeysWithPrefix(string prefix)
        {
            var allKeys = GetAllKeys();
            int deletedCount = 0;

            foreach (var key in allKeys)
            {
                if (key.StartsWith(prefix))
                {
                    PlayerPrefs.DeleteKey(key);
                    UntrackKey(key);
                    deletedCount++;
                }
            }

            if (deletedCount > 0)
            {
                PlayerPrefs.Save();
                Debug.Log($"StorageService: Deleted {deletedCount} keys with prefix '{prefix}'");
            }
        }

        public List<string> GetAllKeys()
        {
            if (!PlayerPrefs.HasKey(ALL_KEYS_LIST))
            {
                return new List<string>();
            }

            string json = PlayerPrefs.GetString(ALL_KEYS_LIST, "");
            try
            {
                KeyList keyList = JsonUtility.FromJson<KeyList>(json);
                return keyList?.keys ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        // ========================================
        // RESET OPERATION
        // ========================================

        public void ResetToDefaults()
        {
            Debug.Log("StorageService: Resetting to defaults - DELETING ALL PLAYERPREFS");

            // Nuclear option - clear everything
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            Debug.Log("StorageService: All PlayerPrefs cleared - app will reinitialize from JSON defaults");
        }

        // ========================================
        // KEY TRACKING (for bulk operations)
        // ========================================

        private void TrackKey(string key)
        {
            if (key == ALL_KEYS_LIST) return; // Don't track the tracking list itself

            var keys = GetAllKeys();
            if (!keys.Contains(key))
            {
                keys.Add(key);

                KeyList keyList = new KeyList { keys = keys };
                string json = JsonUtility.ToJson(keyList);
                PlayerPrefs.SetString(ALL_KEYS_LIST, json);
            }
        }

        private void UntrackKey(string key)
        {
            var keys = GetAllKeys();
            if (keys.Remove(key))
            {
                KeyList keyList = new KeyList { keys = keys };
                string json = JsonUtility.ToJson(keyList);
                PlayerPrefs.SetString(ALL_KEYS_LIST, json);
            }
        }

        // ========================================
        // DEBUG METHODS
        // ========================================

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugPrintAllKeys()
        {
            var keys = GetAllKeys();
            Debug.Log($"=== StorageService: All Keys ({keys.Count}) ===");
            foreach (var key in keys)
            {
                Debug.Log($"  - {key}");
            }
        }
    }
}