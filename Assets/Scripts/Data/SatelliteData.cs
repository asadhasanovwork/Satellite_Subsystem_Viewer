using System;
using System.Collections.Generic;

namespace SatelliteViewer.Data
{
    [Serializable]
    public class SatelliteData
    {
        public string satelliteId;
        public string satelliteName;
        public string modelRootName;
        public float defaultCameraDistance = 6f;
        public List<SubsystemData> subsystems;
    }

    [Serializable]
    public class SubsystemData
    {
        public string id;
        public string name;
        public bool canIsolate = true;
        public string defaultOperationalState = "Standby";
        public List<PartData> parts;
    }

    [Serializable]
    public class PartData
    {
        public string partId;
        public string unityName;
        public string displayName;
        public string type;
        public string description;
        public string image;

        public bool canToggle = true;
        public string defaultState = "Standby";
        public string statusText;
    }

    [Serializable]
    public class SatelliteCatalog
    {
        public List<SatelliteCatalogItem> satellites;
    }

    [Serializable]
    public class SatelliteCatalogItem
    {
        public string id;
        public string displayName;
        public string prefabPath;      // Resources path
        public string metadataFile;    // StreamingAssets JSON
        public string thumbnail;
        public float defaultCameraDistance = 6f;
    }

    public enum OperationalState
    {
        Off,
        Standby,
        Active,
        Fault
    }

    public enum ViewerMode
    {
        Normal,
        SubsystemIsolation,
        PartFocus,
        Exploded
    }
}