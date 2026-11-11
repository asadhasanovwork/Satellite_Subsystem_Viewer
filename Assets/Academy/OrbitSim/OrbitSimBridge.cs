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

        public static bool IsOpen { get { return SceneManager.GetActiveScene().name == SceneName; } }

        /// <summary>Fly the last design built in the Builder (or a recommended default satellite).</summary>
        public static bool Open()
        {
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
            Time.timeScale = 1f;
            if (_savedFixedDeltaTime > 0f) Time.fixedDeltaTime = _savedFixedDeltaTime;
            BodyService.SkipSceneSatellites = false;
            PendingDesign = null;
            PendingTemplate = null;
            SatelliteBuilderSceneFactory.OpenMainMenu(null);
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

            var btnGo = new GameObject("Back To Academy");
            btnGo.transform.SetParent(canvasGo.transform, false);
            var rt = btnGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            rt.sizeDelta = new Vector2(150f, 34f);
            var img = btnGo.AddComponent<Image>();
            img.color = BtnC;
            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(OrbitSimBridge.Close);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = "← Academy";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 17;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }
        }
    }
}
