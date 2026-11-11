using System;

namespace Academy.Operations
{
    // Pure C# maths for the operations simulator (no UnityEngine), so the physics can be tested headless.
    //
    // Frames
    //   Inertial (ECI): right-handed, X towards the vernal equinox, Z towards the north pole. Units km, km/s.
    //   Body: right-handed satellite frame. Unity's left-handed local frame maps to it by swapping Y and Z
    //         (see Swap): body = (local.x, local.z, local.y). The same swap maps ECI to Unity world.
    //   Attitude quaternion q maps body vectors to inertial: v_i = q.Rotate(v_b).

    [Serializable]
    public struct Vec3d
    {
        public double x, y, z;

        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d UnitX = new Vec3d(1, 0, 0);
        public static readonly Vec3d UnitY = new Vec3d(0, 1, 0);
        public static readonly Vec3d UnitZ = new Vec3d(0, 0, 1);

        public static Vec3d operator +(Vec3d a, Vec3d b) { return new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3d operator -(Vec3d a, Vec3d b) { return new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3d operator -(Vec3d a) { return new Vec3d(-a.x, -a.y, -a.z); }
        public static Vec3d operator *(Vec3d a, double s) { return new Vec3d(a.x * s, a.y * s, a.z * s); }
        public static Vec3d operator *(double s, Vec3d a) { return new Vec3d(a.x * s, a.y * s, a.z * s); }
        public static Vec3d operator /(Vec3d a, double s) { return new Vec3d(a.x / s, a.y / s, a.z / s); }

        public double Length { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public double SqrLength { get { return x * x + y * y + z * z; } }

        public Vec3d Normalized
        {
            get
            {
                double l = Length;
                return l > 1e-15 ? this / l : Zero;
            }
        }

        public static double Dot(Vec3d a, Vec3d b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static Vec3d Cross(Vec3d a, Vec3d b)
        {
            return new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        /// <summary>Component-wise product (used for diagonal inertia).</summary>
        public static Vec3d Scale(Vec3d a, Vec3d b) { return new Vec3d(a.x * b.x, a.y * b.y, a.z * b.z); }

        /// <summary>Unity local/world (left-handed) &lt;-&gt; body/ECI (right-handed). Its own inverse.</summary>
        public Vec3d Swap() { return new Vec3d(x, z, y); }

        public double this[int i]
        {
            get { return i == 0 ? x : (i == 1 ? y : z); }
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }

        /// <summary>Angle between two vectors in degrees.</summary>
        public static double AngleDeg(Vec3d a, Vec3d b)
        {
            double la = a.Length, lb = b.Length;
            if (la < 1e-15 || lb < 1e-15)
                return 0;
            double c = Dot(a, b) / (la * lb);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * OpsMath.Rad2Deg;
        }

        /// <summary>Any unit vector perpendicular to this one.</summary>
        public Vec3d AnyPerpendicular()
        {
            Vec3d n = Normalized;
            Vec3d t = Math.Abs(n.x) < 0.9 ? UnitX : UnitY;
            return Cross(n, t).Normalized;
        }

        public override string ToString() { return string.Format("({0:0.####}, {1:0.####}, {2:0.####})", x, y, z); }
    }

    [Serializable]
    public struct QuatD
    {
        public double w, x, y, z;

        public QuatD(double w, double x, double y, double z) { this.w = w; this.x = x; this.y = y; this.z = z; }

        public static readonly QuatD Identity = new QuatD(1, 0, 0, 0);

        public Vec3d Vector { get { return new Vec3d(x, y, z); } }

        public static QuatD operator *(QuatD a, QuatD b)
        {
            return new QuatD(
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w);
        }

        public QuatD Conjugate { get { return new QuatD(w, -x, -y, -z); } }

        public QuatD Normalized
        {
            get
            {
                double n = Math.Sqrt(w * w + x * x + y * y + z * z);
                return n > 1e-15 ? new QuatD(w / n, x / n, y / n, z / n) : Identity;
            }
        }

        /// <summary>v' = q v q*</summary>
        public Vec3d Rotate(Vec3d v)
        {
            Vec3d u = Vector;
            Vec3d t = 2.0 * Vec3d.Cross(u, v);
            return v + w * t + Vec3d.Cross(u, t);
        }

        public Vec3d InverseRotate(Vec3d v) { return Conjugate.Rotate(v); }

        public static QuatD FromAxisAngle(Vec3d axis, double angleRad)
        {
            Vec3d n = axis.Normalized;
            double h = angleRad * 0.5, s = Math.Sin(h);
            return new QuatD(Math.Cos(h), n.x * s, n.y * s, n.z * s);
        }

        /// <summary>Rotation vector (axis * angle, radians) of this quaternion, shortest way.</summary>
        public Vec3d ToRotationVector()
        {
            QuatD q = w < 0 ? new QuatD(-w, -x, -y, -z) : this;
            double s = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (s < 1e-12)
                return 2.0 * q.Vector;
            double angle = 2.0 * Math.Atan2(s, q.w);
            return q.Vector * (angle / s);
        }

        /// <summary>Quaternion of a rotation matrix given by its columns (the images of the basis vectors).</summary>
        public static QuatD FromColumns(Vec3d c0, Vec3d c1, Vec3d c2)
        {
            double m00 = c0.x, m10 = c0.y, m20 = c0.z;
            double m01 = c1.x, m11 = c1.y, m21 = c1.z;
            double m02 = c2.x, m12 = c2.y, m22 = c2.z;
            double trace = m00 + m11 + m22;
            QuatD q;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2;
                q = new QuatD(0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
                q = new QuatD((m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s);
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
                q = new QuatD((m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s);
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
                q = new QuatD((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s);
            }
            return q.Normalized;
        }

        /// <summary>
        /// Attitude that maps body axis <paramref name="bodyPrimary"/> onto <paramref name="targetPrimary"/> and puts
        /// <paramref name="bodySecondary"/> as close as possible to <paramref name="targetSecondary"/> (TRIAD).
        /// </summary>
        public static QuatD FromTwoVectors(Vec3d bodyPrimary, Vec3d bodySecondary, Vec3d targetPrimary, Vec3d targetSecondary)
        {
            Vec3d b1 = bodyPrimary.Normalized;
            Vec3d b2 = Vec3d.Cross(b1, bodySecondary);
            if (b2.SqrLength < 1e-12) b2 = b1.AnyPerpendicular();
            b2 = b2.Normalized;
            Vec3d b3 = Vec3d.Cross(b1, b2);

            Vec3d t1 = targetPrimary.Normalized;
            Vec3d t2 = Vec3d.Cross(t1, targetSecondary);
            if (t2.SqrLength < 1e-12) t2 = t1.AnyPerpendicular();
            t2 = t2.Normalized;
            Vec3d t3 = Vec3d.Cross(t1, t2);

            // R = T * B^T ; columns of R are images of the body basis vectors
            Vec3d c0 = t1 * b1.x + t2 * b2.x + t3 * b3.x;
            Vec3d c1 = t1 * b1.y + t2 * b2.y + t3 * b3.y;
            Vec3d c2 = t1 * b1.z + t2 * b2.z + t3 * b3.z;
            return FromColumns(c0, c1, c2);
        }

        /// <summary>Integrate q' = 0.5 q (0, w_body) for dt seconds (exact for constant rate).</summary>
        public QuatD Integrate(Vec3d omegaBody, double dt)
        {
            double rate = omegaBody.Length;
            if (rate * dt < 1e-12)
                return this;
            return (this * FromAxisAngle(omegaBody, rate * dt)).Normalized;
        }
    }

    public static class OpsMath
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;
        public const double TwoPi = Math.PI * 2.0;

        // Earth (WGS-84 / EGM96 values, public)
        public const double MuEarth = 398600.4418;     // km^3/s^2
        public const double EarthRadius = 6378.137;     // km
        public const double J2 = 1.08262668e-3;
        public const double EarthRotationRate = 7.2921150e-5; // rad/s
        public const double DipoleB0 = 3.12e-5;          // T, field at the equator surface
        public const double G0 = 9.80665;                 // m/s^2

        // North geomagnetic pole (approximate, 2020s)
        public const double GeomagneticPoleLatDeg = 80.7;
        public const double GeomagneticPoleLonDeg = -72.7;

        public static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static double WrapTwoPi(double a)
        {
            a %= TwoPi;
            return a < 0 ? a + TwoPi : a;
        }

        /// <summary>Wrap degrees to (-180, 180].</summary>
        public static double WrapDeg180(double a)
        {
            a %= 360.0;
            if (a > 180) a -= 360;
            if (a <= -180) a += 360;
            return a;
        }

        public static Vec3d RotZ(Vec3d v, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new Vec3d(c * v.x - s * v.y, s * v.x + c * v.y, v.z);
        }

        public static Vec3d RotX(Vec3d v, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new Vec3d(v.x, c * v.y - s * v.z, s * v.y + c * v.z);
        }

        // ------------------------------------------------------------------ time

        /// <summary>Seconds since J2000 (2000-01-01 12:00 UTC; the UTC/TT difference is ignored for teaching).</summary>
        public static double SecondsSinceJ2000(DateTime utc)
        {
            var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return (utc.ToUniversalTime() - j2000).TotalSeconds;
        }

        public static DateTime ToUtc(double secondsSinceJ2000)
        {
            var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return j2000.AddSeconds(secondsSinceJ2000);
        }

        /// <summary>Greenwich mean sidereal angle (radians).</summary>
        public static double Gmst(double secondsSinceJ2000)
        {
            double d = secondsSinceJ2000 / 86400.0;
            return WrapTwoPi((280.46061837 + 360.98564736629 * d) * Deg2Rad);
        }

        public static Vec3d EcefToEci(Vec3d ecef, double t) { return RotZ(ecef, Gmst(t)); }
        public static Vec3d EciToEcef(Vec3d eci, double t) { return RotZ(eci, -Gmst(t)); }

        // ------------------------------------------------------------------ sun

        /// <summary>Unit vector Earth -&gt; Sun in ECI (low-precision almanac formula, ~0.01 deg).</summary>
        public static Vec3d SunDirection(double secondsSinceJ2000)
        {
            double d = secondsSinceJ2000 / 86400.0;
            double L = (280.460 + 0.9856474 * d) * Deg2Rad;
            double g = (357.528 + 0.9856003 * d) * Deg2Rad;
            double lambda = L + (1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Deg2Rad;
            double eps = (23.439 - 0.0000004 * d) * Deg2Rad;
            return new Vec3d(Math.Cos(lambda), Math.Cos(eps) * Math.Sin(lambda), Math.Sin(eps) * Math.Sin(lambda)).Normalized;
        }

        /// <summary>True when the satellite is in Earth's shadow (cylindrical shadow model).</summary>
        public static bool InEclipse(Vec3d rEci, Vec3d sunDir)
        {
            double along = Vec3d.Dot(rEci, sunDir);
            if (along >= 0)
                return false;
            Vec3d perpendicular = rEci - sunDir * along;
            return perpendicular.Length < EarthRadius;
        }

        // ------------------------------------------------------------------ magnetic field

        /// <summary>Earth's magnetic field at rEci (km) as a tilted dipole, in tesla, ECI.</summary>
        public static Vec3d MagneticField(Vec3d rEci, double t)
        {
            double lat = GeomagneticPoleLatDeg * Deg2Rad, lon = GeomagneticPoleLonDeg * Deg2Rad;
            // The dipole moment points towards the geographic south (opposite the north geomagnetic pole).
            Vec3d mEcef = -new Vec3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
            Vec3d m = EcefToEci(mEcef, t);
            double r = rEci.Length;
            Vec3d rh = rEci / r;
            double k = DipoleB0 * Math.Pow(EarthRadius / r, 3);
            return k * (3.0 * Vec3d.Dot(m, rh) * rh - m);
        }

        // ------------------------------------------------------------------ geography

        public static void LatLon(Vec3d rEci, double t, out double latDeg, out double lonDeg)
        {
            Vec3d e = EciToEcef(rEci, t);
            double r = e.Length;
            latDeg = Math.Asin(Clamp(e.z / r, -1, 1)) * Rad2Deg;
            lonDeg = Math.Atan2(e.y, e.x) * Rad2Deg;
        }

        public static Vec3d StationEcef(double latDeg, double lonDeg, double altKm = 0)
        {
            double lat = latDeg * Deg2Rad, lon = lonDeg * Deg2Rad;
            double r = EarthRadius + altKm;
            return new Vec3d(r * Math.Cos(lat) * Math.Cos(lon), r * Math.Cos(lat) * Math.Sin(lon), r * Math.Sin(lat));
        }

        /// <summary>Elevation (degrees) of the satellite above the station's horizon.</summary>
        public static double Elevation(Vec3d rEci, Vec3d stationEcef, double t)
        {
            Vec3d gs = EcefToEci(stationEcef, t);
            Vec3d up = gs.Normalized;
            Vec3d los = rEci - gs;
            double range = los.Length;
            if (range < 1e-9)
                return 90;
            return Math.Asin(Clamp(Vec3d.Dot(los, up) / range, -1, 1)) * Rad2Deg;
        }
    }

    /// <summary>A ground station (public locations only).</summary>
    [Serializable]
    public class GroundStationDef
    {
        public string name;
        public double latDeg;
        public double lonDeg;
        public double minElevationDeg = 5;
    }
}
