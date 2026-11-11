using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    public enum ObcState { Boot, Run, Safe, Hung, Off }

    /// <summary>Onboard computer: runs the attitude software, rules and stored commands. Reset = reboot into safe mode.</summary>
    public sealed class ComputerComponent : SimComponent
    {
        public ObcState State = ObcState.Run;
        public double BootSeconds = 30;
        public double WatchdogSeconds = 120;
        public int Reboots;
        private double _timer;

        public ComputerComponent() { Switchable = false; Essential = true; }

        /// <summary>Software is executing (attitude control, rules, sequencer, command handling).</summary>
        public bool Running { get { return Working && (State == ObcState.Run || State == ObcState.Safe); } }

        public override void Step(double dt)
        {
            if (!Sim.Power.BusAlive || Offline)
            {
                State = ObcState.Off;
                return;
            }
            switch (State)
            {
                case ObcState.Off:
                    Reboot("power restored");
                    break;
                case ObcState.Boot:
                    _timer -= dt;
                    if (_timer <= 0)
                    {
                        State = ObcState.Safe;
                        Log(EventSeverity.Warning, "Computer booted into SAFE MODE");
                        Sim.Obsw.EnterSafeMode("computer reboot");
                    }
                    break;
                case ObcState.Hung:
                    _timer -= dt;
                    if (_timer <= 0)
                        Reboot("watchdog timeout");
                    break;
            }
        }

        public void Reboot(string reason)
        {
            State = ObcState.Boot;
            _timer = BootSeconds;
            Reboots++;
            Log(EventSeverity.Alarm, "Computer reset (" + reason + "), booting...");
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            list.Add(new CommandSpec("RESET", "", "Reboot the computer (it comes back in safe mode)"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "RESET")
            {
                Reboot("ground command");
                return "Computer rebooting";
            }
            return base.Execute(command, args);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            tm.SetText(Id + ".state", Subsystem, "Computer", State.ToString().ToUpperInvariant(), State == ObcState.Run ? 1 : (State == ObcState.Safe ? 2 : 0));
            tm.Set(Id + ".reboots", Subsystem, "Reboot count", Reboots, "", 0);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("RESET", "", "Unexpected reboot (e.g. radiation upset)"));
            list.Add(new FaultSpec("HANG", "", "Software freezes until the watchdog resets it"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "RESET") { Reboot("single-event upset"); return "Computer upset: rebooting"; }
            if (fault == "HANG")
            {
                State = ObcState.Hung;
                _timer = WatchdogSeconds;
                Log(EventSeverity.Alarm, "Computer software stopped responding");
                return "Computer hung";
            }
            return base.InjectFault(fault, value);
        }
    }

    /// <summary>Mass memory for payload data.</summary>
    public sealed class StorageComponent : SimComponent
    {
        public double CapacityMB = 8000;
        public double UsedMB;
        public double DownlinkedMB;
        public double CollectedMB;

        public StorageComponent() { Switchable = false; }

        public double Store(double mb)
        {
            double room = Math.Max(0, CapacityMB - UsedMB);
            double stored = Math.Min(room, mb);
            UsedMB += stored;
            CollectedMB += stored;
            return stored;
        }

        public double Take(double mb)
        {
            double taken = Math.Min(UsedMB, mb);
            UsedMB -= taken;
            DownlinkedMB += taken;
            return taken;
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.Set(Id + ".used", Subsystem, "Stored data", UsedMB, "MB", 1, double.NaN, CapacityMB * 0.9);
            tm.Set(Id + ".collected", Subsystem, "Data collected (total)", CollectedMB, "MB", 1);
            tm.Set(Id + ".downlinked", Subsystem, "Data delivered to ground", DownlinkedMB, "MB", 1);
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            list.Add(new CommandSpec("CLEAR", "", "Delete all stored data"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "CLEAR") { UsedMB = 0; return "Storage cleared"; }
            return base.Execute(command, args);
        }

        public override void GetFaults(List<FaultSpec> list)
        {
            list.Add(new FaultSpec("CORRUPT", "", "Stored data lost"));
        }

        public override string InjectFault(string fault, double value)
        {
            if (fault == "CORRUPT") { UsedMB = 0; return "Stored data corrupted and lost"; }
            return base.InjectFault(fault, value);
        }
    }

    /// <summary>Radio: "ttc" carries commands and telemetry, "data" carries payload data to the ground.</summary>
    public sealed class RadioComponent : SimComponent
    {
        public string Role = "ttc";
        public double RateKbps = 9.6;
        public bool Transmitting;

        public bool IsTtc { get { return Role == "ttc"; } }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            tm.SetText(Id + ".tx", Subsystem, Name + " link", Transmitting ? "LINK UP" : "NO LINK", Transmitting ? 1 : 0);
        }
    }

    /// <summary>
    /// Payload unit. Types: imager (collects data over the Earth in daylight), science (collects continuously),
    /// clock (needs a warm-up before it is ready), chain (part of a navigation/communication payload chain),
    /// comms (amplifiers: CHANNELS sets how many are on).
    /// </summary>
    public sealed class PayloadComponent : SimComponent
    {
        public string PayloadType = "chain";
        public double DataRateMBps;        // imager / science
        public double WarmupSeconds = 1800;// clock
        public int Channels = 1, MaxChannels = 1; // comms
        public bool ChainMember = true;

        public bool Ready { get; private set; }
        public bool Collecting { get; private set; }
        private double _warm;

        public override double PowerDraw
        {
            get
            {
                if (!On || Offline) return 0;
                if (PayloadType == "comms" && MaxChannels > 0)
                    return PowerW * (0.1 + 0.9 * Channels / (double)MaxChannels);
                return PowerW;
            }
        }

        /// <summary>Start already warmed up (free flight).</summary>
        public void StartReady()
        {
            _warm = WarmupSeconds;
            Ready = On && !Offline;
        }

        public override void SetOn(bool on)
        {
            if (!on) { _warm = 0; Ready = false; }
            base.SetOn(on);
        }

        public override void Step(double dt)
        {
            Collecting = false;
            if (!Working)
            {
                _warm = 0;
                Ready = false;
                return;
            }
            if (PayloadType == "clock")
            {
                _warm += dt;
                if (!Ready && _warm >= WarmupSeconds)
                {
                    Ready = true;
                    Log(EventSeverity.Info, Name + " warmed up and locked: ready");
                }
                return;
            }
            Ready = true;
            if (PayloadType == "imager" || PayloadType == "science")
            {
                bool canCollect = PayloadType == "science" || (Sim.Env.InSun && Sim.Adcs.PayloadPointingOk);
                if (canCollect && Sim.Storage != null)
                {
                    double stored = Sim.Storage.Store(DataRateMBps * dt);
                    Collecting = stored > 0;
                }
            }
        }

        public override void GetCommands(List<CommandSpec> list)
        {
            base.GetCommands(list);
            if (PayloadType == "comms")
                list.Add(new CommandSpec("CHANNELS", "<1-" + MaxChannels + ">", "Number of amplifier channels switched on"));
        }

        public override string Execute(string command, string[] args)
        {
            if (command == "CHANNELS" && PayloadType == "comms")
            {
                Channels = Math.Max(1, Math.Min(MaxChannels, ArgInt(args, 0, MaxChannels)));
                return Channels + " of " + MaxChannels + " channels on";
            }
            return base.Execute(command, args);
        }

        public override void WriteTelemetry(TelemetryStore tm)
        {
            base.WriteTelemetry(tm);
            if (PayloadType == "clock")
                tm.SetText(Id + ".ready", Subsystem, Name + " lock", Ready ? "LOCKED" : (Working ? "WARMING UP " + (int)(100 * _warm / Math.Max(1, WarmupSeconds)) + "%" : "OFF"), Ready ? 1 : 0);
            if (PayloadType == "imager" || PayloadType == "science")
                tm.SetText(Id + ".collecting", Subsystem, Name + " data", Collecting ? "COLLECTING" : "IDLE", Collecting ? 1 : 0);
            if (PayloadType == "comms")
                tm.Set(Id + ".channels", Subsystem, "Channels on", On ? Channels : 0, "", 0);
        }
    }
}
