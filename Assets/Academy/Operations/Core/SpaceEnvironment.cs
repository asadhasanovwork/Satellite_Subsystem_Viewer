using System;

namespace Academy.Operations
{
    /// <summary>
    /// The single source of "truth" (like 42 in NOS3): time, orbit, Sun, eclipse, magnetic field and the
    /// satellite's rigid-body attitude. Every component reads its environment from here.
    /// </summary>
    public sealed class SpaceEnvironment
    {
        // ---- time
        /// <summary>Simulation time, seconds since J2000.</summary>
        public double Time { get; private set; }
        public double StartTime { get; private set; }
        /// <summary>Mission elapsed time (seconds since the simulation started).</summary>
        public double Met { get { return Time - StartTime; } }

        // ---- orbit
        public KeplerOrbit Orbit { get; private set; }
        public Vec3d Position { get; private set; }     // km, ECI
        public Vec3d Velocity { get; private set; }     // km/s, ECI
        public double AltitudeKm { get { return Position.Length - OpsMath.EarthRadius; } }
        public double LatitudeDeg { get; private set; }
        public double LongitudeDeg { get; private set; }

        // ---- environment
        public Vec3d SunEci { get; private set; }       // unit
        public bool InSun { get; private set; }
        public Vec3d MagneticFieldEci { get; private set; } // T

        // ---- attitude (body -> inertial)
        public QuatD Attitude = QuatD.Identity;
        public Vec3d BodyRate;                          // rad/s, body frame
        public Vec3d Inertia = new Vec3d(0.03, 0.03, 0.01); // kg m^2, principal (body frame)

        /// <summary>Constant disturbance torque in body frame (aero / solar pressure / residual dipole), N m.</summary>
        public Vec3d DisturbanceBody;
        /// <summary>Random disturbance magnitude (N m).</summary>
        public double DisturbanceNoise;

        private readonly Random _random = new Random(1234);

        public SpaceEnvironment(KeplerOrbit orbit, double startTime)
        {
            Orbit = orbit;
            StartTime = startTime;
            Time = startTime;
            UpdateEnvironment();
        }

        // ------------------------------------------------------------------ queries

        public Vec3d SunBody { get { return Attitude.InverseRotate(SunEci); } }
        public Vec3d MagneticFieldBody { get { return Attitude.InverseRotate(MagneticFieldEci); } }
        public Vec3d NadirEci { get { return -Position.Normalized; } }
        public Vec3d NadirBody { get { return Attitude.InverseRotate(NadirEci); } }
        public Vec3d OrbitNormalEci { get { return Vec3d.Cross(Position, Velocity).Normalized; } }
        public double OrbitRate { get { return Vec3d.Cross(Position, Velocity).Length / Position.SqrLength; } }

        /// <summary>Sun-to-orbit-plane angle (beta), degrees.</summary>
        public double BetaDeg { get { return 90.0 - Vec3d.AngleDeg(OrbitNormalEci, SunEci); } }

        /// <summary>Angle between a body axis and nadir, degrees.</summary>
        public double AngleToNadirDeg(Vec3d bodyAxis) { return Vec3d.AngleDeg(Attitude.Rotate(bodyAxis), NadirEci); }

        public double AngleToSunDeg(Vec3d bodyAxis) { return Vec3d.AngleDeg(Attitude.Rotate(bodyAxis), SunEci); }

        /// <summary>Total angular momentum of the body (without wheels), inertial frame.</summary>
        public Vec3d BodyMomentum { get { return Vec3d.Scale(Inertia, BodyRate); } }

        public double RateDegPerSec { get { return BodyRate.Length * OpsMath.Rad2Deg; } }

        // ------------------------------------------------------------------ stepping

        /// <summary>Advance the clock and orbit only (no attitude).</summary>
        public void AdvanceTime(double dt)
        {
            Time += dt;
            UpdateEnvironment();
        }

        /// <summary>
        /// Integrate attitude for dt seconds.
        /// Euler: I w' = T_ext - w x (I w + h_wheels) - hdot_wheels.
        /// </summary>
        public void IntegrateAttitude(double dt, Vec3d externalTorque, Vec3d wheelMomentum, Vec3d wheelTorqueOnWheels)
        {
            Vec3d disturbance = DisturbanceBody;
            if (DisturbanceNoise > 0)
                disturbance += new Vec3d(Gaussian(), Gaussian(), Gaussian()) * DisturbanceNoise;

            Vec3d hTotal = Vec3d.Scale(Inertia, BodyRate) + wheelMomentum;
            Vec3d torque = externalTorque + disturbance - Vec3d.Cross(BodyRate, hTotal) - wheelTorqueOnWheels;
            Vec3d wDot = new Vec3d(torque.x / Inertia.x, torque.y / Inertia.y, torque.z / Inertia.z);
            BodyRate += wDot * dt;
            Attitude = Attitude.Integrate(BodyRate, dt);
        }

        public void SetOrbit(KeplerOrbit orbit)
        {
            Orbit = orbit;
            UpdateEnvironment();
        }

        /// <summary>Instant velocity change (m/s, ECI).</summary>
        public void ApplyDeltaV(Vec3d dvMetersPerSecond)
        {
            Orbit.ApplyDeltaV(Time, dvMetersPerSecond / 1000.0);
            UpdateEnvironment();
        }

        private void UpdateEnvironment()
        {
            Vec3d r, v;
            Orbit.StateAt(Time, out r, out v);
            Position = r;
            Velocity = v;
            SunEci = OpsMath.SunDirection(Time);
            InSun = !OpsMath.InEclipse(r, SunEci);
            MagneticFieldEci = OpsMath.MagneticField(r, Time);
            double lat, lon;
            OpsMath.LatLon(r, Time, out lat, out lon);
            LatitudeDeg = lat;
            LongitudeDeg = lon;
        }

        public double Gaussian()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(OpsMath.TwoPi * u2);
        }

        public double Uniform() { return _random.NextDouble(); }

        /// <summary>Position at an arbitrary time (for pass prediction / ground track).</summary>
        public Vec3d PositionAt(double t)
        {
            Vec3d r, v;
            Orbit.StateAt(t, out r, out v);
            return r;
        }
    }
}
