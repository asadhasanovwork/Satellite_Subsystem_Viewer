using System.Collections.Generic;
using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>Generators for large spacecraft: panel-built bus and multi-panel deployable solar wings.</summary>
    public static class BusAndWingGenerators
    {
        // ================================================================== Bus
        // Local frame: centred box of size `size`. Each face gets its own surface treatment:
        // tiled optical solar reflectors (radiators), crinkled multi-layer insulation, or plain paint.

        public static void BuildBus(PartSpec s, GeneratedPart part)
        {
            Vector3 size = s.size;
            Vector3 h = size * 0.5f;
            var mb = new ProceduralMeshBuilder();

            AddFace(mb, new Vector3(h.x, 0f, 0f), Vector3.right, Vector3.forward, Vector3.up, size.z, size.y, s.facePX, 11);
            AddFace(mb, new Vector3(-h.x, 0f, 0f), Vector3.left, Vector3.forward, Vector3.up, size.z, size.y, s.faceNX, 12);
            AddFace(mb, new Vector3(0f, h.y, 0f), Vector3.up, Vector3.right, Vector3.forward, size.x, size.z, s.facePY, 13);
            AddFace(mb, new Vector3(0f, -h.y, 0f), Vector3.down, Vector3.right, Vector3.forward, size.x, size.z, s.faceNY, 14);
            AddFace(mb, new Vector3(0f, 0f, h.z), Vector3.forward, Vector3.right, Vector3.up, size.x, size.y, s.facePZ, 15);
            AddFace(mb, new Vector3(0f, 0f, -h.z), Vector3.back, Vector3.right, Vector3.up, size.x, size.y, s.faceNZ, 16);

            if (s.edgeBeams)
            {
                float e = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.015f;
                for (int a = -1; a <= 1; a += 2)
                {
                    for (int b = -1; b <= 1; b += 2)
                    {
                        mb.AddBox(new Vector3(0f, a * h.y, b * h.z), new Vector3(size.x + e, e, e), "aluminium");
                        mb.AddBox(new Vector3(a * h.x, 0f, b * h.z), new Vector3(e, size.y + e, e), "aluminium");
                        mb.AddBox(new Vector3(a * h.x, b * h.y, 0f), new Vector3(e, e, size.z + e), "aluminium");
                    }
                }
            }

            // Launch-vehicle adapter ring on the -Z (anti-Earth) face
            if (!s.adapterRing)
            {
                PartGenerators.Bake("Bus", part.Root.transform, mb, part);
                return;
            }
            float ringR = Mathf.Min(size.x, size.y) * 0.33f;
            float ringH = Mathf.Min(size.x, size.y) * 0.05f;
            var ring = new List<Vector2>
            {
                new Vector2(ringR, 0f),
                new Vector2(ringR, ringH),
                new Vector2(ringR * 0.93f, ringH),
                new Vector2(ringR * 0.93f, 0f)
            };
            // Lathe builds around +Y; rotate so the ring axis points along -Z, starting at the face
            mb.Push(new Vector3(0f, 0f, -h.z), Quaternion.Euler(-90f, 0f, 0f));
            mb.AddLathe(ring, 48, "aluminium", true, true);
            mb.Pop();

            PartGenerators.Bake("Bus", part.Root.transform, mb, part);
        }

        private static void AddFace(ProceduralMeshBuilder mb, Vector3 center, Vector3 normal, Vector3 u, Vector3 v,
                                    float sizeU, float sizeV, string material, int seed)
        {
            string key = string.IsNullOrEmpty(material) ? "white_paint" : material;

            if (key == "osr_radiator")
            {
                // Dark substrate with a grid of mirror tiles standing slightly proud of it
                mb.AddQuadFlat(center - u * sizeU * 0.5f - v * sizeV * 0.5f,
                               center + u * sizeU * 0.5f - v * sizeV * 0.5f,
                               center + u * sizeU * 0.5f + v * sizeV * 0.5f,
                               center - u * sizeU * 0.5f + v * sizeV * 0.5f, normal, "panel_substrate");

                float tile = 0.15f;
                int nu = Mathf.Max(1, Mathf.RoundToInt(sizeU / tile));
                int nv = Mathf.Max(1, Mathf.RoundToInt(sizeV / tile));
                float tu = sizeU / nu, tv = sizeV / nv;
                Vector3 lift = normal * 0.002f;
                for (int i = 0; i < nu; i++)
                {
                    for (int j = 0; j < nv; j++)
                    {
                        Vector3 c = center + lift + u * (-sizeU * 0.5f + tu * (i + 0.5f)) + v * (-sizeV * 0.5f + tv * (j + 0.5f));
                        float hu = tu * 0.47f, hv = tv * 0.47f;
                        mb.AddQuadFlat(c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv, normal, "osr_radiator");
                    }
                }
                return;
            }

            if (key.Contains("mli") || key == "gold_foil")
            {
                mb.AddCrinkledFace(center, u, v, normal, sizeU, sizeV, 0.22f, 0.09f, key, seed);
                return;
            }

            mb.AddQuadFlat(center - u * sizeU * 0.5f - v * sizeV * 0.5f,
                           center + u * sizeU * 0.5f - v * sizeV * 0.5f,
                           center + u * sizeU * 0.5f + v * sizeV * 0.5f,
                           center - u * sizeU * 0.5f + v * sizeV * 0.5f, normal, key);
        }

        // ================================================================== Radiator panel
        // Local frame: centred panel, width along X, height along Y, mirror tiles on +Z, substrate behind.

        public static void BuildRadiatorPanel(PartSpec s, GeneratedPart part)
        {
            float t = Mathf.Max(0.005f, s.thickness);
            var mb = new ProceduralMeshBuilder();
            mb.AddBox(new Vector3(0f, 0f, -t * 0.5f), new Vector3(s.width, s.height, t), "panel_substrate");
            AddFace(mb, Vector3.zero, Vector3.forward, Vector3.right, Vector3.up, s.width, s.height, "osr_radiator", 21);
            PartGenerators.Bake("RadiatorPanel", part.Root.transform, mb, part);
        }

        // ================================================================== Adapter ring / central thrust tube
        // Local frame: cylinder shell along local +Y from 0 to length, radius = radius.

        public static void BuildAdapterRing(PartSpec s, GeneratedPart part)
        {
            float r = s.radius;
            float h = s.length;
            var mb = new ProceduralMeshBuilder();
            mb.AddLathe(new List<Vector2>
            {
                new Vector2(r, 0f), new Vector2(r, h), new Vector2(r * 0.96f, h), new Vector2(r * 0.96f, 0f)
            }, 48, "aluminium", true, true);
            mb.AddLathe(new List<Vector2>
            {
                new Vector2(r, 0f), new Vector2(r * 1.06f, 0f), new Vector2(r * 1.06f, h * 0.04f + 0.004f), new Vector2(r, h * 0.04f + 0.004f)
            }, 48, "anodized_black", true, true);
            PartGenerators.Bake("Ring", part.Root.transform, mb, part);
        }

        // ================================================================== Solar array drive (standalone)

        public static void BuildSada(PartSpec s, GeneratedPart part)
        {
            PartGenerators.Bake("SADA", part.Root.transform, SadaMesh(s.sadaLength, s.sadaRadius), part);
        }

        private static ProceduralMeshBuilder SadaMesh(float length, float radius)
        {
            var sada = new ProceduralMeshBuilder();
            sada.AddCylinder(Vector3.zero, radius * 1.5f, length * 0.12f, 28, "anodized_black");
            sada.AddCylinder(new Vector3(0f, length * 0.12f, 0f), radius, length * 0.88f, 28, "aluminium");
            return sada;
        }

        // ================================================================== Solar wing
        // Local frame: mounted on the bus face at the origin, wing axis = local +Y (outward),
        // cells face local +Z when deployed, panel width along local X.
        //
        // Kinematic chain:  SADA -> yoke hinge -> panel 1 -> hinge -> panel 2 -> hinge -> ...
        // Stowed: the yoke is folded by yokeStowedAngle (-90 = along local -Z, flat against the bus)
        // and the panels are folded accordion-style (+/-180 deg) into a stack, cells face to face.
        // Deployed: everything at 0 deg, all panels in one plane.

        public static void BuildSolarWing(PartSpec s, GeneratedPart part)
        {
            float panelW = s.width;
            float panelL = s.height;
            float t = Mathf.Max(0.005f, s.thickness);
            float gap = Mathf.Max(t * 1.8f, 0.03f);
            int panels = Mathf.Max(1, s.panelCount);
            Transform root = part.Root.transform;

            // Solar Array Drive Assembly (rotates the wing to track the Sun on a real satellite)
            if (s.drawSada)
                PartGenerators.Bake("SADA", root, SadaMesh(s.sadaLength, s.sadaRadius), part);

            // Yoke: two struts in a V from the drive shaft to the first panel, plus a cross bar
            Transform yoke = PartGenerators.NewChild("YokeHinge", root, new Vector3(0f, s.sadaLength, 0f));
            part.Deployables.Add(new Deployable
            {
                Transform = yoke,
                Mode = DeployMode.Rotate,
                Axis = Vector3.right,
                StowedAngle = s.yokeStowedAngle,
                TargetAngle = 0f
            });

            float strut = Mathf.Max(0.012f, panelW * 0.012f);
            var yb = new ProceduralMeshBuilder();
            yb.AddBox(Vector3.zero, new Vector3(s.sadaRadius * 1.6f, strut * 3f, strut * 3f), "anodized_black");
            yb.AddCylinderBetween(Vector3.zero, new Vector3(panelW * 0.35f, s.yokeLength, 0f), strut, 10, "graphite");
            yb.AddCylinderBetween(Vector3.zero, new Vector3(-panelW * 0.35f, s.yokeLength, 0f), strut, 10, "graphite");
            yb.AddCylinderBetween(new Vector3(-panelW * 0.4f, s.yokeLength, 0f), new Vector3(panelW * 0.4f, s.yokeLength, 0f), strut, 10, "graphite");
            PartGenerators.Bake("Yoke", yoke, yb, part);

            // Panels
            Transform previous = yoke;
            float previousEnd = s.yokeLength;
            for (int i = 0; i < panels; i++)
            {
                Transform body;
                if (i == 0)
                {
                    body = PartGenerators.NewChild("Panel_1", previous, new Vector3(0f, previousEnd, 0f));
                }
                else
                {
                    // Alternate hinge side so the folded stack grows outward: panel i lands one gap further out
                    float side = (i % 2 == 1) ? 1f : -1f;
                    Transform hinge = PartGenerators.NewChild("PanelHinge_" + i, previous, new Vector3(0f, previousEnd, side * gap * 0.5f));
                    part.Deployables.Add(new Deployable
                    {
                        Transform = hinge,
                        Mode = DeployMode.Rotate,
                        Axis = Vector3.right,
                        StowedAngle = side * 180f,
                        TargetAngle = 0f
                    });
                    body = PartGenerators.NewChild("Panel_" + (i + 1), hinge, new Vector3(0f, 0f, -side * gap * 0.5f));
                }

                var pb = new ProceduralMeshBuilder();
                pb.AddBox(new Vector3(0f, panelL * 0.5f, -t * 0.5f), new Vector3(panelW, panelL, t), "panel_substrate");

                // Aluminium edge frame
                float fe = Mathf.Max(0.006f, panelW * 0.008f);
                pb.AddBox(new Vector3(-panelW * 0.5f + fe * 0.5f, panelL * 0.5f, -t * 0.5f), new Vector3(fe, panelL, t * 1.15f), "aluminium");
                pb.AddBox(new Vector3(panelW * 0.5f - fe * 0.5f, panelL * 0.5f, -t * 0.5f), new Vector3(fe, panelL, t * 1.15f), "aluminium");
                pb.AddBox(new Vector3(0f, fe * 0.5f, -t * 0.5f), new Vector3(panelW, fe, t * 1.15f), "aluminium");
                pb.AddBox(new Vector3(0f, panelL - fe * 0.5f, -t * 0.5f), new Vector3(panelW, fe, t * 1.15f), "aluminium");

                PartGenerators.AddSolarCells(pb, s, panelW, panelL, 0f, 1f, panelW * 0.035f, panelW * 0.006f);

                // Hinge knuckles at the outer edge (except the last panel)
                if (i < panels - 1)
                {
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float x = k * panelW * 0.3f;
                        pb.AddCylinderBetween(new Vector3(x - panelW * 0.04f, panelL, 0f), new Vector3(x + panelW * 0.04f, panelL, 0f),
                                              strut * 1.2f, 10, "steel");
                    }
                }

                PartGenerators.Bake("PanelMesh", body, pb, part);
                previous = body;
                previousEnd = panelL;
            }
        }
    }
}
