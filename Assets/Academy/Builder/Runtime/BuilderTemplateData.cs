using System;
using System.Collections.Generic;
using Academy.Prototype;

namespace Academy.Builder
{
    /// <summary>Index of available satellite templates (StreamingAssets/Academy/Builder/builder_index.json).</summary>
    [Serializable]
    public class BuilderIndex
    {
        public List<string> templates = new List<string>();
    }

    /// <summary>
    /// A satellite class the learner can build: its fixed orbit, the eight subsystems,
    /// and inside each subsystem the hardware slots to fill.
    /// </summary>
    [Serializable]
    public class BuilderTemplate
    {
        public string id;
        public string displayName;
        public string satelliteClass;      // e.g. "3U CubeSat"
        public string orbitClass;          // LEO / MEO / GEO (fixed by the satellite type)
        public string orbitSummary;
        public string description;
        public string designName = "My satellite";
        public float cameraDistance = 1f;
        public float explodeDistance = 0.1f;
        public List<BuilderSubsystem> subsystems = new List<BuilderSubsystem>();
    }

    [Serializable]
    public class BuilderSubsystem
    {
        public string id;
        public string name;
        public string description;
        public List<BuilderSlot> slots = new List<BuilderSlot>();
    }

    /// <summary>One hardware position in a subsystem (e.g. "Battery", "Reaction wheels").</summary>
    [Serializable]
    public class BuilderSlot
    {
        public string id;
        public string name;
        /// <summary>"mandatory", "conditional" or "optional".</summary>
        public string requirement = "mandatory";
        /// <summary>Why this hardware exists (shown to the learner).</summary>
        public string why;
        /// <summary>Typical quantity text, e.g. "2 (redundant)".</summary>
        public string quantity;
        /// <summary>For conditional slots: active when ANY condition is met.</summary>
        public List<BuilderCondition> conditions = new List<BuilderCondition>();
        /// <summary>Shown when a conditional slot is active, e.g. "Required because you chose deployable wings".</summary>
        public string conditionText;
        public List<BuilderVariant> variants = new List<BuilderVariant>();
    }

    [Serializable]
    public class BuilderCondition
    {
        public string slot;
        public List<string> variants = new List<string>();
    }

    /// <summary>A catalogue choice for a slot: its parts (with fixed mount points), budgets and sliders.</summary>
    [Serializable]
    public class BuilderVariant
    {
        public string id;
        public string name;
        public string description;
        /// <summary>Short key figures, e.g. "40 Wh · 4 cells".</summary>
        public string specs;
        public float massKg;
        /// <summary>Average power consumption (W).</summary>
        public float powerW;
        /// <summary>Average power generation (W), solar arrays only.</summary>
        public float powerGenW;
        public List<PartSpec> parts = new List<PartSpec>();
        public List<BuilderParameter> parameters = new List<BuilderParameter>();
    }

    /// <summary>
    /// A slider that edits numeric PartSpec fields of the variant's parts and adjusts its budgets
    /// linearly: delta = (value - defaultValue) * perUnit.
    /// </summary>
    [Serializable]
    public class BuilderParameter
    {
        public string id;
        public string label;
        public string unit;
        public float min;
        public float max = 1f;
        public float defaultValue;
        public bool wholeNumbers;
        /// <summary>PartSpec float/int field names to set to the slider value (e.g. "panelCount", "scale").</summary>
        public List<string> fields = new List<string>();
        /// <summary>Indices into the variant's parts; empty = all parts.</summary>
        public List<int> partIndices = new List<int>();
        /// <summary>Multiply the slider value by this before writing it to the fields (default 1).</summary>
        public float fieldMultiplier = 1f;
        public float massPerUnit;
        public float powerPerUnit;
        public float powerGenPerUnit;
        public string help;
    }
}
