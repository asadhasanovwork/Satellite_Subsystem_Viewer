using System.Collections.Generic;
using UnityEngine;

namespace Academy.Prototype
{
    public enum DeployMode
    {
        /// <summary>Rotate the transform about <see cref="Deployable.Axis"/> by TargetAngle * fraction.</summary>
        Rotate,
        /// <summary>Scale the transform along <see cref="Deployable.Axis"/> from StowedScale to 1 (tape antennas).</summary>
        Extend
    }

    /// <summary>A moving joint inside a generated part (solar-panel hinge, antenna tape...).</summary>
    public sealed class Deployable
    {
        public Transform Transform;
        public DeployMode Mode;
        public Vector3 Axis = Vector3.right;
        /// <summary>Hinge angle when deployed (degrees).</summary>
        public float TargetAngle = 90f;
        /// <summary>Hinge angle when stowed for launch (degrees).</summary>
        public float StowedAngle = 0f;
        public float StowedScale = 0.05f;
        /// <summary>Portion of the satellite's overall deployment (0..1) during which this joint moves.</summary>
        public float Start = 0f;
        public float End = 1f;

        public void Apply(float overallFraction)
        {
            if (Transform == null)
                return;

            float fraction = Mathf.Clamp01((overallFraction - Start) / Mathf.Max(1e-4f, End - Start));
            if (Mode == DeployMode.Rotate)
            {
                Transform.localRotation = Quaternion.AngleAxis(Mathf.Lerp(StowedAngle, TargetAngle, fraction), Axis);
            }
            else
            {
                float s = Mathf.Lerp(StowedScale, 1f, fraction);
                Vector3 a = new Vector3(Mathf.Abs(Axis.x), Mathf.Abs(Axis.y), Mathf.Abs(Axis.z));
                Transform.localScale = Vector3.one + a * (s - 1f);
            }
        }
    }

    /// <summary>Result of generating one part.</summary>
    public sealed class GeneratedPart
    {
        public PartSpec Spec;
        public GameObject Root;
        public readonly List<Deployable> Deployables = new List<Deployable>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
    }

    /// <summary>
    /// Procedural generators for satellite hardware. Every generator builds in the part's
    /// local frame (metres); the builder then places the part using PartSpec.position/forward/up.
    /// </summary>
    public static class PartGenerators
    {
        public static GeneratedPart Create(PartSpec spec, Transform parent)
        {
            var part = new GeneratedPart { Spec = spec };
            string name = !string.IsNullOrEmpty(spec.label) ? spec.label : (spec.id ?? spec.type ?? "Part");
            part.Root = new GameObject(name);
            part.Root.transform.SetParent(parent, false);

            string type = (spec.type ?? string.Empty).Trim().ToLowerInvariant();
            switch (type)
            {
                case "cubesat_frame": BuildCubeSatFrame(spec, part); break;
                case "solar_panel": BuildSolarPanel(spec, part); break;
                case "reaction_wheel": BuildReactionWheel(spec, part); break;
                case "antenna": BuildAntenna(spec, part); break;
                case "thruster": BuildThruster(spec, part); break;
                case "box": BuildBox(spec, part); break;
                case "bus": BusAndWingGenerators.BuildBus(spec, part); break;
                case "solar_wing": BusAndWingGenerators.BuildSolarWing(spec, part); break;
                case "reflector": RfGenerators.BuildReflector(spec, part); break;
                case "feed_horn": RfGenerators.BuildFeedHorn(spec, part); break;
                case "sensor": RfGenerators.BuildSensor(spec, part); break;
                case "retroreflector": RfGenerators.BuildRetroreflector(spec, part); break;
                case "magnetorquer": BuildMagnetorquer(spec, part); break;
                case "tank": BuildTank(spec, part); break;
                case "radiator_panel": BusAndWingGenerators.BuildRadiatorPanel(spec, part); break;
                case "adapter_ring": BusAndWingGenerators.BuildAdapterRing(spec, part); break;
                case "sada": BusAndWingGenerators.BuildSada(spec, part); break;
                default:
                    Debug.LogWarning("[PartGenerators] Unknown part type '" + spec.type + "' (" + spec.id + "). Using placeholder.");
                    BuildPlaceholder(part);
                    break;
            }

            if (spec.scale > 0f && !Mathf.Approximately(spec.scale, 1f))
                part.Root.transform.localScale = Vector3.one * spec.scale;

            foreach (Deployable d in part.Deployables)
            {
                d.Start = Mathf.Clamp01(spec.deployStart);
                d.End = Mathf.Clamp01(spec.deployEnd <= spec.deployStart ? 1f : spec.deployEnd);
            }

            var info = part.Root.AddComponent<GeneratedPartInfo>();
            info.partId = spec.id;
            info.partType = spec.type;
            info.label = spec.label;
            info.description = spec.description;
            return part;
        }

        // ================================================================== CubeSat frame
        // Local frame: long axis = +Y, centred on the origin. Footprint unitSize x unitSize.

        private static void BuildCubeSatFrame(PartSpec s, GeneratedPart part)
        {
            int units = Mathf.Max(1, s.units);
            float half = s.unitSize * 0.5f;
            float totalHeight = units * s.unitHeight;
            float hy = totalHeight * 0.5f;
            float rw = s.railWidth;
            float railCentre = half - rw * 0.5f;
            float inner = 2f * (half - rw);
            const float barThickness = 0.0035f;

            var mb = new ProceduralMeshBuilder();

            // Four corner rails
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.AddBox(new Vector3(sx * railCentre, 0f, sz * railCentre), new Vector3(rw, totalHeight, rw), "aluminium");

            // Ring of bars at every unit boundary (ends included)
            for (int k = 0; k <= units; k++)
            {
                float y = Mathf.Clamp(-hy + k * s.unitHeight, -hy + barThickness * 0.5f, hy - barThickness * 0.5f);
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.AddBox(new Vector3(0f, y, sz * railCentre), new Vector3(inner, barThickness, rw * 0.8f), "aluminium");
                for (int sx = -1; sx <= 1; sx += 2)
                    mb.AddBox(new Vector3(sx * railCentre, y, 0f), new Vector3(rw * 0.8f, barThickness, inner), "aluminium");
            }

            // End plates
            mb.AddBox(new Vector3(0f, hy - 0.00075f, 0f), new Vector3(inner, 0.0015f, inner), "anodized_black");
            mb.AddBox(new Vector3(0f, -hy + 0.00075f, 0f), new Vector3(inner, 0.0015f, inner), "anodized_black");

            // Optional PC/104-style electronics stack in the top unit
            if (s.pcbStack)
            {
                float stackBase = hy - s.unitHeight;
                float boardSize = inner - 0.006f;
                for (int b = 0; b < 5; b++)
                {
                    float y = stackBase + 0.015f + b * 0.018f;
                    mb.AddBox(new Vector3(0f, y, 0f), new Vector3(boardSize, 0.0016f, boardSize), "pcb_green");
                    mb.AddBox(new Vector3(0.010f, y + 0.0017f, -0.010f), new Vector3(0.014f, 0.0018f, 0.014f), "anodized_black");
                    mb.AddBox(new Vector3(-0.016f, y + 0.0013f, 0.012f), new Vector3(0.008f, 0.001f, 0.005f), "anodized_black");
                    mb.AddBox(new Vector3(0f, y + 0.0035f, boardSize * 0.5f - 0.004f), new Vector3(0.05f, 0.0055f, 0.005f), "connector");
                }
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        mb.AddCylinder(new Vector3(sx * 0.033f, stackBase + 0.010f, sz * 0.033f), 0.0015f, 0.085f, 10, "steel");
            }

            Bake("Frame", part.Root.transform, mb, part);
        }

        // ================================================================== Solar panel
        // Local frame: hinge / bottom edge at the origin, panel extends along +Y,
        // width along X, cells face +Z, substrate behind (towards -Z).
        // Deployable panels rotate about local +X at the hinge.

        private static void BuildSolarPanel(PartSpec s, GeneratedPart part)
        {
            float w = s.width;
            float h = s.height;
            float t = s.thickness;
            Transform meshParent = part.Root.transform;

            if (s.deployable)
            {
                var hb = new ProceduralMeshBuilder();
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * w * 0.3f;
                    // Cylinder along +X: rotate local +Y onto +X.
                    hb.Push(new Vector3(x - 0.006f, 0f, -t * 0.5f), Quaternion.Euler(0f, 0f, -90f));
                    hb.AddCylinder(Vector3.zero, 0.0022f, 0.012f, 12, "steel");
                    hb.Pop();
                }
                Bake("HingeBarrels", part.Root.transform, hb, part);

                var hinge = new GameObject("Hinge").transform;
                hinge.SetParent(part.Root.transform, false);
                meshParent = hinge;
                part.Deployables.Add(new Deployable
                {
                    Transform = hinge,
                    Mode = DeployMode.Rotate,
                    Axis = Vector3.right,
                    TargetAngle = s.deployAngle
                });
            }

            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, h * 0.5f, -t * 0.5f), new Vector3(w, h, t), "panel_substrate");
            AddSolarCells(mb, s, w, h, 0f, 1f, 0.003f, 0.0012f);
            if (s.cellsBothSides)
                AddSolarCells(mb, s, w, h, -t, -1f, 0.003f, 0.0012f);
            Bake("Panel", meshParent, mb, part);
        }

        internal static void AddSolarCells(ProceduralMeshBuilder mb, PartSpec s, float w, float h, float surfaceZ, float side,
                                           float margin, float gap)
        {
            int cols = Mathf.Max(1, s.cellColumns);
            int rows = Mathf.Max(1, s.cellRows);
            float cellThickness = Mathf.Max(0.0004f, gap * 0.3f);

            float cw = (w - 2f * margin - (cols - 1) * gap) / cols;
            float ch = (h - 2f * margin - (rows - 1) * gap) / rows;
            if (cw <= 0f || ch <= 0f)
                return;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = -w * 0.5f + margin + cw * 0.5f + c * (cw + gap);
                    float y = margin + ch * 0.5f + r * (ch + gap);
                    float z = surfaceZ + side * cellThickness * 0.5f;
                    mb.AddBox(new Vector3(x, y, z), new Vector3(cw, ch, cellThickness), "solar_cell");

                    // Silver interconnect (bus bar) along the bottom edge of each cell
                    float barHeight = Mathf.Max(0.0014f, ch * 0.04f);
                    float barThickness = cellThickness * 0.5f;
                    float zBar = surfaceZ + side * (cellThickness + barThickness * 0.5f);
                    mb.AddBox(new Vector3(x, y - ch * 0.5f + barHeight * 0.7f, zBar), new Vector3(cw * 0.92f, barHeight, barThickness), "cell_interconnect");
                }
            }
        }

        // ================================================================== Reaction wheel
        // Local frame: mounting base at the origin, spin axis = local +Y.

        private static void BuildReactionWheel(PartSpec s, GeneratedPart part)
        {
            float r = s.radius;
            float wh = s.wheelHeight;
            const float baseThickness = 0.003f;

            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, baseThickness * 0.5f, 0f), new Vector3(2.4f * r, baseThickness, 2.4f * r), "anodized_black");

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.AddCylinder(new Vector3(sx * 1.05f * r, baseThickness, sz * 1.05f * r), 0.0012f, 0.0008f, 8, "steel");

            // Motor stator
            mb.AddCylinder(new Vector3(0f, baseThickness, 0f), 0.30f * r, wh * 0.6f, 20, "steel");

            float housingR = 1.12f * r;
            if (s.openHousing)
            {
                // Low open ring so the flywheel is visible (teaching cut-away)
                var ring = new List<Vector2>
                {
                    new Vector2(housingR, baseThickness),
                    new Vector2(housingR, baseThickness + wh * 0.45f),
                    new Vector2(housingR - 0.0012f, baseThickness + wh * 0.45f),
                    new Vector2(housingR - 0.0012f, baseThickness)
                };
                mb.AddLathe(ring, 40, "anodized_black", true, false);
            }
            else
            {
                mb.AddCylinder(new Vector3(0f, baseThickness, 0f), housingR, wh + 0.004f, 40, "anodized_black");
            }

            // Electrical connector on the side of the base
            mb.AddBox(new Vector3(1.2f * r, baseThickness + 0.003f, 0f), new Vector3(0.006f, 0.006f, 0.008f), "connector");
            Bake("Housing", part.Root.transform, mb, part);

            // Spinning flywheel: hub, web and heavy rim (revolved profile)
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(part.Root.transform, false);
            rotor.localPosition = new Vector3(0f, baseThickness + 0.0015f, 0f);

            var rb = new ProceduralMeshBuilder();
            var profile = new List<Vector2>
            {
                new Vector2(0.28f * r, 0f),
                new Vector2(r, 0f),
                new Vector2(r, wh * 0.85f),
                new Vector2(0.84f * r, wh * 0.85f),
                new Vector2(0.84f * r, wh * 0.35f),
                new Vector2(0.42f * r, wh * 0.35f),
                new Vector2(0.42f * r, wh * 0.70f),
                new Vector2(0.28f * r, wh * 0.70f)
            };
            rb.AddLathe(profile, 48, "flywheel", true, true);
            // White balance mark so the rotation is visible
            rb.AddBox(new Vector3(0.92f * r, wh * 0.85f + 0.0003f, 0f), new Vector3(0.12f * r, 0.0006f, 0.25f * r), "white_paint");
            Bake("Flywheel", rotor, rb, part);

            var spin = rotor.gameObject.AddComponent<RotorSpin>();
            spin.rpm = s.spinRpm;
            spin.axis = Vector3.up;
        }

        // ================================================================== Antennas
        // Local frame: mounting face at the origin, antenna points out along local +Z.

        private static void BuildAntenna(PartSpec s, GeneratedPart part)
        {
            string style = (s.style ?? "turnstile").Trim().ToLowerInvariant();
            switch (style)
            {
                case "patch": BuildPatchAntenna(s, part); break;
                case "omni": RfGenerators.BuildOmniAntenna(s, part); break;
                case "helix": RfGenerators.BuildHelixAntenna(s, part, false); break;
                case "helix_array": RfGenerators.BuildHelixAntenna(s, part, true); break;
                default: BuildTurnstileAntenna(s, part); break;
            }
        }

        private static void BuildTurnstileAntenna(PartSpec s, GeneratedPart part)
        {
            float plate = s.plateSize;
            const float plateThickness = 0.006f;
            int n = Mathf.Max(1, s.elements);

            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, 0f, plateThickness * 0.5f), new Vector3(plate, plate, plateThickness), "white_paint");
            mb.AddBox(new Vector3(0f, 0f, plateThickness + 0.0005f), new Vector3(plate * 0.7f, plate * 0.7f, 0.001f), "pcb_green");

            for (int i = 0; i < n; i++)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, i * 360f / n);
                Vector3 dir = q * Vector3.right;
                // Burn-wire release block at each element root
                mb.Push(dir * (plate * 0.40f) + Vector3.forward * (plateThickness + 0.002f), q);
                mb.AddBox(Vector3.zero, new Vector3(0.008f, 0.010f, 0.004f), "anodized_black");
                mb.Pop();
            }
            Bake("AntennaModule", part.Root.transform, mb, part);

            for (int i = 0; i < n; i++)
            {
                Quaternion q = Quaternion.Euler(0f, 0f, i * 360f / n);
                Vector3 dir = q * Vector3.right;

                var element = new GameObject("Element_" + i).transform;
                element.SetParent(part.Root.transform, false);
                element.localPosition = dir * (plate * 0.45f) + Vector3.forward * (plateThickness * 0.6f);
                element.localRotation = q;

                var eb = new ProceduralMeshBuilder();
                eb.AddBox(new Vector3(s.length * 0.5f, 0f, 0f), new Vector3(s.length, 0.004f, 0.0003f), "tape_spring");
                Bake("Tape", element, eb, part);

                part.Deployables.Add(new Deployable
                {
                    Transform = element,
                    Mode = DeployMode.Extend,
                    Axis = Vector3.right,
                    StowedScale = 0.05f
                });
            }
        }

        private static void BuildPatchAntenna(PartSpec s, GeneratedPart part)
        {
            float ps = s.patchSize;
            float ground = ps * 1.6f;

            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, 0f, 0.00075f), new Vector3(ground, ground, 0.0015f), "pcb_green");
            mb.AddBox(new Vector3(0f, 0f, 0.0015f + 0.002f), new Vector3(ps, ps, 0.004f), "ceramic");
            mb.AddBox(new Vector3(0f, 0f, 0.0056f), new Vector3(ps * 0.8f, ps * 0.8f, 0.0002f), "cell_interconnect");

            // Feed pin (cylinder along +Z: rotate local +Y onto +Z)
            mb.Push(new Vector3(ps * 0.15f, ps * 0.12f, 0.0057f), Quaternion.Euler(90f, 0f, 0f));
            mb.AddCylinder(Vector3.zero, 0.0012f, 0.0004f, 10, "copper");
            mb.Pop();

            // SMA connector on the back (cylinder along -Z)
            mb.Push(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
            mb.AddCylinder(Vector3.zero, 0.0025f, 0.008f, 12, "connector");
            mb.Pop();

            Bake("PatchAntenna", part.Root.transform, mb, part);
        }

        // ================================================================== Thruster
        // Local frame: mounting face at the origin, exhaust leaves along local +Z,
        // valve body sits behind the face (towards -Z, inside the satellite).

        private static void BuildThruster(PartSpec s, GeneratedPart part)
        {
            float rt = s.throatRadius;
            float re = Mathf.Max(s.nozzleExitRadius, rt * 1.5f);
            float length = s.nozzleLength;

            var mb = new ProceduralMeshBuilder();
            // Build along +Y, then rotate +Y onto +Z.
            mb.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));

            // Valve body and solenoid coil (behind the mounting face)
            mb.AddCylinder(new Vector3(0f, -0.014f, 0f), 0.0042f, 0.012f, 16, "steel");
            mb.AddBox(new Vector3(0.0052f, -0.009f, 0f), new Vector3(0.004f, 0.006f, 0.005f), "copper");

            // Propellant feed line stub (along -X)
            mb.Push(new Vector3(0f, -0.012f, 0f), Quaternion.Euler(0f, 0f, 90f));
            mb.AddCylinder(Vector3.zero, 0.0010f, 0.012f, 8, "steel");
            mb.Pop();

            // Mounting flange
            mb.AddCylinder(new Vector3(0f, -0.002f, 0f), 0.0068f, 0.002f, 24, "aluminium");

            // Chamber -> throat -> bell nozzle (revolved profile)
            const float chamberR = 0.0028f;
            const float throatY = 0.0062f;
            var profile = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(chamberR, 0f),
                new Vector2(chamberR, 0.0040f),
                new Vector2(rt, throatY)
            };
            const int bellSteps = 12;
            for (int k = 1; k <= bellSteps; k++)
            {
                float t = k / (float)bellSteps;
                float shape = 1f - (1f - t) * (1f - t); // fast expansion, then straightens: bell-like
                profile.Add(new Vector2(rt + (re - rt) * shape, throatY + length * t));
            }
            mb.AddLathe(profile, 28, "nozzle", true, false);

            // Exit lip
            float exitY = throatY + length;
            var lip = new List<Vector2>
            {
                new Vector2(re, exitY - 0.0008f),
                new Vector2(re + 0.0005f, exitY - 0.0008f),
                new Vector2(re + 0.0005f, exitY),
                new Vector2(re, exitY)
            };
            mb.AddLathe(lip, 28, "nozzle", true, false);

            mb.Pop();
            Bake("Thruster", part.Root.transform, mb, part);
        }

        // ================================================================== Magnetorquer rod
        // Local frame: rod along local +Y, centred on the origin (length = length, radius = radius).

        private static void BuildMagnetorquer(PartSpec s, GeneratedPart part)
        {
            float r = s.radius;
            float L = s.length;
            var mb = new ProceduralMeshBuilder();
            mb.AddCylinder(new Vector3(0f, -L * 0.5f, 0f), r * 0.55f, L, 12, "steel");                 // ferromagnetic core
            mb.AddCylinder(new Vector3(0f, -L * 0.42f, 0f), r, L * 0.84f, 16, "copper");               // copper winding
            mb.AddCylinder(new Vector3(0f, -L * 0.46f, 0f), r * 1.15f, L * 0.04f, 16, "anodized_black"); // end bobbins
            mb.AddCylinder(new Vector3(0f, L * 0.42f, 0f), r * 1.15f, L * 0.04f, 16, "anodized_black");
            Bake("Magnetorquer", part.Root.transform, mb, part);
        }

        // ================================================================== Propellant / pressurant tank
        // Local frame: tank axis along local +Y, centred on the origin. length = 0 gives a sphere,
        // length > 0 adds a cylindrical section of that length (capsule tank).

        private static void BuildTank(PartSpec s, GeneratedPart part)
        {
            float r = s.radius;
            float half = Mathf.Max(0f, s.length) * 0.5f;
            var profile = new List<Vector2>();
            const int steps = 10;
            for (int i = 0; i <= steps; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * i / steps;
                profile.Add(new Vector2(Mathf.Cos(a) * r, -half + Mathf.Sin(a) * r));
            }
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI * 0.5f * i / steps;
                profile.Add(new Vector2(Mathf.Cos(a) * r, half + Mathf.Sin(a) * r));
            }
            var mb = new ProceduralMeshBuilder();
            mb.AddLathe(profile, 32, string.IsNullOrEmpty(s.material) ? "titanium" : s.material, false, false);
            // Mounting band and outlet pipe
            mb.AddCylinder(new Vector3(0f, -r * 0.05f, 0f), r * 1.02f, r * 0.1f, 32, "aluminium", false, false);
            mb.AddCylinder(new Vector3(0f, -half - r - r * 0.25f, 0f), r * 0.06f, r * 0.27f, 8, "steel");
            Bake("Tank", part.Root.transform, mb, part);
        }

        // ================================================================== Generic box

        private static void BuildBox(PartSpec s, GeneratedPart part)
        {
            var mb = new ProceduralMeshBuilder();
            mb.AddBox(Vector3.zero, s.size, string.IsNullOrEmpty(s.material) ? "aluminium" : s.material);
            Bake("Box", part.Root.transform, mb, part);
        }

        private static void BuildPlaceholder(GeneratedPart part)
        {
            var mb = new ProceduralMeshBuilder();
            mb.AddBox(Vector3.zero, new Vector3(0.02f, 0.02f, 0.02f), "placeholder");
            Bake("Placeholder", part.Root.transform, mb, part);
        }

        // ================================================================== helpers

        internal static Transform NewChild(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        internal static GameObject Bake(string name, Transform parent, ProceduralMeshBuilder mb, GeneratedPart part)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mesh = new Mesh();
            mesh.name = name + "_ProcMesh";
            mesh.hideFlags = HideFlags.DontSave;
            mb.Build(mesh);
            part.Meshes.Add(mesh);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[mb.MaterialKeys.Count];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = PartMaterialLibrary.Get(mb.MaterialKeys[i]);
            renderer.sharedMaterials = materials;
            return go;
        }
    }
}
