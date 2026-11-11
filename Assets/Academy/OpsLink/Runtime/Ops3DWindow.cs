using System;
using System.Collections.Generic;
using Academy.Operations;
using Academy.Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Academy.OpsLink
{
    /// <summary>
    /// Floating 3D window over the OpenC3 browser: the Mission Operations camera rendered into a movable,
    /// resizable panel, so the learner sees how the satellite reacts (attitude, deployment, wheels, burns,
    /// eclipse, ground contact) while commanding it from OpenC3.
    /// </summary>
    public sealed class Ops3DWindow : MonoBehaviour
    {
        public enum ViewMode { CloseUp, Earth, SunView }

        static Ops3DWindow _current;
        public static bool IsOpen => _current != null;

        static readonly Color Bg = new Color(0.035f, 0.05f, 0.08f, 0.97f);
        static readonly Color Bar = new Color(0.08f, 0.11f, 0.16f, 1f);
        static readonly Color Btn = new Color(0.263f, 0.627f, 0.278f, 0.92f);
        static readonly Color BtnIdle = new Color(0.18f, 0.24f, 0.32f, 1f);
        const float BarHeight = 30f, StatusHeight = 22f, MinW = 320f, MinH = 220f;

        RectTransform _win, _viewport;
        RawImage _image;
        Text _title, _status;
        RenderTexture _rt;
        OperationsSimulation _ops;
        Camera _cam;
        PrototypeOrbitCamera _orbit;
        ViewMode _mode = ViewMode.CloseUp;
        bool _modeApplied, _maximized;
        Vector2 _restorePos, _restoreSize;
        float _baseDistance = 1f, _defaultFar = 1000f;
        readonly Dictionary<ViewMode, Image> _modeButtons = new Dictionary<ViewMode, Image>();

        /// <summary>Opens the window on the given overlay canvas, or closes it when it is open.</summary>
        public static void Toggle(Transform canvas)
        {
            if (_current != null) { Destroy(_current.gameObject); _current = null; return; }
            var go = new GameObject("Ops3DWindow", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            _current = go.AddComponent<Ops3DWindow>();
            _current.Build();
        }

        public static void CloseIfOpen()
        {
            if (_current != null) { Destroy(_current.gameObject); _current = null; }
        }

        // ------------------------------------------------------------------ UI

        void Build()
        {
            _win = (RectTransform)transform;
            _win.anchorMin = _win.anchorMax = _win.pivot = new Vector2(1f, 0f);
            _win.sizeDelta = new Vector2(Mathf.Min(560f, Screen.width * 0.42f), Mathf.Min(400f, Screen.height * 0.45f));
            _win.anchoredPosition = new Vector2(-16f, 40f);
            gameObject.AddComponent<Image>().color = Bg;
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.263f, 0.627f, 0.278f, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            // title bar (drag to move, double-click to maximise)
            var bar = Rect("Bar", _win, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -BarHeight), Vector2.zero);
            bar.gameObject.AddComponent<Image>().color = Bar;
            var drag = bar.gameObject.AddComponent<DragHandle>();
            drag.Moved = d => { if (!_maximized) _win.anchoredPosition += d; };
            drag.DoubleClicked = ToggleMaximize;

            _title = Label(bar, "3D", 13, FontStyle.Bold, TextAnchor.MiddleLeft);
            var trt = (RectTransform)_title.transform;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-300, 0);
            _title.raycastTarget = false;

            float x = -6f;
            x = BarButton(bar, "X", 26, x, () => { _current = null; Destroy(gameObject); }, null);
            x = BarButton(bar, "[ ]", 30, x, ToggleMaximize, null);
            x = BarButton(bar, "Sun", 46, x, () => SetMode(ViewMode.SunView), ViewMode.SunView);
            x = BarButton(bar, "Earth", 52, x, () => SetMode(ViewMode.Earth), ViewMode.Earth);
            BarButton(bar, "Close-up", 72, x, () => SetMode(ViewMode.CloseUp), ViewMode.CloseUp);

            // 3D view
            _viewport = Rect("View", _win, Vector2.zero, Vector2.one, new Vector2(2, StatusHeight), new Vector2(-2, -BarHeight));
            _image = _viewport.gameObject.AddComponent<RawImage>();
            _image.color = Color.white;

            // status line
            var status = Rect("Status", _win, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, StatusHeight));
            status.gameObject.AddComponent<Image>().color = Bar;
            _status = Label(status, "", 11, FontStyle.Normal, TextAnchor.MiddleLeft);
            var srt = (RectTransform)_status.transform;
            srt.offsetMin = new Vector2(26, 0); srt.offsetMax = new Vector2(-8, 0);
            _status.raycastTarget = false;

            // resize grip (bottom-left: the window is anchored to the bottom-right corner)
            var grip = Rect("Resize", _win, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(18, 18));
            grip.gameObject.AddComponent<Image>().color = new Color(0.263f, 0.627f, 0.278f, 0.9f);
            Label(grip, "//", 10, FontStyle.Bold, TextAnchor.MiddleCenter).raycastTarget = false;
            var rh = grip.gameObject.AddComponent<DragHandle>();
            rh.Moved = d =>
            {
                if (_maximized) return;
                Vector2 s = _win.sizeDelta + new Vector2(-d.x, -d.y);
                s.x = Mathf.Clamp(s.x, MinW, Screen.width - 20); s.y = Mathf.Clamp(s.y, MinH, Screen.height - 20);
                Vector2 grow = s - _win.sizeDelta;
                _win.sizeDelta = s;
                _win.anchoredPosition += new Vector2(0, -grow.y); // keep the top edge in place
            };
            UpdateModeButtons();
        }

        float BarButton(RectTransform bar, string text, float width, float right, Action click, ViewMode? mode)
        {
            var rt = Rect(text, bar, new Vector2(1, 0), new Vector2(1, 1), new Vector2(right - width, 4), new Vector2(right, -4));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = mode.HasValue ? BtnIdle : new Color(0.25f, 0.3f, 0.38f, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => click());
            Label(rt, text, 12, FontStyle.Bold, TextAnchor.MiddleCenter).raycastTarget = false;
            if (mode.HasValue) _modeButtons[mode.Value] = img;
            return right - width - 4;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static Text Label(RectTransform parent, string s, int size, FontStyle style, TextAnchor anchor)
        {
            var rt = Rect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = s; t.fontSize = size; t.fontStyle = style; t.alignment = anchor; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        void ToggleMaximize()
        {
            if (!_maximized)
            {
                _restorePos = _win.anchoredPosition; _restoreSize = _win.sizeDelta;
                _win.sizeDelta = new Vector2(Screen.width - 180f, Screen.height - 110f);
                _win.anchoredPosition = new Vector2(-10f, 40f);
            }
            else { _win.sizeDelta = _restoreSize; _win.anchoredPosition = _restorePos; }
            _maximized = !_maximized;
        }

        void SetMode(ViewMode m) { _mode = m; _modeApplied = false; UpdateModeButtons(); }

        void UpdateModeButtons()
        {
            foreach (var kv in _modeButtons) kv.Value.color = kv.Key == _mode ? Btn : BtnIdle;
        }

        // ------------------------------------------------------------------ rendering

        void OnEnable()
        {
            PrototypeOrbitCamera.PointerOverUi = p => !RectTransformUtility.RectangleContainsScreenPoint(_viewport, p, null);
        }

        void OnDisable()
        {
            // While OpenC3 is open, drags in the browser must not move the camera.
            PrototypeOrbitCamera.PointerOverUi = OpsBrowserScreen.IsOpen ? (Func<Vector2, bool>)(_ => true) : null;
            if (_cam != null) { _cam.targetTexture = null; _cam.farClipPlane = _defaultFar; }
            if (_orbit != null) _orbit.Frame(_baseDistance);
        }

        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_current == this) _current = null;
        }

        void Update()
        {
            if (_ops == null || _ops.Sim == null)
            {
                _ops = FindFirstObjectByType<OperationsSimulation>();
                _cam = null;
                if (_ops == null || _ops.Sim == null) { _status.text = "  No satellite is flying."; return; }
            }
            if (_cam == null)
            {
                _cam = _ops.mainCamera;
                _orbit = _ops.orbitCamera;
                _defaultFar = _cam != null ? _cam.farClipPlane : 1000f;
                _baseDistance = Mathf.Max(0.3f, _ops.Template != null ? _ops.Template.cameraDistance : 1f);
                _modeApplied = false;
                if (_ops.GetComponent<Ops3DEffects>() == null) _ops.gameObject.AddComponent<Ops3DEffects>();
                _title.text = "3D  -  " + _ops.Sim.SatelliteName + "   (drag to rotate, wheel to zoom)";
            }
            if (_cam == null) return;

            // render target follows the window size
            Vector2 size = _viewport.rect.size;
            int w = Mathf.Max(64, Mathf.RoundToInt(size.x)), h = Mathf.Max(64, Mathf.RoundToInt(size.y));
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 24) { name = "Ops3D", antiAliasing = 4 };
                _rt.Create();
                _image.texture = _rt;
            }
            if (_cam.targetTexture != _rt) _cam.targetTexture = _rt;

            ApplyMode();
            _status.text = StatusText(_ops.Sim);
        }

        void ApplyMode()
        {
            if (_orbit == null) return;
            float earthDistance = _baseDistance * 400f; // same scale as OperationsSimulation
            if (!_modeApplied)
            {
                _modeApplied = true;
                switch (_mode)
                {
                    case ViewMode.CloseUp: _orbit.Frame(_baseDistance); _cam.farClipPlane = _defaultFar; break;
                    case ViewMode.Earth:
                        // further out, Earth fills the background: attitude relative to the planet
                        _orbit.Frame(_baseDistance * 6f);
                        if (_ops.earth != null)
                        {
                            // look down past the satellite at the Earth (slightly off-nadir so both show)
                            Vector3 toEarth = _ops.earth.transform.position.normalized;
                            Vector3 e = Quaternion.LookRotation(toEarth, Vector3.up).eulerAngles;
                            _orbit.pitch = Mathf.Clamp((e.x > 180f ? e.x - 360f : e.x) - 20f, -85f, 85f);
                            _orbit.yaw = e.y;
                        }
                        _cam.farClipPlane = Mathf.Max(_defaultFar, earthDistance * 2.5f);
                        break;
                    case ViewMode.SunView: _orbit.Frame(_baseDistance * 1.4f); _cam.farClipPlane = _defaultFar; break;
                }
            }
            if (_mode == ViewMode.SunView && _ops.sunLight != null)
            {
                // camera between the Sun and the satellite: lit faces and the solar arrays face the viewer
                Vector3 toSun = -_ops.sunLight.transform.forward;
                Quaternion look = Quaternion.LookRotation(-toSun, Vector3.up);
                Vector3 e = look.eulerAngles;
                _orbit.pitch = e.x > 180f ? e.x - 360f : e.x;
                _orbit.yaw = e.y;
            }
        }

        static string StatusText(SatelliteSim sim)
        {
            var parts = new List<string>();
            parts.Add("ADCS " + sim.Adcs.Mode.ToString().ToUpperInvariant());
            parts.Add("rate " + sim.Env.RateDegPerSec.ToString("0.00") + " deg/s");
            parts.Add(sim.Env.InSun ? "SUNLIGHT" : "ECLIPSE");
            parts.Add(sim.Link.Contact ? "CONTACT " + sim.Link.StationName : "no contact");
            foreach (ThrusterSet t in sim.ThrusterSets)
                if (t.Firing) { parts.Add("FIRING " + t.Name); break; }
            return "  " + string.Join("   |   ", parts);
        }

        /// <summary>Small drag helper for the title bar and the resize grip.</summary>
        sealed class DragHandle : MonoBehaviour, IDragHandler, IPointerClickHandler
        {
            public Action<Vector2> Moved;
            public Action DoubleClicked;
            public void OnDrag(PointerEventData e) { Moved?.Invoke(e.delta / (GetComponentInParent<Canvas>()?.scaleFactor ?? 1f)); }
            public void OnPointerClick(PointerEventData e) { if (e.clickCount == 2) DoubleClicked?.Invoke(); }
        }
    }

    /// <summary>
    /// Visual cues the Mission Operations scene lacks: Earth shadow (eclipse dims the Sun), thruster plumes while
    /// firing, and a line to the ground station while in contact.
    /// </summary>
    public sealed class Ops3DEffects : MonoBehaviour
    {
        OperationsSimulation _ops;
        float _sunIntensity = -1f;
        readonly List<(ThrusterSet set, Transform plume)> _plumes = new List<(ThrusterSet, Transform)>();
        SatelliteSim _builtFor;
        LineRenderer _link;
        Material _plumeMat, _linkMat;

        void Awake() { _ops = GetComponent<OperationsSimulation>(); }

        void LateUpdate()
        {
            if (_ops == null || _ops.Sim == null) return;
            SatelliteSim sim = _ops.Sim;
            if (_builtFor != sim) Rebuild(sim);

            // eclipse: the Sun goes out behind the Earth
            if (_ops.sunLight != null)
            {
                if (_sunIntensity < 0f) _sunIntensity = _ops.sunLight.intensity;
                float target = sim.Env.InSun ? _sunIntensity : 0.04f;
                _ops.sunLight.intensity = Mathf.MoveTowards(_ops.sunLight.intensity, target, Time.unscaledDeltaTime * 3f);
            }

            // plumes
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 18f, 0f);
            foreach (var p in _plumes)
            {
                if (p.plume == null) continue;
                bool on = p.set.Firing;
                if (p.plume.gameObject.activeSelf != on) p.plume.gameObject.SetActive(on);
                if (on) p.plume.localScale = new Vector3(1f, 1f, flicker);
            }

            // ground link
            if (_link != null)
            {
                Transform station = null;
                if (sim.Link.Contact && _ops.earth != null)
                    station = FindStation(_ops.earth.transform, "Station " + sim.Link.StationName);
                _link.enabled = station != null;
                if (station != null)
                {
                    _link.SetPosition(0, _ops.view.transform.position);
                    _link.SetPosition(1, station.position);
                    float wdt = Mathf.Max(0.003f, Vector3.Distance(_ops.view.transform.position, Camera.main != null ? Camera.main.transform.position : Vector3.zero) * 0.004f);
                    _link.startWidth = _link.endWidth = wdt;
                }
            }
        }

        static Transform FindStation(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        void Rebuild(SatelliteSim sim)
        {
            _builtFor = sim;
            foreach (var p in _plumes) if (p.plume != null) Destroy(p.plume.gameObject);
            _plumes.Clear();
            if (_plumeMat == null) _plumeMat = Unlit(new Color(1f, 0.62f, 0.2f));
            if (_linkMat == null) _linkMat = Unlit(new Color(0.35f, 0.95f, 0.45f));

            float size = Mathf.Max(0.3f, _ops.Template != null ? _ops.Template.cameraDistance : 1f);
            Mesh cone = Cone();
            foreach (ThrusterSet set in sim.ThrusterSets)
            {
                foreach (Thruster th in set.Thrusters)
                {
                    var go = new GameObject("Plume " + set.Id, typeof(MeshFilter), typeof(MeshRenderer));
                    go.GetComponent<MeshFilter>().sharedMesh = cone;
                    var r = go.GetComponent<MeshRenderer>();
                    r.sharedMaterial = _plumeMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var holder = new GameObject("PlumeHolder " + set.Id).transform;
                    holder.SetParent(_ops.view.transform, false);
                    // body frame -> model frame swaps y and z (see OperationsSimulation.SyncVisuals)
                    holder.localPosition = new Vector3((float)th.Position.x, (float)th.Position.z, (float)th.Position.y);
                    Vector3 exhaust = -new Vector3((float)th.ForceDir.x, (float)th.ForceDir.z, (float)th.ForceDir.y);
                    if (exhaust.sqrMagnitude < 1e-6f) exhaust = Vector3.back;
                    holder.localRotation = Quaternion.LookRotation(exhaust.normalized);
                    float len = size * (set.ThrustN * Mathf.Max(1, set.Thrusters.Count) > 50 ? 0.45f : 0.18f);
                    go.transform.SetParent(holder, false);
                    go.transform.localScale = new Vector3(len * 0.22f, len * 0.22f, len);
                    // the holder is what flickers; keep the cone's own scale
                    var plume = holder;
                    holder.gameObject.SetActive(false);
                    _plumes.Add((set, plume));
                }
            }

            if (_link == null)
            {
                var lgo = new GameObject("Ground link");
                lgo.transform.SetParent(transform, false);
                _link = lgo.AddComponent<LineRenderer>();
                _link.positionCount = 2;
                _link.useWorldSpace = true;
                _link.sharedMaterial = _linkMat;
                _link.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _link.enabled = false;
            }
        }

        static Material Unlit(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        static Mesh Cone()
        {
            const int n = 16;
            var v = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0.15f));
            }
            v.Add(new Vector3(0, 0, 1f));
            int tip = v.Count - 1;
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                tris.AddRange(new[] { 0, b, a, a, b, tip });
            }
            var mesh = new Mesh { name = "Plume" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
