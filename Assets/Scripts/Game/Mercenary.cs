using UnityEngine;
using LoGa.LudoEngine.Core;
using LoGa.LudoEngine.Services;

namespace LoGa.LudoEngine.Game
{
    public class Mercenary
    {
        public string id;
        public float bearing;

        private float startDistance = 50f;
        private float attackDistance = 5f;
        private float currentDistance;
        private bool isApproaching = false;

        // Services (only needed for head tracking in simulated combat)
        private IHeadTrackingService HeadTrackingService => ServiceLocator.GetService<IHeadTrackingService>();

        public Mercenary(string mercenaryId, float angleFromPlayer)
        {
            id = mercenaryId;
            bearing = angleFromPlayer;
            currentDistance = startDistance;
            Debug.Log($"[TEST] Mercenary {id} created at bearing {bearing}° (simulated combat)");
        }

        public Vector3 GetCurrentAudioPosition()
        {
            // In simulated combat, position is relative to fixed player position
            // Only head tracking affects the relative position calculation
            float currentPlayerHeading = HeadTrackingService.CurrentHeading;
            float relativeAngle = bearing - currentPlayerHeading;
            float angleRad = relativeAngle * Mathf.Deg2Rad;

            return new Vector3(
                currentDistance * Mathf.Sin(angleRad),
                0,
                currentDistance * Mathf.Cos(angleRad)
            );
        }

        public void StartApproach()
        {
            isApproaching = true;
            currentDistance = startDistance;
            Debug.Log($"[TEST] Mercenary {id} starting approach from {startDistance}m");
        }

        public void UpdateApproach(float progress)
        {
            if (isApproaching)
            {
                currentDistance = Mathf.Lerp(startDistance, attackDistance, progress);
                Debug.Log($"[TEST] Mercenary {id} approach progress: {progress:F2} (distance: {currentDistance:F1}m)");
            }
        }
    }
}