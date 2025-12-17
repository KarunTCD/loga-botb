using UnityEngine;

namespace LoGa.LudoEngine.Utilities
{
    /// <summary>
    /// Provides application lifecycle information, accessible from anywhere
    /// </summary>
    public static class ApplicationState
    {
        private static bool _isQuitting = false;

        /// <summary>
        /// Returns true if the application is in the process of quitting
        /// </summary>
        public static bool IsQuitting => _isQuitting;

        [RuntimeInitializeOnLoadMethod]
        private static void Initialize()
        {
            // Reset flag when application starts
            _isQuitting = false;

            // Subscribe to application quit event
            Application.quitting += () => _isQuitting = true;
        }
    }
}