using System;
using System.Collections.Generic;
using System.Globalization;

namespace Academy.Operations
{
    /// <summary>Creates a ready-to-fly simulation from template data, built slots and an optional scenario.</summary>
    public static class MissionSetup
    {
        public static double ParseUtc(string iso, double fallback)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                return OpsMath.SecondsSinceJ2000(dt);
            return fallback;
        }

        /// <param name="configure">Called after the components exist and before calibration / initial attitude
        /// (the Unity layer uses it to give the solar panels their real deployed normals).</param>
        public static SatelliteSim Create(TemplateModels models, IList<SlotInstance> slots, double totalMassKg, string orbitClass,
                                          ScenarioDef scenario, Action<SatelliteSim> configure = null)
        {
            ScenarioStart start = scenario != null ? scenario.start : null;
            double epoch = ParseUtc(start != null ? start.utc : null, ParseUtc(models.startUtc, OpsMath.SecondsSinceJ2000(new DateTime(2027, 3, 20, 6, 0, 0, DateTimeKind.Utc))));
            OrbitSettings orbitSettings = start != null && start.overrideOrbit ? start.orbit : models.orbit;
            KeplerOrbit orbit = KeplerOrbit.FromSettings(orbitSettings, epoch);

            if (start != null && start.placement == "station")
            {
                GroundStationDef gs = models.groundStations.Find(s => s.name.Equals(start.placeStation, StringComparison.OrdinalIgnoreCase));
                if (gs == null && models.groundStations.Count > 0) gs = models.groundStations[0];
                if (gs != null) orbit.PlaceBeforeStation(gs.latDeg, gs.lonDeg, start.placeLeadDeg);
            }
            else if (start != null && start.placement == "eclipse")
            {
                orbit.PlaceForEclipse(start.placeLeadDeg);
            }

            SatelliteSim sim = SimFactory.Build(models, slots, orbit, epoch, totalMassKg, orbitClass);
            if (configure != null)
                configure(sim);
            sim.CalibrateSolar();

            // ---- initial state
            AdcsMode mode = sim.Adcs.HasWheels ? AdcsMode.SunSafe : AdcsMode.Detumble;
            if (start != null && !string.IsNullOrEmpty(start.adcsMode))
            {
                AdcsMode parsed;
                if (Enum.TryParse(start.adcsMode, true, out parsed)) mode = parsed;
                if (start.adcsMode.Equals("SUNSAFE", StringComparison.OrdinalIgnoreCase)) mode = AdcsMode.SunSafe;
            }
            sim.Adcs.Mode = mode;
            AlignForMode(sim, mode);

            if (start != null)
            {
                Vec3d rates = ParamUtil.Vec(start.ratesDps, Vec3d.Zero).Swap() * OpsMath.Deg2Rad;
                if (rates.SqrLength > 0) sim.Env.BodyRate = rates;
                if (start.socPercent >= 0 && sim.Power.Battery != null) sim.Power.Battery.Soc = OpsMath.Clamp(start.socPercent / 100.0, 0, 1);
                if (start.temperatureC > -500) sim.Thermal.TemperatureC = start.temperatureC;
                if (start.stowed)
                {
                    foreach (DeployComponent d in sim.Deployers) d.SetDeployed(false);
                    foreach (SolarArrayComponent a in sim.Power.Arrays)
                        foreach (SolarPanel p in a.Panels) if (p.Tracking) p.Deployed = false;
                }
                sim.Link.AlwaysInContact = start.alwaysInContact;
                if (start.commands != null)
                    foreach (string c in start.commands)
                        sim.Obsw.Execute(c, CommandSource.Instructor);
            }

            // Free flight: start with the training aid on so the console has data immediately
            // (the instructor panel can switch it off for realistic pass-limited operations)
            if (scenario == null)
                sim.Link.AlwaysInContact = true;

            sim.WriteTelemetry();
            sim.Link.InvalidatePrediction();
            sim.Link.Update();
            return sim;
        }

        /// <summary>Start already pointing as the mode wants, so free flight begins calm.</summary>
        public static void AlignForMode(SatelliteSim sim, AdcsMode mode)
        {
            SpaceEnvironment env = sim.Env;
            if (mode == AdcsMode.Nadir)
            {
                Vec3d secondary = sim.Adcs.SecondaryIsOrbitNormal ? env.OrbitNormalEci : env.Velocity.Normalized;
                env.Attitude = QuatD.FromTwoVectors(sim.Adcs.NadirAxis, sim.Adcs.SecondaryAxis, env.NadirEci, secondary);
                env.BodyRate = env.Attitude.InverseRotate(env.OrbitNormalEci * env.OrbitRate);
            }
            else if (mode == AdcsMode.SunSafe)
            {
                Vec3d axis = sim.Adcs.SunAxis.SqrLength > 0 ? sim.Adcs.SunAxis : (sim.Solar != null ? sim.Solar.BestSunAxis() : Vec3d.UnitZ);
                env.Attitude = QuatD.FromTwoVectors(axis, sim.Adcs.SecondaryAxis, env.SunEci, env.OrbitNormalEci);
                env.BodyRate = Vec3d.Zero;
            }
        }
    }
}
