using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Services
{
    /// <summary>
    /// MMRL Head Tracking Provider - Final Working Fusion Implementation
    /// Uses proven Python-script approach for reliable quaternion data
    /// Simplified to match the old working script exactly
    /// </summary>
    public class MMRLHeadTrackingProvider : MonoBehaviour, IHeadTrackingProvider
    {
        // Provider Identity
        public string ProviderName => "MMRL 9DOF";
        public int Priority => 100;
        public bool IsAvailable => CheckAvailability();
        public bool IsConnected { get; private set; }
        public float CurrentHeading => currentHeading;

        [Header("MMRL Configuration")]
        [SerializeField] private string targetDeviceName = "MetaWear";
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool enableRawDataLogging = false;

        [Header("Orientation Testing")]
        [SerializeField] private bool usePitchAsHeading = false;
        [SerializeField] private bool useRollAsHeading = false;

        // Events
        public event Action<float> HeadingUpdated;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> StatusMessage;

        // 3D Orientation tracking
        public Quaternion CurrentOrientation { get; private set; } = Quaternion.identity;
        public Vector3 CurrentEulerAngles => CurrentOrientation.eulerAngles;

        // Bluetooth LE characteristics
        private string serviceUUID = "326A9000-85CB-9195-D9DD-464CFBBAE75A";
        private string readWriteCharacteristicUUID = "326A9001-85CB-9195-D9DD-464CFBBAE75A";
        private string subscribeCharacteristicUUID = "326A9006-85CB-9195-D9DD-464CFBBAE75A";

        // Connection state
        private bool isInitialized = false;
        private bool isTracking = false;
        private string deviceAddress = null;
        private float timeout = 0f;
        private ConnectionState currentState = ConnectionState.None;
        private float currentHeading = 0f;

        // WORKING FUSION COMMANDS (Based on successful Python script approach)
        private readonly byte[] CMD_FUSION_SET_NDOF_MODE = { 0x19, 0x02, 0x01 };           // Set NDOF mode
        private readonly byte[] CMD_FUSION_WRITE_CONFIG = { 0x19, 0x02, 0x01, 0x23 };       // Write config with ranges
        private readonly byte[] CMD_FUSION_ENABLE_QUATERNION = { 0x19, 0x03, 0x08, 0x00 }; // Enable quaternion output
        private readonly byte[] CMD_FUSION_START = { 0x19, 0x01, 0x01 };                   // Start fusion algorithm

        // MONITORING COMMANDS
        private readonly byte[] CMD_READ_CALIBRATION_STATE = { 0x19, 0x8B };                // Monitor calibration
        private readonly byte[] CMD_READ_FUSION_STATUS = { 0x19, 0x81 };                    // Check if running

        private enum ConnectionState
        {
            None,
            Scanning,
            Connecting,
            ConfiguringFusion,
            WaitingForCalibration,
            Streaming,
            Disconnecting
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                StatusMessage?.Invoke("Initializing MMRL fusion provider...");

                if (!IsAvailable)
                {
                    StatusMessage?.Invoke("Bluetooth LE not available on this platform");
                    return false;
                }

                // Android-specific permission check
                #if UNITY_ANDROID && !UNITY_EDITOR
                        StatusMessage?.Invoke("Requesting Android Bluetooth permissions...");
        
                        var permissionService = ServiceLocator.GetService<IPermissionService>();
                        if (permissionService != null)
                        {
                            bool permissionsGranted = await permissionService.RequestBluetoothPermissions();
            
                            if (!permissionsGranted)
                            {
                                StatusMessage?.Invoke("Bluetooth permissions denied - MMRL provider unavailable");
                                return false;
                            }
            
                            StatusMessage?.Invoke("Android Bluetooth permissions granted ✅");
                        }
                        else
                        {
                            Debug.LogWarning("PermissionService not found on Android - MMRL may not work properly");
                        }
                #endif

                // Create TaskCompletionSource to handle the callback-based Bluetooth initialization
                var tcs = new TaskCompletionSource<bool>();

                BluetoothLEHardwareInterface.Initialize(true, false, () =>
                {
                    SetState(ConnectionState.Scanning, 0.1f);
                    isInitialized = true;
                    StatusMessage?.Invoke("MMRL fusion provider initialized");
                    tcs.SetResult(true);
                }, (error) =>
                {
                    StatusMessage?.Invoke($"Bluetooth initialization failed: {error}");
                    tcs.SetResult(false);
                });

                // Wait for the Bluetooth initialization to complete
                return await tcs.Task;
            }
            catch (Exception e)
            {
                StatusMessage?.Invoke($"MMRL initialization failed: {e.Message}");
                Debug.LogError($"MMRL Provider Error: {e}");
                return false;
            }
        }

        public void StartTracking()
        {
            if (!isInitialized)
            {
                StatusMessage?.Invoke("Provider not initialized");
                return;
            }

            isTracking = true;
            StatusMessage?.Invoke("MMRL tracking started");
        }

        public void StopTracking()
        {
            isTracking = false;
            StatusMessage?.Invoke("MMRL tracking stopped");
        }

        public void CalibrateToHeading(float targetHeading)
        {
            StatusMessage?.Invoke($"Calibration to {targetHeading:F1}° - not implemented yet");
        }

        public void Cleanup()
        {
            StopTracking();
            DisconnectDevice();
            isInitialized = false;
            UpdateConnectionStatus(false);
        }

        // -----------------------------------------------
        // Connection Management
        // -----------------------------------------------

        private bool CheckAvailability()
        {
            return Application.platform == RuntimePlatform.Android ||
                   Application.platform == RuntimePlatform.IPhonePlayer;
        }

        private void SetState(ConnectionState newState, float timeoutDuration)
        {
            currentState = newState;
            timeout = timeoutDuration;

            if (enableDebugLogging)
                Debug.Log($"🎯 MMRL State: {newState}");
        }

        private void Update()
        {
            if (!isInitialized) return;

            if (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0f)
                {
                    timeout = 0f;
                    HandleStateTimeout();
                }
            }
        }

        private void HandleStateTimeout()
        {
            switch (currentState)
            {
                case ConnectionState.Scanning:
                    StartDeviceScan();
                    break;

                case ConnectionState.Connecting:
                    AttemptConnection();
                    break;

                case ConnectionState.ConfiguringFusion:
                    StartCoroutine(ConfigureFusion());
                    break;

                case ConnectionState.WaitingForCalibration:
                    // Continuous monitoring handled in coroutine
                    break;
            }
        }

        private void StartDeviceScan()
        {
            StatusMessage?.Invoke("Scanning for MMRL device...");

            BluetoothLEHardwareInterface.ScanForPeripheralsWithServices(null, (address, deviceName) =>
            {
                if (enableDebugLogging)
                    Debug.Log($"Found device: {deviceName} at {address}");

                if (deviceName.Contains(targetDeviceName))
                {
                    StatusMessage?.Invoke($"Found MMRL device: {deviceName}");
                    BluetoothLEHardwareInterface.StopScan();

                    deviceAddress = address;
                    SetState(ConnectionState.Connecting, 0.5f);
                }
            }, null, true);

            SetState(ConnectionState.Scanning, 10f);
        }

        private void AttemptConnection()
        {
            if (string.IsNullOrEmpty(deviceAddress)) return;

            StatusMessage?.Invoke("Connecting to MMRL device...");

            BluetoothLEHardwareInterface.ConnectToPeripheral(deviceAddress, null, null,
                (address, serviceUUID, characteristicUUID) =>
                {
                    if (enableDebugLogging)
                        Debug.Log($"Found characteristic: {serviceUUID}, {characteristicUUID}");

                    if (IsTargetCharacteristic(serviceUUID, characteristicUUID))
                    {
                        OnDeviceConnected();
                    }
                },
                (disconnectAddress) =>
                {
                    StatusMessage?.Invoke("MMRL device disconnected");
                    OnDeviceDisconnected();
                });
        }

        private bool IsTargetCharacteristic(string serviceUUID, string characteristicUUID)
        {
            return IsEqual(serviceUUID, this.serviceUUID) &&
                   (IsEqual(characteristicUUID, readWriteCharacteristicUUID) ||
                    IsEqual(characteristicUUID, subscribeCharacteristicUUID));
        }

        private void OnDeviceConnected()
        {
            UpdateConnectionStatus(true);
            StatusMessage?.Invoke("MMRL connected! Configuring fusion...");

            SubscribeToDataUpdates();
            SetState(ConnectionState.ConfiguringFusion, 2f);
        }

        private void OnDeviceDisconnected()
        {
            UpdateConnectionStatus(false);
            StatusMessage?.Invoke("Reconnecting...");
            SetState(ConnectionState.Scanning, 3f);
        }

        private void SubscribeToDataUpdates()
        {
            StatusMessage?.Invoke("Subscribing to data stream...");

            BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress(
                deviceAddress, serviceUUID, subscribeCharacteristicUUID, null,
                (deviceAddr, characteristic, bytes) =>
                {
                    OnDataReceived(bytes);
                });
        }

        // -----------------------------------------------
        // Fusion Configuration (Proven Working Approach)
        // -----------------------------------------------

        private IEnumerator ConfigureFusion()
        {
            Debug.Log(" === CONFIGURING FUSION (PROVEN APPROACH) ===");
            StatusMessage?.Invoke("Configuring sensor fusion...");

            // Step 1: Set NDOF mode
            SendCommand(CMD_FUSION_SET_NDOF_MODE, "Set NDOF mode");
            yield return new WaitForSeconds(1f);

            // Step 2: Write configuration
            SendCommand(CMD_FUSION_WRITE_CONFIG, "Write fusion config");
            yield return new WaitForSeconds(2f);

            // Step 3: Enable quaternion output
            SendCommand(CMD_FUSION_ENABLE_QUATERNION, "Enable quaternion output");
            yield return new WaitForSeconds(1f);

            // Step 4: Start fusion algorithm
            SendCommand(CMD_FUSION_START, "Start fusion algorithm");
            yield return new WaitForSeconds(2f);

            Debug.Log("=== FUSION CONFIGURED - MONITORING FOR DATA ===");
            StatusMessage?.Invoke("Fusion configured! Move device to calibrate...");

            SetState(ConnectionState.WaitingForCalibration, 0f);
            StartCoroutine(MonitorForQuaternionData());
        }

        private IEnumerator MonitorForQuaternionData()
        {
            Debug.Log("=== MONITORING FOR QUATERNION DATA ===");
            StatusMessage?.Invoke("Waiting for quaternion data...");

            float monitorTime = 0f;

            while (currentState == ConnectionState.WaitingForCalibration)
            {
                yield return new WaitForSeconds(3f);
                monitorTime += 3f;

                // Check calibration status periodically
                SendCommand(CMD_READ_CALIBRATION_STATE, "Check calibration");

                if (monitorTime >= 15f)
                {
                    SendCommand(CMD_READ_FUSION_STATUS, "Check fusion status");
                    monitorTime = 0f;
                }
            }
        }

        // -----------------------------------------------
        // Data Processing
        // -----------------------------------------------

        private void OnDataReceived(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;

            if (enableRawDataLogging)
            {
                string bytesString = System.BitConverter.ToString(bytes);
                Debug.Log($"RAW: [{bytesString}] Len: {bytes.Length}");
            }

            if (bytes.Length >= 2)
            {
                byte module = bytes[0];
                byte register = bytes[1];

                if (module == 0x19) // Sensor Fusion Module
                {
                    HandleSensorFusionResponse(register, bytes);
                }
            }
        }

        private void HandleSensorFusionResponse(byte register, byte[] bytes)
        {
            switch (register)
            {
                case 0x07: // QUATERNION - THE SUCCESS!
                    Debug.Log("=== QUATERNION DATA RECEIVED! ===");
                    ParseQuaternionData(bytes);
                    break;

                case 0x8B: // Calibration state
                    HandleCalibrationState(bytes);
                    break;

                case 0x81: // Fusion status
                    if (bytes.Length >= 3)
                    {
                        bool isRunning = bytes[2] == 1;
                        Debug.Log($"Fusion status: {(isRunning ? "RUNNING" : "STOPPED")}");
                    }
                    break;

                default:
                    if (enableDebugLogging)
                        Debug.Log($"Fusion register: 0x{register:X2}");
                    break;
            }
        }

        private void HandleCalibrationState(byte[] bytes)
        {
            if (bytes.Length >= 5)
            {
                byte accCalib = bytes[2];
                byte gyroCalib = bytes[3];
                byte magCalib = bytes[4];

                Debug.Log($"CALIBRATION: ACC:{accCalib}/3, GYRO:{gyroCalib}/3, MAG:{magCalib}/3");

                bool allCalibrated = (accCalib >= 2 && gyroCalib >= 2 && magCalib >= 2);

                if (allCalibrated)
                {
                    StatusMessage?.Invoke($"Well calibrated! ACC:{accCalib}/3, GYRO:{gyroCalib}/3, MAG:{magCalib}/3");
                }
                else
                {
                    StatusMessage?.Invoke($"Calibrating... ACC:{accCalib}/3, GYRO:{gyroCalib}/3, MAG:{magCalib}/3");
                }
            }
        }

        private void ParseQuaternionData(byte[] bytes)
        {
            try
            {
                if (bytes.Length >= 18) // 2 header + 16 bytes quaternion
                {
                    // Parse quaternion components (little-endian floats) - EXACTLY like old script
                    float w = System.BitConverter.ToSingle(bytes, 2);   // bytes 2-5
                    float x = System.BitConverter.ToSingle(bytes, 6);   // bytes 6-9  
                    float y = System.BitConverter.ToSingle(bytes, 10);  // bytes 10-13
                    float z = System.BitConverter.ToSingle(bytes, 14);  // bytes 14-17

                    // Create Unity quaternion (Unity order: x, y, z, w) - EXACTLY like old script
                    Quaternion deviceOrientation = new Quaternion(x, y, z, w);

                    // Normalize quaternion to ensure it's valid - EXACTLY like old script
                    deviceOrientation = deviceOrientation.normalized;

                    // Store the current orientation
                    CurrentOrientation = deviceOrientation;

                    // Convert quaternion to Euler angles - EXACTLY like old script
                    Vector3 eulerAngles = deviceOrientation.eulerAngles;

                    // Extract heading - TEST DIFFERENT AXES
                    if (usePitchAsHeading)
                    {
                        currentHeading = eulerAngles.x; // Try PITCH for face-up
                    }
                    else if (useRollAsHeading)
                    {
                        currentHeading = eulerAngles.z; // Try ROLL for face-up
                    }
                    else
                    {
                        currentHeading = eulerAngles.y; // Default YAW
                    }

                    // Normalize heading to 0-360
                    currentHeading = (currentHeading + 360f) % 360f;

                    // Trigger events
                    HeadingUpdated?.Invoke(currentHeading);

                    // Debug output (throttled) - showing all axes for comparison
                    if (enableDebugLogging && Time.frameCount % 30 == 0) // More frequent for testing
                    {
                        Debug.Log($"ALL AXES: Pitch={eulerAngles.x:F1}°, Yaw={eulerAngles.y:F1}°, Roll={eulerAngles.z:F1}°");
                        Debug.Log($"CURRENT HEADING: {currentHeading:F1}° (using YAW)");
                        Debug.Log($"PITCH as heading would be: {eulerAngles.x:F1}°");
                        Debug.Log($"ROLL as heading would be: {eulerAngles.z:F1}°");
                    }

                    // Switch to streaming state
                    if (currentState != ConnectionState.Streaming)
                    {
                        SetState(ConnectionState.Streaming, 10f);
                        StatusMessage?.Invoke("QUATERNION streaming (old script method)!");
                    }
                }
                else
                {
                    Debug.LogWarning($" Quaternion data too short: {bytes.Length} bytes, expected 18");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing quaternion: {e.Message}");
            }
        }

        // -----------------------------------------------
        // Helper Methods
        // -----------------------------------------------

        private void SendCommand(byte[] command, string description)
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"Cannot send {description} - not connected");
                return;
            }

            string commandStr = System.BitConverter.ToString(command);
            if (enableDebugLogging)
                Debug.Log($"{description} → [{commandStr}]");

            BluetoothLEHardwareInterface.WriteCharacteristic(
                deviceAddress, serviceUUID, readWriteCharacteristicUUID,
                command, command.Length, true,
                (characteristicUUID) =>
                {
                    if (enableDebugLogging)
                        Debug.Log($"{description} sent successfully");
                });
        }

        private void DisconnectDevice()
        {
            if (!string.IsNullOrEmpty(deviceAddress))
            {
                BluetoothLEHardwareInterface.DisconnectPeripheral(deviceAddress, (address) =>
                {
                    StatusMessage?.Invoke("Disconnected from MMRL");
                });
            }
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (IsConnected != connected)
            {
                IsConnected = connected;
                ConnectionStatusChanged?.Invoke(connected);

                string status = connected ? "MMRL connected" : "MMRL disconnected";
                StatusMessage?.Invoke(status);
            }
        }

        private bool IsEqual(string uuid1, string uuid2)
        {
            return string.Equals(uuid1, uuid2, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------
        // Public Methods for External Use
        // -----------------------------------------------

        /// <summary>
        /// Get current orientation as Euler angles in degrees
        /// </summary>
        /// <returns>Vector3 with x=pitch, y=yaw/heading, z=roll</returns>
        public Vector3 GetEulerAngles()
        {
            return CurrentOrientation.eulerAngles;
        }

        /// <summary>
        /// Get current heading in degrees [0-360] - uses YAW like old script
        /// </summary>
        /// <returns>Heading where 0 = North</returns>
        public float GetHeading()
        {
            return currentHeading;
        }

        /// <summary>
        /// Get raw quaternion from sensor fusion
        /// </summary>
        /// <returns>Normalized quaternion</returns>
        public Quaternion GetQuaternion()
        {
            return CurrentOrientation;
        }

        // -----------------------------------------------
        // Debug Methods
        // -----------------------------------------------

        public void TestConnection()
        {
            StatusMessage?.Invoke($"Test - State: {currentState}, Connected: {IsConnected}");
        }

        public void ForceReconnect()
        {
            DisconnectDevice();
            SetState(ConnectionState.Scanning, 1f);
            StatusMessage?.Invoke("Force reconnect initiated");
        }

        public void ToggleRawDataLogging()
        {
            enableRawDataLogging = !enableRawDataLogging;
            StatusMessage?.Invoke($"Raw data logging: {enableRawDataLogging}");
        }

        public void ToggleDebugLogging()
        {
            enableDebugLogging = !enableDebugLogging;
            StatusMessage?.Invoke($"Debug logging: {enableDebugLogging}");
        }
    }
}