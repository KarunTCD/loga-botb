using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Services
{
    /// <summary>
    /// MMRL Head Tracking Provider - Only timeout fixes applied
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
        [SerializeField] private float scanTimeoutDuration = 5f; // TIMEOUT FIX: Configurable timeout

        [Header("Initialization Method")]
        [SerializeField] private bool useMetaWearAppSequence = true;

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
        private string deviceAddress = null;
        private float timeout = 0f;
        private ConnectionState currentState = ConnectionState.None;
        private float currentHeading = 0f;
        private Coroutine scanTimeoutCoroutine; // TIMEOUT FIX: Track timeout coroutine

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

            StatusMessage?.Invoke("MMRL tracking started");
        }

        public void StopTracking()
        {
            StatusMessage?.Invoke("MMRL tracking stopped");

            // TIMEOUT FIX: Clean up any ongoing scan
            if (scanTimeoutCoroutine != null)
            {
                StopCoroutine(scanTimeoutCoroutine);
                scanTimeoutCoroutine = null;
            }
        }

        public void CalibrateToHeading(float targetHeading)
        {
            StatusMessage?.Invoke($"Calibration to {targetHeading:F1}° - not implemented yet");
        }

        public void Cleanup()
        {
            StopTracking();
            DisconnectDevice();

            // TIMEOUT FIX: Clean up timeout coroutine
            if (scanTimeoutCoroutine != null)
            {
                StopCoroutine(scanTimeoutCoroutine);
                scanTimeoutCoroutine = null;
            }

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
                Debug.Log($"MMRL State: {newState}");
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
                    if (useMetaWearAppSequence)
                        StartCoroutine(MetaWearAppConfigurationSequence());
                    else
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

                    // TIMEOUT FIX: Clean up timeout coroutine
                    if (scanTimeoutCoroutine != null)
                    {
                        StopCoroutine(scanTimeoutCoroutine);
                        scanTimeoutCoroutine = null;
                    }

                    deviceAddress = address;
                    SetState(ConnectionState.Connecting, 0.5f);
                }
            }, null, true);

            // TIMEOUT FIX: Start timeout coroutine
            if (scanTimeoutCoroutine != null)
            {
                StopCoroutine(scanTimeoutCoroutine);
            }
            scanTimeoutCoroutine = StartCoroutine(ScanTimeoutCoroutine());
        }

        // TIMEOUT FIX: Proper scan timeout handling
        private IEnumerator ScanTimeoutCoroutine()
        {
            yield return new WaitForSeconds(scanTimeoutDuration);

            if (currentState == ConnectionState.Scanning && deviceAddress == null)
            {
                if (enableDebugLogging)
                    Debug.Log("MMRL: Scan timeout reached, no MetaWear device found");

                BluetoothLEHardwareInterface.StopScan();
                StatusMessage?.Invoke("MMRL scan timeout - no MetaWear device found");

                // Don't mark as failed - just stop scanning and leave disconnected
                currentState = ConnectionState.None;
                scanTimeoutCoroutine = null;
            }
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

            BluetoothLEHardwareInterface.WriteCharacteristic(
                deviceAddress, serviceUUID, subscribeCharacteristicUUID,
                new byte[] { 0x01, 0x00 }, 2, false,
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
        // MetaWear App Configuration Sequence (from HCI logs)
        // -----------------------------------------------

        private IEnumerator MetaWearAppConfigurationSequence()
        {
            Debug.Log("=== METAWEAR APP CONFIGURATION SEQUENCE ===");
            StatusMessage?.Invoke("Using MetaWear app command sequence...");

            SendCommand(CMD_SETTINGS_READ, "Read settings");
            yield return new WaitForSeconds(0.2f);

            SendCommand(CMD_DEBUG_SETUP, "Debug/logging setup");
            yield return new WaitForSeconds(0.2f);

            SendCommand(CMD_DEVICE_SETUP, "Device setup");
            yield return new WaitForSeconds(0.5f);

            Debug.Log("Starting rapid configuration sequence...");

            SendCommand(new byte[] { 0x19, 0x02, 0x01, 0x13 }, "Set fusion mode with parameter 0x13");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x03, 0x03, 0x28, 0x0C }, "Accel config: 0x28, 0x0C");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x13, 0x03, 0x28, 0x00 }, "Gyro config: 0x28, 0x00");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x01, 0x00 }, "Stop magnetometer");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x04, 0x04, 0x0E }, "Mag config: 0x04, 0x0E");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x03, 0x06 }, "Mag preset: 0x06");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x19, 0x07, 0x01 }, "Fusion config: Register 0x07, value 0x01");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x03, 0x02, 0x01, 0x00 }, "Accel data rate config");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x13, 0x02, 0x01, 0x00 }, "Gyro data rate config");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x02, 0x01, 0x00 }, "Mag data rate config");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x03, 0x01, 0x01 }, "Start accelerometer");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x13, 0x01, 0x01 }, "Start gyroscope");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x15, 0x01, 0x01 }, "Start magnetometer");
            yield return new WaitForSeconds(0.1f);

            SendCommand(new byte[] { 0x19, 0x03, 0x08, 0x00 }, "Enable quaternion output");
            yield return new WaitForSeconds(0.2f);

            SendCommand(new byte[] { 0x19, 0x01, 0x01 }, "Start sensor fusion");
            yield return new WaitForSeconds(0.5f);

            Debug.Log("=== METAWEAR APP SEQUENCE COMPLETE ===");
            StatusMessage?.Invoke("MetaWear app sequence complete! Monitoring for data...");

            SetState(ConnectionState.WaitingForCalibration, 0f);
            StartCoroutine(MonitorForQuaternionData());
        }

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

                SendCommand(CMD_READ_CALIBRATION_STATE, "Check calibration");

                if (monitorTime >= 15f)
                {
                    SendCommand(CMD_READ_FUSION_STATUS, "Check fusion status");
                    monitorTime = 0f;
                }
            }
        }

        // Data Processing (same as original)
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

                if (module == 0x19)
                {
                    HandleSensorFusionResponse(register, bytes);
                }
            }
        }

        private void HandleSensorFusionResponse(byte register, byte[] bytes)
        {
            switch (register)
            {
                case 0x07:
                    Debug.Log("=== QUATERNION DATA RECEIVED! ===");
                    ParseQuaternionData(bytes);
                    break;

                case 0x8B:
                    HandleCalibrationState(bytes);
                    break;

                case 0x81:
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
                if (bytes.Length >= 18)
                {
                    float w = System.BitConverter.ToSingle(bytes, 2);
                    float x = System.BitConverter.ToSingle(bytes, 6);
                    float y = System.BitConverter.ToSingle(bytes, 10);
                    float z = System.BitConverter.ToSingle(bytes, 14);

                    Quaternion deviceOrientation = new Quaternion(x, y, z, w).normalized;
                    CurrentOrientation = deviceOrientation;

                    Vector3 eulerAngles = deviceOrientation.eulerAngles;

                    if (usePitchAsHeading)
                    {
                        currentHeading = eulerAngles.x;
                    }
                    else if (useRollAsHeading)
                    {
                        currentHeading = eulerAngles.z;
                    }
                    else
                    {
                        currentHeading = eulerAngles.y;
                    }

                    currentHeading = (currentHeading + 360f) % 360f;
                    HeadingUpdated?.Invoke(currentHeading);

                    if (enableDebugLogging && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"HEADING: {currentHeading:F1}° | Pitch={eulerAngles.x:F1}°, Yaw={eulerAngles.y:F1}°, Roll={eulerAngles.z:F1}°");
                    }

                    if (currentState != ConnectionState.Streaming)
                    {
                        SetState(ConnectionState.Streaming, 10f);
                        StatusMessage?.Invoke("QUATERNION streaming active!");
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

        // Helper Methods (same as original)
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

        // Debug Methods
        [ContextMenu("Test Connection")]
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
    }
}