using UnityEngine;
using System;
using System.Text;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.Core;

namespace LoGa.LudoEngine.Utilities
{
    /// <summary>
    /// Simple utility for generating and storing unique feedback codes per device
    /// </summary>
    public static class FeedbackCodeUtility
    {
        private const string FEEDBACK_CODE_KEY = "FeedbackCode";
        private const int CODE_LENGTH = 4;

        // Use alphanumeric characters excluding confusing ones (0, O, I, 1, etc.)
        private const string CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>
        /// Get the feedback code for this device. Generates one if it doesn't exist.
        /// </summary>
        public static string GetFeedbackCode()
        {
            var storageService = ServiceLocator.GetService<IStorageService>();

            if (storageService == null)
            {
                Debug.LogWarning("FeedbackCodeUtility: StorageService not available, using fallback");
                return GenerateFallbackCode();
            }

            // Try to load existing code
            string existingCode = storageService.Load<string>(FEEDBACK_CODE_KEY);

            if (!string.IsNullOrEmpty(existingCode))
            {
                Debug.Log($"FeedbackCodeUtility: Using existing code: {existingCode}");
                return existingCode;
            }

            // Generate new code
            string newCode = GenerateUniqueCode();
            storageService.Save(FEEDBACK_CODE_KEY, newCode);

            Debug.Log($"FeedbackCodeUtility: Generated new code: {newCode}");
            return newCode;
        }

        /// <summary>
        /// Generate a unique 4-character code based on device identifiers
        /// </summary>
        private static string GenerateUniqueCode()
        {
            // Create a seed based on device-specific identifiers
            string deviceInfo = SystemInfo.deviceUniqueIdentifier +
                               SystemInfo.deviceModel +
                               SystemInfo.deviceName +
                               Application.version;

            // Use hash for consistency across app runs
            int hash = deviceInfo.GetHashCode();

            // Convert to positive number and use as seed
            var random = new System.Random(Math.Abs(hash));

            StringBuilder code = new StringBuilder(CODE_LENGTH);

            for (int i = 0; i < CODE_LENGTH; i++)
            {
                int index = random.Next(CODE_CHARS.Length);
                code.Append(CODE_CHARS[index]);
            }

            return code.ToString();
        }

        /// <summary>
        /// Fallback code generation when StorageService is not available
        /// </summary>
        private static string GenerateFallbackCode()
        {
            // Use PlayerPrefs as fallback
            string existingCode = PlayerPrefs.GetString(FEEDBACK_CODE_KEY, "");

            if (!string.IsNullOrEmpty(existingCode))
            {
                return existingCode;
            }

            string newCode = GenerateUniqueCode();
            PlayerPrefs.SetString(FEEDBACK_CODE_KEY, newCode);
            PlayerPrefs.Save();

            return newCode;
        }

        /// <summary>
        /// Reset the feedback code (for testing purposes)
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void ResetCode()
        {
            var storageService = ServiceLocator.GetService<IStorageService>();

            if (storageService != null)
            {
                storageService.Save(FEEDBACK_CODE_KEY, "");
            }

            PlayerPrefs.DeleteKey(FEEDBACK_CODE_KEY);
            PlayerPrefs.Save();

            Debug.Log("FeedbackCodeUtility: Code reset");
        }

        /// <summary>
        /// Check if a feedback code has been generated for this device
        /// </summary>
        public static bool HasFeedbackCode()
        {
            var storageService = ServiceLocator.GetService<IStorageService>();

            if (storageService != null)
            {
                string code = storageService.Load<string>(FEEDBACK_CODE_KEY);
                return !string.IsNullOrEmpty(code);
            }

            // Fallback to PlayerPrefs
            string fallbackCode = PlayerPrefs.GetString(FEEDBACK_CODE_KEY, "");
            return !string.IsNullOrEmpty(fallbackCode);
        }
    }
}