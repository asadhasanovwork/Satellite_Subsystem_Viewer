using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SatelliteViewer.Data;

public class SatelliteMetadataLoader : MonoBehaviour
{
    [Header("JSON Source")]
    [SerializeField] private string jsonFileName = "6u_v2.json";
    [SerializeField] private bool autoLoadOnAwake = true;

    public string JsonFileName => jsonFileName;
    public SatelliteData Data { get; private set; }

    public Dictionary<string, PartData> ByUnityName { get; private set; } = new();
    public Dictionary<string, PartData> ByPartId { get; private set; } = new();
    public Dictionary<string, string> SubsystemNameById { get; private set; } = new();
    public Dictionary<string, string> PartToSubsystemId { get; private set; } = new();

    public event Action<SatelliteData> OnMetadataLoaded;

    private void Awake()
    {
        ClearCaches();

        if (autoLoadOnAwake && !string.IsNullOrWhiteSpace(jsonFileName))
        {
            Load();
        }
        else if (string.IsNullOrWhiteSpace(jsonFileName))
        {
            Debug.LogWarning("[SatelliteMetadataLoader] jsonFileName is empty. Waiting for runtime assignment.");
        }
    }

    public void SetJsonFile(string fileName, bool reload = true)
    {
        jsonFileName = fileName;

        if (reload)
            Load();
    }

    public void Load()
    {
        ClearCaches();

        if (string.IsNullOrWhiteSpace(jsonFileName))
        {
            Debug.LogWarning("[SatelliteMetadataLoader] jsonFileName is empty. Waiting for runtime assignment.");
            Data = null;
            return;
        }

        string fullPath = Path.Combine(Application.streamingAssetsPath, jsonFileName);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[SatelliteMetadataLoader] JSON not found: {fullPath}");
            Data = null;
            return;
        }

        string json;

        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SatelliteMetadataLoader] Failed to read JSON file: {e.Message}");
            Data = null;
            return;
        }

        try
        {
            Data = JsonUtility.FromJson<SatelliteData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SatelliteMetadataLoader] JSON parse error: {e.Message}");
            Data = null;
            return;
        }

        if (Data == null)
        {
            Debug.LogError("[SatelliteMetadataLoader] Parsed Data is null. Check JSON format.");
            return;
        }

        if (Data.subsystems == null)
        {
            Debug.LogError("[SatelliteMetadataLoader] Data.subsystems is null. Check JSON format.");
            Data = null;
            return;
        }

        foreach (var ss in Data.subsystems)
        {
            if (ss == null)
                continue;

            if (!string.IsNullOrWhiteSpace(ss.id) && !SubsystemNameById.ContainsKey(ss.id))
            {
                SubsystemNameById.Add(ss.id, string.IsNullOrWhiteSpace(ss.name) ? ss.id : ss.name);
            }

            if (ss.parts == null)
                continue;

            foreach (var p in ss.parts)
            {
                if (p == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(p.unityName) && !ByUnityName.ContainsKey(p.unityName))
                {
                    ByUnityName.Add(p.unityName, p);
                }

                if (!string.IsNullOrWhiteSpace(p.partId) && !ByPartId.ContainsKey(p.partId))
                {
                    ByPartId.Add(p.partId, p);
                }

                if (!string.IsNullOrWhiteSpace(p.partId) &&
                    !string.IsNullOrWhiteSpace(ss.id) &&
                    !PartToSubsystemId.ContainsKey(p.partId))
                {
                    PartToSubsystemId.Add(p.partId, ss.id);
                }
            }
        }

        Debug.Log($"[SatelliteMetadataLoader] Loaded {ByUnityName.Count} parts from {jsonFileName}");
        OnMetadataLoaded?.Invoke(Data);
    }

    private void ClearCaches()
    {
        ByUnityName.Clear();
        ByPartId.Clear();
        SubsystemNameById.Clear();
        PartToSubsystemId.Clear();
        Data = null;
    }

    public bool TryGetPartByUnityName(string unityName, out PartData part)
    {
        if (string.IsNullOrWhiteSpace(unityName))
        {
            part = null;
            return false;
        }

        return ByUnityName.TryGetValue(unityName, out part);
    }

    public bool TryGetPartByPartId(string partId, out PartData part)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            part = null;
            return false;
        }

        return ByPartId.TryGetValue(partId, out part);
    }

    public string GetSubsystemName(string subsystemId)
    {
        if (string.IsNullOrWhiteSpace(subsystemId))
            return "-";

        return SubsystemNameById.TryGetValue(subsystemId, out var name) ? name : subsystemId;
    }

    public string GetSubsystemIdForPart(string partId)
    {
        if (string.IsNullOrWhiteSpace(partId))
            return null;

        return PartToSubsystemId.TryGetValue(partId, out var subsystemId) ? subsystemId : null;
    }
}