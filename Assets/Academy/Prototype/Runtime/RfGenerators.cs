using System.Collections.Generic;
using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>
    /// Generators for antennas, reflectors, feeds and sensors used on MEO and GEO satellites.
    /// Convention: each part points out along its local +Z.
    /// </summary>
    public static class RfGenerators
    {
        private static readonly Quaternion YToZ = Quaternion.Euler(90f, 0f, 0f);

        // ================================================================== Offset reflector
        // Hinge at the origin. Deployed pose (deployAngle, normally 0): the paraboloid vertex sits at
        // vertexOffset and its focus at vertexOffset + (0, 0, focalLength), i.e. on the feed horn.
        // Stowed pose (stowedAngle): the whole reflector is folded against the bus about local +X.

        public static void BuildReflector(PartSpec s, GeneratedPart part)
        {
            float R = s.diameter * 0.5f;
            float f = Mathf.Max(0.05f, s.focalLength);
            float h = s.offsetHeight;
            float fourF = 4f * f;
            Vector3 vertex = s.vertexOffset;

            var bracket = new ProceduralMeshBuilder();
            bracket.AddBox(Vector3.zero, new Vector3(R * 0.12f, R * 0.08f, R * 0.12f), "aluminium");
            PartGenerators.Bake("HingeBracket", part.Root.transform, bracket, part);

            Transform hinge = PartGenerators.NewChild("ReflectorHinge", part.Root.transform, Vector3.zero);
            part.Deployables.Add(new Deployable
            {
                Transform = hinge,
                Mode = DeployMode.Rotate,
                Axis = Vector3.right,
                StowedAngle = s.stowedAngle,
                TargetAngle = s.deployAngle
            });

            var rb = new ProceduralMeshBuilder();
            rb.Push(vertex, Quaternion.identity);

            rb.AddOffsetParaboloid(R, f, h, 12, 56, "reflector_white", true);

            // Stiffening rim
            var rim = new List<Vector3>();
            for (int j = 0; j < 64; j++)
                rim.Add(ProceduralMeshBuilder.ParaboloidPoint(R, j / 64f * Mathf.PI * 2f, h, fourF));
            rb.AddTube(rim, R * 0.012f, 8, "graphite", true);

            // Radial ribs on the back of the shell
            Vector3 behind = new Vector3(0f, 0f, -R * 0.02f);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                var rib = new List<Vector3>();
                for (int q = 0; q <= 8; q++)
                    rib.Add(ProceduralMeshBuilder.ParaboloidPoint(R * 0.97f * q / 8f, a, h, fourF) + behind);
                rb.AddTube(rib, R * 0.008f, 6, "graphite");
            }

            Vector3 apertureCentre = ProceduralMeshBuilder.ParaboloidPoint(0f, 0f, h, fourF);
            rb.Pop();

            // Deployment arm from the hinge to a 2-axis gimbal on the back of the dish
            Vector3 gimbal = vertex + apertureCentre + new Vector3(0f, 0f, -R * 0.07f);
            rb.AddCylinderBetween(Vector3.zero, gimbal, R * 0.025f, 12, "graphite");
            rb.AddBox(gimbal, Vector3.one * R * 0.08f, "anodized_black");
            rb.AddCylinderBetween(gimbal, vertex + apertureCentre + behind, R * 0.02f, 10, "graphite");

            PartGenerators.Bake("Reflector", hinge, rb, part);
        }

        // ================================================================== Feed horn
        // Conical horn pointing along +Z (aperture radius = radius, length = length), waveguide behind.

        public static void BuildFeedHorn(PartSpec s, GeneratedPart part)
        {
            float R = s.radius;
            float L = s.length;
            float rt = R * 0.28f;

            var mb = new ProceduralMeshBuilder();
            mb.Push(Vector3.zero, YToZ);
            mb.AddLathe(new List<Vector2> { new Vector2(rt, 0f), new Vector2(R, L) }, 32, "white_paint", true, false);
            mb.AddLathe(new List<Vector2>
            {
                new Vector2(R, L), new Vector2(R * 1.08f, L), new Vector2(R * 1.08f, L * 0.95f)
            }, 32, "aluminium", true, false);
            mb.AddCylinder(new Vector3(0f, -L * 0.08f, 0f), rt * 1.6f, L * 0.08f, 20, "aluminium");
            mb.Pop();

            // Rectangular waveguide and orthomode transducer behind the horn
            mb.AddBox(new Vector3(0f, 0f, -L * 0.28f), new Vector3(rt * 1.8f, rt * 1.0f, L * 0.4f), "aluminium");
            mb.AddBox(new Vector3(rt * 1.6f, 0f, -L * 0.22f), new Vector3(rt * 1.2f, rt * 0.9f, rt * 1.2f), "aluminium");
            PartGenerators.Bake("FeedHorn", part.Root.transform, mb, part);
        }

        // ================================================================== Omni antenna (TT&C)

        public static void BuildOmniAntenna(PartSpec s, GeneratedPart part)
        {
            float L = s.length;
            var mb = new ProceduralMeshBuilder();
            mb.Push(Vector3.zero, YToZ);
            mb.AddCylinder(Vector3.zero, L * 0.12f, L * 0.06f, 20, "aluminium");
            mb.AddCylinder(Vector3.zero, L * 0.035f, L * 0.62f, 12, "white_paint");
            float r = L * 0.18f, hh = L * 0.19f;
            mb.Push(new Vector3(0f, L * 0.62f, 0f), Quaternion.identity);
            mb.AddLathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(r, hh), new Vector2(0f, hh * 2f) }, 28, "aluminium", true, false);
            mb.Pop();
            mb.Pop();
            PartGenerators.Bake("OmniAntenna", part.Root.transform, mb, part);
        }

        // ================================================================== Helix antenna / helix array
        // Navigation satellites broadcast L-band signals from arrays of helical elements.

        public static void BuildHelixAntenna(PartSpec s, GeneratedPart part, bool array)
        {
            float hr = s.helixRadius;
            float len = s.length;
            var mb = new ProceduralMeshBuilder();
            mb.Push(Vector3.zero, YToZ);

            if (array)
            {
                int n = Mathf.Max(1, s.elements);
                float plateR = s.arrayRadius + hr * 2.6f;
                mb.AddCylinder(Vector3.zero, plateR, hr * 0.6f, 48, "white_paint");
                float top = hr * 0.6f;
                AddHelixElement(mb, new Vector3(0f, top, 0f), hr, s.helixTurns, len);
                for (int i = 0; i < n - 1; i++)
                {
                    float a = i / (float)(n - 1) * Mathf.PI * 2f;
                    AddHelixElement(mb, new Vector3(Mathf.Cos(a) * s.arrayRadius, top, Mathf.Sin(a) * s.arrayRadius), hr, s.helixTurns, len);
                }
            }
            else
            {
                mb.AddCylinder(Vector3.zero, hr * 2.4f, hr * 0.3f, 32, "aluminium");
                AddHelixElement(mb, new Vector3(0f, hr * 0.3f, 0f), hr, s.helixTurns, len);
            }

            mb.Pop();
            PartGenerators.Bake(array ? "HelixArray" : "HelixAntenna", part.Root.transform, mb, part);
        }

        private static void AddHelixElement(ProceduralMeshBuilder mb, Vector3 b, float hr, float turns, float len)
        {
            // Ground cup
            mb.Push(b, Quaternion.identity);
            mb.AddLathe(new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(hr * 1.9f, 0f), new Vector2(hr * 1.9f, hr * 0.5f)
            }, 24, "aluminium", true, false);
            // Dielectric support rod
            mb.AddCylinder(Vector3.zero, hr * 0.22f, len, 10, "white_paint");
            // Copper coil
            int steps = Mathf.Max(12, Mathf.RoundToInt(turns * 20f));
            var path = new List<Vector3>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float a = t * turns * Mathf.PI * 2f;
                path.Add(new Vector3(Mathf.Cos(a) * hr, hr * 0.3f + len * t * 0.95f, Mathf.Sin(a) * hr));
            }
            mb.AddTube(path, hr * 0.07f, 6, "copper");
            mb.Pop();
        }

        // ================================================================== Laser retroreflector array
        // Ground lasers range to these corner cubes for precise orbit determination (navigation satellites).

        public static void BuildRetroreflector(PartSpec s, GeneratedPart part)
        {
            int cols = Mathf.Max(1, s.cellColumns);
            int rows = Mathf.Max(1, s.cellRows);
            float w = s.width, h = s.height;
            float pitch = Mathf.Min(w / cols, h / rows);

            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, 0f, -pitch * 0.25f), new Vector3(w, h, pitch * 0.5f), "aluminium");
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    float x = -w * 0.5f + (c + 0.5f) * (w / cols);
                    float y = -h * 0.5f + (r + 0.5f) * (h / rows);
                    mb.Push(new Vector3(x, y, 0f), YToZ);
                    mb.AddCylinder(Vector3.zero, pitch * 0.4f, pitch * 0.25f, 12, "retro_glass");
                    mb.Pop();
                }
            }
            PartGenerators.Bake("Retroreflector", part.Root.transform, mb, part);
        }

        // ================================================================== Attitude sensors
        // Body box behind the origin (front face at z = 0), optics pointing along +Z.

        public static void BuildSensor(PartSpec s, GeneratedPart part)
        {
            Vector3 size = s.size;
            string style = (s.style ?? "earth_sensor").Trim().ToLowerInvariant();
            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, 0f, -size.z * 0.5f), size, "anodized_black");
            float r = Mathf.Min(size.x, size.y);

            switch (style)
            {
                case "star_tracker":
                {
                    float r0 = r * 0.25f, r1 = r * 0.45f, L = s.length;
                    mb.Push(Vector3.zero, YToZ);
                    mb.AddLathe(new List<Vector2> { new Vector2(r0, 0f), new Vector2(r1, L) }, 28, "black_mli", true, false);
                    for (int k = 1; k <= 3; k++)
                    {
                        float y = L * k / 4f;
                        float rr = Mathf.Lerp(r0, r1, k / 4f);
                        mb.AddLathe(new List<Vector2> { new Vector2(rr, y), new Vector2(rr * 0.8f, y) }, 28, "anodized_black", true, false);
                    }
                    mb.AddCylinder(Vector3.zero, r0 * 0.8f, 0.002f, 20, "lens");
                    mb.Pop();
                    break;
                }
                case "sun_sensor":
                {
                    float rr = r * 0.3f;
                    var dome = new List<Vector2>();
                    for (int i = 0; i <= 8; i++)
                    {
                        float a = i / 8f * Mathf.PI * 0.5f;
                        dome.Add(new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr));
                    }
                    mb.Push(Vector3.zero, YToZ);
                    mb.AddLathe(dome, 24, "lens", true, false);
                    mb.Pop();
                    break;
                }
                default: // earth_sensor: two optical heads looking at the Earth's horizon
                {
                    float rr = r * 0.18f, L = size.z * 0.35f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        mb.Push(new Vector3(k * size.x * 0.22f, 0f, 0f), YToZ);
                        mb.AddCylinder(Vector3.zero, rr, L, 20, "anodized_black");
                        mb.AddCylinder(new Vector3(0f, L, 0f), rr * 0.75f, 0.002f, 20, "lens");
                        mb.Pop();
                    }
                    break;
                }
            }

            PartGenerators.Bake("Sensor", part.Root.transform, mb, part);
        }
    }
}
