using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Academy.Prototype
{
    /// <summary>
    /// Accumulates geometry (boxes, cylinders, lathed profiles) grouped by material key,
    /// with a transform stack, and bakes it into a multi-submesh Mesh.
    /// Winding is chosen automatically from the supplied normals, so callers never
    /// have to think about clockwise / counter-clockwise order.
    /// </summary>
    public sealed class ProceduralMeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<string> _materialKeys = new List<string>();
        private readonly List<List<int>> _submeshes = new List<List<int>>();
        private readonly Stack<Matrix4x4> _stack = new Stack<Matrix4x4>();
        private Matrix4x4 _matrix = Matrix4x4.identity;

        public int VertexCount { get { return _vertices.Count; } }
        public IList<string> MaterialKeys { get { return _materialKeys; } }

        // ------------------------------------------------------------------ transform stack

        public void Push(Vector3 translation, Quaternion rotation)
        {
            Push(Matrix4x4.TRS(translation, rotation, Vector3.one));
        }

        public void Push(Matrix4x4 m)
        {
            _stack.Push(_matrix);
            _matrix = _matrix * m;
        }

        public void Pop()
        {
            if (_stack.Count > 0)
                _matrix = _stack.Pop();
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Quad a-b-c-d (in order around its edge) with per-vertex normals.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                            Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, string material)
        {
            Vector3 face = Vector3.Cross(b - a, c - a);
            Vector3 wanted = na + nb + nc + nd;
            if (Vector3.Dot(face, wanted) < 0f)
            {
                Vector3 tp = b; b = d; d = tp;
                Vector3 tn = nb; nb = nd; nd = tn;
            }

            List<int> tris = Submesh(material);
            int ia = AddVertex(a, na, new Vector2(0f, 0f));
            int ib = AddVertex(b, nb, new Vector2(0f, 1f));
            int ic = AddVertex(c, nc, new Vector2(1f, 1f));
            int id = AddVertex(d, nd, new Vector2(1f, 0f));
            tris.Add(ia); tris.Add(ib); tris.Add(ic);
            tris.Add(ia); tris.Add(ic); tris.Add(id);
        }

        public void AddQuadFlat(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, string material)
        {
            AddQuad(a, b, c, d, normal, normal, normal, normal, material);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, string material)
        {
            Vector3 face = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(face, normal) < 0f)
            {
                Vector3 t = b; b = c; c = t;
            }

            List<int> tris = Submesh(material);
            tris.Add(AddVertex(a, normal, new Vector2(0.5f, 0.5f)));
            tris.Add(AddVertex(b, normal, new Vector2(0f, 1f)));
            tris.Add(AddVertex(c, normal, new Vector2(1f, 1f)));
        }

        /// <summary>Axis-aligned (in the current local frame) box.</summary>
        public void AddBox(Vector3 center, Vector3 size, string material)
        {
            Vector3 h = size * 0.5f;
            float x0 = center.x - h.x, x1 = center.x + h.x;
            float y0 = center.y - h.y, y1 = center.y + h.y;
            float z0 = center.z - h.z, z1 = center.z + h.z;

            // +X / -X
            AddQuadFlat(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, material);
            AddQuadFlat(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.left, material);
            // +Y / -Y
            AddQuadFlat(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, material);
            AddQuadFlat(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, material);
            // +Z / -Z
            AddQuadFlat(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, material);
            AddQuadFlat(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, material);
        }

        /// <summary>Cylinder along local +Y, starting at <paramref name="baseCenter"/>.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, string material,
                                bool capBottom = true, bool capTop = true)
        {
            segments = Mathf.Max(3, segments);
            Vector3 top = baseCenter + Vector3.up * height;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));

                AddQuad(baseCenter + d0 * radius, baseCenter + d1 * radius, top + d1 * radius, top + d0 * radius,
                        d0, d1, d1, d0, material);

                if (capBottom)
                    AddTriangle(baseCenter, baseCenter + d0 * radius, baseCenter + d1 * radius, Vector3.down, material);
                if (capTop)
                    AddTriangle(top, top + d0 * radius, top + d1 * radius, Vector3.up, material);
            }
        }

        /// <summary>
        /// Revolves a 2D profile (x = radius, y = height) around local +Y.
        /// Double-sided by default so open shells (nozzles, rings) look right from both sides.
        /// </summary>
        public void AddLathe(IList<Vector2> profile, int segments, string material,
                             bool doubleSided = true, bool closed = false)
        {
            if (profile == null || profile.Count < 2)
                return;

            segments = Mathf.Max(3, segments);
            int edgeCount = closed ? profile.Count : profile.Count - 1;

            for (int e = 0; e < edgeCount; e++)
            {
                Vector2 p0 = profile[e];
                Vector2 p1 = profile[(e + 1) % profile.Count];
                Vector2 dir = p1 - p0;
                if (dir.sqrMagnitude < 1e-12f)
                    continue;

                Vector2 n2 = new Vector2(dir.y, -dir.x).normalized;

                for (int i = 0; i < segments; i++)
                {
                    float a0 = (i / (float)segments) * Mathf.PI * 2f;
                    float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                    float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                    float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);

                    Vector3 v00 = new Vector3(p0.x * c0, p0.y, p0.x * s0);
                    Vector3 v01 = new Vector3(p0.x * c1, p0.y, p0.x * s1);
                    Vector3 v11 = new Vector3(p1.x * c1, p1.y, p1.x * s1);
                    Vector3 v10 = new Vector3(p1.x * c0, p1.y, p1.x * s0);

                    Vector3 n0 = new Vector3(n2.x * c0, n2.y, n2.x * s0);
                    Vector3 n1 = new Vector3(n2.x * c1, n2.y, n2.x * s1);

                    AddQuad(v00, v01, v11, v10, n0, n1, n1, n0, material);
                    if (doubleSided)
                        AddQuad(v00, v01, v11, v10, -n0, -n1, -n1, -n0, material);
                }
            }
        }

        /// <summary>Cylinder from point a to point b (any direction).</summary>
        public void AddCylinderBetween(Vector3 a, Vector3 b, float radius, int segments, string material, bool caps = true)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length < 1e-6f)
                return;

            Push(a, Quaternion.FromToRotation(Vector3.up, d / length));
            AddCylinder(Vector3.zero, radius, length, segments, material, caps, caps);
            Pop();
        }

        /// <summary>Round tube swept along a path (wires, helix coils, rims).</summary>
        public void AddTube(IList<Vector3> path, float radius, int sides, string material, bool closed = false)
        {
            if (path == null || path.Count < 2)
                return;

            sides = Mathf.Max(3, sides);
            int count = path.Count;
            var rings = new Vector3[count][];
            var ringNormals = new Vector3[count][];

            for (int i = 0; i < count; i++)
            {
                Vector3 prev = (i > 0) ? path[i - 1] : (closed ? path[count - 1] : path[i]);
                Vector3 next = (i < count - 1) ? path[i + 1] : (closed ? path[0] : path[i]);
                Vector3 tangent = (next - prev).normalized;
                Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 binormal = Vector3.Cross(tangent, reference).normalized;
                Vector3 normal = Vector3.Cross(binormal, tangent).normalized;

                rings[i] = new Vector3[sides];
                ringNormals[i] = new Vector3[sides];
                for (int k = 0; k < sides; k++)
                {
                    float a = (k / (float)sides) * Mathf.PI * 2f;
                    Vector3 n = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    ringNormals[i][k] = n;
                    rings[i][k] = path[i] + n * radius;
                }
            }

            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % count;
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    AddQuad(rings[i][k], rings[i][k1], rings[j][k1], rings[j][k],
                            ringNormals[i][k], ringNormals[i][k1], ringNormals[j][k1], ringNormals[j][k], material);
                }
            }
        }

        /// <summary>
        /// Offset-fed parabolic reflector surface. Paraboloid z = (x² + y²) / (4f) with its vertex at the
        /// current origin and its focus at (0, 0, f). The circular aperture of the given radius is centred
        /// at (0, offset) in x/y, so the dish is a section cut away from the axis (as on real comsats).
        /// </summary>
        public void AddOffsetParaboloid(float apertureRadius, float focalLength, float offset,
                                        int rings, int segments, string material, bool doubleSided = true)
        {
            rings = Mathf.Max(1, rings);
            segments = Mathf.Max(3, segments);
            float f4 = 4f * Mathf.Max(1e-4f, focalLength);

            for (int r = 0; r < rings; r++)
            {
                float rho0 = apertureRadius * r / rings;
                float rho1 = apertureRadius * (r + 1) / rings;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = (s / (float)segments) * Mathf.PI * 2f;
                    float a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;

                    Vector3 p00 = ParaboloidPoint(rho0, a0, offset, f4);
                    Vector3 p01 = ParaboloidPoint(rho0, a1, offset, f4);
                    Vector3 p11 = ParaboloidPoint(rho1, a1, offset, f4);
                    Vector3 p10 = ParaboloidPoint(rho1, a0, offset, f4);

                    Vector3 n00 = ParaboloidNormal(p00, focalLength);
                    Vector3 n01 = ParaboloidNormal(p01, focalLength);
                    Vector3 n11 = ParaboloidNormal(p11, focalLength);
                    Vector3 n10 = ParaboloidNormal(p10, focalLength);

                    AddQuad(p00, p01, p11, p10, n00, n01, n11, n10, material);
                    if (doubleSided)
                        AddQuad(p00, p01, p11, p10, -n00, -n01, -n11, -n10, material);
                }
            }
        }

        public static Vector3 ParaboloidPoint(float rho, float angle, float offset, float fourF)
        {
            float x = rho * Mathf.Cos(angle);
            float y = offset + rho * Mathf.Sin(angle);
            return new Vector3(x, y, (x * x + y * y) / fourF);
        }

        private static Vector3 ParaboloidNormal(Vector3 p, float focalLength)
        {
            float twoF = 2f * Mathf.Max(1e-4f, focalLength);
            return new Vector3(-p.x / twoF, -p.y / twoF, 1f).normalized;
        }

        /// <summary>
        /// Flat rectangular face split into tiles whose normals are slightly jittered:
        /// reads as crinkled multi-layer insulation (MLI) under lighting. Geometry stays planar.
        /// </summary>
        public void AddCrinkledFace(Vector3 center, Vector3 uAxis, Vector3 vAxis, Vector3 normal,
                                    float sizeU, float sizeV, float tileSize, float jitter, string material, int seed)
        {
            int tilesU = Mathf.Max(1, Mathf.RoundToInt(sizeU / Mathf.Max(0.01f, tileSize)));
            int tilesV = Mathf.Max(1, Mathf.RoundToInt(sizeV / Mathf.Max(0.01f, tileSize)));
            var random = new System.Random(seed);

            for (int i = 0; i < tilesU; i++)
            {
                for (int j = 0; j < tilesV; j++)
                {
                    float u0 = -sizeU * 0.5f + sizeU * i / tilesU;
                    float u1 = -sizeU * 0.5f + sizeU * (i + 1) / tilesU;
                    float v0 = -sizeV * 0.5f + sizeV * j / tilesV;
                    float v1 = -sizeV * 0.5f + sizeV * (j + 1) / tilesV;

                    Vector3 n = (normal
                                 + uAxis * (float)((random.NextDouble() * 2.0 - 1.0) * jitter)
                                 + vAxis * (float)((random.NextDouble() * 2.0 - 1.0) * jitter)).normalized;

                    AddQuadFlat(center + uAxis * u0 + vAxis * v0,
                                center + uAxis * u1 + vAxis * v0,
                                center + uAxis * u1 + vAxis * v1,
                                center + uAxis * u0 + vAxis * v1, n, material);
                }
            }
        }

        // ------------------------------------------------------------------ bake

        public void Build(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _submeshes.Count;
            for (int i = 0; i < _submeshes.Count; i++)
                mesh.SetTriangles(_submeshes[i], i);
            mesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ internals

        private List<int> Submesh(string material)
        {
            if (string.IsNullOrEmpty(material))
                material = "aluminium";

            int index = _materialKeys.IndexOf(material);
            if (index < 0)
            {
                _materialKeys.Add(material);
                _submeshes.Add(new List<int>());
                index = _materialKeys.Count - 1;
            }
            return _submeshes[index];
        }

        private int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            _vertices.Add(_matrix.MultiplyPoint3x4(position));
            _normals.Add(_matrix.MultiplyVector(normal).normalized);
            _uvs.Add(uv);
            return _vertices.Count - 1;
        }
    }
}
