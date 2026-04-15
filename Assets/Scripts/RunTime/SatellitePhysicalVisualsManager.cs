using System.Collections.Generic;
using UnityEngine;
using SatelliteViewer.Data;

public class SatellitePhysicalVisualsManager : MonoBehaviour
{
    [Header("References")]
    public SatellitePhysicsManager physics;
    public SatelliteOperationsManager operations;
    public SatelliteMetadataLoader metadata;
    public VisibilityManager visibility;
    public SatelliteRuntimeLoader runtimeLoader;

    [Header("Behavior")]
    public bool autoFindDependencies = true;
    public bool affectRootRotation = true;
    public bool affectMaterials = true;
    public bool affectPartScale = true;

    [Header("Root Motion")]
    public float maxTumbleAngleDeg = 8f;
    public float maxSpinVisualDegPerSec = 45f;
    public float rootReturnSpeed = 3f;

    [Header("Thermal Visuals")]
    public float coolTemp = 15f;
    public float hotTemp = 80f;
    public Color hotTint = new Color(1.0f, 0.45f, 0.15f, 1f);
    public float maxThermalTint = 0.55f;

    [Header("Battery / Power Visuals")]
    public float lowBatteryThreshold = 25f;
    public Color lowBatteryTint = new Color(0.45f, 0.45f, 0.45f, 1f);
    public float lowBatteryTintStrength = 0.45f;

    [Header("Fuel / Prop Visuals")]
    public Color lowFuelTint = new Color(1f, 0.55f, 0.2f, 1f);
    public float lowFuelTintStrength = 0.45f;
    public float propPulseAmplitude = 0.06f;
    public float propPulseSpeed = 7f;

    [Header("Comms / Payload Visuals")]
    public Color commsAlertTint = new Color(1f, 0.25f, 0.25f, 1f);
    public float commsBlinkSpeed = 5f;
    public Color payloadActiveTint = new Color(0.25f, 0.9f, 1f, 1f);
    public float payloadPulseAmplitude = 0.04f;
    public float payloadPulseSpeed = 4f;

    [Header("Optional Name Hints")]
    public string[] solarPanelNameHints = { "panel", "solar", "eps" };
    public string[] propNameHints = { "fuel", "prop", "thruster", "brick" };
    public string[] commsNameHints = { "transceiver", "antenna", "gps", "comms" };
    public string[] payloadNameHints = { "payload", "capture", "camera" };

    private Transform _root;
    private Quaternion _baseRootLocalRotation;
    private readonly Dictionary<Renderer, Material[]> _baseMaterials = new();
    private readonly Dictionary<Transform, Vector3> _baseScales = new();

    private readonly List<Renderer> _allRenderers = new();
    private readonly List<Transform> _propParts = new();
    private readonly List<Transform> _payloadParts = new();
    private readonly List<Renderer> _commsRenderers = new();
    private readonly List<Renderer> _batterySensitiveRenderers = new();
    private readonly List<Renderer> _thermalRenderers = new();
    private readonly List<Renderer> _fuelRenderers = new();

    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        if (autoFindDependencies)
            AutoFind();

        _mpb = new MaterialPropertyBlock();
        CacheCurrentSatellite();
    }

    private void OnEnable()
    {
        if (runtimeLoader != null)
            runtimeLoader.OnSatelliteLoaded += OnSatelliteLoaded;
    }

    private void OnDisable()
    {
        if (runtimeLoader != null)
            runtimeLoader.OnSatelliteLoaded -= OnSatelliteLoaded;
    }

    private void Update()
    {
        if (physics == null)
            return;

        if (_root == null)
        {
            CacheCurrentSatellite();
            if (_root == null)
                return;
        }

        ApplyRootMotion();
        ApplyMaterialEffects();
        ApplyScaleEffects();
    }

    private void AutoFind()
    {
        if (physics == null)
            physics = FindFirstObjectByType<SatellitePhysicsManager>(FindObjectsInactive.Include);

        if (operations == null)
            operations = FindFirstObjectByType<SatelliteOperationsManager>(FindObjectsInactive.Include);

        if (metadata == null)
            metadata = FindFirstObjectByType<SatelliteMetadataLoader>(FindObjectsInactive.Include);

        if (visibility == null)
            visibility = FindFirstObjectByType<VisibilityManager>(FindObjectsInactive.Include);

        if (runtimeLoader == null)
            runtimeLoader = FindFirstObjectByType<SatelliteRuntimeLoader>(FindObjectsInactive.Include);
    }

    private void OnSatelliteLoaded(SatelliteCatalogItem _)
    {
        CacheCurrentSatellite();
    }

    [ContextMenu("Cache Current Satellite")]
    public void CacheCurrentSatellite()
    {
        _allRenderers.Clear();
        _propParts.Clear();
        _payloadParts.Clear();
        _commsRenderers.Clear();
        _batterySensitiveRenderers.Clear();
        _thermalRenderers.Clear();
        _fuelRenderers.Clear();
        _baseMaterials.Clear();
        _baseScales.Clear();

        _root = runtimeLoader != null ? runtimeLoader.CurrentSatelliteRoot : null;
        if (_root == null && visibility != null)
            _root = visibility.satelliteRoot;

        if (_root == null)
            return;

        _baseRootLocalRotation = _root.localRotation;

        var renderers = _root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null)
                continue;

            _allRenderers.Add(r);

            if (!_baseMaterials.ContainsKey(r))
                _baseMaterials.Add(r, r.sharedMaterials);

            _thermalRenderers.Add(r);

            string n = r.name.ToLowerInvariant();

            if (ContainsAnyHint(n, solarPanelNameHints))
                _batterySensitiveRenderers.Add(r);

            if (ContainsAnyHint(n, commsNameHints))
                _commsRenderers.Add(r);

            if (ContainsAnyHint(n, propNameHints))
                _fuelRenderers.Add(r);
        }

        var transforms = _root.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t == null || t == _root)
                continue;

            if (!_baseScales.ContainsKey(t))
                _baseScales.Add(t, t.localScale);

            string n = t.name.ToLowerInvariant();

            if (ContainsAnyHint(n, propNameHints))
                _propParts.Add(t);

            if (ContainsAnyHint(n, payloadNameHints))
                _payloadParts.Add(t);
        }
    }

    private bool ContainsAnyHint(string source, string[] hints)
    {
        if (string.IsNullOrWhiteSpace(source) || hints == null)
            return false;

        for (int i = 0; i < hints.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(hints[i]) && source.Contains(hints[i].ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private void ApplyRootMotion()
    {
        if (!affectRootRotation || _root == null)
            return;

        float tumble01 = Mathf.Clamp01(physics.attitudeErrorDeg / 45f);
        float spin01 = Mathf.Clamp01(physics.angularRateDegPerSec / 10f);

        float time = Time.time;

        Quaternion wobble = Quaternion.identity;

        if (tumble01 > 0.001f)
        {
            float wobbleX = Mathf.Sin(time * 1.7f) * maxTumbleAngleDeg * tumble01;
            float wobbleZ = Mathf.Cos(time * 1.25f) * maxTumbleAngleDeg * tumble01;
            wobble *= Quaternion.Euler(wobbleX, 0f, wobbleZ);
        }

        if (spin01 > 0.001f)
        {
            float spinY = Mathf.Sin(time * 0.75f) * maxSpinVisualDegPerSec * spin01;
            wobble *= Quaternion.Euler(0f, spinY, 0f);
        }

        Quaternion target = _baseRootLocalRotation * wobble;
        _root.localRotation = Quaternion.Slerp(_root.localRotation, target, Time.deltaTime * rootReturnSpeed);
    }

    private void ApplyMaterialEffects()
    {
        if (!affectMaterials)
            return;

        float temp01 = Mathf.InverseLerp(coolTemp, hotTemp, physics.temperatureC);
        float lowBattery01 = 1f - Mathf.Clamp01(physics.batteryPercent / 100f);
        float lowFuel01 = 1f - Mathf.Clamp01(physics.fuelPercent / 100f);

        bool commsBad = !physics.commandLinkAvailable || physics.signalQuality < 0.2f;
        float commsBlink = (Mathf.Sin(Time.time * commsBlinkSpeed) * 0.5f + 0.5f);

        foreach (var r in _allRenderers)
        {
            if (r == null)
                continue;

            r.GetPropertyBlock(_mpb);
            _mpb.Clear();

            Color finalColor = Color.white;
            float tintStrength = 0f;

            if (_thermalRenderers.Contains(r))
            {
                finalColor = Color.Lerp(finalColor, hotTint, temp01);
                tintStrength = Mathf.Max(tintStrength, temp01 * maxThermalTint);
            }

            if (_batterySensitiveRenderers.Contains(r) && physics.batteryPercent < lowBatteryThreshold)
            {
                float t = Mathf.InverseLerp(lowBatteryThreshold, 0f, physics.batteryPercent);
                finalColor = Color.Lerp(finalColor, lowBatteryTint, t);
                tintStrength = Mathf.Max(tintStrength, t * lowBatteryTintStrength);
            }

            if (_fuelRenderers.Contains(r) && physics.fuelPercent < 30f)
            {
                float t = Mathf.InverseLerp(30f, 0f, physics.fuelPercent);
                finalColor = Color.Lerp(finalColor, lowFuelTint, t);
                tintStrength = Mathf.Max(tintStrength, t * lowFuelTintStrength);
            }

            if (_commsRenderers.Contains(r) && commsBad)
            {
                finalColor = Color.Lerp(finalColor, commsAlertTint, commsBlink * 0.85f);
                tintStrength = Mathf.Max(tintStrength, 0.55f * commsBlink);
            }

            if (physics.payloadDataRate > 0.25f && RendererBelongsToPayload(r))
            {
                float pulse = Mathf.Sin(Time.time * payloadPulseSpeed) * 0.5f + 0.5f;
                finalColor = Color.Lerp(finalColor, payloadActiveTint, pulse * 0.75f);
                tintStrength = Mathf.Max(tintStrength, pulse * 0.45f);
            }

            if (tintStrength > 0.001f)
            {
                if (HasColorProperty(r))
                    _mpb.SetColor("_Color", Color.Lerp(Color.white, finalColor, tintStrength));

                if (HasBaseColorProperty(r))
                    _mpb.SetColor("_BaseColor", Color.Lerp(Color.white, finalColor, tintStrength));
            }

            r.SetPropertyBlock(_mpb);
        }
    }

    private bool RendererBelongsToPayload(Renderer r)
    {
        if (r == null) return false;
        string n = r.name.ToLowerInvariant();
        return ContainsAnyHint(n, payloadNameHints);
    }

    private bool HasColorProperty(Renderer r)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && mats[i].HasProperty("_Color"))
                return true;
        }
        return false;
    }

    private bool HasBaseColorProperty(Renderer r)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && mats[i].HasProperty("_BaseColor"))
                return true;
        }
        return false;
    }

    private void ApplyScaleEffects()
    {
        if (!affectPartScale)
            return;

        float propPulse = Mathf.Sin(Time.time * propPulseSpeed) * 0.5f + 0.5f;
        float payloadPulse = Mathf.Sin(Time.time * payloadPulseSpeed) * 0.5f + 0.5f;

        OperationalState propState = operations != null ? operations.GetSubsystemState("PROP") : OperationalState.Off;
        OperationalState payloadState = operations != null ? operations.GetSubsystemState("PAYLOAD") : OperationalState.Off;

        foreach (var t in _propParts)
        {
            if (t == null || !_baseScales.TryGetValue(t, out var baseScale))
                continue;

            float amp = 0f;

            if (propState == OperationalState.Active)
                amp = propPulseAmplitude * propPulse;
            else if (propState == OperationalState.Fault)
                amp = propPulseAmplitude * 0.5f * (1f - propPulse);

            t.localScale = baseScale * (1f + amp);
        }

        foreach (var t in _payloadParts)
        {
            if (t == null || !_baseScales.TryGetValue(t, out var baseScale))
                continue;

            float amp = 0f;

            if (payloadState == OperationalState.Active && physics.payloadDataRate > 0.1f)
                amp = payloadPulseAmplitude * payloadPulse;

            t.localScale = baseScale * (1f + amp);
        }
    }
}