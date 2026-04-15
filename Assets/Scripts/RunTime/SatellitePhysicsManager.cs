using System.Collections.Generic;
using UnityEngine;
using SatelliteViewer.Data;

public class SatellitePhysicsManager : MonoBehaviour
{
    [Header("References")]
    public SatelliteOperationsManager operations;

    [Tooltip("Optional ScriptableObject profiles. If empty, built-in fallback values will be used.")]
    public List<SubsystemBehaviorProfile> behaviorProfiles = new List<SubsystemBehaviorProfile>();

    private Dictionary<string, SubsystemBehaviorProfile> profileMap =
        new Dictionary<string, SubsystemBehaviorProfile>();

    [Header("Live Telemetry")]
    [Range(0f, 100f)] public float batteryPercent = 100f;
    public float temperatureC = 22f;
    [Range(0f, 180f)] public float attitudeErrorDeg = 0f;
    public float angularRateDegPerSec = 0.02f;
    [Range(0f, 1f)] public float signalQuality = 1f;
    [Range(0f, 100f)] public float fuelPercent = 100f;
    public float payloadDataRate = 0f;
    public bool powerAvailable = true;
    public bool commandLinkAvailable = true;

    [Header("Simulation Settings")]
    public bool simulate = true;
    public bool autoFindOperations = true;

    private void Awake()
    {
        if (operations == null && autoFindOperations)
            operations = FindFirstObjectByType<SatelliteOperationsManager>(FindObjectsInactive.Include);

        BuildProfileMap();
    }

    private void OnValidate()
    {
        BuildProfileMap();
    }

    private void Update()
    {
        if (!simulate || operations == null)
            return;

        Simulate(Time.deltaTime);
    }

    private void BuildProfileMap()
    {
        profileMap.Clear();

        if (behaviorProfiles == null)
            return;

        for (int i = 0; i < behaviorProfiles.Count; i++)
        {
            var profile = behaviorProfiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.subsystemName))
                continue;

            string key = profile.subsystemName.Trim().ToUpperInvariant();
            profileMap[key] = profile;
        }
    }

    private OperationalState GetState(string subsystem)
    {
        if (operations == null)
            return OperationalState.Off;

        return operations.GetSubsystemState(subsystem);
    }

    private SubsystemBehaviorProfile GetProfile(string subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem))
            return null;

        string key = subsystem.Trim().ToUpperInvariant();
        profileMap.TryGetValue(key, out var profile);
        return profile;
    }

    private float GetPowerDraw(string subsystem, OperationalState state, float fallbackActive, float fallbackStandby, float fallbackFault)
    {
        var profile = GetProfile(subsystem);
        if (profile == null)
        {
            return state switch
            {
                OperationalState.Active => fallbackActive,
                OperationalState.Standby => fallbackStandby,
                OperationalState.Fault => fallbackFault,
                _ => 0f
            };
        }

        return state switch
        {
            OperationalState.Active => profile.activePowerDraw,
            OperationalState.Standby => profile.standbyPowerDraw,
            OperationalState.Fault => profile.faultPowerDraw,
            _ => 0f
        };
    }

    private float GetHeatRate(string subsystem, OperationalState state, float fallbackActive, float fallbackStandby, float fallbackFault)
    {
        var profile = GetProfile(subsystem);
        if (profile == null)
        {
            return state switch
            {
                OperationalState.Active => fallbackActive,
                OperationalState.Standby => fallbackStandby,
                OperationalState.Fault => fallbackFault,
                _ => 0f
            };
        }

        return state switch
        {
            OperationalState.Active => profile.activeHeatRate,
            OperationalState.Standby => profile.standbyHeatRate,
            OperationalState.Fault => profile.faultHeatRate,
            _ => 0f
        };
    }

    private void Simulate(float dt)
    {
        OperationalState eps = GetState("EPS");
        OperationalState obc = GetState("OBC");
        OperationalState adcs = GetState("ADCS");
        OperationalState comms = GetState("COMMS");
        OperationalState prop = GetState("PROP");
        OperationalState payload = GetState("PAYLOAD");

        SimulatePower(dt, eps, obc, adcs, comms, prop, payload);
        SimulateAttitude(dt, adcs);
        SimulateComms(dt, obc, adcs, comms);
        SimulatePayload(dt, payload);
        SimulatePropulsion(dt, prop);
        ApplyDependencies(eps, obc, adcs, comms, payload);
        ClampTelemetry();
    }

    private void SimulatePower(
        float dt,
        OperationalState eps,
        OperationalState obc,
        OperationalState adcs,
        OperationalState comms,
        OperationalState prop,
        OperationalState payload)
    {
        powerAvailable = eps != OperationalState.Off && eps != OperationalState.Fault;

        float batteryDrainPerSec = 0f;

        // EPS itself
        batteryDrainPerSec += GetPowerDraw("EPS", eps, 0.02f, 0.01f, 0.05f);

        // Other subsystems only meaningfully consume if power exists
        if (powerAvailable)
        {
            batteryDrainPerSec += GetPowerDraw("OBC", obc, 0.015f, 0.008f, 0.02f);
            batteryDrainPerSec += GetPowerDraw("ADCS", adcs, 0.02f, 0.01f, 0.03f);
            batteryDrainPerSec += GetPowerDraw("COMMS", comms, 0.025f, 0.012f, 0.03f);
            batteryDrainPerSec += GetPowerDraw("PROP", prop, 0.04f, 0.0f, 0.02f);
            batteryDrainPerSec += GetPowerDraw("PAYLOAD", payload, 0.03f, 0.005f, 0.02f);
        }
        else
        {
            batteryDrainPerSec += 0.003f;
        }

        // Temperature from subsystem activity
        float heatPerSec = 0f;
        heatPerSec += GetHeatRate("EPS", eps, 0.01f, 0.005f, 0.03f);
        heatPerSec += GetHeatRate("OBC", obc, 0.01f, 0.004f, 0.015f);
        heatPerSec += GetHeatRate("ADCS", adcs, 0.015f, 0.008f, 0.03f);
        heatPerSec += GetHeatRate("COMMS", comms, 0.015f, 0.007f, 0.02f);
        heatPerSec += GetHeatRate("PAYLOAD", payload, 0.02f, 0.005f, 0.03f);

        batteryPercent -= batteryDrainPerSec * dt;

        if (prop == OperationalState.Active)
            heatPerSec += 0.15f;
        else if (prop == OperationalState.Fault)
            heatPerSec += 0.08f;

        temperatureC += heatPerSec * dt;

        // mild passive cooling toward nominal
        temperatureC = Mathf.Lerp(temperatureC, 22f, dt * 0.03f);
    }

    private void SimulateAttitude(float dt, OperationalState adcs)
    {
        switch (adcs)
        {
            case OperationalState.Active:
                attitudeErrorDeg = Mathf.Max(0f, attitudeErrorDeg - 0.6f * dt);
                angularRateDegPerSec = Mathf.Max(0f, angularRateDegPerSec - 0.01f * dt);
                break;

            case OperationalState.Standby:
                attitudeErrorDeg += 0.05f * dt;
                angularRateDegPerSec += 0.001f * dt;
                break;

            case OperationalState.Off:
                attitudeErrorDeg += 0.2f * dt;
                angularRateDegPerSec += 0.01f * dt;
                break;

            case OperationalState.Fault:
                attitudeErrorDeg += 0.5f * dt;
                angularRateDegPerSec += 0.03f * dt;
                break;
        }
    }

    private void SimulateComms(float dt, OperationalState obc, OperationalState adcs, OperationalState comms)
    {
        switch (comms)
        {
            case OperationalState.Active:
                signalQuality = Mathf.Clamp01(1f - (attitudeErrorDeg / 60f));
                break;

            case OperationalState.Standby:
                signalQuality = Mathf.Clamp01(0.5f - (attitudeErrorDeg / 90f));
                break;

            case OperationalState.Off:
                signalQuality = 0f;
                break;

            case OperationalState.Fault:
                signalQuality = Mathf.Clamp01(0.2f - (attitudeErrorDeg / 120f));
                break;
        }

        if (adcs == OperationalState.Fault || adcs == OperationalState.Off)
            signalQuality *= 0.75f;

        commandLinkAvailable =
            powerAvailable &&
            obc != OperationalState.Off &&
            obc != OperationalState.Fault &&
            comms != OperationalState.Off &&
            signalQuality > 0.15f;
    }

    private void SimulatePayload(float dt, OperationalState payload)
    {
        if (payload == OperationalState.Active && powerAvailable && commandLinkAvailable)
        {
            float pointingFactor = Mathf.Clamp01(1f - (attitudeErrorDeg / 20f));
            payloadDataRate = pointingFactor * Mathf.Clamp01(signalQuality) * 1.0f;
        }
        else if (payload == OperationalState.Standby)
        {
            payloadDataRate = 0.1f;
        }
        else if (payload == OperationalState.Fault)
        {
            payloadDataRate = 0f;
            temperatureC += 0.02f * dt;
        }
        else
        {
            payloadDataRate = 0f;
        }
    }

    private void SimulatePropulsion(float dt, OperationalState prop)
    {
        switch (prop)
        {
            case OperationalState.Active:
                fuelPercent -= 0.08f * dt;
                temperatureC += 0.15f * dt;
                break;

            case OperationalState.Standby:
                break;

            case OperationalState.Off:
                break;

            case OperationalState.Fault:
                fuelPercent -= 0.03f * dt;
                temperatureC += 0.08f * dt;
                break;
        }
    }

    private void ApplyDependencies(
        OperationalState eps,
        OperationalState obc,
        OperationalState adcs,
        OperationalState comms,
        OperationalState payload)
    {
        // EPS failure knocks out powered systems
        if (eps == OperationalState.Off || eps == OperationalState.Fault)
        {
            ForceSubsystemStateIfNeeded("OBC", OperationalState.Off, "No EPS power");
            ForceSubsystemStateIfNeeded("ADCS", OperationalState.Off, "Disabled by EPS");
            ForceSubsystemStateIfNeeded("COMMS", OperationalState.Off, "Disabled by EPS");
            ForceSubsystemStateIfNeeded("PAYLOAD", OperationalState.Off, "Disabled by EPS");
        }

        // OBC failure removes higher control
        if (obc == OperationalState.Off || obc == OperationalState.Fault)
        {
            ForceSubsystemStateIfNeeded("ADCS", OperationalState.Standby, "No OBC control");
            ForceSubsystemStateIfNeeded("PAYLOAD", OperationalState.Standby, "No OBC control");

            if (GetState("COMMS") == OperationalState.Active)
                ForceSubsystemStateIfNeeded("COMMS", OperationalState.Standby, "Limited without OBC");
        }

        // Bad attitude harms pointing-dependent systems
        if ((adcs == OperationalState.Fault || adcs == OperationalState.Off) && attitudeErrorDeg > 10f)
        {
            if (payload == OperationalState.Active)
                ForceSubsystemStateIfNeeded("PAYLOAD", OperationalState.Standby, "Poor pointing");
        }

        if ((adcs == OperationalState.Fault || adcs == OperationalState.Off) && attitudeErrorDeg > 20f)
        {
            if (comms == OperationalState.Active)
                ForceSubsystemStateIfNeeded("COMMS", OperationalState.Standby, "Poor antenna pointing");
        }

        // Dead battery hard stops
        if (batteryPercent <= 0.1f)
        {
            ForceSubsystemStateIfNeeded("OBC", OperationalState.Off, "Battery depleted");
            ForceSubsystemStateIfNeeded("ADCS", OperationalState.Off, "Battery depleted");
            ForceSubsystemStateIfNeeded("COMMS", OperationalState.Off, "Battery depleted");
            ForceSubsystemStateIfNeeded("PROP", OperationalState.Off, "Battery depleted");
            ForceSubsystemStateIfNeeded("PAYLOAD", OperationalState.Off, "Battery depleted");
        }

        // No fuel disables propulsion
        if (fuelPercent <= 0.1f)
        {
            ForceSubsystemStateIfNeeded("PROP", OperationalState.Off, "Fuel depleted");
        }
    }

    private void ForceSubsystemStateIfNeeded(string subsystemName, OperationalState targetState, string reason)
    {
        if (operations == null)
            return;

        OperationalState current = operations.GetSubsystemState(subsystemName);
        if (current == targetState)
            return;

        // STRUCT / MECH üçün bu manager state zorlamır
        if (subsystemName == "STRUCT" || subsystemName == "MECH")
            return;

        operations.SetSubsystemState(subsystemName, targetState, reason);
    }

    private void ClampTelemetry()
    {
        batteryPercent = Mathf.Clamp(batteryPercent, 0f, 100f);
        fuelPercent = Mathf.Clamp(fuelPercent, 0f, 100f);
        attitudeErrorDeg = Mathf.Clamp(attitudeErrorDeg, 0f, 180f);
        angularRateDegPerSec = Mathf.Clamp(angularRateDegPerSec, 0f, 25f);
        signalQuality = Mathf.Clamp01(signalQuality);
        temperatureC = Mathf.Clamp(temperatureC, -50f, 120f);
        payloadDataRate = Mathf.Clamp(payloadDataRate, 0f, 10f);
    }

    [ContextMenu("Reset Telemetry")]
    public void ResetTelemetry()
    {
        batteryPercent = 100f;
        temperatureC = 22f;
        attitudeErrorDeg = 0f;
        angularRateDegPerSec = 0.02f;
        signalQuality = 1f;
        fuelPercent = 100f;
        payloadDataRate = 0f;
        powerAvailable = true;
        commandLinkAvailable = true;
    }
}