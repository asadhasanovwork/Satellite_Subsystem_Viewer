using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    public sealed class ForecastWindow
    {
        public double Start;   // s since J2000
        public double End;
        public string Label;
    }

    /// <summary>Predicts ground-station passes and eclipses ahead of the satellite (for the planning timeline).</summary>
    public static class Forecast
    {
        public static void Compute(SatelliteSim sim, double horizonSeconds, List<ForecastWindow> passes, List<ForecastWindow> eclipses)
        {
            passes.Clear();
            eclipses.Clear();
            SpaceEnvironment env = sim.Env;
            double period = env.Orbit.PeriodSeconds;
            double step = Math.Max(15, Math.Min(90, period / 150));
            var stationEcef = new List<Vec3d>();
            foreach (GroundStationDef s in sim.Link.Stations)
                stationEcef.Add(OpsMath.StationEcef(s.latDeg, s.lonDeg));

            ForecastWindow pass = null, eclipse = null;
            double t0 = env.Time;
            for (double dt = 0; dt <= horizonSeconds; dt += step)
            {
                double t = t0 + dt;
                Vec3d r = env.PositionAt(t);

                string best = null;
                double bestEl = -90;
                for (int i = 0; i < stationEcef.Count; i++)
                {
                    double el = OpsMath.Elevation(r, stationEcef[i], t);
                    if (el >= sim.Link.Stations[i].minElevationDeg && el > bestEl)
                    {
                        bestEl = el;
                        best = sim.Link.Stations[i].name;
                    }
                }
                if (best != null)
                {
                    if (pass == null) { pass = new ForecastWindow { Start = t, End = t, Label = best }; passes.Add(pass); }
                    else pass.End = t;
                }
                else pass = null;

                bool dark = OpsMath.InEclipse(r, OpsMath.SunDirection(t));
                if (dark)
                {
                    if (eclipse == null) { eclipse = new ForecastWindow { Start = t, End = t, Label = "Eclipse" }; eclipses.Add(eclipse); }
                    else eclipse.End = t;
                }
                else eclipse = null;
            }
            // Give single-sample windows a visible width
            foreach (ForecastWindow w in passes) if (w.End - w.Start < step) w.End = w.Start + step;
            foreach (ForecastWindow w in eclipses) if (w.End - w.Start < step) w.End = w.Start + step;
        }
    }
}
