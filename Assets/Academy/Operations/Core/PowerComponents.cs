using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    /// <summary>Rechargeable battery: state of charge integrates (generation - load).</summary>
    public sealed class BatteryComponent : SimComponent
    {
        public double CapacityWh = 20;
        public double NominalVoltage = 7.4;
        public double Soc = 0.8;            // 0..1
        public double CapacityFactor = 1;   // cell failure reduces it
        public double LastNetW;

        public BatteryComponent() { Switchable = false; Essential = true; }

        public double UsableWh { get { return CapacityWh * CapacityFactor; } }
        public double Voltage { get { return NominalVoltage * (0.85 + 0.27 * Soc); } }

        public void Integrate(double netW, double dt)
        {
            LastNetW = netW;
            if (Offline)
            {
                Soc = 0;
                return;
            }
            Soc = OpsMath.Clamp(Soc + netW * dt / 3600.0 / Math.Max(1e-6, UsableWh), 0, 1);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.Set(Id + ".soc", Subsystem, "Battery charge", Soc * 100, "%", 1, 20, double.NaN);
            tm.Set(Id + ".voltage", Subsystem, "Battery voltage", Voltage, "V", 2);
            tm.Set(Id + ".current", Subsystem, LastNetW >= 0 ? "Charge current" : "Discharge current", LastNetW / Math.Max(0.1, Voltage), "A", 2);
            tm.Set(Id + ".capacity", Subsystem, "Usable capacity", UsableWh, "Wh", 1);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("CELL_FAIL", "0.5", "Lose a fraction of the capacity (0-1)"));
            list.Add(new FaultSpec("SET_SOC", "30", "Set the state of charge (%)"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "CELL_FAIL")
            {
                double loss = double.IsNaN(value) ? 0.5 : OpsMath.Clamp(value, 0, 0.95);
                CapacityFactor = 1 - loss;
                Soc = Math.Min(1, Soc / Math.Max(0.05, CapacityFactor) * CapacityFactor);
                return string.Format("Battery cell failure: {0:0}% capacity lost", loss * 100);
            }
            if (fault == "SET_SOC")
            {
                Soc = OpsMath.Clamp((double.IsNaN(value) ? 30 : value) / 100.0, 0, 1);
                return string.Format("Battery charge set to {0:0}%", Soc * 100);
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults()
        {
            base.ClearFaults();
            CapacityFactor = 1;
        }
    }

    /// <summary>One solar panel or wing (body frame geometry is refreshed from the 3D model each frame).</summary>
    public sealed class SolarPanel
    {
        public string Label;
        public Vec3d Normal = Vec3d.UnitX;   // body frame, fixed panels
        public bool Tracking;                // wing on a drive that turns it to the Sun about Axis
        public Vec3d Axis = Vec3d.UnitX;     // body frame, tracking wings
        public double PeakW;                 // at normal incidence
        public bool Disabled;                // instructor fault (string failure)
        public bool Deployed = true;         // tracking wings: stowed wings only expose the outer panel
        public Vec3d StuckNormal;            // normal frozen when the drive is stuck
        public bool HasStuckNormal;
        public double LastW;
    }

    /// <summary>Solar arrays: generation = sum of peak power x cos(sun angle), zero in eclipse.</summary>
    public sealed class SolarArrayComponent : SimComponent
    {
        public readonly List<SolarPanel> Panels = new List<SolarPanel>();
        public double Degradation = 1;       // ageing / contamination
        /// <summary>Orbit-average generation shown in the Builder for this slot (W).</summary>
        public double BuilderAverageW;
        public double LastGenerationW;
        public double LastIncidenceDeg;
        /// <summary>Drive assembly that rotates tracking wings (null = wings fixed).</summary>
        public SadaComponent Drive;

        public SolarArrayComponent() { Switchable = false; }

        public override double PowerDraw { get { return 0; } }

        /// <summary>Generation for a given Sun direction (body frame), ignoring eclipse.</summary>
        public double GenerationFor(Vec3d sunBody, bool updatePanels)
        {
            double total = 0;
            double bestCos = 0;
            bool driveWorks = Drive == null || Drive.Working;
            foreach (SolarPanel p in Panels)
            {
                double cos;
                if (p.Tracking)
                {
                    Vec3d axis = p.Axis.Normalized;
                    if (driveWorks)
                    {
                        double along = Vec3d.Dot(sunBody, axis);
                        cos = Math.Sqrt(Math.Max(0, 1 - along * along));
                        if (updatePanels)
                        {
                            Vec3d perpendicular = sunBody - axis * along;
                            if (perpendicular.SqrLength > 1e-9)
                            {
                                p.StuckNormal = perpendicular.Normalized;
                                p.HasStuckNormal = true;
                            }
                        }
                    }
                    else
                    {
                        Vec3d n = p.HasStuckNormal ? p.StuckNormal : axis.AnyPerpendicular();
                        cos = Math.Max(0, Vec3d.Dot(n, sunBody));
                    }
                    if (!p.Deployed)
                        cos *= 0.12;
                }
                else
                {
                    cos = Math.Max(0, Vec3d.Dot(p.Normal.Normalized, sunBody));
                }
                bestCos = Math.Max(bestCos, cos);
                double w = p.Disabled || Offline ? 0 : p.PeakW * cos * Degradation;
                if (updatePanels)
                    p.LastW = w;
                total += w;
            }
            if (updatePanels)
                LastIncidenceDeg = Math.Acos(OpsMath.Clamp(bestCos, -1, 1)) * OpsMath.Rad2Deg;
            return total;
        }

        public double CurrentGeneration()
        {
            if (!Sim.Env.InSun)
            {
                foreach (SolarPanel p in Panels) p.LastW = 0;
                LastGenerationW = 0;
                return 0;
            }
            LastGenerationW = GenerationFor(Sim.Env.SunBody.Normalized, true);
            return LastGenerationW;
        }

        /// <summary>Body direction that gives the most power (used for sun-safe pointing when not configured).</summary>
        public Vec3d BestSunAxis()
        {
            Vec3d best = Vec3d.UnitZ;
            double bestW = -1;
            const int samples = 400;
            double golden = Math.PI * (3 - Math.Sqrt(5));
            for (int i = 0; i < samples; i++)
            {
                double y = 1 - 2 * (i + 0.5) / samples;
                double r = Math.Sqrt(1 - y * y);
                var d = new Vec3d(Math.Cos(golden * i) * r, y, Math.Sin(golden * i) * r);
                double w = GenerationFor(d, false);
                if (w > bestW + 1e-9) { bestW = w; best = d; }
            }
            return best.Normalized;
        }

        /// <summary>
        /// Scale panel peaks so that, Sun-pointing in the longest eclipses, the orbit average equals the Builder's
        /// generation figure (the Builder shows average values).
        /// </summary>
        public void Calibrate(double builderAverageW, Vec3d sunAxis, double sunlitFraction)
        {
            foreach (SolarPanel p in Panels)
                if (p.PeakW <= 0) p.PeakW = 1;
            double atSunAxis = GenerationFor(sunAxis.Normalized, false);
            if (atSunAxis <= 1e-9 || builderAverageW <= 0)
                return;
            double target = builderAverageW / Math.Max(0.3, sunlitFraction);
            double k = target / atSunAxis;
            foreach (SolarPanel p in Panels)
                p.PeakW *= k;
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.Set(Id + ".power", Subsystem, "Solar generation", LastGenerationW, "W", 1);
            tm.Set(Id + ".incidence", Subsystem, "Sun angle on best panel", LastIncidenceDeg, "deg", 0);
            for (int i = 0; i < Panels.Count; i++)
            {
                SolarPanel p = Panels[i];
                tm.Set(Id + ".panel" + i, Subsystem, p.Label + (p.Disabled ? " (FAILED)" : ""), p.LastW, "W", 1);
            }
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("PANEL_OFF", "0", "Panel/wing number (0..n-1) stops producing"));
            list.Add(new FaultSpec("DEGRADE", "0.7", "Multiply all output by this factor"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "PANEL_OFF")
            {
                int i = double.IsNaN(value) ? 0 : (int)value;
                if (i < 0 || i >= Panels.Count)
                    return "No panel " + i;
                Panels[i].Disabled = true;
                return Panels[i].Label + " stopped producing power";
            }
            if (fault == "DEGRADE")
            {
                Degradation = OpsMath.Clamp(double.IsNaN(value) ? 0.7 : value, 0, 1);
                return string.Format("Solar output degraded to {0:0}%", Degradation * 100);
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults()
        {
            base.ClearFaults();
            Degradation = 1;
            foreach (SolarPanel p in Panels) p.Disabled = false;
        }
    }

    /// <summary>Solar array drive: turns the wings to follow the Sun. Off or stuck = wings stay where they are.</summary>
    public sealed class SadaComponent : SimComponent
    {
        public bool Stuck;

        public override bool Working { get { return base.Working && !Stuck; } }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("STUCK", "", "The drive stops turning; the wings freeze in place"));
            base.GetFaults(list);
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "STUCK")
            {
                Stuck = true;
                return "Solar array drive stuck";
            }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults() { base.ClearFaults(); Stuck = false; }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.SetText(Id + ".tracking", Subsystem, "Sun tracking", Working ? "TRACKING" : "STOPPED", Working ? 1 : 0);
        }
    }

    /// <summary>Thermostat heater: when enabled it heats below LowC until above HighC.</summary>
    public sealed class HeaterComponent : SimComponent
    {
        public double LowC = 2, HighC = 8;
        public bool Heating;
        public bool StuckOn, FailedOff;

        public override double PowerDraw { get { return Heating ? PowerW : 0; } }

        public override void Step(double dt)
        {
            double t = Sim.Thermal.TemperatureC;
            if (StuckOn && Sim.Power.BusAlive) { Heating = true; return; }
            if (!Working || FailedOff) { Heating = false; return; }
            if (t < LowC) Heating = true;
            else if (t > HighC) Heating = false;
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            tm.SetText(Id + ".state", Subsystem, Name + " thermostat", Offline ? "FAILED" : (On ? "ENABLED" : "DISABLED"), On ? 1 : 0);
            tm.SetText(Id + ".heating", Subsystem, Name + " heating", Heating ? "HEATING" : "IDLE", Heating ? 1 : 0);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("STUCK_ON", "", "Heater relay welded closed: always heating"));
            list.Add(new FaultSpec("FAIL_OFF", "", "Heater element open: never heats"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "STUCK_ON") { StuckOn = true; return "Heater stuck on"; }
            if (fault == "FAIL_OFF") { FailedOff = true; return "Heater failed open"; }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults() { base.ClearFaults(); StuckOn = FailedOff = false; }
    }

    /// <summary>Hold-down and release mechanism: DEPLOY fires it (burn wire / pyro) and releases the appendages.</summary>
    public sealed class DeployComponent : SimComponent
    {
        public bool Deployed = true;
        public bool Stuck;
        public double BurnSeconds = 5;
        private double _burnLeft;

        public DeployComponent() { Switchable = false; }

        public override double PowerDraw { get { return _burnLeft > 0 ? PowerW : 0; } }

        public event Action DeployedChanged;

        public override void Step(double dt)
        {
            if (_burnLeft <= 0)
                return;
            _burnLeft -= dt;
            if (_burnLeft > 0)
                return;
            if (Stuck || Offline || !Sim.Power.BusAlive)
            {
                Log(EventSeverity.Alarm, "Deployment FAILED: release mechanism did not open");
                return;
            }
            Deployed = true;
            Log(EventSeverity.Info, "Appendages released and deployed");
            if (DeployedChanged != null) DeployedChanged();
        }

        public void SetDeployed(bool deployed)
        {
            if (Deployed == deployed) return;
            Deployed = deployed;
            if (DeployedChanged != null) DeployedChanged();
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            list.Add(new CommandSpec("DEPLOY", "", "Fire the release mechanism (solar arrays, antennas)"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "DEPLOY")
            {
                if (Deployed) return "Already deployed";
                _burnLeft = BurnSeconds;
                return "Release mechanism firing for " + BurnSeconds + " s";
            }
            return base.Execute(command, args);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            tm.SetText(Id + ".deployed", Subsystem, "Appendages", Deployed ? "DEPLOYED" : (_burnLeft > 0 ? "RELEASING" : "STOWED"), Deployed ? 1 : 0);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("STUCK", "", "Release mechanism will not open"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "STUCK") { Stuck = true; return "Release mechanism stuck"; }
            return base.InjectFault(fault, value);
        }

        public override void ClearFaults() { base.ClearFaults(); Stuck = false; }
    }

    /// <summary>The power bus: generation, loads, battery, hardware under-voltage protection.</summary>
    public sealed class PowerSystem
    {
        private readonly SatelliteSim _sim;
        public BatteryComponent Battery;
        public readonly List<SolarArrayComponent> Arrays = new List<SolarArrayComponent>();

        public double GenerationW, LoadW, NetW;
        /// <summary>False after a full discharge until the battery recovers (everything is unpowered).</summary>
        public bool BusAlive = true;
        public bool LoadShed;

        public const double ShedSoc = 0.05;
        public const double RecoverSoc = 0.08;

        public PowerSystem(SatelliteSim sim) { _sim = sim; }

        public void Step(double dt)
        {
            GenerationW = 0;
            foreach (SolarArrayComponent a in Arrays)
                GenerationW += a.CurrentGeneration();

            LoadW = 0;
            if (BusAlive)
                foreach (SimComponent c in _sim.Components)
                    LoadW += c.PowerDraw;

            double net = GenerationW - LoadW;
            if (Battery == null)
            {
                BusAlive = GenerationW >= LoadW;
                NetW = net;
                return;
            }

            // Battery full: arrays are shunted back to what the loads need
            if (Battery.Soc >= 0.999 && net > 0)
                net = 0;
            NetW = net;
            Battery.Integrate(net, dt);

            if (BusAlive && Battery.Soc <= 0.0)
            {
                BusAlive = false;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Alarm, "EPS", "BATTERY EMPTY: bus under-voltage, every unit lost power");
                _sim.OnBrownout();
            }
            else if (!BusAlive && Battery.Soc >= RecoverSoc)
            {
                BusAlive = true;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Warning, "EPS", "Battery recovered: bus powered, computer rebooting");
                _sim.OnPowerRestored();
            }
            else if (BusAlive && !LoadShed && Battery.Soc < ShedSoc)
            {
                LoadShed = true;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Alarm, "EPS", "Hardware load shedding: battery below 5%, non-essential units switched off");
                _sim.ShedNonEssential();
            }
            else if (LoadShed && Battery.Soc > RecoverSoc)
            {
                LoadShed = false;
            }
        }

        public void WriteTelemetry(TelemetryStore tm)
        {
            tm.Set("eps.generation", "Power", "Total generation", GenerationW, "W", 1);
            tm.Set("eps.load", "Power", "Total load", LoadW, "W", 1);
            tm.Set("eps.net", "Power", "Net power (+ charging)", NetW, "W", 1);
            tm.SetText("eps.bus", "Power", "Power bus", BusAlive ? (LoadShed ? "LOAD SHED" : "NOMINAL") : "DEAD", BusAlive ? 1 : 0);
        }
    }

    /// <summary>One-node thermal model of the satellite's interior.</summary>
    public sealed class ThermalModel
    {
        private readonly SatelliteSim _sim;
        private readonly ThermalSettings _s;
        public double TemperatureC;

        public ThermalModel(SatelliteSim sim, ThermalSettings settings)
        {
            _sim = sim;
            _s = settings ?? new ThermalSettings();
            TemperatureC = _s.startTempC;
        }

        public void Step(double dt)
        {
            double sun = _sim.Env.InSun ? _s.sunHeatW : 0;
            double internalHeat = _sim.Power.LoadW; // electrical power ends up as heat
            double q = sun + internalHeat - _s.conductanceWPerK * (TemperatureC - _s.sinkTempC);
            TemperatureC += q * dt / Math.Max(1, _s.heatCapacityJPerK);
        }

        public void WriteTelemetry(TelemetryStore tm)
        {
            tm.Set("tcs.temperature", "Thermal", "Internal temperature", TemperatureC, "C", 1, _s.minOperatingC, _s.maxOperatingC);
        }
    }
}
