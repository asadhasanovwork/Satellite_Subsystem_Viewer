using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Academy.Operations
{
    public enum CommandSource { Ground, Onboard, Instructor }

    public sealed class ScheduledCommand
    {
        public double Met;
        public string Command;
        public bool Done;
    }

    public sealed class RuleState
    {
        public RuleDef Def;
        public double Timer;
        public bool Triggered;
        public int FireCount;
    }

    /// <summary>
    /// Flight software services: command handling, limit checking (rules), the stored-command sequencer
    /// and safe mode.
    /// </summary>
    public sealed class OnboardSoftware
    {
        private readonly SatelliteSim _sim;
        public readonly List<RuleState> Rules = new List<RuleState>();
        public readonly List<ScheduledCommand> Schedule = new List<ScheduledCommand>();
        public bool SafeMode { get; private set; }
        public int CommandsExecuted { get; private set; }
        /// <summary>Raised for every command accepted from the ground (scenarios check these).</summary>
        public event Action<string> GroundCommandAccepted;

        public OnboardSoftware(SatelliteSim sim, IEnumerable<RuleDef> rules)
        {
            _sim = sim;
            if (rules != null)
                foreach (RuleDef r in rules)
                    AddRule(r);
        }

        public void AddRule(RuleDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.id))
                return;
            Rules.RemoveAll(r => r.Def.id == def.id);
            Rules.Add(new RuleState { Def = def });
        }

        private bool Running { get { return _sim.Computer == null || _sim.Computer.Running; } }

        // ------------------------------------------------------------------ periodic (1 Hz)

        public void Tick(double dt)
        {
            if (!Running)
                return;

            // Stored commands
            foreach (ScheduledCommand c in Schedule)
            {
                if (c.Done || c.Met > _sim.Env.Met)
                    continue;
                c.Done = true;
                string result = Execute(c.Command, CommandSource.Onboard);
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "SEQUENCER", "Stored command '" + c.Command + "': " + result);
            }

            // Limit rules
            foreach (RuleState r in Rules)
            {
                if (!r.Def.enabled)
                {
                    r.Timer = 0;
                    continue;
                }
                TelemetryPoint p;
                if (!_sim.Telemetry.TryGet(r.Def.point, out p))
                    continue;
                bool beyond = r.Def.op == ">" ? p.Value > r.Def.value : p.Value < r.Def.value;
                if (!beyond)
                {
                    r.Timer = 0;
                    r.Triggered = false;
                    continue;
                }
                r.Timer += dt;
                if (r.Triggered || r.Timer < r.Def.seconds)
                    continue;
                r.Triggered = true;
                r.FireCount++;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Warning, "RULE " + r.Def.id,
                    (string.IsNullOrEmpty(r.Def.description) ? r.Def.point + " " + r.Def.op + " " + r.Def.value : r.Def.description) + ": running " + r.Def.actions.Count + " command(s)");
                foreach (string action in r.Def.actions)
                    Execute(action, CommandSource.Onboard);
            }

            // Hard-coded survival rule (cannot be disabled): very low battery -> safe mode
            if (!SafeMode && _sim.Power.Battery != null && _sim.Power.Battery.Soc < 0.10)
                EnterSafeMode("battery below 10%");
        }

        public void EnterSafeMode(string reason)
        {
            SafeMode = true;
            foreach (SimComponent c in _sim.Components)
                if (c.Switchable && !c.Essential && IsLoad(c))
                    c.SetOn(false);
            if (_sim.Adcs != null)
            {
                if (_sim.Adcs.HasWheels && _sim.Adcs.Mode != AdcsMode.Detumble)
                    _sim.Adcs.Mode = AdcsMode.SunSafe;
                else if (!_sim.Adcs.HasWheels)
                    _sim.Adcs.Mode = AdcsMode.Detumble;
            }
            _sim.Events.Add(_sim.Env.Met, EventSeverity.Alarm, "OBSW", "SAFE MODE (" + reason + "): payload and non-essential units off, attitude to survival mode");
        }

        /// <summary>Units safe mode switches off: payloads, heaters stay, attitude hardware stays.</summary>
        private static bool IsLoad(SimComponent c)
        {
            return c is PayloadComponent || (c is RadioComponent && !((RadioComponent)c).IsTtc);
        }

        // ------------------------------------------------------------------ commands

        /// <summary>
        /// Execute "TARGET COMMAND [args]". Targets: a component id (e.g. eps.battery), ADCS, OBSW.
        /// Instructor-only: SIM ... (see FaultInjector).
        /// </summary>
        public string Execute(string line, CommandSource source)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "Empty command";
            string[] tokens = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string target = tokens[0];
            string command = tokens.Length > 1 ? tokens[1].ToUpperInvariant() : "";
            string[] args = tokens.Length > 2 ? Sub(tokens, 2) : new string[0];

            if (target.Equals("SIM", StringComparison.OrdinalIgnoreCase))
            {
                if (source != CommandSource.Instructor)
                    return "SIM commands are for the instructor only";
                return _sim.Faults.Execute(command, args);
            }

            if (source == CommandSource.Ground)
            {
                if (!_sim.Link.CanUplink)
                    return "NOT SENT: no contact with the satellite (" + _sim.Link.NoUplinkReason + "). Use stored commands or wait for a pass.";
            }
            if (source != CommandSource.Instructor && !Running)
                return "REJECTED: the onboard computer is not running";

            string result = Dispatch(target, command, args);
            CommandsExecuted++;
            if (source == CommandSource.Ground)
            {
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "GROUND", line.Trim() + " -> " + result);
                if (GroundCommandAccepted != null && !result.StartsWith("Unknown", StringComparison.Ordinal))
                    GroundCommandAccepted(line.Trim());
            }
            return result;
        }

        private string Dispatch(string target, string command, string[] args)
        {
            if (target.Equals("ADCS", StringComparison.OrdinalIgnoreCase))
                return _sim.Adcs.Execute(command, args) ?? "Unknown ADCS command '" + command + "'";

            if (target.Equals("OBSW", StringComparison.OrdinalIgnoreCase))
                return ExecuteObsw(command, args);

            SimComponent c = _sim.Find(target);
            if (c == null)
                return "Unknown target '" + target + "'";
            string r = c.Execute(command, args);
            return r ?? "Unknown command '" + command + "' for " + c.Name;
        }

        private string ExecuteObsw(string command, string[] args)
        {
            switch (command)
            {
                case "SAFE":
                    EnterSafeMode("ground command");
                    return "Safe mode entered";
                case "NOMINAL":
                    SafeMode = false;
                    if (_sim.Computer != null && _sim.Computer.State == ObcState.Safe)
                        _sim.Computer.State = ObcState.Run;
                    return "Safe mode exited (switch units back on yourself)";
                case "SCHEDULE":
                case "SCHEDULE_IN":
                    {
                        if (args.Length < 2)
                            return "Usage: OBSW " + command + " <seconds> <command...>";
                        double t = Fmt.ParseDouble(args[0], double.NaN);
                        if (double.IsNaN(t))
                            return "Time must be a number of seconds";
                        double met = command == "SCHEDULE" ? t : _sim.Env.Met + t;
                        string stored = string.Join(" ", Sub(args, 1));
                        if (stored.StartsWith("OBSW SCHEDULE", StringComparison.OrdinalIgnoreCase))
                            return "Stored commands cannot schedule other commands";
                        Schedule.Add(new ScheduledCommand { Met = met, Command = stored });
                        Schedule.Sort((a, b) => a.Met.CompareTo(b.Met));
                        return "Stored '" + stored + "' for " + Fmt.Met(met);
                    }
                case "CLEAR_SCHEDULE":
                    Schedule.Clear();
                    return "Stored command list cleared";
                case "RULE":
                    {
                        if (args.Length < 2)
                            return "Usage: OBSW RULE <id> ON|OFF";
                        RuleState r = Rules.Find(x => x.Def.id.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                        if (r == null)
                            return "No rule '" + args[0] + "'";
                        r.Def.enabled = args[1].ToUpperInvariant() != "OFF";
                        r.Triggered = false;
                        r.Timer = 0;
                        return "Rule " + r.Def.id + (r.Def.enabled ? " enabled" : " disabled");
                    }
                default:
                    return "Unknown OBSW command '" + command + "'";
            }
        }

        public void GetCommands(List<CommandSpec> list)
        {
            list.Add(new CommandSpec("SAFE", "", "Enter safe mode"));
            list.Add(new CommandSpec("NOMINAL", "", "Leave safe mode"));
            list.Add(new CommandSpec("SCHEDULE_IN", "<seconds> <command>", "Store a command to run later"));
            list.Add(new CommandSpec("CLEAR_SCHEDULE", "", "Delete all stored commands"));
            list.Add(new CommandSpec("RULE", "<id> ON|OFF", "Enable or disable an onboard rule"));
        }

        public void WriteTelemetry(TelemetryStore tm)
        {
            int pending = 0;
            foreach (ScheduledCommand c in Schedule) if (!c.Done) pending++;
            tm.SetText("obsw.mode", "Computer", "Software mode", SafeMode ? "SAFE" : "NOMINAL", SafeMode ? 1 : 0);
            tm.Set("obsw.stored", "Computer", "Stored commands pending", pending, "", 0);
        }

        private static string[] Sub(string[] a, int start)
        {
            var r = new string[Math.Max(0, a.Length - start)];
            Array.Copy(a, start, r, 0, r.Length);
            return r;
        }
    }

    /// <summary>Instructor back doors (like the NOS3 simulator terminal): faults and environment changes.</summary>
    public sealed class FaultInjector
    {
        private readonly SatelliteSim _sim;

        public FaultInjector(SatelliteSim sim) { _sim = sim; }

        /// <summary>
        /// SIM FAULT &lt;component&gt; &lt;fault&gt; [value] | SIM CLEAR &lt;component|ALL&gt; | SIM TUMBLE &lt;deg/s&gt;
        /// | SIM SET_SOC &lt;%&gt; | SIM TEMP &lt;C&gt;
        /// </summary>
        public string Execute(string command, string[] args)
        {
            switch (command)
            {
                case "FAULT":
                    {
                        if (args.Length < 2) return "Usage: SIM FAULT <component> <fault> [value]";
                        SimComponent c = _sim.Find(args[0]);
                        if (c == null) return "Unknown component '" + args[0] + "'";
                        double v = args.Length > 2 ? Fmt.ParseDouble(args[2], double.NaN) : double.NaN;
                        string r = c.InjectFault(args[1].ToUpperInvariant(), v) ?? "Unknown fault '" + args[1] + "' for " + c.Name;
                        _sim.Events.Add(_sim.Env.Met, EventSeverity.Warning, "INSTRUCTOR", r);
                        return r;
                    }
                case "CLEAR":
                    {
                        string id = args.Length > 0 ? args[0] : "ALL";
                        if (id.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (SimComponent c in _sim.Components) c.ClearFaults();
                            _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "INSTRUCTOR", "All faults cleared");
                            return "All faults cleared";
                        }
                        SimComponent comp = _sim.Find(id);
                        if (comp == null) return "Unknown component '" + id + "'";
                        comp.ClearFaults();
                        return comp.Name + " faults cleared";
                    }
                case "TUMBLE":
                    {
                        double dps = args.Length > 0 ? Fmt.ParseDouble(args[0], 5) : 5;
                        Vec3d axis = new Vec3d(_sim.Env.Gaussian(), _sim.Env.Gaussian(), _sim.Env.Gaussian()).Normalized;
                        _sim.Env.BodyRate += axis * (dps * OpsMath.Deg2Rad);
                        _sim.Events.Add(_sim.Env.Met, EventSeverity.Warning, "INSTRUCTOR", "Satellite knocked into a " + dps + " deg/s tumble");
                        return "Tumble applied";
                    }
                case "SET_SOC":
                    {
                        if (_sim.Power.Battery == null) return "No battery";
                        return _sim.Power.Battery.InjectFault("SET_SOC", args.Length > 0 ? Fmt.ParseDouble(args[0], 30) : 30);
                    }
                case "WHEEL_MOMENTUM":
                    {
                        ReactionWheelSet w = _sim.Adcs.Wheels;
                        if (w == null) return "No reaction wheels";
                        double pct = args.Length > 0 ? Fmt.ParseDouble(args[0], 75) : 75;
                        // Spin all wheels the same way: net momentum stored in the wheels
                        Vec3d dir = Vec3d.Zero;
                        foreach (Wheel wh in w.Wheels) dir += wh.Axis;
                        dir = dir.SqrLength > 1e-9 ? dir.Normalized : w.Wheels[0].Axis;
                        foreach (Wheel wh in w.Wheels) wh.H = 0;
                        Vec3d target = dir * (pct / 100.0 * w.HMax);
                        // least-squares: put the target on the wheels
                        foreach (Wheel wh in w.Wheels) wh.H = OpsMath.Clamp(Vec3d.Dot(target, wh.Axis) * 3.0 / Math.Max(3, w.Wheels.Count), -w.HMax, w.HMax);
                        _sim.Events.Add(_sim.Env.Met, EventSeverity.Warning, "INSTRUCTOR", "Wheels loaded with stored momentum");
                        return "Wheel momentum set";
                    }
                case "TEMP":
                    {
                        _sim.Thermal.TemperatureC = args.Length > 0 ? Fmt.ParseDouble(args[0], 0) : 0;
                        return "Temperature set";
                    }
                default:
                    return "Unknown SIM command. Use FAULT, CLEAR, TUMBLE, SET_SOC, WHEEL_MOMENTUM or TEMP.";
            }
        }
    }
}
