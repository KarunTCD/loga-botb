using System;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Utilities;
using System.Collections.Generic;

namespace LoGa.LudoEngine.Services
{
    public class StorageService : MonoBehaviour, IStorageService
    {
        public bool IsInitialized { get; private set; }

        private static readonly Dictionary<string, object> defaultValues = new Dictionary<string, object>
        {
            { "TotalCompletedPOIs", 0 },
            { "CurrentMaxActiveCues", 1 },
            { "HasPlayedWelcomeDialogue", false },

            // Unloacked preferences
            { "POI_william_Unlocked", false},
            { "POI_louis_Unlocked", false},
            { "POI_james_Unlocked", false},
            { "POI_river_boyne_Unlocked", false},
            { "POI_battle_oak_Unlocked", false},
            { "POI_farmer1690_Unlocked", false},
            { "POI_salmon_Unlocked", false},
            { "POI_barber_surgeon_Unlocked", false},
        };

        public Task<bool> InitializeAsync()
        {
            try
            {
                InitializeDefaults();
                IsInitialized = true;
                Debug.Log("Storage service initialized with defaults");
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize storage service: {e.Message}");
                return Task.FromResult(false);
            }
        }

        private void InitializeDefaults()
        {
            int newPrefsCount = 0;
            foreach (var kvp in defaultValues)
            {
                if (!PlayerPrefs.HasKey(kvp.Key))
                {
                    Save(kvp.Key, kvp.Value);
                    newPrefsCount++;
                }
            }
            Debug.Log($"Initialized {newPrefsCount} new preferences with defaults");
        }

        public void Save(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("Storage key cannot be null or empty");
                return;
            }

            if (value == null)
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                Debug.Log($"Deleted key: {key}");
                return;
            }

            try
            {
                switch (value)
                {
                    case string strValue:
                        PlayerPrefs.SetString(key, strValue);
                        break;
                    case int intValue:
                        PlayerPrefs.SetInt(key, intValue);
                        break;
                    case float floatValue:
                        PlayerPrefs.SetFloat(key, floatValue);
                        break;
                    case bool boolValue:
                        PlayerPrefs.SetInt(key, boolValue ? 1 : 0);
                        break;
                    case DateTime dateValue:
                        PlayerPrefs.SetString(key, dateValue.ToBinary().ToString());
                        break;
                    default:
                        // Serialize complex types to JSON
                        string jsonValue = JsonUtility.ToJson(value);
                        PlayerPrefs.SetString(key, jsonValue);
                        break;
                }

                PlayerPrefs.Save();
                Debug.Log($"Saved {value.GetType().Name}: {key} = {value}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save key '{key}': {e.Message}");
            }
        }

        public T Load<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("Storage key cannot be null or empty");
                return default(T);
            }

            if (!PlayerPrefs.HasKey(key))
            {
                // Return default if we have one defined
                if (defaultValues.TryGetValue(key, out object defaultValue))
                {
                    try
                    {
                        return (T)defaultValue;
                    }
                    catch (InvalidCastException)
                    {
                        Debug.LogError($"Cannot cast default value for key '{key}' to type {typeof(T)}");
                    }
                }
                return default(T);
            }

            try
            {
                Type type = typeof(T);

                if (type == typeof(string))
                {
                    return (T)(object)PlayerPrefs.GetString(key);
                }
                else if (type == typeof(int))
                {
                    return (T)(object)PlayerPrefs.GetInt(key);
                }
                else if (type == typeof(float))
                {
                    return (T)(object)PlayerPrefs.GetFloat(key);
                }
                else if (type == typeof(bool))
                {
                    return (T)(object)(PlayerPrefs.GetInt(key) == 1);
                }
                else if (type == typeof(DateTime))
                {
                    string dateString = PlayerPrefs.GetString(key);
                    if (long.TryParse(dateString, out long dateBinary))
                    {
                        return (T)(object)DateTime.FromBinary(dateBinary);
                    }
                    return default(T);
                }
                else
                {
                    // Deserialize from JSON
                    string jsonValue = PlayerPrefs.GetString(key);
                    if (!string.IsNullOrEmpty(jsonValue))
                    {
                        return JsonUtility.FromJson<T>(jsonValue);
                    }
                    return default(T);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load key '{key}': {e.Message}");
                return default(T);
            }
        }

        public bool HasKey(string key)
        {
            return !string.IsNullOrEmpty(key) && PlayerPrefs.HasKey(key);
        }

        public void DeleteKey(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                Debug.Log($"Deleted key: {key}");
            }
        }

        public void ResetToDefault(string key)
        {
            if (defaultValues.TryGetValue(key, out object defaultValue))
            {
                Save(key, defaultValue);
            }
        }

        public void ResetAllToDefaults()
        {
            foreach (var kvp in defaultValues)
            {
                Save(kvp.Key, kvp.Value);
            }
        }

        public void DeleteAll()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("Deleted all storage keys");
        }

        private void OnDisable()
        {
            if (ApplicationState.IsQuitting)
            {
                ServiceLocator.UnregisterService<IStorageService>();
            }
        }
    }
}