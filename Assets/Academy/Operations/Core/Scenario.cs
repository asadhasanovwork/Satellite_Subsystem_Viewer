using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    // Scenario lessons in the NOS3 style: learning goals, prerequisites, situation, then steps the learner
    // completes on the console. Files: StreamingAssets/Academy/Operations/Scenarios/*.json

    [Serializable]
    public class ScenarioIndex
    {
        public List<string> scenarios = new List<string>();
    }

    [Serializable]
    public class ScenarioDef
    {
        public string id;
        public string title;
        public string template;          // Builder template id
        public string orbitClass;
        public string level = "Beginner";
        public string summary;
        public List<string> goals = new List<string>();
        public List<string> prerequisites = new List<string>();
        public string situation;
        /// <summary>Variant choices on top of the recommended design.</summary>
        public List<ScenarioSlotChoice> design = new List<ScenarioSlotChoice>();
        public ScenarioStart start = new ScenarioStart();
        public List<ScenarioEvent> faults = new List<ScenarioEvent>();
        public List<ScenarioStep> steps = new List<ScenarioStep>();
        public ScenarioCheck failWhen;
        public string failText;
        public string successText;
    }

    [Serializable]
    public class ScenarioSlotChoice
    {
        public string slot;
        public string variant;   // empty = remove optional hardware
    }

    [Serializable]
    public class ScenarioStart
    {
        public string utc;                 // overrides the template start time
        public bool overrideOrbit;
        public OrbitSettings orbit = new OrbitSettings();
        /// <summary>"" | "station" (start before a pass over placeStation) | "eclipse" (Sun in the orbit plane, start before the shadow)</summary>
        public string placement = "";
        public string placeStation = "";
        public float placeLeadDeg = 20f;
        public string adcsMode = "";       // "" keeps the default (SUNSAFE if possible)
        public float[] ratesDps = { 0f, 0f, 0f };
        public float socPercent = -1f;
        public float temperatureC = -999f;
        public bool stowed;
        public bool alwaysInContact;
        public float timeWarp = 1f;
        public List<string> commands = new List<string>();   // run at start (instructor level)
    }

    [Serializable]
    public class ScenarioEvent
    {
        public float atSeconds;
        public string command;            // instructor command, e.g. "SIM FAULT eps.solar PANEL_OFF 0"
    }

    [Serializable]
    public class ScenarioStep
    {
        public string text;
        public string hint;
        public ScenarioCheck check = new ScenarioCheck();
    }

    [Serializable]
    public class ScenarioCheck
    {
        /// <summary>Telemetry key, e.g. "eps.battery.soc". Empty when checking a command.</summary>
        public string point;
        public string op = ">";            // < > <= >= == abs< abs>
        public float value;
        public float seconds;              // how long the condition must hold
        /// <summary>Ground command prefix that completes the step, e.g. "ADCS MODE SUNSAFE".</summary>
        public string command;

        public bool IsEmpty { get { return string.IsNullOrEmpty(point) && string.IsNullOrEmpty(command); } }
    }

    public enum ScenarioState { Running, Succeeded, Failed }

    /// <summary>Runs a scenario: timed faults, step checks, success/failure.</summary>
    public sealed class ScenarioRunner
    {
        private readonly SatelliteSim _sim;
        public ScenarioDef Def { get; private set; }
        public int CurrentStep { get; private set; }
        public ScenarioState State { get; private set; }
        public double StepHoldTimer { get; private set; }
        public event Action Changed;

        private readonly List<string> _commands = new List<string>();
        private readonly bool[] _faultDone;

        public ScenarioRunner(SatelliteSim sim, ScenarioDef def)
        {
            _sim = sim;
            Def = def;
            _faultDone = new bool[def.faults != null ? def.faults.Count : 0];
            sim.Obsw.GroundCommandAccepted += cmd => _commands.Add(cmd);
            sim.Ticked += Tick;
            _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "SCENARIO", "Started: " + def.title);
        }

        public ScenarioStep Step { get { return Def.steps != null && CurrentStep < Def.steps.Count ? Def.steps[CurrentStep] : null; } }
        public int StepCount { get { return Def.steps != null ? Def.steps.Count : 0; } }

        private void Tick(double dt)
        {
            if (State != ScenarioState.Running)
                return;

            for (int i = 0; i < _faultDone.Length; i++)
            {
                if (_faultDone[i] || _sim.Env.Met < Def.faults[i].atSeconds)
                    continue;
                _faultDone[i] = true;
                _sim.Obsw.Execute(Def.faults[i].command, CommandSource.Instructor);
            }

            if (Def.failWhen != null && !Def.failWhen.IsEmpty && !string.IsNullOrEmpty(Def.failWhen.point) && Holds(Def.failWhen))
            {
                State = ScenarioState.Failed;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Alarm, "SCENARIO", "FAILED: " + (Def.failText ?? "limit exceeded"));
                Raise();
                return;
            }

            ScenarioStep step = Step;
            if (step == null)
                return;

            bool done;
            if (!string.IsNullOrEmpty(step.check.command))
            {
                done = _commands.Exists(c => c.StartsWith(step.check.command, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrEmpty(step.check.point))
            {
                bool holds = Holds(step.check);
                if (holds)
                    StepHoldTimer += dt;
                else
                    StepHoldTimer = 0;
                done = holds && StepHoldTimer >= step.check.seconds;
            }
            else
            {
                done = true;
            }

            if (!done)
                return;

            _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "SCENARIO", "Step " + (CurrentStep + 1) + " complete: " + step.text);
            CurrentStep++;
            StepHoldTimer = 0;
            _commands.Clear();
            if (CurrentStep >= StepCount)
            {
                State = ScenarioState.Succeeded;
                _sim.Events.Add(_sim.Env.Met, EventSeverity.Info, "SCENARIO", "SUCCESS: " + (Def.successText ?? "scenario complete"));
            }
            Raise();
        }

        private bool Holds(ScenarioCheck c)
        {
            TelemetryPoint p;
            if (!_sim.Telemetry.TryGet(c.point, out p))
                return false;
            double v = p.Value;
            switch (c.op)
            {
                case "<": return v < c.value;
                case "<=": return v <= c.value;
                case ">=": return v >= c.value;
                case "==": return Math.Abs(v - c.value) < 1e-6;
                case "abs<": return Math.Abs(v) < c.value;
                case "abs>": return Math.Abs(v) > c.value;
                default: return v > c.value;
            }
        }

        private void Raise() { if (Changed != null) Changed(); }
    }
}
