using System.Collections.Generic;
using UnityEngine;

namespace Academy.Prototype
{
    /// <summary>
    /// Creates (once) and caches the runtime materials used by generated parts.
    /// Works with URP Lit, falling back to the Built-in Standard shader.
    /// </summary>
    public static class PartMaterialLibrary
    {
        private struct Def
        {
            public Color Color;
            public float Metallic;
            public float Smoothness;

            public Def(float r, float g, float b, float metallic, float smoothness)
            {
                Color = new Color(r, g, b, 1f);
                Metallic = metallic;
                Smoothness = smoothness;
            }
        }

        private static readonly Dictionary<string, Def> Definitions = new Dictionary<string, Def>
        {
            { "aluminium",         new Def(0.78f, 0.79f, 0.80f, 0.90f, 0.55f) },
            { "anodized_black",    new Def(0.07f, 0.07f, 0.08f, 0.50f, 0.40f) },
            { "white_paint",       new Def(0.90f, 0.90f, 0.88f, 0.00f, 0.30f) },
            { "solar_cell",        new Def(0.04f, 0.07f, 0.22f, 0.60f, 0.88f) },
            { "cell_interconnect", new Def(0.85f, 0.85f, 0.87f, 1.00f, 0.70f) },
            { "panel_substrate",   new Def(0.10f, 0.11f, 0.12f, 0.20f, 0.45f) },
            { "pcb_green",         new Def(0.05f, 0.30f, 0.13f, 0.00f, 0.55f) },
            { "gold_foil",         new Def(1.00f, 0.76f, 0.33f, 1.00f, 0.75f) },
            { "copper",            new Def(0.86f, 0.52f, 0.32f, 1.00f, 0.60f) },
            { "connector",         new Def(0.83f, 0.68f, 0.25f, 1.00f, 0.60f) },
            { "steel",             new Def(0.60f, 0.62f, 0.64f, 0.90f, 0.45f) },
            { "flywheel",          new Def(0.70f, 0.71f, 0.73f, 1.00f, 0.70f) },
            { "nozzle",            new Def(0.26f, 0.25f, 0.24f, 0.85f, 0.35f) },
            { "ceramic",           new Def(0.88f, 0.84f, 0.74f, 0.00f, 0.25f) },
            { "tape_spring",       new Def(0.74f, 0.72f, 0.64f, 0.80f, 0.50f) },
            { "osr_radiator",      new Def(0.86f, 0.88f, 0.90f, 1.00f, 0.95f) },
            { "black_mli",         new Def(0.05f, 0.05f, 0.06f, 0.40f, 0.55f) },
            { "silver_mli",        new Def(0.80f, 0.81f, 0.83f, 1.00f, 0.70f) },
            { "graphite",          new Def(0.14f, 0.14f, 0.15f, 0.30f, 0.45f) },
            { "reflector_white",   new Def(0.93f, 0.93f, 0.90f, 0.00f, 0.20f) },
            { "titanium",          new Def(0.55f, 0.55f, 0.58f, 0.90f, 0.50f) },
            { "retro_glass",       new Def(0.62f, 0.76f, 0.86f, 1.00f, 0.96f) },
            { "lens",              new Def(0.04f, 0.07f, 0.11f, 0.20f, 0.95f) },
            { "placeholder",       new Def(1.00f, 0.00f, 1.00f, 0.00f, 0.20f) },
        };

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static IEnumerable<string> Keys { get { return Definitions.Keys; } }

        public static Material Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = "aluminium";

            Material cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Def def;
            if (!Definitions.TryGetValue(key, out def))
            {
                Debug.LogWarning("[PartMaterialLibrary] Unknown material key '" + key + "', using placeholder.");
                def = Definitions["placeholder"];
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[PartMaterialLibrary] Neither URP Lit nor Standard shader found.");
                return null;
            }

            var material = new Material(shader);
            material.name = "Proc_" + key;
            material.hideFlags = HideFlags.HideAndDontSave;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", def.Color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", def.Color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", def.Metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", def.Smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", def.Smoothness);

            Cache[key] = material;
            return material;
        }
    }
}
