using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
    public class Berry
    {
        public string id { get; private set; }
        public Vector2 gpsPosition { get; private set; }
        public Vector3 worldPosition { get; private set; }

        // GPS-friendly distances accounting for accuracy issues
        private const float SPAWN_MIN_DISTANCE = 6f;   // Minimum 8m from player
        private const float SPAWN_MAX_DISTANCE = 10f;  // Maximum 10m from player  
        private const float COLLECTION_RADIUS = 5f;    // 3m collection radius for GPS accuracy

        private ILocationService LocationService => ServiceLocator.GetService<ILocationService>();

        public Berry(string berryId, Vector2 playerLocation, float bearingDegrees, float distance)
        {
            id = berryId;

            // Clamp distance to safe GPS range
            distance = Mathf.Clamp(distance, SPAWN_MIN_DISTANCE, SPAWN_MAX_DISTANCE);

            // Calculate GPS position using bearing and distance
            gpsPosition = CalculateGPSPosition(playerLocation, bearingDegrees, distance);

            Debug.Log($"[TEST] Berry {id} created at GPS: {gpsPosition}, World: {worldPosition}, Distance: {distance:F1}m");
        }

        public bool CheckCollection()
        {
            Vector2 currentPlayerLocation = LocationService.GetCurrentLocation();
            float distanceToPlayer = CalculateGPSDistance(currentPlayerLocation, gpsPosition);

            Debug.Log($"[TEST] Berry {id} distance check: {distanceToPlayer:F1}m (collection radius: {COLLECTION_RADIUS}m)");

            return distanceToPlayer <= COLLECTION_RADIUS;
        }

        public Vector3 GetAudioPosition()
        {
            // Calculate audio position using player location and heading (like POIs)
            Vector2 playerLocation = LocationService.GetCurrentLocation();
            float playerHeading = ServiceLocator.GetService<IHeadTrackingService>().CurrentHeading;

            worldPosition = CalculateAudioPosition(playerLocation, playerHeading);
            return worldPosition;
        }

        // Calculate audio position relative to player orientation (matching POI behavior)
        private Vector3 CalculateAudioPosition(Vector2 playerLocation, float playerHeading)
        {
            // Calculate bearing from player to berry
            float bearing = CalculateBearing(playerLocation.x, playerLocation.y, gpsPosition.x, gpsPosition.y);

            // Calculate distance
            float distance = CalculateGPSDistance(playerLocation, gpsPosition);

            // Convert to relative angle (bearing relative to player's heading)
            float relativeAngle = bearing - playerHeading;

            // Convert to Unity world coordinates
            float x = distance * Mathf.Sin(relativeAngle * Mathf.Deg2Rad);
            float z = distance * Mathf.Cos(relativeAngle * Mathf.Deg2Rad);

            return new Vector3(x, 0f, z);
        }

        // Calculate bearing from point A to point B (matching POI calculation)
        private float CalculateBearing(float lat1, float lon1, float lat2, float lon2)
        {
            float lat1Rad = lat1 * Mathf.Deg2Rad;
            float lat2Rad = lat2 * Mathf.Deg2Rad;
            float deltaLonRad = (lon2 - lon1) * Mathf.Deg2Rad;

            float x = Mathf.Sin(deltaLonRad) * Mathf.Cos(lat2Rad);
            float y = Mathf.Cos(lat1Rad) * Mathf.Sin(lat2Rad) - Mathf.Sin(lat1Rad) * Mathf.Cos(lat2Rad) * Mathf.Cos(deltaLonRad);

            float bearing = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
            return (bearing + 360f) % 360f; // Normalize to 0-360
        }

        public Vector2 GetGPSPosition()
        {
            return gpsPosition;
        }

        // Calculate GPS coordinates from bearing and distance
        private Vector2 CalculateGPSPosition(Vector2 origin, float bearingDegrees, float distanceMeters)
        {
            const float EARTH_RADIUS = 6371000f; // Earth radius in meters

            float lat1 = origin.x * Mathf.Deg2Rad;
            float lon1 = origin.y * Mathf.Deg2Rad;
            float bearing = bearingDegrees * Mathf.Deg2Rad;
            float angularDistance = distanceMeters / EARTH_RADIUS;

            float lat2 = Mathf.Asin(
                Mathf.Sin(lat1) * Mathf.Cos(angularDistance) +
                Mathf.Cos(lat1) * Mathf.Sin(angularDistance) * Mathf.Cos(bearing)
            );

            float lon2 = lon1 + Mathf.Atan2(
                Mathf.Sin(bearing) * Mathf.Sin(angularDistance) * Mathf.Cos(lat1),
                Mathf.Cos(angularDistance) - Mathf.Sin(lat1) * Mathf.Sin(lat2)
            );

            return new Vector2(lat2 * Mathf.Rad2Deg, lon2 * Mathf.Rad2Deg);
        }

        // Calculate distance between two GPS coordinates using Haversine formula
        private float CalculateGPSDistance(Vector2 pos1, Vector2 pos2)
        {
            const float EARTH_RADIUS = 6371000f;

            float lat1Rad = pos1.x * Mathf.Deg2Rad;
            float lat2Rad = pos2.x * Mathf.Deg2Rad;
            float latDiff = (pos2.x - pos1.x) * Mathf.Deg2Rad;
            float lonDiff = (pos2.y - pos1.y) * Mathf.Deg2Rad;

            float a = Mathf.Sin(latDiff / 2) * Mathf.Sin(latDiff / 2) +
                     Mathf.Cos(lat1Rad) * Mathf.Cos(lat2Rad) *
                     Mathf.Sin(lonDiff / 2) * Mathf.Sin(lonDiff / 2);

            float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));

            return EARTH_RADIUS * c;
        }

        // Static method to get safe spawn parameters
        public static (float distance, float angle) GetSafeSpawnParameters()
        {
            float distance = Random.Range(SPAWN_MIN_DISTANCE, SPAWN_MAX_DISTANCE);
            float angle = Random.Range(0f, 360f);
            return (distance, angle);
        }

        // Get collection radius for UI/debugging
        public static float GetCollectionRadius()
        {
            return COLLECTION_RADIUS;
        }
    }
}