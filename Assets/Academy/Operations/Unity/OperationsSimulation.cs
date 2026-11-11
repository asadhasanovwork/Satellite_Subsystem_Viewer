using System;
using System.Collections.Generic;
using Academy.Builder;
using Academy.Prototype;
using UnityEngine;

namespace Academy.Operations
{
    /// <summary>
    /// Drives the operations simulation in Unity: builds the satellite (3D + models) from a Builder design,
    /// advances time with warp, and keeps the 3D view (attitude, Sun, Earth, wheels, deployment) in sync.
    /// </summary>
    public sealed class OperationsSimulation : MonoBehaviour
    {
        [Header("Scene references (set by OperationsSceneFactory)")]
        public BuilderSatelliteView view;
        public PrototypeOrbitCamera orbitCamera;
        public Light sunLight;
        public EarthVisual earth;
        public Camera mainCamera;

        public List<BuilderTemplate> Templates { get; private set; }
        public ModelsFile Models { get; private set; }
        public List<ScenarioDef> Scenarios { get; private set; }

        public SatelliteSim Sim { get; private set; }
        public ScenarioRunner Runner { get; private set; }
        public ScenarioDef Scenario { get; private set; }
        public BuilderTemplate Template { get; private set; }
        public SatelliteDesign Design { get; private set; }
        public DesignSave LastDesign { get; private set; }

        public float TimeWarp = 1f;
        public bool Paused;

        /// <summary>Raised when a new simulation has been built (the UI rebuilds its panels).</summary>
        public event Action SimulationStarted;

        // Ground track (lat, lon) history for the map
        public readonly List<Vector2> TrackHistory = new List<Vector2>();
        public readonly List<Vector2> TrackPrediction = new List<Vector2>();
        private double _lastTrackSample = double.NegativeInfinity;
        private double _predictionMadeAt = double.NegativeInfinity;

        private float _earthDistance = 400f;

        // Planning forecast (passes and eclipses ahead)
        public readonly List<ForecastWindow> Passes = new List<ForecastWindow>();
        public readonly List<ForecastWindow> Eclipses = new List<ForecastWindow>();
        public double ForecastHorizon { get; private set; }
        private double _forecastAt = double.NegativeInfinity;
        private double _forecastA, _forecastEpoch;

        /// <summary>A lesson was just loaded: time stays paused until the learner closes the briefing.</summary>
        public bool AwaitingBriefing;

        // Debrief statistics
        public double StatMinSoc { get; private set; }
        public double StatMaxRate { get; private set; }
        public int StatAlarms { get; private set; }
        public int StatCommands { get; private set; }

        private void Awake()
        {
            // Event and command texts go to OpenC3 and scripts: always "0.001", never "0,001"
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Templates = OperationsData.LoadTemplates();
            Models = OperationsData.LoadModels();
            Scenarios = OperationsData.LoadScenarios();
            RotorSpin.GlobalEnabled = true;
        }

        private void Start()
        {
            if (OperationsSession.PendingDesign != null)
            {
                DesignSave design = OperationsSession.PendingDesign;
                BuilderTemplate t = Templates.Find(x => x.id == design.templateId);
                if (t != null)
                    StartFreeFlight(t, design);
            }
            else if (!string.IsNullOrEmpty(OperationsSession.PendingScenario))
            {
                ScenarioDef s = Scenarios.Find(x => x.id == OperationsSession.PendingScenario);
                if (s != null)
                    StartScenario(s);
            }
        }

        public TemplateModels ModelsFor(string templateId)
        {
            if (Models != null && Models.templates != null)
                foreach (TemplateModels m in Models.templates)
                    if (m.template == templateId) return m;
            return new TemplateModels { template = templateId };
        }

        // ------------------------------------------------------------------ starting

        public void StartFreeFlight(BuilderTemplate template, DesignSave save)
        {
            var design = new SatelliteDesign(template);
            if (save != null)
                design.LoadSave(save);
            FillMissing(design);
            Launch(template, design, null);
        }

        public void StartScenario(ScenarioDef scenario)
        {
            BuilderTemplate template = Templates.Find(x => x.id == scenario.template);
            if (template == null)
            {
                Debug.LogError("[Operations] Scenario " + scenario.id + " needs template " + scenario.template);
                return;
            }
            var design = new SatelliteDesign(template);
            // Scenario hardware choices first (they may trigger conditional slots), then the recommended rest
            if (scenario.design != null)
                foreach (ScenarioSlotChoice c in scenario.design)
                    design.SelectVariant(c.slot, c.variant);
            FillMissing(design);
            Launch(template, design, scenario);
        }

        public void Restart()
        {
            if (Template == null) return;
            if (Scenario != null) StartScenario(Scenario);
            else StartFreeFlight(Template, LastDesign);
        }

        /// <summary>Simulation inputs for every built slot of a design (also used by the Orbit Simulation).</summary>
        public static List<SlotInstance> BuildSlotInstances(BuilderTemplate template, SatelliteDesign design, out double totalMass)
        {
            var slots = new List<SlotInstance>();
            totalMass = 0;
            foreach (BuilderSubsystem subsystem in template.subsystems)
            {
                foreach (BuilderSlot slot in subsystem.slots)
                {
                    if (!design.IsBuilt(slot)) continue;
                    BuilderVariant v = design.GetSelectedVariant(slot.id);
                    var si = new SlotInstance
                    {
                        SlotId = slot.id,
                        SlotName = slot.name,
                        Subsystem = subsystem.name,
                        VariantId = v.id,
                        VariantName = v.name,
                        MassKg = v.massKg,
                        PowerW = v.powerW,
                        PowerGenW = v.powerGenW
                    };
                    foreach (BuilderParameter p in v.parameters)
                    {
                        float value = design.GetParameter(slot.id, p);
                        float delta = value - p.defaultValue;
                        si.MassKg += delta * p.massPerUnit;
                        si.PowerW += delta * p.powerPerUnit;
                        si.PowerGenW += delta * p.powerGenPerUnit;
                        si.Sliders.Add(new SliderValue { Id = p.id, Value = value, Default = p.defaultValue });
                    }
                    si.MassKg = Math.Max(0, si.MassKg);
                    si.PowerW = Math.Max(0, si.PowerW);
                    si.PowerGenW = Math.Max(0, si.PowerGenW);
                    foreach (PartSpec spec in design.BuildParts(slot))
                    {
                        si.Parts.Add(new PartGeom
                        {
                            Type = spec.type,
                            Style = spec.style,
                            Position = new Vec3d(spec.position.x, spec.position.y, spec.position.z),
                            Forward = new Vec3d(spec.forward.x, spec.forward.y, spec.forward.z),
                            Up = new Vec3d(spec.up.x, spec.up.y, spec.up.z)
                        });
                    }
                    totalMass += si.MassKg;
                    slots.Add(si);
                }
            }
            return slots;
        }

        /// <summary>Recommended hardware for every slot the learner left empty.</summary>
        public static void FillMissingPublic(SatelliteDesign design) { FillMissing(design); }

        /// <summary>Copy of the template data with the Builder's circular orbit in place of the default one.</summary>
        public static TemplateModels WithDesignOrbit(TemplateModels models, DesignOrbit orbit)
        {
            TemplateModels copy = JsonUtility.FromJson<TemplateModels>(JsonUtility.ToJson(models));
            OrbitSettings o = copy.orbit ?? new OrbitSettings();
            o.periapsisAltKm = orbit.altitudeKm;
            o.apoapsisAltKm = orbit.altitudeKm;
            o.inclinationDeg = orbit.inclinationDeg;
            o.raanDeg = orbit.raanDeg;
            o.geoLongitudeDeg = orbit.geoLongitudeDeg >= -180f && orbit.geoLongitudeDeg <= 180f ? orbit.geoLongitudeDeg : -999;
            copy.orbit = o;
            return copy;
        }

        private static void FillMissing(SatelliteDesign design)
        {
            for (int pass = 0; pass < 3; pass++)
                foreach (BuilderSubsystem s in design.Template.subsystems)
                    design.FillRecommended(s);
        }

        private void Launch(BuilderTemplate template, SatelliteDesign design, ScenarioDef scenario)
        {
            Template = template;
            Design = design;
            Scenario = scenario;
            LastDesign = design.ToSave();

            // ---- 3D model, deployed (the solar models need the deployed geometry)
            view.ClearAll();
            view.SetExplodeDistance(template.explodeDistance);
            foreach (BuilderSubsystem subsystem in template.subsystems)
                foreach (BuilderSlot slot in subsystem.slots)
                    if (design.IsBuilt(slot))
                        view.RebuildSlot(slot.id, design.BuildParts(slot), false);
            view.SetDeployImmediate(true);

            // ---- simulation model
            double totalMass;
            List<SlotInstance> slots = BuildSlotInstances(template, design, out totalMass);

            TemplateModels models = ModelsFor(template.id);
            if (scenario == null && design.Orbit != null)
                models = WithDesignOrbit(models, design.Orbit); // free flight: the orbit chosen in the Builder
            Sim = MissionSetup.Create(models, slots, totalMass, template.orbitClass, scenario, RefreshSolarGeometry);
            Sim.SatelliteName = string.IsNullOrEmpty(design.DesignName) ? template.designName : design.DesignName;

            foreach (DeployComponent d in Sim.Deployers)
            {
                DeployComponent dep = d;
                dep.DeployedChanged += () => OnDeployChanged(dep);
            }
            bool stowed = Sim.Deployers.Exists(d => !d.Deployed);
            view.SetDeployImmediate(!stowed);
            RefreshSolarGeometry(Sim); // stowed panels see the Sun differently

            Runner = scenario != null ? new ScenarioRunner(Sim, scenario) : null;

            StatMinSoc = 100; StatMaxRate = 0; StatAlarms = 0; StatCommands = 0;
            Sim.Ticked += dt =>
            {
                double soc = Sim.Telemetry.Get("eps.battery.soc", double.NaN);
                if (!double.IsNaN(soc)) StatMinSoc = Math.Min(StatMinSoc, soc);
                StatMaxRate = Math.Max(StatMaxRate, Sim.Env.RateDegPerSec);
            };
            Sim.Events.Added += e => { if (e.Severity == EventSeverity.Alarm) StatAlarms++; };
            Sim.Obsw.GroundCommandAccepted += c => StatCommands++;
            _forecastAt = double.NegativeInfinity;
            TimeWarp = scenario != null && scenario.start != null ? Mathf.Max(1f, scenario.start.timeWarp) : 1f;
            AwaitingBriefing = scenario != null;
            Paused = AwaitingBriefing;

            // ---- camera, Earth, track
            float camDistance = Mathf.Max(0.3f, template.cameraDistance);
            if (orbitCamera != null) orbitCamera.Frame(camDistance);
            _earthDistance = camDistance * 400f;
            if (mainCamera != null)
            {
                mainCamera.nearClipPlane = Mathf.Max(0.005f, camDistance * 0.01f);
                mainCamera.farClipPlane = _earthDistance * 1.3f + camDistance * 10f;
            }
            if (earth != null)
                earth.SetStations(models.groundStations);
            TrackHistory.Clear();
            TrackPrediction.Clear();
            _lastTrackSample = double.NegativeInfinity;
            _predictionMadeAt = double.NegativeInfinity;

            OperationsSession.Clear();
            SyncVisuals();
            if (SimulationStarted != null) SimulationStarted();
        }

        private void OnDeployChanged(DeployComponent d)
        {
            if (Sim == null) return;
            bool all = !Sim.Deployers.Exists(x => !x.Deployed);
            view.AnimateDeploy(all);
            foreach (SolarArrayComponent a in Sim.Power.Arrays)
                foreach (SolarPanel p in a.Panels)
                    if (p.Tracking) p.Deployed = all;
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            if (Sim == null)
                return;
            if (!Paused)
            {
                double dt = Math.Min(Time.unscaledDeltaTime, 0.1f) * TimeWarp;
                Sim.Step(dt);
            }
            SyncVisuals();
        }

        private void SyncVisuals()
        {
            if (Sim == null) return;
            SpaceEnvironment env = Sim.Env;

            // Satellite attitude: body (right-handed) -> inertial, shown in Unity world (inertial with Y/Z swapped)
            Vector3 forward = EarthVisual.ToUnity(env.Attitude.Rotate(Vec3d.UnitY));
            Vector3 up = EarthVisual.ToUnity(env.Attitude.Rotate(Vec3d.UnitZ));
            view.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(forward, up));

            if (sunLight != null)
                sunLight.transform.rotation = Quaternion.LookRotation(EarthVisual.ToUnity(-env.SunEci));

            if (earth != null)
                earth.UpdatePose(env.Position, env.Time, _earthDistance);

            // Reaction wheels spin with their real speed (slowed down so it can be seen)
            ReactionWheelSet wheels = Sim.Adcs.Wheels;
            if (wheels != null)
            {
                int i = 0;
                foreach (GeneratedPart part in view.GetSlotParts(wheels.Id))
                {
                    if (part == null || part.Spec == null || part.Spec.type != "reaction_wheel" || part.Root == null) continue;
                    if (i >= wheels.Wheels.Count) break;
                    float rpm = (float)(wheels.Rpm(wheels.Wheels[i]) / 30.0);
                    foreach (RotorSpin spin in part.Root.GetComponentsInChildren<RotorSpin>())
                        spin.rpm = rpm;
                    i++;
                }
            }

            // Fixed panels follow the 3D model while it deploys
            if (view.IsDeploying)
                RefreshSolarGeometry(Sim);

            UpdateTrack();
            UpdateForecast();
        }

        private void UpdateForecast()
        {
            double period = Sim.Env.Orbit.PeriodSeconds;
            ForecastHorizon = period < 4 * 3600 ? Math.Max(3 * period, 4 * 3600) : 24 * 3600;
            bool orbitChanged = Sim.Env.Orbit.A != _forecastA || Sim.Env.Orbit.Epoch != _forecastEpoch;
            if (!orbitChanged && Sim.Env.Time - _forecastAt < ForecastHorizon / 40)
                return;
            _forecastAt = Sim.Env.Time;
            _forecastA = Sim.Env.Orbit.A;
            _forecastEpoch = Sim.Env.Orbit.Epoch;
            Forecast.Compute(Sim, ForecastHorizon * 1.05, Passes, Eclipses);
        }

        /// <summary>The lesson after <paramref name="current"/> in the scenario list (null if it was the last).</summary>
        public ScenarioDef NextScenario(ScenarioDef current)
        {
            int i = current != null ? Scenarios.FindIndex(x => x.id == current.id) : -1;
            return i >= 0 && i + 1 < Scenarios.Count ? Scenarios[i + 1] : null;
        }

        public void BeginAfterBriefing()
        {
            AwaitingBriefing = false;
            Paused = false;
        }

        /// <summary>Copy the real panel normals from the generated 3D model into the solar models.</summary>
        private void RefreshSolarGeometry(SatelliteSim sim)
        {
            foreach (SolarArrayComponent array in sim.Power.Arrays)
            {
                int index = 0;
                var fixedPanels = array.Panels.FindAll(p => !p.Tracking);
                foreach (GeneratedPart part in view.GetSlotParts(array.Id))
                {
                    if (part == null || part.Spec == null || part.Spec.type != "solar_panel" || part.Root == null) continue;
                    if (index >= fixedPanels.Count) break;
                    Transform t = part.Root.transform.Find("Hinge") ?? part.Root.transform;
                    Vector3 local = view.transform.InverseTransformDirection(t.forward);
                    fixedPanels[index].Normal = new Vec3d(local.x, local.y, local.z).Swap().Normalized;
                    index++;
                }
            }
        }

        private void UpdateTrack()
        {
            SpaceEnvironment env = Sim.Env;
            double period = env.Orbit.PeriodSeconds;
            double sampleEvery = Math.Max(10, period / 300);
            if (env.Time - _lastTrackSample >= sampleEvery)
            {
                _lastTrackSample = env.Time;
                TrackHistory.Add(new Vector2((float)env.LongitudeDeg, (float)env.LatitudeDeg));
                if (TrackHistory.Count > 600) TrackHistory.RemoveAt(0);
            }
            if (env.Time - _predictionMadeAt > period / 20 || TrackPrediction.Count == 0)
            {
                _predictionMadeAt = env.Time;
                TrackPrediction.Clear();
                double horizon = Math.Min(period, 86400);
                for (int i = 0; i <= 200; i++)
                {
                    double t = env.Time + horizon * i / 200.0;
                    double lat, lon;
                    OpsMath.LatLon(env.PositionAt(t), t, out lat, out lon);
                    TrackPrediction.Add(new Vector2((float)lon, (float)lat));
                }
            }
        }

        // ------------------------------------------------------------------ commands from the UI

        public string SendGround(string command) { return Sim != null ? Sim.Obsw.Execute(command, CommandSource.Ground) : "No satellite"; }
        public string SendInstructor(string command) { return Sim != null ? Sim.Obsw.Execute(command, CommandSource.Instructor) : "No satellite"; }

        public DateTime UtcNow { get { return Sim != null ? OpsMath.ToUtc(Sim.Env.Time) : DateTime.UtcNow; } }
    }
}
