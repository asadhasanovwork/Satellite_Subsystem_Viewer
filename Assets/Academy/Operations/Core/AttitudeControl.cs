using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    public enum AdcsMode { Passive = 0, Detumble = 1, SunSafe = 2, Nadir = 3, Inertial = 4 }

    /// <summary>
    /// Attitude determination and control software (the classic four NOS3 modes plus inertial hold):
    /// PASSIVE - no control.
    /// DETUMBLE - B-dot with magnetorquers (and wheel rate damping) to stop the tumbling after separation.
    /// SUNSAFE - point the solar arrays at the Sun to survive.
    /// NADIR - point the payload at the Earth (needs attitude knowledge).
    /// INERTIAL - hold a fixed attitude in space.
    /// Plus momentum dumping with magnetorquers or thrusters when the wheels fill up.
    /// </summary>
    public sealed class AttitudeControl
    {
        private readonly SatelliteSim _sim;
        private readonly AdcsSettings _s;

        public AdcsMode Mode = AdcsMode.Detumble;
        public bool DumpEnabled = true;
        public bool Dumping { get; private set; }

        public Vec3d NadirAxis, SecondaryAxis, SunAxis;
        public bool SecondaryIsOrbitNormal;

        // Hardware (may be null)
        public ReactionWheelSet Wheels;
        public MagnetorquerSet Torquers;
        public ThrusterSet DumpThrusters;
        public readonly List<SensorComponent> Sensors = new List<SensorComponent>();

        // Determination results
        public bool KnowledgeValid { get; private set; }
        public double KnowledgeErrorDeg { get; private set; }
        public string KnowledgeSource { get; private set; }
        public bool RateValid { get; private set; }
        public Vec3d RateEstimate { get; private set; }
        public double PointingErrorDeg { get; private set; }
        public bool PayloadPointingOk { get; private set; }
        public string Status { get; private set; }

        private Vec3d _lastField;
        private bool _hasLastField;
        private double _controlTimer;
        private double _calmTimer;
        private double _lostKnowledgeTimer;
        private QuatD _inertialTarget = QuatD.Identity;
        private Vec3d _knowledgeErrorAxis = Vec3d.UnitX;
        private Vec3d _wheelTorqueCommand;
        private double _lastAbsoluteError = 180;
        private double _yawErrorDeg;
        private double _propagationSeconds;

        public AttitudeControl(SatelliteSim sim, AdcsSettings settings)
        {
            _sim = sim;
            _s = settings ?? new AdcsSettings();
            NadirAxis = ParamUtil.Vec(_s.nadirAxis, new Vec3d(0, -1, 0)).Swap().Normalized;
            SecondaryAxis = ParamUtil.Vec(_s.secondaryAxis, new Vec3d(0, 0, 1)).Swap().Normalized;
            SecondaryIsOrbitNormal = _s.secondaryTarget == "orbit_normal";
            Vec3d sun = ParamUtil.Vec(_s.sunAxis, Vec3d.Zero);
            SunAxis = sun.SqrLength > 1e-9 ? sun.Swap().Normalized : Vec3d.Zero;
            Status = "";
            KnowledgeSource = "none";
        }

        public AdcsSettings Settings { get { return _s; } }

        private SensorComponent Find(SensorType type, bool requireValid)
        {
            foreach (SensorComponent s in Sensors)
                if (s.Type == type && (!requireValid || s.Valid))
                    return s;
            return null;
        }

        public bool HasWheels { get { return Wheels != null && Wheels.Wheels.Count > 0; } }

        public string SetMode(AdcsMode mode)
        {
            if ((mode == AdcsMode.SunSafe || mode == AdcsMode.Nadir || mode == AdcsMode.Inertial) && !HasWheels)
                return "This satellite has no reaction wheels: only PASSIVE and DETUMBLE are possible.";
            if (mode == AdcsMode.Inertial)
                _inertialTarget = _sim.Env.Attitude;
            Mode = mode;
            _calmTimer = 0;
            _lostKnowledgeTimer = 0;
            _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "ADCS", "Mode " + mode.ToString().ToUpperInvariant());
            return "ADCS mode " + mode.ToString().ToUpperInvariant();
        }

        // ------------------------------------------------------------------ main loop

        /// <summary>Called every physics step; runs the control law at the control period.</summary>
        public void Step(double dt)
        {
            _controlTimer += dt;
            if (_controlTimer + 1e-9 >= Math.Max(0.05, _s.controlPeriod))
            {
                double period = _controlTimer;
                _controlTimer = 0;
                if (_sim.Computer == null || _sim.Computer.Running)
                    Control(period);
                else
                    Idle();
            }
            // Wheels keep the last command between control ticks
            if (Wheels != null)
                Wheels.CommandBodyTorque(_wheelTorqueCommand);
        }

        private void Idle()
        {
            _wheelTorqueCommand = Vec3d.Zero;
            if (Torquers != null) Torquers.DipoleCommand = Vec3d.Zero;
            Status = "computer not running";
            _hasLastField = false;
        }

        private void Control(double period)
        {
            SpaceEnvironment env = _sim.Env;
            Determine();

            Vec3d torque = Vec3d.Zero;
            Vec3d dipole = Vec3d.Zero;
            Status = "";
            PointingErrorDeg = 0;

            // Magnetometer field rate for B-dot
            SensorComponent mag = Find(SensorType.Magnetometer, true);
            Vec3d field = mag != null ? mag.MeasureField() : Vec3d.Zero;
            Vec3d fieldRate = Vec3d.Zero;
            bool haveFieldRate = false;
            if (mag != null)
            {
                if (_hasLastField && period > 0)
                {
                    fieldRate = (field - _lastField) / period;
                    haveFieldRate = true;
                }
                _lastField = field;
                _hasLastField = true;
            }
            else
            {
                _hasLastField = false;
            }

            Vec3d I = env.Inertia;
            double wn = Math.Max(0.005, _s.naturalFrequency);
            double z = Math.Max(0.3, _s.damping);
            Vec3d kp = I * (wn * wn);
            Vec3d kd = I * (2 * z * wn);
            bool wheelsOk = Wheels != null && Wheels.Working && Wheels.UsableCount > 0;

            switch (Mode)
            {
                case AdcsMode.Passive:
                    Status = "no control";
                    break;

                case AdcsMode.Detumble:
                    if (Torquers != null && Torquers.Working && haveFieldRate)
                    {
                        // Bang-bang B-dot: oppose the change of the field seen in the body frame
                        foreach (Vec3d a in Torquers.Axes)
                        {
                            double c = Vec3d.Dot(fieldRate, a);
                            if (Math.Abs(c) > 1e-12)
                                dipole -= a * (Math.Sign(c) * Torquers.MaxDipole);
                        }
                        Status = "B-dot detumbling";
                    }
                    // Wheels only help when there are no working magnetorquers: a tumble usually holds more
                    // momentum than the wheels can store, so real satellites detumble magnetically.
                    if (wheelsOk && RateValid && !(Torquers != null && Torquers.Working && haveFieldRate))
                    {
                        torque = -Vec3d.Scale(kd, RateEstimate);
                        Status = Status.Length > 0 ? Status + " + wheel damping" : "wheel rate damping";
                    }
                    if (Status.Length == 0)
                        Status = "cannot detumble: no working magnetorquers/magnetometer or wheels/gyro";

                    if (_s.autoSunSafeAfterDetumble && HasWheels && env.RateDegPerSec < _s.detumbleExitDps)
                    {
                        _calmTimer += period;
                        if (_calmTimer > 60)
                        {
                            _sim.Events.Add(env.Met, EventSeverity.Info, "ADCS", "Detumble complete (rates below " + _s.detumbleExitDps + " deg/s): switching to SUNSAFE");
                            Mode = AdcsMode.SunSafe;
                            _calmTimer = 0;
                        }
                    }
                    else
                    {
                        _calmTimer = 0;
                    }
                    break;

                case AdcsMode.SunSafe:
                    torque = SunSafeTorque(kp, kd);
                    break;

                case AdcsMode.Nadir:
                case AdcsMode.Inertial:
                    if (!KnowledgeValid)
                    {
                        _lostKnowledgeTimer += period;
                        torque = RateValid ? -Vec3d.Scale(kd, RateEstimate) : Vec3d.Zero;
                        Status = "attitude unknown: holding rates";
                        if (_lostKnowledgeTimer > 30)
                        {
                            _sim.Events.Add(env.Met, EventSeverity.Alarm, "ADCS", "Attitude knowledge lost for 30 s: autonomous switch to SUNSAFE");
                            Mode = AdcsMode.SunSafe;
                            _lostKnowledgeTimer = 0;
                        }
                        break;
                    }
                    _lostKnowledgeTimer = 0;
                    torque = PointingTorque(kp, kd);
                    break;
            }

            // Momentum management
            Dumping = false;
            if (DumpEnabled && Wheels != null && Wheels.Wheels.Count > 0)
            {
                double frac = Wheels.NetMomentumFraction;
                Vec3d h = Wheels.TotalMomentum;
                if (_forceDump && frac <= _s.dumpStop) _forceDump = false;
                bool want = frac > _s.dumpStart || ((_wasDumping || _forceDump) && frac > _s.dumpStop);
                if (want)
                {
                    if (Torquers != null && Torquers.Working && mag != null && field.SqrLength > 1e-18)
                    {
                        // m = k (h x B)/|B|^2 gives torque -k h_perpendicular
                        double k = 1.0 / 300.0;
                        Vec3d m = Vec3d.Cross(h, field) * (k / field.SqrLength);
                        if (Mode == AdcsMode.Detumble) m = Vec3d.Zero; // B-dot owns the rods while detumbling
                        dipole += m;
                        Dumping = m.SqrLength > 0;
                    }
                    else if (DumpThrusters != null && DumpThrusters.CanDump)
                    {
                        DumpThrusters.RequestDumpTorque(-h / 200.0);
                        Dumping = true;
                    }
                }
                _wasDumping = Dumping;
            }

            if (Torquers != null)
                Torquers.DipoleCommand = dipole;
            _wheelTorqueCommand = wheelsOk ? torque : Vec3d.Zero;

            // Payload pointing for service / imaging
            double nadirError = env.AngleToNadirDeg(NadirAxis);
            PayloadPointingOk = nadirError <= _s.payloadToleranceDeg;
        }

        private bool _wasDumping;
        private bool _forceDump;

        private Vec3d SunSafeTorque(Vec3d kp, Vec3d kd)
        {
            SpaceEnvironment env = _sim.Env;
            if (!(Wheels != null && Wheels.Working && Wheels.UsableCount > 0))
            {
                Status = "no working wheels: cannot point";
                return Vec3d.Zero;
            }
            SensorComponent sun = Find(SensorType.SunSensor, true);
            Vec3d axis = SunAxis.SqrLength > 0 ? SunAxis : (_sim.Solar != null ? _sim.Solar.BestSunAxis() : Vec3d.UnitZ);
            PointingErrorDeg = env.AngleToSunDeg(axis);
            Vec3d rate = RateValid ? RateEstimate : Vec3d.Zero;

            if (sun == null)
            {
                if (!env.InSun)
                {
                    Status = "eclipse: holding rates";
                    return RateValid ? -Vec3d.Scale(kd, rate) : Vec3d.Zero;
                }
                // Sun search: turn slowly about an axis perpendicular to the array axis until a sensor sees the Sun
                Vec3d searchAxis = Vec3d.Cross(axis, SecondaryAxis);
                if (searchAxis.SqrLength < 1e-9) searchAxis = axis.AnyPerpendicular();
                Vec3d searchRate = searchAxis.Normalized * (Math.Min(0.5, Math.Max(0.1, _s.maxSlewDps)) * OpsMath.Deg2Rad);
                Status = "searching for the Sun";
                return RateValid ? -Vec3d.Scale(kd, rate - searchRate) : Vec3d.Zero;
            }
            Vec3d s = sun.MeasureSun();
            Vec3d err = Vec3d.Cross(axis, s);          // rotation axis that brings 'axis' onto the Sun
            double angle = Math.Acos(OpsMath.Clamp(Vec3d.Dot(axis, s), -1, 1));
            Vec3d errVec = err.SqrLength > 1e-12 ? err.Normalized * angle : (angle > 1 ? axis.AnyPerpendicular() * angle : Vec3d.Zero);
            errVec = LimitForSlew(errVec);
            Status = "pointing arrays at the Sun";
            return Vec3d.Scale(kp, errVec) - Vec3d.Scale(kd, rate) + Vec3d.Cross(rate, Vec3d.Scale(_sim.Env.Inertia, rate) + Wheels.TotalMomentum);
        }

        private Vec3d PointingTorque(Vec3d kp, Vec3d kd)
        {
            SpaceEnvironment env = _sim.Env;
            QuatD target;
            Vec3d targetRateInertial = Vec3d.Zero;
            if (Mode == AdcsMode.Nadir)
            {
                Vec3d secondary = SecondaryIsOrbitNormal ? env.OrbitNormalEci : env.Velocity.Normalized;
                target = QuatD.FromTwoVectors(NadirAxis, SecondaryAxis, env.NadirEci, secondary);
                targetRateInertial = env.OrbitNormalEci * env.OrbitRate;
                PointingErrorDeg = env.AngleToNadirDeg(NadirAxis);
                Status = "payload pointing at the Earth";
            }
            else
            {
                target = _inertialTarget;
                QuatD diff = target.Conjugate * env.Attitude;
                PointingErrorDeg = diff.ToRotationVector().Length * OpsMath.Rad2Deg;
                Status = "holding inertial attitude";
            }

            QuatD estimate = EstimatedAttitude();
            // Error rotation from target to estimate, expressed in the body frame
            QuatD qe = target.Conjugate * estimate;
            Vec3d errBody = -estimate.InverseRotate(target.Rotate(qe.ToRotationVector()));
            errBody = LimitForSlew(errBody);
            Vec3d rate = RateValid ? RateEstimate : Vec3d.Zero;
            Vec3d desiredRate = estimate.InverseRotate(targetRateInertial);
            return Vec3d.Scale(kp, errBody) - Vec3d.Scale(kd, rate - desiredRate)
                   + Vec3d.Cross(rate, Vec3d.Scale(env.Inertia, rate) + Wheels.TotalMomentum);
        }

        private Vec3d LimitForSlew(Vec3d errVec)
        {
            double wn = Math.Max(0.005, _s.naturalFrequency);
            double maxErr = 2 * Math.Max(0.3, _s.damping) * Math.Max(0.05, _s.maxSlewDps) * OpsMath.Deg2Rad / wn;
            double len = errVec.Length;
            return len > maxErr ? errVec * (maxErr / len) : errVec;
        }

        // ------------------------------------------------------------------ determination

        private void Determine()
        {
            SpaceEnvironment env = _sim.Env;
            SensorComponent st = Find(SensorType.StarTracker, true);
            SensorComponent es = Find(SensorType.EarthSensor, true);
            SensorComponent ss = Find(SensorType.SunSensor, true);
            SensorComponent mg = Find(SensorType.Magnetometer, true);
            SensorComponent gy = Find(SensorType.Gyro, true);

            _yawErrorDeg = 0;
            if (st != null)
            {
                KnowledgeValid = true; KnowledgeErrorDeg = 0.01 + Math.Abs(st.BiasDeg); KnowledgeSource = "star tracker";
            }
            else if (es != null && ss != null)
            {
                KnowledgeValid = true; KnowledgeErrorDeg = 0.05 + Math.Abs(es.BiasDeg) + Math.Abs(ss.BiasDeg); KnowledgeSource = "Earth + Sun sensors";
                _yawErrorDeg = 0.1;
            }
            else if (mg != null && ss != null)
            {
                KnowledgeValid = true; KnowledgeErrorDeg = 3 + Math.Abs(mg.BiasDeg) + Math.Abs(ss.BiasDeg); KnowledgeSource = "magnetometer + Sun sensor";
            }
            else if (es != null && Mode == AdcsMode.Nadir)
            {
                // An Earth sensor measures roll and pitch well; yaw (rotation about the Earth line) only coarsely
                KnowledgeValid = true; KnowledgeErrorDeg = 0.05 + Math.Abs(es.BiasDeg); KnowledgeSource = "Earth sensor + gyro yaw";
                _yawErrorDeg = 1.0;
            }
            else if (gy != null && _lastAbsoluteError < 90 && _propagationSeconds < 3600)
            {
                // No absolute sensor (e.g. eclipse): propagate the last fix with the gyros; the error grows
                _propagationSeconds += Math.Max(0.05, _s.controlPeriod);
                double drift = 0.05 + Math.Abs(gy.BiasDeg) * 60; // deg per minute
                KnowledgeErrorDeg = _lastAbsoluteError + drift * _propagationSeconds / 60.0;
                KnowledgeValid = KnowledgeErrorDeg < 10;
                KnowledgeSource = "gyro propagation";
            }
            else
            {
                KnowledgeValid = false; KnowledgeErrorDeg = 180; KnowledgeSource = "none";
            }
            if (KnowledgeSource != "gyro propagation")
            {
                _propagationSeconds = 0;
                _lastAbsoluteError = KnowledgeValid ? KnowledgeErrorDeg : 180;
            }

            // Slowly wander the error direction so the error looks like real noise + bias
            if (env.Uniform() < 0.05)
                _knowledgeErrorAxis = new Vec3d(env.Gaussian(), env.Gaussian(), env.Gaussian()).Normalized;

            if (gy != null)
            {
                RateValid = true;
                RateEstimate = gy.MeasureRate();
            }
            else if (st != null)
            {
                RateValid = true;
                RateEstimate = env.BodyRate + new Vec3d(env.Gaussian(), env.Gaussian(), env.Gaussian()) * (0.02 * OpsMath.Deg2Rad);
            }
            else
            {
                RateValid = false;
                RateEstimate = Vec3d.Zero;
            }
        }

        private QuatD EstimatedAttitude()
        {
            if (!KnowledgeValid)
                return _sim.Env.Attitude;
            QuatD err = QuatD.FromAxisAngle(_knowledgeErrorAxis, KnowledgeErrorDeg * OpsMath.Deg2Rad);
            if (_yawErrorDeg > 0)
                err = err * QuatD.FromAxisAngle(NadirAxis, _yawErrorDeg * OpsMath.Deg2Rad);
            return (_sim.Env.Attitude * err).Normalized;
        }

        // ------------------------------------------------------------------ telemetry / commands

        public void WriteTelemetry(TelemetryStore tm)
        {
            const string g = "Attitude";
            tm.SetText("adcs.mode", g, "ADCS mode", Mode.ToString().ToUpperInvariant(), (int)Mode);
            tm.SetText("adcs.status", g, "Controller", Status, 0);
            tm.Set("adcs.rate", g, "Body rotation rate", _sim.Env.RateDegPerSec, "deg/s", 3, double.NaN, 2);
            tm.Set("adcs.pointing_error", g, "Pointing error", PointingErrorDeg, "deg", 2);
            tm.Set("adcs.nadir_error", g, "Payload off-nadir", _sim.Env.AngleToNadirDeg(NadirAxis), "deg", 2);
            tm.SetText("adcs.knowledge", g, "Attitude knowledge", KnowledgeValid ? KnowledgeSource + " (" + KnowledgeErrorDeg.ToString("0.##") + " deg" + (_yawErrorDeg > 0 ? ", yaw " + _yawErrorDeg.ToString("0.#") : "") + ")" : "NONE", KnowledgeValid ? 1 : 0);
            tm.SetText("adcs.dump", g, "Momentum dumping", Dumping ? "DUMPING" : (DumpEnabled ? "AUTO" : "DISABLED"), Dumping ? 1 : 0);
            tm.SetText("adcs.payload_pointing", g, "Payload on target", PayloadPointingOk ? "YES" : "NO", PayloadPointingOk ? 1 : 0);
        }

        public void GetCommands(List<CommandSpec> list)
        {
            list.Add(new CommandSpec("MODE", "PASSIVE|DETUMBLE|SUNSAFE|NADIR|INERTIAL", "Attitude control mode"));
            list.Add(new CommandSpec("DUMP", "ON|OFF|NOW", "Momentum dumping: automatic on/off, or dump now"));
        }

        public string Execute(string command, string[] args)
        {
            if (command == "MODE")
            {
                string m = args != null && args.Length > 0 ? args[0].ToUpperInvariant() : "";
                switch (m)
                {
                    case "PASSIVE": return SetMode(AdcsMode.Passive);
                    case "DETUMBLE": return SetMode(AdcsMode.Detumble);
                    case "SUNSAFE": return SetMode(AdcsMode.SunSafe);
                    case "NADIR": return SetMode(AdcsMode.Nadir);
                    case "INERTIAL": return SetMode(AdcsMode.Inertial);
                    default: return "Unknown mode '" + m + "'. Use PASSIVE, DETUMBLE, SUNSAFE, NADIR or INERTIAL.";
                }
            }
            if (command == "DUMP")
            {
                string a = args != null && args.Length > 0 ? args[0].ToUpperInvariant() : "ON";
                if (a == "NOW")
                {
                    DumpEnabled = true;
                    _forceDump = true;
                    return "Momentum dump started";
                }
                DumpEnabled = a != "OFF";
                return "Momentum dumping " + (DumpEnabled ? "enabled (automatic)" : "disabled");
            }
            return null;
        }
    }
}
