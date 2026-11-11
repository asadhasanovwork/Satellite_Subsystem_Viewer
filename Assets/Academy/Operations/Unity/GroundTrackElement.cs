using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Academy.Operations
{
    /// <summary>
    /// 2D map (equirectangular): latitude/longitude grid, ground track (past and next orbit), satellite,
    /// sub-solar point, ground stations and the area each station can see.
    /// </summary>
    public sealed class GroundTrackElement : VisualElement
    {
        private readonly OperationsSimulation _sim;
        private readonly List<Label> _stationLabels = new List<Label>();

        private static readonly Color Ocean = OpsStyle.Hex(0x0B1524);
        private static readonly Color GridColor = OpsStyle.Line;
        private static readonly Color EquatorColor = OpsStyle.Hex(0x3A5A80);
        private static readonly Color PastColor = OpsStyle.Text;
        private static readonly Color FutureColor = OpsStyle.WithAlpha(OpsStyle.Text, 0.3f);
        private static readonly Color StationColor = OpsStyle.Ground;
        private static readonly Color CoverageColor = OpsStyle.WithAlpha(OpsStyle.Ground, 0.5f);
        private static readonly Color SatelliteColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SunColor = OpsStyle.Sun;

        public GroundTrackElement(OperationsSimulation sim)
        {
            _sim = sim;
            generateVisualContent += Draw;
            style.backgroundColor = Ocean;
            pickingMode = PickingMode.Ignore;
        }

        public void RebuildLabels()
        {
            foreach (Label l in _stationLabels) l.RemoveFromHierarchy();
            _stationLabels.Clear();
            if (_sim.Sim == null) return;
            foreach (GroundStationDef s in _sim.Sim.Link.Stations)
            {
                var l = OpsStyle.Label(s.name, 9, false, StationColor);
                l.style.position = Position.Absolute;
                l.pickingMode = PickingMode.Ignore;
                l.userData = s;
                Add(l);
                _stationLabels.Add(l);
            }
        }

        /// <summary>Place the station labels (call before MarkDirtyRepaint, not from the draw callback).</summary>
        public void Refresh()
        {
            Rect r = contentRect;
            if (_sim.Sim != null && r.width > 10)
            {
                int i = 0;
                foreach (GroundStationDef s in _sim.Sim.Link.Stations)
                {
                    if (i >= _stationLabels.Count) break;
                    Vector2 pos = ToMap(r, (float)s.lonDeg, (float)s.latDeg);
                    _stationLabels[i].style.left = pos.x + 4;
                    _stationLabels[i].style.top = pos.y - 7;
                    i++;
                }
            }
            MarkDirtyRepaint();
        }

        private Vector2 ToMap(Rect r, float lon, float lat)
        {
            return new Vector2(r.x + (lon + 180f) / 360f * r.width, r.y + (90f - lat) / 180f * r.height);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 10 || r.height < 10) return;
            Painter2D p = ctx.painter2D;

            // Grid every 30 degrees
            p.lineWidth = 1f;
            p.strokeColor = GridColor;
            for (int lon = -150; lon <= 150; lon += 30)
            {
                p.BeginPath(); p.MoveTo(ToMap(r, lon, 90)); p.LineTo(ToMap(r, lon, -90)); p.Stroke();
            }
            for (int lat = -60; lat <= 60; lat += 30)
            {
                p.BeginPath(); p.MoveTo(ToMap(r, -180, lat)); p.LineTo(ToMap(r, 180, lat)); p.Stroke();
            }
            p.strokeColor = EquatorColor;
            p.BeginPath(); p.MoveTo(ToMap(r, -180, 0)); p.LineTo(ToMap(r, 180, 0)); p.Stroke();

            SatelliteSim sim = _sim.Sim;
            if (sim == null) return;

            // Station coverage circles (where the satellite is above 5 deg elevation)
            double rSat = sim.Env.Position.Length;
            foreach (GroundStationDef s in sim.Link.Stations)
            {
                double eps = s.minElevationDeg * OpsMath.Deg2Rad;
                double psi = Math.Acos(Math.Min(1, OpsMath.EarthRadius * Math.Cos(eps) / rSat)) - eps; // central angle
                p.strokeColor = CoverageColor;
                p.lineWidth = 1.2f;
                DrawSmallCircle(p, r, s.latDeg, s.lonDeg, psi * OpsMath.Rad2Deg);

                Vector2 pos = ToMap(r, (float)s.lonDeg, (float)s.latDeg);
                p.fillColor = StationColor;
                p.BeginPath(); p.Arc(pos, 3f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }

            // Ground track: next orbit (faint) then the past (bright)
            p.lineWidth = 1.5f;
            p.strokeColor = FutureColor;
            DrawPolyline(p, r, _sim.TrackPrediction);
            p.strokeColor = PastColor;
            DrawPolyline(p, r, _sim.TrackHistory);

            // Sub-solar point
            double sunLat, sunLon;
            OpsMath.LatLon(sim.Env.SunEci * (OpsMath.EarthRadius * 10), sim.Env.Time, out sunLat, out sunLon);
            p.fillColor = SunColor;
            p.BeginPath(); p.Arc(ToMap(r, (float)sunLon, (float)sunLat), 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();

            // Satellite
            Vector2 sat = ToMap(r, (float)sim.Env.LongitudeDeg, (float)sim.Env.LatitudeDeg);
            p.fillColor = sim.Link.Contact ? OpsStyle.Ground : SatelliteColor;
            p.BeginPath(); p.Arc(sat, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.strokeColor = Color.black;
            p.lineWidth = 1f;
            p.BeginPath(); p.Arc(sat, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
        }

        private void DrawPolyline(Painter2D p, Rect r, List<Vector2> lonLat)
        {
            if (lonLat.Count < 2) return;
            p.BeginPath();
            p.MoveTo(ToMap(r, lonLat[0].x, lonLat[0].y));
            for (int i = 1; i < lonLat.Count; i++)
            {
                Vector2 a = lonLat[i - 1], b = lonLat[i];
                if (Mathf.Abs(b.x - a.x) > 180f)
                {
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(ToMap(r, b.x, b.y));
                }
                else
                {
                    p.LineTo(ToMap(r, b.x, b.y));
                }
            }
            p.Stroke();
        }

        /// <summary>Circle of a given angular radius around a point on the sphere, drawn in lat/lon.</summary>
        private void DrawSmallCircle(Painter2D p, Rect r, double latDeg, double lonDeg, double radiusDeg)
        {
            var pts = new List<Vector2>();
            double lat1 = latDeg * OpsMath.Deg2Rad, lon1 = lonDeg * OpsMath.Deg2Rad, d = radiusDeg * OpsMath.Deg2Rad;
            for (int i = 0; i <= 72; i++)
            {
                double brg = i * 5 * OpsMath.Deg2Rad;
                double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brg));
                double lon2 = lon1 + Math.Atan2(Math.Sin(brg) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
                pts.Add(new Vector2((float)OpsMath.WrapDeg180(lon2 * OpsMath.Rad2Deg), (float)(lat2 * OpsMath.Rad2Deg)));
            }
            DrawPolyline(p, r, pts);
        }
    }
}
