using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    /// <summary>Geometry of one generated part, in the satellite's Unity local frame (metres).</summary>
    public sealed class PartGeom
    {
        public string Type;
        public string Style;
        public Vec3d Position;
        public Vec3d Forward = Vec3d.UnitZ;
        public Vec3d Up = Vec3d.UnitY;
    }

    public sealed class SliderValue
    {
        public string Id;
        public double Value;
        public double Default;
    }

    /// <summary>Neutral description of one built slot (from the Builder design), independent of Unity.</summary>
    public sealed class SlotInstance
    {
        public string SlotId;
        public string SlotName;
        public string Subsystem;
        public string VariantId;
        public string VariantName;
        public double MassKg;
        public double PowerW;
        public double PowerGenW;
        public readonly List<PartGeom> Parts = new List<PartGeom>();
        public readonly List<SliderValue> Sliders = new List<SliderValue>();

        public SliderValue Slider(string id)
        {
            foreach (SliderValue s in Sliders) if (s.Id == id) return s;
            return null;
        }
    }

    /// <summary>
    /// The whole simulated satellite: environment truth + components + power, thermal, attitude software,
    /// onboard software, ground link and instructor faults.
    /// </summary>
    public sealed class SatelliteSim
    {
        public SpaceEnvironment Env { get; private set; }
        public TemplateModels Models { get; private set; }
        public readonly List<SimComponent> Components = new List<SimComponent>();
        private readonly Dictionary<string, SimComponent> _byId = new Dictionary<string, SimComponent>(StringComparer.OrdinalIgnoreCase);

        public PowerSystem Power { get; private set; }
        public ThermalModel Thermal { get; private set; }
        public AttitudeControl Adcs { get; private set; }
        public OnboardSoftware Obsw { get; private set; }
        public GroundLink Link { get; private set; }
        public FaultInjector Faults { get; private set; }
        public readonly TelemetryStore Telemetry = new TelemetryStore();
        public readonly EventLog Events = new EventLog();

        public ComputerComponent Computer { get; private set; }
        public StorageComponent Storage { get; private set; }
        public SolarArrayComponent Solar { get; private set; }
        public RadioComponent TtcRadio { get; private set; }
        public readonly List<RadioComponent> Radios = new List<RadioComponent>();
        public readonly List<DeployComponent> Deployers = new List<DeployComponent>();
        public readonly List<ThrusterSet> ThrusterSets = new List<ThrusterSet>();
        public readonly List<TankComponent> Tanks = new List<TankComponent>();

        public double DryMassKg { get; private set; }
        public double MassKg
        {
            get
            {
                double m = DryMassKg;
                foreach (TankComponent t in Tanks) m += t.PropellantKg;
                return m;
            }
        }

        public string SatelliteName = "Satellite";
        public string OrbitClass = "LEO";
        public double PhysicsStep = 0.1;
        public int MaxSubsteps = 300;

        private double _tickTimer;

        public SatelliteSim(TemplateModels models, KeplerOrbit orbit, double startTime)
        {
            Models = models ?? new TemplateModels();
            Env = new SpaceEnvironment(orbit, startTime);
            Power = new PowerSystem(this);
            Thermal = new ThermalModel(this, Models.thermal);
            Obsw = new OnboardSoftware(this, CloneRules(Models.rules));
            Link = new GroundLink(this, Models.groundStations);
            Faults = new FaultInjector(this);
            Adcs = new AttitudeControl(this, Models.adcs);
        }

        private static List<RuleDef> CloneRules(List<RuleDef> rules)
        {
            var list = new List<RuleDef>();
            if (rules == null) return list;
            foreach (RuleDef r in rules)
                list.Add(new RuleDef { id = r.id, description = r.description, point = r.point, op = r.op, value = r.value, seconds = r.seconds, enabled = r.enabled, actions = new List<string>(r.actions ?? new List<string>()) });
            return list;
        }

        public SimComponent Find(string id)
        {
            SimComponent c;
            return id != null && _byId.TryGetValue(id, out c) ? c : null;
        }

        // ------------------------------------------------------------------ construction

        public void Add(SimComponent c)
        {
            if (c == null || string.IsNullOrEmpty(c.Id) || _byId.ContainsKey(c.Id))
                return;
            Components.Add(c);
            _byId[c.Id] = c;
            c.Attach(this);

            if (c is BatteryComponent && Power.Battery == null) Power.Battery = (BatteryComponent)c;
            if (c is SolarArrayComponent) { Power.Arrays.Add((SolarArrayComponent)c); if (Solar == null) Solar = (SolarArrayComponent)c; }
            if (c is ComputerComponent && Computer == null) Computer = (ComputerComponent)c;
            if (c is StorageComponent && Storage == null) Storage = (StorageComponent)c;
            if (c is RadioComponent)
            {
                var r = (RadioComponent)c;
                Radios.Add(r);
                if (r.IsTtc && TtcRadio == null) TtcRadio = r;
            }
            if (c is DeployComponent) Deployers.Add((DeployComponent)c);
            if (c is ThrusterSet) ThrusterSets.Add((ThrusterSet)c);
            if (c is TankComponent) Tanks.Add((TankComponent)c);
            if (c is ReactionWheelSet && Adcs.Wheels == null) Adcs.Wheels = (ReactionWheelSet)c;
            if (c is MagnetorquerSet && Adcs.Torquers == null) Adcs.Torquers = (MagnetorquerSet)c;
            if (c is SensorComponent) Adcs.Sensors.Add((SensorComponent)c);
        }

        /// <summary>Finish wiring after every component has been added.</summary>
        public void Complete(double totalMassKg)
        {
            // Thrusters without their own tank draw from the first tank
            foreach (ThrusterSet t in ThrusterSets)
                if (t.Tank == null && Tanks.Count > 0) t.Tank = Tanks[0];
            // Wheel-only satellites dump momentum with thrusters
            if (Adcs.Torquers == null)
                foreach (ThrusterSet t in ThrusterSets)
                    if (t.Tank != null && (Adcs.DumpThrusters == null || t.ThrustN < Adcs.DumpThrusters.ThrustN))
                        Adcs.DumpThrusters = t; // the smallest thrusters (reaction control), not the main engine
            // Wings follow the Sun only if a drive exists
            foreach (SimComponent c in Components)
                if (c is SadaComponent)
                    foreach (SolarArrayComponent a in Power.Arrays) a.Drive = (SadaComponent)c;

            double propellant = 0;
            foreach (TankComponent t in Tanks) propellant += t.PropellantKg;
            DryMassKg = Math.Max(0.1, totalMassKg - propellant);

            // Inertia: reference values scaled by mass
            Vec3d inertiaLocal = ParamUtil.Vec(Models.inertia, new Vec3d(0.02, 0.005, 0.02));
            double scale = Models.referenceMassKg > 0 ? totalMassKg / Models.referenceMassKg : 1;
            Env.Inertia = inertiaLocal.Swap() * Math.Max(0.05, scale);
            Env.DisturbanceBody = ParamUtil.Vec(Models.disturbance, Vec3d.Zero).Swap();
            Env.DisturbanceNoise = Models.disturbanceNoise;
            double minI = Math.Min(Env.Inertia.x, Math.Min(Env.Inertia.y, Env.Inertia.z));
            PhysicsStep = minI < 1 ? 0.1 : 0.25;

            WriteTelemetry();
            Link.Update();
        }

        /// <summary>Scale the arrays so the Builder's average generation is reproduced in sun-safe attitude.</summary>
        public void CalibrateSolar()
        {
            if (Solar == null) return;
            double a = Env.Orbit.A;
            double sunlit = 1.0 - Math.Asin(Math.Min(1, OpsMath.EarthRadius / a)) / Math.PI;
            Vec3d axis = Adcs.SunAxis.SqrLength > 0 ? Adcs.SunAxis : Solar.BestSunAxis();
            foreach (SolarArrayComponent arr in Power.Arrays)
                arr.Calibrate(arr.BuilderAverageW, axis, sunlit);
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>Advance the simulation by dt seconds of simulated time.</summary>
        public void Step(double dt)
        {
            if (dt <= 0) return;
            double h = PhysicsStep;
            int n = (int)Math.Ceiling(dt / h);
            if (n > MaxSubsteps)
            {
                n = MaxSubsteps;
                h = dt / n;
            }
            else
            {
                h = dt / Math.Max(1, n);
            }
            double tickPeriod = Math.Max(1.0, dt / 20.0);

            for (int i = 0; i < n; i++)
            {
                Env.AdvanceTime(h);
                Adcs.Step(h);
                foreach (SimComponent c in Components)
                    c.Step(h);

                Vec3d external = Vec3d.Zero;
                if (Adcs.Torquers != null)
                    external += Adcs.Torquers.Torque(Env.MagneticFieldBody);
                foreach (ThrusterSet t in ThrusterSets)
                    external += t.Torque;

                Vec3d wheelH = Adcs.Wheels != null ? Adcs.Wheels.TotalMomentum : Vec3d.Zero;
                Vec3d wheelT = Adcs.Wheels != null ? Adcs.Wheels.TorqueOnWheels : Vec3d.Zero;
                Env.IntegrateAttitude(h, external, wheelH, wheelT);

                Power.Step(h);
                Thermal.Step(h);
                Link.Downlink(h);

                _tickTimer += h;
                if (_tickTimer >= tickPeriod)
                {
                    double period = _tickTimer;
                    _tickTimer = 0;
                    WriteTelemetry();
                    Link.Update();
                    Obsw.Tick(period);
                    if (Ticked != null) Ticked(period);
                }
            }
        }

        /// <summary>Raised at the onboard-software rate (about 1 Hz of simulated time).</summary>
        public event Action<double> Ticked;

        public void WriteTelemetry()
        {
            TelemetryStore tm = Telemetry;
            const string g = "Orbit";
            tm.Set("orbit.altitude", g, "Altitude", Env.AltitudeKm, "km", 1);
            tm.Set("orbit.latitude", g, "Latitude", Env.LatitudeDeg, "deg", 2);
            tm.Set("orbit.longitude", g, "Longitude", Env.LongitudeDeg, "deg", 2);
            tm.Set("orbit.speed", g, "Speed", Env.Velocity.Length, "km/s", 3);
            tm.SetText("orbit.sun", g, "Sunlight", Env.InSun ? "SUNLIGHT" : "ECLIPSE", Env.InSun ? 1 : 0);
            tm.Set("orbit.beta", g, "Sun angle to orbit plane (beta)", Env.BetaDeg, "deg", 1);
            tm.Set("orbit.period", g, "Orbit period", Env.Orbit.PeriodSeconds / 60.0, "min", 1);
            tm.Set("orbit.inclination", g, "Inclination", Env.Orbit.I * OpsMath.Rad2Deg, "deg", 3);
            if (OrbitClass == "GEO")
            {
                // Drift of the sub-satellite point (deg/day) from the semi-major axis error
                double aGeo = Math.Pow(OpsMath.MuEarth / (OpsMath.EarthRotationRate * OpsMath.EarthRotationRate), 1.0 / 3.0);
                double drift = -1.5 * (Env.Orbit.A - aGeo) / aGeo * 360.0 * (86400.0 / 86164.1);
                tm.Set("orbit.drift", g, "Longitude drift", drift, "deg/day", 4, -0.01, 0.01);
            }

            Power.WriteTelemetry(tm);
            Thermal.WriteTelemetry(tm);
            Adcs.WriteTelemetry(tm);
            Obsw.WriteTelemetry(tm);
            Link.WriteTelemetry(tm);
            foreach (SimComponent c in Components)
                c.WriteTelemetry(tm);
            WritePayloadService(tm);
        }

        /// <summary>Navigation/communication service: every chain unit working, clocks locked, payload on target.</summary>
        private void WritePayloadService(TelemetryStore tm)
        {
            bool hasChain = false, ok = true;
            foreach (SimComponent c in Components)
            {
                var p = c as PayloadComponent;
                if (p == null || !p.ChainMember) continue;
                hasChain = true;
                if (!p.Working || !p.Ready) ok = false;
            }
            if (!hasChain) return;
            ok = ok && Adcs.PayloadPointingOk;
            tm.SetText("payload.service", "Payload", "User service", ok ? "IN SERVICE" : "OUT OF SERVICE", ok ? 1 : 0);
        }

        // ------------------------------------------------------------------ power events

        public void OnBrownout()
        {
            foreach (SimComponent c in Components)
                if (c.Switchable && !c.Essential)
                    c.SetOn(false);
        }

        public void OnPowerRestored()
        {
            // Power-on defaults: loads stay off until commanded
        }

        public void ShedNonEssential()
        {
            foreach (SimComponent c in Components)
                if (c.Switchable && !c.Essential && (c is PayloadComponent || c is RadioComponent && !((RadioComponent)c).IsTtc))
                    c.SetOn(false);
        }

        // ------------------------------------------------------------------ commands for the UI

        public List<CommandSpec> CommandsFor(string target)
        {
            var list = new List<CommandSpec>();
            if (target == "ADCS") Adcs.GetCommands(list);
            else if (target == "OBSW") Obsw.GetCommands(list);
            else
            {
                SimComponent c = Find(target);
                if (c != null) c.GetCommands(list);
            }
            return list;
        }
    }
}
