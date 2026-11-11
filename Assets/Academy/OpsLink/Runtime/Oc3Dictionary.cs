using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Academy.Operations;

namespace Academy.OpsLink
{
    public enum Oc3Type { Float64, UInt8Enum, String }

    [Serializable]
    public sealed class Oc3Item
    {
        public string Name;        // OpenC3 item name, e.g. EPS_BATTERY_SOC
        public string Key;         // simulator telemetry key, e.g. eps.battery.soc
        public string Description;
        public string Unit;
        public int Decimals;
        public double Low = double.NaN, High = double.NaN;
        public Oc3Type Type = Oc3Type.Float64;
        public int StringBytes;    // for String
    }

    [Serializable]
    public sealed class Oc3TlmPacket
    {
        public string Name;
        public ushort Id;
        public string Description;
        public List<Oc3Item> Items = new List<Oc3Item>();
    }

    [Serializable]
    public sealed class Oc3Param
    {
        public string Name;
        public string Description;
        public Oc3Type Type;
        public string[] States;    // for UInt8Enum
        public int StringBytes;    // for String
    }

    [Serializable]
    public sealed class Oc3Command
    {
        public string Name;        // OpenC3 command name, e.g. ADCS_MODE
        public ushort Id;
        public string Description;
        public string SimTarget;   // simulator target, e.g. "adcs.wheels", "ADCS"; null for link-level commands
        public string SimCommand;  // simulator command word, e.g. "MODE"
        public List<Oc3Param> Params = new List<Oc3Param>();
    }

    /// <summary>
    /// The OpenC3 command/telemetry dictionary for one satellite, derived from a running
    /// <see cref="SatelliteSim"/>: every telemetry point becomes an OpenC3 item, every
    /// component command an OpenC3 command. Built once at launch; the packet layout is
    /// then fixed for the session (it is what the generated plugin declares).
    /// </summary>
    [Serializable]
    public sealed class Oc3Dictionary
    {
        // Link-level packet ids (subsystem packets start at 1)
        public const ushort IdSimStatus = 900, IdCmdAck = 901, IdEvent = 902;
        // Link-level command ids (component commands start at 1)
        public const ushort IdRawLine = 900, IdSimWarp = 901, IdSimPause = 902, IdSimResume = 903, IdSimLine = 904;

        public const int ShortString = 32, LongString = 128;

        public string TargetName;
        public string SatelliteName;
        public List<Oc3TlmPacket> Telemetry = new List<Oc3TlmPacket>();
        public List<Oc3Command> Commands = new List<Oc3Command>();

        public Oc3TlmPacket TlmById(ushort id) => Telemetry.FirstOrDefault(p => p.Id == id);
        public Oc3Command CmdById(ushort id) => Commands.FirstOrDefault(c => c.Id == id);

        public static Oc3Dictionary Build(SatelliteSim sim, string targetName)
        {
            var d = new Oc3Dictionary { TargetName = targetName, SatelliteName = sim.SatelliteName };
            sim.WriteTelemetry();
            d.BuildTelemetry(sim.Telemetry);
            d.BuildCommands(sim);
            return d;
        }

        // ------------------------------------------------------------------ telemetry

        void BuildTelemetry(TelemetryStore store)
        {
            var byGroup = new Dictionary<string, Oc3TlmPacket>(StringComparer.OrdinalIgnoreCase);
            var usedPacketNames = new HashSet<string>();
            ushort nextId = 1;
            foreach (TelemetryPoint p in store.Points)
            {
                string group = string.IsNullOrWhiteSpace(p.Group) ? "GENERAL" : p.Group;
                if (!byGroup.TryGetValue(group, out var pkt))
                {
                    pkt = new Oc3TlmPacket
                    {
                        Name = Unique(Sanitize(group, "GROUP"), usedPacketNames),
                        Id = nextId++,
                        Description = group + " telemetry",
                    };
                    byGroup[group] = pkt;
                    Telemetry.Add(pkt);
                }
                var used = new HashSet<string>(pkt.Items.Select(i => i.Name));
                string name = Unique(Sanitize(p.Key, "ITEM"), used);
                pkt.Items.Add(new Oc3Item
                {
                    Name = name, Key = p.Key, Description = p.Label ?? p.Key,
                    Unit = p.Unit, Decimals = Math.Max(0, p.Decimals), Low = p.Low, High = p.High,
                });
                if (!string.IsNullOrEmpty(p.Text))
                    pkt.Items.Add(new Oc3Item
                    {
                        Name = Unique(name + "_TXT", new HashSet<string>(pkt.Items.Select(i => i.Name))),
                        Key = p.Key, Description = (p.Label ?? p.Key) + " (text)",
                        Type = Oc3Type.String, StringBytes = ShortString,
                    });
            }

            var sim = new Oc3TlmPacket { Name = "SIM_STATUS", Id = IdSimStatus, Description = "Simulator and ground-link status" };
            sim.Items.Add(new Oc3Item { Name = "MET", Key = "#met", Description = "Mission elapsed time", Unit = "s", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "WARP", Key = "#warp", Description = "Simulation speed (x real time)", Unit = "x", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "PAUSED", Key = "#paused", Description = "1 = simulation paused", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "CONTACT", Key = "#contact", Description = "1 = ground station in contact", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "ELEVATION", Key = "#elev", Description = "Elevation at the station", Unit = "deg", Decimals = 1 });
            sim.Items.Add(new Oc3Item { Name = "NEXT_AOS_IN", Key = "#aos", Description = "Seconds until next acquisition of signal", Unit = "s", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "NEXT_LOS_IN", Key = "#los", Description = "Seconds until next loss of signal", Unit = "s", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "TLM_AGE", Key = "#tlmage", Description = "Age of the last downlinked telemetry", Unit = "s", Decimals = 0 });
            sim.Items.Add(new Oc3Item { Name = "STATION", Key = "#station", Description = "Ground station", Type = Oc3Type.String, StringBytes = ShortString });
            Telemetry.Add(sim);

            var ack = new Oc3TlmPacket { Name = "CMD_ACK", Id = IdCmdAck, Description = "Result of the last command sent to the satellite" };
            ack.Items.Add(new Oc3Item { Name = "COUNT", Key = "#ackcount", Description = "Commands processed", Decimals = 0 });
            ack.Items.Add(new Oc3Item { Name = "ACCEPTED", Key = "#ackok", Description = "1 = accepted by the satellite", Decimals = 0 });
            ack.Items.Add(new Oc3Item { Name = "COMMAND", Key = "#ackcmd", Description = "Command line", Type = Oc3Type.String, StringBytes = LongString });
            ack.Items.Add(new Oc3Item { Name = "RESULT", Key = "#ackres", Description = "Satellite response", Type = Oc3Type.String, StringBytes = LongString });
            Telemetry.Add(ack);

            var ev = new Oc3TlmPacket { Name = "EVENT", Id = IdEvent, Description = "Onboard and ground events" };
            ev.Items.Add(new Oc3Item { Name = "EVENT_MET", Key = "#evmet", Description = "Event time (MET)", Unit = "s", Decimals = 0 });
            ev.Items.Add(new Oc3Item { Name = "SEVERITY", Key = "#evsev", Description = "0 info, 1 warning, 2 alarm", Decimals = 0, High = 1.5 });
            ev.Items.Add(new Oc3Item { Name = "SOURCE", Key = "#evsrc", Description = "Event source", Type = Oc3Type.String, StringBytes = ShortString });
            ev.Items.Add(new Oc3Item { Name = "TEXT", Key = "#evtxt", Description = "Event text", Type = Oc3Type.String, StringBytes = LongString });
            Telemetry.Add(ev);
        }

        // ------------------------------------------------------------------ commands

        static readonly Regex ArgToken = new Regex(@"<[^>]*>|\S+");

        void BuildCommands(SatelliteSim sim)
        {
            var usedNames = new HashSet<string>();
            ushort nextId = 1;

            var targets = new List<string> { "ADCS", "OBSW" };
            targets.AddRange(sim.Components.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)));

            foreach (string target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (CommandSpec spec in sim.CommandsFor(target))
                {
                    var cmd = new Oc3Command
                    {
                        Name = Unique(Sanitize(target + "_" + spec.Name, "CMD"), usedNames),
                        Id = nextId++,
                        Description = string.IsNullOrEmpty(spec.Help) ? target + " " + spec.Name : spec.Help,
                        SimTarget = target, SimCommand = spec.Name,
                    };
                    ParseArgs(spec.Args, cmd);
                    Commands.Add(cmd);
                }
            }

            var raw = new Oc3Command { Name = "RAW_LINE", Id = IdRawLine, Description = "Send any console command line, e.g. 'eps.battery ON' (power users)" };
            raw.Params.Add(new Oc3Param { Name = "LINE", Description = "Command line", Type = Oc3Type.String, StringBytes = LongString });
            Commands.Add(raw);

            var warp = new Oc3Command { Name = "SIM_SET_WARP", Id = IdSimWarp, Description = "Instructor: simulation speed (1 = real time)" };
            warp.Params.Add(new Oc3Param { Name = "WARP", Description = "x real time (1-1000)", Type = Oc3Type.Float64 });
            Commands.Add(warp);
            Commands.Add(new Oc3Command { Name = "SIM_PAUSE", Id = IdSimPause, Description = "Instructor: pause the simulation" });
            Commands.Add(new Oc3Command { Name = "SIM_RESUME", Id = IdSimResume, Description = "Instructor: resume the simulation" });
            var simLine = new Oc3Command { Name = "SIM_LINE", Id = IdSimLine, Description = "Instructor: simulator command, e.g. 'FAULT adcs.wheels FAIL 1', 'TUMBLE 5'" };
            simLine.Params.Add(new Oc3Param { Name = "LINE", Description = "SIM command (without the SIM prefix)", Type = Oc3Type.String, StringBytes = LongString });
            Commands.Add(simLine);
        }

        static void ParseArgs(string args, Oc3Command cmd)
        {
            if (string.IsNullOrWhiteSpace(args)) return;
            var used = new HashSet<string>();
            int n = 0;
            foreach (Match m in ArgToken.Matches(args))
            {
                n++;
                string tok = m.Value;
                if (tok.StartsWith("<"))
                {
                    string label = tok.Trim('<', '>').Trim();
                    string lower = label.ToLowerInvariant();
                    bool isText = lower.Contains("command") || lower == "id" || lower.Contains("name") || lower.Contains("text");
                    cmd.Params.Add(new Oc3Param
                    {
                        Name = Unique(ParamName(label, n), used),
                        Description = label,
                        Type = isText ? Oc3Type.String : Oc3Type.Float64,
                        StringBytes = lower.Contains("command") ? LongString : ShortString,
                    });
                }
                else
                {
                    string[] states = tok.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                    cmd.Params.Add(new Oc3Param
                    {
                        Name = Unique(n == 1 ? "OPTION" : "OPTION" + n, used),
                        Description = string.Join(" | ", states),
                        Type = Oc3Type.UInt8Enum,
                        States = states,
                    });
                }
            }
        }

        static string ParamName(string label, int position)
        {
            string first = label.Split(new[] { ' ', '/', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string s = Sanitize(first, "");
            return string.IsNullOrEmpty(s) || char.IsDigit(s[0]) ? "ARG" + position : s;
        }

        // ------------------------------------------------------------------ names

        public static string Sanitize(string raw, string fallback)
        {
            var sb = new StringBuilder();
            foreach (char c in (raw ?? "").ToUpperInvariant())
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            string s = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
            if (s.Length == 0) s = fallback;
            if (s.Length > 0 && char.IsDigit(s[0])) s = "N" + s;
            return s.Length > 48 ? s.Substring(0, 48) : s;
        }

        static string Unique(string name, HashSet<string> used)
        {
            string n = name;
            for (int i = 2; used.Contains(n); i++) n = name + "_" + i;
            used.Add(n);
            return n;
        }

        public static string FormatNumber(double v) =>
            Math.Abs(v - Math.Round(v)) < 1e-9 ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                                                : v.ToString("R", CultureInfo.InvariantCulture);
    }
}
