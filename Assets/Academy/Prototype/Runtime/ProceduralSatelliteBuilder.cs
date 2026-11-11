using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Academy.Prototype
{
    /// <summary>
    /// Reads a satellite assembly JSON from StreamingAssets and generates every part procedurally.
    /// Works in Edit mode (preview) and Play mode (deployment animation, spinning wheels, on-screen controls).
    /// Generated objects are never saved into the scene; they are rebuilt from the JSON each time.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ProceduralSatelliteBuilder : MonoBehaviour
    {
        private const string GeneratedRootName = "GeneratedSatellite";

        [Header("Source")]
        [Tooltip("Path relative to Assets/StreamingAssets.")]
        public string jsonFile = "Academy/cubesat_3u_prototype.json";
        [Tooltip("Generate the satellite in the Scene view without pressing Play.")]
        public bool buildInEditMode = true;

        [Header("Pose")]
        [Range(0f, 1f)] public float deployFraction = 1f;
        [Range(0f, 1f)] public float explode;
        [Tooltip("How far (metres) parts move outward at explode = 1.")]
        public float explodeDistance = 0.12f;

        [Header("Gallery (on-screen buttons)")]
        public string[] galleryFiles =
        {
            "Academy/cubesat_3u_prototype.json",
            "Academy/meo_navsat_prototype.json",
            "Academy/geo_comsat_prototype.json"
        };
        public string[] galleryLabels =
        {
            "LEO  \u00b7  3U CubeSat",
            "MEO  \u00b7  Navigation satellite",
            "GEO  \u00b7  Communications satellite"
        };

        [Header("Play Mode Demo")]
        public bool animateDeployOnPlay = true;
        public float deployDelay = 1f;
        public float deployDuration = 5f;
        public bool spinWheels = true;
        public bool showOnScreenControls = true;

        /// <summary>Screen rect of the on-screen panel (GUI coordinates), so cameras can ignore clicks on it.</summary>
        public static Rect GuiRect { get; private set; }

        public SatelliteAssemblyData Data { get; private set; }
        public IList<GeneratedPart> Parts { get { return _parts; } }

        private readonly List<GeneratedPart> _parts = new List<GeneratedPart>();
        private readonly List<Vector3> _basePositions = new List<Vector3>();
        private readonly List<Vector3> _explodeDirections = new List<Vector3>();
        private Transform _generatedRoot;
        private bool _animatingDeploy;
        private float _deployTimer;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (Application.isPlaying || buildInEditMode)
                Rebuild();
        }

        private void OnDisable()
        {
            Clear();
            GuiRect = Rect.zero; // otherwise other scenes keep ignoring clicks in this area
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            GuiRect = Rect.zero;
            RotorSpin.GlobalEnabled = true;
        }

        private void Start()
        {
            if (Application.isPlaying && animateDeployOnPlay)
                ReplayDeployment();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (_animatingDeploy)
            {
                _deployTimer += Time.deltaTime;
                float t = Mathf.Clamp01((_deployTimer - deployDelay) / Mathf.Max(0.01f, deployDuration));
                deployFraction = Mathf.SmoothStep(0f, 1f, t);
                if (t >= 1f)
                    _animatingDeploy = false;
            }

            RotorSpin.GlobalEnabled = spinWheels;
            ApplyPose();
        }

        private void OnValidate()
        {
            // Only moves existing transforms; safe to run from OnValidate.
            ApplyPose();
        }

        // ------------------------------------------------------------------ public API

        [ContextMenu("Rebuild From JSON")]
        public void Rebuild()
        {
            Clear();

            Data = LoadAssembly();
            if (Data == null || Data.parts == null)
                return;

            _generatedRoot = new GameObject(GeneratedRootName).transform;
            _generatedRoot.SetParent(transform, false);

            foreach (PartSpec spec in Data.parts)
            {
                if (spec == null)
                    continue;

                GeneratedPart part = PartGenerators.Create(spec, _generatedRoot);
                Transform t = part.Root.transform;
                t.localPosition = spec.position;
                t.localRotation = SafeLookRotation(spec.forward, spec.up);

                _parts.Add(part);
                _basePositions.Add(spec.position);
                _explodeDirections.Add(ExplodeDirection(spec));
            }

            if (!Application.isPlaying)
                MarkDontSave(_generatedRoot);

            ApplyPose();
            Debug.Log("[ProceduralSatelliteBuilder] Generated '" + Data.name + "' with " + _parts.Count + " parts.");
        }

        public void ReplayDeployment()
        {
            deployFraction = 0f;
            _deployTimer = 0f;
            _animatingDeploy = true;
            ApplyPose();
        }

        public void ApplyPose()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                GeneratedPart part = _parts[i];
                if (part == null || part.Root == null)
                    continue;

                part.Root.transform.localPosition = _basePositions[i] + _explodeDirections[i] * (explode * EffectiveExplodeDistance);
                foreach (Deployable d in part.Deployables)
                    d.Apply(deployFraction);
            }
        }

        /// <summary>Load another satellite JSON (path relative to StreamingAssets) and replay its deployment.</summary>
        public void LoadSatellite(string file)
        {
            if (string.IsNullOrEmpty(file))
                return;
            jsonFile = file;
            Rebuild();
            if (Application.isPlaying)
                ReplayDeployment();
        }

        private float EffectiveExplodeDistance
        {
            get { return (Data != null && Data.explodeDistance > 0f) ? Data.explodeDistance : explodeDistance; }
        }

        public void Clear()
        {
            foreach (GeneratedPart part in _parts)
                foreach (Mesh mesh in part.Meshes)
                    DestroySafe(mesh);

            _parts.Clear();
            _basePositions.Clear();
            _explodeDirections.Clear();

            if (_generatedRoot != null)
                DestroySafe(_generatedRoot.gameObject);
            _generatedRoot = null;

            // Remove leftovers (e.g. after a script reload)
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name == GeneratedRootName)
                    DestroySafe(child.gameObject);
            }
        }

        // ------------------------------------------------------------------ on-screen controls

        private GUIStyle _wrapStyle;

        private void OnGUI()
        {
            if (!Application.isPlaying || !showOnScreenControls)
            {
                GuiRect = Rect.zero;
                return;
            }

            if (_wrapStyle == null)
            {
                _wrapStyle = new GUIStyle(GUI.skin.label);
                _wrapStyle.wordWrap = true;
            }

            var rect = new Rect(12f, 12f, 320f, 470f);
            GuiRect = rect;

            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.Label("Satellite gallery");
            int count = Mathf.Min(galleryFiles != null ? galleryFiles.Length : 0, galleryLabels != null ? galleryLabels.Length : 0);
            for (int i = 0; i < count; i++)
            {
                string label = (galleryFiles[i] == jsonFile ? "> " : "   ") + galleryLabels[i];
                if (GUILayout.Button(label))
                    LoadSatellite(galleryFiles[i]);
            }
            GUILayout.Space(6f);

            if (Data != null)
            {
                GUILayout.Label(Data.name + "   [" + Data.orbitClass + "]");
                if (!string.IsNullOrEmpty(Data.orbitSummary))
                    GUILayout.Label(Data.orbitSummary, _wrapStyle);
                GUILayout.Label(_parts.Count + " parts generated from JSON");
                GUILayout.Space(4f);

                GUILayout.Label("Deploy: " + deployFraction.ToString("0.00"));
                float d = GUILayout.HorizontalSlider(deployFraction, 0f, 1f);
                if (!Mathf.Approximately(d, deployFraction))
                {
                    deployFraction = d;
                    _animatingDeploy = false;
                }

                GUILayout.Label("Explode: " + explode.ToString("0.00"));
                explode = GUILayout.HorizontalSlider(explode, 0f, 1f);

                spinWheels = GUILayout.Toggle(spinWheels, " Spin reaction wheels");

                if (GUILayout.Button("Replay deployment"))
                    ReplayDeployment();
                if (GUILayout.Button("Reload JSON"))
                    Rebuild();
            }

            GUILayout.Label("Drag: orbit   Scroll: zoom");
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ internals

        private SatelliteAssemblyData LoadAssembly()
        {
            string path = Path.Combine(Application.streamingAssetsPath, jsonFile);
            if (!File.Exists(path))
            {
                Debug.LogError("[ProceduralSatelliteBuilder] JSON not found: " + path);
                return null;
            }

            try
            {
                var data = JsonUtility.FromJson<SatelliteAssemblyData>(File.ReadAllText(path));
                if (data == null || data.parts == null || data.parts.Count == 0)
                    Debug.LogWarning("[ProceduralSatelliteBuilder] JSON has no parts: " + path);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError("[ProceduralSatelliteBuilder] Failed to parse " + path + ": " + e.Message);
                return null;
            }
        }

        private static Quaternion SafeLookRotation(Vector3 forward, Vector3 up)
        {
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
            if (Vector3.Cross(forward, up).sqrMagnitude < 1e-8f)
                up = Mathf.Abs(forward.normalized.y) < 0.9f ? Vector3.up : Vector3.right;
            return Quaternion.LookRotation(forward, up);
        }

        private static Vector3 ExplodeDirection(PartSpec spec)
        {
            if (spec.type == "cubesat_frame")
                return Vector3.zero;
            Vector3 p = spec.position;
            return p.sqrMagnitude < 1e-6f ? Vector3.zero : p.normalized;
        }

        private static void MarkDontSave(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
