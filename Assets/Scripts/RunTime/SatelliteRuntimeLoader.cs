using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SatelliteViewer.Data;

// Load the satellite before other scripts' Start (ViewerUI.Start initialises SatelliteMainMenu,
// which looks up the SatelliteOrbitalMotion this loader adds; CameraOrbitMMB.Start must not
// override the distance set here).
[DefaultExecutionOrder(-100)]
public class SatelliteRuntimeLoader : MonoBehaviour
{
    [Header("Catalog")]
    public string catalogFileName = "satellite_catalog.json";

    [Header("Spawn")]
    public Transform spawnParent;

    [Header("Dependencies")]
    public SatelliteMetadataLoader metadataLoader;
    public VisibilityManager visibilityManager;
    public ExplodeController explodeController;
    public ViewModeController viewModeController;
    public SelectionManager selectionManager;
    public ViewerUI viewerUI;
    public CameraOrbitMMB orbitCamera;
    public SatelliteOperationsManager operationsManager;
    public OperationalStateVisualizer operationalStateVisualizer;
    public SatellitePhysicsManager physicsManager;
    public SatellitePhysicalVisualsManager physicalVisualsManager;

    [Header("Orbital Motion Runtime Setup")]
    public bool addOrbitalMotionAtRuntime = true;
    public Transform orbitCenter;
    public bool useOrbitCenterTransform = false;
    public Vector3 orbitCenterPosition = Vector3.zero;

    [Header("Default Orbit Preset")]
    public float semiMajorAxis = 12f;
    public float semiMinorAxis = 9f;
    public float inclinationDeg = 35f;
    public float orbitPeriodSeconds = 40f;
    public float orbitPhaseDeg = 0f;

    [Header("Body Rotation")]
    public bool rotateBody = true;
    public Vector3 bodySpinAxis = Vector3.up;
    public float bodySpinDegPerSec = 30f;

    [Header("Attitude")]
    public bool faceVelocityDirection = true;
    public bool addNadirBias = false;
    public float attitudeSmooth = 4f;

    public SatelliteCatalog Catalog { get; private set; }
    public SatelliteCatalogItem CurrentSatellite { get; private set; }
    public Transform CurrentSatelliteRoot { get; private set; }

    private GameObject _spawnedInstance;
    private readonly Dictionary<string, SatelliteCatalogItem> _catalogById = new();

    public event Action<SatelliteCatalog> OnCatalogLoaded;
    public event Action<SatelliteCatalogItem> OnSatelliteLoaded;

    private void Awake()
    {
        if (metadataLoader == null) metadataLoader = FindFirstObjectByType<SatelliteMetadataLoader>(FindObjectsInactive.Include);
        if (visibilityManager == null) visibilityManager = FindFirstObjectByType<VisibilityManager>(FindObjectsInactive.Include);
        if (explodeController == null) explodeController = FindFirstObjectByType<ExplodeController>(FindObjectsInactive.Include);
        if (viewModeController == null) viewModeController = FindFirstObjectByType<ViewModeController>(FindObjectsInactive.Include);
        if (selectionManager == null) selectionManager = FindFirstObjectByType<SelectionManager>(FindObjectsInactive.Include);
        if (viewerUI == null) viewerUI = FindFirstObjectByType<ViewerUI>(FindObjectsInactive.Include);
        if (orbitCamera == null) orbitCamera = FindFirstObjectByType<CameraOrbitMMB>(FindObjectsInactive.Include);
        if (operationsManager == null) operationsManager = FindFirstObjectByType<SatelliteOperationsManager>(FindObjectsInactive.Include);
        if (operationalStateVisualizer == null) operationalStateVisualizer = FindFirstObjectByType<OperationalStateVisualizer>(FindObjectsInactive.Include);
        if (physicsManager == null) physicsManager = FindFirstObjectByType<SatellitePhysicsManager>(FindObjectsInactive.Include);
        if (physicalVisualsManager == null) physicalVisualsManager = FindFirstObjectByType<SatellitePhysicalVisualsManager>(FindObjectsInactive.Include);
    }

    private void Start()
    {
        LoadCatalog();

        if (Catalog?.satellites != null && Catalog.satellites.Count > 0)
            LoadSatellite(Catalog.satellites[0].id);
        else
            Debug.LogWarning("[SatelliteRuntimeLoader] No satellites found in catalog.");
    }

    public void LoadCatalog()
    {
        _catalogById.Clear();

        string fullPath = Path.Combine(Application.streamingAssetsPath, catalogFileName);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[SatelliteRuntimeLoader] Catalog not found: {fullPath}");
            Catalog = null;
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SatelliteRuntimeLoader] Failed to read catalog: {e.Message}");
            Catalog = null;
            return;
        }

        try
        {
            Catalog = JsonUtility.FromJson<SatelliteCatalog>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SatelliteRuntimeLoader] Catalog parse error: {e.Message}");
            Catalog = null;
            return;
        }

        if (Catalog?.satellites == null)
        {
            Debug.LogError("[SatelliteRuntimeLoader] Catalog satellites list is null.");
            Catalog = null;
            return;
        }

        foreach (var item in Catalog.satellites)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id))
                continue;

            if (!_catalogById.ContainsKey(item.id))
                _catalogById.Add(item.id, item);
        }

        Debug.Log($"[SatelliteRuntimeLoader] Catalog loaded. Satellite count: {_catalogById.Count}");
        OnCatalogLoaded?.Invoke(Catalog);
    }

    public void LoadSatellite(string satelliteId)
    {
        if (string.IsNullOrWhiteSpace(satelliteId))
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] satelliteId is empty.");
            return;
        }

        if (_catalogById.Count == 0)
            LoadCatalog();

        if (!_catalogById.TryGetValue(satelliteId, out var item))
        {
            Debug.LogError($"[SatelliteRuntimeLoader] Satellite id not found in catalog: {satelliteId}");
            return;
        }

        Debug.Log($"[SatelliteRuntimeLoader] Trying prefab path: {item.prefabPath}");

        GameObject prefab = Resources.Load<GameObject>(item.prefabPath);
        Debug.Log($"[SatelliteRuntimeLoader] Prefab found? {prefab != null}");

        if (!prefab)
        {
            Debug.LogError($"[SatelliteRuntimeLoader] Prefab not found in Resources: {item.prefabPath}");
            return;
        }

        if (_spawnedInstance != null)
        {
            Destroy(_spawnedInstance);
            _spawnedInstance = null;
        }

        _spawnedInstance = Instantiate(prefab, spawnParent);
        _spawnedInstance.name = prefab.name;

        CurrentSatellite = item;
        CurrentSatelliteRoot = _spawnedInstance.transform;

        Debug.Log($"[SatelliteRuntimeLoader] Spawned satellite root: {CurrentSatelliteRoot.name}");

        if (metadataLoader != null)
        {
            if (string.IsNullOrWhiteSpace(item.metadataFile))
            {
                Debug.LogError("[SatelliteRuntimeLoader] metadataFile is empty.");
            }
            else
            {
                Debug.Log($"[SatelliteRuntimeLoader] Loading metadata file: {item.metadataFile}");
                metadataLoader.SetJsonFile(item.metadataFile, true);
            }
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] metadataLoader is null.");
        }

        // Must run before Visibility/Explode/Selection cache the root: they all key off PartTag,
        // which nothing else ever adds (isolate/explode by subsystem silently did nothing).
        TagPartsFromMetadata(CurrentSatelliteRoot);

        if (visibilityManager != null)
        {
            visibilityManager.SetSatelliteRoot(CurrentSatelliteRoot, true);
            visibilityManager.ShowAll();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] visibilityManager is null.");
        }

        if (explodeController != null)
        {
            explodeController.SetSatelliteRoot(CurrentSatelliteRoot, true);
            explodeController.Assemble();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] explodeController is null.");
        }

        if (selectionManager != null)
        {
            selectionManager.metadata = metadataLoader;
            selectionManager.ui = viewerUI;
            selectionManager.visibility = visibilityManager;
            selectionManager.viewMode = viewModeController;
            selectionManager.RegisterNewSatelliteRoot(CurrentSatelliteRoot);
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] selectionManager is null.");
        }

        if (operationalStateVisualizer != null)
        {
            operationalStateVisualizer.CacheOriginals(CurrentSatelliteRoot);
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] operationalStateVisualizer is null.");
        }

        if (physicsManager != null)
        {
            physicsManager.operations = operationsManager;
            physicsManager.ResetTelemetry();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] physicsManager is null.");
        }

        if (operationsManager != null)
        {
            operationsManager.ClearRegisteredParts(false);
            RegisterPartsFromMetadata();
            operationsManager.RebuildRuntimeState();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] operationsManager is null.");
        }

        if (physicalVisualsManager != null)
        {
            physicalVisualsManager.runtimeLoader = this;
            physicalVisualsManager.physics = physicsManager;
            physicalVisualsManager.operations = operationsManager;
            physicalVisualsManager.metadata = metadataLoader;
            physicalVisualsManager.visibility = visibilityManager;
            physicalVisualsManager.CacheCurrentSatellite();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] physicalVisualsManager is null.");
        }

        if (addOrbitalMotionAtRuntime)
        {
            SetupOrbitalMotion(CurrentSatelliteRoot);
        }

        if (viewModeController != null)
        {
            viewModeController.EnterNormalMode();
        }

        if (orbitCamera != null)
        {
            float camDistance = ResolveCameraDistance(item);
            orbitCamera.SetTarget(CurrentSatelliteRoot, camDistance);
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] orbitCamera is null.");
        }

        if (viewerUI != null)
        {
            viewerUI.SetSatelliteTitle(string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName);
            viewerUI.RefreshAll();
        }
        else
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] viewerUI is null.");
        }

        OnSatelliteLoaded?.Invoke(item);
    }

    private void TagPartsFromMetadata(Transform root)
    {
        if (root == null || metadataLoader == null || metadataLoader.Data == null || metadataLoader.Data.subsystems == null)
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] Cannot tag parts: satellite root or metadata missing.");
            return;
        }

        var byName = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && !byName.ContainsKey(t.name))
                byName.Add(t.name, t);
        }

        var tagged = new List<PartTag>();
        int missing = 0;

        foreach (var subsystem in metadataLoader.Data.subsystems)
        {
            if (subsystem == null || subsystem.parts == null)
                continue;

            foreach (var part in subsystem.parts)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.unityName))
                    continue;

                if (!byName.TryGetValue(part.unityName, out Transform t))
                {
                    missing++;
                    Debug.LogWarning($"[SatelliteRuntimeLoader] Part '{part.unityName}' ({subsystem.id}) not found under {root.name}.");
                    continue;
                }

                PartTag tag = t.GetComponent<PartTag>();
                if (tag == null)
                    tag = t.gameObject.AddComponent<PartTag>();

                tag.subsystemId = subsystem.id;
                tag.partId = part.partId;
                tag.unityName = part.unityName;
                tagged.Add(tag);
            }
        }

        // Cache after all tags exist so nested parts are excluded from their parent's renderers.
        foreach (var tag in tagged)
            tag.Cache();

        Debug.Log($"[SatelliteRuntimeLoader] Tagged {tagged.Count} parts ({missing} missing in model).");
    }

    private void SetupOrbitalMotion(Transform targetRoot)
    {
        if (targetRoot == null)
            return;

        // Reuse a SatelliteOrbitalMotion that already drives this root or one of its parents
        // (Test_Demo has one on ViewerManagers driving the SatelliteRoot spawn parent). Adding a
        // second one on the spawned instance made two components fight over position/rotation,
        // and SatelliteMainMenu could only pause one of them.
        SatelliteOrbitalMotion orbitalMotion = targetRoot.GetComponent<SatelliteOrbitalMotion>();
        bool reused = false;

        if (orbitalMotion == null)
        {
            var existing = FindObjectsByType<SatelliteOrbitalMotion>(FindObjectsSortMode.None);
            foreach (var candidate in existing)
            {
                if (candidate == null || candidate.satelliteRoot == null)
                    continue;

                if (targetRoot.IsChildOf(candidate.satelliteRoot))
                {
                    orbitalMotion = candidate;
                    reused = true;
                    break;
                }
            }
        }

        bool created = false;
        if (orbitalMotion == null)
        {
            orbitalMotion = targetRoot.gameObject.AddComponent<SatelliteOrbitalMotion>();
            created = true;
        }

        if (!reused)
            orbitalMotion.satelliteRoot = targetRoot;

        orbitalMotion.operationsManager = operationsManager;
        if (orbitCenter != null || !reused)
            orbitalMotion.orbitCenter = orbitCenter;
        orbitalMotion.useOrbitCenterTransform = useOrbitCenterTransform && orbitCenter != null;
        orbitalMotion.orbitCenterPosition = orbitCenterPosition;

        orbitalMotion.semiMajorAxis = semiMajorAxis;
        orbitalMotion.semiMinorAxis = semiMinorAxis;
        orbitalMotion.inclinationDeg = inclinationDeg;
        orbitalMotion.orbitPeriodSeconds = orbitPeriodSeconds;
        orbitalMotion.orbitPhaseDeg = orbitPhaseDeg;

        orbitalMotion.rotateBody = rotateBody;
        orbitalMotion.bodySpinAxis = bodySpinAxis;
        orbitalMotion.bodySpinDegPerSec = bodySpinDegPerSec;

        orbitalMotion.faceVelocityDirection = faceVelocityDirection;
        orbitalMotion.addNadirBias = addNadirBias;
        orbitalMotion.attitudeSmooth = attitudeSmooth;

        // Don't override a pause applied by SatelliteMainMenu on an existing component.
        if (created)
            orbitalMotion.simulate = true;

        Debug.Log($"[SatelliteRuntimeLoader] SatelliteOrbitalMotion {(created ? "added" : "reused")} and configured (drives '{orbitalMotion.satelliteRoot.name}').");
    }

    private float ResolveCameraDistance(SatelliteCatalogItem item)
    {
        if (metadataLoader != null && metadataLoader.Data != null && metadataLoader.Data.defaultCameraDistance > 0f)
            return metadataLoader.Data.defaultCameraDistance;

        if (item != null && item.defaultCameraDistance > 0f)
            return item.defaultCameraDistance;

        return 6f;
    }

    private void RegisterPartsFromMetadata()
    {
        if (operationsManager == null)
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] Cannot register parts: operationsManager is null.");
            return;
        }

        if (metadataLoader == null || metadataLoader.Data == null || metadataLoader.Data.subsystems == null)
        {
            Debug.LogWarning("[SatelliteRuntimeLoader] Cannot register parts: metadata is not loaded.");
            return;
        }

        int registeredCount = 0;

        foreach (var subsystem in metadataLoader.Data.subsystems)
        {
            if (subsystem == null)
                continue;

            string subsystemId = subsystem.id;
            OperationalState subsystemDefaultState = ParseOperationalState(subsystem.defaultOperationalState, OperationalState.Standby);

            // EnsureSubsystem keeps an existing state, so the JSON default was ignored for the
            // built-in subsystems created in Awake (and for states left from a previous satellite).
            operationsManager.InitializeSubsystemState(subsystemId, subsystemDefaultState);

            if (subsystem.parts == null)
                continue;

            foreach (var part in subsystem.parts)
            {
                if (part == null)
                    continue;

                string partName = !string.IsNullOrWhiteSpace(part.unityName)
                    ? part.unityName
                    : part.displayName;

                if (string.IsNullOrWhiteSpace(partName))
                    continue;

                OperationalState initialState = ParseOperationalState(part.defaultState, subsystemDefaultState);

                operationsManager.RegisterOrUpdatePart(partName, subsystemId, initialState);
                registeredCount++;
            }
        }

        Debug.Log($"[SatelliteRuntimeLoader] Registered {registeredCount} parts from metadata into SatelliteOperationsManager.");
    }

    private OperationalState ParseOperationalState(string value, OperationalState fallback = OperationalState.Standby)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "off" => OperationalState.Off,
            "inactive" => OperationalState.Off,
            "deactivate" => OperationalState.Off,
            "deactivated" => OperationalState.Off,

            "standby" => OperationalState.Standby,
            "idle" => OperationalState.Standby,

            "active" => OperationalState.Active,
            "activate" => OperationalState.Active,
            "activated" => OperationalState.Active,
            "on" => OperationalState.Active,

            "fault" => OperationalState.Fault,
            "failed" => OperationalState.Fault,
            "error" => OperationalState.Fault,

            _ => fallback
        };
    }
}