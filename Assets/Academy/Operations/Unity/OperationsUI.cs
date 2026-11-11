using System;
using System.Collections.Generic;
using System.Globalization;
using Academy.Builder;
using Academy.Prototype;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Academy.Operations
{
    /// <summary>
    /// Mission operations console ("night shift in the flight dynamics room"), built in code with UI Toolkit.
    /// Header with clock, time control, contact and master alarm; a planning ribbon of passes and eclipses;
    /// annunciator lamps, vitals and telemetry on the left; an instrument dock (ground track, payload's-eye
    /// view, power flow) under the 3D view; command line with suggestions, plan, rules and log on the right;
    /// mission board, briefing and debrief screens; instructor drawer.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OperationsUI : MonoBehaviour
    {
        public OperationsSimulation simulation;
        public PrototypeOrbitCamera orbitCamera;
        public string mainMenuScene = "Test_Demo";

        private UIDocument _document;
        private VisualElement _root, _console, _board, _modal, _instructor;

        private float _slowTimer;
        private bool _built;

        // ================================================================== lifecycle

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (simulation == null) simulation = FindFirstObjectByType<OperationsSimulation>();
            if (orbitCamera == null) orbitCamera = FindFirstObjectByType<PrototypeOrbitCamera>();
        }

        private void OnEnable()
        {
            PrototypeOrbitCamera.PointerOverUi = IsPointerOverUi;
            if (simulation != null)
            {
                simulation.SimulationStarted -= OnSimulationStarted;
                simulation.SimulationStarted += OnSimulationStarted;
            }
        }

        private void OnDisable()
        {
            if (PrototypeOrbitCamera.PointerOverUi == (Func<Vector2, bool>)IsPointerOverUi)
                PrototypeOrbitCamera.PointerOverUi = null;
            if (simulation != null) simulation.SimulationStarted -= OnSimulationStarted;
        }

        private void Start()
        {
            BuildLayout();
            _built = true;
            if (simulation != null && simulation.Sim != null)
                OnSimulationStarted();
            else
                ShowBoard();
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (_root == null || _root.panel == null) return false;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return _root.panel.Pick(p) != null;
        }

        private void Update()
        {
            if (!_built) return;
            HandleKeys();

            if (_boardVisible)
                _rings.Refresh();

            if (simulation == null || simulation.Sim == null)
                return;

            // Every frame: animated instruments and blinking
            _power.Refresh();
            Blink();
            UpdateToasts();

            _slowTimer -= Time.unscaledDeltaTime;
            if (_slowTimer > 0) return;
            _slowTimer = 0.25f;

            RefreshHeader();
            RefreshTelemetry();
            RefreshAnnunciators();
            RefreshVitals();
            RefreshLessonCard();
            RefreshLog();
            RefreshPlan();
            RefreshRules();
            RefreshUplinkNote();
            _timeline.Refresh();
            _map.Refresh();
            _scope.Refresh();
            CheckDebrief();
        }

        // ================================================================== layout

        private void BuildLayout()
        {
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;

            _console = new VisualElement();
            OpsStyle.Stretch(_console);
            _console.pickingMode = PickingMode.Ignore;
            _console.style.flexDirection = FlexDirection.Column;
            _root.Add(_console);

            _console.Add(BuildHeader());
            _timeline = new TimelineElement(simulation);
            _console.Add(_timeline);

            VisualElement body = OpsStyle.Row();
            body.style.flexGrow = 1;
            body.pickingMode = PickingMode.Ignore;
            _console.Add(body);
            body.Add(BuildLeft());
            body.Add(BuildCentre());
            body.Add(BuildRight());

            _instructor = BuildInstructor();
            _root.Add(_instructor);

            _board = new VisualElement();
            OpsStyle.Stretch(_board);
            _board.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0.985f);
            _board.style.display = DisplayStyle.None;
            _root.Add(_board);

            _modal = new VisualElement();
            OpsStyle.Stretch(_modal);
            _modal.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0.82f);
            _modal.style.alignItems = Align.Center;
            _modal.style.justifyContent = Justify.Center;
            _modal.style.display = DisplayStyle.None;
            _root.Add(_modal);
        }

        // ------------------------------------------------------------------ header

        private Label _satName, _lessonName, _utc, _met, _contactText, _alarmText;
        private VisualElement _contactLamp, _alarmButton;
        private readonly List<Button> _warpButtons = new List<Button>();
        private readonly float[] _warps = { 1f, 10f, 60f, 300f, 1000f, 5000f };
        private Button _pauseButton;
        private bool _alarmUnacked;

        private VisualElement BuildHeader()
        {
            VisualElement h = OpsStyle.Row();
            h.style.height = 58;
            h.style.alignItems = Align.Center;
            h.style.backgroundColor = OpsStyle.Ink;
            OpsStyle.Pad(h, 14, 4);

            var who = new VisualElement();
            who.style.width = 250;
            _satName = OpsStyle.Label("Mission operations", 16, true, OpsStyle.Text);
            _lessonName = OpsStyle.Label("", 11, false, OpsStyle.Muted);
            who.Add(_satName);
            who.Add(_lessonName);
            h.Add(who);

            var clock = new VisualElement();
            clock.style.width = 210;
            _utc = OpsStyle.Number("", 17, OpsStyle.Text);
            _met = OpsStyle.Number("", 11, OpsStyle.Muted);
            clock.Add(_utc);
            clock.Add(_met);
            h.Add(clock);

            // Time control as one segmented control
            VisualElement seg = OpsStyle.Row();
            seg.style.alignItems = Align.Center;
            _pauseButton = OpsStyle.Button("Pause", TogglePause, 58);
            seg.Add(_pauseButton);
            for (int i = 0; i < _warps.Length; i++)
            {
                float w = _warps[i];
                Button b = OpsStyle.Button(w >= 1000 ? (w / 1000f) + "k" : w + "x", () => SetWarp(w), 40, 11);
                b.style.marginLeft = 0; b.style.marginRight = 0;
                OpsStyle.Round(b, 0);
                _warpButtons.Add(b);
                seg.Add(b);
            }
            h.Add(seg);

            VisualElement contact = OpsStyle.Row();
            contact.style.alignItems = Align.Center;
            contact.style.flexGrow = 1;
            contact.style.marginLeft = 16;
            _contactLamp = OpsStyle.Lamp(11, OpsStyle.Faint);
            _contactLamp.style.marginRight = 8;
            contact.Add(_contactLamp);
            _contactText = OpsStyle.Label("", 12, false, OpsStyle.Text);
            contact.Add(_contactText);
            h.Add(contact);

            _alarmButton = new VisualElement();
            _alarmButton.style.flexDirection = FlexDirection.Row;
            _alarmButton.style.alignItems = Align.Center;
            OpsStyle.Pad(_alarmButton, 10, 5);
            OpsStyle.Round(_alarmButton, 4);
            OpsStyle.Border(_alarmButton, 1, OpsStyle.Line);
            _alarmText = OpsStyle.Label("No alarms", 12, true, OpsStyle.Faint);
            _alarmButton.Add(_alarmText);
            _alarmButton.RegisterCallback<ClickEvent>(_ => AcknowledgeAlarms());
            h.Add(_alarmButton);

            h.Add(OpsStyle.Button("Lessons", ShowBoard, 72));
            h.Add(OpsStyle.Button("Instructor", ToggleInstructor, 82));
            h.Add(OpsStyle.Button("Restart", () => { if (simulation != null) simulation.Restart(); }, 64));
            h.Add(OpsStyle.Button("Builder", OpenBuilder, 64));
            h.Add(OpsStyle.Button("Main menu", () => SatelliteBuilderSceneFactory.OpenMainMenu(mainMenuScene), 80));
            return h;
        }

        private void TogglePause()
        {
            if (simulation == null || simulation.AwaitingBriefing) return;
            simulation.Paused = !simulation.Paused;
        }

        private void SetWarp(float w)
        {
            if (simulation == null || simulation.AwaitingBriefing) return;
            simulation.TimeWarp = w;
            simulation.Paused = false;
        }

        private void RefreshHeader()
        {
            SatelliteSim sim = simulation.Sim;
            _satName.text = sim.SatelliteName;
            _lessonName.text = sim.OrbitClass + (simulation.Scenario != null ? " lesson: " + simulation.Scenario.title : " free flight");
            _utc.text = simulation.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            _met.text = "Mission time " + Fmt.Met(sim.Env.Met).Substring(2);

            _pauseButton.text = simulation.Paused ? "Run" : "Pause";
            _pauseButton.style.backgroundColor = simulation.Paused ? OpsStyle.Dim(OpsStyle.Sun, 0.45f) : OpsStyle.Raised;
            for (int i = 0; i < _warpButtons.Count; i++)
            {
                bool on = !simulation.Paused && Mathf.Approximately(simulation.TimeWarp, _warps[i]);
                _warpButtons[i].style.backgroundColor = on ? OpsStyle.Hex(0x1E5E5A) : OpsStyle.Raised;
                _warpButtons[i].style.color = on ? OpsStyle.Text : OpsStyle.Muted;
            }

            GroundLink link = sim.Link;
            if (link.Contact)
            {
                _contactLamp.style.backgroundColor = OpsStyle.Ground;
                _contactText.text = "In contact via " + link.StationName + ", loss of signal in " + Countdown(link.NextLos - sim.Env.Time);
                _contactText.style.color = OpsStyle.Text;
            }
            else
            {
                _contactLamp.style.backgroundColor = link.AlwaysInContact ? OpsStyle.Sun : OpsStyle.Faint;
                string next = double.IsNaN(link.NextAos) ? "no pass in the next day" : "next pass " + link.NextStation + " in " + Countdown(link.NextAos - sim.Env.Time);
                _contactText.text = (link.AlwaysInContact ? "Training aid: commands always allowed. " : "No contact, ") + next;
                _contactText.style.color = OpsStyle.Muted;
            }
        }

        private static string Countdown(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            if (seconds > 86400) return ">1 day";
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 ? string.Format(CultureInfo.InvariantCulture, "{0}h {1:00}m", (int)ts.TotalHours, ts.Minutes)
                                      : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", ts.Minutes, ts.Seconds);
        }

        // ------------------------------------------------------------------ left: lamps, vitals, telemetry

        private sealed class Tile
        {
            public string Name;
            public string[] Prefixes;
            public VisualElement Root, Lamp;
            public Label State;
            public int Level;        // 0 off, 1 nominal, 2 caution, 3 alarm
            public bool Unacked;
            public Color LampColor;
        }

        private readonly List<Tile> _tiles = new List<Tile>();
        private Tile _filterTile;
        private TextField _search;
        private Label _tmStatus;
        private ScrollView _tmScroll;
        private readonly Dictionary<string, Foldout> _tmGroups = new Dictionary<string, Foldout>();
        private readonly Dictionary<string, VisualElement> _tmRows = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Label> _tmValues = new Dictionary<string, Label>();
        private bool _showTruth;

        private sealed class Vital
        {
            public string Key;
            public Label Value;
            public SparklineElement Spark;
            public Func<double, Color> Colour;
            public string Unit;
            public string Format;
        }

        private readonly List<Vital> _vitals = new List<Vital>();

        private VisualElement BuildLeft()
        {
            var left = new VisualElement();
            left.style.width = 330;
            left.style.backgroundColor = OpsStyle.Panel;
            left.style.borderRightWidth = 1;
            left.style.borderRightColor = OpsStyle.Line;
            OpsStyle.Pad(left, 10, 8);

            // Annunciator panel
            VisualElement grid = OpsStyle.Row();
            grid.style.flexWrap = Wrap.Wrap;
            AddTile(grid, "Power", "eps.");
            AddTile(grid, "Attitude", "adcs.");
            AddTile(grid, "Comms", "link.", "ttc.");
            AddTile(grid, "Thermal", "tcs.");
            AddTile(grid, "Computer", "obc.", "obsw.");
            AddTile(grid, "Payload", "pl.", "payload.");
            AddTile(grid, "Propulsion", "prop.");
            AddTile(grid, "Orbit", "orbit.", "str.", "env.");
            left.Add(grid);

            // Vitals
            VisualElement vitals = new VisualElement();
            vitals.style.marginTop = 8;
            vitals.style.marginBottom = 6;
            AddVital(vitals, "Battery", "eps.battery.soc", "%", "0", v => v < 20 ? OpsStyle.Alarm : (v < 40 ? OpsStyle.Caution : OpsStyle.Sun), 0, 100);
            AddVital(vitals, "Net power", "eps.net", "W", "0.0", v => v >= 0 ? OpsStyle.Ground : OpsStyle.Caution, float.NaN, float.NaN);
            AddVital(vitals, "Rotation", "adcs.rate", "deg/s", "0.00", v => v > 2 ? OpsStyle.Alarm : OpsStyle.Text, 0, float.NaN);
            AddVital(vitals, "Inside temp", "tcs.temperature", "C", "0.0", v => OpsStyle.Text, float.NaN, float.NaN);
            left.Add(vitals);

            // Telemetry
            VisualElement head = OpsStyle.Row();
            head.style.alignItems = Align.Center;
            Label t = OpsStyle.Label("Telemetry", 13, true, OpsStyle.Text);
            t.style.flexGrow = 1;
            head.Add(t);
            _search = OpsStyle.Field("", "", 130);
            _search.RegisterValueChangedCallback(_ => ApplyTelemetryFilter());
            head.Add(_search);
            left.Add(head);
            _tmStatus = OpsStyle.Label("", 11, false, OpsStyle.Muted);
            _tmStatus.style.marginBottom = 4;
            left.Add(_tmStatus);
            _tmScroll = new ScrollView(ScrollViewMode.Vertical);
            _tmScroll.style.flexGrow = 1;
            left.Add(_tmScroll);
            return left;
        }

        private void AddTile(VisualElement parent, string name, params string[] prefixes)
        {
            var tile = new Tile { Name = name, Prefixes = prefixes };
            var root = new VisualElement();
            root.style.width = Length.Percent(25);
            root.style.height = 46;
            OpsStyle.Pad(root, 5, 4);
            root.style.borderRightWidth = 1;
            root.style.borderBottomWidth = 1;
            root.style.borderRightColor = OpsStyle.Ink;
            root.style.borderBottomColor = OpsStyle.Ink;
            root.style.backgroundColor = OpsStyle.Raised;
            VisualElement top = OpsStyle.Row();
            top.style.alignItems = Align.Center;
            tile.Lamp = OpsStyle.Lamp(8, OpsStyle.Faint);
            tile.Lamp.style.marginRight = 4;
            top.Add(tile.Lamp);
            Label n = OpsStyle.Label(name, 10, true, OpsStyle.Muted);
            n.pickingMode = PickingMode.Ignore;
            top.Add(n);
            root.Add(top);
            tile.State = OpsStyle.Number("", 10, OpsStyle.Text);
            tile.State.pickingMode = PickingMode.Ignore;
            tile.State.style.marginTop = 3;
            root.Add(tile.State);
            tile.Root = root;
            root.RegisterCallback<ClickEvent>(_ =>
            {
                tile.Unacked = false;
                _filterTile = _filterTile == tile ? null : tile;
                ApplyTelemetryFilter();
                RefreshAnnunciators();
            });
            _tiles.Add(tile);
            parent.Add(root);
        }

        private void AddVital(VisualElement parent, string name, string key, string unit, string format, Func<double, Color> colour, float min, float max)
        {
            VisualElement row = OpsStyle.Row();
            row.style.alignItems = Align.Center;
            row.style.height = 30;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = OpsStyle.Line;
            Label n = OpsStyle.Label(name, 11, false, OpsStyle.Muted);
            n.style.width = 76;
            row.Add(n);
            Label v = OpsStyle.Number("-", 17, OpsStyle.Text);
            v.style.width = 108;
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            v.style.marginRight = 8;
            row.Add(v);
            var spark = new SparklineElement(90) { FixedMin = min, FixedMax = max };
            spark.style.flexGrow = 1;
            spark.style.height = 24;
            row.Add(spark);
            parent.Add(row);
            _vitals.Add(new Vital { Key = key, Value = v, Spark = spark, Colour = colour, Unit = unit, Format = format });
        }

        private TelemetryStore Source
        {
            get { return _showTruth ? simulation.Sim.Telemetry : simulation.Sim.Link.GroundTelemetry; }
        }

        private bool Live
        {
            get { GroundLink l = simulation.Sim.Link; return _showTruth || l.Contact || l.AlwaysInContact; }
        }

        private double Value(string key) { return Source.Get(key, double.NaN); }

        private void RefreshVitals()
        {
            bool live = Live;
            foreach (Vital v in _vitals)
            {
                double x = Value(v.Key);
                if (double.IsNaN(x))
                {
                    v.Value.text = "-";
                    v.Value.style.color = OpsStyle.Faint;
                    continue;
                }
                v.Value.text = x.ToString(v.Format, CultureInfo.InvariantCulture) + " " + v.Unit;
                Color c = v.Colour(x);
                v.Value.style.color = live ? c : OpsStyle.Faint;
                v.Spark.LineColor = c;
                if (live) v.Spark.Push((float)x);
            }
        }

        private void RefreshTelemetry()
        {
            SatelliteSim sim = simulation.Sim;
            GroundLink link = sim.Link;
            bool live = Live;
            if (_showTruth) _tmStatus.text = "Instructor view: onboard values (students see only what is downlinked)";
            else if (double.IsNaN(link.LastTelemetryMet)) _tmStatus.text = "Nothing received yet. Telemetry arrives during a pass.";
            else if (live) _tmStatus.text = link.Contact ? "Live from " + link.StationName : "Live (training aid)";
            else _tmStatus.text = "Last received " + Countdown(sim.Env.Met - link.LastTelemetryMet) + " ago. Values are frozen until the next pass.";
            _tmStatus.style.color = live ? OpsStyle.Ground : OpsStyle.Sun;

            bool added = false;
            foreach (TelemetryPoint p in Source.Points)
            {
                Label value;
                if (!_tmValues.TryGetValue(p.Key, out value))
                {
                    string g = string.IsNullOrEmpty(p.Group) ? "Other" : p.Group;
                    Foldout group;
                    if (!_tmGroups.TryGetValue(g, out group))
                    {
                        group = new Foldout { text = g, value = true };
                        group.style.color = OpsStyle.Text;
                        group.style.marginTop = 2;
                        _tmGroups[g] = group;
                        _tmScroll.Add(group);
                    }
                    VisualElement row = OpsStyle.Row();
                    row.style.alignItems = Align.Center;
                    row.style.paddingLeft = 4;
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0f);
                    row.userData = p.Key + " " + p.Label;
                    Label name = OpsStyle.Label(p.Label, 11, false, OpsStyle.Muted);
                    name.style.flexGrow = 1;
                    name.style.flexShrink = 1;
                    value = OpsStyle.Number("", 11, OpsStyle.Text);
                    value.style.unityTextAlign = TextAnchor.MiddleRight;
                    value.style.maxWidth = 150;
                    row.Add(name);
                    row.Add(value);
                    group.Add(row);
                    _tmRows[p.Key] = row;
                    _tmValues[p.Key] = value;
                    added = true;
                }
                value.text = p.Display;
                bool bad = p.OutOfLimits || (p.Text != null && (p.Text.Contains("FAILED") || p.Text == "DEAD"));
                value.style.color = !live ? OpsStyle.Faint : (bad ? OpsStyle.Alarm : OpsStyle.Text);
                _tmRows[p.Key].style.borderLeftColor = bad && live ? OpsStyle.Alarm : OpsStyle.WithAlpha(OpsStyle.Ink, 0f);
            }
            if (added) ApplyTelemetryFilter();
        }

        private void ApplyTelemetryFilter()
        {
            string q = _search != null ? (_search.value ?? "").Trim().ToLowerInvariant() : "";
            foreach (KeyValuePair<string, VisualElement> kv in _tmRows)
            {
                string text = ((string)kv.Value.userData ?? kv.Key).ToLowerInvariant();
                bool show = (q.Length == 0 || text.Contains(q)) && (_filterTile == null || MatchesTile(_filterTile, kv.Key));
                kv.Value.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static bool MatchesTile(Tile t, string key)
        {
            foreach (string p in t.Prefixes)
                if (key.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void RefreshAnnunciators()
        {
            TelemetryStore src = Source;
            bool live = Live;
            foreach (Tile t in _tiles)
            {
                int level = 0;
                bool any = false;
                foreach (TelemetryPoint p in src.Points)
                {
                    if (!MatchesTile(t, p.Key)) continue;
                    any = true;
                    if (p.OutOfLimits || (p.Text != null && (p.Text.Contains("FAILED") || p.Text == "DEAD"))) level = 3;
                }
                string state = "";
                if (any)
                {
                    if (level < 3) level = 1;
                    switch (t.Name)
                    {
                        case "Power":
                            double soc = src.Get("eps.battery.soc", double.NaN);
                            state = double.IsNaN(soc) ? "" : soc.ToString("0") + "% " + (src.Get("eps.net", 0) >= 0 ? "charging" : "draining");
                            if (src.Get("eps.bus", 1) < 0.5) level = 3;
                            else if (level < 3 && (soc < 40 || TextOf(src, "eps.bus") == "LOAD SHED")) level = 2;
                            break;
                        case "Attitude":
                            state = TextOf(src, "adcs.mode");
                            if (level < 3 && (src.Get("adcs.mode", 2) < 2 || src.Get("adcs.knowledge", 1) < 0.5)) level = 2;
                            break;
                        case "Comms":
                            bool contact = simulation.Sim.Link.Contact;
                            state = contact ? "in contact" : "no contact";
                            if (level < 3) level = contact ? 1 : 0;
                            break;
                        case "Thermal":
                            double temp = src.Get("tcs.temperature", double.NaN);
                            state = double.IsNaN(temp) ? "" : temp.ToString("0.0") + " C";
                            break;
                        case "Computer":
                            state = TextOf(src, "obsw.mode") == "SAFE" ? "SAFE MODE" : TextOf(src, FirstKey(src, "obc.", ".state"));
                            if (level < 3 && state == "SAFE MODE") level = 2;
                            if (state == "BOOT" || state == "HUNG" || state == "OFF") level = 3;
                            break;
                        case "Payload":
                            double svc = src.Get("payload.service", double.NaN);
                            if (!double.IsNaN(svc))
                            {
                                state = svc > 0.5 ? "in service" : "out of service";
                                if (level < 3 && svc < 0.5) level = 2;
                            }
                            else
                            {
                                bool on = false;
                                foreach (TelemetryPoint p in src.Points)
                                    if (p.Key.StartsWith("pl.", StringComparison.OrdinalIgnoreCase) && p.Key.EndsWith(".state", StringComparison.Ordinal) && p.Value > 0.5) on = true;
                                state = on ? "on" : "off";
                                if (level < 3 && !on) level = 0;
                            }
                            break;
                        case "Propulsion":
                            bool firing = false;
                            foreach (TelemetryPoint p in src.Points)
                                if (p.Key.StartsWith("prop.", StringComparison.OrdinalIgnoreCase) && p.Key.EndsWith(".firing", StringComparison.Ordinal) && p.Value > 0.5) firing = true;
                            state = firing ? "firing" : "idle";
                            if (level < 3 && firing) level = 2;
                            break;
                        case "Orbit":
                            bool sun = src.Get("orbit.sun", 1) > 0.5;
                            state = sun ? "sunlight" : "eclipse";
                            break;
                    }
                }
                if (level == 3 && t.Level < 3) t.Unacked = true;
                if (level < 3) t.Unacked = false;
                t.Level = level;
                t.State.text = any ? state : "none";

                Color lamp = level == 3 ? OpsStyle.Alarm : level == 2 ? OpsStyle.Caution : level == 1 ? OpsStyle.Ground : OpsStyle.Faint;
                if (t.Name == "Orbit" && any) lamp = src.Get("orbit.sun", 1) > 0.5 ? OpsStyle.Sun : OpsStyle.Night;
                if (!live && level < 3) lamp = OpsStyle.Dim(lamp, 0.6f);
                t.LampColor = lamp;
                t.Lamp.style.backgroundColor = lamp;
                t.State.style.color = level == 3 ? OpsStyle.Alarm : (any ? OpsStyle.Text : OpsStyle.Faint);
                t.Root.style.backgroundColor = _filterTile == t ? OpsStyle.CardHighlight : (level == 3 ? OpsStyle.Dim(OpsStyle.Alarm, 0.25f) : OpsStyle.Raised);
            }
        }

        private static string TextOf(TelemetryStore s, string key)
        {
            TelemetryPoint p;
            return key != null && s.TryGet(key, out p) ? (p.Text ?? p.Display) : "";
        }

        private static string FirstKey(TelemetryStore s, string prefix, string suffix)
        {
            foreach (TelemetryPoint p in s.Points)
                if (p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Key.EndsWith(suffix, StringComparison.Ordinal)) return p.Key;
            return null;
        }

        private void Blink()
        {
            bool on = (Time.unscaledTime * 2f) % 1f < 0.6f;
            foreach (Tile t in _tiles)
                if (t.Unacked) t.Lamp.style.backgroundColor = on ? OpsStyle.Alarm : OpsStyle.Dim(OpsStyle.Alarm, 0.3f);

            if (_alarmUnacked)
            {
                _alarmButton.style.backgroundColor = on ? OpsStyle.Alarm : OpsStyle.Dim(OpsStyle.Alarm, 0.35f);
                _alarmText.text = "Master alarm";
                _alarmText.style.color = OpsStyle.Text;
            }
            else
            {
                _alarmButton.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0f);
                _alarmText.text = "No alarms";
                _alarmText.style.color = OpsStyle.Faint;
            }
        }

        private void AcknowledgeAlarms()
        {
            _alarmUnacked = false;
            foreach (Tile t in _tiles) t.Unacked = false;
        }

        // ------------------------------------------------------------------ centre: lesson card, toasts, dock

        private VisualElement _lessonCard, _lessonDots, _holdFill, _hintBox, _toastStack, _dock, _dockBody;
        private Label _lessonTitle, _lessonStepCount, _lessonStep, _hintText;
        private Button _hintButton, _lessonCollapse;
        private bool _lessonCollapsed, _hintShown, _dockHidden;
        private int _lessonStepShown = -99;
        private TimelineElement _timeline;
        private GroundTrackElement _map;
        private AttitudeScopeElement _scope;
        private PowerFlowElement _power;

        private VisualElement BuildCentre()
        {
            var centre = new VisualElement();
            centre.style.flexGrow = 1;
            centre.pickingMode = PickingMode.Ignore;
            centre.style.justifyContent = Justify.FlexEnd;

            // Lesson card
            _lessonCard = new VisualElement();
            _lessonCard.style.position = Position.Absolute;
            _lessonCard.style.left = 12;
            _lessonCard.style.top = 12;
            _lessonCard.style.width = 360;
            _lessonCard.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Panel, 0.94f);
            _lessonCard.style.borderLeftWidth = 3;
            _lessonCard.style.borderLeftColor = OpsStyle.Sun;
            OpsStyle.Pad(_lessonCard, 12, 10);
            OpsStyle.Round(_lessonCard, 4);

            VisualElement top = OpsStyle.Row();
            top.style.alignItems = Align.Center;
            _lessonTitle = OpsStyle.Label("", 11, false, OpsStyle.Muted);
            _lessonTitle.style.flexGrow = 1;
            _lessonTitle.style.flexShrink = 1;
            top.Add(_lessonTitle);
            _lessonCollapse = OpsStyle.Button("Hide", () => { _lessonCollapsed = !_lessonCollapsed; _lessonStepShown = -99; RefreshLessonCard(); }, 50, 10);
            top.Add(_lessonCollapse);
            _lessonCard.Add(top);

            VisualElement count = OpsStyle.Row();
            count.style.alignItems = Align.Center;
            count.style.marginTop = 4;
            _lessonStepCount = OpsStyle.Label("", 11, true, OpsStyle.Sun);
            _lessonStepCount.style.marginRight = 8;
            count.Add(_lessonStepCount);
            _lessonDots = OpsStyle.Row();
            count.Add(_lessonDots);
            _lessonCard.Add(count);

            _lessonStep = OpsStyle.Label("", 15, true, OpsStyle.Text);
            _lessonStep.style.marginTop = 6;
            _lessonCard.Add(_lessonStep);

            var holdTrack = new VisualElement();
            holdTrack.style.height = 3;
            holdTrack.style.marginTop = 8;
            holdTrack.style.backgroundColor = OpsStyle.Line;
            _holdFill = new VisualElement();
            _holdFill.style.height = 3;
            _holdFill.style.width = Length.Percent(0);
            _holdFill.style.backgroundColor = OpsStyle.Ground;
            holdTrack.Add(_holdFill);
            _lessonCard.Add(holdTrack);

            _hintBox = new VisualElement();
            _hintBox.style.marginTop = 6;
            _hintButton = OpsStyle.Button("Show a hint", () => { _hintShown = true; _lessonStepShown = -99; RefreshLessonCard(); }, 110, 11);
            _hintText = OpsStyle.Label("", 12, false, OpsStyle.Muted);
            _hintBox.Add(_hintButton);
            _hintBox.Add(_hintText);
            _lessonCard.Add(_hintBox);
            centre.Add(_lessonCard);

            // Toasts
            _toastStack = new VisualElement();
            _toastStack.style.position = Position.Absolute;
            _toastStack.style.right = 12;
            _toastStack.style.top = 12;
            _toastStack.style.width = 330;
            _toastStack.pickingMode = PickingMode.Ignore;
            centre.Add(_toastStack);

            // Instrument dock
            _dock = new VisualElement();
            _dock.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Panel, 0.94f);
            _dock.style.borderTopWidth = 1;
            _dock.style.borderTopColor = OpsStyle.Line;
            VisualElement dockHead = OpsStyle.Row();
            dockHead.style.alignItems = Align.Center;
            OpsStyle.Pad(dockHead, 10, 2);
            Label instruments = OpsStyle.Label("Instruments", 11, true, OpsStyle.Muted);
            instruments.style.flexGrow = 1;
            dockHead.Add(instruments);
            dockHead.Add(OpsStyle.Button("Hide (H)", ToggleDock, 70, 10));
            _dock.Add(dockHead);

            _dockBody = OpsStyle.Row();
            _dockBody.style.height = 200;
            OpsStyle.Pad(_dockBody, 8, 4);
            _map = new GroundTrackElement(simulation);
            _dockBody.Add(Instrument("Ground track", "Satellite path, next orbit (faint) and what each station can see", _map, 2.0f));
            _scope = new AttitudeScopeElement(simulation);
            _dockBody.Add(Instrument("Payload's-eye view", "Centre = where the payload points. Teal disk = Earth, amber = Sun, ring = direction of flight", _scope, 1.0f));
            _power = new PowerFlowElement { Value = Value };
            _dockBody.Add(Instrument("Power flow", "More dots = more watts", _power, 1.4f));
            _dock.Add(_dockBody);
            centre.Add(_dock);
            return centre;
        }

        private VisualElement Instrument(string title, string caption, VisualElement content, float grow)
        {
            var box = new VisualElement();
            box.style.flexGrow = grow;
            box.style.flexBasis = 0;
            box.style.marginLeft = 4; box.style.marginRight = 4;
            Label t = OpsStyle.Label(title, 11, true, OpsStyle.Text);
            box.Add(t);
            Label c = OpsStyle.Label(caption, 9, false, OpsStyle.Faint);
            c.style.marginBottom = 3;
            box.Add(c);
            content.style.flexGrow = 1;
            OpsStyle.Round(content, 3);
            content.style.overflow = Overflow.Hidden;
            box.Add(content);
            return box;
        }

        private void ToggleDock()
        {
            _dockHidden = !_dockHidden;
            _dockBody.style.display = _dockHidden ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshLessonCard()
        {
            ScenarioRunner run = simulation.Runner;
            if (run == null || run.State != ScenarioState.Running)
            {
                _lessonCard.style.display = DisplayStyle.None;
                return;
            }
            _lessonCard.style.display = DisplayStyle.Flex;
            ScenarioStep step = run.Step;
            if (step != null && step.check.seconds > 0)
                _holdFill.style.width = Length.Percent(Mathf.Clamp01((float)(run.StepHoldTimer / step.check.seconds)) * 100f);
            else
                _holdFill.style.width = Length.Percent(0);

            if (run.CurrentStep == _lessonStepShown) return;
            if (run.CurrentStep != _lessonStepShown && _lessonStepShown >= 0) _hintShown = false;
            _lessonStepShown = run.CurrentStep;

            _lessonTitle.text = run.Def.title;
            _lessonStepCount.text = "Step " + Math.Min(run.CurrentStep + 1, run.StepCount) + " of " + run.StepCount;
            _lessonDots.Clear();
            for (int i = 0; i < run.StepCount; i++)
            {
                Color c = i < run.CurrentStep ? OpsStyle.Ground : (i == run.CurrentStep ? OpsStyle.Sun : OpsStyle.Line);
                VisualElement dot = OpsStyle.Lamp(i == run.CurrentStep ? 9 : 7, c);
                dot.style.marginRight = 4;
                _lessonDots.Add(dot);
            }
            _lessonStep.text = step != null ? step.text : "";
            bool hasHint = step != null && !string.IsNullOrEmpty(step.hint);
            _hintButton.style.display = hasHint && !_hintShown ? DisplayStyle.Flex : DisplayStyle.None;
            _hintText.text = hasHint && _hintShown ? step.hint : "";
            _hintText.style.display = hasHint && _hintShown ? DisplayStyle.Flex : DisplayStyle.None;

            _lessonCollapse.text = _lessonCollapsed ? "Show" : "Hide";
            _lessonStep.style.display = _lessonCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _hintBox.style.display = _lessonCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Toasts --------------------------------------------------------

        private int _seenEventVersion;
        private readonly List<KeyValuePair<VisualElement, float>> _toasts = new List<KeyValuePair<VisualElement, float>>();

        private void UpdateToasts()
        {
            EventLog log = simulation.Sim.Events;
            int fresh = log.Version - _seenEventVersion;
            if (fresh > 0)
            {
                _seenEventVersion = log.Version;
                int start = Math.Max(0, log.Events.Count - Math.Min(fresh, 4));
                for (int i = start; i < log.Events.Count; i++)
                {
                    SimEvent e = log.Events[i];
                    bool scenario = e.Source == "SCENARIO" && e.Text.StartsWith("Step", StringComparison.Ordinal);
                    if (e.Severity == EventSeverity.Info && !scenario) continue;
                    if (e.Severity == EventSeverity.Alarm) _alarmUnacked = true;
                    AddToast(e, scenario);
                }
            }
            float now = Time.unscaledTime;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                float age = now - _toasts[i].Value;
                if (age > 8f) { _toasts[i].Key.RemoveFromHierarchy(); _toasts.RemoveAt(i); }
                else if (age > 7f) _toasts[i].Key.style.opacity = 8f - age;
            }
        }

        private void AddToast(SimEvent e, bool scenario)
        {
            Color c = scenario ? OpsStyle.Ground : OpsStyle.SeverityColor(e.Severity);
            var t = new VisualElement();
            t.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Panel, 0.96f);
            t.style.borderLeftWidth = 3;
            t.style.borderLeftColor = c;
            OpsStyle.Pad(t, 10, 6);
            t.style.marginBottom = 6;
            OpsStyle.Round(t, 3);
            t.pickingMode = PickingMode.Ignore;
            t.Add(OpsStyle.Label((scenario ? "Lesson" : e.Source) + ", " + Fmt.Met(e.Met).Substring(2), 9, false, OpsStyle.Muted));
            t.Add(OpsStyle.Label(e.Text, 12, e.Severity == EventSeverity.Alarm, c == OpsStyle.Text ? OpsStyle.Text : c));
            _toastStack.Insert(0, t);
            _toasts.Add(new KeyValuePair<VisualElement, float>(t, Time.unscaledTime));
            while (_toasts.Count > 4)
            {
                _toasts[0].Key.RemoveFromHierarchy();
                _toasts.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------ right: command, plan, rules, log

        private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        private string _activeTab = "Command";
        private VisualElement _tabBody;

        private VisualElement BuildRight()
        {
            var right = new VisualElement();
            right.style.width = 410;
            right.style.backgroundColor = OpsStyle.Panel;
            right.style.borderLeftWidth = 1;
            right.style.borderLeftColor = OpsStyle.Line;

            VisualElement bar = OpsStyle.Row();
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = OpsStyle.Line;
            right.Add(bar);
            _tabBody = new VisualElement();
            _tabBody.style.flexGrow = 1;
            OpsStyle.Pad(_tabBody, 10, 8);
            right.Add(_tabBody);

            AddTab(bar, "Command", BuildCommandTab());
            AddTab(bar, "Plan", BuildPlanTab());
            AddTab(bar, "Rules", BuildRulesTab());
            AddTab(bar, "Log", BuildLogTab());
            SelectTab("Command");
            return right;
        }

        private void AddTab(VisualElement bar, string name, VisualElement content)
        {
            content.style.flexGrow = 1;
            content.style.display = DisplayStyle.None;
            _tabs[name] = content;
            _tabBody.Add(content);
            var b = new Button(() => SelectTab(name)) { text = name };
            b.style.flexGrow = 1;
            b.style.height = 34;
            b.style.fontSize = 12;
            b.style.marginLeft = 0; b.style.marginRight = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.backgroundColor = OpsStyle.WithAlpha(OpsStyle.Ink, 0f);
            OpsStyle.Border(b, 0, OpsStyle.Line);
            b.style.borderBottomWidth = 2;
            OpsStyle.Round(b, 0);
            _tabButtons[name] = b;
            bar.Add(b);
        }

        private void SelectTab(string name)
        {
            _activeTab = name;
            foreach (KeyValuePair<string, VisualElement> kv in _tabs)
                kv.Value.style.display = kv.Key == name ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (KeyValuePair<string, Button> kv in _tabButtons)
            {
                bool on = kv.Key == name;
                kv.Value.style.borderBottomColor = on ? OpsStyle.Ground : OpsStyle.WithAlpha(OpsStyle.Ink, 0f);
                kv.Value.style.color = on ? OpsStyle.Text : OpsStyle.Muted;
                kv.Value.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }
            _logVersion = -1;
            _planHash = -1;
        }

        // Command tab -----------------------------------------------------

        private TextField _commandLine;
        private VisualElement _suggestions;
        private Label _commandResult, _uplinkNote;
        private ScrollView _cards;
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private readonly List<KeyValuePair<string, string>> _knownCommands = new List<KeyValuePair<string, string>>();

        private VisualElement BuildCommandTab()
        {
            var tab = new VisualElement();
            _uplinkNote = OpsStyle.Label("", 11, false, OpsStyle.Muted);
            _uplinkNote.style.marginBottom = 6;
            tab.Add(_uplinkNote);

            VisualElement line = OpsStyle.Row();
            _commandLine = OpsStyle.Field("", "");
            _commandLine.style.flexGrow = 1;
            _commandLine.RegisterValueChangedCallback(_ => RefreshSuggestions());
            _commandLine.RegisterCallback<KeyDownEvent>(OnCommandKey, TrickleDown.TrickleDown);
            line.Add(_commandLine);
            line.Add(OpsStyle.Primary("Send", () => Send(_commandLine.value), 64));
            tab.Add(line);
            Label tip = OpsStyle.Label("Type a unit or command, e.g. adcs mode. Up/Down recalls earlier commands.", 10, false, OpsStyle.Faint);
            tip.style.marginTop = 2;
            tab.Add(tip);

            _suggestions = new VisualElement();
            _suggestions.style.marginTop = 4;
            tab.Add(_suggestions);

            _commandResult = OpsStyle.Label("", 12, false, OpsStyle.Muted);
            _commandResult.style.marginTop = 6;
            _commandResult.style.marginBottom = 6;
            tab.Add(_commandResult);

            _cards = new ScrollView(ScrollViewMode.Vertical);
            _cards.style.flexGrow = 1;
            tab.Add(_cards);
            return tab;
        }

        private void OnCommandKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Send(_commandLine.value);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.UpArrow && _history.Count > 0)
            {
                _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
                _commandLine.SetValueWithoutNotify(_history[_historyIndex]);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.DownArrow && _historyIndex >= 0)
            {
                _historyIndex++;
                _commandLine.SetValueWithoutNotify(_historyIndex < _history.Count ? _history[_historyIndex] : "");
                if (_historyIndex >= _history.Count) _historyIndex = -1;
                e.StopPropagation();
            }
        }

        private void Send(string command)
        {
            if (simulation == null || string.IsNullOrWhiteSpace(command)) return;
            command = command.Trim();
            string result = simulation.SendGround(command);
            bool failed = result.StartsWith("NOT SENT", StringComparison.Ordinal) || result.StartsWith("REJECTED", StringComparison.Ordinal) || result.StartsWith("Unknown", StringComparison.Ordinal);
            _commandResult.text = (failed ? "Not done: " : "Done: ") + command + "\n" + result;
            _commandResult.style.color = failed ? OpsStyle.Alarm : OpsStyle.Ground;
            if (_history.Count == 0 || _history[_history.Count - 1] != command) _history.Add(command);
            _historyIndex = -1;
            if (!failed) _commandLine.SetValueWithoutNotify("");
            RefreshSuggestions();
        }

        private void RebuildCommandIndex()
        {
            _knownCommands.Clear();
            SatelliteSim sim = simulation.Sim;
            var targets = new List<string> { "ADCS", "OBSW" };
            foreach (SimComponent c in sim.Components) targets.Add(c.Id);
            foreach (string t in targets)
            {
                SimComponent c = sim.Find(t);
                string unit = c != null ? c.Name : (t == "ADCS" ? "Attitude control" : "Onboard software");
                foreach (CommandSpec s in sim.CommandsFor(t))
                {
                    if (!string.IsNullOrEmpty(s.Args) && s.Args.IndexOf('<') < 0 && s.Args.IndexOf('|') >= 0)
                        foreach (string choice in s.Args.Split('|'))
                            _knownCommands.Add(new KeyValuePair<string, string>(t + " " + s.Name + " " + choice, unit + ": " + s.Help));
                    else
                        _knownCommands.Add(new KeyValuePair<string, string>(t + " " + s.Name + (string.IsNullOrEmpty(s.Args) ? "" : " "), unit + ": " + s.Help + (string.IsNullOrEmpty(s.Args) ? "" : " (" + s.Args + ")")));
                }
            }
        }

        private void RefreshSuggestions()
        {
            _suggestions.Clear();
            string q = (_commandLine.value ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return;
            string[] words = q.Split(' ');
            int shown = 0;
            foreach (KeyValuePair<string, string> kv in _knownCommands)
            {
                string hay = (kv.Key + " " + kv.Value).ToLowerInvariant();
                bool all = true;
                foreach (string w in words) if (w.Length > 0 && !hay.Contains(w)) { all = false; break; }
                if (!all) continue;
                string cmd = kv.Key;
                var row = new VisualElement();
                OpsStyle.Pad(row, 6, 3);
                row.style.backgroundColor = OpsStyle.Raised;
                row.style.marginBottom = 1;
                Label c = OpsStyle.Number(cmd, 11, OpsStyle.Text);
                Label h = OpsStyle.Label(kv.Value, 10, false, OpsStyle.Muted);
                row.Add(c);
                row.Add(h);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _commandLine.value = cmd;
                    _commandLine.Focus();
                });
                row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = OpsStyle.RaisedHover);
                row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = OpsStyle.Raised);
                _suggestions.Add(row);
                if (++shown >= 6) break;
            }
        }

        private void RefreshUplinkNote()
        {
            GroundLink link = simulation.Sim.Link;
            if (link.CanUplink)
            {
                _uplinkNote.text = link.Contact ? "Uplink open via " + link.StationName + ". Commands go straight to the satellite." : "Training aid on: commands always reach the satellite.";
                _uplinkNote.style.color = OpsStyle.Ground;
            }
            else
            {
                _uplinkNote.text = "No uplink. Commands wait for a pass" + (double.IsNaN(link.NextAos) ? "." : " (" + link.NextStation + " in " + Countdown(link.NextAos - simulation.Sim.Env.Time) + ")") + ", or store them in Plan during a pass.";
                _uplinkNote.style.color = OpsStyle.Sun;
            }
        }

        private void RebuildCards()
        {
            _cards.Clear();
            SatelliteSim sim = simulation.Sim;
            var bySubsystem = new Dictionary<string, Foldout>();
            AddCommandCard(GroupFold(bySubsystem, "Software", true), "ADCS", "Attitude control", sim.CommandsFor("ADCS"), null);
            AddCommandCard(GroupFold(bySubsystem, "Software", true), "OBSW", "Onboard software", sim.CommandsFor("OBSW"), null);
            foreach (SimComponent c in sim.Components)
            {
                List<CommandSpec> cmds = sim.CommandsFor(c.Id);
                if (cmds.Count == 0) continue;
                AddCommandCard(GroupFold(bySubsystem, c.Subsystem, false), c.Id, c.Name, cmds, c);
            }
        }

        private Foldout GroupFold(Dictionary<string, Foldout> map, string name, bool open)
        {
            Foldout f;
            if (!map.TryGetValue(name, out f))
            {
                f = new Foldout { text = name, value = open };
                f.style.color = OpsStyle.Text;
                map[name] = f;
                _cards.Add(f);
            }
            return f;
        }

        private void AddCommandCard(VisualElement parent, string target, string title, List<CommandSpec> commands, SimComponent unit)
        {
            VisualElement card = OpsStyle.Card();
            VisualElement head = OpsStyle.Row();
            head.style.alignItems = Align.Center;
            Label t = OpsStyle.Label(title, 12, true, OpsStyle.Text);
            t.style.flexGrow = 1;
            head.Add(t);
            head.Add(OpsStyle.Number(target, 10, OpsStyle.Faint));
            card.Add(head);
            foreach (CommandSpec spec in commands)
            {
                CommandSpec s = spec;
                VisualElement row = OpsStyle.Row();
                row.style.alignItems = Align.Center;
                row.style.flexWrap = Wrap.Wrap;
                row.style.marginTop = 3;
                Label name = OpsStyle.Label(Pretty(s.Name), 11, false, OpsStyle.Muted);
                name.style.width = 96;
                row.Add(name);
                if (string.IsNullOrEmpty(s.Args))
                {
                    row.Add(OpsStyle.Button(Pretty(s.Name), () => Send(target + " " + s.Name), 0, 11));
                }
                else if (s.Args.IndexOf('<') < 0 && s.Args.IndexOf('|') >= 0)
                {
                    foreach (string choice in s.Args.Split('|'))
                    {
                        string ch = choice;
                        row.Add(OpsStyle.Button(Pretty(ch), () => Send(target + " " + s.Name + " " + ch), 0, 11));
                    }
                }
                else
                {
                    var fields = new List<TextField>();
                    foreach (string part in s.Args.Split(' '))
                    {
                        TextField f = OpsStyle.Field("", part.StartsWith("<", StringComparison.Ordinal) ? "1" : part.Split('|')[0], 64);
                        fields.Add(f);
                        row.Add(f);
                    }
                    row.Add(OpsStyle.Button("Send", () =>
                    {
                        string a = "";
                        foreach (TextField f in fields) a += " " + f.value;
                        Send(target + " " + s.Name + a);
                    }, 0, 11));
                    Label help = OpsStyle.Label(s.Args, 9, false, OpsStyle.Faint);
                    help.style.width = Length.Percent(100);
                    help.style.paddingLeft = 96;
                    row.Add(help);
                }
                card.Add(row);
            }
            parent.Add(card);
        }

        /// <summary>"SUNSAFE" -> "Sunsafe", "CLEAR_SCHEDULE" -> "Clear schedule".</summary>
        private static string Pretty(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string lower = s.Replace('_', ' ').ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        // Plan tab --------------------------------------------------------

        private TextField _planSeconds, _planCommand;
        private VisualElement _planList;
        private int _planHash = -1;

        private VisualElement BuildPlanTab()
        {
            var tab = new VisualElement();
            tab.Add(OpsStyle.Label("Stored commands run on board at their time, even without contact. Send them while you have uplink. Amber diamonds on the timeline show them.", 11, false, OpsStyle.Muted));

            tab.Add(OpsStyle.Heading("Run"));
            _planCommand = OpsStyle.Field("", "pl.main OFF");
            tab.Add(_planCommand);
            tab.Add(OpsStyle.Heading("In how many seconds"));
            VisualElement when = OpsStyle.Row();
            when.style.flexWrap = Wrap.Wrap;
            _planSeconds = OpsStyle.Field("", "600", 80);
            when.Add(_planSeconds);
            when.Add(OpsStyle.Button("+5 min", () => _planSeconds.value = "300", 0, 11));
            when.Add(OpsStyle.Button("At next pass", () => SetPlanTime(NextPassIn(), 60), 0, 11));
            when.Add(OpsStyle.Button("Before next eclipse", () => SetPlanTime(NextEclipseIn(), -120), 0, 11));
            tab.Add(when);
            VisualElement actions = OpsStyle.Row();
            actions.style.marginTop = 6;
            actions.Add(OpsStyle.Primary("Store command", () => Send("OBSW SCHEDULE_IN " + _planSeconds.value + " " + _planCommand.value), 140));
            actions.Add(OpsStyle.Button("Clear all", () => Send("OBSW CLEAR_SCHEDULE"), 90, 11));
            tab.Add(actions);

            tab.Add(OpsStyle.Heading("On board"));
            _planList = new ScrollView(ScrollViewMode.Vertical);
            _planList.style.flexGrow = 1;
            tab.Add(_planList);
            return tab;
        }

        private double NextPassIn()
        {
            double now = simulation.Sim.Env.Time;
            foreach (ForecastWindow w in simulation.Passes) if (w.Start > now + 30) return w.Start - now;
            return double.NaN;
        }

        private double NextEclipseIn()
        {
            double now = simulation.Sim.Env.Time;
            foreach (ForecastWindow w in simulation.Eclipses) if (w.Start > now + 30) return w.Start - now;
            return double.NaN;
        }

        private void SetPlanTime(double seconds, double offset)
        {
            if (double.IsNaN(seconds)) { _commandResult.text = "Nothing like that in the forecast."; return; }
            _planSeconds.value = Math.Max(5, Math.Round(seconds + offset)).ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshPlan()
        {
            if (_activeTab != "Plan") return;
            SatelliteSim sim = simulation.Sim;
            int hash = sim.Obsw.Schedule.Count * 1000 + sim.Obsw.Schedule.FindAll(c => c.Done).Count * 10 + (int)(sim.Env.Met / 5) % 10;
            if (hash == _planHash) return;
            _planHash = hash;
            _planList.Clear();
            if (sim.Obsw.Schedule.Count == 0)
                _planList.Add(OpsStyle.Label("Nothing stored on board yet.", 12, false, OpsStyle.Faint));
            foreach (ScheduledCommand c in sim.Obsw.Schedule)
            {
                VisualElement row = OpsStyle.Row();
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 3;
                row.Add(OpsStyle.Lamp(7, c.Done ? OpsStyle.Faint : OpsStyle.Sun));
                Label when = OpsStyle.Number(c.Done ? "done" : "in " + Countdown(c.Met - sim.Env.Met), 11, c.Done ? OpsStyle.Faint : OpsStyle.Sun);
                when.style.width = 78;
                when.style.marginLeft = 6;
                row.Add(when);
                row.Add(OpsStyle.Number(c.Command, 11, c.Done ? OpsStyle.Faint : OpsStyle.Text));
                _planList.Add(row);
            }
        }

        // Rules tab -------------------------------------------------------

        private ScrollView _rulesList;
        private readonly Dictionary<string, Toggle> _ruleToggles = new Dictionary<string, Toggle>();
        private readonly Dictionary<string, Label> _ruleInfo = new Dictionary<string, Label>();

        private VisualElement BuildRulesTab()
        {
            var tab = new VisualElement();
            tab.Add(OpsStyle.Label("The satellite watches its own telemetry and reacts by itself (fault detection, isolation and recovery). Switching a rule is a command, so it needs uplink.", 11, false, OpsStyle.Muted));
            _rulesList = new ScrollView(ScrollViewMode.Vertical);
            _rulesList.style.flexGrow = 1;
            _rulesList.style.marginTop = 8;
            tab.Add(_rulesList);
            return tab;
        }

        private void RebuildRules()
        {
            _rulesList.Clear();
            _ruleToggles.Clear();
            _ruleInfo.Clear();
            foreach (RuleState r in simulation.Sim.Obsw.Rules)
            {
                RuleState rule = r;
                VisualElement card = OpsStyle.Card();
                var toggle = new Toggle(rule.Def.description) { value = rule.Def.enabled };
                toggle.style.color = OpsStyle.Text;
                toggle.RegisterValueChangedCallback(e =>
                {
                    Send("OBSW RULE " + rule.Def.id + (e.newValue ? " ON" : " OFF"));
                    toggle.SetValueWithoutNotify(rule.Def.enabled);
                });
                card.Add(toggle);
                card.Add(OpsStyle.Number("if " + rule.Def.point + " " + rule.Def.op + " " + rule.Def.value.ToString(CultureInfo.InvariantCulture) + " for " + rule.Def.seconds + " s", 10, OpsStyle.Faint));
                card.Add(OpsStyle.Number("then " + string.Join("; ", rule.Def.actions.ToArray()), 10, OpsStyle.Faint));
                Label info = OpsStyle.Label("", 11, true, OpsStyle.Caution);
                card.Add(info);
                _ruleToggles[rule.Def.id] = toggle;
                _ruleInfo[rule.Def.id] = info;
                _rulesList.Add(card);
            }
        }

        private void RefreshRules()
        {
            if (_activeTab != "Rules") return;
            foreach (RuleState r in simulation.Sim.Obsw.Rules)
            {
                Toggle t;
                if (_ruleToggles.TryGetValue(r.Def.id, out t)) t.SetValueWithoutNotify(r.Def.enabled);
                Label l;
                if (_ruleInfo.TryGetValue(r.Def.id, out l))
                    l.text = r.Triggered ? "Acting now" : (r.FireCount > 0 ? "Acted " + r.FireCount + (r.FireCount == 1 ? " time" : " times") : "");
            }
        }

        // Log tab ---------------------------------------------------------

        private ScrollView _logList;
        private int _logVersion = -1;
        private string _logFilter = "All";
        private readonly Dictionary<string, Button> _logChips = new Dictionary<string, Button>();

        private VisualElement BuildLogTab()
        {
            var tab = new VisualElement();
            VisualElement chips = OpsStyle.Row();
            foreach (string f in new[] { "All", "Alarms", "Warnings", "Ground", "Lesson" })
            {
                string filter = f;
                Button b = OpsStyle.Button(f, () => { _logFilter = filter; _logVersion = -1; RefreshChips(); RefreshLog(); }, 0, 11);
                _logChips[f] = b;
                chips.Add(b);
            }
            tab.Add(chips);
            _logList = new ScrollView(ScrollViewMode.Vertical);
            _logList.style.flexGrow = 1;
            _logList.style.marginTop = 6;
            tab.Add(_logList);
            RefreshChips();
            return tab;
        }

        private void RefreshChips()
        {
            foreach (KeyValuePair<string, Button> kv in _logChips)
                kv.Value.style.backgroundColor = kv.Key == _logFilter ? OpsStyle.Hex(0x1E5E5A) : OpsStyle.Raised;
        }

        private void RefreshLog()
        {
            if (_activeTab != "Log") return;
            EventLog log = simulation.Sim.Events;
            if (log.Version == _logVersion) return;
            _logVersion = log.Version;
            _logList.Clear();
            int shown = 0;
            for (int i = log.Events.Count - 1; i >= 0 && shown < 160; i--)
            {
                SimEvent e = log.Events[i];
                bool ok = _logFilter == "All"
                          || (_logFilter == "Alarms" && e.Severity == EventSeverity.Alarm)
                          || (_logFilter == "Warnings" && e.Severity != EventSeverity.Info)
                          || (_logFilter == "Ground" && e.Source == "GROUND")
                          || (_logFilter == "Lesson" && e.Source == "SCENARIO");
                if (!ok) continue;
                shown++;
                VisualElement row = OpsStyle.Row();
                row.style.marginBottom = 3;
                Label time = OpsStyle.Number(Fmt.Met(e.Met).Substring(2), 10, OpsStyle.Faint);
                time.style.width = 92;
                row.Add(time);
                var text = new VisualElement();
                text.style.flexShrink = 1;
                text.style.flexGrow = 1;
                text.Add(OpsStyle.Label(e.Source, 9, true, OpsStyle.Muted));
                text.Add(OpsStyle.Label(e.Text, 11, e.Severity == EventSeverity.Alarm, OpsStyle.SeverityColor(e.Severity)));
                row.Add(text);
                _logList.Add(row);
            }
            if (shown == 0) _logList.Add(OpsStyle.Label("No entries for this filter.", 12, false, OpsStyle.Faint));
        }

        // ================================================================== instructor drawer

        private DropdownField _faultTarget;
        private VisualElement _faultList;
        private Label _instructorResult;
        private Toggle _aidToggle;
        private bool _instructorVisible;
        private readonly List<SimComponent> _faultComponents = new List<SimComponent>();

        private VisualElement BuildInstructor()
        {
            var d = new VisualElement();
            d.style.position = Position.Absolute;
            d.style.top = 110;
            d.style.right = 414;
            d.style.width = 340;
            d.style.maxHeight = Length.Percent(78);
            d.style.backgroundColor = OpsStyle.Hex(0x1C1424, 0.98f);
            d.style.borderLeftWidth = 3;
            d.style.borderLeftColor = OpsStyle.Alarm;
            OpsStyle.Pad(d, 12, 10);
            OpsStyle.Round(d, 4);
            d.style.display = DisplayStyle.None;

            VisualElement head = OpsStyle.Row();
            Label title = OpsStyle.Label("Instructor", 15, true, OpsStyle.Text);
            title.style.flexGrow = 1;
            head.Add(title);
            head.Add(OpsStyle.Button("Close", ToggleInstructor, 56, 10));
            d.Add(head);
            d.Add(OpsStyle.Label("Break things on purpose. The student console never says what you did, only what the satellite reports.", 11, false, OpsStyle.Muted));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            d.Add(scroll);

            scroll.Add(OpsStyle.Heading("Fail a unit"));
            _faultTarget = new DropdownField("", new List<string>(), 0);
            _faultTarget.style.color = OpsStyle.Text;
            _faultTarget.RegisterValueChangedCallback(_ => RebuildFaultList());
            scroll.Add(_faultTarget);
            _faultList = new VisualElement();
            scroll.Add(_faultList);

            scroll.Add(OpsStyle.Heading("Environment"));
            scroll.Add(EnvRow("Knock into a tumble (deg/s)", "5", v => "SIM TUMBLE " + v));
            scroll.Add(EnvRow("Set battery charge (%)", "30", v => "SIM SET_SOC " + v));
            scroll.Add(EnvRow("Fill the wheels (%)", "75", v => "SIM WHEEL_MOMENTUM " + v));
            scroll.Add(OpsStyle.Button("Clear every fault", () => Instructor("SIM CLEAR ALL"), 0, 11));

            scroll.Add(OpsStyle.Heading("Training aids"));
            _aidToggle = new Toggle("Always in contact");
            _aidToggle.style.color = OpsStyle.Text;
            _aidToggle.RegisterValueChangedCallback(e => { if (simulation.Sim != null) simulation.Sim.Link.AlwaysInContact = e.newValue; });
            scroll.Add(_aidToggle);
            var truth = new Toggle("Show the satellite's true values");
            truth.style.color = OpsStyle.Text;
            truth.RegisterValueChangedCallback(e => _showTruth = e.newValue);
            scroll.Add(truth);

            scroll.Add(OpsStyle.Heading("Instructor command"));
            VisualElement console = OpsStyle.Row();
            TextField line = OpsStyle.Field("", "SIM FAULT eps.solar PANEL_OFF 0");
            line.style.flexGrow = 1;
            console.Add(line);
            console.Add(OpsStyle.Button("Run", () => Instructor(line.value), 50, 11));
            scroll.Add(console);
            _instructorResult = OpsStyle.Label("", 11, false, OpsStyle.Caution);
            scroll.Add(_instructorResult);
            return d;
        }

        private VisualElement EnvRow(string label, string value, Func<string, string> command)
        {
            VisualElement row = OpsStyle.Row();
            row.style.alignItems = Align.Center;
            TextField f = OpsStyle.Field("", value, 56);
            Button b = OpsStyle.Button(label, () => Instructor(command(f.value)), 0, 11);
            b.style.flexGrow = 1;
            row.Add(b);
            row.Add(f);
            return row;
        }

        private void ToggleInstructor()
        {
            _instructorVisible = !_instructorVisible;
            _instructor.style.display = _instructorVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_instructorVisible) RebuildFaultTargets();
        }

        private void RebuildFaultTargets()
        {
            _faultComponents.Clear();
            var names = new List<string>();
            if (simulation.Sim != null)
                foreach (SimComponent c in simulation.Sim.Components)
                {
                    var faults = new List<FaultSpec>();
                    c.GetFaults(faults);
                    if (faults.Count == 0) continue;
                    _faultComponents.Add(c);
                    names.Add(c.Subsystem + ": " + c.Name);
                }
            _faultTarget.choices = names;
            if (names.Count > 0 && (_faultTarget.index < 0 || _faultTarget.index >= names.Count)) _faultTarget.index = 0;
            if (simulation.Sim != null) _aidToggle.SetValueWithoutNotify(simulation.Sim.Link.AlwaysInContact);
            RebuildFaultList();
        }

        private void RebuildFaultList()
        {
            _faultList.Clear();
            int i = _faultTarget.index;
            if (i < 0 || i >= _faultComponents.Count) return;
            SimComponent c = _faultComponents[i];
            var faults = new List<FaultSpec>();
            c.GetFaults(faults);
            foreach (FaultSpec f in faults)
            {
                FaultSpec fault = f;
                VisualElement row = OpsStyle.Row();
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                TextField value = string.IsNullOrEmpty(fault.Args) ? null : OpsStyle.Field("", fault.Args, 50);
                Button b = OpsStyle.Button(fault.Help, () => Instructor("SIM FAULT " + c.Id + " " + fault.Name + (value != null ? " " + value.value : "")), 0, 11);
                b.style.flexGrow = 1;
                b.style.flexShrink = 1;
                row.Add(b);
                if (value != null) row.Add(value);
                _faultList.Add(row);
            }
            _faultList.Add(OpsStyle.Button("Repair this unit", () => Instructor("SIM CLEAR " + c.Id), 0, 11));
        }

        private void Instructor(string command)
        {
            _instructorResult.text = simulation.SendInstructor(command);
        }

        // ================================================================== mission board

        private OrbitRingsElement _rings;
        private bool _boardVisible;
        private string _boardFilter = "";
        private VisualElement _boardList;
        private readonly Dictionary<string, Button> _boardChips = new Dictionary<string, Button>();

        private void ShowBoard()
        {
            _boardVisible = true;
            _board.Clear();
            _board.style.display = DisplayStyle.Flex;
            _boardChips.Clear();

            VisualElement layout = OpsStyle.Row();
            layout.style.flexGrow = 1;
            OpsStyle.Pad(layout, 40, 28);
            _board.Add(layout);

            // Left: the three orbit families
            var left = new VisualElement();
            left.style.width = 430;
            left.style.marginRight = 36;
            Label title = OpsStyle.Label("Mission operations", 34, true, OpsStyle.Text);
            left.Add(title);
            Label sub = OpsStyle.Label("Fly a satellite from the ground. You only hear from it during passes, power comes and goes with the Sun, and things break. Pick an orbit to see its lessons.", 14, false, OpsStyle.Muted);
            sub.style.marginTop = 6;
            sub.style.maxWidth = 420;
            left.Add(sub);
            _rings = new OrbitRingsElement { Highlight = _boardFilter };
            _rings.style.width = 400;
            _rings.style.height = 360;
            _rings.style.marginTop = 16;
            left.Add(_rings);
            VisualElement chips = OpsStyle.Row();
            chips.style.marginTop = 8;
            foreach (string f in new[] { "All orbits", "LEO", "MEO", "GEO" })
            {
                string key = f == "All orbits" ? "" : f;
                Button b = OpsStyle.Button(f, () => { _boardFilter = key; _rings.Highlight = key; RefreshBoardList(); }, 0, 12);
                _boardChips[key] = b;
                chips.Add(b);
            }
            left.Add(chips);
            Label keys = OpsStyle.Label("Keys while flying: Space pauses, 1 to 6 set time warp, I opens the instructor, H hides the instruments.", 11, false, OpsStyle.Faint);
            keys.style.marginTop = 14;
            left.Add(keys);
            layout.Add(left);

            // Right: lessons and free flight
            var right = new ScrollView(ScrollViewMode.Vertical);
            right.style.flexGrow = 1;
            _boardList = right;
            layout.Add(right);

            VisualElement footer = OpsStyle.Row();
            footer.style.position = Position.Absolute;
            footer.style.right = 40;
            footer.style.top = 24;
            if (simulation.Sim != null)
                footer.Add(OpsStyle.Primary("Back to " + simulation.Sim.SatelliteName, HideBoard, 0));
            footer.Add(OpsStyle.Button("Satellite Builder", OpenBuilder, 0));
            footer.Add(OpsStyle.Button("Main menu", () => SatelliteBuilderSceneFactory.OpenMainMenu(mainMenuScene), 0));
            _board.Add(footer);

            RefreshBoardList();
        }

        private void HideBoard()
        {
            _boardVisible = false;
            _board.style.display = DisplayStyle.None;
        }

        private void RefreshBoardList()
        {
            foreach (KeyValuePair<string, Button> kv in _boardChips)
                kv.Value.style.backgroundColor = kv.Key == _boardFilter ? OpsStyle.Hex(0x1E5E5A) : OpsStyle.Raised;
            _rings.Refresh();
            _boardList.Clear();

            foreach (string orbit in new[] { "LEO", "MEO", "GEO" })
            {
                if (_boardFilter.Length > 0 && orbit != _boardFilter) continue;
                List<ScenarioDef> list = simulation.Scenarios.FindAll(s => s.orbitClass == orbit);
                BuilderTemplate template = simulation.Templates.Find(t => t.orbitClass == orbit);

                VisualElement head = OpsStyle.Row();
                head.style.alignItems = Align.Center;
                head.style.marginTop = 14;
                head.style.marginBottom = 6;
                head.Add(OpsStyle.Badge(orbit, OpsStyle.OrbitColor(orbit)));
                Label name = OpsStyle.Label(template != null ? template.displayName + ", " + template.satelliteClass : orbit, 16, true, OpsStyle.Text);
                name.style.marginLeft = 8;
                name.style.flexGrow = 1;
                head.Add(name);
                if (template != null)
                {
                    BuilderTemplate tpl = template;
                    head.Add(OpsStyle.Button("Free flight", () => { HideBoard(); simulation.StartFreeFlight(tpl, null); }, 0, 11));
                }
                _boardList.Add(head);

                for (int i = 0; i < list.Count; i++)
                    _boardList.Add(LessonRow(list[i], i + 1));
            }

            if (simulation.LastDesign != null && simulation.Template != null)
            {
                BuilderTemplate t = simulation.Template;
                DesignSave d = simulation.LastDesign;
                VisualElement own = OpsStyle.Card();
                own.style.marginTop = 16;
                own.style.borderLeftWidth = 3;
                own.style.borderLeftColor = OpsStyle.Sun;
                own.Add(OpsStyle.Label("Your own design", 14, true, OpsStyle.Text));
                own.Add(OpsStyle.Label(t.displayName + " from the Satellite Builder, " + t.orbitClass, 11, false, OpsStyle.Muted));
                Button fly = OpsStyle.Primary("Fly it", () => { HideBoard(); simulation.StartFreeFlight(t, d); }, 120);
                fly.style.marginTop = 6;
                own.Add(fly);
                _boardList.Add(own);
            }
        }

        private VisualElement LessonRow(ScenarioDef s, int number)
        {
            VisualElement row = OpsStyle.Card();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            Label n = OpsStyle.Number(number.ToString(CultureInfo.InvariantCulture), 22, OpsStyle.WithAlpha(OpsStyle.OrbitColor(s.orbitClass), 0.8f));
            n.style.width = 34;
            row.Add(n);
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            VisualElement top = OpsStyle.Row();
            top.style.alignItems = Align.Center;
            Label t = OpsStyle.Label(s.title, 14, true, OpsStyle.Text);
            t.style.flexShrink = 1;
            top.Add(t);
            Label lvl = OpsStyle.Badge(s.level, s.level == "Advanced" ? OpsStyle.Caution : (s.level == "Intermediate" ? OpsStyle.Sun : OpsStyle.Ground));
            lvl.style.marginLeft = 8;
            top.Add(lvl);
            body.Add(top);
            Label sum = OpsStyle.Label(s.summary, 12, false, OpsStyle.Muted);
            sum.style.marginTop = 3;
            body.Add(sum);
            row.Add(body);
            Button go = OpsStyle.Primary("Brief me", () => { HideBoard(); simulation.StartScenario(s); }, 100);
            go.style.marginLeft = 10;
            row.Add(go);
            return row;
        }

        // ================================================================== briefing and debrief

        private ScenarioState _debriefShownFor = ScenarioState.Running;

        private void ShowBriefing()
        {
            ScenarioDef d = simulation.Scenario;
            if (d == null) return;
            _modal.Clear();
            VisualElement card = ModalCard(OpsStyle.OrbitColor(d.orbitClass));

            VisualElement badges = OpsStyle.Row();
            badges.Add(OpsStyle.Badge(d.orbitClass, OpsStyle.OrbitColor(d.orbitClass)));
            Label lvl = OpsStyle.Badge(d.level, OpsStyle.Muted);
            lvl.style.marginLeft = 6;
            badges.Add(lvl);
            card.Add(badges);
            Label title = OpsStyle.Label(d.title, 26, true, OpsStyle.Text);
            title.style.marginTop = 8;
            card.Add(title);
            Label situation = OpsStyle.Label(d.situation, 14, false, OpsStyle.Text);
            situation.style.marginTop = 12;
            card.Add(situation);

            card.Add(OpsStyle.Heading("You will learn to"));
            foreach (string g in d.goals)
            {
                VisualElement r = OpsStyle.Row();
                r.style.alignItems = Align.Center;
                r.style.marginBottom = 2;
                VisualElement dot = OpsStyle.Lamp(6, OpsStyle.Ground);
                dot.style.marginRight = 8;
                r.Add(dot);
                r.Add(OpsStyle.Label(g, 12, false, OpsStyle.Text));
                card.Add(r);
            }
            if (d.prerequisites != null && d.prerequisites.Count > 0)
            {
                card.Add(OpsStyle.Heading("Do these first"));
                card.Add(OpsStyle.Label(string.Join(", ", d.prerequisites.ToArray()), 12, false, OpsStyle.Muted));
            }
            SatelliteSim sim = simulation.Sim;
            card.Add(OpsStyle.Heading("Starting conditions"));
            card.Add(OpsStyle.Number(simulation.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC, "
                                     + sim.Env.AltitudeKm.ToString("0") + " km up, battery " + sim.Telemetry.Get("eps.battery.soc", 0).ToString("0") + "%, "
                                     + "time warp x" + simulation.TimeWarp.ToString("0"), 12, OpsStyle.Muted));

            VisualElement buttons = OpsStyle.Row();
            buttons.style.marginTop = 18;
            buttons.Add(OpsStyle.Primary("Begin mission", () => { CloseModal(); simulation.BeginAfterBriefing(); }, 160));
            buttons.Add(OpsStyle.Button("Choose another lesson", () => { CloseModal(); ShowBoard(); }, 0, 12));
            card.Add(buttons);
            _modal.Add(card);
            _modal.style.display = DisplayStyle.Flex;
            _modalVisible = true;
        }

        private VisualElement ModalCard(Color accent)
        {
            var card = new VisualElement();
            card.style.width = 640;
            card.style.maxWidth = Length.Percent(92);
            card.style.backgroundColor = OpsStyle.Panel;
            card.style.borderTopWidth = 3;
            card.style.borderTopColor = accent;
            OpsStyle.Pad(card, 28, 22);
            OpsStyle.Round(card, 6);
            return card;
        }

        private bool _modalVisible;

        private void CloseModal() { _modalVisible = false; _modal.style.display = DisplayStyle.None; _modal.Clear(); }

        private void CheckDebrief()
        {
            ScenarioRunner run = simulation.Runner;
            if (run == null || run.State == ScenarioState.Running || run.State == _debriefShownFor) return;
            _debriefShownFor = run.State;
            ShowDebrief(run);
        }

        private void ShowDebrief(ScenarioRunner run)
        {
            bool won = run.State == ScenarioState.Succeeded;
            ScenarioDef d = run.Def;
            simulation.Paused = true;
            _modal.Clear();
            VisualElement card = ModalCard(won ? OpsStyle.Ground : OpsStyle.Alarm);
            Label head = OpsStyle.Label(won ? "Mission accomplished" : "Mission lost", 28, true, won ? OpsStyle.Ground : OpsStyle.Alarm);
            card.Add(head);
            card.Add(OpsStyle.Label(d.title, 13, false, OpsStyle.Muted));
            Label text = OpsStyle.Label(won ? d.successText : d.failText, 15, false, OpsStyle.Text);
            text.style.marginTop = 12;
            card.Add(text);

            VisualElement stats = OpsStyle.Row();
            stats.style.marginTop = 18;
            stats.style.flexWrap = Wrap.Wrap;
            stats.Add(Stat("Mission time", Fmt.Duration(simulation.Sim.Env.Met)));
            stats.Add(Stat("Commands sent", simulation.StatCommands.ToString(CultureInfo.InvariantCulture)));
            stats.Add(Stat("Lowest battery", simulation.StatMinSoc.ToString("0") + "%"));
            stats.Add(Stat("Fastest spin", simulation.StatMaxRate.ToString("0.0") + " deg/s"));
            stats.Add(Stat("Alarms", simulation.StatAlarms.ToString(CultureInfo.InvariantCulture)));
            stats.Add(Stat("Steps done", Math.Min(run.CurrentStep, run.StepCount) + " of " + run.StepCount));
            card.Add(stats);

            VisualElement buttons = OpsStyle.Row();
            buttons.style.marginTop = 20;
            ScenarioDef next = simulation.NextScenario(d);
            if (won && next != null)
                buttons.Add(OpsStyle.Primary("Next lesson: " + next.title, () => { CloseModal(); simulation.StartScenario(next); }, 0));
            buttons.Add(won ? OpsStyle.Button("Fly it again", Retry, 0, 12) : OpsStyle.Primary("Try again", Retry, 140));
            buttons.Add(OpsStyle.Button("All lessons", () => { CloseModal(); ShowBoard(); }, 0, 12));
            buttons.Add(OpsStyle.Button("Keep flying", () => { CloseModal(); simulation.Paused = false; }, 0, 12));
            card.Add(buttons);
            _modal.Add(card);
            _modal.style.display = DisplayStyle.Flex;
            _modalVisible = true;
        }

        private void Retry()
        {
            CloseModal();
            simulation.Restart();
        }

        private static VisualElement Stat(string label, string value)
        {
            var s = new VisualElement();
            s.style.width = 180;
            s.style.marginBottom = 12;
            s.Add(OpsStyle.Number(value, 20, OpsStyle.Text));
            s.Add(OpsStyle.Label(label, 11, false, OpsStyle.Muted));
            return s;
        }

        // ================================================================== keyboard

        private void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null || simulation == null || simulation.Sim == null || TypingInField()) return;
            if (_modalVisible || _boardVisible) return;
            if (k.spaceKey.wasPressedThisFrame) TogglePause();
            if (k.digit1Key.wasPressedThisFrame) SetWarp(_warps[0]);
            if (k.digit2Key.wasPressedThisFrame) SetWarp(_warps[1]);
            if (k.digit3Key.wasPressedThisFrame) SetWarp(_warps[2]);
            if (k.digit4Key.wasPressedThisFrame) SetWarp(_warps[3]);
            if (k.digit5Key.wasPressedThisFrame) SetWarp(_warps[4]);
            if (k.digit6Key.wasPressedThisFrame) SetWarp(_warps[5]);
            if (k.iKey.wasPressedThisFrame) ToggleInstructor();
            if (k.hKey.wasPressedThisFrame) ToggleDock();
#endif
        }

        private bool TypingInField()
        {
            if (_root == null || _root.panel == null || _root.panel.focusController == null) return false;
            var focused = _root.panel.focusController.focusedElement as VisualElement;
            return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
        }

        // ================================================================== simulation events

        private void OnSimulationStarted()
        {
            if (!_built) return;
            HideBoard();
            CloseModal();

            _tmScroll.Clear();
            _tmGroups.Clear();
            _tmRows.Clear();
            _tmValues.Clear();
            _filterTile = null;
            foreach (Vital v in _vitals) v.Spark.ResetValues();
            foreach (Tile t in _tiles) { t.Unacked = false; t.Level = 0; }
            _alarmUnacked = false;
            foreach (KeyValuePair<VisualElement, float> t in _toasts) t.Key.RemoveFromHierarchy();
            _toasts.Clear();
            _seenEventVersion = simulation.Sim.Events.Version;

            RebuildCommandIndex();
            RebuildCards();
            RebuildRules();
            _map.RebuildLabels();
            _lessonStepShown = -99;
            _hintShown = false;
            _debriefShownFor = ScenarioState.Running;
            _commandResult.text = "";
            _instructorResult.text = "";
            if (_instructorVisible) RebuildFaultTargets();
            SelectTab("Command");
            _slowTimer = 0;

            if (simulation.AwaitingBriefing)
                ShowBriefing();
        }

        private void OpenBuilder()
        {
            SatelliteBuilderSceneFactory.Open(_document.panelSettings);
        }
    }
}
