using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    /// <summary>
    /// Ground station contact: the satellite can only be commanded and heard while it is above a station's
    /// horizon and its radio works. Telemetry on the ground is the last snapshot received.
    /// </summary>
    public sealed class GroundLink
    {
        private readonly SatelliteSim _sim;
        public readonly List<GroundStationDef> Stations = new List<GroundStationDef>();
        private readonly List<Vec3d> _stationEcef = new List<Vec3d>();

        /// <summary>Training aid: pretend there is always contact.</summary>
        public bool AlwaysInContact;

        public bool InView { get; private set; }
        public bool Contact { get; private set; }
        public string StationName { get; private set; }
        public double Elevation { get; private set; }
        public string NoUplinkReason { get; private set; }

        public double NextAos { get; private set; } = double.NaN;  // sim time (J2000 s)
        public double NextLos { get; private set; } = double.NaN;
        public string NextStation { get; private set; }

        public readonly TelemetryStore GroundTelemetry = new TelemetryStore();
        public double LastTelemetryMet { get; private set; } = double.NaN;

        private double _predictedAt = double.NegativeInfinity;
        private bool _wasContact;

        public GroundLink(SatelliteSim sim, IEnumerable<GroundStationDef> stations)
        {
            _sim = sim;
            if (stations != null)
                foreach (GroundStationDef s in stations)
                {
                    Stations.Add(s);
                    _stationEcef.Add(OpsMath.StationEcef(s.latDeg, s.lonDeg));
                }
            StationName = "";
            NoUplinkReason = "";
            NextStation = "";
        }

        public bool CanUplink { get { return AlwaysInContact || Contact; } }

        private bool VisibleAt(double t, Vec3d r, out string station, out double elevation)
        {
            station = "";
            elevation = -90;
            bool any = false;
            for (int i = 0; i < Stations.Count; i++)
            {
                double el = OpsMath.Elevation(r, _stationEcef[i], t);
                if (el > elevation)
                {
                    elevation = el;
                    if (el >= Stations[i].minElevationDeg)
                    {
                        station = Stations[i].name;
                        any = true;
                    }
                }
            }
            return any;
        }

        public void Update()
        {
            string station;
            double elevation;
            InView = VisibleAt(_sim.Env.Time, _sim.Env.Position, out station, out elevation);
            Elevation = elevation;
            StationName = station;

            RadioComponent ttc = _sim.TtcRadio;
            bool radioOk = ttc == null || ttc.Working;
            bool computerOk = _sim.Computer == null || _sim.Computer.Running || _sim.Computer.State == ObcState.Boot;
            Contact = InView && radioOk && _sim.Power.BusAlive && computerOk;

            if (!InView) NoUplinkReason = "no ground station in view";
            else if (!_sim.Power.BusAlive) NoUplinkReason = "satellite has no power";
            else if (!radioOk) NoUplinkReason = "satellite radio not working";
            else if (!computerOk) NoUplinkReason = "onboard computer not responding";
            else NoUplinkReason = "";

            foreach (RadioComponent r in _sim.Radios)
                r.Transmitting = (Contact || (AlwaysInContact && r.Working)) && r.Working;

            if (Contact && !_wasContact)
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "GROUND", "AOS: contact with " + StationName);
            if (!Contact && _wasContact)
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "GROUND", "LOS: contact lost");
            _wasContact = Contact;

            if (Contact || AlwaysInContact)
            {
                _sim.Telemetry.CopyTo(GroundTelemetry);
                LastTelemetryMet = _sim.Env.Met;
            }

            // Re-predict passes when the current prediction is used up (or after an orbit change)
            double now = _sim.Env.Time;
            if (double.IsNaN(NextAos) || (now > NextLos) || now - _predictedAt > 6 * 3600)
                Predict();
        }

        /// <summary>Downlink payload data while in contact.</summary>
        public void Downlink(double dt)
        {
            if (_sim.Storage == null || !(Contact || AlwaysInContact))
                return;
            double kbps = 0;
            foreach (RadioComponent r in _sim.Radios)
                if (r.Working && r.Transmitting)
                    kbps += r.Role == "data" ? r.RateKbps : r.RateKbps * 0.3;
            if (kbps > 0)
                _sim.Storage.Take(kbps / 8000.0 * dt);
        }

        public void InvalidatePrediction() { NextAos = double.NaN; }

        private void Predict()
        {
            double now = _sim.Env.Time;
            _predictedAt = now;
            double period = _sim.Env.Orbit.PeriodSeconds;
            double step = Math.Max(20, Math.Min(300, period / 200));
            double horizon = Math.Max(36 * 3600, period * 3);
            string st;
            double el;
            bool visibleNow = VisibleAt(now, _sim.Env.Position, out st, out el);

            NextAos = double.NaN;
            NextLos = double.NaN;
            NextStation = "";
            double t = now;
            bool inPass = visibleNow;
            if (inPass)
            {
                NextAos = now;
                NextStation = st;
            }
            for (double dt = step; dt <= horizon; dt += step)
            {
                t = now + dt;
                Vec3d r = _sim.Env.PositionAt(t);
                bool v = VisibleAt(t, r, out st, out el);
                if (!inPass && v)
                {
                    NextAos = t;
                    NextStation = st;
                    inPass = true;
                }
                else if (inPass && !v)
                {
                    NextLos = t;
                    return;
                }
            }
            if (inPass && double.IsNaN(NextLos))
                NextLos = now + horizon; // continuous coverage (e.g. GEO)
        }

        public void WriteTelemetry(TelemetryStore tm)
        {
            const string g = "Communications";
            tm.SetText("link.contact", g, "Ground contact", Contact ? "IN CONTACT (" + StationName + ")" : "NO CONTACT", Contact ? 1 : 0);
            tm.Set("link.elevation", g, "Best station elevation", Elevation, "deg", 1);
        }
    }
}
