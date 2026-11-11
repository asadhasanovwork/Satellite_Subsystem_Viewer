using System;
using System.Collections.Generic;
using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>A whole satellite: a list of parts placed in the satellite's body frame (metres).</summary>
    [Serializable]
    public class SatelliteAssemblyData
    {
        public string name = "Unnamed satellite";
        /// <summary>LEO, MEO or GEO.</summary>
        public string orbitClass = "LEO";
        /// <summary>One line shown to the learner, e.g. "Navigation satellite, ~23,000 km".</summary>
        public string orbitSummary;
        public string description;
        /// <summary>Initial camera distance in metres (scales the view to the satellite's size).</summary>
        public float cameraDistance = 0.9f;
        /// <summary>How far parts move outward at explode = 1 (metres).</summary>
        public float explodeDistance = 0.12f;
        public List<PartSpec> parts = new List<PartSpec>();
    }

    /// <summary>
    /// One part instance. Only the fields relevant to its <see cref="type"/> are used.
    /// Orientation: <see cref="forward"/> is the part's local +Z (its "outward" direction),
    /// <see cref="up"/> is its local +Y.
    /// Types: cubesat_frame, bus, solar_panel, solar_wing, reaction_wheel, antenna, reflector,
    /// feed_horn, thruster, sensor, retroreflector, box.
    /// </summary>
    [Serializable]
    public class PartSpec
    {
        // Identity
        public string id;
        public string type;
        public string label;
        public string description;

        // Placement (body frame, metres)
        public Vector3 position;
        public Vector3 forward = new Vector3(0f, 0f, 1f);
        public Vector3 up = new Vector3(0f, 1f, 0f);
        /// <summary>Uniform scale of the generated part (e.g. a CubeSat thruster scaled up to a GEO engine).</summary>
        public float scale = 1f;

        // Deployment timing within the whole satellite's deployment (0..1), so parts can deploy in sequence
        public float deployStart = 0f;
        public float deployEnd = 1f;

        // cubesat_frame
        public int units = 3;
        public float unitSize = 0.1f;
        public float unitHeight = 0.1135f;
        public float railWidth = 0.0085f;
        public bool pcbStack = true;

        // solar_panel / solar_wing (width = panel width, height = panel length)
        public float width = 0.082f;
        public float height = 0.32f;
        public float thickness = 0.0016f;
        public int cellRows = 7;
        public int cellColumns = 2;
        public bool cellsBothSides;
        public bool deployable;
        /// <summary>Deployed hinge angle (degrees).</summary>
        public float deployAngle = 90f;
        /// <summary>Stowed hinge angle (degrees). Used by reflectors.</summary>
        public float stowedAngle = 0f;

        // solar_wing
        public int panelCount = 4;
        public float yokeLength = 0.4f;
        public float yokeStowedAngle = -90f;
        public float sadaLength = 0.25f;
        public float sadaRadius = 0.09f;

        // reaction_wheel
        public float radius = 0.017f;
        public float wheelHeight = 0.012f;
        public float spinRpm = 90f;
        public bool openHousing = true;

        // antenna  (style: "turnstile" | "patch" | "omni" | "helix" | "helix_array")
        // sensor   (style: "earth_sensor" | "star_tracker" | "sun_sensor")
        public string style = "turnstile";
        public float plateSize = 0.09f;
        public int elements = 4;
        public float length = 0.17f;
        public float patchSize = 0.026f;
        public float arrayRadius = 0.3f;
        public float helixRadius = 0.04f;
        public float helixTurns = 6f;

        // reflector (offset paraboloid: vertex at vertexOffset in the hinge frame, focus at vertexOffset + (0,0,focalLength))
        public float diameter = 2.5f;
        public float focalLength = 1.5f;
        public float offsetHeight = 1.9f;
        public Vector3 vertexOffset;

        // thruster
        public float throatRadius = 0.0012f;
        public float nozzleExitRadius = 0.0055f;
        public float nozzleLength = 0.012f;

        // bus face materials (+X, -X, +Y, -Y, +Z, -Z). Special: "osr_radiator" (tiled mirrors), "*_mli"/"gold_foil" (crinkled)
        public string facePX = "gold_foil";
        public string faceNX = "gold_foil";
        public string facePY = "gold_foil";
        public string faceNY = "gold_foil";
        public string facePZ = "white_paint";
        public string faceNZ = "gold_foil";
        public bool edgeBeams = true;
        /// <summary>Bus: draw the launch-adapter ring on the -Z face (off when the ring is its own part).</summary>
        public bool adapterRing = true;
        /// <summary>Solar wing: draw the drive (SADA) at the root (off when the SADA is its own part).</summary>
        public bool drawSada = true;

        // box / bus / sensor body size
        public Vector3 size = new Vector3(0.08f, 0.03f, 0.08f);
        public string material = "aluminium";
    }
}
