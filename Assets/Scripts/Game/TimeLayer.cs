
using UnityEngine;
using System.Collections.Generic;
using FMODUnity;

namespace LoGa.LudoEngine.Game
{
    [System.Serializable]
    public class TimeLayer
    {
        [Header("Layer Identity")]
        public string layerName;          // e.g., "Neolithic", "Battle of Boyne 1690", "Modern"
        public int layerIndex;           // 0, 1, 2... for ordering

        [Header("Audio")]
        public EventReference ambientSound;  // Era-specific ambient audio

        [Header("POIs")]
        public List<POI> pois;               // All POIs for this layer (regular + portals)
    }
}