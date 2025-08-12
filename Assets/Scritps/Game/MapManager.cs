using UnityEngine;
using TMPro;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;
using LoGa.LudoEngine.UI;

namespace LoGa.LudoEngine.Game
{
    public class MapManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI debugText;
        [SerializeField] private POIManager poiManager;
        [SerializeField] private RectTransform mapBackground;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private FixPhraseManager fixPhraseManager;
        [SerializeField] private GameManager gameManager;

        [Header("Map Configuration")]
        [SerializeField] private float northLatitude = 53.36f;
        [SerializeField] private float southLatitude = 53.34f;
        [SerializeField] private float eastLongitude = -6.24f;
        [SerializeField] private float westLongitude = -6.26f;

        [Header("Spectator Mode")]
        [SerializeField] private float joystickMoveSpeed = 0.00001f; // GPS coordinate scale

        // Internal state
        private float spectatorLat = 53.34817f;
        private float spectatorLon = -6.24976f;
        private bool usingJoystickControl = false;
        private int frameCounter = 0;

        // Public properties for other systems to access current position
        public float CurrentLat
        {
            get
            {
                if (GameManager.Instance?.IsSpectatorMode == true)
                {
                    if (usingJoystickControl)
                        return spectatorLat;
                    else if (GameManager.Instance.IsReceivingSpectatorData)
                        return GameManager.Instance.SpectatorLocation.x;
                    else
                        return spectatorLat; // Fallback
                }
                else
                {
                    return LocationService?.CurrentLocation.x ?? spectatorLat;
                }
            }
        }

        public float CurrentLon
        {
            get
            {
                if (GameManager.Instance?.IsSpectatorMode == true)
                {
                    if (usingJoystickControl)
                        return spectatorLon;
                    else if (GameManager.Instance.IsReceivingSpectatorData)
                        return GameManager.Instance.SpectatorLocation.y;
                    else
                        return spectatorLon; // Fallback
                }
                else
                {
                    return LocationService?.CurrentLocation.y ?? spectatorLon;
                }
            }
        }

        // Services
        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();

        private void Update()
        {
            // Skip if game is inactive
            if (GameManager.Instance?.CurrentMode == GameManager.GameMode.Inactive)
                return;

            frameCounter++;

            // Handle input every frame for responsiveness
            HandleInput();

            // Update map display every frame for smooth movement
            UpdateMapDisplay();

            // Update debug text occasionally for readability
            if (frameCounter % 12 == 0) // ~5Hz at 60fps
            {
                UpdateDebugText();
            }
        }

        private void HandleInput()
        {
            // Only handle joystick in spectator mode
            if (GameManager.Instance?.IsSpectatorMode != true || joystick == null)
            {
                if (usingJoystickControl)
                {
                    usingJoystickControl = false;
                    Debug.Log("Exited joystick control");
                }
                return;
            }

            // Check for joystick input
            if (joystick.Input.magnitude > 0.1f)
            {
                if (!usingJoystickControl)
                {
                    usingJoystickControl = true;
                    Debug.Log("Started joystick control");
                }

                // Move spectator position with joystick
                spectatorLat += joystick.Input.y * joystickMoveSpeed;
                spectatorLon += joystick.Input.x * joystickMoveSpeed;

                // Clamp to map bounds
                spectatorLat = Mathf.Clamp(spectatorLat, southLatitude, northLatitude);
                spectatorLon = Mathf.Clamp(spectatorLon, westLongitude, eastLongitude);
            }
            else if (usingJoystickControl)
            {
                // Released joystick - switch back to spectator data
                usingJoystickControl = false;
                Debug.Log("Released joystick - switching to spectator data");
            }
        }

        private void UpdateMapDisplay()
        {
            // Validate references
            if (playerMarker == null || mapBackground == null)
            {
                if (frameCounter % 300 == 0) // Log error occasionally
                {
                    Debug.LogError("MapManager: playerMarker or mapBackground not assigned");
                }
                return;
            }

            // Get current position and heading
            float currentLat = CurrentLat;
            float currentLon = CurrentLon;
            float currentHeading = GetCurrentHeading();

            // Convert to screen coordinates
            Vector2 screenPos = ConvertToScreenPosition(currentLat, currentLon);

            // Update marker position and rotation
            playerMarker.anchoredPosition = screenPos;
            playerMarker.rotation = Quaternion.Euler(0, 0, -currentHeading);
        }

        private float GetCurrentHeading()
        {
            // Use spectator heading if in spectator mode and receiving Firebase data
            if (GameManager.Instance?.IsSpectatorMode == true && GameManager.Instance.IsReceivingSpectatorData && !usingJoystickControl)
            {
                return GameManager.Instance.SpectatorHeading;
            }

            // Otherwise use head tracking service
            return HeadTrackingService?.CurrentHeading ?? 0f;
        }

        private void UpdateDebugText()
        {
            if (debugText == null) return;

            float currentLat = CurrentLat;
            float currentLon = CurrentLon;
            float heading = GetCurrentHeading();
            string locationSource = GetLocationSource();

            // Basic info
            debugText.text = $"Mode: {GameManager.Instance?.CurrentMode}\n" +
                            $"Location: {locationSource}\n" +
                            $"Lat/Long: {currentLat:F6}, {currentLon:F6}\n" +
                            $"Heading: {heading:F1}°\n" +
                            $"Head Provider: {HeadTrackingService?.ActiveProviderName}\n" +
                            $"FPS: {(1.0f / Time.deltaTime):F0}";

            // Add mode-specific info
            if (GameManager.Instance?.IsSpectatorMode == true)
            {
                debugText.text += $"\nJoystick Control: {usingJoystickControl}\n" +
                                 $"Firebase Data: {GameManager.Instance.IsReceivingSpectatorData}";
            }

            // Add fix phrase if available
            if (fixPhraseManager != null)
            {
                string fixPhrase = fixPhraseManager.EncodeLocation(currentLat, currentLon);
                debugText.text += $"\nFix: {fixPhrase}";
            }
        }

        private string GetLocationSource()
        {
            if (GameManager.Instance?.IsSpectatorMode == true)
            {
                if (usingJoystickControl)
                    return "JOYSTICK";
                else if (GameManager.Instance.IsReceivingSpectatorData)
                    return "SPECTATOR";
                else
                    return "SPECTATOR_FALLBACK";
            }
            else
            {
                if (LocationService?.CurrentLocation != Vector2.zero)
                    return "GPS";
                else
                    return "GPS_FALLBACK";
            }
        }

        // Public utility methods
        public Vector2 ConvertToScreenPosition(float latitude, float longitude)
        {
            if (mapBackground == null)
                return Vector2.zero;

            // Normalize coordinates to 0-1 range
            float normalizedX = (longitude - westLongitude) / (eastLongitude - westLongitude);
            float normalizedY = (latitude - southLatitude) / (northLatitude - southLatitude);

            // Convert to screen coordinates
            float xPos = (normalizedX - 0.5f) * mapBackground.rect.width;
            float yPos = (normalizedY - 0.5f) * mapBackground.rect.height;

            return new Vector2(xPos, yPos);
        }

        public Vector2 GetScreenPosition(float latitude, float longitude)
        {
            return ConvertToScreenPosition(latitude, longitude);
        }

        public void SetSpectatorMode(bool isSpectator)
        {
            // Enable/disable joystick based on spectator mode
            if (joystick != null)
                joystick.gameObject.SetActive(isSpectator);

            // Reset joystick control when leaving spectator mode
            if (!isSpectator)
            {
                usingJoystickControl = false;
                Debug.Log("Exited spectator mode - disabled joystick control");
            }
            else
            {
                Debug.Log("Entered spectator mode - joystick enabled");
            }
        }

        // Debug methods
        [ContextMenu("Debug Map Status")]
        public void DebugMapStatus()
        {
            Debug.Log($"=== MapManager Status ===");
            Debug.Log($"Game Mode: {GameManager.Instance?.CurrentMode}");
            Debug.Log($"Current Position: ({CurrentLat:F6}, {CurrentLon:F6})");
            Debug.Log($"Location Source: {GetLocationSource()}");
            Debug.Log($"Current Heading: {GetCurrentHeading():F1}°");
            Debug.Log($"Using Joystick Control: {usingJoystickControl}");
            Debug.Log($"Map Background Size: {mapBackground?.rect.size}");
            Debug.Log($"Player Marker Position: {playerMarker?.anchoredPosition}");
            Debug.Log($"Frame Counter: {frameCounter}");
        }

        [ContextMenu("Reset Spectator Position")]
        public void ResetSpectatorPosition()
        {
            spectatorLat = 53.34817f;
            spectatorLon = -6.24976f;
            usingJoystickControl = false;
            Debug.Log("Reset spectator position to default");
        }

        [ContextMenu("Test Screen Conversion")]
        public void TestScreenConversion()
        {
            float testLat = 53.35f;
            float testLon = -6.25f;
            Vector2 screenPos = ConvertToScreenPosition(testLat, testLon);
            Debug.Log($"Test position ({testLat}, {testLon}) -> Screen: {screenPos}");
        }

        private void OnDestroy()
        {
            // Cleanup if needed
            Debug.Log("MapManager destroyed");
        }

        // Validation
        private void OnValidate()
        {
            // Ensure map bounds are valid
            if (northLatitude <= southLatitude)
            {
                Debug.LogWarning("MapManager: North latitude should be greater than south latitude");
            }

            if (eastLongitude <= westLongitude)
            {
                Debug.LogWarning("MapManager: East longitude should be greater than west longitude");
            }

            // Ensure move speed is reasonable
            if (joystickMoveSpeed <= 0 || joystickMoveSpeed > 0.001f)
            {
                Debug.LogWarning("MapManager: Joystick move speed should be small positive value (e.g., 0.00001)");
            }
        }
    }
}