using UnityEngine;
using SatelliteViewer.Data;

public class SatelliteOrbitalMotion : MonoBehaviour
{
    [Header("References")]
    public Transform satelliteRoot;
    public SatelliteOperationsManager operationsManager;

    [Header("Orbit Center")]
    public Transform orbitCenter;
    public Vector3 orbitCenterPosition = Vector3.zero;
    public bool useOrbitCenterTransform = false;

    [Header("Nominal Orbit")]
    public float semiMajorAxis = 12f;
    public float semiMinorAxis = 9f;
    public float inclinationDeg = 35f;
    public float orbitPeriodSeconds = 40f;
    public float orbitPhaseDeg = 0f;

    [Header("Nominal Body Rotation")]
    public bool rotateBody = true;
    public Vector3 bodySpinAxis = Vector3.up;
    public float bodySpinDegPerSec = 30f;

    [Header("Nominal Attitude")]
    public bool faceVelocityDirection = true;
    public bool addNadirBias = false;
    public float attitudeSmooth = 4f;

    [Header("Runtime")]
    public bool simulate = true;
    public bool useUnscaledTime = false;

    [Header("Failure / Degradation Effects")]
    public float adcsStandbyAttitudePenalty = 0.35f;
    public float adcsOffAttitudePenalty = 0.75f;
    public float adcsFaultAttitudePenalty = 1.0f;

    public float obcStandbyGuidancePenalty = 0.15f;
    public float obcOffGuidancePenalty = 0.45f;
    public float obcFaultGuidancePenalty = 0.65f;

    public float epsStandbyPowerPenalty = 0.10f;
    public float epsOffPowerPenalty = 0.50f;
    public float epsFaultPowerPenalty = 0.70f;

    [Header("Visual Instability")]
    public float maxFaultTumbleDeg = 25f;
    public float maxFaultYawDeg = 18f;
    public float maxWrongThrustOffset = 3.0f;
    public float wrongThrustSpeed = 0.35f;

    [Header("Propulsion Influence")]
    public float propActiveOrbitRaiseRate = 0.20f;
    public float propActiveMinorRaiseRate = 0.12f;
    public float propFaultDriftRate = 0.10f;
    public float propFaultInclinationRate = 2.0f;

    [Header("Limits")]
    public float minSemiMajorAxis = 4f;
    public float maxSemiMajorAxis = 80f;
    public float minSemiMinorAxis = 3f;
    public float maxSemiMinorAxis = 80f;
    public float minOrbitPeriodSeconds = 8f;
    public float maxOrbitPeriodSeconds = 300f;

    private float _orbitAngleDeg;
    private Vector3 _lastWorldPos;
    private bool _initialized;

    private float _runtimeSemiMajor;
    private float _runtimeSemiMinor;
    private float _runtimeInclination;
    private float _runtimeOrbitPeriod;

    private Quaternion _baseRotationOffset = Quaternion.identity;
    private float _bodySpinAngleDeg;

    private void Start()
    {
        if (satelliteRoot == null)
            satelliteRoot = transform;

        if (operationsManager == null)
            operationsManager = FindFirstObjectByType<SatelliteOperationsManager>(FindObjectsInactive.Include);

        _orbitAngleDeg = orbitPhaseDeg;

        _runtimeSemiMajor = semiMajorAxis;
        _runtimeSemiMinor = semiMinorAxis;
        _runtimeInclination = inclinationDeg;
        _runtimeOrbitPeriod = orbitPeriodSeconds;

        // Keep the model's authored orientation (e.g. the FBX -90 deg X import rotation)
        // instead of replacing it with a bare LookRotation.
        _baseRotationOffset = satelliteRoot.rotation;

        Vector3 startPos = GetOrbitWorldPosition(_orbitAngleDeg);
        satelliteRoot.position = startPos;
        _lastWorldPos = startPos;
        _initialized = true;
    }

    private void Update()
    {
        if (!simulate || satelliteRoot == null)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f)
            return;

        UpdateOrbitStateFromSubsystems(dt);

        if (_runtimeOrbitPeriod <= 0.001f)
            _runtimeOrbitPeriod = orbitPeriodSeconds > 0.001f ? orbitPeriodSeconds : 40f;

        float orbitDegPerSec = 360f / _runtimeOrbitPeriod;
        _orbitAngleDeg += orbitDegPerSec * dt;
        if (_orbitAngleDeg > 360f)
            _orbitAngleDeg -= 360f;

        Vector3 newPos = GetOrbitWorldPosition(_orbitAngleDeg);
        Vector3 velocity = _initialized ? (newPos - _lastWorldPos) / Mathf.Max(dt, 0.0001f) : Vector3.forward;

        satelliteRoot.position = newPos;

        bool attitudeApplied = ApplyAttitudeFromSubsystems(velocity, dt);
        ApplyBodySpinFromSubsystems(dt, attitudeApplied);

        _lastWorldPos = newPos;
        _initialized = true;
    }

    private void UpdateOrbitStateFromSubsystems(float dt)
    {
        OperationalState eps = GetSubsystemState("EPS");
        OperationalState obc = GetSubsystemState("OBC");
        OperationalState adcs = GetSubsystemState("ADCS");
        OperationalState prop = GetSubsystemState("PROP");

        float guidancePenalty = GetGuidancePenalty(obc);
        float powerPenalty = GetPowerPenalty(eps);

        // nominala yumşaq qayıdış
        _runtimeSemiMajor = Mathf.Lerp(_runtimeSemiMajor, semiMajorAxis, dt * 0.35f);
        _runtimeSemiMinor = Mathf.Lerp(_runtimeSemiMinor, semiMinorAxis, dt * 0.35f);
        _runtimeInclination = Mathf.Lerp(_runtimeInclination, inclinationDeg, dt * 0.25f);
        _runtimeOrbitPeriod = Mathf.Lerp(_runtimeOrbitPeriod, orbitPeriodSeconds, dt * 0.25f);

        // PROP active -> orbit raise / maneuver
        if (prop == OperationalState.Active)
        {
            float controlFactor = 1f - Mathf.Clamp01(guidancePenalty + powerPenalty);
            _runtimeSemiMajor += propActiveOrbitRaiseRate * controlFactor * dt;
            _runtimeSemiMinor += propActiveMinorRaiseRate * controlFactor * dt;
            _runtimeOrbitPeriod += 0.08f * dt;
        }
        // PROP fault -> wrong thrust / orbit drift
        else if (prop == OperationalState.Fault)
        {
            float wrong = Mathf.Sin(Time.time * wrongThrustSpeed) * maxWrongThrustOffset;
            _runtimeSemiMajor += (propFaultDriftRate + wrong * 0.05f) * dt;
            _runtimeSemiMinor -= (propFaultDriftRate * 0.65f) * dt;
            _runtimeInclination += Mathf.Sin(Time.time * 0.7f) * propFaultInclinationRate * dt;
            _runtimeOrbitPeriod += 0.12f * dt;
        }
        else if (prop == OperationalState.Off)
        {
            // kiçik natural drift
            _runtimeSemiMajor -= 0.015f * dt;
            _runtimeSemiMinor -= 0.010f * dt;
        }

        // OBC / EPS problem -> orbit maintenance zəifləyir
        float maintenanceLoss = guidancePenalty + powerPenalty;
        if (maintenanceLoss > 0.001f)
        {
            // Scaled by dt (tuned so 60 fps looks as before); previously applied per frame,
            // so the drift magnitude depended on frame rate.
            _runtimeSemiMajor += Mathf.Sin(Time.time * 0.35f) * maintenanceLoss * 1.8f * dt;
            _runtimeSemiMinor += Mathf.Cos(Time.time * 0.41f) * maintenanceLoss * 1.5f * dt;
            _runtimeInclination += Mathf.Sin(Time.time * 0.22f) * maintenanceLoss * 0.35f * dt;
        }

        // ADCS orbit shape-ı fiziki idarə etmir, amma pointing problemə görə orbit plane visualization azca “unstable” görünə bilər
        if (adcs == OperationalState.Fault)
        {
            _runtimeInclination += Mathf.Sin(Time.time * 1.2f) * 0.6f * dt;
        }

        _runtimeSemiMajor = Mathf.Clamp(_runtimeSemiMajor, minSemiMajorAxis, maxSemiMajorAxis);
        _runtimeSemiMinor = Mathf.Clamp(_runtimeSemiMinor, minSemiMinorAxis, maxSemiMinorAxis);
        _runtimeOrbitPeriod = Mathf.Clamp(_runtimeOrbitPeriod, minOrbitPeriodSeconds, maxOrbitPeriodSeconds);
    }

    private bool ApplyAttitudeFromSubsystems(Vector3 velocity, float dt)
    {
        if (!faceVelocityDirection || velocity.sqrMagnitude < 0.000001f)
            return false;

        OperationalState eps = GetSubsystemState("EPS");
        OperationalState obc = GetSubsystemState("OBC");
        OperationalState adcs = GetSubsystemState("ADCS");

        float attitudePenalty = GetAttitudePenalty(adcs) + GetGuidancePenalty(obc) + GetPowerPenalty(eps);
        attitudePenalty = Mathf.Clamp01(attitudePenalty);

        Vector3 forward = velocity.normalized;
        Vector3 up = Vector3.up;

        if (addNadirBias)
        {
            Vector3 toCenter = (GetOrbitCenter() - satelliteRoot.position).normalized;
            if (toCenter.sqrMagnitude > 0.0001f)
                up = Vector3.Slerp(Vector3.up, -toCenter, 0.5f).normalized;
        }

        Quaternion nominalTarget = Quaternion.LookRotation(forward, up);

        if (attitudePenalty > 0.001f)
        {
            float t = Time.time;

            float rollErr = Mathf.Sin(t * 1.7f) * maxFaultTumbleDeg * attitudePenalty;
            float pitchErr = Mathf.Cos(t * 1.15f) * (maxFaultTumbleDeg * 0.65f) * attitudePenalty;
            float yawErr = Mathf.Sin(t * 0.9f) * maxFaultYawDeg * attitudePenalty;

            Quaternion errorRot = Quaternion.Euler(pitchErr, yawErr, rollErr);
            nominalTarget = nominalTarget * errorRot;
        }

        // Body spin is folded into the attitude target; rotating the transform separately
        // was cancelled by this Slerp every frame (net effect: a fixed tilt, no visible spin).
        Quaternion spinRot = rotateBody ? Quaternion.AngleAxis(_bodySpinAngleDeg, GetSpinAxis()) : Quaternion.identity;

        float smooth = Mathf.Lerp(attitudeSmooth, 0.8f, attitudePenalty);
        satelliteRoot.rotation = Quaternion.Slerp(satelliteRoot.rotation, nominalTarget * _baseRotationOffset * spinRot, dt * smooth);
        return true;
    }

    private Vector3 GetSpinAxis()
    {
        return bodySpinAxis.sqrMagnitude > 0.0001f ? bodySpinAxis.normalized : Vector3.up;
    }

    private void ApplyBodySpinFromSubsystems(float dt, bool attitudeApplied)
    {
        if (!rotateBody)
            return;

        OperationalState adcs = GetSubsystemState("ADCS");
        OperationalState eps = GetSubsystemState("EPS");

        Vector3 axis = GetSpinAxis();
        float spin = bodySpinDegPerSec;

        if (adcs == OperationalState.Standby)
            spin *= 1.2f;
        else if (adcs == OperationalState.Off)
            spin *= 1.8f;
        else if (adcs == OperationalState.Fault)
            spin *= 2.6f;

        if (eps == OperationalState.Off || eps == OperationalState.Fault)
            spin *= 1.25f;

        // Advance the spin angle for the next attitude target (see ApplyAttitudeFromSubsystems).
        _bodySpinAngleDeg = Mathf.Repeat(_bodySpinAngleDeg + spin * dt, 360f);

        // Without an attitude target nothing else writes the rotation, so spin directly.
        if (!attitudeApplied)
            satelliteRoot.Rotate(axis, spin * dt, Space.Self);
    }

    private OperationalState GetSubsystemState(string subsystemId)
    {
        if (operationsManager == null || string.IsNullOrWhiteSpace(subsystemId))
            return OperationalState.Standby;

        return operationsManager.GetSubsystemState(subsystemId);
    }

    private float GetAttitudePenalty(OperationalState state)
    {
        return state switch
        {
            OperationalState.Active => 0f,
            OperationalState.Standby => adcsStandbyAttitudePenalty,
            OperationalState.Off => adcsOffAttitudePenalty,
            OperationalState.Fault => adcsFaultAttitudePenalty,
            _ => 0f
        };
    }

    private float GetGuidancePenalty(OperationalState state)
    {
        return state switch
        {
            OperationalState.Active => 0f,
            OperationalState.Standby => obcStandbyGuidancePenalty,
            OperationalState.Off => obcOffGuidancePenalty,
            OperationalState.Fault => obcFaultGuidancePenalty,
            _ => 0f
        };
    }

    private float GetPowerPenalty(OperationalState state)
    {
        return state switch
        {
            OperationalState.Active => 0f,
            OperationalState.Standby => epsStandbyPowerPenalty,
            OperationalState.Off => epsOffPowerPenalty,
            OperationalState.Fault => epsFaultPowerPenalty,
            _ => 0f
        };
    }

    private Vector3 GetOrbitCenter()
    {
        if (useOrbitCenterTransform && orbitCenter != null)
            return orbitCenter.position;

        return orbitCenterPosition;
    }

    private Vector3 GetOrbitWorldPosition(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;

        Vector3 local = new Vector3(
            Mathf.Cos(rad) * _runtimeSemiMajor,
            0f,
            Mathf.Sin(rad) * _runtimeSemiMinor
        );

        Quaternion inclination = Quaternion.Euler(_runtimeInclination, 0f, 0f);
        Vector3 tilted = inclination * local;

        return GetOrbitCenter() + tilted;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = useOrbitCenterTransform && orbitCenter != null ? orbitCenter.position : orbitCenterPosition;

        Gizmos.color = Color.cyan;

        const int steps = 96;
        Vector3 prev = center + Quaternion.Euler(inclinationDeg, 0f, 0f) * new Vector3(semiMajorAxis, 0f, 0f);

        for (int i = 1; i <= steps; i++)
        {
            float t = (i / (float)steps) * Mathf.PI * 2f;
            Vector3 local = new Vector3(
                Mathf.Cos(t) * semiMajorAxis,
                0f,
                Mathf.Sin(t) * semiMinorAxis
            );

            Vector3 next = center + Quaternion.Euler(inclinationDeg, 0f, 0f) * local;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}