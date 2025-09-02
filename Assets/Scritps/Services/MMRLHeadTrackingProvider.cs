using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Services
{
    /// <summary>
    /// MMRL Head Tracking Provider - Updated with MetaWear App Command Sequence
    /// Uses the exact command sequence discovered from HCI logs with proper state management
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

        [Header("Initialization Method")]
        [SerializeField] private bool useMetaWearAppSequence = true;
        [SerializeField] private bool useOldSequence = false;

        [Header("Connection Management")]
        [SerializeField] private bool enablePeriodicMaintenance = true;
        [SerializeField] private float maintenanceInterval = 30f;
        [SerializeField] private float dataTimeoutThreshold = 5f;
        [SerializeField] private int maxDataLossCount = 3;

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
        private bool configurationComplete = false;

        // Connection health monitoring
        private float lastDataReceiveTime = 0f;
        private int consecutiveDataLossCount = 0;
        private Coroutine maintenanceCoroutine;
        private Coroutine monitoringCoroutine;

        // OLD FUSION COMMANDS (kept for fallback)
        private readonly byte[] CMD_FUSION_SET_NDOF_MODE = { 0x19, 0x02, 0x01 };
        private readonly byte[] CMD_FUSION_WRITE_CONFIG = { 0x19, 0x02, 0x01, 0x23 };
        private readonly byte[] CMD_FUSION_ENABLE_QUATERNION = { 0x19, 0x03, 0x08, 0x00 };
        private readonly byte[] CMD_FUSION_START = { 0x19, 0x01, 0x01 };

        // METAWEAR APP COMMAND SEQUENCE (from HCI logs)
        private readonly byte[] CMD_SETTINGS_READ = { 0x0b, 0x84 };
        private readonly byte[] CMD_DEBUG_SETUP = { 0x11, 0x09, 0x06, 0x00, 0x09, 0x00, 0x00, 0x00, 0x58, 0x02 };
        private readonly byte[] CMD_DEVICE_SETUP = { 0x01, 0x01, 0x01 };

        // MONITORING COMMANDS
        private readonly byte[] CMD_READ_CALIBRATION_STATE = { 0x19, 0x8B };
        private readonly byte[] CMD_READ_FUSION_STATUS = { 0x19, 0x81 };

        // MAINTENANCE COMMANDS
        private readonly byte[] CMD_FLUSH_BUFFER = { 0x11, 0x01 };

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
            
                    StatusMessage?.Invoke("Android Bluetooth permissions granted");
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

            // Stop maintenance coroutine
            if (maintenanceCoroutine != null)
            {
                StopCoroutine(maintenanceCoroutine);
                maintenanceCoroutine = null;
            }

            // Stop monitoring coroutine
            if (monitoringCoroutine != null)
            {
                StopCoroutine(monitoringCoroutine);
                monitoringCoroutine = null;
            }

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
            configurationComplete = false;
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
                Debug.Log($"MMRL State: {newState}");
        }

        private void Update()
        {
            if (!isInitialized) return;

            // Handle state timeouts
            if (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0f)
                {
                    timeout = 0f;
                    HandleStateTimeout();
                }
            }

            // Check for data timeout during streaming
            if (isTracking && IsConnected && currentState == ConnectionState.Streaming)
            {
                float timeSinceLastData = Time.time - lastDataReceiveTime;

                if (timeSinceLastData > dataTimeoutThreshold)
                {
                    consecutiveDataLossCount++;

                    if (consecutiveDataLossCount > maxDataLossCount)
                    {
                        Debug.LogWarning("Data stream timeout - attempting recovery");
                        StartCoroutine(RecoverConnection());
                        consecutiveDataLossCount = 0; // Reset to prevent immediate re-triggering
                    }
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
                    // Only configure if not already done
                    if (!configurationComplete)
                    {
                        if (useMetaWearAppSequence)
                            StartCoroutine(MetaWearAppConfigurationSequence());
                        else
                            StartCoroutine(ConfigureFusion());
                    }
                    break;

                case ConnectionState.WaitingForCalibration:
                case ConnectionState.Streaming:
                    // Don't restart configuration once we reach these states
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

            configurationComplete = false; // Reset configuration flag
            SubscribeToDataUpdates();
            SetState(ConnectionState.ConfiguringFusion, 2f);
        }

        private void OnDeviceDisconnected()
        {
            UpdateConnectionStatus(false);
            configurationComplete = false;

            // Stop maintenance
            if (maintenanceCoroutine != null)
            {
                StopCoroutine(maintenanceCoroutine);
                maintenanceCoroutine = null;
            }

            StatusMessage?.Invoke("Reconnecting...");
            SetState(ConnectionState.Scanning, 3f);
        }

        private void SubscribeToDataUpdates()
        {
            StatusMessage?.Invoke("Subscribing to data stream...");

            // Enable notifications first (from HCI log: 01 00 on handle 0x0022)
            BluetoothLEHardwareInterface.WriteCharacteristic(
                deviceAddress, serviceUUID, subscribeCharacteristicUUID,
                new byte[] { 0x01, 0x00 }, 2, false, // Enable notifications
                (characteristicUUID) =>
                {
                    if (enableDebugLogging)
                        Debug.Log("Notifications enabled successfully");
                });

            BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress(
                deviceAddress, serviceUUID, subscribeCharacteristicUUID, null,
                (deviceAddr, characteristic, bytes) =>
                {
                    OnDataReceived(bytes);
                });
        }

        // -----------------------------------------------
        // Connection Recovery
        // -----------------------------------------------

        private IEnumerator RecoverConnection()
        {
            StatusMessage?.Invoke("Recovering connection...");

            // Try to restart just the data stream without full reconnection
            SendCommand(new byte[] { 0x19, 0x01, 0x00 }, "Stop fusion for recovery");
            yield return new WaitForSeconds(0.5f);

            SendCommand(new byte[] { 0x19, 0x01, 0x01 }, "Restart fusion");
            yield return new WaitForSeconds(1f);

            SendCommand(CMD_FUSION_ENABLE_QUATERNION, "Re-enable quaternion");

            lastDataReceiveTime = Time.time; // Reset timeout
            StatusMessage?.Invoke("Connection recovery attempted");
        }

        // -----------------------------------------------
        // Periodic Maintenance
        // -----------------------------------------------

        private IEnumerator PeriodicMaintenance()
        {
            while (isTracking && IsConnected)
            {
                yield return new WaitForSeconds(maintenanceInterval);

                if (IsConnected && currentState == ConnectionState.Streaming)
                {
                    // Clear potential buffer issues
                    SendCommand(CMD_FLUSH_BUFFER, "Flush buffer");
                    yield return new WaitForSeconds(0.1f);

                    // Check fusion status
                    SendCommand(CMD_READ_FUSION_STATUS, "Check fusion health");

                    if (enableDebugLogging)
                        Debug.Log("Performed periodic maintenance");
                }
            }
        }

        // -----------------------------------------------
        // MetaWear App Configuration Sequence (from HCI logs)
        // -----------------------------------------------

        private IEnumerator MetaWearAppConfigurationSequence()
        {
            Debug.Log("=== METAWEAR APP CONFIGURATION SEQUENCE ===");
            StatusMessage?.Invoke("Using MetaWear app command sequence...");

            // Initial setup commands (from HCI log)
            SendCommand(CMD_SETTINGS_READ, "Read settings");
            yield return new WaitForSeconds(0.2f);

            SendCommand(CMD_DEBUG_SETUP, "Debug/logging setup");
            yield return new WaitForSeconds(0.2f);

            SendCommand(CMD_DEVICE_SETUP, "Device setup");
            yield return new WaitForSeconds(0.5f);

            // The critical rapid sequence (exactly from HCI logs)
            Debug.Log("Starting rapid configuration sequence...");

            // 1. Configure sensor fusion mode with parameter 0x13
            SendCommand(new byte[] { 0x19, 0x02, 0x01, 0x13 }, "Set fusion mode with parameter 0x13");
            yield return new WaitForSeconds(0.1f);

            // 2. Configure accelerometer with specific parameters
            SendCommand(new byte[] { 0x03, 0x03, 0x28, 0x0C }, "Accel config: 0x28, 0x0C");
            yield return new WaitForSeconds(0.1f);

            // 3. Configure gyroscope with specific parameters
            SendCommand(new byte[] { 0x13, 0x03, 0x28, 0x00 }, "Gyro config: 0x28, 0x00");
            yield return new WaitForSeconds(0.1f);

            // 4. Stop magnetometer first
            SendCommand(new byte[] { 0x15, 0x01, 0x00 }, "Stop magnetometer");
            yield return new WaitForSeconds(0.1f);

            // 5. Configure magnetometer with specific parameters
            SendCommand(new byte[] { 0x15, 0x04, 0x04, 0x0E }, "Mag config: 0x04, 0x0E");
            yield return new WaitForSeconds(0.1f);

            // 6. Set magnetometer preset
            SendCommand(new byte[] { 0x15, 0x03, 0x06 }, "Mag preset: 0x06");
            yield return new WaitForSeconds(0.1f);

            // 7. CRITICAL: Configure fusion output register 0x07
            SendCommand(new byte[] { 0x19, 0x07, 0x01 }, "Fusion config: Register 0x07, value 0x01");
            yield return new WaitForSeconds(0.1f);

            // 8. Configure individual sensor data rates (optimized)
            SendCommand(new byte[] { 0x03, 0x02, 0x01, 0x00 }, "Accel data rate config");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x13, 0x02, 0x01, 0x00 }, "Gyro data rate config");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x02, 0x01, 0x00 }, "Mag data rate config");
            yield return new WaitForSeconds(0.1f);

            // 9. Start individual sensors in specific order
            SendCommand(new byte[] { 0x03, 0x01, 0x01 }, "Start accelerometer");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x13, 0x01, 0x01 }, "Start gyroscope");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x01, 0x01 }, "Start magnetometer");
            yield return new WaitForSeconds(0.1f);

            // 10. Enable quaternion output
            SendCommand(new byte[] { 0x19, 0x03, 0x08, 0x00 }, "Enable quaternion output");
            yield return new WaitForSeconds(0.2f);

            // 11. Start sensor fusion
            SendCommand(new byte[] { 0x19, 0x01, 0x01 }, "Start sensor fusion");
            yield return new WaitForSeconds(0.5f);

            Debug.Log("=== METAWEAR APP SEQUENCE COMPLETE ===");
            StatusMessage?.Invoke("MetaWear app sequence complete! Monitoring for data...");
            configurationComplete = true;

            SetState(ConnectionState.WaitingForCalibration, 0f);
            monitoringCoroutine = StartCoroutine(MonitorForQuaternionData());
        }

        // -----------------------------------------------
        // Old Configuration (kept as fallback)
        // -----------------------------------------------

        private IEnumerator ConfigureFusion()
        {
            Debug.Log("=== CONFIGURING FUSION (OLD APPROACH) ===");
            StatusMessage?.Invoke("Using old configuration sequence...");

            SendCommand(CMD_FUSION_SET_NDOF_MODE, "Set NDOF mode");
            yield return new WaitForSeconds(1f);

            SendCommand(CMD_FUSION_WRITE_CONFIG, "Write fusion config");
            yield return new WaitForSeconds(2f);

            SendCommand(CMD_FUSION_ENABLE_QUATERNION, "Enable quaternion output");
            yield return new WaitForSeconds(1f);

            SendCommand(CMD_FUSION_START, "Start fusion algorithm");
            yield return new WaitForSeconds(2f);

            Debug.Log("=== OLD FUSION CONFIGURED - MONITORING FOR DATA ===");
            StatusMessage?.Invoke("Old fusion configured! Move device to calibrate...");
            configurationComplete = true;

            SetState(ConnectionState.WaitingForCalibration, 0f);
            monitoringCoroutine = StartCoroutine(MonitorForQuaternionData());
        }

        private IEnumerator MonitorForQuaternionData()
        {
            Debug.Log("=== MONITORING FOR QUATERNION DATA ===");
            StatusMessage?.Invoke("Waiting for quaternion data...");

            float monitorTime = 0f;
            int maxMonitoringTime = 30; // Stop monitoring after 30 seconds

            while (currentState == ConnectionState.WaitingForCalibration && monitorTime < maxMonitoringTime)
            {
                yield return new WaitForSeconds(3f);
                monitorTime += 3f;

                // Only send monitoring commands if still waiting for data
                if (currentState == ConnectionState.WaitingForCalibration && IsConnected)
                {
                    SendCommand(CMD_READ_CALIBRATION_STATE, "Check calibration");

                    if (monitorTime >= 15f)
                    {
                        SendCommand(CMD_READ_FUSION_STATUS, "Check fusion status");
                    }
                }
                else
                {
                    Debug.Log("Data received or disconnected - stopping monitoring loop");
                    break;
                }
            }

            if (monitorTime >= maxMonitoringTime && currentState == ConnectionState.WaitingForCalibration)
            {
                Debug.LogWarning("Monitoring timeout - no quaternion data received");
                StatusMessage?.Invoke("No quaternion data received - check device");
            }
        }

        // -----------------------------------------------
        // Data Processing
        // -----------------------------------------------

        private void OnDataReceived(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;

            lastDataReceiveTime = Time.time;
            consecutiveDataLossCount = 0; // Reset on successful data

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
                    // Parse quaternion components (little-endian floats)
                    float w = System.BitConverter.ToSingle(bytes, 2);   // bytes 2-5
                    float x = System.BitConverter.ToSingle(bytes, 6);   // bytes 6-9  
                    float y = System.BitConverter.ToSingle(bytes, 10);  // bytes 10-13
                    float z = System.BitConverter.ToSingle(bytes, 14);  // bytes 14-17

                    // Create Unity quaternion (Unity order: x, y, z, w)
                    Quaternion deviceOrientation = new Quaternion(x, y, z, w);

                    // Normalize quaternion to ensure it's valid
                    deviceOrientation = deviceOrientation.normalized;

                    // Store the current orientation
                    CurrentOrientation = deviceOrientation;

                    // Convert quaternion to Euler angles
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

                    // Debug output (throttled)
                    if (enableDebugLogging && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"HEADING: {currentHeading:F1}° | Pitch={eulerAngles.x:F1}°, Yaw={eulerAngles.y:F1}°, Roll={eulerAngles.z:F1}°");
                    }

                    // Switch to streaming state
                    if (currentState != ConnectionState.Streaming)
                    {
                        SetState(ConnectionState.Streaming, 0f); // No timeout in streaming
                        StatusMessage?.Invoke("QUATERNION streaming active!");

                        // Stop monitoring coroutine once streaming starts
                        if (monitoringCoroutine != null)
                        {
                            StopCoroutine(monitoringCoroutine);
                            monitoringCoroutine = null;
                        }

                        // Start periodic maintenance
                        if (enablePeriodicMaintenance && maintenanceCoroutine == null)
                        {
                            maintenanceCoroutine = StartCoroutine(PeriodicMaintenance());
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"Quaternion data too short: {bytes.Length} bytes, expected 18");
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
                Debug.Log($"{description} -> [{commandStr}]");

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

        public Vector3 GetEulerAngles()
        {
            return CurrentOrientation.eulerAngles;
        }

        public float GetHeading()
        {
            return currentHeading;
        }

        public Quaternion GetQuaternion()
        {
            return CurrentOrientation;
        }

        // -----------------------------------------------
        // Debug Methods
        // -----------------------------------------------

        [ContextMenu("Test MetaWear App Sequence")]
        public void TestMetaWearAppSequence()
        {
            if (IsConnected)
            {
                configurationComplete = false;
                StartCoroutine(MetaWearAppConfigurationSequence());
            }
            else
            {
                StatusMessage?.Invoke("Not connected - cannot test sequence");
            }
        }

        [ContextMenu("Test Old Sequence")]
        public void TestOldSequence()
        {
            if (IsConnected)
            {
                configurationComplete = false;
                StartCoroutine(ConfigureFusion());
            }
            else
            {
                StatusMessage?.Invoke("Not connected - cannot test sequence");
            }
        }

        public void TestConnection()
        {
            StatusMessage?.Invoke($"Test - State: {currentState}, Connected: {IsConnected}");
        }

        public void ForceReconnect()
        {
            DisconnectDevice();
            configurationComplete = false;
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