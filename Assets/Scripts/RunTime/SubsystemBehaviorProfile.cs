using UnityEngine;

[CreateAssetMenu(fileName = "SubsystemBehaviorProfile", menuName = "Satellite Viewer/Subsystem Behavior Profile")]
public class SubsystemBehaviorProfile : ScriptableObject
{
    [Header("Identity")]
    public string subsystemName = "EPS";

    [Header("Power Draw (battery % / sec style abstract units)")]
    public float activePowerDraw = 0.02f;
    public float standbyPowerDraw = 0.01f;
    public float faultPowerDraw = 0.03f;

    [Header("Heat Rate (deg C / sec abstract)")]
    public float activeHeatRate = 0.01f;
    public float standbyHeatRate = 0.005f;
    public float faultHeatRate = 0.02f;

    [Header("Requirements")]
    public bool requiresPower = true;
    public bool requiresObc = false;
    public bool requiresAdcsForPointing = false;

    [Header("Payload/Comms Specific")]
    public float nominalDataRate = 1.0f;
    public float degradedDataRate = 0.1f;

    [Header("Propulsion Specific")]
    public float activeFuelUseRate = 0.08f;
    public float faultFuelLeakRate = 0.03f;
}