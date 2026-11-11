using System;

namespace Academy.Operations
{
    /// <summary>Orbit settings from data files (km and degrees).</summary>
    [Serializable]
    public class OrbitSettings
    {
        public double periapsisAltKm = 500;
        public double apoapsisAltKm = 500;
        public double inclinationDeg = 97.4;
        public double raanDeg = 0;
        public double argPeriapsisDeg = 0;
        public double trueAnomalyDeg = 0;
        /// <summary>Apply the secular J2 drift (node regression, apsidal rotation).</summary>
        public bool j2 = true;
        /// <summary>GEO: if set (not NaN-like sentinel -999), place the satellite over this longitude at the start.</summary>
        public double geoLongitudeDeg = -999;
    }

    /// <summary>
    /// Two-body Kepler orbit with secular J2 drift. Elements are referenced to <see cref="Epoch"/>.
    /// Impulsive manoeuvres re-derive the elements from position and velocity.
    /// </summary>
    public sealed class KeplerOrbit
    {
        public double A;          // semi-major axis, km
        public double E;          // eccentricity
        public double I;          // inclination, rad
        public double Raan;       // rad, at epoch
        public double ArgP;       // rad, at epoch
        public double M0;         // mean anomaly at epoch, rad
        public double Epoch;      // s since J2000
        public bool UseJ2 = true;

        public double MeanMotion { get { return Math.Sqrt(OpsMath.MuEarth / (A * A * A)); } }
        public double PeriodSeconds { get { return OpsMath.TwoPi / MeanMotion; } }

        public static KeplerOrbit FromSettings(OrbitSettings s, double epoch)
        {
            double rp = OpsMath.EarthRadius + s.periapsisAltKm;
            double ra = OpsMath.EarthRadius + Math.Max(s.apoapsisAltKm, s.periapsisAltKm);
            var o = new KeplerOrbit
            {
                A = 0.5 * (rp + ra),
                E = (ra - rp) / (ra + rp),
                I = s.inclinationDeg * OpsMath.Deg2Rad,
                Raan = s.raanDeg * OpsMath.Deg2Rad,
                ArgP = s.argPeriapsisDeg * OpsMath.Deg2Rad,
                Epoch = epoch,
                UseJ2 = s.j2
            };
            o.M0 = TrueToMean(s.trueAnomalyDeg * OpsMath.Deg2Rad, o.E);
            if (s.geoLongitudeDeg > -360)
                o.PlaceOverLongitude(s.geoLongitudeDeg);
            return o;
        }

        public static double TrueToMean(double nu, double e)
        {
            double E = 2.0 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
            return OpsMath.WrapTwoPi(E - e * Math.Sin(E));
        }

        public static double SolveKepler(double M, double e)
        {
            M = OpsMath.WrapTwoPi(M);
            double E = e < 0.8 ? M : Math.PI;
            for (int k = 0; k < 30; k++)
            {
                double f = E - e * Math.Sin(E) - M;
                double d = f / (1 - e * Math.Cos(E));
                E -= d;
                if (Math.Abs(d) < 1e-13)
                    break;
            }
            return E;
        }

        private void SecularRates(out double raanDot, out double argpDot)
        {
            raanDot = 0; argpDot = 0;
            if (!UseJ2)
                return;
            double n = MeanMotion;
            double p = A * (1 - E * E);
            double k = 1.5 * n * OpsMath.J2 * (OpsMath.EarthRadius / p) * (OpsMath.EarthRadius / p);
            double ci = Math.Cos(I);
            raanDot = -k * ci;
            argpDot = 0.5 * k * (5 * ci * ci - 1);
        }

        public void StateAt(double t, out Vec3d r, out Vec3d v)
        {
            double dt = t - Epoch;
            double n = MeanMotion;
            double raanDot, argpDot;
            SecularRates(out raanDot, out argpDot);
            double raan = Raan + raanDot * dt;
            double argp = ArgP + argpDot * dt;
            double M = M0 + n * dt;
            double Ea = SolveKepler(M, E);
            double cosE = Math.Cos(Ea), sinE = Math.Sin(Ea);
            double sq = Math.Sqrt(1 - E * E);
            double denom = 1 - E * cosE;

            var rp = new Vec3d(A * (cosE - E), A * sq * sinE, 0);
            var vp = new Vec3d(-A * n * sinE / denom, A * n * sq * cosE / denom, 0);
            r = PerifocalToEci(rp, raan, argp);
            v = PerifocalToEci(vp, raan, argp);
        }

        private Vec3d PerifocalToEci(Vec3d p, double raan, double argp)
        {
            return OpsMath.RotZ(OpsMath.RotX(OpsMath.RotZ(p, argp), I), raan);
        }

        /// <summary>Re-define the orbit from a state vector at time t (after a manoeuvre).</summary>
        public void SetFromState(Vec3d r, Vec3d v, double t)
        {
            double mu = OpsMath.MuEarth;
            Vec3d h = Vec3d.Cross(r, v);
            double rl = r.Length;
            Vec3d eVec = Vec3d.Cross(v, h) / mu - r / rl;
            double energy = v.SqrLength / 2 - mu / rl;
            A = -mu / (2 * energy);
            E = eVec.Length;
            I = Math.Acos(OpsMath.Clamp(h.z / h.Length, -1, 1));

            Vec3d node = Vec3d.Cross(Vec3d.UnitZ, h);
            bool equatorial = node.Length < 1e-9 * h.Length;
            bool circular = E < 1e-9;

            Raan = equatorial ? 0 : OpsMath.WrapTwoPi(Math.Atan2(node.y, node.x));
            Vec3d nodeDir = equatorial ? Vec3d.UnitX : node.Normalized;
            Vec3d hHat = h.Normalized;

            // Argument of latitude u measured from the node in the orbit plane
            double u = Math.Atan2(Vec3d.Dot(Vec3d.Cross(nodeDir, r), hHat), Vec3d.Dot(nodeDir, r));
            double nu;
            if (circular)
            {
                ArgP = 0;
                nu = u;
                E = 0;
            }
            else
            {
                double w = Math.Atan2(Vec3d.Dot(Vec3d.Cross(nodeDir, eVec), hHat), Vec3d.Dot(nodeDir, eVec));
                ArgP = OpsMath.WrapTwoPi(w);
                nu = u - w;
            }
            M0 = TrueToMean(OpsMath.WrapTwoPi(nu), E);
            Epoch = t;
        }

        public void ApplyDeltaV(double t, Vec3d dvKmPerS)
        {
            Vec3d r, v;
            StateAt(t, out r, out v);
            SetFromState(r, v + dvKmPerS, t);
        }

        /// <summary>Move along the orbit (circular orbits) so the satellite is over <paramref name="lonDeg"/> at Epoch.</summary>
        public void PlaceOverLongitude(double lonDeg)
        {
            double target = lonDeg * OpsMath.Deg2Rad + OpsMath.Gmst(Epoch);
            // For near-equatorial orbits the right ascension ~ raan + argp + nu
            double nu = OpsMath.WrapTwoPi(target - Raan - ArgP);
            M0 = TrueToMean(nu, E);
        }

        /// <summary>
        /// Rotate the orbit plane and phase (circular orbits) so the satellite starts <paramref name="leadDeg"/> of
        /// argument of latitude before passing over a ground station on an ascending pass.
        /// </summary>
        public void PlaceBeforeStation(double stationLatDeg, double stationLonDeg, double leadDeg)
        {
            double sinI = Math.Sin(I);
            double s = sinI > 1e-6 ? OpsMath.Clamp(Math.Sin(stationLatDeg * OpsMath.Deg2Rad) / sinI, -1, 1) : 0;
            double uStation = Math.Asin(s);                 // ascending-pass argument of latitude over the station
            double u0 = uStation - leadDeg * OpsMath.Deg2Rad; // where we start
            double n = MeanMotion;
            double tLead = (uStation - u0) / n;              // time until we reach the station's latitude
            // Right ascension of the satellite at the station latitude relative to the node:
            double alphaRel = Math.Atan2(Math.Sin(uStation) * Math.Cos(I), Math.Cos(uStation));
            double stationRa = stationLonDeg * OpsMath.Deg2Rad + OpsMath.Gmst(Epoch + tLead);
            double raanDot, argpDot;
            SecularRates(out raanDot, out argpDot);
            Raan = OpsMath.WrapTwoPi(stationRa - alphaRel - raanDot * tLead);
            ArgP = 0;
            M0 = TrueToMean(OpsMath.WrapTwoPi(u0), E);
        }

        /// <summary>Put the Sun in the orbit plane (beta ~ 0, longest eclipses) and start <paramref name="beforeShadowDeg"/> before the shadow.</summary>
        public void PlaceForEclipse(double beforeShadowDeg)
        {
            Vec3d sun = OpsMath.SunDirection(Epoch);
            double sunRa = Math.Atan2(sun.y, sun.x);
            Raan = OpsMath.WrapTwoPi(sunRa);
            ArgP = 0;
            // With the node towards the Sun, the shadow is centred on u = 180 deg, half-width asin(Re/a).
            double halfWidth = Math.Asin(Math.Min(1, OpsMath.EarthRadius / A));
            double u0 = Math.PI - halfWidth - beforeShadowDeg * OpsMath.Deg2Rad;
            M0 = TrueToMean(OpsMath.WrapTwoPi(u0), E);
        }
    }
}
