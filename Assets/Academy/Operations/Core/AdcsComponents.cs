using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    public sealed class Wheel
    {
        public Vec3d Axis;          // body frame, unit
        public double H;            // N m s along Axis
        public double HDotCommand;  // N m commanded
        public double HDot;         // N m actual
        public bool Failed;         // instructor: bearing seized / electronics dead
        public double ExtraFriction;// instructor: N m
        public bool Excluded;       // operator removed it from control
        public bool SaturationLogged;
    }

    /// <summary>Reaction wheels: the controller asks for a body torque, the wheels spin up the opposite way.</summary>
    public sealed class ReactionWheelSet : SimComponent
    {
        public readonly List<Wheel> Wheels = new List<Wheel>();
        public double HMax = 0.0015;       // N m s
        public double TorqueMax = 0.002;   // N m
        public double MaxRpm = 6000;
        public double SpinDownTime = 120;  // s, unpowered wheel coasting to rest

        public Vec3d TotalMomentum
        {
            get
            {
                Vec3d h = Vec3d.Zero;
                foreach (Wheel w in Wheels) h += w.Axis * w.H;
                return h;
            }
        }

        /// <summary>Sum of the wheels' own torque (h-dot) this step, body frame.</summary>
        public Vec3d TorqueOnWheels
        {
            get
            {
                Vec3d t = Vec3d.Zero;
                foreach (Wheel w in Wheels) t += w.Axis * w.HDot;
                return t;
            }
        }

        /// <summary>Net momentum stored in the wheels as a fraction of one wheel's capacity.</summary>
        public double NetMomentumFraction { get { return TotalMomentum.Length / Math.Max(1e-12, HMax); } }

        /// <summary>Spin the wheels up against each other (net momentum zero) like a momentum-biased bus.</summary>
        public void ApplyBias(double fraction)
        {
            if (Wheels.Count < 4 || fraction <= 0) return;
            for (int i = 0; i < Wheels.Count; i++)
                Wheels[i].H = (i % 2 == 0 ? 1 : -1) * fraction * HMax;
            if (TotalMomentum.Length > 0.05 * HMax)       // not a symmetric pyramid: no bias
                foreach (Wheel w in Wheels) w.H = 0;
        }

        public double MaxMomentumFraction
        {
            get
            {
                double m = 0;
                foreach (Wheel w in Wheels) m = Math.Max(m, Math.Abs(w.H) / HMax);
                return m;
            }
        }

        public int UsableCount
        {
            get
            {
                int n = 0;
                foreach (Wheel w in Wheels) if (!w.Excluded) n++;
                return n;
            }
        }

        /// <summary>
        /// Distribute a desired body torque over the wheels the software believes are usable
        /// (minimum-norm pseudo-inverse). The wheels' reaction on the body is -h-dot.
        /// </summary>
        public void CommandBodyTorque(Vec3d bodyTorque)
        {
            var usable = new List<Wheel>();
            foreach (Wheel w in Wheels)
            {
                w.HDotCommand = 0;
                if (!w.Excluded) usable.Add(w);
            }
            if (usable.Count == 0 || !Working)
                return;

            Vec3d hdotBody = -bodyTorque;
            // M = A A^T (3x3)
            double[,] m = new double[3, 3];
            foreach (Wheel w in usable)
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        m[r, c] += w.Axis[r] * w.Axis[c];

            double[,] inv;
            if (Invert3(m, out inv))
            {
                Vec3d y = new Vec3d(
                    inv[0, 0] * hdotBody.x + inv[0, 1] * hdotBody.y + inv[0, 2] * hdotBody.z,
                    inv[1, 0] * hdotBody.x + inv[1, 1] * hdotBody.y + inv[1, 2] * hdotBody.z,
                    inv[2, 0] * hdotBody.x + inv[2, 1] * hdotBody.y + inv[2, 2] * hdotBody.z);
                foreach (Wheel w in usable)
                    w.HDotCommand = Vec3d.Dot(w.Axis, y);
            }
            else
            {
                // Fewer than three independent wheels: project (the missing axis is uncontrolled)
                foreach (Wheel w in usable)
                    w.HDotCommand = Vec3d.Dot(w.Axis, hdotBody);
            }

            // Scale the whole command down if any wheel would exceed its torque limit
            double worst = 0;
            foreach (Wheel w in usable) worst = Math.Max(worst, Math.Abs(w.HDotCommand) / TorqueMax);
            if (worst > 1)
                foreach (Wheel w in usable) w.HDotCommand /= worst;
        }

        private static bool Invert3(double[,] a, out double[,] inv)
        {
            inv = new double[3, 3];
            double det = a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
                       - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
                       + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
            if (Math.Abs(det) < 1e-9)
                return false;
            double k = 1.0 / det;
            inv[0, 0] = (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) * k;
            inv[0, 1] = (a[0, 2] * a[2, 1] - a[0, 1] * a[2, 2]) * k;
            inv[0, 2] = (a[0, 1] * a[1, 2] - a[0, 2] * a[1, 1]) * k;
            inv[1, 0] = (a[1, 2] * a[2, 0] - a[1, 0] * a[2, 2]) * k;
            inv[1, 1] = (a[0, 0] * a[2, 2] - a[0, 2] * a[2, 0]) * k;
            inv[1, 2] = (a[0, 2] * a[1, 0] - a[0, 0] * a[1, 2]) * k;
            inv[2, 0] = (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]) * k;
            inv[2, 1] = (a[0, 1] * a[2, 0] - a[0, 0] * a[2, 1]) * k;
            inv[2, 2] = (a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0]) * k;
            return true;
        }

        public override void Step(double dt)
        {
            bool powered = Working;
            for (int i = 0; i < Wheels.Count; i++)
            {
                Wheel w = Wheels[i];
                double hdot;
                if (!powered || w.Failed)
                {
                    // Unpowered: friction slowly hands the wheel's momentum back to the body.
                    // Failed (seized bearing): it stops within ~20 s and the body takes the kick.
                    hdot = w.Failed ? -w.H / 20.0 : -w.H / Math.Max(1, SpinDownTime);
                }
                else
                {
                    hdot = OpsMath.Clamp(w.HDotCommand, -TorqueMax, TorqueMax);
                    hdot -= Math.Sign(w.H) * w.ExtraFriction;
                    bool saturated = (w.H >= HMax && hdot > 0) || (w.H <= -HMax && hdot < 0);
                    if (saturated)
                    {
                        hdot = 0;
                        if (!w.SaturationLogged)
                        {
                            w.SaturationLogged = true;
                            Log(EventSeverity.Warning, "Wheel " + (i + 1) + " saturated: it cannot spin faster. Dump momentum.");
                        }
                    }
                    else if (Math.Abs(w.H) < 0.8 * HMax)
                    {
                        w.SaturationLogged = false;
                    }
                }
                w.HDot = hdot;
                w.H = OpsMath.Clamp(w.H + hdot * dt, -HMax * 1.0001, HMax * 1.0001);
            }
        }

        public double Rpm(Wheel w) { return w.H / HMax * MaxRpm; }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            for (int i = 0; i < Wheels.Count; i++)
            {
                Wheel w = Wheels[i];
                string tag = w.Failed ? " (FAILED)" : (w.Excluded ? " (disabled)" : "");
                tm.Set(Id + ".rpm" + (i + 1), Subsystem, "Wheel " + (i + 1) + " speed" + tag, Rpm(w), "rpm", 0, -0.9 * MaxRpm, 0.9 * MaxRpm);
            }
            tm.Set(Id + ".momentum", Subsystem, "Highest wheel momentum", MaxMomentumFraction * 100, "%", 0, double.NaN, 90);
            tm.Set(Id + ".net", Subsystem, "Stored (net) momentum", NetMomentumFraction * 100, "%", 0, double.NaN, 70);
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            base.GetCommands(list);
            list.Add(new CommandSpec("DISABLE", "<wheel 1-" + Wheels.Count + ">", "Remove a wheel from attitude control"));
            list.Add(new CommandSpec("ENABLE", "<wheel 1-" + Wheels.Count + ">", "Return a wheel to attitude control"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "DISABLE" || command == "ENABLE")
            {
                int n = ArgInt(args, 0, 0);
                if (n < 1 || n > Wheels.Count)
                    return "Wheel number must be 1-" + Wheels.Count;
                Wheels[n - 1].Excluded = command == "DISABLE";
                return "Wheel " + n + (command == "DISABLE" ? " removed from" : " returned to") + " attitude control (" + UsableCount + " in use)";
            }
            return base.Execute(command, args);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("WHEEL_FAIL", "1", "Wheel number stops responding and spins down"));
            list.Add(new FaultSpec("FRICTION", "1", "Wheel number gets bearing friction"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            int n = double.IsNaN(value) ? 1 : (int)value;
            if (fault == "WHEEL_FAIL" || fault == "FRICTION")
            {
                if (n < 1 || n > Wheels.Count) return "No wheel " + n;
                if (fault == "WHEEL_FAIL") Wheels[n - 1].Failed = true;
                else Wheels[n - 1].ExtraFriction = TorqueMax * 0.3;
                return "Wheel " + n + (fault == "WHEEL_FAIL" ? " failed" : " has high friction");
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults()
        {
            base.ClearFaults();
            foreach (Wheel w in Wheels) { w.Failed = false; w.ExtraFriction = 0; }
        }
    }

    /// <summary>Magnetorquer rods: an electromagnet that pushes against Earth's magnetic field (torque = m x B).</summary>
    public sealed class MagnetorquerSet : SimComponent
    {
        public readonly List<Vec3d> Axes = new List<Vec3d>();
        public readonly List<bool> RodFailed = new List<bool>();
        public double MaxDipole = 0.2;  // A m^2
        public Vec3d DipoleCommand;     // body, A m^2
        public Vec3d Dipole { get; private set; }

        public override double PowerDraw
        {
            get
            {
                if (!On || Offline) return 0;
                double duty = MaxDipole > 0 ? Math.Min(1, Dipole.Length / (MaxDipole * Math.Sqrt(Math.Max(1, Axes.Count)))) : 0;
                return PowerW * (0.15 + 0.85 * duty);
            }
        }

        public Vec3d Torque(Vec3d bBody)
        {
            Vec3d m = Vec3d.Zero;
            if (Working)
                for (int i = 0; i < Axes.Count; i++)
                {
                    if (RodFailed[i]) continue;
                    double c = OpsMath.Clamp(Vec3d.Dot(DipoleCommand, Axes[i]), -MaxDipole, MaxDipole);
                    m += Axes[i] * c;
                }
            Dipole = m;
            return Vec3d.Cross(m, bBody);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.Set(Id + ".dipole", Subsystem, "Magnetic dipole", Dipole.Length, "A m2", 3);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("ROD_FAIL", "1", "Rod number stops working"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "ROD_FAIL")
            {
                int n = double.IsNaN(value) ? 1 : (int)value;
                if (n < 1 || n > RodFailed.Count) return "No rod " + n;
                RodFailed[n - 1] = true;
                return "Magnetorquer rod " + n + " failed";
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults()
        {
            base.ClearFaults();
            for (int i = 0; i < RodFailed.Count; i++) RodFailed[i] = false;
        }
    }

    /// <summary>Propellant tank. Several thruster sets may draw from it.</summary>
    public sealed class TankComponent : SimComponent
    {
        public double PropellantKg = 1;
        public double InitialKg = 1;
        public double PressureStartBar = 22;
        public double LeakKgPerS;

        public TankComponent() { Switchable = false; }

        public double Draw(double kg)
        {
            double taken = Math.Min(kg, PropellantKg);
            PropellantKg -= taken;
            return taken;
        }

        public override void Step(double dt)
        {
            if (LeakKgPerS > 0)
                PropellantKg = Math.Max(0, PropellantKg - LeakKgPerS * dt);
        }

        public double PressureBar
        {
            get { return InitialKg > 0 ? PressureStartBar * (0.35 + 0.65 * PropellantKg / InitialKg) : 0; }
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.Set(Id + ".propellant", Subsystem, "Propellant", PropellantKg, "kg", PropellantKg < 10 ? 3 : 1, InitialKg * 0.1, double.NaN);
            tm.Set(Id + ".pressure", Subsystem, "Tank pressure", PressureBar, "bar", 1);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("LEAK", "0.001", "Propellant leak (kg/s)"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "LEAK")
            {
                LeakKgPerS = double.IsNaN(value) ? 0.001 : Math.Max(0, value);
                return "Tank leaking " + LeakKgPerS + " kg/s";
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults() { base.ClearFaults(); LeakKgPerS = 0; }
    }

    public sealed class Thruster
    {
        public Vec3d Position;   // body, m (relative to the centre of mass)
        public Vec3d ForceDir;   // body, unit (opposite to the exhaust)
        public bool StuckOpen;   // instructor fault
        public double PulseLeft; // s of manual firing left
    }

    /// <summary>Thrusters: manual pulses, orbit manoeuvres (BURN) and momentum dumping for wheel-only satellites.</summary>
    public sealed class ThrusterSet : SimComponent
    {
        public readonly List<Thruster> Thrusters = new List<Thruster>();
        public double ThrustN = 1;
        public double IspS = 220;
        public TankComponent Tank;
        public double DeltaVUsed;   // m/s

        private Vec3d _burnDir;     // ECI unit
        private double _burnDvLeft; // m/s
        private string _burnName;
        private Vec3d _dumpTorque;  // body torque requested for momentum dumping

        public Vec3d Torque { get; private set; }
        public bool Firing { get; private set; }

        public bool HasPropellant { get { return Tank != null && Tank.PropellantKg > 1e-6; } }

        /// <summary>Typical lever arm (m) used for momentum-dumping torque.</summary>
        public double LeverArm
        {
            get
            {
                double sum = 0;
                foreach (Thruster t in Thrusters) sum += Vec3d.Cross(t.Position, t.ForceDir).Length;
                return Thrusters.Count > 0 ? Math.Max(0.05, sum / Thrusters.Count) : 0.5;
            }
        }

        public bool CanDump { get { return Working && HasPropellant && Thrusters.Count > 0; } }

        public void RequestDumpTorque(Vec3d bodyTorque) { _dumpTorque = bodyTorque; }

        public override void Step(double dt)
        {
            Vec3d torque = Vec3d.Zero;
            Vec3d forceBody = Vec3d.Zero;
            double mdot = ThrustN / (IspS * OpsMath.G0);
            double used = 0;
            Firing = false;

            // Stuck-open thrusters fire whenever there is propellant and the latch valve (ON) is open
            foreach (Thruster t in Thrusters)
            {
                bool fire = t.StuckOpen && On && HasPropellant;
                if (!fire && t.PulseLeft > 0 && Working && HasPropellant)
                {
                    fire = true;
                    t.PulseLeft -= dt;
                }
                if (!fire) continue;
                Firing = true;
                Vec3d f = t.ForceDir * ThrustN;
                forceBody += f;
                torque += Vec3d.Cross(t.Position, f);
                used += mdot * dt;
            }

            // Momentum dumping: produce the requested torque with pulse modulation
            if (_dumpTorque.SqrLength > 0 && CanDump)
            {
                double maxTorque = ThrustN * LeverArm;
                Vec3d dump = _dumpTorque;
                if (dump.Length > maxTorque) dump = dump.Normalized * maxTorque;
                torque += dump;
                used += dump.Length / LeverArm * dt / (IspS * OpsMath.G0) * 2; // couples use two thrusters
                Firing = true;
            }
            _dumpTorque = Vec3d.Zero;

            // Orbit manoeuvre: the attitude system is assumed to point the thrusters; the velocity change is
            // applied along the commanded direction with the total thrust of the set.
            if (_burnDvLeft > 0 && Working && HasPropellant)
            {
                double mass = Math.Max(1, Sim.MassKg);
                double accel = ThrustN * Math.Max(1, Thrusters.Count) / mass;  // m/s^2
                double dv = Math.Min(_burnDvLeft, accel * dt);
                _burnDvLeft -= dv;
                DeltaVUsed += dv;
                Sim.Env.ApplyDeltaV(_burnDir * dv);
                used += mass * (1 - Math.Exp(-dv / (IspS * OpsMath.G0)));
                Firing = true;
                if (_burnDvLeft <= 1e-9)
                {
                    _burnDvLeft = 0;
                    Log(EventSeverity.Info, "Burn " + _burnName + " complete");
                }
            }

            // Stuck thrust also changes the orbit slightly
            if (forceBody.SqrLength > 0)
            {
                Vec3d accelEci = Sim.Env.Attitude.Rotate(forceBody) / Math.Max(1, Sim.MassKg);
                Sim.Env.ApplyDeltaV(accelEci * dt);
            }

            if (used > 0 && Tank != null)
            {
                Tank.Draw(used);
                if (!HasPropellant)
                    Log(EventSeverity.Alarm, "Propellant exhausted");
            }
            Torque = torque;
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            base.GetCommands(list);
            list.Add(new CommandSpec("FIRE", "<thruster 1-" + Thrusters.Count + "> <seconds>", "Fire one thruster for a few seconds"));
            list.Add(new CommandSpec("BURN", "PROGRADE|RETROGRADE|NORMAL|ANTINORMAL|RADIAL_OUT|RADIAL_IN <m/s>", "Orbit manoeuvre: change velocity"));
            list.Add(new CommandSpec("ABORT", "", "Stop a burn"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "FIRE")
            {
                int n = ArgInt(args, 0, 0);
                double s = ArgDouble(args, 1, 1);
                if (n < 1 || n > Thrusters.Count) return "Thruster number must be 1-" + Thrusters.Count;
                if (!Working) return "Thrusters are off (latch valve closed)";
                Thrusters[n - 1].PulseLeft = OpsMath.Clamp(s, 0, 600);
                return "Firing thruster " + n + " for " + s + " s";
            }
            if (command == "BURN")
            {
                string dir = ArgText(args, 0, "PROGRADE");
                double dv = ArgDouble(args, 1, 0.1);
                Vec3d v = Sim.Env.Velocity.Normalized, r = Sim.Env.Position.Normalized, h = Sim.Env.OrbitNormalEci;
                Vec3d d;
                switch (dir)
                {
                    case "PROGRADE": d = v; break;
                    case "RETROGRADE": d = -v; break;
                    case "NORMAL": d = h; break;
                    case "ANTINORMAL": d = -h; break;
                    case "RADIAL_OUT": d = r; break;
                    case "RADIAL_IN": d = -r; break;
                    default: return "Direction must be PROGRADE, RETROGRADE, NORMAL, ANTINORMAL, RADIAL_OUT or RADIAL_IN";
                }
                if (!Working) return "Thrusters are off (latch valve closed)";
                if (!HasPropellant) return "No propellant";
                _burnDir = d;
                _burnDvLeft = OpsMath.Clamp(Math.Abs(dv), 0, 500);
                _burnName = dir + " " + _burnDvLeft.ToString("0.###") + " m/s";
                return "Burn started: " + _burnName;
            }
            if (command == "ABORT")
            {
                _burnDvLeft = 0;
                foreach (Thruster t in Thrusters) t.PulseLeft = 0;
                return "Burn aborted";
            }
            return base.Execute(command, args);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.SetText(Id + ".firing", Subsystem, "Thrusters", Firing ? "FIRING" : "IDLE", Firing ? 1 : 0);
            tm.Set(Id + ".dv", Subsystem, "Delta-v used", DeltaVUsed, "m/s", 3);
            if (_burnDvLeft > 0)
                tm.Set(Id + ".burn_left", Subsystem, "Burn remaining", _burnDvLeft, "m/s", 3);
            else
                tm.Set(Id + ".burn_left", Subsystem, "Burn remaining", 0, "m/s", 3);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("STUCK_OPEN", "1", "Thruster number valve stuck open: fires until the latch valve is closed (OFF)"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "STUCK_OPEN")
            {
                int n = double.IsNaN(value) ? 1 : (int)value;
                if (n < 1 || n > Thrusters.Count) return "No thruster " + n;
                Thrusters[n - 1].StuckOpen = true;
                return "Thruster " + n + " valve stuck open";
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults()
        {
            base.ClearFaults();
            foreach (Thruster t in Thrusters) t.StuckOpen = false;
        }
    }

    public enum SensorType { Magnetometer, SunSensor, EarthSensor, StarTracker, Gyro, Gnss }

    /// <summary>Attitude / navigation sensor: truth from the environment plus noise, bias and faults.</summary>
    public sealed class SensorComponent : SimComponent
    {
        public SensorType Type;
        public readonly List<Vec3d> Boresights = new List<Vec3d>();  // body, unit
        public double NoiseDeg = 0.5;     // attitude sensors; gyro: deg/s
        public double BiasDeg;            // instructor fault
        public bool Stuck;
        public string InvalidReason = "";

        private Vec3d _lastSun = Vec3d.UnitX, _lastMag = Vec3d.UnitX, _lastRate;
        private bool _hasLast;

        /// <summary>Can the sensor give a measurement right now?</summary>
        public bool Valid
        {
            get
            {
                InvalidReason = "";
                if (!Working) { InvalidReason = Offline ? "failed" : "off"; return false; }
                SpaceEnvironment env = Sim.Env;
                switch (Type)
                {
                    case SensorType.SunSensor:
                        if (!env.InSun) { InvalidReason = "eclipse"; return false; }
                        // No boresights = coarse sensors on every face: the whole sky is covered
                        if (Boresights.Count > 0)
                        {
                            Vec3d s = env.SunBody;
                            bool any = false;
                            foreach (Vec3d b in Boresights) if (Vec3d.AngleDeg(b, s) < 70) any = true;
                            if (!any) { InvalidReason = "Sun outside field of view"; return false; }
                        }
                        return true;
                    case SensorType.EarthSensor:
                        foreach (Vec3d b in Boresights)
                            if (env.AngleToNadirDeg(b) < 25) return true;
                        InvalidReason = "Earth outside field of view";
                        return false;
                    case SensorType.StarTracker:
                        if (env.RateDegPerSec > 1.5) { InvalidReason = "rotating too fast"; return false; }
                        double earthRadiusDeg = Math.Asin(Math.Min(1, OpsMath.EarthRadius / env.Position.Length)) * OpsMath.Rad2Deg;
                        foreach (Vec3d b in Boresights)
                            if (env.AngleToSunDeg(b) > 35 && env.AngleToNadirDeg(b) > earthRadiusDeg + 15) return true;
                        InvalidReason = "blinded by the Sun or Earth";
                        return false;
                    default:
                        return true;
                }
            }
        }

        private Vec3d Perturb(Vec3d v, double deg)
        {
            if (deg <= 0) return v;
            Vec3d axis = new Vec3d(Sim.Env.Gaussian(), Sim.Env.Gaussian(), Sim.Env.Gaussian());
            return QuatD.FromAxisAngle(axis, deg * OpsMath.Deg2Rad).Rotate(v);
        }

        private Vec3d Biased(Vec3d v)
        {
            if (BiasDeg == 0) return v;
            Vec3d axis = Vec3d.Cross(v, Vec3d.UnitX).SqrLength > 1e-6 ? Vec3d.Cross(v, Vec3d.UnitX) : Vec3d.UnitY;
            return QuatD.FromAxisAngle(axis, BiasDeg * OpsMath.Deg2Rad).Rotate(v);
        }

        public Vec3d MeasureSun()
        {
            if (Stuck && _hasLast) return _lastSun;
            _lastSun = Biased(Perturb(Sim.Env.SunBody.Normalized, NoiseDeg));
            _hasLast = true;
            return _lastSun;
        }

        public Vec3d MeasureField()
        {
            if (Stuck && _hasLast) return _lastMag;
            Vec3d b = Sim.Env.MagneticFieldBody;
            _lastMag = Biased(Perturb(b, NoiseDeg)) * (1 + 0.01 * Sim.Env.Gaussian());
            _hasLast = true;
            return _lastMag;
        }

        public Vec3d MeasureRate()
        {
            if (Stuck && _hasLast) return _lastRate;
            double n = NoiseDeg * OpsMath.Deg2Rad;
            Vec3d noise = new Vec3d(Sim.Env.Gaussian(), Sim.Env.Gaussian(), Sim.Env.Gaussian()) * n;
            _lastRate = Sim.Env.BodyRate + noise + new Vec3d(1, 1, 1).Normalized * (BiasDeg * OpsMath.Deg2Rad);
            _hasLast = true;
            return _lastRate;
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            bool valid = Valid;
            tm.SetText(Id + ".valid", Subsystem, Name, Offline ? "FAILED" : (!On ? "OFF" : (valid ? "VALID" : "NO DATA: " + InvalidReason)), valid ? 1 : 0);
            if (Type == SensorType.Gnss && valid)
            {
                tm.Set(Id + ".lat", Subsystem, "GNSS latitude", Sim.Env.LatitudeDeg, "deg", 2);
                tm.Set(Id + ".lon", Subsystem, "GNSS longitude", Sim.Env.LongitudeDeg, "deg", 2);
                tm.Set(Id + ".alt", Subsystem, "GNSS altitude", Sim.Env.AltitudeKm, "km", 1);
            }
            if (Type == SensorType.Magnetometer && valid)
                tm.Set(Id + ".field", Subsystem, "Magnetic field strength", Sim.Env.MagneticFieldEci.Length * 1e9, "nT", 0);
            if (Type == SensorType.Gyro && valid)
                tm.Set(Id + ".rate", Subsystem, "Measured body rate", MeasureRate().Length * OpsMath.Rad2Deg, "deg/s", 3);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("BIAS", Type == SensorType.Gyro ? "0.5" : "5", Type == SensorType.Gyro ? "Constant rate error (deg/s)" : "Constant angle error (deg)"));
            list.Add(new FaultSpec("STUCK", "", "Output freezes at the last value"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "BIAS")
            {
                BiasDeg = double.IsNaN(value) ? 5 : value;
                return Name + " biased by " + BiasDeg + (Type == SensorType.Gyro ? " deg/s" : " deg");
            }
            if (fault == "STUCK") { Stuck = true; return Name + " output frozen"; }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults() { base.ClearFaults(); BiasDeg = 0; Stuck = false; }
    }
}
