using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FMODUnity;

namespace LoGa.LudoEngine.Services
{
    [CreateAssetMenu(fileName = "AudioEventLookup", menuName = "Game/Audio Event Lookup")]
    public class AudioEventLookup : ScriptableObject
    {
        [System.Serializable]
        public class AudioEventMapping
        {
            [Header("Friendly Name (used in JSON)")]
            public string eventName;

            [Header("FMOD Event Reference")]
            public EventReference eventReference;
        }

        [Header("Character Audio Events")]
        public List<AudioEventMapping> characterAudioEvents = new List<AudioEventMapping>();

        [Header("Portal Audio Events")]
        public List<AudioEventMapping> portalAudioEvents = new List<AudioEventMapping>();

        /// <summary>
        /// Combined list of all mappings for compatibility with other scripts
        /// </summary>
        public List<AudioEventMapping> AllMappings
        {
            get
            {
                var combined = new List<AudioEventMapping>();
                combined.AddRange(characterAudioEvents);
                combined.AddRange(portalAudioEvents);
                return combined;
            }
        }

        /// <summary>
        /// Total count of all mappings
        /// </summary>
        public int TotalMappingCount => characterAudioEvents.Count + portalAudioEvents.Count;

        public EventReference GetEventReference(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogWarning("AudioEventLookup: Cannot get event reference for empty event name");
                return new EventReference();
            }

            // Search character events first
            var mapping = characterAudioEvents.FirstOrDefault(m => m.eventName == eventName);

            // If not found, search portal events
            if (mapping == null)
            {
                mapping = portalAudioEvents.FirstOrDefault(m => m.eventName == eventName);
            }

            if (mapping != null)
            {
                Debug.Log($"AudioEventLookup: Found mapping '{eventName}' → {mapping.eventReference}");
                return mapping.eventReference;
            }

            // Not found in either list
            Debug.LogError($"AudioEventLookup: Missing mapping for '{eventName}' - add to lookup table in inspector");
            Debug.Log($"Available character events: {string.Join(", ", characterAudioEvents.Select(m => m.eventName))}");
            Debug.Log($"Available portal events: {string.Join(", ", portalAudioEvents.Select(m => m.eventName))}");

            return new EventReference();
        }

        /// <summary>
        /// Check if an event mapping exists
        /// </summary>
        public bool HasEventMapping(string eventName)
        {
            return characterAudioEvents.Any(m => m.eventName == eventName) ||
                   portalAudioEvents.Any(m => m.eventName == eventName);
        }

        /// <summary>
        /// Get all available event names
        /// </summary>
        public List<string> GetAllEventNames()
        {
            var names = new List<string>();
            names.AddRange(characterAudioEvents.Select(m => m.eventName));
            names.AddRange(portalAudioEvents.Select(m => m.eventName));
            return names;
        }

        [ContextMenu("Debug All Mappings")]
        public void DebugAllMappings()
        {
            Debug.Log($"=== AudioEventLookup Debug ({TotalMappingCount} total mappings) ===");

            Debug.Log($"Character Audio Events ({characterAudioEvents.Count}):");
            foreach (var mapping in characterAudioEvents)
            {
                Debug.Log($"  '{mapping.eventName}' → {mapping.eventReference}");
            }

            Debug.Log($"Portal Audio Events ({portalAudioEvents.Count}):");
            foreach (var mapping in portalAudioEvents)
            {
                Debug.Log($"  '{mapping.eventName}' → {mapping.eventReference}");
            }
        }
    }
}