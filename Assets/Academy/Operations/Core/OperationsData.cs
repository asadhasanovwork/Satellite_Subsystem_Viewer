using System;
using System.Collections.Generic;

namespace Academy.Operations
{
    // Data read from StreamingAssets/Academy/Operations/*.json with Unity's JsonUtility.
    // All values are generic teaching numbers from public knowledge, not from any specific satellite program.

    [Serializable]
    public class SimParam
    {
        public string key;
        public float value;
    }

    [Serializable]
    public class ModelsFile
    {
        public List<TemplateModels> templates = new List<TemplateModels>();
    }

    /// <summary>Operations data for one Builder template (satellite type).</summary>
    [Serializable]
    public class TemplateModels
    {
        public string template;
        /// <summary>ISO UTC start time for free flight, e.g. "2027-03-20T06:00:00Z".</summary>
        public string startUtc = "2027-03-20T06:00:00Z";
        public OrbitSettings orbit = new OrbitSettings();
        public List<GroundStationDef> groundStations = new List<GroundStationDef>();
        public ThermalSettings thermal = new ThermalSettings();
        public AdcsSettings adcs = new AdcsSettings();
        /// <summary>Principal inertia of the reference design in Unity local axes (x, y, z), kg m^2.</summary>
        public float[] inertia = { 0.024f, 0.004f, 0.024f };
        /// <summary>Mass the inertia above belongs to; actual inertia scales with design mass.</summary>
        public float referenceMassKg = 2.5f;
        /// <summary>Constant disturbance torque, Unity local axes, N m.</summary>
        public float[] disturbance = { 0f, 0f, 0f };
        public float disturbanceNoise;
        public List<RuleDef> rules = new List<RuleDef>();
        public List<SlotModel> slots = new List<SlotModel>();
    }

    [Serializable]
    public class ThermalSettings
    {
        public float heatCapacityJPerK = 300f;
        public float conductanceWPerK = 0.15f;
        public float sinkTempC = -60f;
        public float sunHeatW = 6f;
        public float startTempC = 15f;
        public float minOperatingC = -10f;
        public float maxOperatingC = 45f;
    }

    [Serializable]
    public class AdcsSettings
    {
        /// <summary>Body axis (Unity local) that points at the Earth in NADIR mode.</summary>
        public float[] nadirAxis = { 0f, -1f, 0f };
        /// <summary>Body axis (Unity local) aligned with the secondary target in NADIR mode.</summary>
        public float[] secondaryAxis = { 0f, 0f, 1f };
        /// <summary>"velocity" or "orbit_normal".</summary>
        public string secondaryTarget = "velocity";
        /// <summary>Body axis (Unity local) pointed at the Sun in SUNSAFE mode; all zero = computed from the solar arrays.</summary>
        public float[] sunAxis = { 0f, 0f, 0f };
        public float naturalFrequency = 0.1f;   // rad/s
        public float damping = 0.9f;
        /// <summary>Largest slew rate the pointing modes command, deg/s.</summary>
        public float maxSlewDps = 1f;
        public float controlPeriod = 1f;        // s
        public bool autoSunSafeAfterDetumble = true;
        public float detumbleExitDps = 0.5f;
        /// <summary>Wheel momentum fraction that starts momentum dumping.</summary>
        public float dumpStart = 0.6f;
        public float dumpStop = 0.2f;
        /// <summary>Pointing tolerance for payload service, degrees.</summary>
        public float payloadToleranceDeg = 2f;
    }

    /// <summary>Maps one Builder slot to a simulation model, with per-variant parameters.</summary>
    [Serializable]
    public class SlotModel
    {
        public string slot;
        public string kind;
        public List<SimParam> parameters = new List<SimParam>();
        public List<VariantModel> variants = new List<VariantModel>();
    }

    [Serializable]
    public class VariantModel
    {
        public string id;
        public List<SimParam> parameters = new List<SimParam>();
    }

    /// <summary>Onboard limit rule: when a telemetry point stays beyond a limit, run commands.</summary>
    [Serializable]
    public class RuleDef
    {
        public string id;
        public string description;
        public string point;
        public string op = "<";
        public float value;
        public float seconds = 5f;
        public List<string> actions = new List<string>();
        public bool enabled = true;
    }

    public static class ParamUtil
    {
        public static float Get(List<SimParam> list, string key, float fallback)
        {
            if (list != null)
                foreach (SimParam p in list)
                    if (p != null && p.key == key)
                        return p.value;
            return fallback;
        }

        public static Vec3d Vec(float[] a, Vec3d fallback)
        {
            if (a == null || a.Length < 3)
                return fallback;
            return new Vec3d(a[0], a[1], a[2]);
        }
    }

    /// <summary>Merged parameters of a slot model and its selected variant.</summary>
    public sealed class ModelParams
    {
        private readonly List<SimParam> _variant;
        private readonly List<SimParam> _slot;
        private readonly Dictionary<string, float> _overrides = new Dictionary<string, float>();

        public ModelParams(SlotModel slot, string variantId)
        {
            _slot = slot != null ? slot.parameters : null;
            if (slot != null && slot.variants != null)
                foreach (VariantModel v in slot.variants)
                    if (v != null && v.id == variantId)
                        _variant = v.parameters;
        }

        public void Set(string key, float value) { _overrides[key] = value; }

        public float Get(string key, float fallback)
        {
            float o;
            if (_overrides.TryGetValue(key, out o))
                return o;
            return ParamUtil.Get(_variant, key, ParamUtil.Get(_slot, key, fallback));
        }
    }
}
