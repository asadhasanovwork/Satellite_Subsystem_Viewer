using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    /// <summary>Builds simulation components from Builder slots using operations_models.json.</summary>
    public static class SimFactory
    {
        public static SimComponent Create(SlotInstance slot, SlotModel model)
        {
            string kind = model != null ? model.kind : "passive";
            var p = new ModelParams(model, slot.VariantId);
            SimComponent c;
            switch (kind)
            {
                case "battery":
                    c = new BatteryComponent
                    {
                        CapacityWh = p.Get("capacityWh", 20),
                        NominalVoltage = p.Get("voltage", 7.4f),
                        Soc = p.Get("soc", 0.85f)
                    };
                    break;

                case "solar":
                    {
                        var a = new SolarArrayComponent { BuilderAverageW = slot.PowerGenW };
                        int panel = 0, wing = 0;
                        foreach (PartGeom g in slot.Parts)
                        {
                            if (g.Type == "solar_panel")
                                a.Panels.Add(new SolarPanel { Label = "Panel " + (++panel), Normal = g.Forward.Swap().Normalized, PeakW = 1 });
                            else if (g.Type == "solar_wing")
                                a.Panels.Add(new SolarPanel { Label = (g.Position.x >= 0 ? "Wing +X" : "Wing -X") + (wing++ > 1 ? " " + wing : ""), Tracking = true, Axis = g.Up.Swap().Normalized, PeakW = 1 });
                        }
                        c = a;
                        break;
                    }

                case "sada":
                    c = new SadaComponent();
                    break;

                case "heater":
                    c = new HeaterComponent { LowC = p.Get("lowC", 2), HighC = p.Get("highC", 8) };
                    break;

                case "hdrm":
                    c = new DeployComponent { BurnSeconds = p.Get("burnSeconds", 5) };
                    break;

                case "obc":
                    c = new ComputerComponent { BootSeconds = p.Get("bootSeconds", 30), WatchdogSeconds = p.Get("watchdogSeconds", 120) };
                    break;

                case "storage":
                    c = new StorageComponent { CapacityMB = p.Get("capacityMB", 8000) };
                    break;

                case "radio_ttc":
                    c = new RadioComponent { Role = "ttc", RateKbps = p.Get("rateKbps", 9.6f), Switchable = false, Essential = true };
                    break;

                case "radio_data":
                    c = new RadioComponent { Role = "data", RateKbps = p.Get("rateKbps", 1000) };
                    break;

                case "magnetometer":
                case "sun_sensor":
                case "earth_sensor":
                case "star_tracker":
                case "gyro":
                case "gnss":
                    {
                        var s = new SensorComponent { Type = SensorTypeOf(kind), NoiseDeg = p.Get("noiseDeg", DefaultNoise(kind)) };
                        foreach (PartGeom g in slot.Parts)
                            if (g.Type == "sensor" || g.Type == "antenna" || g.Type == "box")
                                s.Boresights.Add(g.Forward.Swap().Normalized);
                        if (kind == "sun_sensor" && s.Boresights.Count >= 4)
                            s.Boresights.Clear(); // coarse sensors on many faces: whole sky
                        c = s;
                        break;
                    }

                case "wheels":
                    {
                        var w = new ReactionWheelSet
                        {
                            HMax = p.Get("hMax", 0.0015f),
                            TorqueMax = p.Get("torqueMax", 0.002f),
                            MaxRpm = p.Get("maxRpm", 6000)
                        };
                        SliderValue r = slot.Slider("r");
                        if (r != null && r.Default > 0)
                        {
                            double k = r.Value / r.Default;
                            w.HMax *= k * k;
                        }
                        foreach (PartGeom g in slot.Parts)
                            if (g.Type == "reaction_wheel")
                                w.Wheels.Add(new Wheel { Axis = OrthoUp(g).Swap().Normalized });
                        w.ApplyBias(p.Get("biasFraction", 0));
                        c = w;
                        break;
                    }

                case "magnetorquer":
                    {
                        var m = new MagnetorquerSet { MaxDipole = p.Get("maxDipole", 0.2f) };
                        foreach (PartGeom g in slot.Parts)
                            if (g.Type == "magnetorquer")
                            {
                                m.Axes.Add(OrthoUp(g).Swap().Normalized);
                                m.RodFailed.Add(false);
                            }
                        if (m.Axes.Count == 0)
                        {
                            m.Axes.Add(Vec3d.UnitX); m.Axes.Add(Vec3d.UnitY); m.Axes.Add(Vec3d.UnitZ);
                            m.RodFailed.Add(false); m.RodFailed.Add(false); m.RodFailed.Add(false);
                        }
                        c = m;
                        break;
                    }

                case "thrusters":
                    {
                        var t = new ThrusterSet { ThrustN = p.Get("thrustN", 1), IspS = p.Get("ispS", 220) };
                        foreach (PartGeom g in slot.Parts)
                            if (g.Type == "thruster")
                                t.Thrusters.Add(new Thruster { Position = g.Position.Swap(), ForceDir = -g.Forward.Swap().Normalized });
                        float own = p.Get("propellantKg", 0);
                        if (own > 0)
                            t.Tank = new TankComponent { Id = slot.SlotId + ".tank", Name = slot.SlotName + " tank", Kind = "tank", Subsystem = slot.Subsystem, PropellantKg = own, InitialKg = own, PressureStartBar = p.Get("pressureBar", 8) };
                        c = t;
                        break;
                    }

                case "tank":
                    {
                        SliderValue prop = slot.Slider("prop");
                        double kg = prop != null ? prop.Value : p.Get("propellantKg", 50);
                        c = new TankComponent { PropellantKg = kg, InitialKg = kg, PressureStartBar = p.Get("pressureBar", 22) };
                        break;
                    }

                case "payload":
                    {
                        var pl = new PayloadComponent
                        {
                            PayloadType = PayloadTypeOf((int)p.Get("type", 0)),
                            DataRateMBps = p.Get("dataRateMBps", 0),
                            WarmupSeconds = p.Get("warmupSeconds", 1800),
                            ChainMember = p.Get("chain", 1) > 0.5f
                        };
                        SliderValue ch = slot.Slider("ch");
                        if (ch != null)
                        {
                            pl.MaxChannels = (int)Math.Round(ch.Value);
                            pl.Channels = pl.MaxChannels;
                        }
                        c = pl;
                        if (p.Get("startOn", 1) > 0.5f && p.Get("startReady", 1) > 0.5f)
                            pl.StartReady();
                        break;
                    }

                default:
                    c = new PassiveComponent();
                    break;
            }

            c.Id = slot.SlotId;
            c.Name = slot.SlotName;
            c.Kind = kind;
            c.Subsystem = slot.Subsystem;
            c.VariantName = slot.VariantName;
            c.PowerW = (float)slot.PowerW;
            if (model != null)
            {
                float sw = p.Get("switchable", -1);
                if (sw >= 0) c.Switchable = sw > 0.5f;
                float ess = p.Get("essential", -1);
                if (ess >= 0) c.Essential = ess > 0.5f;
                float on = p.Get("startOn", -1);
                if (on >= 0) c.On = on > 0.5f;
            }
            return c;
        }

        private static Vec3d OrthoUp(PartGeom g)
        {
            Vec3d f = g.Forward.Normalized;
            Vec3d u = g.Up - f * Vec3d.Dot(g.Up, f);
            return u.SqrLength > 1e-9 ? u.Normalized : f.AnyPerpendicular();
        }

        private static SensorType SensorTypeOf(string kind)
        {
            switch (kind)
            {
                case "magnetometer": return SensorType.Magnetometer;
                case "sun_sensor": return SensorType.SunSensor;
                case "earth_sensor": return SensorType.EarthSensor;
                case "star_tracker": return SensorType.StarTracker;
                case "gyro": return SensorType.Gyro;
                default: return SensorType.Gnss;
            }
        }

        private static float DefaultNoise(string kind)
        {
            switch (kind)
            {
                case "magnetometer": return 1f;
                case "sun_sensor": return 1f;
                case "earth_sensor": return 0.1f;
                case "star_tracker": return 0.005f;
                case "gyro": return 0.005f;
                default: return 0f;
            }
        }

        private static string PayloadTypeOf(int code)
        {
            switch (code)
            {
                case 1: return "imager";
                case 2: return "science";
                case 3: return "clock";
                case 4: return "comms";
                default: return "chain";
            }
        }

        /// <summary>Build the whole simulation.</summary>
        public static SatelliteSim Build(TemplateModels models, IList<SlotInstance> slots, KeplerOrbit orbit, double startTime,
                                         double totalMassKg, string orbitClass)
        {
            var sim = new SatelliteSim(models, orbit, startTime) { OrbitClass = orbitClass };
            var modelBySlot = new Dictionary<string, SlotModel>();
            if (models != null)
                foreach (SlotModel m in models.slots)
                    if (m != null && !string.IsNullOrEmpty(m.slot)) modelBySlot[m.slot] = m;

            foreach (SlotInstance s in slots)
            {
                SlotModel m;
                modelBySlot.TryGetValue(s.SlotId, out m);
                SimComponent c = Create(s, m);
                sim.Add(c);
                var t = c as ThrusterSet;
                if (t != null && t.Tank != null)
                    sim.Add(t.Tank);
            }
            sim.Complete(totalMassKg);
            return sim;
        }
    }
}
