using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Academy.Operations;
using UnityEngine;

namespace Academy.OpsLink
{
    /// <summary>
    /// Makes the Unity simulator look like a real spacecraft to OpenC3 COSMOS.
    /// Listens on a TCP port; OpenC3's tcpip_client_interface connects to it. Frames are
    /// [UINT32 total length][UINT16 id][...] big-endian, as declared by <see cref="Oc3PluginWriter"/>.
    /// Telemetry is the ground's last downlinked copy, so it only refreshes during passes.
    /// </summary>
    public sealed class SpacecraftLink : MonoBehaviour
    {
        public OperationsSimulation simulation;
        public Oc3Dictionary dictionary;
        public int port = 7779;
        public bool listenOnAllInterfaces;   // false = loopback only (no firewall prompt)
        public float sendInterval = 1f;      // real seconds

        public bool Connected { get { lock (_lock) return _client != null; } }
        public int CommandsReceived { get; private set; }

        readonly object _lock = new object();
        readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        TcpListener _listener;
        TcpClient _client;
        NetworkStream _stream;
        Thread _acceptThread;
        volatile bool _running;

        float _nextSend;
        double _lastTlmMet = double.NaN;
        int _eventsSent;
        int _ackCount, _ackOk;
        string _ackCmd = "", _ackRes = "";
        bool _ackPending;

        void OnEnable()
        {
            _running = true;
            try
            {
                _listener = new TcpListener(listenOnAllInterfaces ? IPAddress.Any : IPAddress.Loopback, port);
                _listener.Start();
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "OpsLink accept" };
                _acceptThread.Start();
                Debug.Log($"[OpsLink] Spacecraft link listening on {(listenOnAllInterfaces ? "*" : "127.0.0.1")}:{port}");
            }
            catch (Exception e)
            {
                Debug.LogError("[OpsLink] Cannot listen on port " + port + ": " + e.Message);
            }
            if (simulation != null && simulation.Sim != null)
                _eventsSent = simulation.Sim.Events.Events.Count; // only stream new events
        }

        void OnDisable()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            DropClient();
        }

        // ------------------------------------------------------------------ network threads

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { break; }
                c.NoDelay = true;
                lock (_lock)
                {
                    try { _client?.Close(); } catch { }
                    _client = c;
                    _stream = c.GetStream();
                }
                var reader = new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "OpsLink read" };
                reader.Start();
            }
        }

        void ReadLoop(TcpClient c)
        {
            try
            {
                var s = c.GetStream();
                var header = new byte[4];
                while (_running && c.Connected)
                {
                    if (!ReadExact(s, header, 0, 4)) break;
                    int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    if (len < 6 || len > 65536) break; // corrupt stream
                    var frame = new byte[len];
                    Buffer.BlockCopy(header, 0, frame, 0, 4);
                    if (!ReadExact(s, frame, 4, len - 4)) break;
                    _inbox.Enqueue(frame);
                }
            }
            catch { /* disconnected */ }
            lock (_lock) { if (_client == c) { _client = null; _stream = null; } }
            try { c.Close(); } catch { }
        }

        static bool ReadExact(Stream s, byte[] buf, int off, int count)
        {
            while (count > 0)
            {
                int n = s.Read(buf, off, count);
                if (n <= 0) return false;
                off += n; count -= n;
            }
            return true;
        }

        void DropClient()
        {
            lock (_lock)
            {
                try { _client?.Close(); } catch { }
                _client = null; _stream = null;
            }
        }

        void Send(byte[] frame)
        {
            NetworkStream s;
            lock (_lock) s = _stream;
            if (s == null) return;
            try { s.Write(frame, 0, frame.Length); }
            catch { DropClient(); }
        }

        // ------------------------------------------------------------------ main thread

        void Update()
        {
            if (simulation == null || simulation.Sim == null || dictionary == null) return;

            while (_inbox.TryDequeue(out byte[] frame))
                HandleCommand(frame);

            if (!Connected) return;

            if (_ackPending)
            {
                _ackPending = false;
                SendPacket(dictionary.TlmById(Oc3Dictionary.IdCmdAck));
            }

            SatelliteSim sim = simulation.Sim;
            var events = sim.Events.Events;
            for (; _eventsSent < events.Count; _eventsSent++)
                SendEvent(events[_eventsSent]);

            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + sendInterval;

            SendPacket(dictionary.TlmById(Oc3Dictionary.IdSimStatus));

            double tlmMet = sim.Link.LastTelemetryMet;
            bool fresh = !double.IsNaN(tlmMet) && !tlmMet.Equals(_lastTlmMet);
            if (fresh)
            {
                _lastTlmMet = tlmMet;
                foreach (Oc3TlmPacket p in dictionary.Telemetry)
                    if (p.Id < Oc3Dictionary.IdSimStatus)
                        SendPacket(p);
            }
        }

        // ------------------------------------------------------------------ telemetry

        void SendPacket(Oc3TlmPacket p)
        {
            if (p == null) return;
            var w = BeginFrame(p.Id);
            TelemetryStore ground = simulation.Sim.Link.GroundTelemetry;
            foreach (Oc3Item item in p.Items)
            {
                if (item.Type == Oc3Type.String)
                    WriteString(w, StringValue(item, ground), item.StringBytes);
                else
                    WriteDouble(w, NumberValue(item, ground));
            }
            Send(EndFrame(w));
        }

        void SendEvent(SimEvent e)
        {
            Oc3TlmPacket p = dictionary.TlmById(Oc3Dictionary.IdEvent);
            var w = BeginFrame(p.Id);
            WriteDouble(w, e.Met);
            WriteDouble(w, (int)e.Severity);
            WriteString(w, e.Source, Oc3Dictionary.ShortString);
            WriteString(w, e.Text, Oc3Dictionary.LongString);
            Send(EndFrame(w));
        }

        double NumberValue(Oc3Item item, TelemetryStore ground)
        {
            SatelliteSim sim = simulation.Sim;
            GroundLink link = sim.Link;
            double now = sim.Env.Time;
            switch (item.Key)
            {
                case "#met": return sim.Env.Met;
                case "#warp": return simulation.TimeWarp;
                case "#paused": return simulation.Paused ? 1 : 0;
                case "#contact": return link.CanUplink ? 1 : 0;
                case "#elev": return link.Elevation;
                case "#aos": return double.IsNaN(link.NextAos) ? -1 : Math.Max(0, link.NextAos - now);
                case "#los": return double.IsNaN(link.NextLos) || !link.CanUplink ? -1 : Math.Max(0, link.NextLos - now);
                case "#tlmage": return double.IsNaN(link.LastTelemetryMet) ? -1 : sim.Env.Met - link.LastTelemetryMet;
                case "#ackcount": return _ackCount;
                case "#ackok": return _ackOk;
            }
            return ground.TryGet(item.Key, out TelemetryPoint tp) ? tp.Value : double.NaN;
        }

        string StringValue(Oc3Item item, TelemetryStore ground)
        {
            switch (item.Key)
            {
                case "#station": return simulation.Sim.Link.CanUplink ? simulation.Sim.Link.StationName : (simulation.Sim.Link.NextStation ?? "");
                case "#ackcmd": return _ackCmd;
                case "#ackres": return _ackRes;
            }
            return ground.TryGet(item.Key, out TelemetryPoint tp) ? (tp.Text ?? tp.Display) : "";
        }

        // ------------------------------------------------------------------ commands

        void HandleCommand(byte[] frame)
        {
            int pos = 4;
            ushort id = (ushort)((frame[pos] << 8) | frame[pos + 1]);
            pos += 2;
            Oc3Command cmd = dictionary.CmdById(id);
            if (cmd == null) { Ack("(unknown id " + id + ")", "Unknown command id", false); return; }
            CommandsReceived++;

            var args = new StringBuilder();
            string firstString = "";
            double firstNumber = double.NaN;
            foreach (Oc3Param p in cmd.Params)
            {
                string token;
                switch (p.Type)
                {
                    case Oc3Type.UInt8Enum:
                        int idx = frame[pos]; pos += 1;
                        token = idx < p.States.Length ? p.States[idx] : idx.ToString();
                        break;
                    case Oc3Type.String:
                        token = Encoding.UTF8.GetString(frame, pos, Math.Min(p.StringBytes, frame.Length - pos)).TrimEnd('\0').Trim();
                        pos += p.StringBytes;
                        if (firstString.Length == 0) firstString = token;
                        break;
                    default:
                        double v = ReadDouble(frame, pos); pos += 8;
                        if (double.IsNaN(firstNumber)) firstNumber = v;
                        token = Oc3Dictionary.FormatNumber(v);
                        break;
                }
                if (token.Length > 0) args.Append(' ').Append(token);
            }

            switch (cmd.Id)
            {
                case Oc3Dictionary.IdSimWarp:
                    simulation.TimeWarp = Mathf.Clamp((float)firstNumber, 1f, 1000f);
                    Ack("SIM SET_WARP " + Oc3Dictionary.FormatNumber(simulation.TimeWarp), "Warp x" + Oc3Dictionary.FormatNumber(simulation.TimeWarp), true);
                    return;
                case Oc3Dictionary.IdSimPause:
                    simulation.Paused = true; Ack("SIM PAUSE", "Paused", true); return;
                case Oc3Dictionary.IdSimResume:
                    simulation.Paused = false; Ack("SIM RESUME", "Running", true); return;
                case Oc3Dictionary.IdSimLine:
                {
                    string line = "SIM " + firstString;
                    string res = simulation.SendInstructor(line);
                    Ack(line, res, IsAccepted(res));
                    return;
                }
                case Oc3Dictionary.IdRawLine:
                {
                    string res = simulation.SendGround(firstString);
                    Ack(firstString, res, IsAccepted(res));
                    return;
                }
            }

            string lineOut = cmd.SimTarget + " " + cmd.SimCommand + args;
            string result = simulation.SendGround(lineOut);
            Ack(lineOut, result, IsAccepted(result));
        }

        static bool IsAccepted(string result)
        {
            if (string.IsNullOrEmpty(result)) return true;
            string r = result.TrimStart();
            return !(r.StartsWith("NOT SENT", StringComparison.OrdinalIgnoreCase) || r.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase)
                     || r.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) || r.StartsWith("Usage", StringComparison.OrdinalIgnoreCase)
                     || r.StartsWith("Error", StringComparison.OrdinalIgnoreCase));
        }

        void Ack(string line, string result, bool ok)
        {
            _ackCount++;
            _ackOk = ok ? 1 : 0;
            _ackCmd = line;
            _ackRes = result ?? "";
            _ackPending = true;
            Debug.Log($"[OpsLink] {(ok ? "OK " : "NOK")} {line} -> {_ackRes}");
        }

        // ------------------------------------------------------------------ encoding

        MemoryStream BeginFrame(ushort id)
        {
            var w = new MemoryStream(256);
            WriteU32(w, 0); // length, patched in EndFrame
            w.WriteByte((byte)(id >> 8)); w.WriteByte((byte)id);
            DateTime utc = OpsMath.ToUtc(simulation.Sim.Env.Time);
            double unix = (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            uint sec = (uint)Math.Floor(unix);
            WriteU32(w, sec);
            WriteU32(w, (uint)Math.Round((unix - sec) * 1e6) % 1000000u);
            return w;
        }

        static byte[] EndFrame(MemoryStream w)
        {
            byte[] b = w.ToArray();
            int len = b.Length;
            b[0] = (byte)(len >> 24); b[1] = (byte)(len >> 16); b[2] = (byte)(len >> 8); b[3] = (byte)len;
            return b;
        }

        static void WriteU32(Stream w, uint v)
        {
            w.WriteByte((byte)(v >> 24)); w.WriteByte((byte)(v >> 16)); w.WriteByte((byte)(v >> 8)); w.WriteByte((byte)v);
        }

        static void WriteDouble(Stream w, double v)
        {
            byte[] b = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            w.Write(b, 0, 8);
        }

        static double ReadDouble(byte[] buf, int pos)
        {
            var b = new byte[8];
            Buffer.BlockCopy(buf, pos, b, 0, 8);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        }

        static void WriteString(Stream w, string s, int bytes)
        {
            var buf = new byte[bytes];
            byte[] src = Encoding.UTF8.GetBytes(s ?? "");
            Buffer.BlockCopy(src, 0, buf, 0, Math.Min(src.Length, bytes));
            w.Write(buf, 0, bytes);
        }
    }
}
