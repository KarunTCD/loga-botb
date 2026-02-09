using UnityEngine;

namespace LoGa.LudoEngine.Core
{
    /// <summary>
    /// Represents a game site with its metadata
    /// </summary>
    [System.Serializable]
    public class Site
    {
        public string id;              // "battle_of_boyne"
        public string name;            // "Battle of the Boyne"
        public string description;     // "Historical battle site 1690"
        public string folderName;      // "BattleOfBoyne"
        public LocationData centerLocation;
        public float activationRadius;
        public bool isDebug;
    }

    [System.Serializable]
    public class LocationData
    {
        public float latitude;
        public float longitude;
    }

    [System.Serializable]
    public class SiteMetadataList
    {
        public System.Collections.Generic.List<Site> sites;
    }
}