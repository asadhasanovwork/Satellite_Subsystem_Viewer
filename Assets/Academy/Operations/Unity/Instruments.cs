using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Academy.Operations
{
    /// <summary>Helpers for Painter2D instruments.</summary>
    internal static class Draw2D
    {
        public static void Circle(Painter2D p, Vector2 c, float r, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();
        }

        public static void Ring(Painter2D p, Vector2 c, float r, Color stroke, float width)
        {
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Stroke();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, Color c, float width)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        public static void Rect(Painter2D p, Rect r, Color fill)
        {
            if (r.width <= 0 || r.height <= 0) return;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Fill();
        }

        public static void RectOutline(Painter2D p, Rect r, Color stroke, float width)
        {
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Stroke();
        }

        public static void Diamond(Painter2D p, Vector2 c, float s, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(c + new Vector2(0, -s));
            p.LineTo(c + new Vector2(s, 0));
            p.LineTo(c + new Vector2(0, s));
            p.LineTo(c + new Vector2(-s, 0));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>A pool of absolutely positioned labels inside an element.</summary>
        public static Label PooledLabel(VisualElement owner, List<Label> pool, int index, int size, Color color, bool mono)
        {
            while (pool.Count <= index)
            {
                Label l = mono ? OpsStyle.Number("", size, color) : OpsStyle.Label("", size, false, color);
                l.style.position = Position.Absolute;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.pickingMode = PickingMode.Ignore;
                owner.Add(l);
                pool.Add(l);
            }
            Label label = pool[index];
            label.style.display = DisplayStyle.Flex;
            label.style.color = color;
            label.style.fontSize = size;
            return label;
        }

        public static void HideFrom(List<Label> pool, int index)
        {
            for (int i = index; i < pool.Count; i++) pool[i].style.display = DisplayStyle.None;
        }
    }

    // ======================================================================================== timeline

    /// <summary>
    /// The planning ribbon: ground-station passes (teal, upper lane) and eclipses (violet, lower lane) for the
    /// next few orbits, stored commands (amber diamonds) and a "now" cursor.
    /// </summary>
    public sealed class TimelineElement : VisualElement
    {
        private readonly OperationsSimulation _sim;
        private readonly List<Label> _labels = new List<Label>();
        private const float PastFraction = 0.04f;

        public TimelineElement(OperationsSimulation sim)
        {
            _sim = sim;
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
            style.height = 46;
            style.backgroundColor = OpsStyle.Hex(0x0B1524);
            style.borderBottomWidth = 1;
            style.borderBottomColor = OpsStyle.Line;
        }

        private float X(Rect r, double t, double t0, double span) { return r.x + (float)((t - t0) / span) * r.width; }

        private void Window(out double t0, out double span)
        {
            span = Math.Max(600, _sim.ForecastHorizon);
            t0 = _sim.Sim.Env.Time - span * PastFraction;
        }

        /// <summary>Position labels (call before MarkDirtyRepaint, not from the draw callback).</summary>
        public void Refresh()
        {
            if (_sim.Sim == null) { Drawn2DHide(); MarkDirtyRepaint(); return; }
            Rect r = contentRect;
            if (r.width < 50) return;
            double t0, span;
            Window(out t0, out span);
            int li = 0;

            // Time ticks
            double tick = span <= 5 * 3600 ? 1800 : (span <= 13 * 3600 ? 3600 : 3 * 3600);
            double now = _sim.Sim.Env.Time;
            for (double k = tick; now + k <= t0 + span; k += tick)
            {
                float x = X(r, now + k, t0, span);
                Label l = Draw2D.PooledLabel(this, _labels, li++, 9, OpsStyle.Faint, true);
                l.text = "+" + (k >= 3600 ? (k / 3600).ToString("0.#") + "h" : (k / 60).ToString("0") + "m");
                l.style.left = x + 2;
                l.style.top = r.height - 12;
            }

            // Pass labels
            foreach (ForecastWindow w in _sim.Passes)
            {
                float a = Mathf.Max(X(r, w.Start, t0, span), X(r, now, t0, span));
                float b = X(r, w.End, t0, span);
                if (b - a < 46) continue;
                Label l = Draw2D.PooledLabel(this, _labels, li++, 10, OpsStyle.Ink, false);
                l.text = w.Label;
                l.style.left = a + 4;
                l.style.top = 5;
            }

            // Lane captions
            Label passCaption = Draw2D.PooledLabel(this, _labels, li++, 9, OpsStyle.Faint, false);
            passCaption.text = "passes";
            passCaption.style.left = r.width - 60;
            passCaption.style.top = 5;
            Label eclCaption = Draw2D.PooledLabel(this, _labels, li++, 9, OpsStyle.Faint, false);
            eclCaption.text = "eclipses";
            eclCaption.style.left = r.width - 60;
            eclCaption.style.top = 21;

            Draw2D.HideFrom(_labels, li);
            MarkDirtyRepaint();
        }

        private void Drawn2DHide() { Draw2D.HideFrom(_labels, 0); }

        private void Draw(MeshGenerationContext ctx)
        {
            if (_sim.Sim == null) return;
            Rect r = contentRect;
            if (r.width < 50) return;
            Painter2D p = ctx.painter2D;
            double t0, span;
            Window(out t0, out span);
            double now = _sim.Sim.Env.Time;

            float passTop = 4, passH = 14, eclTop = 21, eclH = 10;
            // Sunlight baseline under the eclipse lane
            Draw2D.Rect(p, new Rect(r.x, eclTop + eclH * 0.5f - 1, r.width, 2), OpsStyle.WithAlpha(OpsStyle.Sun, 0.35f));

            foreach (ForecastWindow w in _sim.Eclipses)
            {
                float a = X(r, w.Start, t0, span), b = X(r, w.End, t0, span);
                Draw2D.Rect(p, new Rect(a, eclTop, Mathf.Max(2, b - a), eclH), OpsStyle.Night);
            }
            foreach (ForecastWindow w in _sim.Passes)
            {
                float a = X(r, w.Start, t0, span), b = X(r, w.End, t0, span);
                Draw2D.Rect(p, new Rect(a, passTop, Mathf.Max(3, b - a), passH), OpsStyle.Ground);
            }

            // Stored commands
            foreach (ScheduledCommand c in _sim.Sim.Obsw.Schedule)
            {
                if (c.Done) continue;
                double t = _sim.Sim.Env.StartTime + c.Met;
                if (t < t0 || t > t0 + span) continue;
                Draw2D.Diamond(p, new Vector2(X(r, t, t0, span), eclTop + eclH + 6), 4, OpsStyle.Sun);
            }

            // Hour ticks
            double tick = span <= 5 * 3600 ? 1800 : (span <= 13 * 3600 ? 3600 : 3 * 3600);
            for (double k = tick; now + k <= t0 + span; k += tick)
            {
                float x = X(r, now + k, t0, span);
                Draw2D.Line(p, new Vector2(x, r.height - 12), new Vector2(x, r.height - 2), OpsStyle.Line, 1);
            }

            // Now cursor
            float nx = X(r, now, t0, span);
            Draw2D.Rect(p, new Rect(r.x, r.y, nx - r.x, r.height), OpsStyle.WithAlpha(OpsStyle.Ink, 0.55f));
            Draw2D.Line(p, new Vector2(nx, 0), new Vector2(nx, r.height), OpsStyle.Text, 1.5f);
        }
    }

    // ======================================================================================== sparkline

    /// <summary>Small trend line of a value; draws a zero line when the range crosses zero.</summary>
    public sealed class SparklineElement : VisualElement
    {
        private readonly float[] _values;
        private int _count, _head;
        public Color LineColor = OpsStyle.Ground;
        public float FixedMin = float.NaN, FixedMax = float.NaN;

        public SparklineElement(int capacity = 90)
        {
            _values = new float[capacity];
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        public void Push(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return;
            _values[_head] = v;
            _head = (_head + 1) % _values.Length;
            _count = Math.Min(_count + 1, _values.Length);
            MarkDirtyRepaint();
        }

        public void ResetValues() { _count = 0; _head = 0; MarkDirtyRepaint(); }

        private float At(int i) { return _values[(_head - _count + i + _values.Length) % _values.Length]; }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (_count < 2 || r.width < 4) return;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _count; i++) { float v = At(i); min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            if (!float.IsNaN(FixedMin)) min = Mathf.Min(min, FixedMin);
            if (!float.IsNaN(FixedMax)) max = Mathf.Max(max, FixedMax);
            if (max - min < 1e-4f) { max += 0.5f; min -= 0.5f; }

            Painter2D p = ctx.painter2D;
            Func<float, float> y = v => r.yMax - 2 - (v - min) / (max - min) * (r.height - 4);
            if (min < 0 && max > 0)
                Draw2D.Line(p, new Vector2(r.xMin, y(0)), new Vector2(r.xMax, y(0)), OpsStyle.Line, 1);

            p.strokeColor = LineColor;
            p.lineWidth = 1.5f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < _count; i++)
            {
                var pt = new Vector2(r.xMin + r.width * i / (_values.Length - 1), y(At(i)));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
            Draw2D.Circle(p, new Vector2(r.xMin + r.width * (_count - 1) / (_values.Length - 1), y(At(_count - 1))), 2.5f, LineColor);
        }
    }

    // ======================================================================================== attitude scope

    /// <summary>
    /// "Payload's-eye view": the centre is where the payload (camera / antenna) points. Rings at 30, 90 (the
    /// payload's horizon) and 180 degrees. The Earth's disk and the Sun are drawn where they appear from the
    /// satellite, so pointing at the Earth means the Earth disk sits on the cross.
    /// </summary>
    public sealed class AttitudeScopeElement : VisualElement
    {
        private readonly OperationsSimulation _sim;
        private readonly List<Label> _labels = new List<Label>();

        public AttitudeScopeElement(OperationsSimulation sim)
        {
            _sim = sim;
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        private bool Project(Vec3d dirBody, Vector2 c, float R, out Vector2 pos, out float angleDeg)
        {
            AttitudeControl a = _sim.Sim.Adcs;
            Vec3d b = a.NadirAxis.Normalized;
            Vec3d up = a.SecondaryAxis - b * Vec3d.Dot(a.SecondaryAxis, b);
            if (up.SqrLength < 1e-9) up = b.AnyPerpendicular();
            up = up.Normalized;
            Vec3d right = Vec3d.Cross(up, b).Normalized;
            Vec3d d = dirBody.Normalized;
            angleDeg = (float)Vec3d.AngleDeg(d, b);
            var flat = new Vector2((float)Vec3d.Dot(d, right), -(float)Vec3d.Dot(d, up));
            if (flat.sqrMagnitude < 1e-10f) { pos = c; return true; }
            pos = c + flat.normalized * (angleDeg / 180f * R);
            return angleDeg <= 90;
        }

        public void Refresh()
        {
            Rect r = contentRect;
            float R = Mathf.Min(r.width, r.height) * 0.5f - 12;
            Vector2 c = r.center;
            int li = 0;
            Label h = Draw2D.PooledLabel(this, _labels, li++, 9, OpsStyle.Faint, false);
            h.text = "payload horizon";
            h.style.left = c.x - 34; h.style.top = c.y - R * 0.5f - 13;
            if (_sim.Sim != null)
            {
                Label rate = Draw2D.PooledLabel(this, _labels, li++, 10, OpsStyle.Muted, true);
                rate.text = _sim.Sim.Env.RateDegPerSec.ToString("0.00") + " deg/s";
                rate.style.left = 4; rate.style.top = r.height - 14;
                Label err = Draw2D.PooledLabel(this, _labels, li++, 10, OpsStyle.Muted, true);
                err.text = "off-Earth " + _sim.Sim.Env.AngleToNadirDeg(_sim.Sim.Adcs.NadirAxis).ToString("0.0") + " deg";
                err.style.left = 4; err.style.top = 2;
            }
            Draw2D.HideFrom(_labels, li);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 40) return;
            Painter2D p = ctx.painter2D;
            float R = Mathf.Min(r.width, r.height) * 0.5f - 12;
            Vector2 c = r.center;

            Draw2D.Circle(p, c, R, OpsStyle.Hex(0x0B1524));
            Draw2D.Circle(p, c, R * 0.5f, OpsStyle.Hex(0x102036));
            Draw2D.Ring(p, c, R * 0.5f, OpsStyle.Line, 1.5f);
            Draw2D.Ring(p, c, R * 30f / 180f, OpsStyle.Line, 1);
            Draw2D.Ring(p, c, R, OpsStyle.Line, 1);
            Draw2D.Line(p, c + new Vector2(-8, 0), c + new Vector2(8, 0), OpsStyle.Muted, 1);
            Draw2D.Line(p, c + new Vector2(0, -8), c + new Vector2(0, 8), OpsStyle.Muted, 1);

            if (_sim.Sim == null) return;
            SpaceEnvironment env = _sim.Sim.Env;

            // Earth disk (angular radius depends on altitude)
            Vector2 earth;
            float earthAngle;
            Project(env.NadirBody, c, R, out earth, out earthAngle);
            float earthRadiusDeg = (float)(Math.Asin(Math.Min(1, OpsMath.EarthRadius / env.Position.Length)) * OpsMath.Rad2Deg);
            Draw2D.Circle(p, earth, Mathf.Max(4, earthRadiusDeg / 180f * R), OpsStyle.WithAlpha(OpsStyle.Ground, 0.35f));
            Draw2D.Ring(p, earth, Mathf.Max(4, earthRadiusDeg / 180f * R), OpsStyle.Ground, 1.5f);

            // Direction of flight
            Vector2 ram;
            float ramAngle;
            Project(env.Attitude.InverseRotate(env.Velocity.Normalized), c, R, out ram, out ramAngle);
            Draw2D.Ring(p, ram, 4, OpsStyle.Text, 1.2f);

            // Sun
            Vector2 sun;
            float sunAngle;
            Project(env.SunBody, c, R, out sun, out sunAngle);
            if (env.InSun)
            {
                Draw2D.Circle(p, sun, 6, OpsStyle.Sun);
                Draw2D.Ring(p, sun, 9, OpsStyle.WithAlpha(OpsStyle.Sun, 0.4f), 1.5f);
            }
            else
            {
                Draw2D.Ring(p, sun, 6, OpsStyle.Night, 1.5f);
            }
        }
    }

    // ======================================================================================== power flow

    /// <summary>
    /// Live power flow: solar arrays and battery feed the bus, the bus feeds the loads. Particles move along each
    /// path; more particles = more watts. The battery shows its charge level.
    /// </summary>
    public sealed class PowerFlowElement : VisualElement
    {
        public Func<string, double> Value;   // telemetry getter (NaN = no data)
        private readonly List<Label> _labels = new List<Label>();
        private double _gen, _load, _net, _soc;
        private bool _hasData;
        private double _scale = 1;

        public PowerFlowElement()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        private struct Layout
        {
            public Rect Solar, Battery, Bus, Loads;
        }

        private Layout Compute(Rect r)
        {
            float w = r.width, h = r.height;
            var l = new Layout
            {
                Solar = new Rect(r.x + 6, r.y + 18, w * 0.26f, h * 0.28f),
                Battery = new Rect(r.x + 6, r.y + h * 0.58f, w * 0.26f, h * 0.34f),
                Bus = new Rect(r.x + w * 0.45f, r.y + h * 0.36f, w * 0.12f, h * 0.30f),
                Loads = new Rect(r.x + w * 0.70f, r.y + h * 0.30f, w * 0.28f, h * 0.40f)
            };
            return l;
        }

        public void Refresh()
        {
            if (Value != null)
            {
                _gen = Value("eps.generation");
                _load = Value("eps.load");
                _net = Value("eps.net");
                _soc = Value("eps.battery.soc");
                _hasData = !double.IsNaN(_gen) && !double.IsNaN(_load);
                if (_hasData) _scale = Math.Max(_scale * 0.995, Math.Max(_gen, _load));
            }
            Rect r = contentRect;
            Layout l = Compute(r);
            int li = 0;
            Place(li++, "Solar", Fmt(_gen, "W"), l.Solar, OpsStyle.Sun);
            Place(li++, "Battery", double.IsNaN(_soc) ? "-" : _soc.ToString("0") + "%", l.Battery, BatteryColor());
            Place(li++, "Loads", Fmt(_load, "W"), l.Loads, OpsStyle.Text);
            Label net = Draw2D.PooledLabel(this, _labels, li * 2, 10, NetColor(), true);
            net.text = !_hasData ? "" : (_net >= 0 ? "charging " : "draining ") + Math.Abs(_net).ToString("0.#") + " W";
            net.style.left = l.Battery.xMax + 6;
            net.style.top = l.Battery.yMax - 14;
            Draw2D.HideFrom(_labels, li * 2 + 1);
            MarkDirtyRepaint();
        }

        private Color BatteryColor()
        {
            if (double.IsNaN(_soc)) return OpsStyle.Faint;
            return _soc < 20 ? OpsStyle.Alarm : (_soc < 40 ? OpsStyle.Caution : OpsStyle.Ground);
        }

        private Color NetColor() { return _net >= 0 ? OpsStyle.Ground : OpsStyle.Caution; }

        private static string Fmt(double v, string unit) { return double.IsNaN(v) ? "-" : v.ToString(v < 100 ? "0.0" : "0") + " " + unit; }

        private void Place(int i, string title, string value, Rect box, Color c)
        {
            Label t = Draw2D.PooledLabel(this, _labels, i * 2, 9, OpsStyle.Muted, false);
            t.text = title;
            t.style.left = box.x;
            t.style.top = box.y - 13;
            Label v = Draw2D.PooledLabel(this, _labels, i * 2 + 1, 12, c, true);
            v.text = value;
            v.style.left = box.x + 5;
            v.style.top = box.y + box.height * 0.5f - 8;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 80) return;
            Painter2D p = ctx.painter2D;
            Layout l = Compute(r);
            float t = Time.unscaledTime;

            // Paths
            Vector2 busIn = new Vector2(l.Bus.xMin, l.Bus.center.y);
            Vector2 busOut = new Vector2(l.Bus.xMax, l.Bus.center.y);
            Vector2 solarOut = new Vector2(l.Solar.xMax, l.Solar.center.y);
            Vector2 batteryPort = new Vector2(l.Battery.xMax, l.Battery.center.y);
            Vector2 loadsIn = new Vector2(l.Loads.xMin, l.Loads.center.y);

            Flow(p, solarOut, busIn, _gen, OpsStyle.Sun, t);
            if (_net >= 0) Flow(p, busIn + new Vector2(0, 6), batteryPort, _net, OpsStyle.Ground, t);
            else Flow(p, batteryPort, busIn + new Vector2(0, 6), -_net, OpsStyle.Caution, t);
            Flow(p, busOut, loadsIn, _load, OpsStyle.Text, t);

            // Nodes
            Draw2D.Rect(p, l.Solar, OpsStyle.WithAlpha(OpsStyle.Sun, 0.12f));
            Draw2D.RectOutline(p, l.Solar, OpsStyle.WithAlpha(OpsStyle.Sun, 0.6f), 1);
            Draw2D.Rect(p, l.Bus, OpsStyle.Raised);
            Draw2D.RectOutline(p, l.Bus, OpsStyle.Line, 1);
            Draw2D.Rect(p, l.Loads, OpsStyle.Raised);
            Draw2D.RectOutline(p, l.Loads, OpsStyle.Line, 1);

            // Battery with charge level
            Draw2D.Rect(p, l.Battery, OpsStyle.Hex(0x0B1524));
            if (!double.IsNaN(_soc))
            {
                float f = Mathf.Clamp01((float)(_soc / 100.0));
                var fill = new Rect(l.Battery.x, l.Battery.yMax - l.Battery.height * f, l.Battery.width, l.Battery.height * f);
                Draw2D.Rect(p, fill, OpsStyle.WithAlpha(BatteryColor(), 0.35f));
            }
            Draw2D.RectOutline(p, l.Battery, BatteryColor(), 1.2f);
            Draw2D.Rect(p, new Rect(l.Battery.center.x - 6, l.Battery.y - 4, 12, 4), BatteryColor());
        }

        private void Flow(Painter2D p, Vector2 a, Vector2 b, double watts, Color c, float time)
        {
            Draw2D.Line(p, a, b, OpsStyle.WithAlpha(c, 0.18f), 2);
            if (!_hasData || double.IsNaN(watts) || watts < 0.01) return;
            int n = Mathf.Clamp(Mathf.CeilToInt((float)(watts / Math.Max(1e-6, _scale) * 7)), 1, 7);
            float phase = (time * 0.6f) % 1f;
            for (int i = 0; i < n; i++)
            {
                float f = (phase + i / (float)n) % 1f;
                Draw2D.Circle(p, Vector2.Lerp(a, b, f), 2.6f, c);
            }
        }
    }

    // ======================================================================================== orbit rings

    /// <summary>Mission board illustration: Earth with the three orbit families, each with a moving satellite.</summary>
    public sealed class OrbitRingsElement : VisualElement
    {
        private readonly List<Label> _labels = new List<Label>();
        public string Highlight = "";

        private static readonly string[] Names = { "LEO", "MEO", "GEO" };
        private static readonly string[] Heights = { "500 km", "23,000 km", "35,786 km" };
        private static readonly float[] Radius = { 0.36f, 0.66f, 0.92f };
        private static readonly float[] Speed = { 0.30f, 0.09f, 0.04f };

        public OrbitRingsElement()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        public void Refresh()
        {
            Rect r = contentRect;
            float R = Mathf.Min(r.width, r.height) * 0.5f - 8;
            Vector2 c = r.center;
            for (int i = 0; i < 3; i++)
            {
                bool hl = Highlight == Names[i];
                Label l = Draw2D.PooledLabel(this, _labels, i, hl ? 13 : 11, hl ? OpsStyle.OrbitColor(Names[i]) : OpsStyle.Muted, false);
                l.text = Names[i] + "  " + Heights[i];
                l.style.left = c.x + R * Radius[i] * 0.70f + 4;
                l.style.top = c.y - R * Radius[i] * 0.70f - 14;
            }
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 40) return;
            Painter2D p = ctx.painter2D;
            float R = Mathf.Min(r.width, r.height) * 0.5f - 8;
            Vector2 c = r.center;
            float t = Time.unscaledTime;

            // Earth to scale with LEO (Earth radius 6378 km; LEO ring just outside it)
            float earthR = R * Radius[0] * 0.80f;
            Draw2D.Circle(p, c, earthR, OpsStyle.Hex(0x16406A));
            Draw2D.Ring(p, c, earthR, OpsStyle.WithAlpha(OpsStyle.Ground, 0.5f), 1);
            // night side
            p.fillColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0.55f);
            p.BeginPath();
            p.Arc(c, earthR, Angle.Degrees(90), Angle.Degrees(270));
            p.ClosePath();
            p.Fill();

            for (int i = 0; i < 3; i++)
            {
                bool hl = Highlight == Names[i];
                Color col = OpsStyle.OrbitColor(Names[i]);
                Draw2D.Ring(p, c, R * Radius[i], hl ? col : OpsStyle.WithAlpha(col, 0.35f), hl ? 2f : 1f);
                float a = t * Speed[i] * Mathf.PI * 2 + i * 2.1f;
                Draw2D.Circle(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * Radius[i], hl ? 5 : 3.5f, col);
            }
        }
    }
}
