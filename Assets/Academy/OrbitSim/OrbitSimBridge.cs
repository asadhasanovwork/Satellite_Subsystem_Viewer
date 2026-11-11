using System;
using System.Collections.Generic;
using Academy.Builder;
using Academy.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Academy.OrbitSim
{
    /// <summary>
    /// Opens the orbital-control simulator (Assets/ThirdParty/OrbitalControl) with the satellite built in
    /// the Builder: its orbit, mass, propellant, main engine and procedural 3D model.
    /// </summary>
    public static class OrbitSimBridge
    {
        public const string SceneName = "OrbitSimulation";
        public const string ScenePath = "Assets/ThirdParty/OrbitalControl/Scenes/OrbitSimulation.unity";

        internal static BuilderTemplate PendingTemplate;
        internal static SatelliteDesign PendingDesign;
        private static float _savedFixedDeltaTime = -1f;
        /// <summary>UI Toolkit panel of the main menu, reused when going on to Mission Operations.</summary>
        internal static UnityEngine.UIElements.PanelSettings MenuPanel;

        public static bool IsOpen { get { return SceneManager.GetActiveScene().name == SceneName; } }

        /// <summary>Fly the last design built in the Builder (or a recommended default satellite).</summary>
        public static bool Open()
        {
            return Open(LastDesignStore.Load());
        }

        public static bool Open(UnityEngine.UIElements.PanelSettings menuPanel)
        {
            if (menuPanel != null) MenuPanel = menuPanel;
            return Open(LastDesignStore.Load());
        }

        public static bool Open(DesignSave save)
        {
            List<BuilderTemplate> templates = OperationsData.LoadTemplates();
            if (templates == null || templates.Count == 0)
            {
                Debug.LogError("[OrbitSim] No Builder templates found.");
                return false;
            }
            BuilderTemplate template = save != null ? templates.Find(t => t.id == save.templateId) : null;
            if (template == null) template = templates[0];

            var design = new SatelliteDesign(template);
            if (save != null && save.templateId == template.id) design.LoadSave(save);
            OperationsSimulation.FillMissingPublic(design);

            PendingTemplate = template;
            PendingDesign = design;
            if (_savedFixedDeltaTime < 0f) _savedFixedDeltaTime = Time.fixedDeltaTime;

            BodyService.SkipSceneSatellites = true; // the demo satellites make way for ours
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (!SatelliteBuilderSceneFactory.LoadExisting(SceneName, ScenePath))
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                BodyService.SkipSceneSatellites = false;
                Debug.LogError("[OrbitSim] Scene " + SceneName + " is not in Build Settings.");
                return false;
            }
            return true;
        }

        /// <summary>Back to the Academy main menu; undoes the simulator's time warp.</summary>
        public static void Close()
        {
            RestoreTime();
            SatelliteBuilderSceneFactory.OpenMainMenu(null);
        }

        /// <summary>
        /// Straight on to Mission Operations Training with this satellite: the same start-up screen as the main menu
        /// (OpenC3 when available, the Unity console otherwise). The manoeuvre plan was saved just before.
        /// </summary>
        public static void GoToMissionOperations(DesignSave save)
        {
            RestoreTime();
            if (save != null)
            {
                LastDesignStore.Remember(save);
                OperationsSession.FlyDesign(save);
            }
            UnityEngine.UIElements.PanelSettings panel = MenuPanel;
            if (panel == null)
            {
                var found = Resources.FindObjectsOfTypeAll<UnityEngine.UIElements.PanelSettings>();
                if (found.Length > 0) panel = found[0];
            }
            Academy.OpsLink.OpsLinkFlow.Begin(panel);
        }

        private static void RestoreTime()
        {
            Time.timeScale = 1f;
            if (_savedFixedDeltaTime > 0f) Time.fixedDeltaTime = _savedFixedDeltaTime;
            BodyService.SkipSceneSatellites = false;
            PendingDesign = null;
            PendingTemplate = null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            BodyService.SkipSceneSatellites = false;
            var go = new GameObject("Academy Orbit Bridge");
            SceneManager.MoveGameObjectToScene(go, scene);
            var runtime = go.AddComponent<OrbitSimRuntime>();
            runtime.Template = PendingTemplate;
            runtime.Design = PendingDesign;
        }
    }

    /// <summary>Spawns the learner's satellite into the running orbit simulator and adds the Academy overlay.</summary>
    public sealed class OrbitSimRuntime : MonoBehaviour
    {
        public BuilderTemplate Template;
        public SatelliteDesign Design;

        public NBody Body { get; private set; }
        public SatelliteSim Sim { get; private set; }
        public ThrusterSet MainEngine { get; private set; }
        /// <summary>Simulator time (s) when the satellite was placed at the start of its orbit.</summary>
        public double SpawnTime { get; private set; }

        private Text _status;
        private DesignOrbit _orbit;

        private static readonly Color BtnC = new Color(0.263f, 0.627f, 0.278f, 0.92f);

        /// <summary>Model size in the simulator's (already exaggerated) icon units: the largest side of the model.</summary>
        public const float ModelSize = 3f;
        /// <summary>Close-up camera distance (world units) so the Builder model is visible on arrival.</summary>
        public const float CloseUpDistance = 0.6f;

        private System.Collections.IEnumerator Start()
        {
            BuildOverlay();
            if (Design == null || Template == null) yield break;
            try { Spawn(); }
            catch (Exception e) { Debug.LogError("[OrbitSim] Could not place the satellite: " + e); yield break; }

            // The simulator frames the whole orbit by default; start close to the model instead.
            yield return null;
            yield return null;
            SimContext ctx = SimulationBootstrap.Current;
            if (ctx != null && ctx.CameraMovement != null) ctx.CameraMovement.distance = CloseUpDistance;
        }

        private void Spawn()
        {
            SimContext ctx = SimulationBootstrap.Current;
            var spawner = FindFirstObjectByType<SatelliteSpawner>();
            if (ctx == null || ctx.ObjectPlacementManager == null || spawner == null)
                throw new InvalidOperationException("orbit simulator is not initialised");

            // ---- the same simulation model Mission Operations flies (masses, tanks, thrusters)
            TemplateModels models = null;
            ModelsFile file = OperationsData.LoadModels();
            if (file != null && file.templates != null) models = file.templates.Find(m => m.template == Template.id);
            if (models == null) models = new TemplateModels { template = Template.id };
            DesignOrbit orbit = Design.Orbit ?? DesignOrbit.DefaultFor(Template.orbitClass);
            models = OperationsSimulation.WithDesignOrbit(models, orbit);

            double totalMass;
            List<SlotInstance> slots = OperationsSimulation.BuildSlotInstances(Template, Design, out totalMass);
            Sim = MissionSetup.Create(models, slots, totalMass, Template.orbitClass, null);

            double fuel = 0;
            foreach (TankComponent t in Sim.Tanks) fuel += t.PropellantKg;
            double dry = Math.Max(0.1, Sim.DryMassKg);
            MainEngine = null;
            foreach (ThrusterSet t in Sim.ThrusterSets)
                if (t.Tank != null && (MainEngine == null || SetThrust(t) > SetThrust(MainEngine))) MainEngine = t;

            // ---- circular orbit from the Builder (GEO: start over the chosen slot longitude)
            ObjectPlacementManager placement = ctx.ObjectPlacementManager;
            double aMeters = (DesignOrbit.EarthRadiusKm + orbit.altitudeKm) * 1000.0;
            double nu = DesignOrbit.IsGeo(Template.orbitClass) && orbit.geoLongitudeDeg >= -180f ? orbit.geoLongitudeDeg : 0.0;
            var (rEci, vEci) = KeplerUtils.FromElements(aMeters, 0.0, orbit.inclinationDeg, orbit.raanDeg, 0.0, nu, placement.Mu);
            Vector3 pos = FrameUtils.EciToUnity(rEci, placement.MetersPerUnit);
            Vector3 vel = FrameUtils.VelEciToUnity(vEci, placement.MetersPerUnit);

            string name = string.IsNullOrEmpty(Design.DesignName) ? Template.displayName : Design.DesignName;
            Body = spawner.SpawnSatellite(name, pos, (float)dry, vel, true, false, fuel);
            SpawnTime = ctx.BodyRuntimeCoordinator != null ? ctx.BodyRuntimeCoordinator.SimulationTimeSeconds : 0.0;
            _orbit = orbit;

            // ---- propulsion: the real main engine, finite propellant (no engine = no thrust)
            if (MainEngine != null)
                Body.TryConfigurePropulsion((float)SetThrust(MainEngine), (float)Math.Max(1, MainEngine.IspS));
            else
                Body.TryConfigurePropulsion(0f, 300f);
            Body.TrySetUnlimitedPropellant(false);

            float area = AttachModel(Body.gameObject);
            if (area > 0f) Body.TrySetDragAreaSquareMeters(area);

            Debug.Log("[OrbitSim] " + name + ": " + orbit.altitudeKm.ToString("0") + " km, i=" + orbit.inclinationDeg.ToString("0.##") +
                      "°, dry " + dry.ToString("0.##") + " kg, propellant " + fuel.ToString("0.##") + " kg, engine " +
                      (MainEngine != null ? SetThrust(MainEngine).ToString("0.###") + " N / Isp " + MainEngine.IspS.ToString("0") + " s" : "none"));
        }

        private static double SetThrust(ThrusterSet t) { return t.ThrustN * Math.Max(1, t.Thrusters.Count); }

        /// <summary>Replaces the simulator's placeholder with the Builder's procedural model. Returns the mean projected area (m²).</summary>
        private float AttachModel(GameObject satellite)
        {
            var placeholders = new List<Renderer>(satellite.GetComponentsInChildren<Renderer>(true));

            var modelGo = new GameObject("Builder Model");
            modelGo.transform.SetParent(satellite.transform, false);
            var view = modelGo.AddComponent<BuilderSatelliteView>();
            view.SetExplodeDistance(Template.explodeDistance);
            foreach (BuilderSubsystem subsystem in Template.subsystems)
                foreach (BuilderSlot slot in subsystem.slots)
                    if (Design.IsBuilt(slot))
                        view.RebuildSlot(slot.id, Design.BuildParts(slot), false);
            view.SetDeployImmediate(true);

            // Real size, before scaling to the simulator's exaggerated icon size
            Bounds b = LocalBounds(modelGo.transform);
            float extent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float area = (b.size.x * b.size.y + b.size.y * b.size.z + b.size.x * b.size.z) / 3f;
            if (extent > 1e-4f)
            {
                modelGo.transform.localScale = Vector3.one * (ModelSize / extent);
                modelGo.transform.localPosition = -b.center * (ModelSize / extent);
            }

            foreach (Renderer r in placeholders) if (r != null) r.enabled = false;
            return area;
        }

        private static Bounds LocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 toLocal = root.worldToLocalMatrix;
            foreach (Renderer r in renderers)
            {
                Bounds wb = r.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = toLocal.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        // ------------------------------------------------------------------ overlay

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("Academy Orbit Overlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            MakeButton(canvasGo.transform, "← Academy", new Vector2(-250f, -10f), new Vector2(150f, 34f), OrbitSimBridge.Close);
            MakeButton(canvasGo.transform, "Send plan to Mission Ops", new Vector2(-40f, -10f), new Vector2(240f, 34f), SendPlan);
            MakeButton(canvasGo.transform, "Fly it in OpenC3 →", new Vector2(190f, -10f), new Vector2(200f, 34f), FlyInMissionOps);

            var statusGo = new GameObject("Plan Status");
            statusGo.transform.SetParent(canvasGo.transform, false);
            var srt = statusGo.AddComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -50f);
            srt.sizeDelta = new Vector2(900f, 48f);
            _status = statusGo.AddComponent<Text>();
            _status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _status.fontSize = 15;
            _status.alignment = TextAnchor.UpperCenter;
            _status.color = new Color(0.85f, 0.95f, 0.85f);
            _status.raycastTarget = false;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject(label);
            btnGo.transform.SetParent(parent, false);
            var rt = btnGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = btnGo.AddComponent<Image>();
            img.color = BtnC;
            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }

        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message;
            Debug.Log("[OrbitSim] " + message);
        }

        /// <summary>
        /// Saves the finalized manoeuvre nodes of the learner's satellite as a plan for Mission Operations: in OpenC3
        /// it becomes the Script Runner procedure <c>maneuver_plan.rb</c> of the satellite's target.
        /// </summary>
        public void SendPlan()
        {
            try
            {
                ManeuverPlan plan = BuildPlan(out string problem);
                if (plan == null) { SetStatus(problem); return; }
                ManeuverPlanStore.Save(plan);
                SetStatus(plan.burns.Count + " burn" + (plan.burns.Count > 1 ? "s" : "") + " (" + plan.TotalDvMps.ToString("0.###") +
                          " m/s) saved for " + plan.designName + ". Open Mission Operations Training: in OpenC3 run Script Runner → " +
                          ManeuverPlanProcedure.FileName + ".");
            }
            catch (Exception e)
            {
                SetStatus("Could not save the plan: " + e.Message);
            }
        }

        /// <summary>Saves the planned burns (if any) and opens Mission Operations Training / OpenC3 with this satellite.</summary>
        public void FlyInMissionOps()
        {
            if (Design == null) { OrbitSimBridge.Close(); return; }
            try
            {
                ManeuverPlan plan = BuildPlan(out string problem);
                if (plan != null) ManeuverPlanStore.Save(plan);
            }
            catch (Exception e) { Debug.LogWarning("[OrbitSim] Plan not saved: " + e.Message); }
            DesignSave save = Design.ToSave();
            OrbitSimBridge.GoToMissionOperations(save);
        }

        public ManeuverPlan BuildPlan(out string problem)
        {
            problem = null;
            SimContext ctx = SimulationBootstrap.Current;
            if (Body == null || ctx == null || ctx.ManeuverNodeManager == null) { problem = "The satellite is not in orbit yet."; return null; }
            if (MainEngine == null) { problem = "This satellite has no propulsion: add an engine and propellant tank in the Builder to fly manoeuvres."; return null; }

            // A node still being edited (Setup pressed, Place not yet): place it for the learner
            ManeuverNodeManager nodes = ctx.ManeuverNodeManager;
            if (nodes.EditingNode != null && !nodes.EditingNode.isFinalized && nodes.EditingNode.targetBody == Body)
                nodes.FinalizeManeuver();

            double now = ctx.BodyRuntimeCoordinator != null ? ctx.BodyRuntimeCoordinator.SimulationTimeSeconds : 0.0;
            var plan = new ManeuverPlan
            {
                designName = Body.name,
                templateId = Template.id,
                savedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                altitudeKm = _orbit != null ? _orbit.altitudeKm : 0,
                inclinationDeg = _orbit != null ? _orbit.inclinationDeg : 0,
                periodS = _orbit != null ? _orbit.PeriodMinutes * 60.0 : 0
            };
            foreach (ManeuverNode node in ctx.ManeuverNodeManager.GetManualPlan())
            {
                if (node == null || node.targetBody != Body || !node.isFinalized || node.burnTime < now) continue;
                double dv = node.predictedPropulsiveDeltaVMetersPerSecond;
                if (!(dv > 0)) dv = node.deltaV.magnitude * SimulationUnits.MetersPerUnit;
                plan.burns.Add(new PlannedBurn
                {
                    tSec = node.burnTime - SpawnTime,
                    direction = DirectionFor(node.burnType),
                    dvMps = Math.Round(dv, 4),
                    durationS = node.duration,
                    fuelKg = node.predictedFuelUsedKg
                });
            }
            if (plan.burns.Count == 0)
            {
                problem = "No manoeuvre planned yet. In Burn Controls press Setup, choose the burn direction, drag the node to the burn point, press Place, then Send plan.";
                return null;
            }
            plan.burns.Sort((a, b) => a.tSec.CompareTo(b.tSec));
            return plan;
        }

        /// <summary>
        /// The simulator's Unity frame swaps the ECI y and z axes (a mirror), so its "Normal" (r × v in Unity space)
        /// points along the physical anti-normal (-h). Directions are converted to the physical ECI frame used on board.
        /// </summary>
        public static string DirectionFor(BurnType type)
        {
            switch (type)
            {
                case BurnType.Retrograde: return "RETROGRADE";
                case BurnType.RadialIn: return "RADIAL_IN";
                case BurnType.RadialOut: return "RADIAL_OUT";
                case BurnType.Normal: return "ANTINORMAL";
                case BurnType.AntiNormal: return "NORMAL";
                default: return "PROGRADE";
            }
        }
    }
}
