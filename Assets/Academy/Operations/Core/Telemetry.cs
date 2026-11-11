using System;
using System.Collections.Generic;
using System.Globalization;

namespace Academy.Operations
{
    public sealed class TelemetryPoint
    {
        public string Key;
        public string Group;
        public string Label;
        public string Unit;
        public double Value;
        /// <summary>When set, shown instead of the number (e.g. "SUNSAFE").</summary>
        public string Text;
        public int Decimals = 1;
        public double Low = double.NaN;
        public double High = double.NaN;

        public bool OutOfLimits
        {
            get { return (!double.IsNaN(Low) && Value < Low) || (!double.IsNaN(High) && Value > High); }
        }

        public string Display
        {
            get
            {
                if (!string.IsNullOrEmpty(Text))
                    return Text;
                string number = Value.ToString("F" + Math.Max(0, Decimals), CultureInfo.InvariantCulture);
                return string.IsNullOrEmpty(Unit) ? number : number + " " + Unit;
            }
        }

        public TelemetryPoint Clone()
        {
            return (TelemetryPoint)MemberwiseClone();
        }
    }

    /// <summary>Ordered key/value telemetry. Keys look like "eps.battery.soc".</summary>
    public sealed class TelemetryStore
    {
        private readonly Dictionary<string, TelemetryPoint> _points = new Dictionary<string, TelemetryPoint>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TelemetryPoint> _ordered = new List<TelemetryPoint>();

        public IList<TelemetryPoint> Points { get { return _ordered; } }

        /// <summary>Set (or create) a numeric point. Limits: NaN = none.</summary>
        public TelemetryPoint Set(string key, string group, string label, double value, string unit = "", int decimals = 1,
                                  double low = double.NaN, double high = double.NaN)
        {
            TelemetryPoint p;
            if (!_points.TryGetValue(key, out p))
            {
                p = new TelemetryPoint { Key = key, Group = group, Label = label, Unit = unit, Decimals = decimals, Low = low, High = high };
                _points[key] = p;
                _ordered.Add(p);
            }
            p.Value = value;
            p.Text = null;
            return p;
        }

        /// <summary>Set a text point; <paramref name="value"/> is the numeric code rules and scenarios can test.</summary>
        public TelemetryPoint SetText(string key, string group, string label, string text, double value)
        {
            TelemetryPoint p = Set(key, group, label, value);
            p.Text = text;
            return p;
        }

        public bool TryGet(string key, out TelemetryPoint point)
        {
            return _points.TryGetValue(key, out point);
        }

        public double Get(string key, double fallback = double.NaN)
        {
            TelemetryPoint p;
            return _points.TryGetValue(key, out p) ? p.Value : fallback;
        }

        public void Remove(string keyPrefix)
        {
            _ordered.RemoveAll(p => p.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
            var remove = new List<string>();
            foreach (string k in _points.Keys)
                if (k.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
                    remove.Add(k);
            foreach (string k in remove)
                _points.Remove(k);
        }

        /// <summary>Copy every point's current value into <paramref name="target"/> (ground snapshot).</summary>
        public void CopyTo(TelemetryStore target)
        {
            foreach (TelemetryPoint p in _ordered)
            {
                TelemetryPoint t;
                if (!target._points.TryGetValue(p.Key, out t))
                {
                    t = p.Clone();
                    target._points[p.Key] = t;
                    target._ordered.Add(t);
                }
                else
                {
                    t.Value = p.Value;
                    t.Text = p.Text;
                }
            }
        }
    }

    public enum EventSeverity { Info, Warning, Alarm }

    public sealed class SimEvent
    {
        public double Met;
        public EventSeverity Severity;
        public string Source;
        public string Text;
    }

    public sealed class EventLog
    {
        public const int Capacity = 400;
        private readonly List<SimEvent> _events = new List<SimEvent>();
        public IList<SimEvent> Events { get { return _events; } }
        public int Version { get; private set; }
        public event Action<SimEvent> Added;

        public void Add(double met, EventSeverity severity, string source, string text)
        {
            var e = new SimEvent { Met = met, Severity = severity, Source = source, Text = text };
            _events.Add(e);
            if (_events.Count > Capacity)
                _events.RemoveAt(0);
            Version++;
            if (Added != null)
                Added(e);
        }
    }

    /// <summary>A command a component accepts, for building the ground-station buttons.</summary>
    public sealed class CommandSpec
    {
        public string Name;
        /// <summary>Choices separated by '|', or a placeholder like "&lt;seconds&gt;", or empty.</summary>
        public string Args;
        public string Help;

        public CommandSpec(string name, string args, string help) { Name = name; Args = args; Help = help; }
    }

    /// <summary>A fault the instructor can inject into a component.</summary>
    public sealed class FaultSpec
    {
        public string Name;
        public string Args;
        public string Help;

        public FaultSpec(string name, string args, string help) { Name = name; Args = args; Help = help; }
    }

    public static class Fmt
    {
        public static string Met(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return string.Format(CultureInfo.InvariantCulture, "T+{0}d {1:00}:{2:00}:{3:00}", (int)ts.TotalDays, ts.Hours, ts.Minutes, ts.Seconds);
        }

        public static string Duration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m", (int)ts.TotalHours, ts.Minutes);
            return string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", ts.Minutes, ts.Seconds);
        }

        public static double ParseDouble(string s, double fallback)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
