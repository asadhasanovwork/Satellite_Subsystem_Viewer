using System;
using System.Collections.Generic;
using System.IO;
using Academy.Builder;
using UnityEngine;

namespace Academy.Operations
{
    /// <summary>What the Operations scene should fly when it opens (set by the Builder or the main menu).</summary>
    public static class OperationsSession
    {
        /// <summary>A design from the Builder; null = show the scenario list.</summary>
        public static DesignSave PendingDesign;
        /// <summary>Scenario id to start directly (optional).</summary>
        public static string PendingScenario;

        public static void FlyDesign(DesignSave design)
        {
            PendingDesign = design;
            PendingScenario = null;
        }

        public static void Clear()
        {
            PendingDesign = null;
            PendingScenario = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Clear(); }
    }

    /// <summary>Reads the Builder templates, operations models and scenarios from StreamingAssets.</summary>
    public static class OperationsData
    {
        public const string BuilderIndex = "Academy/Builder/builder_index.json";
        public const string ModelsFile = "Academy/Operations/operations_models.json";
        public const string ScenarioFolder = "Academy/Operations/Scenarios";

        private static string Read(string relative)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relative);
            if (!File.Exists(path))
            {
                Debug.LogError("[Operations] Missing data file: " + path);
                return null;
            }
            return File.ReadAllText(path);
        }

        public static List<BuilderTemplate> LoadTemplates()
        {
            var list = new List<BuilderTemplate>();
            string json = Read(BuilderIndex);
            if (json == null) return list;
            BuilderIndex index = JsonUtility.FromJson<BuilderIndex>(json);
            string folder = Path.GetDirectoryName(BuilderIndex);
            foreach (string file in index.templates)
            {
                string t = Read(Path.Combine(folder, file));
                if (t == null) continue;
                try
                {
                    BuilderTemplate template = JsonUtility.FromJson<BuilderTemplate>(t);
                    if (template != null) list.Add(template);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Operations] Could not parse " + file + ": " + e.Message);
                }
            }
            return list;
        }

        public static ModelsFile LoadModels()
        {
            string json = Read(ModelsFile);
            return json != null ? JsonUtility.FromJson<ModelsFile>(json) : new ModelsFile();
        }

        public static List<ScenarioDef> LoadScenarios()
        {
            var list = new List<ScenarioDef>();
            string json = Read(Path.Combine(ScenarioFolder, "scenario_index.json"));
            if (json == null) return list;
            ScenarioIndex index = JsonUtility.FromJson<ScenarioIndex>(json);
            foreach (string file in index.scenarios)
            {
                string s = Read(Path.Combine(ScenarioFolder, file));
                if (s == null) continue;
                try
                {
                    ScenarioDef def = JsonUtility.FromJson<ScenarioDef>(s);
                    if (def != null) list.Add(def);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Operations] Could not parse scenario " + file + ": " + e.Message);
                }
            }
            return list;
        }
    }
}
