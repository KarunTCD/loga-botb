using UnityEngine;
using TMPro;

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
    [SerializeField] private float moveSpeed;

    [SerializeField] private float northLatitude = 53.36f;
    [SerializeField] private float southLatitude = 53.34f;
    [SerializeField] private float eastLongitude = -6.24f;
    [SerializeField] private float westLongitude = -6.26f;

    private float currentLat = 53.34817f;
    private float currentLon = -6.24976f;

    public float CurrentLat => currentLat;
    public float CurrentLon => currentLon;

    // Services
    private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();
    private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();

    private void Start()
    {
        // Subscribe to location updates
        LocationService.LocationUpdated += OnLocationUpdated;
    }

    private void Update()
    {
        // Debug joystick control
        if (joystick.Input.magnitude > 0)
        {
            currentLat += joystick.Input.y * moveSpeed;
            currentLon += joystick.Input.x * moveSpeed;
            string fixPhrase = fixPhraseManager.EncodeLocation(currentLat, currentLon);
            UpdateMapDisplay(currentLat, currentLon, fixPhrase);
        }
    }

    private void OnLocationUpdated(float latitude, float longitude)
    {
        // Check for null references
        if (playerMarker == null || poiManager == null)
        {
            Debug.LogError("MapManager: References not set");
            return;
        }

        // Update current latitude/longitude
        currentLat = latitude;
        currentLon = longitude;
        string fixPhrase = fixPhraseManager.EncodeLocation(latitude, longitude);

        // Update only when it's not spectator mode
        if (GameManager.Instance.CurrentMode != GameManager.GameMode.Spectator)
            UpdateMapDisplay(latitude, longitude, fixPhrase);
    }

    private void UpdateMapDisplay(float latitude, float longitude, string fixPhrase)
    {
        float normalizedX = (longitude - westLongitude) / (eastLongitude - westLongitude);
        float normalizedY = (latitude - southLatitude) / (northLatitude - southLatitude);
        float xPos = (normalizedX * mapBackground.rect.width) - (mapBackground.rect.width / 2);
        float yPos = (normalizedY * mapBackground.rect.height) - (mapBackground.rect.height / 2);

        // Set the marker position and rotation
        playerMarker.anchoredPosition = new Vector2(xPos, yPos);
        playerMarker.rotation = Quaternion.Euler(0, 0, -HeadTrackingService.CurrentHeading);

        debugText.text = $"Lat/Long: {latitude:F6}, {longitude:F6}\n" +
                        $"Norm: X:{normalizedX:F3}, Y:{normalizedY:F3}\n" +
                        $"Marker Rotation: {playerMarker.rotation}\n" +
                        $"Heading Angle: {HeadTrackingService.CurrentHeading}";
    }

    public Vector2 GetScreenPosition(float latitude, float longitude)
    {
        float normalizedX = (longitude - westLongitude) / (eastLongitude - westLongitude);
        float normalizedY = (latitude - southLatitude) / (northLatitude - southLatitude);
        float xPos = (normalizedX * mapBackground.rect.width) - (mapBackground.rect.width / 2);
        float yPos = (normalizedY * mapBackground.rect.height) - (mapBackground.rect.height / 2);
        return new Vector2(xPos, yPos);
    }

    public void SetSpectatorMode(bool isSpectator)
    {
        // Disable joystick in spectator mode
        if (joystick != null)
            joystick.gameObject.SetActive(!isSpectator);
    }

    private void OnDestroy()
    {
        try
        {
            var locationService = ServiceLocator.GetService<ILocationService>();
            if (locationService != null) // Since GetService returns default, need null check
            {
                locationService.LocationUpdated -= OnLocationUpdated;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error during MapManager cleanup: {e.Message}");
        }
    }
}