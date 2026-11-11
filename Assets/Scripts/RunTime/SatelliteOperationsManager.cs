using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SatelliteViewer.Data;

public class SatelliteOperationsManager : MonoBehaviour
{
    [Serializable]
    public class PartBinding
    {
        public string partName;
        public string subsystem;
        public OperationalState initialState = OperationalState.Standby;
        [TextArea] public string notes;
    }

    [Header("Optional Initial Part Bindings")]
    [SerializeField] private List<PartBinding> initialPartBindings = new List<PartBinding>();

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;
    [SerializeField] private bool autoCreateDefaultSubsystems = true;

    private readonly Dictionary<string, OperationalState> subsystemStates =
        new Dictionary<string, OperationalState>();

    private readonly Dictionary<string, OperationalState> partStates =
        new Dictionary<string, OperationalState>();

    private readonly Dictionary<string, string> partToSubsystem =
        new Dictionary<string, string>();

    private readonly Dictionary<string, List<string>> subsystemToParts =
        new Dictionary<string, List<string>>();

    public Action OnOperationalStateChanged;

    public IReadOnlyDictionary<string, OperationalState> SubsystemStates => subsystemStates;
    public IReadOnlyDictionary<string, OperationalState> PartStates => partStates;

    private void Awake()
    {
        if (autoCreateDefaultSubsystems)
            EnsureDefaultSubsystems();

        if (initialPartBindings != null && initialPartBindings.Count > 0)
            ConfigureParts(initialPartBindings, rebuildAfter: true);
    }

    #region Initialization / Registration

    public void EnsureDefaultSubsystems()
    {
        EnsureSubsystem("STRUCT", OperationalState.Standby);
        EnsureSubsystem("MECH", OperationalState.Standby);
        EnsureSubsystem("EPS", OperationalState.Active);
        EnsureSubsystem("OBC", OperationalState.Active);
        EnsureSubsystem("ADCS", OperationalState.Active);
        EnsureSubsystem("COMMS", OperationalState.Active);
        EnsureSubsystem("PROP", OperationalState.Standby);
        EnsureSubsystem("PAYLOAD", OperationalState.Standby);
    }

    public void EnsureSubsystem(string subsystemName, OperationalState defaultState = OperationalState.Standby)
    {
        string key = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(key))
            return;

        if (!subsystemStates.ContainsKey(key))
            subsystemStates[key] = defaultState;

        if (!subsystemToParts.ContainsKey(key))
            subsystemToParts[key] = new List<string>();
    }

    /// <summary>
    /// Sets a subsystem's initial state (e.g. JSON defaultOperationalState) without notifying.
    /// Unlike EnsureSubsystem this overwrites a state that already exists (such as the hard-coded
    /// defaults created in Awake or a state left over from a previously loaded satellite).
    /// </summary>
    public void InitializeSubsystemState(string subsystemName, OperationalState state)
    {
        string key = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(key))
            return;

        EnsureSubsystem(key, state);
        subsystemStates[key] = state;
    }

    public void ConfigureParts(IEnumerable<PartBinding> bindings, bool rebuildAfter = true)
    {
        if (bindings == null)
            return;

        foreach (PartBinding binding in bindings)
        {
            if (binding == null)
                continue;

            RegisterOrUpdatePart(binding.partName, binding.subsystem, binding.initialState);
        }

        if (rebuildAfter)
            RebuildRuntimeState();
    }

    public void RegisterOrUpdatePart(string partName, string subsystemName, OperationalState initialState = OperationalState.Standby)
    {
        string partKey = NormalizePart(partName);
        if (string.IsNullOrEmpty(partKey))
            return;

        string subsystemKey = NormalizeSubsystem(subsystemName);

        if (string.IsNullOrEmpty(subsystemKey))
            subsystemKey = InferSubsystemFromPartName(partKey);

        if (string.IsNullOrEmpty(subsystemKey))
            subsystemKey = "MECH";

        EnsureSubsystem(subsystemKey, OperationalState.Standby);

        if (partToSubsystem.TryGetValue(partKey, out string oldSubsystem))
        {
            if (!string.Equals(oldSubsystem, subsystemKey, StringComparison.Ordinal))
            {
                if (subsystemToParts.TryGetValue(oldSubsystem, out List<string> oldList))
                    oldList.Remove(partKey);
            }
        }

        partToSubsystem[partKey] = subsystemKey;

        if (!subsystemToParts.TryGetValue(subsystemKey, out List<string> partList))
        {
            partList = new List<string>();
            subsystemToParts[subsystemKey] = partList;
        }

        if (!partList.Contains(partKey))
            partList.Add(partKey);

        if (!partStates.ContainsKey(partKey))
            partStates[partKey] = initialState;

        if (logStateChanges)
            Debug.Log($"[SatelliteOperationsManager] Registered part -> {partKey} in {subsystemKey} | Initial={partStates[partKey]}");
    }

    public void ClearRegisteredParts(bool notify = true)
    {
        partStates.Clear();
        partToSubsystem.Clear();
        subsystemToParts.Clear();

        foreach (string subsystem in subsystemStates.Keys.ToList())
            subsystemToParts[subsystem] = new List<string>();

        if (notify)
            NotifyStateChanged();
    }

    #endregion

    #region Getters

    public OperationalState GetSubsystemState(string subsystemName)
    {
        string key = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(key))
            return OperationalState.Off;

        if (subsystemStates.TryGetValue(key, out OperationalState state))
            return state;

        return OperationalState.Off;
    }

    public OperationalState GetPartState(string partName)
    {
        string key = NormalizePart(partName);
        if (string.IsNullOrEmpty(key))
            return OperationalState.Off;

        if (partStates.TryGetValue(key, out OperationalState state))
            return state;

        return OperationalState.Off;
    }

    public string GetPartSubsystem(string partName)
    {
        string key = NormalizePart(partName);
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (partToSubsystem.TryGetValue(key, out string subsystem))
            return subsystem;

        return string.Empty;
    }

    public List<string> GetPartsInSubsystem(string subsystemName)
    {
        string key = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(key))
            return new List<string>();

        if (subsystemToParts.TryGetValue(key, out List<string> parts))
            return new List<string>(parts);

        return new List<string>();
    }

    public bool HasPart(string partName)
    {
        string key = NormalizePart(partName);
        return !string.IsNullOrEmpty(key) && partStates.ContainsKey(key);
    }

    public bool HasSubsystem(string subsystemName)
    {
        string key = NormalizeSubsystem(subsystemName);
        return !string.IsNullOrEmpty(key) && subsystemStates.ContainsKey(key);
    }

    #endregion

    #region State Setters

    public void SetSubsystemState(string subsystemName, OperationalState newState, string reason = "")
    {
        string key = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(key))
            return;

        EnsureSubsystem(key, OperationalState.Standby);

        OperationalState oldState = subsystemStates[key];
        if (oldState == newState)
            return;

        subsystemStates[key] = newState;

        if (logStateChanges)
            Debug.Log($"[SatelliteOperationsManager] Subsystem state change -> {key} = {newState} | Reason: {reason}");

        // Only this subsystem's parts follow the new state; rebuilding every subsystem here
        // used to wipe per-part states (e.g. a single faulted wheel) in unrelated subsystems.
        RebuildPartStatesForSubsystem(key);
        NotifyStateChanged();
    }

    public void SetPartState(string partName, OperationalState newState, string reason = "")
    {
        string partKey = NormalizePart(partName);
        if (string.IsNullOrEmpty(partKey))
            return;

        bool newlyRegistered = false;
        if (!partStates.ContainsKey(partKey))
        {
            string inferredSubsystem = InferSubsystemFromPartName(partKey);
            RegisterOrUpdatePart(partKey, inferredSubsystem, newState);
            newlyRegistered = true;
        }

        OperationalState oldState = partStates[partKey];
        if (oldState == newState && !newlyRegistered)
            return;

        partStates[partKey] = newState;

        if (logStateChanges)
            Debug.Log($"[SatelliteOperationsManager] Part state change -> {partKey} = {newState} | Reason: {reason}");

        RebuildSubsystemStateFromParts(GetPartSubsystem(partKey));
        NotifyStateChanged();
    }

    public void SetAllPartsInSubsystem(string subsystemName, OperationalState newState, string reason = "")
    {
        string subsystemKey = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(subsystemKey))
            return;

        EnsureSubsystem(subsystemKey, OperationalState.Standby);

        if (!subsystemToParts.TryGetValue(subsystemKey, out List<string> parts))
            return;

        bool changed = false;

        for (int i = 0; i < parts.Count; i++)
        {
            string partKey = parts[i];
            if (!partStates.ContainsKey(partKey))
                continue;

            if (partStates[partKey] == newState)
                continue;

            partStates[partKey] = newState;
            changed = true;

            if (logStateChanges)
                Debug.Log($"[SatelliteOperationsManager] Part state change -> {partKey} = {newState} | Reason: {reason}");
        }

        if (subsystemStates.TryGetValue(subsystemKey, out OperationalState oldSubsystemState) && oldSubsystemState != newState)
            changed = true;

        subsystemStates[subsystemKey] = newState;

        if (changed)
            NotifyStateChanged();
    }

    #endregion

    #region Rebuild Logic

    public void RebuildRuntimeState()
    {
        // Parts keep their registered/initial states (JSON defaultState); only parts without a
        // state inherit their subsystem's. Subsystem states are then derived from the parts.
        // (Previously every part was overwritten with its subsystem state, discarding JSON defaults.)
        FillMissingPartStatesFromSubsystems();
        RebuildAllSubsystemStatesFromParts();
        NotifyStateChanged();
    }

    private void FillMissingPartStatesFromSubsystems()
    {
        foreach (KeyValuePair<string, List<string>> kvp in subsystemToParts)
        {
            OperationalState subsystemState = GetSubsystemState(kvp.Key);
            foreach (string partKey in kvp.Value)
            {
                if (!partStates.ContainsKey(partKey))
                    partStates[partKey] = GetPartStateForSubsystemState(kvp.Key, subsystemState);
            }
        }
    }

    private void RebuildPartStatesForSubsystem(string subsystemKey)
    {
        if (!subsystemToParts.TryGetValue(subsystemKey, out List<string> parts))
            return;

        OperationalState subsystemState = GetSubsystemState(subsystemKey);
        OperationalState partState = GetPartStateForSubsystemState(subsystemKey, subsystemState);

        for (int i = 0; i < parts.Count; i++)
            partStates[parts[i]] = partState;
    }

    public void RebuildPartStatesFromSubsystems()
    {
        foreach (KeyValuePair<string, List<string>> kvp in subsystemToParts)
        {
            string subsystem = kvp.Key;
            List<string> parts = kvp.Value;

            OperationalState subsystemState = GetSubsystemState(subsystem);

            for (int i = 0; i < parts.Count; i++)
            {
                string currentPartKey = parts[i];

                if (!partStates.ContainsKey(currentPartKey))
                    partStates[currentPartKey] = subsystemState;
                else
                    partStates[currentPartKey] = GetPartStateForSubsystemState(subsystem, subsystemState);
            }
        }
    }

    public void RebuildAllSubsystemStatesFromParts()
    {
        foreach (string subsystem in subsystemStates.Keys.ToList())
            RebuildSubsystemStateFromParts(subsystem);
    }

    public void RebuildSubsystemStateFromParts(string subsystemName)
    {
        string subsystemKey = NormalizeSubsystem(subsystemName);
        if (string.IsNullOrEmpty(subsystemKey))
            return;

        EnsureSubsystem(subsystemKey, OperationalState.Standby);

        if (!subsystemToParts.TryGetValue(subsystemKey, out List<string> parts) || parts.Count == 0)
            return;

        bool anyFault = false;
        bool anyActive = false;
        bool anyStandby = false;
        bool allOff = true;

        for (int i = 0; i < parts.Count; i++)
        {
            string currentPartKey = parts[i];

            if (!partStates.TryGetValue(currentPartKey, out OperationalState state))
                continue;

            if (state == OperationalState.Fault)
                anyFault = true;

            if (state == OperationalState.Active)
                anyActive = true;

            if (state == OperationalState.Standby)
                anyStandby = true;

            if (state != OperationalState.Off)
                allOff = false;
        }

        OperationalState newSubsystemState;

        if (anyFault)
            newSubsystemState = OperationalState.Fault;
        else if (anyActive)
            newSubsystemState = OperationalState.Active;
        else if (anyStandby)
            newSubsystemState = OperationalState.Standby;
        else if (allOff)
            newSubsystemState = OperationalState.Off;
        else
            newSubsystemState = OperationalState.Standby;

        OperationalState oldState = GetSubsystemState(subsystemKey);
        subsystemStates[subsystemKey] = newSubsystemState;

        if (logStateChanges && oldState != newSubsystemState)
            Debug.Log($"[SatelliteOperationsManager] Rebuilt subsystem -> {subsystemKey} = {newSubsystemState}");
    }

    private OperationalState GetPartStateForSubsystemState(string subsystemName, OperationalState subsystemState)
    {
        string subsystemKey = NormalizeSubsystem(subsystemName);

        switch (subsystemKey)
        {
            case "STRUCT":
            case "MECH":
                return subsystemState switch
                {
                    OperationalState.Active => OperationalState.Standby,
                    OperationalState.Off => OperationalState.Standby,
                    _ => subsystemState
                };

            default:
                return subsystemState;
        }
    }

    #endregion

    #region Helpers

    public void ForceNominalStartupState()
    {
        SetSubsystemState("EPS", OperationalState.Active, "Nominal startup");
        SetSubsystemState("OBC", OperationalState.Active, "Nominal startup");
        SetSubsystemState("ADCS", OperationalState.Active, "Nominal startup");
        SetSubsystemState("COMMS", OperationalState.Active, "Nominal startup");
        SetSubsystemState("PROP", OperationalState.Standby, "Nominal startup");
        SetSubsystemState("PAYLOAD", OperationalState.Standby, "Nominal startup");
        SetSubsystemState("STRUCT", OperationalState.Standby, "Nominal startup");
        SetSubsystemState("MECH", OperationalState.Standby, "Nominal startup");
    }

    public void ResetAllStatesToStandby()
    {
        foreach (string subsystem in subsystemStates.Keys.ToList())
            subsystemStates[subsystem] = OperationalState.Standby;

        foreach (string part in partStates.Keys.ToList())
            partStates[part] = OperationalState.Standby;

        NotifyStateChanged();
    }

    public void RegisterPartIfMissing(string partName)
    {
        string partKey = NormalizePart(partName);
        if (string.IsNullOrEmpty(partKey) || partStates.ContainsKey(partKey))
            return;

        string inferredSubsystem = InferSubsystemFromPartName(partKey);
        RegisterOrUpdatePart(partKey, inferredSubsystem, OperationalState.Standby);
        NotifyStateChanged();
    }

    private string NormalizeSubsystem(string subsystemName)
    {
        if (string.IsNullOrWhiteSpace(subsystemName))
            return string.Empty;

        return subsystemName.Trim().ToUpperInvariant();
    }

    private string NormalizePart(string partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return string.Empty;

        return partName.Trim();
    }

    private string InferSubsystemFromPartName(string partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return "MECH";

        string name = partName.Trim().ToUpperInvariant();

        if (name.Contains("BATTERY") || name.Contains("SOLAR") || name.Contains("PDU") || name.Contains("POWER"))
            return "EPS";

        if (name.Contains("OBC") || name.Contains("COMPUTER") || name.Contains("CPU") || name.Contains("AVIONICS"))
            return "OBC";

        if (name.Contains("REACTIONWHEEL") || name.Contains("REACTION_WHEEL") || name.Contains("RW") ||
            name.Contains("GYRO") || name.Contains("STARTRACKER") || name.Contains("STAR_TRACKER") ||
            name.Contains("SUNSENSOR") || name.Contains("SUN_SENSOR") || name.Contains("MAGNETORQUER") ||
            name.Contains("ADCS"))
            return "ADCS";

        if (name.Contains("ANTENNA") || name.Contains("TRANSMITTER") || name.Contains("RECEIVER") ||
            name.Contains("TRANSCEIVER") || name.Contains("COMMS") || name.Contains("RADIO"))
            return "COMMS";

        if (name.Contains("THRUSTER") || name.Contains("TANK") || name.Contains("VALVE") ||
            name.Contains("ENGINE") || name.Contains("PROP"))
            return "PROP";

        if (name.Contains("CAMERA") || name.Contains("SENSOR") || name.Contains("PAYLOAD") ||
            name.Contains("IMAGER") || name.Contains("RADAR"))
            return "PAYLOAD";

        if (name.Contains("PANEL") || name.Contains("HINGE") || name.Contains("DEPLOY") ||
            name.Contains("GIMBAL") || name.Contains("MECH"))
            return "MECH";

        if (name.Contains("FRAME") || name.Contains("BUS") || name.Contains("BODY") ||
            name.Contains("STRUCT") || name.Contains("CHASSIS"))
            return "STRUCT";

        return "MECH";
    }

    private void NotifyStateChanged()
    {
        OnOperationalStateChanged?.Invoke();
    }

    #endregion

    #region Debug / Context Menu

    [ContextMenu("Debug Print Subsystems")]
    private void DebugPrintSubsystems()
    {
        foreach (KeyValuePair<string, OperationalState> kvp in subsystemStates)
            Debug.Log($"[SatelliteOperationsManager] SUBSYSTEM -> {kvp.Key} = {kvp.Value}");
    }

    [ContextMenu("Debug Print Parts")]
    private void DebugPrintParts()
    {
        foreach (KeyValuePair<string, OperationalState> kvp in partStates)
        {
            string subsystem = GetPartSubsystem(kvp.Key);
            Debug.Log($"[SatelliteOperationsManager] PART -> {kvp.Key} = {kvp.Value} | Subsystem={subsystem}");
        }
    }

    [ContextMenu("Nominal Startup")]
    private void ContextNominalStartup()
    {
        ForceNominalStartupState();
    }

    [ContextMenu("Rebuild Part States From Subsystems")]
    private void ContextRebuildPartStates()
    {
        RebuildPartStatesFromSubsystems();
        NotifyStateChanged();
    }

    [ContextMenu("Rebuild Subsystem States From Parts")]
    private void ContextRebuildSubsystemStates()
    {
        RebuildAllSubsystemStatesFromParts();
        NotifyStateChanged();
    }

    #endregion
}