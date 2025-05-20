using System;
using UnityEngine;
using LoGa.LudoEngine.Utilities;

namespace LoGa.LudoEngine.Services
{
    public class LoggingService : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        private bool isInitializing = false;

        // Start is called before the first frame update
        void Initialize()
        {
            // Prevent multiple initialization attempts
            if (IsInitialized || isInitializing)
                return;

            isInitializing = true;

            try
            {
                // further initialization logic needed

                IsInitialized = true;
                Debug.Log("Logging service initialized");
            }
            catch (Exception e)
            {
                Debug.LogError($"Logging service initialization failed: {e.Message}");
                IsInitialized = false;
            }
            finally
            {
                isInitializing = false;
            }
        }

        // Update is called once per frame
        void onDisable()
        {
            if (ApplicationState.IsQuitting)
            {
            }
        }
    }
}