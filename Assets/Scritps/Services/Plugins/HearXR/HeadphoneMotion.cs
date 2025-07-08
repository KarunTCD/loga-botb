//------------------------------------------------------------------------------
//
// MIT License
//
// Copyright (c) 2020 Anastasia Devana
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//------------------------------------------------------------------------------

//------------------------------------------------------------------------------
// HeadphoneMotion - Unity plugin that exposes the CMHeadphoneMotionManager API
// GitHub: https://github.com/anastasiadevana/HeadphoneMotion
// Integrated into LoGa LudoEngine Services Architecture
// iOS Only - No platform checks needed for functionality, but required for compilation
//------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace LoGa.LudoEngine.Services.Plugins.HearXR
{
    /// <summary>
    /// C# wrapper for the HeadphoneMotion native class.
    /// iOS only - requires iOS 14+ and AirPods Pro
    /// </summary>
    public class HeadphoneMotion : MonoBehaviour
    {
        #region Delegates
        /// <summary>
        /// Head rotation data from the headphone motion manager API.
        /// X, Y, Z, and W correspond to the CMQuaternion values.
        /// </summary>
        public delegate void HeadRotationAction(double x, double y, double z, double w);

        /// <summary>
        /// Headphone connection status change.
        /// </summary>
        public delegate void HeadphoneConnectionAction(bool connected);
        #endregion

        #region Events
        /// <summary>
        /// Head rotation data is available. Passes in raw CMQuaternion x, y, z, w values.
        /// </summary>
        public static HeadRotationAction OnHeadRotationRaw;

        /// <summary>
        /// Head rotation data is available. Passes in a Quaternion ready to use for rotation.
        /// </summary>
        public static event Action<Quaternion> OnHeadRotationQuaternion;

        /// <summary>
        /// Headphones connection status was changed.
        /// </summary>
        public static HeadphoneConnectionAction OnHeadphoneConnectionChanged;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize the Headphone Motion API.
        /// </summary>
        public static void Init()
        {
#if UNITY_IOS && !UNITY_EDITOR
            setHeadphoneConnectionDelegate(HeadphoneConnectionChanged);
            setRotationDelegate(RotationUpdated);
            Debug.Log("[HearXR] HeadphoneMotion initialized");
#else
            Debug.LogWarning("[HearXR] HeadphoneMotion only available on iOS - initialization skipped");
#endif
        }

        /// <summary>
        /// Start listening for headphone connection events and tracking headphone motion.
        /// </summary>
        public static void StartTracking()
        {
#if UNITY_IOS && !UNITY_EDITOR
            startTracking();
            Debug.Log("[HearXR] Started headphone motion tracking");
#else
            Debug.LogWarning("[HearXR] HeadphoneMotion only available on iOS - start tracking skipped");
#endif
        }

        /// <summary>
        /// Stop listening for headphone connection events and tracking headphone motion.
        /// </summary>
        public static void StopTracking()
        {
#if UNITY_IOS && !UNITY_EDITOR
            stopTracking();
            Debug.Log("[HearXR] Stopped headphone motion tracking");
#else
            Debug.LogWarning("[HearXR] HeadphoneMotion only available on iOS - stop tracking skipped");
#endif
        }

        /// <summary>
        /// Check if headphone motion API is available.
        /// </summary>
        public static bool IsHeadphoneMotionAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return isHeadphoneMotionAvailable();
#else
            return false;
#endif
        }

        /// <summary>
        /// Check if headphones are connected.
        /// </summary>
        public static bool AreHeadphonesConnected()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return areHeadphonesConnected();
#else
            return false;
#endif
        }

        /// <summary>
        /// Check if the HeadphoneMotion plugin is properly installed.
        /// </summary>
        public static bool IsPluginAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                return isHeadphoneMotionAvailable();
            }
            catch (Exception e)
            {
                Debug.LogError($"[HearXR] Plugin check failed: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }
        #endregion

        #region Import native class methods - iOS only
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport ("__Internal")]
        private static extern bool isHeadphoneMotionAvailable();
        
        [DllImport ("__Internal")]
        private static extern bool areHeadphonesConnected();
        
        [DllImport("__Internal")]
        private static extern bool startTracking();
        
        [DllImport("__Internal")]
        private static extern bool stopTracking();
        
        [DllImport("__Internal")]
        private static extern void setHeadphoneConnectionDelegate(HeadphoneConnectionAction callback);
        
        [DllImport("__Internal")]
        private static extern void setRotationDelegate(HeadRotationAction callback);
#endif
        #endregion

        #region Handle native class callbacks
        [MonoPInvokeCallback(typeof(HeadphoneConnectionAction))]
        private static void HeadphoneConnectionChanged(bool connected)
        {
            OnHeadphoneConnectionChanged?.Invoke(connected);
        }

        [MonoPInvokeCallback(typeof(HeadRotationAction))]
        private static void RotationUpdated(double x, double y, double z, double w)
        {
            OnHeadRotationRaw?.Invoke(x, y, z, w);

            // Convert to Unity quaternion with proper coordinate system mapping
            Quaternion unityQuaternion = new Quaternion((float)x, (float)z, (float)y, (float)-w);
            OnHeadRotationQuaternion?.Invoke(unityQuaternion);
        }
        #endregion
    }
}