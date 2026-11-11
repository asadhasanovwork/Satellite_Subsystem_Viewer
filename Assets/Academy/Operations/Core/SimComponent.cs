using System;
using System.Collections.Generic;
using System.Globalization;

namespace Academy.Operations
{
    /// <summary>
    /// One piece of hardware in the simulation (the NOS3 "component" idea): a behaviour model plus its
    /// telemetry, commands and instructor faults. The 3D part comes from the Builder slot with the same id.
    /// </summary>
    public abstract class SimComponent
    {
        public string Id;          // Builder slot id, e.g. "eps.battery"
        public string Name;        // Builder slot name
        public string Kind;        // model kind from operations_models.json
        public string Subsystem;   // Builder subsystem name
        public string VariantName; // selected Builder variant
        public float PowerW;       // power draw when on (W)
        public bool Switchable = true;
        /// <summary>Essential units stay on in safe mode and load shedding.</summary>
        public bool Essential;

        public bool On = true;
        public bool Offline;       // instructor fault: the unit is dead

        protected SatelliteSim Sim;

        public virtual void Attach(SatelliteSim sim) { Sim = sim; }

        /// <summary>Powered, switched on and not failed.</summary>
        public virtual bool Working { get { return On && !Offline && (Sim == null || Sim.Power.BusAlive); } }

        public virtual double PowerDraw { get { return On && !Offline ? PowerW : 0; } }

        public virtual void Step(double dt) { }

        public virtual void WriteTelemetry(TelemetryStore tm)
        {
            if (Switchable || Offline)
                tm.SetText(Id + ".state", Subsystem, Name + " state", StateText, On && !Offline ? 1 : 0);
        }

        protected string StateText { get { return Offline ? "FAILED" : (On ? "ON" : "OFF"); } }

        public virtual void GetCommands(List<CommandSpec> list)
        {
            if (Switchable)
            {
                list.Add(new CommandSpec("ON", "", "Switch the unit on"));
                list.Add(new CommandSpec("OFF", "", "Switch the unit off"));
            }
        }

        /// <summary>Execute a command. Return a result message, or null if the command is unknown.</summary>
        public virtual string Execute(string command, string[] args)
        {
            if (command == "ON" || command == "OFF")
            {
                if (!Switchable)
                    return Name + " cannot be switched from the ground.";
                SetOn(command == "ON");
                return Name + " " + command + (Offline ? " (but the unit has failed)" : "");
            }
            return null;
        }

        public virtual void SetOn(bool on)
        {
            On = on;
        }

        public virtual void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("OFFLINE", "", "The unit stops working"));
        }

        public virtual string InjectFault(string fault, double value)
        {
            if (fault == "OFFLINE")
            {
                Offline = true;
                return Name + " failed";
            }
            return null;
        }

        public virtual void ClearFaults()
        {
            Offline = false;
        }

        protected double Met { get { return Sim != null ? Sim.Env.Met : 0; } }

        protected void Log(EventSeverity severity, string text)
        {
            if (Sim != null)
                Sim.Events.Add(Sim.Env.Met, severity, Name, text);
        }

        protected static int ArgInt(string[] args, int index, int fallback)
        {
            int v;
            return args != null && args.Length > index && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        protected static double ArgDouble(string[] args, int index, double fallback)
        {
            return args != null && args.Length > index ? Fmt.ParseDouble(args[index], fallback) : fallback;
        }

        protected static string ArgText(string[] args, int index, string fallback)
        {
            return args != null && args.Length > index ? args[index].ToUpperInvariant() : fallback;
        }
    }

    /// <summary>Structure, harness, MLI, antennas... present but with no behaviour of their own.</summary>
    public sealed class PassiveComponent : SimComponent
    {
        public PassiveComponent() { Switchable = false; }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            if (Offline)
                base.WriteTelemetry(tm);
        }

        public override void GetFaults(List<FaultSpec> list) { }
    }
}
