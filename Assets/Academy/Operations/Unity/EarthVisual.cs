using System.Collections.Generic;
using UnityEngine;

namespace Academy.Operations
{
    /// <summary>
    /// A generic Earth globe for the operations view: ocean colour with a latitude/longitude grid (no invented
    /// geography), lit by the real Sun direction so the day/night terminator is correct, with ground-station markers.
    /// The mesh is generated so longitude/latitude map exactly onto the simulation's Earth-fixed frame.
    /// </summary>
    public sealed class EarthVisual : MonoBehaviour
    {
        private Transform _globe;
        private Material _material;
        private readonly List<Transform> _markers = new List<Transform>();

        private void Awake()
        {
            var globe = new GameObject("Globe");
            globe.transform.SetParent(transform, false);
            _globe = globe.transform;
            var filter = globe.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildSphere(96, 48);
            var renderer = globe.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _material = new Material(shader) { name = "Earth (generated)" };
            Texture2D tex = BuildTexture(2048, 1024);
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", tex);
            if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", tex);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
            if (_material.HasProperty("_Smoothness")) _material.SetFloat("_Smoothness", 0.35f);
            if (_material.HasProperty("_Glossiness")) _material.SetFloat("_Glossiness", 0.35f);
            renderer.sharedMaterial = _material;
        }

        public void SetStations(IList<GroundStationDef> stations)
        {
            foreach (Transform m in _markers)
                if (m != null) Destroy(m.gameObject);
            _markers.Clear();
            if (stations == null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            Color c = new Color(1f, 0.8f, 0.25f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

            foreach (GroundStationDef s in stations)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Station " + s.name;
                var col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);
                marker.GetComponent<Renderer>().sharedMaterial = mat;
                marker.transform.SetParent(_globe, false);
                Vector3 p = LatLonToLocal(s.latDeg, s.lonDeg);
                marker.transform.localPosition = p * 1.002f;
                marker.transform.localRotation = Quaternion.LookRotation(p);
                marker.transform.localScale = new Vector3(0.012f, 0.012f, 0.01f);
                _markers.Add(marker.transform);
            }
        }

        /// <summary>Place and spin the globe. positionEci = satellite position (km); the satellite sits at the world origin.</summary>
        public void UpdatePose(Vec3d satelliteEci, double timeJ2000, float worldDistance)
        {
            double r = satelliteEci.Length;
            Vec3d toEarth = -satelliteEci / r;
            float radius = (float)(worldDistance * OpsMath.EarthRadius / r);
            transform.position = ToUnity(toEarth) * worldDistance;
            transform.localScale = Vector3.one * radius;
            // Earth rotates about the inertial Z axis (Unity Y). Right-handed +gmst about Z = Unity -gmst about Y.
            float gmstDeg = (float)(OpsMath.Gmst(timeJ2000) * OpsMath.Rad2Deg);
            transform.rotation = Quaternion.AngleAxis(-gmstDeg, Vector3.up);
        }

        public static Vector3 ToUnity(Vec3d v) { return new Vector3((float)v.x, (float)v.z, (float)v.y); }

        /// <summary>Earth-fixed (ECEF) unit vector of a latitude/longitude, in the globe's Unity local frame.</summary>
        public static Vector3 LatLonToLocal(double latDeg, double lonDeg)
        {
            double lat = latDeg * OpsMath.Deg2Rad, lon = lonDeg * OpsMath.Deg2Rad;
            var ecef = new Vec3d(System.Math.Cos(lat) * System.Math.Cos(lon), System.Math.Cos(lat) * System.Math.Sin(lon), System.Math.Sin(lat));
            return ToUnity(ecef);
        }

        private static Mesh BuildSphere(int segments, int rings)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                double lat = -90.0 + 180.0 * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    double lon = -180.0 + 360.0 * s / segments;
                    Vector3 p = LatLonToLocal(lat, lon);
                    vertices.Add(p);
                    normals.Add(p);
                    uvs.Add(new Vector2((float)s / segments, (float)r / rings));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                    // Winding: outward faces, clockwise as seen from outside (Unity front faces)
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
            var mesh = new Mesh { name = "Earth (generated)" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // Make sure the winding matches the outward normals whatever the handedness works out to
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            Vector3 n = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
            Vector3 centre = (v[t[0]] + v[t[1]] + v[t[2]]) / 3f;
            if (Vector3.Dot(n, centre) < 0f)
            {
                for (int i = 0; i < t.Length; i += 3)
                {
                    int tmp = t[i + 1];
                    t[i + 1] = t[i + 2];
                    t[i + 2] = tmp;
                }
                mesh.triangles = t;
            }
            return mesh;
        }

        /// <summary>Ocean gradient with a 15 degree graticule; equator and prime meridian highlighted.</summary>
        private static Texture2D BuildTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Earth graticule (generated)", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            var pixels = new Color32[width * height];
            Color deep = new Color(0.05f, 0.16f, 0.34f), shallow = new Color(0.10f, 0.30f, 0.55f), ice = new Color(0.82f, 0.88f, 0.94f);
            Color grid = new Color(0.55f, 0.75f, 0.95f), special = new Color(1f, 0.85f, 0.45f);
            for (int y = 0; y < height; y++)
            {
                double lat = -90.0 + 180.0 * (y + 0.5) / height;
                double absLat = System.Math.Abs(lat);
                Color baseColor = Color.Lerp(shallow, deep, (float)(absLat / 90.0));
                if (absLat > 80) baseColor = Color.Lerp(baseColor, ice, (float)((absLat - 80) / 10.0));
                double latPx = 180.0 / height;
                bool latLine = System.Math.Abs(lat - System.Math.Round(lat / 15.0) * 15.0) < latPx * 0.8;
                bool equator = System.Math.Abs(lat) < latPx * 1.5;
                for (int x = 0; x < width; x++)
                {
                    double lon = -180.0 + 360.0 * (x + 0.5) / width;
                    double lonPx = 360.0 / width;
                    bool lonLine = System.Math.Abs(lon - System.Math.Round(lon / 15.0) * 15.0) < lonPx * 0.8;
                    bool prime = System.Math.Abs(lon) < lonPx * 1.5;
                    Color c = baseColor;
                    if (latLine || lonLine) c = Color.Lerp(c, grid, 0.45f);
                    if (equator || prime) c = Color.Lerp(c, special, 0.8f);
                    pixels[y * width + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }
    }
}
